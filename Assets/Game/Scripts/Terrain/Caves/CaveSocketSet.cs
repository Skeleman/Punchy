using System.Collections.Generic;
using Punchy.Dungeon;
using Unity.Collections;
using Unity.Entities;

namespace Punchy.Terrain
{
    /// <summary>One cave's run of sockets in <see cref="CaveSocketSetBlob.Sockets"/>, with what its log line reports.</summary>
    public struct CaveSocketRange
    {
        public int First, Count;
        public int Chambers, DeadEnds;
        /// <summary>TempleSockets.Hash of the cave's sockets.</summary>
        public uint Hash;
        /// <summary>1 for a road tunnel (no dungeon, no sockets).</summary>
        public byte Tunnel;
    }

    /// <summary>
    /// W-168 row 2: every cave's dungeon sockets (<see cref="CaveSockets.Build"/>), built once at the end of the island
    /// bake and kept in the island file, so a launch that loads the file skips building them (~1.6 s on the main thread).
    /// One range per cave, in cave order. The sockets are plain C# from the cave blob and the seed, so a saved set equals
    /// a fresh one on any machine; CaveSocketSystem builds them itself when the set is missing (v1, an older file).
    /// </summary>
    public struct CaveSocketSetBlob
    {
        public BlobArray<CaveSocketRange> Caves;
        public BlobArray<DungeonSocket> Sockets;

        /// <summary>Builds the set from the source's baked caves (Persistent); default when the source has none.</summary>
        public static BlobAssetReference<CaveSocketSetBlob> Build(TerrainSource source, Allocator allocator)
        {
            if (!source.Caves.IsCreated)
                return default;
            ref var set = ref source.Caves.Value;
            var ranges = new CaveSocketRange[set.Caves.Length];
            var all = new List<DungeonSocket>();
            for (int cave = 0; cave < ranges.Length; cave++)
            {
                // A road tunnel is a passage, not a dungeon: nothing waits in it.
                if (set.Caves[cave].Tunnel != 0)
                {
                    ranges[cave] = new CaveSocketRange { First = all.Count, Tunnel = 1 };
                    continue;
                }
                var sockets = CaveSockets.Build(source, cave, source.World.Seed, out var chambers, out var deadEnds);
                ranges[cave] = new CaveSocketRange { First = all.Count, Count = sockets.Count, Chambers = chambers, DeadEnds = deadEnds, Hash = TempleSockets.Hash(sockets) };
                all.AddRange(sockets);
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CaveSocketSetBlob>();
            var outRanges = builder.Allocate(ref root.Caves, ranges.Length);
            for (int i = 0; i < ranges.Length; i++) outRanges[i] = ranges[i];
            var outSockets = builder.Allocate(ref root.Sockets, all.Count);
            for (int i = 0; i < all.Count; i++) outSockets[i] = all[i];
            var blob = builder.CreateBlobAssetReference<CaveSocketSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }
    }
}
