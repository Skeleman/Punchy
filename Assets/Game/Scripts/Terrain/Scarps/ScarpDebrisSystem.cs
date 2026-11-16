using Punchy.Flora;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Places, builds and draws the rubble round the scarps (W-155 row 2, <see cref="ScarpDebris"/>): whenever the
    /// island's scarps (or the flora rocks they keep clear of) change, the blocks are placed on a worker thread, then
    /// grouped per stretch of rim (big and small apart) and built into one mesh per group, drawn with the terrain's own
    /// material. Each group is drawn while its biggest block is big enough on screen to show (DetailPolicy.ScreenSize),
    /// so small stones go first as the view pulls back. Drawn only: nothing here changes the ground or collision.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScarpDebrisSystem : SystemBase
    {
        /// <summary>A group whose biggest block is smaller than this on screen (pixels) isn't drawn.</summary>
        const float MinPixels = 2.5f;

        public static string LastLog = "";

        BlobAssetReference<ScarpSetBlob> built;
        BlobAssetReference<FloraBlob> builtFlora;
        bool builtClay;
        Mesh[] meshes = System.Array.Empty<Mesh>();
        float[] sizes = System.Array.Empty<float>();
        bool[] small = System.Array.Empty<bool>(), shown = System.Array.Empty<bool>();

        // The build in flight: 1 placing, 2 building meshes.
        int stage;
        JobHandle handle;
        NativeList<ScarpBlock> blocks;
        NativeArray<int> groupStart, order;
        NativeArray<int2> ranges;
        Mesh.MeshDataArray data;
        int[] groupOf = System.Array.Empty<int>();
        System.Diagnostics.Stopwatch watch;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
            RequireForUpdate<TerrainConfig>();
            // #259: a world swap frees the terrain blobs before this system is destroyed; finish the job first.
            Punchy.Net.GameBootstrap.WorldsReplacing += Clear;
        }

        protected override void OnUpdate()
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            var config = SystemAPI.GetSingleton<TerrainConfig>();
            var flora = SystemAPI.TryGetSingleton<FloraLookup>(out var lookup) && lookup.Blob.IsCreated
                ? lookup.Blob : default;

            if (stage == 1 && handle.IsCompleted)
                BuildMeshes(source);
            else if (stage == 2 && handle.IsCompleted)
                Finish();
            if (stage == 0 && (source.Scarps != built || flora != builtFlora || ScarpClay.On != builtClay))
                Begin(source, flora);

            var material = config.Material.Value;
            var camera = Camera.main;
            if (material == null || camera == null)
                return;
            float ppm = Punchy.Settings.DetailPolicy.Current.Data.PixelsPerMetre;
            float3 eye = camera.transform.position;
            for (int i = 0; i < meshes.Length; i++)
            {
                var bounds = meshes[i].bounds;
                float distance = math.sqrt(bounds.SqrDistance(eye));
                shown[i] = Punchy.Settings.DetailPolicy.Holds(
                    Punchy.Settings.DetailPolicy.ScreenSize(sizes[i], math.max(distance, 1f), ppm), MinPixels, shown[i]);
                if (!shown[i])
                    continue;
                var draw = new RenderParams(material)
                {
                    worldBounds = bounds, receiveShadows = true,
                    shadowCastingMode = small[i] ? ShadowCastingMode.Off : ShadowCastingMode.On
                };
                Graphics.RenderMesh(draw, meshes[i], 0, Matrix4x4.identity);
            }
        }

        /// <summary>Starts placing the blocks on a worker thread.</summary>
        void Begin(in TerrainSource source, BlobAssetReference<FloraBlob> flora)
        {
            Clear();
            built = source.Scarps;
            builtFlora = flora;
            builtClay = ScarpClay.On;
            if (!built.IsCreated || built.Value.Scarps.Length == 0)
                return;
            watch = System.Diagnostics.Stopwatch.StartNew();
            blocks = new NativeList<ScarpBlock>(1024, Allocator.Persistent);
            groupStart = new NativeArray<int>(built.Value.Scarps.Length, Allocator.Persistent);
            handle = new ScarpDebrisPlaceJob
            {
                Source = source, Flora = flora, HasFlora = flora.IsCreated, Blocks = blocks, GroupStart = groupStart
            }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            stage = 1;
        }

        /// <summary>Sorts the placed blocks into their draw groups (dropping empty ones) and starts building one mesh
        /// per group.</summary>
        void BuildMeshes(in TerrainSource source)
        {
            handle.Complete();
            int groups = 0;
            for (int i = 0; i < built.Value.Scarps.Length; i++)
                groups += ScarpDebris.Groups(ref built.Value.Scarps[i]);
            var counts = new int[groups];
            for (int b = 0; b < blocks.Length; b++)
                counts[blocks[b].Group]++;
            // Only groups with blocks get a mesh.
            groupOf = new int[groups];
            int meshCount = 0;
            for (int g = 0; g < groups; g++)
                groupOf[g] = counts[g] > 0 ? meshCount++ : -1;
            if (meshCount == 0)
            {
                Log(0);
                DisposeBuild();
                stage = 0;
                return;
            }
            ranges = new NativeArray<int2>(meshCount, Allocator.Persistent);
            order = new NativeArray<int>(blocks.Length, Allocator.Persistent);
            var fill = new int[meshCount];
            int start = 0;
            for (int g = 0; g < groups; g++)
                if (groupOf[g] >= 0)
                {
                    ranges[groupOf[g]] = new int2(start, counts[g]);
                    start += counts[g];
                }
            for (int b = 0; b < blocks.Length; b++)
            {
                int m = groupOf[blocks[b].Group];
                order[ranges[m].x + fill[m]++] = b;
            }

            data = Mesh.AllocateWritableMeshData(meshCount);
            for (int m = 0; m < meshCount; m++)
            {
                var mesh = data[m];
                int n = ranges[m].y;
                mesh.SetVertexBufferParams(n * ScarpDebris.Vertices, TerrainVertex.Layout);
                mesh.SetIndexBufferParams(n * ScarpDebris.Indices, IndexFormat.UInt32);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, n * ScarpDebris.Indices),
                                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);   // the job fills them
            }
            handle = new ScarpDebrisMeshJob
            {
                Meshes = data, Blocks = blocks.AsArray(), Ranges = ranges, Order = order, Source = source, Clay = builtClay
            }.Schedule(meshCount, 1);
            JobHandle.ScheduleBatchedJobs();
            stage = 2;
        }

        void Finish()
        {
            handle.Complete();
            stage = 0;
            int count = ranges.Length;
            meshes = new Mesh[count];
            sizes = new float[count];
            small = new bool[count];
            shown = new bool[count];
            for (int m = 0; m < count; m++)
                meshes[m] = new Mesh { name = "Scarp debris " + m, hideFlags = HideFlags.DontSave };
            Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds);
            for (int m = 0; m < count; m++)
            {
                var range = ranges[m];
                float3 lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < range.y; k++)
                {
                    var b = blocks[order[range.x + k]];
                    // A rounded, roughened block stays inside 1.3 times its largest half size.
                    float reach = 1.3f * math.cmax(b.Half);
                    lo = math.min(lo, b.Position - reach);
                    hi = math.max(hi, b.Position + reach);
                    sizes[m] = math.max(sizes[m], 2f * math.cmax(b.Half));
                }
                small[m] = (blocks[order[range.x]].Group & 1) == 1;
                meshes[m].bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            }
            Log(count);
            DisposeBuild();
        }

        void Log(int meshCount)
        {
            LastLog = $"[scarps] {blocks.Length} debris blocks in {meshCount} groups; {watch.Elapsed.TotalMilliseconds:F0} ms off the main thread";
            Debug.Log(LastLog);
        }

        void DisposeBuild()
        {
            if (blocks.IsCreated) blocks.Dispose();
            if (groupStart.IsCreated) groupStart.Dispose();
            if (order.IsCreated) order.Dispose();
            if (ranges.IsCreated) ranges.Dispose();
        }

        void Clear()
        {
            if (stage != 0)
            {
                handle.Complete();
                if (stage == 2)
                    data.Dispose();
                stage = 0;
            }
            DisposeBuild();
            foreach (var mesh in meshes)
                Object.Destroy(mesh);
            meshes = System.Array.Empty<Mesh>();
            sizes = System.Array.Empty<float>();
            small = System.Array.Empty<bool>();
            shown = System.Array.Empty<bool>();
        }

        protected override void OnDestroy()
        {
            Punchy.Net.GameBootstrap.WorldsReplacing -= Clear;
            Clear();
        }
    }
}
