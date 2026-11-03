using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>What a structure-kit part is, so builders and later systems (docks, ladders, residents) can tell them apart.</summary>
    public enum KitPiece : byte
    {
        Wall, Floor, Roof, Stair, Column, Arch, Platform, Balcony, Rail, Window, Door, Trim, Rubble, Moss, Beam, Chimney
    }

    /// <summary>Which of a culture's trim-set colours a part is drawn in.</summary>
    public enum KitMaterial : byte
    {
        Wall, Trim, Roof, Wood, Opening, Moss, Stone
    }

    /// <summary>How a culture roofs its buildings.</summary>
    public enum KitRoof : byte
    {
        /// <summary>Two pitched slabs over stepped gable ends (humans).</summary>
        Gable,
        /// <summary>Shrinking tiers with deep eaves (elves).</summary>
        Tiered,
        /// <summary>A flat roof behind a crenellated parapet (dwarves).</summary>
        Flat,
        /// <summary>A steep thatch-like gable down to low eaves (snow folk, goblins).</summary>
        Steep
    }

    /// <summary>One box of a kit structure, in the structure's own space (+Z is the front, y up from the ground).</summary>
    public struct KitPart
    {
        public KitPiece Piece;
        public KitMaterial Material;
        public Vector3 Centre, Size;
        public Quaternion Rotation;
        /// <summary>Stands on the ground and stops the player (ruins collide per standing wall bay).</summary>
        public bool Solid;
    }

    /// <summary>
    /// A culture's trim set: the colours and proportions that make the same kit pieces read as one folk's buildings.
    /// Fishing villages (#36) and new races only need a new set, not new pieces.
    /// </summary>
    [System.Serializable]
    public struct CultureTrim
    {
        public Color Wall, Trim, RoofColour, Wood, Opening, Moss;
        public KitRoof Roof;
        /// <summary>Storey height (m), bay width (m) and roof pitch (degrees).</summary>
        public float Storey, Bay, Pitch;
        /// <summary>Dressed quoins up the corners, crenels on parapets and towers, shutters by windows.</summary>
        public bool Quoins, Crenels, Shutters;
        /// <summary>Chance of a corner tower, a balcony and an outside stair on a building of two storeys or more.</summary>
        public float TowerChance, BalconyChance, StairChance;
    }

    /// <summary>
    /// The general structure kit (#24): RuinKit's set pieces join rooms underground; these pieces build what stands above
    /// it. Multi-storey walls split into bays (solid, window or door), string courses, quoins, plinths, roofs in four
    /// styles, balconies, outside stairs, corner towers, gates and docks-ready platforms, each as plain data (KitPart),
    /// coloured by a culture's trim set. A house is fully decided by its seed; with decay above 0 the same house comes
    /// out broken: walls fallen from one corner, roof gone but a few rafters, floors showing, rubble and moss.
    /// RuinKit.Temple is untouched by this (its layouts and fingerprints stay the same).
    /// </summary>
    public static class StructureKit
    {
        public const float Thick = 0.4f;

        /// <summary>The trim set of a folk's buildings.</summary>
        public static CultureTrim For(FolkCulture culture)
        {
            switch (culture)
            {
                case FolkCulture.Dwarf:
                    return new CultureTrim
                    {
                        Wall = new Color(0.52f, 0.47f, 0.42f), Trim = new Color(0.36f, 0.33f, 0.31f), RoofColour = new Color(0.33f, 0.31f, 0.30f),
                        Wood = new Color(0.30f, 0.20f, 0.13f), Opening = new Color(0.10f, 0.08f, 0.07f), Moss = new Color(0.30f, 0.40f, 0.20f),
                        Roof = KitRoof.Flat, Storey = 3.2f, Bay = 2.6f, Pitch = 0f, Quoins = true, Crenels = true, Shutters = false,
                        TowerChance = 0.45f, BalconyChance = 0.2f, StairChance = 0.6f
                    };
                case FolkCulture.Elf:
                    return new CultureTrim
                    {
                        Wall = new Color(0.82f, 0.80f, 0.70f), Trim = new Color(0.55f, 0.62f, 0.45f), RoofColour = new Color(0.30f, 0.45f, 0.36f),
                        Wood = new Color(0.45f, 0.33f, 0.20f), Opening = new Color(0.12f, 0.14f, 0.10f), Moss = new Color(0.36f, 0.50f, 0.24f),
                        Roof = KitRoof.Tiered, Storey = 3.4f, Bay = 2.2f, Pitch = 0f, Quoins = false, Crenels = false, Shutters = false,
                        TowerChance = 0.35f, BalconyChance = 0.7f, StairChance = 0.3f
                    };
                case FolkCulture.Goblin:
                case FolkCulture.Ogre:
                case FolkCulture.SnowFolk:
                    return new CultureTrim
                    {
                        Wall = new Color(0.50f, 0.42f, 0.33f), Trim = new Color(0.36f, 0.27f, 0.18f), RoofColour = new Color(0.58f, 0.50f, 0.32f),
                        Wood = new Color(0.33f, 0.24f, 0.15f), Opening = new Color(0.09f, 0.07f, 0.05f), Moss = new Color(0.34f, 0.42f, 0.20f),
                        Roof = KitRoof.Steep, Storey = 2.6f, Bay = 2.2f, Pitch = 50f, Quoins = false, Crenels = false, Shutters = true,
                        TowerChance = 0.1f, BalconyChance = 0.1f, StairChance = 0.2f
                    };
                default:
                    return new CultureTrim
                    {
                        Wall = new Color(0.78f, 0.72f, 0.62f), Trim = new Color(0.55f, 0.50f, 0.45f), RoofColour = new Color(0.55f, 0.27f, 0.20f),
                        Wood = new Color(0.36f, 0.24f, 0.15f), Opening = new Color(0.10f, 0.09f, 0.08f), Moss = new Color(0.33f, 0.45f, 0.22f),
                        Roof = KitRoof.Gable, Storey = 3f, Bay = 2.4f, Pitch = 38f, Quoins = true, Crenels = false, Shutters = true,
                        TowerChance = 0.18f, BalconyChance = 0.45f, StairChance = 0.35f
                    };
            }
        }


        /// <summary>A culture's trim colour for a material.</summary>
        public static Color Colour(in CultureTrim t, KitMaterial m) => m switch
        {
            KitMaterial.Trim => t.Trim,
            KitMaterial.Roof => t.RoofColour,
            KitMaterial.Wood => t.Wood,
            KitMaterial.Opening => t.Opening,
            KitMaterial.Moss => t.Moss,
            KitMaterial.Stone => Color.Lerp(t.Wall, t.Trim, 0.5f) * 0.85f,
            _ => t.Wall
        };

        /// <summary>
        /// A house of <paramref name="storeys"/> on a width x depth footprint centred on the origin, door at +Z. Decay 0
        /// is whole; towards 1 more of it has fallen. Every choice comes from the seed, so the same seed is the same house.
        /// </summary>
        public static List<KitPart> House(uint seed, CultureTrim t, float width, float depth, int storeys, float decay, List<KitPart> into = null)
        {
            var parts = into ?? new List<KitPart>();
            var rng = new System.Random((int)(Scramble(seed) & 0x7FFFFFFF));
            float R() => (float)rng.NextDouble();
            float Range(float a, float b) => a + (b - a) * R();
            storeys = Mathf.Max(1, storeys);
            var h = t.Storey;
            var top = storeys * h;
            var ruined = decay > 0f;

            // Ruins: each wall keeps a share of its height, falling away from one end; one wall has nearly all gone.
            var gone = rng.Next(4);
            var keep = new float[4];
            var fromLeft = new bool[4];
            for (int s = 0; s < 4; s++)
            {
                keep[s] = !ruined ? 1f : s == gone ? Range(0.05f, 0.2f) : Range(Mathf.Lerp(1f, 0.25f, decay), 1f);
                fromLeft[s] = R() < 0.5f;
            }

            // Plinth course round the foot.
            Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, 0.25f, 0f), new Vector3(width + 0.16f, 0.5f, depth + 0.16f), 0f, false);

            for (int s = 0; s < 4; s++)
            {
                var along = s % 2 == 0 ? width : depth;
                var out_ = (s % 2 == 0 ? depth : width) * 0.5f;
                var yaw = s * 90f;
                var bays = Mathf.Max(1, Mathf.RoundToInt(along / t.Bay));
                var bw = along / bays;
                var windowChance = s == 0 ? 0.7f : s == 2 ? 0.35f : 0.5f;
                for (int i = 0; i < bays; i++)
                {
                    var x = -along * 0.5f + (i + 0.5f) * bw;
                    var u = (i + 0.5f) / bays;
                    var standing = !ruined ? top : top * keep[s] * Mathf.Lerp(0.35f, 1f, fromLeft[s] ? u : 1f - u) * Range(0.8f, 1.05f);
                    if (ruined && standing < 0.35f)
                    {
                        Rubble(parts, rng, Turn(yaw, new Vector3(x, 0f, out_ + 1.2f)), bw * 0.6f, 3);
                        continue;
                    }
                    for (int k = 0; k < storeys; k++)
                    {
                        var y0 = k * h;
                        if (y0 >= standing)
                            break;
                        var y1 = Mathf.Min(y0 + h, standing);
                        var cut = y1 < y0 + h - 0.01f;
                        var door = k == 0 && s == 0 && i == bays / 2;
                        var window = !door && !cut && R() < windowChance && bw > 1.5f;
                        if (door)
                            DoorBay(parts, t, yaw, x, out_, y0, y1, bw, h, ruined);
                        else if (window)
                            WindowBay(parts, t, rng, yaw, x, out_, y0, y1, bw, h, k, ruined);
                        else
                            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (y0 + y1) * 0.5f, out_)), new Vector3(bw + 0.01f, y1 - y0, Thick), yaw, k == 0);
                    }
                    // Moss on a broken top; ivy down some walls.
                    if (ruined && R() < 0.55f)
                        Add(parts, KitPiece.Moss, KitMaterial.Moss, Turn(yaw, new Vector3(x, standing + 0.04f, out_)), new Vector3(bw * Range(0.6f, 1f), 0.09f, Thick + 0.08f), yaw, false);
                    if (ruined && R() < 0.25f)
                    {
                        // Ivy: a few thin trailing strands of different lengths hugging the face, not one flat panel.
                        var ivy = standing * Range(0.4f, 0.9f);
                        var ix = x + Range(-0.3f, 0.3f) * bw;
                        for (int v = 0, strands = rng.Next(3, 6); v < strands; v++)
                        {
                            var len = ivy * Range(0.35f, 1f);
                            Add(parts, KitPiece.Moss, KitMaterial.Moss, Turn(yaw, new Vector3(ix + (v - strands * 0.5f) * 0.13f + Range(-0.04f, 0.04f), standing - len * 0.5f, out_ + Thick * 0.5f + 0.02f + v % 2 * 0.012f)),
                                new Vector3(Range(0.06f, 0.12f), len, 0.04f), yaw, false);
                        }
                    }
                }
                // String courses between storeys and under the eaves.
                for (int k = 1; k <= storeys; k++)
                {
                    if (ruined && k * h > top * keep[s] * 0.6f)
                        break;
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(0f, k * h - 0.08f, out_ + 0.06f)), new Vector3(along + 0.2f, 0.16f, Thick * 0.5f), yaw, false);
                }
            }

            // Quoins up each corner (dressed stones of alternating length).
            if (t.Quoins)
                for (int c = 0; c < 4; c++)
                {
                    var cx = (c & 1) == 0 ? -width * 0.5f : width * 0.5f;
                    var cz = (c & 2) == 0 ? -depth * 0.5f : depth * 0.5f;
                    var corner = !ruined ? top : top * Mathf.Min(keep[c & 1], keep[2 + (c >> 1)]) * 0.7f;
                    for (int k = 0; (k + 0.5f) * 0.45f < corner - 0.1f; k++)
                    {
                        var longX = k % 2 == 0;
                        var size = new Vector3(longX ? 0.75f : 0.4f, 0.41f, longX ? 0.4f : 0.75f);
                        Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(cx - Mathf.Sign(cx) * (size.x * 0.5f - 0.06f), (k + 0.5f) * 0.45f, cz - Mathf.Sign(cz) * (size.z * 0.5f - 0.06f)), size, 0f, false);
                    }
                }

            // Floors show through a ruin's broken walls; a whole house keeps them hidden, so skip them.
            if (ruined)
                for (int k = 1; k < storeys; k++)
                    if (R() < 0.6f)
                        Add(parts, KitPiece.Floor, KitMaterial.Wood, new Vector3(Range(-0.3f, 0.3f) * width, k * h - 0.1f, Range(-0.2f, 0.2f) * depth),
                            new Vector3(width * Range(0.35f, 0.7f), 0.2f, depth * Range(0.3f, 0.6f)), 0f, false);

            if (!ruined)
                Roof(parts, t, rng, width, depth, top);
            else
                for (int k = 0, n = rng.Next(0, 4); k < n; k++)   // a few fallen rafters, one end on the floor, leaning up
                {
                    var len = depth * Range(0.6f, 1f);
                    var rise = Mathf.Min(len * Range(0.15f, 0.42f), top * 0.6f);
                    Add(parts, KitPiece.Beam, KitMaterial.Wood, new Vector3(Range(-0.3f, 0.3f) * width, rise, Range(-0.2f, 0.2f) * depth),
                        new Vector3(0.22f, 0.22f, len), R() * 360f, false, Mathf.Asin(Mathf.Clamp01(2f * rise / len)) * Mathf.Rad2Deg);
                }

            if (storeys >= 2 && R() < (ruined ? t.BalconyChance * 0.5f : t.BalconyChance))
                Balcony(parts, t, new Vector3(Range(-0.15f, 0.15f) * width, h, depth * 0.5f), 0f, Mathf.Min(width * 0.5f, 3.6f), ruined);
            if (storeys >= 2 && R() < t.StairChance)
                Stair(parts, new Vector3(width * 0.5f + 0.55f, 0f, -depth * 0.35f), 0f, h, 1f);
            if (R() < t.TowerChance)
                Tower(parts, t, rng, new Vector3(-width * 0.5f, 0f, -depth * 0.5f), Mathf.Min(width, depth) * 0.42f, top + h * Range(1f, 1.8f), ruined ? keep[gone == 1 ? 3 : 1] : 1f);
            if (ruined)
                for (int k = 0, n = 4 + rng.Next(6); k < n; k++)   // rubble round the foot and inside
                    Rubble(parts, rng, new Vector3(Range(-0.6f, 0.6f) * width, 0f, Range(-0.6f, 0.6f) * depth), 0.6f, 2);
            return parts;
        }

        /// <summary>A gate: two piers and a lintel (with a capping course), or an arch of voussoirs when arched.</summary>
        public static void Gate(List<KitPart> parts, CultureTrim t, Vector3 at, float yaw, float width, float height, bool arched)
        {
            const float pier = 0.9f;
            foreach (var side in new[] { -1f, 1f })
                Add(parts, KitPiece.Column, KitMaterial.Trim, at + Turn(yaw, new Vector3(side * (width + pier) * 0.5f, height * 0.5f, 0f)), new Vector3(pier, height, pier), yaw, true);
            if (!arched)
            {
                Add(parts, KitPiece.Arch, KitMaterial.Trim, at + Turn(yaw, new Vector3(0f, height + 0.3f, 0f)), new Vector3(width + pier * 2.4f, 0.6f, pier * 1.1f), yaw, false);
                if (t.Crenels)
                    for (int k = -2; k <= 2; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(yaw, new Vector3(k * (width + pier * 2f) / 5f, height + 0.85f, 0f)), new Vector3(0.5f, 0.5f, pier), yaw, false);
                return;
            }
            const int stones = 9;
            for (int k = 0; k < stones; k++)
            {
                var a = Mathf.PI * (k + 0.5f) / stones;
                var p = new Vector3(Mathf.Cos(a) * width * 0.55f, height + Mathf.Sin(a) * width * 0.5f, 0f);
                Add(parts, KitPiece.Arch, KitMaterial.Trim, at + Turn(yaw, p), new Vector3(width * 0.35f, 0.55f, pier), yaw, false, 0f, 90f - a * Mathf.Rad2Deg);
            }
        }

        /// <summary>
        /// A deck on posts (docks-ready: the posts run down <paramref name="deckHeight"/> to the bed or ground), planks
        /// across it, a rail along both long sides.
        /// </summary>
        public static void Platform(List<KitPart> parts, Vector3 at, float yaw, float width, float length, float deckHeight, bool rails)
        {
            var planks = Mathf.Max(2, Mathf.RoundToInt(length / 0.35f));
            for (int k = 0; k < planks; k++)
                Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, deckHeight - 0.06f, -length * 0.5f + (k + 0.5f) * length / planks)),
                    new Vector3(width, 0.12f, length / planks * 0.92f), yaw, false);
            for (int k = 0, posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.2f) + 1); k < posts; k++)
                foreach (var side in new[] { -1f, 1f })
                {
                    var z = -length * 0.5f + k * length / (posts - 1);
                    Add(parts, KitPiece.Column, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, (deckHeight - 0.1f) * 0.5f, z)), new Vector3(0.24f, deckHeight, 0.24f), yaw, false);
                    if (rails)
                        Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, deckHeight + 0.5f, z)), new Vector3(0.12f, 1f, 0.12f), yaw, false);
                }
            if (rails)
                foreach (var side in new[] { -1f, 1f })
                    Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width * 0.45f, deckHeight + 0.95f, 0f)), new Vector3(0.1f, 0.1f, length), yaw, false);
        }

        /// <summary>A straight outside stair along +Z rising <paramref name="rise"/>, its treads solid blocks to the ground.</summary>
        public static void Stair(List<KitPart> parts, Vector3 at, float yaw, float rise, float width)
        {
            var steps = Mathf.Max(2, Mathf.CeilToInt(rise / 0.28f));
            var run = 0.3f;
            for (int k = 0; k < steps; k++)
            {
                var y = (k + 1) * rise / steps;
                Add(parts, KitPiece.Stair, KitMaterial.Stone, at + Turn(yaw, new Vector3(0f, y * 0.5f, (k - steps * 0.5f) * run)), new Vector3(width, y, run), yaw, false);
            }
            Add(parts, KitPiece.Platform, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, rise - 0.08f, steps * 0.5f * run + 0.5f)), new Vector3(width, 0.16f, 1f), yaw, false);
        }

        /// <summary>A balcony: a slab on brackets in front of a wall, posts and a top rail.</summary>
        public static void Balcony(List<KitPart> parts, CultureTrim t, Vector3 at, float yaw, float width, bool ruined)
        {
            const float deep = 1.1f;
            Add(parts, KitPiece.Balcony, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, -0.08f, deep * 0.5f)), new Vector3(width, 0.16f, deep), yaw, false);
            foreach (var side in new[] { -0.4f, 0.4f })
                Add(parts, KitPiece.Beam, KitMaterial.Wood, at + Turn(yaw, new Vector3(side * width, -0.45f, deep * 0.35f)), new Vector3(0.16f, 0.16f, deep * 0.9f), yaw, false, -35f);
            var posts = Mathf.Max(3, Mathf.RoundToInt(width / 0.45f));
            for (int k = 0; k < posts; k++)
            {
                if (ruined && k % 3 == 1)
                    continue;
                Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(-width * 0.5f + (k + 0.5f) * width / posts, 0.45f, deep - 0.06f)), new Vector3(0.08f, 0.9f, 0.08f), yaw, false);
            }
            if (!ruined)
                Add(parts, KitPiece.Rail, KitMaterial.Wood, at + Turn(yaw, new Vector3(0f, 0.92f, deep - 0.06f)), new Vector3(width, 0.1f, 0.12f), yaw, false);
        }

        /// <summary>A square corner tower with slit windows, a cornice and a pyramid of tiers or a crenellated top.</summary>
        public static void Tower(List<KitPart> parts, CultureTrim t, System.Random rng, Vector3 at, float side, float height, float keep)
        {
            var standing = height * Mathf.Clamp01(keep);
            if (standing < 0.5f)
                return;
            Add(parts, KitPiece.Wall, KitMaterial.Wall, at + new Vector3(0f, standing * 0.5f, 0f), new Vector3(side, standing, side), 0f, true);
            for (float y = t.Storey * 0.6f; y < standing - 1f; y += t.Storey)
                for (int s = 0; s < 4; s++)
                    Add(parts, KitPiece.Window, KitMaterial.Opening, at + Turn(s * 90f, new Vector3(0f, y, side * 0.5f + 0.01f)), new Vector3(0.28f, 0.9f, 0.06f), s * 90f, false);
            if (keep < 1f)
                return;
            Add(parts, KitPiece.Trim, KitMaterial.Trim, at + new Vector3(0f, height + 0.15f, 0f), new Vector3(side + 0.4f, 0.3f, side + 0.4f), 0f, false);
            if (t.Crenels || t.Roof == KitRoof.Flat)
            {
                for (int s = 0; s < 4; s++)
                    for (int k = -1; k <= 1; k++)
                        Add(parts, KitPiece.Trim, KitMaterial.Trim, at + Turn(s * 90f, new Vector3(k * side / 3f, height + 0.6f, side * 0.5f + 0.1f)), new Vector3(side / 6f, 0.6f, 0.4f), s * 90f, false);
                return;
            }
            var tiers = 5;
            for (int k = 0; k < tiers; k++)
            {
                var w = (side + 0.6f) * (1f - k / (float)tiers);
                Add(parts, KitPiece.Roof, KitMaterial.Roof, at + new Vector3(0f, height + 0.3f + (k + 0.5f) * side * 0.32f, 0f), new Vector3(w, side * 0.32f, w), 0f, false);
            }
        }

        static void Roof(List<KitPart> parts, CultureTrim t, System.Random rng, float width, float depth, float top)
        {
            const float eave = 0.45f, slab = 0.2f;
            switch (t.Roof)
            {
                case KitRoof.Flat:
                    Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + 0.1f, 0f), new Vector3(width, 0.2f, depth), 0f, false);
                    for (int s = 0; s < 4; s++)
                    {
                        var along = s % 2 == 0 ? width : depth;
                        var out_ = (s % 2 == 0 ? depth : width) * 0.5f;
                        Add(parts, KitPiece.Wall, KitMaterial.Trim, Turn(s * 90f, new Vector3(0f, top + 0.35f, out_)), new Vector3(along + Thick, 0.7f, Thick), s * 90f, false);
                        if (t.Crenels)
                            for (int k = 0, n = Mathf.Max(2, Mathf.RoundToInt(along / 1.2f)); k < n; k++)
                                Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(s * 90f, new Vector3(-along * 0.5f + (k + 0.5f) * along / n, top + 0.95f, out_)), new Vector3(along / n * 0.5f, 0.5f, Thick + 0.04f), s * 90f, false);
                    }
                    break;
                case KitRoof.Tiered:
                    for (int k = 0; k < 3; k++)
                    {
                        var shrink = 1f - k * 0.3f;
                        Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + 0.18f + k * 0.85f, 0f), new Vector3(width * shrink + eave * 4f * shrink, 0.32f, depth * shrink + eave * 4f * shrink), 0f, false);
                        Add(parts, KitPiece.Wall, KitMaterial.Trim, new Vector3(0f, top + 0.6f + k * 0.85f, 0f), new Vector3(width * shrink * 0.8f, 0.55f, depth * shrink * 0.8f), 0f, false);
                    }
                    Add(parts, KitPiece.Column, KitMaterial.Trim, new Vector3(0f, top + 3.1f, 0f), new Vector3(0.18f, 1.2f, 0.18f), 0f, false);
                    break;
                default:
                {
                    // Ridge along the width; slabs pitched down to the front and back, gable ends stepped up to the ridge.
                    var pitch = t.Pitch * Mathf.Deg2Rad;
                    var run = depth * 0.5f + eave;
                    var rise = (depth * 0.5f) * Mathf.Tan(pitch);
                    var length = run / Mathf.Cos(pitch);
                    foreach (var side in new[] { -1f, 1f })
                        Add(parts, KitPiece.Roof, KitMaterial.Roof, new Vector3(0f, top + rise * 0.5f - eave * Mathf.Tan(pitch) * 0.5f + slab, side * run * 0.5f),
                            new Vector3(width + eave * 2f, slab, length), 0f, false, side * t.Pitch);
                    Add(parts, KitPiece.Trim, KitMaterial.Trim, new Vector3(0f, top + rise + slab * 1.2f, 0f), new Vector3(width + eave * 2.2f, 0.22f, 0.3f), 0f, false);
                    const int steps = 5;
                    foreach (var side in new[] { -1f, 1f })
                        for (int k = 0; k < steps; k++)
                        {
                            var w = depth * (1f - (k + 0.5f) / steps);
                            Add(parts, KitPiece.Wall, KitMaterial.Wall, new Vector3(side * (width * 0.5f - Thick * 0.5f), top + (k + 0.5f) * rise / steps, 0f), new Vector3(Thick, rise / steps + 0.01f, w), 0f, false);
                        }
                    // A chimney through one slope.
                    var cx = (rng.NextDouble() < 0.5 ? -1f : 1f) * width * 0.3f;
                    Add(parts, KitPiece.Chimney, KitMaterial.Stone, new Vector3(cx, top + rise * 0.75f + 0.6f, -depth * 0.15f), new Vector3(0.7f, rise * 1.5f + 1.2f, 0.7f), 0f, false);
                    break;
                }
            }
        }

        static void DoorBay(List<KitPart> parts, CultureTrim t, float yaw, float x, float out_, float y0, float y1, float bw, float h, bool ruined)
        {
            var dw = Mathf.Min(1.3f, bw * 0.55f);
            var dh = Mathf.Min(h * 0.78f, 2.3f);
            var side = (bw - dw) * 0.5f;
            foreach (var s in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x + s * (dw + side) * 0.5f, (y0 + y1) * 0.5f, out_)), new Vector3(side + 0.01f, y1 - y0, Thick), yaw, true);
            if (y1 > dh + 0.05f)
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (dh + y1) * 0.5f, out_)), new Vector3(dw + 0.02f, y1 - dh, Thick), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, dh + 0.12f, out_ + 0.1f)), new Vector3(dw + 0.4f, 0.24f, Thick * 0.6f), yaw, false);
            if (!ruined)
                Add(parts, KitPiece.Door, KitMaterial.Wood, Turn(yaw, new Vector3(x, dh * 0.5f, out_ - 0.05f)), new Vector3(dw, dh, 0.12f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Stone, Turn(yaw, new Vector3(x, 0.08f, out_ + 0.45f)), new Vector3(dw + 0.5f, 0.16f, 0.6f), yaw, false);   // step
        }

        static void WindowBay(List<KitPart> parts, CultureTrim t, System.Random rng, float yaw, float x, float out_, float y0, float y1, float bw, float h, int storey, bool ruined)
        {
            var ww = Mathf.Min(0.95f, bw * 0.42f);
            var wh = h * (storey == 0 ? 0.4f : 0.45f);
            var sill = y0 + h * 0.34f;
            var head = sill + wh;
            var side = (bw - ww) * 0.5f;
            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (y0 + sill) * 0.5f, out_)), new Vector3(bw + 0.01f, sill - y0, Thick), yaw, storey == 0);
            Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x, (head + y1) * 0.5f, out_)), new Vector3(bw + 0.01f, y1 - head, Thick), yaw, false);
            foreach (var s in new[] { -1f, 1f })
                Add(parts, KitPiece.Wall, KitMaterial.Wall, Turn(yaw, new Vector3(x + s * (ww + side) * 0.5f, (sill + head) * 0.5f, out_)), new Vector3(side + 0.01f, wh, Thick), yaw, false);
            // The opening: dark glass set back (a ruin's is a real hole you can see through), a sill and lintel proud of
            // the wall, sometimes shutters.
            if (!ruined)
                Add(parts, KitPiece.Window, KitMaterial.Opening, Turn(yaw, new Vector3(x, (sill + head) * 0.5f, out_ - 0.08f)), new Vector3(ww, wh, 0.1f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, sill - 0.05f, out_ + 0.08f)), new Vector3(ww + 0.24f, 0.1f, Thick * 0.5f + 0.12f), yaw, false);
            Add(parts, KitPiece.Trim, KitMaterial.Trim, Turn(yaw, new Vector3(x, head + 0.08f, out_ + 0.05f)), new Vector3(ww + 0.3f, 0.16f, Thick * 0.5f + 0.06f), yaw, false);
            if (t.Shutters && !ruined && rng.NextDouble() < 0.5)
                foreach (var s in new[] { -1f, 1f })
                    Add(parts, KitPiece.Window, KitMaterial.Wood, Turn(yaw, new Vector3(x + s * (ww * 0.75f + 0.03f), (sill + head) * 0.5f, out_ + Thick * 0.5f + 0.04f)), new Vector3(ww * 0.48f, wh, 0.06f), yaw, false);
            if (!ruined && storey == 0 && rng.NextDouble() < 0.3)   // a flower box
                Add(parts, KitPiece.Trim, KitMaterial.Moss, Turn(yaw, new Vector3(x, sill + 0.08f, out_ + Thick * 0.5f + 0.18f)), new Vector3(ww, 0.22f, 0.3f), yaw, false);
        }

        static void Rubble(List<KitPart> parts, System.Random rng, Vector3 at, float spread, int stones)
        {
            for (int k = 0; k < stones; k++)
            {
                var size = 0.2f + 0.35f * (float)rng.NextDouble();
                var p = at + new Vector3(((float)rng.NextDouble() - 0.5f) * spread * 2f, size * 0.35f, ((float)rng.NextDouble() - 0.5f) * spread * 2f);
                Add(parts, KitPiece.Rubble, KitMaterial.Stone, p, new Vector3(size * 1.3f, size * 0.8f, size), (float)rng.NextDouble() * 360f, false, ((float)rng.NextDouble() - 0.5f) * 30f);
            }
        }

        static Vector3 Turn(float yaw, Vector3 p) => Quaternion.Euler(0f, yaw, 0f) * p;

        static void Add(List<KitPart> parts, KitPiece piece, KitMaterial m, Vector3 centre, Vector3 size, float yaw, bool solid, float pitch = 0f, float roll = 0f) =>
            parts.Add(new KitPart { Piece = piece, Material = m, Centre = centre, Size = size, Rotation = Quaternion.Euler(pitch, yaw, roll), Solid = solid });

        /// <summary>An order-sensitive hash of a part list (pieces, materials, centres and sizes to the centimetre).</summary>
        public static uint Fingerprint(List<KitPart> parts)
        {
            var h = 2166136261u;
            void Mix(int v) { h = (h ^ (uint)v) * 16777619u; }
            foreach (var p in parts)
            {
                Mix((int)p.Piece | (int)p.Material << 8);
                Mix(Mathf.RoundToInt(p.Centre.x * 100f)); Mix(Mathf.RoundToInt(p.Centre.y * 100f)); Mix(Mathf.RoundToInt(p.Centre.z * 100f));
                Mix(Mathf.RoundToInt(p.Size.x * 100f)); Mix(Mathf.RoundToInt(p.Size.y * 100f)); Mix(Mathf.RoundToInt(p.Size.z * 100f));
            }
            return h;
        }

        /// <summary>A full-avalanche hash (math.hash on uints barely mixes neighbouring seeds).</summary>
        static uint Scramble(uint x)
        {
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
