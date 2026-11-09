using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Punchy.Rendering
{
    /// <summary>How big an effect's low-resolution buffer is, against the camera. Off leaves the effect to draw itself.</summary>
    public enum LowResMode { Off, Half, Quarter }

    /// <summary>
    /// A low-resolution buffer for expensive full-screen effects (refactor 3.4). Each effect listed on this feature is
    /// drawn into a half- or quarter-size target, blended with its own last frame by reprojection, and handed to whoever
    /// composites it as a global texture, which they read depth-aware with PunchyLowResUpsample (LowResEffect.hlsl).
    /// There is no effect code in here: a new user is an entry in Effects on the renderer asset plus a shader that follows
    /// the header of LowResEffect.hlsl. The surface is that list, <see cref="SetMode"/> and the hlsl file.
    /// Only the main game camera gets it; other cameras see the active global at 0, and the effect's consumer falls back.
    /// Every target is created once per size and imported, so nothing is allocated per frame.
    /// </summary>
    public sealed class LowResEffectFeature : ScriptableRendererFeature
    {
        /// <summary>One effect that draws through the buffer.</summary>
        [System.Serializable]
        public sealed class Effect
        {
            [Tooltip("The name SetMode knows it by.")]
            public string Name;
            [Tooltip("Its full-screen shader: see LowResEffect.hlsl.")]
            public Shader Shader;
            [Tooltip("The global texture the result is published as (_Depth and _Size are published beside it).")]
            public string OutputGlobal;
            [Tooltip("A global float: 1 while this camera has the buffer, 0 when the effect should draw itself.")]
            public string ActiveGlobal;
            [Tooltip("How much of last frame stays each frame, once it passes the neighbourhood clamp.")]
            [Range(0f, 0.98f)] public float HistoryWeight = 0.9f;
            [Tooltip("Reproject by the scene depth; off treats the effect as infinitely far away (sky).")]
            public bool HistoryAtDepth;
            [Tooltip("Skip the effect on frames without a skybox material (no sky to draw it on).")]
            public bool NeedsSkybox;
        }

        [SerializeField] Effect[] effects = System.Array.Empty<Effect>();
        [Tooltip("The internal depth downsample and temporal resolve: LowResEffectInternal.shader.")]
        [SerializeField] Shader internalShader;

        static readonly Dictionary<string, LowResMode> Modes = new Dictionary<string, LowResMode>();

        /// <summary>Sets an effect's buffer size by its Name; takes effect next frame. Effects start Off.</summary>
        public static void SetMode(string effect, LowResMode mode) => Modes[effect] = mode;

        /// <summary>The effect's current size.</summary>
        public static LowResMode GetMode(string effect) => Modes.TryGetValue(effect, out var mode) ? mode : LowResMode.Off;

        Pass pass;

        public override void Create()
        {
            pass?.Dispose();
            pass = new Pass(effects, internalShader) { renderPassEvent = RenderPassEvent.BeforeRenderingSkybox };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            bool mainCamera = cameraData.cameraType == CameraType.Game && cameraData.renderType == CameraRenderType.Base
                              && cameraData.camera.CompareTag("MainCamera");
            if (pass != null && pass.Prepare(mainCamera))
            {
                pass.ConfigureInput(ScriptableRenderPassInput.Depth);
                renderer.EnqueuePass(pass);
            }
        }

        protected override void Dispose(bool disposing) => pass?.Dispose();

        sealed class Runtime
        {
            public Effect Def;
            public Material Draw, Internal;
            public int OutputId, DepthId, SizeId, ActiveId;
            public LowResMode Mode;
            public bool Running, HistoryValid;
            public int Width, Height, Write;
            public RTHandle Raw, Depth;
            public readonly RTHandle[] History = new RTHandle[2];
            public Matrix4x4 PreviousViewProjection;
            public Vector3 PreviousCamera;
        }

        sealed class Pass : ScriptableRenderPass
        {
            sealed class DrawData
            {
                public Material Material;
                public int ShaderPass;
                public TextureHandle Source;
            }

            static readonly int SourceDepthId = Shader.PropertyToID("_PunchyLowResSourceDepth");
            static readonly int LowDepthId = Shader.PropertyToID("_PunchyLowResDepth");
            static readonly int SizeId = Shader.PropertyToID("_PunchyLowResSize");
            static readonly int JitterId = Shader.PropertyToID("_PunchyLowResJitter");
            static readonly int InverseViewProjectionId = Shader.PropertyToID("_PunchyLowResInvVP");
            static readonly int PreviousViewProjectionId = Shader.PropertyToID("_PunchyLowResPrevVP");
            static readonly int CameraDeltaId = Shader.PropertyToID("_PunchyLowResCameraDelta");
            static readonly int CurrentId = Shader.PropertyToID("_PunchyLowResCurrent");
            static readonly int HistoryId = Shader.PropertyToID("_PunchyLowResHistory");
            static readonly int ResolveId = Shader.PropertyToID("_PunchyLowResResolve");

            // Sub-pixel offsets (Halton 2,3) cycled per frame, so history gathers detail between the low-resolution pixels.
            static readonly Vector2[] Jitter =
            {
                new Vector2(0f, -0.1667f), new Vector2(-0.25f, 0.1667f), new Vector2(0.25f, -0.3889f), new Vector2(-0.375f, -0.0556f),
                new Vector2(0.125f, 0.2778f), new Vector2(-0.125f, -0.2778f), new Vector2(0.375f, 0.0556f), new Vector2(-0.4375f, 0.3889f),
            };

            static readonly BaseRenderFunc<DrawData, RasterGraphContext> DrawFunc = Execute;

            readonly Runtime[] runtimes;
            int frame;

            public Pass(Effect[] effects, Shader internalShader)
            {
                profilingSampler = new ProfilingSampler("Low-res effects");
                runtimes = new Runtime[effects?.Length ?? 0];
                for (int i = 0; i < runtimes.Length; i++)
                {
                    var effect = effects[i];
                    var runtime = runtimes[i] = new Runtime { Def = effect };
                    if (effect?.Shader == null || internalShader == null || string.IsNullOrEmpty(effect.OutputGlobal))
                        continue;
                    runtime.Draw = CoreUtils.CreateEngineMaterial(effect.Shader);
                    runtime.Internal = CoreUtils.CreateEngineMaterial(internalShader);
                    runtime.OutputId = Shader.PropertyToID(effect.OutputGlobal);
                    runtime.DepthId = Shader.PropertyToID(effect.OutputGlobal + "_Depth");
                    runtime.SizeId = Shader.PropertyToID(effect.OutputGlobal + "_Size");
                    runtime.ActiveId = Shader.PropertyToID(effect.ActiveGlobal);
                }
            }

            /// <summary>Publishes each effect's on/off for this camera; true when any effect runs.</summary>
            public bool Prepare(bool mainCamera)
            {
                bool any = false;
                foreach (var runtime in runtimes)
                {
                    if (runtime.Draw == null)
                        continue;
                    runtime.Mode = GetMode(runtime.Def.Name);
                    runtime.Running = mainCamera && runtime.Mode != LowResMode.Off
                                      && (!runtime.Def.NeedsSkybox || RenderSettings.skybox != null);
                    Shader.SetGlobalFloat(runtime.ActiveId, runtime.Running ? 1f : 0f);
                    if (!runtime.Running)
                        runtime.HistoryValid = false;
                    any |= runtime.Running;
                }
                return any;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resources = frameData.Get<UniversalResourceData>();
                var camera = cameraData.camera;
                frame++;

                // Rotation only: a sky-like effect reprojects by direction, and depth positions stay camera-relative (large
                // world coordinates would lose precision here).
                var view = camera.worldToCameraMatrix;
                view.SetColumn(3, new Vector4(0, 0, 0, 1));
                var viewProjection = GL.GetGPUProjectionMatrix(camera.nonJitteredProjectionMatrix, true) * view;
                var inverseViewProjection = viewProjection.inverse;
                var cameraPosition = camera.transform.position;
                var jitter = Jitter[frame % Jitter.Length];

                foreach (var runtime in runtimes)
                {
                    if (runtime.Draw == null || !runtime.Running)
                        continue;
                    int scale = runtime.Mode == LowResMode.Half ? 2 : 4;
                    int width = Mathf.Max(1, (cameraData.cameraTargetDescriptor.width + scale - 1) / scale);
                    int height = Mathf.Max(1, (cameraData.cameraTargetDescriptor.height + scale - 1) / scale);
                    Allocate(runtime, width, height);

                    var size = new Vector4(width, height, 1f / width, 1f / height);
                    int write = runtime.Write;
                    var writeTarget = runtime.History[write];
                    var readTarget = runtime.History[1 - write];
                    var cameraDelta = cameraPosition - runtime.PreviousCamera;

                    var draw = runtime.Draw;
                    draw.SetTexture(LowDepthId, runtime.Depth.rt);
                    draw.SetVector(SizeId, size);
                    draw.SetVector(JitterId, new Vector4(jitter.x, jitter.y, frame & 1023, Mathf.Repeat(frame * 0.61803398875f, 1f)));
                    draw.SetMatrix(InverseViewProjectionId, inverseViewProjection);

                    var resolve = runtime.Internal;
                    resolve.SetTexture(LowDepthId, runtime.Depth.rt);
                    resolve.SetTexture(CurrentId, runtime.Raw.rt);
                    resolve.SetTexture(HistoryId, readTarget.rt);
                    resolve.SetVector(SizeId, size);
                    resolve.SetMatrix(InverseViewProjectionId, inverseViewProjection);
                    resolve.SetMatrix(PreviousViewProjectionId, runtime.PreviousViewProjection);
                    resolve.SetVector(CameraDeltaId, cameraDelta);
                    resolve.SetVector(ResolveId, new Vector4(runtime.HistoryValid ? runtime.Def.HistoryWeight : 0f, runtime.Def.HistoryAtDepth ? 1f : 0f, scale, 0f));

                    var depthTarget = renderGraph.ImportTexture(runtime.Depth);
                    var rawTarget = renderGraph.ImportTexture(runtime.Raw);
                    var historyTarget = renderGraph.ImportTexture(writeTarget);

                    using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Low-res depth", out var data))
                    {
                        data.Material = resolve;
                        data.ShaderPass = 0;
                        data.Source = resources.cameraDepthTexture;
                        builder.UseTexture(resources.cameraDepthTexture);
                        builder.SetRenderAttachment(depthTarget, 0);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(DrawFunc);
                    }
                    using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Low-res effect", out var data))
                    {
                        data.Material = draw;
                        data.ShaderPass = 0;
                        builder.UseTexture(depthTarget);
                        builder.SetRenderAttachment(rawTarget, 0);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(DrawFunc);
                    }
                    using (var builder = renderGraph.AddRasterRenderPass<DrawData>("Low-res resolve", out var data))
                    {
                        data.Material = resolve;
                        data.ShaderPass = 1;
                        builder.UseTexture(rawTarget);
                        builder.UseTexture(depthTarget);
                        builder.SetRenderAttachment(historyTarget, 0);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(DrawFunc);
                    }

                    Shader.SetGlobalTexture(runtime.OutputId, writeTarget.rt);
                    Shader.SetGlobalTexture(runtime.DepthId, runtime.Depth.rt);
                    Shader.SetGlobalVector(runtime.SizeId, size);
                    runtime.Write = 1 - write;
                    runtime.HistoryValid = true;
                    runtime.PreviousViewProjection = viewProjection;
                    runtime.PreviousCamera = cameraPosition;
                }
            }

            static void Execute(DrawData data, RasterGraphContext context)
            {
                if (data.Source.IsValid())
                    data.Material.SetTexture(SourceDepthId, data.Source);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, data.ShaderPass, MeshTopology.Triangles, 3);
            }

            void Allocate(Runtime runtime, int width, int height)
            {
                if (runtime.Raw != null && runtime.Width == width && runtime.Height == height)
                    return;
                Release(runtime);
                runtime.Width = width;
                runtime.Height = height;
                runtime.HistoryValid = false;
                runtime.Raw = Target(width, height, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Point, runtime.Def.Name + " raw");
                runtime.Depth = Target(width, height, GraphicsFormat.R32_SFloat, FilterMode.Point, runtime.Def.Name + " depth");
                runtime.History[0] = Target(width, height, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, runtime.Def.Name + " history 0");
                runtime.History[1] = Target(width, height, GraphicsFormat.R16G16B16A16_SFloat, FilterMode.Bilinear, runtime.Def.Name + " history 1");
            }

            static RTHandle Target(int width, int height, GraphicsFormat format, FilterMode filter, string name)
                => RTHandles.Alloc(width, height, colorFormat: format, filterMode: filter, wrapMode: TextureWrapMode.Clamp, name: name);

            static void Release(Runtime runtime)
            {
                runtime.Raw?.Release();
                runtime.Depth?.Release();
                runtime.History[0]?.Release();
                runtime.History[1]?.Release();
                runtime.Raw = runtime.Depth = null;
                runtime.History[0] = runtime.History[1] = null;
            }

            public void Dispose()
            {
                foreach (var runtime in runtimes)
                {
                    Release(runtime);
                    CoreUtils.Destroy(runtime.Draw);
                    CoreUtils.Destroy(runtime.Internal);
                }
            }
        }
    }
}
