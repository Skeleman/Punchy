using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// A kind of ground (grass, dirt, rock, sand, ash, lava…) and its material properties.
    /// Biomes decide where surfaces appear; surfaces decide what the ground looks and behaves like.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Terrain Surface", fileName = "Surface")]
    public class TerrainSurface : ScriptableObject
    {
        [Header("Look")]
        public Color Albedo = new Color(0.5f, 0.5f, 0.5f);
        [Range(0f, 1f)] public float Smoothness = 0.2f;
        [Range(0f, 1f)] public float Metallic;
        [ColorUsage(false, true)] public Color Emission = Color.black;
        [Tooltip("Random brightness breakup so large areas don't look flat.")]
        [Range(0f, 0.5f)] public float ColorVariation = 0.1f;

        [Tooltip("Strength of the fine surface relief (clods, grain, cracks) drawn up close by the terrain shader.")]
        [Range(0f, 1f)] public float Bumpiness = 0.5f;

        [Header("Features")]
        [Tooltip("How densely grass blades grow here (drawn by the ground detail system).")]
        [Range(0f, 1f)] public float GrassDensity;
        [Tooltip("How densely small stones are scattered here (drawn by the ground detail system).")]
        [Range(0f, 1f)] public float Pebbles;
        [Tooltip("Depth of deformable snow here (read by the snow-trail system).")]
        [Range(0f, 1f)] public float SnowDepth;
    }
}
