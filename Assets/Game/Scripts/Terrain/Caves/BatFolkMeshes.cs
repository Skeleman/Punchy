using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Punchy.Flora;

namespace Punchy.Terrain
{
    /// <summary>
    /// Placeholder meshes for bat folk homes and floor props (#35, BATFOLK-PLAN step 3), built once per site from the
    /// baked BatFolkProp list and drawn by CaveSystem with copies of the cave material. Three meshes per site: the pale
    /// one (silk, straw and hide: pale woven pods, the hides on racks), the dark one (wood and reed: dark woven pods,
    /// hangers, rack posts, baskets) and the rock one (W-96: the pygmy niches, in the cave's own style slot). W-96:
    /// huts and pods are both drawn as hollow woven nests (Pod); the bat folk have no houses. The vertex layout is the cave walls' own (colour alpha = openness 0, uv0.y = tint, slot 0), so the
    /// lantern and the glow lights light them exactly as they light the rock. Shading is darker underneath (tint).
    /// </summary>
    public static class BatFolkMeshes
    {
        /// <summary>The cave shader's per-material rock tones for the pale and dark meshes.</summary>
        public static readonly Color PaleRock = new Color(0.84f, 0.8f, 0.72f), PaleDark = new Color(0.62f, 0.57f, 0.5f);
        public static readonly Color DarkRock = new Color(0.44f, 0.33f, 0.23f), DarkDark = new Color(0.24f, 0.17f, 0.11f);

        /// <summary>A basket's profile from the base up: (share of height, share of radius, tint).</summary>
        static readonly float3[] BasketProfile =
        {
            new float3(0f, 0f, 0.55f), new float3(0f, 0.78f, 0.6f), new float3(0.18f, 1f, 0.75f),
            new float3(0.8f, 0.94f, 0.95f), new float3(0.94f, 0.82f, 1f), new float3(1.06f, 0.4f, 1f), new float3(1.1f, 0f, 1f)
        };

        class Builder
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Data = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            /// <summary>The cave style slot written to every vertex: 0 (the material's own colours), or the cave's
            /// style for the rock mesh (W-96), so the pygmy niches take the look of the walls they are cut into.</summary>
            public float Slot;

            public void Vertex(float3 p, float3 n, float tint)
            {
                Positions.Add(p);
                Normals.Add(math.normalizesafe(n, new float3(0f, 1f, 0f)));
                // x terrain blend 0, y tint, z style slot, w rib shade 0.
                Data.Add(new Vector4(0f, tint, Slot, 0f));
            }

            /// <summary>A surface of revolution round a vertical axis through <paramref name="origin"/>: rings at
            /// (height, radius, tint), heights growing upwards (negative for things that hang).</summary>
            public void Lathe(float3 origin, float yaw, IReadOnlyList<float3> rings, int sides = 14)
            {
                var start = Positions.Count;
                for (int r = 0; r < rings.Count; r++)
                {
                    // The profile's slope gives the normal's tilt out of the horizontal.
                    var prev = rings[math.max(r - 1, 0)];
                    var next = rings[math.min(r + 1, rings.Count - 1)];
                    var dy = next.x - prev.x;
                    var dr = next.y - prev.y;
                    for (int s = 0; s <= sides; s++)
                    {
                        var a = yaw + s * math.PI * 2f / sides;
                        var dir = new float3(math.cos(a), 0f, math.sin(a));
                        var normal = dir * dy - new float3(0f, dr, 0f);
                        if (math.lengthsq(normal) < 1e-8f)
                            normal = new float3(0f, rings[r].x >= prev.x ? 1f : -1f, 0f);
                        // Rings at the same height as their neighbour are caps: straight up or down.
                        Vertex(origin + dir * rings[r].y + new float3(0f, rings[r].x, 0f), normal * math.sign(dy == 0f ? 1f : dy), rings[r].z);
                    }
                }
                for (int r = 0; r < rings.Count - 1; r++)
                    for (int s = 0; s < sides; s++)
                    {
                        var i = start + r * (sides + 1) + s;
                        var j = i + sides + 1;
                        Indices.Add(i); Indices.Add(j); Indices.Add(i + 1);
                        Indices.Add(i + 1); Indices.Add(j); Indices.Add(j + 1);
                    }
            }

