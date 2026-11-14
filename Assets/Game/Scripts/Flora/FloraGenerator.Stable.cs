using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, a town's stable and a village's hitching rail (A-117): where riders hand in their horses.
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A-117: a stable. A lean-to shed across the back (-Z) over a row of stalls (plank partitions, a manger of hay at
        /// the back of each), planked back and side walls, and in front a railed paddock with its gate gap at +Z (the
        /// street), a trough and a few hay bales. Height is the shed's back wall; Height times Spread its width; its depth
        /// a little more. The walls and rails stop the player, the gate gap doesn't.
        /// </summary>
        static void Stable(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var width = height * s.Spread;
            var depth = width * 1.05f;
            var hx = width * 0.5f;
            var back = -depth * 0.5f;
            var front = depth * 0.5f;
            var shed = back + depth * 0.42f;   // the shed's open front, where the paddock starts
            var eave = height * 0.68f;
            var fence = Mathf.Clamp(height * 0.36f, 1.1f, 1.5f);
            g.Height = height;
            g.Width = Mathf.Max(width, depth) * 1.15f;
            g.TrunkRadius = 0f;
            var plank = g.Bark;
            var hay = AccentAt(g, 0);

            // The shed: planked back and side walls (the sides slope down to the front eave), posts along its open front.
            b.Wood = true;
            b.Box(new Vector3(0f, height * 0.5f, back + 0.06f), new Vector3(width, height, 0.12f), Quaternion.identity, plank * 0.85f, FloraPart.Bark);
            foreach (var x in new[] { -1f, 1f })
            {
                var wall = (height + eave) * 0.5f;
                b.Box(new Vector3(x * (hx - 0.06f), wall * 0.5f, (back + shed) * 0.5f), new Vector3(0.12f, wall, shed - back),
                      Quaternion.identity, plank * 0.8f, FloraPart.Bark);
            }
            var stalls = Mathf.Clamp(g.Count(s.Branches), 3, 4);
            var stall = width / stalls;
            for (int k = 0; k <= stalls; k++)
            {
                var x = -hx + k * stall;
                x = Mathf.Clamp(x, -hx + 0.1f, hx - 0.1f);
                b.Tube(new List<Vector3> { new Vector3(x, -0.05f, shed), new Vector3(x, eave, shed) },
                       new List<float> { 0.11f, 0.1f }, g.Sides(6, 4, 3), plank, FloraPart.Bark, cap: true);
                // Partitions between stalls: chest-high planks from the back wall most of the way to the front.
                if (k > 0 && k < stalls)
                    b.Box(new Vector3(x, 0.7f, (back + shed) * 0.5f - 0.15f), new Vector3(0.07f, 1.4f, shed - back - 0.5f),
                          Quaternion.identity, plank * 0.95f, FloraPart.Bark);
            }
            // A manger of hay against the back wall of each stall.
            for (int k = 0; k < stalls; k++)
            {
                var x = -hx + (k + 0.5f) * stall;
                b.Box(new Vector3(x, 0.75f, back + 0.4f), new Vector3(stall * 0.6f, 0.4f, 0.5f), Quaternion.identity, plank * 0.6f, FloraPart.Bark);
                if (g.Lod < 2)
                {
                    b.Wood = false;
                    b.Blob(new Vector3(x, 1.0f, back + 0.4f), new Vector3(stall * 0.26f, 0.18f, 0.22f), Quaternion.identity, 1, 0.45f, g.R() * 50f,
                           hay, FloraPart.Bark);
                    b.Wood = true;
                }
            }
            b.Wood = false;
            // The lean-to roof: one slab from over the back wall down past the front posts.
            var run = new Vector2(shed - back + 0.9f, height - eave + 0.25f);
            var slope = Mathf.Atan2(run.y, run.x) * Mathf.Rad2Deg;
            var mid = new Vector3(0f, (height + 0.1f + eave - 0.15f) * 0.5f, (back - 0.3f + shed + 0.6f) * 0.5f);
            b.Box(mid, new Vector3(width + 0.6f, 0.14f, run.magnitude), Quaternion.Euler(slope, 0f, 0f), g.FoliageColor(), FloraPart.Bark);
            g.Perches.Add(new Vector3(-hx * 0.6f, height + 0.15f, back));
            g.Perches.Add(new Vector3(hx * 0.6f, height + 0.15f, back));

            // The paddock: posts and two rails round the front, with the gate gap in the middle of the street side.
            var gate = Mathf.Min(1.4f, hx * 0.4f);
            var corners = new[]
            {
                new Vector3(-hx, 0f, shed), new Vector3(-hx, 0f, front), new Vector3(-gate, 0f, front),
                new Vector3(gate, 0f, front), new Vector3(hx, 0f, front), new Vector3(hx, 0f, shed)
            };
            b.Wood = true;
            for (int k = 0; k < corners.Length - 1; k++)
            {
                if (k == 2)
                    continue;   // the gate
                Vector3 p = corners[k], q = corners[k + 1];
                var posts = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(p, q) / 2.2f));
                for (int i = 0; i <= posts; i++)
                {
                    var at = Vector3.Lerp(p, q, i / (float)posts);
                    b.Tube(new List<Vector3> { at + Vector3.down * 0.1f, at + Vector3.up * fence * g.Range(0.97f, 1.06f) },
                           new List<float> { 0.08f, 0.07f }, g.Sides(5, 4, 3), plank, FloraPart.Bark, cap: true);
                    if (i == posts && k % 2 == 0)
                        g.Perches.Add(at + Vector3.up * fence);
                }
                foreach (var y in new[] { 0.45f, 0.88f })
                    b.Beam(p + Vector3.up * fence * y, q + Vector3.up * fence * y, 0.1f, 0.07f, plank * 0.9f, FloraPart.Bark);
            }
            // A trough along one side, hay bales stacked in the other front corner of the shed.
            var trough = new Vector3(-hx + 0.6f, 0.24f, (shed + front) * 0.5f);
            b.Box(trough, new Vector3(0.55f, 0.48f, Mathf.Min(2f, front - shed - 1f)), Quaternion.identity, plank * 0.65f, FloraPart.Bark);
            b.Wood = false;
            if (g.Lod < 2)
                b.Box(trough + Vector3.up * 0.2f, new Vector3(0.4f, 0.04f, Mathf.Min(1.85f, front - shed - 1.15f)), Quaternion.identity,
                      new Color(0.16f, 0.22f, 0.24f), FloraPart.Bark);
            var bales = g.Lod == 0 ? 3 : g.Lod == 1 ? 2 : 1;
            for (int k = 0; k < bales; k++)
            {
                var at = new Vector3(hx - 0.75f - (k % 2) * 1.05f, 0.25f + (k / 2) * 0.5f, shed + 0.55f + (k / 2) * 0.1f);
                b.Box(at, new Vector3(1f, 0.5f, 0.6f), Quaternion.Euler(0f, g.Range(-8f, 8f), 0f), hay * g.Range(0.85f, 1f), FloraPart.Bark);
            }

            // The walls and rails stop the player; the open shed front and the gate don't.
            FenceColliders(g, new Vector3(-hx, 0f, back), new Vector3(hx, 0f, back), height);
            FenceColliders(g, new Vector3(-hx, 0f, back), new Vector3(-hx, 0f, shed), height);
            FenceColliders(g, new Vector3(hx, 0f, back), new Vector3(hx, 0f, shed), height);
            for (int k = 0; k < corners.Length - 1; k++)
                if (k != 2)
                    FenceColliders(g, corners[k], corners[k + 1], fence);
        }

        /// <summary>
        /// A-117: a hitching rail, where a rider ties a horse in a village too small for a stable: two posts and a rail
        /// along X (Height the posts', Height times Spread the rail's length), a small trough in front (+Z) and a hay bale
        /// at one end. The rail stops the player.
        /// </summary>
        static void HitchRail(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var length = height * s.Spread;
            var half = length * 0.5f;
            g.Height = height;
            g.Width = length * 1.2f;
            g.TrunkRadius = 0f;
            var wood = g.Bark;
            b.Wood = true;
            foreach (var x in new[] { -half, half })
            {
                var top = new Vector3(x, height * g.Range(1f, 1.06f), 0f);
                b.Tube(new List<Vector3> { new Vector3(x, -0.1f, 0f), top }, new List<float> { 0.09f, 0.08f }, g.Sides(6, 4, 3),
                       wood, FloraPart.Bark, cap: true);
                g.Perches.Add(top);
            }
            b.Beam(new Vector3(-half - 0.15f, height * 0.9f, 0f), new Vector3(half + 0.15f, height * 0.9f, 0f), 0.11f, 0.09f, wood * 0.9f, FloraPart.Bark);
            var trough = new Vector3(g.Range(-0.3f, 0.3f), 0.2f, 0.75f);
            b.Box(trough, new Vector3(Mathf.Min(1.6f, length * 0.6f), 0.4f, 0.45f), Quaternion.identity, wood * 0.65f, FloraPart.Bark);
            b.Wood = false;
            if (g.Lod < 2)
            {
                b.Box(trough + Vector3.up * 0.17f, new Vector3(Mathf.Min(1.45f, length * 0.55f), 0.04f, 0.32f), Quaternion.identity,
                      new Color(0.16f, 0.22f, 0.24f), FloraPart.Bark);
                b.Box(new Vector3(half + 0.7f, 0.25f, 0.3f), new Vector3(0.6f, 0.5f, 0.95f), Quaternion.Euler(0f, g.Range(-12f, 12f), 0f),
                      AccentAt(g, 0), FloraPart.Bark);
            }
            FenceColliders(g, new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), height);
        }
    }
}
