using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>One bat folk settlement (#35): a chamber deep in an existing cave, and the pool in it.</summary>
    public struct BatFolkSite
    {
        /// <summary>Index into CaveSetBlob.Caves, and that cave's landmark Key.</summary>
        public int Cave;
        public uint Key;
        /// <summary>The settlement's chamber node (index into CaveSetBlob.Nodes), its centre and radius (with the cave's width).</summary>
        public int Chamber;
        public float3 Centre;
        public float Radius;
        /// <summary>The chamber's water pool (index into CaveSetBlob.Pools), drawn as white pool water.</summary>
        public int Pool;
    }

    /// <summary>
    /// Bat folk settlements, kept apart from the caves: nothing else reads this, so landmark, settlement and cave
    /// output (and their hashes) are the same with or without it.
    /// </summary>
    public struct BatFolkSetBlob
    {
        public BlobArray<BatFolkSite> Sites;
        /// <summary>Per cave pool (same order as CaveSetBlob.Pools): 1 = white pool water.</summary>
        public BlobArray<byte> WhitePools;
        /// <summary>How many caves qualified (for the log).</summary>
        public int Qualifying;
        public float BiggestChamber;
    }

    /// <summary>
    /// Picks bat folk caves among the baked caves (BATFOLK-PLAN step 1). Runs after CaveBaking and only reads the
    /// caves. A cave qualifies with a chamber of radius >= <see cref="MinChamber"/> at least <see cref="MinDepth"/>
    /// nodes in from the mouth (out of daylight) and a water pool, in that chamber if it has one, else the nearest in
    /// the cave (that pool is the one turned white). Up to one in <see cref="PerCaves"/> qualifying
    /// caves (at least one if any qualify) is picked, lowest first by a scrambled hash of its mouth cell and its own
    /// seed stream, so neither list order nor any other cave's changes reshuffle the picks.
    /// </summary>
    public static class BatFolkBaking
    {
        public const float MinChamber = 9f;
        public const int MinDepth = 3;
        public const int PerCaves = 6;
        const float MouthCell = 16f;

        /// <summary>The stage's own stream: the world seed with "batfolk" mixed in.</summary>
        static uint StreamSeed(uint seed) => BiomeSampling.Scramble(seed ^ 0xBA7F01C5u);

        public static string LastLog = "";

        public static BlobAssetReference<BatFolkSetBlob> Build(in TerrainSource source, Allocator allocator)
        {
            if (!source.Caves.IsCreated)
            {
                LastLog = "[batfolk] caves 0, picked 0 (no caves)";
                return default;
            }
            ref var set = ref source.Caves.Value;
            var stream = StreamSeed(source.World.Seed);
            // (cave, chamber node, pool, rank)
            var qualifying = new List<(int cave, int node, int pool, uint rank)>();
            var biggest = 0f;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                ref var cave = ref set.Caves[c];
                int bestNode = -1, bestPool = -1;
                var bestR = 0f;
                var bestOwn = false;
                for (int n = cave.FirstNode + MinDepth; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var r = set.Nodes[n].Radius * cave.Profile.x;
                    biggest = math.max(biggest, r);
                    if (r < MinChamber)
                        continue;
                    // A chamber holding its own pool wins over a bigger one without; otherwise the biggest.
                    var pool = PoolIn(ref set, cave, set.Nodes[n].Position, r);
                    var own = pool >= 0;
                    if (own == bestOwn ? r <= bestR : !own)
                        continue;
                    if (!own)
                        pool = PoolIn(ref set, cave, set.Nodes[n].Position, 1e6f);
                    if (pool < 0)
                        continue;
                    bestR = r; bestNode = n; bestPool = pool; bestOwn = own;
                }
                if (bestNode < 0)
                    continue;
                var cell = (int2)math.floor(cave.Mouth.xz / MouthCell);
                var rank = BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble((uint)cell.x * 0x85EBCA6Bu ^ (uint)cell.y * 0xC2B2AE35u));
                qualifying.Add((c, bestNode, bestPool, rank));
            }
            var picks = qualifying.Count == 0 ? 0 : math.max(1, qualifying.Count / PerCaves);
            var chosen = new List<(int cave, int node, int pool, uint rank)>(qualifying);
            chosen.Sort((a, b) => a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.cave.CompareTo(b.cave));
            chosen.RemoveRange(picks, chosen.Count - picks);
            chosen.Sort((a, b) => a.cave.CompareTo(b.cave));

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BatFolkSetBlob>();
            var sites = builder.Allocate(ref root.Sites, chosen.Count);
            root.Qualifying = qualifying.Count;
            root.BiggestChamber = biggest;
            var white = builder.Allocate(ref root.WhitePools, set.Pools.Length);
            for (int i = 0; i < set.Pools.Length; i++)
                white[i] = 0;
            for (int i = 0; i < chosen.Count; i++)
            {
                var (c, n, p, _) = chosen[i];
                sites[i] = new BatFolkSite
                {
                    Cave = c, Key = set.Caves[c].Key, Chamber = n, Centre = set.Nodes[n].Position,
                    Radius = set.Nodes[n].Radius * set.Caves[c].Profile.x, Pool = p
                };
                white[p] = 1;
            }
            var blob = builder.CreateBlobAssetReference<BatFolkSetBlob>(allocator);
            builder.Dispose();
            LastLog = Report(ref blob.Value, set.Caves.Length);
            return blob;
        }

        /// <summary>
        /// The cave's nearest water pool whose centre lies inside a chamber's radius, or -1. With a huge radius, the
        /// nearest water pool anywhere in the cave (the plan allows the pool of the next chamber along).
        /// </summary>
        static int PoolIn(ref CaveSetBlob set, in CaveData cave, float3 centre, float radius)
        {
            int best = -1;
            var bestD = radius;
            for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
            {
                ref var pool = ref set.Pools[i];
                if (pool.Lava > 0.5f)
                    continue;
                var d = math.distance(pool.Centre.xz, centre.xz);
                if (d <= bestD && (radius > 1e5f || math.abs(pool.Centre.y - centre.y) < radius * 2f))
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        static string Report(ref BatFolkSetBlob b, int caves)
        {
            var sb = new StringBuilder($"[batfolk] caves {b.Qualifying}, picked {b.Sites.Length} (of {caves} caves; biggest deep chamber {b.BiggestChamber:F1} m)");
            for (int i = 0; i < b.Sites.Length; i++)
            {
                ref var s = ref b.Sites[i];
                sb.Append($"; cave {s.Cave} chamber {s.Centre.x:F0},{s.Centre.y:F0},{s.Centre.z:F0} r {s.Radius:F1} pool {s.Pool}");
            }
            return sb.ToString();
        }

        /// <summary>The log line for a baked world (island v1 bakes in the editor, so the player logs it from the blob).</summary>
        public static string Report(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated)
                return "[batfolk] none baked";
            return Report(ref source.BatFolk.Value, source.Caves.IsCreated ? source.Caves.Value.Caves.Length : 0);
        }
    }
}
