using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Dungeon
{
    /// <summary>What a dungeon socket holds (#24): the spawner picks content of this kind for it.</summary>
    public enum SocketKind : byte { Creature, Chest, WallDecor, Interactable, Clutter }

    /// <summary>How much fits in a socket: it caps what the spawner may put there.</summary>
    public enum SocketSize : byte { Small, Medium, Large }

    /// <summary>
    /// What is round a socket, as bit flags in <see cref="DungeonSocket.Tags"/>: the spawner and the dungeon
    /// behaviours read them (drink at NearWater, guard at NearChest). New flags go at the end; never renumber.
    /// </summary>
    [System.Flags]
    public enum SocketTags : uint
    {
        None = 0,
        NearWater = 1u << 0,
        NearChest = 1u << 1,
        NearAltar = 1u << 2,
        Dark = 1u << 3,
        Ledge = 1u << 4,
        Doorway = 1u << 5,
        Ceiling = 1u << 6,
    }

    /// <summary>
    /// The shared contract between the dungeon set pieces (player lane, the only writer) and the spawner (animal lane,
    /// read only), from briefs/dungeons-two-phase.md (#24). One per pre-placed point in a dungeon, as a buffer on the
    /// dungeon's entity. The set pieces never pick content; the spawner never writes sockets. Seed comes from the world
    /// seed + dungeon id + socket index, so every client makes the same picks. Changing this struct needs a heads-up in
    /// COORDINATION.md first.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct DungeonSocket : IBufferElementData
    {
        /// <summary>World position and facing.</summary>
        public float3 Pos;
        public quaternion Rot;
        public SocketKind Kind;
        /// <summary>A <see cref="SocketSize"/>: small, medium or large.</summary>
        public byte Size;
        /// <summary><see cref="SocketTags"/> bit flags.</summary>
        public uint Tags;
        /// <summary>World seed + dungeon id + socket index (see <see cref="MakeSeed"/>).</summary>
        public uint Seed;
        public ushort DungeonType;
        public byte Biome;

        /// <summary>The socket's seed: the same on every machine for the same world, dungeon and socket.</summary>
        public static uint MakeSeed(uint worldSeed, uint dungeonId, uint socketIndex) =>
            math.max(1u, math.hash(new uint3(worldSeed, dungeonId, socketIndex)));
    }
}
