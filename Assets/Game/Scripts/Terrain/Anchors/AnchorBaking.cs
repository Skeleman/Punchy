using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Builds the anchor blob after caves (and, later, settlements) are baked. Everything is derived from the seed and
    /// the baked shapes, so every machine builds the same anchors.
    ///
    /// Caves: every chamber tall enough gets a Roost on its ceiling (bats hang there; more fit in bigger chambers), a
    /// Nest on a wall ledge, and, where the style's floor is soft, a Burrow at the foot of a wall. Near each mouth, in
    /// the daylight, a Nest on the wall (swallows, wrens). All cave anchors are sheltered.
    /// </summary>
    public static class AnchorBaking
    {
        const float GridCell = 32f;

        public static BlobAssetReference<AnchorSetBlob> Build(in TerrainSource source, IReadOnlyList<LandmarkType> types, Allocator allocator)
        {
            var anchors = new List<Anchor>();
            if (source.Caves.IsCreated)
                AddCaveAnchors(source, CaveStyleTable.Collect(types), anchors);
            if (anchors.Count == 0)
                return default;
            return BuildBlob(anchors, allocator);
        }

        static void AddCaveAnchors(in TerrainSource source, List<CaveStyle> styles, List<Anchor> anchors)
        {
            ref var set = ref source.Caves.Value;
            var terrain = source;   // local functions can't capture an in parameter
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                var style = cave.Style > 0 && cave.Style - 1 < styles.Count ? styles[cave.Style - 1] : null;
                var tags = (byte)(Anchor.Shelter | (cave.Style & 0x7F));
                var rng = new Random(math.hash(new int2(c, 0x5EED)) | 1u);
                var owner = Anchor.OwnerCave(c);

                Anchor Make(AnchorKind kind, float3 position, float2 facing, float size, int capacity) => new Anchor
                {
                    Position = position,
                    Facing = math.normalizesafe(facing, new float2(1f, 0f)),
                    Size = size,
                    Owner = owner,
                    Kind = kind,
                    Capacity = (byte)math.clamp(capacity, 0, 255),
                    Biome = (byte)Dominant(terrain.SampleGround(position.xz).Biomes),
                    Tags = tags
                };

                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var centre = set.Nodes[n].Position;
                    var radius = set.Nodes[n].Radius;
                    var floor = source.FloorUnder(centre, out var inCave);
                    if (!inCave || source.CaveDistance(centre) >= 0f)
                        continue;
                    var ceiling = Ceiling(source, centre);
                    // The way into the chamber: from the node before it, so anchors face along the cave.
                    var along = n > cave.FirstNode ? (centre - set.Nodes[n - 1].Position).xz : new float2(1f, 0f);

                    if (ceiling - floor > 2.5f)
                        anchors.Add(Make(AnchorKind.Roost, new float3(centre.x, ceiling, centre.z), along, radius * 0.5f, (int)math.round(radius * 3f)));

                    // A ledge part-way up a wall, and the foot of a wall, in random directions from the middle.
                    var dir = Direction(ref rng);
                    if (WallPoint(source, new float3(centre.x, math.lerp(floor, ceiling, 0.65f), centre.z), dir, out var ledge))
                        anchors.Add(Make(AnchorKind.Nest, ledge, -dir, 0.4f, 1));
                    if (style == null || style.Burrows)
                    {
                        dir = Direction(ref rng);
                        if (WallPoint(source, new float3(centre.x, floor + 0.4f, centre.z), dir, out var foot))
                        {
                            var ground = source.FloorUnder(foot + new float3(0f, 0.3f, 0f), out _);
                            anchors.Add(Make(AnchorKind.Burrow, new float3(foot.x, ground, foot.z), -dir, 0.6f, rng.NextInt(1, 4)));
                        }
                    }
                }

                // A nest on the wall just inside the mouth, where the daylight still reaches.
                var mouth = cave.Mouth;
                var inward = cave.NodeCount > 1 ? math.normalizesafe((set.Nodes[cave.FirstNode + 1].Position - mouth).xz) : new float2(1f, 0f);
                var side = rng.NextBool() ? new float2(-inward.y, inward.x) : new float2(inward.y, -inward.x);
                var at = mouth + new float3(inward.x, 0f, inward.y) * 3f + new float3(0f, 1.5f, 0f);
                if (source.CaveDistance(at) < 0f && WallPoint(source, at, side, out var mouthNest))
                    anchors.Add(Make(AnchorKind.Nest, mouthNest, -side, 0.4f, 1));
            }
        }

        /// <summary>The ceiling straight above a point in cave air (sphere-traced up to rock).</summary>
        static float Ceiling(in TerrainSource source, float3 p)
        {
            var y = p.y;
            for (int i = 0; i < 100; i++)
            {
                var d = source.CaveDistance(new float3(p.x, y, p.z));
                if (d >= -0.05f)
                    break;
                y += math.max(0.1f, -d * 0.8f);
            }
            return y;
        }

        /// <summary>The wall reached by going horizontally from a point in cave air, pulled back 0.3 m into the air.</summary>
        static bool WallPoint(in TerrainSource source, float3 from, float2 dir, out float3 wall)
        {
            var d3 = new float3(dir.x, 0f, dir.y);
            float t = 0f, previous = 0f;
            for (int i = 0; i < 100; i++)
            {
                var d = source.CaveDistance(from + d3 * t);
                if (d >= -0.3f)
                {
                    // Wall noise can carry a step past the mark; settle back between the last two steps.
                    for (int k = 0; k < 8; k++)
                    {
                        var mid = (previous + t) * 0.5f;
                        if (source.CaveDistance(from + d3 * mid) >= -0.3f) t = mid; else previous = mid;
                    }
                    wall = from + d3 * previous;
                    return previous > 0.5f;
                }
                previous = t;
                t += math.max(0.1f, -d * 0.8f);
            }
            wall = default;
            return false;
        }

        static float2 Direction(ref Random rng)
        {
            var a = rng.NextFloat(math.PI * 2f);
            return new float2(math.cos(a), math.sin(a));
        }

        static int Dominant(in BiomeWeights b)
        {
            var best = 0;
            for (int k = 1; k < 4; k++)
                if (b.Weights[k] > b.Weights[best])
                    best = k;
            return b.Ids[best];
        }

        static BlobAssetReference<AnchorSetBlob> BuildBlob(List<Anchor> anchors, Allocator allocator)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<AnchorSetBlob>();
            var anchorArray = builder.Allocate(ref root.Anchors, anchors.Count);
            float2 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                anchorArray[i] = anchors[i];
                min = math.min(min, anchors[i].Position.xz);
                max = math.max(max, anchors[i].Position.xz);
            }
            root.GridOrigin = min;
            root.CellSize = GridCell;
            var size = (int2)math.floor((max - min) / GridCell) + 1;
            root.GridSize = size;
            var cells = new List<int>[size.x * size.y];
            for (int i = 0; i < anchors.Count; i++)
            {
                var c = (int2)math.floor((anchors[i].Position.xz - min) / GridCell);
                (cells[c.y * size.x + c.x] ??= new List<int>()).Add(i);
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
            var blob = builder.CreateBlobAssetReference<AnchorSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }
    }
}
