using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Roads between settlements, baked as more streets in the settlement blob (so they're levelled, paved and kept
    /// clear of plants as streets are): every settlement joined to the others along a minimum spanning tree, and a spur
    /// to each cave mouth near a settlement. Each road is routed over a coarse cost grid (gentle slopes preferred, steep
    /// ground and cave entrances avoided, shallow water crossed as a ford left unpaved, deeper rivers bridged at a cost),
    /// smoothed, and cut into short stretches. A signpost stands where each road leaves a settlement, and camps with a
    /// palisade get one round them once the roads' gaps through it are known. Integer-seeded and deterministic.
    /// </summary>
    public static class RoadBaking
    {
        const float Cell = 20f;
        /// <summary>How broad the patches roads wander round are (m); see Route.</summary>
        const float WanderScale = 260f;
        const float Margin = 300f;
        const float MaxSlope = 0.45f;
        /// <summary>
        /// The steepest a laid road may be, rise over run between ground samples a metre apart (W-100, #39). The grid's
        /// 20 m cells can't see a plateau's edge between two of their centres, so a found route is checked at this
        /// grain: re-routed round the steep stretches up to RerouteTries times, and any stretch still too steep isn't
        /// laid (the road ends at the top and picks up again at the bottom instead of running off the cliff).
        /// </summary>
        const float MaxGrade = 0.45f;
        const int RerouteTries = 3;
        /// <summary>Ground steeper than this anywhere inside a grid cell (a mesa's cliff, a gorge wall) is a cliff the road
        /// goes round: the cell's own centre samples can't see a sheer drop between them.</summary>
        const float CliffSlope = 1f;
        const float FordDepth = 0.7f;
        const float CaveReach = 700f;
        const float StretchLength = 10f;
        /// <summary>Deepest water a road will bridge (deeper is a lake or the sea: the road goes round).</summary>
        const float BridgeDepth = 6f;
        const float LongestBridge = 120f;
        /// <summary>Spacing of bridge spans (each 6.6 m long, so they overlap a little, and more than a tree-layer cell's
        /// diagonal apart so each gets its own cell) and of palisade lengths (3.1 m).</summary>
        const float SpanSpacing = 6.4f;
        const float PalisadeSpacing = 3f;

        /// <summary>A settlement's road end: where its main street leaves it, the way out, and its index.</summary>
        struct End
        {
            public float2 At, Out;
            public int Settlement;
            public float Radius;
        }

        /// <summary>
        /// Adds the roads to <paramref name="streets"/>, and their signposts and bridges and the camps' palisades to
        /// <paramref name="plots"/> (both appended after every settlement's own).
        /// </summary>
        public static void Build(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
                                 List<SettlementStreet> streets, List<SettlementPlot> plots, List<RoadsideBaking.RoadPath> roads = null)
        {
            if (settlements.Count == 0)
                return;
            Roads(source, layouts, firstBuilding, settlements, streets, plots, roads);
            Palisades(source, layouts, firstBuilding, settlements, streets, plots);
        }

        static void Roads(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
                          List<SettlementStreet> streets, List<SettlementPlot> plots, List<RoadsideBaking.RoadPath> roads)
        {
            Stats.Reset();
            var ends = new List<End>();
            foreach (var s in settlements)
                ends.Add(new End { At = s.Centre + s.Road * s.Radius * 0.95f, Out = s.Road, Settlement = ends.Count, Radius = s.Radius });

            // Which pairs to join: a minimum spanning tree over the settlements (Prim, by straight distance), then a spur
            // from the nearest settlement to each cave mouth within reach.
            var pairs = new List<(float2 from, float2 to, int settlement, bool toTown, int other)>();
            var inTree = new bool[ends.Count];
            inTree[0] = true;
            for (int added = 1; added < ends.Count; added++)
            {
                var best = (a: -1, b: -1, d: float.MaxValue);
                for (int a = 0; a < ends.Count; a++)
                {
                    if (!inTree[a])
                        continue;
                    for (int b = 0; b < ends.Count; b++)
                    {
                        if (inTree[b])
                            continue;
                        var d = math.distance(ends[a].At, ends[b].At);
                        if (d < best.d)
                            best = (a, b, d);
                    }
                }
                inTree[best.b] = true;
                pairs.Add((ends[best.a].At, ends[best.b].At, best.a, true, best.b));
            }
            if (source.Caves.IsCreated)
            {
                ref var caves = ref source.Caves.Value;
                for (int c = 0; c < caves.Caves.Length; c++)
                {
                    var mouth = caves.Caves[c].Mouth.xz;
                    var nearest = -1;
                    var nearestDistance = CaveReach;
                    for (int e = 0; e < ends.Count; e++)
                    {
                        var d = math.distance(ends[e].At, mouth);
                        if (d < nearestDistance)
                        {
                            nearest = e;
                            nearestDistance = d;
                        }
                    }
                    if (nearest >= 0)
                        pairs.Add((ends[nearest].At, mouth + math.normalizesafe(ends[nearest].At - mouth) * 12f, nearest, false, -1));
                }
            }
            if (pairs.Count == 0)
                return;

            // The cost grid over everything the roads might cross: ground height, water depth, cave entrance.
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var p in pairs)
            {
                min = math.min(min, math.min(p.from, p.to));
                max = math.max(max, math.max(p.from, p.to));
            }
            min -= Margin;
            max += Margin;
            var size = (int2)math.ceil((max - min) / Cell) + 1;
            var grid = new NativeArray<float4>(size.x * size.y, Allocator.TempJob);
            new GroundJob { Source = source, Origin = min, Size = size, Out = grid }.Schedule(grid.Length, 64).Complete();
            // Towns are closed to through roads: cells well inside any settlement are forbidden, so a road reaches a town
            // at its own road end (outside 0.85 of the radius) instead of cutting across its houses (G-18).
            foreach (var s in settlements)
            {
                var c0 = math.max((int2)math.floor((s.Centre - s.Radius - min) / Cell), 0);
                var c1 = math.min((int2)math.ceil((s.Centre + s.Radius - min) / Cell), size - 1);
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                    if (math.distance(min + new float2(x, y) * Cell, s.Centre) < s.Radius * 0.85f)
                    {
                        var cell = grid[y * size.x + x];
                        cell.z = 1f;
                        grid[y * size.x + x] = cell;
                    }
            }

            // W-16: every route reads only the finished grid, so they're found in parallel (a managed copy of the grid,
            // so worker threads never touch the NativeArray), then laid in pair order as before: the same streets and plots.
            var cells = grid.ToArray();
            grid.Dispose();
            var paths = new List<float2>[pairs.Count];
            // The wander factor of every cell, worked out once for all routes (each route used to call the noise for
            // every neighbour it tried, the same cells over and over).
            var seed = source.World.Seed;
            var offset = new float2(seed % 1000u, (seed / 1000u) % 1000u) * 0.37f;
            var wander = new float[cells.Length];
            var origin = min;
            System.Threading.Tasks.Parallel.For(0, size.y, y =>
            {
                for (int x = 0; x < size.x; x++)
                    wander[y * size.x + x] = Wander(origin + new float2(x, y) * Cell, offset);
            });
            var src = source;
            System.Threading.Tasks.Parallel.For(0, pairs.Count, i =>
            {
                var found = Route(cells, wander, size, min, pairs[i].from, pairs[i].to);
                var path = found == null ? null : Smooth(Smooth(Relax(found, 3)));
                // W-100 (#39): close the cells round any stretch too steep to lay and route again on this pair's own copy
                // of the grid. Roads that pass first time are unchanged.
                float4[] closed = null;
                var best = path;
                var fewest = int.MaxValue;
                for (int attempt = 0; attempt <= RerouteTries && path != null; attempt++)
                {
                    var steep = Steep(src, path, settlements);
                    if (steep.Count < fewest)
                    {
                        best = path;
                        fewest = steep.Count;
                    }
                    if (steep.Count == 0 || attempt == RerouteTries)
                        break;
                    closed ??= (float4[])cells.Clone();
                    var keepA = math.clamp((int2)math.round((pairs[i].from - min) / Cell), 0, size - 1);
                    var keepB = math.clamp((int2)math.round((pairs[i].to - min) / Cell), 0, size - 1);
                    foreach (var at in steep)
                    {
                        var c = (int2)math.round((at - min) / Cell);
                        for (int y = c.y - 1; y <= c.y + 1; y++)
                        for (int x = c.x - 1; x <= c.x + 1; x++)
                        {
                            var n = new int2(x, y);
                            if (math.any(n < 0) || math.any(n >= size) || math.all(n == keepA) || math.all(n == keepB))
                                continue;
                            closed[y * size.x + x].z = 1f;
                        }
                    }
                    var again = Route(closed, wander, size, min, pairs[i].from, pairs[i].to);
                    if (again == null)
                        break;   // no way round: keep the best route so far, and Emit leaves out its steep stretches
                    path = Smooth(Smooth(Relax(again, 3)));
                }
                paths[i] = best;
            });
            for (int i = 0; i < pairs.Count; i++)
            {
                var (_, _, settlement, toTown, other) = pairs[i];
                var path = paths[i];
                if (path == null)
                    continue;
                var layout = layouts[settlements[settlement].Layout];
                Emit(source, path, layout.RoadWidth * settlements[settlement].Scale, settlement, settlements, streets);
                ClearPlots(path, layout.RoadWidth * settlements[settlement].Scale, plots);
                // Roads between settlements (not the spurs to caves) carry roadside landmarks.
                if (toTown)
                    roads?.Add(new RoadsideBaking.RoadPath { Points = path, Width = layout.RoadWidth * settlements[settlement].Scale, Settlement = settlement, Other = other });
                AddBridges(source, path, settlement, settlements, layouts, firstBuilding, plots);
                // Gates where the road leaves each settlement it joins.
                AddGate(source, path, settlement, settlements, layouts, plots, false);
                if (toTown)
                    AddGate(source, path, other, settlements, layouts, plots, true);
            }
        }

        [BurstCompile]
        struct GroundJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public int2 Size;
            [WriteOnly] public NativeArray<float4> Out;   // height, water depth (0 dry), closed (1: cave entrance, town), steepest inside

            public void Execute(int i)
            {
                var xz = Origin + new float2(i % Size.x, i / Size.x) * Cell;
                var g = Source.SampleGround(xz);
                var depth = g.HasWater ? math.max(g.WaterSurface - g.Height, 0f) : 0f;
                var hole = Source.CaveClearance(xz, g.Height) < 2f ? 1f : 0f;
                // The steepest the ground gets inside the cell: from its centre out to eight points half a cell away,
                // and between neighbouring pairs of them.
                var steepest = 0f;
                var r = Cell * 0.5f;
                var previous = Source.SampleHeight(xz + new float2(r, 0f));
                for (int k = 1; k <= 8; k++)
                {
                    var a = k * math.PI / 4f;
                    var h = Source.SampleHeight(xz + new float2(math.cos(a), math.sin(a)) * r);
                    steepest = math.max(steepest, math.max(math.abs(h - g.Height) / r, math.abs(h - previous) / (r * 0.765f)));
                    previous = h;
                }
                Out[i] = new float4(g.Height, depth, hole, steepest);
            }
        }

        /// <summary>A* over the grid's 8-neighbours: distance, made dearer by slope (forbidden past MaxSlope), fords, and
        /// forbidden through deep water and cave entrances. Null if there's no way. Pure (reads only its arguments), so
        /// routes run in parallel. Cost and came-from are flat arrays over the grid (W-16: the dictionaries cost most of
        /// the time); unreached cells hold +inf and -1, which behave as the missing keys did.</summary>
        static List<float2> Route(float4[] grid, float[] wanders, int2 size, float2 origin, float2 from, float2 to)
        {
            int Index(int2 c) => c.y * size.x + c.x;
            int2 CellOf(float2 p) => math.clamp((int2)math.round((p - origin) / Cell), 0, size - 1);
            var start = CellOf(from);
            var goal = CellOf(to);
            var cost = new float[grid.Length];
            var came = new int[grid.Length];
            System.Array.Fill(cost, float.PositiveInfinity);
            System.Array.Fill(came, -1);
            cost[Index(start)] = 0f;
            var open = new SortedSet<(float f, int index)> { (0f, Index(start)) };
            var steps = new int2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1) };
            var expanded = 0;
            while (open.Count > 0 && expanded < 400000)
            {
                var current = open.Min;
                open.Remove(current);
                expanded++;
                if (current.index == Index(goal))
                {
                    var path = new List<float2>();
                    var at = current.index;
                    while (true)
                    {
                        path.Add(origin + new float2(at % size.x, at / size.x) * Cell);
                        at = came[at];
                        if (at < 0)
                            break;
                    }
                    path.Reverse();
                    path[0] = from;
                    path[path.Count - 1] = to;
                    return path;
                }
                var c = new int2(current.index % size.x, current.index / size.x);
                var here = grid[current.index];
                foreach (var step in steps)
                {
                    var n = c + step;
                    if (math.any(n < 0) || math.any(n >= size))
                        continue;
                    var ni = Index(n);
                    var there = grid[ni];
                    // (The start and goal stay open even inside a town's closed cells: roads begin at its edge.)
                    if (there.z > 0.5f && ni != Index(goal) || there.y > BridgeDepth)
                        continue;
                    // Cliffs inside a cell, except where the road starts and ends (a town's edge may be rough).
                    if (there.w > CliffSlope && ni != Index(goal) && ni != Index(start))
                        continue;
                    var run = math.length((float2)step) * Cell;
                    var slope = math.abs(there.x - here.x) / run;
                    if (slope > MaxSlope)
                        continue;
                    // Water is dear, fords and bridges alike, so a road crosses a river where it's narrow rather than
                    // following a flat, shallow riverbed along.
                    var wet = there.y > FordDepth ? 20f : there.y > 0f ? 15f : 0f;
                    // Wandering: broad, seeded patches of ground up to 1.8 times dearer, so a road bends round them on
                    // its way instead of running dead straight for kilometres (G-23). Never below 1, so the distance
                    // heuristic still holds.
                    var wander = wanders[ni];
                    var g = cost[current.index] + run * (1f + 25f * slope * slope + wet) * wander;
                    // Each bridge costs as much again to build, so a road along a river doesn't hop back and forth.
                    if (there.y > FordDepth && here.y <= FordDepth)
                        g += 400f;
                    var old = cost[ni];
                    if (old <= g)
                        continue;
                    if (!float.IsPositiveInfinity(old))
                        open.Remove((old + Heuristic(n, goal), ni));
                    cost[ni] = g;
                    came[ni] = current.index;
                    open.Add((g + Heuristic(n, goal), ni));
                }
            }
            return null;

            float Heuristic(int2 a, int2 b) => math.distance((float2)a, (float2)b) * Cell;
        }

        /// <summary>
        /// Wandering: broad, seeded patches of ground up to 1.8 times dearer, so a road bends round them on its way
        /// instead of running dead straight for kilometres (G-23). Never below 1, so the distance heuristic still holds.
        /// Not inlined, so every caller gets the very same float result.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static float Wander(float2 at, float2 offset) => 1f + 0.4f * (noise.snoise(at / WanderScale + offset) + 1f);

        /// <summary>
        /// Bridges where the road crosses water too deep to ford: each wet run with any deep water in it gets spans from
        /// bank to bank, their decks a little above the banks and well above the water, sloping from one bank's height to
        /// the other's.
        /// </summary>
        static void AddBridges(in TerrainSource source, List<float2> path, int settlement, List<SettlementData> settlements,
                               List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementPlot> plots)
        {
            var town = settlements[settlement];
            var layout = layouts[town.Layout];
            if (layout.Bridge == null)
                return;
            // The road sampled every metre, so the banks are found to within one.
            var points = new List<float2>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var n = math.max(1, (int)math.ceil(math.distance(path[i], path[i + 1])));
                for (int k = 0; k < n; k++)
                    points.Add(math.lerp(path[i], path[i + 1], k / (float)n));
            }
            points.Add(path[path.Count - 1]);
            var depths = new float[points.Count];
            var surfaces = new float[points.Count];
            // (Sampled in parallel: each point is independent, and the order below is unchanged.)
            var src = source;
            System.Threading.Tasks.Parallel.For(0, points.Count, i =>
            {
                var g = src.SampleGround(points[i]);
                depths[i] = g.HasWater ? g.WaterSurface - g.Height : 0f;
                surfaces[i] = g.WaterSurface;
            });
            // The channel: water deeper than this (shallow margins and marsh are forded, as the road leaves them).
            const float channel = 0.3f;
            for (int i = 0; i < points.Count; i++)
            {
                if (depths[i] <= channel)
                    continue;
                // A run of channel: from the last shallow point before it to the first one after.
                var start = math.max(i - 1, 0);
                var deepest = 0f;
                var top = float.MinValue;
                for (; i < points.Count && depths[i] > channel; i++)
                {
                    deepest = math.max(deepest, depths[i]);
                    top = math.max(top, surfaces[i]);
                }
                var a = points[start];
                var b = points[math.min(i, points.Count - 1)];
                var length = math.distance(a, b);
                if (deepest > FordDepth && length > LongestBridge)
                    Stats.Unbridged++;   // too deep to ford, too long to bridge: a gap in the road (W-111)
                if (deepest <= FordDepth || length > LongestBridge || length < 1f)
                    continue;
                var along = (b - a) / length;
                var deckA = source.SampleHeight(a) + 0.25f;
                var deckB = source.SampleHeight(b) + 0.25f;
                var spans = (int)math.ceil(length / SpanSpacing);
                var first = (a + b) * 0.5f - along * (spans - 1) * SpanSpacing * 0.5f;
                var half = new float2(math.max(layout.Bridge.Spread, 1f) * 0.5f, 3.3f);
                var reach = math.length(half);
                for (int k = 0; k < spans; k++)
                {
                    var at = first + along * k * SpanSpacing;
                    var t = math.saturate(math.dot(at - a, along) / length);
                    plots.Add(new SettlementPlot
                    {
                        Centre = at,
                        Facing = along,
                        Half = half,
                        Pad = math.max(math.lerp(deckA, deckB, t), top + 0.5f),
                        Role = PlotRole.Bridge,
                        Building = firstBuilding[town.Layout] + layout.Buildings.Count + 2,   // the bridge slot
                        Settlement = settlement,
                        Min = at - reach,
                        Max = at + reach
                    });
                }
            }
        }

        /// <summary>
        /// A palisade round each camp that has one: 3 m lengths round the ring, facing out, leaving gaps where streets
        /// and roads pass, and none in water, over a cave entrance or against a plot.
        /// </summary>
        static void Palisades(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
                              List<SettlementStreet> streets, List<SettlementPlot> plots)
        {
            var built = plots.Count;
            for (int s = 0; s < settlements.Count; s++)
            {
                var town = settlements[s];
                var layout = layouts[town.Layout];
                if (town.Palisade <= 0f || layout.PalisadeWall == null)
                    continue;
                var count = (int)math.floor(2f * math.PI * town.Palisade / PalisadeSpacing);
                for (int k = 0; k < count; k++)
                {
                    var angle = k * 2f * math.PI / count;
                    var outward = new float2(math.sin(angle), math.cos(angle));
                    var at = town.Centre + outward * town.Palisade;
                    var clear = true;
                    foreach (var street in streets)
                    {
                        if (math.any(at < street.Min - 2f) || math.any(at > street.Max + 2f))
                            continue;
                        if (SettlementSampling.Across(street, at, out _) < street.Width * 0.5f + 1.8f)
                        {
                            clear = false;
                            break;
                        }
                    }
                    for (int p = 0; p < built && clear; p++)
                        if (plots[p].Settlement == s && SettlementSampling.Outside(plots[p], at) < 1.2f)
                            clear = false;
                    var g = source.SampleGround(at);
                    if (!clear || g.HasWater && g.WaterSurface > g.Height - 0.2f || source.CaveClearance(at, g.Height) < 2f)
                        continue;
                    plots.Add(new SettlementPlot
                    {
                        Centre = at,
                        Facing = outward,
                        Half = new float2(1.55f, 0.3f),
                        Pad = g.Height,
                        Role = PlotRole.Palisade,
                        Building = firstBuilding[town.Layout] + layout.Buildings.Count + 3,   // the palisade slot
                        Settlement = s,
                        Min = at - 2f,
                        Max = at + 2f
                    });
                }
            }
        }

        /// <summary>Chaikin's corner cutting: each segment's quarter points, ends kept.</summary>
        /// <summary>
        /// Pulls each inner point of a grid path towards its neighbours (a quarter each side), <paramref name="passes"/>
        /// times, ends kept: the grid's alternating straight and diagonal steps become one even line or curve instead
        /// of a sawtooth that smoothing alone only rounds (G-23).
        /// </summary>
        static List<float2> Relax(List<float2> path, int passes)
        {
            if (path.Count < 3)
                return path;
            var result = new List<float2>(path);
            var previous = new float2[path.Count];
            for (int pass = 0; pass < passes; pass++)
            {
                result.CopyTo(previous);
                for (int i = 1; i < previous.Length - 1; i++)
                    result[i] = previous[i] * 0.5f + (previous[i - 1] + previous[i + 1]) * 0.25f;
            }
            return result;
        }

        static List<float2> Smooth(List<float2> path)
        {
            if (path.Count < 3)
                return path;
            var result = new List<float2> { path[0] };
            for (int i = 0; i < path.Count - 1; i++)
            {
                result.Add(math.lerp(path[i], path[i + 1], 0.25f));
                result.Add(math.lerp(path[i], path[i + 1], 0.75f));
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        /// <summary>
        /// The road as street stretches of at most StretchLength, skipping where it runs inside a settlement (its own
        /// streets are there), over water (a ford, left as it is) or over a cave entrance.
        /// </summary>
        /// <summary>
        /// Clears the plots a road runs through: towns are closed to roads only well inside them (Towns are closed, above),
        /// so a road skirting one could pass through a plot at its edge (Z-13: a road through a cabbage patch). Such a
        /// plot is left an empty green (Landscaping, no building), keeping its place in the list, so the roads and
        /// everything placed along them stay where they are. The road's own bridges, gates, fences and palisades stay.
        /// </summary>
        static void ClearPlots(List<float2> path, float width, List<SettlementPlot> plots)
        {
            var reach = width * 0.5f + 0.5f;
            float2 min = float.MaxValue, max = float.MinValue;
            foreach (var point in path)
            {
                min = math.min(min, point);
                max = math.max(max, point);
            }
            for (int p = 0; p < plots.Count; p++)
            {
                var plot = plots[p];
                if (plot.Building < 0 || plot.Role is PlotRole.Bridge or PlotRole.Palisade or PlotRole.Fence or PlotRole.Gate or PlotRole.Signpost ||
                    math.any(plot.Max < min - reach) || math.any(plot.Min > max + reach))
                    continue;
                for (int i = 0; i < path.Count - 1; i++)
                {
                    var a = path[i];
                    var b = path[i + 1];
                    if (math.any(plot.Max < math.min(a, b) - reach) || math.any(plot.Min > math.max(a, b) + reach))
                        continue;
                    // Along the segment every half metre: the plot is a turned rectangle.
                    var steps = (int)math.ceil(math.distance(a, b) / 0.5f);
                    var crossed = false;
                    for (int k = 0; k <= steps && !crossed; k++)
                        crossed = SettlementSampling.Outside(plot, math.lerp(a, b, k / (float)math.max(steps, 1))) < reach;
                    if (!crossed)
                        continue;
                    plot.Role = PlotRole.Landscaping;
                    plot.Building = -1;
                    plots[p] = plot;
                    break;
                }
            }
        }

        /// <summary>The road cut into stretches of at most StretchLength, in order.</summary>
        static List<(float2 p0, float2 p1)> Stretches(List<float2> path)
        {
            var stretches = new List<(float2 p0, float2 p1)>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var n = math.max(1, (int)math.ceil(math.distance(a, b) / StretchLength));
                for (int k = 0; k < n; k++)
                    stretches.Add((math.lerp(a, b, k / (float)n), math.lerp(a, b, (k + 1) / (float)n)));
            }
            return stretches;
        }

        /// <summary>A stretch that isn't paved anyway: inside a settlement (its own streets are there), over water (a
        /// ford, left as it is) or over a cave entrance.</summary>
        static bool Unlaid(TerrainSource source, float2 p0, float2 p1, List<SettlementData> settlements)
        {
            var mid = (p0 + p1) * 0.5f;
            foreach (var s in settlements)
                if (math.distance(mid, s.Centre) < s.Radius * 0.85f)
                    return true;
            var g = source.SampleGround(mid);
            return g.HasWater && g.WaterSurface > g.Height - 0.1f || source.IsCaveHole(mid);
        }

        /// <summary>Whether the ground along a stretch, sampled every metre, rises or falls faster than MaxGrade.</summary>
        internal static bool TooSteep(TerrainSource source, float2 p0, float2 p1)
        {
            var length = math.distance(p0, p1);
            var n = math.max(1, (int)math.ceil(length));
            var step = length / n;
            var previous = source.SampleHeight(p0);
            for (int k = 1; k <= n; k++)
            {
                var h = source.SampleHeight(math.lerp(p0, p1, k / (float)n));
                if (math.abs(h - previous) > MaxGrade * step)
                    return true;
                previous = h;
            }
            return false;
        }

        /// <summary>The middles of a path's stretches that would be laid but are too steep (see MaxGrade).</summary>
        static List<float2> Steep(TerrainSource source, List<float2> path, List<SettlementData> settlements)
        {
            var steep = new List<float2>();
            foreach (var (p0, p1) in Stretches(path))
                if (!Unlaid(source, p0, p1, settlements) && TooSteep(source, p0, p1))
                    steep.Add((p0 + p1) * 0.5f);
            return steep;
        }

        static void Emit(in TerrainSource source, List<float2> path, float width, int settlement, List<SettlementData> settlements,
                         List<SettlementStreet> streets)
        {
            // Every stretch first (skip flag and ground samples), sampled in parallel, then graded and added in order (W-16).
            var stretches = Stretches(path);
            var skip = new bool[stretches.Count];
            var steep = new bool[stretches.Count];
            var ground = new float[stretches.Count][];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, stretches.Count, i =>
            {
                var (p0, p1) = stretches[i];
                skip[i] = Unlaid(src, p0, p1, settlements);
                if (skip[i])
                    return;
                // The ground every metre along the stretch, at TooSteep's very points.
                var n = math.max(1, (int)math.ceil(math.distance(p0, p1)));
                var h = new float[n + 1];
                for (int k = 0; k <= n; k++)
                    h[k] = src.SampleHeight(math.lerp(p0, p1, k / (float)n));
                ground[i] = h;
                steep[i] = TooSteep(src, p0, p1);
            });

            // W-111 (#39): no stretch is left out for being steep any more (W-100 left a gap there, the road ending at
            // the top of a rise and picking up at its foot). Each run of laid stretches gets a road profile no steeper
            // than MaxGrade: the mean of the ground's upper envelope (hills cut down, min over j of h_j + g|i-j|) and its
            // lower one (hollows filled, max of h_j - g|i-j|). Where the ground is already within the grade both equal it
            // exactly, so a road that never was too steep is laid just as before; elsewhere the street's levelling digs a
            // cutting through the crest and banks an embankment below it (Road 2: SettlementSampling.Height blends those
            // wider, by their depth).
            var profile = new float[stretches.Count][];
            for (int start = 0; start < stretches.Count;)
            {
                if (skip[start])
                {
                    start++;
                    continue;
                }
                var end = start;
                while (end < stretches.Count && !skip[end])
                    end++;
                Grade(stretches, ground, profile, start, end);
                start = end;
            }

            var reach = width * 0.5f + SettlementSampling.StreetBlend;
            for (int i = 0; i < stretches.Count; i++)
            {
                if (skip[i])
                    continue;
                var (p0, p1) = stretches[i];
                var y = profile[i];
                var h = ground[i];
                var depth = 0f;
                for (int k = 0; k < y.Length; k++)
                {
                    var linear = math.lerp(y[0], y[y.Length - 1], k / (float)(y.Length - 1));
                    depth = math.max(depth, math.abs(linear - h[k]));
                    // (The stats: deepest cutting and embankment under the road's own line.)
                    var spot = math.lerp(p0, p1, k / (float)(y.Length - 1));
                    if (h[k] - linear > Stats.DeepestCut) { Stats.DeepestCut = h[k] - linear; Stats.CutAt = new float3(spot.x, linear, spot.y); }
                    if (linear - h[k] > Stats.DeepestFill) { Stats.DeepestFill = linear - h[k]; Stats.FillAt = new float3(spot.x, linear, spot.y); }
                }
                var graded = y[0] != h[0] || y[y.Length - 1] != h[h.Length - 1] || steep[i];
                if (steep[i])
                    Stats.SteepStretches++;
                if (graded)
                    Stats.GradedStretches++;
                var from = new float3(p0.x, y[0], p0.y);
                var to = new float3(p1.x, y[y.Length - 1], p1.y);
                // A graded stretch's levelling reaches out over its banks, a metre and a half per metre of depth (the
                // ground beside the road may be deeper than under it, so its bounds take the widest bank).
                var r = graded ? width * 0.5f + SettlementSampling.GradeBlendMax : reach;
                streets.Add(new SettlementStreet
                {
                    A = from,
                    B = to,
                    Width = width,
                    Settlement = settlement,
                    Road = graded ? 2 : 1,
                    Min = math.min(from.xz, to.xz) - r,
                    Max = math.max(from.xz, to.xz) + r
                });
            }
        }

        /// <summary>Road bake numbers for the last island baked (W-111): stretches graded, deepest cut and fill.</summary>
        public static class Stats
        {
            public static int SteepStretches, GradedStretches, Unbridged;
            public static float DeepestCut, DeepestFill;
            public static float3 CutAt, FillAt;
            public static void Reset() { SteepStretches = GradedStretches = Unbridged = 0; DeepestCut = DeepestFill = 0f; }
        }

        /// <summary>
        /// The road profile over stretches [start, end): the mean of the ground's upper and lower MaxGrade envelopes,
        /// each found in two passes (forward, then back) over the metre samples, which share each stretch's end point.
        /// </summary>
        static void Grade(List<(float2 p0, float2 p1)> stretches, float[][] ground, float[][] profile, int start, int end)
        {
            var heights = new List<float>();
            var steps = new List<float>();   // the run before each sample (0 for the first)
            for (int i = start; i < end; i++)
            {
                var h = ground[i];
                var step = math.distance(stretches[i].p0, stretches[i].p1) / (h.Length - 1);
                for (int k = i == start ? 0 : 1; k < h.Length; k++)
                {
                    heights.Add(h[k]);
                    steps.Add(k == 0 ? 0f : step);
                }
            }
            var n = heights.Count;
            var upper = heights.ToArray();
            var lower = heights.ToArray();
            for (int k = 1; k < n; k++)
            {
                upper[k] = math.min(upper[k], upper[k - 1] + MaxGrade * steps[k]);
                lower[k] = math.max(lower[k], lower[k - 1] - MaxGrade * steps[k]);
            }
            for (int k = n - 2; k >= 0; k--)
            {
                upper[k] = math.min(upper[k], upper[k + 1] + MaxGrade * steps[k + 1]);
                lower[k] = math.max(lower[k], lower[k + 1] - MaxGrade * steps[k + 1]);
            }
            var at = 0;
            for (int i = start; i < end; i++)
            {
                var y = new float[ground[i].Length];
                if (i > start)
                    at--;   // the shared end point
                for (int k = 0; k < y.Length; k++, at++)
                    y[k] = upper[at] == lower[at] ? upper[at] : (upper[at] + lower[at]) * 0.5f;
                profile[i] = y;
            }
        }

        /// <summary>
        /// A gate beside the road just outside a settlement (at its start, or its end if <paramref name="atEnd"/>): a plot
        /// with no building, where the Gate anchor goes (AnchorBaking). It once held a signpost (F-43: forks only).
        /// </summary>
        static void AddGate(in TerrainSource source, List<float2> path, int settlement, List<SettlementData> settlements,
                            List<SettlementLayout> layouts, List<SettlementPlot> plots, bool atEnd)
        {
            var town = settlements[settlement];
            var layout = layouts[town.Layout];
            // Walk along the road from that end until it's clear of the settlement.
            for (int k = 0; k < path.Count - 1; k++)
            {
                var i = atEnd ? path.Count - 1 - k : k;
                var j = atEnd ? i - 1 : i + 1;
                if (math.distance(path[i], town.Centre) < town.Radius * 0.95f)
                    continue;
                var along = math.normalizesafe(path[j] - path[i]);
                var side = new float2(-along.y, along.x);
                var at = path[i] + side * (layout.RoadWidth * town.Scale * 0.5f + 1.2f);
                var half = new float2(0.4f, 0.4f);
                plots.Add(new SettlementPlot
                {
                    Centre = at,
                    Facing = -side,
                    Half = half,
                    Pad = source.SampleHeight(at),
                    Role = PlotRole.Gate,
                    Building = -1,
                    Settlement = settlement,
                    Min = at - 1f,
                    Max = at + 1f
                });
                return;
            }
        }
    }
}
