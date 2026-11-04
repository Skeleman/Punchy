using System.Collections.Generic;
using Punchy.Ambience;
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
    /// One half-buried humanoid giant in the biggest Dunes area (#40, first pass): torso, head and one arm out of the sand,
    /// about 67 m tall, built in code from flat-shaded ellipsoids and limbs (no imported assets). It idles slowly: the head
    /// turns, and the arm drags across the sand, leaving a furrow, in a sandstorm that swirls round it. Placed from the terrain
    /// and the world seed only (a seeded pick among the most inland cells of the biggest Dunes region), so it needs no
    /// landmark data. "-nocolossus" (DebugControls, per shot line) hides it.
    /// </summary>
    public class DunesColossus : MonoBehaviour
    {
        public static bool Hidden;
        /// <summary>"-framelog": every 5 s a line with the mean frame time and, where the player reports it, the GPU time.</summary>
        public static bool FrameLog;
        public static bool Placed { get; private set; }
        public static float3 Position { get; private set; }

        const float Step = 150f;
        // Local frame: +Z is the way it faces; the origin is the point on the sand under its middle.
        static readonly Vector3 ShoulderPivot = new Vector3(21f, 35f, 3f);
        static readonly Vector3 HandLocal = new Vector3(44f, -34.5f, 24f);
        const float SweepDegrees = 22f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            // Going solo after losing the host reloads the scene, which destroys this object: build it again then.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            if (FindAnyObjectByType<DunesColossus>() != null)
                return;
            Placed = false;
            var go = new GameObject("DunesColossus");
            go.AddComponent<DunesColossus>();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        TerrainSource source;
        // The world the cached source came from. Its blobs are freed with that world (joining, or the host leaving),
        // so the source is fetched again whenever the default world changes.
        Unity.Entities.World sourceWorld;
        Transform root, head, arm;
        ParticleSystem storm;
        Mesh furrowMesh;
        GameObject furrow;
        float nextTry, clock, handPuff;
        readonly bool[] swept = new bool[61];   // 1-degree buckets of the arm's sweep, -30..30
        int sweptMin = 99, sweptMax = -99, furrowMin = 99, furrowMax = -99;
        // Frame log.
        float logStart, logTime;
        int logFrames, logGpuFrames;
        double logGpu, logCpu;
        bool logHidden;
        readonly FrameTiming[] timings = new FrameTiming[1];

        void Update()
        {
            if (!Placed)
            {
                if (Time.realtimeSinceStartup >= nextTry)
                {
                    nextTry = Time.realtimeSinceStartup + 1f;
                    TryPlace();
                }
                return;
            }
            if (!CurrentSource())
                return;
            var shown = !Hidden;
            if (root.gameObject.activeSelf != shown)
            {
                root.gameObject.SetActive(shown);
                furrow.SetActive(shown);
                var emission = storm.emission;
                emission.enabled = shown;
                if (!shown)
                    storm.Clear();
            }
            if (shown)
                Idle();
            LogFrames();
        }

        /// <summary>True when <see cref="source"/> belongs to the live default world (fetched again after a world swap).</summary>
        bool CurrentSource()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && world == sourceWorld)
                return true;
            sourceWorld = null;
            if (world == null || !world.IsCreated)
                return false;
            using var terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!terrainQuery.TryGetSingleton(out source))
                return false;
            sourceWorld = world;
            return true;
        }

        // ---------------------------------------------------------------- placing

        void TryPlace()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return;
            sourceWorld = world;
            var palette = em.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var dunes = -1;
            for (int b = 0; b < palette.Biomes.Length; b++)
                if (palette.Biomes[b] != null && palette.Biomes[b].name.StartsWith("Dunes", System.StringComparison.OrdinalIgnoreCase))
                    dunes = b;
            if (dunes < 0)
            {
                Debug.Log("DunesColossus: no Dunes biome in this world, none placed");
                nextTry = float.MaxValue;
                return;
            }

            var half = source.World.WorldSize * 0.5f;
            var n = (int)(half * 2f / Step) + 1;
            var grid = new NativeArray<int>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = Step, Size = n, Biome = grid }.Schedule(n * n, 64).Complete();

            // The biggest 4-connected region of Dunes cells.
            var region = new int[n * n];
            var sizes = new List<int> { 0 };
            var queue = new Queue<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] != dunes || region[i] != 0)
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
                        if (grid[j] == dunes && region[j] == 0) { region[j] = id; queue.Enqueue(j); }
                    }
                }
            }
            grid.Dispose();
            var biggest = 0;
            for (int id = 1; id < sizes.Count; id++)
                if (sizes[id] > sizes[biggest]) biggest = id;
            if (biggest == 0)
            {
                Debug.Log("DunesColossus: no dry Dunes cells found, none placed");
                nextTry = float.MaxValue;
                return;
            }

            // Most inland cells of that region: rings of same-region cells round each.
            int At(int x, int z) => x < 0 || z < 0 || x >= n || z >= n ? 0 : region[z * n + x];
            var scores = new int[n * n];
            var best = -1;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                if (At(x, z) != biggest)
                    continue;
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
            var candidates = new List<int>();
            for (int i = 0; i < scores.Length; i++)
                if (At(i % n, i / n) == biggest && scores[i] >= best - 1)
                    candidates.Add(i);
            var rng = Unity.Mathematics.Random.CreateFromIndex((uint)source.World.Seed ^ 0x6C0D055u);
            var pick = candidates[rng.NextInt(candidates.Count)];
            var spot = new float2(-half + pick % n * Step, -half + pick / n * Step) + rng.NextFloat2(-Step * 0.4f, Step * 0.4f);
            var yaw = rng.NextFloat(0f, 360f);
            Debug.Log($"DunesColossus: biggest Dunes region {sizes[biggest]} cells of {Step:F0} m, inland score {best}, {candidates.Count} candidates");

            Build(spot, yaw);
            Placed = true;
            Debug.Log($"DunesColossus: placed at ({Position.x:F0}, {Position.y:F1}, {Position.z:F0}), facing {yaw:F0} degrees, about 67 m tall");
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

        static readonly Color Sandstone = new Color(0.63f, 0.47f, 0.31f);
        static readonly Color Shade = new Color(0.52f, 0.37f, 0.24f);
        static readonly Color Hollow = new Color(0.16f, 0.11f, 0.08f);

        void Build(float2 spot, float yaw)
        {
            var ground = source.SampleHeight(spot);
            Position = new float3(spot.x, ground, spot.y);
            var mat = Resources.Load<Material>("Colossus");
            root = new GameObject("Colossus").transform;
            root.SetPositionAndRotation(new Vector3(spot.x, ground, spot.y), Quaternion.Euler(0f, yaw, 0f));

            // Body: leaning torso, shoulders, neck, the other arm lying along its side, buried below the sand.
            var lean = Quaternion.Euler(18f, 0f, 0f);
            var body = new MeshBuilder();
            body.Ellipsoid(lean * new Vector3(0f, 20f, 0f), new Vector3(17f, 27f, 12.5f), lean, Sandstone);
            body.Ellipsoid(lean * new Vector3(0f, 36f, 2f), new Vector3(20f, 7f, 10f), lean, Shade);
            body.Ellipsoid(lean * new Vector3(-20f, 36f, 2f), new Vector3(8f, 7f, 7.5f), lean, Sandstone);
            body.Ellipsoid(lean * new Vector3(20f, 36f, 2f), new Vector3(8f, 7f, 7.5f), lean, Sandstone);
            body.Ellipsoid(lean * new Vector3(0f, 22f, 10f), new Vector3(11f, 14f, 4f), lean, Shade);
            body.Limb(lean * new Vector3(0f, 40f, 2f), new Vector3(0f, 50f, 9f), 5.5f, 4.6f, Sandstone);
            body.Limb(new Vector3(-24f, 35f, 2f), new Vector3(-31f, 18f, -5f), 6.2f, 5.4f, Sandstone);
            body.Limb(new Vector3(-31f, 18f, -5f), new Vector3(-30f, 3f, 14f), 5.4f, 4.6f, Shade);
            body.Ellipsoid(new Vector3(-30f, 1.5f, 19f), new Vector3(5f, 3f, 7f), Quaternion.identity, Sandstone);
            AddPart("Body", root, body.ToMesh("ColossusBody"), mat);

            // Head, on a pivot at the top of the neck so it can turn.
            head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 50f, 9f);
            var h = new MeshBuilder();
            h.Ellipsoid(new Vector3(0f, 7f, 2f), new Vector3(7.5f, 9.5f, 8.5f), Quaternion.identity, Sandstone);
            h.Ellipsoid(new Vector3(0f, -1f, 5f), new Vector3(5f, 3.5f, 5f), Quaternion.identity, Shade);
            h.Ellipsoid(new Vector3(0f, 9.5f, 8.2f), new Vector3(6.6f, 1.8f, 2.6f), Quaternion.identity, Shade);
            h.Ellipsoid(new Vector3(0f, 5f, 10.2f), new Vector3(1.7f, 3.6f, 2.6f), Quaternion.identity, Sandstone);
            h.Ellipsoid(new Vector3(-3.1f, 8f, 9.9f), new Vector3(1.9f, 1.1f, 1.0f), Quaternion.identity, Hollow);
            h.Ellipsoid(new Vector3(3.1f, 8f, 9.9f), new Vector3(1.9f, 1.1f, 1.0f), Quaternion.identity, Hollow);
            h.Ellipsoid(new Vector3(0f, 0.6f, 9f), new Vector3(2.6f, 0.7f, 0.9f), Quaternion.identity, Hollow);
            h.Ellipsoid(new Vector3(-7.6f, 6f, 0f), new Vector3(1.1f, 3f, 1.8f), Quaternion.identity, Shade);
            h.Ellipsoid(new Vector3(7.6f, 6f, 0f), new Vector3(1.1f, 3f, 1.8f), Quaternion.identity, Shade);
            AddPart("HeadMesh", head, h.ToMesh("ColossusHead"), mat);

            // The reaching arm, on a pivot at the shoulder so it can sweep across the sand.
            arm = new GameObject("Arm").transform;
            arm.SetParent(root, false);
            arm.localPosition = ShoulderPivot;
            var a = new MeshBuilder();
            a.Limb(Vector3.zero, new Vector3(20f, -15f, 8f), 6.4f, 5.4f, Sandstone);
            a.Ellipsoid(new Vector3(20f, -15f, 8f), new Vector3(5.8f, 5.8f, 5.8f), Quaternion.identity, Shade);
            a.Limb(new Vector3(20f, -15f, 8f), new Vector3(40f, -33f, 20f), 5.4f, 4.4f, Sandstone);
            var local = HandLocal - ShoulderPivot + new Vector3(0f, 0f, 0f);
            var handPos = HandLocal - ShoulderPivot;
            var handYaw = Quaternion.Euler(0f, Mathf.Atan2(handPos.x, handPos.z) * Mathf.Rad2Deg, 0f);
            a.Ellipsoid(handPos, new Vector3(6f, 3f, 8f), handYaw, Sandstone);
            for (int k = 0; k < 4; k++)
            {
                var side = handYaw * new Vector3((k - 1.5f) * 3.2f, 0f, 0f);
                a.Ellipsoid(handPos + handYaw * new Vector3(0f, -0.4f, 9f) + side, new Vector3(1.2f, 1.3f, 5.5f), handYaw, Shade);
            }
            AddPart("ArmMesh", arm, a.ToMesh("ColossusArm"), mat);

            // The furrow the hand drags, a world-space ribbon rebuilt as the sweep widens.
            furrow = new GameObject("ColossusFurrow");
            furrowMesh = new Mesh { name = "ColossusFurrow", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            furrow.AddComponent<MeshFilter>().sharedMesh = furrowMesh;
            var fr = furrow.AddComponent<MeshRenderer>();
            fr.sharedMaterial = mat;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            BuildStorm();
        }

        static void AddPart(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        void BuildStorm()
        {
            var ambience = FindAnyObjectByType<AmbienceSystem>();
            var go = new GameObject("ColossusStorm");
            go.SetActive(false);
            go.transform.position = (Vector3)Position + Vector3.up * 2f;
            storm = go.AddComponent<ParticleSystem>();
            storm.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = storm.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(16f, 38f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.70f, 0.48f, 0.30f), new Color(0.78f, 0.60f, 0.40f, 0.42f));
            var emission = storm.emission;
            emission.rateOverTime = 26f;
            var shape = storm.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 62f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            var vel = storm.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(1.6f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0.11f);
            var noise = storm.noise;
            noise.enabled = true;
            noise.strength = 5f;
            noise.frequency = 0.06f;
            noise.scrollSpeed = 0.15f;
            var size = storm.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 1.8f)));
            var color = storm.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var rotation = storm.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            if (ambience != null && ambience.PuffMaterial != null)
                renderer.sharedMaterial = ambience.PuffMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            // Starts already blown in, not building up from nothing.
            storm.Simulate(10f, true, true);
            storm.Play();
        }

        // ---------------------------------------------------------------- idling

        void Idle()
        {
            clock += Time.deltaTime;
            head.localRotation = Quaternion.Euler(4f * Mathf.Sin(clock * 0.31f), 40f * Mathf.Sin(clock * 2f * Mathf.PI / 37f + 1f), 0f);
            var theta = SweepDegrees * Mathf.Sin(clock * 2f * Mathf.PI / 50f);
            var spin = Quaternion.Euler(0f, theta, 0f);
            // Keep the hand on the sand where the ground rises and falls under it (within a few metres).
            var handWorld = root.TransformPoint(ShoulderPivot + spin * (HandLocal - ShoulderPivot));
            var lift = Mathf.Clamp(source.SampleHeight(new float2(handWorld.x, handWorld.z)) - root.position.y - HandLocal.y + 0.5f, -4f, 4f);
            arm.localPosition = ShoulderPivot + new Vector3(0f, lift, 0f);
            arm.localRotation = spin;

            var bucket = Mathf.RoundToInt(theta) + 30;
            if (bucket >= 0 && bucket < swept.Length && !swept[bucket])
            {
                swept[bucket] = true;
                sweptMin = Mathf.Min(sweptMin, bucket);
                sweptMax = Mathf.Max(sweptMax, bucket);
            }
            if (sweptMin <= sweptMax && (sweptMin != furrowMin || sweptMax != furrowMax))
                BuildFurrow();

            // Sand thrown up where the hand drags.
            handPuff += Time.deltaTime;
            if (handPuff > 0.12f && storm != null)
            {
                handPuff = 0f;
                var ep = new ParticleSystem.EmitParams
                {
                    position = handWorld + Vector3.up * 1.5f,
                    velocity = new Vector3(UnityEngine.Random.Range(-2f, 2f), 3f, UnityEngine.Random.Range(-2f, 2f)),
                    startSize = UnityEngine.Random.Range(8f, 14f),
                    startLifetime = 6f,
                };
                storm.Emit(ep, 1);
            }
        }

        void BuildFurrow()
        {
            furrowMin = sweptMin;
            furrowMax = sweptMax;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            const float width = 5f;
            var pivotXZ = new Vector3(ShoulderPivot.x, 0f, ShoulderPivot.z);
            var handXZ = new Vector3(HandLocal.x, 0f, HandLocal.z);
            for (int b = sweptMin; b <= sweptMax; b++)
            {
                var spin = Quaternion.Euler(0f, b - 30f, 0f);
                var centre = pivotXZ + spin * (handXZ - pivotXZ);
                var radial = (spin * (handXZ - pivotXZ)).normalized;
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = root.TransformPoint(centre + radial * (s * width * 0.5f));
                    verts.Add(new Vector3(p.x, source.SampleHeight(new float2(p.x, p.z)) + 0.35f, p.z));
                    cols.Add(Shade * 0.7f);
                }
                if (b > sweptMin)
                {
                    var i = verts.Count - 4;
                    tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                }
            }
            furrowMesh.Clear();
            furrowMesh.SetVertices(verts);
            furrowMesh.SetColors(cols);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            furrowMesh.SetNormals(normals);
            furrowMesh.SetTriangles(tris, 0);
            furrowMesh.RecalculateBounds();
        }

        // ---------------------------------------------------------------- frame timing

        void LogFrames()
        {
            if (!FrameLog)
            {
                logFrames = 0;
                return;
            }
            if (logFrames == 0 || logHidden != Hidden)
            {
                logHidden = Hidden;
                logStart = Time.realtimeSinceStartup;
                logTime = 0f;
                logFrames = 0;
                logGpu = logCpu = 0;
                logGpuFrames = 0;
            }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                logCpu += timings[0].cpuFrameTime;
                if (timings[0].gpuFrameTime > 0) { logGpu += timings[0].gpuFrameTime; logGpuFrames++; }
            }
            logFrames++;
            logTime += Time.unscaledDeltaTime;
            if (Time.realtimeSinceStartup - logStart >= 5f)
            {
                Debug.Log($"DunesColossus: framelog colossus {(logHidden ? "hidden" : "shown")}: {logFrames} frames in {Time.realtimeSinceStartup - logStart:F1} s, " +
                          $"mean frame {logTime / logFrames * 1000f:F2} ms, cpu {logCpu / logFrames:F2} ms, gpu {(logGpuFrames > 0 ? logGpu / logGpuFrames : 0):F2} ms ({logGpuFrames} samples)");
                logFrames = 0;
            }
        }

        // ---------------------------------------------------------------- mesh pieces

        /// <summary>Flat-shaded triangles with a little per-facet brightness variation (so the stone reads as cut).</summary>
        internal sealed class MeshBuilder
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();

            void Tri(Vector3 a, Vector3 c, Vector3 b, Color color)
            {
                // (W-48: Ellipsoid, Dome and Limb list their corners anticlockwise seen from outside, which in Unity faces
                // inwards: every part was inside out, normals and all. The corners swap here, so the faces and the normal
                // taken from them point out, and Dome's "inside" now really is the inside.)
                // Not Vector3.normalized: it returns zero below 1e-5, and the mantrap's 4 mm tooth tips fall under that; a zero
                // normal turns NaN in the shader and bloom spreads it into a white blob (A-10c, #41).
                var n = (Vector3)math.normalizesafe(math.cross((float3)(b - a), (float3)(c - a)), new float3(0f, 1f, 0f));
                var hash = math.frac(math.sin(math.dot((float3)(a + b + c), new float3(12.9898f, 78.233f, 37.719f))) * 43758.547f);
                var tint = color * (0.93f + 0.14f * hash);
                tint.a = 1f;
                verts.Add(a); verts.Add(b); verts.Add(c);
                normals.Add(n); normals.Add(n); normals.Add(n);
                colors.Add(tint); colors.Add(tint); colors.Add(tint);
            }

            public void Ellipsoid(Vector3 centre, Vector3 radii, Quaternion rotation, Color color, int lon = 10, int lat = 6)
            {
                Vector3 P(int i, int j)
                {
                    var phi = Mathf.PI * j / lat;
                    var theta = 2f * Mathf.PI * (i % lon) / lon;
                    var v = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta) * radii.x, Mathf.Cos(phi) * radii.y, Mathf.Sin(phi) * Mathf.Sin(theta) * radii.z);
                    return centre + rotation * v;
                }
                for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    var a = P(i, j); var b = P(i + 1, j); var c = P(i + 1, j + 1); var d = P(i, j + 1);
                    if (j > 0) Tri(a, c, b, color);
                    if (j < lat - 1) Tri(a, d, c, color);
                }
            }

            /// <summary>
            /// W-94 (#40): an ellipsoid whose surface is pushed in and out along each point's direction by
            /// <paramref name="scale"/> (given the unit-sphere direction before the radii; 1 = on the ellipsoid), for rock
            /// bodies with dents, ridges and pits. Same facet layout as <see cref="Ellipsoid"/>.
            /// </summary>
            public void Rock(Vector3 centre, Vector3 radii, Quaternion rotation, Color color, int lon, int lat, System.Func<Vector3, float> scale)
            {
                var ring = new Vector3[(lat + 1) * lon];
                for (int j = 0; j <= lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    var phi = Mathf.PI * j / lat;
                    var theta = 2f * Mathf.PI * i / lon;
                    var u = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    ring[j * lon + i] = centre + rotation * Vector3.Scale(u, radii) * scale(u);
                }
                for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int i1 = (i + 1) % lon;
                    var a = ring[j * lon + i]; var b = ring[j * lon + i1]; var c = ring[(j + 1) * lon + i1]; var d = ring[(j + 1) * lon + i];
                    if (j > 0) Tri(a, c, b, color);
                    if (j < lat - 1) Tri(a, d, c, color);
                }
            }

            /// <summary>The upper half of an ellipsoid (a bowl opening downwards), for coconut-shell shapes; inside = wound to face inwards.</summary>
            public void Dome(Vector3 centre, Vector3 radii, Quaternion rotation, Color color, bool inside = false, int lon = 12, int lat = 4)
            {
                Vector3 P(int i, int j)
                {
                    var phi = Mathf.PI * 0.5f * j / lat;
                    var theta = 2f * Mathf.PI * (i % lon) / lon;
                    var v = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta) * radii.x, Mathf.Cos(phi) * radii.y, Mathf.Sin(phi) * Mathf.Sin(theta) * radii.z);
                    return centre + rotation * v;
                }
                for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    var a = P(i, j); var b = P(i + 1, j); var c = P(i + 1, j + 1); var d = P(i, j + 1);
                    if (inside)
                    {
                        if (j > 0) Tri(a, b, c, color);
                        Tri(a, c, d, color);
                    }
                    else
                    {
                        if (j > 0) Tri(a, c, b, color);
                        Tri(a, d, c, color);
                    }
                }
            }

            public void Limb(Vector3 from, Vector3 to, float r0, float r1, Color color, int sides = 7)
            {
                var axis = (to - from).normalized;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var up = Vector3.Cross(axis, side);
                Vector3 P(int i, bool end) => (end ? to : from) + (side * Mathf.Cos(2f * Mathf.PI * (i % sides) / sides) + up * Mathf.Sin(2f * Mathf.PI * (i % sides) / sides)) * (end ? r1 : r0);
                for (int i = 0; i < sides; i++)
                {
                    var a = P(i, false); var b = P(i + 1, false); var c = P(i + 1, true); var d = P(i, true);
                    Tri(a, c, b, color);
                    Tri(a, d, c, color);
                }
                Ellipsoid(from, new Vector3(r0, r0, r0), Quaternion.identity, color, 8, 4);
                Ellipsoid(to, new Vector3(r1, r1, r1), Quaternion.identity, color, 8, 4);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetColors(colors);
                var tris = new int[verts.Count];
                for (int i = 0; i < tris.Length; i++) tris[i] = i;
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