            /// <summary>
            /// W-96: a woven surface of revolution over an arc (from, span) round a vertical axis through origin, rings
            /// at (height, radius, tint): every other quad of the weave is shaded darker (a checker of reeds), and an
            /// inside surface has its normals turned in (the cave shader draws both faces, but lights by the normal).
            /// </summary>
            public void Weave(float3 origin, IReadOnlyList<float3> rings, float from, float span, int sides, bool inside, float shade)
            {
                // Each quad has its own four corners, so the checker stays crisp instead of blurring across shared ones.
                for (int r = 0; r < rings.Count - 1; r++)
                    for (int s = 0; s < sides; s++)
                    {
                        var start = Positions.Count;
                        var tint = ((r + s) & 1) == 0 ? 1f : shade;
                        for (int c = 0; c < 4; c++)
                        {
                            var ring = r + (c >> 1);
                            var a = from + (s + (c & 1)) * span / sides;
                            var dir = new float3(math.cos(a), 0f, math.sin(a));
                            Vertex(origin + dir * rings[ring].y + new float3(0f, rings[ring].x, 0f), WeaveNormal(rings, ring, dir, inside), rings[ring].z * tint);
                        }
                        Indices.Add(start); Indices.Add(start + 2); Indices.Add(start + 1);
                        Indices.Add(start + 1); Indices.Add(start + 2); Indices.Add(start + 3);
                    }
            }

            /// <summary>
            /// A woven ring's normal: out from the axis along the profile (in, for an inside wall). A flat ring (a cap)
            /// faces up or down by its profile alone, which is already right seen from inside.
            /// </summary>
            static float3 WeaveNormal(IReadOnlyList<float3> rings, int r, float3 dir, bool inside)
            {
                var prev = rings[math.max(r - 1, 0)];
                var next = rings[math.min(r + 1, rings.Count - 1)];
                var dy = next.x - prev.x;
                var dr = next.y - prev.y;
                if (math.abs(dy) < 1e-5f)
                    return new float3(0f, dr > 0f ? -1f : 1f, 0f);
                var normal = (dir * dy - new float3(0f, dr, 0f)) * math.sign(dy);
                return math.normalizesafe(inside ? -normal : normal, dir);
            }

            /// <summary>A box round <paramref name="centre"/>, turned by <paramref name="rotation"/>; tint is lower on its underside.</summary>
            public void Box(float3 centre, float3 half, quaternion rotation, float tint)
            {
                for (int f = 0; f < 6; f++)
                {
                    var axis = f % 3;
                    var sign = f < 3 ? 1f : -1f;
                    var n = float3.zero; n[axis] = sign;
                    var u = float3.zero; u[(axis + 1) % 3] = 1f;
                    var v = float3.zero; v[(axis + 2) % 3] = 1f;
                    var start = Positions.Count;
                    var faceTint = axis == 1 && sign < 0f ? tint * 0.6f : tint;
                    for (int k = 0; k < 4; k++)
                    {
                        var su = k == 1 || k == 2 ? 1f : -1f;
                        var sv = k >= 2 ? 1f : -1f;
                        var local = (n + u * su + v * sv) * half;
                        Vertex(centre + math.mul(rotation, local), math.mul(rotation, n), faceTint);
                    }
                    Indices.Add(start); Indices.Add(start + 1); Indices.Add(start + 2);
                    Indices.Add(start); Indices.Add(start + 2); Indices.Add(start + 3);
                }
            }

