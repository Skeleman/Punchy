using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace Punchy.Weather
{
    /// <summary>
    /// Drives sky, fog, sunlight, ambient light, wind, wetness and precipitation from a list of weather presets,
    /// drifting from one to the next over time. Everything that reads the weather (sky, water and terrain shaders)
    /// gets it through shader globals set here, so they always agree. Fog density never drops below what's needed to
    /// hide the edge of the streamed terrain, and the fog colour is the sky's horizon colour.
    /// Press [ and ] to step through the presets.
    /// </summary>
    [ExecuteAlways]
    public class WeatherSystem : MonoBehaviour
    {
        public List<WeatherPreset> Presets = new List<WeatherPreset>();
        [Tooltip("How long one weather takes to blend into the next, in seconds.")]
        public float TransitionSeconds = 45f;
        public Light Sun;

        [Header("Sky colours")]
        public Color ClearZenith = new Color(0.20f, 0.42f, 0.85f);
        public Color ClearHorizon = new Color(0.70f, 0.80f, 0.93f);
        public Color OvercastZenith = new Color(0.46f, 0.49f, 0.54f);
        public Color OvercastHorizon = new Color(0.64f, 0.67f, 0.71f);
        public Color GroundColor = new Color(0.36f, 0.37f, 0.39f);

        [Header("Ground wetness")]
        [Tooltip("Seconds of full rain to soak the ground.")]
        public float SoakSeconds = 25f;
        [Tooltip("Seconds for soaked ground to dry out.")]
        public float DrySeconds = 120f;

        [Header("Precipitation")]
        public float RainParticlesPerSecond = 9000f;
        public float SnowParticlesPerSecond = 2500f;
        [Tooltip("Above this height (metres above sea level) precipitation falls as snow.")]
        public float SnowLine = 140f;

        static readonly int SunDirectionId = Shader.PropertyToID("_PunchySunDirection");
        static readonly int SunColorId = Shader.PropertyToID("_PunchySunColor");
        static readonly int ZenithId = Shader.PropertyToID("_PunchySkyZenith");
        static readonly int HorizonId = Shader.PropertyToID("_PunchySkyHorizon");
        static readonly int GroundId = Shader.PropertyToID("_PunchySkyGround");
        static readonly int CloudsId = Shader.PropertyToID("_PunchyClouds");
        static readonly int CloudOffsetId = Shader.PropertyToID("_PunchyCloudOffset");
        static readonly int RainId = Shader.PropertyToID("_PunchyRain");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int WetnessId = Shader.PropertyToID("_PunchyWetness");

        struct State
        {
            public float Coverage, Darkness, Overcast, Sunlight, Fog, Wind, Precipitation;

            public static State From(WeatherPreset p) => new State
            {
                Coverage = p.CloudCoverage, Darkness = p.CloudDarkness, Overcast = p.Overcast, Sunlight = p.Sunlight,
                Fog = p.Fog, Wind = p.Wind, Precipitation = p.Precipitation
            };

            public static State Lerp(in State a, in State b, float t) => new State
            {
                Coverage = math.lerp(a.Coverage, b.Coverage, t), Darkness = math.lerp(a.Darkness, b.Darkness, t),
                Overcast = math.lerp(a.Overcast, b.Overcast, t), Sunlight = math.lerp(a.Sunlight, b.Sunlight, t),
                Fog = math.lerp(a.Fog, b.Fog, t), Wind = math.lerp(a.Wind, b.Wind, t),
                Precipitation = math.lerp(a.Precipitation, b.Precipitation, t)
            };
        }

        State from, to, current;
        float blend = 1f, blendSeconds = 1f, timeLeft;
        int index;
        float wetness;
        Vector2 cloudOffset;
        float baseSunIntensity = -1f;
        ParticleSystem rain, snow;
        EntityQuery playerQuery, terrainQuery, configQuery;
        World queryWorld;

        public WeatherPreset Current => Presets.Count > 0 ? Presets[index] : null;

        /// <summary>Starts blending to the given preset.</summary>
        public void SetWeather(int presetIndex, float seconds)
        {
            if (Presets.Count == 0)
                return;
            index = (presetIndex % Presets.Count + Presets.Count) % Presets.Count;
            from = current;
            to = State.From(Presets[index]);
            blend = 0f;
            blendSeconds = math.max(seconds, 0.01f);
            var d = Presets[index].Duration;
            timeLeft = seconds + Random.Range(d.x, math.max(d.x, d.y));
        }

        void OnEnable()
        {
            if (Presets.Count > 0)
                current = from = to = State.From(Presets[0]);
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;
            if (Sun == null)
                Sun = RenderSettings.sun;
            baseSunIntensity = Sun != null ? Sun.intensity : 1f;
            rain = CreatePrecipitation("Rain", true);
            snow = CreatePrecipitation("Snow", false);
            SetWeather(0, 0.01f);
        }

        void Update()
        {
            if (Presets.Count == 0 || Presets[0] == null)
                return;

            if (!Application.isPlaying)
            {
                // In the editor just show the first preset, so the sky isn't black while building scenes.
                current = State.From(Presets[0]);
                ApplyShaderGlobals(current, 0f, 0f, 0f);
                return;
            }

            var dt = Time.deltaTime;
            HandleDebugKeys();
            timeLeft -= dt;
            if (timeLeft <= 0f)
                SetWeather(PickNext(), TransitionSeconds);

            blend = math.min(1f, blend + dt / blendSeconds);
            current = State.Lerp(from, to, math.smoothstep(0f, 1f, blend));

            // Local conditions: dry biomes get less of the precipitation; high or snowy ground gets snow, not rain.
            var rainfall = 1f;
            var snowiness = 0f;
            if (TryGetPlayerGround(out var source, out var playerXZ))
            {
                rainfall = math.saturate(source.Rainfall(playerXZ) * 1.6f);
                var ground = source.SampleGround(playerXZ);
                var surfaceSnow = source.SampleSurface(playerXZ, ground, new float3(0f, 1f, 0f)).Snow;
                snowiness = math.max(math.saturate((ground.Height - source.World.SeaLevel - SnowLine) / 40f), math.saturate(surfaceSnow * 2f));
            }
            var precipitation = current.Precipitation * rainfall;
            var rainAmount = precipitation * (1f - snowiness);

            var wetTarget = rainAmount > 0.05f ? 1f : 0f;
            var rate = wetTarget > wetness ? rainAmount / math.max(SoakSeconds, 0.1f) : 1f / math.max(DrySeconds, 0.1f);
            wetness = Mathf.MoveTowards(wetness, wetTarget, rate * dt);

            var windDirection = new Vector2(0.8f, 0.6f);
            cloudOffset += windDirection * (0.003f + 0.025f * current.Wind) * dt;

            ApplyShaderGlobals(current, rainAmount, current.Wind, wetness);
            ApplyLightingAndFog(current);
            ApplyPrecipitation(rainAmount, precipitation * snowiness, windDirection * current.Wind * 6f);
        }

        void HandleDebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.rightBracketKey.wasPressedThisFrame)
                SetWeather(index + 1, 3f);
            else if (keyboard.leftBracketKey.wasPressedThisFrame)
                SetWeather(index - 1, 3f);
        }

        int PickNext()
        {
            float total = 0f;
            for (int i = 0; i < Presets.Count; i++)
                if (i != index) total += Presets[i].Chance;
            var pick = Random.value * total;
            for (int i = 0; i < Presets.Count; i++)
            {
                if (i == index) continue;
                pick -= Presets[i].Chance;
                if (pick <= 0f) return i;
            }
            return index;
        }

        void ApplyShaderGlobals(in State s, float rainAmount, float wind, float wet)
        {
            var zenith = Color.Lerp(ClearZenith, OvercastZenith, s.Overcast) * (1f - 0.45f * s.Darkness);
            var horizon = Color.Lerp(ClearHorizon, OvercastHorizon, s.Overcast) * (1f - 0.35f * s.Darkness);
            var sun = Sun != null ? Sun : RenderSettings.sun;
            var sunColor = (sun != null ? sun.color : Color.white) * math.saturate(s.Sunlight);

            Shader.SetGlobalVector(SunDirectionId, sun != null ? -sun.transform.forward : Vector3.up);
            Shader.SetGlobalColor(SunColorId, sunColor);
            Shader.SetGlobalColor(ZenithId, zenith);
            Shader.SetGlobalColor(HorizonId, horizon);
            Shader.SetGlobalColor(GroundId, GroundColor * (1f - 0.4f * s.Darkness));
            Shader.SetGlobalVector(CloudsId, new Vector4(s.Coverage, s.Darkness, 1f, 0f));
            Shader.SetGlobalVector(CloudOffsetId, cloudOffset);
            Shader.SetGlobalFloat(RainId, rainAmount);
            Shader.SetGlobalFloat(WindId, wind);
            Shader.SetGlobalFloat(WetnessId, wet);
        }

        void ApplyLightingAndFog(in State s)
        {
            var zenith = Color.Lerp(ClearZenith, OvercastZenith, s.Overcast) * (1f - 0.45f * s.Darkness);
            var horizon = Color.Lerp(ClearHorizon, OvercastHorizon, s.Overcast) * (1f - 0.35f * s.Darkness);

            // Exponential-squared fog reaching ~98% at the edge of the streamed terrain, so it's never visible.
            var drawDistance = 1500f;
            if (EnsureQueries() && configQuery.TryGetSingleton<TerrainConfig>(out var config))
                drawDistance = config.ViewRadiusChunks * config.ChunkSize;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogDensity = 1.98f / drawDistance * s.Fog;

            // Under cloud the sky's share of the light grows as the sun's shrinks.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            var ambientScale = math.lerp(0.9f, 1.15f, s.Overcast) * (1f - 0.35f * s.Darkness);
            RenderSettings.ambientSkyColor = zenith * ambientScale;
            RenderSettings.ambientEquatorColor = horizon * ambientScale * 0.85f;
            RenderSettings.ambientGroundColor = GroundColor * ambientScale * 0.6f;

            if (Sun != null && baseSunIntensity >= 0f)
            {
                Sun.intensity = baseSunIntensity * s.Sunlight;
                Sun.shadowStrength = math.lerp(0.2f, 1f, math.saturate(s.Sunlight));
            }
        }

        void ApplyPrecipitation(float rainAmount, float snowAmount, Vector2 wind)
        {
            var cam = Camera.main;
            if (cam == null || rain == null)
                return;
            UpdateEmitter(rain, rainAmount * RainParticlesPerSecond, cam.transform.position + Vector3.up * 14f, wind, -17f);
            UpdateEmitter(snow, snowAmount * SnowParticlesPerSecond, cam.transform.position + Vector3.up * 10f, wind * 0.4f, -1.4f);
        }

        static void UpdateEmitter(ParticleSystem ps, float rate, Vector3 position, Vector2 wind, float fallSpeed)
        {
            ps.transform.position = position;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var velocity = ps.velocityOverLifetime;
            velocity.x = wind.x;
            velocity.y = fallSpeed;
            velocity.z = wind.y;
        }

        ParticleSystem CreatePrecipitation(string name, bool isRain)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = isRain ? 15000 : 25000;
            main.startLifetime = isRain ? 1.4f : 9f;
            main.startSize = isRain ? 0.025f : 0.07f;
            main.startColor = isRain ? new Color(0.85f, 0.9f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.9f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = isRain ? new Vector3(60f, 1f, 60f) : new Vector3(50f, 1f, 50f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = isRain ? -17f : -1.4f;
            velocity.z = 0f;

            if (!isRain)
            {
                var turbulence = ps.noise;
                turbulence.enabled = true;
                turbulence.strength = 0.6f;
                turbulence.frequency = 0.25f;
            }

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = isRain ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.velocityScale = isRain ? 0.05f : 0f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(Shader.Find("Punchy/Precipitation"));
            material.SetFloat("_Shape", isRain ? 1f : 0f);
            renderer.sharedMaterial = material;

            ps.Play();
            return ps;
        }

        bool EnsureQueries()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                configQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainConfig>());
                queryWorld = world;
            }
            return true;
        }

        bool TryGetPlayerGround(out TerrainSource source, out float2 xz)
        {
            source = default;
            xz = default;
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton(out source) || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;
            xz = player.Position.xz;
            return true;
        }
    }
}
