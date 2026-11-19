using Unity.Mathematics;

namespace Punchy.Items
{
    /// <summary>What a placement record is, for its stand-in id (WorldIds.Record). Never renumber: the number is hashed.</summary>
    public enum RecordKind : uint
    {
        Door = 1,
        /// <summary>A worker's post: the work anchor's spot (CreatureState.WorkSpot).</summary>
        WorkPost = 2,
        Landmark = 3,
    }

    /// <summary>Which kind of seeded world item a uid names, kept in two bits of the uid so any peer can tell (a spare
    /// stands on a rack on every machine). Never renumber.</summary>
    public enum SeedFamily
    {
        DoorStarter = 0,
        Spare = 1,
        Rod = 2,
    }

    /// <summary>
    /// P-35 r3 row 5 (DESIGN-items-save section 5, rule 22): ids for things in the world come from their placement record,
    /// not from bake order, so a world file that reorders records keeps every id.
    /// - A record's id: until the world file exists, a hash of its kind and baked position (to the centimetre), the same
    ///   on every machine and changed only when the thing itself moves.
    /// - A seeded world item is (record, k): SeededBit, its family in bits 28-29, then 28 bits of hash.
    /// - Room storage (a container) is (door record, k): StorageBit, then 29 bits of hash. Cave chests keep ChestId
    ///   (CaveChestBit, the same bit as SeededBit but another table), temple chests their socket index.
    /// - A moved or dropped item takes the host's counter (Items.nextDrop), from 1, below SeededBit. Saving the counter
    ///   with the world is the save row's (row 7).
    /// Clashes: n seeded items of one family clash with odds of about n^2 / 2^29 (2,000 doors: under 1%); the seeders
    /// log one and skip the later item, and WorldIdTests checks the mixing.
    /// </summary>
    public static class WorldIds
    {
        public const int SeededBit = 0x40000000, StorageBit = 0x20000000;
        const int FamilyShift = 28, SeedMask = 0x0FFFFFFF, StorageMask = 0x1FFFFFFF;

        /// <summary>The stand-in record id: kind and baked position, rounded to the centimetre, mixed.</summary>
        public static uint Record(RecordKind kind, float3 baked)
        {
            var q = (int3)math.round(baked * 100f);
            return Mix(math.hash(new uint4((uint)kind, (uint)q.x, (uint)q.y, (uint)q.z)));
        }

        /// <summary>The same for a record placed on the ground plan only (y taken as 0).</summary>
        public static uint Record(RecordKind kind, float2 baked) => Record(kind, new float3(baked.x, 0f, baked.y));

        /// <summary>Seeded world item k of a record.</summary>
        public static int Seeded(SeedFamily family, uint record, int k) =>
            SeededBit | (int)family << FamilyShift | (int)(Mix(record ^ ((uint)k + 1u) * 0x9E3779B9u) & SeedMask);

        /// <summary>Room store k of a door record (a container id).</summary>
        public static int Storage(uint doorRecord, int k) =>
            StorageBit | (int)(Mix(doorRecord ^ ((uint)k + 1u) * 0x85EBCA6Bu) & StorageMask);

        /// <summary>A seeded world item's uid (else a moved or dropped one, from the host's counter).</summary>
        public static bool IsSeeded(int uid) => (uid & SeededBit) != 0;

        public static bool IsSpare(int uid) => IsSeeded(uid) && (uid >> FamilyShift & 3) == (int)SeedFamily.Spare;

        /// <summary>Room storage's container id (not a chest's).</summary>
        public static bool IsStorage(int id) => (id & (SeededBit | StorageBit)) == StorageBit;

        /// <summary>A per-seed draw from a record (where a door's starter items lie, what they are): mixed, so nearby
        /// records don't draw alike (math.hash on a uint doesn't mix its bits).</summary>
        public static uint Draw(uint record, int k, uint salt) => Mix(record ^ ((uint)k + 1u) * 0xC2B2AE35u ^ salt);

        static uint Mix(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
