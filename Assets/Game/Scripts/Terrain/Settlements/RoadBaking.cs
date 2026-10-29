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
        const float Margin = 300f;
        const float MaxSlope = 0.45f;
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

            foreach (var (from, to, settlement, toTown, other) in pairs)
            {
                var path = Route(grid, size, min, from, to);
                if (path == null)
                    continue;
                path = Smooth(Smooth(path));
                var layout = layouts[settlements[settlement].Layout];
                Emit(source, path, layout.RoadWidth * settlements[settlement].Scale, settlement, settlements, streets);
                // Roads between settlements (not the spurs to caves) carry roadside landmarks.
                if (toTown)
                    roads?.Add(new RoadsideBaking.RoadPath { Points = path, Width = layout.RoadWidth * settlements[settlement].Scale, Settlement = settlement, Other = other });
                AddBridges(source, path, settlement, settlements, layouts, firstBuilding, plots);
                // Signposts where the road leaves each settlement it joins.
                AddSignpost(source, path, settlement, settlements, layouts, firstBuilding, plots, false);
                if (toTown)
                    AddSignpost(source, path, other, settlements, layouts, firstBuilding, plots, true);
            }
            grid.Dispose();
        }

        [BurstCompile]
        struct GroundJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public int2 Size;
            [WriteOnly] public NativeArray<float4> Out;   // height, water depth (0 dry), cave entrance (1), steepest inside

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
        /// forbidden through deep water and cave entrances. Null if there's no way.</summary>
        static List<float2> Route(NativeArray<float4> grid, int2 size, float2 origin, float2 from, float2 to)
        {
            int Index(int2 c) => c.y * size.x + c.x;
            int2 CellOf(float2 p) => math.clamp((int2)math.round((p - origin) / Cell), 0, size - 1);
            var start = CellOf(from);
            var goal = CellOf(to);
            var cost = new Dictionary<int, float> { [Index(start)] = 0f };
            var came = new Dictionary<int, int>();
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
                        if (!came.TryGetValue(at, out at))
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
                    if (there.z > 0.5f || there.y > BridgeDepth)
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
                    var g = cost[current.index] + run * (1f + 25f * slope * slope + wet);
                    // Each bridge costs as much again to build, so a road along a river doesn't hop back and forth.
                    if (there.y > FordDepth && here.y <= FordDepth)
                        g += 400f;
                    if (cost.TryGetValue(ni, out var old) && old <= g)
                        continue;
                    if (cost.ContainsKey(ni))
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
            for (int i = 0; i < points.Count; i++)
            {
                var g = source.SampleGround(points[i]);
                depths[i] = g.HasWater ? g.WaterSurface - g.Height : 0f;
                surfaces[i] = g.WaterSurface;
            }
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
        static void Emit(in TerrainSource source, List<float2> path, float width, int settlement, List<SettlementData> settlements,
                         List<SettlementStreet> streets)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var n = math.max(1, (int)math.ceil(math.distance(a, b) / StretchLength));
                for (int k = 0; k < n; k++)
                {
                    var p0 = math.lerp(a, b, k / (float)n);
                    var p1 = math.lerp(a, b, (k + 1) / (float)n);
                    var mid = (p0 + p1) * 0.5f;
                    var inside = false;
                    foreach (var s in settlements)
                        if (math.distance(mid, s.Centre) < s.Radius * 0.85f)
                            inside = true;
                    var g = source.SampleGround(mid);
                    if (inside || g.HasWater && g.WaterSurface > g.Height - 0.1f || source.IsCaveHole(mid))
                        continue;
                    var from = new float3(p0.x, source.SampleHeight(p0), p0.y);
                    var to = new float3(p1.x, source.SampleHeight(p1), p1.y);
                    var reach = width * 0.5f + SettlementSampling.StreetBlend;
                    streets.Add(new SettlementStreet
                    {
                        A = from,
                        B = to,
                        Width = width,
                        Settlement = settlement,
                        Road = 1,
                        Min = math.min(from.xz, to.xz) - reach,
                        Max = math.max(from.xz, to.xz) + reach
                    });
                }
            }
        }

        /// <summary>A signpost beside the road just outside a settlement (at its start, or its end if <paramref name="atEnd"/>).</summary>
        static void AddSignpost(in TerrainSource source, List<float2> path, int settlement, List<SettlementData> settlements,
                                List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementPlot> plots, bool atEnd)
        {
            var town = settlements[settlement];
            var layout = layouts[town.Layout];
            if (layout.Signpost == null)
                return;
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
                    Role = PlotRole.Signpost,
                    Building = firstBuilding[town.Layout] + layout.Buildings.Count + 1,   // the signpost slot
                    Settlement = settlement,
                    Min = at - 1f,
                    Max = at + 1f
                });
                return;
            }
        }
    }
}
