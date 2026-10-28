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
        /// <summary>Wall noise strength and frequency multipliers (the style's Roughness and Grain), its Ribs and RibSpacing.</summary>
        public float4 Wall;
        /// <summary>The style's Jaggedness, FloorFlatness, FloorSteps and Blockiness.</summary>
        public float4 Rock;
        public int FirstPool, PoolCount;
        public int FirstStream, StreamCount;
    }

    /// <summary>
    /// An underground stream: a chain of points along a passage floor, each with its water surface height (never
    /// rising along the flow) and channel width. A channel is carved under it, and CaveSystem draws it with the
    /// terrain's water material, flowing from the first point to the last.
    /// </summary>
    public struct CaveStream
    {
        public int FirstPoint, PointCount;
        /// <summary>Box round the channel, to skip it cheaply when sampling.</summary>
        public float2 Min, Max;
    }

    /// <summary>
    /// A plant on a cave floor (a glowing mushroom, a crystal, a mossy shrub): placed at bake time from the seed, and
    /// drawn by CaveSystem with the flora system's meshes and material.
    /// </summary>
    public struct CavePlant
    {
        public float3 Position;
        public float Yaw, Scale;
        /// <summary>Index into CaveFloraTable's species.</summary>
        public int Species;
        /// <summary>Which of the species' variants, and a per-plant random for the flora shader.</summary>
        public int Variant;
        public float Random;
    }

    public struct CaveStreamPoint
    {
        /// <summary>The middle of the water surface.</summary>
        public float3 Position;
        public float Width, Depth;
    }

    /// <summary>
    /// A pool sunk into a chamber floor: a basin carved below the floor, filled to Level (lava or water). Drawn with
    /// the terrain's own water material; lava pools glow onto the walls round them and keep the player out.
    /// </summary>
    public struct CavePool
    {
        /// <summary>Centre of the surface (y = the surface level).</summary>
        public float3 Centre;
        public float Radius, Depth;
        /// <summary>1 = molten lava, 0 = water.</summary>
        public float Lava;
    }

    /// <summary>One capsule of a passage, between two nodes, with the box its influence reaches.</summary>
    public struct CaveSegment
    {
        public int A, B;
        public int Cave;
        public float3 Min, Max;
    }

    /// <summary>
    /// A stalactite, stalagmite or column: a tapered rod of rock standing in a chamber's air, rooted in the floor or
    /// ceiling. It is part of the rock, so it's drawn with the walls (in the cave's own style), collided with and stood
    /// on like them: no meshes or draws of its own.
    /// </summary>
    public struct CaveSpeleothem
    {
        /// <summary>The root (inside the floor or ceiling) and the tip.</summary>
        public float3 Root, Tip;
        public float RootRadius, TipRadius;
        /// <summary>World-space box around it (with a margin).</summary>
        public float3 Min, Max;
    }

    /// <summary>
    /// Every cave: networks of nodes joined by segments, whose swept, merged spheres are the cave's air. A coarse
    /// column grid lists the segments reaching each cell (grouped by cave), so a sample only looks at the few
    /// passages nearby however many caves the world has. Speleothems have their own lists on the same grid.
    /// </summary>
    public struct CaveSetBlob
    {
        public BlobArray<CaveSpeleothem> Speleothems;
        public BlobArray<int2> SpeleothemCells;   // (first item, count), same grid as Cells
        public BlobArray<int> SpeleothemItems;
        public BlobArray<CaveData> Caves;
        public BlobArray<CaveNode> Nodes;
        /// <summary>Per node (same order as Nodes).</summary>
        public BlobArray<CaveNodeKind> Kinds;
        public BlobArray<CaveSegment> Segments;
        public BlobArray<CavePool> Pools;
        public BlobArray<CaveStream> Streams;
        public BlobArray<CaveStreamPoint> StreamPoints;
        public BlobArray<CavePlant> Plants;
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
        /// <summary>How softly surface air and cave air merge at a mouth (m): rock thinner than about half this opens.
        /// Shared by the walls (CaveSystem.FieldJob) and the terrain's cut (TerrainSource.MouthCut), so they agree.</summary>
        public const float MouthBlend = 1.2f;
        /// <summary>Most the wall noise moves a wall at Roughness 1 (both octaves at full strength).</summary>
        public const float NoiseReach = 0.75f;
        /// <summary>Most the ceiling's ridges, fractures and blocks move it at Jaggedness 1.</summary>
        public const float JaggedReach = 1.3f;
        /// <summary>Height of one floor ledge (m).</summary>
        const float StepHeight = 0.35f;
        /// <summary>Distances whose wall noise is skipped are kept at least this far from zero (beyond the 1.2 m wall cut-off).</summary>
        const float SkipBeyond = 1.5f;

        /// <summary>Most a cave's wall and ceiling noise can move its walls.</summary>
        public static float WallReach(in CaveData c) => NoiseReach * c.Wall.x + JaggedReach * c.Rock.x + 0.45f * c.Wall.z;

        /// <summary>
        /// How far beyond a segment's radius its box reaches, before wall noise. Past the blend and the noise, the
        /// distance is exact for a few metres more, so terrain vertices near an entrance still interpolate the
        /// cut-out edge correctly.
        /// </summary>
        public const float SegmentMargin = Blend + 4f;

        /// <summary>
        /// Distance to one capsule of a passage with the cave's width and height, cut off at its floor. Stretching
        /// makes the field a little off true distance; scaling by the smaller stretch keeps it from overstating it, so
        /// nothing that steps by distance overshoots a wall. Out: the floor's height and the capsule's vertical radius
        /// here (for the noise's floor-to-ceiling fade).
        /// </summary>
        static float Capsule(in CaveNode a, in CaveNode b, float3 p, in CaveData c, float floorShape, float channel, out float floorY, out float height)
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
            height = r * stretch;
            // The floor: halfway down at full flatness, sinking below the capsule's bottom (no effect) as flatness goes.
            floorY = centre.y - height * (0.45f + (1f - flat) * 0.65f) + floorShape;
            // A stream's channel cuts the floor down to its own bed wherever it runs.
            floorY = math.min(floorY, channel);
            return math.max(d, floorY - p.y);
        }

        /// <summary>
        /// The bed of a cave's streams at a spot (absolute height), or a large number away from them: a rounded channel
        /// under each stretch of each stream, Depth below its surface in the middle, rising past the floor at its banks.
        /// </summary>
        static float StreamBed(ref CaveSetBlob set, in CaveData c, float2 xz)
        {
            var bed = 1e9f;
            for (int s = c.FirstStream; s < c.FirstStream + c.StreamCount; s++)
            {
                ref var stream = ref set.Streams[s];
                if (math.any(xz < stream.Min) || math.any(xz > stream.Max))
                    continue;
                for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount - 1; i++)
                {
                    ref var a = ref set.StreamPoints[i];
                    ref var b = ref set.StreamPoints[i + 1];
                    var ab = b.Position.xz - a.Position.xz;
                    var t = math.saturate(math.dot(xz - a.Position.xz, ab) / math.max(math.lengthsq(ab), 1e-6f));
                    var half = math.lerp(a.Width, b.Width, t) * 0.5f;
                    var across = math.distance(xz, a.Position.xz + ab * t);
                    if (across >= half * 2f)
                        continue;
                    var surface = math.lerp(a.Position.y, b.Position.y, t);
                    // The bank rises smoothly from the bed to well above the surface (past the floor), so the cut has
                    // no step at its edge.
                    var depth = math.lerp(a.Depth, b.Depth, t);
                    depth -= (depth + 1.2f) * math.smoothstep(half * 0.5f, half * 2f, across);
                    bed = math.min(bed, surface - depth + 0.05f);
                }
            }
            return bed;
        }

        /// <summary>
        /// A cave's floor height offset at a spot: gentle undulation plus ledges with short ramps between them, sunk
        /// into a basin under each of its pools.
        /// </summary>
        static float FloorShape(ref CaveSetBlob set, in CaveData c, float2 xz)
        {
            var s = c.Seed;
            var shape = noise.snoise(xz * 0.07f + s) * 0.18f;
            if (c.Rock.z > 0f)
            {
                var v = (noise.snoise(xz * 0.045f - s) * 0.5f + 0.5f) * 4f * c.Rock.z;
                var step = math.floor(v);
                shape += (step + math.smoothstep(0.4f, 0.6f, v - step) - 2f * c.Rock.z) * StepHeight;
            }
            for (int i = c.FirstPool; i < c.FirstPool + c.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                var d = math.distance(xz, pool.Centre.xz);
                if (d < pool.Radius * 1.2f)
                    shape -= pool.Depth * math.smoothstep(pool.Radius * 1.2f, pool.Radius * 0.7f, d);
            }
            return shape;
        }

        /// <summary>
        /// The walls' noise: gentle near the floor, and towards the ceiling ridges (low and broad), fractures (sharp
        /// creases) and blocky collapse hollows, by the cave's Jaggedness and Blockiness. Ridges and creases push rock in
        /// and hollow it out, so the ceiling gets both hanging ribs and overhung pockets; blocks only hollow (a
        /// collapse leaves a hole, never a slab hanging in the air).
        /// </summary>
        static float WallNoise(in CaveData c, float3 p, float floorY, float height)
        {
            var s = c.Seed;
            var g = c.Wall.y;
            // 0 at the floor (its own floor, ledges and all, so a ledge doesn't sprout ceiling noise), 1 at the top.
            var up = math.saturate((p.y - floorY) / math.max(height * 1.45f, 0.1f));
            var n = (noise.snoise(p * 0.22f * g + s) * 0.55f + noise.snoise(p * 0.6f * g - s) * 0.2f) * c.Wall.x
                    * math.lerp(0.3f, 1f, math.smoothstep(0f, 0.35f, up));
            if (c.Rock.x > 0f && up > 0.2f)
            {
                var ridge = 1f - math.abs(noise.snoise(p * 0.12f * g + s * 0.7f));
                var crease = math.abs(noise.snoise(p * 0.5f * g - s * 1.3f));
                var jag = (ridge * ridge - 0.45f) * 0.9f + (crease - 0.5f) * 0.35f;
                var blocks = c.Rock.w > 0f ? math.min(0f, noise.snoise(math.floor(p * (1f / 1.7f)) * 0.37f + s)) * 0.6f * c.Rock.w * math.smoothstep(0.45f, 1f, up) : 0f;
                n += (jag + blocks) * c.Rock.x * math.smoothstep(0.2f, 0.75f, up);
            }
            if (c.Wall.z > 0f && up > 0.06f)
            {
                // Ribs (melt-scalloped ice): sharp ridges RibSpacing apart, across a slant set per cave and wavering
                // with noise, with scooped hollows between (about 0.45 m deep at full Ribs). Off the floor, so FloorUnder's march
                // isn't affected.
                var across = math.normalize(new float3(math.sin(s * 3.1f), 0.35f, math.cos(s * 3.1f)));
                var t = math.dot(p, across) / c.Wall.w + noise.snoise(p * 0.15f + s) * 0.6f;
                var rib = 1f - math.abs(math.sin(t * math.PI));
                n += (rib * rib - 0.4f) * 0.75f * c.Wall.z * math.smoothstep(0.06f, 0.3f, up);
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
            float floorShape = 0f, channel = 1e9f, nearest = 1e9f, floorY = 0f, height = 1f;
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var seg = ref set.Segments[set.Items[k]];
                if (math.any(p < seg.Min) || math.any(p > seg.Max))
                    continue;
                if (seg.Cave != current)
                {
                    // Items are grouped by cave, so the previous cave is done.
                    Settle(ref set, current, d, p, floorY, height, ref best, ref cave);
                    current = seg.Cave;
                    d = 1e9f;
                    nearest = 1e9f;
                    floorShape = FloorShape(ref set, set.Caves[current], p.xz);
                    channel = set.Caves[current].StreamCount > 0 ? StreamBed(ref set, set.Caves[current], p.xz) : 1e9f;
                }
                var ds = Capsule(set.Nodes[seg.A], set.Nodes[seg.B], p, set.Caves[current], floorShape, channel, out var fy, out var h);
                // The noise fades from floor to ceiling by the nearest passage's own floor and ceiling.
                if (ds < nearest)
                {
                    nearest = ds;
                    floorY = fy;
                    height = h;
                }
                d = SmoothMin(d, ds, Blend);
            }
            Settle(ref set, current, d, p, floorY, height, ref best, ref cave);
            if (best < SpeleothemReach && set.SpeleothemCells.Length > 0)
                best = Speleothems(ref set, p, best);
            return best;
        }

        /// <summary>Only near the air do speleothems matter: they turn air into rock, never rock into air.</summary>
        const float SpeleothemReach = 2f;
        /// <summary>How softly a speleothem's root blends into the floor or ceiling (m).</summary>
        const float RootFillet = 0.5f;

        /// <summary>Takes the speleothems near a point out of the air: a smooth maximum with each one's rock.</summary>
        static float Speleothems(ref CaveSetBlob set, float3 p, float air)
        {
            var c = (int2)math.floor((p.xz - set.GridOrigin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return air;
            var range = set.SpeleothemCells[c.y * set.GridSize.x + c.x];
            for (int k = range.x; k < range.x + range.y; k++)
            {
                ref var s = ref set.Speleothems[set.SpeleothemItems[k]];
                if (math.any(p < s.Min) || math.any(p > s.Max))
                    continue;
                air = -SmoothMin(-air, Rod(s, p), RootFillet);
            }
            return air;
        }

        /// <summary>Distance to a tapered rod (a cone with rounded ends), slightly understated so stepping never overshoots.</summary>
        internal static float Rod(in CaveSpeleothem s, float3 p)
        {
            var ab = s.Tip - s.Root;
            var t = math.saturate(math.dot(p - s.Root, ab) / math.max(math.lengthsq(ab), 1e-6f));
            return (math.distance(p, s.Root + ab * t) - math.lerp(s.RootRadius, s.TipRadius, t)) * 0.9f;
        }

        /// <summary>Adds a finished cave's wall noise and keeps it if it's the nearest so far.</summary>
        static void Settle(ref CaveSetBlob set, int current, float d, float3 p, float floorY, float height, ref float best, ref int cave)
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
                     : d + WallNoise(c, p, floorY, height);
            if (full < best)
            {
                best = full;
                cave = current;
            }
        }

        /// <summary>
        /// Whether a point is over a cave's lava pool and low enough to step into it (up to a metre above the surface).
        /// Pools are few, so this just checks them all.
        /// </summary>
        public static bool OverLava(ref CaveSetBlob set, float3 p)
        {
            for (int i = 0; i < set.Pools.Length; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f && math.distancesq(p.xz, pool.Centre.xz) < pool.Radius * pool.Radius &&
                    p.y < pool.Centre.y + 1f && p.y > pool.Centre.y - pool.Depth - 1f)
                    return true;
            }
            return false;
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

        internal static float SmoothMin(float a, float b, float k)
        {
            var h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
