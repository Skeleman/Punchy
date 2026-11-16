using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-07 (#260): the map editor's live ground. A square grid mesh over part of the island (the island's own
    /// heights from <see cref="MapIsland.SampleGrid"/>, plus the edits on top) and a flat sea plane, as hidden editor
    /// objects that are never saved. A brush only rewrites the vertices it reached. It's a preview of the ground's
    /// shape, not the game's terrain renderer: no biome surfaces, plants or places.
    /// </summary>
    public sealed class MapPreview : IDisposable
    {
        public float2 Origin { get; private set; }
        public float Step { get; private set; }
        public int Resolution { get; private set; }
        public float Size => Step * (Resolution - 1);

        float[] baseHeights;
        Vector3[] vertices;
        Mesh mesh;
        GameObject ground, sea;
        Material groundMaterial, seaMaterial;

        public bool Built => mesh != null;

        /// <summary>Builds (or rebuilds) the grid centred on <paramref name="centre"/>: <paramref name="size"/> metres across, a vertex every <paramref name="step"/>.</summary>
        public void Build(MapIsland island, IslandEdits edits, float2 centre, float size, float step)
        {
            Step = math.max(1f, step);
            Resolution = math.clamp((int)math.round(size / Step) + 1, 2, 2049);
            // Snap to the grid so rebuilding at a new centre keeps vertices on the same world points.
            Origin = math.round((centre - Size * 0.5f) / Step) * Step;
            baseHeights = island.SampleGrid(Origin, Step, Resolution);
            int n = Resolution * Resolution;
            vertices = new Vector3[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
                uv[i] = new Vector2(i % Resolution, i / Resolution) / (Resolution - 1);
            var tris = new int[(Resolution - 1) * (Resolution - 1) * 6];
            int t = 0;
            for (int z = 0; z < Resolution - 1; z++)
                for (int x = 0; x < Resolution - 1; x++)
                {
                    int a = z * Resolution + x, b = a + 1, c = a + Resolution, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            EnsureObjects();
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            Fill(edits, 0, 0, Resolution - 1, Resolution - 1);
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            float seaSize = Size * 1.5f;
            sea.transform.position = new Vector3(Origin.x + Size * 0.5f, island.SeaLevel, Origin.y + Size * 0.5f);
            sea.transform.localScale = new Vector3(seaSize / 10f, 1f, seaSize / 10f);
        }

        /// <summary>Re-applies the edits over the square [min, max] (metres) after a brush changed them.</summary>
        public void Refresh(IslandEdits edits, float2 min, float2 max)
        {
            if (!Built) return;
            var lo = (int2)math.floor((min - Origin) / Step) - 1;
            var hi = (int2)math.ceil((max - Origin) / Step) + 1;
            lo = math.max(lo, 0); hi = math.min(hi, Resolution - 1);
            if (math.any(lo > hi)) return;
            Fill(edits, lo.x, lo.y, hi.x, hi.y);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>Re-applies all the edits (after load, new or undo).</summary>
        public void RefreshAll(IslandEdits edits) => Refresh(edits, Origin, Origin + Size);

        void Fill(IslandEdits edits, int x0, int z0, int x1, int z1)
        {
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * Resolution + x;
                    var xz = Origin + new float2(x, z) * Step;
                    vertices[i] = new Vector3(xz.x, baseHeights[i] + edits.Sample(xz), xz.y);
                }
        }

        /// <summary>The preview's height at a world point (bilinear between its vertices), or NaN outside it.</summary>
        public float HeightAt(float2 xz)
        {
            if (!Built) return float.NaN;
            var g = (xz - Origin) / Step;
            if (math.any(g < 0f) || math.any(g > Resolution - 1)) return float.NaN;
            var c = math.min((int2)math.floor(g), Resolution - 2);
            var f = g - c;
            int i = c.y * Resolution + c.x;
            float a = vertices[i].y, b = vertices[i + 1].y, d = vertices[i + Resolution].y, e = vertices[i + Resolution + 1].y;
            return math.lerp(math.lerp(a, b, f.x), math.lerp(d, e, f.x), f.y);
        }

        /// <summary>Where a ray first meets the preview ground (marched at half a cell, then bisected).</summary>
        public bool Raycast(Ray ray, out Vector3 hit)
        {
            hit = default;
            if (!Built) return false;
            float stepLen = Step * 0.5f, maxLen = Size * 3f;
            var dir = ray.direction.normalized;
            float prevT = 0f;
            bool prevAbove = true;
            for (float t = 0f; t < maxLen; t += stepLen)
            {
                var p = ray.origin + dir * t;
                float h = HeightAt(new float2(p.x, p.z));
                if (float.IsNaN(h)) { prevAbove = true; prevT = t; continue; }
                bool above = p.y > h;
                if (!above && prevAbove && t > 0f)
                {
                    float a = prevT, b = t;
                    for (int k = 0; k < 16; k++)
                    {
                        float m = (a + b) * 0.5f;
                        var q = ray.origin + dir * m;
                        float hm = HeightAt(new float2(q.x, q.z));
                        if (!float.IsNaN(hm) && q.y <= hm) b = m; else a = m;
                    }
                    hit = ray.origin + dir * b;
                    hit.y = HeightAt(new float2(hit.x, hit.z));
                    return true;
                }
                prevAbove = above; prevT = t;
            }
            return false;
        }

        void EnsureObjects()
        {
            if (mesh == null) mesh = new Mesh { name = "MapEditorGround", hideFlags = HideFlags.HideAndDontSave };
            // Editor-only: Shader.Find is fine here (never at runtime).
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (groundMaterial == null)
            {
                groundMaterial = new Material(lit) { name = "MapEditorGround", hideFlags = HideFlags.HideAndDontSave };
                groundMaterial.SetColor("_BaseColor", new Color(0.55f, 0.6f, 0.42f));
                groundMaterial.SetFloat("_Smoothness", 0.1f);
            }
            if (seaMaterial == null)
            {
                seaMaterial = new Material(lit) { name = "MapEditorSea", hideFlags = HideFlags.HideAndDontSave };
                seaMaterial.SetColor("_BaseColor", new Color(0.18f, 0.36f, 0.55f));
                seaMaterial.SetFloat("_Smoothness", 0.8f);
            }
            if (ground == null)
            {
                ground = new GameObject("MapEditorGround") { hideFlags = HideFlags.HideAndDontSave };
                ground.AddComponent<MeshFilter>().sharedMesh = mesh;
                ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            }
            if (sea == null)
            {
                sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
                sea.name = "MapEditorSea";
                sea.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DestroyImmediate(sea.GetComponent<Collider>());
                sea.GetComponent<MeshRenderer>().sharedMaterial = seaMaterial;
            }
        }

        /// <summary>Outlines each edited tile on the ground (scene view, repaint only).</summary>
        public void DrawTiles(IslandEdits edits)
        {
            if (!Built) return;
            Handles.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            var pts = new Vector3[17];
            foreach (var t in edits.Tiles())
            {
                var min = (float2)t * IslandEdits.TileSize;
                int k = 0;
                for (int side = 0; side < 4; side++)
                    for (int s = 0; s < 4; s++)
                    {
                        float u = s / 4f;
                        var xz = side switch
                        {
                            0 => min + new float2(u, 0f) * IslandEdits.TileSize,
                            1 => min + new float2(1f, u) * IslandEdits.TileSize,
                            2 => min + new float2(1f - u, 1f) * IslandEdits.TileSize,
                            _ => min + new float2(0f, 1f - u) * IslandEdits.TileSize
                        };
                        float h = HeightAt(xz);
                        pts[k++] = new Vector3(xz.x, (float.IsNaN(h) ? 0f : h) + 0.5f, xz.y);
                    }
                pts[16] = pts[0];
                Handles.DrawAAPolyLine(2f, pts);
            }
        }

        public void Dispose()
        {
            if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
            if (sea != null) UnityEngine.Object.DestroyImmediate(sea);
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (groundMaterial != null) UnityEngine.Object.DestroyImmediate(groundMaterial);
            if (seaMaterial != null) UnityEngine.Object.DestroyImmediate(seaMaterial);
            baseHeights = null; vertices = null;
        }
    }
}
