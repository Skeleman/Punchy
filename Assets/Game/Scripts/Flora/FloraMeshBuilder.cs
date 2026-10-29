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
        Cactus = 3,
        Metal = 4,
        Glow = 5,
        /// <summary>A window pane: dark glass by day, lit in its colour at night (some go out late; see Flora.shader).</summary>
        Window = 6,
        /// <summary>Glassy crystal (FoliageFinish.Glass; see Flora.shader).</summary>
        Glass = 7,
        /// <summary>Honeycomb wax: glossy, with faint hexagonal cells up close (wild beehives; see Flora.shader).</summary>
        Comb = 8,
        /// <summary>A bark face's eyes: a faint glow that blinks now and then (see Flora.shader).</summary>
        Eye = 9,
        /// <summary>Cloth (banners, awnings, hide flaps) that waves in the wind; how free each vertex is rides on the part
        /// (see FloraMeshBuilder.Cloth, Flora.hlsl).</summary>
        Cloth = 10,
        /// <summary>An electric arc: bright, crackling in bursts and jumping about (Flora.hlsl, Flora.shader).</summary>
        Arc = 11
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

        /// <summary>What foliage and petals are drawn as (a species' finish: metallic or glowing leaves).</summary>
        public FloraPart FoliageAs = FloraPart.Foliage, PetalAs = FloraPart.Petal;
        /// <summary>The species' glow shimmer (FloraSpecies.GlowPulse), stored on its glowing parts' vertices.</summary>
        public float GlowPulse;
        /// <summary>Bark drawn with glowing seams (FloraSpecies.GlowingSeams): marked on its part as Bark + 0.25.</summary>
        public bool BarkSeams;
        /// <summary>Bark veined with cool flowing light (FloraSpecies.GlowingVeins): marked as Bark + 0.4.</summary>
        public bool BarkVeins;
        /// <summary>Rock banded like the canyon walls (FloraSpecies.RockStrata): marked as Bark + 0.08.</summary>
        public bool BarkStrata;

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
            if (part == FloraPart.Foliage) part = FoliageAs;
            else if (part == FloraPart.Petal) part = PetalAs;
            positions.Add(p);
            normals.Add(n);
            colors.Add(c);
            // Glowing parts carry their shimmer as a fraction on the part (the shader rounds the part).
            data.Add(new Vector4(0f, (float)part + (part == FloraPart.Glow ? Mathf.Clamp01(GlowPulse) * 0.4f : part == FloraPart.Bark && BarkVeins ? 0.4f : part == FloraPart.Bark && BarkSeams ? 0.25f : part == FloraPart.Bark && BarkStrata ? 0.08f : 0f),
                                 flutter, Mathf.Clamp01(ao)));
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

        /// <summary>Moves (and turns the normals of) vertices added since <paramref name="firstVertex"/>.</summary>
        public void Transform(int firstVertex, Func<Vector3, Vector3> position, Func<Vector3, Vector3> normal)
        {
            for (int v = firstVertex; v < positions.Count; v++)
            {
                positions[v] = position(positions[v]);
                normals[v] = normal(normals[v]);
            }
        }

        /// <summary>Recolours vertices by position and normal (moss on the tops of rocks).</summary>
        public void Recolor(int firstVertex, Func<Vector3, Vector3, Color, Color> tint)
        {
            for (int v = firstVertex; v < positions.Count; v++)
                colors[v] = tint(positions[v], normals[v], colors[v]);
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
        /// <param name="boxiness">0 = ellipsoid, towards 1 = a rounded block (rocks).</param>
        /// <param name="noiseScale">Scales the bump frequency: below 1 gives broad lumps rather than spikes.</param>
        public int Blob(Vector3 centre, Vector3 radii, Quaternion rotation, int detail, float bumpiness, float seed,
                        Color color, FloraPart part, float ao = 1f, float boxiness = 0f, float noiseScale = 1f)
        {
            var (verts, tris) = Icosphere.Get(detail);
            var firstVertex = positions.Count;
            var firstIndex = indices.Count;
            var freq = (1.6f + detail * 0.4f) * noiseScale;
            foreach (var u in verts)
            {
                var n = noise.snoise(new float3(u.x * freq + seed, u.y * freq - seed * 0.7f, u.z * freq + seed * 1.3f)) * 0.7f +
                        noise.snoise(new float3(u.x * freq * 2.3f - seed, u.y * freq * 2.3f, u.z * freq * 2.3f + seed)) * 0.3f;
                // Pushed out towards the enclosing cube, so the shape has flat-ish faces and rounded edges.
                var shape = boxiness > 0f ? Vector3.Lerp(u, u / Mathf.Max(Mathf.Abs(u.x), Mathf.Max(Mathf.Abs(u.y), Mathf.Abs(u.z))), boxiness) : u;
                var local = Vector3.Scale(shape, radii) * (1f + bumpiness * n);
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

        /// <summary>A flat-shaded box (planks, walls, roof slabs, counters), centred and rotated.</summary>
        public void Box(Vector3 centre, Vector3 size, Quaternion rotation, Color color, FloraPart part, float ao = 1f)
        {
            var half = size * 0.5f;
            Vector3[] faces = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in faces)
            {
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(n, u);
                var first = positions.Count;
                for (int k = 0; k < 4; k++)
                {
                    var su = k == 0 || k == 3 ? -1f : 1f;
                    var sw = k < 2 ? -1f : 1f;
                    var local = Vector3.Scale(n + u * su + w * sw, half);
                    Vertex(centre + rotation * local, rotation * n, color, part, n.y < -0.5f ? ao * 0.6f : ao);
                }
                Triangle(first, first + 1, first + 2);
                Triangle(first, first + 2, first + 3);
            }
        }

        /// <summary>A box spanning from one point to another (a beam, a rail, a sloping roof slab).</summary>
        public void Beam(Vector3 from, Vector3 to, float width, float thickness, Color color, FloraPart part, float ao = 1f)
        {
            var along = to - from;
            if (along.sqrMagnitude < 1e-6f)
                return;
            var rotation = Quaternion.LookRotation(along.normalized, Mathf.Abs(along.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
            Box((from + to) * 0.5f, new Vector3(width, thickness, along.magnitude), rotation, color, part, ao);
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

        /// <summary>
        /// A foundation skirt: every vertex at or below ground level (y &lt;= 0) drops by depth, so walls, plinths, posts
        /// and feet carry on into the ground in their own material and no slope under the footprint leaves a gap.
        /// Builders keep anything that must stay put (rails, bands, steps) above y = 0.
        /// </summary>
        public void Skirt(float depth)
        {
            for (int i = 0; i < positions.Count; i++)
                if (positions[i].y <= 0.001f)
                    positions[i] -= new Vector3(0f, depth, 0f);
        }

        /// <summary>
        /// The walkable tops of what's been built (see PropSupport): over the footprint of its upward-facing triangles
        /// (those tilted more than about 57 degrees from level don't count: walls, steep faces, undersides), per cell the
        /// heights of every surface there, merged where within 0.4 m and kept highest first, up to
        /// SupportGrids.Layers (surfaces narrower than a cell, a finial or a thin beam, aren't something to stand on and
        /// fall between the samples); and separately the water surface (triangles of the parts <paramref name="water"/> picks).
        /// Parts <paramref name="solid"/> rejects (leaves, petals, glow) are ignored. Checks itself against the
        /// triangles at random points and reports the worst gap. Null if there's nothing to stand on.
        /// </summary>
        public SupportGridData SupportGrid(Func<FloraPart, bool> solid, Func<FloraPart, bool> water)
        {
            var up = new List<int>();
            var wet = new List<int>();
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int t = 0; t + 2 < indices.Count; t += 3)
            {
                var a = positions[indices[t]]; var b = positions[indices[t + 1]]; var c = positions[indices[t + 2]];
                // Facing up (by its vertices' outward normals, so undersides don't count) and near level.
                var up3 = (normals[indices[t]] + normals[indices[t + 1]] + normals[indices[t + 2]]).normalized;
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-10f || up3.y < 0.55f)
                    continue;
                var part = (FloraPart)Mathf.RoundToInt(data[indices[t]].y);
                if (water(part))
                    wet.Add(t);
                else if (solid(part))
                    up.Add(t);
                else
                    continue;
                foreach (var p in new[] { a, b, c })
                {
                    min = Vector2.Min(min, new Vector2(p.x, p.z));
                    max = Vector2.Max(max, new Vector2(p.x, p.z));
                }
            }
            if (up.Count == 0)
                return null;
            var extent = Mathf.Max(max.x - min.x, max.y - min.y);
            var cell = Mathf.Max(extent / 40f, 0.15f);
            var res = new Vector2Int(Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / cell) + 1, 1, 48), Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / cell) + 1, 1, 48));
            cell = Mathf.Max(cell, Mathf.Max((max.x - min.x) / Mathf.Max(res.x - 1, 1), (max.y - min.y) / Mathf.Max(res.y - 1, 1)));
            var heights = new List<float>[res.x * res.y];
            var waterTop = new float[res.x * res.y];
            for (int i = 0; i < waterTop.Length; i++)
                waterTop[i] = float.NaN;

            void Rasterise(int t, Action<int, float> add)
            {
                var a = positions[indices[t]]; var b = positions[indices[t + 1]]; var c = positions[indices[t + 2]];
                var lo = Vector2.Min(Vector2.Min(new Vector2(a.x, a.z), new Vector2(b.x, b.z)), new Vector2(c.x, c.z));
                var hi = Vector2.Max(Vector2.Max(new Vector2(a.x, a.z), new Vector2(b.x, b.z)), new Vector2(c.x, c.z));
                var c0 = new Vector2Int(Mathf.Max(0, Mathf.FloorToInt((lo.x - min.x) / cell)), Mathf.Max(0, Mathf.FloorToInt((lo.y - min.y) / cell)));
                var c1 = new Vector2Int(Mathf.Min(res.x - 1, Mathf.FloorToInt((hi.x - min.x) / cell)), Mathf.Min(res.y - 1, Mathf.FloorToInt((hi.y - min.y) / cell)));
                for (int z = c0.y; z <= c1.y; z++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    var q = new Vector2(min.x + (x + 0.5f) * cell, min.y + (z + 0.5f) * cell);
                    if (HeightOn(a, b, c, q, out var y)) add(z * res.x + x, y);
                }
            }
            foreach (var t in up)
                Rasterise(t, (i, y) => (heights[i] ??= new List<float>()).Add(y));
            foreach (var t in wet)
                Rasterise(t, (i, y) => waterTop[i] = float.IsNaN(waterTop[i]) ? y : Mathf.Max(waterTop[i], y));

            const int layers = 3;
            var values = new float[res.x * res.y * layers];
            for (int i = 0; i < values.Length; i++)
                values[i] = float.NaN;
            for (int i = 0; i < heights.Length; i++)
            {
                if (heights[i] == null)
                    continue;
                heights[i].Sort((p, q) => q.CompareTo(p));
                var kept = 0;
                var last = float.MaxValue;
                foreach (var y in heights[i])
                {
                    if (last - y < 0.4f)
                        continue;   // the same surface (the highest of the cluster is kept)
                    values[i * layers + kept++] = y;
                    last = y;
                    if (kept == layers)
                        break;
                }
            }

            // Self-check: the true top at random points against the cell's top layer, away from edges (where the true
            // top jumps within half a cell, the error is the cell size's aliasing, bounded by it, not a wrong height).
            var rng = new System.Random(indices.Count);
            var worst = 0f;
            float Truth(Vector2 at)
            {
                var best = float.MinValue;
                foreach (var t in up)
                    if (HeightOn(positions[indices[t]], positions[indices[t + 1]], positions[indices[t + 2]], at, out var y))
                        best = Mathf.Max(best, y);
                return best;
            }
            for (int k = 0; k < 64; k++)
            {
                var q = new Vector2(Mathf.Lerp(min.x, max.x, (float)rng.NextDouble()), Mathf.Lerp(min.y, max.y, (float)rng.NextDouble()));
                var truth = Truth(q);
                if (truth == float.MinValue)
                    continue;
                var edge = false;
                // Round the point out to its cell's centre and corners (up to 0.71 cells away).
                for (int e = 0; e < 8 && !edge; e++)
                {
                    var a8 = e * Mathf.PI * 0.25f;
                    var around = Truth(q + new Vector2(Mathf.Cos(a8), Mathf.Sin(a8)) * cell * 0.75f);
                    edge = around == float.MinValue || Mathf.Abs(around - truth) > 0.3f;
                }
                if (edge)
                    continue;
                var x = Mathf.Clamp(Mathf.FloorToInt((q.x - min.x) / cell), 0, res.x - 1);
                var z = Mathf.Clamp(Mathf.FloorToInt((q.y - min.y) / cell), 0, res.y - 1);
                var top = values[(z * res.x + x) * layers];
                if (!float.IsNaN(top))
                    worst = Mathf.Max(worst, Mathf.Abs(top - truth));
            }
            return new SupportGridData
            {
                Min = min, Cell = cell, Res = res, Values = values, Water = wet.Count > 0 ? waterTop : null,
                Reach = Mathf.Max(Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)), Mathf.Max(Mathf.Abs(min.y), Mathf.Abs(max.y))) * 1.42f,
                Error = worst
            };
        }

        /// <summary>The triangle's height over a point in xz, if the point is inside it.</summary>
        static bool HeightOn(Vector3 a, Vector3 b, Vector3 c, Vector2 q, out float y)
        {
            y = 0f;
            var v0 = new Vector2(b.x - a.x, b.z - a.z); var v1 = new Vector2(c.x - a.x, c.z - a.z); var v2 = new Vector2(q.x - a.x, q.y - a.z);
            var den = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(den) < 1e-9f)
                return false;
            var v = (v2.x * v1.y - v1.x * v2.y) / den;
            var w = (v0.x * v2.y - v2.x * v0.y) / den;
            if (v < -1e-4f || w < -1e-4f || v + w > 1.0001f)
                return false;
            y = a.y + v * (b.y - a.y) + w * (c.y - a.y);
            return true;
        }

        /// <summary>
        /// Gives the vertices from <paramref name="firstVertex"/> on a motion the vertex shader plays from the shared
        /// clock (carried in the flutter channel, Flora.hlsl): a slow breath along the normal (breath > 0, its size in
        /// metres) or, with <paramref name="twitch"/>, a sudden small jerk now and then.
        /// </summary>
        public void Motion(int firstVertex, float breath, bool twitch = false)
        {
            for (int v = firstVertex; v < data.Count; v++)
            {
                var d = data[v];
                d.z = twitch ? -2f - breath : -breath;
                data[v] = d;
            }
        }

        /// <summary>
        /// Gives the vertices from <paramref name="firstVertex"/> a slow sideways sway on the shared clock (carried in the
        /// flutter channel below -10, Flora.hlsl), by how far each may swing in metres: hanging vines and ropes, more
        /// the further down they hang.
        /// </summary>
        /// <summary>
        /// Gives the vertices from <paramref name="firstVertex"/> a slow orbit round the plant's centre on the shared clock,
        /// bobbing up and down by up to <paramref name="bob"/> metres (carried in the flutter channel below -100,
        /// Flora.hlsl): floating debris.
        /// </summary>
        public void Orbit(int firstVertex, float bob)
        {
            for (int v = firstVertex; v < data.Count; v++)
            {
                var d = data[v];
                d.z = -100f - Mathf.Max(bob, 0f);
                data[v] = d;
            }
        }

        public void Sway(int firstVertex, Func<Vector3, float> amount)
        {
            for (int v = firstVertex; v < data.Count; v++)
            {
                var d = data[v];
                d.z = -10f - Mathf.Max(amount(positions[v]), 0f);
                data[v] = d;
            }
        }

        /// <summary>
        /// A flat sheet of <paramref name="nu"/> × <paramref name="nv"/> quads from a corner along two edges (cloth:
        /// banners, awnings, flaps), seen from both sides (the flora shader draws double-sided). Returns its first vertex.
        /// </summary>
        public int Sheet(Vector3 corner, Vector3 u, Vector3 v, int nu, int nv, Color color, FloraPart part, float ao = 1f)
        {
            var first = positions.Count;
            var n = Vector3.Cross(u, v).normalized;
            for (int j = 0; j <= nv; j++)
            for (int i = 0; i <= nu; i++)
                Vertex(corner + u * (i / (float)nu) + v * (j / (float)nv), n, color, part, ao, 0f);
            for (int j = 0; j < nv; j++)
            for (int i = 0; i < nu; i++)
            {
                int a = first + j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                Triangle(a, b, c);
                Triangle(b, d, c);
            }
            return first;
        }

        /// <summary>
        /// Makes the vertices from <paramref name="firstVertex"/> cloth that waves in the wind (Flora.hlsl), by how free
        /// each is: 0 where it's held (a pole, a bar, a seam), 1 at a free edge. Carried on the part as Cloth + free × 0.4.
        /// </summary>
        public void Cloth(int firstVertex, Func<Vector3, float> free)
        {
            for (int v = firstVertex; v < data.Count; v++)
            {
                var d = data[v];
                d.y = (float)FloraPart.Cloth + Mathf.Clamp01(free(positions[v])) * 0.4f;
                d.z = 0f;
                data[v] = d;
            }
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
