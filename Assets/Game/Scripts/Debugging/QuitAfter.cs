using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test flag "-quitafter N": the player quits by itself N seconds after it started (real time, so a join's scene
    /// reload doesn't restart the count). For log-only tests, network pairs above all, that need no shot list to end
    /// them: agents may not kill a player, and one left running locks the build's exe.
    /// </summary>
    public static class QuitAfter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-quitafter");
            if (at < 0 || at + 1 >= args.Length || !float.TryParse(args[at + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || seconds <= 0f)
                return;
            var go = new GameObject("QuitAfter");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<Timer>().At = seconds;
            Debug.Log($"QuitAfter: quitting at {seconds:F0} s");
        }

        class Timer : MonoBehaviour
        {
            public float At;
            bool quitting;

            void Update()
            {
                if (quitting || Time.realtimeSinceStartup < At)
                    return;
                quitting = true;
                Debug.Log($"QuitAfter: {Time.realtimeSinceStartup:F1} s, quitting");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }
    }
}
