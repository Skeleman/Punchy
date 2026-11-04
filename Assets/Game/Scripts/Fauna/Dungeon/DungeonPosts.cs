using Punchy.Dungeon;
using Punchy.Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>What a dungeon creature does at its socket, chosen from the socket's tags (#24).</summary>
    public enum DungeonBehaviour : byte { None, Drink, Guard }

    /// <summary>
    /// A dungeon creature's post (#24): the socket it was spawned at and what it does there. Drink at NearWater (goes
    /// back to drink at the socket every so often); guard at NearChest (stays within a few metres of it and turns to
    /// face whoever comes close). Added by <see cref="DungeonSpawnSystem"/> where animals live.
    /// </summary>
    public struct DungeonPost : IComponentData
    {
        public float3 Anchor;
        /// <summary>Where the socket faces (the water for a drinker).</summary>
        public float2 Facing;
        public DungeonBehaviour Behaviour;
        public float Cool;
        /// <summary>Set once a log line has been written, so each post logs once per drink or approach.</summary>
        public bool Logged;
        public FixedString32Bytes Name;

        public static DungeonPost From(in DungeonSocket s, DungeonContent c)
        {
            var fwd = math.mul(s.Rot, new float3(0, 0, 1));
            var tags = (SocketTags)s.Tags;
            return new DungeonPost
            {
                Anchor = s.Pos,
                Facing = math.normalizesafe(fwd.xz, new float2(0, 1)),
                Behaviour = (tags & SocketTags.NearChest) != 0 ? DungeonBehaviour.Guard
                          : (tags & SocketTags.NearWater) != 0 ? DungeonBehaviour.Drink : DungeonBehaviour.None,
                Name = c.ToString(),
            };
        }
    }

    /// <summary>
    /// Runs the dungeon posts after the brain (#24): a Burst job over the few posted creatures, scheduled so it never
    /// pulls the brain onto the main thread. Nudges the brain's own modes (Drink, Wander, Idle) rather than moving bodies.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [BurstCompile]
    public partial struct DungeonPostSystem : ISystem
    {
        const float GuardLeash = 2.5f, GuardSees = 7f;

        EntityQuery players, netPlayers;

        public void OnCreate(ref SystemState state)
        {
            players = SystemAPI.QueryBuilder().WithAll<PlayerTag, LocalTransform>().Build();
            netPlayers = SystemAPI.QueryBuilder().WithAll<Punchy.Net.NetPlayer, LocalTransform>().Build();
            state.RequireForUpdate<DungeonPost>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // On a server the players are the networked ones (the host's and every client's): its world has no
            // PlayerTag. Solo, the one local player. The same choice as the brain's.
            var who = netPlayers.IsEmptyIgnoreFilter ? players : netPlayers;
            var seen = who.ToComponentDataListAsync<LocalTransform>(state.WorldUpdateAllocator, state.Dependency, out var got);
            state.Dependency = new PostJob { Players = seen.AsDeferredJobArray(), Dt = SystemAPI.Time.DeltaTime }.Schedule(got);
        }

        [BurstCompile]
        partial struct PostJob : IJobEntity
        {
            [ReadOnly] public NativeArray<LocalTransform> Players;
            public float Dt;

            void Execute(ref CreatureState s, ref CreatureMind m, ref DungeonPost p)
            {
                p.Cool -= Dt;
                var home = p.Anchor.xz;
                if (p.Behaviour == DungeonBehaviour.Drink)
                {
                    if (s.Mode == CreatureMode.Drink)
                    {
                        if (s.Stage == 1 && !p.Logged)
                        {
                            p.Logged = true;
                            Debug.Log($"[dungeon] {p.Name} drinks at its NearWater socket ({(int)math.round(p.Anchor.x)}, {(int)math.round(p.Anchor.z)})");
                        }
                    }
                    else if (p.Cool <= 0f && (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Rest))
                    {
                        p.Cool = 25f;
                        p.Logged = false;
                        Start(ref s, CreatureMode.Drink);
                        s.Target = home;
                        s.Focus = home + p.Facing * 0.8f;
                    }
                    return;
                }
                if (p.Behaviour != DungeonBehaviour.Guard)
                    return;
                s.Home = home;
                s.Leash = GuardLeash;
                var near = -1;
                var best = GuardSees;
                for (int i = 0; i < Players.Length; i++)
                {
                    var d = math.distance(Players[i].Position.xz, s.Position.xz);
                    if (d < best)
                    {
                        best = d;
                        near = i;
                    }
                }
                var away = math.distance(s.Position.xz, home);
                if (away > GuardLeash && s.Mode != CreatureMode.Flee)
                {
                    // Back to the chest.
                    if (s.Mode != CreatureMode.Wander || math.distancesq(s.Target, home) > 0.01f)
                        Start(ref s, CreatureMode.Wander);
                    s.Target = home;
                    s.Timer = math.max(s.Timer, 4f);
                }
                else if (near >= 0)
                {
                    // Stand and face them.
                    if (s.Mode != CreatureMode.Idle)
                        Start(ref s, CreatureMode.Idle);
                    s.Timer = math.max(s.Timer, 1f);
                    var to = Players[near].Position.xz - s.Position.xz;
                    var want = math.atan2(to.x, to.y);
                    var turn = math.atan2(math.sin(want - s.Yaw), math.cos(want - s.Yaw));
                    s.Yaw += math.clamp(turn, -3f * Dt, 3f * Dt);
                    m.LookAt = Players[near].Position;
                    if (!p.Logged)
                    {
                        p.Logged = true;
                        Debug.Log($"[dungeon] {p.Name} guards its chest: faces a player {(int)math.round(best * 10f)} dm away, {(int)math.round(away * 10f)} dm from the chest");
                    }
                }
                else
                    p.Logged = false;
            }

            static void Start(ref CreatureState s, CreatureMode mode)
            {
                s.Mode = mode;
                s.ModeTime = 0f;
                s.Stage = 0;
                s.StageTime = 0f;
                s.Partner = Entity.Null;
                s.Action = IdleAction.None;
                s.ActionTime = 0f;
            }
        }
    }
}