            public Mesh ToMesh(string name)
            {
                if (Positions.Count == 0)
                    return null;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(Positions);
                mesh.SetNormals(Normals);
                // Colour alpha is the walls' openness: 0, deep in the cave (no daylight, no fog).
                var colors = new Color[Positions.Count];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color(0.5f, 0.5f, 0.5f, 0f);
                mesh.SetColors(colors);
                mesh.SetUVs(0, Data);
                mesh.SetIndices(Indices, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }

        /// <summary>
        /// Builds the pale, dark and rock meshes of one site (any may be null if the site has none of that). The rock
        /// mesh (W-96: the pygmy niches) is drawn with the cave material itself, in the cave's style slot.
        /// </summary>
        public static void Build(ref BatFolkSetBlob set, int site, int caveStyle, out Mesh pale, out Mesh dark, out Mesh rock)
        {
            var p = new Builder();
            var d = new Builder();
            var stone = new Builder { Slot = caveStyle };
            var rings = new List<float3>();
            var shell = new KitBox[BatFolkHomes.ShellBoxes];
            for (int i = 0; i < set.Props.Length; i++)
            {
                ref var prop = ref set.Props[i];
                if (prop.Site != site)
                    continue;
                var at = prop.Position;
                var size = prop.Size;
                var turn = quaternion.RotateY(prop.Yaw);
                switch (prop.Kind)
                {
                    case BatFolkPropKind.Hut:
                        // W-96: the roost huts are woven nest pods too (their Size means the same as a Pod's).
                        Pod(prop, p, d, rings);
                        break;
                    case BatFolkPropKind.Rack:
                    {
                        var w = size.x * 0.5f;
                        var h = size.y;
                        // Posts (sunk into the floor), the bar across their tops, and a hide hanging over it.
                        d.Box(at + math.mul(turn, new float3(-w, h * 0.5f, 0f)), new float3(0.05f, h * 0.5f + 0.05f, 0.05f), turn, 0.8f);
                        d.Box(at + math.mul(turn, new float3(w, h * 0.5f, 0f)), new float3(0.05f, h * 0.5f + 0.05f, 0.05f), turn, 0.8f);
                        d.Box(at + math.mul(turn, new float3(0f, h - 0.04f, 0f)), new float3(w + 0.08f, 0.04f, 0.04f), turn, 0.9f);
                        var hideTurn = math.mul(turn, quaternion.RotateX(0.12f));
                        p.Box(at + math.mul(turn, new float3(0f, h * 0.68f, 0.06f)), new float3(w * 0.78f, h * 0.3f, 0.015f), hideTurn, 0.85f);
                        break;
                    }
                    case BatFolkPropKind.Basket:
                    {
                        rings.Clear();
                        foreach (var r in BasketProfile)
                            rings.Add(new float3(r.x * size.y, r.y * size.x, r.z));
                        d.Lathe(at, prop.Yaw, rings, 10);
                        break;
                    }
                    case BatFolkPropKind.RoostFrame:
                    {
                        // Two cords down to a frame of bars the bat folk hang from by their feet.
                        var w = size.x * 0.5f;
                        var low = -size.z - size.y;
                        for (int s = -1; s <= 1; s += 2)
                            d.Box(at + math.mul(turn, new float3(s * w, (0.3f + low) * 0.5f, 0f)), new float3(0.03f, (0.3f - low) * 0.5f, 0.03f), turn, 0.75f);
                        for (int b = 0; b < 3; b++)
                            d.Box(at + math.mul(turn, new float3(0f, -size.z - b * size.y * 0.5f, 0f)), new float3(w + 0.06f, 0.035f, 0.035f), turn, 0.9f - b * 0.1f);
                        // A hide strip knotted over the top bar.
                        p.Box(at + math.mul(turn, new float3(w * 0.3f, -size.z - 0.25f, 0.04f)), new float3(0.18f, 0.25f, 0.012f), turn, 0.8f);
                        break;
                    }
                    case BatFolkPropKind.GuanoStone:
                    {
                        // A heap of guano-white stones: one big domed one and a few small ones round it.
                        var h = math.hash(new int3(math.floor(at * 16f)));
                        for (int k = 0; k < 4; k++)
                        {
                            var r = k == 0 ? size.x * 0.6f : size.x * (0.2f + 0.08f * ((h >> (k * 3)) & 3));
                            var a = prop.Yaw + k * 2.1f;
                            var o = k == 0 ? float3.zero : new float3(math.cos(a), 0f, math.sin(a)) * size.x * 0.75f;
                            var hh = k == 0 ? size.y : size.y * 0.45f;
                            rings.Clear();
                            rings.Add(new float3(0f, 0f, 0.7f)); rings.Add(new float3(0f, r, 0.75f));
                            rings.Add(new float3(hh * 0.55f, r * 0.9f, 0.95f)); rings.Add(new float3(hh, r * 0.4f, 1f)); rings.Add(new float3(hh * 1.04f, 0f, 1f));
                            p.Lathe(at + o, a, rings, 7);
                        }
                        break;
                    }
                    case BatFolkPropKind.EchoChime:
                    {
                        // A cord down to a ring, and pale bone tubes of falling lengths hung round it.
                        var ring = -size.z;
                        d.Box(at + new float3(0f, (0.3f + ring) * 0.5f, 0f), new float3(0.02f, (0.3f - ring) * 0.5f, 0.02f), turn, 0.75f);
                        rings.Clear();
                        rings.Add(new float3(ring + 0.03f, size.x, 0.9f)); rings.Add(new float3(ring - 0.03f, size.x, 0.8f));
                        d.Lathe(at, prop.Yaw, rings, 10);
                        for (int k = 0; k < 7; k++)
                        {
                            var a = prop.Yaw + k * math.PI * 2f / 7f;
                            var len = size.y * (1f - k * 0.09f);
                            var o = new float3(math.cos(a) * size.x, ring - 0.08f - len * 0.5f, math.sin(a) * size.x);
                            p.Box(at + o, new float3(0.025f, len * 0.5f, 0.025f), quaternion.RotateY(a), 0.9f);
                        }
                        break;
                    }
                    case BatFolkPropKind.Hammock:
                    {
                        // Two posts and a woven sling sagging between them (four slats along the sag).
                        var w = size.x * 0.5f;
                        var h = size.y;
                        for (int s = -1; s <= 1; s += 2)
                            d.Box(at + math.mul(turn, new float3(s * w, h * 0.5f, 0f)), new float3(0.06f, h * 0.5f + 0.05f, 0.06f), turn, 0.8f);
                        for (int k = 0; k < 4; k++)
                        {
                            var x0 = -w + k * w * 0.5f;
                            var x1 = x0 + w * 0.5f;
                            var y0 = h - 0.15f - 0.55f * (1f - (x0 / w) * (x0 / w));
                            var y1 = h - 0.15f - 0.55f * (1f - (x1 / w) * (x1 / w));
                            var slope = quaternion.RotateZ(math.atan2(y1 - y0, x1 - x0));
                            p.Box(at + math.mul(turn, new float3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0f)),
                                new float3(math.distance(new float2(x0, y0), new float2(x1, y1)) * 0.5f + 0.02f, 0.02f, 0.36f), math.mul(turn, slope), 0.85f);
                        }
                        break;
                    }
                    case BatFolkPropKind.GogglePeg:
                    {
                        // A post with 1-3 pegs, each with a pair of round dark-lensed goggles hung on it.
                        var h = size.y;
                        d.Box(at + new float3(0f, h * 0.5f, 0f), new float3(0.05f, h * 0.5f + 0.05f, 0.05f), turn, 0.8f);
                        for (int k = 0; k < (int)size.x; k++)
                        {
                            var a = prop.Yaw + k * 2.2f;
                            var py = h - 0.2f - k * 0.3f;
                            var peg = quaternion.RotateY(a);
                            d.Box(at + math.mul(peg, new float3(0f, py, 0.14f)), new float3(0.015f, 0.015f, 0.12f), peg, 0.9f);
                            for (int e = -1; e <= 1; e += 2)
                            {
                                rings.Clear();
                                rings.Add(new float3(0f, 0f, 0.4f)); rings.Add(new float3(0f, 0.045f, 0.45f));
                                rings.Add(new float3(0.03f, 0.045f, 0.5f)); rings.Add(new float3(0.03f, 0f, 0.5f));
                                d.Lathe(at + math.mul(peg, new float3(e * 0.055f, py - 0.08f, 0.24f)), a, rings, 8);
                            }
                            p.Box(at + math.mul(peg, new float3(0f, py - 0.04f, 0.2f)), new float3(0.11f, 0.01f, 0.005f), peg, 0.7f);
                        }
                        break;
                    }
                    case BatFolkPropKind.Pod:
                        Pod(prop, p, d, rings);
                        break;
                    case BatFolkPropKind.Niche:
                        Niche(prop, p, d, stone, rings, shell);
                        break;
                }
            }
            pale = p.ToMesh("Bat folk (pale)");
            dark = d.ToMesh("Bat folk (dark)");
            rock = stone.ToMesh("Bat folk (rock)");
        }

