using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-86 row 3 (#40): the Mountains colossus. Its ColossusDefinition asset (Resources/Colossi/SkyWhale) says where it
    // is placed; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsSkyWhale = Register(Kind.SkyWhale, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildSkyWhale(at, yaw, seed));

        static readonly Color WhaleBack = new Color(0.36f, 0.42f, 0.50f);
        static readonly Color WhaleBelly = new Color(0.84f, 0.86f, 0.88f);
        static readonly Color WhalePleat = new Color(0.60f, 0.64f, 0.70f);
        static readonly Color WhaleScar = new Color(0.70f, 0.74f, 0.78f);
        static readonly Color Barnacle = new Color(0.84f, 0.82f, 0.76f);
        static readonly Color LichenGreen = new Color(0.55f, 0.62f, 0.42f);
        static readonly Color LichenOrange = new Color(0.80f, 0.56f, 0.26f);
        static readonly Color Twig = new Color(0.42f, 0.32f, 0.20f);
        static readonly Color[] PrayerFlags =
        {
            new Color(0.20f, 0.36f, 0.78f), new Color(0.94f, 0.94f, 0.92f), new Color(0.80f, 0.20f, 0.18f),
            new Color(0.22f, 0.60f, 0.30f), new Color(0.92f, 0.78f, 0.20f),
        };

        /// <summary>
        /// A-86 row 3 (#40): the Mountains colossus, a sky whale about 55 m long swimming the air 50-odd metres over the
        /// peaks. A slate-blue back and a pale belly with grooved pleats under its jaw, long white humpback fins with knobbly
        /// leading edges, barnacles crusting its chin and fins, and a fluked tail. Seeded per world: its size, how high it
        /// rides and where its details sit. Details: snow lying along its back, mountain lichen in green and orange patches,
        /// an eagle's nest with two eggs riding between its shoulders, old pale scars where it once grazed a peak, and a
        /// string of prayer flags torn from some high pass snagged on its right fin, fluttering behind. Mist streams off its
        /// fin tips, a bank of cloud trails under its belly, snow sifts off its back and a flock of birds rides along with
        /// it. It glides on the updrafts (<see cref="SwaySkyWhale"/>): it rises fast over a ridge and sinks slowly over a
        /// valley, now and then catching a thermal with its fins spread, blowing a spout and singing a long low song.
        /// It does not glow, so it needs no night shot. Every part is left out of its solid shape (the definition's
        /// CollisionLeave "Loose"): it swims high over the ground, and its height above its loop changes as it glides.
        /// </summary>
        static Transform BuildSkyWhale(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("SkyWhale").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), Quaternion.Euler(0f, yaw, 0f));
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var lift = rng.NextFloat(48f, 56f);
            var whale = new GameObject("Whale").transform;
            whale.SetParent(root, false);
            whale.localPosition = Vector3.up * lift;
            whale.localScale = Vector3.one * rng.NextFloat(0.92f, 1.08f);
            var m = new DunesColossus.MeshBuilder();

            // The body: one long ellipsoid, the head in front of it a little lower and flatter, the tail stock tapering behind.
            var bodyC = new Vector3(0f, 0f, 2f);
            var bodyR = new Vector3(8.8f, 7.8f, 22f);
            var headC = new Vector3(0f, -0.6f, 17f);
            var headR = new Vector3(7f, 5.6f, 10f);
            Vector3 OnBody(Vector3 dir) => bodyC + Vector3.Scale(dir.normalized, bodyR);
            Vector3 OnHead(Vector3 dir) => headC + Vector3.Scale(dir.normalized, headR);
            m.Ellipsoid(bodyC, bodyR, Quaternion.identity, WhaleBack, 28, 16);
            m.Ellipsoid(headC, headR, Quaternion.identity, WhaleBack, 22, 12);
            m.Limb(new Vector3(0f, 0.4f, -14f), new Vector3(0f, 1.2f, -27f), 5.6f, 1.7f, WhaleBack, 12);
            m.Ellipsoid(new Vector3(0f, 1.2f, -27.5f), new Vector3(1.9f, 1.5f, 2.2f), Quaternion.identity, WhaleBack, 10, 6);
            m.Ellipsoid(new Vector3(0f, 6.0f, -12f), new Vector3(1.2f, 1.4f, 3.2f), Quaternion.identity, WhaleBack, 8, 4);   // the hump
            // Eyes, each in a pale ring, and the long mouth line curving up under them.
            for (int s = -1; s <= 1; s += 2)
            {
                m.Ellipsoid(new Vector3(s * 6.3f, -0.6f, 16.5f), new Vector3(0.5f, 1.0f, 1.3f), Quaternion.identity, WhaleBelly, 8, 4);
                m.Ellipsoid(new Vector3(s * 6.6f, -0.6f, 16.5f), new Vector3(0.45f, 0.6f, 0.75f), Quaternion.identity, Hollow, 8, 4);
                Tentacle(m, new Vector3(s * 3.8f, -3.0f, 26f), new Vector3(s * 5.6f, -3.4f, 22f), new Vector3(s * 6.6f, -2.8f, 17f), new Vector3(s * 6.0f, -1.9f, 13f), 0.32f, 0.26f, Hollow, Hollow, 10);
                // Three pale rake scars low on each flank, where it once grazed a peak.
                for (int k = 0; k < 3; k++)
                {
                    var from = OnBody(new Vector3(s, 0.1f + k * 0.12f, 0.15f - k * 0.05f)) * 1.003f;
                    var to = OnBody(new Vector3(s, -0.05f + k * 0.12f, -0.15f - k * 0.05f)) * 1.003f;
                    m.Limb(from, to, 0.18f, 0.12f, WhaleScar, 4);
                }
            }
            // Blowholes on the crown behind the head.
            m.Ellipsoid(new Vector3(-0.7f, 6.9f, 12f), new Vector3(0.35f, 0.25f, 1.1f), Quaternion.identity, Hollow, 6, 3);
            m.Ellipsoid(new Vector3(0.7f, 6.9f, 12f), new Vector3(0.35f, 0.25f, 1.1f), Quaternion.identity, Hollow, 6, 3);
            // The grooved pleats under the jaw, running back to the belly.
            for (int k = 0; k < 14; k++)
            {
                var a = Mathf.Lerp(-1.05f, 1.05f, k / 13f);
                Vector3 Under(float z, float rx, float ry) => new Vector3(Mathf.Sin(a) * rx, -Mathf.Cos(a) * ry - 0.6f * Mathf.InverseLerp(2f, 24f, z), z) * 1.01f;
                Tentacle(m, Under(24f, 4.4f, 3.4f), Under(17f, 7.0f, 5.9f), Under(9f, 8.4f, 7.4f), Under(1f, 8.8f, 7.8f), 0.22f, 0.18f, WhalePleat, WhalePleat, 10);
            }
            // Barnacles crusting the chin and the snout.
            for (int k = 0; k < 44; k++)
            {
                var dir = new Vector3(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 0.6f), rng.NextFloat(0.35f, 1f));
                var p = OnHead(dir);
                var n = (p - headC).normalized;
                var r = rng.NextFloat(0.3f, 0.75f);
                var turn = Quaternion.FromToRotation(Vector3.up, n);
                m.Ellipsoid(p, new Vector3(r, r * 0.6f, r), turn, Barnacle, 6, 3);
                m.Ellipsoid(p + n * r * 0.5f, new Vector3(r * 0.35f, r * 0.2f, r * 0.35f), turn, Hollow, 4, 2);
            }
            // Mountain lichen in patches over its back.
            for (int k = 0; k < 12; k++)
            {
                var dir = new Vector3(rng.NextFloat(-0.8f, 0.8f), rng.NextFloat(0.45f, 1f), rng.NextFloat(-0.9f, 0.8f));
                var p = OnBody(dir);
                var r = rng.NextFloat(0.9f, 2.4f);
                m.Ellipsoid(p, new Vector3(r, 0.16f, r * 1.3f), Quaternion.FromToRotation(Vector3.up, (p - bodyC).normalized), k % 3 == 0 ? LichenOrange : LichenGreen, 8, 2);
            }
            // An eagle's nest riding between its shoulders, two speckled eggs in it and a feather caught in the twigs.
            var nest = OnBody(new Vector3(rng.NextFloat(-0.12f, 0.12f), 1f, -0.25f)) + Vector3.down * 0.2f;
            m.Ellipsoid(nest, new Vector3(1.6f, 0.5f, 1.6f), Quaternion.identity, Twig, 10, 3);
            for (int k = 0; k < 16; k++)
            {
                var a = k * Mathf.PI * 2f / 16f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var t = new Vector3(d.z, rng.NextFloat(-0.2f, 0.3f), -d.x);
                m.Limb(nest + d * 1.6f + Vector3.up * 0.3f - t * 1.1f, nest + d * 1.7f + Vector3.up * 0.45f + t * 1.1f, 0.12f, 0.08f, k % 2 == 0 ? Twig : Twig * 0.8f, 4);
            }
            m.Ellipsoid(nest + new Vector3(-0.3f, 0.55f, 0.1f), new Vector3(0.28f, 0.36f, 0.28f), Quaternion.Euler(0f, 0f, 20f), new Color(0.88f, 0.84f, 0.72f), 6, 4);
            m.Ellipsoid(nest + new Vector3(0.35f, 0.5f, -0.15f), new Vector3(0.28f, 0.36f, 0.28f), Quaternion.Euler(30f, 0f, 0f), new Color(0.84f, 0.80f, 0.68f), 6, 4);
            m.Limb(nest + new Vector3(1.2f, 0.5f, 0.4f), nest + new Vector3(1.9f, 1.6f, 0.9f), 0.1f, 0.03f, new Color(0.30f, 0.24f, 0.18f), 4);

            var body = m.ToMesh("SkyWhale");
            Belly(body, 0.05f, -0.45f);
            Weather(body, Snow, 0.78f, 0.96f);   // snow lying along the top of its back
            AddPart("Loose", whale, body);

            // The fins, each on its own pivot so it can beat; the right one has the prayer flags snagged on it.
            var fins = new Transform[2];
            var finTips = new Vector3[2];
            for (int s = -1, i = 0; s <= 1; s += 2, i++)
            {
                var pivot = new GameObject(s < 0 ? "FinL" : "FinR").transform;
                pivot.SetParent(whale, false);
                pivot.localPosition = new Vector3(s * 7.0f, -3.2f, 11f);
                fins[i] = pivot;
                var f = new DunesColossus.MeshBuilder();
                var d = new Vector3(s, -0.3f, -0.45f).normalized;
                var chord = Vector3.Cross(d, Vector3.up).normalized * s;
                var normal = Vector3.Cross(chord, d).normalized;
                if (normal.y < 0f) normal = -normal;
                var finTurn = Quaternion.LookRotation(chord, normal);
                const float L = 17f;
                for (int k = 0; k <= 6; k++)
                {
                    var u = k / 6f;
                    var c = d * L * (u * 0.92f + 0.04f);
                    var w = Mathf.Lerp(3.4f, 0.9f, u);
                    f.Ellipsoid(c, new Vector3(L / 9f, Mathf.Lerp(0.75f, 0.25f, u), w), finTurn, WhaleBack, 12, 5);
                }
                // The knobs along the leading edge, some crusted with barnacles.
                for (int k = 0; k < 10; k++)
                {
                    var u = 0.1f + k * 0.085f;
                    var c = d * L * u + chord * Mathf.Lerp(3.2f, 0.9f, u) * 0.92f;
                    var r = Mathf.Lerp(0.6f, 0.3f, u);
                    f.Ellipsoid(c, new Vector3(r, r * 0.8f, r), finTurn, k % 3 == 1 ? Barnacle : WhaleBack, 6, 3);
                }
                finTips[i] = d * L * 0.98f;
                if (s > 0)
                {
                    // A string of prayer flags, torn from some high pass, wound round the fin and streaming back.
                    var hook = d * L * 0.55f;
                    for (int w = 0; w < 3; w++)
                        f.Ellipsoid(hook + chord * (0.8f - w * 0.8f), new Vector3(0.9f, 0.14f, 0.18f), finTurn, Rope, 6, 2);
                    Vector3 p0 = hook, p1 = hook + new Vector3(1f, -2.5f, -4f), p2 = hook + new Vector3(2.5f, -3.5f, -9f), p3 = hook + new Vector3(3f, -2f, -14f);
                    Vector3 Rope3(float t) => (1 - t) * (1 - t) * (1 - t) * p0 + 3f * (1 - t) * (1 - t) * t * p1 + 3f * (1 - t) * t * t * p2 + t * t * t * p3;
                    for (int k = 0; k < 12; k++)
                    {
                        f.Limb(Rope3(k / 12f), Rope3((k + 1) / 12f), 0.05f, 0.05f, Rope, 4);
                        if (k == 0) continue;
                        var at0 = Rope3((k + 0.5f) / 12f);
                        var along = (Rope3((k + 1) / 12f) - Rope3(k / 12f)).normalized;
                        f.Ellipsoid(at0 + Vector3.down * 0.55f, new Vector3(0.04f, 0.55f, 0.42f), Quaternion.LookRotation(along) * Quaternion.Euler(0f, 0f, rng.NextFloat(-15f, 15f)),
                                    PrayerFlags[k % PrayerFlags.Length] * rng.NextFloat(0.82f, 1f), 4, 2);
                    }
                }
                var finMesh = f.ToMesh(s < 0 ? "SkyWhaleFinL" : "SkyWhaleFinR");
                Belly(finMesh, 0.1f, -0.3f);
                AddPart("Loose", pivot, finMesh);
            }

            // The tail, swept-back flukes on a pivot at the end of the stock, pale underneath, barnacles on the edges.
            var tail = new GameObject("Tail").transform;
            tail.SetParent(whale, false);
            tail.localPosition = new Vector3(0f, 1.2f, -27.5f);
            var tm = new DunesColossus.MeshBuilder();
            for (int s = -1; s <= 1; s += 2)
            {
                var turn = Quaternion.Euler(0f, s * -24f, 0f);
                tm.Ellipsoid(new Vector3(s * 4.6f, 0f, -2.6f), new Vector3(5.2f, 0.45f, 2.6f), turn, WhaleBack, 14, 5);
                tm.Ellipsoid(new Vector3(s * 8.8f, 0f, -5.0f), new Vector3(1.6f, 0.3f, 1.0f), turn, WhaleBack, 8, 3);
                for (int k = 0; k < 4; k++)
                    tm.Ellipsoid(new Vector3(s * (2.5f + k * 1.7f), 0.25f, -0.8f - k * 0.9f), new Vector3(0.35f, 0.25f, 0.35f), Quaternion.identity, Barnacle, 5, 2);
            }
            var tailMesh = tm.ToMesh("SkyWhaleTail");
            Belly(tailMesh, 0.1f, -0.3f);
            AddPart("Loose", tail, tailMesh);

            // Mist streaming off the fin tips, a bank of cloud trailing under its belly, snow sifting off its back, birds
            // riding along with it, and the spout it blows (bursts only, from SwaySkyWhale).
            var mistA = new Color(0.95f, 0.97f, 1.00f, 0.55f);
            var mistB = new Color(0.85f, 0.89f, 0.95f, 0.45f);
            for (int i = 0; i < 2; i++)
                Puffs(fins[i], "TipMist", finTips[i], new Vector3(1.5f, 0.6f, 1.5f), mistA, mistB, 1.2f, 2.4f, 6f, 0.05f, 9f);
            Puffs(whale, "Cloud", new Vector3(0f, -9f, -6f), new Vector3(26f, 4f, 48f), mistA, mistB, 5f, 9f, 3f, 0.2f, 16f);
            Puffs(whale, "SnowSift", new Vector3(0f, 6f, -2f), new Vector3(8f, 1f, 30f), new Color(1f, 1f, 1f, 0.8f), new Color(0.9f, 0.93f, 1f, 0.7f), 0.15f, 0.35f, 8f, -1.6f, 10f);
            Puffs(whale, "Birds", new Vector3(0f, 10f, 0f), new Vector3(50f, 16f, 70f), new Color(0.12f, 0.12f, 0.14f, 0.95f), new Color(0.22f, 0.20f, 0.20f, 0.95f), 0.5f, 0.9f, 1.5f, 0.05f, 14f);
            Puffs(whale, "Spout", new Vector3(0f, 7.6f, 12f), new Vector3(1f, 0.5f, 1f), mistA, mistB, 0.8f, 1.6f, 0.2f, 1.5f, 5f);

            WhaleRig.Whale = whale;
            WhaleRig.FinL = fins[0];
            WhaleRig.FinR = fins[1];
            WhaleRig.Tail = tail;
            WhaleRig.Lift = lift;
            WhaleRig.Alt = float.NaN;
            WhaleRig.Thermal = -99f;
            WhaleRig.Spout = whale.Find("Spout")?.GetComponent<ParticleSystem>();
            return root;
        }

        /// <summary>Pale on every face turned down past <paramref name="from"/> (full at <paramref name="full"/>), a whale's belly.</summary>
        static void Belly(Mesh mesh, float from, float full)
        {
            var normals = mesh.normals;
            var colors = mesh.colors;
            for (int i = 0; i < colors.Length; i++)
            {
                var t = Mathf.InverseLerp(from, full, normals[i].y);
                if (t > 0f)
                    colors[i] = Color.Lerp(colors[i], WhaleBelly, Mathf.SmoothStep(0f, 1f, t));
            }
            mesh.SetColors(colors);
        }

        static class WhaleRig
        {
            public static Transform Whale, FinL, FinR, Tail;
            /// <summary>How high it rides over its loop's ground; the height it glides at now (world y, NaN until set).</summary>
            public static float Lift, Alt;
            /// <summary>When the last thermal began, when it next catches one and when it next sings (Time.time).</summary>
            public static float Thermal, NextThermal, NextSong;
            public static ParticleSystem Spout;
            public static ParticleSystem.EmitParams Burst;
        }

        /// <summary>
        /// A-86 row 3: the sky whale glides on the updrafts. The walker carries its root round the loop on the ground; the
        /// whale holds its own height over that, rising fast where the ground climbs (a ridge's updraft) and sinking slowly
        /// where it falls, nose up as it climbs, rolling gently, its fins beating slow and its flukes sweeping. Every 20-35 s
        /// it catches a thermal: it lifts some metres with its fins spread and blows a spout; every 30-50 s it sings a long
        /// low song heard far off. Cosmetic and local (the walker moves the root). Skipped when hidden or past 900 m.
        /// </summary>
        void SwaySkyWhale()
        {
            var whale = WhaleRig.Whale;
            if (whale == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam == null || (cam.transform.position - whale.position).sqrMagnitude > 900f * 900f)
                return;
            float t = Time.time, dt = Mathf.Min(Time.deltaTime, 0.1f);
            var root = whale.parent;
            var rootAt = root.position;
            var target = rootAt.y + WhaleRig.Lift;
            if (float.IsNaN(WhaleRig.Alt))
                WhaleRig.Alt = target;
            var gap = target - WhaleRig.Alt;
            WhaleRig.Alt += gap * Mathf.Min(1f, dt / (gap > 0f ? 2.5f : 12f));
            var updraft = Mathf.Clamp(gap / 12f, -1f, 1f);
            if (t >= WhaleRig.NextThermal)
            {
                WhaleRig.NextThermal = t + UnityEngine.Random.Range(20f, 35f);
                WhaleRig.Thermal = t;
                if (WhaleRig.Spout != null)
                {
                    var hole = whale.TransformPoint(new Vector3(0f, 7.6f, 12f));
                    for (int i = 0; i < 30; i++)
                    {
                        WhaleRig.Burst.position = hole + UnityEngine.Random.insideUnitSphere * 0.8f;
                        WhaleRig.Burst.velocity = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(6f, 10f), UnityEngine.Random.Range(-1f, 1f));
                        WhaleRig.Burst.startSize = UnityEngine.Random.Range(1f, 2.4f);
                        WhaleRig.Burst.startLifetime = UnityEngine.Random.Range(3f, 5f);
                        WhaleRig.Burst.applyShapeToPosition = false;
                        WhaleRig.Spout.Emit(WhaleRig.Burst, 1);
                    }
                }
            }
            var since = t - WhaleRig.Thermal;
            var thermal = since < 8f ? Mathf.Sin(since / 8f * Mathf.PI) : 0f;
            var bob = 2.2f * Mathf.Sin(t * 0.31f) + 0.8f * Mathf.Sin(t * 0.77f + 1.3f);
            whale.position = new Vector3(rootAt.x, WhaleRig.Alt + bob + thermal * 7f, rootAt.z);
            whale.rotation = Quaternion.Euler(-updraft * 9f - thermal * 5f + Mathf.Sin(t * 0.31f + 1.2f) * 2f, root.eulerAngles.y, Mathf.Sin(t * 0.19f) * 4f);
            // Fins: a slow beat, held spread and still while it rides a thermal; flukes sweep, slower on the thermal.
            var beat = Mathf.Lerp(Mathf.Sin(t * 0.55f) * 14f, 10f, thermal);
            if (WhaleRig.FinL != null) WhaleRig.FinL.localRotation = Quaternion.Euler(0f, 0f, -beat);
            if (WhaleRig.FinR != null) WhaleRig.FinR.localRotation = Quaternion.Euler(0f, 0f, beat);
            if (WhaleRig.Tail != null) WhaleRig.Tail.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.55f - 1.4f) * 12f * (1f - 0.6f * thermal), 0f, 0f);
            if (t >= WhaleRig.NextSong)
            {
                var first = WhaleRig.NextSong == 0f;
                WhaleRig.NextSong = t + UnityEngine.Random.Range(30f, 50f);
                if (!first)
                    Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Howl, whale.position, UnityEngine.Random.Range(0.32f, 0.4f), 0.75f, 700f, Punchy.Audio.SoundGroup.World);
            }
        }
    }
}
