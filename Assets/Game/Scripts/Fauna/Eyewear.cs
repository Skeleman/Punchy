namespace Punchy.Fauna
{
    /// <summary>
    /// What's over the eyes (A-103 row 5) and what it does to how the wearer sees. Eyewear is a head item (P-38 row 3,
    /// rule 33): worn in the Head slot like any hat, synced with the rest of the gear, drawn by the shared head pass.
    /// Glasses are for bat eyes: worn, the view is normal (so are a bat folk's smoked goggles). Glow goggles are for
    /// every other body: worn, it sees as a bat does. Only the wearer's own view changes (BatVision).
    /// </summary>
    public static class Eyewear
    {
        /// <summary>The pair (an item id) this body puts on: glasses over bat eyes, glow goggles over any other.</summary>
        public static byte For(float bodyVision) => bodyVision > 0f ? Punchy.Items.ItemTable.Glasses : Punchy.Items.ItemTable.GlowGoggles;

        /// <summary>The one vision value the view uses (0 normal, 1 a bat's): the body's own (CreatureSpecies.Vision),
        /// replaced by the head item worn.</summary>
        public static float Vision(float bodyVision, byte headItem) =>
            headItem == Punchy.Items.ItemTable.Glasses || headItem == Punchy.Items.ItemTable.Goggles ? 0f
            : headItem == Punchy.Items.ItemTable.GlowGoggles ? 1f : bodyVision;
    }
}
