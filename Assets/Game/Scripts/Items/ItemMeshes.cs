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
