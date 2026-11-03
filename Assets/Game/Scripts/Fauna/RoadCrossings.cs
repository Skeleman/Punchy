using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Where the roads cross rivers (A-31), read from what the settlement bake already made: each run of bridge spans
    /// is a bridge, and a road stretch through shallow water with no bridge near is a ford. Each crossing gets a bank
    /// spot for a fisher: a few metres off the road, beside the water, on flat dry ground, outside any settlement,
    /// facing the water; and a seeded roll (about 1 in 3) for whether a fisher is there. Everything comes from the bake
    /// and fixed hashes of the positions, so every machine finds the same list (seed-derived state).
    /// </summary>
    public static class RoadCrossings
    {
        public struct Crossing
        {
            /// <summary>The middle of the crossing on the road, and the road's direction there.</summary>
            public float2 Centre, Along;
            public bool Bridge;
            /// <summary>Whether a bank spot was found, the spot and the way to the water from it.</summary>
            public bool HasSpot;
            public float2 Spot, Face;
            /// <summary>The road on dry ground at the end the spot is by (for framing, and for a lone fisher leaving).</summary>
            public float2 Bank, Outward;
            /// <summary>The seeded roll: a fisher stands here.</summary>
            public bool Fisher;
            /// <summary>The seeded hash behind the roll (lower scores better), and whether this crossing got its fisher
            /// as the island's best score because no crossing rolled one (A-32).</summary>
            public uint Roll;
            public bool Fallback;
        }

        /// <summary>The crossings of the island last found (empty before the first), and its key.</summary>
        public static readonly List<Crossing> List = new List<Crossing>();
        static float4 key = new float4(-1f);
        /// <summary>Counts up each time the list is found again (whoever asked first), for users to notice.</summary>
        public static int Version { get; private set; }

        /// <summary>Spans of one bridge are at most this far apart; crossings nearer than MergeRange are one.</summary>
        const float SpanGap = 14f;
        const float MergeRange = 25f;

        /// <summary>Finds the crossings again if the island changed (the v1 bake can come first, then v2's). True when it did.</summary>
        public static bool Refresh(in TerrainSource source)
        {
            if (!source.Settlements.IsCreated)
                return false;
            ref var set = ref source.Settlements.Value;
            var k = new float4(set.Settlements.Length, set.Plots.Length, set.Settlements.Length > 0 ? set.Settlements[0].Centre : float2.zero);
            if (math.all(k == key))
                return false;
            key = k;
            Version++;
            List.Clear();
            // Bridges: runs of bridge plots one after another (one road's spans, in order).
            for (int i = 0; i < set.Plots.Length; i++)
            {
                if (set.Plots[i].Role != PlotRole.Bridge)
                    continue;
                var first = set.Plots[i];
                var last = first;
                while (i + 1 < set.Plots.Length && set.Plots[i + 1].Role == PlotRole.Bridge &&
                       math.distance(set.Plots[i + 1].Centre, last.Centre) < SpanGap && math.dot(set.Plots[i + 1].Facing, last.Facing) > 0.9f)
                    last = set.Plots[++i];
                Add(source, (first.Centre + last.Centre) * 0.5f, math.normalizesafe(first.Facing, new float2(1f, 0f)), true);
            }
            // Fords: the bake leaves a road's stretches out where it runs through water (RoadBaking.Stretches), so a ford
            // is a gap between one road stretch and the next with water in it, away from the bridges and the towns.
            for (int s = 0; s + 1 < set.Streets.Length; s++)
            {
                var a = set.Streets[s];
                var b = set.Streets[s + 1];
                if (a.Road == 0 || b.Road == 0)
                    continue;
                var gap = b.A.xz - a.B.xz;
                var length = math.length(gap);
                if (length < 1f || length > 60f)
                    continue;
                var n = math.max(1, (int)math.ceil(length / 1.5f));
                for (int j = 1; j < n; j++)
                {
                    var p = a.B.xz + gap * (j / (float)n);
                    if (Depth(source, p) < 0.05f || InTown(ref set, p, 20f))
                        continue;
                    Add(source, a.B.xz + gap * 0.5f, gap / length, false);
                    break;
                }
            }
            // At least one fisher per island (A-32): if no crossing with a bank spot rolled one, the one with the best
            // (lowest) seeded score gets it. Still only from the bake and the hashes, so the same on every machine.
            int best = -1;
            for (int i = 0; i < List.Count; i++)
            {
                if (!List[i].HasSpot)
                    continue;
                if (List[i].Fisher)
                {
                    best = -1;
                    break;
                }
                if (best < 0 || List[i].Roll < List[best].Roll)
                    best = i;
            }
            if (best >= 0)
            {
                var c = List[best];
                c.Fisher = c.Fallback = true;
                List[best] = c;
            }
            return true;
        }

        static void Add(in TerrainSource source, float2 centre, float2 along, bool bridge)
        {
            foreach (var c in List)
                if (math.distance(c.Centre, centre) < MergeRange)
                    return;
            var crossing = new Crossing { Centre = centre, Along = along, Bridge = bridge };
            // The roll and which bank is tried first: fixed hashes of the position (to the metre).
            var cell = (int2)math.floor(centre);
            var h = BiomeSampling.Scramble((uint)cell.x * 0x9E3779B1u ^ (uint)cell.y * 0x85EBCA77u ^ 0xF15E4u);
            crossing.Fisher = h % 3u == 0u;
            crossing.Roll = h;
            var firstSide = (h >> 8) % 2u == 0u ? 1f : -1f;
            for (int k = 0; k < 2 && !crossing.HasSpot; k++)
            {
                var dir = along * (k == 0 ? firstSide : -firstSide);
                if (!DryEnd(source, centre, dir, out var bank))
                    continue;
                if (BankSpot(source, bank, dir, out var spot, out var face))
                {
                    crossing.HasSpot = true;
                    crossing.Spot = spot;
                    crossing.Face = face;
                    crossing.Bank = bank;
                    crossing.Outward = dir;
                }
            }
            List.Add(crossing);
        }

        static float Depth(in TerrainSource source, float2 p)
        {
            var g = source.SampleGround(p);
            return g.HasWater ? g.WaterSurface - g.Height : -1f;
        }

        /// <summary>Along the road from the middle until the ground is dry (and stays so for 2 m), up to 70 m.</summary>
        static bool DryEnd(in TerrainSource source, float2 centre, float2 dir, out float2 bank)
        {
            for (var d = 1f; d <= 70f; d += 1f)
            {
                bank = centre + dir * d;
                if (Depth(source, bank) < 0f && Depth(source, bank + dir * 2f) < 0f)
                    return true;
            }
            bank = centre;
            return false;
        }

        /// <summary>
        /// A fisher's spot by one bank: to either side of the road, 5 to 9 m off it and up to 5 m back from the water,
        /// the first that is dry, flat (under 0.5 m over 2 m), outside settlements and within 3.5 m of water at least
        /// 0.4 m deep (the way it faces).
        /// </summary>
        static bool BankSpot(in TerrainSource source, float2 bank, float2 outward, out float2 spot, out float2 face)
        {
            var across = new float2(-outward.y, outward.x);
            for (int side = 0; side < 2; side++)
                for (var off = 4f; off <= 10f; off += 2f)
                    for (var back = -4f; back <= 6f; back += 1f)
                    {
                        var p = bank + across * (side == 0 ? off : -off) + outward * back;
                        if (Depth(source, p) > -0.05f)
                            continue;
                        if (source.Settlements.IsCreated && InTown(ref source.Settlements.Value, p, 5f))
                            continue;
                        var h0 = source.SampleHeight(p + new float2(1f, 0f));
                        var h1 = source.SampleHeight(p - new float2(1f, 0f));
                        var h2 = source.SampleHeight(p + new float2(0f, 1f));
                        var h3 = source.SampleHeight(p - new float2(0f, 1f));
                        if (math.max(math.max(h0, h1), math.max(h2, h3)) - math.min(math.min(h0, h1), math.min(h2, h3)) > 0.8f)
                            continue;
                        // The nearest water deep enough to cast in round it (16 ways, out to 5 m).
                        for (var r = 0.75f; r <= 5f; r += 0.5f)
                            for (int a = 0; a < 16; a++)
                            {
                                var ang = a * math.PI / 8f;
                                var d = new float2(math.sin(ang), math.cos(ang));
                                if (Depth(source, p + d * r) < 0.2f)
                                    continue;
                                spot = p;
                                face = d;
                                return true;
                            }
                    }
            spot = face = float2.zero;
            return false;
        }

        static bool InTown(ref SettlementSetBlob set, float2 at, float margin)
        {
            for (int i = 0; i < set.Settlements.Length; i++)
                if (math.distance(set.Settlements[i].Centre, at) < set.Settlements[i].Radius + margin)
                    return true;
            return false;
        }
    }
}
