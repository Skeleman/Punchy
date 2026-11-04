using System.Collections.Generic;
using Unity.Collections;

namespace Punchy.Terrain
{
    /// <summary>
    /// Generator version 3, water first (brief v3-island-design.md). Opt-in: <c>-island v3</c>, PUNCHY_ISLAND=v3, or the
    /// editor menu Punchy/Island/v3. <see cref="IslandV2System"/> runs it in place of version 2 and ends every v3 bake
    /// with <see cref="V3Audit"/>.
    /// B1 skeleton: each step still hands over to version 2 unchanged, so v3 bakes the v2 island (same counts, same
    /// hashes apart from the fingerprint's version). Later cards replace one step at a time, in bake order: ranges, biome
    /// field, water sources, rivers, the hydrology table, carve, surfaces and flora, places, audit. No step changes
    /// anything version 2 does.
    /// </summary>
    public static class IslandV3
    {
        public const int Version = 3;

        /// <summary>Biome map and height offsets. B2 (ranges) and B3 (biomes follow ranges) replace this step.</summary>
        public static IslandV2.Result Generate(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, Allocator allocator)
        {
            var gen = IslandV2.Generate(palette, hasOcean, allocator);
            gen.Report = "v3 (B1: v2 steps) " + gen.Report;
            return gen;
        }

        /// <summary>River settings. B5-B7 (hydro table, vector rivers, carve) replace this step.</summary>
        public static RiverSettings Rivers(RiverSettings v1) => IslandV2.Rivers(v1);
    }
}
