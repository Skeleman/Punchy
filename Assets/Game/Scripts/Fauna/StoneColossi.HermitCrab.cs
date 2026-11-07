using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-87 row 4 (#40): the HermitCrab colossus's builder. Its ColossusDefinition asset (Resources/Colossi) says where it
    // stands (the Ocean biome's shore); the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsHermitCrab = Register(Kind.HermitCrab, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildHermitCrab(at, yaw, seed));

        static readonly Color CrabRed = new Color(0.72f, 0.30f, 0.20f);
        static readonly Color CrabDeep = new Color(0.48f, 0.17f, 0.12f);
        static readonly Color CrabPale = new Color(0.90f, 0.76f, 0.62f);
        static readonly Color ShellStone = new Color(0.66f, 0.62f, 0.55f);
        static readonly Color ShellBand = new Color(0.52f, 0.46f, 0.40f);
        static readonly Color ShellLip = new Color(0.86f, 0.70f, 0.66f);
        static readonly Color CrabBarnacle = new Color(0.86f, 0.85f, 0.80f);
        static readonly Color Kelp = new Color(0.30f, 0.34f, 0.16f);
        static readonly Color IsleSand = new Color(0.86f, 0.78f, 0.58f);
        static readonly Color PalmTrunk = new Color(0.50f, 0.38f, 0.24f);
        static readonly Color PalmLeaf = new Color(0.30f, 0.52f, 0.22f);
        static readonly Color Whitewash = new Color(0.92f, 0.90f, 0.86f);
        static readonly Color StarOrange = new Color(0.92f, 0.48f, 0.16f);

        /// <summary>
        /// A-87 row 4 (#40): the Ocean colossus, a hermit crab about 30 m tall that scuttles sideways along the shore and
        /// out into the shallows (its face looks along +x, so walking its loop on +z reads as a crab's sidestep). Its home
        /// is a huge barnacled whelk shell grown over into a little islet: a sand cap, a leaning palm with coconuts, and a
        /// small whitewashed lighthouse whose lamp glows faintly by day and brightly at night. Eight jointed legs, one big
        /// crusher claw and one small picking claw, eyes on stalks, long antennae. Small details: kelp hanging off the
        /// shell's rim, barnacles crusting the whorls, a starfish clinging to one leg, gulls on the shell and lighthouse,
        /// a lobster pot on a rope tangled round the small claw, and a mist of sea spray round its feet.
        /// </summary>
        static Transform BuildHermitCrab(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("HermitCrab").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.3f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new List<Vector3>();

            // Eight walking legs, four down each side (+/-z), each a thigh up to a high knee, then a shin down to a pale tip.
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 4; k++)
                {
                    var x = 6f - k * 3.2f + rng.NextFloat(-0.4f, 0.4f);
                    var splay = (k - 1.5f) * 0.32f;
                    var hip = new Vector3(x, 9f, s * 6.5f);
                    var knee = new Vector3(x + splay * 9f, 16f + rng.NextFloat(-1f, 1f), s * (15f + rng.NextFloat(-1f, 1f)));
                    var foot = new Vector3(x + splay * 16f, -0.4f, s * (21f + rng.NextFloat(-1.5f, 1.5f)));
                    m.Limb(hip, knee, 1.7f, 1.2f, CrabRed, 7);
                    m.Ellipsoid(knee, new Vector3(1.4f, 1.4f, 1.4f), Quaternion.identity, CrabDeep, 6, 4);
                    m.Limb(knee, foot + Vector3.up * 2.5f, 1.2f, 0.7f, CrabRed, 7);
                    m.Limb(foot + Vector3.up * 2.5f, foot, 0.7f, 0.15f, CrabPale, 5);
                }

            // The body, low and wide, with a paler underside.
            var body = new Vector3(2f, 9.5f, 0f);
            m.Ellipsoid(body, new Vector3(8f, 4.5f, 8.5f), Quaternion.identity, CrabRed);
            m.Ellipsoid(body + new Vector3(0.5f, -1.8f, 0f), new Vector3(7f, 2.6f, 7.4f), Quaternion.identity, CrabPale, 10, 4);

            // Eyes on stalks and long antennae sweeping forward and up.
            for (int s = -1; s <= 1; s += 2)
            {
                var stalk = body + new Vector3(7f, 2.5f, s * 2.2f);
                var eye = stalk + new Vector3(1.5f, 5.5f, s * 0.8f);
                m.Limb(stalk, eye, 0.55f, 0.4f, CrabRed, 6);
                m.Ellipsoid(eye, new Vector3(0.9f, 0.9f, 0.9f), Quaternion.identity, Dark, 7, 5);
                m.Ellipsoid(eye + new Vector3(0.6f, 0.4f, s * 0.2f), new Vector3(0.22f, 0.22f, 0.22f), Quaternion.identity, Whitewash, 4, 3);
                var a0 = body + new Vector3(7.6f, 1.6f, s * 1f);
                var a1 = a0 + new Vector3(7f, 5f, s * 3f);
                m.Limb(a0, a1, 0.22f, 0.12f, CrabDeep, 4);
                m.Limb(a1, a1 + new Vector3(4f, 6f, s * 4f), 0.12f, 0.04f, CrabDeep, 4);
            }

            // The big crusher claw (left, -z) and the small picking claw (right, +z), held up in front of the face.
            void Claw(int s, float size)
            {
                var shoulder = body + new Vector3(6f, -0.5f, s * 4.5f);
                var elbow = shoulder + new Vector3(4f * size, 3f * size, s * 4f * size);
                var wrist = elbow + new Vector3(5f * size, -1f * size, s * -1.5f * size);
                m.Limb(shoulder, elbow, 1.6f * size, 1.4f * size, CrabRed, 7);
                m.Limb(elbow, wrist, 1.4f * size, 1.6f * size, CrabRed, 7);
                var palm = wrist + new Vector3(2.4f * size, 0f, 0f);
                m.Ellipsoid(palm, new Vector3(3.2f, 2.4f, 2f) * size, Quaternion.Euler(0f, 0f, -10f), CrabRed, 10, 6);
                // Knobbly bumps on the top of the claw.
                for (int b = 0; b < 5; b++)
                    m.Ellipsoid(palm + new Vector3(rng.NextFloat(-2f, 2f), 1.8f, rng.NextFloat(-1f, 1f)) * size, Vector3.one * 0.45f * size, Quaternion.identity, CrabDeep, 4, 3);
                var tip = palm + new Vector3(2.6f * size, 0f, 0f);
                m.Limb(tip + Vector3.up * 0.6f * size, tip + new Vector3(4.2f, 0.4f, 0f) * size, 0.9f * size, 0.15f * size, CrabDeep, 6);
                m.Limb(tip - Vector3.up * 0.8f * size, tip + new Vector3(3.6f, -1.6f, 0f) * size, 0.8f * size, 0.15f * size, CrabDeep, 6);
            }
            Claw(-1, 1.25f);
            Claw(1, 0.7f);

            // A lobster pot on a frayed rope, tangled round the small claw's wrist and swinging under it.
            var pot = body + new Vector3(11.5f, -4.5f, 7.6f);
            m.Limb(body + new Vector3(12.4f, 1.8f, 7.2f), pot + Vector3.up * 1.2f, 0.12f, 0.12f, Rope, 3);
            m.Ellipsoid(pot, new Vector3(1.4f, 1.1f, 1.1f), Quaternion.identity, Wicker, 7, 4);
            m.Ellipsoid(pot + new Vector3(1.3f, 0f, 0f), new Vector3(0.2f, 0.6f, 0.6f), Quaternion.identity, Hollow, 5, 3);

            // The shell: a big whelk coiled up behind the body, its whorls stepping back and up to a spire, banded and
            // crusted with barnacles. Its pale pink lip rings the opening the body comes out of.
            var shell = new Vector3(-7f, 15f, 0f);
            m.Ellipsoid(shell, new Vector3(12f, 10f, 10.5f), Quaternion.Euler(0f, 0f, 18f), ShellStone, 14, 8);
            m.Ellipsoid(shell + new Vector3(0.4f, -0.5f, 0f), new Vector3(12.2f, 1.6f, 10.7f), Quaternion.Euler(0f, 0f, 18f), ShellBand, 14, 3);
            for (int w = 1; w <= 4; w++)
            {
                var a = w * 1.6f;
                var c = shell + new Vector3(-w * 2.6f, w * 3.6f, Mathf.Sin(a) * 2.2f);
                var r = 10f * Mathf.Pow(0.68f, w);
                m.Ellipsoid(c, new Vector3(r * 1.1f, r * 0.9f, r), Quaternion.Euler(0f, 0f, 22f), w % 2 == 0 ? ShellStone : Color.Lerp(ShellStone, ShellBand, 0.5f), 12, 6);
            }
            m.Ellipsoid(body + new Vector3(-3f, 2.5f, 0f), new Vector3(3f, 7.5f, 9.5f), Quaternion.Euler(0f, 0f, -12f), ShellLip, 12, 6);
            for (int b = 0; b < 46; b++)
            {
                var d = new Vector3(rng.NextFloat(-1f, 1f), rng.NextFloat(-0.3f, 1f), rng.NextFloat(-1f, 1f)).normalized;
                var p = shell + Vector3.Scale(d, new Vector3(12f, 10f, 10.5f)) * 0.97f;
                var r = rng.NextFloat(0.35f, 0.9f);
                m.Ellipsoid(p, new Vector3(r, r * 0.6f, r), Quaternion.FromToRotation(Vector3.up, d), CrabBarnacle, 5, 3);
            }

            // Kelp hanging off the rim of the shell, long and dark, a few strands trailing near the water.
            for (int k = 0; k < 22; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = shell + new Vector3(Mathf.Cos(a) * 11f, -6f + rng.NextFloat(-1f, 1.5f), Mathf.Sin(a) * 9.5f);
                var len = rng.NextFloat(3f, 9f);
                var mid = p + new Vector3(rng.NextFloat(-1f, 1f), -len * 0.5f, rng.NextFloat(-1f, 1f));
                m.Limb(p, mid, 0.4f, 0.3f, Color.Lerp(Kelp, Algae, rng.NextFloat(0f, 0.5f)), 4);
                m.Limb(mid, mid + new Vector3(rng.NextFloat(-0.6f, 0.6f), -len * 0.5f, rng.NextFloat(-0.6f, 0.6f)), 0.3f, 0.1f, Kelp, 4);
            }

            // The islet on top: a cap of sand over the shell's shoulder, a few rocks and tufts of grass.
            var isle = shell + new Vector3(1.5f, 7.5f, 0f);
            m.Ellipsoid(isle, new Vector3(9f, 2.2f, 8f), Quaternion.Euler(0f, 0f, 8f), IsleSand, 12, 4);
            for (int k = 0; k < 4; k++)
                m.Rock(isle + new Vector3(rng.NextFloat(-6f, 6f), 1.4f, rng.NextFloat(-5f, 5f)), new Vector3(1.2f, 0.8f, 1f) * rng.NextFloat(0.8f, 1.5f),
                       Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Stone, 6, 4, d => 1f);
            for (int k = 0; k < 12; k++)
            {
                var g = isle + new Vector3(rng.NextFloat(-7f, 7f), 1.6f, rng.NextFloat(-6f, 6f));
                m.Limb(g, g + new Vector3(rng.NextFloat(-0.4f, 0.4f), rng.NextFloat(1f, 1.8f), rng.NextFloat(-0.4f, 0.4f)), 0.25f, 0.02f, ScrubGreen, 3);
            }

            // A palm leaning out over the edge, coconuts under its crown.
            var palm0 = isle + new Vector3(-3f, 1.5f, 3.5f);
            var palmTop = palm0;
            var lean = new Vector3(-0.35f, 1f, 0.45f);
            for (int k = 0; k < 6; k++)
            {
                var next = palmTop + (lean + new Vector3(0f, 0f, k * 0.12f)).normalized * 2.2f;
                m.Limb(palmTop, next, 0.7f - k * 0.06f, 0.64f - k * 0.06f, k % 2 == 0 ? PalmTrunk : Color.Lerp(PalmTrunk, Dark, 0.2f), 6);
                palmTop = next;
            }
            for (int f = 0; f < 8; f++)
            {
                var a = f / 8f * Mathf.PI * 2f + rng.NextFloat(-0.2f, 0.2f);
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var mid = palmTop + d * 3f + Vector3.up * 0.8f;
                m.Limb(palmTop, mid, 0.35f, 0.3f, PalmLeaf, 4);
                m.Limb(mid, mid + d * 3f + Vector3.down * 2.2f, 0.3f, 0.05f, Color.Lerp(PalmLeaf, ScrubDry, f % 3 == 0 ? 0.4f : 0f), 4);
            }
            for (int c = 0; c < 3; c++)
                m.Ellipsoid(palmTop + new Vector3(Mathf.Cos(c * 2.1f) * 0.6f, -0.6f, Mathf.Sin(c * 2.1f) * 0.6f), Vector3.one * 0.45f, Quaternion.identity, PalmTrunk, 5, 4);

            // A small whitewashed lighthouse near the shell's crown, a red band and a lamp room that glows.
            var tower = isle + new Vector3(3.5f, 1.4f, -2.5f);
            m.Limb(tower, tower + Vector3.up * 6f, 1.3f, 0.95f, Whitewash, 8);
            m.Limb(tower + Vector3.up * 2.6f, tower + Vector3.up * 3.6f, 1.18f, 1.12f, CrabRed, 8);
            m.Limb(tower + Vector3.up * 6f, tower + Vector3.up * 6.3f, 1.35f, 1.35f, Dark, 8);
            // An open lamp room: four thin posts hold the cap, so the flame at its centre shows from every side.
            for (int s = 0; s < 4; s++)
            {
                var post = tower + new Vector3(Mathf.Cos(s * 1.571f + 0.785f) * 0.95f, 6.3f, Mathf.Sin(s * 1.571f + 0.785f) * 0.95f);
                m.Limb(post, post + Vector3.up * 1.5f, 0.09f, 0.09f, Dark, 4);
            }
            m.Ellipsoid(tower + Vector3.up * 6.55f, new Vector3(0.45f, 0.25f, 0.45f), Quaternion.identity, LanternPane, 6, 3);
            m.Dome(tower + Vector3.up * 7.8f, new Vector3(1.1f, 0.9f, 1.1f), Quaternion.identity, Dark);
            m.Ellipsoid(tower + new Vector3(1.2f, 1f, 0f), new Vector3(0.1f, 0.7f, 0.45f), Quaternion.identity, Hollow, 4, 3);
            glow.Add(tower + Vector3.up * 7.1f);

            // Gulls: one on the lighthouse rail, two on the shell, one on the big claw.
            void Gull(Vector3 p, float turn)
            {
                var q = Quaternion.Euler(0f, turn, 0f);
                m.Ellipsoid(p, new Vector3(0.35f, 0.35f, 0.7f), q, Whitewash, 6, 4);
                m.Ellipsoid(p + q * new Vector3(0f, 0.12f, -0.15f), new Vector3(0.38f, 0.15f, 0.55f), q, SlateDark, 5, 3);
                m.Ellipsoid(p + q * new Vector3(0f, 0.4f, 0.55f), Vector3.one * 0.22f, q, Whitewash, 5, 3);
                m.Limb(p + q * new Vector3(0f, 0.4f, 0.75f), p + q * new Vector3(0f, 0.35f, 1.05f), 0.06f, 0.02f, Pollen, 3);
            }
            Gull(tower + new Vector3(0.9f, 6.6f, 0.6f), 80f);
            Gull(shell + new Vector3(-6f, 9.6f, -4f), 200f);
            Gull(shell + new Vector3(-9.5f, 12.8f, 1.5f), 140f);
            Gull(body + new Vector3(15f, 4.6f, -10.6f), 60f);

            // A starfish clinging to the knee of one front leg.
            var star = new Vector3(6f + 0.32f * -1.5f * 9f, 13f, -13.5f);
            for (int a = 0; a < 5; a++)
            {
                var d = Quaternion.Euler(0f, 90f, a * 72f) * Vector3.up;
                m.Limb(star, star + d * 1.3f, 0.35f, 0.08f, StarOrange, 4);
            }

            AddPart("Body", root, m.ToMesh("HermitCrab"));

            // Sea spray drifting round its feet, and a fine drip off the kelp.
            Puffs(root, "Spray", new Vector3(0f, 0.8f, 0f), new Vector3(42f, 1.5f, 44f), new Color(0.86f, 0.90f, 0.90f, 0.35f), new Color(0.95f, 0.97f, 0.97f, 0.45f), 3f, 7f, 6f, 0.4f, 6f);
            Puffs(root, "Drips", shell + new Vector3(0f, -7f, 0f), new Vector3(22f, 2f, 20f), new Color(0.78f, 0.86f, 0.88f, 0.5f), new Color(0.90f, 0.95f, 0.96f, 0.6f), 0.15f, 0.35f, 8f, -2.5f, 2.5f);

            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Glow" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                point.localScale = Vector3.one * 2.5f; // the lighthouse lamp burns as a beacon, brighter than a lantern
                Lanterns.Add(point);
            }
            return root;
        }
    }
}
