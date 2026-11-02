using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Placeholder meshes for bat folk huts and floor props (#35, BATFOLK-PLAN step 3), built once per site from the
    /// baked BatFolkProp list and drawn by CaveSystem with copies of the cave material. Two meshes per site: the pale
    /// one (silk and hide: hut pods, the hides on racks) and the dark one (wood and weave: stalks, rack posts,
    /// baskets). The vertex layout is the cave walls' own (colour alpha = openness 0, uv0.y = tint, slot 0), so the
    /// lantern and the glow lights light them exactly as they light the rock. Shading is darker underneath (tint).
    /// </summary>
    public static class BatFolkMeshes
    {
        /// <summary>The cave shader's per-material rock tones for the pale and dark meshes.</summary>
        public static readonly Color PaleRock = new Color(0.84f, 0.8f, 0.72f), PaleDark = new Color(0.62f, 0.57f, 0.5f);
        public static readonly Color DarkRock = new Color(0.44f, 0.33f, 0.23f), DarkDark = new Color(0.24f, 0.17f, 0.11f);

        /// <summary>A hut pod's profile from top to bottom: (share of length, share of radius, tint).</summary>
        static readonly float3[] PodProfile =
        {
            new float3(0f, 0.14f, 1f), new float3(0.08f, 0.5f, 1f), new float3(0.22f, 0.84f, 0.95f),
            new float3(0.42f, 1f, 0.85f), new float3(0.62f, 0.96f, 0.72f), new float3(0.8f, 0.72f, 0.6f),
            new float3(0.92f, 0.4f, 0.5f), new float3(1f, 0f, 0.45f)
        };
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

            public void Vertex(float3 p, float3 n, float tint)
            {
                Positions.Add(p);
                Normals.Add(math.normalizesafe(n, new float3(0f, 1f, 0f)));
                // x terrain blend 0, y tint, z style slot 0 (the material's own colours), w rib shade 0.
                Data.Add(new Vector4(0f, tint, 0f, 0f));
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

        /// <summary>Builds the pale and dark meshes of one site (either may be null if the site has none of that).</summary>
        public static void Build(ref BatFolkSetBlob set, int site, out Mesh pale, out Mesh dark)
        {
            var p = new Builder();
            var d = new Builder();
            var rings = new List<float3>();
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
                    {
                        // The silk stalk, from a little inside the ceiling down into the pod's top.
                        rings.Clear();
                        rings.Add(new float3(0.3f, 0.07f, 0.8f));
                        rings.Add(new float3(-size.z - 0.1f, 0.05f, 0.7f));
                        d.Lathe(at, prop.Yaw, rings, 6);
                        rings.Clear();
                        foreach (var r in PodProfile)
                            rings.Add(new float3(-size.z - r.x * size.y, r.y * size.x, r.z));
                        p.Lathe(at, prop.Yaw, rings);
                        // A dark band round its middle (the entrance flap's seam), so the pod reads as made.
                        rings.Clear();
                        var band = -size.z - 0.5f * size.y;
                        rings.Add(new float3(band + 0.06f, size.x * 1.015f, 0.5f));
                        rings.Add(new float3(band - 0.06f, size.x * 1.015f, 0.5f));
                        d.Lathe(at, prop.Yaw, rings);
                        break;
                    }
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
                }
            }
            pale = p.ToMesh("Bat folk (pale)");
            dark = d.ToMesh("Bat folk (dark)");
        }
    }
}
