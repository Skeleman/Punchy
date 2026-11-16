using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The species saved from the Model Lab (M-06), in the order they were made. FaunaDirector.Build gives them network
    /// indices after every built-in species, in this order (not by name), so saving one never moves an index the
    /// built-ins already had; each lives where its own Homes say. Append only: removing or reordering an entry moves
    /// the later ones' indices. Loaded from Resources/LabSpecies, so a player build and the editor read the same list;
    /// no asset means no Lab species.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Lab Species List", fileName = "LabSpecies")]
    public class LabSpeciesList : ScriptableObject
    {
        public const string ResourcePath = "LabSpecies";

        [Tooltip("Lab-made species, oldest first. Append only.")]
        public List<CreatureSpecies> Species = new List<CreatureSpecies>();

        static LabSpeciesList loaded;
        static bool looked;

        /// <summary>The list, or null when none has been saved.</summary>
        public static LabSpeciesList Load()
        {
            if (!looked || loaded == null)
            {
                loaded = Resources.Load<LabSpeciesList>(ResourcePath);
                looked = true;
            }
            return loaded;
        }

        /// <summary>The Lab species in index order (empty when there are none).</summary>
        public static IReadOnlyList<CreatureSpecies> All()
        {
            var list = Load();
            return list != null ? list.Species : (IReadOnlyList<CreatureSpecies>)System.Array.Empty<CreatureSpecies>();
        }
    }
}
