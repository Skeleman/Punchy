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
        public enum Kind { KneelingTitan, HalfBuriedHead }

        /// <summary>Where each kind stands (NaN until placed), for probes and shot lists.</summary>
        public static readonly float3[] Positions = { new float3(float.NaN), new float3(float.NaN) };

        const float Step = 150f;
        static readonly string[][] Biomes = { new[] { "Tundra", "Glacier", "Mountains" }, new[] { "Grassland", "Temperate" } };
        static readonly uint[] Salts = { 0x71A4u, 0x4EADu };

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

        readonly Transform[] roots = new Transform[2];
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
            var palette = em.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());

            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            var grid = new NativeArray<int>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = Step, Size = n, Biome = grid }.Schedule(n * n, 64).Complete();
            var biomes = grid.ToArray();
            grid.Dispose();
            placed = true;
            for (int kind = 0; kind < 2; kind++)
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
                var keep = FloraPlacement.KeepOut.Data;
                keep[kind] = new float4(spot.x, spot.y, kind == 0 ? 34f : 26f, 1f);
                FloraPlacement.KeepOut.Data = keep;
                FloraSystem.Replant = true;
                roots[kind] = kind == 0 ? BuildTitan(Positions[kind], yaw) : BuildHead(Positions[kind], yaw);
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
