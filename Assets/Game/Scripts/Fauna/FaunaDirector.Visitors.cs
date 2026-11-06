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
    // A-81 row 4: the visitors (honey thief, messenger, traveller), moved out of FaunaDirector.cs as they were.
    public partial class FaunaDirector
    {
        /// <summary>
        /// Now and then by day, a stranger after a wild hive's honey near a player: one of the folk with a stick walks up
        /// to a hive 25 to 90 m from them, pokes at the comb (the bees boil out round them), grabs a piece and runs off
        /// with it, the bees after them. One at a time. The brain's hive-raid stages do the rest, as for a bear.
        /// </summary>
        void SpawnHoneyThief(in TerrainSource source)
        {
            if (simWorld == null || !source.Anchors.IsCreated || (Cycle != null && Cycle.Night > 0.3f))
                return;
            var em = simWorld.EntityManager;
            if (honeyThief != Entity.Null && em.Exists(honeyThief))
                return;
            var human = TableSpecies(OverworldSlot.HoneyThief, 0);
            if (human < 0)
                return;
            var player = players[rng.NextInt(players.Count)];
            ref var set = ref source.Anchors.Value;
            var near = new NativeList<int>(4, Allocator.Temp);
            Anchors.Near(ref set, player, 90f, AnchorKind.Hive, ref near);
            var pick = -1;
            for (int i = 0; i < near.Length; i++)
            {
                ref var h = ref set.Anchors[near[i]];
                if (!h.InCave && math.distance(h.Position.xz, player.xz) > 25f && (pick < 0 || rng.NextBool()))
                    pick = near[i];
            }
            near.Dispose();
            if (pick < 0)
                return;
            var hive = set.Anchors[pick];
            var trunk = math.max(hive.Size - 1f, 0.1f);
            // (Coming in from out beyond the hive, where the player can watch them walk up.)
            var from = hive.Position.xz + math.normalizesafe(hive.Position.xz - player.xz, rng.NextFloat2Direction()) * 25f;
            for (int tries = 0; tries < 4 && !(source.SampleGround(from) is var g && !(g.HasWater && g.WaterSurface > g.Height)); tries++)
                from = hive.Position.xz + rng.NextFloat2Direction() * 25f;
            var e = Spawn(human, new float3(from.x, source.SampleHeight(from), from.y));
            var state = em.GetComponentData<CreatureState>(e);
            state.Mode = CreatureMode.Work;
            state.Stage = CreatureAnimator.RaidWalk;
            state.StageTime = 0f;
            state.Focus = hive.Position.xz + hive.Facing * trunk;
            state.Target = hive.Position.xz + hive.Facing * (trunk + 0.35f);
            state.Timer = 120f;
            var raidMind = em.GetComponentData<CreatureMind>(e);
            raidMind.ChaseCooldown = 900f;
            em.SetComponentData(e, raidMind);
            state.Held = HeldItem.Stick;
            state.Home = from;
            state.Leash = 0f;
            em.SetComponentData(e, state);
            honeyThief = e;
        }

        /// <summary>
        /// Now and then (every two to four minutes) a messenger bird for a town a player is in: a pigeon high over the
        /// way to another town, 90 m out, with a message (the brain's Messenger task brings it in to one of the townsfolk,
        /// then sends it off towards a third town, or back); one at a time.
        /// </summary>
        void SpawnMessenger(in TerrainSource source)
        {
            if (!source.Settlements.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            {
                var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var flying = false;
                for (int i = 0; i < states.Length && !flying; i++)
                    flying = states[i].Task == CreatureTask.Messenger;
                states.Dispose();
                if (flying)
                    return;
            }
            var pigeon = TableSpecies(OverworldSlot.Messenger, 0);
            ref var set = ref source.Settlements.Value;
            if (pigeon < 0 || set.Settlements.Length < 2)
                return;
            var player = players[rng.NextInt(players.Count)];
            var town = -1;
            for (int t = 0; t < set.Settlements.Length && town < 0; t++)
                if (math.distance(set.Settlements[t].Centre, player.xz) < set.Settlements[t].Radius + 20f)
                    town = t;
            if (town < 0)
                return;
            var from = (town + 1 + rng.NextInt(set.Settlements.Length - 1)) % set.Settlements.Length;
            var to = (town + 1 + rng.NextInt(set.Settlements.Length - 1)) % set.Settlements.Length;
            var centre = set.Settlements[town].Centre;
            var start = centre + math.normalizesafe(set.Settlements[from].Centre - centre, new float2(1f, 0f)) * 90f;
            var e = Spawn(pigeon, new float3(start.x, source.SampleHeight(start) + 25f, start.y));
            var state = em.GetComponentData<CreatureState>(e);
            state.Position.y = source.SampleHeight(start) + 25f;
            state.Task = CreatureTask.Messenger;
            state.Mode = CreatureMode.Work;
            state.Stage = 0;
            state.StageTime = 0f;
            state.Partner = Entity.Null;
            state.Home = centre;
            state.Leash = 0f;
            state.WorkSpot = set.Settlements[to].Centre;
            state.Held = HeldItem.Letter;
            state.AnchorSize = 0f;
            em.SetComponentData(e, state);
        }

        /// <summary>
        /// Now and then (every minute or two) a traveller on a road between towns, a way off from a player (40 to 160 m,
        /// outside any town), heading along it the way that brings it nearer (the brain's Travel task walks it there); at
        /// most two on the roads at a time.
        /// </summary>
        void SpawnTraveller(in TerrainSource source)
        {
            if (!source.Settlements.IsCreated || simWorld == null)
                return;
            var em = simWorld.EntityManager;
            {
                var states = wildQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
                var travelling = 0;
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Task == CreatureTask.Travel)
                        travelling++;
                states.Dispose();
                if (travelling >= 2)
                    return;
            }
            var human = TableSpecies(OverworldSlot.Traveller, 0);
            if (human < 0)
                return;
            ref var set = ref source.Settlements.Value;
            if (set.Streets.Length == 0)
                return;
            var player = players[rng.NextInt(players.Count)];
            // (Every road stretch in the ring round the player, outside the towns; one of them at random.)
            var candidates = new List<int>();
            for (int k = 0; k < set.Streets.Length; k++)
            {
                ref var st = ref set.Streets[k];
                if (st.Road == 0)
                    continue;
                var d = math.distance(st.A.xz, player.xz);
                if (d < 40f || d > 160f)
                    continue;
                var inTown = false;
                for (int t = 0; t < set.Settlements.Length && !inTown; t++)
                    inTown = math.distance(set.Settlements[t].Centre, st.A.xz) < set.Settlements[t].Radius;
                if (!inTown)
                    candidates.Add(k);
            }
            var found = candidates.Count > 0 ? candidates[rng.NextInt(candidates.Count)] : -1;
            if (found < 0)
                return;
            var start = set.Streets[found].A;
            var e = Spawn(human, new float3(start.x, source.SampleHeight(start.xz), start.z));
            var state = em.GetComponentData<CreatureState>(e);
            state.Task = CreatureTask.Travel;
            state.Mode = CreatureMode.Work;
            state.Stage = 0;
            state.TargetHeight = found;   // (the road stretch it's on: see the brain's Travelling)
            // (Heading the way that brings it nearer the player: a traveller seen arriving, not one walking off out of sight.)
            state.AnchorSide = (byte)(math.distance(set.Streets[found].B.xz, player.xz) < math.distance(start.xz, player.xz) ? 1 : 0);
            state.Home = start.xz;
            state.Leash = 0f;
            state.Timer = 900f;
            em.SetComponentData(e, state);
        }

        /// <summary>
        /// The critter tier: round each player, the critter spots (AmbientLife.CritterSpots, from the seed) within
        /// CritterRadius that are free and are for this time of day get their real creature, nearest first, up to that
        /// player's fair share of MaxCritters (shared among the players with spots near them, never below a floor of 24).
        /// Not where a player is right on top of it. They're homed on their spot (leashed close) and run on the brain's
        /// light critter round; Despawn lets them go beyond CritterLetGo, and the spot is free again.
        /// </summary>
        /// <summary>Works out the critter spots of a list of cells, in order; Ends[i] is where cell i's spots end.</summary>
        [BurstCompile]
        struct SettleCellsJob : IJob
        {
            public TerrainSource Source;
            public BlobAssetReference<Punchy.Flora.FloraBlob> Flora;
            [ReadOnly] public NativeArray<int2> Cells;
            public NativeList<AmbientLife.CritterSpot> Spots;
            public NativeArray<int> Ends;

            public void Execute()
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    AmbientLife.CritterSpots(Cells[i], Source, ref Flora.Value, ref Spots);
                    Ends[i] = Spots.Length;
                }
            }
        }
    }
}
