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
    }
}
