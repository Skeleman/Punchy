using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A point on a passage's centre line and how wide the passage is there.</summary>
    public struct CaveNode
    {
        public float3 Position;
        public float Radius;
    }

    /// <summary>What a node is for, so props and anchors can tell a chamber from a passage.</summary>
    public enum CaveNodeKind : byte
    {
        Passage,
        /// <summary>A room: the end of the main passage, one along it, or the end of a side passage.</summary>
        Chamber,
        /// <summary>Part of a shaft rising to the surface.</summary>
        Shaft
    }

    /// <summary>One cave network: everything reached from one landmark's mouth.</summary>
    public struct CaveData
    {
        public int FirstNode, NodeCount;
        public int FirstSegment, SegmentCount;
        /// <summary>World-space box around all of the cave's air (with a margin).</summary>
        public float3 Min, Max;
        /// <summary>The middle of the entrance, where daylight comes in.</summary>
        public float3 Mouth;
        public float Seed;
        /// <summary>Its CaveStyle's slot in CaveStyleTable order, plus one (0 = no style).</summary>
        public int Style;
        /// <summary>Passage width and height multipliers (the style's Width, and Width × Height).</summary>
        public float2 Profile;
        /// <summary>Wall noise strength and frequency multipliers (the style's Roughness and Grain).</summary>
        public float2 Wall;
    }

    /// <summary>One capsule of a passage, between two nodes, with the box its influence reaches.</summary>
    public struct CaveSegment
    {
        public int A, B;
        public int Cave;
        public float3 Min, Max;
    }

    /// <summary>
    /// Every cave: networks of nodes joined by segments, whose swept, merged spheres are the cave's air. A coarse
    /// column grid lists the segments reaching each cell (grouped by cave), so a sample only looks at the few
    /// passages nearby however many caves the world has.
    /// </summary>
    public struct CaveSetBlob
    {
        public BlobArray<CaveData> Caves;
        public BlobArray<CaveNode> Nodes;
        /// <summary>Per node (same order as Nodes).</summary>
        public BlobArray<CaveNodeKind> Kinds;
        public BlobArray<CaveSegment> Segments;
        public float2 GridOrigin;
        public float CellSize;
        public int2 GridSize;
        public BlobArray<int2> Cells;   // (first item, count)
        public BlobArray<int> Items;    // segment indices, grouped by cave
    }

    /// <summary>
    /// The shape of caves as a signed distance: negative inside a cave's air, positive in rock. A cave is a network
    /// of capsules, blended smoothly where they meet, with a flatter floor than ceiling (easier to walk on), shaped by
    /// its style (wide, tall) and with noise on the walls. Rock is everywhere that is below the terrain surface and
    /// outside every cave.
    /// </summary>
    public static class CaveSampling
    {
        public const float Blend = 1.5f;
        /// <summary>Most the wall noise moves a wall at Roughness 1 (both octaves at full strength).</summary>
        public const float NoiseReach = 0.75f;

        /// <summary>
        /// How far beyond a segment's radius its box reaches, before wall noise. Past the blend and the noise, the
        /// distance is exact for a few metres more, so terrain vertices near an entrance still interpolate the
        /// cut-out edge correctly.
        /// </summary>
        public const float SegmentMargin = Blend + 4f;

        /// <summary>
        /// Distance to one capsule of a passage, with a flatter floor than ceiling and the cave's width and height.
        /// Stretching makes the field a little off true distance; scaling by the smaller stretch keeps it from
        /// overstating it, so nothing that steps by distance overshoots a wall.
        /// </summary>
        static float Capsule(in CaveNode a, in CaveNode b, float3 p, float2 profile)
        {
            var ab = b.Position - a.Position;
            var t = math.saturate(math.dot(p - a.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
            var q = p - (a.Position + ab * t);
            var stretch = profile.y / profile.x;
            q.y /= stretch;
            if (q.y < 0f)
                q.y *= 1.8f;   // flatter floor
            var r = math.lerp(a.Radius, b.Radius, t) * profile.x;
            return (math.length(q) - r) * math.min(1f, stretch);
        }

        static float WallNoise(in CaveData c, float3 p)
        {
            var s = c.Seed;
            var g = c.Wall.y;
            return (noise.snoise(p * 0.22f * g + s) * 0.55f + noise.snoise(p * 0.6f * g - s) * 0.2f) * c.Wall.x;
        }

        public static float Distance(ref CaveSetBlob set, int cave, float3 p)
        {
            ref var c = ref set.Caves[cave];
            var d = 1e9f;
            for (int i = c.FirstSegment; i < c.FirstSegment + c.SegmentCount; i++)
            {
                ref var seg = ref set.Segments[i];
                d = SmoothMin(d, Capsule(set.Nodes[seg.A], set.Nodes[seg.B], p, c.Profile), Blend);
            }
            return d + WallNoise(c, p);
        }

        /// <summary>The range of grid items (indices into Items) for the column containing a point.</summary>
        static int2 CellRange(ref CaveSetBlob set, float2 xz)
        {
            if (set.Cells.Length == 0)
                return int2.zero;
            var c = (int2)math.floor((xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return int2.zero;
            return set.Cells[c.y * set.GridSize.x + c.x];
        }

        /// <summary>
        /// Distance to the nearest cave's air at a point (a large number away from every cave). Only the segments
        /// whose boxes hold the point count: the ones left out are further than the blend from it, where the smooth
        /// minimum is an exact minimum, so the result matches a sweep over every segment wherever it is near zero.
        /// </summary>
        public static float Distance(ref CaveSetBlob set, float3 p, out int cave)
        {
            cave = -1;
            var best = 1e9f;
            var range = CellRange(ref set, p.xz);
            var current = -1;
            var d = 1e9f;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.any(p < seg.Min) || math.any(p > seg.Max))
                    continue;
                if (seg.Cave != current)
                {
                    // Items are grouped by cave, so the previous cave is done.
                    Settle(ref set, current, d, p, ref best, ref cave);
                    current = seg.Cave;
                    d = 1e9f;
                }
                d = SmoothMin(d, Capsule(set.Nodes[seg.A], set.Nodes[seg.B], p, set.Caves[current].Profile), Blend);
            }
            Settle(ref set, current, d, p, ref best, ref cave);
            return best;
        }

        /// <summary>Adds a finished cave's wall noise and keeps it if it's the nearest so far.</summary>
        static void Settle(ref CaveSetBlob set, int current, float d, float3 p, ref float best, ref int cave)
        {
            if (current < 0)
                return;
            var full = d + WallNoise(set.Caves[current], p);
            if (full < best)
            {
                best = full;
                cave = current;
            }
        }

        /// <summary>Whether a point's column passes through any cave segment's box (a cheap first test).</summary>
        public static bool NearColumn(ref CaveSetBlob set, float2 xz)
        {
            var range = CellRange(ref set, xz);
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.all(xz >= seg.Min.xz) && math.all(xz <= seg.Max.xz))
                    return true;
            }
            return false;
        }

        static float SmoothMin(float a, float b, float k)
        {
            var h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
