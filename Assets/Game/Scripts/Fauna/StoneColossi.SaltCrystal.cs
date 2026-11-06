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
    // A-81 row 1: the SaltCrystal colossus's builder, moved out of StoneColossi.cs as it was. Its ColossusDefinition
    // asset (Resources/Colossi) says where it stands; the line below hands StoneColossi this builder for it.
    public partial class StoneColossi
    {
        static readonly bool buildsSaltCrystal = Register(Kind.SaltCrystal, (in TerrainSource source, float3 at, float yaw, uint seed) => BuildCrystal(source, at, yaw, seed));

        /// <summary>W-92 (#40): the salt crystal's radius (m) and the most its rolling circle may span. Its definition's
        /// keep radius (78 m) is the circle plus the ball plus 5 m, its collision ball 1.1 times the radius.</summary>
        const float CrystalBall = 13f, CrystalCircleMax = 60f;

        static readonly Color SaltWhite =new Color(0.93f, 0.91f, 0.88f);
        static readonly Color SaltPink = new Color(0.90f, 0.78f, 0.76f);
        static readonly Color SaltDirt = new Color(0.42f, 0.34f, 0.26f);

        /// <summary>
        /// W-92 (#40, Zach's SaltFlat design): a giant rounded salt crystal, faceted and studded with short points,
        /// rolling slowly round a seeded circle about the spot and dragging a fading dirt trail behind it
        /// (<see cref="SaltCrystalRoll"/>). The root sits at the circle's centre. Details: dirt smeared on the ball where
        /// it has rolled through mud, and salt crumbs it has shed lying near the track.
        /// </summary>
        static Transform BuildCrystal(in TerrainSource source, float3 at, float yaw, uint seed)
        {
            var root = new GameObject("SaltCrystal").transform;
            root.position = new Vector3(at.x, at.y, at.z);
            var rng = Unity.Mathematics.Random.CreateFromIndex(seed);
            var radius = rng.NextFloat(36f, CrystalCircleMax - 4f);
            // The ground round the circle, local to the root, so the ball and the trail follow the salt's rise and fall.
            var heights = new float[SaltCrystalRoll.Steps];
            for (int i = 0; i < heights.Length; i++)
            {
                var a = i * Mathf.PI * 2f / heights.Length;
                heights[i] = source.SampleHeight(new float2(at.x + Mathf.Cos(a) * radius, at.z + Mathf.Sin(a) * radius)) - at.y;
            }
            var m = new DunesColossus.MeshBuilder();
            // W-94 (#40, Zach): a meteoroid, not a lumpy sphere. One fine-faceted (32x22), slightly elongated body whose
            // surface is pushed in and out by seeded noise: broad swells, sharp ridges (folded noise) and round pits with
            // low rims. The crystals on it are small and low, so it can plausibly roll over them. Its own random, so the
            // circle, the crust and the crumbs below keep their draws.
            var lrng = Unity.Mathematics.Random.CreateFromIndex(seed ^ 0xBA11u);
            var radii = new Vector3(CrystalBall * lrng.NextFloat(1.08f, 1.18f), CrystalBall * lrng.NextFloat(0.86f, 0.94f), CrystalBall * lrng.NextFloat(0.92f, 1f));
            var turn = Quaternion.Euler(lrng.NextFloat(0f, 360f), lrng.NextFloat(0f, 360f), 0f);
            var o1 = lrng.NextFloat3(-50f, 50f); var o2 = lrng.NextFloat3(-50f, 50f); var o3 = lrng.NextFloat3(-50f, 50f);
            var pits = new float4[lrng.NextInt(8, 12)];
            for (int k = 0; k < pits.Length; k++)
                pits[k] = new float4(lrng.NextFloat3Direction(), lrng.NextFloat(0.16f, 0.42f));
            var pitDepth = lrng.NextFloat(0.045f, 0.07f);
            float Shape(Vector3 u)
            {
                var p = (float3)u;
                var f = 1f + 0.075f * noise.snoise(p * 1.3f + o1) + 0.03f * noise.snoise(p * 3.2f + o2);
                var ridge = 1f - math.abs(noise.snoise(p * 2.3f + o3));
                f += 0.05f * ridge * ridge * ridge - 0.015f;
                for (int k = 0; k < pits.Length; k++)
                {
                    var x = math.acos(math.clamp(math.dot(p, pits[k].xyz), -1f, 1f)) / pits[k].w;
                    var depth = pitDepth * (0.5f + pits[k].w);
                    if (x < 1f) f -= depth * (1f - x * x);
                    else if (x < 1.45f) f += depth * 0.3f * math.sin((x - 1f) / 0.45f * math.PI);
                }
                return f;
            }
            // The body's surface along a direction from its centre (root-local, before the roll).
            Vector3 Surface(Vector3 d)
            {
                var u = Vector3.Scale(Quaternion.Inverse(turn) * d, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z)).normalized;
                return turn * Vector3.Scale(u, radii) * Shape(u);
            }
            m.Rock(Vector3.zero, radii, turn, SaltWhite, 32, 22, Shape);
            int outcrops = lrng.NextInt(2, 4);
            for (int k = 0; k < outcrops; k++)
            {
                var d = (Vector3)lrng.NextFloat3Direction();
                var s = Surface(d);
                m.Limb(s * 0.85f, s * lrng.NextFloat(1.08f, 1.15f), lrng.NextFloat(1.8f, 2.6f), lrng.NextFloat(0.3f, 0.8f), k == 0 ? SaltPink : SaltWhite, 5);
            }
            for (int k = 0; k < 14; k++)
            {
                var d = Quaternion.Euler(rng.NextFloat(-80f, 80f), rng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                var s = Surface(d);
                m.Limb(s * 0.9f, s * rng.NextFloat(1.03f, 1.08f), rng.NextFloat(0.8f, 1.5f), 0.2f, k % 3 == 0 ? SaltPink : SaltWhite, 5);
            }
            for (int k = 0; k < 7; k++)
            {
                var d = Quaternion.Euler(rng.NextFloat(-70f, 70f), rng.NextFloat(0f, 360f), 0f) * Vector3.forward;
                m.Ellipsoid(Surface(d) * 0.99f, new Vector3(rng.NextFloat(2f, 4f), rng.NextFloat(1.5f, 3f), 0.5f), Quaternion.LookRotation(d), SaltDirt, 6, 3);
            }
            AddPart("Ball", root, m.ToMesh("SaltCrystal"));
            // Salt crumbs shed beside the track.
            var g = new DunesColossus.MeshBuilder();
            for (int k = 0; k < 10; k++)
            {
                var a = rng.NextFloat(0f, Mathf.PI * 2f);
                var r = radius + rng.NextFloat(-9f, 9f);
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = source.SampleHeight(new float2(at.x + p.x, at.z + p.z)) - at.y + 0.2f;
                var s = rng.NextFloat(0.4f, 1.2f);
                g.Ellipsoid(p, new Vector3(s, s * 0.7f, s * 0.9f), Quaternion.Euler(rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), 0f), k % 4 == 0 ? SaltPink : SaltWhite, 6, 3);
            }
            AddPart("Crumbs", root, g.ToMesh("SaltCrystalCrumbs"));
            var roll = root.gameObject.AddComponent<SaltCrystalRoll>();
            roll.Init(root.Find("Ball"), radius, CrystalBall, heights, math.radians(yaw), rng.NextBool() ? 1f : -1f, Resources.Load<Material>("Colossus"), lrng.NextUInt());
            Debug.Log($"StoneColossi: SaltCrystal circle radius {radius:F0} m round ({at.x:F0}, {at.z:F0}), ball {CrystalBall:F0} m, lap {SaltCrystalRoll.LapSeconds(radius):F0} s, trail {SaltCrystalRoll.TrailLength:F0} m");
            return root;
        }
    }
}
