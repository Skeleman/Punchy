using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// A region type (temperate, desert, ash wastes…). Shapes the terrain within its region and
    /// lists which surfaces cover it: a base surface, then layers painted on top by rule.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Biome", fileName = "Biome")]
    public class BiomeDefinition : ScriptableObject
    {
        [Tooltip("Colour that represents this biome in exported biome map images. Keep it unique.")]
        public Color MapColor = Color.magenta;
        [Tooltip("Relative chance of this biome being picked for a region when biomes come from noise.")]
        [Min(0f)] public float Frequency = 1f;

        [Header("Shape (noise heights only)")]
        [Tooltip("Multiplies the base terrain height inside this biome.")]
        public float HeightScale = 1f;
        [Tooltip("Metres added to the terrain height inside this biome.")]
        public float HeightOffset;

        [Header("Surfaces")]
        public TerrainSurface BaseSurface;
        [Tooltip("Painted over the base in order; later layers cover earlier ones.")]
        public List<SurfaceLayer> Layers = new List<SurfaceLayer>();
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

        [Tooltip("Restrict to a range of heights (metres).")]
        public bool UseHeight;
        public Vector2 HeightRange = new Vector2(80f, 10000f);
        [Min(0.01f)] public float HeightBlend = 8f;

        [Tooltip("Roughens the rule edges with noise so boundaries aren't perfectly smooth contours.")]
        [Range(0f, 1f)] public float EdgeNoise = 0.5f;
        [Tooltip("Overall strength of this layer where its rules match.")]
        [Range(0f, 1f)] public float Opacity = 1f;
    }
}
