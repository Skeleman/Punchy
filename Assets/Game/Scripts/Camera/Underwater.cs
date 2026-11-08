using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Punchy.CameraRig
{
    /// <summary>
    /// The camera under water (#22, P-19, first slice): when the lens is in a body of water (the sea, a lake or a river:
    /// TerrainSource.SampleGround's water, between its bed and its surface; or a cave stream or pool:
    /// ThirdPersonCamera.CaveWaterAbove), a blue-green fog with short visibility, a tint over the screen, and the sound
    /// muffled (a low-pass filter on the listener). Going in blends over <see cref="BlendIn"/> s; coming out, the fog and
    /// the teal sky go at once and only the tint and muffling fade, over <see cref="BlendOut"/> s (playtest 11-08 item 7:
    /// surfacing was slow, the sky stayed teal and the world popped in as the fog thinned). A few centimetres between
    /// going in and coming out, so a lens bobbing at the waterline doesn't flicker. Water surfaces are only read; the
    /// surface over the lens goes to the shaders (_PunchyUnderwaterSurface) for Snell's window near the top (item 8).
    /// Runs after WeatherSystem sets the fog each frame and after the camera has moved.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class Underwater : MonoBehaviour
    {
        const float BlendIn = 0.25f, BlendOut = 0.15f, Margin = 0.04f, Cutoff = 700f;
        static readonly Color FogColour = new Color(0.04f, 0.22f, 0.25f);
        static readonly Color Tint = new Color(0.08f, 0.32f, 0.36f, 0.32f);
        const float FogDensity = 0.11f;
        /// <summary>
        /// P-20: the fog terrain and flora get under water (Sky.hlsl PunchyAtmosphere reads _PunchyUnderwater: rgb colour,
        /// w density per metre). RenderSettings fog only reaches the shaders that use Unity's own fog (water, rain, fire).
        /// </summary>
        const float WaterFogDensity = 0.07f;
        static readonly int UnderwaterId = Shader.PropertyToID("_PunchyUnderwater");
        /// <summary>Item 8: x the water surface over the lens (m, with the swell), y how deep the lens is under it; 0 when dry.</summary>
        static readonly int SurfaceId = Shader.PropertyToID("_PunchyUnderwaterSurface");
        // The scene's own fog, kept while under water and put back on surfacing (it used to stay on afterwards).
        bool fogSaved, savedFog;
        FogMode savedMode;
        Color savedColour;
        float savedDensity;

        bool under;
        float amount;
        AudioLowPassFilter lowPass;
        EntityQuery terrainQuery;
        World queryWorld;
        GUIStyle none;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Attach();
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        static void OnLoaded(Scene s, LoadSceneMode m) => Attach();

        static void Attach()
        {
            var rig = FindAnyObjectByType<ThirdPersonCamera>();
            if (rig != null && rig.GetComponent<Underwater>() == null)
                rig.gameObject.AddComponent<Underwater>();
        }

        Camera cam;
        int steppedFrame = -1;

        // Driven from the render loop (once a frame, before the first camera draws), not LateUpdate: a posed shot
        // camera (DebugControls.PoseCamera) disables the rig's behaviours, and the sea's swell, the water's quality
        // policy and the underwater look must still follow it, or water shots show a flat, unreflecting sea.
        void OnEnable()
        {
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            RenderPipelineManager.beginContextRendering += BeforeRender;
        }

        void OnDestroy() => RenderPipelineManager.beginContextRendering -= BeforeRender;

        void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (this == null || steppedFrame == Time.frameCount)
                return;
            steppedFrame = Time.frameCount;
            Step();
        }

        void Step()
        {
            // The water's quality policy for this frame (refactor 3.4), from the camera that draws it.
            // (Unity's missing component is a fake null, so not ??.)
            if (cam == null)
                cam = GetComponentInChildren<Camera>();
            if (cam == null)
                cam = Camera.main;
            Punchy.Settings.WaterQuality.Publish(cam);
            var surface = SurfaceAt(transform.position, out var kind);
            var y = transform.position.y;
            var was = under;
            under = surface > float.MinValue && (under ? y < surface + Margin : y < surface - Margin);
            if (under != was)
                Debug.Log($"Underwater: {(under ? "in" : "out of")} the water ({kind}, lens {y:F2}, surface {(surface > float.MinValue ? surface.ToString("F2") : "-")}); low-pass {(under ? $"on ({Cutoff:F0} Hz)" : "off")}");
            amount = Mathf.MoveTowards(amount, under ? 1f : 0f, Time.deltaTime / (under ? BlendIn : BlendOut));
            // The water's fog and teal sky only while the lens is in the water: out of it, nothing is between the eye and
            // the world any more, so they go at once (item 7); the tint (water running off the lens) and muffling fade.
            var fog = under ? amount : 0f;
            if (fog > 0f)
            {
                if (!fogSaved)
                    (fogSaved, savedFog, savedMode, savedColour, savedDensity) =
                        (true, RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(savedColour, FogColour, fog);
                RenderSettings.fogDensity = Mathf.Lerp(savedFog ? savedDensity : 0f, FogDensity, fog);
            }
            else if (fogSaved)
            {
                (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity) =
                    (savedFog, savedMode, savedColour, savedDensity);
                fogSaved = false;
            }
            // (Shaders work in linear colour; the density blends with the rest, so the fog thins in as the lens sinks.)
            var water = FogColour.linear;
            Shader.SetGlobalVector(UnderwaterId, fog > 0f ? new Vector4(water.r, water.g, water.b, WaterFogDensity * fog) : Vector4.zero);
            Shader.SetGlobalVector(SurfaceId, under ? new Vector4(surface, Mathf.Max(surface - y, 0f), 0f, 0f) : Vector4.zero);
            Muffle();
        }

        /// <summary>A low-pass filter on the listener, its cutoff blended with the rest (off when dry).</summary>
        void Muffle()
        {
            if (lowPass == null)
            {
                var listener = FindAnyObjectByType<AudioListener>();
                if (listener == null)
                    return;
                // Unity's missing component is a fake null, so ?? would never add the filter.
                lowPass = listener.GetComponent<AudioLowPassFilter>();
                if (lowPass == null)
                    lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
            }
            lowPass.enabled = amount > 0f;
            lowPass.cutoffFrequency = Mathf.Lerp(22000f, Cutoff, amount * amount);
        }

        /// <summary>The surface of the water the point is in (float.MinValue: not in water), and what kind it is.</summary>
        float SurfaceAt(Vector3 p, out string kind)
        {
            kind = "none";
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return float.MinValue;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return float.MinValue;
            // The sea swell's state for this frame, for the water shader and the swimmer (WaterSwells).
            WaterSwells.Publish(terrain);
            var at = new float3(p.x, p.y, p.z);
            if (terrain.Caves.IsCreated)
            {
                var cave = ThirdPersonCamera.CaveWaterAbove(ref terrain.Caves.Value, at);
                if (cave > float.MinValue)
                {
                    kind = "cave water";
                    return cave;
                }
            }
            var ground = terrain.SampleGround(at.xz);
            // (In the water column: above its bed, and not deep in rock under it, a cave under a lake.)
            // (Up to 2.5 m over the flat surface: a swell's crest can close over the lens.)
            if (ground.HasWater && p.y > ground.Height - 0.5f && p.y < ground.WaterSurface + 2.5f)
            {
                kind = math.abs(ground.WaterSurface - terrain.World.SeaLevel) < 0.05f ? "the sea" : "a lake or river";
                return ground.WaterSurface + WaterSwells.Height(at.xz, ground.WaterSurface, ground.Height);
            }
            return float.MinValue;
        }

        // P-20 (profiler finding 11): drawn through a GuiHook, on only while there is something to draw, so this
        // script stays out of Unity's IMGUI pass (and its per-frame garbage) the rest of the time.
        void Start()
        {
            Punchy.UI.GuiHook.Add(gameObject, DrawGui, () => amount > 0f, false);
        }

        void DrawGui()
        {
            if (amount <= 0f || Event.current.type != EventType.Repaint)
                return;
            var c = GUI.color;
            GUI.color = new Color(Tint.r, Tint.g, Tint.b, Tint.a * amount);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }

        void OnDisable()
        {
            if (lowPass != null)
                lowPass.enabled = false;
        }
    }
}
