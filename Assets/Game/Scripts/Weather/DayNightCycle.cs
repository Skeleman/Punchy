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
            var moonStrength = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.15f, MoonDirection.y)) * MoonBrightness;
            if (SunDirection.y > -0.02f)
            {
                LightDirection = SunDirection;
                LightColor = SunColor.Evaluate(key);
                LightStrength = sunStrength;
            }
            else
            {
                LightDirection = MoonDirection;
                LightColor = MoonColor;
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
