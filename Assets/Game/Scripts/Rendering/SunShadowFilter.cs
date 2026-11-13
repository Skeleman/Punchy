using Punchy.Settings;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-20 row 1 (#286): picks how wide the sun's real-time shadows are filtered. As the sun turns, the edges of the
    /// cascades' shadows land on other shadow texels every frame, and with the narrow filter the scene's sun asks for
    /// they crawl and blink on still ground 10-60 m out. The wide filter averages over enough texels to hold them
    /// still. The cascades, their distances and the shadow map's size are not touched. From Medium up the sun gets
    /// the wide filter; the Low preset keeps the scene's. "-sunshadowfilter scene|low|medium|high" overrides it.
    /// Cosmetic and local.
    /// </summary>
    public class SunShadowFilter : MonoBehaviour
    {
        /// <summary>The override: -1 follows the graphics preset, 0 leaves the scene's value, 1-3 are URP's Low, Medium, High.</summary>
        public static int Override = Read();

        static SunShadowFilter instance;
        UniversalAdditionalLightData data;
        Light dataOf;
        SoftShadowQuality sceneQuality;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null)
                return;
            var go = new GameObject("SunShadowFilter");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SunShadowFilter>();
        }

        void LateUpdate()
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                return;
            if (sun != dataOf)
            {
                // A new sun (a scene load): give the old one its own value back and remember the new one's.
                Restore();
                dataOf = sun;
                data = sun.GetComponent<UniversalAdditionalLightData>();
                if (data != null)
                    sceneQuality = data.softShadowQuality;
            }
            if (data == null)
                return;
            var want = Override > 0 ? (SoftShadowQuality)Mathf.Min(Override, 3)
                : Override == 0 || GraphicsSettings.Preset == GraphicsPreset.Low ? sceneQuality
                : SoftShadowQuality.High;
            if (data.softShadowQuality != want)
                data.softShadowQuality = want;
        }

        void Restore()
        {
            if (data != null)
                data.softShadowQuality = sceneQuality;
            data = null;
            dataOf = null;
        }

        void OnDestroy()
        {
            // In the editor the scene's light outlives play mode only as its saved self, but leave it as found anyway.
            Restore();
            if (instance == this)
                instance = null;
        }

        static int Read()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-sunshadowfilter");
            if (i < 0 || i + 1 >= args.Length)
                return -1;
            switch (args[i + 1].ToLowerInvariant())
            {
                case "scene": return 0;
                case "low": return 1;
                case "medium": return 2;
                case "high": return 3;
                default: return -1;
            }
        }
    }
}
