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
    // A-81 row 1: the GlassHeron colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsGlassHeron = Register(Kind.GlassHeron, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildGlassHeron(at, yaw, seed));

        static readonly Color GlassPale = new Color(0.80f, 0.92f, 0.93f);
        static readonly Color GlassTeal = new Color(0.46f, 0.72f, 0.76f);
        static readonly Color GlassDeep = new Color(0.24f, 0.44f, 0.52f);
        static readonly Color GlassSmoke = new Color(0.30f, 0.37f, 0.42f);
        static readonly Color GlassSand = new Color(0.84f, 0.80f, 0.70f);

        /// <summary>
        /// A-76 row 1 (#40): a heron of cut glass about 30 m tall wading the GlassDesert's dunes as if they were shallows.
        /// Every part is a low, few-sided shape so it reads as faceted glass, in pale, sea and deep teal tones: a body with
        /// folded wings ending in long shard primaries, an S-necked head with an amber dagger beak and a crest of two glass
        /// plumes, and two stilt legs with backward-bending ankles and splayed toes sunk into small sand drifts. Details:
        /// shed shard feathers lying round its feet, a branching fulgurite (lightning glass) beside it, a small sandpiper
        /// standing on its back. Body (body, neck, head, legs) and Litter are separate parts, laid out with the hips at
        /// (0, 16, 0) and the neck forward on +z, so ColossusRigs' heron rig can stab and walk it (A-72 hooks: Spots, Root).
        /// </summary>
        static Transform BuildGlassHeron(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("GlassHeron").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            Color Tone() => rng.NextFloat() < 0.5f ? Color.Lerp(GlassPale, GlassTeal, rng.NextFloat(0f, 0.6f)) : Color.Lerp(GlassTeal, GlassDeep, rng.NextFloat(0f, 0.7f));

            // Two stilt legs: thigh from the hip down and back to the ankle, shin down and forward to the foot, three
            // toes forward and one back; the right foot a little ahead (mid-wade).
            for (int s = -1; s <= 1; s += 2)
            {
                var ahead = s > 0 ? 1.6f : -0.6f;
                var hip = new Vector3(s * 2.2f, 16f, 0f);
                var ankle = new Vector3(s * 2.5f, 8f, -1.6f + ahead * 0.4f);
                var foot = new Vector3(s * 2.7f, 0.5f, 0.4f + ahead);
                m.Limb(hip, ankle, 0.9f, 0.55f, GlassSmoke, 6);
                m.Ellipsoid(ankle, new Vector3(0.75f, 0.75f, 0.75f), Quaternion.identity, GlassDeep, 6, 4);
                m.Limb(ankle, foot, 0.55f, 0.45f, GlassSmoke, 6);
                for (int t = -1; t <= 1; t++)
                    m.Limb(foot, foot + new Vector3(t * 1.9f, -0.35f, 3.6f - math.abs(t) * 0.6f), 0.32f, 0.1f, GlassSmoke, 5);
                m.Limb(foot, foot + new Vector3(0f, -0.3f, -2.2f), 0.28f, 0.08f, GlassSmoke, 5);
                m.Ellipsoid(foot + new Vector3(0f, -0.6f, 0.8f), new Vector3(3.2f, 0.7f, 3.8f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), GlassSand, 7, 3);   // sand drift
            }
            // The body, tipped up at the front, a paler breast under it and a short tail of shards behind.
            var tip = Quaternion.Euler(-14f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 19f, 0f), new Vector3(4.2f, 4.4f, 8f), tip, GlassTeal, 8, 5);
            m.Ellipsoid(new Vector3(0f, 17.6f, 2.6f), new Vector3(3.4f, 3f, 5f), tip, GlassPale, 7, 4);
            for (int k = 0; k < 5; k++)
                m.Ellipsoid(new Vector3((k - 2) * 0.9f, 17.4f - math.abs(k - 2) * 0.3f, -8.6f), new Vector3(0.5f, 0.25f, 2.6f), Quaternion.Euler(18f, (k - 2) * 7f, 0f), Tone(), 4, 3);
            // Folded wings along the sides, each ending in a fan of long shard primaries crossing over the tail.
            for (int s = -1; s <= 1; s += 2)
            {
                var wing = Quaternion.Euler(-10f, s * 4f, s * 6f);
                m.Ellipsoid(new Vector3(s * 3.7f, 19.6f, -1f), new Vector3(1.3f, 3.6f, 8.4f), wing, GlassTeal, 6, 4);
                var shards = rng.NextInt(6, 9);
                for (int k = 0; k < shards; k++)
                {
                    var y = 20.8f - k * 0.55f;
                    var len = rng.NextFloat(3.4f, 5.6f);
                    m.Ellipsoid(new Vector3(s * (3.2f + k * 0.08f), y, -7.4f - len * 0.4f), new Vector3(0.45f, 0.18f, len), Quaternion.Euler(14f + k * 1.5f, -s * (6f + k * 1.6f), 0f), Tone(), 4, 3);
                }
            }
            // The S-neck: up and back from the breast, then forward to the head.
            Tentacle(m, new Vector3(0f, 20.5f, 5.5f), new Vector3(0f, 26f, 2.4f), new Vector3(0f, 24.5f, 8.4f), new Vector3(0f, 29.4f, 7.6f), 1.6f, 0.95f, GlassPale, GlassTeal, 12);
            var head = new Vector3(0f, 30f, 8f);
            m.Ellipsoid(head, new Vector3(1.3f, 1.4f, 2.2f), Quaternion.Euler(-6f, 0f, 0f), GlassPale, 7, 4);
            m.Limb(head + new Vector3(0f, -0.2f, 1.8f), head + new Vector3(0f, -0.5f, 7f), 0.62f, 0.05f, new Color(0.86f, 0.74f, 0.42f), 5);   // amber dagger beak
            for (int s = -1; s <= 1; s += 2)
                m.Ellipsoid(head + new Vector3(s * 1.05f, 0.35f, 0.9f), new Vector3(0.3f, 0.3f, 0.34f), Quaternion.identity, Hollow, 5, 3);
            m.Ellipsoid(head + new Vector3(0f, 1.1f, -0.2f), new Vector3(0.7f, 0.4f, 1.6f), Quaternion.identity, GlassDeep, 5, 3);   // dark cap
            for (int k = 0; k < 2; k++)                                                                                       // two crest plumes trailing back
                m.Limb(head + new Vector3((k - 0.5f) * 0.5f, 1f, -1f), head + new Vector3((k - 0.5f) * 1.4f, 1.6f + k * 0.4f, -6.5f + k * 1.2f), 0.22f, 0.04f, GlassPale, 4);
            AddPart("Body", root, m.ToMesh("GlassHeron"));

            // Round its feet, in its own part (the rig leaves it lying): shed shard feathers, a fulgurite, a sandpiper on its back.
            var f = new DunesColossus.MeshBuilder();
            var feathers = rng.NextInt(10, 16);
            for (int k = 0; k < feathers; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(5f, 17f);
                var len = rng.NextFloat(1.2f, 3.4f);
                f.Ellipsoid(new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r), new Vector3(0.35f, 0.14f, len), Quaternion.Euler(rng.NextFloat(-8f, 8f), rng.NextFloat(0f, 360f), rng.NextFloat(-20f, 20f)), Tone(), 4, 3);
            }
            // A fulgurite: a crooked tube of fused sand standing out of a dune, branching twice.
            var fa = rng.NextFloat(0f, Mathf.PI * 2f);
            var fb = new Vector3(Mathf.Cos(fa) * 20f, -0.4f, Mathf.Sin(fa) * 20f);
            var fulg = new Color(0.62f, 0.58f, 0.50f);
            var f1 = fb + new Vector3(0.6f, 2.6f, -0.3f);
            var f2 = f1 + new Vector3(-0.5f, 2f, 0.4f);
            f.Limb(fb, f1, 0.55f, 0.42f, fulg, 6);
            f.Limb(f1, f2, 0.42f, 0.22f, fulg, 6);
            f.Limb(f1, f1 + new Vector3(1.3f, 1.1f, 0.2f), 0.26f, 0.08f, fulg, 5);
            f.Limb(f2, f2 + new Vector3(0.2f, 1f, -0.7f), 0.2f, 0.06f, fulg, 5);
            AddPart("Litter", root, f.ToMesh("GlassHeronLitter"));
            // A sandpiper standing on its back, facing aft, in a part of its own that rides the body bone.
            var b = new DunesColossus.MeshBuilder();
            var bird = new Vector3(0.6f, 23.3f, -2f);
            var down = new Color(0.62f, 0.54f, 0.44f);
            b.Ellipsoid(bird, new Vector3(0.22f, 0.2f, 0.36f), Quaternion.Euler(-10f, 180f, 0f), down, 6, 3);
            b.Ellipsoid(bird + new Vector3(0f, 0.2f, -0.34f), new Vector3(0.13f, 0.13f, 0.14f), Quaternion.identity, down, 5, 3);
            b.Limb(bird + new Vector3(0f, 0.18f, -0.46f), bird + new Vector3(0f, 0.15f, -0.8f), 0.03f, 0.01f, Hollow, 3);
            AddPart("Sandpiper", root, b.ToMesh("GlassHeronSandpiper"));
            return root;
        }
    }
}
