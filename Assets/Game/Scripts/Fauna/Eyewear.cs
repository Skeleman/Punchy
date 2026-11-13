namespace Punchy.Fauna
{
    /// <summary>
    /// What a player has over their eyes (A-103 row 5), and what it does to how they see. The values are the meaning of
    /// NetPlayerLook.Eyewear (one byte on the wire), kept here so the inventory can put the pair on later without a
    /// change to the ghost's layout. Glasses are for bat eyes: worn, the view is normal. Glow goggles are for every other
    /// body: worn, it sees as a bat does. Each machine draws the pair on the body (CreatureWorn bits); only the wearer's
    /// own view changes (BatVision).
    /// </summary>
    public static class Eyewear
    {
        public const byte None = 0, Glasses = 1, Goggles = 2;

        /// <summary>The pair this body puts on: glasses over bat eyes, glow goggles over any other.</summary>
        public static byte For(float bodyVision) => bodyVision > 0f ? Glasses : Goggles;

        /// <summary>The one vision value the view uses (0 normal, 1 a bat's): the body's own (CreatureSpecies.Vision),
        /// replaced by what's worn.</summary>
        public static float Vision(float bodyVision, byte worn) => worn == Glasses ? 0f : worn == Goggles ? 1f : bodyVision;

        /// <summary>Its CreatureWorn bits, for drawing it on the body.</summary>
        public static byte WornBits(byte worn) => worn == Glasses ? CreatureWorn.Glasses : worn == Goggles ? CreatureWorn.Goggles : (byte)0;
    }
}
