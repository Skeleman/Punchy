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
    // A-81 row 1: the WalkingSaguaro colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsWalkingSaguaro = Register(Kind.WalkingSaguaro, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildSaguaro(at, yaw, seed));

        static readonly Color Saguaro = new Color(0.34f, 0.47f, 0.28f);
        static readonly Color SaguaroDark = new Color(0.22f, 0.33f, 0.19f);
        static readonly Color SpinePale = new Color(0.88f, 0.84f, 0.68f);
        static readonly Color Blossom = new Color(0.96f, 0.95f, 0.88f);
        static readonly Color Pollen = new Color(0.96f, 0.80f, 0.28f);
        static readonly Color Fruit = new Color(0.74f, 0.20f, 0.24f);
        static readonly Color Dirt = new Color(0.50f, 0.39f, 0.28f);

        /// <summary>
        /// W-89 (#40, Zach's Sonoran design): a saguaro about 50 m tall walking across the desert on its roots, mid-stride:
        /// a ribbed, spined trunk leaning into the step on three root legs (one lifted, dripping soil), and two to four
        /// arms (one may droop, as old saguaros' do). Seeded per world: height, lean, the arms and which one droops.
        /// Details: white blossoms and red fruit on the crown and arm tips, woodpecker holes in the trunk, a hawk perched
        /// on an arm, footprint holes in a line behind it, and dust kicked up round the feet.
        /// </summary>
        static Transform BuildSaguaro(float3 at, float yaw, uint seed)
        {
            var root = new GameObject("WalkingSaguaro").transform;
            root.SetPositionAndRotation(new Vector3(at.x, at.y - 0.6f, at.z), Quaternion.Euler(0f, yaw, 0f));
            var m = new DunesColossus.MeshBuilder();
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var height = rng.NextFloat(44f, 54f);
            var lean = rng.NextFloat(4f, 9f);
            const float R = 4.6f;
            var trunkBase = new Vector3(0f, 9f, 0f);
            Vector3 Trunk(float t) => trunkBase + new Vector3(0f, (height - trunkBase.y) * t, lean * t * t);

            // A ribbed column with pale spines along every rib.
            void Ribbed(System.Func<float, Vector3> path, float radius, int steps, int ribs)
            {
                for (int s = 0; s < steps; s++)
                    m.Limb(path(s / (float)steps), path((s + 1f) / steps), radius * 0.9f, radius * 0.9f, SaguaroDark, ribs);
                for (int k = 0; k < ribs; k++)
                {
                    var a = k * Mathf.PI * 2f / ribs;
                    for (int s = 0; s < steps; s++)
                    {
                        Vector3 p0 = path(s / (float)steps), p1 = path((s + 1f) / steps);
                        var along = (p1 - p0).normalized;
                        var side = Vector3.Cross(along, Vector3.right).sqrMagnitude > 0.1f ? Vector3.Cross(along, Vector3.right).normalized : Vector3.forward;
                        var out0 = Quaternion.AngleAxis(a * Mathf.Rad2Deg, along) * side;
                        m.Limb(p0 + out0 * radius * 0.85f, p1 + out0 * radius * 0.85f, radius * 0.22f, radius * 0.22f, Saguaro, 4);
                        if ((s + k) % 2 == 0)
                        {
                            var sp = Vector3.Lerp(p0, p1, 0.5f) + out0 * radius * 1.05f;
                            m.Limb(sp, sp + out0 * radius * 0.16f + along * 0.1f, 0.07f, 0.01f, SpinePale, 3);
                        }
                    }
                }
                var top = path(1f);
                m.Ellipsoid(top, new Vector3(radius, radius * 0.8f, radius), Quaternion.identity, Saguaro, 12, 5);
            }
            void Bloom(Vector3 top, float radius, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    var a = rng.NextFloat(0f, Mathf.PI * 2f);
                    var p = top + new Vector3(Mathf.Cos(a) * radius * 0.55f, radius * 0.6f, Mathf.Sin(a) * radius * 0.55f);
                    if (k % 3 == 2)
                        m.Ellipsoid(p, new Vector3(0.35f, 0.5f, 0.35f) * radius * 0.3f, Quaternion.identity, Fruit, 6, 3);
                    else
                    {
                        m.Ellipsoid(p, new Vector3(0.6f, 0.2f, 0.6f) * radius * 0.3f, Quaternion.identity, Blossom, 8, 3);
                        m.Ellipsoid(p + Vector3.up * 0.1f, new Vector3(0.2f, 0.12f, 0.2f) * radius * 0.3f, Quaternion.identity, Pollen, 5, 2);
                    }
                }
            }
            Ribbed(Trunk, R, 10, 14);
            Bloom(Trunk(1f), R, 9);
            // Arms: out from the trunk, a rounded elbow, then up (or, for the drooping one, down and out).
            var arms = rng.NextInt(2, 5);
            var droop = rng.NextInt(0, arms + 1);                                                           // arms means none droops
            Vector3 perch = default;
            for (int k = 0; k < arms; k++)
            {
                var a = (k * 360f / arms + rng.NextFloat(-25f, 25f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var t = rng.NextFloat(0.35f, 0.7f);
                var from = Trunk(t);
                var out1 = from + d * rng.NextFloat(8f, 11f) + Vector3.up * 1.5f;
                var r = R * rng.NextFloat(0.55f, 0.7f);
                var rise = k == droop ? -rng.NextFloat(6f, 10f) : rng.NextFloat(8f, 16f);
                var tip = out1 + Vector3.up * rise + d * (k == droop ? 3f : 0.5f);
                Ribbed(s => Vector3.Lerp(from, out1, Mathf.Min(1f, s * 2f)) + (s > 0.5f ? (tip - out1) * (s - 0.5f) * 2f : Vector3.zero), r, 6, 10);
                m.Ellipsoid(out1, Vector3.one * r * 0.95f, Quaternion.identity, Saguaro, 10, 5);
                if (k != droop)
                    Bloom(tip, r, 4);
                if (k == 0)
                    perch = tip + Vector3.up * r * 0.8f;
            }
            // A hawk perched on the first arm, looking out.
            m.Ellipsoid(perch + Vector3.up * 0.5f, new Vector3(0.35f, 0.55f, 0.4f), Quaternion.Euler(-15f, 0f, 0f), new Color(0.32f, 0.24f, 0.18f), 6, 4);
            m.Ellipsoid(perch + new Vector3(0f, 1.1f, 0.1f), new Vector3(0.22f, 0.22f, 0.24f), Quaternion.identity, new Color(0.85f, 0.80f, 0.72f), 6, 3);
            // Woodpecker holes, with darker rims.
            for (int k = 0; k < 4; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var p = Trunk(rng.NextFloat(0.2f, 0.8f)) + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R * 1.02f;
                m.Ellipsoid(p, new Vector3(0.9f, 1.1f, 0.9f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), SaguaroDark, 6, 3);
                m.Ellipsoid(p + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.3f, new Vector3(0.55f, 0.75f, 0.55f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), Hollow, 6, 3);
            }
            // A-77 (item 23, Zach): the legs read as a root system. A flared bole under the trunk, then three bundles of
            // gnarled roots: two planted wide and sinking into the sand, the third trailing behind mid-step, soil hanging off.
            m.Ellipsoid(trunkBase + Vector3.up * 0.5f, new Vector3(R * 1.45f, 2.6f, R * 1.45f), Quaternion.identity, SaguaroDark, 14, 5);
            for (int k = 0; k < 3; k++)
            {
                var a = (k * 120f + 180f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var side = new Vector3(d.z, 0f, -d.x);
                var lifted = k == 0;
                var foot = d * (lifted ? 11f : 12f) + Vector3.up * (lifted ? rng.NextFloat(4f, 6f) : 0.5f);
                for (int s = 0; s < 4; s++)
                {
                    var off = s - 1.5f;
                    var from = trunkBase + d * 2.4f + side * off * 1.3f + Vector3.down * rng.NextFloat(0f, 1.5f);
                    var end = foot + side * off * 1.8f + d * rng.NextFloat(-1f, 1.5f) + Vector3.down * (lifted ? 0f : 1f);
                    var kink = Vector3.Lerp(from, end, 0.45f) + side * rng.NextFloat(-1.6f, 1.6f) + Vector3.up * rng.NextFloat(1f, 3f);
                    Tentacle(m, from, kink + d * 1.5f, end + Vector3.up * (lifted ? 2.5f : 4f) - d * 1.5f, end, R * 0.3f, 0.3f, SaguaroDark, Saguaro, 9);
                    var fork = Vector3.Lerp(kink, end, 0.4f);
                    m.Limb(fork, fork + side * (off < 0f ? -1f : 1f) * rng.NextFloat(1.5f, 3f) + d * rng.NextFloat(1f, 2f) + Vector3.down * (lifted ? 0.6f : 2f),
                           0.55f, 0.12f, SaguaroDark, 5);
                }
                for (int r = 0; r < 5; r++)
                {
                    var ra = Mathf.Atan2(d.x, d.z) + (r - 2f) * 0.45f;
                    var to = foot + new Vector3(Mathf.Sin(ra) * rng.NextFloat(3f, 5f), lifted ? -rng.NextFloat(1f, 2.5f) : -0.4f, Mathf.Cos(ra) * rng.NextFloat(3f, 5f));
                    m.Limb(foot, to, 0.6f, 0.1f, Dirt, 5);
                    if (lifted)
                        m.Ellipsoid(to + Vector3.down * 0.3f, Vector3.one * rng.NextFloat(0.4f, 0.8f), Quaternion.identity, Dirt, 5, 3);   // clods of soil
                }
                if (!lifted)
                    m.Ellipsoid(foot + Vector3.down * 0.2f, new Vector3(5f, 0.6f, 5f), Quaternion.identity, Dirt, 10, 3);
            }
            // Footprints behind it: holes torn out of the ground, a line back the way it came.
            for (int k = 1; k <= 5; k++)
            {
                var p = new Vector3((k % 2 == 0 ? -4f : 4f) + rng.NextFloat(-1f, 1f), 0.25f, -10f - k * 11f);
                m.Ellipsoid(p, new Vector3(4.5f, 0.5f, 4f), Quaternion.Euler(0f, rng.NextFloat(0f, 360f), 0f), Dirt, 10, 3);
                m.Ellipsoid(p + Vector3.up * 0.15f, new Vector3(3f, 0.4f, 2.6f), Quaternion.identity, new Color(0.30f, 0.23f, 0.17f), 8, 3);
            }
            AddPart("Body", root, m.ToMesh("WalkingSaguaro"));
            Puffs(root, "Dust", new Vector3(0f, 1.5f, 2f), new Vector3(28f, 1f, 24f), new Color(0.66f, 0.55f, 0.42f, 0.20f), new Color(0.74f, 0.64f, 0.50f, 0.28f), 4f, 10f, 4f, 0.5f, 10f);
            return root;
        }
    }
}
