using System.Collections.Generic;
using UnityEngine;
using Punchy.Terrain;

namespace Punchy.Flora
{
    // FloraGenerator, the building parts (W-175 row 5, #415): walls, floors, doors, windows, a gabled roof, beams, stairs,
    // rails, signs, lanterns and crates as unit parts, so a Model Lab blueprint (or any unit) can be built from them. Each
    // part lays out structure-kit parts (StructureKit's KitPart: a piece, a kit material, a box) and draws them through the
    // kit's own DrawKitPart, so the host folk's trim colours and wall and roof looks apply and every piece takes its W-166
    // family from MaterialLibrary.FamilyOf, the one table the kit buildings use. Every solid piece is a collision box (the
    // collision is the art, W-67); lamps and glass stay walk-through, as in the kit. Lanterns and crates are the kit's own
    // (StructureKit.Parts, rule 33). See docs/agents/parts-and-units.md section 10.
    public static partial class FloraGenerator
    {
        /// <summary>The building part ids (row 5), for the tests: each grows a mesh, and all but the lantern a box.</summary>
        public static readonly string[] BuildPartIds =
            { "wall", "floor", "door", "window", "gabled roof", "beam", "stair", "rail", "sign", "lantern", "crate" };

        static void AddBuildParts(Dictionary<string, PartBuild> library)
        {
            library["wall"] = WallPart;
            library["floor"] = FloorPart;
            library["door"] = DoorPart;
            library["window"] = WindowPart;
            library["gabled roof"] = GabledRoofPart;
            library["beam"] = BeamPart;
            library["stair"] = StairPart;
            library["rail"] = RailPart;
            library["sign"] = SignPart;
            library["lantern"] = LanternPart;
            library["crate"] = CratePart;
        }

        /// <summary>
        /// Decor in the kit's sense (moss, trim, ladders, glass, lamps, flames, runes): drawn but walked through. Shared by
        /// KitStructure and the building parts, so a piece collides the same wherever it stands.
        /// </summary>
        static bool KitDecor(in KitPart p) =>
            p.Piece == KitPiece.Moss || p.Piece == KitPiece.Trim || p.Piece == KitPiece.Ladder || p.Piece == KitPiece.Window ||
            p.Material == KitMaterial.Lamp || p.Material == KitMaterial.Flame || p.Material == KitMaterial.Rune || p.Material == KitMaterial.Moss;

        /// <summary>
        /// Draws a building part's kit pieces in the host's folk trim and looks (KitCulture), dropping small trim at the far
        /// level of detail as KitStructure does, and adds a collision box for each piece that isn't decor or is marked
        /// solid (a crate's body) at LOD 0.
        /// </summary>
        static void DrawBuildParts(Grower g, List<KitPart> parts)
        {
            var culture = g.S.KitCulture;
            var seed = (uint)g.Seed * 0x9E3779B9u;
            var trim = StructureKit.For(culture);
            var looks = StructureKit.Surfaces(culture, seed);
            var contacts = new List<float>();
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var small = p.Piece == KitPiece.Trim || p.Piece == KitPiece.Rail || (p.Piece == KitPiece.Window && p.Material != KitMaterial.Opening);
                if (small && g.Lod >= 2)
                    continue;
                if (!DrawKitPart(g, parts, i, trim, looks, false, false, seed, contacts))
                    continue;
                if (g.Lod == 0 && (!KitDecor(p) || p.Solid))
                    g.Boxes.Add(new KitBox { Centre = p.Centre, Half = p.Size * 0.5f, Rotation = p.Rotation, Reach = p.Size.magnitude * 0.5f });
            }
        }

        static void Piece(List<KitPart> parts, KitPiece piece, KitMaterial m, Vector3 centre, Vector3 size, Quaternion rotation, bool solid = false) =>
            parts.Add(new KitPart { Piece = piece, Material = m, Centre = centre, Size = size, Rotation = rotation, Solid = solid });

        /// <summary>Span, with any axis left at zero taken from a default in metres.</summary>
        static Vector3 SpanOr(UnitFrame f, in UnitPart p, Vector3 fallback)
        {
            var s = f.Span(p);
            return new Vector3(s.x > 0f ? s.x : fallback.x, s.y > 0f ? s.y : fallback.y, s.z > 0f ? s.z : fallback.z);
        }

        /// <summary>The yaw of the line from a to b on the ground (0 along +x), for parts laid along a line.</summary>
        static Quaternion Along(Vector3 a, Vector3 b)
        {
            var d = b - a;
            return Quaternion.Euler(0f, d.x == 0f && d.z == 0f ? 0f : -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg, 0f);
        }

