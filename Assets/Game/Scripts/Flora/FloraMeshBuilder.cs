using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using noise = Unity.Mathematics.noise;
using float3 = Unity.Mathematics.float3;

namespace Punchy.Flora
{
    /// <summary>What part of a plant a vertex belongs to; the flora shader lights each differently.</summary>
    public enum FloraPart
    {
        Bark = 0,
        Foliage = 1,
        Petal = 2,
        Cactus = 3
    }

    /// <summary>
    /// Accumulates plant geometry: tubes (trunks, limbs, stems, cactus columns), noisy blobs (leaf clumps, pads),
    /// loose triangles (leaves, petals) and cones. Vertices carry albedo in the colour and, in UV0:
    /// x = how far the wind moves it, y = <see cref="FloraPart"/>, z = leaf flutter, w = baked ambient occlusion.
    /// </summary>
    public class FloraMeshBuilder
    {
        readonly List<Vector3> positions = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();   // linear
        readonly List<Vector4> data = new List<Vector4>();
        readonly List<int> indices = new List<int>();

        public int VertexCount => positions.Count;
        public int IndexCount => indices.Count;

        public void Clear()
        {
            positions.Clear();
            normals.Clear();
            colors.Clear();
            data.Clear();
            indices.Clear();
        }

        public int Vertex(Vector3 p, Vector3 n, Color c, FloraPart part, float ao, float flutter = -1f)
        {
            if (flutter < 0f)
                flutter = part == FloraPart.Foliage || part == FloraPart.Petal ? 1f : 0f;
            positions.Add(p);
            normals.Add(n);
            colors.Add(c);
            data.Add(new Vector4(0f, (float)part, flutter, Mathf.Clamp01(ao)));
            return positions.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        public Vector3 Position(int i) => positions[i];

        /// <summary>Smooth normals from the triangles added since the given counts.</summary>
        public void SmoothNormals(int firstVertex, int firstIndex)
        {
            for (int v = firstVertex; v < positions.Count; v++)
                normals[v] = Vector3.zero;
            for (int i = firstIndex; i < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                var n = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                if (a >= firstVertex) normals[a] += n;
                if (b >= firstVertex) normals[b] += n;
                if (c >= firstVertex) normals[c] += n;
            }
            for (int v = firstVertex; v < positions.Count; v++)
                normals[v] = normals[v].sqrMagnitude > 1e-12f ? normals[v].normalized : Vector3.up;
        }

        /// <summary>Bends normals since a vertex towards "away from a centre" (soft, canopy-like shading).</summary>
        public void BlendNormalsFrom(int firstVertex, Vector3 centre, Vector3 scale, float amount)
        {
            for (int v = firstVertex; v < positions.Count; v++)
            {
                var d = positions[v] - centre;
                var outward = new Vector3(d.x / scale.x, d.y / scale.y, d.z / scale.z).normalized;
                normals[v] = Vector3.Lerp(normals[v], outward, amount).normalized;
            }
        }

        /// <summary>Applies ambient occlusion since a vertex: ao(position, normal) multiplies what's there.</summary>
        public void Occlude(int firstVertex, Func<Vector3, Vector3, float> ao)
        {
            for (int v = firstVertex; v < positions.Count; v++)
            {
                var d = data[v];
                d.w = Mathf.Clamp01(d.w * ao(positions[v], normals[v]));
                data[v] = d;
            }
        }

        /// <summary>Recolours vertices since a vertex: tint(position, colour) returns the new colour.</summary>
        public void Recolor(int firstVertex, Func<Vector3, Color, Color> tint)
        {
            for (int v = firstVertex; v < positions.Count; v++)
                colors[v] = tint(positions[v], colors[v]);
        }

        // ------------------------------------------------------------------------------------------- primitives

        /// <summary>
        /// A tube along a path, one ring per point, optionally with a rounded cap at the end. Ribs (cacti) push the
        /// ring in and out around its circumference: radius × (1 − ribDepth × valley).
        /// </summary>
        public void Tube(IReadOnlyList<Vector3> path, IReadOnlyList<float> radii, int sides, Color color, FloraPart part,
                         bool cap = false, int ribs = 0, float ribDepth = 0f, float ao = 1f, Color? tipColor = null)
        {
            if (path.Count < 2 || sides < 3)
                return;
            var firstVertex = positions.Count;
            var firstIndex = indices.Count;

            // Parallel-transported frame so the tube doesn't twist.
            var tangent = (path[1] - path[0]).normalized;
            var side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            var rings = new List<(Vector3 centre, Vector3 side, Vector3 up, float radius)>();
            for (int i = 0; i < path.Count; i++)
            {
                var t = i == 0 ? path[1] - path[0] : i == path.Count - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1];
                t.Normalize();
                side = (side - t * Vector3.Dot(side, t)).normalized;
                rings.Add((path[i], side, Vector3.Cross(t, side), radii[i]));
                tangent = t;
            }
            if (cap)
            {
                // A dome: a few shrinking rings beyond the end, then a tip.
                var last = rings[rings.Count - 1];
                const int capRings = 3;
                for (int k = 1; k <= capRings; k++)
                {
                    var a = k / (float)(capRings + 1) * Mathf.PI * 0.5f;
                    rings.Add((last.centre + tangent * (last.radius * Mathf.Sin(a) * 0.9f), last.side, last.up, last.radius * Mathf.Cos(a)));
                }
            }

            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];
                var along = r / (float)(rings.Count - 1);
                var c = tipColor.HasValue ? Color.Lerp(color, tipColor.Value, Smooth(0.7f, 1f, along)) : color;
                for (int s = 0; s < sides; s++)
                {
                    var angle = s / (float)sides * Mathf.PI * 2f;
                    var valley = ribs > 0 ? 0.5f - 0.5f * Mathf.Cos(angle * ribs) : 0f;
                    var radius = ring.radius * (1f - ribDepth * valley);
                    var dir = ring.side * Mathf.Cos(angle) + ring.up * Mathf.Sin(angle);
                    Vertex(ring.centre + dir * radius, dir, c, part, ao * (1f - valley * ribDepth * 2.2f));
                }
            }
            for (int r = 0; r < rings.Count - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = firstVertex + r * sides + s, b = firstVertex + r * sides + (s + 1) % sides;
                int c = a + sides, d = b + sides;
                Triangle(a, b, c);
                Triangle(b, d, c);
            }
            if (cap)
            {
                var lastRing = firstVertex + (rings.Count - 1) * sides;
                var tipPoint = rings[rings.Count - 1].centre + tangent * rings[rings.Count - 1].radius * 0.35f;
                var tip = Vertex(tipPoint, tangent, tipColor ?? color, part, ao);
                for (int s = 0; s < sides; s++)
                    Triangle(lastRing + s, lastRing + (s + 1) % sides, tip);
            }
            SmoothNormals(firstVertex, firstIndex);
        }

        /// <summary>
        /// A noisy ellipsoid (leaf clump, cactus pad). Detail 0–3 subdivides an icosahedron (20 → 1280 triangles).
        /// Returns the first vertex so callers can adjust normals, colour or occlusion.
        /// </summary>
        public int Blob(Vector3 centre, Vector3 radii, Quaternion rotation, int detail, float bumpiness, float seed,
                        Color color, FloraPart part, float ao = 1f)
        {
            var (verts, tris) = Icosphere.Get(detail);
            var firstVertex = positions.Count;
            var firstIndex = indices.Count;
            var freq = 1.6f + detail * 0.4f;
            foreach (var u in verts)
            {
                var n = noise.snoise(new float3(u.x * freq + seed, u.y * freq - seed * 0.7f, u.z * freq + seed * 1.3f)) * 0.7f +
                        noise.snoise(new float3(u.x * freq * 2.3f - seed, u.y * freq * 2.3f, u.z * freq * 2.3f + seed)) * 0.3f;
                var local = Vector3.Scale(u, radii) * (1f + bumpiness * n);
                Vertex(centre + rotation * local, rotation * u, color, part, ao);
            }
            for (int i = 0; i < tris.Length; i += 3)
                Triangle(firstVertex + tris[i], firstVertex + tris[i + 1], firstVertex + tris[i + 2]);
            SmoothNormals(firstVertex, firstIndex);
            // Whatever the winding, normals face out of the clump.
            for (int v = 0; v < verts.Length; v++)
                if (Vector3.Dot(normals[firstVertex + v], rotation * verts[v]) < 0f)
                    normals[firstVertex + v] = -normals[firstVertex + v];
            return firstVertex;
        }

        /// <summary>
        /// Loose leaves scattered over an ellipsoid's surface, poking out to break up a clump's silhouette.
        /// Their normals point away from the clump centre, so they shade as part of it.
        /// </summary>
        public void Leaves(Vector3 centre, Vector3 radii, int count, float size, System.Random rng, Color a, Color b, FloraPart part = FloraPart.Foliage)
        {
            for (int i = 0; i < count; i++)
            {
                var u = RandomDirection(rng);
                if (u.y < -0.5f)
                    u.y = -u.y * 0.5f;   // fewer underneath, where nobody sees them
                var basePoint = centre + Vector3.Scale(u, radii) * (0.92f + (float)rng.NextDouble() * 0.15f);
                var outward = Vector3.Scale(u, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z)).normalized;
                var t1 = Vector3.Cross(outward, RandomDirection(rng)).normalized;
                var t2 = Vector3.Cross(outward, t1);
                var s = size * (0.6f + (float)rng.NextDouble() * 0.8f);
                var tip = basePoint + (t1 + outward * 0.8f).normalized * s;
                var c = Color.Lerp(a, b, (float)rng.NextDouble());
                var ao = 0.75f + 0.25f * Mathf.Clamp01(outward.y + 0.5f);
                int v0 = Vertex(basePoint - t2 * s * 0.3f, outward, c, part, ao);
                int v1 = Vertex(basePoint + t2 * s * 0.3f, outward, c, part, ao);
                int v2 = Vertex(tip, outward, c, part, ao);
                Triangle(v0, v1, v2);
            }
        }

        /// <summary>
        /// Surfaces between consecutive closed rings of equal size (conifer tiers, custom profiles). Points in each
        /// ring go anticlockwise seen from above when rings run downwards (normals then face out).
        /// </summary>
        public int Rings(IReadOnlyList<Vector3[]> rings, IReadOnlyList<Color[]> ringColors, IReadOnlyList<float[]> ao, FloraPart part)
        {
            var firstVertex = positions.Count;
            var firstIndex = indices.Count;
            var n = rings[0].Length;
            for (int k = 0; k < rings.Count; k++)
            for (int j = 0; j < n; j++)
                Vertex(rings[k][j], Vector3.up, ringColors[k][j], part, ao[k][j]);
            for (int k = 0; k < rings.Count - 1; k++)
            for (int j = 0; j < n; j++)
            {
                int a = firstVertex + k * n + j, b = firstVertex + k * n + (j + 1) % n;
                Triangle(a, b, a + n);
                Triangle(b, b + n, a + n);
            }
            SmoothNormals(firstVertex, firstIndex);
            return firstVertex;
        }

        /// <summary>A tiny octahedron (floret, berry, flower bud): 8 triangles.</summary>
        public void Diamond(Vector3 centre, float radius, Color color, FloraPart part, float ao = 1f)
        {
            var first = positions.Count;
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var a in axes)
                Vertex(centre + a * radius * (a.y != 0f ? 0.8f : 1f), a, color, part, ao);
            int[] faces = { 2, 4, 0, 2, 1, 4, 2, 5, 1, 2, 0, 5, 3, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0 };
            for (int i = 0; i < faces.Length; i += 3)
                Triangle(first + faces[i], first + faces[i + 1], first + faces[i + 2]);
        }

        /// <summary>A flat quad strip (leaf blade, palm frond side) through the given spine points, with widths.</summary>
        public void Strip(IReadOnlyList<Vector3> spine, IReadOnlyList<float> widths, Vector3 across, Color color, FloraPart part, float ao, Vector3 normal)
        {
            var first = positions.Count;
            for (int i = 0; i < spine.Count; i++)
            {
                Vertex(spine[i] - across * widths[i] * 0.5f, normal, color, part, ao);
                Vertex(spine[i] + across * widths[i] * 0.5f, normal, color, part, ao);
            }
            for (int i = 0; i < spine.Count - 1; i++)
            {
                int a = first + i * 2;
                Triangle(a, a + 2, a + 1);
                Triangle(a + 1, a + 2, a + 3);
            }
        }

        // ------------------------------------------------------------------------------------------- output

        /// <summary>
        /// Fills in wind sway (none at the base, rising with height and with distance from the stem) and returns a
        /// mesh. Rigid plants pass flexibility 0.
        /// </summary>
        public Mesh ToMesh(string name, float height, float width, float flexibility)
        {
            for (int v = 0; v < positions.Count; v++)
            {
                var p = positions[v];
                var up = Mathf.Clamp01(p.y / Mathf.Max(height, 0.01f));
                var outward = new Vector2(p.x, p.z).magnitude / Mathf.Max(width * 0.5f, 0.01f);
                var d = data[v];
                d.x = flexibility * Mathf.Pow(up, 1.6f) * (1f + 0.6f * outward);
                data[v] = d;
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, data);
            mesh.SetTriangles(indices, 0, true);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Bounding sphere around (0, centreY, 0).</summary>
        public void Bounds(out float centreY, out float radius)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in positions)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            centreY = (minY + maxY) * 0.5f;
            radius = 0f;
            foreach (var p in positions)
                radius = Mathf.Max(radius, (p - new Vector3(0f, centreY, 0f)).magnitude);
        }

        /// <summary>Hermite smoothstep, as in shaders (Mathf.SmoothStep is an interpolation, not this).</summary>
        public static float Smooth(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static Vector3 RandomDirection(System.Random rng)
        {
            var z = (float)rng.NextDouble() * 2f - 1f;
            var a = (float)rng.NextDouble() * Mathf.PI * 2f;
            var r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
    }

    /// <summary>Unit spheres made by subdividing an icosahedron, cached per level.</summary>
    static class Icosphere
    {
        static readonly List<(Vector3[] verts, int[] tris)> levels = new List<(Vector3[], int[])>();

        public static (Vector3[] verts, int[] tris) Get(int level)
        {
            level = Mathf.Clamp(level, 0, 3);
            if (levels.Count == 0)
                levels.Add(Base());
            while (levels.Count <= level)
                levels.Add(Subdivide(levels[levels.Count - 1]));
            return levels[level];
        }

        static (Vector3[], int[]) Base()
        {
            var t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Length; i++)
                v[i].Normalize();
            var f = new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            return (v, f);
        }

        static (Vector3[], int[]) Subdivide((Vector3[] verts, int[] tris) s)
        {
            var verts = new List<Vector3>(s.verts);
            var tris = new List<int>();
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!mid.TryGetValue(key, out var i))
                {
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    i = verts.Count - 1;
                    mid[key] = i;
                }
                return i;
            }
            for (int i = 0; i < s.tris.Length; i += 3)
            {
                int a = s.tris[i], b = s.tris[i + 1], c = s.tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            return (verts.ToArray(), tris.ToArray());
        }
    }
}
