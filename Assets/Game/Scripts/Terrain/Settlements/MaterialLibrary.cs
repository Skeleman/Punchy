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
        Water = 11,
        /// <summary>Painted trim and accents (buoy floats, trim boards): a smooth coat, a little chipped (Materials/Paint.hlsl).</summary>
        Paint = 12
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

    /// <summary>The kinds of natural stone (W-166 #336): rows of MaterialLibrary.Stone. Every bare rock wall names
    /// one (cave walls now; cliff scarps and river notches next), and its relief (lumps, creases, bedding, ribs, faces,
    /// fine grain) comes from that row, so a stone breaks the same way wherever it stands.</summary>
    public enum StoneKind
    {
        Plain = 0,
        Granite = 1,
        Limestone = 2,
        Sandstone = 3,
        Basalt = 4,
        Ice = 5,
        Crystal = 6,
        Ore = 7,
        Shale = 8,
        Tuff = 9
    }

    /// <summary>The kinds of cloth (W-166 #336): rows of MaterialLibrary.Cloth, read by Shaders/Materials/Cloth.hlsl.
    /// Linen is MaterialFamily.Cloth (tents, awnings, banners, nets, gear cloth), Hide is MaterialFamily.Leather.</summary>
    public enum ClothKind
    {
        Linen = 0,
        Hide = 1
    }

    /// <summary>The kinds of metal (W-166 #336): rows of MaterialLibrary.Metal, read by Shaders/Materials/Metal.hlsl.
    /// Iron is every metal drawn so far (kit copper and lamps, tools and gear, colossus metal).</summary>
    public enum MetalKind
    {
        Iron = 0
    }

    /// <summary>
    /// W-166: the one material library. A table of kinds per family (base colour, grain scale, contrast, knots, wear),
    /// pushed to the shaders as globals so every piece of a kind looks the same wherever it's drawn, and the maps from
    /// what builders know (a kit part's KitMaterial, a culture, a flora form) to a family and kind. A piece keeps its
    /// own colour; the kind decides how its surface reads. Wood first; the other families get rows as their includes
    /// land (W-166 row 3). Creature coverings (fur, feathers, scales, skin, chitin) are kinds too (Coat). Cosmetic and seed-free: nothing here moves placement or any hash.
    /// </summary>
    public static class MaterialLibrary
    {
        /// <summary>One kind of wood. Base: its reference colour (also the hue its late wood and knots darken to).
        /// RingSpacing: metres between growth rings. Contrast: how dark the late wood gets (1 oak). Knots: 0..1, how
        /// many knot cells hold a knot. Grey: 0..1, how silver its wear bands go. Pores: fine streak strength.
        /// BoardScale: painted board and shingle width (1 = 21 cm boards, 30 cm shingles; laid geometry keeps its size).
        /// KnotSpacing: metres per knot cell. Figure: how far the grain wanders (1 oak). Wear: dirt band strength.</summary>
        public readonly struct WoodRow
        {
            public readonly Color Base;
            public readonly float RingSpacing, Contrast, Knots, Grey, Pores, BoardScale, KnotSpacing, Figure, Wear;

            public WoodRow(Color baseColour, float ringSpacing, float contrast, float knots, float grey, float pores,
                float boardScale = 1f, float knotSpacing = 0.55f, float figure = 1f, float wear = 0.22f)
            {
                BoardScale = boardScale;
                KnotSpacing = knotSpacing;
                Figure = figure;
                Wear = wear;
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
            new WoodRow(new Color(0.46f, 0.33f, 0.20f), 0.007f, 1.25f, 0.9f, 0.0f, 0.7f, 0.8f, 0.38f, 0.6f, 0.18f),    // pine: pale, tight, knotty; narrow straight boards
            new WoodRow(new Color(0.52f, 0.47f, 0.40f), 0.014f, 0.7f, 0.2f, 0.85f, 1.4f, 1.2f, 0.8f, 1.6f, 0.35f),    // driftwood: washed, silver; wide twisted boards
            new WoodRow(new Color(0.27f, 0.18f, 0.12f), 0.009f, 0.6f, 0.3f, 0.0f, 0.8f, 1.1f, 0.7f, 0.8f, 0.12f),     // dark: walnut-brown, quiet; kept clean
            new WoodRow(new Color(0.48f, 0.42f, 0.34f), 0.012f, 0.8f, 0.4f, 0.5f, 1.2f, 1f, 0.55f, 1.2f, 0.34f),     // bleached: sun-greyed; worn
        };

        /// <summary>How one kind of stone breaks as a bare wall (W-166 #336). Roughness: lumps (0 smooth, 1 rock).
        /// Grain: lump size, above 1 smaller. Jaggedness: ridges, creases and overhangs up high. Blockiness: 0..1,
        /// blocky collapse hollows as a share of the jaggedness (bedded or jointed rock). Ribs, RibSpacing (m):
        /// scalloped ribs. Angularity, FacetSize (m): flat faces meeting in edges. Detail, DetailScale: the shader's
        /// fine relief strength and repeats per metre. Roughness, Jaggedness, Ribs, Angularity and FacetSize set how far
        /// the wall reaches out of its face, so changing them can move what is fitted round it (cave nodes); Grain,
        /// Blockiness, RibSpacing, Detail and DetailScale only change how the wall reads.</summary>
        public readonly struct StoneRow
        {
            public readonly float Roughness, Grain, Jaggedness, Blockiness, Ribs, RibSpacing, Angularity, FacetSize,
                Detail, DetailScale;

            public StoneRow(float roughness, float grain, float jaggedness, float blockiness, float ribs, float ribSpacing,
                float angularity, float facetSize, float detail, float detailScale)
            {
                Roughness = roughness;
                Grain = grain;
                Jaggedness = jaggedness;
                Blockiness = blockiness;
                Ribs = ribs;
                RibSpacing = ribSpacing;
                Angularity = angularity;
                FacetSize = facetSize;
                Detail = detail;
                DetailScale = detailScale;
            }
        }

        /// <summary>The stone kinds, by StoneKind. Columns: roughness, grain, jaggedness, blockiness, ribs, rib
        /// spacing, angularity, facet size, detail, detail scale.</summary>
        public static readonly StoneRow[] Stone =
        {
            new StoneRow(1.0f, 1.0f, 1.15f, 0.3f, 0f, 1.6f, 0f, 2.5f, 0.3f, 1.2f),     // plain: ordinary lumpy rock
            new StoneRow(1.3f, 0.6f, 1.4f, 0.95f, 0f, 1.6f, 0f, 2.5f, 0.45f, 2.2f),    // granite: big jointed blocks
            new StoneRow(1.0f, 1.0f, 1.05f, 0.3f, 0f, 1.6f, 0f, 2.5f, 0.3f, 1.2f),     // limestone: dissolved, rounded
            new StoneRow(0.7f, 0.45f, 0.75f, 1f, 0f, 1.6f, 0f, 2.5f, 0.25f, 1.4f),     // sandstone: bedded slabs
            new StoneRow(0.2f, 0.5f, 0.15f, 0f, 0f, 1.6f, 0f, 2.5f, 0.05f, 1f),        // basalt: smooth flow skin
            new StoneRow(0.5f, 0.6f, 0.8f, 0.8f, 0.8f, 2.2f, 0f, 2.5f, 0.9f, 0.9f),    // ice: melt-scalloped ribs
            new StoneRow(0.8f, 1.6f, 1f, 0.5f, 0f, 1.6f, 0.7f, 1.8f, 0.12f, 2f),       // crystal: faceted
            new StoneRow(0.3f, 1.2f, 0.35f, 0.1f, 0f, 1.6f, 1f, 2.2f, 0.08f, 1.8f),    // ore: hard faces, worked
            new StoneRow(0.6f, 1.0f, 0.35f, 0.2f, 0f, 1.6f, 0f, 2.5f, 0.3f, 1.2f),     // shale: soft, crumbly
            new StoneRow(1.2f, 1.5f, 0.85f, 0.2f, 0f, 1.6f, 0f, 2.5f, 0.4f, 1.8f),     // tuff: soft, pitted
        };

        /// <summary>One kind of creature covering (W-166 #336), read by Shaders/Materials/Coat.hlsl so a creature's coat
        /// and anything later made of it (a fur rug, a feathered cloak, a scale shield) look the same. Size: pattern
        /// size (1 = strands, feathers, scales, pores and plates as A-97 / A-123 drew them). Contrast: how strongly the
        /// pattern darkens (1 as drawn). Bump: relief (1 as drawn). Wrap: how far the diffuse wraps round. Rim: rim
        /// light. Spec, SpecPower: the highlight (feathers add a sheen on dark plumage on top). Sky: sky mirrored at a
        /// glancing angle (chitin's hard gloss).</summary>
        public readonly struct CoatRow
        {
            public readonly float Size, Contrast, Bump, Wrap, Rim, Spec, SpecPower, Sky;

            public CoatRow(float size, float contrast, float bump, float wrap, float rim, float spec, float specPower, float sky = 0f)
            {
                Size = size;
                Contrast = contrast;
                Bump = bump;
                Wrap = wrap;
                Rim = rim;
                Spec = spec;
                SpecPower = specPower;
                Sky = sky;
            }
        }

        /// <summary>The coverings, by Fauna.CoatKind. Columns: size, contrast, bump, wrap, rim, spec, spec power, sky.
        /// The values are Creature.shader's own, so moving it onto Coat.hlsl changes nothing on screen.</summary>
        public static readonly CoatRow[] Coat =
        {
            new CoatRow(1f, 1f, 1f, 0.25f, 0.25f, 0f, 8f),            // fur: soft strands, matte
            new CoatRow(1f, 1f, 1f, 0.35f, 0.3f, 0.02f, 8f),          // feathers: layered vanes, a faint sheen
            new CoatRow(1f, 1f, 1f, 0.2f, 0.15f, 0.35f, 30f),         // scales: grooved domes, a little gloss
            new CoatRow(1f, 1f, 1f, 0.4f, 0.12f, 0.25f, 24f),         // skin: pores and mottling, moist
            new CoatRow(1f, 1f, 1f, 0.15f, 0.1f, 0.6f, 40f, 0.25f),   // chitin: banded plates, hard gloss
        };

        /// <summary>The covering row for a creature's coat.</summary>
        public static CoatRow CoatFor(Punchy.Fauna.CoatKind kind) => Coat[Mathf.Clamp((int)kind, 0, Coat.Length - 1)];

        /// <summary>One kind of cloth (W-166 #336), read by Shaders/Materials/Cloth.hlsl. Size: pattern size (1 =
        /// threads, seams and blotches as W-99 / W-113 drew them). Contrast: how strongly the pattern shows (1 as
        /// drawn). Wrap: how far the diffuse wraps round. Sheen: light at a glancing angle (hide's soft shine).</summary>
        public readonly struct ClothRow
        {
            public readonly float Size, Contrast, Wrap, Sheen;

            public ClothRow(float size, float contrast, float wrap, float sheen)
            {
                Size = size;
                Contrast = contrast;
                Wrap = wrap;
                Sheen = sheen;
            }
        }

        /// <summary>The cloths, by ClothKind. Columns: size, contrast, wrap, sheen. The light columns are creature
        /// gear's own (Creature.shader), so nothing changes on screen.</summary>
        public static readonly ClothRow[] Cloth =
        {
            new ClothRow(1f, 1f, 0.25f, 0f),     // linen: matte weave, sewn panels
            new ClothRow(1f, 1f, 0.2f, 0.22f),   // hide: blotchy tanning, creases, a soft sheen
        };

        /// <summary>One kind of metal (W-166 #336), read by Shaders/Materials/Metal.hlsl. Diffuse: how much of a
        /// plain surface's diffuse it keeps. Sky: tinted sky reflection. Spec, SpecPower: the sun highlight.
        /// GlintCells: glint cells per metre. Glint: glint strength. GlintThreshold: cells whose hash is under it
        /// stay dark. GlintPower: how tight each glint is.</summary>
        public readonly struct MetalRow
        {
            public readonly float Diffuse, Sky, Spec, SpecPower, GlintCells, Glint, GlintThreshold, GlintPower;

            public MetalRow(float diffuse, float sky, float spec, float specPower, float glintCells, float glint,
                            float glintThreshold, float glintPower)
            {
                Diffuse = diffuse;
                Sky = sky;
                Spec = spec;
                SpecPower = specPower;
                GlintCells = glintCells;
                Glint = glint;
                GlintThreshold = glintThreshold;
                GlintPower = glintPower;
            }
        }

        /// <summary>The metals, by MetalKind. Columns: diffuse, sky, spec, spec power, glint cells, glint, glint
        /// threshold, glint power. The values are Metal.hlsl's own, so nothing changes on screen.</summary>
        public static readonly MetalRow[] Metal =
        {
            new MetalRow(0.45f, 0.35f, 3f, 90f, 7f, 14f, 0.55f, 400f),   // iron: sky-tinted, sharp sun, twinkling glints
        };

        /// <summary>The cloth row for a family: Leather is hide, anything else linen.</summary>
        public static ClothRow ClothFor(MaterialFamily family) => Cloth[family == MaterialFamily.Leather ? (int)ClothKind.Hide : (int)ClothKind.Linen];

        public const int MaxWoodKinds = 8;
        public const int MaxCoatKinds = 8;
        public const int MaxClothKinds = 8;
        public const int MaxMetalKinds = 8;
        static readonly int ClothKinds = Shader.PropertyToID("_PunchyClothKinds");
        static readonly int MetalLightKinds = Shader.PropertyToID("_PunchyMetalLightKinds");
        static readonly int MetalGlintKinds = Shader.PropertyToID("_PunchyMetalGlintKinds");
        static readonly int CoatShapeKinds = Shader.PropertyToID("_PunchyCoatShapeKinds");
        static readonly int CoatLightKinds = Shader.PropertyToID("_PunchyCoatLightKinds");
        static readonly int GrainKinds = Shader.PropertyToID("_PunchyWoodGrainKinds");
        static readonly int ToneKinds = Shader.PropertyToID("_PunchyWoodToneKinds");
        static readonly int FormKinds = Shader.PropertyToID("_PunchyWoodFormKinds");

        /// <summary>Pushes the tables to the shaders (Wood.hlsl's _PunchyWoodGrainKinds, _PunchyWoodToneKinds and
        /// _PunchyWoodFormKinds; Coat.hlsl's _PunchyCoatShapeKinds and _PunchyCoatLightKinds; Cloth.hlsl's
        /// _PunchyClothKinds; Metal.hlsl's _PunchyMetalLightKinds and _PunchyMetalGlintKinds).</summary>
        public static void Push()
        {
            var grain = new Vector4[MaxWoodKinds];
            var tone = new Vector4[MaxWoodKinds];
            var form = new Vector4[MaxWoodKinds];
            for (int i = 0; i < MaxWoodKinds; i++)
            {
                var row = Wood[Mathf.Min(i, Wood.Length - 1)];
                grain[i] = new Vector4(row.RingSpacing, row.Contrast, row.Knots, row.Grey);
                form[i] = new Vector4(row.BoardScale, row.KnotSpacing, row.Figure, row.Wear);
                // The base colour's hue at luminance 1, so tinting the late wood by it keeps its brightness.
                var lum = Mathf.Max(0.05f, row.Base.r * 0.299f + row.Base.g * 0.587f + row.Base.b * 0.114f);
                var hue = new Vector3(row.Base.r, row.Base.g, row.Base.b) / lum;
                hue = Vector3.Lerp(Vector3.one, hue, 0.35f);
                tone[i] = new Vector4(hue.x, hue.y, hue.z, row.Pores);
            }
            Shader.SetGlobalVectorArray(GrainKinds, grain);
            Shader.SetGlobalVectorArray(ToneKinds, tone);
            Shader.SetGlobalVectorArray(FormKinds, form);

            var shape = new Vector4[MaxCoatKinds];
            var light = new Vector4[MaxCoatKinds];
            for (int i = 0; i < MaxCoatKinds; i++)
            {
                var row = Coat[Mathf.Min(i, Coat.Length - 1)];
                shape[i] = new Vector4(row.Size, row.Contrast, row.Bump, row.Sky);
                light[i] = new Vector4(row.Wrap, row.Rim, row.Spec, row.SpecPower);
            }
            Shader.SetGlobalVectorArray(CoatShapeKinds, shape);
            Shader.SetGlobalVectorArray(CoatLightKinds, light);

            var cloth = new Vector4[MaxClothKinds];
            for (int i = 0; i < MaxClothKinds; i++)
            {
                var row = Cloth[Mathf.Min(i, Cloth.Length - 1)];
                cloth[i] = new Vector4(row.Size, row.Contrast, row.Wrap, row.Sheen);
            }
            Shader.SetGlobalVectorArray(ClothKinds, cloth);

            var metalLight = new Vector4[MaxMetalKinds];
            var metalGlint = new Vector4[MaxMetalKinds];
            for (int i = 0; i < MaxMetalKinds; i++)
            {
                var row = Metal[Mathf.Min(i, Metal.Length - 1)];
                metalLight[i] = new Vector4(row.Sky, row.Spec, row.SpecPower, row.Diffuse);
                metalGlint[i] = new Vector4(row.GlintCells, row.Glint, row.GlintThreshold, row.GlintPower);
            }
            Shader.SetGlobalVectorArray(MetalLightKinds, metalLight);
            Shader.SetGlobalVectorArray(MetalGlintKinds, metalGlint);
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
            KitMaterial.Trim => MaterialFamily.Paint,   // painted boards and accents, not plaster
            KitMaterial.Fish => MaterialFamily.None,   // #550: a fish's own body (FloraGenerator.HungFish), not a built family
            _ => MaterialFamily.Plaster   // Opening, Moss, Sand, Ash, Dirt, Flame: coloured plaster for now
        };

        /// <summary>The family a creature's gear segment is made of (Fauna.SegmentMaterial): Creature.shader draws each
        /// through that family's include, so gear wood is a town's wood and gear cloth its awnings. Body is None (a
        /// creature's own coat, not a material).</summary>
        public static MaterialFamily FamilyOf(Punchy.Fauna.SegmentMaterial material) => material switch
        {
            Punchy.Fauna.SegmentMaterial.Cloth => MaterialFamily.Cloth,
            Punchy.Fauna.SegmentMaterial.Leather => MaterialFamily.Leather,
            Punchy.Fauna.SegmentMaterial.Metal => MaterialFamily.Metal,
            Punchy.Fauna.SegmentMaterial.Wood => MaterialFamily.Wood,
            Punchy.Fauna.SegmentMaterial.Bone => MaterialFamily.Bone,
            _ => MaterialFamily.None
        };
    }
}
