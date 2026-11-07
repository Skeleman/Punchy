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
    // A-81 row 1: the SkeletalLizard colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsSkeletalLizard = Register(Kind.SkeletalLizard, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildLizard(at, yaw));

        /// <summary>
        /// W-88 (#40, Zach's Ashlands design): the skeleton of a lizard about 80 m nose to tail, still standing on four
        /// splayed legs in the ash, ribcage open, the skull low with its jaw hanging and a frill of horns. It gives off
        /// ash from the ribs and kicks up a ring of dust at its feet. Details: a smouldering heart still glowing inside the
        /// cage, embers in the eye sockets, a snapped rib and a few teeth lying in the ash.
        /// </summary>
        static Transform BuildLizard(float3 at, float yaw)
        {
            var root = new GameObject("SkeletalLizard").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            // The spine from the tail tip (s = 0) to the neck (s = 1): the tail trails on the ground, the back arches.
            Vector3 Spine(float s) => new Vector3(5f * Mathf.Sin(s * Mathf.PI * 1.6f + 0.6f),
                                                  s < 0.35f ? Mathf.Lerp(1.6f, 10f, s / 0.35f) : 10f + 3f * Mathf.Sin((s - 0.35f) / 0.65f * Mathf.PI),
                                                  -48f + 76f * s);
            float Radius(float s) => s < 0.35f ? Mathf.Lerp(0.5f, 2f, s / 0.35f) : 2f;
            const int N = 44;
            for (int i = 0; i <= N; i++)
            {
                var s = i / (float)N;
                var v = Spine(s);
                var r = Radius(s);
                var along = (Spine(math.min(1f, s + 0.02f)) - Spine(math.max(0f, s - 0.02f))).normalized;
                var rot = Quaternion.LookRotation(along);
                m.Ellipsoid(v, new Vector3(r * 1.3f, r, r * 0.75f), rot, i % 2 == 0 ? Bone : BoneDark, 8, 5);
                m.Limb(v, v + Vector3.up * r * 2.4f - along * r * 0.6f, r * 0.35f, r * 0.08f, Bone, 5);       // the spine's ridge
                // Ribs hanging from the back between the hips and the shoulders, deepest mid-chest.
                if (s > 0.42f && s < 0.85f && i % 2 == 0)
                {
                    var depth = Mathf.Sin((s - 0.42f) / 0.43f * Mathf.PI);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var p0 = v + new Vector3(side * r, -0.3f, 0f);
                        var p3 = v + new Vector3(side * (4f + 3f * depth), -(4f + 6.5f * depth), 1f);
                        if (s > 0.6f && s < 0.66f && side > 0)
                            continue;                                                                          // the snapped rib's gap
                        Tentacle(m, p0, p0 + new Vector3(side * 5f, 1.5f, 0f), p3 + new Vector3(side * 4f, 3f, 0f), p3, 0.6f, 0.3f, Bone, Bone, 6);
                    }
                }
            }
            // Hips and shoulders: a flat pelvis and two shoulder blades; four splayed legs with knuckled joints and claws.
            for (int g = 0; g < 2; g++)
            {
                var s = g == 0 ? 0.38f : 0.86f;
                var v = Spine(s);
                m.Ellipsoid(v + Vector3.down * 0.8f, new Vector3(5f, 1.6f, 3.4f), Quaternion.identity, BoneDark, 10, 4);
                for (int side = -1; side <= 1; side += 2)
                {
                    var hip = v + new Vector3(side * 3.5f, -1.5f, 0f);
                    var elbow = v + new Vector3(side * 12f, 1f, g == 0 ? -3f : 3f);
                    var wrist = new Vector3(v.x + side * 14f, 1.8f, v.z + (g == 0 ? 1f : 6f));
                    m.Limb(hip, elbow, 1.5f, 1.2f, Bone, 7);
                    m.Ellipsoid(elbow, new Vector3(1.8f, 1.8f, 1.8f), Quaternion.identity, BoneDark, 8, 5);
                    m.Limb(elbow, wrist, 1.1f, 0.9f, Bone, 7);
                    m.Ellipsoid(wrist, new Vector3(1.4f, 1.2f, 1.4f), Quaternion.identity, BoneDark, 8, 5);
                    for (int t = 0; t < 4; t++)
                    {
                        var ta = (side * 30f + (t - 1.5f) * 24f) * Mathf.Deg2Rad;
                        var knuckle = wrist + new Vector3(Mathf.Sin(ta) * 2.6f, -1f, Mathf.Cos(ta) * 2.6f);
                        m.Limb(wrist, knuckle, 0.45f, 0.35f, Bone, 5);
                        m.Limb(knuckle, knuckle + new Vector3(Mathf.Sin(ta) * 1.6f, -0.9f, Mathf.Cos(ta) * 1.6f), 0.35f, 0.05f, BoneDark, 4);
                    }
                }
            }
            // A-85 row 1: the build is split in two frames (it swung 21-76 ms as one). This frame: the spine, ribs,
            // hips and legs. The next (BuildRest): the neck, skull, heart, what lies in the ash, and the puffs.
            var body = m.ToMesh("SkeletalLizard");
            Weather(body, Ash, 0.82f, 1f);
            AddPart("Bones", root, body);
            BuildRest = () => { if (root != null) BuildLizardHead(root, Spine); };
            return root;
        }

        /// <summary>A-85 row 1: the lizard's second half, built the frame after its body.</summary>
        static void BuildLizardHead(Transform root, System.Func<float, Vector3> Spine)
        {
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xA54Eu);
            // Neck bones down to the skull, held low; the jaw hangs open, a frill of horns at the back.
            var neckBase = Spine(1f);
            var skull = new Vector3(neckBase.x + 1f, 8f, neckBase.z + 13f);
            for (int k = 0; k < 6; k++)
            {
                var p = Vector3.Lerp(neckBase, skull - new Vector3(0f, 0f, 4f), k / 5f) + Vector3.up * Mathf.Sin(k / 5f * Mathf.PI) * 2f;
                m.Ellipsoid(p, new Vector3(1.9f, 1.6f, 1.3f), Quaternion.identity, k % 2 == 0 ? Bone : BoneDark, 8, 5);
                m.Limb(p, p + new Vector3(0f, 2.4f, -0.6f), 0.6f, 0.1f, Bone, 5);
            }
            var pitch = Quaternion.Euler(12f, 6f, 0f);
            m.Ellipsoid(skull, new Vector3(4.4f, 3f, 7.5f), pitch, Bone, 12, 7);
            m.Ellipsoid(skull + pitch * new Vector3(0f, 1.6f, -2.5f), new Vector3(3.6f, 2.2f, 3.4f), pitch, BoneDark, 10, 5);   // the crown
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = skull + pitch * new Vector3(side * 2.9f, 1.4f, 1.5f);
                m.Ellipsoid(eye, new Vector3(1.3f, 1.1f, 1.5f), pitch, Hollow, 8, 5);
                m.Ellipsoid(eye + pitch * new Vector3(side * 0.6f, 0f, 0f), new Vector3(0.35f, 0.3f, 0.35f), pitch, Ember, 5, 3);
                m.Ellipsoid(skull + pitch * new Vector3(side * 1.1f, 1.6f, 6.6f), new Vector3(0.45f, 0.35f, 0.6f), pitch, Hollow, 5, 3); // nostrils
            }
            var jaw = Quaternion.Euler(34f, 6f, 0f);
            var jawHinge = skull + pitch * new Vector3(0f, -1.6f, -4.5f);
            m.Ellipsoid(jawHinge + jaw * new Vector3(0f, 0f, 6f), new Vector3(3.6f, 1.1f, 6.6f), jaw, BoneDark, 10, 5);
            for (int k = 0; k < 8; k++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var z = 1f + k * 0.8f;
                    var top = skull + pitch * new Vector3(side * (3.4f - k * 0.25f), -2.4f, z);
                    m.Limb(top, top + pitch * new Vector3(0f, -1.4f + k * 0.08f, 0.2f), 0.28f, 0.04f, Fang, 4);
                    var bottom = jawHinge + jaw * new Vector3(side * (3f - k * 0.25f), 0.9f, 5f + k * 0.8f);
                    m.Limb(bottom, bottom + jaw * new Vector3(0f, 1.2f, 0.2f), 0.25f, 0.04f, Fang, 4);
                }
            for (int k = 0; k < 5; k++)                                                                                    // the frill of horns
            {
                var a = Mathf.Lerp(-60f, 60f, k / 4f) * Mathf.Deg2Rad;
                var foot = skull + pitch * new Vector3(Mathf.Sin(a) * 3.4f, 2.2f, -5f);
                m.Limb(foot, foot + pitch * new Vector3(Mathf.Sin(a) * 3f, 2.5f + (k % 2) * 1.5f, -4f), 0.7f, 0.06f, BoneDark, 5);
            }
            // The smouldering heart inside the cage, and what has fallen into the ash: the snapped rib and a few teeth.
            var heart = Spine(0.6f) + new Vector3(0f, -6.5f, 0f);
            m.Ellipsoid(heart, new Vector3(2.2f, 1.8f, 2.4f), Quaternion.Euler(10f, 30f, 0f), Ember, 8, 5);
            m.Ellipsoid(heart + Vector3.down * 1.4f, new Vector3(3.4f, 0.8f, 3.4f), Quaternion.identity, Soot, 8, 3);
            var fallen = Spine(0.63f) + new Vector3(9f, 0f, 2f);
            m.Limb(new Vector3(fallen.x, 0.6f, fallen.z), new Vector3(fallen.x + 6f, 0.9f, fallen.z + 5f), 0.55f, 0.3f, Bone, 6);
            m.Limb(new Vector3(fallen.x + 6f, 0.9f, fallen.z + 5f), new Vector3(fallen.x + 8f, 0.5f, fallen.z + 9f), 0.3f, 0.2f, Bone, 6);
            for (int k = 0; k < 6; k++)
            {
                var p = skull + new Vector3(rng.NextFloat(-7f, 7f), 0f, rng.NextFloat(-2f, 9f));
                p.y = 0.7f;
                m.Limb(p, p + new Vector3(rng.NextFloat(-0.6f, 0.6f), 0.3f, rng.NextFloat(-1.2f, 1.2f)), 0.25f, 0.04f, Fang, 4);
            }
            var mesh = m.ToMesh("SkeletalLizard head");
            Weather(mesh, Ash, 0.82f, 1f);
            AddPart("Skull", root, mesh);
            // Ash shed from the ribcage, and dust round the feet.
            Puffs(root, "Ash", Spine(0.62f), new Vector3(14f, 8f, 30f), new Color(0.34f, 0.32f, 0.31f, 0.35f), new Color(0.48f, 0.46f, 0.44f, 0.40f), 2.5f, 7f, 10f, 1.4f, 10f);
            Puffs(root, "Dust", Spine(0.6f) * 0f + new Vector3(0f, 1.5f, 0f), new Vector3(46f, 1f, 70f), new Color(0.55f, 0.50f, 0.45f, 0.20f), new Color(0.62f, 0.58f, 0.52f, 0.28f), 6f, 14f, 5f, 0.5f, 12f);
        }
    }
}
