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
    // A-81 row 1: the CrystalSerpent colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsCrystalSerpent = Register(Kind.CrystalSerpent, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildSerpent(at, yaw));

        /// <summary>
        /// A slate-blue serpent about 120 m long in the electric wastes, swimming through the ground in arches, its
        /// head reared 20 m up with the jaw open and a row of crystal spines (cyan and violet) down every arch. Details:
        /// rubble rings where it dives under, crystal shards sprouting from the ground beside those holes, a spine
        /// snapped off and lying in the dust, and two shed scales.
        /// </summary>
        static Transform BuildSerpent(float3 at, float yaw)
        {
            var root = new GameObject("CrystalSerpent").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 2f, at.z), Quaternion.Euler(0f, yaw, 0f));
            const float Ground = 2f;
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x5E2Bu);
            const int N = 44;
            Vector3 Path(float s) => new Vector3(14f * Mathf.Sin(s * Mathf.PI * 1.8f), Ground - 1.5f + 11f * Mathf.Sin(s * Mathf.PI * 3.5f), -60f + 110f * s);
            float Radius(float s) => Mathf.Lerp(1.2f, 4.6f, Mathf.SmoothStep(0f, 1f, s * 1.6f));
            for (int i = 0; i < N; i++)
            {
                float s0 = i / (float)N, s1 = (i + 1f) / N;
                Vector3 a = Path(s0), b = Path(s1);
                m.Limb(a, b, Radius(s0), Radius(s1), i / 3 % 2 == 0 ? SlateBlue : SlateDark, 8);
                // Rubble ring where the body enters or leaves the ground.
                if ((a.y - Ground) * (b.y - Ground) < 0f)
                {
                    var hole = Vector3.Lerp(a, b, (Ground - a.y) / (b.y - a.y));
                    for (int k = 0; k < 9; k++)
                    {
                        var ang = rng.NextFloat(0f, Mathf.PI * 2f);
                        var size = rng.NextFloat(0.8f, 2f);
                        var p = new Vector3(hole.x + Mathf.Cos(ang) * (Radius(s0) + 1.5f), Ground, hole.z + Mathf.Sin(ang) * (Radius(s0) + 1.5f));
                        m.Ellipsoid(p, new Vector3(size * 1.3f, size * 0.6f, size), Quaternion.Euler(rng.NextFloat(-15f, 15f), rng.NextFloat(0f, 360f), 0f), SlateDark, 6, 3);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        var side = new Vector3(rng.NextBool() ? -1f : 1f, 0f, 0f) * (Radius(s0) + rng.NextFloat(3f, 6f));
                        var foot = new Vector3(hole.x, Ground - 0.5f, hole.z + rng.NextFloat(-3f, 3f)) + side;
                        m.Limb(foot, foot + new Vector3(rng.NextFloat(-1.2f, 1.2f), rng.NextFloat(1.5f, 3.5f), rng.NextFloat(-1.2f, 1.2f)), 0.6f, 0.05f, k % 2 == 0 ? Crystal : Violet, 4);
                    }
                }
                // Crystal spines on the back wherever the body is above the ground, longest mid-body.
                if (a.y > Ground + 1f && i % 2 == 0)
                {
                    var up = Vector3.Cross(b - a, Vector3.right).normalized;
                    if (up.y < 0f) up = -up;
                    var len = rng.NextFloat(3f, 7f) * Mathf.Lerp(0.4f, 1f, Radius(s0) / 4.6f);
                    var lean = (a - b).normalized * 0.35f;
                    m.Limb(a + up * Radius(s0) * 0.8f, a + (up + lean).normalized * (Radius(s0) + len), 1f, 0.08f, i / 2 % 2 == 0 ? Crystal : Violet, 4);
                }
            }
            // The reared neck and head, jaw open, glinting crystal eyes and a crest of short spines.
            var neckBase = Path(1f);
            var headPos = neckBase + new Vector3(0f, 18f, 9f);
            m.Limb(neckBase, neckBase + new Vector3(0f, 10f, 1f), 4.6f, 4.2f, SlateBlue, 8);
            m.Limb(neckBase + new Vector3(0f, 10f, 1f), headPos, 4.2f, 3.6f, SlateDark, 8);
            var pitch = Quaternion.Euler(18f, 0f, 0f);
            m.Ellipsoid(headPos + pitch * new Vector3(0f, 1f, 4f), new Vector3(4.4f, 3f, 8f), pitch, SlateBlue);
            var jaw = Quaternion.Euler(44f, 0f, 0f);
            m.Ellipsoid(headPos + jaw * new Vector3(0f, -1.5f, 4.5f), new Vector3(3.8f, 1.4f, 7f), jaw, SlateDark);
            m.Ellipsoid(headPos + pitch * new Vector3(0f, -0.8f, 5f), new Vector3(3f, 1.2f, 5.5f), pitch, Hollow);        // the open mouth
            m.Ellipsoid(headPos + pitch * new Vector3(-3.4f, 2.4f, 4f), new Vector3(1f, 0.8f, 1.3f), pitch, Crystal);
            m.Ellipsoid(headPos + pitch * new Vector3(3.4f, 2.4f, 4f), new Vector3(1f, 0.8f, 1.3f), pitch, Crystal);
            for (int k = 0; k < 2; k++)                                                                    // fangs
                m.Limb(headPos + pitch * new Vector3(k == 0 ? -2.2f : 2.2f, -1.2f, 10f), headPos + pitch * new Vector3(k == 0 ? -2f : 2f, -4.4f, 10.4f), 0.6f, 0.05f, Stalk, 5);
            for (int k = 0; k < 5; k++)
                m.Limb(headPos + pitch * new Vector3(0f, 3f, 2f - k * 2.2f), headPos + pitch * new Vector3(0f, 6.5f - k * 0.5f, -1f - k * 2.4f), 0.9f, 0.06f, k % 2 == 0 ? Violet : Crystal, 4);
            // A snapped spine lying in the dust, and two shed scales.
            m.Limb(new Vector3(-22f, Ground + 0.4f, 6f), new Vector3(-17f, Ground + 1.2f, 10f), 1f, 0.08f, Crystal, 4);
            m.Ellipsoid(new Vector3(20f, Ground + 0.1f, -14f), new Vector3(2.4f, 0.25f, 1.8f), Quaternion.Euler(6f, 40f, 4f), SlateBlue, 6, 2);
            m.Ellipsoid(new Vector3(23f, Ground + 0.1f, -10f), new Vector3(2f, 0.25f, 1.5f), Quaternion.Euler(-5f, 110f, 3f), SlateDark, 6, 2);
            AddPart("Body", root, m.ToMesh("CrystalSerpent"));
            return root;
        }
    }
}
