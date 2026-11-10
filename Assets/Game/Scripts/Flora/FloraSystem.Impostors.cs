using System;
using System.Globalization;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Flora
{
    /// <summary>
    /// R-7: octahedral impostors, the far stand-in for flora (after shaderbits.com/blog/octahedral-impostors). Models
    /// are grown in code at load, so their impostors are baked at load too: once the layers are built, each model of an
    /// impostor layer is drawn from a grid of view directions laid out on an octahedron (FloraImpostor.hlsl; the upper
    /// half by default, since plants aren't seen from below, the whole one as an option) into its slice of two texture
    /// arrays: albedo and coverage, and the normal, depth and occlusion. Flying is in the game, so the views go all the
    /// way up, not round the horizon alone. The bake runs on the GPU a few models a frame (ImpostorBakesPerFrame) and
    /// logs each; the atlases live with their layer and are freed with it. Cosmetic and local: each client bakes its own.
    /// Once a layer's bake is done, its models' impostor reaches (DetailPolicy.ImpostorReach) go to the cull, which hands
    /// each plant past its coarsest mesh over to draw slot ImpostorDraw: one quad per plant through the same indirect
    /// path (FloraImpostor.shader's ForwardLit), blending the three frames nearest its view (or one, ImpostorOneFrame).
    /// </summary>
    public partial class FloraSystem
    {
        /// <summary>Frames per side of a model's grid (8 or 12 to start: the card's two sizes), "-impostorframes N".</summary>
        public static int ImpostorFrames = (int)LaunchNumber("-impostorframes", 8);
        /// <summary>Texels a side of one frame, "-impostorpixels N". 32 by default: a shrub at impostor range is only a
        /// dozen or two pixels tall on screen, and 64 costs four times the memory (about 570 MB for the shrubs' 214 models).</summary>
        public static int ImpostorPixels = (int)LaunchNumber("-impostorpixels", 32);
        /// <summary>The whole octahedron (seen from below too) rather than its upper half, "-impostorfull".</summary>
        public static bool ImpostorFull = Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-impostorfull") >= 0;
        /// <summary>No impostors at all ("-noimpostors"): nothing baked, for an A/B of the load.</summary>
        public static readonly bool NoImpostors = Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-noimpostors") >= 0;
        /// <summary>Draw each impostor from its one nearest frame instead of blending three ("-impostoroneframe"; for the
        /// cost comparison: cheaper, but the frames step as the view turns).</summary>
        public static bool ImpostorOneFrame = Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-impostoroneframe") >= 0;
        /// <summary>Models baked per frame, so the load spreads over a few frames.</summary>
        public static int ImpostorBakesPerFrame = 12;
        /// <summary>The layers whose models get impostors (ground, shrub, tree). R-11: the tree layer too; its rule
        /// (DetailPolicy.FloraRule) trims the bake to the trees, so its buildings, debris and props stay on their meshes.</summary>
        static readonly bool[] ImpostorLayers = { false, true, true };
        /// <summary>Mip levels go down to frames this many texels a side, no smaller (they'd bleed into each other).</summary>
        const int ImpostorSmallestMip = 8;

        /// <summary>The last finished bake, for the world probe: "layer, models, frames, ms, MB".</summary>
        public static string LastImpostorLog = "";
        /// <summary>GPU memory all impostor atlases hold, in bytes.</summary>
        public static long ImpostorBytes;

        static readonly int ImpostorFramesId = Shader.PropertyToID("_ImpostorFrames");
        static readonly int ImpostorFullId = Shader.PropertyToID("_ImpostorFull");
        static readonly int ImpostorPixelsId = Shader.PropertyToID("_ImpostorPixels");
        static readonly int ImpostorSphereId = Shader.PropertyToID("_ImpostorSphere");
        static readonly int ImpostorAlbedoId = Shader.PropertyToID("_ImpostorAlbedo");
        static readonly int ImpostorDataId = Shader.PropertyToID("_ImpostorData");
        static readonly int ImpostorModelBaseId = Shader.PropertyToID("_ImpostorModelBase");
        static readonly int ImpostorSliceId = Shader.PropertyToID("_ImpostorSlice");
        static readonly int ImpostorSwayId = Shader.PropertyToID("_ImpostorSway");
        static readonly int ImpostorOneFrameId = Shader.PropertyToID("_PunchyImpostorOneFrame");

        /// <summary>A layer's impostors: its two atlases (one slice per model that can become an impostor) and how far
        /// the bake has got.</summary>
        class ImpostorAtlas
        {
            public RenderTexture Albedo, Data;
            public int Frames, Pixels, Mips;
            public bool Full;
            /// <summary>Each model's (local to the layer) atlas slice, or -1 for a model whose DetailPolicy rule gives it
            /// no impostor (it stays on its meshes, so it isn't baked): FloraImpostor.hlsl's _ImpostorSlice.</summary>
            public int[] Slices;
            /// <summary>The models baked, in slice order (local to the layer).</summary>
            public int[] Baked;
            public GraphicsBuffer SliceBuffer;
            /// <summary>R-11: each model's (local) wind sway at its bounding sphere's centre, as its meshes carry it per
            /// vertex (FloraMeshBuilder.Sway): FloraImpostor.hlsl's _ImpostorSway, so a far plant leans with the wind as
            /// its mesh does and the hand-off doesn't jump.</summary>
            public GraphicsBuffer SwayBuffer;
            /// <summary>The next slice to bake; the bake is done at Baked.Length.</summary>
            public int Next;
            public int BakeFrames;
            public double Ms, SlowestMs;
            public int Slowest;
            public long Bytes;
            /// <summary>The layer's draw material (a copy of FloraImpostor holding its atlases and buffers), made when
            /// the bake is done; null before.</summary>
            public Material Material;

            public void Release()
            {
                if (Material != null) UnityEngine.Object.Destroy(Material);
                Material = null;
                if (Albedo != null) { Albedo.Release(); UnityEngine.Object.Destroy(Albedo); }
                if (Data != null) { Data.Release(); UnityEngine.Object.Destroy(Data); }
                Albedo = Data = null;
                SliceBuffer?.Release();
                SliceBuffer = null;
                SwayBuffer?.Release();
                SwayBuffer = null;
                ImpostorBytes -= Bytes;
                Bytes = 0;
            }
        }

        Material impostorMaterial;
        /// <summary>The one quad every impostor is drawn with (corners at -1..1; the shader places and sizes it).</summary>
        Mesh impostorQuad;
        bool impostorLoaded;
        int impostorBakePass = -1;
        /// <summary>The bake's scratch targets (one model at a time, copied into its slice) and its command buffer.</summary>
        RenderTexture bakeAlbedo, bakeData, bakeDepth;
        CommandBuffer bakeCommands;
        MaterialPropertyBlock bakeProps;

        static float LaunchNumber(string arg, float fallback)
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var i = Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        /// <summary>The impostor material (Resources/FloraImpostor), loaded once; null if it's missing.</summary>
        Material ImpostorMaterial()
        {
            if (!impostorLoaded)
            {
                impostorLoaded = true;
                impostorMaterial = Resources.Load<Material>("FloraImpostor");
                impostorBakePass = impostorMaterial != null ? impostorMaterial.FindPass("ImpostorBake") : -1;
                if (impostorBakePass < 0)
                    Debug.LogWarning("[flora] impostors: no FloraImpostor material with an ImpostorBake pass; none baked");
            }
            return impostorBakePass >= 0 ? impostorMaterial : null;
        }

        /// <summary>Sets up an impostor layer's atlases at Build; BakeImpostors fills them over the next frames. Only the
        /// models that can become impostors (DetailPolicy.HasImpostor: in the shrubs, the plants, not the rocks, debris and
        /// structures) get a slice, so the atlases hold no frames nothing ever draws.</summary>
        void QueueImpostors(Layer layer)
        {
            if (NoImpostors || !ImpostorLayers[layer.Index] || layer.ModelCount == 0 || ImpostorMaterial() == null)
                return;
            var slices = new int[layer.ModelCount];
            var baked = new System.Collections.Generic.List<int>();
            for (int m = 0; m < layer.ModelCount; m++)
            {
                var rule = Punchy.Settings.DetailPolicy.FloraRule(models[layer.ModelBase + m].DetailClass());
                slices[m] = Punchy.Settings.DetailPolicy.HasImpostor(rule) ? baked.Count : -1;
                if (slices[m] >= 0)
                    baked.Add(m);
            }
            if (baked.Count == 0)
                return;
            var frames = math.clamp(ImpostorFrames, 2, 32);
            var pixels = math.clamp(ImpostorPixels, 8, 256);
            var size = frames * pixels;
            var mips = 1;
            while (pixels >> mips >= ImpostorSmallestMip)
                mips++;
            var atlas = new ImpostorAtlas { Frames = frames, Pixels = pixels, Mips = mips, Full = ImpostorFull, Slices = slices, Baked = baked.ToArray() };
            atlas.Albedo = ImpostorTarget($"Flora impostors {layer.Index} (albedo)", size, mips, atlas.Baked.Length);
            atlas.Data = ImpostorTarget($"Flora impostors {layer.Index} (data)", size, mips, atlas.Baked.Length);
            atlas.SliceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layer.ModelCount, sizeof(int));
            atlas.SliceBuffer.SetData(slices);
            var sway = new float[layer.ModelCount];
            for (int m = 0; m < layer.ModelCount; m++)
                sway[m] = ImpostorSway(models[layer.ModelBase + m]);
            atlas.SwayBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, layer.ModelCount, sizeof(float));
            atlas.SwayBuffer.SetData(sway);
            // Both RGBA8, each mip a quarter of the one above.
            long texels = 0;
            for (int m = 0; m < mips; m++)
                texels += (long)(size >> m) * (size >> m);
            atlas.Bytes = texels * 4 * 2 * atlas.Baked.Length;
            ImpostorBytes += atlas.Bytes;
            layer.Impostors = atlas;
        }

        /// <summary>A model's wind sway at its bounding sphere's centre: what FloraMeshBuilder.Sway gives a vertex at that
        /// height (0 for a model that never moves, and for built things, which stand rigid).</summary>
        static float ImpostorSway(FloraModel model)
        {
            var form = model.Species.Form;
            if (model.Static || (FloraGenerator.Founded(form) && form != FloraForm.Garden))
                return 0f;
            return model.Species.Flexibility * math.pow(math.saturate(model.CentreY / math.max(model.Height, 0.01f)), 1.6f);
        }

        static RenderTexture ImpostorTarget(string name, int size, int mips, int slices)
        {
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGB32, 0, mips)
            {
                dimension = slices > 0 ? TextureDimension.Tex2DArray : TextureDimension.Tex2D,
                volumeDepth = math.max(slices, 1),
                useMipMap = mips > 1,
                autoGenerateMips = false,
                sRGB = false
            };
            var rt = new RenderTexture(desc) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// Bakes the next few models of every impostor layer still baking (each model: one instanced draw of its LOD 0,
        /// one instance per frame of the grid, into the scratch targets; their mips; a copy into its slices).
        /// </summary>
        void BakeImpostors()
        {
            Shader.SetGlobalFloat(ImpostorOneFrameId, ImpostorOneFrame ? 1f : 0f);
            Material material = null;
            var budget = ImpostorBakesPerFrame;
            foreach (var layer in layers)
            {
                var atlas = layer?.Impostors;
                if (atlas == null || atlas.Next >= atlas.Baked.Length || budget <= 0)
                    continue;
                material ??= ImpostorMaterial();
                if (material == null)
                    return;
                var size = atlas.Frames * atlas.Pixels;
                if (bakeAlbedo == null || bakeAlbedo.width != size || bakeAlbedo.mipmapCount != atlas.Mips)
                {
                    ReleaseImpostorBake();
                    bakeAlbedo = ImpostorTarget("Impostor bake (albedo)", size, atlas.Mips, 0);
                    bakeData = ImpostorTarget("Impostor bake (data)", size, atlas.Mips, 0);
                    bakeDepth = new RenderTexture(size, size, 24, RenderTextureFormat.Depth) { name = "Impostor bake (depth)" };
                    bakeDepth.Create();
                    bakeCommands = new CommandBuffer { name = "Flora impostor bake" };
                    bakeProps = new MaterialPropertyBlock();
                }
                atlas.BakeFrames++;
                var colours = new RenderTargetIdentifier[] { bakeAlbedo, bakeData };
                for (; atlas.Next < atlas.Baked.Length && budget > 0; atlas.Next++, budget--)
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    var model = models[layer.ModelBase + atlas.Baked[atlas.Next]];
                    var mesh = model.Lods[0];
                    var cmd = bakeCommands;
                    cmd.Clear();
                    cmd.SetRenderTarget(colours, bakeDepth);
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    bakeProps.Clear();
                    bakeProps.SetFloat(ImpostorFramesId, atlas.Frames);
                    bakeProps.SetFloat(ImpostorFullId, atlas.Full ? 1f : 0f);
                    // (A hair past the bounding sphere, so its outermost points stay inside their tile.)
                    bakeProps.SetVector(ImpostorSphereId, new Vector4(model.CentreY, math.max(model.Radius, 0.01f) * 1.01f, 0f, 0f));
                    cmd.DrawMeshInstancedProcedural(mesh, 0, material, impostorBakePass, atlas.Frames * atlas.Frames, bakeProps);
                    if (atlas.Mips > 1)
                    {
                        cmd.GenerateMips(bakeAlbedo);
                        cmd.GenerateMips(bakeData);
                    }
                    for (int mip = 0; mip < atlas.Mips; mip++)
                    {
                        cmd.CopyTexture(bakeAlbedo, 0, mip, atlas.Albedo, atlas.Next, mip);
                        cmd.CopyTexture(bakeData, 0, mip, atlas.Data, atlas.Next, mip);
                    }
                    Graphics.ExecuteCommandBuffer(cmd);
                    var ms = clock.Elapsed.TotalMilliseconds;
                    atlas.Ms += ms;
                    if (ms > atlas.SlowestMs)
                    {
                        atlas.SlowestMs = ms;
                        atlas.Slowest = atlas.Next;
                    }
                    Debug.Log($"[flora] impostor bake layer {layer.Index} model {atlas.Next + 1}/{atlas.Baked.Length} {model.Species.name} " +
                              $"({mesh.vertexCount} vertices): {ms:F2} ms");
                }
                if (atlas.Next >= atlas.Baked.Length)
                {
                    var slowest = models[layer.ModelBase + atlas.Baked[atlas.Slowest]];
                    LastImpostorLog = $"layer {layer.Index}: {atlas.Baked.Length} of {layer.ModelCount} models, {atlas.Frames}x{atlas.Frames} frames of {atlas.Pixels} px " +
                                      $"({(atlas.Full ? "full" : "hemi")}-octahedral, {atlas.Mips} mips), {atlas.Ms:F1} ms over {atlas.BakeFrames} frames " +
                                      $"(mean {atlas.Ms / atlas.Baked.Length:F2} ms, slowest {atlas.SlowestMs:F2} ms {slowest.Species.name}), " +
                                      $"atlases {atlas.Bytes / (1024.0 * 1024.0):F1} MB, all impostors {ImpostorBytes / (1024.0 * 1024.0):F1} MB";
                    Debug.Log($"[flora] impostor bake done, {LastImpostorLog}");
                    StartImpostors(layer);
                }
            }
            // Nothing left to bake: the scratch targets go.
            if (budget == ImpostorBakesPerFrame && bakeAlbedo != null)
                ReleaseImpostorBake();
        }

        /// <summary>A layer's bake is done: its draw material, and its models' reaches to the cull (until now all 0, so
        /// the cull kept every plant on its meshes).</summary>
        void StartImpostors(Layer layer)
        {
            var atlas = layer.Impostors;
            atlas.Material = new Material(impostorMaterial) { name = $"Flora impostors {layer.Index}" };
            atlas.Material.SetTexture(ImpostorAlbedoId, atlas.Albedo);
            atlas.Material.SetTexture(ImpostorDataId, atlas.Data);
            atlas.Material.SetFloat(ImpostorFramesId, atlas.Frames);
            atlas.Material.SetFloat(ImpostorFullId, atlas.Full ? 1f : 0f);
            atlas.Material.SetFloat(ImpostorPixelsId, atlas.Pixels);
            atlas.Material.SetInteger(ImpostorModelBaseId, layer.ModelBase);
            atlas.Material.SetBuffer(ImpostorSliceId, atlas.SliceBuffer);
            atlas.Material.SetBuffer(ImpostorSwayId, atlas.SwayBuffer);
            SetImpostorBuffers(layer);
            // A model without a slice keeps reach 0, so the cull never hands it to the impostor draw.
            for (int m = 0; m < layer.ModelCount; m++)
            {
                var model = models[layer.ModelBase + m];
                layer.ImpostorReach[m] = atlas.Slices[m] < 0 ? 0f : Punchy.Settings.DetailPolicy.ImpostorReach(
                    Punchy.Settings.DetailPolicy.FloraRule(model.DetailClass()), model.Radius * 2f);
            }
            layer.ImpostorBuffer.SetData(layer.ImpostorReach);
        }

        /// <summary>The layer's plant buffers onto its impostor material (again whenever they're made anew).</summary>
        void SetImpostorBuffers(Layer layer)
        {
            var material = layer.Impostors?.Material;
            if (material == null || layer.Instances == null)
                return;
            material.SetBuffer(InstancesId, layer.Instances);
            material.SetBuffer(VisibleId, layer.Visible);
            material.SetBuffer(BoundsId, layer.Bounds);
        }

        Mesh ImpostorQuad()
        {
            if (impostorQuad == null)
            {
                impostorQuad = new Mesh { name = "Flora impostor quad", hideFlags = HideFlags.DontSave };
                impostorQuad.SetVertices(new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
                impostorQuad.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0);
                impostorQuad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            }
            return impostorQuad;
        }

        void ReleaseImpostorQuad()
        {
            if (impostorQuad != null)
                Destroy(impostorQuad);
            impostorQuad = null;
        }

        void ReleaseImpostorBake()
        {
            foreach (var rt in new[] { bakeAlbedo, bakeData, bakeDepth })
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            bakeAlbedo = bakeData = bakeDepth = null;
            bakeCommands?.Release();
            bakeCommands = null;
        }

        /// <summary>For the world probe's contact sheet: a layer's impostor atlas (albedo, or the data one) and its grid,
        /// null until the layer has impostors.</summary>
        public RenderTexture ImpostorAtlasOf(int layer, bool data, out int frames, out int pixels, out int baked)
        {
            var atlas = layer >= 0 && layer < layers.Length ? layers[layer]?.Impostors : null;
            frames = atlas?.Frames ?? 0;
            pixels = atlas?.Pixels ?? 0;
            baked = atlas?.Next ?? 0;
            return atlas == null ? null : data ? atlas.Data : atlas.Albedo;
        }

        /// <summary>The species name of a layer's model (local index), for labelling the probe's sheet.</summary>
        public string ModelNameOf(int layer, int model)
        {
            var l = layer >= 0 && layer < layers.Length ? layers[layer] : null;
            return l != null && model >= 0 && model < l.ModelCount ? models[l.ModelBase + model].Species.name : "";
        }
    }
}
