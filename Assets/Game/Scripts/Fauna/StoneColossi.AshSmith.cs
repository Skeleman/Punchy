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
    // A-81 row 1: the AshSmith colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsAshSmith = Register(Kind.AshSmith, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildSmith(at, yaw));

        static readonly Color Basalt = new Color(0.17f, 0.16f, 0.16f);
        static readonly Color Soot = new Color(0.10f, 0.09f, 0.09f);
        static readonly Color Ember = new Color(1.00f, 0.42f, 0.10f);
        static readonly Color Ash = new Color(0.42f, 0.40f, 0.39f);

        /// <summary>
        /// W-86: a basalt smith about 34 m tall sitting cross-legged in the ash, hunched over an anvil as big as a house,
        /// one hand on the hammer laid across it, the other holding tongs on a half-made blade still glowing on the anvil
        /// face. Ember cracks run down his chest and the anvil's waist; ash lies on everything turned up.
        /// Details: a seeded scatter of cooled slag lumps round the anvil (every fourth still glowing) and a quench trough.
        /// </summary>
        static Transform BuildSmith(float3 at, float yaw)
        {
            var root = new GameObject("AshSmith").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            // Crossed legs on the ground, knees out to the sides, feet tucked in front.
            m.Ellipsoid(new Vector3(0f, 6f, -4f), new Vector3(10f, 5.5f, 7f), Quaternion.identity, Soot);
            for (int s = -1; s <= 1; s += 2)
            {
                var knee = new Vector3(s * 13f, 4f, 4f);
                m.Limb(new Vector3(s * 5f, 5f, -4f), knee, 4.6f, 4f, Basalt);
                m.Limb(knee, new Vector3(-s * 4f, 2.5f, 9f), 3.8f, 3f, Basalt);
                m.Ellipsoid(new Vector3(-s * 5.5f, 2f, 10f), new Vector3(3f, 2f, 4f), Quaternion.Euler(0f, s * 50f, 0f), Soot);
            }
            // Torso hunched forward over the anvil, a heavy leather apron, broad shoulders.
            var hunch = Quaternion.Euler(28f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 16f, -1f), new Vector3(9.5f, 11f, 7f), hunch, Basalt);
            m.Ellipsoid(new Vector3(0f, 12f, 4.5f), new Vector3(7.5f, 7f, 1.6f), Quaternion.Euler(14f, 0f, 0f), Rust);
            m.Ellipsoid(new Vector3(0f, 24f, 3f), new Vector3(14f, 4.5f, 6.5f), hunch, Soot);
            // Ember cracks down the chest.
            for (int k = 0; k < 5; k++)
                m.Ellipsoid(new Vector3(-3f + k * 1.5f, 21f - k * 1.8f, 5.6f + k * 0.3f), new Vector3(0.35f, 2.2f, 0.4f),
                            Quaternion.Euler(28f, 0f, k % 2 == 0 ? 25f : -20f), Ember, 5, 4);
            // The anvil in front of him: foot, waist, a broad face and a horn.
            var anvil = new Vector3(0f, 0f, 20f);
            m.Ellipsoid(anvil + new Vector3(0f, 2f, 0f), new Vector3(7f, 2.6f, 5f), Quaternion.identity, Soot, 8, 4);
            m.Limb(anvil + new Vector3(0f, 3f, 0f), anvil + new Vector3(0f, 8.5f, 0f), 3.4f, 2.6f, Basalt, 8);
            m.Ellipsoid(anvil + new Vector3(0f, 9.5f, 0f), new Vector3(8.5f, 1.8f, 4f), Quaternion.identity, Basalt, 10, 4);
            m.Limb(anvil + new Vector3(8f, 9.5f, 0f), anvil + new Vector3(14f, 9.2f, 0f), 1.9f, 0.1f, Basalt, 8);
            m.Ellipsoid(anvil + new Vector3(0f, 6f, 2.7f), new Vector3(1.6f, 0.3f, 0.3f), Quaternion.Euler(0f, 0f, 12f), Ember, 5, 3);
            // The half-made blade glowing on the face.
            m.Ellipsoid(anvil + new Vector3(-1f, 11.6f, 0.5f), new Vector3(6f, 0.35f, 0.9f), Quaternion.Euler(0f, 8f, 0f), Ember, 8, 3);
            // Left arm down to the tongs gripping it.
            var lhand = new Vector3(-8f, 13f, 14f);
            m.Limb(new Vector3(-12.5f, 24f, 3f), new Vector3(-14f, 16f, 8f), 3.6f, 3f, Basalt);
            m.Limb(new Vector3(-14f, 16f, 8f), lhand, 3f, 2.6f, Basalt);
            m.Ellipsoid(lhand, new Vector3(2.6f, 2.2f, 2.8f), Quaternion.identity, Soot);
            m.Limb(lhand + new Vector3(0.5f, -0.5f, 1.5f), anvil + new Vector3(-5f, 11.8f, 0.5f), 0.45f, 0.35f, Rust, 4);
            m.Limb(lhand + new Vector3(-0.5f, 0.3f, 1.5f), anvil + new Vector3(-5f, 12.3f, 0.5f), 0.45f, 0.35f, Rust, 4);
            // Right arm resting on the hammer laid across the anvil.
            var rhand = new Vector3(9f, 12.5f, 16f);
            m.Limb(new Vector3(12.5f, 24f, 3f), new Vector3(15f, 16f, 8f), 3.6f, 3f, Basalt);
            m.Limb(new Vector3(15f, 16f, 8f), rhand, 3f, 2.6f, Basalt);
            m.Ellipsoid(rhand, new Vector3(2.6f, 2.2f, 2.8f), Quaternion.identity, Soot);
            m.Limb(rhand, anvil + new Vector3(3f, 11.8f, -1f), 0.8f, 0.8f, Rust, 6);
            m.Ellipsoid(anvil + new Vector3(2.5f, 12.4f, -1.2f), new Vector3(1.8f, 1.6f, 3.2f), Quaternion.Euler(0f, 30f, 0f), Dark, 8, 4);
            // Neck and head bowed over the work: a heavy brow, eyes lit from the blade, a braided beard.
            var bow = Quaternion.Euler(44f, 0f, 0f);
            var head = new Vector3(0f, 28.5f, 9f);
            m.Limb(new Vector3(0f, 25f, 3f), head, 3.4f, 3f, Basalt);
            m.Ellipsoid(head, new Vector3(4.4f, 5.4f, 4.8f), bow, Basalt);
            m.Ellipsoid(head + bow * new Vector3(0f, 2f, 3.6f), new Vector3(4.2f, 1.1f, 1.8f), bow, Soot);
            m.Ellipsoid(head + bow * new Vector3(-1.7f, 0.7f, 4.4f), new Vector3(0.9f, 0.5f, 0.5f), bow, Ember);
            m.Ellipsoid(head + bow * new Vector3(1.7f, 0.7f, 4.4f), new Vector3(0.9f, 0.5f, 0.5f), bow, Ember);
            m.Limb(head + bow * new Vector3(0f, -3f, 3.4f), head + bow * new Vector3(0f, -9f, 3.8f), 2.6f, 0.8f, Soot, 6);
            // A quench trough beside the anvil.
            m.Ellipsoid(new Vector3(-17f, 1.2f, 18f), new Vector3(3f, 1.6f, 6f), Quaternion.Euler(0f, 10f, 0f), Soot, 8, 4);
            m.Ellipsoid(new Vector3(-17f, 2.6f, 18f), new Vector3(2.2f, 0.2f, 5f), Quaternion.Euler(0f, 10f, 0f), Hollow, 8, 2);
            // Cooled slag lumps round the anvil, every fourth still glowing.
            var rng = Unity.Mathematics.Random.CreateFromIndex(0xA54Du);
            for (int k = 0; k < 14; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(10f, 19f);
                var size = rng.NextFloat(0.8f, 2.2f);
                m.Ellipsoid(anvil + new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r * 0.8f), new Vector3(size, size * 0.6f, size * 1.2f),
                            Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), k % 4 == 0 ? Ember : Soot, 6, 3);
            }
            var mesh = m.ToMesh("AshSmith");
            Weather(mesh, Ash, 0.82f, 1f);
            AddPart("Body", root, mesh);
            return root;
        }
    }
}
