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
        /// <summary>Scales the buildings on this layout's plots (SettlementLayout.BuildingScale).</summary>
        public float BuildingScale;
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
        /// <summary>1 for a road between settlements (darker, rutted), 0 for a town's own street or square.</summary>
        public int Road;
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
        /// <summary>The furthest a pad far off the ground's height blends out (m); plot bounds reach this far.</summary>
        public const float PadBlendMax = 6f;

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
            // W-74 (#87): plots no longer level one after another (a later neighbour's blend fringe tilted an earlier
            // plot's pad and left house corners buried or floating). The pads mix weighted by w^8, so a plot's own
            // pad all but wins inside it and the seam between two fringes stays smooth.
            // W-80 (#87): a pad whose ground is far off its level blends out further (PadBlend plus a metre per metre of
            // height, up to PadBlendMax), so a pad cut into a slope gets a bank instead of a 3 m step; plots whose
            // bounds are too tight for that (gates, signposts) keep PadBlend. The weight also grows without bound as w
            // reaches 1, so inside its rectangle a plot's own pad wins outright and a close neighbour's fringe no
            // longer tilts it.
            range = set.PlotCells[cell];
            float best = 0f, sum = 0f, mix = 0f;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var p = ref set.Plots[set.PlotItems[k]];
                // Bridges and palisades stand on the ground as it is.
                if (p.Role == PlotRole.Bridge || p.Role == PlotRole.Palisade || p.Role == PlotRole.Fence || math.any(xz < p.Min) || math.any(xz > p.Max))
                    continue;
                var room = math.cmin(p.Max - p.Centre) - math.length(p.Half);
                var blend = math.max(PadBlend, math.min(PadBlend + math.abs(h - p.Pad), math.min(room, PadBlendMax)));
                var w = 1f - math.smoothstep(0f, blend, Outside(p, xz));
                if (w <= 0f)
                    continue;
                var w2 = w * w;
                var w8 = w2 * w2 * w2 * w2 / math.max(1f - w, 1e-4f);
                sum += w8;
                mix += w8 * p.Pad;
                best = math.max(best, w);
            }
            // At the very edge of a blend w is ~1e-6 and w^8 underflows to 0, so sum can be 0 while best is not: that
            // gave 0/0 = NaN heights on a thin ring round each pad (NaN normals drew black hexes that bloom smeared
            // white). The pad's pull there is nil anyway, so the ground stays as it is.
            return best > 0f && sum > 0f ? math.lerp(h, mix / sum, best) : h;
        }

        /// <summary>Packed earth's colour (linear) where paved streets give way outside a town's centre.</summary>
        static readonly float3 PackedEarth = new float3(0.17f, 0.115f, 0.07f);

        /// <summary>
        /// How cobbled the ground is at distance d from a paved town's centre (W-108): fully out to its edge, fading to
        /// dirt over the next dozen metres, raggedly.
        /// </summary>
        static float Cobbled(float radius, float d, float edgeNoise)
            => 1f - math.smoothstep(radius - 2f, radius + 12f, d + edgeNoise * 4f);

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
                if (street.Paving > 0f)
                {
                    // W-104 (#39): paved layouts' roads turn to packed earth (no cobbles, browner, duller) away from
                    // town. W-108: the town's own streets are cobbled out to its edge; roads take their cobbles below.
                    ref var town = ref set.Settlements[s.Settlement];
                    var paved = s.Road != 0 ? 0f : Cobbled(town.Radius, math.distance(xz, town.Centre), edgeNoise);
                    var earth = street;
                    earth.Paving = 0f;
                    earth.Albedo = math.lerp(street.Albedo, PackedEarth * (1f + colorNoise * 0.15f), 0.75f);
                    earth.Smoothness *= 0.6f;
                    earth.Pebbles = math.max(street.Pebbles, 0.35f);
                    street = SurfaceProps.Lerp(earth, street, paved);
                }
                if (s.Road != 0)
                {
                    // Roads are worn darker than town streets, with two wheel ruts and a darker verge, so they read
                    // on pale ground from eye level.
                    var across = Across(s, xz, out _);
                    var rut = 1f - math.smoothstep(0.12f, 0.4f, math.abs(across - half * 0.42f));
                    var verge = math.smoothstep(half - 1f, half - 0.2f, across);
                    var earthShade = 0.8f * (1f - 0.2f * rut) * (1f - 0.18f * verge);
                    // W-108 (#39): near a paved town (whoever's road it is) the road is cobbled, fading from the dirt
                    // over a short ragged stretch at the town's edge. Paving above 1 is the shader's wear: the stones
                    // down the wheel ruts are rubbed flat; at the verge it dips under 1 and grass gets between them.
                    var cobbled = 0f;
                    var paver = -1;
                    for (int i = 0; i < set.Settlements.Length; i++)
                    {
                        ref var town = ref set.Settlements[i];
                        ref var l = ref set.Layouts[town.Layout];
                        if (l.HasStreetSurface == 0 || l.Street.Props.Paving <= 0f)
                            continue;
                        var c = Cobbled(town.Radius, math.distance(xz, town.Centre), edgeNoise);
                        if (c > cobbled)
                        {
                            cobbled = c;
                            paver = i;
                        }
                    }
                    if (paver >= 0)
                    {
                        ref var l = ref set.Layouts[set.Settlements[paver].Layout];
                        var cobbles = l.Street.Props;
                        cobbles.Albedo *= (1f + colorNoise * l.Street.ColorVariation) * (1f - 0.1f * verge);
                        var worn = 1f - math.smoothstep(0.2f, 0.7f, math.abs(across - half * 0.42f));
                        cobbles.Paving *= 1f + worn * 0.9f - verge * 0.3f;
                        street.Albedo *= earthShade;
                        street = SurfaceProps.Lerp(street, cobbles, cobbled);
                    }
                    else
                        street.Albedo *= earthShade;
                }
                props = SurfaceProps.Lerp(props, street, w);
            }
        }

        /// <summary>
        /// The deck of a bridge span under a point, if the point is on or above it (feet at most 0.6 m below the deck,
        /// so walking up onto it from the bank works and swimming under it doesn't).
        /// </summary>
        public static bool Deck(ref SettlementSetBlob set, float3 p, out float deck)
        {
            deck = 0f;
            var cell = Cell(ref set, p.xz);
            if (cell < 0)
                return false;
            var found = false;
            var range = set.PlotCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var plot = ref set.Plots[set.PlotItems[k]];
                if (plot.Role != PlotRole.Bridge || math.any(p.xz < plot.Min) || math.any(p.xz > plot.Max))
                    continue;
                if (Outside(plot, p.xz) > 0.05f || p.y < plot.Pad - 0.6f || found && plot.Pad < deck)
                    continue;
                deck = plot.Pad;
                found = true;
            }
            return found;
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
