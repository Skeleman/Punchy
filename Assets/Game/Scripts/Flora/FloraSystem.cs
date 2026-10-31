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
            /// <summary>
            /// The ground's colour round a built thing's footprint (RGB8, linear; alpha how high the dirt reaches in cm, 0 for none): its
            /// base takes on a band of it, like dirt splashed up the walls (FloraPlacement.DirtColor, Flora.shader).
            /// </summary>
            public uint Ground;
        }

        class Layer
        {
            public LayerSettings Settings;
            /// <summary>Settings' Radius and LOD distances at the graphics settings (see ScaleRanges).</summary>
            public float Radius, Lod1Distance, Lod2Distance;
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
            /// <summary>
            /// The draw arguments the culling shader counts instances into (structured, so D3D11 can write it: an
            /// IndirectArguments buffer can't also be Raw there), copied into Args for the draws once culled.
            /// </summary>
            public GraphicsBuffer Counts;
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
                Counts?.Release();
                Regions?.Release();
                Bounds?.Release();
                Instances = Visible = Args = Counts = Regions = Bounds = null;
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
        EntityQuery terrainQuery, paletteQuery, playerQuery, configQuery;
        Entity obstacleEntity;
        /// <summary>Every model's support grid (a SupportGrids singleton) and, per model, its grid's index (-1 none).</summary>
        BlobAssetReference<SupportGridSet> supportSet;
        int[] modelGrid;
        EntityQuery remoteQuery;
        readonly List<float2> supportScratch = new List<float2>();
        /// <summary>The players the support buffer was last gathered round, and when.</summary>
        readonly List<float2> supportCentres = new List<float2>();
        float nextSupport;
        /// <summary>Walkable tops are gathered within this distance of every player.</summary>
        const float SupportRange = 40f;
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

        // ------------------------------------------------------------------------------------------- fountains' water

        /// <summary>A basin of water in a plant in a loaded tile (FloraModel.Pools: a fountain's), in the world.</summary>
        struct Basin
        {
            public int Layer;
            public int2 Tile;
            public float3 Centre;
            public float Radius;
            public int Model;
        }
        readonly List<Basin> basins = new List<Basin>();
        /// <summary>One water disc per model (its species' water colour baked in), made when first drawn.</summary>
        readonly Dictionary<int, Mesh> basinMeshes = new Dictionary<int, Mesh>();
        /// <summary>How far fountains' water is drawn with the terrain's water (at Detail 1); beyond it their models' own discs stand in.</summary>
        const float BasinRange = 80f;

        /// <summary>Notes the basins of a newly loaded tile's plants (turned and scaled as the plants are).</summary>
        void AddBasins(Layer l, int2 tile, NativeArray<FloraInstance> plants)
        {
            foreach (var plant in plants)
            {
                if (plant.Model < 0 || plant.Model >= models.Count || models[plant.Model].Pools.Count == 0)
                    continue;
                float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
                foreach (var pool in models[plant.Model].Pools)
                {
                    var offset = new float2(pool.x * c - pool.z * sn, pool.x * sn + pool.z * c) * plant.Scale;
                    basins.Add(new Basin
                    {
                        Layer = l.Index,
                        Tile = tile,
                        Centre = plant.Position + new float3(offset.x, pool.y * plant.Scale, offset.y),
                        Radius = pool.w * plant.Scale,
                        Model = plant.Model
                    });
                }
            }
        }

        /// <summary>
        /// Fountains' water near the camera, drawn as the landmark pools are (CaveSystem): a disc in the terrain's water
        /// material, so it ripples, reflects and catches the light as all other water does (Z-12; it was a shiny disc).
        /// </summary>
        void DrawBasins(float3 camPos)
        {
            // (Only called once TryGetWorld has made the queries.)
            if (basins.Count == 0 || configQuery.IsEmpty)
                return;
            var water = configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value;
            if (water == null)
                return;
            foreach (var basin in basins)
            {
                if (math.distance(basin.Centre, camPos) > Punchy.Settings.GraphicsSettings.DetailRange(BasinRange) + basin.Radius)
                    continue;
                if (!basinMeshes.TryGetValue(basin.Model, out var mesh))
                {
                    var colour = models[basin.Model].Species.Foliage.linear;
                    mesh = CaveSystem.MakePoolMesh(new float4(colour.r, colour.g, colour.b, 0.1f), 0f);
                    mesh.name = "Fountain basin";
                    basinMeshes[basin.Model] = mesh;
                }
                var rp = new RenderParams(water)
                {
                    worldBounds = new Bounds(basin.Centre, new Vector3(basin.Radius * 2f, 1f, basin.Radius * 2f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.TRS(basin.Centre, Quaternion.identity, new Vector3(basin.Radius, 1f, basin.Radius)));
            }
        }

        void Teardown()
        {
            foreach (var l in layers)
                l?.Release();
            Array.Clear(layers, 0, layers.Length);
            basins.Clear();
            foreach (var mesh in basinMeshes.Values)
                Destroy(mesh);
            basinMeshes.Clear();
            foreach (var m in models)
                m.Destroy();
            models.Clear();
            species.Clear();
            if (placement.IsCreated) placement.Dispose();
            placement = default;
            // Nothing may read the grids once they're gone.
            if (queryWorld != null && queryWorld.IsCreated && obstacleEntity != Entity.Null && queryWorld.EntityManager.Exists(obstacleEntity))
            {
                if (queryWorld.EntityManager.HasComponent<SupportGrids>(obstacleEntity))
                    queryWorld.EntityManager.RemoveComponent<SupportGrids>(obstacleEntity);
                if (queryWorld.EntityManager.HasBuffer<FloraSupport>(obstacleEntity))
                    queryWorld.EntityManager.GetBuffer<FloraSupport>(obstacleEntity).Clear();
            }
            if (supportSet.IsCreated) supportSet.Dispose();
            supportSet = default;
            modelGrid = null;
            supportCentres.Clear();
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

            ScaleRanges();
            // Deep in a cave the surface's plants are out of sight: no placing, no drawing (obstacles stay).
            var hidden = Punchy.Terrain.CaveSystem.SurfaceHidden >= 0.99f;
            var camXZ = new float2(cam.transform.position.x, cam.transform.position.z);
            if (!hidden)
                Stream(source, camXZ);
            foreach (var l in layers)
                if (l.Dirty)
                    Upload(l);
            UpdateObstacles();
            UpdateSupports();
            FloraDoors.Upload();
            if (!hidden)
            {
                Draw(cam);
                DrawBasins(cam.transform.position);
                DrawBolts(source, camXZ);
            }
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
                configQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                // Other players by their NetPlayer ghosts' transforms (not their avatars' CreatureState, which waits on
                // the creature jobs writing it).
                remoteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
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
            BuildSupportGrids();
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
            layer.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            layer.Counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
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
            layer.Counts.SetData(args);
            layer.Bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float) * 2);
            layer.Bounds.SetData(bounds);
            layer.Regions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, draws, sizeof(uint));
        }

        // ------------------------------------------------------------------------------------------- streaming

        float2 TileCentre(Layer l, int2 c) => ((float2)c + 0.5f) * l.Settings.TileSize;

        /// <summary>
        /// Each layer's draw radius and LOD distances at the graphics settings: the ground layer's at its Detail distance,
        /// shrubs' and trees' at its View distance. A layer never streams in closer than SupportRange and a tile and a
        /// half, since the supports the player stands on come from its streamed tiles (UpdateSupports).
        /// </summary>
        void ScaleRanges()
        {
            foreach (var l in layers)
            {
                var scale = l.Index == 0 ? Punchy.Settings.GraphicsSettings.Detail : Punchy.Settings.GraphicsSettings.View;
                l.Radius = math.max(l.Settings.Radius * scale, SupportRange + l.Settings.TileSize * 1.5f);
                l.Lod1Distance = l.Settings.Lod1Distance * scale;
                l.Lod2Distance = l.Settings.Lod2Distance * scale;
            }
        }

        void Stream(in TerrainSource source, float2 camXZ)
        {
            // Unload tiles that fell out of range.
            foreach (var l in layers)
            {
                scratch.Clear();
                var keep = l.Radius + l.Settings.TileSize * 1.5f;
                foreach (var kv in l.Tiles)
                    if (math.distance(TileCentre(l, kv.Key), camXZ) > keep)
                        scratch.Add(kv.Key);
                foreach (var key in scratch)
                {
                    l.Tiles[key].Dispose();
                    l.Tiles.Remove(key);
                    basins.RemoveAll(b => b.Layer == l.Index && b.Tile.Equals(key));
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
                var r = (int)math.ceil(l.Radius / l.Settings.TileSize) + 1;
                var centre = (int2)math.floor(camXZ / l.Settings.TileSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    var c = centre + new int2(x, z);
                    var min = (float2)c * l.Settings.TileSize;
                    if (!l.Tiles.ContainsKey(c) && math.distance(math.clamp(camXZ, min, min + l.Settings.TileSize), camXZ) < l.Radius)
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
                AddBasins(l, tile, plants);
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
            l.Counts.SetData(l.ArgsData);
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
                Culling.SetBuffer(clear, ArgsId, l.Counts);
                Culling.Dispatch(clear, (draws + 63) / 64, 1, 1);

                Culling.SetInt(CountId, l.Count);
                Culling.SetInt(ModelBaseId, l.ModelBase);
                Culling.SetVectorArray(PlanesId, planeVectors);
                Culling.SetVector(CameraId, camPos);
                Culling.SetVector(LodId, new Vector4(l.Lod1Distance, l.Lod2Distance, l.Radius, 0f));
                Culling.SetVector(ShadowId, new Vector4(s.CastShadows ? shadowDistance : 0f, ShadowMargin, 0f, 0f));
                Culling.SetBuffer(cull, InstancesId, l.Instances);
                Culling.SetBuffer(cull, VisibleId, l.Visible);
                Culling.SetBuffer(cull, ArgsId, l.Counts);
                Culling.SetBuffer(cull, RegionsId, l.Regions);
                Culling.SetBuffer(cull, BoundsId, l.Bounds);
                Culling.Dispatch(cull, (l.Count + 63) / 64, 1, 1);
                Graphics.CopyBuffer(l.Counts, l.Args);

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
                    if (nearest > Near(l.Radius))
                        continue;
                    var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));
                    for (int lod = 0; lod < 3; lod++)
                    {
                        if (lod == 2 && l.Lod2Distance >= l.Radius)
                            continue;
                        var lodNear = lod == 0 ? 0f : lod == 1 ? l.Lod1Distance : l.Lod2Distance;
                        var lodFar = lod == 0 ? l.Lod1Distance : lod == 1 && l.Lod2Distance < l.Radius ? l.Lod2Distance : l.Radius;
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

        /// <summary>Packs every model's support grid into one blob (SupportGrids, on the obstacle entity).</summary>
        void BuildSupportGrids()
        {
            modelGrid = new int[models.Count];
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SupportGridSet>();
            int grids = 0, values = 0, water = 0;
            for (int m = 0; m < models.Count; m++)
            {
                var g = models[m].Support;
                modelGrid[m] = g != null ? grids++ : -1;
                if (g == null)
                    continue;
                values += g.Values.Length;
                if (g.Water != null)
                    water += g.Water.Length;
            }
            var infos = builder.Allocate(ref root.Grids, grids);
            var v = builder.Allocate(ref root.Values, values);
            var w = builder.Allocate(ref root.Water, math.max(water, 1));
            int gi = 0, vi = 0, wi = 0;
            foreach (var model in models)
            {
                var g = model.Support;
                if (g == null)
                    continue;
                infos[gi++] = new SupportGridInfo
                {
                    Min = g.Min, Cell = g.Cell, Res = new int2(g.Res.x, g.Res.y), First = vi, WaterFirst = g.Water != null ? wi : -1,
                    Reach = g.Reach, Error = g.Error
                };
                for (int i = 0; i < g.Values.Length; i++)
                    v[vi++] = g.Values[i];
                if (g.Water != null)
                    for (int i = 0; i < g.Water.Length; i++)
                        w[wi++] = g.Water[i];
            }
            supportSet = builder.CreateBlobAssetReference<SupportGridSet>(Allocator.Persistent);
            builder.Dispose();
            obstaclesDirty = true;
            supportCentres.Clear();
        }

        /// <summary>
        /// Refreshes the walkable tops round every player, local and remote (so a remote player standing on a roof stands
        /// on it on this screen too): the placed props and buildings with support grids within SupportRange of any of
        /// them. Gathered again when someone has moved a few metres, or twice a second.
        /// </summary>
        void UpdateSupports()
        {
            if (!supportSet.IsCreated || modelGrid == null || obstacleEntity == Entity.Null)
                return;
            var em = queryWorld.EntityManager;
            var centres = supportScratch;
            centres.Clear();
            if (playerQuery.TryGetSingleton<LocalTransform>(out var player))
                centres.Add(player.Position.xz);
            // (The local player's own ghost is among these too: one more centre in the same place.)
            if (!remoteQuery.IsEmpty)
                using (var remotes = remoteQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                    foreach (var r in remotes)
                        centres.Add(r.Position.xz);
            var moved = centres.Count != supportCentres.Count;
            for (int i = 0; !moved && i < centres.Count; i++)
                moved = math.distance(centres[i], supportCentres[i]) > 2f;
            if (!moved && Time.unscaledTime < nextSupport)
                return;
            nextSupport = Time.unscaledTime + 0.5f;
            supportCentres.Clear();
            supportCentres.AddRange(centres);

            if (!em.HasComponent<SupportGrids>(obstacleEntity))
                em.AddComponentData(obstacleEntity, new SupportGrids { Set = supportSet });
            else
                em.SetComponentData(obstacleEntity, new SupportGrids { Set = supportSet });
            if (!em.HasBuffer<FloraSupport>(obstacleEntity))
                em.AddBuffer<FloraSupport>(obstacleEntity);
            var buffer = em.GetBuffer<FloraSupport>(obstacleEntity);
            buffer.Clear();
            for (int li = 1; li < 3; li++)
            {
                var l = layers[li];
                var size = l.Settings.TileSize;
                foreach (var kv in l.Tiles)
                {
                    var tileMin = (float2)kv.Key * size;
                    var near = false;
                    foreach (var c in centres)
                        near |= math.all(c > tileMin - SupportRange) && math.all(c < tileMin + size + SupportRange);
                    if (!near)
                        continue;
                    foreach (var inst in kv.Value)
                    {
                        if (inst.Model < 0 || inst.Model >= modelGrid.Length || modelGrid[inst.Model] < 0)
                            continue;
                        var close = false;
                        foreach (var c in centres)
                            close |= math.distancesq(inst.Position.xz, c) < SupportRange * SupportRange;
                        if (close)
                            buffer.Add(new FloraSupport { Position = inst.Position, Yaw = inst.Yaw, Scale = inst.Scale, Grid = modelGrid[inst.Model] });
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------- bolts

        /// <summary>
        /// Spire pairs within reach of each other near the camera (and the odd spire arcing to the ground): their ends,
        /// rhythm (how often a strike comes and how long it lasts, s) and phase, all from their positions, so every
        /// machine agrees; and the strike each last made a sound for.
        /// </summary>
        struct Bolt
        {
            public Vector3 A, B;
            public float Period, Duration, Phase;
            public long Struck;
        }
        readonly List<Bolt> bolts = new List<Bolt>();
        /// <summary>The bolts firing this frame, as tip-to-tip matrices for the bolt shader.</summary>
        readonly List<Matrix4x4> firing = new List<Matrix4x4>();
        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");
        float2 boltCentre = new float2(float.MaxValue);
        float nextBolts;
        Mesh boltMesh;
        Material boltMaterial;
        bool boltsLoaded;
        /// <summary>Bolts are found among arcing spires within this distance of the camera.</summary>
        const float BoltRange = 160f;
        /// <summary>BoltRange at the graphics settings' View distance.</summary>
        float BoltReach => Punchy.Settings.GraphicsSettings.ViewRange(BoltRange);

        /// <summary>
        /// Electric bolts between neighbouring arcing spires (FloraForm.ArcSpire): every spire near the camera pairs with
        /// its nearest neighbour 6-20 m away (about half of them, by a hash of the pair, so not every spire is wired),
        /// and a few spires now and then arc down to the ground beside them. Each fires on its own rhythm from the shared
        /// clock (worked out here, so the crackle it makes lands on the strike), drawn by ArcBolt.shader while it lasts.
        /// Pairs come from the placed spires alone, so every machine sees and hears the same ones.
        /// </summary>
        void DrawBolts(in TerrainSource source, float2 camXZ)
        {
            if (!boltsLoaded)
            {
                boltsLoaded = true;
                boltMaterial = Resources.Load<Material>("ArcBolt");
                boltMesh = BoltMesh();
            }
            if (boltMaterial == null)
                return;
            // Again when the camera has moved, and every couple of seconds (tiles stream in after a jump).
            if (math.distance(camXZ, boltCentre) > 20f || Time.unscaledTime > nextBolts)
            {
                boltCentre = camXZ;
                nextBolts = Time.unscaledTime + 2f;
                GatherBolts(source, camXZ);
            }
            if (bolts.Count == 0)
                return;
            var clock = Shader.GetGlobalFloat(ClockId);
            firing.Clear();
            for (int i = 0; i < bolts.Count; i++)
            {
                var bolt = bolts[i];
                var t = clock + bolt.Phase * bolt.Period;
                var strike = (long)math.floor(t / bolt.Period);
                if (t - strike * bolt.Period >= bolt.Duration)
                    continue;
                firing.Add(Matrix4x4.TRS(bolt.A, Quaternion.LookRotation(bolt.B - bolt.A), new Vector3(1f, 1f, Vector3.Distance(bolt.A, bolt.B))));
                // A crack once per strike, at its middle (a placeholder sound until the bank has a proper zap).
                if (strike != bolt.Struck)
                {
                    bolt.Struck = strike;
                    bolts[i] = bolt;
                    var mid = (bolt.A + bolt.B) * 0.5f;
                    if (Punchy.Audio.GameAudio.Audible(mid, 70f))
                        Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Zip, mid, 2.4f, 1f, 70f, Punchy.Audio.SoundGroup.World);
                }
            }
            if (firing.Count == 0)
                return;
            var rp = new RenderParams(boltMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                                                       worldBounds = new Bounds(new Vector3(camXZ.x, 0f, camXZ.y), Vector3.one * 2f * BoltReach + Vector3.up * 2000f) };
            Graphics.RenderMeshInstanced(rp, boltMesh, 0, firing);
        }

        /// <summary>A pair's rhythm from a hash of its ends' positions (whole metres, so every machine agrees).</summary>
        static Bolt MakeBolt(Vector3 a, Vector3 b, float slowest)
        {
            var h = math.hash(new int4((int2)math.round(((float3)a).xz), (int2)math.round(((float3)b).xz)));
            var r = new Unity.Mathematics.Random(h | 1u);
            return new Bolt { A = a, B = b, Period = r.NextFloat(5f, slowest), Duration = r.NextFloat(0.1f, 0.4f), Phase = r.NextFloat(), Struck = long.MinValue };
        }

        void GatherBolts(in TerrainSource source, float2 camXZ)
        {
            // Keep each surviving bolt's last strike, so a regather doesn't crack it again.
            var struck = new Dictionary<Vector3, long>();
            foreach (var bolt in bolts)
                struck[bolt.A + bolt.B * 3.1f] = bolt.Struck;
            bolts.Clear();
            var tips = new List<float3>();
            // Only the tiles within reach: the tree layer holds the island out to the horizon, far too many plants to
            // walk each time the camera moves (gliding, that's every frame or two).
            for (int li = 1; li < 3 && li < layers.Length; li++)
            {
                var size = layers[li].Settings.TileSize;
                var c0 = (int2)math.floor((camXZ - BoltReach) / size);
                var c1 = (int2)math.floor((camXZ + BoltReach) / size);
                for (int ty = c0.y; ty <= c1.y; ty++)
                for (int tx = c0.x; tx <= c1.x; tx++)
                {
                    if (!layers[li].Tiles.TryGetValue(new int2(tx, ty), out var tile))
                        continue;
                    foreach (var inst in tile)
                    {
                        if (inst.Model < 0 || inst.Model >= models.Count || models[inst.Model].Species == null || models[inst.Model].Species.Form != FloraForm.ArcSpire)
                            continue;
                        if (math.distancesq(inst.Position.xz, camXZ) > BoltReach * BoltReach)
                            continue;
                        tips.Add(inst.Position + new float3(0f, models[inst.Model].Height * inst.Scale * 0.85f, 0f));
                    }
                }
            }
            // In a fixed order, so the pairing doesn't depend on how the tiles happened to stream in.
            tips.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            for (int i = 0; i < tips.Count; i++)
            {
                int best = -1;
                var bestDistance = float.MaxValue;
                for (int j = 0; j < tips.Count; j++)
                {
                    if (j == i) continue;
                    var d = math.distance(tips[i].xz, tips[j].xz);
                    if (d >= 6f && d <= 20f && d < bestDistance) { bestDistance = d; best = j; }
                }
                // Each pair once (from its lower end), and only about half of them.
                if (best < i || best < 0)
                    continue;
                var h = math.hash(new int4((int2)math.round(tips[i].xz), (int2)math.round(tips[best].xz)));
                if ((h & 1) == 0)
                    continue;
                bolts.Add(MakeBolt(tips[i], tips[best], 45f));
            }
            // About one spire in five sometimes arcs down to the ground a few metres off, more rarely.
            foreach (var tip in tips)
            {
                var h = math.hash((int3)math.round(tip));
                if (h % 5 != 0)
                    continue;
                var a = (h >> 8) / 16777216f * math.PI * 2f;
                var foot = tip.xz + new float2(math.cos(a), math.sin(a)) * (3f + (h & 0xFF) / 255f * 3f);
                bolts.Add(MakeBolt(tip, new float3(foot.x, source.SampleHeight(foot), foot.y), 90f));
            }
            for (int i = 0; i < bolts.Count; i++)
                if (struck.TryGetValue(bolts[i].A + bolts[i].B * 3.1f, out var last))
                {
                    var bolt = bolts[i];
                    bolt.Struck = last;
                    bolts[i] = bolt;
                }
        }

        /// <summary>Two crossed ribbons along +z from 0 to 1 (ArcBolt.shader bends and widens them).</summary>
        static Mesh BoltMesh()
        {
            const int segments = 24;
            var positions = new List<Vector3>();
            var uvs = new List<Vector4>();
            var indices = new List<int>();
            for (int ribbon = 0; ribbon < 2; ribbon++)
            {
                var first = positions.Count;
                for (int s = 0; s <= segments; s++)
                {
                    var t = s / (float)segments;
                    foreach (var side in new[] { -1f, 1f })
                    {
                        positions.Add(new Vector3(0f, 0f, t));
                        uvs.Add(new Vector4(t, side, ribbon, 0f));
                    }
                }
                for (int s = 0; s < segments; s++)
                {
                    int a = first + s * 2;
                    indices.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                }
            }
            var mesh = new Mesh { name = "Arc bolt" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(4f, 4f, 2f));
            return mesh;
        }

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
                            // (The trunk's lean, turned and scaled as the tree is drawn: Flora.hlsl.)
                            float yc = math.cos(inst.Yaw), ys = math.sin(inst.Yaw);
                            float2 Turned(Vector2 v) => new float2(v.x * yc - v.y * ys, v.x * ys + v.y * yc) * inst.Scale;
                            buffer.Add(new FloraObstacle
                            {
                                Position = inst.Position,
                                Radius = model.TrunkRadius * inst.Scale,
                                Height = model.Height * inst.Scale,
                                LeanMid = Turned(model.TrunkLeanMid),
                                LeanTop = Turned(model.TrunkLeanTop)
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
