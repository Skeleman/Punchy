using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// The small procedural meshes items are drawn with on the ground, and their inventory icons (P-23). Each item's mesh
    /// is built once, flat-shaded, with its origin at ground level. A mesh has one or two parts: part 0 is drawn in the
    /// item's Colour, part 1 in its Accent. An icon is drawn from the same triangles on the CPU, once per item, so it needs
    /// no camera and looks the same whatever the time of day or the graphics settings. Keyed by item name, so an item
    /// with no shape here is a plain block rather than the wrong thing.
    /// </summary>
    public static class ItemMeshes
    {
        const int IconSize = 64, Supersample = 2;
        static readonly Vector3 IconLight = new Vector3(-0.4f, 0.8f, -0.45f).normalized;
        static readonly Mesh[] meshes = new Mesh[ItemTable.Items.Length];
        static readonly Texture2D[] icons = new Texture2D[ItemTable.Items.Length];

        /// <summary>This item's mesh (made the first time it's asked for, kept for the session).</summary>
        public static Mesh Of(byte item)
        {
            if (meshes[item] != null)
                return meshes[item];
            var mesh = Build(ItemTable.Items[item].Name).ToMesh();
            mesh.name = "Item " + ItemTable.Items[item].Name;
            mesh.hideFlags = HideFlags.DontSave;
            return meshes[item] = mesh;
        }

        static Mesh bobber;

        /// <summary>P-33: a fishing line's float, a ball red below and white above, with a short stem; its origin at the
        /// waterline (part 0 red, part 1 white).</summary>
        public static Mesh Bobber()
        {
            if (bobber != null)
                return bobber;
            var k = new Kit();
            k.Blob(0, new Vector3(0f, -0.005f, 0f), new Vector3(0.045f, 0.035f, 0.045f), 1u, 4, 8);
            k.Blob(1, new Vector3(0f, 0.03f, 0f), new Vector3(0.04f, 0.03f, 0.04f), 2u, 4, 8);
            k.Tube(1, new Vector3(0f, 0.05f, 0f), new Vector3(0f, 0.1f, 0f), 0.006f, 0.004f, 4);
            bobber = k.ToMesh();
            bobber.name = "Bobber";
            bobber.hideFlags = HideFlags.DontSave;
            return bobber;
        }

        static Mesh splash;

        /// <summary>P-33 row 2: a bite's splash, a ring of eight droplets 0.12 m out on the water (scaled out as it grows).</summary>
        public static Mesh Splash()
        {
            if (splash != null)
                return splash;
            var k = new Kit();
            for (int i = 0; i < 8; i++)
            {
                var a = i * Mathf.PI * 2f / 8f;
                k.Blob(0, new Vector3(Mathf.Cos(a) * 0.12f, 0.02f + (i % 2) * 0.02f, Mathf.Sin(a) * 0.12f), new Vector3(0.022f, 0.03f, 0.022f), (uint)i, 3, 5);
            }
            splash = k.ToMesh();
            splash.name = "Splash";
            splash.hideFlags = HideFlags.DontSave;
            return splash;
        }

        /// <summary>This item's icon, 64 px with a clear background (made the first time it's asked for).</summary>
        public static Texture2D Icon(byte item)
        {
            if (icons[item] != null)
                return icons[item];
            var def = ItemTable.Items[item];
            return icons[item] = Build(def.Name).ToIcon(def.Colour, def.Accent);
        }

        static Kit Build(string name)
        {
            var k = new Kit();
            switch (name)
            {
                case "Stick": k.Tube(0, new Vector3(0f, 0.04f, -0.3f), new Vector3(0.03f, 0.05f, 0f), 0.028f, 0.024f); k.Tube(0, new Vector3(0.03f, 0.05f, 0f), new Vector3(-0.01f, 0.04f, 0.3f), 0.024f, 0.016f); k.Tube(0, new Vector3(0.03f, 0.05f, 0f), new Vector3(0.14f, 0.05f, 0.1f), 0.015f, 0.007f); break;
                case "Stone": k.Blob(0, new Vector3(0f, 0.08f, 0f), new Vector3(0.14f, 0.09f, 0.11f), 11u); break;
                case "Berry": Berries(k); break;
                case "Mushroom": k.Tube(1, Vector3.zero, new Vector3(0f, 0.12f, 0f), 0.04f, 0.03f); k.Dome(0, new Vector3(0f, 0.11f, 0f), 0.11f, 0.08f); break;
                case "Shell": Shell(k); break;
                case "Fish": Fish(k); break;
                case "Coin": k.Tube(0, new Vector3(0f, 0.005f, 0f), new Vector3(0f, 0.03f, 0f), 0.09f, 0.09f, 12); break;
                case "Key": Key(k); break;
                case "Torch": k.Tube(0, new Vector3(0f, 0.03f, -0.3f), new Vector3(0f, 0.03f, 0.2f), 0.025f, 0.035f); k.Blob(1, new Vector3(0f, 0.04f, 0.26f), new Vector3(0.055f, 0.05f, 0.09f), 5u); break;
                case "Staff": k.Tube(0, new Vector3(0f, 0.03f, -0.55f), new Vector3(0f, 0.03f, 0.5f), 0.02f, 0.028f); k.Blob(1, new Vector3(0f, 0.035f, 0.54f), new Vector3(0.05f, 0.05f, 0.05f), 7u); break;
                case "Hat": k.Dome(1, new Vector3(0f, 0.02f, 0f), 0.22f, 0.02f); k.Dome(0, new Vector3(0f, 0.03f, 0f), 0.12f, 0.13f); break;
                case "Club": k.Tube(0, new Vector3(0f, 0.035f, -0.32f), new Vector3(0f, 0.045f, 0.14f), 0.024f, 0.042f); k.Blob(0, new Vector3(0f, 0.055f, 0.22f), new Vector3(0.07f, 0.065f, 0.11f), 3u); k.Tube(1, new Vector3(0f, 0.035f, -0.32f), new Vector3(0f, 0.035f, -0.22f), 0.03f, 0.03f); break;
                case "Spear": k.Tube(0, new Vector3(0f, 0.025f, -0.9f), new Vector3(0f, 0.025f, 0.68f), 0.018f, 0.018f); k.Tube(1, new Vector3(0f, 0.025f, 0.68f), new Vector3(0f, 0.025f, 0.94f), 0.038f, 0.003f, 4); break;
                case "Sword": Sword(k); break;
                case "Bow": Bow(k); break;
                case "Axe": Axe(k); break;
                case "Rod": k.Tube(1, new Vector3(0f, 0.03f, -0.85f), new Vector3(0f, 0.03f, -0.55f), 0.022f, 0.02f, 6); k.Tube(0, new Vector3(0f, 0.03f, -0.55f), new Vector3(0f, 0.025f, 0.95f), 0.014f, 0.004f, 5); k.Tube(0, new Vector3(0.03f, 0.03f, -0.5f), new Vector3(0.03f, 0.03f, -0.44f), 0.028f, 0.028f, 6); break;
                case "Hammer": k.Tube(0, new Vector3(0f, 0.035f, -0.32f), new Vector3(0f, 0.035f, 0.3f), 0.02f, 0.024f, 6); k.Tube(1, new Vector3(-0.09f, 0.045f, 0.3f), new Vector3(0.09f, 0.045f, 0.3f), 0.045f, 0.045f, 6); k.Tube(0, new Vector3(0f, 0.035f, -0.34f), new Vector3(0f, 0.035f, -0.24f), 0.028f, 0.028f, 6); break;
                case "Arrow": k.Tube(0, new Vector3(0f, 0.012f, -0.32f), new Vector3(0f, 0.012f, 0.3f), 0.008f, 0.008f, 5); k.Tube(1, new Vector3(0f, 0.012f, 0.3f), new Vector3(0f, 0.012f, 0.37f), 0.016f, 0.002f, 4); k.Tube(1, new Vector3(0f, 0.012f, -0.32f), new Vector3(0f, 0.012f, -0.2f), 0.026f, 0.012f, 3); break;
                case "Cap": k.Dome(0, new Vector3(0f, 0.01f, 0f), 0.11f, 0.09f); k.Tube(1, new Vector3(-0.1f, 0.015f, 0.04f), new Vector3(0.1f, 0.015f, 0.04f), 0.012f, 0.012f, 4); break;
                case "Helm": k.Dome(0, new Vector3(0f, 0.01f, 0f), 0.13f, 0.14f); k.Tube(1, new Vector3(0f, 0.12f, 0.12f), new Vector3(0f, 0.02f, 0.135f), 0.014f, 0.01f, 4); k.Tube(1, new Vector3(-0.13f, 0.02f, 0f), new Vector3(0.13f, 0.02f, 0f), 0.014f, 0.014f, 4); break;
                case "Jerkin": k.Blob(0, new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.05f, 0.24f), 4u, 4, 8); k.Tube(1, new Vector3(-0.2f, 0.07f, -0.08f), new Vector3(0.2f, 0.07f, -0.08f), 0.015f, 0.015f, 4); break;
                case "Chainmail": k.Blob(0, new Vector3(0f, 0.05f, 0f), new Vector3(0.21f, 0.05f, 0.26f), 9u, 5, 9); k.Tube(1, new Vector3(-0.2f, 0.06f, 0.2f), new Vector3(0.2f, 0.06f, 0.2f), 0.02f, 0.02f, 5); break;
                case "Greaves": k.Tube(0, new Vector3(-0.07f, 0.04f, -0.2f), new Vector3(-0.07f, 0.04f, 0.18f), 0.045f, 0.04f); k.Tube(0, new Vector3(0.07f, 0.04f, -0.2f), new Vector3(0.07f, 0.04f, 0.18f), 0.045f, 0.04f); k.Tube(1, new Vector3(-0.12f, 0.04f, 0.1f), new Vector3(0.12f, 0.04f, 0.1f), 0.01f, 0.01f, 4); break;
                case "Boots": k.Tube(0, new Vector3(-0.07f, 0.12f, 0f), new Vector3(-0.07f, 0.03f, 0f), 0.04f, 0.045f); k.Tube(0, new Vector3(-0.07f, 0.03f, -0.02f), new Vector3(-0.07f, 0.03f, 0.13f), 0.04f, 0.035f); k.Tube(0, new Vector3(0.07f, 0.12f, 0f), new Vector3(0.07f, 0.03f, 0f), 0.04f, 0.045f); k.Tube(0, new Vector3(0.07f, 0.03f, -0.02f), new Vector3(0.07f, 0.03f, 0.13f), 0.04f, 0.035f); break;
                case "Tunic": k.Blob(0, new Vector3(0f, 0.04f, 0f), new Vector3(0.19f, 0.04f, 0.22f), 6u, 4, 8); k.Tube(1, new Vector3(-0.19f, 0.06f, -0.02f), new Vector3(0.19f, 0.06f, -0.02f), 0.012f, 0.012f, 4); break;
                case "Robe": k.Blob(0, new Vector3(0f, 0.04f, 0f), new Vector3(0.2f, 0.04f, 0.34f), 8u, 4, 8); k.Tube(1, new Vector3(-0.2f, 0.06f, 0.05f), new Vector3(0.2f, 0.06f, 0.05f), 0.014f, 0.014f, 4); break;
                case "Cloak": k.Blob(0, new Vector3(0f, 0.03f, 0f), new Vector3(0.28f, 0.03f, 0.3f), 12u, 3, 9); k.Tube(1, new Vector3(-0.1f, 0.05f, 0.27f), new Vector3(0.1f, 0.05f, 0.27f), 0.02f, 0.02f, 5); break;
                case "Scarf": k.Tube(0, new Vector3(-0.2f, 0.02f, -0.05f), new Vector3(0.2f, 0.02f, 0.02f), 0.03f, 0.03f, 5); k.Tube(1, new Vector3(0.2f, 0.02f, 0.02f), new Vector3(0.24f, 0.02f, 0.03f), 0.032f, 0.02f, 5); break;
                case "Hood": k.Dome(0, new Vector3(0f, 0.01f, 0f), 0.14f, 0.15f); k.Tube(0, new Vector3(0f, 0.04f, -0.12f), new Vector3(0f, 0.02f, -0.26f), 0.08f, 0.04f); break;
                case "Woolcap": k.Dome(0, new Vector3(0f, 0.01f, 0f), 0.11f, 0.1f); k.Blob(1, new Vector3(0f, 0.12f, 0f), new Vector3(0.04f, 0.04f, 0.04f), 4u, 4, 6); break;
                case "Brimhat": k.Dome(1, new Vector3(0f, 0.015f, 0f), 0.26f, 0.02f); k.Dome(0, new Vector3(0f, 0.03f, 0f), 0.11f, 0.12f); break;
                case "Pack": k.Blob(0, new Vector3(0f, 0.15f, 0f), new Vector3(0.17f, 0.15f, 0.11f), 0u, 5, 8); k.Blob(1, new Vector3(0f, 0.25f, 0.01f), new Vector3(0.16f, 0.05f, 0.11f), 0u, 4, 8); k.Tube(1, new Vector3(-0.12f, 0.02f, 0.1f), new Vector3(-0.12f, 0.25f, 0.1f), 0.018f, 0.018f, 4); k.Tube(1, new Vector3(0.12f, 0.02f, 0.1f), new Vector3(0.12f, 0.25f, 0.1f), 0.018f, 0.018f, 4); break;
                default: k.Blob(0, new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.1f, 0.1f), 3u); break;
            }
            return k;
        }

        static void Berries(Kit k)
        {
            Vector3[] at = { new Vector3(0f, 0.06f, 0f), new Vector3(0.08f, 0.05f, 0.03f), new Vector3(-0.07f, 0.05f, 0.05f), new Vector3(0.02f, 0.05f, -0.08f), new Vector3(0.01f, 0.12f, 0.02f) };
            foreach (var p in at)
                k.Blob(0, p, Vector3.one * 0.05f, 0u, 5, 7);
            k.Blob(1, new Vector3(-0.08f, 0.09f, -0.06f), new Vector3(0.07f, 0.012f, 0.04f), 0u, 3, 6);
        }

        static void Shell(Kit k)
        {
            const int ribs = 7;
            var hinge = new Vector3(0f, 0.015f, -0.11f);
            for (int i = 0; i < ribs; i++)
            {
                float a0 = Mathf.Lerp(-1.1f, 1.1f, i / (float)ribs), a1 = Mathf.Lerp(-1.1f, 1.1f, (i + 1) / (float)ribs);
                var mid = (a0 + a1) * 0.5f;
                var p0 = hinge + new Vector3(Mathf.Sin(a0) * 0.2f, 0.01f, Mathf.Cos(a0) * 0.2f);
                var p1 = hinge + new Vector3(Mathf.Sin(a1) * 0.2f, 0.01f, Mathf.Cos(a1) * 0.2f);
                var ridge = hinge + new Vector3(Mathf.Sin(mid) * 0.22f, 0.05f, Mathf.Cos(mid) * 0.22f);
                k.Tri(0, hinge, p0, ridge);
                k.Tri(0, hinge, ridge, p1);
                k.Tri(0, p0, ridge, p1);
            }
        }

        static void Fish(Kit k)
        {
            // (Lying on its side, nose along +z.)
            k.Blob(0, new Vector3(0f, 0.05f, 0.03f), new Vector3(0.1f, 0.045f, 0.2f), 0u, 6, 8);
            var root = new Vector3(0f, 0.05f, -0.15f);
            Vector3 tip0 = new Vector3(0.12f, 0.03f, -0.3f), tip1 = new Vector3(-0.12f, 0.03f, -0.3f);
            k.Tri(1, root, tip1, tip0);
            k.Tri(1, root, tip0, tip1);
            k.Blob(1, new Vector3(0.06f, 0.09f, 0.1f), new Vector3(0.02f, 0.008f, 0.02f), 0u, 3, 5);
        }

        static void Key(Kit k)
        {
            const int segments = 10;
            const float ring = 0.06f, y = 0.02f;
            var centre = new Vector3(0f, y, -0.12f);
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                k.Tube(0, centre + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * ring, centre + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * ring, 0.014f, 0.014f, 5);
            }
            k.Tube(0, centre + new Vector3(0f, 0f, ring), new Vector3(0f, y, 0.16f), 0.014f, 0.014f, 5);
            k.Tube(0, new Vector3(0f, y, 0.14f), new Vector3(0.06f, y, 0.14f), 0.012f, 0.012f, 4);
            k.Tube(0, new Vector3(0f, y, 0.1f), new Vector3(0.045f, y, 0.1f), 0.012f, 0.012f, 4);
        }

        /// <summary>A flat blade with a raised middle (a diamond across), a crossguard, a wrapped grip and a pommel.</summary>
        static void Sword(Kit k)
        {
            const float y = 0.03f, w = 0.032f, ridge = 0.009f;
            Vector3 l = new Vector3(-w, y, -0.24f), r = new Vector3(w, y, -0.24f), tip = new Vector3(0f, y, 0.42f);
            Vector3 top = new Vector3(0f, y + ridge, -0.24f), under = new Vector3(0f, y - ridge, -0.24f);
            Vector3 topTip = new Vector3(0f, y + ridge * 0.3f, 0.36f), underTip = new Vector3(0f, y - ridge * 0.3f, 0.36f);
            k.Tri(0, l, top, topTip); k.Tri(0, l, topTip, tip); k.Tri(0, top, r, topTip); k.Tri(0, topTip, r, tip);
            k.Tri(0, l, underTip, under); k.Tri(0, l, tip, underTip); k.Tri(0, under, underTip, r); k.Tri(0, underTip, tip, r);
            k.Tube(0, new Vector3(-0.09f, y, -0.25f), new Vector3(0.09f, y, -0.25f), 0.014f, 0.014f, 5);
            k.Tube(1, new Vector3(0f, y, -0.38f), new Vector3(0f, y, -0.26f), 0.017f, 0.017f, 6);
            k.Blob(0, new Vector3(0f, y, -0.395f), new Vector3(0.025f, 0.025f, 0.025f), 2u, 4, 6);
        }

        /// <summary>An axe lying flat: a haft, and at its head a flat iron blade widening out to the edge on one side
        /// (thicker by the haft, thin at the edge), a short poll on the other.</summary>
        static void Axe(Kit k)
        {
            const float y = 0.03f, t = 0.012f;
            k.Tube(0, new Vector3(0f, y, -0.34f), new Vector3(0f, y, 0.34f), 0.02f, 0.024f, 6);
            Vector3 n0 = new Vector3(0.02f, y + t, 0.21f), n1 = new Vector3(0.02f, y + t, 0.33f);
            Vector3 m0 = new Vector3(0.02f, y - t, 0.21f), m1 = new Vector3(0.02f, y - t, 0.33f);
            Vector3 e0 = new Vector3(0.16f, y, 0.15f), e1 = new Vector3(0.16f, y, 0.39f);
            k.Tri(1, n0, n1, e1); k.Tri(1, n0, e1, e0);
            k.Tri(1, m0, e1, m1); k.Tri(1, m0, e0, e1);
            k.Blob(1, new Vector3(-0.03f, y, 0.27f), new Vector3(0.035f, 0.022f, 0.045f), 2u, 4, 6);
        }

        /// <summary>A bow lying strung: its two limbs curving back from the grip, the string tip to tip.</summary>
        static void Bow(Kit k)
        {
            const int segments = 8;
            const float y = 0.03f, half = 0.6f, depth = 0.17f;
            var last = new Vector3(-depth * 0.35f, y, -half);
            for (int i = 1; i <= segments; i++)
            {
                var t = -1f + 2f * i / segments;
                var at = new Vector3(depth * (1f - t * t) - depth * 0.35f, y, half * t);
                var r = Mathf.Lerp(0.024f, 0.011f, Mathf.Abs(t));
                k.Tube(0, last, at, Mathf.Lerp(0.024f, 0.011f, Mathf.Abs(-1f + 2f * (i - 1) / segments)), r, 5);
                last = at;
            }
            k.Tube(1, new Vector3(depth * 0.6f, y, -0.07f), new Vector3(depth * 0.6f, y, 0.07f), 0.03f, 0.03f, 6);
            k.Tube(1, new Vector3(-depth * 0.35f, y, -half), new Vector3(-depth * 0.35f, y, half), 0.004f, 0.004f, 3);
        }

        /// <summary>Triangles being put together, by part, each with its own flat normal.</summary>
        class Kit
        {
            readonly List<Vector3> points = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int>[] parts = { new List<int>(), new List<int>() };

            public void Tri(int part, Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                var first = points.Count;
                points.Add(a);
                points.Add(b);
                points.Add(c);
                normals.Add(n);
                normals.Add(n);
                normals.Add(n);
                parts[part].Add(first);
                parts[part].Add(first + 1);
                parts[part].Add(first + 2);
            }

            void Quad(int part, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(part, a, b, c);
                Tri(part, a, c, d);
            }

            /// <summary>A tapered tube with closed ends.</summary>
            public void Tube(int part, Vector3 from, Vector3 to, float r0, float r1, int sides = 6)
            {
                var axis = (to - from).normalized;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var up = Vector3.Cross(side, axis);
                Vector3 Ring(Vector3 at, float r, int i)
                {
                    var a = i * Mathf.PI * 2f / sides;
                    return at + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r;
                }
                for (int i = 0; i < sides; i++)
                {
                    Quad(part, Ring(from, r0, i), Ring(from, r0, i + 1), Ring(to, r1, i + 1), Ring(to, r1, i));
                    Tri(part, from, Ring(from, r0, i), Ring(from, r0, i + 1));
                    Tri(part, to, Ring(to, r1, i + 1), Ring(to, r1, i));
                }
            }

            /// <summary>A rounded lump; a non-zero seed makes it uneven, like a stone.</summary>
            public void Blob(int part, Vector3 centre, Vector3 size, uint seed, int rings = 6, int segments = 9)
            {
                Vector3 Point(int ring, int segment)
                {
                    var v = Mathf.PI * ring / rings;
                    var u = Mathf.PI * 2f * segment / segments;
                    var p = new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u));
                    if (seed != 0u)
                    {
                        var h = (uint)(((segment % segments) * 73 + ring * 179) * 2654435761u + seed * 40503u);
                        p *= 0.82f + ((h >> 16) & 0xFF) / 255f * 0.36f;
                    }
                    return centre + Vector3.Scale(p, size);
                }
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < segments; s++)
                        Quad(part, Point(r, s), Point(r, s + 1), Point(r + 1, s + 1), Point(r + 1, s));
            }

            /// <summary>A cap: the top half of a flattened sphere with a closed underside.</summary>
            public void Dome(int part, Vector3 centre, float radius, float height)
            {
                const int rings = 4, segments = 10;
                Vector3 Point(int ring, int segment)
                {
                    var v = Mathf.PI * 0.5f * ring / rings;
                    var u = Mathf.PI * 2f * segment / segments;
                    return centre + new Vector3(Mathf.Sin(v) * Mathf.Cos(u) * radius, Mathf.Cos(v) * height, Mathf.Sin(v) * Mathf.Sin(u) * radius);
                }
                for (int r = 0; r < rings; r++)
                    for (int s = 0; s < segments; s++)
                        Quad(part, Point(r, s), Point(r, s + 1), Point(r + 1, s + 1), Point(r + 1, s));
                for (int s = 0; s < segments; s++)
                    Tri(part, centre, Point(rings, s), Point(rings, s + 1));
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh { subMeshCount = parts[1].Count > 0 ? 2 : 1 };
                mesh.SetVertices(points);
                mesh.SetNormals(normals);
                for (int i = 0; i < mesh.subMeshCount; i++)
                    mesh.SetTriangles(parts[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>The triangles drawn from above and in front, flat-lit, nearest wins, then averaged down.</summary>
            public Texture2D ToIcon(Color colour, Color accent)
            {
                var view = Quaternion.Euler(38f, 32f, 0f);
                var projected = new Vector3[points.Count];
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < points.Count; i++)
                {
                    projected[i] = Quaternion.Inverse(view) * points[i];
                    min = Vector3.Min(min, projected[i]);
                    max = Vector3.Max(max, projected[i]);
                }
                var size = IconSize * Supersample;
                var scale = (size - 8f) / Mathf.Max(max.x - min.x, max.y - min.y, 1e-4f);
                var offset = new Vector2((size - (max.x - min.x) * scale) * 0.5f - min.x * scale, (size - (max.y - min.y) * scale) * 0.5f - min.y * scale);
                var depth = new float[size * size];
                var pixels = new Color32[size * size];
                for (int i = 0; i < depth.Length; i++)
                    depth[i] = float.MaxValue;
                for (int part = 0; part < parts.Length; part++)
                {
                    var c = part == 0 ? colour : accent;
                    for (int t = 0; t < parts[part].Count; t += 3)
                    {
                        var first = parts[part][t];
                        var lit = 0.5f + 0.6f * Mathf.Max(0f, Vector3.Dot(normals[first], IconLight));
                        // (Back-facing triangles are hidden by the front ones, nearest wins.)
                        Fill(projected[first], projected[first + 1], projected[first + 2], scale, offset, size, depth, pixels,
                             new Color(Mathf.Min(c.r * lit, 1f), Mathf.Min(c.g * lit, 1f), Mathf.Min(c.b * lit, 1f), 1f));
                    }
                }
                var result = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var small = new Color32[IconSize * IconSize];
                for (int y = 0; y < IconSize; y++)
                    for (int x = 0; x < IconSize; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0;
                        for (int sy = 0; sy < Supersample; sy++)
                            for (int sx = 0; sx < Supersample; sx++)
                            {
                                var p = pixels[(y * Supersample + sy) * size + x * Supersample + sx];
                                r += p.r * p.a;
                                g += p.g * p.a;
                                b += p.b * p.a;
                                a += p.a;
                            }
                        small[y * IconSize + x] = a == 0 ? default : new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)(a / (Supersample * Supersample)));
                    }
                result.SetPixels32(small);
                result.Apply();
                return result;
            }

            static void Fill(Vector3 a, Vector3 b, Vector3 c, float scale, Vector2 offset, int size, float[] depth, Color32[] pixels, Color32 colour)
            {
                Vector2 pa = new Vector2(a.x, a.y) * scale + offset, pb = new Vector2(b.x, b.y) * scale + offset, pc = new Vector2(c.x, c.y) * scale + offset;
                var area = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
                if (Mathf.Abs(area) < 1e-5f)
                    return;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x, pc.x))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x, pc.x)));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y, pc.y))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y, pc.y)));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        var w0 = ((pb.x - p.x) * (pc.y - p.y) - (pb.y - p.y) * (pc.x - p.x)) / area;
                        var w1 = ((pc.x - p.x) * (pa.y - p.y) - (pc.y - p.y) * (pa.x - p.x)) / area;
                        var w2 = 1f - w0 - w1;
                        if (w0 < 0f || w1 < 0f || w2 < 0f)
                            continue;
                        var z = w0 * a.z + w1 * b.z + w2 * c.z;
                        if (z >= depth[y * size + x])
                            continue;
                        depth[y * size + x] = z;
                        pixels[y * size + x] = colour;
                    }
            }
        }
    }
}
