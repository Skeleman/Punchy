using System.Collections.Generic;

namespace Punchy.Terrain
{
    /// <summary>The broad climate family of a biome (<see cref="BiomeDefinition.Family"/>). Island v2/v3 realms each
    /// take one family and pick their biomes from it.</summary>
    public enum BiomeFamily : byte { None, Arid, Wet, Cold, Volcanic, Temperate }

    /// <summary>
    /// W-116 (audit 5): biome look-ups by family and tag instead of by asset name, so a new or renamed biome is a data
    /// change and a typo can't fail silently. The tag names code uses live here; a biome opts in by listing the tag in
    /// its asset. Rule: no biome-name string literals in game code (the suite checks it).
    /// </summary>
    public static class BiomeTags
    {
        /// <summary>The biome a volcano border ridge is painted with.</summary>
        public const string RidgeVolcano = "ridge-volcano";
        /// <summary>The biome a mesa border ridge (between two arid realms) is painted with.</summary>
        public const string RidgeMesa = "ridge-mesa";
        /// <summary>The biome any other border ridge is painted with; the grand falls also count it as high ground.</summary>
        public const string RidgeMountain = "ridge-mountain";
        /// <summary>The coast the archipelago is raised off.</summary>
        public const string ArchipelagoCoast = "archipelago-coast";
        /// <summary>Wooded biomes.</summary>
        public const string Forest = "forest";
        /// <summary>Biomes whose plants are forest-fire fuel (their flora burns).</summary>
        public const string Burning = "burning";
        /// <summary>Frozen coasts icebergs calve from.</summary>
        public const string Icy = "icy";
        /// <summary>Open sea: icebergs drift here.</summary>
        public const string Sea = "sea";
        /// <summary>Where mantraps grow (placed in family order, each with its own cap).</summary>
        public const string Mantrap = "mantrap";
        /// <summary>Mystic glades: extra butterflies on top of the biome's density.</summary>
        public const string Glade = "glade";
        /// <summary>Open meadow grassland (the debug meadow finder's grassland-only search).</summary>
        public const string Meadow = "meadow";
        /// <summary>W-114: where the Ashlands ribcage lies (the largest patch of the first biome with the tag).</summary>
        public const string Ribcage = "ribcage";

        /// <summary>Palette index of the first biome with the tag, or -1.</summary>
        public static int First(IReadOnlyList<BiomeDefinition> palette, string tag)
        {
            for (int i = 0; i < palette.Count; i++)
                if (palette[i] != null && palette[i].HasTag(tag))
                    return i;
            return -1;
        }

        /// <summary>Palette indices of the family's biomes, in FamilyOrder (palette order breaks ties).</summary>
        public static List<int> InFamily(IReadOnlyList<BiomeDefinition> palette, BiomeFamily family) =>
            Ordered(palette, b => b.Family == family);

        /// <summary>Palette indices of the biomes with the tag, in FamilyOrder (palette order breaks ties).</summary>
        public static List<int> Tagged(IReadOnlyList<BiomeDefinition> palette, string tag) =>
            Ordered(palette, b => b.HasTag(tag));

        static List<int> Ordered(IReadOnlyList<BiomeDefinition> palette, System.Func<BiomeDefinition, bool> match)
        {
            var list = new List<int>();
            for (int i = 0; i < palette.Count; i++)
                if (palette[i] != null && match(palette[i]))
                    list.Add(i);
            list.Sort((a, b) => palette[a].FamilyOrder != palette[b].FamilyOrder ? palette[a].FamilyOrder.CompareTo(palette[b].FamilyOrder) : a.CompareTo(b));
            return list;
        }
    }
}
