using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Player;
using Punchy.Rendering;
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
    public partial class FloraSystem : MonoBehaviour
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
            [Tooltip("W-102: plants cast shadows only this near the camera (m), and never past the sun's shadow " +
                     "distance. The shadow passes draw their own short list, not the camera's.")]
            public float ShadowCastDistance = 60f;
            [Tooltip("W-102: past this share of Radius, natural plants (not rocks or built things) thin out, each " +
                     "dithering away at its own distance. Keep it beyond the LOD 2 change and the shadow cast distance.")]
            public float ThinFrom = 0.5f;
            [Tooltip("W-102: the share of natural plants still drawn at Radius.")]
            public float ThinTo = 0.3f;
        }

        public Material Material;
        public ComputeShader Culling;
        public LayerSettings Ground = new LayerSettings { CellSize = 0.7f, TileSize = 14f, Radius = 70f, Lod1Distance = 18f, Lod2Distance = 1000f, CastShadows = false, ThinFrom = 0.4f, ThinTo = 0.25f };
        public LayerSettings Shrubs = new LayerSettings { CellSize = 2f, TileSize = 40f, Radius = 520f, Lod1Distance = 30f, Lod2Distance = 110f, ShadowCastDistance = 40f, ThinFrom = 0.5f, ThinTo = 0.25f };
        public LayerSettings Trees = new LayerSettings { CellSize = 4.5f, TileSize = 108f, Radius = 1500f, Lod1Distance = 55f, Lod2Distance = 220f, ShadowCastDistance = 60f, ThinFrom = 0.25f, ThinTo = 0.5f };
        [Tooltip("Placement cells started per frame across all layers. Placement runs on worker threads in the " +
                 "background; too many at once crowds out the terrain's own jobs and causes hitches.")]
        public int CellsPerFrame = 6000;
        [Tooltip("Plants this far outside the view still draw into the shadow maps, so off-screen trees cast shadows.")]
        public float ShadowMargin = 30f;

        /// <summary>W-136 ground contact tunables for one kind of model: how dark the ground gets at its seam and how
        /// far the darkening spreads. One set for every surface (terrain chunks, cave chunks, room tiles): ContactShade.</summary>
        [Serializable]
        public class ContactSettings
        {
            [Tooltip("1 darkens the seam to ContactShade.Seam, 0 turns it off, 2 doubles the darkening.")]
            [Range(0f, 2f)] public float Strength = 1f;
            [Tooltip("1 spreads the darkening about the contact's width (a trunk's, a wall's, a post's) from its edge.")]
            [Range(0f, ContactShade.MaxReachScale)] public float Reach = 1f;
        }
        [Header("Ground contacts (W-136): darkening where things meet the ground, rock or tiles")]
        public ContactSettings ContactPlants = new ContactSettings();
        public ContactSettings ContactRocks = new ContactSettings();
        public ContactSettings ContactBuildings = new ContactSettings();
        public ContactSettings ContactProps = new ContactSettings();
        /// <summary>Indirect draws per variant: its 3 LODs for the camera, then LOD 0 and 1 shadow lists (W-102), then its
        /// impostor (R-7: one quad, FloraSystem.Impostors.cs; given no room in a layer without impostors).</summary>
        const int Draws = 6;
        const int ImpostorDraw = 5;
        static int DrawLod(int draw) => draw < 3 ? draw : draw - 3;
        [Tooltip("Solid plants within this distance of the player are given to player movement as obstacles.")]
        public float CollisionRange = 24f;
        /// <summary>Plants whose collision is stacked slices (FloraModel.ColliderBase) only count within this distance:
        /// more than the CollisionRange/4 the player moves between rebuilds plus a shard's reach from its base.</summary>
        const float ShardCollisionRange = 14f;

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
            public float[] ModelMaxScale, ModelMinScale;
            public GraphicsBuffer Instances, Visible, Args, Regions, Bounds;
            /// <summary>Refactor 3.5c: each model's reaches (see ModelReach) and, per plant, whether it cast last frame.</summary>
            public GraphicsBuffer ReachBuffer, CastState;
            public float4[] Reach;
            /// <summary>3.7 step 3: each model's real-time shadow reach where the cache takes over, and whether its crown
            /// may cast through holes (see ModelShadow; FloraCull.compute's _ModelShadow).</summary>
            public float2[] Sharp;
            public GraphicsBuffer SharpBuffer;
            /// <summary>R-5: how many times its plants were uploaded (the wide shadow map's cull goes stale with each).</summary>
            public int Uploads;
            /// <summary>
            /// The draw arguments the culling shader counts instances into (structured, so D3D11 can write it: an
            /// IndirectArguments buffer can't also be Raw there), copied into Args for the draws once culled.
            /// </summary>
            public GraphicsBuffer Counts;
            /// <summary>The draw arguments as last uploaded: each variant × LOD's mesh range, and its region of the
            /// visible list as startInstance (the shader adds it), so draws need no per-draw property block.</summary>
            public GraphicsBuffer.IndirectDrawIndexedArgs[] ArgsData;
            /// <summary>W-107's cheap far caster: the second shadow list draws the far level of detail (see SetCheapArgs).</summary>
            public bool CheapArgs;
            /// <summary>This layer's copy of the flora material, holding its buffers. Every draw in the layer shares it,
            /// so consecutive draws don't re-apply the shader (a property block per draw made each one a state change).</summary>
            public Material Material;
            /// <summary>Refactor 3.5: the same, with FLORA_STATIC on, for models that never move (FloraModel.Static).</summary>
            public Material StaticMaterial;

            // Tiles being placed by jobs in the background (one batch at a time).
            public JobHandle Placing;
            public NativeArray<int2> PlacingTiles;
            /// <summary>R-6 row 4: where the eye was when the batch was started (its far tiles are trimmed by it).</summary>
            public float2 PlacingEye;
            /// <summary>
            /// R-6 row 4: how far the layer streams: its Radius, or on to where its biggest always-drawn variant is still
            /// DetailPolicy.FloraEdgePixels across, never past the terrain edge. Tiles past Radius ("far tiles") keep just
            /// the plants big enough to be drawn from where they were placed (FarTiles: that distance; FarPpm: the
            /// pixels per metre they were trimmed at), and are placed again whole once the eye comes much closer.
            /// </summary>
            public float FarRadius, FarPpm;
            public readonly Dictionary<int2, float> FarTiles = new Dictionary<int2, float>();
            public NativeArray<FloraInstance> PlacingOutput;
            public int PlacingFrames;
            /// <summary>R-7: its models' impostor atlases (FloraSystem.Impostors.cs), or null for a layer without.</summary>
            public ImpostorAtlas Impostors;
            /// <summary>R-7: each model's impostor reach (FloraCull.compute's _ModelImpostor): all 0 until its bake is done.</summary>
            public float[] ImpostorReach;
            public GraphicsBuffer ImpostorBuffer;

            public void Release()
            {
                Impostors?.Release();
                Impostors = null;
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
                ReachBuffer?.Release();
                CastState?.Release();
                SharpBuffer?.Release();
                ImpostorBuffer?.Release();
                ReachBuffer = CastState = SharpBuffer = ImpostorBuffer = null;
                Instances = Visible = Args = Counts = Regions = Bounds = null;
                if (Material != null)
                    UnityEngine.Object.Destroy(Material);
                if (StaticMaterial != null)
                    UnityEngine.Object.Destroy(StaticMaterial);
                Material = StaticMaterial = null;
            }
        }

        readonly List<FloraModel> models = new List<FloraModel>();
        /// <summary>Per model: a natural plant (trees through mushrooms) that thins out with distance (W-102).</summary>
        readonly List<bool> thins = new List<bool>();
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
        /// <summary>W-67 (#79): kit buildings whose bounds come within this of a player have their boxes placed for collision.</summary>
        const float KitBoxRange = 12f;
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
        static readonly int ShadowStillId = Shader.PropertyToID("_ShadowStill");
        static readonly int CheapHolesId = Shader.PropertyToID("_FloraCheapHoles");
        static readonly int ThinId = Shader.PropertyToID("_Thin");
        static readonly int ModelReachId = Shader.PropertyToID("_ModelReach");
        static readonly int ModelShadowId = Shader.PropertyToID("_ModelShadow");
        static readonly int SharpId = Shader.PropertyToID("_Sharp");
        static readonly int CastStateId = Shader.PropertyToID("_FloraCastState");
        static readonly int ReachId = Shader.PropertyToID("_Reach");
        static readonly int CastHoldId = Shader.PropertyToID("_CastHold");
        static readonly int ModelImpostorId = Shader.PropertyToID("_ModelImpostor");

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

        /// <summary>W-127: a model's lit window panes (FloraModel.Windows; model index as placement gives it), or null.</summary>
        public List<Vector4> ModelWindows(int model) => model >= 0 && model < models.Count ? models[model].Windows : null;

        // ------------------------------------------------------------------------------------------- fountains' water

        /// <summary>A basin of water in a plant in a loaded tile (FloraModel.Pools: a fountain's or a well's), in the world.</summary>
        struct Basin
        {
            public int Layer;
            public int2 Tile;
            public float3 Centre;
            public float Radius;
            public float Yaw, Scale;
            public int Model, Pool;
            public bool Shown;
        }
        readonly List<Basin> basins = new List<Basin>();
        /// <summary>One water polygon per model and pool (its look and depth baked in), made when first drawn.</summary>
        readonly Dictionary<long, Mesh> basinMeshes = new Dictionary<long, Mesh>();
        /// <summary>
        /// A basin's water is drawn with the terrain's water while it is at least this big on screen (reference pixels across,
        /// at Detail 1, rule 20); smaller, the model's own polygon of the same shape stands in.
        /// </summary>
        const float BasinPixels = 16f;

        /// <summary>Notes the basins of a newly loaded tile's plants (turned and scaled as the plants are).</summary>
        void AddBasins(Layer l, int2 tile, NativeArray<FloraInstance> plants)
        {
            foreach (var plant in plants)
            {
                if (plant.Model < 0 || plant.Model >= models.Count || models[plant.Model].Pools.Count == 0)
                    continue;
                float c = math.cos(plant.Yaw), sn = math.sin(plant.Yaw);
                var pools = models[plant.Model].Pools;
                for (int p = 0; p < pools.Count; p++)
                {
                    var pool = pools[p];
                    var offset = new float2(pool.Centre.x * c - pool.Centre.z * sn, pool.Centre.x * sn + pool.Centre.z * c) * plant.Scale;
                    basins.Add(new Basin
                    {
                        Layer = l.Index,
                        Tile = tile,
                        Centre = plant.Position + new float3(offset.x, pool.Centre.y * plant.Scale, offset.y),
                        Radius = pool.Radius * plant.Scale,
                        Yaw = plant.Yaw,
                        Scale = plant.Scale,
                        Model = plant.Model,
                        Pool = p
                    });
                }
            }
        }

        /// <summary>
        /// Fountains' and wells' water while big enough on screen (BasinPixels), in the terrain's water material, so it
        /// ripples, reflects and catches the light as all other water does (Z-12; it was a shiny disc). W-148: the polygon
        /// of the basin's wall, turned as the plant is, not a disc.
        /// </summary>
        void DrawBasins(float3 camPos)
        {
            // (Only called once TryGetWorld has made the queries.)
            if (basins.Count == 0 || configQuery.IsEmpty)
                return;
            var water = configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value;
            if (water == null)
                return;
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            var pixels = BasinPixels / math.max(view.Detail, 0.1f);
            for (int i = 0; i < basins.Count; i++)
            {
                var basin = basins[i];
                var d = math.max(math.distance(basin.Centre, camPos) - basin.Radius, 0f);
                var shown = Punchy.Settings.DetailPolicy.Holds(
                    Punchy.Settings.DetailPolicy.ScreenSize(basin.Radius * 2f, d, view.PixelsPerMetre), pixels, basin.Shown);
                if (shown != basin.Shown)
                {
                    basin.Shown = shown;
                    basins[i] = basin;
                }
                if (!shown)
                    continue;
                var key = ((long)basin.Model << 8) | (uint)basin.Pool;
                if (!basinMeshes.TryGetValue(key, out var mesh))
                {
                    var pool = models[basin.Model].Pools[basin.Pool];
                    mesh = CaveSystem.MakeBasinMesh(pool.Radius, pool.Sides, pool.Depth, pool.Look);
                    basinMeshes[key] = mesh;
                }
                var rp = new RenderParams(water)
                {
                    worldBounds = new Bounds(basin.Centre, new Vector3(basin.Radius * 2f, 1f, basin.Radius * 2f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true
                };
                // The plant's yaw turns its mesh the other way round the up axis (see AddBasins' offsets).
                Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.TRS(basin.Centre, Quaternion.AngleAxis(-basin.Yaw * Mathf.Rad2Deg, Vector3.up), Vector3.one * basin.Scale));
            }
        }

        void Teardown()
        {
            ReleaseShadowCache();
            ReleaseWide();
            ReleaseImpostorBake();
            foreach (var l in layers)
                l?.Release();
            ReleaseImpostorQuad();
            Array.Clear(layers, 0, layers.Length);
            basins.Clear();
            foreach (var mesh in basinMeshes.Values)
                Destroy(mesh);
            basinMeshes.Clear();
            foreach (var m in models)
                m.Destroy();
            models.Clear();
            thins.Clear();
            species.Clear();
            Punchy.Terrain.GroundContacts.Withdraw(placement);
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

        /// <summary>Hands the contact tunables to ContactShade; a change re-meshes the loaded terrain, caves and rooms once.</summary>
        void PushContacts()
        {
            var strength = new float4(ContactPlants.Strength, ContactRocks.Strength, ContactBuildings.Strength, ContactProps.Strength);
            var reach = new float4(ContactPlants.Reach, ContactRocks.Reach, ContactBuildings.Reach, ContactProps.Reach);
            ContactShade.Set(strength, reach);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            PushContacts();
            Shader.SetGlobalFloat(DetailOffId, NoDetail ? 1f : 0f);
            if (cam == null || Material == null || Culling == null || !TryGetWorld(out var source, out var palette))
                return;

            // Rebuild everything when the terrain is re-baked or (in the editor) a biome's flora or a species changes.
            if (built && (!source.Equals(lastSource) || ConfigChanged(palette)))
                Teardown();
            // W-79b (#102): not before the island is baked. The first build used to run on the old terrain (~4 s, its
            // plants not pregrown) and was thrown away when the bake replaced it; now the one build follows the bake,
            // whose plants grew on worker threads beside it.
            if (!built && WaitForIsland && Punchy.Terrain.IslandV2.ActiveVersion == 0)
                return;
            if (!built)
            {
                Build(palette);
                lastSource = source;
                configStamp = ConfigStamp(palette);
            }
            BakeImpostors();

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
                using (DrawMarker.Auto())
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

        /// <summary>When off, the plants are built before the island is baked too, and again after it (for before/after timing).</summary>
        public static bool WaitForIsland = System.Environment.GetEnvironmentVariable("PUNCHY_FLORAWAIT") != "0";

        /// <summary>When off, Build grows every variant on the main thread (for before/after timing).</summary>
        public static bool PregrowOn = true;
        /// <summary>Refactor 3.5 A/B ("-nostaticflora"): every model draws animated, as before the static variant.</summary>
        public static readonly bool NoStatic = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-nostaticflora") >= 0;
        /// <summary>Refactor 3.5c A/B ("-oldfloralod"): mesh changes, draw range and shadows by layer distance, as before the class rules.</summary>
        public static readonly bool OldLod = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-oldfloralod") >= 0;
        /// <summary>Refactor 3.5 measurement ("-nofloradetail"): the flora shader skips its procedural surface detail (weathering, wood,
        /// cloth, strata), to bound what baking it could save. Set as a global once (Flora.shader _PunchyFloraDetailOff).</summary>
        public static readonly bool NoDetail = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-nofloradetail") >= 0;
        static readonly int DetailOffId = Shader.PropertyToID("_PunchyFloraDetailOff");

        /// <summary>
        /// Starts growing every flora and cave plant variant the palette uses on worker threads, so the bake can run
        /// beside it; Build (and the caves) then only turn the grown parts into meshes. Returns the variants started.
        /// </summary>
        public static int Pregrow(BiomePalette palette)
        {
            if (!PregrowOn || palette == null)
                return 0;
            var species = new List<FloraSpecies>();
            FloraPlacement.CollectSpecies(palette, species);
            var list = new List<(FloraSpecies, int)>();
            foreach (var s in species)
                list.Add((s, s.Variants));
            foreach (var s in Punchy.Terrain.CaveFloraTable.Collect(palette.Landmarks))
                if (s != null)
                    list.Add((s, Punchy.Terrain.CaveSystem.PlantVariants));
            return FloraGenerator.Pregrow(list);
        }

        void Build(BiomePalette palette)
        {
            // Species ordered by layer, so each layer's models are contiguous.
            var cellSizes = new float3(FloraPlacement.CellSize(Ground.TileSize, Ground.CellSize),
                                       FloraPlacement.CellSize(Shrubs.TileSize, Shrubs.CellSize),
                                       FloraPlacement.CellSize(Trees.TileSize, Trees.CellSize));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            placement = FloraPlacement.Build(palette, cellSizes, species);
            var placed = clock.Elapsed.TotalMilliseconds;
            foreach (var s in species)
                for (int v = 0; v < s.Variants; v++)
                {
                    models.Add(FloraGenerator.Grow(s, v));
                    thins.Add(s.Form <= FloraForm.Mushroom);
                }
            var grown = clock.Elapsed.TotalMilliseconds;
            BuildSupportGrids();
            long vertices = 0;
            foreach (var m in models)
                foreach (var lod in m.Lods)
                    vertices += lod != null ? lod.vertexCount : 0;
            Debug.Log($"[flora] build {clock.Elapsed.TotalMilliseconds:F0} ms (placement {placed:F0}, grow {grown - placed:F0}, support grids {clock.Elapsed.TotalMilliseconds - grown:F0}); {models.Count} variants, {vertices} vertices");
            // Refactor 3.5a: how much is static (no vertex motion but door leaves) and how much moves, by variants and LOD 0 vertices.
            int staticModels = 0, staticVerts = 0, movingVerts = 0;
            foreach (var m in models)
            {
                var v0 = m.Lods[0] != null ? m.Lods[0].vertexCount : 0;
                if (m.Static) { staticModels++; staticVerts += v0; } else movingVerts += v0;
            }
            Debug.Log($"[flora] static (refactor 3.5): {staticModels} of {models.Count} variants never move, {staticVerts} LOD0 vertices static vs {movingVerts} moving{(NoStatic ? "; -nostaticflora: all drawn animated" : "")}");
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
                QueueImpostors(layer);
            }
            built = true;
            obstaclesDirty = true;
            // W-136: the terrain darkens the ground where these meet it (re-meshes its chunks once).
            Punchy.Terrain.GroundContacts.Publish(placement, models);
        }

        void CreateDrawBuffers(Layer layer)
        {
            var draws = math.max(1, layer.ModelCount * Draws);
            layer.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            layer.Counts = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, draws, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            var args = new GraphicsBuffer.IndirectDrawIndexedArgs[draws];
            var bounds = new float3[math.max(1, layer.ModelCount)];
            if (layer.Material == null)
                layer.Material = new Material(Material) { name = $"Flora layer {layer.Index}" };
            if (layer.StaticMaterial == null)
            {
                layer.StaticMaterial = new Material(Material) { name = $"Flora layer {layer.Index} (static)" };
                layer.StaticMaterial.EnableKeyword("FLORA_STATIC");
            }
            for (int m = 0; m < layer.ModelCount; m++)
            {
                var model = models[layer.ModelBase + m];
                bounds[m] = new float3(model.Radius, model.CentreY, thins[layer.ModelBase + m] ? 1f : 0f);
                for (int draw = 0; draw < Draws; draw++)
                {
                    var mesh = draw == ImpostorDraw ? ImpostorQuad() : model.Lods[DrawLod(draw)];
                    var d = m * Draws + draw;
                    args[d] = new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = mesh.GetIndexCount(0),
                        startIndex = mesh.GetIndexStart(0),
                        baseVertexIndex = mesh.GetBaseVertex(0)
                    };
                }
            }
            layer.ArgsData = args;
            layer.CheapArgs = false;
            layer.Args.SetData(args);
            layer.Counts.SetData(args);
            layer.Bounds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float) * 3);
            layer.Bounds.SetData(bounds);
            layer.Reach = new float4[bounds.Length];
            for (int m = 0; m < layer.ModelCount; m++)
                layer.Reach[m] = ModelReach(models[layer.ModelBase + m], layer);
            layer.ReachBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float) * 4);
            layer.ReachBuffer.SetData(layer.Reach);
            layer.Sharp = new float2[bounds.Length];
            for (int m = 0; m < layer.ModelCount; m++)
                layer.Sharp[m] = ModelShadow(models[layer.ModelBase + m]);
            layer.SharpBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float) * 2);
            layer.SharpBuffer.SetData(layer.Sharp);
            layer.ImpostorReach = new float[bounds.Length];
            layer.ImpostorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bounds.Length, sizeof(float));
            layer.ImpostorBuffer.SetData(layer.ImpostorReach);
            layer.Regions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, draws, sizeof(uint));
        }

        /// <summary>
        /// A model's reaches at scale 1, per reference pixel-per-metre: x LOD 0 ends, y LOD 1 ends, z it stops being drawn,
        /// w it stops casting (DetailPolicy.FloraRule; FloraCull.compute multiplies by the view and the plant's scale).
        /// A layer without a coarsest level (Lod2Distance past Radius: the ground layer) never gets one.
        /// </summary>
        static float4 ModelReach(FloraModel model, Layer layer)
        {
            const float Never = 1e8f;
            var kind = model.DetailClass();
            var rule = Punchy.Settings.DetailPolicy.FloraRule(kind);
            // (A floor in metres, as a reach at the reference view, so it scales with the view and Detail like the rest.)
            var floor = Punchy.Settings.DetailPolicy.FloraFloor(kind) / Punchy.Settings.DetailPolicy.PixelsPerMetreAt(Punchy.Settings.DetailPolicy.ReferenceFov);
            var size = model.Radius * 2f;
            return new float4(math.max(size / rule.Geometry.Near, floor.x),
                layer.Settings.Lod2Distance >= layer.Settings.Radius ? Never : math.max(size / rule.Geometry.Mid, floor.y),
                rule.Geometry.Far > 0f ? size / rule.Geometry.Far
                    // R-6 row 4: always drawn: to the layer's radius at least, and past it while big enough on screen
                    // (negative, see FloraCull.compute). R-6 row 6 (Zach 11-09): the tree layer only; the ground and
                    // shrub layers stay cut at their radius as before (far shrubs cost ~3 ms of streaming per frame).
                    : layer.Index == FarReachLayer ? -size / Punchy.Settings.DetailPolicy.FloraEdgePixels : Never,
                size / rule.Shadow);
        }

        /// <summary>R-6 row 6: the one layer whose always-drawn variants reach past its radius (the trees).</summary>
        const int FarReachLayer = 2;

        /// <summary>R-6 row 4: always-drawn variants reach past their layer's radius by size (false: cut at the radius, as before).</summary>
        public static bool FarReach = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-nofarflora") < 0;

        /// <summary>
        /// 3.7 step 3: a model's shadow tier data at scale 1. x how far it keeps a real-time shadow, per reference
        /// pixel-per-metre, where the trees layer's cache takes over past the cascades (DetailPolicy.SharpReach; 0: only
        /// the floor, NearRange); y 0 for a solid caster, else a tree, whose crown casts with the cheap far caster's mesh:
        /// 1 a full crown, cast whole like its near levels, 2 an open crown (OpenCrown), which also casts through holes.
        /// </summary>
        static float2 ModelShadow(FloraModel model)
        {
            var kind = model.DetailClass();
            var rule = Punchy.Settings.DetailPolicy.FloraRule(kind);
            var tree = kind == Punchy.Settings.DetailPolicy.FloraClass.Tree;
            return new float2(Punchy.Settings.DetailPolicy.SharpReach(rule, model.Radius * 2f),
                !tree ? 0f : model.Species != null && model.Species.Fullness < OpenCrown ? 2f : 1f);
        }

        /// <summary>R-3 row 3: below this Fullness a crown is open (birch-like, bare, desert trees) and its far shadow
        /// gets gaps; fuller crowns cast whole (their holes made the far shadows glitter and swim with the sway).</summary>
        const float OpenCrown = 0.6f;

        // ------------------------------------------------------------------------------------------- streaming

        float2 TileCentre(Layer l, int2 c) => ((float2)c + 0.5f) * l.Settings.TileSize;

        /// <summary>A tile's nearest ground distance from a point (0 inside it).</summary>
        static float TileNearest(Layer l, int2 c, float2 eye)
        {
            var min = (float2)c * l.Settings.TileSize;
            return math.distance(math.clamp(eye, min, min + l.Settings.TileSize), eye);
        }

        /// <summary>R-6 row 4: a far tile is trimmed for this share of its distance, and placed again inside it.</summary>
        const float FarRetrim = 0.8f;

        /// <summary>Orders tiles nearest the eye first (as TileCentre). One reused comparer: a capturing lambda allocated every frame (W-117 GC).</summary>
        sealed class TileOrder : IComparer<int2>
        {
            public float TileSize;
            public float2 Eye;
            public int Compare(int2 a, int2 b) =>
                math.distancesq(((float2)a + 0.5f) * TileSize, Eye).CompareTo(math.distancesq(((float2)b + 0.5f) * TileSize, Eye));
        }
        readonly TileOrder tileOrder = new TileOrder();

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
                l.FarRadius = l.Radius;
                // R-6 row 4: on to the biggest always-drawn variant's size reach (at its largest scale, by last frame's
                // view), within the terrain edge as TerrainStreamingSystem streams it.
                var ppm = FarPpmNow();
                if (ppm <= 0f || l.Reach == null || l.ModelMaxScale == null)
                    continue;
                var far = 0f;
                for (int m = 0; m < l.ModelCount; m++)
                    if (l.Reach[m].z < 0f)
                        far = math.max(far, -l.Reach[m].z * math.max(l.ModelMaxScale[m], 1.15f));
                l.FarRadius = math.max(l.Radius, math.min(far * ppm, TerrainEdge()));
            }
        }

        /// <summary>R-6 row 4: the reference pixels per metre far plants are trimmed by (0: no far plants).</summary>
        static float FarPpmNow()
        {
            if (OldLod || !FarReach)
                return 0f;
            var view = Punchy.Settings.DetailPolicy.Current.Data;
            return view.PixelsPerMetre * math.max(view.Detail, 0.1f);
        }

        /// <summary>R-6 row 4: how far the terrain streams (as WeatherSystem's haze), past which nothing is drawn.</summary>
        float TerrainEdge()
        {
            if (queryWorld == null || !queryWorld.IsCreated || !configQuery.TryGetSingleton<TerrainConfig>(out var config))
                return 1500f * Punchy.Settings.GraphicsSettings.View;
            return math.max(2, math.round(config.ViewRadiusChunks * Punchy.Settings.GraphicsSettings.View)) * config.ChunkSize;
        }

        /// <summary>Set when a flora keep-out circle changes (a colossus placed): every streamed tile is dropped once and re-placed.</summary>
        public static bool Replant;

        void Stream(in TerrainSource source, float2 camXZ)
        {
            // Tiles stream by ground distance on purpose (R-6 row 0): the cull measures 3D distance to each plant,
            // which is never shorter than the ground distance, so the ground ring always holds every plant the cull
            // could draw, and climbing doesn't drop and re-place tiles.
            // Unload tiles that fell out of range (or every tile once, when a keep-out circle changed: see Replant).
            var all = Replant;
            Replant = false;
            foreach (var l in layers)
            {
                scratch.Clear();
                var keep = l.FarRadius + l.Settings.TileSize * 1.5f;
                // R-6 row 4: far tiles go when the view's pixels per metre moved (they were trimmed by it), and each
                // when the eye comes inside the radius or well inside where it was trimmed, to be placed again.
                var ppm = FarPpmNow();
                var refar = l.FarTiles.Count > 0 && (ppm > l.FarPpm * 1.1f || ppm < l.FarPpm * 0.8f);
                foreach (var kv in l.Tiles)
                {
                    var gone = all || math.distance(TileCentre(l, kv.Key), camXZ) > keep;
                    if (!gone && l.FarTiles.TryGetValue(kv.Key, out var from))
                    {
                        var d = TileNearest(l, kv.Key, camXZ);
                        gone = refar || d < l.Radius || d < from * FarRetrim;
                    }
                    if (gone)
                        scratch.Add(kv.Key);
                }
                foreach (var key in scratch)
                {
                    l.Tiles[key].Dispose();
                    l.Tiles.Remove(key);
                    l.FarTiles.Remove(key);
                    // (A plain loop, not RemoveAll with a lambda: capturing l and key cost a closure per layer every frame.)
                    for (int b = basins.Count - 1; b >= 0; b--)
                        if (basins[b].Layer == l.Index && basins[b].Tile.Equals(key))
                            basins.RemoveAt(b);
                }
                if (scratch.Count > 0)
                {
                    l.Dirty = true;
                    obstaclesDirty |= l.Index > 0;
                }
            }

            // Placement runs in the background: collect each layer's finished batch, then start the next one with the
            // nearest missing tiles, within a shared per-frame budget (near layers first).
            // W-75: larger batches while the world first loads (no frame to protect before the player spawns).
            var budget = Punchy.Terrain.TerrainStreamingSystem.Loading ? CellsPerFrame * 4 : CellsPerFrame;
            // (R-6 row 4: two passes, every layer's tiles inside its radius first, then the far tiles with what's left,
            // so the shrubs' far ring never holds up the trees' near tiles.)
            for (int pass = 0; pass < 2; pass++)
            foreach (var l in layers)
            {
                if (l.ModelCount == 0)
                    continue;
                var cellsPerSide = (int)math.round(l.Settings.TileSize / l.Settings.CellSize);
                var perTile = cellsPerSide * cellsPerSide;
                if (pass == 0 && l.PlacingTiles.IsCreated)
                {
                    // Don't let a batch run on for long: the tiles nearest the camera may be waiting on it.
                    if (!l.Placing.IsCompleted && ++l.PlacingFrames < 3)
                        continue;
                    Harvest(l, perTile);
                }
                if (budget <= 0 || l.PlacingTiles.IsCreated || pass == 1 && l.FarRadius <= l.Radius)
                    continue;
                if (pass == 1 && l.FarTiles.Count == 0)
                    l.FarPpm = FarPpmNow();

                scratch.Clear();
                var outer = pass == 0 ? l.Radius : l.FarRadius;
                var r = (int)math.ceil(outer / l.Settings.TileSize) + 1;
                var centre = (int2)math.floor(camXZ / l.Settings.TileSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    var c = centre + new int2(x, z);
                    var d = TileNearest(l, c, camXZ);
                    if (!l.Tiles.ContainsKey(c) && d < outer && (pass == 0 || d >= l.Radius))
                        scratch.Add(c);
                }
                if (scratch.Count == 0)
                    continue;
                tileOrder.TileSize = l.Settings.TileSize;
                tileOrder.Eye = camXZ;
                scratch.Sort(tileOrder);
                var count = math.min(scratch.Count, math.max(1, budget / perTile));
                budget -= count * perTile;

                l.PlacingTiles = new NativeArray<int2>(count, Allocator.Persistent);
                for (int i = 0; i < count; i++)
                    l.PlacingTiles[i] = scratch[i];
                l.PlacingOutput = new NativeArray<FloraInstance>(count * perTile, Allocator.Persistent);
                l.PlacingFrames = 0;
                l.PlacingEye = camXZ;
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
                // R-6 row 4: a tile past the radius keeps only the plants big enough on screen to be drawn from a bit
                // closer than it is now (it's placed again whole before the eye gets nearer than that).
                var from = TileNearest(l, tile, l.PlacingEye);
                var trim = from >= l.Radius ? from * FarRetrim / math.max(l.FarPpm, 1e-3f) : -1f;
                if (trim >= 0f)
                    for (int i = 0; i < perTile; i++)
                    {
                        var m = cells[i].Model - l.ModelBase;
                        if (cells[i].Model >= 0 && !(l.Reach[m].z < 0f && -l.Reach[m].z * cells[i].Scale > trim))
                        {
                            var c = cells[i];
                            c.Model = -1;
                            cells[i] = c;
                        }
                    }
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
                if (trim >= 0f)
                    l.FarTiles[tile] = from;
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
            var minScales = new NativeArray<float>(l.ModelCount, Allocator.TempJob);
            new CountModelsJob { Plants = all, ModelBase = l.ModelBase, Counts = counts, Min = mins, Max = maxs, MaxScale = scales, MinScale = minScales }.Run();
            counts.CopyTo(l.ModelCounts);
            if (l.ModelMin == null || l.ModelMin.Length != l.ModelCount)
            {
                l.ModelMin = new float3[l.ModelCount];
                l.ModelMax = new float3[l.ModelCount];
                l.ModelMaxScale = new float[l.ModelCount];
                l.ModelMinScale = new float[l.ModelCount];
            }
            mins.CopyTo(l.ModelMin);
            maxs.CopyTo(l.ModelMax);
            scales.CopyTo(l.ModelMaxScale);
            minScales.CopyTo(l.ModelMinScale);
            minScales.Dispose();
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
                l.Visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity * (l.Impostors != null ? Draws : Draws - 1), sizeof(uint) * 2);
                l.CastState?.Release();
                l.CastState = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint));
                l.CastState.SetData(new uint[capacity]);
            }
            l.Instances.SetData(all, 0, 0, total);
            all.Dispose();
            l.Count = total;
            l.Uploads++;

            // Each variant gets room for all its plants in each draw (a plant appears at most once per draw).
            var regions = new uint[l.ModelCount * Draws];
            uint offset = 0;
            for (int m = 0; m < l.ModelCount; m++)
            for (int draw = 0; draw < Draws; draw++)
            {
                regions[m * Draws + draw] = offset;
                l.ArgsData[m * Draws + draw].startInstance = offset;
                if (draw != ImpostorDraw || l.Impostors != null)
                    offset += (uint)l.ModelCounts[m];
            }
            l.Regions.SetData(regions);
            l.Args.SetData(l.ArgsData);
            l.Counts.SetData(l.ArgsData);
            l.Material.SetBuffer(InstancesId, l.Instances);
            l.Material.SetBuffer(VisibleId, l.Visible);
            l.StaticMaterial.SetBuffer(InstancesId, l.Instances);
            l.StaticMaterial.SetBuffer(VisibleId, l.Visible);
            SetImpostorBuffers(l);
        }

        [BurstCompile]
        struct CountModelsJob : IJob
        {
            [ReadOnly] public NativeArray<FloraInstance> Plants;
            public int ModelBase;
            public NativeArray<int> Counts;
            public NativeArray<float3> Min, Max;
            public NativeArray<float> MaxScale, MinScale;

            public void Execute()
            {
                for (int m = 0; m < Min.Length; m++)
                {
                    Min[m] = new float3(float.MaxValue);
                    Max[m] = new float3(float.MinValue);
                    MinScale[m] = float.MaxValue;
                }
                for (int i = 0; i < Plants.Length; i++)
                {
                    var m = Plants[i].Model - ModelBase;
                    Counts[m]++;
                    Min[m] = math.min(Min[m], Plants[i].Position);
                    Max[m] = math.max(Max[m], Plants[i].Position);
                    MaxScale[m] = math.max(MaxScale[m], Plants[i].Scale);
                    MinScale[m] = math.min(MinScale[m], Plants[i].Scale);
                }
            }
        }

        // ------------------------------------------------------------------------------------------- drawing

        /// <summary>
        /// W-107 (FloraShadowOptions.CheapFarCaster): points every model's second shadow list at its far level of detail
        /// (on) or back at LOD 1 (off). Only the mesh range changes; Upload keeps the visible-list offsets.
        /// </summary>
        void SetCheapArgs(Layer l, bool cheap)
        {
            for (int m = 0; m < l.ModelCount; m++)
            {
                var mesh = models[l.ModelBase + m].Lods[CheapModel(l, m, cheap) ? 2 : 1];
                ref var a = ref l.ArgsData[m * Draws + 4];
                a.indexCountPerInstance = mesh.GetIndexCount(0);
                a.startIndex = mesh.GetIndexStart(0);
                a.baseVertexIndex = mesh.GetBaseVertex(0);
            }
            l.Args.SetData(l.ArgsData);
            l.Counts.SetData(l.ArgsData);
            l.CheapArgs = cheap;
        }

        /// <summary>Whether this model casts with the cheap far caster's mesh: trees only since 3.7 step 3 (built things
        /// and rocks now cast in real time further out, with their own reduced mesh); the whole layer under -oldfloralod.</summary>
        bool CheapModel(Layer l, int m, bool cheap) => cheap && (OldLod || l.Sharp == null || l.Sharp[m].y > 0f);

        /// <summary>The CPU side of the per-frame cull and draw submission (R-7's flora ms; the GPU side shows in the frame).</summary>
        static readonly Unity.Profiling.ProfilerMarker DrawMarker = new("FloraSystem.Draw");

        void Draw(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++)
                planeVectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            var camPos = cam.transform.position;
            var clear = Culling.FindKernel("Clear");
            var cull = Culling.FindKernel("Cull");
            Punchy.Settings.DetailPolicy.SetView(cam);
            ref readonly var view = ref Punchy.Settings.DetailPolicy.Current.Data;
            // Refactor 3.5c: reference pixels per metre at the graphics settings (DetailPolicy.Decide's scaling; 0: the layers' distances).
            var reachGeometry = OldLod ? 0f : view.PixelsPerMetre * math.max(view.Detail, 0.1f);
            var reachShadow = view.PixelsPerMetre * math.max(view.Shadows, 0.1f);
            Culling.SetVector(ReachId, new Vector4(reachGeometry, reachShadow, 0f, 0f));
            Culling.SetFloat(CastHoldId, OldLod ? 0f : 1f);
            var shadowDistance = QualitySettings.shadowDistance;
            if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                shadowDistance = urp.shadowDistance;
            // W-107: where the shadow lists stop swaying (0: never). The cull does it once per plant, not per vertex.
            Culling.SetFloat(ShadowStillId, FloraShadowOptions.StillFarShadows ? FloraShadowOptions.StillFrom : 0f);
            // W-107: where the cheap far caster's cards get holes (0: off).
            Shader.SetGlobalVector(CheapHolesId, new Vector4(camPos.x, camPos.y, camPos.z,
                FloraShadowOptions.CheapFarCaster ? FloraShadowOptions.NearRange : 0f));

            foreach (var l in layers)
            {
                if (l.Count == 0 || l.ModelCount == 0)
                    continue;
                var s = l.Settings;
                var draws = l.ModelCount * Draws;
                Culling.SetInt(DrawCountId, draws);
                Culling.SetBuffer(clear, ArgsId, l.Counts);
                Culling.Dispatch(clear, (draws + 63) / 64, 1, 1);

                Culling.SetInt(CountId, l.Count);
                Culling.SetInt(ModelBaseId, l.ModelBase);
                Culling.SetVectorArray(PlanesId, planeVectors);
                Culling.SetVector(CameraId, camPos);
                Culling.SetVector(LodId, new Vector4(l.Lod1Distance, l.Lod2Distance, l.FarRadius, l.Radius));
                // (By rule a variant's size says how far it casts, up to the sun's shadow distance; the layer's value is the old rule.)
                var cast = s.CastShadows ? (OldLod ? math.min(s.ShadowCastDistance, shadowDistance) : shadowDistance) : 0f;
                // W-107's shadow cache: past the real-time tier, trees cast into the cache instead (ShadowCache.cs). 3.7
                // step 3: that tier ends by each variant's size on screen (DetailPolicy.ShadowTier), never inside the near
                // range; under -oldfloralod at the near range, as before.
                var tiered = FloraShadowOptions.CachedFarShadows && l.Index == CacheLayer;
                if (tiered && OldLod)
                    cast = math.min(cast, FloraShadowOptions.NearRange);
                Culling.SetVector(SharpId, tiered ? SharpTier(shadowDistance, 0f) : Vector4.zero);
                // W-107's cheap far caster: past the near range, trees cast with their far level of detail.
                var cheap = FloraShadowOptions.CheapFarCaster && l.Index == 2 && cast > 0f;
                if (cheap != l.CheapArgs)
                    SetCheapArgs(l, cheap);
                var lod0Until = cheap ? math.min(FloraShadowOptions.NearRange, l.Lod1Distance * 1.075f) : l.Lod1Distance;
                Culling.SetVector(ShadowId, new Vector4(cast, ShadowMargin, cheap ? lod0Until : 0f, 0f));
                Culling.SetVector(ThinId, new Vector4(s.ThinFrom * l.Radius, s.ThinTo, 0f, 0f));
                Culling.SetBuffer(cull, InstancesId, l.Instances);
                Culling.SetBuffer(cull, VisibleId, l.Visible);
                Culling.SetBuffer(cull, ArgsId, l.Counts);
                Culling.SetBuffer(cull, RegionsId, l.Regions);
                Culling.SetBuffer(cull, BoundsId, l.Bounds);
                Culling.SetBuffer(cull, ModelReachId, l.ReachBuffer);
                Culling.SetBuffer(cull, ModelShadowId, l.SharpBuffer);
                Culling.SetBuffer(cull, CastStateId, l.CastState);
                Culling.SetBuffer(cull, ModelImpostorId, l.ImpostorBuffer);
                Culling.Dispatch(cull, (l.Count + 63) / 64, 1, 1);
                Graphics.CopyBuffer(l.Counts, l.Args);

                // Each model's draws are bounded by where its plants actually are, so Unity culls them per pass and
                // shadow cascade, and a level of detail none of them can be at (by distance, with room for the
                // crossfades) isn't drawn at all: a draw costs a pass even when the GPU cull leaves it no plants.
                float3 eye = camPos;
                float Near(float d) => d * 1.1f + 5f;
                // (Refactor 3.5: the animated models first, then the static ones, so each material's draws come together
                // and the two don't alternate draw by draw.)
                for (int k = 0; k < l.ModelCount * 2; k++)
                {
                    var m = k % l.ModelCount;
                    if (l.ModelCounts[m] == 0)
                        continue;
                    var model = models[l.ModelBase + m];
                    if ((model.Static && !NoStatic) != (k >= l.ModelCount))
                        continue;
                    var reach = (model.Radius + math.abs(model.CentreY)) * l.ModelMaxScale[m];
                    var min = l.ModelMin[m] - reach;
                    var max = l.ModelMax[m] + reach;
                    var nearest = math.distance(math.clamp(eye, min, max), eye);
                    var farthest = math.length(math.max(math.abs(eye - min), math.abs(eye - max)));
                    if (nearest > Near(l.FarRadius))
                        continue;
                    var bounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));
                    // (Refactor 3.5: a model that never moves skips the wind maths; "-nostaticflora" draws all as before.)
                    var material = k >= l.ModelCount ? l.StaticMaterial : l.Material;
                    // The range this model's own plants can be at each level (largest and smallest scale): the layer's
                    // distances under the old rule. Past modelDrawn the cull leaves nothing, so nothing is drawn.
                    var lodScale = reachGeometry * l.Reach[m];
                    var modelLod1Near = OldLod ? l.Lod1Distance : lodScale.x * l.ModelMinScale[m];
                    var modelLod1Far = OldLod ? l.Lod1Distance : lodScale.x * l.ModelMaxScale[m];
                    var modelLod2Near = OldLod ? l.Lod2Distance : lodScale.y * l.ModelMinScale[m];
                    var modelLod2Far = OldLod ? l.Lod2Distance : lodScale.y * l.ModelMaxScale[m];
                    // (R-6 row 4: an always-drawn variant from the radius on to its size reach, see FloraCull.compute.)
                    var modelDrawn = OldLod ? l.Radius : l.Reach[m].z < 0f
                        ? math.clamp(-lodScale.z * l.ModelMaxScale[m], l.Radius, l.FarRadius)
                        : math.min(l.Radius, lodScale.z * l.ModelMaxScale[m]);
                    if (nearest > Near(modelDrawn))
                        continue;
                    for (int lod = 0; lod < 3; lod++)
                    {
                        if (lod == 2 && modelLod2Near >= modelDrawn)
                            continue;
                        var lodNear = lod == 0 ? 0f : lod == 1 ? modelLod1Near : modelLod2Near;
                        var lodFar = lod == 0 ? modelLod1Far : lod == 1 && modelLod2Far < modelDrawn ? modelLod2Far : modelDrawn;
                        if (nearest > Near(lodFar) || farthest < lodNear * 0.9f - 5f)
                            continue;
                        var rp = new RenderParams(material)
                        {
                            worldBounds = bounds,
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = true
                        };
                        Graphics.RenderMeshIndirect(rp, model.Lods[lod], l.Args, 1, m * Draws + lod);
                    }
                    // R-7: its impostor, past its coarsest mesh (as the cull: never nearer than 1.2 x its start).
                    if (!OldLod && l.ImpostorReach != null && l.ImpostorReach[m] > 0f && l.Impostors?.Material != null)
                    {
                        var impNear = math.max(reachGeometry * l.ImpostorReach[m], lodScale.y * 1.2f) * l.ModelMinScale[m] * 0.87f * 0.925f;
                        if (impNear < modelDrawn && farthest >= impNear - 5f)
                        {
                            var rp = new RenderParams(l.Impostors.Material)
                            {
                                worldBounds = bounds,
                                shadowCastingMode = ShadowCastingMode.Off,
                                receiveShadows = true
                            };
                            Graphics.RenderMeshIndirect(rp, ImpostorQuad(), l.Args, 1, m * Draws + ImpostorDraw);
                        }
                    }
                    // The shadow lists (W-102), bounded to the cast distance so cascades they can't reach skip them.
                    // Small props (fences, palisades, signposts...) cast none: the shadow pass's cost is per draw.
                    // (The rule's cast reach, with its hysteresis; the shadow list's change to its second level likewise.)
                    var modelCast = OldLod ? cast : math.min(cast, reachShadow * l.Reach[m].w * l.ModelMaxScale[m] * 1.18f);
                    if (tiered && !OldLod)
                        modelCast = math.min(modelCast, math.clamp(reachShadow * l.Sharp[m].x * l.ModelMaxScale[m],
                            FloraShadowOptions.NearRange, math.max(shadowDistance, FloraShadowOptions.NearRange)) * 1.18f);
                    var cheapModel = CheapModel(l, m, cheap);
                    var modelLod0Until = OldLod ? lod0Until : cheapModel ? math.min(FloraShadowOptions.NearRange, modelLod1Far * 1.075f) : modelLod1Far * 1.075f;
                    if (modelCast <= 0f || !model.CastsShadows)
                        continue;
                    var castReach = modelCast + reach + ShadowMargin;
                    var smin = math.max(min, eye - castReach);
                    var smax = math.min(max, eye + castReach);
                    if (math.any(smin > smax))
                        continue;
                    var shadowBounds = new Bounds((Vector3)((smin + smax) * 0.5f), (Vector3)(smax - smin));
                    for (int lod = 0; lod < 2; lod++)
                    {
                        if (nearest > Near(modelCast) || lod == 0 && nearest > Near(modelLod0Until))
                            continue;
                        var rp = new RenderParams(material)
                        {
                            worldBounds = shadowBounds,
                            shadowCastingMode = ShadowCastingMode.ShadowsOnly,
                            receiveShadows = false
                        };
                        Graphics.RenderMeshIndirect(rp, model.Lods[lod == 1 && cheapModel ? 2 : lod], l.Args, 1, m * Draws + 3 + lod);
                    }
                }
            }
            DrawShadowCache(cam, camPos);
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
            if (!em.HasBuffer<KitBox>(obstacleEntity))
                em.AddBuffer<KitBox>(obstacleEntity);
            var buffer = em.GetBuffer<FloraSupport>(obstacleEntity);
            buffer.Clear();
            // W-67 (#79): the kit buildings' boxes near any player, placed in the world (same refresh as the tops).
            var kitBoxes = em.GetBuffer<KitBox>(obstacleEntity);
            kitBoxes.Clear();
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
                        if (inst.Model < 0 || inst.Model >= modelGrid.Length)
                            continue;
                        var nearest = float.MaxValue;
                        foreach (var c in centres)
                            nearest = math.min(nearest, math.distancesq(inst.Position.xz, c));
                        var model = models[inst.Model];
                        if (model.Boxes.Count > 0 && nearest < math.square(KitBoxRange + model.Radius * inst.Scale))
                            foreach (var box in model.Boxes)
                                kitBoxes.Add(KitCollision.Placed(box, inst.Position, inst.Yaw, inst.Scale));
                        if (modelGrid[inst.Model] >= 0 && nearest < SupportRange * SupportRange)
                            buffer.Add(new FloraSupport { Position = inst.Position, Yaw = inst.Yaw, Scale = inst.Scale, Grid = modelGrid[inst.Model], Kit = model.Boxes.Count > 0 });
                    }
                }
            }
            // W-96 (#35): the blind pygmies' wall niches in the bat folk chambers are solid too.
            BatFolkHomes.AddKitBoxes(lastSource, centres, kitBoxes);
            CaveRoomKit.AddKitBoxes(lastSource, centres, kitBoxes);
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
        internal static Mesh BoltMesh()
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
            // W-101 row 4 (#40): a rolling colossus (the salt ball) in reach rebuilds the buffer five times a second.
            if (Time.time >= nextMovingObstacles && Punchy.Fauna.ColossusCollision.MovingNear(centre, CollisionRange))
            {
                nextMovingObstacles = Time.time + 0.2f;
                obstaclesDirty = true;
            }
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
                        // Stacked slices (leaning shards) cost several obstacles each: only near the player. The
                        // buffer is rebuilt every CollisionRange/4 moved, so this still covers everything in reach.
                        if (model.ColliderBase.Count > 0 && math.distance(inst.Position.xz, centre) > ShardCollisionRange)
                            continue;
                        // Shaped plants (rocks): their cylinders, turned and scaled as the plant is drawn.
                        float c = math.cos(inst.Yaw), sn = math.sin(inst.Yaw);
                        for (int ci = 0; ci < model.Colliders.Count; ci++)
                        {
                            var col = model.Colliders[ci];
                            var y0 = ci < model.ColliderBase.Count ? model.ColliderBase[ci] : 0f;
                            var offset = new float2(col.x * c - col.y * sn, col.x * sn + col.y * c) * inst.Scale;
                            buffer.Add(new FloraObstacle
                            {
                                Position = inst.Position + new float3(offset.x, y0 * inst.Scale, offset.y),
                                Radius = col.z * inst.Scale,
                                Height = (col.w - y0) * inst.Scale,
                                Kit = model.Boxes.Count > 0
                            });
                        }
                    }
                }
            }
            // W-101 row 4 (#40): the colossi's columns, slid round like trunks.
            Punchy.Fauna.ColossusCollision.AddNear(buffer, centre, CollisionRange);
        }

        float nextMovingObstacles;

        /// <summary>
        /// W-106 (#131): trees of the given species within range of a point, for cosmetic effects (the Ember Forest's
        /// night fires): the trunk's foot, its top as drawn (the trunk's lean), its height and crown radius (scaled),
        /// and a seed that stays the same while the tree stands. Appends to <paramref name="into"/>; no allocation.
        /// </summary>
        public void TreesNear(float2 centre, float range, HashSet<FloraSpecies> species,
                              List<(float3 foot, float3 top, float height, float crown, int seed)> into)
        {
            for (int li = 1; li < 3; li++)
            {
                var l = layers[li];
                if (l == null)
                    continue;
                var size = l.Settings.TileSize;
                var min = (int2)math.floor((centre - range) / size);
                var max = (int2)math.floor((centre + range) / size);
                for (int z = min.y; z <= max.y; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (!l.Tiles.TryGetValue(new int2(x, z), out var tile))
                        continue;
                    for (int i = 0; i < tile.Length; i++)
                    {
                        var inst = tile[i];
                        if (inst.Model < 0 || inst.Model >= models.Count)
                            continue;
                        var model = models[inst.Model];
                        if (model.TrunkRadius <= 0f || model.Colliders.Count > 0 || !species.Contains(model.Species)
                            || math.distance(inst.Position.xz, centre) > range)
                            continue;
                        float c = math.cos(inst.Yaw), sn = math.sin(inst.Yaw);
                        var lean = new float2(model.TrunkLeanTop.x * c - model.TrunkLeanTop.y * sn,
                                              model.TrunkLeanTop.x * sn + model.TrunkLeanTop.y * c) * inst.Scale;
                        var height = model.Height * inst.Scale;
                        into.Add((inst.Position, inst.Position + new float3(lean.x, height, lean.y), height,
                                  model.Radius * inst.Scale, (int)math.hash(new int3((int2)math.round(inst.Position.xz * 4f), li))));
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
