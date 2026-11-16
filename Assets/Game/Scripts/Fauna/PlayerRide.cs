using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-104 row 3: the local player is riding (on the PlayerTag entity, this machine only). Mount is the body it rides
    /// here: a creature, or another player's body (a RemoteAvatar); MountGhost is what goes to the others in
    /// NetPlayerLook.Mount: the creature's ghost id, or that player's NetPlayer ghost id (0 solo).
    /// While it's on, PlayerMovementSystem leaves the player be and PlayerRideSystem carries it, unless it steers.
    /// A-126 row 4: on a creature (one no player steers) the player steers: it moves with the creature's abilities
    /// (PlayerBody from PlayerBodyAbilities.Steered; its own kept in RiderBody till it gets down), the player standing
    /// where the creature stands, and the creature is driven from it (SteeredMountSystem, as the player's own body is
    /// by PlayerAvatarSystem). Last..Phase: the creature as this machine last drove it (a client redraws it ahead of
    /// its ghost from these).
    /// </summary>
    public struct PlayerRide : IComponentData
    {
        public Entity Mount;
        public int MountGhost;
        public bool Steering;
        public PlayerBody RiderBody;
        /// <summary>P-43: the abilities we set while steering (Steered); a PlayerBody that differs is the rider's new own
        /// body (its species or build changed mid-ride), taken into RiderBody.</summary>
        public PlayerBody Steer;
        public float3 Last;
        public float Yaw, YawRate, Sway, Speed, Phase;
    }

    /// <summary>
    /// A-104 row 3: what the interact button asked of riding this frame (InteractionInputSystem, on the PlayerTag
    /// entity; PlayerRideSystem takes it off): get on Target, or get down (Off).
    /// </summary>
    public struct PlayerRideAsk : IComponentData
    {
        public Entity Target;
        public bool Off;
    }

    /// <summary>
    /// A-104 row 3: players ride. On the player's machine (solo, or each client, the host's included): gets the local
    /// player on a creature or another player's body when the interact button asks and the mass rule allows (Riding.Check),
    /// then every frame, after the mount has moved as it always does, puts the player's body on the mount's seat
    /// (Riding.Begin, as an NPC rider is) and the player where its body is, facing as the mount faces. The camera and the
    /// look stay the player's own; moving does nothing (PlayerMovementSystem skips a rider), except on a creature, which
    /// the player steers (A-126 row 4: see PlayerRide; jump jumps then, only the button gets down). Gets down, falling clear
    /// (Riding.Clear), on the button or jump and when the mount swims, climbs, sleeps, fights, is picked up or rides
    /// (Riding.Reason; a player's body: climbing, flying, lying down or riding), and where it is when the mount is gone.
    /// Also seats each other player's body on what they ride (NetPlayerLook.Mount) as this machine draws it: a creature
    /// ghost, a third player's body, or our own; an id that doesn't resolve (gone, itself, a pair riding each other, a
    /// chain) leaves that body on its own pose, so nothing warps. Network ownership never changes: each player sends its
    /// own pose, the ridden one steering.
    /// P-43 (#396, was RideFitSystem): bodies change mid-ride. A ridden player's new body (NetAvatars respawns its puppet)
    /// is found again by its NetPlayer ghost id (PlayerRide.MountGhost); the rider's own new body, while it steers, goes
    /// into RiderBody (the mount's abilities set again), so getting down gives back the body it has now. Then every frame
    /// the mass rule of getting on (Riding.Check: TooHeavy, Winged) holds or the rider gets down (RideOff.Size).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerAvatarSystem))]
    [UpdateAfter(typeof(InteractionInputSystem))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [UpdateAfter(typeof(RideSystem))]
    [UpdateAfter(typeof(CreatureReplicaSystem))]
    [UpdateAfter(typeof(SteeredMountSystem))]
    public partial struct PlayerRideSystem : ISystem
    {
        /// <summary>Tests (-rideplayer): the last way down (RideOff, None for the button), and the rides begun and ended.</summary>
        public static RideOff LastOff;
        public static int Begun, Ended;
        /// <summary>Tests: the last refusal (RideRefusal, or 100+ for a player's own reasons: 101 rides, 102 ridden, 103 taken).</summary>
        public static int LastRefusal;
        /// <summary>Tests (-ridefit): rides ended as the bodies no longer fit and the last reason, ridden bodies found
        /// again, and rider bodies taken in while steering.</summary>
        public static int Unfit, Refound, Refreshed;
        public static RideRefusal LastUnfit;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainSource>();
            state.RequireForUpdate<PlayerTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            // (Nothing to do while we're on foot and no other player rides: no wait on the creatures' jobs then. A-112:
            // this was "while no other player is near", so with anyone else in the game it waited every frame. A look
            // naming a mount is all SeatOthers acts on, and reading the looks waits on nothing but their own writers.)
            if (!em.HasComponent<PlayerRideAsk>(player) && !em.HasComponent<PlayerRide>(player))
            {
                var othersRide = false;
                foreach (var look in SystemAPI.Query<RefRO<NetPlayerLook>>())
                    othersRide |= look.ValueRO.Mount != 0;
                if (!othersRide)
                    return;
            }
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            SystemAPI.TryGetSingletonEntity<PlayerAvatar>(out var avatar);
            if (avatar != Entity.Null && (!em.HasComponent<CreatureState>(avatar) || !em.HasComponent<CreatureBody>(avatar)))
                avatar = Entity.Null;
            state.CompleteDependency();
            // Our own NetPlayer ghost id (0 solo), and every player's body by its NetPlayer ghost id.
            var me = 0;
            foreach (var (ghost, _) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<NetPlayer>>().WithAll<GhostOwnerIsLocal>())
                me = ghost.ValueRO.ghostId;
            var bodies = new NativeHashMap<int, Entity>(8, Allocator.Temp);
            foreach (var (remote, e) in SystemAPI.Query<RefRO<RemoteAvatar>>().WithAll<CreatureState, CreatureBody>().WithEntityAccess())
                if (em.HasComponent<GhostInstance>(remote.ValueRO.Ghost))
                    bodies[em.GetComponentData<GhostInstance>(remote.ValueRO.Ghost).ghostId] = e;
            if (me != 0 && avatar != Entity.Null)
                bodies[me] = avatar;
            if (em.HasComponent<PlayerRide>(player))
            {
                // (First, so any way down this frame has the bodies as they are now.)
                SystemAPI.TryGetSingletonEntity<PlayerBody>(out var abilities);
                KeepUp(em, player, bodies, abilities);
            }

            if (em.HasComponent<PlayerRideAsk>(player))
            {
                var ask = em.GetComponentData<PlayerRideAsk>(player);
                em.RemoveComponent<PlayerRideAsk>(player);
                if (ask.Off && em.HasComponent<PlayerRide>(player))
                    GetDown(em, player, avatar, RideOff.None, terrain);
                else if (!ask.Off && !em.HasComponent<PlayerRide>(player) && avatar != Entity.Null)
                    GetOn(em, player, avatar, ask.Target, me, terrain);
            }

            if (em.HasComponent<PlayerRide>(player))
                Carry(em, player, avatar, me, terrain, state.WorldUnmanaged.IsClient(), SystemAPI.Time.DeltaTime);

            SeatOthers(em, bodies, me, player, terrain);
        }

        /// <summary>
        /// P-43: the ride kept up with changed bodies. A ridden player's body replaced: its new one, by its NetPlayer ghost
        /// id. Steering, a PlayerBody other than the one we set: the rider's new own body, kept in RiderBody, and the
        /// mount's abilities set again with its breath.
        /// </summary>
        static void KeepUp(EntityManager em, Entity player, NativeHashMap<int, Entity> bodies, Entity abilities)
        {
            var ride = em.GetComponentData<PlayerRide>(player);
            var changed = false;
            if (!ride.Steering && ride.MountGhost != 0 && !em.HasComponent<CreatureState>(ride.Mount) &&
                bodies.TryGetValue(ride.MountGhost, out var body) && em.HasComponent<RemoteAvatar>(body))
            {
                ride.Mount = body;
                changed = true;
                Refound++;
                Debug.Log($"[ride] mount's body changed: riding its new one (ghost {ride.MountGhost})");
            }
            if (ride.Steering && abilities != Entity.Null)
            {
                var now = em.GetComponentData<PlayerBody>(abilities);
                if (!Same(now, ride.Steer))
                {
                    ride.RiderBody = now;
                    if (em.HasComponent<CreatureBody>(ride.Mount) && em.HasComponent<CreatureState>(ride.Mount))
                    {
                        var mBody = em.GetComponentData<CreatureBody>(ride.Mount);
                        ride.Steer = PlayerBodyAbilities.Steered(ref mBody.Blob.Value, em.GetComponentData<CreatureState>(ride.Mount).Scale, now);
                        em.SetComponentData(abilities, ride.Steer);
                    }
                    changed = true;
                    Refreshed++;
                    Debug.Log($"[ride] rider's body changed while steering: size {now.Size:F2}, speed {now.SpeedFactor:F2} kept for getting down");
                }
            }
            if (changed)
                em.SetComponentData(player, ride);
        }

        static bool Same(in PlayerBody a, in PlayerBody b) =>
            a.SpeedFactor == b.SpeedFactor && a.JumpFactor == b.JumpFactor && a.Flies == b.Flies && a.FlySpeed == b.FlySpeed &&
            a.ClimbSkill == b.ClimbSkill && a.Size == b.Size && a.Swims == b.Swims && a.Hovers == b.Hovers &&
            a.ClimbSpeed == b.ClimbSpeed && a.BreathesWater == b.BreathesWater && a.HoldBreath == b.HoldBreath;

        /// <summary>Gets the player on target if it may ride it (logged either way).</summary>
        static void GetOn(EntityManager em, Entity player, Entity avatar, Entity target, int me, in TerrainSource terrain)
        {
            if (target == avatar || !em.HasComponent<CreatureState>(target) || !em.HasComponent<CreatureBody>(target))
            {
                Refuse((int)RideRefusal.Self, target);
                return;
            }
            var rBody = em.GetComponentData<CreatureBody>(avatar);
            var mBody = em.GetComponentData<CreatureBody>(target);
            var r = em.GetComponentData<CreatureState>(avatar);
            var m = em.GetComponentData<CreatureState>(target);
            var refusal = (int)Riding.Check(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m);
            var ghost = 0;
            if (em.HasComponent<RemoteAvatar>(target))
            {
                // Another player: by its NetPlayer; not one that rides (a chain, or us: a pair riding each other).
                var net = em.GetComponentData<RemoteAvatar>(target).Ghost;
                ghost = em.HasComponent<GhostInstance>(net) ? em.GetComponentData<GhostInstance>(net).ghostId : 0;
                if (refusal == 0 && (ghost == 0 || !em.HasComponent<NetPlayerLook>(net) || em.GetComponentData<NetPlayerLook>(net).Mount != 0))
                    refusal = 101;
            }
            else
                ghost = em.HasComponent<GhostInstance>(target) ? em.GetComponentData<GhostInstance>(target).ghostId : 0;
            // (Not while someone rides us, and not onto a seat someone else has: another player, or an animal riding it.)
            if (refusal == 0 && Taken(em, me, ghost, out var why))
                refusal = why;
            if (refusal != 0)
            {
                Refuse(refusal, target);
                return;
            }
            Riding.Begin(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, ghost, terrain);
            em.SetComponentData(avatar, r);
            var ride = new PlayerRide
            {
                Mount = target, MountGhost = ghost,
                // (A-126 row 4: a creature is steered; another player steers itself, A-104.)
                Steering = !em.HasComponent<RemoteAvatar>(target),
                Last = m.Position, Yaw = m.Yaw, Phase = m.Phase
            };
            if (ride.Steering)
            {
                // The player takes on the creature's abilities and stands where it stands.
                using var abilityQuery = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerBody>());
                if (abilityQuery.TryGetSingletonEntity<PlayerBody>(out var bodyEntity))
                {
                    ride.RiderBody = em.GetComponentData<PlayerBody>(bodyEntity);
                    ride.Steer = PlayerBodyAbilities.Steered(ref mBody.Blob.Value, m.Scale, ride.RiderBody);
                    em.SetComponentData(bodyEntity, ride.Steer);
                }
                else
                    ride.RiderBody = new PlayerBody { SpeedFactor = 1f, JumpFactor = 1f, Size = 1f };
                Hold(em, player, m.Position, m.Yaw);
            }
            else
                Hold(em, player, r.Position, m.Yaw);
            em.AddComponentData(player, ride);
            Begun++;
            Debug.Log($"[ride] player on {(em.HasComponent<RemoteAvatar>(target) ? "a player" : "a creature")} (ghost {ghost}), pose {(RidePose)r.Stage}, " +
                      $"{(ride.Steering ? "steering" : "carried")}, at ({r.Position.x:F2}, {r.Position.y:F2}, {r.Position.z:F2})");
        }

        static void Refuse(int why, Entity target)
        {
            LastRefusal = why;
            Debug.Log($"[ride] player refused ({(why >= 100 ? why == 101 ? "it rides" : why == 102 ? "ridden" : "seat taken" : ((RideRefusal)why).ToString())})");
        }

        /// <summary>Whether someone rides us (102) or the mount's seat is taken (103): by another player, or by an animal.</summary>
        static bool Taken(EntityManager em, int me, int mountGhost, out int why)
        {
            why = 0;
            using var looks = em.CreateEntityQuery(ComponentType.ReadOnly<NetPlayerLook>(), ComponentType.ReadOnly<GhostInstance>());
            using var list = looks.ToComponentDataArray<NetPlayerLook>(Allocator.Temp);
            using var ids = looks.ToComponentDataArray<GhostInstance>(Allocator.Temp);
            for (int i = 0; i < list.Length; i++)
            {
                if (ids[i].ghostId == me)
                    continue;
                if (me != 0 && list[i].Mount == me)
                    why = 102;
                else if (mountGhost != 0 && list[i].Mount == mountGhost)
                    why = 103;
            }
            if (why == 0 && mountGhost != 0)
            {
                using var riders = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<GhostInstance>());
                using var states = riders.ToComponentDataArray<CreatureState>(Allocator.Temp);
                for (int i = 0; i < states.Length; i++)
                    if (states[i].Mode == CreatureMode.Ride && states[i].Holder == -mountGhost)
                        why = 103;
            }
            return why != 0;
        }

        /// <summary>
        /// One frame of a ride: down if the mount says so, else the player's body on its seat. Carried, the player is
        /// held there with it; steering, the player has moved and the creature stands where the player is
        /// (SteeredMountSystem drove it solo and on the host; a client drives it here too, ahead of its ghost, so it
        /// doesn't trail behind the camera).
        /// </summary>
        static void Carry(EntityManager em, Entity player, Entity avatar, int me, in TerrainSource terrain, bool client, float dt)
        {
            var ride = em.GetComponentData<PlayerRide>(player);
            if (avatar == Entity.Null || !em.HasComponent<CreatureState>(ride.Mount) || !em.HasComponent<CreatureBody>(ride.Mount))
            {
                GetDown(em, player, avatar, RideOff.Death, terrain);
                return;
            }
            var mBody = em.GetComponentData<CreatureBody>(ride.Mount);
            var m = em.GetComponentData<CreatureState>(ride.Mount);
            var why = Riding.Reason(ref mBody.Blob.Value, m, new Rider { Mount = ride.Mount, Kind = RideKind.Placed }, terrain);
            if (why == RideOff.None && em.HasComponent<RemoteAvatar>(ride.Mount))
            {
                // Another player's body: off when it climbs, flies, lies down or rides something itself.
                var net = em.GetComponentData<RemoteAvatar>(ride.Mount).Ghost;
                var look = em.HasComponent<NetPlayerLook>(net) ? em.GetComponentData<NetPlayerLook>(net) : default;
                if (!em.HasComponent<NetPlayerLook>(net))
                    why = RideOff.Death;
                else if (look.Climbing || m.Vertical > 0.5f || look.Rim > 0)
                    why = RideOff.Climb;
                else if (m.Lie > 0.5f)
                    why = RideOff.Sleep;
                else if (look.Mount != 0)
                    why = RideOff.Held;
            }
            if (why == RideOff.None)
            {
                // P-43: the mass rule of getting on, so a body changed either way puts the rider down once it no longer
                // holds (Busy is Riding.Reason's to judge: only the bodies here).
                var fitBody = em.GetComponentData<CreatureBody>(avatar);
                var fitState = em.GetComponentData<CreatureState>(avatar);
                var fit = Riding.Check(ref fitBody.Blob.Value, fitState, ref mBody.Blob.Value, m);
                if (fit == RideRefusal.TooHeavy || fit == RideRefusal.Winged)
                {
                    why = RideOff.Size;
                    Unfit++;
                    LastUnfit = fit;
                    Debug.Log($"[ride] player no longer fits its mount ({fit}): rider {fitBody.Blob.Value.WeightAt(fitState.Scale):F1} kg x {Riding.MassRatio}, " +
                              $"mount {mBody.Blob.Value.WeightAt(m.Scale):F1} kg");
                }
            }
            if (why != RideOff.None)
            {
                GetDown(em, player, avatar, why, terrain);
                return;
            }
            if (ride.Steering)
            {
                if (client)
                {
                    // (From where we last drove it, not its ghost a few frames behind, so its gait runs with our pace.)
                    var t = em.GetComponentData<LocalTransform>(player);
                    m.Position = ride.Last;
                    m.Yaw = ride.Yaw;
                    m.YawRate = ride.YawRate;
                    m.Sway = ride.Sway;
                    m.Speed = m.GaitSpeed = ride.Speed;
                    m.Phase = ride.Phase;
                    var grounded = !em.HasComponent<PlayerMotion>(player) || em.GetComponentData<PlayerMotion>(player).Grounded;
                    PlayerAvatarSystem.Drive(ref m, ref mBody.Blob.Value, t, grounded, math.max(dt, 1e-4f));
                    em.SetComponentData(ride.Mount, m);
                }
                ride.Last = m.Position;
                ride.Yaw = m.Yaw;
                ride.YawRate = m.YawRate;
                ride.Sway = m.Sway;
                ride.Speed = m.Speed;
                ride.Phase = m.Phase;
                em.SetComponentData(player, ride);
            }
            var rBody = em.GetComponentData<CreatureBody>(avatar);
            var r = em.GetComponentData<CreatureState>(avatar);
            Riding.Begin(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, ride.MountGhost, terrain);
            // (A-119: the pair's size into both; m is the mount as it stands now, read or driven above.)
            Riding.Pair(ref rBody.Blob.Value, ref r, ref mBody.Blob.Value, ref m);
            em.SetComponentData(ride.Mount, m);
            em.SetComponentData(avatar, r);
            if (!ride.Steering)
                Hold(em, player, r.Position, m.Yaw);
        }

        /// <summary>The player where its body sits, facing as the mount faces, held still (no fall, no climb, no flight).</summary>
        static void Hold(EntityManager em, Entity player, float3 at, float yaw)
        {
            if (!math.all(math.isfinite(at)))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = at;
            t.Rotation = quaternion.RotateY(yaw);
            em.SetComponentData(player, t);
            if (em.HasComponent<PlayerMotion>(player))
            {
                var motion = em.GetComponentData<PlayerMotion>(player);
                motion.Grounded = true;
                motion.Flying = motion.Flapping = false;
                motion.VerticalVelocity = 0f;
                motion.OnTrunk = motion.OnWall = false;
                motion.Mantle = motion.Roll = 0f;
                em.SetComponentData(player, motion);
            }
        }

        /// <summary>Gets the player down: clear of its mount (Riding.Clear), or where it is when the mount is gone.</summary>
        static void GetDown(EntityManager em, Entity player, Entity avatar, RideOff why, in TerrainSource terrain)
        {
            var ride = em.GetComponentData<PlayerRide>(player);
            em.RemoveComponent<PlayerRide>(player);
            Ended++;
            if (ride.Steering)
            {
                // The player's own abilities back (SteeredMountSystem lets the creature go once no one steers it).
                using var abilityQuery = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerBody>());
                if (abilityQuery.TryGetSingletonEntity<PlayerBody>(out var bodyEntity))
                    em.SetComponentData(bodyEntity, ride.RiderBody);
            }
            LastOff = why;
            var t = em.GetComponentData<LocalTransform>(player);
            var at = t.Position;
            var side = -1;
            var room = 0f;
            if (avatar != Entity.Null)
            {
                var rBody = em.GetComponentData<CreatureBody>(avatar);
                var r = em.GetComponentData<CreatureState>(avatar);
                if (em.HasComponent<CreatureState>(ride.Mount) && em.HasComponent<CreatureBody>(ride.Mount))
                {
                    var mBody = em.GetComponentData<CreatureBody>(ride.Mount);
                    var m = em.GetComponentData<CreatureState>(ride.Mount);
                    using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.FloraLookup>());
                    var flora = query.TryGetSingleton<Punchy.Flora.FloraLookup>(out var f) ? f.Blob : default;
                    at = Riding.Clear(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m, terrain, flora, out side);
                    Riding.Down(ref r, at, terrain);
                    room = Riding.Room(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m);
                }
                else
                    Riding.Down(ref r, at, terrain);
                em.SetComponentData(avatar, r);
                at = r.Position;
            }
            else
                at.y = terrain.FloorUnder(at + new float3(0f, 0.5f, 0f), out _);
            if (math.all(math.isfinite(at)))
            {
                t.Position = at;
                em.SetComponentData(player, t);
            }
            var floor = at.y - terrain.FloorUnder(at + new float3(0f, 0.5f, 0f), out _);
            Debug.Log($"[ride] player down ({(why == RideOff.None ? "asked" : why.ToString())}): side {side}, room {room:F2} m, floor {floor:F2} m, " +
                      $"at ({at.x:F2}, {at.y:F2}, {at.z:F2})");
        }

        /// <summary>
        /// Seats every other player's body that rides (NetPlayerLook.Mount) on its mount as drawn here: a creature ghost,
        /// another player's body, or ours. Skipped (left on its own pose) when the id doesn't resolve, names itself, or
        /// names a body that rides too (a pair riding each other, or a chain).
        /// </summary>
        static void SeatOthers(EntityManager em, NativeHashMap<int, Entity> bodies, int me, Entity player, in TerrainSource terrain)
        {
            if (bodies.Count == 0)
                return;
            NativeHashMap<int, Entity> creatures = default;
            foreach (var pair in bodies)
            {
                var self = pair.Key;
                var body = pair.Value;
                if (self == me || !em.HasComponent<RemoteAvatar>(body))
                    continue;
                var net = em.GetComponentData<RemoteAvatar>(body).Ghost;
                var mount = em.HasComponent<NetPlayerLook>(net) ? em.GetComponentData<NetPlayerLook>(net).Mount : 0;
                if (mount == 0 || mount == self)
                    continue;
                if (!bodies.TryGetValue(mount, out var mountBody))
                {
                    if (!creatures.IsCreated)
                    {
                        creatures = new NativeHashMap<int, Entity>(256, Allocator.Temp);
                        using var query = em.CreateEntityQuery(ComponentType.ReadOnly<GhostInstance>(), ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                        using var entities = query.ToEntityArray(Allocator.Temp);
                        using var ids = query.ToComponentDataArray<GhostInstance>(Allocator.Temp);
                        for (int i = 0; i < entities.Length; i++)
                            creatures[ids[i].ghostId] = entities[i];
                    }
                    if (!creatures.TryGetValue(mount, out mountBody))
                        continue;
                }
                else
                {
                    // (A player that rides isn't a mount: our own body while we ride, or another's that names a mount.)
                    if (mount == me ? em.HasComponent<PlayerRide>(player) : RidesToo(em, mountBody))
                        continue;
                }
                var rBody = em.GetComponentData<CreatureBody>(body);
                var mBody = em.GetComponentData<CreatureBody>(mountBody);
                var r = em.GetComponentData<CreatureState>(body);
                var m = em.GetComponentData<CreatureState>(mountBody);
                if (m.Mode == CreatureMode.Ride || m.Gone)
                    continue;
                Riding.Begin(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, mount, terrain);
                if (math.all(math.isfinite(r.Position)))
                {
                    Riding.Pair(ref rBody.Blob.Value, ref r, ref mBody.Blob.Value, ref m);
                    em.SetComponentData(mountBody, m);
                    em.SetComponentData(body, r);
                }
            }
        }

        static bool RidesToo(EntityManager em, Entity body)
        {
            if (!em.HasComponent<RemoteAvatar>(body))
                return false;
            var net = em.GetComponentData<RemoteAvatar>(body).Ghost;
            return em.HasComponent<NetPlayerLook>(net) && em.GetComponentData<NetPlayerLook>(net).Mount != 0;
        }
    }
}
