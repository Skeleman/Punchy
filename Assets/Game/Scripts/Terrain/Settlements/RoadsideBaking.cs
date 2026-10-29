using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Landmarks beside the roads (a lone inn, a wayside shrine, a monument, a campsite, a ruin): once the roads between
    /// settlements are routed, every RoadsideSpacing metres along each one is a candidate spot, seeded by where it is, and
    /// each roadside type rolls for it by its chance per kilometre (the lowest roll against its chance wins, so adding a
    /// type only takes the spots it wins). A spot that wins stands off one side of the road, facing it, with a short spur
    /// to it, where the ground is dry, gentle and clear of settlements, other roads and landmarks. The new landmarks are
    /// appended to the landmark set afterwards (LandmarkBaking.Append), so nothing already placed moves.
    /// </summary>
    public static class RoadsideBaking
    {
        const float RoadsideSpacing = 120f;
        /// <summary>Roadside landmarks keep at least this far apart (m).</summary>
        const float Apart = 260f;
        /// <summary>Clear ground between a landmark's edge and the road beside it (m).</summary>
        const float Verge = 3f;

        /// <summary>A road's centreline, its width and the settlement it belongs to (RoadBaking).</summary>
        public struct RoadPath
        {
            public List<float2> Points;
            public float Width;
            public int Settlement;
        }

        /// <summary>
        /// The roadside landmarks along <paramref name="roads"/>, with their spurs added to <paramref name="streets"/>.
        /// </summary>
        public static List<LandmarkInstance> Place(in TerrainSource source, IReadOnlyList<LandmarkType> types, IReadOnlyList<RoadsideLandmark> roadside,
                                                   List<RoadPath> roads, List<SettlementData> settlements, List<SettlementStreet> streets)
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
            return placed;

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
