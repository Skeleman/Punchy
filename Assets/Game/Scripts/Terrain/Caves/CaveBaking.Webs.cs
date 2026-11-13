using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-142 row 3b (#262): dangling cobwebs. Sheets stretched across the corner where a ceiling meets a wall (three
    /// anchors found by rays from a hub just off the rock), torn webs, old dusty ones with a dead leaf, and single strands
    /// or clusters hanging from the ceiling. Many in dry caves, a few in wet ones, none in hot ones; the corners of ruin
    /// rooms (and now and then a grotto or roost) get several; dark road tunnels and their niches get some, lit ones few
    /// and only well away from the lamps. Rule 22: plain records from each cave's (or room's) own seed, every draw of a
    /// try made before any test can skip it; they change no rock, so nothing placed before them moves.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>Tries per web wanted, and how close two webs may sit (m, between middles).</summary>
        const int WebTries = 8;
        const float WebSpacing = 0.9f;
        /// <summary>How far a tunnel web keeps from a lamp (m): out of its pool of light, so a niche keeps its far corners.</summary>
        const float WebLampClear = 3f;
        /// <summary>Misses by reason, for the log: ceiling, wall, ray (rock too close), air, crowded, lamp, ray (no rock in reach).</summary>
        static readonly int[] WebMisses = new int[7];

        /// <summary>Webs in natural caves and their rooms (not road tunnels: PlaceTunnelWebs does those).</summary>
        static void PlaceWebs(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                              List<CaveStyle> styles, Extras extras)
        {
            System.Array.Clear(WebMisses, 0, WebMisses.Length);
            var first = extras.Webs.Count;
            int dryCaves = 0, wetCaves = 0, hotCaves = 0, inCaves = 0, inRooms = 0;
            var log = new System.Text.StringBuilder();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel != 0 || cave.NodeCount == 0)
                    continue;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style != null && style.LavaPools)
                {
                    hotCaves++;   // the heat keeps spiders out
                    continue;
                }
                // Wetness: water trickles, water pools and streams.
                var wet = cave.StreamCount * 2;
                for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount && p < extras.Pools.Count; p++)
                    if (extras.Pools[p].Lava < 0.5f)
                        wet++;
                foreach (var t in extras.Trickles)
                    if (t.Cave == c && t.Lava < 0.5f)
                        wet++;
                if (wet == 0) dryCaves++;
                else wetCaves++;
                var before = extras.Webs.Count;

                // gameplay: 4-8 in a dry cave, 2-5 in a damp one (a little water), 1-2 in a wet one, 0-1 in a soaked one
                var rng = new Random(math.hash(new uint2(cave.Key, 0xC0B3u)) | 1u);
                var want = wet == 0 ? rng.NextInt(4, 9) : wet <= 3 ? rng.NextInt(2, 6) : wet <= 6 ? rng.NextInt(1, 3) : rng.NextInt(0, 2);
                for (int attempt = 0; attempt < want * WebTries && extras.Webs.Count - before < want; attempt++)
                {
                    // Every draw of the try first, so a miss never shifts the next try's draws.
                    var node = cave.FirstNode + rng.NextInt(cave.NodeCount);
                    var shift = rng.NextFloat2(-1f, 1f);
                    var angle = rng.NextFloat(math.PI * 2f);
                    var shape = rng.NextFloat();
                    var size = rng.NextFloat(0.6f, 1.3f);
                    var length = rng.NextFloat();
                    var jitter = rng.NextFloat(-0.3f, 0.3f);
                    var seed = rng.NextFloat();
                    if (kinds[node] == CaveNodeKind.Shaft)
                        continue;
                    var from = nodes[node].Position + new float3(shift.x, 0f, shift.y) * nodes[node].Radius * cave.Profile.x * 0.5f;
                    var kind = PickWeb(shape, 0.3f, 0.2f, 0.2f, 0.15f);
                    var d1 = new float3(math.cos(angle), 0f, math.sin(angle));
                    if (TryCaveWeb(source, extras, c, cave, from, d1, kind, size, length, jitter, seed, WebPlace.Cave, 0.15f, out var web))
                        extras.Webs.Add(web);
                }
                inCaves += extras.Webs.Count - before;

                // The cave's rooms: ruins are old and dry (3-6 webs, in their top corners), grottos and roosts 0-2; lakes none.
                var roomsFrom = extras.Webs.Count;
                foreach (var room in extras.Rooms)
                {
                    if (room.Cave != c || room.Type == (int)CaveRoomType.Lake)
                        continue;
                    var roomRng = new Random(math.hash(new uint2(room.Seed, 0xC0B4u)) | 1u);
                    var ruin = room.Type == (int)CaveRoomType.Ruin;
                    var roomWant = ruin ? roomRng.NextInt(3, 7) : roomRng.NextInt(0, 3);
                    if (wet > 3)
                        roomWant /= 2;
                    var roomBefore = extras.Webs.Count;
                    for (int attempt = 0; attempt < roomWant * WebTries && extras.Webs.Count - roomBefore < roomWant; attempt++)
                    {
                        var corner = roomRng.NextFloat();
                        var sx = roomRng.NextBool() ? 1f : -1f;
                        var far = roomRng.NextBool();
                        var along = roomRng.NextFloat();
                        var shape = roomRng.NextFloat();
                        var size = roomRng.NextFloat(0.6f, 1.4f);
                        var length = roomRng.NextFloat();
                        var jitter = roomRng.NextFloat(-0.3f, 0.3f);
                        var seed = roomRng.NextFloat();
                        var kind = ruin ? PickWeb(shape, 0.3f, 0.2f, 0.1f, 0.1f) : PickWeb(shape, 0.3f, 0.2f, 0.2f, 0.15f);
                        CaveWeb web = default;
                        var ok = false;
                        // The room kit dresses the box (corner pilasters, a cornice round the top, a grown ruin's inner
                        // walls) and the rock field can't see any of it, so room webs are laid out against the kit's own faces.
                        if (kind == WebKind.Strand || kind == WebKind.Cluster)
                        {
                            // Hanging from the ceiling somewhere along a wall, clear of the cornice and any inner wall.
                            var l = new float3(sx * (room.HX - 0.6f) * along, room.Height * 0.6f, math.lerp(0.8f, room.HZ * 2f - 0.8f, along));
                            ok = ClearOfInnerWalls(room, l, 0.4f)
                                 && TryStrand(source, extras, c, cave, room.World(l), kind, length, seed, WebPlace.Room, 0.15f, out web);
                        }
                        else if (corner < 0.6f)
                        {
                            // A top corner: the nook where the wall, the cornice's underside and a corner pilaster meet.
                            var s = size * 0.5f;
                            float z0 = PilasterOut + 0.02f, zf = far ? -1f : 1f, zb = far ? room.HZ * 2f : 0f;
                            float3 L(float x, float y, float z) => new float3(sx * (room.HX - x), room.Height - CorniceDrop - y, zb + zf * z);
                            ok = TryRoomSheet(source, extras, c, cave, room, L(0.12f, 0.02f, z0 + 0.45f * s + 0.2f), L(PilasterOut - 0.1f, 0.5f * s + 0.05f, z0),
                                              L(0.02f, 0.7f * s + 0.1f, z0 + 0.25f * s + 0.1f * (jitter + 0.3f)), kind, length, seed, out web);
                        }
                        else
                        {
                            // Along a long wall: slung from the cornice's front edge down to the wall under it.
                            var s = size * 0.5f;
                            var z = math.lerp(PilasterOut + 0.5f * s + 0.1f, room.HZ * 2f - PilasterOut - 0.5f * s - 0.1f, along);
                            float3 W(float x, float y, float dz) => new float3(sx * (room.HX - x), room.Height - CorniceDrop - y, z + dz);
                            ok = ClearOfInnerWalls(room, W(0f, 0f, 0f), 0.5f * s + 0.1f)
                                 && TryRoomSheet(source, extras, c, cave, room, W(CorniceOut - 0.03f, 0.02f, -0.45f * s - 0.1f), W(CorniceOut - 0.03f, 0.02f, 0.45f * s + 0.1f),
                                                 W(0.02f, 0.65f * s + 0.15f, jitter * 0.3f), kind, length, seed, out web);
                        }
                        if (ok)
                            extras.Webs.Add(web);
                    }
                }
                inRooms += extras.Webs.Count - roomsFrom;
                var placed = extras.Webs.Count - before;
                log.Append($" {c}{(wet == 0 ? "d" : "w" + wet)}:{placed}");
            }
            LogWebs("caves and rooms", extras, first, $"caves {inCaves} (dry {dryCaves}, wet {wetCaves}, hot {hotCaves} skipped), rooms {inRooms}", log);
        }

        /// <summary>
        /// Webs in road tunnels, after their lamps: 3-7 in a dark tunnel, 0-3 in a lit one (none within 3 m of a lamp),
        /// half of them in the niches when the tunnel has some.
        /// </summary>
        static void PlaceTunnelWebs(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments,
                                    List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            System.Array.Clear(WebMisses, 0, WebMisses.Length);
            var first = extras.Webs.Count;
            var lit = TunnelLampStyle(styles);
            var torch = lit != null ? flora.IndexOf(TunnelLamp(lit)) : -1;
            var lamps = new List<float3>();
            var niches = new List<int>();
            var bores = new List<int>();
            int dark = 0, lighted = 0;
            var log = new System.Text.StringBuilder();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel == 0 || cave.SegmentCount == 0)
                    continue;
                lamps.Clear();
                if (torch >= 0)
                    foreach (var plant in extras.Plants)
                        if (plant.Species == torch && math.all(plant.Position >= cave.Min - 1f) && math.all(plant.Position <= cave.Max + 1f))
                            lamps.Add(plant.Position);
                niches.Clear();
                bores.Clear();
                for (int s = cave.FirstSegment; s < cave.FirstSegment + cave.SegmentCount; s++)
                    (NicheSegment(nodes, segments[s]) ? niches : bores).Add(s);
                if (bores.Count == 0)
                    continue;
                // gameplay: dark tunnels are cobwebbed, lit ones swept now and then
                var rng = new Random(math.hash(new uint2(cave.Key, 0xC0B5u)) | 1u);
                var want = cave.Tunnel == 1 ? rng.NextInt(3, 8) : rng.NextInt(0, 4);
                var before = extras.Webs.Count;
                for (int attempt = 0; attempt < want * WebTries && extras.Webs.Count - before < want; attempt++)
                {
                    var inNiche = rng.NextFloat();
                    var pick = rng.NextFloat();
                    var at = rng.NextFloat(0.1f, 0.9f);
                    var side = rng.NextBool() ? 1f : -1f;
                    var shape = rng.NextFloat();
                    var size = rng.NextFloat(0.6f, 1.2f);
                    var length = rng.NextFloat();
                    var jitter = rng.NextFloat(-0.3f, 0.3f);
                    var seed = rng.NextFloat();
                    var niche = niches.Count > 0 && inNiche < 0.5f;
                    var list = niche ? niches : bores;
                    var seg = segments[list[math.min((int)(pick * list.Count), list.Count - 1)]];
                    var a = nodes[seg.A].Position;
                    var b = nodes[seg.B].Position;
                    var dir = b - a;
                    dir.y = 0f;
                    var flat = math.normalizesafe(dir, new float3(1f, 0f, 0f));
                    var across = new float3(-flat.z, 0f, flat.x) * side;
                    // In a niche, into its far end (or a side of it); in the bore, into a wall where it meets the roof.
                    var from = niche ? math.lerp(a, b, 0.6f + 0.4f * at) : math.lerp(a, b, at);
                    var d1 = niche && side > 0f ? flat : across;
                    var kind = PickWeb(shape, 0.3f, 0.25f, 0.15f, 0.15f);
                    if (!TryCaveWeb(source, extras, c, cave, from, d1, kind, size, length, jitter, seed, WebPlace.Tunnel, 0.35f, out var web))
                        continue;
                    var middle = (web.A + web.B + web.C) / 3f;
                    var nearLamp = false;
                    foreach (var lamp in lamps)
                        nearLamp |= math.distancesq(lamp, middle) < WebLampClear * WebLampClear;
                    if (nearLamp)
                    {
                        WebMisses[5]++;
                        continue;
                    }
                    extras.Webs.Add(web);
                }
                var placed = extras.Webs.Count - before;
                if (cave.Tunnel == 1) dark += placed;
                else lighted += placed;
                if (placed > 0)
                    log.Append($" {c}t{cave.Tunnel}:{placed}");
            }
            LogWebs("tunnels", extras, first, $"dark {dark}, lit {lighted}", log);
        }

        /// <summary>A web kind from a draw, given the shares of corner, torn, strand and cluster (dusty takes the rest).</summary>
        static WebKind PickWeb(float shape, float corner, float torn, float strand, float cluster)
        {
            if (shape < corner) return WebKind.Corner;
            shape -= corner;
            if (shape < torn) return WebKind.Torn;
            shape -= torn;
            if (shape < strand) return WebKind.Strand;
            shape -= strand;
            return shape < cluster ? WebKind.Cluster : WebKind.Dusty;
        }

        /// <summary>A web in a passage near a point: strands hang from the ceiling over it, sheets go where the ceiling
        /// above it meets the wall along d1.</summary>
        static bool TryCaveWeb(in TerrainSource source, Extras extras, int c, in CaveData cave, float3 from, float3 d1, WebKind kind,
                               float size, float length, float jitter, float seed, WebPlace place, float baseSway, out CaveWeb web)
        {
            web = default;
            if (source.CaveDistance(from) >= -0.05f)
            {
                WebMisses[3]++;
                return false;
            }
            if (kind == WebKind.Strand || kind == WebKind.Cluster)
                return TryStrand(source, extras, c, cave, from, kind, length, seed, place, baseSway, out web);
            // Up to just under the ceiling, then out to the wall.
            var roof = RockAlong(source, from, new float3(0f, 1f, 0f), 8f);
            if (roof < 0.4f)
            {
                WebMisses[0]++;
                return false;
            }
            var under = from + new float3(0f, roof - 0.4f, 0f);
            var wall = RockAlong(source, under, d1, 8f);
            if (wall < 0f)
            {
                WebMisses[1]++;
                return false;
            }
            var hub = under + d1 * math.max(wall - 0.35f * size, 0f);
            return TryCorner(source, extras, c, cave, hub, d1, kind, size, length, jitter, seed, place, baseSway, out web);
        }

        /// <summary>A sheet from a hub just off where a ceiling meets a wall (d1 points at the wall).</summary>
        static bool TryCorner(in TerrainSource source, Extras extras, int c, in CaveData cave, float3 hub, float3 d1, WebKind kind,
                              float size, float length, float jitter, float seed, WebPlace place, float baseSway, out CaveWeb web)
        {
            var up = new float3(0f, 1f, 0f);
            var d2 = new float3(-d1.z, 0f, d1.x);
            return TrySheet(source, extras, c, cave, hub, up + d1 + d2 * 0.8f, up + d1 - d2 * 0.8f, d1 - up * 0.7f + d2 * jitter, kind, size, length, seed, place, baseSway, out web);
        }

        /// <summary>
        /// A sheet anchored where three rays from the hub meet rock (each within reach of the web's size), kept only when
        /// it spans a fair triangle whose middle and edges are in the air.
        /// </summary>
        static bool TrySheet(in TerrainSource source, Extras extras, int c, in CaveData cave, float3 hub, float3 r1, float3 r2, float3 r3,
                             WebKind kind, float size, float length, float seed, WebPlace place, float baseSway, out CaveWeb web)
        {
            web = default;
            if (source.CaveDistance(hub) >= -0.02f)
            {
                WebMisses[3]++;
                return false;
            }
            var reach = 1.8f * size;
            var t1 = RockAlong(source, hub, math.normalize(r1), reach);
            var t2 = RockAlong(source, hub, math.normalize(r2), reach);
            var t3 = RockAlong(source, hub, math.normalize(r3), reach);
            if (t1 < 0f || t2 < 0f || t3 < 0f)
            {
                WebMisses[6]++;
                return false;
            }
            if (t1 < 0.12f || t2 < 0.12f || t3 < 0.12f)
            {
                WebMisses[2]++;
                return false;
            }
            // Anchored just short of the rock, so the threads meet it rather than sink in.
            var a = hub + math.normalize(r1) * (t1 - 0.02f);
            var b = hub + math.normalize(r2) * (t2 - 0.02f);
            var cc = hub + math.normalize(r3) * (t3 - 0.02f);
            var middle = (a + b + cc) / 3f;
            var area = math.length(math.cross(b - a, cc - a)) * 0.5f;
            if (area < 0.03f || math.distance(a, b) < 0.15f || math.distance(b, cc) < 0.15f || math.distance(cc, a) < 0.15f
                || source.CaveDistance(middle) >= -0.01f
                || source.CaveDistance(math.lerp((a + b) * 0.5f, middle, 0.15f)) >= 0f
                || source.CaveDistance(math.lerp((b + cc) * 0.5f, middle, 0.15f)) >= 0f
                || source.CaveDistance(math.lerp((cc + a) * 0.5f, middle, 0.15f)) >= 0f)
            {
                WebMisses[3]++;
                return false;
            }
            if (Crowded(extras, middle))
                return false;
            // Torn and dusty webs trail a loose strand; whole ones don't.
            var strand = kind == WebKind.Torn ? 0.2f + 0.5f * length : kind == WebKind.Dusty ? 0.1f + 0.3f * length : 0f;
            web = new CaveWeb { A = a, B = b, C = cc, Cave = c, Kind = kind, Place = place, Sway = Draught(extras, c, cave, middle, baseSway), Strand = strand, Seed = seed };
            return true;
        }

        /// <summary>A strand (or cluster) hanging from the ceiling over a point, with clear air under it.</summary>
        static bool TryStrand(in TerrainSource source, Extras extras, int c, in CaveData cave, float3 from, WebKind kind, float length,
                              float seed, WebPlace place, float baseSway, out CaveWeb web)
        {
            web = default;
            if (source.CaveDistance(from) >= -0.05f)
            {
                WebMisses[3]++;
                return false;
            }
            var roof = RockAlong(source, from, new float3(0f, 1f, 0f), 8f);
            if (roof < 0.3f)
            {
                WebMisses[0]++;
                return false;
            }
            var anchor = from + new float3(0f, roof - 0.02f, 0f);
            var hang = math.lerp(0.3f, 1.4f, length);
            // From the air under it (from the anchor itself FloorUnder would start in the roof's rock).
            var floor = source.FloorUnder(from - new float3(0f, 0.6f, 0f), out _);
            if (anchor.y - floor < hang + 0.6f || source.CaveDistance(anchor - new float3(0f, hang, 0f)) >= -0.05f)
            {
                WebMisses[3]++;
                return false;
            }
            if (Crowded(extras, anchor - new float3(0f, hang * 0.5f, 0f)))
                return false;
            web = new CaveWeb { A = anchor, B = anchor, C = anchor, Cave = c, Kind = kind, Place = place, Sway = Draught(extras, c, cave, anchor, baseSway), Strand = hang, Seed = seed };
            return true;
        }

        /// <summary>The room kit's trims (CaveRoomKit): how far a corner pilaster stands out from both walls, how far the
        /// cornice reaches out from the walls and how far its underside hangs below the ceiling (m).</summary>
        const float PilasterOut = 0.44f, CorniceOut = 0.26f, CorniceDrop = 0.34f;
        /// <summary>Half the thickness of a grown ruin's inner walls (CaveRoomKit.InnerWall).</summary>
        const float RuinInnerWall = 0.35f;

        /// <summary>Whether a room-local point keeps margin metres clear of a grown ruin's inner walls (the cross wall at
        /// the room's middle, the long wall down the back half); always true in other rooms.</summary>
        static bool ClearOfInnerWalls(in CaveRoom room, float3 local, float margin)
        {
            if (!CaveRoomKit.RuinCells(room))
                return true;
            var t = RuinInnerWall + margin;
            return math.abs(local.z - room.HZ) > t && (local.z < room.HZ || math.abs(local.x) > t);
        }

        /// <summary>A room web from three room-local anchors on the kit's faces, kept when its middle is in the air.</summary>
        static bool TryRoomSheet(in TerrainSource source, Extras extras, int c, in CaveData cave, in CaveRoom room, float3 la, float3 lb, float3 lc,
                                 WebKind kind, float length, float seed, out CaveWeb web)
        {
            web = default;
            float3 a = room.World(la), b = room.World(lb), cc = room.World(lc), middle = (a + b + cc) / 3f;
            if (!ClearOfInnerWalls(room, (la + lb + lc) / 3f, 0.3f) || source.CaveDistance(middle) >= -0.05f)
            {
                WebMisses[3]++;
                return false;
            }
            if (Crowded(extras, middle))
                return false;
            var strand = kind == WebKind.Torn ? 0.2f + 0.5f * length : kind == WebKind.Dusty ? 0.1f + 0.3f * length : 0f;
            web = new CaveWeb { A = a, B = b, C = cc, Cave = c, Kind = kind, Place = WebPlace.Room, Sway = Draught(extras, c, cave, middle, 0.15f), Strand = strand, Seed = seed };
            return true;
        }

        static bool Crowded(Extras extras, float3 middle)
        {
            foreach (var w in extras.Webs)
            {
                var other = w.Kind == WebKind.Strand || w.Kind == WebKind.Cluster ? w.A - new float3(0f, w.Strand * 0.5f, 0f) : (w.A + w.B + w.C) / 3f;
                if (math.distancesq(other, middle) < WebSpacing * WebSpacing)
                {
                    WebMisses[4]++;
                    return true;
                }
            }
            return false;
        }

        /// <summary>How much a draught stirs a web: a little everywhere, more within 12 m of the cave's mouth and 6 m of a vent.</summary>
        static float Draught(Extras extras, int c, in CaveData cave, float3 at, float baseSway)
        {
            var sway = baseSway + math.saturate(1f - math.distance(at, cave.Mouth) / 12f) * 0.8f;
            foreach (var vent in extras.Vents)
                if (vent.Cave == c)
                    sway += math.saturate(1f - math.distance(at, vent.Position) / 6f) * 0.6f;
            return math.saturate(sway);
        }

        /// <summary>How far along a ray from a point in the air the rock starts (to a few cm), or -1 past reach.</summary>
        static float RockAlong(in TerrainSource source, float3 from, float3 dir, float reach)
        {
            for (var t = 0.05f; t <= reach; t += 0.05f)   // collision: bake ray to the rock, not LOD
            {
                if (source.CaveDistance(from + dir * t) < 0f)
                    continue;
                float lo = t - 0.05f, hi = t;
                for (int i = 0; i < 3; i++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (source.CaveDistance(from + dir * mid) >= 0f)
                        hi = mid;
                    else
                        lo = mid;
                }
                return hi;
            }
            return -1f;
        }

        static void LogWebs(string what, Extras extras, int first, string counts, System.Text.StringBuilder perCave)
        {
            var byKind = new int[5];
            for (int i = first; i < extras.Webs.Count; i++)
                byKind[(int)extras.Webs[i].Kind]++;
            UnityEngine.Debug.Log($"[cave-webs] {what}: kept {extras.Webs.Count - first}: {counts}; corner {byKind[0]}, torn {byKind[1]}, strand {byKind[2]}, " +
                                  $"cluster {byKind[3]}, dusty {byKind[4]}; misses: ceiling {WebMisses[0]}, wall {WebMisses[1]}, ray near {WebMisses[2]}, far {WebMisses[6]}, " +
                                  $"air {WebMisses[3]}, crowded {WebMisses[4]}, lamp {WebMisses[5]}; per cave{perCave}");
        }
    }
}
