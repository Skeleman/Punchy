using Punchy.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Debugging
{
    /// <summary>
    /// Matched before/after shots (R-42 follow-up, debug only): one call holds the things that otherwise differ between
    /// two play sessions, so a pair taken on two branches shows the same sky, light and water.
    /// - Weather: the Sunny day type, and Clear held round the point ("-weather Clear" alone only clears cells, so an
    ///   overcast day type still gives a grey sky).
    /// - Time: the hour set and the day/night cycle paused.
    /// - Clock: _PunchyClock pinned (swell, tide, surf, ripples, wind in plants), set again late each frame and before
    ///   rendering, after WorldSync writes the shared clock.
    /// Usage from an eval or a test, after the player has landed:
    ///   Punchy.Debugging.MatchedShot.Hold(new Vector2(x, z), 12f, 1000f);
    /// Take each shot after about 25 s (the forced weather settles); Release() frees everything.
    /// </summary>
    public static class MatchedShot
    {
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        static Pin pin;

        /// <summary>The pinned clock (negative: not pinned). Change it between shots for other swell phases.</summary>
        public static float Clock = -1f;

        public static bool Hold(Vector2 xz, float hour = 12f, float clock = 1000f)
        {
            var ok = true;
            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.TimeOfDay = Mathf.Repeat(hour, 24f);
                cycle.Paused = true;
            }
            else
                ok = false;
            var weather = Object.FindAnyObjectByType<WeatherSystem>();
            if (weather != null)
            {
                var sunny = weather.DayTypes.FindIndex(d => d.Name == "Sunny");
                weather.SetDayType(sunny >= 0 ? sunny : 0, true);
                ok &= weather.ForceAt("Clear", xz);
            }
            else
                ok = false;
            Clock = clock;
            if (pin == null)
            {
                pin = new GameObject("MatchedShot").AddComponent<Pin>();
                pin.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            Shader.SetGlobalFloat(ClockId, Clock);
            Debug.Log($"MatchedShot: held Sunny/Clear at {xz.x:F0},{xz.y:F0}, hour {hour:F1}, clock {clock:F2} (ok {ok})");
            return ok;
        }

        public static void Release()
        {
            Clock = -1f;
            var cycle = Object.FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
                cycle.Paused = false;
            if (pin != null)
                Object.Destroy(pin.gameObject);
            pin = null;
        }

        [DefaultExecutionOrder(10000)]
        sealed class Pin : MonoBehaviour
        {
            void OnEnable() => RenderPipelineManager.beginContextRendering += BeforeRender;
            void OnDisable() => RenderPipelineManager.beginContextRendering -= BeforeRender;
            void Update() => Apply();
            void LateUpdate() => Apply();
            static void BeforeRender(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras) => Apply();

            static void Apply()
            {
                if (Clock >= 0f)
                    Shader.SetGlobalFloat(ClockId, Clock);
            }
        }
    }
}
