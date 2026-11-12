using System.Collections.Generic;
using Punchy.Settings;
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
    /// Only the boundary close to the cave is kept: the cave walls, which end 2 cm under the terrain surface
    /// (measured across the slope). The terrain cuts its own opening just inside the cave's edge, overhanging the
    /// walls' top edge, so there's no seam. Near the surface the walls take the terrain's colour and blend into cave
    /// rock as they go underground.
    ///
    /// Underground (camera in a cave, well below the surface) fades from 0 to 1: weather stops, and a lantern on the
    /// player lights the way.
    /// </summary>
    public partial class CaveSystem : MonoBehaviour
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
        /// How completely the surface world is out of sight, 0..1: Underground, faded out near any cave mouth (0 while
        /// the nearest mouth looks bigger than DetailPolicy.SurfaceShownPixels, 1 once smaller than SurfaceHiddenPixels:
        /// 25 m and 45 m at the reference view), so a view out of a mouth still shows the outside. Systems drawing things
        /// on the surface (flora, terrain beyond the camera's own neighbourhood, creatures) skip them at 1. The one rule
        /// everyone keys on.
        /// </summary>
        public static float SurfaceHidden { get; private set; }

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
                    if (b.Pending || b.Next != null)
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
            /// <summary>Which caves' walls reach it, as bits (cave index mod 64; a shared bit only ever draws more).</summary>
            public ulong Caves;
            public int Triangles;
            /// <summary>W-135: the box round its walls that lie open to the sky (where the terrain is cut for a mouth),
            /// [0] min and [1] max; min above max when it has none. Written by MeshJob.</summary>
            public NativeArray<float3> Opening;
            /// <summary>Its opening box (copied out of Opening on upload), and whether it has one.</summary>
            public Bounds OpeningBounds;
            public bool Opens;
            /// <summary>Whether its opening counted as in view last frame (the surface-side skip's hysteresis).</summary>
            public bool OpeningSeen;
            /// <summary>W-135 row 2: its wall level (0: Voxel, 1: twice it, 2: four times), and the rebuild at another
            /// level in flight, swapped in once uploaded so the old walls draw until then.</summary>
            public int Level;
            public Built Next;
            /// <summary>W-135 row 4: the contacts its walls are darkened round (its build's own copy), and the hash of the
            /// chunk's contact list it was built with, so a chunk whose list has changed since is rebuilt.</summary>
            public NativeArray<ContactShade.Contact> Contacts;
            public uint ContactHash;
            /// <summary>W-135 row 6: what the surface skip made of it last frame (0 hidden, 1 shadow only, 2 drawn; -1 not yet),
            /// to count flips.</summary>
            public int LastSeen = -1;
            /// <summary>W-135 row 6: the least cave distance among its field's points (written by MeshJob), and whether
            /// it came out empty with the cave too far off for a finer grid to find a wall either (never rebuilt finer).</summary>
            public NativeArray<float> Gap;
            public bool Clear;

            public void Dispose()
            {
                if (Next != null)
                {
                    Next.Dispose();
                    Next = null;
                }
                Job.Complete();
                if (Opening.IsCreated) Opening.Dispose();
                if (Gap.IsCreated) Gap.Dispose();
                if (Contacts.IsCreated) Contacts.Dispose();
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
        [Tooltip("W-135 row 6: chunks rebuilt (at another wall level, or for a changed contact list) started per frame at " +
                 "most, and in flight at once. A finer rebuild is a new chunk's whole cost (a level-0 field is about 300k " +
                 "points), so walking up to a mouth they're spread out, not started two a frame.")]
        [Min(1)] public int RebuildsPerFrame = 1;
        [Min(1)] public int MaxRebuilding = 2;
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
            // Quitting (or leaving play mode) frees the TerrainSource blobs before this is disabled, and the surface
            // glow's far scan job may still be reading them: finish every job first, while they are intact.
            Application.quitting += ReleaseWorld;
            if (detail == null)
                detail = MakeDetail();
            Shader.SetGlobalTexture(DetailId, detail);
            // Water reads it too, for fine ripples; it keeps them off until this is set.
            Shader.SetGlobalFloat(DetailReadyId, 1f);
            Shader.SetGlobalFloat(WaterDetailId, WaterDetail ? 1f : 0f);
            Shader.SetGlobalFloat(WaterNoSsrId, ArgIs("-waterssr", "0") ? 1f : 0f);
            Shader.SetGlobalFloat(RefractionId, ArgIs("-waterrefraction", "0") ? 0f : 0.035f);
        }

        static readonly int WaterDetailId = Shader.PropertyToID("_PunchyWaterDetail");
        static readonly int WaterNoSsrId = Shader.PropertyToID("_PunchyWaterNoSsr");
        static readonly int RefractionId = Shader.PropertyToID("_PunchyRefraction");

        /// <summary>Whether the command line has <paramref name="flag"/> followed by <paramref name="value"/>.</summary>
        static bool ArgIs(string flag, string value)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    return args[i + 1] == value;
            return false;
        }

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
            Application.quitting -= ReleaseWorld;
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
            ReleaseContacts();
            columns.Clear();
            chunkCaves.Clear();
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
            Punchy.Settings.LightBudget.ClearSlots();
            ReleaseCaveWater();
            ReleasePlants();
            if (floraMaterial != null)
                Destroy(floraMaterial);
            floraMaterial = null;
            if (caveWater != null)
                Destroy(caveWater);
            caveWater = null;
            ReleaseBatFolk();
            ReleaseRooms();
            Shader.SetGlobalVector(LanternId, Vector4.zero);
        }

        // ------------------------------------------------------------------------------------------- bat folk

        /// <summary>Per bat folk site: its pale, dark and rock meshes (BatFolkMeshes), built when the world is first drawn.
        /// The rock mesh (W-96: the pygmy niches) is drawn with the cave material, in the cave's own style slot.</summary>
        readonly List<(Mesh pale, Mesh dark, Mesh rock)> batFolkMeshes = new List<(Mesh, Mesh, Mesh)>();
        Material batFolkPale, batFolkDark;
        static readonly int RockColorId = Shader.PropertyToID("_RockColor");
        static readonly int DarkColorId = Shader.PropertyToID("_DarkColor");

        /// <summary>
        /// Draws the bat folk huts and floor props (#35, W-19) within LoadRadius with two copies of the cave material
        /// (pale silk, dark wood), so they are lit like the walls: lantern, glow lights, no daylight.
        /// </summary>
        void DrawBatFolk(in TerrainSource source, float3 camPos)
        {
            if (!source.BatFolk.IsCreated)
                return;
            ref var bat = ref source.BatFolk.Value;
            if (bat.Sites.Length == 0 || bat.Props.Length == 0)
                return;
            if (batFolkMeshes.Count == 0)
            {
                for (int s = 0; s < bat.Sites.Length; s++)
                {
                    var cave = bat.Sites[s].Cave;
                    var style = source.Caves.IsCreated && cave >= 0 && cave < source.Caves.Value.Caves.Length ? source.Caves.Value.Caves[cave].Style : 0;
                    BatFolkMeshes.Build(ref bat, s, style, out var pale, out var dark, out var rock);
                    batFolkMeshes.Add((pale, dark, rock));
                }
                batFolkPale = new Material(Material) { name = "Bat folk pale" };
                batFolkPale.SetColor(RockColorId, BatFolkMeshes.PaleRock);
                batFolkPale.SetColor(DarkColorId, BatFolkMeshes.PaleDark);
                batFolkDark = new Material(Material) { name = "Bat folk dark" };
                batFolkDark.SetColor(RockColorId, BatFolkMeshes.DarkRock);
                batFolkDark.SetColor(DarkColorId, BatFolkMeshes.DarkDark);
            }
            for (int s = 0; s < batFolkMeshes.Count; s++)
            {
                if (math.distance(bat.Sites[s].Centre, camPos) > LoadRadius)
                    continue;
                var (pale, dark, rock) = batFolkMeshes[s];
                if (pale != null)
                    Graphics.RenderMesh(new RenderParams(batFolkPale) { worldBounds = pale.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, pale, 0, Matrix4x4.identity);
                if (dark != null)
                    Graphics.RenderMesh(new RenderParams(batFolkDark) { worldBounds = dark.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, dark, 0, Matrix4x4.identity);
                if (rock != null)
                    Graphics.RenderMesh(new RenderParams(Material) { worldBounds = rock.bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, rock, 0, Matrix4x4.identity);
            }
        }

        void ReleaseBatFolk()
        {
            foreach (var (pale, dark, rock) in batFolkMeshes)
            {
                if (pale != null) Destroy(pale);
                if (dark != null) Destroy(dark);
                if (rock != null) Destroy(rock);
            }
            batFolkMeshes.Clear();
            if (batFolkPale != null) Destroy(batFolkPale);
            if (batFolkDark != null) Destroy(batFolkDark);
            batFolkPale = batFolkDark = null;
        }

        // ------------------------------------------------------------------------------------------- pools

        /// <summary>A lava pool's glow (rgb linear x intensity; its reach is set by its size).</summary>
        static readonly Vector4 LavaLight = new Vector4(1f * 2.2f, 0.42f * 2.2f, 0.1f * 2.2f, 0f);

        /// <summary>
        /// Hands this frame's light requests (lava pools, glowing plants, wall torches, room lights, lit windows here, and
        /// every other system's) to LightBudget, which picks the 16 glow slots and the real lights by LightPolicy and
        /// uploads them (refactor 3.1; it was nearest-first here).
        /// </summary>
        static void UploadGlowing(Camera cam) => Punchy.Settings.LightBudget.Resolve(cam);
        readonly List<(float distance, int pool)> nearPools = new List<(float, int)>();
        // Comparers made once: List.Sort(Comparison) wraps the delegate in a new comparer on every call, every frame.
        static readonly Comparer<(float distance, int pool)> PoolsByDistance =
            Comparer<(float distance, int pool)>.Create((a, b) => a.distance.CompareTo(b.distance));
        /// <summary>
        /// Lava pools' look (rgb, murk): the water shader's lava path (glow y = 1) makes it glow and creep. W-148 row 4:
        /// on the cave water lattice like any pool, fitted to its basin (it was a shared disc mesh).
        /// </summary>
        static readonly float4 LavaPoolLook = new float4(1f, 0.33f, 0.05f, 0.2f);
        /// <summary>Cave water's look in the water vertex layout: dark, clear and cold (rgb, murk).</summary>
        static readonly float4 CaveWater = new float4(0.1f, 0.28f, 0.3f, 0.1f);
        /// <summary>
        /// White pool water (bat folk caves, #35): milky and pale. Murk stays low (more than about 0.3 and
        /// the shader's opacity turns the pool into a hard-edged slab that reads as snow), so the rim still fades into the
        /// bed and it lies still, well under the thick-and-oily threshold (0.55); the whiteness comes from the colour and a
        /// self-glow, which also shows it where nobody carries a light. The glow's hue swirl only trades blue for green, which stays near white.
        /// W-17: toned down (the pool read as a milky blob).
        /// W-61: milkier again (#35: a milky-white look): a warm bone white instead of the blue-grey, murk 0.24 (still under 0.3).
        /// W-148: drawn as ordinary cave water with this look (no soft-rim draw): it swims, wades and splashes like water.
        /// </summary>
        static readonly float4 WhitePoolWater = new float4(0.88f, 0.88f, 0.84f, 0.24f);
        const float WhitePoolGlow = 0.12f;
        /// <summary>
        /// "-whitepools": every cave water pool is drawn as white pool water (a debug override: W-18 bat folk chambers
        /// carry their own white pools in TerrainSource.BatFolk). Drawing only, so it changes nothing that is baked.
        /// </summary>
        public static bool WhitePools = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-whitepools") >= 0;
        /// <summary>Switches the white pool look at runtime (editor previews): baked cave water rebuilds on the next draw.</summary>
        public static void SetWhitePools(bool on)
        {
            WhitePools = on;
            waterLooks++;
        }
        // Streams run shallow over dark rock: darker and greener than the pools, a little murkier.
        static readonly float4 CaveStreamWater = new float4(0.04f, 0.13f, 0.12f, 0.15f);
        /// <summary>Per pool: its glow handed on last frame (DetailPolicy.Within holds it; only lava pools glow).</summary>
        bool[] poolDrawn = System.Array.Empty<bool>();
        EntityQuery configQuery;

        /// <summary>
        /// Draws cave water with the terrain's own water material: pools and streams baked like chunk water
        /// (CaveSystem.Water, W-134; each tile drawn only while big enough on screen), lava pools among them in the lava
        /// look (W-148 row 4). Hands the lava pools' glow, while within LoadRadius and a couple of pixels wide
        /// (DetailPolicy.CaveWaterPixels, held by DetailPolicy.Within, W-135), to LightBudget so the cave shader can light
        /// the walls round them warmly without real lights.
        /// </summary>
        void DrawPools(in TerrainSource source, float3 camPos)
        {
            ref var set = ref source.Caves.Value;
            nearPools.Clear();
            if (poolDrawn.Length != set.Pools.Length)
                poolDrawn = new bool[set.Pools.Length];
            for (int i = 0; i < set.Pools.Length; i++)
            {
                var d = math.distance(set.Pools[i].Centre, camPos);
                if (set.Pools[i].Lava <= 0.5f)
                {
                    poolDrawn[i] = false;
                    continue;
                }
                var reach = math.min(DetailPolicy.ViewReach(set.Pools[i].Radius * 2f, DetailPolicy.CaveWaterPixels), LoadRadius);
                poolDrawn[i] = DetailPolicy.Within(d, reach, poolDrawn[i]);
                if (poolDrawn[i])
                    nearPools.Add((d, i));
            }
            var water = UnderRockWater(configQuery.IsEmpty ? null : configQuery.GetSingleton<TerrainConfig>().WaterMaterial.Value);
            DrawCaveWater(source, camPos, water);
            if (nearPools.Count == 0)
                return;
            nearPools.Sort(PoolsByDistance);
            foreach (var (_, i) in nearPools)
            {
                ref var pool = ref set.Pools[i];
                var reach = LavaLight;
                reach.w = pool.Radius * 5f + 4f;
                // (LightBudget decides by its on-screen reach whether it takes a glow slot: refactor 3.1.)
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.AmbientGlow, (Vector3)pool.Centre + Vector3.up * 0.3f, reach);
            }
        }

        // ------------------------------------------------------------------------------------------- plants

        /// <summary>W-135 row 3: how far a cave plant is drawn (DetailPolicy.CavePlantPixels, x Detail: 60 m at the
        /// reference view and Detail 1).</summary>
        static float PlantRange => DetailPolicy.ExtraReach(DetailPolicy.CavePlantSize, DetailPolicy.CavePlantPixels);
        /// <summary>Per plant (index into the cave set's Plants): drawn, and drawn at its finest, at the last gather.</summary>
        bool[] plantShown = System.Array.Empty<bool>(), plantFine = System.Array.Empty<bool>();
        float nextFloraLookup;
        /// <summary>
        /// Variants grown per species, and the stride between species' models. It must cover every variant baking
        /// stores: mine frames use one per MineFrameHeights entry, and with a stride of 3 their tall variants 3 and 4
        /// drew the next species' models instead, a frame-sized wall torch standing mid-passage (W-100).
        /// </summary>
        public static readonly int PlantVariants = math.max(3, Punchy.Flora.FloraGenerator.MineFrameHeights.Length);
        /// <summary>Cave plants that look bigger than DetailPolicy.CavePlantFinestPixels (20 m at the reference view, no
        /// Detail) use the finest mesh, the rest the middle one.</summary>
        static float PlantFinestRange => DetailPolicy.ViewReach(DetailPolicy.CavePlantSize, DetailPolicy.CavePlantFinestPixels);

        readonly List<Punchy.Flora.FloraModel> plantModels = new List<Punchy.Flora.FloraModel>();
        BiomePalette plantsFrom;
        /// <summary>The palette whose cave plant models are part grown (from plantSpecies), until all are.</summary>
        BiomePalette plantsGrowing;
        List<Punchy.Flora.FloraSpecies> plantSpecies;
        /// <summary>Time each frame may spend growing cave plant models (#102).</summary>
        const double PlantGrowBudgetMs = 6.0;
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
        /// <summary>How far a glowing plant lights the floor and walls round it (m).</summary>
        const float PlantGlowReach = 5f;
        float3 plantsAround = new float3(float.MaxValue);
        static readonly int FloraInstancesId = Shader.PropertyToID("_FloraInstances");
        static readonly int FloraVisibleId = Shader.PropertyToID("_FloraVisible");

        /// <summary>Whether p is in a bat folk settlement's chamber (its radius and a margin, a little above and below).</summary>
        static bool InBatFolkChamber(in TerrainSource source, float3 p)
        {
            if (!source.BatFolk.IsCreated)
                return false;
            ref var sites = ref source.BatFolk.Value.Sites;
            for (int s = 0; s < sites.Length; s++)
            {
                var reach = sites[s].Radius * 1.4f + 4f;
                if (math.distancesq(p.xz, sites[s].Centre.xz) < reach * reach && math.abs(p.y - sites[s].Centre.y) < reach)
                    return true;
            }
            return false;
        }

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
                if (palette != plantsGrowing)
                {
                    ReleasePlants();
                    plantSpecies = CaveFloraTable.Collect(palette.Landmarks);
                    plantsGrowing = palette;
                }
                // Growing every model at once was a ~150 ms hitch on the first cave frame (#102): grow them in model
                // order a few ms' worth per frame, and draw nothing until all are grown (same models, a few frames later).
                var budget = System.Diagnostics.Stopwatch.StartNew();
                while (plantModels.Count < plantSpecies.Count * PlantVariants && budget.Elapsed.TotalMilliseconds < PlantGrowBudgetMs)
                {
                    var species = plantSpecies[plantModels.Count / PlantVariants];
                    plantModels.Add(Punchy.Flora.FloraGenerator.Grow(species, plantModels.Count % PlantVariants));
                    if (plantModels.Count % PlantVariants == 0)
                    {
                        // The same light the plant casts on the surface (colour and reach by how much of it glows).
                        speciesGlow.Add(Punchy.Flora.FloraPlacement.GlowLight(species));
                        speciesTorch.Add(species.Form == Punchy.Flora.FloraForm.Torch ? species.Height.y : 0f);
                    }
                }
                if (plantModels.Count < plantSpecies.Count * PlantVariants)
                    return;
                plantsFrom = palette;
                plantsGrowing = null;
                plantsAround = new float3(float.MaxValue);
            }

            if (math.distancesq(camPos, plantsAround) > 16f)
            {
                plantsAround = camPos;
                plantKeys.Clear();
                nearGlowPlants.Clear();
                nearTorches.Clear();
                if (plantShown.Length != set.Plants.Length)
                {
                    plantShown = new bool[set.Plants.Length];
                    plantFine = new bool[set.Plants.Length];
                }
                float plantRange = PlantRange, finestRange = PlantFinestRange;
                for (int i = 0; i < set.Plants.Length; i++)
                {
                    ref var p = ref set.Plants[i];
                    var d = math.distance(p.Position, camPos);
                    plantShown[i] = DetailPolicy.Within(d, plantRange, plantShown[i]);
                    plantFine[i] = DetailPolicy.Within(d, finestRange, plantFine[i]);
                    if (!plantShown[i])
                        continue;
                    var model = p.Species * PlantVariants + p.Variant;
                    if (model < 0 || model >= plantModels.Count)
                        continue;
                    // W-113 (item 17): no glowing plants in a bat folk chamber (the bat folk keep no lights and the
                    // pygmies are blind). Dropped here, not at bake, so the cave plants and their hashes stay as they are.
                    if (speciesGlow[p.Species].w > 0f && InBatFolkChamber(source, p.Position))
                        continue;
                    plantKeys.Add((model * 2 + (plantFine[i] ? 0 : 1), i));
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
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.AmbientGlow, position, light);
            // Wall torches: a flame on each (every frame: FireSystem's list is per frame) and its warm light on the rock.
            foreach (var (flame, plant) in nearTorches)
            {
                Punchy.Ambience.FireSystem.AddFlame(flame, 0.3f, plant, Punchy.Ambience.FireSystem.Wood);
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.Fixture, (Vector3)flame + Vector3.up * 0.15f,
                                                     Punchy.Ambience.FireSystem.Light(Punchy.Ambience.FireSystem.Wood));
            }

            var plantBox = PlantRange / (1f - DetailPolicy.Hysteresis) * 2f;
            var bounds = new Bounds(camPos, new Vector3(plantBox, plantBox, plantBox));
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
            plantsFrom = plantsGrowing = null;
            plantsAround = new float3(float.MaxValue);
        }

        /// <summary>
        /// A basin's water (a fountain's or a well's, FloraSystem): a flat polygon of the given sides and radius in the
        /// terrain's water vertex layout, corner s where an upright FloraMeshBuilder.Tube's ring vertex s lies, so it fills
        /// the basin's wall ring for ring (W-148: it was a disc). Still, one depth throughout, in the given look.
        /// </summary>
        internal static Mesh MakeBasinMesh(float radius, int sides, float depth, float4 look)
        {
            sides = math.max(sides, 3);
            var vertices = new List<WaterVertex>(sides + 1) { Vertex(float2.zero) };
            for (int k = 0; k < sides; k++)
            {
                var a = k * math.PI * 2f / sides;
                vertices.Add(Vertex(new float2(-math.sin(a), -math.cos(a)) * radius));
            }
            var indices = new List<int>(sides * 3);
            for (int k = 0; k < sides; k++)
            {
                indices.Add(0); indices.Add(1 + k); indices.Add(1 + (k + 1) % sides);
            }
            var mesh = new Mesh { name = "Basin water" };
            mesh.SetVertexBufferParams(vertices.Count, WaterVertex.Layout);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Count);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(radius * 2f, 1f, radius * 2f));
            return mesh;

            WaterVertex Vertex(float2 xz) => new WaterVertex
            {
                Position = new float3(xz.x, 0f, xz.y),
                Data = new float4(0f, 0f, depth, 0f),
                Look = look,
                Glow = float2.zero
            };
        }

        // ------------------------------------------------------------------------------------------- drips

        /// <summary>One drip source, as the drip shader reads it.</summary>
        struct Drip
        {
            public float4 Tip;    // xyz the tip, w the period (s)
            public float4 Land;   // x landing height, y phase, z ring size, w 1 on water (ring) or 0 on rock (splash)
        }

        const int MaxDrips = 48;
        /// <summary>W-135 row 3: how far a drip is drawn (DetailPolicy.CaveDripPixels, x Detail: 25 m at the reference
        /// view and Detail 1).</summary>
        static float DripRange => DetailPolicy.ExtraReach(DetailPolicy.CaveDripSize, DetailPolicy.CaveDripPixels);
        /// <summary>Per drip (index into allDrips): near at the last gather.</summary>
        bool[] dripNear = System.Array.Empty<bool>();
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
            if (dripsFrom != source.Caves)
            {
                dripsFrom = source.Caves;
                allDrips.Clear();
                dripsAround = new float3(float.MaxValue);
                // #102: finding the spots scans the rock field (~1.7 s on v2), so it runs on a worker thread; the
                // drips start once it's done. The cave blobs are persistent and read-only once baked.
                var shares = new float[styles.Count];
                for (int i = 0; i < shares.Length; i++)
                    shares[i] = styles[i] != null ? styles[i].Drips : 0f;
                var from = source;
                dripTask = System.Threading.Tasks.Task.Run(() => GatherDrips(from, shares));
            }
            if (dripTask != null)
            {
                if (!dripTask.IsCompleted)
                    return;
                if (dripTask.IsFaulted)
                    Debug.LogException(dripTask.Exception);
                else
                    allDrips.AddRange(dripTask.Result);
                dripTask = null;
            }
            if (allDrips.Count == 0)
                return;
            DrawNearDrips(camPos);
        }

        System.Threading.Tasks.Task<List<Drip>> dripTask;

        /// <summary>The drip spots: stalactite tips first, then bare ceiling in each chamber (most caves of a dripping
        /// style have few stalactites), a few spots per chamber, by each style's Drips share. Pure: runs on a worker.</summary>
        static List<Drip> GatherDrips(TerrainSource source, float[] shares)
        {
            var drips = new List<Drip>();
            ref var set = ref source.Caves.Value;
            for (int i = 0; i < set.Speleothems.Length; i++)
            {
                ref var s = ref set.Speleothems[i];
                if (s.Tip.y < s.Root.y)   // hanging (not standing, not a column)
                    AddDrip(source, s.Tip, (uint)i, shares, drips);
            }
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                var drip = cave.Style > 0 && cave.Style <= shares.Length ? shares[cave.Style - 1] : 0f;
                if (drip <= 0f)
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
                        AddDrip(source, new float3(q.x, ceiling - 0.02f, q.y), 100000u + (uint)(n * 8 + k), shares, drips);
                    }
                }
            }
            return drips;
        }

        void DrawNearDrips(float3 camPos)
        {
            if (math.distancesq(camPos, dripsAround) > 4f)
            {
                dripsAround = camPos;
                nearDrips.Clear();
                if (dripNear.Length != allDrips.Count)
                    dripNear = new bool[allDrips.Count];
                var dripRange = DripRange;
                for (int k = 0; k < allDrips.Count; k++)
                {
                    var d = allDrips[k];
                    var dist = math.distance(d.Tip.xyz, camPos);
                    dripNear[k] = DetailPolicy.Within(dist, dripRange, dripNear[k]);
                    if (dripNear[k])
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
                worldBounds = new Bounds(camPos, Vector3.one * (DripRange / (1f - DetailPolicy.Hysteresis) * 2f)),
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
        static void AddDrip(in TerrainSource source, float3 tip, uint seed, float[] shares, List<Drip> drips)
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
            var drip = set.Caves[cave].Style > 0 && set.Caves[cave].Style <= shares.Length ? shares[set.Caves[cave].Style - 1] : 0f;
            if (share >= drip)
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
            drips.Add(new Drip
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
            dripTask = null;
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
            Shader.SetGlobalVector(GlowMapParamsId, Vector4.zero);
            Shader.SetGlobalVector(GlowWorldParamsId, Vector4.zero);
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
            var windowsOn = night < 0.25f ? 0f : math.smoothstep(0.25f, 0.55f, night);
            surfaceGlow.DrawSpill(windowsOn * (1f - Underground), camPos);   // W-127: lit windows' pools outside
            // The whole island's static glow (R-2, Glow.hlsl), past the far map and fading into it.
            if (surfaceGlow.WorldReady)
            {
                Shader.SetGlobalTexture(GlowWorldId, surfaceGlow.WorldMap);
                Shader.SetGlobalVector(GlowWorldRectId, new Vector4(surfaceGlow.WorldCorner.x, surfaceGlow.WorldCorner.y, 1f / surfaceGlow.WorldExtent, 0f));
                var glowFade = DetailPolicy.FadeReach(DetailPolicy.WorldGlowSize, DetailPolicy.WorldGlowFromPixels, DetailPolicy.WorldGlowToPixels, WorldGlowEdge);
                Shader.SetGlobalVector(GlowWorldParamsId, new Vector4((1f - Underground) * WorldGlowStrength, night, glowFade.x, glowFade.y));
            }
            // The far tier (Glow.hlsl): every glowing plant and lit window within about 300 m, baked into a light map,
            // fading in where LightBudget hands a typical glow from its slots to the far tier (refactor 3.1).
            var handOff = Punchy.Settings.LightBudget.GlowHandOff(Punchy.Settings.LightKind.AmbientGlow, TypicalGlowReach);
            if (surfaceGlow.MapReady && (GlowTiers & 2) != 0)
            {
                Shader.SetGlobalTexture(GlowMapId, surfaceGlow.MapPlants);
                Shader.SetGlobalTexture(GlowMapNightId, surfaceGlow.MapWindows);
                Shader.SetGlobalVector(GlowMapRectId, new Vector4(surfaceGlow.MapCorner.x, surfaceGlow.MapCorner.y,
                                                                  1f / (SurfaceGlow.MapSize * SurfaceGlow.MapTexel), 0f));
                // (y: the night map, street lights only (W-127). Lit windows' light is indoor (P-21, item 31), so
                // SurfaceGlow leaves them out of it: see the near tier below.)
                Shader.SetGlobalVector(GlowMapParamsId, new Vector4((1f - Underground) * FarGlowStrength, windowsOn * (1f - Underground), handOff.x * 0.7f, handOff.y));
            }
            if ((GlowTiers & 1) == 0)
                return;
            // The near tier: each a request; LightBudget fades them out by on-screen size as the far map takes over.
            foreach (var (position, light) in surfaceGlow.Found)
            {
                var l = light;
                if (Punchy.Flora.FloraPlacement.IsFixtureGlow(l.w))
                {
                    // W-127: a street light (lantern, brazier, pier lamp), lit after dark like a window, but outdoors.
                    // Past the hand-off the night map lights it in full, so it asks for no slot there.
                    if (night < 0.25f || Vector3.Distance(position, camPos) > handOff.y)
                        continue;
                    l = new Vector4(l.x, l.y, l.z, 0f) * windowsOn;
                    l.w = Punchy.Flora.FloraPlacement.GlowReach(light.w);
                    Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.Fixture, position, l);
                    continue;
                }
                if (l.w < 0f)
                {
                    // Lit windows (only after dark) are the light inside a house: an indoor fixture (P-21, playtest 11-08
                    // item 31), which lights only its building's inside. It used to go out as a glow from the house's
                    // middle and lit its outer walls and the ground all round through the walls. The panes keep their
                    // own night glow (Flora shader).
                    if (night < 0.25f)
                        continue;
                    l = new Vector4(l.x, l.y, l.z, 0f) * windowsOn;
                    l.w = -light.w;
                    Punchy.Settings.LightBudget.AddIndoor(Punchy.Settings.LightKind.Fixture, position, l);
                    continue;
                }
                Punchy.Settings.LightBudget.AddStill(Punchy.Settings.LightKind.AmbientGlow, position, l);
            }
        }

        static readonly int NightId = Shader.PropertyToID("_PunchyNight");
        static readonly int GlowMapId = Shader.PropertyToID("_PunchyGlowMap");
        static readonly int GlowMapNightId = Shader.PropertyToID("_PunchyGlowMapNight");
        static readonly int GlowMapRectId = Shader.PropertyToID("_PunchyGlowMapRect");
        static readonly int GlowMapParamsId = Shader.PropertyToID("_PunchyGlowMapParams");
        static readonly int GlowWorldId = Shader.PropertyToID("_PunchyGlowWorld");
        static readonly int GlowWorldRectId = Shader.PropertyToID("_PunchyGlowWorldRect");
        static readonly int GlowWorldParamsId = Shader.PropertyToID("_PunchyGlowWorldParams");
        /// <summary>The world glow's brightness. Its plants and lava fade in over a camera distance by how big a glow looks
        /// (DetailPolicy.WorldGlowFromPixels to WorldGlowToPixels: 240 m to 320 m at the reference view), never past
        /// WorldGlowEdge, where the 640 m far map fades out towards its edge.</summary>
        public static float WorldGlowStrength = 1f;
        const float WorldGlowEdge = 320f; // streaming: the far map's coverage, not a look
        /// <summary>A typical surface glow's reach (m), for where the far map takes over (LightBudget.GlowHandOff): the
        /// far map fades in from 70% of where the near lights start fading to where they're gone, so the hand-over adds
        /// up to about the same light.</summary>
        const float TypicalGlowReach = 8f;
        /// <summary>Which surface glow tiers are on (1 near lights, 2 far map): both, bar a frame-cost measurement.</summary>
        public static int GlowTiers = 3;
        /// <summary>The far map's brightness: a little under the near lights', so a cluster reads as a soft wash from
        /// afar rather than a floodlit floor.</summary>
        public static float FarGlowStrength = 1f;
        /// <summary>The far map's last scan time (ms, start to pick-up), for the frame-cost report.</summary>
        public float FarGlowScanMs => surfaceGlow.FarScanMs;

        Punchy.Flora.FloraSystem flora;


        // The frame timer and its section marks, reused so Update allocates nothing (W-117 GC).
        readonly System.Diagnostics.Stopwatch frameClock = new System.Diagnostics.Stopwatch();
        readonly double[] frameMarks = new double[8];

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || Material == null || !TryGetWorld(out var source))
            {
                Punchy.Settings.LightBudget.Discard();
                return;
            }
            if (!source.Equals(lastSource))
            {
                Release();
                lastSource = source;
            }
            var camPos = (float3)cam.transform.position;
            Punchy.Settings.DetailPolicy.SetView(cam);
            var clock = frameClock;
            clock.Restart();
            DrawLandmarkPools(source, camPos);
            GatherSurfaceGlow(source, camPos);
            if (!source.Caves.IsCreated)
            {
                UploadGlowing(cam);
                return;
            }

            var marks = frameMarks;
            marks[0] = clock.Elapsed.TotalMilliseconds;
            UploadStyles();
            marks[1] = clock.Elapsed.TotalMilliseconds;
            Stream(source, camPos);
            marks[2] = clock.Elapsed.TotalMilliseconds;
            // Outside every cave's air, buried chunks can't be seen.
            Draw(source, camPos, cam);
            DrawPools(source, camPos);
            marks[3] = clock.Elapsed.TotalMilliseconds;
            DrawBatFolk(source, camPos);
            DrawRooms(source, camPos);
            marks[4] = clock.Elapsed.TotalMilliseconds;
            DrawPlants(source, camPos);
            marks[5] = clock.Elapsed.TotalMilliseconds;
            DrawDrips(source, camPos);
            marks[6] = clock.Elapsed.TotalMilliseconds;
            UploadGlowing(cam);
            marks[7] = clock.Elapsed.TotalMilliseconds;
            // Startup hitch hunting (#102): name the slow parts of any frame over a quarter second.
            if (marks[7] > 250)
                Debug.Log($"[caves] slow frame {marks[7]:F0} ms: pools+glow {marks[0]:F0}, styles {marks[1] - marks[0]:F0}, stream {marks[2] - marks[1]:F0}, draw+pools {marks[3] - marks[2]:F0}, bat folk {marks[4] - marks[3]:F0}, plants {marks[5] - marks[4]:F0}, drips {marks[6] - marks[5]:F0}, glowing {marks[7] - marks[6]:F0}");

            // Underground: the camera in a cave's air, a couple of metres below the surface.
            var deep = source.CaveDistance(camPos) < 0f ? math.saturate((source.SampleHeight(camPos.xz) - camPos.y - 1f) / 3f) : 0f;
            Underground = Mathf.MoveTowards(Underground, deep, Time.deltaTime * 1.5f);
            Shader.SetGlobalFloat(UndergroundId, Underground);
            // The surface is out of sight deep in a cave, away from every mouth.
            var nearestMouth = float.MaxValue;
            ref var caveSet = ref source.Caves.Value;
            for (int c = 0; c < caveSet.Caves.Length; c++)
                nearestMouth = math.min(nearestMouth, math.distance(camPos, caveSet.Caves[c].Mouth));
            var surfaceFade = DetailPolicy.FadeReach(DetailPolicy.CaveMouthSize, DetailPolicy.SurfaceShownPixels, DetailPolicy.SurfaceHiddenPixels, LoadRadius);
            SurfaceHidden = Underground * math.smoothstep(surfaceFade.x, surfaceFade.y, nearestMouth);
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
        /// W-128: 2.5 left sky showing through a hole over one mouth (the cut there needs 2.7 m at MouthCutDepth, 3.3 m
        /// at MeshCutDepth, measured over every mouth).
        /// </summary>
        const float WallReach = 3.5f;

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
                // A coarser level halves the cells across a chunk, so it needs a whole, not too small, number of them.
                maxLevel = 0;
                while (maxLevel < 2 && chunkCells % (2 << maxLevel) == 0 && chunkCells >> (maxLevel + 1) >= 4)
                    maxLevel++;
                indexSeen = new NativeHashMap<int3, ulong>(1024, Allocator.Persistent);
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
                chunkCaves[key] = indexSeen[key];
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
        NativeHashMap<int3, ulong> indexSeen;
        NativeList<int3> indexKeys;
        /// <summary>Each indexed chunk's cave bits (Built.Caves).</summary>
        readonly Dictionary<int3, ulong> chunkCaves = new Dictionary<int3, ulong>();

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
            public NativeHashMap<int3, ulong> Seen;
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
                        var bit = 1ul << (seg.Cave & 63);
                        if (Seen.TryGetValue(key, out var bits))
                            Seen[key] = bits | bit;
                        else
                        {
                            Seen.Add(key, bit);
                            Keys.Add(key);
                        }
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
            BeginFrameCounts();
            if (!IndexChunks(source))
                return;
            RefreshContacts(source, camPos);

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
                    var gone = chunks[key];
                    if (gone.Next != null)
                    {
                        dying.Add(gone.Next);
                        gone.Next = null;
                    }
                    if (gone.Pending)
                        dying.Add(gone);
                    else
                        gone.Dispose();
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
            var rebuilding = 0;
            foreach (var b in chunks.Values)
            {
                if (b.Pending)
                    building++;
                if (b.Next != null)
                {
                    building++;
                    rebuilding++;
                }
            }
            var budget = fast ? 1 : MaxBuilding;
            var started = 0;
            for (; started < BuildsPerFrame && building < budget && wanted.Count > 0; started++, building++)
            {
                var key = wanted[wanted.Count - 1];
                // Moving fast: only the near ones (the list is nearest last, so the rest wait).
                if (fast && ChunkDistance(key, camPos) > FastRadius)
                    break;
                wanted.RemoveAt(wanted.Count - 1);
                if (!chunks.ContainsKey(key))
                {
                    chunks[key] = StartBuild(source, key, WallLevel(key, camPos, -1));
                    Counts.NewBuilds++;
                }
            }
            // W-135 row 2: then, with what's left of the budget, rebuild the nearest chunks whose wall level should
            // change. An empty chunk is only rebuilt finer (a coarse grid can miss a sliver of wall a fine one finds),
            // and not even that once it's Clear (row 6).
            // W-135 row 6: at most RebuildsPerFrame a frame and MaxRebuilding at once, so walking up to a mouth they
            // don't crowd the terrain's jobs off the workers. The nearest going finer (or with a changed contact list)
            // goes first; one going coarser waits until nothing else is building. Its finer walls draw on until then:
            // a few more triangles for a while, never a pop.
            for (var rebuilds = 0; rebuilds < RebuildsPerFrame && started < BuildsPerFrame && building < budget && rebuilding < MaxRebuilding;
                 rebuilds++, started++, building++, rebuilding++)
            {
                var best = new int3(int.MinValue);
                var bestDistance = float.MaxValue;
                var bestLevel = 0;
                var coarse = new int3(int.MinValue);
                var coarseDistance = float.MaxValue;
                var coarseLevel = 0;
                foreach (var kv in chunks)
                {
                    var b = kv.Value;
                    if (b.Pending || b.Next != null)
                        continue;
                    var level = WallLevel(kv.Key, camPos, b.Level);
                    // W-135 row 4: or whose contact list changed since it was built (rebuilt at its level).
                    var stale = b.Mesh != null && b.ContactHash != ChunkContactHash(kv.Key);
                    if (!stale && (level == b.Level || b.Mesh == null && (level > b.Level || b.Clear)))
                        continue;
                    var distance = ChunkDistance(kv.Key, camPos);
                    if (!stale && level > b.Level)
                    {
                        if (distance < coarseDistance)
                        {
                            coarse = kv.Key;
                            coarseDistance = distance;
                            coarseLevel = level;
                        }
                    }
                    else if (distance < bestDistance)
                    {
                        best = kv.Key;
                        bestDistance = distance;
                        bestLevel = level;
                    }
                }
                if (bestDistance == float.MaxValue && building == 0)
                {
                    best = coarse;
                    bestDistance = coarseDistance;
                    bestLevel = coarseLevel;
                }
                if (bestDistance == float.MaxValue || fast && bestDistance > FastRadius)
                    break;
                var rebuilt = chunks[best];
                if (bestLevel != rebuilt.Level)
                {
                    Counts.LevelBuilds++;
                    if (bestLevel < rebuilt.Level) Counts.Finer++; else Counts.Coarser++;
                    if (rebuilt.Mesh == null) Counts.EmptyRebuilds++;
                }
                else
                    Counts.ContactBuilds++;
                rebuilt.Next = StartBuild(source, best, bestLevel);
            }

            var uploads = 0;
            swapped.Clear();
            foreach (var kv in chunks)
            {
                var b = kv.Value;
                if (b.Pending)
                    Finish(source, kv.Key, b, ref uploads);
                else if (b.Next != null && Finish(source, kv.Key, b.Next, ref uploads))
                    swapped.Add(kv.Key);
            }
            // A finished rebuild takes the old one's place (its hysteresis carried over).
            foreach (var key in swapped)
            {
                var old = chunks[key];
                var next = old.Next;
                old.Next = null;
                next.OpeningSeen = old.OpeningSeen;
                next.LastSeen = old.LastSeen;
                old.Dispose();
                chunks[key] = next;
                Counts.Swaps++;
            }
            Counts.Building = building;
            foreach (var b in chunks.Values)
                if (b.Next != null)
                    Counts.Rebuilding++;
            Counts.Wanted = wanted.Count;
        }

        readonly List<int3> swapped = new List<int3>();

        /// <summary>The coarsest wall level the chunk size allows (IndexChunks).</summary>
        int maxLevel;
        /// <summary>Testing (-cavelevel n): every chunk built at this wall level (-1: by screen size).</summary>
        public static int ForceLevel = -1;

        /// <summary>
        /// W-135 row 2: the wall level a chunk should have, from how big a metre looks at its nearest point
        /// (DetailPolicy.CaveWall, held by Hysteresis from the level it has; -1 when it has none yet).
        /// </summary>
        int WallLevel(int3 key, float3 camPos, int previous)
        {
            if (ForceLevel >= 0)
                return math.min(ForceLevel, maxLevel);
            var view = DetailPolicy.Current.Data;
            var ppm = view.PixelsPerMetre > 0f ? view.PixelsPerMetre : DetailPolicy.PixelsPerMetreAt(DetailPolicy.ReferenceFov);
            var pixels = DetailPolicy.ScreenSize(DetailPolicy.CaveWallSize, ChunkDistance(key, camPos), ppm);
            var tiers = view.Detail > 0f ? DetailPolicy.CaveWall.Scaled(view.Detail) : DetailPolicy.CaveWall;
            return math.min(DetailPolicy.Tier(pixels, tiers, previous), maxLevel);
        }

        /// <summary>
        /// Turns a finished build into its mesh (on the main thread, at most UploadsPerFrame with walls a frame) and lets
        /// its build data go. False while it's still building or waiting its turn.
        /// </summary>
        bool Finish(in TerrainSource source, int3 key, Built b, ref int uploads)
        {
            if (!b.Pending || !b.Job.IsCompleted)
                return false;
            b.Job.Complete();   // already done: cheap
            // Empty ones cost nothing to finish; those with a mesh wait their turn.
            if (b.Indices.Length > 0 && uploads >= UploadsPerFrame)
                return false;
            b.Pending = false;
            // W-135 row 6: walls only come where a cell corner is within WallReach of the cave, and every point a finer
            // grid samples is within a coarse voxel's half-diagonal (0.87 voxel) of one this grid sampled. With the
            // least sampled distance two voxels past WallReach, a finer grid can't find a wall here either. (A point
            // no segment box holds reads as far: those boxes reach Blend + 4 m past the walls, more than a level-2
            // voxel's half-diagonal past any point within WallReach, so the nearest sample to such a point is held.)
            b.Clear = b.Indices.Length == 0 && b.Gap[0] >= WallReach + 2f * LevelVoxel(b.Level);
            // Chunks the cave only grazes come out empty: keep the record (so they aren't rebuilt), not a mesh.
            if (b.Indices.Length > 0)
            {
                uploads++;
                Counts.Uploads++;
                Counts.UploadVertices += b.Positions.Length;
                var mesh = new Mesh { name = "Cave chunk", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(b.Positions.AsArray());
                mesh.SetNormals(b.Normals.AsArray());
                mesh.SetColors(b.Colors.AsArray());
                if (ShardProbe)
                    LogShards(b);
                mesh.SetUVs(0, b.Data.AsArray());
                mesh.SetIndices(b.Indices.AsArray(), MeshTopology.Triangles, 0);
                var min = (float3)key * chunkSize;
                // Room for the apron and the skirts (each within two of its voxels outside the chunk).
                mesh.bounds = new Bounds(min + chunkSize * 0.5f, new float3(chunkSize + LevelVoxel(b.Level) * 4f));
                // Drawn only: let the CPU copy go.
                mesh.UploadMeshData(true);
                b.Mesh = mesh;
                b.Triangles = b.Indices.Length / 3;
                b.Caves = chunkCaves.TryGetValue(key, out var caveBits) ? caveBits : ~0ul;
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
                b.Opens = math.all(b.Opening[0] <= b.Opening[1]);
                if (b.Opens)
                    b.OpeningBounds = new Bounds((b.Opening[0] + b.Opening[1]) * 0.5f, b.Opening[1] - b.Opening[0]);
                b.OpeningSeen = false;
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
            b.Opening.Dispose();
            b.Gap.Dispose();
            b.Contacts.Dispose();
            return true;
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

        /// <summary>The voxel of a wall level: Voxel doubled per level.</summary>
        float LevelVoxel(int level) => chunkVoxel * (1 << level);

        /// <summary>
        /// W-135 row 2: a chunk at a coarser level is built on a grid of LevelVoxel(level) (half the cells across per
        /// level), so it costs a quarter of the triangles per level. Each chunk hangs a skirt into the rock from its
        /// edge (MeshJob), which covers the crack where it meets a neighbour at another level.
        /// </summary>
        const float SkirtVoxels = 1.5f;

        Built StartBuild(in TerrainSource source, int3 key, int level)
        {
            // Points run from one voxel before the chunk to one after its far side; cells from -1 to its cells. The
            // origin is a global grid index, so neighbouring chunks sample their shared apron at bit-identical points
            // (a float origin plus an offset rounds differently in each, and a value near zero can flip sign).
            var levelCells = chunkCells >> level;
            var voxel = LevelVoxel(level);
            var origin = key * levelCells - 1;
            var cells = new int3(levelCells + 2);
            var points = cells + 1;
            Counts.BuildPoints += points.x * points.y * points.z;
            var b = new Built
            {
                Level = level,
                Field = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                CaveField = new NativeArray<float>(points.x * points.y * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Surface = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                SurfaceScale = new NativeArray<float>(points.x * points.z, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                Data = new NativeList<float4>(Allocator.Persistent),
                Positions = new NativeList<float3>(Allocator.Persistent),
                Normals = new NativeList<float3>(Allocator.Persistent),
                Colors = new NativeList<float4>(Allocator.Persistent),
                Indices = new NativeList<int>(Allocator.Persistent),
                Opening = new NativeArray<float3>(2, Allocator.Persistent),
                Gap = new NativeArray<float>(1, Allocator.Persistent),
                Contacts = ChunkContacts(key),
                ContactHash = ChunkContactHash(key),
                Pending = true
            };
            // W-128: the skin follows the ground as the near terrain draws it (its LOD0 grid), so it meets the terrain's edge.
            var gridStep = configQuery.IsEmpty ? 0f : configQuery.GetSingleton<TerrainConfig>().ChunkSize / configQuery.GetSingleton<TerrainConfig>().Lod0Quads;
            var columnJob = new ColumnJob { Source = source, Origin = origin, Points = points, Voxel = voxel, GridStep = gridStep, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.x * points.z, 64);
            var field = new FieldJob { Source = source, Origin = origin, Points = points, Voxel = voxel, Field = b.Field, CaveField = b.CaveField, Surface = b.Surface, SurfaceScale = b.SurfaceScale }
                .Schedule(points.y * points.z, 4, columnJob);
            b.Job = new MeshJob
            {
                Source = source, Origin = origin, Cells = cells, Voxel = voxel, Skirt = voxel * SkirtVoxels,
                Field = b.Field, CaveField = b.CaveField, ShardProbe = ShardProbe,
                Positions = b.Positions, Normals = b.Normals, Colors = b.Colors, Data = b.Data, Indices = b.Indices,
                Opening = b.Opening, Gap = b.Gap, Contacts = b.Contacts,
                ContactStrength = ContactShade.Strength, ContactReach = ContactShade.ReachScale
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
            /// <summary>The near terrain mesh's grid step (W-128): the surface is the drawn one, not the exact height
            /// between its corners, so the skin round a mouth stays under the drawn ground. 0: the exact height.</summary>
            public float GridStep;
            [WriteOnly] public NativeArray<float> Surface, SurfaceScale;

            public void Execute(int i)
            {
                var xz = (float2)(Origin.xz + new int2(i % Points.x, i / Points.x)) * Voxel;
                var h = GridStep > 0f ? Source.MeshHeight(xz, GridStep) : Source.SampleHeight(xz);
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
                    Field[i] = -CaveSampling.SmoothMin(-((p.y - Surface[column]) * SurfaceScale[column] + CaveSampling.SkinGap), cave, CaveSampling.MouthBlend);
                }
            }
        }

        static readonly bool ShardProbe = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-caveshards") >= 0;

        /// <summary>"-caveshards": logs where a chunk has ownerless (magenta) walls, so a shot can frame them.</summary>
        static void LogShards(Built b)
        {
            var count = 0;
            var sum = float3.zero;
            var lo = new float3(float.MaxValue);
            var hi = new float3(float.MinValue);
            for (int i = 0; i < b.Colors.Length; i++)
            {
                var c = b.Colors[i];
                if (c.x != 1f || c.y != 0f || c.z != 1f)
                    continue;
                var p = b.Positions[i];
                count++;
                sum += p;
                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            if (count > 0)
                Debug.Log($"CaveSystem shards: {count} vertices round ({sum.x / count:F1}, {sum.y / count:F1}, {sum.z / count:F1}), " +
                          $"box ({lo.x:F0},{lo.y:F0},{lo.z:F0})-({hi.x:F0},{hi.y:F0},{hi.z:F0})");
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
            /// <summary>"-caveshards": walls no cave owns drawn magenta (the grey shards at cave mouths, style 0).</summary>
            public bool ShardProbe;
            /// <summary>W-135: the box round the walls open to the sky ([0] min, [1] max).</summary>
            public NativeArray<float3> Opening;
            /// <summary>W-135 row 2: how far the skirt round the chunk's edge reaches into the rock (0: none).</summary>
            public float Skirt;
            /// <summary>W-135 row 4: the things standing on this chunk's rock, its walls darkened round them.</summary>
            [ReadOnly] public NativeArray<ContactShade.Contact> Contacts;
            /// <summary>W-135 row 6: [0] the least CaveField value (Built.Gap).</summary>
            public NativeArray<float> Gap;
            /// <summary>W-136 row 3: the shared per-kind tunables (ContactShade.Strength, ReachScale) when scheduled.</summary>
            public float4 ContactStrength, ContactReach;

            /// <summary>
            /// W-135 row 4: the seam darkening at a wall vertex (ContactShade: darkest at a contact, back to 1 within its
            /// reach). A coarse level's vertices sit up to about half a voxel off the true wall, so the contact is grown
            /// by that much, and its reach is at least half a voxel so the darkening still spans a cell; a seam much
            /// narrower than that is faded in proportion rather than smeared over a whole cell (ContactShade.Contact.ShadeAt).
            /// </summary>
            float SeamShade(float3 p)
            {
                var pad = math.max(0f, Voxel - 0.5f) * 0.5f;
                var minReach = Voxel * 0.5f;
                var shade = 1f;
                for (int i = 0; i < Contacts.Length; i++)
                {
                    var c = Contacts[i];
                    var far = c.Extent + pad + minReach;
                    if (math.distancesq(p, c.Base) > far * far)
                        continue;
                    shade = math.min(shade, c.ShadeAt(p, minReach, pad, ContactStrength, ContactReach));
                }
                return shade;
            }

            int Point(int x, int y, int z) => (z * (Cells.y + 1) + y) * (Cells.x + 1) + x;
            int Cell(int x, int y, int z) => (z * Cells.y + y) * Cells.x + x;

            public void Execute()
            {
                var gap = float.MaxValue;
                for (int i = 0; i < CaveField.Length; i++)
                    gap = math.min(gap, CaveField[i]);
                Gap[0] = gap;
                var cellVertex = new NativeArray<int>(Cells.x * Cells.y * Cells.z, Allocator.Temp);
                for (int i = 0; i < cellVertex.Length; i++)
                    cellVertex[i] = -1;
                // Which vertices lie in the outermost cells the quads use (0 and Cells - 2 on any axis): the chunk's edge.
                var edge = new NativeList<bool>(Allocator.Temp);
                Opening[0] = new float3(float.MaxValue);
                Opening[1] = new float3(float.MinValue);

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
                    // Room corners kept square, so the destination chambers' tiles cover the rock (W-115).
                    p = CaveSampling.OutOfRooms(ref Source.Caves.Value, p);
                    cellVertex[Cell(x, y, z)] = Positions.Length;
                    edge.Add(x == 0 || y == 0 || z == 0 || x >= Cells.x - 2 || y >= Cells.y - 2 || z >= Cells.z - 2);
                    Positions.Add(p);
                    Normals.Add(Gradient(p));
                    var color = Look(p, out var data, out var open);
                    // W-135 row 4: darkened round the things standing here (through the rib shade; openness left alone).
                    if (Contacts.Length > 0 && !ShardProbe)
                        data.w = SeamRib(data.w, SeamShade(p));
                    Colors.Add(color);
                    Data.Add(data);
                    // Walls the sky can see (the terrain cut away above them, generously): only through these can
                    // anyone outside see into the cave.
                    if (open > 0f)
                    {
                        Opening[0] = math.min(Opening[0], p);
                        Opening[1] = math.max(Opening[1], p);
                    }
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
                if (Skirt > 0f)
                    Skirts(edge);
                edge.Dispose();
            }

            /// <summary>
            /// W-135 row 2: a strip hung from every open edge on the chunk's border, straight back into the rock (along
            /// the wall's inward normal) by Skirt. Where the neighbour is built at the same level its walls hide the strip;
            /// where it's at another level the two edges don't meet, and the strip fills the crack between them with the
            /// same rock (it takes its edge vertex's colour and normal).
            /// </summary>
            void Skirts(NativeList<bool> edge)
            {
                var triangles = Indices.Length;
                var halfEdges = new NativeHashSet<int2>(triangles, Allocator.Temp);
                for (int t = 0; t < triangles; t += 3)
                for (int k = 0; k < 3; k++)
                    halfEdges.Add(new int2(Indices[t + k], Indices[t + (k + 1) % 3]));
                var skirtOf = new NativeArray<int>(Positions.Length, Allocator.Temp);
                for (int i = 0; i < skirtOf.Length; i++)
                    skirtOf[i] = -1;
                for (int t = 0; t < triangles; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = Indices[t + k], b = Indices[t + (k + 1) % 3];
                    // Open: no triangle runs back along it. Only on the border (a cell left out deep in the rock leaves
                    // a hole no one sees).
                    if (!edge[a] || !edge[b] || halfEdges.Contains(new int2(b, a)))
                        continue;
                    int a2 = SkirtVertex(skirtOf, a), b2 = SkirtVertex(skirtOf, b);
                    Indices.Add(b); Indices.Add(a); Indices.Add(a2);
                    Indices.Add(b); Indices.Add(a2); Indices.Add(b2);
                }
                skirtOf.Dispose();
                halfEdges.Dispose();
            }

            int SkirtVertex(NativeArray<int> skirtOf, int v)
            {
                if (skirtOf[v] < 0)
                {
                    skirtOf[v] = Positions.Length;
                    Positions.Add(Positions[v] - Normals[v] * Skirt);
                    Normals.Add(Normals[v]);
                    Colors.Add(Colors[v]);
                    Data.Add(Data[v]);
                }
                return skirtOf[v];
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

            float RockAt(float3 p) => math.max(p.y - Source.SampleHeight(p.xz) + CaveSampling.SkinGap, -CaveSampling.Distance(ref Source.Caves.Value, p, out _));

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
            /// <summary>A road tunnel's floor (its road's level) under p: along the nearest of its passages.</summary>
            static float TunnelFloor(ref CaveSetBlob set, in CaveData c, float3 p)
            {
                var best = 1e9f;
                var floor = p.y;
                for (int s = c.FirstSegment; s < c.FirstSegment + c.SegmentCount; s++)
                {
                    var a = set.Nodes[set.Segments[s].A];
                    var b = set.Nodes[set.Segments[s].B];
                    var ab = b.Position.xz - a.Position.xz;
                    var t = math.saturate(math.dot(p.xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                    var d = math.distancesq(p.xz, a.Position.xz + ab * t);
                    if (d < best)
                    {
                        best = d;
                        floor = math.lerp(a.Position.y, b.Position.y, t) - math.lerp(a.Radius, b.Radius, t) * c.Profile.y * 0.45f;
                    }
                }
                return floor;
            }

            float4 Look(float3 p, out float4 data, out float open)
            {
                var surface = Source.SampleHeight(p.xz);
                // Daylight from the mouth of whichever cave this wall belongs to.
                ref var set = ref Source.Caves.Value;
                CaveSampling.Distance(ref set, p, out var cave);
                var fromMouth = cave >= 0 ? math.saturate(1f - (math.distance(p, set.Caves[cave].Mouth) - 3f) / 14f) : 0f;
                // A road tunnel (W-110) has a portal at each end: daylight comes in at both.
                if (cave >= 0 && set.Caves[cave].Tunnel != 0)
                {
                    var far = set.Nodes[set.Caves[cave].FirstNode + set.Caves[cave].NodeCount - 1].Position;
                    fromMouth = math.max(fromMouth, math.saturate(1f - (math.distance(p, far) - 3f) / 14f));
                }
                var nearSurface = math.saturate(1f - (surface - p.y - 1f) / 3f);
                // Open to the sky (the terrain is cut away above: a trench or crater at a mouth): it's outside ground,
                // so it takes the land's look and daylight however far below the uncut surface height it lies. Generous
                // (up to 2 m of clearance): the terrain's cut is interpolated between its vertices, and wherever the
                // terrain stays it hides this skin anyway.
                open = math.saturate((2f - Source.CaveClearance(p.xz, surface)) / 1.5f);
                var ground = Source.SampleGround(p.xz);
                var albedo = Source.SampleSurface(p.xz, ground, Source.SampleNormal(p.xz, 1f)).Albedo;
                // A road tunnel's floor carries its road through (W-110): the road's own surface, painted as outside,
                // and taken as the terrain look so it reads as the same road from portal to portal.
                var road = 0f;
                if (cave >= 0 && set.Caves[cave].Tunnel != 0 && p.y < TunnelFloor(ref set, set.Caves[cave], p) + 0.35f)
                {
                    var props = new SurfaceProps { Albedo = albedo };
                    road = Source.TunnelRoadSurface(p.xz, ref props);
                    albedo = props.Albedo;
                }
                // Ribbed caves: how much darker the hollow between two ribs is (crests a touch lighter), applied to
                // every look the wall takes, so the ribs read even in the flat light of a shallow ice cave; none where
                // the cave opens to the sky, so mouths still match the terrain.
                var ribShade = 0f;
                if (cave >= 0 && set.Caves[cave].Wall.z > 0f)
                {
                    var rib = CaveSampling.Rib(set.Caves[cave], p);
                    ribShade = (0.55f - 0.75f * rib * rib) * set.Caves[cave].Wall.z * (1f - open);
                }
                data = new float4(math.max(math.max(math.saturate(1f - (surface - p.y - 0.3f) / 2.5f), open), road),
                                  0.85f + 0.3f * (noise.snoise(p * 0.15f) * 0.5f + 0.5f), cave >= 0 ? set.Caves[cave].Style : 0, ribShade);
                if (ShardProbe && cave < 0)
                {
                    data.x = 1f;
                    return new float4(1f, 0f, 1f, 1f);
                }
                return new float4(albedo, math.max(math.max(fromMouth, nearSurface), open));
            }
        }

        /// <summary>Testing (-oldcave): draw every loaded chunk as before the room cull.</summary>
        public static bool NoRoomCull;
        /// <summary>Testing (-nocavewalls): draw no cave walls at all, to price them.</summary>
        public static bool NoWalls;
        /// <summary>Testing (-perflog): count chunks each frame (the frustum test only runs when set).</summary>
        public static bool CountStats;
        /// <summary>Testing (-nocaveskip): from the surface, draw every unburied chunk as before W-135 row 1.</summary>
        public static bool NoSurfaceSkip;
        public static int StatLoaded, StatSubmitted, StatInView, StatShadows, StatTriangles;
        Plane[] statPlanes = new Plane[6];
        Plane[] viewPlanes = new Plane[6];
        readonly List<Bounds> openingsInView = new List<Bounds>();
        readonly List<float4> openingRects = new List<float4>();
        readonly Vector3[] corners = new Vector3[8];

        /// <summary>
        /// W-135 row 1: from the surface a cave's walls show only through its openings (the vertices open to the sky,
        /// where the terrain is cut away). This lists the openings in view, each grown by BuriedReach: a chunk outside
        /// all of them is hidden by the ground. The frustum test is padded (more for an opening seen last frame) so a
        /// turn of the camera after this frame's Update never shows a missing wall, and an opening counts only while it
        /// looks bigger than DetailPolicy.CaveOpeningPixels (held by Hysteresis), so nothing pops as it comes in.
        /// Each opening also keeps the part of the screen it covers (padded): a chunk near one but outside all those
        /// parts can't be seen through any, so it only casts its shadow (the light through a sinkhole stays the same).
        /// </summary>
        void GatherOpenings(float3 camPos, Camera cam)
        {
            openingsInView.Clear();
            openingRects.Clear();
            GeometryUtility.CalculateFrustumPlanes(cam, viewPlanes);
            var ppm = DetailPolicy.Current.Data.PixelsPerMetre > 0f ? DetailPolicy.Current.Data.PixelsPerMetre
                : DetailPolicy.PixelsPerMetreAt(cam.fieldOfView);
            foreach (var b in chunks.Values)
            {
                if (b.Mesh == null || !b.Opens)
                {
                    if (b.Mesh != null) b.OpeningSeen = false;
                    continue;
                }
                var box = b.OpeningBounds;
                var distance = math.distance((float3)box.ClosestPoint(camPos), camPos);
                var size = math.cmax((float3)box.size);
                var seen = DetailPolicy.Holds(DetailPolicy.ScreenSize(math.max(size, 1f), distance, ppm), DetailPolicy.CaveOpeningPixels, b.OpeningSeen);
                if (seen)
                {
                    var padded = box;
                    padded.Expand(2f * (4f + distance * (b.OpeningSeen ? 0.35f : 0.25f)));
                    seen = GeometryUtility.TestPlanesAABB(viewPlanes, padded);
                }
                b.OpeningSeen = seen;
                if (seen)
                {
                    var reach = box;
                    reach.Expand(2f * BuriedReach);
                    openingsInView.Add(reach);
                    var rect = ScreenRect(cam, box);
                    openingRects.Add(rect + new float4(-OpeningRectPad, -OpeningRectPad, OpeningRectPad, OpeningRectPad));
                }
            }
        }

        /// <summary>How far (in viewport units) an opening's part of the screen is padded: what parallax can move a wall
        /// behind it in the frame between this test and the render. Rotation moves both alike.</summary>
        const float OpeningRectPad = 0.08f;

        /// <summary>The viewport rectangle (min x, min y, max x, max y) a box covers; the whole plane if it reaches
        /// behind the camera's near plane.</summary>
        float4 ScreenRect(Camera cam, Bounds box)
        {
            var min = box.min;
            var max = box.max;
            for (int i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) != 0 ? max.x : min.x, (i & 2) != 0 ? max.y : min.y, (i & 4) != 0 ? max.z : min.z);
            var rect = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var v = cam.WorldToViewportPoint(corners[i]);
                if (v.z <= cam.nearClipPlane)
                    return new float4(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue);
                rect = new float4(math.min(rect.xy, new float2(v.x, v.y)), math.max(rect.zw, new float2(v.x, v.y)));
            }
            return rect;
        }

        /// <summary>0: no opening in view reaches the chunk (hidden); 1: one reaches it but it's outside every
        /// opening's part of the screen (shadow only); 2: seen through one.</summary>
        int SeenThroughOpening(Bounds chunk, Camera cam)
        {
            var near = false;
            for (int i = 0; i < openingsInView.Count; i++)
                if (openingsInView[i].Intersects(chunk))
                {
                    near = true;
                    break;
                }
            if (!near)
                return 0;
            var rect = ScreenRect(cam, chunk);
            for (int i = 0; i < openingRects.Count; i++)
            {
                var o = openingRects[i];
                if (rect.x <= o.z && rect.z >= o.x && rect.y <= o.w && rect.w >= o.y)
                    return 2;
            }
            return 1;
        }

        /// <summary>
        /// Draws the loaded chunks. Deep in a cave, out of sight of every mouth (SurfaceHidden), only chunks that
        /// belong to the caves around the camera are drawn: rock hides every other cave, and with only a frustum
        /// cull they were all drawn (a 400 m load radius holds many caves). Shapes are unchanged.
        /// </summary>
        void Draw(TerrainSource source, float3 camPos, Camera cam)
        {
            var outside = source.CaveDistance(camPos) >= 0f;
            // The caves around the camera: those whose air box holds it, then any whose box touches one of those.
            var rooms = ~0ul;
            if (!outside && !NoRoomCull && SurfaceHidden >= 0.99f)
            {
                ref var set = ref source.Caves.Value;
                rooms = 0ul;
                var near = new List<int>();
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    var cv = set.Caves[c];
                    if (math.all(camPos >= cv.Min - 4f) && math.all(camPos <= cv.Max + 4f))
                        near.Add(c);
                }
                var count = near.Count;
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    var cv = set.Caves[c];
                    for (int i = 0; i < count; i++)
                    {
                        var n = set.Caves[near[i]];
                        if (math.all(cv.Min <= n.Max + 4f) && math.all(cv.Max >= n.Min - 4f))
                        {
                            rooms |= 1ul << (c & 63);
                            break;
                        }
                    }
                }
                if (count == 0)
                    rooms = ~0ul;
            }
            if (CountStats)
            {
                StatLoaded = StatSubmitted = StatInView = StatShadows = StatTriangles = 0;
                GeometryUtility.CalculateFrustumPlanes(cam, statPlanes);
            }
            var skip = outside && !NoSurfaceSkip && !NoRoomCull && cam != null;
            if (skip)
                GatherOpenings(camPos, cam);
            foreach (var b in chunks.Values)
            {
                if (b.Mesh == null)
                    continue;
                if (CountStats)
                    StatLoaded++;
                if (NoWalls || outside && b.Buried || (b.Caves & rooms) == 0ul)
                    continue;
                // From the surface, a chunk no opening in view reaches is behind the ground, and one that no opening
                // shows only casts its shadow (W-135 row 1).
                var seen = skip ? SeenThroughOpening(b.Mesh.bounds, cam) : 2;
                if (b.LastSeen >= 0 && b.LastSeen != seen)
                    Counts.SkipFlips++;
                b.LastSeen = seen;
                if (seen == 0 || seen == 1 && !b.CastsShadows)
                    continue;
                if (CountStats)
                {
                    StatSubmitted++;
                    if (seen == 2 && GeometryUtility.TestPlanesAABB(statPlanes, b.Mesh.bounds))
                    {
                        StatInView++;
                        StatTriangles += b.Triangles;
                        if (b.CastsShadows)
                            StatShadows++;
                    }
                }
                var rp = new RenderParams(Material)
                {
                    worldBounds = b.Mesh.bounds,
                    shadowCastingMode = seen == 1 ? ShadowCastingMode.ShadowsOnly : b.CastsShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
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
            PlaceLantern(lantern, player, lanternLevel, 0.5f, LocalLanternId);
            // The flora shader has no light loop (it would cost every plant on the surface), so cave plants get the
            // lantern as one cheap term from these.
            var at = lantern.transform.position;
            Shader.SetGlobalVector(LanternId, new Vector4(at.x, at.y, at.z, lantern.enabled ? lantern.intensity * 0.2f : 0f));
            Shader.SetGlobalVector(LanternColorId, new Vector4(LanternColor.r, LanternColor.g, LanternColor.b, LanternRange));
        }

        static readonly int LanternId = Shader.PropertyToID("_PunchyLantern");
        /// <summary>The local player's lantern's id in LightBudget (other players' are their entity's index).</summary>
        const int LocalLanternId = -1;
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

        /// <summary>
        /// Places someone's lantern and asks LightBudget for its real light (refactor 3.1): it's lit as far as the budget
        /// grants (fading), and when it loses the budget the same light goes in a glow slot instead (about as bright,
        /// a little shorter: the glow's fall-off is squared).
        /// </summary>
        void PlaceLantern(Light light, in Unity.Transforms.LocalTransform holder, float level, float flickerSeed, int id)
        {
            light.color = LanternColor;
            light.range = LanternRange;
            var flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 3f, flickerSeed);
            var forward = math.mul(holder.Rotation, new float3(0f, 0f, 1f));
            var at = holder.Position + new float3(0f, 1.6f, 0f) + forward * 0.4f;
            light.transform.position = at;
            if (level > 0.01f)
            {
                var glow = (Vector4)LanternColor * (LanternIntensity * 0.27f * level * flicker);
                glow.w = LanternRange * 0.7f;
                Punchy.Settings.LightBudget.AddReal(Punchy.Settings.LightKind.Carried, id, at, glow);
            }
            var real = level * Punchy.Settings.LightBudget.RealLevel(Punchy.Settings.LightKind.Carried, id);
            light.enabled = real > 0.01f;
            light.intensity = LanternIntensity * real * flicker;
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
                // (No range of its own: LightBudget caps how many are real lights; the rest glow or go out.)
                if (math.lengthsq(t.Position) < 1e-6f)
                    continue;
                if (!otherLanterns.TryGetValue(e, out var l))
                    otherLanterns[e] = l = new OtherLantern { Light = CreateLantern("Lantern (other player)") };
                l.Seen = true;
                l.Level = Mathf.MoveTowards(l.Level, LanternWanted(source, t.Position), Time.deltaTime * 1.2f);
                PlaceLantern(l.Light, t, l.Level, 1.5f + (e.Index % 7), e.Index);
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
