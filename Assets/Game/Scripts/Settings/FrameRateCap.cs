using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Settings
{
    /// <summary>
    /// The frame-rate cap: 30, 40, 60 or 120 frames a second, or unlimited; 60 unless chosen otherwise, and kept per
    /// machine (PlayerPrefs). The Options button (F4, or the pad's View / Select) steps through the choices, showing the
    /// new one for a moment; the F1 readout shows it too. "-fpscap &lt;30|40|60|120|0&gt;" on the command line (or in
    /// Temp/PlayArgs.txt) sets it for that run only, for benches and tests (0 = unlimited). Vsync stays off, so the cap
    /// is what decides. The network keeps its own tick rate whatever the frame rate (a host's server catches up in
    /// steps), so a cap changes how smooth it looks, not how fast the world runs.
    /// </summary>
    public static class FrameRateCap
    {
        /// <summary>The choices, in the order the Options button steps through them (0 = unlimited).</summary>
        public static readonly int[] Choices = { 30, 40, 60, 120, 0 };
        const string Key = "Punchy.FrameRateCap";
        const int Default = 60;

        /// <summary>The cap now (0 = unlimited).</summary>
        public static int Current { get; private set; } = Default;
        /// <summary>Set from the command line for this run (not saved).</summary>
        public static bool FromCommandLine { get; private set; }

        public static string Describe(int cap) => cap > 0 ? $"{cap} fps" : "unlimited";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Current = PlayerPrefs.GetInt(Key, Default);
            if (System.Array.IndexOf(Choices, Current) < 0)
                Current = Default;
            FromCommandLine = false;
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-fpscap" && int.TryParse(args[i + 1], out var cap) && cap >= 0)
                {
                    Current = cap;
                    FromCommandLine = true;
                }
            Apply();
            // (Something to watch the Options button each frame, kept across the scene reload a join does.)
            var watcher = new GameObject("FrameRateCap");
            Object.DontDestroyOnLoad(watcher);
            watcher.hideFlags = HideFlags.HideInHierarchy;
            watcher.AddComponent<Watcher>();
        }

        static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Current > 0 ? Current : -1;
        }

        /// <summary>The next choice after the current one: applied and saved for this machine.</summary>
        public static void Next()
        {
            var at = System.Array.IndexOf(Choices, Current);
            Current = Choices[(at + 1) % Choices.Length];
            FromCommandLine = false;
            PlayerPrefs.SetInt(Key, Current);
            PlayerPrefs.Save();
            Apply();
        }

        /// <summary>Steps the cap on the Options button, and shows the new one for a moment.</summary>
        class Watcher : MonoBehaviour
        {
            InputAction options;
            bool wasDown;
            float shownUntil;
            GUIStyle style;

            void Update()
            {
                options ??= InputSystem.actions?.FindAction("Player/Options");
                // (On the press itself, from the held state: whichever input update the press arrived in.)
                var down = options != null && options.IsPressed();
                if (down && !wasDown)
                {
                    Next();
                    shownUntil = Time.unscaledTime + 2f;
                }
                wasDown = down;
            }

            void OnGUI()
            {
                if (Time.unscaledTime > shownUntil)
                    return;
                style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.UpperCenter, richText = true };
                GUI.Label(new Rect(0f, 40f, Screen.width, 40f), $"<b>Frame cap: {Describe(Current)}</b>", style);
            }
        }
    }
}
