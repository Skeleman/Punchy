using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-172 row 5 (#262): side ways off long road tunnels, so a bore isn't one straight tube. Dead-end side passages,
    /// chambers (a bench against the wall, or left empty as a tunnel camp socket for folk to camp in) and branches into
    /// a natural cave nearby. Each is a split point on the road's line and a chain of smaller nodes out to one side, in
    /// the tunnel's own cave; the road itself keeps its width and head room. Mushrooms grow on their floors and roots
    /// hang where the rock overhead is thin.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Bores shorter than this (metres) get no side ways.</summary>
        const float TunnelSideBore = 60f;
        /// <summary>Rock kept over a side way's ceiling (metres), so it never breaks the hillside.</summary>
        const float TunnelSideCover = 4f;
        /// <summary>How far a branch may reach for a natural cave's node (metres).</summary>
        const float TunnelBranchReach = 150f;

        /// <summary>A side way: the road point after its split, the split, its nodes' floor points and radii, and what it is.</summary>
        struct TunnelSide
        {
            public int After;
            public float3 Split;
            public List<(float3 floor, float r, CaveNodeKind kind)> Chain;
            /// <summary>0 dead end, 1 chamber, 2 branch into a natural cave.</summary>
            public int Type;
        }

        /// <summary>W-172 row 5: what the side ways baked: dead ends, chambers, benches, camp sockets, branches, mushrooms, roots.</summary>
        static readonly int[] SideCounts = new int[7];

        /// <summary>
        /// A long bore's side ways. Their own roll, so the tunnel's other draws (and its niche) stay as they were. Each sits
        /// along the bore clear of the portals, the niche and each other, opening off a road segment away from the ends
        /// (so the first and last few nodes stay the portals'), and only where the hill keeps TunnelSideCover over it.
        /// </summary>
        static List<TunnelSide> Sides(in TerrainSource source, List<CaveStyle> styles, List<float3> points, float radius, uint key,
                                      float4 niche, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds)
        {
            var sides = new List<TunnelSide>();
            var bore = 0f;
            for (int k = 1; k < points.Count; k++)
                bore += math.distance(points[k - 1].xz, points[k].xz);
            if (bore < TunnelSideBore || points.Count < 6)
                return sides;
            var rng = new Random(math.hash(new uint2(key, 0x51DE5u)) | 1u);
            var wanted = 1 + (bore > 200f ? 1 : 0) + (bore > 400f ? 1 : 0);
            var taken = new List<float3>();
            if (niche.w > 0f)
                taken.Add(niche.xyz);
            var terrain = source;
            bool Covered(float3 floor, float r) => terrain.SampleHeight(floor.xz) - (floor.y + 1.2f * r) >= TunnelSideCover;
            for (int n = 0; n < wanted; n++)
            {
                if (rng.NextFloat() > 0.75f)
                    continue;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    var at = rng.NextFloat(30f, bore - 30f);
                    var after = -1;
                    var q = float3.zero;
                    var along = float2.zero;
                    for (int k = 1; k < points.Count; k++)
                    {
                        var length = math.distance(points[k - 1].xz, points[k].xz);
                        if (at > length)
                        {
                            at -= length;
                            continue;
                        }
                        if (k - 1 >= 2 && k <= points.Count - 3 && at >= 1.5f && at <= length - 1.5f)
                        {
                            after = k;
                            q = math.lerp(points[k - 1], points[k], at / length);
                            along = math.normalize((points[k] - points[k - 1]).xz);
                        }
                        break;
                    }
                    if (after < 0)
                        continue;
                    var clear = true;
                    foreach (var t in taken)
                        clear &= math.distance(t.xz, q.xz) >= 20f;
                    if (!clear)
                        continue;
                    var side = rng.NextBool() ? 1f : -1f;
                    var across = new float2(-along.y, along.x) * side;
                    var roll = rng.NextFloat();
                    // A branch is the rarest (natural caves seldom lie near a bore), so a tunnel's first side tries for
                    // one most of the time; the rest are mostly chambers and dead ends.
                    var type = roll < (sides.Count == 0 ? 0.7f : 0.15f) ? 2 : roll < 0.55f ? 1 : 0;
                    var chain = new List<(float3, float, CaveNodeKind)>();
                    if (type == 2 && sides.Count == 0 && BranchSpot(points, radius, caves, nodes, kinds, styles, Covered, taken, out var spot))
                    {
                        // The first side's branch starts from wherever along the bore a natural cave is nearest, as a
                        // random spot seldom has one in reach.
                        (after, q, along) = spot;
                        across = new float2(-along.y, along.x);
                    }
                    if (type == 2 && !Branch(styles, q, radius, caves, nodes, kinds, Covered, ref across, chain))
                        type = roll < 0.3f ? 1 : 0;   // no cave within reach: a chamber or a dead end instead
                    if (type == 0)
                    {
                        // A dead end: two or three bends, a little uphill, pinching in at its end.
                        var r = rng.NextFloat(1.8f, 2.1f);
                        var dir = across;
                        var p = q + new float3(dir.x, 0f, dir.y) * (radius + 2f);
                        var count = rng.NextInt(2, 4);
                        for (int j = 0; j < count; j++)
                        {
                            var rj = j == count - 1 ? r * 0.8f : r;
                            chain.Add((p, SideRadius(rj), CaveNodeKind.Passage));
                            var bend = math.radians(rng.NextFloat(-40f, 40f));
                            dir = math.mul(float2x2.Rotate(bend), dir);
                            if (math.dot(dir, across) < 0.3f)
                                dir = math.normalize(dir + across);
                            p += new float3(dir.x, rng.NextFloat(0f, 0.4f), dir.y) * rng.NextFloat(5f, 7f);
                        }
                    }
                    else if (type == 1)
                    {
                        // A chamber: a short way in, then a round room wider than the bore.
                        var big = radius * rng.NextFloat(1.3f, 1.5f);
                        var way = q + new float3(across.x, 0f, across.y) * (radius + 4f);
                        var middle = way + new float3(across.x, 0f, across.y) * (big + 1f);
                        chain.Add((way, 2f, CaveNodeKind.Passage));
                        chain.Add((middle, big, CaveNodeKind.Passage));
                    }
                    var ok = chain.Count > 0;
                    for (int j = 0; j < (type == 2 ? chain.Count - 1 : chain.Count); j++)
                        ok &= Covered(chain[j].Item1, chain[j].Item2);
                    if (ok && type == 1)
                    {
                        var (middle, big, _) = chain[chain.Count - 1];
                        for (int j = 0; j < 6 && ok; j++)
                        {
                            var a = j * math.PI / 3f;
                            ok &= Covered(middle + new float3(math.cos(a), 0f, math.sin(a)) * big, 1f);
                        }
                    }
                    if (!ok)
                        continue;
                    taken.Add(q);
                    sides.Add(new TunnelSide { After = after, Split = q, Chain = chain, Type = type });
                    if (type != 1)
                        SideCounts[type == 0 ? 0 : 4]++;
                    break;
                }
            }
            return sides;
        }

        /// <summary>The road point (a whole segment in from either end, clear of what is taken) with the shortest
        /// covered branch to a natural cave, if any.</summary>
        static bool BranchSpot(List<float3> points, float radius, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                               List<CaveStyle> styles, System.Func<float3, float, bool> covered, List<float3> taken,
                               out (int after, float3 q, float2 along) spot)
        {
            spot = default;
            var best = float.MaxValue;
            var chain = new List<(float3, float, CaveNodeKind)>();
            for (int k = 2; k <= points.Count - 3; k++)
            {
                var q = math.lerp(points[k - 1], points[k], 0.5f);
                var clear = true;
                foreach (var t in taken)
                    clear &= math.distance(t.xz, q.xz) >= 20f;
                if (!clear)
                    continue;
                var along = math.normalize((points[k] - points[k - 1]).xz);
                var across = new float2(-along.y, along.x);
                chain.Clear();
                if (!Branch(styles, q, radius, caves, nodes, kinds, covered, ref across, chain) || chain.Count >= best)
                    continue;
                best = chain.Count;
                spot = (k, q, along);
            }
            return best < float.MaxValue;
        }

        /// <summary>A side node's radius, never exactly the niche's (the niche is found by its radius).</summary>
        static float SideRadius(float r) => math.abs(r - TunnelNicheRadius) < 0.02f ? TunnelNicheRadius + 0.05f : r;

        /// <summary>
        /// A branch from the split at q to the nearest natural cave passage or chamber within TunnelBranchReach that's
        /// not a lava cave, not too steep to walk (about 19 degrees at most), and out to one side of the road (across is turned to that side). Nodes
        /// about every 8 m, the last on the cave's node. False with none.
        /// </summary>
        static bool Branch(List<CaveStyle> styles, float3 q, float radius, List<CaveData> caves, List<CaveNode> nodes,
                           List<CaveNodeKind> kinds, System.Func<float3, float, bool> covered, ref float2 across,
                           List<(float3, float, CaveNodeKind)> chain)
        {
            // Every node out to one side within reach, nearest first; the first whose way there stays under cover.
            var candidates = new List<(float d, float3 floor)>();
            var along = new float2(across.y, -across.x);
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel != 0 || math.distance(math.clamp(q, cave.Min, cave.Max), q) > TunnelBranchReach)
                    continue;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style != null && style.LavaPools)
                    continue;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (kinds[n] != CaveNodeKind.Passage && kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var floor = nodes[n].Position - new float3(0f, nodes[n].Radius * cave.Profile.y * 0.5f, 0f);
                    var to = floor.xz - q.xz;
                    var d = math.length(to);
                    // Not too near, not too steep, and out to one side rather than along the road (within 60 degrees of square to it).
                    if (d < radius + 10f || d >= TunnelBranchReach || math.abs(floor.y - q.y) > 0.35f * d || math.abs(math.dot(to / d, along)) > 0.87f)
                        continue;
                    candidates.Add((d, floor));
                }
            }
            candidates.Sort((x, y) => x.d.CompareTo(y.d));
            foreach (var (_, target) in candidates)
            {
                var dir = math.normalize(target.xz - q.xz);
                var start = q + new float3(dir.x, 0f, dir.y) * (radius + 2f);
                var steps = math.max(1, (int)math.ceil(math.distance(start.xz, target.xz) / 8f));
                var ok = true;
                for (int j = 0; j < steps && ok; j++)   // (the last node is in the cave itself)
                    ok = covered(math.lerp(start, target, j / (float)steps), 2f);
                if (!ok)
                    continue;
                if (math.dot(dir, across) < 0f)
                    across = -across;
                for (int j = 0; j <= steps; j++)
                    chain.Add((math.lerp(start, target, j / (float)steps), 2f, CaveNodeKind.Passage));
                return true;
            }
            return false;
        }

        /// <summary>Whether a tunnel's chamber (by its order among the tunnel's chambers) is left as a camp socket rather than given a bench.</summary>
        static bool TunnelCampRoll(uint key, int ordinal) =>
            BiomeSampling.Scramble(math.hash(new uint3(key, 0xCA40u, (uint)ordinal))) % 100u < 50u;

        /// <summary>Whether a tunnel's node is a side chamber's middle: wider than the road's own nodes.</summary>
        static bool TunnelChamberNode(float road, float r) => r > road + 0.01f;

        /// <summary>
        /// W-172 row 5: the tunnel camp sockets: side chambers in road tunnels left without a bench, for folk to make
        /// camp in (xyz the chamber's floor middle, w its radius). Read from the baked caves, so it needs nothing stored.
        /// </summary>
        public static List<float4> TunnelCamps(ref CaveSetBlob set)
        {
            var camps = new List<float4>();
            for (int c = 0; c < set.Caves.Length; c++)
            {
                var cave = set.Caves[c];
                if (cave.Tunnel == 0)
                    continue;
                var road = set.Nodes[cave.FirstNode].Radius;
                var ordinal = 0;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    var node = set.Nodes[n];
                    if (!TunnelChamberNode(road, node.Radius))
                        continue;
                    if (TunnelCampRoll(cave.Key, ordinal++))
                        camps.Add(new float4(node.Position - new float3(0f, node.Radius * cave.Profile.y * 0.45f, 0f), node.Radius));
                }
            }
            return camps;
        }

        /// <summary>
        /// Dresses the side ways: mushrooms and the like (the floor flora of the plain style for the biome above) on
        /// their floors, roots hanging where the rock overhead is thin under grassy ground, and in each chamber not
        /// left as a camp socket a bench against the wall facing the middle.
        /// </summary>
        static void PlaceTunnelSides(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<CaveData> caves, List<CaveNode> nodes,
                                     List<CaveSegment> segments, List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            var lit = TunnelLampStyle(styles);
            var bench = lit != null && lit.Bench != null ? flora.IndexOf(lit.Bench) : -1;
            var terrain = source;
            float Wall(float3 from, float2 dir)
            {
                for (var t = 0.3f; t < 12f; t += 0.1f)   // collision: bake ray to the side way's wall, not LOD
                    if (terrain.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t) >= 0f)
                        return t;
                return 0f;
            }
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel == 0)
                    continue;
                var road = nodes[cave.FirstNode].Radius;
                var rng = new Random(math.hash(new uint2(cave.Key, 0x51D3Fu)) | 1u);
                var ordinal = 0;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    var node = nodes[n];
                    if (node.Radius == road || node.Radius == TunnelNicheRadius)
                        continue;
                    var floorY = node.Position.y - node.Radius * cave.Profile.y * 0.45f;
                    var chamber = TunnelChamberNode(road, node.Radius);
                    if (chamber)
                    {
                        SideCounts[1]++;
                        if (TunnelCampRoll(cave.Key, ordinal++))
                            SideCounts[3]++;
                        else if (bench >= 0)
                        {
                            // Against the wall, at a random bearing, its back to the rock.
                            var a = rng.NextFloat(math.PI * 2f);
                            var w = new float2(math.cos(a), math.sin(a));
                            var t = Wall(new float3(node.Position.x, floorY + 0.5f, node.Position.z), w);
                            if (t > 1f)
                            {
                                var spot = new float3(node.Position.x, floorY - 0.03f, node.Position.z) + new float3(w.x, 0f, w.y) * (t - 0.55f);
                                var floor = terrain.FloorUnder(spot + new float3(0f, 0.5f, 0f), out var inCave);
                                if (inCave && math.abs(floor - floorY) < 0.5f)
                                {
                                    extras.Plants.Add(new CavePlant { Position = new float3(spot.x, floor - 0.03f, spot.z), Yaw = math.atan2(w.x, -w.y), Scale = 1f, Species = bench, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                                    SideCounts[2]++;
                                }
                            }
                        }
                    }
                    // The plain rock style for the biome above (the bore's own may be the dug one, with no flora).
                    var style = BiomeStyle(source, palette, styles, node.Position.xz, ref rng);
                    if (style == null)
                        continue;
                    var spread = node.Radius * 0.75f;
                    foreach (var entry in style.Flora)
                    {
                        if (entry == null || entry.Species == null || entry.Density <= 0f || entry.Light == CaveLight.NearDaylight)
                            continue;
                        var species = flora.IndexOf(entry.Species);
                        if (species < 0)
                            continue;
                        // Thicker than in a cave (details tenet): a side way is a small, quiet place.
                        var expected = entry.Density * 2f * math.PI * spread * spread / 100f * rng.NextFloat(0.6f, 1.4f);
                        var count = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
                        for (int k = 0; k < count; k++)
                        {
                            var a = rng.NextFloat(math.PI * 2f);
                            var q = node.Position.xz + new float2(math.cos(a), math.sin(a)) * spread * math.sqrt(rng.NextFloat());
                            var floor = terrain.FloorUnder(new float3(q.x, node.Position.y, q.y), out var inCave);
                            if (!inCave || math.abs(floor - floorY) > 0.6f || terrain.CaveDistance(new float3(q.x, floor + 0.5f, q.y)) > -0.3f)
                                continue;
                            var at = new float3(q.x, floor - 0.03f, q.y);
                            if (entry.Hanging)
                            {
                                // Deeper than in a cave (up to 14 m of rock): a dug way follows the fissures roots reach down.
                                var ceiling = terrain.CeilingOver(new float3(q.x, floor + 0.3f, q.y));
                                var ground = terrain.SampleGround(q);
                                if (ground.Height - ceiling > math.max(entry.MaxCover, 14f) || ceiling - floor < entry.Species.Height.y * entry.Species.SizeRange.y * 0.5f + 1.2f
                                    || terrain.SampleSurface(q, ground, terrain.SampleNormal(q, 1f)).Grass < 0.15f)
                                    continue;
                                at = new float3(q.x, ceiling + 0.05f, q.y);
                            }
                            if (UnderRock(at, extras.Speleothems))
                                continue;
                            var size = entry.Species.SizeRange;
                            extras.Plants.Add(new CavePlant { Position = at, Yaw = rng.NextFloat(math.PI * 2f), Scale = math.lerp(size.x, size.y, rng.NextFloat()), Species = species, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                            SideCounts[entry.Hanging ? 6 : 5]++;
                        }
                    }
                }
            }
        }
    }
}
