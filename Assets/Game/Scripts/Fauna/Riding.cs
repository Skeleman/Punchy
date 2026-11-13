using Punchy.Flora;
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
    /// A-104: a creature riding another (sim world only: the mount it rides, why, and for how long). Added by
    /// Riding.Mount or RideLifeSystem, taken off by Riding.Dismount or by RideSystem when something gets it down.
    /// </summary>
    public struct Rider : IComponentData
    {
        public Entity Mount;
        /// <summary>Seconds left on this ride (RideSystem counts it down); 0 or less rides till something else gets it down.</summary>
        public float Left;
        public RideKind Kind;
    }

    /// <summary>Why a creature is riding (A-104).</summary>
    public enum RideKind : byte
    {
        /// <summary>Put there (a test, or later a player).</summary>
        Placed,
        /// <summary>A village child on a grown-up's shoulders on the walk home (row 2): down near the door.</summary>
        WalkHome,
        /// <summary>A small pet on a bigger tame animal's back for a while (row 2).</summary>
        Pet
    }

    /// <summary>Why a rider got down (A-104 row 2; RideLog).</summary>
    public enum RideOff : byte
    {
        None,
        /// <summary>The mount is gone: killed, caught or despawned.</summary>
        Death,
        /// <summary>The mount swims, or wades in past its belly.</summary>
        Swim,
        /// <summary>The mount climbs (a trunk, a web, its burrow, a roost) or takes off.</summary>
        Climb,
        /// <summary>The mount lies down to sleep or rest.</summary>
        Sleep,
        /// <summary>The mount hunts, flees or plays chase.</summary>
        Fight,
        /// <summary>Someone picked the mount up.</summary>
        Held,
        /// <summary>A child's ride home: the grown-up is nearly at its door (or has given up going home).</summary>
        Home,
        /// <summary>The ride's time is up.</summary>
        Done
    }

    /// <summary>
    /// A-104 row 2: counts of rides begun and ended, and the last few ended, for the debug tests (-ridelife). Written
    /// only while On, by the one RideSystem or RideLifeSystem job running; read on the main thread.
    /// </summary>
    public struct RideLog
    {
        /// <summary>One ride's end.</summary>
        public struct Off
        {
            public RideOff Why;
            public RideKind Kind;
            /// <summary>Where the rider landed, and where its mount was (its last place, if it's gone).</summary>
            public float3 At, Mount;
            /// <summary>Room between the two bodies sideways (m: centres apart less both radii, above 0 is clear of the
            /// mount); how far the rider stands over the floor there (0 = on it, never below).</summary>
            public float Room, Floor;
            /// <summary>Which side it got down on (Riding.Clear: 0 right, 1 left, 2 behind, 3 in front, 4.. further out;
            /// -1 where it was, the mount being gone from the world).</summary>
            public int Side;
            /// <summary>The rider's Individual.</summary>
            public uint Rider;
        }

        public int On;
        public int Begun, Ended;
        public FixedList512Bytes<Off> Offs;

        public static readonly SharedStatic<RideLog> Data = SharedStatic<RideLog>.GetOrCreate<RideLog, Key>();
        struct Key { }

        /// <summary>Notes a ride's end (when On; the oldest drops off a full list).</summary>
        public static void Note(in Off off)
        {
            ref var log = ref Data.Data;
            if (log.On == 0)
                return;
            log.Ended++;
            if (log.Offs.Length == log.Offs.Capacity)
                log.Offs.RemoveAt(0);
            log.Offs.Add(off);
        }
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
            var ghostId = em.HasComponent<GhostInstance>(mount) ? em.GetComponentData<GhostInstance>(mount).ghostId : 0;
            Begin(ref rBody.Blob.Value, ref r, rBody.Coat, ref mBody.Blob.Value, m, mBody.Coat, ghostId, terrain);
            em.SetComponentData(rider, r);
            if (em.HasComponent<Rider>(rider))
                em.SetComponentData(rider, new Rider { Mount = mount });
            else
                em.AddComponentData(rider, new Rider { Mount = mount });
            return RideRefusal.None;
        }

        /// <summary>A rider's state once on its mount (Check has allowed it): posed for the mount, no mind of its own,
        /// Holder naming the mount's ghost (0: none yet), and on the seat.</summary>
        public static void Begin(ref CreatureBlob rb, ref CreatureState r, int riderCoat, ref CreatureBlob mb, in CreatureState m, int mountCoat,
                                 int mountGhostId, in TerrainSource terrain)
        {
            var pose = PoseFor(ref rb, r, ref mb, m);
            r.Mode = CreatureMode.Ride;
            r.Stage = (byte)pose;
            r.ModeTime = r.StageTime = 0f;
            r.Sit = pose == RidePose.Cling ? 0f : 1f;
            r.Lie = pose == RidePose.Cling ? 1f : 0f;
            r.Speed = r.GaitSpeed = 0f;
            r.Fly = r.Vertical = r.Curl = r.Bow = r.HeadDown = 0f;
            r.Holder = mountGhostId != 0 ? -mountGhostId : SoloMount;
            Place(ref rb, ref r, riderCoat, ref mb, m, mountCoat, terrain);
        }

        /// <summary>Gets a rider down (sim world, main thread): clear of its mount (Clear) on the floor there, idle.</summary>
        public static void Dismount(EntityManager em, Entity rider, in TerrainSource terrain)
        {
            var r = em.GetComponentData<CreatureState>(rider);
            var at = r.Position;
            if (em.HasComponent<Rider>(rider))
            {
                var mount = em.GetComponentData<Rider>(rider).Mount;
                if (em.HasComponent<CreatureState>(mount))
                {
                    var rBody = em.GetComponentData<CreatureBody>(rider);
                    var mBody = em.GetComponentData<CreatureBody>(mount);
                    var query = em.CreateEntityQuery(ComponentType.ReadOnly<FloraLookup>());
                    var flora = query.TryGetSingleton<FloraLookup>(out var f) ? f.Blob : default;
                    at = Clear(ref rBody.Blob.Value, r, ref mBody.Blob.Value, em.GetComponentData<CreatureState>(mount), terrain, flora, out _);
                }
                em.RemoveComponent<Rider>(rider);
            }
            Down(ref r, at, terrain);
            em.SetComponentData(rider, r);
        }

        /// <summary>A rider's state once off its mount, at `at` (on the floor there: a deck, a cave's floor, the ground).</summary>
        public static void Down(ref CreatureState r, float3 at, in TerrainSource terrain)
        {
            at.y = terrain.FloorUnder(at + new float3(0f, 0.5f, 0f), out _);
            r.Position = at;
            r.Mode = CreatureMode.Idle;
            r.Stage = 0;
            r.ModeTime = r.StageTime = 0f;
            r.Sit = r.Lie = 0f;
            r.Speed = r.GaitSpeed = 0f;
            r.Holder = 0;
        }

        /// <summary>How wide a body is from its middle (m): its broader end's radius.</summary>
        public static float Radius(ref CreatureBlob b, float scale) => math.max(b.RearRadius, b.FrontRadius) * scale;

        /// <summary>How far a body reaches fore or aft of its middle (m): half its length and an end's radius, or for an
        /// upright body (whose length is up) just its radius.</summary>
        static float Reach(ref CreatureBlob b, float scale) =>
            b.Upright ? Radius(ref b, scale) : b.BodyLength * 0.5f * scale + Radius(ref b, scale);

        /// <summary>Room between a rider's and a mount's bodies sideways (m): centres apart less both radii (RideLog).</summary>
        public static float Room(ref CreatureBlob rb, in CreatureState r, ref CreatureBlob mb, in CreatureState m) =>
            math.distance(r.Position.xz, m.Position.xz) - Radius(ref rb, r.Scale) - Radius(ref mb, m.Scale);

        /// <summary>
        /// Where a rider gets down from this mount, falling clear (row 2): beside it to the right, else the left, else
        /// behind or in front, else further out; far enough that the two bodies don't meet, never inside a building's
        /// walls (flora, when there is one) or up onto a ledge; dry if any side is, else where the water's shallowest.
        /// On the floor there (Down puts it on it, so never in the ground).
        /// </summary>
        public static float3 Clear(ref CreatureBlob rb, in CreatureState r, ref CreatureBlob mb, in CreatureState m, in TerrainSource terrain,
                                   BlobAssetReference<FloraBlob> flora, out int side)
        {
            const float Margin = 0.15f;
            var right = new float3(math.cos(m.Yaw), 0f, -math.sin(m.Yaw));
            var forward = new float3(math.sin(m.Yaw), 0f, math.cos(m.Yaw));
            var across = Radius(ref mb, m.Scale) + Radius(ref rb, r.Scale) + Margin;
            var along = Reach(ref mb, m.Scale) + Reach(ref rb, r.Scale) + Margin;
            var mountFloor = terrain.FloorUnder(m.Position + new float3(0f, 0.5f, 0f), out _);
            var rr = Radius(ref rb, r.Scale);
            var best = m.Position + right * across;
            var bestScore = float.MaxValue;
            side = 0;
            // (Sides then ends, near then far; past those, eight ways round at two wider rings, for a mount backed into a wall.)
            for (int i = 0; i < 24; i++)
            {
                float3 at;
                if (i < 8)
                {
                    var k = i & 3;
                    var offset = k == 0 ? right * across : k == 1 ? -right * across : k == 2 ? -forward * along : forward * along;
                    at = m.Position + offset * (i < 4 ? 1f : 1.7f);
                }
                else
                {
                    var a = m.Yaw + (i & 7) * (math.PI / 4f);
                    at = m.Position + new float3(math.cos(a), 0f, -math.sin(a)) * math.max(across, along) * (i < 16 ? 2.6f : 4f);
                }
                if (flora.IsCreated && FloraPlacement.Blocked(ref flora.Value, terrain, at.xz, rr))
                    continue;
                var floor = terrain.FloorUnder(new float3(at.x, mountFloor + 0.5f, at.z), out _);
                // (Not up onto a ledge or a wall's top: a step it could take, no more.)
                var climb = floor - mountFloor;
                if (climb > 0.45f)
                    continue;
                var g = terrain.SampleGround(at.xz);
                var depth = g.HasWater ? math.max(0f, g.WaterSurface - floor) : 0f;
                // (Dry and level first, near sides before far, then the shallowest water.)
                var score = (depth > 0.1f ? 100f + depth * 10f : 0f) + math.abs(climb) + i * 0.01f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new float3(at.x, floor, at.z);
                    side = i;
                }
                if (score < 1f)
                    break;
            }
            return best;
        }

        /// <summary>Why a mount's rider must get down now (None: it rides on), by what the mount is doing (A-104 row 2).</summary>
        public static RideOff Reason(ref CreatureBlob mb, in CreatureState m, in Rider ride, in TerrainSource terrain)
        {
            if (m.Gone)
                return RideOff.Death;
            switch (m.Mode)
            {
                case CreatureMode.Swim:
                    return RideOff.Swim;
                case CreatureMode.Climb:
                case CreatureMode.Web:
                case CreatureMode.Burrow:
                case CreatureMode.Roost:
                    return RideOff.Climb;
                case CreatureMode.Sleep:
                case CreatureMode.Rest:
                    return RideOff.Sleep;
                case CreatureMode.Hunt:
                case CreatureMode.Flee:
                case CreatureMode.Play:
                    return RideOff.Fight;
                case CreatureMode.Held:
                case CreatureMode.Ride:
                    return RideOff.Held;
                case CreatureMode.Home:
                    if (m.Stage >= 1 || ride.Kind == RideKind.WalkHome && math.distancesq(m.Position.xz, m.Anchor.xz) < 2.5f * 2.5f)
                        return RideOff.Home;
                    break;
            }
            if (m.Fly > 0.3f)
                return RideOff.Climb;
            // (Wading in past its belly: the rider would be in the water too.)
            var g = terrain.SampleGround(m.Position.xz);
            if (g.HasWater && g.WaterSurface - g.Height > 0.45f * mb.StandHeight * m.Scale)
                return RideOff.Swim;
            // (A child's ride home ends if the grown-up turns aside: stops to talk, goes to work. A puppet is held where it is.)
            return ride.Kind == RideKind.WalkHome && m.Mode != CreatureMode.Home && m.Mode != CreatureMode.Puppet ? RideOff.Home : RideOff.None;
        }
    }

    /// <summary>
    /// A-104: puts every rider on its mount's seat each frame, after the brain and any push have moved the mounts and
    /// before their state goes out to clients (solo, or on the server). Gets a rider down, falling clear (Riding.Clear),
    /// when its mount swims, climbs, sleeps, fights, is picked up or dies, when a child's ride home nears the door, or
    /// when the ride's time is up (row 2; Riding.Reason). A rider whose mount has left the world gets down where it is.
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
                Flora = SystemAPI.TryGetSingleton<FloraLookup>(out var flora) ? flora.Blob : default,
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
            public BlobAssetReference<Punchy.Flora.FloraBlob> Flora;
            public float Dt;
            public EntityCommandBuffer Commands;

            void Execute(Entity self, ref CreatureState s, in CreatureBody body, ref Rider rider)
            {
                var mount = rider.Mount;
                if (s.Mode != CreatureMode.Ride)
                {
                    // (Someone else took it off: picked up, or got down by Riding.Dismount. Logged as None, the side
                    // holding the mode it was put in, for the tests.)
                    Off(self, RideOff.None, rider.Kind, s, s.Position, (int)s.Mode, s.Holder, s.ModeTime);
                    return;
                }
                if (!States.HasComponent(mount))
                {
                    var was = s.Position;
                    Riding.Down(ref s, s.Position, Terrain);
                    Off(self, RideOff.Death, rider.Kind, s, was, -1, s.Position.y - Terrain.FloorUnder(s.Position + new float3(0f, 0.5f, 0f), out _), 0f);
                    return;
                }
                var m = States[mount];
                var mBody = Bodies[mount];
                var why = Riding.Reason(ref mBody.Blob.Value, m, rider, Terrain);
                if (why == RideOff.None && rider.Left > 0f)
                {
                    rider.Left -= Dt;
                    if (rider.Left <= 0f)
                        why = RideOff.Done;
                }
                if (why != RideOff.None)
                {
                    var at = Riding.Clear(ref body.Blob.Value, s, ref mBody.Blob.Value, m, Terrain, Flora, out var side);
                    Riding.Down(ref s, at, Terrain);
                    var room = Riding.Room(ref body.Blob.Value, s, ref mBody.Blob.Value, m);
                    var floor = s.Position.y - Terrain.FloorUnder(s.Position + new float3(0f, 0.5f, 0f), out _);
                    Off(self, why, rider.Kind, s, m.Position, side, floor, room);
                    return;
                }
                s.Clock += Dt;
                s.Speed = s.GaitSpeed = 0f;
                var ghostId = Ghosts.HasComponent(mount) ? Ghosts[mount].ghostId : 0;
                s.Holder = ghostId != 0 ? -ghostId : Riding.SoloMount;
                Riding.Place(ref body.Blob.Value, ref s, body.Coat, ref mBody.Blob.Value, m, mBody.Coat, Terrain);
            }

            void Off(Entity self, RideOff why, RideKind kind, in CreatureState s, float3 mountAt, int side, float floor, float room)
            {
                Commands.RemoveComponent<Rider>(self);
                RideLog.Note(new RideLog.Off
                {
                    Why = why, Kind = kind, At = s.Position, Mount = mountAt, Room = room, Floor = floor, Side = side, Rider = s.Individual
                });
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
