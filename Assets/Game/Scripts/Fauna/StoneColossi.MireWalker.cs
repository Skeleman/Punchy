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
    // A-81 row 1: the MireWalker colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsMireWalker = Register(Kind.MireWalker, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildMireWalker(at, yaw, seed));

        static readonly Color MireBark = new Color(0.27f, 0.23f, 0.18f);
        static readonly Color MireDark = new Color(0.15f, 0.13f, 0.11f);
        static readonly Color BeardMoss = new Color(0.56f, 0.61f, 0.52f);
        static readonly Color FogEye = new Color(0.82f, 0.96f, 0.72f);

        /// <summary>
        /// A-79 (#40): the FoggyMarsh colossus, a hunched mire walker that creeps on a skirt of arching mangrove prop roots.
        /// Moss sheets its back and pale beards of hanging moss sway under it; a long mossy skull with two pale lantern eyes
        /// looms out of the fog ahead of it, with dead branches for antlers and a rusty lantern hanging from one. Small
        /// details: a heron standing on its hump, a reed shrine with a candle on its back, shelf fungi, a fishing net snagged
        /// on a root with a fish still in it, mud mounds and rootlets at each root's foot, and mist pouring off the roots.
        /// </summary>
        static Transform BuildMireWalker(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("MireWalker").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.4f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed | 1u);
            var glow = new List<Vector3>();

            // The root skirt: prop roots arching out and down from under the body, each ending in a mud mound and rootlets.
            const int roots = 22;
            for (int k = 0; k < roots; k++)
            {
                var a = (k + rng.NextFloat(-0.3f, 0.3f)) / roots * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var reach = rng.NextFloat(14f, 18f);
                var from = d * 5f + Vector3.up * rng.NextFloat(11f, 14f);
                var foot = d * reach + Vector3.up * -0.5f;
                Tentacle(m, from, d * (reach * 0.55f) + Vector3.up * rng.NextFloat(14f, 17f), d * (reach * 0.92f) + Vector3.up * 6f, foot,
                         rng.NextFloat(1.2f, 1.6f), 0.5f, MireBark, MireDark, 12);
                m.Ellipsoid(foot + Vector3.up * 0.2f, new Vector3(1.8f, 0.6f, 1.8f), Quaternion.identity, Color.Lerp(MireDark, Moss, 0.3f), 7, 3);
                for (int r = 0; r < 3; r++)
                {
                    var ra = a + rng.NextFloat(-1.2f, 1.2f);
                    m.Limb(foot + Vector3.up * 0.4f, foot + new Vector3(Mathf.Sin(ra), -0.3f, Mathf.Cos(ra)) * rng.NextFloat(1.5f, 3f), 0.35f, 0.08f, MireDark, 4);
                }
            }

            // The hunched body and hump, sheeted in moss.
            var body = new Vector3(0f, 18f, -2f);
            m.Ellipsoid(body, new Vector3(11f, 8f, 14f), Quaternion.identity, MireBark);
            m.Ellipsoid(body + new Vector3(0f, 6f, -3f), new Vector3(8f, 6f, 9f), Quaternion.Euler(-10f, 0f, 0f), MireBark);
            m.Ellipsoid(body + new Vector3(0f, 3.2f, -1f), new Vector3(10.6f, 5.5f, 13.4f), Quaternion.identity, Moss, 12, 5);
            m.Ellipsoid(body + new Vector3(0f, 8.6f, -3.5f), new Vector3(7.2f, 3.6f, 8.2f), Quaternion.Euler(-10f, 0f, 0f), Color.Lerp(Moss, BeardMoss, 0.3f), 10, 4);

            // Beards of pale moss hanging from its underside and flanks, some long enough to brush the root tops.
            for (int k = 0; k < 34; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = body + new Vector3(Mathf.Sin(a) * 10f, rng.NextFloat(-6f, -1f), Mathf.Cos(a) * 12.5f) * rng.NextFloat(0.75f, 0.98f);
                var len = rng.NextFloat(3f, 8f);
                m.Limb(p, p + new Vector3(rng.NextFloat(-0.6f, 0.6f), -len, rng.NextFloat(-0.6f, 0.6f)), rng.NextFloat(0.35f, 0.6f), 0.08f,
                       Color.Lerp(BeardMoss, Moss, rng.NextFloat(0f, 0.4f)), 4);
            }

            // Shelf fungi stepping up one flank.
            for (int k = 0; k < 6; k++)
                m.Ellipsoid(body + new Vector3(10.4f - k * 0.3f, -3f + k * 1.3f, -6f + k * 1.6f), new Vector3(1.6f, 0.3f, 1.2f), Quaternion.Euler(0f, 0f, -12f),
                            Color.Lerp(new Color(0.78f, 0.66f, 0.46f), MireBark, k * 0.1f), 7, 3);

            // The neck and the long mossy skull, jaw hanging a little open, two pale lantern eyes.
            var head = new Vector3(0f, 22f, 23f);
            m.Limb(body + new Vector3(0f, 2f, 11f), head + new Vector3(0f, -0.5f, -2f), 5f, 3.4f, MireBark, 10);
            m.Ellipsoid(head, new Vector3(4f, 3.5f, 5.5f), Quaternion.identity, MireBark);
            m.Ellipsoid(head + new Vector3(0f, 1.6f, -0.5f), new Vector3(3.6f, 1.6f, 4.8f), Quaternion.identity, Moss, 9, 4);
            m.Ellipsoid(head + new Vector3(0f, -1f, 5f), new Vector3(2.4f, 2f, 4f), Quaternion.identity, MireBark);
            m.Ellipsoid(head + new Vector3(0f, -3.2f, 3.6f), new Vector3(2.6f, 0.9f, 4.4f), Quaternion.Euler(12f, 0f, 0f), MireDark, 8, 4);
            m.Ellipsoid(head + new Vector3(0f, -2.4f, 4f), new Vector3(2f, 0.6f, 3.6f), Quaternion.Euler(6f, 0f, 0f), Hollow, 7, 3);
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = head + new Vector3(s * 2.7f, 1f, 2.6f);
                m.Ellipsoid(eye, new Vector3(0.95f, 0.8f, 0.95f), Quaternion.identity, FogEye, 8, 5);
                glow.Add(eye + new Vector3(s * 0.5f, 0f, 0.4f));
                // Dead branches for antlers, forking twice.
                var b0 = head + new Vector3(s * 2f, 3f, -1.5f);
                var b1 = b0 + new Vector3(s * 3.5f, 6f, -2f);
                m.Limb(b0, b1, 0.7f, 0.4f, MireDark, 6);
                m.Limb(b1, b1 + new Vector3(s * 3f, 3.5f, 1f), 0.4f, 0.12f, MireDark, 5);
                m.Limb(b1, b1 + new Vector3(s * 0.5f, 4.5f, -2.5f), 0.35f, 0.1f, MireDark, 5);
                m.Limb(b0 + (b1 - b0) * 0.5f, b0 + (b1 - b0) * 0.5f + new Vector3(s * -1f, 2.5f, 1.5f), 0.3f, 0.08f, MireDark, 4);
                // Beard moss trailing off each antler.
                m.Limb(b1, b1 + new Vector3(0f, -3.5f, 0f), 0.3f, 0.06f, BeardMoss, 4);
            }
            // A rusty lantern hanging from the right antler on a short chain.
            var hook = head + new Vector3(2f + 3.5f * 0.8f, 3f + 6f * 0.8f, -1.5f - 2f * 0.8f);
            m.Limb(hook, hook + Vector3.down * 2.6f, 0.06f, 0.06f, Rust, 3);
            m.Ellipsoid(hook + Vector3.down * 3.4f, new Vector3(0.5f, 0.75f, 0.5f), Quaternion.identity, Rust, 6, 4);
            glow.Add(hook + Vector3.down * 3.4f);

            // A heron standing on the hump, looking out over the fog.
            var heron = body + new Vector3(2.5f, 14.6f, -4f);
            for (int s = -1; s <= 1; s += 2)
                m.Limb(heron + new Vector3(s * 0.2f, -2f, 0f), heron + new Vector3(s * 0.2f, 0f, 0f), 0.06f, 0.06f, Dark, 3);
            m.Ellipsoid(heron + new Vector3(0f, 0.6f, 0f), new Vector3(0.45f, 0.5f, 0.9f), Quaternion.Euler(-15f, 0f, 0f), new Color(0.82f, 0.84f, 0.86f), 7, 4);
            m.Limb(heron + new Vector3(0f, 0.9f, 0.7f), heron + new Vector3(0f, 2f, 0.9f), 0.12f, 0.1f, new Color(0.82f, 0.84f, 0.86f), 4);
            m.Limb(heron + new Vector3(0f, 2f, 0.9f), heron + new Vector3(0f, 1.9f, 1.8f), 0.08f, 0.02f, new Color(0.85f, 0.70f, 0.30f), 3);

            // A little reed shrine on its back, a candle burning inside.
            var shrine = body + new Vector3(-3.5f, 12.4f, 1f);
            for (int k = 0; k < 4; k++)
                m.Limb(shrine + new Vector3(k % 2 == 0 ? -0.9f : 0.9f, -0.3f, k < 2 ? -0.9f : 0.9f), shrine + new Vector3(k % 2 == 0 ? -0.8f : 0.8f, 1.8f, k < 2 ? -0.8f : 0.8f), 0.1f, 0.1f, MireDark, 3);
            m.Ellipsoid(shrine + Vector3.up * 2.3f, new Vector3(1.5f, 0.8f, 1.5f), Quaternion.identity, new Color(0.62f, 0.56f, 0.34f), 7, 3);
            m.Limb(shrine + Vector3.up * -0.2f, shrine + Vector3.up * 0.4f, 0.12f, 0.12f, new Color(0.92f, 0.90f, 0.80f), 4);
            glow.Add(shrine + Vector3.up * 0.6f);

            // A fishing net snagged on one root, a fish still caught in it.
            var snag = new Vector3(Mathf.Sin(1.1f), 0f, Mathf.Cos(1.1f)) * 12f + Vector3.up * 9f;
            m.Ellipsoid(snag, new Vector3(2.4f, 2.8f, 0.3f), Quaternion.Euler(0f, 1.1f * Mathf.Rad2Deg + 90f, 8f), new Color(0.40f, 0.38f, 0.30f), 8, 4);
            m.Ellipsoid(snag + new Vector3(0f, -2f, 0f), new Vector3(0.35f, 0.9f, 0.25f), Quaternion.Euler(0f, 0f, 20f), new Color(0.55f, 0.62f, 0.60f), 6, 3);
            AddPart("Body", root, m.ToMesh("MireWalker"));

            // Mist pouring off the roots, low and slow, and a few fireflies in the moss beards. A-84 row 4: the mist was
            // pale and thin enough to vanish against the sunlit marsh, so it is a touch greyer and thicker, and a dense
            // bank hugs the water round the feet under it. Both stay below the moss beards, so at night they never cover
            // the glow points or the fireflies.
            Puffs(root, "Mist", new Vector3(0f, 1.5f, 0f), new Vector3(34f, 2f, 34f), new Color(0.70f, 0.73f, 0.74f, 0.50f), new Color(0.80f, 0.83f, 0.84f, 0.58f), 7f, 15f, 9f, 0.3f, 14f);
            Puffs(root, "MistBank", new Vector3(0f, 0.6f, 0f), new Vector3(26f, 0.6f, 26f), new Color(0.66f, 0.70f, 0.72f, 0.62f), new Color(0.76f, 0.80f, 0.82f, 0.70f), 9f, 16f, 6f, 0.06f, 16f);
            Puffs(root, "Fireflies", body + new Vector3(0f, -7f, 0f), new Vector3(18f, 6f, 22f), new Color(0.80f, 1f, 0.45f, 0.9f), new Color(0.95f, 1f, 0.60f, 0.8f), 0.12f, 0.25f, 3f, 0.2f, 6f);

            for (int k = 0; k < glow.Count; k++)
            {
                var point = new GameObject("Glow" + k).transform;
                point.SetParent(root, false);
                point.localPosition = glow[k];
                Lanterns.Add(point);
            }
            return root;
        }
    }
}
