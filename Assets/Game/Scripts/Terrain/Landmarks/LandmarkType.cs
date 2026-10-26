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
        [Tooltip("A step: the far half (+X, before rotation) rises by Height behind a steep face through the centre.")] Cliff
    }

    public enum LandmarkEffectKind : byte
    {
        [Tooltip("Rising puffs: steam, smoke, geyser spouts, spray, sparkles.")] Plume,
        [Tooltip("A curtain of falling water from Offset (the lip) out and down towards -X.")] Waterfall
    }

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
    }

    [Serializable]
    public class LandmarkResident
    {
        public Punchy.Fauna.CreatureSpecies Species;
        public Vector2Int Count = new Vector2Int(1, 2);
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
