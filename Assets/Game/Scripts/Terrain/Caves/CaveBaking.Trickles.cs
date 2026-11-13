using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-141 (#261): water trickling down cave walls. A few per cave (more in wet caves: by cave water, lake halls and
    /// river chambers): each out of a narrow slit in a steep, tall stretch of wall, as one thin thread, a fan of two to
    /// four threads, a wide seeping sheet, or drips falling clear off an overhang. Each lands on a small puddle at the
    /// wall's foot, or on cave water when that is right there. Placed last of the landmarks' caves, against the caves as
    /// built (rule 22: plain records, from each cave's own seed, every draw of a try made before any test can skip it).
    /// They carve nothing, so nothing placed before them moves; road tunnels come after and get none.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Trickles a cave tries for: dry caves 1-2, wet ones 2-4 (as many tries again as it can use).</summary>
        // gameplay: a few per cave, so finding one stays a small surprise
        const int DryTrickles = 1, WetTrickles = 2, TrickleTries = 16;
        /// <summary>The lowest a slit sits over the floor, and the highest (m), and the shortest drop kept.</summary>
        const float SlitLow = 2f, SlitHigh = 6f, TrickleMinDrop = 1.6f;
        /// <summary>How steep a slit's wall must be: the most its normal may lean up or down (sine).</summary>
        const float TrickleSteep = 0.35f;

        /// <summary>Trickle misses this bake: [0] no floor, [1] no wall, [2] not steep, [3] too low, [4] broken wall,
        /// [5] crowded (another trickle, a lamp or frame, a room).</summary>
        static readonly int[] TrickleMisses = new int[6];

        static void PlaceTrickles(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                  List<CaveStyle> styles, Extras extras)
        {
            System.Array.Clear(TrickleMisses, 0, TrickleMisses.Length);
            var byKind = new int[4];
            int dryCaves = 0, wetCaves = 0, dry = 0, wet = 0;
            var log = new System.Text.StringBuilder();
            var spots = new List<float3>();
            var keepClear = new List<float3>();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel != 0 || cave.NodeCount == 0)
                    continue;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                // None in frozen or molten caves (a style that never drips).
                if (style != null && (style.LavaPools || style.Drips <= 0f))
                    continue;
                var rng = new Random(math.hash(new uint2(cave.Key, 0x7C1Cu)) | 1u);

                // Wet spots: the cave's water pools and lakes, along its streams, and its river chambers.
                spots.Clear();
                for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount; p++)
                    if (extras.Pools[p].Lava < 0.5f)
                        spots.Add(extras.Pools[p].Centre);
                for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
                {
                    var stream = extras.Streams[s];
                    for (int k = stream.FirstPoint; k < stream.FirstPoint + stream.PointCount; k += 4)
                        spots.Add(extras.StreamPoints[k].Position);
                }
                foreach (var chamber in extras.Chambers)
                    if (chamber.Cave == c)
                    {
                        spots.Add(chamber.A);
                        spots.Add(chamber.B);
                        spots.Add((chamber.A + chamber.B) * 0.5f);
                    }
                var isWet = spots.Count > 0;

                // Lamps and frames in this cave (by its box), which a trickle keeps clear of.
                keepClear.Clear();
                foreach (var plant in extras.Plants)
                    if (math.all(plant.Position >= cave.Min - 2f) && math.all(plant.Position <= cave.Max + 2f))
                        keepClear.Add(plant.Position);

                var want = isWet ? rng.NextInt(WetTrickles, WetTrickles + 3) : rng.NextInt(DryTrickles, DryTrickles + 2);
                var first = extras.Trickles.Count;
                for (int attempt = 0; attempt < want * TrickleTries && extras.Trickles.Count - first < want; attempt++)
                {
                    // Every draw of the try first, so a miss never shifts the next try's draws.
                    var byWater = rng.NextFloat();
                    var spot = rng.NextInt(math.max(spots.Count, 1));
                    var node = cave.FirstNode + rng.NextInt(cave.NodeCount);
                    var angle = rng.NextFloat(math.PI * 2f);
                    var shift = rng.NextFloat2(-1f, 1f);
                    var height = rng.NextFloat();
                    var shape = rng.NextFloat();
                    var threads = rng.NextInt(2, 5);
                    var size = rng.NextFloat();
                    var flow = rng.NextFloat(0.4f, 1f);
                    var seed = rng.NextFloat();

                    float3 from;
                    if (isWet && byWater < 0.65f)
                        from = spots[spot] + new float3(shift.x, 0f, shift.y) * 2f;
                    else
                    {
                        if (kinds[node] == CaveNodeKind.Shaft)
                            continue;
                        from = nodes[node].Position + new float3(shift.x, 0f, shift.y) * nodes[node].Radius * cave.Profile.x * 0.4f;
                    }
                    var dir = new float2(math.cos(angle), math.sin(angle));
                    var pick = shape < 0.3f ? TrickleKind.Thread : shape < 0.55f ? TrickleKind.Fan : shape < 0.78f ? TrickleKind.Sheet : TrickleKind.Overhang;
                    if (TryTrickle(source, extras, c, cave, from, dir, height, pick, threads, size, flow, seed, keepClear, out var trickle))
                    {
                        extras.Trickles.Add(trickle);
                        byKind[(int)trickle.Kind]++;
                    }
                }
                var placed = extras.Trickles.Count - first;
                if (isWet) { wetCaves++; wet += placed; }
                else { dryCaves++; dry += placed; }
                if (placed > 0)
                    log.Append($" {c}{(isWet ? "w" : "d")}:{placed}");
            }
            UnityEngine.Debug.Log($"[cave-trickles] kept {extras.Trickles.Count}: dry caves {dryCaves} ({dry}), wet caves {wetCaves} ({wet}); " +
                                  $"thread {byKind[0]}, fan {byKind[1]}, sheet {byKind[2]}, overhang {byKind[3]}; misses: floor {TrickleMisses[0]}, " +
                                  $"wall {TrickleMisses[1]}, steep {TrickleMisses[2]}, low {TrickleMisses[3]}, broken {TrickleMisses[4]}, crowded {TrickleMisses[5]}; per cave{log}");
        }

        static bool TrickleMiss(int why)
        {
            TrickleMisses[why]++;
            return false;
        }

        /// <summary>
        /// Tries one trickle: from a point in the cave's air, across to the wall at a slit height drawn between SlitLow
        /// and SlitHigh over the floor (under the roof), where the wall must be steep, face back the way it was found,
        /// and run on down to the floor without breaks (or, for an overhang, recede below the slit with clear air under
        /// its lip). The water lands at the wall's foot, on cave water when there is some right there.
        /// </summary>
        static bool TryTrickle(in TerrainSource source, Extras extras, int c, in CaveData cave, float3 from, float2 dir, float height,
                               TrickleKind pick, int threads, float size, float flow, float seed, List<float3> keepClear, out CaveTrickle trickle)
        {
            trickle = default;
            var up = new float3(0f, 1f, 0f);
            var floor = source.FloorUnder(from + up * 0.5f, out var inCave);
            if (!inCave)
                return TrickleMiss(0);
            var baseY = WaterOver(source, new float3(from.x, floor, from.z), floor);
            var hip = new float3(from.x, baseY + 1f, from.z);
            if (source.CaveDistance(hip) >= 0f)
                return TrickleMiss(0);
            var top = math.min(source.CeilingOver(hip) - 0.5f, baseY + SlitHigh);
            if (top - baseY < SlitLow)
                return TrickleMiss(3);
            var eye = new float3(hip.x, math.lerp(baseY + SlitLow, top, height), hip.z);
            if (source.CaveDistance(eye) >= 0f)
                return TrickleMiss(1);
            var t = WallAlong(source, eye, dir, 14f);
            if (t <= 0f)
                return TrickleMiss(1);
            var slit = eye + new float3(dir.x, 0f, dir.y) * t;
            var normal = RockNormal(source, slit);
            if (math.abs(normal.y) > TrickleSteep)
                return TrickleMiss(2);
            var flat = math.length(normal.xz);
            if (flat < 1e-3f)
                return TrickleMiss(2);
            var outward = normal.xz / flat;
            if (math.dot(outward, -dir) < 0.4f)
                return TrickleMiss(2);
            var out3 = new float3(outward.x, 0f, outward.y);
            var along = new float3(-outward.y, 0f, outward.x);

            // The foot of the wall, and what's there (cave water or the floor).
            var footProbe = slit + out3 * 0.35f;
            var footFloor = source.FloorUnder(footProbe, out inCave);
            if (!inCave)
                return TrickleMiss(0);
            var land = new float3(footProbe.x, WaterOver(source, new float3(footProbe.x, footFloor, footProbe.z), footFloor), footProbe.z);
            var drop = slit.y - land.y;
            if (drop < TrickleMinDrop)
                return TrickleMiss(3);

            // The wall below the slit: how far it recedes (+) or bulges out (-) from the slit's face, row by row.
            var wallOk = true;
            var overhangOk = false;
            var last = 0f;
            for (int k = 1; k <= 8; k++)
            {
                var y = slit.y - drop * k / 8.5f;
                var probe = new float3(slit.x, y, slit.z) + out3 * 1.2f;
                var d = source.CaveDistance(probe) >= 0f ? 0f : WallAlong(source, probe, -outward, 2.4f);
                var recede = d > 0f ? d - 1.2f : -1.2f;
                if (k == 1)
                    overhangOk = recede >= 0.3f;
                if (recede < -0.35f || recede > 0.6f || k > 1 && math.abs(recede - last) > 0.3f)
                    wallOk = false;
                last = recede;
            }
            var lip = slit + out3 * 0.08f;
            if (overhangOk)
            {
                // Clear air all the way down from the lip.
                var lipFloor = source.FloorUnder(lip, out inCave);
                overhangOk = inCave && slit.y - lipFloor >= TrickleMinDrop;
                for (var y = lip.y - 0.2f; overhangOk && y > lipFloor + 0.1f; y -= 0.2f)
                    overhangOk = source.CaveDistance(new float3(lip.x, y, lip.z)) < 0f;
            }
            var kind = pick;
            if (kind == TrickleKind.Overhang && !overhangOk)
                kind = TrickleKind.Thread;
            if (kind != TrickleKind.Overhang && !wallOk)
                kind = overhangOk ? TrickleKind.Overhang : kind;
            if (kind != TrickleKind.Overhang && !wallOk)
                return TrickleMiss(4);
            if (kind == TrickleKind.Overhang)
            {
                var lipFloor = source.FloorUnder(lip, out _);
                land = new float3(lip.x, WaterOver(source, new float3(lip.x, lipFloor, lip.z), lipFloor), lip.z);
                drop = slit.y - land.y;
            }

            var width = kind switch
            {
                TrickleKind.Thread => math.lerp(0.25f, 0.5f, size),
                TrickleKind.Fan => math.lerp(0.45f, 0.9f, size),
                TrickleKind.Sheet => math.lerp(0.6f, 1.1f, size),
                _ => math.lerp(0.2f, 0.5f, size)
            };
            // Rock behind both ends of the slit and air in front of them.
            for (int e = -1; e <= 1; e += 2)
            {
                var end = slit + along * (e * width * 0.5f);
                if (source.CaveDistance(end + out3 * 0.12f) >= 0f || source.CaveDistance(end - out3 * 0.3f) < 0f)
                    return TrickleMiss(4);
            }

            // Clear of the other trickles, the lamps and frames, and the rooms (dressed walls).
            for (int i = 0; i < extras.Trickles.Count; i++)
                if (math.distance(extras.Trickles[i].Slit, slit) < 3.5f)   // sim: bake spacing, not a view range
                    return TrickleMiss(5);
            foreach (var p in keepClear)
                if (math.distance(p.xz, slit.xz) < 1.4f && p.y > land.y - 0.5f && p.y < slit.y + 0.5f)   // sim: bake spacing
                    return TrickleMiss(5);
            for (int r = cave.FirstRoom; r < cave.FirstRoom + cave.RoomCount; r++)
            {
                var room = extras.Rooms[r];
                var q = slit.xz - room.Origin.xz;
                var z = math.dot(q, room.F);
                var x = math.dot(q, new float2(-room.F.y, room.F.x));
                if (z > -1.5f && z < room.HZ * 2f + 1.5f && math.abs(x) < room.HX + 1.5f)
                    return TrickleMiss(5);
            }

            var onWater = land.y > source.FloorUnder(land + up * 0.05f, out _) + 0.02f;
            trickle = new CaveTrickle
            {
                Cave = c,
                Slit = slit,
                Out = outward,
                Width = width,
                Drop = drop,
                Land = land,
                Puddle = onWater ? 0f : math.lerp(0.3f, 0.7f, math.frac(seed * 7.31f)) + width * 0.3f,
                Kind = kind,
                Threads = kind == TrickleKind.Fan ? threads : kind == TrickleKind.Overhang ? math.max(threads - 1, 1) : 1,
                Flow = flow,
                Seed = seed
            };
            return true;
        }

        /// <summary>The cave water's surface over a floor point when it is higher, else the floor.</summary>
        static float WaterOver(in TerrainSource source, float3 onFloor, float floor)
        {
            var g = source.SampleGround(onFloor + new float3(0f, 0.05f, 0f));
            return g.CaveWater && g.WaterSurface > floor ? g.WaterSurface : floor;
        }

        /// <summary>How far along a flat direction from a point in the air the rock starts (to 2 cm), or 0 if not within reach.</summary>
        internal static float WallAlong(in TerrainSource source, float3 from, float2 dir, float reach)
        {
            var step = new float3(dir.x, 0f, dir.y);
            for (var t = 0.1f; t < reach; t += 0.1f)
            {
                if (source.CaveDistance(from + step * t) < 0f)
                    continue;
                var lo = t - 0.1f;
                var hi = t;
                for (int i = 0; i < 3; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (source.CaveDistance(from + step * mid) >= 0f)
                        hi = mid;
                    else
                        lo = mid;
                }
                return hi;
            }
            return 0f;
        }

        /// <summary>The rock's surface normal (out into the air) at a point on it, from the distance field.</summary>
        static float3 RockNormal(in TerrainSource source, float3 p)
        {
            const float h = 0.15f;
            var g = new float3(
                source.CaveDistance(p + new float3(h, 0f, 0f)) - source.CaveDistance(p - new float3(h, 0f, 0f)),
                source.CaveDistance(p + new float3(0f, h, 0f)) - source.CaveDistance(p - new float3(0f, h, 0f)),
                source.CaveDistance(p + new float3(0f, 0f, h)) - source.CaveDistance(p - new float3(0f, 0f, h)));
            // The distance grows into the rock, so the way out is against it.
            var length = math.length(g);
            return length > 1e-6f ? -g / length : new float3(0f, 1f, 0f);
        }
    }
}
