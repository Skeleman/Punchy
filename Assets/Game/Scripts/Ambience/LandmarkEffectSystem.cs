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
        public Material WaterfallMaterial;
        [Tooltip("Effects of landmarks within this distance of the camera are drawn.")]
        public float Range = 700f;

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
            public float Pad;
        }

        const int WaterfallColumns = 12, WaterfallRows = 20;

        readonly List<Emitter> plumes = new List<Emitter>();
        readonly List<Emitter> falls = new List<Emitter>();
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
                if (type != null && type.Effects.Count > 0 && math.distance(inst.Position, cam) < Range + set.Types[inst.Type].Radius * inst.Scale)
                    nearby.Add(i);
            }
            if (nearby.Count == lastNearby.Count && nearby.TrueForAll(lastNearby.Contains))
                return;
            lastNearby.Clear();
            lastNearby.AddRange(nearby);

            plumes.Clear();
            falls.Clear();
            maxParticles = 1;
            float3 min = float.MaxValue, max = float.MinValue;
            foreach (var i in nearby)
            {
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
                    float top;
                    if (effect.Kind == LandmarkEffectKind.Waterfall)
                    {
                        // From the lip, out (towards -X) and down to whatever lies below: pool or ground.
                        var outDir = -inst.Facing;
                        var landing = at + outDir * effect.Height * inst.Scale;
                        var below = source.SampleGround(landing);
                        var bottom = below.HasWater ? math.max(below.Height, below.WaterSurface) : below.Height;
                        emitter.Position.y = ground.Height + 0.2f;
                        emitter.Out = outDir;
                        emitter.Drop = math.max(emitter.Position.y - bottom, 1f);
                        falls.Add(emitter);
                        top = emitter.Position.y;
                    }
                    else
                    {
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
