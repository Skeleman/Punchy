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
    // A-81 row 1: the GiantTortoise colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsGiantTortoise = Register(Kind.GiantTortoise, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildTortoise(at, yaw));

        static readonly Color CapRed = new Color(0.72f, 0.24f, 0.16f);
        static readonly Color CapPale = new Color(0.86f, 0.74f, 0.52f);
        static readonly Color Stalk = new Color(0.88f, 0.85f, 0.76f);
        static readonly Color SlateBlue = new Color(0.40f, 0.43f, 0.50f);
        static readonly Color SlateDark = new Color(0.26f, 0.28f, 0.34f);
        static readonly Color Crystal = new Color(0.45f, 0.85f, 0.95f);
        static readonly Color Violet = new Color(0.62f, 0.45f, 0.92f);

        /// <summary>
        /// A stone tortoise about 50 m long in the fungal forest, head stretched out low as if it stopped mid-step:
        /// a domed shell of raised plates with moss on everything facing up, and growing from the moss a crop of
        /// mushrooms with one huge toadstool on the crest. Details: a clutch of three stone eggs half sunk by a front
        /// foot, a chipped plate lying on the ground, and a little mushroom ring on its snout.
        /// </summary>
        static Transform BuildTortoise(float3 at, float yaw)
        {
            var root = new GameObject("GiantTortoise").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 1.5f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var c = new Vector3(0f, 7f, 0f);
            var shell = new Vector3(19f, 15f, 23f);
            m.Dome(c, shell, Quaternion.identity, Stone, false, 16, 5);
            m.Ellipsoid(c, new Vector3(20.5f, 2.6f, 24.5f), Quaternion.identity, Dark, 16, 4);              // shell rim
            // Raised plates on the dome, in rings, each turned to the dome's surface.
            for (int ring = 0; ring < 3; ring++)
            {
                var theta = 0.28f + ring * 0.42f;                       // from the crest down
                var count = ring == 0 ? 1 : ring * 6 + 1;
                for (int k = 0; k < count; k++)
                {
                    var phi = k * Mathf.PI * 2f / count + ring * 0.4f;
                    var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    if (ring == 0) d = Vector3.up;
                    var p = c + Vector3.Scale(d, shell);
                    var n = new Vector3(d.x / shell.x, d.y / shell.y, d.z / shell.z).normalized;
                    m.Ellipsoid(p, new Vector3(4.6f, 1.3f, 4.6f), Quaternion.FromToRotation(Vector3.up, n), (ring + k) % 2 == 0 ? Dark : Stone, 6, 3);
                }
            }
            // Four pillar legs with blunt toes, the front left one lifted a little (mid-step).
            for (int k = 0; k < 4; k++)
            {
                float sx = k % 2 == 0 ? -1f : 1f, sz = k < 2 ? 1f : -1f;
                var lift = k == 0 ? 2.5f : 0f;
                var hip = new Vector3(sx * 14f, 7f, sz * 14f);
                var foot = new Vector3(sx * 18f, 1.2f + lift, sz * 18f);
                m.Limb(hip, foot, 5.5f, 4.6f, Stone);
                m.Ellipsoid(foot + new Vector3(0f, -0.4f, sz * 1.6f), new Vector3(5f, 2f, 5.5f), Quaternion.identity, Dark, 8, 4);
                for (int t = -1; t <= 1; t++)
                    m.Ellipsoid(foot + new Vector3(t * 2.4f, -0.6f, sz * 5.6f), new Vector3(1.1f, 0.9f, 1f), Quaternion.identity, Stone, 5, 3);
            }
            // Neck reaching forward and down, the head turned a little, a hooked beak and deep eyes.
            m.Limb(new Vector3(0f, 8f, 22f), new Vector3(2f, 7f, 32f), 5.4f, 4.2f, Dark);
            var look = Quaternion.Euler(10f, 14f, 0f);
            var head = new Vector3(3f, 7.5f, 36f);
            m.Ellipsoid(head, new Vector3(5f, 4.2f, 6.6f), look, Stone);
            m.Ellipsoid(head + look * new Vector3(0f, -1.2f, 5.6f), new Vector3(2.6f, 2.2f, 2f), look, Dark);            // beak
            m.Ellipsoid(head + look * new Vector3(-3.4f, 1.2f, 3f), new Vector3(1f, 1f, 1f), look, Hollow);
            m.Ellipsoid(head + look * new Vector3(3.4f, 1.2f, 3f), new Vector3(1f, 1f, 1f), look, Hollow);
            m.Limb(new Vector3(0f, 5f, -23f), new Vector3(-2f, 1.5f, -30f), 3f, 0.6f, Dark);               // stubby tail
            // A chipped plate on the ground beside it, and three stone eggs half sunk by the front left foot.
            m.Ellipsoid(new Vector3(26f, 1.5f, -6f), new Vector3(4.2f, 1.1f, 4.4f), Quaternion.Euler(14f, 30f, 22f), Dark, 6, 3);
            m.Ellipsoid(new Vector3(-27f, 1.6f, 22f), new Vector3(1.6f, 2f, 1.6f), Quaternion.Euler(10f, 0f, 8f), Stalk, 8, 5);
            m.Ellipsoid(new Vector3(-29.5f, 1.4f, 20.5f), new Vector3(1.5f, 1.9f, 1.5f), Quaternion.Euler(-8f, 0f, -14f), Stalk, 8, 5);
            m.Ellipsoid(new Vector3(-28f, 1.3f, 19f), new Vector3(1.5f, 1.8f, 1.5f), Quaternion.Euler(4f, 0f, 20f), Stalk, 8, 5);
            var mesh = m.ToMesh("GiantTortoise");
            Weather(mesh, Moss, 0.35f, 0.85f);
            AddPart("Body", root, mesh);

            // The mushrooms go in their own mesh so the moss pass doesn't colour their caps.
            var f = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(0x7027u);
            void Mushroom(Vector3 basePos, float height, float cap, Color color)
            {
                f.Limb(basePos, basePos + Vector3.up * height, cap * 0.22f, cap * 0.16f, Stalk, 6);
                f.Dome(basePos + Vector3.up * height, new Vector3(cap, cap * 0.55f, cap), Quaternion.identity, color, false, 10, 3);
                f.Ellipsoid(basePos + Vector3.up * (height - 0.05f), new Vector3(cap * 0.95f, cap * 0.08f, cap * 0.95f), Quaternion.identity, Stalk, 10, 2);  // gills
            }
            Mushroom(c + new Vector3(1.5f, shell.y - 0.6f, -2f), 7f, 5.5f, CapRed);                    // the toadstool on the crest
            for (int k = 0; k < 7; k++)
            {
                f.Ellipsoid(c + new Vector3(1.5f, shell.y + 6.4f, -2f) + Quaternion.Euler(0f, k * 51f, 0f) * new Vector3(3.2f, 0.2f, 0f), new Vector3(0.6f, 0.25f, 0.6f), Quaternion.identity, Stalk, 6, 2); // spots
            }
            for (int k = 0; k < 14; k++)
            {
                var theta = rng.NextFloat(0.35f, 1.15f);
                var phi = rng.NextFloat(0f, Mathf.PI * 2f);
                var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                var cluster = c + Vector3.Scale(d, shell) - Vector3.up * 0.4f;
                for (int j = rng.NextInt(1, 4); j > 0; j--)
                    Mushroom(cluster + new Vector3(rng.NextFloat(-1.5f, 1.5f), 0f, rng.NextFloat(-1.5f, 1.5f)), rng.NextFloat(0.8f, 2.4f), rng.NextFloat(0.6f, 1.6f), rng.NextBool() ? CapRed : CapPale);
            }
            for (int k = 0; k < 6; k++)                                                                   // a fairy ring on its snout
                Mushroom(head + look * new Vector3(Mathf.Cos(k * 1.05f) * 2.2f, 3.8f, Mathf.Sin(k * 1.05f) * 2.2f - 0.5f), 0.45f, 0.35f, CapPale);
            AddPart("Mushrooms", root, f.ToMesh("GiantTortoiseMushrooms"));
            return root;
        }
    }
}
