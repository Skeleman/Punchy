using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Punchy.Rendering
{
    /// <summary>
    /// R-5: one wide cached sun-shadow map for the huge things. Real shadows stop at the pipeline's shadow distance
    /// and the flora cache at 120 m, so from afar the colossi, the salt tower and the sunken temple stood shadowless,
    /// patched by one strip each (FarShadow). This lays one orthographic sun view about Reach round the player (3.2 km
    /// across by default), texel-snapped, into a 2048x2048 R16 texture that keeps, per texel, how far along the
    /// sunlight the nearest caster is. Casters: the landmarks that register a mesh each frame (<see cref="Cast"/>), the
    /// colossi (their renderers, found from StoneColossi and DunesColossus) and a coarse terrain heightfield (25 m
    /// cells, lowered a little so the ground never shades itself), so mountains shade valleys at a low sun too.
    /// The map is redrawn only when the sun turns RedrawDegrees, the player moves RedrawMove, or a caster moves or
    /// comes and goes; a redraw is spread over RedrawFrames frames into a second texture, swapped in when done. Every
    /// frame the map is handed to the terrain and flora shaders as globals (Shaders/WideShadow.hlsl), which take the
    /// direct sunlight from what a cached caster stands between and the sun, past the real-time shadows; the haze
    /// over it thins it as it did the old strips. Cosmetic and local: each client
    /// lays its own. "-wideshadow 0" turns it off; "-wideshadowdark" sets how dark it is.
    /// </summary>
    public class WideShadow : MonoBehaviour
    {
        /// <summary>On by default; "-wideshadow 0" (command line or a shot line) turns it off. Read every frame.</summary>
        public static bool Enabled = Switch("-wideshadow", true);
        /// <summary>How much of the direct sunlight a wide shadow takes (0-1, times the sun's shadow strength), as a real shadow's 1.</summary>
        public static float Darkness = Number("-wideshadowdark", 1f);
        /// <summary>Half the map's width (m, "-widereach"): it covers 2 x Reach round the player.</summary>
        public static float Reach = Number("-widereach", 1600f);
        /// <summary>Redrawn when the sun has turned this far (degrees) or the player moved this far (m).</summary>
        public static float RedrawDegrees = 1f, RedrawMove = 300f;
        public const int Size = 2048, RedrawFrames = 4;

        /// <summary>GPU memory the map holds (both textures), in bytes.</summary>
        public static long Bytes;
        /// <summary>Redraws finished this session.</summary>
        public static int Redraws;
        /// <summary>Last finished redraw: casters, CPU and GPU time, centre, sun.</summary>
        public static string LastLog = "";

        // Terrain heightfield: Cells x Cells quads of Cell metres round the player, lowered by Drop below the lowest of
        // each point's 3x3 neighbourhood (between grid points the real ground can sag under a straight line).
        const float Cell = 25f, Drop = 1.5f;
        // Colossus roots are looked for again every RescanFrames frames; one that moved RootMove m or turned RootTurn
        // degrees since the map was laid asks for a redraw (a walker gets a fresh shadow, as the old strips after 10 m).
        const int RescanFrames = 90;
        const float RootMove = 10f, RootTurn = 5f;

        static readonly int VPId = Shader.PropertyToID("_WideShadowVP");
        static readonly int EyeId = Shader.PropertyToID("_WideShadowEye");
        static readonly int SunId = Shader.PropertyToID("_WideShadowSun");
        static readonly int FrontEyeId = Shader.PropertyToID("_WideShadowFrontEye");
        static readonly int FrontSunId = Shader.PropertyToID("_WideShadowFrontSun");
        static readonly int TexId = Shader.PropertyToID("_WideShadowTex");
        static readonly int MatrixId = Shader.PropertyToID("_WideShadowMatrix");
        static readonly int ParamsId = Shader.PropertyToID("_WideShadowParams");
        static readonly int FadeId = Shader.PropertyToID("_WideShadowFade");

        struct Caster
        {
            public Mesh Mesh;
            public Matrix4x4 Matrix;
            public int Seen;
        }

        // Landmark casters, registered every frame by their systems; dropped after a few frames unseen.
        static Caster[] casters = new Caster[8];
        static int casterCount;
        static bool castersChanged;
        static WideShadow instance;

        RenderTexture front, back;
        Material caster;
        CommandBuffer commands;
        CustomSampler sampler;
        Recorder recorder;
        bool ready;
        int step = -1;
        Matrix4x4 backVP, backUv, frontUv;
        Vector3 backSun, frontSun, laidSun, backEye, frontEye;
        float backInvRange, frontInvRange;
        float3 laidAt;

        // What a redraw draws, snapshotted when it starts (the slices then take a few frames).
        readonly List<Caster> draws = new List<Caster>(64);
        // Colossi: their roots and the mesh renderers under them.
        readonly List<Transform> roots = new List<Transform>(16);
        readonly List<Vector3> rootAt = new List<Vector3>(16);
        readonly List<Quaternion> rootTurn = new List<Quaternion>(16);
        readonly List<Renderer> filters = new List<Renderer>(256);
        readonly List<Renderer> found = new List<Renderer>(64);
        int nextScan;
        bool rootsChanged;

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

        // Timing of the redraw under way (CPU per slice, GPU read back a frame or more late).
        readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        double cpuSum, cpuMax, gpuSum, gpuMax;
        int gpuFrames, logs;

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
        /// A landmark too big for the real shadows registers its far mesh here every frame it draws (one entry per mesh).
        /// A changed matrix, a new mesh or one not seen for a few frames asks for a redraw.
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
            casters[casterCount++] = new Caster { Mesh = mesh, Matrix = matrix, Seen = frame };
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

            UpdateField(source, eye);
            if (Time.frameCount >= nextScan)
            {
                nextScan = Time.frameCount + RescanFrames;
                ScanColossi();
            }
            CheckRoots();

            if (step < 0 && fieldReady && !fieldPending &&
                (!ready || castersChanged || rootsChanged || Vector3.Angle(dir, laidSun) > RedrawDegrees ||
                 math.distance(eye.xz, laidAt.xz) > RedrawMove))
                StartRedraw(eye, dir);
            if (step >= 0)
                DrawSlice();
            ReadGpu();
            if (ready)
                SetReceivers(sun, up);
        }

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
            }
            // Two 16-bit textures, no depth buffer (the caster pass keeps the nearest by a min blend).
            Bytes = 2L * Size * Size * 2;
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

        // ---- Colossi ----

        /// <summary>Finds the colossi's roots (StoneColossi's slots and the Dunes colossus) and the meshes under them.</summary>
        void ScanColossi()
        {
            var stone = FindAnyObjectByType<StoneColossi>();
            var dunes = FindAnyObjectByType<DunesColossus>();
            int count = 0;
            if (!DunesColossus.Hidden)
            {
                if (stone != null)
                    for (int slot = 0; slot < StoneColossi.Placed.Length; slot++)
                        AddRoot(stone.Root(slot), ref count);
                if (dunes != null && DunesColossus.Placed)
                    AddRoot(dunes.transform, ref count);
            }
            if (count != roots.Count)
            {
                roots.RemoveRange(count, roots.Count - count);
                rootAt.RemoveRange(count, rootAt.Count - count);
                rootTurn.RemoveRange(count, rootTurn.Count - count);
                rootsChanged = true;
            }
        }

        void AddRoot(Transform root, ref int count)
        {
            if (root == null || !root.gameObject.activeInHierarchy)
                return;
            if (count < roots.Count)
            {
                if (roots[count] != root)
                {
                    roots[count] = root;
                    rootAt[count] = new Vector3(float.MaxValue, 0f, 0f);
                    rootsChanged = true;
                }
            }
            else
            {
                roots.Add(root);
                rootAt.Add(new Vector3(float.MaxValue, 0f, 0f));
                rootTurn.Add(Quaternion.identity);
                rootsChanged = true;
            }
            count++;
        }

        void CheckRoots()
        {
            for (int i = 0; i < roots.Count; i++)
            {
                var r = roots[i];
                if (r == null)
                {
                    rootsChanged = true;
                    continue;
                }
                if ((r.position - rootAt[i]).sqrMagnitude > RootMove * RootMove || Quaternion.Angle(r.rotation, rootTurn[i]) > RootTurn)
                    rootsChanged = true;
            }
        }

        // ---- Redraw ----

        /// <summary>Lays a new sun view round the player and snapshots what casts in it (the draws then take a few frames).</summary>
        void StartRedraw(float3 eye, Vector3 dir)
        {
            watch.Restart();
            castersChanged = false;
            rootsChanged = false;
            draws.Clear();
            var centre = new float3(eye.x, 0f, eye.z);
            var reach2 = (Reach + 400f) * (Reach + 400f);
            var top = fieldMax;
            var frame = Time.frameCount;
            for (int i = 0; i < casterCount; i++)
                AddDraw(casters[i].Mesh, casters[i].Matrix, centre, reach2, ref top);
            filters.Clear();
            for (int i = 0; i < roots.Count; i++)
            {
                var r = roots[i];
                if (r == null)
                    continue;
                rootAt[i] = r.position;
                rootTurn[i] = r.rotation;
                r.GetComponentsInChildren(false, found);
                filters.AddRange(found);
            }
            // Skinned parts (the rigged colossi) cast in their bind pose: a far shadow 1.6 m a texel doesn't show the pose.
            foreach (var f in filters)
            {
                if (!f.enabled || f.shadowCastingMode == ShadowCastingMode.Off)
                    continue;
                if (f is SkinnedMeshRenderer skin)
                    AddDraw(skin.sharedMesh, skin.transform.localToWorldMatrix, centre, reach2, ref top);
                else if (f is MeshRenderer && f.TryGetComponent<MeshFilter>(out var mf))
                    AddDraw(mf.sharedMesh, f.transform.localToWorldMatrix, centre, reach2, ref top);
            }

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
            Vector3 c = new Vector3(eye.x, mid, eye.z), right = rot * Vector3.right, upv = rot * Vector3.up;
            float tx = 2f * hx / Size, ty = 2f * hy / Size;
            var cx = math.round(Vector3.Dot(c, right) / tx) * tx;
            var cy = math.round(Vector3.Dot(c, upv) / ty) * ty;
            c = right * cx + upv * cy + dir * Vector3.Dot(c, dir);
            var from = c - dir * depth;
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(from, rot, Vector3.one).inverse;
            var proj = Matrix4x4.Ortho(-hx, hx, -hy, hy, 0f, depth * 2f);
            backVP = GL.GetGPUProjectionMatrix(proj, true) * view;
            backUv = Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0f)) * Matrix4x4.Scale(new Vector3(0.5f, 0.5f, 1f)) * proj * view;
            backEye = from;
            backSun = dir;
            backInvRange = 1f / (depth * 2f);
            laidSun = dir;
            laidAt = eye;
            step = 0;
            cpuSum = cpuMax = gpuSum = gpuMax = 0;
            gpuFrames = 0;
            var ms = watch.Elapsed.TotalMilliseconds;
            cpuSum += ms;
            cpuMax = ms;
        }

        void AddDraw(Mesh mesh, Matrix4x4 matrix, float3 centre, float reach2, ref float top)
        {
            if (mesh == null)
                return;
            var b = mesh.bounds;
            var at = matrix.MultiplyPoint3x4(b.center);
            var size = matrix.lossyScale.x * b.extents.magnitude;
            if (math.distancesq(new float2(at.x, at.z), centre.xz) > reach2 + size * size)
                return;
            top = math.max(top, at.y + size);
            draws.Add(new Caster { Mesh = mesh, Matrix = matrix });
        }

        /// <summary>Draws this frame's share: the terrain and the first casters on the first frame, the rest after; swaps after the last.</summary>
        void DrawSlice()
        {
            watch.Restart();
            var cmd = commands;
            cmd.Clear();
            cmd.BeginSample(sampler);
            cmd.SetRenderTarget(back);
            cmd.SetGlobalMatrix(VPId, backVP);
            cmd.SetGlobalVector(EyeId, new Vector4(backEye.x, backEye.y, backEye.z, backInvRange));
            cmd.SetGlobalVector(SunId, backSun);
            if (step == 0)
            {
                cmd.ClearRenderTarget(false, true, Color.white);
                cmd.DrawMesh(field, Matrix4x4.identity, caster, 0, 0);
            }
            var from = draws.Count * step / RedrawFrames;
            var to = draws.Count * (step + 1) / RedrawFrames;
            for (int i = from; i < to; i++)
            {
                var d = draws[i];
                if (d.Mesh == null)
                    continue;
                for (int s = 0; s < d.Mesh.subMeshCount; s++)
                    cmd.DrawMesh(d.Mesh, d.Matrix, caster, s, 0);
            }
            cmd.EndSample(sampler);
            Graphics.ExecuteCommandBuffer(cmd);
            var ms = watch.Elapsed.TotalMilliseconds;
            cpuSum += ms;
            cpuMax = math.max(cpuMax, ms);
            if (++step < RedrawFrames)
                return;
            (front, back) = (back, front);
            frontUv = backUv;
            frontSun = backSun;
            frontEye = backEye;
            frontInvRange = backInvRange;
            ready = true;
            step = -1;
            Redraws++;
            gpuFrames = RedrawFrames + 3;   // keep reading the GPU timer for the frames the slices land on
        }

        void ReadGpu()
        {
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
                $"{draws.Count} casters + terrain {fieldN}x{fieldN}, CPU {cpuSum:F2} ms (worst frame {cpuMax:F2}), GPU {gpuSum:F2} ms (worst frame {gpuMax:F2}) over {RedrawFrames} frames, at ({laidAt.x:0}, {laidAt.z:0}), sun elevation {math.degrees(math.asin(math.saturate(-frontSun.y))):F1} deg, {Bytes / (1024 * 1024)} MB");
            if (logs++ < 12)
                Debug.Log("[wideshadow] redraw " + Redraws + ": " + LastLog);
        }

        /// <summary>Hands the laid map to the terrain and flora shaders (globals; Params.x 0 turns their read off).</summary>
        void SetReceivers(Light sun, float up)
        {
            var shadowDistance = GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp
                ? urp.shadowDistance : QualitySettings.shadowDistance;
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
