using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, rocks and bones: ice arches, bones, moss, boulders, stone rows, spires and their runes. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A glacier ice arch facing +Z: a bow of ice from one foot to the other along X, thick and splayed at the feet,
        /// thinner over the top, its thickness and line broken up ring by ring so it reads as carved by melt rather
        /// than turned, with icicles under an ice one's crown. Drawn as glass (clear ice) when the species' finish is Glass, else as
        /// rock in its colour. The legs carry colliders, so the way under it stays open.
        /// </summary>
        static void IceArch(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * Mathf.Max(s.Spread, 0.5f) * 0.5f;
            var leg = height * Mathf.Max(s.Thickness, 0.05f);
            var part = s.Finish == FoliageFinish.Glass ? FloraPart.Glass : FloraPart.Bark;
            var lean = g.Range(-0.12f, 0.12f) * half;
            // Each variant its own shape (G-29: on an open glacier one model seen again and again gave itself away): a
            // wider or narrower span, a fuller or more pointed bow, one foot sunk deeper than the other; about a quarter
            // broken (two horns with the crown fallen in), and some of the rest with a smaller arch crossing them.
            half *= g.Range(0.8f, 1.25f);
            var profile = g.Range(0.6f, 1.5f);
            var footRise = -g.Range(0f, 0.15f) * height;   // only ever lower: a raised foot would stand off flat ground
            var broken = g.R() < 0.25f;
            var gap = g.Range(0.38f, 0.5f);
            var twin = !broken && g.R() < 0.2f;
            var twinTurn = g.Range(55f, 125f);
            g.Height = height;
            g.Width = half * 2f + leg * 2f;
            g.TrunkRadius = 0f;
            var rings = g.Lod == 0 ? 18 : g.Lod == 1 ? 11 : 7;
            var sides = g.Sides(9, 7, 5);
            var first = b.VertexCount;
            Bow(half, height, lean, 0f, 1f);
            if (twin)
            {
                // A smaller arch through the first, turned across it, its feet inside the big one's.
                var twinFirst = b.VertexCount;
                Bow(half * 0.62f, height * 0.7f, -lean * 0.5f, 0f, 1f);
                RotateFrom(b, twinFirst, Quaternion.Euler(0f, twinTurn, 0f));
            }
            // Darker underneath, where the bow shades itself.
            b.Occlude(first, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y * 0.5f + 0.6f)));
            if (g.Lod < 2 && part == FloraPart.Glass && !broken)
            {
                // Icicles under the crown (ice arches only; not rock ones).
                for (int k = 0; k < 6; k++)
                {
                    var t = g.Range(0.3f, 0.7f);
                    var top = Point(half, height, lean, t) + Vector3.down * leg * 0.45f;
                    var length = leg * g.Range(0.8f, 2.2f);
                    b.Tube(new List<Vector3> { top, top + Vector3.down * length }, new List<float> { leg * 0.16f, leg * 0.02f },
                           g.Sides(5, 4, 3), g.FoliageColor(), part);
                }
            }
            foreach (var x in new[] { -half, half })
                g.Colliders.Add(new Vector4(x, 0f, leg * 1.3f, height * 0.4f));

            // A point on a bow at t (0 one foot, 1 the other): a half ellipse raised to the profile's power, the far foot
            // footRise lower, leaning slightly one way (sin clamped at 0: sin(pi) is a hair below 0 in floats, and a negative
            // to a fractional power is NaN, which left every arch with NaN bounds).
            Vector3 Point(float w, float h, float l, float t)
            {
                var a = t * Mathf.PI;
                return new Vector3(-Mathf.Cos(a) * w, Mathf.Pow(Mathf.Max(Mathf.Sin(a), 0f), profile) * h - 0.3f + footRise * t, Mathf.Sin(a) * l);
            }

            // One bow of ice or rock (broken into two horns if this variant is), its feet carried on straight down into
            // the ground so they stay seated where it isn't quite level (F-44).
            void Bow(float w, float h, float l, float from, float to)
            {
                var path = new List<Vector3>();
                var radii = new List<float>();
                for (int i = 0; i <= rings; i++)
                {
                    var t = Mathf.Lerp(from, to, i / (float)rings);
                    var p = Point(w, h, l, t);
                    p += new Vector3(g.Range(-0.08f, 0.08f), g.Range(-0.06f, 0.06f), g.Range(-0.1f, 0.1f)) * leg;
                    path.Add(p);
                    var thick = Mathf.Lerp(1f, 0.55f, Mathf.Sin(t * Mathf.PI));
                    radii.Add(leg * thick * (i == 0 || i == rings ? 1.35f : 1f) * g.Range(0.88f, 1.1f));
                }
                var sink = 0.5f + h * 0.12f;
                if (!broken)
                {
                    path.Insert(0, path[0] + Vector3.down * sink);
                    radii.Insert(0, radii[0]);
                    path.Add(path[path.Count - 1] + Vector3.down * sink);
                    radii.Add(radii[radii.Count - 1]);
                    b.Tube(path, radii, sides, g.FoliageColor(), part);
                    return;
                }
                // Broken: each foot up to the gap, capped where it snapped.
                var cut = Mathf.RoundToInt(rings * gap);
                var left = path.GetRange(0, cut + 1);
                var leftRadii = radii.GetRange(0, cut + 1);
                left.Insert(0, left[0] + Vector3.down * sink);
                leftRadii.Insert(0, leftRadii[0]);
                b.Tube(left, leftRadii, sides, g.FoliageColor(), part, cap: true);
                var right = path.GetRange(rings - cut, cut + 1);
                var rightRadii = radii.GetRange(rings - cut, cut + 1);
                right.Reverse();
                rightRadii.Reverse();
                right.Insert(0, right[0] + Vector3.down * sink);
                rightRadii.Insert(0, rightRadii[0]);
                b.Tube(right, rightRadii, sides, g.FoliageColor(), part, cap: true);
            }
        }

        static void Bones(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var size = g.Range(s.Height);   // overall length of the skeleton
            g.Height = size * 0.45f;
            g.Width = size;
            g.TrunkRadius = 0f;

            var bone = s.Bark.linear;
            var aged = Color.Lerp(bone, bone * 0.75f, g.R());
            var heading = g.Flat(g.R() * 6.28f);
            var across = Vector3.Cross(Vector3.up, heading).normalized * (g.R() < 0.5f ? 1f : -1f);
            var r = size * s.Thickness;
            var sides = g.Sides(6, 4, 3);

            // The spine lies along the ground, half buried, with a gentle sideways bend.
            var bend = g.Range(-0.12f, 0.12f) * size;
            Vector3 Spine(float t) => heading * (t - 0.5f) * size + across * bend * Mathf.Sin(t * Mathf.PI) + Vector3.down * r * 0.4f;
            var spine = new List<Vector3>();
            var spineRadii = new List<float>();
            for (int i = 0; i <= 6; i++)
            {
                var t = i / 6f;
                spine.Add(Spine(t));
                spineRadii.Add(r * Mathf.Lerp(1f, 0.45f, t));
            }
            var first = b.VertexCount;
            b.Tube(spine, spineRadii, sides, aged, FloraPart.Bark, cap: true);
            if (g.Lod == 0)
                for (int i = 1; i < 14; i++)
                    b.Diamond(Spine(i / 14f) + Vector3.up * r * 0.8f, r * 1.4f * Mathf.Lerp(1f, 0.5f, i / 14f), aged, FloraPart.Bark);

            // Ribs arch up and over from the spine to the ground beside it (the animal lies on its side). Incomplete
            // skeletons are missing some, and some are broken off part-way.
            var ribCount = g.Count(s.Branches);
            for (int k = 0; k < ribCount; k++)
            {
                var t = Mathf.Lerp(0.22f, 0.62f, ribCount > 1 ? k / (ribCount - 1f) : 0.5f);
                var present = g.R() < 0.45f + 0.55f * s.Fullness;
                var reach = g.R() < s.Fullness ? 1f : g.Range(0.45f, 0.85f);
                var sizeAlong = Mathf.Sin(Mathf.Lerp(0.25f, 0.95f, (t - 0.22f) / 0.4f) * Mathf.PI);
                var ribWidth = size * 0.28f * (0.6f + 0.4f * sizeAlong);
                var ribHeight = size * 0.3f * (0.6f + 0.4f * sizeAlong);
                if (!present)
                    continue;
                var path = new List<Vector3>();
                var radii = new List<float>();
                var steps = g.Lod == 0 ? 8 : 4;
                var root = Spine(t);
                for (int j = 0; j <= steps; j++)
                {
                    var a = j / (float)steps * Mathf.PI * reach;
                    path.Add(root + across * ribWidth * (1f - Mathf.Cos(a)) * 0.5f + Vector3.up * ribHeight * Mathf.Sin(a) - heading * size * 0.03f * Mathf.Sin(a));
                    radii.Add(r * 0.55f * Mathf.Lerp(1f, 0.6f, j / (float)steps));
                }
                b.Tube(path, radii, Mathf.Max(3, sides - 2), aged, FloraPart.Bark, cap: true);
            }

            // Skull at the front, with dark eye sockets.
            var look = Quaternion.LookRotation((heading + Vector3.down * 0.2f).normalized, Vector3.up);
            var skull = Spine(0f) - heading * size * 0.06f + Vector3.up * r * 1.5f;
            var skullRadii = new Vector3(size * 0.06f, size * 0.05f, size * 0.1f);
            b.Blob(skull, skullRadii, look, g.Lod == 0 ? 1 : 0, 0.12f, g.Variant * 5.1f, aged, FloraPart.Bark);
            if (g.Lod < 2)
                for (int side = -1; side <= 1; side += 2)
                    b.Blob(skull + look * new Vector3(side * skullRadii.x * 0.75f, skullRadii.y * 0.35f, -skullRadii.z * 0.2f),
                           Vector3.one * skullRadii.x * 0.32f, look, 0, 0f, 0f, new Color(0.05f, 0.04f, 0.035f), FloraPart.Bark, 0.4f);

            // A few long bones scattered nearby.
            var scattered = 2 + (int)(g.R() * 3f);
            for (int i = 0; i < scattered; i++)
            {
                var at = g.Flat(g.R() * 6.28f) * size * g.Range(0.35f, 0.7f);
                var dir = g.Flat(g.R() * 6.28f);
                var length = size * g.Range(0.12f, 0.22f);
                if (g.Lod == 2)
                    continue;
                var a = at - dir * length * 0.5f + Vector3.up * r * 0.2f;
                var c = at + dir * length * 0.5f + Vector3.up * r * 0.2f;
                b.Tube(new List<Vector3> { a, c }, new List<float> { r * 0.45f, r * 0.45f }, 4, aged, FloraPart.Bark);
                b.Diamond(a, r * 0.9f, aged, FloraPart.Bark);
                b.Diamond(c, r * 0.9f, aged, FloraPart.Bark);
            }
            b.Occlude(first, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(p.y / Mathf.Max(size * 0.15f, 0.05f))) * Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(n.y + 0.5f)));
        }

        // ------------------------------------------------------------------------------------------- rocks

        /// <summary>Moss or lichen on upward-facing rock, in noisy patches (the species' accent).</summary>
        static void Moss(Grower g, int first)
        {
            if (!g.HasAccent)
                return;
            var moss = g.Accent;
            var amount = g.S.AccentAmount;
            var seed = g.Variant * 7.3f;
            g.B.Recolor(first, (p, n, c) =>
            {
                var patch = Mathf.PerlinNoise(p.x * 1.3f + seed, p.z * 1.3f + p.y * 0.7f) - 0.5f;
                var cover = FloraMeshBuilder.Smooth(0.75f - amount * 0.6f, 0.95f - amount * 0.6f, n.y + patch * 0.6f);
                return Color.Lerp(c, moss, cover);
            });
        }

        /// <summary>
        /// Rocks in five families, one per variant in turn: a rounded lump, a flat slab (sometimes two stacked), an
        /// L of a long low block and an upright one, a tall standing stone, and a monstrous split mass with chunks
        /// fallen off it. Built from rounded blocks (boxy, broadly lumpy blobs), with a few loose stones round the foot.
        /// </summary>
        static void Boulder(Grower g)
        {
            var s = g.S;
            var b = g.B;
            if (s.RiverSpot == RiverSpot.Narrows)
            {
                StoneRow(g);
                return;
            }
            var size = g.Range(s.Height);
            var family = g.Variant % 5;
            var detail = g.Sides(3, 2, 1);
            var first = b.VertexCount;
            var yaw = g.R() * 360f;

            void Block(Vector3 centre, Vector3 radii, Quaternion rotation, float sink = 0.3f)
            {
                var at = centre + Vector3.up * radii.y * (1f - sink);
                var colour = Color.Lerp(g.Bark, g.Bark * 0.8f, g.R());
                b.Blob(at, radii, rotation, detail, 0.14f, g.R() * 100f, colour, FloraPart.Bark, 1f, g.Range(0.35f, 0.6f), 0.55f);
                g.AddCollider(at, radii * 0.92f, rotation);
            }
            Quaternion Turn(float tilt = 8f) => Quaternion.Euler(g.Range(-tilt, tilt), yaw + g.Range(-20f, 20f), g.Range(-tilt, tilt));

            var w = size * s.Spread;
            switch (family)
            {
                case 0:   // lump
                    Block(Vector3.zero, new Vector3(w, size * 0.55f, w * g.Range(0.6f, 0.95f)), Turn());
                    break;
                case 1:   // slab, sometimes with a second resting on it
                    Block(Vector3.zero, new Vector3(w * 1.3f, size * 0.22f, w * g.Range(0.7f, 1f)), Turn(4f), 0.4f);
                    if (g.R() < 0.5f)
                        Block(new Vector3(g.Range(-0.3f, 0.3f) * w, size * 0.25f, g.Range(-0.3f, 0.3f) * w),
                              new Vector3(w * 0.75f, size * 0.17f, w * 0.55f), Turn(6f), 0.2f);
                    break;
                case 2:   // L: a long low block with an upright one at its end
                {
                    var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                    Block(-along * w * 0.35f, new Vector3(w * 1.1f, size * 0.3f, w * 0.5f), Quaternion.Euler(g.Range(-4f, 4f), yaw, g.Range(-4f, 4f)), 0.35f);
                    Block(along * w * 0.6f, new Vector3(w * 0.45f, size * 0.75f, w * 0.5f), Quaternion.Euler(g.Range(-6f, 6f), yaw + g.Range(-10f, 10f), g.Range(-6f, 6f)), 0.25f);
                    break;
                }
                case 3:   // standing
                    Block(Vector3.zero, new Vector3(w * 0.5f, size * 0.95f, w * g.Range(0.35f, 0.55f)), Turn(12f), 0.2f);
                    break;
                default:   // monstrous: a big mass split down the middle, chunks fallen from it
                {
                    var big = 1.9f;
                    var split = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * w * big * 0.32f;
                    Block(-split, new Vector3(w * big * 0.6f, size * big * 0.55f, w * big * 0.8f), Turn(5f), 0.3f);
                    Block(split, new Vector3(w * big * 0.5f, size * big * 0.48f, w * big * 0.75f), Turn(7f), 0.3f);
                    for (int k = 0; k < 2; k++)
                        Block(g.Flat(g.R() * 6.28f) * w * big * g.Range(0.9f, 1.2f), new Vector3(w * 0.45f, size * 0.35f, w * 0.4f), Turn(15f), 0.35f);
                    size *= big;
                    w *= big;
                    break;
                }
            }

            // Loose stones round the foot.
            var loose = Mathf.Max(0, g.Count(s.Branches) - 1);
            for (int k = 0; k < loose && g.Lod < 2; k++)
            {
                var r = w * g.Range(0.12f, 0.25f);
                var at = g.Flat(g.R() * 6.28f) * w * g.Range(0.9f, 1.4f) + Vector3.up * r * 0.3f;
                b.Blob(at, new Vector3(r, r * g.Range(0.45f, 0.75f), r * g.Range(0.6f, 1f)), Turn(20f), Mathf.Max(0, detail - 1), 0.12f, g.R() * 100f,
                       Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, 0.4f, 0.6f);
            }

            g.Height = size;
            g.Width = w * 2.4f;
            g.TrunkRadius = w * 0.45f;
            b.Occlude(first, (p, n) => GroundOcclusion(p, size * 0.25f) * Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(n.y + 0.6f)));
            Moss(g, first);
        }

        /// <summary>
        /// How long a row of stepping stones is, end stone to end stone, by its variant (W-65d: 4, 6, ... 14 m, so
        /// FloraPlacement can fit a row to the narrows it crosses and seat both ends on the bank; was always 12 m).
        /// </summary>
        public static float StoneRowLength(int variant) => 4f + 2f * variant;
        /// <summary>How many row lengths there are (the SteppingStone species needs this many variants).</summary>
        public const int StoneRowVariants = 6;
        /// <summary>The shortest row variant reaching across <paramref name="width"/> m of water (bank to bank), or -1
        /// when the water is narrower than 3 m or wider than the longest row.</summary>
        public static int StoneRowVariant(float width) =>
            width < 3f || width > StoneRowLength(StoneRowVariants - 1) ? -1 : Mathf.Max(0, Mathf.CeilToInt((width - 4f) / 2f));

        /// <summary>
        /// W-65b: stepping stones as one row across a shallow narrows (a single stone per draw left 10 m gaps, W-63).
        /// flat stones along X, about 0.85 m apart (W-65f), tops 4-12 cm over the origin (FloraPlacement seats the origin
        /// on the water surface) and reaching 0.9 m down to the bed. No colliders: they are stood on, through the
        /// support grid, not bumped into.
        /// </summary>
        static void StoneRow(Grower g)
        {
            var b = g.B;
            var first = b.VertexCount;
            var detail = g.Sides(3, 2, 1);
            // W-65d: the length by variant. W-65f: stones about 0.85 m apart (were 1.1): a stone's standable top (the part
            // under 57 degrees) is only about 0.36 m round its middle, so 1.1 m left 0.4 m holes in the support grid that
            // the player dropped through into the river; at 0.85 m the holes are narrower than the feet's sample spread.
            var length = StoneRowLength(g.Variant % StoneRowVariants);
            var n = Mathf.RoundToInt(length / 0.85f) + 1;
            var pitch = length / (n - 1);
            for (int k = 0; k < n; k++)
            {
                var x = -length * 0.5f + k * pitch + (k == 0 || k == n - 1 ? 0f : g.Range(-0.06f, 0.06f));
                var radii = new Vector3(g.Range(0.42f, 0.5f), 0.48f, g.Range(0.4f, 0.5f));
                var top = g.Range(0.04f, 0.12f);
                var at = new Vector3(x, top - radii.y, g.Range(-0.15f, 0.15f));
                var turn = Quaternion.Euler(g.Range(-3f, 3f), g.Range(0f, 360f), g.Range(-3f, 3f));
                b.Blob(at, radii, turn, detail, 0.1f, g.R() * 100f, Color.Lerp(g.Bark, g.Bark * 0.8f, g.R()), FloraPart.Bark, 1f, g.Range(0.35f, 0.6f), 0.55f);
            }
            g.Height = 0.2f;
            g.Width = length + 1f;
            g.TrunkRadius = 0f;
            b.Occlude(first, (p, n2) => Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n2.y + 0.6f)));
            Moss(g, first);
        }

        static void Spire(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Thickness;
            var blocks = g.Count(s.Branches);
            var capScale = 1f + s.TrunkFraction * 2.5f;
            var yaw = Quaternion.Euler(0f, g.R() * 360f, 0f);
            var lean = g.Flat(g.R() * 6.28f) * g.Range(0f, 0.05f);
            var detail = g.Sides(2, 2, 1);
            var bands = s.Foliage.linear;

            var first = b.VertexCount;
            var y = -radius * 0.2f;
            var offset = Vector3.zero;
            Vector3 runeCentre = default, runeRadii = default;
            var runeTurn = Quaternion.identity;
            for (int i = 0; i < blocks; i++)
            {
                var t = blocks > 1 ? i / (blocks - 1f) : 0f;
                var blockHeight = height / blocks * g.Range(0.8f, 1.2f);
                var r = radius * g.Range(0.85f, 1.1f);
                if (blocks > 1)
                    r *= Mathf.Lerp(1.35f, 1f, t);   // wider at the foot
                var cap = blocks > 1 && i == blocks - 1 && s.TrunkFraction > 0f;
                if (cap)
                {
                    r = radius * capScale;
                    blockHeight *= 0.6f;
                }
                var radii = new Vector3(r, blockHeight * 0.62f, r * s.Spread);
                var centre = offset + lean * y + Vector3.up * (y + blockHeight * 0.5f);
                var rotation = yaw * Quaternion.Euler(g.Range(-5f, 5f), g.Range(-25f, 25f), g.Range(-5f, 5f));
                var color = Color.Lerp(g.Bark, bands, g.R());
                b.Blob(centre, radii, rotation, detail, cap ? 0.1f : 0.16f, g.R() * 100f, color, FloraPart.Bark);
                if (i == 0)
                {
                    runeCentre = centre;
                    runeRadii = radii;
                    runeTurn = rotation;
                }
                y += blockHeight * 0.88f;
                offset += g.Flat(g.R() * 6.28f) * r * g.Range(0f, 0.12f);
            }
            g.Height = y;
            g.Width = radius * 2f * Mathf.Max(capScale, 1.35f);
            g.TrunkRadius = radius * (blocks > 1 ? 1.2f : Mathf.Lerp(s.Spread, 1f, 0.5f));
            b.Occlude(first, (p, n) => GroundOcclusion(p, radius * 2f) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(n.y + 0.7f)));
            if (s.RunePatterns > 0 && g.Lod < 2)
                SpireRunes(g, runeCentre, runeRadii, runeTurn);
            Moss(g, first);
        }

        // W-101: the glyph colours of a runed spire's patterns (FloraSpecies.RunePatterns), bright enough to read as
        // light at dusk without washing out to white at noon (the shader adds 1.6x albedo on top of the lit colour):
        // cyan, amber, violet, green, rose, ice, gold, teal.
        static readonly Color[] RuneColours =
        {
            new Color(0.04f, 0.55f, 0.75f), new Color(0.8f, 0.36f, 0.03f), new Color(0.42f, 0.12f, 0.8f), new Color(0.18f, 0.72f, 0.1f),
            new Color(0.8f, 0.12f, 0.35f), new Color(0.45f, 0.6f, 0.8f), new Color(0.75f, 0.6f, 0.05f), new Color(0.05f, 0.7f, 0.5f),
        };

        /// <summary>
        /// W-101: glowing glyphs carved into a runed spire's lowest block, on both broad faces. The variant's pattern
        /// (its run of FloraSpecies.Variants / RunePatterns, matching FloraPlacement.PatternModel) picks the colour and
        /// the glyph family, so a whole stone circle glows alike; the variant itself only nudges where the glyphs sit.
        /// Uses its own random so the stone's shape and moss draws stay as they were.
        /// </summary>
        static void SpireRunes(Grower g, Vector3 centre, Vector3 radii, Quaternion turn)
        {
            var s = g.S;
            var patterns = Mathf.Min(s.RunePatterns, Mathf.Max(1, s.Variants));
            var shapes = Mathf.Max(1, s.Variants / patterns);
            var pattern = Mathf.Min(g.Variant / shapes, patterns - 1);
            var glow = RuneColours[pattern % RuneColours.Length];
            var rng = new System.Random(s.Seed * 7919 + g.Variant * 104729 + 31);
            var w = 0.085f;
            for (int face = -1; face <= 1; face += 2)
            {
                var count = 2 + rng.Next(0, 2);
                for (int k = 0; k < count; k++)
                {
                    // Stacked down the face's middle, glyph squares about half the stone's width.
                    var gy = Mathf.Lerp(0.35f, -0.4f, count > 1 ? k / (count - 1f) : 0.5f) + (float)(rng.NextDouble() - 0.5) * 0.06f;
                    var gx = (float)(rng.NextDouble() - 0.5) * 0.15f;
                    var size = radii.x * 0.78f;
                    var cx = gx * radii.x;
                    var cy = gy * radii.y;
                    void Stroke(float a0, float b0, float a1, float b1)
                    {
                        var px = cx + (a0 + a1) * 0.5f * size;
                        var py = cy + (b0 + b1) * 0.5f * size;
                        var ex = px / radii.x;
                        var ey = py / radii.y;
                        var depth = radii.z * Mathf.Sqrt(Mathf.Max(0.05f, 1f - ex * ex - ey * ey));
                        var d = new Vector2(a1 - a0, b1 - b0);
                        var ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        var at = new Vector3(px, py, face * depth);
                        g.B.Box(centre + turn * at, new Vector3((d.magnitude + w) * size, w * size, radii.z * 0.36f),
                                turn * Quaternion.Euler(0f, 0f, ang), glow, FloraPart.Glow);
                    }
                    switch ((pattern + k) % 4)
                    {
                        case 0:   // a ringed cross
                            Stroke(-0.28f, 0f, 0.28f, 0f);
                            Stroke(0f, -0.28f, 0f, 0.28f);
                            for (int r = 0; r < 6; r++)
                            {
                                float t0 = r * Mathf.PI / 3f, t1 = (r + 1) * Mathf.PI / 3f;
                                Stroke(Mathf.Cos(t0) * 0.42f, Mathf.Sin(t0) * 0.42f, Mathf.Cos(t1) * 0.42f, Mathf.Sin(t1) * 0.42f);
                            }
                            break;
                        case 1:   // two stacked chevrons
                            for (int r = 0; r < 2; r++)
                            {
                                var vy = -0.25f + r * 0.32f;
                                Stroke(-0.36f, vy, 0f, vy + 0.22f);
                                Stroke(0f, vy + 0.22f, 0.36f, vy);
                            }
                            break;
                        case 2:   // a stave with branches, like a tree rune
                            Stroke(0f, -0.42f, 0f, 0.42f);
                            Stroke(0f, 0.12f, -0.3f, 0.38f);
                            Stroke(0f, 0.12f, 0.3f, 0.38f);
                            Stroke(0f, -0.18f, -0.26f, 0.02f);
                            Stroke(0f, -0.18f, 0.26f, 0.02f);
                            break;
                        default:  // an eye: a diamond with a dot
                            Stroke(-0.4f, 0f, 0f, 0.26f);
                            Stroke(0f, 0.26f, 0.4f, 0f);
                            Stroke(0.4f, 0f, 0f, -0.26f);
                            Stroke(0f, -0.26f, -0.4f, 0f);
                            Stroke(-0.06f, 0f, 0.06f, 0f);
                            break;
                    }
                }
            }
        }
    }
}
