using UnityEngine;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-34 row 2 (#332): a debug override for the shared stone surface (Stone.hlsl). In play the stone's wetness is
    /// the world's own (rain drying over time, shores, fall spray, cave damp). "-stonewet 0..1" sets the least wetness
    /// stone shows, for shots of the wet look without waiting for rain. Cosmetic and local. <see cref="WetMin"/> can be
    /// set at runtime (shot scripts).
    /// </summary>
    public class StoneWetDebug : MonoBehaviour
    {
        /// <summary>The least wetness stone shows (0: the world's own).</summary>
        public static float WetMin;

        static readonly int WetMinId = Shader.PropertyToID("_PunchyStoneWetMin");
        static StoneWetDebug instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            // (Read here, not in a static initialiser: Play mode may start without a domain reload.)
            WetMin = Mathf.Clamp01(Number("-stonewet", 0f));
            Shader.SetGlobalFloat(WetMinId, WetMin);
            if (WetMin <= 0f || instance != null)
                return;
            var go = new GameObject("StoneWetDebug");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StoneWetDebug>();
        }

        /// <summary>Sets the override from a shot script (adds the updater if the launch flag didn't).</summary>
        public static void Set(float wetMin)
        {
            WetMin = Mathf.Clamp01(wetMin);
            Shader.SetGlobalFloat(WetMinId, WetMin);
            if (instance == null)
            {
                var go = new GameObject("StoneWetDebug");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<StoneWetDebug>();
            }
        }

        void LateUpdate() => Shader.SetGlobalFloat(WetMinId, WetMin);

        void OnDestroy()
        {
            Shader.SetGlobalFloat(WetMinId, 0f);
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
