using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Two still stone giants (#40, after the Dunes colossus): a kneeling titan, head bowed over a planted sword, in the
    /// biggest cold region (Tundra, Glacier or Mountains), snow lying on everything that faces up; and a half-buried head,
    /// tilted, crown broken, in the biggest green region (Grassland or Temperate), moss on its brow and rubble round it.
    /// Built in code from the colossus mesh builder and placed like DunesColossus, from the terrain and world seed only
    /// (a seeded pick among the most inland dry cells of the region, kept 250 m off settlements), so they add no landmark
    /// data and move nothing else. "-nocolossus" hides them with the Dunes colossus.
    /// </summary>
    public class StoneColossi : MonoBehaviour
    {
        /// <summary>
        /// W-86 added a basalt smith hunched over his anvil (Ashlands, Volcano or EmberForest). W-78 added a moss-backed giant
        /// tortoise (FungalForest) and a crystal-spined serpent (ElectricWastelands). W-88 (#40, Zach's designs) put a giant
        /// carnivorous plant in the PoisonSwamp and a skeletal lizard shedding ash in the Ashlands in the last two slots; the
        /// drowned knight and the smith are kept whole for later and come back with "-colossus DrownedKnight" / "-colossus AshSmith".
        /// </summary>
        // W-89 (#40, Zach's designs): a golem of floating rocks held together by arcs takes the ElectricWastelands slot
        // (the crystal serpent comes back with "-colossus CrystalSerpent"), and a seventh slot holds a walking saguaro in
        // the Sonoran.
        public enum Kind { KneelingTitan, HalfBuriedHead, GiantTortoise, CrystalSerpent, DrownedKnight, AshSmith, CarnivorousPlant, SkeletalLizard, RockGolem, WalkingSaguaro, SaltCrystal, MesaTortoise, GlassHeron, LanternStag, CinderSnail, MireWalker }
        // W-92 (#40, Zach's SaltFlat design): an eighth slot holds a giant rounded salt crystal rolling in slow circles.
        // W-106 row 2 (#40): a ninth slot holds a rock tortoise in the MesaCanyonlands that reads as a small mesa.
        // A-76 (#40): a tenth holds a glass heron wading the GlassDesert, an eleventh a lantern stag in the SparklingForest.
        // A-78 (#40): a twelfth holds a cinder snail grazing the EmberForest, the biggest biome that had none.
        // A-79 (#40): a thirteenth holds a mire walker creeping through the FoggyMarsh on its prop roots.
        /// <summary>Slots: one colossus stands in each.</summary>
        const int Kinds = 13;

        /// <summary>What stands in each slot. Slots 3, 4 and 5 hold the golem, the plant and the lizard unless a "-colossus" flag swaps the old kind back in.</summary>
        public static readonly Kind[] Placed = { Kind.KneelingTitan, Kind.HalfBuriedHead, Kind.GiantTortoise, Kind.RockGolem, Kind.CarnivorousPlant, Kind.SkeletalLizard, Kind.WalkingSaguaro, Kind.SaltCrystal, Kind.MesaTortoise, Kind.GlassHeron, Kind.LanternStag, Kind.CinderSnail, Kind.MireWalker };

        /// <summary>Where each slot's colossus stands (NaN until placed), for probes and shot lists.</summary>
        public static readonly float3[] Positions = { new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN) };

        /// <summary>A-72 hook: the seeded spot each slot was placed on (NaN until placed). <see cref="Positions"/> follows
        /// a colossus that walks (<see cref="ColossusWalker"/>); this keeps the spot its loop is centred on.</summary>
        public static readonly float3[] Spots = { new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN) };

        /// <summary>A-72 hook: the root a slot's colossus was built under (null until placed), for <see cref="ColossusWalker"/>.</summary>
        public Transform Root(int slot) => roots[slot];

        const float Step = 150f;
        /// <summary>The biomes each kind looks for, by <see cref="Kind"/>.</summary>
        static readonly string[][] Biomes =
        {
            new[] { "Tundra", "Glacier", "Mountains" }, new[] { "Grassland", "Temperate" }, new[] { "FungalForest" }, new[] { "ElectricWastelands" },
            new[] { "FoggyMarsh", "PoisonSwamp" }, new[] { "Ashlands", "Volcano", "EmberForest" }, new[] { "PoisonSwamp" }, new[] { "Ashlands" },
            new[] { "ElectricWastelands" }, new[] { "Sonoran" }, new[] { "SaltFlat" }, new[] { "MesaCanyonlands" }, new[] { "GlassDesert" },
            new[] { "SparklingForest" }, new[] { "EmberForest" }, new[] { "FoggyMarsh" },
        };
        // Each slot draws from its own salt on the world seed, so adding one moves no other colossus or landmark.
        static readonly uint[] Salts = { 0x71A4u, 0x4EADu, 0x7027u, 0x5E2Bu, 0xD20Au, 0xA54Du, 0x5A6Au, 0x5A17u, 0x3E5Au, 0x6E70u, 0x57A6u, 0xC1D5u, 0xF06Bu };
        static readonly float[] KeepRadius = { 34f, 42f, 38f, 44f, 44f, 46f, 34f, CrystalKeep, 34f, 26f, 30f, 34f, 36f };
        /// <summary>Height and half-width each slot casts with its far shadow (#84); the last entry is the Dunes colossus.</summary>
        static readonly float2[] Casts = { new float2(46f, 14f), new float2(22f, 12f), new float2(30f, 20f), new float2(52f, 14f), new float2(40f, 18f), new float2(18f, 20f), new float2(56f, 12f), new float2(2f * CrystalBall, CrystalBall), new float2(24f, 20f), new float2(32f, 8f), new float2(34f, 14f), new float2(36f, 16f), new float2(44f, 18f), new float2(58f, 20f) };
        /// <summary>
        /// W-89 (#40): the most relief (highest minus lowest ground, m) a colossus spot may have across its keep circle.
        /// A spot steeper than this is redrawn (up to eight times, then the flattest draw wins), so no colossus stands
        /// on a hillside with its feet hanging over the drop. A spot already flat enough keeps its first draw.
        /// </summary>
        const float MaxRelief = 6f;
        /// <summary>W-92 (#40): the salt crystal's radius (m), the most its rolling circle may span, and the keep circle
        /// round the whole circle (flora out of its path; the slope check uses 0.7 of it, like the others).</summary>
        const float CrystalBall = 13f, CrystalCircleMax = 60f, CrystalKeep = CrystalCircleMax + CrystalBall + 5f;

        static readonly Color Stone = new Color(0.50f, 0.50f, 0.49f);
        static readonly Color Dark = new Color(0.36f, 0.36f, 0.36f);
        static readonly Color Hollow = new Color(0.10f, 0.10f, 0.11f);
        static readonly Color Rust = new Color(0.40f, 0.30f, 0.24f);
        static readonly Color Snow = new Color(0.90f, 0.92f, 0.95f);
        static readonly Color Moss = new Color(0.30f, 0.42f, 0.18f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindAnyObjectByType<StoneColossi>() != null)
                return;
            new GameObject("StoneColossi").AddComponent<StoneColossi>();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        readonly Transform[] roots = new Transform[Kinds];
        readonly FarShadow[] shadows = new FarShadow[Kinds + 1];
        // A-72 hook: where each far shadow was last laid, so a walking colossus gets a fresh strip once it has moved.
        readonly float3[] laidAt = new float3[Kinds + 1];
        bool placed;
        float nextTry;

        void Update()
        {
            if (!placed)
            {
                if (Time.realtimeSinceStartup >= nextTry)
                {
                    nextTry = Time.realtimeSinceStartup + 1f;
                    TryPlace();
                }
                return;
            }
            foreach (var r in roots)
                if (r != null && r.gameObject.activeSelf == DunesColossus.Hidden)
                    r.gameObject.SetActive(!DunesColossus.Hidden);
            // W-90 (#84): time the frames that schedule a re-lay and the frame after (which lands it), to show the
            // stall is gone. Logged for the first few re-lays only.
            int before = FarShadow.LaysScheduled;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            DrawFarShadows();
            bool laid = FarShadow.LaysScheduled != before;
            if ((laid || landing) && relayLogs < 12)
            {
                relayLogs++;
                Debug.Log($"[farshadow] {(laid ? "schedule" : "land")} frame: StoneColossi far shadows {watch.Elapsed.TotalMilliseconds:F2} ms");
            }
            landing = laid;
            GlowLanterns();
            SwayPlant();
        }

        bool landing;
        int relayLogs;

        /// <summary>
        /// Past the real shadow distance every colossus (these and the Dunes one) still casts, through a far shadow
        /// proxy (#84, as the salt tower). The terrain is fetched each frame so a rebaked island is never sampled stale.
        /// </summary>
        void DrawFarShadows()
        {
            if (DunesColossus.Hidden)
                return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            using var terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!terrainQuery.TryGetSingleton(out TerrainSource source))
                return;
            for (int k = 0; k <= Kinds; k++)
            {
                var at = k < Kinds ? Positions[k] : DunesColossus.Position;
                if (k < Kinds ? roots[k] == null : math.all(at == 0f))
                    continue;
                // A-72 hook: the strip only re-lays when the sun moves, so one left 10 m behind a walker is laid anew.
                if (shadows[k] != null && math.distancesq(laidAt[k].xz, ((float3)at).xz) > 100f)
                {
                    shadows[k].Dispose();
                    shadows[k] = null;
                }
                if (shadows[k] == null)
                    laidAt[k] = at;
                shadows[k] ??= new FarShadow(k < Kinds ? $"{Placed[k]} far shadow" : "DunesColossus far shadow");
                // W-91 (#40): the carnivorous plant is drawn smaller than its old units; A-77 doubled it (about 22 m, 16 m across).
                var cast = k < Kinds && Placed[k] == Kind.CarnivorousPlant ? new float2(22f, 16f) : Casts[k];
                shadows[k].Draw(source, at, cast.x, cast.y);
            }
        }

        void OnDestroy()
        {
            foreach (var s in shadows)
                s?.Dispose();
        }

        void TryPlace()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!terrainQuery.TryGetSingleton(out TerrainSource source) || paletteQuery.CalculateEntityCount() != 1 || !source.Settlements.IsCreated)
                return;
            // #40 (W-59d): on island v2, wait for the launch bake. The editor's subscene holds a v1 bake (settlements
            // and all) for a few frames before IslandV2System swaps the v2 island in, and placing against that put the
            // editor's colossi on other spots (the head 26 m under the v2 ground). The player only ever saw v2.
            if (IslandV2.Requested() && !source.Island.IsCreated)
                return;
            var palette = em.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());

            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            var grid = new NativeArray<int>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = Step, Size = n, Biome = grid }.Schedule(n * n, 64).Complete();
            var biomes = grid.ToArray();
            grid.Dispose();
            placed = true;
            Lanterns.Clear();
            // "-colossus AshSmith" (or DrownedKnight) puts a kept kind back in the slot it used to stand in.
            // Reset first: statics live on between editor play runs.
            Placed[3] = Kind.RockGolem;
            Placed[4] = Kind.CarnivorousPlant;
            Placed[5] = Kind.SkeletalLizard;
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-colossus" && System.Enum.TryParse<Kind>(args[i + 1], true, out var swap))
                {
                    if (swap == Kind.CrystalSerpent) Placed[3] = swap;
                    if (swap == Kind.DrownedKnight) Placed[4] = swap;
                    if (swap == Kind.AshSmith) Placed[5] = swap;
                }
            for (int kind = 0; kind < Kinds; kind++)
            {
                ColossusCollision.Clear(kind);
                var wanted = new HashSet<int>();
                for (int b = 0; b < palette.Biomes.Length; b++)
                    if (palette.Biomes[b] != null)
                        foreach (var name in Biomes[(int)Placed[kind]])
                            if (palette.Biomes[b].name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase))
                                wanted.Add(b);
                if (!Pick(source, biomes, n, half, wanted, Salts[kind], KeepRadius[kind] * 0.7f, out var spot, out var yaw, out var cells))
                {
                    Debug.Log($"StoneColossi: no room for a {Placed[kind]} in this world, none placed");
                    continue;
                }
                var ground = source.SampleHeight(spot);
                Positions[kind] = new float3(spot.x, ground, spot.y);
                Spots[kind] = Positions[kind];
                // Keep flora out of a circle round it and re-place the streamed tiles, so no tree grows through it (#40).
                // The head's circle is wide enough (W-59d) to leave a clear sight line from about 40 m away.
                // Kinds past the fourth go in a second static (W-85), past the eighth in a third (W-106, four circles: room up to slot 11): a live editor
                // can't resize one it already made.
                var circle = new float4(spot.x, spot.y, KeepRadius[kind], 1f);
                if (kind < 4)
                {
                    var keep = FloraPlacement.KeepOut.Data;
                    keep[kind] = circle;
                    FloraPlacement.KeepOut.Data = keep;
                }
                else if (kind >= 12)
                {
                    // A-79: past the twelfth, a fourth static (four more circles: room up to slot 15).
                    var keep = FloraPlacement.KeepOutFinal.Data;
                    keep[kind - 12] = circle;
                    FloraPlacement.KeepOutFinal.Data = keep;
                }
                else if (kind >= 8)
                {
                    var keep = FloraPlacement.KeepOutLast.Data;
                    keep[kind - 8] = circle;
                    FloraPlacement.KeepOutLast.Data = keep;
                }
                else
                {
                    var keep = FloraPlacement.KeepOutMore.Data;
                    keep[kind - 4] = circle;
                    FloraPlacement.KeepOutMore.Data = keep;
                }
                FloraSystem.Replant = true;
                roots[kind] = Placed[kind] switch
                {
                    Kind.KneelingTitan => BuildTitan(Positions[kind], yaw),
                    Kind.HalfBuriedHead => BuildHead(Positions[kind], yaw),
                    Kind.GiantTortoise => BuildTortoise(Positions[kind], yaw),
                    Kind.CrystalSerpent => BuildSerpent(Positions[kind], yaw),
                    Kind.DrownedKnight => BuildKnight(Positions[kind], yaw),
                    Kind.AshSmith => BuildSmith(Positions[kind], yaw),
                    Kind.CarnivorousPlant => BuildPlant(Positions[kind], yaw),
                    Kind.RockGolem => BuildGolem(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.WalkingSaguaro => BuildSaguaro(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.SaltCrystal => BuildCrystal(source, Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.MesaTortoise => BuildMesaTortoise(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.GlassHeron => BuildGlassHeron(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.LanternStag => BuildLanternStag(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.CinderSnail => BuildCinderSnail(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    Kind.MireWalker => BuildMireWalker(Positions[kind], yaw, (uint)source.World.Seed ^ Salts[kind]),
                    _ => BuildLizard(Positions[kind], yaw),
                };
                // W-101 row 4 (#40): solid to the player and the creatures. The salt ball follows its roll; the crumbs
                // beside its track are left to step over.
                if (Placed[kind] == Kind.SaltCrystal)
                    ColossusCollision.Register(kind, roots[kind], Placed[kind].ToString(), roots[kind].Find("Ball"), CrystalBall * 1.1f, "Crumbs");
                else if (Placed[kind] == Kind.GlassHeron)
                    // A-77 (item 22): its shard litter stays on the ground where it was laid (ColossusWalker detaches it).
                    ColossusCollision.Register(kind, roots[kind], Placed[kind].ToString(), null, 0f, "Litter");
                else
                    ColossusCollision.Register(kind, roots[kind], Placed[kind].ToString());
                Debug.Log($"StoneColossi: {Placed[kind]} placed at ({spot.x:F0}, {ground:F1}, {spot.y:F0}), facing {yaw:F0} degrees, region {cells} cells, relief {Relief(source, spot, KeepRadius[kind] * 0.7f):F1} m");
            }
        }

        /// <summary>The biggest 4-connected region of <paramref name="wanted"/> cells, then a seeded pick among its most inland cells clear of settlements.</summary>
        static bool Pick(in TerrainSource source, int[] grid, int n, float half, HashSet<int> wanted, uint salt, float radius, out float2 spot, out float yaw, out int cells)
        {
            spot = default; yaw = 0f; cells = 0;
            var region = new int[n * n];
            var sizes = new List<int> { 0 };
            var queue = new Queue<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (!wanted.Contains(grid[i]) || region[i] != 0)
                    continue;
                var id = sizes.Count;
                sizes.Add(0);
                region[i] = id;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    sizes[id]++;
                    int x = c % n, z = c / n;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n)
                            continue;
                        var j = nz * n + nx;
                        if (wanted.Contains(grid[j]) && region[j] == 0) { region[j] = id; queue.Enqueue(j); }
                    }
                }
            }
            var biggest = 0;
            for (int id = 1; id < sizes.Count; id++)
                if (sizes[id] > sizes[biggest]) biggest = id;
            if (biggest == 0)
                return false;
            cells = sizes[biggest];

            ref var towns = ref source.Settlements.Value;
            int At(int x, int z) => x < 0 || z < 0 || x >= n || z >= n ? 0 : region[z * n + x];
            var scores = new int[n * n];
            var best = -1;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (At(x, z) != biggest)
                    continue;
                var p = new float2(-half + x * Step, -half + z * Step);
                var clear = true;
                for (int s = 0; s < towns.Settlements.Length && clear; s++)
                    clear = math.distance(p, towns.Settlements[s].Centre) > towns.Settlements[s].Radius + 250f;
                if (!clear)
                {
                    scores[z * n + x] = -1;
                    continue;
                }
                var score = 0;
                for (int r = 1; r <= 12; r++, score++)
                {
                    var d = (int)math.round(r * 0.707f);
                    if (At(x + r, z) != biggest || At(x - r, z) != biggest || At(x, z + r) != biggest || At(x, z - r) != biggest ||
                        At(x + d, z + d) != biggest || At(x - d, z + d) != biggest || At(x + d, z - d) != biggest || At(x - d, z - d) != biggest)
                        break;
                }
                scores[z * n + x] = score;
                best = math.max(best, score);
            }
            if (best < 0)
                return false;
            var candidates = new List<int>();
            for (int i = 0; i < scores.Length; i++)
                if (At(i % n, i / n) == biggest && scores[i] >= best - 1)
                    candidates.Add(i);
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)source.World.Seed ^ salt);
            // The first draw is the pre-W-89 spot; only a steep one draws again (MaxRelief).
            var flattest = float.MaxValue;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var pick = candidates[rng.NextInt(candidates.Count)];
                var at = new float2(-half + pick % n * Step, -half + pick / n * Step) + rng.NextFloat2(-Step * 0.3f, Step * 0.3f);
                var relief = Relief(source, at, radius);
                if (relief < flattest) { flattest = relief; spot = at; }
                if (relief <= MaxRelief)
                    break;
            }
            yaw = rng.NextFloat(0f, 360f);
            return true;
        }

        /// <summary>Highest minus lowest ground at the centre and twelve points round a circle of <paramref name="radius"/>.</summary>
        static float Relief(in TerrainSource source, float2 at, float radius)
        {
            float lo = source.SampleHeight(at), hi = lo;
            for (int k = 0; k < 12; k++)
            {
                var a = k * math.PI / 6f;
                var h = source.SampleHeight(at + new float2(math.cos(a), math.sin(a)) * radius);
                lo = math.min(lo, h); hi = math.max(hi, h);
            }
            return hi - lo;
        }

        [BurstCompile]
        struct SurveyJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Step;
            public int Size;
            [WriteOnly] public NativeArray<int> Biome;

            public void Execute(int i)
            {
                var g = Source.SampleGround(new float2(Origin + i % Size * Step, Origin + i / Size * Step));
                Biome[i] = g.WaterDepth < -1f ? g.Biomes.Dominant() : -1;
            }
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// About 50 m kneeling: the left knee down, the right leg up with the right forearm resting on its knee, the left
        /// hand on the pommel of a sword planted point-down in front, the head bowed towards it. Sunk a couple of metres so
        /// slopes don't show daylight under it.
        /// </summary>
        static Transform BuildTitan(float3 at, float yaw)
        {
            var root = new GameObject("KneelingTitan").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            // Legs: left shin flat on the ground behind, right thigh forward and shin upright.
            m.Limb(new Vector3(-7f, 16f, -2f), new Vector3(-8f, 3f, 6f), 5.2f, 4.4f, Stone);      // left thigh down to the knee
            m.Limb(new Vector3(-8f, 3f, 6f), new Vector3(-8f, 2.5f, -12f), 4.2f, 3.2f, Dark);     // left shin back along the ground
            m.Ellipsoid(new Vector3(-8f, 2f, -15f), new Vector3(3f, 2.2f, 5f), Quaternion.Euler(0f, 0f, 0f), Stone);
            m.Limb(new Vector3(7f, 16f, -1f), new Vector3(8f, 18f, 14f), 5.2f, 4.4f, Stone);      // right thigh forward
            m.Limb(new Vector3(8f, 18f, 14f), new Vector3(8f, 2f, 15f), 4.2f, 3.4f, Dark);        // right shin down
            m.Ellipsoid(new Vector3(8f, 1.5f, 18f), new Vector3(3.2f, 2f, 5.5f), Quaternion.identity, Stone);
            // Hips, torso leaning forward, shoulders.
            var lean = Quaternion.Euler(16f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 17f, -1f), new Vector3(10f, 6f, 7f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(0f, 27f, 1f), new Vector3(10.5f, 12f, 7.5f), lean, Stone);
            m.Ellipsoid(new Vector3(0f, 36f, 4f), new Vector3(15f, 4.5f, 7f), lean, Dark);
            m.Ellipsoid(new Vector3(0f, 26f, 7f), new Vector3(7f, 6f, 2.5f), lean, Dark);           // breastplate ridge
            // Right arm on the raised knee.
            m.Limb(new Vector3(13f, 36f, 4f), new Vector3(14f, 25f, 11f), 3.8f, 3.2f, Stone);
            m.Limb(new Vector3(14f, 25f, 11f), new Vector3(9f, 20f, 19f), 3.2f, 2.8f, Stone);
            m.Ellipsoid(new Vector3(8f, 19f, 21f), new Vector3(3f, 2f, 3.4f), Quaternion.identity, Dark);
            // Left arm down to the pommel.
            m.Limb(new Vector3(-13f, 36f, 4f), new Vector3(-12f, 26f, 12f), 3.8f, 3.2f, Stone);
            m.Limb(new Vector3(-12f, 26f, 12f), new Vector3(-3f, 24f, 22f), 3.2f, 2.8f, Stone);
            m.Ellipsoid(new Vector3(-1.5f, 24f, 23.5f), new Vector3(3f, 3f, 3f), Quaternion.identity, Dark);
            // The sword, point sunk in the ground: pommel, grip, crossguard, a long blade.
            m.Ellipsoid(new Vector3(0f, 27.5f, 24f), new Vector3(1.4f, 1.4f, 1.4f), Quaternion.identity, Rust);
            m.Limb(new Vector3(0f, 27f, 24f), new Vector3(0f, 21f, 24f), 0.8f, 0.8f, Rust, 6);
            m.Ellipsoid(new Vector3(0f, 20.5f, 24f), new Vector3(6f, 0.9f, 1.2f), Quaternion.identity, Rust);
            m.Ellipsoid(new Vector3(0f, 9f, 24f), new Vector3(1.6f, 11.5f, 0.45f), Quaternion.identity, Dark, 6, 8);
            // Neck and bowed head with a brow band and hollow eyes looking down at the blade.
            m.Limb(new Vector3(0f, 38f, 5f), new Vector3(0f, 41f, 10f), 3.4f, 3f, Stone);
            var bow = Quaternion.Euler(38f, 0f, 0f);
            var head = new Vector3(0f, 43.5f, 12.5f);
            m.Ellipsoid(head, new Vector3(4.6f, 5.8f, 5.2f), bow, Stone);
            m.Ellipsoid(head + bow * new Vector3(0f, 2.2f, 3.6f), new Vector3(4.4f, 1.2f, 2f), bow, Dark);
            m.Ellipsoid(head + bow * new Vector3(-1.8f, 0.8f, 4.6f), new Vector3(1f, 0.6f, 0.6f), bow, Hollow);
            m.Ellipsoid(head + bow * new Vector3(1.8f, 0.8f, 4.6f), new Vector3(1f, 0.6f, 0.6f), bow, Hollow);
            m.Ellipsoid(head + bow * new Vector3(0f, -3.6f, 3.4f), new Vector3(2.8f, 2.4f, 2f), bow, Stone);   // bearded chin
            var mesh = m.ToMesh("KneelingTitan");
            Weather(mesh, Snow, 0.55f, 0.95f);
            AddPart("Body", root, mesh);
            return root;
        }

        /// <summary>
        /// A head about 30 m tall sunk to the lips, tilted over, a broken crown of uneven spikes, one fallen spike and a
        /// ring of rubble round it; moss on the brow and crown.
        /// </summary>
        static Transform BuildHead(float3 at, float yaw)
        {
            var root = new GameObject("HalfBuriedHead").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var tilt = Quaternion.Euler(-12f, 0f, 24f);
            var m = new DunesColossus.MeshBuilder();
            var c = new Vector3(0f, 6f, 0f);
            m.Ellipsoid(c, new Vector3(12f, 15f, 13f), tilt, Stone, 14, 9);
            m.Ellipsoid(c + tilt * new Vector3(0f, 5f, 11f), new Vector3(10f, 2.4f, 3.6f), tilt, Dark);               // brow
            m.Ellipsoid(c + tilt * new Vector3(-4.4f, 2.4f, 11.6f), new Vector3(2.6f, 1.5f, 1.3f), tilt, Hollow);      // eyes
            m.Ellipsoid(c + tilt * new Vector3(4.4f, 2.4f, 11.6f), new Vector3(2.6f, 1.5f, 1.3f), tilt, Hollow);
            m.Ellipsoid(c + tilt * new Vector3(0f, -2.5f, 13.4f), new Vector3(2.3f, 4.6f, 3f), tilt, Stone);           // nose
            m.Ellipsoid(c + tilt * new Vector3(-12f, 0f, 1f), new Vector3(1.6f, 4.4f, 2.6f), tilt, Dark);              // ears
            m.Ellipsoid(c + tilt * new Vector3(12f, 0f, 1f), new Vector3(1.6f, 4.4f, 2.6f), tilt, Dark);
            m.Ellipsoid(c + tilt * new Vector3(0f, -8.5f, 11f), new Vector3(5f, 1f, 1.4f), tilt, Hollow);              // lips at the ground
            // Crown band and spikes, three of them snapped short.
            m.Ellipsoid(c + tilt * new Vector3(0f, 12.5f, 0f), new Vector3(11f, 2f, 11.5f), tilt, Rust, 14, 4);
            for (int k = 0; k < 9; k++)
            {
                var a = k * Mathf.PI * 2f / 9f;
                var len = k % 3 == 1 ? 2f : 6.5f;
                var foot = new Vector3(Mathf.Cos(a) * 10f, 13.5f, Mathf.Sin(a) * 10.5f);
                m.Limb(c + tilt * foot, c + tilt * (foot + new Vector3(Mathf.Cos(a) * 1.2f, len, Mathf.Sin(a) * 1.2f)), 1.6f, k % 3 == 1 ? 1.3f : 0.35f, Rust, 5);
            }
            m.Limb(new Vector3(17f, 0.8f, 6f), new Vector3(21f, 1.6f, 11f), 1.5f, 0.35f, Rust, 5);                     // a snapped spike on the ground
            // Rubble ring: broken stones half sunk round the head.
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x4EADu);
            for (int k = 0; k < 26; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(15f, 24f);
                var size = rng.NextFloat(0.8f, 2.6f);
                m.Ellipsoid(new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r), new Vector3(size * 1.3f, size * 0.7f, size), Quaternion.Euler(rng.NextFloat(-20f, 20f), rng.NextFloat(0f, 360f), 0f), rng.NextBool() ? Stone : Dark, 6, 4);
            }
            var mesh = m.ToMesh("HalfBuriedHead");
            Weather(mesh, Moss, 0.35f, 0.85f);
            AddPart("Head", root, mesh);
            return root;
        }

        static readonly Color CapRed = new Color(0.72f, 0.24f, 0.16f);
        static readonly Color CapPale = new Color(0.86f, 0.74f, 0.52f);
        static readonly Color Stalk = new Color(0.88f, 0.85f, 0.76f);
        static readonly Color SlateBlue = new Color(0.40f, 0.43f, 0.50f);
        static readonly Color SlateDark = new Color(0.26f, 0.28f, 0.34f);
        static readonly Color Crystal = new Color(0.45f, 0.85f, 0.95f);
        static readonly Color Violet = new Color(0.62f, 0.45f, 0.92f);

        /// <summary>
        /// A stone tortoise about 50 m long in the fungal forest, head stretched out low as if it stopped mid-step:
        /// a domed shell of raised plates with moss on everything facing up, and growing from the moss a crop of
        /// mushrooms with one huge toadstool on the crest. Details: a clutch of three stone eggs half sunk by a front
        /// foot, a chipped plate lying on the ground, and a little mushroom ring on its snout.
        /// </summary>
        static Transform BuildTortoise(float3 at, float yaw)
        {
            var root = new GameObject("GiantTortoise").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var c = new Vector3(0f, 7f, 0f);
            var shell = new Vector3(19f, 15f, 23f);
            m.Dome(c, shell, Quaternion.identity, Stone, false, 16, 5);
            m.Ellipsoid(c, new Vector3(20.5f, 2.6f, 24.5f), Quaternion.identity, Dark, 16, 4);              // shell rim
            // Raised plates on the dome, in rings, each turned to the dome's surface.
            for (int ring = 0; ring < 3; ring++)
            {
                var theta = 0.28f + ring * 0.42f;                       // from the crest down
                var count = ring == 0 ? 1 : ring * 6 + 1;
                for (int k = 0; k < count; k++)
                {
                    var phi = k * Mathf.PI * 2f / count + ring * 0.4f;
                    var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    if (ring == 0) d = Vector3.up;
                    var p = c + Vector3.Scale(d, shell);
                    var n = new Vector3(d.x / shell.x, d.y / shell.y, d.z / shell.z).normalized;
                    m.Ellipsoid(p, new Vector3(4.6f, 1.3f, 4.6f), Quaternion.FromToRotation(Vector3.up, n), (ring + k) % 2 == 0 ? Dark : Stone, 6, 3);
                }
            }
            // Four pillar legs with blunt toes, the front left one lifted a little (mid-step).
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var lift = k == 0 ? 2.5f : 0f;
                var hip = new Vector3(sx * 14f, 7f, sz * 14f);
                var foot = new Vector3(sx * 18f, 1.2f + lift, sz * 18f);
                m.Limb(hip, foot, 5.5f, 4.6f, Stone);
                m.Ellipsoid(foot + new Vector3(0f, -0.4f, sz * 1.6f), new Vector3(5f, 2f, 5.5f), Quaternion.identity, Dark, 8, 4);
                for (int t = -1; t <= 1; t++)
                    m.Ellipsoid(foot + new Vector3(t * 2.4f, -0.6f, sz * 5.6f), new Vector3(1.1f, 0.9f, 1f), Quaternion.identity, Stone, 5, 3);
            }
            // Neck reaching forward and down, the head turned a little, a hooked beak and deep eyes.
            m.Limb(new Vector3(0f, 8f, 22f), new Vector3(2f, 7f, 32f), 5.4f, 4.2f, Dark);
            var look = Quaternion.Euler(10f, 14f, 0f);
            var head = new Vector3(3f, 7.5f, 36f);
            m.Ellipsoid(head, new Vector3(5f, 4.2f, 6.6f), look, Stone);
            m.Ellipsoid(head + look * new Vector3(0f, -1.2f, 5.6f), new Vector3(2.6f, 2.2f, 2f), look, Dark);            // beak
            m.Ellipsoid(head + look * new Vector3(-3.4f, 1.2f, 3f), new Vector3(1f, 1f, 1f), look, Hollow);
            m.Ellipsoid(head + look * new Vector3(3.4f, 1.2f, 3f), new Vector3(1f, 1f, 1f), look, Hollow);
            m.Limb(new Vector3(0f, 5f, -23f), new Vector3(-2f, 1.5f, -30f), 3f, 0.6f, Dark);               // stubby tail
            // A chipped plate on the ground beside it, and three stone eggs half sunk by the front left foot.
            m.Ellipsoid(new Vector3(26f, 1.5f, -6f), new Vector3(4.2f, 1.1f, 4.4f), Quaternion.Euler(14f, 30f, 22f), Dark, 6, 3);
            m.Ellipsoid(new Vector3(-27f, 1.6f, 22f), new Vector3(1.6f, 2f, 1.6f), Quaternion.Euler(10f, 0f, 8f), Stalk, 8, 5);
            m.Ellipsoid(new Vector3(-29.5f, 1.4f, 20.5f), new Vector3(1.5f, 1.9f, 1.5f), Quaternion.Euler(-8f, 0f, -14f), Stalk, 8, 5);
            m.Ellipsoid(new Vector3(-28f, 1.3f, 19f), new Vector3(1.5f, 1.8f, 1.5f), Quaternion.Euler(4f, 0f, 20f), Stalk, 8, 5);
            var mesh = m.ToMesh("GiantTortoise");
            Weather(mesh, Moss, 0.35f, 0.85f);
            AddPart("Body", root, mesh);

            // The mushrooms go in their own mesh so the moss pass doesn't colour their caps.
            var f = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x7027u);
            void Mushroom(Vector3 basePos, float height, float cap, Color color)
            {
                f.Limb(basePos, basePos + Vector3.up * height, cap * 0.22f, cap * 0.16f, Stalk, 6);
                f.Dome(basePos + Vector3.up * height, new Vector3(cap, cap * 0.55f, cap), Quaternion.identity, color, false, 10, 3);
                f.Ellipsoid(basePos + Vector3.up * (height - 0.05f), new Vector3(cap * 0.95f, cap * 0.08f, cap * 0.95f), Quaternion.identity, Stalk, 10, 2);  // gills
            }
            Mushroom(c + new Vector3(1.5f, shell.y - 0.6f, -2f), 7f, 5.5f, CapRed);                    // the toadstool on the crest
            for (int k = 0; k < 7; k++)
            {
                f.Ellipsoid(c + new Vector3(1.5f, shell.y + 6.4f, -2f) + Quaternion.Euler(0f, k * 51f, 0f) * new Vector3(3.2f, 0.2f, 0f), new Vector3(0.6f, 0.25f, 0.6f), Quaternion.identity, Stalk, 6, 2); // spots
            }
            for (int k = 0; k < 14; k++)
            {
                var theta = rng.NextFloat(0.35f, 1.15f);
                var phi = rng.NextFloat(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                var cluster = c + Vector3.Scale(d, shell) - Vector3.up * 0.4f;
                for (int j = rng.NextInt(1, 4); j > 0; j--)
                    Mushroom(cluster + new Vector3(rng.NextFloat(-1.5f, 1.5f), 0f, rng.NextFloat(-1.5f, 1.5f)), rng.NextFloat(0.8f, 2.4f), rng.NextFloat(0.6f, 1.6f), rng.NextBool() ? CapRed : CapPale);
            }
            for (int k = 0; k < 6; k++)                                                                   // a fairy ring on its snout
                Mushroom(head + look * new Vector3(Mathf.Cos(k * 1.05f) * 2.2f, 3.8f, Mathf.Sin(k * 1.05f) * 2.2f - 0.5f), 0.45f, 0.35f, CapPale);
            AddPart("Mushrooms", root, f.ToMesh("GiantTortoiseMushrooms"));
            return root;
        }

        /// <summary>
        /// A slate-blue serpent about 120 m long in the electric wastes, swimming through the ground in arches, its
        /// head reared 20 m up with the jaw open and a row of crystal spines (cyan and violet) down every arch. Details:
        /// rubble rings where it dives under, crystal shards sprouting from the ground beside those holes, a spine
        /// snapped off and lying in the dust, and two shed scales.
        /// </summary>
        static Transform BuildSerpent(float3 at, float yaw)
        {
            var root = new GameObject("CrystalSerpent").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            const float Ground = 2f;
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x5E2Bu);
            const int N = 44;
            Vector3 Path(float s) => new Vector3(14f * Mathf.Sin(s * Mathf.PI * 1.8f), Ground - 1.5f + 11f * Mathf.Sin(s * Mathf.PI * 3.5f), -60f + 110f * s);
            float Radius(float s) => Mathf.Lerp(1.2f, 4.6f, Mathf.SmoothStep(0f, 1f, s * 1.6f));
            for (int i = 0; i < N; i++)
            {
                float s0 = i / (float)N, s1 = (i + 1f) / N;
                Vector3 a = Path(s0), b = Path(s1);
                m.Limb(a, b, Radius(s0), Radius(s1), i / 3 % 2 == 0 ? SlateBlue : SlateDark, 8);
                // Rubble ring where the body enters or leaves the ground.
                if ((a.y - Ground) * (b.y - Ground) < 0f)
                {
                    var hole = Vector3.Lerp(a, b, (Ground - a.y) / (b.y - a.y));
                    for (int k = 0; k < 9; k++)
                    {
                        var ang = rng.NextFloat(0f, Mathf.PI * 2f);
                        var size = rng.NextFloat(0.8f, 2f);
                        var p = new Vector3(hole.x + Mathf.Cos(ang) * (Radius(s0) + 1.5f), Ground, hole.z + Mathf.Sin(ang) * (Radius(s0) + 1.5f));
                        m.Ellipsoid(p, new Vector3(size * 1.3f, size * 0.6f, size), Quaternion.Euler(rng.NextFloat(-15f, 15f), rng.NextFloat(0f, 360f), 0f), SlateDark, 6, 3);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        var side = new Vector3(rng.NextBool() ? -1f : 1f, 0f, 0f) * (Radius(s0) + rng.NextFloat(3f, 6f));
                        var foot = new Vector3(hole.x, Ground - 0.5f, hole.z + rng.NextFloat(-3f, 3f)) + side;
                        m.Limb(foot, foot + new Vector3(rng.NextFloat(-1.2f, 1.2f), rng.NextFloat(1.5f, 3.5f), rng.NextFloat(-1.2f, 1.2f)), 0.6f, 0.05f, k % 2 == 0 ? Crystal : Violet, 4);
                    }
                }
                // Crystal spines on the back wherever the body is above the ground, longest mid-body.
                if (a.y > Ground + 1f && i % 2 == 0)
                {
                    var up = Vector3.Cross(b - a, Vector3.right).normalized;
                    if (up.y < 0f) up = -up;
                    var len = rng.NextFloat(3f, 7f) * Mathf.Lerp(0.4f, 1f, Radius(s0) / 4.6f);
                    var lean = (a - b).normalized * 0.35f;
                    m.Limb(a + up * Radius(s0) * 0.8f, a + (up + lean).normalized * (Radius(s0) + len), 1f, 0.08f, i / 2 % 2 == 0 ? Crystal : Violet, 4);
                }
            }
            // The reared neck and head, jaw open, glinting crystal eyes and a crest of short spines.
            var neckBase = Path(1f);
            var headPos = neckBase + new Vector3(0f, 18f, 9f);
            m.Limb(neckBase, neckBase + new Vector3(0f, 10f, 1f), 4.6f, 4.2f, SlateBlue, 8);
            m.Limb(neckBase + new Vector3(0f, 10f, 1f), headPos, 4.2f, 3.6f, SlateDark, 8);
            var pitch = Quaternion.Euler(18f, 0f, 0f);
            m.Ellipsoid(headPos + pitch * new Vector3(0f, 1f, 4f), new Vector3(4.4f, 3f, 8f), pitch, SlateBlue);
            var jaw = Quaternion.Euler(44f, 0f, 0f);
            m.Ellipsoid(headPos + jaw * new Vector3(0f, -1.5f, 4.5f), new Vector3(3.8f, 1.4f, 7f), jaw, SlateDark);
            m.Ellipsoid(headPos + pitch * new Vector3(0f, -0.8f, 5f), new Vector3(3f, 1.2f, 5.5f), pitch, Hollow);        // the open mouth
            m.Ellipsoid(headPos + pitch * new Vector3(-3.4f, 2.4f, 4f), new Vector3(1f, 0.8f, 1.3f), pitch, Crystal);
            m.Ellipsoid(headPos + pitch * new Vector3(3.4f, 2.4f, 4f), new Vector3(1f, 0.8f, 1.3f), pitch, Crystal);
            for (int k = 0; k < 2; k++)                                                                    // fangs
                m.Limb(headPos + pitch * new Vector3(k == 0 ? -2.2f : 2.2f, -1.2f, 10f), headPos + pitch * new Vector3(k == 0 ? -2f : 2f, -4.4f, 10.4f), 0.6f, 0.05f, Stalk, 5);
            for (int k = 0; k < 5; k++)
                m.Limb(headPos + pitch * new Vector3(0f, 3f, 2f - k * 2.2f), headPos + pitch * new Vector3(0f, 6.5f - k * 0.5f, -1f - k * 2.4f), 0.9f, 0.06f, k % 2 == 0 ? Violet : Crystal, 4);
            // A snapped spine lying in the dust, and two shed scales.
            m.Limb(new Vector3(-22f, Ground + 0.4f, 6f), new Vector3(-17f, Ground + 1.2f, 10f), 1f, 0.08f, Crystal, 4);
            m.Ellipsoid(new Vector3(20f, Ground + 0.1f, -14f), new Vector3(2.4f, 0.25f, 1.8f), Quaternion.Euler(6f, 40f, 4f), SlateBlue, 6, 2);
            m.Ellipsoid(new Vector3(23f, Ground + 0.1f, -10f), new Vector3(2f, 0.25f, 1.5f), Quaternion.Euler(-5f, 110f, 3f), SlateDark, 6, 2);
            AddPart("Body", root, m.ToMesh("CrystalSerpent"));
            return root;
        }

        static readonly Color Algae = new Color(0.22f, 0.30f, 0.16f);
        static readonly Color Bronze = new Color(0.30f, 0.42f, 0.36f);
        static readonly Color Lily = new Color(0.24f, 0.46f, 0.18f);
        static readonly Color Bloom = new Color(0.92f, 0.86f, 0.90f);

        /// <summary>
        /// A knight about 60 m long lying on his back in the marsh, sunk to the belt: the great helm turned a little to
        /// one side, breastplate and knees breaking the surface, one gauntlet reaching up out of the bog still gripping a
        /// snapped sword, his kite shield tilted half under beside him. Green bronze with algae on every face turned up.
        /// Details: seeded lily pads round the chest (every fifth in bloom), the blade's broken tip lying further off,
        /// and a crest of spikes behind the helm.
        /// </summary>
        static Transform BuildKnight(float3 at, float yaw)
        {
            var root = new GameObject("DrownedKnight").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), Quaternion.Euler(0f, yaw, 0f));
            const float Ground = 0f;
            var m = new DunesColossus.MeshBuilder();
            // Torso lying along +Z (head at +Z), breastplate ridge up, the belt just under the ground.
            m.Ellipsoid(new Vector3(0f, Ground - 1f, 4f), new Vector3(11f, 5.5f, 13f), Quaternion.identity, Bronze);
            m.Ellipsoid(new Vector3(0f, Ground + 3.6f, 6f), new Vector3(3f, 1.8f, 9f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(0f, Ground - 0.5f, -9f), new Vector3(9.5f, 3.5f, 4f), Quaternion.identity, Dark);
            // Legs: knees up out of the water, shins back down, sabatons pointing up at the far end.
            for (int s = -1; s <= 1; s += 2)
            {
                var hip = new Vector3(s * 5f, Ground - 2f, -12f);
                var knee = new Vector3(s * 6.5f, Ground + 5f, -24f);
                var ankle = new Vector3(s * 7f, Ground - 1.5f, -36f);
                m.Limb(hip, knee, 4.6f, 3.8f, Bronze);
                m.Ellipsoid(knee, new Vector3(4.2f, 3.2f, 4.2f), Quaternion.identity, Dark);
                m.Limb(knee, ankle, 3.8f, 3f, Bronze);
                m.Ellipsoid(ankle + new Vector3(0f, 2.5f, -1.5f), new Vector3(2.6f, 4.5f, 2.4f), Quaternion.Euler(-15f, 0f, 0f), Dark);
            }
            // Pauldrons; the left arm lies flat, the right is raised from the elbow with the gauntlet round a snapped sword.
            m.Ellipsoid(new Vector3(-11.5f, Ground + 0.5f, 14f), new Vector3(5f, 3.6f, 5f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(11.5f, Ground + 0.5f, 14f), new Vector3(5f, 3.6f, 5f), Quaternion.identity, Dark);
            m.Limb(new Vector3(-13f, Ground - 1f, 12f), new Vector3(-16f, Ground - 1f, -2f), 3.4f, 3f, Bronze);
            m.Limb(new Vector3(-16f, Ground - 1f, -2f), new Vector3(-15f, Ground - 0.8f, -14f), 3f, 2.6f, Bronze);
            m.Ellipsoid(new Vector3(-15f, Ground - 0.3f, -16.5f), new Vector3(2.8f, 1.6f, 3f), Quaternion.identity, Dark);
            var elbow = new Vector3(15.5f, Ground, 2f);
            var fist = new Vector3(16f, Ground + 15f, 4f);
            m.Limb(new Vector3(13f, Ground - 1f, 12f), elbow, 3.4f, 3f, Bronze);
            m.Limb(elbow, fist, 3f, 2.6f, Bronze);
            m.Ellipsoid(fist + new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3.2f, 3f), Quaternion.identity, Dark);
            m.Ellipsoid(fist + new Vector3(0f, 4.4f, 0f), new Vector3(4.5f, 0.8f, 1.1f), Quaternion.identity, Rust);
            m.Ellipsoid(fist + new Vector3(0f, -0.2f, 0f), new Vector3(1.2f, 1.2f, 1.2f), Quaternion.identity, Rust);
            m.Ellipsoid(fist + new Vector3(0f, 9f, 0f), new Vector3(1.4f, 4.6f, 0.4f), Quaternion.Euler(0f, 0f, 4f), Dark, 6, 6);
            m.Ellipsoid(new Vector3(30f, Ground - 0.2f, 12f), new Vector3(1.3f, 0.4f, 6f), Quaternion.Euler(4f, 28f, 8f), Dark, 6, 4);
            // Great helm turned a little to his left: visor slit, breaths, and a crest of spikes behind.
            var turn = Quaternion.Euler(0f, -18f, 0f) * Quaternion.Euler(-80f, 0f, 0f);
            var helm = new Vector3(0f, Ground + 1f, 23f);
            m.Limb(new Vector3(0f, Ground - 1f, 15f), helm, 4.4f, 4f, Dark);
            m.Ellipsoid(helm, new Vector3(6f, 7.5f, 6.4f), turn, Bronze, 12, 8);
            m.Ellipsoid(helm + turn * new Vector3(0f, 1.6f, 5.6f), new Vector3(5f, 0.7f, 1.2f), turn, Hollow);
            for (int k = -2; k <= 2; k++)
                m.Ellipsoid(helm + turn * new Vector3(k * 1.3f, -2.2f, 5.8f), new Vector3(0.4f, 0.9f, 0.6f), turn, Hollow, 5, 3);
            for (int k = 0; k < 6; k++)
            {
                var b = helm + turn * new Vector3(0f, 7f - k * 0.2f, -1f - k * 1.2f);
                m.Limb(b, b + new Vector3(0f, 2.5f + (k % 2) * 1.5f, 0f), 0.7f, 0.05f, Rust, 4);
            }
            // His kite shield, tilted half under the water beside the left leg.
            m.Ellipsoid(new Vector3(-20f, Ground + 0.5f, -20f), new Vector3(7f, 0.9f, 10f), Quaternion.Euler(0f, 18f, 32f), Bronze, 10, 4);
            m.Ellipsoid(new Vector3(-19.5f, Ground + 1.2f, -20f), new Vector3(1.6f, 0.6f, 6f), Quaternion.Euler(0f, 18f, 32f), Rust, 6, 3);
            var mesh = m.ToMesh("DrownedKnight");
            Weather(mesh, Algae, 0.5f, 0.9f);
            AddPart("Body", root, mesh);

            // Lily pads round the chest, every fifth in bloom (own mesh so the algae pass leaves them alone).
            var f = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xD20Au);
            for (int k = 0; k < 18; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(13f, 22f);
                var p = new Vector3(Mathf.Cos(a) * r, Ground + 0.15f, 6f + Mathf.Sin(a) * r * 1.3f);
                var size = rng.NextFloat(0.8f, 1.8f);
                f.Ellipsoid(p, new Vector3(size, 0.1f, size), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Lily, 8, 2);
                if (k % 5 == 0)
                    f.Ellipsoid(p + new Vector3(0f, 0.3f, 0f), new Vector3(0.45f, 0.35f, 0.45f), Quaternion.identity, Bloom, 6, 3);
            }
            AddPart("Lilies", root, f.ToMesh("DrownedKnightLilies"));
            return root;
        }

        static readonly Color Basalt = new Color(0.17f, 0.16f, 0.16f);
        static readonly Color Soot = new Color(0.10f, 0.09f, 0.09f);
        static readonly Color Ember = new Color(1.00f, 0.42f, 0.10f);
        static readonly Color Ash = new Color(0.42f, 0.40f, 0.39f);

        /// <summary>
        /// W-86: a basalt smith about 34 m tall sitting cross-legged in the ash, hunched over an anvil as big as a house,
        /// one hand on the hammer laid across it, the other holding tongs on a half-made blade still glowing on the anvil
        /// face. Ember cracks run down his chest and the anvil's waist; ash lies on everything turned up.
        /// Details: a seeded scatter of cooled slag lumps round the anvil (every fourth still glowing) and a quench trough.
        /// </summary>
        static Transform BuildSmith(float3 at, float yaw)
        {
            var root = new GameObject("AshSmith").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            // Crossed legs on the ground, knees out to the sides, feet tucked in front.
            m.Ellipsoid(new Vector3(0f, 6f, -4f), new Vector3(10f, 5.5f, 7f), Quaternion.identity, Soot);
            for (int s = -1; s <= 1; s += 2)
            {
                var knee = new Vector3(s * 13f, 4f, 4f);
                m.Limb(new Vector3(s * 5f, 5f, -4f), knee, 4.6f, 4f, Basalt);
                m.Limb(knee, new Vector3(-s * 4f, 2.5f, 9f), 3.8f, 3f, Basalt);
                m.Ellipsoid(new Vector3(-s * 5.5f, 2f, 10f), new Vector3(3f, 2f, 4f), Quaternion.Euler(0f, s * 50f, 0f), Soot);
            }
            // Torso hunched forward over the anvil, a heavy leather apron, broad shoulders.
            var hunch = Quaternion.Euler(28f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 16f, -1f), new Vector3(9.5f, 11f, 7f), hunch, Basalt);
            m.Ellipsoid(new Vector3(0f, 12f, 4.5f), new Vector3(7.5f, 7f, 1.6f), Quaternion.Euler(14f, 0f, 0f), Rust);
            m.Ellipsoid(new Vector3(0f, 24f, 3f), new Vector3(14f, 4.5f, 6.5f), hunch, Soot);
            // Ember cracks down the chest.
            for (int k = 0; k < 5; k++)
                m.Ellipsoid(new Vector3(-3f + k * 1.5f, 21f - k * 1.8f, 5.6f + k * 0.3f), new Vector3(0.35f, 2.2f, 0.4f),
                            Quaternion.Euler(28f, 0f, k % 2 == 0 ? 25f : -20f), Ember, 5, 4);
            // The anvil in front of him: foot, waist, a broad face and a horn.
            var anvil = new Vector3(0f, 0f, 20f);
            m.Ellipsoid(anvil + new Vector3(0f, 2f, 0f), new Vector3(7f, 2.6f, 5f), Quaternion.identity, Soot, 8, 4);
            m.Limb(anvil + new Vector3(0f, 3f, 0f), anvil + new Vector3(0f, 8.5f, 0f), 3.4f, 2.6f, Basalt, 8);
            m.Ellipsoid(anvil + new Vector3(0f, 9.5f, 0f), new Vector3(8.5f, 1.8f, 4f), Quaternion.identity, Basalt, 10, 4);
            m.Limb(anvil + new Vector3(8f, 9.5f, 0f), anvil + new Vector3(14f, 9.2f, 0f), 1.9f, 0.1f, Basalt, 8);
            m.Ellipsoid(anvil + new Vector3(0f, 6f, 2.7f), new Vector3(1.6f, 0.3f, 0.3f), Quaternion.Euler(0f, 0f, 12f), Ember, 5, 3);
            // The half-made blade glowing on the face.
            m.Ellipsoid(anvil + new Vector3(-1f, 11.6f, 0.5f), new Vector3(6f, 0.35f, 0.9f), Quaternion.Euler(0f, 8f, 0f), Ember, 8, 3);
            // Left arm down to the tongs gripping it.
            var lhand = new Vector3(-8f, 13f, 14f);
            m.Limb(new Vector3(-12.5f, 24f, 3f), new Vector3(-14f, 16f, 8f), 3.6f, 3f, Basalt);
            m.Limb(new Vector3(-14f, 16f, 8f), lhand, 3f, 2.6f, Basalt);
            m.Ellipsoid(lhand, new Vector3(2.6f, 2.2f, 2.8f), Quaternion.identity, Soot);
            m.Limb(lhand + new Vector3(0.5f, -0.5f, 1.5f), anvil + new Vector3(-5f, 11.8f, 0.5f), 0.45f, 0.35f, Rust, 4);
            m.Limb(lhand + new Vector3(-0.5f, 0.3f, 1.5f), anvil + new Vector3(-5f, 12.3f, 0.5f), 0.45f, 0.35f, Rust, 4);
            // Right arm resting on the hammer laid across the anvil.
            var rhand = new Vector3(9f, 12.5f, 16f);
            m.Limb(new Vector3(12.5f, 24f, 3f), new Vector3(15f, 16f, 8f), 3.6f, 3f, Basalt);
            m.Limb(new Vector3(15f, 16f, 8f), rhand, 3f, 2.6f, Basalt);
            m.Ellipsoid(rhand, new Vector3(2.6f, 2.2f, 2.8f), Quaternion.identity, Soot);
            m.Limb(rhand, anvil + new Vector3(3f, 11.8f, -1f), 0.8f, 0.8f, Rust, 6);
            m.Ellipsoid(anvil + new Vector3(2.5f, 12.4f, -1.2f), new Vector3(1.8f, 1.6f, 3.2f), Quaternion.Euler(0f, 30f, 0f), Dark, 8, 4);
            // Neck and head bowed over the work: a heavy brow, eyes lit from the blade, a braided beard.
            var bow = Quaternion.Euler(44f, 0f, 0f);
            var head = new Vector3(0f, 28.5f, 9f);
            m.Limb(new Vector3(0f, 25f, 3f), head, 3.4f, 3f, Basalt);
            m.Ellipsoid(head, new Vector3(4.4f, 5.4f, 4.8f), bow, Basalt);
            m.Ellipsoid(head + bow * new Vector3(0f, 2f, 3.6f), new Vector3(4.2f, 1.1f, 1.8f), bow, Soot);
            m.Ellipsoid(head + bow * new Vector3(-1.7f, 0.7f, 4.4f), new Vector3(0.9f, 0.5f, 0.5f), bow, Ember);
            m.Ellipsoid(head + bow * new Vector3(1.7f, 0.7f, 4.4f), new Vector3(0.9f, 0.5f, 0.5f), bow, Ember);
            m.Limb(head + bow * new Vector3(0f, -3f, 3.4f), head + bow * new Vector3(0f, -9f, 3.8f), 2.6f, 0.8f, Soot, 6);
            // A quench trough beside the anvil.
            m.Ellipsoid(new Vector3(-17f, 1.2f, 18f), new Vector3(3f, 1.6f, 6f), Quaternion.Euler(0f, 10f, 0f), Soot, 8, 4);
            m.Ellipsoid(new Vector3(-17f, 2.6f, 18f), new Vector3(2.2f, 0.2f, 5f), Quaternion.Euler(0f, 10f, 0f), Hollow, 8, 2);
            // Cooled slag lumps round the anvil, every fourth still glowing.
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xA54Du);
            for (int k = 0; k < 14; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(10f, 19f);
                var size = rng.NextFloat(0.8f, 2.2f);
                m.Ellipsoid(anvil + new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r * 0.8f), new Vector3(size, size * 0.6f, size * 1.2f),
                            Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), k % 4 == 0 ? Ember : Soot, 6, 3);
            }
            var mesh = m.ToMesh("AshSmith");
            Weather(mesh, Ash, 0.82f, 1f);
            AddPart("Body", root, mesh);
            return root;
        }

        static readonly Color PlantGreen = new Color(0.20f, 0.42f, 0.16f);
        static readonly Color PlantDark = new Color(0.12f, 0.30f, 0.12f);
        static readonly Color Maw = new Color(0.55f, 0.10f, 0.14f);
        static readonly Color Fang = new Color(0.86f, 0.82f, 0.66f);
        static readonly Color Slime = new Color(0.55f, 0.70f, 0.30f);
        static readonly Color Bone = new Color(0.80f, 0.76f, 0.68f);
        static readonly Color BoneDark = new Color(0.58f, 0.54f, 0.48f);
        static readonly Color PlantRed = new Color(0.66f, 0.10f, 0.12f);
        static readonly Color PlantPurple = new Color(0.40f, 0.14f, 0.46f);
        static readonly Color SkirtGreen = new Color(0.05f, 0.15f, 0.04f);
        static readonly Color SkirtDark = new Color(0.03f, 0.10f, 0.03f);

        /// <summary>A tapering tube along a cubic Bezier from <paramref name="p0"/> to <paramref name="p3"/>, banded in two colours.</summary>
        static void Tentacle(DunesColossus.MeshBuilder m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float r0, float r1, Color c0, Color c1, int segs = 12)
        {
            Vector3 P(float t) { var u = 1f - t; return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3; }
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1f) / segs;
                m.Limb(P(t0), P(t1), Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), i % 2 == 0 ? c0 : c1, 7);
            }
        }

        /// <summary>A snap-trap head like the Mantrap's (#41): two shell halves hinged at <paramref name="hinge"/>, mouth along the frame's +Z, red lining and fangs round the rims.</summary>
        static void Trap(DunesColossus.MeshBuilder m, Vector3 hinge, Quaternion frame, Vector3 radii, float gape, float fang, int only = -1)
        {
            var up = frame * Quaternion.Euler(-gape * 0.7f, 0f, 0f);
            var low = frame * Quaternion.Euler(gape * 0.3f, 0f, 0f);
            for (int half = 0; half < 2; half++)
            {
                if (only >= 0 && half != only) continue; // A-77: one half only (0 upper, 1 lower), for a jaw that moves
                var rot = half == 0 ? up : low;
                var flip = half == 0 ? rot : rot * Quaternion.Euler(180f, 180f, 0f);
                var centre = hinge + rot * new Vector3(0f, 0f, radii.z);
                m.Dome(centre, radii, flip, half == 0 ? PlantGreen : PlantDark, false, 14, 5);
                m.Dome(centre, radii * 0.93f, flip, Maw, true, 14, 5);
                var dir = rot * (half == 0 ? Vector3.down : Vector3.up);
                for (int k = 0; k < 11; k++)
                {
                    var a = Mathf.Lerp(-80f, 80f, (k + half * 0.5f) / 10.5f) * Mathf.Deg2Rad;
                    var rim = centre + rot * new Vector3(Mathf.Sin(a) * radii.x * 0.95f, 0f, Mathf.Cos(a) * radii.z * 0.95f);
                    m.Limb(rim, rim + dir * fang + rot * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * fang * 0.3f, fang * 0.12f, fang * 0.02f, Fang, 4);
                }
            }
        }

        /// <summary>
        /// W-88 (#40, Zach's PoisonSwamp design): a carnivorous plant about 40 m tall standing in the bog. A ribbed bulb
        /// on six arched tentacle legs rooted in the mud, a thick stem up to a snap-trap head (as the Mantrap's, #41) gaping
        /// at the sky with drool hanging from its fangs, and eight tentacle arms reaching out, every third ending in a small
        /// trap of its own. Details: broad leaves lying flat round its feet, three young mantrap sprouts, a deer's bones
        /// picked clean by one foot, and pale spores drifting from the mouth.
        /// </summary>
        static Transform BuildPlant(float3 at, float yaw)
        {
            var root = new GameObject("CarnivorousPlant").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xD20Bu);
            // W-91 (#40, Zach): the body is drawn in its old units and shown at PlantScale (about a quarter of its old
            // ~40 m height), with a bigger head (HeadScale) and thinner vines (VineScale); the sprouts and bones stay life size.
            // A-77 (item 21, Zach): twice that now (about 22 m), and alive: the stem, the upper jaw and each arm are their
            // own pivoted parts, which SwayPlant moves (see PlantRig). The draws keep their order, so the shape is unchanged.
            const float PlantScale = 0.5f, HeadScale = 1.7f, VineScale = 0.5f;
            var stemMesh = new DunesColossus.MeshBuilder();
            var jawMesh = new DunesColossus.MeshBuilder();
            var armMesh = new DunesColossus.MeshBuilder[8];
            var armFrom = new Vector3[8];
            // The bulb, raised on its legs, with darker ribs down it.
            var bulb = new Vector3(0f, 12f, 0f);
            m.Ellipsoid(bulb, new Vector3(9f, 8f, 9f), Quaternion.identity, PlantGreen, 14, 8);
            for (int k = 0; k < 8; k++)
                m.Ellipsoid(bulb + Quaternion.Euler(0f, k * 45f, 0f) * new Vector3(0f, 0f, 8.2f), new Vector3(1.4f, 7.4f, 1.2f), Quaternion.Euler(0f, k * 45f, 0f), PlantDark, 6, 6);
            // Six tentacle legs arching out and down into the mud, each splayed into roots at the foot.
            for (int k = 0; k < 6; k++)
            {
                var a = (k * 60f + 30f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var from = bulb + d * 6f - Vector3.up * 3f;
                var foot = d * rng.NextFloat(22f, 27f) + Vector3.up * 0.6f;
                Tentacle(m, from, from + d * 7f + Vector3.up * 9f, foot + Vector3.up * 12f - d * 2f, foot, 3.2f * VineScale, 1.4f * VineScale, PlantGreen, PlantDark, 14);
                for (int r = 0; r < 4; r++)
                {
                    var ra = a + (r - 1.5f) * 0.5f;
                    m.Limb(foot, foot + new Vector3(Mathf.Sin(ra), -0.4f, Mathf.Cos(ra)) * rng.NextFloat(3f, 5f), 0.9f * VineScale, 0.15f, PlantDark, 5);
                }
            }
            // The stem, a little bent, up to the head; the mouth gapes at the sky over the bog.
            var neck = new Vector3(2f, 30f, 1f);
            stemMesh.Limb(bulb + Vector3.up * 6f, new Vector3(2.5f, 24f, -0.5f), 4.6f, 3.8f, PlantDark, 9);
            stemMesh.Limb(new Vector3(2.5f, 24f, -0.5f), neck, 3.8f, 3.4f, PlantGreen, 9);
            var face = Quaternion.Euler(-35f, 0f, 0f);
            var hinge = neck + new Vector3(0f, 2f, -4f) * HeadScale;
            Trap(jawMesh, hinge, face, new Vector3(7.5f, 5.5f, 8.5f) * HeadScale, 60f, 2.8f * HeadScale, 0);
            Trap(stemMesh, hinge, face, new Vector3(7.5f, 5.5f, 8.5f) * HeadScale, 60f, 2.8f * HeadScale, 1);
            // Drool hanging from the upper fangs.
            for (int k = 0; k < 4; k++)
            {
                var drip = hinge + face * Quaternion.Euler(-42f, 0f, 0f) * new Vector3(-4.5f + k * 3f, -1.5f, 15f) * HeadScale;
                jawMesh.Limb(drip, drip + Vector3.down * rng.NextFloat(2.5f, 5.5f), 0.35f * HeadScale, 0.15f * HeadScale, Slime, 4);
            }
            // Eight arms reaching out from the top of the bulb and the stem, curling at the tips; every third ends in a trap.
            for (int k = 0; k < 8; k++)
            {
                var a = (k * 45f + rng.NextFloat(-12f, 12f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var from = new Vector3(1f, 16f + k % 3 * 3.5f, 0f) + d * 4f;
                var reach = rng.NextFloat(20f, 28f);
                var lift = rng.NextFloat(-4f, 12f);
                var tip = from + d * reach + Vector3.up * lift;
                var am = armMesh[k] = new DunesColossus.MeshBuilder();
                armFrom[k] = from;
                Tentacle(am, from, from + d * 8f + Vector3.up * 12f, tip - d * 6f + Vector3.up * 10f + side * 4f, tip, 2.2f * VineScale, 0.5f * VineScale, PlantGreen, PlantDark, 14);
                if (k % 3 == 0)
                    Trap(am, tip, Quaternion.LookRotation(d + Vector3.up * 0.3f), new Vector3(1.8f, 1.3f, 2.1f), 50f, 0.8f);
                else
                {
                    // A tight curl back on itself.
                    var p = tip;
                    for (int c = 0; c < 4; c++)
                    {
                        var q = p + (Quaternion.AngleAxis(-70f * (c + 1), side) * d) * (2.2f - c * 0.4f);
                        am.Limb(p, q, (0.5f - c * 0.08f) * VineScale, (0.42f - c * 0.08f) * VineScale, c % 2 == 0 ? PlantDark : PlantGreen, 6);
                        p = q;
                    }
                }
            }
            // Broad leaves lying flat round the feet.
            for (int k = 0; k < 7; k++)
            {
                var a = k * 51.4f + rng.NextFloat(-10f, 10f);
                var rot = Quaternion.Euler(0f, a, 0f);
                m.Ellipsoid(rot * new Vector3(0f, 0.8f, 15f), new Vector3(4f, 0.4f, 9f), rot * Quaternion.Euler(-4f, 0f, 0f), k % 2 == 0 ? PlantGreen : PlantDark, 12, 4);
                m.Limb(rot * new Vector3(0f, 1.1f, 7f), rot * new Vector3(0f, 1.1f, 23f), 0.3f, 0.1f, PlantDark, 4);
            }
            // W-92 (#40, Zach): a leafy skirt, broad dark leaves each with a purple vein and every third tipped red, plus a
            // few red spots on the bulb. Its own random, seeded by the plant's spot, so the count and lean differ per plant
            // and the draws above keep their values. W-93: rooted high on the bulb under the stem and hanging down over
            // it (steep, then near-vertical), rather than splaying out at the base.
            var srng = Unity.Mathematics.Random.CreateFromIndex(math.hash((int3)math.floor(at)) ^ 0x5C127u);
            int skirt = srng.NextInt(9, 14);
            var lean = srng.NextFloat(18f, 34f);
            for (int k = 0; k < skirt; k++)
            {
                var turn = Quaternion.Euler(0f, k * 360f / skirt + srng.NextFloat(-8f, 8f), 0f);
                var droop = lean + srng.NextFloat(-6f, 6f);
                var len = srng.NextFloat(7f, 10f);
                var root0 = bulb + turn * new Vector3(0f, 6.5f, 6.4f);
                var inner = turn * Quaternion.Euler(35f + droop * 0.6f, 0f, 0f);
                var outer = turn * Quaternion.Euler(70f + droop * 0.4f, 0f, 0f);
                var bend = root0 + inner * Vector3.forward * len * 0.7f;
                var tipEnd = bend + outer * Vector3.forward * len * 0.9f;
                var col = k % 2 == 0 ? SkirtGreen : SkirtDark;
                m.Ellipsoid(root0 + inner * Vector3.forward * len * 0.35f, new Vector3(len * 0.4f, 0.35f, len * 0.4f), inner, col, 10, 4);
                m.Ellipsoid(bend + outer * Vector3.forward * len * 0.45f, new Vector3(len * 0.34f, 0.3f, len * 0.5f), outer, col, 10, 4);
                m.Limb(root0 + inner * Vector3.up * 0.3f, bend + inner * Vector3.up * 0.3f, 0.24f, 0.16f, PlantPurple, 4);
                m.Limb(bend + outer * Vector3.up * 0.28f, tipEnd + outer * Vector3.up * 0.2f, 0.16f, 0.06f, PlantPurple, 4);
                if (k % 3 == 0)
                    m.Ellipsoid(tipEnd - outer * Vector3.forward * 0.8f, new Vector3(len * 0.12f, 0.36f, 1.1f), outer, PlantRed, 8, 4);
            }
            for (int k = 0; k < 6; k++)
            {
                var spot = Quaternion.Euler(srng.NextFloat(-40f, 30f), srng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                m.Ellipsoid(bulb + Vector3.Scale(spot, new Vector3(9f, 8f, 9f)) * 0.99f, Vector3.one * srng.NextFloat(0.6f, 1.1f), Quaternion.LookRotation(spot), PlantRed, 8, 4);
            }
            AddPart("Body", root, m.ToMesh("CarnivorousPlant"));
            var body = root.Find("Body");
            body.localScale = Vector3.one * PlantScale;
            var stemPivot = new Vector3(0f, 18f, 0f);
            PlantRig.Stem = PlantPart("Stem", body, stemPivot, Vector3.zero, stemMesh);
            PlantRig.Jaw = PlantPart("Jaw", PlantRig.Stem, hinge, stemPivot, jawMesh);
            PlantRig.JawAxis = face * Vector3.right;
            for (int k = 0; k < 8; k++)
                PlantRig.Arms[k] = PlantPart("Arm" + k, body, armFrom[k], Vector3.zero, armMesh[k]);
            // Three young sprouts, man-high, with shut traps (life size, round the shrunk plant).
            m = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 3; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.NextFloat(30f, 36f) * PlantScale + Vector3.up * 0.3f;
                var top = p + new Vector3(rng.NextFloat(-0.5f, 0.5f), 2.2f, rng.NextFloat(-0.5f, 0.5f));
                m.Limb(p, top, 0.18f, 0.12f, PlantGreen, 5);
                Trap(m, top, Quaternion.Euler(-20f, rng.NextFloat(0f, 360f), 0f), new Vector3(0.35f, 0.28f, 0.4f), 18f, 0.12f);
            }
            // A deer's bones picked clean by one foot: ribs, spine, skull, a leg bone.
            var bones = new Vector3(-24f * PlantScale, 0.9f, 8f * PlantScale);
            m.Limb(bones, bones + new Vector3(0f, 0f, 2.4f), 0.16f, 0.14f, Bone, 5);
            for (int k = 0; k < 5; k++)
                for (int s = -1; s <= 1; s += 2)
                    m.Limb(bones + new Vector3(0f, 0.1f, 0.3f + k * 0.45f), bones + new Vector3(s * 0.7f, 0.5f, 0.4f + k * 0.45f), 0.06f, 0.05f, Bone, 4);
            m.Ellipsoid(bones + new Vector3(0.3f, 0.15f, 3f), new Vector3(0.28f, 0.25f, 0.45f), Quaternion.Euler(0f, 20f, 0f), Bone, 8, 4);
            m.Limb(bones + new Vector3(1.2f, 0f, -0.6f), bones + new Vector3(2.4f, 0f, 0.4f), 0.07f, 0.06f, BoneDark, 4);
            AddPart("Sprouts", root, m.ToMesh("CarnivorousPlantSprouts"));
            Puffs(root, "Spores", (hinge + face * new Vector3(0f, 0f, 9f * HeadScale) + Vector3.up * 4f) * PlantScale, new Vector3(4f, 1.5f, 4f),
                  new Color(0.80f, 0.86f, 0.55f, 0.22f), new Color(0.70f, 0.80f, 0.45f, 0.30f), 1.5f, 4f, 3f, 0.6f, 12f);
            return root;
        }

        /// <summary>A pivot named <paramref name="name"/> at <paramref name="at"/> (body units) under <paramref name="parent"/> (itself at <paramref name="parentAt"/>), holding <paramref name="m"/>'s mesh drawn in body units.</summary>
        static Transform PlantPart(string name, Transform parent, Vector3 at, Vector3 parentAt, DunesColossus.MeshBuilder m)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at - parentAt;
            AddPart(name + "Mesh", pivot, m.ToMesh("CarnivorousPlant" + name));
            pivot.GetChild(0).localPosition = -at;
            return pivot;
        }

        /// <summary>A-77 (item 21): the carnivorous plant's moving parts, set by BuildPlant (one plant per island).</summary>
        static class PlantRig
        {
            public static Transform Stem, Jaw;
            public static Vector3 JawAxis;
            public static readonly Transform[] Arms = new Transform[8];
        }

        /// <summary>
        /// A-77 (item 21): the carnivorous plant comes alive. Its stem sways a few degrees, its upper jaw snaps shut every
        /// 9 s (0.3 s to close, a 0.6 s hold, 2.5 s to gape again) and its arms writhe. Cosmetic and clock-driven, so every
        /// player sees much the same; skipped when hidden or past 500 m from the camera.
        /// </summary>
        static void SwayPlant()
        {
            if (PlantRig.Stem == null || PlantRig.Jaw == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - PlantRig.Stem.position).sqrMagnitude > 500f * 500f)
                return;
            float t = Time.time;
            PlantRig.Stem.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.37f) * 3f, 0f, Mathf.Sin(t * 0.29f + 1f) * 2.5f);
            float c = t % 9f;
            float shut = c < 0.3f ? c / 0.3f : c < 0.9f ? 1f : c < 3.4f ? 1f - Mathf.SmoothStep(0f, 1f, (c - 0.9f) / 2.5f) : 0f;
            PlantRig.Jaw.localRotation = Quaternion.AngleAxis(50f * shut, PlantRig.JawAxis);
            for (int k = 0; k < PlantRig.Arms.Length; k++)
                if (PlantRig.Arms[k] != null)
                    PlantRig.Arms[k].localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f + k * 1.3f) * 7f, Mathf.Sin(t * 0.23f + k) * 4f, Mathf.Sin(t * 0.41f + k * 2.1f) * 6f);
        }

        /// <summary>
        /// W-88 (#40, Zach's Ashlands design): the skeleton of a lizard about 80 m nose to tail, still standing on four
        /// splayed legs in the ash, ribcage open, the skull low with its jaw hanging and a frill of horns. It gives off
        /// ash from the ribs and kicks up a ring of dust at its feet. Details: a smouldering heart still glowing inside the
        /// cage, embers in the eye sockets, a snapped rib and a few teeth lying in the ash.
        /// </summary>
        static Transform BuildLizard(float3 at, float yaw)
        {
            var root = new GameObject("SkeletalLizard").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xA54Eu);
            // The spine from the tail tip (s = 0) to the neck (s = 1): the tail trails on the ground, the back arches.
            Vector3 Spine(float s) => new Vector3(5f * Mathf.Sin(s * Mathf.PI * 1.6f + 0.6f),
                                                  s < 0.35f ? Mathf.Lerp(1.6f, 10f, s / 0.35f) : 10f + 3f * Mathf.Sin((s - 0.35f) / 0.65f * Mathf.PI),
                                                  -48f + 76f * s);
            float Radius(float s) => s < 0.35f ? Mathf.Lerp(0.5f, 2f, s / 0.35f) : 2f;
            const int N = 44;
            for (int i = 0; i <= N; i++)
            {
                var s = i / (float)N;
                var v = Spine(s);
                var r = Radius(s);
                var along = (Spine(math.min(1f, s + 0.02f)) - Spine(math.max(0f, s - 0.02f))).normalized;
                var rot = Quaternion.LookRotation(along);
                m.Ellipsoid(v, new Vector3(r * 1.3f, r, r * 0.75f), rot, i % 2 == 0 ? Bone : BoneDark, 8, 5);
                m.Limb(v, v + Vector3.up * r * 2.4f - along * r * 0.6f, r * 0.35f, r * 0.08f, Bone, 5);       // the spine's ridge
                // Ribs hanging from the back between the hips and the shoulders, deepest mid-chest.
                if (s > 0.42f && s < 0.85f && i % 2 == 0)
                {
                    var depth = Mathf.Sin((s - 0.42f) / 0.43f * Mathf.PI);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var p0 = v + new Vector3(side * r, -0.3f, 0f);
                        var p3 = v + new Vector3(side * (4f + 3f * depth), -(4f + 6.5f * depth), 1f);
                        if (s > 0.6f && s < 0.66f && side > 0)
                            continue;                                                                          // the snapped rib's gap
                        Tentacle(m, p0, p0 + new Vector3(side * 5f, 1.5f, 0f), p3 + new Vector3(side * 4f, 3f, 0f), p3, 0.6f, 0.3f, Bone, Bone, 6);
                    }
                }
            }
            // Hips and shoulders: a flat pelvis and two shoulder blades; four splayed legs with knuckled joints and claws.
            for (int g = 0; g < 2; g++)
            {
                var s = g == 0 ? 0.38f : 0.86f;
                var v = Spine(s);
                m.Ellipsoid(v + Vector3.down * 0.8f, new Vector3(5f, 1.6f, 3.4f), Quaternion.identity, BoneDark, 10, 4);
                for (int side = -1; side <= 1; side += 2)
                {
                    var hip = v + new Vector3(side * 3.5f, -1.5f, 0f);
                    var elbow = v + new Vector3(side * 12f, 1f, g == 0 ? -3f : 3f);
                    var wrist = new Vector3(v.x + side * 14f, 1.8f, v.z + (g == 0 ? 1f : 6f));
                    m.Limb(hip, elbow, 1.5f, 1.2f, Bone, 7);
                    m.Ellipsoid(elbow, new Vector3(1.8f, 1.8f, 1.8f), Quaternion.identity, BoneDark, 8, 5);
                    m.Limb(elbow, wrist, 1.1f, 0.9f, Bone, 7);
                    m.Ellipsoid(wrist, new Vector3(1.4f, 1.2f, 1.4f), Quaternion.identity, BoneDark, 8, 5);
                    for (int t = 0; t < 4; t++)
                    {
                        var ta = (side * 30f + (t - 1.5f) * 24f) * Mathf.Deg2Rad;
                        var knuckle = wrist + new Vector3(Mathf.Sin(ta) * 2.6f, -1f, Mathf.Cos(ta) * 2.6f);
                        m.Limb(wrist, knuckle, 0.45f, 0.35f, Bone, 5);
                        m.Limb(knuckle, knuckle + new Vector3(Mathf.Sin(ta) * 1.6f, -0.9f, Mathf.Cos(ta) * 1.6f), 0.35f, 0.05f, BoneDark, 4);
                    }
                }
            }
            // Neck bones down to the skull, held low; the jaw hangs open, a frill of horns at the back.
            var neckBase = Spine(1f);
            var skull = new Vector3(neckBase.x + 1f, 8f, neckBase.z + 13f);
            for (int k = 0; k < 6; k++)
            {
                var p = Vector3.Lerp(neckBase, skull - new Vector3(0f, 0f, 4f), k / 5f) + Vector3.up * Mathf.Sin(k / 5f * Mathf.PI) * 2f;
                m.Ellipsoid(p, new Vector3(1.9f, 1.6f, 1.3f), Quaternion.identity, k % 2 == 0 ? Bone : BoneDark, 8, 5);
                m.Limb(p, p + new Vector3(0f, 2.4f, -0.6f), 0.6f, 0.1f, Bone, 5);
            }
            var pitch = Quaternion.Euler(12f, 6f, 0f);
            m.Ellipsoid(skull, new Vector3(4.4f, 3f, 7.5f), pitch, Bone, 12, 7);
            m.Ellipsoid(skull + pitch * new Vector3(0f, 1.6f, -2.5f), new Vector3(3.6f, 2.2f, 3.4f), pitch, BoneDark, 10, 5);   // the crown
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = skull + pitch * new Vector3(side * 2.9f, 1.4f, 1.5f);
                m.Ellipsoid(eye, new Vector3(1.3f, 1.1f, 1.5f), pitch, Hollow, 8, 5);
                m.Ellipsoid(eye + pitch * new Vector3(side * 0.6f, 0f, 0f), new Vector3(0.35f, 0.3f, 0.35f), pitch, Ember, 5, 3);
                m.Ellipsoid(skull + pitch * new Vector3(side * 1.1f, 1.6f, 6.6f), new Vector3(0.45f, 0.35f, 0.6f), pitch, Hollow, 5, 3); // nostrils
            }
            var jaw = Quaternion.Euler(34f, 6f, 0f);
            var jawHinge = skull + pitch * new Vector3(0f, -1.6f, -4.5f);
            m.Ellipsoid(jawHinge + jaw * new Vector3(0f, 0f, 6f), new Vector3(3.6f, 1.1f, 6.6f), jaw, BoneDark, 10, 5);
            for (int k = 0; k < 8; k++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var z = 1f + k * 0.8f;
                    var top = skull + pitch * new Vector3(side * (3.4f - k * 0.25f), -2.4f, z);
                    m.Limb(top, top + pitch * new Vector3(0f, -1.4f + k * 0.08f, 0.2f), 0.28f, 0.04f, Fang, 4);
                    var bottom = jawHinge + jaw * new Vector3(side * (3f - k * 0.25f), 0.9f, 5f + k * 0.8f);
                    m.Limb(bottom, bottom + jaw * new Vector3(0f, 1.2f, 0.2f), 0.25f, 0.04f, Fang, 4);
                }
            for (int k = 0; k < 5; k++)                                                                                    // the frill of horns
            {
                var a = Mathf.Lerp(-60f, 60f, k / 4f) * Mathf.Deg2Rad;
                var foot = skull + pitch * new Vector3(Mathf.Sin(a) * 3.4f, 2.2f, -5f);
                m.Limb(foot, foot + pitch * new Vector3(Mathf.Sin(a) * 3f, 2.5f + (k % 2) * 1.5f, -4f), 0.7f, 0.06f, BoneDark, 5);
            }
            // The smouldering heart inside the cage, and what has fallen into the ash: the snapped rib and a few teeth.
            var heart = Spine(0.6f) + new Vector3(0f, -6.5f, 0f);
            m.Ellipsoid(heart, new Vector3(2.2f, 1.8f, 2.4f), Quaternion.Euler(10f, 30f, 0f), Ember, 8, 5);
            m.Ellipsoid(heart + Vector3.down * 1.4f, new Vector3(3.4f, 0.8f, 3.4f), Quaternion.identity, Soot, 8, 3);
            var fallen = Spine(0.63f) + new Vector3(9f, 0f, 2f);
            m.Limb(new Vector3(fallen.x, 0.6f, fallen.z), new Vector3(fallen.x + 6f, 0.9f, fallen.z + 5f), 0.55f, 0.3f, Bone, 6);
            m.Limb(new Vector3(fallen.x + 6f, 0.9f, fallen.z + 5f), new Vector3(fallen.x + 8f, 0.5f, fallen.z + 9f), 0.3f, 0.2f, Bone, 6);
            for (int k = 0; k < 6; k++)
            {
                var p = skull + new Vector3(rng.NextFloat(-7f, 7f), 0f, rng.NextFloat(-2f, 9f));
                p.y = 0.7f;
                m.Limb(p, p + new Vector3(rng.NextFloat(-0.6f, 0.6f), 0.3f, rng.NextFloat(-1.2f, 1.2f)), 0.25f, 0.04f, Fang, 4);
            }
            var mesh = m.ToMesh("SkeletalLizard");
            Weather(mesh, Ash, 0.82f, 1f);
            AddPart("Bones", root, mesh);
            // Ash shed from the ribcage, and dust round the feet.
            Puffs(root, "Ash", Spine(0.62f), new Vector3(14f, 8f, 30f), new Color(0.34f, 0.32f, 0.31f, 0.35f), new Color(0.48f, 0.46f, 0.44f, 0.40f), 2.5f, 7f, 10f, 1.4f, 10f);
            Puffs(root, "Dust", Spine(0.6f) * 0f + new Vector3(0f, 1.5f, 0f), new Vector3(46f, 1f, 70f), new Color(0.55f, 0.50f, 0.45f, 0.20f), new Color(0.62f, 0.58f, 0.52f, 0.28f), 6f, 14f, 5f, 0.5f, 12f);
            return root;
        }

        // A-77 (item 24, Zach): near-black, like the floating rocks of the magnetic monolith it walks among.
        static readonly Color GolemBasalt = new Color(0.10f, 0.09f, 0.10f);
        static readonly Color BasaltPale = new Color(0.17f, 0.15f, 0.16f);
        static readonly Color Scorch = new Color(0.12f, 0.11f, 0.12f);

        /// <summary>
        /// W-89 (#40, Zach's ElectricWastelands design): a golem about 55 m tall built from the area's floating rocks, caught
        /// mid-stride. Every body part is its own jagged rock (the floating rocks' dark basalt with violet and blue shards),
        /// hanging a couple of metres from the next, and each one bobs and turns on its own; the floating rocks' arcs
        /// (ArcBolt, as between the arcing spires) crackle across every gap and hold it together (<see cref="GolemArcs"/>).
        /// Seeded per world: its size, the step, how far each arm swings and the tilt of the head. Details: shards orbiting
        /// the chest with arcs to some of them, crystal eyes, a scorched mound under each foot, and a rock it has dropped
        /// lying in the dirt with one weak arc still reaching up to its hand.
        /// </summary>
        static Transform BuildGolem(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("RockGolem").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.8f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var scale = rng.NextFloat(0.9f, 1.12f);
            root.localScale = Vector3.one * scale;
            var step = rng.NextFloat(4f, 7f);
            var swingL = rng.NextFloat(-6f, -2f);
            var swingR = rng.NextFloat(3f, 9f);
            var tilt = Quaternion.Euler(rng.NextFloat(-6f, 12f), rng.NextFloat(-20f, 20f), rng.NextFloat(-8f, 8f));
            var arcs = root.gameObject.AddComponent<GolemArcs>();
            var shape = Unity.Mathematics.Random.CreateFromIndex(seed ^ 0x9E37u);

            int Rock(string name, Vector3 centre, Vector3 radii, Quaternion rot, float bob, int shards)
            {
                var m = new DunesColossus.MeshBuilder();
                m.Ellipsoid(Vector3.zero, radii, rot, GolemBasalt, 7, 4);
                for (int k = 0; k < 3; k++)                                                                     // jagged lumps
                {
                    var d = (Vector3)shape.NextFloat3Direction();
                    m.Ellipsoid(Vector3.Scale(d, radii) * 0.55f, radii * shape.NextFloat(0.45f, 0.7f), Quaternion.Euler((Vector3)shape.NextFloat3(0f, 360f)), k == 0 ? BasaltPale : GolemBasalt, 6, 3);
                }
                for (int k = 0; k < shards; k++)                                                                // crystal shards poking out
                {
                    var d = (Vector3)shape.NextFloat3Direction();
                    var foot = Vector3.Scale(d, radii) * 0.85f;
                    m.Limb(foot, foot + d * shape.NextFloat(1.2f, 2.6f), shape.NextFloat(0.3f, 0.6f), 0.04f, k % 2 == 0 ? Violet : Crystal, 5);
                }
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                AddPart("Rock", go.transform, m.ToMesh(name));
                return arcs.AddSegment(go.transform, Mathf.Min(radii.x, Mathf.Min(radii.y, radii.z)) * 0.9f, bob, shape.NextFloat(0f, 6.28f));
            }

            // Legs mid-stride: the left forward, the right back with its heel lifted.
            int lFoot = Rock("LeftFoot", new Vector3(-5.5f, 2f, step), new Vector3(3.6f, 2f, 5f), Quaternion.identity, 0.1f, 1);
            int rFoot = Rock("RightFoot", new Vector3(5.5f, 3f, -step - 1f), new Vector3(3.4f, 2f, 4.6f), Quaternion.Euler(-18f, 0f, 0f), 0.2f, 1);
            int lShin = Rock("LeftShin", new Vector3(-5.5f, 10.5f, step * 0.7f), new Vector3(3.2f, 4f, 3.2f), Quaternion.Euler(-8f, 0f, 0f), 0.5f, 1);
            int rShin = Rock("RightShin", new Vector3(5.5f, 11f, -step * 0.8f), new Vector3(3.2f, 4f, 3.2f), Quaternion.Euler(14f, 0f, 0f), 0.5f, 1);
            int lThigh = Rock("LeftThigh", new Vector3(-5f, 20.5f, step * 0.35f), new Vector3(3.8f, 4.2f, 3.8f), Quaternion.Euler(-14f, 0f, 0f), 0.7f, 2);
            int rThigh = Rock("RightThigh", new Vector3(5f, 20.5f, -step * 0.35f), new Vector3(3.8f, 4.2f, 3.8f), Quaternion.Euler(12f, 0f, 0f), 0.7f, 2);
            int pelvis = Rock("Pelvis", new Vector3(0f, 28.5f, 0f), new Vector3(7f, 2.8f, 4.5f), Quaternion.identity, 0.8f, 2);
            int torso = Rock("Torso", new Vector3(0f, 40f, 0.8f), new Vector3(10f, 7f, 6.5f), Quaternion.Euler(6f, 0f, 0f), 0.9f, 6);
            int head = Rock("Head", new Vector3(0f, 52.5f, 2f), new Vector3(3.8f, 4f, 4.2f), tilt, 1.2f, 3);
            // Crystal eyes in the head's face.
            {
                var eyes = new DunesColossus.MeshBuilder();
                for (int side = -1; side <= 1; side += 2)
                    eyes.Ellipsoid(tilt * new Vector3(side * 1.5f, 0.6f, 3.9f), new Vector3(0.8f, 0.5f, 0.5f), tilt, Crystal, 6, 3);
                AddPart("Eyes", arcs.Segment(head), eyes.ToMesh("RockGolemEyes"));
            }
            // Arms swinging against the legs: shoulder, upper arm, forearm, fist, each hanging free.
            var fists = new int[2];
            for (int side = -1; side <= 1; side += 2)
            {
                var swing = side < 0 ? swingL : swingR;
                var n = side < 0 ? "Left" : "Right";
                int shoulder = Rock(n + "Shoulder", new Vector3(side * 15f, 45.5f, 0.5f), new Vector3(3.6f, 3.4f, 3.6f), Quaternion.identity, 1f, 2);
                int upper = Rock(n + "UpperArm", new Vector3(side * 17f, 37f, swing * 0.4f), new Vector3(3f, 4.2f, 3f), Quaternion.Euler(-swing * 2f, 0f, side * 6f), 1.1f, 1);
                int fore = Rock(n + "Forearm", new Vector3(side * 18f, 27f, swing * 0.9f), new Vector3(3f, 4f, 3f), Quaternion.Euler(-swing * 3f, 0f, side * 4f), 1.2f, 1);
                int fist = Rock(n + "Fist", new Vector3(side * 18.5f, 18f, swing * 1.4f), new Vector3(3.6f, 3.4f, 3.6f), Quaternion.identity, 1.3f, 3);
                fists[side < 0 ? 0 : 1] = fist;
                arcs.Link(torso, shoulder, 2);
                arcs.Link(shoulder, upper, 1);
                arcs.Link(upper, fore, 2);
                arcs.Link(fore, fist, 1);
            }
            arcs.Link(lFoot, lShin, 1); arcs.Link(lShin, lThigh, 2); arcs.Link(lThigh, pelvis, 1);
            arcs.Link(rFoot, rShin, 1); arcs.Link(rShin, rThigh, 2); arcs.Link(rThigh, pelvis, 1);
            arcs.Link(pelvis, torso, 3);
            arcs.Link(torso, head, 2);
            // Shards orbiting the chest; every other one is wired to it.
            for (int k = 0; k < 7; k++)
            {
                var a = k / 7f * Mathf.PI * 2f + rng.NextFloat(-0.3f, 0.3f);
                var p = new Vector3(Mathf.Cos(a) * 15f, 40f + rng.NextFloat(-6f, 7f), Mathf.Sin(a) * 11f);
                var piece = rng.NextFloat(0.8f, 1.6f);
                int shard = Rock("Shard" + k, p, new Vector3(piece, piece * 1.3f, piece), Quaternion.identity, 2f, 0);
                if (k % 2 == 0)
                    arcs.Link(torso, shard, 1);
            }

            // On the ground: a scorched, cracked mound under each foot, magnetic rubble, and the dropped rock.
            var g = new DunesColossus.MeshBuilder();
            foreach (var foot in new[] { new Vector3(-5.5f, 0.3f, step), new Vector3(5.5f, 0.3f, -step - 1f) })
            {
                g.Ellipsoid(foot, new Vector3(7f, 0.7f, 8f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Scorch, 10, 3);
                for (int k = 0; k < 4; k++)
                {
                    var ca = rng.NextFloat(0f, Mathf.PI * 2f);
                    g.Limb(foot + Vector3.up * 0.5f, foot + new Vector3(Mathf.Cos(ca) * 9f, 0.2f, Mathf.Sin(ca) * 9f), 0.35f, 0.08f, Hollow, 4);
                }
            }
            for (int k = 0; k < 14; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.NextFloat(10f, 28f);
                var r = rng.NextFloat(0.5f, 1.8f);
                p.y = r * 0.4f;
                g.Ellipsoid(p, new Vector3(r, r * 0.7f, r * 0.9f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), k % 3 == 0 ? BasaltPale : GolemBasalt, 6, 3);
            }
            var dropped = new Vector3(rng.NextFloat(22f, 26f), 1.6f, rng.NextFloat(-2f, 8f));
            g.Ellipsoid(dropped, new Vector3(3f, 2f, 2.6f), Quaternion.Euler(12f, 40f, 8f), GolemBasalt, 7, 4);
            g.Ellipsoid(dropped + new Vector3(0.4f, -1.2f, 0f), new Vector3(4.5f, 0.5f, 4f), Quaternion.identity, Scorch, 8, 3);
            g.Limb(dropped + Vector3.up * 1.6f, dropped + new Vector3(-0.6f, 4f, 0.4f), 0.4f, 0.04f, Violet, 5);
            AddPart("Ground", root, g.ToMesh("RockGolemGround"));
            var droppedPoint = new GameObject("DroppedRock").transform;
            droppedPoint.SetParent(root, false);
            droppedPoint.localPosition = dropped;
            arcs.Link(arcs.AddSegment(droppedPoint, 1.8f, 0f, 0f), fists[1], 1, 0.5f);
            return root;
        }

        static readonly Color SaltWhite = new Color(0.93f, 0.91f, 0.88f);
        static readonly Color SaltPink = new Color(0.90f, 0.78f, 0.76f);
        static readonly Color SaltDirt = new Color(0.42f, 0.34f, 0.26f);

        /// <summary>
        /// W-92 (#40, Zach's SaltFlat design): a giant rounded salt crystal, faceted and studded with short points,
        /// rolling slowly round a seeded circle about the spot and dragging a fading dirt trail behind it
        /// (<see cref="SaltCrystalRoll"/>). The root sits at the circle's centre. Details: dirt smeared on the ball where
        /// it has rolled through mud, and salt crumbs it has shed lying near the track.
        /// </summary>
        static Transform BuildCrystal(in TerrainSource source, float3 at, float yaw, uint seed)
        {
            var root = new GameObject("SaltCrystal").transform;
            root.position = new Vector3(at.x, at.y, at.z);
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var radius = rng.NextFloat(36f, CrystalCircleMax - 4f);
            // The ground round the circle, local to the root, so the ball and the trail follow the salt's rise and fall.
            var heights = new float[SaltCrystalRoll.Steps];
            for (int i = 0; i < heights.Length; i++)
            {
                var a = i * Mathf.PI * 2f / heights.Length;
                heights[i] = source.SampleHeight(new float2(at.x + Mathf.Cos(a) * radius, at.z + Mathf.Sin(a) * radius)) - at.y;
            }
            var m = new DunesColossus.MeshBuilder();
            // W-94 (#40, Zach): a meteoroid, not a lumpy sphere. One fine-faceted (32x22), slightly elongated body whose
            // surface is pushed in and out by seeded noise: broad swells, sharp ridges (folded noise) and round pits with
            // low rims. The crystals on it are small and low, so it can plausibly roll over them. Its own random, so the
            // circle, the crust and the crumbs below keep their draws.
            var lrng = Unity.Mathematics.Random.CreateFromIndex(seed ^ 0xBA11u);
            var radii = new Vector3(CrystalBall * lrng.NextFloat(1.08f, 1.18f), CrystalBall * lrng.NextFloat(0.86f, 0.94f), CrystalBall * lrng.NextFloat(0.92f, 1f));
            var turn = Quaternion.Euler(lrng.NextFloat(0f, 360f), lrng.NextFloat(0f, 360f), 0f);
            var o1 = lrng.NextFloat3(-50f, 50f); var o2 = lrng.NextFloat3(-50f, 50f); var o3 = lrng.NextFloat3(-50f, 50f);
            var pits = new float4[lrng.NextInt(8, 12)];
            for (int k = 0; k < pits.Length; k++)
                pits[k] = new float4(lrng.NextFloat3Direction(), lrng.NextFloat(0.16f, 0.42f));
            var pitDepth = lrng.NextFloat(0.045f, 0.07f);
            float Shape(Vector3 u)
            {
                var p = (float3)u;
                var f = 1f + 0.075f * noise.snoise(p * 1.3f + o1) + 0.03f * noise.snoise(p * 3.2f + o2);
                var ridge = 1f - math.abs(noise.snoise(p * 2.3f + o3));
                f += 0.05f * ridge * ridge * ridge - 0.015f;
                for (int k = 0; k < pits.Length; k++)
                {
                    var x = math.acos(math.clamp(math.dot(p, pits[k].xyz), -1f, 1f)) / pits[k].w;
                    var depth = pitDepth * (0.5f + pits[k].w);
                    if (x < 1f) f -= depth * (1f - x * x);
                    else if (x < 1.45f) f += depth * 0.3f * math.sin((x - 1f) / 0.45f * math.PI);
                }
                return f;
            }
            // The body's surface along a direction from its centre (root-local, before the roll).
            Vector3 Surface(Vector3 d)
            {
                var u = Vector3.Scale(Quaternion.Inverse(turn) * d, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z)).normalized;
                return turn * Vector3.Scale(u, radii) * Shape(u);
            }
            m.Rock(Vector3.zero, radii, turn, SaltWhite, 32, 22, Shape);
            int outcrops = lrng.NextInt(2, 4);
            for (int k = 0; k < outcrops; k++)
            {
                var d = (Vector3)lrng.NextFloat3Direction();
                var s = Surface(d);
                m.Limb(s * 0.85f, s * lrng.NextFloat(1.08f, 1.15f), lrng.NextFloat(1.8f, 2.6f), lrng.NextFloat(0.3f, 0.8f), k == 0 ? SaltPink : SaltWhite, 5);
            }
            for (int k = 0; k < 14; k++)
            {
                var d = Quaternion.Euler(rng.NextFloat(-80f, 80f), rng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                var s = Surface(d);
                m.Limb(s * 0.9f, s * rng.NextFloat(1.03f, 1.08f), rng.NextFloat(0.8f, 1.5f), 0.2f, k % 3 == 0 ? SaltPink : SaltWhite, 5);
            }
            for (int k = 0; k < 7; k++)
            {
                var d = Quaternion.Euler(rng.NextFloat(-70f, 70f), rng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                m.Ellipsoid(Surface(d) * 0.99f, new Vector3(rng.NextFloat(2f, 4f), rng.NextFloat(1.5f, 3f), 0.5f), Quaternion.LookRotation(d), SaltDirt, 6, 3);
            }
            AddPart("Ball", root, m.ToMesh("SaltCrystal"));
            // Salt crumbs shed beside the track.
            var g = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 10; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = radius + rng.NextFloat(-9f, 9f);
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = source.SampleHeight(new float2(at.x + p.x, at.z + p.z)) - at.y + 0.2f;
                var s = rng.NextFloat(0.4f, 1.2f);
                g.Ellipsoid(p, new Vector3(s, s * 0.7f, s * 0.9f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), k % 4 == 0 ? SaltPink : SaltWhite, 6, 3);
            }
            AddPart("Crumbs", root, g.ToMesh("SaltCrystalCrumbs"));
            var roll = root.gameObject.AddComponent<SaltCrystalRoll>();
            roll.Init(root.Find("Ball"), radius, CrystalBall, heights, math.radians(yaw), rng.NextBool() ? 1f : -1f, Resources.Load<Material>("Colossus"), lrng.NextUInt());
            Debug.Log($"StoneColossi: SaltCrystal circle radius {radius:F0} m round ({at.x:F0}, {at.z:F0}), ball {CrystalBall:F0} m, lap {SaltCrystalRoll.LapSeconds(radius):F0} s, trail {SaltCrystalRoll.TrailLength:F0} m");
            return root;
        }

        static readonly Color Saguaro = new Color(0.34f, 0.47f, 0.28f);
        static readonly Color SaguaroDark = new Color(0.22f, 0.33f, 0.19f);
        static readonly Color SpinePale = new Color(0.88f, 0.84f, 0.68f);
        static readonly Color Blossom = new Color(0.96f, 0.95f, 0.88f);
        static readonly Color Pollen = new Color(0.96f, 0.80f, 0.28f);
        static readonly Color Fruit = new Color(0.74f, 0.20f, 0.24f);
        static readonly Color Dirt = new Color(0.50f, 0.39f, 0.28f);

        /// <summary>
        /// W-89 (#40, Zach's Sonoran design): a saguaro about 50 m tall walking across the desert on its roots, mid-stride:
        /// a ribbed, spined trunk leaning into the step on three root legs (one lifted, dripping soil), and two to four
        /// arms (one may droop, as old saguaros' do). Seeded per world: height, lean, the arms and which one droops.
        /// Details: white blossoms and red fruit on the crown and arm tips, woodpecker holes in the trunk, a hawk perched
        /// on an arm, footprint holes in a line behind it, and dust kicked up round the feet.
        /// </summary>
        static Transform BuildSaguaro(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("WalkingSaguaro").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.6f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var height = rng.NextFloat(44f, 54f);
            var lean = rng.NextFloat(4f, 9f);
            const float R = 4.6f;
            var trunkBase = new Vector3(0f, 9f, 0f);
            Vector3 Trunk(float t) => trunkBase + new Vector3(0f, (height - trunkBase.y) * t, lean * t * t);

            // A ribbed column with pale spines along every rib.
            void Ribbed(System.Func<float, Vector3> path, float radius, int steps, int ribs)
            {
                for (int s = 0; s < steps; s++)
                    m.Limb(path(s / (float)steps), path((s + 1f) / steps), radius * 0.9f, radius * 0.9f, SaguaroDark, ribs);
                for (int k = 0; k < ribs; k++)
                {
                    var a = k * Mathf.PI * 2f / ribs;
                    for (int s = 0; s < steps; s++)
                    {
                        Vector3 p0 = path(s / (float)steps), p1 = path((s + 1f) / steps);
                        var along = (p1 - p0).normalized;
                        var side = Vector3.Cross(along, Vector3.right).sqrMagnitude > 0.1f ? Vector3.Cross(along, Vector3.right).normalized : Vector3.forward;
                        var out0 = Quaternion.AngleAxis(a * Mathf.Rad2Deg, along) * side;
                        m.Limb(p0 + out0 * radius * 0.85f, p1 + out0 * radius * 0.85f, radius * 0.22f, radius * 0.22f, Saguaro, 4);
                        if ((s + k) % 2 == 0)
                        {
                            var sp = Vector3.Lerp(p0, p1, 0.5f) + out0 * radius * 1.05f;
                            m.Limb(sp, sp + out0 * radius * 0.16f + along * 0.1f, 0.07f, 0.01f, SpinePale, 3);
                        }
                    }
                }
                var top = path(1f);
                m.Ellipsoid(top, new Vector3(radius, radius * 0.8f, radius), Quaternion.identity, Saguaro, 12, 5);
            }
            void Bloom(Vector3 top, float radius, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var p = top + new Vector3(Mathf.Cos(a) * radius * 0.55f, radius * 0.6f, Mathf.Sin(a) * radius * 0.55f);
                    if (k % 3 == 2)
                        m.Ellipsoid(p, new Vector3(0.35f, 0.5f, 0.35f) * radius * 0.3f, Quaternion.identity, Fruit, 6, 3);
                    else
                    {
                        m.Ellipsoid(p, new Vector3(0.6f, 0.2f, 0.6f) * radius * 0.3f, Quaternion.identity, Blossom, 8, 3);
                        m.Ellipsoid(p + Vector3.up * 0.1f, new Vector3(0.2f, 0.12f, 0.2f) * radius * 0.3f, Quaternion.identity, Pollen, 5, 2);
                    }
                }
            }
            Ribbed(Trunk, R, 10, 14);
            Bloom(Trunk(1f), R, 9);
            // Arms: out from the trunk, a rounded elbow, then up (or, for the drooping one, down and out).
            var arms = rng.NextInt(2, 5);
            var droop = rng.NextInt(0, arms + 1);                                                           // arms means none droops
            Vector3 perch = default;
            for (int k = 0; k < arms; k++)
            {
                var a = (k * 360f / arms + rng.NextFloat(-25f, 25f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var t = rng.NextFloat(0.35f, 0.7f);
                var from = Trunk(t);
                var out1 = from + d * rng.NextFloat(8f, 11f) + Vector3.up * 1.5f;
                var r = R * rng.NextFloat(0.55f, 0.7f);
                var rise = k == droop ? -rng.NextFloat(6f, 10f) : rng.NextFloat(8f, 16f);
                var tip = out1 + Vector3.up * rise + d * (k == droop ? 3f : 0.5f);
                Ribbed(s => Vector3.Lerp(from, out1, Mathf.Min(1f, s * 2f)) + (s > 0.5f ? (tip - out1) * (s - 0.5f) * 2f : Vector3.zero), r, 6, 10);
                m.Ellipsoid(out1, Vector3.one * r * 0.95f, Quaternion.identity, Saguaro, 10, 5);
                if (k != droop)
                    Bloom(tip, r, 4);
                if (k == 0)
                    perch = tip + Vector3.up * r * 0.8f;
            }
            // A hawk perched on the first arm, looking out.
            m.Ellipsoid(perch + Vector3.up * 0.5f, new Vector3(0.35f, 0.55f, 0.4f), Quaternion.Euler(-15f, 0f, 0f), new Color(0.32f, 0.24f, 0.18f), 6, 4);
            m.Ellipsoid(perch + new Vector3(0f, 1.1f, 0.1f), new Vector3(0.22f, 0.22f, 0.24f), Quaternion.identity, new Color(0.85f, 0.80f, 0.72f), 6, 3);
            // Woodpecker holes, with darker rims.
            for (int k = 0; k < 4; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = Trunk(rng.NextFloat(0.2f, 0.8f)) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R * 1.02f;
                m.Ellipsoid(p, new Vector3(0.9f, 1.1f, 0.9f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), SaguaroDark, 6, 3);
                m.Ellipsoid(p + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.3f, new Vector3(0.55f, 0.75f, 0.55f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), Hollow, 6, 3);
            }
            // A-77 (item 23, Zach): the legs read as a root system. A flared bole under the trunk, then three bundles of
            // gnarled roots: two planted wide and sinking into the sand, the third trailing behind mid-step, soil hanging off.
            m.Ellipsoid(trunkBase + Vector3.up * 0.5f, new Vector3(R * 1.45f, 2.6f, R * 1.45f), Quaternion.identity, SaguaroDark, 14, 5);
            for (int k = 0; k < 3; k++)
            {
                var a = (k * 120f + 180f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var lifted = k == 0;
                var foot = d * (lifted ? 11f : 12f) + Vector3.up * (lifted ? rng.NextFloat(4f, 6f) : 0.5f);
                for (int s = 0; s < 4; s++)
                {
                    var off = s - 1.5f;
                    var from = trunkBase + d * 2.4f + side * off * 1.3f + Vector3.down * rng.NextFloat(0f, 1.5f);
                    var end = foot + side * off * 1.8f + d * rng.NextFloat(-1f, 1.5f) + Vector3.down * (lifted ? 0f : 1f);
                    var kink = Vector3.Lerp(from, end, 0.45f) + side * rng.NextFloat(-1.6f, 1.6f) + Vector3.up * rng.NextFloat(1f, 3f);
                    Tentacle(m, from, kink + d * 1.5f, end + Vector3.up * (lifted ? 2.5f : 4f) - d * 1.5f, end, R * 0.3f, 0.3f, SaguaroDark, Saguaro, 9);
                    var fork = Vector3.Lerp(kink, end, 0.4f);
                    m.Limb(fork, fork + side * (off < 0f ? -1f : 1f) * rng.NextFloat(1.5f, 3f) + d * rng.NextFloat(1f, 2f) + Vector3.down * (lifted ? 0.6f : 2f),
                           0.55f, 0.12f, SaguaroDark, 5);
                }
                for (int r = 0; r < 5; r++)
                {
                    var ra = Mathf.Atan2(d.x, d.z) + (r - 2f) * 0.45f;
                    var to = foot + new Vector3(Mathf.Sin(ra) * rng.NextFloat(3f, 5f), lifted ? -rng.NextFloat(1f, 2.5f) : -0.4f, Mathf.Cos(ra) * rng.NextFloat(3f, 5f));
                    m.Limb(foot, to, 0.6f, 0.1f, Dirt, 5);
                    if (lifted)
                        m.Ellipsoid(to + Vector3.down * 0.3f, Vector3.one * rng.NextFloat(0.4f, 0.8f), Quaternion.identity, Dirt, 5, 3);   // clods of soil
                }
                if (!lifted)
                    m.Ellipsoid(foot + Vector3.down * 0.2f, new Vector3(5f, 0.6f, 5f), Quaternion.identity, Dirt, 10, 3);
            }
            // Footprints behind it: holes torn out of the ground, a line back the way it came.
            for (int k = 1; k <= 5; k++)
            {
                var p = new Vector3((k % 2 == 0 ? -4f : 4f) + rng.NextFloat(-1f, 1f), 0.25f, -10f - k * 11f);
                m.Ellipsoid(p, new Vector3(4.5f, 0.5f, 4f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Dirt, 10, 3);
                m.Ellipsoid(p + Vector3.up * 0.15f, new Vector3(3f, 0.4f, 2.6f), Quaternion.identity, new Color(0.30f, 0.23f, 0.17f), 8, 3);
            }
            AddPart("Body", root, m.ToMesh("WalkingSaguaro"));
            Puffs(root, "Dust", new Vector3(0f, 1.5f, 2f), new Vector3(28f, 1f, 24f), new Color(0.66f, 0.55f, 0.42f, 0.20f), new Color(0.74f, 0.64f, 0.50f, 0.28f), 4f, 10f, 4f, 0.5f, 10f);
            return root;
        }

        static readonly Color MesaRed = new Color(0.62f, 0.31f, 0.19f);
        static readonly Color MesaOrange = new Color(0.74f, 0.45f, 0.25f);
        static readonly Color MesaCream = new Color(0.83f, 0.71f, 0.53f);
        static readonly Color MesaBrown = new Color(0.46f, 0.29f, 0.20f);
        static readonly Color MesaDust = new Color(0.72f, 0.58f, 0.40f);
        static readonly Color ScrubGreen = new Color(0.34f, 0.39f, 0.19f);
        static readonly Color ScrubDry = new Color(0.56f, 0.50f, 0.30f);

        /// <summary>
        /// W-106 row 2 (#40): a rock tortoise in the canyonlands, about 40 m long and 24 m high, that reads as a small
        /// mesa from afar: its shell is a stack of flat strata (red, orange, cream and brown bands, each with its own
        /// ragged cliff line, narrowing a little going up) under an overhanging caprock with scrub, two small cacti and
        /// a walker's cairn on top, and a skirt of fallen talus round the foot. Close to, four stubby feet with blunt toes
        /// show under the lowest band and a head with heavy-lidded eyes rests on the ground at the front, half hidden by
        /// the overhang. Details: a strata slab fallen and leaning on a foot, a dead juniper on the rim, a pale lizard
        /// sunning on the head. Seeded, so each world's tortoise has its own cliff lines and scrub. Body (shell, legs,
        /// head) and Scrub are separate parts, laid out as the giant tortoise is (shell centred, neck forward on +z), so
        /// the animal lane can rig it later (A-72 hooks: Spots, Root).
        /// </summary>
        static Transform BuildMesaTortoise(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("MesaTortoise").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);

            // A flat slab with a ragged cliff round it: a sphere pushed out to a cylinder (flat top and bottom), its
            // sides moved in and out by three seeded waves round the slab.
            void Slab(Vector3 centre, Vector3 radii, Color color, float ragged)
            {
                float p1 = rng.NextFloat(0f, 6.3f), p2 = rng.NextFloat(0f, 6.3f), p3 = rng.NextFloat(0f, 6.3f);
                m.Rock(centre, radii, Quaternion.identity, color, 28, 8, u =>
                {
                    var hor = Mathf.Sqrt(u.x * u.x + u.z * u.z);
                    var box = 1f / Mathf.Max(hor, Mathf.Abs(u.y), 0.001f);
                    if (hor + 0.001f < Mathf.Abs(u.y))
                        return box;
                    var a = Mathf.Atan2(u.z, u.x);
                    return box * (1f + ragged * (0.5f * Mathf.Sin(3f * a + p1) + 0.3f * Mathf.Sin(7f * a + p2) + 0.2f * Mathf.Sin(13f * a + p3)));
                });
            }

            // Four stubby legs with blunt toes under the lowest band; the front right one set forward (mid-step).
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var forward = k == 1 ? 2.5f : 0f;
                var hip = new Vector3(sx * 11f, 6.5f, sz * 11f);
                var foot = new Vector3(sx * 14.5f, 1.4f, sz * 14f + forward);
                m.Limb(hip, foot, 4.6f, 4f, MesaBrown);
                m.Ellipsoid(foot + new Vector3(0f, -0.5f, sz * 1.4f), new Vector3(4.4f, 1.8f, 4.8f), Quaternion.identity, MesaRed, 8, 4);
                for (int t = -1; t <= 1; t++)
                    m.Ellipsoid(foot + new Vector3(t * 2.1f, -0.6f, sz * 5f), new Vector3(1f, 0.8f, 0.9f), Quaternion.identity, MesaCream, 5, 3);
            }
            // The underside (the plastron), then the strata, each a little narrower going up, then the caprock.
            Slab(new Vector3(0f, 5.4f, 0f), new Vector3(16.5f, 1.4f, 19f), MesaBrown, 0.03f);
            Color[] bands = { MesaRed, MesaOrange, MesaCream, MesaRed, MesaBrown, MesaOrange };
            var y = 6.6f;
            var layers = rng.NextInt(5, 7);
            for (int k = 0; k < layers; k++)
            {
                var h = rng.NextFloat(2.4f, 3.8f);
                var shrink = 1f - k * 0.035f;
                var color = Color.Lerp(bands[(k + (int)(seed % 3u)) % bands.Length], MesaDust, rng.NextFloat(0f, 0.2f));
                Slab(new Vector3(rng.NextFloat(-0.4f, 0.4f), y + h * 0.5f, rng.NextFloat(-0.4f, 0.4f)), new Vector3(17.5f * shrink, h * 0.5f, 20f * shrink), color, rng.NextFloat(0.04f, 0.08f));
                y += h * 0.98f;
            }
            var capTop = y + 1.6f;
            Slab(new Vector3(0f, y + 0.8f, -0.5f), new Vector3(17.5f * (1f - layers * 0.035f) + 1.4f, 0.8f, 20f * (1f - layers * 0.035f) + 1.6f), MesaBrown, 0.05f);

            // The head resting on the ground at the front, under the overhang: a heavy brow, lidded eyes, a hooked beak.
            m.Limb(new Vector3(0f, 6f, 17f), new Vector3(1f, 4f, 24f), 4.4f, 3.6f, MesaBrown);
            var look = Quaternion.Euler(14f, rng.NextFloat(-18f, 18f), 0f);
            var head = new Vector3(1.5f, 3.6f, 27.5f);
            m.Ellipsoid(head, new Vector3(4.2f, 3.4f, 5.4f), look, MesaRed);
            m.Ellipsoid(head + look * new Vector3(0f, 1.8f, 1.6f), new Vector3(4f, 1.1f, 3.4f), look, MesaBrown);                 // brow
            m.Ellipsoid(head + look * new Vector3(-2.9f, 0.9f, 2.6f), new Vector3(0.9f, 0.5f, 0.8f), look, Hollow);              // lidded eyes
            m.Ellipsoid(head + look * new Vector3(2.9f, 0.9f, 2.6f), new Vector3(0.9f, 0.5f, 0.8f), look, Hollow);
            m.Ellipsoid(head + look * new Vector3(0f, -0.9f, 4.8f), new Vector3(2.2f, 1.8f, 1.6f), look, MesaBrown);              // beak
            m.Limb(new Vector3(0f, 5f, -20f), new Vector3(-1.5f, 1.4f, -25f), 2.6f, 0.6f, MesaBrown);                          // stubby tail
            // Talus round the foot: broken blocks in the band colours, sunk a little.
            for (int k = 0; k < 34; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(1.05f, 1.35f);
                var size = rng.NextFloat(0.6f, 2.2f);
                var p = new Vector3(Mathf.Cos(a) * 18f * r, size * 0.25f, Mathf.Sin(a) * 21f * r);
                if (p.z > 20f && Mathf.Abs(p.x) < 8f)
                    continue;                                                                                   // keep the head clear
                m.Ellipsoid(p, new Vector3(size * 1.3f, size * 0.7f, size), Quaternion.Euler(rng.NextFloat(-20f, 20f), rng.NextFloat(0f, 360f), rng.NextFloat(-15f, 15f)), bands[rng.NextInt(bands.Length)], 6, 4);
            }
            // A strata slab broken off and leaning on the back left foot.
            m.Ellipsoid(new Vector3(-19f, 3.2f, -13f), new Vector3(1f, 3.6f, 4.8f), Quaternion.Euler(0f, 30f, -28f), MesaCream, 6, 4);
            var mesh = m.ToMesh("MesaTortoise");
            Weather(mesh, MesaDust, 0.6f, 0.95f);
            AddPart("Body", root, mesh);

            // On top, in its own part so the dust pass leaves its colours: scrub, two small cacti, a cairn, a dead juniper.
            var f = new DunesColossus.MeshBuilder();
            var topR = new Vector2(15f, 17f) * (1f - layers * 0.035f);
            Vector3 OnTop(float edge)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = Mathf.Sqrt(rng.NextFloat()) * edge;
                return new Vector3(Mathf.Cos(a) * topR.x * r, capTop - 0.1f, Mathf.Sin(a) * topR.y * r - 0.5f);
            }
            var bushes = rng.NextInt(14, 22);
            for (int k = 0; k < bushes; k++)
            {
                var p = OnTop(0.95f);
                var s = rng.NextFloat(0.5f, 1.4f);
                var c = Color.Lerp(ScrubGreen, ScrubDry, rng.NextFloat());
                f.Ellipsoid(p + Vector3.up * s * 0.4f, new Vector3(s, s * 0.7f, s * rng.NextFloat(0.8f, 1.2f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), c, 7, 4);
                if (rng.NextFloat() < 0.4f)
                    f.Ellipsoid(p + new Vector3(s * 0.7f, s * 0.3f, s * 0.4f), new Vector3(s * 0.6f, s * 0.45f, s * 0.6f), Quaternion.identity, c * 0.9f, 6, 3);
            }
            for (int k = 0; k < 2; k++)
            {
                var p = OnTop(0.8f);
                var h = rng.NextFloat(1.6f, 2.6f);
                f.Limb(p, p + Vector3.up * h, 0.32f, 0.28f, new Color(0.30f, 0.42f, 0.24f), 8);
                f.Limb(p + Vector3.up * h * 0.45f, p + new Vector3(0.7f, h * 0.55f, 0f), 0.2f, 0.2f, new Color(0.30f, 0.42f, 0.24f), 6);
                f.Limb(p + new Vector3(0.7f, h * 0.55f, 0f), p + new Vector3(0.75f, h * 0.85f, 0f), 0.2f, 0.17f, new Color(0.30f, 0.42f, 0.24f), 6);
            }
            var cairn = OnTop(0.5f);
            for (int k = 0; k < 4; k++)
            {
                var s = 0.75f - k * 0.13f;
                f.Ellipsoid(cairn + new Vector3(rng.NextFloat(-0.08f, 0.08f), 0.3f + k * 0.62f * (0.9f - k * 0.08f), 0f), new Vector3(s, s * 0.5f, s * 0.85f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), rng.NextFloat(-8f, 8f)), k % 2 == 0 ? MesaCream : Dark, 7, 4);
            }
            var rimA = rng.NextFloat(0f, Mathf.PI * 2f);
            var trunk = new Vector3(Mathf.Cos(rimA) * topR.x * 0.92f, capTop - 0.2f, Mathf.Sin(rimA) * topR.y * 0.92f - 0.5f);
            var bark = new Color(0.42f, 0.36f, 0.30f);
            var crook = trunk + new Vector3(Mathf.Cos(rimA) * 1.2f, 2.4f, Mathf.Sin(rimA) * 1.2f);
            f.Limb(trunk, crook, 0.35f, 0.24f, bark, 6);
            f.Limb(crook, crook + new Vector3(Mathf.Cos(rimA) * 1.8f, 1.2f, Mathf.Sin(rimA) * 1.8f), 0.22f, 0.08f, bark, 5);
            f.Limb(crook, crook + new Vector3(-Mathf.Sin(rimA) * 1.2f, 1.6f, Mathf.Cos(rimA) * 1.2f), 0.18f, 0.06f, bark, 5);
            f.Limb(trunk + Vector3.up * 1.2f, trunk + new Vector3(Mathf.Sin(rimA) * 1.3f, 2f, -Mathf.Cos(rimA) * 1.3f), 0.15f, 0.05f, bark, 5);
            // A pale lizard sunning on the head, tail curled.
            var sun = head + look * new Vector3(0.6f, 3.3f, 0.5f);
            var lizard = new Color(0.86f, 0.80f, 0.66f);
            f.Ellipsoid(sun, new Vector3(0.18f, 0.1f, 0.42f), look, lizard, 6, 3);
            f.Ellipsoid(sun + look * new Vector3(0f, 0.02f, 0.5f), new Vector3(0.12f, 0.08f, 0.16f), look, lizard, 5, 3);
            f.Limb(sun + look * new Vector3(0f, 0f, -0.4f), sun + look * new Vector3(0.35f, 0f, -0.85f), 0.07f, 0.02f, lizard, 4);
            AddPart("Scrub", root, f.ToMesh("MesaTortoiseScrub"));
            return root;
        }

        static readonly Color GlassPale = new Color(0.80f, 0.92f, 0.93f);
        static readonly Color GlassTeal = new Color(0.46f, 0.72f, 0.76f);
        static readonly Color GlassDeep = new Color(0.24f, 0.44f, 0.52f);
        static readonly Color GlassSmoke = new Color(0.30f, 0.37f, 0.42f);
        static readonly Color GlassSand = new Color(0.84f, 0.80f, 0.70f);

        /// <summary>
        /// A-76 row 1 (#40): a heron of cut glass about 30 m tall wading the GlassDesert's dunes as if they were shallows.
        /// Every part is a low, few-sided shape so it reads as faceted glass, in pale, sea and deep teal tones: a body with
        /// folded wings ending in long shard primaries, an S-necked head with an amber dagger beak and a crest of two glass
        /// plumes, and two stilt legs with backward-bending ankles and splayed toes sunk into small sand drifts. Details:
        /// shed shard feathers lying round its feet, a branching fulgurite (lightning glass) beside it, a small sandpiper
        /// standing on its back. Body (body, neck, head, legs) and Litter are separate parts, laid out with the hips at
        /// (0, 16, 0) and the neck forward on +z, so ColossusRigs' heron rig can stab and walk it (A-72 hooks: Spots, Root).
        /// </summary>
        static Transform BuildGlassHeron(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("GlassHeron").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            Color Tone() => rng.NextFloat() < 0.5f ? Color.Lerp(GlassPale, GlassTeal, rng.NextFloat(0f, 0.6f)) : Color.Lerp(GlassTeal, GlassDeep, rng.NextFloat(0f, 0.7f));

            // Two stilt legs: thigh from the hip down and back to the ankle, shin down and forward to the foot, three
            // toes forward and one back; the right foot a little ahead (mid-wade).
            for (int s = -1; s <= 1; s += 2)
            {
                var ahead = s > 0 ? 1.6f : -0.6f;
                var hip = new Vector3(s * 2.2f, 16f, 0f);
                var ankle = new Vector3(s * 2.5f, 8f, -1.6f + ahead * 0.4f);
                var foot = new Vector3(s * 2.7f, 0.5f, 0.4f + ahead);
                m.Limb(hip, ankle, 0.9f, 0.55f, GlassSmoke, 6);
                m.Ellipsoid(ankle, new Vector3(0.75f, 0.75f, 0.75f), Quaternion.identity, GlassDeep, 6, 4);
                m.Limb(ankle, foot, 0.55f, 0.45f, GlassSmoke, 6);
                for (int t = -1; t <= 1; t++)
                    m.Limb(foot, foot + new Vector3(t * 1.9f, -0.35f, 3.6f - math.abs(t) * 0.6f), 0.32f, 0.1f, GlassSmoke, 5);
                m.Limb(foot, foot + new Vector3(0f, -0.3f, -2.2f), 0.28f, 0.08f, GlassSmoke, 5);
                m.Ellipsoid(foot + new Vector3(0f, -0.6f, 0.8f), new Vector3(3.2f, 0.7f, 3.8f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), GlassSand, 7, 3);   // sand drift
            }
            // The body, tipped up at the front, a paler breast under it and a short tail of shards behind.
            var tip = Quaternion.Euler(-14f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 19f, 0f), new Vector3(4.2f, 4.4f, 8f), tip, GlassTeal, 8, 5);
            m.Ellipsoid(new Vector3(0f, 17.6f, 2.6f), new Vector3(3.4f, 3f, 5f), tip, GlassPale, 7, 4);
            for (int k = 0; k < 5; k++)
                m.Ellipsoid(new Vector3((k - 2) * 0.9f, 17.4f - math.abs(k - 2) * 0.3f, -8.6f), new Vector3(0.5f, 0.25f, 2.6f), Quaternion.Euler(18f, (k - 2) * 7f, 0f), Tone(), 4, 3);
            // Folded wings along the sides, each ending in a fan of long shard primaries crossing over the tail.
            for (int s = -1; s <= 1; s += 2)
            {
                var wing = Quaternion.Euler(-10f, s * 4f, s * 6f);
                m.Ellipsoid(new Vector3(s * 3.7f, 19.6f, -1f), new Vector3(1.3f, 3.6f, 8.4f), wing, GlassTeal, 6, 4);
                var shards = rng.NextInt(6, 9);
                for (int k = 0; k < shards; k++)
                {
                    var y = 20.8f - k * 0.55f;
                    var len = rng.NextFloat(3.4f, 5.6f);
                    m.Ellipsoid(new Vector3(s * (3.2f + k * 0.08f), y, -7.4f - len * 0.4f), new Vector3(0.45f, 0.18f, len), Quaternion.Euler(14f + k * 1.5f, -s * (6f + k * 1.6f), 0f), Tone(), 4, 3);
                }
            }
            // The S-neck: up and back from the breast, then forward to the head.
            Tentacle(m, new Vector3(0f, 20.5f, 5.5f), new Vector3(0f, 26f, 2.4f), new Vector3(0f, 24.5f, 8.4f), new Vector3(0f, 29.4f, 7.6f), 1.6f, 0.95f, GlassPale, GlassTeal, 12);
            var head = new Vector3(0f, 30f, 8f);
            m.Ellipsoid(head, new Vector3(1.3f, 1.4f, 2.2f), Quaternion.Euler(-6f, 0f, 0f), GlassPale, 7, 4);
            m.Limb(head + new Vector3(0f, -0.2f, 1.8f), head + new Vector3(0f, -0.5f, 7f), 0.62f, 0.05f, new Color(0.86f, 0.74f, 0.42f), 5);   // amber dagger beak
            for (int s = -1; s <= 1; s += 2)
                m.Ellipsoid(head + new Vector3(s * 1.05f, 0.35f, 0.9f), new Vector3(0.3f, 0.3f, 0.34f), Quaternion.identity, Hollow, 5, 3);
            m.Ellipsoid(head + new Vector3(0f, 1.1f, -0.2f), new Vector3(0.7f, 0.4f, 1.6f), Quaternion.identity, GlassDeep, 5, 3);   // dark cap
            for (int k = 0; k < 2; k++)                                                                                       // two crest plumes trailing back
                m.Limb(head + new Vector3((k - 0.5f) * 0.5f, 1f, -1f), head + new Vector3((k - 0.5f) * 1.4f, 1.6f + k * 0.4f, -6.5f + k * 1.2f), 0.22f, 0.04f, GlassPale, 4);
            AddPart("Body", root, m.ToMesh("GlassHeron"));

            // Round its feet, in its own part (the rig leaves it lying): shed shard feathers, a fulgurite, a sandpiper on its back.
            var f = new DunesColossus.MeshBuilder();
            var feathers = rng.NextInt(10, 16);
            for (int k = 0; k < feathers; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(5f, 17f);
                var len = rng.NextFloat(1.2f, 3.4f);
                f.Ellipsoid(new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r), new Vector3(0.35f, 0.14f, len), Quaternion.Euler(rng.NextFloat(-8f, 8f), rng.NextFloat(0f, 360f), rng.NextFloat(-20f, 20f)), Tone(), 4, 3);
            }
            // A fulgurite: a crooked tube of fused sand standing out of a dune, branching twice.
            var fa = rng.NextFloat(0f, Mathf.PI * 2f);
            var fb = new Vector3(Mathf.Cos(fa) * 20f, -0.4f, Mathf.Sin(fa) * 20f);
            var fulg = new Color(0.62f, 0.58f, 0.50f);
            var f1 = fb + new Vector3(0.6f, 2.6f, -0.3f);
            var f2 = f1 + new Vector3(-0.5f, 2f, 0.4f);
            f.Limb(fb, f1, 0.55f, 0.42f, fulg, 6);
            f.Limb(f1, f2, 0.42f, 0.22f, fulg, 6);
            f.Limb(f1, f1 + new Vector3(1.3f, 1.1f, 0.2f), 0.26f, 0.08f, fulg, 5);
            f.Limb(f2, f2 + new Vector3(0.2f, 1f, -0.7f), 0.2f, 0.06f, fulg, 5);
            AddPart("Litter", root, f.ToMesh("GlassHeronLitter"));
            // A sandpiper standing on its back, facing aft, in a part of its own that rides the body bone.
            var b = new DunesColossus.MeshBuilder();
            var bird = new Vector3(0.6f, 23.3f, -2f);
            var down = new Color(0.62f, 0.54f, 0.44f);
            b.Ellipsoid(bird, new Vector3(0.22f, 0.2f, 0.36f), Quaternion.Euler(-10f, 180f, 0f), down, 6, 3);
            b.Ellipsoid(bird + new Vector3(0f, 0.2f, -0.34f), new Vector3(0.13f, 0.13f, 0.14f), Quaternion.identity, down, 5, 3);
            b.Limb(bird + new Vector3(0f, 0.18f, -0.46f), bird + new Vector3(0f, 0.15f, -0.8f), 0.03f, 0.01f, Hollow, 3);
            AddPart("Sandpiper", root, b.ToMesh("GlassHeronSandpiper"));
            return root;
        }

        static readonly Color StagPelt = new Color(0.26f, 0.24f, 0.40f);
        static readonly Color StagBelly = new Color(0.52f, 0.50f, 0.62f);
        static readonly Color StagAntler = new Color(0.86f, 0.82f, 0.72f);
        static readonly Color Brass = new Color(0.55f, 0.42f, 0.20f);
        static readonly Color LanternPane = new Color(1.00f, 0.90f, 0.62f);

        /// <summary>A-76 row 2: the flame points of the lantern stag's lanterns (StoneColossi glows them each frame).</summary>
        static readonly List<Transform> Lanterns = new List<Transform>();
        static Punchy.Weather.DayNightCycle cycle;

        /// <summary>
        /// A-76 row 2 (#40): a stag about 34 m to its antler tips walking the SparklingForest, its antlers hung with brass
        /// lanterns that glow faintly by day and brightly at night (a small flame and a warm light each, see
        /// <see cref="GlowLanterns"/>). Dusk-violet pelt flecked with pale sparkles, a paler belly and throat, slim legs with
        /// dark hooves, head raised; wide branching antlers of pale bone. Details: moss caught on the beams, a trailing vine
        /// of small blossoms on one antler, a bird's nest with three eggs in a fork of the other, one lantern hanging lower
        /// than the rest on a longer chain. Body (body, legs, neck, head, antlers, lantern cages) is one part, laid out as
        /// the mesa tortoise is (shoulders forward on +z, hips at -z), so ColossusRigs' stag rig can walk it; the flame
        /// points ("Lantern0".."LanternN") are empty children the rig moves onto its head bone.
        /// </summary>
        static Transform BuildLanternStag(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("LanternStag").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.3f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);

            // Four slim legs: upper leg to the knee (the hind ones bent back), cannon down to a dark hoof.
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var forward = k == 1 ? 2f : k == 2 ? -1.5f : 0f;
                var hip = new Vector3(sx * 3.4f, 15f, sz * 9f);
                var knee = new Vector3(sx * 3.3f, 8f, sz * 9f + (sz < 0f ? -2.2f : 0.6f) + forward * 0.5f);
                var hoof = new Vector3(sx * 3.2f, 1.2f, sz * 9f + forward);
                m.Limb(hip, knee, 1.7f, 0.9f, StagPelt, 7);
                m.Limb(knee, hoof, 0.8f, 0.6f, StagPelt, 7);
                m.Ellipsoid(hoof + new Vector3(0f, -0.5f, 0.3f), new Vector3(0.8f, 0.7f, 1f), Quaternion.identity, Hollow, 6, 3);
            }
            // Barrel, chest and haunches, belly paler; a short tail with a pale underside.
            m.Ellipsoid(new Vector3(0f, 16.5f, 0f), new Vector3(4.6f, 4.6f, 11f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 15.4f, 0.5f), new Vector3(3.8f, 3.2f, 9f), Quaternion.identity, StagBelly);
            m.Ellipsoid(new Vector3(0f, 17.2f, 8f), new Vector3(4.4f, 5f, 4.6f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 17.5f, -8.5f), new Vector3(4.6f, 4.8f, 4.4f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 18.6f, -12.8f), new Vector3(1f, 1.6f, 0.8f), Quaternion.Euler(-30f, 0f, 0f), StagBelly, 6, 4);
            // The neck up and forward to the head, with a pale throat patch.
            m.Limb(new Vector3(0f, 18.5f, 9.5f), new Vector3(0f, 24.5f, 13.5f), 3.2f, 2f, StagPelt, 9);
            m.Ellipsoid(new Vector3(0f, 20.8f, 12.6f), new Vector3(1.8f, 2.6f, 1.4f), Quaternion.Euler(-30f, 0f, 0f), StagBelly, 7, 4);
            var head = new Vector3(0f, 25.5f, 14.5f);
            var face = Quaternion.Euler(32f, 0f, 0f);
            m.Ellipsoid(head, new Vector3(1.8f, 1.9f, 2.4f), face, StagPelt);
            m.Limb(head + face * new Vector3(0f, -0.2f, 1.2f), head + face * new Vector3(0f, -0.4f, 4.6f), 1.3f, 0.8f, StagPelt, 8);   // muzzle
            m.Ellipsoid(head + face * new Vector3(0f, -0.3f, 4.7f), new Vector3(0.75f, 0.6f, 0.5f), face, Hollow, 6, 3);           // nose
            for (int s = -1; s <= 1; s += 2)
            {
                m.Ellipsoid(head + face * new Vector3(s * 1.5f, 0.5f, 1f), new Vector3(0.35f, 0.4f, 0.4f), face, Hollow, 5, 3);   // eyes
                m.Ellipsoid(head + new Vector3(s * 2.4f, 1.2f, -0.8f), new Vector3(1.4f, 0.5f, 0.8f), Quaternion.Euler(0f, s * 20f, s * -25f), StagPelt, 6, 3);   // ears
            }
            // Sparkle flecks on the pelt: tiny pale facets over the back and flanks.
            for (int k = 0; k < 70; k++)
            {
                var a = rng.NextFloat(-1.3f, 1.3f);
                var z = rng.NextFloat(-11f, 10f);
                var p = new Vector3(Mathf.Sin(a) * 4.7f, 16.5f + Mathf.Cos(a) * 4.7f, z);
                m.Ellipsoid(p, new Vector3(0.18f, 0.12f, 0.18f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Color.Lerp(LanternPane, Color.white, 0.6f), 4, 2);
            }

            // Antlers: a beam each side sweeping up, out and back, three tines and a split crown; lanterns hang under
            // the tine tips on short chains (the brow tines carry none, one hangs lower than the rest).
            var tineTips = new List<Vector3>();
            for (int s = -1; s <= 1; s += 2)
            {
                var b0 = head + new Vector3(s * 1.1f, 1.7f, -0.6f);
                var b1 = b0 + new Vector3(s * 3.2f, 2.8f, -1.2f);
                var b2 = b1 + new Vector3(s * 2.6f, 3.6f, -1.6f);
                var b3 = b2 + new Vector3(s * 1.4f, 3.2f, 0.4f);
                var b4 = b2 + new Vector3(s * 3.2f, 1.4f, -1.4f);
                m.Limb(b0, b1, 0.7f, 0.55f, StagAntler, 6);
                m.Limb(b1, b2, 0.55f, 0.42f, StagAntler, 6);
                m.Limb(b2, b3, 0.42f, 0.18f, StagAntler, 6);
                m.Limb(b2, b4, 0.32f, 0.12f, StagAntler, 5);
                Vector3[] from = { b0 + (b1 - b0) * 0.4f, b1, b1 + (b2 - b1) * 0.6f };
                Vector3[] reach = { new Vector3(s * 0.6f, 1.8f, 2.6f), new Vector3(s * 2.8f, 1.2f, 1.2f), new Vector3(s * 1.2f, 2.6f, -2.4f) };
                for (int k = 0; k < 3; k++)
                {
                    m.Limb(from[k], from[k] + reach[k], 0.36f, 0.12f, StagAntler, 5);
                    tineTips.Add(from[k] + reach[k]);
                }
                tineTips.Add(b3);
                tineTips.Add(b4);
            }
            var lanternAt = new List<Vector3>();
            var low = 1 + rng.NextInt(4);
            for (int k = 0; k < tineTips.Count; k++)
            {
                if (k % 5 == 0)
                    continue;
                var hook = tineTips[k];
                var chain = k == low ? 3.4f : rng.NextFloat(0.9f, 1.6f);
                var lamp = hook + Vector3.down * (chain + 0.7f);
                m.Limb(hook, lamp + Vector3.up * 0.7f, 0.06f, 0.06f, Dark, 3);                                              // chain
                m.Ellipsoid(lamp + Vector3.up * 0.7f, new Vector3(0.55f, 0.22f, 0.55f), Quaternion.identity, Brass, 6, 3);   // cap
                m.Ellipsoid(lamp, new Vector3(0.48f, 0.62f, 0.48f), Quaternion.Euler(0f, 45f, 0f), LanternPane, 4, 3);       // pane
                m.Ellipsoid(lamp + Vector3.down * 0.62f, new Vector3(0.42f, 0.14f, 0.42f), Quaternion.identity, Brass, 6, 2); // base
                lanternAt.Add(lamp);
            }
            // Moss on the beams, a vine of small blossoms trailing from one antler, and a nest in a fork of the other.
            for (int k = 0; k < 10; k++)
            {
                var q = Vector3.Lerp(head + Vector3.up * 2f, tineTips[rng.NextInt(tineTips.Count)], rng.NextFloat(0.3f, 0.7f));
                m.Ellipsoid(q, new Vector3(0.5f, 0.3f, 0.5f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Moss, 5, 3);
            }
            var vineSide = rng.NextFloat() < 0.5f ? -1f : 1f;
            var v0 = head + new Vector3(vineSide * 4.3f, 4.5f, -1.8f);
            Tentacle(m, v0, v0 + new Vector3(vineSide * 1.5f, -2.5f, 0.4f), v0 + new Vector3(vineSide * 0.6f, -4.5f, 1.2f), v0 + new Vector3(vineSide * 1.8f, -6.2f, 0.6f), 0.12f, 0.06f, Moss, Moss, 8);
            for (int k = 0; k < 6; k++)
            {
                var t = (k + 0.5f) / 6f;
                var p = v0 + new Vector3(vineSide * 1.5f * t, -6f * t, 0.8f * Mathf.Sin(t * 3f));
                m.Ellipsoid(p + new Vector3(0.2f, 0f, 0.15f), new Vector3(0.22f, 0.18f, 0.22f), Quaternion.identity, k % 2 == 0 ? new Color(0.96f, 0.72f, 0.86f) : new Color(0.98f, 0.94f, 0.80f), 5, 3);
            }
            var nest = head + new Vector3(-vineSide * 4.3f, 4.5f, -1.8f);
            m.Ellipsoid(nest, new Vector3(0.9f, 0.45f, 0.9f), Quaternion.identity, new Color(0.45f, 0.36f, 0.24f), 7, 3);
            m.Ellipsoid(nest + Vector3.up * 0.32f, new Vector3(0.6f, 0.15f, 0.6f), Quaternion.identity, new Color(0.30f, 0.24f, 0.16f), 6, 2);
            for (int k = 0; k < 3; k++)
                m.Ellipsoid(nest + new Vector3((k - 1) * 0.25f, 0.42f, (k % 2) * 0.2f), new Vector3(0.13f, 0.11f, 0.15f), Quaternion.identity, new Color(0.62f, 0.80f, 0.86f), 5, 3);
            AddPart("Body", root, m.ToMesh("LanternStag"));

            // The flame points, as children so they follow the root (and the rig's head once it is awake).
            for (int k = 0; k < lanternAt.Count; k++)
            {
                var point = new GameObject("Lantern" + k).transform;
                point.SetParent(root, false);
                point.localPosition = lanternAt[k] + Vector3.down * 0.25f;
                Lanterns.Add(point);
            }
            return root;
        }

        static readonly Color SnailFlesh = new Color(0.36f, 0.31f, 0.29f);
        static readonly Color SnailSole = new Color(0.22f, 0.18f, 0.17f);
        static readonly Color Cinder = new Color(0.16f, 0.14f, 0.13f);

        /// <summary>
        /// A-78 (#40): the EmberForest colossus, a cinder snail about 34 m tall. Its shell is a coil of cooled lava with
        /// ember seams glowing between the whorls and a smoking vent on top; it glides on a long ash-grey foot (no legs, so
        /// <see cref="ColossusWalker"/> moves it without a rig), with ember eyes on tall stalks. Riders and wear: a charred
        /// tree rooted on the shell, a baby snail clinging to the whorl, an old rope ladder, a crow on the vent and fire
        /// mushrooms under the shell's lip. Its glow points reuse the stag's lantern flames.
        /// </summary>
        static Transform BuildCinderSnail(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("CinderSnail").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new List<Vector3>();

            // The foot: a long low sole, a paler skirt rippling along each side, and the tail tapering out behind.
            m.Ellipsoid(new Vector3(0f, 2.6f, 1f), new Vector3(7.5f, 3f, 21f), Quaternion.identity, SnailFlesh);
            m.Ellipsoid(new Vector3(0f, 0.9f, 1f), new Vector3(8.4f, 1.1f, 21.5f), Quaternion.identity, SnailSole, 12, 4);
            for (int k = 0; k < 14; k++)
            {
                var z = -17f + k * 2.7f;
                for (int s = -1; s <= 1; s += 2)
                    m.Ellipsoid(new Vector3(s * (7.6f - Mathf.Abs(z) * 0.08f), 1.6f, z), new Vector3(1.2f, 1f, 1.8f), Quaternion.identity, Color.Lerp(SnailFlesh, Ash, 0.4f), 6, 3);
                // Ember glints in the skirt, as if it had just slid over hot coals.
                if (k % 3 == 1)
                    m.Ellipsoid(new Vector3((k % 2 == 0 ? -1f : 1f) * (7.9f - Mathf.Abs(z) * 0.08f), 0.9f, z), new Vector3(0.5f, 0.3f, 0.7f), Quaternion.identity, Ember, 5, 2);
            }
            m.Limb(new Vector3(0f, 2.4f, -19f), new Vector3(0f, 1.2f, -27f), 3f, 0.6f, SnailFlesh, 9);

            // The head and neck rise at the front: two tall eye stalks with ember eyes, two short feelers below.
            var neck = new Vector3(0f, 4f, 18f);
            var head = new Vector3(0f, 9.5f, 23.5f);
            m.Limb(neck, head, 5.2f, 3.4f, SnailFlesh, 10);
            m.Ellipsoid(head + new Vector3(0f, 0.4f, 1.2f), new Vector3(3.6f, 3f, 3.4f), Quaternion.identity, SnailFlesh);
            m.Ellipsoid(head + new Vector3(0f, -1.6f, 4f), new Vector3(2f, 0.6f, 0.6f), Quaternion.identity, Hollow, 6, 3);   // mouth
            for (int s = -1; s <= 1; s += 2)
            {
                var stalk0 = head + new Vector3(s * 1.6f, 2.4f, 1f);
                var eye = head + new Vector3(s * (3.4f + rng.NextFloat(0f, 0.8f)), 9.5f + rng.NextFloat(-0.6f, 0.6f), 4f);
                Tentacle(m, stalk0, stalk0 + new Vector3(s * 0.4f, 3f, 0.4f), eye + new Vector3(0f, -3f, -0.6f), eye, 0.9f, 0.55f, SnailFlesh, SnailFlesh, 10);
                m.Ellipsoid(eye, new Vector3(1.1f, 1.1f, 1.1f), Quaternion.identity, Cinder, 8, 5);
                m.Ellipsoid(eye + new Vector3(0f, 0f, 0.8f), new Vector3(0.6f, 0.6f, 0.4f), Quaternion.identity, Ember, 6, 3);
                glow.Add(eye + new Vector3(0f, 0f, 1.1f));
                var feeler = head + new Vector3(s * 2.2f, -1f, 3f);
                m.Limb(feeler, feeler + new Vector3(s * 2.4f, -1.4f, 3.2f), 0.6f, 0.2f, SnailFlesh, 6);
            }

            // The shell: a coil of cooled lava wound round a sideways axis over the back, each whorl smaller than the
            // last, with an ember seam between whorls and pitted lumps of crust.
            // A raised mantle on the back so the shell sits on the body rather than floating over it.
            m.Ellipsoid(new Vector3(0f, 5.5f, -3f), new Vector3(6.5f, 3.5f, 13f), Quaternion.identity, Color.Lerp(SnailFlesh, Ash, 0.25f));
            var axis = new Vector3(0f, 13.5f, -3f);
            const float turns = 2.6f;
            const int steps = 70;
            Vector3 Coil(float t, float lift)
            {
                var theta = t * turns * Mathf.PI * 2f;
                var r = 13.5f * Mathf.Exp(-0.28f * theta) + lift;
                return axis + new Vector3(Mathf.Lerp(0f, 2.5f, t), Mathf.Sin(theta) * r, -Mathf.Cos(theta) * r);
            }
            for (int k = 0; k < steps; k++)
            {
                var t = k / (float)steps;
                var theta = t * turns * Mathf.PI * 2f;
                var tube = 7.5f * Mathf.Exp(-0.28f * theta) + 0.6f;
                var p = Coil(t, 0f);
                var turn = Quaternion.Euler(-theta * Mathf.Rad2Deg, 0f, 0f);
                m.Ellipsoid(p, new Vector3(tube * 1.15f, tube, tube), turn, Color.Lerp(Cinder, Soot, rng.NextFloat()), 10, 6);
                if (k % 2 == 0)
                {
                    var seam = Coil(t, tube * 0.92f);
                    for (int s = -1; s <= 1; s += 2)
                        m.Ellipsoid(seam + new Vector3(s * tube * 0.55f, 0f, 0f), new Vector3(0.45f, 0.5f, tube * 0.32f), turn, Ember, 5, 2);
                }
                if (k % 9 == 4)
                    glow.Add(Coil(t, tube * 0.95f) + new Vector3(tube * 0.6f, 0f, 0f));
                if (rng.NextFloat() < 0.5f)
                {
                    var lump = p + Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f) * Vector3.up * tube * 0.95f;
                    m.Ellipsoid(lump, Vector3.one * rng.NextFloat(0.6f, 1.4f), Quaternion.identity, Color.Lerp(Ash, Cinder, 0.5f), 5, 3);
                }
            }
            // The shell's lip flares over the foot, with fire mushrooms tucked under it.
            var lip = Coil(0f, 0f);
            m.Ellipsoid(lip + new Vector3(0f, -3f, 1f), new Vector3(9f, 3f, 4f), Quaternion.Euler(-20f, 0f, 0f), Cinder, 10, 5);
            for (int k = 0; k < 7; k++)
            {
                var c = lip + new Vector3(rng.NextFloat(-7f, 7f), -4.5f + rng.NextFloat(-0.5f, 0.5f), rng.NextFloat(2f, 4.5f));
                var cap = rng.NextFloat(0.5f, 1.1f);
                m.Limb(c, c + Vector3.up * cap * 1.2f, cap * 0.25f, cap * 0.2f, new Color(0.86f, 0.80f, 0.70f), 5);
                m.Ellipsoid(c + Vector3.up * cap * 1.3f, new Vector3(cap, cap * 0.45f, cap), Quaternion.identity, k % 3 == 0 ? new Color(0.95f, 0.70f, 0.18f) : new Color(0.85f, 0.24f, 0.10f), 6, 3);
            }

            // A smoking vent on the crown of the shell, a crow on its rim.
            var crown = axis + new Vector3(0f, 14f, 1f);
            var vent = crown + new Vector3(0.8f, 4.5f, -0.6f);
            m.Limb(crown, vent, 2.2f, 1.4f, Soot, 9);
            m.Ellipsoid(vent + Vector3.up * 0.1f, new Vector3(1.6f, 0.5f, 1.6f), Quaternion.identity, Hollow, 8, 3);
            m.Ellipsoid(vent, new Vector3(1.1f, 0.3f, 1.1f), Quaternion.identity, Ember, 7, 2);
            glow.Add(vent + Vector3.up * 0.4f);
            var crow = vent + new Vector3(1.4f, 0.5f, 0f);
            m.Ellipsoid(crow, new Vector3(0.35f, 0.4f, 0.6f), Quaternion.Euler(-20f, 0f, 0f), Hollow, 6, 4);
            m.Ellipsoid(crow + new Vector3(0f, 0.45f, 0.45f), new Vector3(0.24f, 0.24f, 0.26f), Quaternion.identity, Hollow, 6, 3);
            m.Limb(crow + new Vector3(0f, 0.45f, 0.65f), crow + new Vector3(0f, 0.38f, 1.05f), 0.08f, 0.02f, Dark, 4);   // beak

            // A charred tree has rooted on the shoulder of the shell: a black trunk, bare forks, two last ember leaves.
            var treeAt = Coil(0.18f, 0f) + new Vector3(4.5f, 4.5f, 0f);
            var top = treeAt + new Vector3(1.5f, 7f, -0.5f);
            m.Limb(treeAt, top, 0.9f, 0.4f, Hollow, 7);
            for (int k = 0; k < 4; k++)
            {
                var from = Vector3.Lerp(treeAt, top, 0.45f + k * 0.15f);
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var to = from + new Vector3(Mathf.Cos(a) * 2.6f, rng.NextFloat(1.2f, 2.4f), Mathf.Sin(a) * 2.6f);
                m.Limb(from, to, 0.3f, 0.08f, Hollow, 5);
                if (k < 2)
                    m.Ellipsoid(to, new Vector3(0.3f, 0.12f, 0.22f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 30f), Ember, 4, 2);
            }
            for (int k = 0; k < 4; k++)
            {
                var a = k * Mathf.PI * 0.5f + 0.4f;
                m.Limb(treeAt, treeAt + new Vector3(Mathf.Cos(a) * 2.8f, -1.6f, Mathf.Sin(a) * 2.8f), 0.4f, 0.1f, Hollow, 5);   // roots gripping the crust
            }

            // A baby snail clinging to the far side of the whorl, its own shell a tiny ember-seamed coil.
            var baby = Coil(0.42f, 0f) + new Vector3(-6.5f, 0f, 0f);
            m.Ellipsoid(baby, new Vector3(0.9f, 0.6f, 2.4f), Quaternion.Euler(70f, 0f, 0f), SnailFlesh, 7, 4);
            m.Ellipsoid(baby + new Vector3(-1f, 0f, 0f), new Vector3(1.1f, 1.4f, 1.4f), Quaternion.identity, Cinder, 7, 5);
            m.Ellipsoid(baby + new Vector3(-1.9f, 0.1f, 0f), new Vector3(0.2f, 0.8f, 0.8f), Quaternion.identity, Ember, 6, 3);

            // An old rope ladder hangs down the flank from a peg in the crust: two ropes and slats, one slat missing.
            var peg = Coil(0.05f, 0f) + new Vector3(7.5f, 2f, 0f);
            for (int s = -1; s <= 1; s += 2)
                m.Limb(peg + new Vector3(0f, 0f, s * 0.7f), peg + new Vector3(0.6f, -12f, s * 0.7f), 0.08f, 0.08f, new Color(0.52f, 0.42f, 0.28f), 3);
            for (int k = 0; k < 8; k++)
            {
                if (k == 5)
                    continue;
                var y = -1.4f - k * 1.4f;
                m.Ellipsoid(peg + new Vector3(0.6f * -y / 12f, y, 0f), new Vector3(0.12f, 0.1f, 0.85f), Quaternion.identity, new Color(0.40f, 0.30f, 0.20f), 4, 2);
            }
            AddPart("Body", root, m.ToMesh("CinderSnail"));

            // Smoke from the vent, and embers drifting off the shell seams.
            Puffs(root, "Smoke", vent + Vector3.up * 1f, new Vector3(1.2f, 0.5f, 1.2f), new Color(0.20f, 0.18f, 0.17f, 0.40f), new Color(0.36f, 0.33f, 0.31f, 0.35f), 2.5f, 6f, 3f, 2.2f, 9f);
            Puffs(root, "Embers", axis + new Vector3(0f, 4f, 0f), new Vector3(10f, 14f, 16f), new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.35f, 0.08f, 0.8f), 0.15f, 0.35f, 4f, 1.2f, 4f);

            // The glow points, as children so they follow the root as it glides.
            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Ember" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                Lanterns.Add(point);
            }
            return root;
        }

        static readonly Color MireBark = new Color(0.27f, 0.23f, 0.18f);
        static readonly Color MireDark = new Color(0.15f, 0.13f, 0.11f);
        static readonly Color BeardMoss = new Color(0.56f, 0.61f, 0.52f);
        static readonly Color FogEye = new Color(0.82f, 0.96f, 0.72f);

        /// <summary>
        /// A-79 (#40): the FoggyMarsh colossus, a hunched mire walker that creeps on a skirt of arching mangrove prop roots.
        /// Moss sheets its back and pale beards of hanging moss sway under it; a long mossy skull with two pale lantern eyes
        /// looms out of the fog ahead of it, with dead branches for antlers and a rusty lantern hanging from one. Small
        /// details: a heron standing on its hump, a reed shrine with a candle on its back, shelf fungi, a fishing net snagged
        /// on a root with a fish still in it, mud mounds and rootlets at each root's foot, and mist pouring off the roots.
        /// </summary>
        static Transform BuildMireWalker(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("MireWalker").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new List<Vector3>();

            // The root skirt: prop roots arching out and down from under the body, each ending in a mud mound and rootlets.
            const int roots = 22;
            for (int k = 0; k < roots; k++)
            {
                var a = (k + rng.NextFloat(-0.3f, 0.3f)) / roots * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var reach = rng.NextFloat(14f, 18f);
                var from = d * 5f + Vector3.up * rng.NextFloat(11f, 14f);
                var foot = d * reach + Vector3.up * -0.5f;
                Tentacle(m, from, d * (reach * 0.55f) + Vector3.up * rng.NextFloat(14f, 17f), d * (reach * 0.92f) + Vector3.up * 6f, foot,
                         rng.NextFloat(1.2f, 1.6f), 0.5f, MireBark, MireDark, 12);
                m.Ellipsoid(foot + Vector3.up * 0.2f, new Vector3(1.8f, 0.6f, 1.8f), Quaternion.identity, Color.Lerp(MireDark, Moss, 0.3f), 7, 3);
                for (int r = 0; r < 3; r++)
                {
                    var ra = a + rng.NextFloat(-1.2f, 1.2f);
                    m.Limb(foot + Vector3.up * 0.4f, foot + new Vector3(Mathf.Sin(ra), -0.3f, Mathf.Cos(ra)) * rng.NextFloat(1.5f, 3f), 0.35f, 0.08f, MireDark, 4);
                }
            }

            // The hunched body and hump, sheeted in moss.
            var body = new Vector3(0f, 18f, -2f);
            m.Ellipsoid(body, new Vector3(11f, 8f, 14f), Quaternion.identity, MireBark);
            m.Ellipsoid(body + new Vector3(0f, 6f, -3f), new Vector3(8f, 6f, 9f), Quaternion.Euler(-10f, 0f, 0f), MireBark);
            m.Ellipsoid(body + new Vector3(0f, 3.2f, -1f), new Vector3(10.6f, 5.5f, 13.4f), Quaternion.identity, Moss, 12, 5);
            m.Ellipsoid(body + new Vector3(0f, 8.6f, -3.5f), new Vector3(7.2f, 3.6f, 8.2f), Quaternion.Euler(-10f, 0f, 0f), Color.Lerp(Moss, BeardMoss, 0.3f), 10, 4);

            // Beards of pale moss hanging from its underside and flanks, some long enough to brush the root tops.
            for (int k = 0; k < 34; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = body + new Vector3(Mathf.Sin(a) * 10f, rng.NextFloat(-6f, -1f), Mathf.Cos(a) * 12.5f) * rng.NextFloat(0.75f, 0.98f);
                var len = rng.NextFloat(3f, 8f);
                m.Limb(p, p + new Vector3(rng.NextFloat(-0.6f, 0.6f), -len, rng.NextFloat(-0.6f, 0.6f)), rng.NextFloat(0.35f, 0.6f), 0.08f,
                       Color.Lerp(BeardMoss, Moss, rng.NextFloat(0f, 0.4f)), 4);
            }

            // Shelf fungi stepping up one flank.
            for (int k = 0; k < 6; k++)
                m.Ellipsoid(body + new Vector3(10.4f - k * 0.3f, -3f + k * 1.3f, -6f + k * 1.6f), new Vector3(1.6f, 0.3f, 1.2f), Quaternion.Euler(0f, 0f, -12f),
                            Color.Lerp(new Color(0.78f, 0.66f, 0.46f), MireBark, k * 0.1f), 7, 3);

            // The neck and the long mossy skull, jaw hanging a little open, two pale lantern eyes.
            var head = new Vector3(0f, 22f, 23f);
            m.Limb(body + new Vector3(0f, 2f, 11f), head + new Vector3(0f, -0.5f, -2f), 5f, 3.4f, MireBark, 10);
            m.Ellipsoid(head, new Vector3(4f, 3.5f, 5.5f), Quaternion.identity, MireBark);
            m.Ellipsoid(head + new Vector3(0f, 1.6f, -0.5f), new Vector3(3.6f, 1.6f, 4.8f), Quaternion.identity, Moss, 9, 4);
            m.Ellipsoid(head + new Vector3(0f, -1f, 5f), new Vector3(2.4f, 2f, 4f), Quaternion.identity, MireBark);
            m.Ellipsoid(head + new Vector3(0f, -3.2f, 3.6f), new Vector3(2.6f, 0.9f, 4.4f), Quaternion.Euler(12f, 0f, 0f), MireDark, 8, 4);
            m.Ellipsoid(head + new Vector3(0f, -2.4f, 4f), new Vector3(2f, 0.6f, 3.6f), Quaternion.Euler(6f, 0f, 0f), Hollow, 7, 3);
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = head + new Vector3(s * 2.7f, 1f, 2.6f);
                m.Ellipsoid(eye, new Vector3(0.95f, 0.8f, 0.95f), Quaternion.identity, FogEye, 8, 5);
                glow.Add(eye + new Vector3(s * 0.5f, 0f, 0.4f));
                // Dead branches for antlers, forking twice.
                var b0 = head + new Vector3(s * 2f, 3f, -1.5f);
                var b1 = b0 + new Vector3(s * 3.5f, 6f, -2f);
                m.Limb(b0, b1, 0.7f, 0.4f, MireDark, 6);
                m.Limb(b1, b1 + new Vector3(s * 3f, 3.5f, 1f), 0.4f, 0.12f, MireDark, 5);
                m.Limb(b1, b1 + new Vector3(s * 0.5f, 4.5f, -2.5f), 0.35f, 0.1f, MireDark, 5);
                m.Limb(b0 + (b1 - b0) * 0.5f, b0 + (b1 - b0) * 0.5f + new Vector3(s * -1f, 2.5f, 1.5f), 0.3f, 0.08f, MireDark, 4);
                // Beard moss trailing off each antler.
                m.Limb(b1, b1 + new Vector3(0f, -3.5f, 0f), 0.3f, 0.06f, BeardMoss, 4);
            }
            // A rusty lantern hanging from the right antler on a short chain.
            var hook = head + new Vector3(2f + 3.5f * 0.8f, 3f + 6f * 0.8f, -1.5f - 2f * 0.8f);
            m.Limb(hook, hook + Vector3.down * 2.6f, 0.06f, 0.06f, Rust, 3);
            m.Ellipsoid(hook + Vector3.down * 3.4f, new Vector3(0.5f, 0.75f, 0.5f), Quaternion.identity, Rust, 6, 4);
            glow.Add(hook + Vector3.down * 3.4f);

            // A heron standing on the hump, looking out over the fog.
            var heron = body + new Vector3(2.5f, 14.6f, -4f);
            for (int s = -1; s <= 1; s += 2)
                m.Limb(heron + new Vector3(s * 0.2f, -2f, 0f), heron + new Vector3(s * 0.2f, 0f, 0f), 0.06f, 0.06f, Dark, 3);
            m.Ellipsoid(heron + new Vector3(0f, 0.6f, 0f), new Vector3(0.45f, 0.5f, 0.9f), Quaternion.Euler(-15f, 0f, 0f), new Color(0.82f, 0.84f, 0.86f), 7, 4);
            m.Limb(heron + new Vector3(0f, 0.9f, 0.7f), heron + new Vector3(0f, 2f, 0.9f), 0.12f, 0.1f, new Color(0.82f, 0.84f, 0.86f), 4);
            m.Limb(heron + new Vector3(0f, 2f, 0.9f), heron + new Vector3(0f, 1.9f, 1.8f), 0.08f, 0.02f, new Color(0.85f, 0.70f, 0.30f), 3);

            // A little reed shrine on its back, a candle burning inside.
            var shrine = body + new Vector3(-3.5f, 12.4f, 1f);
            for (int k = 0; k < 4; k++)
                m.Limb(shrine + new Vector3(k % 2 == 0 ? -0.9f : 0.9f, -0.3f, k < 2 ? -0.9f : 0.9f), shrine + new Vector3(k % 2 == 0 ? -0.8f : 0.8f, 1.8f, k < 2 ? -0.8f : 0.8f), 0.1f, 0.1f, MireDark, 3);
            m.Ellipsoid(shrine + Vector3.up * 2.3f, new Vector3(1.5f, 0.8f, 1.5f), Quaternion.identity, new Color(0.62f, 0.56f, 0.34f), 7, 3);
            m.Limb(shrine + Vector3.up * -0.2f, shrine + Vector3.up * 0.4f, 0.12f, 0.12f, new Color(0.92f, 0.90f, 0.80f), 4);
            glow.Add(shrine + Vector3.up * 0.6f);

            // A fishing net snagged on one root, a fish still caught in it.
            var snag = new Vector3(Mathf.Sin(1.1f), 0f, Mathf.Cos(1.1f)) * 12f + Vector3.up * 9f;
            m.Ellipsoid(snag, new Vector3(2.4f, 2.8f, 0.3f), Quaternion.Euler(0f, 1.1f * Mathf.Rad2Deg + 90f, 8f), new Color(0.40f, 0.38f, 0.30f), 8, 4);
            m.Ellipsoid(snag + new Vector3(0f, -2f, 0f), new Vector3(0.35f, 0.9f, 0.25f), Quaternion.Euler(0f, 0f, 20f), new Color(0.55f, 0.62f, 0.60f), 6, 3);
            AddPart("Body", root, m.ToMesh("MireWalker"));

            // Mist pouring off the roots, low and slow, and a few fireflies in the moss beards.
            Puffs(root, "Mist", new Vector3(0f, 1.5f, 0f), new Vector3(34f, 2f, 34f), new Color(0.78f, 0.80f, 0.80f, 0.30f), new Color(0.86f, 0.88f, 0.88f, 0.36f), 6f, 14f, 6f, 0.3f, 14f);
            Puffs(root, "Fireflies", body + new Vector3(0f, -7f, 0f), new Vector3(18f, 6f, 22f), new Color(0.80f, 1f, 0.45f, 0.9f), new Color(0.95f, 1f, 0.60f, 0.8f), 0.12f, 0.25f, 3f, 0.2f, 6f);

            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Glow" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                Lanterns.Add(point);
            }
            return root;
        }


        /// <summary>Lanterns glow within this far of the camera (m).</summary>
        const float LanternRange = 450f;

        /// <summary>
        /// A-76 row 2: each frame the stag's lanterns burn as small standard-fire flames and throw a warm light round them
        /// (CaveSystem.MovingLights lights the ground and trees too), faint by day and brightening into the night
        /// (DayNightCycle.Night). Cosmetic and local.
        /// </summary>
        void GlowLanterns()
        {
            if (Lanterns.Count == 0 || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var night = cycle != null ? cycle.Night : 0f;
            var eye = cam.transform.position;
            for (int k = 0; k < Lanterns.Count; k++)
            {
                var point = Lanterns[k];
                if (point == null || !point.gameObject.activeInHierarchy || (point.position - eye).sqrMagnitude > LanternRange * LanternRange)
                    continue;
                var flicker = 0.9f + 0.1f * Mathf.Sin(Time.time * 7f + k * 1.7f);
                Punchy.Ambience.FireSystem.AddFlame(point.position, 0.3f + 0.25f * night, 0x57A60 + k, Punchy.Ambience.FireSystem.Wood);
                var light = Punchy.Ambience.FireSystem.Light(Punchy.Ambience.FireSystem.Wood, flicker * (0.35f + 1.4f * night));
                light.w *= 0.6f + 0.8f * night;
                Punchy.Terrain.CaveSystem.MovingLights.Add((0x57A60 + k, point.position, light));
            }
        }

        /// <summary>Slow, soft puffs rising from a box round <paramref name="local"/> (ash, dust, spores), in the ambience puff material.</summary>
        static void Puffs(Transform parent, string name, Vector3 local, Vector3 box, Color a, Color b, float sizeMin, float sizeMax, float rate, float rise, float life)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(rise, rise * 1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.5f;
            noise.frequency = 0.15f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var ambience = FindAnyObjectByType<Punchy.Ambience.AmbienceSystem>();
            if (ambience != null && ambience.PuffMaterial != null)
                renderer.sharedMaterial = ambience.PuffMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Simulate(life, true, true);
            ps.Play();
        }

        /// <summary>Snow or moss on every face turned up past <paramref name="from"/> (full at <paramref name="full"/>).</summary>
        static void Weather(Mesh mesh, Color cover, float from, float full)
        {
            var normals = mesh.normals;
            var colors = mesh.colors;
            for (int i = 0; i < colors.Length; i++)
            {
                var t = Mathf.InverseLerp(from, full, normals[i].y);
                if (t > 0f)
                    colors[i] = Color.Lerp(colors[i], cover * (0.94f + 0.06f * (i % 3)), Mathf.Min(1f, t * 1.6f));
            }
            mesh.SetColors(colors);
        }

        static void AddPart(string name, Transform parent, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Colossus");
        }
    }
}
