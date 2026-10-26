using Unity.Entities;

namespace Punchy.Terrain
{
    /// <summary>
    /// Singleton holding the biome assets in id order, so managed systems (weather) can turn the biome ids in a
    /// GroundSample back into BiomeDefinitions and read their non-terrain settings.
    /// </summary>
    public class BiomePalette : IComponentData
    {
        public BiomeDefinition[] Biomes;
    }
}
