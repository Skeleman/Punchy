using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Flora
{
    public static partial class FloraPlacement
    {
        // #682: a town's buildings take their roof, plaster and thatch from the biome at its centre, so a desert town
        // gets no slate. A pure remap of the drawn model: no draws move.
        static int TownSurfaceModel(in TerrainSource source, in FloraRules rule, float2 centre, int model)
        {
            if (rule.Patterns > 0 || rule.Variants < 2 || !source.Biomes.IsCreated)
                return model;
            source.SampleBiomes(centre, out var surface, out _);
            var id = surface.Dominant();
            ref var biomes = ref source.Biomes.Value.Biomes;
            var biome = id >= 0 && id < biomes.Length ? (BiomeFamily)biomes[id].Family : BiomeFamily.Temperate;
            return rule.FirstModel + MaterialLibrary.SurfaceVariant(model - rule.FirstModel, rule.Variants, MaterialLibrary.SurfaceStride(rule.ShoreKit != 0), biome);
        }
    }
}
