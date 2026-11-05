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
        /// <summary>#12 A/B switch for one build: 1 (DebugControls' -noflaresettle) steers the flare the old way, wheeling
        /// round to face the landing spot at the full turn rate.</summary>
        /// <summary>#46 tests ("-flatfear"): fear by plain 3D distance, as before the height weighting.</summary>
        public static readonly Unity.Burst.SharedStatic<int> HeightFearOff = Unity.Burst.SharedStatic<int>.GetOrCreate<HeightFearKey>();
        struct HeightFearKey { }
        const float HeightFear = 2.5f;
        /// <summary>#12 A/B switch ("-oldwander"): unhurried walkers ease their speed down while turning, as before A-46.</summary>
        public static readonly Unity.Burst.SharedStatic<int> OldWander = Unity.Burst.SharedStatic<int>.GetOrCreate<OldWanderKey>();
        struct OldWanderKey { }
        /// <summary>#12 A/B switch ("-oldplay"): a playing runner re-aims freely every frame, as before A-46b.</summary>
        public static readonly Unity.Burst.SharedStatic<int> OldPlay = Unity.Burst.SharedStatic<int>.GetOrCreate<OldPlayKey>();
        struct OldPlayKey { }
        public static readonly Unity.Burst.SharedStatic<int> FlareSettleOff = Unity.Burst.SharedStatic<int>.GetOrCreate<FlareSettleKey>();
        struct FlareSettleKey { }
        /// <summary>A-26 tests ("-chattest"): folk stop to talk far more often, from further apart, cooldowns short.</summary>
        public static readonly Unity.Burst.SharedStatic<int> ForceChat = Unity.Burst.SharedStatic<int>.GetOrCreate<ForceChatKey>();
        struct ForceChatKey { }
        /// <summary>#57 A/B switch ("-oldsearch"): predator, alarm and asked-to-chat searches every frame, as before the stagger.</summary>
        public static readonly Unity.Burst.SharedStatic<int> SearchEveryFrame = Unity.Burst.SharedStatic<int>.GetOrCreate<SearchEveryFrameKey>();
        struct SearchEveryFrameKey { }
        /// <summary>
        /// #57 counters for -perflog (Interlocked from the brain's workers): [0] predator searches, [1] alarm searches,
        /// [2] asked-to-chat searches, [3] flee started by a predator, [4] flee started by an alarm.
        /// </summary>
        public static readonly Unity.Burst.SharedStatic<SearchCounts> Searches = Unity.Burst.SharedStatic<SearchCounts>.GetOrCreate<SearchCountsKey2>();
        // (Key renamed when the struct grew in A-47: a SharedStatic keeps its first size until the editor restarts.)
        struct SearchCountsKey2 { }
        public struct SearchCounts { public long Predator, Alarm, Chat, PredatorFlee, AlarmFlee, PlayerFlee, Thinks, LodSkips; }
        /// <summary>#59 A/B switch ("-nobrainlod"): every creature thinks every frame, however far from a player.</summary>
        public static readonly Unity.Burst.SharedStatic<int> BrainLodOff = Unity.Burst.SharedStatic<int>.GetOrCreate<BrainLodOffKey>();
        struct BrainLodOffKey { }
        /// <summary>#59 brain LOD: beyond this (m) from every player a creature thinks once every BrainLodFrames frames,
        /// phased by its Individual so the load stays even; in between a walker on open ground only coasts on.
        /// (A-54: 80 m and every 4th frame, was 60 m and every 3rd; "-brainlod 60,3" sets them for an A/B.)</summary>
        public static readonly Unity.Burst.SharedStatic<float2> BrainLod = Unity.Burst.SharedStatic<float2>.GetOrCreate<BrainLodKey>();
        struct BrainLodKey { }
        public static readonly float2 BrainLodDefault = new float2(80f, 4f);
        static float BrainLodRange => BrainLod.Data.x > 0f ? BrainLod.Data.x : BrainLodDefault.x;
        static uint BrainLodFrames => BrainLod.Data.y >= 1f ? (uint)BrainLod.Data.y : (uint)BrainLodDefault.y;
        /// <summary>#57: each creature looks for predators, alarms and someone asking it to chat once per this (s), phased
        /// by its Individual on the shared clock (the same on every machine); fleeing ones still look every frame.</summary>
        const float SearchPeriod = 0.2f;

        /// <summary>
        /// One of the townsfolk who could stop for a talk (A-26, widened in A-27, when a settled town of a few folk, most
        /// at their posts, had all but stopped talking): about the town, walking somewhere, standing idle (or fidgeting at
        /// an idle action), on its way to its post or a seat, or at a post in town (a smith or a vendor breaks off for a
        /// word, then goes back to it); not seated, fishing, on guard, reading a letter, or on a round of its own (miners,
        /// travellers, messengers, raids).
        /// </summary>
        internal static bool ChatFree(in CreatureState s, ref CreatureBlob b) =>
            b.Upright && b.Hands && !b.Haunts && s.Leash > 0f && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Watch &&
            s.Held != HeldItem.Letter && s.Gone == false &&
            (s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Idle && s.Stage < CreatureAnimator.MessageWait ||
             s.Mode == CreatureMode.Work && WalksToPost(s.Task) && (s.Stage == 0 || s.Stage == 1 && s.Task != CreatureTask.Fish && s.Task != CreatureTask.Hermit) ||
             s.Mode == CreatureMode.Sit && s.Stage == 0);

        /// <summary>A worker whose walk to work is a plain walk to its post (it can stop on the way and walk on after).</summary>
        static bool WalksToPost(CreatureTask task) =>
            task != CreatureTask.Mine && task != CreatureTask.Travel && task != CreatureTask.Messenger && task != CreatureTask.Critter;
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
        // #132: Advance's two wall checks (buildings' walls, and A-44's solids near a player), for -perflog.
        static readonly Unity.Profiling.ProfilerMarker WallsMarker = new Unity.Profiling.ProfilerMarker("Brain.Walls");
        static readonly Unity.Profiling.ProfilerMarker SolidsMarker = new Unity.Profiling.ProfilerMarker("Brain.Solids");
        static readonly Unity.Profiling.ProfilerMarker BuiltMarker = new Unity.Profiling.ProfilerMarker("Brain.Built");
        static readonly Unity.Profiling.ProfilerMarker EaseMarker = new Unity.Profiling.ProfilerMarker("Brain.EaseGait");
        /// <summary>The whole of one creature's tick (#59: DebugControls' -perflog sums it per frame as "brain ms").</summary>
        public static readonly Unity.Profiling.ProfilerMarker TickMarker = new Unity.Profiling.ProfilerMarker("Brain.Tick");
        EntityQuery creatures;
        /// <summary>Where each player was last frame (by Holder id), for how fast they're going.</summary>
        NativeHashMap<int, float3> lastPlayerPositions;
        /// <summary>Frames run, for the brain LOD's turns (#59).</summary>
        uint frame;

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
                Now = (float)SystemAPI.Time.ElapsedTime,
                Step = SystemAPI.Time.DeltaTime,
                Frame = ++frame,
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
            /// <summary>The shared clock and this frame's raw step, for staggered searches (#57).</summary>
            public float Now, Step;
            /// <summary>This frame's number, for whose turn it is to think far off (#59).</summary>
            public uint Frame;
            [ReadOnly] public NativeArray<Entity> Entities;

            /// <summary>Whether this creature's staggered searches run this frame: its slot of SearchPeriod came round.</summary>
            bool SearchDue(in CreatureState s)
            {
                if (SearchEveryFrame.Data != 0)
                    return true;
                var phase = math.frac(math.hash(new uint2(s.Individual, 0x9E3779B9u)) * (1f / 4294967296f)) * SearchPeriod;
                return math.floor((Now + phase) / SearchPeriod) != math.floor((Now - Step + phase) / SearchPeriod);
            }
            [ReadOnly] public NativeArray<CreatureState> States;
            [ReadOnly] public NativeArray<CreatureBody> Bodies;
            [ReadOnly] public NativeHashMap<Entity, int> Lookup;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
            /// <summary>Snapshot indices of the hunters out after game now (FolkHunting stages).</summary>
            [ReadOnly] public NativeArray<int> Hunters;

            /// <summary>This creature's CreatureMind while it ticks (#58: a copy, written back after, so every helper
            /// reaches it without passing it along; the job struct is each worker's own, as with Dt and Step).</summary>
            CreatureMind M;

            void Execute(Entity self, ref CreatureState s, ref CreatureMind mind, in CreatureBody body)
            {
                M = mind;
                Tick(self, ref s, in body);
                mind = M;
            }

            void Tick(Entity self, ref CreatureState s, in CreatureBody body)
            {
                using var ticked = TickMarker.Auto();
                // #59 brain LOD: far from every player it thinks only on its turn, for the time since its last think; on
                // the frames between, a walker on open ground coasts along its heading (taken back before it next thinks,
                // so the think moves it the whole way) and anything else holds still.
                var frameDt = Dt;
                var frameStep = Step;
                var far = BrainLodOff.Data == 0 && !s.Gone && s.Mode != CreatureMode.Held && s.Mode != CreatureMode.Puppet;
                for (int p = 0; p < Players.Length && far; p++)
                    far = math.distancesq(Players[p].xz, s.Position.xz) > BrainLodRange * BrainLodRange;
                // (The phase is mixed first: math.hash alone doesn't spread its low bits well, and only those pick the frame.)
                var phase = math.hash(new uint2(s.Individual, 0x59u));
                phase ^= phase >> 16;
                phase *= 0x7FEB352Du;
                phase ^= phase >> 15;
                var thinks = !far || (Frame + phase) % BrainLodFrames == 0;
                if (!thinks)
                {
                    M.LodDebt += frameStep;
                    if (body.Blob.Value.Habitat == Habitat.Land && s.Speed > 0f && s.Fly < 0.01f &&
                        math.abs(s.Position.y - Terrain.SampleHeight(s.Position.xz)) < 0.3f)
                    {
                        var coast = new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * frameDt;
                        M.LodCoast += coast;
                        s.Position.xz += coast;
                        s.Position.y = CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                    }
                    System.Threading.Interlocked.Increment(ref Searches.Data.LodSkips);
                    return;
                }
                if (M.LodDebt > 0f)
                {
                    s.Position.xz -= M.LodCoast;
                    Step = frameStep + M.LodDebt;
                    Dt = math.min(Step, 0.3f);
                    M.LodDebt = 0f;
                    M.LodCoast = float2.zero;
                }
                System.Threading.Interlocked.Increment(ref Searches.Data.Thinks);
                Think(self, ref s, in body);
                Dt = frameDt;
                Step = frameStep;
                // A-44: one lying or standing still over a cave entrance (it rested there before the ground under it was
                // known to be carved) settles onto the carved ground; up a tree or on a perch it is above the surface.
                // (A-44b: at once, and moving too: easing down, or only when still, left 258 floating ticks a minute.)
                if (!s.Gone && nearSolids && s.Fly < 0.5f && s.Mode != CreatureMode.Held && !body.Blob.Value.CaveDweller &&
                    !underRock && s.Position.y < Terrain.SampleHeight(s.Position.xz) + 0.3f)
                {
                    var ground = CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                    if (s.Position.y - ground > 0.1f)
                        s.Position.y = ground;
                }
                if (!s.Gone && CreatureMotion.StatsOn.Data != 0)
                    CountSolids(s, ref body.Blob.Value);
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
                var nearest = 0;
                for (int p = 1; p < Players.Length; p++)
                    if (math.distancesq(Players[p].xz, s.Position.xz) < math.distancesq(player.xz, s.Position.xz))
                    {
                        player = Players[p];
                        emote = (IdleAction)PlayerPoses[p];
                        playerSpeed = PlayerSpeeds[p];
                        nearest = p;
                    }
                // (A-44: only animals a player could see keep out of solids; the rest are left as they were, for the cost.)
                nearSolids = CreatureMotion.SolidsOff.Data == 0 && !b.Upright && !b.Haunts && !b.Tunnels &&
                             (b.Habitat == Habitat.Land || b.Habitat == Habitat.Flies) &&
                             math.distancesq(player.xz, s.Position.xz) < CreatureMotion.SolidReach * CreatureMotion.SolidReach;
                // (A player in a body of its own kind is one of its own: not run from. A ghost doesn't flee a ghost.)
                var ownKind = nearest < PlayerSpecies.Length && PlayerSpecies[nearest] == body.Species && body.Species >= 0;
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
                // An animal a fox, wolf or the like has pounced on and caught (Z-53): gone (eaten).
                if (b.Habitat == Habitat.Land && b.Hunted && Pounced(self, in s))
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

                var away = s.Position.xz - player.xz;
                var playerDistance = math.distance(s.Position, player);   // (3D: high overhead is far)
                // Danger (Sense), what else is going on round it (React), and running from it.
                var threatened = Sense(self, ref s, ref b, body.Species, player, playerSpeed, emote, away, ownKind, aloft, perched, flier, tame, out var threat);
                React(self, ref s, ref b, body.Species, ref rng, player, emote, flier, perched, ref threatened, threat);
                if (threatened)
                    StartFleeing(ref s, ref b, ref rng, threat, flier);
                // How one of the folk takes the nearest player's body (Z-52): kin, a rival, wary, annoyed, warm or neither.
                var stance = b.Upright && b.Hands && !b.Haunts && nearest < PlayerSpecies.Length
                    ? b.StanceToward(PlayerSpecies[nearest], s.Individual) : FolkStance.Neutral;
                if (!threatened && emote != IdleAction.None && !b.Rooted)
                    Answer(ref s, ref b, ref rng, emote, player, playerDistance, dt, stance);
                if (!threatened && stance != FolkStance.Neutral)
                    Regarding(ref s, ref b, ref rng, stance, player, playerDistance, dt);

                if (b.Haunts && SleepTime(ref b) < 0.5f)
                    Haunting(ref s, ref b, ref rng, player, playerDistance, ownKind);

                Errands(self, ref s, ref b, ref rng, dt, threatened);

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
                        Idling(self, ref s, ref b, body.Species, ref rng, dt, walk, idleLook, perched, player, ref targetSpeed, ref moving, ref desiredLook);
                        break;

                    case CreatureMode.Wander:
                        Wandering(ref s, ref b, ref rng, dt, walk, flier, ref targetSpeed, ref moving, ref custom, ref vertical);
                        break;

                    case CreatureMode.Flee:
                        Fleeing(ref s, ref b, ref rng, dt, run, flier, threatened, ref targetSpeed, ref moving, ref custom, ref vertical);
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
                        Sitting(self, ref s, ref b, ref rng, dt, walk, idleLook, ref targetSpeed, ref moving, ref lie, ref sit, ref desiredLook);
                        break;

                    case CreatureMode.Swim:
                        Swimming(ref s, ref b, ref rng, dt, walk, idleLook, ref targetSpeed, ref moving, ref desiredLook);
                        break;

                    case CreatureMode.Patrol:
                        Patrol(self, ref s, ref b, ref rng, dt, walk, run, player, playerSpeed, emote, ref targetSpeed, ref moving, ref desiredLook);
                        break;

                    case CreatureMode.Work:
                        Working(ref s, ref b, ref rng, dt, walk, run, idleLook, ref targetSpeed, ref moving, ref custom, ref vertical, ref sit, ref desiredLook, ref headDown, ref bow);
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
                ChildPlay(ref s, ref b, ref rng, dt);
                KeepToLeash(ref s, moving, ref targetSpeed);

                var urgent = s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Hunt || s.Mode == CreatureMode.Play ||
                             s.Mode == CreatureMode.Burrow && s.ModeTime < 3f;
                if (!custom)
                    Steer(ref s, ref b, ref rng, dt, moving, urgent, targetSpeed, flier, perched, walk, run);
                else
                    CreatureMotion.AdvancePhase(ref s, ref b, dt);
                Posture(self, ref s, ref b, ref rng, dt, player, playerSpeed, emote, desiredLook, urgent, lie, headDown, bow, curl, sit, vertical);
                var fly = Flying(s) && (flier || b.Breaches && s.Mode == CreatureMode.Wander && s.Stage >= 2) ? 1f : 0f;
                CarriedItem(ref s, ref b);
                s.Fly += (fly - s.Fly) * math.min(1f, dt * 6f);
                s.Seed = rng.state;
            }

            /// <summary>Whether it is in danger this tick (the player, a hunter after it, a predator, a guard driving it off, the herd bolting), and from where.</summary>
            bool Sense(Entity self, ref CreatureState s, ref CreatureBlob b, int species, float3 player, float playerSpeed, IdleAction emote, float2 away, bool ownKind, bool aloft, bool perched, bool flier, float tame, out float2 threat)
            {
                // Danger: the player, or predators of this species (from further off when they're hunting it).
                // Asleep, it notices only at half the distance; perched birds let you a bit closer.
                var alert = (s.Mode == CreatureMode.Sleep ? 0.5f : 1f) * (perched ? 0.5f : 1f);
                // Someone walking up slowly (not running) is noticed half as far off; from behind or the side, only at
                // the last moment, so a small animal can be crept up on and caught.
                var facing = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
                // (A blind one goes by its ears: facing makes no odds, a slow step is heard only close by, and it hears
                // through rock as well as anyone. #10.)
                if (b.Blind)
                    alert *= playerSpeed < 2.5f ? 0.45f : 1f;
                // (Bat folk covering their eyes from a light (#35) see nothing: they stand and cover rather than bolt
                // from the lantern that dazzled them, and only someone running up, or a step right by them, startles them. A-68.)
                else if (s.Action == IdleAction.ShieldEyes)
                    alert *= playerSpeed < 2.5f ? 0.2f : 1f;
                else if (playerSpeed < 2.5f)
                    alert *= math.dot(facing, math.normalizesafe(-away)) < 0.3f ? 0.1f : 0.5f;
                // Danger is judged by real distance, so high overhead is far: a player flying over the herd doesn't
                // scatter it, and nor does someone walking on the ground over a cave. (On foot across level ground it's
                // much the same as the distance across.) A bird in the air keeps on with a hunt unless the player is
                // really close.
                // (#46: and the height gap counts for more than the ground between: a flyer hovering over the herd, 15 m up,
                // is further off than 15 m. Within 1.5 m of level it's the plain distance, so ground-level fear is as it was.)
                var gapUp = math.abs(s.Position.y - player.y);
                var heightWeight = HeightFearOff.Data == 0 && gapUp > 1.5f ? 1.5f + (gapUp - 1.5f) * HeightFear : gapUp;
                var threatDistance = math.length(new float3(s.Position.x - player.x, heightWeight, s.Position.z - player.z));
                var nerve = s.Mode == CreatureMode.Hunt && flier ? 0.4f : 1f;
                // Someone cheering or dancing about is noticed (and run from) further off; waving, a little. (Only by
                // the skittish: a curious one comes to look, and some pay it no heed.)
                var commotion = b.Reaction != EmoteReaction.Flees ? 1f :
                                emote == IdleAction.Cheer || emote == IdleAction.Dance ? 1.8f : emote == IdleAction.Wave ? 1.25f : 1f;
                var threatened = !aloft && !ownKind && b.FleeDistance > 0f && threatDistance < b.FleeDistance * s.Scale * alert * nerve * tame * commotion;
                threat = player.xz;
                if (threatened && s.Mode != CreatureMode.Flee)
                    System.Threading.Interlocked.Increment(ref Searches.Data.PlayerFlee);
                // A hunter after it: heard creeping close, or seen drawing back to throw (and the throw itself).
                if (!threatened && !aloft && b.FleeDistance > 0f && FindHunter(self, ref s, alert, out var hunter))
                {
                    threatened = true;
                    threat = hunter;
                }
                // (#57: staggered to once per SearchPeriod, phased per creature; one already fleeing keeps looking every frame.)
                var searchDue = SearchDue(in s);
                if (!threatened && !aloft && b.Hunted && (searchDue || s.Mode == CreatureMode.Flee) && FindPredator(self, ref s, species, alert, out var predator))
                {
                    threatened = true;
                    threat = predator;
                    if (s.Mode != CreatureMode.Flee)
                        System.Threading.Interlocked.Increment(ref Searches.Data.PredatorFlee);
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
                if (!threatened && !aloft && b.FleeDistance > 0f && s.Mode != CreatureMode.Flee && searchDue && FindAlarm(self, ref s, species, out var alarm))
                {
                    threatened = true;
                    threat = alarm;
                    System.Threading.Interlocked.Increment(ref Searches.Data.AlarmFlee);
                }
                return threatened;
            }

            /// <summary>What else it heeds round it: a flock taking off, a game of chase, its burrow, a startled roost, snapping, a town it is driven from.</summary>
            void React(Entity self, ref CreatureState s, ref CreatureBlob b, int species, ref Random rng, float3 player, IdleAction emote, bool flier, bool perched, ref bool threatened, float2 threat)
            {
                // Birds of a feather: when one takes off, the others near it go along to the same place.
                if (!threatened && flier && (s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Graze || s.Mode == CreatureMode.Rest) &&
                    FindTakeoff(self, ref s, species, out var leader))
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
                    M.Unsettled = rng.NextFloat(40f, 80f);
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
            }

            /// <summary>Threatened: it starts running from the threat (a flier up and away), or keeps on.</summary>
            void StartFleeing(ref CreatureState s, ref CreatureBlob b, ref Random rng, float2 threat, bool flier)
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

            /// <summary>A ghost abroad at night: gone from someone close, drifting nearer to watch someone in sight.</summary>
            void Haunting(ref CreatureState s, ref CreatureBlob b, ref Random rng, float3 player, float playerDistance, bool ownKind)
            {
                // (Not from a ghost: one of its own it lets come close.)
                if (playerDistance < 3.5f && !ownKind)
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

            /// <summary>Small errands between ticks: a swimmer making for shore, folk stopping to talk, meeting a messenger bird, a raider's cooldown.</summary>
            void Errands(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool threatened)
            {
                // Swimming with nowhere to go (it stopped, or wanted to graze or rest): it makes for the nearest shore.
                if (b.CrossesWater && !threatened && s.Mode != CreatureMode.Wander && s.Mode != CreatureMode.Flee &&
                    s.Mode != CreatureMode.Hunt && s.Mode != CreatureMode.Play && s.Mode != CreatureMode.Greet && s.Mode != CreatureMode.Held &&
                    WaterDepth(s.Position.xz) > b.StandHeight * s.Scale * 0.8f && NearestShore(s.Position.xz, out var ashore))
                {
                    SetMode(ref s, CreatureMode.Wander);
                    s.Target = ashore;
                }

                // Two folk stopped to talk (A-26): a talk something else broke off is over for this one; and now and then
                // two whose paths cross (or who stand near each other at a well or a seat) stop for one.
                s.ChatCool = math.max(s.ChatCool - dt, 0f);
                if (s.ChatRole != 0 && !(s.Mode == CreatureMode.Idle && (s.Stage == ChatTalk || s.Stage == ChatListen)))
                    s.ChatRole = 0;
                if (s.ChatRole == 0 && ChatFree(in s, ref b))
                    StartChat(self, ref s, ref b, ref rng, dt);

                // A messenger bird coming to this one: it stops what it's doing to meet it.
                if (b.Upright && b.Hands && s.Leash > 0f && (Free(s.Mode) || s.Mode == CreatureMode.Greet) && !(s.Mode == CreatureMode.Idle && s.Stage >= MessageWait) &&
                    rng.NextFloat() < 0.25f && FindMessenger(self, in s, out var messenger))
                {
                    SetMode(ref s, CreatureMode.Idle);
                    s.Stage = MessageWait;
                    s.Partner = messenger;
                    s.Timer = 40f;
                }

                // Bat folk (#35): a light coming near breaks off what it was doing, to cover its face at once
                // (DecideShield) rather than when its timer runs out.
                if (b.ShunsLight && s.Action != IdleAction.ShieldEyes && s.Held != HeldItem.Goggles &&
                    (Free(s.Mode) || s.Mode == CreatureMode.Work) && !(s.Mode == CreatureMode.Idle && s.Timer <= 0.6f))
                    // (Straight into the cover, not via Decide next tick: a moment idle and facing the light, it would
                    // see the player and bolt instead. A-68.)
                    DecideShield(ref s, ref b, ref rng);

                if (b.RaidsHives)
                    M.ChaseCooldown = math.max(M.ChaseCooldown - dt, 0f);
            }

            /// <summary>
            /// For bat folk (#35): a light near enough to dazzle it. Under the rock, a player within 6 m (their lantern
            /// lights by itself underground); out in the open, the day. Anywhere, a fire (a hearth or a camp's pit) or
            /// another creature's torch or lantern within FireGlare (A-68).
            /// </summary>
            bool LightNear(in CreatureState s, ref CreatureBlob b)
            {
                // (underRock is only worked out for cave visitors; bat folk are cave dwellers, so for them it's
                // whether they're below the ground here: at home, or not yet up on a surface trip. A-68.)
                var under = underRock || b.CaveDweller && s.Position.y < Terrain.SampleHeight(s.Position.xz) - 1f;
                if (!under && Night < 0.5f)
                    return true;
                if (under)
                    for (int p = 0; p < Players.Length; p++)
                        if (math.distancesq(Players[p], s.Position) < 36f)
                            return true;
                if (Terrain.Anchors.IsCreated)
                {
                    ref var set = ref Terrain.Anchors.Value;
                    var fires = new NativeList<int>(2, Allocator.Temp);
                    Anchors.Near(ref set, s.Position, FireGlare, AnchorKind.Hearth, ref fires);
                    Anchors.Near(ref set, s.Position, FireGlare, AnchorKind.Camp, ref fires);
                    if (fires.Length > 0)
                        return true;
                }
                var c = Cell(s.Position.xz);
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if ((o.Held == HeldItem.Torch || o.Held == HeldItem.Lantern) && !o.Gone &&
                            math.distancesq(o.Position, s.Position) < FireGlare * FireGlare)
                            return true;
                    }
                    while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }

            /// <summary>How near a fire or a carried flame dazzles a light shunner (m, A-68).</summary>
            const float FireGlare = 4f;

            /// <summary>CreatureMode.Idle: talking, reading a message, or standing about until it decides on something.</summary>
            void Idling(Entity self, ref CreatureState s, ref CreatureBlob b, int species, ref Random rng, float dt, float walk, float idleLook, bool perched, float3 player, ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                if (s.Stage == ChatTalk || s.Stage == ChatListen)
                {
                    if (Chatting(self, ref s, ref b, ref rng, dt))
                    {
                        targetSpeed = walk;
                        moving = true;
                    }
                    desiredLook = 0f;
                    return;
                }
                if (s.Stage >= MessageWait)
                {
                    Receiving(ref s, ref b, ref rng, dt);
                    return;
                }
                // An idle action under way finishes before it moves on.
                if (s.Action == IdleAction.None)
                    s.Timer -= dt;
                desiredLook = idleLook;
                if (s.Timer <= 0f)
                    Decide(self, ref s, ref b, species, ref rng, perched, player);
            }

            /// <summary>CreatureMode.Wander: a moth round its light, a flier in the air, or a walk to its target.</summary>
            void Wandering(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, bool flier, ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                // A moth round its light: from point to point (in a cave, under the rock: no cruising height).
                if (b.DrawnToLight)
                {
                    custom = true;
                    if (FlitTo(ref s, ref b, dt, ref vertical))
                        Flit(ref s, ref rng);
                    return;
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
                        return;
                    }
                    if (b.Breaches && s.Stage == 3)
                        return;
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
                    return;
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
            }

            /// <summary>CreatureMode.Flee: running (in a cave along the passages, jinking with something close behind), or flying off.</summary>
            void Fleeing(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float run, bool flier, bool threatened, ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical)
            {
                if (flier)
                {
                    custom = true;
                    if (FlyTo(ref s, ref b, s.Target, s.TargetHeight, b.FlySpeed * 1.2f, dt, ref vertical) ||
                        !threatened && s.ModeTime > 6f && s.Position.y - Terrain.SampleHeight(s.Position.xz) < 0.5f)
                        Land(ref s, ref b, ref rng);
                    return;
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
                    // (A-68: and it calms down like any other once nothing's after it; it used to run on for good.)
                    else if (!threatened && (s.ModeTime > 3f || math.distance(s.Position.xz, s.Target) < 1.5f))
                        Pause(ref s, ref b, ref rng);
                }
                else if (threatened && math.distance(s.Position.xz, s.Target) < 2f)
                    s.Target = s.Position.xz + math.normalizesafe(s.Position.xz - s.Focus, new float2(1f, 0f)) * 30f;
                // Prey with something close behind it jinks: every second or two a sharp turn of up to 50° off
                // straight away (Z-53: a chase that weaves, and that a hunter can close on).
                else if (threatened && b.Hunted && b.Habitat == Habitat.Land && math.distance(s.Position.xz, s.Focus) < 20f &&
                         s.StageTime > 1f + (s.Seed % 8u) * 0.125f)
                {
                    var fleeDir = math.normalizesafe(s.Position.xz - s.Focus, new float2(1f, 0f));
                    math.sincos(rng.NextFloat(-0.87f, 0.87f), out var jinkSin, out var jinkCos);
                    s.Target = s.Position.xz + new float2(fleeDir.x * jinkCos - fleeDir.y * jinkSin, fleeDir.x * jinkSin + fleeDir.y * jinkCos) * 30f;
                    s.StageTime = 0f;
                }
                else if (!threatened && (s.ModeTime > 3f || math.distance(s.Position.xz, s.Target) < 2f))
                    Pause(ref s, ref b, ref rng);
            }

            /// <summary>CreatureMode.Sit: walking to its spot, then sitting (or lying) there facing the way it chose.</summary>
            void Sitting(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float idleLook, ref float targetSpeed, ref bool moving, ref float lie, ref float sit, ref float desiredLook)
            {
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
            }

            /// <summary>CreatureMode.Work: a miner's, traveller's, messenger's or raider's round, or walking to its post and working there.</summary>
            void Working(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, float idleLook, ref float targetSpeed, ref bool moving, ref bool custom, ref float vertical, ref float sit, ref float desiredLook, ref float headDown, ref float bow)
            {
                // (A miner has no post: its day is a round, into the mine and back out with ore.)
                if (s.Task == CreatureTask.Mine)
                {
                    custom = true;
                    Mining(ref s, ref b, ref rng, dt, walk);
                    return;
                }
                if (s.Task == CreatureTask.Travel)
                {
                    custom = true;
                    Travelling(ref s, ref b, ref rng, dt, walk);
                    return;
                }
                if (s.Task == CreatureTask.Messenger)
                {
                    custom = true;
                    Messenger(ref s, ref b, ref rng, dt, ref vertical);
                    return;
                }
                if (s.Stage >= CreatureAnimator.RaidWalk && s.Stage <= CreatureAnimator.RaidLick)
                {
                    Raiding(ref s, ref b, ref rng, dt, walk, run, ref targetSpeed, ref moving, ref sit, ref vertical, ref desiredLook, ref headDown, ref bow);
                    return;
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
                        s.Stage = (byte)(s.Task == CreatureTask.Hermit && s.AnchorSide >= 2 ? s.AnchorSide : 1);
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
            }

            /// <summary>A child's poke with its stick or throw of its stone.</summary>
            void ChildPlay(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt)
            {
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
                    M.LookKind = 0;
                    M.LookAt = new float3(ahead.x, Terrain.SampleHeight(ahead), ahead.y);
                    s.LookFor = s.ActionLength + 0.5f;
                    s.Timer = math.max(s.Timer, s.ActionLength + 0.5f);
                }
            }

            /// <summary>Keeps the target inside its home range.</summary>
            void KeepToLeash(ref CreatureState s, bool moving, ref float targetSpeed)
            {
                // Animals with a home never head out of range of it (penned animals stay in their pen, even fleeing).
                // Going indoors is going home: a door beyond the leash would otherwise be cut short to a spot it stands
                // on for a minute, turning on it (Z-07, an elf by the ElfGrove fountain).
                if (s.Leash > 0f && moving && s.Mode != CreatureMode.Patrol && s.Mode != CreatureMode.Swim && s.Mode != CreatureMode.Home)
                {
                    var fromHome = s.Target - s.Home;
                    var far = math.length(fromHome);
                    if (far > s.Leash)
                        s.Target = s.Home + fromHome / far * s.Leash;
                    if (math.distance(s.Position.xz, s.Target) < 0.8f && s.Mode == CreatureMode.Flee)
                        targetSpeed *= 0.2f;   // cornered: mill about rather than run on the spot
                }
            }

            /// <summary>Turning and moving by itself (not climbing, flying or the like): turn to the target, keep off water, cliffs and solids, then Advance.</summary>
            void Steer(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, bool moving, bool urgent, float targetSpeed, bool flier, bool perched, float walk, float run)
            {
                // Getting up comes before going anywhere.
                targetSpeed *= math.saturate(1f - math.max(s.Lie, s.Sit) * 1.5f);

                // Turn towards the target, then move; slow down while turning sharply.
                var turn = 0f;
                // (Not within 10 cm of the target: the way to it there is noise, and following it spins the body on
                // the spot at its full turn rate, Z-07.)
                var to = s.Target - s.Position.xz;
                if (moving && math.lengthsq(to) > 0.01f)
                {
                    var wanted = WrapAngle(math.atan2(to.x, to.y) - s.Yaw);
                    turn = math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt);
                    targetSpeed *= math.saturate(math.cos(wanted) * 1.5f);
                    // (#12, A-46: an unhurried walker slows at once to a speed whose turning circle reaches its target,
                    // radius = distance / (2 sin off-heading). Easing down from walking pace instead, a dormouse or a
                    // songbird near its spot went round and round it at full turn rate.)
                    if (!urgent && OldWander.Data == 0 && math.abs(wanted) > 0.2f)
                    {
                        var reach = b.TurnRate * math.length(to) / (2f * math.sin(math.min(math.abs(wanted), math.PI * 0.5f)));
                        targetSpeed = math.min(targetSpeed, reach);
                        s.Speed = math.min(s.Speed, reach);
                    }
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
                // A-44: near a player, a wild animal turns from a solid just ahead (a cave wall, a tower, an iceberg)
                // before it presses into it, and from a boulder or shard taller than it steps (that one checked four
                // times a second: a rock's placement is a whole cell's worth of work).
                if (nearSolids && landBound && !blocked && s.Speed > 0.05f && s.Fly < 0.5f && !underRock && !b.CaveDweller)
                {
                    var probe = s.Position.xz + forward * (math.max(0.6f, s.Speed * 0.3f) * s.Scale + BodyRadius(s, ref b));
                    blocked = SolidAt(s, ref b, probe, forward, out _);
                    if (!blocked && HasFlora && (int)(s.Clock * 4f) != (int)((s.Clock - dt) * 4f) &&
                        FloraPlacement.RockAt(ref Flora.Value, Terrain, probe, BodyRadius(s, ref b), out var rockTop) &&
                        rockTop > math.max(0.3f, b.StandHeight * s.Scale * 0.6f))
                        blocked = true;
                }
                // (In a cave, running or going somewhere along the passage, it follows the wall round: Advance.)
                var followsWall =CaveWalker(s, ref b) && (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Drink ||
                                                    s.Mode == CreatureMode.Wander && math.distance(s.Position.xz, s.Target) > 2f);
                if (!followsWall && (blocked || landBound && s.Speed > 0.05f && SlopeAhead(s.Position.xz, ahead) > 0.8f))
                {
                    s.Speed *= 0.5f;
                    if (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Play && s.Stage == 1)
                    {
                        // Running, it veers along whatever's in the way: to the side its run leans to (head-on, its
                        // own side), the other side if that's blocked too, or back the way it came if both are.
                        // Never a coin toss: a fresh one each frame left it turning on the spot (Z-07, quail
                        // fleeing on a slope, playing prairie dogs).
                        var left = new float2(-forward.y, forward.x);
                        var lean = math.dot(s.Target - s.Position.xz, left);
                        var side = math.abs(lean) > 0.5f ? math.sign(lean) : (s.Seed & 1u) == 0u ? 1f : -1f;
                        var way = left * side;
                        var reach = 1.5f * s.Scale + 0.5f;
                        if (!CaveWalker(s, ref b) && !b.Haunts && !b.LavaDweller && s.Mode != CreatureMode.Swim &&
                            (!Open(s.Position.xz, s.Position.xz + way * reach, b.Habitat, landBound) ||
                             nearSolids && SolidAt(s, ref b, s.Position.xz + way * reach, way, out _)))
                            way = Open(s.Position.xz, s.Position.xz - way * reach, b.Habitat, landBound) &&
                                  !(nearSolids && SolidAt(s, ref b, s.Position.xz - way * reach, -way, out _)) ? -way : -forward;
                        s.Target = s.Position.xz + way * 20f;
                    }
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

            /// <summary>Where it looks and how it holds itself, eased towards this tick's wishes.</summary>
            void Posture(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float3 player, float playerSpeed, IdleAction emote, float desiredLook, bool urgent, float lie, float headDown, float bow, float curl, float sit, float vertical)
            {
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
            }

            /// <summary>What it has in its hands: lanterns after dark, tools at work, a child's plaything.</summary>
            void CarriedItem(ref CreatureState s, ref CreatureBlob b)
            {
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
                // A blind cave pygmy needs no light: up and about, most feel their way with a staff (the rest by hand).
                // Bat folk never carry a light (#35): goggles out on the surface by day (none underground, none at night),
                // else their own tool, a staff or a stick or empty hands (by a hash of the individual), put down to sleep.
                else if (b.ShunsLight && b.Hands)
                {
                    var tool = ((s.Individual >> 9) % 3u) switch { 0u => HeldItem.Staff, 1u => HeldItem.Stick, _ => HeldItem.None };
                    s.Held = !underRock && Night < 0.5f ? HeldItem.Goggles
                           : s.Mode != CreatureMode.Sleep && s.Mode != CreatureMode.Rest ? tool : HeldItem.None;
                }
                else if (b.Blind && b.Hands)
                    s.Held = ((s.Individual >> 7) & 3u) != 0 && s.Mode != CreatureMode.Sleep && s.Mode != CreatureMode.Rest ? HeldItem.Staff : HeldItem.None;
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
            }
        }
    }
}
