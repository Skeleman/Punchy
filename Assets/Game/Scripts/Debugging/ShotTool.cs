using System.Collections;
using System.Collections.Generic;
using System.Text;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
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
            var stillFor = 0;
            Vector3 camWas = Vector3.positiveInfinity, playerWas = Vector3.positiveInfinity;
            Quaternion rotWas = Quaternion.identity;
            var lastTime = start;
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
                    Status = State.Failed;
                    yield break;
                }
                yield return null;
            }

            // The shot: this frame's own render of the camera, into the shot's texture.
            var camera = Camera.main;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ShotTool" };
            rt.Create();
            var active = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(1f, 0f, 1f, 1f));   // a marker: still magenta afterwards means the frame didn't render
            RenderTexture.active = active;
            var was = camera.targetTexture;
            camera.targetTexture = rt;
            var frame = Time.frameCount;
            string how;
            Texture2D image;
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
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
            var pos = camera.transform.position;
            LastLog = $"[shot] '{name}' ready after {Time.realtimeSinceStartup - start:F1} s (waited {Summary(waited)}), {how} at frame {frame}, " +
                      $"{width}x{height}, camera ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})";
            Debug.Log(LastLog);
            Status = State.Done;
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
