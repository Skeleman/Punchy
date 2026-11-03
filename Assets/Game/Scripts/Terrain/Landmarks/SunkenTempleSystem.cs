using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the sunken temple (W-32) from SunkenTempleShape, so what's drawn is what's walked on: the sand mound with
    /// the trench and light shafts cut into it, the portico, ziggurat tiers and column stumps poking out (three LODs),
    /// and, only while the camera is in or near the temple, the interior: every room's floor, walls (with openings
    /// where rooms meet), ceiling (with shaft holes), sand drifts, pillars, fallen columns, altar, statue and eight
    /// sconces, each with a small point light. While the interior is hidden, dark plugs close the door and the shaft
    /// bottoms so nobody sees through the mound. Built once per island when the landmark blob changes; seed-derived,
    /// so solo, host and client all draw the same temple.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class SunkenTempleSystem : SystemBase
    {
        const float Lod0Distance = 120f, Lod1Distance = 1200f;

        /// <summary>The last build's triangle counts: exterior LOD0-2, then the interior.</summary>
        public static int[] LodTris = new int[4];

        /// <summary>Whether the interior was drawn last frame (for tests and reports).</summary>
        public static bool InteriorShown;

        BlobAssetReference<LandmarkSetBlob> builtFrom;
        Mesh[] lods;
        Mesh interior, plug;
        Matrix4x4 matrix;
        Bounds bounds;
        SunkenTempleLayout layout;
        Material stone, sand, innerStone, innerSand, glow;
        Light[] lights;
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
            if (lods == null || !Materials())
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var cp = cam.transform.position;
            var d = Vector3.Distance(cp, bounds.ClosestPoint(cp));
            var lod = d < Lod0Distance ? 0 : d < Lod1Distance ? 1 : 2;
            var q = SunkenTempleShape.Local(layout, cp);
            var inside = lod == 0 && q.x > -40f && q.x < 36f && math.abs(q.z) < 20f && q.y < 20f;
            InteriorShown = inside;

            Draw(lods[lod], 0, stone, ShadowCastingMode.TwoSided);
            Draw(lods[lod], 1, sand, ShadowCastingMode.TwoSided);
            if (inside)
            {
                Draw(interior, 0, innerStone, ShadowCastingMode.Off);
                Draw(interior, 1, innerSand, ShadowCastingMode.Off);
                Draw(interior, 2, glow, ShadowCastingMode.Off);
            }
            else if (lod == 0)
                Draw(plug, 0, innerStone, ShadowCastingMode.Off);
            Lights(inside);
        }

        void Draw(Mesh mesh, int submesh, Material m, ShadowCastingMode shadows)
        {
            var rp = new RenderParams(m) { worldBounds = bounds, shadowCastingMode = shadows, receiveShadows = true };
            Graphics.RenderMesh(rp, mesh, submesh, matrix);
        }

        /// <summary>Tinted copies of the salt tower's material (no Shader.Find), and the emissive sconce glow.</summary>
        bool Materials()
        {
            if (stone != null)
                return true;
            var basalt = Resources.Load<Material>("SaltTowerBasalt");
            if (basalt == null)
            {
                if (!warned)
                    Debug.LogWarning("[landmarks] SaltTowerBasalt material missing from Resources");
                warned = true;
                return false;
            }
            stone = Tint(basalt, new Color(0.72f, 0.58f, 0.40f), "SunkenTempleStone");
            sand = Tint(basalt, new Color(0.80f, 0.67f, 0.46f), "SunkenTempleSand");
            innerStone = Tint(basalt, new Color(0.42f, 0.33f, 0.23f), "SunkenTempleInnerStone");
            innerSand = Tint(basalt, new Color(0.55f, 0.45f, 0.30f), "SunkenTempleInnerSand");
            var g = Resources.Load<Material>("TempleGlow");
            glow = g != null ? g : Tint(basalt, new Color(1f, 0.8f, 0.5f), "SunkenTempleGlow");
            return true;
        }

        static Material Tint(Material from, Color c, string name)
        {
            var m = new Material(from) { name = name };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", 0.08f);
            return m;
        }

        void Lights(bool on)
        {
            if (lights == null)
            {
                if (!on)
                    return;
                lights = new Light[SunkenTempleShape.Sconces];
                for (int k = 0; k < lights.Length; k++)
                {
                    var t = SunkenTempleShape.SconceTable;
                    var j = k * 5;
                    var at = new float3(t[j] + 0.7f * t[j + 3], t[j + 1] + 0.3f, t[j + 2] + 0.7f * t[j + 4]);
                    var go = new GameObject($"SunkenTemple sconce {k}");
                    go.transform.position = SunkenTempleShape.World(layout, at);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 9f;
                    light.intensity = 2.5f;
                    light.color = new Color(1f, 0.66f, 0.35f);
                    light.shadows = LightShadows.None;
                    lights[k] = light;
                }
            }
            foreach (var l in lights)
                if (l != null && l.enabled != on)
                    l.enabled = on;
        }

        protected override void OnDestroy() => Clear();

        void Clear()
        {
            if (lods != null)
                foreach (var m in lods)
                    if (m != null)
                        Object.Destroy(m);
            if (interior != null) Object.Destroy(interior);
            if (plug != null) Object.Destroy(plug);
            if (lights != null)
                foreach (var l in lights)
                    if (l != null)
                        Object.Destroy(l.gameObject);
            lods = null;
            interior = plug = null;
            lights = null;
        }

        void Build(in TerrainSource source)
        {
            Clear();
            ref var set = ref source.Landmarks.Value;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var inst = set.Instances[i];
                if (set.Types[inst.Type].Temple == 0)
                    continue;
                layout = SunkenTempleShape.Layout(inst);
                var l = layout;
                // Mesh frame (u, y, v) to world: columns F, up, R; determinant +1, so winding is kept.
                matrix = new Matrix4x4(
                    new Vector4(l.F.x, 0f, l.F.y, 0f), new Vector4(0f, 1f, 0f, 0f),
                    new Vector4(l.R.x, 0f, l.R.y, 0f), new Vector4(l.Centre.x, l.Base, l.Centre.y, 1f));
                var lo = new Vector3(float.MaxValue, l.Base + SunkenTempleShape.Bottom - 10f, float.MaxValue);
                var hi = new Vector3(float.MinValue, l.Base + 19f, float.MinValue);
                for (int c = 0; c < 4; c++)
                {
                    var w = SunkenTempleShape.World(l, new float3((c & 1) == 0 ? SunkenTempleShape.MinU : SunkenTempleShape.MaxU, 0f, (c & 2) == 0 ? -SunkenTempleShape.MaxV : SunkenTempleShape.MaxV));
                    lo.x = math.min(lo.x, w.x); lo.z = math.min(lo.z, w.z);
                    hi.x = math.max(hi.x, w.x); hi.z = math.max(hi.z, w.z);
                }
                bounds = new Bounds((lo + hi) * 0.5f, hi - lo);

                lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var b = new Builder(2);
                    Exterior(b, lod);
                    lods[lod] = b.ToMesh($"SunkenTemple LOD{lod}");
                    LodTris[lod] = b.Tris;
                }
                var ib = new Builder(3);
                Interior(ib);
                interior = ib.ToMesh("SunkenTemple interior");
                LodTris[3] = ib.Tris;
                var pb = new Builder(1);
                Plugs(pb);
                plug = pb.ToMesh("SunkenTemple plugs");
                Debug.Log($"[landmarks] SunkenTemple meshes LOD0 {LodTris[0]} tris, LOD1 {LodTris[1]}, LOD2 {LodTris[2]}, interior {LodTris[3]}, {SunkenTempleShape.Sconces} sconce lights");
                return;
            }
        }

        static List<float> Steps(float a, float b, float step, List<float> extra = null)
        {
            var s = new List<float>();
            var n = math.max(1, (int)math.ceil((b - a) / step));
            for (int i = 0; i <= n; i++)
                s.Add(math.lerp(a, b, i / (float)n));
            if (extra != null)
                foreach (var e in extra)
                    if (e > a && e < b)
                        s.Add(e);
            s.Sort();
            for (int i = s.Count - 1; i > 0; i--)
                if (s[i] - s[i - 1] < 0.05f)
                    s.RemoveAt(s[i] == b ? i - 1 : i);
            return s;
        }

        /// <summary>The mound (with the trench and shafts cut out at LOD0), its skirt, the trench and the outside ruins.</summary>
        static void Exterior(Builder b, int lod)
        {
            float step = lod == 0 ? 2f : lod == 1 ? 4f : 8f;
            List<float> eu = null, ev = null;
            if (lod == 0)
            {
                eu = new List<float>();
                ev = new List<float>();
                for (int i = 0; i < SunkenTempleShape.FirstShaft + SunkenTempleShape.Shafts; i++)
                {
                    if (i != SunkenTempleShape.Trench && i < SunkenTempleShape.FirstShaft)
                        continue;
                    var r = SunkenTempleShape.GetRoom(i);
                    eu.Add(r.U0); eu.Add(r.U1); ev.Add(r.V0); ev.Add(r.V1);
                }
            }
            var us = Steps(SunkenTempleShape.MinU, SunkenTempleShape.MaxU, step, eu);
            var vs = Steps(-SunkenTempleShape.MaxV, SunkenTempleShape.MaxV, step, ev);
            b.HeightGrid(1, us, vs, SunkenTempleShape.MoundTop,
                lod == 0 ? (u, v) => SunkenTempleShape.InCut(u, v) : (Func<float, float, bool>)null);

            // Skirt down from the mound's edge (at Bottom there) so no gap shows on slopes.
            float y0 = SunkenTempleShape.Bottom - 10f, y1 = SunkenTempleShape.Bottom;
            float u0 = SunkenTempleShape.MinU, u1 = SunkenTempleShape.MaxU, v0 = -SunkenTempleShape.MaxV, v1 = SunkenTempleShape.MaxV;
            b.Panel(1, 0, u0, v0, v1, y0, y1, Vector3.left);
            b.Panel(1, 0, u1, v0, v1, y0, y1, Vector3.right);
            b.Panel(1, 1, v0, u0, u1, y0, y1, Vector3.back);
            b.Panel(1, 1, v1, u0, u1, y0, y1, Vector3.forward);

            if (lod == 0)
            {
                // Trench: sand floor, side walls up to the mound's top, the end wall round the door.
                var t = SunkenTempleShape.GetRoom(SunkenTempleShape.Trench);
                var tu = Steps(t.U0, t.U1, 0.42f);
                b.HeightGrid(1, tu, Steps(t.V0, t.V1, 0.4f), (u, v) => t.FloorAt(u) + SunkenTempleShape.Sand(SunkenTempleShape.Trench, u, v), null);
                for (int s = 0; s < 2; s++)
                {
                    var v = s == 0 ? t.V0 : t.V1;
                    var n = s == 0 ? Vector3.forward : Vector3.back;
                    for (int i = 0; i + 1 < tu.Count; i++)
                    {
                        float a = tu[i], c = tu[i + 1];
                        b.Quad(0, new Vector3(a, t.FloorAt(a) + SunkenTempleShape.Sand(0, a, v), v), new Vector3(c, t.FloorAt(c) + SunkenTempleShape.Sand(0, c, v), v),
                            new Vector3(c, SunkenTempleShape.MoundTop(c, v), v), new Vector3(a, SunkenTempleShape.MoundTop(a, v), v), n);
                    }
                }
                var door = SunkenTempleShape.GetRoom(1);
                b.Panel(0, 0, t.U1, t.V0, t.V1, t.Floor, SunkenTempleShape.Top, Vector3.left,
                    new List<float4> { new float4(door.V0, door.V1, door.Floor, door.Ceil) });
                // Shaft walls from the room ceiling up through the mound.
                for (int i = SunkenTempleShape.FirstShaft; i < SunkenTempleShape.FirstShaft + SunkenTempleShape.Shafts; i++)
                {
                    var r = SunkenTempleShape.GetRoom(i);
                    var top = SunkenTempleShape.MoundTop(0.5f * (r.U0 + r.U1), 0.5f * (r.V0 + r.V1));
                    b.Panel(0, 0, r.U0, r.V0, r.V1, r.Floor, top, Vector3.right);
                    b.Panel(0, 0, r.U1, r.V0, r.V1, r.Floor, top, Vector3.left);
                    b.Panel(0, 1, r.V0, r.U0, r.U1, r.Floor, top, Vector3.forward);
                    b.Panel(0, 1, r.V1, r.U0, r.U1, r.Floor, top, Vector3.back);
                }
            }
            for (int k = 0; k < SunkenTempleShape.Boxes; k++)
            {
                SunkenTempleShape.Box(k, out var uv, out var y, out var draw, out var outside);
                if (!outside || lod == 2 && draw != 0)
                    continue;
                b.Solid(0, uv, y, draw, lod == 0 ? 8 : 6);
            }
        }

        /// <summary>Every room's floor, walls with openings, ceiling with shaft holes and drifts, the inside boxes and the sconces.</summary>
        static void Interior(Builder b)
        {
            for (int i = 0; i < SunkenTempleShape.Rooms; i++)
            {
                var r = SunkenTempleShape.GetRoom(i);
                if (r.Kind != 0)
                    continue;
                b.Panel(0, 2, r.Floor, r.U0, r.U1, r.V0, r.V1, Vector3.up);
                var holes = new List<float4>();
                for (int j = SunkenTempleShape.FirstShaft; j < SunkenTempleShape.FirstShaft + SunkenTempleShape.Shafts; j++)
                {
                    var s = SunkenTempleShape.GetRoom(j);
                    if (math.abs(s.Floor - r.Ceil) < 1e-3f && s.U0 >= r.U0 && s.U1 <= r.U1 && s.V0 >= r.V0 && s.V1 <= r.V1)
                        holes.Add(new float4(s.U0, s.U1, s.V0, s.V1));
                }
                b.Panel(0, 2, r.Ceil, r.U0, r.U1, r.V0, r.V1, Vector3.down, holes);
                Wall(b, i, r, 0, r.U0, Vector3.right);
                Wall(b, i, r, 0, r.U1, Vector3.left);
                Wall(b, i, r, 1, r.V0, Vector3.forward);
                Wall(b, i, r, 1, r.V1, Vector3.back);
            }
            for (int k = 0; k < SunkenTempleShape.Drifts; k++)
            {
                SunkenTempleShape.Drift(k, out var e, out _, out var room);
                var r = SunkenTempleShape.GetRoom(room);
                if (r.Kind != 0)
                    continue;
                float a0 = math.max(r.U0, e.x - e.z), a1 = math.min(r.U1, e.x + e.z), c0 = math.max(r.V0, e.y - e.w), c1 = math.min(r.V1, e.y + e.w);
                b.HeightGrid(1, Steps(a0, a1, 0.4f), Steps(c0, c1, 0.4f),
                    (u, v) => r.FloorAt(u) + SunkenTempleShape.Sand(room, u, v) + 0.02f,
                    (u, v) => SunkenTempleShape.Sand(room, u, v) < 0.01f);
            }
            for (int k = 0; k < SunkenTempleShape.Boxes; k++)
            {
                SunkenTempleShape.Box(k, out var uv, out var y, out var draw, out var outside);
                if (!outside)
                    b.Solid(0, uv, y, draw, 8);
            }
            var t = SunkenTempleShape.SconceTable;
            for (int k = 0; k < SunkenTempleShape.Sconces; k++)
            {
                var j = k * 5;
                var w = new float3(t[j], t[j + 1], t[j + 2]);
                var n = new float3(t[j + 3], 0f, t[j + 4]);
                var side = new float3(-n.z, 0f, n.x);
                // Bracket: 0.22 deep, 0.3 wide; the glowing bowl sits on it.
                var a = w + side * 0.15f + new float3(0f, -0.2f, 0f);
                var c = w - side * 0.15f + n * 0.22f + new float3(0f, 0.15f, 0f);
                b.Solid(0, new float4(math.min(a.x, c.x), math.max(a.x, c.x), math.min(a.z, c.z), math.max(a.z, c.z)), new float2(a.y, c.y), 0, 8);
                var g = w + n * 0.16f + new float3(0f, 0.25f, 0f);
                b.Solid(2, new float4(g.x - 0.09f, g.x + 0.09f, g.z - 0.09f, g.z + 0.09f), new float2(g.y - 0.09f, g.y + 0.09f), 0, 8);
            }
        }

        /// <summary>Room i's wall on axis (0: constant u, 1: constant v) at c, open wherever another room meets it.</summary>
        static void Wall(Builder b, int i, in SunkenTempleShape.Room r, int axis, float c, Vector3 normal)
        {
            float s0 = axis == 0 ? r.V0 : r.U0, s1 = axis == 0 ? r.V1 : r.U1;
            var holes = new List<float4>();
            for (int j = 0; j < SunkenTempleShape.Rooms; j++)
            {
                var o = SunkenTempleShape.GetRoom(j);
                if (j == i || o.Kind == 2)
                    continue;
                float face0 = axis == 0 ? o.U0 : o.V0, face1 = axis == 0 ? o.U1 : o.V1;
                if (math.abs(face0 - c) > 1e-3f && math.abs(face1 - c) > 1e-3f)
                    continue;
                float o0 = math.max(s0, axis == 0 ? o.V0 : o.U0), o1 = math.min(s1, axis == 0 ? o.V1 : o.U1);
                float f = math.max(r.Floor, o.FloorAt(math.abs(face0 - c) <= 1e-3f ? o.U0 : o.U1)), top = math.min(r.Ceil, o.Ceil);
                if (o1 - o0 > 0.01f && top > f)
                    holes.Add(new float4(o0, o1, f, top));
            }
            b.Panel(0, axis, c, s0, s1, r.Floor, r.Ceil, normal, holes);
        }

        /// <summary>Dark plugs shown while the interior is hidden: across the door and at each shaft's bottom.</summary>
        static void Plugs(Builder b)
        {
            var door = SunkenTempleShape.GetRoom(1);
            b.Panel(0, 0, -18.3f, door.V0, door.V1, door.Floor, door.Ceil, Vector3.left);
            for (int i = SunkenTempleShape.FirstShaft; i < SunkenTempleShape.FirstShaft + SunkenTempleShape.Shafts; i++)
            {
                var r = SunkenTempleShape.GetRoom(i);
                b.Panel(0, 2, r.Floor + 0.05f, r.U0, r.U1, r.V0, r.V1, Vector3.up);
            }
        }

        /// <summary>Collects faces into one mesh with several submeshes. Positions are in the temple frame (u, y, v).</summary>
        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<int>[] sub;

            public Builder(int submeshes)
            {
                sub = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++)
                    sub[i] = new List<int>();
            }

            public int Tris
            {
                get
                {
                    var t = 0;
                    foreach (var s in sub)
                        t += s.Count / 3;
                    return t;
                }
            }

            int Vert(Vector3 p, Vector3 normal)
            {
                v.Add(p);
                n.Add(normal);
                return v.Count - 1;
            }

            /// <summary>A triangle of existing vertices, wound so its front faces the way 'want' points.</summary>
            void Index(int s, int a, int b, int c, Vector3 want)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) < 0f)
                    (b, c) = (c, b);
                sub[s].Add(a); sub[s].Add(b); sub[s].Add(c);
            }

            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int ia = Vert(a, normal), ib = Vert(b, normal), ic = Vert(c, normal), id = Vert(d, normal);
                Index(s, ia, ib, ic, normal);
                Index(s, ia, ic, id, normal);
            }

            void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 normal) =>
                Index(s, Vert(a, normal), Vert(b, normal), Vert(c, normal), normal);

            static Vector3 P(int axis, float c, float s, float t) =>
                axis == 0 ? new Vector3(c, t, s) : axis == 1 ? new Vector3(s, t, c) : new Vector3(s, c, t);

            /// <summary>A flat rectangle at c on axis (0 u, 1 v, 2 y) over s0..s1, t0..t1, minus the holes (s0, s1, t0, t1).</summary>
            public void Panel(int sm, int axis, float c, float s0, float s1, float t0, float t1, Vector3 normal, List<float4> holes = null)
            {
                if (s1 - s0 < 1e-4f || t1 - t0 < 1e-4f)
                    return;
                var ss = new List<float> { s0, s1 };
                var ts = new List<float> { t0, t1 };
                if (holes != null)
                    foreach (var h in holes)
                    {
                        if (h.x > s0 && h.x < s1) ss.Add(h.x);
                        if (h.y > s0 && h.y < s1) ss.Add(h.y);
                        if (h.z > t0 && h.z < t1) ts.Add(h.z);
                        if (h.w > t0 && h.w < t1) ts.Add(h.w);
                    }
                ss.Sort();
                ts.Sort();
                for (int i = 0; i + 1 < ss.Count; i++)
                    for (int j = 0; j + 1 < ts.Count; j++)
                    {
                        float a = ss[i], b = ss[i + 1], e = ts[j], f = ts[j + 1];
                        if (b - a < 1e-4f || f - e < 1e-4f)
                            continue;
                        float ms = 0.5f * (a + b), mt = 0.5f * (e + f);
                        var cut = false;
                        if (holes != null)
                            foreach (var h in holes)
                                cut |= ms > h.x && ms < h.y && mt > h.z && mt < h.w;
                        if (!cut)
                            Quad(sm, P(axis, c, a, e), P(axis, c, b, e), P(axis, c, b, f), P(axis, c, a, f), normal);
                    }
            }

            /// <summary>A smooth heightfield over the grid lines us x vs, skipping cells whose centre 'skip' rejects.</summary>
            public void HeightGrid(int s, List<float> us, List<float> vs, Func<float, float, float> height, Func<float, float, bool> skip)
            {
                if (us.Count < 2 || vs.Count < 2)
                    return;
                var id = new int[us.Count, vs.Count];
                for (int i = 0; i < us.Count; i++)
                    for (int j = 0; j < vs.Count; j++)
                    {
                        float u = us[i], w = vs[j], e = 0.25f;
                        var du = height(u + e, w) - height(u - e, w);
                        var dv = height(u, w + e) - height(u, w - e);
                        id[i, j] = Vert(new Vector3(u, height(u, w), w), new Vector3(-du, 2f * e, -dv).normalized);
                    }
                for (int i = 0; i + 1 < us.Count; i++)
                    for (int j = 0; j + 1 < vs.Count; j++)
                    {
                        if (skip != null && skip(0.5f * (us[i] + us[i + 1]), 0.5f * (vs[j] + vs[j + 1])))
                            continue;
                        Index(s, id[i, j], id[i + 1, j], id[i + 1, j + 1], Vector3.up);
                        Index(s, id[i, j], id[i + 1, j + 1], id[i, j + 1], Vector3.up);
                    }
            }

            static Vector3 Ax(int axis, float along, float p, float q) =>
                axis == 0 ? new Vector3(along, p, q) : axis == 1 ? new Vector3(p, along, q) : new Vector3(p, q, along);

            /// <summary>A solid from the shape's box table: a box (draw 0) or a drum fitted inside it along u, y or v.</summary>
            public void Solid(int s, float4 uv, float2 y, int draw, int sides)
            {
                if (draw == 0)
                {
                    float u0 = uv.x, u1 = uv.y, v0 = uv.z, v1 = uv.w;
                    Panel(s, 2, y.y, u0, u1, v0, v1, Vector3.up);
                    Panel(s, 2, y.x, u0, u1, v0, v1, Vector3.down);
                    Panel(s, 0, u0, v0, v1, y.x, y.y, Vector3.left);
                    Panel(s, 0, u1, v0, v1, y.x, y.y, Vector3.right);
                    Panel(s, 1, v0, u0, u1, y.x, y.y, Vector3.back);
                    Panel(s, 1, v1, u0, u1, y.x, y.y, Vector3.forward);
                    return;
                }
                // axis 0 u (p = y, q = v), 1 y (p = u, q = v), 2 v (p = u, q = y)
                var axis = draw == 2 ? 0 : draw == 1 ? 1 : 2;
                float a0, a1, cp, cq, hp, hq;
                if (axis == 0) { a0 = uv.x; a1 = uv.y; cp = 0.5f * (y.x + y.y); cq = 0.5f * (uv.z + uv.w); hp = 0.5f * (y.y - y.x); hq = 0.5f * (uv.w - uv.z); }
                else if (axis == 1) { a0 = y.x; a1 = y.y; cp = 0.5f * (uv.x + uv.y); cq = 0.5f * (uv.z + uv.w); hp = 0.5f * (uv.y - uv.x); hq = 0.5f * (uv.w - uv.z); }
                else { a0 = uv.z; a1 = uv.w; cp = 0.5f * (uv.x + uv.y); cq = 0.5f * (y.x + y.y); hp = 0.5f * (uv.y - uv.x); hq = 0.5f * (y.y - y.x); }
                // Flats at the box's half width: the drum stays inside what collides.
                var radius = math.min(hp, hq) / math.cos(math.PI / sides);
                for (int k = 0; k < sides; k++)
                {
                    float t0 = (k + 0.5f) * 2f * math.PI / sides, t1 = (k + 1.5f) * 2f * math.PI / sides, tm = 0.5f * (t0 + t1);
                    float p0 = cp + radius * math.cos(t0), q0 = cq + radius * math.sin(t0), p1 = cp + radius * math.cos(t1), q1 = cq + radius * math.sin(t1);
                    var out_ = Ax(axis, 0f, math.cos(tm), math.sin(tm));
                    Quad(s, Ax(axis, a0, p0, q0), Ax(axis, a0, p1, q1), Ax(axis, a1, p1, q1), Ax(axis, a1, p0, q0), out_);
                    Tri(s, Ax(axis, a1, cp, cq), Ax(axis, a1, p0, q0), Ax(axis, a1, p1, q1), Ax(axis, 1f, 0f, 0f));
                    Tri(s, Ax(axis, a0, cp, cq), Ax(axis, a0, p0, q0), Ax(axis, a0, p1, q1), Ax(axis, -1f, 0f, 0f));
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.subMeshCount = sub.Length;
                for (int i = 0; i < sub.Length; i++)
                    m.SetTriangles(sub[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
