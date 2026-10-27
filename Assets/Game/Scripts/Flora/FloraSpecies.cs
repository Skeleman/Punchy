using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>The kind of plant a species is grown as. Each form has its own procedural generator.</summary>
    public enum FloraForm : byte
    {
        [Tooltip("Trunk, branching limbs and a crown of leafy clumps (oak, birch, palo verde).")] BroadleafTree,
        [Tooltip("Straight trunk under stacked, drooping tiers of needles (pine, spruce).")] Conifer,
        [Tooltip("Ringed, curving trunk with a crown of arching fronds.")] Palm,
        [Tooltip("A low dome of leafy clumps on a few stems, optionally dotted with flowers or berries.")] Shrub,
        [Tooltip("Bare, twiggy branches with few or no leaves (dead bush, tumbleweed).")] DryBush,
        [Tooltip("A small clump of flowers on stems, with leaves at the base.")] Flowers,
        [Tooltip("A tall ribbed column with upturned arms.")] Saguaro,
        [Tooltip("A squat ribbed barrel with a crown of flowers.")] BarrelCactus,
        [Tooltip("Flat oval pads growing from each other's rims.")] PricklyPear,
        [Tooltip("A fan of long thin canes with red tips.")] Ocotillo,
        [Tooltip("A cluster of mushrooms: stems with domed caps (Foliage colours the caps, Bark the stems).")] Mushroom,
        [Tooltip("Bleached remains: a spine with arching ribs and a skull, from a small carcass up to giant rib " +
                 "arches. Bark is the bone colour; Branches sets the rib pairs; Fullness how complete it is.")] Bones,
        [Tooltip("A lumpy rock, with smaller ones around it (Branches = rocks in the group). Bark is the rock " +
                 "colour; accents are moss or lichen on top.")] Boulder,
        [Tooltip("Stacked stones: a standing stone (1 block), a tor or a hoodoo. Thickness sets the block width, " +
                 "Spread how flat (slab-like) they are, Trunk Fraction a wide cap stone; blocks are banded between " +
                 "Bark and Foliage.")] Spire,
        [Tooltip("A cluster of pointed crystals from a rocky base (Foliage colours them; Finish makes them metallic " +
                 "or glowing). Branches = crystals.")] Crystal,
        [Tooltip("A rocky or earthen hump with a dark opening: a den, burrow or cave mouth. Bark is the rock.")] Den,
        [Tooltip("Packed hexagonal columns of cooled lava (basalt), flat-topped at stepped heights. Branches = " +
                 "columns, Thickness = column width, Spread = how much the heights vary.")] Columns,
        [Tooltip("A round hut: Bark-coloured walls, a conical thatched roof (Foliage) with an overhang, and a dark " +
                 "doorway facing +Z. Height is the whole hut; Spread is the width relative to it.")] Hut,
        [Tooltip("A square stone house: Bark is the stone, Foliage the slate roof; a chimney, and windows in the " +
                 "accent colour (Glowing Accents lights them at night). Doorway faces +Z.")] StoneHouse,
        [Tooltip("A tent of hides over crooked poles that poke out of the top: Foliage is the hide (each panel " +
                 "varies towards Foliage Variation), Bark the poles; Branches = poles. Opening faces +Z.")] Tent,
        [Tooltip("A slender tower: tapering walls (Bark) ringed with balconies, a tall pointed roof (Foliage) and " +
                 "windows in the accent colour. Doorway faces +Z.")] Tower,
        [Tooltip("A round fenced pen: posts (Bark) joined by two rails, a gap for the gate at +Z and a trough. " +
                 "Height is the fence; Spread sets the pen's size (radius = Height × Spread / 2); Branches = posts.")] Pen,
        [Tooltip("A market stall: four posts, a counter at the front (+Z) and a sloping awning striped in Foliage " +
                 "and the accent colour, with goods on the counter.")] Stall,
        [Tooltip("An inn: a two-storey timber house (Bark) with a gabled roof (Foliage), chimney, windows lit in the " +
                 "accent colour, a porch on posts at the front (+Z) and a hanging sign.")] Inn,
        [Tooltip("A smithy: an open shed on posts under a pyramid roof (Foliage), with a stone forge of glowing coals " +
                 "(accent colour), its chimney, and an anvil. Faces +Z.")] Smithy,
        [Tooltip("A forum: a ring of columns (Bark) on a round platform, joined by a lintel ring. Height is the " +
                 "columns; Spread sets the ring's size; Branches = columns.")] Forum,
        [Tooltip("A fountain: a round stone basin (Bark) of water (Foliage, shining), with a pillar and a smaller " +
                 "bowl in the middle.")] Fountain
    }

    public enum FoliageFinish : byte
    {
        [Tooltip("Ordinary leaves.")] Matte,
        [Tooltip("Polished metal: sharp highlights and glittering glints.")] Metallic,
        [Tooltip("Shines by itself, gently pulsing; visible at night.")] Glowing
    }

    public enum FlowerHead : byte
    {
        [Tooltip("Flat ring of petals around a centre (daisy, aster).")] Daisy,
        [Tooltip("Petals cupped upwards (poppy, tulip).")] Cup,
        [Tooltip("Small florets up the top of the stem (lupine, foxglove).")] Spike
    }

    /// <summary>
    /// Which placement layer a plant belongs to. Each layer scatters on its own grid (spacing roughly the size of
    /// its plants, so plants in one layer don't overlap) and draws out to its own distance.
    /// </summary>
    public enum FloraLayer : byte
    {
        [Tooltip("Flowers and other small plants. ~0.7 m spacing, drawn to ~70 m.")] Ground,
        [Tooltip("Bushes and small cacti. ~2 m spacing, drawn to ~260 m.")] Shrub,
        [Tooltip("Trees and large cacti. ~4.5 m spacing, drawn to the horizon.")] Tree
    }

    /// <summary>
    /// A kind of plant: how it is grown (form, size, colours) and where it is happy (slope, height, ground, water).
    /// Biomes list which species grow in them and how densely. Every plant is built procedurally: each species is
    /// grown in several random variants at three levels of detail, and each placed plant gets its own rotation,
    /// size and tint.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Flora Species", fileName = "Flora")]
    public class FloraSpecies : ScriptableObject
    {
        [Header("Growth")]
        public FloraForm Form = FloraForm.Shrub;
        public FloraLayer Layer = FloraLayer.Shrub;
        [Tooltip("Height range of fully grown plants, in metres.")]
        public Vector2 Height = new Vector2(1f, 1.6f);
        [Tooltip("Width of the crown / plant relative to its height.")]
        [Range(0.1f, 2f)] public float Spread = 0.7f;
        [Tooltip("Fraction of the height that is bare trunk below the crown (trees, palms).")]
        [Range(0f, 0.9f)] public float TrunkFraction = 0.35f;
        [Tooltip("Trunk thickness relative to height.")]
        [Range(0.005f, 0.2f)] public float Thickness = 0.035f;
        [Tooltip("How full the foliage is: number and size of leaf clumps, tiers, pads or petals.")]
        [Range(0f, 1f)] public float Fullness = 0.7f;
        [Tooltip("Number of limbs, arms, stems, canes or flowers (random in range).")]
        public Vector2Int Branches = new Vector2Int(3, 5);
        [Tooltip("How much the plant moves in the wind (0 = rigid, like a cactus).")]
        [Range(0f, 1f)] public float Flexibility = 0.5f;
        [Tooltip("Flowers form only.")]
        public FlowerHead FlowerHead = FlowerHead.Daisy;

        [Header("Colours")]
        public Color Bark = new Color(0.36f, 0.27f, 0.2f);
        public Color Foliage = new Color(0.22f, 0.4f, 0.14f);
        [Tooltip("Second foliage colour; each clump picks somewhere between the two.")]
        public Color FoliageVariation = new Color(0.3f, 0.45f, 0.16f);
        [Tooltip("Flowers, berries or fruit. Each variant picks one. Empty = none.")]
        public List<Color> Accents = new List<Color>();
        [Tooltip("Share of the plant dotted with flowers/fruit (shrubs, cacti).")]
        [Range(0f, 1f)] public float AccentAmount = 0.3f;
        [Tooltip("Random tint per plant.")]
        [Range(0f, 0.5f)] public float ColorVariation = 0.12f;
        [Tooltip("Surface of the leaves, needles or caps.")]
        public FoliageFinish Finish = FoliageFinish.Matte;
        [Tooltip("Flowers, berries and fruit glow by themselves.")]
        public bool GlowingAccents;

        [Header("Placement")]
        [Tooltip("Steepest ground it grows on, in degrees.")]
        [Range(0f, 90f)] public float MaxSlope = 30f;
        [Tooltip("Height range above sea level it grows in, in metres. It shrinks towards the upper limit (tree line).")]
        public Vector2 Elevation = new Vector2(0.5f, 10000f);
        [Tooltip("0 = grows on any ground; 1 = only on grassy ground (avoids rock, sand and bare earth).")]
        [Range(0f, 1f)] public float GrassAffinity = 0.5f;
        [Tooltip("Grows on snowy ground.")]
        public bool ToleratesSnow;
        [Tooltip("Groups plants into patches (forests, meadows of one flower). Size of the patches in metres.")]
        [Min(1f)] public float PatchSize = 60f;
        [Tooltip("Share of the land covered by patches (1 = everywhere, evenly).")]
        [Range(0f, 1f)] public float PatchCoverage = 1f;
        [Tooltip("Density multiplier just above water (river banks, lake shores, beaches).")]
        [Min(0f)] public float NearWater = 1f;
        [Tooltip("Grows only close to water (oasis palms, willows).")]
        public bool OnlyNearWater;
        [Tooltip("Blocks the player (trees, big cacti).")]
        public bool Solid = true;
        [Tooltip("Random size of each placed plant, on top of its variant's own size.")]
        public Vector2 SizeRange = new Vector2(0.85f, 1.15f);

        [Header("Variety")]
        [Tooltip("Number of distinct procedurally grown shapes.")]
        [Range(1, 8)] public int Variants = 4;
        [Tooltip("Changes every generated shape of this species.")]
        public int Seed = 1;
    }

    /// <summary>A species growing in a biome, and how densely.</summary>
    [System.Serializable]
    public class BiomeFlora
    {
        public FloraSpecies Species;
        [Tooltip("Plants per 100 m² where conditions suit it fully.")]
        [Min(0f)] public float Density = 1f;
    }
}
