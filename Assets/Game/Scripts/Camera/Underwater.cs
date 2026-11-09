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
    /// The look follows the fluid the lens is in (playtest 11-08 item 36), from one table, <see cref="Fluids"/>: water,
    /// a poison swamp mostly green, lava orange; the more opaque the fluid, the heavier and shorter its fog; deeper is
    /// darker, and light shafts stream down from the surface (item 38, Sky.hlsl PunchyUnderwaterShafts).
    /// Runs after WeatherSystem sets the fog each frame and after the camera has moved.
    /// </summary>
    [DefaultExecutionOrder(1100)]
    public class Underwater : MonoBehaviour
    {
        const float BlendIn = 0.25f, BlendOut = 0.15f, Margin = 0.04f;

        /// <summary>How the view looks from inside one fluid.</summary>
        struct FluidLook
        {
            /// <summary>The fog's colour (sRGB) and the tint laid over the screen (alpha: how strongly).</summary>
            public Color Fog, Tint;
            /// <summary>
            /// Fog per metre: Unity's own (water, rain and fire shaders) and the shaders' through PunchyAtmosphere
            /// (Sky.hlsl reads _PunchyUnderwater: rgb colour, w density per metre; P-20: terrain, flora, rocks).
            /// </summary>
            public float UnityFog, ShaderFog;
            /// <summary>The listener's low-pass cutoff (Hz).</summary>
            public float Cutoff;
            /// <summary>How fast the fog darkens with the lens's depth under the surface (per metre; light from above fades).</summary>
            public float Darken;
            /// <summary>Item 38: how bright the light shafts from the surface are (0 none), and how fast they fade with
            /// depth under the surface (per metre).</summary>
            public float Shafts, ShaftFade;

            public static FluidLook Lerp(FluidLook a, FluidLook b, float t) => new FluidLook
            {
                Fog = Color.Lerp(a.Fog, b.Fog, t), Tint = Color.Lerp(a.Tint, b.Tint, t),
                UnityFog = Mathf.Lerp(a.UnityFog, b.UnityFog, t), ShaderFog = Mathf.Lerp(a.ShaderFog, b.ShaderFog, t),
                Cutoff = Mathf.Lerp(a.Cutoff, b.Cutoff, t), Darken = Mathf.Lerp(a.Darken, b.Darken, t),
                Shafts = Mathf.Lerp(a.Shafts, b.Shafts, t), ShaftFade = Mathf.Lerp(a.ShaftFade, b.ShaftFade, t)
            };
        }

        enum Fluid { Water, Swamp, Lava }

        /// <summary>
        /// One row per fluid (item 36), the more opaque the heavier the fog and the shorter the view: water a blue-green
        /// murk (as before), a poison swamp mostly green, lava orange and nearly blind. Mixed fluids blend their rows.
        /// </summary>
        static readonly FluidLook[] Fluids =
        {
            new FluidLook { Fog = new Color(0.04f, 0.22f, 0.25f), Tint = new Color(0.08f, 0.32f, 0.36f, 0.32f), UnityFog = 0.11f, ShaderFog = 0.07f, Cutoff = 700f, Darken = 0.035f, Shafts = 1f, ShaftFade = 0.06f },
            new FluidLook { Fog = new Color(0.09f, 0.19f, 0.03f), Tint = new Color(0.16f, 0.30f, 0.05f, 0.5f), UnityFog = 0.3f, ShaderFog = 0.2f, Cutoff = 450f, Darken = 0.08f, Shafts = 0.3f, ShaftFade = 0.2f },
            new FluidLook { Fog = new Color(0.95f, 0.32f, 0.04f), Tint = new Color(0.9f, 0.3f, 0.03f, 0.75f), UnityFog = 1.6f, ShaderFog = 1.1f, Cutoff = 250f, Darken = 0f, Shafts = 0f, ShaftFade = 1f },
        };

        /// <summary>The look for the fluid the lens is in: swamp and lava shares blended over water.</summary>
        static FluidLook LookFor(float swamp, float lava) =>
            FluidLook.Lerp(FluidLook.Lerp(Fluids[(int)Fluid.Water], Fluids[(int)Fluid.Swamp], swamp), Fluids[(int)Fluid.Lava], lava);

        // The fluid last seen (kept while surfacing, so the tint fades in its own colour).
        FluidLook look = Fluids[(int)Fluid.Water];
        static readonly int UnderwaterId = Shader.PropertyToID("_PunchyUnderwater");
        /// <summary>Item 8: x the water surface over the lens (m, with the swell), y how deep the lens is under it; 0 when dry.</summary>
        static readonly int SurfaceId = Shader.PropertyToID("_PunchyUnderwaterSurface");
        /// <summary>Item 38: the light shafts under water, x march steps (the water quality setting; 0 off), y brightness,
        /// z fade with depth under the surface (per metre); 0 when dry (Sky.hlsl PunchyUnderwaterShafts).</summary>
        static readonly int ShaftsId = Shader.PropertyToID("_PunchyUnderwaterShafts");
        // The scene's own fog, kept while under water and put back on surfacing (it used to stay on afterwards).
        bool fogSaved, savedFog;
        FogMode savedMode;
        Color savedColour;
        float savedDensity;

        bool under;
        float amount;
        /// <summary>Whether the camera's lens is in the water now (WeatherSystem hides its precipitation then, item 18).</summary>
        public static bool LensUnder { get; private set; }
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

        void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= BeforeRender;
            LensUnder = false;
        }

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
            var surface = SurfaceAt(transform.position, out var kind, out var swamp, out var lava);
            var y = transform.position.y;
            var was = under;
            under = surface > float.MinValue && (under ? y < surface + Margin : y < surface - Margin);
            LensUnder = under;
            if (under != was)
                Debug.Log($"Underwater: {(under ? "in" : "out of")} the water ({kind}, lens {y:F2}, surface {(surface > float.MinValue ? surface.ToString("F2") : "-")}); low-pass {(under ? $"on ({LookFor(swamp, lava).Cutoff:F0} Hz)" : "off")}");
            amount = Mathf.MoveTowards(amount, under ? 1f : 0f, Time.deltaTime / (under ? BlendIn : BlendOut));
            if (under)
                look = LookFor(swamp, lava);
            // The water's fog and teal sky only while the lens is in the water: out of it, nothing is between the eye and
            // the world any more, so they go at once (item 7); the tint (water running off the lens) and muffling fade.
            var fog = under ? amount : 0f;
            // Deeper is darker (item 38): the fog's colour falls off with the lens's depth under the surface, to a floor.
            var fogColour = look.Fog * Mathf.Max(Mathf.Exp(-look.Darken * (under ? Mathf.Max(surface - y, 0f) : 0f)), 0.15f);
            fogColour.a = 1f;
            if (fog > 0f)
            {
                if (!fogSaved)
                    (fogSaved, savedFog, savedMode, savedColour, savedDensity) =
                        (true, RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(savedColour, fogColour, fog);
                RenderSettings.fogDensity = Mathf.Lerp(savedFog ? savedDensity : 0f, look.UnityFog, fog);
            }
            else if (fogSaved)
            {
                (RenderSettings.fog, RenderSettings.fogMode, RenderSettings.fogColor, RenderSettings.fogDensity) =
                    (savedFog, savedMode, savedColour, savedDensity);
                fogSaved = false;
            }
            // (Shaders work in linear colour; the density blends with the rest, so the fog thins in as the lens sinks.)
            var water = fogColour.linear;
            Shader.SetGlobalVector(UnderwaterId, fog > 0f ? new Vector4(water.r, water.g, water.b, look.ShaderFog * fog) : Vector4.zero);
            Shader.SetGlobalVector(SurfaceId, under ? new Vector4(surface, Mathf.Max(surface - y, 0f), 0f, 0f) : Vector4.zero);
            var steps = Punchy.Settings.WaterQuality.UnderwaterShaftSteps;
            Shader.SetGlobalVector(ShaftsId, fog > 0f && steps > 0 && look.Shafts > 0f && !NoShafts
                ? new Vector4(steps, look.Shafts * fog, look.ShaftFade, 0f) : Vector4.zero);
            Muffle();
            Drift(under, lava);
        }

        // ---------------------------------------------------------------- bubbles and motes (item 34)

        ParticleSystem motes, bubbles;
        bool driftShown;
        /// <summary>"-nodrift": no bubbles or specks; "-noshafts": no light shafts; to price them (read once, in Start).</summary>
        bool NoDrift, NoShafts;

        /// <summary>
        /// Item 34 (the cheap part): specks drifting in the water round the lens and now and then a few bubbles rising
        /// past it, while the lens is in water or a swamp (not lava). Two small particle systems on the weather's
        /// precipitation shader (its soft dots, in Unity's fog, so they fade into the fluid's own): at most ~200 tiny
        /// billboards, drawn only under water; out of it they stop and hide at once, like the rain.
        /// </summary>
        void Drift(bool under, float lava)
        {
            var on = under && lava < 0.5f && !NoDrift;
            if (on && motes == null && !MakeDrift())
                return;
            if (motes == null)
                return;
            if (on != driftShown)
            {
                driftShown = on;
                motes.GetComponent<ParticleSystemRenderer>().enabled = on;
                bubbles.GetComponent<ParticleSystemRenderer>().enabled = on;
                if (!on)
                {
                    motes.Clear();
                    bubbles.Clear();
                }
            }
            var position = transform.position;
            motes.transform.position = position;
            // (Bubbles start a little below and ahead of the lens, so they rise through the view.)
            bubbles.transform.position = position + transform.forward * 2.5f - Vector3.up * 1.5f;
            var m = motes.emission;
            m.rateOverTime = on ? 40f : 0f;
            var b = bubbles.emission;
            b.enabled = on;
            // (Specks take the fluid's colour, lightened: blue-green flecks in the sea, green ones in a swamp.)
            var main = motes.main;
            var fleck = Color.Lerp(look.Fog, Color.white, 0.55f);
            fleck.a = 0.55f;
            main.startColor = fleck;
        }

        /// <summary>Makes the two systems on first use (false: no weather system to borrow the shader from yet).</summary>
        bool MakeDrift()
        {
            var weather = FindAnyObjectByType<Punchy.Weather.WeatherSystem>();
            if (weather == null || weather.PrecipitationShader == null)
                return false;
            var material = new Material(weather.PrecipitationShader) { name = "Underwater drift" };
            material.SetFloat("_Shape", 0f);   // soft dots
            motes = MakeSystem("UnderwaterMotes", material, 260, new Vector3(14f, 8f, 14f));
            var main = motes.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.08f);
            var wander = motes.noise;
            wander.enabled = true;
            wander.strength = 0.08f;
            wander.frequency = 0.3f;
            var fade = motes.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            // Bubbles: now and then (every 2-5 s) a little cluster of 3-8 rising and wobbling, gone at the surface or after 4 s.
            bubbles = MakeSystem("UnderwaterBubbles", material, 40, new Vector3(3f, 0.5f, 3f));
            var bm = bubbles.main;
            bm.startLifetime = 4f;
            bm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            bm.startSpeed = 0f;
            bm.startColor = new Color(0.85f, 0.95f, 1f, 0.7f);
            var rise = bubbles.velocityOverLifetime;
            rise.enabled = true;
            rise.space = ParticleSystemSimulationSpace.World;
            rise.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            rise.y = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            rise.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var wobble = bubbles.noise;
            wobble.enabled = true;
            wobble.strength = 0.25f;
            wobble.frequency = 1.5f;
            var be = bubbles.emission;
            be.rateOverTime = 0f;
            be.SetBursts(new[] { new ParticleSystem.Burst(0f, 3, 8, 0, 3.5f) { probability = 0.8f } });
            driftShown = true;
            return true;
        }

        ParticleSystem MakeSystem(string name, Material material, int max, Vector3 box)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform.parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
            ps.Play();
            return ps;
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
            lowPass.cutoffFrequency = Mathf.Lerp(22000f, look.Cutoff, amount * amount);
        }

        /// <summary>
        /// The surface of the water the point is in (float.MinValue: not in water), what kind it is, and how much of it
        /// is swamp (thick, from the water's murk, as Water.shader turns oily) and lava (0..1 each).
        /// </summary>
        float SurfaceAt(Vector3 p, out string kind, out float swamp, out float lava)
        {
            kind = "none";
            swamp = lava = 0f;
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
                // (A cave's lava pool, which the camera's water test leaves out.)
                ref var pools = ref terrain.Caves.Value.Pools;
                for (int i = 0; i < pools.Length; i++)
                {
                    var pool = pools[i];
                    if (pool.Lava > 0.5f && math.distance(at.xz, pool.Centre.xz) < pool.Radius && at.y < pool.Centre.y && at.y > pool.Centre.y - pool.Depth - 1.5f)
                    {
                        kind = "cave lava";
                        lava = 1f;
                        return pool.Centre.y;
                    }
                }
            }
            var ground = terrain.SampleGround(at.xz);
            // (In the water column: above its bed, and not deep in rock under it, a cave under a lake.)
            // (Up to 2.5 m over the flat surface: a swell's crest can close over the lens.)
            if (ground.HasWater && p.y > ground.Height - 0.5f && p.y < ground.WaterSurface + 2.5f)
            {
                kind = math.abs(ground.WaterSurface - terrain.World.SeaLevel) < 0.05f ? "the sea" : "a lake or river";
                var waterLook = terrain.WaterLook(at.xz, ground.Biomes, out var glow);
                lava = math.saturate(glow.y);
                swamp = math.smoothstep(0.55f, 0.9f, waterLook.w);
                if (lava > 0.5f || swamp > 0.5f)
                    kind = lava > 0.5f ? "lava" : "a swamp";
                return ground.WaterSurface + WaterSwells.Height(at.xz, ground.WaterSurface, ground.Height);
            }
            return float.MinValue;
        }

        // P-20 (profiler finding 11): drawn through a GuiHook, on only while there is something to draw, so this
        // script stays out of Unity's IMGUI pass (and its per-frame garbage) the rest of the time.
        void Start()
        {
            Punchy.UI.GuiHook.Add(gameObject, DrawGui, () => amount > 0f, false);
            NoDrift = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-nodrift") >= 0;
            NoShafts = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-noshafts") >= 0;
        }

        void DrawGui()
        {
            if (amount <= 0f || Event.current.type != EventType.Repaint)
                return;
            var c = GUI.color;
            GUI.color = new Color(look.Tint.r, look.Tint.g, look.Tint.b, look.Tint.a * amount);
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
