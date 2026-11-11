using System.Collections.Generic;
using Punchy.Flora;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-110 row 2: the portal kit piece at each end of a road tunnel (CaveBaking.Tunnels). A stone headwall in ashlar
    /// courses stands across the cutting's end with the passage's own arch cut out of it: jambs on plinths, impost
    /// blocks, a ring of voussoirs round the arch with a keystone at the crown, and a coping along the top. Each portal
    /// varies from its own seed: how much moss lies on the coping, the voussoirs and the face, sometimes an ivy fringe
    /// hanging from the coping, a cracked keystone, a mark carved on the keystone, and a fallen stone or two (and now and
    /// then a small rockfall) beside the jambs. All seed-derived from the cave set, so solo, host and client build the
    /// same portals. Drawn by CaveSystem.DrawPortals with copies of the cave material, open to daylight (vertex openness
    /// 1) so the sun, fog and the shader's own moss reach it like the ground; the jambs, the headwall's feet and the
    /// fallen stones are kit boxes (CaveRoomKit.AddKitBoxes adds them with the rooms).
    /// </summary>
    public static class TunnelPortalKit
    {
        /// <summary>Material groups (indices into CaveRoomKit's group slots), each drawn with its own colours.</summary>
        public const int Stone = 0, Trim = 1, Moss = 2, Ivy = 3, Groups = CaveRoomKit.Groups;

        /// <summary>One portal: the middle of the road at the tunnel's end, the way out, and the passage's radius.</summary>
        public struct Portal
        {
            public int Cave, End;
            public float3 Floor;
            public float2 Out;
            public float Radius;
            public uint Seed;
        }

        /// <summary>The rock and dark tones a portal group is drawn with.</summary>
        public static void Colours(int group, out Color rock, out Color dark)
        {
            switch (group)
            {
                case Stone: rock = new Color(0.64f, 0.61f, 0.55f); dark = new Color(0.37f, 0.35f, 0.31f); break;
                case Trim: rock = new Color(0.72f, 0.69f, 0.62f); dark = new Color(0.44f, 0.41f, 0.36f); break;
                case Moss: rock = new Color(0.4f, 0.52f, 0.2f); dark = new Color(0.18f, 0.28f, 0.08f); break;
                default: rock = new Color(0.24f, 0.44f, 0.17f); dark = new Color(0.08f, 0.2f, 0.06f); break;
            }
        }

        /// <summary>Both portals of every road tunnel in the cave set (its first and last road nodes, facing out).</summary>
        public static void Portals(ref CaveSetBlob set, List<Portal> into)
        {
            into.Clear();
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                if (cave.Tunnel == 0 || cave.NodeCount < 4)
                    continue;
                for (int end = 0; end < 2; end++)
                {
                    // Nodes run overrun, road ends..., overrun: the road's end is one in from each end of the list.
                    var outer = set.Nodes[end == 0 ? cave.FirstNode : cave.FirstNode + cave.NodeCount - 1];
                    var road = set.Nodes[end == 0 ? cave.FirstNode + 1 : cave.FirstNode + cave.NodeCount - 2];
                    var radius = road.Radius * cave.Profile.x;
                    var floor = road.Position - new float3(0f, radius * cave.Profile.y * 0.45f, 0f);
                    into.Add(new Portal
                    {
                        Cave = c, End = end, Floor = floor, Out = math.normalizesafe(outer.Position.xz - road.Position.xz),
                        Radius = radius, Seed = math.hash(new uint2(cave.Key, 0xA2C0u + (uint)end)) | 1u
                    });
                }
            }
        }

        static readonly Dictionary<(int, int), CaveRoomKit.Kit> Cache = new Dictionary<(int, int), CaveRoomKit.Kit>();

        /// <summary>The portal's kit, built once (from its seed) and kept until <see cref="ClearCache"/>.</summary>
        public static CaveRoomKit.Kit Get(in TerrainSource source, in Portal portal)
        {
            var key = (portal.Cave, portal.End);
            if (!Cache.TryGetValue(key, out var kit))
            {
                kit = Build(source, portal);
                Cache[key] = kit;
            }
            return kit;
        }

        /// <summary>Forgets every built kit (the world was released or re-baked).</summary>
        public static void ClearCache() => Cache.Clear();

        static readonly List<Portal> Scratch = new List<Portal>();

        /// <summary>Adds the solid parts of portals near any of the centres (players) to the kit collision buffer.</summary>
        public static void AddKitBoxes(in TerrainSource source, List<float2> centres, DynamicBuffer<KitBox> boxes, float range)
        {
            Portals(ref source.Caves.Value, Scratch);
            foreach (var p in Scratch)
            {
                var reach = range + p.Radius + 6f;
                var near = false;
                foreach (var c in centres)
                    near |= math.distancesq(c, p.Floor.xz) < reach * reach;
                if (!near)
                    continue;
                foreach (var s in Get(source, p).Solids)
                    boxes.Add(new KitBox { Centre = s.Centre, Half = s.Half, Rotation = s.Rotation, Reach = math.length(s.Half) });
            }
        }

        // ------------------------------------------------------------------------------------------- the kit

        /// <summary>Lays out one portal in its own frame (x across, y up from the road, z out of the hill).</summary>
        sealed class Maker
        {
            public Portal P;
            public CaveRoomKit.Kit K;
            public Random Rng;
            public float3 Right, Out;
            public quaternion Frame;
            /// <summary>The arch: its springing height, inner half width and inner rise, and the jambs' inner faces.</summary>
            public float Spring, ArchX, ArchY, JambX;

            public float3 World(float3 l) => P.Floor + Right * l.x + new float3(0f, l.y, 0f) + Out * l.z;

            public void Add(float3 local, float3 half, quaternion rot, int group, float tint, byte shape = 0, bool solid = false)
            {
                var c = World(local);
                var q = math.mul(Frame, rot);
                K.Parts.Add(new CaveRoomKit.Part { Centre = c, Half = half, Rotation = q, Tint = tint, Group = (byte)group, Shape = shape });
                if (solid)
                    K.Solids.Add(new CaveRoomKit.Solid { Centre = c, Half = half, Rotation = q });
            }

            public void Span(float3 lo, float3 hi, int group, float tint, bool solid = false) =>
                Add((lo + hi) * 0.5f, math.abs(hi - lo) * 0.5f, quaternion.identity, group, tint, 0, solid);

            public float Jit(float lo = 0.84f, float hi = 1.04f) => Rng.NextFloat(lo, hi);

            /// <summary>Whether a point of the portal's face is in the opening (the arch above the springing, the gap
            /// between the jambs below it).</summary>
            public bool InOpening(float x, float y)
            {
                if (y < 0f)
                    return false;
                if (y < Spring)
                    return math.abs(x) < JambX;
                var e = new float2(x / ArchX, (y - Spring) / ArchY);
                return math.lengthsq(e) < 1f;
            }

            /// <summary>The ground's height in the portal's frame at a local spot (for stones that lie on it).</summary>
            public float Ground(in TerrainSource source, float x, float z) => source.SampleHeight(World(new float3(x, 0f, z)).xz) - P.Floor.y;

            /// <summary>
            /// W-137: the ground as the terrain mesh draws it at a local spot, in the portal's frame: the highest (or
            /// lowest) of its LOD0-2 grids (1, 2 and 4 m, TerrainSource.MeshHeight), so masonry meets the ground at
            /// whichever LOD the terrain round it is drawn.
            /// </summary>
            public float Drawn(in TerrainSource source, float x, float z, bool highest)
            {
                var xz = World(new float3(x, 0f, z)).xz;
                float a = Mesh(source, xz, 1f), b = Mesh(source, xz, 2f), c = Mesh(source, xz, 4f);
                return (highest ? math.max(a, math.max(b, c)) : math.min(a, math.min(b, c))) - P.Floor.y;
            }

            /// <summary>W-137: whether the terrain mesh is cut open for the tunnel's mouth at a local spot (its cut as the
            /// finest grid samples it there; BuildChunkMeshJob cuts at LOD 0 and 1 only).</summary>
            public bool Cut(in TerrainSource source, float x, float z)
            {
                var xz = World(new float3(x, 0f, z)).xz;
                return source.MouthCut(xz, Mesh(source, xz, 1f), TerrainSource.MeshCutDepth, out _) < 0f;
            }

            /// <summary>TerrainSource.MeshHeight, its corner heights kept (every grid's corners lie on the half-metre
            /// lattice), since the cutting samples the same corners many times over.</summary>
            float Mesh(in TerrainSource source, float2 xz, float step)
            {
                var grid = xz / step;
                var corner = math.floor(grid);
                var f = grid - corner;
                if (f.x + f.y <= 1f)
                {
                    var h00 = Height(source, corner * step);
                    return h00 + f.x * (Height(source, (corner + new float2(1f, 0f)) * step) - h00) + f.y * (Height(source, (corner + new float2(0f, 1f)) * step) - h00);
                }
                var h11 = Height(source, (corner + 1f) * step);
                return h11 + (1f - f.x) * (Height(source, (corner + new float2(0f, 1f)) * step) - h11) + (1f - f.y) * (Height(source, (corner + new float2(1f, 0f)) * step) - h11);
            }

            readonly Dictionary<long, float> heights = new Dictionary<long, float>();

            float Height(in TerrainSource source, float2 corner)
            {
                var key = ((long)math.round(corner.x * 2f) << 32) ^ (uint)(int)math.round(corner.y * 2f);
                if (!heights.TryGetValue(key, out var h))
                {
                    h = source.SampleHeight(corner);
                    heights[key] = h;
                }
                return h;
            }
        }

        /// <summary>The coping's thickness.</summary>
        const float CopingHeight = 0.22f;

        public static CaveRoomKit.Kit Build(in TerrainSource source, in Portal portal)
        {
            var out3 = new float3(portal.Out.x, 0f, portal.Out.y);
            var m = new Maker
            {
                P = portal,
                K = new CaveRoomKit.Kit(),
                Rng = new Random(portal.Seed),
                Out = out3,
                Right = new float3(out3.z, 0f, -out3.x),
                Frame = quaternion.LookRotation(out3, new float3(0f, 1f, 0f))
            };
            var r = portal.Radius;
            // The passage's section (CaveSampling.Capsule, Profile (1, 0.8)): an ellipse r across and 0.8 r high round
            // 0.36 r over the road. The arch frames it a little inside, so the noisy rock edge hides behind the stones.
            m.Spring = 0.36f * r;
            m.ArchX = 0.95f * r;
            m.ArchY = 0.8f * r * 0.95f;
            m.JambX = 0.86f * r;
            var rng = m.Rng;
            var moss = rng.NextFloat();            // 0 bare .. 1 thickly mossed
            var ivy = rng.NextFloat() < 0.4f;
            var cracked = rng.NextFloat() < 0.3f;
            var mark = rng.NextFloat() < 0.4f ? rng.NextInt(4) : -1;
            var rockfall = rng.NextFloat() < 0.4f;
            var fallen = rng.NextInt(1, 4);
            var row = rng.NextFloat(0.36f, 0.48f);
            var ring = rng.NextFloat(0.55f, 0.72f);    // the voussoirs' depth across the ring
            m.Rng = rng;

            Arch(m, ring, cracked, mark);
            var top = Headwall(m, ring, row, out var halfW);
            Dressing(source, m, top, moss, ivy, rockfall, fallen);
            Cutting(source, m, ring, row, top - CopingHeight, halfW, moss);
            return m.K;
        }

        /// <summary>Jambs on plinths, impost blocks, the voussoirs and the keystone (cracked, carved or plain).</summary>
        static void Arch(Maker m, float ring, bool cracked, int mark)
        {
            const float depth = 0.95f, front = 0.28f;   // the stones' depth along the road, and how far they stand out
            var outerX = m.ArchX + ring;
            var jamb = outerX - m.JambX;
            for (int side = -1; side <= 1; side += 2)
            {
                var x0 = side * m.JambX;
                var x1 = side * outerX;
                var lo = math.min(x0, x1);
                var hi = math.max(x0, x1);
                // A plinth a little proud, the jamb in two or three stones, an impost under the springing.
                m.Span(new float3(lo - 0.06f, 0f, front - depth - 0.06f), new float3(hi + 0.06f, 0.42f, front + 0.08f), CaveRoomKit.Trim, m.Jit(), true);
                var courses = m.Spring > 2.2f ? 3 : 2;
                var y = 0.42f;
                for (int k = 0; k < courses; k++)
                {
                    var y1 = k == courses - 1 ? m.Spring - 0.2f : y + (m.Spring - 0.62f) / courses * m.Jit(0.9f, 1.1f);
                    m.Span(new float3(lo + 0.02f, y + 0.02f, front - depth), new float3(hi - 0.02f, y1 - 0.02f, front), Stone, m.Jit(), true);
                    y = y1;
                }
                m.Span(new float3(lo - 0.05f, m.Spring - 0.2f, front - depth - 0.04f), new float3(hi + 0.08f, m.Spring, front + 0.07f), Trim, m.Jit(0.92f, 1.06f));
            }
            // The voussoirs: an odd count so one sits at the crown, each a wedge-ish box square to the ellipse.
            var count = 2 * (int)math.round(math.PI * (m.ArchX + m.ArchY) * 0.5f / 0.62f / 2f) + 1;
            count = math.clamp(count, 9, 19);
            for (int i = 0; i < count; i++)
            {
                var a0 = math.PI * i / count;
                var a1 = math.PI * (i + 1) / count;
                var am = (a0 + a1) * 0.5f;
                float2 Inner(float a) => new float2(math.cos(a) * m.ArchX, m.Spring + math.sin(a) * m.ArchY);
                var p0 = Inner(a0);
                var p1 = Inner(a1);
                var normal = math.normalize(new float2(math.cos(am) / m.ArchX, math.sin(am) / m.ArchY));
                var key = i == count / 2;
                var thick = key ? ring * 1.3f : ring * m.Jit(0.95f, 1.05f);
                var chord = math.distance(p0, p1);
                var mid = (p0 + p1) * 0.5f + normal * thick * 0.5f;
                var roll = quaternion.RotateZ(math.atan2(normal.y, normal.x) - math.PI * 0.5f);
                var proud = key ? front + 0.07f : front;
                var half = new float3(chord * 0.5f + (key ? 0.04f : -0.02f), thick * 0.5f, depth * 0.5f);
                var centre = new float3(mid.x, mid.y, proud - depth * 0.5f);
                if (!key)
                {
                    m.Add(centre, half, roll, Stone, m.Jit());
                    continue;
                }
                if (cracked)
                {
                    // Two halves with a dark crack between, one sagged a few centimetres and both turned a little.
                    var h = new float3(half.x * 0.5f - 0.015f, half.y, half.z);
                    m.Add(centre + new float3(-half.x * 0.5f, 0f, 0f), h, math.mul(roll, quaternion.RotateZ(0.03f)), Trim, m.Jit());
                    m.Add(centre + new float3(half.x * 0.5f, -0.045f, 0.01f), h, math.mul(roll, quaternion.RotateZ(-0.05f)), Trim, m.Jit());
                    m.Add(centre + new float3(0f, 0f, -0.05f), new float3(0.03f, half.y * 0.95f, half.z * 0.95f), roll, Stone, 0.3f);
                }
                else
                    m.Add(centre, half, roll, Trim, m.Jit(0.95f, 1.08f));
                if (mark >= 0)
                    Mark(m, new float3(mid.x, mid.y, proud + 0.006f), mark, half);
            }
        }

        /// <summary>A mark cut into the keystone's face (a ring, a chevron, a cross or a tally), as dark inlay.</summary>
        static void Mark(Maker m, float3 face, int mark, float3 key)
        {
            const float tint = 0.38f;
            var s = math.min(key.x, key.y) * 0.75f;
            var w = math.max(0.035f, s * 0.12f);
            switch (mark)
            {
                case 0:
                    m.Add(face, new float3(s * 0.75f, 0.008f, s * 0.75f), quaternion.RotateX(math.PI * 0.5f), Stone, tint, 2);
                    m.Add(face + new float3(0f, 0f, 0.004f), new float3(s * 0.5f, 0.008f, s * 0.5f), quaternion.RotateX(math.PI * 0.5f), Trim, 1f, 2);
                    break;
                case 1:
                    m.Add(face + new float3(-s * 0.32f, 0f, 0f), new float3(w, s * 0.6f, 0.008f), quaternion.RotateZ(0.6f), Stone, tint);
                    m.Add(face + new float3(s * 0.32f, 0f, 0f), new float3(w, s * 0.6f, 0.008f), quaternion.RotateZ(-0.6f), Stone, tint);
                    break;
                case 2:
                    m.Add(face, new float3(w, s * 0.8f, 0.008f), quaternion.identity, Stone, tint);
                    m.Add(face + new float3(0f, s * 0.15f, 0f), new float3(s * 0.55f, w, 0.008f), quaternion.identity, Stone, tint);
                    break;
                default:
                    for (int k = -1; k <= 1; k++)
                        m.Add(face + new float3(k * s * 0.38f, 0f, 0f), new float3(w, s * 0.6f, 0.008f), quaternion.identity, Stone, tint);
                    m.Add(face, new float3(s * 0.75f, w, 0.008f), quaternion.RotateZ(0.35f), Stone, tint);
                    break;
            }
        }

        /// <summary>
        /// The headwall: ashlar courses either side of and over the arch, set back behind the arch stones, cut into
        /// small setts where they meet the opening so none crosses it; a dark bed behind; a coping on top. Returns the
        /// coping's top, and the wall's half width.
        /// </summary>
        static float Headwall(Maker m, float ring, float row, out float wallHalf)
        {
            const float face = 0.12f, depth = 0.7f, grout = 0.035f;
            var halfW = m.ArchX + ring + m.Rng.NextFloat(0.9f, 1.6f);
            var height = m.Spring + m.ArchY + ring * 1.3f + m.Rng.NextFloat(0.35f, 0.7f);
            height = row * math.ceil(height / row);
            // The bed behind, in two pieces either side and one over the arch, all clear of the opening.
            var crown = m.Spring + m.ArchY + 0.05f;
            var outerX = m.ArchX + ring;
            m.Span(new float3(-halfW, 0f, face - depth - 0.05f), new float3(-m.JambX, height, face - depth + 0.02f), Stone, 0.4f);
            m.Span(new float3(m.JambX, 0f, face - depth - 0.05f), new float3(halfW, height, face - depth + 0.02f), Stone, 0.4f);
            m.Span(new float3(-outerX, crown, face - depth - 0.05f), new float3(outerX, height, face - depth + 0.02f), Stone, 0.4f);
            for (float b0 = 0f; b0 < height - 0.05f; b0 += row)
            {
                var b1 = math.min(height, b0 + row);
                var a = -halfW - m.Rng.NextFloat(0f, row * 1.6f);
                while (a < halfW)
                {
                    var a1 = a + row * m.Rng.NextFloat(1.4f, 2.6f);
                    float lo = math.max(a, -halfW), hi = math.min(a1, halfW);
                    a = a1;
                    var tint = m.Jit();
                    var out_ = m.Rng.NextFloat() < 0.05f ? 0.05f : 0f;
                    if (hi - lo < 0.12f)
                        continue;
                    if (!m.InOpening(lo, b0) && !m.InOpening(hi, b0) && !m.InOpening(lo, b1) && !m.InOpening(hi, b1) && !m.InOpening((lo + hi) * 0.5f, b0))
                    {
                        Block(lo, hi, b0, b1, out_, tint, grout);
                        continue;
                    }
                    // Near the opening: small setts, any that touch it left out (the arch stones cover the edge).
                    int nu = (int)math.ceil((hi - lo) / 0.26f), nv = (int)math.ceil((b1 - b0) / 0.26f);
                    for (int i = 0; i < nu; i++)
                        for (int j = 0; j < nv; j++)
                        {
                            float s0 = lo + (hi - lo) * i / nu, s1 = lo + (hi - lo) * (i + 1) / nu;
                            float t0 = b0 + (b1 - b0) * j / nv, t1 = b0 + (b1 - b0) * (j + 1) / nv;
                            if (m.InOpening(s0, t0) || m.InOpening(s1, t0) || m.InOpening(s0, t1) || m.InOpening(s1, t1))
                                continue;
                            Block(s0, s1, t0, t1, 0f, tint * m.Jit(0.94f, 1.04f), grout * 0.7f);
                        }
                }
            }
            // The headwall's feet beside the jambs are solid (the cutting may be wide enough to reach them).
            m.K.Solids.Add(new CaveRoomKit.Solid { Centre = m.World(new float3(-(outerX + halfW) * 0.5f, 1.2f, face - depth * 0.5f)), Half = new float3((halfW - outerX) * 0.5f, 1.2f, depth * 0.5f), Rotation = m.Frame });
            m.K.Solids.Add(new CaveRoomKit.Solid { Centre = m.World(new float3((outerX + halfW) * 0.5f, 1.2f, face - depth * 0.5f)), Half = new float3((halfW - outerX) * 0.5f, 1.2f, depth * 0.5f), Rotation = m.Frame });
            // The coping: a course of long slabs, overhanging front and ends.
            // (The end is held in one local: comparing against a recomputed halfW + 0.12f could, at higher float
            // precision, never be reached by the clamped c and loop forever.)
            float c = -halfW - 0.12f, end = halfW + 0.12f;
            while (end - c > 0.05f)
            {
                var c1 = math.min(end, c + m.Rng.NextFloat(0.8f, 1.4f));
                m.Span(new float3(c + 0.015f, height, face - depth - 0.04f), new float3(c1 - 0.015f, height + CopingHeight, face + 0.12f), Trim, m.Jit(0.9f, 1.05f));
                c = c1;
            }
            wallHalf = halfW;
            return height + CopingHeight;

            void Block(float a0, float a1, float t0, float t1, float proud, float tint, float g)
            {
                var half = new float3(math.max(0.02f, (a1 - a0 - g) * 0.5f), math.max(0.02f, (t1 - t0 - g) * 0.5f), depth * 0.5f);
                m.Add(new float3((a0 + a1) * 0.5f, (t0 + t1) * 0.5f, face + proud - depth * 0.5f), half, quaternion.identity, Stone, tint);
            }
        }

        /// <summary>
        /// W-137 row 2b: where a road tunnel runs out of a slope its passage stands higher than the ground for its first
        /// metres, so the terrain is cut open over it (TerrainSource.MouthCut) back to where the hill finally covers
        /// it, and a little in front of the face beside the road. The headwall alone left that cut as a roofless trench
        /// behind it and a strip of hole each side of the road in front. This builds what a real cut-and-cover portal
        /// has there, from the drawn ground (MeshHeight) and the cut itself (on the grids that cut holes): behind the
        /// headwall a covered way, two ashlar side walls under a roof of long lintels, the roof stepping up in whole
        /// courses to stay over the cut's rim until the hill closes over the passage (or the tunnel's middle, where the
        /// other portal's meets it), closed by an end wall; in front, wing walls along the road's edge over the cut
        /// beside it, stepping down in whole courses with a coping on each step. Where nothing is cut nothing is added.
        /// Its own random stream (from the portal's seed), so the arch, headwall and dressing are unchanged.
        /// </summary>
        static void Cutting(in TerrainSource source, Maker m, float ring, float row, float height, float halfW, float moss)
        {
            const float face = 0.12f, back = face - 0.7f, bury = 0.12f, wallT = 0.55f, grout = 0.035f, slice = 1f;
            var rng = new Random(m.P.Seed ^ 0x6A11E5u | 1u);
            var outerX = m.ArchX + ring;
            var capTop = height + 14f;
            // The covered way runs at most to the tunnel's middle (where the other end's meets it).
            ref var set = ref source.Caves.Value;
            ref var cave = ref set.Caves[m.P.Cave];
            var reach = math.min(30f, 0.5f * math.distance(set.Nodes[cave.FirstNode + 1].Position.xz,
                set.Nodes[cave.FirstNode + cave.NodeCount - 2].Position.xz));

            // The cut follows the passage, whose rough walls stand out up to about a metre past its radius, so the
            // masonry is that wide each side and the cut is sampled at a handful of points across it per slice (the
            // cave field is costly to sample, and a kit is built when its portal first comes near).
            var r = m.P.Radius;
            var g = math.min(math.max(outerX + 0.2f, r + 1.35f), halfW + 1.5f);
            float[] across = { 0f, -0.55f * r, 0.55f * r, -r, r, -(r + 0.7f), r + 0.7f };

            // ---- Behind the face: per metre back while the passage is cut open, its rim's height.
            var tops = new List<float>();
            var ends = new List<float>();
            var open = true; // still cut at the last slice (stopped at the middle, not closed by the hill)
            for (int k = 0; k < 40; k++)
            {
                float za = back - k * slice, zb = math.max(za - slice, -reach);
                if (za - zb < 0.05f)
                    break;
                var cut = false;
                for (int i = 0; i < across.Length && !cut; i++)
                    cut = m.Cut(source, across[i], za - 0.25f) || m.Cut(source, across[i], zb + 0.25f);
                if (!cut)
                {
                    open = false;
                    break;
                }
                var rim = float.MinValue;
                for (var z = za; z >= zb - 0.01f; z -= 0.5f)
                    for (var x = -g; x <= g + 0.01f; x += 0.5f)
                        rim = math.max(rim, m.Drawn(source, x, z, true));
                tops.Add(math.clamp(row * math.ceil((rim + bury) / row), height, capTop));
                ends.Add(zb);
            }
            if (tops.Count > 0)
                CoveredWay(source, m, ref rng, tops, ends, g, row, open, moss);

            // ---- In front of the face: the cut beside the road, each side, in slices out from the arch's plinths.
            const float plinth = 0.36f, step = 0.75f;
            for (int side = -1; side <= 1; side += 2)
            {
                var wTops = new List<float>();
                var wEnds = new List<float>();
                var w = g;
                for (int k = 0; k < 10; k++)
                {
                    float za = plinth + k * step, zb = za + step, zm = (za + zb) * 0.5f;
                    if (!m.Cut(source, side * (m.JambX + 0.3f), zm) && !m.Cut(source, side * r, zm) && !m.Cut(source, side * (r + 0.7f), zm))
                        break;
                    var rim = 0f;
                    for (var z = za; z <= zb + 0.01f; z += step * 0.5f)
                        for (var x = m.JambX; x <= w + 0.01f; x += 0.5f)
                            rim = math.max(rim, m.Drawn(source, side * x, z, true));
                    wTops.Add(math.clamp(row * math.ceil((rim + bury) / row), row, height));
                    wEnds.Add(zb);
                }
                if (wTops.Count > 0)
                    Wing(m, ref rng, side, wTops, wEnds, w, row, plinth, face);
            }

            void Block(Maker mk, float3 lo, float3 hi, float tint, float gap)
            {
                var half = math.max(0.02f, (hi - lo - gap) * 0.5f);
                mk.Add((lo + hi) * 0.5f, half, quaternion.identity, Stone, tint);
            }

            // The covered way: side walls in ashlar courses on a dark bed, from the ground up to each slice's roof,
            // lintels across the top, an end wall where the hill closes over it, moss on the roof.
            void CoveredWay(in TerrainSource src, Maker mk, ref Random r, List<float> tp, List<float> en, float gw, float rw, bool stillOpen, float mo)
            {
                var n = tp.Count;
                var bottoms = new float[n];
                var lowest = 0f;
                var highest = 0f;
                for (int k = 0; k < n; k++)
                {
                    float za = k == 0 ? back : en[k - 1], zb = en[k];
                    var low = float.MaxValue;
                    for (var z = za; z >= zb - 0.01f; z -= 0.5f)
                        for (int sd = -1; sd <= 1; sd += 2)
                            low = math.min(low, math.min(mk.Drawn(src, sd * gw, z, false), mk.Drawn(src, sd * (gw - wallT), z, false)));
                    bottoms[k] = math.clamp(rw * math.floor((low - 0.1f) / rw), -6f, tp[k]);
                    lowest = math.min(lowest, bottoms[k]);
                    highest = math.max(highest, tp[k]);
                }
                var end = en[n - 1];
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float xo = sd * gw, xi = sd * (gw - wallT);
                    float xa = math.min(xo, xi), xb = math.max(xo, xi);
                    for (int k = 0; k < n; k++)
                    {
                        float za = k == 0 ? back : en[k - 1], zb = en[k];
                        if (tp[k] - bottoms[k] < 0.05f)
                            continue;
                        // The bed on the inner side (no light through the joints), and the wall solid to walk against.
                        var bx = sd > 0 ? xa - 0.05f : xb - 0.02f;
                        mk.Span(new float3(bx, bottoms[k], zb), new float3(bx + 0.07f, tp[k], za), Stone, 0.4f);
                        mk.K.Solids.Add(new CaveRoomKit.Solid { Centre = mk.World(new float3((xa + xb) * 0.5f, (bottoms[k] + tp[k]) * 0.5f, (za + zb) * 0.5f)),
                            Half = new float3(wallT * 0.5f, (tp[k] - bottoms[k]) * 0.5f, (za - zb) * 0.5f), Rotation = mk.Frame });
                    }
                    for (var b0 = lowest; b0 < highest - 0.05f; b0 += rw)
                    {
                        var b1 = b0 + rw;
                        var a = back + r.NextFloat(0f, rw * 1.6f);
                        while (a > end)
                        {
                            var a1 = math.max(end, a - rw * r.NextFloat(1.4f, 2.6f));
                            var tint = mk.Jit();
                            // Split where the roof steps; pieces above a slice's roof, below its foot or wholly buried
                            // in the ground beside the wall are left out.
                            for (var piece = math.min(a, back); piece > a1 + 0.001f;)
                            {
                                var k = SliceAt(en, piece - 0.001f);
                                var lo = math.max(a1, en[k]);
                                var hiZ = piece;
                                piece = lo;
                                if (hiZ - lo < 0.08f || b1 > tp[k] + 0.001f || b0 < bottoms[k] - 0.001f)
                                    continue;
                                var ground = math.min(mk.Drawn(src, xo, lo, false), mk.Drawn(src, xo, hiZ, false));
                                if (b1 < ground - 0.05f)
                                    continue;
                                Block(mk, new float3(xa, b0, lo), new float3(xb, b1, hiZ), tint, grout);
                            }
                            a = a1;
                        }
                    }
                }
                // The roof: lintels across the way, breaking where it steps, solid to walk on (starting just behind the
                // headwall's coping so the two never share a face).
                var zr = back - 0.05f;
                while (zr - end > 0.05f)
                {
                    var k = SliceAt(en, zr - 0.001f);
                    var z1 = math.max(math.max(end, en[k]), zr - r.NextFloat(0.7f, 1.2f));
                    if (z1 - end < 0.2f)
                        z1 = end;
                    mk.Span(new float3(-gw - 0.1f, tp[k] - 0.2f, z1 + 0.012f), new float3(gw + 0.1f, tp[k] + CopingHeight, zr - 0.012f), Trim, mk.Jit(0.88f, 1.05f));
                    mk.K.Solids.Add(new CaveRoomKit.Solid { Centre = mk.World(new float3(0f, tp[k] + CopingHeight * 0.5f - 0.1f, (zr + z1) * 0.5f)),
                        Half = new float3(gw + 0.1f, CopingHeight * 0.5f + 0.1f, (zr - z1) * 0.5f), Rotation = mk.Frame });
                    zr = z1;
                }
                // A riser where the roof steps (and behind the headwall's coping when the first slice stands above it),
                // laid in courses under the higher slice's lintels, so the passage never shows through the step.
                for (int k = 0; k < n; k++)
                {
                    var z = k == 0 ? back : en[k - 1];
                    var below = k == 0 ? height : tp[k - 1];
                    float lowT = math.min(below, tp[k]), highT = math.max(below, tp[k]);
                    if (highT - lowT < 0.05f)
                        continue;
                    // On the higher slice's side of the boundary.
                    float z0 = tp[k] > below ? z - 0.35f : z, z1 = z0 + 0.35f;
                    for (var b0 = lowT - 0.2f; b0 < highT - 0.05f; b0 += rw)
                    {
                        var b1 = math.min(highT, b0 + rw);
                        var a = -gw - r.NextFloat(0f, rw * 1.6f);
                        while (a < gw)
                        {
                            var a1 = math.min(gw, a + rw * r.NextFloat(1.4f, 2.6f));
                            if (a1 - math.max(a, -gw) > 0.1f)
                                Block(mk, new float3(math.max(a, -gw), b0, z0), new float3(a1, b1, z1), mk.Jit(), grout);
                            a = a1;
                        }
                    }
                    mk.Span(new float3(-gw, lowT - 0.2f, z0 + 0.05f), new float3(gw, highT, z1 - 0.05f), Stone, 0.4f);
                }
                // The end wall where the hill closes over the passage (none at the tunnel's middle).
                if (!stillOpen)
                    mk.Span(new float3(-gw, bottoms[n - 1], end - 0.45f), new float3(gw, tp[n - 1], end), Stone, mk.Jit(0.55f, 0.75f));
                // Moss on the roof, in patches as thick as the portal's moss.
                var patches = (int)(mo * (back - end) * 1.2f);
                for (int i = 0; i < patches; i++)
                {
                    var z = r.NextFloat(end + 0.3f, back - 0.3f);
                    var x = r.NextFloat(-gw, gw);
                    var t = tp[SliceAt(en, z)] + CopingHeight;
                    float lx = r.NextFloat(0.3f, 1.1f) * (0.5f + mo), lz = r.NextFloat(0.2f, 0.6f);
                    mk.Span(new float3(x - lx * 0.5f, t, z - lz * 0.5f), new float3(x + lx * 0.5f, t + 0.03f, z + lz * 0.5f), Moss, mk.Jit(0.8f, 1.1f));
                }
            }

            // A wing wall along the road's edge from the arch's plinth out over the cut beside the road: blocks per
            // course per slice, stepping down in whole courses as the ground falls away from the face, a coping on each
            // step; its first slice reaches back to the headwall's face beyond the arch's stones.
            void Wing(Maker mk, ref Random r, int sd, List<float> tp, List<float> en, float w, float rw, float za0, float fc)
            {
                var outer = mk.ArchX + ring;
                for (int k = 0; k < tp.Count; k++)
                {
                    float za = k == 0 ? za0 : en[k - 1], zb = en[k];
                    float xi = sd * mk.JambX, xo = sd * w;
                    float xa = math.min(xi, xo), xb = math.max(xi, xo);
                    for (float b0 = 0f; b0 < tp[k] - 0.05f; b0 += rw)
                    {
                        var tint = mk.Jit();
                        // Wider than a block: two, the joint somewhere near the middle.
                        if (xb - xa > 1.3f)
                        {
                            var split = math.lerp(xa, xb, r.NextFloat(0.35f, 0.65f));
                            Block(mk, new float3(xa, b0, za), new float3(split, b0 + rw, zb), tint, grout);
                            Block(mk, new float3(split, b0, za), new float3(xb, b0 + rw, zb), tint * mk.Jit(0.94f, 1.04f), grout);
                        }
                        else
                            Block(mk, new float3(xa, b0, za), new float3(xb, b0 + rw, zb), tint, grout);
                        if (k == 0)
                        {
                            // Back to the headwall's face, beyond the arch's stones.
                            float ba = sd > 0 ? outer : xa, bb = sd > 0 ? xb : -outer;
                            if (bb - ba > 0.1f)
                                Block(mk, new float3(ba, b0, fc), new float3(bb, b0 + rw, za), tint, grout);
                        }
                    }
                    mk.Span(new float3(xa - 0.06f, tp[k], za - (k == 0 ? 0.24f : 0f)), new float3(xb + 0.06f, tp[k] + 0.18f, zb + 0.06f), Trim, mk.Jit(0.9f, 1.05f));
                    mk.K.Solids.Add(new CaveRoomKit.Solid { Centre = mk.World(new float3((xa + xb) * 0.5f, (tp[k] + 0.18f) * 0.5f, (za + zb) * 0.5f)),
                        Half = new float3((xb - xa) * 0.5f, (tp[k] + 0.18f) * 0.5f, (zb - za) * 0.5f), Rotation = mk.Frame });
                }
            }
        }

        /// <summary>The slice (of the covered way's, ends[k] its far end, running back into the hill) a local z is in.</summary>
        static int SliceAt(List<float> ends, float z)
        {
            var k = 0;
            while (k < ends.Count - 1 && z < ends[k])
                k++;
            return k;
        }

        /// <summary>Moss on the coping, the arch and the face; an ivy fringe; fallen stones and a rockfall by the jambs.</summary>
        static void Dressing(in TerrainSource source, Maker m, float top, float moss, bool ivy, bool rockfall, int fallen)
        {
            var outerX = m.ArchX + 0.7f;
            var halfW = outerX + 1.2f;
            // Moss along the coping's top, in patches whose count and length follow the portal's moss.
            var patches = (int)(moss * 9f);
            for (int k = 0; k < patches; k++)
            {
                var x = m.Rng.NextFloat(-halfW, halfW);
                var len = m.Rng.NextFloat(0.3f, 1.2f) * (0.5f + moss);
                m.Span(new float3(x - len * 0.5f, top, -0.5f + m.Rng.NextFloat(0f, 0.2f)), new float3(x + len * 0.5f, top + 0.035f, 0.2f + m.Rng.NextFloat(0f, 0.04f)), Moss, m.Jit(0.8f, 1.1f));
                // Some spill down the front a little.
                if (m.Rng.NextFloat() < moss)
                    m.Span(new float3(x - len * 0.4f, top - m.Rng.NextFloat(0.15f, 0.5f), 0.24f), new float3(x + len * 0.4f, top, 0.27f), Moss, m.Jit(0.75f, 1f));
            }
            // On the arch's extrados near the crown, and low on the face where rain splashes.
            var archMoss = (int)(moss * moss * 7f);
            for (int k = 0; k < archMoss; k++)
            {
                var a = m.Rng.NextFloat(0.35f, math.PI - 0.35f);
                var p = new float2(math.cos(a) * (m.ArchX + 0.68f), m.Spring + math.sin(a) * (m.ArchY + 0.68f));
                var half = new float3(m.Rng.NextFloat(0.12f, 0.3f), 0.025f, m.Rng.NextFloat(0.15f, 0.35f));
                m.Add(new float3(p.x, p.y, 0.05f), half, quaternion.RotateZ(a - math.PI * 0.5f), Moss, m.Jit(0.8f, 1.1f));
            }
            var footMoss = (int)(moss * 6f);
            for (int k = 0; k < footMoss; k++)
            {
                var side = m.Rng.NextBool() ? 1f : -1f;
                var x = side * m.Rng.NextFloat(m.JambX + 0.1f, halfW);
                var w = m.Rng.NextFloat(0.2f, 0.6f);
                var h = m.Rng.NextFloat(0.15f, 0.7f);
                m.Span(new float3(x - w * 0.5f, 0.05f, 0.13f), new float3(x + w * 0.5f, 0.05f + h, 0.16f), Moss, m.Jit(0.7f, 1f));
            }
            if (ivy)
            {
                // Strands hanging off the coping's front edge, leaves along them, thickest to one side.
                var centre = m.Rng.NextFloat(-halfW * 0.6f, halfW * 0.6f);
                var strands = m.Rng.NextInt(7, 15);
                for (int k = 0; k < strands; k++)
                {
                    var x = centre + m.Rng.NextFloat(-1.4f, 1.4f) * (1f + k * 0.05f);
                    if (math.abs(x) > halfW)
                        continue;
                    // Over the arch the strands stop at its stones; beside it they reach further down the face.
                    var lowest = math.abs(x) < outerX ? m.Spring + m.ArchY * math.sqrt(math.saturate(1f - x * x / (outerX * outerX))) + 0.75f : 0.6f;
                    var len = math.min(m.Rng.NextFloat(0.5f, 2.2f), top - lowest);
                    if (len < 0.25f)
                        continue;
                    var z = 0.3f + m.Rng.NextFloat(0f, 0.03f);
                    m.Span(new float3(x - 0.02f, top - len, z), new float3(x + 0.02f, top, z + 0.025f), Ivy, 0.7f);
                    for (var d = 0.1f; d < len; d += m.Rng.NextFloat(0.1f, 0.22f))
                    {
                        var leaf = m.Rng.NextFloat(0.06f, 0.11f);
                        var turn = quaternion.RotateZ(m.Rng.NextFloat(-0.8f, 0.8f));
                        m.Add(new float3(x + m.Rng.NextFloat(-0.07f, 0.07f), top - d, z + 0.03f), new float3(leaf, leaf * 0.8f, 0.01f), turn, Ivy, m.Jit(0.8f, 1.15f));
                    }
                }
            }
            // A fallen stone or two by the jambs, off the road, lying on the ground at odd angles.
            for (int k = 0; k < fallen; k++)
            {
                var side = m.Rng.NextBool() ? 1f : -1f;
                var x = side * m.Rng.NextFloat(m.JambX + 0.2f, halfW + 0.6f);
                var z = m.Rng.NextFloat(0.6f, 2.4f);
                var half = new float3(m.Rng.NextFloat(0.18f, 0.38f), m.Rng.NextFloat(0.12f, 0.22f), m.Rng.NextFloat(0.15f, 0.3f));
                var y = math.max(0f, m.Ground(source, x, z)) + half.y * 0.8f;
                var turn = math.mul(quaternion.RotateY(m.Rng.NextFloat(0f, math.PI)), quaternion.RotateZ(m.Rng.NextFloat(-0.25f, 0.25f)));
                m.Add(new float3(x, y, z), half, turn, Stone, m.Jit(0.8f, 0.98f), 0, math.cmax(half) > 0.3f);
            }
            if (rockfall)
            {
                // A small slip of rubble against the headwall to one side, bigger pieces at the bottom.
                var side = m.Rng.NextBool() ? 1f : -1f;
                var cx = side * m.Rng.NextFloat(m.JambX + 0.6f, halfW);
                var pieces = m.Rng.NextInt(6, 11);
                for (int k = 0; k < pieces; k++)
                {
                    var f = k / (float)pieces;
                    var size = math.lerp(0.42f, 0.14f, f) * m.Jit(0.8f, 1.2f);
                    var x = cx + m.Rng.NextFloat(-0.9f, 0.9f) * (1f - f * 0.6f);
                    var z = 0.35f + m.Rng.NextFloat(0f, 1.4f) * (1f - f * 0.7f);
                    var y = math.max(0f, m.Ground(source, x, z)) + size * 0.6f + f * 0.45f;
                    var turn = quaternion.Euler(m.Rng.NextFloat(-0.6f, 0.6f), m.Rng.NextFloat(0f, math.PI), m.Rng.NextFloat(-0.6f, 0.6f));
                    m.Add(new float3(x, y, z), new float3(size, size * m.Jit(0.55f, 0.85f), size * m.Jit(0.7f, 1f)), turn, Stone, m.Jit(0.72f, 0.95f), 0, size > 0.3f);
                }
            }
        }
    }
}
