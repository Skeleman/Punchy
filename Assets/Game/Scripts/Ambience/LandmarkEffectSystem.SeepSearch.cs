using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;

namespace Punchy.Ambience
{
    /// <summary>
    /// The seep search (W-142) on a worker thread: hashing up to a thousand world cells and probing cave walls takes
    /// thousands of ground samples, which on the main thread stalled every frame the camera crossed a seep cell for
    /// 80 ms or more (#712). The spots found replace the old ones when the job is done, a frame or two later.
    /// </summary>
    public partial class LandmarkEffectSystem
    {
        NativeArray<bool> lavaStyle, seepVents;
        NativeArray<float> seepWet;
        NativeList<SeepSpot> seepFound;
        JobHandle seepJob;
        bool seepSearching;
        float3 seepEye;

        [BurstCompile]
        struct SeepSearchJob : IJob
        {
            [ReadOnly] public TerrainSource Source;
            [ReadOnly] public NativeArray<bool> LavaStyle, Vents;
            [ReadOnly] public NativeArray<float> Wet;
            public float3 Eye;
            public float Range;
            public int2 Cell;
            public NativeList<SeepSpot> Found;

            public void Execute()
            {
                Found.Clear();
                var at = Eye.xz;
                var n = (int)math.ceil(Range / SeepCell);
                for (int z = -n; z <= n; z++)
                for (int x = -n; x <= n; x++)
                {
                    var c = Cell + new int2(x, z);
                    var h = Scramble(math.hash(new int3(c, 0x5EE9)));
                    if ((h & 0xFFFF) / 65536f >= SeepShare)
                        continue;
                    var r = Scramble(h ^ 0x9E3779B9u);
                    var xz = ((float2)c + new float2((r & 0xFFFF) / 65536f, (r >> 16) / 65536f)) * SeepCell;
                    if (math.distancesq(xz, at) > Range * Range)
                        continue;
                    if (SurfaceSeep(xz, Scramble(r), out var spot))
                        Found.Add(spot);
                }
                if (Source.Caves.IsCreated)
                    CaveSeeps();
            }

            bool SurfaceSeep(float2 xz, uint h, out SeepSpot spot)
            {
                spot = default;
                var ground = Source.SampleGround(xz);
                var lava = Weighs(ground.Biomes, Vents) > 0.6f;
                if (ground.HasWater && ground.WaterSurface > ground.Height - 0.5f)
                    return false;
                const float e = 0.75f;
                var grad = new float2(Source.SampleHeight(xz + new float2(e, 0f)) - Source.SampleHeight(xz - new float2(e, 0f)),
                                      Source.SampleHeight(xz + new float2(0f, e)) - Source.SampleHeight(xz - new float2(0f, e))) / (2f * e);
                var slope = math.length(grad);
                if (slope < SeepSteep)
                    return false;
                var down = -grad / slope;
                var drop = 1.2f + 2.3f * ((h & 0xFF) / 255f);
                var foot = xz + down * (drop / slope);
                // A face that bends away under the lip would leave the beads in the air.
                if (math.abs(Source.SampleHeight(foot) - (ground.Height - drop)) > 0.3f * drop)
                {
                    drop *= 0.5f;
                    foot = xz + down * (drop / slope);
                    if (math.abs(Source.SampleHeight(foot) - (ground.Height - drop)) > 0.3f * drop)
                        return false;
                }
                if (!lava && !WaterSeepSite(ground, foot, down, h))
                    return false;
                var lift = 0.08f * math.sqrt(1f + slope * slope);
                spot = new SeepSpot
                {
                    Lip = new float3(xz.x, ground.Height + lift, xz.y),
                    Run = down / slope,
                    Drop = drop,
                    Width = (0.3f + 0.5f * (((h >> 8) & 0xFF) / 255f)) * (lava ? 1f : 0.5f),
                    Kind = lava ? SeepKind.Lava : SeepKind.Water,
                    Hash = h
                };
                return true;
            }

            bool WaterSeepSite(in GroundSample ground, float2 foot, float2 down, uint h)
            {
                var wet = Weighs(ground.Biomes, Wet);
                if (wet <= 0.05f)
                    return false;
                var roll = ((h >> 16) & 0xFF) / 255f;
                for (var reach = 1f; reach <= 7f; reach += 3f)
                {
                    var below = Source.SampleGround(foot + down * reach);
                    if (below.HasWater && below.WaterSurface > below.Height + 0.05f)
                        return roll < WaterSeepBankShare;
                }
                return roll < WaterSeepMossShare * math.saturate((wet - 0.45f) / 0.35f);
            }

            static float Weighs(in BiomeWeights biomes, NativeArray<bool> table)
            {
                var w = 0f;
                for (int k = 0; k < 4; k++)
                    if (biomes.Weights[k] > 0f && biomes.Ids[k] >= 0 && biomes.Ids[k] < table.Length && table[biomes.Ids[k]])
                        w += biomes.Weights[k];
                return w;
            }

