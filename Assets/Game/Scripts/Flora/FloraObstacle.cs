using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Flora
{
    /// <summary>
    /// Solid plants (trunks, cacti) near the player, as vertical cylinders. Kept up to date by FloraSystem on a
    /// singleton entity; player movement slides around them.
    /// </summary>
    public struct FloraObstacle : IBufferElementData
    {
        /// <summary>Base of the trunk.</summary>
        public float3 Position;
        public float Radius;
        public float Height;
        /// <summary>How far a tree trunk's centre leans from its base at half its height and at its height (world x, z;
        /// FloraModel.TrunkLeanMid/Top, turned and scaled as the tree is drawn). Zero for a straight post or a rock.</summary>
        public float2 LeanMid, LeanTop;

        /// <summary>The trunk's centre (x, z) at a height (world y): as drawn, not a straight post up from its base.</summary>
        public static float2 CentreAt(float3 position, float height, float2 leanMid, float2 leanTop, float y)
        {
            var t = math.saturate((y - position.y) / math.max(height, 0.01f)) * 2f;
            return position.xz + (t < 1f ? leanMid * t : math.lerp(leanMid, leanTop, t - 1f));
        }
    }
}
