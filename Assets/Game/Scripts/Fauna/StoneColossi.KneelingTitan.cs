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
    // A-81 row 1: the KneelingTitan colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsKneelingTitan = Register(Kind.KneelingTitan, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildTitan(at, yaw));

        /// <summary>
        /// About 50 m kneeling: the left knee down, the right leg up with the right forearm resting on its knee, the left
        /// hand on the pommel of a sword planted point-down in front, the head bowed towards it. Sunk a couple of metres so
        /// slopes don't show daylight under it.
        /// </summary>
        static Transform BuildTitan(float3 at, float yaw)
        {
            var root = new GameObject("KneelingTitan").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            // Legs: left shin flat on the ground behind, right thigh forward and shin upright.
            m.Limb(new Vector3(-7f, 16f, -2f), new Vector3(-8f, 3f, 6f), 5.2f, 4.4f, Stone);      // left thigh down to the knee
            m.Limb(new Vector3(-8f, 3f, 6f), new Vector3(-8f, 2.5f, -12f), 4.2f, 3.2f, Dark);     // left shin back along the ground
            m.Ellipsoid(new Vector3(-8f, 2f, -15f), new Vector3(3f, 2.2f, 5f), Quaternion.Euler(0f, 0f, 0f), Stone);
            m.Limb(new Vector3(7f, 16f, -1f), new Vector3(8f, 18f, 14f), 5.2f, 4.4f, Stone);      // right thigh forward
            m.Limb(new Vector3(8f, 18f, 14f), new Vector3(8f, 2f, 15f), 4.2f, 3.4f, Dark);        // right shin down
            m.Ellipsoid(new Vector3(8f, 1.5f, 18f), new Vector3(3.2f, 2f, 5.5f), Quaternion.identity, Stone);
            // Hips, torso leaning forward, shoulders.
            var lean = Quaternion.Euler(16f, 0f, 0f);
            m.Ellipsoid(new Vector3(0f, 17f, -1f), new Vector3(10f, 6f, 7f), Quaternion.identity, Dark);
            m.Ellipsoid(new Vector3(0f, 27f, 1f), new Vector3(10.5f, 12f, 7.5f), lean, Stone);
            m.Ellipsoid(new Vector3(0f, 36f, 4f), new Vector3(15f, 4.5f, 7f), lean, Dark);
            m.Ellipsoid(new Vector3(0f, 26f, 7f), new Vector3(7f, 6f, 2.5f), lean, Dark);           // breastplate ridge
            // Right arm on the raised knee.
            m.Limb(new Vector3(13f, 36f, 4f), new Vector3(14f, 25f, 11f), 3.8f, 3.2f, Stone);
            m.Limb(new Vector3(14f, 25f, 11f), new Vector3(9f, 20f, 19f), 3.2f, 2.8f, Stone);
            m.Ellipsoid(new Vector3(8f, 19f, 21f), new Vector3(3f, 2f, 3.4f), Quaternion.identity, Dark);
            // Left arm down to the pommel.
            m.Limb(new Vector3(-13f, 36f, 4f), new Vector3(-12f, 26f, 12f), 3.8f, 3.2f, Stone);
            m.Limb(new Vector3(-12f, 26f, 12f), new Vector3(-3f, 24f, 22f), 3.2f, 2.8f, Stone);
            m.Ellipsoid(new Vector3(-1.5f, 24f, 23.5f), new Vector3(3f, 3f, 3f), Quaternion.identity, Dark);
            // The sword, point sunk in the ground: pommel, grip, crossguard, a long blade.
            m.Ellipsoid(new Vector3(0f, 27.5f, 24f), new Vector3(1.4f, 1.4f, 1.4f), Quaternion.identity, Rust);
            m.Limb(new Vector3(0f, 27f, 24f), new Vector3(0f, 21f, 24f), 0.8f, 0.8f, Rust, 6);
            m.Ellipsoid(new Vector3(0f, 20.5f, 24f), new Vector3(6f, 0.9f, 1.2f), Quaternion.identity, Rust);
            m.Ellipsoid(new Vector3(0f, 9f, 24f), new Vector3(1.6f, 11.5f, 0.45f), Quaternion.identity, Dark, 6, 8);
            // Neck and bowed head with a brow band and hollow eyes looking down at the blade.
            m.Limb(new Vector3(0f, 38f, 5f), new Vector3(0f, 41f, 10f), 3.4f, 3f, Stone);
            var bow = Quaternion.Euler(38f, 0f, 0f);
            var head = new Vector3(0f, 43.5f, 12.5f);
            m.Ellipsoid(head, new Vector3(4.6f, 5.8f, 5.2f), bow, Stone);
            m.Ellipsoid(head + bow * new Vector3(0f, 2.2f, 3.6f), new Vector3(4.4f, 1.2f, 2f), bow, Dark);
            m.Ellipsoid(head + bow * new Vector3(-1.8f, 0.8f, 4.6f), new Vector3(1f, 0.6f, 0.6f), bow, Hollow);
            m.Ellipsoid(head + bow * new Vector3(1.8f, 0.8f, 4.6f), new Vector3(1f, 0.6f, 0.6f), bow, Hollow);
            m.Ellipsoid(head + bow * new Vector3(0f, -3.6f, 3.4f), new Vector3(2.8f, 2.4f, 2f), bow, Stone);   // bearded chin
            var mesh = m.ToMesh("KneelingTitan");
            Weather(mesh, Snow, 0.55f, 0.95f);
            AddPart("Body", root, mesh);
            return root;
        }
    }
}
