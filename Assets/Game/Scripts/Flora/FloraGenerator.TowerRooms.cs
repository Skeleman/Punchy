using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// P-51 row 2 (#337): the elf tower walked into (StructureKit.WalkInTower, with Seamless on). Its wall is a shell
    /// with the doorway cut through it under the porch, lined inside. Its storeys stand about 3.2 m apart. A stair climbs
    /// round the inside of the wall, and each floor is open over the flight that comes up through it. Lining, floors and
    /// stair are drawn only at the two nearest detail levels (rule 20). At the nearest they are also kit boxes (what you
    /// see is what you stand on), as the walk-in kit houses are. Nothing here draws a random number, so the tower's own
    /// draws are unchanged; worlds without -seamless build the tower as before (Tower's other branch).
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
            WallRings(ys, y => TowerProfile(radius, walls, y), sides, radius * 0.85f, 1f, g.Bark, 1f);
            // The lining: a plain cylinder facing in, a little lighter.
            WallRings(new List<float> { 0f, top, walls }, y => inner, g.Lod == 0 ? 16 : 12, inner, -1f, g.Bark * 1.25f, 0.8f);

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
            void WallRings(List<float> heights, System.Func<float, float> radiusAt, int count, float narrowest, float facing, Color c, float ao)
            {
                var rings = new List<List<Vector3>>();
                foreach (var y in heights)
                {
                    var r = radiusAt(y);
                    var ring = new List<Vector3>();
                    foreach (var a in TowerRingAngles(r, gap, count, narrowest))
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
                        TowerQuad(b, lo[j], lo[k], hi[k], hi[j], Normal(lo[j]), Normal(lo[k]), c, ao);
                    }
                    Vector3 Normal(Vector3 p) => new Vector3(p.x, lean * new Vector2(p.x, p.z).magnitude, p.z).normalized * facing;
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
        /// of planks open over the flight below it, the stair round the wall and a ceiling under the eaves.
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

            // Storeys, and the stair up to each: a flight round the wall, then a 15° landing on the floor it reaches.
            var storeys = Mathf.Clamp(Mathf.FloorToInt(walls / 3.1f), 2, 3);
            var along = room - TowerStairWidth * 0.5f;
            var tread = TowerTread / along * Mathf.Rad2Deg;
            var start = TowerStairStart;
            var below = 0.04f;
            for (int k = 1; k < storeys; k++)
            {
                var level = walls * k / storeys;
                var steps = Mathf.CeilToInt((level - below) / TowerRise);
                var rise = (level - below) / steps;
                // (The last rise is onto the floor itself.)
                for (int s = 1; s < steps; s++)
                {
                    var a = start + (s - 0.5f) * tread;
                    var y = below + s * rise;
                    var turn = Quaternion.Euler(0f, -a, 0f);
                    Part(TowerAt(a, along, y - 0.2f), new Vector3(TowerStairWidth, 0.4f, 2f * room * Mathf.Tan(tread * 0.5f * Mathf.Deg2Rad) + 0.04f), turn,
                         planks * (s % 2 == 0 ? 1f : 0.9f));
                }
                var end = start + (steps - 1) * tread;
                // The floor: a round middle, and a band of planks round the wall, open over this flight.
                if (g.Lod < 2)
                    TowerDisc(b, level, room - TowerStairWidth + 0.02f, 0.2f, planks, true, true);
                TowerFloor(g, level, 0.2f, (room - TowerStairWidth) * 0.99f);
                var arc = 360f - (end - start);
                var pieces = Mathf.CeilToInt(arc / 20f);
                for (int p = 0; p < pieces; p++)
                {
                    var a = end + (p + 0.5f) * arc / pieces;
                    Part(TowerAt(a, room - TowerStairWidth * 0.5f + 0.025f, level - 0.1f), new Vector3(TowerStairWidth + 0.05f, 0.2f, 2f * room * Mathf.Tan(arc / pieces * 0.5f * Mathf.Deg2Rad) + 0.02f),
                         Quaternion.Euler(0f, -a, 0f), planks * (p % 2 == 0 ? 0.95f : 1.02f));
                }
                start = end + 15f;
                below = level;
            }
            // The ceiling under the eaves.
            if (g.Lod < 2)
                TowerDisc(b, walls - 0.05f, room + 0.05f, 0f, planks * 0.7f, false, true);

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

        /// <summary>A floor's solid middle (at the nearest level): four squares turned 22.5° apart, a 16-sided slab of this apothem.</summary>
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
