using Punchy.Fauna;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Punchy.Net
{
    // How creatures are networked
    // ---------------------------
    // The server owns the animals: FaunaDirector spawns them there (around every player) as instances of one creature
    // ghost prefab, and CreatureBrainSystem runs on the server only. Each tick CreatureNetWriteSystem copies the parts
    // of CreatureState the animator needs into the ghost (LocalTransform for position and heading, CreatureNet for the
    // rest). Clients receive the ghosts interpolated; FaunaDirector gives each one its species' body there (it builds
    // the same species table on every machine, sorted by name), and CreatureReplicaSystem turns the replicated values
    // back into a CreatureState each frame, advancing the gait by the replicated speed, so the animator and renderer
    // work on replicas exactly as on local animals.

    /// <summary>A creature's replicated state (the ghost's own fields; position and heading ride on LocalTransform).</summary>
    public struct CreatureNet : IComponentData
    {
        /// <summary>Index in FaunaDirector's species table (identical on every machine).</summary>
        [GhostField] public int Species;
        [GhostField] public uint Individual;
        [GhostField] public byte Coat;
        [GhostField] public byte Mode, Task, Stage;
        [GhostField(Quantization = 1000)] public float Scale;
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float Speed;
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float Look;
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float Lie, HeadDown, Bow, Curl, Air;
        [GhostField(Quantization = 100, Smoothing = SmoothingAction.Interpolate)] public float Sit, Vertical, Fly;
        /// <summary>Not smoothed: it jumps back to 0 when a new action starts.</summary>
        [GhostField(Quantization = 100)] public float ActionTime;
        [GhostField] public byte Action;
        /// <summary>Its lair, if it has one (a burrow's mouth, a web's centre) and how big: drawn on every machine.</summary>
        [GhostField(Quantization = 100)] public float3 Anchor;
        [GhostField(Quantization = 100)] public float AnchorSize;
    }

    /// <summary>Client: this creature is a server ghost that FaunaDirector has given a body (CreatureState/CreatureBody).</summary>
    public struct CreatureReplica : IComponentData { }

    /// <summary>Points at the creature ghost prefab; baked into the world subscene.</summary>
    public struct CreatureGhostSpawner : IComponentData
    {
        public Entity Prefab;
    }

    /// <summary>Server: publishes each simulated creature's state on its ghost.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    public partial struct CreatureNetWriteSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (s, body, net, transform) in SystemAPI.Query<RefRO<CreatureState>, RefRO<CreatureBody>, RefRW<CreatureNet>, RefRW<LocalTransform>>())
            {
                var c = s.ValueRO;
                transform.ValueRW.Position = c.Position;
                transform.ValueRW.Rotation = quaternion.RotateY(c.Yaw);
                ref var n = ref net.ValueRW;
                n.Species = body.ValueRO.Species;
                n.Coat = (byte)body.ValueRO.Coat;
                n.Individual = c.Individual;
                n.Scale = c.Scale;
                n.Mode = (byte)c.Mode;
                n.Task = (byte)c.Task;
                n.Stage = c.Stage;
                n.Speed = c.Speed;
                n.Look = c.Look;
                n.Lie = c.Lie;
                n.HeadDown = c.HeadDown;
                n.Bow = c.Bow;
                n.Curl = c.Curl;
                n.Air = c.Air;
                n.Sit = c.Sit;
                n.Vertical = c.Vertical;
                n.Fly = c.Fly;
                n.Action = (byte)c.Action;
                n.ActionTime = c.ActionTime;
                n.Anchor = c.Anchor;
                n.AnchorSize = c.AnchorSize;
            }
        }
    }

    /// <summary>
    /// Server: each player only receives the animals near them (within <see cref="Radius"/> of their NetPlayer), so
    /// players far apart don't pay for each other's surroundings. Everything else (players, the world state) is
    /// always sent.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct CreatureRelevancySystem : ISystem
    {
        /// <summary>A bit beyond FaunaDirector's despawn distance, so nothing a player could see is ever held back.</summary>
        public const float Radius = 200f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GhostRelevancy>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ref var relevancy = ref SystemAPI.GetSingletonRW<GhostRelevancy>().ValueRW;
            relevancy.GhostRelevancyMode = GhostRelevancyMode.SetIsIrrelevant;
            var set = relevancy.GhostRelevancySet;
            set.Clear();

            // Where each connection's player is (connections without a placed player get everything).
            var players = new NativeHashMap<int, float3>(8, Allocator.Temp);
            foreach (var (owner, transform) in SystemAPI.Query<RefRO<GhostOwner>, RefRO<LocalTransform>>().WithAll<NetPlayer>())
                if (math.lengthsq(transform.ValueRO.Position) > 1e-6f)
                    players[owner.ValueRO.NetworkId] = transform.ValueRO.Position;
            if (players.Count == 0)
                return;
            var radiusSq = Radius * Radius;
            foreach (var (ghost, transform) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<LocalTransform>>().WithAll<CreatureNet>())
            {
                var xz = transform.ValueRO.Position.xz;
                foreach (var player in players)
                    if (math.distancesq(player.Value.xz, xz) > radiusSq)
                        set.TryAdd(new RelevantGhostForConnection(player.Key, ghost.ValueRO.ghostId), 1);
            }
        }
    }

    /// <summary>
    /// Client: rebuilds each replica's CreatureState from its interpolated ghost: pose and posture as replicated, the
    /// gait phase advanced by the replicated speed (the same stride maths as the brain), sway from the turning rate.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureReplicaSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = math.max(math.min(SystemAPI.Time.DeltaTime, 0.1f), 1e-4f);
            foreach (var (s, body, net, transform) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>, RefRO<CreatureNet>, RefRO<LocalTransform>>()
                         .WithAll<CreatureReplica>())
            {
                ref var c = ref s.ValueRW;
                ref var b = ref body.ValueRO.Blob.Value;
                var n = net.ValueRO;
                var forward = math.mul(transform.ValueRO.Rotation, new float3(0f, 0f, 1f));
                var yaw = math.atan2(forward.x, forward.z);
                var turn = yaw - c.Yaw;
                turn -= 2f * math.PI * math.floor((turn + math.PI) / (2f * math.PI));
                c.YawRate = turn / dt;
                c.Yaw = yaw;
                c.Position = transform.ValueRO.Position;
                c.Clock += dt;
                c.Scale = n.Scale > 0f ? n.Scale : 1f;
                c.Mode = (CreatureMode)n.Mode;
                c.Task = (CreatureTask)n.Task;
                c.Stage = n.Stage;
                c.Speed = n.Speed;
                c.Look = n.Look;
                c.Lie = n.Lie;
                c.HeadDown = n.HeadDown;
                c.Bow = n.Bow;
                c.Curl = n.Curl;
                c.Air = n.Air;
                c.Sit = n.Sit;
                c.Vertical = n.Vertical;
                c.Fly = n.Fly;
                c.Action = (IdleAction)n.Action;
                c.ActionTime = n.ActionTime;
                c.Anchor = n.Anchor;
                c.AnchorSize = n.AnchorSize;
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
            }
        }
    }
}
