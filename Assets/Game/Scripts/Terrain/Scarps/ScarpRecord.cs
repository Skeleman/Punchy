using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// A scarp as data (W-155, rule 22): the rim line where the ground steps down, how far it drops along that line,
    /// which side is down, what made it, the beds it is cut through, its age and its seed. Everything else (the cut
    /// in the ground, the skin mesh, later the walkable ledges) is derived from the record by <see cref="ScarpBake"/>.
    /// </summary>
    public struct ScarpRecord
    {
        public string Name;
        public ScarpKind Kind;
        /// <summary>The rim, in order along it; smoothed into a curve by the bake.</summary>
        public float2[] Rim;
        /// <summary>The drop wanted at each rim point (m). The bake tapers it to nothing at both ends and never cuts
        /// below water or below where the floor can meet the ground again.</summary>
        public float[] Drop;
        /// <summary>Roughly which way is down from the rim (level direction).</summary>
        public float2 Down;
        /// <summary>The beds, bottom to top; the stack repeats up the face.</summary>
        public ScarpLayer[] Layers;
        /// <summary>0 fresh and sharp, 1 old and rounded.</summary>
        public float Age;
        public uint Seed;
        /// <summary>Cut after settlement plots are levelled (a river notch's bank, W-153).</summary>
        public bool Late;
    }

    /// <summary>
    /// The scarps on the island. For now two test scarps placed by hand where the ground suits them (W-155 row 1):
    /// a canyon wall on the steep side of a dry ridge and a bank a river has cut. The generator (row 4) will write
    /// the same records from the island's own ridges and river courses.
    /// </summary>
    public static class ScarpRecords
    {
        static readonly float2 CanyonAt = new float2(-1544f, 1816f), CanyonDown = new float2(0.548f, -0.837f), CanyonAlong = new float2(0.837f, 0.548f);
        static readonly float2 BankAt = new float2(-880f, -1456f), BankFlow = new float2(0.65f, 0.76f), BankSide = new float2(0.76f, -0.65f);

        static float2 Canyon(float along, float down) => CanyonAt + CanyonAlong * along + CanyonDown * down;
        static float2 Bank(float along, float off) => BankAt + BankFlow * along + BankSide * off;

        public static readonly ScarpRecord[] All =
        {
            new ScarpRecord
            {
                Name = "ridge canyon wall", Kind = ScarpKind.Canyon, Seed = 15501u, Age = 0.45f, Down = CanyonDown,
                Rim = new[] { Canyon(-105f, 2f), Canyon(-55f, 5f), Canyon(0f, 1f), Canyon(52f, 4f), Canyon(105f, 2f) },
                Drop = new[] { 44f, 50f, 54f, 50f, 44f },
                Layers = new[]
                {
                    new ScarpLayer { Thickness = 5f, Hardness = 0.9f }, new ScarpLayer { Thickness = 2f, Hardness = 0.2f },
                    new ScarpLayer { Thickness = 7f, Hardness = 0.8f }, new ScarpLayer { Thickness = 1.5f, Hardness = 0.1f },
                    new ScarpLayer { Thickness = 4f, Hardness = 0.7f }, new ScarpLayer { Thickness = 3f, Hardness = 0.3f }
                }
            },
            new ScarpRecord
            {
                Name = "river cut bank", Kind = ScarpKind.RiverCut, Seed = 15502u, Age = 0.2f, Down = -BankSide,
                Rim = new[] { Bank(-52f, 13f), Bank(-30f, 12f), Bank(-10f, 13.5f), Bank(10f, 13f), Bank(32f, 12.5f) },
                Drop = new[] { 4f, 4.5f, 5f, 4.5f, 4f },
                Layers = new[]
                {
                    new ScarpLayer { Thickness = 0.8f, Hardness = 0.7f }, new ScarpLayer { Thickness = 0.5f, Hardness = 0.2f },
                    new ScarpLayer { Thickness = 1.2f, Hardness = 0.6f }, new ScarpLayer { Thickness = 0.4f, Hardness = 0.3f }
                }
            }
        };
    }
}
