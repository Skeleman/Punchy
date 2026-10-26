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
        [Tooltip("Shader for rain, snow, hail and dust particles. Referenced here so builds include it.")]
        public Shader PrecipitationShader;
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
        public float HailParticlesPerSecond = 2500f;
        [Tooltip("Large translucent dust veils per second in a full sandstorm.")]
        public float DustVeilsPerSecond = 70f;
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
        static readonly int WindDirectionId = Shader.PropertyToID("_PunchyWindDirection");
        static readonly int CloudNoiseId = Shader.PropertyToID("_PunchyCloudNoise");
        static readonly int LightningPosId = Shader.PropertyToID("_PunchyLightningPos");
        static readonly int DustColorId = Shader.PropertyToID("_PunchyDustColor");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
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
        ParticleSystem rain, snow, hail, dust;
        Transform hailGround;
        Light lightning;
        int dayType;
        float lastTimeOfDay = -1f;
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
            var snowNoise = snow.noise;
            snowNoise.enabled = true;
            snowNoise.strength = 0.6f;
            snowNoise.frequency = 0.25f;
            CreateHail();
            CreateDustVeils();

            var lightningObject = new GameObject("Lightning");
            lightningObject.transform.SetParent(transform, false);
            lightning = lightningObject.AddComponent<Light>();
            lightning.type = LightType.Directional;
            lightning.color = new Color(0.8f, 0.85f, 1f);
            lightning.intensity = 0f;
            lightning.shadows = LightShadows.None;

            SetDayType(PickDayType(), false);
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

            // A new day brings a new day type (decided at midnight).
            if (Cycle != null)
            {
                if (lastTimeOfDay >= 0f && Cycle.TimeOfDay < lastTimeOfDay - 12f)
                    SetDayType(PickDayType(), false);
                lastTimeOfDay = Cycle.TimeOfDay;
            }

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
            var falling = Local.Precipitation * (1f - Local.Hail * 0.5f);
            ApplyParticles(cam, falling * (1f - snowiness), falling * snowiness, Local.Precipitation * Local.Hail * (1f - snowiness), Local.Dust, Local.Wind);
        }

        // ---------------------------------------------------------------- day types

        /// <summary>The overall character of a day: shifts the pressure pattern and how thundery the bad weather is.</summary>
        [System.Serializable]
        public class DayType
        {
            public string Name;
            [Tooltip("−1 settled (few lows) … +1 unsettled (few highs).")]
            [Range(-1f, 1f)] public float Bias;
            [Tooltip("Multiplies the odds of thunderstorms and hail.")]
            public float StormFactor = 1f;
            [Tooltip("Multiplies the odds of any rain or snow.")]
            public float PrecipitationFactor = 1f;
            [Tooltip("Relative chance of a day being this type.")]
            public float Chance = 1f;
        }

        [Header("Days")]
        public List<DayType> DayTypes = new List<DayType>
        {
            new DayType { Name = "Sunny", Bias = -0.45f, StormFactor = 0.2f, Chance = 2f },
            new DayType { Name = "Partly cloudy", Bias = -0.2f, StormFactor = 0.6f, Chance = 3f },
            new DayType { Name = "Changeable", Bias = 0.05f, StormFactor = 1f, Chance = 2.5f },
            new DayType { Name = "Overcast", Bias = 0.4f, StormFactor = 0.1f, PrecipitationFactor = 0.25f, Chance = 1.5f },
            new DayType { Name = "Stormy", Bias = 0.55f, StormFactor = 3f, Chance = 1f }
        };

        public string DayTypeName => DayTypes.Count > 0 ? DayTypes[dayType].Name : "-";

        int PickDayType()
        {
            float total = 0f;
            foreach (var d in DayTypes) total += d.Chance;
            var pick = Random.value * total;
            for (int i = 0; i < DayTypes.Count; i++)
            {
                pick -= DayTypes[i].Chance;
                if (pick <= 0f) return i;
            }
            return 0;
        }

        /// <summary>Switches the day type. With reseed, today's weather is replaced at once (for previewing).</summary>
        public void SetDayType(int index, bool reseed)
        {
            if (DayTypes.Count == 0)
                return;
            dayType = (index % DayTypes.Count + DayTypes.Count) % DayTypes.Count;
            field.Bias = DayTypes[dayType].Bias;
            field.StormFactor = DayTypes[dayType].StormFactor;
            field.PrecipitationFactor = DayTypes[dayType].PrecipitationFactor;
            if (reseed && field.Ready)
                field.Reseed();
        }

        public void NextDayType() => SetDayType(dayType + 1, true);

        void TryInitializeField()
        {
            if (!EnsureQueries() || !terrainQuery.TryGetSingleton<TerrainSource>(out var source) || !TryGetPalette(out var palette))
                return;
            field.Initialize(source, palette.Biomes, Presets, SnowLine, WeatherAreaSize);
            SetDayType(dayType, true);
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
            Shader.SetGlobalVector(WindDirectionId, WindDirection);
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

        /// <summary>
        /// Colour of the airborne dust: whatever dust is around the camera (sand, ash, spores…), or failing that the
        /// first dusty preset's, for distant dust seen from clear air.
        /// </summary>
        Color DustColor()
        {
            var fallback = new Color(0.78f, 0.6f, 0.4f);
            foreach (var p in Presets)
                if (p != null && p.Dust > 0f)
                {
                    fallback = p.DustColor;
                    break;
                }
            var c = Local.DustColor(new float3(fallback.r, fallback.g, fallback.b));
            return new Color(c.x, c.y, c.z);
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
                    // Thunder arrives after the light (~3 s per kilometre), from the strike's direction.
                    if (distance < 9000f)
                    {
                        var pan = Vector3.Dot(horizontal, cam.transform.right) * 0.7f;
                        StartCoroutine(PlayThunder(distance, pan));
                    }
                }
            }
            lightning.intensity = FlashBrightness() * strikeBrightness * 2.2f;
        }

        IEnumerator PlayThunder(float distance, float pan)
        {
            // Synthesise on a worker thread while the sound "travels" (it always takes longer than the synthesis).
            var seed = thunderRandom.Next();
            var task = System.Threading.Tasks.Task.Run(() => ThunderSynth.Generate(distance, seed));
            yield return new WaitForSeconds(distance / 343f);
            while (!task.IsCompleted)
                yield return null;
            var clip = ThunderSynth.ToClip(task.Result);
            // Each clap gets its own source so overlapping thunder from different directions pans independently.
            var source = new GameObject("Thunder").AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.spatialBlend = 0f;
            source.panStereo = pan;
            source.volume = math.lerp(1f, 0.25f, math.saturate(distance / 9000f));
            source.clip = clip;
            source.Play();
            Destroy(source.gameObject, clip.length + 0.5f);
            Destroy(clip, clip.length + 1f);
        }

        readonly System.Random thunderRandom = new System.Random();

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

        // ---------------------------------------------------------------- particles

        void ApplyParticles(Camera cam, float rainAmount, float snowAmount, float hailAmount, float dustAmount, float wind)
        {
            if (cam == null || rain == null)
                return;
            var position = cam.transform.position;
            var drift = WindDirection * wind * 6f;
            UpdateEmitter(rain, rainAmount * RainParticlesPerSecond, position + Vector3.up * 14f, new Vector3(drift.x, -17f, drift.y));
            UpdateEmitter(snow, snowAmount * SnowParticlesPerSecond, position + Vector3.up * 10f, new Vector3(drift.x * 0.4f, -1.4f, drift.y * 0.4f));
            UpdateEmitter(hail, hailAmount * HailParticlesPerSecond, position + Vector3.up * 16f, new Vector3(drift.x * 0.5f, -24f, drift.y * 0.5f));
            // Hailstones bounce off the ground under the camera.
            if (hailGround != null && EnsureQueries() && terrainQuery.TryGetSingleton<TerrainSource>(out var source))
                hailGround.position = new Vector3(position.x, source.SampleHeight(new float2(position.x, position.z)) + 0.02f, position.z);

            // Dust: no visible grains (too small to see) — slow, translucent veils of dust rolling past on the wind,
            // on top of the visibility loss the atmosphere already applies.
            var gust = WindDirection * (8f + 14f * wind);
            UpdateEmitter(dust, dustAmount * DustVeilsPerSecond, position - new Vector3(gust.x, 0f, gust.y) * 2.5f + Vector3.up * 3f, new Vector3(gust.x, 0.3f, gust.y));
            var main = dust.main;
            main.startColor = DustColor() * new Color(1f, 1f, 1f, 0.16f);

            // Tinted and glowing precipitation (falling ash, sparks).
            var tint = Local.PrecipitationColor;
            var glow = Local.PrecipitationGlow;
            foreach (var ps in new[] { rain, snow })
            {
                var m = ps.main;
                m.startColor = ps == rain ? new Color(tint.x * 0.85f, tint.y * 0.9f, tint.z, 0.4f) : new Color(tint.x, tint.y, tint.z, 0.9f);
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.SetFloat(GlowId, glow);
            }
        }

        void CreateHail()
        {
            hail = CreateParticles("Hail", ParticleSystemRenderMode.Billboard, 6000, 2.2f, 0.035f, new Color(0.95f, 0.97f, 1f, 1f), new Vector3(45f, 1f, 45f), 0f);
            hailGround = new GameObject("HailGround").transform;
            hailGround.SetParent(transform, false);
            var collision = hail.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, hailGround);
            collision.bounce = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            collision.dampen = 0.35f;
            collision.lifetimeLoss = 0.25f;
            var velocity = hail.velocityOverLifetime;
            velocity.enabled = false;   // hail falls under gravity so it can bounce
            var main = hail.main;
            main.gravityModifier = 2.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        }

        void CreateDustVeils()
        {
            dust = CreateParticles("DustVeils", ParticleSystemRenderMode.Billboard, 600, 7f, 10f, new Color(0.8f, 0.65f, 0.45f, 0.16f), new Vector3(90f, 10f, 90f), 2f);
            var main = dust.main;
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var fade = dust.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var grow = dust.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.3f));
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
            var material = new Material(PrecipitationShader != null ? PrecipitationShader : Shader.Find("Punchy/Precipitation"));
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
