using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Punchy.CameraRig
{
    /// <summary>
    /// R-18 (#282, rule 24): URP's Stop NaN on the main camera, set from code at start-up and after every scene load (not
    /// in the scene, the renderer or ProjectSettings). Every white bloom circle so far was one NaN pixel that bloom's blur
    /// spread wide; this replaces such pixels with black before post, so one missed NaN can't grow into a disc. A safety
    /// net only: shader and mesh maths must stay NaN-safe at the source, and SparkleProbe's own camera stays raw (off),
    /// so its count still finds them. "-stopnan 0" turns the guard off (a bench arm, and for hunting by eye).
    /// </summary>
    public static class NanGuard
    {
        /// <summary>Whether the guard is wanted ("-stopnan 0" clears it at start-up; <see cref="Set"/> changes it live).</summary>
        public static bool On { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-stopnan");
            // (Play mode may start without a domain reload: each run starts from on.)
            On = !(at >= 0 && at + 1 < args.Length && args[at + 1] == "0");
            Apply();
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode m) => Apply();

        /// <summary>Turns the guard on or off for the main camera now (for probes and before/after shots).</summary>
        public static void Set(bool on)
        {
            On = on;
            Apply();
        }

        /// <summary>The main camera's current Stop NaN (false with no main camera).</summary>
        public static bool Active
        {
            get
            {
                var cam = MainCamera();
                return cam != null && cam.GetUniversalAdditionalCameraData().stopNaN;
            }
        }

        static void Apply()
        {
            var cam = MainCamera();
            if (cam != null)
                cam.GetUniversalAdditionalCameraData().stopNaN = On;
        }

        // The rig's camera (the one the player sees through); Camera.main as a fallback.
        static Camera MainCamera()
        {
            var rig = Object.FindAnyObjectByType<ThirdPersonCamera>();
            var cam = rig != null ? rig.GetComponent<Camera>() : null;
            return cam != null ? cam : Camera.main;
        }
    }
}
