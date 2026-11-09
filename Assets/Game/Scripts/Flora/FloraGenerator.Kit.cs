using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, kit structures: KitStructure, stone houses, ruins, bedrolls, rail fences. Code moved from FloraGenerator.cs (W-116).
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A structure-kit building (FloraForm.KitBuilding) or its ruin (FloraForm.KitRuin), #24: StructureKit lays out
        /// the parts from a seed drawn per variant, in the species' Kit Culture trim set (a ruin takes its stone from Bark
        /// and its moss from Accent). Far LODs drop the small trim. Intact buildings collide round their walls up to the
        /// eaves; ruins per standing ground-storey bay, like Ruin. Perches on the highest tops.
        /// </summary>
        static void KitStructure(Grower g, bool ruined)
        {
            var s = g.S;
            var b = g.B;
            var width = g.Range(s.Height) * s.Spread;
            var depth = width * g.Range(0.8f, 1.05f);
            var seed = (uint)(g.R() * 16777215f) ^ ((uint)g.Variant << 24) ^ (uint)s.Seed * 0x9E3779B9u;
            var roll = g.R();
            var storeys = ruined ? (roll < 0.5f ? 1 : 2) : roll < 0.3f ? 1 : roll < 0.8f ? 2 : 3;
            var trim = Punchy.Terrain.StructureKit.For(s.KitCulture);
            if (ruined)
            {
                trim.Wall = g.Bark;
                trim.Trim = g.Bark * 0.82f;
                if (g.HasAccent)
                    trim.Moss = g.Accent;
            }
            // #36: fishing villages' shapes. Their draws come after the house's own, so every existing kit building is the same.
            var shape = ruined ? Punchy.Terrain.KitShape.House : s.KitShape;
            var lift = 0f;
            var wharfHouses = new System.Collections.Generic.List<Vector4>();
            System.Collections.Generic.List<Punchy.Terrain.KitPart> parts;
            switch (shape)
            {
                case Punchy.Terrain.KitShape.StiltHouse:
                    storeys = 1;
                    lift = g.Range(1.4f, 2.2f);
                    parts = Punchy.Terrain.StructureKit.StiltHouse(seed, trim, width, depth, lift);
                    break;
                case Punchy.Terrain.KitShape.Dock:
                    parts = Punchy.Terrain.StructureKit.Dock(seed, trim, width * 2.4f, 2.6f, 0.8f);
                    break;
                case Punchy.Terrain.KitShape.Boardwalk: // W-119 (#36): a shore boardwalk piece, its length from the variant
                    parts = Punchy.Terrain.StructureKit.Boardwalk(seed, trim, Punchy.Terrain.StructureKit.BoardwalkLength(g.Variant), Punchy.Terrain.StructureKit.BoardwalkDeck(g.Variant), g.Variant / 16);   // W-119 row 3: deck details by the run of 16
                    break;
                case Punchy.Terrain.KitShape.Shore:
                    parts = Punchy.Terrain.StructureKit.Shore(seed, trim, 2.5f, g.Variant % Punchy.Terrain.StructureKit.FishTrades);   // W-119: the variant's trade
                    break;
                case Punchy.Terrain.KitShape.Igloo:     // #38: a turtle folk's dome, one tall storey
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.Igloo(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.Alpine:    // #38: mountaintop details, walked round
                    parts = Punchy.Terrain.StructureKit.AlpineCluster(seed, trim, 2.5f);
                    break;
                case Punchy.Terrain.KitShape.ShellDome: // #37: sea folk on the lake bed
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.ShellDome(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.Spire:     // #37: Height is the spire's full height, tall enough to break the lake surface
                    parts = Punchy.Terrain.StructureKit.Spire(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.Pier:      // #37: Height is the deck's length; piles reach 30 m down to a deep bed
                    parts = Punchy.Terrain.StructureKit.Pier(seed, trim, width, 2.8f, 1.0f, 30f);
                    break;
                case Punchy.Terrain.KitShape.Reef:      // #37: lake-bed details
                    parts = Punchy.Terrain.StructureKit.Reef(seed, trim, 3f);
                    break;
                case Punchy.Terrain.KitShape.GlowStone: // W-120 (#37): Height is the stone's height
                    parts = Punchy.Terrain.StructureKit.GlowStone(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.Wreck:     // W-120 (#37): Height is the boat's length
                    parts = Punchy.Terrain.StructureKit.Wreck(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.ClamBed:   // W-120 (#37): Height is the bed's spread
                    parts = Punchy.Terrain.StructureKit.ClamBed(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.VentField: // W-120 (#37): Height is the field's spread
                    parts = Punchy.Terrain.StructureKit.VentField(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.ShellBuoy: // W-121 (#37): Height is the float's width; it rides the lake surface (Floats)
                    parts = Punchy.Terrain.StructureKit.ShellBuoy(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.SeaRaft:   // W-121 (#37): Height is the raft's length; it floats by the landing (Floats)
                    parts = Punchy.Terrain.StructureKit.SeaRaft(seed, trim, width);
                    break;
                case Punchy.Terrain.KitShape.MarshHut:  // W-122: a marsh folk hut on stilts, its walk out to the smoke hut
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.MarshHut(seed, trim, width, depth, Punchy.Terrain.StructureKit.MarshRing, g.Variant % 3);
                    break;
                case Punchy.Terrain.KitShape.SmokeHut:  // W-122: the marsh folk's smoke hut on its deck
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.SmokeHut(seed, trim, width, g.Variant % 3);
                    break;
                case Punchy.Terrain.KitShape.MarshCross: // W-122: Height is the walk's length
                case Punchy.Terrain.KitShape.MarshWalk:
                    parts = Punchy.Terrain.StructureKit.MarshCross(seed, trim, width, shape == Punchy.Terrain.KitShape.MarshCross, g.Variant % 3);
                    break;
                case Punchy.Terrain.KitShape.Wharf:     // W-65: walkways and houses over the water, laid out by the seed
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.Wharf(seed, trim, 1.0f, wharfHouses);
                    break;
                case Punchy.Terrain.KitShape.AshHouse:   // #69: harsh-biome hazard houses, one storey each
                case Punchy.Terrain.KitShape.SnowHouse:
                case Punchy.Terrain.KitShape.StormHouse:
                case Punchy.Terrain.KitShape.SandHouse:
                    storeys = 1;
                    parts = Punchy.Terrain.StructureKit.HazardHouse(seed, trim, shape, width, depth);
                    break;
                case Punchy.Terrain.KitShape.SlabShelter:   // W-95 (#69): Bark is the local rock, the accent its cap (snow, ash, sand)
                    storeys = 1;
                    trim.Wall = g.Bark;
                    trim.Trim = g.HasAccent ? g.Accent : g.Bark * 1.1f;
                    parts = Punchy.Terrain.StructureKit.SlabShelter(seed, trim, width, depth);
                    break;
                default:
                    parts = Punchy.Terrain.StructureKit.House(seed, trim, width, depth, storeys, ruined ? g.Range(0.45f, 0.9f) : 0f);
                    break;
            }
            var house = Punchy.Terrain.StructureKit.HouseLike(shape) && shape != Punchy.Terrain.KitShape.SlabShelter ||   // a slab shelter is walked into
                        shape == Punchy.Terrain.KitShape.Igloo || shape == Punchy.Terrain.KitShape.ShellDome;

            var looks = Punchy.Terrain.StructureKit.Surfaces(s.KitCulture, seed);
            var contacts = new List<float>();
            var first = b.VertexCount;
            var top = 0f;
            var perch = Vector3.zero;
            var eaves = house ? lift + storeys * trim.Storey : 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var small = p.Piece == Punchy.Terrain.KitPiece.Trim || p.Piece == Punchy.Terrain.KitPiece.Rail ||
                            p.Piece == Punchy.Terrain.KitPiece.Rubble || p.Piece == Punchy.Terrain.KitPiece.Moss || p.Piece == Punchy.Terrain.KitPiece.MossCap ||
                            (p.Piece == Punchy.Terrain.KitPiece.Window && p.Material != Punchy.Terrain.KitMaterial.Opening);
                if (small && g.Lod >= 2)
                    continue;
                var lit = house && !ruined && p.Piece == Punchy.Terrain.KitPiece.Window && p.Material == Punchy.Terrain.KitMaterial.Opening;
                var c = Punchy.Terrain.StructureKit.Colour(trim, p.Material);
                var jitter = ((i * 2654435761u) >> 24) / 255f;                 // a little per-stone variety, the same at every LOD
                c = Color.Lerp(c * 0.94f, c * 1.08f, jitter);
                if (p.Piece == Punchy.Terrain.KitPiece.MossCap)
                {
                    // W-64 (#24): one ragged, see-through sheet draped over the stone (was a 9 cm box that read as a pad).
                    b.MossSheet(p.Centre, p.Size, p.Rotation, c, (uint)i * 2654435761u ^ seed);
                    continue;
                }
                // W-99: planks, shingles or thatch on the folk's timber walls and roofs, fine grain on bare timber.
                var look = ruined ? Punchy.Terrain.KitSurface.Plain : p.Material == Punchy.Terrain.KitMaterial.Wall ? looks.Wall :
                           p.Material == Punchy.Terrain.KitMaterial.Roof ? looks.Roof : Punchy.Terrain.KitSurface.Plain;
                b.Surface = (int)look;
                b.Wood = p.Material == Punchy.Terrain.KitMaterial.Wood;
                b.Unfounded = p.Afloat;
                if (ruined && g.Lod == 0 && p.Piece == Punchy.Terrain.KitPiece.Wall && p.Material == Punchy.Terrain.KitMaterial.Wall)
                    RuinBricks(b, p, c, seed ^ (uint)i * 0x9E3779B9u);   // W-94 (#80): old coursed blocks up close
                else if (g.Lod == 0 && p.Piece == Punchy.Terrain.KitPiece.Roof && (look == Punchy.Terrain.KitSurface.Shingles || look == Punchy.Terrain.KitSurface.Thatch))
                    RoofCourses(b, p, c, look == Punchy.Terrain.KitSurface.Thatch, seed ^ (uint)i * 0x85EBCA77u);
                else if (lit)
                {
                    // W-127: a lived-in house's window glass is a lit pane (dark glass by day, lamplight at night, each
                    // pane its own hour: the colour keys it), and its light spills out onto the street (WindowSpill).
                    b.Box(p.Centre, p.Size, p.Rotation, Punchy.Terrain.StructureKit.Colour(trim, Punchy.Terrain.KitMaterial.Lamp) * Mathf.Lerp(0.9f, 1.05f, jitter), FloraPart.Window);
                    var outward = p.Rotation * Vector3.forward;
                    if (outward.x * p.Centre.x + outward.z * p.Centre.z < 0f)
                        outward = -outward;
                    g.Windows.Add(new Vector4(p.Centre.x, p.Centre.y, p.Centre.z, Mathf.Atan2(outward.z, outward.x)));
                }
                else if (g.Lod < 2 && Contacts(parts, i, contacts))
                    b.BandedBox(p.Centre, p.Size, p.Rotation, c, FloraPart.Bark, contacts, Mathf.Min(0.5f, p.Size.y * 0.3f), 0.7f);
                else
                    b.Box(p.Centre, p.Size, p.Rotation, c, p.Material == Punchy.Terrain.KitMaterial.Moss ? FloraPart.Foliage :
                                                           p.Material == Punchy.Terrain.KitMaterial.Lamp || p.Material == Punchy.Terrain.KitMaterial.Rune ? FloraPart.Window : FloraPart.Bark);
                b.Surface = 0;
                b.Wood = false;
                b.Unfounded = false;
                var high = p.Centre.y + p.Size.y * 0.5f;
                top = Mathf.Max(top, high);
                // W-67 (#79): every part is a solid box unless it's decor (moss, trim, windows, lamps and runes): the
                // collision is the art. Only LOD 0 keeps them (that's the one FloraModel copies).
                var decor = p.Piece == Punchy.Terrain.KitPiece.Moss || p.Piece == Punchy.Terrain.KitPiece.Trim ||
                            p.Piece == Punchy.Terrain.KitPiece.Window || p.Material == Punchy.Terrain.KitMaterial.Lamp ||
                            p.Material == Punchy.Terrain.KitMaterial.Rune || p.Material == Punchy.Terrain.KitMaterial.Moss;
                // W-113 (item 10): a ruin's plinth course (trim, but a 0.5 m slab under the whole floor) is solid too, so a
                // body walks up onto the platform rather than sinking to the ground through it. The door step (a low, small
                // ground trim) is solid with it, so the climb is two rises under KitStep.
                if (ruined && p.Piece == Punchy.Terrain.KitPiece.Trim && p.Centre.y - p.Size.y * 0.5f < 0.05f && (p.Size.x * p.Size.z > 4f || p.Size.y < 0.3f))
                    decor = false;
                if (g.Lod == 0 && !decor)
                    g.Boxes.Add(new KitBox { Centre = p.Centre, Half = p.Size * 0.5f, Rotation = p.Rotation, Reach = p.Size.magnitude * 0.5f });
                if (p.Solid)
                {
                    var r = Mathf.Max(p.Size.x, p.Size.z) * 0.55f;
                    if (ruined)
                    {
                        if (high > 0.7f)
                            g.Colliders.Add(new Vector4(p.Centre.x, p.Centre.z, r, high));
                    }
                    else
                    {
                        // A wall's circle is set back so its outer edge sits on the wall's outer face: centred on the wall
                        // it bulged ~1.2 m out over the door step and any outside stair (W-58b walk test).
                        var at = new Vector2(p.Centre.x, p.Centre.z);
                        if (p.Piece == Punchy.Terrain.KitPiece.Wall && wharfHouses.Count > 0)
                        {
                            // W-65: a wharf's houses stand off the origin; set each wall back from its own house's centre.
                            var home = wharfHouses[0];
                            foreach (var h in wharfHouses)
                                if ((new Vector2(h.x, h.y) - at).sqrMagnitude < (new Vector2(home.x, home.y) - at).sqrMagnitude)
                                    home = h;
                            var local = at - new Vector2(home.x, home.y);
                            var outward = Mathf.Abs(local.x) / home.z > Mathf.Abs(local.y) / home.w ? new Vector2(Mathf.Sign(local.x), 0f) : new Vector2(0f, Mathf.Sign(local.y));
                            at -= outward * Mathf.Max(0f, r - Punchy.Terrain.StructureKit.Thick * 0.5f);
                        }
                        else if (p.Piece == Punchy.Terrain.KitPiece.Wall)
                        {
                            var outward = Mathf.Abs(at.x) / width > Mathf.Abs(at.y) / depth ? new Vector2(Mathf.Sign(at.x), 0f) : new Vector2(0f, Mathf.Sign(at.y));
                            at -= outward * Mathf.Max(0f, r - Punchy.Terrain.StructureKit.Thick * 0.5f);
                        }
                        g.Colliders.Add(new Vector4(at.x, at.y, r, Mathf.Max(high, p.Piece == Punchy.Terrain.KitPiece.Wall && p.Size.x * p.Size.z > 4f ? high : eaves)));
                    }
                }
                if ((p.Piece == Punchy.Terrain.KitPiece.Wall || p.Piece == Punchy.Terrain.KitPiece.Roof) && high > perch.y)
                    perch = new Vector3(p.Centre.x, high, p.Centre.z);
            }
            if (!ruined && house)   // the inside: one fat cylinder so nothing walks through the middle of the house
            {
                var inner = Mathf.Min(width, depth) * 0.45f;
                // W-64 (#38): an igloo's door stands well inside the dome now; the cylinder stops 0.4 m short of it.
                if (shape == Punchy.Terrain.KitShape.Igloo)
                    foreach (var p in parts)
                        if (p.Piece == Punchy.Terrain.KitPiece.Door)
                            inner = Mathf.Min(inner, p.Centre.z - 0.4f);
                g.Colliders.Add(new Vector4(0f, 0f, inner, eaves));
                // W-65c (#38): a collider-only circle behind the igloo's door (no mesh, so nothing shows through), its
                // front 0.1 m before the door slab: walkers stop at the door line, not inside it.
                if (shape == Punchy.Terrain.KitShape.Igloo)
                    foreach (var p in parts)
                        if (p.Piece == Punchy.Terrain.KitPiece.Door)
                            g.Colliders.Add(new Vector4(p.Centre.x, p.Centre.z + 0.2f - 0.95f, 0.95f, p.Size.y));
            }
            foreach (var h in wharfHouses)  // W-65: one inside cylinder per house on the wharf, up to its eaves
                g.Colliders.Add(new Vector4(h.x, h.y, Mathf.Min(h.z, h.w) * 0.45f, 1.0f + trim.Storey));
            if (perch.y > 1f)
                g.Perches.Add(perch);
            if (shape == Punchy.Terrain.KitShape.SmokeHut)   // W-122: smoke from the smoke hut's stack
                foreach (var p in parts)
                    if (p.Piece == Punchy.Terrain.KitPiece.Chimney)
                        g.Chimneys.Add(p.Centre + Vector3.up * (p.Size.y * 0.5f + 0.1f));
            b.Recolor(first, (p, c) => p.y < 0.5f ? c * 0.82f : c);   // darker footing
            if (ruined && g.Lod < 2)
                RuinSand(g, parts, width, depth, seed);
            b.Occlude(first, (p, n) => GroundOcclusion(p, Mathf.Min(top, 4f) * 0.5f));
            g.Height = top;
            g.Width = shape == Punchy.Terrain.KitShape.Wharf ? 30f : Mathf.Max(width, depth) * 1.4f;
            if (shape >= Punchy.Terrain.KitShape.MarshHut)   // W-122: a marsh hut's walk reaches well past the hut
            {
                var reach = 0f;
                foreach (var p in parts)
                    reach = Mathf.Max(reach, Mathf.Abs(p.Centre.x) + p.Size.magnitude * 0.5f, Mathf.Abs(p.Centre.z) + p.Size.magnitude * 0.5f);
                g.Width = Mathf.Max(g.Width, reach * 2f);
            }
            // (A dock or a shore cluster is walked on and round, not a trunk to bump into.)
            g.TrunkRadius = house ? Mathf.Max(width, depth) * 0.55f : 0f;
        }

        static void StoneHouse(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;      // half the wall's width
            var wall = height * 0.65f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 1.1f;
            var corner = half * 1.414f;              // a 4-sided tube's radius to its corners
            var square = Quaternion.Euler(0f, 45f, 0f);

            // Walls: a block with flat faces (Z-32: a four-sided tube's smoothed normals turned 45 degrees at each corner,
            // so every wall shaded from lit edges to a dark middle, or the reverse, and at a glancing view the
            // double-sided flip caught part of a wall).
            var first = b.VertexCount;
            b.Box(new Vector3(0f, (wall - 0.15f) * 0.5f, 0f), new Vector3(half * 2f, wall + 0.15f, half * 2f), Quaternion.identity, g.Bark, FloraPart.Bark);
            b.Recolor(first, (p, c) => p.y < 0.6f ? c * 0.8f : c);   // darker footing
            b.Occlude(first, (p, n) => GroundOcclusion(p, wall * 0.5f));

            // Wall detail: dressed quoins up each corner (alternating long and short blocks), a plinth course round
            // the foot, and a string course under the eaves.
            if (g.Lod < 2)
            {
                var dressed = g.Bark * 1.18f;
                var course = 0.42f;
                for (int c = 0; c < 4; c++)
                {
                    var cx = (c & 1) == 0 ? -half : half;
                    var cz = (c & 2) == 0 ? -half : half;
                    for (int k = 0; (k + 0.5f) * course < wall - 0.2f; k++)
                    {
                        var longOnX = k % 2 == 0;
                        var size = new Vector3(longOnX ? 0.7f : 0.35f, course * 0.92f, longOnX ? 0.35f : 0.7f);
                        var at = new Vector3(cx - Mathf.Sign(cx) * (size.x * 0.5f - 0.04f), (k + 0.5f) * course, cz - Mathf.Sign(cz) * (size.z * 0.5f - 0.04f));
                        b.Box(at, size, Quaternion.identity, dressed * g.Range(0.92f, 1.08f), FloraPart.Bark);
                    }
                }
                b.Box(new Vector3(0f, 0.2f, 0f), new Vector3(half * 2f + 0.14f, 0.4f, half * 2f + 0.14f), Quaternion.identity, g.Bark * 0.72f, FloraPart.Bark);
                b.Box(new Vector3(0f, wall - 0.15f, 0f), new Vector3(half * 2f + 0.12f, 0.16f, half * 2f + 0.12f), Quaternion.identity, dressed, FloraPart.Bark);
            }

            // Low pyramid roof with an overhang, and a chimney.
            var roof = b.VertexCount;
            RoofTube(b, wall - 0.24f, corner * 1.2f, 4, g.FoliageColor(), FloraPart.Bark, height, 0.05f, 0f, 0f);   // rounded lip, not a knife edge (#32)
            RotateFrom(b, roof, square);
            b.Occlude(roof, (p, n) => Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(n.y + 0.3f)));
            var chimney = new Vector3(half * 0.55f, 0f, -half * 0.4f);
            var stack = b.VertexCount;
            b.Tube(new List<Vector3> { chimney + Vector3.up * (wall * 0.8f), chimney + Vector3.up * (height + 0.5f) },
                   new List<float> { 0.32f, 0.3f }, 4, g.Bark * 0.85f, FloraPart.Bark);
            RotateAround(b, stack, chimney, square);
            ChimneyTop(g, chimney + Vector3.up * (height + 0.5f), 0.3f * 0.7071f, g.Bark * 0.85f);
            g.Perches.Add(Vector3.up * height);
            g.Perches.Add(chimney + Vector3.up * (height + 0.5f));

            Doorway(g, half + 0.02f, Mathf.Min(half * 0.6f, 1.25f), Mathf.Min(wall * 0.8f, HumanDoor));
            // A window in the middle of each side wall and the back (the four-way Windows would put them on corners).
            if (g.HasAccent && g.Lod < 2)
                foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.back })
                    Window(g, dir * (half + 0.03f) + Vector3.up * wall * 0.55f, Quaternion.LookRotation(dir), 0.6f, 0.75f);
            // ...and one either side of the door.
            if (g.HasAccent && g.Lod < 2 && half > 1.6f)
                foreach (var x in new[] { -1f, 1f })
                    Window(g, new Vector3(x * half * 0.62f, wall * 0.55f, half + 0.03f), Quaternion.identity, 0.6f, 0.75f);
            // Collision: a square, not the circle round its middle, so its corners stop you too.
            for (int k = 0; k < 4; k++)
                g.Colliders.Add(new Vector4((k & 1) == 0 ? -half * 0.5f : half * 0.5f, (k & 2) == 0 ? -half * 0.5f : half * 0.5f, half * 0.55f, wall));
            DoorMat(g, half + 0.02f, Mathf.Min(half * 0.6f, 1.25f));
        }

        /// <summary>
        /// A ruined stone house: four walls of stone piers, each wall's tops sloping down towards one end (the way walls
        /// fall, from a corner) with a ragged edge, one wall nearly gone, a gap for the door at +Z, a window hole in a
        /// tall side wall, moss on some broken tops and fallen stones round the foot. No roof. W-106 (#80): up close each
        /// pier is old coursed stone brick (StructureKit's ruin blocks, RuinBricks, seeded per pier so no two walls
        /// match), with a few blocks fallen at its foot and sand drifted against some walls.
        /// </summary>
        static void Ruin(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var height = g.Range(s.Height);
            var half = height * s.Spread * 0.5f;
            g.Height = height;
            g.Width = half * 2.8f;
            g.TrunkRadius = half * 1.1f;
            const float thick = 0.55f;
            var pier = g.Sides(1, 2, 4) * 0.55f;      // finer piers up close, so the broken edge reads as stones
            var doorHalf = Mathf.Min(half * 0.3f, 0.7f);
            var gone = g.Variant % 4;                   // the wall that has nearly all fallen

            // W-106 (#80): a pier up close is laid as brick courses (a kit wall part through RuinBricks); its seed comes
            // from the species, variant, wall and pier, never from g.R(), so every ruin keeps its layout and collision.
            var brickSeed = (uint)s.Seed * 0x9E3779B9u ^ (uint)g.Variant * 0x85EBCA77u;
            void Pier(Vector3 centre, Vector3 size, Quaternion facing, Color colour, uint pierSeed)
            {
                if (g.Lod == 0)
                    RuinBricks(b, new Punchy.Terrain.KitPart { Piece = Punchy.Terrain.KitPiece.Wall, Material = Punchy.Terrain.KitMaterial.Wall,
                                                               Centre = centre, Size = size, Rotation = facing }, colour, pierSeed);
                else
                    b.Box(centre, size, facing, colour, FloraPart.Bark);
            }

            var first = b.VertexCount;
            for (int w = 0; w < 4; w++)
            {
                var perch = Vector3.zero;
                var facing = Quaternion.Euler(0f, w * 90f, 0f);     // wall 0 faces +Z, the front
                var keep = w == gone ? g.Range(0.06f, 0.2f) : g.Range(0.45f, 1f);
                var length = half * 2f + thick;
                var n = Mathf.Max(2, Mathf.RoundToInt(length / pier));
                var step = length / n;
                var fromLeft = g.R() < 0.5f;
                for (int i = 0; i < n; i++)
                {
                    var x = -length * 0.5f + (i + 0.5f) * step;
                    if (w == 0 && Mathf.Abs(x) < doorHalf)
                        continue;
                    var u = (i + 0.5f) / n;
                    var top = Mathf.Max(0.25f, height * keep * Mathf.Lerp(0.3f, 1f, fromLeft ? u : 1f - u) * g.Range(0.75f, 1.05f));
                    var colour = Color.Lerp(g.Bark, g.Bark * 1.15f, g.R());
                    var pierSeed = brickSeed ^ (uint)(w * 64 + i + 1) * 0xC2B2AE35u;
                    // A window hole: a pier in the middle of a tall side wall, split round the opening.
                    if (w != 0 && i == n / 2 && top > 2.3f && g.Lod < 2)
                    {
                        Pier(facing * new Vector3(x, 0.4f, half), new Vector3(step * 1.02f, 1.1f, thick), facing, colour, pierSeed);
                        Pier(facing * new Vector3(x, (1.85f + top) * 0.5f, half), new Vector3(step * 1.02f, top - 1.85f, thick), facing, colour, pierSeed ^ 0x27D4EB2Fu);
                    }
                    else
                        Pier(facing * new Vector3(x, top * 0.5f - 0.15f, half), new Vector3(step * 1.02f, top + 0.3f, thick), facing, colour, pierSeed);
                    // Moss on the broken top: up close it sits down among the ragged top course, not on a slab over it.
                    if (g.HasAccent && g.Lod < 2 && g.R() < s.AccentAmount)
                        b.Box(facing * new Vector3(x, top + (g.Lod == 0 ? -0.07f : 0.03f), half), new Vector3(step * 0.95f, 0.07f, thick * 1.06f), facing, g.Accent, FloraPart.Foliage);
                    if (top > 0.7f)
                    {
                        var c = facing * new Vector3(x, 0f, half);
                        g.Colliders.Add(new Vector4(c.x, c.z, Mathf.Max(step, thick) * 0.6f, top));
                    }
                    if (top > perch.y)
                        perch = facing * new Vector3(x, top, half);
                }
                if (perch.y > 1f)
                    g.Perches.Add(perch);
            }
            b.Recolor(first, (p, c) => p.y < 0.5f ? c * 0.8f : c);   // darker footing
            b.Occlude(first, (p, n) => GroundOcclusion(p, height * 0.5f));

            // Fallen stones, most of them below the wall that came down.
            if (g.Lod < 2)
            {
                var rubble = b.VertexCount;
                var fallen = Quaternion.Euler(0f, gone * 90f, 0f);
                var stones = g.Count(s.Branches);
                for (int k = 0; k < stones; k++)
                {
                    var size = g.Range(0.2f, 0.5f);
                    var at = k % 3 != 0
                        ? fallen * new Vector3(g.Range(-half, half), 0f, half + g.Range(-0.8f, 1.6f))
                        : new Vector3(g.Range(-half, half) * 1.3f, 0f, g.Range(-half, half) * 1.3f);
                    at.y = size * 0.25f;
                    b.Box(at, new Vector3(size * g.Range(1f, 1.8f), size, size * g.Range(0.8f, 1.2f)),
                          Quaternion.Euler(g.Range(-15f, 15f), g.R() * 360f, g.Range(-15f, 15f)), Color.Lerp(g.Bark * 0.85f, g.Bark * 1.1f, g.R()), FloraPart.Bark);
                }
                b.Occlude(rubble, (p, n) => GroundOcclusion(p, 0.5f));

                // W-106 (#80): sand drifted against the foot of one or two walls, banked higher in the middle of the wall
                // and thinning out to its ends (its own rng, so the ruin's other draws are unchanged).
                var drifts = new System.Random((int)(brickSeed & 0x7FFFFFFF));
                var sand = new Color(0.76f, 0.66f, 0.48f);
                var drift = b.VertexCount;
                for (int w = 0; w < 4; w++)
                {
                    if (w == gone || drifts.NextDouble() < 0.55)
                        continue;
                    var facing = Quaternion.Euler(0f, w * 90f, 0f);
                    var side = drifts.NextDouble() < 0.6 ? 1f : -1f;       // outside the wall, or banked up inside it
                    var lumps = 2 + drifts.Next(3);
                    for (int k = 0; k < lumps; k++)
                    {
                        var x = ((k + 0.5f) / lumps - 0.5f) * half * 1.7f + (float)(drifts.NextDouble() - 0.5) * 0.6f;
                        var bank = 0.35f + (float)drifts.NextDouble() * 0.4f * (1f - Mathf.Abs(x) / half);
                        var reach = 0.8f + (float)drifts.NextDouble() * 0.7f;
                        var at = facing * new Vector3(x, 0f, half + side * (thick * 0.5f + reach * 0.35f));
                        b.Blob(at, new Vector3(half * 0.55f, bank, reach), facing * Quaternion.Euler(0f, (float)(drifts.NextDouble() - 0.5) * 20f, 0f),
                               1, 0.35f, (float)drifts.NextDouble() * 100f, Color.Lerp(sand, g.Bark, 0.2f + (float)drifts.NextDouble() * 0.15f), FloraPart.Bark);
                    }
                }
                b.Occlude(drift, (p, n) => GroundOcclusion(p, 0.4f));
            }
        }

        /// <summary>
        /// A bedroll along Z, feet at +Z, soft: a lumpy pad a hand's height thick, a second blanket over most of it thrown
        /// back a little at the head, and a rolled pillow across the head end, all shaded as cloth (held still).
        /// </summary>
        static void Bedroll(Grower g)
        {
            var s = g.S;
            var b = g.B;
            var length = g.Range(s.Height);
            var width = length * s.Spread;
            g.Height = 0.18f;
            g.Width = length;
            g.TrunkRadius = 0f;
            var first = b.VertexCount;
            var detail = g.Sides(3, 2, 1);
            // Rounded-off, flattened blobs with broad lumps (folds), not boxes: boxes read as planks.
            b.Blob(new Vector3(0f, 0.02f, 0f), new Vector3(width * 0.5f, 0.06f, length * 0.5f), Quaternion.identity, detail, 0.25f,
                   g.R() * 50f, s.Foliage.linear, FloraPart.Bark, 1f, 0.7f, 0.6f);
            b.Blob(new Vector3(g.Range(-0.03f, 0.03f), 0.07f, length * 0.12f), new Vector3(width * 0.47f, 0.05f, length * 0.36f),
                   Quaternion.Euler(0f, g.Range(-4f, 4f), 0f), detail, 0.3f, g.R() * 50f,
                   Color.Lerp(s.Foliage.linear, s.FoliageVariation.linear, 0.7f), FloraPart.Bark, 1f, 0.6f, 0.6f);
            b.Blob(new Vector3(0f, 0.08f, -length * 0.4f), new Vector3(width * 0.42f, 0.08f, 0.11f), Quaternion.identity, detail, 0.15f,
                   g.R() * 50f, g.Bark, FloraPart.Bark, 1f, 0.3f, 0.6f);
            b.Occlude(first, (p, n) => GroundOcclusion(p, 0.15f));
            b.Cloth(first, p => 0f);
        }

        /// <summary>
        /// One 3 m length of rail fence along X, laid end to end: a split post at its start (-X end), leaning a touch,
        /// and two rails nailed to its face on into the next length's post (a little past where it stands, so a slightly
        /// wider spacing on a bend still meets it), boards on edge (thin against the post, as nailed), each sagging a
        /// little in the middle.
        /// </summary>
        static void RailFence(Grower g)
        {
            var s = g.S;
            var b = g.B;
            b.Wood = true;   // timber throughout, with its grain (Z-30)
            var height = g.Range(s.Height);
            const float length = 3f;
            g.Height = height;
            g.Width = length;
            g.TrunkRadius = 0f;
            var wood = g.Bark;
            var post = new Vector3(-length * 0.5f, 0f, 0f);
            var lean = new Vector3(g.Range(-0.02f, 0.02f), 0f, g.Range(-0.02f, 0.02f));
            b.Box(post + Vector3.up * (height * 0.5f - 0.1f) + lean * 0.5f, new Vector3(0.14f, height + 0.2f, 0.14f),
                  Quaternion.Euler(lean.z * 40f, g.Range(0f, 90f), -lean.x * 40f), wood * g.Range(0.85f, 1.05f), FloraPart.Bark);
            if (g.Lod < 2)
                b.Box(post + Vector3.up * (height + 0.02f) + lean, new Vector3(0.16f, 0.04f, 0.16f), Quaternion.identity, s.Foliage.linear, FloraPart.Bark);
            g.Perches.Add(post + Vector3.up * (height + 0.04f) + lean);
            // A slimmer, shorter post at its far end: hidden inside the next length's own post where the run goes on,
            // closing it off where the run stops.
            b.Box(post + Vector3.right * length + Vector3.up * (height * 0.5f - 0.12f), new Vector3(0.11f, height + 0.12f, 0.11f),
                  Quaternion.identity, wood * 0.9f, FloraPart.Bark);
            foreach (var y in new[] { 0.42f, 0.82f })
            {
                var sag = g.Range(0.01f, 0.05f);
                // Flush on the post's face: half the post (0.07) plus half the board's thickness.
                var a = post + Vector3.up * height * y + new Vector3(0.05f, 0f, 0.086f);
                var c = a + Vector3.right * (length + 0.1f);
                var mid = (a + c) * 0.5f + Vector3.down * sag;
                b.Beam(a, mid, 0.03f, 0.12f, wood * g.Range(0.8f, 1f), FloraPart.Bark);
                b.Beam(mid, c, 0.03f, 0.12f, wood * g.Range(0.8f, 1f), FloraPart.Bark);
            }
            FenceColliders(g, post, post + Vector3.right * length, height);
        }

        /// <summary>Rotates vertices added since <paramref name="first"/> about the vertical axis through the origin.</summary>
        static void RotateFrom(FloraMeshBuilder b, int first, Quaternion q) => b.Transform(first, p => q * p, n => q * n);

        static void RotateAround(FloraMeshBuilder b, int first, Vector3 centre, Quaternion q) =>
            b.Transform(first, p => centre + q * (p - centre), n => q * n);
    }
}
