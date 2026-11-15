using UnityEngine;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-34 row 1 (#332): the wet-stone look trial (Stone.hlsl) on boulders, built stone, steep rock faces and cave
    /// walls. Off by default; "-stonetrial 1" shows the dry trial (relief and crevice/edge colouring), "-stonetrial 2"
    /// the wet one (adds the gloss lobes and grazing sky). Cosmetic and local. <see cref="Mode"/> can be set at
    /// runtime (shot scripts).
    /// </summary>
    public class StoneTrial : MonoBehaviour
    {
        /// <summary>0 off (default), 1 dry, 2 wet.</summary>
        public static int Mode;

        static readonly int TrialId = Shader.PropertyToID("_PunchyStoneTrial");
        static StoneTrial instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            // (Read here, not in a static initialiser: Play mode may start without a domain reload.)
            Mode = Mathf.Clamp(Mathf.RoundToInt(Number("-stonetrial", 0f)), 0, 2);
            if (instance != null)
                return;
            var go = new GameObject("StoneTrial");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StoneTrial>();
        }

        void LateUpdate() => Shader.SetGlobalFloat(TrialId, Mode);

        void OnDestroy()
        {
            Shader.SetGlobalFloat(TrialId, 0f);
            if (instance == this)
                instance = null;
        }

        static float Number(string arg, float fallback)
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;   // (in the editor, also Temp/PlayArgs.txt)
            var i = System.Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
