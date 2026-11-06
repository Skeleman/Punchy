using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, house parts: roof tubes, huts, doors, porches, windows, ruin sand, bricks and glyphs, roof courses. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A roof tube with a rounded eave (#32): from the underside the profile bevels out, rolls over a thick lip and
        /// rises into the slope, so the roofing reads as having thickness instead of coming to a knife edge.
        /// <paramref name="eaveY"/> is the lowest point of the lip, <paramref name="rim"/> its widest radius.
        /// </summary>
        static void RoofTube(FloraMeshBuilder b, float eaveY, float rim, int sides, Color color, FloraPart part, float apexY, float apexR,
                             float midY, float midR, float lip = 0.2f)
        {
            var path = new List<Vector3>();
            var radii = new List<float>();
            void Add(float y, float r) { path.Add(Vector3.up * y); radii.Add(r); }
            Add(eaveY + lip * 0.1f, rim * 0.84f);
            Add(eaveY, rim * 0.93f);
            Add(eaveY + lip * 0.4f, rim * 1.0f);
            Add(eaveY + lip * 0.9f, rim * 0.985f);
            if (midY > eaveY + lip)
                Add(midY, midR);
            Add(apexY, apexR);
            var first = b.VertexCount;
            b.Tube(path, radii, sides, color, part, cap: true);
            // The lip's underside faces down and went black against the sky, worst at the far-reaching corners of
            // square roofs (W-113 item 13): tilt its four rings' normals up so the lip reads as lit stone.
            b.LiftNormals(first, 4 * sides, 0.75f);
        }

        static void Hut(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var radius = height * s.Spread * 0.5f;
            var wall = height * 0.55f;
            g.Height = height;
            g.Width = radius * 2.5f;
            g.TrunkRadius = radius;
            // Collision only up to the eaves (the trunk cylinder would reach the roof's peak and push anyone on the roof
            // off it, F-36); the roof itself is stood on through its support grid.
            g.Colliders.Add(new Vector4(0f, 0f, radius, wall));
            var sides = g.Sides(14, 10, 7);

            // Walls, slightly bulging, sunk a little into the ground.
            var first = b.VertexCount;
            b.Tube(new List<Vector3> { Vector3.down * 0.1f, Vector3.up * wall * 0.5f, Vector3.up * wall },
                   new List<float> { radius, radius * 1.02f, radius * 0.98f }, sides, g.Bark, FloraPart.Bark);
            // Timber bands round the wall.
            if (g.Lod < 2)
                foreach (var y in new[] { 0.15f, wall * 0.95f })
                    b.Tube(new List<Vector3> { Vector3.up * (y - 0.06f), Vector3.up * (y + 0.06f) },
                           new List<float> { radius * 1.04f, radius * 1.04f }, sides, g.Bark * 0.6f, FloraPart.Bark);
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.6f));

            // Conical roof with an overhang, rough at the edge.
            var roof = b.VertexCount;
            var overhang = radius * 1.3f;
            var roofColor = g.FoliageColor();
            // A thick lip at the rim (#32): a lower ring just inside the edge, so the eave has a visible thickness.
            b.Surface = (int)Punchy.Terrain.KitSurface.Thatch;   // W-99: drawn as thatch
            RoofTube(b, wall - 0.3f, overhang, sides, roofColor, FloraPart.Bark, height, 0.05f, wall + (height - wall) * 0.45f, overhang * 0.55f);
            b.Surface = 0;
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n.y + 0.2f)));
            g.Perches.Add(Vector3.up * height);

            // A door facing +Z in a shallow porch whose sides run back into the curved wall, and a couple of windows.
            // The door stands out past the wall's slight bulge, so the curve doesn't poke through it.
            var doorWidth = Mathf.Min(radius * 0.5f, 1.25f);
            Porch(g, radius * 1.05f, WallBehind(radius, doorWidth), doorWidth, Mathf.Min(wall * 0.8f, HumanDoor), 0.4f, g.Bark, roofColor, FloraPart.Bark);
            // On the wall itself: its radius at that height (between the bulge and the top).
            Windows(g, radius * Mathf.Lerp(1.02f, 0.98f, 0.16f), wall * 0.58f, 4, 0.55f, 40f, sides);
            DoorMat(g, radius * 1.05f + 0.4f, doorWidth);
        }


        /// <summary>
        /// A door's height for human folk (about 1.3 times a 1.85 m person, so there's headroom); buildings are drawn
        /// for humans and each settlement scales them by its folk's size, so dwarf and goblin doors come out lower and
        /// ogre doors higher.
        /// </summary>
        const float HumanDoor = 2.4f;

        /// <summary>
        /// How far in front of its origin a building's door stands (along +Z, unscaled, at its mean height), or -1 for
        /// forms without one or where it isn't known. The same sums as the builders (Hut, StoneHouse, Tower, Inn, Hall), so
        /// a Door anchor can stand at the door itself (AnchorBaking); variants differ from it by their height's spread.
        /// </summary>
        public static float DoorFront(FloraSpecies s)
        {
            var half = (s.Height.x + s.Height.y) * 0.5f * s.Spread * 0.5f;
            return s.Form switch
            {
                FloraForm.Hut => half * 1.05f,
                FloraForm.StoneHouse => half + 0.02f,
                FloraForm.Tower => half * 1.38f + 0.05f,
                FloraForm.Inn => half * 0.8f + 0.02f,
                FloraForm.Hall => half * 0.55f + 0.04f,
                FloraForm.KitBuilding => half * 0.93f + 0.3f,
                _ => -1f
            };
        }

        /// <summary>
        /// #25 (W-83): a house type's door and inside, for interior cells (Punchy.Terrain.InteriorCells), at the species'
        /// mean height and unscaled (multiply by the settlement's folk scale as the model is). <paramref name="door"/> is
        /// the door's foot in the model's local space (it faces +Z); <paramref name="inside"/> is the room's inner width
        /// (X), height (Y, one storey: up to the eaves or the first floor) and depth (Z), inside the walls. False for
        /// forms with no door, and kit buildings other than houses and stilt houses (a stilt house's door is up on its
        /// deck; the height is not added here).
        /// </summary>
        public static bool HouseInterior(FloraSpecies s, out Vector3 door, out Vector3 inside)
        {
            door = default;
            inside = default;
            var front = DoorFront(s);
            if (front < 0f)
                return false;
            var height = (s.Height.x + s.Height.y) * 0.5f;
            var half = height * s.Spread * 0.5f;
            // The same sums as the builders (Hut, StoneHouse, Tower, Inn, Hall, KitStructure), less a wall's thickness.
            switch (s.Form)
            {
                case FloraForm.Hut:
                    inside = new Vector3((half - 0.15f) * 2f, height * 0.55f, (half - 0.15f) * 2f);
                    break;
                case FloraForm.StoneHouse:
                    inside = new Vector3((half - 0.3f) * 2f, height * 0.65f, (half - 0.3f) * 2f);
                    break;
                case FloraForm.Tower:
                    inside = new Vector3((half - 0.3f) * 2f, height * 0.62f, (half - 0.3f) * 2f);
                    break;
                case FloraForm.Inn:
                    inside = new Vector3((half - 0.25f) * 2f, height * 0.6f, (half * 0.8f - 0.25f) * 2f);
                    break;
                case FloraForm.Hall:
                    // Its floor is on the 0.6 m plinth: the door's foot is there too.
                    inside = new Vector3((half - 0.3f) * 2f, height * 0.38f, (half * 0.55f - 0.3f) * 2f);
                    door = new Vector3(0f, 0.6f - 0.05f, front);
                    return true;
                case FloraForm.KitBuilding:
                    if (!Punchy.Terrain.StructureKit.HouseLike(s.KitShape))
                        return false;
                    var width = height * s.Spread;
                    var thick = Punchy.Terrain.StructureKit.Thick;
                    inside = new Vector3(width - thick * 2f, Punchy.Terrain.StructureKit.For(s.KitCulture).Storey, width * 0.925f - thick * 2f);
                    break;
                default:
                    return false;
            }
            door = new Vector3(0f, 0f, front);
            return true;
        }

        /// <summary>A doorway in a wall facing +Z, at distance <paramref name="front"/> (see Door).</summary>
        static void Doorway(Grower g, float front, float width, float height) =>
            Door(g, new Vector3(0f, 0f, front), Quaternion.identity, width, height);

        /// <summary>
        /// A door standing in a wall, its foot at <paramref name="at"/>, facing out along <paramref name="facing"/>'s +Z:
        /// a timber frame (two jambs and a lintel standing proud of the wall), a plank door set back in it with a
        /// handle and iron straps, and a stone step. Far away, just the door's dark rectangle.
        /// </summary>
        static void Door(Grower g, Vector3 at, Quaternion facing, float width, float height)
        {
            var b = g.B;
            g.DoorHeight = Mathf.Max(g.DoorHeight, height);
            var wood = new Color(0.3f, 0.2f, 0.12f) * g.Range(0.85f, 1.15f);
            var frame = g.Bark * 0.55f;
            Vector3 P(float x, float y, float z) => at + facing * new Vector3(x, y, z);
            if (g.Lod == 2)
            {
                b.Box(P(0f, height * 0.5f, 0.02f), new Vector3(width, height, 0.06f), facing, wood * 0.6f, FloraPart.Bark);
                return;
            }
            var post = Mathf.Clamp(width * 0.12f, 0.1f, 0.18f);
            // The leaf: vertical planks with thin gaps, set back a little in the frame, hung on its left edge.
            var leaf = b.VertexCount;
            var planks = 4;
            var plank = (width - 0.03f * (planks - 1)) / planks;
            for (int k = 0; k < planks; k++)
            {
                var x = -width * 0.5f + plank * 0.5f + k * (plank + 0.03f);
                b.Box(P(x, height * 0.5f, -0.02f), new Vector3(plank, height, 0.08f), facing, wood * g.Range(0.9f, 1.1f), FloraPart.Bark);
            }
            // Two iron straps and a handle, on the leaf.
            if (g.Lod == 0)
            {
                foreach (var y in new[] { height * 0.22f, height * 0.78f })
                    b.Box(P(0f, y, 0.03f), new Vector3(width * 0.92f, 0.07f, 0.02f), facing, new Color(0.12f, 0.12f, 0.13f), FloraPart.Bark);
                b.Box(P(width * 0.3f, height * 0.48f, 0.06f), new Vector3(0.05f, 0.14f, 0.05f), facing, new Color(0.15f, 0.14f, 0.13f), FloraPart.Bark);
            }
            b.Hinge(leaf, P(-width * 0.5f, 0f, 0f), facing * Vector3.right, facing.eulerAngles.y * Mathf.Deg2Rad);
            // The dark doorway behind the leaf (what shows when it swings open).
            b.Box(P(0f, height * 0.5f, -0.08f), new Vector3(width, height, 0.04f), facing, new Color(0.03f, 0.025f, 0.02f), FloraPart.Bark, 0.4f);
            // Frame: jambs and a lintel standing proud of the wall.
            foreach (var side in new[] { -1f, 1f })
                b.Box(P(side * (width * 0.5f + post * 0.5f), height * 0.5f + 0.05f, 0.04f), new Vector3(post, height + 0.1f, 0.2f), facing, frame, FloraPart.Bark);
            b.Box(P(0f, height + post * 0.5f, 0.05f), new Vector3(width + post * 2.6f, post, 0.24f), facing, frame, FloraPart.Bark);
            // A stone step.
            b.Box(P(0f, 0.06f, 0.32f), new Vector3(width + post * 2f, 0.16f, 0.5f), facing, new Color(0.45f, 0.44f, 0.41f) * g.Range(0.9f, 1.1f), FloraPart.Bark);
        }

        /// <summary>
        /// Where a round wall of the given radius is at the edges of a door of that width (plus its frame): the
        /// curve falls back from the door's middle, so anything meant to meet the wall starts here, or behind it.
        /// </summary>
        static float WallBehind(float radius, float doorWidth)
        {
            var half = doorWidth * 0.5f + 0.35f;
            return Mathf.Sqrt(Mathf.Max(radius * radius - half * half, 0f)) - 0.12f;
        }

        /// <summary>
        /// A door in a shallow entrance porch facing +Z, for round walls: the door (see Door) stands at
        /// <paramref name="doorZ"/>, and two side cheeks and a lintel in the wall's colour run from
        /// <paramref name="backZ"/> (inside the wall, see WallBehind) to <paramref name="depth"/> in front of the door,
        /// under a small roof. However the wall curves behind it, no gap shows at the sides.
        /// </summary>
        static void Porch(Grower g, float doorZ, float backZ, float width, float height, float depth, Color wall, Color roof, FloraPart roofPart)
        {
            var b = g.B;
            Door(g, new Vector3(0f, 0f, doorZ), Quaternion.identity, width, height);
            var post = Mathf.Clamp(width * 0.12f, 0.1f, 0.18f);
            var cheek = 0.16f;
            var front = doorZ + depth;
            var length = front - backZ;
            var mid = (front + backZ) * 0.5f;
            var top = height + post + 0.08f;
            var inner = width * 0.5f + post;
            foreach (var side in new[] { -1f, 1f })
                b.Box(new Vector3(side * (inner + cheek * 0.5f), (top - 0.1f) * 0.5f, mid), new Vector3(cheek, top + 0.1f, length), Quaternion.identity, wall, FloraPart.Bark);
            b.Box(new Vector3(0f, top + 0.1f, mid), new Vector3((inner + cheek) * 2f, 0.2f, length), Quaternion.identity, g.Bark * 0.6f, FloraPart.Bark);
            // Solid (W-43: the foyer had no collision): a row of cylinders as wide as the porch from the wall to its
            // front, and one at each front corner, as tall as its little roof. The door is shut, so nothing is lost.
            var span = inner + cheek;
            var count = Mathf.Clamp(Mathf.CeilToInt(length / (span * 1.2f)), 1, 3);
            for (int k = 0; k < count; k++)
            {
                var z = count == 1 ? Mathf.Max(backZ, front - span * 0.95f) : Mathf.Lerp(backZ, Mathf.Max(backZ, front - span * 0.95f), k / (count - 1f));
                g.Colliders.Add(new Vector4(0f, z, span, top + 0.35f));
            }
            foreach (var side in new[] { -1f, 1f })
                g.Colliders.Add(new Vector4(side * (inner + cheek * 0.5f), front - 0.2f, 0.22f, top + 0.35f));
            if (g.Lod < 2)
            {
                // A small roof, sloping out over the porch.
                var tilt = Quaternion.Euler(14f, 0f, 0f);
                b.Box(new Vector3(0f, top + 0.3f, mid + 0.12f), new Vector3((inner + cheek) * 2f + 0.3f, 0.1f, length + 0.35f), tilt, roof, roofPart);
            }
        }

        /// <summary>
        /// Windows round a round wall of this radius (a tube of <paramref name="sides"/>) at a height: a timber frame
        /// with a sill and a cross bar, round a pane in the accent colour (FloraPart.Window: dark glass by day, lit at
        /// night). The frame reaches back into the wall past both its curve across the window and its facets, so it
        /// reads as set into the wall from any side, never as a plane floating off it.
        /// </summary>
        static void Windows(Grower g, float radius, float y, int count, float size, float skipAngle, int sides)
        {
            var reach = size * 0.5f + 0.1f;
            var sag = radius - Mathf.Sqrt(Mathf.Max(radius * radius - reach * reach, 0f)) + radius * (1f - Mathf.Cos(Mathf.PI / sides));
            if (!g.HasAccent || g.Lod == 2)
                return;
            for (int k = 0; k < count; k++)
            {
                var a = (k + 0.5f) / count * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < skipAngle)
                    continue;   // keep clear of the door (+Z is 90° here)
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Window(g, dir * (radius + 0.02f) + Vector3.up * y, Quaternion.LookRotation(dir), size, size * 1.25f, null, sag);
            }
        }

        /// <summary>
        /// One window facing out along <paramref name="facing"/>'s +Z, centred at <paramref name="at"/>: the pane, set
        /// back 8 cm behind the front of its frame (jambs, lintel and a projecting sill), whose back edge runs
        /// <paramref name="sag"/> plus 12 cm behind the pane, into the wall (curved walls pass how far the wall falls
        /// away across the window, so no gap shows at its sides).
        /// </summary>
        static void Window(Grower g, Vector3 at, Quaternion facing, float width, float height, Color? light = null, float sag = 0f)
        {
            var b = g.B;
            var frame = g.Bark * 0.55f;
            // Each pane's colour a touch different: the shader keys its own lighting-up hour on it.
            b.Box(at, new Vector3(width, height, 0.06f), facing, (light ?? g.Accent) * g.Range(0.96f, 1.04f), FloraPart.Window);
            if (g.Lod == 2)
                return;
            var bar = Mathf.Clamp(width * 0.1f, 0.04f, 0.08f);
            const float front = 0.08f;
            var back = sag + 0.12f;
            var mid = (front - back) * 0.5f;
            Vector3 P(float x, float y, float z) => at + facing * new Vector3(x, y, z);
            foreach (var side in new[] { -1f, 1f })
            {
                b.Box(P(side * (width + bar) * 0.5f, 0f, mid), new Vector3(bar, height + bar * 2f, front + back), facing, frame, FloraPart.Bark);
                b.Box(P(0f, side * (height + bar) * 0.5f, mid), new Vector3(width + bar * 2f, bar, front + back), facing, frame, FloraPart.Bark);
            }
            if (g.Lod > 0)
                return;
            b.Box(P(0f, 0f, 0.035f), new Vector3(bar * 0.6f, height, 0.04f), facing, frame, FloraPart.Bark);
            // The sill sticks out past the frame and runs back into the wall with it.
            b.Box(P(0f, -height * 0.5f - bar * 1.4f, (front + 0.08f - back) * 0.5f), new Vector3(width + bar * 3f, bar * 0.8f, front + 0.08f + back), facing, frame, FloraPart.Bark);
        }

        /// <summary>
        /// W-104 (#80): wind-blown sand round a kit ruin. Drifts lean on the feet of the standing wall bays (mostly on one
        /// windward side), one low natural mound lies against the ruin where a wall came down, and dry grass tufts poke
        /// out of the sand up close. Drawing only, from its own random stream: the kit parts, boxes and colliders are the
        /// same as before. W-104 3b: the house stands on a 0.5 m plinth, so outside drifts and the mound start at the
        /// ground beside it and rise past the plinth's top (the mound to about 0.8 m), and inside drifts sit on the floor.
        /// </summary>
        static void RuinSand(Grower g, System.Collections.Generic.List<Punchy.Terrain.KitPart> parts, float width, float depth, uint seed)
        {
            var b = g.B;
            const float FloorTop = 0.5f;   // StructureKit.House's plinth course is 0.5 m tall
            var rng = new System.Random((int)(seed * 0x27D4EB2Du ^ 0x5A17u));
            float R() => (float)rng.NextDouble();
            var sand = new Color(0.58f, 0.52f, 0.4f);
            var grass = new Color(0.52f, 0.55f, 0.3f);
            var wind = Quaternion.Euler(0f, R() * 360f, 0f) * Vector3.forward;   // the side the sand piles up on
            var tufts = new System.Collections.Generic.List<Vector3>();
            var first = b.VertexCount;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Piece != Punchy.Terrain.KitPiece.Wall || p.Centre.y - p.Size.y * 0.5f > 0.3f || p.Size.y < 0.6f)
                    continue;                                              // only bays standing on the ground
                var along = p.Size.x >= p.Size.z ? p.Rotation * Vector3.right : p.Rotation * Vector3.forward;
                var normal = p.Size.x >= p.Size.z ? p.Rotation * Vector3.forward : p.Rotation * Vector3.right;
                var len = Mathf.Max(p.Size.x, p.Size.z);
                var thick = Mathf.Min(p.Size.x, p.Size.z);
                var side = Vector3.Dot(normal, wind) < 0f ? 1f : -1f;      // windward face first
                if (R() > 0.75f)
                    side = -side;
                if (R() < 0.2f)
                    continue;                                              // a few bare feet
                var outside = Vector3.Dot(normal * side, new Vector3(p.Centre.x, 0f, p.Centre.z)) > 0f;
                var r = outside
                    ? new Vector3(Mathf.Min(len * 0.35f, 1.4f) + R() * 0.6f, 0.45f + R() * 0.35f, 0.7f + R() * 0.5f)   // from the ground, past the plinth
                    : new Vector3(Mathf.Min(len * 0.3f, 1.1f) + R() * 0.5f, Mathf.Min(0.16f + R() * 0.24f, p.Size.y * 0.4f), 0.45f + R() * 0.4f);
                var at = new Vector3(p.Centre.x, (outside ? 0f : FloorTop) - r.y * 0.3f, p.Centre.z) + along * (R() - 0.5f) * len * 0.4f +
                         normal * side * (thick * 0.5f + (outside ? 0.1f : 0f) + r.z * 0.35f);
                var rot = Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
                b.Blob(at, r, rot, 2, 0.2f, i * 1.7f + (seed & 255), Color.Lerp(sand, sand * 0.88f, R()), FloraPart.Bark, 1f, 0f, 0.6f);
                if (R() < 0.4f)
                    tufts.Add(at + Vector3.up * r.y * 0.55f + along * (R() - 0.5f) * r.x);
            }
            // The mound: a long low swell lapping one side of the ruin, its gentle flank to windward, with a smaller
            // drift running off its end.
            var reach = Mathf.Max(width, depth) * 0.5f;
            var edge = Mathf.Abs(wind.x) * width * 0.5f + Mathf.Abs(wind.z) * depth * 0.5f + 0.1f;   // the plinth's edge on the windward side
            var mr = new Vector3(reach * (0.95f + R() * 0.3f), 1.05f + R() * 0.3f, 2.6f + R() * 1f);
            var centre = -wind * (edge + mr.z * 0.4f) + Vector3.Cross(Vector3.up, wind) * (R() - 0.5f) * reach * 0.5f;
            var face = Quaternion.LookRotation(wind, Vector3.up) * Quaternion.Euler(0f, 90f + (R() - 0.5f) * 25f, 0f);
            b.Blob(centre + Vector3.down * mr.y * 0.4f, mr, face, g.Lod == 0 ? 2 : 1, 0.14f, seed & 1023, sand, FloraPart.Bark, 1f, 0f, 0.6f);
            var tail = centre + face * new Vector3(mr.x * (0.8f + R() * 0.3f), 0f, 0.4f);
            b.Blob(tail + Vector3.down * mr.y * 0.3f, new Vector3(mr.x * 0.5f, mr.y * 0.55f, mr.z * 0.6f), face, 1, 0.12f, (seed & 1023) + 5f, sand * 0.95f, FloraPart.Bark, 1f, 0f, 0.6f);
            for (int k = 0; k < 3; k++)
                tufts.Add(centre + face * new Vector3((R() - 0.5f) * mr.x * 1.2f, mr.y * 0.45f, (R() - 0.5f) * mr.z * 0.6f));
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.5f));
            if (g.Lod == 0)
                foreach (var t in tufts)
                    b.Leaves(t + Vector3.up * 0.15f, new Vector3(0.3f, 0.22f, 0.3f), 10, 0.16f, rng, grass, grass * 1.25f);
        }

        /// <summary>
        /// W-94 (#80): a ruin's wall bay drawn as old coursed stone blocks instead of one slab: a darker mortar core a
        /// little inside the face, then courses about 0.32 m high of blocks 0.55-0.95 m long, every other course half a
        /// block over, each block worn smaller at its edges, proud or sunk a little, tipped a degree or two and shaded
        /// apart; a few blocks missing (more along the ragged top course), and some of those lying fallen at the wall's
        /// foot. Drawing only: the bay's collision box is still the part's own (KitStructure adds it), so walks are unchanged.
        /// </summary>
        static void RuinBricks(FloraMeshBuilder b, in Punchy.Terrain.KitPart p, Color c, uint seed)

        {
            var rng = new System.Random((int)(seed & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            var alongX = p.Size.x >= p.Size.z;
            float length = alongX ? p.Size.x : p.Size.z, thick = alongX ? p.Size.z : p.Size.x, height = p.Size.y;
            Vector3 Local(float a, float y, float t) => alongX ? new Vector3(a, y, t) : new Vector3(t, y, a);
            // W-101 (#80): the mortar core darker (was 0.6), so the joints between blocks read as lines from further off.
            b.Box(p.Centre, Local(length - 0.04f, height - 0.02f, Mathf.Max(0.05f, thick - 0.1f)), p.Rotation, c * 0.38f, FloraPart.Bark);
            var glyphs = new System.Random((int)((seed * 2654435761u ^ 0x5bd1e995u) & 0x7FFFFFFF));
            var courses = Mathf.Max(1, Mathf.RoundToInt(height / 0.32f));
            var ch = height / courses;
            for (int row = 0; row < courses; row++)
            {
                var top = row == courses - 1;
                float start = -length * 0.5f, end = length * 0.5f, cursor = start;
                var first = true;
                while (cursor < end - 0.05f)
                {
                    var len = first && (row & 1) == 1 ? 0.28f + R() * 0.2f : 0.55f + R() * 0.4f;
                    var e = Mathf.Min(end, cursor + len);
                    if (end - e < 0.25f)
                        e = end;
                    float a = (cursor + e) * 0.5f, l = e - cursor;
                    cursor = e;
                    first = false;
                    var gone = R() < (top ? 0.22f : row > 0 ? 0.03f : 0f);
                    var wear = 0.025f + R() * 0.035f;
                    var bh = ch - wear - (top ? R() * ch * 0.35f : 0f);
                    var y = -height * 0.5f + row * ch + bh * 0.5f + wear * 0.5f;
                    var shade = Color.Lerp(c * 0.84f, c * 1.12f, R());
                    if (R() < 0.15f)
                        shade = Color.Lerp(shade, new Color(0.42f, 0.44f, 0.36f), 0.25f);   // a lichened or weathered block
                    var tip = Quaternion.Euler((R() - 0.5f) * 3f, (R() - 0.5f) * 3f, (R() - 0.5f) * 3f);
                    if (!gone)
                    {
                        var size = Local(Mathf.Max(0.1f, l - wear), bh, thick + 0.02f - R() * 0.06f);
                        var at = p.Centre + p.Rotation * Local(a, y, (R() - 0.5f) * 0.03f);
                        b.Box(at, size, p.Rotation * tip, shade, FloraPart.Bark);
                        // W-101 (#80): now and then an old carving on one face of a whole block (its own draws, so the
                        // courses above are laid exactly as before).
                        if (!top && l > 0.45f && glyphs.NextDouble() < 0.1)
                            RuinGlyph(b, at, p.Rotation * tip, alongX, l - wear, bh, (alongX ? size.z : size.x) * 0.5f, shade, glyphs);
                    }
                    else if (row > 0 && R() < 0.6f && p.Centre.y - height * 0.5f < 0.3f)   // only from a bay standing on the ground
                    {
                        // Fallen to the foot of the wall, on one face or the other, turned and settled into the ground.
                        var side = R() < 0.5f ? -1f : 1f;
                        var at = Local(a + (R() - 0.5f) * 0.8f, -height * 0.5f + bh * 0.35f, side * (thick * 0.5f + 0.25f + R() * 0.9f));
                        var fall = Quaternion.Euler((R() - 0.5f) * 30f, R() * 360f, (R() - 0.5f) * 30f);
                        b.Box(p.Centre + p.Rotation * at, Local(Mathf.Max(0.2f, l - wear), bh, thick * 0.8f), p.Rotation * fall, shade * 0.95f, FloraPart.Bark);
                    }
                }
            }
        }

        /// <summary>
        /// W-101 (#80): a worn carving on one face of a ruin block: a frame line or none, then one of a few simple glyphs
        /// (a ringed cross, a chevron stack, a stepped spiral) cut as dark grooves a few millimetres proud of the face.
        /// </summary>
        static void RuinGlyph(FloraMeshBuilder b, Vector3 centre, Quaternion rot, bool alongX, float len, float high, float face, Color shade, System.Random rng)
        {
            var side = rng.NextDouble() < 0.5 ? -1f : 1f;
            var groove = shade * 0.3f;
            var w = 0.06f;
            var s = Mathf.Min(len * 0.8f, high * 0.95f);   // the glyph's square
            void Stroke(float a0, float y0, float a1, float y1)
            {
                var d = new Vector2(a1 - a0, y1 - y0);
                var l = d.magnitude + w;
                var ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                var local = new Vector3((a0 + a1) * 0.5f * s, (y0 + y1) * 0.5f * s, side * (face + 0.004f));
                var at = alongX ? local : new Vector3(local.z, local.y, local.x);
                var turn = alongX ? Quaternion.Euler(0f, 0f, ang) : Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(0f, 0f, ang);
                b.Box(centre + rot * at, new Vector3(l * s, w * s, 0.012f), rot * turn, groove, FloraPart.Bark);
            }
            switch (rng.Next(0, 3))
            {
                case 0:   // a cross in a ring of eight strokes
                    Stroke(-0.3f, 0f, 0.3f, 0f);
                    Stroke(0f, -0.3f, 0f, 0.3f);
                    for (int k = 0; k < 8; k++)
                    {
                        float t0 = k * Mathf.PI / 4f, t1 = (k + 1) * Mathf.PI / 4f;
                        Stroke(Mathf.Cos(t0) * 0.42f, Mathf.Sin(t0) * 0.42f, Mathf.Cos(t1) * 0.42f, Mathf.Sin(t1) * 0.42f);
                    }
                    break;
                case 1:   // three stacked chevrons
                    for (int k = 0; k < 3; k++)
                    {
                        var y = -0.3f + k * 0.28f;
                        Stroke(-0.35f, y, 0f, y + 0.18f);
                        Stroke(0f, y + 0.18f, 0.35f, y);
                    }
                    break;
                default:  // a squared spiral
                    float x = -0.05f, yy = -0.05f, step = 0.1f;
                    for (int k = 0; k < 6; k++, step += 0.1f)
                    {
                        float nx = x, ny = yy;
                        switch (k % 4) { case 0: nx += step; break; case 1: ny += step; break; case 2: nx -= step; break; default: ny -= step; break; }
                        Stroke(x, yy, nx, ny);
                        x = nx;
                        yy = ny;
                    }
                    break;
            }
        }

        /// <summary>
        /// A pitched kit roof slab up close (W-99): the slab itself (the collision), then overlapping courses laid on it
        /// from the eave up, each tipped so its lower edge stands proud (shingle rows, or thicker thatch layers), and on
        /// thatch a ragged fringe hanging over the eave, each tuft its own length. Flat tiers keep only the shader's
        /// courses. Drawing only, like RuinBricks; the caller sets the builder's Surface so the courses get the look too.
        /// </summary>
        /// <summary>
        /// Where the upright part <paramref name="index"/> (a wall, post, pile or chimney, turned about Y only) meets a
        /// floor, deck plank or plinth (W-99: contact shading): heights local to its centre, into
        /// <paramref name="into"/>. A slab's top counts where the upright stands on it or passes up through it, its
        /// underside where the upright holds it up or passes down through it. False when nothing meets it.
        /// </summary>
        static bool Contacts(System.Collections.Generic.List<Punchy.Terrain.KitPart> parts, int index, List<float> into)
        {
            into.Clear();
            var u = parts[index];
            if (u.Piece != Punchy.Terrain.KitPiece.Wall && u.Piece != Punchy.Terrain.KitPiece.Column && u.Piece != Punchy.Terrain.KitPiece.Chimney ||
                (u.Rotation * Vector3.up).y < 0.995f || u.Size.y < 0.4f)
                return false;
            var bottom = u.Centre.y - u.Size.y * 0.5f;
            var topY = u.Centre.y + u.Size.y * 0.5f;
            for (int j = 0; j < parts.Count && into.Count < 4; j++)
            {
                var f = parts[j];
                if (j == index || (f.Rotation * Vector3.up).y < 0.995f || f.Size.y > 0.6f)
                    continue;
                var slab = f.Piece == Punchy.Terrain.KitPiece.Floor || f.Piece == Punchy.Terrain.KitPiece.Platform || f.Piece == Punchy.Terrain.KitPiece.Balcony ||
                           f.Piece == Punchy.Terrain.KitPiece.Trim && f.Size.x > 1f && f.Size.z > 1f;   // a plinth round a house's foot
                if (!slab)
                    continue;
                // The upright's middle, in the slab's frame, must lie over the slab (give or take the upright's own half).
                var local = Quaternion.Inverse(f.Rotation) * (u.Centre - f.Centre);
                var reach = Mathf.Max(u.Size.x, u.Size.z) * 0.5f;
                if (Mathf.Abs(local.x) > f.Size.x * 0.5f + reach || Mathf.Abs(local.z) > f.Size.z * 0.5f + reach)
                    continue;
                var fTop = f.Centre.y + f.Size.y * 0.5f;
                var fBottom = f.Centre.y - f.Size.y * 0.5f;
                if (fTop > bottom - 0.15f && fTop < topY - 0.1f)
                    Add(into, Mathf.Max(fTop, bottom) - u.Centre.y);
                if (fBottom > bottom + 0.1f && fBottom < topY + 0.15f)
                    Add(into, Mathf.Min(fBottom, topY) - u.Centre.y);
            }
            return into.Count > 0;

            static void Add(List<float> list, float y)
            {
                foreach (var v in list)
                    if (Mathf.Abs(v - y) < 0.15f)   // a deck's planks and the bed under them: one join
                        return;
                list.Add(y);
            }
        }

        static void RoofCourses(FloraMeshBuilder b, in Punchy.Terrain.KitPart p, Color c, bool thatch, uint seed)
        {
            b.Box(p.Centre, p.Size, p.Rotation, c, FloraPart.Bark);
            var up = p.Rotation * Vector3.up;
            if (up.y < 0.2f || up.y > 0.97f)
                return;
            var rng = new System.Random((int)(seed & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            // The slab's local z runs down the slope one way or the other: the eave is its lower end.
            var downhill = (p.Rotation * Vector3.forward).y > 0f ? -1f : 1f;
            float course = thatch ? 0.42f : 0.26f, thick = thatch ? 0.09f : 0.035f, proud = thatch ? 0.05f : 0.022f;
            var length = p.Size.z;
            var rows = Mathf.Max(2, Mathf.FloorToInt(length / course));
            course = length / rows;
            var tip = Quaternion.Euler(-downhill * Mathf.Atan2(proud, course) * Mathf.Rad2Deg, 0f, 0f);
            var lift = p.Size.y * 0.5f + thick * 0.5f + proud * 0.5f;
            var look = b.Surface;
            if (!thatch)
                b.Surface = 4;   // the rows are geometry: the shader adds only the tiles (FloraMeshBuilder.Surface)
            for (int k = 0; k < rows; k++)
            {
                var z = downhill * (length * 0.5f - (k + 0.5f) * course);
                var shade = Color.Lerp(c * 0.9f, c * 1.07f, R());
                b.Box(p.Centre + p.Rotation * new Vector3((R() - 0.5f) * 0.06f, lift, z), new Vector3(p.Size.x - R() * 0.06f, thick, course * 1.18f),
                      p.Rotation * tip, shade, FloraPart.Bark);
            }
            b.Surface = look;
            if (!thatch)
                return;
            // The fringe: tufts along the eave, hanging a little lower and further out, each its own length and shade.
            var tufts = Mathf.Max(3, Mathf.RoundToInt(p.Size.x / 0.26f));
            var droop = Quaternion.Euler(-downhill * (8f + R() * 6f), 0f, 0f);
            for (int j = 0; j < tufts; j++)
            {
                var hang = 0.22f + R() * 0.22f;
                var x = -p.Size.x * 0.5f + (j + 0.5f) * p.Size.x / tufts + (R() - 0.5f) * 0.08f;
                var at = new Vector3(x, lift + thick * 0.2f, downhill * (length * 0.5f + hang * 0.25f));
                b.Box(p.Centre + p.Rotation * at, new Vector3(p.Size.x / tufts * (1.05f + R() * 0.3f), thick * (1.1f + R() * 0.5f), hang),
                      p.Rotation * droop, Color.Lerp(c * 0.78f, c * 0.96f, R()), FloraPart.Bark);
            }
        }
    }
}
