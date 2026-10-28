using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// A realm: a large family of related biomes (Frost, Fire, Desert…) that the island is first divided into, before
    /// each realm's biome regions pick among its own biomes. Keeps hot and cold apart and gives each part of the island
    /// one character, with variety inside it. Realms are listed in a <see cref="RealmSet"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Realm", fileName = "Realm")]
    public class RealmDefinition : ScriptableObject
    {
        [Tooltip("Colour of this realm on the realm preview map.")]
        public Color MapColor = Color.magenta;
        [Tooltip("How common this realm is, relative to the others (before climate).")]
        [Min(0f)] public float Frequency = 1f;
        [Tooltip("The biomes inside this realm, each with a weight (times the biome's own Frequency and climate fit). " +
                 "A realm's climate is its biomes': it goes where any of them would fit.")]
        public List<RealmBiome> Biomes = new List<RealmBiome>();
        [Tooltip("Realms this one must never border (e.g. Frost and Fire). Where they would, the set's buffer realm " +
                 "goes between them.")]
        public List<RealmDefinition> NotNextTo = new List<RealmDefinition>();
    }

    [Serializable]
    public class RealmBiome
    {
        public BiomeDefinition Biome;
        [Min(0f)] public float Weight = 1f;
    }
}
