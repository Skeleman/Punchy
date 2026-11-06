using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, village buildings: tents, towers, pens, stalls, inns, halls, shrines, gardens. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        static void Tent(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.2f;
            g.TrunkRadius = radius * 0.85f;

            // Poles lean in to meet near the top, then poke out past it, a little crooked.
            var poles = Mathf.Max(3, g.Count(s.Branches));
            var apex = new Vector3(g.Range(-0.1f, 0.1f), height * 0.88f, g.Range(-0.1f, 0.1f));
            var rows = g.Sides(5, 3, 2);
            // Each edge sags towards the axis under the apex, not along its own panel's middle (W-43): a pole's edge is
            // shared by two panels, so both put it in the same place and no slit opens at the corner.
            Vector3 Inward(Vector3 p) => -new Vector3(p.x - apex.x, 0f, p.z - apex.z).normalized;
            float Sag(float t) => Mathf.Sin(t * Mathf.PI) * radius * 0.06f;
            var feet = new List<Vector3>();
            for (int k = 0; k < poles; k++)
            {
                var a = (k + g.Range(-0.2f, 0.2f)) / poles * Mathf.PI * 2f + 0.3f;
                var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.9f, 1.05f);
                feet.Add(foot);
                var through = apex + (apex - foot).normalized * height * g.Range(0.12f, 0.22f);
                // W-113 (item 9): the pole bows with the hide's sag and stands just inside it, so the hide covers it from
                // the ground up and only its tip shows past the apex (a straight pole stood outside the sagging hide its
                // whole length and poked out at the foot).
                // Deep enough that even a three- or four-pole tent's flat panels cover a pole's faceted sides; it closes
                // only just under the apex, where the poles cross.
                var inset = radius * 0.12f;
                var path = new List<Vector3>();
                var radii = new List<float>();
                for (int r = 0; r < rows; r++)
                {
                    var t = r / (float)rows;
                    var p = Vector3.Lerp(foot, apex, t);
                    path.Add(p + Inward(p) * (Sag(t) + inset * (1f - t * t)) + (r == 0 ? Vector3.down * 0.1f : Vector3.zero));
                    radii.Add(radius * Mathf.Lerp(0.04f, 0.035f, t));
                }
                path.Add(apex);
                radii.Add(radius * 0.035f);
                path.Add(through);
                radii.Add(radius * 0.025f);
                b.Tube(path, radii, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }

            // Hide panels between neighbouring poles, each a slightly different hide, sagging inwards.
            var first = b.VertexCount;
            for (int k = 0; k < poles; k++)
            {
                var a = feet[k];
                var c = feet[(k + 1) % poles];
                var colour = Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, g.R());
                // The panel facing +Z is left open as the doorway.
                var mid = (a + c) * 0.5f;
                var open = mid.z > radius * 0.5f && Mathf.Abs(mid.x) < radius * 0.6f;
                var top = b.VertexCount;
                for (int r = 0; r <= rows; r++)
                {
                    var t = r / (float)rows;
                    var sag = Sag(t);
                    var p0 = Vector3.Lerp(a, apex, t);
                    var p1 = Vector3.Lerp(c, apex, t);
                    var ao = Mathf.Lerp(0.7f, 1f, t);
                    var cut = open && t < 0.55f;
                    var n = Vector3.Cross(p1 - p0, apex - p0).normalized;
                    if (Vector3.Dot(n, (p0 + p1) * 0.5f - Vector3.up * p0.y) < 0f) n = -n;
                    b.Vertex(p0 + Inward(p0) * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                    b.Vertex(p1 + Inward(p1) * sag, n, cut ? new Color(0.02f, 0.017f, 0.015f) : colour, FloraPart.Bark, cut ? 0.2f : ao);
                }
                for (int r = 0; r < rows; r++)
                {
                    int i0 = top + r * 2;
                    b.Triangle(i0, i0 + 2, i0 + 1);
                    b.Triangle(i0 + 1, i0 + 2, i0 + 3);
                }
            }
            // The panels are hide (W-113): cloth's snow, held still on the poles, but leather's mottle and stitched seams
            // instead of W-99's linen weave (the tents read as linen).
            b.Hide(first);
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.3f));
            DoorMat(g, radius, radius * 0.7f);

            // Collision follows the slope (W-43): stepped rings, each as wide as the hide half a metre below its top, so
            // a body stops where the slope meets it about the chest, not with its head through the hide (a small goblin
            // tent too). (With none, the whole tent was one fat trunk cylinder; its hides' support grid gave the climb
            // logic a "ledge" all round it, so it is left out of the support grid: FloraGenerator.Grow.)
            foreach (var top in new[] { 1.1f, 2f, 3f })
                if (top < apex.y - 0.5f)
                    g.Colliders.Add(new Vector4(apex.x * top / apex.y, apex.z * top / apex.y, radius * 0.95f * (1f - (top - 0.5f) / apex.y), top));
        }

        /// <summary>
        /// Where a gabled roof slab's eave edge starts (its centre line's height, <paramref name="overhang"/> past the
        /// wall) so that its underside meets the wall top at <paramref name="front"/> (4 cm into it), rising to
        /// <paramref name="ridgeY"/> over the middle (W-43: a fixed drop left the slab floating up to half a metre above the
        /// wall on a steep roof).
        /// </summary>
        static float EaveStart(float wallTop, float front, float overhang, float ridgeY, float thickness)
        {
            var slope = (ridgeY - wallTop) / front;
            var atWall = wallTop;
            for (int k = 0; k < 4; k++)
            {
                atWall = wallTop - 0.04f + thickness * 0.5f * Mathf.Sqrt(1f + slope * slope);
                slope = (ridgeY - atWall) / front;
            }
            return atWall - slope * overhang;
        }

        /// <summary>
        /// A chimney's top (W-43), on a square stack of half width <paramref name="half"/> ending at
        /// <paramref name="top"/>: a collar a little proud of the stack, a chamfer sloping in like the roofs' bevels,
        /// and a dark square set down inside it for the flue. Draws no random numbers, so nothing after it changes.
        /// </summary>
        static void ChimneyTop(Grower g, Vector3 top, float half, Color stone)
        {
            g.Chimneys.Add(top + Vector3.up * 0.1f);
            if (g.Lod == 2)
                return;
            var b = g.B;
            b.Box(top + Vector3.down * 0.09f, new Vector3(half * 2f + 0.12f, 0.18f, half * 2f + 0.12f), Quaternion.identity, stone * 0.95f, FloraPart.Bark);
            var chamfer = b.VertexCount;
            b.Tube(new List<Vector3> { top, top + Vector3.up * 0.1f }, new List<float> { (half + 0.06f) * 1.414f, half * 0.6f * 1.414f }, 4, stone * 0.9f, FloraPart.Bark);
            RotateAround(b, chamfer, top, Quaternion.Euler(0f, 45f, 0f));
            b.Box(top + Vector3.up * 0.03f, new Vector3(half * 1.2f, 0.06f, half * 1.2f), Quaternion.identity, new Color(0.025f, 0.02f, 0.018f), FloraPart.Bark, 0.2f);
        }

        static void Tower(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var walls = height * 0.62f;
            g.Height = height;
            g.Width = radius * 3f;
            g.TrunkRadius = radius;
            // Collision only up to the eaves (the trunk cylinder would reach the roof's peak and push anyone on the roof
            // off it, F-36); the roof itself is stood on through its support grid.
            g.Colliders.Add(new Vector4(0f, 0f, radius, walls));
            var sides = g.Sides(12, 8, 6);

            // Walls taper gently, flaring at the foot like a root.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * walls * 0.15f, Vector3.up * walls * 0.6f, Vector3.up * walls },
                   new List<float> { radius * 1.35f, radius * 1.05f, radius * 0.9f, radius * 0.85f }, sides, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, walls * 0.3f));

            // Balconies: thin rings round the walls.
            var floors = Mathf.Clamp(g.Count(s.Branches), 1, 4);
            for (int f = 1; f <= floors; f++)
            {
                var y = walls * f / (floors + 1f);
                var r = Mathf.Lerp(radius * 1.05f, radius * 0.88f, y / walls);
                b.Tube(new List<Vector3> { Vector3.up * (y - 0.05f), Vector3.up * (y + 0.05f) }, new List<float> { r * 1.45f, r * 1.45f }, sides, g.Bark * 0.8f, FloraPart.Bark);
                // On the wall at the window's own height (it tapers).
                var wy = y + 0.7f;
                var wr = wy < walls * 0.6f ? Mathf.Lerp(radius * 1.05f, radius * 0.9f, Mathf.InverseLerp(walls * 0.15f, walls * 0.6f, wy))
                                           : Mathf.Lerp(radius * 0.9f, radius * 0.85f, Mathf.InverseLerp(walls * 0.6f, walls, wy));
                Windows(g, wr, wy, 5, 0.35f, f == 1 ? 30f : -1f, sides);
            }

            // A tall, slightly flared roof.
            var roof = b.VertexCount;
            RoofTube(b, walls - 0.2f, radius * 1.25f, sides, g.FoliageColor(), FloraPart.Foliage, height, 0.02f, walls + (height - walls) * 0.25f, radius * 0.7f);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            g.Perches.Add(Vector3.up * height);

            // The door in a porch standing out past the flared foot (1.35 × radius at the ground), so the flare tucks
            // in behind the porch's sides and nothing crosses the opening; the sides run back into the wall above it.
            var doorWidth = Mathf.Min(radius * 0.7f, 1.25f);
            Porch(g, radius * 1.38f + 0.05f, WallBehind(radius * 0.9f, doorWidth), doorWidth, Mathf.Min(walls * 0.4f, HumanDoor), 0.35f,
                  g.Bark, g.FoliageColor(), FloraPart.Foliage);
        }


        // ------------------------------------------------------------------------------------------- settlement structures

        static Color Stone(Grower g) => new Color(0.42f, 0.41f, 0.39f).linear;
        /// <summary>
        /// A mat on the ground before a door facing +Z at <paramref name="front"/>, about the door's width, on about half
        /// of a building's variants (F-41): a rug with a darker border, in an accent colour or a dyed earth tone, laid
        /// as still cloth (not part of the dirt band). Called last in a builder, so its draws don't change the rest.
        /// </summary>
        static void DoorMat(Grower g, float front, float width)
        {
            if (g.Lod >= 2 || g.R() >= 0.5f)
                return;
            var b = g.B;
            var colour = g.HasAccent ? AccentAt(g, 1) : Color.Lerp(new Color(0.42f, 0.16f, 0.1f), new Color(0.2f, 0.25f, 0.38f), g.R());
            var size = new Vector3(Mathf.Max(width * 1.15f, 0.9f), 0.025f, g.Range(0.7f, 0.95f));
            var at = new Vector3(g.Range(-0.08f, 0.08f), 0.015f, front + size.z * 0.5f + 0.05f);
            var turn = Quaternion.Euler(0f, g.Range(-6f, 6f), 0f);
            var first = b.VertexCount;
            b.Box(at, size, turn, colour * 0.55f, FloraPart.Bark);
            b.Box(at + Vector3.up * 0.004f, new Vector3(size.x * 0.82f, size.y, size.z * 0.72f), turn, colour, FloraPart.Bark);
            b.Cloth(first, p => 0f);
        }

        static Color AccentAt(Grower g, int i) => g.S.Accents.Count > 0 ? g.S.Accents[i % g.S.Accents.Count].linear : Color.white;

        static void Pen(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = radius * 2.2f;
            g.TrunkRadius = 0f;
            var postRadius = Mathf.Max(0.05f, height * 0.055f);
            var posts = Mathf.Max(6, g.Count(s.Branches));
            var tops = new List<Vector3>();
            b.Wood = true;   // posts, rails and trough are timber, with its grain (Z-30)
            for (int k = 0; k < posts; k++)
            {
                var a = (k + 0.5f) / posts * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * g.Range(0.97f, 1.03f);
                tops.Add(p);
                var first = b.VertexCount;
                var postTop = p + Vector3.up * height * g.Range(0.95f, 1.08f);
                b.Tube(new List<Vector3> { p + Vector3.down * 0.1f, postTop },
                       new List<float> { postRadius, postRadius * 0.9f }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark, cap: true);
                if (k % 2 == 0)
                    g.Perches.Add(postTop);
                b.Occlude(first, (q, n) => GroundOcclusion(q, height * 0.5f));
            }
            // Two rails between neighbouring posts, except across the gate (+Z).
            for (int k = 0; k < posts; k++)
            {
                var p = tops[k];
                var q = tops[(k + 1) % posts];
                var mid = (p + q) * 0.5f;
                if (mid.z > radius * 0.85f)
                    continue;
                foreach (var y in new[] { 0.45f, 0.85f })
                    b.Beam(p + Vector3.up * height * y, q + Vector3.up * height * y, postRadius * 1.2f, postRadius * 0.9f, g.Bark * 0.9f, FloraPart.Bark);
            }
            // A trough inside.
            b.Box(new Vector3(-radius * 0.45f, 0.22f, -radius * 0.35f), new Vector3(1.4f, 0.44f, 0.5f), Quaternion.Euler(0f, 30f, 0f), g.Bark * 0.7f, FloraPart.Bark);
            b.Wood = false;
            // The rails stop the player (all but the gate).
            for (int k = 0; k < posts; k++)
            {
                var p = tops[k];
                var q = tops[(k + 1) % posts];
                if ((p + q).z * 0.5f <= radius * 0.85f)
                    FenceColliders(g, p, q, height);
            }
        }

        /// <summary>
        /// Colliders along a fence line from a to b: small upright cylinders about 0.9 m apart, close enough that the
        /// player (whose own radius adds to theirs) can't slip between them, even sprinting at an angle.
        /// </summary>
        static void FenceColliders(Grower g, Vector3 a, Vector3 b, float height)
        {
            var count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 0.9f));
            for (int k = 0; k <= count; k++)
            {
                var p = Vector3.Lerp(a, b, k / (float)count);
                g.Colliders.Add(new Vector4(p.x, p.z, 0.25f, height));
            }
        }

        static void Stall(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var width = height * s.Spread;
            var depth = width * 0.7f;
            g.Height = height;
            g.Width = Mathf.Max(width, depth) * 1.3f;
            g.TrunkRadius = 0f;
            var post = 0.06f;
            var frontHeight = height * 0.82f;
            // Posts and counter are timber, with its grain (Z-30).
            b.Wood = true;
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var z in new[] { -0.5f, 0.5f })
            {
                var top = z > 0f ? frontHeight : height;
                b.Tube(new List<Vector3> { new Vector3(x * width, -0.05f, z * depth), new Vector3(x * width, top, z * depth) },
                       new List<float> { post, post }, g.Sides(5, 4, 3), g.Bark, FloraPart.Bark);
            }
            // Counter across the front, and goods laid out on it.
            b.Box(new Vector3(0f, 0.45f, depth * 0.5f - 0.15f), new Vector3(width * 0.95f, 0.9f, 0.45f), Quaternion.identity, g.Bark * 0.8f, FloraPart.Bark);
            b.Wood = false;
            if (g.Lod < 2)
                for (int k = 0; k < 7; k++)
                {
                    var at = new Vector3(Mathf.Lerp(-0.4f, 0.4f, k / 6f) * width, 0.98f, depth * 0.5f - 0.15f + g.Range(-0.08f, 0.08f));
                    b.Blob(at, Vector3.one * g.Range(0.07f, 0.12f), Quaternion.identity, 0, 0.2f, g.R() * 50f, AccentAt(g, k + 1), FloraPart.Bark);
                }
            // A striped awning sloping from the back down over the front, overhanging it.
            var back = new Vector3(0f, height + 0.05f, -depth * 0.55f);
            var front = new Vector3(0f, frontHeight - 0.15f, depth * 0.5f + 0.45f);
            // Cloth held at both bars, so it only ripples between them.
            var stripes = g.Sides(6, 4, 2);
            var awning = b.VertexCount;
            for (int k = 0; k < stripes; k++)
            {
                var x = k / (float)stripes - 0.5f;
                var colour = k % 2 == 0 ? g.FoliageColor() : AccentAt(g, 0);
                b.Sheet(back + Vector3.right * x * width * 1.1f, Vector3.right * width * 1.1f / stripes, front - back, 1, g.Sides(5, 2, 1), colour, FloraPart.Bark);
            }
            b.Cloth(awning, p => 0.3f * Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(back.z, front.z, p.z)) * Mathf.PI), taut: true);
        }

        static void Inn(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            var front = half * 0.8f;
            var wall = height * 0.6f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 0.9f;
            // Collision only up to the eaves (the trunk cylinder would reach the roof's peak and push anyone on the roof
            // off it, F-36); the roof itself is stood on through its support grid.
            g.Colliders.Add(new Vector4(0f, 0f, half * 0.9f, wall));

            // Walls, with a timber band between the storeys and dark corner posts.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, wall * 0.5f - 0.1f, 0f), new Vector3(half * 2f, wall + 0.2f, front * 2f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.4f));
            var timber = g.Bark * 0.5f;
            b.Wood = true;   // the timber band and corner posts (Z-30)
            if (g.Lod < 2)
            {
                b.Box(new Vector3(0f, wall * 0.5f, 0f), new Vector3(half * 2f + 0.08f, 0.2f, front * 2f + 0.08f), Quaternion.identity, timber, FloraPart.Bark);
                foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -1f, 1f })
                    b.Box(new Vector3(x * half, wall * 0.5f, z * front), new Vector3(0.25f, wall, 0.25f), Quaternion.identity, timber, FloraPart.Bark);
            }
            b.Wood = false;

            // Gabled roof: two slabs meeting at a ridge along X, and the gable ends filled in.
            var roof = g.FoliageColor();
            var ridge = height;
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, EaveStart(wall, front, 0.6f, ridge + 0.1f, 0.18f), z * (front + 0.6f)), new Vector3(0f, ridge + 0.1f, 0f), half * 2f + 0.8f, 0.18f, roof, FloraPart.Bark);
            // A rounded ridge tile along the top, so the slabs meet in a soft bevel, not a hard point (Z-41).
            b.Tube(new List<Vector3> { new Vector3(-half - 0.4f, ridge + 0.13f, 0f), new Vector3(half + 0.4f, ridge + 0.13f, 0f) },
                   new List<float> { 0.15f, 0.15f }, g.Sides(6, 4, 3), roof * 0.92f, FloraPart.Bark, cap: true);
            foreach (var x in new[] { -1f, 1f })
            {
                var v0 = b.Vertex(new Vector3(x * half, wall, -front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v1 = b.Vertex(new Vector3(x * half, wall, front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v2 = b.Vertex(new Vector3(x * half, ridge, 0f), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                b.Triangle(v0, v1, v2);
            }
            b.Box(new Vector3(half * 0.6f, height * 0.85f, -front * 0.35f), new Vector3(0.6f, height * 0.5f, 0.6f), Quaternion.identity, Stone(g), FloraPart.Bark);
            ChimneyTop(g, new Vector3(half * 0.6f, height * 1.1f, -front * 0.35f), 0.3f, Stone(g));
            g.Perches.Add(new Vector3(-half - 0.2f, ridge + 0.29f, 0f));
            g.Perches.Add(new Vector3(half + 0.2f, ridge + 0.29f, 0f));
            g.Perches.Add(new Vector3(half * 0.6f, height * 1.1f, -front * 0.35f));

            // Door, windows (lit at night) and the porch out front.
            Doorway(g, front + 0.02f, 1.25f, Mathf.Min(wall * 0.42f, HumanDoor));
            if (g.Lod < 2)
                foreach (var (x, y) in new[] { (-0.55f, 0.27f), (0.55f, 0.27f), (-0.55f, 0.75f), (0f, 0.75f), (0.55f, 0.75f) })
                    Window(g, new Vector3(x * half, y * wall, front + 0.03f), Quaternion.identity, 0.6f, 0.75f);
            var porch = 2.4f;
            b.Wood = true;   // the porch deck and its posts (Z-30)
            b.Box(new Vector3(0f, 0.15f, front + porch * 0.5f), new Vector3(half * 1.8f, 0.3f, porch), Quaternion.identity, g.Bark * 0.75f, FloraPart.Bark);
            b.Wood = false;
            // W-100: the porch roof half as high again at its front edge (it hung at head height), and only as steep as
            // still clears the upper windows' sills where it meets the wall.
            b.Beam(new Vector3(0f, wall * 0.66f, front), new Vector3(0f, wall * 0.6f, front + porch + 0.3f), half * 1.9f, 0.12f, roof, FloraPart.Bark);
            b.Wood = true;
            foreach (var x in new[] { -0.85f, 0.85f })
                b.Tube(new List<Vector3> { new Vector3(x * half, 0.3f, front + porch - 0.1f), new Vector3(x * half, wall * 0.61f, front + porch - 0.1f) },
                       new List<float> { 0.1f, 0.1f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
            b.Wood = false;
            // A sign hanging from the porch roof.
            if (g.Lod < 2)
                b.Box(new Vector3(half * 0.55f, wall * 0.45f, front + porch + 0.2f), new Vector3(0.9f, 0.55f, 0.06f), Quaternion.identity, AccentAt(g, 1), FloraPart.Bark);
        }

        /// <summary>
        /// A leader's hall, its door facing +Z: a long timber hall on a stone plinth, corner and wall posts, a steep
        /// gabled roof with its ridge along X and crossed beams at each gable, a wide door up two steps, lit windows (first
        /// accent) along the front and back, and two banners (second accent) flanking the door. Collision covers its
        /// length.
        /// </summary>
        static void Hall(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;      // half its length (along X)
            var front = half * 0.55f;                 // half its depth (along Z)
            var plinth = 0.6f;
            var wall = plinth + height * 0.38f;
            g.Height = height;
            g.Width = half * 2.4f;
            g.TrunkRadius = front;
            var timber = g.Bark * 0.5f;
            var stone = Stone(g) * g.Range(0.9f, 1.1f);

            // Stone plinth, then timber walls with posts.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, plinth * 0.5f - 0.15f, 0f), new Vector3(half * 2f + 0.4f, plinth + 0.3f, front * 2f + 0.4f), Quaternion.identity, stone, FloraPart.Bark);
            b.Wood = true;   // timber walls, posts and the wall plate (Z-30)
            b.Box(new Vector3(0f, (plinth + wall) * 0.5f, 0f), new Vector3(half * 2f, wall - plinth, front * 2f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.4f));
            if (g.Lod < 2)
            {
                var posts = Mathf.Max(2, Mathf.RoundToInt(half / 1.6f));
                for (int k = 0; k <= posts; k++)
                {
                    var x = Mathf.Lerp(-half, half, k / (float)posts);
                    foreach (var z in new[] { -1f, 1f })
                        b.Box(new Vector3(x, (plinth + wall) * 0.5f, z * (front + 0.06f)), new Vector3(0.28f, wall - plinth, 0.2f), Quaternion.identity, timber, FloraPart.Bark);
                }
                b.Box(new Vector3(0f, wall - 0.12f, 0f), new Vector3(half * 2f + 0.12f, 0.25f, front * 2f + 0.12f), Quaternion.identity, timber, FloraPart.Bark);
            }
            b.Wood = false;

            // Steep gabled roof along X, overhanging; gable ends filled; crossed beams rising past the ridge at each end.
            var roof = g.FoliageColor();
            var ridge = height;
            foreach (var z in new[] { -1f, 1f })
                b.Beam(new Vector3(0f, EaveStart(wall, front, 0.9f, ridge + 0.1f, 0.22f), z * (front + 0.9f)), new Vector3(0f, ridge + 0.1f, 0f), half * 2f + 1.2f, 0.22f, roof, FloraPart.Bark);
            // A rounded ridge tile along the top (Z-41: a soft bevel, not a hard point).
            b.Tube(new List<Vector3> { new Vector3(-half - 0.6f, ridge + 0.14f, 0f), new Vector3(half + 0.6f, ridge + 0.14f, 0f) },
                   new List<float> { 0.18f, 0.18f }, g.Sides(6, 4, 3), roof * 0.92f, FloraPart.Bark, cap: true);
            foreach (var x in new[] { -1f, 1f })
            {
                var v0 = b.Vertex(new Vector3(x * half, wall, -front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v1 = b.Vertex(new Vector3(x * half, wall, front), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                var v2 = b.Vertex(new Vector3(x * half, ridge, 0f), Vector3.right * x, g.Bark, FloraPart.Bark, 0.9f);
                b.Triangle(v0, v1, v2);
                b.Wood = true;
                if (g.Lod < 2)
                    foreach (var z in new[] { -1f, 1f })
                        b.Beam(new Vector3(x * (half + 0.6f), wall + (ridge - wall) * 0.55f, z * front * 0.55f), new Vector3(x * (half + 0.6f), ridge + 1.1f, -z * 0.5f),
                               0.22f, 0.18f, timber, FloraPart.Bark);
                    b.Wood = false;
                g.Perches.Add(new Vector3(x * half * 0.9f, ridge + 0.33f, 0f));
                g.Perches.Add(new Vector3(x * (half + 0.6f), ridge + 1.19f, 0.5f));
            }

            // A wide door up two steps, windows along both long walls, banners either side of the door.
            Door(g, new Vector3(0f, plinth - 0.05f, front + 0.04f), Quaternion.identity, 1.8f, Mathf.Min(wall - plinth - 0.4f, HumanDoor * 1.15f));
            b.Box(new Vector3(0f, plinth * 0.29f, front + 0.75f), new Vector3(3.2f, plinth * 0.75f, 0.8f), Quaternion.identity, stone, FloraPart.Bark);
            var light = AccentAt(g, 0);
            if (g.Lod < 2)
            {
                var windows = Mathf.Max(2, Mathf.RoundToInt(half / 2.2f));
                for (int k = 0; k < windows; k++)
                {
                    var x = Mathf.Lerp(-half * 0.75f, half * 0.75f, (k + 0.5f) / windows);
                    if (Mathf.Abs(x) < 1.6f)
                        continue;   // clear of the door
                    var y = plinth + (wall - plinth) * 0.55f;
                    Window(g, new Vector3(x, y, front + 0.09f), Quaternion.identity, 0.7f, 1f, light);
                    Window(g, new Vector3(x, y, -front - 0.09f), Quaternion.LookRotation(Vector3.back), 0.7f, 1f, light);
                }
                foreach (var x in new[] { -2.2f, 2.2f })
                {
                    var foot = new Vector3(x, 0f, front + 1.4f);
                    b.Wood = true;
                    b.Tube(new List<Vector3> { foot + Vector3.down * 0.2f, foot + Vector3.up * (wall + 1.2f) }, new List<float> { 0.08f, 0.06f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
                    b.Wood = false;
                    g.Perches.Add(foot + Vector3.up * (wall + 1.2f));
                    // A banner hung from a short bar near the top of the pole, free to wave below it.
                    var top = wall + 1.1f;
                    var banner = b.VertexCount;
                    b.Sheet(foot + new Vector3(x * 0.12f - 0.45f, top, 0f), Vector3.right * 0.9f, Vector3.down * 1.8f, g.Sides(4, 1, 1), g.Sides(6, 2, 1), AccentAt(g, 1), FloraPart.Bark);
                    b.Cloth(banner, p => (foot.y + top - p.y) / 1.8f);
                    b.Wood = true;
                    b.Box(foot + new Vector3(x * 0.12f, top, 0f), new Vector3(1.0f, 0.05f, 0.05f), Quaternion.identity, timber, FloraPart.Bark);
                    b.Wood = false;
                }
            }

            // Collision: a row of cylinders along its length.
            var count = Mathf.Max(2, Mathf.CeilToInt(half / front));
            for (int k = 0; k < count; k++)
                g.Colliders.Add(new Vector4(Mathf.Lerp(-half + front * 0.9f, half - front * 0.9f, count == 1 ? 0.5f : k / (count - 1f)), 0f, front * 1.05f, wall));
        }

        /// <summary>
        /// A shrine, facing +Z: a stepped stone platform, four posts carrying a small pyramid roof, and an altar under it
        /// with a stone that glows in the accent colour, a pair of offering bowls in front.
        /// </summary>
        static void Shrine(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = half * 2.6f;
            g.TrunkRadius = half * 0.5f;
            var stone = Stone(g) * g.Range(0.95f, 1.15f);
            var timber = g.Bark * 0.6f;

            // Two stone steps up to the platform.
            var first = b.VertexCount;
            b.Box(new Vector3(0f, 0.1f, 0f), new Vector3(half * 2.4f, 0.45f, half * 2.4f), Quaternion.identity, stone * 0.92f, FloraPart.Bark);
            b.Box(new Vector3(0f, 0.38f, 0f), new Vector3(half * 2f, 0.25f, half * 2f), Quaternion.identity, stone, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.5f));
            var floor = 0.5f;
            var eave = floor + height * 0.55f;
            // Posts and the roof.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Tube(new List<Vector3> { new Vector3(x * half * 0.82f, floor, z * half * 0.82f), new Vector3(x * half * 0.82f, eave, z * half * 0.82f) },
                       new List<float> { 0.12f, 0.1f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
            var roof = b.VertexCount;
            RoofTube(b, eave - 0.22f, half * 1.45f, 4, g.FoliageColor(), FloraPart.Bark, height, 0.05f, 0f, 0f);
            RotateFrom(b, roof, Quaternion.Euler(0f, 45f, 0f));
            g.Perches.Add(Vector3.up * height);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            // The altar and its glowing stone.
            b.Box(new Vector3(0f, floor + 0.45f, 0f), new Vector3(half * 0.9f, 0.9f, half * 0.6f), Quaternion.identity, stone * 1.1f, FloraPart.Bark);
            b.Blob(new Vector3(0f, floor + 1.1f, 0f), new Vector3(0.22f, 0.26f, 0.22f), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.15f, 3f, g.Accent, FloraPart.Glow);
            if (g.Lod == 0)
                foreach (var x in new[] { -0.45f, 0.45f })
                    b.Tube(new List<Vector3> { new Vector3(x, floor + 0.02f, half * 0.55f), new Vector3(x, floor + 0.14f, half * 0.55f) },
                           new List<float> { 0.1f, 0.14f }, 6, stone * 0.8f, FloraPart.Bark, cap: true);
        }

        /// <summary>
        /// A garden, its gate facing +Z: a low wattle fence (posts and two rails) round a square with a gap at the front,
        /// rows of vegetables across it (leafy clumps in the foliage colours), and a few flowers in the accents.
        /// </summary>
        static void Garden(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var fence = g.Range(s.Height);
            var half = fence * s.Spread * 0.5f;
            g.Height = fence;
            g.Width = half * 2f;
            g.TrunkRadius = 0f;
            var wood = g.Bark;

            // The fence: posts round the square, rails between them, a gap in the middle of the front.
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                g.Perches.Add(new Vector3(x * half, fence * 0.925f, z * half));
            if (g.Lod < 2)
            {
                var posts = Mathf.Max(3, Mathf.RoundToInt(half / 0.9f));
                for (int side = 0; side < 4; side++)
                {
                    var rot = Quaternion.Euler(0f, side * 90f, 0f);
                    for (int k = 0; k <= posts; k++)
                    {
                        var x = Mathf.Lerp(-half, half, k / (float)posts);
                        if (side == 0 && Mathf.Abs(x) < 0.7f)
                            continue;   // the gate gap
                        b.Box(rot * new Vector3(x, fence * 0.45f, half), new Vector3(0.08f, fence * 0.95f, 0.08f), rot, wood * g.Range(0.85f, 1.1f), FloraPart.Bark);
                    }
                    if (g.Lod == 0)
                    {
                        // The fence stops the player, except at the gate.
                        if (side == 0)
                            foreach (var dir in new[] { -1f, 1f })
                                FenceColliders(g, rot * new Vector3(dir * half, 0f, half), rot * new Vector3(dir * 0.75f, 0f, half), fence);
                        else
                            FenceColliders(g, rot * new Vector3(-half, 0f, half), rot * new Vector3(half, 0f, half), fence);
                    }
                    foreach (var y in new[] { fence * 0.35f, fence * 0.75f })
                    {
                        if (side == 0)
                        {
                            foreach (var dir in new[] { -1f, 1f })
                                b.Box(rot * new Vector3(dir * (half + 0.7f) * 0.5f, y, half), new Vector3(half - 0.7f, 0.05f, 0.05f), rot, wood, FloraPart.Bark);
                        }
                        else
                            b.Box(rot * new Vector3(0f, y, half), new Vector3(half * 2f, 0.05f, 0.05f), rot, wood, FloraPart.Bark);
                    }
                }
            }

            // Rows of vegetables, running front to back, with a path down the middle from the gate.
            var rows = Mathf.Max(2, Mathf.RoundToInt(half / 0.7f));
            for (int r = 0; r < rows; r++)
            {
                var x = Mathf.Lerp(-half + 0.5f, half - 0.5f, r / (rows - 1f));
                if (Mathf.Abs(x) < 0.45f)
                    continue;
                var count = Mathf.Max(2, Mathf.RoundToInt((half * 2f - 1f) / (g.Lod == 2 ? 1.4f : 0.6f)));
                for (int k = 0; k < count; k++)
                {
                    var z = Mathf.Lerp(-half + 0.5f, half - 0.6f, k / (count - 1f));
                    var size = g.Range(0.18f, 0.3f);
                    var color = g.FoliageColor();
                    if (g.HasAccent && g.R() < s.AccentAmount * 0.2f)
                        color = g.Accent;
                    b.Blob(new Vector3(x + g.Range(-0.06f, 0.06f), size * 0.6f, z), new Vector3(size, size * 0.8f, size), Quaternion.identity, g.Lod == 0 ? 1 : 0, 0.3f, g.R() * 50f, color, FloraPart.Foliage);
                }
            }
        }
    }
}
