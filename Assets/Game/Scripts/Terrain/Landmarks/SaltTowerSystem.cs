using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the salt tower (W-27): a cluster of jittered hexagonal basalt columns split into blocks, the spiral of
    /// column-stub steps from SaltTowerShape (so what's drawn is exactly what's stood on), the two tunnels cut through
    /// the columns, and the summit slab. Three LODs, built once per island when the landmark blob changes. Seed-derived,
    /// so solo, host and client all draw the same tower.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class SaltTowerSystem : SystemBase
    {
        const float Lod0Distance = 250f, Lod1Distance = 1200f;

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
                // W-77b (#84): a copy of the Colossus material tinted like the basalt (vertex colour x tint, terrain
                // haze), so the tower fades into the fog from high up like the ground and the sunken temple.
                var colossus = Resources.Load<Material>("Colossus");
                var basalt = Resources.Load<Material>("SaltTowerBasalt");
                if (colossus == null)
                {
                    if (!warned)
                        Debug.LogWarning("[landmarks] Colossus material missing from Resources");
                    warned = true;
                    return;
                }
                material = new Material(colossus) { name = "SaltTowerHazed" };
                material.SetColor("_BaseColor", basalt != null && basalt.HasProperty("_BaseColor") ? basalt.GetColor("_BaseColor") : new Color(0.32f, 0.31f, 0.33f));
            }
            var cam = Camera.main;
            if (cam == null)
                return;
            var d = Vector3.Distance(cam.transform.position, bounds.ClosestPoint(cam.transform.position));
            var lod = d < Lod0Distance ? 0 : d < Lod1Distance ? 1 : 2;
            var rp = new RenderParams(material) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            Graphics.RenderMesh(rp, lods[lod], 0, Matrix4x4.Translate(origin));
            Punchy.Rendering.WideShadow.Cast(lods[2], Matrix4x4.Translate(origin));   // R-5: past the real shadows it casts through the wide shadow map
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
                var h = set.Types[inst.Type].TowerHeight;
                if (h <= 0f)
                    continue;
                var l = SaltTowerShape.Layout(inst, h);
                origin = new Vector3(l.Centre.x, l.Base, l.Centre.y);
                lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var b = new Builder();
                    BuildLod(b, l, inst.Key, lod);
                    lods[lod] = b.ToMesh($"SaltTower LOD{lod}");
                    LodTris[lod] = b.Tris;
                }
                bounds = lods[0].bounds;
                bounds.center += origin;
                Debug.Log($"[landmarks] SaltTower meshes LOD0 {LodTris[0]} tris, LOD1 {LodTris[1]}, LOD2 {LodTris[2]}");
                return;
            }
        }

        /// <summary>One column of the body: centre (local), corner radius, twist.</summary>
        struct Column
        {
            public float2 At;
            public float Radius, Twist;
        }

        static void BuildLod(Builder b, in SaltTowerLayout l, uint key, int lod)
        {
            var rng = new Unity.Mathematics.Random(key | 1u);
            var top = l.Height - SaltTowerShape.SummitThick + 0.05f;
            var bottom = -3f;
            if (lod == 2)
            {
                b.Prism(float2.zero, SaltTowerShape.BodyRadius + 0.2f, 0.3f, bottom, top, 6, true, false);
            }
            else
            {
                // A thick centre column ringed by eight, each jittered so the cluster looks grown, not machined.
                var columns = new List<Column> { new Column { At = rng.NextFloat2(-0.3f, 0.3f), Radius = 3.4f, Twist = rng.NextFloat(1f) } };
                for (int k = 0; k < 8; k++)
                {
                    var a = k * math.PI / 4f + rng.NextFloat(-0.12f, 0.12f);
                    var r = 4.9f + rng.NextFloat(-0.2f, 0.2f);
                    columns.Add(new Column { At = r * new float2(math.cos(a), math.sin(a)), Radius = 2.7f + rng.NextFloat(-0.15f, 0.15f), Twist = rng.NextFloat(1f) });
                }
                // Tunnel bands: the columns crossing each tunnel's corridor are left out over its height.
                var bands = new List<(float2 a, float2 b, float lo, float hi)>();
                for (int t = 0; t < 2; t++)
                {
                    SaltTowerShape.Tunnel(l, t, out var ta, out var tb, out var first);
                    float lo = SaltTowerShape.TunnelFloor(l, first, 0f) - l.Base - 0.4f, hi = SaltTowerShape.TunnelFloor(l, first, 1f) - l.Base + SaltTowerShape.TunnelHeight;
                    bands.Add((ta - l.Centre, tb - l.Centre, lo, hi));
                }
                foreach (var c in columns)
                {
                    var spans = new List<float2> { new float2(bottom, top) };
                    foreach (var band in bands)
                    {
                        var ab = band.b - band.a;
                        var s = math.saturate(math.dot(c.At - band.a, ab) / math.lengthsq(ab));
                        if (math.distance(c.At, band.a + ab * s) > SaltTowerShape.TunnelHalfWidth + 0.6f * 0.866f * c.Radius)
                            continue;
                        var next = new List<float2>();
                        foreach (var sp in spans)
                        {
                            if (band.lo > sp.x) next.Add(new float2(sp.x, math.min(sp.y, band.lo)));
                            if (band.hi < sp.y) next.Add(new float2(math.max(sp.x, band.hi), sp.y));
                        }
                        spans = next;
                    }
                    foreach (var sp in spans)
                    {
                        if (lod == 1)
                        {
                            b.Prism(c.At, c.Radius, c.Twist, sp.x, sp.y, 6, true, false);
                            continue;
                        }
                        // Blocks of 4-7 m, each a touch narrower and turned, so the joints read.
                        var y = sp.x;
                        while (y < sp.y - 0.5f)
                        {
                            var y1 = math.min(sp.y, y + rng.NextFloat(4f, 7f));
                            if (sp.y - y1 < 1.5f)
                                y1 = sp.y;
                            b.Prism(c.At, c.Radius * rng.NextFloat(0.96f, 1f), c.Twist + rng.NextFloat(-0.03f, 0.03f), y, y1, 6, true, y > sp.x);
                            y = y1;
                        }
                    }
                }
            }

            // Steps: LOD0 every step; LOD1 one stub per three; LOD2 one per six.
            var stride = lod == 0 ? 1 : lod == 1 ? 3 : 6;
            for (int i = 0; i < l.Steps; i += stride)
            {
                var j = math.min(l.Steps - 1, i + stride / 2);
                SaltTowerShape.Step(l, j, out var xz, out var stepTop);
                var y1 = stepTop - l.Base;
                b.Prism(xz - l.Centre, SaltTowerShape.StepDrawRadius, rng.NextFloat(1f), y1 - SaltTowerShape.StepThick, y1, 6, true, lod == 0);
            }
            // The summit slab: twelve-sided so its edge stays inside the standing area.
            b.Prism(float2.zero, SaltTowerShape.SummitRadius, 0f, l.Height - SaltTowerShape.SummitThick, l.Height, lod == 2 ? 6 : 12, true, true);
        }

        /// <summary>Collects flat-shaded prisms into one mesh.</summary>
        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<int> idx = new List<int>();
            public int Tris => idx.Count / 3;

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var normal = Vector3.Cross(b - a, c - a).normalized;
                var s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
                idx.Add(s); idx.Add(s + 2); idx.Add(s + 3);
            }

            void Cap(float2 c, float radius, float twist, float y, int sides, bool up)
            {
                var s = v.Count;
                for (int k = 0; k < sides; k++)
                {
                    var a = (k + twist) * 2f * math.PI / sides;
                    v.Add(new Vector3(c.x + radius * math.cos(a), y, c.y + radius * math.sin(a)));
                    n.Add(up ? Vector3.up : Vector3.down);
                }
                for (int k = 1; k < sides - 1; k++)
                {
                    if (up) { idx.Add(s); idx.Add(s + k + 1); idx.Add(s + k); }
                    else { idx.Add(s); idx.Add(s + k); idx.Add(s + k + 1); }
                }
            }

            /// <summary>A prism of the given sides (corner radius), from y0 to y1, with a top and optionally a bottom.</summary>
            public void Prism(float2 c, float radius, float twist, float y0, float y1, int sides, bool top, bool bottomCap)
            {
                for (int k = 0; k < sides; k++)
                {
                    float a0 = (k + twist) * 2f * math.PI / sides, a1 = (k + 1 + twist) * 2f * math.PI / sides;
                    var p0 = new Vector3(c.x + radius * math.cos(a0), 0f, c.y + radius * math.sin(a0));
                    var p1 = new Vector3(c.x + radius * math.cos(a1), 0f, c.y + radius * math.sin(a1));
                    // Outward-facing with clockwise winding seen from outside (Unity's front faces).
                    Quad(p0 + Vector3.up * y0, p0 + Vector3.up * y1, p1 + Vector3.up * y1, p1 + Vector3.up * y0);
                }
                if (top) Cap(c, radius, twist, y1, sides, true);
                if (bottomCap) Cap(c, radius, twist, y0, sides, false);
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(System.Linq.Enumerable.ToList(System.Linq.Enumerable.Repeat(Color.white, v.Count))); // Colossus reads vertex colour
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
