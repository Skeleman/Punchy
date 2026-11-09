using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Settings;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-5: one wide cached sun-shadow map for the huge things. Real shadows stop at the pipeline's shadow distance
    /// and the flora cache at 120 m, so from afar the colossi, the salt tower and the sunken temple stood shadowless,
    /// patched by one ground strip each (the old FarShadow, retired by R-5 row 2). This lays one orthographic sun view about Reach round the player (3.2 km
    /// across by default), texel-snapped, into a 2048x2048 R16 texture that keeps, per texel, how far along the
    /// sunlight the nearest caster is.
    /// Which things cast is the DetailPolicy size-on-screen rule, never a list of types: every shadow-casting mesh in
    /// the loaded scenes and every mesh a landmark offers (<see cref="Cast"/>) is given its shadow tier by
    /// DetailPolicy.Huge, and those in tiers 0-2 (still WideShadow reference pixels big, with the usual hysteresis)
    /// cast; the flora picks its big trees and built things the same way on the GPU (FloraSystem.Wide.cs). A coarse
    /// terrain heightfield (25 m cells, lowered a little so the ground never shades itself) lets mountains shade
    /// valleys at a low sun too.
    /// The map is redrawn only when the sun turns RedrawDegrees, the player moves RedrawMove, a caster comes or goes,
    /// or the flora re-streams (at most every FloraWait seconds); a redraw is spread over RedrawFrames frames into a
    /// second texture, swapped in when done. A caster that moves (a walking colossus) only has the part of the map it
    /// left and the part it entered wiped and drawn again, under a scissor rect, in one frame. Every frame the map is
    /// handed to the terrain and flora shaders as globals (Shaders/WideShadow.hlsl), which take the direct sunlight
    /// from what a cached caster stands between and the sun, past the real-time shadows. Cosmetic and local: each
    /// client lays its own. "-wideshadow 0" turns it off; "-wideshadowdark" sets how dark it is.
    /// </summary>
    public class WideShadow : MonoBehaviour
    {
        /// <summary>On by default; "-wideshadow 0" (command line or a shot line) turns it off. Read every frame.</summary>
        public static bool Enabled = Switch("-wideshadow", true);
        /// <summary>How much of the direct sunlight a wide shadow takes (0-1, times the sun's shadow strength), as a real shadow's 1.</summary>
        public static float Darkness = Number("-wideshadowdark", 1f);
        /// <summary>Whether the flora's big trees and built things cast into it too ("-wideshadowflora 0" leaves them out).</summary>
        public static bool Flora = Switch("-wideshadowflora", true);
        /// <summary>Half the map's width (m, "-widereach"): it covers 2 x Reach round the player.</summary>
        public static float Reach = Number("-widereach", 1600f);
        /// <summary>Redrawn when the sun has turned this far (degrees) or the player moved this far (m).</summary>
        public static float RedrawDegrees = 1f, RedrawMove = 300f;
        /// <summary>A flora re-stream asks for a redraw at most this often (s).</summary>
        public static float FloraWait = 5f;
        public const int Size = 2048, RedrawFrames = 4;

        /// <summary>GPU memory the map holds (both textures and the flora's wide cull), in bytes.</summary>
        public static long Bytes;
        /// <summary>Redraws and dirty rects finished this session.</summary>
        public static int Redraws, DirtyRects;
        /// <summary>Last finished redraw: casters, CPU and GPU time, centre, sun.</summary>
        public static string LastLog = "";
        /// <summary>Last dirty rect: its size in texels and its CPU and GPU time.</summary>
        public static string LastDirtyLog = "";
        /// <summary>Debug: the next redraws count their casters by kind into <see cref="KindsLog"/> (the flora's count is a GPU sync).</summary>
        public static bool LogKinds;
        /// <summary>The casters of the last redraw started with LogKinds set, by kind (the root's name; the flora by its DetailPolicy class).</summary>
        public static string KindsLog = "";

        // Terrain heightfield: Cells x Cells quads of Cell metres round the player, lowered by Drop below the lowest of
        // each point's 3x3 neighbourhood (between grid points the real ground can sag under a straight line).
        const float Cell = 25f, Drop = 1.5f;
        // The scene's renderers are looked through again every RescanFrames frames. A caster in the map that moved
        // MoveMetres or turned MoveDegrees since it was drawn has its dirty rect redrawn (a walker keeps its shadow).
        const int RescanFrames = 90;
        const float MoveMetres = 10f, MoveDegrees = 5f;

        static readonly int VPId = Shader.PropertyToID("_WideShadowVP");
        static readonly int EyeId = Shader.PropertyToID("_WideShadowEye");
        static readonly int SunId = Shader.PropertyToID("_WideShadowSun");
        static readonly int FrontEyeId = Shader.PropertyToID("_WideShadowFrontEye");
        static readonly int FrontSunId = Shader.PropertyToID("_WideShadowFrontSun");
        static readonly int TexId = Shader.PropertyToID("_WideShadowTex");
        static readonly int MatrixId = Shader.PropertyToID("_WideShadowMatrix");
        static readonly int ParamsId = Shader.PropertyToID("_WideShadowParams");
        static readonly int FadeId = Shader.PropertyToID("_WideShadowFade");

        /// <summary>A mesh a landmark offers every frame it draws, with its tier from the last redraw (-1: none yet).</summary>
        struct Caster
        {
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public int Seen, Tier;
            public string Kind;
        }

        /// <summary>One mesh a redraw draws; Source is the renderer it came from (null for an offered mesh).</summary>
        struct Draw
        {
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public Renderer Source;
            public Vector3 At;
            public Quaternion Turn;
            public Bounds Box;
        }

        // Landmark casters, registered every frame by their systems; dropped after a few frames unseen.
        static Caster[] casters = new Caster[8];
        static int casterCount;
        static bool castersChanged;
        int floraHeld;
        static WideShadow instance;

        RenderTexture front, back;
        Material caster;
        CommandBuffer commands;
        CustomSampler sampler, dirtySampler;
        Recorder recorder, dirtyRecorder;
        bool ready;
        int step = -1;
        Matrix4x4 backVP, frontVP, backUv, frontUv;
        Vector3 backSun, frontSun, laidSun, backEye, frontEye;
        float backInvRange, frontInvRange;
        float3 laidAt;
        float laidTime;

        // What a redraw draws, snapshotted when it starts (the slices then take a few frames; the dirty rects keep the
        // renderers' entries up to date afterwards).
        readonly List<Draw> draws = new List<Draw>(64);
        // The scene's renderers that could ever pass the rule (big enough at the real shadows' end), from the last scan.
        List<Renderer> candidates = new List<Renderer>(64), scanned = new List<Renderer>(64);
        readonly List<GameObject> rootObjects = new List<GameObject>(256);
        readonly List<Renderer> found = new List<Renderer>(256);
        // Each renderer's tier at the last redraw (the rule's hysteresis), swapped with the next redraw's.
        Dictionary<Renderer, int> tiers = new Dictionary<Renderer, int>(64), nextTiers = new Dictionary<Renderer, int>(64);
        // A kind per root (its name, read once), and the counts LogKinds gathers.
        readonly Dictionary<Transform, string> kinds = new Dictionary<Transform, string>(64);
        readonly Dictionary<string, int> kindCounts = new Dictionary<string, int>(16);
        int nextScan;
        bool candidatesChanged;
        FloraSystem flora;
        int laidFloraVersion = -1;

        // Terrain.
        Unity.Entities.World terrainWorld;
        EntityQuery terrainQuery;
        BlobAssetReference<LandmarkSetBlob> fieldLandmarks;
        Mesh field;
        NativeArray<float> heights;
        NativeArray<float3> points;
        JobHandle fieldJob;
        bool fieldPending, fieldReady;
        float2 fieldAt;
        float fieldMin, fieldMax;
        int fieldN;

        // Timing of the redraw under way (CPU per slice, GPU read back a frame or more late), and of the last dirty rect.
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        double cpuSum, cpuMax, gpuSum, gpuMax, dirtyCpu, dirtyGpu;
        int gpuFrames, dirtyFrames, logs, dirtyLogs;
        RectInt dirtyRect;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null)
                return;
            var go = new GameObject("WideShadow");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WideShadow>();
        }

        /// <summary>
        /// A landmark drawn without a renderer offers its far mesh here every frame it draws (one entry per mesh); the
        /// rule then decides, at each redraw, whether it is big enough on screen to cast. A changed matrix, a new mesh
        /// or one not seen for a few frames asks for a redraw.
        /// </summary>
        public static void Cast(Mesh mesh, Matrix4x4 matrix)
        {
            if (!Enabled || mesh == null)
                return;
            var frame = Time.frameCount;
            for (int i = 0; i < casterCount; i++)
                if (ReferenceEquals(casters[i].Mesh, mesh))
                {
                    if (casters[i].Matrix != matrix)
                    {
                        casters[i].Matrix = matrix;
                        castersChanged = true;
                    }
                    casters[i].Seen = frame;
                    return;
                }
            if (casterCount == casters.Length)
                System.Array.Resize(ref casters, casterCount * 2);
            casters[casterCount++] = new Caster { Mesh = mesh, Matrix = matrix, Seen = frame, Tier = -1, Kind = mesh.name };
            castersChanged = true;
        }

        void LateUpdate()
        {
            // Off until this frame hands a laid map over (SetReceivers), so the shaders never read a stale or missing one.
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            if (!Enabled)
            {
                if (front != null)
                    Release();
                return;
            }
            DropUnseen();
            var cam = Camera.main;
            var sun = RenderSettings.sun;
            if (cam == null || sun == null || !sun.isActiveAndEnabled)
                return;
            var dir = sun.transform.forward;
            // The sun at or under the horizon casts nothing worth caching (and its shadows fade out as it nears it).
            var up = math.saturate((-dir.y - 0.03f) / 0.1f);
            if (up <= 0f || !Create(out var source))
                return;
            float3 eye = cam.transform.position;
            DetailPolicy.SetView(cam);
            var handOver = ShadowDistance() * 0.75f;

            UpdateField(source, eye);
            if (Time.frameCount >= nextScan)
            {
                nextScan = Time.frameCount + RescanFrames;
                Scan(handOver);
            }

            var floraDue = FloraOn && flora.WideVersion != laidFloraVersion && Time.unscaledTime - laidTime > FloraWait;
            if (step < 0 && fieldReady && !fieldPending &&
                (!ready || castersChanged || candidatesChanged || floraDue || Vector3.Angle(dir, laidSun) > RedrawDegrees ||
                 math.distance(eye.xz, laidAt.xz) > RedrawMove))
                StartRedraw(eye, dir, handOver);
            if (step >= 0)
                DrawSlice();
            else if (ready)
                CheckMoves();
            ReadGpu();
            if (ready)
                SetReceivers(sun, up);
        }

        static float ShadowDistance()
            => UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                ? urp.shadowDistance : QualitySettings.shadowDistance;

        bool Create(out TerrainSource source)
        {
            source = default;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (terrainWorld != world)
            {
                fieldJob.Complete();
                fieldPending = false;
                fieldReady = false;
                terrainWorld = world;
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            }
            if (!terrainQuery.TryGetSingleton(out source))
                return false;
            if (front == null)
            {
                front = MapTexture("Wide shadow A");
                back = MapTexture("Wide shadow B");
                ready = false;
            }
            if (caster == null)
            {
                var shader = Resources.Load<Shader>("WideShadow");
                if (shader == null)
                    return false;
                caster = new Material(shader) { name = "Wide shadow caster" };
            }
            if (commands == null)
            {
                commands = new CommandBuffer { name = "Wide shadow" };
                sampler = CustomSampler.Create("WideShadow Redraw", true);
                recorder = sampler.GetRecorder();
                recorder.enabled = true;
                dirtySampler = CustomSampler.Create("WideShadow Dirty", true);
                dirtyRecorder = dirtySampler.GetRecorder();
                dirtyRecorder.enabled = true;
            }
            return true;
        }

        static RenderTexture MapTexture(string name)
        {
            var rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.R16)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, useMipMap = false
            };
            rt.Create();
            return rt;
        }

        static void DropUnseen()
        {
            var frame = Time.frameCount;
            for (int i = casterCount - 1; i >= 0; i--)
                if (frame - casters[i].Seen > 3)
                {
                    casters[i] = casters[--casterCount];
                    casters[casterCount] = default;
                    castersChanged = true;
                }
        }

        // ---- Terrain heightfield ----

        void UpdateField(in TerrainSource source, float3 eye)
        {
            if (fieldPending)
            {
                if (!fieldJob.IsCompleted)
                    return;
                LandField();
                return;
            }
            var n = (int)math.ceil(2f * Reach / Cell) + 1;
            if (fieldReady && fieldN == n && source.Landmarks == fieldLandmarks && math.distance(eye.xz, fieldAt) <= RedrawMove * 0.5f)
                return;
            if (fieldReady && fieldN == n && source.Landmarks == fieldLandmarks && step < 0 && math.distance(eye.xz, laidAt.xz) <= RedrawMove)
                return;   // the map's redraw (300 m) relays the field; between redraws it waits
            if (fieldN != n)
            {
                if (heights.IsCreated) heights.Dispose();
                if (points.IsCreated) points.Dispose();
                heights = new NativeArray<float>(n * n, Allocator.Persistent);
                points = new NativeArray<float3>(n * n, Allocator.Persistent);
                fieldN = n;
                BuildFieldIndices(n);
            }
            fieldLandmarks = source.Landmarks;
            fieldAt = math.floor(eye.xz / Cell) * Cell;
            var origin = fieldAt - (n - 1) * 0.5f * Cell;
            var sample = new HeightJob { Source = source, Origin = origin, N = n, Heights = heights }.Schedule(n * n, 64);
            fieldJob = new LowerJob { Origin = origin, N = n, Heights = heights, Points = points }.Schedule(n * n, 64, sample);
            JobHandle.ScheduleBatchedJobs();
            fieldPending = true;
        }

        void BuildFieldIndices(int n)
        {
            if (field == null)
                field = new Mesh { name = "Wide shadow terrain" };
            field.Clear();
            field.indexFormat = n * n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            field.SetVertices(points);
            var idx = new int[(n - 1) * (n - 1) * 6];
            int k = 0;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    int a = z * n + x, b = a + n;
                    idx[k++] = a; idx[k++] = b; idx[k++] = a + 1;
                    idx[k++] = a + 1; idx[k++] = b; idx[k++] = b + 1;
                }
            field.SetIndices(idx, MeshTopology.Triangles, 0, false);
        }

        void LandField()
        {
            fieldJob.Complete();
            fieldPending = false;
            field.SetVertices(points);
            field.RecalculateBounds();
            fieldMin = field.bounds.min.y;
            fieldMax = field.bounds.max.y + Drop;
            fieldReady = true;
            castersChanged = true;
        }

        [BurstCompile]
        struct HeightJob : IJobParallelFor
        {
            [ReadOnly] public TerrainSource Source;
            public float2 Origin;
            public int N;
            [WriteOnly] public NativeArray<float> Heights;

            public void Execute(int i) => Heights[i] = Source.SampleHeight(Origin + new float2(i % N, i / N) * Cell);
        }

        [BurstCompile]
        struct LowerJob : IJobParallelFor
        {
            public float2 Origin;
            public int N;
            [ReadOnly] public NativeArray<float> Heights;
            [WriteOnly] public NativeArray<float3> Points;

            public void Execute(int i)
            {
                int x = i % N, z = i / N;
                var low = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = math.clamp(x + dx, 0, N - 1), zz = math.clamp(z + dz, 0, N - 1);
                        low = math.min(low, Heights[zz * N + xx]);
                    }
                var p = Origin + new float2(x, z) * Cell;
                Points[i] = new float3(p.x, low - Drop, p.y);
            }
        }

        // ---- Picking the casters (the rule) ----

        /// <summary>
        /// Looks through the loaded scenes for shadow-casting meshes that could pass the rule at all: as big on screen
        /// as the wide tier asks (less its hysteresis) at the distance the map takes over from the real shadows. Which of
        /// them cast is decided per redraw (<see cref="Tier"/>); a candidate coming or going asks for a redraw.
        /// </summary>
        void Scan(float handOver)
        {
            ref readonly var view = ref DetailPolicy.Current.Data;
            var q = math.max(view.Shadows, 0.1f);
            var least = DetailPolicy.Reach(1f, DetailPolicy.Huge.WideShadow * q * (1f - DetailPolicy.Hysteresis), view.PixelsPerMetre);
            var smallest = handOver / math.max(least, 1e-3f);   // m: the size that is just big enough at handOver
            scanned.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
                ScanScene(SceneManager.GetSceneAt(s), smallest);
            ScanScene(gameObject.scene, smallest);   // DontDestroyOnLoad's scene
            if (scanned.Count != candidates.Count)
                candidatesChanged = true;
            else
                for (int i = 0; i < scanned.Count && !candidatesChanged; i++)
                    candidatesChanged = scanned[i] != candidates[i];
            (candidates, scanned) = (scanned, candidates);
            if (flora == null)
                flora = FindAnyObjectByType<FloraSystem>();
        }

        void ScanScene(Scene scene, float smallest)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            scene.GetRootGameObjects(rootObjects);
            foreach (var root in rootObjects)
            {
                if (!root.activeInHierarchy)
                    continue;
                root.GetComponentsInChildren(false, found);
                foreach (var r in found)
                    if (MeshOf(r) != null && Extent(r.bounds) >= smallest)
                        scanned.Add(r);
            }
        }

        /// <summary>The mesh a shadow-casting mesh or skinned renderer draws (null for anything else: particles, lines, off).</summary>
        static Mesh MeshOf(Renderer r)
        {
            if (r == null || !r.enabled || r.shadowCastingMode == ShadowCastingMode.Off || !r.gameObject.activeInHierarchy)
                return null;
            if (r is SkinnedMeshRenderer skin)
                return skin.sharedMesh;
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf))
                return mf.sharedMesh;
            return null;
        }

        /// <summary>A box's largest extent (DetailPolicy's size).</summary>
        static float Extent(Bounds b) => 2f * math.cmax((float3)b.extents);

        /// <summary>
        /// The rule for one caster: its shadow tier by DetailPolicy.Huge at its size and distance, though never nearer
        /// than handOver (inside the real shadows its far shadow still counts from where the map takes over).
        /// </summary>
        static int Tier(Bounds box, float3 eye, float handOver, int previous)
        {
            ref readonly var view = ref DetailPolicy.Current.Data;
            var d = math.max(math.distance((float3)box.center, eye), handOver);
            return DetailPolicy.ShadowTier(DetailPolicy.ScreenSize(Extent(box), d, view.PixelsPerMetre), DetailPolicy.Huge, view.Shadows, previous);
        }

        string KindOf(Renderer r)
        {
            var root = r.transform.root;
            if (!kinds.TryGetValue(root, out var kind))
                kinds[root] = kind = root.name;
            return kind;
        }

        void CountKind(string kind)
        {
            kindCounts.TryGetValue(kind, out var n);
            kindCounts[kind] = n + 1;
        }

        // ---- Redraw ----

        /// <summary>Lays a new sun view round the player and snapshots what casts in it (the draws then take a few frames).</summary>
        void StartRedraw(float3 eye, Vector3 dir, float handOver)
        {
            watch.Restart();
            castersChanged = false;
            candidatesChanged = false;
            draws.Clear();
            if (LogKinds)
                kindCounts.Clear();
            var centre = new float3(eye.x, 0f, eye.z);
            var reach2 = (Reach + 400f) * (Reach + 400f);
            var top = fieldMax;
            int picked = 0;
            for (int i = 0; i < casterCount; i++)
            {
                ref var c = ref casters[i];
                var box = BoxOf(c.Mesh, c.Matrix);
                c.Tier = Tier(box, eye, handOver, c.Tier);
                if (c.Tier <= 2 && AddDraw(c.Mesh, c.Matrix, null, box, centre, reach2, ref top))
                {
                    picked++;
                    if (LogKinds)
                        CountKind(c.Kind);
                }
            }
            // Skinned parts (the rigged colossi) cast in their bind pose: a far shadow 1.6 m a texel doesn't show the pose.
            nextTiers.Clear();
            foreach (var r in candidates)
            {
                var mesh = MeshOf(r);
                if (mesh == null)
                    continue;
                var box = r.bounds;
                var tier = Tier(box, eye, handOver, tiers.TryGetValue(r, out var was) ? was : -1);
                nextTiers[r] = tier;
                if (tier <= 2 && AddDraw(mesh, r.transform.localToWorldMatrix, r, box, centre, reach2, ref top))
                {
                    picked++;
                    if (LogKinds)
                        CountKind(KindOf(r));
                }
            }
            (tiers, nextTiers) = (nextTiers, tiers);

            // The sun view: x across the sunlight, y up it, z along it. Its half height and depth hold the circle of Reach
            // round the player and the heights from the lowest ground to the tallest caster, so a low sun keeps them tight.
            var flat = math.length(new float2(dir.x, dir.z));
            var sinE = math.saturate(-dir.y);
            var mid = 0.5f * (fieldMin + top);
            var half = 0.5f * (top - fieldMin) + 5f;
            var hx = Reach;
            var hy = Reach * sinE + half * flat;
            var depth = Reach * flat + half * sinE + 50f;
            var rot = Quaternion.LookRotation(dir, math.abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            // Texel snap: the centre's place across and up the sunlight is rounded to whole texels.
            Vector3 c0 = new Vector3(eye.x, mid, eye.z), right = rot * Vector3.right, upv = rot * Vector3.up;
            float tx = 2f * hx / Size, ty = 2f * hy / Size;
            var cx = math.round(Vector3.Dot(c0, right) / tx) * tx;
            var cy = math.round(Vector3.Dot(c0, upv) / ty) * ty;
            c0 = right * cx + upv * cy + dir * Vector3.Dot(c0, dir);
            var from = c0 - dir * depth;
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(from, rot, Vector3.one).inverse;
            var proj = Matrix4x4.Ortho(-hx, hx, -hy, hy, 0f, depth * 2f);
            backVP = GL.GetGPUProjectionMatrix(proj, true) * view;
            backUv = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f)) * Matrix4x4.Scale(new Vector3(0.5f, 0.5f, 1f)) * proj * view;
            backEye = from;
            backSun = dir;
            backInvRange = 1f / (depth * 2f);
            laidSun = dir;
            laidAt = eye;
            laidTime = Time.unscaledTime;

            // The flora's big trees and built things, by the same rule on the GPU (all of them out to the map's corner:
            // the player can walk RedrawMove before the next redraw, so none are left to the near shadows).
            if (FloraOn)
            {
                FloraSystem.CountKinds = LogKinds;
                flora.WideCull(eye, Reach * 1.415f, 0f);
                FloraSystem.CountKinds = false;
                laidFloraVersion = flora.WideVersion;
            }
            if (LogKinds)
                KindsLog = KindsText(picked);
            // Two 16-bit textures (no depth buffer: the caster pass keeps the nearest by a min blend) and the flora's cull.
            Bytes = 2L * Size * Size * 2 + (FloraOn ? flora.WideBytes() : 0);
            step = 0;
            cpuSum = cpuMax = gpuSum = gpuMax = 0;
            gpuFrames = 0;
            var ms = watch.Elapsed.TotalMilliseconds;
            cpuSum += ms;
            cpuMax = ms;
        }

        bool FloraOn => Flora && flora != null && flora.isActiveAndEnabled;

        string KindsText(int picked)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(picked).Append(" meshes (");
            var first = true;
            foreach (var kv in kindCounts)
            {
                sb.Append(first ? "" : ", ").Append(kv.Key).Append(' ').Append(kv.Value);
                first = false;
            }
            sb.Append("); flora: ").Append(FloraSystem.WideKinds).Append("; of ").Append(candidates.Count).Append(" candidate renderers");
            return sb.ToString();
        }

        static Bounds BoxOf(Mesh mesh, Matrix4x4 m)
        {
            var b = mesh.bounds;
            var c = m.MultiplyPoint3x4(b.center);
            var e = b.extents;
            // The box's extents through the matrix (its axes' absolute contributions).
            var x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            var y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            var z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            var ext = new Vector3(math.abs(x.x) + math.abs(y.x) + math.abs(z.x),
                math.abs(x.y) + math.abs(y.y) + math.abs(z.y),
                math.abs(x.z) + math.abs(y.z) + math.abs(z.z));
            return new Bounds(c, ext * 2f);
        }

        bool AddDraw(Mesh mesh, Matrix4x4 matrix, Renderer source, Bounds box, float3 centre, float reach2, ref float top)
        {
            var size = box.extents.magnitude;
            if (math.distancesq(new float2(box.center.x, box.center.z), centre.xz) > reach2 + size * size)
                return false;
            top = math.max(top, box.max.y);
            var t = source != null ? source.transform : null;
            draws.Add(new Draw
            {
                Mesh = mesh, Matrix = matrix, Source = source, Box = box,
                At = t != null ? t.position : Vector3.zero, Turn = t != null ? t.rotation : Quaternion.identity
            });
            return true;
        }

        /// <summary>Draws this frame's share: the terrain and the first casters on the first frame, the rest after; swaps after the last.</summary>
        void DrawSlice()
        {
            // The flora draws in the slices after the first, once its cull's counts are back (a frame or two), so it
            // skips the models the cull left empty; it waits at most a few frames for them.
            if (step == 1 && FloraOn && !flora.WideReady() && floraHeld++ < 6)
                return;
            if (step == 1)
                floraHeld = 0;
            watch.Restart();
            var cmd = commands;
            cmd.Clear();
            cmd.BeginSample(sampler);
            cmd.SetRenderTarget(back);
            SetView(cmd, backVP, backEye, backInvRange, backSun);
            if (step == 0)
            {
                cmd.ClearRenderTarget(false, true, Color.white);
                cmd.DrawMesh(field, Matrix4x4.identity, caster, 0, 0);
            }
            DrawMeshes(cmd, draws.Count * step / RedrawFrames, draws.Count * (step + 1) / RedrawFrames);
            if (FloraOn && step > 0)
                flora.WideDraw(cmd, step - 1, RedrawFrames - 1);
            cmd.EndSample(sampler);
            Graphics.ExecuteCommandBuffer(cmd);
            var ms = watch.Elapsed.TotalMilliseconds;
            cpuSum += ms;
            cpuMax = math.max(cpuMax, ms);
            if (++step < RedrawFrames)
                return;
            (front, back) = (back, front);
            frontVP = backVP;
            frontUv = backUv;
            frontSun = backSun;
            frontEye = backEye;
            frontInvRange = backInvRange;
            ready = true;
            step = -1;
            Redraws++;
            gpuFrames = RedrawFrames + 3;   // keep reading the GPU timer for the frames the slices land on
        }

        static void SetView(CommandBuffer cmd, Matrix4x4 vp, Vector3 eye, float invRange, Vector3 sun)
        {
            cmd.SetGlobalMatrix(VPId, vp);
            cmd.SetGlobalVector(EyeId, new Vector4(eye.x, eye.y, eye.z, invRange));
            cmd.SetGlobalVector(SunId, sun);
        }

        void DrawMeshes(CommandBuffer cmd, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                var d = draws[i];
                if (d.Mesh == null)
                    continue;
                for (int s = 0; s < d.Mesh.subMeshCount; s++)
                    cmd.DrawMesh(d.Mesh, d.Matrix, caster, s, 0);
            }
        }

        // ---- Dirty rects (moving casters) ----

        /// <summary>
        /// A renderer in the map that moved or turned past the limits, or went away, has the map's part under where it
        /// was and where it is now wiped and drawn again (the terrain, every mesh and the flora, under a scissor rect).
        /// </summary>
        void CheckMoves()
        {
            var any = false;
            float2 lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < draws.Count; i++)
            {
                var d = draws[i];
                if (d.Mesh == null || ReferenceEquals(d.Source, null))
                    continue;
                var gone = MeshOf(d.Source) == null;
                Transform t = gone ? null : d.Source.transform;
                if (!gone && (t.position - d.At).sqrMagnitude <= MoveMetres * MoveMetres && Quaternion.Angle(t.rotation, d.Turn) <= MoveDegrees)
                    continue;
                Grow(d.Box, ref lo, ref hi);
                if (gone)
                    d.Mesh = null;
                else
                {
                    d.Matrix = t.localToWorldMatrix;
                    d.At = t.position;
                    d.Turn = t.rotation;
                    d.Box = d.Source.bounds;
                    Grow(d.Box, ref lo, ref hi);
                }
                draws[i] = d;
                any = true;
            }
            if (!any)
                return;
            // Texels, with two to spare for the receivers' taps; nothing when it's all off the map.
            var a = (int2)math.floor(math.saturate(lo) * Size) - 2;
            var b = (int2)math.ceil(math.saturate(hi) * Size) + 2;
            a = math.max(a, 0);
            b = math.min(b, Size);
            if (math.any(b <= a))
                return;
            DrawDirty(new RectInt(a.x, a.y, b.x - a.x, b.y - a.y));
        }

        /// <summary>Grows a uv rect by a world box's eight corners through the front map's view.</summary>
        void Grow(Bounds box, ref float2 lo, ref float2 hi)
        {
            var c = box.center;
            var e = box.extents;
            for (int k = 0; k < 8; k++)
            {
                var p = c + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                var uv = frontUv.MultiplyPoint3x4(p);
                lo = math.min(lo, new float2(uv.x, uv.y));
                hi = math.max(hi, new float2(uv.x, uv.y));
            }
        }

        void DrawDirty(RectInt r)
        {
            watch.Restart();
            var cmd = commands;
            cmd.Clear();
            cmd.BeginSample(dirtySampler);
            cmd.SetRenderTarget(front);
            cmd.EnableScissorRect(new Rect(r.x, r.y, r.width, r.height));
            cmd.DrawProcedural(Matrix4x4.identity, caster, 1, MeshTopology.Triangles, 3);
            SetView(cmd, frontVP, frontEye, frontInvRange, frontSun);
            cmd.DrawMesh(field, Matrix4x4.identity, caster, 0, 0);
            DrawMeshes(cmd, 0, draws.Count);
            if (FloraOn)
                flora.WideDraw(cmd, 0, 1);
            cmd.DisableScissorRect();
            cmd.EndSample(dirtySampler);
            Graphics.ExecuteCommandBuffer(cmd);
            dirtyCpu = watch.Elapsed.TotalMilliseconds;
            dirtyGpu = 0;
            dirtyRect = r;
            dirtyFrames = 3;
            DirtyRects++;
        }

        void ReadGpu()
        {
            if (dirtyFrames > 0)
            {
                if (dirtyRecorder != null && dirtyRecorder.gpuSampleBlockCount > 0)
                    dirtyGpu = math.max(dirtyGpu, dirtyRecorder.gpuElapsedNanoseconds / 1e6);
                if (--dirtyFrames == 0)
                {
                    LastDirtyLog = System.FormattableString.Invariant(
                        $"{dirtyRect.width}x{dirtyRect.height} texels at ({dirtyRect.x}, {dirtyRect.y}), CPU {dirtyCpu:F2} ms, GPU {dirtyGpu:F2} ms");
                    if (dirtyLogs++ < 12)
                        Debug.Log("[wideshadow] dirty rect " + DirtyRects + ": " + LastDirtyLog);
                }
            }
            if (gpuFrames <= 0)
                return;
            if (recorder != null && recorder.gpuSampleBlockCount > 0)
            {
                var ms = recorder.gpuElapsedNanoseconds / 1e6;
                gpuSum += ms;
                gpuMax = math.max(gpuMax, ms);
            }
            if (--gpuFrames > 0)
                return;
            LastLog = System.FormattableString.Invariant(
                $"{draws.Count} meshes + flora + terrain {fieldN}x{fieldN}, CPU {cpuSum:F2} ms (worst frame {cpuMax:F2}), GPU {gpuSum:F2} ms (worst frame {gpuMax:F2}) over {RedrawFrames} frames, at ({laidAt.x:0}, {laidAt.z:0}), sun elevation {math.degrees(math.asin(math.saturate(-frontSun.y))):F1} deg, {Bytes / (1024 * 1024)} MB");
            if (logs++ < 12)
                Debug.Log("[wideshadow] redraw " + Redraws + ": " + LastLog);
        }

        /// <summary>Hands the laid map to the terrain and flora shaders (globals; Params.x 0 turns their read off).</summary>
        void SetReceivers(Light sun, float up)
        {
            var shadowDistance = ShadowDistance();
            Shader.SetGlobalTexture(TexId, front);
            Shader.SetGlobalMatrix(MatrixId, frontUv);
            Shader.SetGlobalVector(FrontSunId, frontSun);
            Shader.SetGlobalVector(FrontEyeId, new Vector4(frontEye.x, frontEye.y, frontEye.z, frontInvRange));
            Shader.SetGlobalVector(ParamsId, new Vector4(Darkness * sun.shadowStrength * up, 0f, 0f, 0f));
            // In over the real shadows' last quarter (the shaders take the darker of the two there, so it can't double).
            Shader.SetGlobalVector(FadeId, new Vector4(shadowDistance * 0.75f, shadowDistance, 0f, 0f));
        }

        void Release()
        {
            foreach (var rt in new[] { front, back })
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            front = back = null;
            ready = false;
            step = -1;
            Bytes = 0;
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Release();
            fieldJob.Complete();
            if (heights.IsCreated) heights.Dispose();
            if (points.IsCreated) points.Dispose();
            if (field != null) Destroy(field);
            if (caster != null) Destroy(caster);
            commands?.Release();
            if (instance == this)
                instance = null;
        }

        static bool Switch(string arg, bool fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, arg);
            if (i < 0)
                return fallback;
            return !(i + 1 < args.Length && args[i + 1] == "0");
        }

        static float Number(string arg, float fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, arg);
            return i >= 0 && i + 1 < args.Length &&
                   float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
