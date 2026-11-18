using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Rendering.Effects
{
    /// <summary>
    /// R-29: draws the effects kit once a frame, so no caller has to own a scene object. It hooks the render
    /// pipeline's start of frame (after every LateUpdate, when callers have added this frame's arcs) and hands each
    /// family its material from Resources (never Shader.Find). Families join here as they move onto the kit.
    /// SKETCH (11-16, uncompiled): the Electricity material is made at the first editor slot.
    /// </summary>
    static class EffectsDriver
    {
        static Material electricity, plume;
        static int drawnFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            RenderPipelineManager.beginContextRendering -= Draw;
            RenderPipelineManager.beginContextRendering += Draw;
        }

        static void Draw(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            // Once a frame however many contexts render (scene view, reflection probes).
            if (drawnFrame == Time.frameCount)
                return;
            drawnFrame = Time.frameCount;
            var camera = Camera.main;
            if (camera == null)
                return;
            if (electricity == null)
                electricity = Resources.Load<Material>("Electricity");
            ElectricitySystem.Draw(electricity, camera.transform.position);
            if (plume == null)
                plume = Resources.Load<Material>("LandmarkPlume");
            PlumeSystem.Draw(plume);
        }
    }
}
