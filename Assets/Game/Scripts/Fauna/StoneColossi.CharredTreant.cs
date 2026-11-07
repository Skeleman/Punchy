using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-85 row 3 (#40): the second EmberForest colossus. Its ColossusDefinition asset (Resources/Colossi/CharredTreant)
    // says where it stands (kept apart from the CinderSnail); the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsCharredTreant = Register(Kind.CharredTreant, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildTreant(at, yaw, seed));

        static readonly Color Char = new Color(0.055f, 0.048f, 0.045f);
        static readonly Color CharGrey = new Color(0.15f, 0.14f, 0.13f);
        static readonly Color CharBark = new Color(0.09f, 0.075f, 0.065f);
        static readonly Color Coal = new Color(0.62f, 0.16f, 0.05f);
        static readonly Color Sprout = new Color(0.42f, 0.66f, 0.22f);

        /// <summary>
        /// A-85 row 3 (#40): a treant about 40 m tall that burned standing and never stopped walking. A split black
        /// trunk, its bark cracked into plates over seams of glowing coal, leans on four charred root legs. Its boughs are
        /// burned to stumps and bare spars; two still smoulder at the tips. A face of embers looks out of the bark (glowing
        /// eyes, a mouth full of coal), and a hollow in its chest glows like a stove. Seeded per world: height, lean and the
        /// boughs. Details: a woodsman's axe still stuck in its shin, a chain with a melted lantern on a spar, a scorched
        /// nest, a single green sprout at the very top, and a ring of ash and coals round its feet. As it walks it leaves a
        /// trail of smouldering embers that slowly die out (<see cref="SwayTreant"/>); smoke rises off the crown, ash
        /// drifts down and sparks blow off the seams. It glows, so it gets a night shot.
        /// </summary>
        static Transform BuildTreant(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("CharredTreant").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.6f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var loose = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new System.Collections.Generic.List<Vector3>();
            var top = rng.NextFloat(24f, 28f);
            var lean = rng.NextFloat(-3f, -1.5f);
            const float R = 4.6f;
            var trunkBase = new Vector3(0f, 5.5f, 0f);
            Vector3 Trunk(float t) => trunkBase + new Vector3(0.8f * Mathf.Sin(t * 2.2f), (top - trunkBase.y) * t, lean * t * t);
            float Radius(float t) => R * (1f - 0.35f * t);

            // The trunk: a char core, and bark plates in rings round it with coal glowing in the gaps between them.
            const int Steps = 9, Plates = 10;
            for (int s = 0; s < Steps; s++)
                m.Limb(Trunk(s / (float)Steps), Trunk((s + 1f) / Steps), Radius(s / (float)Steps) * 0.9f, Radius((s + 1f) / Steps) * 0.9f, Coal, 10);
            for (int s = 0; s < Steps; s++)
            {
                var t = (s + 0.5f) / Steps;
                for (int k = 0; k < Plates; k++)
                {
                    var a = (k + (s % 2) * 0.5f) * Mathf.PI * 2f / Plates + rng.NextFloat(-0.08f, 0.08f);
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var p = Trunk(t) + dir * Radius(t) * 0.86f + Vector3.up * rng.NextFloat(-0.4f, 0.4f);
                    var tone = rng.NextFloat();
                    m.Ellipsoid(p, new Vector3(Radius(t) * rng.NextFloat(0.3f, 0.37f), (top - trunkBase.y) / Steps * rng.NextFloat(0.5f, 0.64f), 0.6f), Quaternion.LookRotation(dir), tone < 0.3f ? CharGrey : tone < 0.75f ? CharBark : Char, 6, 4);
                }
            }
            // The split: a black gash up one side, coal deep in it, and the flared, burnt bole over the roots.
            for (int s = 0; s < 5; s++)
            {
                var t0 = 0.15f + s * 0.1f;
                var side = new Vector3(-1f, 0f, 0.3f).normalized;
                m.Ellipsoid(Trunk(t0) + side * Radius(t0) * 0.95f, new Vector3(0.5f, 1.4f, 1f), Quaternion.LookRotation(side), Char, 6, 4);
            }
            m.Ellipsoid(trunkBase + Vector3.down * 0.4f, new Vector3(R * 1.2f, 4f, R * 1.2f), Quaternion.identity, Char, 14, 5);

            // The face on the front (+z): a heavy brow, ember eyes, a knot of a nose and a mouth full of coal.
            Vector3 Front(float t, float x) => Trunk(t) + new Vector3(x, 0f, Radius(t) * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (Radius(t) * Radius(t)))));
            var faceT = 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var eye = Front(faceT, sx * 1.5f);
                m.Ellipsoid(eye, new Vector3(1.1f, 0.75f, 0.8f), Quaternion.identity, Char, 8, 4);
                m.Ellipsoid(eye + Vector3.forward * 0.3f, new Vector3(0.7f, 0.45f, 0.5f), Quaternion.identity, Ember, 8, 4);
                m.Limb(Front(faceT + 0.06f, sx * 0.3f) + Vector3.forward * 0.4f, Front(faceT + 0.08f, sx * 2.8f) + Vector3.forward * 0.3f, 0.75f, 0.4f, CharGrey, 6);
                glow.Add(eye + Vector3.forward * 0.9f);
            }
            m.Ellipsoid(Front(faceT - 0.06f, 0f) + Vector3.forward * 0.5f, new Vector3(0.8f, 1.2f, 1f), Quaternion.Euler(15f, 0f, 0f), CharBark, 8, 4);
            var mouth = Front(faceT - 0.17f, 0f);
            m.Ellipsoid(mouth, new Vector3(2.2f, 1.2f, 1f), Quaternion.identity, Char, 10, 4);
            m.Ellipsoid(mouth + Vector3.forward * 0.3f, new Vector3(1.7f, 0.8f, 0.8f), Quaternion.identity, Coal, 10, 4);
            for (int k = 0; k < 5; k++)
                m.Ellipsoid(mouth + new Vector3(-1.2f + k * 0.6f, -0.3f + 0.15f * (k % 2), 0.55f), new Vector3(0.28f, 0.22f, 0.25f), Quaternion.identity, Ember, 5, 3);
            glow.Add(mouth + Vector3.forward * 1f);
            // The stove in its chest: a hollow lower down on the left with coals heaped in it.
            var stove = Front(0.25f, -1.8f);
            m.Ellipsoid(stove, new Vector3(1.4f, 1.9f, 1f), Quaternion.identity, Char, 8, 4);
            m.Ellipsoid(stove + new Vector3(0f, -0.4f, 0.25f), new Vector3(1.1f, 1.2f, 0.8f), Quaternion.identity, Coal, 8, 4);
            for (int k = 0; k < 4; k++)
                m.Ellipsoid(stove + new Vector3(rng.NextFloat(-0.6f, 0.6f), -1.1f + k * 0.15f, 0.5f), Vector3.one * rng.NextFloat(0.25f, 0.4f), Quaternion.identity, Ember, 5, 3);
            glow.Add(stove + Vector3.forward * 1f);

            // Seven boughs burned to stumps and bare, forked spars. The two longest still smoulder at the tips; spar 1
            // carries the chain and melted lantern, spar 3 the scorched nest.
            var forkTop = Trunk(1f);
            Vector3 lanternAt = default;
            var smoulder = new System.Collections.Generic.List<Vector3>();
            for (int b = 0; b < 7; b++)
            {
                var a = (b * 360f / 7f + 30f + rng.NextFloat(-18f, 18f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var from = Trunk(rng.NextFloat(0.62f, 0.97f));
                var stump = b == 4 || b == 6;
                var reach = stump ? rng.NextFloat(3f, 5f) : rng.NextFloat(10f, 16f);
                var rise = stump ? 1f : rng.NextFloat(5f, 12f);
                var tip = from + d * reach + Vector3.up * rise;
                var elbow = from + d * reach * 0.5f + Vector3.up * (rise * 0.25f + 1.5f);
                var sideways = new Vector3(d.z, 0f, -d.x) * rng.NextFloat(-2.5f, 2.5f);
                Tentacle(m, from, elbow + sideways, Vector3.Lerp(elbow, tip, 0.6f) - sideways * 0.4f, tip, stump ? 2f : 2f, stump ? 1.5f : 0.25f, Char, CharBark, 9);
                if (stump)
                {
                    m.Ellipsoid(tip, new Vector3(1.6f, 0.4f, 1.6f), Quaternion.LookRotation(d), Coal, 8, 3);   // the burnt-off face
                    continue;
                }
                // Bare, crooked twigs off the spar, snapped short.
                for (int t = 0; t < 3; t++)
                {
                    var on = Vector3.Lerp(elbow, tip, 0.25f + t * 0.3f);
                    var twigDir = (d + new Vector3(rng.NextFloat(-0.9f, 0.9f), rng.NextFloat(0.2f, 1f), rng.NextFloat(-0.9f, 0.9f))).normalized;
                    var mid = on + twigDir * rng.NextFloat(2f, 3.5f);
                    m.Limb(on, mid, 0.55f, 0.3f, Char, 5);
                    m.Limb(mid, mid + (twigDir + new Vector3(rng.NextFloat(-0.6f, 0.6f), 0.4f, rng.NextFloat(-0.6f, 0.6f))).normalized * rng.NextFloat(1.5f, 2.5f), 0.3f, 0.06f, CharBark, 4);
                }
                if (smoulder.Count < 2 && reach > 12f)
                {
                    m.Ellipsoid(tip, new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity, Ember, 6, 3);
                    smoulder.Add(tip + Vector3.up * 0.4f);
                }
                if (b == 1)
                    lanternAt = Vector3.Lerp(elbow, tip, 0.45f);
                if (b == 3)
                {
                    var nest = Vector3.Lerp(from, elbow, 0.7f) + Vector3.up * 0.6f;
                    for (int k = 0; k < 9; k++)
                    {
                        var a0 = k * Mathf.PI * 2f / 9f;
                        var a1 = a0 + Mathf.PI * 2f / 9f * 1.3f;
                        m.Limb(nest + new Vector3(Mathf.Cos(a0), 0.1f * (k % 2), Mathf.Sin(a0)) * 1.1f, nest + new Vector3(Mathf.Cos(a1), 0.1f * ((k + 1) % 2), Mathf.Sin(a1)) * 1.1f, 0.26f, 0.26f, CharGrey, 4);
                    }
                    m.Ellipsoid(nest + Vector3.down * 0.15f, new Vector3(0.9f, 0.3f, 0.9f), Quaternion.identity, Ash, 7, 3);
                }
            }
            if (smoulder.Count == 0)
                smoulder.Add(forkTop + Vector3.up * 2f);
            glow.AddRange(smoulder);
            // The crown: the trunk's burnt-off top, a jagged rim round a coal heart, and one green sprout out of it.
            for (int k = 0; k < 7; k++)
            {
                var a = k * Mathf.PI * 2f / 7f;
                var p = forkTop + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius(1f) * 0.75f;
                m.Limb(p, p + new Vector3(Mathf.Sin(a) * 0.4f, rng.NextFloat(1.2f, 3.5f), Mathf.Cos(a) * 0.4f), 0.9f, 0.15f, Char, 5);
            }
            m.Ellipsoid(forkTop + Vector3.up * 0.4f, new Vector3(Radius(1f) * 0.65f, 0.6f, Radius(1f) * 0.65f), Quaternion.identity, Coal, 10, 3);
            var sproutAt = forkTop + new Vector3(0.6f, 0.6f, -0.4f);
            m.Limb(sproutAt, sproutAt + new Vector3(0.2f, 1.6f, 0.1f), 0.1f, 0.06f, Sprout, 4);
            for (int k = 0; k < 3; k++)
                m.Ellipsoid(sproutAt + new Vector3(0.2f, 1.2f + k * 0.25f, 0.1f) + new Vector3(Mathf.Sin(k * 2.1f), 0f, Mathf.Cos(k * 2.1f)) * 0.35f, new Vector3(0.35f, 0.08f, 0.2f), Quaternion.Euler(0f, k * 120f, 20f), Sprout, 5, 2);

            // Four charred root legs, one lifted mid-step with coals spilling from it; a woodsman's axe stuck in the front shin.
            Vector3 liftedFoot = default;
            for (int k = 0; k < 4; k++)
            {
                var a = (k * 90f + 180f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var lifted = k == 0;
                var foot = d * (lifted ? 11f : 14f) + Vector3.up * (lifted ? rng.NextFloat(3.5f, 5f) : 0.5f);
                for (int s = 0; s < 3; s++)
                {
                    var off = s - 1f;
                    var from = trunkBase + d * 2.4f + side * off * 1.6f + Vector3.down * rng.NextFloat(0f, 1.2f);
                    var end = foot + side * off * 3f + d * rng.NextFloat(-1f, 1.5f) + Vector3.down * (lifted ? 0f : 1f);
                    var kink = Vector3.Lerp(from, end, 0.45f) + side * rng.NextFloat(-1.4f, 1.4f) + d * 2f + Vector3.up * rng.NextFloat(3f, 5f);
                    Tentacle(m, from, kink + d * 1.5f, end + Vector3.up * (lifted ? 2.5f : 3.5f) - d * 1.5f, end, R * 0.32f, 0.3f, Char, CharBark, 9);
                    if (s == 1 && k == 2)
                    {
                        // The axe: an iron head buried in the shin, its haft sticking out and charred at the end.
                        var shin = Vector3.Lerp(kink, end, 0.35f);
                        m.Ellipsoid(shin + d * 0.9f, new Vector3(0.9f, 0.7f, 0.25f), Quaternion.LookRotation(side, Vector3.up), Rust, 6, 3);
                        m.Limb(shin + d * 1.2f, shin + d * 1.2f + (d * 0.6f + Vector3.up * 0.4f + side * 0.5f).normalized * 3.2f, 0.16f, 0.14f, new Color(0.46f, 0.34f, 0.22f), 5);
                    }
                }
                if (lifted)
                {
                    liftedFoot = foot;
                    for (int r = 0; r < 5; r++)
                    {
                        var p = foot + new Vector3(rng.NextFloat(-2.5f, 2.5f), -rng.NextFloat(0.8f, 2f), rng.NextFloat(-2.5f, 2.5f));
                        m.Ellipsoid(p, Vector3.one * rng.NextFloat(0.3f, 0.6f), Quaternion.identity, r % 2 == 0 ? Ember : Coal, 5, 3);
                    }
                }
                else
                    loose.Ellipsoid(foot + Vector3.down * 0.7f, new Vector3(3.4f, 0.4f, 2.6f), Quaternion.Euler(0f, rng.NextFloat(0f, 180f), 0f), Ash, 9, 3);
            }
            // The chain and melted lantern hanging off spar 1 (on a pivot so it swings), and a ring of ash and coals round its feet.
            for (int k = 0; k < 26; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(5f, 15f);
                var p = new Vector3(Mathf.Sin(a) * r, 0.55f, Mathf.Cos(a) * r);
                if (k % 3 == 0)
                    loose.Ellipsoid(p, new Vector3(0.35f, 0.25f, 0.3f), Quaternion.identity, Ember, 5, 3);
                else
                    loose.Ellipsoid(p, new Vector3(rng.NextFloat(0.6f, 1.6f), 0.18f, rng.NextFloat(0.6f, 1.6f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), k % 2 == 0 ? Ash : CharGrey, 6, 2);
            }

            var body = m.ToMesh("CharredTreant");
            Weather(body, Ash, 0.7f, 1f);
            AddPart("Body", root, body);
            AddPart("Loose", root, loose.ToMesh("CharredTreantLoose"));

            var chainPivot = new GameObject("Chain").transform;
            chainPivot.SetParent(root, false);
            chainPivot.localPosition = lanternAt;
            var chain = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 7; k++)
                chain.Ellipsoid(new Vector3(0f, -0.5f - k * 0.55f, 0f), new Vector3(k % 2 == 0 ? 0.25f : 0.08f, 0.3f, k % 2 == 0 ? 0.08f : 0.25f), Quaternion.identity, Rust, 5, 3);
            var lamp = new Vector3(0f, -4.6f, 0f);
            chain.Ellipsoid(lamp, new Vector3(0.6f, 0.75f, 0.6f), Quaternion.Euler(0f, 0f, 14f), Char, 7, 4);           // the slumped cage
            chain.Ellipsoid(lamp + new Vector3(0.1f, -0.6f, 0f), new Vector3(0.45f, 0.35f, 0.4f), Quaternion.Euler(0f, 0f, -20f), Rust, 6, 3);   // the drip of melted iron
            chain.Ellipsoid(lamp + new Vector3(0f, 0.1f, 0f), new Vector3(0.28f, 0.3f, 0.28f), Quaternion.identity, Ember, 5, 3);
            AddPart("Rope", chainPivot, chain.ToMesh("CharredTreantChain"));
            var lampGlow = new GameObject("Ember lamp").transform;
            lampGlow.SetParent(chainPivot, false);
            lampGlow.localPosition = lamp;
            Lanterns.Add(lampGlow);

            // Smoke off the crown and the smouldering spars, ash drifting down, sparks blowing off the seams, coals off the lifted foot.
            Puffs(root, "Smoke", forkTop + Vector3.up * 2f, new Vector3(4f, 1f, 4f), new Color(0.16f, 0.15f, 0.14f, 0.45f), new Color(0.32f, 0.30f, 0.28f, 0.40f), 3f, 7f, 4f, 2.4f, 10f);
            Puffs(root, "Ash", forkTop + Vector3.up * 4f, new Vector3(26f, 6f, 26f), new Color(0.40f, 0.38f, 0.37f, 0.6f), new Color(0.55f, 0.53f, 0.51f, 0.6f), 0.15f, 0.35f, 8f, -0.9f, 14f);
            Puffs(root, "Embers", Trunk(0.5f), new Vector3(9f, 18f, 9f), new Color(1f, 0.55f, 0.15f, 0.95f), new Color(1f, 0.30f, 0.06f, 0.9f), 0.12f, 0.3f, 7f, 1.6f, 4f);
            Puffs(root, "Coals", liftedFoot + Vector3.down * 1.5f, new Vector3(3f, 1f, 3f), new Color(1f, 0.45f, 0.10f, 0.95f), new Color(0.35f, 0.12f, 0.05f, 0.9f), 0.15f, 0.3f, 4f, -2.4f, 2f);
            // Wisps of smoke where the trail's embers lie, emitted by SwayTreant as it drops them.
            Puffs(root, "TrailSmoke", Vector3.zero, Vector3.one, new Color(0.22f, 0.20f, 0.19f, 0.35f), new Color(0.36f, 0.34f, 0.32f, 0.3f), 1.2f, 3f, 0f, 1.2f, 7f);

            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Ember" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                Lanterns.Add(point);
            }
            // Points along the glowing seams between the bark plates, where SwayTreant lets small flames lick out.
            for (int k = 0; k < TreantRig.Seams.Length; k++)
            {
                var st = 0.08f + 0.8f * k / TreantRig.Seams.Length + rng.NextFloat(-0.03f, 0.03f);
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                TreantRig.Seams[k] = Trunk(st) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius(st) * 1.02f;
            }
            TreantRig.Root = root;
            TreantRig.Chain = chainPivot;
            TreantRig.Smoke = root.Find("TrailSmoke")?.GetComponent<ParticleSystem>();
            TreantRig.Count = 0;
            TreantRig.Next = 0;
            TreantRig.HasLast = false;
            return root;
        }

        static class TreantRig
        {
            public static Transform Root, Chain;
            public static ParticleSystem Smoke;
            /// <summary>The trail: where each ember was dropped (world) and when (Time.time), a ring of <see cref="TrailLength"/>.</summary>
            public static readonly Vector3[] Trail = new Vector3[TrailLength];
            public static readonly float[] Dropped = new float[TrailLength];
            /// <summary>Points on the trunk's glowing seams (local), where small flames lick out, bigger at night.</summary>
            public static readonly Vector3[] Seams = new Vector3[14];
            public static int Count, Next;
            public static bool HasLast, Left;
            public static Vector3 Last;
            public static float NextCreak, NextWisp;
            public static int WispAt;
            public static ParticleSystem.EmitParams Wisp;
            public static Unity.Entities.World TerrainWorld;
            public static Unity.Entities.EntityQuery TerrainQuery;
        }

        /// <summary>The ground height under a trail ember (the terrain, the single source of truth); the fallback if it is not baked.</summary>
        static float GroundUnder(Vector3 p, float fallback)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return fallback;
            if (TreantRig.TerrainWorld != world)
            {
                TreantRig.TerrainWorld = world;
                TreantRig.TerrainQuery = world.EntityManager.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<TerrainSource>());
            }
            return TreantRig.TerrainQuery.TryGetSingleton(out TerrainSource source) ? source.SampleHeight(new float2(p.x, p.z)) : fallback;
        }

        const int TrailLength = 24;
        /// <summary>How long a trail ember smoulders (s) before it dies out.</summary>
        const float TrailLife = 120f;

        /// <summary>
        /// A-85 row 3: the treant's chain and lantern swing and it groans now and then, and every 7 m it walks it drops an
        /// ember where a foot left the ground (alternating sides), with a crackle and a wisp of smoke. Each ember burns as a
        /// small flame that shrinks over two minutes and goes out; the newest two light the ground. Cosmetic and local (the
        /// walker moves the root; every player sees their own trail). Skipped when hidden or past 600 m from the camera,
        /// though the trail keeps being laid.
        /// </summary>
        static void SwayTreant()
        {
            if (TreantRig.Root == null || DunesColossus.Hidden)
                return;
            float t = Time.time;
            var at = TreantRig.Root.position;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : at;
            var near = (eye - at).sqrMagnitude < 600f * 600f;
            // Lay the trail as it walks.
            if (!TreantRig.HasLast || (at - TreantRig.Last).sqrMagnitude > 30f * 30f)    // first frame, or the walker set it down elsewhere
            {
                TreantRig.Last = at;
                TreantRig.HasLast = true;
            }
            else if ((at - TreantRig.Last).sqrMagnitude > 7f * 7f)
            {
                var fwd = (at - TreantRig.Last).normalized;
                var side = new Vector3(fwd.z, 0f, -fwd.x) * (TreantRig.Left ? -4f : 4f);
                TreantRig.Left = !TreantRig.Left;
                var drop = TreantRig.Last + side;
                drop.y = GroundUnder(drop, TreantRig.Last.y) + 0.5f;                           // on the ground, not hanging off a slope
                TreantRig.Last = at;
                TreantRig.Trail[TreantRig.Next] = drop;
                TreantRig.Dropped[TreantRig.Next] = t;
                TreantRig.Next = (TreantRig.Next + 1) % TrailLength;
                TreantRig.Count = Mathf.Min(TreantRig.Count + 1, TrailLength);
                if (near)
                {
                    Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.FireCrackle, drop, UnityEngine.Random.Range(0.8f, 1.1f), 0.5f, 60f, Punchy.Audio.SoundGroup.World);
                    if (TreantRig.Smoke != null)
                        for (int i = 0; i < 4; i++)
                        {
                            TreantRig.Wisp.position = drop + UnityEngine.Random.insideUnitSphere * 1.2f;
                            TreantRig.Wisp.velocity = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(0.8f, 1.6f), UnityEngine.Random.Range(-0.3f, 0.3f));
                            TreantRig.Wisp.applyShapeToPosition = false;
                            TreantRig.Smoke.Emit(TreantRig.Wisp, 1);
                        }
                }
            }
            if (!near)
                return;
            if (TreantRig.Chain != null)
                TreantRig.Chain.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.8f + 0.5f) * 5f, 0f, Mathf.Sin(t * 1.1f) * 7f);
            if (t >= TreantRig.NextCreak)
            {
                TreantRig.NextCreak = t + UnityEngine.Random.Range(9f, 18f);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Draw, at + Vector3.up * 20f, UnityEngine.Random.Range(0.2f, 0.26f), 1f, 220f, Punchy.Audio.SoundGroup.World);
            }
            // A thin wisp of smoke off one live trail ember every half second, in turn, so the trail smokes by day too.
            if (t >= TreantRig.NextWisp && TreantRig.Count > 0 && TreantRig.Smoke != null)
            {
                TreantRig.NextWisp = t + 0.5f;
                TreantRig.WispAt = (TreantRig.WispAt + 1) % TreantRig.Count;
                var i = (TreantRig.Next - 1 - TreantRig.WispAt + TrailLength) % TrailLength;
                if (t - TreantRig.Dropped[i] < TrailLife * 0.8f)
                {
                    TreantRig.Wisp.position = TreantRig.Trail[i] + new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), 0f, UnityEngine.Random.Range(-0.6f, 0.6f));
                    TreantRig.Wisp.velocity = new Vector3(0f, UnityEngine.Random.Range(0.6f, 1.1f), 0f);
                    TreantRig.Wisp.applyShapeToPosition = false;
                    TreantRig.Smoke.Emit(TreantRig.Wisp, 1);
                }
            }
            // The trail's embers: flames shrinking as they age, and a warm light round the newest two.
            if ((eye - at).sqrMagnitude > LanternRange * LanternRange)
                return;
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var night = cycle != null ? cycle.Night : 0f;
            var wood = Punchy.Ambience.FireSystem.Wood;
            for (int k = 0; k < TreantRig.Seams.Length; k++)
            {
                var lick = 0.75f + 0.25f * Mathf.Sin(t * 1.3f + k * 2.1f);
                Punchy.Ambience.FireSystem.AddFlame(TreantRig.Root.TransformPoint(TreantRig.Seams[k]), (0.25f + 0.7f * night) * lick, 0x7ED00 + k, wood);
            }
            for (int k = 0; k < TreantRig.Count; k++)
            {
                var i = (TreantRig.Next - 1 - k + TrailLength) % TrailLength;
                var life = 1f - (t - TreantRig.Dropped[i]) / TrailLife;
                if (life <= 0f)
                    continue;
                var p = TreantRig.Trail[i];
                Punchy.Ambience.FireSystem.AddFlame(p, (1.1f + 0.9f * night) * (0.35f + 0.65f * life), 0x7EE00 + i, wood);
                if (k < 2)
                {
                    var light = Punchy.Ambience.FireSystem.Light(wood, (0.9f + 0.1f * Mathf.Sin(t * 6f + i)) * life * (0.3f + 1.1f * night));
                    light.w *= 0.5f + 0.6f * night;
                    Punchy.Terrain.CaveSystem.MovingLights.Add((0x7EE00 + i, p, light));
                }
            }
        }
    }
}
