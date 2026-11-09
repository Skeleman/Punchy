using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Collections;
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
    public partial class LandmarkEffectSystem : MonoBehaviour
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
        /// <summary>River waterfalls: how far the water carries out as it falls (m), the distance they are drawn from, and the index offset.</summary>
        const float RiverFallReach = 1.6f, RiverFallRange = 350f;
        const int RiverFallBase = 1000000, GrandFallBase = 2000000;
        /// <summary>
        /// W-43c chimney smoke: plots within this distance whose building has a chimney may smoke (index offset
        /// ChimneyBase); a seeded share of buildings smoke, the same ones on every machine.
        /// </summary>
        const float ChimneyRange = 220f, SmokeShare = 0.42f;
        const int ChimneyBase = 3000000, SmokeParticles = 14;
        /// <summary>Last chimney gather: "smoking/total chimney buildings in range, particles, ms" (for the world probe).</summary>
        public static string LastChimneyLog = "";
        World floraWorld;
        EntityQuery floraQuery;

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
        /// <summary>River waterfalls (W-98): heard like a curtain but not drawn, as the river's own surface falls there.</summary>
        readonly List<Emitter> heardFalls = new List<Emitter>();
        /// <summary>Nearby landmarks' flames (position, size; seed, palette), handed to FireSystem every frame.</summary>
        readonly List<(Vector3 at, float size, int seed, int palette, bool night)> fires = new List<(Vector3, float, int, int, bool)>();
        /// <summary>One light per landmark fire (campfires, braziers, sconces): where it sits, its width (m), seed,
        /// palette and night-only; requested from LightBudget every frame as a fire light (refactor 3.1: they had none).</summary>
        readonly List<(Vector3 at, float width, int seed, int palette, bool night)> fireLights = new List<(Vector3, float, int, int, bool)>();
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
            foreach (var handle in fallLoops.Values)
                Punchy.Audio.GameAudio.StopLoop(handle);
            fallLoops.Clear();
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
            FallSounds(eye);
            var flameRange = Punchy.Settings.GraphicsSettings.DetailRange(FlameRange);
            // Night-only fires catch as the light goes (the sky's night past a fifth) and die down at dawn.
            var lit = math.saturate((Shader.GetGlobalFloat(NightId) - 0.2f) / 0.3f);
            foreach (var f in fires)
            {
                var size = f.night ? f.size * lit : f.size;
                if (size > 0.02f && (f.at - eye).sqrMagnitude < flameRange * flameRange)
                    FireSystem.AddFlame(f.at, size, f.seed, f.palette);
            }
            // Each fire's light: brighter and further for a wider fire, flickering; LightBudget decides by its
            // on-screen reach whether it lights (a glow slot) or only its flames show (refactor 3.1).
            var t = Time.time;
            foreach (var f in fireLights)
            {
                var strength = f.night ? lit : 1f;
                if (strength <= 0.02f)
                    continue;
                var flicker = 0.85f + 0.1f * Mathf.Sin(t * 8.3f + (f.seed & 255)) + 0.05f * Mathf.Sin(t * 21.1f + (f.seed & 63));
                var wide = math.clamp(f.width, 0.3f, 4f);
                var light = FireSystem.Light(f.palette, flicker * strength * math.clamp(0.6f + 0.4f * wide, 0.7f, 1.8f));
                light.w *= math.clamp(0.6f + 0.5f * wide, 0.8f, 2.2f);
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fire, f.seed, f.at, light);
            }
            ForestFires(source, palette, eye, lit);
            Draw();
        }

        // ---- Waterfall sound (#13, W-22) ----
        /// <summary>At most this many falls are heard at once (each loop holds one of GameAudio's 24 voices).</summary>
        const int MaxFallSounds = 4;
        readonly Dictionary<float3, int> fallLoops = new Dictionary<float3, int>();
        readonly List<float3> fallGone = new List<float3>();
        readonly List<(float3 at, float d, float volume, float reach)> fallHeard = new List<(float3, float, float, float)>();
        // The last logged (sources, falls drawn, max distance): the log string is only built when one changes (W-117 GC).
        int3 lastFallLog = new int3(-1);

        /// <summary>
        /// A fall's loudness and reach from its drop: 0.45 at 2 m up to 1.0 at 14 m and over; heard 40 m out, 60 m at 14 m,
        /// then (W-29, the grand falls) on out to 180 m for a 70 m drop and over.
        /// </summary>
        static void FallVoice(float drop, out float volume, out float reach)
        {
            var tall = math.saturate((drop - 2f) / 12f);
            volume = math.lerp(0.45f, 1f, tall);
            reach = math.lerp(40f, 60f, tall) + 120f * math.saturate((drop - 14f) / 56f);
        }

        /// <summary>
        /// A looping rush (SoundId.WaterfallRush) at the middle of each of the nearest falls (river, landmark and cave
        /// falls: every curtain emitter in range), louder and carrying further for taller falls. GameAudio's logarithmic
        /// rolloff holds full volume to a quarter of the reach; on top of it the volume fades smoothly to zero from 60% to
        /// 100% of the reach, where the loop stops, so nothing cuts off with a click.
        /// </summary>
        void FallSounds(Vector3 eye)
        {
            fallHeard.Clear();
            float3 ear = eye;
            for (int k = 0; k < falls.Count + heardFalls.Count; k++)
            {
                var f = k < falls.Count ? falls[k] : heardFalls[k - falls.Count];
                var mid = f.Position + new float3(f.Out.x, 0f, f.Out.y) * f.Shape.x * 0.5f - new float3(0f, f.Drop * 0.5f, 0f);
                FallVoice(f.Drop, out var volume, out var reach);
                var d = math.distance(mid, ear);
                if (d < reach)
                    fallHeard.Add((mid, d, volume * (1f - math.smoothstep(reach * 0.6f, reach, d)), reach));
            }
            fallHeard.Sort((a, b) => a.d.CompareTo(b.d));
            if (fallHeard.Count > MaxFallSounds)
                fallHeard.RemoveRange(MaxFallSounds, fallHeard.Count - MaxFallSounds);
            fallGone.Clear();
            foreach (var key in fallLoops.Keys)
            {
                var keep = false;
                foreach (var h in fallHeard) keep |= h.at.Equals(key);
                if (!keep) fallGone.Add(key);
            }
            foreach (var key in fallGone)
            {
                Punchy.Audio.GameAudio.StopLoop(fallLoops[key]);
                fallLoops.Remove(key);
            }
            var maxReach = 0f;
            foreach (var h in fallHeard)
            {
                maxReach = math.max(maxReach, h.reach);
                if (!fallLoops.TryGetValue(h.at, out var handle) || handle == 0)
                    fallLoops[h.at] = Punchy.Audio.GameAudio.StartLoop(Punchy.Audio.SoundId.WaterfallRush, h.at, h.volume, h.reach, Punchy.Audio.SoundGroup.World);
                else
                    Punchy.Audio.GameAudio.MoveLoop(handle, h.at, h.volume);
            }
            var logged = new int3(fallHeard.Count, falls.Count, (int)math.round(maxReach));
            if (fallHeard.Count > 0 && !logged.Equals(lastFallLog))
            {
                lastFallLog = logged;
                Debug.Log($"[falls] sound sources {fallHeard.Count} (of {falls.Count} falls drawn), max distance {maxReach:F0} m");
            }
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

        /// <summary>
        /// One grand falls tier (W-29): river-fall curtains across its width (three side by side for the main drop, which
        /// also throws a wider, longer arc), white water at the lip, and churn, foam and mist over its plunge pool; the main
        /// drop's mist rises high and wide.
        /// </summary>
        void GrandTier(ref GrandFallTier t, float seed, ref float3 min, ref float3 max)
        {
            var main = t.Drop > 30f;
            var reach = main ? 3f + t.Drop * 0.06f : RiverFallReach + t.Drop * 0.05f;
            var out3 = new float3(t.Out.x, 0f, t.Out.y);
            var side = new float3(-t.Out.y, 0f, t.Out.x);
            int curtains = main && !t.Shelf ? 3 : 1; // a lake shelf stretch is one sheet, as wide as its chord of the lip (W-81)
            for (int c = 0; c < curtains; c++)
            {
                var offset = curtains == 1 ? 0f : (c - 1) * t.Width * 0.3f;
                falls.Add(new Emitter
                {
                    Position = t.Lip + side * offset,
                    Seed = math.frac(seed + c * 0.37f),
                    Color = new float4(0.6f, 0.72f, 0.76f, 0f),
                    Shape = new float4(reach, t.Width * (t.Shelf ? 1f : main ? 0.4f : 0.85f), main ? 2.4f : 1.3f, main ? 3.4f : 1.9f),
                    Timing = new float4(0f, 0.25f, 0.85f, 24f),
                    Out = t.Out,
                    Drop = t.Drop
                });
            }
            var foot = t.Lip + out3 * reach - new float3(0f, t.Drop, 0f);
            plumes.Add(new Emitter
            {
                Position = foot, Seed = math.frac(seed + 0.31f), Color = new float4(0.95f, 0.97f, 1f, 0f),
                Shape = new float4(main ? 6f : 2.5f, t.Width * (main ? 1.4f : 0.9f), main ? 2.2f : 1f, main ? 2.2f : 1.4f), Timing = new float4(0f, 0.25f, 0.6f, 24f)
            });
            if (!t.Shelf) // the shelf falls straight into the lake: the foot spray is its pool
            plumes.Add(new Emitter
            {
                Position = new float3(t.Pool.x, t.Pool.y + 0.1f, t.Pool.z), Seed = math.frac(seed + 0.62f), Color = new float4(0.92f, 0.94f, 0.96f, 0f),
                Shape = new float4(0.3f, t.PoolRadius * 1.3f, main ? 2f : 1.2f, 2.4f), Timing = new float4(0f, 0.25f, 0.6f, 24f)
            });
            plumes.Add(new Emitter
            {
                Position = t.Lip + out3 * 0.8f - new float3(0f, 0.3f, 0f), Seed = math.frac(seed + 0.9f), Color = new float4(0.94f, 0.96f, 1f, 0f),
                Shape = new float4(1.2f, t.Width * 0.8f, 1.1f, 1.4f), Timing = new float4(0f, 0.25f, 0.4f, 24f)
            });
            if (main)
            {
                // Mist: two big soft plumes rising from the pool, the higher one drifting up the face.
                plumes.Add(new Emitter
                {
                    Position = foot + out3 * 4f, Seed = math.frac(seed + 0.13f), Color = new float4(0.93f, 0.95f, 0.98f, 0f),
                    Shape = new float4(t.Drop * 0.35f, t.PoolRadius * 1.6f, 6f, 5f), Timing = new float4(0f, 0.25f, 0.35f, 24f)
                });
                plumes.Add(new Emitter
                {
                    Position = foot + out3 * 10f, Seed = math.frac(seed + 0.47f), Color = new float4(0.93f, 0.95f, 0.98f, 0f),
                    Shape = new float4(t.Drop * 0.6f, t.PoolRadius * 2.2f, 9f, 7f), Timing = new float4(0f, 0.25f, 0.22f, 24f)
                });
            }
            maxParticles = math.max(maxParticles, 24);
            min = math.min(min, foot - t.PoolRadius - 6f);
            max = math.max(max, t.Lip + t.PoolRadius + 6f);
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
            // River waterfalls (version 2 rivers) within sight, as -(RiverFallBase + index).
            if (source.Rivers.IsCreated && source.RiverSettings.FallDrop > 0f)
            {
                ref var riverFalls = ref source.Rivers.Value.Falls;
                for (int k = 0; k < riverFalls.Length; k++)
                    if (math.distance(riverFalls[k].Lip.xz, cam) < Punchy.Settings.GraphicsSettings.DetailRange(RiverFallRange))
                        nearby.Add(-(RiverFallBase + k));
            }
            // The grand falls' tiers (W-29) are big: seen from the whole view range, as -(GrandFallBase + index).
            if (source.GrandFalls.IsCreated)
            {
                ref var tiers = ref source.GrandFalls.Value.Tiers;
                for (int k = 0; k < tiers.Length; k++)
                    if (math.distance(tiers[k].Lip.xz, cam) < Punchy.Settings.GraphicsSettings.ViewRange(Range))
                        nearby.Add(-(GrandFallBase + k));
            }
            NearChimneys(source, cam);
            if (nearby.Count == lastNearby.Count && nearby.TrueForAll(lastNearby.Contains))
                return;
            lastNearby.Clear();
            lastNearby.AddRange(nearby);

            plumes.Clear();
            falls.Clear();
            heardFalls.Clear();
            fires.Clear();
            fireLights.Clear();
            maxParticles = 1;
            float3 min = float.MaxValue, max = float.MinValue;
            foreach (var i in nearby)
            {
                if (i <= -ChimneyBase)
                {
                    ChimneySmoke(source, -i - ChimneyBase, ref min, ref max);
                    continue;
                }
                if (i <= -GrandFallBase)
                {
                    GrandTier(ref source.GrandFalls.Value.Tiers[-i - GrandFallBase], math.frac(-i * 0.618f), ref min, ref max);
                    continue;
                }
                if (i <= -RiverFallBase)
                {
                    // A river waterfall: white water at the lip and spray and foam at the foot. The falling water itself is
                    // the river's surface over the step (Water.shader, W-98); its curtain is only heard, not drawn.
                    var rf = source.Rivers.Value.Falls[-i - RiverFallBase];
                    var seed = math.frac(-i * 0.618f);
                    heardFalls.Add(new Emitter
                    {
                        Position = rf.Lip,
                        Seed = seed,
                        Color = new float4(0.55f, 0.68f, 0.72f, 0f),
                        Shape = new float4(RiverFallReach, rf.Width * 0.8f, 1.1f, 1.8f),
                        Timing = new float4(0f, 0.25f, 0.8f, 20f),
                        Out = rf.Out,
                        Drop = rf.Drop
                    });
                    var foot = rf.Lip + new float3(rf.Out.x, 0f, rf.Out.y) * RiverFallReach - new float3(0f, rf.Drop, 0f);
                    plumes.Add(new Emitter
                    {
                        Position = foot, Seed = math.frac(seed + 0.31f), Color = new float4(0.95f, 0.97f, 1f, 0f),
                        Shape = new float4(2.5f, rf.Width * 0.9f, 0.9f, 1.4f), Timing = new float4(0f, 0.25f, 0.6f, 24f)
                    });
                    plumes.Add(new Emitter
                    {
                        Position = foot + new float3(0f, 0.1f, 0f), Seed = math.frac(seed + 0.62f), Color = new float4(0.92f, 0.94f, 0.96f, 0f),
                        Shape = new float4(0.3f, rf.Width * 1.3f, 1.2f, 2.2f), Timing = new float4(0f, 0.25f, 0.6f, 24f)
                    });
                    plumes.Add(new Emitter
                    {
                        Position = rf.Lip + new float3(rf.Out.x, -0.3f, rf.Out.y) * 0.8f, Seed = math.frac(seed + 0.9f), Color = new float4(0.94f, 0.96f, 1f, 0f),
                        Shape = new float4(1.2f, rf.Width * 0.8f, 1.1f, 1.4f), Timing = new float4(0f, 0.25f, 0.4f, 24f)
                    });
                    maxParticles = math.max(maxParticles, 24);
                    min = math.min(min, foot - 6f);
                    max = math.max(max, rf.Lip + 6f);
                    continue;
                }
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
                        fireLights.Add((new Vector3(at.x, y + 0.05f + effect.Size * inst.Scale * 0.6f, at.y), effect.Width * inst.Scale,
                                        (int)(math.abs(inst.Seed) * 31 + e * 7), (int)effect.Flame, effect.NightOnly));
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
                        // White water breaking over the lip (Z-35; the landing's spray is the type's own plume).
                        plumes.Add(new Emitter
                        {
                            Position = emitter.Position + new float3(outDir.x, -0.3f, outDir.y) * 0.8f,
                            Seed = math.frac(math.abs(inst.Seed) * 0.53f + e * 0.29f),
                            Color = new float4(0.94f, 0.96f, 1f, 0f),
                            Shape = new float4(1.2f * inst.Scale, effect.Width * inst.Scale, 1.1f * inst.Scale, 1.4f),
                            Timing = new float4(0f, 0.25f, 0.35f, 24f)
                        });
                        maxParticles = math.max(maxParticles, 24);
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

        /// <summary>The animals' placement tables (FloraLookup), which carry the buildings' chimney tops.</summary>
        bool TryGetFlora(out BlobAssetReference<Punchy.Flora.FloraBlob> blob)
        {
            blob = default;
            if (floraWorld == null || !floraWorld.IsCreated)
            {
                floraWorld = null;
                foreach (var w in World.All)
                {
                    var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.FloraLookup>());
                    if (q.CalculateEntityCount() == 1) { floraWorld = w; floraQuery = q; break; }
                }
                if (floraWorld == null)
                    return false;
            }
            if (!floraQuery.TryGetSingleton<Punchy.Flora.FloraLookup>(out var lookup) || !lookup.Blob.IsCreated)
                return false;
            blob = lookup.Blob;
            return blob.Value.Chimneys.Length > 0;
        }

        /// <summary>Seeded per plot: whether its building's chimneys smoke (multiplied and shifted, not math.hash, so it mixes).</summary>
        static bool Smokes(float2 centre)
        {
            var h = (uint)(int)math.round(centre.x * 4f) * 73856093u ^ (uint)(int)math.round(centre.y * 4f) * 19349663u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f < SmokeShare;
        }

        /// <summary>Adds the smoking chimney plots near the camera to <see cref="nearby"/> and logs the counts.</summary>
        void NearChimneys(in TerrainSource source, float2 cam)
        {
            if (!source.Settlements.IsCreated || !TryGetFlora(out var blob))
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ref var f = ref blob.Value;
            ref var plots = ref source.Settlements.Value.Plots;
            var range = Punchy.Settings.GraphicsSettings.DetailRange(ChimneyRange);
            int total = 0, smoking = 0;
            for (int k = 0; k < plots.Length; k++)
            {
                ref var plot = ref plots[k];
                if (plot.Building < 0 || plot.Building >= f.SettlementSpecies.Length || math.distancesq(plot.Centre, cam) > range * range)
                    continue;
                var building = f.SettlementSpecies[plot.Building];
                if (building.x < 0 || f.Models[f.Rules[building.x].FirstModel].ChimneyCount == 0)
                    continue;
                total++;
                if (!Smokes(plot.Centre))
                    continue;
                smoking++;
                nearby.Add(-(ChimneyBase + k));
            }
            LastChimneyLog = $"smoking {smoking}/{total} chimney buildings within {range:F0} m, particles {smoking * SmokeParticles}, gather {watch.Elapsed.TotalMilliseconds:F2} ms";
        }

        /// <summary>A thin plume of grey smoke from each chimney on a plot, drifting downwind (the plume shader's drift).</summary>
        void ChimneySmoke(in TerrainSource source, int plotIndex, ref float3 min, ref float3 max)
        {
            if (!TryGetFlora(out var blob))
                return;
            var tops = new FixedList128Bytes<float3>();
            Punchy.Flora.FloraPlacement.PlotChimneys(ref blob.Value, source, plotIndex, ref tops);
            for (int k = 0; k < tops.Length; k++)
            {
                plumes.Add(new Emitter
                {
                    Position = tops[k], Seed = math.frac(plotIndex * 0.618f + k * 0.37f), Color = new float4(0.5f, 0.5f, 0.52f, 0f),
                    Shape = new float4(9f, 0.5f, 1.3f, 7f), Timing = new float4(0f, 0.25f, 0.32f, SmokeParticles)
                });
                min = math.min(min, tops[k] - 6f);
                max = math.max(max, tops[k] + new float3(6f, 12f, 6f));
            }
            maxParticles = math.max(maxParticles, SmokeParticles);
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
