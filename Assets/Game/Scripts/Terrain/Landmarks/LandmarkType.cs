using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>How a landmark reshapes the ground.</summary>
    public enum LandmarkShape : byte
    {
        [Tooltip("Leaves the ground's shape alone (Flatten and Roughness still apply).")] None,
        [Tooltip("A rounded hill (positive Height) or hollow (negative).")] Dome,
        [Tooltip("Flat top with steep sides (positive Height: a butte) or a flat-bottomed basin (negative).")] Mesa,
        [Tooltip("A step: the far half (+X, before rotation) rises by Height behind a steep face through the centre.")] Cliff,
        [Tooltip("A cone with steepening flanks and a crater at the top (Edge = crater size, in units of the shape).")] Volcano
    }

    public enum LandmarkEffectKind : byte
    {
        [Tooltip("Rising puffs: steam, smoke, geyser spouts, spray, sparkles.")] Plume,
        [Tooltip("A curtain of falling water from Offset (the lip) out and down towards -X.")] Waterfall,
        [Tooltip("Flames (FireSystem) at Offset: Particles tongues of fire Size high, spread over Width, in the Flame " +
                 "palette. Drawn within 150 m. Pair it with a FirePit (or brazier) prop and an ember plume.")] Flame
    }

    /// <summary>Flame colours (FireSystem's palettes).</summary>
    public enum FlamePalette : byte { Wood, Blue, Green, Violet }

    /// <summary>A pool of still water in a landmark (oasis, spring, crater lake).</summary>
    [Serializable]
    public class LandmarkPool
    {
        [Tooltip("Centre, in units of the landmark radius (X is the landmark's facing; a cliff rises towards +X).")]
        public Vector2 Offset;
        [Tooltip("Radius, in units of the landmark radius.")]
        [Range(0.02f, 1f)] public float Radius = 0.3f;
        [Tooltip("Depth at the centre, in metres.")]
        [Min(0.1f)] public float Depth = 1.5f;
    }

    /// <summary>Plants or rocks placed in a landmark (any flora species: trees, boulders, spires, crystals, dens).</summary>
    [Serializable]
    public class LandmarkProp
    {
        public Punchy.Flora.FloraSpecies Species;
        [Tooltip("Per 100 m² inside the ring. Placement uses the species' layer grid, so props never overlap.")]
        [Min(0f)] public float Density = 5f;
        [Tooltip("Where they go, as a range of distances from the centre in units of the landmark radius.")]
        public Vector2 Ring = new Vector2(0f, 1f);
        [Tooltip("Size multiplier range.")]
        public Vector2 Scale = Vector2.one;
        [Tooltip("Exactly one, at the centre (an ancient tree, a den). Density and Ring are ignored.")]
        public bool Centrepiece;
        [Tooltip("Turn its front (doorway, den mouth) towards the centre, as huts round a square.")]
        public bool FaceCentre;
        [Tooltip("Place exactly this many (an inn, a pen, a couple of stalls) evenly round a circle at Ring.x, " +
                 "starting at Angle, instead of scattering by Density. 0 = scatter.")]
        [Min(0)] public int Count;
        [Tooltip("Where round the circle the first one goes, in degrees from the landmark's facing.")]
        public float Angle;
        [Tooltip("Placed ones keep everything else (plants, scattered props) this many metres away.")]
        [Min(0f)] public float Clearance = 4f;
    }

    /// <summary>A procedural effect drawn at a landmark by LandmarkEffectSystem.</summary>
    [Serializable]
    public class LandmarkEffect
    {
        public LandmarkEffectKind Kind;
        [Tooltip("Where it starts, in units of the landmark radius (X is the landmark's facing).")]
        public Vector2 Offset;
        [Tooltip("Plume: how high the puffs rise (m). Waterfall: how far out the water travels before reaching the bottom (m).")]
        public float Height = 6f;
        [Tooltip("Plume: how wide the puffs spread (m). Waterfall: curtain width (m).")]
        public float Width = 2f;
        [Tooltip("Puff size (m).")]
        public float Size = 1.5f;
        [Tooltip("Plume puffs alive at once.")]
        [Range(1, 256)] public int Particles = 40;
        [Tooltip("Seconds each puff lives (plume) or the water takes to fall (waterfall streaks).")]
        [Min(0.1f)] public float Lifetime = 4f;
        [ColorUsage(false)] public Color Color = new Color(0.9f, 0.92f, 0.95f);
        [Range(0f, 1f)] public float Opacity = 0.5f;
        [Tooltip("Shines by itself (embers, sparkles, glowing vapour).")]
        [Min(0f)] public float Glow;
        [Tooltip("Seconds between eruptions (0 = continuous).")]
        [Min(0f)] public float Period;
        [Tooltip("Share of each period the eruption lasts.")]
        [Range(0.02f, 1f)] public float Duty = 0.25f;
        [Tooltip("Flame: the fire's colours (wood fire, or the blue, green and violet of strange fires).")]
        public FlamePalette Flame;
    }

    [Serializable]
    public class LandmarkResident
    {
        public Punchy.Fauna.CreatureSpecies Species;
        public Vector2Int Count = new Vector2Int(1, 2);
        [Tooltip("Keep them within this many metres of their home (penned animals). 0 = anywhere in the landmark.")]
        [Min(0f)] public float Leash;
        [Tooltip("Their home, as a distance from the centre (in units of the radius) and an angle (degrees from " +
                 "the landmark's facing): the same as a placed prop's Ring.x and Angle.")]
        public float HomeRing;
        public float HomeAngle;
        [Tooltip("A job: they spend the day at their home spot doing it, facing Face Ring / Face Angle.")]
        public Punchy.Fauna.CreatureTask Task;
        public float FaceRing;
        public float FaceAngle;
        [Tooltip("Size range for these individuals (children). Zero = the species' own.")]
        public Vector2 Size;
    }

    public static class LandmarkLayout
    {
        /// <summary>
        /// The spot a landmark puts a placed thing: a distance from the centre in units of the radius, and an angle
        /// in degrees from its facing; as a point in the landmark's frame, in radii (for LandmarkSampling.ToWorld).
        /// </summary>
        public static Unity.Mathematics.float2 Spot(float ring, float angleDegrees)
        {
            var a = Unity.Mathematics.math.radians(angleDegrees);
            return new Unity.Mathematics.float2(Unity.Mathematics.math.cos(a), Unity.Mathematics.math.sin(a)) * ring;
        }
    }

    /// <summary>
    /// A natural landmark: a small place with its own ground shape, water, ground cover, plants and rocks, effects
    /// and animals (an oasis, a geyser, a waterfall, a den). Biomes list which landmarks appear in them and how often;
    /// the terrain also takes hand-placed ones. Shapes, pools and ground are baked into the terrain; plants, effects
    /// and residents are read by their systems at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Landmark Type", fileName = "Landmark")]
    public class LandmarkType : ScriptableObject
    {
        [Header("Footprint")]
        [Tooltip("Radius of the area it shapes and decorates, in metres.")]
        [Min(4f)] public float Radius = 30f;
        [Tooltip("Random size multiplier range.")]
        public Vector2 Scale = new Vector2(0.85f, 1.15f);
        [Tooltip("Only placed where the natural ground varies by less than this across the footprint (m).")]
        [Min(0f)] public float MaxRelief = 10f;
        [Tooltip("Only placed where the natural ground varies by at least this much (m): for landmarks that need " +
                 "an existing slope, such as a cave entrance in a hillside.")]
        [Min(0f)] public float MinRelief;
        [Tooltip("Height range above sea level where it can be placed (m).")]
        public Vector2 Elevation = new Vector2(3f, 10000f);

        [Header("Shape")]
        public LandmarkShape Shape = LandmarkShape.None;
        [Tooltip("Metres the shape rises (negative sinks).")]
        public float Height;
        [Tooltip("Size of the shape, in units of the radius.")]
        [Range(0.1f, 1f)] public float ShapeRadius = 0.7f;
        [Tooltip("Width of a mesa's or cliff's slope, in units of the radius.")]
        [Range(0.02f, 1f)] public float Edge = 0.2f;
        [Tooltip("How much the natural ground is levelled to the centre's height (0 = not at all).")]
        [Range(0f, 1f)] public float Flatten = 0.5f;
        [Tooltip("Bumps added over the footprint (m).")]
        [Min(0f)] public float Roughness;

        [Header("Water")]
        public List<LandmarkPool> Pools = new List<LandmarkPool>();
        [Tooltip("Pools have their own look instead of the biome's water.")]
        public bool OverrideWater;
        [ColorUsage(false)] public Color WaterColor = new Color(0.2f, 0.6f, 0.6f);
        [Range(0f, 1f)] public float WaterMurk = 0.3f;
        [Min(0f)] public float WaterGlow;
        [Tooltip("Molten rock instead of water (volcano craters).")]
        [Range(0f, 1f)] public float WaterLava;

        [Header("Ground")]
        [Tooltip("Covers the middle of the landmark (sinter, ash, moss…). Empty = the biome's own ground.")]
        public TerrainSurface Ground;
        [Tooltip("Size of the ground patch, in units of the radius.")]
        [Range(0.05f, 1f)] public float GroundRadius = 0.6f;

        [Header("Plants & rocks")]
        [Tooltip("The biome's own plants are cleared inside this share of the radius (fading out to it).")]
        [Range(0f, 1f)] public float ClearRadius = 0.7f;
        public List<LandmarkProp> Props = new List<LandmarkProp>();

        [Header("Effects")]
        public List<LandmarkEffect> Effects = new List<LandmarkEffect>();

        [Header("Cave")]
        [Tooltip("A cave opens here, running into the rise (best with the Cliff shape) and down to a chamber.")]
        public bool Cave;
        [Tooltip("Length of the passage, in metres.")]
        [Min(5f)] public float CaveLength = 40f;
        [Tooltip("How far the passage descends below the entrance, in metres.")]
        [Min(0f)] public float CaveDepth = 8f;
        [Tooltip("Radius of the passage, in metres.")]
        [Min(1f)] public float CaveRadius = 2.4f;
        [Tooltip("Radius of the chamber at the end, in metres.")]
        [Min(1f)] public float ChamberRadius = 6f;
        [Tooltip("More chambers along the main passage, each a little smaller than the end one.")]
        [Range(0, 6)] public int CaveChambers;
        [Tooltip("Side passages leading off the main one (and off each other, in big networks), each to a small chamber.")]
        [Range(0, 12)] public int CaveBranches;
        [Tooltip("Length of a side passage, in metres.")]
        [Min(5f)] public float BranchLength = 30f;
        [Tooltip("Shafts rising from chambers to the surface: sinkholes that let daylight in (and let the careless fall in).")]
        [Range(0, 4)] public int CaveShafts;
        [Tooltip("Styles this cave can take; it takes one listed for the biome it opens in (see CaveStyle.Biomes).")]
        public List<CaveStyle> CaveStyles = new List<CaveStyle>();

        [Header("Settlement")]
        [Tooltip("Lays the landmark out as a town: streets, plots and what stands on them (see SettlementLayout).")]
        public SettlementLayout Settlement;

        [Header("Companions")]
        [Tooltip("Landmarks placed beside this one where the ground allows (a mining village by its mine): the first in " +
                 "the list that the biome there lists and that finds a spot within Companion Range.")]
        public List<LandmarkType> Companions = new List<LandmarkType>();
        [Min(20f)] public float CompanionRange = 150f;
        [Tooltip("Only ever placed as another landmark's companion, never on its own (biomes still list it, which says " +
                 "where it may go).")]
        public bool OnlyAsCompanion;

        [Header("Residents")]
        [Tooltip("Animals that live here; they appear when the player comes near.")]
        public List<LandmarkResident> Residents = new List<LandmarkResident>();
    }

    /// <summary>A landmark type appearing in a biome, and how often.</summary>
    [Serializable]
    public class BiomeLandmark
    {
        public LandmarkType Type;
        [Tooltip("Landmarks per km² where the ground suits it.")]
        [Min(0f)] public float Density = 0.3f;
        [Tooltip("Settlements only: at least this many in each realm whose biomes list it (where the land allows).")]
        [Range(1, 3)] public int PerRealm = 1;
    }

    /// <summary>A landmark placed by hand.</summary>
    [Serializable]
    public class PlacedLandmark
    {
        public LandmarkType Type;
        [Tooltip("World X and Z.")]
        public Vector2 Position;
        [Tooltip("Facing, in degrees.")]
        public float Rotation;
        public float Scale = 1f;
    }
}
