using System;
using System.Collections.Generic;
using Punchy.Terrain;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Which domestic animals live in which kind of settlement (dogs, cats, chickens, town pigeons). FaunaDirector
    /// spawns them with the settlement's residents, homed on it and leashed to it. Kept apart from the landmark assets
    /// so creature behaviour and settlement geometry can change independently. Loaded from Resources ("Domestics").
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Domestic Table", fileName = "Domestics")]
    public class DomesticTable : ScriptableObject
    {
        public List<DomesticEntry> Entries = new List<DomesticEntry>();
    }

    [Serializable]
    public class DomesticEntry
    {
        public LandmarkType Settlement;
        public CreatureSpecies Species;
        public Vector2Int Count = new Vector2Int(1, 2);
        [Tooltip("How far from the settlement's middle they roam, as a share of its radius.")]
        [Range(0.1f, 1.2f)] public float Range = 0.8f;
    }
}