        /// <summary>
        /// W-96: a bat folk pod, the only kind of bat folk home (Zach: nothing that looks like a house). An egg hung by
        /// its narrow end from a cord or a root, hollow like a weaver bird's nest: an outer and an inner woven wall
        /// (a checker of light and dark reed), a wide opening in one side with a thick woven lip, a closed woven floor
        /// with bedding on it, loose twigs poking out and straws dangling under it. Dark reed or pale straw from a hash
        /// of its anchor. The huts are drawn the same way (their Size means the same).
        /// </summary>
        static void Pod(in BatFolkProp prop, Builder p, Builder d, List<float3> rings)
        {
            var at = prop.Position;
            var r = prop.Size.x;
            var len = prop.Size.y;
            var style = BatFolkHomes.Style(at);
            var reed = BatFolkHomes.Woven(style) ? d : p;
            var rng = new Unity.Mathematics.Random(style | 1u);
            var top = -prop.Size.z;
            if (BatFolkHomes.Root(style))
            {
                // A root down from the rock: a flared foot in the ceiling, then a crooked run of segments to the pod.
                rings.Clear();
                rings.Add(new float3(0.3f, 0.32f, 0.5f)); rings.Add(new float3(-0.1f, 0.2f, 0.55f)); rings.Add(new float3(-0.45f, 0.08f, 0.6f));
                d.Lathe(at, prop.Yaw, rings, 7);
                var from = new float3(0f, -0.4f, 0f);
                const int Segments = 4;
                for (int s = 1; s <= Segments; s++)
                {
                    var t = s / (float)Segments;
                    var sway = s == Segments ? float2.zero
                        : 0.24f * new float2(BatFolkHomes.Unit(style, 8 + s * 3) - 0.5f, BatFolkHomes.Unit(style, 12 + s * 3) - 0.5f);
                    var to = new float3(sway.x, math.lerp(-0.4f, top + 0.15f, t), sway.y);
                    Strand(d, at + from, at + to, 0.05f, 0.55f);
                    from = to;
                }
            }
            else
            {
                rings.Clear();
                rings.Add(new float3(0.3f, 0.035f, 0.8f)); rings.Add(new float3(top - 0.1f, 0.03f, 0.7f));
                d.Lathe(at, prop.Yaw, rings, 6);
            }
            // The opening faces along the prop's Face (sin yaw, cos yaw), as BatFolkHome.Face says.
            var open = math.atan2(math.cos(prop.Yaw), math.sin(prop.Yaw));
            const float Gap = 2.1f, Full = math.PI * 2f, Inner = 0.86f;
            float t0 = PodOpenFrom, t1 = PodOpenTo;
            // The outer wall: whole above and below the opening, an arc round it.
            PodWall(reed, at, rings, r, top, len, 0f, t0, 1f, 0f, Full, false, false, false);
            PodWall(reed, at, rings, r, top, len, t0, t1, 1f, open + Gap * 0.5f, Full - Gap, false, false, false);
            PodWall(reed, at, rings, r, top, len, t1, 1f, 1f, 0f, Full, false, false, false);
            // The inner wall (what is seen through the opening), capped under the neck and over the floor.
            PodWall(reed, at, rings, r, top, len, 0.05f, t0, Inner, 0f, Full, true, true, false);
            PodWall(reed, at, rings, r, top, len, t0, t1, Inner, open + Gap * 0.5f, Full - Gap, true, false, false);
            PodWall(reed, at, rings, r, top, len, t1, BatFolkHomes.PodFloor, Inner, 0f, Full, true, false, true);
            // The top knot where the hanger takes it.
            rings.Clear();
            rings.Add(new float3(top + 0.12f, 0f, 0.8f)); rings.Add(new float3(top + 0.12f, r * 0.2f, 0.8f)); rings.Add(new float3(top - 0.1f, r * 0.36f, 0.8f));
            d.Lathe(at, prop.Yaw, rings, 10);
            // The lip: bundles of reed round the opening's edge, bridging the two walls.
            var thick = 0.03f + 0.05f * r;
            for (int e = 0; e < 2; e++)
            {
                var a = open + (e == 0 ? -0.5f : 0.5f) * Gap;
                for (int s = 0; s < 6; s++)
                    Strand(reed, PodPoint(at, r, top, len, a, math.lerp(t0, t1, s / 6f), rng.NextFloat(-0.04f, 0.04f)),
                        PodPoint(at, r, top, len, a, math.lerp(t0, t1, (s + 1) / 6f), rng.NextFloat(-0.04f, 0.04f)), thick, 0.7f);
            }
            for (int e = 0; e < 2; e++)
                for (int s = 0; s < 8; s++)
                {
                    var t = e == 0 ? t0 : t1;
                    Strand(reed, PodPoint(at, r, top, len, open + Gap * (s / 8f - 0.5f), t, rng.NextFloat(-0.04f, 0.04f)),
                        PodPoint(at, r, top, len, open + Gap * ((s + 1) / 8f - 0.5f), t, rng.NextFloat(-0.04f, 0.04f)), thick, 0.75f);
                }
            // Bedding on the floor inside: a pale pad of hide and moss.
            var bed = top - len * BatFolkHomes.PodFloor + 0.02f;
            rings.Clear();
            rings.Add(new float3(bed + 0.06f, 0f, 1f)); rings.Add(new float3(bed + 0.06f, PodRadius(r, BatFolkHomes.PodFloor - 0.08f) * Inner * 0.8f, 0.9f));
            rings.Add(new float3(bed, PodRadius(r, BatFolkHomes.PodFloor - 0.08f) * Inner * 0.85f, 0.8f));
            p.Lathe(at, prop.Yaw, rings, 10);
            // Loose twigs poking out of the weave (not out of the opening).
            var twigs = 8 + (int)(r * 6f);
            for (int i = 0; i < twigs; i++)
            {
                var a = rng.NextFloat(Full);
                var t = rng.NextFloat(0.12f, 0.95f);
                if (t > t0 - 0.04f && t < t1 + 0.04f && math.abs(math.atan2(math.sin(a - open), math.cos(a - open))) < Gap * 0.5f + 0.15f)
                    continue;
                var root = PodPoint(at, r, top, len, a, t, 0.04f);
                var dir = math.normalize(new float3(math.cos(a), rng.NextFloat(-0.6f, 0.6f), math.sin(a)) + rng.NextFloat3(-0.5f, 0.5f));
                Strand(d, root - dir * 0.08f, root + dir * r * rng.NextFloat(0.18f, 0.45f), 0.012f + 0.006f * r, 0.5f);
            }
            // Straws dangling under it.
            for (int i = 0; i < 5; i++)
            {
                var a = rng.NextFloat(Full);
                var t = rng.NextFloat(0.86f, 0.97f);
                var root = PodPoint(at, r, top, len, a, t, 0f);
                var tip = root + new float3(math.cos(a) * 0.1f, -rng.NextFloat(0.3f, 0.75f) * math.sqrt(r), math.sin(a) * 0.1f);
                Strand(reed, root, tip, 0.012f, 0.6f);
            }
        }

