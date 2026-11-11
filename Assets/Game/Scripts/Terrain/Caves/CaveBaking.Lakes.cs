using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-134 row 3: underground lakes. Some wet caves (a style with pools or streams, never lava or dug mines) roll for a
    /// lake: a domed hall opening off the side of a passage, filled with deep water (2.8 m at the middle, deeper than
    /// SwimDepth over most of it), with a beach rising from the water back to the passage, sometimes a second basin
    /// joined to the first. A lake is a CavePool with a Hall: its water is the same baked cave water as every pool
    /// (CaveSystem.Water), its hall joins the cave's air in CaveSampling.Settle. Each is measured against the caves as
    /// they are (rock all round, cover over the dome, no other air in the hall or under the water) and kept only where it
    /// fits; its draws come from its own seed, so nothing placed before it moves. Then a few details from the lake's own
    /// seed (the details tenet): drip columns standing in the water, stalactites over it, a ledge of fallen blocks at
    /// the shore and patches of the style's glowing flora on the beach, all carved rock and cave plants.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Chance a wet cave tries for a lake (most tries find a spot along the cave: 2-4 lakes an island).</summary>
        // gameplay: rare enough that finding one stays a surprise
        const float LakeChance = 0.15f;
        /// <summary>Water depth at a lake's middle (m): 1.3 m swimming depth reaches out to about 0.94 of its radius.</summary>
        const float LakeBasin = 2.8f;
        /// <summary>A lake's dome never rises closer than this under the ground (m), on top of Cover.</summary>
        const float LakeCover = 1.5f;

        /// <summary>A kept lake: its cave, its pools (the main basin first) and its seed, for its details.</summary>
        class Lake
        {
            public int Cave;
            public uint Seed;
            /// <summary>From the main basin's centre towards the passage it opens off (flat, unit).</summary>
            public float2 Entrance;
            public readonly List<CavePool> Pools = new List<CavePool>();
        }

        /// <summary>Lakes misses this bake: [0] no floor at the host, [1] cover, [2] air under the water, [3] other air in the hall, [4] overlap.</summary>
        static readonly int[] LakeMisses = new int[5];

        /// <summary>The box a lake pool's hall (and its basin) fills, grown by a margin.</summary>
        internal static void LakeBox(in CavePool pool, float margin, out float3 lo, out float3 hi)
        {
            lo = new float3(pool.Centre.x - pool.Span - margin, pool.Centre.y - pool.Depth - margin, pool.Centre.z - pool.Span - margin);
            hi = new float3(pool.Centre.x + pool.Span + margin, pool.Centre.y + pool.Hall + margin, pool.Centre.z + pool.Span + margin);
        }

        static void PlaceLakes(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                               List<CaveSegment> segments, List<CaveStyle> styles, Extras extras)
        {
            var terrain = source;   // local functions can't capture an in parameter
            System.Array.Clear(LakeMisses, 0, LakeMisses.Length);
            var planned = 0;
            var log = new System.Text.StringBuilder();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var rng = new Random(math.hash(new uint2(cave.Key, 0x1A4Eu)) | 1u);
                // Every draw is made whatever happens, so one cave's lake never shifts another's.
                float roll = rng.NextFloat(), radius = rng.NextFloat(6f, 9f), height = rng.NextFloat(6.5f, 9.5f);
                float twin = rng.NextFloat(), twinSize = rng.NextFloat(0.65f, 0.95f), twinTurn = rng.NextFloat(-0.6f, 0.6f);
                var pickStart = rng.NextUInt();
                var flip = rng.NextBool();
                var turn = rng.NextFloat(-0.35f, 0.35f);
                var seed = rng.NextUInt() | 1u;
                if (cave.Tunnel != 0 || style == null || style.LavaPools || style.Straightness >= 0.5f ||
                    style.PoolChance + style.StreamChance <= 0f || cave.NodeCount < 4 || roll >= LakeChance)
                    continue;
                planned++;

                var hosts = new List<int>();
                for (int n = cave.FirstNode + 2; n < cave.FirstNode + cave.NodeCount; n++)
                    if (kinds[n] == CaveNodeKind.Passage || kinds[n] == CaveNodeKind.Chamber)
                        hosts.Add(n);
                for (int k = 0; k < hosts.Count; k++)
                {
                    var n = hosts[(int)((pickStart + (uint)k) % (uint)hosts.Count)];
                    if (TryLake(n, flip ? 1f : -1f) || TryLake(n, flip ? -1f : 1f))
                        break;
                }

                bool TryLake(int n, float side)
                {
                    // The way along the passage here, from the node's neighbours.
                    var near = new List<int> { n };
                    for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                        if (segments[s].A == n) near.Add(segments[s].B);
                        else if (segments[s].B == n) near.Add(segments[s].A);
                    if (near.Count < 2)
                        return false;
                    var along = near.Count >= 3 ? nodes[near[2]].Position.xz - nodes[near[1]].Position.xz
                                                : nodes[n].Position.xz - nodes[near[1]].Position.xz;
                    along = math.normalizesafe(along, new float2(1f, 0f));
                    var across = side * new float2(-along.y, along.x);
                    var dir = new float2(across.x * math.cos(turn) - across.y * math.sin(turn), across.x * math.sin(turn) + across.y * math.cos(turn));
                    var host = nodes[n].Position;
                    var r = nodes[n].Radius * cave.Profile.x;
                    var floor = terrain.FloorUnder(host, out var inCave);
                    if (!inCave)
                        return LakeMiss(0);
                    // The beach meets the passage's floor at the host node.
                    var dist = radius * 1.2f + 2f + r * 0.3f;
                    var level = floor - 0.12f - (dist - radius * 1.2f) * CaveSampling.HallBeach;
                    var centre = host.xz + dir * dist;
                    var pools = new List<CavePool>
                    {
                        new CavePool
                        {
                            Centre = new float3(centre.x, level, centre.y), Radius = radius, Depth = LakeBasin,
                            Hall = height, Span = dist + 6f
                        }
                    };
                    if (twin < 0.5f)
                    {
                        var d2 = new float2(dir.x * math.cos(twinTurn) - dir.y * math.sin(twinTurn), dir.x * math.sin(twinTurn) + dir.y * math.cos(twinTurn));
                        var c2 = centre + d2 * radius * 1.25f;
                        var r2 = radius * twinSize;
                        pools.Add(new CavePool
                        {
                            Centre = new float3(c2.x, level, c2.y), Radius = r2, Depth = LakeBasin * 0.9f,
                            Hall = height * 0.85f, Span = r2 * 1.2f + 5f
                        });
                    }

                    // Cover: the dome stays under the ground by Cover and a margin; a low ground lowers it, down to 4.5 m.
                    for (int i = 0; i < pools.Count; i++)
                    {
                        var pool = pools[i];
                        var ground = terrain.SampleHeight(pool.Centre.xz);
                        for (int a = 0; a < 8; a++)
                        {
                            var angle = a * math.PI * 0.25f;
                            ground = math.min(ground, terrain.SampleHeight(pool.Centre.xz + new float2(math.cos(angle), math.sin(angle)) * pool.Span * 0.7f));
                        }
                        pool.Hall = math.min(pool.Hall, ground - level - Cover - LakeCover);
                        if (pool.Hall < 4.5f)
                            return LakeMiss(1);
                        pools[i] = pool;
                    }

                    // No air under the water (it would drain into a passage below): the basins and a metre round them.
                    foreach (var pool in pools)
                    {
                        var reach = pool.Radius * 1.2f + 1f;
                        for (float x = -reach; x <= reach; x += 1.5f)
                        for (float z = -reach; z <= reach; z += 1.5f)
                        {
                            if (x * x + z * z > reach * reach)
                                continue;
                            for (var y = level - pool.Depth - 1.5f; y <= level + 0.3f; y += 1f)
                                if (terrain.CaveDistance(new float3(pool.Centre.x + x, y, pool.Centre.z + z)) < 0.3f)
                                    return LakeMiss(2);
                        }
                    }

                    // No other air in the hall (another cave, or this one's passages looping through), except the
                    // passages round the host node, which the hall opens onto.
                    var open = new List<int>();
                    foreach (var m in near)
                        for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                            if ((segments[s].A == m || segments[s].B == m) && !open.Contains(s))
                                open.Add(s);
                    bool OnOpen(float3 p)
                    {
                        foreach (var s in open)
                        {
                            var a = nodes[segments[s].A];
                            var b = nodes[segments[s].B];
                            var ab = b.Position - a.Position;
                            var t = math.saturate(math.dot(p - a.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
                            if (math.distance(p, a.Position + ab * t) < math.lerp(a.Radius, b.Radius, t) * math.cmax(cave.Profile) * 1.2f + 1.5f)
                                return true;
                        }
                        return false;
                    }
                    foreach (var pool in pools)
                        for (float x = -pool.Span; x <= pool.Span; x += 2f)
                        for (float z = -pool.Span; z <= pool.Span; z += 2f)
                        for (var y = level + 0.3f; y <= level + pool.Hall; y += 2f)
                        {
                            var p = new float3(pool.Centre.x + x, y, pool.Centre.z + z);
                            if (HallAir(pool, p) < 1f && terrain.CaveDistance(p) < 0.3f && !OnOpen(p))
                                return LakeMiss(3);
                        }

                    // Clear of destination chambers, other lakes, this cave's pools and its stream.
                    foreach (var pool in pools)
                    {
                        LakeBox(pool, 2f, out var lo, out var hi);
                        foreach (var room in extras.Rooms)
                        {
                            RoomBox(room, out var rlo, out var rhi);
                            if (math.all(rlo <= hi) && math.all(rhi >= lo))
                                return LakeMiss(4);
                        }
                        foreach (var lake in extras.Lakes)
                            foreach (var other in lake.Pools)
                            {
                                LakeBox(other, 2f, out var olo, out var ohi);
                                if (math.all(olo <= hi) && math.all(ohi >= lo))
                                    return LakeMiss(4);
                            }
                        for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                            if (math.distance(extras.Pools[i].Centre.xz, pool.Centre.xz) < pool.Span + extras.Pools[i].Radius * 1.2f)
                                return LakeMiss(4);
                        for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
                        {
                            var stream = extras.Streams[s];
                            for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount; i++)
                                if (math.distance(extras.StreamPoints[i].Position.xz, pool.Centre.xz) < pool.Span)
                                    return LakeMiss(4);
                        }
                    }

                    // Kept: the pools go after the cave's own, and the cave's box grows over the hall.
                    var at = cave.FirstPool + cave.PoolCount;
                    extras.Pools.InsertRange(at, pools);
                    cave.PoolCount += pools.Count;
                    foreach (var pool in pools)
                    {
                        LakeBox(pool, 1f, out var lo, out var hi);
                        cave.Min = math.min(cave.Min, lo);
                        cave.Max = math.max(cave.Max, hi);
                    }
                    caves[c] = cave;
                    for (int later = c + 1; later < caves.Count; later++)
                    {
                        var next = caves[later];
                        next.FirstPool += pools.Count;
                        caves[later] = next;
                    }
                    var kept = new Lake { Cave = c, Seed = seed, Entrance = -dir };
                    kept.Pools.AddRange(pools);
                    extras.Lakes.Add(kept);
                    log.Append($"; cave {c} at ({centre.x:F1}, {level:F1}, {centre.y:F1}) radius {radius:F1}, hall {pools[0].Hall:F1}, basins {pools.Count}, host ({host.x:F1}, {floor:F1}, {host.z:F1})");
                    return true;
                }
            }
            UnityEngine.Debug.Log($"[cave-lakes] planned {planned}, kept {extras.Lakes.Count}; misses: host {LakeMisses[0]}, cover {LakeMisses[1]}, wet {LakeMisses[2]}, air {LakeMisses[3]}, overlap {LakeMisses[4]}{log}");
        }

        static bool LakeMiss(int why)
        {
            LakeMisses[why]++;
            return false;
        }

        /// <summary>A lake hall's air without its wall noise (for measuring): under the dome and over the floor.</summary>
        static float HallAir(in CavePool pool, float3 p) =>
            math.max(CaveSampling.HallDome(pool, p), (CaveSampling.HallFloor(pool, math.distance(p.xz, pool.Centre.xz)) - p.y) * 0.6f);

        /// <summary>
        /// The details tenet for each kept lake, from its own seed: one or two drip columns standing in the water, a few
        /// stalactites over it (their tips well clear of a swimmer), sometimes a ledge of fallen blocks at the shore by the
        /// way in, and a patch or two of the style's glowing flora on the beach. Appended after everything else, so
        /// nothing placed before moves.
        /// </summary>
        static void PlaceLakeDetails(in TerrainSource source, List<CaveData> caves, List<CaveStyle> styles,
                                     List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            foreach (var lake in extras.Lakes)
            {
                var rng = new Random(lake.Seed);
                var main = lake.Pools[0];
                var level = main.Centre.y;
                var away = math.atan2(-lake.Entrance.y, -lake.Entrance.x);
                var towards = math.atan2(lake.Entrance.y, lake.Entrance.x);
                float2 Round(float angle) => new float2(math.cos(angle), math.sin(angle));

                // Drip columns, out in the water on the far side from the way in.
                var columns = 1 + rng.NextInt(2);
                for (int i = 0; i < columns; i++)
                {
                    var q = main.Centre.xz + Round(away + rng.NextFloat(-1.2f, 1.2f)) * main.Radius * rng.NextFloat(0.3f, 0.6f);
                    var thick = rng.NextFloat(0.7f, 1.3f);
                    var lean = rng.NextFloat2(-0.4f, 0.4f);
                    var ceiling = source.CeilingOver(new float3(q.x, level + 0.5f, q.y));
                    if (ceiling - level < 3f || ceiling - level > main.Hall + 3f)
                        continue;
                    var bed = CaveSampling.HallFloor(main, math.distance(q, main.Centre.xz));
                    // A drip column: flared at foot and ceiling, ringed and lumpy (CaveSampling.Column), not a smooth rod.
                    var column = Rod(new float3(q.x, bed - 0.5f, q.y), new float3(q.x + lean.x, ceiling + 0.5f, q.y + lean.y),
                                     thick * 0.85f, thick * 0.6f, 0.9f);
                    column.Flow = 0.3f * thick + 0.12f;
                    var flare = 2.3f * column.Flow;
                    column.Min -= flare;
                    column.Max += flare;
                    extras.Speleothems.Add(column);
                }

                // Stalactites over the water, their tips at least 2 m over it.
                var hanging = 3 + rng.NextInt(5);
                for (int i = 0; i < hanging; i++)
                {
                    var q = main.Centre.xz + Round(rng.NextFloat(math.PI * 2f)) * main.Radius * 0.9f * math.sqrt(rng.NextFloat());
                    var root = rng.NextFloat(0.35f, 0.6f);
                    var want = rng.NextFloat(0.8f, 3f);
                    var ceiling = source.CeilingOver(new float3(q.x, level + 0.5f, q.y));
                    var length = math.min(want, ceiling - level - 2f);
                    if (length < 0.6f || ceiling - level > main.Hall + 3f)
                        continue;
                    extras.Speleothems.Add(Rod(new float3(q.x, ceiling + 0.4f, q.y), new float3(q.x, ceiling - length, q.y), root, 0.04f, 0.4f));
                }

                // A ledge of fallen blocks at the shore, beside the way in.
                var ledge = rng.NextFloat() < 0.6f;
                var ledgeAngle = towards + (rng.NextBool() ? 1f : -1f) * rng.NextFloat(0.5f, 1.1f);
                var ledgeOut = rng.NextFloat(1f, 1.15f);
                var blocks = 2 + rng.NextInt(2);
                for (int i = 0; i < blocks; i++)
                {
                    var size = rng.NextFloat(0.8f, 1.4f);
                    var rise = rng.NextFloat(-0.2f, 0.3f);
                    var shape = rng.NextFloat(0.05f, 1f);
                    if (!ledge)
                        continue;
                    var q = main.Centre.xz + Round(ledgeAngle + i * 1.3f / main.Radius) * main.Radius * ledgeOut;
                    var middle = new float3(q.x, level + rise, q.y);
                    var block = Rod(middle, middle, size, size, 0.08f);
                    block.Block = shape;
                    extras.Speleothems.Add(block);
                }

                // Glowing flora on the beach: the style's glow species if it has one, else any floor flora that grows in
                // the dark.
                var style = caves[lake.Cave].Style > 0 ? styles[caves[lake.Cave].Style - 1] : null;
                var pick = GlowFlora(style);
                var patches = rng.NextFloat() < 0.5f ? 2 : 1;
                for (int k = 0; k < patches; k++)
                {
                    var spot = main.Centre.xz + Round(rng.NextFloat(math.PI * 2f)) * (main.Radius * 1.2f + rng.NextFloat(1f, 2.5f));
                    var count = 8 + rng.NextInt(9);
                    for (int i = 0; i < count; i++)
                    {
                        var q = spot + Round(rng.NextFloat(math.PI * 2f)) * 1.8f * math.sqrt(rng.NextFloat());
                        var yaw = rng.NextFloat(math.PI * 2f);
                        var grow = rng.NextFloat();
                        var variant = rng.NextInt(3);
                        var random = rng.NextFloat();
                        var species = pick != null ? flora.IndexOf(pick.Species) : -1;
                        if (species < 0)
                            continue;
                        var floor = source.FloorUnder(new float3(q.x, level + 3f, q.y), out var inCave);
                        if (!inCave || floor < level + 0.1f || source.CaveDistance(new float3(q.x, floor + 0.5f, q.y)) > -0.3f)
                            continue;
                        var at = new float3(q.x, floor - 0.03f, q.y);
                        if (UnderRock(at, extras.Speleothems))
                            continue;
                        extras.Plants.Add(new CavePlant
                        {
                            Position = at,
                            Yaw = yaw,
                            Scale = math.lerp(pick.Species.SizeRange.x, pick.Species.SizeRange.y, grow),
                            Species = species,
                            Variant = variant,
                            Random = random
                        });
                    }
                }
            }
        }
    }
}
