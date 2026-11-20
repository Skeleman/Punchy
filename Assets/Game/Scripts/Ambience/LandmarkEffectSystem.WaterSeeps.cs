using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
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

        readonly Vector4[] streakLip = new Vector4[SeepStreakCount], streakRun = new Vector4[SeepStreakCount];
        readonly List<int> streakOrder = new List<int>();

        void WaterSeepTables(BiomePalette palette)
        {
            seepWet = new NativeArray<float>(palette.Biomes.Length, Allocator.Persistent);
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                var biome = palette.Biomes[b];
                if (biome != null && !biome.HasTag(BiomeTags.Vents) && biome.WaterLava < 0.5f && biome.Cold < 0.75f)
                    seepWet[b] = biome.Rainfall;
            }
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
