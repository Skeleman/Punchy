using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Keeps the land around the players populated with the animals each biome lists in its Fauna: spawns groups out
    /// of sight-range (between Min Spawn Distance and Radius) wherever a species is below its biome's density, and
    /// removes animals that end up far behind everyone. Landmarks' residents appear at their landmark when a player
    /// comes within Radius of it. Builds each species' runtime data and the shared capsule mesh, and the player's body.
    ///
    /// With netcode the animals live in the server world (when this machine hosts) as creature ghosts; every client
    /// world, the host's included, gives the ghosts it receives their bodies (see CreatureGhosts.cs). The species table
    /// is sorted by name so its indices mean the same animal on every machine. Without netcode everything happens in
    /// the one local world, as before.
    /// </summary>
    public class FaunaDirector : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Tells animals whether it's day or night (when they sleep).")]
        public Punchy.Weather.DayNightCycle Cycle;
        [Tooltip("The player's body: any species (a human, a wolf…). Empty = the player's own placeholder mesh.")]
        public CreatureSpecies PlayerSpecies;
        [Tooltip("Animals are kept within this distance of each player.")]
        public float Radius = 120f;
        [Tooltip("New animals appear at least this far away.")]
        public float MinSpawnDistance = 40f;
        [Tooltip("Animals further than this from every player are removed.")]
        public float DespawnDistance = 170f;
        [Tooltip("At most this many wild animals (the ones that come and go round the players).")]
        [UnityEngine.Serialization.FormerlySerializedAs("MaxCreatures")]
        public int MaxWild = 300;
        [Tooltip("At most this many settled ones (settlements' residents and their domestic animals), counted apart so " +
                 "a big town never stops the wild ones appearing.")]
        public int MaxSettled = 400;
        [Tooltip("At most this many cave dwellers.")]
        public int MaxCave = 120;
        [Tooltip("Spawn attempts per second (per player).")]
        public float SpawnRate = 6f;

        readonly List<CreatureSpecies> species = new List<CreatureSpecies>();
        /// <summary>Settlements' dogs, cats, chickens and pigeons (from Resources).</summary>
        DomesticTable domestics;
        readonly List<BlobAssetReference<CreatureBlob>> blobs = new List<BlobAssetReference<CreatureBlob>>();
        BiomeDefinition[] biomes;
        LandmarkType[] landmarks;
        /// <summary>Landmarks whose residents are out; forgotten once every player is far enough away for them to be gone.</summary>
        readonly HashSet<int> occupied = new HashSet<int>();
        /// <summary>Caves whose dwellers are out (like landmarks' residents).</summary>
        readonly HashSet<int> occupiedCaves = new HashSet<int>();
        readonly List<int> scratch = new List<int>();
        readonly List<float3> players = new List<float3>();
        float nextResidentCheck;

        // The client (or local) world shows things: render settings, the player's body, ghosts' bodies. The simulation
        // world runs the animals: the server world when hosting, the local world without netcode, none on a client
        // that joined someone else.
        World queryWorld, simWorld;
        EntityQuery terrainQuery, paletteQuery, playerQuery, avatarQuery, replicaQuery, newReplicaQuery;
        EntityQuery wildQuery, simCreatureQuery, netPlayerQuery, simPlayerQuery, ghostSpawnerQuery;
        CreatureSpecies avatarSpecies;
        BlobAssetReference<CreatureBlob> avatarBlob;
        Entity settingsEntity, simClockEntity, floraEntity;
        BlobAssetReference<CarryBlob> carryBlob;
        /// <summary>Where trees are, for climbing, perching and webs (the same tables the plants are drawn from).</summary>
        BlobAssetReference<Punchy.Flora.FloraBlob> floraBlob;
        readonly List<Punchy.Flora.FloraSpecies> floraSpecies = new List<Punchy.Flora.FloraSpecies>();
        /// <summary>The plant placement animals use (where a machine simulates them), and its species in rule order (tests).</summary>
        public BlobAssetReference<Punchy.Flora.FloraBlob> FloraPlacementBlob => floraBlob;
        public IReadOnlyList<Punchy.Flora.FloraSpecies> FloraSpeciesList => floraSpecies;
        Mesh capsule;
        float spawnBudget;
        bool built;
        TerrainSource lastSource;
        /// <summary>Per player, how many of each species are within Radius of them (player-major).</summary>
        int[] counts;
        /// <summary>How many there are of each kind (wild, settled, cave), as last counted (Despawn) plus those since.</summary>
        int wildCount, settledCount, caveCount;
        /// <summary>Species index by asset name (SpeciesIndex: no string or closure per look-up).</summary>
        readonly Dictionary<string, int> speciesByName = new Dictionary<string, int>();
        long configStamp;
        float nextConfigCheck;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x9E3779B9u);

        void OnDisable()
        {
            Teardown();
            DropAvatar();
        }

        void Teardown()
        {
            if (simWorld != null && simWorld.IsCreated)
            {
                var em = simWorld.EntityManager;
                em.DestroyEntity(simCreatureQuery);
                if (simClockEntity != Entity.Null && em.Exists(simClockEntity))
                    em.DestroyEntity(simClockEntity);
                if (floraEntity != Entity.Null && em.Exists(floraEntity))
                    em.DestroyEntity(floraEntity);
            }
            if (floraBlob.IsCreated)
                floraBlob.Dispose();
            floraBlob = default;
            if (carryBlob.IsCreated)
                carryBlob.Dispose();
            carryBlob = default;
            if (queryWorld != null && queryWorld.IsCreated)
            {
                var em = queryWorld.EntityManager;
                // Ghosts belong to the network: just take their bodies away (they get new ones after the rebuild).
                if (queryWorld != simWorld)
                {
                    em.RemoveComponent<CreatureBody>(replicaQuery);
                    em.RemoveComponent<CreatureReplica>(replicaQuery);
                }
                if (settingsEntity != Entity.Null && em.Exists(settingsEntity))
                    em.DestroyEntity(settingsEntity);
            }
            settingsEntity = simClockEntity = floraEntity = Entity.Null;
            foreach (var b in blobs)
                if (b.IsCreated) b.Dispose();
            blobs.Clear();
            species.Clear();
            speciesByName.Clear();
            occupied.Clear();
            occupiedCaves.Clear();
            Caught = 0;
            if (capsule != null) Destroy(capsule);
            capsule = null;
            built = false;
        }

        void Update()
        {
            if (Material == null || !TryGetWorld(out var source, out var palette))
                return;
            // Rebuild when the terrain is re-baked or (in the editor) a species or limb type is edited.
            if (built && (!source.Equals(lastSource) || ConfigChanged()))
                Teardown();
            if (!built)
                Build(source, palette);
            KeepAvatar();
            GiveReplicasBodies();

            var clock = new FaunaClock { Night = Cycle != null ? Cycle.Night : 0f };
            queryWorld.EntityManager.SetComponentData(settingsEntity, clock);
            if (simWorld == null)
                return;
            if (simWorld != queryWorld)
                simWorld.EntityManager.SetComponentData(simClockEntity, clock);

            GatherPlayers();
            Despawn();
            if (players.Count == 0)
                return;
            if (Time.time >= nextResidentCheck)
            {
                nextResidentCheck = Time.time + 0.5f;
                SpawnResidents(source);
                SpawnCaveDwellers(source);
            }
            spawnBudget = math.min(spawnBudget + SpawnRate * players.Count * Time.deltaTime, 3f * players.Count);
            while (spawnBudget >= 1f)
            {
                spawnBudget -= 1f;
                TrySpawn(source, rng.NextInt(players.Count));
            }
        }

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            // Without netcode the local world is also where animals live; with it, the server world if we host.
            var sim = world.IsClient() ? NetWorlds.Server : world;
            if (queryWorld != world || simWorld != sim || simWorld != null && !simWorld.IsCreated)
            {
                if (queryWorld != null && queryWorld.IsCreated)
                    Teardown();
                DropAvatar();
                var em = world.EntityManager;
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                avatarQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>());
                replicaQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureReplica>());
                newReplicaQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<CreatureNet>(), ComponentType.ReadOnly<LocalTransform>() },
                    None = new[] { ComponentType.ReadOnly<CreatureReplica>() }
                });
                queryWorld = world;
                simWorld = sim;
                if (sim != null)
                {
                    var sem = sim.EntityManager;
                    // Animals: everything with a body except players' (ours, and other players' stand-ins).
                    var wild = new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>() },
                        None = new[] { ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureReplica>() }
                    };
                    wildQuery = sem.CreateEntityQuery(wild);
                    simCreatureQuery = sem.CreateEntityQuery(wild);
                    netPlayerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
                    simPlayerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                    ghostSpawnerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<CreatureGhostSpawner>());
                }
                built = false;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        void Build(in TerrainSource source, BiomePalette palette)
        {
            // Each species once, by name: in a built player the Domestics table (loaded from Resources) holds its own
            // copies of species the world's palette also lists, so the same species can arrive as two objects. (Counted
            // twice, the indices shifted, and an editor and a build disagreed on what each index meant.)
            var named = new HashSet<string>();
            void AddSpecies(CreatureSpecies s)
            {
                if (s != null && named.Add(s.name))
                    species.Add(s);
            }
            biomes = palette.Biomes;
            foreach (var biome in biomes)
                if (biome != null)
                    foreach (var f in biome.Fauna)
                        AddSpecies(f.Species);
            landmarks = palette.Landmarks ?? System.Array.Empty<LandmarkType>();
            foreach (var landmark in landmarks)
                if (landmark != null)
                    foreach (var r in landmark.Residents)
                        AddSpecies(r.Species);
            domestics = Resources.Load<DomesticTable>("Domestics");
            Debug.Log($"FaunaDirector: domestic animals table {(domestics != null ? domestics.Entries.Count + " entries" : "missing")}");
            if (domestics != null)
                foreach (var d in domestics.Entries)
                    AddSpecies(d.Species);
            // The index is how the network names a species, so it must mean the same animal on every machine: sort by
            // asset name, and refuse to network if two share one.
            species.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 1; i < species.Count; i++)
                if (species[i].name == species[i - 1].name)
                    Debug.LogError($"FaunaDirector: two species are both named '{species[i].name}'; creature indices would be ambiguous over the network.");
            speciesByName.Clear();
            for (int i = 0; i < species.Count; i++)
                speciesByName[species[i].name] = i;
            // Who hunts whom: a listed species covers its variants too.
            var hunted = new bool[species.Count];
            var preyLists = new List<int>[species.Count];
            for (int i = 0; i < species.Count; i++)
            {
                preyLists[i] = new List<int>();
                for (int j = 0; j < species.Count; j++)
                    if (j != i && (species[i].Hunts.Contains(species[j]) || species[i].Hunts.Contains(species[j].Body)))
                    {
                        preyLists[i].Add(j);
                        hunted[j] = true;
                    }
            }
            for (int i = 0; i < species.Count; i++)
                blobs.Add(CreatureBlobBuilder.Build(species[i], preyLists[i], hunted[i]));

            capsule = BuildCapsule(12, 6, 4);
            var em = queryWorld.EntityManager;
            settingsEntity = em.CreateEntity();
            em.SetName(settingsEntity, "Fauna Render Settings");
            em.AddComponentObject(settingsEntity, new FaunaRenderSettings { Material = Material, Capsule = capsule });
            em.AddComponentData(settingsEntity, new FaunaClock());
            carryBlob = BuildCarryTable();
            em.AddComponentData(settingsEntity, new CarryTable { Blob = carryBlob });
            if (simWorld != null && simWorld != queryWorld)
            {
                simClockEntity = simWorld.EntityManager.CreateEntity(typeof(FaunaClock));
                simWorld.EntityManager.SetName(simClockEntity, "Fauna Clock");
                simWorld.EntityManager.AddComponentData(simClockEntity, new CarryTable { Blob = carryBlob });
            }
            if (simWorld != null)
            {
                // The tree grid must match FloraSystem's (its tree layer's cell size), so animals find the trees drawn.
                var flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
                var cells = flora != null
                    ? new float3(Punchy.Flora.FloraPlacement.CellSize(flora.Ground.TileSize, flora.Ground.CellSize),
                                 Punchy.Flora.FloraPlacement.CellSize(flora.Shrubs.TileSize, flora.Shrubs.CellSize),
                                 Punchy.Flora.FloraPlacement.CellSize(flora.Trees.TileSize, flora.Trees.CellSize))
                    : new float3(0.7f, 2f, 4.5f);
                floraBlob = Punchy.Flora.FloraPlacement.Build(palette, cells, floraSpecies, obstacles: true);
                floraEntity = simWorld.EntityManager.CreateEntity();
                simWorld.EntityManager.SetName(floraEntity, "Flora Lookup");
                simWorld.EntityManager.AddComponentData(floraEntity, new Punchy.Flora.FloraLookup { Blob = floraBlob });
            }
            lastSource = source;
            configStamp = ConfigStamp();
            built = true;
        }

        /// <summary>
        /// Who can carry and where a carried animal sits, per playable body (players are named by their index in
        /// NetAvatars.PlayableSpecies): folk carry, at the height and reach of a human's arms (0.98 m up, 0.38 m out)
        /// scaled by how high their arms are next to a human's, so a goblin holds it lower and an ogre higher. Taken
        /// from the species, so every machine puts it in the same place.
        /// </summary>
        BlobAssetReference<CarryBlob> BuildCarryTable()
        {
            var avatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
            var playable = avatars != null ? avatars.PlayableSpecies : new List<CreatureSpecies>();
            var stand = new float[playable.Count];
            var chest = new float[playable.Count];
            var upright = new bool[playable.Count];
            var reference = 0.9f;
            for (int i = 0; i < playable.Count; i++)
            {
                if (playable[i] == null)
                    continue;
                // (How high its arms are: legs plus half the trunk, an upright body's length being its height.)
                var body = CreatureBlobBuilder.Build(playable[i]);
                var size = (playable[i].Size.x + playable[i].Size.y) * 0.5f;
                stand[i] = (body.Value.StandHeight + body.Value.BodyLength * 0.5f) * size;
                chest[i] = math.max(body.Value.FrontRadius, body.Value.RearRadius) * body.Value.BodyFlatten * size;
                upright[i] = body.Value.Upright;
                body.Dispose();
                if (playable[i].name == "Human")
                    reference = math.max(stand[i], 0.1f);
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CarryBlob>();
            var folk = builder.Allocate(ref root.Folk, playable.Count);
            var hold = builder.Allocate(ref root.Hold, playable.Count);
            for (int i = 0; i < playable.Count; i++)
            {
                folk[i] = (byte)(upright[i] ? 1 : 0);
                // (Out by the chest's own depth and a forearm's thickness, so a stout body holds it further out.)
                hold[i] = new float2(0.98f * (stand[i] > 0f ? stand[i] / reference : 1f), chest[i] + 0.1f);
            }
            var blob = builder.CreateBlobAssetReference<CarryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        /// <summary>
        /// Changes whenever any species, limb type or prey list in use is edited (editor only): the sum of their
        /// dirty counters, which Unity bumps on every inspector edit. Cheap enough to check every second.
        /// </summary>
        long ConfigStamp()
        {
            long stamp = 0;
#if UNITY_EDITOR
            foreach (var variant in species)
            {
                stamp += UnityEditor.EditorUtility.GetDirtyCount(variant);
                var s = variant.Body;
                if (s != variant)
                    stamp += UnityEditor.EditorUtility.GetDirtyCount(s);
                foreach (var slot in s.Parts)
                    if (slot.Limb != null)
                        stamp += UnityEditor.EditorUtility.GetDirtyCount(slot.Limb);
            }
#endif
            return stamp;
        }

        bool ConfigChanged()
        {
            if (!Application.isEditor || Time.unscaledTime < nextConfigCheck)
                return false;
            nextConfigCheck = Time.unscaledTime + 1f;
            return ConfigStamp() != configStamp;
        }

        /// <summary>A species' index (by name: a build can hold more than one copy of the same species asset).</summary>
        public int SpeciesIndex(CreatureSpecies s) => s != null ? SpeciesIndex(s.name) : -1;
        public int SpeciesIndex(string name) => name != null && speciesByName.TryGetValue(name, out var i) ? i : -1;
        /// <summary>How many animals birds of prey have caught (for the test log).</summary>
        public static int Caught;
        public Habitat SpeciesHabitat(int index) => species[index].Body.Habitat;
        public bool SpeciesCaveDweller(int index) => species[index].Body.CaveDweller;
        /// <summary>A species that goes into caves now and then (CaveAffinity), though it lives outside.</summary>
        public bool SpeciesVisitsCaves(int index) => species[index].Body.CaveAffinity > 0f;
        /// <summary>A species' name as people say it ("MountainLion": "mountain lion").</summary>
        public string SpeciesName(int index)
        {
            if (index < 0 || index >= species.Count)
                return "animal";
            var name = species[index].name;
            var words = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    words.Append(' ');
                words.Append(char.ToLowerInvariant(name[i]));
            }
            return words.ToString();
        }
        /// <summary>Whether this machine simulates animals (alone or hosting), so Spawn works.</summary>
        public bool CanSpawn => simWorld != null && simWorld.IsCreated && built;

        // ------------------------------------------------------------------------------------------- the player's body

        /// <summary>
        /// Spawns the player's body if it's missing (or the species changed). There's always one: with no species
        /// chosen, a human (the player entity itself has no mesh). The body has its own runtime data, so changing
        /// species never disturbs the animals.
        /// </summary>
        void KeepAvatar()
        {
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            if (PlayerSpecies == null)
                PlayerSpecies = species.Find(s => s.name == "Human") ?? species.Find(s => s.Body.Upright);
            if (PlayerSpecies != avatarSpecies)
                DropAvatar();
            if (PlayerSpecies == null)
                return;
            if (avatarQuery.CalculateEntityCount() > 0)
                return;
            if (!avatarBlob.IsCreated)
                avatarBlob = CreatureBlobBuilder.Build(PlayerSpecies);
            avatarSpecies = PlayerSpecies;
            ref var b = ref avatarBlob.Value;
            var e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody), typeof(PlayerAvatar));
            em.SetName(e, "PlayerBody");
            em.SetComponentData(e, new CreatureBody
            {
                Blob = avatarBlob,
                Species = SpeciesIndex(PlayerSpecies),
                SegmentCount = b.Segments.Length,
                Coat = b.PickCoat(rng.NextFloat())
            });
            // (At the species' middle size: a cat body is cat-sized, an osprey osprey-sized.)
            var size = (b.Size.x + b.Size.y) * 0.5f;
            em.SetComponentData(e, new CreatureState
            {
                Position = player.Position,
                Phase = rng.NextFloat(),
                Scale = size,
                Mode = CreatureMode.Puppet,
                Seed = rng.NextUInt() | 1u,
                Individual = rng.NextUInt() | 1u
            });
            // What the body can do, for player movement: its pace next to a human's, and flight.
            var human = SpeciesIndex("Human");
            var humanRun = human >= 0 ? blobs[human].Value.RunSpeed * (blobs[human].Value.Size.x + blobs[human].Value.Size.y) * 0.5f : 0f;
            var abilities = new Punchy.Player.PlayerBody
            {
                SpeedFactor = humanRun > 0f ? math.clamp(b.RunSpeed * size / humanRun, 0.35f, 2.5f) : 1f,
                Flies = b.Habitat == Habitat.Flies,
                FlySpeed = b.FlySpeed
            };
            if (!em.CreateEntityQuery(typeof(Punchy.Player.PlayerBody)).TryGetSingletonEntity<Punchy.Player.PlayerBody>(out var bodyEntity))
                bodyEntity = em.CreateEntity(typeof(Punchy.Player.PlayerBody));
            em.SetComponentData(bodyEntity, abilities);
        }

        void DropAvatar()
        {
            if (queryWorld != null && queryWorld.IsCreated)
                queryWorld.EntityManager.DestroyEntity(avatarQuery);
            if (avatarBlob.IsCreated)
                avatarBlob.Dispose();
            avatarBlob = default;
            avatarSpecies = null;
        }

        // ------------------------------------------------------------------------------------------- ghosts

        /// <summary>Client: gives every newly arrived creature ghost the body of its species.</summary>
        void GiveReplicasBodies()
        {
            if (queryWorld == simWorld || newReplicaQuery.IsEmpty)
                return;
            var em = queryWorld.EntityManager;
            var arrived = newReplicaQuery.ToEntityArray(Allocator.Temp);
            var ghosts = new NativeList<Entity>(arrived.Length, Allocator.Temp);
            foreach (var e in arrived)
            {
                var net = em.GetComponentData<CreatureNet>(e);
                if (net.Species >= 0 && net.Species < blobs.Count)
                    ghosts.Add(e);
            }
            arrived.Dispose();
            // (All of them given their components in one go: one archetype move for the lot, not three each.)
            em.AddComponent(ghosts.AsArray(), new ComponentTypeSet(ComponentType.ReadWrite<CreatureBody>(), ComponentType.ReadWrite<CreatureState>(),
                                                                   ComponentType.ReadWrite<CreatureReplica>()));
            foreach (var e in ghosts)
            {
                var net = em.GetComponentData<CreatureNet>(e);
                var blob = blobs[net.Species];
                ref var b = ref blob.Value;
                var t = em.GetComponentData<LocalTransform>(e);
                var forward = math.mul(t.Rotation, new float3(0f, 0f, 1f));
                em.SetComponentData(e, new CreatureBody
                {
                    Blob = blob,
                    Species = net.Species,
                    SegmentCount = b.Segments.Length,
                    Coat = math.min(net.Coat, math.max(0, b.Coats.Length - 1))
                });
                em.SetComponentData(e, new CreatureState
                {
                    Position = t.Position,
                    Yaw = math.atan2(forward.x, forward.z),
                    Phase = rng.NextFloat(),
                    Scale = net.Scale > 0f ? net.Scale : 1f,
                    Mode = (CreatureMode)net.Mode,
                    Seed = net.Individual | 1u,
                    Individual = net.Individual
                });
            }
            ghosts.Dispose();
        }

        // ------------------------------------------------------------------------------------------- population

        /// <summary>Where the players are, in the simulation world: networked players, or the one local player.</summary>
        void GatherPlayers()
        {
            players.Clear();
            // (A server's own baked player entity is an idle leftover: only connected players count there.)
            var networked = simWorld.IsServer();
            var transforms = (networked ? netPlayerQuery : simPlayerQuery).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            foreach (var t in transforms)
                // A networked player sits at the origin until its owner reports where it is.
                if (!networked || math.lengthsq(t.Position) > 1e-6f)
                    players.Add(t.Position);
            transforms.Dispose();
        }

        float NearestPlayer(float2 xz)
        {
            var best = float.MaxValue;
            foreach (var p in players)
                best = math.min(best, math.distance(p.xz, xz));
            return best;
        }

        void Despawn()
        {
            var em = simWorld.EntityManager;
            var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = wildQuery.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = wildQuery.ToEntityArray(Allocator.Temp);
            var size = math.max(1, players.Count) * species.Count;
            if (counts == null || counts.Length != size)
                counts = new int[size];
            System.Array.Clear(counts, 0, counts.Length);
            var far = new NativeList<Entity>(Allocator.Temp);
            wildCount = settledCount = caveCount = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                var xz = states[i].Position.xz;
                // Far from everyone, or caught by a bird of prey; never one someone is carrying (wherever they go).
                // (Huge animals seen from far off are let go only beyond that.)
                var letGo = math.max(DespawnDistance, bodies[i].Blob.Value.SeenFrom + 20f);
                if ((players.Count == 0 || NearestPlayer(xz) > letGo || states[i].Gone) && states[i].Mode != CreatureMode.Held)
                {
                    if (states[i].Gone)
                        Caught++;
                    far.Add(entities[i]);
                    continue;
                }
                // (Of its kind: settled ones are leashed to a home, cave dwellers live in the dark.)
                if (states[i].Leash > 0f)
                    settledCount++;
                else if (bodies[i].Blob.Value.CaveDweller)
                    caveCount++;
                else
                    wildCount++;
                var s = bodies[i].Species;
                if (s < 0 || s >= species.Count)
                    continue;
                for (int p = 0; p < players.Count; p++)
                    if (math.distance(players[p].xz, xz) <= Radius)
                        counts[p * species.Count + s]++;
            }
            if (far.Length > 0)
                em.DestroyEntity(far.AsArray());
            far.Dispose();
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }

        void TrySpawn(in TerrainSource source, int playerIndex)
        {
            if (wildCount >= MaxWild || species.Count == 0)
                return;
            var player = players[playerIndex].xz;
            // A random spot in the ring around the player; whichever of its biome's species is furthest below its
            // density there gets a group. Not where another player would see it pop in.
            var angle = rng.NextFloat(math.PI * 2f);
            var distance = math.sqrt(rng.NextFloat(MinSpawnDistance * MinSpawnDistance, Radius * Radius));
            var at = player + new float2(math.cos(angle), math.sin(angle)) * distance;
            if (NearestPlayer(at) < MinSpawnDistance)
                return;
            var ground = source.SampleGround(at);
            // On land, land animals; in water, swimmers and floaters (deep enough for them).
            var depth = ground.HasWater ? ground.WaterSurface - ground.Height : -1f;
            var biome = biomes[ground.Biomes.Dominant()];
            if (biome == null)
                return;

            var areaHectares = math.PI * Radius * Radius / 10000f;
            var nearWater = ground.WaterDepth > -4f;
            var offset = playerIndex * species.Count;
            int best = -1;
            var bestShortfall = 0f;
            foreach (var f in biome.Fauna)
            {
                // A sky serpent: over deep water only, one at a time near a player, by its own chance (not the
                // shortfall, which the fish there would always win).
                if (f.Species.Body.Breaches)
                {
                    var serpent = SpeciesIndex(f.Species);
                    if (serpent >= 0 && depth >= 6f && counts[offset + serpent] == 0 && rng.NextFloat() < f.Density * areaHectares)
                    {
                        best = serpent;
                        bestShortfall = 1f;
                        break;
                    }
                    continue;
                }
                var index = SpeciesIndex(f.Species);
                // (Moths live round a cave's glowing plants: they come only with the cave.)
                // (Nor a wolf, a bear or the like in a town: the guards would only be driving it off again.)
                if (index < 0 || f.Species.Body.NearWater && !nearWater || !Suits(f.Species.Body.Habitat, depth) || f.Species.Body.DrawnToLight ||
                    f.Species.Body.DrivenOff && InTown(source, at))
                    continue;
                var shortfall = f.Density * areaHectares - counts[offset + index];
                if (shortfall > bestShortfall)
                {
                    bestShortfall = shortfall;
                    best = index;
                }
            }
            // Rare species (less than one expected nearby) turn up only some of the time.
            if (best < 0 || bestShortfall < 1f && rng.NextFloat() > bestShortfall)
                return;
            var s = species[best].Body;
            var group = rng.NextInt(math.max(1, s.GroupSize.x), math.max(s.GroupSize.x, s.GroupSize.y) + 1);
            for (int i = 0; i < group; i++)
            {
                var p = at + rng.NextFloat2Direction() * rng.NextFloat(0f, 2f + group * 1.5f);
                var g = source.SampleGround(p);
                var d = g.HasWater ? g.WaterSurface - g.Height : -1f;
                if (Suits(s.Habitat, d))
                    Spawn(best, new float3(p.x, s.Habitat == Habitat.Land || s.Habitat == Habitat.Flies ? g.Height : g.WaterSurface - math.min(d * 0.45f, s.SwimDepth), p.y));
            }
            counts[offset + best] += group;
        }

        /// <summary>Whether water this deep (negative = dry land) suits an animal of a habitat.</summary>
        /// <summary>Within a settlement, or 20 m of its edge.</summary>
        static bool InTown(in TerrainSource source, float2 at)
        {
            if (!source.Settlements.IsCreated)
                return false;
            ref var towns = ref source.Settlements.Value.Settlements;
            for (int i = 0; i < towns.Length; i++)
                if (math.distance(towns[i].Centre, at) < towns[i].Radius + 20f)
                    return true;
            return false;
        }

        static bool Suits(Habitat habitat, float depth) =>
            habitat == Habitat.Swims ? depth >= 0.9f : habitat == Habitat.Floats ? depth >= 0.4f : depth <= 0f;   // fliers start on the ground

        void SpawnResidents(in TerrainSource source)
        {
            if (!source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            scratch.Clear();
            foreach (var i in occupied)
                if (NearestPlayer(set.Instances[i].Position) > DespawnDistance + 20f)
                    scratch.Add(i);
            foreach (var i in scratch)
                occupied.Remove(i);

            var em = simWorld.EntityManager;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                if (inst.Type >= landmarks.Length || landmarks[inst.Type] == null || occupied.Contains(i) ||
                    NearestPlayer(inst.Position) > Radius)
                    continue;
                // (Full of settled folk and animals already: this one is peopled once some are let go.)
                if (settledCount >= MaxSettled)
                    break;
                occupied.Add(i);
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                SpawnDomestics(source, landmarks[inst.Type], inst.Position, radius);
                foreach (var r in landmarks[inst.Type].Residents)
                {
                    var index = SpeciesIndex(r.Species);
                    if (index < 0)
                        continue;
                    var count = rng.NextInt(math.max(0, r.Count.x), math.max(r.Count.x, r.Count.y) + 1);
                    // Residents with a leash live round their own home spot (a pen); workers at their post (and stay near
                    // it); the rest anywhere in the landmark.
                    var worker = r.Task != CreatureTask.None;
                    var stationed = r.Leash > 0f || worker;
                    var home = stationed ? LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.HomeRing, r.HomeAngle), radius) : inst.Position;
                    var leash = r.Leash > 0f ? r.Leash : worker ? 7f : radius * 0.8f;
                    for (int k = 0; k < count; k++)
                    {
                        var p = worker ? home + rng.NextFloat2Direction() * 0.2f
                              : r.Leash > 0f ? home + rng.NextFloat2Direction() * rng.NextFloat(0f, leash * 0.7f)
                              : inst.Position + rng.NextFloat2Direction() * rng.NextFloat(0.1f, 0.45f) * radius;
                        var g = source.SampleGround(p);
                        if (g.HasWater && g.WaterSurface > g.Height)
                            continue;
                        var e = Spawn(index, new float3(p.x, g.Height, p.y));
                        settledCount++;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Home = home;
                        state.Leash = leash;
                        if (r.Size.y > 0f)
                            state.Scale = rng.NextFloat(r.Size.x, math.max(r.Size.x, r.Size.y));
                        if (worker)
                        {
                            state.Task = r.Task;
                            state.WorkSpot = home;
                            state.WorkFace = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(r.FaceRing, r.FaceAngle), radius);
                            state.Mode = CreatureMode.Work;
                            state.Timer = rng.NextFloat(40f, 120f);
                        }
                        em.SetComponentData(e, state);
                    }
                }
            }
        }

        /// <summary>The settlement's domestic animals, about its middle, homed on it and leashed to it.</summary>
        void SpawnDomestics(in TerrainSource source, LandmarkType settlement, float2 centre, float radius)
        {
            if (domestics == null)
                return;
            var spawnedDomestics = 0;
            var em = simWorld.EntityManager;
            foreach (var d in domestics.Entries)
            {
                // By name: the palette's landmark types (baked into the world subscene) aren't the same objects as the table's.
                if (d.Settlement == null || d.Species == null || d.Settlement.name != settlement.name)
                    continue;
                var index = SpeciesIndex(d.Species);
                if (index < 0)
                    continue;
                var count = rng.NextInt(math.max(0, d.Count.x), math.max(d.Count.x, d.Count.y) + 1);
                // Hunters range well beyond the settlement, out to where the game is (fishers to the water).
                var leash = d.Task == CreatureTask.Hunt || d.Task == CreatureTask.Fish ? math.max(radius * 3f, 70f) : radius * d.Range;
                // Lava dwellers go in the landmark's lava (a few tries each), not on dry ground.
                var lava = d.Species.Body.LavaDweller;
                for (int k = 0; k < count; k++)
                {
                    var p = centre + rng.NextFloat2Direction() * rng.NextFloat(0.1f, 0.7f) * leash;
                    var g = source.SampleGround(p);
                    for (int t = 0; lava && t < 12 && !(g.HasWater && g.WaterSurface - g.Height > 0.5f &&
                                                        source.LavaAt(new float3(p.x, g.WaterSurface - 0.2f, p.y)) > 0.5f); t++)
                    {
                        p = centre + rng.NextFloat2Direction() * rng.NextFloat(0f, 0.8f) * leash;
                        g = source.SampleGround(p);
                    }
                    if (lava ? !(g.HasWater && g.WaterSurface > g.Height + 0.5f) : g.HasWater && g.WaterSurface > g.Height)
                        continue;
                    var e = Spawn(index, new float3(p.x, lava ? g.WaterSurface - 0.3f : g.Height, p.y));
                    spawnedDomestics++;
                    settledCount++;
                    var state = em.GetComponentData<CreatureState>(e);
                    state.Home = centre;
                    state.Leash = leash;
                    state.Task = d.Task;
                    em.SetComponentData(e, state);
                }
            }
            if (spawnedDomestics > 0)
                Debug.Log($"FaunaDirector: {spawnedDomestics} domestic animals at {settlement.name}");
        }

        /// <summary>
        /// Cave dwellers appear along the inner passages of a cave when a player comes within Radius of its mouth, a
        /// few of each kind; like residents, they're forgotten once everyone is well away.
        /// </summary>
        void SpawnCaveDwellers(in TerrainSource source)
        {
            if (!source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;
            scratch.Clear();
            foreach (var i in occupiedCaves)
                if (NearestPlayer(set.Caves[i].Mouth.xz) > DespawnDistance + 20f)
                    scratch.Add(i);
            foreach (var i in scratch)
                occupiedCaves.Remove(i);

            for (int i = 0; i < set.Caves.Length; i++)
            {
                ref var cave = ref set.Caves[i];
                if (occupiedCaves.Contains(i) || cave.NodeCount < 2 || NearestPlayer(cave.Mouth.xz) > Radius)
                    continue;
                if (caveCount >= MaxCave)
                    break;
                occupiedCaves.Add(i);
                for (int sp = 0; sp < species.Count; sp++)
                {
                    var body = species[sp].Body;
                    // Now and then a surface animal (a bear in its den, a wolf, a fox), if it lives round the mouth:
                    // one or two in the chambers near the mouth.
                    var visitor = !body.CaveDweller && species[sp].CaveAffinity > 0f;
                    if (!body.CaveDweller && !visitor)
                        continue;
                    if (visitor && (rng.NextFloat() >= species[sp].CaveAffinity || !LivesAt(source, sp, cave.Mouth.xz)))
                        continue;
                    var count = visitor ? rng.NextInt(1, 3)
                              : rng.NextInt(math.max(1, body.GroupSize.x), math.max(body.GroupSize.x, body.GroupSize.y) + 1);
                    // Moths: a few round each of the cave's glowing plants (none in a cave without any).
                    if (body.DrawnToLight)
                    {
                        if (!source.Anchors.IsCreated)
                            continue;
                        ref var anchors = ref source.Anchors.Value;
                        for (int a = 0; a < anchors.Anchors.Length; a++)
                        {
                            var glow = anchors.Anchors[a];
                            if (glow.Kind != AnchorKind.Glow || !glow.InCave || glow.OwnerIndex != i)
                                continue;
                            for (int k = 0; k < count; k++)
                            {
                                Spawn(sp, glow.Position + new float3(rng.NextFloat(-0.6f, 0.6f), rng.NextFloat(0.4f, 1.4f), rng.NextFloat(-0.6f, 0.6f)));
                                caveCount++;
                            }
                        }
                        continue;
                    }
                    for (int k = 0; k < count; k++)
                    {
                        // On the floor somewhere in the inner two thirds of the passage (a visitor, the outer half).
                        var node = set.Nodes[cave.FirstNode + (visitor ? rng.NextInt(1, math.max(2, cave.NodeCount / 2))
                                                                       : rng.NextInt(cave.NodeCount / 3, cave.NodeCount))];
                        var at = node.Position + new float3(rng.NextFloat(-0.5f, 0.5f), 0f, rng.NextFloat(-0.5f, 0.5f)) * node.Radius;
                        var floor = source.FloorUnder(at, out var inCave);
                        // (Really under the rock: not where a passage opens to the sky.)
                        if (inCave && source.CaveDistance(new float3(at.x, floor + 0.3f, at.z)) < -0.05f)
                        {
                            Spawn(sp, new float3(at.x, floor, at.z));
                            if (visitor)
                                wildCount++;
                            else
                                caveCount++;
                        }
                    }
                }
            }
        }

        /// <summary>Whether a species is among the animals of the biome at a point.</summary>
        bool LivesAt(in TerrainSource source, int speciesIndex, float2 at)
        {
            var biome = source.SampleGround(at).Biomes.Dominant();
            if (biome < 0 || biome >= biomes.Length || biomes[biome] == null)
                return false;
            foreach (var f in biomes[biome].Fauna)
                if (f.Species != null && f.Species.name == species[speciesIndex].name)
                    return true;
            return false;
        }

        /// <summary>
        /// Creates one animal of a species (by index) at a point, in the simulation world: a creature ghost when this
        /// machine is a server, a plain entity without netcode. Also handy for testing.
        /// </summary>
        public Entity Spawn(int speciesIndex, float3 position, float yaw = float.NaN)
        {
            var em = simWorld.EntityManager;
            ref var b = ref blobs[speciesIndex].Value;
            Entity e;
            if (simWorld.IsServer() && ghostSpawnerQuery.TryGetSingleton<CreatureGhostSpawner>(out var spawner))
            {
                e = em.Instantiate(spawner.Prefab);
                // (Both at once: one move to the new archetype, not two.)
                em.AddComponent(e, new ComponentTypeSet(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>()));
            }
            else
                e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody));
            em.SetComponentData(e, new CreatureBody
            {
                Blob = blobs[speciesIndex],
                Species = speciesIndex,
                SegmentCount = b.Segments.Length,
                Coat = b.PickCoat(rng.NextFloat())
            });
            em.SetComponentData(e, new CreatureState
            {
                Position = position,
                Yaw = float.IsNaN(yaw) ? rng.NextFloat(-math.PI, math.PI) : yaw,
                Phase = rng.NextFloat(),
                Scale = rng.NextFloat(b.Size.x, math.max(b.Size.x, b.Size.y)),
                Mode = CreatureMode.Idle,
                Timer = rng.NextFloat(0.5f, 4f),
                LairCave = -1,
                // Needs start part-way, so a new arrival doesn't do everything at once.
                Hunger = rng.NextFloat(0.1f, 0.7f),
                Thirst = rng.NextFloat(0.1f, 0.6f),
                Tired = rng.NextFloat(0f, 0.5f),
                Lonely = rng.NextFloat(0f, 0.5f),
                Seed = rng.NextUInt() | 1u,
                Individual = rng.NextUInt() | 1u
            });
            if (em.HasComponent<LocalTransform>(e))
                em.SetComponentData(e, LocalTransform.FromPosition(position));
            return e;
        }

        // ------------------------------------------------------------------------------------------- mesh

        /// <summary>
        /// A unit capsule template: positions hold (cos, sin) around the axis and t (0..1) along it; UV0 holds the
        /// cap offset along the axis and the radius factor, in units of the local radius. The creature shader turns
        /// it into any length and taper.
        /// </summary>
        static Mesh BuildCapsule(int sides, int lengthRows, int capRows)
        {
            var rows = new List<Vector3>();   // t, capZ, capR
            for (int k = 0; k < capRows; k++)
            {
                var phi = (1f - k / (float)capRows) * Mathf.PI * 0.5f;
                rows.Add(new Vector3(0f, -Mathf.Sin(phi), Mathf.Cos(phi)));
            }
            for (int k = 0; k <= lengthRows; k++)
                rows.Add(new Vector3(k / (float)lengthRows, 0f, 1f));
            for (int k = 1; k <= capRows; k++)
            {
                var phi = k / (float)capRows * Mathf.PI * 0.5f;
                rows.Add(new Vector3(1f, Mathf.Sin(phi), Mathf.Cos(phi)));
            }

            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var row in rows)
                for (int s = 0; s < sides; s++)
                {
                    var a = s / (float)sides * Mathf.PI * 2f;
                    positions.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), row.x));
                    uvs.Add(new Vector2(row.y, row.z));
                }
            var indices = new List<int>();
            for (int r = 0; r < rows.Count - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides;
                // Clockwise seen from outside, so the outside faces are the front faces.
                indices.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
            }
            var mesh = new Mesh { name = "Creature Capsule" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
