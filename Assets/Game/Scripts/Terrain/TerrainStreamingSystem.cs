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
        static readonly int DetailFarId = Shader.PropertyToID("_PunchyTerrainDetailFar");
        /// <summary>Where the terrain's fine relief has faded out at Detail 1 (m; TerrainSurface.shader). W-101: was a fixed 70.</summary>
        const float DetailFar = 110f;
        /// <summary>Vertices per batch (a full-detail chunk is about 4,500).</summary>
        const int VertexBudget = 14000;
        /// <summary>When off, the first batch is load-boosted too (for before/after timing).</summary>
        public static bool SmallFirstBatch = true;
        /// <summary>Frames a batch may run before the main thread waits for it: only a backstop. A chunk at a cave mouth
        /// meshes for about 40 ms on one worker (every vertex samples the mouth cut five times), and waiting for it
        /// after a few frames was a 100 ms frame on the walk in; the next batch just starts a little later.</summary>
        const int MaxBuildFrames = 60;
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
        /// <summary>W-75: new chunk entities are instantiated from these (Prefab-tagged) in one call per batch, instead of
        /// several structural changes per entity.</summary>
        Entity terrainPrototype, waterPrototype;

        // The batch being built in the background.
        readonly List<BuildItem> building = new List<BuildItem>();
        JobHandle buildHandle;
        int buildFrames;

        /// <summary>True while a batch of chunks is meshing in the background, so other background work (the weather
        /// maps) can hold off rather than crowd it out.</summary>
        public static bool Meshing { get; private set; }
        /// <summary>W-75: true from the island bake until the first time every chunk in view is built. There is no frame
        /// to protect before the player spawns, so batches are <see cref="LoadBoost"/> times larger (flora places faster
        /// too). PUNCHY_LOADBOOST=0 turns it off, for before/after benches.</summary>
        public static bool Loading { get; private set; } = true;
        const int LoadBoost = 6;
        static readonly bool LoadBoostOn = System.Environment.GetEnvironmentVariable("PUNCHY_LOADBOOST") != "0";
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
            // A batch still meshing when play mode ends (or the game quits) reads the terrain blobs, which the scene
            // teardown frees before this system is destroyed: finish it first, so it never reads a freed blob.
            Application.quitting += CancelBuild;
        }

        protected override void OnDestroy()
        {
            Application.quitting -= CancelBuild;
            CancelBuild();
            foreach (var chunk in loaded.Values)
            {
                Object.Destroy(chunk.Mesh);
                Object.Destroy(chunk.WaterMesh);
            }
            ClearWaterRegions(true);
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

            config = ApplyViewDistance(config);

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
            // The chunk rings (and the shader's geomorph) stay on the ground plane, unlike the 3D draw distances
            // (R-6 row 0): a chunk spans every height its ground reaches, so its nearest point to a camera above it
            // is already about the ground distance, and a height-dependent ring would reload chunks as the camera
            // climbs or jumps.
            // The terrain shader geomorphs vertices by their distance from this same point.
            Shader.SetGlobalVector(TerrainFocusId, new Vector4(focus.x, focus.y, 0f, 0f));
            Shader.SetGlobalFloat(DetailFarId, Punchy.Settings.GraphicsSettings.DetailRange(DetailFar));
            RefreshWaterRegions(focus, config.ChunkSize);

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
            // A new island (IslandV2 resets its version while it bakes) loads in large batches again.
            if (Punchy.Terrain.IslandV2.ActiveVersion == 0)
                Loading = true;
            // The first batch stays small so the island's first frame isn't held up (#102); the boost starts once
            // the nearest chunks are in.
            var boost = Loading && LoadBoostOn && (loaded.Count > 0 || !SmallFirstBatch) ? LoadBoost : 1;
            budget *= boost;

            UnloadDistantChunks(config, focus);
            CollectWantedChunks(config, focus);
            if (wanted.Count == 0)
            {
                if (Loading && Punchy.Terrain.IslandV2.ActiveVersion != 0 && building.Count == 0)
                {
                    Loading = false;
                    UnityEngine.Debug.Log($"[stream] world ready at {UnityEngine.Time.realtimeSinceStartup:F1} s since start, {loaded.Count} chunks (load boost {(LoadBoostOn ? LoadBoost : 1)}x, {World.Name})");
                }
                return;
            }

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
            while (count < wanted.Count && count < config.MaxBuildsPerFrame * boost)
            {
                var quads = config.QuadsForLod(wanted[count].Lod);
                vertices += BuildChunkMeshJob.VertexCount(quads);
                if (count > 0 && vertices > budget)
                    break;
                count++;
            }
            StartBuild(config, source, count);
        }

        /// <summary>The graphics settings' View distance (GraphicsSettings.View) last applied, and one waiting to be.</summary>
        float appliedView = 1f, pendingView = 1f;
        double pendingViewSince;

        /// <summary>
        /// The config with its view radius and LOD distances at the graphics settings' View distance. A new setting
        /// applies once it has held still for a moment (a slider being dragged would otherwise restart the rebuild every
        /// frame), and then every loaded chunk is rebuilt at its new LOD and morph range, nearest first, keeping its
        /// mesh until then (no holes). Only what's drawn changes: everything else samples TerrainSource.
        /// </summary>
        TerrainConfig ApplyViewDistance(TerrainConfig config)
        {
            var view = Punchy.Settings.GraphicsSettings.View;
            var now = SystemAPI.Time.ElapsedTime;
            if (view != pendingView)
            {
                pendingView = view;
                pendingViewSince = now;
            }
            if (pendingView != appliedView && now - pendingViewSince > 0.3)
            {
                appliedView = pendingView;
                scratchCoords.Clear();
                scratchCoords.AddRange(loaded.Keys);
                foreach (var coord in scratchCoords)
                {
                    var chunk = loaded[coord];
                    chunk.Lod = -1;
                    loaded[coord] = chunk;
                }
            }
            config.Lod0Distance *= appliedView;
            config.ViewRadiusChunks = math.max(2, (int)math.round(config.ViewRadiusChunks * appliedView));
            // The camera reaches past the farthest chunk (only ever pushed out: at View 1 its own far plane already does).
            var cam = Camera.main;
            var reach = (config.ViewRadiusChunks + 1.5f) * config.ChunkSize;
            if (cam != null && cam.farClipPlane < reach)
                cam.farClipPlane = reach;
            return config;
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

            UnloadCoords();
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
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var first = loaded.Count == 0;
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

            // The batch's new terrain and water entities, made in one structural change each.
            var newTerrain = 0;
            var newWater = 0;
            for (int i = 0; i < count; i++)
            {
                var exists = loaded.TryGetValue(building[i].Coord, out var known);
                if (!exists)
                    newTerrain++;
                if (buildWaterRanges[i].x <= buildWaterRanges[i].y && waterMaterialId != BatchMaterialID.Null && (!exists || known.WaterEntity == Entity.Null))
                    newWater++;
            }
            var terrainEntities = Spawn(ref terrainPrototype, TerrainDesc, materialId, buildMeshIds[0], true, newTerrain);
            var waterEntities = Spawn(ref waterPrototype, WaterDesc, waterMaterialId, buildMeshIds[count], false, newWater);
            newTerrain = 0;
            newWater = 0;

            for (int i = 0; i < count; i++)
            {
                var item = building[i];
                var origin = buildRequests[i].Origin;
                var terrainBounds = SetBounds(buildMeshes[i], buildRanges[i], config.ChunkSize);
                var hasWater = buildWaterRanges[i].x <= buildWaterRanges[i].y;
                // (Padded by the swell's reach: the shader moves the sea's vertices, refactor 3.4.)
                var waterBounds = hasWater ? SetBounds(buildMeshes[count + i], buildWaterRanges[i], config.ChunkSize, WaterSwells.BoundsPad) : default;

                if (!loaded.TryGetValue(item.Coord, out var chunk))
                {
                    chunk = new LoadedChunk
                    {
                        Entity = Place(terrainEntities[newTerrain++], origin, materialId, buildMeshIds[i], terrainBounds, "TerrainChunk"),
                        Mesh = buildMeshes[i],
                        MeshId = buildMeshIds[i],
                        WaterMesh = buildMeshes[count + i],
                        WaterMeshId = buildMeshIds[count + i]
                    };
                    EntityManager.SetComponentData(chunk.Entity, new TerrainChunk { Coord = item.Coord, Lod = item.Lod });
                }
                else
                {
                    EntityManager.SetComponentData(chunk.Entity, new TerrainChunk { Coord = item.Coord, Lod = item.Lod });
                    EntityManager.SetComponentData(chunk.Entity, new RenderBounds { Value = terrainBounds });
                }
                chunk.Lod = item.Lod;

                // Water entities come and go as a chunk's water appears or disappears.
                var canDrawWater = hasWater && waterMaterialId != BatchMaterialID.Null;
                if (canDrawWater || chunk.WaterEntity != Entity.Null)
                    MarkWaterRegion(item.Coord);
                if (canDrawWater && chunk.WaterEntity == Entity.Null)
                    chunk.WaterEntity = Place(waterEntities[newWater++], origin, waterMaterialId, chunk.WaterMeshId, waterBounds, "WaterChunk");
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
            if (first || clock.Elapsed.TotalMilliseconds > 250)
                UnityEngine.Debug.Log($"[stream] batch of {count} chunks applied in {clock.Elapsed.TotalMilliseconds:F0} ms{(first ? " (first)" : "")}");
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

        /// <summary>A chunk mesh's bounds from its height range, grown by <paramref name="pad"/> on every side (m; the
        /// water's swell reach, zero for the ground).</summary>
        static AABB SetBounds(Mesh mesh, float2 heightRange, float size, float3 pad = default)
        {
            var aabb = new AABB
            {
                Center = new float3(size * 0.5f, (heightRange.x + heightRange.y) * 0.5f, size * 0.5f),
                Extents = new float3(size * 0.5f, math.max((heightRange.y - heightRange.x) * 0.5f, 0.5f), size * 0.5f) + pad
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
            // Gathered first and changed in two batches: one structural change per chunk was a hitch on the way in.
            var toHide = new NativeList<Entity>(Allocator.Temp);
            var toShow = new NativeList<Entity>(Allocator.Temp);
            void Sort(Entity e, bool far)
            {
                if (e == Entity.Null)
                    return;
                var off = EntityManager.HasComponent<DisableRendering>(e);
                if (hide && far && !off)
                    toHide.Add(e);
                else if ((!hide || !far) && off)
                    toShow.Add(e);
            }
            foreach (var kv in loaded)
            {
                var centre = ((float2)kv.Key + 0.5f) * chunkSize;
                var far = math.distance(centre, eye) > HiddenKeep + chunkSize * 0.71f;
                Sort(kv.Value.Entity, far);
                // Water in a merged far region stays hidden either way; the region's own entity draws it.
                if (kv.Value.WaterEntity != Entity.Null && RegionMerged(kv.Key))
                {
                    if (!EntityManager.HasComponent<DisableRendering>(kv.Value.WaterEntity))
                        toHide.Add(kv.Value.WaterEntity);
                }
                else
                    Sort(kv.Value.WaterEntity, far);
            }
            foreach (var region in regions.Values)
                if (region.Entity != Entity.Null && region.Merged)
                    Sort(region.Entity, true);
            EntityManager.AddComponent<DisableRendering>(toHide.AsArray());
            EntityManager.RemoveComponent<DisableRendering>(toShow.AsArray());
            toHide.Dispose();
            toShow.Dispose();
        }

        /// <summary><paramref name="n"/> new render entities, instantiated in one call from a prototype made on first use.</summary>
        NativeArray<Entity> Spawn(ref Entity prototype, in RenderMeshDescription desc, BatchMaterialID material, BatchMeshID mesh, bool chunk, int n)
        {
            if (n == 0)
                return default;
            if (prototype == Entity.Null || !EntityManager.Exists(prototype))
            {
                prototype = EntityManager.CreateEntity();
                EntityManager.AddComponentData(prototype, LocalTransform.Identity);
                EntityManager.AddComponentData(prototype, new LocalToWorld { Value = float4x4.identity });
                RenderMeshUtility.AddComponents(prototype, EntityManager, desc, new MaterialMeshInfo(material, mesh));
                if (chunk)
                    EntityManager.AddComponentData(prototype, new TerrainChunk());
                EntityManager.AddComponent<Prefab>(prototype);
            }
            return EntityManager.Instantiate(prototype, n, Allocator.Temp);
        }

        /// <summary>Fills in a freshly instantiated chunk entity (no structural change).</summary>
        Entity Place(Entity entity, float2 origin, BatchMaterialID material, BatchMeshID mesh, AABB bounds, string name)
        {
            var transform = LocalTransform.FromPosition(origin.x, 0f, origin.y);
            EntityManager.SetComponentData(entity, transform);
            EntityManager.SetComponentData(entity, new LocalToWorld { Value = transform.ToMatrix() });
            EntityManager.SetComponentData(entity, new MaterialMeshInfo(material, mesh));
            EntityManager.SetComponentData(entity, new RenderBounds { Value = bounds });
#if UNITY_EDITOR
            // One name per kind: the editor only has room for a few thousand distinct entity names, and streaming
            // creates chunks without end.
            EntityManager.SetName(entity, name);
#endif
            return entity;
        }

        /// <summary>Unloads a chunk; its entities go on the dead list, destroyed together by the caller.</summary>
        void Unload(int2 coord, NativeList<Entity> dead)
        {
            var chunk = loaded[coord];
            dead.Add(chunk.Entity);
            if (chunk.WaterEntity != Entity.Null)
            {
                dead.Add(chunk.WaterEntity);
                MarkWaterRegion(coord);
            }
            meshPool.Push((chunk.Mesh, chunk.MeshId));
            meshPool.Push((chunk.WaterMesh, chunk.WaterMeshId));
            loaded.Remove(coord);
        }

        void UnloadAll()
        {
            ClearWaterRegions(false);
            scratchCoords.Clear();
            scratchCoords.AddRange(loaded.Keys);
            UnloadCoords();
        }

        /// <summary>Unloads the chunks in scratchCoords, destroying their entities in one structural change.</summary>
        void UnloadCoords()
        {
            if (scratchCoords.Count == 0)
                return;
            var dead = new NativeList<Entity>(scratchCoords.Count * 2, Allocator.Temp);
            foreach (var coord in scratchCoords)
                Unload(coord, dead);
            EntityManager.DestroyEntity(dead.AsArray());
            dead.Dispose();
        }
    }
}
