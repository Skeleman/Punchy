using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, village buildings: tents, towers, pens, stalls, inns, halls, shrines, gardens. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        // W-175 row 3: the tent is a unit of library parts (FloraGenerator.Units.TentUnit).
        static void Tent(Grower g) => BuildUnit(g, TentUnit);

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
            // P-51 row 2: walked into (with Seamless on), the tower is hollow: kit boxes for its wall, floors and stair
            // (TowerRooms) in place of the solid cylinder, and no trunk to climb.
            var walkIn = Punchy.Terrain.StructureKit.WalkInTower;
            g.TrunkRadius = walkIn ? 0f : radius;
            // Collision only up to the eaves (the trunk cylinder would reach the roof's peak and push anyone on the roof
            // off it, F-36); the roof itself is stood on through its support grid.
            if (!walkIn)
                g.Colliders.Add(new Vector4(0f, 0f, radius, walls));
            var sides = g.Sides(12, 8, 6);
            var doorWidth = Mathf.Min(radius * 0.7f, 1.25f);
            var doorHeight = Mathf.Min(walls * 0.4f, HumanDoor);

            // Walls taper gently, flaring at the foot like a root.
            var first = b.VertexCount;
            if (walkIn && g.Lod < 2)
                TowerShell(g, radius, walls, sides, doorWidth, doorHeight);
            else
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
                // (A walk-in tower has its own, at its storeys: TowerRooms.)
                if (!walkIn)
                    Windows(g, wr, wy, 5, 0.35f, f == 1 ? 30f : -1f, sides);
            }

            // A tall, slightly flared roof; walked into, lifted on posts over a roof deck at the eaves (P-51 row 3).
            var roof = b.VertexCount;
            var eaves = walkIn ? walls + TowerLookout : walls;
            RoofTube(b, eaves - 0.2f, radius * 1.25f, sides, g.FoliageColor(), FloraPart.Foliage, height, 0.02f, eaves + (height - eaves) * 0.25f, radius * 0.7f);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            g.Perches.Add(Vector3.up * height);

            // The door in a porch standing out past the flared foot (1.35 × radius at the ground), so the flare tucks
            // in behind the porch's sides and nothing crosses the opening; the sides run back into the wall above it.
            var doorZ = radius * 1.38f + 0.05f;
            if (!walkIn)
                Porch(g, doorZ, WallBehind(radius * 0.9f, doorWidth), doorWidth, doorHeight, 0.35f, g.Bark, g.FoliageColor(), FloraPart.Foliage);
            else
            {
                TowerPorch(g, radius, walls, doorZ, WallBehind(radius * 0.9f, doorWidth), doorWidth, doorHeight, 0.35f, g.Bark, g.FoliageColor(), FloraPart.Foliage);
                TowerRooms(g, radius, walls, doorWidth, doorHeight, doorZ);
            }
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

        // W-175: the market stall is a unit of library parts (FloraGenerator.Units.StallUnit).
        static void Stall(Grower g) => BuildUnit(g, StallUnit);

        static void Inn(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            var front = half * 0.8f;
            var wall = height * 0.6f;
            const float porch = 2.4f;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 0.9f;
            // Creatures still stop at a circle up to the eaves; the player skips it for the kit boxes.
            g.Colliders.Add(new Vector4(0f, 0f, half * 0.9f, wall));

            // W-128: the walls, roof, porch and sign are kit pieces in the folk's trim and looks (StructureKit.Inn), on the
            // old box's footprint, so the door, the interior spots and the collider stay where they were.
            var seed = (uint)(g.R() * 16777215f) ^ ((uint)g.Variant << 24) ^ (uint)s.Seed * 0x9E3779B9u;
            var trim = Punchy.Terrain.StructureKit.For(s.KitCulture);
            var parts = Punchy.Terrain.StructureKit.Inn(seed, trim, half, front, wall, porch, out var sign);
            var looks = Punchy.Terrain.StructureKit.Surfaces(s.KitCulture, seed);
            g.Height = height;
            DrawKitHouse(g, parts, trim, looks, seed);

            // The door the folk use (hinged, with its frame and step), in the middle bay of the front.
            Doorway(g, front + 0.02f, 1.25f, Mathf.Min(wall * 0.42f, HumanDoor));
            // The sign, hanging on its rods under the porch roof's front edge.
            if (g.Lod < 2)
                b.Box(sign, new Vector3(0.9f, 0.55f, 0.06f), Quaternion.identity, AccentAt(g, 1), FloraPart.Bark);
        }

        /// <summary>
        /// A leader's hall, its door facing +Z: kit parts (StructureKit.Hall) on a stone plinth under a steep gable with
        /// crossed beams at each end, a wide door up a step, lit windows (first accent) along both long walls and two
        /// banners (second accent) flanking the door.
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
            var ridge = height;
            g.Height = height;
            g.Width = half * 2.4f;
            g.TrunkRadius = front;
            var timber = g.Bark * 0.5f;
            var stone = Stone(g) * g.Range(0.9f, 1.1f);

            var seed = (uint)(g.R() * 16777215f) ^ ((uint)g.Variant << 24) ^ (uint)s.Seed * 0x9E3779B9u;
            var trim = Punchy.Terrain.StructureKit.For(s.KitCulture);
            trim.Wood = g.Bark;
            trim.Trim = timber;
            trim.RoofColour = g.FoliageColor();
            // Colour(Stone) is Lerp(Wall, Trim, 0.5) * 0.85: solve Wall so the plinth comes out the old stone.
            trim.Wall = stone * (2f / 0.85f) - timber;
            trim.Wall.a = 1f;
            var parts = Punchy.Terrain.StructureKit.Hall(trim, half, front, plinth, wall, ridge);
            DrawKitHouse(g, parts, trim, Punchy.Terrain.StructureKit.Surfaces(s.KitCulture, seed), seed);
            foreach (var x in new[] { -1f, 1f })
                g.Perches.Add(new Vector3(x * (half + 0.6f), ridge + 1.19f, 0.5f));

            Door(g, new Vector3(0f, plinth - 0.05f, front + 0.04f), Quaternion.identity, 1.8f, Mathf.Min(wall - plinth - 0.4f, HumanDoor * 1.15f));
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

            // Creatures still stop at a row of circles; the player skips them for the kit boxes.
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
            b.Family = Timber(g);   // W-166: timber posts on the stone
            foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Tube(new List<Vector3> { new Vector3(x * half * 0.82f, floor, z * half * 0.82f), new Vector3(x * half * 0.82f, eave, z * half * 0.82f) },
                       new List<float> { 0.12f, 0.1f }, g.Sides(6, 4, 3), timber, FloraPart.Bark);
            b.Family = b.BaseFamily;
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
            b.Family = Timber(g);   // W-166: the fence is wood
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

            b.Family = b.BaseFamily;

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
