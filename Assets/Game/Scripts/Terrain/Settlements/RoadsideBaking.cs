using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Landmarks beside the roads (a lone inn, a wayside shrine, a monument, a campsite, a ruin): once the roads between
    /// settlements are routed, every RoadsideSpacing metres along each one is a candidate spot, seeded by where it is, and
    /// each roadside type rolls for it by its chance per kilometre (the lowest roll against its chance wins, so adding a
    /// type only takes the spots it wins). A spot that wins stands off one side of the road, facing it, with a short spur
    /// to it and a signpost at the fork, where the ground is dry, gentle and clear of settlements, other roads and
    /// landmarks. The new landmarks are
    /// appended to the landmark set afterwards (LandmarkBaking.Append), so nothing already placed moves.
    /// </summary>
    public static class RoadsideBaking
    {
        const float RoadsideSpacing = 120f;
        /// <summary>Roadside landmarks keep at least this far apart (m).</summary>
        const float Apart = 260f;
        /// <summary>Clear ground between a landmark's edge and the road beside it (m).</summary>
        const float Verge = 3f;
        /// <summary>No fork signpost within this far of a settlement's edge (m).</summary>
        const float TownClear = 80f;

        /// <summary>A road's centreline, its width and the settlement it belongs to (RoadBaking).</summary>
        public struct RoadPath
        {
            public List<float2> Points;
            public float Width;
            /// <summary>The settlement at its start, and the one at its end.</summary>
            public int Settlement, Other;
        }

        const float FenceSpacing = 3f;
        /// <summary>How far past a settlement's edge its road fences run (m).</summary>
        const float FenceReach = 120f;

        /// <summary>
        /// The roadside landmarks along <paramref name="roads"/>, with their spurs added to <paramref name="streets"/>.
        /// </summary>
        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<LandmarkType> types, IReadOnlyList<RoadsideLandmark> roadside,
                                                   List<RoadPath> roads, List<SettlementData> settlements, List<SettlementStreet> streets,
                                                   List<SettlementLayout> layouts, List<int> firstBuilding, List<SettlementPlot> plots)
        {
            var placed = new List<LandmarkInstance>();
            if (roadside == null || roadside.Count == 0 || roads.Count == 0)
                return placed;
            var src = source;   // local functions can't capture an in parameter
            var seed = source.World.Seed;
            var hashes = new uint[roadside.Count];
            for (int e = 0; e < roadside.Count; e++)
                hashes[e] = roadside[e].Type != null ? LandmarkBaking.NameHash(roadside[e].Type) : 0u;
            var roadStreets = streets.Count;
            var forks = new List<float2>();

            foreach (var road in roads)
            {
                // Along the road at a fixed spacing from its start.
                var along = 0f;
                var next = RoadsideSpacing * 0.5f;
                for (int i = 0; i < road.Points.Count - 1; i++)
                {
                    var a = road.Points[i];
                    var b = road.Points[i + 1];
                    var length = math.distance(a, b);
                    while (next <= along + length)
                    {
                        var t = (next - along) / math.max(length, 1e-4f);
                        Try(math.lerp(a, b, t), math.normalizesafe(b - a), road);
                        next += RoadsideSpacing;
                    }
                    along += length;
                }
            }
            foreach (var road in roads)
            {
                Fences(road, false);
                Fences(road, true);
            }
            return placed;

            // Rail fences along a road for a stretch just outside the settlement at one end (fields), on one side or
            // both, about half the time, with gaps where anything else is: forks, signposts, bridges, other streets,
            // water and landmarks.
            void Fences(RoadPath road, bool atEnd)
            {
                var s = atEnd ? road.Other : road.Settlement;
                if (s < 0)
                    return;
                var town = settlements[s];
                var layout = layouts[town.Layout];
                if (layout.RoadFence == null)
                    return;
                var start = atEnd ? road.Points[road.Points.Count - 1] : road.Points[0];
                var key = math.hash(new uint4(seed, (uint)(int)math.floor(start.x), (uint)(int)math.floor(start.y), 0xFE7CEu)) | 1u;
                if ((key & 0xFF) > 140)
                    return;
                var sides = (key & 0x100) != 0 ? 2 : 1;
                var first = (key & 0x200) != 0 ? 1f : -1f;
                var builtPlots = plots.Count;
                // The road from that end, every metre.
                var points = new List<float2>();
                for (int i = 0; i < road.Points.Count - 1; i++)
                {
                    var a = road.Points[atEnd ? road.Points.Count - 1 - i : i];
                    var b = road.Points[atEnd ? road.Points.Count - 2 - i : i + 1];
                    var n = math.max(1, (int)math.ceil(math.distance(a, b)));
                    for (int k = 0; k < n; k++)
                        points.Add(math.lerp(a, b, k / (float)n));
                }
                for (int side = 0; side < sides; side++)
                {
                    var sign = side == 0 ? first : -first;
                    // The fence line a little off the road on this side, from the settlement's edge out.
                    var line = new List<float2>();
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        var d = math.distance(points[i], town.Centre);
                        if (d < town.Radius * 1.05f)
                        {
                            if (line.Count > 0)
                                break;
                            continue;
                        }
                        if (d > town.Radius + FenceReach)
                            break;
                        var tangent = math.normalizesafe(points[i + 1] - points[i]);
                        line.Add(points[i] + new float2(-tangent.y, tangent.x) * sign * (road.Width * 0.5f + 1.6f));
                    }
                    if (line.Count < 2)
                        continue;
                    // Posts exactly FenceSpacing apart along it (so round a bend too), each length turned to run from
                    // its post straight to the next.
                    var post = line[0];
                    for (int j = 1; j < line.Count; )
                    {
                        if (math.distance(line[j], post) < FenceSpacing)
                        {
                            j++;
                            continue;
                        }
                        var along = math.normalizesafe(line[j] - post);
                        var next = post + along * FenceSpacing;
                        var from = post;
                        post = next;
                        if (!FenceClear(from, builtPlots) || !FenceClear((from + next) * 0.5f, builtPlots) || !FenceClear(next, builtPlots))
                            continue;
                        var centre = from + along * (FenceSpacing * 0.5f);
                        plots.Add(new SettlementPlot
                        {
                            Centre = centre,
                            // Its +X along the run (the post at its -X end stands on this post).
                            Facing = new float2(-along.y, along.x),
                            Half = new float2(1.55f, 0.3f),
                            Pad = src.SampleHeight(centre),
                            Role = PlotRole.Fence,
                            Building = firstBuilding[town.Layout] + layout.Buildings.Count + 4,   // the road fence slot
                            Settlement = s,
                            Min = centre - 2f,
                            Max = centre + 2f
                        });
                    }
                }
            }

            bool FenceClear(float2 at, int builtPlots)
            {
                foreach (var f in forks)
                    if (math.distance(at, f) < 9f)
                        return false;
                foreach (var street in streets)
                {
                    if (math.any(at < street.Min - 2f) || math.any(at > street.Max + 2f))
                        continue;
                    if (SettlementSampling.Across(street, at, out _) < street.Width * 0.5f + 1.2f)
                        return false;
                }
                for (int p = 0; p < builtPlots; p++)
                    if (plots[p].Role != PlotRole.Fence && math.all(at > plots[p].Min - 3f) && math.all(at < plots[p].Max + 3f))
                        return false;
                foreach (var p in placed)
                    if (math.distance(at, p.Position) < types[p.Type].Radius + 2f)
                        return false;
                if (src.Landmarks.IsCreated)
                {
                    ref var set = ref src.Landmarks.Value;
                    for (int l = 0; l < set.Instances.Length; l++)
                        if (math.distance(at, set.Instances[l].Position) < set.Types[set.Instances[l].Type].Radius * set.Instances[l].Scale)
                            return false;
                }
                var g = src.SampleGround(at);
                return !(g.HasWater && g.WaterSurface > g.Height - 0.2f) && g.River < 0.02f && src.CaveClearance(at, g.Height) >= 2f;
            }

            void Try(float2 at, float2 tangent, RoadPath road)
            {
                // The spot's draws, from where it is (whole 4 m cells, so a road rerouted slightly keeps its spots).
                var key = math.hash(new uint4(seed, (uint)(int)math.floor(at.x / 4f), (uint)(int)math.floor(at.y / 4f), 0x50AD51DEu)) | 1u;
                var pick = -1;
                var best = 1f;
                for (int e = 0; e < roadside.Count; e++)
                {
                    var chance = roadside[e].Type != null ? roadside[e].PerKm * RoadsideSpacing / 1000f : 0f;
                    if (chance <= 0f)
                        continue;
                    var u = (BiomeSampling.Scramble(math.hash(new uint2(key, hashes[e]))) >> 8) * (1f / 16777216f);
                    if (u < chance && u / chance < best)
                    {
                        best = u / chance;
                        pick = e;
                    }
                }
                if (pick < 0)
                    return;
                var type = roadside[pick].Type;
                var typeIndex = -1;
                for (int k = 0; k < types.Count; k++)
                    if (types[k] == type)
                        typeIndex = k;
                if (typeIndex < 0)
                    return;
                var rng = new Random(key);
                var side = (key & 2u) != 0 ? 1f : -1f;
                var normal = new float2(-tangent.y, tangent.x) * side;
                var radius = type.Radius;
                var site = at + normal * (road.Width * 0.5f + Verge + radius * 0.75f);

                // Clear of settlements, other roadside landmarks, landmarks and roads (but its own road's spur end).
                foreach (var s in settlements)
                    if (math.distance(site, s.Centre) < s.Radius + radius + 20f)
                        return;
                foreach (var p in placed)
                    if (math.distance(site, p.Position) < Apart)
                        return;
                if (src.Landmarks.IsCreated)
                {
                    ref var set = ref src.Landmarks.Value;
                    for (int l = 0; l < set.Instances.Length; l++)
                        if (math.distance(site, set.Instances[l].Position) < set.Types[set.Instances[l].Type].Radius * set.Instances[l].Scale + radius + 15f)
                            return;
                }
                for (int k = 0; k < roadStreets; k++)
                {
                    var st = streets[k];
                    var d = SegmentDistance(site, st.A.xz, st.B.xz);
                    if (d < radius * 0.75f + st.Width * 0.5f + Verge - 0.5f)
                        return;
                }
                // Dry, gentle ground with no cave under it.
                var lowest = float.MaxValue;
                var highest = float.MinValue;
                for (int k = 0; k < 9; k++)
                {
                    var o = k == 0 ? float2.zero : new float2(math.cos(k * math.PI / 4f), math.sin(k * math.PI / 4f)) * radius * 0.8f;
                    var g = src.SampleGround(site + o);
                    if (g.HasWater && g.WaterSurface > g.Height - 0.5f || g.River > 0.02f || src.IsCaveHole(site + o))
                        return;
                    lowest = math.min(lowest, g.Height);
                    highest = math.max(highest, g.Height);
                }
                if (highest - lowest > type.MaxRelief || lowest < src.World.SeaLevel + 1.5f)
                    return;

                placed.Add(new LandmarkInstance
                {
                    Position = site,
                    // Its +X towards the road.
                    Facing = -normal,
                    Scale = 1f,
                    Type = typeIndex,
                    BaseHeight = src.SampleHeight(site),
                    Seed = rng.NextFloat(-100f, 100f),
                    Key = key
                });
                // A short spur from the road to its edge, narrower than the road.
                forks.Add(at);
                var end = site - normal * radius * 0.45f;
                var width = road.Width * 0.7f;
                var from = new float3(at.x, src.SampleHeight(at), at.y);
                var to = new float3(end.x, src.SampleHeight(end), end.y);
                var reach = width * 0.5f + SettlementSampling.StreetBlend;
                streets.Add(new SettlementStreet
                {
                    A = from,
                    B = to,
                    Width = width,
                    Settlement = road.Settlement,
                    Road = 1,
                    Min = math.min(from.xz, to.xz) - reach,
                    Max = math.max(from.xz, to.xz) + reach
                });
                // A signpost at the fork, on the spur's side a few paces along, facing the road (the road's own
                // settlement's signpost); none within TownClear of a settlement's edge (F-43).
                var town = settlements[road.Settlement];
                var layout = layouts[town.Layout];
                var nearTown = false;
                foreach (var s in settlements)
                    nearTown |= math.distance(at, s.Centre) < s.Radius + TownClear;
                if (layout.Signpost != null && !nearTown)
                {
                    var post = at + normal * (road.Width * 0.5f + 1.2f) + tangent * 3f;
                    plots.Add(new SettlementPlot
                    {
                        Centre = post,
                        Facing = -normal,
                        Half = new float2(0.4f, 0.4f),
                        Pad = src.SampleHeight(post),
                        Role = PlotRole.Signpost,
                        Building = firstBuilding[town.Layout] + layout.Buildings.Count + 1,   // the signpost slot
                        Settlement = road.Settlement,
                        Min = post - 1f,
                        Max = post + 1f
                    });
                }
            }
        }

        static float SegmentDistance(float2 p, float2 a, float2 b)
        {
            var ab = b - a;
            var t = math.saturate(math.dot(p - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
            return math.distance(p, a + ab * t);
        }
    }
}
