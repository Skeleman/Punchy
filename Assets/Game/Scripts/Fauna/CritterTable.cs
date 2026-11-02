using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// The species of the critter tier (FaunaDirector): the small life put round players from the seed. Loaded from
    /// Resources/Critters; each kind of spot (AmbientLife.CritterSpot) names the species that takes it.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Critter Table", fileName = "Critters")]
    public class CritterTable : ScriptableObject
    {
        [Tooltip("In a tree's crown by day.")] public CreatureSpecies Songbird;
        [Tooltip("On a tree's trunk by day.")] public CreatureSpecies Woodpecker;
        [Tooltip("On a low bough at night.")] public CreatureSpecies Owl;
        [Tooltip("At the water's edge at night.")] public CreatureSpecies Frog;
        [Tooltip("In the grass, day or night.")] public CreatureSpecies Mouse;
        [Header("Wild folk (FaunaDirector.SpawnWildFolk)")]
        [Tooltip("The one who lives alone at a hermit's shelter (a Home anchor).")] public CreatureSpecies Hermit;

        public IEnumerable<CreatureSpecies> All()
        {
            yield return Songbird;
            yield return Woodpecker;
            yield return Owl;
            yield return Frog;
            yield return Mouse;
            yield return Hermit;
        }
    }
}
