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
    // A-81 row 1: the CinderSnail colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsCinderSnail = Register(Kind.CinderSnail, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildCinderSnail(at, yaw, seed));

        static readonly Color SnailFlesh = new Color(0.36f, 0.31f, 0.29f);
        static readonly Color SnailSole = new Color(0.22f, 0.18f, 0.17f);
        static readonly Color Cinder = new Color(0.16f, 0.14f, 0.13f);

        /// <summary>
        /// A-78 (#40): the EmberForest colossus, a cinder snail about 34 m tall. Its shell is a coil of cooled lava with
        /// ember seams glowing between the whorls and a smoking vent on top; it glides on a long ash-grey foot (no legs, so
        /// <see cref="ColossusWalker"/> moves it without a rig), with ember eyes on tall stalks. Riders and wear: a charred
        /// tree rooted on the shell, a baby snail clinging to the whorl, an old rope ladder, a crow on the vent and fire
        /// mushrooms under the shell's lip. Its glow points reuse the stag's lantern flames.
        /// </summary>
        static Transform BuildCinderSnail(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("CinderSnail").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new List<Vector3>();

            // The foot: a long low sole, a paler skirt rippling along each side, and the tail tapering out behind.
            m.Ellipsoid(new Vector3(0f, 2.6f, 1f), new Vector3(7.5f, 3f, 21f), Quaternion.identity, SnailFlesh);
            m.Ellipsoid(new Vector3(0f, 0.9f, 1f), new Vector3(8.4f, 1.1f, 21.5f), Quaternion.identity, SnailSole, 12, 4);
            for (int k = 0; k < 14; k++)
            {
                var z = -17f + k * 2.7f;
                for (int s = -1; s <= 1; s += 2)
                    m.Ellipsoid(new Vector3(s * (7.6f - Mathf.Abs(z) * 0.08f), 1.6f, z), new Vector3(1.2f, 1f, 1.8f), Quaternion.identity, Color.Lerp(SnailFlesh, Ash, 0.4f), 6, 3);
                // Ember glints in the skirt, as if it had just slid over hot coals.
                if (k % 3 == 1)
                    m.Ellipsoid(new Vector3((k % 2 == 0 ? -1f : 1f) * (7.9f - Mathf.Abs(z) * 0.08f), 0.9f, z), new Vector3(0.5f, 0.3f, 0.7f), Quaternion.identity, Ember, 5, 2);
            }
            m.Limb(new Vector3(0f, 2.4f, -19f), new Vector3(0f, 1.2f, -27f), 3f, 0.6f, SnailFlesh, 9);

            // The head and neck rise at the front: two tall eye stalks with ember eyes, two short feelers below.
            var neck = new Vector3(0f, 4f, 18f);
            var head = new Vector3(0f, 9.5f, 23.5f);
            m.Limb(neck, head, 5.2f, 3.4f, SnailFlesh, 10);
            m.Ellipsoid(head + new Vector3(0f, 0.4f, 1.2f), new Vector3(3.6f, 3f, 3.4f), Quaternion.identity, SnailFlesh);
            m.Ellipsoid(head + new Vector3(0f, -1.6f, 4f), new Vector3(2f, 0.6f, 0.6f), Quaternion.identity, Hollow, 6, 3);   // mouth
            for (int s = -1; s <= 1; s += 2)
            {
                var stalk0 = head + new Vector3(s * 1.6f, 2.4f, 1f);
                var eye = head + new Vector3(s * (3.4f + rng.NextFloat(0f, 0.8f)), 9.5f + rng.NextFloat(-0.6f, 0.6f), 4f);
                Tentacle(m, stalk0, stalk0 + new Vector3(s * 0.4f, 3f, 0.4f), eye + new Vector3(0f, -3f, -0.6f), eye, 0.9f, 0.55f, SnailFlesh, SnailFlesh, 10);
                m.Ellipsoid(eye, new Vector3(1.1f, 1.1f, 1.1f), Quaternion.identity, Cinder, 8, 5);
                m.Ellipsoid(eye + new Vector3(0f, 0f, 0.8f), new Vector3(0.6f, 0.6f, 0.4f), Quaternion.identity, Ember, 6, 3);
                glow.Add(eye + new Vector3(0f, 0f, 1.1f));
                var feeler = head + new Vector3(s * 2.2f, -1f, 3f);
                m.Limb(feeler, feeler + new Vector3(s * 2.4f, -1.4f, 3.2f), 0.6f, 0.2f, SnailFlesh, 6);
            }

            // The shell: a coil of cooled lava wound round a sideways axis over the back, each whorl smaller than the
            // last, with an ember seam between whorls and pitted lumps of crust.
            // A raised mantle on the back so the shell sits on the body rather than floating over it.
            m.Ellipsoid(new Vector3(0f, 5.5f, -3f), new Vector3(6.5f, 3.5f, 13f), Quaternion.identity, Color.Lerp(SnailFlesh, Ash, 0.25f));
            var axis = new Vector3(0f, 13.5f, -3f);
            const float turns = 2.6f;
            const int steps = 70;
            Vector3 Coil(float t, float lift)
            {
                var theta = t * turns * Mathf.PI * 2f;
                var r = 13.5f * Mathf.Exp(-0.28f * theta) + lift;
                return axis + new Vector3(Mathf.Lerp(0f, 2.5f, t), Mathf.Sin(theta) * r, -Mathf.Cos(theta) * r);
            }
            for (int k = 0; k < steps; k++)
            {
                var t = k / (float)steps;
                var theta = t * turns * Mathf.PI * 2f;
                var tube = 7.5f * Mathf.Exp(-0.28f * theta) + 0.6f;
                var p = Coil(t, 0f);
                var turn = Quaternion.Euler(-theta * Mathf.Rad2Deg, 0f, 0f);
                m.Ellipsoid(p, new Vector3(tube * 1.15f, tube, tube), turn, Color.Lerp(Cinder, Soot, rng.NextFloat()), 10, 6);
                if (k % 2 == 0)
                {
                    var seam = Coil(t, tube * 0.92f);
                    for (int s = -1; s <= 1; s += 2)
                        m.Ellipsoid(seam + new Vector3(s * tube * 0.55f, 0f, 0f), new Vector3(0.45f, 0.5f, tube * 0.32f), turn, Ember, 5, 2);
                }
                if (k % 9 == 4)
                    glow.Add(Coil(t, tube * 0.95f) + new Vector3(tube * 0.6f, 0f, 0f));
                if (rng.NextFloat() < 0.5f)
                {
                    var lump = p + Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f) * Vector3.up * tube * 0.95f;
                    m.Ellipsoid(lump, Vector3.one * rng.NextFloat(0.6f, 1.4f), Quaternion.identity, Color.Lerp(Ash, Cinder, 0.5f), 5, 3);
                }
            }
            // The shell's lip flares over the foot, with fire mushrooms tucked under it.
            var lip = Coil(0f, 0f);
            m.Ellipsoid(lip + new Vector3(0f, -3f, 1f), new Vector3(9f, 3f, 4f), Quaternion.Euler(-20f, 0f, 0f), Cinder, 10, 5);
            for (int k = 0; k < 7; k++)
            {
                var c = lip + new Vector3(rng.NextFloat(-7f, 7f), -4.5f + rng.NextFloat(-0.5f, 0.5f), rng.NextFloat(2f, 4.5f));
                var cap = rng.NextFloat(0.5f, 1.1f);
                m.Limb(c, c + Vector3.up * cap * 1.2f, cap * 0.25f, cap * 0.2f, new Color(0.86f, 0.80f, 0.70f), 5);
                m.Ellipsoid(c + Vector3.up * cap * 1.3f, new Vector3(cap, cap * 0.45f, cap), Quaternion.identity, k % 3 == 0 ? new Color(0.95f, 0.70f, 0.18f) : new Color(0.85f, 0.24f, 0.10f), 6, 3);
            }

            // A smoking vent on the crown of the shell, a crow on its rim.
            var crown = axis + new Vector3(0f, 14f, 1f);
            var vent = crown + new Vector3(0.8f, 4.5f, -0.6f);
            m.Limb(crown, vent, 2.2f, 1.4f, Soot, 9);
            m.Ellipsoid(vent + Vector3.up * 0.1f, new Vector3(1.6f, 0.5f, 1.6f), Quaternion.identity, Hollow, 8, 3);
            m.Ellipsoid(vent, new Vector3(1.1f, 0.3f, 1.1f), Quaternion.identity, Ember, 7, 2);
            glow.Add(vent + Vector3.up * 0.4f);
            var crow = vent + new Vector3(1.4f, 0.5f, 0f);
            m.Ellipsoid(crow, new Vector3(0.35f, 0.4f, 0.6f), Quaternion.Euler(-20f, 0f, 0f), Hollow, 6, 4);
            m.Ellipsoid(crow + new Vector3(0f, 0.45f, 0.45f), new Vector3(0.24f, 0.24f, 0.26f), Quaternion.identity, Hollow, 6, 3);
            m.Limb(crow + new Vector3(0f, 0.45f, 0.65f), crow + new Vector3(0f, 0.38f, 1.05f), 0.08f, 0.02f, Dark, 4);   // beak

            // A charred tree has rooted on the shoulder of the shell: a black trunk, bare forks, two last ember leaves.
            var treeAt = Coil(0.18f, 0f) + new Vector3(4.5f, 4.5f, 0f);
            var top = treeAt + new Vector3(1.5f, 7f, -0.5f);
            m.Limb(treeAt, top, 0.9f, 0.4f, Hollow, 7);
            for (int k = 0; k < 4; k++)
            {
                var from = Vector3.Lerp(treeAt, top, 0.45f + k * 0.15f);
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var to = from + new Vector3(Mathf.Cos(a) * 2.6f, rng.NextFloat(1.2f, 2.4f), Mathf.Sin(a) * 2.6f);
                m.Limb(from, to, 0.3f, 0.08f, Hollow, 5);
                if (k < 2)
                    m.Ellipsoid(to, new Vector3(0.3f, 0.12f, 0.22f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 30f), Ember, 4, 2);
            }
            for (int k = 0; k < 4; k++)
            {
                var a = k * Mathf.PI * 0.5f + 0.4f;
                m.Limb(treeAt, treeAt + new Vector3(Mathf.Cos(a) * 2.8f, -1.6f, Mathf.Sin(a) * 2.8f), 0.4f, 0.1f, Hollow, 5);   // roots gripping the crust
            }

            // A baby snail clinging to the far side of the whorl, its own shell a tiny ember-seamed coil.
            var baby = Coil(0.42f, 0f) + new Vector3(-6.5f, 0f, 0f);
            m.Ellipsoid(baby, new Vector3(0.9f, 0.6f, 2.4f), Quaternion.Euler(70f, 0f, 0f), SnailFlesh, 7, 4);
            m.Ellipsoid(baby + new Vector3(-1f, 0f, 0f), new Vector3(1.1f, 1.4f, 1.4f), Quaternion.identity, Cinder, 7, 5);
            m.Ellipsoid(baby + new Vector3(-1.9f, 0.1f, 0f), new Vector3(0.2f, 0.8f, 0.8f), Quaternion.identity, Ember, 6, 3);

            // An old rope ladder hangs down the flank from a peg in the crust: two ropes and slats, one slat missing.
            var peg = Coil(0.05f, 0f) + new Vector3(7.5f, 2f, 0f);
            for (int s = -1; s <= 1; s += 2)
                m.Limb(peg + new Vector3(0f, 0f, s * 0.7f), peg + new Vector3(0.6f, -12f, s * 0.7f), 0.08f, 0.08f, new Color(0.52f, 0.42f, 0.28f), 3);
            for (int k = 0; k < 8; k++)
            {
                if (k == 5)
                    continue;
                var y = -1.4f - k * 1.4f;
                m.Ellipsoid(peg + new Vector3(0.6f * -y / 12f, y, 0f), new Vector3(0.12f, 0.1f, 0.85f), Quaternion.identity, new Color(0.40f, 0.30f, 0.20f), 4, 2);
            }
            AddPart("Body", root, m.ToMesh("CinderSnail"));

            // Smoke from the vent, and embers drifting off the shell seams.
            Puffs(root, "Smoke", vent + Vector3.up * 1f, new Vector3(1.2f, 0.5f, 1.2f), new Color(0.20f, 0.18f, 0.17f, 0.40f), new Color(0.36f, 0.33f, 0.31f, 0.35f), 2.5f, 6f, 3f, 2.2f, 9f);
            Puffs(root, "Embers", axis + new Vector3(0f, 4f, 0f), new Vector3(10f, 14f, 16f), new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.35f, 0.08f, 0.8f), 0.15f, 0.35f, 4f, 1.2f, 4f);

            // The glow points, as children so they follow the root as it glides.
            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Ember" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                Lanterns.Add(point);
            }
            return root;
        }
    }
}
