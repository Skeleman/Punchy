using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-134 row 4: river chambers. Some wet caves (as for lakes: a style with pools or streams, never lava or dug mines)
    /// roll for one: a long vaulted hall beside a passage, joined to it by a short gallery, with a river coming out of a
    /// hole in one end wall, running the whole length along the far side and leaving by a hole in the other, dropping
    /// down a short chute partway (whitewater over the steep water, spray at its foot). The hall is a CaveChamber, its
    /// air joining the cave's in CaveSampling.Settle; the river is one of the cave's streams, so its water is the same
    /// baked cave water as every stream (CaveSystem.Water). Each is measured against the caves as they are (cover over
    /// the vault, no air under the floor, no other air in the hall or by the river) and kept only where it fits; its
    /// draws come from its own seed, so nothing placed before it moves. Then a few details from the river's own seed (the
    /// details tenet): gravel bars on the inside of bends, a fallen slab on the near bank, stalactites over the water and
    /// patches of the style's glowing flora, all carved rock and cave plants.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Chance a wet cave tries for a river chamber.</summary>
        // gameplay: rare enough that finding one stays a surprise (about as often as a lake)
        const float RiverChance = 0.15f;
        /// <summary>Points along a chamber's river (m apart).</summary>
        const float RiverStep = 1.2f;
        /// <summary>A chamber's river meanders across its floor this many radians per metre (a bend every 10 m or so).</summary>
        const float Meander = 0.3f;

        /// <summary>A kept river chamber: its cave, its chamber, its river's points and its seed, for its details.</summary>
        class River
        {
            public int Cave;
            public uint Seed;
            public int Chamber;
            public int FirstPoint, PointCount;
            /// <summary>Which side of the axis the river runs (+1 left of the flow), the passage being on the other.</summary>
            public float Side;
            /// <summary>The meander's phase and the river's width (m).</summary>
            public float Phase, Width;
        }

        /// <summary>River misses this bake: [0] no floor at the host, [1] cover, [2] air under the floor, [3] other air in the hall or by the river, [4] overlap.</summary>
        static readonly int[] RiverMisses = new int[5];

        /// <summary>Where a chamber's river runs across its axis at a distance along it (local y, see CaveSampling.ChamberLocal).</summary>
        static float RiverAcross(in CaveChamber ch, float side, float phase, float u) =>
            side * ch.Width * 0.4f + 0.8f * math.sin(u * Meander + phase);

        /// <summary>A point on a chamber's floor plan from its local place (x along from A, y across, left positive).</summary>
        static float2 ChamberPoint(in CaveChamber ch, float u, float v)
        {
            var dir = math.normalizesafe(ch.B.xz - ch.A.xz, new float2(1f, 0f));
            return ch.A.xz + dir * u + new float2(-dir.y, dir.x) * v;
        }

        /// <summary>How near a spot along a chamber's axis is to the chute (1 on it, 0 a chute's run away).</summary>
        static float ChuteNear(in CaveChamber ch, float u)
        {
            var length = math.max(math.distance(ch.A.xz, ch.B.xz), 1f);
            var run = ch.Drop * CaveSampling.ChuteRun;
            return math.saturate(1f - math.abs(u - ch.DropAt * length) / math.max(run, 0.01f));
        }

        static void PlaceRivers(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                List<CaveSegment> segments, List<CaveStyle> styles, Extras extras)
        {
            var terrain = source;   // local functions can't capture an in parameter
            System.Array.Clear(RiverMisses, 0, RiverMisses.Length);
            var planned = 0;
            var log = new System.Text.StringBuilder();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var rng = new Random(math.hash(new uint2(cave.Key, 0x81E4u)) | 1u);
                // Every draw is made whatever happens, so one cave's river never shifts another's.
                float roll = rng.NextFloat(), length = rng.NextFloat(32f, 44f), wide = rng.NextFloat(5.5f, 7f);
                float height = rng.NextFloat(7f, 10f), drop = rng.NextFloat(1.6f, 2.4f), dropAt = rng.NextFloat(0.55f, 0.7f);
                float doorAt = rng.NextFloat(0.22f, 0.38f), fall = rng.NextFloat(0.03f, 0.05f);
                var flip = rng.NextBool();
                var reverse = rng.NextBool();
                var pickStart = rng.NextUInt();
                var riverWidth = rng.NextFloat(2.2f, 3.2f);
                var phase = rng.NextFloat(math.PI * 2f);
                var seed = rng.NextUInt() | 1u;
                if (cave.Tunnel != 0 || style == null || style.LavaPools || style.Straightness >= 0.5f ||
                    style.PoolChance + style.StreamChance <= 0f || cave.NodeCount < 4 || roll >= RiverChance)
                    continue;
                planned++;

                var hosts = new List<int>();
                for (int n = cave.FirstNode + 2; n < cave.FirstNode + cave.NodeCount; n++)
                    if (kinds[n] == CaveNodeKind.Passage || kinds[n] == CaveNodeKind.Chamber)
                        hosts.Add(n);
                for (int k = 0; k < hosts.Count; k++)
                {
                    var n = hosts[(int)((pickStart + (uint)k) % (uint)hosts.Count)];
                    if (TryRiver(n, flip ? 1f : -1f) || TryRiver(n, flip ? -1f : 1f))
                        break;
                }

                bool TryRiver(int n, float side)
                {
                    // The way along the passage here, from the node's neighbours: the chamber lies alongside it.
                    var near = new List<int> { n };
                    for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                        if (segments[s].A == n) near.Add(segments[s].B);
                        else if (segments[s].B == n) near.Add(segments[s].A);
                    if (near.Count < 2)
                        return false;
                    var along = near.Count >= 3 ? nodes[near[2]].Position.xz - nodes[near[1]].Position.xz
                                                : nodes[n].Position.xz - nodes[near[1]].Position.xz;
                    along = math.normalizesafe(along, new float2(1f, 0f)) * (reverse ? -1f : 1f);
                    var left = new float2(-along.y, along.x);
                    var host = nodes[n].Position;
                    var r = nodes[n].Radius * cave.Profile.x;
                    var hostFloor = terrain.FloorUnder(host, out var inCave);
                    if (!inCave)
                        return RiverMiss(0);
                    // The axis runs past the passage a vault's width and more away, on this side; the gallery comes in
                    // a metre down onto its floor, a third or so along, and the river keeps to the far side.
                    var dist = r + wide + 3.5f;
                    var centre = host.xz + left * side * dist;
                    var a = centre - along * doorAt * length;
                    var ay = hostFloor - 1f + fall * doorAt * length;
                    var ch = new CaveChamber
                    {
                        Cave = c,
                        A = new float3(a.x, ay, a.y),
                        B = new float3(a.x + along.x * length, ay - fall * length - drop, a.y + along.y * length),
                        Width = wide, Height = height, DropAt = dropAt, Drop = drop,
                        Door = new float3(host.x, hostFloor, host.z)
                    };
                    // The river runs out of the far end walls (the vault's ends are rounded, a width past A and B).
                    var u0 = -wide - 2f;
                    var u1 = length + wide + 2f;

                    // Cover: the vault stays under the ground by Cover and a margin; a low ground lowers it, down to 5.5 m.
                    for (var u = -wide; u <= length + wide; u += 4f)
                        for (var v = -wide; v <= wide; v += wide)
                        {
                            var q = ChamberPoint(ch, u, v);
                            var top = CaveSampling.ChamberFloor(ch, u, out _);
                            ch.Height = math.min(ch.Height, terrain.SampleHeight(q) - top - Cover - LakeCover);
                        }
                    if (ch.Height < 5.5f)
                        return RiverMiss(1);

                    // No air under the floor or the water (it would drain into a passage below), a metre past its edge.
                    for (var u = -wide; u <= length + wide; u += 1.5f)
                        for (var v = -wide - 1f; v <= wide + 1f; v += 1.5f)
                        {
                            var q = ChamberPoint(ch, u, v);
                            var floor = CaveSampling.ChamberFloor(ch, u, out _);
                            for (var y = floor - 2.5f; y <= floor + 0.3f; y += 1f)
                                if (terrain.CaveDistance(new float3(q.x, y, q.y)) < 0.3f)
                                    return RiverMiss(2);
                        }

                    // No other air in the hall or its gallery (another cave, or this one's passages looping through),
                    // except the passages round the host node, which the gallery opens onto; and none by the river's
                    // line as far as its banks reach (its channel lowers this cave's floors there), out past its ends.
                    var open = new List<int>();
                    foreach (var m in near)
                        for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                            if ((segments[s].A == m || segments[s].B == m) && !open.Contains(s))
                                open.Add(s);
                    bool OnOpen(float3 p)
                    {
                        foreach (var s in open)
                        {
                            var na = nodes[segments[s].A];
                            var nb = nodes[segments[s].B];
                            var ab = nb.Position - na.Position;
                            var t = math.saturate(math.dot(p - na.Position, ab) / math.max(math.lengthsq(ab), 1e-6f));
                            if (math.distance(p, na.Position + ab * t) < math.lerp(na.Radius, nb.Radius, t) * math.cmax(cave.Profile) * 1.2f + 1.5f)
                                return true;
                        }
                        return false;
                    }
                    for (var u = -wide; u <= length + wide; u += 2f)
                        for (var v = -wide; v <= wide; v += 2f)
                        {
                            var q = ChamberPoint(ch, u, v);
                            var floor = CaveSampling.ChamberFloor(ch, u, out _);
                            for (var y = floor + 0.3f; y <= floor + ch.Height; y += 2f)
                            {
                                var p = new float3(q.x, y, q.y);
                                if (CaveSampling.ChamberVault(ch, p, out _, out _) < 1f && terrain.CaveDistance(p) < 0.3f && !OnOpen(p))
                                    return RiverMiss(3);
                            }
                        }
                    var doorEnd = CaveSampling.ChamberDoorEnd(ch);
                    for (var t = 0f; t <= 1f; t += 0.1f)
                    {
                        var p = math.lerp(ch.Door, doorEnd, t) + new float3(0f, CaveSampling.GalleryRise, 0f);
                        if (terrain.CaveDistance(p) < 0.3f && !OnOpen(p))
                            return RiverMiss(3);
                    }
                    for (var u = u0 - 6f; u <= u1 + 6f; u += 1.5f)
                    {
                        var line = RiverAcross(ch, side, phase, math.clamp(u, u0, u1));
                        var surface = CaveSampling.ChamberFloor(ch, u, out _) - 0.15f;
                        for (var v = line - 6.5f; v <= line + 6.5f; v += 1.5f)
                        {
                            var q = ChamberPoint(ch, u, v);
                            for (var y = surface - 1.5f; y <= surface + 3f; y += 1.5f)
                            {
                                var p = new float3(q.x, y, q.y);
                                if (terrain.CaveDistance(p) < 0.3f && !OnOpen(p))
                                    return RiverMiss(3);
                            }
                        }
                    }

                    // The box round the hall, its gallery and the river's ends.
                    float3 lo = float.MaxValue, hi = float.MinValue;
                    for (int k = 0; k < 4; k++)
                    {
                        var q = ChamberPoint(ch, (k & 1) == 0 ? u0 - 1f : u1 + 1f, (k & 2) == 0 ? -wide - 1.5f : wide + 1.5f);
                        lo = math.min(lo, new float3(q.x, ch.B.y - 2f, q.y));
                        hi = math.max(hi, new float3(q.x, ch.A.y + ch.Height + 1f, q.y));
                    }
                    // (the hall alone, for this cave's own water: the gallery may cross its passage's stream or pools)
                    var vlo = lo - 2f;
                    var vhi = hi + 2f;
                    lo = math.min(lo, ch.Door - CaveSampling.GalleryRadius);
                    hi = math.max(hi, ch.Door + CaveSampling.GalleryRadius + CaveSampling.GalleryRise);
                    ch.Min = lo;
                    ch.Max = hi;

                    // Clear of destination chambers, lakes, other river chambers, this cave's pools and its streams.
                    lo -= 2f;
                    hi += 2f;
                    foreach (var room in extras.Rooms)
                    {
                        RoomBox(room, out var rlo, out var rhi);
                        if (math.all(rlo <= hi) && math.all(rhi >= lo))
                            return RiverMiss(4);
                    }
                    foreach (var lake in extras.Lakes)
                        foreach (var other in lake.Pools)
                        {
                            LakeBox(other, 2f, out var olo, out var ohi);
                            if (math.all(olo <= hi) && math.all(ohi >= lo))
                                return RiverMiss(4);
                        }
                    foreach (var other in extras.Chambers)
                        if (math.all(other.Min - 2f <= hi) && math.all(other.Max + 2f >= lo))
                            return RiverMiss(4);
                    for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                    {
                        var pool = extras.Pools[i];
                        var reach = pool.Radius * 1.2f + 2f;
                        if (math.all(pool.Centre.xz + reach >= vlo.xz) && math.all(pool.Centre.xz - reach <= vhi.xz))
                            return RiverMiss(4);
                    }
                    for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
                    {
                        var stream = extras.Streams[s];
                        for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount; i++)
                        {
                            var q = extras.StreamPoints[i].Position.xz;
                            if (math.all(q >= vlo.xz - 4f) && math.all(q <= vhi.xz + 4f))
                                return RiverMiss(4);
                        }
                    }

                    // Kept: the river's points go on the end of the list and its stream after the cave's own (later
                    // caves' ranges move up one), its banks stopping at their edge (CaveStream.Chamber).
                    var first = extras.StreamPoints.Count;
                    float2 min = float.MaxValue, max = float.MinValue;
                    var steps = (int)math.ceil((u1 - u0) / RiverStep);
                    for (int i = 0; i <= steps; i++)
                    {
                        var u = u0 + (u1 - u0) * i / steps;
                        var q = ChamberPoint(ch, u, RiverAcross(ch, side, phase, u));
                        var width = riverWidth * (1f - 0.25f * ChuteNear(ch, u));
                        var point = new CaveStreamPoint
                        {
                            Position = new float3(q.x, CaveSampling.ChamberFloor(ch, u, out _) - 0.15f, q.y),
                            Width = width,
                            Depth = 0.8f
                        };
                        extras.StreamPoints.Add(point);
                        var reach = width + (point.Depth + 1.2f) * CaveSampling.BankRun;   // to the bank's edge (StreamBed)
                        min = math.min(min, q - reach);
                        max = math.max(max, q + reach);
                    }
                    extras.Streams.Insert(cave.FirstStream + cave.StreamCount, new CaveStream
                    {
                        FirstPoint = first, PointCount = extras.StreamPoints.Count - first, Min = min, Max = max, Chamber = 1f
                    });
                    cave.StreamCount++;
                    cave.Min = math.min(cave.Min, ch.Min - 1f);
                    cave.Max = math.max(cave.Max, ch.Max + 1f);
                    caves[c] = cave;
                    for (int later = c + 1; later < caves.Count; later++)
                    {
                        var next = caves[later];
                        next.FirstStream++;
                        caves[later] = next;
                    }
                    extras.Rivers.Add(new River
                    {
                        Cave = c, Seed = seed, Chamber = extras.Chambers.Count, FirstPoint = first,
                        PointCount = extras.StreamPoints.Count - first, Side = side, Phase = phase, Width = riverWidth
                    });
                    extras.Chambers.Add(ch);
                    log.Append($"; cave {c} from ({ch.A.x:F1}, {ch.A.y:F1}, {ch.A.z:F1}) to ({ch.B.x:F1}, {ch.B.y:F1}, {ch.B.z:F1}), " +
                               $"width {wide:F1}, height {ch.Height:F1}, drop {drop:F1}, host ({host.x:F1}, {hostFloor:F1}, {host.z:F1})");
                    return true;
                }
            }
            UnityEngine.Debug.Log($"[cave-rivers] planned {planned}, kept {extras.Rivers.Count}; misses: host {RiverMisses[0]}, cover {RiverMisses[1]}, wet {RiverMisses[2]}, air {RiverMisses[3]}, overlap {RiverMisses[4]}{log}");
        }

        static bool RiverMiss(int why)
        {
            RiverMisses[why]++;
            return false;
        }

        /// <summary>
        /// The details tenet for each kept river chamber, from its own seed: dark holes in the end walls where the river
        /// comes in and goes out, spray and churn at the chute's foot, two or three gravel bars of cobbles on the inside
        /// of bends, a fallen slab on the near bank, stalactites over the water (well clear of a wader) and a patch or
        /// two of the style's glowing flora. Appended after everything else, so nothing placed before moves.
        /// </summary>
        static void PlaceRiverDetails(in TerrainSource source, List<CaveData> caves, List<CaveStyle> styles,
                                      List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            foreach (var river in extras.Rivers)
            {
                var rng = new Random(river.Seed);
                var ch = extras.Chambers[river.Chamber];
                var length = math.max(math.distance(ch.A.xz, ch.B.xz), 1f);
                var flow = math.normalizesafe(ch.B.xz - ch.A.xz, new float2(1f, 0f));
                var flow3 = new float3(flow.x, 0f, flow.y);
                float3 RiverAt(float u)
                {
                    var q = ChamberPoint(ch, u, RiverAcross(ch, river.Side, river.Phase, u));
                    return new float3(q.x, CaveSampling.ChamberFloor(ch, u, out _) - 0.15f, q.y);
                }

                // The holes in the end walls: from just inside the hall, along the river, into the rock.
                for (int end = 0; end < 2; end++)
                {
                    var into = end == 0 ? -flow3 : flow3;
                    var water = RiverAt(end == 0 ? 0f : length);
                    var wall = water + new float3(0f, 0.6f, 0f);
                    for (int step = 0; step < 200 && source.CaveDistance(wall) < 0f; step++)
                        wall += into * 0.1f;
                    var notch = Rod(wall - into * 0.6f, wall + into * 3f, river.Width * 0.5f + 0.3f, river.Width * 0.35f + 0.2f, 0.2f);
                    notch.Carve = 1f;
                    extras.Speleothems.Add(notch);
                }

                // Spray and churn where the chute lands (the stream's own water falls there: no curtain).
                var foot = RiverAt(ch.DropAt * length + ch.Drop * CaveSampling.ChuteRun * 0.5f);
                extras.Waterfalls.Add(new CaveWaterfall
                {
                    Lip = foot + new float3(0f, ch.Drop, 0f) - flow3 * 0.9f,
                    Width = river.Width,
                    Out = flow,
                    Drop = ch.Drop,
                    Spray = 1f
                });

                // Gravel bars: cobbles half sunk at the waterline on the inside of the meander's bends, clear of the chute.
                // Where the meander bends hardest (u * Meander + phase = pi/2 + n pi), along the hall and clear of the chute.
                var bends = new List<float>();
                for (int b = -2; b < 20; b++)
                {
                    var at = (math.PI * 0.5f + b * math.PI - river.Phase) / Meander;
                    if (at > 2f && at < length - 2f && ChuteNear(ch, at) <= 0f)
                        bends.Add(at);
                }
                var bars = 2 + rng.NextInt(2);
                var barStart = rng.NextInt(4);
                for (int k = 0; k < bars && k < bends.Count; k++)
                {
                    var u = bends[(barStart + k) % bends.Count];
                    var cobbles = 6 + rng.NextInt(5);
                    var inside = -math.sign(math.sin(u * Meander + river.Phase));
                    for (int i = 0; i < cobbles; i++)
                    {
                        var du = rng.NextFloat(-1.4f, 1.4f);
                        var dv = rng.NextFloat(-0.1f, 0.9f);
                        var size = rng.NextFloat(0.3f, 0.5f);
                        var shape = rng.NextFloat(0.05f, 1f);
                        var half = river.Width * 0.5f;
                        var v = RiverAcross(ch, river.Side, river.Phase, u + du) + inside * (half * 0.8f + dv);
                        var q = ChamberPoint(ch, u + du, v);
                        var floor = source.FloorUnder(new float3(q.x, CaveSampling.ChamberFloor(ch, u + du, out _) + 1.5f, q.y), out var inCave);
                        if (!inCave)
                            continue;
                        var middle = new float3(q.x, floor - size * 0.35f, q.y);
                        var cobble = Rod(middle, middle, size, size, 0.05f);
                        cobble.Block = shape;
                        extras.Speleothems.Add(cobble);
                    }
                }

                // A slab fallen from the vault onto the near bank, lying tilted, away from the gallery's way in: above
                // the chute or below it, whichever is drawn first and clear of the gallery.
                {
                    var above = rng.NextFloat(0.05f, ch.DropAt * 0.45f);
                    var below = rng.NextFloat(ch.DropAt + 0.15f, 0.95f);
                    var first = rng.NextBool();
                    var v = -river.Side * ch.Width * rng.NextFloat(0.3f, 0.55f);
                    var size = rng.NextFloat(1.6f, 2.2f);
                    var tilt = rng.NextFloat2(-0.5f, 0.5f);
                    var shape = rng.NextFloat(0.05f, 1f);
                    var door = CaveSampling.ChamberDoorEnd(ch);
                    for (int k = 0; k < 2; k++)
                    {
                        var u = ((k == 0) == first ? above : below) * length;
                        var q = ChamberPoint(ch, u, v);
                        var floor = source.FloorUnder(new float3(q.x, CaveSampling.ChamberFloor(ch, u, out _) + 1.5f, q.y), out var inCave);
                        if (!inCave || math.distance(q, door.xz) <= size + CaveSampling.GalleryRadius + 1f)
                            continue;
                        var root = new float3(q.x, floor + 0.25f, q.y);
                        var slab = Rod(root, root + math.normalize(new float3(tilt.x, 1f, tilt.y)) * 0.35f, size, size, 0.1f);
                        slab.Block = shape;
                        extras.Speleothems.Add(slab);
                        break;
                    }
                }

                // Stalactites over the water, their tips at least 2.5 m over it.
                var hanging = 3 + rng.NextInt(4);
                for (int i = 0; i < hanging; i++)
                {
                    var u = rng.NextFloat(1f, length - 1f);
                    var off = rng.NextFloat(-1.5f, 1.5f);
                    var root = rng.NextFloat(0.3f, 0.55f);
                    var want = rng.NextFloat(0.8f, 2.6f);
                    var water = RiverAt(u);
                    var q = ChamberPoint(ch, u, RiverAcross(ch, river.Side, river.Phase, u) + off);
                    var ceiling = source.CeilingOver(new float3(q.x, water.y + 1f, q.y));
                    var hang = math.min(want, ceiling - water.y - 2.5f);
                    if (hang < 0.6f || ceiling - water.y > ch.Height + 3f)
                        continue;
                    extras.Speleothems.Add(Rod(new float3(q.x, ceiling + 0.4f, q.y), new float3(q.x, ceiling - hang, q.y), root, 0.04f, 0.4f));
                }

                // Glowing flora on the near bank.
                var style = caves[river.Cave].Style > 0 ? styles[caves[river.Cave].Style - 1] : null;
                var pick = GlowFlora(style);
                var patches = rng.NextFloat() < 0.5f ? 2 : 1;
                for (int k = 0; k < patches; k++)
                {
                    var su = rng.NextFloat(0.1f, 0.9f) * length;
                    var sv = -river.Side * ch.Width * rng.NextFloat(0.2f, 0.65f);
                    var count = 8 + rng.NextInt(9);
                    for (int i = 0; i < count; i++)
                    {
                        var d = rng.NextFloat2Direction() * 1.8f * math.sqrt(rng.NextFloat());
                        var yaw = rng.NextFloat(math.PI * 2f);
                        var grow = rng.NextFloat();
                        var variant = rng.NextInt(3);
                        var random = rng.NextFloat();
                        var species = pick != null ? flora.IndexOf(pick.Species) : -1;
                        if (species < 0)
                            continue;
                        var q = ChamberPoint(ch, su + d.x, sv + d.y);
                        var water = CaveSampling.ChamberFloor(ch, su + d.x, out _) - 0.15f;
                        var floor = source.FloorUnder(new float3(q.x, water + 3f, q.y), out var inCave);
                        if (!inCave || floor < water + 0.1f || source.CaveDistance(new float3(q.x, floor + 0.5f, q.y)) > -0.3f)
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

        /// <summary>
        /// A style's flora for the glow by cave water: its glow species if it has one, else any floor flora that grows in
        /// the dark (null without either).
        /// </summary>
        static CaveFlora GlowFlora(CaveStyle style)
        {
            CaveFlora pick = null;
            if (style != null)
                foreach (var entry in style.Flora)
                {
                    if (entry == null || entry.Species == null || entry.Hanging || entry.Light == CaveLight.NearDaylight)
                        continue;
                    var glow = entry.Species.name.Contains("Glow") || entry.Species.name.Contains("Glimmer");
                    if (pick == null || glow)
                        pick = entry;
                    if (glow)
                        break;
                }
            return pick;
        }
    }
}
