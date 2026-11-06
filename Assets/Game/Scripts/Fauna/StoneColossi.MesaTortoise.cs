using System.Collections.Generic;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 1: the MesaTortoise colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsMesaTortoise = Register(Kind.MesaTortoise, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildMesaTortoise(at, yaw, seed));

        static readonly Color MesaRed = new Color(0.62f, 0.31f, 0.19f);
        static readonly Color MesaOrange = new Color(0.74f, 0.45f, 0.25f);
        static readonly Color MesaCream = new Color(0.83f, 0.71f, 0.53f);
        static readonly Color MesaBrown = new Color(0.46f, 0.29f, 0.20f);
        static readonly Color MesaDust = new Color(0.72f, 0.58f, 0.40f);
        static readonly Color ScrubGreen = new Color(0.34f, 0.39f, 0.19f);
        static readonly Color ScrubDry = new Color(0.56f, 0.50f, 0.30f);

        /// <summary>
        /// W-106 row 2 (#40): a rock tortoise in the canyonlands, about 40 m long and 24 m high, that reads as a small
        /// mesa from afar: its shell is a stack of flat strata (red, orange, cream and brown bands, each with its own
        /// ragged cliff line, narrowing a little going up) under an overhanging caprock with scrub, two small cacti and
        /// a walker's cairn on top, and a skirt of fallen talus round the foot. Close to, four stubby feet with blunt toes
        /// show under the lowest band and a head with heavy-lidded eyes rests on the ground at the front, half hidden by
        /// the overhang. Details: a strata slab fallen and leaning on a foot, a dead juniper on the rim, a pale lizard
        /// sunning on the head. Seeded, so each world's tortoise has its own cliff lines and scrub. Body (shell, legs,
        /// head) and Scrub are separate parts, laid out as the giant tortoise is (shell centred, neck forward on +z), so
        /// the animal lane can rig it later (A-72 hooks: Spots, Root).
        /// </summary>
        static Transform BuildMesaTortoise(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("MesaTortoise").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);

            // A flat slab with a ragged cliff round it: a sphere pushed out to a cylinder (flat top and bottom), its
            // sides moved in and out by three seeded waves round the slab.
            void Slab(Vector3 centre, Vector3 radii, Color color, float ragged)
            {
                float p1 = rng.NextFloat(0f, 6.3f), p2 = rng.NextFloat(0f, 6.3f), p3 = rng.NextFloat(0f, 6.3f);
                m.Rock(centre, radii, Quaternion.identity, color, 28, 8, u =>
                {
                    var hor = Mathf.Sqrt(u.x * u.x + u.z * u.z);
                    var box = 1f / Mathf.Max(hor, Mathf.Abs(u.y), 0.001f);
                    if (hor + 0.001f < Mathf.Abs(u.y))
                        return box;
                    var a = Mathf.Atan2(u.z, u.x);
                    return box * (1f + ragged * (0.5f * Mathf.Sin(3f * a + p1) + 0.3f * Mathf.Sin(7f * a + p2) + 0.2f * Mathf.Sin(13f * a + p3)));
                });
            }

            // Four stubby legs with blunt toes under the lowest band; the front right one set forward (mid-step).
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var forward = k == 1 ? 2.5f : 0f;
                var hip = new Vector3(sx * 11f, 6.5f, sz * 11f);
                var foot = new Vector3(sx * 14.5f, 1.4f, sz * 14f + forward);
                m.Limb(hip, foot, 4.6f, 4f, MesaBrown);
                m.Ellipsoid(foot + new Vector3(0f, -0.5f, sz * 1.4f), new Vector3(4.4f, 1.8f, 4.8f), Quaternion.identity, MesaRed, 8, 4);
                for (int t = -1; t <= 1; t++)
                    m.Ellipsoid(foot + new Vector3(t * 2.1f, -0.6f, sz * 5f), new Vector3(1f, 0.8f, 0.9f), Quaternion.identity, MesaCream, 5, 3);
            }
            // The underside (the plastron), then the strata, each a little narrower going up, then the caprock.
            Slab(new Vector3(0f, 5.4f, 0f), new Vector3(16.5f, 1.4f, 19f), MesaBrown, 0.03f);
            Color[] bands = { MesaRed, MesaOrange, MesaCream, MesaRed, MesaBrown, MesaOrange };
            var y = 6.6f;
            var layers = rng.NextInt(5, 7);
            for (int k = 0; k < layers; k++)
            {
                var h = rng.NextFloat(2.4f, 3.8f);
                var shrink = 1f - k * 0.035f;
                var color = Color.Lerp(bands[(k + (int)(seed % 3u)) % bands.Length], MesaDust, rng.NextFloat(0f, 0.2f));
                Slab(new Vector3(rng.NextFloat(-0.4f, 0.4f), y + h * 0.5f, rng.NextFloat(-0.4f, 0.4f)), new Vector3(17.5f * shrink, h * 0.5f, 20f * shrink), color, rng.NextFloat(0.04f, 0.08f));
                y += h * 0.98f;
            }
            var capTop = y + 1.6f;
            Slab(new Vector3(0f, y + 0.8f, -0.5f), new Vector3(17.5f * (1f - layers * 0.035f) + 1.4f, 0.8f, 20f * (1f - layers * 0.035f) + 1.6f), MesaBrown, 0.05f);

            // The head resting on the ground at the front, under the overhang: a heavy brow, lidded eyes, a hooked beak.
            m.Limb(new Vector3(0f, 6f, 17f), new Vector3(1f, 4f, 24f), 4.4f, 3.6f, MesaBrown);
            var look = Quaternion.Euler(14f, rng.NextFloat(-18f, 18f), 0f);
            var head = new Vector3(1.5f, 3.6f, 27.5f);
            m.Ellipsoid(head, new Vector3(4.2f, 3.4f, 5.4f), look, MesaRed);
            m.Ellipsoid(head + look * new Vector3(0f, 1.8f, 1.6f), new Vector3(4f, 1.1f, 3.4f), look, MesaBrown);                 // brow
            m.Ellipsoid(head + look * new Vector3(-2.9f, 0.9f, 2.6f), new Vector3(0.9f, 0.5f, 0.8f), look, Hollow);              // lidded eyes
            m.Ellipsoid(head + look * new Vector3(2.9f, 0.9f, 2.6f), new Vector3(0.9f, 0.5f, 0.8f), look, Hollow);
            m.Ellipsoid(head + look * new Vector3(0f, -0.9f, 4.8f), new Vector3(2.2f, 1.8f, 1.6f), look, MesaBrown);              // beak
            m.Limb(new Vector3(0f, 5f, -20f), new Vector3(-1.5f, 1.4f, -25f), 2.6f, 0.6f, MesaBrown);                          // stubby tail
            // Talus round the foot: broken blocks in the band colours, sunk a little.
            for (int k = 0; k < 34; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(1.05f, 1.35f);
                var size = rng.NextFloat(0.6f, 2.2f);
                var p = new Vector3(Mathf.Cos(a) * 18f * r, size * 0.25f, Mathf.Sin(a) * 21f * r);
                if (p.z > 20f && Mathf.Abs(p.x) < 8f)
                    continue;                                                                                   // keep the head clear
                m.Ellipsoid(p, new Vector3(size * 1.3f, size * 0.7f, size), Quaternion.Euler(rng.NextFloat(-20f, 20f), rng.NextFloat(0f, 360f), rng.NextFloat(-15f, 15f)), bands[rng.NextInt(bands.Length)], 6, 4);
            }
            // A strata slab broken off and leaning on the back left foot.
            m.Ellipsoid(new Vector3(-19f, 3.2f, -13f), new Vector3(1f, 3.6f, 4.8f), Quaternion.Euler(0f, 30f, -28f), MesaCream, 6, 4);
            var mesh = m.ToMesh("MesaTortoise");
            Weather(mesh, MesaDust, 0.6f, 0.95f);
            AddPart("Body", root, mesh);

            // On top, in its own part so the dust pass leaves its colours: scrub, two small cacti, a cairn, a dead juniper.
            var f = new DunesColossus.MeshBuilder();
            var topR = new Vector2(15f, 17f) * (1f - layers * 0.035f);
            Vector3 OnTop(float edge)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = Mathf.Sqrt(rng.NextFloat()) * edge;
                return new Vector3(Mathf.Cos(a) * topR.x * r, capTop - 0.1f, Mathf.Sin(a) * topR.y * r - 0.5f);
            }
            var bushes = rng.NextInt(14, 22);
            for (int k = 0; k < bushes; k++)
            {
                var p = OnTop(0.95f);
                var s = rng.NextFloat(0.5f, 1.4f);
                var c = Color.Lerp(ScrubGreen, ScrubDry, rng.NextFloat());
                f.Ellipsoid(p + Vector3.up * s * 0.4f, new Vector3(s, s * 0.7f, s * rng.NextFloat(0.8f, 1.2f)), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), c, 7, 4);
                if (rng.NextFloat() < 0.4f)
                    f.Ellipsoid(p + new Vector3(s * 0.7f, s * 0.3f, s * 0.4f), new Vector3(s * 0.6f, s * 0.45f, s * 0.6f), Quaternion.identity, c * 0.9f, 6, 3);
            }
            for (int k = 0; k < 2; k++)
            {
                var p = OnTop(0.8f);
                var h = rng.NextFloat(1.6f, 2.6f);
                f.Limb(p, p + Vector3.up * h, 0.32f, 0.28f, new Color(0.30f, 0.42f, 0.24f), 8);
                f.Limb(p + Vector3.up * h * 0.45f, p + new Vector3(0.7f, h * 0.55f, 0f), 0.2f, 0.2f, new Color(0.30f, 0.42f, 0.24f), 6);
                f.Limb(p + new Vector3(0.7f, h * 0.55f, 0f), p + new Vector3(0.75f, h * 0.85f, 0f), 0.2f, 0.17f, new Color(0.30f, 0.42f, 0.24f), 6);
            }
            var cairn = OnTop(0.5f);
            for (int k = 0; k < 4; k++)
            {
                var s = 0.75f - k * 0.13f;
                f.Ellipsoid(cairn + new Vector3(rng.NextFloat(-0.08f, 0.08f), 0.3f + k * 0.62f * (0.9f - k * 0.08f), 0f), new Vector3(s, s * 0.5f, s * 0.85f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), rng.NextFloat(-8f, 8f)), k % 2 == 0 ? MesaCream : Dark, 7, 4);
            }
            var rimA = rng.NextFloat(0f, Mathf.PI * 2f);
            var trunk = new Vector3(Mathf.Cos(rimA) * topR.x * 0.92f, capTop - 0.2f, Mathf.Sin(rimA) * topR.y * 0.92f - 0.5f);
            var bark = new Color(0.42f, 0.36f, 0.30f);
            var crook = trunk + new Vector3(Mathf.Cos(rimA) * 1.2f, 2.4f, Mathf.Sin(rimA) * 1.2f);
            f.Limb(trunk, crook, 0.35f, 0.24f, bark, 6);
            f.Limb(crook, crook + new Vector3(Mathf.Cos(rimA) * 1.8f, 1.2f, Mathf.Sin(rimA) * 1.8f), 0.22f, 0.08f, bark, 5);
            f.Limb(crook, crook + new Vector3(-Mathf.Sin(rimA) * 1.2f, 1.6f, Mathf.Cos(rimA) * 1.2f), 0.18f, 0.06f, bark, 5);
            f.Limb(trunk + Vector3.up * 1.2f, trunk + new Vector3(Mathf.Sin(rimA) * 1.3f, 2f, -Mathf.Cos(rimA) * 1.3f), 0.15f, 0.05f, bark, 5);
            // A pale lizard sunning on the head, tail curled.
            var sun = head + look * new Vector3(0.6f, 3.3f, 0.5f);
            var lizard = new Color(0.86f, 0.80f, 0.66f);
            f.Ellipsoid(sun, new Vector3(0.18f, 0.1f, 0.42f), look, lizard, 6, 3);
            f.Ellipsoid(sun + look * new Vector3(0f, 0.02f, 0.5f), new Vector3(0.12f, 0.08f, 0.16f), look, lizard, 5, 3);
            f.Limb(sun + look * new Vector3(0f, 0f, -0.4f), sun + look * new Vector3(0.35f, 0f, -0.85f), 0.07f, 0.02f, lizard, 4);
            AddPart("Scrub", root, f.ToMesh("MesaTortoiseScrub"));
            return root;
        }
    }
}
