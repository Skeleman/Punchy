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
        /// in the material test instead of drawing as a flat-colour plank. Natural forms (trees, rocks, logs) are None
        /// too: they aren't built and keep their own bark.
        /// </summary>
        public static MaterialFamily FormFamily(FloraForm form) => form switch
        {
            FloraForm.StoneHouse or FloraForm.Shrine or FloraForm.Columns or FloraForm.Ruin or FloraForm.KitRuin or FloraForm.Well or
            FloraForm.Fountain or FloraForm.Forum or FloraForm.HazardShelter => MaterialFamily.Stone,
            FloraForm.Tent or FloraForm.Bedroll => MaterialFamily.Cloth,
            FloraForm.Lodge => MaterialFamily.Leather,
            FloraForm.Hut or FloraForm.Inn or FloraForm.Smithy or FloraForm.Hall or FloraForm.KitBuilding or
            FloraForm.Tower => MaterialFamily.Plaster,
            _ => MaterialFamily.None
        };

        /// <summary>
        /// The family a builder's timber takes: wood, except an elf's (W-161: elf towns build nothing of wood), which
        /// keeps its old plain look until W-161 gives it its own material.
        /// </summary>
        static MaterialFamily Timber(Grower g) => g.S.KitCulture == FolkCulture.Elf ? MaterialFamily.Plaster : MaterialFamily.Wood;

        /// <summary>The wood a species' timber is (MaterialLibrary.Wood): set by the form where it says, else its folk.</summary>
        public static WoodKind WoodKindFor(FloraSpecies species) => species.Form switch
        {
            FloraForm.Driftwood => WoodKind.Driftwood,
            FloraForm.MineFrame or FloraForm.Torch => WoodKind.Pine,
            FloraForm.Palisade => WoodKind.Bleached,
            _ => MaterialLibrary.WoodFor(species.KitCulture)
        };

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
                for (int f = 0; f < counts.Length && f <= (int)MaterialFamily.Paint; f++)
                    counts[f] = builder.BarkVerticesOf((MaterialFamily)f);
            return counts;
        }
    }
}
