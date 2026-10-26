using System.Collections;
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
    /// Drives sky, fog, sun/moon light, ambient light, wind, wetness, precipitation, dust and lightning from the
    /// time of day (DayNightCycle, if present) and a list of weather presets, drifting from one preset to the next.
    /// Weather is regional: the next preset is picked using the odds of the biome the player is in, and weather
    /// that can't happen there (a sandstorm outside the desert) clears away. Everything that reads the weather (sky,
    /// water and terrain shaders) gets it through shader globals set here, so they always agree. Fog never thins
    /// below what's needed to hide the edge of the streamed terrain, and its colour is the sky's horizon colour.
    /// Press [ and ] to step through the presets.
    /// </summary>
    [ExecuteAlways]
    public class WeatherSystem : MonoBehaviour
    {
        public List<WeatherPreset> Presets = new List<WeatherPreset>();
        [Tooltip("How long one weather takes to blend into the next, in seconds.")]
        public float TransitionSeconds = 45f;
        public Light Sun;
        [Tooltip("Optional. Without it the sky stays at fixed daytime colours.")]
        public DayNightCycle Cycle;

        [Header("Sky colours (used without a DayNightCycle)")]
        public Color ClearZenith = new Color(0.20f, 0.42f, 0.85f);
        public Color ClearHorizon = new Color(0.70f, 0.80f, 0.93f);
        public Color GroundColor = new Color(0.36f, 0.37f, 0.39f);

        [Header("Ground wetness")]
        [Tooltip("Seconds of full rain to soak the ground.")]
        public float SoakSeconds = 25f;
        [Tooltip("Seconds for soaked ground to dry out.")]
        public float DrySeconds = 120f;

        [Header("Particles")]
        public float RainParticlesPerSecond = 9000f;
        public float SnowParticlesPerSecond = 2500f;
        public float SandParticlesPerSecond = 7000f;
        [Tooltip("Above this height (metres above sea level) precipitation falls as snow.")]
        public float SnowLine = 140f;

        static readonly int SunDirectionId = Shader.PropertyToID("_PunchySunDirection");
        static readonly int MoonDirectionId = Shader.PropertyToID("_PunchyMoonDirection");
        static readonly int SunColorId = Shader.PropertyToID("_PunchySunColor");
        static readonly int ZenithId = Shader.PropertyToID("_PunchySkyZenith");
        static readonly int HorizonId = Shader.PropertyToID("_PunchySkyHorizon");
        static readonly int GroundId = Shader.PropertyToID("_PunchySkyGround");
        static readonly int CloudsId = Shader.PropertyToID("_PunchyClouds");
        static readonly int CloudOffsetId = Shader.PropertyToID("_PunchyCloudOffset");
        static readonly int NightId = Shader.PropertyToID("_PunchyNight");
        static readonly int LightningId = Shader.PropertyToID("_PunchyLightning");
        static readonly int RainId = Shader.PropertyToID("_PunchyRain");
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int WetnessId = Shader.PropertyToID("_PunchyWetness");

        struct State
        {
            public float Coverage, Darkness, Overcast, Sunlight, Fog, Wind, Precipitation, AlwaysSnow, Dust, Lightning;
            public Color DustColor;

            public static State From(WeatherPreset p) => new State
            {
                Coverage = p.CloudCoverage, Darkness = p.CloudDarkness, Overcast = p.Overcast, Sunlight = p.Sunlight,
                Fog = p.Fog, Wind = p.Wind, Precipitation = p.Precipitation, AlwaysSnow = p.AlwaysSnow ? 1f : 0f,
                Dust = p.Dust, DustColor = p.DustColor, Lightning = p.LightningPerMinute
            };

            public static State Lerp(in State a, in State b, float t) => new State
            {
                Coverage = math.lerp(a.Coverage, b.Coverage, t), Darkness = math.lerp(a.Darkness, b.Darkness, t),
                Overcast = math.lerp(a.Overcast, b.Overcast, t), Sunlight = math.lerp(a.Sunlight, b.Sunlight, t),
                Fog = math.lerp(a.Fog, b.Fog, t), Wind = math.lerp(a.Wind, b.Wind, t),
                Precipitation = math.lerp(a.Precipitation, b.Precipitation, t), AlwaysSnow = math.lerp(a.AlwaysSnow, b.AlwaysSnow, t),
                Dust = math.lerp(a.Dust, b.Dust, t), DustColor = Color.Lerp(a.DustColor, b.DustColor, t),
                Lightning = math.lerp(a.Lightning, b.Lightning, t)
            };
        }

        /// <summary>Final colours for this frame, after time of day and weather.</summary>
        struct Sky
        {
            public Color Zenith, Horizon, Ground;
            public float Daylight;
        }

        State from, to, current;
        float blend = 1f, blendSeconds = 1f, timeLeft, regionCheck;
        int index;
        float wetness;
        Vector2 cloudOffset;
        float baseSunIntensity = -1f;
        ParticleSystem rain, snow, sand;
        Light lightning;
        AudioSource thunder;
        AudioClip[] thunderClips;
        float nextStrike, strikeTime = -10f;
        Vector3 strikeDirection;
        EntityQuery playerQuery, terrainQuery, configQuery, paletteQuery;
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
            if (Presets.Count > 0 && Presets[0] != null)
                current = from = to = State.From(Presets[0]);
            if (Cycle == null)
                Cycle = GetComponent<DayNightCycle>();
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;
            if (Sun == null)
                Sun = RenderSettings.sun;
            baseSunIntensity = Sun != null ? Sun.intensity : 1f;
            rain = CreateParticles("Rain", ParticleSystemRenderMode.Stretch, 15000, 1.4f, 0.025f, new Color(0.85f, 0.9f, 1f, 0.4f), new Vector3(60f, 1f, 60f), 1f);
            snow = CreateParticles("Snow", ParticleSystemRenderMode.Billboard, 25000, 9f, 0.07f, new Color(1f, 1f, 1f, 0.9f), new Vector3(50f, 1f, 50f), 0f);
            sand = CreateParticles("Sand", ParticleSystemRenderMode.Stretch, 12000, 1.2f, 0.035f, new Color(0.8f, 0.65f, 0.45f, 0.5f), new Vector3(50f, 8f, 50f), 1f);
            var snowNoise = snow.noise;
            snowNoise.enabled = true;
            snowNoise.strength = 0.6f;
            snowNoise.frequency = 0.25f;

            var lightningObject = new GameObject("Lightning");
            lightningObject.transform.SetParent(transform, false);
            lightning = lightningObject.AddComponent<Light>();
            lightning.type = LightType.Directional;
            lightning.color = new Color(0.8f, 0.85f, 1f);
            lightning.intensity = 0f;
            lightning.shadows = LightShadows.None;

            thunder = gameObject.AddComponent<AudioSource>();
            thunder.playOnAwake = false;
            thunder.spatialBlend = 0f;
            thunderClips = new[] { MakeThunder(1), MakeThunder(2), MakeThunder(3) };

            SetWeather(0, 0.01f);
        }

        void Update()
        {
            if (Presets.Count == 0 || Presets[0] == null)
                return;

            if (!Application.isPlaying)
            {
                // In the editor just show the first preset at the cycle's time, so the sky isn't black while building scenes.
                current = State.From(Presets[0]);
                if (Cycle != null)
                    Cycle.Evaluate();
                ApplyShaderGlobals(current, ComputeSky(current), 0f, 0f, 0f);
                return;
            }

            var dt = Time.deltaTime;
            HandleDebugKeys();
            var hasGround = TryGetPlayerGround(out var source, out var playerXZ, out var ground);

            timeLeft -= dt;
            regionCheck -= dt;
            if (timeLeft <= 0f)
                SetWeather(PickNext(hasGround, ground), TransitionSeconds);
            else if (regionCheck <= 0f && hasGround)
            {
                // Regional weather: if the player has walked somewhere this weather can't happen, it clears away.
                regionCheck = 1f;
                if (blend >= 1f && LocalChance(Presets[index], ground) <= 0.001f)
                    SetWeather(PickNext(true, ground), TransitionSeconds * 0.5f);
            }

            blend = math.min(1f, blend + dt / blendSeconds);
            current = State.Lerp(from, to, math.smoothstep(0f, 1f, blend));

            // Local conditions: dry biomes get less of the precipitation; high or snowy ground gets snow, not rain.
            var rainfall = 1f;
            var snowiness = current.AlwaysSnow;
            if (hasGround)
            {
                rainfall = math.saturate(source.Rainfall(playerXZ) * 1.6f);
                var surfaceSnow = source.SampleSurface(playerXZ, ground, new float3(0f, 1f, 0f)).Snow;
                snowiness = math.max(snowiness, math.max(math.saturate((ground.Height - source.World.SeaLevel - SnowLine) / 40f), math.saturate(surfaceSnow * 2f)));
                if (current.AlwaysSnow > 0.5f)
                    rainfall = 1f;
            }
            var precipitation = current.Precipitation * rainfall;
            var rainAmount = precipitation * (1f - snowiness);

            var wetTarget = rainAmount > 0.05f ? 1f : 0f;
            var rate = wetTarget > wetness ? rainAmount / math.max(SoakSeconds, 0.1f) : 1f / math.max(DrySeconds, 0.1f);
            wetness = Mathf.MoveTowards(wetness, wetTarget, rate * dt);

            var windDirection = new Vector2(0.8f, 0.6f);
            // Cloud layer drift in metres per second, converted to the shader's cloud-layer units.
            cloudOffset += windDirection * (4f + 18f * current.Wind) * dt / 1800f;

            var sky = ComputeSky(current);
            UpdateLightning(dt, current.Lightning);
            ApplyShaderGlobals(current, sky, rainAmount, current.Wind, wetness);
            ApplyLightingAndFog(current, sky);
            ApplyParticles(rainAmount, precipitation * snowiness, current.Dust, windDirection, current.Wind);
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

        // ---------------------------------------------------------------- choosing weather

        /// <summary>How likely a preset is at the player's location: the biome odds, blended by biome weight.</summary>
        float LocalChance(WeatherPreset preset, in GroundSample ground)
        {
            if (!TryGetPalette(out var palette))
                return preset.OnlyInListedBiomes ? 0f : preset.Chance;
            float chance = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = ground.Biomes.Weights[i];
                var id = ground.Biomes.Ids[i];
                if (w > 0f && id < palette.Biomes.Length)
                    chance += w * ChanceIn(preset, palette.Biomes[id]);
            }
            return chance;
        }

        static float ChanceIn(WeatherPreset preset, BiomeDefinition biome)
        {
            if (biome != null)
                foreach (var entry in biome.Weather)
                    if (entry.Preset == preset)
                        return entry.Chance;
            return preset.OnlyInListedBiomes ? 0f : preset.Chance;
        }

        int PickNext(bool hasGround, in GroundSample ground)
        {
            var chances = new float[Presets.Count];
            float total = 0f;
            for (int i = 0; i < Presets.Count; i++)
            {
                if (i == index) continue;
                chances[i] = hasGround ? LocalChance(Presets[i], ground) : (Presets[i].OnlyInListedBiomes ? 0f : Presets[i].Chance);
                total += chances[i];
            }
            if (total <= 0f)
                return index;
            var pick = Random.value * total;
            for (int i = 0; i < Presets.Count; i++)
            {
                pick -= chances[i];
                if (chances[i] > 0f && pick <= 0f)
                    return i;
            }
            return index;
        }

        // ---------------------------------------------------------------- applying

        Sky ComputeSky(in State s)
        {
            var clearZenith = Cycle != null ? Cycle.ClearZenith : ClearZenith;
            var clearHorizon = Cycle != null ? Cycle.ClearHorizon : ClearHorizon;
            var brightness = clearHorizon.grayscale;

            // Overcast desaturates towards the sky's own brightness, so it works at noon and at midnight alike.
            var greyHorizon = new Color(brightness, brightness * 1.02f, brightness * 1.06f);
            var zenith = Color.Lerp(clearZenith, greyHorizon * 0.78f, s.Overcast) * (1f - 0.45f * s.Darkness);
            var horizon = Color.Lerp(clearHorizon, greyHorizon, s.Overcast) * (1f - 0.35f * s.Darkness);

            // Dust and sand turn the whole sky the colour of the dust.
            var dust = s.DustColor * (horizon.grayscale * 1.3f + 0.02f);
            horizon = Color.Lerp(horizon, dust, s.Dust);
            zenith = Color.Lerp(zenith, dust * 0.85f, s.Dust * 0.9f);

            var daylight = math.saturate(brightness / 0.8f);
            return new Sky
            {
                Zenith = zenith,
                Horizon = horizon,
                Ground = GroundColor * (daylight * (1f - 0.4f * s.Darkness)),
                Daylight = daylight
            };
        }

        void ApplyShaderGlobals(in State s, in Sky sky, float rainAmount, float wind, float wet)
        {
            var sun = Sun != null ? Sun : RenderSettings.sun;
            var sunDirection = Cycle != null ? Cycle.SunDirection : (sun != null ? -sun.transform.forward : Vector3.up);
            var moonDirection = Cycle != null ? Cycle.MoonDirection : -sunDirection;
            var discColor = Cycle != null ? Cycle.SunDiscColor : (sun != null ? sun.color : Color.white);

            Shader.SetGlobalVector(SunDirectionId, sunDirection);
            Shader.SetGlobalVector(MoonDirectionId, moonDirection);
            Shader.SetGlobalColor(SunColorId, discColor * math.saturate(s.Sunlight) * (1f - 0.8f * s.Dust));
            Shader.SetGlobalColor(ZenithId, sky.Zenith);
            Shader.SetGlobalColor(HorizonId, sky.Horizon);
            Shader.SetGlobalColor(GroundId, sky.Ground);
            Shader.SetGlobalVector(CloudsId, new Vector4(s.Coverage, s.Darkness, 1f, 0f));
            Shader.SetGlobalVector(CloudOffsetId, cloudOffset);
            Shader.SetGlobalFloat(NightId, Cycle != null ? Cycle.Night * (1f - s.Dust) : 0f);
            Shader.SetGlobalVector(LightningId, new Vector4(strikeDirection.x, strikeDirection.y, strikeDirection.z, FlashBrightness()));
            Shader.SetGlobalFloat(RainId, rainAmount);
            Shader.SetGlobalFloat(WindId, wind);
            Shader.SetGlobalFloat(WetnessId, wet);
        }

        void ApplyLightingAndFog(in State s, in Sky sky)
        {
            // Exponential-squared fog reaching ~98% at the edge of the streamed terrain, so it's never visible.
            var drawDistance = 1500f;
            if (EnsureQueries() && configQuery.TryGetSingleton<TerrainConfig>(out var config))
                drawDistance = config.ViewRadiusChunks * config.ChunkSize;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = sky.Horizon;
            RenderSettings.fogDensity = 1.98f / drawDistance * s.Fog;

            // Under cloud the sky's share of the light grows as the sun's shrinks. Moonlight keeps nights from going
            // pitch black.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            var ambientScale = math.lerp(0.9f, 1.15f, s.Overcast) * (1f - 0.35f * s.Darkness);
            var moonFloor = new Color(0.025f, 0.03f, 0.05f) * (Cycle != null ? Cycle.Night : 0f);
            RenderSettings.ambientSkyColor = sky.Zenith * ambientScale + moonFloor;
            RenderSettings.ambientEquatorColor = sky.Horizon * (ambientScale * 0.85f) + moonFloor;
            RenderSettings.ambientGroundColor = sky.Ground * (ambientScale * 0.6f) + moonFloor * 0.5f;

            if (Sun != null && baseSunIntensity >= 0f)
            {
                var strength = Cycle != null ? Cycle.LightStrength : 1f;
                if (Cycle != null)
                {
                    Sun.transform.rotation = Quaternion.LookRotation(-Cycle.LightDirection);
                    Sun.color = Color.Lerp(Cycle.LightColor, s.DustColor, s.Dust * 0.6f);
                }
                Sun.intensity = baseSunIntensity * strength * s.Sunlight * (1f - 0.7f * s.Dust);
                Sun.shadowStrength = math.lerp(0.2f, 1f, math.saturate(s.Sunlight)) * (1f - 0.6f * s.Dust);
            }
        }

        // ---------------------------------------------------------------- lightning & thunder

        void UpdateLightning(float dt, float strikesPerMinute)
        {
            if (lightning == null)
                return;
            if (strikesPerMinute > 0.01f)
            {
                nextStrike -= dt;
                if (nextStrike <= 0f)
                {
                    Strike();
                    // Random gaps (exponentially distributed) around the average rate.
                    nextStrike = -Mathf.Log(Mathf.Max(Random.value, 1e-4f)) * 60f / strikesPerMinute;
                }
            }
            else
                nextStrike = Random.Range(2f, 8f);

            lightning.intensity = FlashBrightness() * 2.2f;
        }

        void Strike()
        {
            strikeTime = Time.time;
            var azimuth = Random.Range(0f, Mathf.PI * 2f);
            var elevation = Random.Range(0.1f, 0.5f);
            strikeDirection = new Vector3(Mathf.Cos(azimuth) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(azimuth) * Mathf.Cos(elevation));
            lightning.transform.rotation = Quaternion.LookRotation(-new Vector3(strikeDirection.x * 0.4f, 1f, strikeDirection.z * 0.4f));

            // Thunder arrives after the light: ~3 s per kilometre.
            var distance = Random.Range(300f, 4000f);
            StartCoroutine(PlayThunder(distance / 343f, math.lerp(1f, 0.25f, distance / 4000f)));
        }

        IEnumerator PlayThunder(float delay, float volume)
        {
            yield return new WaitForSeconds(delay);
            thunder.PlayOneShot(thunderClips[Random.Range(0, thunderClips.Length)], volume);
        }

        /// <summary>A double-flicker flash envelope after each strike.</summary>
        float FlashBrightness()
        {
            var t = Time.time - strikeTime;
            if (t < 0f || t > 1f)
                return 0f;
            var flash = Mathf.Exp(-t * 18f);
            if (t > 0.13f) flash += 0.7f * Mathf.Exp(-(t - 0.13f) * 22f);
            if (t > 0.30f) flash += 0.4f * Mathf.Exp(-(t - 0.30f) * 25f);
            return flash;
        }

        /// <summary>Synthesises a thunderclap: a sharp crack, then a long, swelling brown-noise rumble.</summary>
        static AudioClip MakeThunder(int seed)
        {
            const int rate = 22050;
            const float seconds = 7f;
            var samples = new float[(int)(rate * seconds)];
            var rng = new System.Random(seed);
            float brown = 0f, crackFilter = 0f, peak = 1e-4f;
            for (int i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                var white = (float)rng.NextDouble() * 2f - 1f;
                brown = (brown + 0.02f * white) / 1.02f;
                crackFilter += (white - crackFilter) * 0.3f;
                var crack = t < 0.3f ? crackFilter * Mathf.Exp(-t * 14f) : 0f;
                var swell = 0.6f + 0.4f * Mathf.Sin(t * 2.3f + seed) * Mathf.Sin(t * 0.9f + seed * 2f);
                var rumble = brown * (1f - Mathf.Exp(-t * 6f)) * Mathf.Exp(-t * 0.55f) * swell * 4f;
                samples[i] = crack * 0.6f + rumble;
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= 0.9f / peak;
            var clip = AudioClip.Create($"Thunder{seed}", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ---------------------------------------------------------------- particles

        void ApplyParticles(float rainAmount, float snowAmount, float dust, Vector2 windDirection, float wind)
        {
            var cam = Camera.main;
            if (cam == null || rain == null)
                return;
            var position = cam.transform.position;
            var drift = windDirection * wind * 6f;
            UpdateEmitter(rain, rainAmount * RainParticlesPerSecond, position + Vector3.up * 14f, new Vector3(drift.x, -17f, drift.y));
            UpdateEmitter(snow, snowAmount * SnowParticlesPerSecond, position + Vector3.up * 10f, new Vector3(drift.x * 0.4f, -1.4f, drift.y * 0.4f));
            // Sand blows sideways past the camera, entering from upwind.
            var gust = windDirection * (12f + 18f * wind);
            UpdateEmitter(sand, dust * SandParticlesPerSecond, position - new Vector3(gust.x, 0f, gust.y) * 0.6f + Vector3.up * 2f, new Vector3(gust.x, -0.5f, gust.y));
        }

        static void UpdateEmitter(ParticleSystem ps, float rate, Vector3 position, Vector3 velocity)
        {
            ps.transform.position = position;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var v = ps.velocityOverLifetime;
            v.x = velocity.x;
            v.y = velocity.y;
            v.z = velocity.z;
        }

        ParticleSystem CreateParticles(string name, ParticleSystemRenderMode mode, int max, float lifetime, float size, Color color, Vector3 box, float shape)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.maxParticles = max;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = color;

            var emitShape = ps.shape;
            emitShape.shapeType = ParticleSystemShapeType.Box;
            emitShape.scale = box;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = 0f;
            velocity.z = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = mode;
            renderer.velocityScale = mode == ParticleSystemRenderMode.Stretch ? 0.05f : 0f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(Shader.Find("Punchy/Precipitation"));
            material.SetFloat("_Shape", shape);
            renderer.sharedMaterial = material;

            ps.Play();
            return ps;
        }

        // ---------------------------------------------------------------- ECS access

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
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                queryWorld = world;
            }
            return true;
        }

        bool TryGetPlayerGround(out TerrainSource source, out float2 xz, out GroundSample ground)
        {
            source = default;
            xz = default;
            ground = default;
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton(out source) || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;
            xz = player.Position.xz;
            ground = source.SampleGround(xz);
            return true;
        }

        bool TryGetPalette(out BiomePalette palette)
        {
            palette = null;
            if (!EnsureQueries() || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette?.Biomes != null;
        }
    }
}
