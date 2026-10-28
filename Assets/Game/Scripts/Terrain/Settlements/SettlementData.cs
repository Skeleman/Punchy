using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A settlement layout's sampling settings, flattened for Burst.</summary>
    public struct SettlementLayoutData
    {
        public int HasStreetSurface;
        public SurfaceBlobData Street;
        /// <summary>This layout's buildings in the flattened building list (see SettlementBaking.Buildings).</summary>
        public int FirstBuilding, BuildingCount;
    }

    /// <summary>One laid-out settlement.</summary>
    public struct SettlementData
    {
        public float2 Centre;
        public float Radius;
        /// <summary>The folk's size next to humans: phase 5's doors, eaves and paths scale with it.</summary>
        public float Scale;
        /// <summary>Landmark instance and type, and the layout (index into Layouts).</summary>
        public int Landmark, Type, Layout;
        public int FirstStreet, StreetCount, FirstPlot, PlotCount;
        /// <summary>The water feature's spot (a well or fountain, mid-square), and its ground height.</summary>
        public float3 Water;
        /// <summary>Which way the main road leaves (unit, XZ), for roads to join.</summary>
        public float2 Road;
        /// <summary>The palisade's radius (0 = none).</summary>
        public float Palisade;
    }

    /// <summary>
    /// A stretch of street (A to B, at most about 10 m, so its levelling follows the ground), or a round open space
    /// (square, green or yard) when A and B coincide. Width is the whole width (the open space's diameter).
    /// </summary>
    public struct SettlementStreet
    {
        public float3 A, B;
        public float Width;
        public int Settlement;
        public float2 Min, Max;
    }

    /// <summary>A plot: a rectangle levelled to Pad, its front (door side) facing its street.</summary>
    public struct SettlementPlot
    {
        public float2 Centre;
        /// <summary>Unit vector from the plot towards its street (the way its building faces).</summary>
        public float2 Facing;
        /// <summary>Half the frontage (x) and half the depth (y).</summary>
        public float2 Half;
        public float Pad;
        public PlotRole Role;
        /// <summary>What stands on it: an index into the flattened building list, or -1 for nothing yet.</summary>
        public int Building;
        public int Settlement;
        public float2 Min, Max;
    }

    /// <summary>
    /// Every settlement's streets and plots, with a coarse grid so a point only looks at the few that reach it.
    /// </summary>
    public struct SettlementSetBlob
    {
        public BlobArray<SettlementLayoutData> Layouts;
        public BlobArray<SettlementData> Settlements;
        public BlobArray<SettlementStreet> Streets;
        public BlobArray<SettlementPlot> Plots;
        /// <summary>Per cell: (first item, count) in StreetItems and PlotItems.</summary>
        public BlobArray<int2> StreetCells, PlotCells;
        public BlobArray<int> StreetItems, PlotItems;
        public float2 GridOrigin;
        public float CellSize;
        public int2 GridSize;
    }

    /// <summary>Settlement queries for TerrainSource (height, ground) and the flora system (clearing, buildings).</summary>
    public static class SettlementSampling
    {
        /// <summary>How far a street's levelling and paving blend into the ground beside it (m).</summary>
        public const float StreetBlend = 2.5f;
        /// <summary>How far a plot's pad blends into the ground round it (m).</summary>
        public const float PadBlend = 3f;

        /// <summary>The grid cell holding a point, or -1 outside the grid.</summary>
        public static int Cell(ref SettlementSetBlob set, float2 xz)
        {
            var c = (int2)math.floor((xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return -1;
            return c.y * set.GridSize.x + c.x;
        }

        /// <summary>Distance across a street from its centre line (or from an open space's centre), and how far along.</summary>
        public static float Across(in SettlementStreet s, float2 xz, out float t)
        {
            var ab = s.B.xz - s.A.xz;
            t = math.saturate(math.dot(xz - s.A.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return math.distance(xz, s.A.xz + ab * t);
        }

        /// <summary>How far outside a plot's rectangle a point is (0 inside).</summary>
        public static float Outside(in SettlementPlot p, float2 xz)
        {
            var d = xz - p.Centre;
            var local = new float2(d.x * p.Facing.y - d.y * p.Facing.x, math.dot(d, p.Facing));
            return math.length(math.max(math.abs(local) - p.Half, 0f));
        }

        /// <summary>Ground height with streets levelled across and plots levelled to their pads.</summary>
        public static float Height(ref SettlementSetBlob set, float2 xz, float h)
        {
            var cell = Cell(ref set, xz);
            if (cell < 0)
                return h;
            var range = set.StreetCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Streets[set.StreetItems[k]];
                if (math.any(xz < s.Min) || math.any(xz > s.Max))
                    continue;
                var across = Across(s, xz, out var t);
                var w = 1f - math.smoothstep(s.Width * 0.5f, s.Width * 0.5f + StreetBlend, across);
                if (w > 0f)
                    h = math.lerp(h, math.lerp(s.A.y, s.B.y, t), w);
            }
            range = set.PlotCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var p = ref set.Plots[set.PlotItems[k]];
                if (math.any(xz < p.Min) || math.any(xz > p.Max))
                    continue;
                var w = 1f - math.smoothstep(0f, PadBlend, Outside(p, xz));
                if (w > 0f)
                    h = math.lerp(h, p.Pad, w);
            }
            return h;
        }

        /// <summary>Paints the streets' surface over the ground, with a ragged edge.</summary>
        public static void Ground(ref SettlementSetBlob set, float2 xz, float edgeNoise, float colorNoise, ref SurfaceProps props)
        {
            var cell = Cell(ref set, xz);
            if (cell < 0)
                return;
            var range = set.StreetCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Streets[set.StreetItems[k]];
                if (math.any(xz < s.Min) || math.any(xz > s.Max))
                    continue;
                ref var layout = ref set.Layouts[set.Settlements[s.Settlement].Layout];
                if (layout.HasStreetSurface == 0)
                    continue;
                var half = s.Width * 0.5f;
                var w = 1f - math.smoothstep(half - 0.6f, half + 0.6f, Across(s, xz, out _) + edgeNoise * 0.8f);
                if (w <= 0f)
                    continue;
                var street = layout.Street.Props;
                street.Albedo *= 1f + colorNoise * layout.Street.ColorVariation;
                props = SurfaceProps.Lerp(props, street, w);
            }
        }

        /// <summary>How much plants are kept here (0 on streets and built plots, 1 away from them).</summary>
        public static float Keep(ref SettlementSetBlob set, float2 xz)
        {
            var cell = Cell(ref set, xz);
            if (cell < 0)
                return 1f;
            var keep = 1f;
            var range = set.StreetCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Streets[set.StreetItems[k]];
                if (math.any(xz < s.Min) || math.any(xz > s.Max))
                    continue;
                keep = math.min(keep, math.smoothstep(s.Width * 0.5f + 0.5f, s.Width * 0.5f + 2f, Across(s, xz, out _)));
            }
            range = set.PlotCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var p = ref set.Plots[set.PlotItems[k]];
                // Green plots keep their plants, unless a garden stands on them.
                if (p.Role == PlotRole.Landscaping && p.Building < 0 || math.any(xz < p.Min) || math.any(xz > p.Max))
                    continue;
                keep = math.min(keep, math.smoothstep(0.5f, 2f, Outside(p, xz)));
            }
            return keep;
        }
    }
}
