using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Lays out caves for landmarks whose type has one. The entrance opens in the landmark's cliff face (its +X side
    /// rises, so the cave runs into the rise), and the main passage winds in and down to a chamber, with more chambers
    /// along it if the type asks. Side passages branch off it (and, in big networks, off each other) to small chambers
    /// of their own, and shafts can rise from chambers to the surface. Every node except a shaft's is kept well below
    /// the surface (after landmark shapes), so passages never break out of the hillside. Each cave takes a style
    /// suited to its biome, which sets the passages' shape and the walls' look.
    /// </summary>
    public static class CaveBaking
    {
        const float NodeSpacing = 7f;
        /// <summary>Column size of the segment grid (m): a few passage segments across, so cells stay short.</summary>
        const float GridCell = 32f;
        /// <summary>Rock kept above every passage (m), so the hillside never opens except at mouths and shafts.</summary>
        const float Cover = 3f;

        /// <summary>A node a side passage can start from, and the way its own passage was heading there.</summary>
        struct Attach
        {
            public int Node;
            public float Heading;
        }

        public static BlobAssetReference<CaveSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette,
                                                            IReadOnlyList<LandmarkType> types, Allocator allocator)
        {
            if (!source.Landmarks.IsCreated)
                return default;
            ref var landmarks = ref source.Landmarks.Value;
            var styles = CaveStyleTable.Collect(types);
            var caves = new List<CaveData>();
            var nodes = new List<CaveNode>();
            var kinds = new List<CaveNodeKind>();
            var segments = new List<CaveSegment>();
            var attach = new List<Attach>();
            var terrain = source;   // local functions can't capture an in parameter

            for (int i = 0; i < landmarks.Instances.Length; i++)
            {
                ref var inst = ref landmarks.Instances[i];
                var type = (uint)inst.Type < (uint)types.Count ? types[inst.Type] : null;
                if (type == null || !type.Cave)
                    continue;
                var rng = new Random(math.hash(new uint2(inst.Key, 7717u)) | 1u);
                var style = PickStyle(source, palette, type, inst.Position, ref rng);
                var profile = style != null ? new float2(style.Width, style.Width * style.Height) : new float2(1f);
                var wall = style != null ? new float4(style.Roughness, style.Grain, style.Ribs, style.RibSpacing) : new float4(1f, 1f, 0f, 1.6f);
                var rock = style != null ? new float4(style.Jaggedness, style.FloorFlatness, style.FloorSteps, style.Blockiness) : new float4(0.7f, 0.85f, 0.4f, 0.3f);
                // Each cave its own take on its style, from its seed: a little wider or lower, rougher or smoother, more
                // or less broken, with more or fewer steps (so two limestone caves don't look cast from one mould).
                var vary = new Random(math.hash(new uint2(inst.Key, 0x5A17u)) | 1u);
                profile *= new float2(vary.NextFloat(0.88f, 1.12f), vary.NextFloat(0.85f, 1.15f));
                wall.x *= vary.NextFloat(0.8f, 1.2f);
                rock.x = math.min(rock.x * vary.NextFloat(0.8f, 1.2f), 1.5f);
                rock.z = math.saturate(rock.z + vary.NextFloat(-0.2f, 0.2f));
                // Dug caves (mines) run straight and even; the random draws are the same either way, so natural caves
                // come out exactly as before.
                var wander = 1f - (style != null ? style.Straightness : 0f);
                var radius = type.Radius * inst.Scale;
                var r = type.CaveRadius * inst.Scale;
                var chamber = type.ChamberRadius * inst.Scale;
                var floor = inst.BaseHeight;
                var forward = inst.Facing;
                var cave = caves.Count;
                var firstNode = nodes.Count;
                var firstSegment = segments.Count;
                attach.Clear();

                int AddNode(float3 position, float nodeRadius, CaveNodeKind kind)
                {
                    nodes.Add(new CaveNode { Position = position, Radius = nodeRadius });
                    kinds.Add(kind);
                    return nodes.Count - 1;
                }
                void Join(int a, int b) => segments.Add(new CaveSegment { A = a, B = b, Cave = cave });
                // Highest a node's centre may be at a spot: its top (the passage's height there) under the cover.
                float Ceiling(float2 xz, float nodeRadius) => terrain.SampleHeight(xz) - nodeRadius * profile.y - Cover;

                // The entrance: starting just outside the face, so the passage cuts cleanly into it.
                var start = inst.Position - forward * (radius * type.Edge * 0.5f + r * 0.6f);
                var mouth = new float3(start.x, floor + r * 0.4f, start.y);
                var previous = AddNode(mouth, r, CaveNodeKind.Passage);
                var heading = math.atan2(forward.y, forward.x);
                var p = start;
                var steps = math.max(3, (int)math.ceil(type.CaveLength / NodeSpacing));

                // Chambers along the main passage, evenly spaced between the entrance and the end.
                var chamberAt = new HashSet<int>();
                for (int c = 0; c < type.CaveChambers; c++)
                    chamberAt.Add(math.clamp((int)math.round((c + 1f) * steps / (type.CaveChambers + 1f)), 2, steps - 1));

                for (int k = 1; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    if (k > 1)
                        heading += rng.NextFloat(-0.5f, 0.5f) * wander;
                    p += new float2(math.cos(heading), math.sin(heading)) * NodeSpacing;
                    var isChamber = k == steps || chamberAt.Contains(k);
                    var nodeRadius = k == steps ? chamber
                        : isChamber ? chamber * rng.NextFloat(0.6f, 0.85f)
                        : r * math.lerp(1f, rng.NextFloat(0.85f, 1.2f), wander);
                    // Down by the cave's depth, and always a good way under the surface above.
                    var y = floor + r * 0.4f - type.CaveDepth * math.smoothstep(0.1f, 1f, t);
                    if (k > 1)
                        y = math.min(y, Ceiling(p, nodeRadius));
                    var node = AddNode(new float3(p.x, y, p.y), nodeRadius, isChamber ? CaveNodeKind.Chamber : CaveNodeKind.Passage);
                    Join(previous, node);
                    previous = node;
                    if (k >= 2)
                        attach.Add(new Attach { Node = node, Heading = heading });
                }

                // Side passages. The first half start from the main passage; later ones may start from earlier side
                // passages too, so big networks branch like a tree (and meet themselves where branches cross).
                var mainAttach = attach.Count;
                for (int b = 0; b < type.CaveBranches && attach.Count > 0; b++)
                {
                    var from = attach[rng.NextInt(b < (type.CaveBranches + 1) / 2 ? mainAttach : attach.Count)];
                    var origin = nodes[from.Node].Position;
                    var bh = from.Heading + (rng.NextBool() ? 1f : -1f) * math.lerp(math.PI * 0.5f, rng.NextFloat(0.7f, 1.6f), wander);
                    var length = type.BranchLength * inst.Scale * rng.NextFloat(0.6f, 1.25f);
                    var bsteps = math.max(2, (int)math.ceil(length / NodeSpacing));
                    var targetY = origin.y - rng.NextFloat(-2f, type.CaveDepth * 0.5f + 1f);
                    var bp = origin.xz;
                    var last = from.Node;
                    for (int k = 1; k <= bsteps; k++)
                    {
                        if (k > 1)
                            bh += rng.NextFloat(-0.45f, 0.45f) * wander;
                        bp += new float2(math.cos(bh), math.sin(bh)) * NodeSpacing;
                        var end = k == bsteps;
                        var nodeRadius = end ? chamber * rng.NextFloat(0.45f, 0.7f) : r * math.lerp(0.9f, rng.NextFloat(0.7f, 1.05f), wander);
                        var y = math.min(math.lerp(origin.y, targetY, k / (float)bsteps), Ceiling(bp, nodeRadius));
                        var node = AddNode(new float3(bp.x, y, bp.y), nodeRadius, end ? CaveNodeKind.Chamber : CaveNodeKind.Passage);
                        Join(last, node);
                        last = node;
                        attach.Add(new Attach { Node = node, Heading = bh });
                    }
                }

                // Shafts: from chambers, the last laid out first (side-passage ends, then the main passage's), straight
                // up to a little above the surface, wherever the surface there is dry.
                var shafts = 0;
                for (int n = nodes.Count - 1; n > firstNode && shafts < type.CaveShafts; n--)
                {
                    if (kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var c = nodes[n];
                    var ground = source.SampleGround(c.Position.xz);
                    if (ground.River > 0.02f || ground.HasWater && ground.WaterSurface > ground.Height - 1f)
                        continue;
                    var top = ground.Height + 2f;
                    var sr = math.min(r * 0.8f, c.Radius * 0.6f);
                    var last = n;
                    var count = math.max(2, (int)math.ceil((top - c.Position.y) / 5f));
                    for (int k = 1; k <= count; k++)
                    {
                        var position = new float3(c.Position.x, math.lerp(c.Position.y, top, k / (float)count), c.Position.z);
                        var node = AddNode(position, sr * rng.NextFloat(0.9f, 1.1f), CaveNodeKind.Shaft);
                        Join(last, node);
                        last = node;
                    }
                    shafts++;
                }

                float3 min = float.MaxValue, max = float.MinValue;
                for (int n = firstNode; n < nodes.Count; n++)
                {
                    var reach = nodes[n].Radius * math.cmax(profile) + 2.5f + CaveSampling.NoiseReach * wall.x + CaveSampling.JaggedReach * rock.x;
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
                    Mouth = mouth,
                    Seed = rng.NextFloat(-50f, 50f),
                    Key = inst.Key,
                    Style = style != null ? styles.IndexOf(style) + 1 : 0,
                    Profile = profile,
                    Wall = wall,
                    Facet = style != null ? new float2(style.Angularity, style.FacetSize) : float2.zero,
                    Rock = rock
                });
            }

            if (caves.Count == 0)
                return default;
            // Pools, streams and speleothems sit on the finished floors and hang from the finished ceilings, so each is
            // placed against the caves as built so far, then everything is built again with them: water first (basins
            // and channels change the floors), then speleothems and rubble (kept out of the water).
            var extras = new Extras();
            var withCaves = source;
            var bare = BuildBlob(caves, nodes, kinds, segments, extras, Allocator.Persistent);
            withCaves.Caves = bare;
            PlacePools(withCaves, caves, nodes, kinds, segments, styles, extras.Pools);
            PlaceWaterfalls(withCaves, caves, styles, extras);
            PlaceStreams(withCaves, caves, nodes, segments, styles, extras);
            bare.Dispose();
            var withWater = BuildBlob(caves, nodes, kinds, segments, extras, Allocator.Persistent);
            withCaves.Caves = withWater;
            PlaceSpeleothems(withCaves, caves, nodes, kinds, segments, styles, extras.Pools, extras.Speleothems);
            PlaceRubble(withCaves, caves, nodes, kinds, segments, styles, extras.Pools, extras.Speleothems);
            PlacePlants(withCaves, caves, nodes, kinds, segments, styles, CaveFloraTable.Collect(types), extras);
            PlaceFittings(withCaves, caves, nodes, kinds, segments, styles, CaveFloraTable.Collect(types), extras);
            withWater.Dispose();
            return BuildBlob(caves, nodes, kinds, segments, extras, allocator);
        }

        /// <summary>What's placed in caves after their shape: pools, streams, speleothems (and rubble), plants.</summary>
        class Extras
        {
            public readonly List<CavePool> Pools = new List<CavePool>();
            public readonly List<CaveWaterfall> Waterfalls = new List<CaveWaterfall>();
            public readonly List<CaveStream> Streams = new List<CaveStream>();
            public readonly List<CaveStreamPoint> StreamPoints = new List<CaveStreamPoint>();
            public readonly List<CaveSpeleothem> Speleothems = new List<CaveSpeleothem>();
            public readonly List<CavePlant> Plants = new List<CavePlant>();
        }

        /// <summary>
        /// A stream along the main passage of caves whose style rolls for one, from the second node in (clear of the
        /// mouth) down to the end chamber, ending in a pool (F-30). Points every 1.5 m along the passage floor; the surface follows the floor
        /// but never rises along the flow, so where the floor climbs the channel cuts deeper.
        /// </summary>
        static void PlaceStreams(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments,
                                 List<CaveStyle> styles, Extras extras)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                cave.FirstStream = extras.Streams.Count;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0x5711u)) | 1u);
                if (style != null && style.StreamChance > 0f && rng.NextFloat() < style.StreamChance && cave.NodeCount >= 4)
                {
                    // The main passage: laid out first, each node joined to the one before it; it ends where that
                    // stops (the side passages' nodes come next, joined elsewhere).
                    var path = new List<int> { cave.FirstNode + 1 };
                    for (int n = cave.FirstNode + 2; n < cave.FirstNode + cave.NodeCount && Joined(n - 1, n, cave, segments); n++)
                        path.Add(n);
                    var first = extras.StreamPoints.Count;
                    var level = float.MaxValue;
                    float2 min = float.MaxValue, max = float.MinValue;
                    for (int k = 0; k < path.Count - 1; k++)
                    {
                        var a = nodes[path[k]];
                        var b = nodes[path[k + 1]];
                        var steps = math.max(1, (int)math.ceil(math.distance(a.Position.xz, b.Position.xz) / 1.5f));
                        for (int s = 0; s < steps; s++)
                        {
                            var t = s / (float)steps;
                            var at = math.lerp(a.Position, b.Position, t);
                            var floor = source.FloorUnder(at, out var inCave);
                            if (!inCave)
                                continue;
                            level = math.min(level, floor - 0.3f);
                            var width = math.clamp(math.lerp(a.Radius, b.Radius, t) * cave.Profile.x * 0.35f, 0.8f, 2f);
                            extras.StreamPoints.Add(new CaveStreamPoint { Position = new float3(at.x, level, at.z), Width = width, Depth = 0.45f });
                            min = math.min(min, at.xz - width);
                            max = math.max(max, at.xz + width);
                        }
                    }
                    if (extras.StreamPoints.Count - first >= 4)
                    {
                        extras.Streams.Add(new CaveStream { FirstPoint = first, PointCount = extras.StreamPoints.Count - first, Min = min, Max = max });
                        // Every stream ends in a pool at its own level, shrunk until the floor holds it all round (or
                        // left out), added to this cave's pools (later caves' ranges move up one).
                        var end = extras.StreamPoints[extras.StreamPoints.Count - 1];
                        var radius = math.clamp(end.Width * 1.6f, 1.2f, 2.5f);
                        while (radius >= 1.2f && !RimHolds(source, end.Position, radius))
                            radius *= 0.8f;
                        if (radius >= 1.2f)
                        {
                            extras.Pools.Insert(cave.FirstPool + cave.PoolCount, new CavePool
                            {
                                Centre = end.Position,
                                Radius = radius,
                                Depth = math.min(1.2f, radius * 0.4f),
                                Lava = 0f
                            });
                            cave.PoolCount++;
                            for (int later = c + 1; later < caves.Count; later++)
                            {
                                var next = caves[later];
                                next.FirstPool++;
                                caves[later] = next;
                            }
                        }
                    }
                    else
                        extras.StreamPoints.RemoveRange(first, extras.StreamPoints.Count - first);
                }
                cave.StreamCount = extras.Streams.Count - cave.FirstStream;
                caves[c] = cave;
            }
        }

        static bool Joined(int a, int b, CaveData cave, List<CaveSegment> segments)
        {
            for (int i = cave.FirstSegment; i < cave.FirstSegment + cave.SegmentCount; i++)
                if (segments[i].A == a && segments[i].B == b)
                    return true;
            return false;
        }

        static BlobAssetReference<CaveSetBlob> BuildBlob(List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                                         List<CaveSegment> segments, Extras extras, Allocator allocator)
        {
            var pools = extras.Pools;
            var speleothems = extras.Speleothems;
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CaveSetBlob>();
            var caveArray = builder.Allocate(ref root.Caves, caves.Count);
            for (int i = 0; i < caves.Count; i++)
                caveArray[i] = caves[i];
            var nodeArray = builder.Allocate(ref root.Nodes, nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                nodeArray[i] = nodes[i];
            var kindArray = builder.Allocate(ref root.Kinds, kinds.Count);
            for (int i = 0; i < kinds.Count; i++)
                kindArray[i] = kinds[i];
            var poolArray = builder.Allocate(ref root.Pools, pools.Count);
            for (int i = 0; i < pools.Count; i++)
                poolArray[i] = pools[i];
            var fallArray = builder.Allocate(ref root.Waterfalls, extras.Waterfalls.Count);
            for (int i = 0; i < extras.Waterfalls.Count; i++)
                fallArray[i] = extras.Waterfalls[i];
            var streamArray = builder.Allocate(ref root.Streams, extras.Streams.Count);
            for (int i = 0; i < extras.Streams.Count; i++)
                streamArray[i] = extras.Streams[i];
            var plantArray = builder.Allocate(ref root.Plants, extras.Plants.Count);
            for (int i = 0; i < extras.Plants.Count; i++)
                plantArray[i] = extras.Plants[i];
            var pointArray = builder.Allocate(ref root.StreamPoints, extras.StreamPoints.Count);
            for (int i = 0; i < extras.StreamPoints.Count; i++)
                pointArray[i] = extras.StreamPoints[i];
            BuildGrid(builder, ref root, caves, nodes, segments);
            BuildSpeleothemGrid(builder, ref root, speleothems);
            var blob = builder.CreateBlobAssetReference<CaveSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }

        /// <summary>
        /// Stalactites, stalagmites and columns in every chamber, by its cave's style: scattered over the middle of the
        /// floor, rooted half a metre into the floor or ceiling. Hanging ones leave head room; none stand across the
        /// way through the chamber (near the axis of a passage joining it) or in a pool, so they never block the cave.
        /// </summary>
        static void PlaceSpeleothems(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                     List<CaveSegment> segments, List<CaveStyle> styles, List<CavePool> pools, List<CaveSpeleothem> result)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var density = style != null ? style.SpeleothemDensity : 4f;
                if (density <= 0f)
                    continue;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0x57A1u)) | 1u);
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    // Chambers, and at half the density any passage wide enough to keep a way past them (so even a
                    // small cave gets a few; the way through stays clear either way).
                    var chamber = kinds[n] == CaveNodeKind.Chamber;
                    if (!chamber && nodes[n].Radius * cave.Profile.x < 2.5f)
                        continue;
                    var centre = nodes[n].Position;
                    var spread = nodes[n].Radius * cave.Profile.x * 0.8f;
                    var count = (int)math.round(density * (chamber ? 1f : 0.5f) * math.PI * spread * spread / 100f * rng.NextFloat(0.7f, 1.3f));
                    for (int k = 0; k < count; k++)
                    {
                        var a = rng.NextFloat(math.PI * 2f);
                        var q = centre.xz + new float2(math.cos(a), math.sin(a)) * spread * math.sqrt(rng.NextFloat());
                        var thickness = (style != null ? style.SpeleothemThickness : 0.55f) * rng.NextFloat(0.7f, 1.3f);
                        if (InTheWay(q, 0.6f + thickness * 1.3f, n, nodes, segments, cave) || InPool(q, thickness, pools, cave))
                            continue;
                        var floor = source.FloorUnder(new float3(q.x, centre.y, q.y), out var inCave);
                        if (!inCave || source.CaveDistance(new float3(q.x, floor + 0.3f, q.y)) >= 0f)
                            continue;
                        var ceiling = source.CeilingOver(new float3(q.x, floor + 0.3f, q.y));
                        var height = ceiling - floor;
                        if (height < 2.5f)
                            continue;
                        var length = height * (style != null ? style.SpeleothemLength : 0.4f) * rng.NextFloat(0.5f, 1.2f);
                        CaveSpeleothem s;
                        if (rng.NextFloat() < (style != null ? style.Columns : 0.1f))
                            s = Rod(new float3(q.x, floor - 0.5f, q.y), new float3(q.x, ceiling + 0.5f, q.y), thickness * 1.3f, thickness * 0.8f);
                        else if (rng.NextFloat() < (style != null ? style.Hanging : 0.55f))
                            s = Rod(new float3(q.x, ceiling + 0.5f, q.y), new float3(q.x, ceiling - math.min(length, height - 2.2f), q.y), thickness, 0.05f);
                        else
                            s = Rod(new float3(q.x, floor - 0.5f, q.y), new float3(q.x, floor + math.min(length, height - 0.8f), q.y), thickness * 1.2f, 0.08f);
                        if (s.Tip.y != s.Root.y)
                            result.Add(s);
                    }
                }
            }
        }

        /// <summary>
        /// Fallen rocks in every chamber, by its cave's style: most heaped against the foot of the walls, the rest
        /// scattered over the floor, each a rounded, slightly elongated lump sunk a third into the floor. They are rods
        /// like the speleothems (part of the rock: drawn with it, solid), so they cost no meshes or draws of their own.
        /// Never across the way through or in a pool.
        /// </summary>
        static void PlaceRubble(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                List<CaveSegment> segments, List<CaveStyle> styles, List<CavePool> pools, List<CaveSpeleothem> result)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var density = style != null ? style.Rubble : 3f;
                if (density <= 0f)
                    continue;
                var size = style != null ? style.RubbleSize : 0.6f;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0x2B1Eu)) | 1u);
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var centre = nodes[n].Position;
                    var spread = nodes[n].Radius * cave.Profile.x;
                    var count = (int)math.round(density * math.PI * spread * spread * 0.64f / 100f * rng.NextFloat(0.7f, 1.3f));
                    for (int k = 0; k < count; k++)
                    {
                        var r = size * rng.NextFloat(0.6f, 1.5f);
                        var a = rng.NextFloat(math.PI * 2f);
                        var dir = new float2(math.cos(a), math.sin(a));
                        float2 q;
                        if (rng.NextFloat() < 0.65f)
                        {
                            // Against the wall: walk out from the middle at knee height until rock, then back off.
                            var floorHere = source.FloorUnder(centre, out _);
                            var from = new float3(centre.x, floorHere + 0.5f, centre.z);
                            var t = 0f;
                            var hit = false;
                            for (int s = 0; s < 80 && !hit; s++)
                            {
                                var d = source.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t);
                                if (d >= 0f) hit = true;
                                else t += math.max(0.2f, -d * 0.4f);
                            }
                            if (!hit || t < r * 2f)
                                continue;
                            q = centre.xz + dir * (t - r * 0.6f);
                        }
                        else
                            q = centre.xz + dir * spread * 0.8f * math.sqrt(rng.NextFloat());
                        if (InTheWay(q, 0.6f + r, n, nodes, segments, cave) || InPool(q, r, pools, cave))
                            continue;
                        var floor = source.FloorUnder(new float3(q.x, centre.y, q.y), out var inCave);
                        if (!inCave || source.CaveDistance(new float3(q.x, floor + 0.3f, q.y)) >= 0f)
                            continue;
                        var lean = rng.NextFloat(math.PI * 2f);
                        var root = new float3(q.x, floor + r * 0.3f, q.y);
                        var tip = root + new float3(math.cos(lean), rng.NextFloat(-0.1f, 0.2f), math.sin(lean)) * r * rng.NextFloat(0f, 0.8f);
                        // Loose rock sits on the floor rather than growing out of it: a tighter fillet than a stalagmite.
                        result.Add(Rod(root, tip, r, r * rng.NextFloat(0.6f, 0.9f), 0.3f));
                    }

                    // A few chunky boulders per chamber, fallen near the walls: sharp-edged blocks of the cave's own
                    // rock (its look and collision), crisply set on the floor.
                    var boulders = (int)math.round(math.min(density / 5f, 1f) * rng.NextFloat(2f, 4.5f));
                    for (int k = 0; k < boulders; k++)
                    {
                        var r = size * rng.NextFloat(1.6f, 2.6f);
                        var a = rng.NextFloat(math.PI * 2f);
                        var dir = new float2(math.cos(a), math.sin(a));
                        var floorHere = source.FloorUnder(centre, out _);
                        var from = new float3(centre.x, floorHere + 0.5f, centre.z);
                        var t = 0f;
                        var hit = false;
                        for (int s = 0; s < 80 && !hit; s++)
                        {
                            var d = source.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t);
                            if (d >= 0f) hit = true;
                            else t += math.max(0.2f, -d * 0.4f);
                        }
                        if (!hit || t < r * 2.5f)
                            continue;
                        var q = centre.xz + dir * (t - r * 0.9f);
                        if (InTheWay(q, 0.8f + r, n, nodes, segments, cave) || InPool(q, r, pools, cave))
                            continue;
                        var floor = source.FloorUnder(new float3(q.x, centre.y, q.y), out var inCave);
                        if (!inCave || source.CaveDistance(new float3(q.x, floor + r * 1.6f, q.y)) >= 0f)
                            continue;
                        // Set a little into the floor, so it rests on a flat face.
                        var middle = new float3(q.x, floor + r * 0.45f, q.y);
                        var block = Rod(middle, middle, r, r, 0.08f);
                        block.Block = rng.NextFloat(0.05f, 1f);
                        result.Add(block);
                    }
                }
            }
        }

        /// <summary>
        /// Plants on chamber and passage floors, by each cave's style: each listed species scattered by its density
        /// where the light suits it (near daylight: within reach of the mouth or under a shaft; dark: away from both),
        /// off the way through and out of pools and streams.
        /// </summary>
        static void PlacePlants(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                List<CaveSegment> segments, List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            const float DaylightReach = 15f;
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style == null || style.Flora.Count == 0)
                    continue;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0xF10Au)) | 1u);
                var shafts = new List<float2>();
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                    if (kinds[n] == CaveNodeKind.Shaft)
                        shafts.Add(nodes[n].Position.xz);
                bool NearDaylight(float2 q)
                {
                    if (math.distance(q, cave.Mouth.xz) < DaylightReach)
                        return true;
                    foreach (var s in shafts)
                        if (math.distance(q, s) < 6f)
                            return true;
                    return false;
                }

                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (kinds[n] == CaveNodeKind.Shaft)
                        continue;
                    var centre = nodes[n].Position;
                    var spread = nodes[n].Radius * cave.Profile.x * 0.75f;
                    foreach (var entry in style.Flora)
                    {
                        if (entry == null || entry.Species == null || entry.Density <= 0f)
                            continue;
                        // Rounded at random (most nodes expect less than one plant of a kind), so the density holds on average.
                        var expected = entry.Density * math.PI * spread * spread / 100f * rng.NextFloat(0.6f, 1.4f);
                        var count = (int)expected + (rng.NextFloat() < expected - (int)expected ? 1 : 0);
                        for (int k = 0; k < count; k++)
                        {
                            var a = rng.NextFloat(math.PI * 2f);
                            var q = centre.xz + new float2(math.cos(a), math.sin(a)) * spread * math.sqrt(rng.NextFloat());
                            var daylight = NearDaylight(q);
                            if (entry.Light == CaveLight.NearDaylight && !daylight || entry.Light == CaveLight.Dark && daylight)
                                continue;
                            // A narrow way through is enough for small plants: 0.6 m each side of the axis.
                            if (InTheWay(q, -0.6f, n, nodes, segments, cave) || InPool(q, 0.3f, extras.Pools, cave) || InStream(q, extras, cave))
                                continue;
                            var floor = source.FloorUnder(new float3(q.x, centre.y, q.y), out var inCave);
                            if (!inCave || source.CaveDistance(new float3(q.x, floor + 0.5f, q.y)) > -0.3f)
                                continue;
                            var at = new float3(q.x, floor - 0.03f, q.y);
                            if (entry.Hanging)
                            {
                                // From the ceiling, where the rock above is thin and the ground over it grows plants.
                                var ceiling = source.CeilingOver(new float3(q.x, floor + 0.3f, q.y));
                                var ground = source.SampleGround(q);
                                // Head room: most strands (three quarters of the longest) clear a walker's head.
                                if (ground.Height - ceiling > entry.MaxCover || ceiling - floor < entry.Species.Height.y * entry.Species.SizeRange.y * 0.75f + 1.8f
                                    || source.SampleSurface(q, ground, source.SampleNormal(q, 1f)).Grass < 0.15f)
                                    continue;
                                at = new float3(q.x, ceiling + 0.05f, q.y);
                            }
                            if (UnderRock(at, extras.Speleothems))
                                continue;
                            var size = entry.Species.SizeRange;
                            extras.Plants.Add(new CavePlant
                            {
                                Position = at,
                                Yaw = rng.NextFloat(math.PI * 2f),
                                Scale = math.lerp(size.x, size.y, rng.NextFloat()),
                                Species = flora.IndexOf(entry.Species),
                                Variant = rng.NextInt(3),
                                Random = rng.NextFloat()
                            });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Dug caves' fittings, along every passage (not chambers or shafts): a timber frame across the passage every
        /// FrameSpacing, posts against the walls (found by looking sideways from the axis at hip height), and a torch
        /// on alternate walls every TorchSpacing at shoulder height, leaning out. Both are cave plants.
        /// </summary>
        static void PlaceFittings(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                                  List<CaveSegment> segments, List<CaveStyle> styles, List<Punchy.Flora.FloraSpecies> flora, Extras extras)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style == null || style.Frame == null && style.Torch == null)
                    continue;
                var terrain = source;   // local functions can't capture an in parameter
                var frame = style.Frame != null ? flora.IndexOf(style.Frame) : -1;
                var torch = style.Torch != null ? flora.IndexOf(style.Torch) : -1;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0xF177u)) | 1u);
                var side = 1f;
                var sinceTorch = style.TorchSpacing * 0.5f;
                for (int si = cave.FirstSegment; si < cave.FirstSegment + cave.SegmentCount; si++)
                {
                    var seg = segments[si];
                    if (kinds[seg.A] != CaveNodeKind.Passage && kinds[seg.B] != CaveNodeKind.Passage || kinds[seg.A] == CaveNodeKind.Shaft || kinds[seg.B] == CaveNodeKind.Shaft)
                        continue;
                    var a = nodes[seg.A].Position;
                    var b = nodes[seg.B].Position;
                    var length = math.distance(a.xz, b.xz);
                    if (length < 0.5f)
                        continue;
                    var along = math.normalize((b - a).xz);
                    var across = new float2(-along.y, along.x);
                    // Model +Z along the passage (its posts, on X, then stand across it): yaw maps +Z to (-sin, cos).
                    var yaw = math.atan2(-along.x, along.y);
                    float Wall(float3 from, float2 dir)
                    {
                        for (var t = 0.3f; t < 12f; t += 0.1f)
                            if (terrain.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t) >= 0f)
                                return t;
                        return 0f;
                    }
                    var step = frame >= 0 ? style.FrameSpacing : style.TorchSpacing;
                    for (var d = step * 0.5f; d < length; d += step)
                    {
                        var q = math.lerp(a, b, d / length);
                        var floor = source.FloorUnder(q + new float3(0f, 0.5f, 0f), out var inCave);
                        if (!inCave)
                            continue;
                        var hip = new float3(q.x, floor + 0.9f, q.z);
                        if (source.CaveDistance(hip) >= 0f)
                            continue;
                        var left = Wall(hip, across);
                        var right = Wall(hip, -across);
                        if (frame >= 0 && left > 0f && right > 0f && left + right < 7f)
                        {
                            var middle = hip + new float3(across.x, 0f, across.y) * (left - right) * 0.5f;
                            var half = (left + right) * 0.5f * 0.94f;
                            var mid = new float3(middle.x, source.FloorUnder(middle + new float3(0f, 0.5f, 0f), out _) - 0.02f, middle.z);
                            // Only where the lintel clears a walker's head and stays under the roof. The tallest variant
                            // that fits, so the lintel meets the roof in steep passages too (F-34). (The draw is kept so
                            // later frames and torches keep their seeds.)
                            var roof = source.CeilingOver(mid + new float3(0f, 0.5f, 0f)) - mid.y;
                            if (half > 1.9f && half < roof - 0.1f)
                            {
                                rng.NextInt(3);
                                var heights = Punchy.Flora.FloraGenerator.MineFrameHeights;
                                var variant = 0;
                                for (int v = 1; v < heights.Length; v++)
                                    if (half * heights[v] < roof - 0.1f)
                                        variant = v;
                                extras.Plants.Add(new CavePlant { Position = mid, Yaw = yaw, Scale = half, Species = frame, Variant = variant, Random = rng.NextFloat() });
                            }
                        }
                        sinceTorch += step;
                        if (torch >= 0 && sinceTorch >= style.TorchSpacing)
                        {
                            // Halfway to the next frame, so it isn't hidden behind a post.
                            var tq = math.lerp(a, b, math.min(d + step * 0.5f, length) / length);
                            var tfloor = source.FloorUnder(tq + new float3(0f, 0.5f, 0f), out _);
                            var shoulder = new float3(tq.x, tfloor + 1.6f, tq.z);
                            var dir = across * side;
                            var t = source.CaveDistance(shoulder) < 0f ? Wall(shoulder, dir) : 0f;
                            if (t > 0f)
                            {
                                var at = shoulder + new float3(dir.x, 0f, dir.y) * (t - 0.12f);
                                // Facing out of the wall (+Z of the torch model).
                                extras.Plants.Add(new CavePlant { Position = at, Yaw = math.atan2(dir.x, -dir.y), Scale = 1f, Species = torch, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                                sinceTorch = 0f;
                                side = -side;
                            }
                        }
                    }
                }
                // Chambers (where miners work the ore faces): two torches on opposite walls at shoulder height.
                if (torch < 0)
                    continue;
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var centre = nodes[n].Position;
                    var floor = source.FloorUnder(centre, out var inCave);
                    var shoulder = new float3(centre.x, floor + 1.6f, centre.z);
                    if (!inCave || source.CaveDistance(shoulder) >= 0f)
                        continue;
                    var start = rng.NextFloat(math.PI * 2f);
                    for (int k = 0; k < 2; k++)
                    {
                        var a = start + k * math.PI;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var t = 0f;
                        for (var r = 0.3f; r < 12f; r += 0.1f)
                            if (source.CaveDistance(shoulder + new float3(dir.x, 0f, dir.y) * r) >= 0f) { t = r; break; }
                        if (t <= 0f)
                            continue;
                        var at = shoulder + new float3(dir.x, 0f, dir.y) * (t - 0.12f);
                        extras.Plants.Add(new CavePlant { Position = at, Yaw = math.atan2(dir.x, -dir.y), Scale = 1f, Species = torch, Variant = rng.NextInt(3), Random = rng.NextFloat() });
                    }
                }
            }
        }

        static bool InStream(float2 q, Extras extras, CaveData cave)
        {
            for (int s = cave.FirstStream; s < cave.FirstStream + cave.StreamCount; s++)
            {
                var stream = extras.Streams[s];
                for (int i = stream.FirstPoint; i < stream.FirstPoint + stream.PointCount; i++)
                    if (math.distance(q, extras.StreamPoints[i].Position.xz) < extras.StreamPoints[i].Width * 0.8f + 0.3f)
                        return true;
            }
            return false;
        }

        static bool InPool(float2 q, float thickness, List<CavePool> pools, CaveData cave)
        {
            for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                if (math.distance(q, pools[i].Centre.xz) < pools[i].Radius * 1.2f + thickness)
                    return true;
            return false;
        }

        static CaveSpeleothem Rod(float3 root, float3 tip, float rootRadius, float tipRadius, float fillet = 0.5f)
        {
            var reach = math.max(rootRadius, tipRadius) + 1f;
            return new CaveSpeleothem
            {
                Root = root, Tip = tip, RootRadius = rootRadius, TipRadius = tipRadius, Fillet = fillet,
                Min = math.min(root, tip) - reach,
                Max = math.max(root, tip) + reach
            };
        }

        /// <summary>Lists each speleothem in the grid columns its box touches (the segment grid's cells).</summary>
        static void BuildSpeleothemGrid(BlobBuilder builder, ref CaveSetBlob root, List<CaveSpeleothem> speleothems)
        {
            var array = builder.Allocate(ref root.Speleothems, speleothems.Count);
            for (int i = 0; i < speleothems.Count; i++)
                array[i] = speleothems[i];
            var size = root.GridSize;
            var cells = new List<int>[size.x * size.y];
            for (int i = 0; i < speleothems.Count; i++)
            {
                var c0 = math.max((int2)math.floor((speleothems[i].Min.xz - root.GridOrigin) / root.CellSize), 0);
                var c1 = math.min((int2)math.floor((speleothems[i].Max.xz - root.GridOrigin) / root.CellSize), size - 1);
                for (int z = c0.y; z <= c1.y; z++)
                for (int x = c0.x; x <= c1.x; x++)
                    (cells[z * size.x + x] ??= new List<int>()).Add(i);
            }
            // No speleothems: no cell table at all, so sampling skips them with one length check.
            var cellArray = builder.Allocate(ref root.SpeleothemCells, speleothems.Count > 0 ? cells.Length : 0);
            var items = new List<int>();
            if (speleothems.Count > 0)
                for (int c = 0; c < cells.Length; c++)
                {
                    cellArray[c] = new int2(items.Count, cells[c]?.Count ?? 0);
                    if (cells[c] != null)
                        items.AddRange(cells[c]);
                }
            var itemArray = builder.Allocate(ref root.SpeleothemItems, items.Count);
            for (int i = 0; i < items.Count; i++)
                itemArray[i] = items[i];
        }

        /// <summary>
        /// Pools for caves whose style rolls for them: in up to the two biggest chambers, off to one side of the way
        /// through (beside the axis of the passages joining the chamber), sunk into the floor there. Sets each cave's
        /// pool range.
        /// </summary>
        static void PlacePools(in TerrainSource source, List<CaveData> caves, List<CaveNode> nodes, List<CaveNodeKind> kinds,
                               List<CaveSegment> segments, List<CaveStyle> styles, List<CavePool> pools)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                cave.FirstPool = pools.Count;
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0x9001u)) | 1u);
                if (style != null && style.PoolChance > 0f && rng.NextFloat() < style.PoolChance)
                {
                    var chambers = new List<int>();
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                        if (kinds[n] == CaveNodeKind.Chamber && nodes[n].Radius * cave.Profile.x >= 3.5f)
                            chambers.Add(n);
                    chambers.Sort((a, b) => nodes[b].Radius.CompareTo(nodes[a].Radius));
                    for (int k = 0; k < chambers.Count && k < 2; k++)
                    {
                        var n = chambers[k];
                        var centre = nodes[n].Position;
                        var r = nodes[n].Radius * cave.Profile.x;
                        // Beside the way in: across the line from the previous node.
                        var along = math.normalizesafe((centre - nodes[math.max(n - 1, cave.FirstNode)].Position).xz, new float2(1f, 0f));
                        var side = (rng.NextBool() ? 1f : -1f) * new float2(-along.y, along.x);
                        var radius = r * style.PoolSize;
                        var xz = centre.xz + side * (r * 0.8f - radius * 0.4f);
                        if (InTheWay(xz, radius, n, nodes, segments, cave))
                            continue;
                        var floor = source.FloorUnder(new float3(xz.x, centre.y, xz.y), out var inCave);
                        if (!inCave || floor < centre.y - r * 2f)
                            continue;
                        // The water is a flat disc just under the floor at its centre, so the floor must rise (or at
                        // least not fall) all round its rim, or the disc's edge would hang over a sloping floor: shrink
                        // it until it fits, or leave the pool out.
                        while (radius >= 1.2f && !RimHolds(source, new float3(xz.x, floor - 0.12f, xz.y), radius))
                            radius *= 0.75f;
                        if (radius < 1.2f)
                            continue;
                        pools.Add(new CavePool
                        {
                            Centre = new float3(xz.x, floor - 0.12f, xz.y),
                            Radius = radius,
                            Depth = math.min(1.2f, radius * 0.4f),
                            Lava = style.LavaPools ? 1f : 0f
                        });
                    }
                }
                cave.PoolCount = pools.Count - cave.FirstPool;
                caves[c] = cave;
            }
        }

        /// <summary>
        /// Waterfalls, by the style's chance: over a water pool, from the nearest wall within a few metres of its rim, a
        /// lip high up (under the ceiling, at most 7 m above the water) pouring out and down into the pool. At most
        /// two a cave, at least 2 m of drop.
        /// </summary>
        static void PlaceWaterfalls(in TerrainSource source, List<CaveData> caves, List<CaveStyle> styles, Extras extras)
        {
            for (int c = 0; c < caves.Count; c++)
            {
                var cave = caves[c];
                var style = cave.Style > 0 ? styles[cave.Style - 1] : null;
                if (style == null || style.WaterfallChance <= 0f)
                    continue;
                var rng = new Random(math.hash(new uint2(caves[c].Key, 0xFA11u)) | 1u);
                var made = 0;
                for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount && made < 2; i++)
                {
                    var pool = extras.Pools[i];
                    if (pool.Lava > 0.5f || rng.NextFloat() >= style.WaterfallChance)
                        continue;
                    var from = pool.Centre + new float3(0f, 1.5f, 0f);
                    var start = rng.NextFloat(math.PI * 2f);
                    for (int k = 0; k < 8; k++)
                    {
                        var a = start + k * math.PI * 0.25f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var t = 0.3f;
                        while (t < pool.Radius + 4f && source.CaveDistance(from + new float3(dir.x, 0f, dir.y) * t) < 0f)
                            t += 0.1f;
                        if (t >= pool.Radius + 4f)
                            continue;
                        // Just off the wall, as high as the ceiling there allows.
                        var foot = pool.Centre.xz + dir * (t - 0.35f);
                        var ceiling = source.CeilingOver(new float3(foot.x, pool.Centre.y + 1f, foot.y));
                        var lipY = math.min(ceiling - 0.4f, pool.Centre.y + 7f);
                        if (lipY - pool.Centre.y < 2f)
                            continue;
                        // The water comes out of a dark notch cut into the wall, and starts inside it.
                        var width = math.min(pool.Radius * 0.6f, 1.6f);
                        var into = new float3(dir.x, 0f, dir.y);
                        // The wall at the lip's height (it may lean in or out from where it was found lower down).
                        var wall = new float3(foot.x, lipY, foot.y) + into * 0.35f;
                        for (int step = 0; step < 30 && source.CaveDistance(wall) < 0f; step++)
                            wall += into * 0.1f;
                        for (int step = 0; step < 30 && source.CaveDistance(wall - into * 0.1f) > 0f; step++)
                            wall -= into * 0.1f;
                        var notch = Rod(wall - into * 0.3f, wall + (into + new float3(0f, 0.25f, 0f)) * 0.9f,
                                        width * 0.42f, width * 0.25f, 0.15f);
                        notch.Carve = 1f;
                        extras.Speleothems.Add(notch);
                        extras.Waterfalls.Add(new CaveWaterfall
                        {
                            Lip = wall + into * 0.25f,
                            Width = width,
                            Out = -dir,
                            Drop = lipY - pool.Centre.y
                        });
                        made++;
                        break;
                    }
                }
            }
        }

        /// <summary>Whether the cave floor stays above a pool's surface all round its rim and just inside it.</summary>
        static bool RimHolds(in TerrainSource source, float3 surface, float radius)
        {
            for (int k = 0; k < 12; k++)
            {
                var a = k / 12f * math.PI * 2f;
                var dir = new float2(math.cos(a), math.sin(a));
                foreach (var f in new[] { 0.75f, 1.05f })
                {
                    var q = surface.xz + dir * radius * f;
                    var floor = source.FloorUnder(new float3(q.x, surface.y + 1.5f, q.y), out var inCave);
                    // Under the rim the floor may dip a little below the water (it's the shore), but not away.
                    if (!inCave || floor < surface.y - (f < 1f ? 0.6f : 0.02f))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether a speleothem or rubble rock (added to the rock after plants are placed) would swallow a spot, its
        /// fillet included.
        /// </summary>
        static bool UnderRock(float3 p, List<CaveSpeleothem> rods)
        {
            foreach (var s in rods)
                if (s.Carve == 0f && math.all(p >= s.Min - 0.6f) && math.all(p <= s.Max + 0.6f) &&
                    (s.Block != 0f ? CaveSampling.Block(s, p) : CaveSampling.Rod(s, p)) < 0.6f)
                    return true;
            return false;
        }

        /// <summary>Whether a pool there would cover the way through a chamber: the axis of any passage joining it.</summary>
        static bool InTheWay(float2 q, float radius, int node, List<CaveNode> nodes, List<CaveSegment> segments, CaveData cave)
        {
            for (int i = cave.FirstSegment; i < cave.FirstSegment + cave.SegmentCount; i++)
            {
                var seg = segments[i];
                if (seg.A != node && seg.B != node)
                    continue;
                // The passage's line, carried on across the chamber.
                var dir = math.normalizesafe(nodes[seg.B].Position.xz - nodes[seg.A].Position.xz);
                var off = q - nodes[node].Position.xz;
                if (math.abs(off.x * dir.y - off.y * dir.x) < radius + 1.2f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// One of the type's styles listed for the biome the cave opens in (chosen by the cave's own roll when several
        /// are), else one listed for anywhere, else the first; none if the type lists no styles.
        /// </summary>
        static CaveStyle PickStyle(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, LandmarkType type,
                                   float2 position, ref Random rng)
        {
            if (type.CaveStyles.Count == 0)
                return null;
            var biome = Dominant(source.SampleGround(position).Biomes);
            var here = biome >= 0 && biome < palette.Count ? palette[biome] : null;
            var matches = new List<CaveStyle>();
            foreach (var s in type.CaveStyles)
                if (s != null && here != null && s.Biomes.Contains(here))
                    matches.Add(s);
            if (matches.Count == 0)
                foreach (var s in type.CaveStyles)
                    if (s != null && s.Biomes.Count == 0)
                        matches.Add(s);
            if (matches.Count == 0)
                return type.CaveStyles[0];
            return matches[rng.NextInt(matches.Count)];
        }

        static int Dominant(in BiomeWeights b)
        {
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (b.Weights[k] > b.Weights[best])
                    best = k;
            return b.Ids[best];
        }

        /// <summary>
        /// Lists every segment in the grid columns its box touches. Segments go in cave by cave, so each cell's items
        /// stay grouped by cave (sampling relies on it).
        /// </summary>
        static void BuildGrid(BlobBuilder builder, ref CaveSetBlob root, List<CaveData> caves, List<CaveNode> nodes, List<CaveSegment> segments)
        {
            float2 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var c = caves[seg.Cave];
                var a = nodes[seg.A];
                var b = nodes[seg.B];
                // The margin is measured in the cave's field, which runs slower than true distance along a stretch.
                var reach = math.max(a.Radius, b.Radius) * math.cmax(c.Profile)
                            + (CaveSampling.SegmentMargin + CaveSampling.WallReach(c)) * CaveSampling.Stretch(c.Profile);
                seg.Min = math.min(a.Position, b.Position) - reach;
                seg.Max = math.max(a.Position, b.Position) + reach;
                segments[i] = seg;
                min = math.min(min, seg.Min.xz);
                max = math.max(max, seg.Max.xz);
            }

            var segmentArray = builder.Allocate(ref root.Segments, segments.Count);
            for (int i = 0; i < segments.Count; i++)
                segmentArray[i] = segments[i];

            root.GridOrigin = min;
            root.CellSize = GridCell;
            var size = (int2)math.ceil((max - min) / GridCell) + 1;
            root.GridSize = size;
            var cells = new List<int>[size.x * size.y];
            for (int i = 0; i < segments.Count; i++)
            {
                var c0 = (int2)math.floor((segments[i].Min.xz - min) / GridCell);
                var c1 = (int2)math.floor((segments[i].Max.xz - min) / GridCell);
                for (int z = c0.y; z <= c1.y; z++)
                for (int x = c0.x; x <= c1.x; x++)
                    (cells[z * size.x + x] ??= new List<int>()).Add(i);
            }
            var cellArray = builder.Allocate(ref root.Cells, cells.Length);
            var items = new List<int>();
            for (int c = 0; c < cells.Length; c++)
            {
                cellArray[c] = new int2(items.Count, cells[c]?.Count ?? 0);
                if (cells[c] != null)
                    items.AddRange(cells[c]);
            }
            var itemArray = builder.Allocate(ref root.Items, items.Count);
            for (int i = 0; i < items.Count; i++)
                itemArray[i] = items[i];
        }
    }
}
