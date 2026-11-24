using System.Threading;
using Punchy.Flora;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// Job 115: the contact pass's placement cache. Each chunk build re-placed every shrub and tree cell round it
    /// (FloraPlacement.Place, ~34 µs a cell, ~700 cells a chunk), and as the camera walks the LOD rings move, so the same
    /// chunks are rebuilt at each LOD in turn and the same cells placed again: ~60 ms of worker time a frame on a walk, in
    /// bursts of single chunk jobs up to 700 ms that starve the frame's own jobs. The cache keeps each cell's plant (or
    /// none) between batches. A build reads it (read-only) and queues the cells it had to place; a merge job adds them
    /// once the build is done, before the next batch. Placement is seed-derived, so a kept cell is the plant Place would
    /// give, as long as what it read is unchanged: the flora tables (GroundContacts.Version), the world (TerrainSource,
    /// whose Edits carry digging), each starting the cache afresh, and the colossi's keep-out circles (the cells round
    /// one that moved are dropped). "-oldcontacts" turns it off; "-contactcheck" places every cached cell again and
    /// counts any that differ. Cosmetic and seed-derived: each peer keeps its own.
    /// </summary>
    public static partial class GroundContacts
    {
        /// <summary>A/B: every chunk places its cells, as before (-oldcontacts).</summary>
        public static bool CacheOff;
        /// <summary>Testing: cached cells are placed again and compared (-contactcheck).</summary>
        public static bool CacheCheck;
        /// <summary>Past this many cells the cache starts afresh (~50 bytes each).</summary>
        const int CacheCap = 300000;

        /// <summary>A cell's plant as the contact pass needs it; Model -1: none, or nothing on the ground.</summary>
        public struct CachedPlant
        {
            public float3 Position;
            public float Yaw, Scale;
            public int Model;
        }

        public struct CachedCell
        {
            public int3 Key;
            public CachedPlant Plant;
        }

        /// <summary>What a chunk build reads and writes (default: no cache).</summary>
        public struct CacheView
        {
            [ReadOnly] public UnsafeParallelHashMap<int3, CachedPlant> Cells;
            public UnsafeQueue<CachedCell>.ParallelWriter Misses;
            public bool On, Check;
        }

        struct CheckKey { }
        struct CheckStatsKey { }
        /// <summary>-contactcheck: x cached cells that differed, y cached cells used, z placed.</summary>
        static readonly SharedStatic<int3> CheckStats = SharedStatic<int3>.GetOrCreate<CheckKey, CheckStatsKey>();

        static UnsafeParallelHashMap<int3, CachedPlant> cacheCells;
        static UnsafeQueue<CachedCell> cacheMisses;
        static JobHandle cacheMerge;
        static int cacheVersion = -1;
        static TerrainSource cacheSource;
        static FixedList512Bytes<float4> cacheKeepOut;
        static int cacheBatches;

        /// <summary>
        /// Before a chunk batch is scheduled (main thread, no build running): the cache view for its jobs, and the job
        /// dropping the cells round any keep-out circle that moved, which the batch must depend on.
        /// </summary>
        public static CacheView CacheBegin(in TerrainSource source, out JobHandle ready)
        {
            ready = default;
            cacheMerge.Complete();
            cacheMerge = default;
            if (CacheOff || !Flora.IsCreated || !Contacts.IsCreated)
                return default;
            if (!cacheCells.IsCreated)
            {
                cacheCells = new UnsafeParallelHashMap<int3, CachedPlant>(16384, Allocator.Persistent);
                cacheMisses = new UnsafeQueue<CachedCell>(Allocator.Persistent);
            }
            var keepOut = FloraPlacement.ColossusKeepOut.Data;
            var moved = new FixedList512Bytes<float4>();
            var fresh = cacheVersion != Version || !source.Equals(cacheSource) || cacheCells.Count() > CacheCap;
            if (!fresh)
                for (int i = 0; i < math.max(keepOut.Length, cacheKeepOut.Length); i++)
                {
                    var was = i < cacheKeepOut.Length ? cacheKeepOut[i] : default;
                    var now = i < keepOut.Length ? keepOut[i] : default;
                    if (was.Equals(now))
                        continue;
                    if (moved.Length + 2 > moved.Capacity)
                    {
                        fresh = true;
                        break;
                    }
                    moved.Add(was);
                    moved.Add(now);
                }
            if (fresh)
                cacheCells.Clear();
            else if (moved.Length > 0)
                ready = new DropJob { Cells = cacheCells, Moved = moved, CellSizes = new float2(Flora.Value.CellSizes[1], Flora.Value.CellSizes[2]) }.Schedule();
            cacheVersion = Version;
            cacheSource = source;
            cacheKeepOut = keepOut;
            if (++cacheBatches % 50 == 0)
                NoteCheck();
            return new CacheView { Cells = cacheCells, Misses = cacheMisses.AsParallelWriter(), On = true, Check = CacheCheck };
        }

        /// <summary>After the batch is scheduled: adds the cells it placed to the cache once it is done.</summary>
        public static void CacheEnd(in CacheView view, JobHandle build)
        {
            if (!view.On)
                return;
            cacheMerge = new MergeJob { Cells = cacheCells, Misses = cacheMisses }.Schedule(build);
        }

        /// <summary>Frees the cache (the flora withdrawn, or the terrain system gone).</summary>
        public static void CacheDispose()
        {
            cacheMerge.Complete();
            cacheMerge = default;
            if (cacheCells.IsCreated)
                cacheCells.Dispose();
            if (cacheMisses.IsCreated)
                cacheMisses.Dispose();
            cacheVersion = -1;
        }

        static void NoteCheck()
        {
            if (!CacheCheck)
                return;
            var s = CheckStats.Data;
            UnityEngine.Debug.Log($"GroundContacts: contactcheck batch {cacheBatches}: {s.x} cells differ, {s.y} cached cells used, {s.z} placed, {cacheCells.Count()} kept");
        }

        /// <summary>The plant in a cell for the contact pass: from the cache when it holds it, else placed (and queued).</summary>
        static CachedPlant PlaceCached(ref FloraBlob f, ref Table t, in TerrainSource source, int2 cell, int layer, in CacheView cache)
        {
            var key = new int3(cell, layer);
            if (cache.On && cache.Cells.TryGetValue(key, out var kept))
            {
                if (cache.Check)
                {
                    var again = PlaceFor(ref f, ref t, source, cell, layer);
                    if (again.Model != kept.Model || !again.Position.Equals(kept.Position) || again.Yaw != kept.Yaw || again.Scale != kept.Scale)
                        Interlocked.Increment(ref CheckStats.Data.x);
                    Interlocked.Increment(ref CheckStats.Data.y);
                }
                return kept;
            }
            var plant = PlaceFor(ref f, ref t, source, cell, layer);
            if (cache.On)
            {
                cache.Misses.Enqueue(new CachedCell { Key = key, Plant = plant });
                if (cache.Check)
                    Interlocked.Increment(ref CheckStats.Data.z);
            }
            return plant;
        }

        static CachedPlant PlaceFor(ref FloraBlob f, ref Table t, in TerrainSource source, int2 cell, int layer)
        {
            var plant = FloraPlacement.Place(ref f, source, cell, layer, footprint: true);
            // (Floating plants: no ground.)
            if (plant.Model < 0 || plant.Model >= t.Models.Length || plant.Snow < 0f)
                return new CachedPlant { Model = -1 };
            return new CachedPlant { Position = plant.Position, Yaw = plant.Yaw, Scale = plant.Scale, Model = plant.Model };
        }

        /// <summary>Adds a finished batch's placed cells to the cache.</summary>
        [BurstCompile]
        struct MergeJob : IJob
        {
            public UnsafeParallelHashMap<int3, CachedPlant> Cells;
            public UnsafeQueue<CachedCell> Misses;

            public void Execute()
            {
                while (Misses.TryDequeue(out var c))
                    Cells.TryAdd(c.Key, c.Plant);
            }
        }

        /// <summary>Drops the cached cells (shrub and tree layers) that could hold a spot inside a keep-out circle that moved.</summary>
        [BurstCompile]
        struct DropJob : IJob
        {
            public UnsafeParallelHashMap<int3, CachedPlant> Cells;
            public FixedList512Bytes<float4> Moved;
            /// <summary>The shrub and tree layers' cell sizes.</summary>
            public float2 CellSizes;

            public void Execute()
            {
                for (int i = 0; i < Moved.Length; i++)
                {
                    var c = Moved[i];
                    if (c.z <= 0f)
                        continue;
                    for (int layer = 1; layer <= 2; layer++)
                    {
                        var size = CellSizes[layer - 1];
                        // (Place keeps out a cell whose spot, anywhere in it, falls inside: every cell the circle touches.)
                        var lo = (int2)math.floor((c.xy - c.z) / size) - 1;
                        var hi = (int2)math.floor((c.xy + c.z) / size) + 1;
                        for (int y = lo.y; y <= hi.y; y++)
                        for (int x = lo.x; x <= hi.x; x++)
                            Cells.Remove(new int3(x, y, layer));
                    }
                }
            }
        }
    }
}
