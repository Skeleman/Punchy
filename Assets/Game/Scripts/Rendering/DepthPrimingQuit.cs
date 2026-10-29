using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Punchy.Rendering
{
    /// <summary>
    /// Turns depth priming off as a player quits, before the engine tears the renderer down: with priming Forced (Forward+,
    /// D3D12) the player crashed on every quit (access violation after "CodeReloadManager destroyed"). The renderer asset
    /// is only changed in memory, and only in players (in the editor it would be saved).
    /// </summary>
    static class DepthPrimingQuit
    {
#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => Application.quitting += () =>
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset)
                return;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(asset) as ScriptableRendererData[];
            if (list == null)
                return;
            foreach (var data in list)
                if (data is UniversalRendererData universal)
                    universal.depthPrimingMode = DepthPrimingMode.Disabled;
        };
#endif
    }
}
