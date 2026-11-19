using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Ambience
{
    public partial class LandmarkEffectSystem
    {
        const float WaterSeepBankShare = 0.6f;
        const float WaterSeepMossShare = 0.25f;
        const float WaterTubeSeepShare = 0.12f;
        const int WaterPoolSeeps = 2;
        const int SeepStreakCount = 8;

        static readonly int SeepLipId = Shader.PropertyToID("_PunchySeepLip");
        static readonly int SeepRunId = Shader.PropertyToID("_PunchySeepRun");
        static readonly int SeepCountId = Shader.PropertyToID("_PunchySeepCount");
        static readonly int SeepRectId = Shader.PropertyToID("_PunchySeepRect");

        float[] seepWet = System.Array.Empty<float>();
        readonly Vector4[] streakLip = new Vector4[SeepStreakCount], streakRun = new Vector4[SeepStreakCount];
        readonly List<int> streakOrder = new List<int>();

        void WaterSeepTables(BiomePalette palette)
        {
            seepWet = new float[palette.Biomes.Length];
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                var biome = palette.Biomes[b];
                if (biome != null && !biome.HasTag(BiomeTags.Vents) && biome.WaterLava < 0.5f && biome.Cold < 0.75f)
                    seepWet[b] = biome.Rainfall;
            }
        }

        float SeepWetness(in BiomeWeights biomes)
        {
            var w = 0f;
            for (int k = 0; k < 4; k++)
                if (biomes.Weights[k] > 0f && biomes.Ids[k] >= 0 && biomes.Ids[k] < seepWet.Length)
                    w += biomes.Weights[k] * seepWet[biomes.Ids[k]];
            return w;
        }

        bool WaterSeepSite(in TerrainSource source, in GroundSample ground, float2 foot, float2 down, uint h)
        {
            var wet = SeepWetness(ground.Biomes);
            if (wet <= 0.05f)
                return false;
            var roll = ((h >> 16) & 0xFF) / 255f;
            for (var reach = 1f; reach <= 7f; reach += 3f)
            {
                var below = source.SampleGround(foot + down * reach);
                if (below.HasWater && below.WaterSurface > below.Height + 0.05f)
                    return roll < WaterSeepBankShare;
            }
            return roll < WaterSeepMossShare * math.saturate((wet - 0.45f) / 0.35f);
        }

        static float4 WaterSeepLook(in TerrainSource source, in SeepSpot s, out float2 glow)
        {
            source.SampleBiomes(s.Lip.xz, out var biomes, out _);
            return source.WaterLook(s.Lip.xz, biomes, out glow);
        }

        void SeepStreaks(float3 eye, float range)
        {
            streakOrder.Clear();
            for (int i = 0; i < seeps.Count; i++)
                if (seeps[i].Kind == SeepKind.Water)
                    streakOrder.Add(i);
            streakOrder.Sort((a, b) => math.distancesq(seeps[a].Lip, eye).CompareTo(math.distancesq(seeps[b].Lip, eye)));
            var n = 0;
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var i in streakOrder)
            {
                if (n == SeepStreakCount)
                    break;
                var s = seeps[i];
                var fade = 1f - math.smoothstep(0.75f, 1f, math.distance(s.Lip, eye) / math.max(range, 1f));
                if (fade < 0.01f)
                    continue;
                var dir = math.normalizesafe(s.Run, new float2(1f, 0f));
                var reach = math.length(s.Run) * s.Drop;
                var half = s.Width * 0.6f;
                streakLip[n] = new Vector4(s.Lip.x, s.Lip.y, s.Lip.z, half);
                streakRun[n] = new Vector4(dir.x * fade, dir.y * fade, s.Drop, reach);
                var grow = half * 4f + 0.5f;
                var foot = s.Lip.xz + dir * reach;
                min = math.min(min, math.min(s.Lip.xz, foot) - grow);
                max = math.max(max, math.max(s.Lip.xz, foot) + grow);
                n++;
            }
            Shader.SetGlobalVectorArray(SeepLipId, streakLip);
            Shader.SetGlobalVectorArray(SeepRunId, streakRun);
            Shader.SetGlobalFloat(SeepCountId, n);
            Shader.SetGlobalVector(SeepRectId, n > 0 ? new Vector4(min.x, min.y, max.x, max.y) : Vector4.zero);
        }

        static void ClearSeepStreaks() => Shader.SetGlobalFloat(SeepCountId, 0f);
    }
}
