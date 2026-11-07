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
    // A-81 row 1: the CarnivorousPlant colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsCarnivorousPlant = Register(Kind.CarnivorousPlant, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildPlant(at, yaw));

        static readonly Color PlantGreen = new Color(0.20f, 0.42f, 0.16f);
        static readonly Color PlantDark = new Color(0.12f, 0.30f, 0.12f);
        static readonly Color Maw = new Color(0.55f, 0.10f, 0.14f);
        static readonly Color Fang = new Color(0.86f, 0.82f, 0.66f);
        static readonly Color Slime = new Color(0.55f, 0.70f, 0.30f);
        static readonly Color Bone = new Color(0.80f, 0.76f, 0.68f);
        static readonly Color BoneDark = new Color(0.58f, 0.54f, 0.48f);
        static readonly Color PlantRed = new Color(0.66f, 0.10f, 0.12f);
        static readonly Color PlantPurple = new Color(0.40f, 0.14f, 0.46f);
        static readonly Color SkirtGreen = new Color(0.05f, 0.15f, 0.04f);
        static readonly Color SkirtDark = new Color(0.03f, 0.10f, 0.03f);

        /// <summary>A tapering tube along a cubic Bezier from <paramref name="p0"/> to <paramref name="p3"/>, banded in two colours.</summary>
        static void Tentacle(DunesColossus.MeshBuilder m, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float r0, float r1, Color c0, Color c1, int segs = 12)
        {
            Vector3 P(float t) { var u = 1f - t; return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3; }
            for (int i = 0; i < segs; i++)
            {
                float t0 = i / (float)segs, t1 = (i + 1f) / segs;
                m.Limb(P(t0), P(t1), Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), i % 2 == 0 ? c0 : c1, 7);
            }
        }

        /// <summary>A snap-trap head like the Mantrap's (#41): two shell halves hinged at <paramref name="hinge"/>, mouth along the frame's +Z, red lining and fangs round the rims.</summary>
        static void Trap(DunesColossus.MeshBuilder m, Vector3 hinge, Quaternion frame, Vector3 radii, float gape, float fang, int only = -1)
        {
            var up = frame * Quaternion.Euler(-gape * 0.7f, 0f, 0f);
            var low = frame * Quaternion.Euler(gape * 0.3f, 0f, 0f);
            for (int half = 0; half < 2; half++)
            {
                if (only >= 0 && half != only) continue; // A-77: one half only (0 upper, 1 lower), for a jaw that moves
                var rot = half == 0 ? up : low;
                var flip = half == 0 ? rot : rot * Quaternion.Euler(180f, 180f, 0f);
                var centre = hinge + rot * new Vector3(0f, 0f, radii.z);
                m.Dome(centre, radii, flip, half == 0 ? PlantGreen : PlantDark, false, 14, 5);
                m.Dome(centre, radii * 0.93f, flip, Maw, true, 14, 5);
                var dir = rot * (half == 0 ? Vector3.down : Vector3.up);
                for (int k = 0; k < 11; k++)
                {
                    var a = Mathf.Lerp(-80f, 80f, (k + half * 0.5f) / 10.5f) * Mathf.Deg2Rad;
                    var rim = centre + rot * new Vector3(Mathf.Sin(a) * radii.x * 0.95f, 0f, Mathf.Cos(a) * radii.z * 0.95f);
                    m.Limb(rim, rim + dir * fang + rot * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * fang * 0.3f, fang * 0.12f, fang * 0.02f, Fang, 4);
                }
            }
        }

        /// <summary>
        /// W-88 (#40, Zach's PoisonSwamp design): a carnivorous plant about 40 m tall standing in the bog. A ribbed bulb
        /// on six arched tentacle legs rooted in the mud, a thick stem up to a snap-trap head (as the Mantrap's, #41) gaping
        /// at the sky with drool hanging from its fangs, and eight tentacle arms reaching out, every third ending in a small
        /// trap of its own. Details: broad leaves lying flat round its feet, three young mantrap sprouts, a deer's bones
        /// picked clean by one foot, and pale spores drifting from the mouth.
        /// </summary>
        static Transform BuildPlant(float3 at, float yaw)
        {
            var root = new GameObject("CarnivorousPlant").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xD20Bu);
            // W-91 (#40, Zach): the body is drawn in its old units and shown at PlantScale (about a quarter of its old
            // ~40 m height), with a bigger head (HeadScale) and thinner vines (VineScale); the sprouts and bones stay life size.
            // A-77 (item 21, Zach): twice that now (about 22 m), and alive: the stem, the upper jaw and each arm are their
            // own pivoted parts, which SwayPlant moves (see PlantRig). The draws keep their order, so the shape is unchanged.
            const float PlantScale = 0.5f, HeadScale = 1.7f, VineScale = 0.5f;
            var stemMesh = new DunesColossus.MeshBuilder();
            var jawMesh = new DunesColossus.MeshBuilder();
            var armMesh = new DunesColossus.MeshBuilder[8];
            var armFrom = new Vector3[8];
            // The bulb, raised on its legs, with darker ribs down it.
            var bulb = new Vector3(0f, 12f, 0f);
            m.Ellipsoid(bulb, new Vector3(9f, 8f, 9f), Quaternion.identity, PlantGreen, 14, 8);
            for (int k = 0; k < 8; k++)
                m.Ellipsoid(bulb + Quaternion.Euler(0f, k * 45f, 0f) * new Vector3(0f, 0f, 8.2f), new Vector3(1.4f, 7.4f, 1.2f), Quaternion.Euler(0f, k * 45f, 0f), PlantDark, 6, 6);
            // Six tentacle legs arching out and down into the mud, each splayed into roots at the foot.
            for (int k = 0; k < 6; k++)
            {
                var a = (k * 60f + 30f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var from = bulb + d * 6f - Vector3.up * 3f;
                var foot = d * rng.NextFloat(22f, 27f) + Vector3.up * 0.6f;
                Tentacle(m, from, from + d * 7f + Vector3.up * 9f, foot + Vector3.up * 12f - d * 2f, foot, 3.2f * VineScale, 1.4f * VineScale, PlantGreen, PlantDark, 14);
                for (int r = 0; r < 4; r++)
                {
                    var ra = a + (r - 1.5f) * 0.5f;
                    m.Limb(foot, foot + new Vector3(Mathf.Sin(ra), -0.4f, Mathf.Cos(ra)) * rng.NextFloat(3f, 5f), 0.9f * VineScale, 0.15f, PlantDark, 5);
                }
            }
            // The stem, a little bent, up to the head; the mouth gapes at the sky over the bog.
            var neck = new Vector3(2f, 30f, 1f);
            stemMesh.Limb(bulb + Vector3.up * 6f, new Vector3(2.5f, 24f, -0.5f), 4.6f, 3.8f, PlantDark, 9);
            stemMesh.Limb(new Vector3(2.5f, 24f, -0.5f), neck, 3.8f, 3.4f, PlantGreen, 9);
            var face = Quaternion.Euler(-35f, 0f, 0f);
            var hinge = neck + new Vector3(0f, 2f, -4f) * HeadScale;
            Trap(jawMesh, hinge, face, new Vector3(7.5f, 5.5f, 8.5f) * HeadScale, 60f, 2.8f * HeadScale, 0);
            Trap(stemMesh, hinge, face, new Vector3(7.5f, 5.5f, 8.5f) * HeadScale, 60f, 2.8f * HeadScale, 1);
            // Drool hanging from the upper fangs.
            for (int k = 0; k < 4; k++)
            {
                var drip = hinge + face * Quaternion.Euler(-42f, 0f, 0f) * new Vector3(-4.5f + k * 3f, -1.5f, 15f) * HeadScale;
                jawMesh.Limb(drip, drip + Vector3.down * rng.NextFloat(2.5f, 5.5f), 0.35f * HeadScale, 0.15f * HeadScale, Slime, 4);
            }
            // Eight arms reaching out from the top of the bulb and the stem, curling at the tips; every third ends in a trap.
            for (int k = 0; k < 8; k++)
            {
                var a = (k * 45f + rng.NextFloat(-12f, 12f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var from = new Vector3(1f, 16f + k % 3 * 3.5f, 0f) + d * 4f;
                var reach = rng.NextFloat(20f, 28f);
                var lift = rng.NextFloat(-4f, 12f);
                var tip = from + d * reach + Vector3.up * lift;
                var am = armMesh[k] = new DunesColossus.MeshBuilder();
                armFrom[k] = from;
                Tentacle(am, from, from + d * 8f + Vector3.up * 12f, tip - d * 6f + Vector3.up * 10f + side * 4f, tip, 2.2f * VineScale, 0.5f * VineScale, PlantGreen, PlantDark, 14);
                if (k % 3 == 0)
                    Trap(am, tip, Quaternion.LookRotation(d + Vector3.up * 0.3f), new Vector3(1.8f, 1.3f, 2.1f), 50f, 0.8f);
                else
                {
                    // A tight curl back on itself.
                    var p = tip;
                    for (int c = 0; c < 4; c++)
                    {
                        var q = p + (Quaternion.AngleAxis(-70f * (c + 1), side) * d) * (2.2f - c * 0.4f);
                        am.Limb(p, q, (0.5f - c * 0.08f) * VineScale, (0.42f - c * 0.08f) * VineScale, c % 2 == 0 ? PlantDark : PlantGreen, 6);
                        p = q;
                    }
                }
            }
            // Broad leaves lying flat round the feet.
            for (int k = 0; k < 7; k++)
            {
                var a = k * 51.4f + rng.NextFloat(-10f, 10f);
                var rot = Quaternion.Euler(0f, a, 0f);
                m.Ellipsoid(rot * new Vector3(0f, 0.8f, 15f), new Vector3(4f, 0.4f, 9f), rot * Quaternion.Euler(-4f, 0f, 0f), k % 2 == 0 ? PlantGreen : PlantDark, 12, 4);
                m.Limb(rot * new Vector3(0f, 1.1f, 7f), rot * new Vector3(0f, 1.1f, 23f), 0.3f, 0.1f, PlantDark, 4);
            }
            // W-92 (#40, Zach): a leafy skirt, broad dark leaves each with a purple vein and every third tipped red, plus a
            // few red spots on the bulb. Its own random, seeded by the plant's spot, so the count and lean differ per plant
            // and the draws above keep their values. W-93: rooted high on the bulb under the stem and hanging down over
            // it (steep, then near-vertical), rather than splaying out at the base.
            var srng = Unity.Mathematics.Random.CreateFromIndex(math.hash((int3)math.floor(at)) ^ 0x5C127u);
            int skirt = srng.NextInt(9, 14);
            var lean = srng.NextFloat(18f, 34f);
            for (int k = 0; k < skirt; k++)
            {
                var turn = Quaternion.Euler(0f, k * 360f / skirt + srng.NextFloat(-8f, 8f), 0f);
                var droop = lean + srng.NextFloat(-6f, 6f);
                var len = srng.NextFloat(7f, 10f);
                var root0 = bulb + turn * new Vector3(0f, 6.5f, 6.4f);
                var inner = turn * Quaternion.Euler(35f + droop * 0.6f, 0f, 0f);
                var outer = turn * Quaternion.Euler(70f + droop * 0.4f, 0f, 0f);
                var bend = root0 + inner * Vector3.forward * len * 0.7f;
                var tipEnd = bend + outer * Vector3.forward * len * 0.9f;
                var col = k % 2 == 0 ? SkirtGreen : SkirtDark;
                m.Ellipsoid(root0 + inner * Vector3.forward * len * 0.35f, new Vector3(len * 0.4f, 0.35f, len * 0.4f), inner, col, 10, 4);
                m.Ellipsoid(bend + outer * Vector3.forward * len * 0.45f, new Vector3(len * 0.34f, 0.3f, len * 0.5f), outer, col, 10, 4);
                m.Limb(root0 + inner * Vector3.up * 0.3f, bend + inner * Vector3.up * 0.3f, 0.24f, 0.16f, PlantPurple, 4);
                m.Limb(bend + outer * Vector3.up * 0.28f, tipEnd + outer * Vector3.up * 0.2f, 0.16f, 0.06f, PlantPurple, 4);
                if (k % 3 == 0)
                    m.Ellipsoid(tipEnd - outer * Vector3.forward * 0.8f, new Vector3(len * 0.12f, 0.36f, 1.1f), outer, PlantRed, 8, 4);
            }
            for (int k = 0; k < 6; k++)
            {
                var spot = Quaternion.Euler(srng.NextFloat(-40f, 30f), srng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                m.Ellipsoid(bulb + Vector3.Scale(spot, new Vector3(9f, 8f, 9f)) * 0.99f, Vector3.one * srng.NextFloat(0.6f, 1.1f), Quaternion.LookRotation(spot), PlantRed, 8, 4);
            }
            AddPart("Body", root, m.ToMesh("CarnivorousPlant"));
            var body = root.Find("Body");
            body.localScale = Vector3.one * PlantScale;
            // A-89 row 2: the plant cost about 32 ms warm (over 100 ms cold) on one frame. The body above is this frame;
            // the moving parts, the sprouts and bones and the spores are the next (BuildRest), with the same draws.
            BuildRest = () => { if (root != null) BuildPlantRest(root, body, rng, stemMesh, jawMesh, armMesh, armFrom, hinge, face); };
            return root;
        }

        /// <summary>A-89 row 2: the carnivorous plant's second frame: its pivoted parts, the sprouts and bones, the spores.</summary>
        static void BuildPlantRest(Transform root, Transform body, Unity.Mathematics.Random rng, DunesColossus.MeshBuilder stemMesh,
                                   DunesColossus.MeshBuilder jawMesh, DunesColossus.MeshBuilder[] armMesh, Vector3[] armFrom, Vector3 hinge, Quaternion face)
        {
            const float PlantScale = 0.5f, HeadScale = 1.7f;
            var stemPivot = new Vector3(0f, 18f, 0f);
            PlantRig.Stem = PlantPart("Stem", body, stemPivot, Vector3.zero, stemMesh);
            PlantRig.Jaw = PlantPart("Jaw", PlantRig.Stem, hinge, stemPivot, jawMesh);
            PlantRig.JawAxis = face * Vector3.right;
            for (int k = 0; k < 8; k++)
                PlantRig.Arms[k] = PlantPart("Arm" + k, body, armFrom[k], Vector3.zero, armMesh[k]);
            // Three young sprouts, man-high, with shut traps (life size, round the shrunk plant).
            var m = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 3; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.NextFloat(30f, 36f) * PlantScale + Vector3.up * 0.3f;
                var top = p + new Vector3(rng.NextFloat(-0.5f, 0.5f), 2.2f, rng.NextFloat(-0.5f, 0.5f));
                m.Limb(p, top, 0.18f, 0.12f, PlantGreen, 5);
                Trap(m, top, Quaternion.Euler(-20f, rng.NextFloat(0f, 360f), 0f), new Vector3(0.35f, 0.28f, 0.4f), 18f, 0.12f);
            }
            // A deer's bones picked clean by one foot: ribs, spine, skull, a leg bone.
            var bones = new Vector3(-24f * PlantScale, 0.9f, 8f * PlantScale);
            m.Limb(bones, bones + new Vector3(0f, 0f, 2.4f), 0.16f, 0.14f, Bone, 5);
            for (int k = 0; k < 5; k++)
                for (int s = -1; s <= 1; s += 2)
                    m.Limb(bones + new Vector3(0f, 0.1f, 0.3f + k * 0.45f), bones + new Vector3(s * 0.7f, 0.5f, 0.4f + k * 0.45f), 0.06f, 0.05f, Bone, 4);
            m.Ellipsoid(bones + new Vector3(0.3f, 0.15f, 3f), new Vector3(0.28f, 0.25f, 0.45f), Quaternion.Euler(0f, 20f, 0f), Bone, 8, 4);
            m.Limb(bones + new Vector3(1.2f, 0f, -0.6f), bones + new Vector3(2.4f, 0f, 0.4f), 0.07f, 0.06f, BoneDark, 4);
            AddPart("Sprouts", root, m.ToMesh("CarnivorousPlantSprouts"));
            Puffs(root, "Spores", (hinge + face * new Vector3(0f, 0f, 9f * HeadScale) + Vector3.up * 4f) * PlantScale, new Vector3(4f, 1.5f, 4f),
                  new Color(0.80f, 0.86f, 0.55f, 0.22f), new Color(0.70f, 0.80f, 0.45f, 0.30f), 1.5f, 4f, 3f, 0.6f, 12f);
        }

        /// <summary>A pivot named <paramref name="name"/> at <paramref name="at"/> (body units) under <paramref name="parent"/> (itself at <paramref name="parentAt"/>), holding <paramref name="m"/>'s mesh drawn in body units.</summary>
        static Transform PlantPart(string name, Transform parent, Vector3 at, Vector3 parentAt, DunesColossus.MeshBuilder m)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = at - parentAt;
            AddPart(name + "Mesh", pivot, m.ToMesh("CarnivorousPlant" + name));
            pivot.GetChild(0).localPosition = -at;
            return pivot;
        }

        /// <summary>A-77 (item 21): the carnivorous plant's moving parts, set by BuildPlant (one plant per island).</summary>
        static class PlantRig
        {
            public static Transform Stem, Jaw;
            public static Vector3 JawAxis;
            public static readonly Transform[] Arms = new Transform[8];
        }

        /// <summary>
        /// A-77 (item 21): the carnivorous plant comes alive. Its stem sways a few degrees, its upper jaw snaps shut every
        /// 9 s (0.3 s to close, a 0.6 s hold, 2.5 s to gape again) and its arms writhe. Cosmetic and clock-driven, so every
        /// player sees much the same; skipped when hidden or past 500 m from the camera.
        /// </summary>
        static void SwayPlant()
        {
            if (PlantRig.Stem == null || PlantRig.Jaw == null || DunesColossus.Hidden)
                return;
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - PlantRig.Stem.position).sqrMagnitude > 500f * 500f)
                return;
            float t = Time.time;
            PlantRig.Stem.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.37f) * 3f, 0f, Mathf.Sin(t * 0.29f + 1f) * 2.5f);
            float c = t % 9f;
            float shut = c < 0.3f ? c / 0.3f : c < 0.9f ? 1f : c < 3.4f ? 1f - Mathf.SmoothStep(0f, 1f, (c - 0.9f) / 2.5f) : 0f;
            PlantRig.Jaw.localRotation = Quaternion.AngleAxis(50f * shut, PlantRig.JawAxis);
            for (int k = 0; k < PlantRig.Arms.Length; k++)
                if (PlantRig.Arms[k] != null)
                    PlantRig.Arms[k].localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f + k * 1.3f) * 7f, Mathf.Sin(t * 0.23f + k) * 4f, Mathf.Sin(t * 0.41f + k * 2.1f) * 6f);
        }
    }
}
