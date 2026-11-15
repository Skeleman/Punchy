using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A town's notice board as the townsfolk use it (A-116, #316): where it stands, where readers stand before it, and
    /// the sheets the townsfolk pin up on it. The board itself (its model and quests) is Quests' in Items, which builds
    /// one per settlement beside the centre, its face to -z; Offset here must match Quests.BuildBoards. Seed-derived, so
    /// every machine has the same boards.
    /// <para>The pinned sheets come from the town, the day and the hour alone (the day count and the clock are shared
    /// world state, synced by WorldSync), so host and client show the same sheets with nothing new on the wire: each
    /// day a town gets one or two, each pinned at its own hour and left up two days. A poster walks one over in the
    /// hour before (the brain's Board step); the sheet goes up at its hour whether or not anyone came.</para>
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

        /// <summary>Spots for pinned sheets on the board's face (x along it, y up from its foot, m), around and over the
        /// edges of Quests' two fixed sheets. Posts take them in turn, so the six up at most never share one.</summary>
        public static float2 SlotAt(int slot) => slot switch
        {
            0 => new float2(-0.55f, 1.55f),
            1 => new float2(0.56f, 1.5f),
            2 => new float2(-0.02f, 1.62f),
            3 => new float2(-0.52f, 1.13f),
            4 => new float2(0.54f, 1.16f),
            _ => new float2(0.05f, 1.08f)
        };

        /// <summary>How many slots a board has.</summary>
        public const int SlotCount = 6;

        /// <summary>How long a sheet stays up (game hours).</summary>
        public const float KeptHours = 48f;

        /// <summary>The first and last hour a sheet can go up.</summary>
        const float FirstPin = 8f, PinSpan = 7f;

        static uint Hash(int town, int day, uint salt) =>
            SpawnTable.Scramble((uint)town * 0x9E3779B1u ^ SpawnTable.Scramble((uint)day * 0x85EBCA77u ^ salt));

        /// <summary>How many sheets this town pins up on this day (1 or 2).</summary>
        public static int Posts(int town, int day) => 1 + (int)(Hash(town, day, 0x5EE7u) & 1u);

        /// <summary>The hour sheet k of this town and day goes up.</summary>
        public static float PinHour(int town, int day, int k) =>
            FirstPin + PinSpan * (Hash(town, day, 0x9140u + (uint)k) >> 8) / 16777216f;

        /// <summary>The slot (see SlotAt) sheet k of this town and day goes in.</summary>
        public static int Slot(int town, int day, int k) => (int)(((uint)(town * 7 + day * 2 + k)) % (uint)SlotCount);

        /// <summary>A per-sheet number for its look (tint, tilt).</summary>
        public static uint Look(int town, int day, int k) => Hash(town, day, 0x1004u + (uint)k);

        /// <summary>
        /// The sheet up in this slot of this town's board now, if any (its day and index). Only the posts of the last
        /// three days can be up, and they take six slots in turn, so at most one fits.
        /// </summary>
        public static bool SheetIn(int town, int slot, int day, float hour, out int postDay, out int k)
        {
            for (postDay = day; postDay >= day - 2; postDay--)
                for (k = 0; k < Posts(town, postDay); k++)
                {
                    if (Slot(town, postDay, k) != slot)
                        continue;
                    var age = (day - postDay) * 24f + hour - PinHour(town, postDay, k);
                    if (age >= 0f && age < KeptHours)
                        return true;
                }
            postDay = k = -1;
            return false;
        }

        /// <summary>A sheet of this town's due to go up within the hour (its slot and hour): a poster's errand.</summary>
        public static bool Due(int town, int day, float hour, out int slot, out float pin)
        {
            for (int k = 0; k < Posts(town, day); k++)
            {
                pin = PinHour(town, day, k);
                if (pin > hour && pin - hour <= 1f)
                {
                    slot = Slot(town, day, k);
                    return true;
                }
            }
            slot = -1;
            pin = 0f;
            return false;
        }

        /// <summary>Where a poster stands to pin a sheet in this slot: close in before it.</summary>
        public static float2 PinSpot(float2 board, int slot) => board + new float2(SlotAt(slot).x * 0.8f, -0.62f);
    }
}