        /// <summary>A wall from At to To along the ground (its foot line), Span.y high and Span.z thick (kit thickness by default).</summary>
        static void WallPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 a = f.At(p), b = f.To(p), s = SpanOr(f, p, new Vector3(0f, 2.6f, StructureKit.Thick));
            var length = Mathf.Max(0.1f, Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)));
            var parts = new List<KitPart>();
            Piece(parts, KitPiece.Wall, KitMaterial.Wall, (a + b) * 0.5f + Vector3.up * s.y * 0.5f, new Vector3(length, s.y, s.z), Along(a, b), true);
            DrawBuildParts(g, parts);
        }

        /// <summary>A floor or deck: a timber slab Span in size (0.16 m thick by default), its top at At.</summary>
        static void FloorPart(Grower g, UnitFrame f, UnitPart p)
        {
            var s = SpanOr(f, p, new Vector3(3f, 0.16f, 3f));
            var parts = new List<KitPart>();
            Piece(parts, KitPiece.Floor, KitMaterial.Wood, f.At(p) - Vector3.up * s.y * 0.5f, s, Quaternion.identity);
            DrawBuildParts(g, parts);
        }

        /// <summary>
        /// A door facing +z: a plank leaf Span.x wide and Span.y high standing on At, a lintel over it and a stone step in
        /// front. The leaf is solid (a closed door); the wall round it is its own part.
        /// </summary>
        static void DoorPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(1.1f, 2.1f, 0.12f));
            var parts = new List<KitPart>();
            var q = Quaternion.identity;
            Piece(parts, KitPiece.Door, KitMaterial.Wood, at + new Vector3(0f, s.y * 0.5f, 0f), s, q);
            Piece(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, s.y + 0.12f, 0.1f), new Vector3(s.x + 0.4f, 0.24f, StructureKit.Thick * 0.6f), q);
            Piece(parts, KitPiece.Trim, KitMaterial.Stone, at + new Vector3(0f, 0.08f, 0.45f), new Vector3(s.x + 0.5f, 0.16f, 0.6f), q, true);
            DrawBuildParts(g, parts);
        }

        /// <summary>
        /// A window facing +z: dark glass Span.x by Span.y centred on At, a sill and a head proud of the wall, and a
        /// shutter either side when the folk keep them. The glass is walk-through; the sill is the solid piece.
        /// </summary>
        static void WindowPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(0.9f, 1.1f, 0.1f));
            var parts = new List<KitPart>();
            var q = Quaternion.identity;
            Piece(parts, KitPiece.Window, KitMaterial.Opening, at, s, q);
            Piece(parts, KitPiece.Beam, KitMaterial.Trim, at + new Vector3(0f, -s.y * 0.5f - 0.05f, 0.08f), new Vector3(s.x + 0.24f, 0.1f, StructureKit.Thick * 0.5f + 0.12f), q);
            Piece(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, s.y * 0.5f + 0.08f, 0.05f), new Vector3(s.x + 0.3f, 0.16f, StructureKit.Thick * 0.5f + 0.06f), q);
            if (StructureKit.For(g.S.KitCulture).Shutters)
                foreach (var side in new[] { -1f, 1f })
                    Piece(parts, KitPiece.Window, KitMaterial.Wood, at + new Vector3(side * (s.x * 0.75f + 0.03f), 0f, StructureKit.Thick * 0.5f + 0.04f), new Vector3(s.x * 0.48f, s.y, 0.06f), q);
            DrawBuildParts(g, parts);
        }

        /// <summary>
        /// A gabled roof: two pitched slabs from eaves Span.x apart (the slope runs across x, the ridge along z, Span.z
        /// long) rising Span.y to the ridge, its eave line centred on At, and a ridge board on top.
        /// </summary>
        static void GabledRoofPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(4f, 1.6f, 4f));
            var half = s.x * 0.5f;
            var pitch = Mathf.Atan2(s.y, half) * Mathf.Rad2Deg;
            var slope = Mathf.Sqrt(half * half + s.y * s.y) + 0.3f;   // runs past the eave a little
            var parts = new List<KitPart>();
            foreach (var side in new[] { -1f, 1f })
                Piece(parts, KitPiece.Roof, KitMaterial.Roof, at + new Vector3(side * half * 0.5f, s.y * 0.5f, 0f), new Vector3(slope, 0.16f, s.z + 0.4f),
                      Quaternion.Euler(0f, 0f, -side * pitch));
            Piece(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, s.y + 0.06f, 0f), new Vector3(0.24f, 0.12f, s.z + 0.5f), Quaternion.identity);
            DrawBuildParts(g, parts);
        }

        /// <summary>A squared timber beam from At to To, Span.x square (0.2 m by default).</summary>
        static void BeamPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 a = f.At(p), b = f.To(p);
            var side = f.Span(p).x > 0f ? f.Span(p).x : 0.2f;
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f)
                return;
            var parts = new List<KitPart>();
            Piece(parts, KitPiece.Beam, KitMaterial.Wood, (a + b) * 0.5f, new Vector3(side, side, d.magnitude), Quaternion.LookRotation(d, Mathf.Abs(d.normalized.y) > 0.99f ? Vector3.forward : Vector3.up));
            DrawBuildParts(g, parts);
        }

        /// <summary>A stair climbing +z: the kit's steps (StructureKit.Stair) from At, Span.x wide, rising Span.y, landing included.</summary>
        static void StairPart(Grower g, UnitFrame f, UnitPart p)
        {
            var s = SpanOr(f, p, new Vector3(1.2f, 1.4f, 0f));
            var parts = new List<KitPart>();
            StructureKit.Stair(parts, f.At(p), 0f, s.y, s.x);
            DrawBuildParts(g, parts);
        }

        /// <summary>A rail from At to To: posts about every 1.2 m and a top rail, Span.y high (1 m by default).</summary>
        static void RailPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 a = f.At(p), b = f.To(p);
            var height = f.Span(p).y > 0f ? f.Span(p).y : 1f;
            var length = Vector3.Distance(a, b);
            var q = Along(a, b);
            var parts = new List<KitPart>();
            var posts = Mathf.Max(2, Mathf.CeilToInt(length / 1.2f) + 1);
            for (int k = 0; k < posts; k++)
                Piece(parts, KitPiece.Column, KitMaterial.Wood, Vector3.Lerp(a, b, k / (posts - 1f)) + Vector3.up * height * 0.5f, new Vector3(0.1f, height, 0.1f), q);
            Piece(parts, KitPiece.Rail, KitMaterial.Wood, (a + b) * 0.5f + Vector3.up * (height - 0.04f), new Vector3(length + 0.1f, 0.08f, 0.1f), q, true);
            DrawBuildParts(g, parts);
        }

        /// <summary>
        /// A hanging shop sign facing +z: a board Span.x by Span.y hung from At (the bracket's end) on two short chains, a
        /// trim frame round it. Its board is solid so a body bumps it, like a beam.
        /// </summary>
        static void SignPart(Grower g, UnitFrame f, UnitPart p)
        {
            Vector3 at = f.At(p), s = SpanOr(f, p, new Vector3(0.8f, 0.5f, 0.05f));
            var parts = new List<KitPart>();
            var q = Quaternion.identity;
            var board = at - Vector3.up * (0.15f + s.y * 0.5f);
            Piece(parts, KitPiece.Beam, KitMaterial.Wood, board, s, q);
            Piece(parts, KitPiece.Trim, KitMaterial.Trim, board, new Vector3(s.x + 0.06f, s.y + 0.06f, s.z * 0.5f), q);
            foreach (var side in new[] { -1f, 1f })
                Piece(parts, KitPiece.Trim, KitMaterial.Iron, at + new Vector3(side * s.x * 0.35f, -0.075f, 0f), new Vector3(0.02f, 0.15f, 0.02f), q);
            DrawBuildParts(g, parts);
        }

        /// <summary>A hung lantern: the kit's lantern head (StructureKit.LanternHead) centred on At, on a short hanger. Walk-through.</summary>
        static void LanternPart(Grower g, UnitFrame f, UnitPart p)
        {
            var at = f.At(p);
            var parts = new List<KitPart>();
            StructureKit.LanternHead(parts, at, 0f);
            Piece(parts, KitPiece.Trim, KitMaterial.Iron, at + Vector3.up * (StructureKit.LanternGlass.y * 0.5f + 0.15f), new Vector3(0.02f, 0.2f, 0.02f), Quaternion.identity);
            DrawBuildParts(g, parts);
        }

        /// <summary>A wooden crate: the kit's crate (StructureKit.Crate), Span.x across, standing on At. Its body is solid.</summary>
        static void CratePart(Grower g, UnitFrame f, UnitPart p)
        {
            var size = f.Span(p).x > 0f ? f.Span(p).x : 0.6f;
            var parts = new List<KitPart>();
            StructureKit.Crate(parts, f.At(p) + Vector3.up * size * 0.5f, size, 0f, true);
            DrawBuildParts(g, parts);
        }
    }
}
