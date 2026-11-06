using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 4: the critter tier and the domestic animals, moved out of FaunaDirector.cs as they were.
    public partial class FaunaDirector
    {
        /// <summary>
        /// Keeps the critter cells round the players worked out: collects a finished settling job's cells into the cache,
        /// then hands the next batch of unknown cells (nearest a player first) to a new job. The main thread only does the
        /// bookkeeping, so the critter tier never stalls a frame.
        /// </summary>
        void SettleCritterCells(in TerrainSource source)
        {
            if (settling)
            {
                if (!settleHandle.IsCompleted)
                    return;
                settleHandle.Complete();
                // (Unless the world changed meanwhile: then they're not this world's spots.)
                if (critterSource.Equals(source))
                {
                    var from = 0;
                    for (int i = 0; i < settleCells.Length; i++)
                    {
                        var spots = new AmbientLife.CritterSpot[settleEnds[i] - from];
                        for (int k = 0; k < spots.Length; k++)
                            spots[k] = settleSpots[from + k];
                        critterCells[settleCells[i]] = spots;
                        from = settleEnds[i];
                    }
                }
                EndSettling();
            }
            if (overworld == null || simWorld == null || players.Count == 0)
                return;
            if (!critterSource.Equals(source))
            {
                critterCells.Clear();
                critterSource = source;
            }
            if (floraQuery.IsEmpty)
                return;
            var flora = floraQuery.GetSingleton<Punchy.Flora.FloraLookup>();
            if (!flora.Blob.IsCreated)
                return;
            // Rings outwards round each player in turn, so the nearest cells come first.
            const float cell = 16f;
            critterMissing.Clear();
            var reach = (int)math.ceil(CritterRadius / cell);
            for (int ring = 0; ring <= reach && critterMissing.Count < CritterCellsPerJob; ring++)
            foreach (var player in players)
            {
                var centre = (int2)math.floor(player.xz / cell);
                for (int y = -ring; y <= ring; y++)
                for (int x = -ring; x <= ring; x++)
                {
                    var key = centre + new int2(x, y);
                    if (math.max(math.abs(x), math.abs(y)) == ring && critterMissing.Count < CritterCellsPerJob &&
                        !critterCells.ContainsKey(key) && !critterMissing.Contains(key))
                        critterMissing.Add(key);
                }
            }
            if (critterMissing.Count == 0)
                return;
            settleCells = new NativeArray<int2>(critterMissing.Count, Allocator.Persistent);
            for (int i = 0; i < critterMissing.Count; i++)
                settleCells[i] = critterMissing[i];
            settleSpots = new NativeList<AmbientLife.CritterSpot>(critterMissing.Count * 4, Allocator.Persistent);
            settleEnds = new NativeArray<int>(critterMissing.Count, Allocator.Persistent);
            settleHandle = new SettleCellsJob { Source = source, Flora = flora.Blob, Cells = settleCells, Spots = settleSpots, Ends = settleEnds }.Schedule();
            settling = true;
        }

        /// <summary>Finishes any settling job and frees its lists.</summary>
        void EndSettling()
        {
            if (!settling)
                return;
            settleHandle.Complete();
            settleCells.Dispose();
            settleSpots.Dispose();
            settleEnds.Dispose();
            settling = false;
        }

        void SpawnCritters(in TerrainSource source)
        {
            if (overworld == null || simWorld == null || species.Count == 0)
                return;
            var em = simWorld.EntityManager;
            if (floraQuery.IsEmpty)
                return;
            var flora = floraQuery.GetSingleton<Punchy.Flora.FloraLookup>();
            if (!flora.Blob.IsCreated)
                return;
            // (Spots whose creature has gone: free again.)
            var gone = new List<uint>();
            foreach (var kv in critters)
                if (!em.Exists(kv.Value))
                    gone.Add(kv.Key);
            foreach (var id in gone)
                critters.Remove(id);
            var night = Cycle != null ? Cycle.Night : 0f;
            var share = math.max(24, MaxCritters / math.max(players.Count, 1));
            const float cell = 16f;
            // (A new world: none of the old spots hold.)
            if (!critterSource.Equals(source))
            {
                critterCells.Clear();
                critterSource = source;
            }
            if (critterCells.Count > 600)
            {
                critterCellsGone.Clear();
                foreach (var key in critterCells.Keys)
                    if (NearestPlayer(((float2)key + 0.5f) * cell) > CritterLetGo + cell * 2f)
                        critterCellsGone.Add(key);
                foreach (var key in critterCellsGone)
                    critterCells.Remove(key);
            }
            foreach (var player in players)
            {
                if (critterCount >= MaxCritters)
                    return;
                // How many this player already has round it.
                var mine = 0;
                foreach (var kv in critters)
                    if (em.HasComponent<CreatureState>(kv.Value) &&
                        math.distance(em.GetComponentData<CreatureState>(kv.Value).Position.xz, player.xz) < CritterRadius)
                        mine++;
                if (mine >= share)
                    continue;
                critterSpots.Clear();
                var min = (int2)math.floor((player.xz - CritterRadius) / cell);
                var max = (int2)math.floor((player.xz + CritterRadius) / cell);
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var key = new int2(x, y);
                    // (Cells not worked out yet are settled a few a frame by SettleCritterCells.)
                    if (critterCells.TryGetValue(key, out var spots))
                        critterSpots.AddRange(spots);
                    // (And the gulls on any wharf's posts there.)
                    if (gullCells.TryGetValue(key, out var gulls))
                        critterSpots.AddRange(gulls);
                }
                critterSpots.Sort((a, b) => math.distancesq(a.Position.xz, player.xz).CompareTo(math.distancesq(b.Position.xz, player.xz)));
                foreach (var spot in critterSpots)
                {
                    if (mine >= share || critterCount >= MaxCritters)
                        break;
                    var d = math.distance(spot.Position.xz, player.xz);
                    if (d > CritterRadius || d < 6f || critters.ContainsKey(spot.Id) || pendingCritterSpots.Contains(spot.Id))
                        continue;
                    var kind = spot.Kind;
                    // (The table's rows say when: owls and frogs at night; songbirds, woodpeckers and gulls by day; mice
                    // and the wharves' fish either.)
                    var phase = night < 0.4f ? DayPhase.Day : DayPhase.Night;
                    var index = TableSpecies(CritterSlot(kind), spot.Id, phase);
                    if (index < 0)
                        continue;
                    var at = spot.Position;
                    var id = spot.Id;
                    var leash = kind == AmbientLife.CritterKind.Songbird || kind == AmbientLife.CritterKind.Gull ? 18f : 8f;
                    var timer = rng.NextFloat(4f, 15f);
                    pendingCritterSpots.Add(id);
                    Enqueue(index, at, SpawnTier.Critter, e =>
                    {
                        var em = simWorld.EntityManager;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Position = at;
                        state.Task = CreatureTask.Critter;
                        state.Home = at.xz;
                        state.Leash = leash;
                        // (Settled on its perch a while before it does anything.)
                        state.Mode = CreatureMode.Rest;
                        state.Timer = timer;
                        em.SetComponentData(e, state);
                        critters[id] = e;
                        pendingCritterSpots.Remove(id);
                    }, rng.NextFloat(6.28f));
                    mine++;
                }
            }
        }

        /// <summary>The settlement's domestic animals, about its middle, homed on it and leashed to it.</summary>
        void SpawnDomestics(in TerrainSource source, LandmarkType settlement, float2 centre, float radius)
        {
            if (overworld == null)
                return;
            // (Every Domestic row for this kind of settlement, in table order; the table matches the landmark by name.)
            foreach (var ri in TableRows(OverworldSlot.Domestic, settlement).ToArray())
            {
                var d = overworld.Rows[ri];
                if (d.Species == null)
                    continue;
                var index = SpeciesIndex(d.Species);
                if (index < 0)
                    continue;
                var count = rng.NextInt(math.max(0, d.Count.x), math.max(d.Count.x, d.Count.y) + 1);
                // Hunters range well beyond the settlement, out to where the game is (fishers to the water).
                var leash = d.Task == CreatureTask.Hunt || d.Task == CreatureTask.Fish || d.Task == CreatureTask.Mine ? math.max(radius * 3f, 70f) : radius * d.Range;
                // Lava dwellers go in the landmark's lava (a few tries each), not on dry ground.
                var lava = d.Species.Body.LavaDweller;
                for (int k = 0; k < count; k++)
                {
                    var p = centre + rng.NextFloat2Direction() * rng.NextFloat(0.1f, 0.7f) * leash;
                    var g = source.SampleGround(p);
                    for (int t = 0; lava && t < 12 && !(g.HasWater && g.WaterSurface - g.Height > 0.5f &&
                                                        source.LavaAt(new float3(p.x, g.WaterSurface - 0.2f, p.y)) > 0.5f); t++)
                    {
                        p = centre + rng.NextFloat2Direction() * rng.NextFloat(0f, 0.8f) * leash;
                        g = source.SampleGround(p);
                    }
                    if (lava ? !(g.HasWater && g.WaterSurface > g.Height + 0.5f) : g.HasWater && g.WaterSurface > g.Height)
                        continue;
                    var task = d.Task;
                    Enqueue(index, new float3(p.x, lava ? g.WaterSurface - 0.3f : g.Height, p.y), SpawnTier.Settled, e =>
                    {
                        var em = simWorld.EntityManager;
                        var state = em.GetComponentData<CreatureState>(e);
                        state.Home = centre;
                        state.Leash = leash;
                        state.Task = task;
                        em.SetComponentData(e, state);
                    });
                }
            }
        }

        /// <summary>
        /// A cave fills from the cave table (SpawnCaveFromTable) when a player comes within Radius of its mouth; like
        /// residents, its animals are forgotten once everyone is well away. (This cave's glowing plants, found once.)
        /// </summary>
        readonly List<int> caveGlows = new List<int>();

        /// <summary>The Work anchor nearest a point within a radius: where a worker stands, and the way it faces.</summary>
        static bool WorkAnchorNear(ref AnchorSetBlob set, float3 at, float radius, out float2 stand, out float2 facing)
        {
            stand = facing = float2.zero;
            var near = new NativeList<int>(4, Allocator.Temp);
            Anchors.Near(ref set, at, radius, AnchorKind.Work, ref near);
            var best = float.MaxValue;
            foreach (var i in near)
            {
                var d = math.distancesq(set.Anchors[i].Position, at);
                if (d < best)
                {
                    best = d;
                    stand = set.Anchors[i].Position.xz;
                    facing = set.Anchors[i].Facing;
                }
            }
            near.Dispose();
            return best < float.MaxValue;
        }
    }
}
