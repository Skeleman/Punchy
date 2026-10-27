using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Lays out caves for landmarks whose type has one. The entrance opens in the landmark's cliff face (its +X side
    /// rises, so the cave runs into the rise), and the passage winds in and down to a chamber. Every node is kept
    /// well below the surface (after landmark shapes), so the passage never breaks out of the hillside.
    /// </summary>
    public static class CaveBaking
    {
        const float NodeSpacing = 7f;
        /// <summary>Column size of the segment grid (m): a few passage segments across, so cells stay short.</summary>
        const float GridCell = 32f;

        public static BlobAssetReference<CaveSetBlob> Build(in TerrainSource source, IReadOnlyList<LandmarkType> types, Allocator allocator)
        {
            if (!source.Landmarks.IsCreated)
                return default;
            ref var landmarks = ref source.Landmarks.Value;
            var caves = new List<CaveData>();
            var nodes = new List<CaveNode>();

            for (int i = 0; i < landmarks.Instances.Length; i++)
            {
                ref var inst = ref landmarks.Instances[i];
                var type = inst.Type < types.Count ? types[inst.Type] : null;
                if (type == null || !type.Cave)
                    continue;
                var rng = new Random(math.hash(new int2(i, 7717)) | 1u);
                var radius = type.Radius * inst.Scale;
                var r = type.CaveRadius * inst.Scale;
                var floor = inst.BaseHeight;
                var forward = inst.Facing;
                var first = nodes.Count;

                // The entrance: starting just outside the face, so the passage cuts cleanly into it.
                var start = inst.Position - forward * (radius * type.Edge * 0.5f + r * 0.6f);
                var centreY = floor + r * 0.4f;
                nodes.Add(new CaveNode { Position = new float3(start.x, centreY, start.y), Radius = r });
                var heading = math.atan2(forward.y, forward.x);
                var p = start;
                var steps = math.max(3, (int)math.ceil(type.CaveLength / NodeSpacing));
                for (int k = 1; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    if (k > 1)
                        heading += rng.NextFloat(-0.5f, 0.5f);
                    p += new float2(math.cos(heading), math.sin(heading)) * NodeSpacing;
                    // Down by the cave's depth, and always a good way under the surface above.
                    var y = floor + r * 0.4f - type.CaveDepth * math.smoothstep(0.1f, 1f, t);
                    var cover = source.SampleHeight(p) - r - 3f;
                    if (k > 1)
                        y = math.min(y, cover);
                    var nodeRadius = k == steps ? type.ChamberRadius * inst.Scale : r * rng.NextFloat(0.85f, 1.2f);
                    if (k == steps)
                        y = math.min(y, source.SampleHeight(p) - nodeRadius - 3f);
                    nodes.Add(new CaveNode { Position = new float3(p.x, y, p.y), Radius = nodeRadius });
                }

                float3 min = float.MaxValue, max = float.MinValue;
                for (int n = first; n < nodes.Count; n++)
                {
                    min = math.min(min, nodes[n].Position - nodes[n].Radius - 2.5f);
                    max = math.max(max, nodes[n].Position + nodes[n].Radius + 2.5f);
                }
                caves.Add(new CaveData
                {
                    FirstNode = first,
                    NodeCount = nodes.Count - first,
                    Min = min,
                    Max = max,
                    Mouth = nodes[first].Position,
                    Seed = rng.NextFloat(-50f, 50f)
                });
            }

            if (caves.Count == 0)
                return default;
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CaveSetBlob>();
            var caveArray = builder.Allocate(ref root.Caves, caves.Count);
            for (int i = 0; i < caves.Count; i++)
                caveArray[i] = caves[i];
            var nodeArray = builder.Allocate(ref root.Nodes, nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
                nodeArray[i] = nodes[i];
            BuildGrid(builder, ref root, caves, nodes);
            var blob = builder.CreateBlobAssetReference<CaveSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }

        /// <summary>
        /// Splits every cave into its segments and lists each segment in the grid columns its box touches. Segments go
        /// in cave by cave, so each cell's items stay grouped by cave (sampling relies on it).
        /// </summary>
        static void BuildGrid(BlobBuilder builder, ref CaveSetBlob root, List<CaveData> caves, List<CaveNode> nodes)
        {
            var segments = new List<CaveSegment>();
            float2 min = float.MaxValue, max = float.MinValue;
            for (int c = 0; c < caves.Count; c++)
                for (int i = 0; i < caves[c].NodeCount - 1; i++)
                {
                    var a = nodes[caves[c].FirstNode + i];
                    var b = nodes[caves[c].FirstNode + i + 1];
                    var reach = math.max(a.Radius, b.Radius) + CaveSampling.SegmentMargin;
                    var seg = new CaveSegment
                    {
                        Node = caves[c].FirstNode + i,
                        Cave = c,
                        Min = math.min(a.Position, b.Position) - reach,
                        Max = math.max(a.Position, b.Position) + reach
                    };
                    segments.Add(seg);
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
