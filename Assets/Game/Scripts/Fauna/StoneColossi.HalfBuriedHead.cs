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
    // A-81 row 1: the HalfBuriedHead colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsHalfBuriedHead = Register(Kind.HalfBuriedHead, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildHead(at, yaw));

        /// <summary>
        /// A head about 30 m tall sunk to the lips, tilted over, a broken crown of uneven spikes, one fallen spike and a
        /// ring of rubble round it; moss on the brow and crown.
        /// </summary>
        static Transform BuildHead(float3 at, float yaw)
        {
            var root = new GameObject("HalfBuriedHead").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var tilt = Quaternion.Euler(-12f, 0f, 24f);
            var m = new DunesColossus.MeshBuilder();
            var c = new Vector3(0f, 6f, 0f);
            m.Ellipsoid(c, new Vector3(12f, 15f, 13f), tilt, Stone, 14, 9);
            m.Ellipsoid(c + tilt * new Vector3(0f, 5f, 11f), new Vector3(10f, 2.4f, 3.6f), tilt, Dark);               // brow
            m.Ellipsoid(c + tilt * new Vector3(-4.4f, 2.4f, 11.6f), new Vector3(2.6f, 1.5f, 1.3f), tilt, Hollow);      // eyes
            m.Ellipsoid(c + tilt * new Vector3(4.4f, 2.4f, 11.6f), new Vector3(2.6f, 1.5f, 1.3f), tilt, Hollow);
            m.Ellipsoid(c + tilt * new Vector3(0f, -2.5f, 13.4f), new Vector3(2.3f, 4.6f, 3f), tilt, Stone);           // nose
            m.Ellipsoid(c + tilt * new Vector3(-12f, 0f, 1f), new Vector3(1.6f, 4.4f, 2.6f), tilt, Dark);              // ears
            m.Ellipsoid(c + tilt * new Vector3(12f, 0f, 1f), new Vector3(1.6f, 4.4f, 2.6f), tilt, Dark);
            m.Ellipsoid(c + tilt * new Vector3(0f, -8.5f, 11f), new Vector3(5f, 1f, 1.4f), tilt, Hollow);              // lips at the ground
            // Crown band and spikes, three of them snapped short.
            m.Ellipsoid(c + tilt * new Vector3(0f, 12.5f, 0f), new Vector3(11f, 2f, 11.5f), tilt, Rust, 14, 4);
            for (int k = 0; k < 9; k++)
            {
                var a = k * Mathf.PI * 2f / 9f;
                var len = k % 3 == 1 ? 2f : 6.5f;
                var foot = new Vector3(Mathf.Cos(a) * 10f, 13.5f, Mathf.Sin(a) * 10.5f);
                m.Limb(c + tilt * foot, c + tilt * (foot + new Vector3(Mathf.Cos(a) * 1.2f, len, Mathf.Sin(a) * 1.2f)), 1.6f, k % 3 == 1 ? 1.3f : 0.35f, Rust, 5);
            }
            m.Limb(new Vector3(17f, 0.8f, 6f), new Vector3(21f, 1.6f, 11f), 1.5f, 0.35f, Rust, 5);                     // a snapped spike on the ground
            // Rubble ring: broken stones half sunk round the head.
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x4EADu);
            for (int k = 0; k < 26; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = rng.NextFloat(15f, 24f);
                var size = rng.NextFloat(0.8f, 2.6f);
                m.Ellipsoid(new Vector3(Mathf.Cos(a) * r, size * 0.3f, Mathf.Sin(a) * r), new Vector3(size * 1.3f, size * 0.7f, size), Quaternion.Euler(rng.NextFloat(-20f, 20f), rng.NextFloat(0f, 360f), 0f), rng.NextBool() ? Stone : Dark, 6, 4);
            }
            var mesh = m.ToMesh("HalfBuriedHead");
            Weather(mesh, Moss, 0.35f, 0.85f);
            AddPart("Head", root, mesh);
            return root;
        }
    }
}
