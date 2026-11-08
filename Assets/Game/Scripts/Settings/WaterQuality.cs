using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>The water's reflections: High marches the screen as tuned, Medium with fewer steps, Low only mirrors
    /// the sky (with its Fresnel), no screen march at all.</summary>
    public enum ReflectionQuality { Low, Medium, High }

    /// <summary>
    /// One quality policy for the water (refactor 3.4; ChatGPT's audit of the first pass, section 3): where the water's
    /// dear parts are worked out, decided per pixel and per vertex by how big the water looks there, the
    /// <see cref="DetailPolicy"/> idea (reference pixels a metre of water spans, not metres away), instead of an
    /// assortment of distance checks in the shader. Four tiers by <see cref="Tiers"/>:
    /// - Near: the screen-space reflection march at full steps, refraction, every swell wave, fine ripples.
    /// - Mid: the march with fewer, longer steps (it still reaches as far), refraction fading out towards Far.
    /// - Far: a short, coarse march fading out; no refraction.
    /// - Very far: the sky and Fresnel only.
    /// Swell waves are summed while each looks at least <see cref="SwellWavePixels"/> long (the short ones go first,
    /// smoothly), and the fine ripples, glitter, rain rings and wakes fade out as a metre shrinks to
    /// <see cref="FineFadePixels"/> (350 m at the reference view, as tuned in W-117).
    ///
    /// The Graphics menu's Water reflections setting picks the march's steps (High as tuned, Medium fewer, Low none),
    /// kept per machine (PlayerPrefs); "-waterquality low|medium|high" sets it for one run (benches), never saved.
    /// The Detail setting moves the tier boundaries, as it does DetailPolicy's. All of it is cosmetic and per machine:
    /// what the camera sees, never what the swell does (WaterSwells.Height counts every wave). "-waterssr 0" and
    /// "-waterrefraction 0" (CaveSystem) still turn those off outright, for profiling.
    /// </summary>
    public static class WaterQuality
    {
        const string Key = "Punchy.Gfx.WaterReflection";

        public static ReflectionQuality Reflection { get; private set; } = ReflectionQuality.High;
        /// <summary>Set from the command line for this run (not saved).</summary>
        public static bool FromCommandLine { get; private set; }

        /// <summary>The tier boundaries, in reference pixels one metre of water spans, at Detail 1: near inside ~78 m,
        /// mid to ~230 m, far to ~620 m at the reference view.</summary>
        public static readonly DetailPolicy.Thresholds Tiers = new DetailPolicy.Thresholds(12f, 4f, 1.5f);
        /// <summary>A swell wave is summed while its length looks at least this many reference pixels, fading in over
        /// half as many again (the 5.7 m wave out to ~270 m, the 41 m one to ~1.9 km).</summary>
        public const float SwellWavePixels = 20f;
        /// <summary>The fine detail fades out as a metre of water shrinks to this many reference pixels.</summary>
        public static readonly float FineFadePixels = DetailPolicy.PixelsPerMetreAt(DetailPolicy.ReferenceFov) / 350f;

        /// <summary>The reflection march's steps by setting (Low, Medium, High): x near, y mid, z far, w the refinement
        /// steps once it hits. High near is the march as tuned (28 + 5).</summary>
        static readonly int4[] Steps = { int4.zero, new int4(16, 10, 6, 4), new int4(28, 16, 10, 5) };
        /// <summary>The march's first step (m), and the 1.22x growth of the tuned 28 steps: every step count grows its
        /// steps so it reaches as far as those do (~355 m), only more coarsely.</summary>
        const float FirstStep = 0.3f, TunedGrowth = 1.22f;
        const int TunedSteps = 28;

        static readonly int TiersId = Shader.PropertyToID("_PunchyWaterTiers");
        static readonly int SsrId = Shader.PropertyToID("_PunchyWaterSsr");
        static readonly int GrowthId = Shader.PropertyToID("_PunchyWaterSsrGrowth");
        static readonly int DetailId = Shader.PropertyToID("_PunchyWaterDetailAt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            FromCommandLine = false;
            Reflection = (ReflectionQuality)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)ReflectionQuality.High), 0, 2);
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-waterquality" && System.Enum.TryParse<ReflectionQuality>(args[i + 1], true, out var q))
                {
                    Reflection = q;
                    FromCommandLine = true;
                }
            Debug.Log($"Water reflections: {Reflection}" + (FromCommandLine ? " (from the command line)" : ""));
        }

        /// <summary>The next setting (High, Medium, Low, High...): applied and saved for this machine.</summary>
        public static void Next()
        {
            Reflection = Reflection == ReflectionQuality.Low ? ReflectionQuality.High : Reflection - 1;
            FromCommandLine = false;
            PlayerPrefs.SetInt(Key, (int)Reflection);
        }

        /// <summary>This frame's policy for the water shader, from the camera that draws it (main thread, once a frame).</summary>
        public static void Publish(Camera cam)
        {
            if (cam == null)
                return;
            var detail = math.max(GraphicsSettings.Detail, 0.1f);
            var tiers = Tiers.Scaled(detail);
            Shader.SetGlobalVector(TiersId, new Vector4(DetailPolicy.PixelsPerMetreAt(cam.fieldOfView), tiers.Near, tiers.Mid, tiers.Far));
            var steps = Steps[(int)Reflection];
            Shader.SetGlobalVector(SsrId, new Vector4(steps.x, steps.y, steps.z, steps.w));
            Shader.SetGlobalVector(GrowthId, new Vector4(Growth(steps.x), Growth(steps.y), Growth(steps.z), FirstStep));
            Shader.SetGlobalVector(DetailId, new Vector4(SwellWavePixels / detail, FineFadePixels / detail, 0f, 0f));
        }

        /// <summary>How much each of this many steps grows over the last so the march reaches as far as the tuned one.</summary>
        static float Growth(int steps)
        {
            if (steps <= 0 || steps >= TunedSteps)
                return TunedGrowth;
            var reach = FirstStep * (math.pow(TunedGrowth, TunedSteps) - 1f) / (TunedGrowth - 1f);
            // (Reach grows with the growth, so halve the range till it's found.)
            float lo = TunedGrowth, hi = 4f;
            for (int i = 0; i < 30; i++)
            {
                var g = (lo + hi) * 0.5f;
                if (FirstStep * (math.pow(g, steps) - 1f) / (g - 1f) < reach) lo = g; else hi = g;
            }
            return (lo + hi) * 0.5f;
        }
    }
}
