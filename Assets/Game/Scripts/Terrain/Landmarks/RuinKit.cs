using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>A ruin room in its landmark's frame (u, y, v): rectangle, floor (rising by Slope towards -u from U1, for
    /// an open ramp), ceiling, and kind (0 closed room, 1 open to the sky: an entrance cut through the heightmap).</summary>
    public struct RuinRoom
    {
        public float U0, U1, V0, V1, Floor, Ceil, Slope;
        public int Kind;
        public bool Contains(float u, float v) => u >= U0 && u <= U1 && v >= V0 && v <= V1;
        public float FloorAt(float u) => Floor + Slope * (U1 - u);
    }

    /// <summary>A solid ruin box: u0, u1, v0, v1 (UV), y0, y1, drawn as (0 box, 1 upright drum, 2 drum along u, 3 drum
    /// along v), and whether it shows outside (drawn with the exterior, by day) or only with the interior.</summary>
    public struct RuinBox
    {
        public float4 UV;
        public float2 Y;
        public int Draw, Outside;
    }

    /// <summary>Sand drifted onto a room's floor: ellipse centre u, v and radii, peak height.</summary>
    public struct RuinDrift
    {
        public float4 Ellipse;
        public float Height;
        public int Room;
    }

    /// <summary>A wall torch: the wall point (u, y, v) and the way into the room (nu, nv).</summary>
    public struct RuinSconce
    {
        public float3 At;
        public float2 N;
    }

    /// <summary>
    /// The modular ruin kit (W-56, issue #24): ruins are built from room set pieces (entrance, chamber, sanctum, hall,
    /// stair, corner, dead end), each a little data set (rooms, solid boxes, drifts, torches) in its own frame with its
    /// entry socket at the origin facing +a (b is a quarter turn left), plus exit sockets. A seeded layout builder joins
    /// pieces socket to socket, rotating them in quarter turns and mirroring the whole ruin, and tries optional side
    /// branches (halls, corners, dead ends) that it keeps only if they clear every other room. The result is plain
    /// arrays baked into LandmarkSetBlob, so SunkenTempleShape (collision, Burst) and SunkenTempleSystem (drawing) read
    /// the same rooms. Seed-derived from the landmark's key: solo, host and client all build the same ruin.
    /// </summary>
    public static class RuinKit
    {
        public enum Kind { Entrance, Chamber, Sanctum, Hall, Stair, Corner, DeadEnd }

        /// <summary>An exit socket in piece frame: where the next piece's entry goes and its turn (0 on, 1 left, 2 back, 3 right).</summary>
        public struct Socket
        {
            public float A, B, Y;
            public int Turn;
            public bool Side;
        }

        /// <summary>One set piece: everything in its frame (a along the entry direction, b to its left, y up from the entry floor).</summary>
        public class Piece
        {
            public Kind Kind;
            public readonly List<RuinRoom> Rooms = new List<RuinRoom>();      // U = a, V = b
            public readonly List<RuinBox> Boxes = new List<RuinBox>();
            public readonly List<RuinDrift> Drifts = new List<RuinDrift>();   // Room = index into this piece's rooms
            public readonly List<RuinSconce> Sconces = new List<RuinSconce>();
            public readonly List<Socket> Exits = new List<Socket>();

            public Piece(Kind kind) => Kind = kind;

            public Piece Room(float a0, float a1, float b0, float b1, float floor, float ceil, float slope = 0f, int kind = 0)
            {
                Rooms.Add(new RuinRoom { U0 = a0, U1 = a1, V0 = b0, V1 = b1, Floor = floor, Ceil = ceil, Slope = slope, Kind = kind });
                return this;
            }

            public Piece Box(float a0, float a1, float b0, float b1, float y0, float y1, int draw = 0, bool outside = false)
            {
                Boxes.Add(new RuinBox { UV = new float4(a0, a1, b0, b1), Y = new float2(y0, y1), Draw = draw, Outside = outside ? 1 : 0 });
                return this;
            }

            public Piece Drift(float a, float b, float ra, float rb, float h, int room)
            {
                Drifts.Add(new RuinDrift { Ellipse = new float4(a, b, ra, rb), Height = h, Room = room });
                return this;
            }

            public Piece Sconce(float a, float y, float b, float na, float nb)
            {
                Sconces.Add(new RuinSconce { At = new float3(a, y, b), N = new float2(na, nb) });
                return this;
            }

            public Piece Exit(float a, float b, float y, int turn = 0, bool side = false)
            {
                Exits.Add(new Socket { A = a, B = b, Y = y, Turn = turn, Side = side });
                return this;
            }
        }

        // ---- The piece set (sunken temple, desert). Other biomes add their own factories with the same sockets. ----

        public const float Open = 1e4f;

        /// <summary>Entrance: a ramp then five low steps cut down through the heightmap, flanking pillars, a lintel
        /// over the door, and the broken portico standing in the sand above.</summary>
        public static Piece Entrance(ref Random rng)
        {
            var p = new Piece(Kind.Entrance).Room(0f, 4.4f, -2f, 2f, 1.25f, Open, 0.3f, 1);
            for (int i = 0; i < 5; i++)
                p.Room(4.4f + 0.64f * i, 4.4f + 0.64f * (i + 1), -2f, 2f, 1f - 0.25f * i, Open, 0f, 1);
            p.Room(7.6f, 8.2f, -1.2f, 1.2f, 0f, 3f);
            // Pillars either side of the door, the lintel on them, the portico (four columns, two broken) above.
            p.Box(6.9f, 7.6f, 1.25f, 1.95f, 0f, 3f, 1, true).Box(6.9f, 7.6f, -1.95f, -1.25f, 0f, 3f, 1, true);
            p.Box(6.8f, 7.7f, -2.25f, 2.25f, 3f, 3.75f, 0, true);
            p.Box(5.5f, 6.5f, 2.3f, 3.3f, -1f, 8f, 1, true).Box(5.5f, 6.5f, -3.3f, -2.3f, -1f, 8f, 1, true);
            p.Box(5.5f, 6.5f, 5.1f, 6.1f, -1f, 5.5f, 1, true).Box(5.5f, 6.5f, -6.1f, -5.1f, -1f, 7f, 1, true);
            p.Box(5.4f, 6.6f, -6.2f, 1.2f, 8f, 8.9f, 0, true).Box(5.6f, 6.4f, -5.2f, 0.6f, 8.9f, 9.6f, 0, true);
            p.Box(5.6f, 6.4f, -3.8f, -0.6f, 9.6f, 10.2f, 0, true).Box(3f, 4.5f, 5f, 7.4f, 3.4f, 4.4f, 0, true);
            // A cracked step and a toppled block in the cut (small details, low enough to step over).
            p.Box(4.6f + rng.NextFloat(0f, 1.6f), 5.1f + rng.NextFloat(1.6f, 1.9f), 1.2f, 1.9f, 0.6f, 0.82f);
            p.Drift(2.5f, 1.0f, 3f, 1.0f, 0.45f, 0).Drift(1.5f, -1.3f, 1.6f, 0.9f, 0.35f, 0);
            return p.Exit(8.2f, 0f, 0f);
        }

        /// <summary>Chamber (the entry hall): rubble in one of a few arrangements, side sockets for branches.</summary>
        public static Piece Chamber(ref Random rng)
        {
            var p = new Piece(Kind.Chamber).Room(0f, 11.4f, -6f, 6f, 0f, 4f);
            var f = rng.NextBool() ? 1f : -1f;
            p.Box(3f, 8.5f, f * 2.65f, f * 3.75f, 0f, 1.1f, 2).Box(8.3f, 9.4f, -f * 5.4f, -f * 1.8f, 0f, 1.1f, 3);
            p.Box(1.45f, 2.55f, f * 4.05f, f * 5.15f, 0f, rng.NextFloat(1.2f, 1.9f), 1);
            if (rng.NextFloat() < 0.7f)
                p.Box(5.45f, 6.55f, -f * 5.15f, -f * 4.05f, 0f, 4f, 1);
            p.Drift(1f, 0f, 2.2f, 2.6f, 0.35f, 0).Sconce(0f, 2.2f, -4f * f, 1f, 0f);
            p.Exit(11.4f, 0f, 0f);
            return p.Exit(4.5f, 6f, 0f, 1, true).Exit(4.5f, -6f, 0f, 3, true);
        }

        /// <summary>Hall: a plain passage (doorway, corridor), optionally with a torch on its left wall.</summary>
        public static Piece Hall(float length, float width, float ceil, float torch = -1f)
        {
            var p = new Piece(Kind.Hall).Room(0f, length, -width * 0.5f, width * 0.5f, 0f, ceil);
            if (torch >= 0f)
                p.Sconce(torch, ceil - 0.6f, width * 0.5f, 0f, -1f);
            return p.Exit(length, 0f, 0f);
        }

        /// <summary>The pillared hall: two rows of four pillars (sometimes one has fallen), the ziggurat tiers and
        /// column stumps that stick out of the swell above it, and its exit to the first stair.</summary>
        public static Piece PillaredHall(ref Random rng)
        {
            var p = new Piece(Kind.Chamber).Room(0f, 16f, -8f, 8f, 0f, 4f);
            var broken = rng.NextInt(-2, 8);
            for (int k = 0; k < 8; k++)
            {
                float a = 2.45f + 3.5f * (k & 3), b = k < 4 ? 3.95f : -5.05f;
                if (k == broken)
                {
                    p.Box(a, a + 1.1f, b, b + 1.1f, 0f, 1.3f, 1).Box(a + 1.3f, a + 2.4f, b - 2.2f, b + 0.6f, 0f, 1.1f, 3);
                    continue;
                }
                p.Box(a, a + 1.1f, b, b + 1.1f, 0f, 4f, 1);
            }
            p.Box(20f, 30f, -5f, 5f, 3f, 6f, 0, true).Box(21.5f, 28.5f, -3.5f, 3.5f, 6f, 7.5f, 0, true);
            p.Box(23f, 27f, -2f, 2f, 7.5f, 9f, 0, true).Box(24f, 26f, -1f, 1f, 9f, 10.6f, 0, true);
            p.Box(5.5f, 6.5f, 12f, 13f, 1f, 5.5f, 1, true).Box(17.5f, 18.5f, 12f, 13f, 1f, 6.2f, 1, true);
            p.Box(5.5f, 6.5f, -13f, -12f, 1f, 6.2f, 1, true).Box(17.5f, 18.5f, -13f, -12f, 1f, 5.5f, 1, true);
            p.Sconce(8f, 2.2f, 8f, 0f, -1f).Sconce(8f, 2.2f, -8f, 0f, 1f);
            return p.Exit(16f, 5.5f, 0f);
        }

        /// <summary>Stair: n treads going down by drop each, head room above every tread.</summary>
        public static Piece Stair(int n, float tread, float drop, float width, float head)
        {
            var p = new Piece(Kind.Stair);
            for (int i = 0; i < n; i++)
                p.Room(i * tread, (i + 1) * tread, -width * 0.5f, width * 0.5f, -drop * (i + 1), -drop * (i + 1) + head);
            return p.Exit(n * tread, 0f, -drop * n);
        }

        /// <summary>The sand chamber: a landing at the stair's foot opening into a half-buried room, leaving back the way it came.</summary>
        public static Piece SandChamber(ref Random rng)
        {
            var p = new Piece(Kind.Chamber).Room(0f, 4f, -3.9f, 0.8f, 0f, 3.4f).Room(-8f, 4f, -13.5f, -3.9f, 0f, 3.6f);
            p.Drift(-2f, -9.3f, 5.5f, 4.6f, 1.4f, 1).Drift(-6.2f, -5.7f, 2.2f, 1.8f, 0.7f, 1);
            p.Drift(rng.NextFloat(1f, 3f), -5f, 1.4f, 1.1f, 0.3f, 1);
            p.Box(1.45f, 2.55f, -13f, -8.5f, 0f, 1f, 3).Sconce(4f, 2.2f, -9.5f, -1f, 0f);
            return p.Exit(-8f, -10.5f, 0f, 2);
        }

        /// <summary>The sanctum: altar, the statue on its plinth, four pillars (sometimes one broken) and three torches.</summary>
        public static Piece Sanctum(ref Random rng)
        {
            var p = new Piece(Kind.Sanctum).Room(0f, 12f, -8f, 4f, 0f, 5.5f);
            p.Box(7.6f, 9f, -3.2f, -0.8f, 0f, 1.1f).Box(10.2f, 11.8f, -2.8f, -1.2f, 0f, 0.5f);
            p.Box(10.4f, 11.6f, -2.6f, -1.4f, 0.5f, 3.1f, 1).Box(10.65f, 11.35f, -2.35f, -1.65f, 3.1f, 3.9f);
            var broken = rng.NextInt(-3, 4);
            for (int k = 0; k < 4; k++)
            {
                float a = (k & 1) == 0 ? 2.45f : 8.45f, b = k < 2 ? 1.45f : -6.55f;
                p.Box(a, a + 1.1f, b, b + 1.1f, 0f, k == broken ? 2.1f : 5.5f, 1);
            }
            p.Drift(1.2f, 2.8f, 1.6f, 1.2f, 0.25f, 0);
            return p.Sconce(12f, 2.7f, 2f, -1f, 0f).Sconce(12f, 2.7f, -6f, -1f, 0f).Sconce(0f, 2.7f, -5f, 1f, 0f);
        }

        /// <summary>Corner: a square turn to the left (turn 1) or right (turn 3).</summary>
        public static Piece Corner(float width, float ceil, int turn) =>
            new Piece(Kind.Corner).Room(0f, width, -width * 0.5f, width * 0.5f, 0f, ceil)
                .Exit(width * 0.5f, turn == 1 ? width * 0.5f : -width * 0.5f, 0f, turn);

        /// <summary>Dead end: a small closed niche with one of a few things in it (an urn, a toppled block, a sand heap).</summary>
        public static Piece DeadEnd(ref Random rng)
        {
            var p = new Piece(Kind.DeadEnd).Room(0f, 2.6f, -1.3f, 1.3f, 0f, 2.8f);
            switch (rng.NextInt(3))
            {
                case 0: p.Box(1.7f, 2.3f, 0.4f, 1.0f, 0f, 0.75f, 1).Box(1.9f, 2.4f, -1.0f, -0.5f, 0f, 0.28f); break;
                case 1: p.Box(1.2f, 2.5f, -1.2f, -0.2f, 0f, 0.6f).Box(0.4f, 0.8f, 0.6f, 1.0f, 0f, 0.2f); break;
                default: p.Drift(2.0f, 0f, 1.2f, 1.4f, 0.55f, 0); break;
            }
            return p.Sconce(2.6f, 2.0f, 0f, -1f, 0f);
        }

        // ---- The layout builder. ----

        /// <summary>A built ruin: every room, box, drift and torch in the landmark's frame.</summary>
        public class Layout
        {
            public readonly List<RuinRoom> Rooms = new List<RuinRoom>();
            public readonly List<RuinBox> Boxes = new List<RuinBox>();
            public readonly List<RuinDrift> Drifts = new List<RuinDrift>();
            public readonly List<RuinSconce> Sconces = new List<RuinSconce>();
            public int Pieces, Branches;
        }

        /// <summary>A socket placed in the landmark frame: point, floor height, direction index (0 +u, 1 +v, 2 -u, 3 -v).</summary>
        public struct Placed
        {
            public float2 At;
            public float Y;
            public int Dir;
            public bool Side;
        }

        static readonly float2[] Dirs = { new float2(1f, 0f), new float2(0f, 1f), new float2(-1f, 0f), new float2(0f, -1f) };

        /// <summary>Piece rooms placed at a socket (mirror m flips b), without adding them.</summary>
        static List<RuinRoom> RoomsAt(Piece piece, Placed at, float m)
        {
            float2 f = Dirs[at.Dir], s = new float2(-f.y, f.x) * m;
            var list = new List<RuinRoom>();
            foreach (var r in piece.Rooms)
            {
                float2 c0 = at.At + f * r.U0 + s * r.V0, c1 = at.At + f * r.U1 + s * r.V1;
                list.Add(new RuinRoom
                {
                    U0 = math.min(c0.x, c1.x), U1 = math.max(c0.x, c1.x), V0 = math.min(c0.y, c1.y), V1 = math.max(c0.y, c1.y),
                    Floor = at.Y + r.Floor, Ceil = r.Ceil >= Open ? Open : at.Y + r.Ceil, Slope = r.Slope, Kind = r.Kind
                });
            }
            return list;
        }

        /// <summary>Adds a piece at a socket and returns its exits, placed.</summary>
        static List<Placed> Place(Layout l, Piece piece, Placed at, float m)
        {
            float2 f = Dirs[at.Dir], s = new float2(-f.y, f.x) * m;
            var first = l.Rooms.Count;
            l.Rooms.AddRange(RoomsAt(piece, at, m));
            foreach (var b in piece.Boxes)
            {
                float2 c0 = at.At + f * b.UV.x + s * b.UV.z, c1 = at.At + f * b.UV.y + s * b.UV.w;
                var draw = b.Draw >= 2 && (at.Dir & 1) == 1 ? 5 - b.Draw : b.Draw;
                l.Boxes.Add(new RuinBox
                {
                    UV = new float4(math.min(c0.x, c1.x), math.max(c0.x, c1.x), math.min(c0.y, c1.y), math.max(c0.y, c1.y)),
                    Y = b.Y + at.Y, Draw = draw, Outside = b.Outside
                });
            }
            foreach (var d in piece.Drifts)
            {
                var c = at.At + f * d.Ellipse.x + s * d.Ellipse.y;
                var r = (at.Dir & 1) == 0 ? d.Ellipse.zw : d.Ellipse.wz;
                l.Drifts.Add(new RuinDrift { Ellipse = new float4(c, r), Height = d.Height, Room = first + d.Room });
            }
            foreach (var sc in piece.Sconces)
            {
                var c = at.At + f * sc.At.x + s * sc.At.z;
                var n = f * sc.N.x + s * sc.N.y;
                l.Sconces.Add(new RuinSconce { At = new float3(c.x, at.Y + sc.At.y, c.y), N = n });
            }
            var exits = new List<Placed>();
            foreach (var e in piece.Exits)
            {
                var dir = e.Turn == 0 ? f : e.Turn == 2 ? -f : e.Turn == 1 ? s : -s;
                var index = 0;
                for (int k = 0; k < 4; k++)
                    if (math.all(math.abs(Dirs[k] - dir) < 1e-3f))
                        index = k;
                exits.Add(new Placed { At = at.At + f * e.A + s * e.B, Y = at.Y + e.Y, Dir = index, Side = e.Side });
            }
            l.Pieces++;
            return exits;
        }

        /// <summary>Whether a branch's rooms keep clear (0.5 m) of every room already in the ruin from index 'from' on,
        /// stay within the area the swell covers and keep their ceilings under its top.</summary>
        static bool Clears(Layout l, List<RuinRoom> branch, int skipFrom, int skipTo)
        {
            const float gap = 0.5f;
            foreach (var r in branch)
            {
                if (r.U0 < -27f || r.U1 > 27f || r.V0 < -11f || r.V1 > 11f || r.Ceil > SunkenTempleShape.Top - 0.4f)
                    return false;
                for (int i = 0; i < l.Rooms.Count; i++)
                {
                    if (i >= skipFrom && i < skipTo)
                        continue;
                    var o = l.Rooms[i];
                    if (r.U0 < o.U1 + gap && r.U1 > o.U0 - gap && r.V0 < o.V1 + gap && r.V1 > o.V0 - gap &&
                        r.Floor < math.min(o.Ceil, 1e3f) + gap && math.min(r.Ceil, 1e3f) > o.Floor - gap)
                        return false;
                }
            }
            return true;
        }

        /// <summary>Tries a side branch (hall, maybe a corner and another hall, then a dead end) at a side socket.</summary>
        static void Branch(Layout l, ref Random rng, Placed at, float m, int parentFrom, int parentTo)
        {
            var chain = new List<Piece> { Hall(rng.NextFloat(1.4f, 2.4f), 1.4f, 2.6f) };
            if (rng.NextBool())
            {
                chain.Add(Corner(1.4f, 2.6f, rng.NextBool() ? 1 : 3));
                chain.Add(Hall(rng.NextFloat(1.2f, 2.2f), 1.4f, 2.6f));
            }
            chain.Add(DeadEnd(ref rng));
            // Dry run: place every piece's rooms and check them before adding any.
            var rooms = new List<RuinRoom>();
            var socket = at;
            foreach (var piece in chain)
            {
                rooms.AddRange(RoomsAt(piece, socket, m));
                var probe = new Layout();
                var exits = Place(probe, piece, socket, m);
                socket = exits.Count > 0 ? exits[0] : socket;
            }
            if (!Clears(l, rooms, parentFrom, parentTo))
                return;
            socket = at;
            foreach (var piece in chain)
            {
                var exits = Place(l, piece, socket, m);
                socket = exits.Count > 0 ? exits[0] : socket;
            }
            l.Branches++;
        }

        /// <summary>Small rubble (at most 0.3 m tall) in some corners of the bigger rooms, away from every doorway.</summary>
        static void Rubble(Layout l, ref Random rng)
        {
            var n = l.Rooms.Count;
            for (int i = 0; i < n; i++)
            {
                var r = l.Rooms[i];
                if (r.Kind != 0 || r.U1 - r.U0 < 3f || r.V1 - r.V0 < 3f || rng.NextFloat() < 0.4f)
                    continue;
                var count = rng.NextInt(1, 3);
                for (int k = 0; k < count; k++)
                {
                    int corner = rng.NextInt(4);
                    float su = rng.NextFloat(0.2f, 0.5f), sv = rng.NextFloat(0.2f, 0.5f);
                    float u = (corner & 1) == 0 ? r.U0 + rng.NextFloat(0.3f, 0.9f) : r.U1 - rng.NextFloat(0.3f, 0.9f) - su;
                    float v = (corner & 2) == 0 ? r.V0 + rng.NextFloat(0.3f, 0.9f) : r.V1 - rng.NextFloat(0.3f, 0.9f) - sv;
                    var clear = true;
                    for (int j = 0; j < l.Rooms.Count && clear; j++)
                    {
                        var o = l.Rooms[j];
                        clear = j == i || !(u < o.U1 + 1.2f && u + su > o.U0 - 1.2f && v < o.V1 + 1.2f && v + sv > o.V0 - 1.2f &&
                            math.abs(o.Floor - r.Floor) < 1.5f);
                    }
                    if (clear)
                        l.Boxes.Add(new RuinBox { UV = new float4(u, u + su, v, v + sv), Y = new float2(r.Floor, r.Floor + rng.NextFloat(0.1f, 0.3f)) });
                }
            }
        }

        /// <summary>
        /// The sunken temple's plan: entrance, entry hall (with up to two side branches), doorway, pillared hall,
        /// corridor, first stair, sand chamber, corridor back, second stair, sanctum. The trunk keeps W-32's rooms; the
        /// mirror, rubble, broken pillars and branches come from the seed.
        /// </summary>
        public static Layout Temple(uint seed)
        {
            var rng = new Random(seed | 1u);
            var l = new Layout();
            var m = rng.NextBool() ? 1f : -1f;
            var at = new Placed { At = new float2(-27f, 0f), Y = 8f, Dir = 0 };
            at = Place(l, Entrance(ref rng), at, m)[0];
            var hallFrom = l.Rooms.Count;
            var hall = Place(l, Chamber(ref rng), at, m);
            var hallTo = l.Rooms.Count;
            at = Place(l, Hall(0.6f, 3f, 3.2f), hall[0], m)[0];
            at = Place(l, PillaredHall(ref rng), at, m)[0];
            at = Place(l, Hall(3f, 1.6f, 2.8f), at, m)[0];
            at = Place(l, Stair(16, 0.5625f, 0.25f, 1.6f, 3.25f), at, m)[0];
            at = Place(l, SandChamber(ref rng), at, m)[0];
            at = Place(l, Hall(14f, 1.2f, 2.6f, 7f), at, m)[0];
            at = Place(l, Stair(14, 10f / 14f, 0.25f, 1.6f, 3.25f), at, m)[0];
            Place(l, Sanctum(ref rng), at, m);
            for (int k = 1; k < hall.Count; k++)
                if (hall[k].Side && rng.NextFloat() < 0.75f)
                    Branch(l, ref rng, hall[k], m, hallFrom, hallTo);
            Rubble(l, ref rng);
            return l;
        }

        /// <summary>A layout's fingerprint (every number in it), to check the same seed builds the same ruin.</summary>
        public static uint Fingerprint(Layout l)
        {
            var h = 2166136261u;
            void Add(float x) => h = (h ^ math.asuint(x)) * 16777619u;
            foreach (var r in l.Rooms) { Add(r.U0); Add(r.U1); Add(r.V0); Add(r.V1); Add(r.Floor); Add(r.Ceil); }
            foreach (var b in l.Boxes) { Add(b.UV.x); Add(b.UV.y); Add(b.UV.z); Add(b.UV.w); Add(b.Y.x); Add(b.Y.y); }
            foreach (var d in l.Drifts) { Add(d.Ellipse.x); Add(d.Ellipse.y); Add(d.Height); }
            foreach (var s in l.Sconces) { Add(s.At.x); Add(s.At.y); Add(s.At.z); }
            return h;
        }

        /// <summary>A full-avalanche mix of a landmark key (math.hash of a uint has none; see docs/agents/world.md).</summary>
        public static uint Scramble(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du; x ^= x >> 15; x *= 0x846CA68Bu; x ^= x >> 16;
            return x;
        }

        /// <summary>Bakes a layout into the landmark blob's ruin arrays (empty layout: empty arrays).</summary>
        public static void Fill(ref BlobBuilder builder, ref LandmarkSetBlob root, Layout l)
        {
            var rooms = builder.Allocate(ref root.RuinRooms, l?.Rooms.Count ?? 0);
            var boxes = builder.Allocate(ref root.RuinBoxes, l?.Boxes.Count ?? 0);
            var drifts = builder.Allocate(ref root.RuinDrifts, l?.Drifts.Count ?? 0);
            var sconces = builder.Allocate(ref root.RuinSconces, l?.Sconces.Count ?? 0);
            if (l == null)
                return;
            for (int i = 0; i < l.Rooms.Count; i++) rooms[i] = l.Rooms[i];
            for (int i = 0; i < l.Boxes.Count; i++) boxes[i] = l.Boxes[i];
            for (int i = 0; i < l.Drifts.Count; i++) drifts[i] = l.Drifts[i];
            for (int i = 0; i < l.Sconces.Count; i++) sconces[i] = l.Sconces[i];
        }
    }
}
