using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-126 row 4: drives each creature a player rides and steers, where the creature is simulated (solo, or the
    /// server). The player moves as it always does, with the creature's abilities (PlayerRide, PlayerBodyAbilities.Steered),
    /// and the creature is placed where the player stands and given its pace and gait the way the player's own body is
    /// (PlayerAvatarSystem.Drive): Mode Puppet, Holder the rider's holder id, so the brain leaves it be. Solo the rider
    /// is the local player (PlayerRide.Steering); on the server it's each NetPlayer whose look names a creature ghost,
    /// at the pose its owner sent (client-authoritative, as player movement is). Nothing new goes over the wire: the
    /// creature's position, mode and holder replicate on its ghost as before.
    /// A creature left alone (Held or Ride: picked up or riding something) isn't driven, nor one that went its own way
    /// this frame (swimming, say): the rider's PlayerRideSystem sees that (Riding.Reason) and gets down. One no player
    /// steers any more is put down on its feet (Interactions.PutDown) for the brain to take back.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [UpdateBefore(typeof(CreatureNetWriteSystem))]
    public partial struct SteeredMountSystem : ISystem
    {
        /// <summary>Tests: creatures driven by a rider last frame.</summary>
        public static int Steered;

        NativeHashSet<Entity> driven;
        Random rng;

        public void OnCreate(ref SystemState state)
        {
            driven = new NativeHashSet<Entity>(4, Allocator.Persistent);
            rng = new Random(0x51EEu);
            state.RequireForUpdate<TerrainSource>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (driven.IsCreated)
                driven.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var server = state.WorldUnmanaged.IsServer();
            // Who steers what this frame: (mount, holder, the rider's pose, grounded).
            var mounts = new NativeList<Entity>(2, Allocator.Temp);
            var holders = new NativeList<int>(2, Allocator.Temp);
            var poses = new NativeList<LocalTransform>(2, Allocator.Temp);
            var grounds = new NativeList<bool>(2, Allocator.Temp);
            if (server)
            {
                NativeHashMap<int, Entity> creatures = default;
                foreach (var (look, owner, transform) in SystemAPI.Query<RefRO<NetPlayerLook>, RefRO<GhostOwner>, RefRO<LocalTransform>>().WithAll<NetPlayer>())
                {
                    if (look.ValueRO.Mount == 0)
                        continue;
                    if (!creatures.IsCreated)
                    {
                        // (Creature ghosts only: a NetPlayer has no CreatureState here, so a player ridden steers itself.)
                        creatures = new NativeHashMap<int, Entity>(256, Allocator.Temp);
                        foreach (var (ghost, e) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<CreatureState, CreatureBody>().WithEntityAccess())
                            creatures[ghost.ValueRO.ghostId] = e;
                    }
                    if (!creatures.TryGetValue(look.ValueRO.Mount, out var mount))
                        continue;
                    mounts.Add(mount);
                    holders.Add(Interactions.HolderId(owner.ValueRO.NetworkId));
                    poses.Add(transform.ValueRO);
                    grounds.Add(look.ValueRO.Grounded);
                }
            }
            else
            {
                foreach (var (ride, transform, entity) in SystemAPI.Query<RefRO<PlayerRide>, RefRO<LocalTransform>>().WithAll<PlayerTag>().WithEntityAccess())
                {
                    if (!ride.ValueRO.Steering || !em.HasComponent<CreatureState>(ride.ValueRO.Mount))
                        continue;
                    mounts.Add(ride.ValueRO.Mount);
                    holders.Add(Interactions.HolderId(0));
                    poses.Add(transform.ValueRO);
                    grounds.Add(!em.HasComponent<PlayerMotion>(entity) || em.GetComponentData<PlayerMotion>(entity).Grounded);
                }
            }
            if (mounts.Length == 0 && driven.Count == 0)
                return;

            // (The brain and the rest write creature states in jobs: done with them first.)
            state.CompleteDependency();
            var dt = math.max(SystemAPI.Time.DeltaTime, 1e-4f);
            var now = new NativeHashSet<Entity>(mounts.Length + 1, Allocator.Temp);
            for (int i = 0; i < mounts.Length; i++)
            {
                var mount = mounts[i];
                if (!now.Add(mount))
                    continue;   // (one rider per creature: the seat rule already sees to it)
                var s = em.GetComponentData<CreatureState>(mount);
                if (s.Gone || s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Ride)
                    continue;
                // (Steered last frame and something else took it since: left so the rider gets down for it.)
                if (s.Holder == holders[i] && s.Mode != CreatureMode.Puppet)
                    continue;
                var pose = poses[i];
                if (!math.all(math.isfinite(pose.Position)))
                    continue;
                var body = em.GetComponentData<CreatureBody>(mount);
                ref var b = ref body.Blob.Value;
                PlayerAvatarSystem.Drive(ref s, ref b, pose, grounds[i], dt);
                s.Holder = holders[i];
                s.Vertical = s.Lie = s.Sit = s.Fly = 0f;
                em.SetComponentData(mount, s);
            }
            Steered = now.Count;

            // Let go of what no one steers now.
            if (driven.Count > 0)
            {
                var terrain = SystemAPI.GetSingleton<TerrainSource>();
                foreach (var mount in driven)
                {
                    if (now.Contains(mount) || !em.Exists(mount) || !em.HasComponent<CreatureState>(mount))
                        continue;
                    var s = em.GetComponentData<CreatureState>(mount);
                    if (s.Mode != CreatureMode.Puppet || s.Holder <= 0)
                        continue;
                    Interactions.PutDown(ref s, terrain, s.Position, ref rng);
                    em.SetComponentData(mount, s);
                }
            }
            driven.Clear();
            foreach (var mount in now)
                driven.Add(mount);
        }
    }
}
