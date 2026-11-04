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
        /// <summary>W-78 added a moss-backed giant tortoise (FungalForest) and a crystal-spined serpent (ElectricWastelands).</summary>
        public enum Kind { KneelingTitan, HalfBuriedHead, GiantTortoise, CrystalSerpent, DrownedKnight }
        const int Kinds = 5;

        /// <summary>Where each kind stands (NaN until placed), for probes and shot lists.</summary>
        public static readonly float3[] Positions = { new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN), new float3(float.NaN) };

        const float Step = 150f;
        static readonly string[][] Biomes =
        {
            new[] { "Tundra", "Glacier", "Mountains" }, new[] { "Grassland", "Temperate" }, new[] { "FungalForest" }, new[] { "ElectricWastelands" },
            new[] { "FoggyMarsh", "PoisonSwamp" },
        };
        // Each kind draws from its own salt on the world seed, so adding one moves no other colossus or landmark.
        static readonly uint[] Salts = { 0x71A4u, 0x4EADu, 0x7027u, 0x5E2Bu, 0xD20Au };
        static readonly float[] KeepRadius = { 34f, 42f, 38f, 44f, 40f };
        /// <summary>Height and half-width each one casts with its far shadow (#84); the last entry is the Dunes colossus.</summary>
        static readonly float2[] Casts = { new float2(46f, 14f), new float2(22f, 12f), new float2(30f, 20f), new float2(26f, 9f), new float2(30f, 12f), new float2(58f, 20f) };

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
            DrawFarShadows();
        }

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
                shadows[k] ??= new FarShadow(k < Kinds ? $"{(Kind)k} far shadow" : "DunesColossus far shadow");
                shadows[k].Draw(source, at, Casts[k].x, Casts[k].y);
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
            for (int kind = 0; kind < Kinds; kind++)
            {
                var wanted = new HashSet<int>();
                for (int b = 0; b < palette.Biomes.Length; b++)
                    if (palette.Biomes[b] != null)
                        foreach (var name in Biomes[kind])
                            if (palette.Biomes[b].name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase))
                                wanted.Add(b);
                if (!Pick(source, biomes, n, half, wanted, Salts[kind], out var spot, out var yaw, out var cells))
                {
                    Debug.Log($"StoneColossi: no room for a {(Kind)kind} in this world, none placed");
                    continue;
                }
                var ground = source.SampleHeight(spot);
                Positions[kind] = new float3(spot.x, ground, spot.y);
                // Keep flora out of a circle round it and re-place the streamed tiles, so no tree grows through it (#40).
                // The head's circle is wide enough (W-59d) to leave a clear sight line from about 40 m away.
                // Kinds past the fourth go in a second static (W-85): a live editor can't resize one it already made.
                var circle = new float4(spot.x, spot.y, KeepRadius[kind], 1f);
                if (kind < 4)
                {
                    var keep = FloraPlacement.KeepOut.Data;
                    keep[kind] = circle;
                    FloraPlacement.KeepOut.Data = keep;
                }
                else
                {
                    var keep = FloraPlacement.KeepOutMore.Data;
                    keep[kind - 4] = circle;
                    FloraPlacement.KeepOutMore.Data = keep;
                }
                FloraSystem.Replant = true;
                roots[kind] = (Kind)kind switch
                {
                    Kind.KneelingTitan => BuildTitan(Positions[kind], yaw),
                    Kind.HalfBuriedHead => BuildHead(Positions[kind], yaw),
                    Kind.GiantTortoise => BuildTortoise(Positions[kind], yaw),
                    Kind.CrystalSerpent => BuildSerpent(Positions[kind], yaw),
                    _ => BuildKnight(Positions[kind], yaw),
                };
                Debug.Log($"StoneColossi: {(Kind)kind} placed at ({spot.x:F0}, {ground:F1}, {spot.y:F0}), facing {yaw:F0} degrees, region {cells} cells");
            }
        }

        /// <summary>The biggest 4-connected region of <paramref name="wanted"/> cells, then a seeded pick among its most inland cells clear of settlements.</summary>
        static bool Pick(in TerrainSource source, int[] grid, int n, float half, HashSet<int> wanted, uint salt, out float2 spot, out float yaw, out int cells)
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
            var pick = candidates[rng.NextInt(candidates.Count)];
            spot = new float2(-half + pick % n * Step, -half + pick / n * Step) + rng.NextFloat2(-Step * 0.3f, Step * 0.3f);
            yaw = rng.NextFloat(0f, 360f);
            return true;
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
