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
        // (Where the brain's time goes, for the profiler: its searches for danger and company, its needs and idle
        // actions, its choices, its moving and its gait.)
        static readonly Unity.Profiling.ProfilerMarker HunterMarker = new Unity.Profiling.ProfilerMarker("Brain.Hunter");
        static readonly Unity.Profiling.ProfilerMarker PredatorMarker = new Unity.Profiling.ProfilerMarker("Brain.Predator");
        static readonly Unity.Profiling.ProfilerMarker ChaserMarker = new Unity.Profiling.ProfilerMarker("Brain.Chaser");
        static readonly Unity.Profiling.ProfilerMarker AlarmMarker = new Unity.Profiling.ProfilerMarker("Brain.Alarm");
        static readonly Unity.Profiling.ProfilerMarker PlaymateMarker = new Unity.Profiling.ProfilerMarker("Brain.Playmate");
        static readonly Unity.Profiling.ProfilerMarker NeedsMarker = new Unity.Profiling.ProfilerMarker("Brain.Needs");
        static readonly Unity.Profiling.ProfilerMarker FidgetMarker = new Unity.Profiling.ProfilerMarker("Brain.Fidget");
        static readonly Unity.Profiling.ProfilerMarker DecideMarker = new Unity.Profiling.ProfilerMarker("Brain.Decide");
        static readonly Unity.Profiling.ProfilerMarker AdvanceMarker = new Unity.Profiling.ProfilerMarker("Brain.Advance");
        // (Inside Advance: its ground, water and wall samples, its gait, and one empty mark per creature that moves, so the
        // profiler's calls a frame count the movers.)
        static readonly Unity.Profiling.ProfilerMarker SampleMarker = new Unity.Profiling.ProfilerMarker("Brain.Sample");
        static readonly Unity.Profiling.ProfilerMarker PhaseMarker = new Unity.Profiling.ProfilerMarker("Brain.Phase");
        static readonly Unity.Profiling.ProfilerMarker MovingMarker = new Unity.Profiling.ProfilerMarker("Brain.Moving");
        static readonly Unity.Profiling.ProfilerMarker EaseMarker = new Unity.Profiling.ProfilerMarker("Brain.EaseGait");
        EntityQuery creatures;
        /// <summary>Where each player was last frame (by Holder id), for how fast they're going.</summary>
        NativeHashMap<int, float3> lastPlayerPositions;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainSource>();
            state.RequireForUpdate<CreatureState>();
            creatures = SystemAPI.QueryBuilder().WithAll<CreatureState, CreatureBody>().Build();
            lastPlayerPositions = new NativeHashMap<int, float3>(8, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (lastPlayerPositions.IsCreated)
                lastPlayerPositions.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Every player animals should notice: the networked players on a server, or the one local player; who each
            // is (as a Holder), which way they face (for an animal in their arms) and their pose (an emote, say).
            var players = new NativeList<float3>(4, Allocator.TempJob);
            var playerIds = new NativeList<int>(4, Allocator.TempJob);
            var playerYaws = new NativeList<float>(4, Allocator.TempJob);
            var playerPoses = new NativeList<byte>(4, Allocator.TempJob);
            // (And which body each has, for where a carried animal sits.)
            var playerSpecies = new NativeList<int>(4, Allocator.TempJob);
            foreach (var (t, owner, command) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<Unity.NetCode.GhostOwner>, RefRO<NetPlayerCommand>>().WithAll<NetPlayer>())
            {
                AddPlayer(t.ValueRO, Interactions.HolderId(owner.ValueRO.NetworkId), command.ValueRO.Pose, ref players, ref playerIds, ref playerYaws, ref playerPoses);
                playerSpecies.Add(command.ValueRO.Species);
            }
            if (players.Length == 0)
            {
                var pose = SystemAPI.TryGetSingleton<InteractionRequest>(out var request) ? (byte)request.Pose : (byte)0;
                var species = SystemAPI.TryGetSingleton<LocalPlayerSpecies>(out var local) ? local.Index : -1;
                foreach (var t in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerTag>())
                {
                    AddPlayer(t.ValueRO, Interactions.HolderId(0), pose, ref players, ref playerIds, ref playerYaws, ref playerPoses);
                    playerSpecies.Add(species);
                }
            }
            if (players.Length == 0)
            {
                AddPlayer(LocalTransform.FromPosition(new float3(1e9f, 0f, 1e9f)), 0, 0, ref players, ref playerIds, ref playerYaws, ref playerPoses);
                playerSpecies.Add(-1);
            }
            // How fast each is going (from where they were last frame): a slow, quiet approach lets wild animals be crept up on.
            var frameDt = math.max(SystemAPI.Time.DeltaTime, 1e-3f);
            var playerSpeeds = new NativeArray<float>(players.Length, Allocator.TempJob);
            for (int p = 0; p < players.Length; p++)
            {
                // (A jump of more than a few metres in a frame is a teleport, not speed.)
                if (lastPlayerPositions.TryGetValue(playerIds[p], out var was) && math.distance(was, players[p]) < 5f)
                    playerSpeeds[p] = math.distance(was.xz, players[p].xz) / frameDt;
                lastPlayerPositions[playerIds[p]] = players[p];
            }

            // Snapshot of everyone, for seeing each other.
            var entities = creatures.ToEntityArray(Allocator.TempJob);
            var states = creatures.ToComponentDataArray<CreatureState>(Allocator.TempJob);
            var bodies = creatures.ToComponentDataArray<CreatureBody>(Allocator.TempJob);
            var lookup = new NativeHashMap<Entity, int>(entities.Length, Allocator.TempJob);
            var grid = new NativeParallelMultiHashMap<int2, int>(entities.Length, Allocator.TempJob);
            var hunters = new NativeList<int>(4, Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                lookup.Add(entities[i], i);
                grid.Add(Cell(states[i].Position.xz), i);
                if (states[i].Mode == CreatureMode.Hunt && states[i].Stage >= FolkHunting.Stalk)
                    hunters.Add(i);
            }
            var hasFlora = SystemAPI.TryGetSingleton<FloraLookup>(out var flora) && flora.Blob.IsCreated;

            state.Dependency = new BrainJob
            {
                Terrain = SystemAPI.GetSingleton<TerrainSource>(),
                Flora = hasFlora ? flora.Blob : default,
                HasFlora = hasFlora,
                Players = players.AsArray(),
                PlayerIds = playerIds.AsArray(),
                PlayerYaws = playerYaws.AsArray(),
                PlayerPoses = playerPoses.AsArray(),
                PlayerSpecies = playerSpecies.AsArray(),
                PlayerSpeeds = playerSpeeds,
                Carry = SystemAPI.TryGetSingleton<CarryTable>(out var carryTable) ? carryTable : default,
                Dt = math.min(SystemAPI.Time.DeltaTime, 0.1f),
                Night = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Night : 0f,
                Hour = SystemAPI.TryGetSingleton<FaunaClock>(out var hourClock) ? hourClock.Hour : 12f,
                Entities = entities,
                States = states,
                Bodies = bodies,
                Lookup = lookup,
                Grid = grid,
                Hunters = hunters.AsArray()
            }.ScheduleParallel(state.Dependency);
            hunters.Dispose(state.Dependency);
            entities.Dispose(state.Dependency);
            states.Dispose(state.Dependency);
            bodies.Dispose(state.Dependency);
            lookup.Dispose(state.Dependency);
            grid.Dispose(state.Dependency);
            players.Dispose(state.Dependency);
            playerIds.Dispose(state.Dependency);
            playerYaws.Dispose(state.Dependency);
            playerPoses.Dispose(state.Dependency);
            playerSpecies.Dispose(state.Dependency);
            playerSpeeds.Dispose(state.Dependency);
        }

        static void AddPlayer(in LocalTransform t, int id, byte pose, ref NativeList<float3> players, ref NativeList<int> ids, ref NativeList<float> yaws,
                              ref NativeList<byte> poses)
        {
            poses.Add(pose);
            var forward = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            players.Add(t.Position);
            ids.Add(id);
            yaws.Add(math.atan2(forward.x, forward.z));
        }

        static int2 Cell(float2 xz) => (int2)math.floor(xz / CellSize);

        /// <summary>Every creature but players' bodies (PlayerAvatarSystem moves those) and test specimens.</summary>
        [BurstCompile]
        [WithNone(typeof(PlayerAvatar), typeof(RemoteAvatar), typeof(PosedForTest))]
        partial struct BrainJob : IJobEntity
        {
            public TerrainSource Terrain;
            public BlobAssetReference<FloraBlob> Flora;
            public bool HasFlora;
            [ReadOnly] public NativeArray<float3> Players;
            [ReadOnly] public NativeArray<int> PlayerIds;
            [ReadOnly] public NativeArray<float> PlayerYaws;
            /// <summary>Each player's pose (an IdleAction): emotes are answered.</summary>
            [ReadOnly] public NativeArray<byte> PlayerPoses;
            /// <summary>Each player's body (species index, -1 unknown) and ground speed.</summary>
            [ReadOnly] public NativeArray<int> PlayerSpecies;
            [ReadOnly] public NativeArray<float> PlayerSpeeds;
            public CarryTable Carry;
            public float Dt, Night, Hour;
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeHashMap<Entity, int> Lookup;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
            /// <summary>Snapshot indices of the hunters out after game now (FolkHunting stages).</summary>
            [ReadOnly] public NativeArray<int> Hunters;

            void Execute(Entity self, ref CreatureState s, in CreatureBody body)
            {
                Think(self, ref s, in body);
                // (The legs' pose follows whatever speed it ended the tick at, whichever way it got there; its feet are
                // only kept planted through it near someone, where they're seen.)
                if (!s.Gone)
                {
                    var near = false;
                    for (int p = 0; p < Players.Length && !near; p++)
                        near = math.distancesq(Players[p], s.Position) < CreatureMotion.NearFeet * CreatureMotion.NearFeet;
                    using (EaseMarker.Auto())
                        CreatureMotion.EaseGait(ref s, ref body.Blob.Value, Dt, near);
                }
            }

            void Think(Entity self, ref CreatureState s, in CreatureBody body)
            {
                if (s.Gone)
                    return;
                ref var b = ref body.Blob.Value;
                var rng = new Random(s.Seed | 1u);
                // (Whether a cave visitor is in a cave: a march down the cave's distance field near one, so once a tick,
                // where it starts the tick, rather than at each of the places that ask.)
                underRock = UnderRock(s, ref b);
                // The nearest player is the one it watches and runs from.
                var player = Players[0];
                var emote = (IdleAction)PlayerPoses[0];
                var playerSpeed = PlayerSpeeds[0];
                for (int p = 1; p < Players.Length; p++)
                    if (math.distancesq(Players[p].xz, s.Position.xz) < math.distancesq(player.xz, s.Position.xz))
                    {
                        player = Players[p];
                        emote = (IdleAction)PlayerPoses[p];
                        playerSpeed = PlayerSpeeds[p];
                    }
                if (emote < IdleAction.Wave)
                    emote = IdleAction.None;
                var dt = Dt;
                s.Clock += dt;
                if (s.Mode == CreatureMode.Puppet)
                {
                    Advance(ref s, ref b, dt);
                    return;
                }
                // A sand or snow worm lives its own life under the ground, heeding nothing.
                if (b.Tunnels)
                {
                    Tunnel(ref s, ref b, ref rng, dt);
                    s.Seed = rng.state;
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
                var perched = flier && !Flying(s) && s.Position.y > Terrain.SampleHeight(s.Position.xz) + 0.3f;
                // Up a tree or in its web, an animal feels safe from the ground.
                var aloft = s.Mode == CreatureMode.Climb && s.Stage >= 1 || s.Mode == CreatureMode.Web && s.Stage >= 1 ||
                            s.Mode == CreatureMode.Burrow && s.Stage >= 1 || s.Mode == CreatureMode.Roost ||
                            // (In someone's hands, it isn't afraid of them.)
                            s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Petted ||
                            // (Nor a messenger bird of the folk it's come to.)
                            s.Task == CreatureTask.Messenger;
                // Settlements' animals are used to people: they let them come close (not predators, though).
                var tame = s.Leash > 0f && !b.Upright ? 0.3f : 1f;

                // Danger: the player, or predators of this species (from further off when they're hunting it).
                // Asleep, it notices only at half the distance; perched birds let you a bit closer.
                var alert = (s.Mode == CreatureMode.Sleep ? 0.5f : 1f) * (perched ? 0.5f : 1f);
                var away = s.Position.xz - player.xz;
                // Someone walking up slowly (not running) is noticed half as far off; from behind or the side, only at
                // the last moment, so a small animal can be crept up on and caught.
                var facing = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                if (playerSpeed < 2.5f)
                    alert *= math.dot(facing, math.normalizesafe(-away)) < 0.3f ? 0.1f : 0.5f;
                var playerDistance = math.length(away);
                // Danger is judged by real distance, so high overhead is far: a player flying over the herd doesn't
                // scatter it, and nor does someone walking on the ground over a cave. (On foot across level ground it's
                // much the same as the distance across.) A bird in the air keeps on with a hunt unless the player is
                // really close.
                var threatDistance = math.distance(s.Position, player);
                var nerve = s.Mode == CreatureMode.Hunt && flier ? 0.4f : 1f;
                // Someone cheering or dancing about is noticed (and run from) further off; waving, a little. (Only by
                // the skittish: a curious one comes to look, and some pay it no heed.)
                var commotion = b.Reaction != EmoteReaction.Flees ? 1f :
                                emote == IdleAction.Cheer || emote == IdleAction.Dance ? 1.8f : emote == IdleAction.Wave ? 1.25f : 1f;
                var threatened = !aloft && b.FleeDistance > 0f && threatDistance < b.FleeDistance * s.Scale * alert * nerve * tame * commotion;
                var threat = player.xz;
                // A hunter after it: heard creeping close, or seen drawing back to throw (and the throw itself).
                if (!threatened && !aloft && b.FleeDistance > 0f && FindHunter(self, ref s, alert, out var hunter))
                {
                    threatened = true;
                    threat = hunter;
                }
                if (!threatened && !aloft && b.Hunted && FindPredator(self, ref s, body.Species, alert, out var predator))
                {
                    threatened = true;
                    threat = predator;
                }
                // A wolf, a bear or the like in a town: a guard running at it (or a night guard's torch close by) sends it
                // off out of the place.
                if (!threatened && !aloft && b.DrivenOff && FindChaser(self, in s, out var chaser))
                {
                    threatened = true;
                    threat = chaser;
                    // It makes for the town's edge, the nearest way out, veering off from the guard (so it doesn't run
                    // deeper in, away from one coming in from the bounds).
                    var town = SettlementOf(s.Position.xz);
                    if (town >= 0)
                    {
                        ref var t = ref Terrain.Settlements.Value.Settlements[town];
                        var outward = math.normalizesafe(s.Position.xz - t.Centre, new float2(1f, 0f));
                        var veer = math.normalizesafe(s.Position.xz - chaser, outward);
                        threat = s.Position.xz - math.normalizesafe(outward + veer * 0.6f, outward) * 5f;
                    }
                }
                // One bolts, they all bolt: a herd (or a flock) runs when any of it close by does, from the same thing.
                if (!threatened && !aloft && b.FleeDistance > 0f && s.Mode != CreatureMode.Flee && FindAlarm(self, ref s, body.Species, out var alarm))
                {
                    threatened = true;
                    threat = alarm;
                }
                // Birds of a feather: when one takes off, the others near it go along to the same place.
                if (!threatened && flier && (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Graze || s.Mode == CreatureMode.Rest) &&
                    FindTakeoff(self, ref s, body.Species, out var leader))
                    Follow(ref s, leader, ref rng);
                // Invited to play: someone has started a game of chase with this one.
                if (!threatened && b.Playfulness > 0f && Free(s.Mode) && !perched && !(flier && Flying(s)) && FindPlaymate(self, ref s, out var playmate))
                {
                    SetMode(ref s, CreatureMode.Play);
                    s.Partner = playmate;
                    s.Stage = 1;
                    s.Timer = rng.NextFloat(12f, 25f);
                    s.Focus = s.Leash > 0f ? s.Home : s.Position.xz;   // where the game is played
                }
                // A burrower with its burrow near runs for it instead of just running.
                if (threatened && b.Burrows && s.Mode != CreatureMode.Burrow && s.AnchorSize > 0f &&
                    math.distance(s.Position.xz, s.Anchor.xz) < 30f)
                {
                    SetMode(ref s, CreatureMode.Burrow);
                    s.Timer = rng.NextFloat(8f, 20f);
                    s.Focus = threat;
                    threatened = false;
                }
                else if (threatened && s.Mode == CreatureMode.Burrow)
                {
                    s.Focus = threat;
                    threatened = false;
                }
                // Bats hanging asleep are startled off their roost by someone waving, bowing, cheering or dancing close by:
                // out along the passages, as at dusk (they come back to roost when they're sleepy again).
                if (b.Roosts && s.Mode == CreatureMode.Roost && s.Stage == 200 && emote != IdleAction.None &&
                    math.distance(s.Position, player) < 15f)
                {
                    s.Unsettled = rng.NextFloat(40f, 80f);
                    if (s.LairCave >= 0)
                    {
                        s.Stage = (byte)(100 + math.min(s.LairNode, 89));
                        s.StageTime = 0f;
                    }
                    else
                        NightLoop(ref s, ref b, ref rng);
                }
                // Rooted to the spot: it never runs from anything; what comes within reach, it snaps at.
                if (b.Rooted)
                {
                    threatened = false;
                    Snap(self, ref s, ref b, player, ref rng);
                }
                // A beast scared inside a town (by a guard, or bolting with the rest of its pack) takes itself off
                // somewhere else: its range moved out past the edge the way it's running, so it isn't held to ground
                // inside the place and doesn't come straight back.
                if (threatened && b.DrivenOff && s.Leash > 0f)
                {
                    var town = SettlementOf(s.Position.xz);
                    if (town >= 0)
                    {
                        ref var t = ref Terrain.Settlements.Value.Settlements[town];
                        var off = math.normalizesafe(s.Position.xz - threat, math.normalizesafe(s.Position.xz - t.Centre, new float2(1f, 0f)));
                        if (math.distance(s.Position.xz, t.Centre) < t.Radius && math.distance(s.Home, t.Centre) < t.Radius + s.Leash)
                            s.Home = t.Centre + math.normalizesafe(s.Position.xz + off * t.Radius - t.Centre, off) * (t.Radius + s.Leash + 10f);
                    }
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
                        else if (CaveWalker(s, ref b))
                            s.Target = CaveEscape(s, threat, ref rng);
                        else
                            s.Target = s.Position.xz + dir * 30f + rng.NextFloat2Direction() * 6f;
                    }
                    s.Focus = threat;
                }
                else if (emote != IdleAction.None && !b.Rooted)
                    Answer(ref s, ref b, ref rng, emote, player, playerDistance, dt);

                // A ghost abroad at night: someone coming close and it's gone, to appear again further off (dithering
                // back in, as anything new does); someone within sight, and it drifts closer to watch them from a few
                // metres off.
                if (b.Haunts && SleepTime(ref b) < 0.5f)
                {
                    if (playerDistance < 3.5f)
                    {
                        var off = math.normalizesafe(s.Position.xz - player.xz, rng.NextFloat2Direction());
                        s.Position.xz += off * rng.NextFloat(20f, 35f) + rng.NextFloat2Direction() * 6f;
                        s.Clock = 0f;
                        Pause(ref s, ref b, ref rng);
                    }
                    else if (playerDistance < 18f && (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander))
                    {
                        if (s.Mode != CreatureMode.Wander)
                            SetMode(ref s, CreatureMode.Wander);
                        s.Target = player.xz - math.normalizesafe(player.xz - s.Position.xz) * 5f;
                    }
                }

                // Swimming with nowhere to go (it stopped, or wanted to graze or rest): it makes for the nearest shore.
                if (b.CrossesWater && !threatened && s.Mode != CreatureMode.Wander && s.Mode != CreatureMode.Flee &&
                    s.Mode != CreatureMode.Hunt && s.Mode != CreatureMode.Play && s.Mode != CreatureMode.Greet && s.Mode != CreatureMode.Held &&
                    WaterDepth(s.Position.xz) > b.StandHeight * s.Scale * 0.8f && NearestShore(s.Position.xz, out var ashore))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = ashore;
                }

                // A messenger bird coming to this one: it stops what it's doing to meet it.
                if (b.Upright && b.Hands && s.Leash > 0f && (Free(s.Mode) || s.Mode == CreatureMode.Greet) && !(s.Mode == CreatureMode.Idle && s.Stage >= MessageWait) &&
                    rng.NextFloat() < 0.25f && FindMessenger(self, in s, out var messenger))
                {
                    SetMode(ref s, CreatureMode.Idle);
                    s.Stage = MessageWait;
                    s.Partner = messenger;
                    s.Timer = 40f;
                }

                if (b.RaidsHives)
                    s.ChaseCooldown = math.max(s.ChaseCooldown - dt, 0f);

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
                        if (s.Stage >= MessageWait)
                        {
                            Receiving(ref s, ref b, ref rng, dt);
                            break;
                        }
                        // An idle action under way finishes before it moves on.
                        if (s.Action == IdleAction.None)
                            s.Timer -= dt;
                        desiredLook = idleLook;
                        if (s.Timer <= 0f)
                            Decide(self, ref s, ref b, body.Species, ref rng, perched, player);
                        break;

                    case CreatureMode.Wander:
                        // A moth round its light: from point to point (in a cave, under the rock: no cruising height).
                        if (b.DrawnToLight)
                        {
                            custom = true;
                            if (FlitTo(ref s, ref b, dt, ref vertical))
                                Flit(ref s, ref rng);
                            break;
                        }
                        if ((flier || b.Breaches) && s.Stage >= 1)
                        {
                            custom = true;
                            if (s.Stage == 2)
                                Soar(ref s, ref b, ref rng, dt);
                            // A sky serpent diving back in: once under, swimming again.
                            if (b.Breaches && s.Stage == 3 && FlyTo(ref s, ref b, s.Target, s.TargetHeight, b.FlySpeed, dt, ref vertical))
                            {
                                Pause(ref s, ref b, ref rng);
                                break;
                            }
                            if (b.Breaches && s.Stage == 3)
                                break;
                            if (FlyTo(ref s, ref b, s.Target, s.TargetHeight, b.FlySpeed, dt, ref vertical))
                            {
                                // Bats don't land: they hawk for insects in loops all night, then go to roost.
                                if (b.DrawnToLight)
                                    Flit(ref s, ref rng);
                                else if (b.Roosts && SleepTime(ref b) < 0.5f)
                                    NightLoop(ref s, ref b, ref rng);
                                else if (b.Roosts)
                                    StartRoost(ref s, ref b, ref rng);
                                else
                                    Land(ref s, ref b, ref rng);
                            }
                            break;
                        }
                        targetSpeed = walk;
                        moving = true;
                        // (Right into its nest, when that's where it's going.)
                        var arrive = b.Nests == NestSite.Ground && math.all(s.Target == s.Anchor.xz) ? 0.2f : 1f;
                        // (A crawler in a cave is going to a point on a wall or the ceiling: there in three dimensions.)
                        var crawlTo = b.Crawls && CaveWalker(s, ref b);
                        if (crawlTo ? math.distance(s.Position, new float3(s.Target.x, s.TargetHeight, s.Target.y)) < 0.4f
                                    : math.distance(s.Position.xz, s.Target) < arrive)
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
                        // In a cave: on along the passages, a point at a time (and another way if it's stuck at a
                        // wall: StageTime counts the time without getting anywhere).
                        if (CaveWalker(s, ref b))
                        {
                            if (s.Speed > 0.15f)
                                s.StageTime = 0f;
                            if (threatened && (math.distance(s.Position.xz, s.Target) < 1.5f || s.StageTime > 1f))
                            {
                                s.Target = CaveEscape(s, s.Focus, ref rng);
                                s.StageTime = 0f;
                            }
                        }
                        else if (threatened && math.distance(s.Position.xz, s.Target) < 2f)
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
                        // Passing for a plant: legs tucked right under, head still.
                        if (b.Disguised)
                        {
                            curl = 1f;
                            desiredLook = 0f;
                        }
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
                        if (s.Stage >= FolkHunting.Stalk)
                            FolkHunt(ref s, ref b, ref rng, dt, walk, run, ref targetSpeed, ref moving, ref desiredLook, ref bow, ref headDown);
                        else if (flier)
                        {
                            custom = true;
                            Dive(ref s, ref b, ref rng, dt, ref vertical);
                        }
                        else
                            Hunt(ref s, ref b, ref rng, ref targetSpeed, ref desiredLook, ref headDown, ref moving);
                        break;

                    case CreatureMode.Sit:
                        if (s.Stage == 0)
                        {
                            targetSpeed = walk;
                            moving = true;
                            // (Its time there starts when it sits down; a spot it can't reach in a minute, it gives up on.)
                            if (s.StageTime > 60f)
                                Pause(ref s, ref b, ref rng);
                            // (Two that chose the same spot in the same moment: the later of them finds another.)
                            else if (Contested(self, in s) && !Resettle(self, ref s, ref rng))
                                Pause(ref s, ref b, ref rng);
                            // (Close is enough: it shuffles the rest of the way on, which for a seat is past the front
                            // of the seat itself, solid to walkers.)
                            else if (math.distance(s.Position.xz, s.Target) < (s.TargetHeight == 4f || s.TargetHeight == CreatureAnimator.HearthSeat ? 0.9f : 0.5f))
                            {
                                s.Stage = (byte)s.TargetHeight;
                                s.StageTime = 0f;
                            }
                        }
                        else
                        {
                            // Turned to the well (or the view, or the way its seat faces), then down: sitting up and
                            // looking about, or lying back.
                            s.Timer -= dt;
                            // (Shuffling the last of the way onto its own spot, so it neither crowds the next nor the wall.)
                            var short_ = s.Target - s.Position.xz;
                            var shuffle = math.min(math.length(short_), 0.5f * dt);
                            s.Position.xz += math.normalizesafe(short_) * shuffle;
                            var toFace = s.Focus - s.Position.xz;
                            var wantedYaw = WrapAngle(math.atan2(toFace.x, toFace.y) - s.Yaw);
                            s.Yaw = WrapAngle(s.Yaw + math.clamp(wantedYaw, -b.TurnRate * dt, b.TurnRate * dt));
                            // (At the town's fire, some stand.)
                            if (s.Stage == 3)
                                lie = 1f;
                            else if (s.Stage != CreatureAnimator.HearthStand)
                                sit = 1f;
                            desiredLook = s.Stage == 3 ? 0f : idleLook;
                            var hearth = AtHearth(s.Stage);
                            if (s.Timer <= 0f || hearth && !Gathering(in s, ref b) || Contested(self, in s) && !Resettle(self, ref s, ref rng))
                                Pause(ref s, ref b, ref rng);
                        }
                        break;

                    case CreatureMode.Swim:
                        Swimming(ref s, ref b, ref rng, dt, walk, idleLook, ref targetSpeed, ref moving, ref desiredLook);
                        break;

                    case CreatureMode.Patrol:
                        Patrol(self, ref s, ref b, ref rng, dt, walk, run, player, playerSpeed, emote, ref targetSpeed, ref moving, ref desiredLook);
                        break;

                    case CreatureMode.Work:
                        // (A miner has no post: its day is a round, into the mine and back out with ore.)
                        if (s.Task == CreatureTask.Mine)
                        {
                            custom = true;
                            Mining(ref s, ref b, ref rng, dt, walk);
                            break;
                        }
                        if (s.Task == CreatureTask.Travel)
                        {
                            custom = true;
                            Travelling(ref s, ref b, ref rng, dt, walk);
                            break;
                        }
                        if (s.Task == CreatureTask.Messenger)
                        {
                            custom = true;
                            Messenger(ref s, ref b, ref rng, dt, ref vertical);
                            break;
                        }
                        if (s.Stage >= CreatureAnimator.RaidWalk && s.Stage <= CreatureAnimator.RaidLick)
                        {
                            Raiding(ref s, ref b, ref rng, dt, walk, run, ref targetSpeed, ref moving, ref sit, ref vertical, ref desiredLook, ref headDown, ref bow);
                            break;
                        }
                        // (A fisher's time at the water starts when it gets there, however far it had to walk.)
                        if (s.Stage >= 1 || s.Task != CreatureTask.Fish)
                            s.Timer -= dt;
                        if (s.Stage == 0)
                        {
                            s.Target = s.WorkSpot;
                            targetSpeed = walk;
                            moving = true;
                            if (math.distance(s.Position.xz, s.WorkSpot) < 0.4f)
                            {
                                // (A hermit's chores: chopping at its woodpile is stage 1, stooped over its garden or the
                                // water stage 2, so every machine poses it right.)
                                s.Stage = (byte)(s.Task == CreatureTask.Hermit && s.AnchorSide == 2 ? 2 : 1);
                                s.StageTime = 0f;
                            }
                        }
                        else if (s.Task == CreatureTask.Fish)
                            Fishing(ref s, ref b, ref rng, dt, ref desiredLook);
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

                    case CreatureMode.Greet:
                        Greet(ref s, ref b, ref rng, dt, player, walk, run, ref targetSpeed, ref moving, ref sit, ref desiredLook);
                        break;

                    case CreatureMode.Petted:
                        Petted(ref s, ref b, ref rng, dt, ref sit, ref bow, ref desiredLook);
                        break;

                    case CreatureMode.Held:
                        custom = true;
                        // Legs tucked up in the arms, not dangling; birds settle their wings.
                        lie = 1f;
                        Held(ref s, ref b, ref rng, dt);
                        break;

                    case CreatureMode.Roost:
                        custom = true;
                        Roost(ref s, ref b, ref rng, dt, ref vertical);
                        break;

                    case CreatureMode.Burrow:
                        GoToGround(ref s, ref b, ref rng, dt, walk, run, playerDistance, ref targetSpeed, ref moving, ref custom, ref vertical);
                        break;

                    case CreatureMode.Home:
                        GoIndoors(ref s, ref b, ref rng, dt, walk, ref targetSpeed, ref moving, ref custom);
                        break;
                }

                Needs(ref s, ref b, dt);
                if (s.Task == CreatureTask.Hermit)
                    Wary(ref s, ref rng, player);

                // Little things done while standing about: start one now and then, keep it going, and take up the
                // posture it needs.
                Fidget(self, body.Species, ref s, ref b, ref rng, dt, perched, ref sit, ref vertical, ref bow, ref desiredLook);
                // A child standing about now and then pokes at the ground with its stick, or throws its stone; more
                // often sitting in a play ring, into the middle of it.
                var playing = s.Mode == CreatureMode.Sit && s.Stage == CreatureAnimator.PlayRing;
                if (b.Child && s.Action == IdleAction.None && (s.Mode == CreatureMode.Idle || playing) && s.Speed < 0.1f &&
                    rng.NextFloat() < dt * (playing ? 0.4f : 0.25f) &&
                    (s.Held == HeldItem.Stick || s.Held == HeldItem.Stone))
                {
                    s.Action = s.Held == HeldItem.Stick ? IdleAction.Poke : IdleAction.Throw;
                    s.ActionTime = 0f;
                    s.ActionLength = s.Held == HeldItem.Stick ? rng.NextFloat(2f, 3.5f) : 1.2f;
                    // (Watching where its stone lands, a few metres ahead, or the ground it pokes at.)
                    var ahead = s.Position.xz + new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * (s.Held == HeldItem.Stone ? 3.5f : 0.6f);
                    s.LookKind = 0;
                    s.LookAt = new float3(ahead.x, Terrain.SampleHeight(ahead), ahead.y);
                    s.LookFor = s.ActionLength + 0.5f;
                    s.Timer = math.max(s.Timer, s.ActionLength + 0.5f);
                }

                // Animals with a home never head out of range of it (penned animals stay in their pen, even fleeing).
                if (s.Leash > 0f && moving && s.Mode != CreatureMode.Patrol && s.Mode != CreatureMode.Swim)
                {
                    var fromHome = s.Target - s.Home;
                    var far = math.length(fromHome);
                    if (far > s.Leash)
                        s.Target = s.Home + fromHome / far * s.Leash;
                    if (math.distance(s.Position.xz, s.Target) < 0.8f && s.Mode == CreatureMode.Flee)
                        targetSpeed *= 0.2f;   // cornered: mill about rather than run on the spot
                }

                var urgent = s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Hunt || s.Mode == CreatureMode.Play ||
                             s.Mode == CreatureMode.Burrow && s.ModeTime < 3f;
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
                    var landBound = (b.Habitat == Habitat.Land || flier) && !CaveWalker(s, ref b);
                    var blocked = b.Crawls && CaveWalker(s, ref b) ? false : CaveWalker(s, ref b) ? s.Speed > 0.05f && !CaveFloor(ahead, s.Position.y)
                                                : s.Speed > 0.05f && !Walkable(ahead, b.Habitat);
                    // Folk out swimming keep to the water while they swim, and wade out onto the bank when they're done.
                    if (s.Mode == CreatureMode.Swim && s.Stage >= 1)
                    {
                        blocked = s.Stage == 1 && s.Speed > 0.05f && !Swimmable(ahead);
                        landBound = false;
                    }
                    // Animals that swim go on into water when they're on their way somewhere (not to stand in it), and a
                    // lake bed's slope doesn't stop them.
                    // A lava dweller keeps to lava (a swimmer's water test passes ordinary water too).
                    if (b.LavaDweller && s.Speed > 0.05f && !blocked && !InLava(ahead))
                        blocked = true;
                    // Ghosts drift over water and up slopes alike (only a cave's hole turns them).
                    if (b.Haunts)
                    {
                        blocked = s.Speed > 0.05f && Terrain.IsCaveHole(ahead);
                        landBound = false;
                    }
                    if (b.CrossesWater && blocked && s.Mode != CreatureMode.Drink && s.Mode != CreatureMode.Graze && Swimmable(ahead))
                    {
                        blocked = false;
                        landBound = false;
                    }
                    if (b.CrossesWater && WaterDepth(s.Position.xz) > b.StandHeight * s.Scale * 0.6f)
                    {
                        landBound = false;
                        s.Speed = math.min(s.Speed, run * 0.45f);   // paddling is slow
                    }
                    // (In a cave, running or going somewhere along the passage, it follows the wall round: Advance.)
                    var followsWall = CaveWalker(s, ref b) && (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Drink ||
                                                        s.Mode == CreatureMode.Wander && math.distance(s.Position.xz, s.Target) > 2f);
                    if (!followsWall && (blocked || landBound && s.Speed > 0.05f && SlopeAhead(s.Position.xz, ahead) > 0.8f))
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
                // A glance at something near and interesting, over whatever else it was looking at. Not while the head is
                // busy with something of its own (preening, grazing, drinking, a head-down posture, reading...): then
                // the look eases back to straight ahead and leaves the head to the action.
                var desiredPitch = 0f;
                if (HeadBusy(s))
                {
                    s.LookFor = 0f;
                    desiredLook = 0f;
                }
                else if (s.Task != CreatureTask.Critter)
                    Glance(self, ref s, ref b, ref rng, dt, player, playerSpeed, emote, ref desiredLook, out desiredPitch);
                // Never a snap: eased, and no faster than a bird's 180 or a mammal's 120 degrees a second.
                var turnRate = (b.Habitat == Habitat.Flies || b.Nests != NestSite.None ? 3.1f : 2.1f) * dt;
                s.Look += math.clamp((desiredLook - s.Look) * math.min(1f, dt * 5f), -turnRate, turnRate);
                s.LookPitch += math.clamp((desiredPitch - s.LookPitch) * math.min(1f, dt * 5f), -turnRate, turnRate);

                // Ease into postures: lying down takes a moment, springing up to run doesn't.
                var lieRate = lie > s.Lie ? 0.8f : urgent ? 5f : 1.5f;
                s.Lie += (lie - s.Lie) * math.min(1f, dt * lieRate);
                s.HeadDown += (headDown - s.HeadDown) * math.min(1f, dt * (urgent ? 5f : 2f));
                s.Bow += (bow - s.Bow) * math.min(1f, dt * 5f);
                s.Curl += (curl - s.Curl) * math.min(1f, dt * (curl > s.Curl ? 0.6f : 4f));
                s.Sit += (sit - s.Sit) * math.min(1f, dt * (urgent ? 6f : 2.5f));
                s.Vertical += (vertical - s.Vertical) * math.min(1f, dt * 3f);
                var fly = Flying(s) && (flier || b.Breaches && s.Mode == CreatureMode.Wander && s.Stage >= 2) ? 1f : 0f;
                // Folk out and about after dark carry a lantern: lit at dusk, put away at dawn (with a margin either way so
                // it doesn't flicker at the turn), and not asleep, indoors, dozing at a post, or in someone's arms.
                // (A guard by day has its weapon, worn or drawn: Patrol works out which.)
                if (s.Task == CreatureTask.Patrol && s.Mode == CreatureMode.Patrol)
                    s.Held = Night > 0.5f ? HeldItem.Torch : s.Held == HeldItem.SpearDrawn ? HeldItem.SpearDrawn : HeldItem.Spear;
                else if (s.Task == CreatureTask.Fish && s.Mode == CreatureMode.Work && s.Stage >= 1)
                    s.Held = HeldItem.Rod;
                // A traveller has its staff by day, a lantern after dark.
                else if (s.Task == CreatureTask.Travel && s.Mode == CreatureMode.Work)
                    s.Held = Night > 0.5f ? HeldItem.Lantern : HeldItem.Staff;
                // Reading a message just taken from a messenger bird. (The bird's own letter, Messenger sees to.)
                else if (s.Mode == CreatureMode.Idle && s.Stage == MessageRead && b.Hands)
                    s.Held = HeldItem.Letter;
                // (A miner at work has its pick, or a sack of ore: Mining says which.)
                else if (s.Task == CreatureTask.Mine && s.Mode == CreatureMode.Work)
                {
                    if (s.Held != HeldItem.Pickaxe && s.Held != HeldItem.OreSack)
                        s.Held = HeldItem.Pickaxe;
                }
                // A hunter by day wears its spear across its back, and draws it when it goes after game (put away
                // when it's done): the same half-second reach as a guard's weapon. (After dark, a lantern below.)
                else if (s.Task == CreatureTask.Hunt && b.Hands && Night < 0.5f)
                {
                    if (s.Held != HeldItem.Spear && s.Held != HeldItem.SpearDrawn)
                        s.Held = HeldItem.Spear;
                    var hunting = s.Mode == CreatureMode.Hunt;
                    if (s.Action == IdleAction.None && hunting != (s.Held == HeldItem.SpearDrawn))
                    {
                        s.Held = hunting ? HeldItem.SpearDrawn : HeldItem.Spear;
                        s.Action = hunting ? IdleAction.Draw : IdleAction.Sheathe;
                        s.ActionTime = 0f;
                        s.ActionLength = 0.5f;
                    }
                }
                // One of the folk after a hive's honey: a stick to poke at the comb, then the comb itself (Raiding).
                else if (b.Hands && s.Mode == CreatureMode.Work && s.Stage >= CreatureAnimator.RaidWalk && s.Stage <= CreatureAnimator.RaidLick)
                {
                    if (s.Held != HeldItem.Honey)
                        s.Held = HeldItem.Stick;
                }
                // A child out by day has a stick or a stone (its own kind of plaything), not indoors or asleep.
                else if (b.Child && b.Hands && Night < 0.5f && s.Mode != CreatureMode.Sleep && !(s.Mode == CreatureMode.Home && s.Stage >= 1))
                    s.Held = ((s.Individual >> 5) & 1u) != 0 ? HeldItem.Stick : HeldItem.Stone;
                else if (b.Hands && b.Upright)
                {
                    var dark = s.Held == HeldItem.Lantern || s.Held == HeldItem.Torch ? Night > 0.4f : Night > 0.6f;
                    // (By the town's fire it has no need of one: put away.)
                    var about = s.Mode != CreatureMode.Sleep && s.Mode != CreatureMode.Held && !(s.Mode == CreatureMode.Home && s.Stage >= 1) &&
                                !(s.Mode == CreatureMode.Work && s.Stage >= 1) && !(s.Mode == CreatureMode.Sit && AtHearth(s.Stage));
                    // (A lantern for the folk who make them; goblins and ogres carry a burning brand.)
                    var light = b.GuardKit == GuardKit.Goblin || b.GuardKit == GuardKit.Ogre ? HeldItem.Torch : HeldItem.Lantern;
                    s.Held = dark && about ? light : HeldItem.None;
                }
                s.Fly += (fly - s.Fly) * math.min(1f, dt * 6f);
                s.Seed = rng.state;
            }

            // ------------------------------------------------------------------------------- needs

            /// <summary>
            /// Needs rise over time (by the species' tastes: a big grazer gets hungry faster) and fall while the
            /// activity that meets them goes on: grazing or a hunt for hunger, drinking for thirst, rest and sleep for
            /// tiredness, play, a flock or company for loneliness.
            /// </summary>
            static void Needs(ref CreatureState s, ref CreatureBlob b, float dt)
            {
                using var profiled = NeedsMarker.Auto();
                s.Unsettled = math.max(s.Unsettled - dt, 0f);
                s.Hunger += dt * (0.002f + (b.Grazing + b.Hunger) * 0.003f);
                s.Thirst += dt * b.Thirst * 0.004f;
                s.Tired += dt * 0.0015f;
                s.Lonely += dt * b.Playfulness * 0.003f;
                switch (s.Mode)
                {
                    case CreatureMode.Graze: s.Hunger -= dt * 0.04f; break;
                    case CreatureMode.Hunt: s.Hunger -= dt * 0.01f; break;
                    case CreatureMode.Drink: if (s.Stage == 1) s.Thirst -= dt * 0.12f; break;
                    case CreatureMode.Rest: s.Tired -= dt * 0.02f; break;
                    case CreatureMode.Sleep: s.Tired -= dt * 0.04f; break;
                    case CreatureMode.Burrow: if (s.Stage == 2) s.Tired -= dt * 0.03f; break;
                    case CreatureMode.Home: if (s.Stage == 2) s.Tired -= dt * 0.04f; break;
                    case CreatureMode.Roost: if (s.Stage == 200) s.Tired -= dt * 0.04f; break;
                    case CreatureMode.Climb: if (s.Stage == 3) s.Tired -= dt * 0.02f; break;
                    case CreatureMode.Play: s.Lonely -= dt * 0.05f; break;
                    case CreatureMode.Greet: s.Lonely -= dt * 0.03f; break;
                    case CreatureMode.Petted: s.Lonely -= dt * 0.08f; break;
                }
                s.Hunger = math.saturate(s.Hunger);
                s.Thirst = math.saturate(s.Thirst);
                s.Tired = math.saturate(s.Tired);
                s.Lonely = math.saturate(s.Lonely);
            }

            /// <summary>How much a need pushes an activity: a little even when met, a lot when pressing.</summary>
            static float Urge(float need) => 0.25f + need * need * 3f;

            // ------------------------------------------------------------------------------- choosing what to do

            /// <summary>How much this is its time to sleep (0..1).</summary>
            float SleepTime(ref CreatureBlob b) =>
                b.Active == ActiveTime.Day ? Night : b.Active == ActiveTime.Night ? 1f - Night : 0f;

            void Decide(Entity self, ref CreatureState s, ref CreatureBlob b, int species, ref Random rng, bool perched, float3 player)
            {
                using var profiled = DecideMarker.Auto();
                if (b.DrawnToLight)
                {
                    Flit(ref s, ref rng);
                    return;
                }
                var sleepy = SleepTime(ref b);
                var awake = 1f - sleepy * 0.8f;
                var flier = b.Habitat == Habitat.Flies;

                // A sky serpent, awake under the water, now and then bursts up and out to soar a while.
                if (b.Breaches && sleepy < 0.5f && rng.NextFloat() < 0.3f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 2;
                    s.Focus = s.Position.xz;
                    s.TargetHeight = Cruise(s, ref b, s.Focus);
                    s.Timer = rng.NextFloat(180f, 480f);   // (long spells: it spends much of its day in the air)
                    s.Target = s.Focus;
                    return;
                }

                // A guard on shift goes (back) on its rounds; a night guard off shift sleeps the day away indoors (a day
                // guard off shift has its evening and goes home to bed like anyone).
                if (s.Task == CreatureTask.Patrol)
                {
                    if (OnShift(s) && StartPatrol(ref s))
                        return;
                    if (NightShift(s) && Night < 0.45f)
                    {
                        if (FindDoor(ref s))
                        {
                            SetMode(ref s, CreatureMode.Home);
                            s.Timer = rng.NextFloat(300f, 600f);
                        }
                        else
                        {
                            SetMode(ref s, CreatureMode.Sleep);
                            s.Timer = rng.NextFloat(120f, 300f);
                        }
                        return;
                    }
                }

                // A hermit's day at its shelter, and a traveller's night at a roadside camp.
                if (s.Task == CreatureTask.Hermit && HermitDay(ref s, ref b, ref rng, sleepy))
                    return;
                if (s.Task == CreatureTask.Camper && CamperNight(ref s, ref rng))
                    return;

                // A fisher finds its spot the first time (none within reach, and it's just one of the townsfolk).
                if (s.Task == CreatureTask.Fish && sleepy < 0.5f && math.all(s.WorkSpot == float2.zero) && !FindFishingSpot(ref s))
                    s.Task = CreatureTask.None;

                // Rooted to the spot: it only waits, awake or (in its sleeping hours) closed up asleep.
                if (b.Rooted)
                {
                    SetMode(ref s, sleepy > 0.5f ? CreatureMode.Sleep : CreatureMode.Idle);
                    s.Timer = sleepy > 0.5f ? rng.NextFloat(90f, 300f) : rng.NextFloat(5f, 15f);
                    return;
                }
                // Passing for a plant: it mostly sits still, and only now and then (in its waking hours) creeps off
                // somewhere new.
                if (b.Disguised && (sleepy > 0.5f || rng.NextFloat() < 0.85f))
                {
                    SetMode(ref s, sleepy > 0.5f ? CreatureMode.Sleep : CreatureMode.Rest);
                    s.Timer = rng.NextFloat(30f, 90f);
                    return;
                }

                // Find company first: the options depend on it.
                var playmate = Entity.Null;
                // (A critter runs light: no company to look for.)
                if (b.Playfulness > 0f && sleepy < 0.5f && !perched && s.Task != CreatureTask.Critter)
                    FindKin(self, ref s, species, 15f, out playmate);
                var prey = Entity.Null;
                if (b.Prey.Length > 0 && b.Hunger > 0f && sleepy < 0.5f)
                    FindPrey(self, ref s, ref b, flier ? 60f : 35f, out prey);

                // On a branch, a bird can only rest, sleep or fly off.
                var onGround = !perched;
                // Each activity's appeal: the species' taste for it, pressed by the need it meets.
                var wander = 1f * awake * (onGround ? 1f : 0f);
                var graze = b.Grazing * 2.5f * awake * (onGround ? 1f : 0f) * Urge(s.Hunger);
                var rest = b.Laziness * 0.7f * Urge(s.Tired);
                var sleep = sleepy > 0.5f ? 6f : b.Active == ActiveTime.Always ? b.Laziness * 0.4f * Urge(s.Tired) : 0f;
                var drink = b.Thirst * 1.2f * awake * (onGround && !flier ? 1f : 0f) * Urge(s.Thirst);
                var play = playmate != Entity.Null ? b.Playfulness * 2f * Urge(s.Lonely) : 0f;
                var hunt = prey != Entity.Null ? b.Hunger * 2.5f * Urge(s.Hunger) : 0f;
                // Climbers go up trees to sit, and to sleep (they sleep up there, not on the ground).
                var climb = b.Climbing > 0f && HasFlora && !flier ? b.Climbing * (sleepy > 0.5f ? 8f : 2.5f) : 0f;
                // Townsfolk sit a while by the well, or lie back on the ground near it, by day.
                var lounge = b.Upright && b.Hands && s.Leash > 0f && sleepy < 0.3f && Night < 0.3f && s.Task != CreatureTask.Patrol ? 0.2f * awake : 0f;
                // Townsfolk now and then go for a swim, in the warm middle of the day.
                var swim = b.Upright && b.Hands && s.Leash > 0f && sleepy < 0.2f && Night < 0.2f && s.Task != CreatureTask.Patrol ? 0.12f * awake : 0f;
                if (climb > 0f && sleepy > 0.5f)
                    sleep *= 0.2f;
                // Web spinners spend most of their time in their web.
                var web = b.SpinsWebs ? 6f : 0f;
                // Birds fly off somewhere new more often from a branch; to sleep, they go up into a tree first.
                var fly = flier ? (perched ? 2f : 1.2f) * awake : 0f;
                // Nesters pick their nest the first time it's needed, and go home to it to sleep.
                if (b.Nests != NestSite.None && s.AnchorSize == 0f)
                    FindNest(ref s, ref b);
                var nest = b.Nests != NestSite.None && s.AnchorSize > 0f;
                var awayFromNest = nest && math.distance(s.Position, s.Anchor) > 0.5f;
                if (flier && sleepy > 0.5f && HasFlora && (!perched || awayFromNest))
                {
                    fly = 8f;
                    sleep = 0f;
                }
                else if (nest && sleepy > 0.5f && !awayFromNest)
                    fly = 0f;   // home in the nest: stays put till morning
                if (!flier && nest && sleepy > 0.5f && awayFromNest)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = s.Anchor.xz;
                    return;
                }
                // Workers mostly work by day, taking a short wander or rest between stints; at night they doze at
                // their post.
                // (Hunters have no post: by day they go out after game, at night they sleep at home.)
                var posted = s.Task != CreatureTask.None && s.Task != CreatureTask.Hunt && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Critter;
                // (A fisher fishes by day only: at night it sleeps at home like anyone.)
                var dayWork = s.Task == CreatureTask.Fish || s.Task == CreatureTask.Mine;
                var work = posted ? sleepy > 0.5f ? (dayWork ? 0f : 10f) : 6f : 0f;
                if (posted && sleepy > 0.5f && !dayWork)
                    sleep = 0f;
                // Townsfolk go indoors through their door: for the night (all but the watch), and now and then by day.
                var home = 0f;
                if (b.Upright && s.Leash > 0f && s.Task != CreatureTask.Watch && FindDoor(ref s))
                {
                    home = sleepy > 0.5f ? 14f : 0.15f * awake;
                    if (sleepy > 0.5f)
                        work = sleep = 0f;
                }
                if (s.Task == CreatureTask.Hunt && sleepy < 0.5f && rng.NextFloat() < 0.6f && FindGame(self, ref s, 100f, out var game))
                {
                    SetMode(ref s, CreatureMode.Hunt);
                    s.Partner = game;
                    s.Stage = FolkHunting.Stalk;
                    s.Timer = rng.NextFloat(60f, 100f);
                    return;
                }
                // A bear by day, now and then, goes after a wild hive's honey (rarely: after a raid it leaves hives be a
                // long while).
                if (b.RaidsHives && sleepy < 0.3f && s.ChaseCooldown <= 0f && rng.NextFloat() < 0.08f && StartRaid(ref s, ref rng))
                    return;
                // Children by day now and then sit down in a ring on a green to play with their sticks and stones.
                if (b.Child && sleepy < 0.3f && Night < 0.4f && s.Leash > 0f && rng.NextFloat() < 0.3f && StartPlayRing(self, ref s, ref rng))
                    return;
                // Townsfolk gather round the town's fire at dusk, as many as it has room for (the rest go in as usual).
                if (Gathering(in s, ref b) && rng.NextFloat() < 0.8f && StartHearth(self, ref s, ref rng))
                    return;
                var roll = rng.NextFloat(wander + graze + rest + sleep + drink + play + hunt + work + climb + web + fly + home + swim + lounge);

                // Friendly animals go over to a person nearby (within their home range) more often than not.
                // (Someone arriving at the edge of its home range counts: it goes as far as its range lets it.)
                var nearPerson = math.distance(player.xz, s.Position.xz) < 35f &&
                                 (s.Leash <= 0f || math.distance(player.xz, s.Home) < s.Leash + 15f);
                if (b.Friendly && nearPerson && sleepy < 0.5f && rng.NextFloat() < 0.35f + s.Lonely * 0.6f)
                {
                    SetMode(ref s, CreatureMode.Greet);
                    s.Timer = rng.NextFloat(20f, 45f);
                    return;
                }
                // Roosting species: hang up to sleep through their sleeping time, and otherwise stay on the wing.
                if (b.Roosts)
                {
                    // (Startled off its roost, it flies about a while before it settles again.)
                    if (sleepy > 0.5f && s.Unsettled <= 0f)
                        StartRoost(ref s, ref b, ref rng);
                    else
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                // Burrowers sleep down their burrow (digging one where they are if they have none yet).
                if (b.Burrows && s.AnchorSize < 0f && s.Leash <= 0f &&
                    math.any((float2)s.Anchor.xz != (math.floor(s.Position.xz / 16f) + 0.5f) * 16f))
                    s.AnchorSize = 0f;   // somewhere new since it last found no room: look again
                if (b.Burrows && sleep > 0f && sleepy > 0.5f && (s.AnchorSize > 0f || s.AnchorSize == 0f && DigBurrow(ref s, ref b, ref rng)))
                {
                    SetMode(ref s, CreatureMode.Burrow);
                    s.Timer = rng.NextFloat(120f, 400f);
                    return;
                }
                if (b.Burrows && s.AnchorSize == 0f)
                    DigBurrow(ref s, ref b, ref rng);

                if ((roll -= work) < 0f)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.Timer = s.Task == CreatureTask.Fish ? rng.NextFloat(120f, 300f) : rng.NextFloat(40f, 120f);
                }
                else if ((roll -= home) < 0f)
                {
                    SetMode(ref s, CreatureMode.Home);
                    // (At night it stays in till morning: the time only runs by day.)
                    s.Timer = rng.NextFloat(15f, 45f);
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
                else if ((roll -= drink) < 0f && (CaveWalker(s, ref b) ? FindCaveWater(ref s, ref b, ref rng, out var shore, out var water)
                                                                 : FindWater(ref s, ref b, ref rng, out shore, out water)))
                {
                    SetMode(ref s, CreatureMode.Drink);
                    s.Target = shore;
                    s.Focus = water;
                }
                // (Only for species that do these: a failed drink above leaves the roll below zero.)
                else if (climb > 0f && (roll -= climb) < 0f && StartClimb(ref s, ref b, ref rng, sleepy > 0.5f)) { }
                else if (swim > 0f && (roll -= swim) < 0f && StartSwim(ref s, ref rng)) { }
                else if (lounge > 0f && (roll -= lounge) < 0f && StartLounge(self, ref s, ref rng)) { }
                else if (web > 0f && (roll -= web) < 0f && GoToWeb(ref s, ref b, ref rng)) { }
                else if (fly > 0f && (roll -= fly) < 0f && StartFlight(ref s, ref b, ref rng, sleepy > 0.5f)) { }
                else if (onGround)
                {
                    var candidate = s.Position.xz + rng.NextFloat2Direction() * b.WanderRadius * rng.NextFloat(0.3f, 1f) * (flier ? 0.15f : 1f);
                    // Animals that swim now and then set off for the far side of water in front of them.
                    if (b.CrossesWater && s.Leash <= 0f && rng.NextFloat() < 0.3f && FarShore(s.Position.xz, ref rng, out var farShore))
                        candidate = farShore;
                    // Animals with a home stay near it.
                    if (s.Leash > 0f && math.distance(candidate, s.Home) > s.Leash)
                        candidate = s.Home + rng.NextFloat2Direction() * s.Leash * rng.NextFloat(0.1f, 0.8f);
                    // A crawler in a cave: off over the rock, onto a wall or the ceiling as likely as the floor.
                    if (b.Crawls && CaveWalker(s, ref b))
                    {
                        if (CrawlTarget(s, ref b, ref rng, out var spot))
                        {
                            SetMode(ref s, CreatureMode.Wander);
                            s.Target = spot.xz;
                            s.TargetHeight = spot.y;
                        }
                        else
                            s.Timer = 0.5f;
                    }
                    // A lizard outside makes for steep, sunny rock (of a few places, the steepest).
                    else if (b.Crawls && b.Habitat == Habitat.Land && CaveWalkerNot(s, ref b) && SteepestOf(s.Position.xz, b.WanderRadius, ref rng, out var face))
                    {
                        SetMode(ref s, CreatureMode.Wander);
                        s.Target = face;
                    }
                    else if ((CaveWalker(s, ref b) ? CaveFloor(candidate, s.Position.y) : b.Habitat == Habitat.Land ? OpenGround(candidate) : Walkable(candidate, b.Habitat)) &&
                        (!b.LavaDweller || InLava(candidate)))
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
            void Fidget(Entity self, int species, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool perched,
                        ref float sit, ref float vertical, ref float bow, ref float desiredLook)
            {
                using var profiled = FidgetMarker.Auto();
                var lying = s.Mode == CreatureMode.Rest && s.Lie > 0.5f;
                var calm = s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Rest ||
                           s.Mode == CreatureMode.Climb && s.Stage == 3 ||
                           s.Mode == CreatureMode.Greet && s.Speed < 0.2f;
                // (Being petted or held, it carries on with what it was given to do: a wag, a wriggle; nothing new. A
                // guard drawing its weapon or putting it away finishes, whatever it's doing.)
                var keeps = calm || s.Mode == CreatureMode.Petted || s.Mode == CreatureMode.Held ||
                            s.Action == IdleAction.Draw || s.Action == IdleAction.Sheathe ||
                            s.Mode == CreatureMode.Sit && s.Stage == CreatureAnimator.PlayRing;
                if (s.Action != IdleAction.None)
                {
                    s.ActionTime += dt / math.max(s.ActionLength, 0.1f);
                    if (s.ActionTime >= 1f || !keeps)
                    {
                        s.Action = IdleAction.None;
                        s.ActionTime = 0f;
                    }
                }
                else if (calm && b.Idles != IdleActions.None && s.Speed < 0.1f && !(b.Disguised && s.Mode == CreatureMode.Rest) &&
                         rng.NextFloat() < dt * b.Fidgets * 0.15f)
                    StartAction(ref s, ref b, ref rng, lying, perched || s.Mode == CreatureMode.Climb, Night);
                // A howl is taken up by the rest of the pack, one after another.
                else if (calm && !lying && (b.Idles & IdleActions.Howl) != 0 && s.Speed < 0.1f && Night > 0.5f &&
                         rng.NextFloat() < dt * 0.8f && Howling(self, ref s, species))
                {
                    s.Action = IdleAction.Howl;
                    s.ActionTime = 0f;
                    s.ActionLength = rng.NextFloat(4f, 7f);
                }

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
                    case IdleAction.Howl:
                        sit = 1f;
                        desiredLook = 0f;
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
            static void StartAction(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool lying, bool aloft, float night)
            {
                var options = new FixedList64Bytes<byte>();
                for (int a = 1; a <= (int)IdleAction.Howl; a++)
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
                                  action == IdleAction.SitUp || action == IdleAction.HeadBob || action == IdleAction.Howl))
                        continue;
                    // Howling is for the night (and not lying down).
                    if (action == IdleAction.Howl && (night < 0.5f || lying))
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
                    IdleAction.Howl => rng.NextFloat(4f, 7f),
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

            /// <summary>
            /// A rooted creature: the nearest thing within its reach (Flee Distance; half that asleep), the player or
            /// another animal, and now and then (every third of a second or so while it stays) it whips round and
            /// snaps at it, waking if it was asleep.
            /// </summary>
            void Snap(Entity self, ref CreatureState s, ref CreatureBlob b, float3 player, ref Random rng)
            {
                if (s.Action == IdleAction.Snap || b.FleeDistance <= 0f || rng.NextFloat() > Dt * 3f)
                    return;
                var reach = b.FleeDistance * s.Scale * (s.Mode == CreatureMode.Sleep ? 0.5f : 1f);
                var best = reach;
                var at = float2.zero;
                var found = false;
                var d = math.distance(player, s.Position);
                if (d < best)
                {
                    best = d;
                    at = player.xz;
                    found = true;
                }
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(reach / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || Bodies[j].Blob.Value.Rooted)
                            continue;
                        var e = math.distance(States[j].Position, s.Position);
                        if (e < best)
                        {
                            best = e;
                            at = States[j].Position.xz;
                            found = true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                if (!found)
                    return;
                if (s.Mode != CreatureMode.Idle)
                {
                    SetMode(ref s, CreatureMode.Idle);
                    s.Timer = rng.NextFloat(5f, 15f);
                }
                var to = at - s.Position.xz;
                if (math.lengthsq(to) > 1e-4f)
                    s.Yaw = math.atan2(to.x, to.y);
                s.Action = IdleAction.Snap;
                s.ActionTime = 0f;
                s.ActionLength = 0.7f;
            }

            static void Tired(ref CreatureState s, ref Random rng)
            {
                s.Hunger = math.max(0f, s.Hunger - 0.4f);
                SetMode(ref s, CreatureMode.Rest);
                s.Timer = rng.NextFloat(15f, 40f);
            }

            // ------------------------------------------------------------------------------- trees

            /// <summary>Picks a tree nearby and sets off to climb it (false if there's none).</summary>
            bool StartClimb(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                // (A burrower's Anchor is its burrow: it doesn't climb.)
                if (b.Burrows || !HasFlora || !FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 30f, 3.5f, ref rng, out var tree))
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
                // Standing at the foot (and out on a branch) the body keeps its legs' length off the trunk; up it, its feet
                // are on the bark and the body hangs a leg's length out from them (CreatureAnimator). It goes from one to
                // the other over its first stand height up the trunk, and back over its last coming down, so its chest
                // never ends up in the trunk.
                var standOff = s.AnchorSize + b.StandHeight * s.Scale * 0.8f + 0.02f;
                var low = math.saturate((s.Position.y - Terrain.SampleHeight(s.Position.xz)) / math.max(b.StandHeight * s.Scale, 0.05f));
                var reach = math.lerp(standOff, s.AnchorSize + 0.02f, math.min(math.saturate(s.Vertical), low));
                var outward = math.normalizesafe(s.Position.xz - trunk, new float2(0f, 1f));
                switch (s.Stage)
                {
                    case 0:
                        s.Target = trunk + outward * (standOff + 0.15f);
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 0.3f)
                            NextStage(ref s);
                        else if (s.ModeTime > 40f)
                        {
                            Pause(ref s, ref b, ref rng);
                            s.AnchorSize = 0f;
                        }
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
                        if (GlideTo(ref s, ref b, trunk + math.normalizesafe(s.Focus - trunk, outward) * standOff, walk * 0.6f, dt) < 0.1f)
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
                        {
                            Pause(ref s, ref b, ref rng);
                            s.AnchorSize = 0f;   // let go of the tree (AnchorSize > 0 means a lair to burrowers)
                        }
                        break;
                }
            }

            /// <summary>
            /// A sand or snow worm: deep under the ground, gliding from one flat spot to another round where it lives (the
            /// world draws its ripple and furrow on the surface from CreatureBurrowSystem); every half minute to minute and
            /// a half, out on flat ground, it bursts up out of it in a great arc, its body rippling up and down, and dives
            /// back in further on. (Air runs 0..1 through the arc; Fly holds its body's wave upright meanwhile.)
            /// </summary>
            void Tunnel(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var radius = math.max(b.FrontRadius * s.Scale, 0.3f);
                var depth = radius * 3f + 1f;
                if (s.Mode != CreatureMode.Wander)
                    SetMode(ref s, CreatureMode.Wander);
                if (math.all(s.Home == float2.zero))
                {
                    s.Home = s.Position.xz;
                    s.Timer = rng.NextFloat(10f, 40f);
                }
                var ground = Terrain.SampleHeight(s.Position.xz);
                if (s.Air > 0f)
                {
                    // Out of the ground and back, over four seconds, at speed.
                    s.Air += dt / 4f;
                    s.Speed = b.RunSpeed;
                    s.Fly = 1f;
                    if (s.Air >= 1f)
                    {
                        s.Air = 0f;
                        s.Fly = 0f;
                        s.Timer = rng.NextFloat(30f, 80f);
                    }
                }
                else
                {
                    s.Speed = b.WalkSpeed;
                    s.Fly = 0f;
                    s.Timer -= dt;
                    // Somewhere flat to make for: the flattest of a few spots round its home.
                    if (math.distance(s.Position.xz, s.Target) < 4f || math.all(s.Target == float2.zero))
                    {
                        var best = -1f;
                        for (int k = 0; k < 6; k++)
                        {
                            var p = s.Home + rng.NextFloat2Direction() * rng.NextFloat(0.2f, 1f) * b.WanderRadius;
                            var g = Terrain.SampleGround(p);
                            if (g.HasWater && g.WaterSurface > g.Height)
                                continue;
                            var flat = Terrain.SampleNormal(p, 3f).y;
                            if (flat > best)
                            {
                                best = flat;
                                s.Target = p;
                            }
                        }
                    }
                    // Its time come and the ground flat here (dunes, a snowfield): up it bursts. (Not yet: soon again.)
                    if (s.Timer <= 0f)
                    {
                        var here = Terrain.SampleGround(s.Position.xz);
                        if (!(here.HasWater && here.WaterSurface > here.Height) && Terrain.SampleNormal(s.Position.xz, 3f).y > 0.97f)
                        {
                            s.Air = 0.001f;
                            s.AirHeight = radius * 6f;
                        }
                        else
                            s.Timer = 3f;
                    }
                }
                TurnTowards(ref s, ref b, s.Target, dt);
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                ground = Terrain.SampleHeight(s.Position.xz);
                s.Position.y = ground - depth + (depth + s.AirHeight) * (s.Air > 0f ? math.sin(math.PI * s.Air) : 0f);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
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
                CreatureMotion.Accelerate(ref s, ref b, dt);
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
            /// A spot for a web, only ever on something big and solid (never a mushroom or a flower): best strung across
            /// the gap between two things 1.5–4 m apart (two trunks, a trunk and a boulder, two posts or a palisade's
            /// stakes, a cave passage's two walls), hung no higher than the lower of them; failing that, beside one tall
            /// trunk; failing that, none. From the place, like burrows: the same web site whenever a spider lives here.
            /// </summary>
            bool PlaceWeb(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                var radius = b.WebSize * s.Scale;
                // (Each spider of a place its own: two don't share a web.)
                var place = LairRandom(s.Position.xz, 0xA11Eu ^ s.Individual);
                var cellCentre = (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                if (CaveWalker(s, ref b))
                    return CaveWeb(ref s, radius, cellCentre, ref place);
                if (!HasFlora)
                    return false;
                var anchors = new FixedList512Bytes<FloraPlacement.WebAnchor>();
                FloraPlacement.NearAnchors(ref Flora.Value, Terrain, cellCentre, 12f, 1.2f, ref anchors);

                // Every pair the right distance apart, one of them at random (the same one each time: place's draw).
                var found = 0;
                float3 bestCentre = default;
                float2 bestDir = default;
                var bestSpan = 0f;
                for (int i = 0; i < anchors.Length; i++)
                for (int j = i + 1; j < anchors.Length; j++)
                {
                    var a = anchors[i];
                    var c = anchors[j];
                    var dir = c.Base.xz - a.Base.xz;
                    var apart = math.length(dir);
                    dir /= math.max(apart, 1e-4f);
                    var gap = apart - a.Radius - c.Radius;
                    if (gap < math.max(1.5f, radius * 2.2f) || gap > 4f)
                        continue;
                    var mid = a.Base.xz + dir * (a.Radius + gap * 0.5f);
                    if (!Walkable(mid, Habitat.Land))
                        continue;
                    var ground = Terrain.SampleHeight(mid);
                    // Its top within both: hung from the lower one's top down, its foot clear of the ground.
                    var y = math.min(ground + radius + 0.4f, math.min(a.Top, c.Top) - 0.1f - radius);
                    if (y - radius < ground + 0.1f)
                        continue;
                    found++;
                    if (place.NextFloat() * found >= 1f)
                        continue;
                    bestCentre = new float3(mid.x, y, mid.y);
                    bestDir = dir;
                    bestSpan = gap * 0.5f;
                }
                if (found > 0)
                {
                    SetWeb(ref s, bestCentre, bestDir, bestSpan, place.NextBool());
                    return true;
                }

                // One tall trunk: the web beside it, its edge just off the bark.
                found = 0;
                var trunk = -1;
                for (int i = 0; i < anchors.Length; i++)
                    if (anchors[i].Trunk && anchors[i].Top - anchors[i].Base.y >= 2f && place.NextFloat() * ++found < 1f)
                        trunk = i;
                if (trunk < 0)
                    return false;
                var tree = anchors[trunk];
                var yaw = place.NextFloat(-math.PI, math.PI);
                var right = new float2(math.cos(yaw), -math.sin(yaw));
                // Trunk to the spider's left (side 1: centre to the trunk's right) or right (side 0).
                var toTheRight = place.NextBool();
                var centre = tree.Base.xz + right * (toTheRight ? 1f : -1f) * (tree.Radius + radius * 1.02f);
                if (!Walkable(centre, Habitat.Land))
                    return false;
                s.Anchor = new float3(centre.x, Terrain.SampleHeight(centre) + radius + 0.25f, centre.y);
                s.AnchorSize = radius;
                s.AnchorYaw = yaw;
                s.AnchorSide = (byte)(toTheRight ? 1 : 0);
                return true;
            }

            /// <summary>
            /// A web strung between two anchors (side 2): its centre, the way across (from one to the other), and how
            /// far each anchor's surface is from the centre (kept in AnchorSize, so every machine draws the threads
            /// out to them). The spider faces one way out of it or the other.
            /// </summary>
            static void SetWeb(ref CreatureState s, float3 centre, float2 across, float span, bool flip)
            {
                s.Anchor = centre;
                s.AnchorSize = span;
                // (The web's plane faces the spider's heading, its right along the gap.)
                s.AnchorYaw = WrapAngle(math.atan2(-across.y, across.x) + (flip ? math.PI : 0f));
                s.AnchorSide = 2;
            }

            /// <summary>
            /// A cave web: across a passage where its two walls are 1.5–5 m apart, a little above the floor and clear of
            /// the roof; failing that, beside the nearest wall (as beside a trunk). Spots tried round where the spider
            /// is (the same for the same few metres of cave, each time), on its level of the cave.
            /// </summary>
            bool CaveWeb(ref CreatureState s, float radius, float2 cellCentre, ref Random place)
            {
                if (!Terrain.Caves.IsCreated)
                    return false;
                var near = (math.floor(s.Position.xz / 4f) + 0.5f) * 4f;
                var wallCentre = float3.zero;
                var wallAcross = float3.zero;
                var byWall = false;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    var xz = near + place.NextFloat2Direction() * place.NextFloat(0f, 4f);
                    var floor = Terrain.FloorUnder(new float3(xz.x, s.Position.y + 0.5f, xz.y), out var inCave);
                    if (!inCave || math.abs(floor - s.Position.y) > 3f)
                        continue;
                    var centre = new float3(xz.x, floor + radius + 0.3f, xz.y);
                    if (Terrain.CaveDistance(centre) > -0.2f || Terrain.CaveDistance(centre + new float3(0f, radius * 0.9f, 0f)) > 0f)
                        continue;
                    // Across the passage: towards the nearest wall (the way the distance field rises fastest).
                    const float e = 0.2f;
                    var across = math.normalizesafe(new float3(
                        Terrain.CaveDistance(centre + new float3(e, 0f, 0f)) - Terrain.CaveDistance(centre - new float3(e, 0f, 0f)), 0f,
                        Terrain.CaveDistance(centre + new float3(0f, 0f, e)) - Terrain.CaveDistance(centre - new float3(0f, 0f, e))),
                        new float3(1f, 0f, 0f));
                    // Out to each wall.
                    float left = 0f, right = 0f;
                    for (; left < 4f && Terrain.CaveDistance(centre - across * left) < 0f; left += 0.1f) { }
                    for (; right < 4f && Terrain.CaveDistance(centre + across * right) < 0f; right += 0.1f) { }
                    var gap = left + right;
                    if (left >= 4f || right >= 4f || gap < math.max(1.5f, radius * 2.2f) || gap > 5f)
                    {
                        // (The first spot near a wall with room for the web beside it, in case there's no crossing.)
                        var beside = centre + across * (right - radius * 1.02f);
                        if (!byWall && right < 4f && right > 0.3f && Level(ref beside, radius))
                        {
                            byWall = true;
                            wallCentre = beside;
                            wallAcross = across;
                        }
                        continue;
                    }
                    var mid = centre + across * ((right - left) * 0.5f);
                    if (!Level(ref mid, radius))
                        continue;
                    SetWeb(ref s, mid, across.xz, gap * 0.5f, place.NextBool());
                    return true;
                }
                if (!byWall)
                    return false;
                // Beside the wall, the wall to the spider's right (side 0), as a web beside a trunk is drawn.
                s.Anchor = wallCentre;
                s.AnchorSize = radius;
                s.AnchorYaw = math.atan2(-wallAcross.z, wallAcross.x);
                s.AnchorSide = 0;
                return true;
            }

            /// <summary>
            /// A cave web's centre put at its height over the floor right under it (the floor rises towards the walls),
            /// if there's room there for the web: clear of the rock round it and of the roof.
            /// </summary>
            bool Level(ref float3 centre, float radius)
            {
                var floor = Terrain.FloorUnder(centre + new float3(0f, 0.5f, 0f), out var inCave);
                centre.y = floor + radius + 0.3f;
                return inCave && Terrain.CaveDistance(centre) < -0.2f && Terrain.CaveDistance(centre + new float3(0f, radius * 0.9f, 0f)) < 0f;
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
                    // (Down to whatever is under the web: the cave floor in a cave.)
                    var floor = Terrain.FloorUnder(s.Position + new float3(0f, 0.1f, 0f), out _);
                    s.Position.y = math.max(s.Position.y - s.Speed * dt, floor);
                    if (s.Position.y <= floor + 0.01f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- burrows and nests

            static Random LairRandom(float2 near, uint kind) => new Random(math.hash(new int3((int2)math.floor(near / 16f), (int)kind)) | 1u);

            /// <summary>
            /// Picks its nest, from the place like a burrow (the same nest whenever a bird lives here): a cup against a
            /// trunk low in a tree's crown, a platform of sticks on a treetop, or a scrape on flat ground (near home, for
            /// residents).
            /// </summary>
            bool FindNest(ref CreatureState s, ref CreatureBlob b)
            {
                var near = s.Leash > 0f ? s.Home : (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                var place = LairRandom(near, 0x4E57u ^ (s.Leash > 0f ? s.Individual : 0u));
                var size = math.max(b.BodyLength * s.Scale * 0.7f, 0.08f);
                if (b.Nests == NestSite.Ledge)
                {
                    // The Nest anchor nearest home (a cave mouth's wall): the colony's cups spread along the wall either
                    // side of it and a little up and down, each standing out from the rock.
                    if (!Terrain.Anchors.IsCreated)
                        return false;
                    var ledges = new NativeList<int>(8, Allocator.Temp);
                    Anchors.Near(ref Terrain.Anchors.Value, new float3(near.x, s.Position.y, near.y), 60f, AnchorKind.Nest, ref ledges);
                    var found = -1;
                    // (Not an egg clutch on a cave floor, which the world marks as a Nest too: those are Size 0.6, a
                    // ledge 0.4.)
                    for (int k = 0; k < ledges.Length; k++)
                        if (Terrain.Anchors.Value.Anchors[ledges[k]].Size > 0.5f)
                            continue;
                        else if (found < 0 || math.distancesq(Terrain.Anchors.Value.Anchors[ledges[k]].Position.xz, near) <
                                         math.distancesq(Terrain.Anchors.Value.Anchors[found].Position.xz, near))
                            found = ledges[k];
                    ledges.Dispose();
                    if (found >= 0)
                    {
                        var ledge = Terrain.Anchors.Value.Anchors[found];
                        var along = new float2(-ledge.Facing.y, ledge.Facing.x);
                        var spot = ledge.Position.xz + along * place.NextFloat(-1.4f, 1.4f);
                        var cup = new float3(spot.x, ledge.Position.y + place.NextFloat(-0.4f, 0.6f), spot.y);
                        // Onto the rock itself (the wall curves away from the anchor, or out into the way): in to the
                        // wall from there, or out of it, the cup just proud of the surface; failing that, at the anchor.
                        var into = new float3(-ledge.Facing.x, 0f, -ledge.Facing.y);
                        var inRock = Terrain.CaveDistance(cup) >= 0f;
                        var onRock = false;
                        for (int k = 1; k <= 25 && !onRock; k++)
                        {
                            var probe = cup + into * ((inRock ? -0.1f : 0.1f) * k);
                            if (Terrain.CaveDistance(probe) >= 0f != inRock)
                            {
                                // The surface lies between this probe and the one before: halved down to a few
                                // millimetres (air on one side, rock on the other).
                                var air = inRock ? probe : probe - into * 0.1f;
                                var rock = inRock ? probe - into * -0.1f : probe;
                                for (int h = 0; h < 5; h++)
                                {
                                    var mid = (air + rock) * 0.5f;
                                    if (Terrain.CaveDistance(mid) >= 0f)
                                        rock = mid;
                                    else
                                        air = mid;
                                }
                                cup = air - into * size * 0.6f;
                                onRock = true;
                            }
                        }
                        if (!onRock)
                            cup = ledge.Position + into * (0.3f - size * 0.6f);
                        s.Anchor = cup;
                        s.AnchorSize = size;
                        return true;
                    }
                }
                else if (b.Nests == NestSite.Ground)
                {
                    // A town bird's (a hen's): against a house wall beside its door, not out on the square.
                    if (s.Leash > 0f && Terrain.Anchors.IsCreated)
                    {
                        var doors = new NativeList<int>(8, Allocator.Temp);
                        Anchors.Near(ref Terrain.Anchors.Value, new float3(near.x, s.Position.y, near.y), math.max(s.Leash, 20f), AnchorKind.Door, ref doors);
                        var placed = false;
                        for (int attempt = 0; attempt < 6 && doors.Length > 0 && !placed; attempt++)
                        {
                            var door = Terrain.Anchors.Value.Anchors[doors[place.NextInt(doors.Length)]];
                            if (door.InCave)
                                continue;
                            var across = new float2(-door.Facing.y, door.Facing.x) * (place.NextBool() ? 1f : -1f);
                            var spot = door.Position.xz + across * place.NextFloat(1.2f, 2.2f) - door.Facing * 0.3f;
                            if (!Walkable(spot, Habitat.Land))
                                continue;
                            s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                            s.AnchorSize = size;
                            placed = true;
                        }
                        doors.Dispose();
                        if (placed)
                            return true;
                    }
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        var spot = near + place.NextFloat2Direction() * place.NextFloat(1f, 6f);
                        if (!Walkable(spot, Habitat.Land) || SlopeAhead(spot, spot + new float2(0.5f, 0f)) > 0.35f ||
                            SlopeAhead(spot, spot + new float2(0f, 0.5f)) > 0.35f)
                            continue;
                        s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                        s.AnchorSize = size;
                        return true;
                    }
                }
                else if (HasFlora && FloraPlacement.FindTree(ref Flora.Value, Terrain, near, 30f, b.Nests == NestSite.Treetop ? 6f : 3f, ref place, out var tree))
                {
                    if (b.Nests == NestSite.Treetop)
                    {
                        // Big and untidy, on the very top of the crown (in plain view, not inside the leaves).
                        s.AnchorSize = size * 1.4f;
                        // (A broadleaf's leaf clumps bulge about a third of the crown's radius above its height; a
                        // conifer ends in a spike at its height, the nest settled on it.)
                        var top = tree.Kind == TreeKind.Broadleaf ? tree.Height + tree.CrownRadius * 0.3f : tree.Height;
                        s.Anchor = tree.Base + new float3(0f, top - s.AnchorSize * 0.15f, 0f);
                    }
                    else
                    {
                        var dir = place.NextFloat2Direction();
                        var at = tree.Base.xz + dir * (tree.TrunkRadius + size * 0.8f);
                        s.Anchor = new float3(at.x, tree.Base.y + tree.CrownBase + (tree.Height - tree.CrownBase) * 0.25f, at.y);
                        s.AnchorSize = size;
                    }
                    return true;
                }
                s.AnchorSize = -1f;   // nowhere for one here: don't keep trying
                return false;
            }

            /// <summary>
            /// A moth's life: round and round a light. Its light is a Glow anchor (a glowing plant) near where it first
            /// looks, kept in Anchor; it flits to a point near it, and on getting there, to another.
            /// </summary>
            void Flit(ref CreatureState s, ref Random rng)
            {
                if (s.AnchorSize == 0f)
                {
                    if (Terrain.Anchors.IsCreated && Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 25f, AnchorKind.Glow, ref rng, out var glow))
                        s.Anchor = glow.Position + new float3(0f, 0.5f, 0f);
                    else
                        s.Anchor = s.Position + new float3(0f, 1f, 0f);   // no light: it flits about where it is
                    s.AnchorSize = -1f;   // (not a lair to draw)
                }
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = s.Anchor.xz + rng.NextFloat2Direction() * rng.NextFloat(0.15f, 1.1f);
                s.TargetHeight = s.Anchor.y + rng.NextFloat(-0.3f, 1.2f);
            }

            /// <summary>
            /// A moth's flight: straight at its point in three dimensions at its own pace, fluttering (no glide, no
            /// cruising height, no landing), turning to face the way it goes. True once it's there.
            /// </summary>
            bool FlitTo(ref CreatureState s, ref CreatureBlob b, float dt, ref float vertical)
            {
                var to = new float3(s.Target.x, s.TargetHeight, s.Target.y) - s.Position;
                var distance = math.length(to);
                s.Position += to * math.min(1f, b.FlySpeed * dt / math.max(distance, 1e-4f));
                var wanted = distance < 0.02f ? 0f : WrapAngle(math.atan2(to.x, to.z) - s.Yaw);
                var turn = math.clamp(wanted, -b.TurnRate * 1.5f * dt, b.TurnRate * 1.5f * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                s.Speed = b.FlySpeed;
                vertical = math.clamp(to.y / math.max(distance, 1e-3f), -0.5f, 0.5f) * 0.5f;
                return distance < 0.05f;
            }

            /// <summary>Picks a burrow mouth on flat, dry ground a few metres off (near home, for residents).</summary>
            bool DigBurrow(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                // Lairs come from the place (a 16 m cell of the world, round its centre, or a resident's home), so an
                // animal that despawns and comes back finds the same burrow, with nothing saved. Residents sharing a
                // home each get their own.
                var near = s.Leash > 0f ? s.Home : (math.floor(s.Position.xz / 16f) + 0.5f) * 16f;
                var place = LairRandom(near, 0x5B0Bu ^ (s.Leash > 0f ? s.Individual : 0u));
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    var spot = near + place.NextFloat2Direction() * place.NextFloat(1.5f, 6f);
                    if (!Walkable(spot, Habitat.Land) || SlopeAhead(spot, spot + new float2(0.5f, 0f)) > 0.35f ||
                        SlopeAhead(spot, spot + new float2(0f, 0.5f)) > 0.35f)
                        continue;
                    s.Anchor = new float3(spot.x, Terrain.SampleHeight(spot), spot.y);
                    s.AnchorSize = math.max(b.BodyLength * s.Scale * 0.8f, 0.18f);
                    return true;
                }
                // No room here: don't keep trying in this place (Anchor remembers which), but a wild one tries again once
                // it has moved on to another cell.
                s.AnchorSize = -1f;
                s.Anchor = new float3(near.x, 0f, near.y);
                return false;
            }

            /// <summary>
            /// To the burrow's mouth (running, if startled), down it, hidden a while (longer while the player is close),
            /// then up again to sit up and look about.
            /// </summary>
            void GoToGround(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float playerDistance,
                            ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                var mouth = s.Anchor;
                var down = mouth.y - (b.StandHeight + b.BodyLength) * s.Scale - 0.1f;
                switch (s.Stage)
                {
                    case 0:
                        s.Target = mouth.xz;
                        targetSpeed = s.ModeTime < 6f && s.Timer < 30f ? run : walk;
                        moving = true;
                        if (math.distance(s.Position.xz, mouth.xz) < 0.25f)
                            NextStage(ref s);
                        else if (s.ModeTime > 30f)
                            Pause(ref s, ref b, ref rng);
                        break;
                    case 1:
                        custom = true;
                        vertical = -0.7f;
                        s.Speed = 0f;
                        s.Position.xz = mouth.xz;
                        s.Position.y = math.max(s.Position.y - dt * 1.2f, down);
                        if (s.Position.y <= down + 0.01f)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Speed = 0f;
                        s.Position.y = down;
                        // Waits out danger: while the player is near, it stays down.
                        if (playerDistance > b.FleeDistance * s.Scale * 1.5f)
                            s.Timer -= dt;
                        if (s.Timer <= 0f)
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        vertical = 0.6f;
                        s.Speed = 0f;
                        s.Position.y = math.min(s.Position.y + dt * 0.8f, mouth.y);
                        if (s.Position.y >= mouth.y - 0.01f)
                        {
                            // Out, and straight up on its hind legs to look about (if it does that).
                            Pause(ref s, ref b, ref rng);
                            if ((b.Idles & IdleActions.SitUp) != 0)
                            {
                                s.Action = IdleAction.SitUp;
                                s.ActionTime = 0f;
                                s.ActionLength = rng.NextFloat(3f, 6f);
                            }
                        }
                        break;
                }
            }

            // ------------------------------------------------------------------------------- taking their ease

            /// <summary>
            /// Somewhere to sit or lie a while near home. Sitting: on a seat (a bench, a log, a stump, a fountain's rim,
            /// a hall's step: Sit anchors), or on the grass of a green, or on the ground by a well or fountain; mostly a
            /// seat, if there's one free. Lying back: on a green, or failing that the ground by a well. Each takes a slot
            /// of its own (by individual, the next along if that's taken), so two never sit in one another's lap:
            /// <list type="bullet">
            /// <item>a seat: one sitter to each place along it;</item>
            /// <item>a green: round its shade tree, a few metres out, facing out from it;</item>
            /// <item>a well: sitting in a row before it, facing it, an arm's length clear of the wall; lying in a second
            /// row further out, feet to it.</item>
            /// </list>
            /// False if there's nowhere in range, or no free slot.
            /// </summary>
            bool StartLounge(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                var lying = rng.NextFloat() < 0.4f;
                var roll = rng.NextFloat();
                // The order it looks in: lying, a green then a well; sitting, mostly a seat first.
                var order = lying ? new int3(1, 2, -1) : roll < 0.55f ? new int3(0, 1, 2) : roll < 0.8f ? new int3(1, 0, 2) : new int3(2, 0, 1);
                for (int o = 0; o < 3; o++)
                {
                    var kind = order[o] == 0 ? AnchorKind.Sit : order[o] == 1 ? AnchorKind.Green : order[o] == 2 ? AnchorKind.Well : (AnchorKind)255;
                    if ((byte)kind != 255 && LoungeAt(self, ref s, ref rng, kind, lying))
                        return true;
                }
                return false;
            }

            /// <summary>A free slot at one of the anchors of a kind near home (a few tried, at random), taken.</summary>
            bool LoungeAt(Entity self, ref CreatureState s, ref Random rng, AnchorKind kind, bool lying)
            {
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f), kind, ref near);
                var found = false;
                for (int tries = 0; tries < 3 && near.Length > 0 && !found; tries++)
                {
                    var pick = rng.NextInt(near.Length);
                    var a = set.Anchors[near[pick]];
                    near.RemoveAtSwapBack(pick);
                    if (a.InCave)
                        continue;
                    var across = new float2(-a.Facing.y, a.Facing.x);
                    var slots = kind == AnchorKind.Sit ? math.max((int)a.Capacity, 1) : kind == AnchorKind.Green ? math.max((int)a.Capacity, 4) : lying ? 4 : 5;
                    var room = kind == AnchorKind.Sit ? math.max(a.Size * 0.8f, 0.3f) : lying ? 1.6f : 0.9f;
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        float2 spot, face;
                        if (kind == AnchorKind.Sit)
                        {
                            spot = a.Position.xz + across * ((k - (slots - 1) * 0.5f) * a.Size);
                            face = spot + a.Facing * 3f;
                        }
                        else if (kind == AnchorKind.Green)
                        {
                            math.sincos(k * 2f * math.PI / slots + (a.Owner & 7) * 0.4f, out var sin, out var cos);
                            var outward = new float2(sin, cos);
                            spot = a.Position.xz + outward * math.max(a.Size * 0.6f, 2.5f);
                            face = spot + outward * 3f;
                        }
                        else
                        {
                            spot = lying ? a.Position.xz + across * ((k - 1.5f) * 2f) - a.Facing * 4f
                                         : a.Position.xz + across * ((k - 2) * 0.95f) - a.Facing * 1f;
                            face = spot + a.Facing * 3f;
                        }
                        if ((kind != AnchorKind.Sit && !Walkable(spot, Habitat.Land)) || Taken(self, spot, room))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = face;
                        // (On a seat: its height goes in Anchor, which every machine has, for the posture.)
                        s.TargetHeight = kind == AnchorKind.Sit ? 4f : lying ? 3f : 1f;
                        if (kind == AnchorKind.Sit)
                            s.Anchor = new float3(spot.x, a.Position.y, spot.y);
                        s.Timer = lying ? rng.NextFloat(80f, 200f) : rng.NextFloat(40f, 120f);
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>
            /// Whether this one would be at the town's fire now: townsfolk (not the watch or the guards, nor a traveller
            /// still on the road), from sunset until, by individual, a quarter to ten to ten past.
            /// </summary>
            bool Gathering(in CreatureState s, ref CreatureBlob b) =>
                b.Upright && b.Hands && s.Leash > 0f && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Watch &&
                s.Task != CreatureTask.Travel && Hour >= 17.8f && Hour < 21.95f + 0.2f * CreatureBlob.Trait(s.Individual, 41);

            static bool AtHearth(float stage) => stage >= CreatureAnimator.HearthStand && stage <= CreatureAnimator.HearthSeat;

            /// <summary>
            /// A place at the town's fire (its Hearth anchor) taken: round the ring, a free one of its places (Capacity of
            /// them, by individual and the next along if that's taken), sitting on the ground (legs out or cross-legged)
            /// or standing, facing the fire; or now and then a seat close by that faces it. It stays till it's time to go
            /// in (Gathering). False if there's no fire in reach, or no room at it.
            /// </summary>
            bool StartHearth(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(4, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f) + 20f, AnchorKind.Hearth, ref near);
                var found = false;
                var stand = rng.NextFloat() < 0.4f;
                var seat = rng.NextFloat() < 0.3f;
                for (int h = 0; h < near.Length && !found; h++)
                {
                    var a = set.Anchors[near[h]];
                    if (a.InCave)
                        continue;
                    var fire = a.Position.xz;
                    if (seat && HearthSeat(self, ref s, ref rng, fire, a.Size))
                    {
                        found = true;
                        break;
                    }
                    var slots = math.max((int)a.Capacity, 1);
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        math.sincos(k * 2f * math.PI / slots + (a.Owner & 7) * 0.4f, out var sin, out var cos);
                        var spot = fire + new float2(sin, cos) * (a.Size + (stand ? 0.1f : 0.25f));
                        if (!Walkable(spot, Habitat.Land) || Taken(self, spot, 0.9f))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = fire;
                        s.TargetHeight = stand ? CreatureAnimator.HearthStand : CreatureAnimator.HearthGround;
                        s.Timer = 9999f;   // (till it's time to go in)
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>A free place on a seat (bench, log, stump) near the fire and facing it, taken.</summary>
            bool HearthSeat(Entity self, ref CreatureState s, ref Random rng, float2 fire, float ring)
            {
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(fire.x, s.Position.y, fire.y), ring + 6f, AnchorKind.Sit, ref near);
                var found = false;
                for (int n = 0; n < near.Length && !found; n++)
                {
                    var a = set.Anchors[near[n]];
                    if (a.InCave || math.dot(a.Facing, math.normalizesafe(fire - a.Position.xz)) < 0.3f)
                        continue;
                    var across = new float2(-a.Facing.y, a.Facing.x);
                    var slots = math.max((int)a.Capacity, 1);
                    var first = (int)(s.Individual % (uint)slots);
                    for (int i = 0; i < slots && !found; i++)
                    {
                        var k = (first + i) % slots;
                        var spot = a.Position.xz + across * ((k - (slots - 1) * 0.5f) * a.Size);
                        if (Taken(self, spot, math.max(a.Size * 0.8f, 0.3f)))
                            continue;
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = spot;
                        s.Focus = spot + a.Facing * 3f;
                        s.TargetHeight = CreatureAnimator.HearthSeat;
                        s.Anchor = new float3(spot.x, a.Position.y, spot.y);
                        s.Timer = 9999f;
                        found = true;
                    }
                }
                near.Dispose();
                return found;
            }

            /// <summary>Another place of the same kind, for one beaten to its spot: at the fire, in the play ring, or to lounge.</summary>
            bool Resettle(Entity self, ref CreatureState s, ref Random rng) =>
                AtHearth(s.TargetHeight) ? StartHearth(self, ref s, ref rng) :
                s.TargetHeight == CreatureAnimator.PlayRing ? StartPlayRing(self, ref s, ref rng) : StartLounge(self, ref s, ref rng);

            /// <summary>
            /// A place in the children's play ring: on the green nearest its home (the same green for every child of the
            /// town, so they find each other there), to one side of its shade tree, six places round, sitting facing the
            /// middle. A while there, poking at the ground with its stick or tossing its stone into the middle (see the
            /// child's play above). False if there's no green in reach or the ring is full.
            /// </summary>
            bool StartPlayRing(Entity self, ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(8, Allocator.Temp);
                Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), math.max(s.Leash, 30f), AnchorKind.Green, ref near);
                var best = -1;
                for (int i = 0; i < near.Length; i++)
                    if (!set.Anchors[near[i]].InCave && (best < 0 || math.distancesq(set.Anchors[near[i]].Position.xz, s.Home) <
                                                                     math.distancesq(set.Anchors[best].Position.xz, s.Home)))
                        best = near[i];
                near.Dispose();
                if (best < 0)
                    return false;
                var a = set.Anchors[best];
                var centre = a.Position.xz + new float2(-a.Facing.y, a.Facing.x) * math.max(a.Size * 0.5f, 3f);
                const int places = 6;
                var first = (int)(s.Individual % places);
                for (int i = 0; i < places; i++)
                {
                    var k = (first + i) % places;
                    math.sincos(k * 2f * math.PI / places + (a.Owner & 7) * 0.5f, out var sin, out var cos);
                    var spot = centre + new float2(sin, cos) * 0.85f;
                    if (!Walkable(spot, Habitat.Land) || Taken(self, spot, 0.55f))
                        continue;
                    SetMode(ref s, CreatureMode.Sit);
                    s.Target = spot;
                    s.Focus = centre;
                    s.TargetHeight = CreatureAnimator.PlayRing;
                    s.Timer = rng.NextFloat(40f, 100f);
                    return true;
                }
                return false;
            }

            /// <summary>Another on its way to the same spot to sit or lie, that took it first (the lower entity index).</summary>
            bool Contested(Entity self, in CreatureState s)
            {
                var c = Cell(s.Target);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j].Index < self.Index && States[j].Mode == CreatureMode.Sit && math.distance(States[j].Target, s.Target) < 0.9f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Someone else there already, or on their way to sit or lie there (within room of it).</summary>
            bool Taken(Entity self, float2 spot, float room)
            {
                var c = Cell(spot);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self)
                            continue;
                        var other = States[j];
                        if (math.distance(other.Position.xz, spot) < room ||
                            other.Mode == CreatureMode.Sit && math.distance(other.Target, spot) < room)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            // ------------------------------------------------------------------------------- looking

            /// <summary>
            /// Glances: now and then (prey animals more often), not busy fleeing, hunting, asleep or held, it glances at the
            /// most interesting thing near (within 12 m) and in front of it: another creature (more so one that's busy or
            /// moving fast) or the player (more so sprinting or emoting). It follows that with its head for one to four
            /// seconds (a predator watching its prey, longer), turning no further than 70° round and 35° up or down, then
            /// looks away a while. The target is kept in the state (LookAt and the rest) for anything else that wants it.
            /// </summary>
            void Glance(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float3 player, float playerSpeed,
                        IdleAction emote, ref float desiredLook, out float desiredPitch)
            {
                desiredPitch = 0f;
                var prey = !b.Upright && b.FleeDistance > 0f;
                s.LookCool -= dt;
                if (s.LookFor > 0f)
                {
                    s.LookFor -= dt;
                    if (s.LookKind == 1 && Lookup.TryGetValue(s.LookEntity, out var j))
                        s.LookAt = States[j].Position + new float3(0f, HeadHeight(States[j], ref Bodies[j].Blob.Value), 0f);
                    else if (s.LookKind == 1)
                        s.LookFor = 0f;
                    else if (s.LookKind == 2)
                        s.LookAt = player + new float3(0f, 1.6f, 0f);
                    var to = s.LookAt - (s.Position + new float3(0f, HeadHeight(s, ref b), 0f));
                    var yaw = WrapAngle(math.atan2(to.x, to.z) - s.Yaw);
                    // (Gone round behind it: let it go.)
                    if (math.abs(yaw) > 1.9f)
                        s.LookFor = 0f;
                    else
                    {
                        desiredLook = math.clamp(yaw, -1.22f, 1.22f);
                        desiredPitch = math.clamp(math.atan2(to.y, math.max(math.length(to.xz), 0.1f)), -0.6f, 0.6f);
                    }
                    if (s.LookFor <= 0f)
                        s.LookCool = prey ? rng.NextFloat(1f, 3f) : rng.NextFloat(3f, 8f);
                    return;
                }
                var calm = s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander && !Flying(s) || s.Mode == CreatureMode.Graze ||
                           s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Sit || s.Mode == CreatureMode.Work || s.Mode == CreatureMode.Greet ||
                           // (A guard on its round looks about too: most of all at someone sprinting or emoting near it.)
                           s.Mode == CreatureMode.Patrol && s.Stage != 2;
                if (s.LookCool > 0f || !calm || rng.NextFloat() >= dt * (prey ? 0.5f : 0.2f))
                    return;
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                var best = 0f;
                var target = -1;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self)
                            continue;
                        var o = States[j];
                        var away = o.Position.xz - s.Position.xz;
                        var d = math.length(away);
                        if (d > 12f || d < 0.3f || math.dot(away / d, forward) < 0.1f)
                            continue;
                        var score = (o.Action != IdleAction.None || o.Speed > 2f ? 2f : 1f) / d;
                        if (score > best)
                        {
                            best = score;
                            target = j;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                var toPlayer = player.xz - s.Position.xz;
                var dp = math.length(toPlayer);
                var playerScore = dp < 12f && dp > 0.3f && math.dot(toPlayer / dp, forward) > 0.1f
                    ? (playerSpeed > 4f || emote != IdleAction.None ? 3f : 1.2f) / dp : 0f;
                if (playerScore > best)
                {
                    s.LookKind = 2;
                    s.LookAt = player + new float3(0f, 1.6f, 0f);
                    s.LookFor = rng.NextFloat(1f, 4f);
                }
                else if (target >= 0)
                {
                    s.LookKind = 1;
                    s.LookEntity = Entities[target];
                    s.LookAt = States[target].Position + new float3(0f, HeadHeight(States[target], ref Bodies[target].Blob.Value), 0f);
                    // (A predator keeps its eyes on prey longer.)
                    var watching = false;
                    for (int k = 0; k < b.Prey.Length && !watching; k++)
                        watching = b.Prey[k] == Bodies[target].Species;
                    s.LookFor = watching ? rng.NextFloat(3f, 6f) : rng.NextFloat(1f, 4f);
                }
            }

            /// <summary>
            /// Whether the head is busy with something that moves it itself, so a glance would fight it: preening,
            /// sniffing, grooming, scratching, howling, a head-bob, a petting or carrying pose, grazing or drinking, a
            /// head-down posture, or taking or reading a message. (A child's poke or throw sets its own look.)
            /// </summary>
            static bool HeadBusy(in CreatureState s) =>
                s.Action == IdleAction.Preen || s.Action == IdleAction.Sniff || s.Action == IdleAction.Groom || s.Action == IdleAction.Scratch ||
                s.Action == IdleAction.Howl || s.Action == IdleAction.HeadBob || s.Action == IdleAction.Pet || s.Action == IdleAction.Carry ||
                s.Mode == CreatureMode.Graze || s.Mode == CreatureMode.Drink || s.HeadDown > 0.3f ||
                s.Mode == CreatureMode.Idle && (s.Stage == MessageTake || s.Stage == MessageRead);

            /// <summary>About where a creature's eyes are, above its feet.</summary>
            static float HeadHeight(in CreatureState s, ref CreatureBlob b) => b.StandHeight * math.max(s.Scale, 0.1f) * (b.Upright ? 1.8f : 1.2f);

            // ------------------------------------------------------------------------------- hermits and campers

            /// <summary>The nearest anchor of a kind within reach of a point (its index), or -1.</summary>
            int NearestAnchor(AnchorKind kind, float2 around, float within)
            {
                if (!Terrain.Anchors.IsCreated)
                    return -1;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(4, Allocator.Temp);
                Anchors.Near(ref set, new float3(around.x, Terrain.SampleHeight(around), around.y), within, kind, ref near);
                var best = -1;
                var d = float.MaxValue;
                foreach (var i in near)
                {
                    var dd = math.distancesq(set.Anchors[i].Position.xz, around);
                    if (dd < d)
                    {
                        d = dd;
                        best = i;
                    }
                }
                near.Dispose();
                return best;
            }

            /// <summary>
            /// What a hermit does next, by the time of day, all round its shelter (its Home is the Home anchor): asleep in
            /// its shelter at night (in by its door, or curled up in an open one); by its fire in the evening; by day
            /// chopping at its woodpile, tending its garden, fetching water, or sitting by its door. False to let it wander.
            /// </summary>
            bool HermitDay(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                // (By the hour, not by sleepiness: a hermit sits up by its fire after dark, then turns in.)
                if (Hour >= 21.5f || Hour < 6f)
                {
                    var door = NearestAnchor(AnchorKind.Door, s.Home, 8f);
                    if (door >= 0)
                    {
                        SetMode(ref s, CreatureMode.Home);
                        s.Anchor = set.Anchors[door].Position;
                        s.AnchorYaw = math.atan2(set.Anchors[door].Facing.x, set.Anchors[door].Facing.y);
                        s.Timer = rng.NextFloat(300f, 600f);
                    }
                    else
                    {
                        // (An open shelter: lying down on its floor.)
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = s.Home;
                        s.Focus = s.Home + new float2(1f, 0f);
                        s.TargetHeight = 3f;
                        s.Timer = rng.NextFloat(300f, 600f);
                    }
                    return true;
                }
                if (Hour >= 17.8f && Hour < 21.5f)
                {
                    var hearth = NearestAnchor(AnchorKind.Hearth, s.Home, 15f);
                    if (hearth >= 0)
                    {
                        var h = set.Anchors[hearth];
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = h.Position.xz + rng.NextFloat2Direction() * (h.Size + 0.5f);
                        s.Focus = h.Position.xz;
                        s.TargetHeight = 1f;
                        s.Timer = rng.NextFloat(60f, 150f);
                        return true;
                    }
                }
                var roll = rng.NextFloat();
                int a;
                if (roll < 0.35f && (a = NearestAnchor(AnchorKind.Work, s.Home, 15f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.WorkSpot = set.Anchors[a].Position.xz;
                    s.WorkFace = s.WorkSpot + set.Anchors[a].Facing * 2f;
                    s.AnchorSide = 1;
                    s.Timer = rng.NextFloat(40f, 90f);
                    return true;
                }
                if (roll < 0.62f && (a = NearestAnchor(AnchorKind.Garden, s.Home, 15f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    var g = set.Anchors[a];
                    s.WorkSpot = g.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(0f, g.Size * 0.8f);
                    s.WorkFace = s.WorkSpot + g.Facing * 2f;
                    s.AnchorSide = 2;
                    s.Timer = rng.NextFloat(20f, 45f);
                    return true;
                }
                if (roll < 0.72f && (a = NearestAnchor(AnchorKind.Pool, s.Home, 30f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Work);
                    s.WorkSpot = set.Anchors[a].Position.xz;
                    s.WorkFace = s.WorkSpot + set.Anchors[a].Facing * 2f;
                    s.AnchorSide = 2;
                    s.Timer = rng.NextFloat(10f, 20f);
                    return true;
                }
                if (roll < 0.9f && (a = NearestAnchor(AnchorKind.Sit, s.Home, 10f)) >= 0)
                {
                    SetMode(ref s, CreatureMode.Sit);
                    var seat = set.Anchors[a];
                    s.Target = seat.Position.xz;
                    s.Focus = seat.Position.xz + seat.Facing * 3f;
                    s.TargetHeight = 4f;
                    s.Anchor = seat.Position;
                    s.Timer = rng.NextFloat(40f, 100f);
                    return true;
                }
                // Otherwise a potter round its place.
                SetMode(ref s, CreatureMode.Wander);
                s.Target = s.Home + rng.NextFloat2Direction() * rng.NextFloat(3f, 10f);
                s.Timer = rng.NextFloat(10f, 25f);
                return true;
            }

            /// <summary>
            /// A hermit doesn't care for company: someone coming within 4.5 m, it backs off a way, waving them off; nearer
            /// than that it watches them (its glances do that).
            /// </summary>
            void Wary(ref CreatureState s, ref Random rng, float3 player)
            {
                if (s.Mode == CreatureMode.Home || s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Held || s.Mode == CreatureMode.Wander ||
                    s.Mode == CreatureMode.Sit && s.Stage == 3)
                    return;
                if (math.distance(s.Position.xz, player.xz) > 4.5f)
                    return;
                var away = math.normalizesafe(s.Position.xz - player.xz, rng.NextFloat2Direction());
                SetMode(ref s, CreatureMode.Wander);
                s.Target = s.Position.xz + away * 8f;
                s.Timer = 10f;
                if (s.Action == IdleAction.None)
                {
                    s.Action = IdleAction.Wave;
                    s.ActionTime = 0f;
                    s.ActionLength = 1.4f;
                }
            }

            /// <summary>
            /// A traveller's night at a roadside camp (its Home is the Camp anchor): round the fire through the evening,
            /// on a bedroll (a Bed anchor, lying with its feet to the fire) from late on till morning, then on its way
            /// along the road, done with the camp.
            /// </summary>
            bool CamperNight(ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var camp = NearestAnchor(AnchorKind.Camp, s.Home, 3f);
                if (camp < 0)
                    return false;
                var c = set.Anchors[camp];
                if (Hour >= 6.5f && Hour < 16f)
                {
                    // Morning: off along the road, no longer anyone's camper.
                    s.Task = CreatureTask.None;
                    s.Leash = 0f;
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = c.Position.xz + c.Facing * 120f + new float2(-c.Facing.y, c.Facing.x) * rng.NextFloat(-40f, 40f);
                    s.Timer = 120f;
                    return true;
                }
                if (Hour >= 21.5f || Hour < 6.5f)
                {
                    var beds = new NativeList<int>(4, Allocator.Temp);
                    Anchors.Near(ref set, c.Position, c.Size + 3f, AnchorKind.Bed, ref beds);
                    if (beds.Length > 0)
                    {
                        var bed = set.Anchors[beds[(int)(s.Individual % (uint)beds.Length)]];
                        SetMode(ref s, CreatureMode.Sit);
                        s.Target = bed.Position.xz;
                        s.Focus = bed.Position.xz + bed.Facing * 3f;
                        s.TargetHeight = 3f;
                        s.Timer = rng.NextFloat(400f, 800f);
                        beds.Dispose();
                        return true;
                    }
                    beds.Dispose();
                }
                // The evening round the fire.
                var hearth = NearestAnchor(AnchorKind.Hearth, c.Position.xz, c.Size + 2f);
                var middle = hearth >= 0 ? set.Anchors[hearth] : c;
                SetMode(ref s, CreatureMode.Sit);
                math.sincos((s.Individual % 13u) / 13f * 2f * math.PI, out var sn, out var cs);
                s.Target = middle.Position.xz + new float2(sn, cs) * (math.max(middle.Size, 1f) + 0.5f);
                s.Focus = middle.Position.xz;
                s.TargetHeight = 1f;
                s.Timer = rng.NextFloat(60f, 120f);
                return true;
            }

            // ------------------------------------------------------------------------------- hive raids

            const float RaidCooldown = 900f;

            /// <summary>Off to the nearest wild hive within 60 m (to the foot of the trunk under its comb), if there is one.</summary>
            bool StartRaid(ref CreatureState s, ref Random rng)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var near = new NativeList<int>(2, Allocator.Temp);
                Anchors.Near(ref set, s.Position, 60f, AnchorKind.Hive, ref near);
                var best = -1;
                for (int i = 0; i < near.Length; i++)
                    if (!set.Anchors[near[i]].InCave && (best < 0 || math.distancesq(set.Anchors[near[i]].Position, s.Position) <
                                                                     math.distancesq(set.Anchors[best].Position, s.Position)))
                        best = near[i];
                near.Dispose();
                if (best < 0)
                    return false;
                var hive = set.Anchors[best];
                var trunk = math.max(hive.Size - 1f, 0.1f);
                SetMode(ref s, CreatureMode.Work);
                s.Stage = CreatureAnimator.RaidWalk;
                s.Focus = hive.Position.xz + hive.Facing * trunk;
                s.Target = hive.Position.xz + hive.Facing * (trunk + 0.05f);
                s.Timer = 120f;
                // (Even if it never gets there: no other raid for a long while.)
                s.ChaseCooldown = RaidCooldown;
                return true;
            }

            /// <summary>
            /// A bear after a hive's honey: walks to the foot of the trunk under the comb (RaidWalk); rears up at it, pawing
            /// and swaying, while the bees mob it (RaidAtComb); then makes off, running at first, to somewhere 70 to 100 m
            /// from the hive (RaidLeave), and takes up its day there.
            /// </summary>
            void Raiding(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                         ref float targetSpeed, ref bool moving, ref float sit, ref float vertical, ref float desiredLook, ref float headDown, ref float bow)
            {
                s.Timer -= dt;
                if (s.Stage == CreatureAnimator.RaidWalk)
                {
                    targetSpeed = walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Target) < 0.4f)
                    {
                        NextStage(ref s);
                        s.Timer = rng.NextFloat(6f, 10f);
                    }
                    else if (s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
                else if (s.Stage == CreatureAnimator.RaidAtComb && b.Upright)
                {
                    // One of the folk after the honey: stood at the trunk poking up at the comb with a stick (the
                    // animator's pose) while the bees boil out, till a piece comes down; then off with it at a run.
                    s.Speed = 0f;
                    var to = s.Focus - s.Position.xz;
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    desiredLook = 0.15f * math.sin(s.StageTime * 3.1f);
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.Honey;
                        var away = math.normalizesafe(s.Position.xz - s.Focus, rng.NextFloat2Direction());
                        var spot = s.Position.xz + away * rng.NextFloat(80f, 110f);
                        for (int tries = 0; tries < 4 && !Walkable(spot, Habitat.Land); tries++)
                            spot = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(80f, 110f);
                        s.Stage = CreatureAnimator.RaidLeave;
                        s.StageTime = 0f;
                        s.Target = spot;
                        s.Timer = 120f;
                    }
                }
                else if (s.Stage == CreatureAnimator.RaidAtComb)
                {
                    s.Speed = 0f;
                    // (Shuffling the last of the way in, up against the bark.)
                    var short_ = s.Target - s.Position.xz;
                    s.Position.xz += math.normalizesafe(short_) * math.min(math.length(short_), 0.5f * dt);
                    var to = s.Focus - s.Position.xz;
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    // Up on its hind legs against the trunk, leaning in, front paws braced up it and scraping at it,
                    // head tipped up to the comb.
                    sit = 0.6f;
                    vertical = 0.78f + 0.06f * math.sin(s.StageTime * 5f);
                    desiredLook = 0.25f * math.sin(s.StageTime * 2.3f);
                    // Down comes a chunk of comb: down on all fours to lick at it.
                    if (s.Timer <= 0f)
                    {
                        s.Stage = CreatureAnimator.RaidLick;
                        s.StageTime = 0f;
                        s.Held = HeldItem.Honey;
                        s.Timer = rng.NextFloat(4f, 6f);
                    }
                }
                else if (s.Stage == CreatureAnimator.RaidLick)
                {
                    // Licking at it, nose down to it (the front end bowed: a bear has no neck to speak of), till the bees
                    // drive it off.
                    s.Speed = 0f;
                    headDown = 1f;
                    bow = 0.8f + 0.2f * math.sin(s.StageTime * 7f);
                    desiredLook = 0.2f * math.sin(s.StageTime * 6f);
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.None;
                        // Away from the hive, a little to one side or the other.
                        var away = math.normalizesafe(s.Position.xz - s.Focus, rng.NextFloat2Direction());
                        var turn = rng.NextFloat(-0.6f, 0.6f);
                        math.sincos(turn, out var sn, out var cs);
                        away = new float2(away.x * cs - away.y * sn, away.x * sn + away.y * cs);
                        var spot = s.Position.xz + away * rng.NextFloat(70f, 100f);
                        for (int tries = 0; tries < 4 && !Walkable(spot, Habitat.Land); tries++)
                            spot = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(70f, 100f);
                        s.Stage = CreatureAnimator.RaidLeave;
                        s.StageTime = 0f;
                        s.Target = spot;
                        s.Timer = 120f;
                    }
                }
                else
                {
                    // (Running from the bees at first; one of the folk runs further, flapping at them.)
                    targetSpeed = s.StageTime < (b.Upright ? 12f : 6f) ? run : walk;
                    moving = true;
                    if (math.distance(s.Position.xz, s.Target) < 2f || s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- messenger birds

            /// <summary>Idle-mode stages of one of the townsfolk a messenger bird has come to: waiting for it, taking the
            /// message, reading it.</summary>
            const byte MessageWait = CreatureAnimator.MessageWait, MessageTake = CreatureAnimator.MessageTake, MessageRead = CreatureAnimator.MessageRead;

            /// <summary>Where a messenger bird perches on the forearm one of the townsfolk holds up for it (see the
            /// animator's MessageWait pose): out in front of them at chest height, a little to their left.</summary>
            float3 Perch(in CreatureState them, ref CreatureBlob tb)
            {
                var f = new float2(math.sin(them.Yaw), math.cos(them.Yaw));
                var left = new float2(-f.y, f.x);
                var scale = math.max(them.Scale, 0.3f);
                var xz = them.Position.xz + f * 0.42f * scale + left * 0.12f * scale;
                return new float3(xz.x, them.Position.y + tb.StandHeight * scale * PerchHeight, xz.y);
            }

            const float PerchHeight = 1.3f;

            /// <summary>
            /// A messenger bird with a message: in over the town to one of the townsfolk about the place (the nearest to
            /// it, re-chosen if that one goes in), down to land on the forearm they hold up for it (0); there, waiting while they
            /// take it (1); then up and away towards the town it's making for next (2), where it's an ordinary bird again.
            /// </summary>
            void Messenger(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                var hasThem = Lookup.TryGetValue(s.Partner, out var r);
                if (s.Stage == 0)
                {
                    if (!hasThem || !Receiver(States[r], ref Bodies[r].Blob.Value, s.Home) && !(States[r].Mode == CreatureMode.Idle && States[r].Stage == MessageWait))
                    {
                        // (Nobody free just now: round over the town, looking again now and then, for two minutes.)
                        s.Partner = rng.NextFloat() < dt * 2f ? FindReceiver(s.Position.xz, s.Home) : Entity.Null;
                        if (s.StageTime > 120f)
                        {
                            NextStage(ref s);
                            s.Stage = 2;
                        }
                        var angle = s.StageTime * 0.25f;
                        var round = s.Home + new float2(math.cos(angle), math.sin(angle)) * 15f;
                        FlyTo(ref s, ref b, round, Cruise(s, ref b, round) + 8f, b.FlySpeed, dt, ref vertical);
                        return;
                    }
                    var perch = Perch(States[r], ref Bodies[r].Blob.Value);
                    if (FlyTo(ref s, ref b, perch.xz, perch.y, b.FlySpeed, dt, ref vertical))
                        NextStage(ref s);
                    else if (s.StageTime > 120f)
                    {
                        NextStage(ref s);
                        s.Stage = 2;
                    }
                }
                else if (s.Stage == 1)
                {
                    // On the forearm (kept there as it moves), facing them.
                    s.Speed = 0f;
                    if (hasThem)
                    {
                        s.Position = Perch(States[r], ref Bodies[r].Blob.Value);
                        var to = States[r].Position.xz - s.Position.xz;
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                    }
                    // Off once the message is taken (or nobody comes for it).
                    var taken = hasThem && States[r].Mode == CreatureMode.Idle && States[r].Stage == MessageRead;
                    if (taken)
                        s.Held = HeldItem.None;
                    if (taken || s.StageTime > 10f)
                        NextStage(ref s);
                }
                else
                {
                    if (FlyTo(ref s, ref b, s.WorkSpot, Cruise(s, ref b, s.WorkSpot) + 10f, b.FlySpeed, dt, ref vertical))
                    {
                        s.Task = CreatureTask.None;
                        s.Held = HeldItem.None;
                        Pause(ref s, ref b, ref rng);
                    }
                }
            }

            /// <summary>One of the townsfolk who could take a message: grown, about the town (not indoors, asleep, at a post
            /// or on guard), near its middle.</summary>
            static bool Receiver(in CreatureState o, ref CreatureBlob ob, float2 town) =>
                ob.Upright && ob.Hands && !ob.Child && o.Leash > 0f && (Free(o.Mode) || o.Mode == CreatureMode.Greet) &&
                !(o.Mode == CreatureMode.Idle && o.Stage >= MessageWait) &&
                o.Task != CreatureTask.Patrol && o.Task != CreatureTask.Watch && math.distance(o.Position.xz, town) < 60f;

            /// <summary>The one of the townsfolk nearest the bird who could take its message, or none.</summary>
            Entity FindReceiver(float2 from, float2 town)
            {
                var best = Entity.Null;
                var d = float.MaxValue;
                for (int j = 0; j < States.Length; j++)
                {
                    if (!Receiver(States[j], ref Bodies[j].Blob.Value, town))
                        continue;
                    var dj = math.distancesq(States[j].Position.xz, from);
                    if (dj < d)
                    {
                        d = dj;
                        best = Entities[j];
                    }
                }
                return best;
            }

            /// <summary>A messenger bird coming down to this one (within a cell or so).</summary>
            bool FindMessenger(Entity self, in CreatureState s, out Entity bird)
            {
                bird = Entity.Null;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if (o.Task == CreatureTask.Messenger && o.Mode == CreatureMode.Work && o.Stage <= 1 && o.Partner == self &&
                            math.distance(o.Position.xz, s.Position.xz) < 25f)
                        {
                            bird = Entities[j];
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>
            /// One of the townsfolk a messenger bird has come to: stands and turns to it as it comes down, holding up a
            /// forearm for it to land on (60); once it's on it, takes the message from its leg with the other hand (61);
            /// then reads it a while (62), and goes about its day.
            /// </summary>
            void Receiving(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                var hasBird = Lookup.TryGetValue(s.Partner, out var j);
                var bird = hasBird ? States[j] : default;
                // (Eyes on the bird, as it comes in and while it's there.)
                if (hasBird && s.Stage < MessageRead)
                {
                    s.LookKind = 1;
                    s.LookEntity = s.Partner;
                    s.LookFor = math.max(s.LookFor, 0.3f);
                }
                // (Turned to the bird as it comes in; holding still for the last few metres, so it has a steady arm to land on.)
                if (hasBird)
                {
                    var to = bird.Position.xz - s.Position.xz;
                    if (s.Stage == MessageWait && math.lengthsq(to) > 9f)
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -b.TurnRate * dt, b.TurnRate * dt));
                }
                if (s.Stage == MessageWait)
                {
                    s.Timer -= dt;
                    if (!hasBird || bird.Task != CreatureTask.Messenger || bird.Stage == 2 || s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                    else if (bird.Stage == 1 && math.distance(bird.Position.xz, s.Position.xz) < 1.5f)
                        NextStage(ref s);
                }
                else if (s.Stage == MessageTake)
                {
                    if (s.StageTime > 1.4f)
                    {
                        NextStage(ref s);
                        s.Timer = rng.NextFloat(8f, 14f);
                    }
                }
                else
                {
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                        Pause(ref s, ref b, ref rng);
                }
            }

            // ------------------------------------------------------------------------------- travelling

            /// <summary>
            /// A traveller on the road: along it stretch by stretch (its stretch in TargetHeight, which way in
            /// AnchorSide: 1 towards each stretch's B), at a brisk walk; at a fork, on along whichever stretch carries on
            /// from where it stands (any but the way it came). Where the road ends (in the town it was making for) it has
            /// arrived: it stays a while there as a visitor, the town's streets its home.
            /// </summary>
            void Travelling(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk)
            {
                if (!Terrain.Settlements.IsCreated)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                ref var set = ref Terrain.Settlements.Value;
                var k = (int)s.TargetHeight;
                if (k < 0 || k >= set.Streets.Length)
                {
                    Arrived(ref s, ref b, ref rng);
                    return;
                }
                var onward = s.AnchorSide == 1;
                var goal = onward ? set.Streets[k].B : set.Streets[k].A;
                if (WalkOn(ref s, ref b, goal, walk * 1.15f, dt) > 0.8f)
                    return;
                // The next stretch: the neighbour in the list, if it carries on from here, else any road stretch that does.
                var next = -1;
                var nextOnward = onward;
                var n = onward ? k + 1 : k - 1;
                if (n >= 0 && n < set.Streets.Length && set.Streets[n].Road != 0 &&
                    math.distancesq((onward ? set.Streets[n].A : set.Streets[n].B).xz, goal.xz) < 2f)
                    next = n;
                for (int i = 0; i < set.Streets.Length && next < 0; i++)
                {
                    ref var st = ref set.Streets[i];
                    if (i == k || st.Road == 0)
                        continue;
                    if (math.distancesq(st.A.xz, goal.xz) < 2f)
                    {
                        next = i;
                        nextOnward = true;
                    }
                    else if (math.distancesq(st.B.xz, goal.xz) < 2f)
                    {
                        next = i;
                        nextOnward = false;
                    }
                }
                if (next < 0)
                {
                    Arrived(ref s, ref b, ref rng);
                    return;
                }
                s.TargetHeight = next;
                s.AnchorSide = (byte)(nextOnward ? 1 : 0);
            }

            /// <summary>A traveller at its road's end: a visitor in the town now, about its streets for a while.</summary>
            void Arrived(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                s.Task = CreatureTask.None;
                s.Home = s.Position.xz;
                s.Leash = 30f;
                s.Held = HeldItem.None;
                Pause(ref s, ref b, ref rng);
            }

            // ------------------------------------------------------------------------------- mining

            /// <summary>
            /// A miner's day: from its camp to the mouth of the nearest mine (stage 1), in along the passage node by node
            /// (10 + node), to the ore face (100) and there swinging its pick a while (101: MinerAtFace); then a sack of
            /// ore out again node by node (110 + node), up out of the mouth and back to the camp (200), where it sets the
            /// sack down (201) and goes back for more. At dusk it makes its way out, without a load, and home. Its mine
            /// and its face (its own, chosen by its individual number, the same each day) are its LairCave and
            /// LairNode, the face's spot its Anchor. It walks itself, on the floor under it: the cave's in the mine,
            /// the ground outside.
            /// </summary>
            void Mining(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk)
            {
                if (!Terrain.Caves.IsCreated)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage == 0)
                {
                    if (!FindOreFace(ref s))
                    {
                        Pause(ref s, ref b, ref rng);
                        return;
                    }
                    s.Stage = 1;
                    s.StageTime = 0f;
                    s.Held = HeldItem.Pickaxe;
                }
                ref var set = ref Terrain.Caves.Value;
                ref var cave = ref set.Caves[s.LairCave];
                var dusk = Night > 0.45f;
                var pace = walk * (s.Held == HeldItem.OreSack ? 0.8f : 1f);
                // (At dusk, on the way in or at the face: turn about and make for the way out, as it is.)
                if (dusk && (s.Stage >= 10 && s.Stage <= 101))
                {
                    var at = s.Stage == 100 || s.Stage == MinerAtFace ? s.LairNode : s.Stage - 10;
                    s.Stage = (byte)(110 + math.clamp(at, 0, 88));
                    s.StageTime = 0f;
                }
                if (s.Stage == 1)
                {
                    if (WalkOn(ref s, ref b, cave.Mouth, pace, dt) < 1f)
                        Next(ref s, 11);
                }
                else if (s.Stage >= 10 && s.Stage < 100)
                {
                    var node = s.Stage - 10;
                    if (WalkOn(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, pace, dt) < 1.2f)
                        Next(ref s, node >= s.LairNode ? (byte)100 : (byte)(s.Stage + 1));
                }
                else if (s.Stage == 100)
                {
                    if (WalkOn(ref s, ref b, s.Anchor, pace, dt) < 0.4f)
                    {
                        Next(ref s, MinerAtFace);
                        s.Timer = rng.NextFloat(40f, 90f);
                    }
                }
                else if (s.Stage == MinerAtFace)
                {
                    // At the face, swinging at it (the swing is the Mine task's pose), till the sack's full.
                    s.Speed = 0f;
                    var wanted = WrapAngle(s.AnchorYaw - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.OreSack;
                        Next(ref s, (byte)(110 + math.clamp(s.LairNode, 0, 88)));
                    }
                }
                else if (s.Stage >= 110 && s.Stage < 199)
                {
                    var node = s.Stage - 110;
                    if (WalkOn(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, pace, dt) < 1.2f)
                        Next(ref s, node <= 1 ? (byte)199 : (byte)(s.Stage - 1));
                }
                else if (s.Stage == 199)
                {
                    if (WalkOn(ref s, ref b, cave.Mouth, pace, dt) < 1f)
                        Next(ref s, 200);
                }
                else if (s.Stage == 200)
                {
                    var home = new float3(s.Home.x, Terrain.SampleHeight(s.Home), s.Home.y);
                    if (WalkOn(ref s, ref b, home, pace, dt) < 1.5f)
                    {
                        Next(ref s, 201);
                        s.Timer = rng.NextFloat(3f, 6f);
                    }
                }
                else
                {
                    // Setting the sack down at the camp; back for more while it's day, else done for the day.
                    s.Speed = 0f;
                    s.Timer -= dt;
                    if (s.Timer <= 0f)
                    {
                        s.Held = HeldItem.None;
                        if (dusk)
                            Pause(ref s, ref b, ref rng);
                        else
                        {
                            s.Held = HeldItem.Pickaxe;
                            Next(ref s, 1);
                        }
                    }
                }
                // (A stage that can't be got through in two minutes, four for the walks between the camp and the mine,
                // it gives up on: home, and try again later.)
                var limit = s.Stage == 1 || s.Stage == 200 ? 240f : 120f;
                if (s.Mode == CreatureMode.Work && s.Stage != MinerAtFace && s.Stage != 201 && s.StageTime > limit)
                {
                    s.Held = HeldItem.None;
                    Pause(ref s, ref b, ref rng);
                }

                static void Next(ref CreatureState s, byte stage)
                {
                    s.Stage = stage;
                    s.StageTime = 0f;
                }
            }

            const byte MinerAtFace = CreatureAnimator.MinerAtFace;

            /// <summary>
            /// The ore face a miner works (one of the mines' Work anchors in a chamber, not the one by the mouth, within
            /// 400 m of its camp; which one by its individual number), and its mine and the chamber's node; false if
            /// there's none.
            /// </summary>
            bool FindOreFace(ref CreatureState s)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var anchors = ref Terrain.Anchors.Value;
                ref var caves = ref Terrain.Caves.Value;
                var found = new NativeList<int>(16, Allocator.Temp);
                Anchors.Near(ref anchors, new float3(s.Home.x, s.Position.y, s.Home.y), 400f, AnchorKind.Work, ref found);
                var faces = new FixedList128Bytes<int>();
                for (int i = 0; i < found.Length && faces.Length < 30; i++)
                {
                    var a = anchors.Anchors[found[i]];
                    if (a.InCave && a.Size < 1.2f && a.OwnerIndex < caves.Caves.Length)
                        faces.Add(found[i]);
                }
                found.Dispose();
                if (faces.Length == 0)
                    return false;
                var face = anchors.Anchors[faces[(int)(s.Individual % (uint)faces.Length)]];
                ref var cave = ref caves.Caves[face.OwnerIndex];
                var best = float.MaxValue;
                var node = 1;
                for (int n = 1; n < cave.NodeCount; n++)
                {
                    var d = math.distance(caves.Nodes[cave.FirstNode + n].Position, face.Position);
                    if (d < best)
                    {
                        best = d;
                        node = n;
                    }
                }
                s.LairCave = (short)face.OwnerIndex;
                s.LairNode = (short)math.min(node, 88);
                s.Anchor = face.Position;
                s.AnchorYaw = math.atan2(face.Facing.x, face.Facing.y);
                return true;
            }

            /// <summary>
            /// A step towards a point at a walking pace, turning to face the way, on the floor under it (the cave's
            /// under rock, the ground's outside); how far it still is, across.
            /// </summary>
            float WalkOn(ref CreatureState s, ref CreatureBlob b, float3 target, float speed, float dt)
            {
                var to = target.xz - s.Position.xz;
                var d = math.length(to);
                s.Speed = speed;
                CreatureMotion.Accelerate(ref s, ref b, dt);
                speed = s.Speed;
                if (d > 1e-3f)
                {
                    s.Position.xz += to / d * math.min(d, speed * dt);
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                }
                s.Speed = d > 0.05f ? speed : 0f;
                // (From a little above where it is, or where it's going if that's higher: up a slope, out of a pit.)
                var from = new float3(s.Position.x, math.max(s.Position.y, target.y) + 0.8f, s.Position.z);
                s.Position.y = Terrain.FloorUnder(from, out _);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
                return d;
            }

            // ------------------------------------------------------------------------------- swimming

            /// <summary>
            /// Water good for a swim near where it is: calm (no rapids), deep enough to swim (1.6 m), not lava or a
            /// cave's hole, and warm (no snow on the bank). Sets off to the bank there (WorkSpot) with the middle of the
            /// swim at Focus. False if there's none within 60 m.
            /// </summary>
            bool StartSwim(ref CreatureState s, ref Random rng)
            {
                var start = rng.NextFloat(math.PI * 2f);
                for (var d = 6f; d <= 60f; d += 6f)
                    for (int k = 0; k < 12; k++)
                    {
                        var a = start + k * math.PI / 6f;
                        var dir = new float2(math.sin(a), math.cos(a));
                        var p = s.Position.xz + dir * d;
                        var g = Terrain.SampleGround(p);
                        if (!g.HasWater || g.WaterSurface - g.Height < 1.6f || g.Rapids > 0.15f || !Swimmable(p))
                            continue;
                        var q = p;
                        for (int step = 0; step < 40 && !Walkable(q, Habitat.Land); step++)
                            q -= dir * 0.5f;
                        if (!Walkable(q, Habitat.Land) || Terrain.SampleSurface(q, Terrain.SampleGround(q), Terrain.SampleNormal(q, 1f)).Snow > 0.2f)
                            continue;
                        SetMode(ref s, CreatureMode.Swim);
                        s.WorkSpot = q;
                        s.Focus = p;
                        s.Target = q;
                        s.Timer = rng.NextFloat(60f, 150f);
                        return true;
                    }
                return false;
            }

            /// <summary>
            /// Out for a swim: to the bank (0), in and swimming about from point to point in the deep water round Focus
            /// at a gentle breaststroke (1), back to the bank when the time's up or the day draws in (2), and standing
            /// on the bank a while drying off, looking about (3).
            /// </summary>
            void Swimming(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float idleLook,
                          ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                switch (s.Stage)
                {
                    case 0:
                        s.Target = s.WorkSpot;
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, s.WorkSpot) < 0.8f)
                        {
                            NextStage(ref s);
                            s.Target = s.Focus;
                        }
                        break;
                    case 1:
                        s.Timer -= dt;
                        targetSpeed = 0.9f * s.Scale;
                        moving = true;
                        if (math.distance(s.Position.xz, s.Target) < 1f)
                        {
                            // Somewhere else in the deep water round the middle.
                            for (int k = 0; k < 6; k++)
                            {
                                var p = s.Focus + rng.NextFloat2Direction() * rng.NextFloat(2f, 10f);
                                if (WaterDepth(p) > 1.3f && Swimmable(p))
                                {
                                    s.Target = p;
                                    break;
                                }
                            }
                        }
                        if (s.Timer <= 0f || Night > 0.4f)
                        {
                            NextStage(ref s);
                            s.Target = s.WorkSpot;
                        }
                        break;
                    case 2:
                        s.Target = s.WorkSpot;
                        targetSpeed = 0.9f * s.Scale;
                        moving = true;
                        if (math.distance(s.Position.xz, s.WorkSpot) < 0.8f)
                        {
                            NextStage(ref s);
                            s.Timer = rng.NextFloat(15f, 35f);
                        }
                        break;
                    default:
                        s.Timer -= dt;
                        desiredLook = idleLook;
                        if (s.Timer <= 0f)
                            Pause(ref s, ref b, ref rng);
                        break;
                }
            }

            // ------------------------------------------------------------------------------- fishers

            /// <summary>
            /// A fisher's spot: dry shore near its home (within its range) looking out over water a metre deep or more,
            /// the same one each time (searched outward from its home, from a bearing of its own): WorkSpot the shore,
            /// WorkFace the water a few metres out where its float goes.
            /// </summary>
            bool FindFishingSpot(ref CreatureState s)
            {
                var range = math.max(s.Leash * 0.9f, 30f);
                var start = (s.Individual % 360u) * math.PI / 180f;
                for (var d = 8f; d <= range; d += 6f)
                    for (int k = 0; k < 16; k++)
                    {
                        var a = start + k * math.PI / 8f;
                        var dir = new float2(math.sin(a), math.cos(a));
                        var p = s.Home + dir * d;
                        if (WaterDepth(p) < 1f)
                            continue;
                        // Back from the water to the bank.
                        var q = p;
                        for (int step = 0; step < 40 && !Walkable(q, Habitat.Land); step++)
                            q -= dir * 0.5f;
                        if (!Walkable(q, Habitat.Land))
                            continue;
                        s.WorkSpot = q - dir * 0.3f;
                        s.WorkFace = q + dir * 4f;
                        return true;
                    }
                return false;
            }

            /// <summary>
            /// A fisher at the water's edge (Work, stage 1 on), facing the water: waiting with its float out (1),
            /// glancing about now and then; now and then casting again (2: ActionTime through the swing); sometimes a
            /// bite and a fish landed, held up on the line a moment before it goes in the basket (3), then a fresh cast.
            /// It packs up at dusk.
            /// </summary>
            void Fishing(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float desiredLook)
            {
                if (Night > 0.5f)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                var toWater = s.WorkFace - s.Position.xz;
                var wanted = WrapAngle(math.atan2(toWater.x, toWater.y) - s.Yaw);
                s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                switch (s.Stage)
                {
                    case 2:
                        s.ActionTime = math.saturate(s.ActionTime + dt / 1.6f);
                        if (s.ActionTime >= 1f)
                        {
                            s.Stage = 1;
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                    case 3:
                        desiredLook = 0f;
                        s.ActionTime = math.saturate(s.ActionTime + dt / 3f);
                        if (s.ActionTime >= 1f)
                        {
                            s.Stage = 2;
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                    default:
                        s.Stage = 1;
                        desiredLook = math.frac(s.ModeTime / 11f) > 0.8f ? math.sin(s.ModeTime) * 0.8f : 0f;
                        if (rng.NextFloat() < dt / 75f)
                        {
                            s.Stage = 3;   // a bite
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        else if (rng.NextFloat() < dt / 25f)
                        {
                            s.Stage = 2;   // reel in and cast again
                            s.StageTime = 0f;
                            s.ActionTime = 0f;
                        }
                        break;
                }
            }

            // ------------------------------------------------------------------------------- guards

            /// <summary>Whether a guard is on its shift: half by day, half by night (by its individual number), with a
            /// little gap at dawn and dusk while the watch changes.</summary>
            static bool NightShift(in CreatureState s) => ((s.Individual >> 3) & 1u) != 0;   // (bit 0 is always set)

            bool OnShift(in CreatureState s) =>
                s.Task == CreatureTask.Patrol && (NightShift(s) ? Night > 0.55f : Night < 0.45f);

            /// <summary>The settlement a resident belongs to (whose middle its home is nearest), or -1.</summary>
            int SettlementOf(float2 home)
            {
                if (!Terrain.Settlements.IsCreated)
                    return -1;
                ref var towns = ref Terrain.Settlements.Value.Settlements;
                var best = -1;
                var bestD = float.MaxValue;
                for (int i = 0; i < towns.Length; i++)
                {
                    var d = math.distance(towns[i].Centre, home);
                    if (d < towns[i].Radius * 1.5f && d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                return best;
            }

            /// <summary>Sets a guard going on its round, from the leg it's in the slice of (false if it has no town).</summary>
            bool StartPatrol(ref CreatureState s)
            {
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return false;
                var legs = RoundLegs(s);
                var off = s.Position.xz - Terrain.Settlements.Value.Settlements[town].Centre;
                var leg = ((int)math.round(math.atan2(off.x, off.y) / (2f * math.PI / legs)) % legs + legs) % legs;
                if (!RoundPoint(town, leg, legs, InnerRound(s), out var point, out var gate))
                    return false;
                SetMode(ref s, CreatureMode.Patrol);
                s.RouteLeg = (byte)leg;
                s.WorkSpot = point;
                s.WorkFace = s.Position.xz;
                s.AnchorSize = gate ? 1f : 0f;
                return true;
            }

            // A guard's round, fixed by its individual number (so the same on every machine): most walk the bounds, a
            // loop just inside the town's edge an eighth of the way round at a time, by way of its gates; some an inner
            // loop round the square, a quarter of the way round at a time; one way round or the other.
            static bool InnerRound(in CreatureState s) => CreatureBlob.Trait(s.Individual, 33) > 0.4f;
            static int RoundWay(in CreatureState s) => CreatureBlob.Trait(s.Individual, 34) > 0f ? 1 : -1;
            static int RoundLegs(in CreatureState s) => InnerRound(s) ? 4 : 8;

            /// <summary>
            /// The point a leg of a round makes for: on the bounds, the town's gate in that leg's slice (seen from the
            /// middle) if it has one there (gate true), else a point just inside the edge, drawn in (or turned aside a
            /// little) until it's clear of the houses and their yards and on dry ground; round the square, a point about
            /// a quarter of the way out, likewise clear, or failing that the square's edge. False if there's nowhere
            /// clear on the bounds.
            /// </summary>
            bool RoundPoint(int settlement, int leg, int legs, bool inner, out float2 point, out bool gate)
            {
                ref var town = ref Terrain.Settlements.Value.Settlements[settlement];
                var slice = 2f * math.PI / legs;
                var angle = leg * slice;
                gate = false;
                point = town.Centre;
                if (!inner && Terrain.Anchors.IsCreated)
                {
                    ref var anchors = ref Terrain.Anchors.Value;
                    var found = new NativeList<int>(8, Allocator.Temp);
                    Anchors.Near(ref anchors, new float3(town.Centre.x, 0f, town.Centre.y), town.Radius * 1.3f, AnchorKind.Gate, ref found);
                    var best = float.MaxValue;
                    for (int i = 0; i < found.Length; i++)
                    {
                        var a = anchors.Anchors[found[i]];
                        var off = a.Position.xz - town.Centre;
                        var turn = math.abs(WrapAngle(math.atan2(off.x, off.y) - angle));
                        if (a.InCave || a.OwnerIndex != settlement || turn > slice * 0.5f || turn >= best)
                            continue;
                        best = turn;
                        point = a.Position.xz;
                        gate = true;
                    }
                    found.Dispose();
                    if (gate)
                        return true;
                }
                // (Straight out from the middle first, then a little to either side within the slice: a spoke of
                // houses can fill the line from the middle out.)
                for (int side = 0; side < 5; side++)
                {
                    var turn = (side == 0 ? 0f : (side % 2 == 1 ? 1f : -1f) * (side < 3 ? 0.2f : 0.4f)) * slice;
                    math.sincos(angle + turn, out var sin, out var cos);
                    var dir = new float2(sin, cos);
                    for (int k = 0; k < 8; k++)
                    {
                        var r = town.Radius * (inner ? 0.22f + 0.04f * k : 0.9f - 0.05f * k);
                        point = town.Centre + dir * r;
                        if (!InYard(settlement, point) && Walkable(point, Habitat.Land))
                            return true;
                    }
                }
                // Round the square, nowhere clear in the slice: the square's own edge there (open ground), so the
                // round is always a whole ring.
                if (inner)
                {
                    math.sincos(angle, out var sin, out var cos);
                    point = town.Centre + new float2(sin, cos) * town.Radius * 0.15f;
                    return true;
                }
                return false;
            }

            /// <summary>Whether a point is on a built plot of a settlement (a house and its yard, a metre round).</summary>
            bool InYard(int settlement, float2 at)
            {
                ref var set = ref Terrain.Settlements.Value;
                ref var town = ref set.Settlements[settlement];
                for (int i = town.FirstPlot; i < town.FirstPlot + town.PlotCount; i++)
                {
                    ref var plot = ref set.Plots[i];
                    if (plot.Building >= 0 && math.all(at > plot.Min - 1f) && math.all(at < plot.Max + 1f))
                        return true;
                }
                return false;
            }

            /// <summary>
            /// A beast to drive off (a wolf, a bear or the like) well inside the guard's town (10 m in from its edge: not
            /// one just roaming by) and within 25 m of it (35 m by torchlight), that fewer than two other guards are
            /// already after: the nearest.
            /// </summary>
            bool FindMenace(Entity self, in CreatureState s, out Entity menace)
            {
                menace = Entity.Null;
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return false;
                ref var t = ref Terrain.Settlements.Value.Settlements[town];
                var best = s.Held == HeldItem.Torch ? 35f : 25f;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (!Bodies[j].Blob.Value.DrivenOff || States[j].Gone)
                            continue;
                        var at = States[j].Position.xz;
                        var d = math.distance(at, s.Position.xz);
                        if (d >= best || math.distance(at, t.Centre) > t.Radius - 10f || Chasers(self, Entities[j], at) >= 2)
                            continue;
                        best = d;
                        menace = Entities[j];
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return menace != Entity.Null;
            }

            /// <summary>How many guards (other than this one) are running at the beast.</summary>
            int Chasers(Entity self, Entity beast, float2 at)
            {
                var count = 0;
                var c = Cell(at);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] != self && States[j].Mode == CreatureMode.Patrol && States[j].Stage == 2 && States[j].Partner == beast)
                            count++;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return count;
            }

            /// <summary>
            /// For a beast in a town: a guard running at it (noticed within 30 m), or a night guard's torch within 12 m;
            /// and where that guard is.
            /// </summary>
            bool FindChaser(Entity self, in CreatureState s, out float2 at)
            {
                using var profiled = ChaserMarker.Auto();
                at = default;
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (other.Mode != CreatureMode.Patrol)
                            continue;
                        var d = math.distance(other.Position.xz, s.Position.xz);
                        if (other.Stage == 2 && other.Partner == self && d < 30f || other.Held == HeldItem.Torch && d < 12f)
                        {
                            at = other.Position.xz;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Another guard pausing within a few metres (for a word), and where it is.</summary>
            bool FindGuard(Entity self, in CreatureState s, out float2 at)
            {
                at = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || States[j].Mode != CreatureMode.Patrol || States[j].Stage != 1)
                            continue;
                        if (math.distance(States[j].Position.xz, s.Position.xz) < 3.5f)
                        {
                            at = States[j].Position.xz;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>
            /// A guard at the point its leg of the round was making for: a while there at a gate, looking out down the
            /// road, or at either of two lookouts (the first leg's point and the one halfway round), looking about; then
            /// on to the next leg's point.
            /// </summary>
            void NextOnRound(ref CreatureState s, ref Random rng)
            {
                var town = SettlementOf(s.Home);
                if (town < 0)
                    return;
                var legs = RoundLegs(s);
                var inner = InnerRound(s);
                var reached = s.RouteLeg % legs;
                var pause = s.AnchorSize > 0f ? rng.NextFloat(8f, 16f) : reached == 0 || reached == legs / 2 ? rng.NextFloat(4f, 8f) : 0f;
                var leg = reached;
                for (int k = 0; k < legs; k++)
                {
                    leg = (leg + legs + RoundWay(s)) % legs;
                    if (RoundPoint(town, leg, legs, inner, out var point, out var gate) && math.distancesq(point, s.WorkSpot) > 1f)
                    {
                        s.RouteLeg = (byte)leg;
                        s.WorkFace = s.WorkSpot;
                        s.WorkSpot = point;
                        s.AnchorSize = gate ? 1f : 0f;
                        break;
                    }
                }
                if (pause > 0f)
                {
                    s.Stage = 1;
                    s.StageTime = 0f;
                    s.Timer = pause;
                }
            }

            /// <summary>
            /// A guard on its round: to the point it's making for (stage 0), at a slow, steady pace;
            /// at a gate or a lookout it stops to look about (stage 1), longer at a gate, looking out down the road;
            /// meeting another guard stopped there, it turns to it for a word. Anyone running or carrying on close by, it
            /// stops to watch. Off shift, it goes on with its own day.
            /// </summary>
            void Patrol(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float3 player,
                        float playerSpeed, IdleAction emote, ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                if (!OnShift(s))
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                // Its weapon: worn in its sheath on its rounds; drawn when there's something to see to (a beast to drive
                // off, someone running or carrying on close by), and put away again once all's been quiet a few
                // seconds. Drawing or sheathing is a half-second reach (Draw, Sheathe: the action time every machine
                // sees), the weapon passing from sheath to hand at the middle of it. (By night a torch fills its hand;
                // an elf's bow stays on its back.)
                if (Night <= 0.5f && b.GuardKit != GuardKit.Elf && b.Hands)
                {
                    var near = player.xz - s.Position.xz;
                    var alert = s.Stage == 2 || math.length(near) < 15f && (playerSpeed > 7f || emote != IdleAction.None);
                    s.WeaponCalm = alert ? 0f : s.WeaponCalm + dt;
                    if (s.Action == IdleAction.None && alert && s.Held == HeldItem.Spear)
                    {
                        s.Held = HeldItem.SpearDrawn;
                        s.Action = IdleAction.Draw;
                        s.ActionTime = 0f;
                        s.ActionLength = 0.5f;
                    }
                    else if (s.Action == IdleAction.None && !alert && s.Held == HeldItem.SpearDrawn && s.WeaponCalm > 4f)
                    {
                        s.Held = HeldItem.Spear;
                        s.Action = IdleAction.Sheathe;
                        s.ActionTime = 0f;
                        s.ActionLength = 0.5f;
                    }
                }
                // A wolf, a bear or the like come into the town: the guard runs at it, spear levelled (stage 2), till it's
                // off out past the town's edge, then back to its round (to the point it was making for). It never goes
                // more than 30 m beyond the edge after one, nor for more than 40 s, and leaves any one beast to a pair;
                // after one, a minute and a half before it goes after another (so it's now and then, not all day).
                s.ChaseCooldown = math.max(s.ChaseCooldown - dt, 0f);
                if (s.Stage == 2)
                {
                    var town = SettlementOf(s.Home);
                    if (town >= 0 && s.StageTime < 40f && Lookup.TryGetValue(s.Partner, out var p) && !States[p].Gone)
                    {
                        ref var t = ref Terrain.Settlements.Value.Settlements[town];
                        var beast = States[p].Position.xz;
                        if (math.distance(beast, t.Centre) < t.Radius + 8f && math.distance(s.Position.xz, t.Centre) < t.Radius + 30f)
                        {
                            s.Target = beast;
                            // (Not right up to it: a spear's length off, it slows to keep there.)
                            targetSpeed = math.distance(s.Position.xz, beast) > 2.5f ? run : 0f;
                            moving = true;
                            desiredLook = 0f;
                            return;
                        }
                    }
                    s.Stage = 0;
                    s.StageTime = 0f;
                    s.Partner = Entity.Null;
                    s.ChaseCooldown = 90f;
                }
                else if (s.ChaseCooldown <= 0f && FindMenace(self, in s, out var menace))
                {
                    s.Stage = 2;
                    s.StageTime = 0f;
                    s.Partner = menace;
                    return;
                }
                var toPlayer = player.xz - s.Position.xz;
                var watching = math.length(toPlayer) < 15f && (playerSpeed > 7f || emote != IdleAction.None);
                if (s.Stage == 0)
                {
                    s.Target = s.WorkSpot;
                    moving = !watching;
                    targetSpeed = watching ? 0f : walk * 0.7f;
                    if (math.distance(s.Position.xz, s.WorkSpot) < 0.7f)
                        NextOnRound(ref s, ref rng);
                }
                else
                {
                    s.Timer -= dt;
                    desiredLook = math.sin(s.StageTime * 0.9f + s.Individual % 7) * 1.1f;
                    if (FindGuard(self, s, out var other))
                    {
                        // A word with the other guard (for a while, then on).
                        var to = other - s.Position.xz;
                        var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                        desiredLook = 0f;
                        if (s.StageTime < 10f)
                            s.Timer = math.max(s.Timer, 0.5f);
                    }
                    if (s.Timer <= 0f)
                    {
                        s.Stage = 0;
                        s.StageTime = 0f;
                    }
                }
                if (watching)
                {
                    var wanted = WrapAngle(math.atan2(toPlayer.x, toPlayer.y) - s.Yaw);
                    if (s.Stage == 0)
                        s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                    desiredLook = math.clamp(WrapAngle(math.atan2(toPlayer.x, toPlayer.y) - s.Yaw), -1.2f, 1.2f);
                }
            }

            /// <summary>
            /// A door to go in by: one of the three Door anchors of its settlement nearest where it is (which of them,
            /// by its individual number, so neighbours spread over the houses round them). False if there are none in
            /// its range.
            /// </summary>
            bool FindDoor(ref CreatureState s)
            {
                if (!Terrain.Anchors.IsCreated)
                    return false;
                ref var set = ref Terrain.Anchors.Value;
                var doors = new NativeList<int>(16, Allocator.Temp);
                Anchors.Near(ref set, s.Position, 30f, AnchorKind.Door, ref doors);
                if (doors.Length == 0)
                    Anchors.Near(ref set, new float3(s.Home.x, s.Position.y, s.Home.y), s.Leash + 10f, AnchorKind.Door, ref doors);
                if (doors.Length == 0)
                {
                    doors.Dispose();
                    return false;
                }
                // The nearest three, nearest first (a few insertion passes: the list is short).
                var nearest = new FixedList32Bytes<int>();
                for (int k = 0; k < doors.Length; k++)
                {
                    var d = math.distancesq(set.Anchors[doors[k]].Position.xz, s.Position.xz);
                    var at = nearest.Length;
                    while (at > 0 && math.distancesq(set.Anchors[nearest[at - 1]].Position.xz, s.Position.xz) > d)
                        at--;
                    if (at < 3)
                    {
                        nearest.Insert(at, doors[k]);
                        if (nearest.Length > 3)
                            nearest.RemoveAt(3);
                    }
                }
                doors.Dispose();
                var door = set.Anchors[nearest[(int)(s.Individual % (uint)nearest.Length)]];
                s.Anchor = door.Position;
                s.AnchorYaw = math.atan2(door.Facing.x, door.Facing.y);
                return true;
            }

            /// <summary>
            /// Goes indoors: walks to its door, steps in (fading from sight), stays in (through the night, or a while by
            /// day), then steps out again and gets on with its day. A door it can't get to, it gives up on.
            /// </summary>
            void GoIndoors(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, ref float targetSpeed, ref bool moving,
                           ref bool custom)
            {
                var door = s.Anchor.xz;
                var outward = new float2(math.sin(s.AnchorYaw), math.cos(s.AnchorYaw));
                const float through = 0.9f;   // seconds to step through
                switch (s.Stage)
                {
                    case 0:
                        s.Target = door;
                        targetSpeed = walk;
                        moving = true;
                        if (math.distance(s.Position.xz, door) < 0.4f)
                            NextStage(ref s);
                        else if (s.ModeTime > 60f)
                            Pause(ref s, ref b, ref rng);
                        break;
                    case 1:
                        custom = true;
                        s.Yaw = WrapAngle(s.AnchorYaw + math.PI);
                        s.Speed = 0.6f;
                        s.Position.xz = door - outward * (0.7f * math.saturate(s.StageTime / through));
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                        s.ActionTime = math.saturate(s.StageTime / through);
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        if (s.StageTime >= through)
                            NextStage(ref s);
                        break;
                    case 2:
                        custom = true;
                        s.Speed = 0f;
                        s.ActionTime = 1f;
                        if (SleepTime(ref b) < 0.5f)
                            s.Timer -= dt;
                        // (A guard comes out when its watch begins, however long it meant to stay in: a night guard
                        // asleep through the day is out again at dusk.)
                        if (s.Timer <= 0f || OnShift(s))
                            NextStage(ref s);
                        break;
                    default:
                        custom = true;
                        s.Yaw = s.AnchorYaw;
                        s.Speed = 0.6f;
                        s.Position.xz = door - outward * (0.7f * (1f - math.saturate(s.StageTime / through)));
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                        s.ActionTime = 1f - math.saturate(s.StageTime / through);
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                        if (s.StageTime >= through)
                            Pause(ref s, ref b, ref rng);
                        break;
                }
            }

            // ------------------------------------------------------------------------------- people

            /// <summary>
            /// Game for a hunter: the nearest wild grazer or small animal on land (not a predator, not a settlement's own,
            /// not someone's body) within range and within the hunter's own range from home.
            /// </summary>
            bool FindGame(Entity self, ref CreatureState s, float range, out Entity found)
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
                        var other = States[j];
                        ref var ob = ref Bodies[j].Blob.Value;
                        var size = ob.BodyLength * other.Scale;
                        if (Entities[j] == self || ob.Upright || ob.Habitat != Habitat.Land || ob.Prey.Length > 0 || ob.CaveDweller ||
                            other.Leash > 0f || other.Gone || other.Mode == CreatureMode.Puppet || other.Mode == CreatureMode.Held ||
                            size < 0.25f || size > 2.6f || ob.FleeDistance <= 0f ||
                            s.Leash > 0f && math.distance(other.Position.xz, s.Home) > s.Leash)
                            continue;
                        var d = math.distance(other.Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found != Entity.Null;
            }

            /// <summary>
            /// A hunter's outing (stages in FolkHunting): out towards the game openly, then creeping bent low once
            /// within 25 m; at about 11 m it stops and draws back (Aim), throws (Throw), and runs after the game a
            /// little way as it bolts (Chase), before giving up and heading home. Nothing is killed (no hunt ends in a
            /// kill until there's a health system: the throw always misses). Gives up if the game is gone or too long passes.
            /// </summary>
            void FolkHunt(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                          ref float targetSpeed, ref bool moving, ref float desiredLook, ref float bow, ref float headDown)
            {
                s.Timer -= dt;
                if (s.Timer <= 0f || !Lookup.TryGetValue(s.Partner, out var p) || States[p].Gone)
                {
                    HeadHome(ref s, ref rng);
                    return;
                }
                var prey = States[p];
                var to = prey.Position.xz - s.Position.xz;
                var distance = math.length(to);
                switch (s.Stage)
                {
                    case FolkHunting.Stalk:
                    {
                        var close = distance < 25f;
                        s.Target = prey.Position.xz - math.normalizesafe(to) * 1.5f;
                        moving = true;
                        targetSpeed = close ? walk * 0.35f : walk;
                        if (close)
                        {
                            bow = 1f;
                            headDown = 0.2f;
                        }
                        // It's seen them coming: off it goes, and the hunter after it.
                        if (prey.Mode == CreatureMode.Flee && close)
                            NextStage(ref s, FolkHunting.Chase);
                        else if (distance < 11f)
                            NextStage(ref s, FolkHunting.Aim);
                        break;
                    }
                    case FolkHunting.Aim:
                        TurnTowards(ref s, ref b, prey.Position.xz, dt);
                        bow = 0.3f;
                        if (s.StageTime > 1.4f)
                            NextStage(ref s, FolkHunting.Throw);
                        break;
                    case FolkHunting.Throw:
                        TurnTowards(ref s, ref b, prey.Position.xz, dt);
                        bow = 1f;   // leaning into the throw
                        if (s.StageTime > 0.7f)
                            NextStage(ref s, FolkHunting.Chase);
                        break;
                    default:
                        // After it a little way, then gives up.
                        s.Target = prey.Position.xz;
                        moving = true;
                        targetSpeed = run * 0.6f;
                        if (s.StageTime > 4f || distance > 40f)
                            HeadHome(ref s, ref rng);
                        break;
                }
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1f, 1f);
            }

            static void NextStage(ref CreatureState s, byte stage)
            {
                s.Stage = stage;
                s.StageTime = 0f;
            }

            /// <summary>Back towards home (a settlement's folk), to take up the day there.</summary>
            static void HeadHome(ref CreatureState s, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Target = (s.Leash > 0f ? s.Home : s.Position.xz) + rng.NextFloat2Direction() * rng.NextFloat(2f, 8f);
            }

            /// <summary>
            /// A hunter after this animal near enough to notice: creeping within 5 m (heard), aiming within 15 m (seen), or
            /// throwing or running after it within 40 m. Where they are.
            /// </summary>
            bool FindHunter(Entity self, ref CreatureState s, float alert, out float2 at)
            {
                using var profiled = HunterMarker.Auto();
                // (Only the hunters out now are looked at: usually none, so it costs nothing.)
                at = default;
                for (int h = 0; h < Hunters.Length; h++)
                {
                    var other = States[Hunters[h]];
                    if (other.Partner != self)
                        continue;
                    var range = other.Stage == FolkHunting.Stalk ? 5f * alert : other.Stage == FolkHunting.Aim ? 15f * alert : 40f;
                    if (math.distance(other.Position.xz, s.Position.xz) < range)
                    {
                        at = other.Position.xz;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Where the player with this Holder id is, and which way they face (false if they've gone).</summary>
            bool FindHolder(int holder, out float3 position, out float yaw) => FindHolder(holder, out position, out yaw, out _);

            bool FindHolder(int holder, out float3 position, out float yaw, out int species)
            {
                for (int p = 0; p < PlayerIds.Length; p++)
                    if (PlayerIds[p] == holder)
                    {
                        position = Players[p];
                        yaw = PlayerYaws[p];
                        species = PlayerSpecies[p];
                        return true;
                    }
                position = default;
                yaw = 0f;
                species = -1;
                return false;
            }

            /// <summary>
            /// Being petted: turned to the person, it leans into the hand with its tail going (stage 0) or stands for it,
            /// watching them (1). When the person leaves off (or walks away) it goes on with its day; a friendly one
            /// keeps them company.
            /// </summary>
            void Petted(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float sit, ref float bow, ref float desiredLook)
            {
                s.Timer -= dt;
                var hasPerson = FindHolder(s.Holder, out var person, out _);
                if (s.Timer <= 0f || !hasPerson || math.distance(person.xz, s.Position.xz) > Interactions.Reach + 1.5f)
                {
                    s.Holder = 0;
                    if (b.Friendly && hasPerson)
                    {
                        SetMode(ref s, CreatureMode.Greet);
                        s.Timer = rng.NextFloat(20f, 45f);
                    }
                    else
                    {
                        Pause(ref s, ref b, ref rng);
                        // Tidying itself after being handled.
                        if ((b.Idles & IdleActions.Groom) != 0 && rng.NextFloat() < 0.5f)
                        {
                            s.Action = IdleAction.Groom;
                            s.ActionLength = rng.NextFloat(2f, 4f);
                        }
                    }
                    return;
                }
                s.Focus = person.xz;
                TurnTowards(ref s, ref b, person.xz, dt);
                var to = person.xz - s.Position.xz;
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                if (s.Stage == 0)
                {
                    // Leaning in: head pressed up to the hand, chest down a little, tail going.
                    desiredLook = lookAt * 0.6f;
                    bow = 0.3f;
                    sit = b.Friendly ? 0.6f : 0f;
                    if (s.Action == IdleAction.None && (b.Idles & IdleActions.TailFlick) != 0 && rng.NextFloat() < dt * 1.5f)
                    {
                        s.Action = IdleAction.TailFlick;
                        s.ActionTime = 0f;
                        s.ActionLength = rng.NextFloat(1.5f, 2.5f);
                    }
                }
                else
                    desiredLook = lookAt;   // putting up with it, keeping an eye on them
            }

            /// <summary>
            /// In someone's arms, across their chest. One that isn't friendly puts up with it for a while, then wriggles
            /// (a shake), and before long squirms free and runs. If the holder has gone, it's back on the ground.
            /// </summary>
            void Held(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                s.Speed = 0f;
                if (!FindHolder(s.Holder, out var holder, out var yaw, out var holderSpecies))
                {
                    Interactions.PutDown(ref s, Terrain, s.Position, ref rng);
                    return;
                }
                s.Position = Interactions.Hands(Carry, holderSpecies, holder, yaw, Interactions.HalfWidth(ref b, s.Scale));
                s.Yaw = yaw + math.PI * 0.5f;
                s.YawRate = 0f;
                s.Timer -= dt;
                if (s.Timer < 2f && s.Action == IdleAction.None && (b.Idles & IdleActions.Shake) != 0)
                {
                    s.Action = IdleAction.Shake;
                    s.ActionTime = 0f;
                    s.ActionLength = 1.2f;
                }
                if (s.Timer <= 0f)
                {
                    // Free: it drops to the ground beside them and runs.
                    var side = new float3(math.cos(yaw), 0f, -math.sin(yaw)) * (rng.NextBool() ? 0.6f : -0.6f);
                    Interactions.PutDown(ref s, Terrain, holder + side, ref rng);
                    Interactions.Bolt(ref s, ref b, Terrain, holder, ref rng);
                    s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - holder.xz, new float2(1f, 0f)) * 10f;
                }
            }

            /// <summary>
            /// Someone near is emoting. Folk stop, turn to them and answer in kind (a wave back, a bow, a cheer; some
            /// dance along, the rest cheer them on), each after a moment of their own, and a few shy ones not at all.
            /// Friendly animals come over to see; one sitting by them play-bows back to a bow. A curious one comes partway
            /// and watches (Greet's Curious stage); the skittish run sooner (commotion, in Think); some pay no heed.
            /// </summary>
            void Answer(ref CreatureState s, ref CreatureBlob b, ref Random rng, IdleAction emote, float3 player, float distance, float dt)
            {
                if (b.Upright)
                {
                    if (distance > 20f || !(Free(s.Mode) || s.Mode == CreatureMode.Work))
                        return;
                    // Answering already: keep facing them.
                    if (s.Action >= IdleAction.Wave)
                    {
                        TurnTowards(ref s, ref b, player.xz, dt);
                        return;
                    }
                    if (s.Action != IdleAction.None || CreatureBlob.Trait(s.Individual, 21) < -0.75f || rng.NextFloat() > dt * 2.5f)
                        return;
                    var reply = emote != IdleAction.Dance ? emote : CreatureBlob.Trait(s.Individual, 22) > 0f ? IdleAction.Dance : IdleAction.Cheer;
                    SetMode(ref s, CreatureMode.Idle);
                    s.Action = reply;
                    s.ActionTime = 0f;
                    s.ActionLength = reply == IdleAction.Dance ? rng.NextFloat(4f, 7f) : reply == IdleAction.Wave ? 2f : 1.8f;
                    s.Timer = s.ActionLength + rng.NextFloat(0.5f, 1.5f);
                    s.Speed = 0f;
                    s.Focus = player.xz;
                    return;
                }
                if (b.Reaction == EmoteReaction.Curious)
                {
                    if (distance < 25f && Free(s.Mode))
                    {
                        SetMode(ref s, CreatureMode.Greet);
                        s.Stage = Curious;
                        s.Timer = rng.NextFloat(6f, 12f);
                    }
                    return;
                }
                if (b.Reaction != EmoteReaction.Wags)
                    return;
                if (emote != IdleAction.Bow && distance < 20f && Free(s.Mode))
                {
                    SetMode(ref s, CreatureMode.Greet);
                    s.Timer = rng.NextFloat(15f, 30f);
                }
                else if (emote == IdleAction.Bow && s.Mode == CreatureMode.Greet && distance < 4f && s.Action == IdleAction.None &&
                         (b.Idles & IdleActions.Stretch) != 0)
                {
                    s.Action = IdleAction.Stretch;
                    s.ActionTime = 0f;
                    s.ActionLength = 2.2f;
                }
            }

            /// <summary>Greet's stage for a curious animal come to look (EmoteReaction.Curious), not to sit by them.</summary>
            const byte Curious = 1;

            /// <summary>
            /// Trots over to the person (to their side, not into them), then sits by them looking up and wagging its
            /// tail; follows when they walk off (as far as its home range allows), until it loses interest.
            /// </summary>
            void Greet(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float3 player, float walk, float run,
                       ref float targetSpeed, ref bool moving, ref float sit, ref float desiredLook)
            {
                s.Timer -= dt;
                var to = player.xz - s.Position.xz;
                var distance = math.length(to);
                if (s.Timer <= 0f || distance > 40f)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                // Its own spot round the person (one of five, by individual), so several don't pile into each other.
                var dir = math.normalizesafe(to, new float2(0f, 1f));
                var angle = ((int)(s.Individual % 5u) - 2) * 0.75f;
                var away = new float2(dir.x * math.cos(angle) - dir.y * math.sin(angle), dir.x * math.sin(angle) + dir.y * math.cos(angle));
                var spot = player.xz - away * (1.3f + b.BodyLength * s.Scale * 0.5f);
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                if (s.Stage == Curious)
                {
                    // Curious: up to a wary few metres (well outside where it would bolt), slowly, then stands watching
                    // them, sniffing now and then, until it's seen enough.
                    var standoff = math.max(b.FleeDistance * s.Scale * 0.6f, 2f + b.BodyLength * s.Scale);
                    if (distance > standoff + 0.5f)
                    {
                        s.Target = player.xz - dir * standoff;
                        targetSpeed = walk * 0.6f;
                        moving = true;
                        desiredLook = lookAt;
                    }
                    else
                    {
                        TurnTowards(ref s, ref b, player.xz, dt);
                        desiredLook = lookAt * 0.6f;
                        if (s.Action == IdleAction.None && (b.Idles & IdleActions.Sniff) != 0 && rng.NextFloat() < dt * 0.4f)
                        {
                            s.Action = IdleAction.Sniff;
                            s.ActionTime = 0f;
                            s.ActionLength = rng.NextFloat(1f, 2f);
                        }
                    }
                    return;
                }
                if (math.distance(s.Position.xz, spot) > 0.6f)
                {
                    s.Target = spot;
                    targetSpeed = distance > 6f ? run * 0.7f : walk;
                    moving = true;
                    desiredLook = lookAt;
                }
                else
                {
                    // Sitting by them, facing them, tail going.
                    TurnTowards(ref s, ref b, player.xz, dt);
                    sit = 1f;
                    desiredLook = lookAt * 0.5f;
                    if (s.Action == IdleAction.None && rng.NextFloat() < dt * 0.8f)
                    {
                        s.Action = IdleAction.TailFlick;
                        s.ActionTime = 0f;
                        s.ActionLength = rng.NextFloat(1.5f, 3f);
                    }
                }
            }

            // ------------------------------------------------------------------------------- caves and roosts

            /// <summary>
            /// A drinking spot by underground water: one of the Pool anchors the caves export (on a pool's rim or a
            /// stream bank, facing the water), near enough on about the same level, spread over the anchor's size.
            /// </summary>
            bool FindCaveWater(ref CreatureState s, ref CreatureBlob b, ref Random rng, out float2 shore, out float2 water)
            {
                shore = water = default;
                if (!Terrain.Anchors.IsCreated ||
                    !Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 20f, AnchorKind.Pool, ref rng, out var pool) ||
                    math.abs(pool.Position.y - s.Position.y) > 3f)
                    return false;
                var spot = pool.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(0f, pool.Size * 0.5f);
                if (!CaveFloor(spot, pool.Position.y))
                    return false;
                shore = spot - pool.Facing * (b.BodyLength * 0.4f * s.Scale);
                water = spot + pool.Facing;
                return true;
            }

            /// <summary>
            /// Where a cave dweller runs to next: a point along the passages (a cave node within 15 m, on about its level),
            /// the one that puts most distance between it and the threat without heading towards it; straight away
            /// if there's none (the wall-sliding in Advance keeps it off the rock either way).
            /// </summary>
            float2 CaveEscape(in CreatureState s, float2 threat, ref Random rng)
            {
                var away = math.normalizesafe(s.Position.xz - threat, rng.NextFloat2Direction());
                var best = s.Position.xz + away * 12f;
                if (!Terrain.Caves.IsCreated)
                    return best;
                ref var set = ref Terrain.Caves.Value;
                var bestScore = float.MinValue;
                for (int n = 0; n < set.Nodes.Length; n++)
                {
                    var p = set.Nodes[n].Position;
                    var to = p.xz - s.Position.xz;
                    var d = math.length(to);
                    // Near ones: the next stretch of passage (it picks another on arriving).
                    if (d < 2.5f || d > 15f || math.abs(p.y - s.Position.y) > 4f)
                        continue;
                    // Not past the threat: the way to it mustn't lead towards what it's running from.
                    if (math.dot(to / d, away) < -0.2f)
                        continue;
                    // (A little chance in it, so one stuck at a wall tries another way.)
                    var score = math.distance(p.xz, threat) - d * 0.3f + rng.NextFloat(0f, 4f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p.xz;
                    }
                }
                return best;
            }

            /// <summary>
            /// Whether a cave dweller at this height can step here: cave floor at about the same level, under the rock
            /// (not out of the mouth into the open: cave dwellers stay in their cave).
            /// </summary>
            bool CaveFloor(float2 xz, float y) => CaveFloorAt(xz, y, out _);

            /// <summary>
            /// The cave floor under a point at about body height above <paramref name="y"/>, if there's cave air there
            /// (so not in the rock: inside solid rock FloorUnder answers "in a cave" with the floor where the point
            /// is) and the floor is near that level, under the rock (not out of the mouth).
            /// </summary>
            bool CaveFloorAt(float2 xz, float y, out float floor)
            {
                floor = y;
                var above = new float3(xz.x, y + 0.6f, xz.y);
                if (Terrain.CaveDistance(above) >= 0f)
                    return false;
                floor = Terrain.FloorUnder(above, out var inCave);
                // (Up a step of up to half a metre; down off a ledge of up to 3 m. A real floor is below the point
                // asked from: FloorUnder answers with the point's own height when its search starts in rock.)
                return inCave && floor < above.y - 0.05f && floor - y < 0.5f && y - floor < 3f && floor < Terrain.SampleHeight(xz) - 1f;
            }

            /// <summary>A short, low loop to somewhere nearby: a bat hawking for insects.</summary>
            void NightLoop(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = s.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(6f, 20f);
                s.TargetHeight = Terrain.SampleHeight(s.Target) + math.lerp(b.FlightHeight.x, b.FlightHeight.y, rng.NextFloat());
            }

            /// <summary>
            /// Picks a roost: the ceiling of the nearest cave's inner passage (within 150 m), else the underside of a
            /// branch nearby.
            /// </summary>
            /// <summary>How many are roosting at (or on their way to) a roost: their Anchor is on it.</summary>
            int RoostUsers(in Anchor roost)
            {
                var users = 0;
                var r2 = (roost.Size + 0.5f) * (roost.Size + 0.5f);
                for (int i = 0; i < States.Length; i++)
                    if (States[i].Mode == CreatureMode.Roost && math.distancesq(States[i].Anchor, roost.Position) < r2)
                        users++;
                return users;
            }

            void StartRoost(ref CreatureState s, ref CreatureBlob b, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Roost);
                s.Timer = rng.NextFloat(60f, 200f);
                s.LairCave = -1;
                // A Roost anchor the caves export: a spot on a chamber ceiling, which bats share (spread over its Size), up
                // to its Capacity: a full one, it tries another.
                var roost = default(Anchor);
                var picked = false;
                for (int tries = 0; tries < 4 && !picked && Terrain.Anchors.IsCreated; tries++)
                    picked = Anchors.Pick(ref Terrain.Anchors.Value, s.Position, 150f, AnchorKind.Roost, ref rng, out roost) &&
                             (roost.Capacity == 0 || RoostUsers(roost) < roost.Capacity);
                if (picked && Terrain.Caves.IsCreated && roost.InCave && roost.OwnerIndex < Terrain.Caves.Value.Caves.Length)
                {
                    ref var caves = ref Terrain.Caves.Value;
                    ref var home = ref caves.Caves[roost.OwnerIndex];
                    // The passage node nearest the roost: the way there.
                    var nearest = 0;
                    for (int n = 1; n < home.NodeCount; n++)
                        if (math.distancesq(caves.Nodes[home.FirstNode + n].Position, roost.Position) <
                            math.distancesq(caves.Nodes[home.FirstNode + nearest].Position, roost.Position))
                            nearest = n;
                    s.LairCave = (short)roost.OwnerIndex;
                    s.LairNode = (short)nearest;
                    var spread = rng.NextFloat2Direction() * rng.NextFloat(0f, roost.Size * 0.8f);
                    s.Anchor = roost.Position + new float3(spread.x, 0f, spread.y);
                    s.AnchorSize = 0.3f;
                    s.Stage = 1;
                    return;
                }
                if (Terrain.Caves.IsCreated)
                {
                    ref var set = ref Terrain.Caves.Value;
                    var best = 150f;
                    for (int i = 0; i < set.Caves.Length; i++)
                    {
                        var d = math.distance(set.Caves[i].Mouth.xz, s.Position.xz);
                        if (d < best && set.Caves[i].NodeCount >= 2 && set.Caves[i].NodeCount < 80)
                        {
                            best = d;
                            s.LairCave = (short)i;
                        }
                    }
                }
                if (s.LairCave >= 0)
                {
                    ref var set = ref Terrain.Caves.Value;
                    ref var cave = ref set.Caves[s.LairCave];
                    // Somewhere in the far half of the cave, on the ceiling straight above the passage's middle.
                    s.LairNode = (short)rng.NextInt(cave.NodeCount / 2, cave.NodeCount);
                    var p = set.Nodes[cave.FirstNode + s.LairNode].Position;
                    for (int k = 0; k < 60 && Terrain.CaveDistance(p + new float3(0f, 0.2f, 0f)) < 0f; k++)
                        p.y += 0.2f;
                    s.Anchor = p + rng.NextFloat3Direction() * new float3(0.3f, 0f, 0.3f);
                    s.AnchorSize = 0.3f;
                    s.Stage = 1;
                    return;
                }
                if (HasFlora && FloraPlacement.FindTree(ref Flora.Value, Terrain, s.Position.xz, 60f, 4f, ref rng, out var tree))
                {
                    var under = tree.Base.xz + rng.NextFloat2Direction() * (tree.TrunkRadius + tree.CrownRadius * 0.3f);
                    s.Anchor = new float3(under.x, tree.Base.y + tree.CrownBase, under.y);
                    s.AnchorSize = 0.2f;
                    s.Stage = 198;
                    return;
                }
                // Nowhere to hang: keep flying.
                NightLoop(ref s, ref b, ref rng);
            }

            /// <summary>
            /// A bat's jinking: its heading swings off the line to its target and back, a couple of times a second
            /// (after insects), each bat on its own beat. None near the target, so it still lands and hangs up.
            /// </summary>
            static float Swerve(in CreatureState s, ref CreatureBlob b, float distance)
            {
                if (!b.Roosts || s.Fly < 0.5f)
                    return 0f;
                var own = (s.Individual % 89) * 0.37f;
                return (0.7f * math.sin(s.Clock * 2.3f + own) + 0.45f * math.sin(s.Clock * 5.3f + own * 1.7f)) * math.saturate((distance - 2f) / 4f);
            }

            /// <summary>Flies straight at a point (no ground to keep clear of: inside a cave). Returns the distance left.</summary>
            float FlyDirect(ref CreatureState s, ref CreatureBlob b, float3 target, float speed, float dt, ref float vertical)
            {
                var to = target - s.Position;
                var distance = math.length(to);
                var wanted = WrapAngle(math.atan2(to.x, to.z) - s.Yaw) + Swerve(s, ref b, distance);
                var turn = math.clamp(wanted, -b.TurnRate * 2f * dt, b.TurnRate * 2f * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                var flare = math.saturate(1f - distance / CreatureMotion.FlareDistance);
                var wantSpeed = math.lerp(math.max(speed, b.FlySpeed * CreatureMotion.MinAirspeed), math.max(distance * 1.5f, 0.3f), flare);
                s.Speed += (wantSpeed - s.Speed) * math.min(1f, dt * 3f);
                // Along its heading, rising or sinking no steeper than the flight angle (the last moments of a landing
                // excepted): it swings round to line up rather than lifting straight up.
                var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
                var flat = math.length(to.xz);
                var level = math.abs(to.y) < 0.3f;
                var across = level ? math.min(s.Speed, flat / math.max(dt, 1e-4f)) : s.Speed;
                var steepest = across * CreatureMotion.MaxClimb;
                var vy = math.clamp(to.y * 2f, -steepest, steepest);
                var move = forward * across + new float3(0f, vy, 0f);
                s.Position += move * dt;
                vertical = math.max(math.clamp(vy / math.max(b.FlySpeed, 1f), -0.6f, 0.5f), flare * 0.45f);
                return math.length(target - s.Position);
            }

            void Roost(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, ref float vertical)
            {
                var speed = b.FlySpeed * 0.6f;
                var hangAt = s.Anchor - new float3(0f, (b.BodyLength * 0.5f + b.FrontRadius) * s.Scale + 0.05f, 0f);
                var waking = SleepTime(ref b) < 0.3f;
                if (s.Stage == 200)
                {
                    // Hanging head down, asleep, till dusk.
                    vertical = -1f;
                    s.Speed = 0f;
                    s.Position = hangAt;
                    s.Timer -= dt;
                    if (waking && s.Timer <= 0f)
                    {
                        if (s.LairCave >= 0)
                        {
                            s.Stage = (byte)(100 + math.min(s.LairNode, 89));
                            s.StageTime = 0f;
                        }
                        else
                            NightLoop(ref s, ref b, ref rng);
                    }
                    return;
                }
                if (s.Stage == 198)
                {
                    // To a branch outdoors: flying as usual (clear of the ground), to hang just under it.
                    if (FlyTo(ref s, ref b, hangAt.xz, hangAt.y, b.FlySpeed, dt, ref vertical))
                    {
                        s.Stage = 200;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 60f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage == 199)
                {
                    if (FlyDirect(ref s, ref b, hangAt, speed * 0.6f, dt, ref vertical) < 0.15f)
                    {
                        s.Stage = 200;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 30f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.LairCave < 0 || !Terrain.Caves.IsCreated)
                {
                    NightLoop(ref s, ref b, ref rng);
                    return;
                }
                ref var set = ref Terrain.Caves.Value;
                ref var cave = ref set.Caves[s.LairCave];
                if (s.Stage == 1)
                {
                    // To the cave's mouth, from outside.
                    var mouth = cave.Mouth;
                    if (FlyTo(ref s, ref b, mouth.xz, mouth.y, b.FlySpeed, dt, ref vertical) || math.distance(s.Position, mouth) < 1.2f)
                    {
                        s.Stage = 10;
                        s.StageTime = 0f;
                    }
                    else if (s.StageTime > 60f)
                        NightLoop(ref s, ref b, ref rng);
                    return;
                }
                if (s.Stage >= 10 && s.Stage < 100)
                {
                    // In along the passage, node by node, then up to the ceiling.
                    var node = s.Stage - 10;
                    if (FlyDirect(ref s, ref b, set.Nodes[cave.FirstNode + node].Position, speed, dt, ref vertical) < 1f)
                    {
                        s.Stage = (byte)(node >= s.LairNode ? 199 : s.Stage + 1);
                        s.StageTime = 0f;
                    }
                    return;
                }
                // Out again at dusk, back along the passage to the mouth, then off hawking.
                var back = s.Stage - 100;
                var target = back >= 0 ? set.Nodes[cave.FirstNode + back].Position : cave.Mouth;
                if (FlyDirect(ref s, ref b, target, speed, dt, ref vertical) < 1f)
                {
                    if (back <= 0)
                        NightLoop(ref s, ref b, ref rng);
                    else
                    {
                        s.Stage--;
                        s.StageTime = 0f;
                    }
                }
            }

            // ------------------------------------------------------------------------------- flight

            /// <summary>Whether a flying species is in the air now (flying somewhere, fleeing or hunting).</summary>
            static bool Flying(in CreatureState s) =>
                (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Hunt) && s.Stage >= 1 || s.Mode == CreatureMode.Flee ||
                s.Mode == CreatureMode.Work && s.Task == CreatureTask.Messenger && s.Stage != 1 ||
                s.Mode == CreatureMode.Roost && s.Stage != 200;

            /// <summary>
            /// Takes off for somewhere new: a branch in a tree nearby (more likely by its Perching, and always to
            /// sleep), or a patch of ground.
            /// </summary>
            bool StartFlight(ref CreatureState s, ref CreatureBlob b, ref Random rng, bool toSleep)
            {
                var range = math.max(b.WanderRadius, 20f);
                // A soarer mostly goes up to circle a while over somewhere near (not straight after a spell of it).
                if (b.Soars && !toSleep && s.Stage != 2 && rng.NextFloat() < 0.75f)
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 2;
                    s.Focus = s.Position.xz + rng.NextFloat2Direction() * range * rng.NextFloat(0.2f, 0.8f);
                    s.TargetHeight = Cruise(s, ref b, s.Focus);
                    s.Timer = rng.NextFloat(60f, 240f);
                    s.Target = s.Focus;
                    return true;
                }
                if (toSleep && b.Nests != NestSite.None && b.Nests != NestSite.Ground && s.AnchorSize > 0f)
                {
                    // Home to its nest.
                    SetMode(ref s, CreatureMode.Wander);
                    s.Stage = 1;
                    s.Target = s.Anchor.xz;
                    s.TargetHeight = s.Anchor.y;
                    return true;
                }
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
                if (!OpenGround(spot))
                    return false;
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                s.Target = spot;
                s.TargetHeight = Terrain.SampleHeight(spot);
                return true;
            }

            /// <summary>
            /// Soaring: round and round over its centre (Focus) at its own radius and turning its own way, steering for a
            /// point a little ahead on the circle so it never slows as if to land; when its time is up, off to come down
            /// somewhere.
            /// </summary>
            void Soar(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
                s.Timer -= dt;
                if (s.Timer <= 0f && b.Breaches)
                {
                    // Back down into the deep water under the circle (where it came up), nose first.
                    s.Stage = 3;
                    s.Target = s.Focus;
                    var water = Terrain.SampleGround(s.Focus);
                    s.TargetHeight = math.max(water.WaterSurface - b.SwimDepth * s.Scale * 0.5f, water.Height + 0.5f);
                    return;
                }
                if (s.Timer <= 0f)
                {
                    if (StartFlight(ref s, ref b, ref rng, false))
                        return;
                    s.Timer = 10f;   // (nowhere to come down just here: round again)
                }
                // A sky serpent now and then climbs to a higher pass, or sinks to a lower one, over the same water.
                if (b.Breaches && rng.NextFloat() < dt / 40f)
                    s.TargetHeight = Cruise(s, ref b, s.Focus) + rng.NextFloat(0f, 45f);
                var radius = math.lerp(30f, 70f, CreatureBlob.Trait(s.Individual, 21) * 0.5f + 0.5f);
                var way = CreatureBlob.Trait(s.Individual, 22) > 0f ? 1f : -1f;
                var from = s.Position.xz - s.Focus;
                var angle = math.atan2(from.y, from.x) + 0.6f * way;
                s.Target = s.Focus + new float2(math.cos(angle), math.sin(angle)) * radius;
                // (Its height over the circle: its cruising height over the middle, held as the ground falls away.)
                s.TargetHeight = math.max(s.TargetHeight, Cruise(s, ref b, s.Position.xz));
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
                // (Right over the spot the bearing means nothing: hold the heading.)
                var wanted = distance < 0.1f ? 0f : WrapAngle(math.atan2(to.x, to.y) - s.Yaw) + Swerve(s, ref b, distance);
                var turnRate = b.TurnRate * 1.5f;
                var turn = math.clamp(wanted, -turnRate * dt, turnRate * dt);
                s.Yaw = WrapAngle(s.Yaw + turn);
                s.YawRate = turn / math.max(dt, 1e-4f);
                // Airspeed never drops below the minimum (a bird above its landing spot circles down rather than
                // sinking), except in the flare, where it slows to touch down within a body length or two.
                var minSpeed = b.FlySpeed * CreatureMotion.MinAirspeed;
                var flare = math.saturate(1f - distance / CreatureMotion.FlareDistance);
                var cruiseSpeed = math.max(speed * math.saturate(math.cos(wanted) + 0.4f), minSpeed);
                var wantSpeed = math.lerp(cruiseSpeed, math.max(distance * 1.5f, 0.3f), flare);
                // Taking off from standing: a hop forward, straight into the minimum speed.
                if (s.Fly < 0.5f)
                    s.Speed = math.max(s.Speed, minSpeed);
                s.Speed += (wantSpeed - s.Speed) * math.min(1f, dt * 2f);
                if (flare <= 0f)
                    s.Speed = math.max(s.Speed, minSpeed);
                // Always moving forward: arriving over the spot too high, it carries on past and circles down.
                var step = s.Speed * dt;
                s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * step;

                // Height: cruising, or coming down the glide slope to the landing spot; never climbing or sinking
                // steeper than the flight angle allows for how far it moved forward.
                // (Never below where it's landing: a nest or branch can be higher than its usual cruising height.)
                var cruise = math.max(math.max(Cruise(s, ref b, s.Position.xz), Cruise(s, ref b, target)), targetHeight);
                // (The slope meets the landing height just short of the spot, so it touches down still moving forward.
                // Landing on the ground: the ground right where it is; on a branch: the branch's height.)
                var groundHere = Terrain.SampleHeight(s.Position.xz);
                var onGround = targetHeight < Terrain.SampleHeight(target) + 0.1f;
                var touchdown = onGround && distance < CreatureMotion.FlareDistance ? groundHere : targetHeight;
                var wantY = math.min(cruise, touchdown + math.max(distance - 0.3f, 0f) * CreatureMotion.GlideSlope);
                // (In the flare, the last few centimetres settle as it slows.)
                var steepest = math.max(step / math.max(dt, 1e-4f) * CreatureMotion.MaxClimb, flare * 0.4f);
                var vy = math.clamp((wantY - s.Position.y) * 1.5f, -steepest, steepest);
                // In the flare the last of the height goes at a steady rate (not ever more slowly), so it settles as
                // it slows rather than hanging over the spot.
                var gap = wantY - s.Position.y;
                if (flare > 0f && math.abs(gap) > 1e-3f)
                    vy = math.sign(gap) * math.min(math.max(math.abs(vy), 0.3f * flare), math.abs(gap) / math.max(dt, 1e-4f));
                s.Position.y += vy * dt;
                // Clear of the ground (except right at the landing spot).
                var floor = Terrain.SampleHeight(s.Position.xz) + (distance > 1f ? 0.4f : 0f);
                s.Position.y = math.max(s.Position.y, floor);
                // Nose with the climb or descent; nose up in the flare.
                vertical = math.max(math.clamp(vy / math.max(b.FlySpeed, 1f), -0.6f, 0.5f), flare * 0.45f);
                // Touches down where it is, once it's down at the landing height near the spot (no snap).
                if (distance < 0.5f && math.abs(s.Position.y - touchdown) < 0.005f)
                {
                    s.Position.y = touchdown;
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
                    // Steep (a stoop), but never straight down: at least the minimum airspeed forward, and no
                    // steeper than 70°.
                    s.Speed = math.clamp(distance / math.max(drop / 9f, 0.1f), b.FlySpeed * CreatureMotion.MinAirspeed, b.FlySpeed * 1.3f);
                    s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * dt;
                    s.Position.y = math.max(s.Position.y - math.min(9f, s.Speed * 2.75f) * dt, surface + 0.15f);
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
                using var profiled = PredatorMarker.Auto();
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

            /// <summary>Another of its kind close by that has just started running: where from (what it runs from).</summary>
            bool FindAlarm(Entity self, ref CreatureState s, int species, out float2 from)
            {
                using var profiled = AlarmMarker.Auto();
                from = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (Entities[j] != self && Bodies[j].Species == species && other.Mode == CreatureMode.Flee && other.ModeTime < 3f &&
                            math.distance(other.Position.xz, s.Position.xz) < 12f)
                        {
                            from = other.Focus;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Another bird of its kind close by that has just taken off somewhere (not fleeing).</summary>
            bool FindTakeoff(Entity self, ref CreatureState s, int species, out CreatureState leader)
            {
                leader = default;
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var other = States[j];
                        if (Entities[j] != self && Bodies[j].Species == species && other.Mode == CreatureMode.Wander && other.Stage >= 1 &&
                            other.ModeTime < 1.5f && math.distance(other.Position.xz, s.Position.xz) < 12f)
                        {
                            leader = other;
                            return true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>Takes off after a flock-mate, to land a little way from where it's going (same height: a branch, or the ground).</summary>
            void Follow(ref CreatureState s, in CreatureState leader, ref Random rng)
            {
                SetMode(ref s, CreatureMode.Wander);
                s.Stage = 1;
                var groundThere = Terrain.SampleHeight(leader.Target);
                var onBranch = leader.TargetHeight > groundThere + 0.5f;
                s.Target = leader.Target + rng.NextFloat2Direction() * (onBranch ? rng.NextFloat(0.3f, 1.2f) : rng.NextFloat(0.8f, 3f));
                s.TargetHeight = onBranch ? leader.TargetHeight + rng.NextFloat(-0.4f, 0.4f) : Terrain.SampleHeight(s.Target);
            }

            /// <summary>Another of its kind within earshot, howling.</summary>
            bool Howling(Entity self, ref CreatureState s, int species)
            {
                var c = Cell(s.Position.xz);
                for (int z = -3; z <= 3; z++)
                for (int x = -3; x <= 3; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] != self && Bodies[j].Species == species && States[j].Action == IdleAction.Howl &&
                            States[j].ActionTime > 0.2f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
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
                using var profiled = PlaymateMarker.Auto();
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
                using var profiled = AdvanceMarker.Auto();
                var forward = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                CreatureMotion.Accelerate(ref s, ref b, dt);
                if (s.Speed > 0f)
                {
                    MovingMarker.Begin();
                    MovingMarker.End();
                }
                if (b.Crawls && CaveWalker(s, ref b))
                {
                    SampleMarker.Begin();
                    Crawl(ref s, ref b, dt);
                    SampleMarker.End();
                    return;
                }
                if (CaveWalker(s, ref b))
                {
                    SampleMarker.Begin();
                    // On the passage floor, never into rock: blocked, it slides along the wall (the nearest open way
                    // to either side of its heading, turning towards it), or stops.
                    var step = s.Speed * dt;
                    var next = s.Position.xz + forward * step;
                    if (step > 0f && !CaveFloor(next, s.Position.y))
                    {
                        var moved = false;
                        // (All the way round if need be: cornered, it turns back the way it came.)
                        for (int k = 1; k <= 7 && !moved; k++)
                            for (int side = -1; side <= 1 && !moved; side += 2)
                            {
                                var angle = s.Yaw + side * k * 0.45f;
                                var slide = s.Position.xz + new float2(math.sin(angle), math.cos(angle)) * step;
                                if (CaveFloor(slide, s.Position.y))
                                {
                                    next = slide;
                                    s.Yaw = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                                    moved = true;
                                }
                            }
                        if (!moved)
                        {
                            next = s.Position.xz;
                            s.Speed = 0f;
                        }
                    }
                    s.Position.xz = next;
                    // (Only a floor in the cave: outside it FloorUnder gives the surface, which would lift it through
                    // the rock to the ground overhead.)
                    // Onto the floor: up a step at once, down off a ledge falling.
                    if (CaveFloorAt(s.Position.xz, s.Position.y, out var floorY))
                        s.Position.y = floorY > s.Position.y ? floorY : math.max(floorY, s.Position.y - dt * 6f);
                    SampleMarker.End();
                    using (PhaseMarker.Auto())
                        CreatureMotion.AdvancePhase(ref s, ref b, dt);
                    return;
                }
                SampleMarker.Begin();
                var to = s.Position.xz + forward * s.Speed * dt;
                if (s.Speed > 0f && WalksRoundWalls(in s, ref b, keepHeight))
                {
                    // Buildings are solid: walled off, it walks along the wall (the nearest open way to either side,
                    // turning towards it), so it goes round the building rather than through it; boxed in, it stops.
                    // (One already inside, spawned there, walks out.)
                    var radius = math.max(b.FrontRadius * s.Scale, 0.2f);
                    if (Walled(to, radius) && !Walled(s.Position.xz, radius))
                    {
                        var step = s.Speed * dt;
                        var moved = false;
                        for (int k = 1; k <= 7 && !moved; k++)
                            for (int side = -1; side <= 1 && !moved; side += 2)
                            {
                                var angle = s.Yaw + side * k * 0.45f;
                                var slide = s.Position.xz + new float2(math.sin(angle), math.cos(angle)) * step;
                                if (!Walled(slide, radius))
                                {
                                    to = slide;
                                    s.Yaw = WrapAngle(s.Yaw + WrapAngle(angle - s.Yaw) * 0.3f);
                                    moved = true;
                                }
                            }
                        if (!moved)
                        {
                            to = s.Position.xz;
                            s.Speed = 0f;
                        }
                    }
                }
                s.Position.xz = to;
                if (b.Haunts)
                {
                    // Drifting half a metre above the ground or water, rising and sinking slowly.
                    var g = Terrain.SampleGround(s.Position.xz);
                    var floor = g.HasWater ? math.max(g.Height, g.WaterSurface) : g.Height;
                    var drift = floor + 0.45f * s.Scale + math.sin(s.Clock * 0.9f + (s.Individual % 7)) * 0.12f * s.Scale;
                    s.Position.y += (drift - s.Position.y) * math.min(1f, dt * 2f);
                }
                else if (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies)
                {
                    if (!keepHeight)
                        s.Position.y = Terrain.SampleHeight(s.Position.xz);
                    // Out of its depth, a land animal that swims sits low: only its head, neck and a strip of back
                    // above the surface (a bear's broad back shows more than a deer's), legs paddling below, bobbing
                    // gently. Wading in, it settles from standing on the bottom into that as the water deepens.
                    // Folk out swimming lie in the water, back just under the surface, bobbing; wading in or out they
                    // stand on the bottom (settling into the swim as the water deepens).
                    if (b.Upright && s.Mode == CreatureMode.Swim)
                    {
                        var g = Terrain.SampleGround(s.Position.xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                        {
                            var deep = math.smoothstep(b.StandHeight * s.Scale * 0.6f, b.StandHeight * s.Scale * 0.9f, g.WaterSurface - g.Height);
                            // (Low: the back a hand's breadth under, only the shoulders and the raised head out.)
                            var floatAt = g.WaterSurface - 0.24f * s.Scale + math.sin(s.Clock * 1.9f) * 0.02f * s.Scale;
                            s.Position.y = math.lerp(g.Height, floatAt, deep);
                        }
                    }
                    if (b.CrossesWater)
                    {
                        var g = Terrain.SampleGround(s.Position.xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                        {
                            var swim = SwimDepth(ref b, s.Scale, g.WaterSurface - g.Height);
                            var floatAt = g.WaterSurface - b.StandHeight * s.Scale - b.FrontRadius * b.BodyFlatten * s.Scale * 0.55f +
                                          math.sin(s.Clock * 2.3f) * 0.015f * s.Scale;
                            s.Position.y = math.lerp(g.Height, floatAt, swim);
                        }
                    }
                }
                else
                {
                    // Floating on the surface, or swimming a little under it (shallower in shallow water); a leap
                    // arcs up out of the water and back.
                    var g = Terrain.SampleGround(s.Position.xz);
                    var surface = g.HasWater ? g.WaterSurface : g.Height;
                    // (A lava dweller basks at the surface, its back and head out: lava hides anything below it.)
                    var y = b.Habitat == Habitat.Floats ? surface + b.StandHeight * 0.35f * s.Scale
                          : b.LavaDweller ? surface + b.FrontRadius * s.Scale * 0.4f
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
                SampleMarker.End();
                using (PhaseMarker.Auto())
                    CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>
            /// Whether buildings' walls stop it: anything walking (not a ghost, nor a bird on the wing or a climber),
            /// except in the last couple of metres to where it's going (a work spot at a counter, a door).
            /// </summary>
            bool WalksRoundWalls(in CreatureState s, ref CreatureBlob b, bool keepHeight) =>
                HasFlora && !b.Haunts && !keepHeight && s.Mode != CreatureMode.Held &&
                (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies && s.Fly < 0.5f) &&
                math.distancesq(s.Position.xz, s.Target) > 4f;

            bool Walled(float2 at, float radius) => FloraPlacement.Blocked(ref Flora.Value, Terrain, at, radius);

            bool CaveWalkerNot(in CreatureState s, ref CreatureBlob b) => !CaveWalker(s, ref b);

            /// <summary>
            /// Crawls over the cave's rock in three dimensions: straight for its target (Target, TargetHeight), then
            /// back onto the surface it's on (its feet on the rock, its body off it), so it goes up walls and across the
            /// ceiling as readily as along the floor.
            /// </summary>
            void Crawl(ref CreatureState s, ref CreatureBlob b, float dt)
            {
                var target = new float3(s.Target.x, s.TargetHeight, s.Target.y);
                var way = target - s.Position;
                var dir = math.normalizesafe(way);
                var p = s.Position + dir * math.min(s.Speed * dt, math.length(way));
                // Onto the rock: the distance field is the way to it (its slope points into rock, its value how far).
                const float e = 0.1f;
                for (int k = 0; k < 2; k++)
                {
                    var d = Terrain.CaveDistance(p);
                    var grad = math.normalizesafe(new float3(
                        Terrain.CaveDistance(p + new float3(e, 0f, 0f)) - Terrain.CaveDistance(p - new float3(e, 0f, 0f)),
                        Terrain.CaveDistance(p + new float3(0f, e, 0f)) - Terrain.CaveDistance(p - new float3(0f, e, 0f)),
                        Terrain.CaveDistance(p + new float3(0f, 0f, e)) - Terrain.CaveDistance(p - new float3(0f, 0f, e))));
                    p -= grad * d;
                }
                s.Position = p;
                if (math.lengthsq(dir.xz) > 0.01f)
                    s.Yaw = math.atan2(dir.x, dir.z);
                CreatureMotion.AdvancePhase(ref s, ref b, dt);
            }

            /// <summary>
            /// A point on the cave's rock a few metres off in a random direction (up a wall, across the ceiling, along
            /// the floor): marched out from the body until it meets rock.
            /// </summary>
            bool CrawlTarget(in CreatureState s, ref CreatureBlob b, ref Random rng, out float3 spot)
            {
                spot = default;
                var from = s.Position + CreatureMotion.Surface(Terrain, s.Position, out _, out _) * 0.3f;
                var dir = rng.NextFloat3Direction();
                for (float t = 0.5f; t < 8f; t += 0.25f)
                {
                    var p = from + dir * t;
                    if (Terrain.CaveDistance(p) >= 0f)
                    {
                        spot = p - dir * 0.05f;
                        return t > 1f;
                    }
                }
                return false;
            }

            /// <summary>The steepest of a few places round a point (a rock face to bask on), if any is a real slope.</summary>
            bool SteepestOf(float2 near, float radius, ref Random rng, out float2 best)
            {
                best = near;
                var flattest = 0.85f;
                for (int k = 0; k < 5; k++)
                {
                    var p = near + rng.NextFloat2Direction() * radius * rng.NextFloat(0.2f, 0.8f);
                    if (!OpenGround(p))
                        continue;
                    var n = Terrain.SampleNormal(p, 0.5f);
                    if (n.y < flattest)
                    {
                        flattest = n.y;
                        best = p;
                    }
                }
                return flattest < 0.85f;
            }

            /// <summary>
            /// Moves as a cave animal does (on the cave floor, along its walls): cave dwellers always, and a surface animal
            /// that's sometimes found in caves (CaveAffinity) while it's under rock.
            /// </summary>
            bool CaveWalker(in CreatureState s, ref CreatureBlob b) => b.CaveDweller || underRock;

            /// <summary>This tick's creature is a cave visitor (CaveAffinity) under rock (see Execute).</summary>
            bool underRock;

            bool UnderRock(in CreatureState s, ref CreatureBlob b)
            {
                if (b.CaveDweller || b.CaveAffinity <= 0f || !Terrain.Caves.IsCreated)
                    return false;
                // (Up on the surface, as it nearly always is: one sample, and no march down the cave's field.)
                if (s.Position.y >= Terrain.SampleHeight(s.Position.xz) - 1f)
                    return false;
                Terrain.FloorUnder(s.Position + new float3(0f, 0.5f, 0f), out var inCave);
                return inCave;
            }

            /// <summary>
            /// Land on the far side of water within 60 m, in one of a few directions tried: the way there starts on
            /// land, crosses water it can swim, and ends on dry land.
            /// </summary>
            bool FarShore(float2 from, ref Random rng, out float2 shore)
            {
                shore = default;
                var start = rng.NextFloat(math.PI * 2f);
                for (int k = 0; k < 6; k++)
                {
                    var a = start + k * math.PI / 3f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    var wet = false;
                    for (var d = 4f; d <= 60f; d += 4f)
                    {
                        var p = from + dir * d;
                        if (Swimmable(p))
                            wet = true;
                        else if (Walkable(p, Habitat.Land))
                        {
                            if (!wet)
                                continue;
                            shore = p + dir * 3f;
                            return Walkable(shore, Habitat.Land);
                        }
                        else
                            break;   // a cave hole, lava, too shallow to count: not this way
                    }
                }
                return false;
            }

            /// <summary>How far a land animal that swims has gone from wading (0) to swimming (1) at this depth.</summary>
            static float SwimDepth(ref CreatureBlob b, float scale, float depth) =>
                math.smoothstep(b.StandHeight * scale * 0.6f, b.StandHeight * scale * 1.1f, depth);

            /// <summary>The nearest dry land within 80 m, looking out in 12 directions.</summary>
            bool NearestShore(float2 from, out float2 shore)
            {
                shore = default;
                var best = float.MaxValue;
                for (int k = 0; k < 12; k++)
                {
                    var a = k * math.PI / 6f;
                    var dir = new float2(math.sin(a), math.cos(a));
                    for (var d = 3f; d <= 80f && d < best; d += 3f)
                        if (Walkable(from + dir * d, Habitat.Land))
                        {
                            best = d;
                            shore = from + dir * (d + 2f);
                            break;
                        }
                }
                return best < float.MaxValue;
            }

            /// <summary>Lava deep enough to swim in (a volcano's crater lake, a lava biome's pools).</summary>
            bool InLava(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater && g.WaterSurface - g.Height > 0.5f &&
                       Terrain.LavaAt(new float3(xz.x, g.WaterSurface - 0.2f, xz.y)) > 0.5f;
            }

            /// <summary>How deep the water is here (0 on dry land).</summary>
            float WaterDepth(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater ? math.max(0f, g.WaterSurface - g.Height) : 0f;
            }

            /// <summary>Water a land animal that swims can cross: open water (not a cave's hole, not lava).</summary>
            bool Swimmable(float2 xz)
            {
                var g = Terrain.SampleGround(xz);
                return g.HasWater && g.WaterSurface > g.Height && !Terrain.IsCaveHole(xz) &&
                       Terrain.LavaAt(new float3(xz.x, g.WaterSurface - 0.2f, xz.y)) < 0.3f;
            }

            /// <summary>Whether an animal of this habitat can go here: dry land, or water deep enough to swim or float in.</summary>
            /// <summary>Somewhere to go on foot or to land: walkable ground that isn't inside a building's walls.</summary>
            bool OpenGround(float2 xz) => Walkable(xz, Habitat.Land) && !(HasFlora && Walled(xz, 0.3f));

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
