using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-122: places the marsh folk's (lizardfolk) stilt villages after every other landmark, on their own spots: up to
    /// <see cref="Villages"/> of them in the largest patch of the biome tagged BiomeTags.MarshFolk (the Foggy marsh), each
    /// the deepest level, clear cell left, clear of every landmark's footprint and of each other. A village flattens its
    /// ground and digs a murky pool (MarshFolkVillage.asset) with its reed huts on stilts round a smoke hut over the water.
    /// Nothing else moves; on an island without that biome there are simply none.
    /// </summary>
    public static class MarshFolkBaking
    {
        public const string TypeName = "MarshFolkVillage";
        const int Villages = 2;
        const float Footprint = 90f, MaxRelief = 4f, ReliefStep = 15f, Steepest = 10f;

        public static LandmarkType LoadType() => Resources.Load<LandmarkType>(TypeName);

        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, IReadOnlyList<LandmarkType> types)
        {
            var result = new List<LandmarkInstance>();
            var t = -1;
            for (int i = 0; i < types.Count; i++)
                if (types[i] != null && types[i].name == TypeName)
                    t = i;
            var biome = BiomeTags.First(palette, BiomeTags.MarshFolk);
            if (t < 0 || biome < 0 || !source.Landmarks.IsCreated)
                return result;
            var listed = new byte[palette.Count];
            listed[biome] = 1;
            for (int v = 0; v < Villages; v++)
            {
                if (!SaltTowerBaking.FindSpot(source, types, listed, TypeName, Footprint, MaxRelief, ReliefStep, result,
                                              out var at, out var relief, out _, out var patch, out var inFromEdge, out var why))
                {
                    Debug.Log(why ?? $"[landmarks] {TypeName}: no spot for village {v + 1}");
                    break;
                }
                // Past the first village, only a level spot will do: a later one on a fallback slope would sit in a pit.
                if (v > 0 && relief > Steepest)
                {
                    Debug.Log($"[landmarks] {TypeName}: village {v + 1} skipped, the best spot left has {relief:F1} m relief");
                    break;
                }
                var key = math.hash(new uint3(math.asuint(at.x), math.asuint(at.y), 0x3A75u)) | 1u;
                var spin = BiomeSampling.Scramble(key);
                var facing = (spin & 0xFFFF) * (2f * math.PI / 65536f);
                // Base: the median ground over the village, so its pool neither floods the marsh nor sits dry.
                var heights = new List<float>();
                for (float u = -40f; u <= 40f; u += 8f)
                    for (float w = -40f; w <= 40f; w += 8f)
                        heights.Add(source.SampleHeight(at + new float2(u, w)));
                heights.Sort();
                var inst = new LandmarkInstance
                {
                    Position = at,
                    Facing = new float2(math.cos(facing), math.sin(facing)),
                    Scale = 1f,
                    Type = t,
                    BaseHeight = heights[heights.Count / 2],
                    Seed = (BiomeSampling.Scramble(spin) >> 8) * (1f / 16777216f),
                    Key = key
                };
                result.Add(inst);
                Debug.Log($"[landmarks] {TypeName} {v + 1} at {at.x:F1},{at.y:F1}: facing {math.degrees(facing):F0} deg, base {inst.BaseHeight:F1}, " +
                          $"relief {relief:F2} m, patch {patch} cells, {inFromEdge:F0} m in from its edge");
            }
            return result;
        }
    }
}
