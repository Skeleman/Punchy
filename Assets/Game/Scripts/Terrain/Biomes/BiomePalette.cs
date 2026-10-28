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
        /// <summary>
        /// Per biome, the realm it belongs to (index in TerrainAuthoring.Realms), or -1 (realms off, the ocean, or a
        /// biome no realm lists). With realms on, every region's biome is one of its realm's, so this is its realm.
        /// </summary>
        public int[] BiomeRealms;
    }
}
