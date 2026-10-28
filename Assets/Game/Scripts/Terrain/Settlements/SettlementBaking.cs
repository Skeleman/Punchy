using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Lays out every settlement landmark (one whose type has a SettlementLayout) from its seed: streets in its
    /// culture's pattern, the leader's Seat on the best side of the square, a water feature spot mid-square, plots
    /// along the streets facing them (skipped where the ground is steep, wet, holed by a cave or taken by a placed
    /// prop), then roles: shrines, the layout's other buildings, green plots and houses. Integer-seeded, so every
    /// machine lays the same towns. Runs after landmarks (it samples their shapes), before caves.
    /// </summary>
    public static class SettlementBaking
    {
        const float GridCell = 32f;
        /// <summary>Slots after each layout's own buildings in the flat list: water feature, signpost, bridge, palisade.</summary>
        public const int ExtraSlots = 4;
        /// <summary>Streets are cut into stretches no longer than this, so their levelling follows the ground.</summary>
        const float StretchLength = 10f;

        /// <summary>The distinct layouts among the landmark types, in first-use order (the order Layouts are baked in).</summary>
        public static List<SettlementLayout> Layouts(IReadOnlyList<LandmarkType> types)
        {
            var list = new List<SettlementLayout>();
            foreach (var t in types)
                if (t != null && t.Settlement != null && !list.Contains(t.Settlement))
                    list.Add(t.Settlement);
            return list;
        }

        /// <summary>
        /// Every layout's buildings, flattened in Layouts order: a plot's Building indexes this list (the flora system
        /// maps it to its species).
        /// </summary>
        public static List<SettlementBuilding> Buildings(IReadOnlyList<LandmarkType> types)
        {
            var list = new List<SettlementBuilding>();
            foreach (var layout in Layouts(types))
            {
                list.AddRange(layout.Buildings);
                // Then the water feature, signpost, bridge and palisade, one slot each per layout (empty if it has none).
                list.Add(new SettlementBuilding { Role = PlotRole.Well, Species = layout.WaterFeature, Count = 1 });
                list.Add(new SettlementBuilding { Role = PlotRole.Signpost, Species = layout.Signpost, Count = 1 });
                list.Add(new SettlementBuilding { Role = PlotRole.Bridge, Species = layout.Bridge, Count = 1 });
                list.Add(new SettlementBuilding { Role = PlotRole.Palisade, Species = layout.PalisadeWall, Count = 1 });
            }
            return list;
        }

        public static BlobAssetReference<SettlementSetBlob> Build(in TerrainSource source, IReadOnlyList<LandmarkType> types, Allocator allocator)
        {
            var layouts = Layouts(types);
            if (layouts.Count == 0 || !source.Landmarks.IsCreated)
                return default;
            ref var landmarks = ref source.Landmarks.Value;
            var settlements = new List<SettlementData>();
            var streets = new List<SettlementStreet>();
            var plots = new List<SettlementPlot>();
            var firstBuilding = new List<int>();
            var count = 0;
            foreach (var layout in layouts)
            {
                firstBuilding.Add(count);
                count += layout.Buildings.Count + ExtraSlots;
            }

            for (int i = 0; i < landmarks.Instances.Length; i++)
            {
                var inst = landmarks.Instances[i];
                var type = inst.Type < types.Count ? types[inst.Type] : null;
                if (type == null || type.Settlement == null)
                    continue;
                var index = layouts.IndexOf(type.Settlement);
                new Planner(source, type, type.Settlement, inst, i, settlements.Count, firstBuilding[index], streets, plots)
                    .Lay(index, settlements);
            }
            RoadBaking.Build(source, layouts, firstBuilding, settlements, streets, plots);
            return BuildBlob(layouts, firstBuilding, settlements, streets, plots, allocator);
        }

        /// <summary>One settlement being laid out.</summary>
        class Planner
        {
            readonly TerrainSource source;
            readonly LandmarkType type;
            readonly SettlementLayout layout;
            readonly LandmarkInstance inst;
            readonly int landmark, settlement, firstBuilding;
            readonly List<SettlementStreet> streets;
            readonly List<SettlementPlot> plots;
            readonly List<float3> reserved;   // placed props: xz and clearance
            /// <summary>Each street's line as laid (stopping short at water), for the plots along it.</summary>
            readonly List<(List<float2> line, float width)> lines = new List<(List<float2>, float)>();
            readonly int firstStreet, firstPlot;
            readonly float radius, scale;
            Random rng;

            public Planner(in TerrainSource source, LandmarkType type, SettlementLayout layout, in LandmarkInstance inst, int landmark,
                int settlement, int firstBuilding, List<SettlementStreet> streets, List<SettlementPlot> plots)
            {
                this.source = source;
                this.type = type;
                this.layout = layout;
                this.inst = inst;
                this.landmark = landmark;
                this.settlement = settlement;
                this.firstBuilding = firstBuilding;
                this.streets = streets;
                this.plots = plots;
                firstStreet = streets.Count;
                firstPlot = plots.Count;
                radius = type.Radius * inst.Scale;
                scale = layout.FolkScale;
                rng = new Random(math.hash(new int2(landmark, 0x5E77)) | 1u);
                // Placed props (an inn, a smithy, pens) keep their spots, so their resident workers keep theirs.
                reserved = new List<float3>();
                foreach (var p in type.Props)
                {
                    if (p.Species == null)
                        continue;
                    if (p.Centrepiece)
                        reserved.Add(new float3(inst.Position, p.Clearance * 0.6f));
                    for (int k = 0; k < p.Count; k++)
                        reserved.Add(new float3(LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(p.Ring.x, p.Angle + k * 360f / p.Count), radius),
                            p.Clearance * 0.6f));
                }
            }

            float2 Dir(float angle) => inst.Facing * math.cos(angle) + new float2(-inst.Facing.y, inst.Facing.x) * math.sin(angle);

            public void Lay(int layoutIndex, List<SettlementData> settlements)
            {
                var centre = inst.Position;
                var road = rng.NextFloat(math.PI * 2f);
                var square = layout.SquareSize * scale;
                var street = layout.StreetWidth * scale;
                var lane = layout.LaneWidth * scale;
                var depth = layout.PlotSize.y * scale;
                var setback = layout.Setback * scale;
                var water = centre;
                var palisade = 0f;

                // The open space in the middle, then the Seat facing it from the side away from the road.
                Open(centre, square);
                switch (layout.Pattern)
                {
                    case SettlementPattern.Square:
                        // Lanes out of the square, the first the main street (where the road comes in).
                        for (int k = 0; k < layout.Lanes; k++)
                        {
                            var a = road + k * math.PI * 2f / layout.Lanes + (k == 0 ? 0f : rng.NextFloat(-0.25f, 0.25f));
                            Street(centre + Dir(a) * square * 0.5f, centre + Dir(a + rng.NextFloat(-0.15f, 0.15f)) * radius * 0.95f, k == 0 ? street : lane);
                        }
                        Seat(centre, Away(road, layout.Lanes), square * 0.5f + setback);
                        Around(centre, square * 0.5f + setback);
                        break;
                    case SettlementPattern.Grid:
                    {
                        // A main street through the middle along the road, parallel streets either side and cross
                        // streets, a block (two plot depths) apart.
                        var along = Dir(road);
                        var across = new float2(-along.y, along.x);
                        var block = 2f * (depth + setback) + lane;
                        var reach = radius * 0.92f;
                        for (int k = -3; k <= 3; k++)
                        {
                            var offset = k * block;
                            if (math.abs(offset) >= reach * 0.85f)
                                continue;
                            var half = math.sqrt(reach * reach - offset * offset);
                            Street(centre + across * offset - along * half, centre + across * offset + along * half, k == 0 ? street : lane);
                            Street(centre + along * offset - across * half, centre + along * offset + across * half, lane);
                        }
                        // The Seat: square-on in a block beside the square, facing the main street (either side).
                        var seatHalf = new float2(layout.PlotSize.x, layout.PlotSize.y) * 0.75f * scale;
                        for (int side = 1; side >= -1; side -= 2)
                            if (TryPlot(centre + along * (square * 0.5f + seatHalf.x) + across * side * (street * 0.5f + setback + seatHalf.y),
                                    -across * side, seatHalf, PlotRole.Seat))
                                break;
                        break;
                    }
                    case SettlementPattern.Ring:
                    {
                        // A ring street round the green, paths in from it, and the main path out along the road.
                        var ring = math.max(square * 0.5f + 2f * (depth + setback) + lane, radius * 0.55f);
                        var sides = math.max(8, (int)math.ceil(math.PI * 2f * ring / StretchLength));
                        var points = new List<float2>();
                        for (int k = 0; k <= sides; k++)
                        {
                            var wobble = 1f + 0.06f * noise.snoise(new float2(k % sides, landmark) * 0.7f);
                            points.Add(centre + Dir(road + k * math.PI * 2f / sides) * ring * wobble);
                        }
                        Street(points, lane);
                        for (int k = 0; k < layout.Lanes; k++)
                        {
                            var a = road + (k + 0.5f) * math.PI * 2f / layout.Lanes;
                            Street(centre + Dir(a) * square * 0.5f, centre + Dir(a) * ring, lane * 0.75f);
                        }
                        Street(centre + Dir(road) * ring, centre + Dir(road) * radius * 0.95f, street);
                        Seat(centre, road + math.PI, square * 0.5f + setback);
                        break;
                    }
                    case SettlementPattern.Camp:
                        // No streets: huts round the yard and scattered out to the palisade, all facing the yard.
                        Seat(centre, road + math.PI, square * 0.5f + setback);
                        Around(centre, square * 0.5f + setback);
                        Scatter(centre, square * 0.5f + depth + setback * 3f, radius * 0.8f);
                        if (layout.Palisade)
                            palisade = radius * 0.9f;
                        // The gate path in from the road.
                        Street(centre + Dir(road) * square * 0.5f, centre + Dir(road) * radius, lane);
                        break;
                }
                if (layout.Pattern != SettlementPattern.Camp)
                    foreach (var (line, width) in lines)
                        Along(line, width);

                // A well or fountain on the square, unless the landmark already places one: the first clear spot round
                // the middle, on its own small plot (levelled, kept clear, with a Well anchor).
                if (layout.WaterFeature != null && !PlacesWater())
                {
                    var size = new float2(1.6f, 1.6f) * scale;
                    // The middle, then out along the diagonals between the main streets (a grid's cross through the
                    // middle), then round about, to the square's edge.
                    for (int k = 0; k < 25; k++)
                    {
                        var angle = road + math.PI * 0.25f + (k - 1) * math.PI * 0.5f + (k > 8 ? 0.4f * (k % 3) : 0f);
                        var spot = centre + (k == 0 ? float2.zero : Dir(angle) * square * (0.25f + 0.05f * ((k - 1) / 4)));
                        if (TryPlot(spot, Dir(road), size, PlotRole.Well, true))
                        {
                            water = spot;
                            break;
                        }
                    }
                }

                AssignRoles();
                settlements.Add(new SettlementData
                {
                    Centre = centre,
                    Radius = radius,
                    Scale = scale,
                    Landmark = landmark,
                    Type = inst.Type,
                    Layout = layoutIndex,
                    FirstStreet = firstStreet,
                    StreetCount = streets.Count - firstStreet,
                    FirstPlot = firstPlot,
                    PlotCount = plots.Count - firstPlot,
                    Water = new float3(water.x, source.SampleHeight(water), water.y),
                    Road = Dir(road),
                    Palisade = palisade
                });
            }

            /// <summary>The angle between the two lanes nearest the side opposite the road (the square's best side).</summary>
            static float Away(float road, int lanes) => lanes % 2 == 0 ? road + math.PI + math.PI / lanes : road + math.PI;

            void Open(float2 at, float diameter)
            {
                if (diameter > 0f)
                    AddStreet(new float3(at.x, source.SampleHeight(at), at.y), new float3(at.x, 0f, at.y), diameter, true);
            }

            void Street(float2 a, float2 b, float width) => Street(new List<float2> { a, b }, width);

            /// <summary>
            /// A street through the given points, cut into stretches; it stops where it would run into water or over a
            /// cave hole. The line it actually takes is kept, so plots can follow its whole length.
            /// </summary>
            void Street(List<float2> points, float width)
            {
                var line = new List<float2> { points[0] };
                var from = new float3(points[0].x, source.SampleHeight(points[0]), points[0].y);
                for (int i = 1; i < points.Count; i++)
                {
                    var a = points[i - 1];
                    var b = points[i];
                    var n = math.max(1, (int)math.ceil(math.distance(a, b) / StretchLength));
                    for (int k = 1; k <= n; k++)
                    {
                        var p = math.lerp(a, b, k / (float)n);
                        var mid = math.lerp(from.xz, p, 0.5f);
                        if (Wet(mid) || source.IsCaveHole(mid))
                        {
                            lines.Add((line, width));
                            return;
                        }
                        var to = new float3(p.x, source.SampleHeight(p), p.y);
                        AddStreet(from, to, width, false);
                        line.Add(p);
                        from = to;
                    }
                }
                lines.Add((line, width));
            }

            void AddStreet(float3 a, float3 b, float width, bool open)
            {
                if (open)
                    b = a;
                var reach = width * 0.5f + SettlementSampling.StreetBlend;
                streets.Add(new SettlementStreet
                {
                    A = a,
                    B = b,
                    Width = width,
                    Settlement = settlement,
                    Min = math.min(a.xz, b.xz) - reach,
                    Max = math.max(a.xz, b.xz) + reach
                });
            }

            bool Wet(float2 xz)
            {
                var g = source.SampleGround(xz);
                return g.HasWater && g.WaterSurface > g.Height - 0.3f;
            }

            /// <summary>The leader's hall: a plot half as big again, facing the middle from the given side.</summary>
            void Seat(float2 centre, float angle, float from)
            {
                var facing = -Dir(angle);
                var half = new float2(layout.PlotSize.x * 0.75f, layout.PlotSize.y * 0.75f) * scale;
                // Try the best side first, then a little round either way.
                for (int k = 0; k < 5; k++)
                {
                    var a = angle + (k + 1) / 2 * 0.5f * (k % 2 == 0 ? 1f : -1f);
                    facing = -Dir(a);
                    if (TryPlot(centre - facing * (from + half.y), facing, half, PlotRole.Seat))
                        return;
                }
            }

            /// <summary>Plots round an open space, facing it.</summary>
            void Around(float2 centre, float from)
            {
                var half = new float2(layout.PlotSize.x, layout.PlotSize.y) * 0.5f * scale;
                var gap = layout.PlotGap * scale;
                var ring = from + half.y;
                var n = (int)math.floor(math.PI * 2f * (from + 0.5f) / (half.x * 2f + gap));
                var start = rng.NextFloat(math.PI * 2f);
                for (int k = 0; k < n; k++)
                {
                    var facing = -Dir(start + k * math.PI * 2f / n);
                    TryPlot(centre - facing * ring, facing, half, PlotRole.House);
                }
            }

            /// <summary>Plots along both sides of a street's whole line, facing it.</summary>
            void Along(List<float2> line, float width)
            {
                var half = new float2(layout.PlotSize.x, layout.PlotSize.y) * 0.5f * scale;
                var step = half.x * 2f + layout.PlotGap * scale;
                var off = width * 0.5f + layout.Setback * scale + half.y;
                var t = half.x;
                var walked = 0f;
                for (int i = 1; i < line.Count; i++)
                {
                    var ab = line[i] - line[i - 1];
                    var length = math.length(ab);
                    if (length < 1e-3f)
                        continue;
                    var along = ab / length;
                    for (; t <= walked + length; t += step)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var normal = new float2(-along.y, along.x) * side;
                            TryPlot(line[i - 1] + along * (t - walked) + normal * off, -normal, half, PlotRole.House);
                        }
                    walked += length;
                }
            }

            /// <summary>Huts scattered between two rings, facing the middle.</summary>
            void Scatter(float2 centre, float inner, float outer)
            {
                var half = new float2(layout.PlotSize.x, layout.PlotSize.y) * 0.5f * scale;
                for (int k = 0; k < 120; k++)
                {
                    var a = rng.NextFloat(math.PI * 2f);
                    var r = math.sqrt(rng.NextFloat(inner * inner, outer * outer));
                    var facing = -Dir(a + rng.NextFloat(-0.3f, 0.3f));
                    TryPlot(centre + Dir(a) * r, facing, half, PlotRole.House);
                }
            }

            /// <summary>Adds a plot if it's inside the settlement, clear of streets, plots and placed props, and buildable.</summary>
            bool TryPlot(float2 centre, float2 facing, float2 half, PlotRole role, bool onSquare = false)
            {
                var side = new float2(facing.y, -facing.x);
                var gap = layout.PlotGap * scale * 0.5f;
                var corners = new FixedList128Bytes<float2>();
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    corners.Add(centre + side * half.x * x + facing * half.y * y);

                foreach (var c in corners)
                    if (math.distance(c, inst.Position) > radius * 0.95f)
                        return false;
                for (int s = firstStreet; s < streets.Count; s++)
                {
                    var street = streets[s];
                    // A well stands on the open square itself (a street with A == B), not on a street proper.
                    if (onSquare && street.A.Equals(street.B))
                        continue;
                    var clear = street.Width * 0.5f + 0.5f;
                    foreach (var c in corners)
                        if (SettlementSampling.Across(street, c, out _) < clear)
                            return false;
                }
                foreach (var r in reserved)
                    if (math.length(math.max(math.abs(Local(centre, facing, r.xy)) - half, 0f)) < r.z)
                        return false;
                for (int p = firstPlot; p < plots.Count; p++)
                    if (Overlap(plots[p], centre, facing, half + gap))
                        return false;

                float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
                foreach (var c in corners)
                {
                    var g = source.SampleGround(c);
                    if (g.HasWater && g.WaterSurface > g.Height - 0.3f || source.IsCaveHole(c))
                        return false;
                    lo = math.min(lo, g.Height);
                    hi = math.max(hi, g.Height);
                    sum += g.Height;
                }
                if (hi - lo > layout.MaxRelief * scale)
                    return false;

                var reach = math.length(half) + SettlementSampling.PadBlend;
                plots.Add(new SettlementPlot
                {
                    Centre = centre,
                    Facing = facing,
                    Half = half,
                    Pad = sum / corners.Length,
                    Role = role,
                    Building = -1,
                    Settlement = settlement,
                    Min = centre - reach,
                    Max = centre + reach
                });
                return true;
            }

            static float2 Local(float2 centre, float2 facing, float2 p)
            {
                var d = p - centre;
                return new float2(d.x * facing.y - d.y * facing.x, math.dot(d, facing));
            }

            /// <summary>Separating-axis test between a placed plot and a candidate (grown by the gap).</summary>
            static bool Overlap(in SettlementPlot a, float2 centre, float2 facing, float2 half)
            {
                var axes = new FixedList64Bytes<float2>();
                axes.Add(a.Facing);
                axes.Add(new float2(a.Facing.y, -a.Facing.x));
                axes.Add(facing);
                axes.Add(new float2(facing.y, -facing.x));
                foreach (var axis in axes)
                {
                    var ra = Extent(a.Facing, a.Half, axis);
                    var rb = Extent(facing, half, axis);
                    if (math.abs(math.dot(centre - a.Centre, axis)) > ra + rb)
                        return false;
                }
                return true;
            }

            static float Extent(float2 facing, float2 half, float2 axis) =>
                math.abs(math.dot(new float2(facing.y, -facing.x), axis)) * half.x + math.abs(math.dot(facing, axis)) * half.y;

            /// <summary>
            /// Shrines (on the square or at the edge), then the layout's other buildings (inns and markets near the
            /// middle, workshops further out), then green plots; the rest are houses. Each plot then picks its building.
            /// </summary>
            void AssignRoles()
            {
                var order = new List<int>();
                for (int p = firstPlot; p < plots.Count; p++)
                    if (plots[p].Role == PlotRole.House)
                        order.Add(p);
                var centre = inst.Position;
                order.Sort((a, b) => math.distance(plots[a].Centre, centre).CompareTo(math.distance(plots[b].Centre, centre)));

                for (int k = 0; k < layout.Shrines && order.Count > 0; k++)
                    Take(order, rng.NextBool() ? 0 : order.Count - 1, PlotRole.Shrine);
                foreach (var b in layout.Buildings)
                {
                    if (b.Role == PlotRole.House || b.Role == PlotRole.Landscaping || b.Role == PlotRole.Seat || b.Role == PlotRole.Shrine)
                        continue;
                    var central = b.Role == PlotRole.Inn || b.Role == PlotRole.Market;
                    for (int k = 0; k < b.Count && order.Count > 0; k++)
                        Take(order, central ? 0 : order.Count / 2, b.Role);
                }
                foreach (var p in order)
                {
                    var plot = plots[p];
                    if (rng.NextFloat() < layout.Landscaping)
                        plot.Role = PlotRole.Landscaping;
                    plots[p] = plot;
                }
                for (int p = firstPlot; p < plots.Count; p++)
                {
                    var plot = plots[p];
                    plot.Building = PickBuilding(plot.Role);
                    plots[p] = plot;
                }
            }

            void Take(List<int> order, int at, PlotRole role)
            {
                var plot = plots[order[at]];
                plot.Role = role;
                plots[order[at]] = plot;
                order.RemoveAt(at);
            }

            /// <summary>Whether the landmark type already places a well or fountain among its props.</summary>
            bool PlacesWater()
            {
                foreach (var p in type.Props)
                    if (p.Species != null && (p.Species.Form == Punchy.Flora.FloraForm.Well || p.Species.Form == Punchy.Flora.FloraForm.Fountain))
                        return true;
                return false;
            }

            int PickBuilding(PlotRole role)
            {
                if (role == PlotRole.Well)
                    return layout.WaterFeature != null ? firstBuilding + layout.Buildings.Count : -1;
                var total = 0f;
                foreach (var b in layout.Buildings)
                    if (b.Role == role && b.Species != null)
                        total += b.Weight;
                if (total <= 0f)
                    return -1;
                var roll = rng.NextFloat(total);
                for (int i = 0; i < layout.Buildings.Count; i++)
                {
                    var b = layout.Buildings[i];
                    if (b.Role != role || b.Species == null)
                        continue;
                    roll -= b.Weight;
                    if (roll <= 0f)
                        return firstBuilding + i;
                }
                return -1;
            }
        }

        static BlobAssetReference<SettlementSetBlob> BuildBlob(List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
            List<SettlementStreet> streets, List<SettlementPlot> plots, Allocator allocator)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<SettlementSetBlob>();
            var l = builder.Allocate(ref root.Layouts, layouts.Count);
            for (int i = 0; i < layouts.Count; i++)
                l[i] = new SettlementLayoutData
                {
                    HasStreetSurface = layouts[i].StreetSurface != null ? 1 : 0,
                    Street = layouts[i].StreetSurface != null ? BiomeBaking.SurfaceData(layouts[i].StreetSurface) : default,
                    FirstBuilding = firstBuilding[i],
                    BuildingCount = layouts[i].Buildings.Count + ExtraSlots,
                    BuildingScale = layouts[i].BuildingScale
                };
            var s = builder.Allocate(ref root.Settlements, settlements.Count);
            for (int i = 0; i < settlements.Count; i++)
                s[i] = settlements[i];
            var st = builder.Allocate(ref root.Streets, streets.Count);
            for (int i = 0; i < streets.Count; i++)
                st[i] = streets[i];
            var p = builder.Allocate(ref root.Plots, plots.Count);
            for (int i = 0; i < plots.Count; i++)
                p[i] = plots[i];

            // The grid covers every street's and plot's reach.
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var x in streets) { min = math.min(min, x.Min); max = math.max(max, x.Max); }
            foreach (var x in plots) { min = math.min(min, x.Min); max = math.max(max, x.Max); }
            if (streets.Count + plots.Count == 0)
                min = max = float2.zero;
            root.CellSize = GridCell;
            root.GridOrigin = math.floor(min / GridCell) * GridCell;
            root.GridSize = (int2)math.ceil((max - root.GridOrigin) / GridCell) + 1;
            Fill(ref builder, ref root.StreetCells, ref root.StreetItems, root.GridOrigin, root.GridSize, streets.Count, i => (streets[i].Min, streets[i].Max));
            Fill(ref builder, ref root.PlotCells, ref root.PlotItems, root.GridOrigin, root.GridSize, plots.Count, i => (plots[i].Min, plots[i].Max));

            var blob = builder.CreateBlobAssetReference<SettlementSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }

        static void Fill(ref BlobBuilder builder, ref BlobArray<int2> cells, ref BlobArray<int> items, float2 origin, int2 size, int count,
            System.Func<int, (float2 min, float2 max)> bounds)
        {
            var lists = new List<int>[size.x * size.y];
            for (int i = 0; i < count; i++)
            {
                var (lo, hi) = bounds(i);
                var a = math.clamp((int2)math.floor((lo - origin) / GridCell), 0, size - 1);
                var b = math.clamp((int2)math.floor((hi - origin) / GridCell), 0, size - 1);
                for (int y = a.y; y <= b.y; y++)
                for (int x = a.x; x <= b.x; x++)
                    (lists[y * size.x + x] ??= new List<int>()).Add(i);
            }
            var c = builder.Allocate(ref cells, lists.Length);
            var flat = new List<int>();
            for (int i = 0; i < lists.Length; i++)
            {
                c[i] = new int2(flat.Count, lists[i]?.Count ?? 0);
                if (lists[i] != null)
                    flat.AddRange(lists[i]);
            }
            var it = builder.Allocate(ref items, flat.Count);
            for (int i = 0; i < flat.Count; i++)
                it[i] = flat[i];
        }
    }
}
