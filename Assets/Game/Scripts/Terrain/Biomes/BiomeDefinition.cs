using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    public enum TerrainShapeKind : byte
    {
        [Tooltip("Rolling fractal hills.")] Hills,
        [Tooltip("Sharp ridges and peaks, grouped into massifs.")] Ridged,
        [Tooltip("Parallel wind-shaped crests with a gentle windward side and a steep lee face.")] Dunes
    }

    /// <summary>
    /// A region type (temperate, desert, tundra, ocean…). Sets the ground's elevation and shape inside its
    /// region, how much rain it feeds to rivers, and which surfaces cover it: a base surface plus rule-driven layers.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Biome", fileName = "Biome")]
    public class BiomeDefinition : ScriptableObject
    {
        [Tooltip("Colour that represents this biome in exported biome map images. Keep it unique.")]
        public Color MapColor = Color.magenta;
        [Tooltip("Relative chance of this biome being picked for a region when biomes come from noise.")]
        [Min(0f)] public float Frequency = 1f;

        [Header("Climate (noise biomes)")]
        [Tooltip("Regions get a climate from slowly varying fields (each 0..1); a biome is picked mostly where the " +
                 "region's climate is inside all its ranges, so biomes that like the same climate end up together.\n" +
                 "Altitude: lowlands 0 – uplands 1. Temperature falls with altitude.")]
        public Vector2 Altitude = new Vector2(0f, 1f);
        [Tooltip("Cold 0 – hot 1 (also colder at altitude).")]
        public Vector2 Temperature = new Vector2(0f, 1f);
        [Tooltip("Arid 0 – wet 1.")]
        public Vector2 Humidity = new Vector2(0f, 1f);
        [Tooltip("Volcanic activity: mostly 0, rising to 1 at a few hotspots.")]
        public Vector2 Volcanism = new Vector2(0f, 0.3f);

        [Header("Shape (noise heights only)")]
        [Tooltip("Ground level of this biome's lowlands, in metres relative to sea level.")]
        public float BaseElevation = 20f;
        public TerrainShapeKind Shape = TerrainShapeKind.Hills;
        [Tooltip("Height of the relief above the base elevation, in metres. Small = flat.")]
        [Min(0f)] public float Relief = 40f;
        [Tooltip("Typical horizontal size of hills / spacing of ridges or dunes, in metres.")]
        [Min(1f)] public float FeatureSize = 400f;
        [Range(1, 8)] public int Octaves = 5;
        [Tooltip("Above 1 flattens lowlands and sharpens peaks; below 1 rounds them off.")]
        [Range(0.3f, 4f)] public float Sharpness = 1.5f;
        [Tooltip("Quantise heights into steps for mesas and terraces. 0 = off.")]
        [Range(0, 16)] public int TerraceSteps;
        [Tooltip("Dunes only: compass direction the wind blows towards, in degrees.")]
        [Range(0f, 360f)] public float WindDirection = 60f;

        [Header("Water")]
        [Tooltip("How much rain this biome contributes to rivers (0 = none, 1 = lush).")]
        [Range(0f, 1f)] public float Rainfall = 0.5f;
        [Tooltip("How murky and thick this biome's lakes, pools and rivers are (0 = ordinary clear water, 1 = opaque " +
                 "sludge). Murky water takes on Water Color, barely ripples and bubbles.")]
        [Range(0f, 1f)] public float WaterMurk;
        public Color WaterColor = new Color(0.2f, 0.35f, 0.2f);
        [Tooltip("How much the water glows by itself (toxic pools).")]
        [Range(0f, 1f)] public float WaterGlow;
        [Tooltip("Molten rock instead of water: a dark cooling crust over glowing cracks, barely reflective.")]
        [Range(0f, 1f)] public float WaterLava;

        [Header("Weather")]
        [Tooltip("Overrides how likely each weather is here. Presets not listed keep their own Chance, unless they're " +
                 "marked Only In Listed Biomes, in which case they never happen here.")]
        public List<BiomeWeather> Weather = new List<BiomeWeather>();

        [Header("Surfaces")]
        public TerrainSurface BaseSurface;
        [Tooltip("Painted over the base in order; later layers cover earlier ones.")]
        public List<SurfaceLayer> Layers = new List<SurfaceLayer>();

        [Header("Ambience")]
        [Tooltip("How hot the ground gets in sunshine: heat shimmer and mirages (0 = none, 1 = scorching desert).")]
        [Range(0f, 1f)] public float Heat;
        [Tooltip("How cold the air is: the player's breath shows (0 = mild, 1 = arctic). Altitude, night and snow add to it.")]
        [Range(0f, 1f)] public float Cold;
        [Tooltip("Fireflies per 100 m² on warm, dry nights.")]
        [Min(0f)] public float Fireflies;
        [Tooltip("Butterflies per 100 m² on fair days.")]
        [Min(0f)] public float Butterflies;
        [Tooltip("Particles hanging in the air here: sparkles, spores, embers, ash, fog wisps (shared types).")]
        public List<Punchy.Ambience.BiomeParticles> Particles = new List<Punchy.Ambience.BiomeParticles>();

        [Header("Flora")]
        [Tooltip("Plants that grow here (drawn by the flora system). Changing these doesn't re-bake the terrain.")]
        public List<Punchy.Flora.BiomeFlora> Flora = new List<Punchy.Flora.BiomeFlora>();

        [Header("Landmarks")]
        [Tooltip("Natural landmarks that appear here (oases, rock formations, dens…). Baked into the terrain.")]
        public List<BiomeLandmark> Landmarks = new List<BiomeLandmark>();

        [Header("Fauna")]
        [Tooltip("Animals that live here (spawned around the player by FaunaDirector).")]
        public List<Punchy.Fauna.BiomeFauna> Fauna = new List<Punchy.Fauna.BiomeFauna>();
    }

    [Serializable]
    public class BiomeWeather
    {
        public Weather.WeatherPreset Preset;
        [Min(0f)] public float Chance = 1f;
    }

    /// <summary>A surface painted where its rules match. All enabled rules must match (they multiply).</summary>
    [Serializable]
    public class SurfaceLayer
    {
        public TerrainSurface Surface;

        [Tooltip("Restrict to a range of slope angles (degrees, 0 = flat).")]
        public bool UseSlope;
        public Vector2 SlopeRange = new Vector2(35f, 90f);
        [Min(0.01f)] public float SlopeBlend = 5f;

        [Tooltip("Restrict to a range of heights (metres, sea level = 0).")]
        public bool UseHeight;
        public Vector2 HeightRange = new Vector2(80f, 10000f);
        [Min(0.01f)] public float HeightBlend = 8f;

        [Tooltip("Restrict to a range of water depths (metres below the sea or river surface; negative = above the water).\n" +
                 "e.g. (-2, 10000) for beaches and anything underwater.")]
        public bool UseWaterDepth;
        public Vector2 WaterDepthRange = new Vector2(-2f, 10000f);
        [Min(0.01f)] public float WaterDepthBlend = 1f;

        [Tooltip("Only inside river channels.")]
        public bool OnlyInRivers;

        [Tooltip("Break the layer into noisy patches (snow patches, grass tufts in sand…).")]
        public bool UsePatches;
        [Min(1f)] public float PatchSize = 40f;
        [Range(0f, 1f)] public float PatchCoverage = 0.4f;

        [Tooltip("Roughens the slope/height/depth rule edges with noise so boundaries aren't perfect contours.")]
        [Range(0f, 1f)] public float EdgeNoise = 0.5f;
        [Tooltip("Overall strength of this layer where its rules match.")]
        [Range(0f, 1f)] public float Opacity = 1f;
    }
}
