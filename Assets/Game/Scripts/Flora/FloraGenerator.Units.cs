using UnityEngine;

namespace Punchy.Flora
{
    // FloraGenerator, the units (W-175, #415): placed things made of library parts (FloraGenerator.Parts), written as
    // data (UnitDef). The market stall and the smithy moved here from their coded builders in row 2, the tent and the fire pit
    // in row 3, with no change in look; docks follow. See docs/agents/parts-and-units.md.
    public static partial class FloraGenerator
    {
        /// <summary>
        /// A market stall facing +Z: four timber poles (the front two shorter), a counter across the front with goods laid
        /// out on it, and a striped awning sloping from the back down over the front, overhanging it.
        /// </summary>
        public static readonly UnitDef StallUnit = new UnitDef
        {
            Id = "stall",
            DepthOfWidth = 0.7f,
            Parts = new[]
            {
                StallPole(-0.5f, -0.5f), StallPole(-0.5f, 0.5f), StallPole(0.5f, -0.5f), StallPole(0.5f, 0.5f),
                new UnitPart { Part = "counter", At = new Vector3(0f, 0f, 0.5f), AtMetres = new Vector3(0f, 0.45f, -0.15f),
                               Span = new Vector3(0.95f, 0f, 0f), SpanMetres = new Vector3(0f, 0.9f, 0.45f) },
                new UnitPart { Part = "wares", At = new Vector3(-0.4f, 0f, 0.5f), AtMetres = new Vector3(0f, 0.98f, -0.15f),
                               To = new Vector3(0.4f, 0f, 0.5f), ToMetres = new Vector3(0f, 0.98f, -0.15f) },
                new UnitPart { Part = "awning", At = new Vector3(0f, 1f, -0.55f), AtMetres = new Vector3(0f, 0.05f, 0f),
                               To = new Vector3(0f, 0.82f, 0.5f), ToMetres = new Vector3(0f, -0.15f, 0.45f),
                               Span = new Vector3(1.1f, 0f, 0f), SpanMetres = new Vector3(0f, 0.3f, 0f) },
            },
        };

        // A stall pole at a corner (shares of width and depth): the front ones (z > 0) stop at 0.82 of the height.
        static UnitPart StallPole(float x, float z) => new UnitPart
        {
            Part = "pole", At = new Vector3(x, 0f, z), AtMetres = new Vector3(0f, -0.05f, 0f),
            To = new Vector3(x, z > 0f ? 0.82f : 1f, z), SpanMetres = new Vector3(0.12f, 0f, 0f),
        };

        /// <summary>
        /// A smithy: an open shed of four heavy corner posts under a pyramid roof; the forge at the back (a stone hearth
        /// with its coals, a brick chimney stack up through the roof) and an anvil out front.
        /// </summary>
        public static readonly UnitDef SmithyUnit = new UnitDef
        {
            Id = "smithy",
            DepthOfWidth = 0.4f,
            Parts = new[]
            {
                SmithyPost(-0.5f, -1f), SmithyPost(-0.5f, 1f), SmithyPost(0.5f, -1f), SmithyPost(0.5f, 1f),
                new UnitPart { Part = "pyramid roof", At = new Vector3(0f, 0.58f, 0f), To = new Vector3(0f, 1f, 0f), Span = new Vector3(1f, 0f, 2f) },
                new UnitPart { Part = "forge hearth", At = new Vector3(0f, 0f, -0.45f), Span = new Vector3(0.55f, 0f, 0.8f) },
                new UnitPart { Part = "chimney stack", At = new Vector3(0f, 0f, -0.7f), AtMetres = new Vector3(0f, 1.1f, 0f),
                               To = new Vector3(0f, 1f, -0.7f), ToMetres = new Vector3(0f, 0.8f, 0f), SpanMetres = new Vector3(0.7f, 0f, 0.6f) },
                new UnitPart { Part = "anvil", At = new Vector3(0.15f, 0f, 0.25f) },
            },
        };

        // A smithy corner post (shares of width and depth) up to the eaves at 0.58 of the height.
        static UnitPart SmithyPost(float x, float z) => new UnitPart
        {
            Part = "post", At = new Vector3(x, 0f, z), AtMetres = new Vector3(0f, -0.05f, 0f),
            To = new Vector3(x, 0.58f, z), SpanMetres = new Vector3(0.24f, 0.22f, 0f),
        };

        /// <summary>
        /// A hide tent (goblin and ogre camps): the shell, the frame's full size and setting the unit's bounds, and a door
        /// mat before its open +Z panel.
        /// </summary>
        public static readonly UnitDef TentUnit = new UnitDef
        {
            Id = "tent",
            Parts = new[]
            {
                new UnitPart { Part = "hide tent", Span = new Vector3(1f, 1f, 1f), Bounds = true },
                new UnitPart { Part = "door mat", At = new Vector3(0f, 0f, 0.5f), Span = new Vector3(0.35f, 0f, 0f) },
            },
        };

        /// <summary>
        /// A camp's fire pit, its frame as wide as it is tall and its height the fire's radius: a ring of stones round an
        /// ash bed (setting the unit's bounds), charred logs leaning over it and embers in the ash.
        /// </summary>
        public static readonly UnitDef FirePitUnit = new UnitDef
        {
            Id = "fire pit",
            WidthOfHeight = 1f,
            Parts = new[]
            {
                new UnitPart { Part = "fire ring", Span = new Vector3(2f, 0f, 2f), Bounds = true },
                new UnitPart { Part = "log tepee", Span = new Vector3(2f, 0f, 2f) },
                new UnitPart { Part = "embers", Span = new Vector3(2f, 0f, 2f) },
            },
        };

        /// <summary>Every unit (for the unit tests and, later, the Model Lab's list).</summary>
        public static UnitDef[] Units => new[] { StallUnit, SmithyUnit, TentUnit, FirePitUnit };
    }
}
