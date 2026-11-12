using Punchy.Player;
using Punchy.Weather;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Settings
{
    /// <summary>
    /// The weather shortcut (P-20, item 29): K on the keyboard (keyboard only since Zach 11-12: a pad's RT is the right hand's attack) puts the next weather type (WeatherSystem's
    /// presets in order, then back to clear) over the player, through WeatherSystem.ForceAt, the same forcing the ] debug
    /// key and the "-weather" test flag use. Listed in the Keys tab. Local only: on a joined client the host's weather
    /// comes back with the next sync (WeatherSystem.Remote), so it's a solo and host shortcut.
    /// "-weathercycle N" (tests): presses it every N seconds and, with -shotdir, saves a shot of each type
    /// (weather-&lt;n&gt;-&lt;type&gt;.png) halfway to the next press.
    /// </summary>
    public class WeatherKey : MonoBehaviour
    {
        public const string KeyboardName = "K";
        int next = 1;
        float every, pressAt, shotAt;
        string shotDir, shotName;
        int presses;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var go = new GameObject("WeatherKey");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            var key = go.AddComponent<WeatherKey>();
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-weathercycle");
            if (at >= 0 && at + 1 < args.Length)
                float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out key.every);
            at = System.Array.IndexOf(args, "-shotdir");
            if (at >= 0 && at + 1 < args.Length)
                key.shotDir = args[at + 1];
            key.pressAt = Time.realtimeSinceStartup + 25f;   // (the first test press once the world is up)
            key.shotAt = float.MaxValue;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var pressed = !Punchy.UI.Overlay.AnyOpen && GUIUtility.keyboardControl == 0 &&
                          (keyboard != null && keyboard.kKey.wasPressedThisFrame);
            var test = every > 0f && Time.realtimeSinceStartup >= pressAt;
            if (test)
                pressAt = Time.realtimeSinceStartup + every;
            if (pressed || test)
                Cycle(test ? "-weathercycle" : KeyboardName);
            if (Time.realtimeSinceStartup >= shotAt)
            {
                shotAt = float.MaxValue;
                var path = System.IO.Path.Combine(shotDir, shotName + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"WeatherKey: shot {path}");
            }
        }

        void Cycle(string by)
        {
            var weather = FindAnyObjectByType<WeatherSystem>();
            if (weather == null || weather.Presets.Count == 0 || !TryPlayerXZ(out var xz))
                return;
            if (next >= weather.Presets.Count)
                next = 0;
            var preset = weather.Presets[next];
            var name = preset != null ? preset.name : "Clear";
            // (Preset 0 is the clear sky: ForceAt with it only clears the cells round the player.)
            var forced = weather.ForceAt(name, xz);
            Debug.Log($"WeatherKey: {by} -> weather {next}/{weather.Presets.Count - 1} {name}{(forced ? "" : " (not forced)")}");
            presses++;
            if (every > 0f && shotDir != null)
            {
                shotName = $"weather-{presses}-{name}";
                shotAt = Time.realtimeSinceStartup + every * 0.6f;
            }
            next++;
        }

        static bool TryPlayerXZ(out Vector2 xz)
        {
            xz = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (!q.TryGetSingleton<LocalTransform>(out var t))
                return false;
            xz = new Vector2(t.Position.x, t.Position.z);
            return true;
        }
    }
}
