using Punchy.Fauna;
using Punchy.Net;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Player
{
    /// <summary>
    /// P-40 (#396): a rider whose body no longer fits its mount gets down. On the player's machine (solo, or each client,
    /// the host's included), every frame the local player rides: the same mass rule as getting on (Riding.Check: the
    /// rider at most two thirds of the mount, no winged mount), so a size change either way (the rider's body switched,
    /// or the ridden player's) puts the rider down when it no longer holds, and keeps it on when it still does.
    /// A ridden player whose body changes gets a new body entity here (NetAvatars respawns the puppet), so the ride's
    /// Mount is first found again by its NetPlayer ghost id (PlayerRide.MountGhost); without that the rider fell off
    /// whatever its new size. The way down is the button's own (a PlayerRideAsk Off for PlayerRideSystem).
    /// Creatures don't change size once spawned, so creature riders (RideSystem) need no such check today.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerAvatarSystem))]
    [UpdateAfter(typeof(InteractionInputSystem))]
    [UpdateBefore(typeof(PlayerRideSystem))]
    public partial struct RideFitSystem : ISystem
    {
        /// <summary>Tests (-ridefit): rides ended because the bodies no longer fit, the last reason, and mounts found again.</summary>
        public static int Dropped, Refound;
        public static RideRefusal LastReason;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<PlayerRide>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            if (!em.HasComponent<PlayerRide>(player))
                return;
            state.CompleteDependency();
            var ride = em.GetComponentData<PlayerRide>(player);
            if (!em.HasComponent<CreatureState>(ride.Mount) && ride.MountGhost != 0)
            {
                // The ridden player's body was replaced (a new species, coat or build): the same player's new one.
                foreach (var (remote, e) in SystemAPI.Query<RefRO<RemoteAvatar>>().WithAll<CreatureState, CreatureBody>().WithEntityAccess())
                {
                    var net = remote.ValueRO.Ghost;
                    if (em.HasComponent<GhostInstance>(net) && em.GetComponentData<GhostInstance>(net).ghostId == ride.MountGhost)
                    {
                        ride.Mount = e;
                        em.SetComponentData(player, ride);
                        Refound++;
                        Debug.Log($"[ride] mount's body changed: riding its new one (ghost {ride.MountGhost})");
                        break;
                    }
                }
            }
            if (!SystemAPI.TryGetSingletonEntity<PlayerAvatar>(out var avatar) || !em.HasComponent<CreatureState>(avatar) ||
                !em.HasComponent<CreatureBody>(avatar) || !em.HasComponent<CreatureState>(ride.Mount) || !em.HasComponent<CreatureBody>(ride.Mount))
                return;
            var rBody = em.GetComponentData<CreatureBody>(avatar);
            var mBody = em.GetComponentData<CreatureBody>(ride.Mount);
            var r = em.GetComponentData<CreatureState>(avatar);
            var m = em.GetComponentData<CreatureState>(ride.Mount);
            // (Busy is PlayerRideSystem's to judge, by its own reasons: only the bodies themselves here.)
            var why = Riding.Check(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m);
            if (why != RideRefusal.TooHeavy && why != RideRefusal.Winged)
                return;
            var ask = new PlayerRideAsk { Off = true };
            if (em.HasComponent<PlayerRideAsk>(player))
                em.SetComponentData(player, ask);
            else
                em.AddComponentData(player, ask);
            Dropped++;
            LastReason = why;
            Debug.Log($"[ride] player no longer fits its mount ({why}): rider {rBody.Blob.Value.WeightAt(r.Scale):F1} kg x {Riding.MassRatio}, " +
                      $"mount {mBody.Blob.Value.WeightAt(m.Scale):F1} kg");
        }
    }
}
