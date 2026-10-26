using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Punchy.Ambience
{
    /// <summary>
    /// Small environmental touches driven by where the player is and what the weather and time of day are doing:
    ///  - heat shimmer and mirages over hot ground on sunny days (a full-screen pass reads a heat map of the land
    ///    around the camera, built from each biome's Heat);
    ///  - the player's breath showing as puffs of vapour in the cold, and powder kicked up by footsteps in snow;
    ///  - how many fireflies and butterflies are out (drawn by CritterSystem).
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class AmbienceSystem : MonoBehaviour
    {
        public WeatherSystem Weather;
        public DayNightCycle Cycle;
        [Tooltip("Material for breath and snow puffs (Punchy/Soft Particle).")]
        public Material PuffMaterial;
        [Tooltip("The heat haze full-screen pass on the renderer; switched off when nothing is hot.")]
        public ScriptableRendererFeature HeatHaze;

        [Header("Heat")]
        [Tooltip("Width of the heat map around the camera, in metres.")]
        public float HeatMapSize = 6000f;
        public int HeatMapResolution = 128;

        [Header("Breath")]
        [Tooltip("Seconds between breaths when walking (half that when sprinting).")]
        public float BreathInterval = 3.2f;
        [Tooltip("How cold it must be (0..1) before breath shows.")]
        public float BreathThreshold = 0.35f;
        [Tooltip("Height of the mouth above the feet, and how far in front of the body's centre.")]
        public Vector2 Mouth = new Vector2(1.5f, 0.3f);

        /// <summary>0..1 conditions where the player is (for debugging and other systems).</summary>
        public float Heat { get; private set; }
        public float Cold { get; private set; }
        public float Fireflies { get; private set; }
        public float Butterflies { get; private set; }

        static readonly int HeatId = Shader.PropertyToID("_PunchyHeat");
        static readonly int HeatMapId = Shader.PropertyToID("_PunchyHeatMap");
        static readonly int HeatRectId = Shader.PropertyToID("_PunchyHeatRect");
        static readonly int CrittersId = Shader.PropertyToID("_PunchyCritters");

        World queryWorld;
        EntityQuery terrainQuery, paletteQuery, playerQuery;
        TerrainSource lastSource;
        NativeArray<float4> biomeAmbience;   // heat, cold, fireflies, butterflies per biome id

        Texture2D heatMap;
        NativeArray<byte> heatPixels;
        JobHandle heatJob;
        bool heatPending;
        float2 heatOrigin, pendingOrigin;
        float heatMapMax;
        bool hasHeatMap;

        ParticleSystem puffs;
        float breathTimer, exhaleLeft, stepDistance;
        bool leftFoot;
        float3 lastPlayerPosition;
        bool hasLastPosition;

        void OnDisable()
        {
            heatJob.Complete();
            heatPending = false;
            if (heatPixels.IsCreated) heatPixels.Dispose();
            if (biomeAmbience.IsCreated) biomeAmbience.Dispose();
            if (heatMap != null) Destroy(heatMap);
            heatMap = null;
            hasHeatMap = false;
            if (puffs != null) Destroy(puffs.gameObject);
            Shader.SetGlobalFloat(HeatId, 0f);
            Shader.SetGlobalVector(CrittersId, Vector4.zero);
            if (HeatHaze != null) HeatHaze.SetActive(false);
        }

        void Update()
        {
            if (!TryGetWorld(out var source, out var palette, out var player, out var grounded))
                return;
            if (!biomeAmbience.IsCreated || !source.Equals(lastSource) || biomeAmbience.Length != palette.Biomes.Length)
                Rebuild(source, palette);

            var cam = Camera.main;
            var camXZ = cam != null ? new float2(cam.transform.position.x, cam.transform.position.z) : player.Position.xz;
            UpdateHeatMap(source, camXZ);
            UpdateConditions(source, player.Position);
            UpdatePuffs(source, player, grounded);
        }

        // ------------------------------------------------------------------------------------------- setup

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette, out LocalTransform player, out bool grounded)
        {
            source = default;
            palette = null;
            player = default;
            grounded = false;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PlayerMotion>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1 || playerQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            player = playerQuery.GetSingleton<LocalTransform>();
            grounded = playerQuery.GetSingleton<PlayerMotion>().Grounded;
            return palette != null && palette.Biomes != null;
        }

        void Rebuild(in TerrainSource source, BiomePalette palette)
        {
            heatJob.Complete();
            heatPending = false;
            hasHeatMap = false;
            if (biomeAmbience.IsCreated) biomeAmbience.Dispose();
            biomeAmbience = new NativeArray<float4>(palette.Biomes.Length, Allocator.Persistent);
            for (int i = 0; i < palette.Biomes.Length; i++)
            {
                var b = palette.Biomes[i];
                if (b != null)
                    biomeAmbience[i] = new float4(b.Heat, b.Cold, b.Fireflies, b.Butterflies);
            }
            lastSource = source;
        }

        /// <summary>Weighted ambience of the biomes at a point.</summary>
        float4 AmbienceAt(in BiomeWeights biomes)
        {
            var a = float4.zero;
            for (int i = 0; i < 4; i++)
                if (biomes.Weights[i] > 0f && biomes.Ids[i] < biomeAmbience.Length)
                    a += biomeAmbience[biomes.Ids[i]] * biomes.Weights[i];
            return a;
        }

        // ------------------------------------------------------------------------------------------- heat

        void UpdateHeatMap(in TerrainSource source, float2 camXZ)
        {
            var res = HeatMapResolution;
            if (heatPending && heatJob.IsCompleted)
            {
                heatJob.Complete();
                heatPending = false;
                if (heatMap == null || heatMap.width != res)
                {
                    if (heatMap != null) Destroy(heatMap);
                    heatMap = new Texture2D(res, res, TextureFormat.R8, false, true)
                    {
                        name = "Heat Map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                    };
                }
                heatMap.SetPixelData(heatPixels, 0);
                heatMap.Apply(false);
                heatMapMax = 0f;
                foreach (var p in heatPixels)
                    heatMapMax = math.max(heatMapMax, p / 255f);
                heatOrigin = pendingOrigin;
                hasHeatMap = true;
                Shader.SetGlobalTexture(HeatMapId, heatMap);
                Shader.SetGlobalVector(HeatRectId, new Vector4(heatOrigin.x, heatOrigin.y, 1f / HeatMapSize, 1f / HeatMapSize));
            }

            // Re-centre when the camera has moved a good way from the middle of the map.
            var centre = heatOrigin + HeatMapSize * 0.5f;
            if (heatPending || (hasHeatMap && math.distance(camXZ, centre) < HeatMapSize * 0.15f))
                return;
            if (!heatPixels.IsCreated || heatPixels.Length != res * res)
            {
                if (heatPixels.IsCreated) heatPixels.Dispose();
                heatPixels = new NativeArray<byte>(res * res, Allocator.Persistent);
            }
            pendingOrigin = math.floor((camXZ - HeatMapSize * 0.5f) / 100f) * 100f;
            heatJob = new HeatMapJob
            {
                Source = source,
                Ambience = biomeAmbience,
                Origin = pendingOrigin,
                Texel = HeatMapSize / res,
                Resolution = res,
                Pixels = heatPixels
            }.Schedule(res * res, 64);
            heatPending = true;
        }

        [BurstCompile]
        struct HeatMapJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float4> Ambience;
            public float2 Origin;
            public float Texel;
            public int Resolution;
            [WriteOnly] public NativeArray<byte> Pixels;

            public void Execute(int i)
            {
                var xz = Origin + (new float2(i % Resolution, i / Resolution) + 0.5f) * Texel;
                Source.SampleBiomes(xz, out var biomes, out _);
                var heat = 0f;
                for (int b = 0; b < 4; b++)
                    if (biomes.Weights[b] > 0f && biomes.Ids[b] < Ambience.Length)
                        heat += Ambience[biomes.Ids[b]].x * biomes.Weights[b];
                // Water stays cool.
                if (Source.SampleHeight(xz) < Source.World.SeaLevel + 0.5f)
                    heat = 0f;
                Pixels[i] = (byte)math.round(math.saturate(heat) * 255f);
            }
        }

        // ------------------------------------------------------------------------------------------- conditions

        void UpdateConditions(in TerrainSource source, float3 playerPosition)
        {
            var xz = playerPosition.xz;
            var ground = source.SampleGround(xz);
            var surface = source.SampleSurface(xz, ground, source.SampleNormal(xz, 1f));
            var biome = AmbienceAt(ground.Biomes);

            var w = Weather != null ? Weather.Local : default;
            var night = Cycle != null ? Cycle.Night : 0f;
            var sunHeight = Cycle != null ? Cycle.SunDirection.y : 0.7f;
            var dry = 1f - math.saturate(w.Precipitation * 2f);
            var calm = 1f - math.smoothstep(0.45f, 0.85f, w.Wind);
            var elevation = ground.Height - source.World.SeaLevel;
            var snowing = w.Precipitation * math.max(w.AlwaysSnow, Weather != null ? math.step(Weather.SnowLine, elevation) : 0f);

            // Heat: strong sun on clear, dry days. Where it shows is up to the heat map.
            Heat = math.smoothstep(0.15f, 0.6f, sunHeight) * (1f - w.Coverage * 0.75f) * dry * (1f - w.Dust * 0.7f);
            // Cold: the biome, height, night, snow on the ground or falling; warm biomes push back.
            Cold = math.saturate(biome.y + math.saturate((elevation - 90f) / 160f) * 0.6f + night * 0.25f +
                                 surface.Snow * 0.4f + snowing * 0.4f - biome.x * 0.6f);
            Fireflies = night * dry * calm * (1f - math.smoothstep(0.4f, 0.7f, Cold));
            Butterflies = math.smoothstep(0.1f, 0.4f, sunHeight) * dry * calm * math.lerp(1f, 0.35f, w.Coverage) * (1f - math.smoothstep(0.4f, 0.7f, Cold));

            var heatVisible = Heat * (hasHeatMap ? heatMapMax : 0f);
            Shader.SetGlobalFloat(HeatId, Heat);
            Shader.SetGlobalVector(CrittersId, new Vector4(Fireflies, Butterflies, 0f, 0f));
            if (HeatHaze != null && HeatHaze.isActive != heatVisible > 0.02f)
                HeatHaze.SetActive(heatVisible > 0.02f);
        }

        // ------------------------------------------------------------------------------------------- breath & snow

        void UpdatePuffs(in TerrainSource source, LocalTransform player, bool grounded)
        {
            if (PuffMaterial == null)
                return;
            if (puffs == null)
                puffs = CreatePuffSystem(PuffMaterial);

            var dt = Time.deltaTime;
            var position = player.Position;
            var velocity = hasLastPosition && dt > 0f ? (position - lastPlayerPosition) / dt : float3.zero;
            if (math.lengthsq(velocity) > 400f)
                velocity = float3.zero;   // teleported
            lastPlayerPosition = position;
            hasLastPosition = true;
            var speed = math.length(velocity.xz);
            var wind = Weather != null ? (Vector3)new float3(Weather.WindDirection.x, 0f, Weather.WindDirection.y) * Weather.Local.Wind * 1.5f : Vector3.zero;
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));

            // Breath: an exhale every few seconds (quicker when running), as a short stream of vapour.
            var breathe = math.smoothstep(BreathThreshold, BreathThreshold + 0.3f, Cold);
            breathTimer -= dt;
            if (breathTimer <= 0f)
            {
                breathTimer = BreathInterval * (speed > 8f ? 0.5f : 1f) * UnityEngine.Random.Range(0.85f, 1.15f);
                exhaleLeft = 0.45f;
            }
            if (exhaleLeft > 0f && breathe > 0f)
            {
                exhaleLeft -= dt;
                var mouth = position + new float3(0f, Mouth.x, 0f) + forward * Mouth.y;
                var count = UnityEngine.Random.value < dt * 30f ? 1 : 0;
                for (int i = 0; i < count; i++)
                {
                    puffs.Emit(new ParticleSystem.EmitParams
                    {
                        position = mouth,
                        velocity = (Vector3)(forward * 0.7f + velocity * 0.85f + new float3(0f, -0.05f, 0f)) + wind + UnityEngine.Random.insideUnitSphere * 0.08f,
                        startSize = UnityEngine.Random.Range(0.1f, 0.15f),
                        startLifetime = UnityEngine.Random.Range(1.2f, 1.8f),
                        startColor = new Color(1f, 1f, 1f, 0.75f * breathe),
                        rotation = UnityEngine.Random.Range(0f, 360f),
                        applyShapeToPosition = false
                    }, 1);
                }
            }

            // Footsteps in snow throw up a little powder.
            var snow = 0f;
            if (grounded && speed > 1f)
            {
                var ground = source.SampleGround(position.xz);
                snow = source.SampleSurface(position.xz, ground, source.SampleNormal(position.xz, 1f)).Snow;
            }
            if (snow > 0.3f)
            {
                stepDistance += speed * dt;
                var stride = speed > 8f ? 1.6f : 1.1f;
                if (stepDistance >= stride)
                {
                    stepDistance -= stride;
                    leftFoot = !leftFoot;
                    var side = math.normalizesafe(new float3(forward.z, 0f, -forward.x)) * (leftFoot ? -0.15f : 0.15f);
                    for (int i = 0; i < 5; i++)
                    {
                        puffs.Emit(new ParticleSystem.EmitParams
                        {
                            position = position + side + new float3(0f, 0.05f, 0f),
                            velocity = (Vector3)(velocity * 0.25f + new float3(0f, UnityEngine.Random.Range(0.3f, 0.8f), 0f)) + wind * 0.5f + UnityEngine.Random.insideUnitSphere * 0.4f,
                            startSize = UnityEngine.Random.Range(0.1f, 0.2f) * (speed > 8f ? 1.5f : 1f),
                            startLifetime = UnityEngine.Random.Range(0.6f, 1.1f),
                            startColor = new Color(0.95f, 0.97f, 1f, 0.55f * math.saturate(snow)),
                            rotation = UnityEngine.Random.Range(0f, 360f),
                            applyShapeToPosition = false
                        }, 1);
                    }
                }
            }
            else
                stepDistance = 0f;
        }

        static ParticleSystem CreatePuffSystem(Material material)
        {
            var go = new GameObject("Puffs");
            go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.startSpeed = 0f;
            main.gravityModifier = -0.015f;   // warm breath rises a little
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // Puffs swell and slow as they mix with the air, and fade out.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 5f)));
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.6f;
            drag.multiplyDragByParticleSize = false;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
            return ps;
        }
    }
}
