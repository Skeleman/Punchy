using Punchy.Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Net
{
    // How players are networked
    // -------------------------
    // Each machine moves its own player locally, exactly as in single player (PlayerMovementSystem on the local
    // PlayerTag entity, with tree collisions and cave walls that only exist around that player). The server spawns a
    // NetPlayer ghost per connection; the owner writes its player's pose into that ghost's NetPlayerCommand every
    // tick, the server copies it onto the ghost, and every other client sees the ghost interpolated. Movement is
    // client-authoritative, which suits co-op exploration and avoids mispredicting against client-only colliders.

    /// <summary>A player as the network sees it (one per connection, owned by that connection).</summary>
    public struct NetPlayer : IComponentData { }

    /// <summary>The owner's pose for this tick, sent to the server.</summary>
    public struct NetPlayerCommand : IInputComponentData
    {
        public float3 Position;
        public float Yaw;
        /// <summary>False until the owner has a player to report (so the ghost doesn't flash at the origin).</summary>
        public bool Valid;
    }

    /// <summary>Points at the NetPlayer ghost prefab; baked into the world subscene.</summary>
    public struct NetPlayerSpawner : IComponentData
    {
        public Entity Prefab;
    }

    public struct GoInGameRequest : IRpcCommand { }

    /// <summary>
    /// Client: once connected, go in game (start the snapshot and command streams) and ask for a player. Waits for the
    /// world subscene (which holds the ghost prefabs) to load first, or the first snapshot names prefabs we don't have.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial struct GoInGameClientSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetPlayerSpawner>();
            state.RequireForUpdate(state.GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NetworkId>().WithNone<NetworkStreamInGame>()));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (_, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithNone<NetworkStreamInGame>().WithEntityAccess())
            {
                commands.AddComponent<NetworkStreamInGame>(connection);
                var request = commands.CreateEntity();
                commands.AddComponent<GoInGameRequest>(request);
                commands.AddComponent(request, new SendRpcCommandRequest { TargetConnection = connection });
            }
            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>Server: puts the connection in game and spawns its NetPlayer, which goes when the connection does.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial struct GoInGameServerSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetPlayerSpawner>();
            state.RequireForUpdate(state.GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<GoInGameRequest, ReceiveRpcCommandRequest>()));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var prefab = SystemAPI.GetSingleton<NetPlayerSpawner>().Prefab;
            var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, entity) in SystemAPI.Query<RefRO<ReceiveRpcCommandRequest>>().WithAll<GoInGameRequest>().WithEntityAccess())
            {
                var connection = request.ValueRO.SourceConnection;
                commands.AddComponent<NetworkStreamInGame>(connection);
                var id = SystemAPI.GetComponent<NetworkId>(connection).Value;
                var player = commands.Instantiate(prefab);
                commands.SetComponent(player, new GhostOwner { NetworkId = id });
                commands.AppendToBuffer(connection, new LinkedEntityGroup { Value = player });
                commands.DestroyEntity(entity);
            }
            commands.Playback(state.EntityManager);
        }
    }

    /// <summary>Client: reports the local player's pose on our own NetPlayer ghost.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial struct NetPlayerSendSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetPlayer>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var command = default(NetPlayerCommand);
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                var forward = math.mul(t.ValueRO.Rotation, new float3(0f, 0f, 1f));
                command = new NetPlayerCommand
                {
                    Position = t.ValueRO.Position,
                    Yaw = math.atan2(forward.x, forward.z),
                    Valid = true
                };
            }
            foreach (var input in SystemAPI.Query<RefRW<NetPlayerCommand>>().WithAll<NetPlayer, GhostOwnerIsLocal>())
                input.ValueRW = command;
        }
    }

    /// <summary>Server: moves each NetPlayer to where its owner says it is.</summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial struct NetPlayerApplySystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (command, transform) in SystemAPI.Query<RefRO<NetPlayerCommand>, RefRW<LocalTransform>>().WithAll<NetPlayer>())
            {
                if (!command.ValueRO.Valid)
                    continue;
                transform.ValueRW.Position = command.ValueRO.Position;
                transform.ValueRW.Rotation = quaternion.RotateY(command.ValueRO.Yaw);
            }
        }
    }

    /// <summary>
    /// Client: hides our own NetPlayer (we already see our real body), and remote ones until their owner has
    /// reported a position.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial struct NetPlayerVisibilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetPlayer>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var commands = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<NetPlayer>()
                         .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).WithEntityAccess())
            {
                var local = SystemAPI.IsComponentEnabled<GhostOwnerIsLocal>(entity);
                // A remote player stays hidden at the origin until its first real pose arrives.
                var hide = local || math.lengthsq(transform.ValueRO.Position) < 1e-6f;
                var hidden = SystemAPI.HasComponent<DisableRendering>(entity);
                if (hide == hidden)
                    continue;
                if (SystemAPI.HasBuffer<LinkedEntityGroup>(entity))
                {
                    foreach (var linked in SystemAPI.GetBuffer<LinkedEntityGroup>(entity))
                        if (hide) commands.AddComponent<DisableRendering>(linked.Value);
                        else commands.RemoveComponent<DisableRendering>(linked.Value);
                }
                else if (hide) commands.AddComponent<DisableRendering>(entity);
                else commands.RemoveComponent<DisableRendering>(entity);
            }
            commands.Playback(state.EntityManager);
        }
    }
}
