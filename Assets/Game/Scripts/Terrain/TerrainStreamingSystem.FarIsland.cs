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
    /// <summary>
    /// R-22 row 1: the whole island, at every preset. Past the streamed ground (the chunk disc, and the far ring where
    /// it is on) the rest of the island is drawn from one coarse mesh built once from TerrainSource: 2 km tiles on a
    /// 64 m grid over the land and its coast, and a ring of eight big tiles of open sea round that square, three times
    /// its size, so the sea reaches the horizon. It is the same chunk mesh job, material and water material as the streamed ground (so the
    /// terrain shader's far path colours it: biome colour, strata, snow line, far grass), drawn only: no colliders, no
    /// shadows cast, no flora, no TerrainChunk tag, so nothing that reads chunks ever sees it.
    ///
    /// It sits <see cref="IslandDrop"/> below the streamed ground, so wherever the two overlap the streamed ground
    /// wins the depth test and the coarse grid never shows through; where the streamed ground ends the skirts hang
    /// over the small step. A tile lying wholly inside the streamed ground stops drawing (DisableRendering). Rebuilt
    /// when the island's shape changes (a re-bake); built from TerrainSource only, so a future world file changes it
    /// for free (rule 22). Haze today reaches 98% at the streamed ground's edge, so the island only reads once R-22
    /// row 2's atmosphere lands; <c>-farhaze &lt;m&gt;</c> pushes the haze out for shots meanwhile.
    /// </summary>
    public partial class TerrainStreamingSystem
    {
        /// <summary>Off draws no island past the streamed ground (PUNCHY_FARISLAND=0 or <c>-farisland 0</c>).</summary>
        public static bool FarIsland = System.Environment.GetEnvironmentVariable("PUNCHY_FARISLAND") != "0";
        /// <summary>How far the island sits below the streamed ground (m): enough that the 64 m grid stays under the
        /// finer grids it overlaps, small enough that the step at the streamed edge is about a pixel at 2 km.</summary>
        public static float IslandDrop = 4f;
        /// <summary>Debug: the haze edge (m) instead of the drawn edge, so the island can be seen before row 2's
        /// atmosphere (<c>-farhaze 12000</c>). 0 leaves the haze alone.</summary>
        float farHaze;
        bool islandFlagsRead, islandOn;

        const float IslandSpan = 2048f;
        /// <summary>A 64 m grid on land; the open sea's tiles (twice the land square's half-size) get 8 quads.</summary>
        const int IslandQuads = 32, SeaQuads = 8;
        /// <summary>How far the sea reaches from the island's centre (m): three times the land square's half-size.</summary>
        float seaReach;

        struct IslandTile
        {
            public Entity Entity, WaterEntity;   // WaterEntity is Entity.Null when the tile is dry
            public Mesh Mesh, WaterMesh;
            public BatchMeshID MeshId, WaterMeshId;
            public float2 Min;
            public float Span;
        }

        /// <summary>One tile size's build: one mesh job builds one span.</summary>
        struct IslandBatch
        {
            public Mesh.MeshDataArray Data;
            public NativeArray<ChunkBuildRequest> Requests;
            public NativeArray<float2> Ranges, WaterRanges;
            public NativeArray<VertexAttributeDescriptor> WaterLayout;
            public float Span;
            public int Count;
        }

        readonly List<IslandTile> islandTiles = new List<IslandTile>();
        IslandBatch islandLand, islandSea;
        JobHandle islandHandle;
        bool islandBuilding, islandBuilt;
        Entity islandPrototype, islandWaterPrototype;
        static readonly RenderMeshDescription IslandDesc = new RenderMeshDescription(ShadowCastingMode.Off, receiveShadows: true);

        /// <summary>A number after a test flag (command line, or PlayArgs in the editor), NaN when it's absent.</summary>
        static float ArgValue(string[] args, string flag)
        {
            var i = System.Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
        }

        /// <summary>The debug haze edge; returns the far plane the island needs (the sea's edge), 0 when off.</summary>
        float ApplyFarIsland()
        {
            if (!islandFlagsRead)
            {
                // Read once per run (this system is made afresh each Play session; statics may outlive one).
                islandFlagsRead = true;
                var args = Punchy.Debugging.DebugControls.LaunchArgs;
                islandOn = FarIsland && ArgValue(args, "-farisland") != 0f;
                farHaze = math.max(0f, ArgValue(args, "-farhaze"));
            }
            if (!islandOn)
                return 0f;
            if (farHaze > DrawDistance)
                DrawDistance = farHaze;
            return math.max(seaReach * 1.5f, 20000f);
        }

        /// <summary>Builds the island once (in the background), then each frame hides the tiles the streamed ground covers.</summary>
        void UpdateFarIsland(in TerrainConfig config, in TerrainSource source, float2 focus)
        {
            if (!islandOn)
                return ;
            if (islandBuilding)
            {
                if (!islandHandle.IsCompleted)
                    return;
                FinishIslandBuild();
            }
            if (!islandBuilt)
            {
                StartIslandBuild(config, source);
                return;
            }
            if (surfaceHidden)
                return;

            // The streamed ground is solid out to the far ring's edge, or to the disc's less half a chunk.
            var cover = farRadius > 0f ? farRadius : nearRadius - config.ChunkSize;
            foreach (var tile in islandTiles)
            {
                var far = math.max(math.abs(tile.Min - focus), math.abs(tile.Min + tile.Span - focus));
                var covered = !Loading && math.length(far) < cover;
                ShowIslandTile(tile.Entity, !covered);
                ShowIslandTile(tile.WaterEntity, !covered);
            }
        }

        void ShowIslandTile(Entity e, bool show)
        {
            if (e == Entity.Null)
                return;
            var off = EntityManager.HasComponent<DisableRendering>(e);
            if (show && off)
                EntityManager.RemoveComponent<DisableRendering>(e);
            else if (!show && !off)
                EntityManager.AddComponent<DisableRendering>(e);
        }

        void StartIslandBuild(in TerrainConfig config, in TerrainSource source)
        {
            var world = source.World;
            // The coast wanders out by up to its roughness times the radius (past it is sea floor, under the sea).
            var reach = world.WorldSize * 0.5f * (1f + world.CoastlineRoughness);
            var n = (int)math.ceil(reach / IslandSpan);
            var land = new List<float2>();
            for (int z = -n; z < n; z++)
            for (int x = -n; x < n; x++)
                land.Add(new float2(x, z) * IslandSpan);
            var landEdge = n * IslandSpan;
            // Eight sea tiles, each the land square's size, abut it all round (none overlaps it, so no z-fight).
            var sea = new List<float2>();
            var seaSpan = 2f * landEdge;
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                if (x != 0 || z != 0)
                    sea.Add(new float2(x, z) * seaSpan - landEdge);
            seaReach = 3f * landEdge;

            var last = config.LodLevels - 1;
            islandLand = ScheduleIslandBatch(config, source, land, IslandSpan, IslandQuads, last + 1, default, out var landJob);
            islandSea = ScheduleIslandBatch(config, source, sea, seaSpan, SeaQuads, last + 2, default, out var seaJob);
            islandHandle = JobHandle.CombineDependencies(landJob, seaJob);
            JobHandle.ScheduleBatchedJobs();
            islandBuilding = true;
        }

        IslandBatch ScheduleIslandBatch(in TerrainConfig config, in TerrainSource source, List<float2> origins, float span,
            int quads, int lod, JobHandle after, out JobHandle handle)
        {
            var count = origins.Count;
            var batch = new IslandBatch
            {
                Data = Mesh.AllocateWritableMeshData(count * 2),
                Requests = new NativeArray<ChunkBuildRequest>(count, Allocator.Persistent),
                Ranges = new NativeArray<float2>(count, Allocator.Persistent),
                WaterRanges = new NativeArray<float2>(count, Allocator.Persistent),
                WaterLayout = new NativeArray<VertexAttributeDescriptor>(WaterVertex.Layout, Allocator.Persistent),
                Span = span,
                Count = count
            };
            var layout = new NativeArray<VertexAttributeDescriptor>(TerrainVertex.Layout, Allocator.Temp);
            var indexCount = BuildChunkMeshJob.IndexCount(quads);
            for (int i = 0; i < count; i++)
            {
                // Past the disc's last LOD: no cave cuts, landmarks, scarps or puddles, no morph, deep skirts.
                batch.Requests[i] = new ChunkBuildRequest
                {
                    Origin = origins[i],
                    Quads = quads,
                    Lod = lod,
                    CanMorph = false,
                    MorphRange = config.MorphRange(config.LodLevels - 1)
                };
                var data = batch.Data[i];
                data.SetVertexBufferParams(BuildChunkMeshJob.VertexCount(quads), layout);
                data.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount), MeshFlags);
            }
            var job = new BuildChunkMeshJob
            {
                Meshes = batch.Data,
                Requests = batch.Requests,
                WaterLayout = batch.WaterLayout,
                Source = source,
                ChunkSize = span,
                // The disc's normal scale, so the island lights like the ground it meets.
                NormalEpsilon = config.ChunkSize / config.Lod0Quads,
                SkirtDepth = config.SkirtDepth,
                HeightRanges = batch.Ranges,
                WaterRanges = batch.WaterRanges,
                Clay = ScarpClay.On
            };
            handle = IJobParallelForExtensions.Schedule(job, count, 1, after);
            return batch;
        }

        void FinishIslandBuild()
        {
            islandHandle.Complete();
            islandBuilding = false;
            islandBuilt = true;
            ApplyIslandBatch(ref islandLand);
            ApplyIslandBatch(ref islandSea);
        }

        void ApplyIslandBatch(ref IslandBatch batch)
        {
            var count = batch.Count;
            var meshes = new List<Mesh>(count * 2);
            var ids = new List<BatchMeshID>(count * 2);
            for (int i = 0; i < count * 2; i++)
            {
                var (mesh, id) = RentMesh();
                meshes.Add(mesh);
                ids.Add(id);
            }
            Mesh.ApplyAndDisposeWritableMeshData(batch.Data, meshes, MeshFlags);

            var wet = 0;
            for (int i = 0; i < count; i++)
                if (Wet(batch, i))
                    wet++;
            var terrain = Spawn(ref islandPrototype, IslandDesc, materialId, ids[0], false, count);
            var water = Spawn(ref islandWaterPrototype, WaterDesc, waterMaterialId, ids[count], false, wet);
            wet = 0;
            for (int i = 0; i < count; i++)
            {
                var origin = batch.Requests[i].Origin;
                var tile = new IslandTile
                {
                    Entity = PlaceIsland(terrain[i], origin, materialId, ids[i], SetBounds(meshes[i], batch.Ranges[i], batch.Span), "FarIsland"),
                    Mesh = meshes[i],
                    MeshId = ids[i],
                    WaterMesh = meshes[count + i],
                    WaterMeshId = ids[count + i],
                    Min = origin,
                    Span = batch.Span
                };
                if (Wet(batch, i))
                    tile.WaterEntity = PlaceIsland(water[wet++], origin, waterMaterialId, ids[count + i],
                        SetBounds(meshes[count + i], batch.WaterRanges[i], batch.Span, WaterSwells.BoundsPad), "FarIslandWater");
                // Built while the surface is hidden (deep in a cave): starts hidden too.
                if (surfaceHidden)
                {
                    ShowIslandTile(tile.Entity, false);
                    ShowIslandTile(tile.WaterEntity, false);
                }
                islandTiles.Add(tile);
            }
            DisposeIslandBatch(ref batch);
        }

        bool Wet(in IslandBatch batch, int i) => batch.WaterRanges[i].x <= batch.WaterRanges[i].y && waterMaterialId != BatchMaterialID.Null;

        Entity PlaceIsland(Entity entity, float2 origin, BatchMaterialID material, BatchMeshID mesh, AABB bounds, string name)
        {
            Place(entity, origin, material, mesh, bounds, name);
            var transform = LocalTransform.FromPosition(origin.x, -IslandDrop, origin.y);
            EntityManager.SetComponentData(entity, transform);
            EntityManager.SetComponentData(entity, new LocalToWorld { Value = transform.ToMatrix() });
            return entity;
        }

        static void DisposeIslandBatch(ref IslandBatch batch)
        {
            batch.Requests.Dispose();
            batch.Ranges.Dispose();
            batch.WaterRanges.Dispose();
            batch.WaterLayout.Dispose();
            batch.Count = 0;
        }

        /// <summary>Drops the island (settings or island shape changed): it builds again next frame.</summary>
        void UnloadIsland()
        {
            if (islandBuilding)
            {
                islandHandle.Complete();
                islandLand.Data.Dispose();
                islandSea.Data.Dispose();
                DisposeIslandBatch(ref islandLand);
                DisposeIslandBatch(ref islandSea);
                islandBuilding = false;
            }
            islandBuilt = false;
            if (islandTiles.Count == 0)
                return;
            var dead = new NativeList<Entity>(islandTiles.Count * 2, Allocator.Temp);
            foreach (var tile in islandTiles)
            {
                dead.Add(tile.Entity);
                if (tile.WaterEntity != Entity.Null)
                    dead.Add(tile.WaterEntity);
                meshPool.Push((tile.Mesh, tile.MeshId));
                meshPool.Push((tile.WaterMesh, tile.WaterMeshId));
            }
            EntityManager.DestroyEntity(dead.AsArray());
            dead.Dispose();
            islandTiles.Clear();
        }

        /// <summary>Island tiles for HideFarChunks: always far from a camera in a cave.</summary>
        void SortIslandTiles(System.Action<Entity, bool> sort)
        {
            foreach (var tile in islandTiles)
            {
                sort(tile.Entity, true);
                if (tile.WaterEntity != Entity.Null)
                    sort(tile.WaterEntity, true);
            }
        }

        void DestroyIsland()
        {
            if (islandBuilding)
            {
                islandHandle.Complete();
                islandLand.Data.Dispose();
                islandSea.Data.Dispose();
                DisposeIslandBatch(ref islandLand);
                DisposeIslandBatch(ref islandSea);
                islandBuilding = false;
            }
            foreach (var tile in islandTiles)
            {
                Object.Destroy(tile.Mesh);
                Object.Destroy(tile.WaterMesh);
            }
            islandTiles.Clear();
        }

        /// <summary>Island tiles built, for probes and the shot gate.</summary>
        public int FarIslandTiles => islandTiles.Count;
    }
}
