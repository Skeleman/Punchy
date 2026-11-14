using Punchy.Settings;
using UnityEngine;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-19 row 2 (#281): cost switches for the layered rock the terrain shader draws on steep faces, for the bench
    /// to compare where rock fills the view. All off by default, which is the full rock. They only drop or thin the
    /// shader's noise: "-rockhighonly" keeps the beds and slabs for the High preset and up (below it a face keeps
    /// its colour and grain), "-rockoctaves 1" reads one noise octave where the beds, their wander and the slabs
    /// read two or more, and "-rockslabpx n" shows the slabs only once a 2.5 m slab spans n reference pixels
    /// (DetailPolicy.ScreenSize's measure; the default is 2). Cosmetic and local.
    /// </summary>
    public class SteepRockCost : MonoBehaviour
    {
        /// <summary>Beds and slabs only from the High preset up.</summary>
        public static bool HighOnly = Flag("-rockhighonly");
        /// <summary>One noise octave instead of two.</summary>
        public static bool OneOctave = Number("-rockoctaves", 2f) < 1.5f;
        /// <summary>Reference pixels a 2.5 m slab spans where slabs begin to show; 0 keeps the shader's 2.</summary>
        public static float SlabPixels = Mathf.Max(0f, Number("-rockslabpx", 0f));

        static readonly int CostId = Shader.PropertyToID("_PunchyRockCost");
        static SteepRockCost instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null)
                return;
            var go = new GameObject("SteepRockCost");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SteepRockCost>();
        }

        void LateUpdate()
        {
            var preset = GraphicsSettings.Preset;
            var plain = HighOnly && (preset == GraphicsPreset.Low || preset == GraphicsPreset.Medium);
            Shader.SetGlobalVector(CostId, new Vector4(plain ? 1f : 0f, OneOctave ? 1f : 0f, SlabPixels, 0f));
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector(CostId, Vector4.zero);
            if (instance == this)
                instance = null;
        }

        static bool Flag(string arg)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, arg);
            return i >= 0 && !(i + 1 < args.Length && args[i + 1] == "0");
        }

        static float Number(string arg, float fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
