using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// A biome map image plus its placement in the world. Each pixel's colour is a biome's MapColor
    /// (nearest match wins), +Z (north) at the top. Usually lower resolution than the heightmap.
    /// Paint it in any image editor; Unity re-imports it and the terrain re-bakes.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Biome Map", fileName = "BiomeMap")]
    public class BiomeMapAsset : ScriptableObject
    {
        public Texture2D Image;
        [Tooltip("World XZ position of the image's bottom-left pixel.")]
        public float2 Origin = new float2(-1024f, -1024f);
        [Tooltip("Width and depth of the region the image covers, in metres.")]
        public float WorldSize = 2048f;
    }
}
