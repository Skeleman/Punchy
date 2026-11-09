using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test only (-ssao on a shot line): how the screen-space ambient occlusion gets its input. Job 31 found the
    /// depth-normal prepass, which only SSAO needs, at 22-33% of a town or forest frame. "normals" is today's setup; "depth"
    /// rebuilds normals from a depth-only prepass; "after" applies it after the opaque pass, with no prepass at all; "off"
    /// removes it. URP keeps these settings private, hence the reflection; it changes the loaded asset for this run only.
    /// </summary>
    static class SsaoTest
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        public static void Apply(string mode)
        {
            var asset = UniversalRenderPipeline.asset;
            if (asset == null)
                return;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Private)?.GetValue(asset) as ScriptableRendererData[];
            if (list == null)
            {
                Debug.LogWarning("SsaoTest: no renderer list");
                return;
            }
            foreach (var data in list)
            {
                if (data == null)
                    continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion")
                        continue;
                    feature.SetActive(mode != "off");
                    var settings = feature.GetType().GetField("m_Settings", Private)?.GetValue(feature);
                    var source = settings?.GetType().GetField("Source", Private);
                    var after = settings?.GetType().GetField("AfterOpaque", Private);
                    if (source == null || after == null)
                    {
                        Debug.LogWarning("SsaoTest: settings not found");
                        return;
                    }
                    source.SetValue(settings, System.Enum.ToObject(source.FieldType, mode == "normals" ? 1 : 0));
                    after.SetValue(settings, mode == "after");
                    feature.SetDirty();
                    Debug.Log($"SsaoTest: {mode} on {data.name}");
                }
            }
        }
    }
}
