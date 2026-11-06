using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-84 row 3 (#40): the Temperate colossus. Its ColossusDefinition asset (Resources/Colossi/WalkingOak) says where it
    // stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsWalkingOak = Register(Kind.WalkingOak, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildOak(at, yaw, seed));

        static readonly Color OakBark = new Color(0.36f, 0.29f, 0.22f);
        static readonly Color OakBarkDark = new Color(0.22f, 0.17f, 0.13f);
        static readonly Color OakLeaf = new Color(0.24f, 0.42f, 0.16f);
        static readonly Color OakLeafDark = new Color(0.16f, 0.31f, 0.12f);
        static readonly Color OakLeafLight = new Color(0.40f, 0.55f, 0.22f);
        static readonly Color OakTurn = new Color(0.72f, 0.46f, 0.16f);
        static readonly Color Turf = new Color(0.30f, 0.46f, 0.18f);
        static readonly Color Loam = new Color(0.33f, 0.24f, 0.16f);
        static readonly Color Fungus = new Color(0.86f, 0.70f, 0.44f);
        static readonly Color Comb = new Color(0.78f, 0.58f, 0.22f);
        static readonly Color Rope = new Color(0.62f, 0.52f, 0.36f);
        static readonly Color EggBlue = new Color(0.62f, 0.80f, 0.84f);

        /// <summary>
        /// A-84 row 3 (#40): the Temperate colossus, an oak about 45 m tall walking on its roots. A gnarled, ridged trunk
        /// leans into the step on four root legs (the back one lifted, a torn sod of turf still hanging in it), and six great
        /// boughs carry a broad crown, a few leaves already turning. An old face looks out of the bark: hollow eyes under a
        /// mossy brow, a knot of a nose and a gaping mouth. Seeded per world: height, lean and the boughs.
        /// Details: a rope swing on the low bough, a bird's nest with blue eggs in the fork, an owl in a hollow on its back,
        /// a wild beehive hanging under a bough (bees round it), shelf fungi and ivy on the trunk, moss on every upturned
        /// face, and footprints behind it with oak saplings already sprouting in them. Leaves drift down from the crown and
        /// soil trickles off the lifted foot. The crown and the swing sway (<see cref="SwayOak"/>); it creaks, the birds
        /// in it sing and the hive hums. No glow, so it needs no night shot.
        /// </summary>
        static Transform BuildOak(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("WalkingOak").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.6f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var crown = new DunesColossus.MeshBuilder();
            var loose = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var fork = rng.NextFloat(22f, 26f);
            var lean = rng.NextFloat(2f, 4f);
            const float R = 5.2f;
            var trunkBase = new Vector3(0f, 6f, 0f);
            Vector3 Trunk(float t) => trunkBase + new Vector3(0f, (fork - trunkBase.y) * t, lean * t * t);
            float Radius(float t) => R * (1f - 0.28f * t);

            // The trunk: a dark core and twelve wandering bark ridges round it.
            const int Steps = 8, Ridges = 12;
            for (int s = 0; s < Steps; s++)
                m.Limb(Trunk(s / (float)Steps), Trunk((s + 1f) / Steps), Radius(s / (float)Steps) * 0.92f, Radius((s + 1f) / Steps) * 0.92f, OakBarkDark, 12);
            for (int k = 0; k < Ridges; k++)
            {
                var a0 = k * Mathf.PI * 2f / Ridges;
                var wob = rng.NextFloat(0f, 6f);
                Vector3 Ridge(float t)
                {
                    var a = a0 + 0.12f * Mathf.Sin(t * 5f + wob);
                    return Trunk(t) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius(t) * 0.86f;
                }
                for (int s = 0; s < Steps; s++)
                    m.Limb(Ridge(s / (float)Steps), Ridge((s + 1f) / Steps), Radius(s / (float)Steps) * 0.2f, Radius((s + 1f) / Steps) * 0.2f, OakBark, 5);
            }
            // The flared bole over the roots.
            m.Ellipsoid(trunkBase + Vector3.up * 0.8f, new Vector3(R * 1.5f, 3f, R * 1.5f), Quaternion.identity, OakBarkDark, 14, 5);

            // The face, on the front (+z) of the trunk: brow, two hollow eyes, a knot of a nose and a gaping mouth.
            Vector3 Front(float t, float x) => Trunk(t) + new Vector3(x, 0f, Radius(t) * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (Radius(t) * Radius(t)))) * 0.95f);
            var faceT = 0.42f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var eye = Front(faceT, sx * 1.9f);
                m.Ellipsoid(eye, new Vector3(1.3f, 0.9f, 0.9f), Quaternion.identity, OakBarkDark, 8, 4);
                m.Ellipsoid(eye + Vector3.forward * 0.25f, new Vector3(0.95f, 0.65f, 0.7f), Quaternion.identity, Hollow, 8, 4);
                m.Limb(Front(faceT + 0.06f, sx * 0.4f) + Vector3.forward * 0.4f, Front(faceT + 0.07f, sx * 3.2f) + Vector3.forward * 0.4f, 0.8f, 0.5f, OakBark, 6);
            }
            m.Ellipsoid(Front(faceT - 0.07f, 0f) + Vector3.forward * 0.5f, new Vector3(0.9f, 1.4f, 1.1f), Quaternion.Euler(15f, 0f, 0f), OakBark, 8, 4);
            var mouth = Front(faceT - 0.2f, 0f);
            m.Ellipsoid(mouth, new Vector3(2.4f, 1.3f, 1.1f), Quaternion.identity, OakBarkDark, 10, 4);
            m.Ellipsoid(mouth + Vector3.forward * 0.3f, new Vector3(1.9f, 0.9f, 0.9f), Quaternion.identity, Hollow, 10, 4);

            // Six boughs out of the top of the trunk, each forking into twigs that end in leaf clusters (the crown, its own
            // part on a pivot at the fork so it can sway). Bough 0 is the low one, out to the right, for the swing; bough 2
            // carries the hive.
            const int Boughs = 6;
            var forkTop = Trunk(1f);
            var crownPivotAt = forkTop;
            Vector3 swingAt = default, hiveAt = default;
            void Cluster(Vector3 c, float size, int count)
            {
                for (int k = 0; k < count * 3; k++)
                {
                    var p = c + new Vector3(rng.NextFloat(-1.2f, 1.2f), rng.NextFloat(-0.5f, 0.8f), rng.NextFloat(-1.2f, 1.2f)) * size;
                    var tone = rng.NextFloat();
                    var col = tone < 0.07f ? OakTurn : tone < 0.4f ? OakLeafDark : tone < 0.8f ? OakLeaf : OakLeafLight;
                    crown.Ellipsoid(p - crownPivotAt, new Vector3(1f, 0.7f, 1f) * size * rng.NextFloat(0.4f, 0.65f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), col, 7, 4);
                }
            }
            for (int b = 0; b < Boughs; b++)
            {
                var a = (b == 0 ? 90f : b * 360f / Boughs + 90f + rng.NextFloat(-20f, 20f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var low = b == 0;
                var from = Trunk(low ? 0.72f : rng.NextFloat(0.85f, 1f));
                var reach = low ? 17f : rng.NextFloat(13f, 19f);
                var rise = low ? 2f : rng.NextFloat(8f, 15f);
                var tip = from + d * reach + Vector3.up * rise;
                var elbow = from + d * reach * 0.45f + Vector3.up * (rise * 0.2f + 2f);
                var sideways = new Vector3(d.z, 0f, -d.x) * rng.NextFloat(-3f, 3f);
                Tentacle(m, from, elbow + sideways, Vector3.Lerp(elbow, tip, 0.6f) - sideways * 0.5f, tip, 2.6f, 0.35f, OakBarkDark, OakBark, 10);
                // Twigs off the bough, each with its own leaf cluster.
                for (int t = 0; t < 3; t++)
                {
                    var on = Vector3.Lerp(elbow, tip, 0.3f + t * 0.3f);
                    var twigDir = (d + new Vector3(rng.NextFloat(-0.8f, 0.8f), rng.NextFloat(0.3f, 1f), rng.NextFloat(-0.8f, 0.8f))).normalized;
                    var twigEnd = on + twigDir * rng.NextFloat(4f, 7f);
                    m.Limb(on, twigEnd, 0.7f, 0.15f, OakBark, 5);
                    Cluster(twigEnd, rng.NextFloat(3.5f, 5f), 3);
                }
                Cluster(tip + Vector3.up * 1.5f, rng.NextFloat(4.5f, 6f), 4);
                if (low)
                    swingAt = Vector3.Lerp(elbow, tip, 0.35f);
                if (b == 2)
                    hiveAt = Vector3.Lerp(from, elbow, 0.75f) + Vector3.down * 2.4f;
            }
            Cluster(forkTop + Vector3.up * (16f + rng.NextFloat(-2f, 2f)), 8f, 6);   // the crown's top

            // A bird's nest in the fork, three pale blue eggs in it.
            var nest = forkTop + Vector3.up * 0.8f + Vector3.forward * 1.2f;
            for (int k = 0; k < 10; k++)
            {
                var a0 = k * Mathf.PI * 2f / 10f;
                var a1 = a0 + Mathf.PI * 2f / 10f * 1.3f;
                m.Limb(nest + new Vector3(Mathf.Cos(a0), 0.1f * (k % 2), Mathf.Sin(a0)) * 1.3f, nest + new Vector3(Mathf.Cos(a1), 0.1f * ((k + 1) % 2), Mathf.Sin(a1)) * 1.3f, 0.32f, 0.32f, OakBark, 4);
            }
            m.Ellipsoid(nest - Vector3.up * 0.2f, new Vector3(1.2f, 0.4f, 1.2f), Quaternion.identity, Loam, 8, 3);
            for (int k = 0; k < 3; k++)
                m.Ellipsoid(nest + new Vector3(Mathf.Cos(k * 2.1f), 0.25f, Mathf.Sin(k * 2.1f)) * 0.45f, new Vector3(0.22f, 0.28f, 0.22f), Quaternion.identity, EggBlue, 6, 3);

            // An owl in a hollow on its back (-z), its pale face and round eyes peering out.
            var hollowAt = Trunk(0.68f) + Vector3.back * Radius(0.68f) * 0.95f;
            m.Ellipsoid(hollowAt, new Vector3(1.5f, 2f, 1f), Quaternion.identity, OakBarkDark, 8, 4);
            m.Ellipsoid(hollowAt + Vector3.back * 0.3f, new Vector3(1.15f, 1.6f, 0.8f), Quaternion.identity, Hollow, 8, 4);
            m.Ellipsoid(hollowAt + new Vector3(0f, -0.3f, -0.5f), new Vector3(0.6f, 0.8f, 0.5f), Quaternion.identity, new Color(0.45f, 0.36f, 0.26f), 7, 4);
            m.Ellipsoid(hollowAt + new Vector3(0f, 0.5f, -0.75f), new Vector3(0.5f, 0.42f, 0.25f), Quaternion.identity, new Color(0.86f, 0.80f, 0.68f), 7, 3);
            for (int sx = -1; sx <= 1; sx += 2)
                m.Ellipsoid(hollowAt + new Vector3(sx * 0.2f, 0.55f, -0.95f), Vector3.one * 0.12f, Quaternion.identity, new Color(0.95f, 0.75f, 0.20f), 5, 3);

            // A wild beehive hanging under bough 2: stacked combs tapering down, a dark way in.
            for (int k = 0; k < 4; k++)
                m.Ellipsoid(hiveAt + Vector3.down * k * 0.9f, new Vector3(1.5f - k * 0.25f, 0.65f, 1.4f - k * 0.25f), Quaternion.identity, k % 2 == 0 ? Comb : Comb * 0.85f, 9, 4);
            m.Limb(hiveAt + Vector3.up * 0.4f, hiveAt + Vector3.up * 2.6f, 0.5f, 0.4f, OakBark, 5);
            m.Ellipsoid(hiveAt + new Vector3(0f, -1.6f, 0.9f), new Vector3(0.35f, 0.25f, 0.2f), Quaternion.identity, Hollow, 6, 3);

            // Shelf fungi stepping up the trunk, and ivy climbing it.
            for (int k = 0; k < 7; k++)
            {
                var t = rng.NextFloat(0.1f, 0.65f);
                var a = rng.NextFloat(Mathf.PI * 0.35f, Mathf.PI * 1.65f) + Mathf.PI;   // round the sides and back, off the face
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var p = Trunk(t) + dir * Radius(t) * 0.98f;
                for (int s = 0; s < 3; s++)
                    m.Ellipsoid(p + Vector3.up * s * 0.9f + dir * 0.4f, new Vector3(1.1f - s * 0.25f, 0.22f, 1.1f - s * 0.25f), Quaternion.identity, s == 0 ? Fungus : Fungus * 0.9f, 8, 3);
            }
            for (int k = 0; k < 3; k++)
            {
                var a = (150f + k * 35f) * Mathf.Deg2Rad;
                var prev = Trunk(0f) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius(0f) * 0.97f;
                for (int s = 1; s <= 10; s++)
                {
                    var t = s * 0.08f;
                    var aa = a + Mathf.Sin(s * 0.9f + k) * 0.25f;
                    var next = Trunk(t) + new Vector3(Mathf.Sin(aa), 0f, Mathf.Cos(aa)) * Radius(t) * 0.97f;
                    m.Limb(prev, next, 0.18f, 0.18f, OakLeafDark, 4);
                    m.Ellipsoid(next + new Vector3(Mathf.Sin(aa), 0f, Mathf.Cos(aa)) * 0.25f, new Vector3(0.5f, 0.4f, 0.3f), Quaternion.Euler(0f, aa * Mathf.Rad2Deg, 0f), OakLeaf, 5, 3);
                    prev = next;
                }
            }

            // Four root legs: three planted wide and sunk into the ground, the back one lifted mid-step with soil clods
            // and a torn sod of turf still caught in it.
            Vector3 liftedFoot = default;
            for (int k = 0; k < 4; k++)
            {
                var a = (k * 90f + 180f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var lifted = k == 0;
                var foot = d * (lifted ? 10f : 12f) + Vector3.up * (lifted ? rng.NextFloat(4f, 5.5f) : 0.5f);
                for (int s = 0; s < 4; s++)
                {
                    var off = s - 1.5f;
                    var from = trunkBase + d * 2.6f + side * off * 1.4f + Vector3.down * rng.NextFloat(0f, 1.5f);
                    var end = foot + side * off * 1.9f + d * rng.NextFloat(-1f, 1.5f) + Vector3.down * (lifted ? 0f : 1f);
                    var kink = Vector3.Lerp(from, end, 0.45f) + side * rng.NextFloat(-1.6f, 1.6f) + Vector3.up * rng.NextFloat(1f, 3f);
                    Tentacle(m, from, kink + d * 1.5f, end + Vector3.up * (lifted ? 2.5f : 4f) - d * 1.5f, end, R * 0.3f, 0.3f, OakBarkDark, OakBark, 9);
                    var forkAt = Vector3.Lerp(kink, end, 0.4f);
                    m.Limb(forkAt, forkAt + side * (off < 0f ? -1f : 1f) * rng.NextFloat(1.5f, 3f) + d * rng.NextFloat(1f, 2f) + Vector3.down * (lifted ? 0.6f : 2f), 0.55f, 0.12f, OakBarkDark, 5);
                }
                for (int r = 0; r < 5; r++)
                {
                    var ra = Mathf.Atan2(d.x, d.z) + (r - 2f) * 0.45f;
                    var to = foot + new Vector3(Mathf.Sin(ra) * rng.NextFloat(3f, 5f), lifted ? -rng.NextFloat(1f, 2.5f) : -0.4f, Mathf.Cos(ra) * rng.NextFloat(3f, 5f));
                    m.Limb(foot, to, 0.6f, 0.1f, OakBarkDark, 5);
                    if (lifted)
                        m.Ellipsoid(to + Vector3.down * 0.3f, Vector3.one * rng.NextFloat(0.4f, 0.8f), Quaternion.identity, Loam, 5, 3);
                }
                if (lifted)
                {
                    liftedFoot = foot;
                    // The sod: grass on top, a layer of earth under it, tilted where it hangs.
                    var sod = foot + Vector3.down * 1.4f + side * 1.5f;
                    var tilt = Quaternion.Euler(18f, 0f, -12f);
                    m.Ellipsoid(sod, new Vector3(3f, 0.7f, 2.4f), tilt, Loam, 10, 3);
                    m.Ellipsoid(sod + tilt * Vector3.up * 0.45f, new Vector3(2.9f, 0.35f, 2.3f), tilt, Turf, 10, 3);
                    for (int g = 0; g < 8; g++)
                    {
                        var gp = sod + tilt * new Vector3(rng.NextFloat(-2.2f, 2.2f), 0.6f, rng.NextFloat(-1.8f, 1.8f));
                        m.Limb(gp, gp + tilt * new Vector3(rng.NextFloat(-0.2f, 0.2f), rng.NextFloat(0.6f, 1.1f), rng.NextFloat(-0.2f, 0.2f)), 0.08f, 0.01f, OakLeafLight, 3);
                    }
                }
                else
                    m.Ellipsoid(foot + Vector3.down * 0.2f, new Vector3(5f, 0.6f, 5f), Quaternion.identity, Loam, 10, 3);
            }
            // Acorns and a few fallen leaves round its feet.
            for (int k = 0; k < 18; k++)
            {
                var p = new Vector3(rng.NextFloat(-14f, 14f), 0.6f, rng.NextFloat(-14f, 14f));
                if (k % 3 == 0)
                    loose.Ellipsoid(p, new Vector3(0.7f, 0.06f, 0.5f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), k % 2 == 0 ? OakTurn : OakLeafLight, 6, 2);
                else
                {
                    loose.Ellipsoid(p, new Vector3(0.18f, 0.24f, 0.18f), Quaternion.identity, new Color(0.55f, 0.40f, 0.20f), 5, 3);
                    loose.Ellipsoid(p + Vector3.up * 0.18f, new Vector3(0.2f, 0.1f, 0.2f), Quaternion.identity, OakBarkDark, 5, 2);
                }
            }
            // Footprints behind it, back the way it came, with oak saplings already sprouting in the older ones.
            for (int k = 1; k <= 6; k++)
            {
                var p = new Vector3((k % 2 == 0 ? -4.5f : 4.5f) + rng.NextFloat(-1f, 1f), 0.4f, -12f - k * 12f);
                loose.Ellipsoid(p, new Vector3(5f, 0.5f, 4.5f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Loam, 10, 3);
                loose.Ellipsoid(p + Vector3.up * 0.15f, new Vector3(3.4f, 0.4f, 3f), Quaternion.identity, new Color(0.24f, 0.18f, 0.12f), 8, 3);
                for (int s = 0; s < k / 2; s++)
                {
                    var sp = p + new Vector3(rng.NextFloat(-2f, 2f), 0.3f, rng.NextFloat(-2f, 2f));
                    var h = rng.NextFloat(0.8f, 0.6f + k * 0.35f);
                    loose.Limb(sp, sp + Vector3.up * h, 0.08f, 0.04f, OakBark, 4);
                    loose.Ellipsoid(sp + Vector3.up * (h + 0.15f), new Vector3(0.4f, 0.25f, 0.4f) * (0.6f + h * 0.3f), Quaternion.identity, OakLeafLight, 6, 3);
                }
            }

            var body = m.ToMesh("WalkingOak");
            Weather(body, Moss, 0.6f, 0.95f);
            AddPart("Body", root, body);
            AddPart("Loose", root, loose.ToMesh("WalkingOakLoose"));
            var crownPivot = new GameObject("Crown").transform;
            crownPivot.SetParent(root, false);
            crownPivot.localPosition = crownPivotAt;
            AddPart("Leaves", crownPivot, crown.ToMesh("WalkingOakCrown"));

            // The rope swing on the low bough: two ropes down to a plank, on its own pivot at the bough so it can sway.
            var swingPivot = new GameObject("Swing").transform;
            swingPivot.SetParent(root, false);
            swingPivot.localPosition = swingAt;
            var swing = new DunesColossus.MeshBuilder();
            var drop = Mathf.Max(4f, swingAt.y - 3f);
            for (int sz = -1; sz <= 1; sz += 2)
            {
                swing.Limb(new Vector3(0f, 0f, sz * 0.8f), new Vector3(0f, -drop, sz * 0.8f), 0.07f, 0.07f, Rope, 4);
                swing.Ellipsoid(new Vector3(0f, 0.1f, sz * 0.8f), new Vector3(0.35f, 0.25f, 0.35f), Quaternion.identity, Rope, 6, 3);
            }
            swing.Limb(new Vector3(0f, -drop, -1f), new Vector3(0f, -drop, 1f), 0.3f, 0.3f, new Color(0.48f, 0.34f, 0.20f), 4);
            AddPart("Rope", swingPivot, swing.ToMesh("WalkingOakSwing"));

            // Leaves drifting down from the crown, bees round the hive, soil trickling off the lifted foot.
            Puffs(root, "FallingLeaves", forkTop + Vector3.up * 10f, new Vector3(36f, 8f, 36f), new Color(0.30f, 0.46f, 0.16f, 0.9f), new Color(0.74f, 0.50f, 0.18f, 0.9f), 0.25f, 0.5f, 7f, -1.1f, 14f);
            Puffs(root, "Bees", hiveAt + Vector3.down * 1f, new Vector3(3f, 3f, 3f), new Color(0.20f, 0.16f, 0.06f, 0.95f), new Color(0.75f, 0.60f, 0.15f, 0.95f), 0.08f, 0.14f, 10f, 0.05f, 2.5f);
            Puffs(root, "Soil", liftedFoot + Vector3.down * 1.5f, new Vector3(4f, 1f, 4f), new Color(0.30f, 0.22f, 0.14f, 0.9f), new Color(0.40f, 0.30f, 0.20f, 0.8f), 0.12f, 0.3f, 6f, -2.2f, 2.5f);

            OakRig.Crown = crownPivot;
            OakRig.Swing = swingPivot;
            OakRig.Hive = hiveAt;
            return root;
        }

        static class OakRig
        {
            public static Transform Crown, Swing;
            /// <summary>The hive, local to the colossus's root.</summary>
            public static Vector3 Hive;
            /// <summary>When it next creaks, a bird next sings in its crown, and the hive next hums (Time.time).</summary>
            public static float NextCreak, NextSong, NextHum;
        }

        /// <summary>
        /// A-84 row 3: the oak's crown sways a degree or so and the swing rocks on its ropes, slow and clock-driven, so
        /// every player sees much the same (cosmetic, local; the walker moves the whole root). It creaks every 8-16 s (a
        /// bowstring's draw pitched far down), a bird sings in the crown every 3-7 s by day, and the hive hums.
        /// Skipped when hidden or past 600 m from the camera.
        /// </summary>
        static void SwayOak()
        {
            if (OakRig.Crown == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - OakRig.Crown.position).sqrMagnitude > 600f * 600f)
                return;
            float t = Time.time;
            OakRig.Crown.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.31f) * 1.1f, 0f, Mathf.Sin(t * 0.23f + 1.3f) * 1.4f);
            if (OakRig.Swing != null)
                OakRig.Swing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.9f) * 9f);
            var crownAt = OakRig.Crown.position + Vector3.up * 14f;
            if (t >= OakRig.NextCreak)
            {
                OakRig.NextCreak = t + UnityEngine.Random.Range(8f, 16f);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Draw, OakRig.Crown.position, UnityEngine.Random.Range(0.25f, 0.33f), 1f, 220f, Punchy.Audio.SoundGroup.World);
            }
            if (t >= OakRig.NextSong)
            {
                OakRig.NextSong = t + UnityEngine.Random.Range(3f, 7f);
                if (RenderSettings.sun == null || RenderSettings.sun.transform.forward.y < -0.05f)
                    Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Song, crownAt + UnityEngine.Random.insideUnitSphere * 12f, UnityEngine.Random.Range(0.9f, 1.2f), 0.8f, 70f, Punchy.Audio.SoundGroup.Creatures);
            }
            if (t >= OakRig.NextHum)
            {
                OakRig.NextHum = t + UnityEngine.Random.Range(3f, 5f);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.BeeBuzz, OakRig.Crown.parent.TransformPoint(OakRig.Hive), 1f, 0.7f, 30f, Punchy.Audio.SoundGroup.Creatures);
            }
        }
    }
}
