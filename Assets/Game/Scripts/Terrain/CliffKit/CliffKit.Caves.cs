using System.Collections.Generic;
using Punchy.Flora;
using Unity.Collections;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    public static partial class CliffKit
    {
        public const byte CaveFace = 2;

        // Deck only stands tops above the ground, so cave shelves need their own top under FloorUnder.
        public static float CaveDeck(in TerrainSource source, float3 p, float floor)
        {
            if (!source.CliffKit.IsCreated)
                return floor;
            ref var set = ref source.CliffKit.Value;
            var boxes = new FixedList4096Bytes<KitBox>();
            var owners = new FixedList512Bytes<int>();
            BoxesAt(ref set, p.xz, 0f, ref boxes, ref owners);
            var deck = floor;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (set.Pieces[owners[i]].Face != CaveFace || !KitCollision.Span(boxes[i], p.xz, out _, out var top) ||
                    top > p.y + DeckBelow || top <= deck)
                    continue;
                var clear = true;
                for (int j = 0; j < boxes.Length && clear; j++)
                    clear = j == i || !KitCollision.Span(boxes[j], p.xz, out var b2, out var t2) || t2 <= top + 0.05f || b2 >= top + Headroom;
                if (clear)
                    deck = top;
            }
            return deck;
        }
    }

    public static partial class CliffKitBaking
    {
        const float ShelfLow = 2.6f, ShelfHigh = 3.4f, ShelfSpacing = 6f; // gameplay
        const float ShelfChance = 0.5f;

        // Runs after every other piece on its own per-cave rolls, so no other piece moves.
        static int CaveShelves(in TerrainSource source, uint stream, List<CliffPiece> pieces)
        {
            var src = source;
            if (!src.Caves.IsCreated)
                return 0;
            ref var set = ref src.Caves.Value;
            var added = 0;
            var boxes = new FixedList512Bytes<KitBox>();
            var placed = new List<float3>();
            for (int c = 0; c < set.Caves.Length; c++)
            {
                var cave = set.Caves[c];
                if (cave.Tunnel > 0)
                    continue;
                var rng = new Random(BiomeSampling.Scramble(stream ^ 0x5E1F5EEDu ^ cave.Key) | 1u);
                placed.Clear();
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber || rng.NextFloat() >= ShelfChance)
                        continue;
                    var want = rng.NextInt(1, 3);
                    for (int attempt = 0; attempt < 6 && want > 0; attempt++)
                    {
                        var angle = rng.NextFloat(0f, math.PI * 2f);
                        var lift = rng.NextFloat(ShelfLow, ShelfHigh);
                        var scale = new float3(rng.NextFloat(0.25f, 0.6f), rng.NextFloat(0.3f, 0.4f), rng.NextFloat(0.28f, 0.4f));
                        var tilt = rng.NextFloat(-0.05f, 0.05f);
                        var tint = rng.NextFloat(0.85f, 1.1f);
                        var seed = rng.NextUInt();
                        if (!Wall(src, ref set, c, set.Nodes[n], angle, lift, scale.x * 3.4f, out var lip, out var nh))
                            continue;
                        var crowded = false;
                        foreach (var q in placed)
                            crowded |= math.distance(q, lip) < ShelfSpacing;
                        if (crowded)
                            continue;
                        var piece = new CliffPiece
                        {
                            Position = lip - new float3(0f, 1.6f * scale.y, 0f),
                            Yaw = math.atan2(nh.x, nh.y),
                            Tilt = tilt,
                            Scale = scale,
                            Tint = tint,
                            Seed = seed,
                            Kit = CliffKit.Slab,
                            Grade = CliffKit.GradeOf(CliffKit.Slab),
                            Face = CliffKit.CaveFace
                        };
                        boxes.Clear();
                        CliffKit.Boxes(piece, ref boxes);
                        var reach = 0f;
                        for (int k = 0; k < boxes.Length; k++)
                            reach = math.max(reach, math.distance(boxes[k].Centre.xz, piece.Position.xz) + boxes[k].Reach);
                        piece.Reach = reach;
                        pieces.Add(piece);
                        placed.Add(lip);
                        added++;
                        want--;
                    }
                }
            }
            return added;
        }

        static bool Wall(in TerrainSource source, ref CaveSetBlob set, int c, CaveNode node, float angle, float lift, float halfWidth,
                         out float3 lip, out float2 nh)
        {
            lip = default;
            nh = default;
            var up = new float3(0f, 1f, 0f);
            if (!Air(ref set, c, node.Position, 0f) || !CaveWallMesh.March(ref set, node.Position, -up, node.Radius * 3f + 6f, out var floor))
                return false;
            var dir = new float3(math.cos(angle), 0f, math.sin(angle));
            var from = floor + up * 2f;
            if (!Air(ref set, c, from, 0f) || !CaveWallMesh.March(ref set, from, dir, 30f, out var hit))
                return false;
            var normal = CaveWallMesh.Normal(ref set, hit);
            if (math.abs(normal.y) > 0.4f || !Air(ref set, c, hit + normal * 0.25f, 0f))
                return false;
            nh = math.normalize(normal.xz);
            var out3 = new float3(nh.x, 0f, nh.y);
            var foot = source.FloorUnder(new float3(hit.x + nh.x, hit.y, hit.z + nh.y), out var inCave);
            if (!inCave || math.abs(foot - floor.y) > 1.5f)
                return false;
            var start = new float3(hit.x, foot + lift, hit.z) + out3 * 2.5f;
            if (!Air(ref set, c, start, 0f) || !CaveWallMesh.March(ref set, start, -out3, 5f, out lip))
                return false;
            var side = new float3(-nh.y, 0f, nh.x);
            if (lip.y > source.SampleHeight(lip.xz) - 4f || math.distance(lip, set.Caves[c].Mouth) < 12f ||
                math.abs(CaveWallMesh.Normal(ref set, lip).y) > 0.5f ||
                !Air(ref set, c, lip + out3 * 0.6f + up * 0.3f, 0.15f) || !Air(ref set, c, lip + out3 * 0.6f + up * 1.9f, 0.15f) ||
                !Air(ref set, c, lip + out3 * 0.8f - up * 1.2f, 0.15f) || !Rock(ref set, lip - out3 * 1.4f - up * 0.5f) ||
                !Rock(ref set, lip + side * halfWidth - out3 * 0.4f - up * 0.5f) || !Rock(ref set, lip - side * halfWidth - out3 * 0.4f - up * 0.5f))
                return false;
            var cave = set.Caves[c];
            for (int r = cave.FirstRoom; r < cave.FirstRoom + cave.RoomCount; r++)
            {
                var room = set.Rooms[r];
                var l = room.Local(lip);
                if (math.abs(l.x) < room.HX + 3f && l.z > -3f && l.z < room.HZ * 2f + 3f && l.y > -3f && l.y < room.Height + 3f)
                    return false;
            }
            for (int p = cave.FirstPool; p < cave.FirstPool + cave.PoolCount; p++)
            {
                var pool = set.Pools[p];
                if (math.distance(pool.Centre.xz, lip.xz) < math.max(pool.Radius, pool.Span) + 2f && math.abs(pool.Centre.y - foot) < 6f)
                    return false;
            }
            return true;
        }

        static bool Air(ref CaveSetBlob set, int c, float3 p, float margin) => CaveSampling.Distance(ref set, p, out var owner) < -margin && owner == c;

        static bool Rock(ref CaveSetBlob set, float3 p) => CaveSampling.Distance(ref set, p, out _) > 0.2f;
    }
}
