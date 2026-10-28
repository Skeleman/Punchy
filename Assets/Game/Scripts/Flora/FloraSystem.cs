using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Punchy.Flora
{
    /// <summary>
    /// Grows and draws the plants listed in each biome's Flora. Plants are placed in three layers (ground, shrub,
    /// tree), each on its own jittered grid, so plants of one layer never overlap and each layer streams in tiles out
    /// to its own distance. Placement is a pure function of position (Burst jobs sampling the terrain), so the same
    /// plant always grows in the same place.
    ///
    /// Drawing is GPU-driven: all of a layer's plants live in one buffer, and every frame a compute shader culls them
    /// to the camera, picks each one's level of detail (dithering across the change) and fills indirect draw
    /// arguments, so each variant and LOD is a single instanced draw however many plants there are.
    ///
    /// Landmarks clear the biome's plants from their middles and place their own props (trees, boulders, spires,
    /// crystals, dens) on the same grids, so props never overlap plants either.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after the camera has moved this frame
    public class FloraSystem : MonoBehaviour
    {
        [Serializable]
        public class LayerSettings
        {
            [Tooltip("Spacing of the placement grid (one plant at most per cell), in metres.")]
            public float CellSize = 2f;
            public float TileSize = 40f;
            [Tooltip("Plants fade out by this distance.")]
            public float Radius = 260f;
            public float Lod1Distance = 30f;
            [Tooltip("Set beyond Radius to never use the coarsest level.")]
            public float Lod2Distance = 110f;
            public bool CastShadows = true;
        }

        public Material Material;
        public ComputeShader Culling;
        public LayerSettings Ground = new LayerSettings { CellSize = 0.7f, TileSize = 14f, Radius = 70f, Lod1Distance = 18f, Lod2Distance = 1000f, CastShadows = false };
        public LayerSettings Shrubs = new LayerSettings { CellSize = 2f, TileSize = 40f, Radius = 260f, Lod1Distance = 30f, Lod2Distance = 110f };
        public LayerSettings Trees = new LayerSettings { CellSize = 4.5f, TileSize = 108f, Radius = 1500f, Lod1Distance = 55f, Lod2Distance = 220f };
        [Tooltip("Placement cells started per frame across all layers. Placement runs on worker threads in the " +
                 "background; too many at once crowds out the terrain's own jobs and causes hitches.")]
        public int CellsPerFrame = 6000;
        [Tooltip("Plants this far outside the view still draw into the shadow maps, so off-screen trees cast shadows.")]
        public float ShadowMargin = 30f;
        [Tooltip("Solid plants within this distance of the player are given to player movement as obstacles.")]
        public float CollisionRange = 24f;

        [StructLayout(LayoutKind.Sequential)]
        public struct FloraInstance
        {
            public float3 Position;
            public float Yaw;
            public float Scale;
            public int Model;      // -1 = empty cell
            public float Random;
            public float Snow;
            /// <summary>Ground tilt: the ground normal's x and z (rocks lie on the slope; 0 for everything upright).</summary>
            public float2 Tilt;
        }

        class Layer
        {
            public LayerSettings Settings;
            public int Index;
            public int ModelBase, ModelCount;
            public readonly Dictionary<int2, NativeArray<FloraInstance>> Tiles = new Dictionary<int2, NativeArray<FloraInstance>>();
            public bool Dirty;
            public int Count;
            public int[] ModelCounts;
            /// <summary>Per model, the box its placed plants' bases span and their largest scale (for draw bounds).</summary>
            public float3[] ModelMin, ModelMax;
            public float[] ModelMaxScale;
            public GraphicsBuffer Instances, Visible, Args, Regions, Bounds;
            /// <summary>The draw arguments as last uploaded: each variant × LOD's mesh range, and its region of the
            /// visible list as startInstance (the shader adds it), so draws need no per-draw property block.</summary>
            public GraphicsBuffer.IndirectDrawIndexedArgs[] ArgsData;
            /// <summary>This layer's copy of the flora material, holding its buffers. Every draw in the layer shares it,
            /// so consecutive draws don't re-apply the shader (a property block per draw made each one a state change).</summary>
            public Material Material;

            // Tiles being placed by jobs in the background (one batch at a time).
            public JobHandle Placing;
            public NativeArray<int2> PlacingTiles;
            public NativeArray<FloraInstance> PlacingOutput;
            public int PlacingFrames;

            public void Release()
            {
                Placing.Complete();
                if (PlacingTiles.IsCreated) PlacingTiles.Dispose();
                if (PlacingOutput.IsCreated) PlacingOutput.Dispose();
                foreach (var tile in Tiles.Values)
                    tile.Dispose();
                Tiles.Clear();
                Instances?.Release();
                Visible?.Release();
                Args?.Release();
                Regions?.Release();
                Bounds?.Release();
                Instances = Visible = Args = Regions = Bounds = null;
                if (Material != null)
                    UnityEngine.Object.Destroy(Material);
                Material = null;
            }
        }

        readonly List<FloraModel> models = new List<FloraModel>();
        readonly List<FloraSpecies> species = new List<FloraSpecies>();
        readonly Layer[] layers = new Layer[3];
        /// <summary>Where plants grow (shared with the animals through FloraPlacement).</summary>
        BlobAssetReference<FloraBlob> placement;
        bool built;
        TerrainSource lastSource;
        long configStamp;
        float nextConfigCheck;

        World queryWorld;
        EntityQuery terrainQuery, paletteQuery, playerQuery;
        Entity obstacleEntity;
        float2 lastObstacleCentre = new float2(float.MaxValue);
        bool obstaclesDirty;

        readonly List<int2> scratch = new List<int2>();
        readonly Plane[] planes = new Plane[6];
        readonly Vector4[] planeVectors = new Vector4[6];

        static readonly int InstancesId = Shader.PropertyToID("_FloraInstances");
        static readonly int VisibleId = Shader.PropertyToID("_FloraVisible");
        static readonly int ArgsId = Shader.PropertyToID("_Args");
        static readonly int RegionsId = Shader.PropertyToID("_Regions");
        static readonly int BoundsId = Shader.PropertyToID("_ModelBounds");
        static readonly int CountId = Shader.PropertyToID("_Count");
        static readonly int DrawCountId = Shader.PropertyToID("_DrawCount");
        static readonly int ModelBaseId = Shader.PropertyToID("_ModelBase");
        static readonly int PlanesId = Shader.PropertyToID("_Planes");
        static readonly int CameraId = Shader.PropertyToID("_CameraPosition");
        static readonly int LodId = Shader.PropertyToID("_Lod");
        static readonly int ShadowId = Shader.PropertyToID("_Shadow");

        public int PlantCount
        {
            get
            {
                var n = 0;
                foreach (var l in layers)
                    if (l != null) n += l.Count;
                return n;
            }
        }

        void OnDisable() => Teardown();

        void Teardown()
        {
            foreach (var l in layers)
                l?.Release();
            Array.Clear(layers, 0, layers.Length);
            foreach (var m in models)
                m.Destroy();
            models.Clear();
            species.Clear();
            if (placement.IsCreated) placement.Dispose();
            placement = default;
            built = false;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Material == null || Culling == null || !TryGetWorld(out var source, out var palette))
                return;

            // Rebuild everything when the terrain is re-baked or (in the editor) a biome's flora or a species changes.
            if (built && (!source.Equals(lastSource) || ConfigChanged(palette)))
                Teardown();
            if (!built)
            {
                Build(palette);
                lastSource = source;
                configStamp = ConfigStamp(palette);
            }

            var camXZ = new float2(cam.transform.position.x, cam.transform.position.z);
            Stream(source, camXZ);
            foreach (var l in layers)
                if (l.Dirty)
                    Upload(l);
            UpdateObstacles();
            Draw(cam);
        }

        // ------------------------------------------------------------------------------------------- setup

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                queryWorld = world;
                obstacleEntity = Entity.Null;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        /// <summary>
        /// Changes whenever a biome, species or landmark type in use is edited (editor only): the sum of their dirty
        /// counters, which Unity bumps on every inspector edit. Cheap enough to check every second.
        /// </summary>
        static long ConfigStamp(BiomePalette palette)
        {
            long stamp = 0;
#if UNITY_EDITOR
            foreach (var biome in palette.Biomes)
            {
                if (biome == null) continue;
                stamp += UnityEditor.EditorUtility.GetDirtyCount(biome);
                foreach (var f in biome.Flora)
                    if (f.Species != null)
                        stamp += UnityEditor.EditorUtility.GetDirtyCount(f.Species);
            }
            if (palette.Landmarks != null)
                foreach (var landmark in palette.Landmarks)
                {
                    if (landmark == null) continue;
                    stamp += UnityEditor.EditorUtility.GetDirtyCount(landmark);
                    foreach (var p in landmark.Props)
                        if (p.Species != null)
                            stamp += UnityEditor.EditorUtility.GetDirtyCount(p.Species);
                }
#endif
            return stamp;
        }

        bool ConfigChanged(BiomePalette palette)
        {
            if (!Application.isEditor || Time.unscaledTime < nextConfigCheck)
                return false;
            nextConfigCheck = Time.unscaledTime + 1f;
            return ConfigStamp(palette) != configStamp;
        }

        void Build(BiomePalette palette)
        {
            // Species ordered by layer, so each layer's models are contiguous.
            var cellSizes = new float3(FloraPlacement.CellSize(Ground.TileSize, Ground.CellSize),
                                       FloraPlacement.CellSize(Shrubs.TileSize, Shrubs.CellSize),
                                       FloraPlacement.CellSize(Trees.TileSize, Trees.CellSize));
            placement = FloraPlacement.Build(palette, cellSizes, species);
            foreach (var s in species)
                for (int v = 0; v < s.Variants; v++)
                    models.Add(FloraGenerator.Grow(s, v));
            ref var rules = ref placement.Value.Rules;

            var settings = new[] { Ground, Shrubs, Trees };
            for (int i = 0; i < 3; i++)
            {
                var layer = new Layer { Settings = settings[i], Index = i, ModelBase = models.Count, Dirty = true };
                for (int s = 0; s < species.Count; s++)
                    if ((int)species[s].Layer == i)
                    {
                        layer.ModelBase = math.min(layer.ModelBase, rules[s].FirstModel);
                        layer.ModelCount += rules[s].Variants;
                    }
                if (layer.ModelCount == 0)
                    layer.ModelBase = 0;
                layer.ModelCounts = new int[layer.ModelCount];
                CreateDrawBuffers(layer);
                layers[i] = layer;
            }
            built = true;
            obstaclesDirty = true;
        }

        void CreateDrawBuffers(Layer layer)
        {
            var draws = math.max(1, layer.ModelCount * 3);
            layer.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[draws];
            var bounds = new float2[math.max(1, layer.ModelCount)];
            if (layer.Material == null)
                layer.Material = new Material(Material) { name = $"Flora layer {layer.Index}" };
            for (int m = 0; m < layer.ModelCount; m++)
            {
                var model = models[layer.ModelBase + m];
                bounds[m] = new float2(model.Radius, model.CentreY);
                for (int lod = 0; lod < 3; lod++)
                {
                    var mesh = model.Lods[lod];
                    var d = m * 3 + lod;
                    args[d] = new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = mesh.GetIndexCount(0),
                        startIndex = mesh.GetIndexStart(0),
                        baseVertexIndex = mesh.GetBaseVertex(0)
                    };
                }
            }
            layer.ArgsData = args;
            layer.Args.SetData(args);
            layer.Bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float) * 2);
            layer.Bounds.SetData(bounds);
            layer.Regions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, draws, sizeof(uint));
        }

        // ------------------------------------------------------------------------------------------- streaming

        float2 TileCentre(Layer l, int2 c) => ((float2)c + 0.5f) * l.Settings.TileSize;

        void Stream(in TerrainSource source, float2 camXZ)
        {
            // Unload tiles that fell out of range.
            foreach (var l in layers)
            {
                scratch.Clear();
                var keep = l.Settings.Radius + l.Settings.TileSize * 1.5f;
                foreach (var kv in l.Tiles)
                    if (math.distance(TileCentre(l, kv.Key), camXZ) > keep)
                        scratch.Add(kv.Key);
                foreach (var key in scratch)
                {
                    l.Tiles[key].Dispose();
                    l.Tiles.Remove(key);
                }
                if (scratch.Count > 0)
                {
                    l.Dirty = true;
                    obstaclesDirty |= l.Index > 0;
                }
            }

            // Placement runs in the background: collect each layer's finished batch, then start the next one with the
            // nearest missing tiles, within a shared per-frame budget (near layers first).
            var budget = CellsPerFrame;
            foreach (var l in layers)
            {
                if (l.ModelCount == 0)
                    continue;
                var cellsPerSide = (int)math.round(l.Settings.TileSize / l.Settings.CellSize);
                var perTile = cellsPerSide * cellsPerSide;
                if (l.PlacingTiles.IsCreated)
                {
                    // Don't let a batch run on for long: the tiles nearest the camera may be waiting on it.
                    if (!l.Placing.IsCompleted && ++l.PlacingFrames < 3)
                        continue;
                    Harvest(l, perTile);
                }
                if (budget <= 0)
                    continue;

                scratch.Clear();
                var r = (int)math.ceil(l.Settings.Radius / l.Settings.TileSize) + 1;
                var centre = (int2)math.floor(camXZ / l.Settings.TileSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    var c = centre + new int2(x, z);
                    var min = (float2)c * l.Settings.TileSize;
                    if (!l.Tiles.ContainsKey(c) && math.distance(math.clamp(camXZ, min, min + l.Settings.TileSize), camXZ) < l.Settings.Radius)
                        scratch.Add(c);
                }
                if (scratch.Count == 0)
                    continue;
                scratch.Sort((a, b) => math.distancesq(TileCentre(l, a), camXZ).CompareTo(math.distancesq(TileCentre(l, b), camXZ)));
                var count = math.min(scratch.Count, math.max(1, budget / perTile));
                budget -= count * perTile;

                l.PlacingTiles = new NativeArray<int2>(count, Allocator.Persistent);
                for (int i = 0; i < count; i++)
                    l.PlacingTiles[i] = scratch[i];
                l.PlacingOutput = new NativeArray<FloraInstance>(count * perTile, Allocator.Persistent);
                l.PlacingFrames = 0;
                l.Placing = new PlaceJob
                {
                    Source = source,
                    Tiles = l.PlacingTiles,
                    CellsPerSide = cellsPerSide,
                    Layer = l.Index,
                    Placement = placement,
                    Output = l.PlacingOutput
                }.Schedule(l.PlacingOutput.Length, 64);
            }
            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Turns a finished placement batch into tiles (each holding just its occupied cells).</summary>
        void Harvest(Layer l, int perTile)
        {
            l.Placing.Complete();
            for (int t = 0; t < l.PlacingTiles.Length; t++)
            {
                var tile = l.PlacingTiles[t];
                if (l.Tiles.ContainsKey(tile))
                    continue;
                var cells = l.PlacingOutput.GetSubArray(t * perTile, perTile);
                var occupied = 0;
                for (int i = 0; i < perTile; i++)
                    if (cells[i].Model >= 0)
                        occupied++;
                var plants = new NativeArray<FloraInstance>(occupied, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var n = 0;
                for (int i = 0; i < perTile; i++)
                    if (cells[i].Model >= 0)
                        plants[n++] = cells[i];
                l.Tiles[tile] = plants;
            }
            l.PlacingTiles.Dispose();
            l.PlacingOutput.Dispose();
            l.Dirty = true;
            obstaclesDirty |= l.Index > 0;
        }

        /// <summary>Gathers a layer's tiles into its GPU instance buffer and lays out per-variant/LOD output regions.</summary>
        void Upload(Layer l)
        {
            l.Dirty = false;
            Array.Clear(l.ModelCounts, 0, l.ModelCounts.Length);
            var total = 0;
            foreach (var tile in l.Tiles.Values)
                total += tile.Length;
            l.Count = 0;
            if (total == 0)
                return;

            var all = new NativeArray<FloraInstance>(total, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var n = 0;
            foreach (var tile in l.Tiles.Values)
            {
                if (tile.Length == 0)
                    continue;
                NativeArray<FloraInstance>.Copy(tile, 0, all, n, tile.Length);
                n += tile.Length;
            }
            var counts = new NativeArray<int>(l.ModelCount, Allocator.TempJob);
            var mins = new NativeArray<float3>(l.ModelCount, Allocator.TempJob);
            var maxs = new NativeArray<float3>(l.ModelCount, Allocator.TempJob);
            var scales = new NativeArray<float>(l.ModelCount, Allocator.TempJob);
            new CountModelsJob { Plants = all, ModelBase = l.ModelBase, Counts = counts, Min = mins, Max = maxs, MaxScale = scales }.Run();
            counts.CopyTo(l.ModelCounts);
            if (l.ModelMin == null || l.ModelMin.Length != l.ModelCount)
            {
                l.ModelMin = new float3[l.ModelCount];
                l.ModelMax = new float3[l.ModelCount];
                l.ModelMaxScale = new float[l.ModelCount];
            }
            mins.CopyTo(l.ModelMin);
            maxs.CopyTo(l.ModelMax);
            scales.CopyTo(l.ModelMaxScale);
            counts.Dispose();
            mins.Dispose();
            maxs.Dispose();
            scales.Dispose();

            if (l.Instances == null || l.Instances.count < total)
            {
                l.Instances?.Release();
                l.Visible?.Release();
                var capacity = math.ceilpow2(total);
                l.Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<FloraInstance>());
                l.Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * 3, sizeof(uint) * 2);
            }
            l.Instances.SetData(all, 0, 0, total);
            all.Dispose();
            l.Count = total;

            // Each variant gets room for all its plants at each LOD (a plant appears at most once per LOD).
            var regions = new uint[l.ModelCount * 3];
            uint offset = 0;
            for (int m = 0; m < l.ModelCount; m++)
            for (int lod = 0; lod < 3; lod++)
            {
                regions[m * 3 + lod] = offset;
                l.ArgsData[m * 3 + lod].startInstance = offset;
                offset += (uint)l.ModelCounts[m];
            }
            l.Regions.SetData(regions);
            l.Args.SetData(l.ArgsData);
            l.Material.SetBuffer(InstancesId, l.Instances);
            l.Material.SetBuffer(VisibleId, l.Visible);
        }

        [BurstCompile]
        struct CountModelsJob : IJob
        {
            [ReadOnly] public NativeArray<FloraInstance> Plants;
            public int ModelBase;
            public NativeArray<int> Counts;
            public NativeArray<float3> Min, Max;
            public NativeArray<float> MaxScale;

            public void Execute()
            {
                for (int m = 0; m < Min.Length; m++)
                {
                    Min[m] = new float3(float.MaxValue);
                    Max[m] = new float3(float.MinValue);
                }
                for (int i = 0; i < Plants.Length; i++)
                {
                    var m = Plants[i].Model - ModelBase;
                    Counts[m]++;
                    Min[m] = math.min(Min[m], Plants[i].Position);
                    Max[m] = math.max(Max[m], Plants[i].Position);
                    MaxScale[m] = math.max(MaxScale[m], Plants[i].Scale);
                }
            }
        }

        // ------------------------------------------------------------------------------------------- drawing

        void Draw(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++)
                planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            var camPos = cam.transform.position;
            var clear = Culling.FindKernel("Clear");
            var cull = Culling.FindKernel("Cull");
            var shadowDistance = QualitySettings.shadowDistance;
            if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                shadowDistance = urp.shadowDistance;

            foreach (var l in layers)
            {
                if (l.Count == 0 || l.ModelCount == 0)
                    continue;
                var s = l.Settings;
                var draws = l.ModelCount * 3;
                Culling.SetInt(DrawCountId, draws);
                Culling.SetBuffer(clear, ArgsId, l.Args);
                Culling.Dispatch(clear, (draws + 63) / 64, 1, 1);

                Culling.SetInt(CountId, l.Count);
                Culling.SetInt(ModelBaseId, l.ModelBase);
                Culling.SetVectorArray(PlanesId, planeVectors);
                Culling.SetVector(CameraId, camPos);
                Culling.SetVector(LodId, new Vector4(s.Lod1Distance, s.Lod2Distance, s.Radius, 0f));
                Culling.SetVector(ShadowId, new Vector4(s.CastShadows ? shadowDistance : 0f, ShadowMargin, 0f, 0f));
                Culling.SetBuffer(cull, InstancesId, l.Instances);
                Culling.SetBuffer(cull, VisibleId, l.Visible);
                Culling.SetBuffer(cull, ArgsId, l.Args);
                Culling.SetBuffer(cull, RegionsId, l.Regions);
                Culling.SetBuffer(cull, BoundsId, l.Bounds);
                Culling.Dispatch(cull, (l.Count + 63) / 64, 1, 1);

                // Each model's draws are bounded by where its plants actually are, so Unity culls them per pass and
                // shadow cascade, and a level of detail none of them can be at (by distance, with room for the
                // crossfades) isn't drawn at all: a draw costs a pass even when the GPU cull leaves it no plants.
                float3 eye = camPos;
                float Near(float d) => d * 1.1f + 5f;
                for (int m = 0; m < l.ModelCount; m++)
                {
                    if (l.ModelCounts[m] == 0)
                        continue;
                    var model = models[l.ModelBase + m];
                    var reach = (model.Radius + math.abs(model.CentreY)) * l.ModelMaxScale[m];
                    var min = l.ModelMin[m] - reach;
                    var max = l.ModelMax[m] + reach;
                    var nearest = math.distance(math.clamp(eye, min, max), eye);
                    var farthest = math.length(math.max(math.abs(eye - min), math.abs(eye - max)));
                    if (nearest > Near(s.Radius))
                        continue;
                    var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));
                    for (int lod = 0; lod < 3; lod++)
                    {
                        if (lod == 2 && s.Lod2Distance >= s.Radius)
                            continue;
                        var lodNear = lod == 0 ? 0f : lod == 1 ? s.Lod1Distance : s.Lod2Distance;
                        var lodFar = lod == 0 ? s.Lod1Distance : lod == 1 && s.Lod2Distance < s.Radius ? s.Lod2Distance : s.Radius;
                        if (nearest > Near(lodFar) || farthest < lodNear * 0.9f - 5f)
                            continue;
                        var rp = new RenderParams(l.Material)
                        {
                            worldBounds = bounds,
                            // Small props (fences, palisades, signposts...) cast none: the shadow pass's cost is per draw.
                            shadowCastingMode = s.CastShadows && model.CastsShadows && lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                            receiveShadows = true
                        };
                        Graphics.RenderMeshIndirect(rp, model.Lods[lod], l.Args, 1, m * 3 + lod);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------- collisions

        void UpdateObstacles()
        {
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var centre = player.Position.xz;
            if (!obstaclesDirty && math.distance(centre, lastObstacleCentre) < CollisionRange * 0.25f)
                return;
            obstaclesDirty = false;
            lastObstacleCentre = centre;

            var em = queryWorld.EntityManager;
            if (obstacleEntity == Entity.Null || !em.Exists(obstacleEntity))
            {
                obstacleEntity = em.CreateEntity();
                em.SetName(obstacleEntity, "Flora Obstacles");
                em.AddBuffer<FloraObstacle>(obstacleEntity);
            }
            var buffer = em.GetBuffer<FloraObstacle>(obstacleEntity);
            buffer.Clear();
            for (int li = 1; li < 3; li++)
            {
                var l = layers[li];
                var size = l.Settings.TileSize;
                var min = (int2)math.floor((centre - CollisionRange) / size);
                var max = (int2)math.floor((centre + CollisionRange) / size);
                for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!l.Tiles.TryGetValue(new int2(x, z), out var tile))
                        continue;
                    foreach (var inst in tile)
                    {
                        var model = models[inst.Model];
                        // Solid with a trunk, or with colliders of its own (rocks, buildings, fences: those have no trunk).
                        if (!model.Species.Solid || model.TrunkRadius <= 0f && model.Colliders.Count == 0
                            || math.distance(inst.Position.xz, centre) > CollisionRange)
                            continue;
                        if (model.Colliders.Count == 0)
                        {
                            buffer.Add(new FloraObstacle
                            {
                                Position = inst.Position,
                                Radius = model.TrunkRadius * inst.Scale,
                                Height = model.Height * inst.Scale
                            });
                            continue;
                        }
                        // Shaped plants (rocks): their cylinders, turned and scaled as the plant is drawn.
                        float c = math.cos(inst.Yaw), sn = math.sin(inst.Yaw);
                        foreach (var col in model.Colliders)
                        {
                            var offset = new float2(col.x * c - col.y * sn, col.x * sn + col.y * c) * inst.Scale;
                            buffer.Add(new FloraObstacle
                            {
                                Position = inst.Position + new float3(offset.x, 0f, offset.y),
                                Radius = col.z * inst.Scale,
                                Height = col.w * inst.Scale
                            });
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------- placement

        [BurstCompile]
        struct PlaceJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<int2> Tiles;
            public int CellsPerSide;
            public int Layer;
            public BlobAssetReference<FloraBlob> Placement;
            [WriteOnly] public NativeArray<FloraInstance> Output;

            public void Execute(int i)
            {
                var perTile = CellsPerSide * CellsPerSide;
                var local = i % perTile;
                // Cells are numbered in world space, so a cell's plant doesn't depend on how tiles are cut.
                var cell = Tiles[i / perTile] * CellsPerSide + new int2(local % CellsPerSide, local / CellsPerSide);
                Output[i] = FloraPlacement.Place(ref Placement.Value, Source, cell, Layer);
            }
        }
    }
}
