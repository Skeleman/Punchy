using Punchy.Flora;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>
    /// Animal behaviour. Between wanders, an animal picks its next activity by its species' tastes and what's
    /// around it: grazing, walking to the water's edge to drink, lying down to rest, sleeping (at night, or by day for
    /// night animals), a game of chase with another of its kind, hunting prey, climbing a tree to sit in it, or going
    /// to its web. Anything that comes too close and might be dangerous (the player; predators, especially hunting
    /// ones) sends it running; sleeping animals notice later, and animals up a tree or in a web feel safe. Each
    /// activity also sets a posture (lying, head down, play bow, curled up, sitting, climbing) that the animator
    /// layers over the gait, and while standing about animals do little things of their own (pawing the ground,
    /// grooming, scratching, sitting up to look about…).
    ///
    /// Birds that fly cruise above the ground to their next spot (a perch in a tree, or the ground), take off when
    /// startled, and birds of prey circle over water and dive at fish (which may be caught: the fish notices and
    /// marks itself Gone).
    ///
    /// Animals see each other through a snapshot of every creature taken at the start of the frame, bucketed into a
    /// coarse grid. Two-animal activities agree through it: a chaser targets its Partner, the partner notices and
    /// runs; when the chaser catches up the roles swap. Trees come from the same placement tables the plants are
    /// drawn from (FloraLookup), so every machine agrees on where they are.
    ///
    /// Movement: creatures keep to dry land they can climb, turn at a limited rate and speed up and slow down
    /// smoothly, and their gait phase advances by the distance they cover, so the stride (measured from the key
    /// poses) matches the ground speed.
    /// </summary>
    [BurstCompile]
    // Runs where the animals are simulated: the server (clients only see them as ghosts; see CreatureReplicaSystem),
    // or the one world of a game without netcode.
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CreatureBrainSystem : ISystem
    {
        const float CellSize = 16f;
        EntityQuery creatures;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainSource>();
            state.RequireForUpdate<CreatureState>();
            creatures = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBody>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Every player animals should notice: the networked players on a server, or the one local player.
            var players = new NativeList<float3>(4, Allocator.TempJob);
            foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<NetPlayer>())
                players.Add(t.ValueRO.Position);
            if (players.Length == 0)
                foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
                    players.Add(t.ValueRO.Position);
            if (players.Length == 0)
                players.Add(new float3(1e9f, 0f, 1e9f));

            // Snapshot of everyone, for seeing each other.
            var entities = creatures.ToEntityArray(Allocator.TempJob);
            var states = creatures.ToComponentDataArray<CreatureState>(Allocator.TempJob);
            var bodies = creatures.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
            var lookup = new NativeHashMap<Entity, int>(entities.Length, Allocator.TempJob);
            var grid = new NativeParallelMultiHashMap<int2, int>(entities.Length, Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                lookup.Add(entities[i], i);
                grid.Add(Cell(states[i].Position.xz), i);
            }
            var hasFlora = SystemAPI.TryGetSingleton<FloraLookup>(out var flora) && flora.Blob.IsCreated;

            state.Dependency = new BrainJob
            {
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Flora = hasFlora ? flora.Blob : default,
                HasFlora = hasFlora,
                Players = players.AsArray(),
                Dt = math.min(SystemAPI.Time.DeltaTime, 0.1f),
                Night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f,
                Entities = entities,
                States = states,
                Bodies = bodies,
                Lookup = lookup,
                Grid = grid
            }.ScheduleParallel(state.Dependency);
            entities.Dispose(state.Dependency);
            states.Dispose(state.Dependency);
            bodies.Dispose(state.Dependency);
            lookup.Dispose(state.Dependency);
            grid.Dispose(state.Dependency);
            players.Dispose(state.Dependency);
        }

        static int2 Cell(float2 xz) => (int2)math.floor(xz / CellSize);

        /// <summary>Every creature but players' bodies (PlayerAvatarSystem moves those).</summary>
        [BurstCompile]
        [WithNone(typeof(PlayerAvatar), typeof(RemoteAvatar))]
        partial struct BrainJob : IJobEntity
        {
            public TerrainSource Terrain;
            public BlobAssetReference<FloraBlob> Flora;
            public bool HasFlora;
            [ReadOnly] public NativeArray<float3> Players;
            public float Dt, Night;
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeHashMap<Entity, int> Lookup;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;

            void Execute(Entity self, ref CreatureState s, in CreatureBody body)
            {
                if (s.Gone)
                    return;
                ref var b = ref body.Blob.Value;
                var rng = new Random(s.Seed | 1u);
                // The nearest player is the one it watches and runs from.
                var player = Players[0];
                for (int p = 1; p < Players.Length; p++)
                    if (math.distancesq(Players[p].xz, s.Position.xz) < math.distancesq(player.xz, s.Position.xz))
                        player = Players[p];
                var dt = Dt;
                s.Clock += dt;
                if (s.Mode == CreatureMode.Puppet)
                {
                    Advance(ref s, ref b, dt);
                    return;
                }
                s.ModeTime += dt;
                s.StageTime += dt;

                // A fish a diving bird has struck: caught (or, as often, it gets away).
                if (b.Habitat == Habitat.Swims && b.Hunted && Struck(self, ref s))
                {
                    s.Gone = true;
                    return;
                }

                var flier = b.Habitat == Habitat.Flies;
                var ground = Terrain.SampleHeight(s.Position.xz);
                var perched = flier && !Flying(s) && s.Position.y > ground + 0.3f;
                // Up a tree or in its web, an animal feels safe from the ground.
                var aloft = s.Mode == CreatureMode.Climb && s.Stage >= 1 || s.Mode == CreatureMode.Web && s.Stage >= 1;

                // Danger: the player, or predators of this species (from further off when they're hunting it).
                // Asleep, it notices only at half the distance; perched birds let you a bit closer.
                var alert = (s.Mode == CreatureMode.Sleep ? 0.5f : 1f) * (perched ? 0.5f : 1f);
                var away = s.Position.xz - player.xz;
                var playerDistance = math.length(away);
                // A bird in the air judges the player by real distance (high overhead is far), and keeps on with a hunt
                // unless the player is really close.
                var threatDistance = flier && Flying(s) ? math.distance(s.Position, player) : playerDistance;
                var nerve = s.Mode == CreatureMode.Hunt && flier ? 0.4f : 1f;
                var threatened = !aloft && b.FleeDistance > 0f && threatDistance < b.FleeDistance * s.Scale * alert * nerve;
                var threat = player.xz;
                if (!threatened && !aloft && b.Hunted && FindPredator(self, ref s, body.Species, alert, out var predator))
                {
                    threatened = true;
                    threat = predator;
                }
                // Invited to play: someone has started a game of chase with this one.
                if (!threatened && b.Playfulness > 0f && Free(s.Mode) && !perched && FindPlaymate(self, ref s, out var playmate))
                {
                    SetMode(ref s, CreatureMode.Play);
                    s.Partner = playmate;
                    s.Stage = 1;
                    s.Timer = rng.NextFloat(12f, 25f);
                    s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
                }
                if (threatened)
                {
                    if (s.Mode != CreatureMode.Flee)
                    {
                        SetMode(ref s, CreatureMode.Flee);
                        var dir = math.normalizesafe(s.Position.xz - threat, new float2(1f, 0f));
                        if (flier)
                        {
                            // Up and away, to come down somewhere further off.
                            s.Stage = 1;
                            s.Target = s.Position.xz + dir * rng.NextFloat(30f, 50f) + rng.NextFloat2Direction() * 8f;
                            s.TargetHeight = Terrain.SampleHeight(s.Target);
                        }
                        else
                            s.Target = s.Position.xz + dir * 30f + rng.NextFloat2Direction() * 6f;
                    }
                    s.Focus = threat;
                }

                var targetSpeed = 0f;
                var desiredLook = 0f;
                var moving = false;
                // Climbing, flying and hanging in a web move the animal themselves.
                var custom = false;
                float lie = 0f, headDown = 0f, bow = 0f, curl = 0f, sit = 0f, vertical = 0f;
                // Each individual walks a little faster or slower than the next.
                var pace = 1f + CreatureBlob.Trait(s.Individual, 5) * 0.08f;
                var walk = b.WalkSpeed * s.Scale * pace;
                var run = b.RunSpeed * s.Scale * pace;

                // Keep an eye on a nearby player (busy feeding or drinking, only a close one).
                var watchPlayer = b.FleeDistance > 0f && playerDistance < b.FleeDistance * 2.5f;
                var busyWatch = b.FleeDistance > 0f && playerDistance < b.FleeDistance * 1.6f;
                var lookAtPlayer = math.clamp(WrapAngle(math.atan2(-away.x, -away.y) - s.Yaw), -1.2f, 1.2f);
                var idleLook = watchPlayer ? lookAtPlayer : math.sin(s.Clock * 0.7f + s.Seed % 7) * 0.5f;

                switch (s.Mode)
                {
                    case CreatureMode.Idle:
                        // An idle action under way finishes before it moves on.
                        if (s.Action == IdleAction.None)
                            s.Timer -= dt;
                        desiredLook = idleLook;
                        if (s.Timer <= 0f)
                            Decide(self, ref s, ref b, body.Species, ref rng, perched);
                        break;

                    case CreatureMode.Wander:
                        if (flier && s.Stage >= 1)
                        {
                            custom = true;
                            if (FlyTo(ref s, ref b, s.Target, s.TargetHeight, b.FlySpeed, dt, ref vertical))
                                Land(ref s, ref b, ref rng);
                            break;
                        }
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 1f)
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Flee:
                        if (flier)
                        {
                            custom = true;
                            if (FlyTo(ref s, ref b, s.Target, s.TargetHeight, b.FlySpeed * 1.2f, dt, ref vertical) ||
                                !threatened && s.ModeTime > 6f && s.Position.y - Terrain.SampleHeight(s.Position.xz) < 0.5f)
                                Land(ref s, ref b, ref rng);
                            break;
                        }
                        targetSpeed = run;
                        moving = true;
                        if (threatened && math.distance(s.Position.xz, s.Target) < 2f)
                            s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - s.Focus, new float2(1f, 0f)) * 30f;
                        else if (!threatened && (s.ModeTime > 3f || math.distance(s.Position.xz, s.Target) < 2f))
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Graze:
                        // Head down, now and then looking up to chew and check around.
                        s.Timer -= dt;
                        var lookUp = math.frac(s.ModeTime / 7f + (s.Seed % 100) * 0.01f) > 0.78f;
                        headDown = lookUp ? 0.1f : 1f;
                        desiredLook = lookUp ? idleLook : 0f;
                        if (busyWatch)
                        {
                            headDown = 0.1f;
                            desiredLook = lookAtPlayer;
                        }
                        if (s.Timer <= 0f)
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Drink:
                        if (s.Stage == 0)
                        {
                            targetSpeed = walk;
                            moving = true;
                            if (math.distance(s.Position.xz, s.Target) < 0.6f)
                            {
                                s.Stage = 1;
                                s.StageTime = 0f;
                                s.Timer = rng.NextFloat(6f, 14f);
                                s.Target = s.Focus;
                            }
                        }
                        else
                        {
                            // Face the water and lap at it.
                            moving = true;
                            s.Timer -= dt;
                            headDown = busyWatch ? 0.2f : 1f;
                            desiredLook = busyWatch ? lookAtPlayer : 0f;
                            if (s.Timer <= 0f)
                                Pause(ref s, ref b, ref rng);
                        }
                        break;

                    case CreatureMode.Rest:
                        if (s.Action == IdleAction.None)
                            s.Timer -= dt;
                        // Upright bodies have no sitting pose yet: they rest on their feet. Birds settle down where
                        // they are (on a branch too).
                        lie = b.Upright || flier ? 0f : 1f;
                        sit = flier ? 0.6f : 0f;
                        desiredLook = idleLook;
                        if (s.Timer <= 0f)
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Sleep:
                        s.Timer -= dt;
                        // Animals curl up on the ground; upright bodies doze on their feet, head nodding; birds fluff
                        // up with their heads tucked.
                        lie = b.Upright || flier ? 0f : 1f;
                        headDown = b.Upright ? 0.35f : 0.75f;
                        curl = b.Upright ? 0f : 1f;
                        sit = flier ? 1f : 0f;
                        // Wakes when the nap ends, or (drowsily) once its sleeping time is over.
                        if (s.Timer <= 0f || SleepTime(ref b) < 0.3f && rng.NextFloat() < dt * 0.05f)
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Play:
                        Play(self, ref s, ref b, ref rng, ref targetSpeed, ref desiredLook, ref bow, ref moving);
                        break;

                    case CreatureMode.Hunt:
                        if (flier)
                        {
                            custom = true;
                            Dive(ref s, ref b, ref rng, dt, ref vertical);
                        }
                        else
                            Hunt(ref s, ref b, ref rng, ref targetSpeed, ref desiredLook, ref headDown, ref moving);
                        break;

                    case CreatureMode.Work:
                        s.Timer -= dt;
                        if (s.Stage == 0)
                        {
                            s.Target = s.WorkSpot;
                            targetSpeed = walk;
                            moving = true;
                            if (math.distance(s.Position.xz, s.WorkSpot) < 0.4f)
                            {
                                s.Stage = 1;
                                s.StageTime = 0f;
                            }
                        }
                        else
                        {
                            // At the post: turn to face the work, glance about now and then.
                            var toWork = s.WorkFace - s.Position.xz;
                            var wanted = WrapAngle(math.atan2(toWork.x, toWork.y) - s.Yaw);
                            s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                            desiredLook = math.frac(s.ModeTime / 9f) > 0.85f ? idleLook : 0f;
                            // Asleep on their feet at night.
                            if (SleepTime(ref b) > 0.5f)
                                headDown = 0.35f;
                        }
                        if (s.Timer <= 0f)
                            Pause(ref s, ref b, ref rng);
                        break;

                    case CreatureMode.Climb:
                        Climb(ref s, ref b, ref rng, dt, walk, idleLook, ref targetSpeed, ref moving, ref custom,
                              ref vertical, ref sit, ref desiredLook, ref headDown, ref curl);
                        break;

                    case CreatureMode.Web:
                        InWeb(ref s, ref b, ref rng, dt, walk, ref targetSpeed, ref moving, ref custom, ref vertical);
                        break;
                }

                // Little things done while standing about: start one now and then, keep it going, and take up the
                // posture it needs.
                Fidget(ref s, ref b, ref rng, dt, perched, ref sit, ref vertical, ref bow, ref desiredLook);

                // Animals with a home never head out of range of it (penned animals stay in their pen, even fleeing).
                if (s.Leash > 0f && moving)
                {
                    var fromHome = s.Target - s.Home;
                    var far = math.length(fromHome);
                    if (far > s.Leash)
                        s.Target = s.Home + fromHome / far * s.Leash;
                    if (math.distance(s.Position.xz, s.Target) < 0.8f && s.Mode == CreatureMode.Flee)
                        targetSpeed *= 0.2f;   // cornered: mill about rather than run on the spot
                }

                var urgent = s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Hunt || s.Mode == CreatureMode.Play;
                if (!custom)
                {
                    // Getting up comes before going anywhere.
                    targetSpeed *= math.saturate(1f - math.max(s.Lie, s.Sit) * 1.5f);

                    // Turn towards the target, then move; slow down while turning sharply.
                    var turn = 0f;
                    if (moving)
                    {
                        var to = s.Target - s.Position.xz;
                        var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                        turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                        targetSpeed *= math.saturate(math.cos(wanted) * 1.5f);
                    }
                    s.Yaw = WrapAngle(s.Yaw + turn);
                    s.YawRate = dt > 0f ? turn / dt : 0f;
                    s.Speed += (targetSpeed - s.Speed) * math.min(1f, dt * (urgent ? 4f : 2.5f));

                    // Stay out of water and off cliffs: stop (or, running, veer) when the way ahead is bad.
                    var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    var ahead = s.Position.xz + forward * math.max(1.2f, s.Speed * 0.5f) * s.Scale;
                    var landBound = b.Habitat == Habitat.Land || flier;
                    if (s.Speed > 0.05f && !Walkable(ahead, b.Habitat) || landBound && SlopeAhead(s.Position.xz, ahead) > 0.8f)
                    {
                        s.Speed *= 0.5f;
                        if (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Play && s.Stage == 1)
                            s.Target = s.Position.xz + new float2(-forward.y, forward.x) * (rng.NextBool() ? 20f : -20f);
                        else if (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Drink && s.Stage == 0 ||
                                 s.Mode == CreatureMode.Climb && s.Stage == 0 || s.Mode == CreatureMode.Web && s.Stage == 0)
                            Pause(ref s, ref b, ref rng);
                    }
                    // Swimmers now and then leap out of the water while they're moving.
                    if (b.Habitat == Habitat.Swims && b.Leaps > 0f && s.Air <= 0f && s.Speed > walk * 0.5f && rng.NextFloat() < b.Leaps * dt / 60f)
                    {
                        s.Air = 0.001f;
                        s.AirHeight = (b.BodyLength * 1.2f + 0.4f) * s.Scale * rng.NextFloat(0.6f, 1.2f);
                    }
                    Advance(ref s, ref b, dt, keepHeight: perched);
                }
                else
                    CreatureMotion.AdvancePhase(ref s, ref b, dt);
                s.Look += (desiredLook - s.Look) * math.min(1f, dt * 3f);

                // Ease into postures: lying down takes a moment, springing up to run doesn't.
                var lieRate = lie > s.Lie ? 0.8f : urgent ? 5f : 1.5f;
                s.Lie += (lie - s.Lie) * math.min(1f, dt * lieRate);
                s.HeadDown += (headDown - s.HeadDown) * math.min(1f, dt * (urgent ? 5f : 2f));
                s.Bow += (bow - s.Bow) * math.min(1f, dt * 5f);
                s.Curl += (curl - s.Curl) * math.min(1f, dt * (curl > s.Curl ? 0.6f : 4f));
                s.Sit += (sit - s.Sit) * math.min(1f, dt * (urgent ? 6f : 2.5f));
                s.Vertical += (vertical - s.Vertical) * math.min(1f, dt * 3f);
                var fly = Flying(s) && flier ? 1f : 0f;
                s.Fly += (fly - s.Fly) * math.min(1f, dt * 6f);
                s.Seed = rng.state;
            }

            // ------------------------------------------------------------------------------- choosing what to do

            /// <summary>How much this is its time to sleep (0..1).</summary>
            float SleepTime(ref CreatureBlob b) =>
                b.Active == ActiveTime.Day ? Night : b.Active == ActiveTime.Night ? 1f - Night : 0f;

            void Decide(Entity self, ref CreatureState s, ref CreatureBlob b, int species, ref Random rng, bool perched)
            {
                var sleepy = SleepTime(ref b);
                var awake = 1f - sleepy * 0.8f;
                var flier = b.Habitat == Habitat.Flies;

                // Find company first: the options depend on it.
                var playmate = Entity.Null;
                if (b.Playfulness > 0f && sleepy < 0.5f && !perched)
                    FindKin(self, ref s, species, 15f, out playmate);
                var prey = Entity.Null;
                if (b.Prey.Length > 0 && b.Hunger > 0f && sleepy < 0.5f)
                    FindPrey(self, ref s, ref b, flier ? 60f : 35f, out prey);

                // On a branch, a bird can only rest, sleep or fly off.
                var onGround = !perched;
                var wander = 1f * awake * (onGround ? 1f : 0f);
                var graze = b.Grazing * 2.5f * awake * (onGround ? 1f : 0f);
                var rest = b.Laziness * 0.7f;
                var sleep = sleepy > 0.5f ? 6f : b.Active == ActiveTime.Always ? b.Laziness * 0.4f : 0f;
                var drink = b.Thirst * 1.2f * awake * (onGround && !flier ? 1f : 0f);
                var play = playmate != Entity.Null ? b.Playfulness * 2f : 0f;
                var hunt = prey != Entity.Null ? b.Hunger * 2.5f : 0f;
                // Climbers go up trees to sit, and to sleep (they sleep up there, not on the ground).
                var climb = b.Climbing > 0f && HasFlora && !flier ? b.Climbing * (sleepy > 0.5f ? 8f : 2.5f) : 0f;
                if (climb > 0f && sleepy > 0.5f)
                    sleep *= 0.2f;
                // Web spinners spend most of their time in their web.
                var web = b.SpinsWebs ? 6f : 0f;
                // Birds fly off somewhere new more often from a branch; to sleep, they go up into a tree first.
                var fly = flier ? (perched ? 2f : 1.2f) * awake : 0f;
                if (flier && sleepy > 0.5f && !perched && HasFlora)
                {
                    fly = 8f;
                    sleep = 0f;
                }
                // Workers mostly work by day, taking a short wander or rest between stints; at night they doze at
                // their post.
                var work = s.Task != CreatureTask.None ? sleepy > 0.5f ? 10f : 6f : 0f;
                if (s.Task != CreatureTask.None && sleepy > 0.5f)
                    sleep = 0f;
                var roll = rng.NextFloat(wander + graze + rest + sleep + drink + play + hunt + work + climb + web + fly);

                if ((roll -= work) < 0f)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.Timer = rng.NextFloat(40f, 120f);
                }
                else if ((roll -= hunt) < 0f)
                {
                    SetMode(ref s, CreatureMode.Hunt);
                    s.Partner = prey;
                    s.Timer = b.ChaseTime * rng.NextFloat(0.7f, 1.3f);
                    if (flier)
                        s.Stage = 1;
                }
                else if ((roll -= play) < 0f)
                {
                    SetMode(ref s, CreatureMode.Play);
                    s.Partner = playmate;
                    s.Timer = rng.NextFloat(12f, 25f);
                    s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
                }
                else if ((roll -= sleep) < 0f)
                {
                    SetMode(ref s, CreatureMode.Sleep);
                    s.Timer = sleepy > 0.5f ? rng.NextFloat(90f, 300f) : rng.NextFloat(20f, 60f);
                }
                else if ((roll -= rest) < 0f)
                {
                    SetMode(ref s, CreatureMode.Rest);
                    s.Timer = rng.NextFloat(12f, 40f);
                }
                else if ((roll -= graze) < 0f)
                {
                    SetMode(ref s, CreatureMode.Graze);
                    s.Timer = rng.NextFloat(8f, 25f);
                }
                else if ((roll -= drink) < 0f && FindWater(ref s, ref b, ref rng, out var shore, out var water))
                {
                    SetMode(ref s, CreatureMode.Drink);
                    s.Target = shore;
                    s.Focus = water;
                }
                // (Only for species that do these: a failed drink above leaves the roll below zero.)
                else if (climb > 0f && (roll -= climb) < 0f && StartClimb(ref s, ref b, ref rng, sleepy > 0.5f)) { }
                else if (web > 0f && (roll -= web) < 0f && GoToWeb(ref s, ref b, ref rng)) { }
                else if (fly > 0f && (roll -= fly) < 0f && StartFlight(ref s, ref b, ref rng, sleepy > 0.5f)) { }
                else if (onGround)
                {
                    var candidate = s.Position.xz + rng.NextFloat2Direction() * b.WanderRadius * rng.NextFloat(0.3f, 1f) * (flier ? 0.15f : 1f);
                    // Animals with a home stay near it.
                    if (s.Leash > 0f && math.distance(candidate, s.Home) > s.Leash)
                        candidate = s.Home + rng.NextFloat2Direction() * s.Leash * rng.NextFloat(0.1f, 0.8f);
                    if (Walkable(candidate, b.Habitat))
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        s.Target = candidate;
                    }
                    else
                        s.Timer = 0.5f;
                }
                else
                    s.Timer = 1f;
            }

            static bool Free(CreatureMode mode) =>
                mode == CreatureMode.Idle || mode == CreatureMode.Wander || mode == CreatureMode.Graze || mode == CreatureMode.Rest;

            static void SetMode(ref CreatureState s, CreatureMode mode)
            {
                s.Mode = mode;
                s.ModeTime = 0f;
                s.Stage = 0;
                s.StageTime = 0f;
                s.Partner = Entity.Null;
                s.Action = IdleAction.None;
                s.ActionTime = 0f;
            }

            static void NextStage(ref CreatureState s)
            {
                s.Stage++;
                s.StageTime = 0f;
            }

            static void Pause(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Idle);
                s.Timer = rng.NextFloat(b.IdleTime.x, math.max(b.IdleTime.x, b.IdleTime.y));
            }

            // ------------------------------------------------------------------------------- idle actions

            /// <summary>
            /// While standing, lying or sitting about (on the ground, on a branch), now and then starts one of its
            /// species' little actions, carries it through, and takes up the posture it needs.
            /// </summary>
            void Fidget(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool perched,
                        ref float sit, ref float vertical, ref float bow, ref float desiredLook)
            {
                var lying = s.Mode == CreatureMode.Rest && s.Lie > 0.5f;
                var calm = s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Rest ||
                           s.Mode == CreatureMode.Climb && s.Stage == 3;
                if (s.Action != IdleAction.None)
                {
                    s.ActionTime += dt / math.max(s.ActionLength, 0.1f);
                    if (s.ActionTime >= 1f || !calm)
                    {
                        s.Action = IdleAction.None;
                        s.ActionTime = 0f;
                    }
                }
                else if (calm && b.Idles != IdleActions.None && s.Speed < 0.1f && rng.NextFloat() < dt * b.Fidgets * 0.15f)
                    StartAction(ref s, ref b, ref rng, lying, perched || s.Mode == CreatureMode.Climb);

                var t = s.ActionTime;
                switch (s.Action)
                {
                    case IdleAction.Sit:
                        sit = 1f;
                        break;
                    case IdleAction.SitUp:
                        sit = 1f;
                        vertical = 0.5f;
                        desiredLook = math.sin(s.ModeTime * 1.3f) * 1.1f;
                        break;
                    case IdleAction.Scratch:
                        sit = math.max(sit, 0.8f);
                        break;
                    case IdleAction.Stretch:
                        bow = t < 0.5f ? 1f : 0f;
                        break;
                    case IdleAction.LookAround:
                        desiredLook = math.sin(s.ModeTime * 1.7f + s.Individual % 5) * 1.15f;
                        break;
                }
            }

            /// <summary>Picks one of the species' idle actions that suits how it is now (lying, perched or standing).</summary>
            static void StartAction(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool lying, bool aloft)
            {
                var options = new FixedList64Bytes<byte>();
                for (int a = 1; a <= (int)IdleAction.LookAround; a++)
                {
                    var flag = (IdleActions)(1 << (a - 1));
                    if ((b.Idles & flag) == 0)
                        continue;
                    var action = (IdleAction)a;
                    // Lying down, only what can be done lying down; up a tree or on a branch, nothing that needs the
                    // ground.
                    if (lying && action != IdleAction.Groom && action != IdleAction.TailFlick && action != IdleAction.LookAround &&
                        action != IdleAction.Preen)
                        continue;
                    if (aloft && (action == IdleAction.PawGround || action == IdleAction.Sniff || action == IdleAction.Stretch ||
                                  action == IdleAction.SitUp || action == IdleAction.HeadBob))
                        continue;
                    options.Add((byte)a);
                }
                if (options.Length == 0)
                    return;
                s.Action = (IdleAction)options[rng.NextInt(options.Length)];
                s.ActionTime = 0f;
                s.ActionLength = s.Action switch
                {
                    IdleAction.PawGround => 2.4f,
                    IdleAction.Groom => rng.NextFloat(3f, 6f),
                    IdleAction.Scratch => 2.2f,
                    IdleAction.Shake => 1.1f,
                    IdleAction.Stretch => 3f,
                    IdleAction.Sniff => 2.5f,
                    IdleAction.Sit => rng.NextFloat(6f, 15f),
                    IdleAction.SitUp => rng.NextFloat(4f, 9f),
                    IdleAction.TailFlick => 1.6f,
                    IdleAction.Preen => rng.NextFloat(2.5f, 5f),
                    IdleAction.HeadBob => 1.4f,
                    _ => 3f
                };
            }

            // ------------------------------------------------------------------------------- two-animal activities

            /// <summary>
            /// Chase in fun. The chaser play-bows, then runs at its partner; catching up swaps the roles (the runner
            /// that has been running longest turns chaser, so both agree). Ends when the time is up or the partner
            /// stops playing.
            /// </summary>
            void Play(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng,
                      ref float targetSpeed, ref float desiredLook, ref float bow, ref bool moving)
            {
                s.Timer -= Dt;
                if (!Lookup.TryGetValue(s.Partner, out var p) || s.Timer <= 0f ||
                    s.ModeTime > 1.5f && (States[p].Mode != CreatureMode.Play || States[p].Partner != self))
                {
                    if (rng.NextFloat() < 0.4f)
                    {
                        SetMode(ref s, CreatureMode.Rest);
                        s.Timer = rng.NextFloat(10f, 25f);
                    }
                    else
                        Pause(ref s, ref b, ref rng);
                    return;
                }

                var other = States[p];
                var to = other.Position.xz - s.Position.xz;
                var distance = math.length(to);
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                moving = true;
                var walk = b.WalkSpeed * s.Scale;
                var run = b.RunSpeed * s.Scale;

                if (s.Stage == 0)
                {
                    s.Target = other.Position.xz;
                    if (s.StageTime < 1f)
                        bow = 1f;   // play bow, then off
                    else
                    {
                        targetSpeed = math.lerp(walk, run, 0.75f);
                        var reach = (b.BodyLength * s.Scale + Bodies[p].Blob.Value.BodyLength * other.Scale) * 0.6f + 0.3f;
                        if (distance < reach)
                        {
                            s.Stage = 1;
                            s.StageTime = 0f;
                        }
                    }
                }
                else
                {
                    // Both running: the one that's run longest turns round to chase.
                    if (other.Stage == 1 && other.Partner == self && s.StageTime > other.StageTime && s.StageTime > 0.3f)
                    {
                        s.Stage = 0;
                        s.StageTime = 0f;
                        return;
                    }
                    var flee = math.normalizesafe(-to, new float2(1f, 0f));
                    var side = new float2(-flee.y, flee.x) * math.sin(s.Clock * 1.3f + s.Seed % 11) * 0.6f;
                    // Don't run off: past a dozen metres from the chaser, circle back round; and keep the game near
                    // where it started (home, for animals that have one), turning back towards it further out.
                    var dir = distance > 12f ? new float2(-flee.y, flee.x) : flee + side;
                    var toCentre = s.Focus - s.Position.xz;
                    var wander = math.length(toCentre);
                    var range = s.Leash > 0f ? s.Leash * 0.6f : 12f;
                    if (wander > range)
                        dir = math.lerp(math.normalizesafe(dir), toCentre / wander, math.saturate((wander - range) / 6f));
                    s.Target = s.Position.xz + math.normalizesafe(dir, flee) * 8f;
                    targetSpeed = math.lerp(walk, run, distance > 12f ? 0.3f : 0.6f);
                }
            }

            /// <summary>Stalks low towards its prey, then sprints; gives up when it's caught up or run out of puff.</summary>
            void Hunt(ref CreatureState s, ref CreatureBlob b, ref Random rng,
                      ref float targetSpeed, ref float desiredLook, ref float headDown, ref bool moving)
            {
                s.Timer -= Dt;
                if (!Lookup.TryGetValue(s.Partner, out var p) || s.Timer <= 0f)
                {
                    Tired(ref s, ref rng);
                    return;
                }
                var prey = States[p];
                var to = prey.Position.xz - s.Position.xz;
                var distance = math.length(to);
                if (distance > 60f || distance < b.BodyLength * s.Scale + 0.5f)
                {
                    Tired(ref s, ref rng);
                    return;
                }
                s.Target = prey.Position.xz;
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1f, 1f);
                moving = true;
                if (distance > 14f && s.Stage == 0)
                {
                    targetSpeed = b.WalkSpeed * s.Scale * 0.8f;
                    headDown = 0.35f;
                }
                else
                {
                    s.Stage = 1;
                    targetSpeed = b.RunSpeed * s.Scale;
                }
            }

            static void Tired(ref CreatureState s, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Rest);
                s.Timer = rng.NextFloat(15f, 40f);
            }

            // ------------------------------------------------------------------------------- trees

            /// <summary>Picks a tree nearby and sets off to climb it (false if there's none).</summary>
            bool StartClimb(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                if (!HasFlora || !FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 30f, 3.5f, ref rng, out var tree))
                    return false;
                SetMode(ref s, CreatureMode.Climb);
                s.Anchor = tree.Base;
                s.AnchorSize = math.max(tree.TrunkRadius, 0.05f);
                // A branch a little way up into the crown, and a spot out along it.
                var up = math.clamp(tree.CrownBase + (tree.Height - tree.CrownBase) * rng.NextFloat(0.15f, 0.4f), 1.2f, tree.Height * 0.85f);
                s.TargetHeight = tree.Base.y + up;
                s.Focus = tree.Base.xz + rng.NextFloat2Direction() * (s.AnchorSize + math.clamp(tree.CrownRadius * 0.4f, 0.35f, 1.6f));
                s.Timer = toSleep ? rng.NextFloat(120f, 300f) : rng.NextFloat(15f, 50f);
                return true;
            }

            /// <summary>
            /// Climbing a tree: to the foot of the trunk, up it (facing it), out along a branch, sitting there (idle
            /// actions, or asleep at night), back to the trunk and down again. Running from danger it ignores while
            /// up there.
            /// </summary>
            void Climb(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float idleLook,
                       ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical, ref float sit,
                       ref float desiredLook, ref float headDown, ref float curl)
            {
                var trunk = s.Anchor.xz;
                var climbSpeed = walk * 0.75f;
                // While climbing, the body keeps its legs' length off the trunk.
                var reach = s.AnchorSize + b.StandHeight * s.Scale * 0.8f + 0.02f;
                var outward = math.normalizesafe(s.Position.xz - trunk, new float2(0f, 1f));
                switch (s.Stage)
                {
                    case 0:
                        s.Target = trunk + outward * (reach + 0.15f);
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 0.3f)
                            NextStage(ref s);
                        else if (s.ModeTime > 40f)
                            Pause(ref s, ref b, ref rng);
                        break;
                    case 1:
                        custom = true;
                        vertical = 1f;
                        TurnTowards(ref s, ref b, trunk, dt);
                        s.Position.xz = trunk + outward * reach;
                        // Gets onto the trunk before going up.
                        s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                        s.Position.y = math.min(s.Position.y + s.Speed * dt, s.TargetHeight);
                        if (s.Position.y >= s.TargetHeight - 0.01f)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Position.y = s.TargetHeight;
                        // Level out, then walk out along the branch.
                        if (s.Vertical > 0.25f)
                            s.Speed = 0f;
                        else if (GlideTo(ref s, ref b, s.Focus, walk * 0.6f, dt) < 0.15f)
                        {
                            NextStage(ref s);
                            s.Yaw = math.atan2(s.Position.x - trunk.x, s.Position.z - trunk.y);   // faces out
                        }
                        break;
                    case 3:
                        custom = true;
                        s.Speed = 0f;
                        s.YawRate = 0f;
                        sit = 1f;
                        desiredLook = idleLook;
                        if (SleepTime(ref b) > 0.5f)
                        {
                            headDown = 0.6f;
                            curl = 0.7f;
                        }
                        if (s.Action == IdleAction.None)
                            s.Timer -= dt;
                        if (s.Timer <= 0f)
                            NextStage(ref s);
                        break;
                    case 4:
                        custom = true;
                        s.Position.y = s.TargetHeight;
                        if (GlideTo(ref s, ref b, trunk + math.normalizesafe(s.Focus - trunk, outward) * reach, walk * 0.6f, dt) < 0.1f)
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        vertical = 1f;
                        TurnTowards(ref s, ref b, trunk, dt);
                        s.Position.xz = trunk + outward * reach;
                        s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                        var floor = Terrain.SampleHeight(s.Position.xz);
                        s.Position.y = math.max(s.Position.y - s.Speed * dt, floor);
                        if (s.Position.y <= floor + 0.01f)
                            Pause(ref s, ref b, ref rng);
                        break;
                }
            }

            /// <summary>Turns on the spot towards a point.</summary>
            static void TurnTowards(ref CreatureState s, ref CreatureBlob b, float2 at, float dt)
            {
                var to = at - s.Position.xz;
                var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                var turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = dt > 0f ? turn / dt : 0f;
            }

            /// <summary>Walks straight to a point at a steady pace, keeping its height (along a branch). Returns the distance left.</summary>
            static float GlideTo(ref CreatureState s, ref CreatureBlob b, float2 at, float speed, float dt)
            {
                var to = at - s.Position.xz;
                var distance = math.length(to);
                TurnTowards(ref s, ref b, at, dt);
                var facing = math.saturate(math.cos(WrapAngle(math.atan2(to.x, to.y) - s.Yaw)) * 1.5f);
                s.Speed = math.min(speed * facing, distance / math.max(dt, 1e-4f));
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                return math.length(at - s.Position.xz);
            }

            // ------------------------------------------------------------------------------- webs

            /// <summary>Goes to its web, picking a spot for one first if it has none (false if there's nowhere).</summary>
            bool GoToWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (s.AnchorSize <= 0f && !PlaceWeb(ref s, ref b, ref rng))
                    return false;
                SetMode(ref s, CreatureMode.Web);
                s.Timer = rng.NextFloat(60f, 200f);
                return true;
            }

            /// <summary>
            /// A spot for a web: hung beside a tree trunk, facing a random way, its edge just off the bark; or, with no
            /// tree about, low in the grass.
            /// </summary>
            bool PlaceWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                var radius = b.WebSize * s.Scale;
                var yaw = rng.NextFloat(-math.PI, math.PI);
                var right = new float2(math.cos(yaw), -math.sin(yaw));
                float2 centre;
                byte side;
                if (HasFlora && FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 14f, 2f, ref rng, out var tree))
                {
                    // Trunk to the spider's left (side 1: centre to the trunk's right) or right (side 0).
                    var toTheRight = rng.NextBool();
                    centre = tree.Base.xz + right * (toTheRight ? 1f : -1f) * (tree.TrunkRadius + radius * 1.02f);
                    side = (byte)(toTheRight ? 1 : 0);
                }
                else
                {
                    centre = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(1f, 4f);
                    side = 2;
                }
                if (!Walkable(centre, Habitat.Land))
                    return false;
                s.Anchor = new float3(centre.x, Terrain.SampleHeight(centre) + radius + (side == 2 ? 0.05f : 0.25f), centre.y);
                s.AnchorSize = radius;
                s.AnchorYaw = yaw;
                s.AnchorSide = side;
                return true;
            }

            /// <summary>
            /// Under its web (0), up a thread (1), then waiting in it head down, facing out of the web (Stage 2–4 says
            /// where the tree is; see CreatureMode.Web); down again when it's time to move on (5).
            /// </summary>
            void InWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk,
                       ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                var climbSpeed = walk * 0.6f;
                if (s.Stage == 0)
                {
                    s.Target = s.Anchor.xz;
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Anchor.xz) < 0.2f)
                        NextStage(ref s);
                    else if (s.ModeTime > 40f)
                    {
                        // Can't get there: forget it and find another spot next time.
                        s.AnchorSize = 0f;
                        Pause(ref s, ref b, ref rng);
                    }
                    return;
                }
                custom = true;
                if (s.Stage == 1)
                {
                    vertical = 1f;
                    s.Position.xz = s.Anchor.xz;
                    s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                    s.Position.y = math.min(s.Position.y + s.Speed * dt, s.Anchor.y);
                    if (s.Position.y >= s.Anchor.y - 0.01f)
                    {
                        s.Stage = (byte)(2 + s.AnchorSide);
                        s.StageTime = 0f;
                    }
                }
                else if (s.Stage <= 4)
                {
                    vertical = -1f;
                    s.Speed = 0f;
                    s.Position = s.Anchor;
                    var wanted = WrapAngle(s.AnchorYaw - s.Yaw);
                    var turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                    s.Yaw = WrapAngle(s.Yaw + turn);
                    s.YawRate = turn / math.max(dt, 1e-4f);
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Stage = 5;
                        s.StageTime = 0f;
                    }
                }
                else
                {
                    vertical = 1f;
                    s.Speed = climbSpeed * math.saturate(s.Vertical * 1.5f - 0.3f);
                    var floor = Terrain.SampleHeight(s.Position.xz);
                    s.Position.y = math.max(s.Position.y - s.Speed * dt, floor);
                    if (s.Position.y <= floor + 0.01f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- flight

            /// <summary>Whether a flying species is in the air now (flying somewhere, fleeing or hunting).</summary>
            static bool Flying(in CreatureState s) =>
                (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Hunt) && s.Stage >= 1 || s.Mode == CreatureMode.Flee;

            /// <summary>
            /// Takes off for somewhere new: a branch in a tree nearby (more likely by its Perching, and always to
            /// sleep), or a patch of ground.
            /// </summary>
            bool StartFlight(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                var range = math.max(b.WanderRadius, 20f);
                if (HasFlora && (toSleep || rng.NextFloat() < b.Perching) &&
                    FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz + rng.NextFloat2Direction() * range * 0.5f, range * 0.6f, 3f, ref rng, out var tree))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    var up = tree.CrownBase + (tree.Height - tree.CrownBase) * rng.NextFloat(0.25f, 0.75f);
                    s.Target = tree.Base.xz + rng.NextFloat2Direction() * (tree.TrunkRadius + tree.CrownRadius * rng.NextFloat(0.25f, 0.7f));
                    s.TargetHeight = tree.Base.y + up;
                    return true;
                }
                if (toSleep)
                    return false;
                var spot = s.Position.xz + rng.NextFloat2Direction() * range * rng.NextFloat(0.5f, 1.2f);
                if (!Walkable(spot, Habitat.Land))
                    return false;
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = spot;
                s.TargetHeight = Terrain.SampleHeight(spot);
                return true;
            }

            /// <summary>Touches down: on a branch it settles to rest a while, on the ground it looks about.</summary>
            void Land(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                s.Speed = 0f;
                var onBranch = s.Position.y > Terrain.SampleHeight(s.Position.xz) + 0.3f;
                if (onBranch && rng.NextFloat() < 0.5f)
                {
                    SetMode(ref s, CreatureMode.Rest);
                    s.Timer = rng.NextFloat(10f, 40f);
                }
                else
                    Pause(ref s, ref b, ref rng);
            }

            /// <summary>Cruising height over a point: its own share of the species' range above the ground or water.</summary>
            float Cruise(in CreatureState s, ref CreatureBlob b, float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                var floor = g.HasWater ? math.max(g.Height, g.WaterSurface) : g.Height;
                return floor + math.lerp(b.FlightHeight.x, b.FlightHeight.y, CreatureBlob.Trait(s.Individual, 11) * 0.5f + 0.5f);
            }

            /// <summary>
            /// Flies towards a point and height: climbs to cruising height, glides down to it as it comes near, and
            /// keeps clear of the ground. Returns true once it's there. Pitches the body with the climb or descent.
            /// </summary>
            bool FlyTo(ref CreatureState s, ref CreatureBlob b, float2 target, float targetHeight, float speed, float dt, ref float vertical)
            {
                var to = target - s.Position.xz;
                var distance = math.length(to);
                var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                var turnRate = b.TurnRate * 1.5f;
                var turn = math.clamp(wanted, -turnRate * dt, turnRate * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                // Slows to land; takes off (from standing) gradually.
                var wantSpeed = speed * math.saturate(distance / 5f + 0.15f) * math.saturate(math.cos(wanted) + 0.4f);
                s.Speed += (wantSpeed - s.Speed) * math.min(1f, dt * 2f);
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * math.min(s.Speed * dt, distance);

                var cruise = math.max(Cruise(s, ref b, s.Position.xz), Cruise(s, ref b, target));
                var descent = math.max(cruise - targetHeight, 0f) * 1.3f + 3f;
                var wantY = distance > descent ? cruise : math.lerp(targetHeight, cruise, distance / descent);
                var vy = math.clamp((wantY - s.Position.y) * 1.5f, -4f, 3f);
                s.Position.y += vy * dt;
                // Clear of the ground (except right at the landing spot).
                var floor = Terrain.SampleHeight(s.Position.xz) + (distance > 1f ? 0.4f : 0f);
                s.Position.y = math.max(s.Position.y, floor);
                vertical = math.clamp(vy / math.max(b.FlySpeed, 1f), -0.6f, 0.5f);
                if (distance < 0.35f && math.abs(s.Position.y - targetHeight) < 0.25f)
                {
                    s.Position.xz = target;
                    s.Position.y = targetHeight;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// A bird of prey after a fish: circles above it (1), folds and dives at it (2), then beats back up and away
            /// (3). The fish decides for itself whether it was caught (see Struck).
            /// </summary>
            void Dive(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                s.Timer -= dt;
                var hasPrey = Lookup.TryGetValue(s.Partner, out var p);
                if (s.Stage <= 1)
                {
                    if (!hasPrey || s.Timer <= 0f)
                    {
                        AfterHunt(ref s, ref b, ref rng);
                        return;
                    }
                    var prey = States[p];
                    var angle = s.Clock * 0.45f + (s.Individual % 10);
                    var circle = prey.Position.xz + new float2(math.cos(angle), math.sin(angle)) * 10f;
                    FlyTo(ref s, ref b, circle, Cruise(s, ref b, prey.Position.xz), b.FlySpeed * 0.8f, dt, ref vertical);
                    if (s.StageTime > 4f && math.distance(s.Position.xz, prey.Position.xz) < 14f && rng.NextFloat() < dt * 0.6f)
                        NextStage(ref s);
                }
                else if (s.Stage == 2)
                {
                    if (!hasPrey)
                    {
                        NextStage(ref s);
                        return;
                    }
                    var prey = States[p];
                    var g = Terrain.SampleGround(prey.Position.xz);
                    var surface = g.HasWater ? g.WaterSurface : g.Height;
                    var to = prey.Position.xz - s.Position.xz;
                    var distance = math.length(to);
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    var turn = math.clamp(wanted, -b.TurnRate * 2f * dt, b.TurnRate * 2f * dt);
                    s.Yaw = WrapAngle(s.Yaw + turn);
                    s.YawRate = turn / math.max(dt, 1e-4f);
                    // Steep enough to arrive over the fish as it reaches the water.
                    var drop = math.max(s.Position.y - surface, 0.1f);
                    s.Speed = math.min(b.FlySpeed * 1.3f, distance / math.max(drop / 9f, 0.1f));
                    s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * math.min(s.Speed * dt, distance);
                    s.Position.y = math.max(s.Position.y - 9f * dt, surface + 0.15f);
                    vertical = -0.85f;
                    if (s.Position.y <= surface + 0.2f || s.StageTime > 6f)
                        NextStage(ref s);
                }
                else
                {
                    // Labouring back up and away.
                    var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                    FlyTo(ref s, ref b, s.Position.xz + forward * 30f, Cruise(s, ref b, s.Position.xz) , b.FlySpeed, dt, ref vertical);
                    vertical = math.max(vertical, 0.35f);
                    if (s.StageTime > 3.5f)
                        AfterHunt(ref s, ref b, ref rng);
                }
            }

            void AfterHunt(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                if (!StartFlight(ref s, ref b, ref rng, false))
                {
                    // Nowhere in particular: down to the ground nearby.
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    s.Target = s.Position.xz + rng.NextFloat2Direction() * 15f;
                    s.TargetHeight = Terrain.SampleHeight(s.Target);
                }
            }

            /// <summary>
            /// A fish being dived at: struck if the bird reaches it (within a metre as the bird hits the water). Half
            /// the strikes catch it: the pair decides the same way every time, so the fish and the bird agree.
            /// </summary>
            bool Struck(Entity self, ref CreatureState s)
            {
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode == CreatureMode.Hunt && other.Partner == self && other.Stage == 2 &&
                            math.distance(other.Position, s.Position) < 1.1f)
                            return (math.hash(new int2(self.Index, Entities[j].Index)) & 1u) == 0u;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            // ------------------------------------------------------------------------------- seeing each other

            /// <summary>A predator of this species close by (further off if it's hunting this animal).</summary>
            bool FindPredator(Entity self, ref CreatureState s, int species, float alert, out float2 at)
            {
                at = default;
                var best = float.MaxValue;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || !Hunts(ref Bodies[j].Blob.Value, species))
                            continue;
                        var other = States[j];
                        // In three dimensions: a bird circling high overhead isn't close.
                        var d = math.distance(other.Position, s.Position);
                        var range = (other.Mode == CreatureMode.Hunt && other.Partner == self ? 30f : 8f) * alert;
                        // A bird of prey overhead goes unnoticed until it dives (a fish only sees it hit the water).
                        if (Bodies[j].Blob.Value.Habitat == Habitat.Flies)
                            range = other.Mode == CreatureMode.Hunt && other.Partner == self && other.Stage == 2 ? 3f : 0f;
                        if (d < range && d < best)
                        {
                            best = d;
                            at = other.Position.xz;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return best < float.MaxValue;
            }

            static bool Hunts(ref CreatureBlob b, int species)
            {
                for (int k = 0; k < b.Prey.Length; k++)
                    if (b.Prey[k] == species)
                        return true;
                return false;
            }

            /// <summary>Someone close by who has started a game of chase with this animal.</summary>
            bool FindPlaymate(Entity self, ref CreatureState s, out Entity playmate)
            {
                playmate = Entity.Null;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode == CreatureMode.Play && other.Partner == self && other.Stage == 0)
                        {
                            playmate = Entities[j];
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>The nearest free animal of the same species within range.</summary>
            bool FindKin(Entity self, ref CreatureState s, int species, float range, out Entity found)
            {
                found = Entity.Null;
                var best = range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || Bodies[j].Species != species || !Free(States[j].Mode))
                            continue;
                        var d = math.distance(States[j].Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            /// <summary>The nearest animal it hunts within range.</summary>
            bool FindPrey(Entity self, ref CreatureState s, ref CreatureBlob b, float range, out Entity found)
            {
                found = Entity.Null;
                var best = range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || !Hunts(ref b, Bodies[j].Species))
                            continue;
                        var d = math.distance(States[j].Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            // ------------------------------------------------------------------------------- ground

            /// <summary>
            /// Looks around for water within 50 m: the spot to stand (on dry land, front end at the edge) and the
            /// water it faces.
            /// </summary>
            bool FindWater(ref CreatureState s, ref CreatureBlob b, ref Random rng, out float2 shore, out float2 water)
            {
                shore = water = default;
                var start = rng.NextFloat(math.PI * 2f);
                for (int k = 0; k < 8; k++)
                {
                    var a = start + k * math.PI * 0.25f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    for (var d = 6f; d <= 50f; d += 8f)
                    {
                        var p = s.Position.xz + dir * d;
                        if (Walkable(p, Habitat.Land))
                            continue;
                        // Back up from the water to dry land.
                        var q = p;
                        for (int step = 0; step < 30 && !Walkable(q, Habitat.Land); step++)
                            q -= dir;
                        if (!Walkable(q, Habitat.Land))
                            break;
                        water = p;
                        shore = q + dir * 0.6f - dir * (b.BodyLength * 0.4f * s.Scale);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Moves along the heading and advances the gait by the distance covered, so feet don't slide. Land animals
            /// keep to the ground (unless <paramref name="keepHeight"/>: a bird on a branch); swimmers and floaters to
            /// the water.
            /// </summary>
            void Advance(ref CreatureState s, ref CreatureBlob b, float dt, bool keepHeight = false)
            {
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                s.Position.xz += forward * s.Speed * dt;
                if (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies)
                {
                    if (!keepHeight)
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                }
                else
                {
                    // Floating on the surface, or swimming a little under it (shallower in shallow water); a leap
                    // arcs up out of the water and back.
                    var g = Terrain.SampleGround(s.Position.xz);
                    var surface = g.HasWater ? g.WaterSurface : g.Height;
                    var y = b.Habitat == Habitat.Floats ? surface + b.StandHeight * 0.35f * s.Scale
                          : surface - math.clamp((surface - g.Height) * 0.45f, 0.25f, b.SwimDepth);
                    if (s.Air > 0f)
                    {
                        s.Air += dt / (0.6f + s.AirHeight * 0.35f);
                        if (s.Air >= 1f)
                            s.Air = 0f;
                        else
                            y = math.max(y, surface + math.sin(math.PI * s.Air) * s.AirHeight - (surface - y) * (1f - math.sin(math.PI * s.Air)));
                    }
                    s.Position.y = y;
                }
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>Whether an animal of this habitat can go here: dry land, or water deep enough to swim or float in.</summary>
            bool Walkable(float2 xz, Habitat habitat)
            {
                var g = Terrain.SampleGround(xz);
                var depth = g.HasWater ? g.WaterSurface - g.Height : -1f;
                if (habitat == Habitat.Swims)
                    return depth >= 0.7f;
                if (habitat == Habitat.Floats)
                    return depth >= 0.3f;
                return depth <= 0.15f && !Terrain.IsCaveHole(xz);
            }

            float SlopeAhead(float2 from, float2 to)
            {
                var rise = Terrain.SampleHeight(to) - Terrain.SampleHeight(from);
                return rise / math.max(math.distance(from, to), 0.1f);
            }

            static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
        }
    }
}
