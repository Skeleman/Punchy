using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Rendering.Effects
{
    /// <summary>
    /// R-29 row 2 (#320, rule 30): electric arcs anyone can draw for a frame, on the FireSystem model. Callers add each
    /// arc every frame (its two ends, a stable seed, a colour, a width) and the system draws them all in one procedural
    /// draw with the Electricity shader, then clears the list. The shader builds every path from the shared clock and the
    /// seed: the spine re-jags on irregular gaps (ElectricityEffect.RejagRate, never a fixed beat), the old path glows on
    /// for a moment, forks come and go and most of them miss, and the bolt drops out now and then. Every player sees the
    /// same bolt with nothing sent. Here: the light (LightBudget, rule 26), the crack (GameAudio, or the Struck hook),
    /// and the fade by screen size (DetailPolicy, rule 20).
    /// Two ways to add one: AddArc for a steady arc (the golem's links) and AddStrike for one that fires now and then on
    /// its own rhythm (spire to spire, spire to ground), which says whether it is firing so the caller can skip work.
    /// Callers: FloraSystem.DrawBolts (spire strikes) and GolemArcs (the golems' links).
    /// </summary>
    public static class ElectricitySystem
    {
        /// <summary>Colours (indices into the standard electricity's colours).</summary>
        public const int Blue = 0, Violet = 1, Green = 2, Gold = 3;

        /// <summary>An arc's rhythm when it fires now and then: on average every Period s, for Duration s.</summary>
        public struct Rhythm
        {
            public float Period, Duration;
            public Rhythm(float period, float duration) { Period = math.max(period, 0.05f); Duration = math.max(duration, 0.02f); }
        }

        /// <summary>Hook for anything that wants to know a strike landed (a sound bank, a creature flinching): where, and
        /// the arc's seed. Raised on the main thread inside Draw.</summary>
        public static event Action<Vector3, int> Struck;

        /// <summary>Arcs dropped because the cap was full, since the start.</summary>
        public static int Dropped;

        /// <summary>The standard electricity (Resources/StandardElectricity), loaded on first use.</summary>
        public static ElectricityEffect Effect => effect != null ? effect : effect = Resources.Load<ElectricityEffect>("StandardElectricity");

        /// <summary>Below this many reference pixels (its glow band's thickness, DetailPolicy) an arc fades out; gone at
        /// half of it. A spire's 0.6 m band goes at about 190 m at the reference view and Detail 1, near the old 160 m
        /// bolt range. TODO: move to DetailPolicy beside TorchFlamePixels once the value is picked in the editor.</summary>
        const float ArcPixels = 3f, ArcEndShare = 0.5f;

        /// <summary>
        /// How far an arc of this width shows at the current view (rule 20): its glow band's thickness on screen, not its
        /// length, since a long arc far off is still a hairline. Callers that gather arcs (spire bolts) use it as their
        /// radius so nothing is gathered past where it fades.
        /// </summary>
        public static float Reach(float width = 1f)
        {
            var e = Effect;
            var band = 2f * (e != null ? e.CoreWidth * e.GlowWidth : 0.3f) * width;
            return Punchy.Settings.DetailPolicy.ExtraReach(band, ArcPixels);
        }

        /// <summary>The shared clock (WorldSync sets it every frame; the server tick when joined).</summary>
        public static float Clock => Shader.GetGlobalFloat(ClockId);

        /// <summary>A steady arc this frame (it re-jags and flickers by itself). Main thread only; add it every frame.</summary>
        /// <param name="seed">Anything that stays the same while it lives (a colossus link index, a spire pair's hash),
        /// never a position that moves.</param>
        /// <param name="width">1 for a spire's arc; a colossus's arcs are about 7.</param>
        /// <param name="quiet">True for no cracks (a cluster of arcs where one cracking is enough).</param>
        public static void AddArc(Vector3 from, Vector3 to, int seed, int colour = Blue, float width = 1f, bool quiet = false)
            => Push(from, to, seed, colour, width, false, 0, quiet);

        /// <summary>
        /// An arc that fires now and then on <paramref name="rhythm"/>; strike times are jittered by the seed so a field
        /// of them never pulses together. Returns whether it is firing this frame (drawn, lit and cracked by the system).
        /// </summary>
        public static bool AddStrike(Vector3 from, Vector3 to, int seed, Rhythm rhythm, int colour = Blue, float width = 1f)
        {
            // Strike n starts at (n + jitter_n) x Period, jitter in [0, 0.6): gaps from 0.4 to 1.6 Periods, in order.
            var t = Clock / rhythm.Period;
            var n = (long)math.floor(t);
            var start = n + Jitter(seed, n);
            if (t < start)
            {
                n--;
                start = n + Jitter(seed, n);
            }
            if ((t - start) * rhythm.Period >= rhythm.Duration)
                return false;
            Push(from, to, seed, colour, width, true, n);
            return true;
        }

        // ------------------------------------------------------------------------------------------------- internals

        struct Arc
        {
            public Vector3 From, To;
            public float Width;
            public int Seed, Colour;
            public bool Struck, Quiet;
            public long Strike;
        }

        /// <summary>One arc for the Electricity shader (ArcInstance there).</summary>
        struct ArcInstance
        {
            public float4 FromSeed;   // xyz one end (world), w seed as uint bits
            public float4 ToWidth;    // xyz the other end, w width
            public float4 Params;     // x colour, y fade 0..1 (screen size), z jag (m), w strike start (clock s; 0 steady)
            public const int Stride = 48;
        }

        const int MaxArcs = 2048;
        /// <summary>Quads per arc: the spine (16 segments) and two forks (6 each), for the live path and its afterglow.</summary>
        public const int SpineSegments = 16, ForkSegments = 6, QuadsPerPath = SpineSegments + 2 * ForkSegments, QuadsPerArc = 2 * QuadsPerPath;

        static ElectricityEffect effect;
        static readonly List<Arc> arcs = new List<Arc>();
        static readonly ArcInstance[] instanceData = new ArcInstance[MaxArcs];
        static Dictionary<int, long> lastStrike = new Dictionary<int, long>(), lastStrikeNext = new Dictionary<int, long>();
        static GraphicsBuffer instances;
        static MaterialPropertyBlock props;
        static readonly Vector4[] colourTable = new Vector4[ElectricityEffect.MaxColours * 2];
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static readonly int InstancesId = Shader.PropertyToID("_ArcInstances");
        static readonly int ColoursId = Shader.PropertyToID("_PunchyArcColours");
        static readonly int ShapeId = Shader.PropertyToID("_PunchyArcShape");
        static readonly int ForkId = Shader.PropertyToID("_PunchyArcForks");
        static readonly int TimingId = Shader.PropertyToID("_PunchyArcTiming");

        static void Push(Vector3 from, Vector3 to, int seed, int colour, float width, bool struck, long strike = 0, bool quiet = false)
        {
            if (arcs.Count >= MaxArcs)
            {
                Dropped++;
                return;
            }
            arcs.Add(new Arc { From = from, To = to, Seed = seed, Colour = Colour(colour), Width = width, Struck = struck, Strike = strike, Quiet = quiet });
        }

        static int Colour(int colour)
        {
            var e = Effect;
            var count = e != null ? Mathf.Min(e.Colours.Length, ElectricityEffect.MaxColours) : 1;
            return colour >= 0 && colour < count ? colour : 0;
        }

        /// <summary>PCG hash, the same integer steps as PunchyArcHash in Electricity.shader, so the light and sound
        /// here land on the shader's paths.</summary>
        public static uint Hash(uint v)
        {
            var state = v * 747796405u + 2891336453u;
            var word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return (word >> 22) ^ word;
        }

        static float Hash01(uint v) => (Hash(v) & 0xFFFFFF) / 16777216f;
        static float Jitter(int seed, long n) => 0.6f * Hash01((uint)seed ^ Hash((uint)n * 0x9E3779B9u));

        /// <summary>The live path's index for a steady arc at <paramref name="clock"/>: path k starts at
        /// (k + 0.85 x h_k) / rate, so gaps run 0.15 to 1.85 mean gaps. Mirrors the shader's PathAt.</summary>
        static long PathAt(int seed, float clock, float rate)
        {
            var t = clock * rate;
            var k = (long)math.floor(t);
            if (t < k + 0.85f * Hash01((uint)seed ^ (uint)k * 0x632BE5ABu))
                k--;
            return k;
        }

        /// <summary>Draws this frame's arcs and clears the list. Called once a frame by EffectsDriver.</summary>
        public static void Draw(Material material, Vector3 camera)
        {
            var e = Effect;
            if (arcs.Count == 0 || material == null || e == null || e.Colours.Length == 0)
            {
                arcs.Clear();
                lastStrike.Clear();
                return;
            }
            Upload(e);
            Buffers();
            var clock = Clock;
            lastStrikeNext.Clear();
            var min = new float3(float.MaxValue);
            var max = new float3(float.MinValue);
            int count = 0;
            foreach (var a in arcs)
            {
                var length = Vector3.Distance(a.From, a.To);
                if (length < 0.05f)
                    continue;
                var mid = (a.From + a.To) * 0.5f;
                // Rule 20: fade by how thick the arc looks, never by fixed metres.
                var reach = Reach(a.Width);
                var distance = Vector3.Distance(camera, mid);
                var fade = math.saturate((reach - distance) / math.max(reach * (1f - ArcEndShare), 0.01f));
                if (fade <= 0f)
                    continue;

                // A crack once per strike (struck arcs) or now and then on a re-jag (steady ones).
                var path = a.Struck ? a.Strike : PathAt(a.Seed, clock, e.RejagRate);
                var crack = lastStrike.TryGetValue(a.Seed, out var was) && was != path
                            && (a.Struck || Hash01((uint)a.Seed ^ (uint)path * 0x85EBCA6Bu) < e.CrackChance);
                if (!lastStrike.ContainsKey(a.Seed) && a.Struck)
                    crack = true;
                lastStrikeNext[a.Seed] = path;
                if (crack && !a.Quiet)
                {
                    var reachSound = e.CrackReach * math.sqrt(a.Width);
                    if (Punchy.Audio.GameAudio.Audible(mid, reachSound))
                        Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Zip, mid, e.CrackPitch, e.CrackVolume, reachSound, Punchy.Audio.SoundGroup.World);
                    Struck?.Invoke(mid, a.Seed);
                }

                // Its light: one moving request per arc (keyed by seed), flickering with the path's brightness.
                var c = (Vector4)e.Colours[a.Colour].Light * (e.LightIntensity * fade * Brightness(e, a.Seed, path));
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Electric, a.Seed, mid,
                                                new Vector4(c.x, c.y, c.z, e.LightReach * math.sqrt(a.Width)));

                var jag = math.min(e.Jag * length, e.JagMax * math.max(a.Width, 1f));
                var strikeStart = a.Struck ? 1f : 0f; // TODO: pass the strike's start so the shader can flash it in
                instanceData[count++] = new ArcInstance
                {
                    FromSeed = new float4(a.From.x, a.From.y, a.From.z, math.asfloat((uint)a.Seed)),
                    ToWidth = new float4(a.To.x, a.To.y, a.To.z, a.Width),
                    Params = new float4(a.Colour, fade, jag, strikeStart)
                };
                min = math.min(min, math.min((float3)a.From, (float3)a.To));
                max = math.max(max, math.max((float3)a.From, (float3)a.To));
            }
            (lastStrike, lastStrikeNext) = (lastStrikeNext, lastStrike);
            arcs.Clear();
            if (count == 0)
                return;
            instances.SetData(instanceData, 0, 0, count);
            var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min) + Vector3.one * (2f * e.JagMax * 8f));
            var rp = new RenderParams(material) { worldBounds = bounds, matProps = props, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, QuadsPerArc * 6, count);
        }

        /// <summary>A path's brightness, as the shader works it out (without the dropout, which is too quick to light).</summary>
        static float Brightness(ElectricityEffect e, int seed, long path)
            => 1f - e.BrightnessJitter * Hash01((uint)seed ^ (uint)path * 0xC2B2AE35u);

        static void Buffers()
        {
            if (instances != null && instances.IsValid())
                return;
            instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxArcs, ArcInstance.Stride);
            props = new MaterialPropertyBlock();
            props.SetBuffer(InstancesId, instances);
            Application.quitting += Release;
        }

        static void Release()
        {
            instances?.Release();
            instances = null;
        }

        static void Upload(ElectricityEffect e)
        {
            var count = Mathf.Min(e.Colours.Length, ElectricityEffect.MaxColours);
            if (count == 0)
                return;
            for (int i = 0; i < ElectricityEffect.MaxColours; i++)
            {
                var c = e.Colours[i < count ? i : 0];
                colourTable[i * 2] = (Vector4)c.Core;
                colourTable[i * 2 + 1] = (Vector4)c.Glow;
            }
            Shader.SetGlobalVectorArray(ColoursId, colourTable);
            Shader.SetGlobalVector(ShapeId, new Vector4(e.CoreWidth, e.GlowWidth, e.GlowStrength, 0f));
            Shader.SetGlobalVector(ForkId, new Vector4(e.ForkChance, e.ForkLength, e.ForkMiss, 0f));
            Shader.SetGlobalVector(TimingId, new Vector4(e.RejagRate, e.Afterglow, e.Dropout, e.BrightnessJitter));
        }
    }
}
