using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// P-51 row 2 (#337): the elf tower walked into (StructureKit.WalkInTower, with Seamless on). Its wall is a shell
    /// with the doorway cut through it under the porch, lined inside. Its storeys stand about 3.2 m apart. A stair climbs
    /// round the inside of the wall, and each floor is open over the flight that comes up through it. Lining, floors and
    /// stair are drawn only at the two nearest detail levels (rule 20). At the nearest they are also kit boxes (what you
    /// see is what you stand on), as the walk-in kit houses are. Only the windows draw random numbers (Window), and they come
    /// last, so the tower's own draws are unchanged; worlds without -seamless build the tower as before (Tower's other branch).
    /// </summary>
    public static partial class FloraGenerator
    {
        /// <summary>Each stair rise (m, before the building's scale: about 1.1 at most, so under the 0.28 m a body climbs).</summary>
        const float TowerRise = 0.22f;
        const float TowerTread = 0.3f;
        const float TowerStairWidth = 0.95f;
        /// <summary>How thick the wall's colliders are (both faces: the room's lining and the outside).</summary>
        const float TowerWallBox = 0.25f;
        /// <summary>Where the first flight starts (degrees, +Z, the door, at 90): clear of the doorway, rising anticlockwise.</summary>
        const float TowerStairStart = 140f;
        /// <summary>P-51 row 3: how far over the eaves (the roof deck) the lifted roof's rim stands, on posts on the parapet.</summary>
        const float TowerLookout = 2.3f;
        const float TowerMerlon = 1f, TowerCrenel = 0.7f;
        /// <summary>The storeys' windows (the kit's shared Window): pane size, and its sill's height over the floor.</summary>
        const float TowerWindowWidth = 0.5f, TowerWindowHeight = 0.8f, TowerSill = 1.05f;
        /// <summary>How much of the top flight the roof deck leaves open (its hatch): the treads within this of the deck.</summary>
        const float TowerHatchDrop = 2.15f;

        /// <summary>One flight of the stair: its first and last tread's angles, the floors it runs between, its rise.</summary>
        struct TowerFlight
        {
            public float Start, End, Below, Level, Rise, Tread;
            public int Steps;
        }

        /// <summary>
        /// The stair's flights (the same sums TowerRooms draws them by; no random numbers): one up to each storey's floor
        /// and the last up to the roof deck at the eaves.
        /// </summary>
        static List<TowerFlight> TowerFlights(float radius, float walls)
        {
            var room = TowerInner(radius);
            var storeys = Mathf.Clamp(Mathf.FloorToInt(walls / 3.1f), 2, 3);
            var tread = TowerTread / (room - TowerStairWidth * 0.5f) * Mathf.Rad2Deg;
            var flights = new List<TowerFlight>();
            var start = TowerStairStart;
            var below = 0.04f;
            for (int k = 1; k <= storeys; k++)
            {
                var level = walls * k / storeys;
                var steps = Mathf.CeilToInt((level - below) / TowerRise);
                var f = new TowerFlight { Start = start, End = start + (steps - 1) * tread, Below = below, Level = level, Rise = (level - below) / steps, Tread = tread, Steps = steps };
                flights.Add(f);
                start = f.End + 15f;
                below = level;
            }
            return flights;
        }

        /// <summary>
        /// P-51 row 3: the windows of the walk-in tower, two on each storey (angle in degrees, centre height): clear of the
        /// doorway and of the flight climbing past them, turned a little from storey to storey. The lining has a hole at
        /// each (TowerShell) and TowerRooms sets the kit's Window in it.
        /// </summary>
        static List<Vector2> TowerWindowSpots(float radius, float walls)
        {
            var room = TowerInner(radius);
            var half = Mathf.Asin(Mathf.Clamp01((TowerWindowWidth * 0.5f + 0.1f) / room)) * Mathf.Rad2Deg;
            var spots = new List<Vector2>();
            var flights = TowerFlights(radius, walls);
            for (int k = 0; k < flights.Count; k++)
            {
                var f = flights[k];
                var y = f.Below + TowerSill + TowerWindowHeight * 0.5f;
                for (int w = 0; w < 2; w++)
                {
                    var a = 270f + 70f * k + 180f * w;
                    for (int tries = 0; tries < 18 && Blocked(a); tries++)
                        a += 20f;
                    if (!Blocked(a))
                        spots.Add(new Vector2(Mathf.Repeat(a, 360f), y));
                }

                bool Blocked(float a)
                {
                    // Clear of the doorway, on every storey (the lining's rings break there).
                    if (Mathf.Abs(Mathf.DeltaAngle(a, 90f)) < 55f)
                        return true;
                    foreach (var p in spots)
                        if (p.y == y && Mathf.Abs(Mathf.DeltaAngle(p.x, a)) < half * 2f + 30f)
                            return true;
                    // A tread of the flight up from this floor in front of it (each 0.4 m deep, its top at its step).
                    for (int s = 1; s < f.Steps; s++)
                    {
                        if (Mathf.Abs(Mathf.DeltaAngle(f.Start + (s - 0.5f) * f.Tread, a)) > half + f.Tread)
                            continue;
                        var top = f.Below + s * f.Rise;
                        if (top > y - TowerWindowHeight * 0.5f - 0.15f && top - 0.4f < y + TowerWindowHeight * 0.5f + 0.15f)
                            return true;
                    }
                    return false;
                }
            }
            return spots;
        }

        /// <summary>The window's frame bar (as Window sizes it).</summary>
        static float TowerWindowBar => Mathf.Clamp(TowerWindowWidth * 0.1f, 0.04f, 0.08f);

        /// <summary>The holes the windows leave in the lining: (from, to) degrees and (bottom, top), inside the frames' bars.</summary>
        static List<Vector4> TowerWindowHoles(float radius, float walls)
        {
            var inner = TowerInner(radius);
            var bar = TowerWindowBar;
            var half = Mathf.Asin(Mathf.Clamp01((TowerWindowWidth + bar) * 0.5f / inner)) * Mathf.Rad2Deg;
            var holes = new List<Vector4>();
            foreach (var s in TowerWindowSpots(radius, walls))
                holes.Add(new Vector4(s.x - half, s.x + half, s.y - (TowerWindowHeight + bar) * 0.5f, s.y + (TowerWindowHeight + bar) * 0.5f));
            return holes;
        }

        /// <summary>The room's radius: the eaves' wall less its thickness.</summary>
        static float TowerInner(float radius) => radius * 0.85f - 0.25f;
        static float TowerPost(float doorWidth) => Mathf.Clamp(doorWidth * 0.12f, 0.1f, 0.18f);
        /// <summary>Half the doorway cut through the wall: to the porch cheeks' inner faces (1 cm wider, so they show in front).</summary>
        static float TowerGap(float doorWidth) => doorWidth * 0.5f + TowerPost(doorWidth) + 0.01f;
        /// <summary>The cut's top: the porch lintel's underside.</summary>
        static float TowerGapTop(float doorWidth, float doorHeight) => doorHeight + TowerPost(doorWidth) + 0.08f;

        /// <summary>The tower wall's radius at a height: the same profile as Tower's tube.</summary>
        static float TowerProfile(float radius, float walls, float y)
        {
            if (y < walls * 0.15f)
                return Mathf.Lerp(radius * 1.35f, radius * 1.05f, Mathf.InverseLerp(-0.1f, walls * 0.15f, y));
            if (y < walls * 0.6f)
                return Mathf.Lerp(radius * 1.05f, radius * 0.9f, Mathf.InverseLerp(walls * 0.15f, walls * 0.6f, y));
            return Mathf.Lerp(radius * 0.9f, radius * 0.85f, Mathf.InverseLerp(walls * 0.6f, walls, y));
        }

        /// <summary>
        /// The angles (degrees, +Z at 90) of a ring's points round the solid part of a wall of radius <paramref name="r"/>:
        /// from the doorway's left edge (x = -gap) round to its right edge (x = +gap), with the regular facets between.
        /// Facets are left out across the widest gap (at <paramref name="narrowest"/>), so every ring of one wall has as
        /// many points.
        /// </summary>
        static List<float> TowerRingAngles(float r, float gap, int sides, float narrowest)
        {
            var edge = Mathf.Acos(Mathf.Clamp(gap / r, -1f, 1f)) * Mathf.Rad2Deg;
            var clear = Mathf.Acos(Mathf.Clamp(gap / narrowest, -1f, 1f)) * Mathf.Rad2Deg - 2f;
            var angles = new List<float>();
            for (int k = 0; k < sides; k++)
            {
                var a = k * 360f / sides;
                if (a >= clear && a <= 180f - clear)
                    continue;
                angles.Add(a < clear ? a + 360f : a);
            }
            angles.Sort();
            angles.Insert(0, 180f - edge);
            angles.Add(360f + edge);
            return angles;
        }

        static Vector3 TowerAt(float degrees, float r, float y) =>
            new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad) * r, y, Mathf.Sin(degrees * Mathf.Deg2Rad) * r);

        /// <summary>A quad facing along <paramref name="n0"/>/<paramref name="n1"/> (the normals at p0, p3 and at p1, p2), wound to face that way.</summary>
        static void TowerQuad(FloraMeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n0, Vector3 n1, Color c, float ao)
        {
            var i0 = b.Vertex(p0, n0, c, FloraPart.Bark, ao);
            var i1 = b.Vertex(p1, n1, c, FloraPart.Bark, ao);
            var i2 = b.Vertex(p2, n1, c, FloraPart.Bark, ao);
            var i3 = b.Vertex(p3, n0, c, FloraPart.Bark, ao);
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n0 + n1) >= 0f)
            {
                b.Triangle(i0, i1, i2);
                b.Triangle(i0, i2, i3);
            }
            else
            {
                b.Triangle(i0, i2, i1);
                b.Triangle(i0, i3, i2);
            }
        }

        /// <summary>
        /// The walk-in tower's wall (in place of Tower's tube, at the two nearest detail levels): the same tapering profile
        /// with the doorway cut through it below the porch lintel, its lining facing the room, and the cut's sides and top.
        /// </summary>
        static void TowerShell(Grower g, float radius, float walls, int sides, float doorWidth, float doorHeight)
        {
            var b = g.B;
            var gap = TowerGap(doorWidth);
            var top = TowerGapTop(doorWidth, doorHeight);
            var inner = TowerInner(radius);
            var ys = new List<float> { -0.1f, walls * 0.15f, walls * 0.6f, walls };
            if (!ys.Contains(top))
                ys.Add(top);
            ys.Sort();
            WallRings(ys, y => TowerProfile(radius, walls, y), sides, radius * 0.85f, 1f, g.Bark, 1f, null);
            // The lining: a plain cylinder facing in, a little lighter, with a hole at each window (P-51 row 3: the
            // window's frame, set in from outside, runs back through the wall to it).
            var holes = TowerWindowHoles(radius, walls);
            var liningYs = new List<float> { 0f, top, walls };
            foreach (var h in holes)
                foreach (var y in new[] { h.z, h.w })
                    if (!liningYs.Contains(y))
                        liningYs.Add(y);
            liningYs.Sort();
            WallRings(liningYs, y => inner, g.Lod == 0 ? 16 : 12, inner, -1f, g.Bark * 1.25f, 0.8f, holes);

            // The cut's sides and top, from the lining out to the outer face.
            var zIn = Mathf.Sqrt(inner * inner - gap * gap);
            float ZOut(float y) { var r = TowerProfile(radius, walls, y); return Mathf.Sqrt(r * r - gap * gap); }
            var cut = new List<float>();
            foreach (var y in ys)
                if (y <= top)
                    cut.Add(y);
            foreach (var side in new[] { -1f, 1f })
            {
                var x = side * gap;
                var n = new Vector3(-side, 0f, 0f);
                for (int i = 0; i + 1 < cut.Count; i++)
                    TowerQuad(b, new Vector3(x, cut[i], zIn), new Vector3(x, cut[i], ZOut(cut[i])), new Vector3(x, cut[i + 1], ZOut(cut[i + 1])), new Vector3(x, cut[i + 1], zIn),
                              n, n, g.Bark * 1.1f, 0.7f);
            }
            var zt = ZOut(top);
            TowerQuad(b, new Vector3(-gap, top + 0.005f, zIn), new Vector3(gap, top + 0.005f, zIn), new Vector3(gap, top + 0.005f, zt), new Vector3(-gap, top + 0.005f, zt),
                      Vector3.down, Vector3.down, g.Bark * 1.1f, 0.6f);

            // Rings of points up the wall, joined facet by facet; across the doorway only above the cut's top.
            // (Holes: (from, to) degrees, (bottom, top); their edges join each ring, and the facets inside are left out.)
            void WallRings(List<float> heights, System.Func<float, float> radiusAt, int count, float narrowest, float facing, Color c, float ao, List<Vector4> cuts)
            {
                var rings = new List<List<Vector3>>();
                List<float> angles = null;
                foreach (var y in heights)
                {
                    var r = radiusAt(y);
                    var ring = new List<Vector3>();
                    angles = TowerRingAngles(r, gap, count, narrowest);
                    if (cuts != null)
                    {
                        var first = angles[0];
                        foreach (var h in cuts)
                        {
                            angles.Add(h.x < first ? h.x + 360f : h.x);
                            angles.Add(h.y < first ? h.y + 360f : h.y);
                        }
                        angles.Sort();
                    }
                    foreach (var a in angles)
                        ring.Add(TowerAt(a, r, y));
                    rings.Add(ring);
                }
                for (int i = 0; i + 1 < heights.Count; i++)
                {
                    var lo = rings[i];
                    var hi = rings[i + 1];
                    var lean = (radiusAt(heights[i]) - radiusAt(heights[i + 1])) / (heights[i + 1] - heights[i]);
                    var closed = heights[i] >= top - 0.001f;
                    for (int j = 0; j < (closed ? lo.Count : lo.Count - 1); j++)
                    {
                        var k = (j + 1) % lo.Count;
                        if (k > j && InHole(heights[i], heights[i + 1], (angles[j] + angles[k]) * 0.5f))
                            continue;
                        TowerQuad(b, lo[j], lo[k], hi[k], hi[j], Normal(lo[j]), Normal(lo[k]), c, ao);
                    }
                    Vector3 Normal(Vector3 p) => new Vector3(p.x, lean * new Vector2(p.x, p.z).magnitude, p.z).normalized * facing;
                }

                bool InHole(float y0, float y1, float a)
                {
                    if (cuts == null)
                        return false;
                    foreach (var h in cuts)
                        if (y0 >= h.z - 0.001f && y1 <= h.w + 0.001f && Mathf.Abs(Mathf.DeltaAngle(a, (h.x + h.y) * 0.5f)) < (h.y - h.x) * 0.5f)
                            return true;
                    return false;
                }
            }
        }

        /// <summary>
        /// The walk-in tower's porch: Porch's door, cheeks, lintel and roof, with no dark panel behind the leaf (the doorway
        /// is open behind it) and solid where you see it: the cheeks run as kit boxes into the wall, the leaf blocks only
        /// while shut (a door box), and its step is stood on. The same draws as Porch and Door, in the same order.
        /// </summary>
        static void TowerPorch(Grower g, float radius, float walls, float doorZ, float backZ, float width, float height, float depth, Color wall, Color roof, FloraPart roofPart)
        {
            var b = g.B;
            g.DoorHeight = Mathf.Max(g.DoorHeight, height);
            var wood = new Color(0.3f, 0.2f, 0.12f) * g.Range(0.85f, 1.15f);
            var frame = g.Bark * 0.55f;
            var post = TowerPost(width);
            Vector3 P(float x, float y, float z) => new Vector3(x, y, doorZ + z);
            if (g.Lod == 2)
                b.Box(P(0f, height * 0.5f, 0.02f), new Vector3(width, height, 0.06f), Quaternion.identity, wood * 0.6f, FloraPart.Bark);
            else
            {
                var leaf = b.VertexCount;
                var planks = 4;
                var plank = (width - 0.03f * (planks - 1)) / planks;
                for (int k = 0; k < planks; k++)
                {
                    var x = -width * 0.5f + plank * 0.5f + k * (plank + 0.03f);
                    b.Box(P(x, height * 0.5f, -0.02f), new Vector3(plank, height, 0.08f), Quaternion.identity, wood * g.Range(0.9f, 1.1f), FloraPart.Bark);
                }
                if (g.Lod == 0)
                {
                    foreach (var y in new[] { height * 0.22f, height * 0.78f })
                        b.Box(P(0f, y, 0.03f), new Vector3(width * 0.92f, 0.07f, 0.02f), Quaternion.identity, new Color(0.12f, 0.12f, 0.13f), FloraPart.Bark);
                    b.Box(P(width * 0.3f, height * 0.48f, 0.06f), new Vector3(0.05f, 0.14f, 0.05f), Quaternion.identity, new Color(0.15f, 0.14f, 0.13f), FloraPart.Bark);
                }
                b.Hinge(leaf, P(-width * 0.5f, 0f, 0f), Vector3.right, 0f);
                foreach (var side in new[] { -1f, 1f })
                    b.Box(P(side * (width * 0.5f + post * 0.5f), height * 0.5f + 0.05f, 0.04f), new Vector3(post, height + 0.1f, 0.2f), Quaternion.identity, frame, FloraPart.Bark);
                b.Box(P(0f, height + post * 0.5f, 0.05f), new Vector3(width + post * 2.6f, post, 0.24f), Quaternion.identity, frame, FloraPart.Bark);
                b.Box(P(0f, 0.06f, 0.32f), new Vector3(width + post * 2f, 0.16f, 0.5f), Quaternion.identity, new Color(0.45f, 0.44f, 0.41f) * g.Range(0.9f, 1.1f), FloraPart.Bark);
            }
            if (g.Lod == 0)
            {
                g.DoorBoxes.Add(Solid(P(0f, height * 0.5f, -0.02f), new Vector3(width, height, 0.12f), Quaternion.identity));
                g.Boxes.Add(Solid(P(0f, 0.06f, 0.32f), new Vector3(width + post * 2f, 0.16f, 0.5f), Quaternion.identity));
            }

            // Porch's cheeks, lintel and little roof.
            var cheek = 0.16f;
            var front = doorZ + depth;
            var length = front - backZ;
            var mid = (front + backZ) * 0.5f;
            var top = height + post + 0.08f;
            var inner = width * 0.5f + post;
            foreach (var side in new[] { -1f, 1f })
                b.Box(new Vector3(side * (inner + cheek * 0.5f), (top - 0.1f) * 0.5f, mid), new Vector3(cheek, top + 0.1f, length), Quaternion.identity, wall, FloraPart.Bark);
            b.Box(new Vector3(0f, top + 0.1f, mid), new Vector3((inner + cheek) * 2f, 0.2f, length), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            if (g.Lod < 2)
                b.Box(new Vector3(0f, top + 0.3f, mid + 0.12f), new Vector3((inner + cheek) * 2f + 0.3f, 0.1f, length + 0.35f), Quaternion.Euler(14f, 0f, 0f), roof, roofPart);

            // The passage's sides as kit boxes, from the room to the porch's front (as tall as Porch's cylinders were).
            if (g.Lod == 0)
            {
                var room = TowerInner(radius);
                var zIn = Mathf.Sqrt(room * room - inner * inner);
                foreach (var side in new[] { -1f, 1f })
                    g.Boxes.Add(Solid(new Vector3(side * (inner + 0.16f), (top + 0.35f) * 0.5f, (zIn + front) * 0.5f), new Vector3(0.32f, top + 0.35f, front - zIn), Quaternion.identity));
            }
        }

        /// <summary>
        /// Inside the walk-in tower: the ground floor and the threshold, the wall's colliders (the lining's and the outside's,
        /// with the doorway left open below the porch lintel), storeys about 3.2 m apart (two or three), each with a floor
        /// of planks open over the flight below it, and the stair round the wall. P-51 row 3: the stair's last flight comes
        /// up through a hatch onto a roof deck at the eaves, with a parapet round it and the roof lifted on posts above,
        /// and each storey has two windows (the kit's Window, set into holes in the lining).
        /// </summary>
        static void TowerRooms(Grower g, float radius, float walls, float doorWidth, float doorHeight, float doorZ)
        {
            var b = g.B;
            var room = TowerInner(radius);
            var gap = TowerGap(doorWidth);
            var top = TowerGapTop(doorWidth, doorHeight);
            var zIn = Mathf.Sqrt(room * room - gap * gap);
            var planks = new Color(0.34f, 0.23f, 0.14f);
            var stone = new Color(0.45f, 0.44f, 0.41f);

            // Ground floor and threshold (their tops 4 cm up).
            if (g.Lod < 2)
                TowerDisc(b, 0.04f, room + 0.02f, 0f, planks * 0.85f, true, false);
            TowerFloor(g, 0.04f, 0.4f, room);
            Part(new Vector3(0f, -0.16f, (zIn - 0.1f + doorZ) * 0.5f), new Vector3(gap * 2f, 0.4f, doorZ - zIn + 0.1f), Quaternion.identity, stone * 0.85f);

            // The wall's colliders: the lining's face and the outside's (0.95 of the tower's radius, as near as the old
            // cylinder stood), open across the doorway up to the lintel, and the wall over it.
            if (g.Lod == 0)
            {
                var outside = radius * 0.95f;
                WallBoxes(room, 1f);
                WallBoxes(outside, -1f);
                var zOut = Mathf.Sqrt(outside * outside - gap * gap);
                g.Boxes.Add(Solid(new Vector3(0f, (top + walls) * 0.5f, (zIn + zOut + 0.15f) * 0.5f), new Vector3(gap * 2f + 0.6f, walls - top, zOut + 0.15f - zIn), Quaternion.identity));
            }

            // Storeys, and the stair up to each: a flight round the wall, then a 15° landing on the floor it reaches. The
            // last flight comes up through the roof deck at the eaves (P-51 row 3), which is open over only its top
            // treads: the hatch.
            var flights = TowerFlights(radius, walls);
            for (int k = 0; k < flights.Count; k++)
            {
                var f = flights[k];
                var deck = k == flights.Count - 1;
                // (The last rise is onto the floor itself.)
                for (int s = 1; s < f.Steps; s++)
                {
                    var a = f.Start + (s - 0.5f) * f.Tread;
                    var y = f.Below + s * f.Rise;
                    var turn = Quaternion.Euler(0f, -a, 0f);
                    Part(TowerAt(a, room - TowerStairWidth * 0.5f, y - 0.2f), new Vector3(TowerStairWidth, 0.4f, 2f * room * Mathf.Tan(f.Tread * 0.5f * Mathf.Deg2Rad) + 0.04f), turn,
                         planks * (s % 2 == 0 ? 1f : 0.9f));
                }
                // The floor: a round middle, and a band of planks round the wall, open over this flight (over the deck's
                // top treads only, with headroom for climbing out).
                if (g.Lod < 2)
                    TowerDisc(b, f.Level, room - TowerStairWidth + 0.02f, 0.2f, planks, true, true);
                TowerFloor(g, f.Level, 0.2f, (room - TowerStairWidth) * 0.99f);
                var open = f.End - f.Start;
                if (deck)
                    open = Mathf.Min(open, (Mathf.Ceil(TowerHatchDrop / f.Rise) + 0.5f) * f.Tread);
                var arc = 360f - open;
                var pieces = Mathf.CeilToInt(arc / 20f);
                for (int p = 0; p < pieces; p++)
                {
                    var a = f.End + (p + 0.5f) * arc / pieces;
                    Part(TowerAt(a, room - TowerStairWidth * 0.5f + 0.025f, f.Level - 0.1f), new Vector3(TowerStairWidth + 0.05f, 0.2f, 2f * room * Mathf.Tan(arc / pieces * 0.5f * Mathf.Deg2Rad) + 0.02f),
                         Quaternion.Euler(0f, -a, 0f), planks * (p % 2 == 0 ? 0.95f : 1.02f));
                }
                if (deck)
                    Hatch(f.End - open, f.End, f.Level);
            }

            // On the deck: a crenellated parapet over the wall's top, and on its merlons the posts the lifted roof
            // stands on (Tower draws the roof), with planks under it. Seen from afar, so drawn at every level; the deck
            // itself is drawn far off as a plain disc.
            var wallTop = radius * 0.85f;
            var ring = (room + wallTop) * 0.5f + 0.01f;
            var thick = wallTop - room + 0.06f;
            var length = 2f * (wallTop + 0.04f) * Mathf.Tan(Mathf.PI / 16f);
            var eave = walls + TowerLookout - 0.2f;
            for (int k = 0; k < 16; k++)
            {
                var a = (k + 0.5f) * 22.5f;
                var turn = Quaternion.Euler(0f, -a, 0f);
                var tall = k % 2 == 0;
                var h = tall ? TowerMerlon : TowerCrenel;
                Outer(TowerAt(a, ring, walls + h * 0.5f), new Vector3(thick, h, tall ? length : length + 0.02f), turn, g.Bark * (tall ? 1.05f : 0.95f));
                if (tall)
                {
                    var post = eave + 0.03f - walls - TowerMerlon;
                    Outer(TowerAt(a, ring, walls + TowerMerlon + post * 0.5f), new Vector3(0.2f, post, 0.2f), turn, g.Bark * 0.55f);
                }
            }
            TowerDisc(b, eave + 0.03f, radius * 1.05f, 0f, planks * 0.6f, false, true);
            if (g.Lod == 2)
                TowerDisc(b, walls, wallTop, 0f, planks * 0.8f, true, false);

            // The windows, last (Window draws random numbers): the kit's Window set in from outside, its frame running
            // back through the wall to the lining's hole.
            var bar = TowerWindowBar;
            var holeHalf = Mathf.Asin(Mathf.Clamp01((TowerWindowWidth + bar) * 0.5f / room)) * Mathf.Rad2Deg;
            foreach (var spot in TowerWindowSpots(radius, walls))
            {
                var dir = TowerAt(spot.x, 1f, 0f);
                var outside = TowerProfile(radius, walls, spot.y) + 0.02f;
                var sag = outside - (room * Mathf.Cos(holeHalf * Mathf.Deg2Rad) - 0.01f) - 0.12f;
                Window(g, dir * outside + Vector3.up * spot.y, Quaternion.LookRotation(dir), TowerWindowWidth, TowerWindowHeight,
                       g.HasAccent ? (Color?)null : new Color(0.62f, 0.55f, 0.38f), sag);
            }

            // The hatch's two leaves, flung open: standing on the deck along the opening's inner edge (a rail to the
            // middle), clear of its top end where you step off the stair.
            void Hatch(float from, float to, float level)
            {
                var inner = room - TowerStairWidth + 0.03f;
                var clear = Mathf.Min(1.1f / inner * Mathf.Rad2Deg, (to - from) * 0.5f);
                var span = to - clear - from;
                var leaves = span * Mathf.Deg2Rad * inner > 1.3f ? 2 : 1;
                for (int l = 0; l < leaves; l++)
                {
                    var a = from + (l + 0.5f) * span / leaves;
                    var chord = 2f * inner * Mathf.Sin(span / leaves * 0.5f * Mathf.Deg2Rad) - 0.04f;
                    var turn = Quaternion.Euler(0f, -a, 0f) * Quaternion.Euler(0f, 0f, 10f);
                    var size = new Vector3(0.05f, TowerStairWidth, chord);
                    var centre = TowerAt(a, inner * Mathf.Cos(span / leaves * 0.5f * Mathf.Deg2Rad), level) + turn * new Vector3(0.025f, TowerStairWidth * 0.5f, 0f);
                    Part(centre, size, turn, planks * 0.8f);
                    if (g.Lod == 0)
                        foreach (var z in new[] { -0.3f, 0.3f })
                            b.Box(centre + turn * new Vector3(-0.03f, 0f, z * chord), new Vector3(0.015f, TowerStairWidth * 0.9f, 0.05f), turn, new Color(0.12f, 0.12f, 0.13f), FloraPart.Bark);
                }
            }

            // A part seen from afar: drawn at every level, solid at the nearest.
            void Outer(Vector3 centre, Vector3 size, Quaternion turn, Color c)
            {
                b.Box(centre, size, turn, c, FloraPart.Bark);
                if (g.Lod == 0)
                    g.Boxes.Add(Solid(centre, size, turn));
            }

            // A part drawn at the near levels and solid at the nearest.
            void Part(Vector3 centre, Vector3 size, Quaternion turn, Color c)
            {
                if (g.Lod < 2)
                    b.Box(centre, size, turn, c, FloraPart.Bark);
                if (g.Lod == 0)
                    g.Boxes.Add(Solid(centre, size, turn));
            }

            // One ring of thin boxes along a wall's facets (the solid part, from the doorway's left edge round to its
            // right), each face on its facet: the boxes stand outward of it for the lining (1), inward for the outside (-1).
            void WallBoxes(float r, float outward)
            {
                var angles = TowerRingAngles(r, gap, 16, r);
                for (int i = 0; i + 1 < angles.Count; i++)
                {
                    var p0 = TowerAt(angles[i], r, 0f);
                    var p1 = TowerAt(angles[i + 1], r, 0f);
                    var midPoint = (p0 + p1) * 0.5f;
                    var n = midPoint.normalized;
                    g.Boxes.Add(Solid(midPoint + n * (outward * TowerWallBox * 0.5f) + Vector3.up * walls * 0.5f,
                                      new Vector3((p1 - p0).magnitude + TowerWallBox, walls, TowerWallBox), Quaternion.LookRotation(n)));
                }
            }
        }

        /// <summary>A floor's solid middle (at the nearest level): four squares turned 22.5Â° apart, a 16-sided slab of this apothem.</summary>
        static void TowerFloor(Grower g, float level, float thickness, float apothem)
        {
            if (g.Lod != 0)
                return;
            for (int k = 0; k < 4; k++)
                g.Boxes.Add(Solid(new Vector3(0f, level - thickness * 0.5f, 0f), new Vector3(apothem * 2f, thickness, apothem * 2f), Quaternion.Euler(0f, k * 22.5f, 0f)));
        }

        /// <summary>A round slab of planks, its top at <paramref name="level"/>: its top, its underside and (when thick) its edge.</summary>
        static void TowerDisc(FloraMeshBuilder b, float level, float radius, float thickness, Color c, bool topFace, bool underside)
        {
            const int segments = 16;
            var under = level - thickness;
            for (int k = 0; k < segments; k++)
            {
                var a0 = k * 360f / segments;
                var a1 = (k + 1) * 360f / segments;
                if (topFace)
                    Fan(level, Vector3.up, 1f);
                if (underside)
                    Fan(under, Vector3.down, 0.7f);
                if (thickness > 0f)
                {
                    var n0 = TowerAt(a0, 1f, 0f);
                    var n1 = TowerAt(a1, 1f, 0f);
                    TowerQuad(b, TowerAt(a0, radius, under), TowerAt(a1, radius, under), TowerAt(a1, radius, level), TowerAt(a0, radius, level), n0, n1, c * 0.85f, 0.8f);
                }

                void Fan(float y, Vector3 n, float ao)
                {
                    var i0 = b.Vertex(new Vector3(0f, y, 0f), n, c, FloraPart.Bark, ao);
                    var i1 = b.Vertex(TowerAt(a0, radius, y), n, c, FloraPart.Bark, ao);
                    var i2 = b.Vertex(TowerAt(a1, radius, y), n, c, FloraPart.Bark, ao);
                    // (Anticlockwise seen from above, 0 to 1, faces down; so up takes them the other way round.)
                    if (n.y > 0f)
                        b.Triangle(i0, i2, i1);
                    else
                        b.Triangle(i0, i1, i2);
                }
            }
        }

        static KitBox Solid(Vector3 centre, Vector3 size, Quaternion turn) =>
            new KitBox { Centre = centre, Half = size * 0.5f, Rotation = turn, Reach = size.magnitude * 0.5f };
    }
}
