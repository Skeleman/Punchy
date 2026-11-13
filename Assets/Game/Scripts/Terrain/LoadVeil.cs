using Punchy.Flora;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-151 row 2 (#287): a short veil over the screen while the world round the camera streams in, so a fresh view
    /// doesn't show bare ground filling up with plants and buildings. It covers the first load (and a client's join,
    /// which bakes the island again) and any jump of the camera of more than <see cref="JumpDistance"/> in one frame
    /// (F5-F8 warps, -goto, a respawn or join that lands far away). It snaps on (a fade in would show the very pop it is
    /// for) and fades out once the near ring is in: terrain chunks, buildings, trees and shrubs within
    /// <see cref="NearRadius"/>, or after <see cref="Cap"/> at the latest (counted from when there is an island to show:
    /// while the island bakes there is nothing behind it, so it holds). Ordinary walking, riding or flying never moves
    /// the camera that far in a frame, so it never shows then.
    /// Cosmetic and per player (multiplayer.md): it reads only this machine's camera and streaming, and sends nothing.
    /// "-noveil" turns it off (benches comparing frames). The shot tool waits for it to clear (ShotTool.Ready).
    /// The watch (this LateUpdate: one camera distance a frame) always runs; the drawing lives in
    /// <see cref="LoadVeilPainter"/>, which is enabled only while the veil shows, so no IMGUI pass otherwise (P-20).
    /// </summary>
    public class LoadVeil : MonoBehaviour
    {
        /// <summary>A camera move longer than this in one frame raises the veil.</summary>
        const float JumpDistance = 300f; // streaming: detects a jump (warp/teleport), not a visual range
        /// <summary>The veil lifts once everything within this of the camera is in.</summary>
        const float NearRadius = 150f; // streaming: the ring the veil waits for (W-151 card), not a draw range
        /// <summary>The veil lifts after this long (real seconds) even if the near ring isn't in.</summary>
        const float Cap = 4f;
        /// <summary>Fade out time (real seconds).</summary>
        const float FadeOut = 0.35f;

        static LoadVeil instance;
        static readonly bool Off = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-noveil") >= 0;

        /// <summary>The veil covers the screen at all (fully or fading out).</summary>
        public static bool Showing => instance != null && instance.alpha > 0f;
        /// <summary>Why the veil last went up ("load", "jump 812 m"), for logs and tests.</summary>
        public static string Reason => instance != null ? instance.reason : null;

        float alpha;
        bool up;
        bool holding;
        float upAt = -1f, capFrom = -1f;
        string reason;
        string waiting;
        Vector3 lastEye;
        bool eyeSet;
        bool sawBake;
        FloraSystem flora;
        float nextFind;
        LoadVeilPainter painter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Off || instance != null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            var go = new GameObject("LoadVeil") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LoadVeil>();
        }

        void Awake()
        {
            painter = gameObject.AddComponent<LoadVeilPainter>();
            painter.veil = this;
            Raise("load");
        }

        internal float Alpha => alpha;
        internal bool Holding => holding;

        void Raise(string why)
        {
            if (!up)
                Debug.Log($"[veil] on ({why}) at {Time.realtimeSinceStartup:F1} s since start");
            up = true;
            alpha = 1f;
            upAt = Time.realtimeSinceStartup;
            capFrom = -1f;
            reason = why;
            painter.enabled = true;
        }

        void LateUpdate()
        {
            var now = Time.realtimeSinceStartup;
            // A new island baking (a client's join reloads the scene and bakes again) is a new load.
            var baking = IslandV2.ActiveVersion == 0;
            if (baking && !sawBake)
            {
                sawBake = true;
                if (!up)
                    Raise("load");
            }
            if (!baking)
                sawBake = false;

            var cam = Camera.main;
            if (cam != null)
            {
                var eye = cam.transform.position;
                if (eyeSet && (eye - lastEye).magnitude > JumpDistance)
                    Raise($"jump {(eye - lastEye).magnitude:F0} m");
                lastEye = eye;
                eyeSet = true;
            }

            if (up)
            {
                if (flora == null && now >= nextFind)
                {
                    flora = FindAnyObjectByType<FloraSystem>();   // (a search only while up and missing, twice a second)
                    nextFind = now + 0.5f;
                }
                var world = World.DefaultGameObjectInjectionWorld;
                var terrain = world != null && world.IsCreated ? world.GetExistingSystemManaged<TerrainStreamingSystem>() : null;
                // Nothing to show yet: the island is baking, or its flora tables aren't built. The cap waits.
                holding = baking || cam == null || flora == null || !flora.Built || terrain == null;
                if (holding)
                {
                    waiting = baking ? "island baking" : cam == null ? "no camera" : flora == null || !flora.Built ? "flora not built" : "no terrain";
                    capFrom = -1f;
                }
                else
                {
                    if (capFrom < 0f)
                        capFrom = now;
                    var eye = cam.transform.position;
                    var chunks = terrain.MissingNear(new float2(eye.x, eye.z), NearRadius);
                    var ready = chunks == 0 && flora.NearReady(eye, NearRadius, out waiting);
                    if (chunks > 0)
                        waiting = $"terrain {chunks} chunks";
                    var capped = now - capFrom >= Cap;
                    if (ready || capped)
                    {
                        up = false;
                        Debug.Log($"[veil] off ({reason}) after {now - upAt:F2} s ({(ready ? "near ring in" : $"cap, still waiting on {waiting}")}" +
                                  $"{(now - capFrom < now - upAt - 0.01f ? $"; {now - capFrom:F2} s after the island was up" : "")})");
                    }
                }
            }
            if (!up && alpha > 0f)
                alpha = math.max(0f, alpha - Time.unscaledDeltaTime / FadeOut);
            if (!up && alpha <= 0f && painter.enabled)
                painter.enabled = false;   // nothing to draw: out of the IMGUI pass (P-20) until the next jump
        }
    }

    /// <summary>Draws the <see cref="LoadVeil"/> (IMGUI). Enabled only while the veil shows, so it costs no IMGUI pass
    /// the rest of the time.</summary>
    public class LoadVeilPainter : MonoBehaviour
    {
        internal LoadVeil veil;
        Texture2D pixel;
        GUIStyle label;

        void Awake() => useGUILayout = false;

        void OnGUI()
        {
            var alpha = veil != null ? veil.Alpha : 0f;
            if (alpha <= 0f || Event.current.type != EventType.Repaint || Punchy.UI.MainMenu.Open)
                return;
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            // The fog's colour: what the far land fades into, so the veil reads as the view hazing over.
            var c = RenderSettings.fogColor;
            if (!(c.r >= 0f && c.g >= 0f && c.b >= 0f))   // (NaN-safe: a bad colour never reaches the screen)
                c = new Color(0.62f, 0.72f, 0.82f);
            GUI.depth = -1000;
            var was = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), pixel);
            if (veil.Holding)
            {
                label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height / 36f) };
                label.normal.textColor = new Color(1f, 1f, 1f, 0.8f * alpha);
                GUI.color = Color.white;
                GUI.Label(new Rect(0f, Screen.height * 0.45f, Screen.width, Screen.height * 0.1f), "Raising the island...", label);
            }
            GUI.color = was;
        }

        void OnDestroy()
        {
            if (pixel != null)
                Destroy(pixel);
        }
    }
}
