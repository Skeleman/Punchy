using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Punchy.Debugging
{
    /// <summary>
    /// A-96 (debug, "-sparkleprobe label", off by default): counts the creature pixels that bloom turns into small white
    /// sparkles. Bloom clamps each pixel at 16 before it spreads it, but a pixel that is not a number (or infinite) still
    /// blooms as a full bright circle, so one bad pixel on a running rabbit reads as a flicker.
    ///
    /// For a number of frames it renders the main camera's view a second time, into a half-float texture with no post
    /// effects, with the creature shader's probe on (_PunchySparkleProbe: the cap): a creature pixel that comes out bad or
    /// over the cap is drawn as a marker that says which shell layer it was on and which step went bad first. It counts the
    /// markers each frame, plus any other pixel that is bad or over the cap (another shader), and logs one "[sparkle]" line
    /// a frame and a summary at the end. The first frames with markers are also saved as crops, the normal picture (post
    /// effects on, so the sparkle shows) beside the marked one.
    /// </summary>
    public class SparkleProbe : MonoBehaviour
    {
        const float Marker = 30000f;
        const int CropSize = 64, CropScale = 4, MaxCrops = 3;

        static SparkleProbe runner;
        static readonly int ProbeId = Shader.PropertyToID("_PunchySparkleProbe");

        string label, cropDir;
        float cap, startAt;
        int framesLeft, framesDone, crops;
        Camera probe;
        RenderTexture rt;
        Texture2D read;
        // Totals over the run: marked pixels by shell layer (0 body, 1, 2), by the step that went bad, other shaders'.
        readonly long[] layers = new long[3];
        readonly long[] steps = new long[6];
        long otherBad, otherHot, frameHits;
        readonly StringBuilder hitFrames = new StringBuilder();

        /// <summary>Starts a run of frames frames after delay seconds; crops go to cropDir (none when null).</summary>
        public static void Begin(string label, int frames = 360, float delay = 3f, float cap = 16f, string cropDir = null)
        {
            if (runner == null)
            {
                var go = new GameObject("SparkleProbe") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                runner = go.AddComponent<SparkleProbe>();
                Hook();
                // (Play mode stopping does not destroy a hidden object: the summary is logged as it stops.)
                Application.quitting += () => { if (runner != null) runner.Finish(); };
            }
            runner.Finish();
            runner.label = label;
            runner.cap = cap;
            runner.framesLeft = frames;
            runner.framesDone = 0;
            runner.crops = 0;
            runner.cropDir = cropDir;
            runner.startAt = Time.unscaledTime + delay;
            System.Array.Clear(runner.layers, 0, 3);
            System.Array.Clear(runner.steps, 0, 6);
            runner.otherBad = runner.otherHot = runner.frameHits = 0;
            runner.hitFrames.Clear();
            runner.Log($"[sparkle] {label}: probing {frames} frames from {delay:F0} s, cap {cap}");
        }

        /// <summary>A run under way.</summary>
        public static bool Running => runner != null && runner.framesLeft > 0;

        // This frame's segment check (CheckSegments): how many were bad, and the first one.
        int segBad;
        string segFirst;

        /// <summary>
        /// The creature segments about to be drawn this frame: counts those with a value that is not a number or infinite,
        /// a rotation far from unit length, or a radius or flattening at or below zero, for the frame's log line.
        /// </summary>
        public static void CheckSegments(Unity.Collections.NativeArray<Punchy.Fauna.SegmentInstance> segments, int count)
        {
            if (runner == null)
                return;
            int bad = 0;
            string first = null;
            for (int i = 0; i < count && i < segments.Length; i++)
            {
                var s = segments[i];
                var q = s.Rotation.value;
                bool finite = Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(s.Position)) && Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(q)) &&
                              Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(new Unity.Mathematics.float4(s.Length, s.RadiusStart, s.RadiusEnd, s.Flatten))) &&
                              Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(s.Color)) && Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(s.Material));
                var unit = Unity.Mathematics.math.abs(Unity.Mathematics.math.length(q) - 1f) < 0.01f;
                // (Hidden parts are drawn collapsed to a point, radius and length 0: not counted.)
                var point = s.RadiusStart == 0f && s.RadiusEnd == 0f && s.Length == 0f;
                var shaped = point || (s.RadiusStart >= 0f && s.RadiusEnd >= 0f && s.Flatten > 0f);
                if (finite && unit && shaped)
                    continue;
                if (bad++ == 0)
                    first = $"{(finite ? "" : "not finite ")}{(unit ? "" : "not unit ")}{(shaped ? "" : "bad shape ")}#{i} pos {s.Position} len {s.Length} rot {q} r {s.RadiusStart}-{s.RadiusEnd} flat {s.Flatten} pad {s.Pad} mat {s.Material}";
            }
            runner.segBad = bad;
            runner.segFirst = first;
        }

        /// <summary>"-nobloom": bloom off in every volume (their own copies of the profile: the asset is untouched), or back on.</summary>
        public static void NoBloom(bool off)
        {
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if ((off || volume.HasInstantiatedProfile()) && volume.profile != null && volume.profile.TryGet<Bloom>(out var bloom))
                    bloom.active = !off;
        }

        // The probe renders at the start of PostLateUpdate: the creature draws are queued in PresentationSystemGroup,
        // after the scripts' LateUpdate, so a render from LateUpdate would show no creatures at all.
        static void Hook()
        {
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var subs = loop.subSystemList[i].subSystemList;
                foreach (var sub in subs)
                    if (sub.type == typeof(SparkleProbe))
                        return;
                var list = new System.Collections.Generic.List<UnityEngine.LowLevel.PlayerLoopSystem>(subs);
                list.Insert(0, new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(SparkleProbe),
                    updateDelegate = () => { if (runner != null) runner.Tick(); }
                });
                loop.subSystemList[i].subSystemList = list.ToArray();
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
                return;
            }
        }

        void Tick()
        {
            if (framesLeft <= 0 || Time.unscaledTime < startAt)
                return;
            var main = Camera.main;
            if (main == null)
                return;
            int w = main.pixelWidth, h = main.pixelHeight;
            Setup(main, w, h);
            Render(true, false);
            Count(w, h);
            if (--framesLeft == 0)
                Finish();
        }

        void Setup(Camera main, int w, int h)
        {
            if (probe == null)
            {
                var go = new GameObject("SparkleProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(transform, false);
                probe = go.AddComponent<Camera>();
                probe.enabled = false;
            }
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                    Destroy(read);
                }
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "SparkleProbe" };
                rt.Create();
                read = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
            }
            probe.CopyFrom(main);
            probe.enabled = false;
            probe.targetTexture = rt;
            var mainData = main.GetUniversalAdditionalCameraData();
            var data = probe.GetUniversalAdditionalCameraData();
            data.renderShadows = mainData.renderShadows;
            data.requiresDepthTexture = mainData.requiresDepthTexture;
            data.requiresColorTexture = mainData.requiresColorTexture;
            data.antialiasing = AntialiasingMode.None;
            // R-18: the probe reads the raw frame; Stop NaN (on for the main camera) would hide what it counts.
            data.stopNaN = false;
        }

        // One render of the probe camera this frame: with the creature shader's markers, or the normal picture.
        void Render(bool marked, bool post)
        {
            // (ECS jobs from this frame's systems may still be writing transforms; the culling reads them.)
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                world.EntityManager.CompleteAllTrackedJobs();
            probe.GetUniversalAdditionalCameraData().renderPostProcessing = post;
            Shader.SetGlobalFloat(ProbeId, marked ? cap : 0f);
            probe.Render();
            Shader.SetGlobalFloat(ProbeId, 0f);
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = active;
        }

        // Each line to the console and, with a crop folder, to sparkle-log.txt there (editors can share one Editor.log).
        void Log(string line)
        {
            Debug.Log(line);
            if (cropDir == null)
                return;
            System.IO.Directory.CreateDirectory(cropDir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(cropDir, "sparkle-log.txt"), line + "\n");
        }

        static bool Bad(ushort half) => (half & 0x7C00) == 0x7C00;

        void Count(int w, int h)
        {
            var px = read.GetRawTextureData<ushort>();
            // (Positive halves sort as their bits do: the tests stay on the raw bits, sign masked off.)
            ushort markBits = Mathf.FloatToHalf(Marker), capBits = Mathf.FloatToHalf(cap);
            var frameLayers = new int[3];
            var frameSteps = new int[6];
            int bad = 0, hot = 0, markMask = 0, minX = w, minY = h, maxX = -1, maxY = -1, badX = -1, badY = -1, markX = -1, markY = -1;
            for (int i = 0; i < w * h; i++)
            {
                ushort r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                if (Bad(r) || Bad(g) || Bad(b))
                {
                    if (bad++ == 0)
                    {
                        badX = i % w;
                        badY = i / w;
                    }
                    continue;
                }
                if (r >= markBits && (r & 0x8000) == 0)
                {
                    var layer = Mathf.Clamp(Mathf.RoundToInt(Mathf.HalfToFloat(b)) - 1, 0, 2);
                    var mask = Mathf.RoundToInt(Mathf.HalfToFloat(g));
                    frameLayers[layer]++;
                    for (int s = 0; s < 6; s++)
                        if ((mask & (1 << s)) != 0)
                            frameSteps[s]++;
                    int x = i % w, y = i / w;
                    // (The crop centres on the first marker that is not a number, else the first marker.)
                    if (markX < 0 || ((mask & 16) != 0 && (markMask & 16) == 0))
                    {
                        markX = x;
                        markY = y;
                        markMask = mask;
                    }
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                else if ((r & 0x7FFF) > capBits || (g & 0x7FFF) > capBits || (b & 0x7FFF) > capBits)
                    hot++;
            }
            var marked = frameLayers[0] + frameLayers[1] + frameLayers[2];
            for (int k = 0; k < 3; k++)
                layers[k] += frameLayers[k];
            for (int s = 0; s < 6; s++)
                steps[s] += frameSteps[s];
            otherBad += bad;
            otherHot += hot;
            framesDone++;
            if (marked > 0)
            {
                frameHits++;
                if (hitFrames.Length < 400)
                    hitFrames.Append(' ').Append(Time.frameCount);
            }
            Log($"[sparkle] {label} f{Time.frameCount} creature marked {marked} (body {frameLayers[0]}, shell1 {frameLayers[1]}, shell2 {frameLayers[2]}; " +
                      $"steps {Steps(frameSteps)}) other bad {bad} hot {hot}" + (marked > 0 ? $" at x {minX}-{maxX} y {minY}-{maxY}, first x {markX} y {markY}" : "") +
                (bad > 0 ? $" bad first at x {badX} y {badY}" : "") + (segBad > 0 ? $"; segments odd {segBad}, first {segFirst}" : ""));
            segBad = 0;
            if ((marked > 0 || bad > 0) && crops < MaxCrops && cropDir != null)
                Crop(w, h, marked > 0 ? markX : badX, marked > 0 ? markY : badY);
        }

        static string Steps(System.Collections.Generic.IList<int> s) => $"vert {s[0]} norm {s[1]} bump {s[2]} lit {s[3]} final {s[4]} hot {s[5]}";
        static string Steps(long[] s) => $"vert {s[0]} norm {s[1]} bump {s[2]} lit {s[3]} final {s[4]} hot {s[5]}";

        // The first frames with markers: the marked picture (markers magenta) beside the normal one, cropped round them.
        void Crop(int w, int h, int cx, int cy)
        {
            var image = new Texture2D(CropSize * CropScale * 2 + 4, CropSize * CropScale, TextureFormat.RGB24, false);
            var x0 = Mathf.Clamp(cx - CropSize / 2, 0, w - CropSize);
            var y0 = Mathf.Clamp(cy - CropSize / 2, 0, h - CropSize);
            var fill = new Color32[image.width * image.height];
            for (int i = 0; i < fill.Length; i++)
                fill[i] = new Color32(40, 40, 40, 255);
            image.SetPixels32(fill);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                    Render(false, true);
                var px = read.GetRawTextureData<ushort>();
                for (int y = 0; y < CropSize; y++)
                for (int x = 0; x < CropSize; x++)
                {
                    var i = ((y0 + y) * w + x0 + x) * 4;
                    Color c;
                    if (Bad(px[i]) || Bad(px[i + 1]) || Bad(px[i + 2]))
                        c = Color.cyan;
                    else if (Mathf.HalfToFloat(px[i]) >= Marker)
                        c = Color.magenta;
                    else
                        c = new Color(Mathf.HalfToFloat(px[i]), Mathf.HalfToFloat(px[i + 1]), Mathf.HalfToFloat(px[i + 2])).gamma;
                    for (int sy = 0; sy < CropScale; sy++)
                    for (int sx = 0; sx < CropScale; sx++)
                        image.SetPixel(pass * (CropSize * CropScale + 4) + x * CropScale + sx, y * CropScale + sy, c);
                }
            }
            image.Apply();
            System.IO.Directory.CreateDirectory(cropDir);
            var path = System.IO.Path.Combine(cropDir, $"sparkle-{label}-{++crops}-f{Time.frameCount}.png");
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            Destroy(image);
            Log($"[sparkle] {label} crop {path} (left marked: magenta creature marker, cyan bad elsewhere; right the normal picture)");
        }

        void Finish()
        {
            if (label == null || framesDone == 0)
                return;
            Log($"[sparkle] {label} summary: {framesDone} frames, {frameHits} with markers; creature marked {layers[0] + layers[1] + layers[2]} " +
                      $"(body {layers[0]}, shell1 {layers[1]}, shell2 {layers[2]}; steps {Steps(steps)}); other bad {otherBad} hot {otherHot}; frames:{hitFrames}");
            label = null;
            framesLeft = 0;
        }

        void OnDestroy()
        {
            Finish();
            if (rt != null)
                rt.Release();
        }
    }
}
