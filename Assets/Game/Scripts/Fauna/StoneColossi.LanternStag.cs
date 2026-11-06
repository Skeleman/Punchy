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
    // A-81 row 1: the LanternStag colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsLanternStag = Register(Kind.LanternStag, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildLanternStag(at, yaw, seed));

        static readonly Color StagPelt = new Color(0.26f, 0.24f, 0.40f);
        static readonly Color StagBelly = new Color(0.52f, 0.50f, 0.62f);
        static readonly Color StagAntler = new Color(0.86f, 0.82f, 0.72f);
        static readonly Color Brass = new Color(0.55f, 0.42f, 0.20f);
        static readonly Color LanternPane = new Color(1.00f, 0.90f, 0.62f);

        /// <summary>A-76 row 2: the flame points of the lantern stag's lanterns (StoneColossi glows them each frame).</summary>
        static readonly List<Transform> Lanterns = new List<Transform>();
        static Punchy.Weather.DayNightCycle cycle;

        /// <summary>
        /// A-76 row 2 (#40): a stag about 34 m to its antler tips walking the SparklingForest, its antlers hung with brass
        /// lanterns that glow faintly by day and brightly at night (a small flame and a warm light each, see
        /// <see cref="GlowLanterns"/>). Dusk-violet pelt flecked with pale sparkles, a paler belly and throat, slim legs with
        /// dark hooves, head raised; wide branching antlers of pale bone. Details: moss caught on the beams, a trailing vine
        /// of small blossoms on one antler, a bird's nest with three eggs in a fork of the other, one lantern hanging lower
        /// than the rest on a longer chain. Body (body, legs, neck, head, antlers, lantern cages) is one part, laid out as
        /// the mesa tortoise is (shoulders forward on +z, hips at -z), so ColossusRigs' stag rig can walk it; the flame
        /// points ("Lantern0".."LanternN") are empty children the rig moves onto its head bone.
        /// </summary>
        static Transform BuildLanternStag(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("LanternStag").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.3f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);

            // Four slim legs: upper leg to the knee (the hind ones bent back), cannon down to a dark hoof.
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var forward = k == 1 ? 2f : k == 2 ? -1.5f : 0f;
                var hip = new Vector3(sx * 3.4f, 15f, sz * 9f);
                var knee = new Vector3(sx * 3.3f, 8f, sz * 9f + (sz < 0f ? -2.2f : 0.6f) + forward * 0.5f);
                var hoof = new Vector3(sx * 3.2f, 1.2f, sz * 9f + forward);
                m.Limb(hip, knee, 1.7f, 0.9f, StagPelt, 7);
                m.Limb(knee, hoof, 0.8f, 0.6f, StagPelt, 7);
                m.Ellipsoid(hoof + new Vector3(0f, -0.5f, 0.3f), new Vector3(0.8f, 0.7f, 1f), Quaternion.identity, Hollow, 6, 3);
            }
            // Barrel, chest and haunches, belly paler; a short tail with a pale underside.
            m.Ellipsoid(new Vector3(0f, 16.5f, 0f), new Vector3(4.6f, 4.6f, 11f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 15.4f, 0.5f), new Vector3(3.8f, 3.2f, 9f), Quaternion.identity, StagBelly);
            m.Ellipsoid(new Vector3(0f, 17.2f, 8f), new Vector3(4.4f, 5f, 4.6f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 17.5f, -8.5f), new Vector3(4.6f, 4.8f, 4.4f), Quaternion.identity, StagPelt);
            m.Ellipsoid(new Vector3(0f, 18.6f, -12.8f), new Vector3(1f, 1.6f, 0.8f), Quaternion.Euler(-30f, 0f, 0f), StagBelly, 6, 4);
            // The neck up and forward to the head, with a pale throat patch.
            m.Limb(new Vector3(0f, 18.5f, 9.5f), new Vector3(0f, 24.5f, 13.5f), 3.2f, 2f, StagPelt, 9);
            m.Ellipsoid(new Vector3(0f, 20.8f, 12.6f), new Vector3(1.8f, 2.6f, 1.4f), Quaternion.Euler(-30f, 0f, 0f), StagBelly, 7, 4);
            var head = new Vector3(0f, 25.5f, 14.5f);
            var face = Quaternion.Euler(32f, 0f, 0f);
            m.Ellipsoid(head, new Vector3(1.8f, 1.9f, 2.4f), face, StagPelt);
            m.Limb(head + face * new Vector3(0f, -0.2f, 1.2f), head + face * new Vector3(0f, -0.4f, 4.6f), 1.3f, 0.8f, StagPelt, 8);   // muzzle
            m.Ellipsoid(head + face * new Vector3(0f, -0.3f, 4.7f), new Vector3(0.75f, 0.6f, 0.5f), face, Hollow, 6, 3);           // nose
            for (int s = -1; s <= 1; s += 2)
            {
                m.Ellipsoid(head + face * new Vector3(s * 1.5f, 0.5f, 1f), new Vector3(0.35f, 0.4f, 0.4f), face, Hollow, 5, 3);   // eyes
                m.Ellipsoid(head + new Vector3(s * 2.4f, 1.2f, -0.8f), new Vector3(1.4f, 0.5f, 0.8f), Quaternion.Euler(0f, s * 20f, s * -25f), StagPelt, 6, 3);   // ears
            }
            // Sparkle flecks on the pelt: tiny pale facets over the back and flanks.
            for (int k = 0; k < 70; k++)
            {
                var a = rng.NextFloat(-1.3f, 1.3f);
                var z = rng.NextFloat(-11f, 10f);
                var p = new Vector3(Mathf.Sin(a) * 4.7f, 16.5f + Mathf.Cos(a) * 4.7f, z);
                m.Ellipsoid(p, new Vector3(0.18f, 0.12f, 0.18f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Color.Lerp(LanternPane, Color.white, 0.6f), 4, 2);
            }

            // Antlers: a beam each side sweeping up, out and back, three tines and a split crown; lanterns hang under
            // the tine tips on short chains (the brow tines carry none, one hangs lower than the rest).
            var tineTips = new List<Vector3>();
            for (int s = -1; s <= 1; s += 2)
            {
                var b0 = head + new Vector3(s * 1.1f, 1.7f, -0.6f);
                var b1 = b0 + new Vector3(s * 3.2f, 2.8f, -1.2f);
                var b2 = b1 + new Vector3(s * 2.6f, 3.6f, -1.6f);
                var b3 = b2 + new Vector3(s * 1.4f, 3.2f, 0.4f);
                var b4 = b2 + new Vector3(s * 3.2f, 1.4f, -1.4f);
                m.Limb(b0, b1, 0.7f, 0.55f, StagAntler, 6);
                m.Limb(b1, b2, 0.55f, 0.42f, StagAntler, 6);
                m.Limb(b2, b3, 0.42f, 0.18f, StagAntler, 6);
                m.Limb(b2, b4, 0.32f, 0.12f, StagAntler, 5);
                Vector3[] from = { b0 + (b1 - b0) * 0.4f, b1, b1 + (b2 - b1) * 0.6f };
                Vector3[] reach = { new Vector3(s * 0.6f, 1.8f, 2.6f), new Vector3(s * 2.8f, 1.2f, 1.2f), new Vector3(s * 1.2f, 2.6f, -2.4f) };
                for (int k = 0; k < 3; k++)
                {
                    m.Limb(from[k], from[k] + reach[k], 0.36f, 0.12f, StagAntler, 5);
                    tineTips.Add(from[k] + reach[k]);
                }
                tineTips.Add(b3);
                tineTips.Add(b4);
            }
            var lanternAt = new List<Vector3>();
            var low = 1 + rng.NextInt(4);
            for (int k = 0; k < tineTips.Count; k++)
            {
                if (k % 5 == 0)
                    continue;
                var hook = tineTips[k];
                var chain = k == low ? 3.4f : rng.NextFloat(0.9f, 1.6f);
                var lamp = hook + Vector3.down * (chain + 0.7f);
                m.Limb(hook, lamp + Vector3.up * 0.7f, 0.06f, 0.06f, Dark, 3);                                              // chain
                m.Ellipsoid(lamp + Vector3.up * 0.7f, new Vector3(0.55f, 0.22f, 0.55f), Quaternion.identity, Brass, 6, 3);   // cap
                m.Ellipsoid(lamp, new Vector3(0.48f, 0.62f, 0.48f), Quaternion.Euler(0f, 45f, 0f), LanternPane, 4, 3);       // pane
                m.Ellipsoid(lamp + Vector3.down * 0.62f, new Vector3(0.42f, 0.14f, 0.42f), Quaternion.identity, Brass, 6, 2); // base
                lanternAt.Add(lamp);
            }
            // Moss on the beams, a vine of small blossoms trailing from one antler, and a nest in a fork of the other.
            for (int k = 0; k < 10; k++)
            {
                var q = Vector3.Lerp(head + Vector3.up * 2f, tineTips[rng.NextInt(tineTips.Count)], rng.NextFloat(0.3f, 0.7f));
                m.Ellipsoid(q, new Vector3(0.5f, 0.3f, 0.5f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Moss, 5, 3);
            }
            var vineSide = rng.NextFloat() < 0.5f ? -1f : 1f;
            var v0 = head + new Vector3(vineSide * 4.3f, 4.5f, -1.8f);
            Tentacle(m, v0, v0 + new Vector3(vineSide * 1.5f, -2.5f, 0.4f), v0 + new Vector3(vineSide * 0.6f, -4.5f, 1.2f), v0 + new Vector3(vineSide * 1.8f, -6.2f, 0.6f), 0.12f, 0.06f, Moss, Moss, 8);
            for (int k = 0; k < 6; k++)
            {
                var t = (k + 0.5f) / 6f;
                var p = v0 + new Vector3(vineSide * 1.5f * t, -6f * t, 0.8f * Mathf.Sin(t * 3f));
                m.Ellipsoid(p + new Vector3(0.2f, 0f, 0.15f), new Vector3(0.22f, 0.18f, 0.22f), Quaternion.identity, k % 2 == 0 ? new Color(0.96f, 0.72f, 0.86f) : new Color(0.98f, 0.94f, 0.80f), 5, 3);
            }
            var nest = head + new Vector3(-vineSide * 4.3f, 4.5f, -1.8f);
            m.Ellipsoid(nest, new Vector3(0.9f, 0.45f, 0.9f), Quaternion.identity, new Color(0.45f, 0.36f, 0.24f), 7, 3);
            m.Ellipsoid(nest + Vector3.up * 0.32f, new Vector3(0.6f, 0.15f, 0.6f), Quaternion.identity, new Color(0.30f, 0.24f, 0.16f), 6, 2);
            for (int k = 0; k < 3; k++)
                m.Ellipsoid(nest + new Vector3((k - 1) * 0.25f, 0.42f, (k % 2) * 0.2f), new Vector3(0.13f, 0.11f, 0.15f), Quaternion.identity, new Color(0.62f, 0.80f, 0.86f), 5, 3);
            AddPart("Body", root, m.ToMesh("LanternStag"));

            // The flame points, as children so they follow the root (and the rig's head once it is awake).
            for (int k = 0; k < lanternAt.Count; k++)
            {
                var point = new GameObject("Lantern" + k).transform;
                point.SetParent(root, false);
                point.localPosition = lanternAt[k] + Vector3.down * 0.25f;
                Lanterns.Add(point);
            }
            return root;
        }
    }
}
