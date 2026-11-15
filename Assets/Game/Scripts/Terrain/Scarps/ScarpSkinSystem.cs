using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds and draws the scarp skins (W-155): one mesh per scarp, built off the main thread whenever the island's
    /// scarps change, drawn with the terrain's own material so the rock on the face is the rock of the ground round
    /// it. A skin is drawn while the scarp is big enough on screen to show (DetailPolicy.ScreenSize); the ground
    /// under it is always the true stepped height field, so collision and far tiles need nothing from here.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ScarpSkinSystem : SystemBase
    {
        /// <summary>A scarp shorter than this on screen (pixels) isn't drawn as a skin.</summary>
        const float MinPixels = 3f;

        public static string LastLog = "";

        BlobAssetReference<ScarpSetBlob> built, building;
        Mesh[] meshes = System.Array.Empty<Mesh>();
        bool[] shown = System.Array.Empty<bool>();
        Mesh.MeshDataArray data;
        JobHandle handle;
        bool running, builtClay;
        System.Diagnostics.Stopwatch watch;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
            RequireForUpdate<TerrainConfig>();
        }

        protected override void OnUpdate()
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            var config = SystemAPI.GetSingleton<TerrainConfig>();

            if (running && handle.IsCompleted)
                Finish();
            if (!running && (source.Scarps != built || ScarpClay.On != builtClay))
                Begin(source, config);

            var material = config.Material.Value;
            var camera = Camera.main;
            if (material == null || camera == null || !built.IsCreated || meshes.Length != built.Value.Scarps.Length)
                return;
            float ppm = Punchy.Settings.DetailPolicy.Current.Data.PixelsPerMetre;
            float3 eye = camera.transform.position;
            for (int i = 0; i < meshes.Length; i++)
            {
                ref var sc = ref built.Value.Scarps[i];
                var bounds = meshes[i].bounds;
                float distance = math.sqrt(bounds.SqrDistance(eye));
                shown[i] = Punchy.Settings.DetailPolicy.Holds(
                    Punchy.Settings.DetailPolicy.ScreenSize(sc.MaxDrop, math.max(distance, 1f), ppm), MinPixels, shown[i]);
                if (!shown[i])
                    continue;
                var draw = new RenderParams(material)
                {
                    worldBounds = bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true
                };
                Graphics.RenderMesh(draw, meshes[i], 0, Matrix4x4.identity);
            }
        }

        /// <summary>Starts the skins' build on worker threads; <see cref="Finish"/> takes the meshes when it's done.</summary>
        void Begin(in TerrainSource source, in TerrainConfig config)
        {
            Clear();
            built = source.Scarps;
            builtClay = ScarpClay.On;
            if (!built.IsCreated || built.Value.Scarps.Length == 0)
                return;
            int count = built.Value.Scarps.Length;
            data = Mesh.AllocateWritableMeshData(count);
            for (int i = 0; i < count; i++)
            {
                ref var sc = ref built.Value.Scarps[i];
                var mesh = data[i];
                mesh.SetVertexBufferParams(ScarpSkinJob.VertexCount(ref sc), TerrainVertex.Layout);
                mesh.SetIndexBufferParams(ScarpSkinJob.IndexCount(ref sc), IndexFormat.UInt32);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, ScarpSkinJob.IndexCount(ref sc)),
                                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);   // the job fills them
            }
            watch = System.Diagnostics.Stopwatch.StartNew();
            building = built;
            handle = new ScarpSkinJob
            {
                Meshes = data, Source = source, NormalEpsilon = config.ChunkSize / config.Lod0Quads, Clay = builtClay
            }.Schedule(count, 1);
            JobHandle.ScheduleBatchedJobs();
            running = true;
        }

        void Finish()
        {
            handle.Complete();
            running = false;
            int count = building.Value.Scarps.Length, vertices = 0;
            meshes = new Mesh[count];
            shown = new bool[count];
            for (int i = 0; i < count; i++)
                meshes[i] = new Mesh { name = "Scarp skin " + i, hideFlags = HideFlags.DontSave };
            Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds);
            for (int i = 0; i < count; i++)
            {
                ref var sc = ref building.Value.Scarps[i];
                var size = new Vector3(sc.Bounds.z - sc.Bounds.x, sc.MaxY - sc.MinY + 4f, sc.Bounds.w - sc.Bounds.y);
                meshes[i].bounds = new Bounds(
                    new Vector3(0.5f * (sc.Bounds.x + sc.Bounds.z), 0.5f * (sc.MinY + sc.MaxY), 0.5f * (sc.Bounds.y + sc.Bounds.w)), size);
                vertices += meshes[i].vertexCount;
            }
            LastLog = $"[scarps] {count} skins, {vertices} vertices; {watch.Elapsed.TotalMilliseconds:F0} ms off the main thread";
            Debug.Log(LastLog);
        }

        void Clear()
        {
            if (running)
            {
                handle.Complete();
                data.Dispose();
                running = false;
            }
            foreach (var mesh in meshes)
                Object.Destroy(mesh);
            meshes = System.Array.Empty<Mesh>();
            shown = System.Array.Empty<bool>();
        }

        protected override void OnDestroy() => Clear();
    }
}
