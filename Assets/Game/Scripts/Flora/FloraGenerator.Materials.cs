using Punchy.Terrain;

namespace Punchy.Flora
{
    // FloraGenerator, the W-166 material hookup: which family a built form's untagged bark is, which wood a species
    // builds with, and a count of a model's bark by family for the material test.
    public static partial class FloraGenerator
    {
        /// <summary>
        /// The family a form's bark is unless its builder tags a piece (FloraMeshBuilder.Family / Wood). Timber forms
        /// return None on purpose: every piece of a fence, frame or stall must say it's wood, so a missed flag shows up
        /// in the material test instead of drawing as a flat-colour plank. Trees and rocks are None too and keep their
        /// own bark; lying logs are wood, so their grain runs along each piece and not round it as rock.
        /// </summary>
        public static MaterialFamily FormFamily(FloraForm form) => form switch
        {
            FloraForm.StoneHouse or FloraForm.Shrine or FloraForm.Columns or FloraForm.Ruin or FloraForm.KitRuin or FloraForm.Well or
            FloraForm.Fountain or FloraForm.Forum or FloraForm.HazardShelter => MaterialFamily.Stone,
            FloraForm.Tent or FloraForm.Bedroll => MaterialFamily.Cloth,
            FloraForm.Lodge => MaterialFamily.Leather,
            FloraForm.Log or FloraForm.Driftwood => MaterialFamily.Wood,
            FloraForm.Hut or FloraForm.Inn or FloraForm.Smithy or FloraForm.Hall or FloraForm.KitBuilding or
            FloraForm.Tower => MaterialFamily.Plaster,
            _ => MaterialFamily.None
        };

        /// <summary>The family a builder's timber takes: wood, except an elf's stone (W-161: elf towns build nothing of wood).</summary>
        static MaterialFamily Timber(Grower g) => g.B.TimberFamily;

        /// <summary>The wood a species' timber is (MaterialLibrary.Wood): set by the form where it says, else its folk.</summary>
        public static WoodKind WoodKindFor(FloraSpecies species) => species.TrunkOf != null
            ? species.TrunkOf.Form == FloraForm.Conifer ? WoodKind.Pine : WoodKind.Oak
            : species.Form switch
        {
            FloraForm.Driftwood => WoodKind.Driftwood,
            FloraForm.MineFrame or FloraForm.Torch => WoodKind.Pine,
            FloraForm.Palisade => WoodKind.Bleached,
            _ => MaterialLibrary.WoodFor(species.KitCulture)
        };

        // The biome isn't known when a species is grown, so each variant stands for one (temperate twice as often).
        static void SurfaceKinds(FloraMeshBuilder b, FolkCulture culture, int seed)
        {
            var biome = ((((uint)seed * 0x9E3779B1u) >> 16) % 5u) switch
            {
                0u => BiomeFamily.Arid,
                1u => BiomeFamily.Cold,
                2u => BiomeFamily.Wet,
                _ => BiomeFamily.Temperate
            };
            b.ThatchKind = MaterialLibrary.ThatchFor(culture, biome);
            b.PlasterKind = MaterialLibrary.PlasterFor(culture, biome);
            b.RoofKind = MaterialLibrary.RoofFor(culture, biome);
        }

        /// <summary>
        /// W-166's material test: grows a species' nearest LOD and counts its bark vertices per MaterialFamily (index by
        /// the enum). Editor and test use only: it builds the whole model on the calling thread.
        /// </summary>
        public static int[] BarkFamilies(FloraSpecies species, int variant)
        {
            var model = GrowParts(species, variant, species.name);
            var counts = new int[16];
            var builder = model.Pending != null && model.Pending.Length > 0 ? model.Pending[0] : null;
            if (builder != null)
                for (int f = 0; f < counts.Length && f <= (int)MaterialFamily.Tile; f++)
                    counts[f] = builder.BarkVerticesOf((MaterialFamily)f);
            return counts;
        }
    }
}
