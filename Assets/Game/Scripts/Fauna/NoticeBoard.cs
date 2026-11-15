using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A town's notice board as the townsfolk use it (A-116, #316): where it stands, and where readers stand before it.
    /// The board itself (its model and quests) is Quests' in Items, which builds one per settlement beside the centre,
    /// its face to -z; Offset here must match Quests.BuildBoards. Seed-derived, so every machine has the same boards.
    /// </summary>
    public static class NoticeBoard
    {
        /// <summary>The board's foot from its town's centre (m), as Quests.BuildBoards places it.</summary>
        public const float OffsetX = 4f, OffsetZ = 1.5f;

        /// <summary>How many can stand reading at once: the rest go about their day, so a crowd never blocks it.</summary>
        public const int Places = 3;

        /// <summary>The board of the town whose centre this is (xz).</summary>
        public static float2 At(float2 centre) => centre + new float2(OffsetX, OffsetZ);

        /// <summary>Reader place k (0 to Places-1): a step back from the board's face, side by side along it.</summary>
        public static float2 Place(float2 board, int k) => board + new float2((k - 1) * 0.7f, -1.05f);
    }
}
