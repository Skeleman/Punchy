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
        }

        /// <summary>A species' placement rules, flattened for Burst.</summary>
        struct SpeciesRules
        {
            public int FirstModel, Variants;
            public float MaxSlope;
            public float2 Elevation;
            public float GrassAffinity;
            public int ToleratesSnow;
            public float PatchFrequency, PatchCoverage;
            public float2 PatchOffset;
            public float NearWater;
            public int OnlyNearWater;
        }

        struct BiomeEntry
        {
            public int Species;
            public float Density;
        }

        /// <summary>A landmark prop, flattened for Burst.</summary>
        struct PropEntry
        {
            public int Species;
            public float Density;
            public float2 Ring, Scale;
            public int Centrepiece;
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
            public GraphicsBuffer Instances, Visible, Args, Regions, Bounds;
            public MaterialPropertyBlock[] Props;

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
            }
        }

        readonly List<FloraModel> models = new List<FloraModel>();
        readonly List<FloraSpecies> species = new List<FloraSpecies>();
        readonly Layer[] layers = new Layer[3];
        NativeArray<SpeciesRules> rules;
        NativeArray<BiomeEntry> entries;
        NativeArray<int2> ranges;   // per biome × layer: (first entry, count)
        NativeArray<PropEntry> props;
        NativeArray<int2> propRanges;   // per landmark type × layer
        int biomeCount, landmarkTypeCount;
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
        static readonly int RegionStartId = Shader.PropertyToID("_RegionStart");
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
            if (rules.IsCreated) rules.Dispose();
            if (entries.IsCreated) entries.Dispose();
            if (ranges.IsCreated) ranges.Dispose();
            if (props.IsCreated) props.Dispose();
            if (propRanges.IsCreated) propRanges.Dispose();
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
            var found = new HashSet<FloraSpecies>();
            foreach (var biome in palette.Biomes)
                if (biome != null)
                    foreach (var f in biome.Flora)
                        if (f.Species != null)
                            found.Add(f.Species);
            var landmarks = palette.Landmarks ?? Array.Empty<LandmarkType>();
            foreach (var landmark in landmarks)
                if (landmark != null)
                    foreach (var p in landmark.Props)
                        if (p.Species != null)
                            found.Add(p.Species);
            species.AddRange(found);
            species.Sort((a, b) => a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer) : string.CompareOrdinal(a.name, b.name));

            rules = new NativeArray<SpeciesRules>(species.Count, Allocator.Persistent);
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                var first = models.Count;
                for (int v = 0; v < s.Variants; v++)
                    models.Add(FloraGenerator.Grow(s, v));
                var h = math.hash(new int2(s.Seed, i));
                rules[i] = new SpeciesRules
                {
                    FirstModel = first,
                    Variants = s.Variants,
                    MaxSlope = s.MaxSlope,
                    Elevation = s.Elevation,
                    GrassAffinity = s.GrassAffinity,
                    ToleratesSnow = s.ToleratesSnow ? 1 : 0,
                    PatchFrequency = 1f / math.max(s.PatchSize, 1f),
                    PatchCoverage = s.PatchCoverage,
                    PatchOffset = new float2(h & 0xFFFF, h >> 16) / 65535f * 300f,
                    NearWater = s.NearWater,
                    OnlyNearWater = s.OnlyNearWater ? 1 : 0
                };
            }

            biomeCount = palette.Biomes.Length;
            var list = new List<BiomeEntry>();
            ranges = new NativeArray<int2>(biomeCount * 3, Allocator.Persistent);
            for (int b = 0; b < biomeCount; b++)
            for (int layer = 0; layer < 3; layer++)
            {
                var start = list.Count;
                if (palette.Biomes[b] != null)
                    foreach (var f in palette.Biomes[b].Flora)
                        if (f.Species != null && (int)f.Species.Layer == layer && f.Density > 0f)
                            list.Add(new BiomeEntry { Species = species.IndexOf(f.Species), Density = f.Density });
                ranges[b * 3 + layer] = new int2(start, list.Count - start);
            }
            entries = new NativeArray<BiomeEntry>(list.ToArray(), Allocator.Persistent);

            landmarkTypeCount = landmarks.Length;
            var propList = new List<PropEntry>();
            propRanges = new NativeArray<int2>(math.max(1, landmarkTypeCount * 3), Allocator.Persistent);
            for (int t = 0; t < landmarkTypeCount; t++)
            for (int layer = 0; layer < 3; layer++)
            {
                var start = propList.Count;
                if (landmarks[t] != null)
                    foreach (var p in landmarks[t].Props)
                        if (p.Species != null && (int)p.Species.Layer == layer && (p.Density > 0f || p.Centrepiece))
                            propList.Add(new PropEntry
                            {
                                Species = species.IndexOf(p.Species),
                                Density = p.Density,
                                Ring = p.Ring,
                                Scale = p.Scale,
                                Centrepiece = p.Centrepiece ? 1 : 0
                            });
                propRanges[t * 3 + layer] = new int2(start, propList.Count - start);
            }
            props = new NativeArray<PropEntry>(propList.ToArray(), Allocator.Persistent);

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
            layer.Props = new MaterialPropertyBlock[draws];
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
                    layer.Props[d] = new MaterialPropertyBlock();
                }
            }
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
                    CellSize = l.Settings.TileSize / cellsPerSide,
                    Layer = l.Index,
                    Rules = rules,
                    Entries = entries,
                    Ranges = ranges,
                    BiomeCount = biomeCount,
                    Props = props,
                    PropRanges = propRanges,
                    LandmarkTypeCount = landmarkTypeCount,
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
            new CountModelsJob { Plants = all, ModelBase = l.ModelBase, Counts = counts }.Run();
            counts.CopyTo(l.ModelCounts);
            counts.Dispose();

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
                offset += (uint)l.ModelCounts[m];
                var props = l.Props[m * 3 + lod];
                props.SetBuffer(InstancesId, l.Instances);
                props.SetBuffer(VisibleId, l.Visible);
                props.SetInt(RegionStartId, (int)regions[m * 3 + lod]);
            }
            l.Regions.SetData(regions);
        }

        [BurstCompile]
        struct CountModelsJob : IJob
        {
            [ReadOnly] public NativeArray<FloraInstance> Plants;
            public int ModelBase;
            public NativeArray<int> Counts;

            public void Execute()
            {
                for (int i = 0; i < Plants.Length; i++)
                    Counts[Plants[i].Model - ModelBase]++;
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

                var bounds = new Bounds(camPos, Vector3.one * (s.Radius * 2f + 400f));
                for (int m = 0; m < l.ModelCount; m++)
                {
                    if (l.ModelCounts[m] == 0)
                        continue;
                    var model = models[l.ModelBase + m];
                    for (int lod = 0; lod < 3; lod++)
                    {
                        if (lod == 2 && s.Lod2Distance >= s.Radius)
                            continue;
                        var rp = new RenderParams(Material)
                        {
                            worldBounds = bounds,
                            matProps = l.Props[m * 3 + lod],
                            shadowCastingMode = s.CastShadows && lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off,
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
                        if (!model.Species.Solid || model.TrunkRadius <= 0f || math.distance(inst.Position.xz, centre) > CollisionRange)
                            continue;
                        buffer.Add(new FloraObstacle
                        {
                            Position = inst.Position,
                            Radius = model.TrunkRadius * inst.Scale,
                            Height = model.Height * inst.Scale
                        });
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
            public float CellSize;
            public int Layer;
            [ReadOnly] public NativeArray<SpeciesRules> Rules;
            [ReadOnly] public NativeArray<BiomeEntry> Entries;
            [ReadOnly] public NativeArray<int2> Ranges;
            public int BiomeCount;
            [ReadOnly] public NativeArray<PropEntry> Props;
            [ReadOnly] public NativeArray<int2> PropRanges;
            public int LandmarkTypeCount;
            [WriteOnly] public NativeArray<FloraInstance> Output;

            public void Execute(int i)
            {
                Output[i] = new FloraInstance { Model = -1 };
                var perTile = CellsPerSide * CellsPerSide;
                var local = i % perTile;
                // Cells are numbered in world space, so a cell's plant doesn't depend on how tiles are cut.
                var cell = Tiles[i / perTile] * CellsPerSide + new int2(local % CellsPerSide, local / CellsPerSide);
                var rng = new Random(math.hash(new int3(cell, Layer * 7919 + 13)) | 1u);
                var xz = ((float2)cell + rng.NextFloat2()) * CellSize;
                var roll = rng.NextFloat();
                var area = CellSize * CellSize / 100f;
                var candidates = new FixedList64Bytes<int>();   // species index, or -(prop entry + 1)
                var weights = new FixedList64Bytes<float>();
                var bound = 0f;

                // Inside a landmark: its centrepiece takes the cell holding the centre, the biome's plants thin out
                // towards the middle, and its props join the candidates.
                var keep = 1f;
                if (Source.Landmarks.IsCreated && LandmarkSampling.Find(ref Source.Landmarks.Value, xz, out var landmark) && landmark.Type < LandmarkTypeCount)
                {
                    ref var set = ref Source.Landmarks.Value;
                    var clear = set.Types[landmark.Type].ClearRadius;
                    keep = clear > 0f ? math.smoothstep(clear * 0.8f, clear, landmark.Distance) : 1f;
                    var propRange = PropRanges[landmark.Type * 3 + Layer];
                    for (int e = propRange.x; e < propRange.x + propRange.y; e++)
                    {
                        var prop = Props[e];
                        if (prop.Centrepiece != 0)
                        {
                            var centre = set.Instances[landmark.Instance].Position;
                            if (math.all((int2)math.floor(centre / CellSize) == cell))
                            {
                                var centreRule = Rules[prop.Species];
                                Output[i] = new FloraInstance
                                {
                                    Position = new float3(centre.x, Source.SampleHeight(centre), centre.y),
                                    Yaw = rng.NextFloat(math.PI * 2f),
                                    Scale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y)),
                                    Model = centreRule.FirstModel + rng.NextInt(centreRule.Variants),
                                    Random = rng.NextFloat()
                                };
                                return;
                            }
                            continue;
                        }
                        var ring = math.smoothstep(prop.Ring.x - 0.05f, prop.Ring.x, landmark.Distance) *
                                   (1f - math.smoothstep(prop.Ring.y, prop.Ring.y + 0.05f, landmark.Distance));
                        var d = prop.Density * area * ring;
                        if (d <= 0f || candidates.Length >= candidates.Capacity)
                            continue;
                        candidates.Add(-(e + 1));
                        weights.Add(d);
                        bound += d;
                    }
                }

                // Cheap first pass: which species could grow here, at most how likely.
                Source.SampleBiomes(xz, out var biomes, out _);
                for (int b = 0; b < 4; b++)
                {
                    var w = biomes.Weights[b];
                    if (w <= 0f || biomes.Ids[b] >= BiomeCount)
                        continue;
                    var range = Ranges[biomes.Ids[b] * 3 + Layer];
                    for (int e = range.x; e < range.x + range.y; e++)
                    {
                        var entry = Entries[e];
                        var d = entry.Density * w * area * keep;
                        if (d <= 0f)
                            continue;
                        var at = candidates.IndexOf(entry.Species);
                        if (at < 0)
                        {
                            if (candidates.Length >= candidates.Capacity)
                                continue;
                            candidates.Add(entry.Species);
                            weights.Add(d);
                        }
                        else
                            weights[at] += d;
                        bound += d * math.max(1f, Rules[entry.Species].NearWater);
                    }
                }
                if (roll >= bound)
                    return;

                // Full sample of the ground.
                var ground = Source.SampleGround(xz);
                if (ground.HasWater && ground.WaterSurface > ground.Height - 0.15f)
                    return;
                var normal = Source.SampleNormal(xz, 1f);
                var surface = Source.SampleSurface(xz, ground, normal);
                var slope = math.degrees(math.acos(math.saturate(normal.y)));
                var elevation = ground.Height - Source.World.SeaLevel;
                var nearWater = 1f - math.smoothstep(1.5f, 8f, -ground.WaterDepth);
                var dry = 1f - math.saturate(ground.River * 1.5f);

                var total = 0f;
                var shrink = new FixedList64Bytes<float>();
                for (int c = 0; c < candidates.Length; c++)
                {
                    // Props go where the landmark puts them, whatever the ground.
                    if (candidates[c] < 0)
                    {
                        shrink.Add(1f);
                        total += weights[c];
                        continue;
                    }
                    var r = Rules[candidates[c]];
                    var f = weights[c] * dry;
                    f *= 1f - math.smoothstep(r.MaxSlope - 4f, r.MaxSlope, slope);
                    var upper = 1f - math.smoothstep(r.Elevation.y - 25f, r.Elevation.y, elevation);
                    f *= math.smoothstep(r.Elevation.x - 1f, r.Elevation.x + 1f, elevation) * upper;
                    f *= math.lerp(1f, math.saturate(surface.Grass * 1.6f), r.GrassAffinity);
                    if (r.ToleratesSnow == 0)
                        f *= math.saturate(1f - surface.Snow * 3f);
                    if (r.PatchCoverage < 1f)
                    {
                        var n = noise.snoise(xz * r.PatchFrequency + r.PatchOffset) * 0.5f + 0.5f;
                        f *= math.smoothstep(1f - r.PatchCoverage - 0.12f, 1f - r.PatchCoverage + 0.12f, n);
                    }
                    f *= math.lerp(1f, r.NearWater, nearWater);
                    if (r.OnlyNearWater != 0)
                        f *= nearWater;
                    weights[c] = f;
                    shrink.Add(upper);
                    total += f;
                }
                if (roll >= total)
                    return;

                var pick = 0;
                for (var cumulative = weights[0]; pick < candidates.Length - 1 && roll >= cumulative; cumulative += weights[++pick]) { }
                var scale = rng.NextFloat(0.85f, 1.15f);
                var picked = candidates[pick];
                if (picked < 0)
                {
                    var prop = Props[-picked - 1];
                    picked = prop.Species;
                    scale = rng.NextFloat(prop.Scale.x, math.max(prop.Scale.x, prop.Scale.y));
                }
                var rule = Rules[picked];
                Output[i] = new FloraInstance
                {
                    Position = new float3(xz.x, ground.Height, xz.y),
                    Yaw = rng.NextFloat(math.PI * 2f),
                    // Plants grow smaller towards the top of their range (a tree line).
                    Scale = scale * (0.55f + 0.45f * shrink[pick]),
                    Model = rule.FirstModel + rng.NextInt(rule.Variants),
                    Random = rng.NextFloat(),
                    Snow = surface.Snow
                };
            }
        }
    }
}
