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
        }

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

            // W-138 row 3: the road's cutting is CutHalf wide either side up to the headwall (SettlementSampling.Height
            // stops levelling there), so the headwall spans it and buries its ends in the cutting's walls, rises
            // towards the hill over it, and wing walls retain the cutting's sides.
            var cutHalf = SettlementSampling.PortalHalf(r);
            Arch(m, ring, cracked, mark);
            var top = Headwall(source, m, ring, row, cutHalf);
            Dressing(source, m, top, moss, ivy, rockfall, fallen, cutHalf);
            Wing(source, m, -1f, row, top - 0.22f, cutHalf);
            Wing(source, m, 1f, row, top - 0.22f, cutHalf);
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
        /// coping's top.
        /// </summary>
        static float Headwall(in TerrainSource source, Maker m, float ring, float row, float cutHalf)
        {
            const float face = 0.12f, depth = 0.7f, grout = 0.035f;
            // W-138 row 3: past the cutting's walls and the wings on them, so both ends sit in the slope.
            var halfW = math.max(m.ArchX + ring + m.Rng.NextFloat(0.9f, 1.6f), cutHalf + WingThick + m.Rng.NextFloat(0.3f, 0.8f));
            var height = m.Spring + m.ArchY + ring * 1.3f + m.Rng.NextFloat(0.35f, 0.7f);
            height = row * math.ceil(height / row);
            // W-138 row 3: raised by whole courses towards the hill behind (up to HeadwallRaise), so the slope meets the
            // coping instead of rising behind a free-standing wall. Row 3b: up past the ground there, which in a steep
            // flank is the ledge (SettlementSampling.PortalLedge), so the coping hides the ledge's edge and the gap the
            // terrain leaves round the mouth.
            var behind = m.Ground(source, 0f, -2f) + 0.2f;
            if (behind > height)
                height += row * math.ceil(math.min(behind - height, HeadwallRaise) / row);
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
                m.Span(new float3(c + 0.015f, height, face - depth - 0.04f), new float3(c1 - 0.015f, height + 0.22f, face + 0.12f), Trim, m.Jit(0.9f, 1.05f));
                c = c1;
            }
            return height + 0.22f;

            void Block(float a0, float a1, float t0, float t1, float proud, float tint, float g)
            {
                var half = new float3(math.max(0.02f, (a1 - a0 - g) * 0.5f), math.max(0.02f, (t1 - t0 - g) * 0.5f), depth * 0.5f);
                m.Add(new float3((a0 + a1) * 0.5f, (t0 + t1) * 0.5f, face + proud - depth * 0.5f), half, quaternion.identity, Stone, tint);
            }
        }

        /// <summary>Moss on the coping, the arch and the face; an ivy fringe; fallen stones and a rockfall by the jambs.</summary>
        static void Dressing(in TerrainSource source, Maker m, float top, float moss, bool ivy, bool rockfall, int fallen, float cutHalf)
        {
            var outerX = m.ArchX + 0.7f;
            // (Inside the cutting's walls, W-138 row 3: past them the face is in the ground.)
            var halfW = math.min(outerX + 1.2f, cutHalf - 0.2f);
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
                var x = side * m.Rng.NextFloat(m.JambX + 0.2f, math.max(m.JambX + 0.3f, halfW - 0.3f));
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

        /// <summary>W-138 row 3: a wing wall's thickness (m), and the most the headwall rises over its arch for the hill behind.</summary>
        const float WingThick = SettlementSampling.PortalWall, HeadwallRaise = 5f;

        /// <summary>
        /// W-138 row 3: a wing wall on one side (side -1 left, 1 right) retaining the cutting's wall, from the headwall's
        /// face out along the road. It steps down in lengths of a metre or two to the ground outside it (sampled past the
        /// cutting's edge behind it), never above the headwall's coping, each length ashlar courses with a coping; it ends where the ground
        /// outside is under a course high or the cutting has widened into its banks.
        /// </summary>
        static void Wing(in TerrainSource source, Maker m, float side, float row, float cap, float cutHalf)
        {
            const float grout = 0.035f;
            float x0 = cutHalf, x1 = cutHalf + WingThick;
            var z = 0.12f;   // from the headwall's face
            var last = SettlementSampling.PortalWing + SettlementSampling.PortalTaper * 0.6f;
            var top = cap;
            while (z < last)
            {
                var z1 = math.min(last, z + m.Rng.NextFloat(1.1f, 1.8f));
                // The ground outside this length at its outer end (the lower one: the hill falls away from the portal),
                // sampled past the cutting's wall behind it.
                var ox = x1 + 0.1f + SettlementSampling.PortalCutBlend + 0.5f;
                var outside = math.min(m.Ground(source, side * ox, z1), m.Ground(source, side * ox, (z + z1) * 0.5f));
                var want = math.min(row * math.floor((outside + 0.1f) / row), top);
                if (want < row * 1.5f)
                    break;
                top = want;
                for (float b0 = 0f; b0 < top - 0.05f; b0 += row)
                {
                    var b1 = math.min(top, b0 + row);
                    var a = z - m.Rng.NextFloat(0f, row * 1.6f);
                    while (a < z1)
                    {
                        var a1 = a + row * m.Rng.NextFloat(1.4f, 2.6f);
                        float lo = math.max(a, z), hi = math.min(a1, z1);
                        a = a1;
                        if (hi - lo < 0.12f)
                            continue;
                        var proud = m.Rng.NextFloat() < 0.06f ? 0.04f : 0f;
                        var half = new float3(WingThick * 0.5f, math.max(0.02f, (b1 - b0 - grout) * 0.5f), math.max(0.02f, (hi - lo - grout) * 0.5f));
                        m.Add(new float3(side * ((x0 + x1) * 0.5f - proud), (b0 + b1) * 0.5f, (lo + hi) * 0.5f), half, quaternion.identity, Stone, m.Jit());
                    }
                }
                // The dark bed behind the joints, the coping (overhanging the cutting a little) and the solid.
                m.Span(new float3(side * (x0 + 0.04f), 0f, z), new float3(side * (x1 + 0.05f), top, z1), Stone, 0.4f);
                m.Span(new float3(side * (x0 - 0.1f), top, z + 0.015f), new float3(side * (x1 + 0.04f), top + 0.18f, z1 - 0.015f), Trim, m.Jit(0.9f, 1.05f));
                m.K.Solids.Add(new CaveRoomKit.Solid { Centre = m.World(new float3(side * (x0 + x1) * 0.5f, top * 0.5f, (z + z1) * 0.5f)), Half = new float3(WingThick * 0.5f, top * 0.5f + 0.09f, (z1 - z) * 0.5f), Rotation = m.Frame });
                z = z1;
            }
        }
    }
}
