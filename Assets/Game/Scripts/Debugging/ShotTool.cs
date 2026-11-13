using System.Collections;
using System.Collections.Generic;
using System.Text;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// T-1: the one screenshot path every lane uses (play-mode tests through Shots.Settled, "-shotlist" in the editor and
    /// in player builds). A shot waits at a ready gate, then is taken from the real camera's own frame.
    ///
    /// Why: a manual camera.Render() into a texture in the middle of a frame (the old Shots.Take) misses what is drawn
    /// through the normal frame: the flora's indirect draws are queued in LateUpdate for that frame's cameras, and the
    /// far-tree shadow cache is laid over several frames, so trees came out missing or as dark shapes. Here the camera
    /// renders one ordinary frame into the shot's texture instead (its targetTexture set for that frame), so the picture
    /// is what the player sees, minus the screen-space HUD.
    ///
    /// The gate waits until: the camera and the player have held still for a few frames (the flora cull and the terrain
    /// geomorph follow them), the terrain has no chunk wanted or meshing, the flora has every tile in range placed and
    /// uploaded and no shadow-cache redraw under way, and the caves round the camera are built. It logs what it waited on
    /// and for how long; after the timeout it fails loudly ("[shot] FAIL ... waiting on ...") and writes no file, rather
    /// than shoot a half-built world.
    ///
    /// Each shot then gets a written check in its log line (mean brightness, near-black and near-white shares, sky share,
    /// and for a "-frame" shot whether the subject is on-screen with its centre ray clear). A black, blown-out, under-ground
    /// or subject-hidden shot is retried once at another heading, then kept and marked BAD with its reason (LastBad).
    /// With PUNCHY_GALLERY set, every shot is also copied there at full size; agents look only at a ShotSheet.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class ShotTool : MonoBehaviour
    {
        public enum State { Idle, Waiting, Done, Failed }

        /// <summary>The last request's state; tests poll it.</summary>
        public static State Status { get; private set; } = State.Idle;
        /// <summary>The last request's log line (the ready report, or the failure).</summary>
        public static string LastLog { get; private set; } = "";
        public static string LastPath { get; private set; } = "";
        public static bool Busy => Status == State.Waiting;

        /// <summary>Frames the camera and player must hold still (and the gate stay open) before the shot.</summary>
        public const int StillFrames = 6;
        /// <summary>Default gate timeout, real seconds.</summary>
        public const float DefaultTimeout = 60f;

        /// <summary>Something else the gate waits on (a "-frame" still placing the camera): its reason, or null.</summary>
        public static System.Func<string> ExtraWait;

        /// <summary>
        /// T-1 row 3: the next shot's subject check, set by "-frame": null when its subject is on-screen and its centre ray
        /// is clear, else the reason ("target off-screen", "target hidden by terrain"). Cleared after each shot.
        /// </summary>
        public static System.Func<Camera, string> TargetCheck;
        /// <summary>The next shot's retry, set by "-frame": re-poses at its next-best heading and says where, or null. Cleared after each shot.</summary>
        public static System.Func<string> Retry;

        /// <summary>The last shot's written check (brightness, near-black and near-white shares, sky, target).</summary>
        public static string LastCheck { get; private set; } = "";
        /// <summary>Why the last shot is BAD after its retry (black, blown out, under ground, target hidden...), or null when good.</summary>
        public static string LastBad { get; private set; }

        /// <summary>One shot written this session, for a contact sheet (<see cref="ShotSheet"/>).</summary>
        public struct Taken
        {
            public string Path, Check, Bad;
        }
        /// <summary>Every shot written since the last <see cref="ClearTaken"/>.</summary>
        public static readonly List<Taken> Written = new List<Taken>();
        public static void ClearTaken() => Written.Clear();

        static ShotTool runner;
        Camera lateRender;

        // Batch mode only: render the shot's camera after every other LateUpdate this frame (see the order attribute).
        void LateUpdate()
        {
            if (lateRender == null)
                return;
            // (ECS jobs from this frame's systems may still be writing transforms; the culling reads them.)
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                world.EntityManager.CompleteAllTrackedJobs();
            lateRender.Render();
            lateRender = null;
        }

        /// <summary>
        /// Starts a shot of Camera.main into path (a PNG, width x height). Returns false if one is already under way.
        /// Poll <see cref="Status"/> for Done or Failed. still: false when the camera is meant to move (following a creature,
        /// a walk); the gate then waits only for the world to be in.
        /// </summary>
        public static bool Take(string path, int width = 1280, int height = 720, float timeout = DefaultTimeout, bool still = true)
        {
            if (Busy)
                return false;
            if (runner == null)
            {
                var go = new GameObject("ShotTool") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                runner = go.AddComponent<ShotTool>();
            }
            Status = State.Waiting;
            LastPath = path;
            LastLog = "";
            LastCheck = "";
            LastBad = null;
            runner.StartCoroutine(runner.Run(path, width, height, timeout, still));
            return true;
        }

        /// <summary>
        /// One check of the gate for this camera: true when everything is in, else false with what it waits on.
        /// </summary>
        public static bool Ready(Camera cam, out string waiting)
        {
            waiting = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                waiting = "no world";
                return false;
            }
            if (ExtraWait?.Invoke() is string extra)
            {
                waiting = extra;
                return false;
            }
            if (LoadVeil.Showing)
            {
                waiting = "load veil";   // W-151: never a picture of the veil (or of it fading out)
                return false;
            }
            var terrain = world.GetExistingSystemManaged<TerrainStreamingSystem>();
            if (terrain != null && !terrain.ShotIdle)
            {
                waiting = $"terrain {terrain.ShotPending} chunks";
                return false;
            }
            var flora = FindAnyObjectByType<FloraSystem>();
            if (flora != null && !flora.ShotReady(cam.transform.position, out waiting))
                return false;
            var caves = FindAnyObjectByType<CaveSystem>();
            if (caves != null && CaveSystem.Underground > 0.01f && !caves.Settled)
            {
                waiting = "caves";
                return false;
            }
            return true;
        }

        static bool PlayerAt(out Vector3 at)
        {
            at = Vector3.zero;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (!q.TryGetSingleton<LocalTransform>(out var t))
                return false;
            at = t.Position;
            return true;
        }

        IEnumerator Run(string path, int width, int height, float timeout, bool still)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var start = Time.realtimeSinceStartup;
            var waited = new Dictionary<string, float>();
            string retried = null, firstBad = null;
            Texture2D image = null;
            Camera camera = null;
            string how = null, check = null, bad = null;
            var frame = 0;
            for (int attempt = 0; ; attempt++)
            {
                var stillFor = 0;
                Vector3 camWas = Vector3.positiveInfinity, playerWas = Vector3.positiveInfinity;
                Quaternion rotWas = Quaternion.identity;
                var lastTime = Time.realtimeSinceStartup;
                while (true)
                {
                    var now = Time.realtimeSinceStartup;
                    var dt = now - lastTime;
                    lastTime = now;
                    var cam = Camera.main;
                    string waiting;
                    if (cam == null)
                        waiting = "no camera";
                    else
                    {
                        var moved = (cam.transform.position - camWas).sqrMagnitude > 1e-4f || Quaternion.Angle(cam.transform.rotation, rotWas) > 0.05f;
                        var hasPlayer = PlayerAt(out var p);
                        moved |= hasPlayer && (p - playerWas).sqrMagnitude > 0.0025f;
                        camWas = cam.transform.position;
                        rotWas = cam.transform.rotation;
                        playerWas = p;
                        moved &= still;
                        if (!Ready(cam, out waiting) || moved)
                        {
                            stillFor = 0;
                            waiting ??= "camera or player moving";
                        }
                        else if (++stillFor < StillFrames)
                            waiting = "settling";
                    }
                    if (waiting == null)
                        break;
                    var key = waiting.Split(' ')[0] == "flora" && waiting.Contains("tiles") ? "flora tiles" : StripNumbers(waiting);
                    waited[key] = (waited.TryGetValue(key, out var s) ? s : 0f) + dt;
                    if (now - start > timeout)
                    {
                        LastLog = $"[shot] FAIL '{name}' not ready after {timeout:F0} s, waiting on: {waiting} (waited {Summary(waited)})";
                        Debug.LogWarning(LastLog);
                        TargetCheck = null;
                        Retry = null;
                        Status = State.Failed;
                        yield break;
                    }
                    yield return null;
                }

                // The shot: this frame's own render of the camera, into the shot's texture.
                camera = Camera.main;
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ShotTool" };
                rt.Create();
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(1f, 0f, 1f, 1f));   // a marker: still magenta afterwards means the frame didn't render
                RenderTexture.active = active;
                var was = camera.targetTexture;
                camera.targetTexture = rt;
                frame = Time.frameCount;
                if (image != null)
                    Object.Destroy(image);
                if (Application.isBatchMode)
                {
                    // Batch mode renders no cameras in the player loop and never resumes WaitForEndOfFrame: render the camera
                    // by hand from this component's LateUpdate, which runs after the flora's LateUpdate has queued its draws.
                    lateRender = camera;
                    while (lateRender != null)
                        yield return null;
                    image = Read(rt);
                    how = "late render";
                }
                else
                {
                    yield return new WaitForEndOfFrame();
                    how = "frame";
                    image = Read(rt);
                    if (Unrendered(image))
                    {
                        camera.Render();   // (a frame that drew no camera: render it now, its draws are queued)
                        Object.Destroy(image);
                        image = Read(rt);
                        how = "manual render";
                    }
                }
                camera.targetTexture = was;
                rt.Release();
                Object.Destroy(rt);

                // T-1 row 3: the written check. A bad shot gets one retry at another heading, then is kept and marked BAD.
                check = Check(camera, image, out bad);
                if (bad == null || attempt > 0)
                    break;
                firstBad = bad;
                retried = Retry != null ? Retry() : TurnPosedCamera(camera);
                if (retried == null)
                    break;
                Debug.Log($"[shot] '{name}' {bad} ({check}): retrying, {retried}");
            }
            TargetCheck = null;
            Retry = null;

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            var png = image.EncodeToPNG();
            System.IO.File.WriteAllBytes(path, png);
            Object.Destroy(image);
            // Full-resolution shots also go to the gallery folder named by PUNCHY_GALLERY, if set (for people, not agents).
            var gallery = System.Environment.GetEnvironmentVariable("PUNCHY_GALLERY");
            if (!string.IsNullOrEmpty(gallery))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(gallery);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(gallery, System.IO.Path.GetFileName(path)), png);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[shot] gallery copy failed: {e.Message}");
                }
            }
            LastCheck = check;
            LastBad = bad;
            Written.Add(new Taken { Path = path, Check = check, Bad = bad });
            var pos = camera.transform.position;
            var verdict = bad != null ? $"BAD {bad}" + (retried != null ? $" (also after retry, first {firstBad})" : " (no retry: camera not posed)")
                        : retried != null ? $"good after retry (first {firstBad}, {retried})" : "good";
            LastLog = $"[shot] '{name}' ready after {Time.realtimeSinceStartup - start:F1} s (waited {Summary(waited)}), {how} at frame {frame}, " +
                      $"{width}x{height}, camera ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}); check: {check}; {verdict}";
            if (bad != null)
                Debug.LogWarning(LastLog);
            else
                Debug.Log(LastLog);
            Status = State.Done;
        }

        /// <summary>
        /// The written check of a shot: mean brightness, near-black (luminance under 0.04) and near-white (every channel
        /// over 0.96) shares of the pixels, the sky share (a 16x9 grid of camera rays that meet no ground within 3 km),
        /// whether the lens is under the ground, and the subject check if "-frame" set one. bad: the first reason it is
        /// unusable, or null. Night and caves are allowed to be dark; a lot of sky only counts against a framed subject.
        /// </summary>
        static string Check(Camera cam, Texture2D image, out string bad)
        {
            bad = null;
            var px = image.GetPixels32();
            double sum = 0;
            int black = 0, white = 0, n = 0;
            for (int i = 0; i < px.Length; i += 5)
            {
                var c = px[i];
                var lum = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
                sum += lum;
                if (lum < 0.04f)
                    black++;
                if (c.r > 245 && c.g > 245 && c.b > 245)
                    white++;
                n++;
            }
            var mean = (float)(sum / math.max(n, 1));
            var blackShare = black / (float)math.max(n, 1);
            var whiteShare = white / (float)math.max(n, 1);
            var cycle = FindAnyObjectByType<DayNightCycle>();
            var night = cycle != null ? cycle.Night : 0f;
            var cave = CaveSystem.Underground;
            var sky = -1f;
            var under = false;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                if (q.TryGetSingleton<TerrainSource>(out var source))
                {
                    float3 eye = cam.transform.position;
                    if (eye.y < source.SampleHeight(eye.xz) - 0.2f)
                    {
                        source.FloorUnder(eye, out var inCave);
                        under = !inCave;
                    }
                    sky = SkyShare(cam, source);
                }
            }
            var target = TargetCheck?.Invoke(cam);
            var text = Invariant($"mean {mean:F2}, near-black {blackShare:F2}, near-white {whiteShare:F2}, sky {sky:F2}") +
                       ", " + (TargetCheck == null ? "target none set" : target ?? "target on-screen and clear") +
                       (night > 0.5f ? ", night" : "") + (cave > 0.5f ? ", in a cave" : "");
            if (under)
                bad = "inside geometry (lens under the ground)";
            else if (night < 0.5f && cave < 0.5f ? mean < 0.06f || blackShare > 0.6f : mean < 0.01f && blackShare > 0.98f)
                bad = "black";
            else if (whiteShare > 0.4f)
                bad = "blown out";
            else if (target != null)
                bad = target;
            else if (TargetCheck != null && sky > 0.9f)
                bad = "all sky";
            return text;
        }

        static string Invariant(System.FormattableString s) => System.FormattableString.Invariant(s);

        /// <summary>The share of a 16x9 grid of camera rays that meet no ground within 3 km (ground: under SampleHeight, out of a cave).</summary>
        static float SkyShare(Camera cam, in TerrainSource source)
        {
            var open = 0;
            for (int y = 0; y < 9; y++)
            for (int x = 0; x < 16; x++)
            {
                var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / 16f, (y + 0.5f) / 9f, 0f));
                float3 o = ray.origin, d = ray.direction;
                var hit = false;
                for (float t = 1f; t < 3000f; t += math.max(1f, t * 0.03f))
                {
                    var p = o + d * t;
                    if (p.y < source.SampleHeight(p.xz))
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit)
                    open++;
            }
            return open / 144f;
        }

        /// <summary>
        /// A retry for a shot with no "-frame": a camera posed by a flag (nothing on it enabled) turns 90 degrees in heading
        /// and, if under the ground, rises over it. A live camera (the player's, a creature watch) isn't moved: null.
        /// </summary>
        static string TurnPosedCamera(Camera cam)
        {
            foreach (var b in cam.GetComponents<MonoBehaviour>())
                if (b.enabled)
                    return null;
            var t = cam.transform;
            var pos = t.position;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                if (q.TryGetSingleton<TerrainSource>(out var source))
                    pos.y = math.max(pos.y, source.SampleHeight(((float3)pos).xz) + 2f);
            }
            t.SetPositionAndRotation(pos, Quaternion.Euler(0f, 90f, 0f) * t.rotation);
            return Invariant($"camera turned 90 deg to heading {t.eulerAngles.y:F0}");
        }

        static Texture2D Read(RenderTexture rt)
        {
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            return image;
        }

        /// <summary>Every sampled pixel still the magenta marker.</summary>
        static bool Unrendered(Texture2D image)
        {
            var px = image.GetPixels32();
            for (int i = 0; i < px.Length; i += 97)
                if (px[i].r != 255 || px[i].g != 0 || px[i].b != 255)
                    return false;
            return true;
        }

        static string StripNumbers(string s)
        {
            var sb = new StringBuilder();
            foreach (var part in s.Split(' '))
                if (part.Length > 0 && !char.IsDigit(part[0]) && part[0] != '(')
                    sb.Append(sb.Length > 0 ? " " : "").Append(part);
            return sb.ToString();
        }

        static string Summary(Dictionary<string, float> waited)
        {
            if (waited.Count == 0)
                return "nothing";
            var sb = new StringBuilder();
            foreach (var kv in waited)
                sb.Append(sb.Length > 0 ? ", " : "").Append($"{kv.Key} {kv.Value:F1} s");
            return sb.ToString();
        }
    }
}
