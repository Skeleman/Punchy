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
    /// Keeps a disc of terrain chunks loaded around the TerrainFocus entity. Each frame it unloads
    /// chunks that fell out of range, then builds (or re-LODs) the nearest chunks that need it,
    /// up to MaxBuildsPerFrame. Each chunk has a terrain mesh and, where there is water, a water mesh;
    /// both are built in parallel Burst jobs and drawn by Entities Graphics.
    /// </summary>
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

        readonly Dictionary<int2, LoadedChunk> loaded = new Dictionary<int2, LoadedChunk>();
        readonly Stack<(Mesh mesh, BatchMeshID id)> meshPool = new Stack<(Mesh, BatchMeshID)>();
        readonly List<int2> scratchCoords = new List<int2>();
        readonly List<BuildItem> wanted = new List<BuildItem>();
        readonly List<Mesh> buildMeshes = new List<Mesh>();
        readonly List<BatchMeshID> buildMeshIds = new List<BatchMeshID>();

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

            var focus = focusQuery.TryGetSingleton<LocalTransform>(out var focusTransform)
                ? focusTransform.Position.xz
                : float2.zero;
            // The terrain shader geomorphs vertices by their distance from this same point.
            Shader.SetGlobalVector(TerrainFocusId, new Vector4(focus.x, focus.y, 0f, 0f));

            UnloadDistantChunks(config, focus);
            CollectWantedChunks(config, focus);
            if (wanted.Count == 0)
                return;

            wanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            BuildChunks(config, source, math.min(wanted.Count, config.MaxBuildsPerFrame));
        }

        void HandleSettingsChanges(in TerrainConfig config, in TerrainSource source)
        {
            // Settings change when the subscene re-bakes (e.g. editing the Terrain inspector during Play mode).
            if (!initialized || !config.Equals(lastConfig))
            {
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

        void BuildChunks(in TerrainConfig config, in TerrainSource source, int count)
        {
            // Terrain meshes at [0, count), water meshes at [count, 2·count).
            var meshData = Mesh.AllocateWritableMeshData(count * 2);
            var requests = new NativeArray<ChunkBuildRequest>(count, Allocator.TempJob);
            var ranges = new NativeArray<float2>(count, Allocator.TempJob);
            var waterRanges = new NativeArray<float2>(count, Allocator.TempJob);
            var layout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.Temp);
            var waterLayout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.TempJob);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

            for (int i = 0; i < count; i++)
            {
                var item = wanted[i];
                var quads = config.QuadsForLod(item.Lod);
                var indexCount = BuildChunkMeshJob.IndexCount(quads);
                requests[i] = new ChunkBuildRequest
                {
                    Origin = (float2)item.Coord * config.ChunkSize,
                    Quads = quads,
                    Lod = item.Lod,
                    CanMorph = item.Lod < config.LodLevels - 1 && quads > 1,
                    MorphRange = config.MorphRange(item.Lod)
                };

                var data = meshData[i];
                data.SetVertexBufferParams(BuildChunkMeshJob.VertexCount(quads), layout);
                data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), flags);
            }

            var job = new BuildChunkMeshJob
            {
                Meshes = meshData,
                Requests = requests,
                WaterLayout = waterLayout,
                Source = source,
                ChunkSize = config.ChunkSize,
                NormalEpsilon = config.ChunkSize / config.Lod0Quads,
                SkirtDepth = config.SkirtDepth,
                HeightRanges = ranges,
                WaterRanges = waterRanges
            };
            IJobParallelForExtensions.Schedule(job, count, 1).Complete();

            // Rebuilt chunks reuse their own meshes; new chunks take them from the pool.
            buildMeshes.Clear();
            buildMeshIds.Clear();
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                var (mesh, id) = loaded.TryGetValue(wanted[i].Coord, out var existing)
                    ? pass == 0 ? (existing.Mesh, existing.MeshId) : (existing.WaterMesh, existing.WaterMeshId)
                    : RentMesh();
                buildMeshes.Add(mesh);
                buildMeshIds.Add(id);
            }
            Mesh.ApplyAndDisposeWritableMeshData(meshData, buildMeshes, flags);

            for (int i = 0; i < count; i++)
            {
                var item = wanted[i];
                var origin = requests[i].Origin;
                var terrainBounds = SetBounds(buildMeshes[i], ranges[i], config.ChunkSize);
                var hasWater = waterRanges[i].x <= waterRanges[i].y;
                var waterBounds = hasWater ? SetBounds(buildMeshes[count + i], waterRanges[i], config.ChunkSize) : default;

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

            requests.Dispose();
            ranges.Dispose();
            waterRanges.Dispose();
            waterLayout.Dispose();
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

        Entity CreateRenderEntity(int2 coord, float2 origin, in RenderMeshDescription desc, BatchMaterialID material, BatchMeshID mesh, string name)
        {
            var transform = LocalTransform.FromPosition(origin.x, 0f, origin.y);
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, transform);
            EntityManager.AddComponentData(entity, new LocalToWorld { Value = transform.ToMatrix() });
            RenderMeshUtility.AddComponents(entity, EntityManager, desc, new MaterialMeshInfo(material, mesh));
#if UNITY_EDITOR
            EntityManager.SetName(entity, $"{name} {coord.x},{coord.y}");
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
