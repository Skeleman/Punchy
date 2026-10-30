using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds and draws the inside of caves near the camera, and looks after being underground.
    ///
    /// A cave's walls are the boundary of the rock: everywhere below the terrain surface and outside the cave's air.
    /// That field is sampled on a grid and turned into a mesh by surface nets (a vertex in each grid cell the boundary
    /// passes through, joined across every grid edge it crosses), in background Burst jobs. The grid is cut into fixed
    /// cubes (chunks), built only where a cave reaches and streamed by distance, so a big cave network costs no more
    /// to load than the part of it near the camera. Each chunk samples a one-voxel apron round itself and only joins
    /// the edges that start inside it, so neighbouring chunks share their boundary vertices exactly.
    /// Only the boundary close to the cave is kept: the cave walls, which end 10 cm under the terrain surface
    /// (measured across the slope). The terrain cuts its own opening just inside the cave's edge, overhanging the
    /// walls' top edge, so there's no seam. Near the surface the walls take the terrain's colour and blend into cave
    /// rock as they go underground.
    ///
    /// Underground (camera in a cave, well below the surface) fades from 0 to 1: weather stops, and a lantern on the
    /// player lights the way.
    /// </summary>
    public class CaveSystem : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Caves within this distance of the camera are built and drawn.")]
        public float LoadRadius = 400f;
        [Tooltip("Grid spacing for building cave walls, in metres.")]
        [Range(0.25f, 1.5f)] public float Voxel = 0.5f;

        [Header("Lantern")]
        public Color LanternColor = new Color(1f, 0.78f, 0.5f);
        public float LanternIntensity = 6f;
        public float LanternRange = 15f;

        /// <summary>0 above ground, 1 deep in a cave (from the camera). Weather and ambience read it.</summary>
        public static float Underground { get; private set; }

        /// <summary>
        /// How completely the surface world is out of sight, 0..1: Underground, faded out near any cave mouth (0 within
        /// SurfaceFadeNear of a mouth, 1 past SurfaceFadeFar), so a view out of a mouth still shows the outside. Systems
        /// drawing things on the surface (flora, terrain beyond the camera's own neighbourhood, creatures) skip them at
        /// 1. The one rule everyone keys on.
        /// </summary>
        public static float SurfaceHidden { get; private set; }
        const float SurfaceFadeNear = 25f, SurfaceFadeFar = 45f;

        /// <summary>
        /// Whether every cave chunk in range of the camera is built and nothing is queued or building: what tools that
        /// take screenshots wait on, instead of a fixed delay, after moving the camera.
        /// </summary>
        public bool Settled
        {
            get
            {
                if (wanted.Count > 0 || dying.Count > 0)
                    return false;
                foreach (var b in chunks.Values)
                    if (b.Pending)
                        return false;
                return true;
            }
        }

        class Built
        {
            public Mesh Mesh;
            public JobHandle Job;
            public NativeArray<float> Field, CaveField, Surface, SurfaceScale;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float4> Data;
            public NativeList<int> Indices;
            public bool Pending;
            /// <summary>Whether its walls cast sun shadows: only chunks reaching up near the surface can shade anything the sun lights.</summary>
            public bool CastsShadows;
            /// <summary>Wholly under the ground and well away from every mouth: nothing outside a cave can see it.</summary>
            public bool Buried;

            public void Dispose()
            {
                Job.Complete();
                if (Field.IsCreated) Field.Dispose();
                if (CaveField.IsCreated) CaveField.Dispose();
                if (Surface.IsCreated) Surface.Dispose();
                if (SurfaceScale.IsCreated) SurfaceScale.Dispose();
                if (Data.IsCreated) Data.Dispose();
                if (Positions.IsCreated) Positions.Dispose();
                if (Normals.IsCreated) Normals.Dispose();
                if (Colors.IsCreated) Colors.Dispose();
                if (Indices.IsCreated) Indices.Dispose();
                if (Mesh != null) Destroy(Mesh);
            }
        }

        [Tooltip("Cave chunks started per frame at most (nearest first), so a cave coming into range doesn't hitch.")]
        [Min(1)] public int BuildsPerFrame = 2;
        [Tooltip("Most finished chunks turned into meshes (made and uploaded on the main thread) per frame; the rest wait " +
                 "a frame, so a batch finishing together at a mouth or after a teleport doesn't land in one frame.")]
        [Min(1)] public int UploadsPerFrame = 2;
        [Tooltip("Cave chunks building at once at most. Each holds a few MB of fields while it builds, and too many " +
                 "crowd the terrain's own jobs off the workers.")]
        [Min(1)] public int MaxBuilding = 4;
        [Tooltip("Above this camera speed (m/s: gliding, riding) caves build only within Fast Radius, one chunk at a " +
                 "time: cave walls can't be seen from the air, and their jobs would land on the frames the terrain is " +
                 "meshing in.")]
        public float FastSpeed = 10f;
        public float FastRadius = 150f;
        float cameraSpeed;
        float3 lastStreamCamera = new float3(float.MaxValue);

        /// <summary>Cave walls are built in cubes this many metres across (rounded to whole voxels).</summary>
        const float ChunkMetres = 32f;

        readonly Dictionary<int3, Built> chunks = new Dictionary<int3, Built>();
        /// <summary>Every chunk any cave reaches, by column (x, z), with the chunk heights (y) in that column.</summary>
        readonly Dictionary<int2, List<int>> columns = new Dictionary<int2, List<int>>();
        readonly List<int3> wanted = new List<int3>();
        readonly List<int3> scratch = new List<int3>();
        int chunkCells;
        float chunkSize;
        /// <summary>Voxel as it was when the chunks were indexed: editing Voxel in play mode mustn't change the grid under them.</summary>
        float chunkVoxel;
        /// <summary>Chunks unloaded while still building: dropped once their jobs finish, not waited on.</summary>
        readonly List<Built> dying = new List<Built>();
        int3 lastCameraChunk = new int3(int.MinValue);
        World queryWorld;
        EntityQuery terrainQuery, playerQuery;
        TerrainSource lastSource;
        Light lantern;
        float lanternLevel;

        // Other players carry lanterns too (lit from where their bodies are; nothing extra is sent).
        class OtherLantern { public Light Light; public float Level; public bool Seen; }
        readonly Dictionary<Entity, OtherLantern> otherLanterns = new Dictionary<Entity, OtherLantern>();
        readonly List<Entity> goneLanterns = new List<Entity>();
        EntityQuery othersQuery;

        static readonly int UndergroundId = Shader.PropertyToID("_PunchyUnderground");
        static readonly int StylesId = Shader.PropertyToID("_PunchyCaveStyles");
        readonly Vector4[] styleTable = new Vector4[CaveStyleTable.MaxStyles * CaveStyleTable.Vectors];
        EntityQuery paletteQuery;
        BiomePalette stylesFrom;
        List<CaveStyle> styles;

        static readonly int DetailId = Shader.PropertyToID("_PunchyCaveDetail");
        static readonly int DetailReadyId = Shader.PropertyToID("_PunchyCaveDetailReady");
        static readonly int FloraInCaveId = Shader.PropertyToID("_FloraInCave");
        static readonly int UnderRockId = Shader.PropertyToID("_UnderRock");
        static Texture2D detail;

        void OnEnable()
        {
            Punchy.Net.GameBootstrap.WorldsReplacing += ReleaseWorld;
            if (detail == null)
                detail = MakeDetail();
            Shader.SetGlobalTexture(DetailId, detail);
            // Water reads it too, for fine ripples; it keeps them off until this is set.
            Shader.SetGlobalFloat(DetailReadyId, 1f);
            Shader.SetGlobalFloat(WaterDetailId, WaterDetail ? 1f : 0f);
        }

        static readonly int WaterDetailId = Shader.PropertyToID("_PunchyWaterDetail");

        /// <summary>
        /// Whether water draws its fine detail (ripples, caustics, glitter): on unless the command line says
        /// -waterdetail 0, so a profiling build can compare both in one run.
        /// </summary>
        static bool WaterDetail
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-waterdetail")
                        return args[i + 1] != "0";
                return true;
            }
        }

        /// <summary>
        /// The fine surface detail every cave style shares (frost, grain, grit), so there's one texture rather than one
        /// per style: rg a normal from a tiling height, b that height, a white-noise sparkle. The height is periodic
        /// gradient noise over four octaves with a slight warp (value noise on a lattice shows its grid as rows of
        /// bumps). Deterministic, so it's the same on every machine.
        /// </summary>
        static Texture2D MakeDetail()
        {
            const int size = 256;
            var height = new float[size * size];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var uv = new float2(x, y) / size;
                // Periods are whole numbers of repeats across the texture, so it tiles.
                uv += new float2(noise.pnoise(uv * 4f + 3.7f, new float2(4f)), noise.pnoise(uv * 4f - 1.3f, new float2(4f))) * 0.04f;
                var h = 0f;
                var amplitude = 0.5f;
                for (int o = 0, period = 6; o < 4; o++, period *= 2, amplitude *= 0.55f)
                    h += noise.pnoise(uv * period, new float2(period)) * amplitude;
                height[y * size + x] = h;
                lo = math.min(lo, h);
                hi = math.max(hi, h);
            }
            for (int i = 0; i < height.Length; i++)
                height[i] = (height[i] - lo) / math.max(hi - lo, 1e-6f);

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float H(int dx, int dy) => height[((y + dy + size) % size) * size + (x + dx + size) % size];
                var slope = new float2(H(1, 0) - H(-1, 0), H(0, 1) - H(0, -1)) * 10f;
                var normal = math.normalize(new float3(-slope, 1f));
                var sparkle = (math.hash(new int2(x, y + 7919)) & 0xFF) / 255f;
                pixels[y * size + x] = new Color32((byte)((normal.x * 0.5f + 0.5f) * 255f), (byte)((normal.y * 0.5f + 0.5f) * 255f),
                                                   (byte)(H(0, 0) * 255f), (byte)(sparkle * 255f));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Cave detail",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        void OnDisable()
        {
            Punchy.Net.GameBootstrap.WorldsReplacing -= ReleaseWorld;
            Release();
        }

        /// <summary>
        /// Hosting, joining or leaving disposes the game worlds, and with them the TerrainSource blobs that chunk jobs
        /// still in flight are reading. This runs just before, while the old world is intact: every job is finished
        /// and every chunk dropped, and the world is looked up afresh next frame.
        /// </summary>
        void ReleaseWorld()
        {
            Release();
            lastSource = default;
            queryWorld = null;
        }

        void Release()
        {
            foreach (var c in chunks.Values)
                c.Dispose();
            chunks.Clear();
            foreach (var c in dying)
                c.Dispose();
            dying.Clear();
            columns.Clear();
            DisposeIndex();
            indexed = false;
            ReleaseLandmarkPools();
            surfaceGlow.Release();
            ReleaseDrips();
            lastCameraChunk = new int3(int.MinValue);
            Underground = 0f;
            SurfaceHidden = 0f;
            Shader.SetGlobalFloat(UndergroundId, 0f);
            if (lantern != null)
                lantern.enabled = false;
            foreach (var l in otherLanterns.Values)
                if (l.Light != null) Destroy(l.Light.gameObject);
            otherLanterns.Clear();
            Shader.SetGlobalInt(HeatCountId, 0);
            foreach (var m in streamMeshes)
                if (m != null) Destroy(m);
            streamMeshes.Clear();
            ReleasePlants();
            if (floraMaterial != null)
                Destroy(floraMaterial);
            floraMaterial = null;
            if (caveWater != null)
                Destroy(caveWater);
            caveWater = null;
            Shader.SetGlobalVector(LanternId, Vector4.zero);
        }

        // ------------------------------------------------------------------------------------------- pools

        static readonly int HeatId = Shader.PropertyToID("_PunchyCaveHeat");
        static readonly int HeatColorId = Shader.PropertyToID("_PunchyCaveHeatColor");
        static readonly int HeatCountId = Shader.PropertyToID("_PunchyCaveHeatCount");
        /// <summary>Glowing things lighting the walls at once at most (the nearest; must match Cave.shader's arrays).</summary>
        const int MaxHeat = 16;
        readonly Vector4[] heat = new Vector4[MaxHeat];
        readonly Vector4[] heatColor = new Vector4[MaxHeat];
        /// <summary>
        /// This frame's glowing things near the camera: lava pools, glowing plants, lit windows and moving lights. Key:
        /// what tracks a light's fade from frame to frame (its position, for things that don't move).
        /// </summary>
        readonly List<(float distance, Vector4 key, Vector4 position, Vector4 color)> glowing = new List<(float, Vector4, Vector4, Vector4)>();

        /// <summary>
        /// Lights that move (a lantern a walker carries), for the glow list: other systems add theirs each frame, with
        /// an id that stays the same while the light lives (so its fade-in carries on as it moves), its position and
        /// its light (rgb linear times intensity, w its reach in metres, as for glowing plants). CaveSystem takes the
        /// nearest with everything else and clears the list once it has uploaded them. Main thread only.
        /// </summary>
        public static readonly List<(int id, Vector3 position, Vector4 color)> MovingLights = new List<(int, Vector3, Vector4)>();
        static readonly Vector4 LavaLight = new Vector4(1f * 2.2f, 0.42f * 2.2f, 0.1f * 2.2f, 0f);

        /// <summary>How long a light takes to come on as it joins the list (with a small swell) and to go out as it leaves (s).</summary>
        const float HeatFadeIn = 0.8f, HeatFadeOut = 0.8f;
        /// <summary>A light already in the list counts as this much nearer, so lights at the edge of the nearest
        /// MaxHeat don't flicker in and out as the camera moves.</summary>
        const float HeatStickiness = 0.85f;
        /// <summary>Each listed light's time in the list (s), by key.</summary>
        readonly Dictionary<Vector4, float> heatAge = new Dictionary<Vector4, float>();
        /// <summary>Lights that have left the list, still fading out where they were last: colour, position, time left.</summary>
        readonly Dictionary<Vector4, (Vector4 color, Vector4 position, float left)> heatLeaving = new Dictionary<Vector4, (Vector4, Vector4, float)>();
        readonly List<Vector4> heatScratch = new List<Vector4>();
        readonly Dictionary<Vector4, float> heatNextAge = new Dictionary<Vector4, float>();
        /// <summary>Each listed light's colour and position when last uploaded, by key, so one that drops out of the
        /// candidates altogether (a surface rescan that no longer reaches it) still fades out rather than popping off.</summary>
        readonly Dictionary<Vector4, (Vector4 color, Vector4 position)> heatLast = new Dictionary<Vector4, (Vector4, Vector4)>();

        /// <summary>
        /// Uploads the nearest glowing things to the cave shader, which lights the walls round them. A light joining the
        /// list fades in over HeatFadeIn with a small swell past full, and one leaving fades out (in a spare slot), so
        /// lights don't pop on and off at the edge of the list.
        /// </summary>
        void UploadGlowing(float3 camPos)
        {
            // Moving lights, keyed by their id (a y no position has keeps them apart from the still ones).
            foreach (var (id, position, color) in MovingLights)
                glowing.Add((math.distance((float3)position, camPos), new Vector4(id, float.MaxValue, 0f, 0f),
                             new Vector4(position.x, position.y, position.z, 0f), color));
            MovingLights.Clear();

            var dt = math.min(Time.unscaledDeltaTime, 0.1f);
            for (int i = 0; i < glowing.Count; i++)
                if (heatAge.ContainsKey(glowing[i].key))
                    glowing[i] = (glowing[i].distance * HeatStickiness, glowing[i].key, glowing[i].position, glowing[i].color);
            glowing.Sort(GlowingByDistance);
            var count = math.min(glowing.Count, MaxHeat);

            heatNextAge.Clear();
            for (int i = 0; i < count; i++)
            {
                var (_, key, position, color) = glowing[i];
                var age = (heatAge.TryGetValue(key, out var a) ? a : 0f) + dt;
                heatNextAge[key] = age;
                heatLeaving.Remove(key);
                heatLast[key] = (color, position);
                // Up to full over the first 60%, a swell of about 12% over full, settling back by the end.
                var t = age / HeatFadeIn;
                var envelope = math.smoothstep(0f, 0.6f, t) + 0.12f * math.smoothstep(0.3f, 0.6f, t) * (1f - math.smoothstep(0.6f, 1.2f, t));
                heat[i] = position;
                heatColor[i] = new Vector4(color.x * envelope, color.y * envelope, color.z * envelope, color.w);
            }
            // Those that just left start fading out.
            foreach (var kv in heatAge)
                if (!heatNextAge.ContainsKey(kv.Key) && !heatLeaving.ContainsKey(kv.Key))
                {
                    if (!heatLast.TryGetValue(kv.Key, out var last))
                        continue;
                    heatLeaving[kv.Key] = (last.color, last.position, HeatFadeOut * math.saturate(kv.Value / HeatFadeIn));
                }
            foreach (var kv in heatAge)
                if (!heatNextAge.ContainsKey(kv.Key))
                    heatLast.Remove(kv.Key);
            heatAge.Clear();
            foreach (var kv in heatNextAge)
                heatAge[kv.Key] = kv.Value;

            // Fading ones fill the spare slots (and are dropped, already dim, when there are none).
            heatScratch.Clear();
            foreach (var key in heatLeaving.Keys)
                heatScratch.Add(key);
            foreach (var key in heatScratch)
            {
                var (color, position, left) = heatLeaving[key];
                left -= dt;
                if (left <= 0f || count >= MaxHeat)
                {
                    heatLeaving.Remove(key);
                    continue;
                }
                heatLeaving[key] = (color, position, left);
                var fade = math.smoothstep(0f, 1f, left / HeatFadeOut);
                heat[count] = position;
                heatColor[count] = new Vector4(color.x * fade, color.y * fade, color.z * fade, color.w);
                count++;
            }

            Shader.SetGlobalVectorArray(HeatId, heat);
            Shader.SetGlobalVectorArray(HeatColorId, heatColor);
            Shader.SetGlobalInt(HeatCountId, count);
            glowing.Clear();
        }
        readonly List<(float distance, int pool)> nearPools = new List<(float, int)>();
        // Comparers made once: List.Sort(Comparison) wraps the delegate in a new comparer on every call, every frame.
        static readonly Comparer<(float distance, Vector4 key, Vector4 position, Vector4 color)> GlowingByDistance =
            Comparer<(float distance, Vector4 key, Vector4 position, Vector4 color)>.Create((a, b) => a.distance.CompareTo(b.distance));
        static readonly Comparer<(float distance, int pool)> PoolsByDistance =
            Comparer<(float distance, int pool)>.Create((a, b) => a.distance.CompareTo(b.distance));
        static Mesh lavaPoolMesh, waterPoolMesh;
        /// <summary>Cave water's look in the water vertex layout: dark, clear and cold (rgb, murk).</summary>
        static readonly float4 CaveWater = new float4(0.1f, 0.28f, 0.3f, 0.1f);
        // Streams run shallow over dark rock: darker and greener than the pools, a little murkier.
        static readonly float4 CaveStreamWater = new float4(0.04f, 0.13f, 0.12f, 0.15f);
        /// <summary>One mesh per stream, built when the world's caves are first drawn (streams are few and fixed).</summary>
        readonly List<Mesh> streamMeshes = new List<Mesh>();
        EntityQuery configQuery;

        /// <summary>
        /// Draws every pool and stream within LoadRadius with the terrain's own water material (lava is its lava path):
        /// pools with one shared disc mesh each for water and lava, streams with a ribbon mesh each. Uploads the nearest
        /// lava pools so the cave shader can light the walls round them warmly without real lights.
        /// </summary>
        void DrawPools(in TerrainSource source, float3 camPos)
        {
            ref var set = ref source.Caves.Value;
            nearPools.Clear();
            for (int i = 0; i < set.Pools.Length; i++)
            {
                var d = math.distance(set.Pools[i].Centre, camPos);
                if (d < LoadRadius)
                    nearPools.Add((d, i));
            }
            var water = UnderRockWater(configQuery.IsEmpty ? null : configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value);
            if (nearPools.Count > 0)
            {
                if (lavaPoolMesh == null)
                {
                    lavaPoolMesh = MakePoolMesh(new float4(1f, 0.33f, 0.05f, 0.2f), 1f);
                    waterPoolMesh = MakePoolMesh(CaveWater, 0f);
                }
                nearPools.Sort(PoolsByDistance);
                foreach (var (_, i) in nearPools)
                {
                    ref var pool = ref set.Pools[i];
                    if (water != null)
                    {
                        var rp = new RenderParams(water)
                        {
                            worldBounds = new Bounds(pool.Centre, new Vector3(pool.Radius * 2f, 1f, pool.Radius * 2f)),
                            shadowCastingMode = ShadowCastingMode.Off,
                            receiveShadows = false
                        };
                        var mesh = pool.Lava > 0.5f ? lavaPoolMesh : waterPoolMesh;
                        Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.TRS(pool.Centre, Quaternion.identity, new Vector3(pool.Radius, 1f, pool.Radius)));
                    }
                    if (pool.Lava > 0.5f)
                    {
                        var reach = LavaLight;
                        reach.w = pool.Radius * 5f + 4f;
                        // Only pools whose light could reach something near the camera take one of the 16 slots.
                        if (math.distance(pool.Centre, camPos) < reach.w + PlantRadius)
                            {
                            var at = new Vector4(pool.Centre.x, pool.Centre.y + 0.3f, pool.Centre.z, 0f);
                            glowing.Add((math.distance(pool.Centre, camPos), at, at, reach));
                        }
                    }
                }
            }

            if (set.Streams.Length == 0 || water == null)
                return;
            if (streamMeshes.Count == 0)
                for (int s = 0; s < set.Streams.Length; s++)
                    streamMeshes.Add(MakeStreamMesh(ref set, s));
            for (int s = 0; s < streamMeshes.Count; s++)
            {
                var bounds = streamMeshes[s].bounds;
                if (math.distance((float3)bounds.ClosestPoint(camPos), camPos) > LoadRadius)
                    continue;
                var rp = new RenderParams(water) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMesh(rp, streamMeshes[s], 0, Matrix4x4.identity);
            }
        }

        // ------------------------------------------------------------------------------------------- plants

        [Tooltip("Cave plants within this distance of the camera are drawn.")]
        public float PlantRadius = 60f;
        float nextFloraLookup;
        /// <summary>Variants grown per species (as the flora system does).</summary>
        const int PlantVariants = 3;
        /// <summary>Cave plants nearer than this use the finest mesh, the rest the middle one.</summary>
        const float PlantLod0 = 20f;

        readonly List<Punchy.Flora.FloraModel> plantModels = new List<Punchy.Flora.FloraModel>();
        BiomePalette plantsFrom;
        Material floraMaterial;
        GraphicsBuffer plantInstances, plantVisible;

        Material caveWater;

        /// <summary>
        /// A copy of the terrain's water material for cave pools and streams, marked as under rock, so their
        /// reflections never fall back to the sky (near a mouth the camera isn't yet counted as underground). Kept in
        /// step with the original's settings.
        /// </summary>
        Material UnderRockWater(Material water)
        {
            if (water == null)
                return null;
            if (caveWater == null || caveWater.shader != water.shader)
            {
                if (caveWater != null)
                    Destroy(caveWater);
                caveWater = new Material(water) { name = "Cave water" };
            }
            else
                caveWater.CopyPropertiesFromMaterial(water);
            caveWater.SetFloat(UnderRockId, 1f);
            return caveWater;
        }
        /// <summary>One draw per run of the same model and LOD: its mesh, and its index in plantArgs.</summary>
        readonly List<Mesh> plantDraws = new List<Mesh>();
        /// <summary>Indirect arguments per draw (instance count, and the run's start in plantVisible as startInstance,
        /// which the flora shader adds), so the draws share floraMaterial with no per-draw property block.</summary>
        GraphicsBuffer plantArgs;
        readonly List<(int key, int plant)> plantKeys = new List<(int, int)>();
        /// <summary>Per species, the light its glowing parts give the walls round it (rgb strength, w reach); zero if none.</summary>
        readonly List<Vector4> speciesGlow = new List<Vector4>();
        readonly List<(float3 position, Vector4 light)> nearGlowPlants = new List<(float3, Vector4)>();
        /// <summary>Per species: the height of its torch's head (1 at scale 1), or 0 if it isn't a torch.</summary>
        readonly List<float> speciesTorch = new List<float>();
        /// <summary>Torches near the camera: where the flame sits and the plant's index (its seed).</summary>
        readonly List<(float3 flame, int plant)> nearTorches = new List<(float3, int)>();
        /// <summary>A wall torch's light on the rock round it (warm, about 9 m).</summary>
        static readonly Vector4 TorchLight = new Vector4(1.7f, 0.95f, 0.4f, 9f);
        /// <summary>How far a glowing plant lights the floor and walls round it (m).</summary>
        const float PlantGlowReach = 5f;
        float3 plantsAround = new float3(float.MaxValue);
        static readonly int FloraInstancesId = Shader.PropertyToID("_FloraInstances");
        static readonly int FloraVisibleId = Shader.PropertyToID("_FloraVisible");

        /// <summary>
        /// Draws the cave plants near the camera with the flora system's own material, shader and procedural meshes
        /// (grown once per species and variant), so they match the plants above ground and add no materials. Each
        /// species variant and LOD is one instanced draw over this system's own small instance list, rebuilt when the
        /// camera has moved a few metres; the flora shader reads it exactly as it reads the flora system's.
        /// </summary>
        void DrawPlants(in TerrainSource source, float3 camPos)
        {
            ref var set = ref source.Caves.Value;
            if (set.Plants.Length == 0 || paletteQuery.IsEmpty)
                return;
            if (floraMaterial == null)
            {
                // Looking it up is slow; without one, try again only now and then.
                if (Time.unscaledTime < nextFloraLookup)
                    return;
                nextFloraLookup = Time.unscaledTime + 1f;
                var flora = FindFirstObjectByType<Punchy.Flora.FloraSystem>();
                // A copy, since this sets its own plant buffers on it.
                floraMaterial = flora != null && flora.Material != null ? new Material(flora.Material) { name = "Cave flora" } : null;
                if (floraMaterial == null)
                    return;
                // Lit as underground wherever the camera is (no sun on roots in the cave's ceiling).
                floraMaterial.SetFloat(FloraInCaveId, 1f);
            }
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            if (palette != plantsFrom)
            {
                ReleasePlants();
                foreach (var species in CaveFloraTable.Collect(palette.Landmarks))
                {
                    for (int v = 0; v < PlantVariants; v++)
                        plantModels.Add(Punchy.Flora.FloraGenerator.Grow(species, v));
                    // The same light the plant casts on the surface (colour and reach by how much of it glows).
                    speciesGlow.Add(Punchy.Flora.FloraPlacement.GlowLight(species));
                    speciesTorch.Add(species.Form == Punchy.Flora.FloraForm.Torch ? species.Height.y : 0f);
                }
                plantsFrom = palette;
            }

            if (math.distancesq(camPos, plantsAround) > 16f)
            {
                plantsAround = camPos;
                plantKeys.Clear();
                nearGlowPlants.Clear();
                nearTorches.Clear();
                for (int i = 0; i < set.Plants.Length; i++)
                {
                    ref var p = ref set.Plants[i];
                    var d = math.distance(p.Position, camPos);
                    if (d > PlantRadius)
                        continue;
                    var model = p.Species * PlantVariants + p.Variant;
                    if (model < 0 || model >= plantModels.Count)
                        continue;
                    plantKeys.Add((model * 2 + (d < PlantLod0 ? 0 : 1), i));
                    if (speciesGlow[p.Species].w > 0f)
                        nearGlowPlants.Add((p.Position + new float3(0f, 0.3f * p.Scale, 0f), speciesGlow[p.Species]));
                    if (speciesTorch[p.Species] > 0f)
                    {
                        // The head: up the stick and out from the wall (the model leans out along its +Z by ~20°).
                        var head = speciesTorch[p.Species] * p.Scale;
                        var outward = new float3(-math.sin(p.Yaw), 0f, math.cos(p.Yaw));
                        nearTorches.Add((p.Position + new float3(0f, head * 0.94f, 0f) + outward * head * 0.34f, i));
                    }
                }
                plantKeys.Sort((a, b) => a.key.CompareTo(b.key));
                var count = plantKeys.Count;
                if (plantInstances == null || plantInstances.count < math.max(count, 1))
                {
                    plantInstances?.Release();
                    plantVisible?.Release();
                    var capacity = math.max(64, math.ceilpow2(math.max(count, 1)));
                    plantInstances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, UnsafeUtility.SizeOf<Punchy.Flora.FloraSystem.FloraInstance>());
                    plantVisible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(uint) * 2);
                }
                var instances = new NativeArray<Punchy.Flora.FloraSystem.FloraInstance>(count, Allocator.Temp);
                var visible = new NativeArray<uint2>(count, Allocator.Temp);
                var fullyVisible = math.asuint(1f);
                for (int k = 0; k < count; k++)
                {
                    ref var p = ref set.Plants[plantKeys[k].plant];
                    instances[k] = new Punchy.Flora.FloraSystem.FloraInstance
                    {
                        Position = p.Position, Yaw = p.Yaw, Scale = p.Scale,
                        Model = plantKeys[k].key / 2, Random = p.Random, Snow = 0f
                    };
                    visible[k] = new uint2((uint)k, fullyVisible);
                }
                plantInstances.SetData(instances);
                plantVisible.SetData(visible);
                instances.Dispose();
                visible.Dispose();

                // One draw per run of the same model and LOD.
                plantDraws.Clear();
                var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
                for (int k = 0; k < count;)
                {
                    var key = plantKeys[k].key;
                    var start = k;
                    while (k < count && plantKeys[k].key == key)
                        k++;
                    var mesh = plantModels[key / 2].Lods[key % 2];
                    plantDraws.Add(mesh);
                    args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = mesh.GetIndexCount(0),
                        startIndex = mesh.GetIndexStart(0),
                        baseVertexIndex = mesh.GetBaseVertex(0),
                        instanceCount = (uint)(k - start),
                        startInstance = (uint)start
                    });
                }
                if (args.Count > 0 && (plantArgs == null || plantArgs.count < args.Count))
                {
                    plantArgs?.Release();
                    plantArgs = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, math.ceilpow2(args.Count), GraphicsBuffer.IndirectDrawIndexedArgs.size);
                }
                if (args.Count > 0)
                    plantArgs.SetData(args);
                floraMaterial.SetBuffer(FloraInstancesId, plantInstances);
                floraMaterial.SetBuffer(FloraVisibleId, plantVisible);
            }

            foreach (var (position, light) in nearGlowPlants)
            {
                var at = new Vector4(position.x, position.y, position.z, 0f);
                glowing.Add((math.distance(position, camPos), at, at, light));
            }
            // Wall torches: a flame on each (every frame: FireSystem's list is per frame) and its warm light on the rock.
            foreach (var (flame, plant) in nearTorches)
            {
                Punchy.Ambience.FireSystem.AddFlame(flame, 0.3f, plant, Punchy.Ambience.FireSystem.Wood);
                var at = new Vector4(flame.x, flame.y + 0.15f, flame.z, 0f);
                glowing.Add((math.distance(flame, camPos), at, at, TorchLight));
            }

            var bounds = new Bounds(camPos, new Vector3(PlantRadius * 2f, PlantRadius * 2f, PlantRadius * 2f));
            for (int d = 0; d < plantDraws.Count; d++)
            {
                var rp = new RenderParams(floraMaterial)
                {
                    worldBounds = bounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true
                };
                Graphics.RenderMeshIndirect(rp, plantDraws[d], plantArgs, 1, d);
            }
        }

        void ReleasePlants()
        {
            foreach (var m in plantModels)
                m.Destroy();
            plantModels.Clear();
            speciesGlow.Clear();
            speciesTorch.Clear();
            nearGlowPlants.Clear();
            nearTorches.Clear();
            plantDraws.Clear();
            plantInstances?.Release();
            plantVisible?.Release();
            plantArgs?.Release();
            plantInstances = plantVisible = plantArgs = null;
            plantsFrom = null;
            plantsAround = new float3(float.MaxValue);
        }

        /// <summary>
        /// A stream's water surface: a ribbon along its points, in world space, flowing from first to last. Shallow at
        /// the banks; rapids where the surface drops quickly, so the water shader's river look carries over.
        /// </summary>
        static Mesh MakeStreamMesh(ref CaveSetBlob set, int index)
        {
            ref var stream = ref set.Streams[index];
            var vertices = new List<WaterVertex>();
            var indices = new List<int>();
            var count = stream.PointCount;
            for (int k = 0; k < count; k++)
            {
                ref var p = ref set.StreamPoints[stream.FirstPoint + k];
                var prev = set.StreamPoints[stream.FirstPoint + math.max(k - 1, 0)].Position;
                var next = set.StreamPoints[stream.FirstPoint + math.min(k + 1, count - 1)].Position;
                var along = math.normalizesafe((next - prev).xz, new float2(1f, 0f));
                // Narrowing to a trickle at both ends, rather than stopping square.
                var taper = math.lerp(0.15f, 1f, math.saturate(math.min(k, count - 1 - k) / 2f));
                // A little past where the banks rise out of the water (the edges tuck under them), so it fills the channel.
                var side = new float2(-along.y, along.x) * p.Width * 0.58f * taper;
                var drop = (prev.y - next.y) / math.max(math.distance(prev.xz, next.xz), 0.1f);
                // Cave streams run steep all the way, so only the steepest steps churn white; but a brook is never still.
                var rapids = math.max(math.saturate(drop * 1.5f - 0.15f), 0.2f);
                foreach (var edge in new[] { -1f, 0f, 1f })
                    vertices.Add(new WaterVertex
                    {
                        Position = p.Position + new float3(side.x, 0f, side.y) * edge,
                        // White water breaks against the banks more than mid-channel (G-28).
                        Data = new float4(along, edge == 0f ? p.Depth : 0.05f, edge == 0f ? rapids * 0.6f : math.min(rapids + 0.15f, 1f)),
                        Look = CaveStreamWater,
                        Glow = float2.zero
                    });
                if (k == 0)
                    continue;
                var a = (k - 1) * 3;
                var b = k * 3;
                for (int e = 0; e < 2; e++)
                {
                    indices.Add(a + e); indices.Add(b + e); indices.Add(a + e + 1);
                    indices.Add(a + e + 1); indices.Add(b + e); indices.Add(b + e + 1);
                }
            }
            var mesh = new Mesh { name = "Cave stream" };
            mesh.SetVertexBufferParams(vertices.Count, WaterVertex.Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A unit disc in the terrain's water vertex layout: no flow, deepest at the middle (lava cools and crusts
        /// towards the rim), in the given look and lava amount.
        /// </summary>
        static Mesh MakePoolMesh(float4 look, float lava) => MakePoolMesh(look, new float2(0f, lava));

        /// <summary>The same disc with a self-glow too (toxic pools): glow x, lava y.</summary>
        static Mesh MakePoolMesh(float4 look, float2 glow)
        {
            const int rings = 6, spokes = 32;
            var vertices = new List<WaterVertex> { Vertex(float2.zero) };
            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < spokes; s++)
                {
                    var a = s / (float)spokes * math.PI * 2f;
                    vertices.Add(Vertex(new float2(math.cos(a), math.sin(a)) * r / rings));
                }
            var indices = new List<int>();
            for (int s = 0; s < spokes; s++)
            {
                indices.Add(0); indices.Add(1 + (s + 1) % spokes); indices.Add(1 + s);
            }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < spokes; s++)
                {
                    int a = 1 + (r - 1) * spokes + s, b = 1 + (r - 1) * spokes + (s + 1) % spokes;
                    int c = a + spokes, d = b + spokes;
                    indices.Add(a); indices.Add(b); indices.Add(c);
                    indices.Add(b); indices.Add(d); indices.Add(c);
                }
            var mesh = new Mesh { name = "Cave pool" };
            mesh.SetVertexBufferParams(vertices.Count, WaterVertex.Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 1f, 2f));
            return mesh;

            WaterVertex Vertex(float2 xz) => new WaterVertex
            {
                Position = new float3(xz.x, 0f, xz.y),
                Data = new float4(0f, 0f, 1f - math.lengthsq(xz), 0f),
                Look = look,
                Glow = glow
            };
        }

        // ------------------------------------------------------------------------------------------- landmark pools

        /// <summary>A landmark's pool, drawn as a smooth disc (the terrain's water grid leaves it out).</summary>
        struct LandmarkPool
        {
            public float3 Centre;
            public float Radius;
            public Mesh Mesh;
        }

        readonly List<LandmarkPool> landmarkPools = new List<LandmarkPool>();
        BlobAssetReference<LandmarkSetBlob> landmarkPoolsFrom;

        /// <summary>
        /// Draws every landmark pool (oases, hot springs, toxic pools, crater lava) within LoadRadius with the terrain's
        /// water material: a disc a tenth wider than the pool, so its rim tucks under the gentle bank, in the water look
        /// the terrain would give it there. A small pool spanned only a few quads of the terrain's water grid, which
        /// left a ragged edge. One mesh per pool, built when the landmarks change.
        /// </summary>
        void DrawLandmarkPools(in TerrainSource source, float3 camPos)
        {
            if (!source.Landmarks.IsCreated || configQuery.IsEmpty)
                return;
            var water = configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value;
            if (water == null)
                return;
            if (landmarkPoolsFrom != source.Landmarks)
            {
                ReleaseLandmarkPools();
                landmarkPoolsFrom = source.Landmarks;
                ref var set = ref source.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var type = ref set.Types[set.Instances[i].Type];
                    for (int p = 0; p < type.PoolCount; p++)
                    {
                        LandmarkSampling.Pool(ref set, i, p, out var centre, out var radius);
                        var look = source.WaterLook(centre.xz, source.SampleGround(centre.xz).Biomes, out var glow);
                        var mesh = MakePoolMesh(look, glow);
                        mesh.name = "Landmark pool";
                        landmarkPools.Add(new LandmarkPool { Centre = centre, Radius = radius * 1.1f, Mesh = mesh });
                    }
                }
            }
            foreach (var pool in landmarkPools)
            {
                if (math.distance(pool.Centre, camPos) > LoadRadius + pool.Radius)
                    continue;
                var rp = new RenderParams(water)
                {
                    worldBounds = new Bounds(pool.Centre, new Vector3(pool.Radius * 2f, 1f, pool.Radius * 2f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, pool.Mesh, 0, Matrix4x4.TRS(pool.Centre, Quaternion.identity, new Vector3(pool.Radius, 1f, pool.Radius)));
            }
        }

        // ------------------------------------------------------------------------------------------- drips

        /// <summary>One drip source, as the drip shader reads it.</summary>
        struct Drip
        {
            public float4 Tip;    // xyz the tip, w the period (s)
            public float4 Land;   // x landing height, y phase, z ring size, w 1 on water (ring) or 0 on rock (splash)
        }

        const int MaxDrips = 48;
        const float DripRadius = 25f;
        readonly List<Drip> allDrips = new List<Drip>();
        readonly List<(float distance, Drip drip)> nearDrips = new List<(float, Drip)>();
        static readonly Comparer<(float distance, Drip drip)> DripsByDistance =
            Comparer<(float distance, Drip drip)>.Create((a, b) => a.distance.CompareTo(b.distance));
        readonly Drip[] dripUpload = new Drip[MaxDrips];
        BlobAssetReference<CaveSetBlob> dripsFrom;
        float3 dripsAround = new float3(float.MaxValue);
        GraphicsBuffer dripBuffer;
        int dripCount;
        Material dripMaterial;
        MaterialPropertyBlock dripProps;
        static readonly int DripsId = Shader.PropertyToID("_Drips");

        /// <summary>
        /// Water drips from some stalactite tips (the style's Drips share; none in frozen or molten caves): a drop
        /// gathers, falls and rings on the floor or a pool, each source on its own period. The sources are worked out once
        /// per world (seeded), the nearest 48 within 25 m uploaded as the camera moves; the shader animates them on the
        /// shared clock. The material comes from Resources (CaveDrips), so it needs no scene reference.
        /// </summary>
        void DrawDrips(in TerrainSource source, float3 camPos)
        {
            if (styles == null)
                return;
            if (dripMaterial == null)
            {
                dripMaterial = Resources.Load<Material>("CaveDrips");
                if (dripMaterial == null)
                    return;
            }
            ref var set = ref source.Caves.Value;
            if (dripsFrom != source.Caves)
            {
                dripsFrom = source.Caves;
                allDrips.Clear();
                dripsAround = new float3(float.MaxValue);
                // Stalactite tips first, then bare ceiling in each chamber (most caves of a dripping style have few
                // stalactites): a few spots per chamber, by the style's Drips share.
                for (int i = 0; i < set.Speleothems.Length; i++)
                {
                    ref var s = ref set.Speleothems[i];
                    if (s.Tip.y < s.Root.y)   // hanging (not standing, not a column)
                        AddDrip(source, s.Tip, (uint)i);
                }
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    ref var cave = ref set.Caves[c];
                    var style = cave.Style > 0 && cave.Style <= styles.Count ? styles[cave.Style - 1] : null;
                    if (style == null || style.Drips <= 0f)
                        continue;
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                    {
                        if (set.Kinds[n] != CaveNodeKind.Chamber)
                            continue;
                        var rng = new Unity.Mathematics.Random(math.hash(new int2(n, 0xC3E1)) | 1u);
                        var node = set.Nodes[n];
                        for (int k = 0; k < 6; k++)
                        {
                            var q = node.Position.xz + rng.NextFloat2Direction() * node.Radius * cave.Profile.x * 0.7f * math.sqrt(rng.NextFloat());
                            var floor = source.FloorUnder(new float3(q.x, node.Position.y, q.y), out var inCave);
                            if (!inCave)
                                continue;
                            var ceiling = source.CeilingOver(new float3(q.x, floor + 0.5f, q.y));
                            AddDrip(source, new float3(q.x, ceiling - 0.02f, q.y), 100000u + (uint)(n * 8 + k));
                        }
                    }
                }
            }
            if (allDrips.Count == 0)
                return;
            if (math.distancesq(camPos, dripsAround) > 4f)
            {
                dripsAround = camPos;
                nearDrips.Clear();
                foreach (var d in allDrips)
                {
                    var dist = math.distance(d.Tip.xyz, camPos);
                    if (dist < DripRadius)
                        nearDrips.Add((dist, d));
                }
                nearDrips.Sort(DripsByDistance);
                dripCount = math.min(nearDrips.Count, MaxDrips);
                for (int i = 0; i < dripCount; i++)
                    dripUpload[i] = nearDrips[i].drip;
                if (dripBuffer == null)
                    dripBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxDrips, System.Runtime.InteropServices.Marshal.SizeOf<Drip>());
                dripBuffer.SetData(dripUpload, 0, 0, MaxDrips);
            }
            if (dripCount == 0)
                return;
            dripProps ??= new MaterialPropertyBlock();
            dripProps.SetBuffer(DripsId, dripBuffer);
            var rp = new RenderParams(dripMaterial)
            {
                worldBounds = new Bounds(camPos, new Vector3(DripRadius * 2f, DripRadius * 2f, DripRadius * 2f)),
                matProps = dripProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 36, dripCount);
        }

        /// <summary>
        /// Adds a drip source at a point on a ceiling or a stalactite's tip if its cave's style drips (by its share,
        /// seeded): falling straight down through the air to the first rock (or a water pool's surface) below.
        /// </summary>
        void AddDrip(in TerrainSource source, float3 tip, uint seed)
        {
            ref var set = ref source.Caves.Value;
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xD819u)) | 1u);
            var share = rng.NextFloat();
            // The tip's rounded end is part of the rock field, so scan down to air, then on to rock; the cave (and so
            // its style) is the one that air belongs to.
            var land = float.NaN;
            var cave = -1;
            for (float y = 0.05f; y < 25f; y += 0.1f)
            {
                var p = tip - new float3(0f, y, 0f);
                if (CaveSampling.Distance(ref set, p, out var c) < 0f)
                    cave = c;
                else if (cave >= 0)
                {
                    land = p.y + 0.05f;
                    break;
                }
            }
            if (float.IsNaN(land))
                return;
            var style = set.Caves[cave].Style > 0 && set.Caves[cave].Style <= styles.Count ? styles[set.Caves[cave].Style - 1] : null;
            if (style == null || share >= style.Drips)
                return;
            var ring = 0.25f;
            var onWater = 0f;
            for (int p = 0; p < set.Pools.Length; p++)
            {
                ref var pool = ref set.Pools[p];
                if (pool.Lava < 0.5f && math.distance(pool.Centre.xz, tip.xz) < pool.Radius && pool.Centre.y > land)
                {
                    land = pool.Centre.y;
                    ring = 0.4f;
                    onWater = 1f;
                }
            }
            // Or a stream's surface (within half its width of its line, and above where it would land).
            for (int s = 0; s < set.Streams.Length && onWater == 0f; s++)
            {
                ref var stream = ref set.Streams[s];
                if (math.any(tip.xz < stream.Min) || math.any(tip.xz > stream.Max))
                    continue;
                for (int k = stream.FirstPoint; k < stream.FirstPoint + stream.PointCount - 1; k++)
                {
                    var a = set.StreamPoints[k];
                    var b = set.StreamPoints[k + 1];
                    var ab = b.Position.xz - a.Position.xz;
                    var t = math.saturate(math.dot(tip.xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                    var on = math.lerp(a.Position, b.Position, t);
                    if (math.distance(tip.xz, on.xz) < math.lerp(a.Width, b.Width, t) * 0.5f && on.y > land - 0.3f)
                    {
                        land = on.y;
                        ring = 0.3f;
                        onWater = 1f;
                        break;
                    }
                }
            }
            if (tip.y - land < 0.3f)
                return;
            allDrips.Add(new Drip
            {
                Tip = new float4(tip, rng.NextFloat(2.5f, 7f)),
                Land = new float4(land, rng.NextFloat(), ring, onWater)
            });
        }

        void ReleaseDrips()
        {
            dripBuffer?.Release();
            dripBuffer = null;
            dripCount = 0;
            allDrips.Clear();
            dripsFrom = default;
        }

        // ------------------------------------------------------------------------------------------- surface glow

        readonly SurfaceGlow surfaceGlow = new SurfaceGlow();

        /// <summary>
        /// Glowing plants on the surface near the camera join the glow list (the nearest 16 lights of any kind win), so
        /// their light falls on the ground, plants and creatures round them as cave plants' does in caves.
        /// </summary>
        void GatherSurfaceGlow(in TerrainSource source, float3 camPos)
        {
            if (paletteQuery.IsEmpty || Underground > 0.9f)
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            if (flora == null && Time.unscaledTime >= nextFloraLookup)
            {
                nextFloraLookup = Time.unscaledTime + 1f;
                flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
            }
            surfaceGlow.Update(source, palette, flora, camPos);
            var night = Shader.GetGlobalFloat(NightId);
            foreach (var (position, light) in surfaceGlow.Found)
            {
                var l = light;
                if (l.w < 0f)
                {
                    // Lit windows: only after dark.
                    if (night < 0.25f)
                        continue;
                    l = new Vector4(l.x, l.y, l.z, 0f) * math.smoothstep(0.25f, 0.55f, night);
                    l.w = -light.w;
                }
                var at = new Vector4(position.x, position.y, position.z, 0f);
                glowing.Add((math.distance(position, camPos), at, at, l));
            }
        }

        static readonly int NightId = Shader.PropertyToID("_PunchyNight");

        Punchy.Flora.FloraSystem flora;

        void ReleaseLandmarkPools()
        {
            foreach (var pool in landmarkPools)
                Destroy(pool.Mesh);
            landmarkPools.Clear();
            landmarkPoolsFrom = default;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || Material == null || !TryGetWorld(out var source))
            {
                MovingLights.Clear();
                return;
            }
            if (!source.Equals(lastSource))
            {
                Release();
                lastSource = source;
            }
            var camPos = (float3)cam.transform.position;
            DrawLandmarkPools(source, camPos);
            GatherSurfaceGlow(source, camPos);
            if (!source.Caves.IsCreated)
            {
                UploadGlowing(camPos);
                return;
            }

            UploadStyles();
            Stream(source, camPos);
            // Outside every cave's air, buried chunks can't be seen.
            Draw(source.CaveDistance(camPos) >= 0f);
            DrawPools(source, camPos);
            DrawPlants(source, camPos);
            DrawDrips(source, camPos);
            UploadGlowing(camPos);

            // Underground: the camera in a cave's air, a couple of metres below the surface.
            var deep = source.CaveDistance(camPos) < 0f ? math.saturate((source.SampleHeight(camPos.xz) - camPos.y - 1f) / 3f) : 0f;
            Underground = Mathf.MoveTowards(Underground, deep, Time.deltaTime * 1.5f);
            Shader.SetGlobalFloat(UndergroundId, Underground);
            // The surface is out of sight deep in a cave, away from every mouth.
            var nearestMouth = float.MaxValue;
            ref var caveSet = ref source.Caves.Value;
            for (int c = 0; c < caveSet.Caves.Length; c++)
                nearestMouth = math.min(nearestMouth, math.distance(camPos, caveSet.Caves[c].Mouth));
            SurfaceHidden = Underground * math.smoothstep(SurfaceFadeNear, SurfaceFadeFar, nearestMouth);
            UpdateLantern(source);
            UpdateOtherLanterns(source, cam.transform.position);
        }

        bool TryGetWorld(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerTag>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                othersQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Net.NetPlayer>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                configQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
                queryWorld = world;
            }
            return terrainQuery.TryGetSingleton(out source);
        }

        /// <summary>
        /// Uploads the cave styles' looks in the order baking numbered them (slot 0 stays empty: the material's own
        /// colours). In the editor every frame, so editing a style's colours or finish in play mode shows at once (its
        /// shape is baked and needs a re-bake); in a build only when the palette changes.
        /// </summary>
        void UploadStyles()
        {
            if (paletteQuery.IsEmpty)
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var changed = palette != stylesFrom;
            if (changed)
            {
                styles = CaveStyleTable.Collect(palette.Landmarks);
                stylesFrom = palette;
            }
#if !UNITY_EDITOR
            if (!changed)
                return;
#endif
            for (int i = 0; i < styles.Count; i++)
                styles[i].Pack(styleTable, i + 1);
            Shader.SetGlobalVectorArray(StylesId, styleTable);
        }

        // ------------------------------------------------------------------------------------------- building

        /// <summary>
        /// Walls are kept only where the cave's distance is under this at a cell corner (MeshJob's nearCave). Wide enough
        /// that round a mouth the walls' skin of the ground surface runs on under the terrain past wherever the terrain's
        /// per-vertex cut (TerrainSource.MouthCut) can end, so there's never a gap between them.
        /// </summary>
        const float WallReach = 2.5f;

        /// <summary>
        /// Lists every chunk a cave's walls can reach, by column. Done once per baked world, in a Burst job picked up a
        /// frame or more later (it grows with the caves' total length, so on the main thread it was a load hitch);
        /// false until the list is ready. Chunks follow the segments, so the list is as long as the caves are, not as big
        /// as the world.
        /// </summary>
        bool IndexChunks(in TerrainSource source)
        {
            if (indexed)
                return true;
            if (!indexing)
            {
                chunkVoxel = Voxel;
                chunkCells = math.max(4, (int)math.round(ChunkMetres / chunkVoxel));
                chunkSize = chunkCells * chunkVoxel;
                indexSeen = new NativeHashSet<int3>(1024, Allocator.Persistent);
                indexKeys = new NativeList<int3>(1024, Allocator.Persistent);
                indexJob = new IndexJob
                {
                    Source = source, ChunkSize = chunkSize, ChunkVoxel = chunkVoxel, Seen = indexSeen, Keys = indexKeys
                }.Schedule();
                indexing = true;
                return false;
            }
            if (!indexJob.IsCompleted)
                return false;
            indexJob.Complete();
            foreach (var key in indexKeys)
            {
                if (!columns.TryGetValue(key.xz, out var ys))
                    columns[key.xz] = ys = new List<int>();
                ys.Add(key.y);
            }
            DisposeIndex();
            indexed = true;
            return true;
        }

        void DisposeIndex()
        {
            if (!indexing)
                return;
            indexJob.Complete();
            indexSeen.Dispose();
            indexKeys.Dispose();
            indexing = false;
        }

        bool indexed, indexing;
        JobHandle indexJob;
        NativeHashSet<int3> indexSeen;
        NativeList<int3> indexKeys;

        /// <summary>
        /// The chunks each segment's walls can reach (IndexChunks). Each segment's box here is only as wide as its walls
        /// can be (radius and stretch, blend, wall noise, the wall cut-off and a couple of voxels), not the wider box
        /// sampling uses, so chunks the cave only grazes aren't built just to come out empty.
        /// </summary>
        [BurstCompile]
        struct IndexJob : IJob
        {
            public TerrainSource Source;
            public float ChunkSize, ChunkVoxel;
            public NativeHashSet<int3> Seen;
            public NativeList<int3> Keys;

            public void Execute()
            {
                ref var set = ref Source.Caves.Value;
                for (int i = 0; i < set.Segments.Length; i++)
                {
                    ref var seg = ref set.Segments[i];
                    ref var cave = ref set.Caves[seg.Cave];
                    ref var a = ref set.Nodes[seg.A];
                    ref var b = ref set.Nodes[seg.B];
                    // A stretched passage's field runs slower than true distance along the stretch, so distances
                    // measured in it reach that much further.
                    var stretch = CaveSampling.Stretch(cave.Profile);
                    var reach = math.max(a.Radius, b.Radius) * math.cmax(cave.Profile)
                                + (CaveSampling.Blend + CaveSampling.WallReach(cave) + WallReach) * stretch + ChunkVoxel * 2f;
                    var c0 = (int3)math.floor((math.min(a.Position, b.Position) - reach) / ChunkSize);
                    var c1 = (int3)math.floor((math.max(a.Position, b.Position) + reach) / ChunkSize);
                    for (int z = c0.z; z <= c1.z; z++)
                    for (int y = c0.y; y <= c1.y; y++)
                    for (int x = c0.x; x <= c1.x; x++)
                    {
                        var key = new int3(x, y, z);
                        if (Seen.Add(key))
                            Keys.Add(key);
                    }
                }
            }
        }

        float ChunkDistance(int3 key, float3 camPos)
        {
            var min = (float3)key * chunkSize;
            return math.distance(math.clamp(camPos, min, min + chunkSize), camPos);
        }

        void Stream(in TerrainSource source, float3 camPos)
        {
            if (!IndexChunks(source))
                return;

            // Which chunks are in range only changes when the camera crosses into another chunk.
            var cameraChunk = (int3)math.floor(camPos / chunkSize);
            if (!cameraChunk.Equals(lastCameraChunk))
            {
                lastCameraChunk = cameraChunk;
                wanted.Clear();
                var reach = (int)math.ceil(LoadRadius / chunkSize);
                for (int z = -reach; z <= reach; z++)
                for (int x = -reach; x <= reach; x++)
                {
                    var column = cameraChunk.xz + new int2(x, z);
                    if (!columns.TryGetValue(column, out var ys))
                        continue;
                    foreach (var y in ys)
                    {
                        var key = new int3(column.x, y, column.y);
                        if (!chunks.ContainsKey(key) && ChunkDistance(key, camPos) < LoadRadius)
                            wanted.Add(key);
                    }
                }
                // Nearest last, so they come off the end of the list first.
                var cam = camPos;
                wanted.Sort((a, b) => ChunkDistance(b, cam).CompareTo(ChunkDistance(a, cam)));

                scratch.Clear();
                foreach (var kv in chunks)
                    if (ChunkDistance(kv.Key, camPos) > LoadRadius * 1.2f)
                        scratch.Add(kv.Key);
                foreach (var key in scratch)
                {
                    // One still building is parked until its jobs finish rather than waited on here.
                    if (chunks[key].Pending)
                        dying.Add(chunks[key]);
                    else
                        chunks[key].Dispose();
                    chunks.Remove(key);
                }
            }
            for (int i = dying.Count - 1; i >= 0; i--)
                if (dying[i].Job.IsCompleted)
                {
                    dying[i].Dispose();
                    dying.RemoveAt(i);
                }

            // The camera's speed, smoothed over about half a second (a teleport reads as briefly fast).
            var dt = math.max(Time.unscaledDeltaTime, 1e-4f);
            var moved = lastStreamCamera.x == float.MaxValue ? 0f : math.distance(camPos, lastStreamCamera);
            lastStreamCamera = camPos;
            cameraSpeed = math.lerp(cameraSpeed, moved / dt, 1f - math.exp(-dt * 2f));
            var fast = cameraSpeed > FastSpeed;

            var building = dying.Count;
            foreach (var b in chunks.Values)
                if (b.Pending)
                    building++;
            for (int n = 0; n < BuildsPerFrame && building < (fast ? 1 : MaxBuilding) && wanted.Count > 0; n++, building++)
            {
                var key = wanted[wanted.Count - 1];
                // Moving fast: only the near ones (the list is nearest last, so the rest wait).
                if (fast && ChunkDistance(key, camPos) > FastRadius)
                    break;
                wanted.RemoveAt(wanted.Count - 1);
                if (!chunks.ContainsKey(key))
                    chunks[key] = StartBuild(source, key);
            }

            var uploads = 0;
            foreach (var kv in chunks)
            {
                var b = kv.Value;
                if (!b.Pending || !b.Job.IsCompleted)
                    continue;
                b.Job.Complete();   // already done: cheap
                // Empty ones cost nothing to finish; those with a mesh wait their turn.
                if (b.Indices.Length > 0 && uploads >= UploadsPerFrame)
                    continue;
                b.Pending = false;
                // Chunks the cave only grazes come out empty: keep the record (so they aren't rebuilt), not a mesh.
                if (b.Indices.Length > 0)
                {
                    uploads++;
                    var mesh = new Mesh { name = "Cave chunk", indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(b.Positions.AsArray());
                    mesh.SetNormals(b.Normals.AsArray());
                    mesh.SetColors(b.Colors.AsArray());
                    mesh.SetUVs(0, b.Data.AsArray());
                    mesh.SetIndices(b.Indices.AsArray(), MeshTopology.Triangles, 0);
                    var min = (float3)kv.Key * chunkSize;
                    mesh.bounds = new Bounds(min + chunkSize * 0.5f, new float3(chunkSize + chunkVoxel * 4f));
                    // Drawn only: let the CPU copy go.
                    mesh.UploadMeshData(true);
                    b.Mesh = mesh;
                    // A chunk whose top is a few metres under the lowest ground above it can't shade anything the sun
                    // reaches, so it stays out of the shadow cascades (the column heights are already sampled) -
                    // unless it's near a mouth, where daylight comes in sideways under the hill.
                    var lowestGround = float.MaxValue;
                    for (int i = 0; i < b.Surface.Length; i++)
                        lowestGround = math.min(lowestGround, b.Surface[i]);
                    b.CastsShadows = min.y + chunkSize + chunkVoxel > lowestGround - 4f || NearMouth(source, min, min + chunkSize, MouthLight);
                    // Buried: wholly a few metres under the ground and out of sight of every mouth (a view in through a
                    // mouth reaches a winding passage only so far), so it's skipped while the camera is outside caves.
                    b.Buried = min.y + chunkSize + chunkVoxel < lowestGround - 4f && !NearMouth(source, min, min + chunkSize, BuriedReach);
                }
                b.Field.Dispose();
                b.CaveField.Dispose();
                b.Surface.Dispose();
                b.SurfaceScale.Dispose();
                b.Positions.Dispose();
                b.Normals.Dispose();
                b.Colors.Dispose();
                b.Data.Dispose();
                b.Indices.Dispose();
            }
        }

        /// <summary>Daylight from a mouth lights walls up to about 17 m in (Look's fromMouth), so walls within 20 m shade.</summary>
        const float MouthLight = 20f;
        /// <summary>How far in from a mouth a cave chunk can be seen from outside (beyond it, from the surface it's buried).</summary>
        const float BuriedReach = 32f;

        static bool NearMouth(in TerrainSource source, float3 min, float3 max, float reach)
        {
            ref var set = ref source.Caves.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                var mouth = set.Caves[c].Mouth;
                if (math.distance(math.clamp(mouth, min, max), mouth) < reach)
                    return true;
            }
            return false;
        }

        Built StartBuild(in TerrainSource source, int3 key)
        {
            // Points run from one voxel before the chunk to one after its far side; cells from -1 to chunkCells. The
            // origin is a global grid index, so neighbouring chunks sample their shared apron at bit-identical points
            // (a float origin plus an offset rounds differently in each, and a value near zero can flip sign).
            var origin = key * chunkCells - 1;
            var cells = new int3(chunkCells + 2);
            var points = cells + 1;
            var b = new Built
            {
                Field = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                CaveField = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Surface = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                SurfaceScale = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Data = new NativeList<float4>(Allocator.Persistent),
                Positions = new NativeList<float3>(Allocator.Persistent),
                Normals = new NativeList<float3>(Allocator.Persistent),
                Colors = new NativeList<float4>(Allocator.Persistent),
                Indices = new NativeList<int>(Allocator.Persistent),
                Pending = true
            };
            var columnJob = new ColumnJob { Source = source, Origin = origin, Points = points, Voxel = chunkVoxel, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.x * points.z, 64);
            var field = new FieldJob { Source = source, Origin = origin, Points = points, Voxel = chunkVoxel, Field = b.Field, CaveField = b.CaveField, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.y * points.z, 4, columnJob);
            b.Job = new MeshJob
            {
                Source = source, Origin = origin, Cells = cells, Voxel = chunkVoxel,
                Field = b.Field, CaveField = b.CaveField,
                Positions = b.Positions, Normals = b.Normals, Colors = b.Colors, Data = b.Data, Indices = b.Indices
            }.Schedule(field);
            JobHandle.ScheduleBatchedJobs();
            return b;
        }

        /// <summary>
        /// Per grid column: the surface height, and how to turn height above it into distance across the slope.
        /// </summary>
        [BurstCompile]
        struct ColumnJob : IJobParallelFor
        {
            public TerrainSource Source;
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
            public int3 Points;
            public float Voxel;
            [WriteOnly] public NativeArray<float> Surface, SurfaceScale;

            public void Execute(int i)
            {
                var xz = (float2)(Origin.xz + new int2(i % Points.x, i / Points.x)) * Voxel;
                var h = Source.SampleHeight(xz);
                const float e = 0.5f;
                var slope = new float2(Source.SampleHeight(xz + new float2(e, 0f)) - Source.SampleHeight(xz - new float2(e, 0f)),
                                       Source.SampleHeight(xz + new float2(0f, e)) - Source.SampleHeight(xz - new float2(0f, e))) / (2f * e);
                Surface[i] = h;
                SurfaceScale[i] = math.rsqrt(1f + math.lengthsq(slope));
            }
        }

        /// <summary>
        /// Samples, at every grid point, the rock's signed distance (negative in rock: 10 cm under the surface,
        /// measured across the slope, and outside every cave) and the nearest cave's distance. One job item per grid row.
        /// </summary>
        [BurstCompile]
        struct FieldJob : IJobParallelFor
        {
            public TerrainSource Source;
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
            public int3 Points;
            public float Voxel;
            [NativeDisableParallelForRestriction] public NativeArray<float> Field, CaveField;
            [ReadOnly] public NativeArray<float> Surface, SurfaceScale;

            public void Execute(int row)
            {
                var y = row % Points.y;
                var z = row / Points.y;
                for (int x = 0; x < Points.x; x++)
                {
                    var p = (float3)(Origin + new int3(x, y, z)) * Voxel;
                    var cave = CaveSampling.Distance(ref Source.Caves.Value, p, out _);
                    var i = (z * Points.y + y) * Points.x + x;
                    CaveField[i] = cave;
                    var column = z * Points.x + x;
                    // Air is above the surface or in the cave; a smooth union, so a lip of rock thinner than about half
                    // a metre between them (a thin roof at a mouth) opens up instead of hanging as a floating slab. The
                    // terrain cuts its own surface from this same field (TerrainSource.MouthCut), so the two agree.
                    Field[i] = -CaveSampling.SmoothMin(-((p.y - Surface[column]) * SurfaceScale[column] + 0.1f), cave, CaveSampling.MouthBlend);
                }
            }
        }

        [BurstCompile]
        struct MeshJob : IJob
        {
            public TerrainSource Source;
            /// <summary>Global grid index of the first point (world position = index × Voxel).</summary>
            public int3 Origin;
            /// <summary>Cells including the apron: the chunk's own cells are 1 to Cells - 2.</summary>
            public int3 Cells;
            public float Voxel;
            [ReadOnly] public NativeArray<float> Field, CaveField;
            public NativeList<float3> Positions, Normals;
            public NativeList<float4> Colors;
            public NativeList<float4> Data;
            public NativeList<int> Indices;

            int Point(int x, int y, int z) => (z * (Cells.y + 1) + y) * (Cells.x + 1) + x;
            int Cell(int x, int y, int z) => (z * Cells.y + y) * Cells.x + x;

            public void Execute()
            {
                var cellVertex = new NativeArray<int>(Cells.x * Cells.y * Cells.z, Allocator.Temp);
                for (int i = 0; i < cellVertex.Length; i++)
                    cellVertex[i] = -1;

                // A vertex in every cell the boundary crosses (near the cave): the average of the crossing points
                // on the cell's edges.
                for (int z = 0; z < Cells.z; z++)
                for (int y = 0; y < Cells.y; y++)
                for (int x = 0; x < Cells.x; x++)
                {
                    int rock = 0;
                    var nearCave = false;
                    for (int c = 0; c < 8; c++)
                    {
                        var i = Point(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1));
                        if (Field[i] < 0f) rock++;
                        nearCave |= CaveField[i] < WallReach;
                    }
                    if (rock == 0 || rock == 8 || !nearCave)
                        continue;

                    var sum = float3.zero;
                    var crossings = 0;
                    for (int c = 0; c < 8; c++)
                    for (int bit = 1; bit <= 4; bit <<= 1)
                    {
                        if ((c & bit) != 0)
                            continue;
                        var d = c | bit;
                        var ca = new int3(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                        var cb = new int3(d & 1, (d >> 1) & 1, (d >> 2) & 1);
                        var va = Field[Point(x + ca.x, y + ca.y, z + ca.z)];
                        var vb = Field[Point(x + cb.x, y + cb.y, z + cb.z)];
                        if (va < 0f == vb < 0f)
                            continue;
                        sum += math.lerp((float3)ca, (float3)cb, va / (va - vb));
                        crossings++;
                    }
                    var p = ((float3)(Origin + new int3(x, y, z)) + sum / crossings) * Voxel;
                    cellVertex[Cell(x, y, z)] = Positions.Length;
                    Positions.Add(p);
                    Normals.Add(Gradient(p));
                    Colors.Add(Look(p, out var data));
                    Data.Add(data);
                }

                // A quad across every grid edge the boundary crosses, joining the four cells round it. Only edges that
                // start at the chunk's own points (1 to Cells - 2; 0 and the last are the apron): the neighbour joins
                // the rest, using the same apron vertices, so the seam closes with nothing drawn twice.
                var own = Cells - 2;
                for (int z = 1; z <= own.z; z++)
                for (int y = 1; y <= own.y; y++)
                for (int x = 1; x <= own.x; x++)
                {
                    var a = Field[Point(x, y, z)] < 0f;
                    if (a != Field[Point(x + 1, y, z)] < 0f)
                        Quad(cellVertex, Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), a);
                    if (a != Field[Point(x, y + 1, z)] < 0f)
                        Quad(cellVertex, Cell(x - 1, y, z - 1), Cell(x - 1, y, z), Cell(x, y, z), Cell(x, y, z - 1), a);
                    if (a != Field[Point(x, y, z + 1)] < 0f)
                        Quad(cellVertex, Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z), a);
                }
                cellVertex.Dispose();
            }

            void Quad(NativeArray<int> cellVertex, int c0, int c1, int c2, int c3, bool flip)
            {
                int v0 = cellVertex[c0], v1 = cellVertex[c1], v2 = cellVertex[c2], v3 = cellVertex[c3];
                if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                    return;
                if (flip)
                {
                    Indices.Add(v0); Indices.Add(v2); Indices.Add(v1);
                    Indices.Add(v0); Indices.Add(v3); Indices.Add(v2);
                }
                else
                {
                    Indices.Add(v0); Indices.Add(v1); Indices.Add(v2);
                    Indices.Add(v0); Indices.Add(v2); Indices.Add(v3);
                }
            }

            float RockAt(float3 p) => math.max(p.y - Source.SampleHeight(p.xz) + 0.1f, -CaveSampling.Distance(ref Source.Caves.Value, p, out _));

            /// <summary>Points out of the rock, into the air.</summary>
            float3 Gradient(float3 p)
            {
                const float e = 0.15f;
                return math.normalizesafe(new float3(
                    RockAt(p + new float3(e, 0f, 0f)) - RockAt(p - new float3(e, 0f, 0f)),
                    RockAt(p + new float3(0f, e, 0f)) - RockAt(p - new float3(0f, e, 0f)),
                    RockAt(p + new float3(0f, 0f, e)) - RockAt(p - new float3(0f, 0f, e))), new float3(0f, 1f, 0f));
            }

            /// <summary>
            /// rgb the terrain's colour at this spot; alpha how much daylight reaches (the entrance and near the
            /// surface). Data: x how much to look like the terrain (1 at the surface, fading out a few metres
            /// underground), y a slight brightness tint for the cave rock, z the cave's style slot, w how much darker
            /// (negative: lighter) ribs make it here.
            /// </summary>
            float4 Look(float3 p, out float4 data)
            {
                var surface = Source.SampleHeight(p.xz);
                // Daylight from the mouth of whichever cave this wall belongs to.
                ref var set = ref Source.Caves.Value;
                CaveSampling.Distance(ref set, p, out var cave);
                var fromMouth = cave >= 0 ? math.saturate(1f - (math.distance(p, set.Caves[cave].Mouth) - 3f) / 14f) : 0f;
                var nearSurface = math.saturate(1f - (surface - p.y - 1f) / 3f);
                // Open to the sky (the terrain is cut away above: a trench or crater at a mouth): it's outside ground,
                // so it takes the land's look and daylight however far below the uncut surface height it lies. Generous
                // (up to 2 m of clearance): the terrain's cut is interpolated between its vertices, and wherever the
                // terrain stays it hides this skin anyway.
                var open = math.saturate((2f - Source.CaveClearance(p.xz, surface)) / 1.5f);
                var ground = Source.SampleGround(p.xz);
                var albedo = Source.SampleSurface(p.xz, ground, Source.SampleNormal(p.xz, 1f)).Albedo;
                // Ribbed caves: how much darker the hollow between two ribs is (crests a touch lighter), applied to
                // every look the wall takes, so the ribs read even in the flat light of a shallow ice cave; none where
                // the cave opens to the sky, so mouths still match the terrain.
                var ribShade = 0f;
                if (cave >= 0 && set.Caves[cave].Wall.z > 0f)
                {
                    var rib = CaveSampling.Rib(set.Caves[cave], p);
                    ribShade = (0.55f - 0.75f * rib * rib) * set.Caves[cave].Wall.z * (1f - open);
                }
                data = new float4(math.max(math.saturate(1f - (surface - p.y - 0.3f) / 2.5f), open),
                                  0.85f + 0.3f * (noise.snoise(p * 0.15f) * 0.5f + 0.5f), cave >= 0 ? set.Caves[cave].Style : 0, ribShade);
                return new float4(albedo, math.max(math.max(fromMouth, nearSurface), open));
            }
        }

        void Draw(bool outside)
        {
            foreach (var b in chunks.Values)
            {
                if (b.Mesh == null || outside && b.Buried)
                    continue;
                var rp = new RenderParams(Material)
                {
                    worldBounds = b.Mesh.bounds,
                    shadowCastingMode = b.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true
                };
                Graphics.RenderMesh(rp, b.Mesh, 0, Matrix4x4.identity);
            }
        }

        // ------------------------------------------------------------------------------------------- lantern

        void UpdateLantern(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingleton<Unity.Transforms.LocalTransform>(out var player))
                return;
            lanternLevel = Mathf.MoveTowards(lanternLevel, LanternWanted(source, player.Position), Time.deltaTime * 1.2f);
            if (lantern == null)
                lantern = CreateLantern("Lantern");
            PlaceLantern(lantern, player, lanternLevel, 0.5f);
            // The flora shader has no light loop (it would cost every plant on the surface), so cave plants get the
            // lantern as one cheap term from these.
            var at = lantern.transform.position;
            Shader.SetGlobalVector(LanternId, new Vector4(at.x, at.y, at.z, lantern.enabled ? lantern.intensity * 0.2f : 0f));
            Shader.SetGlobalVector(LanternColorId, new Vector4(LanternColor.r, LanternColor.g, LanternColor.b, LanternRange));
        }

        static readonly int LanternId = Shader.PropertyToID("_PunchyLantern");
        static readonly int LanternColorId = Shader.PropertyToID("_PunchyLanternColor");

        /// <summary>How lit someone's lantern should be: in a cave and away from the daylight at the entrance.</summary>
        static float LanternWanted(in TerrainSource source, float3 feet)
        {
            source.FloorUnder(feet, out var inCave);
            var depth = source.SampleHeight(feet.xz) - feet.y;
            return inCave ? math.saturate((depth - 1f) / 3f) : 0f;
        }

        Light CreateLantern(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            return light;
        }

        void PlaceLantern(Light light, in Unity.Transforms.LocalTransform holder, float level, float flickerSeed)
        {
            light.enabled = level > 0.01f;
            light.color = LanternColor;
            light.range = LanternRange;
            var flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, flickerSeed);
            light.intensity = LanternIntensity * level * flicker;
            var forward = math.mul(holder.Rotation, new float3(0f, 0f, 1f));
            light.transform.position = holder.Position + new float3(0f, 1.6f, 0f) + forward * 0.4f;
        }

        void UpdateOtherLanterns(in TerrainSource source, Vector3 camera)
        {
            if (othersQuery.IsEmpty && otherLanterns.Count == 0)
                return;
            foreach (var l in otherLanterns.Values)
                l.Seen = false;
            var em = queryWorld.EntityManager;
            var entities = othersQuery.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                if (em.HasComponent<Unity.NetCode.GhostOwnerIsLocal>(e) && em.IsComponentEnabled<Unity.NetCode.GhostOwnerIsLocal>(e))
                    continue;
                var t = em.GetComponentData<Unity.Transforms.LocalTransform>(e);
                if (math.lengthsq(t.Position) < 1e-6f || math.distance(t.Position, camera) > 150f)
                    continue;
                if (!otherLanterns.TryGetValue(e, out var l))
                    otherLanterns[e] = l = new OtherLantern { Light = CreateLantern("Lantern (other player)") };
                l.Seen = true;
                l.Level = Mathf.MoveTowards(l.Level, LanternWanted(source, t.Position), Time.deltaTime * 1.2f);
                PlaceLantern(l.Light, t, l.Level, 1.5f + (e.Index % 7));
            }
            entities.Dispose();
            goneLanterns.Clear();
            foreach (var pair in otherLanterns)
                if (!pair.Value.Seen)
                    goneLanterns.Add(pair.Key);
            foreach (var e in goneLanterns)
            {
                if (otherLanterns[e].Light != null) Destroy(otherLanterns[e].Light.gameObject);
                otherLanterns.Remove(e);
            }
        }
    }
}
