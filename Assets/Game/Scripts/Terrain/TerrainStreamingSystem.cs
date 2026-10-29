using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>Put this on the entity the terrain should stream around (the player).</summary>
    public struct TerrainFocus : IComponentData { }

    /// <summary>
    /// Keeps a disc of terrain chunks loaded around the TerrainFocus entity. It unloads chunks that fell out of
    /// range, then builds (or re-LODs) the nearest chunks that need it, up to MaxBuildsPerFrame and a vertex budget
    /// per batch. Each chunk has a terrain mesh and, where there is water, a water mesh; a batch is built by
    /// parallel Burst jobs in the background and picked up when it's done (a frame or two later), so building never
    /// stalls the main thread. Meshes are drawn by Entities Graphics.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class TerrainStreamingSystem : SystemBase
    {
        struct LoadedChunk
        {
            public Entity Entity;
            public int Lod;
            public Mesh Mesh;
            public BatchMeshID MeshId;
            public Entity WaterEntity;   // Entity.Null when the chunk is dry
            public Mesh WaterMesh;
            public BatchMeshID WaterMeshId;
        }

        struct BuildItem
        {
            public int2 Coord;
            public int Lod;
            public float Distance;
        }

        static readonly int TerrainFocusId = Shader.PropertyToID("_TerrainFocus");
        /// <summary>Vertices per batch (a full-detail chunk is about 4,500).</summary>
        const int VertexBudget = 14000;
        /// <summary>Frames a batch may run before the main thread waits for it.</summary>
        const int MaxBuildFrames = 4;
        /// <summary>Above this focus speed (m/s: gliding, riding) batches are a quarter the size (still at least one
        /// chunk), so a stream of chunks coming into range is meshed a little every frame instead of in bursts that
        /// crowd out everything else, and chunks ahead of the way it's heading come first.</summary>
        const float FastSpeed = 10f;
        float focusSpeed;
        /// <summary>The focus's smoothed horizontal heading (unit length, or zero when still).</summary>
        float2 focusHeading;
        float2 lastFocus = new float2(float.MaxValue);
        double lastFocusTime;

        readonly Dictionary<int2, LoadedChunk> loaded = new Dictionary<int2, LoadedChunk>();
        /// <summary>Whether far chunks are hidden for a camera deep in a cave (see HideFarChunks).</summary>
        bool surfaceHidden;
        /// <summary>Chunks within this distance of the camera keep drawing while the surface is hidden (the ground over a
        /// cave's own roof, which a shallow cave's walls meet).</summary>
        const float HiddenKeep = 60f;
        readonly Stack<(Mesh mesh, BatchMeshID id)> meshPool = new Stack<(Mesh, BatchMeshID)>();
        readonly List<int2> scratchCoords = new List<int2>();
        readonly List<BuildItem> wanted = new List<BuildItem>();
        readonly List<Mesh> buildMeshes = new List<Mesh>();
        readonly List<BatchMeshID> buildMeshIds = new List<BatchMeshID>();

        // The batch being built in the background.
        readonly List<BuildItem> building = new List<BuildItem>();
        JobHandle buildHandle;
        int buildFrames;

        /// <summary>True while a batch of chunks is meshing in the background, so other background work (the weather
        /// maps) can hold off rather than crowd it out.</summary>
        public static bool Meshing { get; private set; }
        Mesh.MeshDataArray buildData;
        NativeArray<ChunkBuildRequest> buildRequests;
        NativeArray<float2> buildRanges, buildWaterRanges;
        NativeArray<VertexAttributeDescriptor> buildWaterLayout;

        EntitiesGraphicsSystem graphics;
        BatchMaterialID materialId;
        BatchMaterialID waterMaterialId;
        TerrainConfig lastConfig;
        TerrainSource lastSource;
        bool initialized;
        EntityQuery focusQuery;

        static readonly RenderMeshDescription TerrainDesc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);
        static readonly RenderMeshDescription WaterDesc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: true);

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainConfig>();
            RequireForUpdate<TerrainSource>();
            focusQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainFocus>(), ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnDestroy()
        {
            CancelBuild();
            foreach (var chunk in loaded.Values)
            {
                Object.Destroy(chunk.Mesh);
                Object.Destroy(chunk.WaterMesh);
            }
            foreach (var pooled in meshPool)
                Object.Destroy(pooled.mesh);
            loaded.Clear();
            meshPool.Clear();
        }

        protected override void OnUpdate()
        {
            graphics ??= World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (graphics == null)
                return;

            var config = SystemAPI.GetSingleton<TerrainConfig>();
            var source = SystemAPI.GetSingleton<TerrainSource>();
            HandleSettingsChanges(config, source);
            if (materialId == BatchMaterialID.Null)
                return;

            // Pick up the batch being built once it's done (or it has had long enough).
            if (building.Count > 0)
            {
                if (!buildHandle.IsCompleted && ++buildFrames < MaxBuildFrames)
                    return;
                FinishBuild(config);
            }

            // Deep in a cave (CaveSystem.SurfaceHidden) the surface is out of sight: chunks beyond the camera's own
            // neighbourhood stop drawing, and nothing new streams in until the surface comes back into view.
            var hidden = CaveSystem.SurfaceHidden >= 0.99f;
            if (hidden != surfaceHidden)
                HideFarChunks(hidden);
            if (hidden)
                return;

            var focus = focusQuery.TryGetSingleton<LocalTransform>(out var focusTransform)
                ? focusTransform.Position.xz
                : float2.zero;
            // The terrain shader geomorphs vertices by their distance from this same point.
            Shader.SetGlobalVector(TerrainFocusId, new Vector4(focus.x, focus.y, 0f, 0f));

            // The focus's speed since the last batch started (batches can be frames apart), smoothed over about half a
            // second (a teleport reads as briefly fast).
            var now = SystemAPI.Time.ElapsedTime;
            var dt = math.max((float)(now - lastFocusTime), 1e-4f);
            var step = lastFocus.x == float.MaxValue ? float2.zero : focus - lastFocus;
            var moved = math.length(step);
            lastFocus = focus;
            lastFocusTime = now;
            focusSpeed = math.lerp(focusSpeed, moved / dt, 1f - math.exp(-dt * 2f));
            if (moved > 1e-3f && moved < 200f)
                focusHeading = math.normalizesafe(math.lerp(focusHeading, step / moved, 1f - math.exp(-dt * 3f)));
            var fast = focusSpeed > FastSpeed;
            var budget = fast ? VertexBudget / 4 : VertexBudget;

            UnloadDistantChunks(config, focus);
            CollectWantedChunks(config, focus);
            if (wanted.Count == 0)
                return;

            // Fast: chunks ahead count as up to 40% nearer and those behind up to 40% farther, so small batches go
            // where the view is heading first.
            if (fast && math.lengthsq(focusHeading) > 0.5f)
                for (int i = 0; i < wanted.Count; i++)
                {
                    var w = wanted[i];
                    var centre = ((float2)w.Coord + 0.5f) * config.ChunkSize;
                    var ahead = math.dot(math.normalizesafe(centre - focus), focusHeading);
                    w.Distance *= 1f - 0.4f * ahead;
                    wanted[i] = w;
                }
            wanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var count = 0;
            var vertices = 0;
            while (count < wanted.Count && count < config.MaxBuildsPerFrame)
            {
                var quads = config.QuadsForLod(wanted[count].Lod);
                vertices += BuildChunkMeshJob.VertexCount(quads);
                if (count > 0 && vertices > budget)
                    break;
                count++;
            }
            StartBuild(config, source, count);
        }

        void HandleSettingsChanges(in TerrainConfig config, in TerrainSource source)
        {
            // Settings change when the subscene re-bakes (e.g. editing the Terrain inspector during Play mode).
            if (!initialized || !config.Equals(lastConfig))
            {
                CancelBuild();
                UnloadAll();
                var material = config.Material.Value;
                materialId = material != null ? graphics.RegisterMaterial(material) : BatchMaterialID.Null;
                if (material == null)
                    Debug.LogWarning("TerrainAuthoring has no Material assigned; terrain will not render.");
                var waterMaterial = config.WaterMaterial.Value;
                waterMaterialId = waterMaterial != null ? graphics.RegisterMaterial(waterMaterial) : BatchMaterialID.Null;
            }
            else if (!source.Equals(lastSource))
            {
                CancelBuild();
                // Same layout, new shape: flag every chunk for rebuild. Nearest ones are rebuilt first.
                scratchCoords.Clear();
                scratchCoords.AddRange(loaded.Keys);
                foreach (var coord in scratchCoords)
                {
                    var chunk = loaded[coord];
                    chunk.Lod = -1;
                    loaded[coord] = chunk;
                }
            }

            lastConfig = config;
            lastSource = source;
            initialized = true;
        }

        void UnloadDistantChunks(in TerrainConfig config, float2 focus)
        {
            // Unload a bit beyond the load radius so chunks on the boundary don't thrash.
            var unloadRadius = (config.ViewRadiusChunks + 1.5f) * config.ChunkSize;
            scratchCoords.Clear();
            foreach (var coord in loaded.Keys)
            {
                var center = ((float2)coord + 0.5f) * config.ChunkSize;
                if (math.distance(center, focus) > unloadRadius)
                    scratchCoords.Add(coord);
            }

            foreach (var coord in scratchCoords)
                Unload(coord);
        }

        void CollectWantedChunks(in TerrainConfig config, float2 focus)
        {
            wanted.Clear();
            var size = config.ChunkSize;
            var radius = config.ViewRadiusChunks * size;
            var r = config.ViewRadiusChunks;
            var centerCoord = (int2)math.floor(focus / size);

            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                var coord = centerCoord + new int2(dx, dz);
                var min = (float2)coord * size;
                if (math.distance(min + size * 0.5f, focus) > radius)
                    continue;

                var distance = math.distance(focus, math.clamp(focus, min, min + size));
                var lod = config.LodForDistance(distance);
                if (loaded.TryGetValue(coord, out var existing) && existing.Lod == lod)
                    continue;

                wanted.Add(new BuildItem { Coord = coord, Lod = lod, Distance = distance });
            }
        }

        /// <summary>Starts building the first <paramref name="count"/> wanted chunks in the background.</summary>
        void StartBuild(in TerrainConfig config, in TerrainSource source, int count)
        {
            // Terrain meshes at [0, count), water meshes at [count, 2·count).
            buildData = Mesh.AllocateWritableMeshData(count * 2);
            buildRequests = new NativeArray<ChunkBuildRequest>(count, Allocator.Persistent);
            buildRanges = new NativeArray<float2>(count, Allocator.Persistent);
            buildWaterRanges = new NativeArray<float2>(count, Allocator.Persistent);
            var layout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.Temp);
            buildWaterLayout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.Persistent);
            building.Clear();

            for (int i = 0; i < count; i++)
            {
                var item = wanted[i];
                building.Add(item);
                var quads = config.QuadsForLod(item.Lod);
                var indexCount = BuildChunkMeshJob.IndexCount(quads);
                buildRequests[i] = new ChunkBuildRequest
                {
                    Origin = (float2)item.Coord * config.ChunkSize,
                    Quads = quads,
                    Lod = item.Lod,
                    CanMorph = item.Lod < config.LodLevels - 1 && quads > 1,
                    MorphRange = config.MorphRange(item.Lod)
                };

                var data = buildData[i];
                data.SetVertexBufferParams(BuildChunkMeshJob.VertexCount(quads), layout);
                data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshFlags);
            }

            var job = new BuildChunkMeshJob
            {
                Meshes = buildData,
                Requests = buildRequests,
                WaterLayout = buildWaterLayout,
                Source = source,
                ChunkSize = config.ChunkSize,
                NormalEpsilon = config.ChunkSize / config.Lod0Quads,
                SkirtDepth = config.SkirtDepth,
                HeightRanges = buildRanges,
                WaterRanges = buildWaterRanges
            };
            buildHandle = IJobParallelForExtensions.Schedule(job, count, 1);
            buildFrames = 0;
            Meshing = true;
            JobHandle.ScheduleBatchedJobs();
        }

        const MeshUpdateFlags MeshFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        /// <summary>Applies a finished batch: fills the chunks' meshes and creates or updates their entities.</summary>
        void FinishBuild(in TerrainConfig config)
        {
            buildHandle.Complete();
            Meshing = false;
            var count = building.Count;

            // Rebuilt chunks reuse their own meshes; new chunks take them from the pool.
            buildMeshes.Clear();
            buildMeshIds.Clear();
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                var (mesh, id) = loaded.TryGetValue(building[i].Coord, out var existing)
                    ? pass == 0 ? (existing.Mesh, existing.MeshId) : (existing.WaterMesh, existing.WaterMeshId)
                    : RentMesh();
                buildMeshes.Add(mesh);
                buildMeshIds.Add(id);
            }
            Mesh.ApplyAndDisposeWritableMeshData(buildData, buildMeshes, MeshFlags);

            for (int i = 0; i < count; i++)
            {
                var item = building[i];
                var origin = buildRequests[i].Origin;
                var terrainBounds = SetBounds(buildMeshes[i], buildRanges[i], config.ChunkSize);
                var hasWater = buildWaterRanges[i].x <= buildWaterRanges[i].y;
                var waterBounds = hasWater ? SetBounds(buildMeshes[count + i], buildWaterRanges[i], config.ChunkSize) : default;

                if (!loaded.TryGetValue(item.Coord, out var chunk))
                {
                    chunk = new LoadedChunk
                    {
                        Entity = CreateRenderEntity(item.Coord, origin, TerrainDesc, materialId, buildMeshIds[i], "TerrainChunk"),
                        Mesh = buildMeshes[i],
                        MeshId = buildMeshIds[i],
                        WaterMesh = buildMeshes[count + i],
                        WaterMeshId = buildMeshIds[count + i]
                    };
                    EntityManager.AddComponentData(chunk.Entity, new TerrainChunk { Coord = item.Coord, Lod = item.Lod });
                }
                else
                {
                    EntityManager.SetComponentData(chunk.Entity, new TerrainChunk { Coord = item.Coord, Lod = item.Lod });
                    EntityManager.SetComponentData(chunk.Entity, new RenderBounds { Value = terrainBounds });
                }
                chunk.Lod = item.Lod;

                // Water entities come and go as a chunk's water appears or disappears.
                var canDrawWater = hasWater && waterMaterialId != BatchMaterialID.Null;
                if (canDrawWater && chunk.WaterEntity == Entity.Null)
                    chunk.WaterEntity = CreateRenderEntity(item.Coord, origin, WaterDesc, waterMaterialId, chunk.WaterMeshId, "WaterChunk");
                else if (canDrawWater)
                    EntityManager.SetComponentData(chunk.WaterEntity, new RenderBounds { Value = waterBounds });
                else if (chunk.WaterEntity != Entity.Null)
                {
                    EntityManager.DestroyEntity(chunk.WaterEntity);
                    chunk.WaterEntity = Entity.Null;
                }

                loaded[item.Coord] = chunk;
            }
            DisposeBuild();
        }

        /// <summary>Abandons the batch being built (settings changed, or shutting down).</summary>
        void CancelBuild()
        {
            if (building.Count == 0)
                return;
            buildHandle.Complete();
            Meshing = false;
            buildData.Dispose();
            DisposeBuild();
        }

        void DisposeBuild()
        {
            building.Clear();
            buildRequests.Dispose();
            buildRanges.Dispose();
            buildWaterRanges.Dispose();
            buildWaterLayout.Dispose();
        }

        static AABB SetBounds(Mesh mesh, float2 heightRange, float size)
        {
            var aabb = new AABB
            {
                Center = new float3(size * 0.5f, (heightRange.x + heightRange.y) * 0.5f, size * 0.5f),
                Extents = new float3(size * 0.5f, math.max((heightRange.y - heightRange.x) * 0.5f, 0.5f), size * 0.5f)
            };
            mesh.bounds = new Bounds(aabb.Center, aabb.Extents * 2f);
            return aabb;
        }

        (Mesh mesh, BatchMeshID id) RentMesh()
        {
            if (meshPool.Count > 0)
                return meshPool.Pop();

            var mesh = new Mesh { name = "TerrainChunk" };
            mesh.MarkDynamic();
            return (mesh, graphics.RegisterMesh(mesh));
        }

        /// <summary>
        /// Hides (or shows again) the chunks farther than HiddenKeep from the camera, for a camera deep in a cave
        /// (DisableRendering on their entities).
        /// </summary>
        void HideFarChunks(bool hide)
        {
            surfaceHidden = hide;
            var cam = Camera.main;
            var eye = cam != null ? ((float3)cam.transform.position).xz : float2.zero;
            var chunkSize = SystemAPI.GetSingleton<TerrainConfig>().ChunkSize;
            foreach (var kv in loaded)
            {
                var centre = ((float2)kv.Key + 0.5f) * chunkSize;
                var far = math.distance(centre, eye) > HiddenKeep + chunkSize * 0.71f;
                foreach (var e in new[] { kv.Value.Entity, kv.Value.WaterEntity })
                {
                    if (e == Entity.Null)
                        continue;
                    var off = EntityManager.HasComponent<DisableRendering>(e);
                    if (hide && far && !off)
                        EntityManager.AddComponent<DisableRendering>(e);
                    else if ((!hide || !far) && off)
                        EntityManager.RemoveComponent<DisableRendering>(e);
                }
            }
        }

        Entity CreateRenderEntity(int2 coord, float2 origin, in RenderMeshDescription desc, BatchMaterialID material, BatchMeshID mesh, string name)
        {
            var transform = LocalTransform.FromPosition(origin.x, 0f, origin.y);
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, transform);
            EntityManager.AddComponentData(entity, new LocalToWorld { Value = transform.ToMatrix() });
            RenderMeshUtility.AddComponents(entity, EntityManager, desc, new MaterialMeshInfo(material, mesh));
#if UNITY_EDITOR
            // One name per kind: the editor only has room for a few thousand distinct entity names, and streaming
            // creates chunks without end.
            EntityManager.SetName(entity, name);
#endif
            return entity;
        }

        void Unload(int2 coord)
        {
            var chunk = loaded[coord];
            EntityManager.DestroyEntity(chunk.Entity);
            if (chunk.WaterEntity != Entity.Null)
                EntityManager.DestroyEntity(chunk.WaterEntity);
            meshPool.Push((chunk.Mesh, chunk.MeshId));
            meshPool.Push((chunk.WaterMesh, chunk.WaterMeshId));
            loaded.Remove(coord);
        }

        void UnloadAll()
        {
            scratchCoords.Clear();
            scratchCoords.AddRange(loaded.Keys);
            foreach (var coord in scratchCoords)
                Unload(coord);
        }
    }
}
