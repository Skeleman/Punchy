using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-37 (#354): a test hook for the sun's shadow biases. "-shadowbias &lt;d&gt;" and "-shadownormalbias &lt;n&gt;" give the
    /// sun its own depth and normal bias in place of the pipeline asset's, so pairs can be compared without editing the
    /// asset (which stays the one place the shipped values live). The values go on the scene's sun light for this run
    /// only: in the editor the light goes back to its saved self when play mode ends, and the asset is never written.
    /// Either flag alone keeps the asset's value for the other. Cosmetic and local.
    /// </summary>
    public class SunShadowBias : MonoBehaviour
    {
        /// <summary>The depth and normal bias to use; below 0 means the pipeline asset's. Settable from eval for shots.</summary>
        public static float Depth = Read("-shadowbias"), Normal = Read("-shadownormalbias");

        static SunShadowBias instance;
        Light sunOf;
        UniversalAdditionalLightData data;
        bool sceneUsesPipeline;
        float sceneDepth, sceneNormal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null || (Depth < 0f && Normal < 0f))
                return;
            Ensure();
        }

        /// <summary>Starts the hook if a flag didn't (for setting Depth and Normal from eval in play mode).</summary>
        public static void Ensure()
        {
            if (instance != null)
                return;
            var go = new GameObject("SunShadowBias");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SunShadowBias>();
        }

        void LateUpdate()
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                return;
            if (sun != sunOf)
            {
                // A new sun (a scene load): give the old one its own values back and remember the new one's.
                Restore();
                sunOf = sun;
                data = sun.GetComponent<UniversalAdditionalLightData>();
                if (data == null)
                    return;
                sceneUsesPipeline = data.usePipelineSettings;
                sceneDepth = sun.shadowBias;
                sceneNormal = sun.shadowNormalBias;
            }
            if (data == null)
                return;
            var asset = UniversalRenderPipeline.asset;
            float depth = Depth >= 0f ? Depth : asset != null ? asset.shadowDepthBias : sceneDepth;
            float normal = Normal >= 0f ? Normal : asset != null ? asset.shadowNormalBias : sceneNormal;
            if (Depth < 0f && Normal < 0f)
            {
                if (data.usePipelineSettings != sceneUsesPipeline)
                    Restore();
                return;
            }
            // URP reads the light's own biases (same units as the asset's) once it stops using the pipeline's.
            data.usePipelineSettings = false;
            if (sun.shadowBias != depth)
                sun.shadowBias = depth;
            if (sun.shadowNormalBias != normal)
                sun.shadowNormalBias = normal;
        }

        void Restore()
        {
            if (sunOf != null && data != null)
            {
                data.usePipelineSettings = sceneUsesPipeline;
                sunOf.shadowBias = sceneDepth;
                sunOf.shadowNormalBias = sceneNormal;
            }
            if (sunOf != null && data == null)
                sunOf = null;
        }

        void OnDestroy()
        {
            Restore();
            data = null;
            sunOf = null;
            if (instance == this)
                instance = null;
        }

        static float Read(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, flag);
            if (i < 0 || i + 1 >= args.Length
                || !float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return -1f;
            return Mathf.Max(0f, value);
        }
    }
}
