using Punchy.Settings;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Flora
{
    /// <summary>
    /// R-5: the flora's part of the wide sun-shadow map (Rendering.WideShadow). Which plants cast into it is the
    /// DetailPolicy size-on-screen rule, not a list: a model whose rule has a wide tier (trees and built things,
    /// DetailPolicy.WideReach) casts while it is bigger on screen than the rule's WideShadow, per plant and with the
    /// same 15% hysteresis as the camera's cull (FloraCull.compute's WideCull). So a tall tree on a ridge or a big hall
    /// keeps a far shadow and a shrub never does. WideShadow culls once when it starts a redraw and then draws a slice
    /// of the models each frame, with their coarsest mesh, into its back texture.
    /// </summary>
    public partial class FloraSystem
    {
        static readonly int ModelWideId = Shader.PropertyToID("_ModelWide");
        static readonly int WideStateId = Shader.PropertyToID("_FloraWideState");
        static readonly int WideId = Shader.PropertyToID("_Wide");

        /// <summary>Debug: the next WideCull reads its counts back for <see cref="WideKinds"/> (a GPU sync; probes only).</summary>
        public static bool CountKinds;
        /// <summary>Plants in the last wide cull by kind, when <see cref="CountKinds"/> was set (e.g. "Tree 812, Structure 40").</summary>
        public static string WideKinds = "";

        /// <summary>A layer's wide-map cull: its own visible list and counts, so the camera's draws are untouched.</summary>
        sealed class WideLayer
        {
            public GraphicsBuffer Visible, Counts, Args, State, ModelWide;
            public GraphicsBuffer.IndirectDrawIndexedArgs[] Data;
            public MaterialPropertyBlock[] Blocks;
            public uint[] Bases;
            // The layer's models that can cast into the wide map at all (a wide tier and shadows on); the rest are never
            // drawn, as even an empty indirect draw costs the GPU a few microseconds.
            public int[] Models;
            public bool Any;
            // Of those, the ones the last cull left plants in, read back from the GPU without a stall
            // (AsyncGPUReadback); until it lands every wide model is drawn.
            public int[] Live;
            public int LiveCount, Pending;
            public bool LiveReady;
            public System.Action<AsyncGPUReadbackRequest> OnCounts;
            public GraphicsBuffer For;
            public int ForCount, ForUploads = -1, ForBase = -1;

            public void Release()
            {
                Visible?.Release();
                Counts?.Release();
                Args?.Release();
                State?.Release();
                ModelWide?.Release();
                Visible = Counts = Args = State = ModelWide = null;
                For = null;
                LiveReady = false;
                ForUploads = -1;
            }
        }

        readonly WideLayer[] wide = new WideLayer[3];

        /// <summary>Changes whenever a layer's plants are uploaded again (the wide map redraws for new plants).</summary>
        public int WideVersion
        {
            get
            {
                int v = 0;
                foreach (var l in layers)
                    if (l != null)
                        v += l.Uploads;
                return v;
            }
        }

        /// <summary>
        /// Picks the plants that cast into the wide map round eye (out to reach, m; none nearer than skipNear, where the
        /// real-time shadows and the flora cache already have them) and keeps them for <see cref="WideDraw"/>.
        /// </summary>
        public void WideCull(float3 eye, float reach, float skipNear)
        {
            ref readonly var view = ref DetailPolicy.Current.Data;
            var reachShadow = view.PixelsPerMetre * math.max(view.Shadows, 0.1f);
            var kernel = Culling.FindKernel("WideCull");
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var w = l == null ? null : PrepareWide(i, l);
                if (w == null)
                    continue;
                w.For = l.Instances;
                w.ForCount = l.Count;
                w.ForUploads = l.Uploads;
                for (int d = 0; d < w.Data.Length; d++)
                {
                    w.Data[d] = l.ArgsData[d];
                    w.Data[d].instanceCount = 0;
                    w.Data[d].startInstance = 0;
                }
                for (int m = 0; m < l.ModelCount; m++)
                    w.Bases[m] = l.ArgsData[m * Draws + 2].startInstance;
                w.Counts.SetData(w.Data);
                var skip = l.Index == CacheLayer && Punchy.Rendering.FloraShadowOptions.CachedFarShadows ? math.min(skipNear, Punchy.Rendering.FloraShadowOptions.CacheRange) : 0f;
                Culling.SetInt(CountId, l.Count);
                Culling.SetInt(ModelBaseId, l.ModelBase);
                Culling.SetVector(CameraId, (Vector3)eye);
                Culling.SetVector(WideId, new Vector4(reachShadow, skip, reach, 0f));
                Culling.SetBuffer(kernel, InstancesId, l.Instances);
                Culling.SetBuffer(kernel, VisibleId, w.Visible);
                Culling.SetBuffer(kernel, ArgsId, w.Counts);
                Culling.SetBuffer(kernel, RegionsId, l.Regions);
                Culling.SetBuffer(kernel, ModelWideId, w.ModelWide);
                Culling.SetBuffer(kernel, WideStateId, w.State);
                Culling.Dispatch(kernel, (l.Count + 63) / 64, 1, 1);
                Graphics.CopyBuffer(w.Counts, w.Args);
                w.LiveReady = false;
                w.Pending++;
                AsyncGPUReadback.Request(w.Counts, w.OnCounts);
            }
            if (CountKinds)
                ReadWideKinds();
        }

        /// <summary>Whether the last WideCull's counts are back, so WideDraw skips the models it left empty.</summary>
        public bool WideReady()
        {
            foreach (var w in wide)
                if (w != null && w.Any && w.ForUploads >= 0 && !w.LiveReady)
                    return false;
            return true;
        }

        /// <summary>Keeps the wide models the cull left plants in (the newest cull's readback only).</summary>
        static void TakeCounts(WideLayer w, AsyncGPUReadbackRequest request)
        {
            if (--w.Pending > 0 || w.Models == null)
                return;
            if (request.hasError || w.Counts == null)
            {
                w.LiveCount = w.Models.Length;
                System.Array.Copy(w.Models, w.Live, w.LiveCount);
            }
            else
            {
                var data = request.GetData<GraphicsBuffer.IndirectDrawIndexedArgs>();
                w.LiveCount = 0;
                foreach (var m in w.Models)
                    if (m * Draws + 2 < data.Length && data[m * Draws + 2].instanceCount > 0)
                        w.Live[w.LiveCount++] = m;
            }
            w.LiveReady = true;
        }

        /// <summary>Whether a layer re-uploaded since the last WideCull (its visible list then points at old plants).</summary>
        public bool WideStale()
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var w = wide[i];
                if (l != null && w != null && w.Any && w.ForUploads >= 0 &&
                    (l.Instances != w.For || l.Count != w.ForCount || l.Uploads != w.ForUploads))
                    return true;
            }
            return false;
        }

        /// <summary>Draws slice step of steps of the culled plants with the wide map's globals already set on cmd.</summary>
        public void WideDraw(CommandBuffer cmd, int step, int steps)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var w = wide[i];
                if (l == null || w == null || !w.Any || w.ForUploads != l.Uploads || l.Material == null)
                    continue;
                var pass = l.Material.FindPass("FloraWide");
                if (pass < 0)
                    continue;
                var list = w.LiveReady ? w.Live : w.Models;
                var n = w.LiveReady ? w.LiveCount : w.Models.Length;
                var from = n * step / steps;
                var to = n * (step + 1) / steps;
                for (int k = from; k < to; k++)
                {
                    var m = list[k];
                    var model = models[l.ModelBase + m];
                    if (l.ModelCounts[m] == 0 || !model.CastsShadows || model.Lods == null || model.Lods.Length < 3)
                        continue;
                    var block = w.Blocks[m] ??= new MaterialPropertyBlock();
                    block.SetBuffer(VisibleId, w.Visible);
                    block.SetInteger(CacheBaseId, (int)w.Bases[m]);
                    cmd.DrawMeshInstancedIndirect(model.Lods[2], 0, l.Material, pass, w.Args,
                        (m * Draws + 2) * GraphicsBuffer.IndirectDrawIndexedArgs.size, block);
                }
            }
        }

        /// <summary>GPU memory the wide cull holds (its visible lists, state and arguments), in bytes.</summary>
        public long WideBytes()
        {
            long b = 0;
            foreach (var w in wide)
                if (w != null && w.Visible != null)
                    b += (long)w.Visible.count * 8 + (long)w.State.count * 4 + 2L * w.Data.Length * GraphicsBuffer.IndirectDrawIndexedArgs.size;
            return b;
        }

        /// <summary>Sizes a layer's wide buffers to its current upload; null when none of its models has a wide tier.</summary>
        WideLayer PrepareWide(int i, Layer l)
        {
            if (l.Count == 0 || l.ModelCount == 0 || l.Visible == null || l.CastState == null)
                return null;
            var w = wide[i] ??= new WideLayer();
            var draws = l.ModelCount * Draws;
            if (w.ModelWide == null || w.ModelWide.count != l.ModelCount || w.ForBase != l.ModelBase)
            {
                w.ModelWide?.Release();
                var reach = new float[l.ModelCount];
                var casting = new System.Collections.Generic.List<int>();
                for (int m = 0; m < l.ModelCount; m++)
                {
                    var model = models[l.ModelBase + m];
                    reach[m] = model.CastsShadows ? DetailPolicy.WideReach(DetailPolicy.FloraRule(model.DetailClass()), model.Radius * 2f) : 0f;
                    if (reach[m] > 0f)
                        casting.Add(m);
                }
                w.Models = casting.ToArray();
                w.Live = new int[w.Models.Length];
                w.LiveReady = false;
                var layer = w;
                w.OnCounts ??= request => TakeCounts(layer, request);
                w.ForBase = l.ModelBase;
                w.Any = w.Models.Length > 0;
                w.ModelWide = new GraphicsBuffer(GraphicsBuffer.Target.Structured, l.ModelCount, sizeof(float));
                w.ModelWide.SetData(reach);
            }
            if (!w.Any)
                return null;
            if (w.Visible == null || w.Visible.count != l.Visible.count)
            {
                w.Visible?.Release();
                w.Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, l.Visible.count, sizeof(uint) * 2);
            }
            if (w.State == null || w.State.count != l.CastState.count)
            {
                w.State?.Release();
                w.State = new GraphicsBuffer(GraphicsBuffer.Target.Structured, l.CastState.count, sizeof(uint));
                w.State.SetData(new uint[l.CastState.count]);
            }
            if (w.Counts == null || w.Counts.count != draws)
            {
                w.Counts?.Release();
                w.Args?.Release();
                w.Counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                w.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                w.Data = new GraphicsBuffer.IndirectDrawIndexedArgs[draws];
                w.Blocks = new MaterialPropertyBlock[l.ModelCount];
                w.Bases = new uint[l.ModelCount];
            }
            return w;
        }

        /// <summary>Debug: the plants in the last wide cull by DetailPolicy kind (reads the counts back: a GPU sync).</summary>
        void ReadWideKinds()
        {
            var counts = new int[5];
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                var w = wide[i];
                if (l == null || w == null || !w.Any || w.ForUploads != l.Uploads)
                    continue;
                var data = new GraphicsBuffer.IndirectDrawIndexedArgs[w.Data.Length];
                w.Counts.GetData(data);
                for (int m = 0; m < l.ModelCount; m++)
                    counts[(int)models[l.ModelBase + m].DetailClass()] += (int)data[m * Draws + 2].instanceCount;
            }
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < counts.Length; k++)
                if (counts[k] > 0)
                    sb.Append(sb.Length > 0 ? ", " : "").Append((DetailPolicy.FloraClass)k).Append(' ').Append(counts[k]);
            WideKinds = sb.Length > 0 ? sb.ToString() : "none";
        }

        void ReleaseWide()
        {
            for (int i = 0; i < wide.Length; i++)
            {
                wide[i]?.Release();
                wide[i] = null;
            }
        }
    }
}
