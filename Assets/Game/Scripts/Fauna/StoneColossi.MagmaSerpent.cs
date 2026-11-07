using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-88 row 3 (#40): the volcano colossus. On v3 the volcano has no land cells of its own, so its ColossusDefinition
    // asset (Resources/Colossi/MagmaSerpent) names the VolcanoCone landmark instead of biomes: it lives in a crater. The
    // line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsMagmaSerpent = Register(Kind.MagmaSerpent, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildMagmaSerpent(source, at, yaw, seed));

        static readonly Color MagmaCrust = new Color(0.20f, 0.17f, 0.16f);
        static readonly Color MagmaBelly = new Color(0.34f, 0.18f, 0.12f);
        static readonly Color Magma = DunesColossus.MeshBuilder.Glowing(new Color(1.00f, 0.30f, 0.05f));   // (A-89: lit at night; warmer, it read yellow-green)
        static readonly Color Obsidian = new Color(0.06f, 0.06f, 0.09f);
        static readonly Color Sulphur = new Color(0.82f, 0.74f, 0.26f);
        static readonly Color ShedSkin = new Color(0.55f, 0.52f, 0.50f);

        /// <summary>
        /// A-88 row 3 (#40): a magma serpent living in a volcano's crater. Its crusted body loops right round the crater
        /// floor, breaching the cooled crust in four arcs out of glowing, cracked mounds, and the loop slides slowly round
        /// the crater (<see cref="SwayMagmaSerpent"/>, a turn every three minutes or so). Its neck rears out of the lava
        /// pool in the middle to a wedge head well above the rim, with a glowing maw, ember eyes and lava dripping from
        /// its jaw; the head turns slowly, watching the crater. Details: basalt plates down its back with magma glowing
        /// in the seams, a pale shed skin draped on the crater wall, obsidian shards and sulphur crust on the floor, a
        /// half-melted shield and sword of someone who came too close, smoke off the pool and sparks off the mounds.
        /// The maw, eyes and mounds glow (lanterns), brighter at night.
        /// </summary>
        static Transform BuildMagmaSerpent(in TerrainSource source, float3 at, float yaw, uint seed)
        {
            var root = new GameObject("MagmaSerpent").transform;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), turn);
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var src = source;
            float Ground(float x, float z)
            {
                var w = turn * new Vector3(x, 0f, z);
                return src.SampleHeight(new float2(at.x + w.x, at.z + w.z)) - at.y;
            }
            Vector3 OnGround(float x, float z, float lift = 0f) => new Vector3(x, Ground(x, z) + lift, z);
            // The crater, measured: its rim is the highest ground going out from the middle (the crater is round, so the
            // loop's arcs keep to the floor as it slides round), and the lava's surface in the middle.
            var rim = 30f;
            var rimHeight = 0f;
            for (float d = 6f; d < 90f; d += 1f)
            {
                var h = 0f;
                for (int k = 0; k < 4; k++)
                    h += Ground(Mathf.Cos(k * 1.57f) * d, Mathf.Sin(k * 1.57f) * d) * 0.25f;
                if (h > rimHeight)
                {
                    rimHeight = h;
                    rim = d;
                }
            }
            var middle = src.SampleGround(new float2(at.x, at.z));
            var lava = middle.HasWater ? math.max(0f, middle.WaterSurface - at.y) : 0f;

            var m = new DunesColossus.MeshBuilder();
            // On v3 the volcano has no land of its own, so in the Ashlands it usually finds no crater: then it brings its
            // own, a ring of cinder and basalt round a lava pool, broken on one side where a lava stream ran out.
            var ownCrater = rimHeight - Ground(0f, 0f) < 4f;
            var ownLava = 0f;
            var streamAt = 0f;
            if (ownCrater)
            {
                rim = 36f;
                rimHeight = Ground(0f, 0f) + 9f;
                var notch = rng.NextFloat(0f, Mathf.PI * 2f);
                streamAt = notch;
                var cinder = new Color(0.25f, 0.15f, 0.12f);
                for (int k = 0; k < 64; k++)
                {
                    var a = k / 64f * Mathf.PI * 2f;
                    var off = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, notch * Mathf.Rad2Deg));
                    if (off < 12f)
                        continue;
                    // A low, lumpy berm: lower towards the notch, its lip ragged, its faces slumping in and out.
                    var tall = (off < 28f ? 4f : 8f) * rng.NextFloat(0.75f, 1.2f);
                    var d = rim * rng.NextFloat(0.97f, 1.03f);
                    var along = Quaternion.Euler(rng.NextFloat(-10f, 10f), -a * Mathf.Rad2Deg, rng.NextFloat(-12f, 12f));
                    var colour = k % 5 == 0 ? cinder : k % 2 == 0 ? Basalt : Soot;
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, tall * 0.1f), new Vector3(6f, tall, 4f), along, colour, 10, 5);
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * (d + 8f), Mathf.Sin(a) * (d + 8f), 0f), new Vector3(9f, tall * 0.5f, 4.5f), along, k % 3 == 0 ? cinder : Soot, 8, 3);
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * (d - 6f), Mathf.Sin(a) * (d - 6f), 0f), new Vector3(7f, tall * 0.55f, 4.2f), along, Basalt, 8, 3);
                    if (k % 5 == 2)
                        m.Limb(OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, tall * 0.8f), OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, tall * 1.1f + rng.NextFloat(1.5f, 4f)) + new Vector3(rng.NextFloat(-1f, 1f), 0f, rng.NextFloat(-1f, 1f)), 1.1f, 0.15f, Obsidian, 5);
                    // Now and then a glowing crack down the inner face.
                    if (k % 7 == 3)
                        m.Limb(OnGround(Mathf.Cos(a) * (d - 2f), Mathf.Sin(a) * (d - 2f), tall * 0.7f), OnGround(Mathf.Cos(a) * (d - 8f), Mathf.Sin(a) * (d - 8f), 0.3f), 0.35f, 0.2f, Magma, 4);
                }
                // The lava pool in the middle, crusting at its edge, with plates of cooled crust drifting on it.
                ownLava = 0.25f;
                m.Ellipsoid(OnGround(0f, 0f, 0.1f), new Vector3(15f, 0.35f, 15f), Quaternion.identity, Magma, 20, 3);
                m.Ellipsoid(OnGround(0f, 0f, 0.0f), new Vector3(17.5f, 0.3f, 17.5f), Quaternion.identity, Soot, 20, 3);
                for (int k = 0; k < 9; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var d = rng.NextFloat(5f, 13f);
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.35f), new Vector3(rng.NextFloat(1f, 2.6f), 0.2f, rng.NextFloat(0.8f, 2f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), MagmaCrust, 7, 2);
                }
                // The lava stream out through the notch, glowing where it is still running, and dark where it cooled.
                for (int k = 0; k < 18; k++)
                {
                    var d = 14f + k * 2.6f;
                    var a = notch + Mathf.Sin(k * 0.7f) * 0.06f;
                    var w = Mathf.Lerp(3.2f, 1.6f, k / 17f);
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.15f), new Vector3(w, 0.25f, 2f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), k < 12 ? Magma : MagmaCrust, 8, 2);
                    m.Ellipsoid(OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.05f), new Vector3(w + 1.2f, 0.3f, 2.2f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), Soot, 8, 2);
                }
                lava = Ground(0f, 0f) + ownLava;
            }
            var coil = new DunesColossus.MeshBuilder();
            var h2 = new DunesColossus.MeshBuilder();

            // The loop round the crater floor: ring segments round a circle, rising and dipping four times, crust and
            // belly-bronze in turn, a basalt plate on the back of every other ring with magma in the seam behind it.
            // Rings under the crust are skipped; each breach gets a cracked, glowing mound.
            var loop = math.clamp(rim * 0.6f, 15f, 40f);
            var thick = math.clamp(rim * 0.12f, 3f, 5.5f);
            var segs = 72;
            Vector3 Loop(float t)
            {
                var a = t * Mathf.PI * 2f;
                var x = Mathf.Cos(a) * loop;
                var z = Mathf.Sin(a) * loop;
                return new Vector3(x, Ground(x, z) + Mathf.Sin(a * 4f) * thick * 2.2f + thick * 0.4f, z);
            }
            var breaches = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < segs; i++)
            {
                var t = i / (float)segs;
                var p = Loop(t);
                var ground = Ground(p.x, p.z);
                var r = thick * (1f + 0.06f * Mathf.Sin(i * 2.3f));
                var tangent = Loop(t + 0.003f) - Loop(t - 0.003f);
                var rot = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                var up = rot * Vector3.up;
                var side = rot * Vector3.right;
                var buried = p.y + r < ground - 0.3f;
                var wasBuried = Loop(t - 1f / segs).y + r < ground - 0.3f;
                if (buried != wasBuried)
                    breaches.Add(new Vector3(p.x, ground, p.z));
                if (buried)
                    continue;
                var step = (Loop(t + 1f / segs) - p).magnitude;
                coil.Ellipsoid(p, new Vector3(r, r * 0.92f, math.max(step * 0.85f, r * 0.5f)), rot, i % 2 == 0 ? MagmaCrust : MagmaBelly, 12, 6);
                // (Magma glowing in the seam between this ring and the next, along both flanks.)
                coil.Ellipsoid(p + rot * Vector3.forward * step * 0.5f, new Vector3(r * 0.97f, r * 0.88f, 0.25f), rot, Magma, 12, 3);
                if (i % 2 == 0 && p.y + r > ground)
                {
                    var plate = Quaternion.LookRotation(tangent.normalized, up) * Quaternion.Euler(-18f, 0f, 0f);
                    coil.Ellipsoid(p + up * r * 0.92f, new Vector3(r * 0.55f, 0.45f, r * 0.7f), plate, Basalt, 7, 3);
                    coil.Limb(p + up * r * 0.9f, p + up * (r + 1.4f + rng.NextFloat(0f, 1f)) - tangent.normalized * 1f, r * 0.13f, 0.06f, Obsidian, 5);
                }
                // Clinker clinging to the flanks, and now and then a drip of lava still running off it.
                if (rng.NextFloat() < 0.3f)
                    coil.Ellipsoid(p + side * (rng.NextFloat() < 0.5f ? -r : r) * 0.95f + up * rng.NextFloat(-0.3f, 0.4f) * r, new Vector3(0.4f, r * 0.3f, r * 0.35f), rot, Soot, 6, 3);
                if (i % 5 == 2 && p.y - r > ground + 1.5f)
                {
                    var drip = p - up * r * 0.9f;
                    coil.Limb(drip, drip + Vector3.down * math.min(2.5f, p.y - r - ground), 0.22f, 0.05f, Magma, 5);
                }
            }
            // The breach mounds: crust heaved up and split, magma showing in the cracks, slabs tipped up round it.
            var coilLights = new System.Collections.Generic.List<Vector3>();
            foreach (var b in breaches)
            {
                var size = thick * 1.6f;
                coil.Ellipsoid(b + Vector3.up * 0.2f, new Vector3(size, size * 0.4f, size), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Soot, 12, 5);
                for (int k = 0; k < 5; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var crack = b + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * 0.5f + Vector3.up * size * 0.32f;
                    coil.Ellipsoid(crack, new Vector3(0.35f, 0.2f, size * 0.55f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), Magma, 5, 2);
                }
                for (int k = 0; k < 4; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var d = size * rng.NextFloat(1f, 1.5f);
                    var slab = new Vector3(b.x + Mathf.Cos(a) * d, 0f, b.z + Mathf.Sin(a) * d);
                    slab.y = Ground(slab.x, slab.z) + 0.5f;
                    coil.Ellipsoid(slab, new Vector3(rng.NextFloat(1.2f, 2.4f), 0.45f, rng.NextFloat(1f, 1.8f)),
                                   Quaternion.Euler(rng.NextFloat(-40f, 40f), rng.NextFloat(0f, 360f), rng.NextFloat(-40f, 40f)), k % 2 == 0 ? Basalt : Obsidian, 6, 3);
                }
                coilLights.Add(b + Vector3.up * size * 0.5f);
            }

            // The neck rears out of the lava pool to a wedge head well above the rim. Its own part, pivoted at the pool,
            // so the head can turn to watch the crater.
            var neckBase = new Vector3(0f, lava, 0f);
            var top = rimHeight + 16f;
            var n0 = new Vector3(0f, lava - 6f, -3f);
            var n1 = new Vector3(0f, top * 0.45f, -6f);
            var n2 = new Vector3(0f, top * 0.95f, -1f);
            var n3 = new Vector3(0f, top, 7f);
            Vector3 Neck(float t) { var u = 1f - t; return u * u * u * n0 + 3f * u * u * t * n1 + 3f * u * t * t * n2 + t * t * t * n3; }
            var neckSegs = 24;
            for (int i = 0; i <= neckSegs; i++)
            {
                var t = i / (float)neckSegs;
                var p = Neck(t);
                if (p.y < lava - 1f)
                    continue;
                var tangent = Neck(math.min(1f, t + 0.01f)) - Neck(math.max(0f, t - 0.01f));
                var rot = Quaternion.LookRotation(tangent.normalized, Vector3.forward);
                var r = Mathf.Lerp(thick * 1.25f, thick * 0.95f, t);
                var step = (Neck(math.min(1f, t + 1f / neckSegs)) - p).magnitude;
                h2.Ellipsoid(p, new Vector3(r, r * 0.95f, math.max(step * 0.85f, r * 0.5f)), rot, i % 2 == 0 ? MagmaCrust : MagmaBelly, 12, 6);
                h2.Ellipsoid(p + rot * Vector3.forward * step * 0.5f, new Vector3(r * 0.97f, r * 0.9f, 0.25f), rot, Magma, 12, 3);
                // (The back of the neck: a crest of basalt plates, tipped back.)
                if (i % 2 == 1)
                {
                    var back = rot * Vector3.down;
                    h2.Limb(p + back * r * 0.85f, p + back * (r + 1.6f) - tangent.normalized * 0.8f, r * 0.2f, 0.08f, Basalt, 5);
                }
            }
            // The head: a wedge of crusted plates, jaws open on a glowing throat, a horn crest swept back, ember eyes,
            // lava dripping from the lower jaw.
            var head = Neck(1f);
            var fwd = (Neck(1f) - Neck(0.95f)).normalized;
            fwd = (fwd + Vector3.down * 0.35f).normalized;
            var face = Quaternion.LookRotation(fwd, Vector3.up);
            Vector3 hUp = face * Vector3.up, hSide = face * Vector3.right;
            var s0 = thick;
            h2.Ellipsoid(head, new Vector3(s0 * 1.15f, s0 * 0.9f, s0 * 1.4f), face, MagmaCrust, 14, 8);
            var snout = head + fwd * s0 * 1.5f;
            var upperJaw = face * Quaternion.Euler(-14f, 0f, 0f);
            var lowerJaw = face * Quaternion.Euler(22f, 0f, 0f);
            h2.Ellipsoid(snout + hUp * s0 * 0.25f, new Vector3(s0 * 0.75f, s0 * 0.35f, s0 * 1.3f), upperJaw, MagmaCrust, 12, 6);
            h2.Ellipsoid(snout - hUp * s0 * 0.55f, new Vector3(s0 * 0.65f, s0 * 0.28f, s0 * 1.15f), lowerJaw, MagmaBelly, 12, 6);
            var maw = snout - hUp * s0 * 0.1f;
            h2.Ellipsoid(maw - fwd * s0 * 0.2f, new Vector3(s0 * 0.55f, s0 * 0.4f, s0 * 0.9f), face, Magma, 10, 5);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                for (int k = 0; k < 6; k++)
                {
                    var along = snout + fwd * (s0 * 0.9f - k * s0 * 0.32f) + hSide * sd * s0 * (0.55f + k * 0.04f);
                    h2.Limb(along + hUp * s0 * 0.05f, along - hUp * s0 * 0.35f, 0.3f, 0.03f, Obsidian, 4);
                    h2.Limb(along - hUp * s0 * 0.7f, along - hUp * s0 * 0.4f, 0.26f, 0.03f, Obsidian, 4);
                }
                var eye = head + hSide * sd * s0 * 0.8f + hUp * s0 * 0.45f + fwd * s0 * 0.35f;
                h2.Ellipsoid(eye, new Vector3(0.7f, 0.45f, 0.9f), face, Magma, 8, 4);
                h2.Ellipsoid(eye + hUp * 0.45f, new Vector3(0.9f, 0.3f, 1.1f), face, Basalt, 6, 3);   // a heavy brow
                // The horn crest: three swept-back horns either side.
                for (int k = 0; k < 3; k++)
                {
                    var horn = head + hSide * sd * s0 * (0.55f - k * 0.1f) + hUp * s0 * 0.7f - fwd * s0 * (0.2f + k * 0.45f);
                    h2.Limb(horn, horn + hUp * (2.2f - k * 0.4f) - fwd * (3.4f - k * 0.5f) + hSide * sd * 0.8f, 0.6f - k * 0.1f, 0.06f, Obsidian, 6);
                }
            }
            for (int k = 0; k < 4; k++)
            {
                var chin = snout - hUp * s0 * 0.75f + hSide * rng.NextFloat(-0.4f, 0.4f) * s0 + fwd * rng.NextFloat(-0.5f, 0.6f) * s0;
                h2.Limb(chin, chin + Vector3.down * rng.NextFloat(1.2f, 3.2f), 0.26f, 0.05f, Magma, 5);
            }

            // On the crater floor (the still part): a pale shed skin draped up the crater wall, obsidian shards, sulphur
            // crust round the pool, and a shield and sword half sunk and melted where someone came too close.
            var skinAt = rng.NextFloat(0f, Mathf.PI * 2f);
            for (int k = 0; k < 9; k++)
            {
                var a = skinAt + k * 0.07f;
                var d = math.lerp(loop * 1.15f, rim * 0.97f, k / 8f);
                var p = OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.3f);
                var along = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                m.Ellipsoid(p, new Vector3(thick * 0.8f, 0.5f, thick * 0.9f), along * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 8f : -8f), ShedSkin, 9, 3);
                if (k % 2 == 0)
                    m.Ellipsoid(p + Vector3.up * 0.4f, new Vector3(thick * 0.45f, 0.2f, thick * 0.4f), along, Color.Lerp(ShedSkin, Ash, 0.5f), 6, 2);
            }
            for (int k = 0; k < 16; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var d = rng.NextFloat(loop * 0.75f, rim * 0.95f);
                var shard = OnGround(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0f);
                m.Limb(shard - Vector3.up * 0.3f, shard + new Vector3(rng.NextFloat(-0.6f, 0.6f), rng.NextFloat(1f, 2.6f), rng.NextFloat(-0.6f, 0.6f)), rng.NextFloat(0.25f, 0.5f), 0.04f, Obsidian, 4);
            }
            var poolEdge = math.max(8f, loop * 0.62f);
            for (int k = 0; k < 10; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = OnGround(Mathf.Cos(a) * poolEdge * rng.NextFloat(0.95f, 1.15f), Mathf.Sin(a) * poolEdge * rng.NextFloat(0.95f, 1.15f), 0.05f);
                m.Ellipsoid(p, new Vector3(rng.NextFloat(0.8f, 1.8f), 0.15f, rng.NextFloat(0.6f, 1.4f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Sulphur, 6, 2);
            }
            var gearAt = skinAt + Mathf.PI * 0.8f;
            var gear = OnGround(Mathf.Cos(gearAt) * rim * 0.85f, Mathf.Sin(gearAt) * rim * 0.85f, 0.1f);
            m.Ellipsoid(gear, new Vector3(0.9f, 0.12f, 1.1f), Quaternion.Euler(14f, gearAt * Mathf.Rad2Deg, -22f), Rust, 8, 3);   // the shield, slumped
            m.Ellipsoid(gear + new Vector3(0.1f, 0.12f, 0.1f), new Vector3(0.25f, 0.1f, 0.25f), Quaternion.identity, Ember, 5, 2);   // still hot at the boss
            m.Limb(gear + new Vector3(1.6f, -0.3f, 0.4f), gear + new Vector3(2.4f, 1f, 1.8f), 0.07f, 0.05f, Rust, 4);   // the sword, stuck in and drooping
            m.Limb(gear + new Vector3(2.4f, 1f, 1.8f), gear + new Vector3(2.9f, 0.6f, 2.3f), 0.05f, 0.03f, Rust, 4);
            m.Limb(gear + new Vector3(2.25f, 0.85f, 1.55f), gear + new Vector3(2.55f, 1.05f, 1.95f), 0.12f, 0.12f, SledWood, 4);   // its grip, charred

            // A-89 row 2: the build cost 47-82 ms on one frame. This frame: the crater, loop and head shapes; the next
            // (BuildRest): their meshes, the pivots, glows and puffs. No draws in the second half, so the shape is unchanged.
            BuildRest = () => { if (root != null) MagmaRest(); };
            return root;

            void MagmaRest()
            {
                AddPart("Floor", root, m.ToMesh("MagmaSerpentFloor"));
                var coilPivot = new GameObject("Coil turn").transform;
                coilPivot.SetParent(root, false);
                var coilMesh = coil.ToMesh("MagmaSerpentCoil");
                Weather(coilMesh, Ash, 0.6f, 0.95f);
                AddPart("Coil", coilPivot, coilMesh);
                var neckPivot = new GameObject("Neck").transform;
                neckPivot.SetParent(root, false);
                neckPivot.localPosition = neckBase;
                var headMesh = h2.ToMesh("MagmaSerpentHead");
                Weather(headMesh, Ash, 0.7f, 0.97f);
                AddPart("Head", neckPivot, headMesh);
                neckPivot.GetChild(0).localPosition = -neckBase;
                MagmaRig.Coil = coilPivot;
                MagmaRig.Neck = neckPivot;
                MagmaRig.Maw = maw;

                // Glow: the maw and eyes, and each breach mound (those turn with the loop).
                var headPart = neckPivot.GetChild(0);
                Glow(headPart, "Maw glow", maw + fwd * 0.5f, 3.4f);
                for (int sd = -1; sd <= 1; sd += 2)
                    Glow(headPart, "Eye glow", head + hSide * sd * s0 * 0.85f + hUp * s0 * 0.45f + fwd * s0 * 0.5f, 0.8f);
                for (int k = 0; k < coilLights.Count; k++)
                    Glow(coilPivot, "Breach glow", coilLights[k], 2.2f);

                if (ownCrater)
                {
                    Glow(root, "Pool glow", OnGround(0f, 0f, 1f), 4.5f);
                    Glow(root, "Pool glow", OnGround(9f, 0f, 1f), 3f);
                    Glow(root, "Pool glow", OnGround(-6f, -7f, 1f), 3f);
                    Glow(root, "Stream glow", OnGround(Mathf.Cos(streamAt) * 24f, Mathf.Sin(streamAt) * 24f, 0.6f), 2f);
                    Glow(root, "Stream glow", OnGround(Mathf.Cos(streamAt) * 38f, Mathf.Sin(streamAt) * 38f, 0.6f), 1.6f);
                }
                // Smoke off the pool, sparks off the breaches, and heat shimmer of smoke from the maw.
                Puffs(root, "Pool smoke", new Vector3(0f, lava + 1f, 0f), new Vector3(loop * 0.8f, 1f, loop * 0.8f), new Color(0.30f, 0.28f, 0.27f, 0.45f), new Color(0.45f, 0.40f, 0.36f, 0.35f), 3f, 7f, 2.5f, 1.4f, 9f);
                Puffs(headPart, "Maw smoke", maw + fwd * 2f, new Vector3(2f, 1.5f, 2f), new Color(0.35f, 0.32f, 0.30f, 0.45f), new Color(0.55f, 0.45f, 0.38f, 0.35f), 1.5f, 3.5f, 2f, 1.2f, 6f);
                foreach (var b in breaches)
                    Puffs(coilPivot, "Sparks", b + Vector3.up * 1.5f, new Vector3(thick * 2f, 1f, thick * 2f), new Color(1f, 0.6f, 0.15f, 0.95f), new Color(1f, 0.35f, 0.08f, 0.9f), 0.12f, 0.28f, 3f, 2.2f, 2.5f);
            }

            static void Glow(Transform parent, string name, Vector3 local, float size)
            {
                var point = new GameObject(name).transform;
                point.SetParent(parent, false);
                point.localPosition = local;
                point.localScale = Vector3.one * size;
                Lanterns.Add(point);
            }
        }

        static class MagmaRig
        {
            public static Transform Coil, Neck;
            /// <summary>When it next rumbles (Time.time).</summary>
            public static float NextRumble;
            /// <summary>The maw, in the head part's space.</summary>
            public static Vector3 Maw;
        }

        /// <summary>
        /// A-88 row 3: the magma serpent's loop slides slowly round the crater (about 2 degrees a second) while its head
        /// turns to watch the crater and sways a little. Cosmetic and clock-driven, so every player sees much the same;
        /// skipped when hidden or past 600 m from the camera. Every 16-28 s it rumbles from the maw.
        /// </summary>
        static void SwayMagmaSerpent()
        {
            if (MagmaRig.Coil == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - MagmaRig.Coil.position).sqrMagnitude > 600f * 600f)
                return;
            float t = Time.time;
            MagmaRig.Coil.localRotation = Quaternion.Euler(0f, t * 2f % 360f, 0f);
            MagmaRig.Neck.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.17f) * 2f, Mathf.Sin(t * 0.05f) * 40f + Mathf.Sin(t * 0.23f) * 4f, Mathf.Sin(t * 0.13f + 1f) * 2.5f);
            if (t >= MagmaRig.NextRumble)
            {
                MagmaRig.NextRumble = t + UnityEngine.Random.Range(16f, 28f);
                var head = MagmaRig.Neck.GetChild(0).TransformPoint(MagmaRig.Maw);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Growl, head, UnityEngine.Random.Range(0.3f, 0.38f), 0.7f, 320f, Punchy.Audio.SoundGroup.World);
            }
        }
    }
}
