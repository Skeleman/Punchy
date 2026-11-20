using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>W-178 r2: what a cave hanging is (CaveSystem.Hangings gives each kind its look and swing).</summary>
    public enum CaveHangingKind : byte
    {
        /// <summary>Glowing strands over a GlowFungi patch.</summary>
        Tendril,
        /// <summary>A curtain of tree roots through the ceiling of a shallow passage under forest or wet ground.</summary>
        Roots,
        /// <summary>A green curtain of vines just inside the mouth of a cave in wet country, where daylight still reaches.</summary>
        Vines,
        /// <summary>A sagging rope strung wall to wall across a mine passage, pinned at both ends.</summary>
        Rope
    }

    /// <summary>One hanging: anchor on the rock, End the far pin (ropes only), Length the rest length.</summary>
    public struct CaveHanging
    {
        public CaveHangingKind Kind;
        public float3 Anchor, End;
        public float Length, Random;

        public CaveHanging(CaveHangingKind kind, float3 anchor, float3 end, float length, float random)
        {
            Kind = kind;
            Anchor = anchor;
            End = end;
            Length = length;
            Random = random;
        }
    }

    /// <summary>
    /// W-178 r2: the set pieces hung across a cave's passages for players and creatures to push through: root curtains
    /// and vine drapes from the ceiling, ropes strung across mine passages. Placed once a cave's wall details are done,
    /// on hash stream 11, so no earlier draw (details, mushrooms, tendrils) moves. Swung by CaveSystem.Hangings.
    /// </summary>
    public partial class CaveWallBuild
    {
        bool hung;
        readonly System.Collections.Generic.List<int> band = new System.Collections.Generic.List<int>();

        void PlaceHangings(in TerrainSource source)
        {
            ref var set = ref source.Caves.Value;
            var h = CaveWallDetails.Hash(key, 11, 0, 0);
            // Curtains under forest or rain, never in hot rock or the cold: roots through the ceiling, and in the wettest
            // country some vine drapes just inside the mouth instead.
            if ((Site.Forest || Site.Rain > 0.35f) && !Site.Volcanic && Site.Cold < 0.5f && CaveWallDetails.U(h, 0) < 0.75f)
            {
                var count = 1 + (int)(CaveWallDetails.U(h, 1) * 3f);
                for (int c = 0; c < count; c++)
                {
                    var hc = CaveWallDetails.Hash(key, 11, c + 1, 0);
                    var vines = Site.Rain > 0.6f && CaveWallDetails.U(hc, 0) < 0.45f;
                    Curtain(source, ref set, vines ? CaveHangingKind.Vines : CaveHangingKind.Roots, hc);
                }
            }
            if (Site.Mine && CaveWallDetails.U(h, 2) < 0.85f)
            {
                var count = 1 + (int)(CaveWallDetails.U(h, 3) * 2f);
                for (int c = 0; c < count; c++)
                    Rope(source, ref set, CaveWallDetails.Hash(key, 11, c + 1, 1));
            }
        }

        /// <summary>A passage node within a depth band (not a room door or shaft), and the passage's way there.</summary>
        bool PassageNode(ref CaveSetBlob set, uint h, float near, float far, out float3 at, out float3 along)
        {
            at = along = default;
            // Drawn from the nodes in the band (a narrow band near the mouth may hold only one or two).
            band.Clear();
            for (int k = 0; k < nodes.Count; k++)
            {
                var kind = set.Kinds[nodes[k]];
                var depth = math.distance(set.Nodes[nodes[k]].Position, mouth);
                if (kind != CaveNodeKind.Room && kind != CaveNodeKind.Shaft && depth >= near && depth <= far)
                    band.Add(k);
            }
            for (int tries = 0; tries < 3 && band.Count > 0 && nodes.Count > 1; tries++)
            {
                var i = band[(int)(CaveWallDetails.U(h, 10 + tries) * band.Count) % band.Count];
                var n = nodes[i];
                // The passage runs to the nearer of the nodes either side in the list.
                var a = set.Nodes[nodes[math.max(i - 1, 0)]].Position;
                var b = set.Nodes[nodes[math.min(i + 1, nodes.Count - 1)]].Position;
                var p = set.Nodes[n].Position;
                var to = i == 0 || (i < nodes.Count - 1 && math.distancesq(b, p) < math.distancesq(a, p)) ? b - p : p - a;
                along = math.normalizesafe(new float3(to.x, 0f, to.z), new float3(1f, 0f, 0f));
                at = p;
                if (CaveSampling.Distance(ref set, at, out var owner) < -0.3f && owner == Cave)
                    return true;
            }
            return false;
        }

        /// <summary>A row of strands across the passage, tips at a walker's chest or head so they part as one goes by.</summary>
        void Curtain(in TerrainSource source, ref CaveSetBlob set, CaveHangingKind kind, uint h)
        {
            if (!PassageNode(ref set, h, kind == CaveHangingKind.Vines ? 2f : 5f, kind == CaveHangingKind.Vines ? 10f : 24f, out var at, out var along))
                return;
            if (!Cast(source, at, new float3(0f, 1f, 0f), 8f, out var ceiling, out var cn) || cn.y > -0.3f)
                return;
            if (!Cast(source, at, new float3(0f, -1f, 0f), 10f, out var floor, out _))
                return;
            var high = ceiling.y - floor.y;
            if (high < 2.2f || high > 6f)
                return;
            // The passage's width at head height, wall to wall.
            var across = new float3(-along.z, 0f, along.x);
            var head = new float3(at.x, floor.y + math.min(1.7f, high - 0.3f), at.z);
            var left = Cast(source, head, -across, 3f, out var l, out _) ? math.distance(l, head) : 3f;
            var right = Cast(source, head, across, 3f, out var r, out _) ? math.distance(r, head) : 3f;
            var strands = 5 + (int)(CaveWallDetails.U(h, 1) * 6f);
            for (int s = 0; s < strands; s++)
            {
                var hs = CaveWallDetails.Hash(h, 11, s, 2);
                var u = math.lerp(-left * 0.85f, right * 0.85f, (s + CaveWallDetails.U(hs, 0)) / strands);
                var from = head + across * u + along * (CaveWallDetails.U(hs, 1) - 0.5f) * 0.5f;
                if (!Cast(source, from, new float3(0f, 1f, 0f), 6f, out var q, out var qn) || qn.y > -0.3f)
                    continue;
                var gap = Cast(source, q - new float3(0f, 0.1f, 0f), new float3(0f, -1f, 0f), 8f, out var f, out _) ? q.y - f.y : 8f;
                var length = math.min(gap - 0.9f - CaveWallDetails.U(hs, 2) * 0.8f, 4.5f);
                if (length < 0.4f)
                    continue;
                Hangings.Add(new CaveHanging(kind, q + new float3(0f, 0.04f, 0f), default, length, CaveWallDetails.U(hs, 3)));
            }
        }

        /// <summary>A rope wall to wall just over head height, slack enough that its belly brushes a walker's head.</summary>
        void Rope(in TerrainSource source, ref CaveSetBlob set, uint h)
        {
            if (!PassageNode(ref set, h, 4f, 1e4f, out var at, out var along))
                return;
            if (!Cast(source, at, new float3(0f, -1f, 0f), 10f, out var floor, out _))
                return;
            var across = new float3(-along.z, 0f, along.x);
            var from = new float3(at.x, floor.y + 2.1f + CaveWallDetails.U(h, 1) * 0.3f, at.z);
            if (CaveSampling.Distance(ref set, from, out _) > -0.2f)
                return;
            if (!Cast(source, from, -across, 5f, out var a, out var an) || !Cast(source, from, across, 5f, out var b, out var bn))
                return;
            a += an * 0.03f;
            b += bn * 0.03f;
            var span = math.distance(a, b);
            if (span < 1.6f || span > 8f)
                return;
            Hangings.Add(new CaveHanging(CaveHangingKind.Rope, a, b, span * (1.08f + CaveWallDetails.U(h, 2) * 0.08f), CaveWallDetails.U(h, 3)));
        }
    }
}
