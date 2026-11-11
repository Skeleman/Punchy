using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-110: road tunnels. Where RoadBaking bored a road through a crest instead of cutting it (its tunnel roads, Road
    /// 3, which leave the hill standing), a straight-graded passage follows the road from portal to portal: a cave whose
    /// nodes sit on the road's line, its flat floor at the road's level and wide enough for the road and a margin. The
    /// cuttings either side lead in, and the portals open where the hill meets the passage, as any cave mouth does.
    /// Tunnels are caves appended after the landmarks' (with no chambers, rooms, water or rock decorations), so natural
    /// caves keep their indices and seeds. Each tunnel's own roll keeps it dark, hangs lamps along it, or lamps and
    /// timber frames.
    /// </summary>
    public static partial class CaveBaking
    {
        /// <summary>A tunnel's height for its width (its Profile.y): a low arch, as bored road tunnels are, so a shallow hill still roofs it.</summary>
        const float TunnelArch = 0.8f;
        /// <summary>How far past each portal's road end a tunnel's passage runs (metres).</summary>
        const float TunnelOverrun = 1.5f;
        /// <summary>Bores shorter than this (metres) are never lit: daylight reaches through from the portals.</summary>
        const float TunnelDaylit = 25f;
        /// <summary>W-138 row 4: bores at least this long (metres) are always lit; no one walks half a kilometre blind.</summary>
        const float TunnelAlwaysLit = 300f;
        /// <summary>W-138 row 4: bores at least this long (metres) may have a side niche with a shrine.</summary>
        const float TunnelNicheBore = 150f;
        /// <summary>The niche's radius (metres): about 2.2 m high at the back, deep enough for a shrine.</summary>
        const float TunnelNicheRadius = 1.9f;

        /// <summary>Each tunnel road's line, portal to portal (its stretches' ends, in order), and its width.</summary>
        static List<(List<float3> points, float width)> TunnelRuns(in TerrainSource source)
        {
            var runs = new List<(List<float3> points, float width)>();
            if (!source.Settlements.IsCreated)
                return runs;
            ref var streets = ref source.Settlements.Value.Streets;
            List<float3> current = null;
            for (int i = 0; i < streets.Length; i++)
            {
                ref var s = ref streets[i];
                if (s.Road != SettlementSampling.TunnelRoad)
                {
                    current = null;
                    continue;
                }
                if (current != null && math.distancesq(current[current.Count - 1], s.A) < 1e-4f)
                {
                    current.Add(s.B);
                    continue;
                }
                current = new List<float3> { s.A, s.B };
                runs.Add((current, s.Width));
            }
            return runs;
        }

        static void AddTunnels(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<CaveStyle> styles,
                               List<(List<float3> points, float width)> runs, List<CaveData> caves, List<CaveNode> nodes,
                               List<CaveNodeKind> kinds, List<CaveSegment> segments)
        {
            var lit = TunnelLampStyle(styles);
            foreach (var (points, width) in runs)
            {
                var first = points[0];
                var last = points[points.Count - 1];
                var key = BiomeSampling.Scramble(math.hash(new int4((int2)math.floor(first.xz * 4f), (int2)math.floor(last.xz * 4f))) ^ 0x7B0E0110u);
                var rng = new Random(key | 1u);
                // Dark, lamps, or lamps and frames (W-110: lamps in some tunnels, dark in others). A short bore stays
                // dark, the daylight at its portals reaching through; a longer one rolls.
                var roll = rng.NextFloat();
                var bore = 0f;
                for (int k = 1; k < points.Count; k++)
                    bore += math.distance(points[k - 1].xz, points[k].xz);
                var tunnel = lit == null || bore < TunnelDaylit || (roll < 0.25f && bore < TunnelAlwaysLit) ? 1 : roll < 0.65f ? 2 : 3;
                // The rock: a style for the biome it runs under, as a cave there would have; a framed tunnel takes the
                // dug style (timbered, even).
                var style = tunnel == 3 ? lit : BiomeStyle(source, palette, styles, (first.xz + last.xz) * 0.5f, ref rng);
                // Wide enough that the road (and half a metre more each side) is all floor, at least 3 m (head room is
                // 1.45 times it). Shared with RoadBaking, which sizes each portal's cutting from it.
                var radius = SettlementSampling.TunnelRadius(width);
                // Past each portal by a little, so the passage's rounded end lies in the cutting and the portal opens
                // full height instead of narrowing to the end cap.
                var line = new List<float3>(points.Count + 2) { first + math.normalizesafe(first - points[1]) * TunnelOverrun };
                line.AddRange(points);
                line.Add(last + math.normalizesafe(last - points[points.Count - 2]) * TunnelOverrun);
                // W-138 row 4: a long bore may have a side niche (a shrine's alcove) somewhere in its middle, where the
                // hill over it is deep. A point split into the line there, and a smaller node out to one side. Its own
                // roll (the tunnel's draws above are as they were).
                var niche = Niche(source, points, radius, key, out var nicheSplit);
                if (niche.w > 0f)
                    line.Insert((int)niche.w + 1, nicheSplit);
                var cave = caves.Count;
                var firstNode = nodes.Count;
                var firstSegment = segments.Count;
                // The niche's node goes right after the split point, so the first and last nodes (and the ones one in
                // from them) stay the portals' (the portal kit, the daylight and the floor read them), and its segment
                // after all the passage's.
                var lineNodes = new int[line.Count];
                var nicheNode = -1;
                for (int k = 0; k < line.Count; k++)
                {
                    lineNodes[k] = nodes.Count;
                    nodes.Add(new CaveNode { Position = line[k] + new float3(0f, radius * TunnelArch * 0.45f, 0f), Radius = radius });
                    kinds.Add(CaveNodeKind.Passage);
                    if (niche.w > 0f && k == (int)niche.w + 1)
                    {
                        nicheNode = nodes.Count;
                        nodes.Add(new CaveNode { Position = niche.xyz + new float3(0f, TunnelNicheRadius * TunnelArch * 0.45f, 0f), Radius = TunnelNicheRadius });
                        kinds.Add(CaveNodeKind.Passage);
                    }
                }
                for (int k = 1; k < line.Count; k++)
                    segments.Add(new CaveSegment { A = lineNodes[k - 1], B = lineNodes[k], Cave = cave });
                if (nicheNode >= 0)
                    segments.Add(new CaveSegment { A = nicheNode - 1, B = nicheNode, Cave = cave });
                // Bored rock: smoother than a natural cave, a little blocky where it's broken away overhead.
                var wall = new float4(rng.NextFloat(0.25f, 0.45f), 1f, 0f, 1.6f);
                var rock = new float4(rng.NextFloat(0.15f, 0.35f), 1f, 0f, rng.NextFloat(0.1f, 0.35f));
                float3 min = float.MaxValue, max = float.MinValue;
                for (int n = firstNode; n < nodes.Count; n++)
                {
                    var reach = radius + 2.5f + CaveSampling.NoiseReach * wall.x + CaveSampling.JaggedReach * rock.x;
                    min = math.min(min, nodes[n].Position - reach);
                    max = math.max(max, nodes[n].Position + reach);
                }
                caves.Add(new CaveData
                {
                    FirstNode = firstNode,
                    NodeCount = nodes.Count - firstNode,
                    FirstSegment = firstSegment,
                    SegmentCount = segments.Count - firstSegment,
                    Min = min,
                    Max = max,
                    Mouth = nodes[firstNode].Position,
                    Seed = rng.NextFloat(-50f, 50f),
                    Key = key,
                    Style = style != null ? styles.IndexOf(style) + 1 : 0,
                    Profile = new float2(1f, TunnelArch),
                    Wall = wall,
                    Rock = rock,
                    Facet = float2.zero,
                    Tunnel = tunnel,
                    MainEnds = new int3(-1)   // (a road, not a cave: no main paths to walk)
                });
            }
        }

        /// <summary>
        /// W-138 row 4: where a long bore's side niche goes. xyz the niche's floor point (out to one side of the road),
        /// w the index of the road point after it (0 for none); split is the point on the road's line it opens off.
        /// Only somewhere in the bore's middle third whose hill is deep enough that the niche stays well under ground.
        /// </summary>
        static float4 Niche(in TerrainSource source, List<float3> points, float radius, uint key, out float3 split)
        {
            split = float3.zero;
            var bore = 0f;
            for (int k = 1; k < points.Count; k++)
                bore += math.distance(points[k - 1].xz, points[k].xz);
            if (bore < TunnelNicheBore)
                return float4.zero;
            var rng = new Random(math.hash(new uint2(key, 0x51C4Eu)) | 1u);
            if (rng.NextFloat() < 0.2f)
                return float4.zero;
            var side = rng.NextBool() ? 1f : -1f;
            // A few tries along the middle third: the first with 6 m of rock over the niche's back.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var at = bore * rng.NextFloat(0.33f, 0.67f);
                for (int k = 1; k < points.Count; k++)
                {
                    var a = points[k - 1];
                    var b = points[k];
                    var length = math.distance(a.xz, b.xz);
                    if (at > length)
                    {
                        at -= length;
                        continue;
                    }
                    // Clear of the road's own points (about 10 m apart), so the split doesn't make a sliver.
                    if (at < 1.5f || at > length - 1.5f)
                        break;
                    var q = math.lerp(a, b, at / length);
                    var along = math.normalize((b - a).xz);
                    var across = new float2(-along.y, along.x) * side;
                    var back = q + new float3(across.x, 0f, across.y) * (radius + 0.5f);
                    var reach = back.xz + across * TunnelNicheRadius;
                    if (source.SampleHeight(back.xz) - back.y < radius * 1.2f + 6f || source.SampleHeight(reach) - back.y < radius * 1.2f + 6f)
                        break;
                    split = q;
                    return new float4(back, k);
                }
            }
            return float4.zero;
        }

        /// <summary>The style whose lamps and frames tunnels hang (the first with a torch), or none.</summary>
        static CaveStyle TunnelLampStyle(List<CaveStyle> styles)
        {
            foreach (var s in styles)
                if (s != null && s.Torch != null)
                    return s;
            return null;
        }

        /// <summary>
        /// A style listed for the biome at a spot (by the roll when several are), else one for anywhere, else none.
        /// W-138 row 3b: only plain rock. A glowing or glossy style (ice, crystal, lava, fungus) lit the whole bore
        /// blue-white from the portal in; a road tunnel goes dark a few metres in, bar the daylight on its floor.
        /// </summary>
        static CaveStyle BiomeStyle(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<CaveStyle> styles,
                                    float2 position, ref Random rng)
        {
            var biome = Dominant(source.SampleGround(position).Biomes);
            var here = biome >= 0 && biome < palette.Count ? palette[biome] : null;
            var matches = new List<CaveStyle>();
            foreach (var s in styles)
                if (s != null && s.Torch == null && Plain(s) && here != null && s.Biomes.Contains(here))
                    matches.Add(s);
            if (matches.Count == 0)
                foreach (var s in styles)
                    if (s != null && s.Torch == null && Plain(s) && s.Biomes.Count == 0)
                        matches.Add(s);
            return matches.Count > 0 ? matches[rng.NextInt(matches.Count)] : null;
        }

        /// <summary>W-138 row 4c: the lamp a road tunnel hangs: the style's own tunnel lamp (a dim wall lantern), else its torch.</summary>
        static Punchy.Flora.FloraSpecies TunnelLamp(CaveStyle s) => s.TunnelLamp != null ? s.TunnelLamp : s.Torch;

        static bool Plain(CaveStyle s) => s.Glow <= 0f && s.GlowEverywhere <= 0f && s.Gloss <= 0.5f;

        /// <summary>
        /// Lamps on alternate walls at shoulder height along lit tunnels (each its own spacing, some hung in facing
        /// pairs), and timber frames across framed ones: the dug style's torch and frame plants.
        /// </summary>
        static void PlaceTunnelFittings(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments,
                                        List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras, List<float3> keepClear)
        {
            var lit = TunnelLampStyle(styles);
            if (lit == null)
                return;
            var torch = flora.IndexOf(TunnelLamp(lit));
            var frame = lit.Frame != null ? flora.IndexOf(lit.Frame) : -1;
            var terrain = source;   // local functions can't capture an in parameter
            float Wall(float3 from, float2 dir)
            {
                for (var t = 0.3f; t < 12f; t += 0.1f)
                    if (terrain.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t) >= 0f)
                        return t;
                return 0f;
            }
            void Lamp(float3 shoulder, float2 dir, ref Random rng)
            {
                var t = terrain.CaveDistance(shoulder) < 0f ? Wall(shoulder, dir) : 0f;
                if (t <= 0f || torch < 0)
                    return;
                // Not on a marker stone or across the niche's mouth.
                foreach (var k in keepClear)
                    if (math.distancesq(k.xz, shoulder.xz) < 2.5f * 2.5f)
                        return;
                var at = shoulder + new float3(dir.x, 0f, dir.y) * (t - 0.12f);
                extras.Plants.Add(new CavePlant { Position = at, Yaw = math.atan2(dir.x, -dir.y), Scale = 1f, Species = torch, Variant = rng.NextInt(3), Random = rng.NextFloat() });
            }
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel < 2)
                    continue;
                var rng = new Random(math.hash(new uint2(cave.Key, 0xF177u)) | 1u);
                var spacing = rng.NextFloat(7f, 13f);
                var paired = rng.NextFloat() < 0.3f;
                var frameSpacing = lit.FrameSpacing * rng.NextFloat(0.9f, 1.4f);
                var side = rng.NextBool() ? 1f : -1f;
                // The portals stay clear by a few metres (daylight there), so lamps start inside.
                var travelled = 0f;
                var nextLamp = rng.NextFloat(4f, 7f);
                var nextFrame = frameSpacing * 0.5f + 2f;
                var total = 0f;
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                    if (!NicheSegment(nodes, segments[si]))
                        total += math.distance(nodes[segments[si].A].Position.xz, nodes[segments[si].B].Position.xz);
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
                    if (NicheSegment(nodes, segments[si]))
                        continue;
                    var a = nodes[segments[si].A].Position;
                    var b = nodes[segments[si].B].Position;
                    var length = math.distance(a.xz, b.xz);
                    if (length < 0.5f)
                        continue;
                    var along = math.normalize((b - a).xz);
                    var across = new float2(-along.y, along.x);
                    var yaw = math.atan2(-along.x, along.y);
                    var floorOffset = nodes[segments[si].A].Radius * cave.Profile.y * 0.45f;
                    for (var d = 0f; d < length; d += 0.5f)
                    {
                        var at = travelled + d;
                        if (at > total - 3f)
                            break;
                        var q = math.lerp(a, b, d / length);
                        var floor = q.y - floorOffset;
                        if (at >= nextLamp)
                        {
                            var shoulder = new float3(q.x, floor + 1.7f, q.z);
                            Lamp(shoulder, across * side, ref rng);
                            if (paired)
                                Lamp(shoulder, -across * side, ref rng);
                            else
                                side = -side;
                            nextLamp = at + spacing * rng.NextFloat(0.85f, 1.15f);
                        }
                        if (cave.Tunnel == 3 && frame >= 0 && at >= nextFrame)
                        {
                            nextFrame = at + frameSpacing;
                            var hip = new float3(q.x, floor + 0.9f, q.z);
                            var left = Wall(hip, across);
                            var right = Wall(hip, -across);
                            if (left <= 0f || right <= 0f || left + right >= 7f)
                                continue;
                            var middle = hip + new float3(across.x, 0f, across.y) * (left - right) * 0.5f;
                            var half = (left + right) * 0.5f * 0.94f;
                            var mid = new float3(middle.x, floor - 0.02f, middle.z);
                            var roof = source.CeilingOver(mid + new float3(0f, 0.5f, 0f)) - mid.y;
                            if (half <= 1.9f || half >= roof - 0.1f)
                                continue;
                            var heights = Punchy.Flora.FloraGenerator.MineFrameHeights;
                            var variant = 0;
                            for (int v = 1; v < heights.Length; v++)
                                if (half * heights[v] < roof - 0.1f)
                                    variant = v;
                            extras.Plants.Add(new CavePlant { Position = mid, Yaw = yaw, Scale = half, Species = frame, Variant = variant, Random = rng.NextFloat() });
                        }
                    }
                    travelled += length;
                }
            }
        }

        /// <summary>A niche's segment (from its split point on the road's line out to the smaller niche node), not the road's.</summary>
        static bool NicheSegment(List<CaveNode> nodes, CaveSegment s) => nodes[s.B].Radius < nodes[s.A].Radius - 0.01f;

        /// <summary>
        /// W-138 row 4: the small things along a road tunnel (details tenet). The niche's shrine, its back to the rock;
        /// marker stones either side of the road where the land above changes biome; and fallen stones heaped against
        /// the foot of the walls here and there (rods and blocks of the bore's own rock, as cave rubble is: solid, no
        /// meshes of their own), never on the road. Returns where lamps mustn't go: the markers and the niche's mouth.
        /// </summary>
        static List<float3> PlaceTunnelDetails(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments,
                                               List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            var keepClear = new List<float3>();
            var lit = TunnelLampStyle(styles);
            var marker = lit != null && lit.Marker != null ? flora.IndexOf(lit.Marker) : -1;
            var shrine = lit != null && lit.Shrine != null ? flora.IndexOf(lit.Shrine) : -1;
            var terrain = source;   // local functions can't capture an in parameter
            float Wall(float3 from, float2 dir)
            {
                for (var t = 0.3f; t < 12f; t += 0.1f)
                    if (terrain.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t) >= 0f)
                        return t;
                return 0f;
            }
            float Roof(float3 at, float floor) => terrain.CeilingOver(new float3(at.x, floor + 0.5f, at.z)) - floor;
            var run = new List<(float3 a, float3 b, float floorOffset)>();
            var biomes = new List<int>();
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                if (cave.Tunnel == 0)
                    continue;
                var rng = new Random(math.hash(new uint2(cave.Key, 0xDE7Au)) | 1u);
                var radius = nodes[cave.FirstNode].Radius;
                // The road's half width at most (the bore is sized from it), so nothing set here reaches the road.
                var roadHalf = (radius - 0.6f) * 0.89f;
                run.Clear();
                var total = 0f;
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
                    var a = nodes[segments[si].A];
                    var b = nodes[segments[si].B];
                    if (!NicheSegment(nodes, segments[si]))
                    {
                        run.Add((a.Position, b.Position, a.Radius * cave.Profile.y * 0.45f));
                        total += math.distance(a.Position.xz, b.Position.xz);
                        continue;
                    }
                    // The niche: a shrine against its back wall, facing the road.
                    keepClear.Add(a.Position);
                    if (shrine < 0)
                        continue;
                    var dir = math.normalizesafe(b.Position.xz - a.Position.xz);
                    var nicheFloor = b.Position.y - b.Radius * cave.Profile.y * 0.45f;
                    var back = Wall(new float3(b.Position.x, nicheFloor + 0.5f, b.Position.z), dir);
                    if (back <= 0f)
                        continue;
                    var spot = new float3(b.Position.x, nicheFloor - 0.03f, b.Position.z) + new float3(dir.x, 0f, dir.y) * math.max(0f, back - 0.75f);
                    var height = flora[shrine].Height.y;
                    var scale = math.min(1.6f, Roof(spot, nicheFloor) - 0.3f) / height;
                    if (scale > 0.2f)
                        extras.Plants.Add(new CavePlant { Position = spot, Yaw = math.atan2(dir.x, -dir.y), Scale = scale, Species = shrine, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                    // And a lamp on the niche's side wall, so its rock takes warm light like the bore's rather than
                    // the cool ambient alone (W-138 row 4).
                    var torch = flora.IndexOf(TunnelLamp(lit));
                    var sideways = new float2(-dir.y, dir.x) * (rng.NextBool() ? 1f : -1f);
                    var shoulder = new float3(b.Position.x, nicheFloor + 1.5f, b.Position.z) + new float3(dir.x, 0f, dir.y) * 0.3f;
                    var reach = terrain.CaveDistance(shoulder) < 0f ? Wall(shoulder, sideways) : 0f;
                    if (torch >= 0 && reach > 0f)
                        extras.Plants.Add(new CavePlant { Position = shoulder + new float3(sideways.x, 0f, sideways.y) * (reach - 0.12f), Yaw = math.atan2(sideways.x, -sideways.y), Scale = 1f, Species = torch, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                }
                if (total < 40f)
                    continue;
                // A point along the road's line: where, which way it runs, and the floor there.
                void At(float along, out float3 q, out float2 dir, out float floor)
                {
                    foreach (var (a, b, floorOffset) in run)
                    {
                        var length = math.distance(a.xz, b.xz);
                        if (along > length && length > 0f)
                        {
                            along -= length;
                            continue;
                        }
                        q = math.lerp(a, b, length > 0f ? math.saturate(along / length) : 0f);
                        dir = math.normalizesafe((b - a).xz);
                        floor = q.y - floorOffset;
                        return;
                    }
                    var last = run[run.Count - 1];
                    q = last.b;
                    dir = math.normalizesafe((last.b - last.a).xz);
                    floor = q.y - last.floorOffset;
                }
                bool Clear(float3 q, float reach)
                {
                    foreach (var k in keepClear)
                        if (math.distancesq(k.xz, q.xz) < reach * reach)
                            return false;
                    return true;
                }

                // Marker stones where the land above changes biome: the biome under the road every 5 m, and each place
                // it changes and stays changed for 15 m either side (not a patch's edge flickering), at most two, 80 m
                // apart, clear of the portals' daylight.
                biomes.Clear();
                const float step = 5f;
                for (var d = 0f; d <= total; d += step)
                {
                    At(d, out var q, out _, out _);
                    biomes.Add(Dominant(terrain.SampleGround(q.xz).Biomes));
                }
                var lastBorder = -1e9f;
                var borders = 0;
                for (int i = 3; i + 3 <= biomes.Count && borders < 2 && marker >= 0; i++)
                {
                    var along = i * step;
                    if (along < 20f || along > total - 20f || along - lastBorder < 80f || biomes[i] == biomes[i - 1])
                        continue;
                    var steady = true;
                    for (int j = 1; j <= 3 && steady; j++)
                        steady = biomes[i - j] == biomes[i - 1] && biomes[i + j - 1] == biomes[i];
                    if (!steady)
                        continue;
                    lastBorder = along;
                    borders++;
                    At(along, out var q, out var dir, out var floor);
                    var across = new float2(-dir.y, dir.x);
                    foreach (var side in new[] { 1f, -1f })
                    {
                        var to = across * side;
                        var t = Wall(new float3(q.x, floor + 0.6f, q.z), to);
                        if (t <= 0f)
                            continue;
                        var lateral = math.max(t - 0.35f, roadHalf + 0.5f);
                        if (lateral > t + 0.15f)
                            continue;
                        var spot = new float3(q.x, floor - 0.05f, q.z) + new float3(to.x, 0f, to.y) * lateral;
                        var height = flora[marker].Height.y;
                        var scale = math.min(2.4f, Roof(spot, floor) - 0.4f) / height;
                        if (scale < 0.2f)
                            continue;
                        // Facing the road (a plant's front is its +Z turned by its yaw).
                        extras.Plants.Add(new CavePlant { Position = spot, Yaw = math.atan2(to.x, -to.y), Scale = scale, Species = marker, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                        keepClear.Add(spot);
                    }
                }

                // Fallen stones: a small heap against one wall every few tens of metres, clear of the portals, the
                // niche and the markers; now and then a sharp-edged block among them.
                for (var along = rng.NextFloat(20f, 35f); along < total - 20f; along += rng.NextFloat(25f, 55f))
                {
                    At(along, out var q, out var dir, out var floor);
                    if (!Clear(q, 5f))
                        continue;
                    var side = rng.NextBool() ? 1f : -1f;
                    var to = new float2(-dir.y, dir.x) * side;
                    var count = rng.NextInt(1, 4);
                    for (int k = 0; k < count; k++)
                    {
                        var block = k == 0 && rng.NextFloat() < 0.3f;
                        var r = block ? rng.NextFloat(0.45f, 0.65f) : rng.NextFloat(0.3f, 0.55f);
                        var at = q + new float3(dir.x, 0f, dir.y) * rng.NextFloat(-1.3f, 1.3f);
                        var t = Wall(new float3(at.x, floor + 0.5f, at.z), to);
                        if (t <= 0f)
                            continue;
                        var lateral = t - r * (block ? 0.5f : 0.4f);
                        if (lateral - r < roadHalf + 0.15f)
                            continue;
                        var middle = at + new float3(to.x, 0f, to.y) * lateral;
                        if (terrain.CaveDistance(new float3(middle.x - to.x * r, floor + 0.3f, middle.z - to.y * r)) >= 0f)
                            continue;
                        if (block)
                        {
                            var centre = new float3(middle.x, floor + r * 0.45f, middle.z);
                            var stone = Rod(centre, centre, r, r, 0.08f);
                            stone.Block = rng.NextFloat(0.05f, 1f);
                            extras.Speleothems.Add(stone);
                        }
                        else
                        {
                            var root = new float3(middle.x, floor + r * 0.3f, middle.z);
                            var lean = rng.NextFloat(math.PI * 2f);
                            var tip = root + new float3(math.cos(lean), rng.NextFloat(-0.1f, 0.2f), math.sin(lean)) * r * rng.NextFloat(0f, 0.8f);
                            extras.Speleothems.Add(Rod(root, tip, r, r * rng.NextFloat(0.6f, 0.9f), 0.3f));
                        }
                    }
                }
            }
            return keepClear;
        }
    }
}