        /// <summary>Where the pod's opening runs, as shares of its length from the top.</summary>
        const float PodOpenFrom = 0.3f, PodOpenTo = 0.8f;

        /// <summary>
        /// One woven section of a pod's wall, from share t0 to t1 of its length, over an arc (from, span) round it:
        /// scale 1 for the outer wall, less for the inner (whose normals face in). Caps close it to the axis.
        /// </summary>
        static void PodWall(Builder b, float3 at, List<float3> rings, float r, float top, float len, float t0, float t1,
            float scale, float from, float span, bool inside, bool capStart, bool capEnd)
        {
            rings.Clear();
            var n = math.max(2, (int)math.ceil((t1 - t0) * 16f));
            if (capStart)
                rings.Add(new float3(top - t0 * len, 0f, 0.6f));
            for (int i = 0; i <= n; i++)
            {
                var t = math.lerp(t0, t1, i / (float)n);
                // Lighter where the light reaches, darker inside and towards the bottom.
                var tint = math.lerp(1f, 0.62f, t) * (inside ? 0.72f : 1f);
                rings.Add(new float3(top - t * len, PodRadius(r, t) * scale, tint));
            }
            if (capEnd)
                rings.Add(new float3(top - t1 * len, 0f, 0.5f));
            var sides = math.max(6, (int)math.ceil(span / (math.PI * 2f) * 22f));
            b.Weave(at, rings, from, span, sides, inside, 0.68f);
        }

