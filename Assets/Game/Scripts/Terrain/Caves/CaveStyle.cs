using System;
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
        [Tooltip("How broken the upper walls and ceiling are: ridges, sharp creases and overhangs (0 = a smooth tube, " +
                 "as a lava tube; 1 = jagged rock).")]
        [Range(0f, 1.5f)] public float Jaggedness = 0.7f;
        [Tooltip("Blocky collapse hollows in the ceiling, as a share of the jaggedness (fractured ice, bedded rock).")]
        [Range(0f, 1f)] public float Blockiness = 0.3f;
        [Tooltip("Sharp ribs across the walls and ceiling with scooped hollows between (melt-scalloped ice).")]
        [Range(0f, 1f)] public float Ribs;
        [Tooltip("Space between ribs (m).")]
        [Range(0.8f, 4f)] public float RibSpacing = 1.6f;
        [Tooltip("Flat faces meeting in sharp edges across the walls and ceiling (ore bodies, crystal): 0 = none.")]
        [Range(0f, 1f)] public float Angularity;
        [Tooltip("Size of those faces (m).")]
        [Range(1f, 6f)] public float FacetSize = 2.5f;
        [Tooltip("How straight the passages run (1 = dug: straight drifts of even width, side passages at right " +
                 "angles, as a mine; 0 = winding natural passages).")]
        [Range(0f, 1f)] public float Straightness;

        [Header("Fittings (dug caves)")]
        [Tooltip("A timber frame (flora form Mine Frame) set across the passages every Frame Spacing, scaled to fit.")]
        public Punchy.Flora.FloraSpecies Frame;
        [Min(1f)] public float FrameSpacing = 4f;
        [Tooltip("A wall torch (flora form Torch) every Torch Spacing along the passages, on alternate walls, lit with a " +
                 "flame and a warm light near the camera.")]
        public Punchy.Flora.FloraSpecies Torch;
        [Min(2f)] public float TorchSpacing = 10f;
        [Tooltip("How flat the floor is: 1 = a level floor halfway down the passage, 0 = the passage's own round bottom.")]
        [Range(0f, 1f)] public float FloorFlatness = 0.85f;
        [Tooltip("How many ledges and steps the floor has.")]
        [Range(0f, 1f)] public float FloorSteps = 0.4f;

        [Tooltip("The floor is soft enough to dig (earth, sand, rotten rock): chambers get Burrow anchors.")]
        public bool Burrows = true;

        [Header("Speleothems")]
        [Tooltip("Stalactites, stalagmites and columns per 100 m² of chamber floor (icicles, crystal spikes, lava drips " +
                 "by style). Only in chambers, never across the way through or in a pool. 0 = none.")]
        [Range(0f, 15f)] public float SpeleothemDensity = 4f;
        [Tooltip("Typical length, as a share of the chamber's height.")]
        [Range(0.05f, 0.9f)] public float SpeleothemLength = 0.4f;
        [Tooltip("Radius at the root, in metres.")]
        [Range(0.2f, 2f)] public float SpeleothemThickness = 0.55f;
        [Tooltip("Share that hang from the ceiling (the rest stand on the floor).")]
        [Range(0f, 1f)] public float Hanging = 0.55f;
        [Tooltip("Chance one reaches all the way from floor to ceiling as a column.")]
        [Range(0f, 1f)] public float Columns = 0.1f;
        [Tooltip("Share of the hanging ones that drip water now and then, a drop falling to the floor (or a pool) with a " +
                 "small splash. 0 for frozen or molten caves.")]
        [Range(0f, 1f)] public float Drips = 0.4f;

        [Header("Rubble and moss")]
        [Tooltip("Fallen rocks per 100 m² of chamber floor, heaped against the walls and scattered across the middle. " +
                 "Part of the rock (drawn with it, solid), never across the way through or in a pool.")]
        [Range(0f, 15f)] public float Rubble = 3f;
        [Tooltip("Typical radius of a fallen rock, in metres.")]
        [Range(0.3f, 1.5f)] public float RubbleSize = 0.6f;
        [Tooltip("Moss on floors and lower walls, in patches, thickest where daylight comes in.")]
        [Range(0f, 1f)] public float Moss;
        [ColorUsage(false)] public Color MossColor = new Color(0.22f, 0.32f, 0.12f);

        [Header("Plants")]
        [Tooltip("Plants on chamber and passage floors (glowing mushrooms, crystals, mossy shrubs): any flora species, " +
                 "drawn with the flora system's own meshes and material.")]
        public List<CaveFlora> Flora = new List<CaveFlora>();

        [Header("Pools")]
        [Tooltip("Chance a cave of this style has pools sunk into its biggest chambers' floors (off to one side, " +
                 "clear of the way through).")]
        [Range(0f, 1f)] public float PoolChance;
        [Tooltip("The pools are molten lava (glowing onto the walls, and the player can't step in) rather than water.")]
        public bool LavaPools;
        [Tooltip("Pool radius as a share of the chamber's.")]
        [Range(0.15f, 0.5f)] public float PoolSize = 0.32f;
        [Tooltip("Chance a cave of this style has a stream running along its main passage floor, in a channel of its " +
                 "own, flowing down into the cave.")]
        [Range(0f, 1f)] public float StreamChance;
        [Tooltip("Chance of a waterfall pouring from high on a wall into one of the cave's pools (at most two a cave).")]
        [Range(0f, 1f)] public float WaterfallChance;

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

        [Header("Floor")]
        [Tooltip("Ground facing up gets its own material: packed earth, gravel, sand, ash, frost crust.")]
        [ColorUsage(false)] public Color FloorColor = new Color(0.45f, 0.4f, 0.33f);
        [ColorUsage(false)] public Color FloorDark = new Color(0.25f, 0.22f, 0.18f);
        [Tooltip("Speckle of grains and pebbles (0 = smooth mud or ice, 1 = coarse gravel).")]
        [Range(0f, 1f)] public float FloorGrit = 0.5f;
        [Range(0f, 1f)] public float FloorGloss = 0.1f;

        [Header("Surface detail")]
        [Tooltip("Strength of the fine surface relief: rough grain on rock, frost on ice. It also adds glints where the " +
                 "lantern catches it.")]
        [Range(0f, 1f)] public float Detail = 0.3f;
        [Tooltip("Detail repeats per metre: above 1 makes it finer.")]
        [Range(0.2f, 4f)] public float DetailScale = 1f;
        [Tooltip("A soft frosty rim where the wall turns away from the eye.")]
        [Range(0f, 1f)] public float Sheen;

        [Header("Glow")]
        [Tooltip("Light the rock gives off by itself, in veins: crystal light, lava cracks, glowing lichen.")]
        [ColorUsage(false, true)] public Color GlowColor = new Color(0.4f, 0.8f, 1f);
        [Min(0f)] public float Glow;
        [Tooltip("Width of the glowing cracks.")]
        [Range(0.01f, 0.5f)] public float GlowCover = 0.08f;
        [Tooltip("Share of the walls that has cracks, in patches (never the floor). 0 = none.")]
        [Range(0f, 1f)] public float Cracks = 0.3f;
        [Tooltip("Vein spacing: above 1 makes them finer.")]
        [Range(0.2f, 4f)] public float GlowScale = 1f;
        [Tooltip("Share of the glow given off by the whole wall rather than only the veins (ice, softly lit crystal).")]
        [Range(0f, 1f)] public float GlowEverywhere;
        [Tooltip("How much the cracks darken the rock besides any glow in them: dark fissures in ice, black seams.")]
        [Range(0f, 1f)] public float CrackDarkness;
        [Tooltip("How broken the cracks are: 0 = whole nets of cells, 1 = short scattered fragments that fade in and out.")]
        [Range(0f, 1f)] public float CrackBreakup;

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
            table[i + 4] = new Vector4(GlowScale, GlowEverywhere, Cracks, CrackDarkness);
            var floor = FloorColor.linear;
            var floorDark = FloorDark.linear;
            table[i + 5] = new Vector4(floor.r, floor.g, floor.b, FloorGrit);
            table[i + 6] = new Vector4(floorDark.r, floorDark.g, floorDark.b, FloorGloss);
            table[i + 7] = new Vector4(Detail, DetailScale, Sheen, Moss);
            var moss = MossColor.linear;
            table[i + 8] = new Vector4(moss.r, moss.g, moss.b, CrackBreakup);
        }
    }

    /// <summary>Where in a cave a plant grows: by the daylight (mouths, shafts), away from it, or anywhere.</summary>
    public enum CaveLight : byte
    {
        Anywhere,
        [Tooltip("Within reach of daylight: just inside a mouth or under a shaft (mossy shrubs, ferns).")] NearDaylight,
        [Tooltip("Away from daylight (glowing mushrooms and crystals).")] Dark
    }

    /// <summary>A plant species growing on a cave style's floors.</summary>
    [Serializable]
    public class CaveFlora
    {
        public Punchy.Flora.FloraSpecies Species;
        [Tooltip("Plants per 100 m² of floor where the light suits them.")]
        [Range(0f, 20f)] public float Density = 2f;
        public CaveLight Light;
        [Tooltip("Hangs from the ceiling instead of growing on the floor (roots): only where the rock overhead is " +
                 "thinner than Max Cover and the ground above is vegetated, so not under deserts, ice or bare rock.")]
        public bool Hanging;
        [Min(0.5f)] public float MaxCover = 5f;
    }

    /// <summary>
    /// The plant species every cave style uses, in one order shared by baking (which stores a species index in each
    /// cave plant) and CaveSystem (which grows their meshes in the same order).
    /// </summary>
    public static class CaveFloraTable
    {
        public static List<Punchy.Flora.FloraSpecies> Collect(IReadOnlyList<LandmarkType> types)
        {
            var species = new List<Punchy.Flora.FloraSpecies>();
            foreach (var style in CaveStyleTable.Collect(types))
            {
                foreach (var f in style.Flora)
                    if (f != null && f.Species != null && !species.Contains(f.Species))
                        species.Add(f.Species);
                if (style.Frame != null && !species.Contains(style.Frame))
                    species.Add(style.Frame);
                if (style.Torch != null && !species.Contains(style.Torch))
                    species.Add(style.Torch);
            }
            return species;
        }
    }

    /// <summary>
    /// The styles in use and their order, shared by baking (which stores a style index in each cave) and CaveSystem
    /// (which uploads the looks in the same order). Slot 0 is no style: the cave material's own colours.
    /// </summary>
    public static class CaveStyleTable
    {
        public const int Vectors = 9;
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
