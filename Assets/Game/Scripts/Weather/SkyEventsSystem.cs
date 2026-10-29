using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Weather
{
    /// <summary>
    /// Sky events that depend on where the viewer stands: how cold the lands round the camera are (the biomes' Cold,
    /// weighted), eased in and out, set as _PunchyAurora for the sky's auroras (Sky.hlsl). Shooting stars and meteor
    /// showers need nothing from here: they run on the shared clock and the moon's phase. Cosmetic and per machine,
    /// worked out from the camera, so every player sees auroras where they stand.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SkyEventsSystem : SystemBase
    {
        static readonly int AuroraId = Shader.PropertyToID("_PunchyAurora");
        float aurora;

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainSource>();
        }

        protected override void OnUpdate()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var source = SystemAPI.GetSingleton<TerrainSource>();
            BiomePalette palette = null;
            foreach (var p in SystemAPI.Query<BiomePalette>())
                palette = p;
            if (palette == null || palette.Biomes == null)
                return;
            var xz = new float2(cam.transform.position.x, cam.transform.position.z);
            source.SampleBiomes(xz, out var weights, out _);
            var cold = 0f;
            for (int k = 0; k < 4; k++)
                if (weights.Weights[k] > 0f && weights.Ids[k] < palette.Biomes.Length && palette.Biomes[weights.Ids[k]] != null)
                    cold += weights.Weights[k] * palette.Biomes[weights.Ids[k]].Cold;
            // Eased over a few seconds, so walking over a biome edge brings them in gently.
            var target = math.smoothstep(0.35f, 0.75f, cold);
            aurora = math.lerp(aurora, target, 1f - math.exp(-SystemAPI.Time.DeltaTime * 0.5f));
            Shader.SetGlobalFloat(AuroraId, aurora);
        }
    }
}
