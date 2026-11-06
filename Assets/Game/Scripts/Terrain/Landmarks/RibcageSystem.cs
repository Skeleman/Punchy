using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the Ashlands ribcage (W-114): rib tubes along RibcageShape.Rib (snapped ribs split where they broke, with
    /// jagged ends), the spine with its vertebrae and dorsal spikes, the half-buried skull with dark eye sockets, horns
    /// and a row of teeth at the ash line, and the fallen pieces. Vertex colours carry the weathering: bone on the
    /// sides, ash settled on the tops, charring where the bones meet the drifts, and thin cracks. Three LODs, built once
    /// per island when the landmark blob changes. The bones are drawn from the same functions the collision uses, so
    /// what's seen is what's met. Seed-derived, so solo, host and client all draw the same skeleton. No far shadow: a
    /// solid strip would misrepresent the open cage.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class RibcageSystem : SystemBase
    {
        const float Lod0Distance = 300f, Lod1Distance = 1500f;

        /// <summary>The last build's triangle counts per LOD, for reports.</summary>
        public static int[] LodTris = new int[3];

        BlobAssetReference<LandmarkSetBlob> builtFrom;
        Mesh[] lods;
        Vector3 origin;
        Bounds bounds;
        Material material;
        bool warned;

        protected override void OnCreate() => RequireForUpdate<TerrainSource>();

        protected override void OnUpdate()
        {
            var source = SystemAPI.GetSingleton<TerrainSource>();
            if (!source.Landmarks.IsCreated)
                return;
            if (source.Landmarks != builtFrom)
            {
                builtFrom = source.Landmarks;
                Build(source);
            }
            if (lods == null)
                return;
            if (material == null)
            {
                // The Colossus material (vertex colour x tint, terrain haze), untinted: the colours are in the mesh.
                var colossus = Resources.Load<Material>("Colossus");
                if (colossus == null)
                {
                    if (!warned)
                        Debug.LogWarning("[landmarks] Colossus material missing from Resources");
                    warned = true;
                    return;
                }
                material = new Material(colossus) { name = "RibcageBone" };
                material.SetColor("_BaseColor", Color.white);
            }
            var cam = Camera.main;
            if (cam == null)
                return;
            var d = Vector3.Distance(cam.transform.position, bounds.ClosestPoint(cam.transform.position));
            var lod = d < Lod0Distance ? 0 : d < Lod1Distance ? 1 : 2;
            var rp = new RenderParams(material) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            Graphics.RenderMesh(rp, lods[lod], 0, Matrix4x4.Translate(origin));
        }

        protected override void OnDestroy()
        {
            Clear();
            if (material != null)
                Object.Destroy(material);
        }

        void Clear()
        {
            if (lods == null)
                return;
            foreach (var m in lods)
                if (m != null)
                    Object.Destroy(m);
            lods = null;
        }

        void Build(in TerrainSource source)
        {
            Clear();
            ref var set = ref source.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var inst = set.Instances[i];
                if (set.Types[inst.Type].Ribcage == 0)
                    continue;
                var l = RibcageShape.Layout(inst);
                origin = new Vector3(l.Centre.x, l.Base, l.Centre.y);
                lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var b = new Builder(l.Axis, l.Side);
                    BuildLod(b, l, lod);
                    lods[lod] = b.ToMesh($"Ribcage LOD{lod}");
                    LodTris[lod] = b.Tris;
                }
                bounds = lods[0].bounds;
                bounds.center += origin;
                Debug.Log($"[landmarks] AshRibcage meshes LOD0 {LodTris[0]} tris, LOD1 {LodTris[1]}, LOD2 {LodTris[2]}");
                return;
            }
        }

        static void BuildLod(Builder b, in RibcageLayout l, int lod)
        {
            int sides = lod == 0 ? 10 : lod == 1 ? 6 : 4;
            int segs = lod == 0 ? 24 : lod == 1 ? 12 : 6;
            var pts = new List<float3>();
            var radii = new List<float>();

            // Ribs: whole arcs, or the upper stub and the standing stump of a snapped one.
            for (int i = 0; i < RibcageShape.Pairs; i++)
                for (int side = 0; side < 2; side++)
                {
                    if ((l.Broken & (1u << (i * 2 + side))) != 0)
                    {
                        RibcageShape.Break(l.Key, i, side, out var tb, out var tb2);
                        RibArc(b, pts, radii, i, side, 0f, tb, segs, sides, false, true, l.Key);
                        RibArc(b, pts, radii, i, side, tb2, RibcageShape.RibEnd, segs, sides, true, false, l.Key);
                    }
                    else
                        RibArc(b, pts, radii, i, side, 0f, RibcageShape.RibEnd, segs, sides, false, false, l.Key);
                }

            // The spine, from inside the skull to the buried tail tip.
            var step = lod == 0 ? 3f : lod == 1 ? 6f : 15f;
            pts.Clear(); radii.Clear();
            for (var u = RibcageShape.NeckStart; ; u += step)
            {
                u = math.min(u, RibcageShape.TailEnd);
                pts.Add(new float3(u, RibcageShape.SpineY(u), 0f));
                radii.Add(RibcageShape.SpineR(u));
                if (u >= RibcageShape.TailEnd)
                    break;
            }
            b.Tube(pts, radii, sides, false, false, l.Key);

            // Vertebrae and dorsal spikes, the same on every LOD that draws them.
            if (lod < 2)
            {
                var rng = new Unity.Mathematics.Random(l.Key ^ 0x5E1Au);
                var n = 0;
                for (var u = RibcageShape.NeckStart + 10f; u < RibcageShape.TailEnd - 8f; u += 9f, n++)
                {
                    var y = RibcageShape.SpineY(u);
                    var r = RibcageShape.SpineR(u);
                    var dy = RibcageShape.SpineY(u + 1.2f) - RibcageShape.SpineY(u - 1.2f);
                    var t = math.normalize(new float3(2.4f, dy, 0f));
                    var c = new float3(u, y, 0f);
                    var len = rng.NextFloat(5f, 11f) * r / RibcageShape.SpineRadius;
                    var lean = rng.NextFloat(0.25f, 0.6f);
                    var twist = rng.NextFloat(-0.15f, 0.15f);
                    if (lod == 1 && (n & 1) == 1)
                        continue;
                    pts.Clear(); radii.Clear();
                    pts.Add(c - t * 1.2f); pts.Add(c + t * 1.2f);
                    radii.Add(r * 1.3f); radii.Add(r * 1.3f);
                    b.Tube(pts, radii, sides, false, false, l.Key);
                    var top = c + new float3(0f, r * 0.9f, 0f);
                    b.Cone(top, top + len * math.normalize(new float3(lean, 1f, twist)), r * 0.45f, math.max(4, sides - 2));
                }
            }

            // The skull, half sunk in the ash; its sockets and nostrils are coloured into it.
            int lat = lod == 0 ? 16 : lod == 1 ? 10 : 6, lon = lod == 0 ? 24 : lod == 1 ? 14 : 8;
            b.Skull(RibcageShape.Skull, RibcageShape.SkullRadii, lat, lon);
            if (lod < 2)
            {
                // Horns sweeping back off the crown.
                for (int s = -1; s <= 1; s += 2)
                {
                    pts.Clear(); radii.Clear();
                    var k = lod == 0 ? 8 : 4;
                    for (int j = 0; j <= k; j++)
                    {
                        var f = j / (float)k;
                        pts.Add(RibcageShape.Skull + new float3(-2f + 16f * f, 7.5f + 9f * f - 6f * f * f * f, s * (6f + 7f * f)));
                        radii.Add(math.lerp(2.4f, 0.35f, f));
                    }
                    b.Tube(pts, radii, math.max(4, sides - 2), false, false, l.Key, true);
                }
            }
            if (lod == 0)
            {
                // A row of teeth along the jaw where it meets the ash, a few snapped short.
                var rng = new Unity.Mathematics.Random(l.Key ^ 0x7EE7u);
                for (int j = -6; j <= 6; j++)
                {
                    var a = j * 0.13f;
                    var foot = RibcageShape.Skull + new float3(-13.2f * math.cos(a), -5.5f, 9.4f * math.sin(a));
                    var h = rng.NextFloat(1.6f, 3.2f) * (rng.NextFloat() < 0.2f ? 0.45f : 1f);
                    b.Cone(foot, foot + new float3(-0.6f, h, 0f), 0.55f, 5);
                }
            }

            // The fallen pieces, lying in the ash with both ends snapped.
            for (int k = 0; k < l.Fallen; k++)
            {
                RibcageShape.Fallen(l, k, out var fa, out var fb, out var fc);
                pts.Clear(); radii.Clear();
                var n = lod == 0 ? 6 : lod == 1 ? 3 : 1;
                for (int j = 0; j <= 2 * n; j++)
                {
                    var f = j / (float)n;
                    var p = f <= 1f ? math.lerp(fa, fb, f) : math.lerp(fb, fc, f - 1f);
                    pts.Add(new float3(p.x, RibcageShape.FallenY, p.y));
                    radii.Add(RibcageShape.FallenRadius);
                }
                b.Tube(pts, radii, sides, true, true, l.Key);
            }
        }

        static void RibArc(Builder b, List<float3> pts, List<float> radii, int i, int side, float t0, float t1, int segs, int sides, bool jagStart, bool jagEnd, uint key)
        {
            pts.Clear(); radii.Clear();
            var n = math.max(2, (int)math.ceil(segs * (t1 - t0) / RibcageShape.RibEnd));
            for (int k = 0; k <= n; k++)
            {
                var t = math.lerp(t0, t1, k / (float)n);
                pts.Add(RibcageShape.Rib(i, side, t));
                radii.Add(RibcageShape.RibR(t));
            }
            b.Tube(pts, radii, sides, jagStart, jagEnd, key + (uint)(i * 2 + side) * 7919u);
        }

        /// <summary>Collects smooth tubes, cones and the skull into one vertex-coloured mesh, in the ribcage's frame.</summary>
        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Color> col = new List<Color>();
            readonly List<int> idx = new List<int>();
            readonly float2 axis, side;
            public int Tris => idx.Count / 3;

            public Builder(float2 axis, float2 side)
            {
                this.axis = axis;
                this.side = side;
            }

            /// <summary>Local (u, y, v) to the mesh's world-aligned frame (a rotation about y; no mirror).</summary>
            Vector3 W(float3 p) => new Vector3(axis.x * p.x + side.x * p.z, p.y, axis.y * p.x + side.y * p.z);

            int Add(float3 p, float3 normal, Color c)
            {
                v.Add(W(p));
                n.Add(W(math.normalizesafe(normal, new float3(0f, 1f, 0f))));
                col.Add(c);
                return v.Count - 1;
            }

            /// <summary>A triangle facing the side 'outward' (local) points to (Unity's front faces).</summary>
            void Tri(int a, int b, int c, float3 outward)
            {
                var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(face, W(outward)) < 0f)
                    (b, c) = (c, b);
                idx.Add(a); idx.Add(b); idx.Add(c);
            }

            /// <summary>Weathered bone: ash dusting the tops, charring at the ash line, faint mottling and thin cracks.</summary>
            static Color Bone(float3 p, float3 normal)
            {
                var c = new float3(0.78f, 0.74f, 0.64f) * (1f + 0.07f * noise.snoise(p * 0.11f));
                var crack = math.abs(noise.snoise(new float3(p.x * 0.07f, p.y * 0.45f, p.z * 0.07f)));
                c *= math.lerp(0.55f, 1f, math.smoothstep(0.015f, 0.06f, crack));
                var up = math.normalizesafe(normal).y;
                c = math.lerp(c, new float3(0.44f, 0.42f, 0.41f), 0.65f * math.smoothstep(0.55f, 0.92f, up));
                c = math.lerp(c, new float3(0.2f, 0.18f, 0.17f), 0.75f * (1f - math.smoothstep(-0.5f, 6f, p.y)));
                return new Color(c.x, c.y, c.z, 1f).linear; // authored in sRGB; the shader reads vertex colour as linear
            }

            /// <summary>
            /// A smooth tube through pts with the given radii (parallel-transported rings, so no twist seams). Jagged
            /// ends push each end-ring vertex out by a random amount and close it on a short point: a snapped bone.
            /// A pointed end closes the last ring on a tip instead (horns).
            /// </summary>
            public void Tube(List<float3> pts, List<float> radii, int sides, bool jagStart, bool jagEnd, uint key, bool pointedEnd = false)
            {
                var count = pts.Count;
                if (count < 2)
                    return;
                var rng = new Unity.Mathematics.Random(key | 1u);
                var t0 = math.normalize(pts[1] - pts[0]);
                var nrm = math.normalize(math.cross(math.abs(t0.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f), t0));
                var start = v.Count;
                var firstT = t0;
                var lastT = t0;
                for (int i = 0; i < count; i++)
                {
                    var t = math.normalize(pts[math.min(i + 1, count - 1)] - pts[math.max(i - 1, 0)]);
                    nrm = math.normalize(nrm - math.dot(nrm, t) * t);
                    var bin = math.cross(t, nrm);
                    if (i == 0) firstT = t;
                    lastT = t;
                    for (int k = 0; k < sides; k++)
                    {
                        var a = k * 2f * math.PI / sides;
                        var dir = math.cos(a) * nrm + math.sin(a) * bin;
                        // Weathered, not turned: the surface swells and pits a little (well inside the collision margin).
                        var p = pts[i] + radii[i] * (1f + 0.06f * noise.snoise((pts[i] + radii[i] * dir) * 0.3f)) * dir;
                        if (i == 0 && jagStart)
                            p -= t * rng.NextFloat(0f, 0.9f) * radii[i];
                        if (i == count - 1 && jagEnd)
                            p += t * rng.NextFloat(0f, 0.9f) * radii[i];
                        Add(p, dir, Bone(p, dir));
                    }
                }
                for (int i = 0; i < count - 1; i++)
                    for (int k = 0; k < sides; k++)
                    {
                        int k1 = (k + 1) % sides;
                        int a = start + i * sides + k, b = start + i * sides + k1, c = start + (i + 1) * sides + k1, d = start + (i + 1) * sides + k;
                        var outward = (float3)n[a];
                        var o = new float3(math.dot(new float2(outward.x, outward.z), axis), outward.y, math.dot(new float2(outward.x, outward.z), side));
                        Tri(a, b, c, o);
                        Tri(a, c, d, o);
                    }
                // End caps: a short point, so ends never show open.
                var tipStart = pts[0] - firstT * radii[0] * (jagStart ? 0.6f : 0.3f);
                var tipEnd = pts[count - 1] + lastT * radii[count - 1] * (jagEnd ? 0.6f : pointedEnd ? 2.5f : 0.3f);
                Fan(start, sides, tipStart, -firstT);
                Fan(start + (count - 1) * sides, sides, tipEnd, lastT);
            }

            void Fan(int ring, int sides, float3 tip, float3 outward)
            {
                var c = Add(tip, outward, Bone(tip, outward));
                for (int k = 0; k < sides; k++)
                    Tri(ring + k, ring + (k + 1) % sides, c, outward);
            }

            /// <summary>A cone (spike or tooth) from a base ring to a tip.</summary>
            public void Cone(float3 basePt, float3 tip, float radius, int sides)
            {
                var t = math.normalize(tip - basePt);
                var nrm = math.normalize(math.cross(math.abs(t.y) < 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f), t));
                var bin = math.cross(t, nrm);
                var start = v.Count;
                for (int k = 0; k < sides; k++)
                {
                    var a = k * 2f * math.PI / sides;
                    var dir = math.cos(a) * nrm + math.sin(a) * bin;
                    var p = basePt + radius * dir;
                    Add(p, dir, Bone(p, dir));
                }
                var c = Add(tip, t, Bone(tip, t));
                for (int k = 0; k < sides; k++)
                {
                    var a = (k + 0.5f) * 2f * math.PI / sides;
                    Tri(start + k, start + (k + 1) % sides, c, math.cos(a) * nrm + math.sin(a) * bin);
                }
            }

            /// <summary>The skull: an ellipsoid (radii as u, y, v) with dark eye sockets and nostrils coloured in.</summary>
            public void Skull(float3 c, float3 r, int lat, int lon)
            {
                var start = v.Count;
                var eyeL = math.normalize(new float3(-0.72f, 0.45f, 0.53f));
                var eyeR = eyeL * new float3(1f, 1f, -1f);
                var noseL = math.normalize(new float3(-0.99f, 0.05f, 0.1f));
                var noseR = noseL * new float3(1f, 1f, -1f);
                for (int i = 0; i <= lat; i++)
                {
                    var th = math.PI * i / lat;
                    for (int j = 0; j < lon; j++)
                    {
                        var ph = 2f * math.PI * j / lon;
                        var d = new float3(math.sin(th) * math.cos(ph), math.cos(th), math.sin(th) * math.sin(ph));
                        var p = c + r * d;
                        var normal = d / r;
                        var bone = Bone(p, normal);
                        var socket = math.max(math.smoothstep(0.9f, 0.95f, math.max(math.dot(d, eyeL), math.dot(d, eyeR))),
                                              math.smoothstep(0.975f, 0.99f, math.max(math.dot(d, noseL), math.dot(d, noseR))));
                        Add(p, normal, Color.Lerp(bone, new Color(0.06f, 0.05f, 0.05f).linear, socket));
                    }
                }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        int j1 = (j + 1) % lon;
                        int a = start + i * lon + j, b = start + i * lon + j1, cc = start + (i + 1) * lon + j1, d = start + (i + 1) * lon + j;
                        var th = math.PI * (i + 0.5f) / lat;
                        var ph = 2f * math.PI * (j + 0.5f) / lon;
                        var o = new float3(math.sin(th) * math.cos(ph), math.cos(th), math.sin(th) * math.sin(ph));
                        if (i > 0) Tri(a, b, cc, o);
                        if (i < lat - 1) Tri(a, cc, d, o);
                    }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(col);
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
