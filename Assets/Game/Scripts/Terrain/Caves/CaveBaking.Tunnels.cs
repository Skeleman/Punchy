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
        /// <summary>The fewest metres of passage radius a tunnel gets (head room is 1.45 times it).</summary>
        const float TunnelMinRadius = 3f;
        /// <summary>A tunnel's height for its width (its Profile.y): a low arch, as bored road tunnels are, so a shallow hill still roofs it.</summary>
        const float TunnelArch = 0.8f;
        /// <summary>How far past each portal's road end a tunnel's passage runs (metres).</summary>
        const float TunnelOverrun = 1.5f;
        /// <summary>Bores shorter than this (metres) are never lit: daylight reaches through from the portals.</summary>
        const float TunnelDaylit = 25f;

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
                var tunnel = lit == null || bore < TunnelDaylit || roll < 0.25f ? 1 : roll < 0.65f ? 2 : 3;
                // The rock: a style for the biome it runs under, as a cave there would have; a framed tunnel takes the
                // dug style (timbered, even).
                var style = tunnel == 3 ? lit : BiomeStyle(source, palette, styles, (first.xz + last.xz) * 0.5f, ref rng);
                // Wide enough that the road (and half a metre more each side) is all floor: the floor is 0.89 radius
                // either side of the axis (0.45 of the arch's height under the centre).
                var radius = math.max(TunnelMinRadius, width * 0.5f / 0.89f + 0.6f);
                // Past each portal by a little, so the passage's rounded end lies in the cutting and the portal opens
                // full height instead of narrowing to the end cap.
                var line = new List<float3>(points.Count + 2) { first + math.normalizesafe(first - points[1]) * TunnelOverrun };
                line.AddRange(points);
                line.Add(last + math.normalizesafe(last - points[points.Count - 2]) * TunnelOverrun);
                var cave = caves.Count;
                var firstNode = nodes.Count;
                var firstSegment = segments.Count;
                for (int k = 0; k < line.Count; k++)
                {
                    nodes.Add(new CaveNode { Position = line[k] + new float3(0f, radius * TunnelArch * 0.45f, 0f), Radius = radius });
                    kinds.Add(CaveNodeKind.Passage);
                    if (k > 0)
                        segments.Add(new CaveSegment { A = nodes.Count - 2, B = nodes.Count - 1, Cave = cave });
                }
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

        /// <summary>The style whose lamps and frames tunnels hang (the first with a torch), or none.</summary>
        static CaveStyle TunnelLampStyle(List<CaveStyle> styles)
        {
            foreach (var s in styles)
                if (s != null && s.Torch != null)
                    return s;
            return null;
        }

        /// <summary>A style listed for the biome at a spot (by the roll when several are), else one for anywhere, else none.</summary>
        static CaveStyle BiomeStyle(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, List<CaveStyle> styles,
                                    float2 position, ref Random rng)
        {
            var biome = Dominant(source.SampleGround(position).Biomes);
            var here = biome >= 0 && biome < palette.Count ? palette[biome] : null;
            var matches = new List<CaveStyle>();
            foreach (var s in styles)
                if (s != null && s.Torch == null && here != null && s.Biomes.Contains(here))
                    matches.Add(s);
            if (matches.Count == 0)
                foreach (var s in styles)
                    if (s != null && s.Torch == null && s.Biomes.Count == 0)
                        matches.Add(s);
            return matches.Count > 0 ? matches[rng.NextInt(matches.Count)] : null;
        }

        /// <summary>
        /// Lamps on alternate walls at shoulder height along lit tunnels (each its own spacing, some hung in facing
        /// pairs), and timber frames across framed ones: the dug style's torch and frame plants.
        /// </summary>
        static void PlaceTunnelFittings(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments,
                                        List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            var lit = TunnelLampStyle(styles);
            if (lit == null)
                return;
            var torch = flora.IndexOf(lit.Torch);
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
                    total += math.distance(nodes[segments[si].A].Position.xz, nodes[segments[si].B].Position.xz);
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
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
    }
}