            static float Weighs(in BiomeWeights biomes, NativeArray<float> table)
            {
                var w = 0f;
                for (int k = 0; k < 4; k++)
                    if (biomes.Weights[k] > 0f && biomes.Ids[k] >= 0 && biomes.Ids[k] < table.Length)
                        w += biomes.Weights[k] * table[biomes.Ids[k]];
                return w;
            }

            void CaveSeeps()
            {
                ref var set = ref Source.Caves.Value;
                for (int c = 0; c < set.Caves.Length; c++)
                {
                    ref var cave = ref set.Caves[c];
                    if (cave.Style <= 0 || cave.Style > LavaStyle.Length)
                        continue;
                    var lava = LavaStyle[cave.Style - 1];
                    var kind = lava ? SeepKind.Lava : SeepKind.Water;
                    if (math.any(Eye + Range < cave.Min) || math.any(Eye - Range > cave.Max))
                        continue;
                    for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                    {
                        var node = set.Nodes[n];
                        if (math.distance(node.Position, Eye) > Range + node.Radius)
                            continue;
                        var h = Scramble(math.hash(new int2(n, 0x5EE9)));
                        if ((h & 0xFFFF) / 65536f >= (lava ? TubeSeepShare : WaterTubeSeepShare))
                            continue;
                        var next = set.Nodes[n + 1 < cave.FirstNode + cave.NodeCount ? n + 1 : math.max(n - 1, cave.FirstNode)].Position;
                        var along = math.normalizesafe((next - node.Position).xz, new float2(1f, 0f));
                        var dir = ((h >> 16) & 1u) == 0u ? new float2(-along.y, along.x) : new float2(along.y, -along.x);
                        var y = node.Position.y + node.Radius * (0.1f + 0.4f * (((h >> 17) & 0xFF) / 255f));
                        if (WallSeep(Source, new float3(node.Position.x, y, node.Position.z), dir, 0f, node.Radius * 3f, node.Position.y - node.Radius * 2f, kind, Scramble(h), out var spot) &&
                            math.distance(spot.Lip, Eye) <= Range)
                            Found.Add(spot);
                    }
                    for (int i = cave.FirstPool; i < cave.FirstPool + cave.PoolCount; i++)
                    {
                        var pool = set.Pools[i];
                        if (pool.Lava > 0.5f != lava || math.distance(pool.Centre, Eye) > Range + pool.Radius + 20f)
                            continue;
                        for (int k = 0; k < (lava ? PoolSeeps : WaterPoolSeeps); k++)
                        {
                            var h = Scramble(math.hash(new int2(i, k + 0x5EE9)));
                            var angle = (h & 0xFFFF) / 65536f * 6.2832f;
                            var y = pool.Centre.y + 1.2f + 2.3f * (((h >> 16) & 0xFF) / 255f);
                            if (WallSeep(Source, new float3(pool.Centre.x, y, pool.Centre.z), new float2(math.cos(angle), math.sin(angle)), pool.Radius * 0.5f, pool.Radius + 25f, pool.Centre.y, kind, Scramble(h), out var spot) &&
                                math.distance(spot.Lip, Eye) <= Range)
                                Found.Add(spot);
                        }
                    }
                }
            }
        }

        void BeginSeepSearch(in TerrainSource source, float3 eye, float range, int2 cell)
        {
            if (!lavaStyle.IsCreated || !seepVents.IsCreated || !seepWet.IsCreated)
                return;
            if (!seepFound.IsCreated)
                seepFound = new NativeList<SeepSpot>(64, Allocator.Persistent);
            seepEye = eye;
            seepJob = new SeepSearchJob
            {
                Source = source, LavaStyle = lavaStyle, Vents = seepVents, Wet = seepWet,
                Eye = eye, Range = range, Cell = cell, Found = seepFound
            }.Schedule();
            JobHandle.ScheduleBatchedJobs();
            seepSearching = true;
        }

        void EndSeepSearch(in TerrainSource source)
        {
            seepJob.Complete();
            seepSearching = false;
            seeps.Clear();
            for (int i = 0; i < seepFound.Length; i++)
                seeps.Add(seepFound[i]);
            if (seeps.Count > MaxSeeps)
            {
                var eye = seepEye;
                seeps.Sort((a, b) => math.distancesq(a.Lip, eye).CompareTo(math.distancesq(b.Lip, eye)));
                seeps.RemoveRange(MaxSeeps, seeps.Count - MaxSeeps);
            }
            seepBeads.Clear();
            foreach (var s in seeps)
                if (s.Kind == SeepKind.Lava)
                    seepBeads.Add(SeepPlume(s));
            LastSeeps = seeps.Count;
            Kit.Sparks.Set(seepOwner, seepBeads);
            SeepFallMesh(source, seepEye, seepRangeAt);
            SeepStreaks(seepEye, seepRangeAt);
        }

        void DisposeSeepSearch()
        {
            seepJob.Complete();
            seepSearching = false;
            if (seepFound.IsCreated)
                seepFound.Dispose();
            if (lavaStyle.IsCreated)
                lavaStyle.Dispose();
            if (seepVents.IsCreated)
                seepVents.Dispose();
            if (seepWet.IsCreated)
                seepWet.Dispose();
            seepCellAt = new int2(int.MinValue);
        }
    }
}
