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
    /// up to MaxBuildsPerFrame. Chunk meshes are built in parallel Burst jobs and drawn by Entities Graphics.
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
        }

        struct BuildItem
        {
            public int2 Coord;
            public int Lod;
            public float Distance;
        }

        readonly Dictionary<int2, LoadedChunk> loaded = new Dictionary<int2, LoadedChunk>();
        readonly Stack<(Mesh mesh, BatchMeshID id)> meshPool = new Stack<(Mesh, BatchMeshID)>();
        readonly List<int2> scratchCoords = new List<int2>();
        readonly List<BuildItem> wanted = new List<BuildItem>();
        readonly List<Mesh> buildMeshes = new List<Mesh>();
        readonly List<BatchMeshID> buildMeshIds = new List<BatchMeshID>();

        EntitiesGraphicsSystem graphics;
        BatchMaterialID materialId;
        TerrainConfig lastConfig;
        TerrainSource lastSource;
        bool initialized;
        EntityQuery focusQuery;

        static readonly RenderMeshDescription RenderDesc = new RenderMeshDescription(ShadowCastingMode.On, receiveShadows: true);

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainConfig>();
            RequireForUpdate<TerrainSource>();
            focusQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainFocus>(), ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnDestroy()
        {
            foreach (var chunk in loaded.Values)
                Object.Destroy(chunk.Mesh);
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
            var meshData = Mesh.AllocateWritableMeshData(count);
            var requests = new NativeArray<ChunkBuildRequest>(count, Allocator.TempJob);
            var ranges = new NativeArray<float2>(count, Allocator.TempJob);
            var layout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.Temp);
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

            for (int i = 0; i < count; i++)
            {
                var item = wanted[i];
                var quads = config.QuadsForLod(item.Lod);
                var indexCount = BuildChunkMeshJob.IndexCount(quads);
                requests[i] = new ChunkBuildRequest { Origin = (float2)item.Coord * config.ChunkSize, Quads = quads, Lod = item.Lod };

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
                Source = source,
                ChunkSize = config.ChunkSize,
                NormalEpsilon = config.ChunkSize / config.Lod0Quads,
                SkirtDepth = config.SkirtDepth,
                HeightRanges = ranges
            };
            IJobParallelForExtensions.Schedule(job, count, 1).Complete();

            // Rebuilt chunks reuse their own mesh; new chunks take one from the pool.
            buildMeshes.Clear();
            buildMeshIds.Clear();
            for (int i = 0; i < count; i++)
            {
                var (mesh, id) = loaded.TryGetValue(wanted[i].Coord, out var existing)
                    ? (existing.Mesh, existing.MeshId)
                    : RentMesh();
                buildMeshes.Add(mesh);
                buildMeshIds.Add(id);
            }
            Mesh.ApplyAndDisposeWritableMeshData(meshData, buildMeshes, flags);

            for (int i = 0; i < count; i++)
            {
                var item = wanted[i];
                var mesh = buildMeshes[i];
                var range = ranges[i];
                var size = config.ChunkSize;
                var aabb = new AABB
                {
                    Center = new float3(size * 0.5f, (range.x + range.y) * 0.5f, size * 0.5f),
                    Extents = new float3(size * 0.5f, math.max((range.y - range.x) * 0.5f, 0.01f), size * 0.5f)
                };
                mesh.bounds = new Bounds(aabb.Center, aabb.Extents * 2f);

                if (loaded.TryGetValue(item.Coord, out var chunk))
                {
                    chunk.Lod = item.Lod;
                    loaded[item.Coord] = chunk;
                    EntityManager.SetComponentData(chunk.Entity, new TerrainChunk { Coord = item.Coord, Lod = item.Lod });
                    EntityManager.SetComponentData(chunk.Entity, new RenderBounds { Value = aabb });
                }
                else
                {
                    loaded[item.Coord] = new LoadedChunk
                    {
                        Entity = CreateChunkEntity(item.Coord, item.Lod, requests[i].Origin, buildMeshIds[i]),
                        Lod = item.Lod,
                        Mesh = mesh,
                        MeshId = buildMeshIds[i]
                    };
                }
            }

            requests.Dispose();
            ranges.Dispose();
        }

        (Mesh mesh, BatchMeshID id) RentMesh()
        {
            if (meshPool.Count > 0)
                return meshPool.Pop();

            var mesh = new Mesh { name = "TerrainChunk" };
            mesh.MarkDynamic();
            return (mesh, graphics.RegisterMesh(mesh));
        }

        Entity CreateChunkEntity(int2 coord, int lod, float2 origin, BatchMeshID meshId)
        {
            var transform = LocalTransform.FromPosition(origin.x, 0f, origin.y);
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new TerrainChunk { Coord = coord, Lod = lod });
            EntityManager.AddComponentData(entity, transform);
            EntityManager.AddComponentData(entity, new LocalToWorld { Value = transform.ToMatrix() });
            RenderMeshUtility.AddComponents(entity, EntityManager, RenderDesc, new MaterialMeshInfo(materialId, meshId));
#if UNITY_EDITOR
            EntityManager.SetName(entity, $"TerrainChunk {coord.x},{coord.y}");
#endif
            return entity;
        }

        void Unload(int2 coord)
        {
            var chunk = loaded[coord];
            EntityManager.DestroyEntity(chunk.Entity);
            meshPool.Push((chunk.Mesh, chunk.MeshId));
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
