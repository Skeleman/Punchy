using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Weather
{
    /// <summary>
    /// Advances the time of day and works out the sun and moon positions and the clear-sky colours for it.
    /// It doesn't touch the scene itself: WeatherSystem reads these values, layers the weather on top and applies
    /// the result. Colours are gradients keyed by the sun's height (left = sun straight down, middle = on the
    /// horizon, right = straight up), so dawn and dusk come out the same. Press T to toggle fast-forward.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-10)]   // before WeatherSystem reads it
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Current time, in hours (0–24).")]
        [Range(0f, 24f)] public float TimeOfDay = 9f;
        [Tooltip("Real minutes per full day.")]
        [Min(0.1f)] public float DayLengthMinutes = 24f;
        [Tooltip("How much faster time runs while fast-forward (T) is on.")]
        public float FastForward = 60f;
        public bool Paused;

        [Header("Sun path")]
        [Tooltip("Latitude in degrees: the noon sun stands 90° minus this above the horizon.")]
        [Range(0f, 80f)] public float Latitude = 35f;
        [Tooltip("Compass direction (degrees) the sun rises from.")]
        public float SunriseAzimuth = 90f;

        [Header("Colours by sun height")]
        public Gradient Zenith = Make(
            (0.00f, new Color(0.006f, 0.010f, 0.030f)), (0.44f, new Color(0.03f, 0.05f, 0.13f)),
            (0.50f, new Color(0.22f, 0.30f, 0.52f)), (0.60f, new Color(0.20f, 0.40f, 0.82f)), (1.00f, new Color(0.17f, 0.39f, 0.86f)));
        public Gradient Horizon = Make(
            (0.00f, new Color(0.012f, 0.018f, 0.040f)), (0.45f, new Color(0.13f, 0.11f, 0.17f)),
            (0.50f, new Color(0.92f, 0.52f, 0.30f)), (0.56f, new Color(0.86f, 0.74f, 0.64f)), (0.66f, new Color(0.70f, 0.80f, 0.93f)),
            (1.00f, new Color(0.70f, 0.80f, 0.93f)));
        public Gradient SunColor = Make(
            (0.48f, new Color(1.00f, 0.35f, 0.12f)), (0.53f, new Color(1.00f, 0.62f, 0.38f)),
            (0.62f, new Color(1.00f, 0.92f, 0.84f)), (1.00f, new Color(1.00f, 0.97f, 0.93f)));

        [Header("Moon")]
        public Color MoonColor = new Color(0.55f, 0.65f, 0.95f);
        [Tooltip("Moonlight strength relative to full sunlight.")]
        [Range(0f, 0.5f)] public float MoonBrightness = 0.12f;
        [Tooltip("Days from one full moon to the next. Day 0 (a fresh launch) is a full moon.")]
        [Range(2, 30)] public int LunarDays = 8;
        [Tooltip("Every this many full moons is a blue moon.")]
        [Range(2, 20)] public int BlueMoonEvery = 7;
        [Tooltip("Whole days since the start (counted as midnight passes). With the time of day it gives the moon's phase.")]
        public int Day;

        /// <summary>Unit vector towards the sun.</summary>
        public Vector3 SunDirection { get; private set; }
        public Vector3 MoonDirection { get; private set; }
        /// <summary>Direction towards whichever body is lighting the scene (sun by day, moon by night).</summary>
        public Vector3 LightDirection { get; private set; }
        public Color LightColor { get; private set; }
        /// <summary>Light strength relative to the noon sun.</summary>
        public float LightStrength { get; private set; }
        /// <summary>Colour × brightness of the sun's disc and glow in the sky (black once it has set).</summary>
        public Color SunDiscColor { get; private set; }
        public Color ClearZenith { get; private set; }
        public Color ClearHorizon { get; private set; }
        /// <summary>0 by day, 1 at full night.</summary>
        public float Night { get; private set; }
        /// <summary>The moon's phase, 0..1: 0 new, 0.5 full.</summary>
        public float MoonPhase { get; private set; }
        /// <summary>The share of the moon's face that is lit, 0..1 (moonlight scales with it).</summary>
        public float MoonLit { get; private set; }
        /// <summary>1 on the nights round a blue moon (every BlueMoonEvery-th full moon), easing in and out.</summary>
        public float BlueMoon { get; private set; }

        /// <summary>Command line: -moonphase 0..1 pins the phase (benches, shots); -bluemoon makes it blue.</summary>
        float pinnedPhase = -1f;
        bool forceBlue;
        float lastTime = -1f;

        void Awake()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-moonphase" && i + 1 < args.Length &&
                    float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var phase))
                    pinnedPhase = Mathf.Repeat(phase, 1f);
                if (args[i] == "-bluemoon")
                    forceBlue = true;
            }
        }

        /// <summary>Pins the moon's phase (0..1, negative to follow the days again) and whether it's blue.</summary>
        public void PinMoon(float phase, bool blue)
        {
            pinnedPhase = phase < 0f ? -1f : Mathf.Repeat(phase, 1f);
            forceBlue = blue;
        }

        void Update()
        {
            if (Application.isPlaying && !Paused)
            {
                if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
                    fastForward = !fastForward;
                var hoursPerSecond = 24f / (DayLengthMinutes * 60f) * (fastForward ? FastForward : 1f);
                TimeOfDay = Mathf.Repeat(TimeOfDay + hoursPerSecond * Time.deltaTime, 24f);
            }
            Evaluate();
        }

        bool fastForward;

        public void Evaluate()
        {
            // Midnight passing (however the time got there: advancing, or copied from the host) starts a new day.
            if (lastTime >= 0f && TimeOfDay < lastTime - 12f)
                Day++;
            lastTime = TimeOfDay;
            // The moon: full on day 0, waning, new halfway through the cycle, waxing back. Every BlueMoonEvery-th full
            // moon is blue on the nights round it.
            var days = Day + TimeOfDay / 24f;
            MoonPhase = pinnedPhase >= 0f ? pinnedPhase : Mathf.Repeat(0.5f + days / LunarDays, 1f);
            MoonLit = 0.5f - 0.5f * Mathf.Cos(MoonPhase * Mathf.PI * 2f);
            var fullMoon = Mathf.FloorToInt((days + LunarDays * 0.5f) / LunarDays);   // which full moon is nearest
            var nearFull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.04f, Mathf.Abs(MoonPhase - 0.5f)));
            BlueMoon = forceBlue ? 1f : fullMoon % BlueMoonEvery == BlueMoonEvery - 1 ? nearFull : 0f;
            // The sun turns once a day in a plane tilted south by the latitude; it rises at 6:00.
            var angle = (TimeOfDay - 6f) / 24f * Mathf.PI * 2f;
            var tilt = Latitude * Mathf.Deg2Rad;
            var local = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) * Mathf.Cos(tilt), -Mathf.Sin(angle) * Mathf.Sin(tilt));
            SunDirection = Quaternion.Euler(0f, SunriseAzimuth - 90f, 0f) * local;
            // A full moon, opposite the sun, nudged so it isn't exactly on the sun's path.
            MoonDirection = (Quaternion.Euler(0f, 0f, 8f) * -SunDirection).normalized;

            var key = SunDirection.y * 0.5f + 0.5f;
            ClearZenith = Zenith.Evaluate(key);
            ClearHorizon = Horizon.Evaluate(key);
            Night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.05f, SunDirection.y));
            SunDiscColor = SunColor.Evaluate(key) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.03f, SunDirection.y));

            // Whichever body is up lights the scene. Around sunset both are near the horizon and dim, so the
            // switch is invisible.
            var sunStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.15f, SunDirection.y));
            // Moonlight follows the phase: a thin crescent gives little, a new moon almost none.
            var moonStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.15f, MoonDirection.y)) * MoonBrightness *
                               Mathf.Lerp(0.08f, 1f, MoonLit);
            if (SunDirection.y > -0.02f)
            {
                LightDirection = SunDirection;
                LightColor = SunColor.Evaluate(key);
                LightStrength = sunStrength;
            }
            else
            {
                LightDirection = MoonDirection;
                LightColor = Color.Lerp(MoonColor, new Color(0.45f, 0.62f, 1f), BlueMoon);
                LightStrength = moonStrength;
            }
        }

        static Gradient Make(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