        /// <summary>A point in the pod's wall at angle a and share t of its length, pushed out by out (m).</summary>
        static float3 PodPoint(float3 at, float r, float top, float len, float a, float t, float push)
        {
            var radius = PodRadius(r, t) * 0.93f + push;
            return at + new float3(math.cos(a) * radius, top - t * len, math.sin(a) * radius);
        }

        /// <summary>An egg hung by its narrow end: a neck at the top, fullest a little below the middle, rounded shut at the bottom.</summary>
        static float PodRadius(float r, float t)
        {
            var s = math.sin(math.PI * math.lerp(0.07f, 1f, math.saturate(t)));
            return r * math.pow(math.max(s, 0f), 0.7f) * math.lerp(0.82f, 1f, math.saturate(t));
        }

        /// <summary>A thin square rod from one point to another (a twig, a reed bundle, a root segment).</summary>
        static void Strand(Builder b, float3 from, float3 to, float thick, float tint)
        {
            var run = to - from;
            var length = math.length(run);
            if (length < 1e-4f)
                return;
            var dir = run / length;
            var rotation = quaternion.LookRotationSafe(dir, math.abs(dir.y) > 0.95f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f));
            b.Box((from + to) * 0.5f, new float3(thick, thick, length * 0.5f + thick * 0.5f), rotation, tint);
        }

