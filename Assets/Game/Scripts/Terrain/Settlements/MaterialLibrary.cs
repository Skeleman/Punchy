using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>What a built piece is made of: each family is one look, drawn by one shared include
    /// (Shaders/Materials/&lt;Family&gt;.hlsl) whatever shader draws the piece (W-166).</summary>
    public enum MaterialFamily
    {
        None = 0,
        Wood = 1,
        Brick = 2,
        Stone = 3,
        Thatch = 4,
        Plaster = 5,
        Metal = 6,
        Cloth = 7,
        Leather = 8,
        Ice = 9,
        Bone = 10,
        /// <summary>Standing water in a built piece (troughs, basins): flat by design, not a family's include.</summary>
        Water = 11
    }

    /// <summary>The kinds of wood (W-166): rows of MaterialLibrary.Wood, read by Shaders/Materials/Wood.hlsl.
    /// At most eight (the kind rides on the flora wood mark in 0.0025 steps under 0.035).</summary>
    public enum WoodKind
    {
        Oak = 0,
        Pine = 1,
        Driftwood = 2,
        Dark = 3,
        Bleached = 4
    }

    /// <summary>
    /// W-166: the one material library. A table of kinds per family (base colour, grain scale, contrast, knots, wear),
    /// pushed to the shaders as globals so every piece of a kind looks the same wherever it's drawn, and the maps from
    /// what builders know (a kit part's KitMaterial, a culture, a flora form) to a family and kind. A piece keeps its
    /// own colour; the kind decides how its surface reads. Wood first; the other families get rows as their includes
    /// land (W-166 row 3). Cosmetic and seed-free: nothing here moves placement or any hash.
    /// </summary>
    public static class MaterialLibrary
    {
        /// <summary>One kind of wood. Base: its reference colour (also the hue its late wood and knots darken to).
        /// RingSpacing: metres between growth rings. Contrast: how dark the late wood gets (1 oak). Knots: 0..1, how
        /// many half-metre cells hold a knot. Grey: 0..1, how silver its wear bands go. Pores: fine streak strength.</summary>
        public readonly struct WoodRow
        {
            public readonly Color Base;
            public readonly float RingSpacing, Contrast, Knots, Grey, Pores;

            public WoodRow(Color baseColour, float ringSpacing, float contrast, float knots, float grey, float pores)
            {
                Base = baseColour;
                RingSpacing = ringSpacing;
                Contrast = contrast;
                Knots = knots;
                Grey = grey;
                Pores = pores;
            }
        }

        /// <summary>The wood kinds, by WoodKind.</summary>
        public static readonly WoodRow[] Wood =
        {
            new WoodRow(new Color(0.36f, 0.24f, 0.15f), 0.011f, 1.0f, 0.45f, 0.0f, 1.0f),    // oak: warm, open rings
            new WoodRow(new Color(0.46f, 0.33f, 0.20f), 0.007f, 1.25f, 0.9f, 0.0f, 0.7f),    // pine: pale, tight, knotty
            new WoodRow(new Color(0.52f, 0.47f, 0.40f), 0.014f, 0.7f, 0.2f, 0.85f, 1.4f),    // driftwood: washed, silver
            new WoodRow(new Color(0.27f, 0.18f, 0.12f), 0.009f, 0.6f, 0.3f, 0.0f, 0.8f),     // dark: walnut-brown, quiet
            new WoodRow(new Color(0.48f, 0.42f, 0.34f), 0.012f, 0.8f, 0.4f, 0.5f, 1.2f),     // bleached: sun-greyed
        };

        public const int MaxWoodKinds = 8;
        static readonly int GrainKinds = Shader.PropertyToID("_PunchyWoodGrainKinds");
        static readonly int ToneKinds = Shader.PropertyToID("_PunchyWoodToneKinds");

        /// <summary>Pushes the table to the shaders (Wood.hlsl's _PunchyWoodGrainKinds and _PunchyWoodToneKinds).</summary>
        public static void Push()
        {
            var grain = new Vector4[MaxWoodKinds];
            var tone = new Vector4[MaxWoodKinds];
            for (int i = 0; i < MaxWoodKinds; i++)
            {
                var row = Wood[Mathf.Min(i, Wood.Length - 1)];
                grain[i] = new Vector4(row.RingSpacing, row.Contrast, row.Knots, row.Grey);
                // The base colour's hue at luminance 1, so tinting the late wood by it keeps its brightness.
                var lum = Mathf.Max(0.05f, row.Base.r * 0.299f + row.Base.g * 0.587f + row.Base.b * 0.114f);
                var hue = new Vector3(row.Base.r, row.Base.g, row.Base.b) / lum;
                hue = Vector3.Lerp(Vector3.one, hue, 0.35f);
                tone[i] = new Vector4(hue.x, hue.y, hue.z, row.Pores);
            }
            Shader.SetGlobalVectorArray(GrainKinds, grain);
            Shader.SetGlobalVectorArray(ToneKinds, tone);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void PushAtStart() => Push();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void PushInEditor() => Push();
#endif

        /// <summary>The wood a culture builds with (FolkCulture.Elf builds none, W-161: oak only as a fallback).</summary>
        public static WoodKind WoodFor(FolkCulture culture) => culture switch
        {
            FolkCulture.Dwarf => WoodKind.Dark,
            FolkCulture.BatFolk => WoodKind.Dark,
            FolkCulture.MarshFolk => WoodKind.Dark,
            FolkCulture.SnowFolk => WoodKind.Pine,
            FolkCulture.Goblin => WoodKind.Bleached,
            FolkCulture.Ogre => WoodKind.Bleached,
            FolkCulture.FishFolk => WoodKind.Driftwood,
            FolkCulture.SeaFolk => WoodKind.Driftwood,
            FolkCulture.TurtleFolk => WoodKind.Driftwood,
            _ => WoodKind.Oak
        };

        /// <summary>The family a kit part is made of (StructureKit.KitMaterial), given the look its wall or roof takes.
        /// Wall and Roof parts take their surface's family (planks and shingles wood, thatch thatch, plain plaster).</summary>
        public static MaterialFamily FamilyOf(KitMaterial material, KitSurface look) => material switch
        {
            KitMaterial.Wall or KitMaterial.Roof => look switch
            {
                KitSurface.Planks or KitSurface.Shingles => MaterialFamily.Wood,
                KitSurface.Thatch => MaterialFamily.Thatch,
                _ => MaterialFamily.Plaster
            },
            KitMaterial.Wood => MaterialFamily.Wood,
            KitMaterial.Stone or KitMaterial.Rune => MaterialFamily.Stone,
            KitMaterial.Ice => MaterialFamily.Ice,
            KitMaterial.Copper or KitMaterial.Lamp => MaterialFamily.Metal,
            KitMaterial.Net => MaterialFamily.Cloth,
            _ => MaterialFamily.Plaster   // Trim, Opening, Moss, Sand, Ash, Dirt, Flame: coloured plaster for now
        };
    }
}
