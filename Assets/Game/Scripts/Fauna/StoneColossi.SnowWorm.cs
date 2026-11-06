using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-84 row 2 (#40): the Tundra colossus. Its ColossusDefinition asset (Resources/Colossi/SnowWorm) says where it
    // stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsSnowWorm = Register(Kind.SnowWorm, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildSnowWorm(source, at, yaw, seed));

        static readonly Color WormHide = new Color(0.66f, 0.69f, 0.74f);
        static readonly Color WormBand = new Color(0.52f, 0.56f, 0.63f);
        static readonly Color WormGum = new Color(0.62f, 0.30f, 0.36f);
        static readonly Color WormFang = new Color(0.90f, 0.90f, 0.84f);
        static readonly Color Ice = new Color(0.66f, 0.84f, 0.94f);
        static readonly Color SnowShade = new Color(0.72f, 0.77f, 0.85f);
        static readonly Color PineDark = new Color(0.12f, 0.24f, 0.16f);
        static readonly Color SledWood = new Color(0.42f, 0.30f, 0.20f);

        /// <summary>
        /// A-84 row 2 (#40): the Tundra colossus, a snow worm far bigger than the snow worm creature. Its ringed, frosted
        /// body breaches the snow in three arcs (tail, back, neck) out of churned mounds, the neck rearing about 44 m to a
        /// blind head that gapes with three peeled-back lips and rings of ice-white teeth. Details: dorsal frost plates and
        /// icicles under every arc, snow settled on top, a ploughed furrow behind the tail, a harpoon stuck in its back with
        /// the rope running down to a hunter's broken sled, a pine knocked flat by the breach, breath steaming from the maw,
        /// snow spraying off the mounds and sifting down from the arcs. The neck sways slowly (<see cref="SwayWorm"/>).
        /// No glow, so it needs no night shot.
        /// </summary>
        static Transform BuildSnowWorm(in TerrainSource source, float3 at, float yaw, uint seed)
        {
            var root = new GameObject("SnowWorm").transform;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), turn);
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var src = source;
            // The ground under a local point, local to the root, so the mounds and the furrow follow the snowfield.
            float Ground(float x, float z)
            {
                var w = turn * new Vector3(x, 0f, z);
                return src.SampleHeight(new float2(at.x + w.x, at.z + w.z)) - at.y;
            }
            Vector3 OnGround(float x, float z, float lift = 0f) => new Vector3(x, Ground(x, z) + lift, z);

            var m = new DunesColossus.MeshBuilder();
            var h = new DunesColossus.MeshBuilder();

            // One arc of the body: overlapping ring segments along a curve, hide and band in turn, with a frost plate on
            // every other ring and icicles under the rings that stand clear of the snow. Rings buried in the snow are
            // skipped. Returns the curve so riders can sit on it.
            System.Func<float, Vector3> Arc(DunesColossus.MeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float r0, float r1, int segs)
            {
                Vector3 P(float t) { var u = 1f - t; return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3; }
                for (int i = 0; i <= segs; i++)
                {
                    var t = i / (float)segs;
                    var p = P(t);
                    var tangent = P(math.min(1f, t + 0.01f)) - P(math.max(0f, t - 0.01f));
                    var r = Mathf.Lerp(r0, r1, t) * (1f + 0.05f * Mathf.Sin(i * 2.1f));
                    var ground = Ground(p.x, p.z);
                    if (p.y + r < ground - 0.5f)
                        continue;
                    var rot = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                    var step = (P(math.min(1f, t + 1f / segs)) - p).magnitude;
                    b.Ellipsoid(p, new Vector3(r, r * 0.94f, math.max(step * 0.8f, r * 0.45f)), rot, i % 2 == 0 ? WormHide : WormBand, 12, 6);
                    var up = rot * Vector3.up;
                    var side = rot * Vector3.right;
                    if (i % 2 == 0 && p.y + r * 0.8f > ground)
                        b.Limb(p + up * r * 0.8f, p + up * (r + 1.6f + rng.NextFloat(0f, 0.8f)) - tangent.normalized * 1.2f, r * 0.14f, 0.08f, Color.Lerp(Ice, WormHide, 0.4f), 5);
                    if (i % 3 == 1 && p.y - r > ground + 2.5f)
                        for (int k = 0; k < 3; k++)
                        {
                            var top = p - up * r * 0.88f + side * rng.NextFloat(-r * 0.5f, r * 0.5f);
                            var len = rng.NextFloat(1.2f, math.min(3.6f, p.y - r - ground - 0.5f));
                            b.Limb(top, top + Vector3.down * len, 0.28f, 0.03f, Ice, 5);
                        }
                    // Patches of ice crusted on the flanks.
                    if (rng.NextFloat() < 0.25f && p.y > ground)
                        b.Ellipsoid(p + side * (rng.NextFloat() < 0.5f ? -r : r) * 0.96f + up * rng.NextFloat(-0.3f, 0.5f) * r, new Vector3(0.3f, r * 0.35f, r * 0.4f), rot, Ice, 6, 3);
                }
                return P;
            }

            // A churned mound of snow where the body breaks the surface, with clods and an ice slab or two thrown up.
            void Mound(float x, float z, float size)
            {
                var c = OnGround(x, z, 0.2f);
                m.Ellipsoid(c, new Vector3(size, size * 0.38f, size * 0.9f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Snow, 12, 5);
                for (int k = 0; k < 9; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var d = size * rng.NextFloat(0.9f, 1.6f);
                    var clod = OnGround(x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, 0.2f);
                    m.Ellipsoid(clod, Vector3.one * rng.NextFloat(0.5f, 1.4f), Quaternion.identity, k % 3 == 0 ? SnowShade : Snow, 6, 3);
                }
                for (int k = 0; k < 2; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var slab = OnGround(x + Mathf.Cos(a) * size * 1.1f, z + Mathf.Sin(a) * size * 1.1f, 0.6f);
                    m.Ellipsoid(slab, new Vector3(rng.NextFloat(1.2f, 2.2f), 0.4f, rng.NextFloat(1f, 1.8f)), Quaternion.Euler(rng.NextFloat(-35f, 35f), rng.NextFloat(0f, 360f), rng.NextFloat(-35f, 35f)), Ice, 6, 3);
                }
            }

            // The body, tail first: the tail tip flicks out of the snow, then the tail arc, the back arc and the neck.
            m.Limb(OnGround(-1f, -40f, -1f), OnGround(-3.5f, -46f, 5f), 2.4f, 0.4f, WormBand, 8);
            Mound(-1.5f, -40.5f, 4f);
            Arc(m, new Vector3(0f, -5f, -34f), new Vector3(1f, 13f, -30f), new Vector3(2f, 13f, -18f), new Vector3(2f, -5f, -13f), 3.6f, 4.6f, 14);
            Mound(0.2f, -33f, 6f);
            Mound(2f, -13.5f, 6.5f);
            var back = Arc(m, new Vector3(2f, -6f, -7f), new Vector3(3f, 23f, -1f), new Vector3(1f, 23f, 15f), new Vector3(0f, -6f, 21f), 5f, 6f, 18);
            Mound(2f, -6f, 8f);
            Mound(0.5f, 20f, 8f);
            // The neck and head are their own part, pivoted at the breach, so the neck can sway.
            var neckBase = OnGround(0f, 26f);
            var neck = Arc(h, new Vector3(0f, neckBase.y - 7f, 25f), new Vector3(0f, 20f, 27f), new Vector3(0f, 44f, 32f), new Vector3(0f, 41f, 42f), 6.6f, 5.4f, 20);
            Mound(0f, 26f, 9.5f);

            // The head: a blunt bulb with three lips peeled back round a maw of teeth, ring inside ring, and a beard of
            // icicles under the jaw. Blind; a row of dark sensing pits runs along each side.
            var head = neck(1f);
            var fwd = (neck(1f) - neck(0.96f)).normalized;
            var face = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 hUp = face * Vector3.up, hSide = face * Vector3.right;
            h.Ellipsoid(head + fwd * 1.5f, new Vector3(6.4f, 6.2f, 5.2f), face, WormHide, 14, 8);
            var maw = head + fwd * 5.6f;
            h.Ellipsoid(maw, new Vector3(4.4f, 4.4f, 1.1f), face, Hollow, 14, 4);
            h.Ellipsoid(maw - fwd * 0.2f, new Vector3(4.9f, 4.9f, 0.9f), face, WormGum, 14, 4);
            for (int ring = 0; ring < 3; ring++)
            {
                var radius = 4.3f - ring * 1.1f;
                var count = 16 - ring * 4;
                var inset = ring * 0.9f;
                for (int k = 0; k < count; k++)
                {
                    var a = (k + ring * 0.5f) * Mathf.PI * 2f / count;
                    var radial = hSide * Mathf.Cos(a) + hUp * Mathf.Sin(a);
                    var tooth = maw + radial * radius - fwd * inset;
                    var len = rng.NextFloat(0.9f, 1.5f) * (1f - ring * 0.15f);
                    h.Limb(tooth, tooth - radial * len + fwd * 0.4f, 0.42f - ring * 0.08f, 0.03f, WormFang, 5);
                }
            }
            for (int k = 0; k < 3; k++)
            {
                var a = (90f + k * 120f) * Mathf.Deg2Rad;
                var radial = hSide * Mathf.Cos(a) + hUp * Mathf.Sin(a);
                var lipRot = Quaternion.LookRotation(fwd, radial) * Quaternion.Euler(-55f, 0f, 0f);
                h.Ellipsoid(maw + radial * 5.6f - fwd * 0.6f, new Vector3(3.6f, 0.9f, 3.2f), lipRot, WormBand, 10, 5);
                h.Ellipsoid(maw + radial * 5.3f - fwd * 0.1f, new Vector3(3f, 0.5f, 2.6f), lipRot, WormGum, 10, 4);
            }
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 4; k++)
                    h.Ellipsoid(head + hSide * s * 5.9f + hUp * (1.5f - k * 0.2f) - fwd * (k * 1.6f - 1f), new Vector3(0.35f, 0.35f, 0.35f), face, Hollow, 5, 3);
            for (int k = 0; k < 9; k++)
            {
                var chin = head + fwd * rng.NextFloat(-1f, 3f) - hUp * 5.8f + hSide * rng.NextFloat(-3.5f, 3.5f);
                h.Limb(chin, chin + Vector3.down * rng.NextFloat(1f, 3.2f), 0.3f, 0.03f, Ice, 5);
            }

            // A hunter's harpoon stuck in the top of the back arc, its rope running down to a broken sled in the snow.
            var hump = back(0.42f);
            var harpoonIn = hump + Vector3.up * 5.4f + Vector3.right * 1.4f;
            var harpoonEnd = harpoonIn + new Vector3(3f, 6.5f, -2.2f);
            m.Limb(harpoonIn - (harpoonEnd - harpoonIn).normalized * 1.2f, harpoonEnd, 0.18f, 0.15f, SledWood, 6);
            m.Ellipsoid(harpoonEnd, new Vector3(0.3f, 0.3f, 0.3f), Quaternion.identity, Rust, 5, 3);
            var sled = OnGround(17f, -2f, 0.3f);
            Tentacle(m, harpoonEnd, harpoonEnd + new Vector3(4f, -4f, 0f), sled + new Vector3(-3f, 2f, 0f), sled + new Vector3(0f, 0.4f, 0f), 0.09f, 0.09f, new Color(0.62f, 0.54f, 0.40f), new Color(0.62f, 0.54f, 0.40f), 14);
            var sledTurn = Quaternion.Euler(4f, 35f, -12f);
            for (int s = -1; s <= 1; s += 2)
                m.Limb(sled + sledTurn * new Vector3(s * 0.8f, 0f, -2f), sled + sledTurn * new Vector3(s * 0.8f, 0.1f, 2.2f), 0.09f, 0.09f, SledWood, 4);
            m.Limb(sled + sledTurn * new Vector3(-0.8f, 0.1f, 2.2f), sled + sledTurn * new Vector3(-0.6f, 0.9f, 2.7f), 0.09f, 0.07f, SledWood, 4);   // a curled runner tip
            for (int k = 0; k < 6; k++)
            {
                if (k == 3)
                    continue;   // a slat snapped out
                m.Ellipsoid(sled + sledTurn * new Vector3(0f, 0.25f, -1.7f + k * 0.7f), new Vector3(1f, 0.06f, 0.25f), sledTurn * Quaternion.Euler(0f, k == 4 ? 18f : 0f, 0f), SledWood, 4, 2);
            }
            m.Ellipsoid(sled + sledTurn * new Vector3(0.2f, 0.6f, -0.8f), new Vector3(0.7f, 0.45f, 0.6f), sledTurn, new Color(0.46f, 0.36f, 0.28f), 6, 4);   // a fur bundle
            var slatAway = OnGround(19.5f, 1.5f, 0.15f);
            m.Ellipsoid(slatAway, new Vector3(1f, 0.06f, 0.25f), Quaternion.Euler(0f, 70f, 8f), SledWood, 4, 2);

            // A pine knocked flat by the back arc's breach, half buried, its needles snowed over.
            var pineFoot = OnGround(-9f, -4f, 0.2f);
            var pineDir = Quaternion.Euler(0f, -120f, 0f) * new Vector3(0f, 0.18f, 1f);
            var pineTop = pineFoot + pineDir.normalized * 11f;
            m.Limb(pineFoot, pineTop, 0.45f, 0.1f, SledWood, 6);
            for (int k = 0; k < 4; k++)
            {
                var c = Vector3.Lerp(pineFoot, pineTop, 0.3f + k * 0.18f);
                var spread = 2.4f - k * 0.5f;
                m.Ellipsoid(c, new Vector3(spread, spread * 0.6f, 1.1f), Quaternion.LookRotation(pineDir, Vector3.up), PineDark, 8, 4);
            }
            for (int k = 0; k < 3; k++)
            {
                var a = k * 2.1f;
                m.Limb(pineFoot, pineFoot + new Vector3(Mathf.Cos(a) * 1.8f, 1.2f, Mathf.Sin(a) * 1.8f), 0.25f, 0.06f, SledWood, 4);   // roots torn up
            }

            var body = m.ToMesh("SnowWorm");
            Weather(body, Snow, 0.45f, 0.85f);
            AddPart("Body", root, body);
            var neckPivot = new GameObject("Neck").transform;
            neckPivot.SetParent(root, false);
            neckPivot.localPosition = neckBase;
            var headMesh = h.ToMesh("SnowWormHead");
            Weather(headMesh, Snow, 0.55f, 0.9f);
            AddPart("Head", neckPivot, headMesh);
            neckPivot.GetChild(0).localPosition = -neckBase;
            WormRig.Neck = neckPivot;

            // The furrow behind the tail: two berms of thrown snow either side of a trodden trench, wandering off over the
            // snowfield. Its own part, which ColossusCollision leaves out (the definition's CollisionLeave).
            var trail = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 24; k++)
            {
                var z = -44f - k * 3f;
                var x = Mathf.Sin(k * 0.22f) * 5f;
                var width = 3.2f + 0.4f * Mathf.Sin(k * 0.9f);
                trail.Ellipsoid(OnGround(x, z, 0.05f), new Vector3(width, 0.22f, 2.2f), Quaternion.identity, SnowShade, 8, 3);
                for (int s = -1; s <= 1; s += 2)
                    trail.Ellipsoid(OnGround(x + s * (width + 1.2f), z, 0.3f), new Vector3(1.6f, 0.8f + 0.3f * Mathf.Sin(k * 1.7f + s), 2.1f), Quaternion.identity, Snow, 7, 3);
                if (k % 4 == 2)
                    trail.Ellipsoid(OnGround(x + rng.NextFloat(-6f, 6f), z, 0.3f), Vector3.one * rng.NextFloat(0.4f, 0.9f), Quaternion.identity, Snow, 5, 3);
            }
            AddPart("Trail", root, trail.ToMesh("SnowWormTrail"));

            // Breath steaming from the maw, snow spraying off the breach mounds, and powder sifting down from the arcs.
            WormRig.Maw = maw;
            Puffs(neckPivot.GetChild(0), "Breath", maw + fwd * 2f, new Vector3(2.5f, 2.5f, 2f), new Color(0.92f, 0.95f, 1f, 0.35f), new Color(0.85f, 0.90f, 0.96f, 0.30f), 2f, 4.5f, 2.5f, 0.5f, 6f);
            Puffs(root, "Spray", OnGround(0f, 26f, 1f), new Vector3(16f, 2f, 14f), new Color(0.95f, 0.97f, 1f, 0.45f), new Color(0.88f, 0.92f, 0.98f, 0.40f), 1.5f, 3.5f, 3f, 0.7f, 7f);
            Puffs(root, "Spray2", OnGround(1f, -6f, 1f), new Vector3(14f, 2f, 12f), new Color(0.95f, 0.97f, 1f, 0.40f), new Color(0.88f, 0.92f, 0.98f, 0.35f), 1.2f, 3f, 2f, 0.6f, 7f);
            Puffs(root, "Sift", back(0.5f) + Vector3.up * 2f, new Vector3(6f, 2f, 18f), new Color(1f, 1f, 1f, 0.8f), new Color(0.92f, 0.95f, 1f, 0.7f), 0.12f, 0.3f, 6f, -1.4f, 9f);
            return root;
        }

        static class WormRig
        {
            public static Transform Neck;
            /// <summary>When it next groans (Time.time).</summary>
            public static float NextGroan;
            /// <summary>The maw, in the head part's space (where the groan comes from).</summary>
            public static Vector3 Maw;
        }

        /// <summary>
        /// A-84 row 2: the snow worm's neck sways a few degrees, slow and seasick, as if it were scenting the air.
        /// Cosmetic and clock-driven, so every player sees much the same; skipped when hidden or past 600 m from the camera.
        /// Every 14-26 s it groans from the maw (a growl pitched far down), and snow hisses off the breach mounds after it.
        /// </summary>
        static void SwayWorm()
        {
            if (WormRig.Neck == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - WormRig.Neck.position).sqrMagnitude > 600f * 600f)
                return;
            float t = Time.time;
            WormRig.Neck.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.19f) * 2.2f, Mathf.Sin(t * 0.11f + 0.7f) * 5f, Mathf.Sin(t * 0.15f + 2f) * 1.8f);
            if (t >= WormRig.NextGroan)
            {
                WormRig.NextGroan = t + UnityEngine.Random.Range(14f, 26f);
                var head = WormRig.Neck.GetChild(0).TransformPoint(WormRig.Maw);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Growl, head, UnityEngine.Random.Range(0.28f, 0.36f), 1f, 320f, Punchy.Audio.SoundGroup.World);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.StepSnow, WormRig.Neck.parent.position, 0.45f, 0.8f, 120f, Punchy.Audio.SoundGroup.World);
            }
        }
    }
}
