using System.Collections.Generic;
using Punchy.Dungeon;
using Punchy.Flora;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-115 row 2: the destination chamber kit. Each baked CaveRoom (CaveBaking.Rooms) is dressed from its own seed:
    /// ashlar tiles on every face of its box (floor flags, wall courses, ceiling slabs, a few missing or proud), corner
    /// pilasters, a plinth and a cornice that hide the meshed rock's rounded edges, a framed doorway (jamb tiles, soffit,
    /// threshold flags, a lintel and keystone facing the passage), then the room's own set: crystal clusters in a grotto,
    /// a coped pool with stepping stones, a boat and lanterns in a lake room, beams, perch ledges and guano in a roost,
    /// and a RuinKit chamber or sanctum (pillars, altar, torches, rubble, a mosaic border) in a buried ruin room. The
    /// same kit gives the room's lights, its solid parts (kit boxes, so you bump into what you see) and its socket spots
    /// (CaveSockets), all seed-derived: solo, host and client build the same room. Tiles sit a couple of centimetres
    /// proud of the cave field's flat faces, so the floor you stand on is the floor you see and the dark rock between
    /// tiles reads as grout. Drawn by CaveSystem.DrawRooms with copies of the cave material, lit like the walls.
    /// </summary>
    public static class CaveRoomKit
    {
        /// <summary>Material groups: each room's meshes are split by these, each drawn with its own cave material copy.</summary>
        public const int Stone = 0, Trim = 1, Wood = 2, Cyan = 3, Violet = 4, Pale = 5, Groups = 6;

        /// <summary>One drawn piece: a box (Shape 0), a hex crystal (1: Centre its base, Half.x its radius, Half.y its
        /// length, along its local up) or a drum (2: radius Half.x, half height Half.y, along its local up).</summary>
        public struct Part
        {
            public float3 Centre, Half;
            public quaternion Rotation;
            public float Tint;
            public byte Group, Shape;
        }

        /// <summary>A light the room gives: rgb strength and reach (as glowing plants), and whether it is a flame.</summary>
        public struct Glow
        {
            public float3 At;
            public float4 Light;
            public bool Flame;
        }

        /// <summary>A dungeon socket spot (world space, face horizontal), before CaveSockets adds the room's tags.</summary>
        public struct Spot
        {
            public float3 At;
            public float2 Face;
            public SocketKind Kind;
            public SocketSize Size;
            public SocketTags Tags;
        }

        /// <summary>A solid box (world space), added to the player's kit collision.</summary>
        public struct Solid
        {
            public float3 Centre, Half;
            public quaternion Rotation;
        }

        public class Kit
        {
            public readonly List<Part> Parts = new List<Part>();
            public readonly List<Glow> Glows = new List<Glow>();
            public readonly List<Spot> Spots = new List<Spot>();
            public readonly List<Solid> Solids = new List<Solid>();
        }

        /// <summary>The cave shader's rock tones per room type (grotto, lake, roost, ruin) for the stone and trim groups.</summary>
        static readonly Color[] StoneRock =
        {
            new Color(0.7f, 0.76f, 0.8f), new Color(0.5f, 0.55f, 0.6f), new Color(0.47f, 0.41f, 0.35f), new Color(0.86f, 0.74f, 0.55f)
        };
        static readonly Color[] StoneDark =
        {
            new Color(0.42f, 0.48f, 0.55f), new Color(0.26f, 0.3f, 0.35f), new Color(0.25f, 0.2f, 0.16f), new Color(0.58f, 0.46f, 0.31f)
        };
        static readonly Color[] TrimRock =
        {
            new Color(0.86f, 0.9f, 0.94f), new Color(0.66f, 0.7f, 0.72f), new Color(0.6f, 0.54f, 0.46f), new Color(0.95f, 0.86f, 0.68f)
        };
        static readonly Color[] TrimDark =
        {
            new Color(0.55f, 0.6f, 0.68f), new Color(0.38f, 0.42f, 0.45f), new Color(0.34f, 0.29f, 0.23f), new Color(0.66f, 0.55f, 0.4f)
        };

        /// <summary>The rock and dark tones a room type's group is drawn with.</summary>
        public static void Colours(int type, int group, out Color rock, out Color dark)
        {
            type = math.clamp(type, 0, 3);
            switch (group)
            {
                case Stone: rock = StoneRock[type]; dark = StoneDark[type]; break;
                case Trim: rock = TrimRock[type]; dark = TrimDark[type]; break;
                case Wood: rock = BatFolkMeshes.DarkRock; dark = BatFolkMeshes.DarkDark; break;
                case Cyan: rock = new Color(0.62f, 0.95f, 1f); dark = new Color(0.18f, 0.5f, 0.7f); break;
                case Violet: rock = new Color(0.86f, 0.62f, 1f); dark = new Color(0.38f, 0.22f, 0.6f); break;
                default: rock = BatFolkMeshes.PaleRock; dark = BatFolkMeshes.PaleDark; break;
            }
        }

        // ------------------------------------------------------------------------------------------- the kit

        static readonly Dictionary<(uint, int, int), Kit> Cache = new Dictionary<(uint, int, int), Kit>();

        /// <summary>The room's kit, built once (from its seed) and kept until <see cref="ClearCache"/>.</summary>
        public static Kit Get(in CaveRoom room)
        {
            var key = (room.Seed, room.Cave, room.Node);
            if (!Cache.TryGetValue(key, out var kit))
            {
                kit = Build(room);
                Cache[key] = kit;
            }
            return kit;
        }

        /// <summary>Forgets every built kit (the world was released or re-baked).</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>Lays out one room in its own frame (x across, y up, z in from the doorway), then moves it to the world.</summary>
        sealed class Maker
        {
            public CaveRoom R;
            public Kit K;
            public Random Rng;
            public quaternion Yaw;
            /// <summary>Floor and wall spots in the room's frame (moved to the world at the end).</summary>
            public readonly List<Spot> Spots = new List<Spot>();
            public float LakeR;

            public void Add(float3 local, float3 half, quaternion rot, int group, float tint, byte shape = 0, bool solid = false)
            {
                var c = R.World(local);
                var q = math.mul(Yaw, rot);
                K.Parts.Add(new Part { Centre = c, Half = half, Rotation = q, Tint = tint, Group = (byte)group, Shape = shape });
                if (solid)
                {
                    // A crystal's box is round its middle; boxes and drums are their own size.
                    var box = shape == 1 ? new Solid { Centre = c + math.mul(q, new float3(0f, half.y * 0.5f, 0f)), Half = new float3(half.x * 0.7f, half.y * 0.4f, half.x * 0.7f), Rotation = q }
                                         : new Solid { Centre = c, Half = half, Rotation = q };
                    K.Solids.Add(box);
                }
            }

            /// <summary>A box between two local corners, unturned.</summary>
            public void Span(float3 lo, float3 hi, int group, float tint, bool solid = false) =>
                Add((lo + hi) * 0.5f, math.abs(hi - lo) * 0.5f, quaternion.identity, group, tint, 0, solid);

            public float Jit(float lo = 0.84f, float hi = 1.04f) => Rng.NextFloat(lo, hi);

            /// <summary>Whether a floor point is free for a prop: off the walk in from the door, away from the floor
            /// spots (by d), out of the lake's coping and inside the walls (by wall).</summary>
            public bool Clear(float2 xz, float d, float wall = 0.5f)
            {
                if (math.abs(xz.x) > R.HX - wall || xz.y < wall || xz.y > R.HZ * 2f - wall)
                    return false;
                if (math.abs(xz.x) < R.DoorWidth * 0.5f + 0.5f && xz.y < 2.2f)
                    return false;
                if (LakeR > 0f && math.distance(xz, new float2(0f, R.HZ)) < LakeR + 0.85f)
                    return false;
                foreach (var s in Spots)
                    if (s.At.y < 0.5f && math.distance(s.At.xz, xz) < d)
                        return false;
                return true;
            }

            /// <summary>A free floor point (or null after a few tries), nearer the walls when hug is set.</summary>
            public float2? FreeFloor(float d, bool hug)
            {
                for (int k = 0; k < 24; k++)
                {
                    float2 p;
                    if (hug)
                    {
                        var side = Rng.NextInt(3);
                        var inset = Rng.NextFloat(0.55f, 1.1f);
                        p = side == 0 ? new float2(-R.HX + inset, Rng.NextFloat(1f, R.HZ * 2f - 0.6f))
                          : side == 1 ? new float2(R.HX - inset, Rng.NextFloat(1f, R.HZ * 2f - 0.6f))
                          : new float2(Rng.NextFloat(-R.HX + 0.6f, R.HX - 0.6f), R.HZ * 2f - inset);
                    }
                    else
                        p = new float2(Rng.NextFloat(-R.HX + 0.6f, R.HX - 0.6f), Rng.NextFloat(1.2f, R.HZ * 2f - 0.6f));
                    if (Clear(p, d))
                        return p;
                }
                return null;
            }

            /// <summary>
            /// Lays ashlar on one face: courses of height row along v, blocks of length len (× row) along u, staggered,
            /// grouted (the rock shows between), each tinted a little differently; a few blocks are missing or stand
            /// proud. Blocks inside hole (u0, u1, v0, v1) are cut back to its edges. A lake circle (cu, cv, r) splits the
            /// blocks near it into small setts and drops those under its coping.
            /// </summary>
            public void Face(float3 o, float3 u, float3 v, float3 n, float w, float h, float row, float2 len, int group,
                             float proud, float depth, float4 hole, float3 lake = default, float odd = 1f)
            {
                const float grout = 0.035f;
                var rot = new quaternion(new float3x3(u, v, math.cross(u, v)));
                Bed(o, u, v, n, rot, w, h, group, hole, lake);
                for (float b0 = 0f; b0 < h - 0.05f; b0 += row)
                {
                    var b1 = math.min(h, b0 + row * Rng.NextFloat(0.94f, 1.06f));
                    if (h - b1 < row * 0.35f)
                        b1 = h;
                    var a = -Rng.NextFloat(0f, row * len.x);
                    while (a < w)
                    {
                        var a1 = a + row * Rng.NextFloat(len.x, len.y);
                        float lo = math.max(a, 0f), hi = math.min(a1, w);
                        a = a1;
                        var roll = Rng.NextFloat();
                        var tint = Jit();
                        if (hi - lo < 0.12f)
                            continue;
                        if (b1 > hole.z && b0 < hole.w && hi > hole.x && lo < hole.y)
                        {
                            // Keep the part either side of the hole.
                            if (lo < hole.x && hole.x - lo > 0.12f)
                                Block(o, u, v, n, rot, lo, hole.x, b0, b1, proud, depth, group, tint, grout);
                            if (hi > hole.y && hi - hole.y > 0.12f)
                                Block(o, u, v, n, rot, hole.y, hi, b0, b1, proud, depth, group, tint, grout);
                            continue;
                        }
                        if (roll < 0.018f * odd)
                            continue;   // a missing block: the rock shows through
                        var out_ = roll > 1f - 0.03f * odd ? proud + 0.05f : proud;
                        if (lake.z > 0f)
                        {
                            var mid = new float2((lo + hi) * 0.5f, (b0 + b1) * 0.5f);
                            if (math.distance(mid, lake.xy) < lake.z + 1.6f)
                            {
                                // Small setts round the pool; the coping covers where they stop.
                                int nu = (int)math.ceil((hi - lo) / 0.32f), nv = (int)math.ceil((b1 - b0) / 0.32f);
                                for (int i = 0; i < nu; i++)
                                    for (int j = 0; j < nv; j++)
                                    {
                                        float s0 = lo + (hi - lo) * i / nu, s1 = lo + (hi - lo) * (i + 1) / nu;
                                        float t0 = b0 + (b1 - b0) * j / nv, t1 = b0 + (b1 - b0) * (j + 1) / nv;
                                        if (math.distance(new float2((s0 + s1) * 0.5f, (t0 + t1) * 0.5f), lake.xy) < lake.z + 0.55f)
                                            continue;
                                        Block(o, u, v, n, rot, s0, s1, t0, t1, proud, depth, group, tint * Jit(0.94f, 1.04f), grout * 0.7f);
                                    }
                                continue;
                            }
                        }
                        Block(o, u, v, n, rot, lo, hi, b0, b1, out_, depth, group, tint, grout);
                    }
                }
            }

            /// <summary>
            /// A dark mortar bed just proud of the rock under a face's blocks, in strips along v that leave out the hole
            /// and the pool, so the grout and the missing blocks read as dark recesses whatever colour the cave is.
            /// </summary>
            void Bed(float3 o, float3 u, float3 v, float3 n, quaternion rot, float w, float h, int group, float4 hole, float3 lake)
            {
                const float strip = 0.6f, tint = 0.42f;
                for (float b0 = 0f; b0 < h - 0.01f; b0 += strip)
                {
                    var b1 = math.min(h, b0 + strip);
                    // Cut-outs along u in this strip: the hole, and the pool's chord (widest across the strip).
                    float c0 = 1e4f, c1 = -1e4f, d0 = 1e4f, d1 = -1e4f;
                    if (b1 > hole.z && b0 < hole.w) { c0 = hole.x; c1 = hole.y; }
                    if (lake.z > 0f)
                    {
                        var dv = math.max(0f, math.max(b0 - lake.y, lake.y - b1));
                        var r = lake.z + 0.1f;
                        if (dv < r) { var half = math.sqrt(r * r - dv * dv); d0 = lake.x - half; d1 = lake.x + half; }
                    }
                    // The two cut-outs in order along u (an absent one is (1e4, -1e4) and is skipped).
                    var first = c0 <= d0 ? new float2(c0, c1) : new float2(d0, d1);
                    var second = c0 <= d0 ? new float2(d0, d1) : new float2(c0, c1);
                    var a = 0f;
                    foreach (var cut in new[] { first, second })
                    {
                        if (cut.x > 1e3f || cut.y <= a)
                            continue;
                        if (cut.x - a > 0.02f)
                            Strip(a, math.min(cut.x, w));
                        a = math.max(a, cut.y);
                    }
                    if (w - a > 0.02f)
                        Strip(a, w);

                    void Strip(float s0, float s1) =>
                        Add(o + u * ((s0 + s1) * 0.5f) + v * ((b0 + b1) * 0.5f) + n * 0.001f,
                            new float3((s1 - s0) * 0.5f, (b1 - b0) * 0.5f, 0.004f), rot, group, tint);
                }
            }

            void Block(float3 o, float3 u, float3 v, float3 n, quaternion rot, float a0, float a1, float b0, float b1,
                       float proud, float depth, int group, float tint, float grout)
            {
                var c = o + u * ((a0 + a1) * 0.5f) + v * ((b0 + b1) * 0.5f) + n * (proud - depth * 0.5f);
                var half = new float3(math.max(0.02f, (a1 - a0 - grout) * 0.5f), math.max(0.02f, (b1 - b0 - grout) * 0.5f), depth * 0.5f);
                Add(c, half, rot, group, tint);
            }
        }

        static readonly float4 NoHole = new float4(-1e4f, -1e4f, -1e4f, -1e4f);

        public static Kit Build(in CaveRoom room)
        {
            var m = new Maker
            {
                R = room,
                K = new Kit(),
                Rng = new Random(room.Seed | 1u),
                Yaw = quaternion.LookRotation(room.Forward, new float3(0f, 1f, 0f)),
                LakeR = room.Type == (int)CaveRoomType.Lake ? CaveSampling.LakeRadius(room) : 0f
            };
            Spots(m);
            Shell(m);
            switch ((CaveRoomType)room.Type)
            {
                case CaveRoomType.Lake: Lake(m); break;
                case CaveRoomType.Roost: Roost(m); break;
                case CaveRoomType.Ruin: Ruin(m); break;
                default: Grotto(m); break;
            }
            Finish(m);
            return m.K;
        }

        /// <summary>The room's socket spots, laid first so the props keep clear of them.</summary>
        static void Spots(Maker m)
        {
            var r = m.R;
            var toDoor = new float2(0f, -1f);
            var size = r.HX * r.HZ >= 20f ? SocketSize.Large : SocketSize.Medium;
            var z = m.LakeR > 0f ? (r.HZ + m.LakeR + 0.85f < r.HZ * 2f - 0.55f ? r.HZ + m.LakeR + 0.85f : r.HZ - m.LakeR - 0.85f) : r.HZ;
            m.Spots.Add(new Spot { At = new float3(0f, 0.05f, z), Face = toDoor, Kind = SocketKind.Creature, Size = size });
            // The chest by the back wall: in the middle or to one side (whichever the room's set leaves free, see Finish).
            var side = m.Rng.NextBool() ? 1f : -1f;
            foreach (var x in new[] { 0f, side * r.HX * 0.5f, -side * r.HX * 0.5f })
                m.Spots.Add(new Spot { At = new float3(x, 0.05f, r.HZ * 2f - 0.85f), Face = toDoor, Kind = SocketKind.Chest, Size = SocketSize.Medium, Tags = SocketTags.NearChest });
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? -1f : 1f, front = k < 2 ? 1f : 0f;
                var at = new float3(sx * (r.HX - 0.85f), 0.05f, front > 0f ? 1.3f : r.HZ * 2f - 0.85f);
                m.Spots.Add(new Spot { At = at, Face = math.normalize(new float2(-sx, front > 0f ? 1f : -1f)), Kind = SocketKind.Clutter, Size = SocketSize.Small });
            }
            for (int k = 0; k < 2; k++)
            {
                var sx = k == 0 ? -1f : 1f;
                m.Spots.Add(new Spot { At = new float3(sx * (r.HX - 0.25f), 1.6f, r.HZ), Face = new float2(-sx, 0f), Kind = SocketKind.WallDecor, Size = r.HZ >= 4f ? SocketSize.Medium : SocketSize.Small });
            }
        }

        /// <summary>The tiled box: floor, ceiling, four walls (the front one round the doorway), trims and the doorway.</summary>
        static void Shell(Maker m)
        {
            var r = m.R;
            float hx = r.HX, d = r.HZ * 2f, h = r.Height, dw = r.DoorWidth * 0.5f, dh = r.DoorHeight, t = CaveRoom.Wall;
            float3 X = new float3(1f, 0f, 0f), Y = new float3(0f, 1f, 0f), Z = new float3(0f, 0f, 1f);
            var row = m.Rng.NextFloat(0.45f, 0.6f);
            var flag = m.Rng.NextFloat(0.8f, 1.1f);
            var len = r.Type == (int)CaveRoomType.Grotto ? new float2(1.2f, 2.6f) : new float2(1.4f, 2.4f);
            var odd = r.Type == (int)CaveRoomType.Ruin ? 2.2f : 1f;
            var lake = m.LakeR > 0f ? new float3(hx, r.HZ, m.LakeR) : default;

            m.Face(new float3(-hx, 0f, 0f), X, Z, Y, hx * 2f, d, flag, new float2(0.8f, 1.35f), Stone, 0.012f, 0.07f, NoHole, lake, odd);
            m.Face(new float3(-hx, h, 0f), X, Z, -Y, hx * 2f, d, flag * 1.1f, new float2(0.9f, 1.5f), Stone, 0.02f, 0.08f, NoHole, default, odd * 0.5f);
            m.Face(new float3(-hx, 0f, 0f), Z, Y, X, d, h, row, len * 0.5f, Stone, 0.025f, 0.09f, NoHole, default, odd);
            m.Face(new float3(hx, 0f, 0f), Z, Y, -X, d, h, row, len * 0.5f, Stone, 0.025f, 0.09f, NoHole, default, odd);
            m.Face(new float3(-hx, 0f, d), X, Y, -Z, hx * 2f, h, row, len * 0.5f, Stone, 0.025f, 0.09f, NoHole, default, odd);
            m.Face(new float3(-hx, 0f, 0f), X, Y, Z, hx * 2f, h, row, len * 0.5f, Stone, 0.025f, 0.09f,
                   new float4(hx - dw - 0.3f, hx + dw + 0.3f, -1f, dh + 0.42f));

            // The doorway's own faces: jambs, soffit and threshold, flush with the stub the field cuts.
            m.Face(new float3(-dw, 0f, -t), Z, Y, X, t, dh, row * 0.9f, new float2(0.8f, 1.4f), Stone, 0.02f, 0.08f, NoHole);
            m.Face(new float3(dw, 0f, -t), Z, Y, -X, t, dh, row * 0.9f, new float2(0.8f, 1.4f), Stone, 0.02f, 0.08f, NoHole);
            m.Face(new float3(-dw, dh, -t), X, Z, -Y, dw * 2f, t, t * 0.5f, new float2(1.6f, 2.2f), Trim, 0.02f, 0.08f, NoHole);
            m.Face(new float3(-dw, 0f, -t - 0.3f), X, Z, Y, dw * 2f, t + 0.3f, 0.75f, new float2(0.7f, 1.2f), Trim, 0.012f, 0.07f, NoHole);

            // Trims over the rock's rounded edges: corner pilasters, a plinth and a cornice (solid where you'd bump them).
            const float p = 0.24f, plinth = 0.3f, plinthOut = 0.2f, cornice = 0.34f, corniceOut = 0.26f;
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? -1f : 1f, z = k < 2 ? p - 0.04f : d - p + 0.04f;
                m.Add(new float3(sx * (hx - p + 0.04f), h * 0.5f, z), new float3(p, h * 0.5f, p), quaternion.identity, Trim, m.Jit(0.92f, 1.02f), 0, true);
            }
            void Run(float3 a, float3 b, float y0, float y1, float outward, float3 n, bool solid)
            {
                // From just behind the wall (into the rock) out into the room by outward.
                var lo = math.min(math.min(a, b) - n * 0.04f, math.min(a, b) + n * outward);
                var hi = math.max(math.max(a, b) - n * 0.04f, math.max(a, b) + n * outward);
                lo.y = y0; hi.y = y1;
                m.Span(lo, hi, Trim, m.Jit(0.88f, 1f), solid);
            }
            var q = p * 2f - 0.04f;
            Run(new float3(-hx, 0f, q), new float3(-hx, 0f, d - q), 0f, plinth, plinthOut, X, true);
            Run(new float3(hx, 0f, q), new float3(hx, 0f, d - q), 0f, plinth, plinthOut, -X, true);
            Run(new float3(-hx + q, 0f, d), new float3(hx - q, 0f, d), 0f, plinth, plinthOut, -Z, true);
            Run(new float3(-hx + q, 0f, 0f), new float3(-dw - 0.32f, 0f, 0f), 0f, plinth, plinthOut, Z, true);
            Run(new float3(dw + 0.32f, 0f, 0f), new float3(hx - q, 0f, 0f), 0f, plinth, plinthOut, Z, true);
            Run(new float3(-hx, 0f, q), new float3(-hx, 0f, d - q), h - cornice, h + 0.02f, corniceOut, X, false);
            Run(new float3(hx, 0f, q), new float3(hx, 0f, d - q), h - cornice, h + 0.02f, corniceOut, -X, false);
            Run(new float3(-hx + q, 0f, d), new float3(hx - q, 0f, d), h - cornice, h + 0.02f, corniceOut, -Z, false);
            Run(new float3(-hx + q, 0f, 0f), new float3(hx - q, 0f, 0f), h - cornice, h + 0.02f, corniceOut, Z, false);

            // The doorway's frame, inside (posts and lintel round the opening) and out (on the passage's cut face).
            m.Span(new float3(-dw - 0.32f, 0f, -0.12f), new float3(-dw, dh, 0.08f), Trim, m.Jit(0.9f, 1f));
            m.Span(new float3(dw, 0f, -0.12f), new float3(dw + 0.32f, dh, 0.08f), Trim, m.Jit(0.9f, 1f));
            m.Span(new float3(-dw - 0.32f, dh, -0.12f), new float3(dw + 0.32f, dh + 0.42f, 0.08f), Trim, m.Jit(0.9f, 1f));
            m.Span(new float3(-dw - 0.45f, 0f, -t - 0.13f), new float3(-dw, dh, -t + 0.05f), Trim, m.Jit(0.86f, 1f), true);
            m.Span(new float3(dw, 0f, -t - 0.13f), new float3(dw + 0.45f, dh, -t + 0.05f), Trim, m.Jit(0.86f, 1f), true);
            m.Span(new float3(-dw - 0.5f, dh, -t - 0.15f), new float3(dw + 0.5f, dh + 0.5f, -t + 0.05f), Trim, m.Jit(0.86f, 1f));
            var key = m.Rng.NextFloat(0.18f, 0.24f);
            m.Add(new float3(0f, dh + 0.27f, -t - 0.1f), new float3(key, 0.36f, 0.12f), quaternion.identity, Trim, 1.04f);
        }

        // ------------------------------------------------------------------------------------------- the four sets

        /// <summary>A crystal: base (local), its axis, length and radius.</summary>
        static void Crystal(Maker m, float3 at, float3 axis, float length, float radius, int group, bool solid)
        {
            var rot = math.mul((quaternion)Quaternion.FromToRotation(Vector3.up, (Vector3)math.normalize(axis)), quaternion.RotateY(m.Rng.NextFloat(math.PI)));
            m.Add(at, new float3(radius, length, radius), rot, group, m.Jit(0.85f, 1.1f), 1, solid);
        }

        static float3 Tilt(ref Random rng, float3 axis, float most)
        {
            var side = math.normalizesafe(math.cross(axis, rng.NextFloat3Direction()), new float3(1f, 0f, 0f));
            return math.normalize(axis * math.cos(most * rng.NextFloat()) + side * math.sin(most * rng.NextFloat()));
        }

        static void Grotto(Maker m)
        {
            var r = m.R;
            var group = m.Rng.NextBool() ? Cyan : Violet;
            var light = group == Cyan ? new float4(0.3f, 0.9f, 1.4f, 7f) : new float4(0.9f, 0.4f, 1.5f, 7f);
            var clusters = new List<float3>();
            int floor = m.Rng.NextInt(5, 10);
            float3 bigAt = default; var big = 0f;
            for (int c = 0; c < floor; c++)
            {
                var at = m.FreeFloor(1.2f, m.Rng.NextFloat() < 0.65f);
                if (at == null) continue;
                var centre = new float3(at.Value.x, -0.08f, at.Value.y);
                clusters.Add(centre);
                int n = m.Rng.NextInt(3, 8);
                for (int k = 0; k < n; k++)
                {
                    var first = k == 0;
                    var len = first ? m.Rng.NextFloat(0.9f, 1.6f) : m.Rng.NextFloat(0.3f, 0.9f);
                    var rad = first ? m.Rng.NextFloat(0.12f, 0.18f) : m.Rng.NextFloat(0.05f, 0.12f);
                    var off = first ? float2.zero : m.Rng.NextFloat2Direction() * m.Rng.NextFloat(0.12f, 0.38f);
                    var axis = first ? Tilt(ref m.Rng, new float3(0f, 1f, 0f), 0.25f)
                                     : math.normalize(new float3(off.x, 0f, off.y) * 1.4f + new float3(0f, 1f, 0f) * m.Rng.NextFloat(0.8f, 1.6f));
                    Crystal(m, centre + new float3(off.x, 0f, off.y), axis, len, rad, group, len > 1.05f);
                    if (first && len > big) { big = len; bigAt = centre; }
                }
            }
            // On the walls (pointing out and up) and hanging from the ceiling.
            int walls = m.Rng.NextInt(3, 6);
            for (int c = 0; c < walls; c++)
            {
                var side = m.Rng.NextInt(3);
                float3 at, n;
                var y = m.Rng.NextFloat(1.2f, r.Height - 1.4f);
                if (side == 2) { at = new float3(m.Rng.NextFloat(-r.HX + 0.8f, r.HX - 0.8f), y, r.HZ * 2f + 0.06f); n = new float3(0f, 0f, -1f); }
                else { var sx = side == 0 ? -1f : 1f; at = new float3(sx * (r.HX + 0.06f), y, m.Rng.NextFloat(1.2f, r.HZ * 2f - 0.8f)); n = new float3(-sx, 0f, 0f); }
                for (int k = 0, count = m.Rng.NextInt(3, 6); k < count; k++)
                {
                    var axis = math.normalize(Tilt(ref m.Rng, n, 0.6f) + new float3(0f, m.Rng.NextFloat(0.2f, 0.7f), 0f));
                    Crystal(m, at + m.Rng.NextFloat3(-0.25f, 0.25f) * new float3(1f, 1f, 1f) * (1f - math.abs(n)), axis, m.Rng.NextFloat(0.25f, 0.8f), m.Rng.NextFloat(0.04f, 0.1f), group, false);
                }
            }
            for (int c = 0, ceil = m.Rng.NextInt(3, 6); c < ceil; c++)
            {
                var at = new float3(m.Rng.NextFloat(-r.HX + 0.9f, r.HX - 0.9f), r.Height + 0.06f, m.Rng.NextFloat(1.5f, r.HZ * 2f - 0.9f));
                for (int k = 0, count = m.Rng.NextInt(2, 6); k < count; k++)
                    Crystal(m, at + new float3(m.Rng.NextFloat(-0.3f, 0.3f), 0f, m.Rng.NextFloat(-0.3f, 0.3f)), Tilt(ref m.Rng, new float3(0f, -1f, 0f), 0.5f),
                            m.Rng.NextFloat(0.3f, 1.1f), m.Rng.NextFloat(0.04f, 0.11f), group, false);
            }
            // Shards on the floor, and a fallen crystal snapped in two.
            for (int k = 0, shards = m.Rng.NextInt(6, 13); k < shards; k++)
                if (m.FreeFloor(0.6f, false) is float2 s)
                    Crystal(m, new float3(s.x, 0.03f, s.y), Tilt(ref m.Rng, new float3(1f, 0f, 0f), 3f) * new float3(1f, 0.15f, 1f), m.Rng.NextFloat(0.1f, 0.25f), m.Rng.NextFloat(0.02f, 0.04f), group, false);
            if (m.FreeFloor(1.4f, false) is float2 f)
            {
                var dir = m.Rng.NextFloat2Direction();
                var axis = math.normalize(new float3(dir.x, 0.12f, dir.y));
                Crystal(m, new float3(f.x, 0.12f, f.y), axis, 0.75f, 0.15f, group, true);
                Crystal(m, new float3(f.x, 0.1f, f.y) - new float3(dir.x, 0f, dir.y) * 0.15f, -math.normalize(new float3(dir.x, -0.1f, dir.y) + new float3(0.25f, 0f, 0.1f)), 0.6f, 0.14f, group, true);
            }
            // Their light, from a few of the clusters.
            for (int k = 0, lights = math.min(clusters.Count, m.Rng.NextInt(2, 4)); k < lights; k++)
                m.K.Glows.Add(new Glow { At = m.R.World(clusters[k] + new float3(0f, 0.8f, 0f)), Light = light });
            if (big > 0f)
            {
                var toward = math.normalizesafe(new float2(0f, r.HZ) - bigAt.xz, new float2(0f, -1f));
                var at = bigAt.xz + toward * 0.95f;
                m.Spots.Add(new Spot { At = new float3(at.x, 0.05f, at.y), Face = -toward, Kind = SocketKind.Interactable, Size = SocketSize.Small });
            }
        }

        static void Lake(Maker m)
        {
            var r = m.R;
            var R = m.LakeR;
            var c = new float3(0f, 0f, r.HZ);
            // The coping round the pool, a kerb of curved stones, one cracked and sunk.
            int n = (int)math.round(2f * math.PI * (R + 0.33f) / 0.7f);
            var a0 = m.Rng.NextFloat(math.PI * 2f);
            var cracked = m.Rng.NextInt(n);
            for (int k = 0; k < n; k++)
            {
                var a = a0 + k * math.PI * 2f / n;
                var dir = new float3(math.cos(a), 0f, math.sin(a));
                var tan = math.PI * (R + 0.33f) / n - 0.02f;
                var drop = k == cracked ? -0.06f : 0f;
                m.Add(c + dir * (R + 0.33f) + new float3(0f, -0.01f + drop, 0f), new float3(0.37f, 0.07f, tan), quaternion.RotateY(-a), Trim, m.Jit(0.86f, 1.02f));
            }
            float Angle(float2 xz) => math.atan2(xz.y - r.HZ, xz.x);
            // Stepping stones across (most lakes), drums standing up from the pool's bed.
            if (m.Rng.NextFloat() < 0.7f)
            {
                var wave = m.Rng.NextFloat(math.PI * 2f);
                for (float z = r.HZ - R + 0.5f; z <= r.HZ + R - 0.5f; z += 0.82f)
                {
                    var rad = m.Rng.NextFloat(0.3f, 0.38f);
                    var x = math.sin(wave + z * 1.3f) * 0.35f;
                    m.Add(new float3(x, -0.78f, z), new float3(rad, 0.82f, rad), quaternion.RotateY(m.Rng.NextFloat(3f)), Stone, m.Jit(0.8f, 0.98f), 2, false);
                    m.K.Solids.Add(new Solid { Centre = r.World(new float3(x, -0.78f, z)), Half = new float3(rad * 0.75f, 0.82f, rad * 0.75f), Rotation = m.Yaw });
                }
            }
            // A boat moored on one side, its post on the coping and its line to the bow.
            var side = m.Rng.NextBool() ? 1f : -1f;
            var boat = new float3(side * R * 0.55f, -0.12f, r.HZ + m.Rng.NextFloat(-0.6f, 0.6f));
            var yaw = quaternion.RotateY(m.Rng.NextFloat(-0.4f, 0.4f));
            void Hull(float3 at, float3 half, float tint) => m.Add(boat + math.mul(yaw, at), half, yaw, Wood, tint);
            Hull(new float3(0f, -0.12f, 0f), new float3(0.42f, 0.04f, 1.0f), 0.8f);
            Hull(new float3(0.42f, 0.02f, 0f), new float3(0.04f, 0.14f, 1.0f), 0.95f);
            Hull(new float3(-0.42f, 0.02f, 0f), new float3(0.04f, 0.14f, 1.0f), 0.95f);
            Hull(new float3(0f, 0.02f, 1.0f), new float3(0.42f, 0.14f, 0.04f), 0.9f);
            Hull(new float3(0f, 0.02f, -1.0f), new float3(0.42f, 0.14f, 0.04f), 0.9f);
            Hull(new float3(0f, 0.08f, 0.15f), new float3(0.4f, 0.025f, 0.13f), 1.05f);
            Hull(new float3(0.1f, -0.05f, -0.5f), new float3(0.05f, 0.02f, 0.6f), 1.1f);   // an oar left in it
            var postA = Angle(boat.xz + new float2(side * 0.6f, 0f));
            var post = c + new float3(math.cos(postA), 0f, math.sin(postA)) * (R + 0.35f);
            m.Add(post + new float3(0f, 0.5f, 0f), new float3(0.08f, 0.45f, 0.08f), quaternion.identity, Wood, 0.9f, 2, true);
            var bow = boat + math.mul(yaw, new float3(0f, 0.1f, 0.9f));
            var line = (post + new float3(0f, 0.8f, 0f)) - bow;
            m.Add(bow + line * 0.5f, new float3(0.012f, 0.012f, math.length(line) * 0.5f), quaternion.LookRotationSafe(line, new float3(0f, 1f, 0f)), Pale, 0.8f);
            // Two lantern posts on the coping, away from the boat, each with a flame.
            for (int k = 0; k < 2; k++)
            {
                var a = postA + math.PI * (0.55f + k * 0.8f) + m.Rng.NextFloat(-0.2f, 0.2f);
                var at = c + new float3(math.cos(a), 0f, math.sin(a)) * (R + 0.4f);
                m.Add(at + new float3(0f, 0.78f, 0f), new float3(0.07f, 0.72f, 0.07f), quaternion.identity, Wood, 0.85f, 2, true);
                m.Add(at + new float3(0f, 1.56f, 0f), new float3(0.13f, 0.03f, 0.13f), quaternion.identity, Wood, 1f);
                m.Add(at + new float3(0f, 1.86f, 0f), new float3(0.15f, 0.03f, 0.15f), quaternion.RotateY(0.785f), Wood, 1f);
                for (int s = 0; s < 4; s++)
                {
                    var corner = new float3((s & 1) == 0 ? -0.11f : 0.11f, 1.71f, (s & 2) == 0 ? -0.11f : 0.11f);
                    m.Add(at + corner, new float3(0.015f, 0.14f, 0.015f), quaternion.identity, Wood, 0.9f);
                }
                m.K.Glows.Add(new Glow { At = r.World(at + new float3(0f, 1.62f, 0f)), Light = (Vector4)Punchy.Ambience.FireSystem.Light(Punchy.Ambience.FireSystem.Wood), Flame = true });
            }
            // A soft blue glow from deep in the pool, an urn sunk on its bed, a coil of rope on the coping.
            m.K.Glows.Add(new Glow { At = r.World(c + new float3(0f, -1f, 0f)), Light = new float4(0.15f, 0.45f, 0.9f, 6f) });
            var urn = m.Rng.NextFloat2Direction() * R * m.Rng.NextFloat(0.3f, 0.6f);
            m.Add(c + new float3(urn.x, -1.45f, urn.y), new float3(0.2f, 0.2f, 0.2f), math.mul(quaternion.RotateZ(1.2f), quaternion.RotateY(m.Rng.NextFloat(3f))), Pale, 0.7f, 2);
            var coilA = postA + m.Rng.NextFloat(0.25f, 0.45f) * side;
            var coil = c + new float3(math.cos(coilA), 0f, math.sin(coilA)) * (R + 0.35f);
            m.Add(coil + new float3(0f, 0.1f, 0f), new float3(0.17f, 0.04f, 0.17f), quaternion.identity, Pale, 0.75f, 2);
            m.Add(coil + new float3(0f, 0.16f, 0f), new float3(0.12f, 0.03f, 0.12f), quaternion.identity, Pale, 0.8f, 2);
            // A fishing rod leaning on a wall.
            if (m.FreeFloor(0.9f, true) is float2 rod)
            {
                var wall = math.abs(rod.x) > r.HX - 1.2f ? new float3(math.sign(rod.x), 0f, 0f) : new float3(0f, 0f, 1f);
                var top = new float3(rod.x, 0f, rod.y) + wall * 0.5f + new float3(0f, 2.1f, 0f);
                var foot = new float3(rod.x, 0.02f, rod.y);
                var along = top - foot;
                m.Add(foot + along * 0.5f, new float3(0.018f, 0.018f, math.length(along) * 0.5f), quaternion.LookRotationSafe(along, new float3(0f, 1f, 0f)), Wood, 1.05f);
            }
            var berth = c + new float3(math.cos(postA), 0f, math.sin(postA)) * (R + 0.95f);
            m.Spots.Add(new Spot { At = berth + new float3(0f, 0.05f, 0f), Face = -math.normalize(new float2(math.cos(postA), math.sin(postA))), Kind = SocketKind.Interactable, Size = SocketSize.Small, Tags = SocketTags.NearWater });
        }

        static void Roost(Maker m)
        {
            var r = m.R;
            float hx = r.HX, d = r.HZ * 2f, h = r.Height;
            // Beams across under the cornice; one has broken and hangs from its wall end.
            var beams = new List<float>();
            for (float z = m.Rng.NextFloat(0.9f, 1.3f); z < d - 0.8f; z += m.Rng.NextFloat(1.2f, 1.6f))
                beams.Add(z);
            var broken = beams.Count > 2 ? m.Rng.NextInt(1, beams.Count - 1) : -1;
            for (int k = 0; k < beams.Count; k++)
            {
                var y = h - 0.55f + m.Rng.NextFloat(-0.06f, 0.06f);
                if (k != broken)
                {
                    m.Span(new float3(-hx - 0.05f, y - 0.13f, beams[k] - 0.1f), new float3(hx + 0.05f, y + 0.13f, beams[k] + 0.1f), Wood, m.Jit(0.85f, 1f));
                    continue;
                }
                var sx = m.Rng.NextBool() ? 1f : -1f;
                m.Span(new float3(sx > 0f ? hx * 0.3f : -hx - 0.05f, y - 0.13f, beams[k] - 0.1f), new float3(sx > 0f ? hx + 0.05f : -hx * 0.3f, y + 0.13f, beams[k] + 0.1f), Wood, 0.9f);
                var pivot = new float3(-sx * hx, y, beams[k]);
                var hang = new float3(sx * math.cos(1.05f), -math.sin(1.05f), 0f) * hx * 0.65f;
                m.Add(pivot + hang, new float3(hx * 0.65f, 0.12f, 0.1f), quaternion.RotateZ(-sx * 1.05f), Wood, 0.8f);
            }
            // Stone perches jutting from the walls, high up, each a Ledge spot.
            for (int k = 0, perches = m.Rng.NextInt(5, 9); k < perches; k++)
            {
                var side = m.Rng.NextInt(3);
                var y = m.Rng.NextFloat(2.4f, h - 1.3f);
                var length = m.Rng.NextFloat(0.8f, 1.4f);
                float3 at, half; float2 face;
                if (side == 2) { at = new float3(m.Rng.NextFloat(-hx + 1f, hx - 1f), y, d - 0.18f); half = new float3(length * 0.5f, 0.07f, 0.24f); face = new float2(0f, -1f); }
                else
                {
                    var sx = side == 0 ? -1f : 1f;
                    at = new float3(sx * (hx - 0.18f), y, m.Rng.NextFloat(1.2f, d - 1f));
                    half = new float3(0.24f, 0.07f, length * 0.5f);
                    face = new float2(-sx, 0f);
                }
                m.Add(at, half, quaternion.identity, Trim, m.Jit(0.8f, 0.95f), 0, true);
                m.Spots.Add(new Spot { At = at + new float3(face.x * 0.05f, 0.1f, face.y * 0.05f), Face = face, Kind = SocketKind.Creature, Size = SocketSize.Small, Tags = SocketTags.Ledge });
            }
            // Two roosts on the ceiling.
            for (int k = 0; k < 2; k++)
                m.Spots.Add(new Spot { At = new float3((k == 0 ? -1f : 1f) * hx * 0.35f, h - 0.35f, r.HZ * (k == 0 ? 0.7f : 1.3f)), Face = new float2(0f, -1f), Kind = SocketKind.Creature, Size = SocketSize.Small, Tags = SocketTags.Ceiling });
            // Guano heaps, bones, a skull and feathers on the floor.
            for (int k = 0, heaps = m.Rng.NextInt(6, 11); k < heaps; k++)
                if (m.FreeFloor(0.9f, m.Rng.NextBool()) is float2 g)
                {
                    var rad = m.Rng.NextFloat(0.2f, 0.6f);
                    var tall = m.Rng.NextFloat(0.03f, 0.12f);
                    m.Add(new float3(g.x, tall * 0.5f, g.y), new float3(rad, tall * 0.5f + 0.01f, rad * m.Rng.NextFloat(0.7f, 1f)), quaternion.RotateY(m.Rng.NextFloat(3f)), Pale, m.Jit(0.8f, 0.95f), 2);
                    if (tall > 0.07f)
                        m.Add(new float3(g.x, tall + 0.02f, g.y), new float3(rad * 0.55f, 0.03f, rad * 0.5f), quaternion.RotateY(m.Rng.NextFloat(3f)), Pale, 0.9f, 2);
                }
            for (int k = 0, bones = m.Rng.NextInt(5, 10); k < bones; k++)
                if (m.FreeFloor(0.6f, false) is float2 b)
                {
                    var rot = math.mul(quaternion.RotateY(m.Rng.NextFloat(6.28f)), quaternion.RotateZ(math.PI * 0.5f));
                    var rad = m.Rng.NextFloat(0.022f, 0.04f);
                    m.Add(new float3(b.x, rad + 0.01f, b.y), new float3(rad, m.Rng.NextFloat(0.1f, 0.22f), rad), rot, Pale, 1.05f, 2);
                }
            if (m.FreeFloor(0.8f, true) is float2 skull)
            {
                m.Add(new float3(skull.x, 0.08f, skull.y), new float3(0.09f, 0.07f, 0.11f), quaternion.RotateY(m.Rng.NextFloat(6.28f)), Pale, 1.08f);
                m.Add(new float3(skull.x, 0.03f, skull.y) + new float3(0.03f, 0f, 0.12f), new float3(0.06f, 0.025f, 0.05f), quaternion.RotateY(m.Rng.NextFloat(6.28f)), Pale, 0.95f);
            }
            for (int k = 0, feathers = m.Rng.NextInt(6, 11); k < feathers; k++)
                if (m.FreeFloor(0.4f, false) is float2 f)
                    m.Add(new float3(f.x, 0.012f, f.y), new float3(0.018f, 0.004f, m.Rng.NextFloat(0.07f, 0.13f)), math.mul(quaternion.RotateY(m.Rng.NextFloat(6.28f)), quaternion.RotateZ(m.Rng.NextFloat(-0.3f, 0.3f))), Wood, m.Jit(0.6f, 1.1f));
            // A dim green glow of worms on the ceiling.
            for (int k = 0; k < 2; k++)
                m.K.Glows.Add(new Glow { At = r.World(new float3(m.Rng.NextFloat(-hx * 0.5f, hx * 0.5f), h - 0.4f, r.HZ * (k == 0 ? 0.6f : 1.4f))), Light = new float4(0.25f, 0.6f, 0.4f, 8f) });
        }

        static void Ruin(Maker m)
        {
            var r = m.R;
            var rng = new Random(m.Rng.NextUInt() | 1u);
            var piece = m.Rng.NextFloat() < 0.55f ? RuinKit.Sanctum(ref rng) : RuinKit.Chamber(ref rng);
            var room = piece.Rooms[0];
            float sz = r.HZ * 2f / (room.U1 - room.U0), sx = r.HX * 2f / (room.V1 - room.V0), sy = r.Height / (room.Ceil - room.Floor);
            var bc = (room.V0 + room.V1) * 0.5f;
            float3 Map(float a, float y, float b) => new float3(-(b - bc) * sx, (y - room.Floor) * sy, (a - room.U0) * sz);
            bool InDoor(float x0, float x1, float z0) => x1 > -r.DoorWidth * 0.5f - 0.3f && x0 < r.DoorWidth * 0.5f + 0.3f && z0 < 2f;
            var altar = float3.zero; var hasAltar = false;
            foreach (var box in piece.Boxes)
            {
                if (box.Outside != 0)
                    continue;
                var lo = Map(box.UV.x, box.Y.x, box.UV.w);
                var hi = Map(box.UV.y, math.min(box.Y.y, room.Ceil), box.UV.z);
                var mn = math.min(lo, hi); var mx = math.max(lo, hi);
                mx.y = math.min(mx.y, r.Height);
                if (InDoor(mn.x, mx.x, mn.z))
                    continue;
                var c = (mn + mx) * 0.5f; var half = (mx - mn) * 0.5f;
                switch (box.Draw)
                {
                    case 1:
                        var rad = math.min(half.x, half.z);
                        m.Add(c, new float3(rad, half.y, rad), quaternion.identity, Trim, m.Jit(0.88f, 1f), 2);
                        m.K.Solids.Add(new Solid { Centre = r.World(c), Half = new float3(rad * 0.85f, half.y, rad * 0.85f), Rotation = m.Yaw });
                        // A base and (on a whole pillar) a capital.
                        m.Add(new float3(c.x, mn.y + 0.12f, c.z), new float3(rad + 0.1f, 0.12f, rad + 0.1f), quaternion.identity, Stone, 0.9f);
                        if (mx.y > r.Height - 0.3f)
                            m.Add(new float3(c.x, mx.y - 0.16f, c.z), new float3(rad + 0.14f, 0.16f, rad + 0.14f), quaternion.identity, Stone, 0.95f);
                        else
                            m.Add(new float3(c.x + rad * 0.6f, 0.15f, c.z + rad * 1.4f), new float3(rad * 0.7f, 0.15f, rad * 0.5f), quaternion.RotateY(m.Rng.NextFloat(3f)), Trim, 0.85f);
                        break;
                    case 2:
                    case 3:
                        // A fallen drum lying along z (2) or x (3).
                        var along = box.Draw == 2 ? half.z : half.x;
                        var across = math.min(box.Draw == 2 ? half.x : half.z, half.y);
                        var rot = box.Draw == 2 ? quaternion.RotateX(math.PI * 0.5f) : quaternion.RotateZ(math.PI * 0.5f);
                        m.Add(new float3(c.x, across, c.z), new float3(across, along, across), rot, Trim, m.Jit(0.85f, 0.98f), 2, true);
                        break;
                    default:
                        m.Add(c, half, quaternion.identity, Stone, m.Jit(0.9f, 1.05f), 0, true);
                        if (!hasAltar && half.y < 0.7f && half.y > 0.35f) { altar = c; hasAltar = true; }
                        break;
                }
            }
            foreach (var s in piece.Sconces)
            {
                var at = Map(s.At.x, s.At.y, s.At.z);
                var n = math.normalize(new float3(-s.N.y * sx, 0f, s.N.x * sz));
                if (InDoor(at.x - 0.3f, at.x + 0.3f, at.z) || math.abs(at.x) > r.HX + 0.1f && math.abs(at.z - r.HZ) > r.HZ + 0.1f)
                    continue;
                at.x = math.clamp(at.x, -r.HX, r.HX); at.z = math.clamp(at.z, 0f, r.HZ * 2f);
                at.y = math.min(at.y, r.Height - 1f);
                m.Add(at + n * 0.12f, new float3(0.06f, 0.06f, 0.06f) + math.abs(n) * 0.08f, quaternion.identity, Wood, 0.9f);
                m.Add(at + n * 0.28f + new float3(0f, 0.06f, 0f), new float3(0.07f, 0.05f, 0.07f), quaternion.identity, Wood, 1f, 2);
                m.K.Glows.Add(new Glow { At = r.World(at + n * 0.28f + new float3(0f, 0.2f, 0f)), Light = (Vector4)Punchy.Ambience.FireSystem.Light(Punchy.Ambience.FireSystem.Wood), Flame = true });
            }
            // Drifts become rubble: fallen blocks in a heap.
            foreach (var drift in piece.Drifts)
            {
                var c = Map(drift.Ellipse.x, 0f, drift.Ellipse.y);
                for (int k = 0, count = m.Rng.NextInt(3, 7); k < count; k++)
                {
                    var off = m.Rng.NextFloat2(-1f, 1f) * new float2(drift.Ellipse.w * sx, drift.Ellipse.z * sz) * 0.5f;
                    var at = c.xz + off;
                    if (!m.Clear(at, 0.8f, 0.4f) && !(math.abs(at.x) < r.HX - 0.4f && at.y > 0.4f && at.y < r.HZ * 2f - 0.4f && !InDoor(at.x, at.x, at.y)))
                        continue;
                    if (InDoor(at.x - 0.3f, at.x + 0.3f, at.y))
                        continue;
                    var s = m.Rng.NextFloat(0.1f, 0.26f);
                    m.Add(new float3(at.x, s * 0.6f, at.y), new float3(s, s * 0.7f, s * 0.8f), quaternion.EulerXYZ(m.Rng.NextFloat3(-0.4f, 0.4f)), Stone, m.Jit(0.8f, 0.98f));
                }
            }
            // A mosaic border round the floor, inside the plinth, along the sides and back.
            const float band = 0.62f, tile = 0.24f;
            void Border(float3 from, float3 to)
            {
                var along = to - from;
                var count = (int)(math.length(along) / tile);
                for (int k = 0; k < count; k++)
                {
                    var at = from + along * ((k + 0.5f) / count);
                    m.Add(at + new float3(0f, 0.008f, 0f), new float3(tile * 0.44f, 0.012f, tile * 0.44f), quaternion.identity, (k & 1) == 0 ? Pale : Trim, m.Jit(0.85f, 1f));
                }
            }
            Border(new float3(-r.HX + band, 0f, 1.6f), new float3(-r.HX + band, 0f, r.HZ * 2f - band));
            Border(new float3(r.HX - band, 0f, 1.6f), new float3(r.HX - band, 0f, r.HZ * 2f - band));
            Border(new float3(-r.HX + band + tile, 0f, r.HZ * 2f - band), new float3(r.HX - band - tile, 0f, r.HZ * 2f - band));
            if (hasAltar)
            {
                var toward = math.normalizesafe(new float2(0f, r.HZ) - altar.xz, new float2(0f, -1f));
                var at = altar.xz + toward * 1.2f;
                m.Spots.Add(new Spot { At = new float3(at.x, 0.05f, at.y), Face = -toward, Kind = SocketKind.Interactable, Size = SocketSize.Small, Tags = SocketTags.NearAltar });
            }
        }

        /// <summary>
        /// Drops spots the set has filled (inside or against a solid part, or in the pool), keeps one chest, tags
        /// neighbours (near the chest, the water, the altar), and moves the spots to the world.
        /// </summary>
        static void Finish(Maker m)
        {
            var r = m.R;
            bool Hit(float3 local, float margin)
            {
                var w = r.World(local + new float3(0f, 0.4f, 0f));
                foreach (var s in m.K.Solids)
                {
                    var l = math.mul(math.inverse(s.Rotation), w - s.Centre);
                    if (math.all(math.abs(l) < s.Half + margin))
                        return true;
                }
                return false;
            }
            var chest = false;
            var kept = new List<Spot>();
            foreach (var s in m.Spots)
            {
                var floor = s.At.y < 0.5f;
                var margin = s.Kind == SocketKind.WallDecor ? 0.3f : floor ? 0.45f : 0.15f;
                if (floor && Hit(s.At, margin) || s.Kind == SocketKind.WallDecor && Hit(s.At - new float3(0f, 0.4f, 0f), margin))
                    continue;
                if (m.LakeR > 0f && floor && s.Kind != SocketKind.Interactable && math.distance(s.At.xz, new float2(0f, r.HZ)) < m.LakeR + 0.6f)
                    continue;
                if (s.Kind == SocketKind.Chest)
                {
                    if (chest) continue;
                    chest = true;
                }
                kept.Add(s);
            }
            float3 chestAt = default, altarAt = default;
            foreach (var s in kept)
            {
                if (s.Kind == SocketKind.Chest) chestAt = s.At;
                if ((s.Tags & SocketTags.NearAltar) != 0) altarAt = s.At;
            }
            foreach (var s0 in kept)
            {
                var s = s0;
                if (chest && math.distance(s.At, chestAt) < 4f) s.Tags |= SocketTags.NearChest;
                if (altarAt.z > 0f && math.distance(s.At, altarAt) < 3f) s.Tags |= SocketTags.NearAltar;
                if (m.LakeR > 0f && math.distance(s.At.xz, new float2(0f, r.HZ)) < m.LakeR + 2.5f) s.Tags |= SocketTags.NearWater;
                var face = r.Right.xz * s.Face.x + r.Forward.xz * s.Face.y;
                m.K.Spots.Add(new Spot { At = r.World(s.At), Face = math.normalizesafe(face), Kind = s.Kind, Size = s.Size, Tags = s.Tags });
            }
        }

        // ------------------------------------------------------------------------------------------- collision

        /// <summary>
        /// Adds the solid parts of rooms near any of the centres (players) to the kit collision buffer, as
        /// BatFolkHomes.AddKitBoxes does for the pygmy niches; FloraSystem refreshes it every half second.
        /// </summary>
        public static void AddKitBoxes(in TerrainSource source, List<float2> centres, DynamicBuffer<KitBox> boxes, float range = 16f)
        {
            if (!source.Caves.IsCreated)
                return;
            ref var set = ref source.Caves.Value;
            for (int i = 0; i < set.Rooms.Length; i++)
            {
                var room = set.Rooms[i];
                var reach = range + math.length(new float2(room.HX, room.HZ)) + 2f;
                var near = false;
                foreach (var c in centres)
                    near |= math.distancesq(c, room.Centre.xz) < reach * reach;
                if (!near)
                    continue;
                foreach (var s in Get(room).Solids)
                    boxes.Add(new KitBox { Centre = s.Centre, Half = s.Half, Rotation = s.Rotation, Reach = math.length(s.Half) });
            }
        }

        // ------------------------------------------------------------------------------------------- meshes

        /// <summary>One mesh per material group (null where the room has none), in the cave walls' vertex layout.</summary>
        public static Mesh[] BuildMeshes(Kit kit)
        {
            var builders = new Builder[Groups];
            foreach (var p in kit.Parts)
            {
                var b = builders[p.Group] ??= new Builder();
                switch (p.Shape)
                {
                    case 1: b.Prism(p.Centre, p.Rotation, p.Half.x, p.Half.y, p.Tint); break;
                    case 2: b.Drum(p.Centre, p.Half, p.Rotation, p.Tint); break;
                    default: b.Box(p.Centre, p.Half, p.Rotation, p.Tint); break;
                }
            }
            var meshes = new Mesh[Groups];
            for (int g = 0; g < Groups; g++)
                meshes[g] = builders[g]?.ToMesh("Cave room " + g);
            return meshes;
        }

        class Builder
        {
            readonly List<Vector3> positions = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector4> data = new List<Vector4>();
            readonly List<int> indices = new List<int>();

            void Vertex(float3 p, float3 n, float tint)
            {
                positions.Add(p);
                normals.Add(math.normalizesafe(n, new float3(0f, 1f, 0f)));
                // x terrain blend 0, y tint, z style slot 0 (the material's own colours), w rib shade 0.
                data.Add(new Vector4(0f, tint, 0f, 0f));
            }

            void Quad(float3 a, float3 b, float3 c, float3 d, float3 n, float tint)
            {
                var start = positions.Count;
                Vertex(a, n, tint); Vertex(b, n, tint); Vertex(c, n, tint); Vertex(d, n, tint);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
            }

            void Tri(float3 a, float3 b, float3 c, float3 n, float tint)
            {
                var start = positions.Count;
                Vertex(a, n, tint); Vertex(b, n, tint); Vertex(c, n, tint);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            }

            /// <summary>A box round centre, turned by rotation; darker underneath.</summary>
            public void Box(float3 centre, float3 half, quaternion rotation, float tint)
            {
                for (int f = 0; f < 6; f++)
                {
                    var axis = f % 3;
                    var sign = f < 3 ? 1f : -1f;
                    var n = float3.zero; n[axis] = sign;
                    var u = float3.zero; u[(axis + 1) % 3] = 1f;
                    var v = float3.zero; v[(axis + 2) % 3] = 1f;
                    var wn = math.mul(rotation, n);
                    var faceTint = wn.y < -0.5f ? tint * 0.6f : tint;
                    float3 P(float su, float sv) => centre + math.mul(rotation, (n + u * su + v * sv) * half);
                    Quad(P(-1f, -1f), P(1f, -1f), P(1f, 1f), P(-1f, 1f), wn, faceTint);
                }
            }

            /// <summary>A hex crystal from its base along the rotation's up: a slightly narrower foot, six faces, a pointed tip.</summary>
            public void Prism(float3 b, quaternion rotation, float radius, float length, float tint)
            {
                var axis = math.mul(rotation, new float3(0f, 1f, 0f));
                var u = math.mul(rotation, new float3(1f, 0f, 0f));
                var w = math.mul(rotation, new float3(0f, 0f, 1f));
                var shoulder = length * 0.72f;
                var tip = b + axis * length;
                float3 Ring(int k, float rad, float y)
                {
                    var a = k * math.PI / 3f;
                    return b + (u * math.cos(a) + w * math.sin(a)) * rad + axis * y;
                }
                for (int k = 0; k < 6; k++)
                {
                    var mid = (k + 0.5f) * math.PI / 3f;
                    var out_ = u * math.cos(mid) + w * math.sin(mid);
                    var shade = 0.82f + 0.18f * ((k * 5) % 3) / 2f;
                    Quad(Ring(k, radius * 0.8f, 0f), Ring(k + 1, radius * 0.8f, 0f), Ring(k + 1, radius, shoulder), Ring(k, radius, shoulder), out_, tint * shade);
                    Tri(Ring(k, radius, shoulder), Ring(k + 1, radius, shoulder), tip, out_ * (length - shoulder) + axis * radius, tint * (shade + 0.15f));
                }
            }

            /// <summary>A ten-sided drum along the rotation's up (radius half.x, half height half.y), capped.</summary>
            public void Drum(float3 centre, float3 half, quaternion rotation, float tint)
            {
                const int sides = 10;
                var axis = math.mul(rotation, new float3(0f, 1f, 0f));
                var u = math.mul(rotation, new float3(1f, 0f, 0f));
                var w = math.mul(rotation, new float3(0f, 0f, 1f));
                var rz = half.z > 0f ? half.z : half.x;
                float3 P(int k, float s)
                {
                    var a = k * math.PI * 2f / sides;
                    return centre + u * (math.cos(a) * half.x) + w * (math.sin(a) * rz) + axis * (half.y * s);
                }
                var top = centre + axis * half.y;
                var bottom = centre - axis * half.y;
                for (int k = 0; k < sides; k++)
                {
                    var mid = (k + 0.5f) * math.PI * 2f / sides;
                    var n = u * math.cos(mid) + w * math.sin(mid);
                    Quad(P(k, -1f), P(k + 1, -1f), P(k + 1, 1f), P(k, 1f), n, tint * (0.9f + 0.1f * (k & 1)));
                    Tri(P(k, 1f), P(k + 1, 1f), top, axis, tint * 1.05f);
                    Tri(P(k + 1, -1f), P(k, -1f), bottom, -axis, tint * 0.6f);
                }
            }

            public Mesh ToMesh(string name)
            {
                if (positions.Count == 0)
                    return null;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                // Colour alpha is the walls' openness: 0, deep in the cave (no daylight, no fog).
                var colors = new Color[positions.Count];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color(0.5f, 0.5f, 0.5f, 0f);
                mesh.SetColors(colors);
                mesh.SetUVs(0, data);
                mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }
    }
}
