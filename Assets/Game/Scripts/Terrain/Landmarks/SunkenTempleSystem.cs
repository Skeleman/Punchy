using System;
using System.Collections.Generic;
using Punchy.Ambience;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Punchy.Terrain
{
    /// <summary>
    /// Draws the sunken temple (W-32, rebuilt from the ruin kit in W-56) from SunkenTempleShape and the blob's ruin
    /// arrays, so what's drawn is what's walked on. The sand swell is ground now (TerrainSource), so the exterior is
    /// only the entrance cut (its stepped floor, the walls down to the door and a kerb of coping stones hiding the
    /// cut terrain edge) and the boxes that stand in the sand (portico, entrance pillars and lintel, ziggurat tiers,
    /// column stumps), in three LODs. Only while the camera is in or near the temple, the interior: every closed
    /// room's floor, walls (with openings where rooms meet), ceiling, sand drifts, pillars, fallen columns, altar,
    /// statue and the wall torches, which burn with FireSystem flames and light the rooms through CaveSystem's pooled
    /// moving lights (no Light components). While the interior is hidden, dark plugs close the door. Built once per
    /// island when the landmark blob changes; seed-derived, so solo, host and client all draw the same temple.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class SunkenTempleSystem : SystemBase
    {
        int lod = -1;   // last frame's level (R-6 row 1: picked by on-screen size, DetailPolicy.LandmarkLod)

        /// <summary>The last build's triangle counts: exterior LOD0-2, then the interior.</summary>
        public static int[] LodTris = new int[4];

        /// <summary>Whether the interior was drawn last frame, and how many torches burned (for tests and reports).</summary>
        public static bool InteriorShown;
        public static int TorchesLit;

        BlobAssetReference<LandmarkSetBlob> builtFrom;
        Mesh[] lods;
        Mesh interior, plug;
        Matrix4x4 matrix;
        Bounds bounds;
        SunkenTempleLayout layout;
        Material stone, sand, innerStone, innerSand, glow;
        float3[] torches;
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
            TorchesLit = 0;
            if (lods == null || !Materials())
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var cp = cam.transform.position;
            Punchy.Settings.DetailPolicy.SetView(cam);
            var d = Vector3.Distance(cp, bounds.ClosestPoint(cp));
            var q = SunkenTempleShape.Local(layout, cp);
            var near = q.x > -70f && q.x < 66f && math.abs(q.z) < 50f && q.y < 40f;
            // Within the interior's box the full mesh always draws (its door and the interior belong to LOD 0).
            lod = near ? 0 : Punchy.Settings.DetailPolicy.LandmarkLod(bounds.size.magnitude, d, lod);
            // W-113 (item 12): the interior shows from well outside the door (30 m round the ruin), so the plugs that
            // close the door while it's hidden never stand between a chase camera and the player walking in (they
            // read as a plane splitting the temple that the player passed through).
            var inside = near;
            InteriorShown = inside;

            Draw(lods[lod], 0, stone, ShadowCastingMode.TwoSided);
            Draw(lods[lod], 1, sand, ShadowCastingMode.TwoSided);
            if (inside)
            {
                Draw(interior, 0, innerStone, ShadowCastingMode.Off);
                Draw(interior, 1, innerSand, ShadowCastingMode.Off);
                Draw(interior, 2, glow, ShadowCastingMode.Off);
                Torches();
            }
            else if (lod == 0)
                Draw(plug, 0, innerStone, ShadowCastingMode.Off);
            Punchy.Rendering.WideShadow.Cast(lods[2], matrix);   // R-5: past the real shadows it casts through the wide shadow map
        }

        /// <summary>The wall torches: a wood flame each (FireSystem) and a flickering fixture light (LightBudget, refactor 3.1).</summary>
        void Torches()
        {
            if (torches == null)
                return;
            var t = (float)SystemAPI.Time.ElapsedTime;
            for (int k = 0; k < torches.Length; k++)
            {
                var p = (Vector3)torches[k];
                FireSystem.AddFlame(p, 0.3f, 0x7E00 + k, FireSystem.Wood);
                var flicker = 0.85f + 0.1f * math.sin(t * 9.1f + k * 2.3f) + 0.05f * math.sin(t * 23.7f + k * 5.1f);
                Punchy.Settings.LightBudget.Add(Punchy.Settings.LightKind.Fixture, 0x7E0000 + k, p + Vector3.up * 0.25f, FireSystem.Light(FireSystem.Wood, flicker));
            }
            TorchesLit = torches.Length;
        }

        void Draw(Mesh mesh, int submesh, Material m, ShadowCastingMode shadows)
        {
            var rp = new RenderParams(m) { worldBounds = bounds, shadowCastingMode = shadows, receiveShadows = true };
            Graphics.RenderMesh(rp, mesh, submesh, matrix);
        }

        /// <summary>Tinted copies of the salt tower's material (no Shader.Find), and the emissive torch glow.</summary>
        bool Materials()
        {
            if (stone != null)
                return true;
            // W-77: tinted copies of the Colossus material (vertex colour x tint, terrain haze), so the ruins fade into
            // the fog from high up like the ground around them.
            // W-113 (item 12): the temple's own material (Punchy/SunkenTemple: the same haze, plus sandstone blocks or
            // sand ripples and the torches' glow light); the plain Colossus one if it's missing.
            var basalt = Resources.Load<Material>("SunkenTemple");
            if (basalt == null)
                basalt = Resources.Load<Material>("Colossus");
            if (basalt == null)
            {
                if (!warned)
                    Debug.LogWarning("[landmarks] Colossus material missing from Resources");
                warned = true;
                return false;
            }
            stone = Tint(basalt, new Color(0.72f, 0.58f, 0.40f), "SunkenTempleStone");
            sand = Tint(basalt, new Color(0.80f, 0.67f, 0.46f), "SunkenTempleSand", false);
            innerStone = Tint(basalt, new Color(0.42f, 0.33f, 0.23f), "SunkenTempleInnerStone");
            innerSand = Tint(basalt, new Color(0.55f, 0.45f, 0.30f), "SunkenTempleInnerSand", false);
            var g = Resources.Load<Material>("TempleGlow");
            glow = g != null ? g : Tint(basalt, new Color(1f, 0.8f, 0.5f), "SunkenTempleGlow");
            return true;
        }

        static Material Tint(Material from, Color c, string name, bool blocks = true)
        {
            var m = new Material(from) { name = name };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Blocks"))
                m.SetFloat("_Blocks", blocks ? 1f : 0f);
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", 0.08f);
            return m;
        }

        protected override void OnDestroy()
        {
            Clear();
        }

        void Clear()
        {
            if (lods != null)
                foreach (var m in lods)
                    if (m != null)
                        Object.Destroy(m);
            if (interior != null) Object.Destroy(interior);
            if (plug != null) Object.Destroy(plug);
            lods = null;
            interior = plug = null;
            torches = null;
        }

        void Build(in TerrainSource source)
        {
            Clear();
            ref var set = ref source.Landmarks.Value;
            if (set.RuinRooms.Length == 0)
                return;
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
                    var w = SunkenTempleShape.World(l, new float3((c & 1) == 0 ? SunkenTempleShape.RuinU0 : SunkenTempleShape.RuinU1, 0f, (c & 2) == 0 ? -SunkenTempleShape.RuinV : SunkenTempleShape.RuinV));
                    lo.x = math.min(lo.x, w.x); lo.z = math.min(lo.z, w.z);
                    hi.x = math.max(hi.x, w.x); hi.z = math.max(hi.z, w.z);
                }
                bounds = new Bounds((lo + hi) * 0.5f, hi - lo);

                var ruin = new Ruin(ref set, source, l);
                lods = new Mesh[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var b = new Builder(2);
                    ruin.Exterior(b, lod, inst.Key);
                    lods[lod] = b.ToMesh($"SunkenTemple LOD{lod}");
                    LodTris[lod] = b.Tris;
                }
                var ib = new Builder(3);
                ruin.Interior(ib);
                interior = ib.ToMesh("SunkenTemple interior");
                LodTris[3] = ib.Tris;
                var pb = new Builder(1);
                ruin.Plugs(pb);
                plug = pb.ToMesh("SunkenTemple plug");
                torches = new float3[ruin.Sconces.Length];
                for (int k = 0; k < torches.Length; k++)
                {
                    var s = ruin.Sconces[k];
                    torches[k] = SunkenTempleShape.World(l, s.At + new float3(s.N.x, 0f, s.N.y) * 0.32f + new float3(0f, 0.42f, 0f));
                }
                Debug.Log($"[landmarks] sunken temple: {ruin.Rooms.Length} rooms, {ruin.Boxes.Length} boxes, {torches.Length} torches; tris LOD0-2 {LodTris[0]}/{LodTris[1]}/{LodTris[2]}, interior {LodTris[3]}");
                lod = -1;
                var reach = Punchy.Settings.DetailPolicy.LandmarkReach(bounds.size.magnitude);
                Debug.Log($"[landmarks] sunken temple {bounds.size.magnitude:F0} m across, LOD switches at {reach.x:F0}/{reach.y:F0} m (was 120/1200)");
                return;
            }
        }

        /// <summary>The ruin arrays copied out of the blob, and the meshes built from them (mesh frame u, y, v).</summary>
        class Ruin
        {
            public readonly RuinRoom[] Rooms;
            public readonly RuinBox[] Boxes;
            public readonly RuinDrift[] Drifts;
            public readonly RuinSconce[] Sconces;
            readonly TerrainSource source;
            readonly SunkenTempleLayout layout;

            public Ruin(ref LandmarkSetBlob set, in TerrainSource source, SunkenTempleLayout layout)
            {
                Rooms = set.RuinRooms.ToArray();
                Boxes = set.RuinBoxes.ToArray();
                Drifts = set.RuinDrifts.ToArray();
                Sconces = set.RuinSconces.ToArray();
                this.source = source;
                this.layout = layout;
            }

            /// <summary>The ground's height in the temple frame at (u, v).</summary>
            float Ground(float u, float v) => source.SampleHeight(SunkenTempleShape.World(layout, new float3(u, 0f, v)).xz) - layout.Base;

            float Sand(int room, float u, float v)
            {
                var s = 0f;
                foreach (var d in Drifts)
                {
                    if (d.Room != room)
                        continue;
                    float du = (u - d.Ellipse.x) / d.Ellipse.z, dv = (v - d.Ellipse.y) / d.Ellipse.w;
                    s += d.Height * math.smoothstep(0f, 1f, 1f - du * du - dv * dv);
                }
                return s;
            }

            /// <summary>The room (other than 'self') containing a point, or -1.</summary>
            int RoomAt(float u, float v, int self)
            {
                for (int i = 0; i < Rooms.Length; i++)
                    if (i != self && Rooms[i].Contains(u, v))
                        return i;
                return -1;
            }

            /// <summary>
            /// Walks room i's four edges in pieces split at every other room's edge (and every half metre, so a wall can
            /// follow the ground), calling back with the edge (0 -u, 1 +u, 2 -v, 3 +v), the piece's ends and the
            /// room just outside it (or -1).
            /// </summary>
            void Edges(int i, Action<int, float, float, int> piece)
            {
                var r = Rooms[i];
                for (int e = 0; e < 4; e++)
                {
                    bool alongU = e >= 2;
                    float s0 = alongU ? r.U0 : r.V0, s1 = alongU ? r.U1 : r.V1;
                    var cuts = new List<float> { s0, s1 };
                    for (float s = math.ceil(s0 * 2f) * 0.5f; s < s1; s += 0.5f)
                        cuts.Add(s);
                    foreach (var o in Rooms)
                    {
                        cuts.Add(alongU ? o.U0 : o.V0);
                        cuts.Add(alongU ? o.U1 : o.V1);
                    }
                    cuts.Sort();
                    var c = e == 0 ? r.U0 - 0.05f : e == 1 ? r.U1 + 0.05f : e == 2 ? r.V0 - 0.05f : r.V1 + 0.05f;
                    for (int k = 0; k + 1 < cuts.Count; k++)
                    {
                        float a = math.max(cuts[k], s0), b = math.min(cuts[k + 1], s1);
                        if (b - a < 1e-3f)
                            continue;
                        var m = 0.5f * (a + b);
                        piece(e, a, b, alongU ? RoomAt(m, c, i) : RoomAt(c, m, i));
                    }
                }
            }

            /// <summary>A wall on room i's edge e over s0..s1, y0..y1, facing into the room.</summary>
            void Wall(Builder b, int sm, int i, int e, float s0, float s1, float y0, float y1)
            {
                if (y1 - y0 < 1e-3f)
                    return;
                var r = Rooms[i];
                switch (e)
                {
                    case 0: b.Panel(sm, 0, r.U0, s0, s1, y0, y1, Vector3.right); break;
                    case 1: b.Panel(sm, 0, r.U1, s0, s1, y0, y1, Vector3.left); break;
                    case 2: b.Panel(sm, 1, r.V0, s0, s1, y0, y1, Vector3.forward); break;
                    default: b.Panel(sm, 1, r.V1, s0, s1, y0, y1, Vector3.back); break;
                }
            }

            float FloorOn(RuinRoom r, int e, float s0, float s1) => e >= 2 ? r.FloorAt(0.5f * (s0 + s1)) : r.FloorAt(e == 0 ? r.U0 : r.U1);

            static readonly List<float> Pair = new List<float>(2);

            /// <summary>A room's floor (with its drifts) as a smooth grid every half metre.</summary>
            void FloorGrid(Builder b, int sm, int i, float lift)
            {
                var r = Rooms[i];
                List<float> us = Steps(r.U0, r.U1), vs = Steps(r.V0, r.V1);
                b.HeightGrid(sm, us, vs, (u, v) => r.FloorAt(u) + Sand(i, u, v) + lift, null);
            }

            static List<float> Steps(float a, float b)
            {
                var n = math.max(1, (int)math.ceil((b - a) / 0.5f));
                var list = new List<float>(n + 1);
                for (int k = 0; k <= n; k++)
                    list.Add(math.lerp(a, b, k / (float)n));
                return list;
            }

            /// <summary>The entrance cut (LOD0 only: floor, walls up to the ground, coping) and the boxes standing outside.</summary>
            public void Exterior(Builder b, int lod, uint key)
            {
                var sides = lod == 0 ? 12 : lod == 1 ? 8 : 6;
                foreach (var box in Boxes)
                    if (box.Outside != 0)
                        b.Solid(0, box.UV, box.Y, box.Draw, sides);
                if (lod == 2)
                    return;
                var rng = new Unity.Mathematics.Random(RuinKit.Scramble(key ^ 0xC0B1u) | 1u);
                for (int i = 0; i < Rooms.Length; i++)
                {
                    var r = Rooms[i];
                    if (r.Kind != 1)
                        continue;
                    FloorGrid(b, 1, i, 0f);
                    Edges(i, (e, s0, s1, n) =>
                    {
                        float f = FloorOn(r, e, s0, s1), m = 0.5f * (s0 + s1);
                        float gu = e == 0 ? r.U0 - 0.3f : e == 1 ? r.U1 + 0.3f : m, gv = e == 2 ? r.V0 - 0.3f : e == 3 ? r.V1 + 0.3f : m;
                        var top = Ground(gu, gv) + 0.05f;
                        if (n < 0)
                        {
                            Wall(b, 0, i, e, s0, s1, f, top);
                            // Coping: a kerb of stone blocks on the rim, a little uneven, hiding the terrain's cut edge.
                            if (lod == 0)
                            {
                                float lift = rng.NextFloat(-0.06f, 0.1f), back = rng.NextFloat(1.1f, 1.5f), inset = rng.NextFloat(0.05f, 0.18f);
                                float y0 = top - 0.5f, y1 = top + 0.05f + lift;
                                var uv = e == 0 ? new float4(r.U0 - back, r.U0 + inset, s0 + 0.03f, s1 - 0.03f)
                                    : e == 1 ? new float4(r.U1 - inset, r.U1 + back, s0 + 0.03f, s1 - 0.03f)
                                    : e == 2 ? new float4(s0 + 0.03f, s1 - 0.03f, r.V0 - back, r.V0 + inset)
                                    : new float4(s0 + 0.03f, s1 - 0.03f, r.V1 - inset, r.V1 + back);
                                b.Solid(0, uv, new float2(y0, y1), 0, 4);
                            }
                            return;
                        }
                        var o = Rooms[n];
                        var of = FloorOn(o, e ^ 1, s0, s1);
                        if (of > f)
                            Wall(b, 0, i, e, s0, s1, f, of);  // a riser up to the next step
                        if (o.Kind == 0)
                            Wall(b, 0, i, e, s0, s1, math.max(f, o.Ceil), top);  // above the door, up to the ground
                    });
                }
            }

            /// <summary>Every closed room: floor, walls (open where rooms meet), ceiling, drifts; inside boxes; torch brackets.</summary>
            public void Interior(Builder b)
            {
                for (int i = 0; i < Rooms.Length; i++)
                {
                    var r = Rooms[i];
                    if (r.Kind != 0)
                        continue;
                    b.Kind = Builder.Flags;   // W-128: the floor in close-laid tiles
                    b.Panel(0, 2, r.Floor, r.U0, r.U1, r.V0, r.V1, Vector3.up);
                    b.Kind = Builder.Ashlar;
                    b.Panel(0, 2, r.Ceil, r.U0, r.U1, r.V0, r.V1, Vector3.down);
                    Edges(i, (e, s0, s1, n) =>
                    {
                        if (n < 0)
                        {
                            Wall(b, 0, i, e, s0, s1, r.Floor, r.Ceil);
                            return;
                        }
                        var o = Rooms[n];
                        var of = FloorOn(o, e ^ 1, s0, s1);
                        if (of > r.Floor)
                            Wall(b, 0, i, e, s0, s1, r.Floor, math.min(of, r.Ceil));
                        if (o.Ceil < r.Ceil)
                            Wall(b, 0, i, e, s0, s1, math.max(o.Ceil, r.Floor), r.Ceil);
                    });
                }
                foreach (var d in Drifts)
                {
                    var r = Rooms[d.Room];
                    if (r.Kind != 0)
                        continue;
                    float u0 = math.max(d.Ellipse.x - d.Ellipse.z, r.U0), u1 = math.min(d.Ellipse.x + d.Ellipse.z, r.U1);
                    float v0 = math.max(d.Ellipse.y - d.Ellipse.w, r.V0), v1 = math.min(d.Ellipse.y + d.Ellipse.w, r.V1);
                    var room = d.Room;
                    b.HeightGrid(1, Steps(u0, u1), Steps(v0, v1), (u, v) => r.FloorAt(u) + Sand(room, u, v) + 0.01f,
                        (u, v) => Sand(room, u, v) < 0.02f);
                }
                foreach (var box in Boxes)
                    if (box.Outside == 0)
                        b.Solid(0, box.UV, box.Y, box.Draw, 12);
                foreach (var s in Sconces)
                {
                    // An iron bracket on the wall, a torch stick in it and the glowing head (the flame burns above it).
                    float3 n = new float3(s.N.x, 0f, s.N.y), side = new float3(-s.N.y, 0f, s.N.x), at = s.At + n * 0.02f;
                    Cube(b, 0, at + n * 0.08f, side * 0.12f + n * 0.08f + new float3(0f, 0.18f, 0f));
                    Cube(b, 0, at + n * 0.3f + new float3(0f, 0.15f, 0f), side * 0.04f + n * 0.04f + new float3(0f, 0.25f, 0f));
                    Cube(b, 2, at + n * 0.32f + new float3(0f, 0.42f, 0f), side * 0.07f + n * 0.07f + new float3(0f, 0.06f, 0f));
                }
            }

            /// <summary>An axis-aligned box from its centre and half size (vectors along u/v either way round).</summary>
            static void Cube(Builder b, int sm, float3 c, float3 half)
            {
                half = math.abs(half);
                b.Solid(sm, new float4(c.x - half.x, c.x + half.x, c.z - half.z, c.z + half.z), new float2(c.y - half.y, c.y + half.y), 0, 4);
            }

            /// <summary>Dark faces across the openings where closed rooms meet open ones, drawn while the interior is hidden.</summary>
            public void Plugs(Builder b)
            {
                for (int i = 0; i < Rooms.Length; i++)
                {
                    var r = Rooms[i];
                    if (r.Kind != 0)
                        continue;
                    Edges(i, (e, s0, s1, n) =>
                    {
                        if (n < 0 || Rooms[n].Kind != 1)
                            return;
                        var y0 = math.max(r.Floor, FloorOn(Rooms[n], e ^ 1, s0, s1));
                        // Facing out of the closed room, towards the open one.
                        switch (e)
                        {
                            case 0: b.Panel(0, 0, r.U0, s0, s1, y0, r.Ceil, Vector3.left); break;
                            case 1: b.Panel(0, 0, r.U1, s0, s1, y0, r.Ceil, Vector3.right); break;
                            case 2: b.Panel(0, 1, r.V0, s0, s1, y0, r.Ceil, Vector3.back); break;
                            default: b.Panel(0, 1, r.V1, s0, s1, y0, r.Ceil, Vector3.forward); break;
                        }
                    });
                }
            }
        }

        /// <summary>Collects vertices and per-submesh triangles; faces are wound to match the normal given.</summary>
        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<Color> col = new List<Color>();
            readonly List<Vector2> st = new List<Vector2>();
            readonly List<int>[] sub;
            public int Tris;
            /// <summary>W-128: the stone pattern of what's added next, in vertex alpha for Punchy/SunkenTemple:
            /// Ashlar (walls), Column (fluted drums, UV x around in flutes, y along the shaft) or Flags (floor tiles, no grout).</summary>
            public float Kind = Ashlar;
            public const float Ashlar = 1f, Column = 0.5f, Flags = 0f;

            public Builder(int submeshes)
            {
                sub = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++)
                    sub[i] = new List<int>();
            }

            int Vert(Vector3 p, Vector3 normal, Vector2 at = default)
            {
                v.Add(p);
                n.Add(normal);
                col.Add(new Color(1f, 1f, 1f, Kind));
                st.Add(at);
                return v.Count - 1;
            }

            void Index(int s, int a, int b, int c, Vector3 normal)
            {
                var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(face, normal) < 0f)
                    (b, c) = (c, b);
                sub[s].Add(a); sub[s].Add(b); sub[s].Add(c);
                Tris++;
            }

            void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
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

            /// <summary>A solid from the ruin's boxes: a box (draw 0) or a drum fitted inside it along u, y or v.</summary>
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
                    // W-128: the shaft in the column pattern, one flute per face (UV x across it, y along the shaft from
                    // its foot), with round normals so it shades as a drum; the ends are plain tiles.
                    var kind = Kind;
                    Kind = Column;
                    Vector3 n0 = Ax(axis, 0f, math.cos(t0), math.sin(t0)), n1 = Ax(axis, 0f, math.cos(t1), math.sin(t1));
                    int ia = Vert(Ax(axis, a0, p0, q0), n0, new Vector2(k, 0f)), ib = Vert(Ax(axis, a0, p1, q1), n1, new Vector2(k + 1, 0f));
                    int ic = Vert(Ax(axis, a1, p1, q1), n1, new Vector2(k + 1, a1 - a0)), id = Vert(Ax(axis, a1, p0, q0), n0, new Vector2(k, a1 - a0));
                    Index(s, ia, ib, ic, out_);
                    Index(s, ia, ic, id, out_);
                    Kind = Flags;
                    Tri(s, Ax(axis, a1, cp, cq), Ax(axis, a1, p0, q0), Ax(axis, a1, p1, q1), Ax(axis, 1f, 0f, 0f));
                    Tri(s, Ax(axis, a0, cp, cq), Ax(axis, a0, p0, q0), Ax(axis, a0, p1, q1), Ax(axis, -1f, 0f, 0f));
                    Kind = kind;
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetColors(col);
                m.SetUVs(0, st);
                m.subMeshCount = sub.Length;
                for (int i = 0; i < sub.Length; i++)
                    m.SetTriangles(sub[i], i);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
