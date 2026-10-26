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
    /// Runs the world's weather and applies it. The weather itself lives in the world as a WeatherField (cells
    /// that form by biome odds and drift with the wind); this component steps it, publishes its maps to the shaders
    /// (clouds, cloud shadows, distant dust and rain, wetness), and handles everything tied to the camera's own
    /// location: sky tint, sun/moon light, ambient light, precipitation and dust particles, and lightning with
    /// thunder from nearby storm cells. Time of day comes from DayNightCycle, if present.
    /// Press ] to spawn the next preset as a cell over the player, [ to clear the weather around them.
    /// </summary>
    [ExecuteAlways]
    public class WeatherSystem : MonoBehaviour
    {
        [Tooltip("The first preset is the fair-weather background between cells.")]
        public List<WeatherPreset> Presets = new List<WeatherPreset>();
        public Light Sun;
        [Tooltip("Optional. Without it the sky stays at fixed daytime colours.")]
        public DayNightCycle Cycle;

        [Header("World weather")]
        [Tooltip("Most weather cells alive at once across the world.")]
        public int MaxCells = 14;
        [Tooltip("How often a new cell may try to form somewhere (the biome odds decide what, if anything).")]
        public float SpawnAttemptsPerMinute = 10f;
        [Tooltip("Width of the square the weather covers, centred on the world origin, in metres.")]
        public float WeatherAreaSize = 16384f;
        [Tooltip("How quickly the prevailing wind direction wanders, in degrees per minute.")]
        public float WindVeer = 6f;

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
        static readonly int WindId = Shader.PropertyToID("_PunchyWind");
        static readonly int WeatherMapId = Shader.PropertyToID("_PunchyWeatherMap");
        static readonly int WetnessMapId = Shader.PropertyToID("_PunchyWetnessMap");
        static readonly int WeatherRectId = Shader.PropertyToID("_PunchyWeatherRect");
        static readonly int CloudMapId = Shader.PropertyToID("_PunchyCloudMap");
        static readonly int ClimateId = Shader.PropertyToID("_PunchyClimate");
        static readonly int CloudNoiseId = Shader.PropertyToID("_PunchyCloudNoise");
        static readonly int LightningPosId = Shader.PropertyToID("_PunchyLightningPos");
        static readonly int DustColorId = Shader.PropertyToID("_PunchyDustColor");
        static readonly int HazeDensityId = Shader.PropertyToID("_PunchyHazeDensity");

        /// <summary>Final sky colours for this frame, after time of day and the weather overhead.</summary>
        struct Sky
        {
            public Color Zenith, Horizon, Ground;
        }

        readonly WeatherField field = new WeatherField();
        int forcedPreset;
        float windAngle = 37f;
        Vector2 cloudOffset;
        float baseSunIntensity = -1f;
        ParticleSystem rain, snow, sand;
        Light lightning;
        AudioSource thunder;
        AudioClip[] thunderClips;
        float strikeTime = -10f, strikeBrightness;
        Vector3 strikeDirection, strikePosition;
        Texture2D fallbackWeather, fallbackWetness, fallbackCloud;
        Texture3D cloudNoise;
        EntityQuery playerQuery, terrainQuery, configQuery, paletteQuery;
        World queryWorld;

        /// <summary>The world's weather (cells, maps, sampling).</summary>
        public WeatherField Field => field;
        /// <summary>Weather at the camera this frame.</summary>
        public WeatherValues Local { get; private set; }
        public Vector2 WindDirection => new Vector2(Mathf.Cos(windAngle * Mathf.Deg2Rad), Mathf.Sin(windAngle * Mathf.Deg2Rad));

        void OnEnable()
        {
            if (Cycle == null)
                Cycle = GetComponent<DayNightCycle>();
            if (cloudNoise == null)
            {
                cloudNoise = CloudNoise.Create();
                cloudNoise.hideFlags = HideFlags.HideAndDontSave;
            }
            Shader.SetGlobalTexture(CloudNoiseId, cloudNoise);
        }

        void OnDisable() => field.Dispose();

        void OnDestroy()
        {
            if (cloudNoise != null)
                DestroyImmediate(cloudNoise);
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
        }

        void Update()
        {
            if (Presets.Count == 0 || Presets[0] == null)
                return;

            var cameraXZ = Vector2.zero;
            var cam = Camera.main;
            if (cam != null)
                cameraXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);

            if (!Application.isPlaying)
            {
                // In the editor just show fair weather at the cycle's time, so the sky isn't black while building scenes.
                if (Cycle != null)
                    Cycle.Evaluate();
                Local = WeatherValues.From(Presets[0]);
                PublishFallbackMaps(Local);
                ApplyShaderGlobals(Local, ComputeSky(Local), 1f / 1500f);
                return;
            }

            var dt = Time.deltaTime;
            if (!field.Ready)
                TryInitializeField();
            HandleDebugKeys();

            windAngle += (Mathf.PerlinNoise(Time.time * 0.01f, 3.7f) - 0.5f) * 2f * WindVeer / 60f * dt;
            field.Step(dt, WindDirection, SpawnAttemptsPerMinute, MaxCells, SoakSeconds, DrySeconds);
            Local = field.Ready ? field.Sample(cameraXZ) : WeatherValues.From(Presets[0]);

            if (field.Ready)
            {
                Shader.SetGlobalTexture(WeatherMapId, field.WeatherMap);
                Shader.SetGlobalTexture(WetnessMapId, field.WetnessMap);
                Shader.SetGlobalTexture(CloudMapId, field.CloudMap);
                Shader.SetGlobalVector(WeatherRectId, field.MapRect);
                Shader.SetGlobalVector(ClimateId, field.Climate);
            }
            else
                PublishFallbackMaps(Local);

            // Clouds drift with the wind (metres per second, converted to the shader's cloud-layer units).
            cloudOffset += WindDirection * (4f + 18f * Local.Wind) * dt / 1800f;

            var drawDistance = 1500f;
            if (EnsureQueries() && configQuery.TryGetSingleton<TerrainConfig>(out var config))
                drawDistance = config.ViewRadiusChunks * config.ChunkSize;
            // Exponential-squared haze reaching ~98% at the edge of the streamed terrain, so it's never visible.
            var hazeDensity = 1.98f / drawDistance;

            var sky = ComputeSky(Local);
            UpdateLightning(dt, cam);
            ApplyShaderGlobals(Local, sky, hazeDensity);
            ApplyLighting(Local, sky, hazeDensity);
            var snowiness = math.max(Local.AlwaysSnow, 1f - field.RainFraction(cameraXZ));
            ApplyParticles(cam, Local.Precipitation * (1f - snowiness), Local.Precipitation * snowiness, Local.Dust, Local.Wind);
        }

        void TryInitializeField()
        {
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var source) || !TryGetPalette(out var palette))
                return;
            field.Initialize(source, palette.Biomes, Presets, SnowLine, WeatherAreaSize);
        }

        void HandleDebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !field.Ready || !TryGetPlayerXZ(out var player))
                return;
            if (keyboard.rightBracketKey.wasPressedThisFrame)
            {
                forcedPreset = forcedPreset % (Presets.Count - 1) + 1;
                field.Clear(player, 300f);
                field.ForceCell(forcedPreset, player, 1500f);
                Debug.Log($"Weather: forced {Presets[forcedPreset].name} over the player");
            }
            else if (keyboard.leftBracketKey.wasPressedThisFrame)
                field.Clear(player, 2000f);
        }

        // ---------------------------------------------------------------- applying

        Sky ComputeSky(in WeatherValues s)
        {
            var clearZenith = Cycle != null ? Cycle.ClearZenith : ClearZenith;
            var clearHorizon = Cycle != null ? Cycle.ClearHorizon : ClearHorizon;
            var brightness = clearHorizon.grayscale;

            // Overcast desaturates towards the sky's own brightness, so it works at noon and at midnight alike.
            var greyHorizon = new Color(brightness, brightness * 1.02f, brightness * 1.06f);
            var zenith = Color.Lerp(clearZenith, greyHorizon * 0.78f, s.Overcast) * (1f - 0.45f * s.Darkness);
            var horizon = Color.Lerp(clearHorizon, greyHorizon, s.Overcast) * (1f - 0.35f * s.Darkness);

            var daylight = math.saturate(brightness / 0.8f);
            return new Sky
            {
                Zenith = zenith,
                Horizon = horizon,
                Ground = GroundColor * (daylight * (1f - 0.4f * s.Darkness))
            };
        }

        void ApplyShaderGlobals(in WeatherValues s, in Sky sky, float hazeDensity)
        {
            var sun = Sun != null ? Sun : RenderSettings.sun;
            var sunDirection = Cycle != null ? Cycle.SunDirection : (sun != null ? -sun.transform.forward : Vector3.up);
            var moonDirection = Cycle != null ? Cycle.MoonDirection : -sunDirection;
            var discColor = Cycle != null ? Cycle.SunDiscColor : (sun != null ? sun.color : Color.white);

            Shader.SetGlobalVector(SunDirectionId, sunDirection);
            Shader.SetGlobalVector(MoonDirectionId, moonDirection);
            Shader.SetGlobalColor(SunColorId, discColor * (1f - 0.8f * s.Dust));
            Shader.SetGlobalColor(ZenithId, sky.Zenith);
            Shader.SetGlobalColor(HorizonId, sky.Horizon);
            Shader.SetGlobalColor(GroundId, sky.Ground);
            Shader.SetGlobalColor(DustColorId, DustColor());
            Shader.SetGlobalFloat(HazeDensityId, hazeDensity);
            Shader.SetGlobalVector(CloudsId, new Vector4(s.Coverage, 0f, 1f, 0f));
            Shader.SetGlobalVector(CloudOffsetId, cloudOffset);
            Shader.SetGlobalFloat(NightId, Cycle != null ? Cycle.Night : 0f);
            Shader.SetGlobalVector(LightningId, new Vector4(strikeDirection.x, strikeDirection.y, strikeDirection.z, FlashBrightness() * strikeBrightness));
            Shader.SetGlobalVector(LightningPosId, new Vector4(strikePosition.x, strikePosition.y, strikePosition.z, FlashBrightness()));
            Shader.SetGlobalFloat(WindId, s.Wind);
        }

        void ApplyLighting(in WeatherValues s, in Sky sky, float hazeDensity)
        {
            // Unity's own fog only affects standard materials (e.g. the player); the terrain, water and sky do
            // their own atmosphere from the weather map. Match it roughly: base haze plus the local dust and rain.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(sky.Horizon, DustColor() * (sky.Horizon.grayscale * 1.4f + 0.03f), s.Dust);
            RenderSettings.fogDensity = hazeDensity + s.Dust * 0.012f + s.Haze * 0.0012f;

            // Under cloud the sky's share of the light grows as the sun's shrinks (cloud shadows dim the direct
            // light where the clouds actually are). Moonlight keeps nights from going pitch black.
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
                    Sun.color = Color.Lerp(Cycle.LightColor, DustColor(), s.Dust * 0.6f);
                }
                Sun.intensity = baseSunIntensity * strength * (1f - 0.7f * s.Dust);
                Sun.shadowStrength = math.lerp(0.25f, 1f, 1f - s.Coverage * 0.6f) * (1f - 0.6f * s.Dust);
            }
        }

        Color DustColor()
        {
            foreach (var p in Presets)
                if (p != null && p.Dust > 0f)
                    return p.DustColor;
            return new Color(0.78f, 0.6f, 0.4f);
        }

        /// <summary>A 1×1 fair-weather map, so shaders have something sensible before the field is ready.</summary>
        void PublishFallbackMaps(in WeatherValues s)
        {
            if (fallbackWeather == null)
            {
                fallbackWeather = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                fallbackWetness = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                fallbackCloud = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                fallbackWetness.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));
                fallbackWetness.Apply();
            }
            fallbackWeather.SetPixel(0, 0, new Color(s.Coverage, s.Darkness, s.Haze, s.Dust));
            fallbackWeather.Apply();
            fallbackCloud.SetPixel(0, 0, new Color(s.CloudBase / WeatherValues.MaxCloudBase, s.CloudThickness / WeatherValues.MaxCloudThickness, 0f, 1f));
            fallbackCloud.Apply();
            Shader.SetGlobalTexture(WeatherMapId, fallbackWeather);
            Shader.SetGlobalTexture(WetnessMapId, fallbackWetness);
            Shader.SetGlobalTexture(CloudMapId, fallbackCloud);
            Shader.SetGlobalVector(ClimateId, new Vector4(s.Coverage, s.Darkness,
                s.CloudBase / WeatherValues.MaxCloudBase, s.CloudThickness / WeatherValues.MaxCloudThickness));
            if (cloudNoise != null)
                Shader.SetGlobalTexture(CloudNoiseId, cloudNoise);
            Shader.SetGlobalVector(WeatherRectId, new Vector4(0f, 0f, 1f, 1f));
        }

        // ---------------------------------------------------------------- lightning & thunder

        void UpdateLightning(float dt, Camera cam)
        {
            if (lightning == null || cam == null)
                return;
            if (field.TryStrike(dt, out var strike))
            {
                var camPos = cam.transform.position;
                var offset = new Vector3(strike.x - camPos.x, 0f, strike.z - camPos.z);
                var distance = offset.magnitude;
                if (distance < 12000f)
                {
                    strikeTime = Time.time;
                    // The flash lights the storm cloud from inside, around a third of the way up the tower.
                    var cloud = field.Sample(new Vector2(strike.x, strike.z));
                    strikePosition = new Vector3(strike.x, cloud.CloudBase + cloud.CloudThickness * 0.3f, strike.z);
                    // Flashes fade with distance; the glow sits low over the storm, high when it's overhead.
                    strikeBrightness = 1f / (1f + (distance / 1500f) * (distance / 1500f));
                    var horizontal = distance > 1f ? offset / distance : Vector3.forward;
                    strikeDirection = (horizontal * distance + Vector3.up * 1200f).normalized;
                    lightning.transform.rotation = Quaternion.LookRotation(-(Vector3.up + horizontal * 0.4f).normalized);
                    // Thunder arrives after the light: ~3 s per kilometre.
                    if (distance < 9000f)
                        StartCoroutine(PlayThunder(distance / 343f, math.lerp(1f, 0.15f, distance / 9000f)));
                }
            }
            lightning.intensity = FlashBrightness() * strikeBrightness * 2.2f;
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

        void ApplyParticles(Camera cam, float rainAmount, float snowAmount, float dust, float wind)
        {
            if (cam == null || rain == null)
                return;
            var position = cam.transform.position;
            var drift = WindDirection * wind * 6f;
            UpdateEmitter(rain, rainAmount * RainParticlesPerSecond, position + Vector3.up * 14f, new Vector3(drift.x, -17f, drift.y));
            UpdateEmitter(snow, snowAmount * SnowParticlesPerSecond, position + Vector3.up * 10f, new Vector3(drift.x * 0.4f, -1.4f, drift.y * 0.4f));
            // Sand blows sideways past the camera, entering from upwind.
            var gust = WindDirection * (12f + 18f * wind);
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

        bool TryGetPlayerXZ(out Vector2 xz)
        {
            xz = default;
            if (!EnsureQueries() || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return false;
            xz = new Vector2(player.Position.x, player.Position.z);
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
