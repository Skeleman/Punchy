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
    public partial class FaunaDirector : MonoBehaviour
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
        /// <summary>The world that simulates animals (the server's, when hosting); null before the director is up.</summary>
        public World SimWorld => simWorld;
        /// <summary>Counts the director's builds; a rebuild (terrain re-baked) destroys every animal, so dungeons refill theirs (#24).</summary>
        public int Builds { get; private set; }
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
        BlobAssetReference<WharfSpotSet> wharfBlob;
        /// <summary>A-73: the gull spots on the wharves' rail posts, by critter cell (WharfSpots.GullSpots).</summary>
        readonly Dictionary<int2, List<AmbientLife.CritterSpot>> gullCells = new Dictionary<int2, List<AmbientLife.CritterSpot>>();
        readonly List<Punchy.Flora.FloraSpecies> floraSpecies = new List<Punchy.Flora.FloraSpecies>();
        /// <summary>The plant placement animals use (where a machine simulates them), and its species in rule order (tests).</summary>
        public BlobAssetReference<Punchy.Flora.FloraBlob> FloraPlacementBlob => floraBlob;
        public IReadOnlyList<Punchy.Flora.FloraSpecies> FloraSpeciesList => floraSpecies;
        Mesh capsule, capsuleMid, capsuleFar;
        float spawnBudget;
        bool built;
        // (The flora lookup's shape-only grows, started on workers before Build, #102.)
        Dictionary<(Punchy.Flora.FloraSpecies, int), System.Threading.Tasks.Task<Punchy.Flora.FloraModel>> pendingShapes;
        float shapesSince;
        int shapeFrames;
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
        /// <summary>Which critter spots are taken, by whom (the species come from the overworld spawn table).</summary>
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
            if (wharfBlob.IsCreated)
                wharfBlob.Dispose();
            wharfBlob = default;
            gullCells.Clear();
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
            CreatureSnapshotSystem.Forget();
            foreach (var b in blobs)
                if (b.IsCreated) b.Dispose();
            blobs.Clear();
            species.Clear();
            speciesByName.Clear();
            occupied.Clear();
            occupiedCaves.Clear();
            occupiedChambers.Clear();
            Caught = 0;
            if (capsule != null) Destroy(capsule);
            if (capsuleMid != null) Destroy(capsuleMid);
            if (capsuleFar != null) Destroy(capsuleFar);
            capsule = capsuleMid = capsuleFar = null;
            built = false;
            pendingShapes = null;
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
            {
                // (The build frame's whole cost, Build and the first spawn passes, for the startup hitch, #102.)
                var wasBuilt = built;
                var watch = wasBuilt ? null : System.Diagnostics.Stopwatch.StartNew();
                UpdateDirector();
                if (!wasBuilt && built)
                    Debug.Log($"[fauna build] build frame {watch.ElapsedMilliseconds} ms");
            }
        }

        void UpdateDirector()
        {
            if (Material == null || !TryGetWorld(out var source, out var palette))
                return;
            // Rebuild when the terrain is re-baked or (in the editor) a species or limb type is edited.
            if (built && (!source.Equals(lastSource) || ConfigChanged()))
                Teardown();
            if (!built)
            {
                // A-55 (#102): the flora lookup's shape-only grows were ~0.6 s of the island-ready frame. Start them on
                // workers and build once they're done (a few frames later; nothing spawns before), or after 10 s regardless.
                // (-syncshapes: the old way, all in the build frame, for A/B.)
                if (simWorld != null && pendingShapes == null && System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-syncshapes") < 0)
                {
                    pendingShapes = Punchy.Flora.FloraPlacement.StartObstacleShapes(palette);
                    shapesSince = Time.realtimeSinceStartup;
                    shapeFrames = 0;
                }
                if (pendingShapes != null && Time.realtimeSinceStartup - shapesSince < 10f)
                {
                    shapeFrames++;
                    foreach (var task in pendingShapes.Values)
                        if (!task.IsCompleted)
                            return;
                }
                Build(source, palette);
            }
            KeepAvatar();
            GiveReplicasBodies();

            var clock = new FaunaClock { Night = Cycle != null ? Cycle.Night : 0f, Hour = Cycle != null ? Cycle.TimeOfDay : 12f };
            queryWorld.EntityManager.SetComponentData(settingsEntity, clock);
            Comfort.Hour.Data = new float2(clock.Hour, Cycle != null ? 1f : 0f);
            if (simWorld == null)
                return;
            if (simWorld != queryWorld)
                simWorld.EntityManager.SetComponentData(simClockEntity, clock);

            GatherPlayers();
            Comfort.Refresh(players, source);
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
                    SpawnChamberBands(source);
                    SpawnWildFolk(source);
                    SpawnCrossingFishers(source);
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
                // (A-44: not over a cave entrance's carved ground, where the heightmap would leave it in the air.)
                var land = s.Habitat == Habitat.Land || s.Habitat == Habitat.Flies;
                if (land && g.Height - CreatureMotion.Ground(source, p, g.Height) > 0.2f)
                    continue;
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
                em.AddComponent(e, new ComponentTypeSet(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>(), ComponentType.ReadWrite<CreatureMind>()));
            }
            else
                e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody), typeof(CreatureMind));
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
                em.AddComponent(made, new ComponentTypeSet(ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadWrite<CreatureBody>(), ComponentType.ReadWrite<CreatureMind>()));
            }
            else
            {
                if (!plainArchetype.Valid)
                    plainArchetype = em.CreateArchetype(typeof(CreatureState), typeof(CreatureBody), typeof(CreatureMind));
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
            // (Drawn in the same order as before the needs moved to CreatureMind (#58), so spawns stay the same.)
            var startYaw = float.IsNaN(yaw) ? rng.NextFloat(-math.PI, math.PI) : yaw;
            var phase = rng.NextFloat();
            var scale = rng.NextFloat(b.Size.x, math.max(b.Size.x, b.Size.y));
            var timer = rng.NextFloat(0.5f, 4f);
            // Needs start part-way, so a new arrival doesn't do everything at once.
            var mind = new CreatureMind
            {
                Hunger = rng.NextFloat(0.1f, 0.7f),
                Thirst = rng.NextFloat(0.1f, 0.6f),
                Tired = rng.NextFloat(0f, 0.5f),
                Lonely = rng.NextFloat(0f, 0.5f)
            };
            em.SetComponentData(e, new CreatureState
            {
                Position = position,
                Yaw = startYaw,
                Phase = phase,
                Scale = scale,
                Mode = CreatureMode.Idle,
                Timer = timer,
                LairCave = -1,
                Seed = rng.NextUInt() | 1u,
                Individual = rng.NextUInt() | 1u
            });
            if (em.HasComponent<CreatureMind>(e))
                em.SetComponentData(e, mind);
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
