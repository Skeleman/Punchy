using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>
    /// Driftwood floating down the rivers near the camera (#86): a small pool of logs and branches that now and then
    /// appear upstream on a river within ~150 m, drift with its flow, bob and turn slowly, sometimes snag on a bank for
    /// a few seconds, and sink away where the river meets a lake or the sea, or when far behind. Cosmetic and local:
    /// each player sees their own. The meshes are the Driftwood flora species' variants (the gravel-bar logs), drawn
    /// with the vertex-coloured Colossus material. The drift is a small Burst job that runs while the rest of the frame
    /// does and is read back at the next Update, as CritterSystem's placement (W-75).
    /// </summary>
    public class DriftwoodSystem : MonoBehaviour
    {
        // W-80 (#86): a lone piece here and there, never several in one view.
        public int MaxLogs = 3;
        public float SpawnRadius = 150f;
        [Tooltip("Seconds between new logs while the pool isn't full.")]
        public float SpawnInterval = 15f;
        [Tooltip("No new log within this distance (m) of one already afloat.")]
        public float MinGap = 60f;

        /// <summary>Logs afloat now, and the main-thread cost of the last Update (ms), for probes and the perf log.</summary>
        public static int Live;
        public static float LastMs;

        const int Variants = 4;

        struct Log
        {
            public float3 Position;
            public float2 Velocity;
            public float Yaw, Spin, Phase, Scale;
            public float Fade;      // 0..1: rises in from under the surface, sinks away again
            public float Snag;      // seconds left caught on a bank (0 = drifting)
            public int Variant;
            public int State;       // 0 free slot, 1 afloat, 2 sinking away
        }

        NativeArray<Log> logs;
        JobHandle pending;
        bool hasPending;
        float spawnTimer;
        uint frame;
        readonly Mesh[] meshes = new Mesh[Variants];
        readonly float[] heights = new float[Variants];
        Material material;
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery;
        bool meshesTried;
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<DriftwoodSystem>() == null)
                new GameObject("Driftwood").AddComponent<DriftwoodSystem>();
        }

        void OnDisable()
        {
            if (hasPending)
                pending.Complete();
            hasPending = false;
            if (logs.IsCreated)
                logs.Dispose();
            Live = 0;
        }

        void Update()
        {
            watch.Restart();
            var cam = Camera.main;
            if (hasPending)
            {
                pending.Complete();
                hasPending = false;
            }
            if (cam == null || !TryGetWorld(out var source, out var palette) || !Meshes(palette))
                return;
            if (!logs.IsCreated || logs.Length != MaxLogs)
            {
                if (logs.IsCreated) logs.Dispose();
                logs = new NativeArray<Log>(MaxLogs, Allocator.Persistent);
            }
            if (CaveSystem.SurfaceHidden >= 0.99f)
                return;

            Draw();

            spawnTimer -= Time.deltaTime;
            var spawn = spawnTimer <= 0f;
            if (spawn)
                spawnTimer = SpawnInterval;
            pending = new DriftJob
            {
                Source = source,
                Logs = logs,
                Camera = cam.transform.position,
                Dt = math.min(Time.deltaTime, 0.1f),
                Time = Time.time,
                Seed = math.hash(new uint2(++frame, 0x9E3779B9u)) | 1u,
                Spawn = spawn,
                MinGap = MinGap,
                SpawnRadius = Punchy.Settings.GraphicsSettings.DetailRange(SpawnRadius)
            }.Schedule();
            hasPending = true;
            JobHandle.ScheduleBatchedJobs();
            watch.Stop();
            LastMs = (float)watch.Elapsed.TotalMilliseconds;
        }

        void Draw()
        {
            var live = 0;
            for (int i = 0; i < logs.Length; i++)
            {
                var log = logs[i];
                if (log.State == 0)
                    continue;
                live++;
                // Half under water, rolling and pitching a little on the bob; sinking a log's height as it fades.
                var t = Time.time + log.Phase * 10f;
                var rot = Quaternion.Euler(math.sin(t * 0.9f) * 4f, math.degrees(log.Yaw), math.sin(t * 1.3f) * 6f);
                var scale = log.Scale * math.lerp(0.6f, 1f, log.Fade);
                var pos = (Vector3)log.Position + Vector3.down * (heights[log.Variant] * scale * (0.45f + (1f - log.Fade) * 1.2f));
                var rp = new RenderParams(material)
                {
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true,
                    worldBounds = new Bounds(pos, Vector3.one * 8f * scale)
                };
                Graphics.RenderMesh(rp, meshes[log.Variant], 0, Matrix4x4.TRS(pos, rot, Vector3.one * scale));
            }
            Live = live;
        }

        /// <summary>Grows the Driftwood species' variants once (found through the biomes' flora lists).</summary>
        bool Meshes(BiomePalette palette)
        {
            if (material != null)
                return true;
            if (meshesTried)
                return false;
            meshesTried = true;
            FloraSpecies species = null;
            foreach (var biome in palette.Biomes)
                if (biome != null && biome.Flora != null)
                    foreach (var f in biome.Flora)
                        if (f?.Species != null && f.Species.Form == FloraForm.Driftwood)
                            species = f.Species;
            var colossus = Resources.Load<Material>("Colossus");
            if (species == null || colossus == null)
                return false;
            for (int v = 0; v < Variants; v++)
            {
                var model = FloraGenerator.Grow(species, v);
                meshes[v] = model.Lods[1] != null ? model.Lods[1] : model.Lods[0];
                heights[v] = math.max(model.Height, 0.2f);
            }
            // Wet wood: a touch darker than the bleached logs on the bars.
            material = new Material(colossus) { name = "Driftwood" };
            material.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.7f, 1f));
            return true;
        }

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        /// <summary>Moves every log one step and, when asked, tries to put a new one on a river upstream of the camera.</summary>
        [BurstCompile]
        struct DriftJob : IJob
        {
            public TerrainSource Source;
            public NativeArray<Log> Logs;
            public float3 Camera;
            public float Dt, Time;
            public uint Seed;
            public bool Spawn;
            public float SpawnRadius;
            public float MinGap;

            /// <summary>Another log afloat within MinGap of a point (so two never share a view).</summary>
            bool Crowded(float2 xz)
            {
                for (int i = 0; i < Logs.Length; i++)
                    if (Logs[i].State != 0 && math.distance(Logs[i].Position.xz, xz) < MinGap)
                        return true;
                return false;
            }

            /// <summary>Running river water here: a channel with flow, deep enough to float in.</summary>
            bool River(float2 xz, out GroundSample g)
            {
                g = Source.SampleGround(xz);
                return g.HasWater && g.River > 0.3f && math.lengthsq(g.Flow) > 0.25f && g.WaterSurface > g.Height + 0.3f;
            }

            float Depth(float2 xz)
            {
                var g = Source.SampleGround(xz);
                return g.HasWater ? g.WaterSurface - g.Height : -1f;
            }

            public void Execute()
            {
                var rng = new Unity.Mathematics.Random(Seed);
                var cam = Camera.xz;
                var free = -1;
                for (int i = 0; i < Logs.Length; i++)
                {
                    var log = Logs[i];
                    if (log.State == 0)
                    {
                        free = i;
                        continue;
                    }
                    var xz = log.Position.xz;
                    if (math.distance(xz, cam) > SpawnRadius * 1.25f)
                    {
                        Logs[i] = default;   // out of sight: just drop it
                        continue;
                    }
                    var running = River(xz, out var g);
                    if (log.State == 1 && !running)
                        log.State = 2;   // reached a lake, the sea or a dry edge: sink away
                    log.Fade = log.State == 2 ? log.Fade - Dt / 2.5f : math.min(1f, log.Fade + Dt / 2f);
                    if (log.Fade <= 0f)
                    {
                        Logs[i] = default;
                        continue;
                    }

                    var target = float2.zero;
                    if (running)
                    {
                        // Faster in white water; steer away from the shallower side so logs keep to the channel.
                        var speed = 1.1f + g.Rapids * 2.6f;
                        var side = new float2(-g.Flow.y, g.Flow.x);
                        var lean = math.clamp(Depth(xz + side * 2.5f) - Depth(xz - side * 2.5f), -1f, 1f);
                        target = g.Flow * speed + side * lean * 0.6f;
                        var depth = g.WaterSurface - g.Height;
                        if (log.Snag <= 0f && depth < 0.7f && rng.NextFloat() < Dt * 0.25f)
                        {
                            log.Snag = rng.NextFloat(3f, 9f);   // caught on the bank for a while
                            log.Spin = rng.NextFloat(-0.25f, 0.25f);
                        }
                    }
                    if (log.Snag > 0f)
                    {
                        log.Snag -= Dt;
                        target = float2.zero;
                        log.Yaw += log.Spin * 0.4f * Dt;   // pivots slowly on its snag
                    }
                    else
                        log.Yaw += log.Spin * Dt;
                    log.Velocity = math.lerp(log.Velocity, target, math.saturate(Dt * (log.Snag > 0f ? 3f : 0.8f)));
                    xz += log.Velocity * Dt;
                    var surface = g.HasWater ? g.WaterSurface : log.Position.y;
                    var bob = math.sin(Time * 1.4f + log.Phase * 6.28f) * 0.04f;
                    log.Position = new float3(xz.x, math.lerp(log.Position.y, surface + bob, math.saturate(Dt * 4f)), xz.y);
                    Logs[i] = log;
                }

                if (!Spawn || free < 0)
                    return;
                // A handful of tries in the ring round the camera; keep the first on running water that flows
                // towards the camera (so it drifts into view), or any river close by.
                for (int k = 0; k < 24; k++)
                {
                    var a = rng.NextFloat(math.PI * 2f);
                    var d = math.sqrt(rng.NextFloat(0.04f, 1f)) * SpawnRadius;
                    var xz = cam + new float2(math.cos(a), math.sin(a)) * d;
                    if (!River(xz, out var g) || g.WaterSurface - g.Height < 0.6f || Crowded(xz))
                        continue;
                    var towards = math.dot(g.Flow, math.normalizesafe(cam - xz));
                    if (towards < -0.2f && d > 50f)
                        continue;
                    var variant = rng.NextInt(4);
                    Logs[free] = new Log
                    {
                        Position = new float3(xz.x, g.WaterSurface, xz.y),
                        Velocity = g.Flow,
                        Yaw = rng.NextFloat(math.PI * 2f),
                        Spin = rng.NextFloat(-0.15f, 0.15f),
                        Phase = rng.NextFloat(),
                        // Smaller ones read as branches; a few are whole logs.
                        Scale = rng.NextFloat() < 0.4f ? rng.NextFloat(0.4f, 0.6f) : rng.NextFloat(0.7f, 1.1f),
                        Variant = variant,
                        State = 1
                    };
                    return;
                }
            }
        }
    }
}
