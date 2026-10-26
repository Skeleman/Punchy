using Unity.Entities;

namespace Punchy.Terrain
{
    /// <summary>
    /// Singleton holding the biome assets in id order, so managed systems (weather) can turn the biome ids in a
    /// GroundSample back into BiomeDefinitions and read their non-terrain settings, and the landmark types likewise.
    /// </summary>
    public class BiomePalette : IComponentData
    {
        public BiomeDefinition[] Biomes;
        /// <summary>Landmark types in the landmark blob's type order.</summary>
        public LandmarkType[] Landmarks;
    }
}
