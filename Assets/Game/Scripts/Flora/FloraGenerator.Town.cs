using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, town pieces: lodges, signposts, bridges, palisades, smithies, forums, fountains, woodpiles, wells. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A big folk's lodge, its door facing +Z: crossed pole pairs along its length carrying a ridge pole, hides
        /// sagging between them down to the ground (patchy, each hide its own shade), a hide flap over the door between two
        /// great tusks, and skulls on the ridge ends. Low and wide; collision along its length.
        /// </summary>
        static void Lodge(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;     // half its length, along X
            var foot = height * 0.95f;               // half its width at the ground, along Z
            g.Height = height;
            g.Width = half * 2.4f;
            g.TrunkRadius = foot * 0.7f;
            var pole = g.Bark;
            var bays = Mathf.Max(3, Mathf.RoundToInt(half / 1.8f));

            // Pole pairs crossing above the ridge, and the ridge pole.
            for (int k = 0; k <= bays; k++)
            {
                var x = Mathf.Lerp(-half, half, k / (float)bays) + g.Range(-0.15f, 0.15f);
                foreach (var z in new[] { -1f, 1f })
                    b.Tube(new List<Vector3> { new Vector3(x, -0.2f, z * foot), new Vector3(x, height, 0f), new Vector3(x, height + 0.7f, -z * 0.45f) },
                           new List<float> { 0.16f, 0.12f, 0.08f }, g.Sides(6, 4, 3), pole * g.Range(0.85f, 1.1f), FloraPart.Bark);
            }
            b.Tube(new List<Vector3> { new Vector3(-half - 0.6f, height - 0.05f, 0f), new Vector3(half + 0.6f, height - 0.05f, 0f) },
                   new List<float> { 0.13f, 0.13f }, g.Sides(6, 4, 3), pole, FloraPart.Bark);

            // Hides: one sagging panel per bay and side, from the ridge to the ground, and the two ends closed.
            var rows = g.Sides(4, 2, 1);
            var first = b.VertexCount;
            for (int k = 0; k < bays; k++)
            {
                var x0 = Mathf.Lerp(-half, half, k / (float)bays);
                var x1 = Mathf.Lerp(-half, half, (k + 1f) / bays);
                foreach (var z in new[] { -1f, 1f })
                {
                    var hide = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R()) * g.Range(0.85f, 1.1f);
                    var start = b.VertexCount;
                    for (int r = 0; r <= rows; r++)
                    {
                        var t = r / (float)rows;
                        var sag = Mathf.Sin(t * Mathf.PI) * 0.35f;
                        var p = new Vector3(0f, Mathf.Lerp(height, -0.1f, t), Mathf.Lerp(0f, z * foot, t));
                        var n = new Vector3(0f, foot, z * height).normalized;
                        p -= n * sag;
                        b.Vertex(new Vector3(x0 + 0.05f, p.y, p.z), n, hide, FloraPart.Bark, Mathf.Lerp(1f, 0.75f, t));
                        b.Vertex(new Vector3(x1 - 0.05f, p.y, p.z), n, hide, FloraPart.Bark, Mathf.Lerp(1f, 0.75f, t));
                    }
                    for (int r = 0; r < rows; r++)
                    {
                        int a = start + r * 2;
                        if (z > 0) { b.Triangle(a, a + 1, a + 2); b.Triangle(a + 1, a + 3, a + 2); }
                        else { b.Triangle(a, a + 2, a + 1); b.Triangle(a + 1, a + 2, a + 3); }
                    }
                }
            }
            foreach (var x in new[] { -1f, 1f })
            {
                var hide = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R()) * 0.9f;
                var v0 = b.Vertex(new Vector3(x * half, -0.1f, -foot), Vector3.right * x, hide, FloraPart.Bark, 0.8f);
                var v1 = b.Vertex(new Vector3(x * half, -0.1f, foot), Vector3.right * x, hide, FloraPart.Bark, 0.8f);
                var v2 = b.Vertex(new Vector3(x * half, height, 0f), Vector3.right * x, hide, FloraPart.Bark, 0.9f);
                if (x > 0) b.Triangle(v0, v2, v1); else b.Triangle(v0, v1, v2);
            }
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.3f));

            // The door: a darker hide flap in the middle bay on the +Z side, between two great tusks; skulls on the ridge.
            var bone = AccentAt(g, 0);
            var doorZ = foot * 0.55f + 0.1f;
            var doorY = height * 0.45f;
            // A heavy hide hung from its top edge: it stirs at the bottom, no more.
            var flapTurn = Quaternion.Euler(-Mathf.Atan2(foot, height) * Mathf.Rad2Deg * 0.4f, 0f, 0f);
            var flapTop = new Vector3(0f, doorY * 0.5f, doorZ) + flapTurn * new Vector3(-0.8f, doorY * 0.5f, 0.04f);
            var flap = b.VertexCount;
            b.Sheet(flapTop, flapTurn * Vector3.right * 1.6f, flapTurn * Vector3.down * doorY, g.Sides(4, 1, 1), g.Sides(4, 2, 1), s.Foliage.linear * 0.5f, FloraPart.Bark, 0.6f);
            b.Cloth(flap, p => (flapTop.y - p.y) / doorY * 0.45f);
            if (g.Lod < 2)
            {
                foreach (var x in new[] { -1.3f, 1.3f })
                {
                    var root = new Vector3(x, -0.1f, foot * 0.62f + 0.4f);
                    b.Tube(new List<Vector3> { root, root + new Vector3(x * 0.15f, height * 0.35f, 0.5f), root + new Vector3(-x * 0.35f, height * 0.6f, 0.2f) },
                           new List<float> { 0.22f, 0.14f, 0.03f }, g.Sides(7, 5, 3), bone, FloraPart.Bark, cap: true);
                }
                foreach (var x in new[] { -half - 0.6f, half + 0.6f })
                    b.Blob(new Vector3(x, height + 0.15f, 0f), new Vector3(0.35f, 0.3f, 0.42f), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.2f, x, bone, FloraPart.Bark);
            }

            var count = Mathf.Max(2, Mathf.CeilToInt(half / foot) + 1);
            for (int k = 0; k < count; k++)
                g.Colliders.Add(new Vector4(Mathf.Lerp(-half + foot * 0.6f, half - foot * 0.6f, k / (count - 1f)), 0f, foot * 0.75f, height * 0.6f));
        }

        /// <summary>A signpost: a post, two or three arm boards at different heights pointing different ways, a cap.</summary>
        static void Signpost(Grower g)
        {
            var s = g.S;
            var b = g.B;
            b.Wood = true;   // timber throughout, with its grain (Z-30)
            var height = g.Range(s.Height);
            g.Height = height;
            g.Width = 1.6f;
            g.TrunkRadius = 0.08f;
            b.Box(new Vector3(0f, height * 0.5f - 0.2f, 0f), new Vector3(0.14f, height + 0.4f, 0.14f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Box(new Vector3(0f, height + 0.04f, 0f), new Vector3(0.24f, 0.08f, 0.24f), Quaternion.identity, g.Bark * 0.7f, FloraPart.Bark);
            g.Perches.Add(Vector3.up * (height + 0.08f));
            var arms = g.Range(0f, 1f) < 0.5f ? 2 : 3;
            for (int k = 0; k < arms; k++)
            {
                var yaw = (k * 2.1f + g.Range(-0.3f, 0.3f)) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var y = height - 0.25f - k * 0.32f;
                var board = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R());
                b.Box(rot * new Vector3(0.45f, y, 0f), new Vector3(0.85f, 0.2f, 0.04f), rot, board, FloraPart.Bark);
                if (g.Lod == 0)
                    b.Box(rot * new Vector3(0.9f, y, 0f), new Vector3(0.12f, 0.12f, 0.04f), rot * Quaternion.Euler(0f, 0f, 45f), board, FloraPart.Bark);
            }
        }

        /// <summary>Rail length of a bridge span, post to post: RoadBaking's span spacing, so joints share a post.</summary>
        const float RailRun = 6.4f;

        /// <summary>
        /// One 6.6 m span of a plank bridge along its +Z, deck top at the origin: planks across two stringers, two pairs
        /// of piles down into the water, and a rail each side. It's long so that spans 6.4 m apart each get their own cell
        /// in the tree layer, which is drawn to the horizon. Spans are laid end to end across a river; each draws a post
        /// at both ends of each rail (W-34; before, only at its start, so the far end's rails hung in the air). No
        /// colliders: the deck is walked on through SettlementSampling.Deck.
        /// </summary>
        static void Bridge(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var rail = g.Range(s.Height);
            var half = Mathf.Max(s.Spread, 1f) * 0.5f;
            const float length = 6.6f;
            g.Height = rail;
            g.Width = length;
            g.TrunkRadius = 0f;
            var wood = g.Bark;
            b.Wood = true;   // planks, stringers, piles and rails are timber, with its grain (Z-30)
            // Planks, each a slightly different shade and height so the deck doesn't read as one board.
            var planks = g.Lod == 0 ? 22 : g.Lod == 1 ? 11 : 1;
            for (int k = 0; k < planks; k++)
            {
                var z = Mathf.Lerp(-length * 0.5f, length * 0.5f, (k + 0.5f) / planks);
                b.Box(new Vector3(g.Range(-0.04f, 0.04f), -0.06f + g.Range(-0.012f, 0.012f), z),
                      new Vector3(half * 2f + g.Range(0f, 0.14f), 0.1f, length / planks * 0.92f), Quaternion.identity,
                      wood * g.Range(0.82f, 1.08f), FloraPart.Bark);
            }
            foreach (var x in new[] { -1f, 1f })
            {
                // A stringer under the planks, a pile down into the riverbed, and the rail.
                b.Box(new Vector3(x * (half - 0.25f), -0.24f, 0f), new Vector3(0.2f, 0.26f, length), Quaternion.identity, wood * 0.7f, FloraPart.Bark);
                foreach (var z in new[] { -length * 0.25f, length * 0.25f })
                    b.Tube(new List<Vector3> { new Vector3(x * (half - 0.25f), -5f, z), new Vector3(x * (half - 0.25f), -0.2f, z) },
                           new List<float> { 0.16f, 0.14f }, g.Sides(6, 4, 3), wood * 0.6f, FloraPart.Bark);
                // W-34: a post at both ends of each side, at +-3.2 m (half the 6.4 m span spacing), so neighbouring
                // spans share a post at each joint and the last span's rails end on one too. The rails run post to
                // post. Posts reach 0.6 m below the deck (down its side), so a neighbour's post still covers a rail
                // set a little lower, and nothing hangs in the air.
                foreach (var z in new[] { -RailRun * 0.5f, RailRun * 0.5f })
                    b.Box(new Vector3(x * (half - 0.05f), rail * 0.5f - 0.25f, z), new Vector3(0.14f, rail + 0.7f, 0.14f), Quaternion.identity, wood * 0.85f, FloraPart.Bark);
                b.Box(new Vector3(x * (half - 0.05f), rail, 0f), new Vector3(0.1f, 0.1f, RailRun), Quaternion.identity, wood * 0.95f, FloraPart.Bark);
                if (g.Lod == 0)
                    b.Box(new Vector3(x * (half - 0.05f), rail * 0.5f, 0f), new Vector3(0.06f, 0.07f, RailRun), Quaternion.identity, wood * 0.8f, FloraPart.Bark);
            }
            b.Wood = false;
        }

        /// <summary>
        /// One 3 m length of palisade along its X: a row of sharpened logs (Bark) of uneven height sunk into the ground,
        /// with a binding rail across the back (-Z, inside the camp). Laid end to end round a camp.
        /// </summary>
        static void Palisade(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            const float length = 3.1f;
            g.Height = height;
            g.Width = length;
            g.TrunkRadius = 0f;
            var logs = g.Lod == 2 ? 4 : 10;
            for (int k = 0; k < logs; k++)
            {
                var x = Mathf.Lerp(-length * 0.5f, length * 0.5f, (k + 0.5f) / logs);
                var r = length / logs * 0.5f * g.Range(0.95f, 1.15f);
                var top = height * g.Range(0.85f, 1.1f);
                var lean = new Vector3(g.Range(-0.06f, 0.06f), 0f, g.Range(-0.05f, 0.05f));
                // A cone cap on a tube is as tall as the tube is wide, so a thin last ring gives a sharpened point.
                b.Tube(new List<Vector3> { new Vector3(x, -1f, 0f), new Vector3(x, 0f, 0f) + lean * 0.2f, new Vector3(x, top - r * 2.5f, 0f) + lean, new Vector3(x, top, 0f) + lean * 1.05f },
                       new List<float> { r, r, r * 0.95f, r * 0.12f }, g.Sides(6, 5, 4), g.Bark * g.Range(0.8f, 1.1f), FloraPart.Bark);
                if (k == 2 || k == logs - 3)
                    g.Perches.Add(new Vector3(x, top, 0f) + lean * 1.05f);
            }
            if (g.Lod < 2)
                foreach (var y in new[] { 0.25f, 0.7f })
                    b.Box(new Vector3(0f, height * y, -0.2f), new Vector3(length, 0.12f, 0.1f), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            for (int k = -1; k <= 1; k++)
                g.Colliders.Add(new Vector4(k * 1f, 0f, 0.55f, height));
        }

        static void Smithy(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            var depth = half * 0.8f;
            var eaves = height * 0.58f;
            g.Height = height;
            g.Width = half * 2.6f;
            g.TrunkRadius = 0f;

            // An open shed: corner posts under a pyramid roof.
            b.Wood = true;   // the shed's corner posts (Z-30)
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Tube(new List<Vector3> { new Vector3(x * half, -0.05f, z * depth), new Vector3(x * half, eaves, z * depth) },
                       new List<float> { 0.12f, 0.11f }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark);
            b.Wood = false;
            var roof = b.VertexCount;
            RoofTube(b, eaves - 0.2f, Mathf.Max(half, depth) * 1.414f * 1.15f, 4, g.FoliageColor(), FloraPart.Bark, height, 0.05f, 0f, 0f);
            RotateFrom(b, roof, Quaternion.Euler(0f, 45f, 0f));

            // The forge at the back: a stone hearth with its coals sunk in a pit inside a raised rim (Z-40: they sat on top),
            // glowing embers among dark coals, and its chimney up through the roof.
            var hearth = new Vector3(0f, 0.55f, -depth * 0.45f);
            float hearthX = half * 1.1f, hearthZ = depth * 0.8f, bed = 0.85f, rim = 0.15f;
            b.Box(new Vector3(hearth.x, bed * 0.5f, hearth.z), new Vector3(hearthX, bed, hearthZ), Quaternion.identity, Stone(g), FloraPart.Bark);
            foreach (var side in new[] { -1f, 1f })
            {
                b.Box(new Vector3(hearth.x, (bed + 1.1f) * 0.5f, hearth.z + side * (hearthZ - rim) * 0.5f), new Vector3(hearthX, 1.1f - bed, rim),
                      Quaternion.identity, Stone(g), FloraPart.Bark);
                b.Box(new Vector3(hearth.x + side * (hearthX - rim) * 0.5f, (bed + 1.1f) * 0.5f, hearth.z), new Vector3(rim, 1.1f - bed, hearthZ - rim * 2f),
                      Quaternion.identity, Stone(g), FloraPart.Bark);
            }
            var pit = new Vector2(hearthX * 0.5f - rim - 0.08f, hearthZ * 0.5f - rim - 0.08f);
            var embers = g.Lod == 0 ? 7 : 3;
            for (int k = 0; k < embers; k++)
            {
                var at = new Vector3(hearth.x + g.Range(-pit.x, pit.x), bed + 0.02f, hearth.z + g.Range(-pit.y, pit.y));
                var size = g.Range(0.08f, 0.17f);
                var glowing = k % 3 != 2;
                b.Blob(at, new Vector3(size, size * 0.6f, size * g.Range(0.8f, 1.2f)), Quaternion.Euler(0f, g.R() * 360f, 0f), 1, 0.3f, g.R() * 50f,
                       glowing ? AccentAt(g, 0) : g.Bark * 0.25f, glowing ? FloraPart.Glow : FloraPart.Bark);
            }
            b.Box(new Vector3(0f, (1.1f + height + 0.8f) * 0.5f, -depth * 0.7f), new Vector3(0.7f, height + 0.8f - 1.1f, 0.6f), Quaternion.identity, Stone(g) * 0.85f, FloraPart.Bark);
            ChimneyTop(g, new Vector3(0f, height + 0.8f, -depth * 0.7f), 0.3f, Stone(g) * 0.85f);
            g.Perches.Add(Vector3.up * height);
            g.Perches.Add(new Vector3(0f, height + 0.8f, -depth * 0.7f));
            // The anvil on its block.
            var anvil = new Vector3(half * 0.3f, 0f, depth * 0.25f);
            var iron = new Color(0.06f, 0.06f, 0.07f);
            b.Wood = true;   // the anvil's block (Z-30)
            b.Box(anvil + Vector3.up * 0.3f, new Vector3(0.35f, 0.6f, 0.35f), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            b.Wood = false;
            b.Box(anvil + Vector3.up * 0.72f, new Vector3(0.75f, 0.24f, 0.3f), Quaternion.identity, iron, FloraPart.Metal);
        }

        static void Forum(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var columns = Mathf.Max(6, g.Count(s.Branches));
            var shaft = height * 0.045f;
            g.Height = height + 0.6f;
            g.Width = radius * 2.3f;
            g.TrunkRadius = 0f;

            // A low round platform.
            b.Blob(Vector3.zero, new Vector3(radius + 1.2f, 0.25f, radius + 1.2f), Quaternion.identity, g.Sides(3, 2, 1), 0f, 0f, g.Bark * 0.9f, FloraPart.Bark);
            var tops = new List<Vector3>();
            for (int k = 0; k < columns; k++)
            {
                var a = k / (float)columns * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                var first = b.VertexCount;
                b.Box(p + Vector3.up * 0.25f, new Vector3(shaft * 3f, 0.3f, shaft * 3f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), g.Bark, FloraPart.Bark);
                b.Tube(new List<Vector3> { p + Vector3.up * 0.35f, p + Vector3.up * height * 0.5f, p + Vector3.up * height },
                       new List<float> { shaft, shaft * 1.02f, shaft * 0.86f }, g.Sides(10, 6, 4), g.Bark, FloraPart.Bark, ribs: g.Lod == 0 ? 10 : 0, ribDepth: 0.06f);
                b.Box(p + Vector3.up * (height + 0.12f), new Vector3(shaft * 3f, 0.25f, shaft * 3f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), g.Bark, FloraPart.Bark);
                b.Occlude(first, (q, n) => GroundOcclusion(q, height * 0.3f));
                tops.Add(p + Vector3.up * (height + 0.4f));
            }
            // The lintel ring joining the column tops.
            for (int k = 0; k < columns; k++)
                b.Beam(tops[k], tops[(k + 1) % columns], shaft * 2.6f, 0.4f, g.Bark * 0.95f, FloraPart.Bark);
        }

        static void Fountain(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.1f;
            g.TrunkRadius = 0f;   // the basin is wadeable and its rim a ledge (PropSupport); only the pillar blocks
            var sides = g.Sides(18, 12, 8);

            // The basin wall, its rim, and the water inside.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.6f }, new List<float> { radius, radius }, sides, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.6f));
            b.Tube(new List<Vector3> { Vector3.up * 0.58f, Vector3.up * 0.7f }, new List<float> { radius + 0.12f, radius + 0.12f }, sides, g.Bark * 1.05f, FloraPart.Bark);
            // The rim's top: a flat stone ring from just inside the wall out to the rim's edge (from above, the gap
            // between the wall and the rim showed the ground below, Z-12).
            var rimTop = b.VertexCount;
            for (int k = 0; k < sides; k++)
            {
                var a = k * Mathf.PI * 2f / sides;
                var round = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Vertex(round * (radius - 0.08f) + Vector3.up * 0.7f, Vector3.up, g.Bark * 1.05f, FloraPart.Bark, 1f);
                b.Vertex(round * (radius + 0.12f) + Vector3.up * 0.7f, Vector3.up, g.Bark * 1.05f, FloraPart.Bark, 1f);
            }
            for (int k = 0; k < sides; k++)
            {
                int inner0 = rimTop + k * 2, inner1 = rimTop + (k + 1) % sides * 2;
                b.Triangle(inner0, inner1, inner0 + 1);
                b.Triangle(inner0 + 1, inner1, inner1 + 1);
            }
            // The water itself is drawn by FloraSystem with the terrain's water (Pools); this disc, just under it, stands in
            // beyond the range it's drawn to.
            var water = s.Foliage.linear;
            b.Blob(Vector3.up * 0.64f, new Vector3(radius * 0.97f, 0.03f, radius * 0.97f), Quaternion.identity, g.Sides(2, 1, 1), 0f, 0f, water, FloraPart.Metal);
            g.Pools.Add(new Vector4(0f, 0.66f, 0f, radius * 0.97f));

            // A pillar holding up a smaller bowl, brimming, with a finial on top.
            b.Tube(new List<Vector3> { Vector3.up * 0.5f, Vector3.up * height * 0.72f }, new List<float> { 0.22f, 0.16f }, g.Sides(8, 6, 4), g.Bark, FloraPart.Bark);
            g.Colliders.Add(new Vector4(0f, 0f, 0.3f, height * 0.72f));
            b.Blob(Vector3.up * height * 0.75f, new Vector3(radius * 0.38f, 0.14f, radius * 0.38f), Quaternion.identity, g.Sides(2, 1, 1), 0f, 0f, g.Bark, FloraPart.Bark);
            b.Blob(Vector3.up * (height * 0.75f + 0.08f), new Vector3(radius * 0.34f, 0.03f, radius * 0.34f), Quaternion.identity, 1, 0f, 0f, water, FloraPart.Metal);
            g.Pools.Add(new Vector4(0f, height * 0.75f + 0.1f, 0f, radius * 0.34f));
            b.Tube(new List<Vector3> { Vector3.up * height * 0.78f, Vector3.up * height }, new List<float> { 0.09f, 0.03f }, g.Sides(6, 4, 3), g.Bark, FloraPart.Bark, cap: true);
        }


        /// <summary>Pale cut wood, for log ends and stripped logs (W-99).</summary>
        static readonly Color LogSapwood = new Color(0.74f, 0.58f, 0.37f);

        /// <summary>
        /// A sawn log end (W-99): a flat disc facing along X (side -1 or +1) instead of a rounded cap, its rings laid
        /// out as concentric vertex rings in alternating sapwood shades round a slightly off-centre heart, inside a
        /// thin bark rim. Marked as Surface 5 so the flora shader adds only fine end-grain noise, no lengthwise grain.
        /// </summary>
        static void LogEnd(Grower g, Vector3 centre, float side, float radius, int sides, Color bark)
        {
            var b = g.B;
            var sap = Color.Lerp(LogSapwood, bark, 0.12f) * g.Range(0.92f, 1.06f);
            var heart = Color.Lerp(sap, new Color(0.55f, 0.34f, 0.2f), 0.45f);
            var n = new Vector3(side, 0f, 0f);
            centre += n * 0.002f;
            // Radius fractions and shades, outside in: bark rim, sapwood, then rings darkening to the heart.
            var lod = g.Lod;
            var fractions = lod >= 2 ? new[] { 1f, 0f } : new[] { 1f, 0.9f, 0.78f, 0.62f, 0.47f, 0.3f, 0.14f, 0f };
            var heartShift = new Vector3(0f, g.Range(-0.15f, 0.15f), g.Range(-0.15f, 0.15f)) * radius;
            b.Surface = 5;
            var first = b.VertexCount;
            for (int i = 0; i < fractions.Length; i++)
            {
                var f = fractions[i];
                Color c;
                if (lod >= 2) c = Color.Lerp(sap, bark, 0.3f);
                else if (i == 0) c = bark;
                else if (i == fractions.Length - 1) c = heart * 0.9f;
                else c = Color.Lerp(sap, heart, (1f - f) * 0.8f) * (i % 2 == 0 ? 0.86f : 1.04f);
                // Rings drift towards the off-centre heart as they shrink, and wobble a little.
                var ringCentre = centre + heartShift * (1f - f);
                var count = f == 0f ? 1 : sides;
                for (int s = 0; s < count; s++)
                {
                    var angle = s / (float)sides * Mathf.PI * 2f;
                    var dir = new Vector3(0f, -Mathf.Sin(angle), Mathf.Cos(angle));
                    var wobble = i == 0 ? 1f : 1f + g.Range(-0.05f, 0.05f);
                    b.Vertex(ringCentre + dir * radius * f * wobble, n, c, FloraPart.Bark, 1f);
                }
            }
            for (int i = 0; i < fractions.Length - 1; i++)
            {
                var outer = first + i * sides;
                var inner = outer + sides;
                var last = i == fractions.Length - 2;
                for (int s = 0; s < sides; s++)
                {
                    int o0 = outer + s, o1 = outer + (s + 1) % sides;
                    int i0 = last ? inner : inner + s, i1 = last ? inner : inner + (s + 1) % sides;
                    if (side > 0f) b.Triangle(o0, o1, i0); else b.Triangle(o1, o0, i0);
                    if (!last)
                    {
                        if (side > 0f) b.Triangle(o1, i1, i0); else b.Triangle(i1, o1, i0);
                    }
                }
            }
            b.Surface = 0;
        }

        static void Woodpile(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var length = height * s.Spread;
            var log = height / 8f;
            g.Height = height;
            g.Width = length * 1.6f;
            g.TrunkRadius = length * 0.35f;

            // Logs along X, stacked in rows that narrow upwards; each a slightly different shade.
            var rows = 4;
            for (int r = 0; r < rows; r++)
            {
                var count = rows - r + 1;
                for (int k = 0; k < count; k++)
                {
                    var z = (k - (count - 1) * 0.5f) * log * 2.05f;
                    var y = log + r * log * 1.75f;
                    var ends = g.Range(-0.15f, 0.15f);
                    var r0 = log * g.Range(0.85f, 1.05f);
                    var r1 = log * g.Range(0.85f, 1.05f);
                    var bark = Color.Lerp(g.Bark, g.Bark * 1.25f, g.R());
                    var sides = g.Sides(9, 6, 4);
                    // Most logs keep their bark; now and then one is split or stripped, its grain showing (W-99).
                    var stripped = g.R() < 0.2f;
                    var a = new Vector3(-length * 0.5f + ends, y, z);
                    var e = new Vector3(length * 0.5f + ends, y, z);
                    b.Wood = stripped;
                    b.Tube(new List<Vector3> { a, e }, new List<float> { r0, r1 }, sides,
                           stripped ? Color.Lerp(bark, LogSapwood, 0.55f) : bark, FloraPart.Bark);
                    b.Wood = false;
                    LogEnd(g, a, -1f, r0, sides, stripped ? Color.Lerp(bark, LogSapwood, 0.55f) : bark);
                    LogEnd(g, e, 1f, r1, sides, stripped ? Color.Lerp(bark, LogSapwood, 0.55f) : bark);
                }
            }
            // The chopping block in front, with an axe in it.
            var block = new Vector3(length * 0.25f, 0f, log * 2.05f * 2.5f + 0.6f);
            b.Wood = true;   // the chopping block (Z-30)
            b.Tube(new List<Vector3> { block + Vector3.down * 0.05f, block + Vector3.up * 0.5f }, new List<float> { 0.3f, 0.28f }, g.Sides(9, 6, 4), g.Bark * 0.85f, FloraPart.Bark);
            b.Wood = false;
            b.Blob(block + Vector3.up * 0.5f, new Vector3(0.28f, 0.02f, 0.28f), Quaternion.identity, 1, 0f, 0f, g.Bark * 1.3f, FloraPart.Bark);
            if (g.Lod < 2)
            {
                var head = block + new Vector3(0.05f, 0.56f, 0f);
                b.Beam(head, head + new Vector3(-0.45f, 0.45f, 0.1f), 0.05f, 0.05f, g.Bark * 0.7f, FloraPart.Bark);
                b.Box(head + new Vector3(0.03f, 0.02f, 0f), new Vector3(0.22f, 0.12f, 0.05f), Quaternion.Euler(0f, 0f, 35f), new Color(0.12f, 0.12f, 0.13f), FloraPart.Metal);
            }
        }

        static void Well(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var stone = s.FoliageVariation.linear;
            var sides = g.Sides(16, 10, 7);
            g.Height = height;
            g.Width = radius * 2.6f;
            g.TrunkRadius = 0f;   // stand on the rim (PropSupport); the shaft itself is walled off
            g.Colliders.Add(new Vector4(0f, 0f, radius - 0.16f, 0.8f));

            // The wall (outer and inner faces, and a rim), with dark water well down inside.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.8f }, new List<float> { radius, radius }, sides, stone, FloraPart.Bark);
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * 0.8f }, new List<float> { radius - 0.16f, radius - 0.16f }, sides, stone * 0.6f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.6f));
            b.Tube(new List<Vector3> { Vector3.up * 0.78f, Vector3.up * 0.9f }, new List<float> { radius + 0.05f, radius + 0.05f }, sides, stone * 1.05f, FloraPart.Bark);
            b.Blob(Vector3.up * 0.25f, new Vector3(radius - 0.16f, 0.02f, radius - 0.16f), Quaternion.identity, 1, 0f, 0f, new Color(0.03f, 0.05f, 0.06f), FloraPart.Metal);

            // Posts, a little gabled roof, the crank and a bucket.
            var top = height * 0.78f;
            foreach (var x in new[] { -1f, 1f })
                b.Box(new Vector3(x * (radius + 0.05f), top * 0.5f, 0f), new Vector3(0.16f, top, 0.16f), Quaternion.identity, g.Bark, FloraPart.Bark);
            var roof = g.FoliageColor();
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, top - 0.05f, z * (radius * 0.75f + 0.25f)), new Vector3(0f, height, 0f), radius * 2f + 0.6f, 0.08f, roof, FloraPart.Bark);
            foreach (var x in new[] { -1f, 0f, 1f })
                g.Perches.Add(new Vector3(x * (radius + 0.2f), height + 0.04f, 0f));
            b.Beam(new Vector3(-radius - 0.15f, top * 0.72f, 0f), new Vector3(radius + 0.25f, top * 0.72f, 0f), 0.08f, 0.08f, g.Bark * 0.8f, FloraPart.Bark);
            if (g.Lod < 2)
                b.Tube(new List<Vector3> { new Vector3(0f, top * 0.42f, 0f), new Vector3(0f, top * 0.55f, 0f) }, new List<float> { 0.15f, 0.17f },
                       g.Sides(8, 5, 4), g.Bark * 0.7f, FloraPart.Bark, cap: true);
        }
    }
}
