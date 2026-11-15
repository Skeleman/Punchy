using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Punchy.CameraRig
{
    /// <summary>
    /// How the local player's eyes take the light (A-103 row 5). Bat eyes (CreatureSpecies.Vision, or glow goggles:
    /// NetAvatars.LocalVision) see caves and the night bright, and the open daytime sun as a glare: highlights blown
    /// and a bright haze over far things, worst at noon in the open, easing in shade (trees overhead), at dusk and
    /// underground. Applied through a global post-processing volume made at runtime (exposure, contrast, bloom) and the
    /// haze globals the sky, terrain and flora shaders read; no asset changes. Local only: each machine's view is its
    /// own player's. Changes ease in over about a second and a half, as eyes adjust.
    /// </summary>
    public class BatVision : MonoBehaviour
    {
        /// <summary>Stops of exposure added in full darkness (deep in a cave, or midnight) for bat eyes.</summary>
        const float DarkLift = 2f;
        /// <summary>Stops of exposure added by a full glare (noon, in the open), and how much it eases contrast.</summary>
        const float GlareExposure = 1.3f, GlareContrast = -18f;
        /// <summary>Bloom added by a full glare, and how far its threshold falls (so far bright things bleed).</summary>
        const float GlareBloom = 2.2f, GlareThreshold = 0.55f;
        /// <summary>How much thicker the far haze gets in a full glare (x density); its clear-day thinning goes.</summary>
        const float GlareHaze = 2.5f;
        /// <summary>Seconds for the eyes to adjust (the ease's time constant).</summary>
        const float Adapt = 1.5f;

        static readonly int HazeDensityId = Shader.PropertyToID("_PunchyHazeDensity");
        static readonly int HazeClearId = Shader.PropertyToID("_PunchyHazeClear");

        /// <summary>For probes and logs: how dark it is (0..1), how much glare (0..1), how shaded the player is (0..1)
        /// and the exposure added, as last applied.</summary>
        public static float Darkness { get; private set; }
        public static float Glare { get; private set; }
        public static float Shade { get; private set; }
        public static float Exposure { get; private set; }

        Volume volume;
        ColorAdjustments adjust;
        Bloom bloom, baseBloom;
        DayNightCycle cycle;
        World world;
        EntityQuery obstacleQuery, playerQuery;
        float darkness, glare, shade, nextShade, loggedVision = -1f;
        // The haze globals as WeatherSystem last set them, and as this last wrote them (so the glare never compounds).
        float baseDensity, baseClear, wroteDensity = float.NaN, wroteClear = float.NaN;

        void Start()
        {
            var go = new GameObject("BatVisionVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 1f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "BatVision (runtime)";
            adjust = profile.Add<ColorAdjustments>(false);
            adjust.postExposure.overrideState = true;
            adjust.contrast.overrideState = true;
            bloom = profile.Add<Bloom>(false);
            bloom.intensity.overrideState = true;
            bloom.threshold.overrideState = true;
            volume.sharedProfile = profile;
            volume.enabled = false;
            // The scene's own bloom, which the glare adds to.
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v != volume && v.sharedProfile != null && v.sharedProfile.TryGet<Bloom>(out var b))
                {
                    baseBloom = b;
                    break;
                }
            cycle = FindAnyObjectByType<DayNightCycle>();
        }

        void OnDestroy()
        {
            if (volume != null && volume.sharedProfile != null)
                Destroy(volume.sharedProfile);
        }

        void LateUpdate()
        {
            var vision = NetAvatars.LocalVision;
            if (!Mathf.Approximately(vision, loggedVision))
            {
                loggedVision = vision;
                Debug.Log($"BatVision: vision {vision:F2}");
            }
            if (cycle == null)
                cycle = FindAnyObjectByType<DayNightCycle>();
            var dt = Time.deltaTime;
            var ease = 1f - Mathf.Exp(-dt / Adapt);

            // How dark it is: underground, or night.
            var night = cycle != null ? cycle.Night : 0f;
            var under = CaveSystem.Underground;
            darkness = Mathf.Lerp(darkness, vision * Mathf.Max(under, night), ease);

            // The glare: the sun high and strong, the player out from under trees, above ground, the lens in air.
            if (vision > 0f && Time.time >= nextShade)
            {
                nextShade = Time.time + 0.2f;
                shade = Mathf.Lerp(shade, ShadeOverPlayer(), 0.5f);
            }
            var sunY = cycle != null ? cycle.SunDirection.y : 0.8f;
            var sunUp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.75f, sunY)) * Mathf.Clamp01(cycle != null ? cycle.LightStrength : 1f);
            var glareNow = vision * sunUp * (1f - shade) * (1f - under) * (Underwater.LensUnder ? 0f : 1f);
            glare = Mathf.Lerp(glare, glareNow, ease);

            Darkness = darkness;
            Glare = glare;
            Shade = shade;
            Exposure = DarkLift * darkness + GlareExposure * glare;
            var on = Exposure > 0.001f || glare > 0.001f;
            if (volume != null)
            {
                volume.enabled = on;
                if (on)
                {
                    adjust.postExposure.value = Exposure;
                    adjust.contrast.value = GlareContrast * glare;
                    // (Off when the scene's bloom is: "-nobloom" shots and the sparkle probe.)
                    var hasBloom = baseBloom != null && baseBloom.active;
                    bloom.active = hasBloom;
                    if (hasBloom)
                    {
                        bloom.intensity.value = baseBloom.intensity.value + GlareBloom * glare;
                        bloom.threshold.value = Mathf.Lerp(baseBloom.threshold.value, baseBloom.threshold.value * (1f - GlareThreshold), glare);
                    }
                }
            }
            ApplyHaze();
        }

        /// <summary>Thickens the far haze in the glare. WeatherSystem sets the globals in its Update; a value that
        /// isn't the one this wrote is a fresh base.</summary>
        void ApplyHaze()
        {
            var density = Shader.GetGlobalFloat(HazeDensityId);
            var clear = Shader.GetGlobalFloat(HazeClearId);
            if (density != wroteDensity)
                baseDensity = density;
            if (clear != wroteClear)
                baseClear = clear;
            wroteDensity = baseDensity * (1f + GlareHaze * glare);
            wroteClear = baseClear * (1f - glare);
            Shader.SetGlobalFloat(HazeDensityId, wroteDensity);
            Shader.SetGlobalFloat(HazeClearId, wroteClear);
        }

        /// <summary>
        /// How shaded the player is from the sun (0 open sky, 1 deep shade): each solid plant near the player (the
        /// FloraObstacle buffer) whose crown, about three quarters up its leaned trunk, lies close to the line from the
        /// player's head towards the sun shades them, more the closer; a kit building's circles shade by their own size.
        /// </summary>
        float ShadeOverPlayer()
        {
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated || cycle == null)
                return 0f;
            if (world != client)
            {
                world = client;
                obstacleQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<FloraObstacle>());
                playerQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            }
            if (!playerQuery.TryGetSingleton<LocalTransform>(out var player) || !obstacleQuery.TryGetSingletonBuffer<FloraObstacle>(out var obstacles, true))
                return 0f;
            var sun = (float3)cycle.SunDirection;
            if (sun.y < 0.05f)
                return 0f;
            var head = player.Position + new float3(0f, 1.6f, 0f);
            var total = 0f;
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                if (o.Height < 2.5f && !o.Kit)
                    continue;
                var crownY = o.Position.y + o.Height * (o.Kit ? 0.6f : 0.75f);
                if (crownY <= head.y)
                    continue;
                // Where the line towards the sun passes the crown's height, and how far that is from the crown.
                var along = (crownY - head.y) / sun.y;
                var at = head + sun * along;
                var centre = FloraObstacle.CentreAt(o.Position, o.Height, o.LeanMid, o.LeanTop, crownY);
                var reach = o.Kit ? math.max(o.Radius, 1f) : math.max(1.5f, 0.3f * o.Height);
                total += math.saturate(1.3f - math.distance(at.xz, centre) / reach);
                if (total >= 1f)
                    return 1f;
            }
            return total;
        }
    }
}
