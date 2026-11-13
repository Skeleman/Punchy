using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-104: a creature riding another (sim world only: the mount it rides). Added by Riding.Mount, taken off by
    /// Riding.Dismount (or by RideSystem when the mount is gone).
    /// </summary>
    public struct Rider : IComponentData
    {
        public Entity Mount;
    }

    /// <summary>How a rider sits on its mount (in CreatureState.Stage while riding, A-104).</summary>
    public enum RidePose : byte
    {
        /// <summary>An upright body astride a four-legged one, legs down its flanks.</summary>
        Straddle,
        /// <summary>An upright body on an upright one's shoulders, legs forward round its neck (a child on an adult).</summary>
        Shoulders,
        /// <summary>Lying flat along the mount's back, holding on (a four-legged body on a much smaller-backed one, or on an upright one).</summary>
        Cling,
        /// <summary>Sitting up on the mount's back (a small four-legged body on a much bigger one: a cat on a dog).</summary>
        Sit
    }

    /// <summary>Why a creature may not ride another (Riding.Check).</summary>
    public enum RideRefusal : byte
    {
        None,
        /// <summary>The rider weighs more than the mount can carry (rider x MassRatio > mount).</summary>
        TooHeavy,
        /// <summary>The mount has wings (between the wings is off for now).</summary>
        Winged,
        /// <summary>The mount is itself riding, or carried.</summary>
        MountBusy,
        /// <summary>A creature can't ride itself.</summary>
        Self
    }

    /// <summary>
    /// A-104: riding. Any creature can ride another at least half again as heavy (rider x 1.5 &lt;= mount, by WeightAt,
    /// so a child on an adult, a cat on a dog). The mount moves as it always does; the rider has no mind of its own
    /// while it rides (the brain skips it) and is put on the mount's seat every frame, after the mount has moved, by
    /// posing both bodies as the renderer will: the seat is the top of a four-legged mount's back, or an upright one's
    /// shoulders (the base of its neck), and the rider's contact (the underside of its body) goes exactly there.
    /// What it rides goes to other machines in CreatureNet.Holder as minus the mount's ghost id (no new ghost field), and
    /// each client re-seats the rider on the mount it draws (RideSeatSystem), so it never lags or jitters on it.
    /// </summary>
    public static class Riding
    {
        /// <summary>A mount must weigh at least this many times its rider.</summary>
        public const float MassRatio = 1.5f;
        /// <summary>CreatureState.Holder of a rider whose mount has no ghost id (solo, or before the first snapshot).</summary>
        public const int SoloMount = int.MinValue + 1;

        /// <summary>Whether this rider may ride this mount, and if not why.</summary>
        public static RideRefusal Check(ref CreatureBlob rider, in CreatureState r, ref CreatureBlob mount, in CreatureState m)
        {
            if (m.Mode == CreatureMode.Ride || m.Mode == CreatureMode.Held)
                return RideRefusal.MountBusy;
            for (int p = 0; p < mount.Parts.Length; p++)
                if (mount.Parts[p].Role == LimbRole.Wing)
                    return RideRefusal.Winged;
            return rider.WeightAt(r.Scale) * MassRatio <= mount.WeightAt(m.Scale) ? RideRefusal.None : RideRefusal.TooHeavy;
        }

        /// <summary>How this rider sits on this mount: by body plan, and for two four-legged bodies by how they compare.</summary>
        public static RidePose PoseFor(ref CreatureBlob rider, in CreatureState r, ref CreatureBlob mount, in CreatureState m)
        {
            if (mount.Upright)
                return rider.Upright ? RidePose.Shoulders : RidePose.Cling;
            if (rider.Upright)
                return RidePose.Straddle;
            // (A small body sits up on a long back; one nearer the mount's own length lies along it.)
            return rider.BodyLength * r.Scale <= 0.45f * mount.BodyLength * m.Scale ? RidePose.Sit : RidePose.Cling;
        }

        /// <summary>
        /// Puts a rider on its mount's seat: facing as the mount faces, its contact on the seat, both bodies posed as the
        /// renderer poses them now. (Twice: the first pass lifts it clear of the ground, which can change its pose.)
        /// </summary>
        public static void Place(ref CreatureBlob rb, ref CreatureState r, int riderCoat, ref CreatureBlob mb, in CreatureState m, int mountCoat,
                                 in TerrainSource terrain)
        {
            var output = new NativeArray<SegmentInstance>(math.max(rb.Segments.Length, mb.Segments.Length) + 64, Allocator.Temp);
            var seat = SeatFor(ref rb, r, ref mb, m, mountCoat, terrain, output);
            r.Yaw = m.Yaw;
            r.YawRate = m.YawRate;
            r.Position = seat;
            // (The pose depends on where the body is, e.g. how high off the ground, so a few passes till it settles.)
            for (int i = 0; i < 4; i++)
            {
                var miss = seat - Contact(ref rb, r, riderCoat, terrain, output);
                r.Position += miss;
                if (math.lengthsq(miss) < 0.0005f * 0.0005f)
                    break;
            }
            output.Dispose();
        }

        /// <summary>How far a rider's contact is from its seat now (m): 0 when it sits exactly where it should (-ridetest).</summary>
        public static float Offset(ref CreatureBlob rb, in CreatureState r, int riderCoat, ref CreatureBlob mb, in CreatureState m, int mountCoat,
                                   in TerrainSource terrain)
        {
            var output = new NativeArray<SegmentInstance>(math.max(rb.Segments.Length, mb.Segments.Length) + 64, Allocator.Temp);
            var d = math.distance(SeatFor(ref rb, r, ref mb, m, mountCoat, terrain, output), Contact(ref rb, r, riderCoat, terrain, output));
            output.Dispose();
            return d;
        }

        /// <summary>The seat for this rider on this mount (Seat, and on the shoulders back behind the neck).</summary>
        static float3 SeatFor(ref CreatureBlob rb, in CreatureState r, ref CreatureBlob mb, in CreatureState m, int mountCoat,
                              in TerrainSource terrain, NativeArray<SegmentInstance> output)
        {
            var seat = Seat(ref mb, m, mountCoat, terrain, output);
            if (r.Stage == (byte)RidePose.Shoulders)
            {
                // (Sitting behind the neck, not on it: back by the rider's hips, so the mount's head is in front of its body.)
                var forward = new float3(math.sin(m.Yaw), 0f, math.cos(m.Yaw));
                seat -= forward * math.max(rb.RearRadius, rb.FrontRadius) * r.Scale * 0.9f;
            }
            return seat;
        }

        /// <summary>Where a mount carries its rider now: the top of the middle of its back, or for an upright body the base
        /// of its neck (its shoulders).</summary>
        public static float3 Seat(ref CreatureBlob b, in CreatureState s, int coat, in TerrainSource terrain, NativeArray<SegmentInstance> output)
        {
            CreatureAnimator.Evaluate(ref b, s, coat, terrain, output, 0, true, false);
            if (b.Upright)
            {
                int neck = -1, head = -1;
                for (int p = 0; p < b.Parts.Length; p++)
                {
                    ref var part = ref b.Parts[p];
                    if (part.SegmentCount == 0)
                        continue;
                    if (part.Role == LimbRole.Neck && neck < 0)
                        neck = p;
                    else if (part.Role == LimbRole.Head && head < 0)
                        head = p;
                }
                var at = neck >= 0 ? neck : head;
                if (at >= 0)
                {
                    var seg = output[b.Parts[at].FirstSegment];
                    return seg.Position;
                }
            }
            var body = output[0];
            return Surface(body, 0.5f, 1f);
        }

        /// <summary>Where a rider touches its seat: the underside of its body (towards the rump, sitting up).</summary>
        public static float3 Contact(ref CreatureBlob b, in CreatureState s, int coat, in TerrainSource terrain, NativeArray<SegmentInstance> output)
        {
            CreatureAnimator.Evaluate(ref b, s, coat, terrain, output, 0, true, false);
            return Surface(output[0], s.Stage == (byte)RidePose.Sit ? 0.15f : 0.5f, -1f);
        }

        /// <summary>The top (up 1) or underside (-1) of a body segment, `t` of the way along it.</summary>
        static float3 Surface(SegmentInstance seg, float t, float up)
        {
            var mid = seg.Position + math.mul(seg.Rotation, new float3(0f, 0f, seg.Length * t));
            var radius = math.lerp(seg.RadiusStart, seg.RadiusEnd, t);
            return mid + math.mul(seg.Rotation, new float3(0f, up * radius * seg.Flatten, 0f));
        }

        /// <summary>
        /// Sets a rider on a mount (sim world, main thread): refused (and nothing changes) unless Check allows it.
        /// </summary>
        public static RideRefusal Mount(EntityManager em, Entity rider, Entity mount, in TerrainSource terrain)
        {
            if (rider == mount)
                return RideRefusal.Self;
            var rBody = em.GetComponentData<CreatureBody>(rider);
            var mBody = em.GetComponentData<CreatureBody>(mount);
            var r = em.GetComponentData<CreatureState>(rider);
            var m = em.GetComponentData<CreatureState>(mount);
            var refusal = Check(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m);
            if (refusal != RideRefusal.None)
                return refusal;
            var pose = PoseFor(ref rBody.Blob.Value, r, ref mBody.Blob.Value, m);
            r.Mode = CreatureMode.Ride;
            r.Stage = (byte)pose;
            r.Sit = pose == RidePose.Cling ? 0f : 1f;
            r.Lie = pose == RidePose.Cling ? 1f : 0f;
            r.Speed = r.GaitSpeed = 0f;
            r.Fly = r.Vertical = 0f;
            r.Holder = em.HasComponent<GhostInstance>(mount) && em.GetComponentData<GhostInstance>(mount).ghostId != 0
                ? -em.GetComponentData<GhostInstance>(mount).ghostId : SoloMount;
            Place(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, terrain);
            em.SetComponentData(rider, r);
            if (em.HasComponent<Rider>(rider))
                em.SetComponentData(rider, new Rider { Mount = mount });
            else
                em.AddComponentData(rider, new Rider { Mount = mount });
            return RideRefusal.None;
        }

        /// <summary>Gets a rider down (sim world, main thread): beside its mount on the floor there, idle.</summary>
        public static void Dismount(EntityManager em, Entity rider, in TerrainSource terrain)
        {
            var r = em.GetComponentData<CreatureState>(rider);
            var right = new float3(math.cos(r.Yaw), 0f, -math.sin(r.Yaw));
            Down(ref r, r.Position + right * 0.6f, terrain);
            em.SetComponentData(rider, r);
            if (em.HasComponent<Rider>(rider))
                em.RemoveComponent<Rider>(rider);
        }

        /// <summary>A rider's state once off its mount, at `at` (dropped to the floor under it).</summary>
        public static void Down(ref CreatureState r, float3 at, in TerrainSource terrain)
        {
            at.y = terrain.FloorUnder(at + new float3(0f, 0.5f, 0f), out _);
            r.Position = at;
            r.Mode = CreatureMode.Idle;
            r.Stage = 0;
            r.Sit = r.Lie = 0f;
            r.Holder = 0;
        }
    }

    /// <summary>
    /// A-104: puts every rider on its mount's seat each frame, after the brain and any push have moved the mounts and
    /// before their state goes out to clients (solo, or on the server). A rider whose mount is gone gets down where it is.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [UpdateAfter(typeof(ColossusPushSystem))]
    [UpdateBefore(typeof(Punchy.Net.CreatureNetWriteSystem))]
    public partial struct RideSystem : ISystem
    {
        ComponentLookup<CreatureState> states;
        ComponentLookup<CreatureBody> bodies;
        ComponentLookup<GhostInstance> ghosts;
        EntityQuery riders;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            riders = SystemAPI.QueryBuilder().WithAll<Rider, CreatureState, CreatureBody>().Build();
            state.RequireForUpdate(riders);
            state.RequireForUpdate<TerrainSource>();
            states = state.GetComponentLookup<CreatureState>(true);
            bodies = state.GetComponentLookup<CreatureBody>(true);
            ghosts = state.GetComponentLookup<GhostInstance>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            states.Update(ref state);
            bodies.Update(ref state);
            ghosts.Update(ref state);
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            state.Dependency = new SeatJob
            {
                States = states,
                Bodies = bodies,
                Ghosts = ghosts,
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Dt = SystemAPI.Time.DeltaTime,
                Commands = ecb
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        partial struct SeatJob : IJobEntity
        {
            // (Mounts are read while riders are written: a mount is never itself riding, Riding.Check.)
            [ReadOnly, NativeDisableContainerSafetyRestriction] public ComponentLookup<CreatureState> States;
            [ReadOnly] public ComponentLookup<CreatureBody> Bodies;
            [ReadOnly] public ComponentLookup<GhostInstance> Ghosts;
            [ReadOnly] public TerrainSource Terrain;
            public float Dt;
            public EntityCommandBuffer Commands;

            void Execute(Entity self, ref CreatureState s, in CreatureBody body, in Rider rider)
            {
                var mount = rider.Mount;
                if (!States.HasComponent(mount) || States[mount].Gone || s.Mode != CreatureMode.Ride)
                {
                    if (s.Mode == CreatureMode.Ride)
                        Riding.Down(ref s, s.Position, Terrain);
                    Commands.RemoveComponent<Rider>(self);
                    return;
                }
                var m = States[mount];
                var mBody = Bodies[mount];
                s.Clock += Dt;
                s.Speed = s.GaitSpeed = 0f;
                var ghostId = Ghosts.HasComponent(mount) ? Ghosts[mount].ghostId : 0;
                s.Holder = ghostId != 0 ? -ghostId : Riding.SoloMount;
                Riding.Place(ref body.Blob.Value, ref s, body.Coat, ref mBody.Blob.Value, m, mBody.Coat, Terrain);
            }
        }
    }

    /// <summary>
    /// A-104: on a client, re-seats each rider on its mount as this machine draws it (the mount's ghost, by the id in
    /// Holder), just before drawing, so the two interpolated ghosts never drift apart. Nothing is sent.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(HeldPoseSystem))]
    [UpdateBefore(typeof(CreatureRenderSystem))]
    public partial struct RideSeatSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureState>();
            state.RequireForUpdate<TerrainSource>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // (Nothing held or ridden in the last copy: nothing to do, and no wait for the replica on the main thread.)
            if (HeldPoseSystem.Needed.Data == 0)
                return;
            // (Each creature ghost's state and body, by ghost id: the mounts are among them.)
            var mounts = new NativeHashMap<int, (CreatureState state, CreatureBody body)>(64, Allocator.Temp);
            var any = false;
            foreach (var (ghost, s, body) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<CreatureState>, RefRO<CreatureBody>>())
            {
                mounts[ghost.ValueRO.ghostId] = (s.ValueRO, body.ValueRO);
                any |= s.ValueRO.Mode == CreatureMode.Ride;
            }
            if (!any)
                return;
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            foreach (var (s, body) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithAll<GhostInstance>())
            {
                if (s.ValueRO.Mode != CreatureMode.Ride || s.ValueRO.Holder >= 0 || s.ValueRO.Holder == Riding.SoloMount ||
                    !mounts.TryGetValue(-s.ValueRO.Holder, out var mount))
                    continue;
                var r = s.ValueRO;
                Riding.Place(ref body.ValueRO.Blob.Value, ref r, body.ValueRO.Coat, ref mount.body.Blob.Value, mount.state, mount.body.Coat, terrain);
                s.ValueRW = r;
            }
            HeldPoseSystem.Ran.Data = 1;
        }
    }
}
