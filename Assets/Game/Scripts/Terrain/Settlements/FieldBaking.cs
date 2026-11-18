using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-165: farm fields round a settlement. After the roads and roadside landmarks, each settlement whose layout lists
    /// Field buildings rings itself with a patchwork of them just past its edge: two rings deep, walked round from an
    /// angle its own hash picks, each field facing the town (its gate that way) on gentle dry ground clear of streets,
    /// roads, plots, landmarks and other towns. The crop is picked per field by hash among the Field entries for the
    /// biome under the town, so nothing laid out before moves.
    /// </summary>
    public static class FieldBaking
    {
        /// <summary>Clear ground between a field and the settlement's edge (m).</summary>
        const float Margin = 12f;
        /// <summary>How far apart neighbouring fields keep, room for a hedgerow or a path (m).</summary>
        const float Apart = 2.5f;
        const float RingStep = 21f;
        const float AngleStep = 9f;
        const float MaxRelief = 3f;

        /// <summary>W-165: how many fields the last Place laid, for the bake log.</summary>
        public static int LastCount;

        public static void Place(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding,
                                 List<SettlementData> settlements, List<SettlementStreet> streets, List<SettlementPlot> plots,
                                 List<LandmarkInstance> roadside, IReadOnlyList<BiomeDefinition> palette)
        {
            LastCount = 0;
            var seed = source.World.Seed;
            var entries = new List<int>();
            for (int s = 0; s < settlements.Count; s++)
            {
                var town = settlements[s];
                var layout = layouts[town.Layout];
                var biome = Biome(source, town.Centre, palette);
                entries.Clear();
                var total = 0f;
                for (int i = 0; i < layout.Buildings.Count; i++)
                {
                    var b = layout.Buildings[i];
                    if (b.Role != PlotRole.Field || b.Species == null || b.Species.Form != Punchy.Flora.FloraForm.Field ||
                        b.OnlyIn != null && b.OnlyIn.Count > 0 && (biome == null || !b.OnlyIn.Contains(biome)))
                        continue;
                    entries.Add(i);
                    total += b.Weight;
                }
                if (entries.Count == 0 || total <= 0f)
                    continue;
                var key = BiomeSampling.Scramble(math.hash(new uint3(seed, (uint)town.Landmark, 0xF1E1Du)));
                var wanted = math.max(2, (int)math.round(town.Radius / 11f) + (int)(key % 3u) - 1);
                var start = (key >> 8) / 16777216f * math.PI * 2f;
                var made = 0;
                var steps = (int)math.ceil(math.PI * 2f * (town.Radius + Margin + RingStep) / AngleStep);
                for (int k = 0; k < steps && made < wanted; k++)
                    for (int ring = 0; ring < 2 && made < wanted; ring++)
                    {
                        var h = BiomeSampling.Scramble(math.hash(new uint4(seed, (uint)town.Landmark, (uint)(k * 2 + ring), 0xC209u)));
                        var roll = (h & 0xFFFF) / 65536f * total;
                        var pick = entries[entries.Count - 1];
                        foreach (var i in entries)
                        {
                            roll -= layout.Buildings[i].Weight;
                            if (roll < 0f)
                            {
                                pick = i;
                                break;
                            }
                        }
                        var species = layout.Buildings[pick].Species;
                        var half = (float2)species.FieldSize * 0.5f * layout.BuildingScale * 1.05f;
                        var angle = start + k * math.PI * 2f / steps;
                        var dir = new float2(math.cos(angle), math.sin(angle));
                        var centre = town.Centre + dir * (town.Radius + Margin + half.y + ring * RingStep);
                        var facing = -dir;
                        if (!Clear(source, centre, facing, half, s, settlements, streets, plots, roadside, out var pad))
                            continue;
                        var reach = math.length(half) + SettlementSampling.PadBlendMax;
                        plots.Add(new SettlementPlot
                        {
                            Centre = centre,
                            Facing = facing,
                            Half = half,
                            Pad = pad,
                            Role = PlotRole.Field,
                            Building = firstBuilding[town.Layout] + pick,
                            Settlement = s,
                            Min = centre - reach,
                            Max = centre + reach,
                            Tone = Tone(species)
                        });
                        made++;
                    }
                LastCount += made;
            }
        }

        static float3 Tone(Punchy.Flora.FloraSpecies s)
        {
            var leaf = UnityEngine.Color.Lerp(s.Foliage, s.FoliageVariation, 0.5f);
            var c = s.Crop == Punchy.Flora.CropKind.Grain && s.Accents.Count > 0 ? UnityEngine.Color.Lerp(leaf, s.Accents[0], 0.6f) : leaf;
            c = c.linear;
            return new float3(c.r, c.g, c.b);
        }

        static BiomeDefinition Biome(in TerrainSource source, float2 at, IReadOnlyList<BiomeDefinition> palette)
        {
            if (palette == null)
                return null;
            var biomes = source.SampleGround(at).Biomes;
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (biomes.Weights[k] > biomes.Weights[best])
                    best = k;
            var id = (int)biomes.Ids[best];
            return id >= 0 && id < palette.Count ? palette[id] : null;
        }

        static bool Clear(in TerrainSource source, float2 centre, float2 facing, float2 half, int own, List<SettlementData> settlements,
                          List<SettlementStreet> streets, List<SettlementPlot> plots, List<LandmarkInstance> roadside, out float pad)
        {
            pad = 0f;
            var right = new float2(facing.y, -facing.x);
            var bound = math.abs(right) * half.x + math.abs(facing) * half.y;
            float2 lo = centre - bound, hi = centre + bound;
            for (int p = 0; p < plots.Count; p++)
                if (math.all(plots[p].Max > lo) && math.all(plots[p].Min < hi) && Overlap(plots[p], centre, facing, half + Apart))
                    return false;
            for (int t = 0; t < settlements.Count; t++)
            {
                var d = math.distance(centre, settlements[t].Centre) - math.length(half);
                if (d < settlements[t].Radius + (t == own ? Margin * 0.5f : Margin))
                    return false;
            }
            float low = float.MaxValue, high = float.MinValue, sum = 0f;
            var count = 0;
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 6; x++)
                {
                    var at = centre + right * (x / 3f - 1f) * half.x + facing * (y / 2f - 1f) * half.y;
                    foreach (var street in streets)
                    {
                        if (math.any(at < street.Min - 3f) || math.any(at > street.Max + 3f))
                            continue;
                        if (SettlementSampling.Across(street, at, out _) < street.Width * 0.5f + 2.5f)
                            return false;
                    }
                    if ((x & 1) != 0 || (y & 1) != 0)
                        continue;
                    var g = source.SampleGround(at);
                    if (g.HasWater && g.WaterSurface > g.Height - 0.5f || g.River > 0.02f || source.IsCaveHole(at) ||
                        g.Height < source.World.SeaLevel + 1.5f || source.CaveClearance(at, g.Height) < 3f)
                        return false;
                    low = math.min(low, g.Height);
                    high = math.max(high, g.Height);
                    sum += g.Height;
                    count++;
                    foreach (var r in roadside)
                        if (math.distance(at, r.Position) < 12f)
                            return false;
                    if (source.Landmarks.IsCreated)
                    {
                        ref var set = ref source.Landmarks.Value;
                        for (int l = 0; l < set.Instances.Length; l++)
                            if (math.distance(at, set.Instances[l].Position) < set.Types[set.Instances[l].Type].Radius * set.Instances[l].Scale + 2f)
                                return false;
                    }
                }
            if (high - low > MaxRelief)
                return false;
            pad = sum / count;
            return true;
        }

        /// <summary>Whether two turned rectangles overlap (separating axes).</summary>
        static bool Overlap(in SettlementPlot a, float2 centre, float2 facing, float2 half)
        {
            var d = centre - a.Centre;
            var ax = new[] { new float2(a.Facing.y, -a.Facing.x), a.Facing, new float2(facing.y, -facing.x), facing };
            foreach (var axis in ax)
            {
                var ra = math.abs(math.dot(ax[0], axis)) * a.Half.x + math.abs(math.dot(ax[1], axis)) * a.Half.y;
                var rb = math.abs(math.dot(ax[2], axis)) * half.x + math.abs(math.dot(ax[3], axis)) * half.y;
                if (math.abs(math.dot(d, axis)) > ra + rb)
                    return false;
            }
            return true;
        }
    }
}