        /// <summary>
        /// W-96: a blind pygmy home niche: its rock shell (the same boxes as its collision) in the cave's look, a
        /// bedroll on one side and a glow-pool in a stone rim or a fire ring on the other.
        /// </summary>
        static void Niche(in BatFolkProp prop, Builder p, Builder d, Builder k, List<float3> rings, KitBox[] shell)
        {
            var n = BatFolkHomes.NicheShell(prop, shell);
            for (int i = 0; i < n; i++)
                k.Box(shell[i].Centre, shell[i].Half, shell[i].Rotation, i == 3 ? 0.7f : 0.95f);
            var style = BatFolkHomes.Style(prop.Position);
            var w = prop.Size.x * 0.5f;
            var depth = prop.Size.y;
            var turn = quaternion.RotateY(prop.Yaw);
            var headroom = BatFolkHomes.Headroom(prop);
            // A few rough stones on the brow and the jambs, so the cut doesn't read as a box.
            for (int s = 0; s < 4; s++)
            {
                var x = (BatFolkHomes.Unit(style, 4 + s * 6) - 0.5f) * 2f * (w + BatFolkHomes.Jamb * 0.6f);
                var y = s < 2 ? headroom + BatFolkHomes.Lintel * 0.8f : headroom * (0.3f + 0.4f * BatFolkHomes.Unit(style, 7 + s * 5));
                if (s >= 2)
                    x = (s == 2 ? -1f : 1f) * (w + BatFolkHomes.Jamb * 0.7f);
                var size = 0.18f + 0.14f * BatFolkHomes.Unit(style, 9 + s * 5);
                var lump = math.mul(turn, quaternion.Euler(0.4f + 0.5f * s, 0.9f * s, 0.3f));
                k.Box(BatFolkHomes.At(prop, new float3(x, y, depth + 0.04f)), new float3(size, size * 0.8f, size), lump, 0.9f);
            }
            // The bedroll along the niche, a rolled head end at the back, a dark tie.
            var side = BatFolkHomes.Left(style) ? -1f : 1f;
            var bx = side * (w - 0.42f);
            var bedLength = math.min(depth - 0.15f, 1.25f);
            p.Box(BatFolkHomes.At(prop, new float3(bx, 0.05f, depth * 0.5f)), new float3(0.33f, 0.05f, bedLength * 0.5f), turn, 0.85f);
            p.Box(BatFolkHomes.At(prop, new float3(bx, 0.13f, depth * 0.5f - bedLength * 0.5f + 0.14f)), new float3(0.3f, 0.08f, 0.11f), turn, 0.95f);
            d.Box(BatFolkHomes.At(prop, new float3(bx, 0.1f, depth * 0.5f + 0.1f)), new float3(0.34f, 0.012f, 0.03f), turn, 0.5f);
            // The glow-pool or the fire ring on the other side, towards the opening.
            var c = BatFolkHomes.At(prop, new float3(-side * (w - 0.38f), 0f, depth * 0.62f));
            if (BatFolkHomes.Fire(style))
            {
                for (int s = 0; s < 7; s++)
                {
                    var a = prop.Yaw + s * math.PI * 2f / 7f;
                    k.Box(c + new float3(math.cos(a) * 0.26f, 0.05f, math.sin(a) * 0.26f), new float3(0.07f, 0.06f, 0.06f), quaternion.RotateY(a), 0.85f);
                }
                // Ash and three crossed sticks, burnt dark (no flame: the pygmies are blind, and the bat folk keep no lights).
                rings.Clear();
                rings.Add(new float3(0.015f, 0f, 0.4f)); rings.Add(new float3(0.015f, 0.2f, 0.35f));
                p.Lathe(c, prop.Yaw, rings, 9);
                for (int s = 0; s < 3; s++)
                {
                    var stick = math.mul(quaternion.RotateY(prop.Yaw + s * 2.1f), quaternion.RotateX(0.35f));
                    d.Box(c + new float3(0f, 0.09f, 0f), new float3(0.022f, 0.022f, 0.17f), stick, 0.3f);
                }
            }
            else
            {
                // A glow-pool: a shallow bowl of pale water in a rim of stone.
                rings.Clear();
                rings.Add(new float3(0f, 0.3f, 0.8f)); rings.Add(new float3(0.08f, 0.28f, 0.9f)); rings.Add(new float3(0.08f, 0.22f, 0.9f));
                k.Lathe(c, prop.Yaw, rings, 10);
                rings.Clear();
                rings.Add(new float3(0.055f, 0.23f, 1f)); rings.Add(new float3(0.055f, 0f, 1f));
                p.Lathe(c, prop.Yaw, rings, 10);
            }
        }
    }
}
