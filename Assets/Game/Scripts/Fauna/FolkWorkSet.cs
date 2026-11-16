using System.Collections.Generic;
using Punchy.Terrain;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-106 row 1: the work a harsh biome gives its settlements, as data in Resources/WorkSets. Folk in a settlement whose
    /// ground is mostly one of <see cref="Biomes"/> take these posts alongside the usual village posts (FolkJobs.VillagePosts):
    /// sweeping ash, shovelling snow or sand from a door, tying a roof down, mending at a seat. A biome with no set (a
    /// temperate one) keeps the usual posts exactly. Hermits in these biomes work from the same set (row 2), and every hermit
    /// talks to itself from its biome's set's AloneLines; a set with no biomes (WildAlone) holds the lines for anywhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Folk Work Set", fileName = "WorkSet")]
    public class FolkWorkSet : ScriptableObject
    {
        /// <summary>One post: the job, the anchor it stands by, and when the settlement is big enough for it.</summary>
        [System.Serializable]
        public struct Entry
        {
            public FolkJob Job;
            [Tooltip("The anchor kind it works by (a Door to clear, a Sit to mend at).")]
            public AnchorKind Kind;
            [Tooltip("How many free folk the settlement needs before it gets this post.")]
            public int MinFree;
            [Tooltip("How far past the landmark's edge the anchor may be.")]
            public float Beyond;
            [Tooltip("The colour of the dust its work kicks up (ash, snow, sand); alpha 0 for none.")]
            public Color Dust;
        }

        [Tooltip("The biomes whose settlements and hermits work from this set.")]
        public BiomeDefinition[] Biomes = new BiomeDefinition[0];
        [Tooltip("The posts, in hand-out order (merged before the usual posts that need as many free folk).")]
        public Entry[] Jobs = new Entry[0];
        [Tooltip("Hermit talk about living alone here (row 2); {name} is the hermit's name.")]
        public string[] AloneLines = new string[0];
    }

    /// <summary>The work sets, loaded once from Resources/WorkSets, looked up by biome.</summary>
    public static class FolkWorkSets
    {
        static List<FolkWorkSet> all;

        /// <summary>Every work set.</summary>
        public static List<FolkWorkSet> All => all ??= new List<FolkWorkSet>(Resources.LoadAll<FolkWorkSet>("WorkSets"));

        /// <summary>The set a biome works from, or null (temperate and other gentle biomes have none).</summary>
        public static FolkWorkSet For(BiomeDefinition biome)
        {
            if (biome == null)
                return null;
            foreach (var set in All)
                foreach (var b in set.Biomes)
                    // (By name as well: in a player build the palette's biomes come from the baked subscene's
                    // content, which are other objects than the ones a Resources asset points at.)
                    if (b == biome || b != null && b.name == biome.name)
                        return set;
            return null;
        }

        /// <summary>A-106 row 2: what a hermit in this biome says to itself about living alone: its set's lines, else
        /// those of the set that lists no biomes (the wild fallback). Empty if there are none.</summary>
        public static string[] AloneLinesFor(BiomeDefinition biome)
        {
            var set = For(biome);
            if (set != null && set.AloneLines.Length > 0)
                return set.AloneLines;
            foreach (var s in All)
                if (s.Biomes.Length == 0 && s.AloneLines.Length > 0)
                    return s.AloneLines;
            return System.Array.Empty<string>();
        }

        /// <summary>The dust colour of a harsh-biome job (from the first set that lists it), alpha 0 for none.</summary>
        public static Color DustOf(FolkJob job)
        {
            foreach (var set in All)
                foreach (var e in set.Jobs)
                    if (e.Job == job)
                        return e.Dust;
            return Color.clear;
        }
    }
}
