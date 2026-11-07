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
            // W-94 (#40, Zach): a meteoroid body pushed in and out by seeded noise. W-118 (#40, Zach): round, since it
            // rolls: near-equal radii, the broad swells kept faint, no ridges, and the surface peppered with many small,
            // shallow dents with soft rims (pocks where crystals broke off as it rolled). Finer facets (72x50) so the
            // dents, about 0.6-1.6 m across, read. Its own random, so the circle, the crust and the crumbs below keep
            // their draws.
            var lrng = Unity.Mathematics.Random.CreateFromIndex(seed ^ 0xBA11u);
            var radii = new Vector3(CrystalBall * lrng.NextFloat(1f, 1.03f), CrystalBall * lrng.NextFloat(0.98f, 1.01f), CrystalBall * lrng.NextFloat(0.99f, 1.02f));
            var turn = Quaternion.Euler(lrng.NextFloat(0f, 360f), lrng.NextFloat(0f, 360f), 0f);
            var o1 = lrng.NextFloat3(-50f, 50f); var o2 = lrng.NextFloat3(-50f, 50f);
            var dents = new float4[lrng.NextInt(110, 140)];
            for (int k = 0; k < dents.Length; k++)
                dents[k] = new float4(lrng.NextFloat3Direction(), lrng.NextFloat(0.025f, 0.065f));
            // A few larger, still shallow bowls, so the dents aren't all one size.
            for (int k = 0; k < 6; k++)
                dents[k].w = lrng.NextFloat(0.09f, 0.14f);
            const float DentRim = 1.5f;
            float Shape(Vector3 u)
            {
                var p = (float3)u;
                var f = 1f + 0.012f * noise.snoise(p * 1.4f + o1) + 0.004f * noise.snoise(p * 5f + o2);
                for (int k = 0; k < dents.Length; k++)
                {
                    var cos = math.dot(p, dents[k].xyz);
                    if (cos < math.cos(dents[k].w * DentRim)) continue;
                    var x = math.acos(math.min(cos, 1f)) / dents[k].w;
                    // Depth grows with size but stays fine: 1-2.5 % of the radius (13-33 cm).
                    var depth = 0.006f + 0.14f * dents[k].w;
                    if (x < 1f) f -= depth * (1f - x * x);
                    else f += depth * 0.25f * math.sin((x - 1f) / (DentRim - 1f) * math.PI);
                }
                return f;
            }
            // The body's surface along a direction from its centre (root-local, before the roll).
            Vector3 Surface(Vector3 d)
            {
                var u = Vector3.Scale(Quaternion.Inverse(turn) * d, new Vector3(1f / radii.x, 1f / radii.y, 1f / radii.z)).normalized;
                return turn * Vector3.Scale(u, radii) * Shape(u);
            }
            m.Rock(Vector3.zero, radii, turn, SaltWhite, 72, 50, Shape);
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
