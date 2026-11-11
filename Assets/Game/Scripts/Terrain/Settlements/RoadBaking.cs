using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Roads between settlements, baked as more streets in the settlement blob (so they're levelled, paved and kept
    /// clear of plants as streets are): every settlement joined to the others along a minimum spanning tree, then a spur
    /// off that network to each destination beyond the towns (W-138: the sea folk landing, mines, caves; see
    /// Destinations). Each road is routed over a coarse cost grid (gentle slopes preferred, steep
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
        /// W-138: the router's costs, as named settings. Climb weighs a step's slope squared (per metre run), Detour is
        /// how much dearer the wander patches get (see Wander), and a bore would cost TunnelPerMetre times its length
        /// plus TunnelEntry. A was the router before row 2b: it never bores (a climb always comes cheaper), so the only
        /// tunnels were the ones Emit's strict rule finds under a road's own line. C (Zach's pick, W-138 row 2b) makes
        /// climbing dearer and lets a route jump through a mountain along a bore (see FindBores): a straight, graded line
        /// from one hill foot to another with real rock over it all the way.
        /// </summary>
        public struct RouterCosts
        {
            public float Climb, Detour, TunnelPerMetre, TunnelEntry;
            public static readonly RouterCosts A = new() { Climb = 25f, Detour = 0.4f, TunnelPerMetre = float.PositiveInfinity, TunnelEntry = 0f };
            public static readonly RouterCosts C = new() { Climb = 80f, Detour = 0.4f, TunnelPerMetre = 1.5f, TunnelEntry = 150f };
        }

        /// <summary>The router setting roads are baked with.</summary>
        public static readonly RouterCosts Router = RouterCosts.C;

        /// <summary>W-138 row 2b: the steepest a bore may climb (rise over run, portal to portal); outside, MaxGrade holds.</summary>
        public const float BoreGrade = 0.15f;
        /// <summary>W-138 row 2b: the longest bore the router tries (m). Row 3a: 1500 m let the router run dead-straight
        /// kilometre bores where a shorter bore and a valley road fit (Zach asked for meandering); the long one is C0a, by hand.</summary>
        const float BoreLongest = 800f;
        /// <summary>W-138 row 2b: how far a bore's tunnel (TunnelPortal of rock over it) may start from either end of its line (m); the rest is a cutting in.</summary>
        const float BoreApproach = 60f;

        /// <summary>
        /// W-138 row 2b: the bores the roads of the last bake run through, each a pair of path points (both ways round):
        /// the road runs straight between them, at a straight grade, and Emit makes its covered middle a tunnel. Steep,
        /// bridges, plot clearing and roadside landmarks all pass over a bore's line, as it runs under the hill.
        /// </summary>
        static readonly System.Collections.Concurrent.ConcurrentDictionary<(float2, float2), byte> bores = new();

        /// <summary>W-138 row 2b: whether the path segment from a to b is a bore (see bores).</summary>
        public static bool IsBore(float2 a, float2 b) => bores.ContainsKey((a, b));

        static void AddBore(float2 a, float2 b)
        {
            bores[(a, b)] = 0;
            bores[(b, a)] = 0;
        }

        /// <summary>
        /// W-138: a place beyond the towns a road leads to: a spur from the nearest point of the roads already laid
        /// (the town network, then the spurs before it, nearest first) to At. Footpath is how many metres at the end
        /// are only a narrow path; Reach, how far from the roads it may be and still get one; Served, whether it goes
        /// without when a road already passes within ServedReach (a second grotto beside one with a path).
        /// </summary>
        public struct Destination
        {
            public float2 At;
            public string Name;
            public float Footpath, Reach;
            public bool Served;
        }

        /// <summary>W-138: how far from the roads a cave (with chambers) may be and still get a road, and a mountain grotto a path.</summary>
        public const float CaveRoadReach = 1000f;
        /// <summary>W-138: how far from the roads a mine may be and still get a road.</summary>
        public const float MineReach = 1500f;
        /// <summary>W-138: a grotto with a road this close to its mouth already counts as reached.</summary>
        public const float ServedReach = 320f;
        /// <summary>W-138: the last metres to a mountain grotto are a footpath this share of the road's width.</summary>
        public const float FootpathLength = 250f, FootpathWidth = 0.45f;
        /// <summary>W-138: a road ends this far out in front of a cave's mouth.</summary>
        const float MouthApron = 6f;

        /// <summary>
        /// W-138 (row 2a): the destinations beyond the towns, from the landmarks placed so far: the sea folk landing
        /// (<paramref name="landing"/>, required), every mine (a cave whose style is dug), every cave with chambers, and
        /// the grottos that open in a mountain ridge biome (a footpath for the last stretch). Reach and Served trim them
        /// once the town roads are known. In landmark order, so the same island gives the same list.
        /// </summary>
        public static List<Destination> Destinations(in TerrainSource source, IReadOnlyList<LandmarkType> types,
                                                     IReadOnlyList<BiomeDefinition> palette, float2? landing)
        {
            var list = new List<Destination>();
            if (landing.HasValue)
                list.Add(new Destination { At = landing.Value, Name = SeaFolkBaking.LandingName, Reach = float.MaxValue });
            if (!source.Landmarks.IsCreated || types == null)
                return list;
            ref var set = ref source.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var inst = set.Instances[i];
                var type = (uint)inst.Type < (uint)types.Count ? types[inst.Type] : null;
                if (type == null || !type.Cave)
                    continue;
                var mouth = CaveBaking.MouthAt(inst, type);
                var at = mouth - inst.Facing * MouthApron;
                var style = palette != null ? CaveBaking.StyleOf(source, palette, type, inst) : null;
                if (style != null && style.Straightness > 0f)
                    list.Add(new Destination { At = at, Name = $"mine {type.name} {i}", Reach = MineReach });
                else if (type.CaveChambers > 0)
                    list.Add(new Destination { At = at, Name = $"cave {type.name} {i}", Reach = CaveRoadReach });
                else if (palette != null && MountainAt(source, palette, inst.Position))
                    list.Add(new Destination { At = at, Name = $"grotto {type.name} {i}", Reach = CaveRoadReach, Footpath = FootpathLength, Served = true });
            }
            return list;
        }

        /// <summary>Whether the ground's dominant biome there is a mountain ridge biome.</summary>
        static bool MountainAt(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, float2 xz)
        {
            var b = source.SampleGround(xz).Biomes;
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (b.Weights[k] > b.Weights[best])
                    best = k;
            var id = b.Ids[best];
            return id >= 0 && id < palette.Count && palette[id] != null && palette[id].HasTag(BiomeTags.RidgeMountain);
        }

        /// <summary>
        /// Adds the roads to <paramref name="streets"/>, and their signposts and bridges and the camps' palisades to
        /// <paramref name="plots"/> (both appended after every settlement's own).
        /// </summary>
        public static void Build(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
                                 List<SettlementStreet> streets, List<SettlementPlot> plots, List<RoadsideBaking.RoadPath> roads = null,
                                 List<Destination> destinations = null)
        {
            if (settlements.Count == 0)
                return;
            Roads(source, layouts, firstBuilding, settlements, streets, plots, roads, destinations);
            Palisades(source, layouts, firstBuilding, settlements, streets, plots);
        }

        static void Roads(in TerrainSource source, List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementData> settlements,
                          List<SettlementStreet> streets, List<SettlementPlot> plots, List<RoadsideBaking.RoadPath> roads,
                          List<Destination> destinations)
        {
            Stats.Reset();
            bores.Clear();
            BoreNames.Clear();
            Portals.Clear();
            Bridged.Clear();
            SpurStreets = (streets.Count, streets.Count);
            var ends = new List<End>();
            foreach (var s in settlements)
                ends.Add(new End { At = s.Centre + s.Road * s.Radius * 0.95f, Out = s.Road, Settlement = ends.Count, Radius = s.Radius });

            // Which pairs to join: a minimum spanning tree over the settlements (Prim, by straight distance). (The spurs
            // to caves once added here read source.Caves, which isn't baked yet when roads are: W-138 replaced them with
            // Destinations, routed after the town roads.)
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
            var jumps = FindBores(source, cells, size, min);
            System.Threading.Tasks.Parallel.For(0, pairs.Count, i => paths[i] = RoutePair(pairs[i].from, pairs[i].to));

            // W-138 row 2b: a spur (or a hand tunnel's road, or the lake loop) takes a bore only where that makes it
            // SpurBoreSaving shorter than the way over the ground: with a climb as dear as router C's, a spur would
            // otherwise run kilometres through the mountains to a cave a few hundred metres off the road.
            List<float2> SpurRoute(float2 from, float2 to)
            {
                var bored = RoutePair(from, to);
                var open = RoutePair(from, to, false);
                return bored == null || open != null && Length(bored) > SpurBoreSaving * Length(open) ? open : bored;
            }

            List<float2> RoutePair(float2 from, float2 to, bool tunnels = true)
            {
                var found = Route(cells, wander, tunnels ? jumps : null, size, min, from, to);
                var path = found == null ? null : Shape(found);
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
                    var keepA = math.clamp((int2)math.round((from - min) / Cell), 0, size - 1);
                    var keepB = math.clamp((int2)math.round((to - min) / Cell), 0, size - 1);
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
                    var again = Route(closed, wander, tunnels ? jumps : null, size, min, from, to);
                    if (again == null)
                        break;   // no way round: keep the best route so far, and Emit leaves out its steep stretches
                    path = Shape(again);
                }
                return best;
            }

            // W-138: the spurs to the destinations, after the town roads (so those are found exactly as before), one at
            // a time nearest first, each from the nearest point of every road found so far, so a spur may branch off
            // another (two caves side by side share one way in). The branch keeps the width of the road it leaves.
            var spurs = new List<(List<float2> path, int settlement, Destination to)>();
            if (destinations != null && destinations.Count > 0)
            {
                var laid = new List<(List<float2> path, int settlement)>();
                for (int i = 0; i < pairs.Count; i++)
                    if (paths[i] != null)
                        laid.Add((paths[i], pairs[i].settlement));
                var left = new List<Destination>(destinations);
                var edge = min + Cell;
                var far = max - Cell;
                while (left.Count > 0 && laid.Count > 0)
                {
                    // The destination nearest the roads so far (ties to the first listed).
                    int pick = -1, road = -1, point = -1;
                    var nearest = float.MaxValue;
                    for (int d = 0; d < left.Count; d++)
                    {
                        var (r, p, dist) = NearestOn(laid, left[d].At);
                        if (dist < nearest)
                        {
                            nearest = dist;
                            pick = d;
                            road = r;
                            point = p;
                        }
                    }
                    var to = left[pick];
                    left.RemoveAt(pick);
                    if (nearest > to.Reach || to.Served && nearest < ServedReach)
                    {
                        Stats.Spurs.Add(new SpurStat { Name = to.Name + (nearest > to.Reach ? " (too far)" : " (served)"), From = laid[road].path[point], To = to.At });
                        continue;
                    }
                    if (math.any(to.At < edge) || math.any(to.At > far))
                    {
                        Stats.Spurs.Add(new SpurStat { Name = to.Name + " (off the grid)", To = to.At });
                        continue;
                    }
                    var from = laid[road].path[point];
                    var path = from.Equals(to.At) ? null : SpurRoute(from, to.At);
                    if (path == null)
                    {
                        Stats.Spurs.Add(new SpurStat { Name = to.Name + " (no way)", From = from, To = to.At });
                        continue;
                    }
                    laid.Add((path, laid[road].settlement));
                    spurs.Add((path, laid[road].settlement, to));
                }

                // W-138 row 2b: the hand-placed tunnels, each a bore from portal to portal with a road in to each from
                // the nearest road so far (so it closes a loop), then the lake loop: the landing's road on past the
                // marsh folk to the south portal of C0a, a second way between the elf grove and the mining camp.
                foreach (var hand in HandBores)
                    HandTunnel(hand.Name, hand.A, hand.B);
                LakeLoop();

                void HandTunnel(string name, float2 a, float2 b)
                {
                    if (!HandBoreHolds(src, a, b))
                    {
                        Stats.Spurs.Add(new SpurStat { Name = name + " (no mountain over it)", From = a, To = b });
                        return;
                    }
                    var (ra, pa, _) = NearestOn(laid, a);
                    var (rb, pb, _) = NearestOn(laid, b);
                    var settlement = laid[ra].settlement;
                    var into = SpurRoute(laid[ra].path[pa], a);
                    var outOf = SpurRoute(b, laid[rb].path[pb]);
                    if (into == null || outOf == null)
                    {
                        Stats.Spurs.Add(new SpurStat { Name = name + " (no way)", From = a, To = b });
                        return;
                    }
                    AddBore(a, b);
                    BoreNames[(a, b)] = name;
                    BoreNames[(b, a)] = name;
                    var path = new List<float2>(into);
                    path.AddRange(outOf);
                    laid.Add((path, settlement));
                    spurs.Add((path, settlement, new Destination { At = path[path.Count - 1], Name = name }));
                    var width = layouts[settlements[settlement].Layout].RoadWidth * settlements[settlement].Scale;
                    var back = new List<float2>(into);
                    back.Reverse();
                    Portals.Add(new Portal { At = a, Out = WayOut(back, laid[ra].path, pa), Settlement = settlement, Width = width, Name = name, North = a.y > b.y });
                    Portals.Add(new Portal { At = b, Out = WayOut(outOf, laid[rb].path, pb), Settlement = settlement, Width = width, Name = name, North = b.y > a.y });
                    Stats.Loops++;
                }

                void LakeLoop()
                {
                    // From the landing's road end, round the marsh folk village (LakeLoopClear off its middle, on the side
                    // towards the landing) to the first hand tunnel's south portal, or the nearest road there.
                    List<float2> landingRoad = null;
                    foreach (var spur in spurs)
                        if (spur.to.Name == SeaFolkBaking.LandingName)
                            landingRoad = spur.path;
                    if (landingRoad == null)
                        return;
                    var start = landingRoad[landingRoad.Count - 1];
                    var via = LakeLoopVia + math.normalizesafe(start - LakeLoopVia) * LakeLoopClear;
                    var end = Portals.Count > 0 ? (Portals[0].North ? Portals[1].At : Portals[0].At) : laid[NearestOn(laid, via).road].path[NearestOn(laid, via).point];
                    if (math.any(via < edge) || math.any(via > far))
                        return;
                    var leg1 = SpurRoute(start, via);
                    var leg2 = leg1 == null ? null : SpurRoute(via, end);
                    if (leg2 == null)
                    {
                        Stats.Spurs.Add(new SpurStat { Name = "lake loop (no way)", From = start, To = end });
                        return;
                    }
                    var path = new List<float2>(leg1);
                    path.AddRange(leg2.GetRange(1, leg2.Count - 1));
                    var settlement = spurs.Find(sp => sp.path == landingRoad).settlement;
                    laid.Add((path, settlement));
                    spurs.Add((path, settlement, new Destination { At = end, Name = "lake loop" }));
                    Stats.Loops++;
                }
            }

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

            // The spurs, after every town road (so those keep their streets and plots): no gates (they leave a road, not
            // a town) and no roadside landmarks. A footpath's last stretch is laid narrow.
            var firstSpur = streets.Count;
            foreach (var (path, settlement, to) in spurs)
            {
                var layout = layouts[settlements[settlement].Layout];
                var width = layout.RoadWidth * settlements[settlement].Scale;
                var length = Length(path);
                var footpath = math.min(to.Footpath, length);
                if (footpath > 0f)
                {
                    var (road, path2) = Split(path, length - footpath);
                    if (road.Count > 1 && length - footpath > 0.5f)
                        Emit(source, road, width, settlement, settlements, streets);
                    Emit(source, path2, width * FootpathWidth, settlement, settlements, streets);
                }
                else
                    Emit(source, path, width, settlement, settlements, streets);
                ClearPlots(path, width, plots);
                AddBridges(source, path, settlement, settlements, layouts, firstBuilding, plots);
                Stats.Spurs.Add(new SpurStat { Name = to.Name, From = path[0], To = path[path.Count - 1], Length = length, Footpath = footpath });
            }
            Ruin(source, streets, plots);
            SpurStreets = (firstSpur, streets.Count);
        }

        /// <summary>
        /// W-138 row 2b: the hand-placed tunnels (Zach's pick C0a: from the marsh lake's shore north through the ridge into
        /// the Mountains realm), as portal pairs on the default island. Each is checked (HandBoreHolds) before it is laid,
        /// so on another island it is simply left out.
        /// </summary>
        static readonly (string Name, float2 A, float2 B)[] HandBores = { ("C0a", new float2(-1596f, 44f), new float2(-1596f, 1164f)) };

        /// <summary>W-138 row 2b: the marsh folk village the lake loop passes (the default island's), and how far off its middle.</summary>
        static readonly float2 LakeLoopVia = new(-1240f, 320f);
        const float LakeLoopClear = 75f;

        /// <summary>W-138 row 2b: the ruined bridge is the bridge nearest here (the landing road's river crossing), if one is within RuinReach.</summary>
        static readonly float2 RuinNear = new(-1876f, -756f);
        const float RuinReach = 900f;
        /// <summary>W-138 row 2b: another bridge whose middle is this near the ruined one's crosses the same water, and is ruined with it.</summary>
        const float RuinTwin = 20f;
        /// <summary>W-138 row 2b: the ford beside a ruined bridge: how far off its line, its bed under the water (m) and its width.</summary>
        const float FordOffset = 10f, FordBed = 0.35f, FordWidth = 3f;

        /// <summary>W-138 row 2b: a hand tunnel's road ends, for roadside waystations: its portal, the road out from it, and whose road it is.</summary>
        public struct Portal
        {
            public float2 At;
            public List<float2> Out;
            public int Settlement;
            public float Width;
            public string Name;
            public bool North;
        }

        /// <summary>
        /// W-138 row 2b: the way out from a portal for its waystation: its own road, then on along the road it joins
        /// (towards that road's farther end) until it is PortalWayOut long, so a portal whose road joins another a few
        /// metres out still has room beside its way.
        /// </summary>
        static List<float2> WayOut(List<float2> own, List<float2> joined, int at)
        {
            var way = new List<float2>(own);
            var length = Length(own);
            var step = at >= joined.Count - 1 - at ? -1 : 1;
            for (int i = at + step; i >= 0 && i < joined.Count && length < PortalWayOut; i += step)
            {
                length += math.distance(way[way.Count - 1], joined[i]);
                way.Add(joined[i]);
            }
            return way;
        }

        const float PortalWayOut = 300f;

        /// <summary>W-138 row 2b: the hand tunnels' portals the last bake laid (two per tunnel).</summary>
        public static readonly List<Portal> Portals = new();

        /// <summary>W-138 row 2b: each bridge the last bake laid: its first span's centre, how many spans, its banks and its water's top.</summary>
        static readonly List<(float2 first, int count, float2 a, float2 b, int settlement)> Bridged = new();

        /// <summary>
        /// Whether a hand tunnel's line has a mountain over it: TunnelDeep of ground over its straight grade somewhere,
        /// TunnelPortal or more everywhere past BoreApproach from either end, and a grade within BoreGrade.
        /// </summary>
        static bool HandBoreHolds(in TerrainSource source, float2 a, float2 b)
        {
            var length = math.distance(a, b);
            var hA = source.SampleHeight(a);
            var hB = source.SampleHeight(b);
            if (math.abs(hB - hA) > BoreGrade * length)
                return false;
            var peak = 0f;
            var n = (int)math.ceil(length / 10f);
            for (int k = 0; k <= n; k++)
            {
                var t = k / (float)n;
                var cover = source.SampleHeight(math.lerp(a, b, t)) - math.lerp(hA, hB, t);
                peak = math.max(peak, cover);
                if (t * length > BoreApproach && (1f - t) * length > BoreApproach && cover < TunnelPortal)
                    return false;
            }
            return peak >= TunnelDeep;
        }

        /// <summary>
        /// W-138 row 2b: a break in the road on purpose. The bridge nearest RuinNear loses its middle spans (a third of
        /// them from each end stay, standing out over the water as stubs), and a ford is laid beside it: a footpath
        /// FordOffset off the bridge's line, from the road on one bank to the road on the other, its bed raised to
        /// FordBed under the water (a street levels the ground under it), so the river can always be crossed on foot.
        /// </summary>
        static void Ruin(in TerrainSource source, List<SettlementStreet> streets, List<SettlementPlot> plots)
        {
            var pick = -1;
            var nearest = RuinReach;
            for (int i = 0; i < Bridged.Count; i++)
            {
                var d = math.distance((Bridged[i].a + Bridged[i].b) * 0.5f, RuinNear);
                if (Bridged[i].count >= 3 && d < nearest)
                {
                    nearest = d;
                    pick = i;
                }
            }
            if (pick < 0)
                return;
            var (first, count, a, b, settlement) = Bridged[pick];
            var keep = math.max(1, count / 3);
            var along = math.normalizesafe(b - a);
            // Its middle spans, found by where they stand (a later road's ClearPlots may have moved them in the list).
            for (int k = keep; k < count - keep; k++)
            {
                var at = first + along * k * SpanSpacing;
                plots.RemoveAll(pl => pl.Role == PlotRole.Bridge && math.distancesq(pl.Centre, at) < 0.01f);
            }
            // Any other road's bridge over the same crossing goes too (all of it), or the river stays bridged.
            var middle = (a + b) * 0.5f;
            foreach (var twin in Bridged)
            {
                if (twin.first.Equals(first) || math.distance((twin.a + twin.b) * 0.5f, middle) > RuinTwin)
                    continue;
                var twinAlong = math.normalizesafe(twin.b - twin.a);
                for (int k = 0; k < twin.count; k++)
                {
                    var at = twin.first + twinAlong * k * SpanSpacing;
                    plots.RemoveAll(pl => pl.Role == PlotRole.Bridge && math.distancesq(pl.Centre, at) < 0.01f);
                }
            }
            var side = new float2(-along.y, along.x);
            // The ford on whichever side its water is shallower.
            List<float2> best = null;
            var bestDeep = float.MaxValue;
            foreach (var sign in new[] { 1f, -1f })
            {
                var line = new List<float2> { a - along * 12f, a + side * sign * FordOffset - along * 3f, b + side * sign * FordOffset + along * 3f, b + along * 12f };
                var deepest = 0f;
                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    var g = source.SampleGround(math.lerp(line[1], line[2], t));
                    if (g.HasWater)
                        deepest = math.max(deepest, g.WaterSurface - g.Height);
                }
                if (deepest < bestDeep)
                {
                    bestDeep = deepest;
                    best = line;
                }
            }
            var points = new List<float3>();
            for (int i = 0; i < best.Count - 1; i++)
            {
                var n = math.max(1, (int)math.ceil(math.distance(best[i], best[i + 1]) / 2f));
                for (int k = i == 0 ? 0 : 1; k <= n; k++)
                {
                    var at = math.lerp(best[i], best[i + 1], k / (float)n);
                    var g = source.SampleGround(at);
                    var y = g.HasWater && g.WaterSurface - g.Height > FordBed ? g.WaterSurface - FordBed : g.Height;
                    points.Add(new float3(at.x, y, at.y));
                }
            }
            var reach = FordWidth * 0.5f + SettlementSampling.StreetBlend;
            for (int i = 0; i < points.Count - 1; i++)
                streets.Add(new SettlementStreet
                {
                    A = points[i],
                    B = points[i + 1],
                    Width = FordWidth,
                    Settlement = settlement,
                    Road = 1,
                    Min = math.min(points[i].xz, points[i + 1].xz) - reach,
                    Max = math.max(points[i].xz, points[i + 1].xz) + reach
                });
            Stats.Breaks++;
            Stats.Ford = (a, b, best[1], best[2], bestDeep);
        }

        /// <summary>The road point nearest <paramref name="at"/> over every road so far: which road, which point, how far.</summary>
        static (int road, int point, float distance) NearestOn(List<(List<float2> path, int settlement)> laid, float2 at)
        {
            var best = (road: -1, point: -1, distance: float.MaxValue);
            for (int r = 0; r < laid.Count; r++)
            {
                var path = laid[r].path;
                for (int p = 0; p < path.Count; p++)
                {
                    var d = math.distance(path[p], at);
                    if (d < best.distance)
                        best = (r, p, d);
                }
            }
            return best;
        }

        static float Length(List<float2> path)
        {
            var length = 0f;
            for (int i = 0; i < path.Count - 1; i++)
                length += math.distance(path[i], path[i + 1]);
            return length;
        }

        /// <summary>A path cut in two <paramref name="at"/> metres along it (the cut point ends the first and starts the second).</summary>
        static (List<float2> first, List<float2> second) Split(List<float2> path, float at)
        {
            var first = new List<float2> { path[0] };
            var along = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var span = math.distance(path[i], path[i + 1]);
                if (along + span >= at)
                {
                    var cut = math.lerp(path[i], path[i + 1], span > 0f ? (at - along) / span : 0f);
                    first.Add(cut);
                    var second = new List<float2> { cut };
                    for (int k = i + 1; k < path.Count; k++)
                        second.Add(path[k]);
                    return (first, second);
                }
                along += span;
                first.Add(path[i + 1]);
            }
            return (first, new List<float2> { path[path.Count - 1], path[path.Count - 1] });
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
        static List<float2> Route(float4[] grid, float[] wanders, List<(int to, float length)>[] jumps, int2 size, float2 origin, float2 from, float2 to)
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
                    // (A bore's end stays on its cell's centre, where its cover was checked: the road comes to it.)
                    if (path.Count > 1 && math.distance(path[0], path[1]) > BoreStep)
                        path.Insert(0, from);
                    else
                        path[0] = from;
                    if (path.Count > 1 && math.distance(path[path.Count - 1], path[path.Count - 2]) > BoreStep)
                        path.Add(to);
                    else
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
                    var g = cost[current.index] + run * (1f + Router.Climb * slope * slope + wet) * wander;
                    // Each bridge costs as much again to build, so a road along a river doesn't hop back and forth.
                    if (there.y > FordDepth && here.y <= FordDepth)
                        g += 400f;
                    Offer(ni, n, g, current.index);
                }
                // W-138 row 2b: the bores from this cell, straight through the hill to another foot (FindBores).
                var bored = jumps?[current.index];
                if (bored == null)
                    continue;
                foreach (var (ni, length) in bored)
                {
                    if (grid[ni].z > 0.5f && ni != Index(goal))
                        continue;
                    Offer(ni, new int2(ni % size.x, ni / size.x), cost[current.index] + Router.TunnelPerMetre * length + Router.TunnelEntry, current.index);
                }
            }
            return null;

            void Offer(int ni, int2 n, float g, int from)
            {
                var old = cost[ni];
                if (old <= g)
                    return;
                if (!float.IsPositiveInfinity(old))
                    open.Remove((old + Heuristic(n, goal), ni));
                cost[ni] = g;
                came[ni] = from;
                open.Add((g + Heuristic(n, goal), ni));
            }

            float Heuristic(int2 a, int2 b) => math.distance((float2)a, (float2)b) * Cell;
        }

        /// <summary>A path step longer than this is a bore (the grid's own steps are at most a diagonal, 28 m; bores are 60 m or more).</summary>
        const float BoreStep = Cell * 1.5f;
        /// <summary>W-138 row 2b: a spur takes a bore only if that makes it this share of its length over the ground or less.</summary>
        const float SpurBoreSaving = 0.8f;

        /// <summary>
        /// W-138 row 2b: the bores the router may take, as jumps between grid cells (both ways round). From each open,
        /// dry cell at a hill's foot (the ground rising 4 m within 20 m and 10 m within 40 m, in one of 16 directions),
        /// the shortest straight line to another such foot on the far side that passes the strict rule: no more than
        /// BoreGrade from end to end, never coming out of the ground, and a tunnel of TunnelPortal or more of rock over it
        /// starting within BoreApproach of each end, TunnelLeast long or more, with TunnelDeep of cover over
        /// TunnelDeepShare of it (StrictBore's rule, on a 10 m height grid; Emit checks it again every metre). Found in
        /// parallel by grid row and gathered in row order, so the list is the same every bake.
        /// </summary>
        static List<(int to, float length)>[] FindBores(in TerrainSource source, float4[] cells, int2 size, float2 origin)
        {
            var edges = new List<(int to, float length)>[cells.Length];
            if (float.IsPositiveInfinity(Router.TunnelPerMetre))
                return edges;
            const float fine = Cell * 0.5f;
            var fsize = size * 2 - 1;
            var native = new NativeArray<float>(fsize.x * fsize.y, Allocator.TempJob);
            new HeightJob { Source = source, Origin = origin, Size = fsize, Step = fine, Out = native }.Schedule(native.Length, 256).Complete();
            var heights = native.ToArray();
            native.Dispose();
            var extent = (float2)(fsize - 1) * fine;

            float H(float2 p)
            {
                var f = math.clamp((p - origin) / fine, 0f, (float2)(fsize - 1) - 0.001f);
                var c = (int2)math.floor(f);
                var t = f - c;
                var i = c.y * fsize.x + c.x;
                var a = math.lerp(heights[i], heights[i + 1], t.x);
                var b = math.lerp(heights[i + fsize.x], heights[i + fsize.x + 1], t.x);
                return math.lerp(a, b, t.y);
            }

            bool Open(float4 c) => c.z < 0.5f && c.y <= 0f && c.w <= CliffSlope;

            bool Passes(float2 a, float hA, float2 b, float hB, float length)
            {
                var n = (int)math.ceil(length / fine);
                var step = length / n;
                var cover = new float[n + 1];
                var top = 0;
                for (int k = 1; k < n; k++)
                {
                    var t = k / (float)n;
                    cover[k] = H(math.lerp(a, b, t)) - math.lerp(hA, hB, t);
                    if (cover[k] < -0.25f)
                        return false;
                    if (cover[k] > cover[top])
                        top = k;
                }
                if (cover[top] < TunnelDeep)
                    return false;
                int lo = top, hi = top;
                while (lo > 0 && cover[lo - 1] >= TunnelPortal)
                    lo--;
                while (hi < n && cover[hi + 1] >= TunnelPortal)
                    hi++;
                var run = (hi - lo) * step;
                if (run < TunnelLeast || lo * step > BoreApproach || (n - hi) * step > BoreApproach)
                    return false;
                var deep = 0f;
                for (int k = lo + 1; k <= hi; k++)
                    if (cover[k] >= TunnelDeep)
                        deep += step;
                return deep >= TunnelDeepShare * run;
            }

            // W-138 row 3a: a bore never runs under a town or a cave mouth (the closed cells): one passing 11 m from the
            // mining camp took the elf grove's road past the camp's gate and back down a second bore beside it.
            bool UnderTown(float2 a, float2 b, float length)
            {
                for (var t = Cell; t < length - Cell; t += Cell * 0.5f)
                {
                    var c = math.clamp((int2)math.round((math.lerp(a, b, t / length) - origin) / Cell), 0, size - 1);
                    if (cells[c.y * size.x + c.x].z > 0.5f)
                        return true;
                }
                return false;
            }

            var rows = new List<(int a, int b, float length)>[size.y];
            var src = origin;
            System.Threading.Tasks.Parallel.For(0, size.y, y =>
            {
                var found = new List<(int a, int b, float length)>();
                for (int x = 0; x < size.x; x++)
                {
                    var ia = y * size.x + x;
                    var ca = cells[ia];
                    if (!Open(ca))
                        continue;
                    var a = src + new float2(x, y) * Cell;
                    var hA = ca.x;
                    for (int dir = 0; dir < 16; dir++)
                    {
                        var d = new float2(math.cos(dir * math.PI / 8f), math.sin(dir * math.PI / 8f));
                        if (H(a + d * 20f) < hA + 4f || H(a + d * 40f) < hA + 10f)
                            continue;
                        var last = ia;
                        var marched = 0f;
                        for (var length = TunnelLeast; length <= BoreLongest; length += Cell)
                        {
                            // Past where the ground along the way drops under the lowest line a bore from here could
                            // take, no bore that way can stay underground.
                            var dropped = false;
                            for (; marched < length && !dropped; marched += fine)
                                dropped = H(a + d * marched) < hA - BoreGrade * marched - 0.25f;
                            var reach = a + d * length;
                            if (dropped || math.any(reach < src) || math.any(reach > src + extent))
                                break;
                            var cb = (int2)math.round((reach - src) / Cell);
                            var ib = cb.y * size.x + cb.x;
                            if (ib == last)
                                continue;
                            last = ib;
                            var b = src + (float2)cb * Cell;
                            var cell = cells[ib];
                            var span = math.distance(a, b);
                            if (!Open(cell) || math.abs(cell.x - hA) > BoreGrade * span || H(b - (b - a) / span * 20f) < cell.x + 4f)
                                continue;
                            if (UnderTown(a, b, span) || !Passes(a, hA, b, cell.x, span))
                                continue;
                            found.Add((ia, ib, span));
                            break;
                        }
                    }
                }
                rows[y] = found;
            });
            foreach (var row in rows)
                foreach (var (a, b, length) in row)
                {
                    (edges[a] ??= new List<(int to, float length)>()).Add((b, length));
                    (edges[b] ??= new List<(int to, float length)>()).Add((a, length));
                }
            return edges;
        }

        /// <summary>W-138 row 2b: the ground's height on a square grid (FindBores' fine grid).</summary>
        [BurstCompile]
        struct HeightJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public int2 Size;
            public float Step;
            [WriteOnly] public NativeArray<float> Out;

            public void Execute(int i) => Out[i] = Source.SampleHeight(Origin + new float2(i % Size.x, i / Size.x) * Step);
        }

        /// <summary>
        /// W-138 row 2b: a found route made a road: each run between bores relaxed and smoothed as before (its ends kept,
        /// so each bore stays a straight line between the cell centres it was checked on), and the bores noted.
        /// </summary>
        static List<float2> Shape(List<float2> found)
        {
            var result = new List<float2>();
            var start = 0;
            for (int i = 0; i < found.Count; i++)
            {
                // (The first and last hops are never bores: Route moved those ends off their cells' centres.)
                if (i < found.Count - 1 && (i == 0 || i == found.Count - 2 || math.distance(found[i], found[i + 1]) <= BoreStep))
                    continue;
                result.AddRange(Smooth(Smooth(Relax(found.GetRange(start, i - start + 1), 3))));
                if (i < found.Count - 1)
                    AddBore(found[i], found[i + 1]);
                start = i + 1;
            }
            return result;
        }

        /// <summary>
        /// Wandering: broad, seeded patches of ground up to 1.8 times dearer, so a road bends round them on its way
        /// instead of running dead straight for kilometres (G-23). Never below 1, so the distance heuristic still holds.
        /// Not inlined, so every caller gets the very same float result.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static float Wander(float2 at, float2 offset) => 1f + Router.Detour * (noise.snoise(at / WanderScale + offset) + 1f);

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
            var under = new List<bool>();   // W-138 row 2b: on a bore, under the hill (never bridged)
            for (int i = 0; i < path.Count - 1; i++)
            {
                var n = math.max(1, (int)math.ceil(math.distance(path[i], path[i + 1])));
                var isBore = IsBore(path[i], path[i + 1]);
                for (int k = 0; k < n; k++)
                {
                    points.Add(math.lerp(path[i], path[i + 1], k / (float)n));
                    under.Add(isBore && k > 0);
                }
            }
            points.Add(path[path.Count - 1]);
            under.Add(false);
            var depths = new float[points.Count];
            var surfaces = new float[points.Count];
            // (Sampled in parallel: each point is independent, and the order below is unchanged.)
            var src = source;
            System.Threading.Tasks.Parallel.For(0, points.Count, i =>
            {
                if (under[i])
                    return;
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
                Bridged.Add((first, spans, a, b, settlement));
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
                    if (IsBore(a, b) || math.any(plot.Max < math.min(a, b) - reach) || math.any(plot.Min > math.max(a, b) + reach))
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
        static List<(float2 p0, float2 p1)> Stretches(List<float2> path) => Stretches(path, out _);

        /// <summary>The road cut into stretches, and for each the path segment it is on if that segment is a bore (else -1).</summary>
        static List<(float2 p0, float2 p1)> Stretches(List<float2> path, out List<int> bore)
        {
            var stretches = new List<(float2 p0, float2 p1)>();
            bore = new List<int>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var n = math.max(1, (int)math.ceil(math.distance(a, b) / StretchLength));
                var isBore = IsBore(a, b);
                for (int k = 0; k < n; k++)
                {
                    stretches.Add((math.lerp(a, b, k / (float)n), math.lerp(a, b, (k + 1) / (float)n)));
                    bore.Add(isBore ? i : -1);
                }
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
            var stretches = Stretches(path, out var bore);
            for (int i = 0; i < stretches.Count; i++)
            {
                var (p0, p1) = stretches[i];
                // (A bore runs under the hill at its own grade: never steep.)
                if (bore[i] < 0 && !Unlaid(source, p0, p1, settlements) && TooSteep(source, p0, p1))
                    steep.Add((p0 + p1) * 0.5f);
            }
            return steep;
        }

        static void Emit(in TerrainSource source, List<float2> path, float width, int settlement, List<SettlementData> settlements,
                         List<SettlementStreet> streets)
        {
            // Every stretch first (skip flag and ground samples), sampled in parallel, then graded and added in order (W-16).
            var stretches = Stretches(path, out var bore);
            var skip = new bool[stretches.Count];
            var steep = new bool[stretches.Count];
            var ground = new float[stretches.Count][];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, stretches.Count, i =>
            {
                var (p0, p1) = stretches[i];
                // (A bore is laid all the way: it runs under any water or town edge above it.)
                skip[i] = bore[i] < 0 && Unlaid(src, p0, p1, settlements);
                if (skip[i])
                    return;
                // The ground every metre along the stretch, at TooSteep's very points.
                var n = math.max(1, (int)math.ceil(math.distance(p0, p1)));
                var h = new float[n + 1];
                for (int k = 0; k <= n; k++)
                    h[k] = src.SampleHeight(math.lerp(p0, p1, k / (float)n));
                ground[i] = h;
                steep[i] = bore[i] < 0 && TooSteep(src, p0, p1);
            });

            // W-138 row 2b: the bores' runs of stretches (one per bore segment). Each is graded as if the ground ran
            // straight from its one hill foot to the other, so the road either side meets it at its own levels.
            var runs = new List<(int start, int end)>();
            for (int i = 0; i < stretches.Count; i++)
            {
                if (bore[i] < 0)
                    continue;
                var e = i;
                while (e < stretches.Count && bore[e] == bore[i])
                    e++;
                runs.Add((i, e));
                i = e - 1;
            }
            var graded = ground;
            if (runs.Count > 0)
            {
                graded = (float[][])ground.Clone();
                foreach (var (rs, re) in runs)
                {
                    var hA = ground[rs][0];
                    var hB = ground[re - 1][ground[re - 1].Length - 1];
                    for (int i = rs; i < re; i++)
                    {
                        var line = new float[ground[i].Length];
                        for (int k = 0; k < line.Length; k++)
                            line[k] = math.lerp(hA, hB, (i - rs + k / (float)(line.Length - 1)) / (re - rs));
                        graded[i] = line;
                    }
                }
            }

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
                Grade(stretches, graded, profile, start, end);
                start = end;
            }

            // A bore's road runs at one straight grade between its graded ends; its tunnel is the run of stretches round
            // the thickest with TunnelPortal or more of rock over every metre (the rest is a cutting in to each portal).
            var tunnel = new bool[stretches.Count];
            foreach (var (rs, re) in runs)
            {
                var length = 0f;
                for (int i = rs; i < re; i++)
                    length += math.distance(stretches[i].p0, stretches[i].p1);
                var y0 = profile[rs][0];
                var y1 = profile[re - 1][profile[re - 1].Length - 1];
                var along = 0f;
                var thinnest = new float[re - rs];
                var peak = new float[re - rs];
                var thickest = rs;
                for (int i = rs; i < re; i++)
                {
                    var span = math.distance(stretches[i].p0, stretches[i].p1);
                    var y = profile[i];
                    thinnest[i - rs] = float.MaxValue;
                    peak[i - rs] = float.MinValue;
                    for (int k = 0; k < y.Length; k++)
                    {
                        y[k] = math.lerp(y0, y1, (along + span * k / (float)(y.Length - 1)) / length);
                        var cover = ground[i][k] - y[k];
                        thinnest[i - rs] = math.min(thinnest[i - rs], cover);
                        peak[i - rs] = math.max(peak[i - rs], cover);
                    }
                    if (thinnest[i - rs] > thinnest[thickest - rs])
                        thickest = i;
                    along += span;
                }
                var stat = new TunnelStat { Name = BoreNames.TryGetValue((path[bore[rs]], path[bore[rs] + 1]), out var named) ? named : "router", A = stretches[rs].p0, B = stretches[re - 1].p1, Grade = math.abs(y1 - y0) / math.max(length, 1f), Line = length };
                if (thinnest[thickest - rs] >= TunnelPortal)
                {
                    int first = thickest, last = thickest;
                    while (first > rs && thinnest[first - 1 - rs] >= TunnelPortal)
                        first--;
                    while (last < re - 1 && thinnest[last + 1 - rs] >= TunnelPortal)
                        last++;
                    for (int i = first; i <= last; i++)
                    {
                        tunnel[i] = true;
                        stat.Length += math.distance(stretches[i].p0, stretches[i].p1);
                        stat.PeakCover = math.max(stat.PeakCover, peak[i - rs]);
                    }
                    stat.A = stretches[first].p0;
                    stat.B = stretches[last].p1;
                    stat.YA = profile[first][0];
                    stat.YB = profile[last][profile[last].Length - 1];
                    stat.CoverA = thinnest[first - rs];
                    stat.CoverB = thinnest[last - rs];
                    var centre = math.lerp(stat.A, stat.B, 0.5f);
                    Stats.Tunnels.Add(new float4(centre.x, math.max(stat.YA, stat.YB), centre.y, stat.Length));
                }
                Stats.Bores.Add(stat);
            }

            // W-110, made strict by W-138: a crest the grade cuts through (TunnelCrest deep or more) is bored only where
            // the hill is a real mountain over the road. The bore runs at a straight grade between the road profile's
            // levels at two stretch ends either side of the crest, the ground staying above that line all the way; its
            // tunnel is the run of stretches with at least TunnelPortal of hill over every metre (so each portal arch
            // has that much rock over it), and it is bored only when that run is TunnelLeast long or more with
            // TunnelDeep of cover over TunnelDeepShare of it. Of the pairs that pass, the one with the most deep metres
            // wins. The stretches become tunnel roads (Road 3) and CaveBaking bores their passage; the rest of the bore
            // is cuttings at its grade up to the portals. Any lesser crest stays a cutting (the small-crest tunnels
            // W-110 bored are gone).
            for (int c = 0; c < stretches.Count; c++)
            {
                if (skip[c] || bore[c] >= 0 || DeepestCut(ground[c], profile[c]) < TunnelCrest)
                    continue;
                int bestStart = -1, bestEnd = -1, bestFirst = -1, bestLast = -1;
                float bestDeep = 0f, bestBored = 0f;
                for (int s = c; s >= 0 && s > c - TunnelReach && !skip[s] && !tunnel[s] && bore[s] < 0; s--)
                    for (int e = c + 1; e <= stretches.Count && e <= c + TunnelReach && !skip[e - 1] && bore[e - 1] < 0; e++)
                    {
                        // A portal opens on a cutting, never straight onto a bridge or other skipped stretch.
                        if (SkipNear(skip, s - TunnelLead, s) || SkipNear(skip, e, e + TunnelLead))
                            continue;
                        if (StrictBore(stretches, ground, profile, s, e, out var first, out var last, out var bored, out var deep) && deep > bestDeep)
                        {
                            bestDeep = deep;
                            bestBored = bored;
                            (bestStart, bestEnd, bestFirst, bestLast) = (s, e, first, last);
                        }
                    }
                if (bestStart < 0)
                    continue;
                var length = 0f;
                for (int i = bestStart; i < bestEnd; i++)
                    length += math.distance(stretches[i].p0, stretches[i].p1);
                var y0 = profile[bestStart][0];
                var y1 = profile[bestEnd - 1][profile[bestEnd - 1].Length - 1];
                var along = 0f;
                for (int i = bestStart; i < bestEnd; i++)
                {
                    var span = math.distance(stretches[i].p0, stretches[i].p1);
                    var y = profile[i];
                    for (int k = 0; k < y.Length; k++)
                        y[k] = math.lerp(y0, y1, (along + span * k / (float)(y.Length - 1)) / length);
                    along += span;
                }
                for (int i = bestFirst; i <= bestLast; i++)
                    tunnel[i] = true;
                var centre = math.lerp(stretches[bestFirst].p0, stretches[bestLast].p1, 0.5f);
                Stats.Tunnels.Add(new float4(centre.x, math.max(profile[bestFirst][0], profile[bestLast][profile[bestLast].Length - 1]), centre.y, bestBored));
                c = math.max(c, bestEnd - 1);
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
                    // (The stats: deepest cutting and embankment under the road's own line, tunnels left out.)
                    if (tunnel[i])
                        continue;
                    var spot = math.lerp(p0, p1, k / (float)(y.Length - 1));
                    if (h[k] - linear > Stats.DeepestCut) { Stats.DeepestCut = h[k] - linear; Stats.CutAt = new float3(spot.x, linear, spot.y); }
                    if (linear - h[k] > Stats.DeepestFill) { Stats.DeepestFill = linear - h[k]; Stats.FillAt = new float3(spot.x, linear, spot.y); }
                }
                var isGraded = y[0] != h[0] || y[y.Length - 1] != h[h.Length - 1] || steep[i];
                if (steep[i])
                    Stats.SteepStretches++;
                if (isGraded)
                    Stats.GradedStretches++;
                var from = new float3(p0.x, y[0], p0.y);
                var to = new float3(p1.x, y[y.Length - 1], p1.y);
                // A graded stretch's levelling reaches out over its banks, a metre and a half per metre of depth (the
                // ground beside the road may be deeper than under it, so its bounds take the widest bank).
                var r = isGraded ? width * 0.5f + SettlementSampling.GradeBlendMax : reach;
                streets.Add(new SettlementStreet
                {
                    A = from,
                    B = to,
                    Width = width,
                    Settlement = settlement,
                    Road = tunnel[i] ? SettlementSampling.TunnelRoad : isGraded ? 2 : 1,
                    Min = math.min(from.xz, to.xz) - r,
                    Max = math.max(from.xz, to.xz) + r
                });
            }
        }

        /// <summary>
        /// W-138: the streets the last Build laid as spurs (first, end). Roadside landmarks keep only their middles off
        /// these, so a spur leaving a road beside a roadside cave passes it instead of pushing it away.
        /// </summary>
        public static (int First, int End) SpurStreets;

        /// <summary>W-138: a spur laid to a destination: where it branched off a road, where it ends, its length and the footpath share of it (m).</summary>
        public struct SpurStat { public string Name; public float2 From, To; public float Length, Footpath; }

        /// <summary>
        /// W-138 row 2b: a bore's tunnel: its portals (A, B) and their road levels, its length, the most rock over it, the
        /// least over each portal's stretch, its grade (portal to portal) and the bore line's whole length (m). Name is
        /// the hand-placed tunnel's, or "router".
        /// </summary>
        public struct TunnelStat { public string Name; public float2 A, B; public float YA, YB, Length, PeakCover, CoverA, CoverB, Grade, Line; }

        /// <summary>W-138 row 2b: the hand-placed bores' names (by their path segment, both ways round), for Stats.Bores.</summary>
        static readonly Dictionary<(float2, float2), string> BoreNames = new();

        /// <summary>Road bake numbers for the last island baked (W-111): stretches graded, deepest cut and fill.</summary>
        public static class Stats
        {
            public static int SteepStretches, GradedStretches, Unbridged;
            public static float DeepestCut, DeepestFill;
            public static float3 CutAt, FillAt;
            /// <summary>W-110 (strict since W-138): each tunnel bored, its middle (xz), its west/first portal's road level (y) and length (w).</summary>
            public static readonly List<float4> Tunnels = new();
            public static void Reset() { SteepStretches = GradedStretches = Unbridged = Loops = Breaks = 0; DeepestCut = DeepestFill = 0f; Tunnels.Clear(); Spurs.Clear(); Bores.Clear(); }
            /// <summary>W-138 row 2b: each bore laid (its tunnel, or Length 0 when no stretch of it had TunnelPortal of rock over it).</summary>
            public static readonly List<TunnelStat> Bores = new();
            /// <summary>W-138 row 2b: second routes laid (each closes a loop) and roads broken on purpose (a ruined bridge, with a ford).</summary>
            public static int Loops, Breaks;
            /// <summary>W-138 row 2b: the ruined bridge's banks, the ford's ends in the water and the deepest water it crosses before its bed is raised.</summary>
            public static (float2 a, float2 b, float2 fordA, float2 fordB, float deep) Ford;
            /// <summary>W-138: each spur laid to a destination beyond the towns, and each destination left without one (Length 0, Name saying why).</summary>
            public static readonly List<SpurStat> Spurs = new();
        }

        /// <summary>W-110: how deep the grade must cut through a crest for a bore to be tried there </summary>
        const float TunnelCrest = 0.75f;
        /// <summary>W-110: the most stretches either side of a crest a bore may reach to its portals.</summary>
        const int TunnelReach = 24;
        /// <summary>W-138: the shortest tunnel bored (m).</summary>
        public const float TunnelLeast = 60f;
        /// <summary>W-138: the cover (m of hill over the road) a tunnel must have over TunnelDeepShare of its length.</summary>
        public const float TunnelDeep = 20f, TunnelDeepShare = 0.5f;
        /// <summary>W-138: the least hill over every metre of a tunnel stretch's road, its portal arches included (the arch and some rock); thinner, it is a cutting into the portal.</summary>
        public const float TunnelPortal = 6f;
        /// <summary>W-110: the fewest ordinary stretches between a bore's portal and a skipped one (a bridge's deck sits above the grade).</summary>
        const int TunnelLead = 2;

        /// <summary>Whether any stretch from first up to (not including) last is skipped.</summary>
        static bool SkipNear(bool[] skip, int first, int last)
        {
            for (int i = math.max(first, 0); i < math.min(last, skip.Length); i++)
                if (skip[i])
                    return true;
            return false;
        }

        /// <summary>
        /// W-138's strict tunnel rule for a straight-grade road from stretch start's level to stretch end - 1's: false
        /// when the ground dips under that line anywhere (the road would come out in the air). Otherwise the tunnel is
        /// the run of stretches [first, last] round the thickest one with TunnelPortal or more over every metre, and it
        /// passes when that run is TunnelLeast long or more with TunnelDeep of cover over TunnelDeepShare of it
        /// (<paramref name="bored"/> its length, <paramref name="deep"/> the metres that deep).
        /// </summary>
        static bool StrictBore(List<(float2 p0, float2 p1)> stretches, float[][] ground, float[][] profile, int start, int end,
                               out int first, out int last, out float bored, out float deep)
        {
            first = last = -1;
            bored = deep = 0f;
            var length = 0f;
            for (int i = start; i < end; i++)
                length += math.distance(stretches[i].p0, stretches[i].p1);
            var y0 = profile[start][0];
            var y1 = profile[end - 1][profile[end - 1].Length - 1];
            var thinnest = new float[end - start];
            var deepRun = new float[end - start];
            var thickest = start;
            var along = 0f;
            for (int i = start; i < end; i++)
            {
                var span = math.distance(stretches[i].p0, stretches[i].p1);
                var h = ground[i];
                var step = span / (h.Length - 1);
                var least = float.MaxValue;
                for (int k = 0; k < h.Length; k++)
                {
                    var cover = h[k] - math.lerp(y0, y1, (along + step * k) / length);
                    if (cover < -0.25f)
                        return false;
                    least = math.min(least, cover);
                    if (k > 0 && cover >= TunnelDeep)
                        deepRun[i - start] += step;
                }
                thinnest[i - start] = least;
                if (least > thinnest[thickest - start])
                    thickest = i;
                along += span;
            }
            if (thinnest[thickest - start] < TunnelPortal)
                return false;
            first = last = thickest;
            while (first > start && thinnest[first - 1 - start] >= TunnelPortal)
                first--;
            while (last < end - 1 && thinnest[last + 1 - start] >= TunnelPortal)
                last++;
            for (int i = first; i <= last; i++)
            {
                bored += math.distance(stretches[i].p0, stretches[i].p1);
                deep += deepRun[i - start];
            }
            return bored >= TunnelLeast && deep >= TunnelDeepShare * bored;
        }

        /// <summary>The deepest cut under a stretch: its ground less the road's straight line, the most over every metre.</summary>
        static float DeepestCut(float[] h, float[] y)
        {
            var most = float.MinValue;
            for (int k = 0; k < y.Length; k++)
                most = math.max(most, h[k] - math.lerp(y[0], y[y.Length - 1], k / (float)(y.Length - 1)));
            return most;
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
