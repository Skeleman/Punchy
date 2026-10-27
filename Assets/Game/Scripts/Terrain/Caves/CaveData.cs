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
        /// <summary>The style's Jaggedness, FloorFlatness, FloorSteps and Blockiness.</summary>
        public float4 Rock;
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
    /// of capsules, blended smoothly where they meet and shaped by its style (wide, tall). Each passage's air stops at
    /// a floor: a level plane under its centre with gentle undulation and stepped ledges, so floors are flat enough to
    /// walk. Above it the walls get noise, and towards the ceiling ridged, fractured and blocky noise (jagged rock,
    /// overhangs, collapse hollows) that fades out before the floor. Rock is everywhere that is below the terrain
    /// surface and outside every cave.
    /// </summary>
    public static class CaveSampling
    {
        public const float Blend = 1.5f;
        /// <summary>Most the wall noise moves a wall at Roughness 1 (both octaves at full strength).</summary>
        public const float NoiseReach = 0.75f;
        /// <summary>Most the ceiling's ridges, fractures and blocks move it at Jaggedness 1.</summary>
        public const float JaggedReach = 1.2f;
        /// <summary>Height of one floor ledge (m).</summary>
        const float StepHeight = 0.35f;
        /// <summary>Distances whose wall noise is skipped are kept at least this far from zero (beyond the 1.2 m wall cut-off).</summary>
        const float SkipBeyond = 1.5f;

        /// <summary>Most a cave's wall and ceiling noise can move its walls.</summary>
        public static float WallReach(in CaveData c) => NoiseReach * c.Wall.x + JaggedReach * c.Rock.x;

        /// <summary>
        /// How far beyond a segment's radius its box reaches, before wall noise. Past the blend and the noise, the
        /// distance is exact for a few metres more, so terrain vertices near an entrance still interpolate the
        /// cut-out edge correctly.
        /// </summary>
        public const float SegmentMargin = Blend + 4f;

        /// <summary>
        /// Distance to one capsule of a passage with the cave's width and height, cut off at its floor. Stretching
        /// makes the field a little off true distance; scaling by the smaller stretch keeps it from overstating it, so
        /// nothing that steps by distance overshoots a wall. Out: the capsule's centre height and vertical radius here
        /// (for the noise's floor-to-ceiling fade).
        /// </summary>
        static float Capsule(in CaveNode a, in CaveNode b, float3 p, in CaveData c, float floorShape, out float centreY, out float height)
        {
            var ab = b.Position - a.Position;
            var t = math.saturate(math.dot(p - a.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
            var centre = a.Position + ab * t;
            var q = p - centre;
            var stretch = c.Profile.y / c.Profile.x;
            var flat = c.Rock.y;
            q.y /= stretch;
            if (q.y < 0f)
                q.y *= math.lerp(1.8f, 1f, flat);   // a flatter underside, until the floor takes over
            var r = math.lerp(a.Radius, b.Radius, t) * c.Profile.x;
            var d = (math.length(q) - r) * math.min(1f, stretch);
            centreY = centre.y;
            height = r * stretch;
            // The floor: halfway down at full flatness, sinking below the capsule's bottom (no effect) as flatness goes.
            var floor = centre.y - height * (0.45f + (1f - flat) * 0.65f) + floorShape;
            return math.max(d, floor - p.y);
        }

        /// <summary>A cave's floor height offset at a spot: gentle undulation plus ledges with short ramps between them.</summary>
        static float FloorShape(in CaveData c, float2 xz)
        {
            var s = c.Seed;
            var swell = noise.snoise(xz * 0.07f + s) * 0.18f;
            if (c.Rock.z <= 0f)
                return swell;
            var v = (noise.snoise(xz * 0.045f - s) * 0.5f + 0.5f) * 4f * c.Rock.z;
            var step = math.floor(v);
            return swell + (step + math.smoothstep(0.4f, 0.6f, v - step) - 2f * c.Rock.z) * StepHeight;
        }

        /// <summary>
        /// The walls' noise: gentle near the floor, and towards the ceiling ridges (low and broad), fractures (sharp
        /// creases) and blocky collapse hollows, by the cave's Jaggedness and Blockiness. Positive pushes rock in,
        /// negative hollows it out, so the ceiling gets both hanging ribs and overhung pockets.
        /// </summary>
        static float WallNoise(in CaveData c, float3 p, float centreY, float height)
        {
            var s = c.Seed;
            var g = c.Wall.y;
            // 0 at the floor, 1 at the top of the passage.
            var up = math.saturate((p.y - (centreY - height * 0.45f)) / math.max(height * 1.45f, 0.1f));
            var n = (noise.snoise(p * 0.22f * g + s) * 0.55f + noise.snoise(p * 0.6f * g - s) * 0.2f) * c.Wall.x
                    * math.lerp(0.3f, 1f, math.smoothstep(0f, 0.35f, up));
            if (c.Rock.x > 0f && up > 0.2f)
            {
                var ridge = 1f - math.abs(noise.snoise(p * 0.12f * g + s * 0.7f));
                var crease = math.abs(noise.snoise(p * 0.5f * g - s * 1.3f));
                var jag = (ridge * ridge - 0.45f) * 0.9f + (crease - 0.5f) * 0.35f;
                var blocks = c.Rock.w > 0f ? noise.snoise(math.floor(p * (1f / 1.7f)) * 0.37f + s) * 0.5f * c.Rock.w * math.smoothstep(0.45f, 1f, up) : 0f;
                n += (jag + blocks) * c.Rock.x * math.smoothstep(0.2f, 0.75f, up);
            }
            return n;
        }

        /// <summary>
        /// How much further than true distance a cave's field can reach along its stretch (1 for a round passage): it
        /// is scaled by the smaller stretch, so margins measured in it must grow by the larger.
        /// </summary>
        public static float Stretch(float2 profile)
        {
            var s = profile.y / profile.x;
            return math.max(1f, math.max(s, 1f / s));
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
            float floorShape = 0f, nearest = 1e9f, centreY = 0f, height = 1f;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.any(p < seg.Min) || math.any(p > seg.Max))
                    continue;
                if (seg.Cave != current)
                {
                    // Items are grouped by cave, so the previous cave is done.
                    Settle(ref set, current, d, p, centreY, height, ref best, ref cave);
                    current = seg.Cave;
                    d = 1e9f;
                    nearest = 1e9f;
                    floorShape = FloorShape(set.Caves[current], p.xz);
                }
                var ds = Capsule(set.Nodes[seg.A], set.Nodes[seg.B], p, set.Caves[current], floorShape, out var cy, out var h);
                // The noise fades from floor to ceiling by the nearest passage's own floor and ceiling.
                if (ds < nearest)
                {
                    nearest = ds;
                    centreY = cy;
                    height = h;
                }
                d = SmoothMin(d, ds, Blend);
            }
            Settle(ref set, current, d, p, centreY, height, ref best, ref cave);
            return best;
        }

        /// <summary>Adds a finished cave's wall noise and keeps it if it's the nearest so far.</summary>
        static void Settle(ref CaveSetBlob set, int current, float d, float3 p, float centreY, float height, ref float best, ref int cave)
        {
            if (current < 0)
                return;
            ref var c = ref set.Caves[current];
            // Well beyond the noise's reach of the wall, it can't decide rock or air: skip it, and step the distance
            // towards the wall by its reach so anything that steps by distance still never overshoots. The skipped
            // values stay at least SkipBeyond from zero, so everything near a wall (and the meshing's cut-off) is exact.
            var reach = WallReach(c);
            var full = d > reach + SkipBeyond ? d - reach
                     : d < -reach - SkipBeyond ? d + reach
                     : d + WallNoise(c, p, centreY, height);
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
