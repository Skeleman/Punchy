using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// What a cave is like inside: the shape of its passages and the look of its rock (limestone, sandstone, lava tube,
    /// ice, crystal…). Cave landmarks list the styles they can take; each cave takes one whose Biomes include the
    /// biome it opens in (or one with no Biomes at all), so the same cave landmark looks different in each biome.
    /// The shape is baked into the cave; the look is drawn by the cave shader from a small table CaveSystem uploads.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Cave Style", fileName = "CaveStyle")]
    public class CaveStyle : ScriptableObject
    {
        [Tooltip("Biomes this style is used in. Empty = anywhere (a fallback).")]
        public List<BiomeDefinition> Biomes = new List<BiomeDefinition>();

        [Header("Shape")]
        [Tooltip("Passage width as a multiple of the landmark's radii (wide galleries > 1).")]
        [Range(0.5f, 2f)] public float Width = 1f;
        [Tooltip("Passage height as a multiple of its width (tall fissures > 1, low galleries < 1).")]
        [Range(0.4f, 2.5f)] public float Height = 1f;
        [Tooltip("How lumpy the walls are (0 = smooth, as a lava tube or ice; 1 = ordinary rock).")]
        [Range(0f, 2f)] public float Roughness = 1f;
        [Tooltip("Size of the wall lumps: above 1 makes them smaller and busier.")]
        [Range(0.3f, 3f)] public float Grain = 1f;

        [Tooltip("The floor is soft enough to dig (earth, sand, rotten rock): chambers get Burrow anchors.")]
        public bool Burrows = true;

        [Header("Rock")]
        [ColorUsage(false)] public Color RockColor = new Color(0.48f, 0.44f, 0.4f);
        [ColorUsage(false)] public Color DarkColor = new Color(0.22f, 0.2f, 0.18f);
        [Tooltip("How strongly horizontal bands show (sandstone, layered rock).")]
        [Range(0f, 1f)] public float Strata = 0.3f;
        [Tooltip("Bands per metre.")]
        [Range(0.1f, 4f)] public float StrataFrequency = 0.37f;

        [Header("Finish")]
        [Tooltip("Wet or polished highlights from lanterns (damp rock, ice).")]
        [Range(0f, 1f)] public float Gloss;
        [Tooltip("Highlights take the rock's colour and the diffuse dims (ore, metal veins).")]
        [Range(0f, 1f)] public float Metallic;
        [Tooltip("Flat crystal facets that catch the light at different angles.")]
        [Range(0f, 1f)] public float Facets;
        [Tooltip("Light passing into the rock and glowing back out (ice).")]
        [Range(0f, 1f)] public float Translucency;

        [Header("Glow")]
        [Tooltip("Light the rock gives off by itself, in veins: crystal light, lava cracks, glowing lichen.")]
        [ColorUsage(false, true)] public Color GlowColor = new Color(0.4f, 0.8f, 1f);
        [Min(0f)] public float Glow;
        [Tooltip("How much of the wall the veins cover.")]
        [Range(0.01f, 0.5f)] public float GlowCover = 0.08f;
        [Tooltip("Vein spacing: above 1 makes them finer.")]
        [Range(0.2f, 4f)] public float GlowScale = 1f;
        [Tooltip("Share of the glow given off by the whole wall rather than only the veins (ice, softly lit crystal).")]
        [Range(0f, 1f)] public float GlowEverywhere;

        /// <summary>The look as four vectors for the cave shader's style table.</summary>
        public void Pack(Vector4[] table, int slot)
        {
            var rock = RockColor.linear;
            var dark = DarkColor.linear;
            var i = slot * CaveStyleTable.Vectors;
            table[i] = new Vector4(rock.r, rock.g, rock.b, Strata);
            table[i + 1] = new Vector4(dark.r, dark.g, dark.b, StrataFrequency);
            table[i + 2] = new Vector4(GlowColor.r * Glow, GlowColor.g * Glow, GlowColor.b * Glow, GlowCover);
            table[i + 3] = new Vector4(Gloss, Metallic, Facets, Translucency);
            table[i + 4] = new Vector4(GlowScale, GlowEverywhere, 0f, 0f);
        }
    }

    /// <summary>
    /// The styles in use and their order, shared by baking (which stores a style index in each cave) and CaveSystem
    /// (which uploads the looks in the same order). Slot 0 is no style: the cave material's own colours.
    /// </summary>
    public static class CaveStyleTable
    {
        public const int Vectors = 5;
        public const int MaxStyles = 32;

        public static List<CaveStyle> Collect(IReadOnlyList<LandmarkType> types)
        {
            var styles = new List<CaveStyle>();
            if (types == null)
                return styles;
            foreach (var t in types)
                if (t != null && t.Cave)
                    foreach (var s in t.CaveStyles)
                        if (s != null && !styles.Contains(s) && styles.Count < MaxStyles - 1)
                            styles.Add(s);
            return styles;
        }
    }
}
