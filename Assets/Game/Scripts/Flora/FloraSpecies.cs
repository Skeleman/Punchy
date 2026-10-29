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
                 "bowl in the middle.")] Fountain,
        [Tooltip("Firewood: logs (Bark) stacked in a pile, and a chopping block with an axe stuck in it, towards +Z.")] Woodpile,
        [Tooltip("A well: a round stone wall (Foliage Variation) round dark water, two posts (Bark) holding a little " +
                 "roof (Foliage), a crank and a bucket.")] Well,
        [Tooltip("Lily pads floating in a loose cluster (Foliage), some with cupped flowers in the accent colour. " +
                 "Height is a pad's size; Spread the cluster's; Branches = pads.")] LilyPad,
        [Tooltip("A clump of tall, thin, arching blades (Foliage to Foliage Variation); Accent Amount of them carry a " +
                 "slim cattail head in the accent colour. Branches = blades.")] Reeds,
        [Tooltip("Roots hanging down from a cave ceiling (the plant's origin is the ceiling): tapered, wandering " +
                 "strands with a few side roots, in Bark. Height is the longest; Spread how widely they hang; " +
                 "Branches = strands.")] Roots,
        [Tooltip("A leader's hall: a long timber hall on a stone plinth under a steep gabled roof with crossed ridge beams, " +
                 "a wide door, lit windows (first accent) and banners (second accent). Height is to the ridge; Spread its length.")] Hall,
        [Tooltip("A shrine: a stepped stone platform under a small roof on four posts, round an altar whose stone glows in " +
                 "the accent colour. Height is to the roof's tip; Spread its width.")] Shrine,
        [Tooltip("A garden: a low wattle fence with a gap at the front, round rows of vegetables (Foliage) and a few flowers " +
                 "(accents). Height is the fence; Spread the garden's width relative to it.")] Garden,
        [Tooltip("A big folk's lodge: low and wide, hides (Foliage) draped over a frame of crossed poles (Bark) down to the " +
                 "ground, with tusks (first accent) flanking a hide-flap door and skulls on the ridge. Height is to the ridge; " +
                 "Spread its length.")] Lodge,
        [Tooltip("A signpost: a post with two or three arm boards pointing different ways (Bark, boards in Foliage) and a little " +
                 "cap. Height is the post.")] Signpost,
        [Tooltip("One 6.6 m span of a plank bridge (Bark), deck top at the origin, piles down into the water and a rail each " +
                 "side. Height is the rail; Spread the deck's width (m).")] Bridge,
        [Tooltip("One 3 m length of palisade: a row of sharpened logs (Bark) sunk into the ground with binding rails behind. " +
                 "Height is the logs'.")] Palisade,
        [Tooltip("A clutch of eggs in a shallow nest (cave floors: what spiders, lizards and salamanders guard). Height is " +
                 "an egg's length, Branches the number of eggs, Spread how loosely they lie; Foliage colours the eggs, " +
                 "Bark the nest of grit or silk.")] Eggs,
        [Tooltip("A mine's timber frame: two posts and a lintel across a passage, a metre either side of its middle and a " +
                 "metre high, scaled to fit (Bark; Thickness the timbers' radius per metre).")] MineFrame,
        [Tooltip("A torch on a wall bracket, leaning out a little: Height is the stick, Bark the stick, Foliage the " +
                 "charred head. Cave styles light a flame on it (FireSystem).")] Torch,
        [Tooltip("A plank bench on two stone feet, its seat 0.45 m up and 1.8 m long (Bark the planks, Foliage the " +
                 "stone). A sitter faces +Z. Towns' Sit anchors expect these sizes at scale 1.")] Bench,
        [Tooltip("A log lying on its side to sit on, 0.4 m high and 1.8 m long (Bark), with cut ends. A sitter faces +Z.")] Log,
        [Tooltip("A sawn stump to sit on, 0.42 m high (Bark, with a pale cut top).")] Stump,
        [Tooltip("A fire pit: a ring of stones (Bark; Height the ring's radius, Branches how many) round an ash bed with " +
                 "charred logs (Foliage) leaning together and glowing embers (first accent; set Glowing Accents so they " +
                 "light the ground). The flames themselves are the landmark's Flame effect.")] FirePit,
        [Tooltip("A broadleaf tree (as BroadleafTree) with a wild bees' comb hanging from its trunk under the crown, at " +
                 "HiveHeight of the trunk (first accent the comb's colour). Use one variant so the comb's height is " +
                 "known; Bee Tree landmarks put a Hive anchor round it.")] HiveTree,
        [Tooltip("A glacier ice arch: a jagged bow of ice from two feet (Height its height, Spread its span over height, " +
                 "Thickness the legs' radius over height), in Foliage; Finish Glass draws it as clear ice. Walk under it.")] IceArch,
        [Tooltip("A fulgurite (lightning-fused sand): a knobbly glass tube twisting up out of a sand-crusted mound, forking " +
                 "Branches times, crusted with sand along its length (Height its height, Thickness the tube's radius over " +
                 "height; the tube in Foliage, so Finish Glass makes it glass; the crust in Bark).")] Fulgurite,
        [Tooltip("An arcing spire (electric wastelands): Branches prongs of dark glinting stone (Bark) rising from a cracked " +
                 "base, with electric arcs (the first accent, or blue-white) crackling between their tips. Height its height, " +
                 "Spread how far apart the prongs stand, Thickness their radius over height.")] ArcSpire,
        [Tooltip("Floating debris (magnetic anomaly): Branches lumps of rock (Bark) hovering round a cracked mound, slowly " +
                 "orbiting it and bobbing. Height the highest a piece floats, Spread the ring's radius over height. Not solid.")] Debris
    }

    public enum FoliageFinish : byte
    {
        [Tooltip("Ordinary leaves.")] Matte,
        [Tooltip("Polished metal: sharp highlights and glittering glints.")] Metallic,
        [Tooltip("Shines by itself, gently pulsing; visible at night.")] Glowing,
        [Tooltip("Glassy crystal: a dark clear body with a faint glow inside, a bright rim where it turns away, sharp " +
                 "highlights and a shift of hue at the edges as if light bent through it. Opaque (drawn solid).")] Glass
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
        [Tooltip("How much the glowing parts shimmer (± share of their brightness, slowly, out of step plant to plant). " +
                 "0 = a steady glow (crystals).")]
        [Range(0f, 1f)] public float GlowPulse = 0.12f;
        [Tooltip("Bark (trunk, branches, stems) cracked with glowing seams that pulse slowly, like embers under char " +
                 "(Ember Forest) or veins of light (weird biomes). Shader only: no extra geometry.")]
        public bool GlowingSeams;
        [Tooltip("Trees (BroadleafTree, HiveTree): the share of variants with bracket fungus shelves stepping up the trunk, " +
                 "in the first accent colour (glowing with Glowing Accents).")]
        [Range(0f, 1f)] public float Brackets;
        [Tooltip("Trees: the share of variants with strands hanging from their limbs: vines and lianas (green), moss beards " +
                 "(grey-green, short) or webs of roots, in Hanging Color, swaying in the wind.")]
        [Range(0f, 1f)] public float Hanging;
        public Color HangingColor = new Color(0.25f, 0.4f, 0.2f);
        [Tooltip("Length of the hanging strands (m, at scale 1).")]
        public Vector2 HangingLength = new Vector2(1f, 3f);
        [Tooltip("Trees: hanging strands as wispy beards (a few fine threads each, no leaves: lichen, old man's beard) " +
                 "rather than leafy vines.")]
        public bool HangingWisps;
        [Tooltip("Trees: the share of variants with a few cocoons hanging from their limbs on threads, now and then twitching.")]
        [Range(0f, 1f)] public float Cocoons;
        [Tooltip("Trees: the share of variants with a face in the bark: hollow eyes that glow faintly and blink, and a mouth.")]
        [Range(0f, 1f)] public float BarkFace;
        [Tooltip("Bark veined with cool light that flows slowly up the trunk (weird biomes), instead of ember seams.")]
        public bool GlowingVeins;
        [Tooltip("Rock: banded strata by height, the same bands and colours as terrain surfaces with Strata (mesa cliffs), " +
                 "so rock props line up with the canyon walls round them; with the built things' weathering too.")]
        public bool RockStrata;
        [Tooltip("Mushrooms: caps that breathe, slowly swelling and settling.")]
        public bool Breathes;

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
        [Tooltip("Grows in water instead of on land, where it's between Water Depth's two depths (m).")]
        public bool Aquatic;
        public Vector2 WaterDepth = new Vector2(0.1f, 1.5f);
        [Tooltip("Aquatic: sits on the surface (lily pads) rather than rising from the bed (reeds).")]
        public bool Floats;
        [Tooltip("Aquatic: only in river rapids at least this steep (0..1; 0 = anywhere). Rocks in white water.")]
        [Range(0f, 1f)] public float MinRapids;
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
