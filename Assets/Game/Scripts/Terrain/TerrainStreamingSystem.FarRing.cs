using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-131: the far ring. At the High View distance and up the ground reaches <see cref="FarScale"/> times past the
    /// chunk disc, so a flyer sees land to the horizon instead of a haze wall. Past the disc the ground is drawn as
    /// 256 m tiles (4x4 chunks) at the coarsest grid only: 16 m vertex spacing for the first band (the same grid the
    /// disc's outermost LOD uses, so the two meet edge to edge with no seam) and 32 m beyond it (skirts cover the step
    /// between them). Past that band the 32 m grid is drawn as 512 m tiles (level 1, 2x2 of the 256 m ones), so the
    /// ring costs about a quarter of the draws there. Far tiles are drawn only: no colliders, no shadows, no flora or props, no TerrainChunk tag, so
    /// nothing that reads chunks ever sees one.
    ///
    /// The disc of chunks is left as it is. A far tile is wanted wherever any of its 16 chunks lies outside the disc
    /// (so along the disc's edge a tile and some of its chunks both draw), and it goes once the disc has fully taken it
    /// over: every chunk centre inside, every chunk built. A disc chunk past its unload margin waits for its far tile,
    /// so the ground never has a hole. Where they overlap the two are the same 16 m grid sampled at the same points,
    /// so the overlap draws identical depth and colour and can't be seen. Far tiles build in their own small
    /// background batches, beside the chunk batches.
    /// </summary>
    public partial class TerrainStreamingSystem
    {
        /// <summary>The ring reaches this many times the chunk disc's radius (High: 31 chunks, about 2 km, to about 3 km).
        /// 2x measured +0.64 ms at 300 m up against 1.5x's +0.44 (W-131's cap is +0.5).</summary>
        public static float FarScale = 1.5f;
        /// <summary>The ring is on from this View distance up (High is 1.3; Medium and below keep the plain disc).</summary>
        const float FarFromView = 1.29f;
        /// <summary>Off keeps the plain disc at every preset (for before/after; PUNCHY_FARRING=0).</summary>
        public static bool FarRing = System.Environment.GetEnvironmentVariable("PUNCHY_FARRING") != "0";
        /// <summary>Where the drawn ground ends (m from the focus): the far ring's edge when it's on, else the disc's.
        /// The haze (WeatherSystem) reaches ~98% here and the camera's far plane reaches past it.</summary>
        public static float DrawDistance { get; private set; } = 1536f;

        /// <summary>Tile side in chunks (level 0; a level 1 tile is twice that).</summary>
        const int FarTileChunks = 4;
        /// <summary>Tiles whose nearest point is within this of the disc's edge use the 16 m grid; farther ones 32 m, as
        /// 512 m tiles where a whole one lies past the band.</summary>
        const float FarFineBand = 512f;
        /// <summary>Most far tiles per batch (a 16 m tile is 357 vertices), and while the world first loads.</summary>
        const int FarBatch = 12, FarBatchLoading = 64;

        struct FarTile
        {
            public Entity Entity;
            public Mesh Mesh;
            public BatchMeshID MeshId;
            public Entity WaterEntity;   // Entity.Null when the tile is dry
            public Mesh WaterMesh;
            public BatchMeshID WaterMeshId;
            public int Quads;            // -1: flagged for a rebuild (new island shape)
        }

        /// <summary>A tile's key is (x, z, level): level 0 is 256 m, level 1 is 512 m.</summary>
        struct FarItem
        {
            public int3 Tile;
            public int Quads;
            public float Distance;
        }

        readonly Dictionary<int3, FarTile> farTiles = new Dictionary<int3, FarTile>();
        /// <summary>Tiles that should stand this frame, built or not; farWanted is the ones not yet built at their grid.</summary>
        readonly HashSet<int3> farKeep = new HashSet<int3>();
        readonly List<FarItem> farWanted = new List<FarItem>();
        readonly List<FarItem> farBuilding = new List<FarItem>();
        readonly List<int3> farScratch = new List<int3>();
        Entity farPrototype;
        JobHandle farHandle;
        Mesh.MeshDataArray farData;
        NativeArray<ChunkBuildRequest> farRequests;
        NativeArray<float2> farRanges, farWaterRanges;
        NativeArray<VertexAttributeDescriptor> farWaterLayout;
        /// <summary>The ring's outer radius (m), or 0 while it's off; and the disc's radius it was set with.</summary>
        float farRadius, nearRadius;

        /// <summary>Far tiles cast no shadow: past the disc they lie far outside the shadow distance anyway.</summary>
        static readonly RenderMeshDescription FarDesc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: true);

        static int3 TileOf(int2 coord) => new int3((int2)math.floor((float2)coord / FarTileChunks), 0);
        static int3 ParentOf(int3 tile) => new int3((int2)math.floor((float2)tile.xy / 2f), 1);
        static int3 ChildOf(int3 big, int i) => new int3(big.xy * 2 + new int2(i & 1, i >> 1), 0);
        static int TileChunks(int3 tile) => FarTileChunks << tile.z;

        /// <summary>Distance from the focus to the farthest of a tile's chunk centres (inside the disc when all are).</summary>
        static float TileFarthestCentre(int3 tile, float2 focus, float chunkSize)
        {
            var n = TileChunks(tile);
            var min = ((float2)tile.xy * n + 0.5f) * chunkSize;
            var max = min + (n - 1) * chunkSize;
            var far = math.select(min, max, math.abs(max - focus) > math.abs(min - focus));
            return math.distance(focus, far);
        }

        /// <summary>Distance from the focus to the nearest point of a tile.</summary>
        static float TileDistance(int3 tile, float2 focus, float chunkSize)
        {
            var span = TileChunks(tile) * chunkSize;
            var min = (float2)tile.xy * span;
            return math.distance(focus, math.clamp(focus, min, min + span));
        }

        bool FarBuilt(int3 tile) => farTiles.TryGetValue(tile, out var t) && t.Quads > 0;

        /// <summary>Sets the ring's reach for this frame's disc, the drawn edge, and the camera's far plane to match.</summary>
        void ApplyFarRing(in TerrainConfig config, float view)
        {
            nearRadius = config.ViewRadiusChunks * config.ChunkSize;
            farRadius = FarRing && view >= FarFromView ? nearRadius * math.max(1f, FarScale) : 0f;
            DrawDistance = farRadius > 0f ? farRadius : nearRadius;
            var cam = Camera.main;
            // (The farthest corner of a 512 m tile can sit most of its diagonal past the edge.)
            var reach = farRadius > 0f ? farRadius + FarTileChunks * config.ChunkSize * 3f : 0f;
            if (cam != null && cam.farClipPlane < reach)
                cam.farClipPlane = reach;
        }

        /// <summary>A disc chunk past the unload margin goes only once its far tile has taken over (or it's past the ring).</summary>
        bool FarTileCovers(int2 coord, float2 focus, float chunkSize)
        {
            var tile = TileOf(coord);
            if (FarBuilt(tile) || FarBuilt(ParentOf(tile)))
                return true;
            return TileDistance(tile, focus, chunkSize) > farRadius;
        }

        /// <summary>
        /// Picks up a finished far batch, drops tiles that the disc has fully taken over or that fell out of reach, and
        /// starts the next batch of the nearest tiles still wanted.
        /// </summary>
        void UpdateFarRing(in TerrainConfig config, in TerrainSource source, float2 focus)
        {
            if (farBuilding.Count > 0)
            {
                if (!farHandle.IsCompleted)
                    return;
                FinishFarBuild(config);
            }

            var size = config.ChunkSize;
            farKeep.Clear();
            farWanted.Clear();
            if (farRadius > 0f)
            {
                // A 512 m tile where the whole of it lies past the fine band, else its four 256 m tiles.
                var coarseFrom = nearRadius + FarFineBand;
                var bigSpan = 2 * FarTileChunks * size;
                var r = (int)math.ceil(farRadius / bigSpan) + 1;
                var centre = (int2)math.floor(focus / bigSpan);
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var big = new int3(centre + new int2(dx, dz), 1);
                    var distance = TileDistance(big, focus, size);
                    if (distance > farRadius)
                        continue;
                    // (A built one holds a little inside the band, so it isn't split and merged as the focus wanders.)
                    if (distance >= coarseFrom || (distance >= coarseFrom - 128f && FarBuilt(big)))
                    {
                        WantFar(big, 16, distance);
                        continue;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var tile = ChildOf(big, i);
                        var d = TileDistance(tile, focus, size);
                        if (d > farRadius || TileFarthestCentre(tile, focus, size) <= nearRadius)
                            continue;
                        var quads = d < coarseFrom ? 16 : 8;
                        // Coarse tiles sharpen as they come into the band; fine ones coarsen only well past it.
                        if (quads == 8 && d < coarseFrom + 128f && farTiles.TryGetValue(tile, out var known) && known.Quads == 16)
                            quads = 16;
                        WantFar(tile, quads, d);
                    }
                }
            }

            // A tile no longer kept goes once whatever replaces it is built: a split 512 m tile once its kept children
            // are, merged 256 m tiles once their parent is, a tile the disc has fully taken over once its chunks are in.
            farScratch.Clear();
            foreach (var kv in farTiles)
            {
                var tile = kv.Key;
                if (farKeep.Contains(tile))
                    continue;
                if (farRadius <= 0f || TileDistance(tile, focus, size) > farRadius + 128f)
                    farScratch.Add(tile);
                else if (tile.z == 1)
                {
                    if (ChildrenBuilt(tile))
                        farScratch.Add(tile);
                }
                else if (farKeep.Contains(ParentOf(tile)))
                {
                    if (FarBuilt(ParentOf(tile)))
                        farScratch.Add(tile);
                }
                // (A little inside the line, so a tile on it isn't dropped and rebuilt as the focus wanders.)
                else if (TileFarthestCentre(tile, focus, size) <= nearRadius - 32f && AllChunksLoaded(tile))
                    farScratch.Add(tile);
            }
            UnloadFarTiles();

            if (farWanted.Count == 0)
                return;
            farWanted.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            StartFarBuild(config, source, Loading ? FarBatchLoading : FarBatch);
        }

        /// <summary>Keeps a tile this frame, and queues it when it isn't built at this grid yet.</summary>
        void WantFar(int3 tile, int quads, float distance)
        {
            farKeep.Add(tile);
            if (!farTiles.TryGetValue(tile, out var known) || known.Quads != quads)
                farWanted.Add(new FarItem { Tile = tile, Quads = quads, Distance = distance });
        }

        /// <summary>True when every kept child of a 512 m tile is built.</summary>
        bool ChildrenBuilt(int3 big)
        {
            for (int i = 0; i < 4; i++)
                if (farKeep.Contains(ChildOf(big, i)) && !FarBuilt(ChildOf(big, i)))
                    return false;
            return true;
        }

        bool AllChunksLoaded(int3 tile)
        {
            var n = TileChunks(tile);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                if (!loaded.ContainsKey(tile.xy * n + new int2(x, z)))
                    return false;
            return true;
        }

        /// <summary>Builds up to <paramref name="most"/> of the nearest wanted tiles, all of the nearest one's level
        /// (one job builds one tile size).</summary>
        void StartFarBuild(in TerrainConfig config, in TerrainSource source, int most)
        {
            farBuilding.Clear();
            var level = farWanted[0].Tile.z;
            foreach (var item in farWanted)
                if (item.Tile.z == level && farBuilding.Count < most)
                    farBuilding.Add(item);
            var count = farBuilding.Count;
            var span = (FarTileChunks << level) * config.ChunkSize;
            farData = Mesh.AllocateWritableMeshData(count * 2);
            farRequests = new NativeArray<ChunkBuildRequest>(count, Allocator.Persistent);
            farRanges = new NativeArray<float2>(count, Allocator.Persistent);
            farWaterRanges = new NativeArray<float2>(count, Allocator.Persistent);
            var layout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.Temp);
            farWaterLayout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.Persistent);
            var last = config.LodLevels - 1;
            for (int i = 0; i < count; i++)
            {
                var item = farBuilding[i];
                var indexCount = BuildChunkMeshJob.IndexCount(item.Quads);
                farRequests[i] = new ChunkBuildRequest
                {
                    Origin = (float2)item.Tile.xy * span,
                    Quads = item.Quads,
                    // The disc's last LOD (no cave cuts, no puddle dips, no morph); the 32 m grid a step past it, for
                    // a deeper skirt over the coarser step.
                    Lod = item.Quads == 16 && level == 0 ? last : last + 1,
                    CanMorph = false,
                    MorphRange = config.MorphRange(last)
                };
                var data = farData[i];
                data.SetVertexBufferParams(BuildChunkMeshJob.VertexCount(item.Quads), layout);
                data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshFlags);
            }

            var job = new BuildChunkMeshJob
            {
                Meshes = farData,
                Requests = farRequests,
                WaterLayout = farWaterLayout,
                Source = source,
                ChunkSize = span,
                // The disc's normal scale, so far tiles light exactly like the chunks they meet.
                NormalEpsilon = config.ChunkSize / config.Lod0Quads,
                SkirtDepth = config.SkirtDepth,
                HeightRanges = farRanges,
                WaterRanges = farWaterRanges
            };
            farHandle = IJobParallelForExtensions.Schedule(job, count, 1);
            JobHandle.ScheduleBatchedJobs();
        }

        void FinishFarBuild(in TerrainConfig config)
        {
            farHandle.Complete();
            var count = farBuilding.Count;
            var span = (FarTileChunks << farBuilding[0].Tile.z) * config.ChunkSize;
            buildMeshes.Clear();
            buildMeshIds.Clear();
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < count; i++)
            {
                var (mesh, id) = farTiles.TryGetValue(farBuilding[i].Tile, out var existing)
                    ? pass == 0 ? (existing.Mesh, existing.MeshId) : (existing.WaterMesh, existing.WaterMeshId)
                    : RentMesh();
                buildMeshes.Add(mesh);
                buildMeshIds.Add(id);
            }
            Mesh.ApplyAndDisposeWritableMeshData(farData, buildMeshes, MeshFlags);

            var newTerrain = 0;
            var newWater = 0;
            for (int i = 0; i < count; i++)
            {
                var exists = farTiles.TryGetValue(farBuilding[i].Tile, out var known);
                if (!exists)
                    newTerrain++;
                if (farWaterRanges[i].x <= farWaterRanges[i].y && waterMaterialId != BatchMaterialID.Null && (!exists || known.WaterEntity == Entity.Null))
                    newWater++;
            }
            var terrainEntities = Spawn(ref farPrototype, FarDesc, materialId, buildMeshIds[0], false, newTerrain);
            var waterEntities = Spawn(ref waterPrototype, WaterDesc, waterMaterialId, buildMeshIds[count], false, newWater);
            newTerrain = 0;
            newWater = 0;

            for (int i = 0; i < count; i++)
            {
                var item = farBuilding[i];
                var origin = farRequests[i].Origin;
                var terrainBounds = SetBounds(buildMeshes[i], farRanges[i], span);
                var hasWater = farWaterRanges[i].x <= farWaterRanges[i].y && waterMaterialId != BatchMaterialID.Null;
                var waterBounds = hasWater ? SetBounds(buildMeshes[count + i], farWaterRanges[i], span, WaterSwells.BoundsPad) : default;
                if (!farTiles.TryGetValue(item.Tile, out var tile))
                    tile = new FarTile
                    {
                        Entity = Place(terrainEntities[newTerrain++], origin, materialId, buildMeshIds[i], terrainBounds, "FarTile"),
                        Mesh = buildMeshes[i],
                        MeshId = buildMeshIds[i],
                        WaterMesh = buildMeshes[count + i],
                        WaterMeshId = buildMeshIds[count + i]
                    };
                else
                    EntityManager.SetComponentData(tile.Entity, new RenderBounds { Value = terrainBounds });
                tile.Quads = item.Quads;
                if (hasWater && tile.WaterEntity == Entity.Null)
                    tile.WaterEntity = Place(waterEntities[newWater++], origin, waterMaterialId, tile.WaterMeshId, waterBounds, "FarWater");
                else if (hasWater)
                    EntityManager.SetComponentData(tile.WaterEntity, new RenderBounds { Value = waterBounds });
                else if (tile.WaterEntity != Entity.Null)
                {
                    EntityManager.DestroyEntity(tile.WaterEntity);
                    tile.WaterEntity = Entity.Null;
                }
                // A tile built while the surface is hidden (deep in a cave) starts hidden too.
                if (surfaceHidden)
                {
                    if (!EntityManager.HasComponent<DisableRendering>(tile.Entity))
                        EntityManager.AddComponent<DisableRendering>(tile.Entity);
                    if (tile.WaterEntity != Entity.Null && !EntityManager.HasComponent<DisableRendering>(tile.WaterEntity))
                        EntityManager.AddComponent<DisableRendering>(tile.WaterEntity);
                }
                farTiles[item.Tile] = tile;
            }
            DisposeFarBuild();
        }

        /// <summary>Abandons the far batch being built (settings changed, or shutting down).</summary>
        void CancelFarBuild()
        {
            if (farBuilding.Count == 0)
                return;
            farHandle.Complete();
            farData.Dispose();
            DisposeFarBuild();
        }

        void DisposeFarBuild()
        {
            farBuilding.Clear();
            farRequests.Dispose();
            farRanges.Dispose();
            farWaterRanges.Dispose();
            farWaterLayout.Dispose();
        }

        /// <summary>Unloads the far tiles in farScratch in one structural change; their meshes go back to the pool.</summary>
        void UnloadFarTiles()
        {
            if (farScratch.Count == 0)
                return;
            var dead = new NativeList<Entity>(farScratch.Count * 2, Allocator.Temp);
            foreach (var key in farScratch)
            {
                var tile = farTiles[key];
                dead.Add(tile.Entity);
                if (tile.WaterEntity != Entity.Null)
                    dead.Add(tile.WaterEntity);
                meshPool.Push((tile.Mesh, tile.MeshId));
                meshPool.Push((tile.WaterMesh, tile.WaterMeshId));
                farTiles.Remove(key);
            }
            EntityManager.DestroyEntity(dead.AsArray());
            dead.Dispose();
        }

        void UnloadAllFarTiles()
        {
            CancelFarBuild();
            farScratch.Clear();
            farScratch.AddRange(farTiles.Keys);
            UnloadFarTiles();
        }

        /// <summary>Same layout, new island shape: every far tile is rebuilt (keeping its mesh until then).</summary>
        void FlagFarTilesForRebuild()
        {
            CancelFarBuild();
            farScratch.Clear();
            farScratch.AddRange(farTiles.Keys);
            foreach (var key in farScratch)
            {
                var tile = farTiles[key];
                tile.Quads = -1;
                farTiles[key] = tile;
            }
        }

        /// <summary>Far tiles for HideFarChunks: always far from a camera in a cave.</summary>
        void SortFarTiles(System.Action<Entity, bool> sort)
        {
            foreach (var tile in farTiles.Values)
            {
                sort(tile.Entity, true);
                if (tile.WaterEntity != Entity.Null)
                    sort(tile.WaterEntity, true);
            }
        }

        void DestroyFarMeshes()
        {
            foreach (var tile in farTiles.Values)
            {
                Object.Destroy(tile.Mesh);
                Object.Destroy(tile.WaterMesh);
            }
            farTiles.Clear();
        }

        /// <summary>Far tiles loaded, for probes and the shot gate.</summary>
        public int FarTileCount => farTiles.Count;
        /// <summary>True while far tiles are still wanted or building.</summary>
        public bool FarPending => farBuilding.Count > 0 || farWanted.Count > 0;
    }
}
