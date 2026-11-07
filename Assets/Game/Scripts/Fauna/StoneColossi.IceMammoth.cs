using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-85 row 4 (#40): the Glacier colossus. Its ColossusDefinition asset (Resources/Colossi/IceMammoth) says where it
    // stands (kept apart from the KneelingTitan, which also takes the Glacier); the line below hands StoneColossi this builder.
    public partial class StoneColossi
    {
        static readonly bool buildsIceMammoth = Register(Kind.IceMammoth, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildMammoth(at, yaw, seed));

        static readonly Color FurDark = new Color(0.17f, 0.11f, 0.07f);
        static readonly Color FurBrown = new Color(0.30f, 0.19f, 0.11f);
        static readonly Color FurRed = new Color(0.42f, 0.25f, 0.13f);
        static readonly Color IcePale = new Color(0.80f, 0.91f, 0.97f);
        static readonly Color IceBlue = new Color(0.56f, 0.76f, 0.90f);
        static readonly Color IceDeep = new Color(0.34f, 0.54f, 0.74f);
        static readonly Color Ivory = new Color(0.88f, 0.83f, 0.70f);
        static readonly Color IvoryDark = new Color(0.66f, 0.60f, 0.48f);

        /// <summary>
        /// A-85 row 4 (#40): a woolly mammoth about 30 m tall at the hump, sealed in a shell of glacier ice that it is
        /// slowly walking out of. Plates of pale and deep blue ice cover its back and flanks; between them its shaggy
        /// red-brown coat shows and hangs in long strands under the belly. Great curled tusks crusted with ice, a trunk
        /// that curls and sways, icicles under the belly, tusks and chin, and snow lying on everything facing up. Seeded
        /// per world: which ice plates are left and the fur. Details: a raven frozen mid-flight in a block on its hump,
        /// one wing out; an old hunter's spear still stuck in the flank ice with its binding frayed; frost on its brow;
        /// snow drifts and old shed chunks round its feet. As it walks it sheds chunks of ice that tumble off its flanks,
        /// land with a crack and a puff of powder, lie a while and melt away (<see cref="SwayMammoth"/>); its breath
        /// steams from the trunk and glitter falls off it. It does not glow, so it gets no night shot.
        /// </summary>
        static Transform BuildMammoth(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("IceMammoth").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.6f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var loose = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var shed = new System.Collections.Generic.List<Vector3>();
            var shedOut = new System.Collections.Generic.List<Vector3>();

            // The body: barrel, the high hump over the shoulders, the domed head. Fur underneath, ice plates over it.
            var barrel = (c: new Vector3(0f, 18f, -2f), r: new Vector3(9f, 8.5f, 15f));
            var hump = (c: new Vector3(0f, 24.5f, 6.5f), r: new Vector3(7f, 5.5f, 7f));
            var head = (c: new Vector3(0f, 22.5f, 15f), r: new Vector3(5.5f, 6f, 5.5f));
            var crown = (c: new Vector3(0f, 27.5f, 14f), r: new Vector3(4f, 3.5f, 4f));
            m.Ellipsoid(barrel.c, barrel.r, Quaternion.identity, FurDark, 18, 10);
            m.Ellipsoid(hump.c, hump.r, Quaternion.identity, FurBrown, 14, 8);
            m.Ellipsoid(head.c, head.r, Quaternion.identity, FurBrown, 14, 8);
            m.Ellipsoid(crown.c, crown.r, Quaternion.identity, FurRed, 12, 6);
            void Shell((Vector3 c, Vector3 r) part, int count, float keep)
            {
                for (int k = 0; k < count; k++)
                {
                    var u = ((Vector3)rng.NextFloat3Direction());
                    if (u.y < -0.45f || rng.NextFloat() > keep)
                        continue;                                                                   // the belly and the gaps: fur shows
                    var p = part.c + Vector3.Scale(u, part.r);
                    var n = new Vector3(u.x / part.r.x, u.y / part.r.y, u.z / part.r.z).normalized;
                    var size = rng.NextFloat(1.8f, 3.4f);
                    var tone = rng.NextFloat();
                    m.Rock(p + n * 0.35f, new Vector3(size, size * rng.NextFloat(0.7f, 1.1f), 0.9f), Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0f, 0f, rng.NextFloat(0f, 360f)),
                           tone < 0.45f ? IcePale : tone < 0.8f ? IceBlue : IceDeep, 6, 3, d => 0.82f + 0.3f * Mathf.Abs(Mathf.Sin(d.x * 7.1f + d.y * 3.3f)));
                    // Icicles off the plates that face out and down a little, and shed spots on the flanks.
                    if (n.y < 0.1f && n.y > -0.5f && rng.NextFloat() < 0.5f)
                        Icicle(m, p + n * 0.6f + Vector3.down * size * 0.6f, rng.NextFloat(1.2f, 3f), 0.35f);
                    if (Mathf.Abs(n.x) > 0.55f && n.y > -0.3f && shed.Count < 20)
                    {
                        shed.Add(p + n * 0.8f);
                        shedOut.Add(n);
                    }
                }
            }
            Shell(barrel, 110, 0.72f);
            Shell(hump, 40, 0.8f);
            Shell(head, 26, 0.55f);
            Shell(crown, 14, 0.7f);

            // The shaggy coat: long strands hanging off the lower flanks, belly and chin, in three browns.
            for (int k = 0; k < 90; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var y = rng.NextFloat(-0.75f, 0.05f);
                var u = new Vector3(Mathf.Sin(a) * Mathf.Sqrt(1f - y * y), y, Mathf.Cos(a) * Mathf.Sqrt(1f - y * y));
                var p = barrel.c + Vector3.Scale(u, barrel.r) * 0.97f;
                var len = rng.NextFloat(3f, 6.5f);
                var tone = rng.NextFloat();
                var col = tone < 0.4f ? FurBrown : tone < 0.75f ? FurRed : FurDark;
                var end = p + Vector3.down * len + new Vector3(u.x, 0f, u.z) * 0.9f;
                m.Limb(p, end, rng.NextFloat(0.7f, 1.1f), 0.12f, col, 5);
            }
            for (int k = 0; k < 14; k++)                                                            // the beard under the chin
            {
                var p = head.c + new Vector3(rng.NextFloat(-3.5f, 3.5f), -4.5f, rng.NextFloat(-1f, 2.5f));
                m.Limb(p, p + new Vector3(rng.NextFloat(-0.5f, 0.5f), -rng.NextFloat(3f, 5f), 0.6f), 0.6f, 0.1f, k % 2 == 0 ? FurRed : FurBrown, 5);
            }

            // Ears, eyes under frosted brows, and icicles hanging in a row along the belly.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                m.Ellipsoid(head.c + new Vector3(sx * 4.8f, 1.5f, -2f), new Vector3(0.8f, 2.4f, 2f), Quaternion.Euler(0f, sx * 20f, sx * 10f), FurDark, 8, 4);
                var eye = head.c + new Vector3(sx * 3.7f, 1f, 3.6f);
                m.Ellipsoid(eye, new Vector3(0.55f, 0.5f, 0.45f), Quaternion.identity, Hollow, 8, 4);
                m.Ellipsoid(eye + new Vector3(sx * 0.1f, 0.75f, 0.1f), new Vector3(1.1f, 0.35f, 0.7f), Quaternion.Euler(0f, 0f, sx * -12f), IcePale, 8, 3);
            }
            for (int k = 0; k < 22; k++)
            {
                var z = Mathf.Lerp(-13f, 11f, k / 21f) + rng.NextFloat(-0.5f, 0.5f);
                var x = rng.NextFloat(-6f, 6f);
                var under = barrel.c.y - barrel.r.y * Mathf.Sqrt(Mathf.Max(0.05f, 1f - x * x / (barrel.r.x * barrel.r.x) - (z - barrel.c.z) * (z - barrel.c.z) / (barrel.r.z * barrel.r.z)));
                Icicle(m, new Vector3(x, under + 0.3f, z), rng.NextFloat(1.5f, 4f), 0.4f);
            }

            // Four pillar legs in ice boots; the front left one mid-step. Fur skirts round the tops, ivory toenails.
            for (int k = 0; k < 4; k++)
            {
                var sx = k % 2 == 0 ? -1f : 1f;
                var front = k < 2;
                var lifted = k == 0;
                var top = new Vector3(sx * 5.6f, 15f, front ? 8f : -11f);
                var foot = new Vector3(sx * 6.2f, lifted ? 3.2f : 0.9f, top.z + (lifted ? 2.5f : front ? -0.5f : 1f));
                var knee = Vector3.Lerp(top, foot, 0.5f) + new Vector3(0f, 0f, front ? (lifted ? 2f : 0.4f) : -0.8f);
                m.Limb(top, knee, 3.9f, 3.3f, FurBrown, 10);
                m.Limb(knee, foot, 3.3f, 3.4f, FurDark, 10);
                for (int s = 0; s < 8; s++)
                {
                    var a = s * Mathf.PI * 2f / 8f;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var p = Vector3.Lerp(top, knee, 0.55f) + d * 3.6f;
                    m.Limb(p, p + Vector3.down * rng.NextFloat(2.5f, 4.5f) + d * 0.6f, 0.75f, 0.12f, s % 2 == 0 ? FurRed : FurBrown, 5);
                }
                m.Rock(foot + Vector3.up * 1.2f, new Vector3(4f, 2.2f, 4f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), k % 2 == 0 ? IceBlue : IcePale, 9, 4,
                       d => 0.86f + 0.22f * Mathf.Abs(Mathf.Sin(d.x * 5.3f + d.z * 4.1f)));
                for (int t = 0; t < 4; t++)
                {
                    var a = (-36f + t * 24f) * Mathf.Deg2Rad;
                    m.Ellipsoid(foot + new Vector3(Mathf.Sin(a) * 3.6f, -0.3f, Mathf.Cos(a) * 3.6f), new Vector3(0.7f, 0.55f, 0.5f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), Ivory, 6, 3);
                }
                if (!lifted)
                    loose.Ellipsoid(new Vector3(foot.x, 0.1f, foot.z), new Vector3(5.5f, 0.7f, 4.5f), Quaternion.Euler(0f, rng.NextFloat(0f, 180f), 0f), Snow, 10, 3);   // the drift it stands in
            }
            // The tail: short, tufted, an icicle off its tip.
            var tailEnd = new Vector3(0.6f, 10.5f, -19f);
            m.Limb(new Vector3(0f, 17.5f, -16.5f), tailEnd, 0.8f, 0.45f, FurBrown, 6);
            m.Ellipsoid(tailEnd + Vector3.down * 0.8f, new Vector3(0.7f, 1.4f, 0.7f), Quaternion.identity, FurDark, 6, 4);
            Icicle(m, tailEnd + Vector3.down * 2f, 1.6f, 0.25f);

            // The tusks: long, curling out, forward and back in, crusted with ice lumps and hung with icicles.
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var p0 = new Vector3(sx * 2.4f, 19.5f, 18.5f);
                var p1 = new Vector3(sx * 5f, 12.5f, 23f);
                var p2 = new Vector3(sx * 8.5f, 12.5f, 31f);
                var p3 = new Vector3(sx * 4.5f, 18.5f, 34f);
                Tentacle(m, p0, p1, p2, p3, 1.15f, 0.3f, Ivory, IvoryDark, 14);
                for (int k = 0; k < 5; k++)
                {
                    var t = 0.25f + k * 0.12f;
                    var on = Bezier(p0, p1, p2, p3, t);
                    if (k % 2 == 0)
                        m.Rock(on, new Vector3(1.3f, 1f, 1.5f) * (1.1f - t * 0.6f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), IcePale, 6, 3, d => 0.8f + 0.3f * Mathf.Abs(d.x));
                    Icicle(m, on + Vector3.down * (1f - t * 0.5f), rng.NextFloat(1f, 2.4f), 0.3f);
                }
                shed.Add(Bezier(p0, p1, p2, p3, 0.45f));
                shedOut.Add(new Vector3(sx, -0.3f, 0.4f).normalized);
            }

            // The raven frozen mid-flight in a block on the hump, one wing and its head out of the ice.
            var block = hump.c + new Vector3(1.6f, hump.r.y - 0.4f, -0.8f);
            m.Rock(block, new Vector3(1.8f, 1.4f, 1.6f), Quaternion.Euler(0f, 25f, 8f), IcePale, 7, 4, d => 0.85f + 0.2f * Mathf.Abs(Mathf.Sin(d.z * 6f)));
            var raven = new Color(0.05f, 0.05f, 0.07f);
            var bird = block + new Vector3(0.2f, 1.2f, 0.3f);
            m.Ellipsoid(bird, new Vector3(0.45f, 0.4f, 0.8f), Quaternion.Euler(-20f, 25f, 0f), raven, 7, 4);
            m.Ellipsoid(bird + new Vector3(0.25f, 0.45f, 0.65f), new Vector3(0.3f, 0.3f, 0.32f), Quaternion.identity, raven, 6, 4);
            m.Limb(bird + new Vector3(0.35f, 0.45f, 0.95f), bird + new Vector3(0.45f, 0.4f, 1.45f), 0.1f, 0.02f, new Color(0.15f, 0.14f, 0.13f), 4);
            m.Ellipsoid(bird + new Vector3(-0.7f, 0.5f, 0f), new Vector3(1.3f, 0.12f, 0.55f), Quaternion.Euler(0f, 25f, 35f), raven, 7, 3);   // the wing out

            // The old hunter's spear in the left flank: an ash shaft out of the ice, a frayed binding near the head.
            var hit = barrel.c + new Vector3(-barrel.r.x * 0.95f, 2.5f, 3f);
            var shaftDir = new Vector3(-0.8f, 0.45f, -0.35f).normalized;
            m.Limb(hit - shaftDir * 0.8f, hit + shaftDir * 7f, 0.2f, 0.17f, new Color(0.48f, 0.37f, 0.25f), 5);
            m.Ellipsoid(hit + shaftDir * 0.5f, new Vector3(0.3f, 0.3f, 0.45f), Quaternion.LookRotation(shaftDir), Rust, 5, 3);
            for (int k = 0; k < 3; k++)
                m.Limb(hit + shaftDir * (0.7f + k * 0.15f), hit + shaftDir * (0.7f + k * 0.15f) + new Vector3(0f, -0.9f - k * 0.3f, 0.1f * k), 0.06f, 0.02f, Rust, 3);

            var body = m.ToMesh("IceMammoth");
            Weather(body, Snow, 0.55f, 0.95f);
            AddPart("Body", root, body);

            // The trunk on its own pivot so it sways: curling down nearly to the snow and up again at the tip, frost rings on it.
            var trunkPivot = new GameObject("Trunk").transform;
            trunkPivot.SetParent(root, false);
            trunkPivot.localPosition = new Vector3(0f, 19.5f, 20f);
            var tm = new DunesColossus.MeshBuilder();
            var q0 = Vector3.zero; var q1 = new Vector3(0f, -6f, 2.5f); var q2 = new Vector3(0f, -12f, 1.5f); var q3 = new Vector3(0.4f, -13.5f, 5f);
            Tentacle(tm, q0, q1, q2, q3, 1.9f, 0.55f, FurBrown, FurDark, 14);
            for (int k = 1; k < 6; k++)
            {
                var on = Bezier(q0, q1, q2, q3, k / 6f);
                tm.Ellipsoid(on, new Vector3(1.9f - k * 0.22f, 0.3f, 1.9f - k * 0.22f), Quaternion.identity, k % 2 == 0 ? IcePale : IceBlue, 9, 2);
            }
            var tipTm = tm.ToMesh("IceMammothTrunk");
            Weather(tipTm, Snow, 0.6f, 0.95f);
            AddPart("Rope", trunkPivot, tipTm);

            // Snow drifts and old shed chunks lying round its feet (left out of its solid shape, as "Loose").
            for (int k = 0; k < 16; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(10f, 22f);
                var p = new Vector3(Mathf.Sin(a) * r, 0.2f, Mathf.Cos(a) * r - 2f);
                if (k % 2 == 0)
                    loose.Rock(p, Vector3.one * rng.NextFloat(0.6f, 1.4f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), k % 4 == 0 ? IceBlue : IcePale, 6, 3, d => 0.8f + 0.35f * Mathf.Abs(Mathf.Sin(d.y * 5f)));
                else
                    loose.Ellipsoid(p, new Vector3(rng.NextFloat(1.5f, 3f), 0.4f, rng.NextFloat(1.5f, 3f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Snow, 8, 3);
            }
            AddPart("Loose", root, loose.ToMesh("IceMammothLoose"));

            // Breath steaming off the trunk tip, glitter falling off its flanks, and the powder where shed chunks land.
            Puffs(trunkPivot, "Breath", q3, new Vector3(0.6f, 0.6f, 0.6f), new Color(0.92f, 0.95f, 1f, 0.35f), new Color(0.85f, 0.90f, 0.96f, 0.25f), 1.2f, 3f, 0f, 0.8f, 4f);
            Puffs(root, "Glitter", barrel.c + Vector3.up * 2f, new Vector3(20f, 10f, 30f), new Color(0.90f, 0.96f, 1f, 0.9f), new Color(0.70f, 0.85f, 1f, 0.8f), 0.08f, 0.2f, 12f, -1.2f, 8f);
            Puffs(root, "Powder", Vector3.zero, Vector3.one, new Color(0.92f, 0.95f, 1f, 0.55f), new Color(0.82f, 0.88f, 0.95f, 0.45f), 1.5f, 3.5f, 0f, 0.4f, 3f);

            // The chunk meshes it sheds, three shapes made once (they share the colossus material and are drawn, not spawned).
            if (MammothRig.Chunks[0] == null)
                for (int v = 0; v < MammothRig.Chunks.Length; v++)
                {
                    var cm = new DunesColossus.MeshBuilder();
                    var f = 3.1f + v * 1.7f;
                    cm.Rock(Vector3.zero, new Vector3(1f, 0.75f + 0.15f * v, 0.9f), Quaternion.Euler(0f, v * 50f, 0f), v == 1 ? IceBlue : IcePale, 7, 4, d => 0.78f + 0.32f * Mathf.Abs(Mathf.Sin(d.x * f + d.y * 2.3f + d.z * 1.7f)));
                    cm.Rock(new Vector3(0.3f, 0.35f, -0.2f), new Vector3(0.5f, 0.4f, 0.55f), Quaternion.identity, v == 2 ? IceDeep : IceBlue, 6, 3, d => 1f);
                    MammothRig.Chunks[v] = cm.ToMesh("IceChunk" + v);
                    Weather(MammothRig.Chunks[v], Snow, 0.7f, 1f);
                }
            MammothRig.Material = Resources.Load<Material>("Colossus");
            MammothRig.Root = root;
            MammothRig.Trunk = trunkPivot;
            MammothRig.Breath = trunkPivot.Find("Breath")?.GetComponent<ParticleSystem>();
            MammothRig.Powder = root.Find("Powder")?.GetComponent<ParticleSystem>();
            MammothRig.Shed = shed.ToArray();
            MammothRig.ShedOut = shedOut.ToArray();
            MammothRig.HasLast = false;
            MammothRig.NextShed = 0f;
            for (int k = 0; k < MammothRig.State.Length; k++)
                MammothRig.State[k] = 0;
            return root;
        }

        /// <summary>A hanging icicle: a thin cone from <paramref name="top"/> straight down.</summary>
        static void Icicle(DunesColossus.MeshBuilder m, Vector3 top, float length, float radius)
        {
            m.Limb(top, top + Vector3.down * length, radius, 0.02f, IcePale, 5);
        }

        static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            var u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        static class MammothRig
        {
            public static Transform Root, Trunk;
            public static ParticleSystem Breath, Powder;
            public static Material Material;
            public static readonly Mesh[] Chunks = new Mesh[3];
            /// <summary>Local spots on its flanks and tusks a chunk can break from, and the way out of the body there.</summary>
            public static Vector3[] Shed = new Vector3[0], ShedOut = new Vector3[0];
            /// <summary>The shed chunks, a pool of <see cref="ChunkCount"/>: 0 unused, 1 falling, 2 lying (melting at the end).</summary>
            public static readonly int[] State = new int[ChunkCount];
            public static readonly Vector3[] Pos = new Vector3[ChunkCount], Vel = new Vector3[ChunkCount], Spin = new Vector3[ChunkCount];
            public static readonly Quaternion[] Rot = new Quaternion[ChunkCount];
            public static readonly float[] Size = new float[ChunkCount], Landed = new float[ChunkCount];
            public static int Next;
            public static bool HasLast;
            public static Vector3 Last;
            public static float NextBreath, NextRumble, NextDrip, NextShed;
            public static ParticleSystem.EmitParams Puff;
        }

        const int ChunkCount = 18;
        /// <summary>How long a shed chunk lies (s) before it has melted away; it shrinks over the last quarter.</summary>
        const float ChunkLife = 110f;

        /// <summary>
        /// A-85 row 4: every 5 m the mammoth walks (or every 7-13 s it stands) a chunk of ice breaks off a flank or a tusk and tumbles down, lands
        /// with a crack and a puff of powder, lies a while and melts away (up to 18 at once). Its trunk sways, it steams
        /// a breath every few seconds, rumbles now and then and its icicles drip. Cosmetic and local, like the treant's
        /// trail: chunks are drawn with Graphics.DrawMesh (no objects, no garbage), skipped when hidden or far off.
        /// </summary>
        static void SwayMammoth()
        {
            if (MammothRig.Root == null || DunesColossus.Hidden)
                return;
            float t = Time.time, dt = Time.deltaTime;
            var root = MammothRig.Root;
            var at = root.position;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : at;
            var near = (eye - at).sqrMagnitude < 600f * 600f;
            // Break a chunk off as it walks.
            if (!MammothRig.HasLast || (at - MammothRig.Last).sqrMagnitude > 30f * 30f)        // first frame, or the walker set it down elsewhere
            {
                MammothRig.Last = at;
                MammothRig.HasLast = true;
            }
            else if (((at - MammothRig.Last).sqrMagnitude > 5f * 5f || t >= MammothRig.NextShed) && MammothRig.Shed.Length > 0)
            {
                // Standing (resting, or on glacier too steep to walk) it still shakes one loose every 7-13 s.
                MammothRig.Last = at;
                MammothRig.NextShed = t + UnityEngine.Random.Range(7f, 13f);
                var i = MammothRig.Next;
                MammothRig.Next = (i + 1) % ChunkCount;
                var spot = UnityEngine.Random.Range(0, MammothRig.Shed.Length);
                MammothRig.State[i] = 1;
                MammothRig.Pos[i] = root.TransformPoint(MammothRig.Shed[spot]);
                MammothRig.Vel[i] = root.TransformDirection(MammothRig.ShedOut[spot]) * UnityEngine.Random.Range(1.5f, 3f) + Vector3.up * UnityEngine.Random.Range(0f, 1.5f);
                MammothRig.Spin[i] = UnityEngine.Random.insideUnitSphere * 220f;
                MammothRig.Rot[i] = UnityEngine.Random.rotation;
                MammothRig.Size[i] = UnityEngine.Random.Range(0.9f, 2.2f);
            }
            // Fall, land, lie and melt.
            for (int i = 0; i < ChunkCount; i++)
            {
                if (MammothRig.State[i] == 1)
                {
                    MammothRig.Vel[i] += Vector3.down * (9.8f * dt);
                    MammothRig.Pos[i] += MammothRig.Vel[i] * dt;
                    MammothRig.Rot[i] = Quaternion.Normalize(Quaternion.Euler(MammothRig.Spin[i] * dt) * MammothRig.Rot[i]);   // renormalised, or the drift breaks DrawMesh's TRS
                    var ground = GroundUnder(MammothRig.Pos[i], at.y);
                    if (MammothRig.Pos[i].y <= ground + MammothRig.Size[i] * 0.4f)
                    {
                        MammothRig.Pos[i].y = ground + MammothRig.Size[i] * 0.3f;          // settled a little into the snow
                        MammothRig.State[i] = 2;
                        MammothRig.Landed[i] = t;
                        if (near)
                        {
                            var p = MammothRig.Pos[i];
                            Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.StepRock, p, UnityEngine.Random.Range(0.45f, 0.6f), 0.9f, 90f, Punchy.Audio.SoundGroup.World);
                            if (MammothRig.Powder != null)
                                for (int k = 0; k < 10; k++)
                                {
                                    MammothRig.Puff.position = p + UnityEngine.Random.insideUnitSphere * MammothRig.Size[i];
                                    MammothRig.Puff.velocity = new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), UnityEngine.Random.Range(0.4f, 1.6f), UnityEngine.Random.Range(-1.5f, 1.5f));
                                    MammothRig.Puff.applyShapeToPosition = false;
                                    MammothRig.Powder.Emit(MammothRig.Puff, 1);
                                }
                        }
                    }
                }
                else if (MammothRig.State[i] == 2 && t - MammothRig.Landed[i] > ChunkLife)
                    MammothRig.State[i] = 0;
            }
            if ((eye - at).sqrMagnitude > 900f * 900f || MammothRig.Material == null)
                return;
            for (int i = 0; i < ChunkCount; i++)
            {
                if (MammothRig.State[i] == 0)
                    continue;
                var melt = MammothRig.State[i] == 2 ? Mathf.Clamp01((ChunkLife - (t - MammothRig.Landed[i])) / (ChunkLife * 0.25f)) : 1f;
                var s = MammothRig.Size[i] * (0.25f + 0.75f * melt);
                var pos = MammothRig.Pos[i] + Vector3.down * (MammothRig.Size[i] * 0.3f * (1f - melt));
                Graphics.DrawMesh(MammothRig.Chunks[i % MammothRig.Chunks.Length], Matrix4x4.TRS(pos, MammothRig.Rot[i], new Vector3(s, s, s)), MammothRig.Material, 0);
            }
            if (!near)
                return;
            // The trunk sways and curls; a breath steams off its tip every few seconds; a low rumble; an icicle drips.
            if (MammothRig.Trunk != null)
                MammothRig.Trunk.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.37f) * 9f - 4f, Mathf.Sin(t * 0.23f + 1f) * 12f, Mathf.Sin(t * 0.31f) * 5f);
            if (t >= MammothRig.NextBreath && MammothRig.Breath != null)
            {
                MammothRig.NextBreath = t + UnityEngine.Random.Range(5f, 9f);
                MammothRig.Breath.Emit(14);
            }
            if (t >= MammothRig.NextRumble)
            {
                MammothRig.NextRumble = t + UnityEngine.Random.Range(14f, 26f);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Moo, at + Vector3.up * 20f, UnityEngine.Random.Range(0.28f, 0.34f), 1f, 260f, Punchy.Audio.SoundGroup.World);
            }
            if (t >= MammothRig.NextDrip)
            {
                MammothRig.NextDrip = t + UnityEngine.Random.Range(2f, 5f);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.CaveDrip, at + Vector3.up * 8f, UnityEngine.Random.Range(0.8f, 1.2f), 0.5f, 40f, Punchy.Audio.SoundGroup.World);
            }
        }
    }
}
