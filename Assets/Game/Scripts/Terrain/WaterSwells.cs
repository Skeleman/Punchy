using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Sea swell (roadmap 3.4): the game-side twin of WaterSwell.hlsl, so a swimmer rides the swell the sea is drawn
    /// with, and the camera knows when a crest has closed over it. Height only: the drawn surface also slides a little
    /// along each wave, which this leaves out (a swimmer's bob is the part that shows).
    ///
    /// The swell runs towards the nearest shore (playtest 11-08 item 2: the sea breeze pushes it in), read from a
    /// coarse shore field built once per terrain from <see cref="TerrainSource.SampleHeight"/> (seed-derived, so the
    /// same on every machine). Each point sums two sets of waves turned to the two nearest of eight headings round
    /// its onshore heading, cross-faded, so a set's headings never turn (a heading that turned across the sea would
    /// warp the waves far out); the swell noise only curves their crests gently. Six waves of unrelated lengths and
    /// headings per set, and the noise's blotchy rise and fall of their height and shift of their phases (R-17), keep
    /// the sea from showing a repeating pattern, even from high up (item 1); over the shelf they grow and
    /// steepen (shoaling), so they crest and break before the beach (item 3).
    ///
    /// This class is the one source of the wave table and every number of the swell's shape: <see cref="Publish"/>
    /// uploads them as shader globals, so the drawn sea and the swimmer can't drift apart (refactor 3.4, audit
    /// section 2). WaterSwell.hlsl keeps only the maths, which must stay the same as <see cref="Height"/>.
    /// The state (on, sea level, wind, clock) is set once a frame on the main thread (Underwater.cs) and read from Burst.
    /// What floats rides it too (playtest 11-08 item 20): <see cref="SwellAt"/> here and PunchySwellFloat in the shader
    /// (swimming and floating creatures in CreatureRenderSystem, floating flora and kit in Flora.hlsl). Limits: the wind
    /// is the camera's, so in multiplayer each player's swell height follows their own wind (everything that rides it is
    /// drawn from the same machine's numbers, so it agrees on each screen).
    /// The weather's wind drives it too (playtest 11-08 item 32: a sandstorm's wind only moved the ripples): its strength
    /// sets the height and, from a strong wind up, its direction turns the swell's heading from onshore towards
    /// downwind. Both follow the weather slowly (<see cref="WindFollow"/>), so a change of weather never makes it jump.
    /// </summary>
    public static class WaterSwells
    {
        /// <summary>-noswells on the command line: a flat sea, as before, to compare.</summary>
        public static readonly bool Enabled = !System.Environment.CommandLine.Contains("-noswells");

        /// <summary>The shore field: onshore headings (unit xz) on a square grid, read bilinearly.</summary>
        struct ShoreField
        {
            public UnsafeList<float2> Headings;
            public float2 Origin;
            public float CellsPerMetre;
            /// <summary>Cells across; 0 until the field is built (the swell then runs towards the world's middle).</summary>
            public int Size;
        }

        /// <summary>R-17: the swell noise as Burst reads it (texels as the shader's texture, taps, drift and weights as
        /// the shader's globals of those names).</summary>
        struct NoiseField
        {
            public UnsafeList<float4> Texels;
            public float4 Tap0, Tap1, Drift, Weight0, Weight1;
            /// <summary>Texels across; 0 until the noise is built (no noise: the shader's taps say the same).</summary>
            public int Size;
        }

        /// <summary>What Burst code reads, kept apart: Burst can't run this class's managed static setup.</summary>
        static class Shared
        {
            struct StateKey { }
            struct TableKey { }
            struct ShapeKey { }
            struct ShoreKey { }
            struct WindKey { }
            struct NoiseKey { }
            /// <summary>x 1 on / 0 off, y the sea level, z the wind 0..1, w the shared clock (s).</summary>
            public static readonly SharedStatic<float4> State = SharedStatic<float4>.GetOrCreate<StateKey>();
            /// <summary>The wave table, as in Table.</summary>
            public static readonly SharedStatic<FixedList4096Bytes<float4>> Table = SharedStatic<FixedList4096Bytes<float4>>.GetOrCreate<TableKey>();
            /// <summary>Shape, Mix, Depths, Limits: as the shader's globals of those names.</summary>
            public static readonly SharedStatic<float4x4> Shape = SharedStatic<float4x4>.GetOrCreate<ShapeKey>();
            public static readonly SharedStatic<ShoreField> Shore = SharedStatic<ShoreField>.GetOrCreate<ShoreKey>();
            /// <summary>xy the way the wind blows (unit xz, followed slowly), z how far it turns the swell from onshore.</summary>
            public static readonly SharedStatic<float4> Wind = SharedStatic<float4>.GetOrCreate<WindKey>();
            public static readonly SharedStatic<NoiseField> Noise = SharedStatic<NoiseField>.GetOrCreate<NoiseKey>();
        }

        static readonly int SwellId = Shader.PropertyToID("_PunchySwell");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int WindDirectionId = Shader.PropertyToID("_PunchyWindDirection");
        static readonly int SwellWindId = Shader.PropertyToID("_PunchySwellWind");
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly int WavesId = Shader.PropertyToID("_PunchySwellWaves");
        static readonly int ShapeId = Shader.PropertyToID("_PunchySwellShape");
        static readonly int MixId = Shader.PropertyToID("_PunchySwellMix");
        static readonly int DepthsId = Shader.PropertyToID("_PunchySwellDepths");
        static readonly int LimitsId = Shader.PropertyToID("_PunchySwellLimits");
        static readonly int ShoreId = Shader.PropertyToID("_PunchySwellShore");
        static readonly int ShoreAtId = Shader.PropertyToID("_PunchySwellShoreAt");
        static readonly int NoiseId = Shader.PropertyToID("_PunchySwellNoise");
        static readonly int NoiseTapsId = Shader.PropertyToID("_PunchySwellNoiseTaps");
        static readonly int NoiseDriftId = Shader.PropertyToID("_PunchySwellNoiseDrift");
        static readonly int NoiseWeightsId = Shader.PropertyToID("_PunchySwellNoiseWeights");

        /// <summary>The shader's room for waves (PunchySwellMaxWaves in WaterSwell.hlsl: the global array's size only).
        /// Unity fixes a global array's length the first time it's set, so the upload is always this long.</summary>
        public const int MaxWaves = 12;

        /// <summary>
        /// The waves of one set, longest first (the shader's detail policy drops the short ones first, far off):
        /// wavelength (m), heading (radians, from the set's own onshore heading), relative height, phase. The lengths
        /// step by uneven ratios (1.46..1.53) so no two waves line up again over any short distance.
        /// </summary>
        static readonly float4[] Table =
        {
            new float4(41f, 0.00f, 1.00f, 0.0f),
            new float4(27.3f, -0.38f, 0.55f, 1.7f),
            new float4(17.9f, 0.31f, 0.34f, 4.1f),
            new float4(12.1f, -0.64f, 0.21f, 2.6f),
            new float4(8.3f, 0.57f, 0.13f, 5.3f),
            new float4(5.7f, -0.17f, 0.08f, 0.9f),
        };

        /// <summary>The swell's height (m) in a calm, and what a full wind adds, before each wave's relative height.</summary>
        const float CalmHeight = 0.18f, WindHeight = 0.9f;
        /// <summary>How far each wave carries a point along it, as a fraction of its height (Gerstner steepness).</summary>
        const float Along = 0.55f;
        /// <summary>Headings the sets are turned to (each set's waves keep straight; a point blends the two nearest).</summary>
        const float Sectors = 8f;
        /// <summary>How much the swell noise raises and lowers the height (1 +- this at most: big sets roll through
        /// between quieter spells, in blotches that never repeat).</summary>
        const float GroupDepth = 0.35f;
        /// <summary>Over the shelf (shoaling): how much higher and how much steeper the waves get at full shoaling.</summary>
        const float ShoalHeight = 0.25f, ShoalSteep = 0.6f;
        /// <summary>The swell fades in between these depths (m: none at the beach) ...</summary>
        const float FadeFrom = 0.1f, FadeTo = 2.5f;
        /// <summary>... and shoals fully at ShoalFull, not at all from ShoalNone deep.</summary>
        const float ShoalFull = 2.5f, ShoalNone = 8f;
        /// <summary>The swell's height is held under this share of the water's depth, so a trough never bares the bed
        /// (over the shelf, shoaled troughs dipped below sand bars and showed ragged patches of bed).</summary>
        const float DepthShare = 0.3f;
        /// <summary>Item 19 (waves too round): the shortest wave carries points along it this much more than the longest
        /// (the rest in between), so the short waves' crests peak and the troughs flatten.</summary>
        const float Chop = 0.7f;
        /// <summary>Item 19 (patterns from far off): each wave's phase is shifted across the sea by the swell noise's warp
        /// channel (R-17; it was two long sines), this strong times the wave's place in the table, so the sets'
        /// interference never repeats within sight. Gentle enough (under 4% of any wave's own wavenumber) not to bend it.</summary>
        const float WarpGain = 1.1f;
        /// <summary>Item 32: how long (s) the swell's wind takes to follow the weather's most of the way (1 - 1/e).</summary>
        const float WindFollow = 12f;
        /// <summary>The wind turns the swell towards downwind from WindSteerFrom, fully (by WindSteer) at WindSteerTo
        /// (0..1; a sandstorm or storm blows about 0.8-1).</summary>
        const float WindSteerFrom = 0.4f, WindSteerTo = 0.85f, WindSteer = 0.85f;
        /// <summary>Extra room (m) round the furthest the swell can reach, for the render bounds.</summary>
        const float BoundsMargin = 0.5f;

        /// <summary>
        /// R-17 (#280, from high up the sea's displacement tiled): the swell noise, a tileable texture baked once from
        /// the seed. Texels across (PunchySwellNoiseSize in WaterSwell.hlsl), and the value-noise lattice cells across it
        /// in its first layer (the second has twice as many); per channel, how much of the second, finer layer it has
        /// (r the set height is blotchy, g and b, the warp and the bend, stay smooth so they never bend a wave too hard).
        /// </summary>
        const int NoiseSize = 256, NoiseCells = 16;
        static readonly float4 NoiseFine = new float4(0.35f, 0f, 0f, 0.35f);
        /// <summary>The two taps: metres one lattice cell spans (ratio not a simple fraction), how each is turned
        /// (radians), and how each drifts (m/s, and its heading in radians), so the noise never sits still or lines up.
        /// The broad tap's cell is 2 km, not 1.5: at 1.5 km the warp's steepest slope came to 5% of a wave's wavenumber.
        /// </summary>
        static readonly float2 NoiseCell = new float2(2000f, 550f), NoiseTurn = new float2(0.61f, 2.17f);
        static readonly float4 NoiseDrift = new float4(0.6f, 0.3f, 3f, -2.4f);
        /// <summary>Each tap's share per channel (r set height, g warp, b bend; normalised so each channel keeps a spread
        /// of 1): the sets come mostly from the finer tap, the warp only from the broad one.</summary>
        static readonly float4 NoiseShare0 = new float4(0.35f, 1f, 0.9f, 0f), NoiseShare1 = new float4(0.95f, 0f, 0.45f, 0f);
        /// <summary>How hard the noise raises and lowers the sets (times GroupDepth, clamped to +-1: the same range and
        /// spread as the two wave-group sines it replaced), and how far (m) it moves each wave's crest along it, which turns
        /// the crests up to about +-6 degrees where it changes fastest.</summary>
        const float NoiseSets = 0.5f, NoiseBend = 13f;

        /// <summary>The shore field: cells across, and how far past the world's edge it reaches (m).</summary>
        const int ShoreCells = 160;
        const float ShoreBeyond = 450f;

        /// <summary>
        /// The furthest the swell can move a point of the sea's surface (m: x and z sideways, y up or down), with a
        /// margin, worked out from the wave table and shape (refactor 3.4, audit section 2: the bounds were the flat
        /// mesh's, so a heaving chunk at a frustum edge could be culled while still in view). Every wave of both sets
        /// at its crest at once, in the tallest group, fully shoaled: never reached, but never exceeded.
        /// TerrainStreamingSystem pads every water chunk's and region's render bounds by it.
        /// </summary>
        public static readonly float3 BoundsPad = ComputeBoundsPad();

        static float3 ComputeBoundsPad()
        {
            var total = 0f;
            foreach (var w in Table)
                total += w.z;
            // (Two sets cross-faded and renormalised add up to at most sqrt 2 of one.)
            var height = (CalmHeight + WindHeight) * total * math.SQRT2 * (1f + GroupDepth) * (1f + ShoalHeight);
            var along = Along * (1f + ShoalSteep) * (1f + Chop) * height;
            return new float3(along, height, along) + BoundsMargin;
        }

        /// <summary>Sets this frame's state for the shader and for Burst code (main thread, once a frame), and builds
        /// the shore field for this terrain when it's new.</summary>
        public static void Publish(in TerrainSource terrain)
        {
            var clock = Shader.GetGlobalFloat(ClockId);
            FollowWind();
            var state = new float4(Enabled ? 1f : 0f, terrain.World.SeaLevel, wind, clock > 0f ? clock : Time.time);
            Shared.State.Data = state;
            Shader.SetGlobalVector(SwellId, new Vector4(state.x, state.y, state.z, 0f));
            Shader.SetGlobalVector(SwellWindId, Shared.Wind.Data);
            if (!built)
                Build();
            // (Every frame, cheap: globals are easily lost in the editor.)
            Shader.SetGlobalVectorArray(WavesId, upload);
            var shape = Shared.Shape.Data;
            Shader.SetGlobalVector(ShapeId, shape.c0);
            Shader.SetGlobalVector(MixId, shape.c1);
            Shader.SetGlobalVector(DepthsId, shape.c2);
            Shader.SetGlobalVector(LimitsId, shape.c3);
            UpdateShore(terrain);
            UpdateNoise(terrain);
            noiseTaps[0].w = noiseTaps[1].w = noiseReady ? 1f : 0f;
            Shader.SetGlobalVectorArray(NoiseTapsId, noiseTaps);
            Shader.SetGlobalVector(NoiseDriftId, Shared.Noise.Data.Drift);
            Shader.SetGlobalVectorArray(NoiseWeightsId, noiseWeights);
            if (noiseTexture != null)
                Shader.SetGlobalTexture(NoiseId, noiseTexture);
            Shader.SetGlobalVector(ShoreAtId, shoreReady ? new Vector4(shore.Origin.x, shore.Origin.y, shore.CellsPerMetre, shore.Size) : Vector4.zero);
            if (shoreTexture != null)
                Shader.SetGlobalTexture(ShoreId, shoreTexture);
        }

        static bool windSet;
        static float wind;
        static float2 windDirection = new float2(1f, 0f);

        // Play mode may start without a domain reload: start each run from the weather's wind as it is.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWind() => windSet = false;

        /// <summary>The swell's wind, following the weather's (strength and direction) over WindFollow seconds; the first
        /// frame takes it as it is.</summary>
        static void FollowWind()
        {
            var target = Mathf.Clamp01(Shader.GetGlobalFloat(WindId));
            var d = (Vector4)Shader.GetGlobalVector(WindDirectionId);
            var towards = new float2(d.x, d.y);
            towards = math.lengthsq(towards) > 1e-6f ? math.normalize(towards) : windDirection;
            var follow = windSet ? 1f - math.exp(-Time.deltaTime / WindFollow) : 1f;
            windSet = true;
            wind = math.lerp(wind, target, follow);
            // (Turned by blending the vectors: through a half turn it shrinks and the normalise picks up the new side.)
            windDirection = math.normalizesafe(math.lerp(windDirection, towards, follow), windDirection);
            Shared.Wind.Data = new float4(windDirection, WindSteer * math.smoothstep(WindSteerFrom, WindSteerTo, wind), 0f);
        }

        static bool built;
        static readonly Vector4[] upload = new Vector4[MaxWaves];

        /// <summary>The wave table and shape for Burst and for the shader upload (once: they never change in a run).
        /// Padding entries have no height, so the shader can loop over all of them.</summary>
        static void Build()
        {
            built = true;
            Debug.Assert(Table.Length <= MaxWaves, "WaterSwells: more waves than the shader has room for");
            var list = new FixedList4096Bytes<float4>();
            for (int i = 0; i < MaxWaves; i++)
            {
                var w = i < Table.Length ? Table[i] : new float4(10f, 0f, 0f, 0f);
                if (i < Table.Length)
                    list.Add(w);
                upload[i] = new Vector4(w.x, w.y, w.z, w.w);
            }
            Shared.Table.Data = list;
            Shared.Shape.Data = new float4x4(
                new float4(CalmHeight, WindHeight, Along, Table.Length),
                new float4(Sectors, GroupDepth, ShoalHeight, ShoalSteep),
                new float4(FadeFrom, FadeTo, ShoalFull, ShoalNone),
                new float4(DepthShare, Chop, WarpGain, 0f));
            BuildNoiseShape();
        }

        // ---------------------------------------------------------------- the swell noise (R-17)

        static readonly Vector4[] noiseTaps = new Vector4[2];
        static readonly Vector4[] noiseWeights = new Vector4[2];
        static bool noiseReady, noiseBuilding;
        static uint noiseSeed, noiseBuildingSeed;
        static JobHandle noiseJob;
        static NativeArray<float4> noiseTexels;
        static Texture2D noiseTexture;

        /// <summary>The taps, drift and weights (once: they never change; only the texels follow the seed).</summary>
        static void BuildNoiseShape()
        {
            var field = Shared.Noise.Data;
            var drift = new float4();
            for (int i = 0; i < 2; i++)
            {
                math.sincos(NoiseTurn[i], out var sn, out var cs);
                var perMetre = NoiseCells / NoiseCell[i];
                var tap = new float4(cs, sn, perMetre, 0f);
                // (A feature moving at v across the sea sits still at turn * (xz - v t): its texels drift by -turn * v.)
                math.sincos(NoiseDrift[i * 2 + 1], out var hs, out var hc);
                var v = NoiseDrift[i * 2] * new float2(hc, hs);
                var d = -new float2(cs * v.x - sn * v.y, sn * v.x + cs * v.y) * perMetre;
                if (i == 0) { field.Tap0 = tap; drift.xy = d; } else { field.Tap1 = tap; drift.zw = d; }
                noiseTaps[i] = tap;
            }
            field.Drift = drift;
            var norm = math.rsqrt(math.max(NoiseShare0 * NoiseShare0 + NoiseShare1 * NoiseShare1, 1e-6f));
            var gain = new float4(NoiseSets, 1f, NoiseBend, 0f);
            field.Weight0 = NoiseShare0 * norm * gain;
            field.Weight1 = NoiseShare1 * norm * gain;
            noiseWeights[0] = field.Weight0;
            noiseWeights[1] = field.Weight1;
            Shared.Noise.Data = field;
        }

        /// <summary>
        /// Starts baking the noise when the seed is new and takes it up once baked (a frame or more later: a background
        /// job, never waited on in the frame it starts). Until then there is no noise (flat sets, no warp or bend), on
        /// the CPU and in the shader alike.
        /// </summary>
        static void UpdateNoise(in TerrainSource terrain)
        {
            var seed = terrain.World.Seed;
            if (noiseBuilding)
            {
                if (!noiseJob.IsCompleted)
                    return;
                noiseJob.Complete();
                if (seed == noiseBuildingSeed)
                    TakeNoise();
                DropNoiseJob();
                return;
            }
            if (noiseReady && seed == noiseSeed)
                return;
            noiseTexels = new NativeArray<float4>(NoiseSize * NoiseSize, Allocator.Persistent);
            noiseJob = new NoiseJob { Seed = seed, Fine = NoiseFine, Texels = noiseTexels }.Schedule();
            noiseBuilding = true;
            noiseBuildingSeed = seed;
            Application.quitting -= DropNoiseJob;
            Application.quitting += DropNoiseJob;
        }

        /// <summary>Copies the baked noise into the Burst copy and the shader's texture.</summary>
        static void TakeNoise()
        {
            var field = Shared.Noise.Data;
            // (Made once and overwritten in place, never freed while a swimmer's job may read it.)
            if (!field.Texels.IsCreated)
                field.Texels = new UnsafeList<float4>(NoiseSize * NoiseSize, Allocator.Persistent);
            field.Texels.Resize(NoiseSize * NoiseSize);
            for (int i = 0; i < NoiseSize * NoiseSize; i++)
                field.Texels[i] = noiseTexels[i];
            field.Size = NoiseSize;
            Shared.Noise.Data = field;
            if (noiseTexture == null)
                noiseTexture = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBAFloat, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "SwellNoise", hideFlags = HideFlags.DontSave };
            noiseTexture.SetPixelData(noiseTexels, 0);
            noiseTexture.Apply(false, false);
            noiseReady = true;
            noiseSeed = noiseBuildingSeed;
        }

        /// <summary>Waits for a bake still in flight and frees its array (taken, stale or quitting).</summary>
        static void DropNoiseJob()
        {
            Application.quitting -= DropNoiseJob;
            if (!noiseBuilding)
                return;
            noiseJob.Complete();
            noiseTexels.Dispose();
            noiseBuilding = false;
        }

        /// <summary>
        /// Bakes the swell noise from the seed: per channel, tileable value noise (quintic fade) on NoiseCells lattice
        /// cells, plus some of a layer on twice as many, then each channel shifted and scaled to a mean of 0 and a
        /// spread of 1. A seed-only hash (no Unity random), so every machine bakes the same texels.
        /// </summary>
        [BurstCompile]
        struct NoiseJob : IJob
        {
            public uint Seed;
            public float4 Fine;
            public NativeArray<float4> Texels;

            public void Execute()
            {
                var n = NoiseSize;
                var sum = new double4();
                var sumSq = new double4();
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var v = new float4();
                        for (int c = 0; c < 4; c++)
                            v[c] = (1f - Fine[c]) * Value(x, y, NoiseCells, c * 2) + Fine[c] * Value(x, y, NoiseCells * 2, c * 2 + 1);
                        Texels[y * n + x] = v;
                        var dv = (double4)v;
                        sum += dv;
                        sumSq += dv * dv;
                    }
                var count = (double)(n * n);
                var mean = sum / count;
                var spread = math.sqrt(math.max(sumSq / count - mean * mean, 1e-12));
                var m = (float4)mean;
                var inv = (float4)(1.0 / spread);
                for (int i = 0; i < n * n; i++)
                    Texels[i] = (Texels[i] - m) * inv;
            }

            /// <summary>Value noise at texel x, y on a lattice of the given cells across (wrapping), -1..1.</summary>
            float Value(int x, int y, int cells, int layer)
            {
                var p = new float2(x, y) * cells / NoiseSize;
                var i0 = (int2)math.floor(p);
                var f = p - i0;
                f = f * f * f * (f * (f * 6f - 15f) + 10f);
                var i1 = (i0 + 1) % cells;
                return math.lerp(math.lerp(Corner(i0.x, i0.y, cells, layer), Corner(i1.x, i0.y, cells, layer), f.x),
                                 math.lerp(Corner(i0.x, i1.y, cells, layer), Corner(i1.x, i1.y, cells, layer), f.x), f.y);
            }

            float Corner(int x, int y, int cells, int layer)
            {
                var h = Mix(Seed * 0x9E3779B9u ^ Mix((uint)(x + y * cells) ^ ((uint)layer << 20)));
                return (h >> 8) * (2f / 16777215f) - 1f;
            }

            /// <summary>A full-avalanche integer mix (lowbias32): math.hash alone doesn't scatter neighbouring inputs.</summary>
            static uint Mix(uint x)
            {
                x ^= x >> 16;
                x *= 0x7feb352du;
                x ^= x >> 15;
                x *= 0x846ca68bu;
                x ^= x >> 16;
                return x;
            }
        }

        /// <summary>The swell noise at xz now (r set height, g phase warp, b crest bend in m; 0 until baked). As
        /// PunchySwellNoise; for probes and the swell's maths.</summary>
        public static float4 Noise(float2 xz) => Noise(xz, Shared.State.Data.w);

        static float4 Noise(float2 xz, float t)
        {
            var f = Shared.Noise.Data;
            if (f.Size < 1)
                return 0f;
            return NoiseTap(f, f.Tap0, f.Drift.xy, xz, t) * f.Weight0 + NoiseTap(f, f.Tap1, f.Drift.zw, xz, t) * f.Weight1;
        }

        /// <summary>One tap of the noise, as PunchySwellNoiseTap.</summary>
        static float4 NoiseTap(in NoiseField f, float4 tap, float2 drift, float2 xz, float t)
        {
            var u = new float2(tap.x * xz.x - tap.y * xz.y, tap.y * xz.x + tap.x * xz.y) * tap.z + drift * t;
            var i0 = math.floor(u);
            var r = u - i0;
            var a = (int2)i0 & (NoiseSize - 1);
            var b = (a + 1) & (NoiseSize - 1);
            var h = f.Texels;
            return math.lerp(math.lerp(h[a.y * NoiseSize + a.x], h[a.y * NoiseSize + b.x], r.x),
                             math.lerp(h[b.y * NoiseSize + a.x], h[b.y * NoiseSize + b.x], r.x), r.y);
        }

        // ---------------------------------------------------------------- the shore field

        static ShoreField shore;
        static bool shoreReady, shoreBuilding;
        static TerrainSource shoreFor, shoreBuildingFor;
        static JobHandle shoreJob;
        static NativeArray<float> shoreLand;
        static NativeArray<float2> shoreHeadings;
        static Texture2D shoreTexture;

        /// <summary>
        /// Starts building the shore field when the terrain is new, and takes it up once built (a frame or more later:
        /// a background job of ~26k height samples, never waited on in the frame it starts). Until then the swell runs
        /// towards the world's middle.
        /// </summary>
        static void UpdateShore(in TerrainSource terrain)
        {
            if (shoreBuilding && !terrain.Equals(shoreBuildingFor))
                DropShoreJob();
            if (shoreBuilding)
            {
                if (!shoreJob.IsCompleted)
                    return;
                shoreJob.Complete();
                TakeShore();
                DropShoreJob();
                return;
            }
            if (shoreReady && terrain.Equals(shoreFor) || !terrain.Biomes.IsCreated)
                return;
            var size = ShoreCells;
            var half = terrain.World.WorldSize * 0.5f + ShoreBeyond;
            var cell = 2f * half / size;
            shoreLand = new NativeArray<float>(size * size, Allocator.Persistent);
            shoreHeadings = new NativeArray<float2>(size * size, Allocator.Persistent);
            var land = new LandJob { Source = terrain, Origin = -half, Cell = cell, Size = size, Land = shoreLand }.Schedule(size * size, 64);
            shoreJob = new HeadingJob { Land = shoreLand, Size = size, Headings = shoreHeadings }.Schedule(land);
            shoreBuilding = true;
            shoreBuildingFor = terrain;
            shore.Origin = -half;
            shore.CellsPerMetre = 1f / cell;
            // The job reads the TerrainSource blobs, which quitting, leaving play mode or a world swap frees: finish it
            // first, while they are intact (as DunesColossus does).
            Application.quitting -= DropShoreJob;
            Application.quitting += DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing -= DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing += DropShoreJob;
        }

        /// <summary>Copies a finished field into the Burst copy and the shader's texture.</summary>
        static void TakeShore()
        {
            var size = ShoreCells;
            var field = Shared.Shore.Data;
            // (The Burst copy is made once and overwritten in place, never freed while a swimmer's job may read it.)
            if (!field.Headings.IsCreated)
                field.Headings = new UnsafeList<float2>(size * size, Allocator.Persistent);
            field.Headings.Resize(size * size);
            for (int i = 0; i < size * size; i++)
                field.Headings[i] = shoreHeadings[i];
            field.Origin = shore.Origin;
            field.CellsPerMetre = shore.CellsPerMetre;
            field.Size = size;
            Shared.Shore.Data = field;
            shore = field;
            if (shoreTexture == null)
                shoreTexture = new Texture2D(size, size, TextureFormat.RGFloat, false, true)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SwellShore", hideFlags = HideFlags.DontSave };
            shoreTexture.SetPixelData(shoreHeadings, 0);
            shoreTexture.Apply(false, false);
            shoreReady = true;
            shoreFor = shoreBuildingFor;
            Debug.Log($"WaterSwells: shore field built ({size}x{size}, {1f / shore.CellsPerMetre:F0} m cells)");
        }

        /// <summary>Waits for a field still in flight and frees its arrays (taken, stale, quitting or a world swap).</summary>
        static void DropShoreJob()
        {
            Application.quitting -= DropShoreJob;
            Punchy.Net.GameBootstrap.WorldsReplacing -= DropShoreJob;
            if (!shoreBuilding)
                return;
            shoreJob.Complete();
            shoreLand.Dispose();
            shoreHeadings.Dispose();
            shoreBuilding = false;
        }

        /// <summary>How much of each cell is land (0 sea, 1 land, eased over 2 m round the sea level).</summary>
        [BurstCompile]
        struct LandJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Cell;
            public int Size;
            [WriteOnly] public NativeArray<float> Land;

            public void Execute(int i)
            {
                var xz = new float2(Origin + (i % Size + 0.5f) * Cell, Origin + (i / Size + 0.5f) * Cell);
                Land[i] = math.saturate((Source.SampleHeight(xz) - Source.World.SeaLevel) * 0.5f + 0.5f);
            }
        }

        /// <summary>
        /// The onshore heading per cell: up the slope of the land blurred at two scales, the fine one (~3 cells) for the
        /// coast close by, the broad one (~12 cells) so open water far out still faces the nearest land; a whisper of
        /// "towards the middle" settles cells with no land in reach.
        /// </summary>
        [BurstCompile]
        struct HeadingJob : IJob
        {
            [ReadOnly] public NativeArray<float> Land;
            public int Size;
            public NativeArray<float2> Headings;

            public void Execute()
            {
                var fine = new NativeArray<float>(Land, Allocator.Temp);
                var broad = new NativeArray<float>(Land, Allocator.Temp);
                var scratch = new NativeArray<float>(Land.Length, Allocator.Temp);
                for (int pass = 0; pass < 3; pass++)
                {
                    Blur(fine, scratch, 2);
                    Blur(broad, scratch, 8);
                }
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var g = Gradient(fine, x, y) + 2f * Gradient(broad, x, y);
                        var middle = new float2(Size * 0.5f - 0.5f - x, Size * 0.5f - 0.5f - y);
                        g += 1e-5f * math.normalizesafe(middle);
                        Headings[y * Size + x] = math.normalizesafe(g, new float2(1f, 0f));
                    }
            }

            float2 Gradient(NativeArray<float> f, int x, int y)
            {
                int x0 = math.max(x - 1, 0), x1 = math.min(x + 1, Size - 1), y0 = math.max(y - 1, 0), y1 = math.min(y + 1, Size - 1);
                return new float2(f[y * Size + x1] - f[y * Size + x0], f[y1 * Size + x] - f[y0 * Size + x]);
            }

            /// <summary>One box blur of the given radius along x, then along y (edge cells repeat).</summary>
            void Blur(NativeArray<float> f, NativeArray<float> tmp, int radius)
            {
                var n = Size;
                var norm = 1f / (2 * radius + 1);
                for (int y = 0; y < n; y++)
                {
                    var sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += f[y * n + math.clamp(k, 0, n - 1)];
                    for (int x = 0; x < n; x++)
                    {
                        tmp[y * n + x] = sum * norm;
                        sum += f[y * n + math.min(x + radius + 1, n - 1)] - f[y * n + math.max(x - radius, 0)];
                    }
                }
                for (int x = 0; x < n; x++)
                {
                    var sum = 0f;
                    for (int k = -radius; k <= radius; k++)
                        sum += tmp[math.clamp(k, 0, n - 1) * n + x];
                    for (int y = 0; y < n; y++)
                    {
                        f[y * n + x] = sum * norm;
                        sum += tmp[math.min(y + radius + 1, n - 1) * n + x] - tmp[math.max(y - radius, 0) * n + x];
                    }
                }
            }
        }

        /// <summary>The way the swell runs at xz (not normalised): towards the nearest shore. As PunchySwellOnshore.</summary>
        /// <summary>The way the swell runs at xz: onshore, turned towards downwind by a strong wind (item 32). As
        /// PunchySwellHeading.</summary>
        static float2 Heading(float2 xz)
        {
            var w = Shared.Wind.Data;
            return math.lerp(Onshore(xz), w.xy, w.z);
        }

        static float2 Onshore(float2 xz)
        {
            var f = Shared.Shore.Data;
            if (f.Size < 1)
                return -xz / math.max(math.length(xz), 1f);
            var u = (xz - f.Origin) * f.CellsPerMetre - 0.5f;
            var i0 = math.floor(u);
            var t = u - i0;
            var n = f.Size - 1;
            var a = math.clamp((int2)i0, 0, n);
            var b = math.clamp((int2)i0 + 1, 0, n);
            var h = f.Headings;
            return math.lerp(math.lerp(h[a.y * f.Size + a.x], h[a.y * f.Size + b.x], t.x),
                             math.lerp(h[b.y * f.Size + a.x], h[b.y * f.Size + b.x], t.x), t.y);
        }

        /// <summary>How far the swell lifts the water surface at xz (m; 0 off the sea, out of the water or with -noswells),
        /// from the ground there: the one helper for anything that floats or swims at the surface (item 20).</summary>
        public static float SwellAt(in TerrainSource terrain, float2 xz)
        {
            if (Shared.State.Data.x <= 0f)
                return 0f;
            var g = terrain.SampleGround(xz);
            return g.HasWater ? Height(xz, g.WaterSurface, g.Height) : 0f;
        }

        /// <summary>
        /// How far the swell lifts the water surface at xz (m; 0 off the sea or with -noswells), for water whose flat
        /// surface is <paramref name="surface"/> over a bed at <paramref name="bed"/>. Same maths as PunchySwell, every
        /// wave counted (the shader drops the short ones only far from the camera).
        /// </summary>
        public static float Height(float2 xz, float surface, float bed)
        {
            var s = Shared.State.Data;
            if (s.x <= 0f || math.abs(surface - s.y) > 0.05f)
                return 0f;
            var shape = Shared.Shape.Data;
            var depth = surface - bed;
            var amount = math.smoothstep(shape.c2.x, shape.c2.y, depth);
            if (amount <= 0f)
                return 0f;
            var shoal = 1f - math.smoothstep(shape.c2.z, shape.c2.w, depth);
            // (R-17: the noise's x raises and lowers the sets, y warps the phases, z moves each crest along its wave.)
            var noise = Noise(xz, s.w);
            var height = (shape.c0.x + shape.c0.y * math.saturate(s.z)) * amount * (1f + shape.c1.z * shoal)
                         * (1f + shape.c1.y * math.clamp(noise.x, -1f, 1f));
            height = math.min(height, shape.c3.x * depth);
            var on = Heading(xz);
            var sectors = shape.c1.x;
            var f = math.atan2(on.y, on.x) * sectors / (2f * math.PI);
            var s0 = math.floor(f);
            var blend = math.smoothstep(0f, 1f, f - s0);
            var norm = math.rsqrt(blend * blend + (1f - blend) * (1f - blend));
            var table = Shared.Table.Data;
            var warp = noise.y * shape.c3.z;
            var y = 0f;
            for (int set = 0; set < 2; set++)
            {
                var weight = (set == 0 ? 1f - blend : blend) * norm;
                if (weight < 1e-3f)
                    continue;
                var sector = s0 + set;
                sector -= sectors * math.floor(sector / sectors);
                var heading = sector * 2f * math.PI / sectors;
                for (int i = 0; i < table.Length; i++)
                {
                    var w = table[i];
                    var k = 2f * math.PI / w.x;
                    var dir = new float2(math.cos(heading + w.y), math.sin(heading + w.y));
                    var theta = k * (math.dot(dir, xz) + noise.z) - math.sqrt(9.81f * k) * s.w + w.w + sector * 2.39996f * (i + 1) + warp * (i + 1);
                    y += height * w.z * weight * math.sin(theta);
                }
            }
            return y;
        }
    }
}
