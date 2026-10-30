using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Ambience
{
    /// <summary>
    /// Draws the effects of landmarks near the camera: plumes (steam, smoke, geyser spouts, spray, sparkles) and
    /// waterfall curtains. Each effect becomes one emitter in a GPU buffer; the shaders animate every puff and
    /// streak procedurally from time, so there is no simulation on the CPU. Emitters are rebuilt only when the set
    /// of nearby landmarks changes.
    /// </summary>
    public class LandmarkEffectSystem : MonoBehaviour
    {
        public Material PlumeMaterial;
        [Tooltip("Material (Punchy/Flame) for the flames FireSystem collects each frame: torches, campfires.")]
        public Material FlameMaterial;
        public Material WaterfallMaterial;
        [Tooltip("Effects of landmarks within this distance of the camera are drawn.")]
        public float Range = 700f;
        /// <summary>Cave waterfalls are drawn within this distance (small, and underground).</summary>
        const float CaveRange = 120f;
        /// <summary>Flame effects are lit within this distance (they're small; FireSystem draws them each frame).</summary>
        const float FlameRange = 150f;
        /// <summary>How far a cave's waterfall carries out from its notch as it falls (m).</summary>
        const float FallReach = 0.9f;

        /// <summary>One effect. Must match LandmarkEffects.hlsl.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Emitter
        {
            public float3 Position;
            public float Seed;
            public float4 Color;    // rgb, glow
            public float4 Shape;    // height (plume rise / waterfall reach), width, size, lifetime
            public float4 Timing;   // period, duty, opacity, particles
            public float2 Out;      // waterfall: horizontal direction the water travels
            public float Drop;      // waterfall: height of the fall
            public float UnderRock; // 1 for a cave's waterfall (lit as underground)
        }

        const int WaterfallColumns = 12, WaterfallRows = 20;

        readonly List<Emitter> plumes = new List<Emitter>();
        readonly List<Emitter> falls = new List<Emitter>();
        /// <summary>Nearby landmarks' flames (position, size; seed, palette), handed to FireSystem every frame.</summary>
        readonly List<(Vector3 at, float size, int seed, int palette, bool night)> fires = new List<(Vector3, float, int, int, bool)>();
        static readonly int NightId = Shader.PropertyToID("_PunchyNight");
        readonly List<int> nearby = new List<int>(), lastNearby = new List<int>();
        GraphicsBuffer plumeBuffer, fallBuffer;
        MaterialPropertyBlock plumeProps, fallProps;
        int maxParticles;
        Bounds bounds;
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery;
        TerrainSource lastSource;
        float nextCheck;

        static readonly int EmittersId = Shader.PropertyToID("_LandmarkEmitters");
        static readonly int MaxParticlesId = Shader.PropertyToID("_MaxParticles");
        static readonly int GridId = Shader.PropertyToID("_WaterfallGrid");

        void OnDisable()
        {
            plumeBuffer?.Release();
            fallBuffer?.Release();
            plumeBuffer = fallBuffer = null;
            lastNearby.Clear();
        }

        void Update()
        {
            // Flames lit this frame by anyone (torches, campfires): drawn whatever else happens here.
            FireSystem.Draw(FlameMaterial);
            var cam = Camera.main;
            if (cam == null || PlumeMaterial == null || WaterfallMaterial == null || !TryGetWorld(out var source, out var palette))
                return;
            if (!source.Landmarks.IsCreated || palette.Landmarks == null)
                return;

            if (!source.Equals(lastSource))
            {
                lastSource = source;
                lastNearby.Clear();
                nextCheck = 0f;
            }
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 0.5f;
                Gather(source, palette.Landmarks, cam.transform.position);
            }
            var eye = cam.transform.position;
            var flameRange = Punchy.Settings.GraphicsSettings.DetailRange(FlameRange);
            // Night-only fires catch as the light goes (the sky's night past a fifth) and die down at dawn.
            var lit = math.saturate((Shader.GetGlobalFloat(NightId) - 0.2f) / 0.3f);
            foreach (var f in fires)
            {
                var size = f.night ? f.size * lit : f.size;
                if (size > 0.02f && (f.at - eye).sqrMagnitude < flameRange * flameRange)
                    FireSystem.AddFlame(f.at, size, f.seed, f.palette);
            }
            Draw();
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
            return palette != null;
        }

        void Gather(in TerrainSource source, LandmarkType[] types, Vector3 camera)
        {
            ref var set = ref source.Landmarks.Value;
            nearby.Clear();
            var cam = new float2(camera.x, camera.z);
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var type = inst.Type < types.Length ? types[inst.Type] : null;
                if (type != null && type.Effects.Count > 0 && math.distance(inst.Position, cam) < Punchy.Settings.GraphicsSettings.ViewRange(Range) + set.Types[inst.Type].Radius * inst.Scale)
                    nearby.Add(i);
            }
            // Cave waterfalls near the camera (they're small and underground: within CaveRange), as -(index + 1).
            if (source.Caves.IsCreated)
            {
                ref var caveFalls = ref source.Caves.Value.Waterfalls;
                for (int k = 0; k < caveFalls.Length; k++)
                    if (math.distance(caveFalls[k].Lip.xz, cam) < Punchy.Settings.GraphicsSettings.DetailRange(CaveRange))
                        nearby.Add(-(k + 1));
            }
            if (nearby.Count == lastNearby.Count && nearby.TrueForAll(lastNearby.Contains))
                return;
            lastNearby.Clear();
            lastNearby.AddRange(nearby);

            plumes.Clear();
            falls.Clear();
            fires.Clear();
            maxParticles = 1;
            float3 min = float.MaxValue, max = float.MinValue;
            foreach (var i in nearby)
            {
                if (i < 0)
                {
                    // A cave's waterfall: a small curtain from its notch in the wall into its pool, churning white
                    // water and a foam patch where it lands, and mist over them, lit as under rock.
                    var fall = source.Caves.Value.Waterfalls[-i - 1];
                    var curtain = new Emitter
                    {
                        Position = fall.Lip,
                        Seed = math.frac(-i * 0.618f),
                        Color = new float4(0.3f, 0.43f, 0.47f, 0f),
                        Shape = new float4(FallReach, fall.Width, 0.6f, 2f),
                        Timing = new float4(0f, 0.25f, 0.75f, 20f),
                        Out = fall.Out,
                        Drop = fall.Drop,
                        UnderRock = 1f
                    };
                    falls.Add(curtain);
                    var landing = fall.Lip + new float3(fall.Out.x, 0f, fall.Out.y) * FallReach - new float3(0f, fall.Drop, 0f);
                    plumes.Add(new Emitter
                    {
                        Position = landing,
                        Seed = math.frac(-i * 0.511f),
                        Color = new float4(0.95f, 0.97f, 1f, 0f),
                        Shape = new float4(0.6f, fall.Width * 0.6f, 0.3f, 0.8f),
                        Timing = new float4(0f, 0.25f, 0.75f, 24f),
                        UnderRock = 1f
                    });
                    plumes.Add(new Emitter
                    {
                        Position = landing + new float3(0f, 0.05f, 0f),
                        Seed = math.frac(-i * 0.733f),
                        Color = new float4(0.9f, 0.95f, 1f, 0f),
                        Shape = new float4(0.12f, fall.Width * 1.1f, 0.45f, 1.8f),
                        Timing = new float4(0f, 0.25f, 0.55f, 24f),
                        UnderRock = 1f
                    });
                    plumes.Add(new Emitter
                    {
                        Position = landing,
                        Seed = math.frac(-i * 0.377f),
                        Color = new float4(0.92f, 0.93f, 0.95f, 0f),
                        Shape = new float4(1.8f, fall.Width * 1.2f, 0.9f, 2.5f),
                        Timing = new float4(0f, 0.25f, 0.16f, 20f),
                        UnderRock = 1f
                    });
                    maxParticles = math.max(maxParticles, 24);
                    min = math.min(min, landing - 3f);
                    max = math.max(max, fall.Lip + 3f);
                    continue;
                }
                ref var inst = ref set.Instances[i];
                var radius = set.Types[inst.Type].Radius * inst.Scale;
                var e = 0;
                foreach (var effect in types[inst.Type].Effects)
                {
                    var at = LandmarkSampling.ToWorld(inst, effect.Offset, radius);
                    var ground = source.SampleGround(at);
                    var y = ground.HasWater ? math.max(ground.Height, ground.WaterSurface) : ground.Height;
                    var color = effect.Color.linear;
                    var emitter = new Emitter
                    {
                        Position = new float3(at.x, y, at.y),
                        Seed = math.frac(math.abs(inst.Seed) * 0.37f + e * 0.618f),
                        Color = new float4(color.r, color.g, color.b, effect.Glow),
                        Shape = new float4(effect.Height * inst.Scale, effect.Width * inst.Scale, effect.Size * inst.Scale, effect.Lifetime),
                        Timing = new float4(effect.Period, effect.Duty, effect.Opacity, effect.Particles)
                    };
                    if (effect.Kind == LandmarkEffectKind.Flame)
                    {
                        // One fire: a tall tongue in the middle and the rest clustered close round it, overlapping,
                        // shorter towards the edge.
                        var tongues = math.max(1, effect.Particles);
                        var reach = effect.Width * inst.Scale * 0.25f;
                        for (int k = 0; k < tongues; k++)
                        {
                            var a = k * 2.39996f + emitter.Seed * 6.28f;
                            var spread = k == 0 ? 0f : reach * math.sqrt(k / (float)tongues);
                            var p = new float3(at.x + math.cos(a) * spread, y + 0.05f, at.y + math.sin(a) * spread);
                            var size = effect.Size * inst.Scale * (k == 0 ? 1.3f : 1f - 0.3f * spread / math.max(reach, 0.01f));
                            fires.Add((p, size, (int)(math.abs(inst.Seed) * 31 + e * 7 + k), (int)effect.Flame, effect.NightOnly));
                        }
                        e++;
                        continue;
                    }
                    float top;
                    if (effect.Kind == LandmarkEffectKind.Waterfall)
                    {
                        // From the lip, out (towards -X) and down to whatever lies below: pool or ground. The lip is
                        // where the ground first reaches the cliff's top, going in (+X) from the offset, since a rough
                        // rim can dip where the offset lands and the water then started low down the face (F-48).
                        var outDir = -inst.Facing;
                        var lipHeight = ground.Height;
                        var rim = lipHeight;
                        for (var d = 0.5f; d <= 10f; d += 0.5f)
                            rim = math.max(rim, source.SampleHeight(at + inst.Facing * d));
                        for (var d = 0f; d <= 10f && lipHeight < rim - 0.5f; d += 0.5f)
                        {
                            lipHeight = source.SampleHeight(at + inst.Facing * d);
                            emitter.Position.xz = at + inst.Facing * d;
                        }
                        var landing = emitter.Position.xz + outDir * effect.Height * inst.Scale;
                        var below = source.SampleGround(landing);
                        var bottom = below.HasWater ? math.max(below.Height, below.WaterSurface) : below.Height;
                        emitter.Position.y = lipHeight + 0.2f;
                        emitter.Out = outDir;
                        emitter.Drop = math.max(emitter.Position.y - bottom, 1f);
                        falls.Add(emitter);
                        top = emitter.Position.y;
                    }
                    else
                    {
                        // Bubbles ride the plume's draw, flagged by a negative lifetime (LandmarkEffects.hlsl).
                        if (effect.Kind == LandmarkEffectKind.Bubbles)
                            emitter.Shape.w = -math.max(effect.Lifetime, 0.1f);
                        plumes.Add(emitter);
                        maxParticles = math.max(maxParticles, effect.Particles);
                        top = y + effect.Height * inst.Scale;
                    }
                    min = math.min(min, emitter.Position - radius);
                    max = math.max(max, new float3(emitter.Position.x, top, emitter.Position.z) + radius);
                    e++;
                }
            }
            bounds = new Bounds((min + max) * 0.5f, max - min);
            Upload(ref plumeBuffer, plumes, ref plumeProps);
            Upload(ref fallBuffer, falls, ref fallProps);
        }

        static void Upload(ref GraphicsBuffer buffer, List<Emitter> emitters, ref MaterialPropertyBlock props)
        {
            if (emitters.Count == 0)
                return;
            if (buffer == null || buffer.count < emitters.Count)
            {
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, math.ceilpow2(emitters.Count), Marshal.SizeOf<Emitter>());
            }
            buffer.SetData(emitters);
            props ??= new MaterialPropertyBlock();
            props.SetBuffer(EmittersId, buffer);
        }

        void Draw()
        {
            if (plumes.Count > 0)
            {
                plumeProps.SetInteger(MaxParticlesId, maxParticles);
                var rp = new RenderParams(PlumeMaterial) { worldBounds = bounds, matProps = plumeProps };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, plumes.Count * maxParticles);
            }
            if (falls.Count > 0)
            {
                fallProps.SetVector(GridId, new Vector4(WaterfallColumns, WaterfallRows, 0f, 0f));
                var rp = new RenderParams(WaterfallMaterial) { worldBounds = bounds, matProps = fallProps };
                Graphics.RenderPrimitives(rp, MeshTopology.Triangles, WaterfallColumns * WaterfallRows * 6, falls.Count);
            }
        }
    }
}
