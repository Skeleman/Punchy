using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-85 row 2 (#40): the FungalForest colossus. Its ColossusDefinition asset (Resources/Colossi/Puffball) says where it
    // stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsPuffball = Register(Kind.Puffball, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildPuffball(at, yaw, seed));

        static readonly Color PuffSkin = new Color(0.86f, 0.82f, 0.72f);
        static readonly Color PuffShade = new Color(0.72f, 0.66f, 0.58f);
        static readonly Color PuffCrack = new Color(0.44f, 0.36f, 0.31f);
        static readonly Color PuffGill = new Color(0.60f, 0.50f, 0.66f);
        static readonly Color PuffGillDark = new Color(0.40f, 0.32f, 0.46f);
        static readonly Color PuffTendril = new Color(0.80f, 0.74f, 0.72f);
        static readonly Color SporeGlow = DunesColossus.MeshBuilder.Glowing(new Color(0.55f, 1.00f, 0.82f), 0.8f);   // (A-89: lit at night)
        static readonly Color Wicker = new Color(0.58f, 0.44f, 0.26f);

        /// <summary>
        /// A-85 row 2 (#40): the FungalForest colossus, a puffball about 30 m across drifting 20-odd metres up on the air.
        /// A cream, warty skin cracks and peels round the dark vent on its crown; glowing pores dot its flanks, a ruffled
        /// lilac frill of gills rings its underside, and pale tendrils hang from it with glowing beads at their tips, one
        /// long rootlet trailing on the ground behind it. Seeded per world: its size, height, lobes and tendrils.
        /// Details: two young puffballs budding on its side, a harvester's basket caught on a tendril (the rope snapped),
        /// moths round the pores, and a slick of spores where the rootlet drags. Glowing spores drift up out of the vent and
        /// down off its skin; every so often it breathes out a burst of them with a soft hiss (<see cref="SwayPuffball"/>),
        /// and the tendrils drip. It glows by night (the pores and beads), so it gets a night shot.
        /// </summary>
        static Transform BuildPuffball(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("Puffball").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), Quaternion.Euler(0f, yaw, 0f));
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var lift = rng.NextFloat(22f, 26f);
            var R = new Vector3(rng.NextFloat(13.5f, 16f), 0f, 0f);
            R.y = R.x * rng.NextFloat(0.74f, 0.84f);
            R.z = R.x * rng.NextFloat(0.94f, 1.06f);
            var m = new DunesColossus.MeshBuilder();
            var loose = new DunesColossus.MeshBuilder();
            Vector3 Surface(Vector3 dir) => Vector3.Scale(dir.normalized, R);

            // The body and three soft lobes, so it is not a perfect ball.
            m.Ellipsoid(Vector3.zero, R, Quaternion.identity, PuffSkin, 26, 14);
            for (int k = 0; k < 3; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Sin(a), rng.NextFloat(-0.3f, 0.2f), Mathf.Cos(a));
                m.Ellipsoid(Surface(dir) * 0.55f, R * rng.NextFloat(0.62f, 0.72f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), PuffShade, 16, 8);
            }
            // Warts over the upper half, small and many.
            for (int k = 0; k < 70; k++)
            {
                var dir = new Vector3(rng.NextFloat(-1f, 1f), rng.NextFloat(-0.15f, 1f), rng.NextFloat(-1f, 1f));
                var r = rng.NextFloat(0.4f, 1.3f);
                m.Ellipsoid(Surface(dir) * 0.99f, new Vector3(r, r * 0.7f, r), Quaternion.FromToRotation(Vector3.up, dir.normalized), k % 3 == 0 ? PuffShade : PuffSkin, 6, 3);
            }
            // The vent on the crown, dark with a glowing throat, and the skin cracked and peeling round it.
            var vent = new Vector3(0f, R.y * 0.98f, 0f);
            m.Ellipsoid(vent, new Vector3(3.2f, 0.9f, 3.2f), Quaternion.identity, PuffCrack, 12, 4);
            m.Ellipsoid(vent + Vector3.up * 0.3f, new Vector3(2.3f, 0.7f, 2.3f), Quaternion.identity, Hollow, 10, 3);
            m.Ellipsoid(vent + Vector3.up * 0.4f, new Vector3(1.4f, 0.5f, 1.4f), Quaternion.identity, SporeGlow, 8, 3);
            for (int k = 0; k < 9; k++)
            {
                var a = k * Mathf.PI * 2f / 9f + rng.NextFloat(-0.2f, 0.2f);
                Vector3 Crack(float t) => Surface(new Vector3(Mathf.Sin(a + t * 0.4f) * t, 1f - t * 0.55f, Mathf.Cos(a + t * 0.4f) * t)) * 1.005f;
                var len = rng.NextFloat(0.6f, 1.1f);
                Tentacle(m, Crack(0.15f), Crack(0.15f + len * 0.33f), Crack(0.15f + len * 0.66f), Crack(0.15f + len), 0.55f, 0.12f, PuffCrack, PuffCrack, 6);
                // A curl of peeled skin lifting off beside the crack.
                var p = Crack(0.15f + len * 0.5f);
                m.Ellipsoid(p + p.normalized * 0.5f, new Vector3(1.6f, 0.18f, 1.1f), Quaternion.FromToRotation(Vector3.up, (p.normalized + Vector3.up).normalized), PuffSkin, 6, 2);
            }
            // Glowing pores on the flanks (each a glow point), and two young puffballs budding on its side.
            var pores = new System.Collections.Generic.List<Vector3>();
            for (int k = 0; k < 12; k++)
            {
                var a = k * Mathf.PI * 2f / 12f + rng.NextFloat(-0.15f, 0.15f);
                var dir = new Vector3(Mathf.Sin(a), (k % 3 - 1) * 0.35f, Mathf.Cos(a));
                var p = Surface(dir) * 1.0f;
                m.Ellipsoid(p, new Vector3(1.1f, 0.35f, 1.1f), Quaternion.FromToRotation(Vector3.up, dir.normalized), PuffCrack, 8, 3);
                m.Ellipsoid(p + dir.normalized * 0.15f, new Vector3(0.65f, 0.3f, 0.65f), Quaternion.FromToRotation(Vector3.up, dir.normalized), SporeGlow, 6, 3);
                pores.Add(p + dir.normalized * 0.6f);
            }
            for (int k = 0; k < 2; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Sin(a), rng.NextFloat(0.1f, 0.5f), Mathf.Cos(a)).normalized;
                var r = rng.NextFloat(1.6f, 2.6f);
                var bud = Surface(dir) + dir * r * 0.6f;
                m.Ellipsoid(bud, new Vector3(r, r * 0.85f, r), Quaternion.identity, PuffSkin, 10, 6);
                m.Ellipsoid(bud + Vector3.up * r * 0.8f, new Vector3(r * 0.25f, r * 0.12f, r * 0.25f), Quaternion.identity, PuffCrack, 6, 2);
            }

            // The frill: a ruffled ring of gill flaps round the underside, and the gills under it running to the middle.
            var frillY = -R.y * 0.62f;
            var frillR = R.x * 0.82f;
            for (int k = 0; k < 26; k++)
            {
                var a = k * Mathf.PI * 2f / 26f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var p = d * frillR + Vector3.up * (frillY + (k % 2 == 0 ? 0.5f : -0.4f));
                m.Ellipsoid(p, new Vector3(2.6f, 0.4f, 3.4f), Quaternion.LookRotation(d) * Quaternion.Euler(-28f + (k % 2) * 12f, 0f, 0f), k % 2 == 0 ? PuffGill : PuffGillDark, 8, 3);
            }
            m.Ellipsoid(Vector3.up * (frillY - 0.6f), new Vector3(frillR * 0.9f, 1.2f, frillR * 0.9f), Quaternion.identity, PuffGillDark, 20, 4);
            for (int k = 0; k < 30; k++)
            {
                var a = k * Mathf.PI * 2f / 30f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                m.Limb(d * 2f + Vector3.up * (frillY - 1.6f), d * frillR * 0.88f + Vector3.up * (frillY - 1f), 0.25f, 0.35f, PuffGill, 4);
            }

            // Tendrils hanging from the underside on their own pivot (they sway), glowing beads at their tips; a harvester's
            // basket caught on one. Left out of its solid shape (the definition's CollisionLeave "Loose").
            var tendrilPivot = new GameObject("Tendrils").transform;
            var cap = new GameObject("Cap").transform;
            cap.SetParent(root, false);
            cap.localPosition = Vector3.up * lift;
            tendrilPivot.SetParent(cap, false);
            tendrilPivot.localPosition = Vector3.up * (frillY - 1f);
            var tips = new System.Collections.Generic.List<Vector3>();
            int count = rng.NextInt(8, 12);
            for (int k = 0; k < count; k++)
            {
                var a = k * Mathf.PI * 2f / count + rng.NextFloat(-0.25f, 0.25f);
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var from = d * rng.NextFloat(3f, frillR * 0.75f);
                var drop = rng.NextFloat(lift * 0.45f, lift * 0.72f);
                var tip = from + d * rng.NextFloat(-1f, 3f) + Vector3.down * drop;
                var side = new Vector3(d.z, 0f, -d.x) * rng.NextFloat(-2.5f, 2.5f);
                Tentacle(loose, from, from + Vector3.down * drop * 0.35f + side, tip + Vector3.up * drop * 0.3f - side, tip, 0.55f, 0.12f, PuffTendril, PuffGill, 10);
                loose.Ellipsoid(tip + Vector3.down * 0.25f, new Vector3(0.45f, 0.6f, 0.45f), Quaternion.identity, SporeGlow, 6, 4);
                tips.Add(tip + Vector3.down * 0.3f);
                if (k == 1)
                {
                    // The basket, hanging by a snapped rope wound round the tendril.
                    var hook = Vector3.Lerp(from, tip, 0.62f);
                    for (int w = 0; w < 4; w++)
                        loose.Ellipsoid(hook + Vector3.up * w * 0.3f, new Vector3(0.7f, 0.12f, 0.7f), Quaternion.Euler(0f, w * 40f, 8f), Rope, 6, 2);
                    var basket = hook + Vector3.down * 3.2f + d * 0.4f;
                    loose.Limb(hook, basket + Vector3.up * 1f, 0.06f, 0.06f, Rope, 4);
                    loose.Limb(hook + Vector3.down * 0.3f, hook + Vector3.down * 1.6f + new Vector3(d.z, 0f, -d.x) * 1.2f, 0.06f, 0.04f, Rope, 4);   // the snapped end
                    loose.Ellipsoid(basket, new Vector3(1f, 0.8f, 1f), Quaternion.Euler(0f, 0f, 14f), Wicker, 10, 5);
                    loose.Ellipsoid(basket + Vector3.up * 0.65f, new Vector3(0.9f, 0.15f, 0.9f), Quaternion.Euler(0f, 0f, 14f), Hollow, 8, 2);
                    loose.Limb(basket + new Vector3(-0.8f, 0.6f, 0f), basket + new Vector3(0.8f, 0.9f, 0f), 0.06f, 0.06f, Wicker, 4);
                    loose.Ellipsoid(basket + new Vector3(0.2f, 0.75f, 0.2f), new Vector3(0.35f, 0.25f, 0.3f), Quaternion.identity, SporeGlow, 5, 3);   // a puff it picked
                }
            }
            // The rootlet trailing behind it (-z, back the way it drifts), its end dragging on the ground in a slick of spores.
            var rootFrom = new Vector3(0f, 0f, -frillR * 0.3f);
            var rootEnd = new Vector3(rng.NextFloat(-3f, 3f), -(lift + frillY - 1f) + 0.4f, -lift * 1.1f);
            Tentacle(loose, rootFrom, rootFrom + new Vector3(2f, -lift * 0.4f, -3f), rootEnd + new Vector3(-2f, 6f, 6f), rootEnd, 0.7f, 0.25f, PuffTendril, PuffCrack, 14);
            for (int k = 0; k < 5; k++)
                loose.Limb(rootEnd, rootEnd + new Vector3(rng.NextFloat(-2.5f, 2.5f), -0.3f, rng.NextFloat(-3f, 1f)), 0.2f, 0.04f, PuffTendril, 4);
            loose.Ellipsoid(rootEnd + Vector3.down * 0.35f, new Vector3(3.5f, 0.12f, 5f), Quaternion.identity, DunesColossus.MeshBuilder.Glowing(Color.Lerp(SporeGlow, PuffGill, 0.5f), 0.1f), 10, 2);

            var body = m.ToMesh("Puffball");
            Weather(body, PuffShade, 0.86f, 1f);
            AddPart("Body", cap, body);
            AddPart("Loose", tendrilPivot, loose.ToMesh("PuffballTendrils"));

            // Glowing spores drifting up out of the vent and down off its skin, moths round the pores, spores off the rootlet.
            var glowA = new Color(0.50f, 1.00f, 0.80f, 0.85f);
            var glowB = new Color(0.80f, 1.00f, 0.60f, 0.75f);
            Puffs(cap, "Spores", vent + Vector3.up * 1f, new Vector3(3f, 1f, 3f), glowA, glowB, 0.3f, 0.7f, 12f, 1.1f, 10f);
            Puffs(cap, "SporeShed", Vector3.zero, new Vector3(R.x * 1.8f, R.y, R.z * 1.8f), glowA, glowB, 0.18f, 0.45f, 10f, -0.45f, 14f);
            Puffs(cap, "Moths", Vector3.zero, new Vector3(R.x * 2.3f, R.y * 1.2f, R.z * 2.3f), new Color(0.92f, 0.90f, 0.82f, 0.95f), new Color(0.75f, 0.70f, 0.62f, 0.95f), 0.12f, 0.22f, 3f, 0.05f, 5f);
            Puffs(root, "RootletSpores", rootEnd + Vector3.up * (lift + frillY - 1f) + Vector3.up * 0.5f, new Vector3(4f, 0.5f, 6f), glowA, glowB, 0.15f, 0.35f, 4f, 0.25f, 6f);

            PuffRig.Cap = cap;
            PuffRig.Tendrils = tendrilPivot;
            PuffRig.Lift = lift;
            PuffRig.Pores = pores.ToArray();
            PuffRig.Tips = tips.ToArray();
            PuffRig.Vent = cap.Find("Spores")?.GetComponent<ParticleSystem>();
            PuffRig.Breath = -1f;
            PuffRig.VentAt = vent + Vector3.up * 1f;
            return root;
        }

        static class PuffRig
        {
            public static Transform Cap, Tendrils;
            public static float Lift;
            /// <summary>The vent's mouth, local to the cap.</summary>
            public static Vector3 VentAt;
            /// <summary>The glowing pores (local to the cap) and the bead at each tendril's tip (local to the tendril pivot).</summary>
            public static Vector3[] Pores, Tips;
            public static ParticleSystem Vent;
            /// <summary>When it next breathes out a burst, a tendril next drips (Time.time); when the last burst began.</summary>
            public static float NextBreath, NextDrip, Breath;
            public static ParticleSystem.EmitParams Burst;
        }

        /// <summary>
        /// A-85 row 2: the puffball bobs a metre or so on the air, turns slowly and swells and shrinks a little, its tendrils
        /// swinging a beat behind; clock-driven, so every player sees much the same (cosmetic, local; the walker moves the
        /// whole root). Every 9-16 s it breathes out a burst of glowing spores from the vent with a soft, low hiss, squashing
        /// as it does; a tendril drips every 4-8 s. Its pores and tendril beads glow green, faint by day and bright at night,
        /// lighting what is round them. Skipped when hidden or past 600 m from the camera.
        /// </summary>
        void SwayPuffball()
        {
            if (PuffRig.Cap == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam == null || (cam.transform.position - PuffRig.Cap.position).sqrMagnitude > 600f * 600f)
                return;
            float t = Time.time;
            var since = PuffRig.Breath < 0f ? 99f : t - PuffRig.Breath;
            var squash = since < 3f ? Mathf.Sin(since / 3f * Mathf.PI) * 0.06f : 0f;
            var swell = 1f + 0.018f * Mathf.Sin(t * 0.7f);
            PuffRig.Cap.localPosition = Vector3.up * (PuffRig.Lift + 1.2f * Mathf.Sin(t * 0.45f) - squash * 6f);
            PuffRig.Cap.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.21f) * 2.5f, t * 1.2f, Mathf.Sin(t * 0.17f + 0.8f) * 2.5f);
            PuffRig.Cap.localScale = new Vector3(swell + squash * 0.5f, swell - squash, swell + squash * 0.5f);
            if (PuffRig.Tendrils != null)
                PuffRig.Tendrils.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.45f - 1.2f) * 4f, 0f, Mathf.Sin(t * 0.33f - 0.9f) * 4f);
            if (t >= PuffRig.NextBreath)
            {
                PuffRig.NextBreath = t + UnityEngine.Random.Range(9f, 16f);
                PuffRig.Breath = t;
                var vent = PuffRig.Cap.TransformPoint(PuffRig.VentAt);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.Hiss, vent, UnityEngine.Random.Range(0.3f, 0.38f), 0.9f, 160f, Punchy.Audio.SoundGroup.World);
                if (PuffRig.Vent != null)
                    for (int i = 0; i < 40; i++)
                    {
                        PuffRig.Burst.position = vent + UnityEngine.Random.insideUnitSphere * 1.5f;
                        PuffRig.Burst.velocity = new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), UnityEngine.Random.Range(3f, 6f), UnityEngine.Random.Range(-1.5f, 1.5f));
                        PuffRig.Burst.startSize = UnityEngine.Random.Range(0.4f, 1f);
                        PuffRig.Burst.startLifetime = UnityEngine.Random.Range(5f, 9f);
                        PuffRig.Burst.applyShapeToPosition = false;
                        PuffRig.Vent.Emit(PuffRig.Burst, 1);
                    }
            }
            if (t >= PuffRig.NextDrip && PuffRig.Tips != null && PuffRig.Tips.Length > 0 && PuffRig.Tendrils != null)
            {
                PuffRig.NextDrip = t + UnityEngine.Random.Range(4f, 8f);
                var tip = PuffRig.Tendrils.TransformPoint(PuffRig.Tips[UnityEngine.Random.Range(0, PuffRig.Tips.Length)]);
                Punchy.Audio.GameAudio.PlayOneShot(Punchy.Audio.SoundId.CaveDrip, tip, UnityEngine.Random.Range(0.7f, 0.9f), 0.6f, 40f, Punchy.Audio.SoundGroup.World);
            }
            // The glow: green flames at the pores and beads, and a green light round every third pore.
            if ((cam.transform.position - PuffRig.Cap.position).sqrMagnitude > LanternRange * LanternRange)
                return;
            if (cycle == null)
                cycle = FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            var night = cycle != null ? cycle.Night : 0f;
            var green = Punchy.Ambience.FireSystem.Green;
            for (int k = 0; k < PuffRig.Pores.Length; k++)
            {
                var p = PuffRig.Cap.TransformPoint(PuffRig.Pores[k]);
                Punchy.Ambience.FireSystem.AddFlame(p, 0.3f + 0.9f * night, 0x9FF00 + k, green);
                if (k % 3 == 0)
                {
                    var light = Punchy.Ambience.FireSystem.Light(green, (0.9f + 0.1f * Mathf.Sin(t * 2f + k)) * (0.3f + 1.3f * night));
                    light.w *= 0.6f + 0.8f * night;
                    Punchy.Terrain.CaveSystem.MovingLights.Add((0x9FF00 + k, p, light));
                }
            }
            if (PuffRig.Tendrils != null)
                for (int k = 0; k < PuffRig.Tips.Length; k++)
                    Punchy.Ambience.FireSystem.AddFlame(PuffRig.Tendrils.TransformPoint(PuffRig.Tips[k]), 0.2f + 0.55f * night, 0x9FF40 + k, green);
            // Glowing spores shed off its skin: motes that slip off the flanks and drift down, swaying, fading as they
            // reach the ground. The particle spores are lit like everything else, so at night these carry the glow.
            var centre = PuffRig.Cap.position;
            for (int i = 0; i < 14; i++)
            {
                var phase = Mathf.Repeat(t / 13f + i * 0.618f, 1f);
                var a = i * 2.4f + t * 0.05f + Mathf.Sin(t * 0.6f + i) * 0.2f;
                var r = 13f + 6f * phase;
                var p = centre + new Vector3(Mathf.Sin(a) * r, -3f - phase * (PuffRig.Lift - 3f), Mathf.Cos(a) * r);
                Punchy.Ambience.FireSystem.AddFlame(p, (0.12f + 0.4f * night) * Mathf.Sin(phase * Mathf.PI), 0x9FF80 + i, green);
            }
        }
    }
}
