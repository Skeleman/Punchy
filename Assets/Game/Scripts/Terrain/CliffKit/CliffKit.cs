using System.Collections.Generic;
using Punchy.Flora;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    /// <summary>
    /// One cliff kit piece (W-150, #281): a baked record (rule 22) of which kit it is, where it sits on the face, how it
    /// is turned (yaw to face out of the cliff, tilt about its own x), its scale, a strata tint and its climbing grade.
    /// Its boxes come from the kit id (CliffKit.Boxes), so the record is all a later map file needs to keep.
    /// </summary>
    public struct CliffPiece
    {
        public float3 Position;
        /// <summary>Yaw (radians) turns the kit's +z out of the face; tilt (radians) turns it about its own x.</summary>
        public float Yaw, Tilt;
        public float3 Scale;
        /// <summary>Multiplies the rock colour the ground has there (0.85..1.1).</summary>
        public float Tint;
        /// <summary>How far from Position any of its boxes reach (m, in xz), for the cell grid.</summary>
        public float Reach;
        public uint Seed;
        /// <summary>CliffKit.Slab ...; the climbing grade (row 2 reads it); the face it was put on (0 sea cliff, 1 mesa or canyon).</summary>
        public byte Kit, Grade, Face;
    }

    /// <summary>
    /// A ledge on a cliff kit piece (W-150 row 2): a near-level stretch of a box's top front edge with a drop of at
    /// least LedgeDrop under it and no rock sitting on it, the line a climber's hands shimmy along (A-101 reads these;
    /// shimmy ledges are data). A baked record (rule 22): it comes from the piece record and the ground alone.
    /// </summary>
    public struct CliffLedge
    {
        /// <summary>The ledge's ends, on the lip (world space).</summary>
        public float3 A, B;
        /// <summary>Level unit vector out of the face, away from the rock.</summary>
        public float3 Out;
        public int Piece;
        /// <summary>A-101's ledge grade (rock lip 3, an overhang's lip 4); Kind: CliffKit.LipLedge or OverhangLedge.</summary>
        public byte Grade, Kind;
    }

    /// <summary>
    /// The island's cliff kit (W-150 row 1): rock pieces the bake puts on the steepest sea cliff and mesa or canyon
    /// faces, built after every place with its own seed stream, so no landmark, cave or settlement hash sees it.
    /// Row 2 adds each piece's ledges, so climbing reads them rather than working them out.
    /// </summary>
    public struct CliffKitSetBlob
    {
        public BlobArray<CliffPiece> Pieces;
        /// <summary>Per cell: (first item, count) in Items.</summary>
        public BlobArray<int2> Cells;
        public BlobArray<int> Items;
        public BlobArray<CliffLedge> Ledges;
        /// <summary>Per piece: (first ledge, count) in Ledges.</summary>
        public BlobArray<int2> PieceLedges;
        public float2 Origin;
        public float CellSize;
        public int2 GridSize;
    }

    /// <summary>
    /// The kit's shapes: each piece is one to three rotated boxes in its own frame (x along the face, y up, z out of
    /// it), its back buried in the slope. Drawn from these boxes (CliffKitSystem) and collided with the same boxes
    /// (KitBox), so what you see is what you bump into. Plain maths, safe in Burst.
    /// </summary>
    public static class CliffKit
    {
        public const byte Slab = 0, Stacked = 1, Overhang = 2, Buttress = 3, Fallen = 4;
        public const int KitCount = 5;
        /// <summary>How far round a box plants and rocks stay out (m).</summary>
        const float Pad = 0.5f;

        /// <summary>The climbing grade each kit gets (row 2 turns it into holds and ledges).</summary>
        public static byte GradeOf(byte kit) => kit == Slab || kit == Overhang ? (byte)5 : kit == Fallen ? (byte)3 : (byte)4;

        static KitBox Box(float3 centre, float3 half, quaternion rotation) => new KitBox { Centre = centre, Half = half, Rotation = rotation };

        /// <summary>The kit's boxes in its own frame, before scale; small twists come from the piece's seed.</summary>
        public static void LocalBoxes(byte kit, uint seed, ref FixedList512Bytes<KitBox> list)
        {
            var rng = new Random(seed | 1u);
            var twist = rng.NextFloat(-1f, 1f);
            // Row 1b: every box at least ~40% as deep and tall as it is wide, and sunk 30-50% of its depth behind the
            // face (Position sits on the face), so a piece reads as rock standing out of the cliff, not a board on it.
            switch (kit)
            {
                case Slab:
                    // A thick ledge of rock, ~40% of it buried, rolled off the level and with a broken chunk risen at one
                    // end, so its front never reads as one straight plank.
                    var side = twist < 0f ? -1f : 1f;
                    list.Add(Box(new float3(0f, -0.5f, 0.5f), new float3(3.4f, 2.1f, 3f), quaternion.RotateZ(twist * 0.12f)));
                    list.Add(Box(new float3(side * 2.6f, 0.9f, 0.9f), new float3(1.8f, 1.6f, 2.4f), math.mul(quaternion.RotateY(twist * 0.3f), quaternion.RotateZ(-twist * 0.2f))));
                    break;
                case Stacked:
                    list.Add(Box(new float3(0f, -2.6f, 0.6f), new float3(3.6f, 1.8f, 3f), quaternion.RotateY(twist * 0.08f)));
                    list.Add(Box(new float3(0.5f, 1f, 0.3f), new float3(3f, 1.7f, 2.8f), quaternion.RotateY(twist * 0.18f)));
                    list.Add(Box(new float3(-0.4f, 3.9f, -0.4f), new float3(2.4f, 1.5f, 2.4f), quaternion.RotateY(-twist * 0.15f)));
                    break;
                case Overhang:
                    // An upper slab tipped up at its lip, a third of it buried, over a block set back into the face.
                    list.Add(Box(new float3(0f, 1.6f, 1.2f), new float3(4.4f, 1.8f, 3.6f), math.mul(quaternion.RotateX(-0.18f), quaternion.RotateZ(twist * 0.04f))));
                    list.Add(Box(new float3(0f, -2.6f, -0.9f), new float3(3.6f, 2.6f, 2.6f), quaternion.RotateY(twist * 0.1f)));
                    break;
                case Buttress:
                    list.Add(Box(new float3(0f, -3f, 0.5f), new float3(2.6f, 7f, 2.6f), quaternion.RotateY(twist * 0.3f)));
                    break;
                default:
                    // A block fallen to the foot: rolled and turned any way.
                    var roll = rng.NextFloat(-0.3f, 0.3f);
                    list.Add(Box(float3.zero, new float3(2.6f, 2.2f, 2.4f), math.mul(quaternion.RotateY(twist * math.PI), quaternion.RotateZ(roll))));
                    break;
            }
        }

        public static quaternion Frame(in CliffPiece p) => math.mul(quaternion.RotateY(p.Yaw), quaternion.RotateX(p.Tilt));

        /// <summary>The piece's boxes in the world.</summary>
        public static void Boxes(in CliffPiece p, ref FixedList512Bytes<KitBox> boxes)
        {
            var local = new FixedList512Bytes<KitBox>();
            LocalBoxes(p.Kit, p.Seed, ref local);
            var frame = Frame(p);
            for (int i = 0; i < local.Length; i++)
            {
                var b = local[i];
                var half = b.Half * p.Scale;
                boxes.Add(new KitBox
                {
                    Centre = p.Position + math.mul(frame, b.Centre * p.Scale),
                    Half = half,
                    Rotation = math.mul(frame, b.Rotation),
                    Reach = math.length(half)
                });
            }
        }

        /// <summary>
        /// Whether a piece stands over this spot: a box rises above the ground here or hangs over it (padded by Pad).
        /// Plants and rocks don't grow there (FloraPlacement.Place).
        /// </summary>
        public static bool Covered(in TerrainSource source, float2 xz)
        {
            if (!source.CliffKit.IsCreated)
                return false;
            ref var set = ref source.CliffKit.Value;
            var c = (int2)math.floor((xz - set.Origin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return false;
            var range = set.Cells[c.y * set.GridSize.x + c.x];
            var boxes = new FixedList512Bytes<KitBox>();
            var ground = float.NaN;
            for (int i = range.x; i < range.x + range.y; i++)
            {
                var piece = set.Pieces[set.Items[i]];
                if (math.distancesq(xz, piece.Position.xz) > math.square(piece.Reach + Pad))
                    continue;
                boxes.Clear();
                Boxes(piece, ref boxes);
                for (int k = 0; k < boxes.Length; k++)
                {
                    var b = boxes[k];
                    b.Half += Pad;
                    b.Reach = math.length(b.Half);
                    if (!KitCollision.Span(b, xz, out _, out var top))
                        continue;
                    if (math.isnan(ground))
                        ground = source.SampleHeight(xz);
                    if (top > ground - Pad)
                        return true;
                }
            }
            return false;
        }

        /// <summary>The pieces near any of these centres, as kit boxes for FloraSystem's obstacle (stood on, bumped into).</summary>
        public static void AddKitBoxes(in TerrainSource source, List<float2> centres, DynamicBuffer<KitBox> boxes, float range = 16f)   // collision
        {
            if (!source.CliffKit.IsCreated)
                return;
            ref var set = ref source.CliffKit.Value;
            var list = new FixedList512Bytes<KitBox>();
            for (int i = 0; i < set.Pieces.Length; i++)
            {
                var piece = set.Pieces[i];
                var reach = range + piece.Reach;
                var near = false;
                foreach (var c in centres)
                    near |= math.distancesq(c, piece.Position.xz) < reach * reach;
                if (!near)
                    continue;
                list.Clear();
                Boxes(piece, ref list);
                for (int k = 0; k < list.Length; k++)
                    boxes.Add(list[k]);
            }
        }

        // ---- Row 2: walk, stand, climb. TerrainSource.Deck and CeilingOver ask these, A-101 climbing asks GradeAt
        // and LedgeNear. Each looks only at the pieces in the 64 m cell (or cells) round the point.

        public const byte LipLedge = 0, OverhangLedge = 1;
        /// <summary>A body's headroom over a box top that stands it (a lower box roofed by its own upper box doesn't).</summary>
        const float Headroom = 1.8f; // collision
        /// <summary>How far under a box top a point may be and still stand on it (SettlementSampling.Deck's 0.6).</summary>
        const float DeckBelow = 0.6f; // collision

        /// <summary>The boxes of the pieces whose reach covers xz (up to ~90 boxes; a cell holds far fewer).</summary>
        static void BoxesAt(ref CliffKitSetBlob set, float2 xz, float margin, ref FixedList4096Bytes<KitBox> boxes, ref FixedList512Bytes<int> owners)
        {
            var c = (int2)math.floor((xz - set.Origin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return;
            var range = set.Cells[c.y * set.GridSize.x + c.x];
            var list = new FixedList512Bytes<KitBox>();
            for (int i = range.x; i < range.x + range.y; i++)
            {
                var index = set.Items[i];
                var piece = set.Pieces[index];
                if (math.distancesq(xz, piece.Position.xz) > math.square(piece.Reach + margin))
                    continue;
                list.Clear();
                Boxes(piece, ref list);
                for (int k = 0; k < list.Length && boxes.Length < boxes.Capacity && owners.Length < owners.Capacity; k++)
                {
                    boxes.Add(list[k]);
                    owners.Add(index);
                }
            }
        }

        /// <summary>
        /// The top of a piece a point stands on (W-150 row 2): the highest box top at xz that the point is on or up to
        /// DeckBelow under, above the ground there, with a body's headroom clear over it. So shelves are walked onto
        /// by everything that asks TerrainSource.Deck or FloorUnder (creatures, the player, a joined client alike).
        /// </summary>
        public static bool Deck(in TerrainSource source, float3 p, out float deck)
        {
            deck = float.MinValue;
            if (!source.CliffKit.IsCreated)
                return false;
            var boxes = new FixedList4096Bytes<KitBox>();
            var owners = new FixedList512Bytes<int>();
            BoxesAt(ref source.CliffKit.Value, p.xz, 0f, ref boxes, ref owners);
            if (boxes.Length == 0)
                return false;
            var ground = float.NaN;
            for (int i = 0; i < boxes.Length; i++)
            {
                if (!KitCollision.Span(boxes[i], p.xz, out _, out var top) || top > p.y + DeckBelow || top <= deck)
                    continue;
                if (math.isnan(ground))
                    ground = source.SampleHeight(p.xz);
                if (top < ground + 0.05f)
                    continue;
                var clear = true;
                for (int j = 0; j < boxes.Length && clear; j++)
                    clear = j == i || !KitCollision.Span(boxes[j], p.xz, out var b2, out var t2) || t2 <= top + 0.05f || b2 >= top + Headroom;
                if (clear)
                    deck = top;
            }
            return deck > float.MinValue;
        }

        /// <summary>
        /// The underside of the lowest piece over a point (W-150 row 2): an overhang's roof, for TerrainSource.CeilingOver
        /// out of cave air. False where no box hangs over it.
        /// </summary>
        public static bool Ceiling(in TerrainSource source, float3 p, out float ceiling)
        {
            ceiling = float.MaxValue;
            if (!source.CliffKit.IsCreated)
                return false;
            var boxes = new FixedList4096Bytes<KitBox>();
            var owners = new FixedList512Bytes<int>();
            BoxesAt(ref source.CliffKit.Value, p.xz, 0f, ref boxes, ref owners);
            for (int i = 0; i < boxes.Length; i++)
                if (KitCollision.Span(boxes[i], p.xz, out var bottom, out _) && bottom > p.y && bottom < ceiling)
                    ceiling = bottom;
            return ceiling < float.MaxValue;
        }

        /// <summary>
        /// The climbing grade of the nearest piece face within reach of a point (A-101's scale: 5 smooth wall, 4 craggy,
        /// 3 a fallen block; 0 = no face), with that face's outward normal and the piece. Only side faces count (tops are
        /// stood on, undersides are overhangs). Reach must stay under 1 m, the cell grid's pad.
        /// </summary>
        public static byte GradeAt(in TerrainSource source, float3 p, float reach, out float3 normal, out int piece)
        {
            normal = default;
            piece = -1;
            if (!source.CliffKit.IsCreated)
                return 0;
            ref var set = ref source.CliffKit.Value;
            var boxes = new FixedList4096Bytes<KitBox>();
            var owners = new FixedList512Bytes<int>();
            BoxesAt(ref set, p.xz, reach, ref boxes, ref owners);
            var best = reach;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                var o = math.mul(math.conjugate(b.Rotation), p - b.Centre);
                var excess = math.abs(o) - b.Half;
                var distance = math.length(math.max(excess, 0f));
                if (distance > best)
                    continue;
                var axis = excess.x >= excess.y && excess.x >= excess.z ? 0 : excess.y >= excess.z ? 1 : 2;
                var local = float3.zero;
                local[axis] = o[axis] < 0f ? -1f : 1f;
                var n = math.mul(b.Rotation, local);
                if (math.abs(n.y) > 0.6f)
                    continue;
                best = distance;
                normal = n;
                piece = owners[i];
            }
            return piece < 0 ? (byte)0 : set.Pieces[piece].Grade;
        }

        /// <summary>
        /// The nearest ledge within reach of a point (A-101's shimmy): the ledge record and the closest point on it.
        /// Reach must stay under 1 m, the cell grid's pad.
        /// </summary>
        public static bool LedgeNear(in TerrainSource source, float3 p, float reach, out CliffLedge ledge, out float3 at)
        {
            ledge = default;
            at = default;
            if (!source.CliffKit.IsCreated)
                return false;
            ref var set = ref source.CliffKit.Value;
            var c = (int2)math.floor((p.xz - set.Origin) / set.CellSize);
            if (math.any(c < 0) || math.any(c >= set.GridSize))
                return false;
            var range = set.Cells[c.y * set.GridSize.x + c.x];
            var best = reach * reach;
            var found = false;
            for (int i = range.x; i < range.x + range.y; i++)
            {
                var index = set.Items[i];
                if (math.distancesq(p.xz, set.Pieces[index].Position.xz) > math.square(set.Pieces[index].Reach + reach))
                    continue;
                var ledges = set.PieceLedges[index];
                for (int l = ledges.x; l < ledges.x + ledges.y; l++)
                {
                    var e = set.Ledges[l];
                    var ab = e.B - e.A;
                    var q = e.A + ab * math.saturate(math.dot(p - e.A, ab) / math.max(math.lengthsq(ab), 1e-6f));
                    var d = math.distancesq(p, q);
                    if (d > best)
                        continue;
                    best = d;
                    ledge = e;
                    at = q;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Bake (W-150 row 2): each box's top front edge where it is near level, faces out, stands LedgeDrop or more over
        /// the ground below its lip and has no other box of its piece sitting on it; the longest such run of 1 m or more
        /// is a ledge.
        /// </summary>
        public static void BakeLedges(in TerrainSource source, int index, in CliffPiece piece, List<CliffLedge> ledges)
        {
            const float LedgeDrop = 1.5f, MinLength = 1f; // gameplay: climbing reach, not a view range
            // About a metre between samples on a 7-10 m lip (each is a ground sample: the bake's cost).
            const int Samples = 8;
            var boxes = new FixedList512Bytes<KitBox>();
            Boxes(piece, ref boxes);
            for (int k = 0; k < boxes.Length; k++)
            {
                var b = boxes[k];
                if (math.mul(b.Rotation, new float3(0f, 1f, 0f)).y < 0.8f)
                    continue;
                var a = b.Centre + math.mul(b.Rotation, new float3(-b.Half.x, b.Half.y, b.Half.z));
                var e = b.Centre + math.mul(b.Rotation, new float3(b.Half.x, b.Half.y, b.Half.z));
                if (math.abs(e.y - a.y) > math.distance(a, e) * 0.34f)
                    continue;
                var outward = math.mul(b.Rotation, new float3(0f, 0f, 1f));
                outward.y = 0f;
                if (math.lengthsq(outward) < 1e-4f)
                    continue;
                outward = math.normalize(outward);
                int runStart = -1, bestStart = 0, bestEnd = -1;
                for (int s = 0; s <= Samples; s++)
                {
                    // Hand room on the lip: the ground half a metre out is far enough down, and no box of the piece
                    // sits on the lip within a metre (a stacked block's lower lip under the one above isn't a ledge).
                    var q = math.lerp(a, e, s / (float)Samples);
                    var ok = q.y - source.SampleHeight(q.xz + outward.xz * 0.5f) >= LedgeDrop;
                    for (int j = 0; j < boxes.Length && ok; j++)
                        ok = j == k || !KitCollision.Span(boxes[j], q.xz - outward.xz * 0.3f, out var bottom, out var top) || top <= q.y + 0.05f || bottom >= q.y + 1f;
                    if (ok && runStart < 0)
                        runStart = s;
                    if (ok && s - runStart > bestEnd - bestStart)
                    {
                        bestStart = runStart;
                        bestEnd = s;
                    }
                    if (!ok)
                        runStart = -1;
                }
                if (bestEnd < 0)
                    continue;
                var from = math.lerp(a, e, bestStart / (float)Samples);
                var to = math.lerp(a, e, bestEnd / (float)Samples);
                if (math.distance(from, to) < MinLength)
                    continue;
                var overhang = piece.Kit == Overhang && k == 0;
                ledges.Add(new CliffLedge
                {
                    A = from,
                    B = to,
                    Out = outward,
                    Piece = index,
                    Grade = overhang ? (byte)4 : (byte)3,
                    Kind = overhang ? OverhangLedge : LipLedge
                });
            }
        }
    }

    /// <summary>
    /// Bakes the cliff kit (W-150 row 1) from the finished ground: the steepest 4 m cell of every 16 m block, where the
    /// slope passes 50 degrees with at least 8 m of relief round it, on a sea cliff or a mesa or canyon face (W-149's
    /// classes), clear of settlements, landmarks and cave mouths, gets a piece by a seeded roll. Fallen blocks walk down
    /// to the foot. Its own seed stream, after every place, so landmarks and settlements never see it.
    /// </summary>
    public static class CliffKitBaking
    {
        const float Step = 4f, GridCell = 64f;
        const int BlockCells = 4;
        /// <summary>Steeper than this (rise over run, tan 50 degrees) counts as a cliff face; under tan 30 the foot is reached.</summary>
        static readonly float Steep = math.tan(math.radians(50f)), Foot = math.tan(math.radians(30f));
        const float MinRelief = 8f, Chance = 0.5f;
        /// <summary>Metres from any piece before a new group starts; metres between pieces of a group.</summary>
        const float GroupSpacing = 26f, MemberSpacing = 6f;
        /// <summary>Metres between neighbouring 16 m blocks before a block's 4 m cells are looked at.</summary>
        const float CoarseStep = 10f;
        const byte SeaFace = 0, MesaFace = 1, NoFace = 255;
        static readonly string[] KitNames = { "slab", "stacked", "overhang", "buttress", "fallen" };

        public static string LastLog = "";

        /// <summary>The stage's own stream: the world seed with "cliff kit" mixed in.</summary>
        static uint StreamSeed(uint seed) => BiomeSampling.Scramble(seed ^ 0xC11FF1D5u);

        struct Candidate
        {
            public float2 Position, Dir;
            public float Height, Angle;
            public byte Face;
        }

        public static BlobAssetReference<CliffKitSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> palette, Allocator allocator)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var src = source;
            var size = source.World.WorldSize;
            var half = size * 0.5f;
            var sea = source.World.SeaLevel;
            var n = (int)(size / Step);
            var bn = n / BlockCells;
            // A coarse pass first, one sample per 16 m block: only blocks with a cliff's relief round them get the fine
            // 4 m cells (sampling the whole island at 4 m took seconds).
            var coarse = new float[bn * bn];
            BakeParallel.For(bn, bz =>
            {
                for (int bx = 0; bx < bn; bx++)
                    coarse[bz * bn + bx] = src.SampleHeight(new float2(-half + (bx + 0.5f) * BlockCells * Step, -half + (bz + 0.5f) * BlockCells * Step));
            });
            // Blocks worth a closer look, then the 4 m cells they (and two cells round them) need, sampled once each.
            var look = new bool[bn * bn];
            var need = new bool[n * n];
            int looked = 0;
            for (int bz = 1; bz < bn - 1; bz++)
            for (int bx = 1; bx < bn - 1; bx++)
            {
                // The steepest step from this block to a neighbour, 16 m apart: a cliff shows at least a few metres.
                var c = coarse[bz * bn + bx];
                float chi = c, step = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var v = coarse[(bz + dz) * bn + bx + dx];
                    chi = math.max(chi, v);
                    step = math.max(step, math.abs(v - c) / math.max(1f, math.abs(dx) + math.abs(dz) - 0.6f));
                }
                if (chi < sea + 0.5f || step < CoarseStep)
                    continue;
                look[bz * bn + bx] = true;
                looked++;
                for (int z = bz * BlockCells - 2; z < (bz + 1) * BlockCells + 2; z++)
                for (int x = bx * BlockCells - 2; x < (bx + 1) * BlockCells + 2; x++)
                    need[z * n + x] = true;
            }
            var h = new float[n * n];
            BakeParallel.For(n, z =>
            {
                for (int x = 0; x < n; x++)
                    if (need[z * n + x])
                        h[z * n + x] = src.SampleHeight(new float2(-half + (x + 0.5f) * Step, -half + (z + 0.5f) * Step));
            });
            var coarseMs = watch.ElapsedMilliseconds;
            var mesaId = BiomeTags.First(palette, BiomeTags.RidgeMesa);
            var found = new Candidate[bn * bn];
            BakeParallel.For(bn, bz =>
            {
                for (int bx = 0; bx < bn; bx++)
                {
                    found[bz * bn + bx] = new Candidate { Face = NoFace };
                    if (!look[bz * bn + bx])
                        continue;
                    var best = -1f;
                    int bestX = 0, bestZ = 0;
                    float2 bestGrad = 0f;
                    for (int dz = 0; dz < BlockCells; dz++)
                    for (int dx = 0; dx < BlockCells; dx++)
                    {
                        int x = bx * BlockCells + dx, z = bz * BlockCells + dz;
                        if (h[z * n + x] < sea + 0.5f)
                            continue;
                        var grad = new float2(h[z * n + x + 1] - h[z * n + x - 1], h[(z + 1) * n + x] - h[(z - 1) * n + x]) / (2f * Step);
                        var sl = math.length(grad);
                        if (sl > best)
                        {
                            best = sl;
                            bestX = x;
                            bestZ = z;
                            bestGrad = grad;
                        }
                    }
                    if (best < Steep)
                        continue;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        var v = h[(bestZ + dz) * n + bestX + dx];
                        lo = math.min(lo, v);
                        hi = math.max(hi, v);
                    }
                    if (hi - lo < MinRelief)
                        continue;
                    var p = new float2(-half + (bestX + 0.5f) * Step, -half + (bestZ + 0.5f) * Step);
                    var face = Classify(src, p, sea, mesaId);
                    if (face == NoFace)
                        continue;
                    found[bz * bn + bx] = new Candidate { Position = p, Dir = -math.normalize(bestGrad), Height = h[bestZ * n + bestX], Angle = math.atan(best), Face = face };
                }
            });
            var blocksMs = watch.ElapsedMilliseconds;
            // In block order, so every machine accepts the same pieces. Row 1b: fewer, bigger pieces in groups (a ledge
            // run along the contour, a buttress with blocks fallen at its foot, a stack or an overhang with a block
            // below), each group well clear of the last.
            var stream = StreamSeed(source.World.Seed);
            var pieces = new List<CliffPiece>();
            var near = new Dictionary<int2, List<int>>();
            int steep = 0, keptOut = 0, groups = 0;
            var kits = new int[CliffKit.KitCount];
            int seaCount = 0, mesaCount = 0;
            var boxes = new FixedList512Bytes<Punchy.Flora.KitBox>();

            // Whether any piece already stands within this many metres (cells of GroupSpacing, so 3x3 covers it).
            bool Crowded(float2 xz, float within)
            {
                var c = (int2)math.floor(xz / GroupSpacing);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (near.TryGetValue(c + new int2(dx, dz), out var list))
                        foreach (var i in list)
                            if (math.distance(pieces[i].Position.xz, xz) < within)
                                return true;
                return false;
            }

            void Add(ref Random rng, byte kit, float3 at, float2 dir, float angle, float size, byte face)
            {
                if (KeptOut(src, at.xz))
                {
                    keptOut++;
                    return;
                }
                if (Crowded(at.xz, MemberSpacing))
                    return;
                float tilt;
                switch (kit)
                {
                    case CliffKit.Buttress: tilt = -(math.PI * 0.5f - angle) * 0.7f; break;
                    case CliffKit.Overhang: tilt = 0f; break;
                    case CliffKit.Fallen: tilt = rng.NextFloat(-0.25f, 0.25f); break;
                    // A slab tipped and turned well off the face, so its front shows a side and never reads flat.
                    case CliffKit.Slab: tilt = rng.NextFloat(-0.15f, 0.15f); break;
                    default: tilt = rng.NextFloat(-0.06f, 0.06f); break;
                }
                var piece = new CliffPiece
                {
                    Position = at,
                    Yaw = math.atan2(dir.x, dir.y) + rng.NextFloat(-0.15f, 0.15f) * (kit == CliffKit.Slab ? 2.4f : 1f),
                    Tilt = tilt,
                    Scale = size * rng.NextFloat3(0.88f, 1.12f),
                    Tint = rng.NextFloat(0.85f, 1.1f),
                    Seed = rng.NextUInt(),
                    Kit = kit,
                    Grade = CliffKit.GradeOf(kit),
                    Face = face
                };
                if (kit == CliffKit.Fallen)
                    piece.Position.y += 0.35f * 2.2f * piece.Scale.y;
                boxes.Clear();
                CliffKit.Boxes(piece, ref boxes);
                var reach = 0f;
                for (int k = 0; k < boxes.Length; k++)
                    reach = math.max(reach, math.distance(boxes[k].Centre.xz, piece.Position.xz) + boxes[k].Reach);
                piece.Reach = reach;
                var cell = (int2)math.floor(piece.Position.xz / GroupSpacing);
                if (!near.TryGetValue(cell, out var inCell))
                    near.Add(cell, inCell = new List<int>());
                inCell.Add(pieces.Count);
                pieces.Add(piece);
                kits[kit]++;
                if (face == SeaFace) seaCount++; else mesaCount++;
            }

            // Blocks fallen at the face's foot, spread along it.
            void Fallen(ref Random rng, in Candidate cand, int count, float size)
            {
                if (!FindFoot(src, cand.Position, cand.Dir, sea, out var foot))
                    return;
                var along = new float2(-cand.Dir.y, cand.Dir.x);
                for (int i = 0; i < count; i++)
                {
                    var q = foot.xz + along * rng.NextFloat(-6f, 6f) + cand.Dir * rng.NextFloat(0f, 4f);
                    var g = src.SampleGround(q);
                    if (g.River > 0.05f || (g.HasWater && g.WaterSurface - g.Height > 1.5f))
                        continue;
                    Add(ref rng, CliffKit.Fallen, new float3(q.x, g.Height, q.y), cand.Dir, cand.Angle, size * rng.NextFloat(0.6f, 0.95f), cand.Face);
                }
            }

            for (int b = 0; b < found.Length; b++)
            {
                var cand = found[b];
                if (cand.Face == NoFace)
                    continue;
                steep++;
                int bx = b % bn, bz = b / bn;
                var rng = new Random(BiomeSampling.Scramble(stream ^ math.hash(new int2(bx, bz))) | 1u);
                if (rng.NextFloat() > Chance || Crowded(cand.Position, GroupSpacing))
                    continue;
                if (KeptOut(src, cand.Position))
                {
                    keptOut++;
                    continue;
                }
                groups++;
                var roll = rng.NextFloat();
                var grow = rng.NextFloat(1f, 1.4f);
                var at = new float3(cand.Position.x, cand.Height, cand.Position.y);
                if (roll < 0.4f)
                {
                    // A ledge run: two or three slabs along the contour, each on the face where it reaches that height.
                    var count = rng.NextFloat() < 0.5f ? 2 : 3;
                    var along = new float2(-cand.Dir.y, cand.Dir.x);
                    for (int i = 0; i < count; i++)
                    {
                        var q = cand.Position + along * ((i - (count - 1) * 0.5f) * 8.5f * grow);
                        var hq = src.SampleHeight(q);
                        var nq = src.SampleNormal(q, 2f);
                        if (math.abs(hq - cand.Height) > 6f || nq.y > math.cos(math.radians(45f)))
                            continue;
                        var dq = math.normalizesafe(nq.xz, cand.Dir);
                        Add(ref rng, CliffKit.Slab, new float3(q.x, hq + rng.NextFloat(-1f, 1f), q.y), dq, cand.Angle, grow, cand.Face);
                    }
                }
                else if (roll < 0.65f)
                {
                    Add(ref rng, CliffKit.Buttress, at, cand.Dir, cand.Angle, grow, cand.Face);
                    Fallen(ref rng, cand, rng.NextFloat() < 0.5f ? 1 : 2, grow);
                }
                else if (roll < 0.85f)
                {
                    Add(ref rng, CliffKit.Stacked, at, cand.Dir, cand.Angle, grow, cand.Face);
                    if (rng.NextFloat() < 0.4f)
                        Fallen(ref rng, cand, 1, grow);
                }
                else
                {
                    Add(ref rng, CliffKit.Overhang, at, cand.Dir, cand.Angle, grow, cand.Face);
                    if (rng.NextFloat() < 0.4f)
                        Fallen(ref rng, cand, 1, grow);
                }
            }

            var acceptMs = watch.ElapsedMilliseconds;
            // The cell grid: each piece in every cell its reach (and the plants' pad) touches.
            var gs = new int2((int)math.ceil(size / GridCell));
            var cells = new List<int>[gs.x * gs.y];
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                var r = p.Reach + 1f;
                var lo = math.clamp((int2)math.floor((p.Position.xz - r + half) / GridCell), 0, gs - 1);
                var hi = math.clamp((int2)math.floor((p.Position.xz + r + half) / GridCell), 0, gs - 1);
                for (int z = lo.y; z <= hi.y; z++)
                for (int x = lo.x; x <= hi.x; x++)
                    (cells[z * gs.x + x] ??= new List<int>()).Add(i);
            }
            // Row 2: each piece's ledges, from its record and the ground (no rolls, so no seed stream moves).
            var ledgeAt = watch.ElapsedMilliseconds;
            var perPiece = new List<CliffLedge>[pieces.Count];
            BakeParallel.For(pieces.Count, i => CliffKit.BakeLedges(src, i, pieces[i], perPiece[i] = new List<CliffLedge>()));
            var ledges = new List<CliffLedge>();
            var pieceLedges = new int2[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                pieceLedges[i] = new int2(ledges.Count, perPiece[i].Count);
                ledges.AddRange(perPiece[i]);
            }
            var ledgeMs = watch.ElapsedMilliseconds - ledgeAt;
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CliffKitSetBlob>();
            var outLedges = builder.Allocate(ref root.Ledges, ledges.Count);
            for (int i = 0; i < ledges.Count; i++)
                outLedges[i] = ledges[i];
            var outPieceLedges = builder.Allocate(ref root.PieceLedges, pieceLedges.Length);
            for (int i = 0; i < pieceLedges.Length; i++)
                outPieceLedges[i] = pieceLedges[i];
            var outPieces = builder.Allocate(ref root.Pieces, pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
                outPieces[i] = pieces[i];
            var outCells = builder.Allocate(ref root.Cells, cells.Length);
            var itemCount = 0;
            foreach (var l in cells)
                itemCount += l?.Count ?? 0;
            var outItems = builder.Allocate(ref root.Items, itemCount);
            var at2 = 0;
            for (int c = 0; c < cells.Length; c++)
            {
                var l = cells[c];
                outCells[c] = new int2(at2, l?.Count ?? 0);
                if (l == null)
                    continue;
                foreach (var i in l)
                    outItems[at2++] = i;
            }
            root.Origin = new float2(-half);
            root.CellSize = GridCell;
            root.GridSize = gs;
            var blob = builder.CreateBlobAssetReference<CliffKitSetBlob>(allocator);
            builder.Dispose();

            var kitText = new System.Text.StringBuilder();
            for (int k = 0; k < CliffKit.KitCount; k++)
                kitText.Append(k == 0 ? "" : ", ").Append(KitNames[k]).Append(' ').Append(kits[k]);
            LastLog = $"[cliffkit] {pieces.Count} pieces in {groups} groups, {ledges.Count} ledges (sea cliff {seaCount}, mesa/canyon {mesaCount}; {kitText}) from {steep} steep blocks, {keptOut} kept out, {watch.ElapsedMilliseconds} ms ({looked} blocks looked at; heights {coarseMs}, faces {blocksMs - coarseMs}, accept {acceptMs - blocksMs}, grid {watch.ElapsedMilliseconds - acceptMs - ledgeMs}, ledges {ledgeMs})";
            return blob;
        }

        /// <summary>W-149's face classes, narrowed to the two this row dresses: water round it (sea first, then a river or
        /// lake, which get no pieces), else a mesa or canyon face, else none.</summary>
        static byte Classify(in TerrainSource s, float2 p, float sea, int mesaId)
        {
            var here = s.SampleGround(p);
            if (here.HasWater || here.River > 0.01f)
                return NoFace;
            bool seaNear = false, otherWater = false;
            for (int a = 0; a < 8; a++)
            {
                var o = new float2(math.cos(a * math.PI / 4f), math.sin(a * math.PI / 4f));
                for (float r = 8f; r <= 40f; r += 16f) // gameplay: bake placement, not a view range
                {
                    var g = s.SampleGround(p + o * r);
                    if (g.River > 0.05f) otherWater = true;
                    else if (g.HasWater && math.abs(g.WaterSurface - sea) < 0.1f) seaNear = true;
                    else if (g.HasWater) otherWater = true;
                }
            }
            if (seaNear)
                return SeaFace;
            if (otherWater || mesaId < 0)
                return NoFace;
            s.SampleBiomes(p, out _, out var shape);
            var w = 0f;
            for (int i = 0; i < 4; i++)
                if (shape.Ids[i] == mesaId)
                    w += shape.Weights[i];
            return w > 0.5f ? MesaFace : NoFace;
        }

        /// <summary>Downhill from the face until the ground flattens (under 30 degrees), in water no deeper than 1.5 m.</summary>
        static bool FindFoot(in TerrainSource s, float2 p, float2 dir, float sea, out float3 foot)
        {
            foot = default;
            for (int k = 1; k <= 15; k++)
            {
                var q = p + dir * (Step * k);
                var hq = s.SampleHeight(q);
                var ahead = s.SampleHeight(q + dir * Step);
                if ((hq - ahead) / Step > Foot)
                    continue;
                var g = s.SampleGround(q);
                if (g.River > 0.05f || (g.HasWater && g.WaterSurface - hq > 1.5f))
                    return false;
                foot = new float3(q.x, hq, q.y);
                return true;
            }
            return false;
        }

        /// <summary>Clear of settlements, landmarks (pieces take no landmark spots) and cave mouths.</summary>
        static bool KeptOut(in TerrainSource s, float2 xz)
        {
            if (s.Settlements.IsCreated)
            {
                ref var set = ref s.Settlements.Value;
                for (int i = 0; i < set.Settlements.Length; i++)
                    if (math.distance(xz, set.Settlements[i].Centre) < set.Settlements[i].Radius + 20f)
                        return true;
            }
            if (s.Landmarks.IsCreated)
            {
                ref var set = ref s.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    var inst = set.Instances[i];
                    if (math.distance(xz, inst.Position) < set.Types[inst.Type].Radius * inst.Scale + 15f)
                        return true;
                }
            }
            s.MouthCut(xz, s.SampleHeight(xz), out var cave);
            return cave < 12f; // gameplay: bake keep-out from cave mouths
        }
    }
}
