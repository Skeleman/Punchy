using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Turtle folk settlements (#38): a settlement type on the highest mountaintops. A settlement is a landmark
    /// (Resources/TurtleVillage) whose placed props are its buildings, all from StructureKit in the turtle folk's trim:
    /// domed snow-block igloos with tall doors and a faint rune glow, and mountaintop details (prayer-flag lines,
    /// snow-buried pots, rune stones, cairns, ice lanterns, sleds) seeded per prop, so no two settlements look alike.
    /// They go in after every other place (fishing villages included), each on its own new spot, so nothing that was
    /// placed before moves. Island v2 only.
    /// </summary>
    public static class TurtleVillageBaking
    {
        public const string TypeName = "TurtleVillage";
        const int Most = 3;
        const float Apart = 600f;
        /// <summary>The peak search grid (m): a local maximum on this grid is a peak.</summary>
        const float Step = 48f;
        /// <summary>The most the igloo ring may stand above or below the middle (m): they sit on the slope.</summary>
        const float Relief = 10f;

        public static string LastLog = "";

        /// <summary>The turtle village type (a Resources asset, so island v2's runtime bake has it too).</summary>
        public static LandmarkType LoadType() => UnityEngine.Resources.Load<LandmarkType>(TypeName);

        /// <summary>Up to three turtle folk settlements on the highest peaks, as landmark instances to append.</summary>
        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<LandmarkType> types)
        {
            var placed = new List<LandmarkInstance>();
            LastLog = "";
            var t = -1;
            for (int i = 0; i < types.Count; i++)
                if (types[i] != null && types[i].name == TypeName)
                    t = i;
            if (t < 0 || !source.Rivers.IsCreated || !source.Landmarks.IsCreated)
            {
                LastLog = "[turtles] none: no TurtleVillage type or no rivers";
                return placed;
            }
            var type = types[t];
            ref var rivers = ref source.Rivers.Value;
            ref var set = ref source.Landmarks.Value;
            var sea = source.World.SeaLevel;
            var min = rivers.Origin;
            var n = (int)math.floor(rivers.Resolution * rivers.CellSize / Step);

            // Heights on a coarse grid (sea where there is no land), then every cell higher than its eight neighbours.
            var heights = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = min + (new float2(x, y) + 0.5f) * Step;
                    heights[y * n + x] = source.OceanFactor(p) > 0.5f ? sea : source.SampleHeight(p);
                }
            var peaks = new List<int>();
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    var h = heights[y * n + x];
                    if (h < sea + 20f)
                        continue;
                    var top = true;
                    for (int dy = -1; dy <= 1 && top; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dy != 0) && heights[(y + dy) * n + x + dx] >= h)
                            {
                                top = false;
                                break;
                            }
                    if (top)
                        peaks.Add(y * n + x);
                }
            // Highest first (the cell index breaks ties, so the order is fixed).
            peaks.Sort((a, b) => heights[a] != heights[b] ? heights[b].CompareTo(heights[a]) : a.CompareTo(b));

            var tried = 0;
            foreach (var c in peaks)
            {
                if (placed.Count >= Most)
                    break;
                tried++;
                var peak = min + (new float2(c % n, c / n) + 0.5f) * Step;
                // The peak itself, then a ring of spots just below it, until one fits.
                for (int k = 0; k < 17; k++)
                {
                    var centre = k == 0 ? peak : peak + new float2(math.cos(k * math.PI / 4f), math.sin(k * math.PI / 4f)) * (k <= 8 ? 40f : 80f);
                    // Facing downhill (the way the settlement looks out), from the slope at the middle.
                    var e = 6f;
                    var grad = new float2(source.SampleHeight(centre + new float2(e, 0f)) - source.SampleHeight(centre - new float2(e, 0f)),
                                          source.SampleHeight(centre + new float2(0f, e)) - source.SampleHeight(centre - new float2(0f, e)));
                    var dir = math.lengthsq(grad) > 1e-6f ? -math.normalize(grad) : new float2(0f, 1f);
                    if (!Fits(source, ref set, types, type, placed, centre, dir, sea))
                        continue;
                    var key = math.hash(new uint3(math.asuint(centre.x), math.asuint(centre.y), 0x7A87u)) | 1u;
                    placed.Add(new LandmarkInstance
                    {
                        Position = centre,
                        Facing = dir,
                        Scale = 1f,
                        Type = t,
                        BaseHeight = source.SampleHeight(centre),
                        Seed = (BiomeSampling.Scramble(key) >> 8) * (1f / 16777216f),
                        Key = key
                    });
                    LastLog += (LastLog.Length > 0 ? "\n" : "") + $"[turtles] turtle settlement {placed.Count} at {centre.x:F0},{centre.y:F0} height {source.SampleHeight(centre):F0} (peak {heights[c]:F0}) facing {math.degrees(math.atan2(dir.y, dir.x)):F0} deg";
                    break;
                }
            }
            LastLog = $"[turtles] {placed.Count} turtle settlements from {peaks.Count} peaks ({tried} tried)" + (LastLog.Length > 0 ? "\n" + LastLog : "");
            UnityEngine.Debug.Log(LastLog);
            return placed;
        }

        /// <summary>Dry ground clear of every landmark and settlement, with every igloo spot on land within Relief of the middle.</summary>
        static bool Fits(in TerrainSource source, ref LandmarkSetBlob set, IReadOnlyList<LandmarkType> types, LandmarkType type,
                         List<LandmarkInstance> placed, float2 centre, float2 dir, float sea)
        {
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var other = set.Instances[i];
                var reach = type.Radius + (other.Type < types.Count && types[other.Type] != null ? types[other.Type].Radius * other.Scale : 30f) + 40f;
                if (math.distance(other.Position, centre) < reach)
                    return false;
            }
            foreach (var v in placed)
                if (math.distance(v.Position, centre) < Apart)
                    return false;
            var mid = source.SampleHeight(centre);
            if (source.OceanFactor(centre) > 0.5f || mid < sea + 20f || InRiver(source, centre))
                return false;
            // The building rings (0.42 and 0.68 radius, eight ways round): on land, out of rivers, within Relief of the middle.
            var inst = new LandmarkInstance { Position = centre, Facing = dir, Scale = 1f };
            foreach (var ring in new[] { 0.42f, 0.68f })
                for (int k = 0; k < 8; k++)
                {
                    var spot = LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(ring, k * 45f), type.Radius);
                    var h = source.SampleHeight(spot);
                    if (source.OceanFactor(spot) > 0.5f || math.abs(h - mid) > Relief || InRiver(source, spot))
                        return false;
                }
            return true;
        }

        static bool InRiver(in TerrainSource source, float2 xz)
        {
            ref var rivers = ref source.Rivers.Value;
            var cell = (int2)math.floor((xz - rivers.Origin) / rivers.CellSize);
            if (math.any(cell < 0) || math.any(cell >= rivers.Resolution))
                return false;
            return rivers.Width[cell.y * rivers.Resolution + cell.x] > 0f;
        }
    }
}
