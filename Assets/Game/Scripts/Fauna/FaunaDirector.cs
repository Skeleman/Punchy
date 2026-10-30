using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
        public int MaxWild = 180;
        [Tooltip("At most this many settled ones (settlements' residents and their domestic animals), counted apart so " +
                 "a big town never stops the wild ones appearing.")]
        public int MaxSettled = 60;
        [Tooltip("At most this many cave dwellers.")]
        public int MaxCave = 100;
        [Tooltip("Spawn attempts per second (per player).")]
        public float SpawnRate = 6f;
        [Tooltip("At most this many critters (the small life put round players from the seed: songbirds, owls, frogs, " +
                 "mice), shared fairly among the players with critter spots near them.")]
        public int MaxCritters = 96;
        [Tooltip("Critters are put at their spots within this distance of a player, and let go beyond CritterLetGo.")]
        public float CritterRadius = 50f, CritterLetGo = 65f;

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
        EntityQuery wildQuery, simCreatureQuery, netPlayerQuery, simPlayerQuery, ghostSpawnerQuery, floraQuery;
        /// <summary>When the far ones are next let go (and the counts tallied): four times a second is plenty.</summary>
        float nextDespawn;
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
        int wildCount, settledCount, caveCount, critterCount;

        /// <summary>Which count a waiting animal goes towards (Despawn's tally).</summary>
        enum SpawnTier : byte { Wild, Settled, Cave, Critter }

        /// <summary>An animal waiting to be made: where, and what to set up on it once it exists.</summary>
        struct PendingSpawn
        {
            public int Species;
            public float3 Position;
            public float Yaw;
            public SpawnTier Tier;
            public System.Action<Entity> Then;
        }

        [Tooltip("Animals made per frame from the waiting queue: a town, a cave or a batch of critters arrives over a few " +
                 "frames instead of all in one.")]
        public int SpawnsPerFrame = 8;
        readonly Queue<PendingSpawn> pendingSpawns = new Queue<PendingSpawn>();
        /// <summary>How many are waiting, of each tier (counted with those already made, so the caps hold).</summary>
        readonly int[] pendingTiers = new int[4];
        /// <summary>Critter spots with an animal on its way.</summary>
        readonly HashSet<uint> pendingCritterSpots = new HashSet<uint>();
        EntityArchetype plainArchetype;
        /// <summary>The critter species (Resources/Critters), and which critter spots are taken, by whom.</summary>
        CritterTable critterTable;
        readonly Dictionary<uint, Entity> critters = new Dictionary<uint, Entity>();
        readonly List<AmbientLife.CritterSpot> critterSpots = new List<AmbientLife.CritterSpot>();
        /// <summary>
        /// Each 16 m cell's critter spots, worked out once: they come from the cell, the ground and the flora alone, and
        /// finding them (a tree search and dozens of ground samples a cell) cost a 100-270 ms stall every half second
        /// when redone on the main thread for every cell round every player. Now new cells are worked out in a Burst job
        /// off the main thread (SettleCritterCells), and cells far from everyone are dropped.
        /// </summary>
        readonly Dictionary<int2, AmbientLife.CritterSpot[]> critterCells = new Dictionary<int2, AmbientLife.CritterSpot[]>();
        readonly List<int2> critterCellsGone = new List<int2>();
        readonly List<int2> critterMissing = new List<int2>();
        /// <summary>Cells handed to one settling job, at most.</summary>
        const int CritterCellsPerJob = 64;
        TerrainSource critterSource;
        JobHandle settleHandle;
        bool settling;
        NativeArray<int2> settleCells;
        NativeList<AmbientLife.CritterSpot> settleSpots;
        NativeArray<int> settleEnds;
        float nextCritterCheck;
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
            // (The settling job reads the flora blob disposed below.)
            EndSettling();
            critterCells.Clear();
            pendingSpawns.Clear();
            System.Array.Clear(pendingTiers, 0, pendingTiers.Length);
            pendingCritterSpots.Clear();
            plainArchetype = default;
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

        // (Benches and profiles see the director's own work, and which of its passes a spike is in.)
        static readonly Unity.Profiling.ProfilerMarker UpdateMarker = new Unity.Profiling.ProfilerMarker("FaunaDirector.Update");
        static readonly Unity.Profiling.ProfilerMarker DespawnMarker = new Unity.Profiling.ProfilerMarker("FaunaDirector.Despawn");
        static readonly Unity.Profiling.ProfilerMarker ResidentsMarker = new Unity.Profiling.ProfilerMarker("FaunaDirector.Residents");
        static readonly Unity.Profiling.ProfilerMarker CrittersMarker = new Unity.Profiling.ProfilerMarker("FaunaDirector.Critters");
        static readonly Unity.Profiling.ProfilerMarker WildMarker = new Unity.Profiling.ProfilerMarker("FaunaDirector.Wild");

        void Update()
        {
            using (UpdateMarker.Auto())
                UpdateDirector();
        }

        void UpdateDirector()
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

            var clock = new FaunaClock { Night = Cycle != null ? Cycle.Night : 0f, Hour = Cycle != null ? Cycle.TimeOfDay : 12f };
            queryWorld.EntityManager.SetComponentData(settingsEntity, clock);
            if (simWorld == null)
                return;
            if (simWorld != queryWorld)
                simWorld.EntityManager.SetComponentData(simClockEntity, clock);

            GatherPlayers();
            if (Time.time >= nextDespawn)
            {
                nextDespawn = Time.time + 0.25f;
                using (DespawnMarker.Auto())
                    Despawn();
            }
            DrainSpawns();
            if (players.Count == 0)
                return;
            if (Time.time >= nextResidentCheck)
            {
                nextResidentCheck = Time.time + 0.5f;
                using (ResidentsMarker.Auto())
                {
                    SpawnResidents(source);
                    SpawnCaveDwellers(source);
                    SpawnWildFolk(source);
                }
            }
            using (CrittersMarker.Auto())
            {
                SettleCritterCells(source);
                if (Time.time >= nextCritterCheck)
                {
                    nextCritterCheck = Time.time + 0.5f;
                    SpawnCritters(source);
                }
            }
            if (Time.time >= nextTraveller)
            {
                nextTraveller = Time.time + rng.NextFloat(60f, 120f);
                SpawnTraveller(source);
            }
            if (Time.time >= nextMessenger)
            {
                nextMessenger = Time.time + rng.NextFloat(120f, 240f);
                SpawnMessenger(source);
            }
            if (Time.time >= nextHoneyThief)
            {
                nextHoneyThief = Time.time + rng.NextFloat(90f, 180f);
                SpawnHoneyThief(source);
            }
            spawnBudget = math.min(spawnBudget + SpawnRate * players.Count * Time.deltaTime, 3f * players.Count);
            using (WildMarker.Auto())
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
                    floraQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.FloraLookup>());
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
            critterTable = Resources.Load<CritterTable>("Critters");
            if (critterTable != null)
                foreach (var c in critterTable.All())
                    AddSpecies(c);
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
                if (playable[i].Role == DirectorRole.Folk)
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
        /// <summary>The first species playing a part for the director (CreatureSpecies.Role), or -1.</summary>
        public int RoleIndex(DirectorRole role)
        {
            for (int i = 0; i < species.Count; i++)
                if (species[i] != null && species[i].Role == role)
                    return i;
            return -1;
        }
        /// <summary>The species asset at an index (for things that read its data directly, as the sound does).</summary>
        public CreatureSpecies SpeciesAt(int index) => index >= 0 && index < species.Count ? species[index] : null;
        public int SpeciesCount => species.Count;
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
                PlayerSpecies = species.Find(s => s.Role == DirectorRole.Folk) ?? species.Find(s => s.Body.Upright);
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
            var human = RoleIndex(DirectorRole.Folk);
            var humanRun = human >= 0 ? blobs[human].Value.RunSpeed * (blobs[human].Value.Size.x + blobs[human].Value.Size.y) * 0.5f : 0f;
            var abilities = new Punchy.Player.PlayerBody
            {
                SpeedFactor = humanRun > 0f ? math.clamp(b.RunSpeed * size / humanRun, 0.35f, 2.5f) : 1f,
                Flies = b.Habitat == Habitat.Flies,
                FlySpeed = PlayerSpecies.PlayerFlySpeed > 0f ? PlayerSpecies.PlayerFlySpeed
                         : PlayerSpecies.Body.PlayerFlySpeed > 0f ? PlayerSpecies.Body.PlayerFlySpeed : b.FlySpeed * 2f,
                Climbs = b.Hands || b.Climbing > 0f || b.Crawls,
                ClimbsTrees = b.ClimbsAs != ClimbAbility.None,
                ClimbsWalls = b.ClimbsAs == ClimbAbility.AnySurface,
                // (A lizard scurries up; a tree climber as nimbly as it's keen: a monkey quick, a cat less, a bear hauling.)
                ClimbSpeed = b.ClimbsAs == ClimbAbility.AnySurface ? 1.4f : math.min(0.6f + 1.6f * b.Climbing, 1.6f),
                // (A lava swimmer walks on land as a player; only fish swim and flop.)
                Swims = b.Habitat == Habitat.Swims && !b.LavaDweller,
                Hovers = b.Haunts,
                Size = human >= 0 && blobs[human].Value.StandHeight > 0f
                    ? math.clamp(b.StandHeight * size / (blobs[human].Value.StandHeight * (blobs[human].Value.Size.x + blobs[human].Value.Size.y) * 0.5f), 0.25f, 3f)
                    : 1f
            };
            using var bodyQuery = em.CreateEntityQuery(typeof(Punchy.Player.PlayerBody));
            if (!bodyQuery.TryGetSingletonEntity<Punchy.Player.PlayerBody>(out var bodyEntity))
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
            // (Someone joined or left: the head counts per player grow or shrink with them now, not at the next tally,
            // which comes only a few times a second; until then the new player's are nought.)
            var size = math.max(1, players.Count) * species.Count;
            if (counts == null || counts.Length != size)
                System.Array.Resize(ref counts, size);
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
            wildCount = settledCount = caveCount = critterCount = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                var xz = states[i].Position.xz;
                // Critters: their own tier, let go much nearer (they're put back at their spots when someone comes near).
                if (states[i].Task == CreatureTask.Critter)
                {
                    if ((players.Count == 0 || NearestPlayer(xz) > CritterLetGo || states[i].Gone) && states[i].Mode != CreatureMode.Held)
                        far.Add(entities[i]);
                    else
                        critterCount++;
                    continue;
                }
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
            // (Those still waiting to be made count too.)
            wildCount += pendingTiers[(int)SpawnTier.Wild];
            settledCount += pendingTiers[(int)SpawnTier.Settled];
            caveCount += pendingTiers[(int)SpawnTier.Cave];
            critterCount += pendingTiers[(int)SpawnTier.Critter];
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

        /// <summary>Hermits living at their Home anchors (one each), and travellers stopping at roadside camps (Camp).</summary>
        readonly Dictionary<int, Entity> hermits = new Dictionary<int, Entity>();
        readonly Dictionary<int, List<Entity>> campers = new Dictionary<int, List<Entity>>();
        float nextCamper;

        /// <summary>
        /// The folk of the wild places near players: a hermit at each hermit's shelter (its Home anchor) within 120 m of
        /// one, while none lives there; and, from late afternoon, now and then a traveller walking in from the road to a
        /// roadside camp with a free bedroll, to spend the night there.
        /// </summary>
        void SpawnWildFolk(in TerrainSource source)
        {
            if (!source.Anchors.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            var human = RoleIndex(DirectorRole.Folk);
            var hermit = RoleIndex(DirectorRole.Hermit);
            if (hermit < 0)
                hermit = human;
            if (human < 0)
                return;
            ref var set = ref source.Anchors.Value;
            var near = new NativeList<int>(8, Allocator.Temp);
            foreach (var player in players)
            {
                near.Clear();
                Anchors.Near(ref set, player, 120f, AnchorKind.Home, ref near);
                foreach (var i in near)
                {
                    if (hermits.TryGetValue(i, out var lives) && em.Exists(lives))
                        continue;
                    var home = set.Anchors[i];
                    // (Out in front of its shelter, getting on with its day.)
                    var at = home.Position.xz + home.Facing * 3f;
                    var e = Spawn(hermit, new float3(at.x, source.SampleHeight(at), at.y));
                    var st = em.GetComponentData<CreatureState>(e);
                    st.Task = CreatureTask.Hermit;
                    st.Home = home.Position.xz;
                    st.Leash = 35f;
                    st.Anchor = home.Position;
                    em.SetComponentData(e, st);
                    hermits[i] = e;
                    settledCount++;
                }
                // A traveller for a camp: from late afternoon to the small hours, now and then.
                var hour = Cycle != null ? Cycle.TimeOfDay : 12f;
                if (Time.time < nextCamper || !(hour >= 16.5f || hour < 2f))
                    continue;
                near.Clear();
                Anchors.Near(ref set, player, 120f, AnchorKind.Camp, ref near);
                foreach (var i in near)
                {
                    if (!campers.TryGetValue(i, out var list))
                        campers[i] = list = new List<Entity>();
                    list.RemoveAll(x => !em.Exists(x));
                    var camp = set.Anchors[i];
                    if (list.Count >= math.max((int)camp.Capacity, 1))
                        continue;
                    nextCamper = Time.time + rng.NextFloat(40f, 90f);
                    // (Walking in from the way the road lies.)
                    var at = camp.Position.xz + camp.Facing * 45f + new float2(-camp.Facing.y, camp.Facing.x) * rng.NextFloat(-8f, 8f);
                    var e = Spawn(human, new float3(at.x, source.SampleHeight(at), at.y));
                    var st = em.GetComponentData<CreatureState>(e);
                    st.Task = CreatureTask.Camper;
                    st.Home = camp.Position.xz;
                    st.Leash = 60f;
                    st.Anchor = camp.Position;
                    em.SetComponentData(e, st);
                    list.Add(e);
                    settledCount++;
                    break;
                }
            }
            near.Dispose();
        }

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
                var place = inst;
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
                    var face = worker ? LandmarkSampling.ToWorld(place, LandmarkLayout.Spot(r.FaceRing, r.FaceAngle), radius) : float2.zero;
                    var task = r.Task;
                    // A smith or woodcutter works where its prop's Work anchor stands it (at the anvil, beside the chopping
                    // block, facing it), if there's one near its post; its home and leash go with it.
                    if (worker && (task == CreatureTask.Hammer || task == CreatureTask.Chop) && source.Anchors.IsCreated &&
                        WorkAnchorNear(ref source.Anchors.Value, new float3(face.x, source.SampleHeight(face), face.y), 12f, out var stand, out var facing))
                    {
                        home = stand;
                        face = stand + facing;
                    }
                    for (int k = 0; k < count && settledCount < MaxSettled; k++)
                    {
                        var p = worker ? home + rng.NextFloat2Direction() * 0.2f
                              : r.Leash > 0f ? home + rng.NextFloat2Direction() * rng.NextFloat(0f, leash * 0.7f)
                              : inst.Position + rng.NextFloat2Direction() * rng.NextFloat(0.1f, 0.45f) * radius;
                        var g = source.SampleGround(p);
                        if (g.HasWater && g.WaterSurface > g.Height)
                            continue;
                        var scale = r.Size.y > 0f ? rng.NextFloat(r.Size.x, math.max(r.Size.x, r.Size.y)) : 0f;
                        var timer = rng.NextFloat(40f, 120f);
                        Enqueue(index, new float3(p.x, g.Height, p.y), SpawnTier.Settled, e =>
                        {
                            var em = simWorld.EntityManager;
                            var state = em.GetComponentData<CreatureState>(e);
                            state.Home = home;
                            state.Leash = leash;
                            if (scale > 0f)
                                state.Scale = scale;
                            if (worker)
                            {
                                state.Task = task;
                                state.WorkSpot = home;
                                state.WorkFace = face;
                                state.Mode = CreatureMode.Work;
                                state.Timer = timer;
                            }
                            em.SetComponentData(e, state);
                        });
                    }
                }
            }
        }

        float nextTraveller;
        float nextMessenger = 30f;
        float nextHoneyThief = 60f;
        Entity honeyThief;

        /// <summary>
        /// Now and then by day, a stranger after a wild hive's honey near a player: one of the folk with a stick walks up
        /// to a hive 25 to 90 m from them, pokes at the comb (the bees boil out round them), grabs a piece and runs off
        /// with it, the bees after them. One at a time. The brain's hive-raid stages do the rest, as for a bear.
        /// </summary>
        void SpawnHoneyThief(in TerrainSource source)
        {
            if (simWorld == null || !source.Anchors.IsCreated || (Cycle != null && Cycle.Night > 0.3f))
                return;
            var em = simWorld.EntityManager;
            if (honeyThief != Entity.Null && em.Exists(honeyThief))
                return;
            var human = RoleIndex(DirectorRole.Folk);
            if (human < 0)
                return;
            var player = players[rng.NextInt(players.Count)];
            ref var set = ref source.Anchors.Value;
            var near = new NativeList<int>(4, Allocator.Temp);
            Anchors.Near(ref set, player, 90f, AnchorKind.Hive, ref near);
            var pick = -1;
            for (int i = 0; i < near.Length; i++)
            {
                ref var h = ref set.Anchors[near[i]];
                if (!h.InCave && math.distance(h.Position.xz, player.xz) > 25f && (pick < 0 || rng.NextBool()))
                    pick = near[i];
            }
            near.Dispose();
            if (pick < 0)
                return;
            var hive = set.Anchors[pick];
            var trunk = math.max(hive.Size - 1f, 0.1f);
            // (Coming in from out beyond the hive, where the player can watch them walk up.)
            var from = hive.Position.xz + math.normalizesafe(hive.Position.xz - player.xz, rng.NextFloat2Direction()) * 25f;
            for (int tries = 0; tries < 4 && !(source.SampleGround(from) is var g && !(g.HasWater && g.WaterSurface > g.Height)); tries++)
                from = hive.Position.xz + rng.NextFloat2Direction() * 25f;
            var e = Spawn(human, new float3(from.x, source.SampleHeight(from), from.y));
            var state = em.GetComponentData<CreatureState>(e);
            state.Mode = CreatureMode.Work;
            state.Stage = CreatureAnimator.RaidWalk;
            state.StageTime = 0f;
            state.Focus = hive.Position.xz + hive.Facing * trunk;
            state.Target = hive.Position.xz + hive.Facing * (trunk + 0.35f);
            state.Timer = 120f;
            state.ChaseCooldown = 900f;
            state.Held = HeldItem.Stick;
            state.Home = from;
            state.Leash = 0f;
            em.SetComponentData(e, state);
            honeyThief = e;
        }

        /// <summary>
        /// Now and then (every two to four minutes) a messenger bird for a town a player is in: a pigeon high over the
        /// way to another town, 90 m out, with a message (the brain's Messenger task brings it in to one of the townsfolk,
        /// then sends it off towards a third town, or back); one at a time.
        /// </summary>
        void SpawnMessenger(in TerrainSource source)
        {
            if (!source.Settlements.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            {
                var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var flying = false;
                for (int i = 0; i < states.Length && !flying; i++)
                    flying = states[i].Task == CreatureTask.Messenger;
                states.Dispose();
                if (flying)
                    return;
            }
            var pigeon = RoleIndex(DirectorRole.Messenger);
            ref var set = ref source.Settlements.Value;
            if (pigeon < 0 || set.Settlements.Length < 2)
                return;
            var player = players[rng.NextInt(players.Count)];
            var town = -1;
            for (int t = 0; t < set.Settlements.Length && town < 0; t++)
                if (math.distance(set.Settlements[t].Centre, player.xz) < set.Settlements[t].Radius + 20f)
                    town = t;
            if (town < 0)
                return;
            var from = (town + 1 + rng.NextInt(set.Settlements.Length - 1)) % set.Settlements.Length;
            var to = (town + 1 + rng.NextInt(set.Settlements.Length - 1)) % set.Settlements.Length;
            var centre = set.Settlements[town].Centre;
            var start = centre + math.normalizesafe(set.Settlements[from].Centre - centre, new float2(1f, 0f)) * 90f;
            var e = Spawn(pigeon, new float3(start.x, source.SampleHeight(start) + 25f, start.y));
            var state = em.GetComponentData<CreatureState>(e);
            state.Position.y = source.SampleHeight(start) + 25f;
            state.Task = CreatureTask.Messenger;
            state.Mode = CreatureMode.Work;
            state.Stage = 0;
            state.StageTime = 0f;
            state.Partner = Entity.Null;
            state.Home = centre;
            state.Leash = 0f;
            state.WorkSpot = set.Settlements[to].Centre;
            state.Held = HeldItem.Letter;
            state.AnchorSize = 0f;
            em.SetComponentData(e, state);
        }

        /// <summary>
        /// Now and then (every minute or two) a traveller on a road between towns, a way off from a player (40 to 160 m,
        /// outside any town), heading along it the way that brings it nearer (the brain's Travel task walks it there); at
        /// most two on the roads at a time.
        /// </summary>
        void SpawnTraveller(in TerrainSource source)
        {
            if (!source.Settlements.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            {
                var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var travelling = 0;
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Task == CreatureTask.Travel)
                        travelling++;
                states.Dispose();
                if (travelling >= 2)
                    return;
            }
            var human = RoleIndex(DirectorRole.Folk);
            if (human < 0)
                return;
            ref var set = ref source.Settlements.Value;
            if (set.Streets.Length == 0)
                return;
            var player = players[rng.NextInt(players.Count)];
            // (Every road stretch in the ring round the player, outside the towns; one of them at random.)
            var candidates = new List<int>();
            for (int k = 0; k < set.Streets.Length; k++)
            {
                ref var st = ref set.Streets[k];
                if (st.Road == 0)
                    continue;
                var d = math.distance(st.A.xz, player.xz);
                if (d < 40f || d > 160f)
                    continue;
                var inTown = false;
                for (int t = 0; t < set.Settlements.Length && !inTown; t++)
                    inTown = math.distance(set.Settlements[t].Centre, st.A.xz) < set.Settlements[t].Radius;
                if (!inTown)
                    candidates.Add(k);
            }
            var found = candidates.Count > 0 ? candidates[rng.NextInt(candidates.Count)] : -1;
            if (found < 0)
                return;
            var start = set.Streets[found].A;
            var e = Spawn(human, new float3(start.x, source.SampleHeight(start.xz), start.z));
            var state = em.GetComponentData<CreatureState>(e);
            state.Task = CreatureTask.Travel;
            state.Mode = CreatureMode.Work;
            state.Stage = 0;
            state.TargetHeight = found;   // (the road stretch it's on: see the brain's Travelling)
            // (Heading the way that brings it nearer the player: a traveller seen arriving, not one walking off out of sight.)
            state.AnchorSide = (byte)(math.distance(set.Streets[found].B.xz, player.xz) < math.distance(start.xz, player.xz) ? 1 : 0);
            state.Home = start.xz;
            state.Leash = 0f;
            state.Timer = 900f;
            em.SetComponentData(e, state);
        }

        /// <summary>
        /// The critter tier: round each player, the critter spots (AmbientLife.CritterSpots, from the seed) within
        /// CritterRadius that are free and are for this time of day get their real creature, nearest first, up to that
        /// player's fair share of MaxCritters (shared among the players with spots near them, never below a floor of 24).
        /// Not where a player is right on top of it. They're homed on their spot (leashed close) and run on the brain's
        /// light critter round; Despawn lets them go beyond CritterLetGo, and the spot is free again.
        /// </summary>
        /// <summary>Works out the critter spots of a list of cells, in order; Ends[i] is where cell i's spots end.</summary>
        [BurstCompile]
        struct SettleCellsJob : IJob
        {
            public TerrainSource Source;
            public BlobAssetReference<Punchy.Flora.FloraBlob> Flora;
            [ReadOnly] public NativeArray<int2> Cells;
            public NativeList<AmbientLife.CritterSpot> Spots;
            public NativeArray<int> Ends;

            public void Execute()
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    AmbientLife.CritterSpots(Cells[i], Source, ref Flora.Value, ref Spots);
                    Ends[i] = Spots.Length;
                }
            }
        }

        /// <summary>
        /// Keeps the critter cells round the players worked out: collects a finished settling job's cells into the cache,
        /// then hands the next batch of unknown cells (nearest a player first) to a new job. The main thread only does the
        /// bookkeeping, so the critter tier never stalls a frame.
        /// </summary>
        void SettleCritterCells(in TerrainSource source)
        {
            if (settling)
            {
                if (!settleHandle.IsCompleted)
                    return;
                settleHandle.Complete();
                // (Unless the world changed meanwhile: then they're not this world's spots.)
                if (critterSource.Equals(source))
                {
                    var from = 0;
                    for (int i = 0; i < settleCells.Length; i++)
                    {
                        var spots = new AmbientLife.CritterSpot[settleEnds[i] - from];
                        for (int k = 0; k < spots.Length; k++)
                            spots[k] = settleSpots[from + k];
                        critterCells[settleCells[i]] = spots;
                        from = settleEnds[i];
                    }
                }
                EndSettling();
            }
            if (critterTable == null || simWorld == null || players.Count == 0)
                return;
            if (!critterSource.Equals(source))
            {
                critterCells.Clear();
                critterSource = source;
            }
            if (floraQuery.IsEmpty)
                return;
            var flora = floraQuery.GetSingleton<Punchy.Flora.FloraLookup>();
            if (!flora.Blob.IsCreated)
                return;
            // Rings outwards round each player in turn, so the nearest cells come first.
            const float cell = 16f;
            critterMissing.Clear();
            var reach = (int)math.ceil(CritterRadius / cell);
            for (int ring = 0; ring <= reach && critterMissing.Count < CritterCellsPerJob; ring++)
            foreach (var player in players)
            {
                var centre = (int2)math.floor(player.xz / cell);
                for (int y = -ring; y <= ring; y++)
                for (int x = -ring; x <= ring; x++)
                {
                    var key = centre + new int2(x, y);
                    if (math.max(math.abs(x), math.abs(y)) == ring && critterMissing.Count < CritterCellsPerJob &&
                        !critterCells.ContainsKey(key) && !critterMissing.Contains(key))
                        critterMissing.Add(key);
                }
            }
            if (critterMissing.Count == 0)
                return;
            settleCells = new NativeArray<int2>(critterMissing.Count, Allocator.Persistent);
            for (int i = 0; i < critterMissing.Count; i++)
                settleCells[i] = critterMissing[i];
            settleSpots = new NativeList<AmbientLife.CritterSpot>(critterMissing.Count * 4, Allocator.Persistent);
            settleEnds = new NativeArray<int>(critterMissing.Count, Allocator.Persistent);
            settleHandle = new SettleCellsJob { Source = source, Flora = flora.Blob, Cells = settleCells, Spots = settleSpots, Ends = settleEnds }.Schedule();
            settling = true;
        }

        /// <summary>Finishes any settling job and frees its lists.</summary>
        void EndSettling()
        {
            if (!settling)
                return;
            settleHandle.Complete();
            settleCells.Dispose();
            settleSpots.Dispose();
            settleEnds.Dispose();
            settling = false;
        }

        void SpawnCritters(in TerrainSource source)
        {
            if (critterTable == null || simWorld == null || species.Count == 0)
                return;
            var em = simWorld.EntityManager;
            if (floraQuery.IsEmpty)
                return;
            var flora = floraQuery.GetSingleton<Punchy.Flora.FloraLookup>();
            if (!flora.Blob.IsCreated)
                return;
            // (Spots whose creature has gone: free again.)
            var gone = new List<uint>();
            foreach (var kv in critters)
                if (!em.Exists(kv.Value))
                    gone.Add(kv.Key);
            foreach (var id in gone)
                critters.Remove(id);
            var night = Cycle != null ? Cycle.Night : 0f;
            var share = math.max(24, MaxCritters / math.max(players.Count, 1));
            const float cell = 16f;
            // (A new world: none of the old spots hold.)
            if (!critterSource.Equals(source))
            {
                critterCells.Clear();
                critterSource = source;
            }
            if (critterCells.Count > 600)
            {
                critterCellsGone.Clear();
                foreach (var key in critterCells.Keys)
                    if (NearestPlayer(((float2)key + 0.5f) * cell) > CritterLetGo + cell * 2f)
                        critterCellsGone.Add(key);
                foreach (var key in critterCellsGone)
                    critterCells.Remove(key);
            }
            foreach (var player in players)
            {
                if (critterCount >= MaxCritters)
                    return;
                // How many this player already has round it.
                var mine = 0;
                foreach (var kv in critters)
                    if (em.HasComponent<CreatureState>(kv.Value) &&
                        math.distance(em.GetComponentData<CreatureState>(kv.Value).Position.xz, player.xz) < CritterRadius)
                        mine++;
                if (mine >= share)
                    continue;
                critterSpots.Clear();
                var min = (int2)math.floor((player.xz - CritterRadius) / cell);
                var max = (int2)math.floor((player.xz + CritterRadius) / cell);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var key = new int2(x, y);
                    // (Cells not worked out yet are settled a few a frame by SettleCritterCells.)
                    if (critterCells.TryGetValue(key, out var spots))
                        critterSpots.AddRange(spots);
                }
                critterSpots.Sort((a, b) => math.distancesq(a.Position.xz, player.xz).CompareTo(math.distancesq(b.Position.xz, player.xz)));
                foreach (var spot in critterSpots)
                {
                    if (mine >= share || critterCount >= MaxCritters)
                        break;
                    var d = math.distance(spot.Position.xz, player.xz);
                    if (d > CritterRadius || d < 6f || critters.ContainsKey(spot.Id) || pendingCritterSpots.Contains(spot.Id))
                        continue;
                    var kind = spot.Kind;
                    // (Owls and frogs come out at night; songbirds and woodpeckers by day; mice either.)
                    var day = night < 0.4f;
                    if ((kind == AmbientLife.CritterKind.Owl || kind == AmbientLife.CritterKind.Frog) == day && kind != AmbientLife.CritterKind.Mouse)
                        continue;
                    var sp = kind switch
                    {
                        AmbientLife.CritterKind.Songbird => critterTable.Songbird,
                        AmbientLife.CritterKind.Woodpecker => critterTable.Woodpecker,
                        AmbientLife.CritterKind.Owl => critterTable.Owl,
                        AmbientLife.CritterKind.Frog => critterTable.Frog,
                        _ => critterTable.Mouse
                    };
                    var index = SpeciesIndex(sp);
                    if (index < 0)
                        continue;
                    var at = spot.Position;
                    var id = spot.Id;
                    var leash = kind == AmbientLife.CritterKind.Songbird ? 18f : 8f;
                    var timer = rng.NextFloat(4f, 15f);
                    pendingCritterSpots.Add(id);
                    Enqueue(index, at, SpawnTier.Critter, e =>
                    {
                        var em = simWorld.EntityManager;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Position = at;
                        state.Task = CreatureTask.Critter;
                        state.Home = at.xz;
                        state.Leash = leash;
                        // (Settled on its perch a while before it does anything.)
                        state.Mode = CreatureMode.Rest;
                        state.Timer = timer;
                        em.SetComponentData(e, state);
                        critters[id] = e;
                        pendingCritterSpots.Remove(id);
                    }, rng.NextFloat(6.28f));
                    mine++;
                }
            }
        }

        /// <summary>The settlement's domestic animals, about its middle, homed on it and leashed to it.</summary>
        void SpawnDomestics(in TerrainSource source, LandmarkType settlement, float2 centre, float radius)
        {
            if (domestics == null)
                return;
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
                var leash = d.Task == CreatureTask.Hunt || d.Task == CreatureTask.Fish || d.Task == CreatureTask.Mine ? math.max(radius * 3f, 70f) : radius * d.Range;
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
                    var task = d.Task;
                    Enqueue(index, new float3(p.x, lava ? g.WaterSurface - 0.3f : g.Height, p.y), SpawnTier.Settled, e =>
                    {
                        var em = simWorld.EntityManager;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Home = centre;
                        state.Leash = leash;
                        state.Task = task;
                        em.SetComponentData(e, state);
                    });
                }
            }
        }

        /// <summary>
        /// Cave dwellers appear along the inner passages of a cave when a player comes within Radius of its mouth, a
        /// few of each kind; like residents, they're forgotten once everyone is well away.
        /// </summary>
        readonly List<int> caveGlows = new List<int>();

        /// <summary>The Work anchor nearest a point within a radius: where a worker stands, and the way it faces.</summary>
        static bool WorkAnchorNear(ref AnchorSetBlob set, float3 at, float radius, out float2 stand, out float2 facing)
        {
            stand = facing = float2.zero;
            var near = new NativeList<int>(4, Allocator.Temp);
            Anchors.Near(ref set, at, radius, AnchorKind.Work, ref near);
            var best = float.MaxValue;
            foreach (var i in near)
            {
                var d = math.distancesq(set.Anchors[i].Position, at);
                if (d < best)
                {
                    best = d;
                    stand = set.Anchors[i].Position.xz;
                    facing = set.Anchors[i].Facing;
                }
            }
            near.Dispose();
            return best < float.MaxValue;
        }

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
                caveGlows.Clear();
                var glowsFound = false;
                for (int sp = 0; sp < species.Count && caveCount < MaxCave; sp++)
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
                    // Moths: a few round each of the cave's glowing plants (none in a cave without any): half a group
                    // each, a cave having so many of them.
                    if (body.DrawnToLight)
                    {
                        count = math.max(1, count / 2);
                        if (!source.Anchors.IsCreated)
                            continue;
                        ref var anchors = ref source.Anchors.Value;
                        // (This cave's glowing plants, looked up once for all the kinds drawn to them.)
                        if (!glowsFound)
                        {
                            glowsFound = true;
                            for (int a = 0; a < anchors.Anchors.Length; a++)
                                if (anchors.Anchors[a].Kind == AnchorKind.Glow && anchors.Anchors[a].InCave && anchors.Anchors[a].OwnerIndex == i)
                                    caveGlows.Add(a);
                        }
                        foreach (var a in caveGlows)
                        {
                            var glow = anchors.Anchors[a];
                            // (The cap holds within a cave too: a big cave has hundreds of glowing plants.)
                            for (int k = 0; k < count && caveCount < MaxCave; k++)
                                Enqueue(sp, glow.Position + new float3(rng.NextFloat(-0.6f, 0.6f), rng.NextFloat(0.4f, 1.4f), rng.NextFloat(-0.6f, 0.6f)), SpawnTier.Cave);
                        }
                        continue;
                    }
                    for (int k = 0; k < count && caveCount < MaxCave; k++)
                    {
                        // On the floor somewhere in the inner two thirds of the passage (a visitor, the outer half).
                        var node = set.Nodes[cave.FirstNode + (visitor ? rng.NextInt(1, math.max(2, cave.NodeCount / 2))
                                                                       : rng.NextInt(cave.NodeCount / 3, cave.NodeCount))];
                        var at = node.Position + new float3(rng.NextFloat(-0.5f, 0.5f), 0f, rng.NextFloat(-0.5f, 0.5f)) * node.Radius;
                        var floor = source.FloorUnder(at, out var inCave);
                        // (Really under the rock: not where a passage opens to the sky.)
                        if (inCave && source.CaveDistance(new float3(at.x, floor + 0.3f, at.z)) < -0.05f)
                            Enqueue(sp, new float3(at.x, floor, at.z), visitor ? SpawnTier.Wild : SpawnTier.Cave);
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
            Entity e;
            if (simWorld.IsServer() && ghostSpawnerQuery.TryGetSingleton<CreatureGhostSpawner>(out var spawner))
            {
                e = em.Instantiate(spawner.Prefab);
                // (Both at once: one move to the new archetype, not two.)
                em.AddComponent(e, new ComponentTypeSet(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>()));
            }
            else
                e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody));
            Init(e, speciesIndex, position, yaw);
            return e;
        }

        /// <summary>
        /// Puts an animal in the queue to be made (DrainSpawns, a few a frame), counted at once towards its tier's cap;
        /// then, once it exists, runs what sets it up (home, leash, task...).
        /// </summary>
        void Enqueue(int speciesIndex, float3 position, SpawnTier tier, System.Action<Entity> then = null, float yaw = float.NaN)
        {
            pendingSpawns.Enqueue(new PendingSpawn { Species = speciesIndex, Position = position, Yaw = yaw, Tier = tier, Then = then });
            pendingTiers[(int)tier]++;
            switch (tier)
            {
                case SpawnTier.Settled: settledCount++; break;
                case SpawnTier.Cave: caveCount++; break;
                case SpawnTier.Critter: critterCount++; break;
                default: wildCount++; break;
            }
        }

        /// <summary>
        /// Makes up to SpawnsPerFrame waiting animals, together: one batch of entities (ghosts on a server) given their
        /// creature components in one go, rather than two structural changes each.
        /// </summary>
        void DrainSpawns()
        {
            var n = math.min(pendingSpawns.Count, math.max(SpawnsPerFrame, 1));
            if (n == 0)
                return;
            var em = simWorld.EntityManager;
            NativeArray<Entity> made;
            if (simWorld.IsServer() && ghostSpawnerQuery.TryGetSingleton<CreatureGhostSpawner>(out var spawner))
            {
                made = em.Instantiate(spawner.Prefab, n, Allocator.Temp);
                em.AddComponent(made, new ComponentTypeSet(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>()));
            }
            else
            {
                if (!plainArchetype.Valid)
                    plainArchetype = em.CreateArchetype(typeof(CreatureState), typeof(CreatureBody));
                made = em.CreateEntity(plainArchetype, n, Allocator.Temp);
            }
            for (int i = 0; i < n; i++)
            {
                var p = pendingSpawns.Dequeue();
                pendingTiers[(int)p.Tier]--;
                Init(made[i], p.Species, p.Position, p.Yaw);
                p.Then?.Invoke(made[i]);
            }
            made.Dispose();
        }

        /// <summary>A new animal's body and starting state, at a point.</summary>
        void Init(Entity e, int speciesIndex, float3 position, float yaw)
        {
            var em = simWorld.EntityManager;
            ref var b = ref blobs[speciesIndex].Value;
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
