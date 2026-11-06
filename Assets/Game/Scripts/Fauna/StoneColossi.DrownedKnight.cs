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
    // A-81 row 1: the DrownedKnight colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsDrownedKnight = Register(Kind.DrownedKnight, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildKnight(at, yaw));

        static readonly Color Algae = new Color(0.22f, 0.30f, 0.16f);
        static readonly Color Bronze = new Color(0.30f, 0.42f, 0.36f);
        static readonly Color Lily = new Color(0.24f, 0.46f, 0.18f);
        static readonly Color Bloom = new Color(0.92f, 0.86f, 0.90f);

        /// <summary>
        /// A knight about 60 m long lying on his back in the marsh, sunk to the belt: the great helm turned a little to
        /// one side, breastplate and knees breaking the surface, one gauntlet reaching up out of the bog still gripping a
        /// snapped sword, his kite shield tilted half under beside him. Green bronze with algae on every face turned up.
        /// Details: seeded lily pads round the chest (every fifth in bloom), the blade's broken tip lying further off,
        /// and a crest of spikes behind the helm.
        /// </summary>
        static Transform BuildKnight(float3 at, float yaw)
        {
            var root = new GameObject("DrownedKnight").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y, at.z), Quaternion.Euler(0f, yaw, 0f));
            const float Ground = 0f;
            var m = new DunesColossus.MeshBuilder();
            // Torso lying along +Z (head at +Z), breastplate ridge up, the belt just under the ground.
            m.Ellipsoid(new Vector3(0f, Ground - 1f, 4f), new Vector3(11f, 5.5f, 13f), Quaternion.identity, Bronze);
            m.Ellipsoid(new Vector3(0f, Ground + 3.6f, 6f), new Vector3(3f, 1.8f, 9f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(0f, Ground - 0.5f, -9f), new Vector3(9.5f, 3.5f, 4f), Quaternion.identity, Dark);
            // Legs: knees up out of the water, shins back down, sabatons pointing up at the far end.
            for (int s = -1; s <= 1; s += 2)
            {
                var hip = new Vector3(s * 5f, Ground - 2f, -12f);
                var knee = new Vector3(s * 6.5f, Ground + 5f, -24f);
                var ankle = new Vector3(s * 7f, Ground - 1.5f, -36f);
                m.Limb(hip, knee, 4.6f, 3.8f, Bronze);
                m.Ellipsoid(knee, new Vector3(4.2f, 3.2f, 4.2f), Quaternion.identity, Dark);
                m.Limb(knee, ankle, 3.8f, 3f, Bronze);
                m.Ellipsoid(ankle + new Vector3(0f, 2.5f, -1.5f), new Vector3(2.6f, 4.5f, 2.4f), Quaternion.Euler(-15f, 0f, 0f), Dark);
            }
            // Pauldrons; the left arm lies flat, the right is raised from the elbow with the gauntlet round a snapped sword.
            m.Ellipsoid(new Vector3(-11.5f, Ground + 0.5f, 14f), new Vector3(5f, 3.6f, 5f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(11.5f, Ground + 0.5f, 14f), new Vector3(5f, 3.6f, 5f), Quaternion.identity, Dark);
            m.Limb(new Vector3(-13f, Ground - 1f, 12f), new Vector3(-16f, Ground - 1f, -2f), 3.4f, 3f, Bronze);
            m.Limb(new Vector3(-16f, Ground - 1f, -2f), new Vector3(-15f, Ground - 0.8f, -14f), 3f, 2.6f, Bronze);
            m.Ellipsoid(new Vector3(-15f, Ground - 0.3f, -16.5f), new Vector3(2.8f, 1.6f, 3f), Quaternion.identity, Dark);
            var elbow = new Vector3(15.5f, Ground, 2f);
            var fist = new Vector3(16f, Ground + 15f, 4f);
            m.Limb(new Vector3(13f, Ground - 1f, 12f), elbow, 3.4f, 3f, Bronze);
            m.Limb(elbow, fist, 3f, 2.6f, Bronze);
            m.Ellipsoid(fist + new Vector3(0f, 1.5f, 0f), new Vector3(3f, 3.2f, 3f), Quaternion.identity, Dark);
            m.Ellipsoid(fist + new Vector3(0f, 4.4f, 0f), new Vector3(4.5f, 0.8f, 1.1f), Quaternion.identity, Rust);
            m.Ellipsoid(fist + new Vector3(0f, -0.2f, 0f), new Vector3(1.2f, 1.2f, 1.2f), Quaternion.identity, Rust);
            m.Ellipsoid(fist + new Vector3(0f, 9f, 0f), new Vector3(1.4f, 4.6f, 0.4f), Quaternion.Euler(0f, 0f, 4f), Dark, 6, 6);
            m.Ellipsoid(new Vector3(30f, Ground - 0.2f, 12f), new Vector3(1.3f, 0.4f, 6f), Quaternion.Euler(4f, 28f, 8f), Dark, 6, 4);
            // Great helm turned a little to his left: visor slit, breaths, and a crest of spikes behind.
            var turn = Quaternion.Euler(0f, -18f, 0f) * Quaternion.Euler(-80f, 0f, 0f);
            var helm = new Vector3(0f, Ground + 1f, 23f);
            m.Limb(new Vector3(0f, Ground - 1f, 15f), helm, 4.4f, 4f, Dark);
            m.Ellipsoid(helm, new Vector3(6f, 7.5f, 6.4f), turn, Bronze, 12, 8);
            m.Ellipsoid(helm + turn * new Vector3(0f, 1.6f, 5.6f), new Vector3(5f, 0.7f, 1.2f), turn, Hollow);
            for (int k = -2; k <= 2; k++)
                m.Ellipsoid(helm + turn * new Vector3(k * 1.3f, -2.2f, 5.8f), new Vector3(0.4f, 0.9f, 0.6f), turn, Hollow, 5, 3);
            for (int k = 0; k < 6; k++)
            {
                var b = helm + turn * new Vector3(0f, 7f - k * 0.2f, -1f - k * 1.2f);
                m.Limb(b, b + new Vector3(0f, 2.5f + (k % 2) * 1.5f, 0f), 0.7f, 0.05f, Rust, 4);
            }
            // His kite shield, tilted half under the water beside the left leg.
            m.Ellipsoid(new Vector3(-20f, Ground + 0.5f, -20f), new Vector3(7f, 0.9f, 10f), Quaternion.Euler(0f, 18f, 32f), Bronze, 10, 4);
            m.Ellipsoid(new Vector3(-19.5f, Ground + 1.2f, -20f), new Vector3(1.6f, 0.6f, 6f), Quaternion.Euler(0f, 18f, 32f), Rust, 6, 3);
            var mesh = m.ToMesh("DrownedKnight");
            Weather(mesh, Algae, 0.5f, 0.9f);
            AddPart("Body", root, mesh);

            // Lily pads round the chest, every fifth in bloom (own mesh so the algae pass leaves them alone).
            var f = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xD20Au);
            for (int k = 0; k < 18; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(13f, 22f);
                var p = new Vector3(Mathf.Cos(a) * r, Ground + 0.15f, 6f + Mathf.Sin(a) * r * 1.3f);
                var size = rng.NextFloat(0.8f, 1.8f);
                f.Ellipsoid(p, new Vector3(size, 0.1f, size), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Lily, 8, 2);
                if (k % 5 == 0)
                    f.Ellipsoid(p + new Vector3(0f, 0.3f, 0f), new Vector3(0.45f, 0.35f, 0.45f), Quaternion.identity, Bloom, 6, 3);
            }
            AddPart("Lilies", root, f.ToMesh("DrownedKnightLilies"));
            return root;
        }
    }
}
