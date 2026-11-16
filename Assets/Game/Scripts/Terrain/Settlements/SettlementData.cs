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
        /// <summary>W-108 (#39): the paved square's diameter round the middle (0 = none), and the world angles where
        /// lanes come into it (up to four; 99 = none), which folk's feet have worn paths towards.</summary>
        public float Square;
        public float4 Mouths;
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
        /// <summary>1 for a road between settlements (darker, rutted), 2 for a road stretch graded through a cutting or
        /// over an embankment (W-111: its banks blend wider), 3 for a road stretch bored through a tunnel (W-110: the
        /// hill stays over it, so it neither levels nor paints the ground; CaveBaking bores the passage and the cave
        /// walls carry its surface), 0 for a town's own street or square.</summary>
        public int Road;
        public float2 Min, Max;
        /// <summary>W-138 row 3: for a road stretch near a tunnel portal, that portal's frame: the way into the hill
        /// (xy, unit), the face's offset along it (z: dot(portal, way in)) and the cutting's half width at the headwall
        /// (w); w 0 for every other street. Near the portal the stretch levels a narrow, steep-walled cutting instead
        /// of its banks, and never past the headwall into the hill (see SettlementSampling.Height).</summary>
        public float4 Portal;
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
        /// <summary>W-111: how far a graded road's banks reach per metre of cutting or embankment, and at most (m).</summary>
        public const float GradeBank = 1.5f, GradeBlendMax = 14f;
        /// <summary>How far a plot's pad blends into the ground round it (m).</summary>
        public const float PadBlend = 3f;
        /// <summary>The furthest a pad far off the ground's height blends out (m); plot bounds reach this far.</summary>
        public const float PadBlendMax = 6f;

        /// <summary>SettlementStreet.Road for a stretch bored through a tunnel (W-110).</summary>
        public const int TunnelRoad = 3;

        /// <summary>
        /// W-138 row 3: a tunnel portal's cutting. For PortalWing metres out from the headwall the cutting is flat to
        /// its half width (PortalCutHalf, just behind the wing walls) with walls only PortalCutBlend wide; over the next
        /// PortalTaper metres it widens back into the road's own banks. Nothing levels past PortalBack into the hill,
        /// so the headwall stands in the slope instead of on a cleared floor (the "arch embedded in flat ground" look).
        /// </summary>
        public const float PortalWing = 6f, PortalTaper = 8f, PortalCutBlend = 0.6f, PortalBack = 0.6f;
        /// <summary>
        /// W-138 row 3b: the ledge behind a headwall. Where the hill rises steeply straight behind a portal, the ground
        /// there is capped at PortalLedge over the road for PortalLedgeBack metres into the hill (as wide as the
        /// cutting), then the slope rises on behind it. The terrain mesh drops every triangle touching the cave's mouth,
        /// and with the ground going straight from the road up to the hilltop in one cell, that dropped a quad the
        /// whole height of the cliff: a window through the ridge over the headwall. Capped, the dropped quads reach only
        /// the ledge, which the headwall (raised to the ground behind it) hides.
        /// </summary>
        public const float PortalLedgeBack = 4f;

        /// <summary>W-138 row 3b: the ledge's height over the road for a cutting this half wide: 2 m over the passage's
        /// crown (its head room is 1.45 radius), so the cave's mouth stays clear of it.</summary>
        public static float PortalLedge(float cutHalf) => 1.45f * (cutHalf - PortalWall - 1.6f) / 0.95f + 2f;

        /// <summary>W-138 row 3: how far out from a portal a road stretch takes its frame (m).</summary>
        public const float PortalReach = 40f;

        /// <summary>A road tunnel's passage radius for its road's width (the road and half a metre more each side all
        /// floor: the floor is 0.89 radius either side of the axis).</summary>
        public static float TunnelRadius(float width) => math.max(3f, width * 0.5f / 0.89f + 0.6f);

        /// <summary>W-138 row 3: a portal cutting's half width at the headwall: the arch's outer ring (at most 0.95 r
        /// plus 0.72) and a little headwall either side of it.</summary>
        public static float PortalHalf(float radius) => 0.95f * radius + 1.5f;

        /// <summary>W-138 row 3: a portal wing wall's thickness (m). Its inner face stands at PortalHalf.</summary>
        public const float PortalWall = 1.2f;

        /// <summary>W-138 row 3: how far the cutting's flat floor reaches: just behind the wing walls, so the steep
        /// cut's triangles (a metre a cell near) rise inside the wall instead of poking out of its face.</summary>
        public static float PortalCutHalf(float radius) => PortalHalf(radius) + PortalWall + 0.1f;

        /// <summary>W-138 row 3: how much a street near a portal (Portal.w over 0) levels at xz, given what it would
        /// level with its own banks: the narrow cutting near the headwall, its banks again further out, nothing behind
        /// the headwall. half and blend are the street's own.</summary>
        static float PortalCut(in SettlementStreet s, float2 xz, float across, float half, float blend)
        {
            var into = math.dot(xz, s.Portal.xy) - s.Portal.z;
            var k = math.smoothstep(-(PortalWing + PortalTaper), -PortalWing, into);
            half = math.lerp(half, s.Portal.w, k);
            blend = math.lerp(blend, PortalCutBlend, k);
            return (1f - math.smoothstep(half, half + blend, across)) * (1f - math.smoothstep(0f, PortalBack, into));
        }

        /// <summary>W-138 row 3b: the ground at xz capped by the ledge behind a portal (PortalLedge), for the stretch
        /// that ends at the headwall; other stretches, and spots off the ledge, keep h.</summary>
        static float PortalLedgeCap(in SettlementStreet s, float2 xz, float h)
        {
            var dir = s.Portal.xy;
            float ia = math.dot(s.A.xz, dir) - s.Portal.z, ib = math.dot(s.B.xz, dir) - s.Portal.z;
            if (math.min(math.abs(ia), math.abs(ib)) > 1f)
                return h;
            var end = math.abs(ia) < math.abs(ib) ? s.A : s.B;
            var into = math.dot(xz, dir) - s.Portal.z;
            var side = math.abs(math.dot(xz - end.xz, new float2(-dir.y, dir.x)));
            var w = (1f - math.smoothstep(s.Portal.w, s.Portal.w + 1f, side))
                  * math.smoothstep(-1.5f, -0.5f, into) * (1f - math.smoothstep(PortalLedgeBack - 1f, PortalLedgeBack, into));
            return w > 0f ? math.lerp(h, math.min(h, end.y + PortalLedge(s.Portal.w)), w) : h;
        }

        /// <summary>W-138 row 3: how far past a portal cutting's edge plants keep clear (m), so no trunk stands in the
        /// cutting and no canopy hangs over its mouth.</summary>
        public const float PortalClear = 2.5f;

        /// <summary>W-138 row 3: whether a footprint of this radius at xz stands in a portal's cutting (or within
        /// PortalClear of it), from the ledge behind the headwall out to where the cutting has widened into the road's
        /// banks.</summary>
        public static bool InPortalCut(in SettlementStreet s, float2 xz, float radius)
        {
            if (s.Portal.w <= 0f)
                return false;
            var into = math.dot(xz, s.Portal.xy) - s.Portal.z;
            return into < PortalLedgeBack + radius && into > -(PortalWing + PortalTaper) - radius
                && Across(s, xz, out _) < s.Portal.w + PortalCutBlend + PortalClear + radius;
        }

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
                if (s.Road == TunnelRoad || math.any(xz < s.Min) || math.any(xz > s.Max))
                    continue;
                var across = Across(s, xz, out var t);
                var level = math.lerp(s.A.y, s.B.y, t);
                // W-111: a graded road stretch (Road 2: a cutting or embankment) banks out further the deeper it is.
                var blend = s.Road == 2 ? math.min(StreetBlend + math.abs(h - level) * GradeBank, GradeBlendMax) : StreetBlend;
                var w = s.Portal.w > 0f ? PortalCut(s, xz, across, s.Width * 0.5f, blend)
                                        : 1f - math.smoothstep(s.Width * 0.5f, s.Width * 0.5f + blend, across);
                if (w > 0f)
                    h = math.lerp(h, level, w);
                if (s.Portal.w > 0f)
                    h = PortalLedgeCap(s, xz, h);
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

        /// <summary>
        /// Paints the streets' surface (tunnel roads only, or every other street) over props, with a ragged edge. Returns
        /// how much xz is on a town's own street (for the squares' kerbs); covered: how much on any street painted.
        /// </summary>
        static float Paint(ref SettlementSetBlob set, float2 xz, float edgeNoise, float colorNoise, bool tunnels, ref SurfaceProps props, out float covered)
        {
            covered = 0f;
            var cell = Cell(ref set, xz);
            if (cell < 0)
                return 0f;
            var lane = 0f;   // how much xz is on a town's own street (not a road or square): no kerb across a lane's mouth
            var range = set.StreetCells[cell];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Streets[set.StreetItems[k]];
                if ((s.Road == TunnelRoad) != tunnels || math.any(xz < s.Min) || math.any(xz > s.Max))
                    continue;
                ref var layout = ref set.Layouts[set.Settlements[s.Settlement].Layout];
                if (layout.HasStreetSurface == 0)
                    continue;
                var half = s.Width * 0.5f;
                var w = 1f - math.smoothstep(half - 0.6f, half + 0.6f, Across(s, xz, out _) + edgeNoise * 0.8f);
                // W-138 row 3: no road surface past a portal's face (the end cap would paint the headwall's foot).
                if (s.Portal.w > 0f)
                    w *= 1f - math.smoothstep(-0.3f, 0.2f, math.dot(xz, s.Portal.xy) - s.Portal.z);
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
                if (s.Road == 0 && !s.A.xz.Equals(s.B.xz))
                    lane = math.max(lane, w);
                props = SurfaceProps.Lerp(props, street, w);
                covered = math.max(covered, w);
            }
            return lane;
        }

        /// <summary>Paints the streets' surface over the ground, with a ragged edge.</summary>
        public static void Ground(ref SettlementSetBlob set, float2 xz, float edgeNoise, float colorNoise, ref SurfaceProps props)
        {
            var lane = Paint(ref set, xz, edgeNoise, colorNoise, false, ref props, out _);
            for (int i = 0; i < set.Settlements.Length; i++)
                if (set.Settlements[i].Square > 0f)
                    Square(ref set, i, xz, edgeNoise, colorNoise, lane, ref props);
        }

        /// <summary>
        /// W-110: a tunnel road's surface at a spot (for the tunnel floor, which CaveSystem shades), and how much of the
        /// spot it covers (0 off every tunnel road). The ground's own paint (Ground) leaves tunnel roads out, as the hill
        /// stays over them.
        /// </summary>
        public static float TunnelGround(ref SettlementSetBlob set, float2 xz, float edgeNoise, float colorNoise, ref SurfaceProps props)
        {
            Paint(ref set, xz, edgeNoise, colorNoise, true, ref props, out var covered);
            return covered;
        }

        /// <summary>One of a town's square draws (0..1): five bits of its hash.</summary>
        static float Draw(uint seed, int k) => ((seed >> (k * 5)) & 31u) / 31f;

        /// <summary>
        /// W-108 (#39 part 2): a big town's paved square. Big flagstones in a rounded square (or a near circle), turned
        /// and shaped per town, ringed by a kerb of paler setts that stops where a lane comes in; paths worn smooth from
        /// each lane's mouth to the well or fountain and a ring worn round it, where folk stand to draw water, inside a
        /// ring of darker stones laid round it. Its shape, tint and lean come from the town's own hash, so no two match.
        /// </summary>
        static void Square(ref SettlementSetBlob set, int i, float2 xz, float edgeNoise, float colorNoise, float lane, ref SurfaceProps props)
        {
            ref var town = ref set.Settlements[i];
            var reach = town.Square * 0.5f + 1.5f;   // out over the open space's edge to the house fronts
            var d = xz - town.Centre;
            if (math.lengthsq(d) > (reach + 2f) * (reach + 2f))
                return;
            ref var layout = ref set.Layouts[town.Layout];
            var seed = math.hash(new uint2((uint)town.Landmark, 0x5c0a7eu));
            seed = (seed ^ (seed >> 15)) * 0x2c1b3c6du;
            seed ^= seed >> 12;
            // Shape: a superellipse turned to its own angle, from nearly round (p 2) to a soft square (p 5).
            var turn = Draw(seed, 0) * math.PI * 0.5f;
            var p = math.lerp(2.2f, 5f, Draw(seed, 1));
            var local = math.abs(new float2(d.x * math.cos(turn) + d.y * math.sin(turn), -d.x * math.sin(turn) + d.y * math.cos(turn)));
            var n = math.pow(math.pow(local.x, p) + math.pow(local.y, p), 1f / p) / math.lerp(1f, 0.92f, Draw(seed, 1));
            var inside = 1f - math.smoothstep(reach - 0.5f, reach + 0.5f, n + edgeNoise * 0.5f);
            if (inside <= 0f)
                return;
            var kerb = (1f - math.smoothstep(0.4f, 0.9f, math.abs(n - (reach - 0.7f)))) * (1f - lane);

            var stone = layout.Street.Props;
            stone.Paving = math.max(stone.Paving, 0.8f);   // an elf town's path is not cobbled, but its square is laid
            var tint = new float3(1f + (Draw(seed, 2) - 0.5f) * 0.16f, 1f, 1f - (Draw(seed, 2) - 0.5f) * 0.16f) * math.lerp(0.95f, 1.12f, Draw(seed, 3));
            var flags = stone;
            flags.Albedo *= tint * (1f + colorNoise * layout.Street.ColorVariation);
            flags.Slabs = 1f;
            flags.Pebbles *= 0.4f;
            // Worn paths: from each lane's mouth to the water, and round the water where folk stand.
            var water = town.Water.xz;
            var worn = 0f;
            for (int m = 0; m < 4; m++)
            {
                var a = town.Mouths[m];
                if (a > 9f)
                    continue;
                var mouth = town.Centre + new float2(math.cos(a), math.sin(a)) * reach;
                var ab = water - mouth;
                var t = math.saturate(math.dot(xz - mouth, ab) / math.max(math.lengthsq(ab), 1e-4f));
                var off = math.distance(xz, mouth + ab * t) + edgeNoise * 0.6f;
                worn = math.max(worn, 1f - math.smoothstep(0.6f, 1.8f, off));
            }
            var r = math.distance(xz, water);
            var ringAt = math.lerp(2.2f, 3f, Draw(seed, 4));
            worn = math.max(worn, 1f - math.smoothstep(0.5f, 1.2f, math.abs(r - ringAt)));
            flags.Paving = stone.Paving + worn * 0.85f;
            // A ring of darker stones laid round the water, just inside the worn ring.
            flags.Albedo *= 1f - 0.22f * (1f - math.smoothstep(0.25f, 0.6f, math.abs(r - (ringAt - 1.1f))));
            // The kerb: a band of paler, smaller setts at the square's edge.
            var setts = stone;
            setts.Albedo = math.lerp(stone.Albedo, new float3(0.6f, 0.58f, 0.54f), 0.55f) * (1f + colorNoise * 0.06f);
            setts.Paving = 1f;
            flags = SurfaceProps.Lerp(flags, setts, kerb);
            props = SurfaceProps.Lerp(props, flags, inside);
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
                // W-138 row 3: no trees in a tunnel portal's cutting or in front of its mouth.
                if (InPortalCut(s, xz, 0f))
                    keep = 0f;
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
