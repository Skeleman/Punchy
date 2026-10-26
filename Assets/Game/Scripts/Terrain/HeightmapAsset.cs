using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// A heightmap image plus the information needed to place it in the world.
    /// The image is a 16-bit grayscale PNG: black = MinHeight, white = MaxHeight, +Z (north) at the top.
    /// Edit the PNG in any image editor; Unity re-imports it and the terrain re-bakes.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Heightmap", fileName = "Heightmap")]
    public class HeightmapAsset : ScriptableObject
    {
        public Texture2D Image;
        [Tooltip("World XZ position of the image's bottom-left pixel.")]
        public float2 Origin = new float2(-1024f, -1024f);
        [Tooltip("Width and depth of the region the image covers, in metres.")]
        public float WorldSize = 2048f;
        public float MinHeight;
        public float MaxHeight = 120f;
    }
}
