using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    /// <summary>
    /// Development helpers for looking at the world under different conditions, with a small on-screen readout:
    ///  N / Shift+N  jump to the next / previous preset location (one deep in each biome, a summit, a lake shore,
    ///               a river, a beach, and one of each landmark type; found automatically from the terrain);
    ///  F5-F8        warp to the next biome region / settlement / cave entrance / landmark on the island, nearest
    ///               first (Shift goes back; see Warp);
    ///  B            next day type (sunny, partly cloudy, changeable, overcast, stormy), replacing today's weather;
    ///  Y            advance the clock two hours;  T  toggle fast time (DayNightCycle);
    ///  ] / [        force the next weather over the player / clear it (WeatherSystem);
    ///  F1           show / hide this readout (off by default since A-51: about 0.5 ms of IMGUI a frame; "-hud" starts
    ///               with it on). "-nohud" hides the interaction prompt too, for screenshots.
    /// The player-facing panels (multiplayer, graphics, quit) are in the main menu on Esc or Start (UI.MainMenu).
    /// </summary>
    public class DebugControls : MonoBehaviour
    {
        public WeatherSystem Weather;
        public DayNightCycle Cycle;
        public bool ShowReadout;
        /// <summary>"-nohud": the interaction prompt hidden as well as the readout (screenshots).</summary>
        bool noHud;

        struct Location
        {
            public string Name;
            public float2 Position;
            public float Yaw;
        }

        readonly List<Location> locations = new List<Location>();
        /// <summary>Landmarks' middles (for -place, not in the N list).</summary>
        readonly List<Location> centres = new List<Location>();
        /// <summary>
        /// A-74: the last "-place Landmark" or "-place Landmark:centre" that found nothing, tried again each time the island's
        /// landmarks change until it times out. The launch options run on the first island (the baked one), and fishing villages and the other
        /// types the v2/v3 pass appends only exist after it, so "-place FishingVillage:centre" found nothing and stayed put.
        /// </summary>
        string landmarkPlacePending;
        float3 landmarkPlaceKey;
        float landmarkPlaceUntil;
        bool landmarkPlaced;
        int current = -1;
        EntityQuery playerQuery, terrainQuery, paletteQuery, floraQuery;
        World queryWorld;
        GUIStyle style, hintStyle;
        /// <summary>The control hints, under the readout: prompt text with button tokens (UI.Prompts draws their icons).</summary>
        string hints;
        string readout;
        Vector2 readoutSize;
        float nextReadout;

        float solidStatsSince = -1f;
        int solidStatsFrames;

        /// <summary>
        /// "-solidstats" (A-44): every 60 s, the wild animals near the player that minute: creature-ticks sampled, wall
        /// contacts (steps turned or stopped by a solid), ticks boxed in, ticks with a body over 0.15 m in a solid, the
        /// deepest sink, ticks floating over carved ground, solid queries per frame; and what those queries cost, timed
        /// here on the main thread unburst for the animals within reach, scaled to 50 (an upper bound: the brain runs
        /// them Burst-compiled, spread over the workers).
        /// </summary>
        // A-69 "-rockwatch SECONDS": once a second, every cave dweller within 60 m of the player whose body (half a metre
        // up from its feet) is inside rock, under the cave roof or the ground; logged per check and in total at the end.
        static float rockWatchFor, rockWatchSince = -1f, rockWatchNext, batFolkPlaceUntil;
        static int rockWatchChecks, rockWatchHits, rockWatchSeen;
        static readonly HashSet<uint> rockWatchStuck = new HashSet<uint>();

        void RockWatch()
        {
            if (rockWatchFor <= 0f || queryWorld == null || !queryWorld.IsCreated || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            if (rockWatchSince < 0f)
            {
                rockWatchSince = rockWatchNext = Time.unscaledTime;
                rockWatchChecks = rockWatchHits = rockWatchSeen = 0;
                rockWatchStuck.Clear();
            }
            if (Time.unscaledTime < rockWatchNext)
                return;
            rockWatchNext += 1f;
            var eye = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            int near = 0, inRock = 0;
            for (int i = 0; i < states.Length; i++)
            {
                var st = states[i];
                ref var b = ref bodies[i].Blob.Value;
                if (st.Gone || !b.CaveDweller || b.Crawls || b.Habitat == Habitat.Flies || math.distance(eye, st.Position) > 60f)
                    continue;
                near++;
                var body = st.Position + new float3(0f, 0.5f * math.max(st.Scale, 0.5f), 0f);
                var solid = math.min(terrain.CaveDistance(body), terrain.SampleHeight(body.xz) - body.y);
                if (solid > 0.05f)
                {
                    inRock++;
                    if (rockWatchStuck.Add(st.Individual))
                        Debug.Log($"[rockwatch] in rock: {(b.ShunsLight ? "bat folk" : b.Blind ? "pygmy" : "cave dweller")} #{st.Individual} at {st.Position} ({solid:F2} m in), mode {st.Mode} stage {st.Stage}");
                }
            }
            states.Dispose();
            bodies.Dispose();
            rockWatchChecks++;
            rockWatchHits += inRock;
            rockWatchSeen = math.max(rockWatchSeen, near);
            if (rockWatchChecks % 30 == 0)
                Debug.Log($"[rockwatch] {rockWatchChecks} s: {near} cave dwellers near, {inRock} in rock now, {rockWatchHits} in-rock samples so far");
            if (Time.unscaledTime - rockWatchSince >= rockWatchFor)
            {
                Debug.Log($"[rockwatch] DONE {rockWatchChecks} checks over {rockWatchFor:F0} s: up to {rockWatchSeen} cave dwellers near, {rockWatchHits} in-rock samples, {rockWatchStuck.Count} creatures ever in rock");
                rockWatchFor = 0f;
            }
        }

        void SolidStatsLog()
        {
            if (CreatureMotion.StatsOn.Data == 0 || queryWorld == null || !queryWorld.IsCreated)
                return;
            solidStatsFrames++;
            if (solidStatsSince < 0f)
                solidStatsSince = Time.unscaledTime;
            var span = Time.unscaledTime - solidStatsSince;
            if (span < 60f || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            var stats = CreatureMotion.Stats.Data;
            CreatureMotion.Stats.Data = default;
            var frames = solidStatsFrames;
            solidStatsFrames = 0;
            solidStatsSince = Time.unscaledTime;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var eye = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;
            var queriesPerTick = frames > 0 && stats.Samples > 0 ? (float)stats.Queries / stats.Samples : 2f;
            var bodyList = new NativeList<float4>(64, Allocator.TempJob);
            var shapeList = new NativeList<float4>(64, Allocator.TempJob);
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                ref var b = ref bodies[i].Blob.Value;
                if (s.Gone || b.Upright || b.Haunts || b.Habitat != Habitat.Land && b.Habitat != Habitat.Flies ||
                    math.distancesq(eye.xz, s.Position.xz) > CreatureMotion.SolidReach * CreatureMotion.SolidReach)
                    continue;
                var radius = math.max(math.max(b.FrontRadius, b.RearRadius) * s.Scale, 0.1f);
                bodyList.Add(new float4(s.Position, s.Yaw));
                shapeList.Add(new float4(radius, b.BodyLength * 0.5f * s.Scale, math.max((b.StandHeight + b.FrontRadius) * s.Scale, radius * 2f), 0f));
            }
            var near = bodyList.Length;
            // (A-44b: the queries a frame's moves make, timed as the brain runs them, Burst-compiled, but on one thread.)
            var hasFlora = floraQuery.TryGetSingleton<FloraLookup>(out var flora) && flora.Blob.IsCreated;
            var job = new SolidCostJob
            {
                Terrain = terrain, Flora = hasFlora ? flora.Blob : default, HasFlora = hasFlora,
                Bodies = bodyList.AsArray(), Shapes = shapeList.AsArray(), PerTick = math.max(1, (int)math.round(queriesPerTick)),
            };
            job.Run(); // (warm: the first run compiles)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int r = 0; r < 10; r++)
                job.Run();
            var ms = (float)watch.Elapsed.TotalMilliseconds / 10f;
            bodyList.Dispose();
            shapeList.Dispose();
            var minutes = span / 60f;
            var cases = CreatureMotion.Cases.Data;
            CreatureMotion.Cases.Data = default;
            var sinks = new System.Text.StringBuilder();
            var floats = new System.Text.StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                var c = cases.Sink(i);
                if (c.z > 0f)
                    sinks.Append($" [{(CreatureMotion.SolidKind)(int)c.w} {c.z:F2} m at {c.x:F0},{c.y:F0}]");
                var f = cases.Float(i);
                if (f.z > 0f)
                    floats.Append($" [{(CreatureMode)((int)f.w % 100)}{(f.w >= 100f ? " moving" : "")} {f.z:F2} m at {f.x:F0},{f.y:F0}]");
            }
            Debug.Log($"CreatureSolids cases: sinks{sinks}; floating{floats}");
            Debug.Log($"CreatureSolids: {(CreatureMotion.SolidsOff.Data != 0 ? "OFF (before)" : "on")}, {span:F0} s, {frames} frames, " +
                      $"{stats.Samples} creature-ticks near, contacts {stats.Contacts / minutes:F1}/min, stuck {stats.Stuck} ticks " +
                      $"({stats.Stuck * span / math.max(1, frames):F1} s), hidden >0.15 m {stats.Hidden} ticks, max sink {stats.MaxSinkMm / 1000f:F2} m, " +
                      $"floating >0.2 m {stats.Floating} ticks, queries {(float)stats.Queries / math.max(1, frames):F1}/frame " +
                      $"({queriesPerTick:F2} per creature-tick); cost now {near} near: {ms:F3} ms Burst one thread, " +
                      $"{(near > 0 ? ms * 50f / near : 0f):F3} ms per frame at 50");
        }

        /// <summary>"-solidstats" cost (A-44b): each animal's solid queries for one frame (its moves' SolidAt, buildings
        /// included, and its ground), Burst-compiled as in the brain.</summary>
        [Unity.Burst.BurstCompile]
        struct SolidCostJob : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public BlobAssetReference<FloraBlob> Flora;
            public bool HasFlora;
            [ReadOnly] public NativeArray<float4> Bodies, Shapes;
            public int PerTick;

            public void Execute()
            {
                for (int i = 0; i < Bodies.Length; i++)
                {
                    var p = Bodies[i];
                    var shape = Shapes[i];
                    var forward = new float2(math.sin(p.w), math.cos(p.w));
                    for (int k = 0; k < PerTick; k++)
                    {
                        var xz = p.xz + forward * 0.1f * k;
                        CreatureMotion.Solid(Terrain, xz, forward, p.y, shape.x, shape.y, shape.z, out _);
                        if (HasFlora && !FloraPlacement.Blocked(ref Flora.Value, Terrain, xz, shape.x))
                            FloraPlacement.Blocked(ref Flora.Value, Terrain, xz + forward * shape.y, shape.x * 0.6f);
                    }
                    CreatureMotion.Ground(Terrain, p.xz, p.y);
                }
            }
        }

        void Update()
        {
            // A client's world is replaced when it loses the host: rebuild the cached queries before anything reads them
            // (A-47 #68: the shot list read the old world's playerQuery and threw a NullReferenceException).
            var liveWorld = World.DefaultGameObjectInjectionWorld;
            if (queryWorld != null && (queryWorld != liveWorld || !queryWorld.IsCreated))
            {
                if (liveWorld != null && liveWorld.IsCreated)
                    EnsureLocations(out _);
                else
                    queryWorld = null;
            }
            // (#61: on a -fixeddt line, the sim's state 60 s of game time in, whatever the frame rate.)
            if (fixedFrames >= 0 && ++fixedFrames == 1800)
            {
                var fs = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
                Debug.Log($"DebugControls: fixed 60 s '{CurrentShotName()}' flees predator {fs.PredatorFlee - fixedSearch0.PredatorFlee}, alarm {fs.AlarmFlee - fixedSearch0.AlarmFlee}, " +
                          $"player {fs.PlayerFlee - fixedSearch0.PlayerFlee}, {SimPrint()}");
            }
            ApplyLaunchOptions();
            if (laterOptions != null && launchOptionsApplied)
            {
                var line = laterOptions;
                laterOptions = null;
                ApplyOptions(line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries));
            }
            SpawnRequested();
            PlaceByLedge();
            ClimbTest();
            ActionTest();
            Watch();
            SettleGoto();
            WalkTo();
            FlyCave();
            PlacePropPending();
            PlaceAtLandmarkPending();
            // (A-69: "-place batfolk" again until the bat folk homes are baked; they come after the island.)
            if (batFolkPlaceUntil > 0f && Time.unscaledTime < batFolkPlaceUntil && EnsureLocations(out var batRetry) && PlaceAtBatFolk(batRetry))
            {
                batFolkPlaceUntil = 0f;
                rockWatchSince = -1f; // (-rockwatch counts from there.)
            }
            RockWatch();
            Pose();
            Interact();
            ShotList();
            SolidStatsLog();
            Lift();
            CallLog();
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f1Key.wasPressedThisFrame)
                ShowReadout = !ShowReadout;
            // (The interaction prompt stays up with the readout off, its default; only "-nohud" hides it.)
            InteractionPrompt.Hidden = noHud;
            if (keyboard.f3Key.wasPressedThisFrame)
                CopyPosition();
            // F4 (#131): every fire in the standard fire's next colour, then back to each its own.
            if (keyboard.f4Key.wasPressedThisFrame)
            {
                var colours = Punchy.Ambience.FireSystem.Effect != null ? Punchy.Ambience.FireSystem.Effect.Colours.Length : 0;
                Punchy.Ambience.FireSystem.ColourOverride = Punchy.Ambience.FireSystem.ColourOverride + 1 >= colours ? -1 : Punchy.Ambience.FireSystem.ColourOverride + 1;
            }
            if (keyboard.bKey.wasPressedThisFrame && Weather != null)
                Weather.NextDayType();
            if (keyboard.yKey.wasPressedThisFrame && Cycle != null)
                Cycle.TimeOfDay = Mathf.Repeat(Cycle.TimeOfDay + 2f, 24f);
            if (keyboard.nKey.wasPressedThisFrame)
                Jump(keyboard.shiftKey.isPressed ? -1 : 1);
            for (int c = 0; c < WarpKeys.Length; c++)
                if (keyboard[WarpKeys[c]].wasPressedThisFrame)
                    Warp(c, keyboard.shiftKey.isPressed ? -1 : 1);
        }

        /// <summary>"-thirsty": tops up every simulated creature's thirst each second (shots of drinking at water).</summary>
        static bool thirsty;
        float thirstyNext;

        void Thirsty()
        {
            if (!thirsty || Time.time < thirstyNext || !QueriesLive())
                return;
            thirstyNext = Time.time + 1f;
            var em = queryWorld.EntityManager;
            if (creatureWorld != queryWorld)
            {
                creatureWorld = queryWorld;
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            }
            var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var e in entities)
            {
                if (!em.HasComponent<CreatureMind>(e))
                    continue;
                var mind = em.GetComponentData<CreatureMind>(e);
                mind.Thirst = 1f;
                em.SetComponentData(e, mind);
            }
            entities.Dispose();
        }

        void LateUpdate()
        {
            Thirsty();
            FootDrift();
            SpinCheck();
            HuntLog();
            PersonalityLog();
            ComfortLog();
            ShotOnHunt();
            ChatShot();
            RemoteRoll();
            ColossusShot();
            WalkerShot();
            MantrapShot();
            MimicShot();
        }

        // ---------------------------------------------------------------- A-09 shots

        static bool shotOnHunt;
        static string huntSpecies;

        /// <summary>"-gaitlog" (A-21, #6): each four-legged species among the loaded creatures and its gallop.</summary>
        void GaitLog()
        {
            if (queryWorld == null || !queryWorld.IsCreated)
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            using var q = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureBody>());
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var body in bodies)
            {
                ref var b = ref body.Blob.Value;
                if (!b.Gallops || !seen.Add(body.Species))
                    continue;
                Debug.Log($"DebugControls: gait {director?.SpeciesAt(body.Species)?.name}: {(b.LeapingRun ? "Leap" : b.Bounds ? "Bound" : "Rotary")}");
            }
            bodies.Dispose();
        }
        bool huntShotTaken;
        static float colossusShotDistance;
        static string mantrapShotMode;
        static bool mantrapLungePending;

        /// <summary>
        /// "-shotonhunt" on a shot-list line: the moment a land hunter's chase turns to a run, the camera is put 16 m to the
        /// side of the hunter and its nearest prey and the line's shot is brought forward to 0.3 s later (#9). The line's -wait
        /// is the longest it waits for a hunt.
        /// </summary>
        void ShotOnHunt()
        {
            if (!shotOnHunt || shotIndex < 0 || shotStarted < 0f || queryWorld == null || !queryWorld.IsCreated)
                return;
            if (huntShotTaken)
            {
                FollowHunter();
                return;
            }
            var em = queryWorld.EntityManager;
            // (A-23: the last line's hunter is skipped until its sprint ends, or a back-to-back line caught it slowing down
            // and shot it walking.)
            if (staleHunter != Entity.Null && (!em.Exists(staleHunter) || !em.HasComponent<CreatureState>(staleHunter) ||
                                               em.GetComponentData<CreatureState>(staleHunter) is var st && (st.Mode != CreatureMode.Hunt || st.Stage != 1)))
                staleHunter = Entity.Null;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var h = states[i];
                if (h.Mode != CreatureMode.Hunt || h.Stage != 1 || entities[i] == staleHunter || bodies[i].Blob.Value.Habitat == Habitat.Flies)
                    continue;
                if (huntSpecies != null && FindAnyObjectByType<FaunaDirector>()?.SpeciesAt(bodies[i].Species)?.name != huntSpecies)
                    continue;
                var prey = -1;
                var best = 60f;
                for (int j = 0; j < states.Length; j++)
                {
                    if (j == i || bodies[j].Species == bodies[i].Species)
                        continue;
                    var d = math.distance(states[j].Position, h.Position);
                    if (d < best) { best = d; prey = j; }
                }
                if (prey < 0)
                    continue;
                var p = states[prey].Position;
                var mid = (h.Position + p) * 0.5f;
                var along = math.normalizesafe(p - h.Position, new float3(0f, 0f, 1f));
                var side = math.normalizesafe(new float3(-along.z, 0f, along.x));
                var from = mid + side * (16f + best * 0.5f) + new float3(0f, 3.5f, 0f);
                PoseCamera(from, Quaternion.LookRotation((Vector3)(mid + new float3(0f, 0.8f, 0f)) - (Vector3)from));
                huntShotTaken = true;
                shotWait = Time.realtimeSinceStartup - shotStarted + (huntSide > 0f ? huntDelay : 0.3f);
                huntHunter = entities[i];
                huntPrey = entities[prey];
                huntSideDir = side;
                huntShotLine = shotIndex;
                if (huntSide > 0f)
                    FollowHunter();
                var director = FindAnyObjectByType<FaunaDirector>();
                Debug.Log($"DebugControls: shotonhunt: {director?.SpeciesAt(bodies[i].Species)?.name} sprinting at {director?.SpeciesAt(bodies[prey].Species)?.name}, {best:F1} m apart, hunter speed {h.Speed:F1}, at ({h.Position.x:F0}, {h.Position.z:F0})");
                break;
            }
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }

        // "-huntside 7" with -shotonhunt (#6): instead of the wide two-animal view, the camera rides that many metres
        // side-on of the hunter, and the shot is taken huntDelay seconds into the sprint (so the gallop has started).
        static float huntSide;
        static bool huntPair;
        const float huntDelay = 1f;
        Entity huntHunter, huntPrey, staleHunter;
        float3 huntSideDir;
        int huntShotLine = -1;
        bool huntShotLogged;

        void FollowHunter()
        {
            if (huntSide <= 0f || shotIndex != huntShotLine || huntHunter == Entity.Null)
                return;
            var em = queryWorld.EntityManager;
            if (!em.Exists(huntHunter) || !em.HasComponent<CreatureState>(huntHunter))
                return;
            var h = em.GetComponentData<CreatureState>(huntHunter);
            // Side-on to the hunter's own heading this frame (A-23: the hunter-to-prey line drifted off the heading as the
            // wolf curved after the hare, and the A-22 shot came out head-on), keeping the side picked at the start.
            var along = new float3(math.sin(h.Yaw), 0f, math.cos(h.Yaw));
            var side = math.normalizesafe(new float3(-along.z, 0f, along.x));
            if (math.dot(side, huntSideDir) < 0f)
                side = -side;
            var target = h.Position + new float3(0f, 0.6f, 0f);
            var distance = huntSide;
            var gap = 0f;
            if (huntPair && em.Exists(huntPrey) && em.HasComponent<CreatureState>(huntPrey))
            {
                var p = em.GetComponentData<CreatureState>(huntPrey).Position;
                gap = math.distance(p.xz, h.Position.xz);
                target = (h.Position + p) * 0.5f + new float3(0f, 0.5f, 0f);
                // Step back if the pair is wider than the view holds at this distance (about 1.6 d across).
                distance = math.max(huntSide, gap * 0.65f);
            }
            // (A-18: keep the side with a clear view. A shrub or tree between the camera and the hunter hid half the
            // wolf in A-17, so if this side has plants in the way and the other has fewer, swap over.)
            var blocked = SightBlockers(target, target + side * distance + new float3(0f, 0.9f, 0f));
            var otherBlocked = blocked > 0 ? SightBlockers(target, target - side * distance + new float3(0f, 0.9f, 0f)) : 0;
            if (blocked > 0 && otherBlocked < blocked)
            {
                side = -side;
                (blocked, otherBlocked) = (otherBlocked, blocked);
            }
            huntSideDir = side;
            var from = target + side * distance + new float3(0f, 0.9f, 0f);
            PoseCamera(from, Quaternion.LookRotation((Vector3)(target - from)));
            var elapsed = Time.realtimeSinceStartup - shotStarted;
            if (!huntShotLogged && elapsed >= shotWait - 0.05f)
            {
                huntShotLogged = true;
                Debug.Log($"DebugControls: huntside: hunter speed {h.Speed:F1}, mode {h.Mode} stage {h.Stage}, camera {distance:F1} m side-on of the heading{(huntPair ? $" (pair, {gap:F1} m apart)" : "")}, " +
                          $"prey line {PreyLineAngle(h, along):F0} deg off the heading, " +
                          $"{elapsed:F2} s into the line, plants in the way {blocked}, leap gather {CreatureAnimator.LeapGather(h.Phase):F2}");
            }
        }

        /// <summary>How far the hunter-to-prey line is turned from the hunter's heading, degrees (-1 with no prey).</summary>
        float PreyLineAngle(CreatureState h, float3 along)
        {
            var em = queryWorld.EntityManager;
            if (!em.Exists(huntPrey) || !em.HasComponent<CreatureState>(huntPrey))
                return -1f;
            var line = em.GetComponentData<CreatureState>(huntPrey).Position - h.Position;
            line.y = 0f;
            return math.degrees(math.acos(math.clamp(math.dot(math.normalizesafe(line), along), -1f, 1f)));
        }

        /// <summary>
        /// Shrubs and trees (and rocks) standing between two points: placed plants in the shrub and tree layers taller than
        /// half a metre whose footprint or crown comes within reach of the line on the ground (FloraPlacement.Place, the
        /// same pure function FloraSystem draws from). -1 if the flora tables aren't up yet.
        /// </summary>
        int SightBlockers(float3 a, float3 b)
        {
            if (!EnsureLocations(out var land))
                return -1;
            using var q = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Flora.FloraLookup));
            if (!q.TryGetSingleton<Punchy.Flora.FloraLookup>(out var lookup) || !lookup.Blob.IsCreated)
                return -1;
            ref var f = ref lookup.Blob.Value;
            var count = 0;
            var lo = math.min(a.xz, b.xz) - 4f;
            var hi = math.max(a.xz, b.xz) + 4f;
            for (int layer = 1; layer <= Punchy.Flora.FloraPlacement.TreeLayer; layer++)
            {
                var size = f.CellSizes[layer];
                var c0 = (int2)math.floor(lo / size);
                var c1 = (int2)math.floor(hi / size);
                for (int y = c0.y; y <= c1.y; y++)
                for (int x = c0.x; x <= c1.x; x++)
                {
                    var plant = Punchy.Flora.FloraPlacement.Place(ref f, land, new int2(x, y), layer);
                    if (plant.Model < 0 || plant.Model >= f.Models.Length)
                        continue;
                    var shape = f.Models[plant.Model];
                    if (shape.Height * plant.Scale < 0.5f)
                        continue;
                    // (Its distance from the line on the ground, the ends left out: something right behind the hunter
                    // or the camera doesn't hide the body.)
                    var ab = b.xz - a.xz;
                    var t = math.saturate(math.dot(plant.Position.xz - a.xz, ab) / math.max(math.lengthsq(ab), 1e-4f));
                    if (t < 0.08f || t > 0.95f)
                        continue;
                    // (A tree whose crown starts above the camera only gets in the way with its trunk.)
                    var crown = shape.CrownBase * plant.Scale > 1.8f ? 0f : shape.CrownRadius;
                    var reach = math.max(math.max(crown, shape.TrunkRadius), 0.6f) * plant.Scale + 0.3f;
                    if (math.distance(plant.Position.xz, a.xz + ab * t) < reach)
                        count++;
                }
            }
            return count;
        }

        static bool remoteRoll;
        int remoteRollStep;

        // A-26: "-chatshot" on a shot-list line: the first two folk stood talking (the one who started it and the one it
        // stopped, within 1.8 m of each other) once the line has run 2 s: the camera 4 m side-on of the pair at head
        // height, looking at the middle of them, and the line's shot brought forward to 1.2 s later (their gestures and
        // turned heads settled). The line's -wait is the longest it waits for a talk.
        static bool chatShot;
        Entity chatA, chatB;
        float3 chatSide;
        int chatShotLine = -1;

        void ChatShot()
        {
            if (!chatShot || shotIndex < 0 || shotStarted < 0f || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            if (chatShotLine == shotIndex)
            {
                // (Held on the pair until the shot.)
                if (em.Exists(chatA) && em.Exists(chatB))
                    FrameChat(em.GetComponentData<CreatureState>(chatA).Position, em.GetComponentData<CreatureState>(chatB).Position, false);
                return;
            }
            if (Time.realtimeSinceStartup - shotStarted < 2f)
                return;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var entities = q.ToEntityArray(Allocator.Temp);
            var eye = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;
            var best = -1;
            var bestPartner = -1;
            var bestD = float.MaxValue;
            for (int i = 0; i < states.Length; i++)
            {
                var a = states[i];
                if (a.ChatRole != 1 || a.Mode != CreatureMode.Idle || a.Stage != CreatureAnimator.ChatTalk && a.Stage != CreatureAnimator.ChatListen)
                    continue;
                for (int j = 0; j < states.Length; j++)
                    if (entities[j] == a.Partner && states[j].ChatRole == 2 && states[j].Partner == entities[i] &&
                        math.distance(states[j].Position.xz, a.Position.xz) < 1.8f && states[j].Speed < 0.2f && a.Speed < 0.2f)
                    {
                        var d = math.distancesq(a.Position, eye);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = i;
                            bestPartner = j;
                        }
                    }
            }
            if (best >= 0)
            {
                chatA = entities[best];
                chatB = entities[bestPartner];
                chatShotLine = shotIndex;
                FrameChat(states[best].Position, states[bestPartner].Position, true);
                shotWait = Time.realtimeSinceStartup - shotStarted + 1.2f;
                var director = FindAnyObjectByType<FaunaDirector>();
                Debug.Log($"DebugControls: chatshot: {director?.SpeciesAt(em.GetComponentData<CreatureBody>(chatA).Species)?.name} and " +
                          $"{director?.SpeciesAt(em.GetComponentData<CreatureBody>(chatB).Species)?.name} talking, " +
                          $"{math.distance(states[best].Position.xz, states[bestPartner].Position.xz):F1} m apart, at ({states[best].Position.x:F0}, {states[best].Position.z:F0}), " +
                          $"camera 4 m side-on ({SightBlockers((states[best].Position + states[bestPartner].Position) * 0.5f + new float3(0f, 1.2f, 0f), (states[best].Position + states[bestPartner].Position) * 0.5f + chatSide * 4f + new float3(0f, 1.4f, 0f))} plants in the way)");
            }
            states.Dispose();
            entities.Dispose();
        }

        void FrameChat(float3 a, float3 b, bool pickSide)
        {
            var mid = (a + b) * 0.5f;
            var along = math.normalizesafe(new float3(b.x - a.x, 0f, b.z - a.z), new float3(1f, 0f, 0f));
            var side = new float3(-along.z, 0f, along.x);
            if (pickSide)
            {
                // (The side with fewer plants between the camera and the pair.)
                var target = mid + new float3(0f, 1.2f, 0f);
                if (SightBlockers(target, target + side * 4f + new float3(0f, 0.2f, 0f)) > SightBlockers(target, target - side * 4f + new float3(0f, 0.2f, 0f)))
                    side = -side;
                chatSide = side;
            }
            else if (math.dot(side, chatSide) < 0f)
                side = -side;
            var look = mid + new float3(0f, 1.15f, 0f);
            var from = mid + chatSide * 4f + new float3(0f, 1.45f, 0f);
            PoseCamera(from, Quaternion.LookRotation((Vector3)(look - from)));
        }

        /// <summary>
        /// "-remoteroll" on a shot-list line (a joined client, #48): the first time another player's body starts its
        /// somersault (Roll above 0, from the Pose byte), the camera is put 5 m to its side and a log line written; once
        /// Roll passes 0.4 a shot (remoteroll.png, into -shotdir) and a second line with the roll value.
        /// </summary>
        void RemoteRoll()
        {
            if (!remoteRoll || remoteRollStep >= 2 || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<RemoteAvatar>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            for (int i = 0; i < states.Length; i++)
            {
                var r = states[i];
                if (r.Roll <= 0f)
                    continue;
                if (remoteRollStep == 0)
                {
                    var fwd = new float3(math.sin(r.Yaw), 0f, math.cos(r.Yaw));
                    var from = r.Position + new float3(fwd.z, 0f, -fwd.x) * 5f + new float3(0f, 1.2f, 0f);
                    PoseCamera(from, Quaternion.LookRotation((Vector3)(r.Position + new float3(0f, 0.5f, 0f)) - (Vector3)from));
                    remoteRollStep = 1;
                    Debug.Log($"DebugControls: remoteroll: remote player rolling (Roll {r.Roll:F2}) at ({r.Position.x:F1}, {r.Position.z:F1})");
                }
                else if (r.Roll > 0.4f)
                {
                    if (shotDir != null)
                        Shot("remoteroll.png");
                    remoteRollStep = 2;
                    Debug.Log($"DebugControls: remoteroll: shot at Roll {r.Roll:F2}");
                }
                break;
            }
            states.Dispose();
        }

        /// <summary>"-colossusshot 300": the player and camera that far from the Dunes colossus (south-west of it), looking at its chest.</summary>
        void ColossusShot()
        {
            if (colossusShotDistance <= 0f || !DunesColossus.Placed || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var d = colossusShotDistance;
            colossusShotDistance = 0f;
            var at = DunesColossus.Position;
            var stand = at.xz + math.normalize(new float2(-0.6f, -0.8f)) * d;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(stand.x, ground.SampleHeight(stand) + 0.1f, stand.y);
            em.SetComponentData(player, t);
            var eye = new Vector3(stand.x, ground.SampleHeight(stand) + (d > 150f ? 25f : 4f), stand.y);
            var target = new Vector3(at.x, at.y + (d > 150f ? 33f : 38f), at.z);
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            if (ground.Settlements.IsCreated)
            {
                // A-12 (#40): how far the colossus stands from the nearest settlement.
                ref var towns = ref ground.Settlements.Value;
                var near = float.MaxValue;
                var nearAt = float2.zero;
                for (int k = 0; k < towns.Settlements.Length; k++)
                {
                    var dist = math.distance(towns.Settlements[k].Centre, at.xz);
                    if (dist < near)
                    {
                        near = dist;
                        nearAt = towns.Settlements[k].Centre;
                    }
                }
                Debug.Log($"DebugControls: colossus at ({at.x:F0}, {at.z:F0}): nearest settlement {near:F0} m away at ({nearAt.x:F0}, {nearAt.y:F0}), of {towns.Settlements.Length}");
            }
            Debug.Log($"DebugControls: colossus shot from {d:F0} m: camera ({eye.x:F0}, {eye.y:F0}, {eye.z:F0}) looking at ({target.x:F0}, {target.y:F0}, {target.z:F0})");
        }

        /// <summary>
        /// A-72: "-walkershot Saguaro,80[,side|front|back]": the camera follows that walking colossus from that far off
        /// (ColossusWalker.ShotPose), with the player moved to stand there when more than 40 m away, so the ground streams in.
        /// </summary>
        void WalkerShot()
        {
            if (ColossusWalker.Shot == null || !ColossusWalker.ShotPose(ColossusWalker.Shot, out var eye, out var rotation, out var stand))
                return;
            PoseCamera(eye, rotation);
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            if (math.distance(t.Position.xz, stand.xz) <= 40f)
                return;
            t.Position = stand;
            em.SetComponentData(player, t);
        }

        /// <summary>
        /// "-mantrapshot side": the player 5 m south of the first carnivorous plant and the camera 4 m east of it, looking at its
        /// head side-on. "-mantrapshot lunge": the player 0.6 m from it, the camera 3 m east, and the strike held at its peak.
        /// </summary>
        void MantrapShot()
        {
            // A-10c: the lunge shot is taken 0.15 s into the 0.3 s strike hold (the line's -wait is the longest it waits).
            if (mantrapLungePending && Mantrap.StrikeHeld && shotIndex >= 0 && shotStarted >= 0f)
            {
                mantrapLungePending = false;
                shotWait = Time.realtimeSinceStartup - shotStarted + 0.15f;
                Debug.Log($"DebugControls: mantrap strike held, shot in 0.15 s (hold {Mantrap.HoldTime:F2} s)");
            }
            if (mantrapShotMode == null || !Mantrap.Placed || Mantrap.Spots.Count == 0 || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var lunge = mantrapShotMode == "lunge";
            mantrapShotMode = null;
            Mantrap.HoldStrike = lunge;
            Mantrap.StrikeHeld = false;
            mantrapLungePending = lunge;
            Mantrap.ThinFog = true;
            var at = Mantrap.Spot(0);
            var stand = new float2(at.x, at.z - (lunge ? 0.6f : 5f));   // player south of the plant; camera east, level with the head
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(stand.x, ground.SampleHeight(stand) + 0.1f, stand.y);
            em.SetComponentData(player, t);
            var cam = new float2(at.x + 4f, at.z - (lunge ? 0.3f : 0f));
            var eye = new Vector3(cam.x, at.y + 2.5f, cam.y);
            var target = new Vector3(at.x, at.y + (lunge ? 1.2f : 1.6f), at.z - (lunge ? 0.3f : 0f));
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            Debug.Log($"DebugControls: mantrap {(lunge ? "lunge" : "side")} shot at plant ({at.x:F0}, {at.z:F0}): camera {Vector3.Distance(eye, new Vector3(at.x, eye.y, at.z)):F1} m away");
        }

        // ---------------------------------------------------------------- A-11 ocotillo mimic

        /// <summary>"-mimicshot rest|run" (with "-spawn OcotilloMimic:1 -place tree:Ocotillo"): "rest" stands the mimic 2.5 m
        /// from the lone ocotillo and frames both side-on; "run" walks the player at it from 30 m, logs the startle distance,
        /// fold time, run speed and settle time, and takes the shot mid-run.</summary>
        static string mimicShotMode;
        static bool mimicTreeSet;
        static float3 mimicTree;
        static float mimicTreeHeight;
        Entity mimicEntity;
        float mimicT0 = -1f, mimicLogAt, mimicStartT = -1f, mimicEndT = -1f, mimicMaxSpeed, mimicShotT = -1f;
        float3 mimicStartPos, mimicRunPos, mimicEndPos;
        float mimicRunT;
        bool mimicFolded, mimicSettled, mimicRunMarked;

        /// <summary>Real ocotillo (flora recipe) vs the mimic's arms, side by side in the log.</summary>
        void LogMimicVsReal()
        {
            FloraSpecies real = null;
            CreatureSpecies mimic = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<FloraSpecies>())
                if (f.name == "Ocotillo")
                    real = f;
            foreach (var c in Resources.FindObjectsOfTypeAll<CreatureSpecies>())
                if (c.name == "OcotilloMimic")
                    mimic = c;
            if (real == null || mimic == null)
            {
                Debug.Log($"DebugControls: mimic vs real: assets not found (real {real != null}, mimic {mimic != null})");
                return;
            }
            // Recipe: stems Branches (random), tilt 4..28 deg off vertical (mean 16), length 0.7..1 of the height.
            var arms = 0;
            float sum = 0f, lo = 999f, hi = 0f;
            foreach (var p in mimic.Parts)
            {
                var tilt = 90f + p.Direction.x;
                var n = p.Mirror ? 2 : 1;
                arms += n;
                sum += tilt * n;
                lo = Mathf.Min(lo, tilt);
                hi = Mathf.Max(hi, tilt);
            }
            var size = (mimic.Size.x + mimic.Size.y) * 0.5f;
            Debug.Log($"DebugControls: REAL ocotillo: stems {real.Branches.x:F0}-{real.Branches.y:F0}, splay 4-28 deg (mean 16), height {real.Height.x:F1}-{real.Height.y:F1} m");
            Debug.Log($"DebugControls: MIMIC: arms {arms}, splay {lo:F0}-{hi:F0} deg (mean {sum / math.max(arms, 1):F0}), arm tilt from Direction only; run speed {mimic.RunSpeed:F1} x scale {size:F1}, flee distance {mimic.FleeDistance:F1} x scale");
        }

        void MimicShot()
        {
            if (mimicShotMode == null || !mimicTreeSet || !EnsureLocations(out var ground) ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex("OcotilloMimic") : -1;
            if (index < 0)
                return;
            var em = queryWorld.EntityManager;
            var now = Time.realtimeSinceStartup;
            if (mimicT0 < 0f)
            {
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                var entities = q.ToEntityArray(Allocator.Temp);
                var pp = em.GetComponentData<LocalTransform>(player).Position;
                var best = float.MaxValue;
                mimicEntity = Entity.Null;
                foreach (var e in entities)
                {
                    if (em.GetComponentData<CreatureBody>(e).Species != index)
                        continue;
                    var d = math.distance(em.GetComponentData<CreatureState>(e).Position, pp);
                    if (d < best)
                    {
                        best = d;
                        mimicEntity = e;
                    }
                }
                entities.Dispose();
                if (mimicEntity == Entity.Null)
                    return;
                var run = mimicShotMode == "run";
                var at = mimicTree;
                var spot = run ? new float2(at.x - 12f, at.z) : new float2(at.x - 2.5f, at.z);
                var s = em.GetComponentData<CreatureState>(mimicEntity);
                s.Position = new float3(spot.x, ground.SampleHeight(spot), spot.y);
                s.Home = spot;
                s.Mode = CreatureMode.Rest;
                s.Timer = 600f;
                s.Speed = 0f;
                var settle = mimicShotMode == "settle";
                s.Curl = settle ? 0f : 1f;
                s.Lie = settle ? 0f : 1f;
                em.SetComponentData(mimicEntity, s);
                mimicT0 = now;
                if (mimicTreeHeight < 0.5f)
                    mimicTreeHeight = 3.5f; // the flora blob reads 0 for this tree: frame a fixed 3.5 m
                LogMimicVsReal();
                mimicLogAt = 0f;
                Debug.Log($"DebugControls: mimic {mimicShotMode}: placed at ({spot.x:F1}, {spot.y:F1}), tree ({at.x:F1}, {at.z:F1}) {mimicTreeHeight:F1} m tall");
            }
            if (!em.Exists(mimicEntity))
                return;
            var m = em.GetComponentData<CreatureState>(mimicEntity);
            var pt = em.GetComponentData<LocalTransform>(player);
            if (mimicShotMode != "run")
            {
                if (mimicShotMode == "settle" && !mimicSettled && m.Curl >= 0.95f)
                {
                    mimicSettled = true;
                    Debug.Log($"DebugControls: mimic SETTLE (safe, Rest, arms down) to arms fully up (curl >= 0.95) took {now - mimicT0:F2} s");
                }
                var far = new float2(m.Position.x + 60f, m.Position.z);
                pt.Position = new float3(far.x, ground.SampleHeight(far) + 0.1f, far.y);
                em.SetComponentData(player, pt);
                var mid = new Vector3(mimicTree.x - 1.25f, mimicTree.y, mimicTree.z);
                var dist = 5f; // A-12: about 5 m back, so both plants fill the frame
                var eye = mid + new Vector3(0f, mimicTreeHeight * 0.5f, -dist);
                PoseCamera(eye, Quaternion.LookRotation(mid + Vector3.up * mimicTreeHeight * 0.5f - eye));
                if (now - mimicT0 > 3f && now - mimicLogAt > 1f)
                {
                    mimicLogAt = now;
                    Debug.Log($"DebugControls: mimic rest: mode {m.Mode}, curl {m.Curl:F2}, lie {m.Lie:F2}, speed {m.Speed:F2}");
                }
                return;
            }

            // Run: until the startle the player walks at 6 m/s from 30 m east; after it, he stands where he was.
            var dx = pt.Position.x - m.Position.x;
            var dz = pt.Position.z - m.Position.z;
            var range = math.sqrt(dx * dx + dz * dz);
            var t = now - mimicT0;
            if (mimicStartT < 0f && m.Mode != CreatureMode.Flee)
            {
                var from = math.max(1f, 30f - t * 6f);
                var p = new float2(m.Position.x + from, m.Position.z);
                pt.Position = new float3(p.x, ground.SampleHeight(p) + 0.1f, p.y);
                em.SetComponentData(player, pt);
            }
            if (mimicStartT < 0f && m.Mode == CreatureMode.Flee)
            {
                mimicStartT = now;
                mimicStartPos = m.Position;
                Debug.Log($"DebugControls: mimic STARTLE at {range:F1} m (t {t:F1} s): curl {m.Curl:F2}, lie {m.Lie:F2}");
            }
            if (mimicStartT >= 0f)
            {
                var since = now - mimicStartT;
                mimicMaxSpeed = math.max(mimicMaxSpeed, m.Speed);
                if (!mimicFolded && m.Curl < 0.1f)
                {
                    mimicFolded = true;
                    Debug.Log($"DebugControls: mimic FOLDED (curl < 0.1) {since:F2} s after the startle; lie {m.Lie:F2}");
                }
                if (!mimicRunMarked && since >= 1.5f)
                {
                    mimicRunMarked = true;
                    mimicRunPos = m.Position;
                    mimicRunT = now;
                }
                if (mimicShotT < 0f && since >= 2.2f && m.Mode == CreatureMode.Flee && shotIndex >= 0 && shotStarted >= 0f)
                {
                    mimicShotT = now;
                    shotWait = now - shotStarted + 0.05f;
                    Debug.Log($"DebugControls: mimic mid-run shot at {since:F2} s: speed {m.Speed:F2} m/s, {range:F1} m from the player, curl {m.Curl:F2}, lie {m.Lie:F2}");
                }
                if (mimicEndT < 0f && mimicRunMarked && m.Mode != CreatureMode.Flee)
                {
                    mimicEndT = now;
                    mimicEndPos = m.Position;
                    var span = math.max(now - mimicRunT, 0.01f);
                    Debug.Log($"DebugControls: mimic FLEE END {since:F2} s after the startle, mode {m.Mode}: max speed {mimicMaxSpeed:F2} m/s, mean speed {math.distance(mimicRunPos.xz, mimicEndPos.xz) / span:F2} m/s over {span:F1} s, {range:F1} m from the player");
                }
                if (mimicEndT >= 0f && !mimicSettled && m.Curl >= 0.95f)
                {
                    mimicSettled = true;
                    Debug.Log($"DebugControls: mimic SETTLED (curl >= 0.95) {now - mimicEndT:F2} s after the flee ended, mode {m.Mode}, lie {m.Lie:F2}");
                }
            }
            if (now - mimicLogAt > 0.5f)
            {
                mimicLogAt = now;
                Debug.Log($"DebugControls: mimic t {t:F1}: mode {m.Mode}, speed {m.Speed:F2}, curl {m.Curl:F2}, lie {m.Lie:F2}, {range:F1} m from the player");
            }
            // Side-on camera on the mimic, 9 m south of it.
            var focus = new Vector3(m.Position.x, m.Position.y + 1.4f, m.Position.z);
            var cam = focus + new Vector3(0f, 0.6f, -6f); // A-12: 6 m back: only the mimic in frame (the tree is 12+ m east)
            PoseCamera(cam, Quaternion.LookRotation(focus - cam));
        }

        // ---------------------------------------------------------------- hunt log

        /// <summary>"-huntlog": logs each land hunter's chase as it goes (Z-53): stalk, run, pounce (landed or missed),
        /// eating, and how the hunt ended (ate, missed, slipped away, tired). Diving birds and folk hunters are left out.</summary>
        static bool huntLog;
        /// <summary>Per creature: its mode and stage last frame.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, int2> hunts = new System.Collections.Generic.Dictionary<Entity, int2>();

        void HuntLog()
        {
            if (!huntLog || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var entities = q.ToEntityArray(Allocator.Temp);
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            FaunaDirector director = null;
            for (int i = 0; i < states.Length; i++)
            {
                var c = states[i];
                var e = entities[i];
                var now = new int2((int)c.Mode, c.Stage);
                var landHunt = c.Mode == CreatureMode.Hunt && c.Stage < FolkHunting.Stalk && bodies[i].Blob.Value.Habitat != Habitat.Flies;
                var had = hunts.TryGetValue(e, out var was);
                if (!landHunt)
                {
                    if (had)
                    {
                        hunts.Remove(e);
                        HuntLine(director ??= FindAnyObjectByType<FaunaDirector>(), bodies[i], c, em.HasComponent<CreatureMind>(e) ? em.GetComponentData<CreatureMind>(e).Hunger : 0f,
                            "ended: " + (was.y == 3 ? "ate" : was.y == 4 ? "missed" : was.y == 2 ? "slipped away" : "tired") + $", now {c.Mode}");
                    }
                    continue;
                }
                if (!had || was.y != now.y)
                    HuntLine(director ??= FindAnyObjectByType<FaunaDirector>(), bodies[i], c, em.HasComponent<CreatureMind>(e) ? em.GetComponentData<CreatureMind>(e).Hunger : 0f,
                        now.y switch { 0 => "stalking", 1 => "running", 2 => "pounce landed", 3 => "eating", 4 => "pounce missed", _ => $"stage {now.y}" });
                hunts[e] = now;
            }
            if (hunts.Count > 4000)
                hunts.Clear();
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();
        }

        static void HuntLine(FaunaDirector director, CreatureBody body, CreatureState c, float hunger, string what)
        {
            var name = director != null ? director.SpeciesAt(body.Species)?.name : body.Species.ToString();
            var aggression = CreaturePersonality.Of(ref body.Blob.Value, c.Individual).Aggression;
            Debug.Log($"DebugControls: hunt: {name} #{c.Individual:X8} {what} (speed {c.Speed:F1}, hunger {hunger:F2}, aggression {aggression:F2}) at " +
                      $"({c.Position.x:F1}, {c.Position.y:F1}, {c.Position.z:F1}); caught so far {FaunaDirector.Caught}");
        }

        // ---------------------------------------------------------------- personality log

        /// <summary>"-personalitylog Dog": 8 s into the line (and again at 20 s), logs every creature of that species with
        /// its Individual, aggression, whether it hunts, its mode and its nearest prey (#50, #7). Personality comes from the
        /// replicated Individual, so a host and a joined client should print the same values.</summary>
        static string personalitySpecies;
        static float personalityAt = -1f;

        void PersonalityLog()
        {
            if (personalitySpecies == null || queryWorld == null || !queryWorld.IsCreated)
                return;
            if (personalityAt < 0f)
                personalityAt = Time.timeSinceLevelLoad;
            var t = Time.timeSinceLevelLoad - personalityAt;
            if (!(t > 8f && t - Time.deltaTime <= 8f) && !(t > 20f && t - Time.deltaTime <= 20f))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var rows = new System.Collections.Generic.List<string>();
            for (int i = 0; i < states.Length; i++)
            {
                var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : null;
                if (name != personalitySpecies || states[i].Gone)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                var c = states[i];
                var prey = float.MaxValue;
                for (int j = 0; j < states.Length; j++)
                    for (int k = 0; k < b.Prey.Length; k++)
                        if (bodies[j].Species == b.Prey[k] && !states[j].Gone)
                            prey = math.min(prey, math.distance(states[j].Position, c.Position));
                rows.Add($"#{c.Individual:X8} aggression {CreaturePersonality.Of(ref b, c.Individual).Aggression:F3} " +
                         $"hunts {CreaturePersonality.WillHunt(ref b, c.Individual)} mode {c.Mode} weight {b.WeightAt(c.Scale):F1} kg " +
                         $"prey {(prey < float.MaxValue ? prey.ToString("F1") + " m" : "none")}");
            }
            rows.Sort(System.StringComparer.Ordinal);
            var role = queryWorld.Name;
            Debug.Log($"DebugControls: personality ({role}, t {t:F0} s): {rows.Count} {personalitySpecies}");
            for (int i = 0; i < rows.Count; i++)
                Debug.Log($"DebugControls: personality {personalitySpecies} {i + 1}: {rows[i]}");
            states.Dispose();
            bodies.Dispose();
        }

        // ---------------------------------------------------------------- comfort

        /// <summary>"-comfort": every 5 s, logs each species that minds the weather with its count, mean discomfort meter,
        /// mean pressure and most common complaint, plus the weather and felt heat where they stand (A-71, #69).
        /// "-comfort off" switches the comfort check off in the brain (for the perf before/after).</summary>
        static bool comfortLog;
        static float comfortNext = 6f;

        void ComfortLog()
        {
            if (!comfortLog || queryWorld == null || !queryWorld.IsCreated || Time.timeSinceLevelLoad < comfortNext)
                return;
            comfortNext = Time.timeSinceLevelLoad + 5f;
            var director = FindAnyObjectByType<FaunaDirector>();
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(),
                ComponentType.ReadOnly<CreatureMind>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var minds = q.ToComponentDataArray<CreatureMind>(Allocator.Temp);
            var night = 0f;
            using (var cq = em.CreateEntityQuery(ComponentType.ReadOnly<FaunaClock>()))
                if (cq.CalculateEntityCount() == 1)
                    night = cq.GetSingleton<FaunaClock>().Night;
            var groups = new SortedDictionary<string, (int n, float meter, float pressure, int[] kinds, float3 at)>(System.StringComparer.Ordinal);
            // Row 2: how many are in shelter now (by kind), and how many are on their way to one.
            var covered = new int[6];
            int sheltered = 0, going = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Gone || !bodies[i].Blob.Value.Comfort.Minds)
                    continue;
                if (minds[i].Cover != ShelterNeed.None)
                {
                    sheltered++;
                    for (int k = 0; k < 6; k++)
                        if (((int)minds[i].Cover & (1 << k)) != 0)
                            covered[k]++;
                }
                if (states[i].Mode == CreatureMode.Shelter && states[i].Stage == 0)
                    going++;
                var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : null;
                name ??= "#" + bodies[i].Species;
                if (!groups.TryGetValue(name, out var g))
                    g = (0, 0f, 0f, new int[9], states[i].Position);
                g.n++;
                g.meter += minds[i].Discomfort;
                g.pressure += minds[i].ComfortPressure;
                g.kinds[(int)minds[i].DiscomfortKind]++;
                groups[name] = g;
            }
            Debug.Log($"[comfort] {queryWorld.Name} t {Time.timeSinceLevelLoad:F0} s night {night:F2} grid ready {Comfort.Grid.Data.Ready} " +
                      $"off {Comfort.Off.Data}: {groups.Count} species");
            var counts = CreatureBrainSystem.Shelters.Data;
            Debug.Log($"[comfort] sheltered {sheltered} (roof {covered[0]} tree {covered[1]} water {covered[2]} fire {covered[3]} shade {covered[4]} " +
                      $"cave {covered[5]}) going {going} | started {counts.Started} indoors {counts.Indoors} arrived {counts.Arrived} none found {counts.NotFound} posed {counts.Posed} rock {counts.Rock}");
            // Every folk race's profile at the player's spot (the same five individuals each), whether or not any are near:
            // one line per race, mean pressure and the commonest complaint (the card's 5 races x 3 scenes).
            if (director != null && QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var me) && EnsureLocations(out var ground))
            {
                var here = Comfort.WeatherAt(me.Position.xz);
                var underground = me.Position.y < ground.SampleHeight(me.Position.xz) - 2f;
                Debug.Log($"[comfort] at player ({me.Position.x:F0},{me.Position.z:F0}) hour {(Cycle != null ? Cycle.TimeOfDay : -1f):F1}: " +
                          $"heat {here.Heat:F2} felt {Comfort.FeltHeat(here, night, underground):F2} humid {here.Humidity:F2} rain {here.Rain:F2} " +
                          $"snow {here.Snow:F2} wind {here.Wind:F2} storm {here.Storm:F2} sun {here.Sunlight:F2}");
                var seen = new HashSet<ComfortRace>();
                for (int sp = 0; sp < director.SpeciesCount; sp++)
                {
                    var blob = director.BlobAt(sp);
                    if (!blob.IsCreated)
                        continue;
                    ref var profile = ref blob.Value.Comfort;
                    if (profile.Race == ComfortRace.None || !profile.Minds || !seen.Add(profile.Race))
                        continue;
                    var sum = 0f;
                    var kinds = new int[9];
                    for (uint k = 1; k <= 5; k++)
                    {
                        sum += Comfort.Pressure(in profile, k * 0x9E3779B9u, in here, night, underground, ShelterNeed.None, out var kind);
                        kinds[(int)kind]++;
                    }
                    int worst = 0;
                    for (int k = 1; k < kinds.Length; k++)
                        if (kinds[k] > 0 && (worst == 0 || kinds[k] > kinds[worst]))
                            worst = k;
                    Debug.Log($"[comfort] race {profile.Race} ({director.SpeciesAt(sp).name}): pressure {sum / 5f:F2} kind {(ComfortKind)worst} " +
                              $"likes {profile.Heat.x:F2}-{profile.Heat.y:F2} tol {profile.Tolerance:F2}");
                }
            }
            foreach (var kv in groups)
            {
                var g = kv.Value;
                int top = 0;
                for (int k = 1; k < g.kinds.Length; k++)
                    if (g.kinds[k] > g.kinds[top] || (top == 0 && g.kinds[k] > 0))
                        top = k;
                var w = Comfort.WeatherAt(g.at.xz);
                Debug.Log($"[comfort] {kv.Key} x{g.n}: meter {g.meter / g.n:F2} pressure {g.pressure / g.n:F2} kind {(ComfortKind)top} " +
                          $"({g.kinds[top]}) | heat {w.Heat:F2} felt {Comfort.FeltHeat(w, night, false):F2} humid {w.Humidity:F2} " +
                          $"rain {w.Rain:F2} snow {w.Snow:F2} wind {w.Wind:F2} storm {w.Storm:F2} sun {w.Sunlight:F2}");
            }
            states.Dispose();
            bodies.Dispose();
            minds.Dispose();
        }

        // ---------------------------------------------------------------- spin check

        /// <summary>"-spincheck" (or "-spincheck 270,3"): logs any creature that turns this many degrees within this many
        /// seconds on the spot (Zach's Z-07: brief spins of 2-3 s).</summary>
        static bool spinCheck;
        static float spinDegrees = 270f, spinSeconds = 3f;
        /// <summary>Per creature: its heading last frame, how far it has turned since standing still, for how long, and
        /// when it was last logged.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> spins = new System.Collections.Generic.Dictionary<Entity, float4>();
        /// <summary>Per species, Mode and (wandering) stage: heading change (deg), seconds, speed x seconds, |yaw rate| x
        /// seconds (#12: the songbirds' wandering split into on the ground, flying, and the flare near the spot).</summary>
        readonly System.Collections.Generic.Dictionary<int, float4> turnSum = new System.Collections.Generic.Dictionary<int, float4>();
        /// <summary>A-54 #12: heading flips (a turn of 15 deg or more one way, then back the other) per turnSum key.</summary>
        readonly System.Collections.Generic.Dictionary<int, int> flipSum = new System.Collections.Generic.Dictionary<int, int>();
        float turnLogAt, traceAt;
        readonly System.Collections.Generic.Dictionary<Entity, float> wanderSince = new System.Collections.Generic.Dictionary<Entity, float>();
        Entity traced = Entity.Null;
        int traceLines;
        readonly System.Collections.Generic.Dictionary<Entity, float> spinLogged = new System.Collections.Generic.Dictionary<Entity, float>();

        /// <summary>
        /// Watches every creature's heading: one that turns through spinDegrees within spinSeconds while barely moving
        /// (under 0.3 m/s) is spinning in place, and is logged (at most every 20 s each) with what it's doing.
        /// </summary>
        void SpinCheck()
        {
            if (!spinCheck || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var entities = q.ToEntityArray(Allocator.Temp);
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var dt = Time.deltaTime;
            var now = Time.time;
            for (int i = 0; i < states.Length; i++)
            {
                var c = states[i];
                var e = entities[i];
                if (!spins.TryGetValue(e, out var w))
                    w = new float4(c.Yaw, 0f, 0f, 0f);
                var signed = math.atan2(math.sin(c.Yaw - w.x), math.cos(c.Yaw - w.x));
                var step = math.abs(signed);
                w.x = c.Yaw;
                // (w.w: the turn so far in the one direction; reversing after 15 deg or more of it is a flip.)
                var flipped = false;
                if (signed * w.w < 0f)
                {
                    flipped = math.abs(w.w) >= math.radians(15f);
                    w.w = signed;
                }
                else
                    w.w += signed;
                // (Heading change per second of the songbirds' and dormice's wandering, for the Z-12 before/after.)
                // (A-13 #12: Wander counts only after 5 s of it, so the turn out of a flee or a takeoff is left out.)
                if (c.Mode != CreatureMode.Wander)
                    wanderSince[e] = -1f;
                else if (!wanderSince.TryGetValue(e, out var ws) || ws < 0f)
                    wanderSince[e] = now;
                var settledWander = c.Mode != CreatureMode.Wander || now - wanderSince[e] > 5f;
                if (bodies[i].Species >= 0 && settledWander)
                {
                    var targetGap = math.distance(c.Position.xz, c.Target);
                    var bucket = c.Mode != CreatureMode.Wander ? 0 : c.Stage == 0 ? 1 : c.Stage == 1 ? (targetGap < 1.5f ? 3 : 2) : 4;
                    var key = (bodies[i].Species * 64 + (int)c.Mode) * 8 + bucket;
                    turnSum.TryGetValue(key, out var acc);
                    flipSum.TryGetValue(key, out var flips);
                    flipSum[key] = flips + (flipped ? 1 : 0);
                    turnSum[key] = new float4(acc.x + math.degrees(step), acc.y + dt, acc.z + c.Speed * dt, acc.w + math.abs(math.degrees(c.YawRate)) * dt);                    // One wandering songbird traced twice a second (40 lines): stage, speed, yaw rate, how far from its
                    // target and how far above its target height.
                    if (c.Mode == CreatureMode.Wander && traceLines < 40 && (traced == Entity.Null ? c.Stage == 1 : traced == e) && now >= traceAt)
                    {
                        var director = FindAnyObjectByType<FaunaDirector>();
                        if (director != null && director.SpeciesAt(bodies[i].Species)?.name == "Songbird")
                        {
                            traced = e;
                            traceAt = now + 0.5f;
                            traceLines++;
                            Debug.Log($"DebugControls: songbird trace: stage {c.Stage} speed {c.Speed:F2} yaw rate {math.degrees(c.YawRate):F0} deg/s " +
                                      $"target {targetGap:F2} m, above target height {c.Position.y - c.TargetHeight:F2} m, fly {c.Fly:F2}");
                        }
                    }
                }
                FlightTrace(e, c, bodies[i].Species, step, now);
                if (c.Speed < 0.3f)
                {
                    w.y += step;
                    w.z += dt;
                }
                else
                    w.y = w.z = 0f;
                if (w.z > spinSeconds)
                    w.y = w.z = 0f;
                if (w.y > math.radians(spinDegrees) && (!spinLogged.TryGetValue(e, out var at) || now - at > 20f))
                {
                    spinLogged[e] = now;
                    var director = FindAnyObjectByType<FaunaDirector>();
                    var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : bodies[i].Species.ToString();
                    Debug.LogWarning($"DebugControls: spinning: {name} turned {math.degrees(w.y):F0} deg in {w.z:F1} s at speed {c.Speed:F2} " +
                                     $"({c.Mode}, stage {c.Stage}, action {c.Action}, sit {c.Sit:F2}, lie {c.Lie:F2}, look {c.Look:F2}, " +
                                     $"yaw rate {math.degrees(c.YawRate):F0} deg/s, target {math.distance(c.Position.xz, c.Target):F2} m, " +
                                     $"focus {math.distance(c.Position.xz, c.Focus):F2} m) at ({c.Position.x:F1}, {c.Position.y:F1}, {c.Position.z:F1})");
                    w.y = w.z = 0f;
                }
                spins[e] = w;
            }
            if (now >= turnLogAt)
            {
                if (turnLogAt > 0f)
                {
                    var director = FindAnyObjectByType<FaunaDirector>();
                    // (A Mode histogram per species: creature-seconds in each mode, and the heading change in it.)
                    var lines = new System.Collections.Generic.SortedDictionary<string, string>();
                    foreach (var kv in turnSum)
                    {
                        var modeKey = kv.Key / 8;
                        var species = director != null ? director.SpeciesAt(modeKey / 64)?.name : (modeKey / 64).ToString();
                        species ??= (modeKey / 64).ToString();
                        lines.TryGetValue(species, out var line);
                        var stage = (kv.Key % 8) switch { 1 => "/ground", 2 => "/flying", 3 => "/flare", 4 => "/stage2+", _ => "" };
                        var secs = math.max(kv.Value.y, 1e-3f);
                        flipSum.TryGetValue(kv.Key, out var flipCount);
                        lines[species] = line + $" {(CreatureMode)(modeKey % 64)}{stage} {kv.Value.y:F0}s {kv.Value.x / secs:F1}deg/s v{kv.Value.z / secs:F1} yr{kv.Value.w / secs:F0} flips{flipCount * 60f / secs:F1}/min;";
                    }
                    foreach (var kv in lines)
                        Debug.Log($"DebugControls: heading change: {kv.Key}:{kv.Value}");
                }
                turnSum.Clear();
                flipSum.Clear();
                turnLogAt = now + 30f;
            }
            if (spins.Count > 4000)
                spins.Clear();
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();
        }

        /// <summary>Per songbird in a flight (Wander stage 1): x start time, y heading change so far (deg), z of it while
        /// above its glide line (arrived too high, circling down), w of it within 6 m of the spot.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> flights = new System.Collections.Generic.Dictionary<Entity, float4>();
        /// <summary>Per songbird in a flight: x bearing to the spot at takeoff (deg), y distance then, z peak |yaw rate|,
        /// w heading change in its first 3 s (the takeoff turn).</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> flightStart = new System.Collections.Generic.Dictionary<Entity, float4>();
        readonly System.Collections.Generic.Dictionary<Entity, float2> flightTarget = new System.Collections.Generic.Dictionary<Entity, float2>();
        int flightLines, songbirdSpecies = -2;

        /// <summary>
        /// #12/A-14: one line per songbird flight as it ends, splitting its heading change into the takeoff turn, the
        /// circling while too high over the spot, and the last 6 m; plus a line for every mid-flight retarget.
        /// </summary>
        void FlightTrace(Entity e, in CreatureState c, int species, float step, float now)
        {
            if (songbirdSpecies == -2)
            {
                var director = FindAnyObjectByType<FaunaDirector>();
                if (director == null || director.SpeciesCount == 0)
                    return;
                songbirdSpecies = -1;
                for (int k = 0; k < director.SpeciesCount; k++)
                    if (director.SpeciesAt(k)?.name == "Songbird")
                        songbirdSpecies = k;
            }
            if (species != songbirdSpecies || flightLines >= 120)
                return;
            var gap = math.distance(c.Position.xz, c.Target);
            var inFlight = c.Mode == CreatureMode.Wander && c.Stage == 1;
            if (inFlight && !flights.ContainsKey(e))
            {
                var to = c.Target - c.Position.xz;
                var bearing = math.degrees(math.atan2(math.sin(math.atan2(to.x, to.y) - c.Yaw), math.cos(math.atan2(to.x, to.y) - c.Yaw)));
                flights[e] = new float4(now, 0f, 0f, 0f);
                flightStart[e] = new float4(bearing, gap, 0f, 0f);
                flightTarget[e] = c.Target;
                return;
            }
            if (inFlight)
            {
                var f = flights[e];
                var st = flightStart[e];
                var deg = math.degrees(step);
                var glide = c.TargetHeight + math.max(gap - 0.3f, 0f) * Punchy.Fauna.CreatureMotion.GlideSlope;
                f.y += deg;
                if (c.Position.y > glide + 0.5f && gap < 15f)
                    f.z += deg;
                if (gap < 6f)
                    f.w += deg;
                if (now - f.x < 3f)
                    st.w += deg;
                st.z = math.max(st.z, math.abs(math.degrees(c.YawRate)));
                if (math.distance(flightTarget[e], c.Target) > 0.5f)
                {
                    flightLines++;
                    Debug.Log($"DebugControls: flight trace: retarget {math.distance(flightTarget[e], c.Target):F1} m at {now - f.x:F1} s");
                    flightTarget[e] = c.Target;
                }
                flights[e] = f;
                flightStart[e] = st;
            }
            else if (flights.TryGetValue(e, out var done))
            {
                var st = flightStart[e];
                var secs = math.max(now - done.x, 1e-3f);
                flightLines++;
                Debug.Log($"DebugControls: flight trace: {secs:F1} s, start bearing {st.x:F0} deg at {st.y:F1} m, turned {done.y:F0} deg " +
                          $"({done.y / secs:F1} deg/s): takeoff 3 s {st.w:F0}, high over spot {done.z:F0}, last 6 m {done.w:F0}; " +
                          $"peak yaw rate {st.z:F0} deg/s; ended {c.Mode} stage {c.Stage}");
                flights.Remove(e);
                flightStart.Remove(e);
                flightTarget.Remove(e);
            }
        }

        // ---------------------------------------------------------------- foot drift

        /// <summary>"-footdrift": measures foot sliding on the player's body (logged every 5 s while moving).</summary>
        static bool footDrift;
        readonly System.Collections.Generic.List<float> footDrifts = new System.Collections.Generic.List<float>();
        readonly float3[] footStart = new float3[8];
        readonly bool[] footPlanted = new bool[8];
        readonly float[] footWorst = new float[8], footLowest = new float[8];
        float footSpeed, footLogAt;
        int footFrames;

        /// <summary>
        /// Poses the player's body as the renderer does and follows each foot (the end of each leg chain from the
        /// body): while a foot is planted (in its stance, within 4 cm of its lowest point of late) its world position
        /// should stay put.
        /// Logs how far planted feet drifted over each stance (mean, 90th percentile, worst) and the mean speed.
        /// </summary>
        void FootDrift()
        {
            if (!footDrift || queryWorld == null || !queryWorld.IsCreated || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(typeof(PlayerAvatar), typeof(CreatureState), typeof(CreatureBody));
            if (q.CalculateEntityCount() != 1)
                return;
            var s = q.GetSingleton<CreatureState>();
            var body = q.GetSingleton<CreatureBody>();
            ref var b = ref body.Blob.Value;
            var output = new NativeArray<SegmentInstance>(b.Segments.Length + 64, Allocator.Temp);
            CreatureAnimator.Evaluate(ref b, s, body.Coat, terrain, output, 0);
            var foot = 0;
            for (int p = 0; p < b.Parts.Length && foot < 8; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var seg = output[part.FirstSegment + part.SegmentCount - 1];
                var tip = seg.Position + math.mul(seg.Rotation, new float3(0f, 0f, seg.Length));
                footLowest[foot] = footFrames == 0 ? tip.y : math.min(footLowest[foot] + Time.deltaTime * 0.05f, tip.y);
                // (On the ground in the gait's stance half, not just low: a low swing isn't a planted foot.)
                var down = tip.y < footLowest[foot] + 0.04f && s.Speed > 0.2f && math.frac(s.Phase + b.LegPhase(ref part, b.Sprint(s, s.GaitSpeed))) < 0.5f;
                if (down && !footPlanted[foot])
                {
                    footPlanted[foot] = true;
                    footStart[foot] = tip;
                    footWorst[foot] = 0f;
                }
                else if (down)
                    footWorst[foot] = math.max(footWorst[foot], math.distance(tip.xz, footStart[foot].xz));
                else if (footPlanted[foot])
                {
                    footPlanted[foot] = false;
                    footDrifts.Add(footWorst[foot]);
                }
                foot++;
            }
            output.Dispose();
            footSpeed += s.Speed;
            footFrames++;
            if (Time.time < footLogAt)
                return;
            footLogAt = Time.time + 5f;
            if (footDrifts.Count > 0)
            {
                footDrifts.Sort();
                float sum = 0f;
                foreach (var d in footDrifts)
                    sum += d;
                Debug.Log($"DebugControls: foot drift over {footDrifts.Count} stances: mean {sum / footDrifts.Count * 100f:F1} cm, " +
                          $"p90 {footDrifts[(int)(footDrifts.Count * 0.9f)] * 100f:F1} cm, worst {footDrifts[footDrifts.Count - 1] * 100f:F1} cm; " +
                          $"speed {footSpeed / math.max(footFrames, 1):F2} m/s");
            }
            footDrifts.Clear();
            footSpeed = 0f;
            footFrames = 0;
        }

        // Once per run (statics survive the scene reloads that hosting and joining do).
        static bool launchOptionsApplied;
        /// <summary>"-watch Squirrel": the camera follows the nearest animal of that species (for testing behaviours).</summary>
        static string watchSpecies;
        /// <summary>The -watch target the "no such creature" warning was last given for, so it is said once, not every frame.</summary>
        static string watchWarned;
        static float? watchPitchOverride;
        /// <summary>
        /// "-watchfrom 12,25" (with -watch): the camera stands still instead, 25 m off and 12 m above the ground, turning
        /// to keep the animal in view (the ground over it, for one under the ground: a worm's furrow and mound). It moves
        /// to a new spot only when the animal gets more than twice that far off. (-watch alone follows a buried worm
        /// under the ground.)
        /// </summary>
        static float2? watchFrom;
        static Vector3? watchFromAt;
        /// <summary>"-walk": where the player is walking to on this shot line (A-34), and when its next probe line is due.</summary>
        static float3? walkTarget;
        static float walkLogNext;
        /// <summary>"-walk x,z;x,z;...": the legs still to walk after walkTarget, in order (W-61b; dev/test only).</summary>
        static readonly System.Collections.Generic.List<float3> walkPath = new();
        /// <summary>W-65c: how the current walk ended ("ARRIVED", "STOPPED"), null while it goes on.</summary>
        static string walkEnd;
        /// <summary>W-67: the walker's height last frame (ARRIVED waits for it to stop falling).</summary>
        static float walkLastY;
        /// <summary>
        /// W-67b: a -goto waiting for the kit boxes round its point (x, z; y the explicit height, or NaN), so it can land
        /// on a deck or floor rather than the ground under it, and the -walk holds till then; and when it gives up.
        /// </summary>
        static float3? gotoSettle;
        static float gotoSettleUntil;
        static float2 walkStillAt;
        static float walkStillSince;
        static int walkMoves;
        static int walkLeg;
        /// <summary>"-untilwork": the most seconds a shot waits past -wait for the watched folk to work, and since when it has.</summary>
        static float untilWork;
        static float workSince = -1f;
        /// <summary>"-untilaction" (A-71): the most seconds a shot waits past -wait for the watched one to be doing the
        /// action asked for in "-watch Deer:Pant" (watchAction).</summary>
        static float untilAction;
        static IdleAction watchAction;
        /// <summary>"-spawn Osprey" (or "Trout:6"): a few of a species put near the player once the world is up (testing).</summary>
        static readonly List<string> pendingSpawns = new List<string>();
        /// <summary>"-pose Bat" (or "Bat:fly", "Bat:glide", "Bat:rest", "Bat:hang", "Crow:nest", and a camera pitch: "Bat:glide:70"):
        /// one held beside the player while the camera circles it, for looking at a body (no brain: it stays put,
        /// beating its wings if flying).</summary>
        static string poseRequest;
        Entity posed;
        float posedAt;   // when the -pose specimen appeared (for "scare")
        float2? swimSpot;
        /// <summary>"-interact Cat:carry" (or ":pet"): walks up to the nearest one, pets it or picks it up, and puts it
        /// down again a while later, as if the keys were pressed (alone, hosting or joined).</summary>
        static string interactRequest;
        /// <summary>"-place ledge": waiting for the settlement's props to load round the player, then put by a rim.</summary>
        static bool ledgeRequest;
        /// <summary>
        /// With "-autowalk" after "-place tree:X" or "-place ledge": the player heads at the trunk (climbs it) or over the
        /// rim (jumps at it: mantles) for a while, then back to where it started, over and over, so a client that joins
        /// late still sees a climb (the multiplayer check of NetPlayerLook.Climbing and Rim).
        /// </summary>
        static float2? climbTarget;
        static float2 climbHome;
        static bool climbJumps;
        static float climbSince;
        static string shotDir;
        float interactStep = -1f;
        Entity interactTarget;
        EntityQuery creatureQuery, requestQuery;
        FaunaDirector interactDirector;
        World requestWorld;
        World creatureWorld;
        FaunaDirector watchDirector;
        // Flight check on the watched animal: its steepest climb or descent while airborne (vertical over horizontal speed).
        Vector3 watchLast;
        float watchSteepest;
        int watchAirFrames;
        Entity watched;
        CreatureMode watchMode;
        byte watchStage;
        int watchShots;
        float nextWatchPick;

        /// <summary>
        /// The test flags: the command line, plus in the editor whatever is in Temp/PlayArgs.txt (so Play mode can run
        /// the same unattended tests as a build, without starting another process).
        /// </summary>
        public static string[] LaunchArgs
        {
            get
            {
                var args = System.Environment.GetCommandLineArgs();
#if UNITY_EDITOR
                const string file = "Temp/PlayArgs.txt";
                if (System.IO.File.Exists(file))
                {
                    var extra = System.IO.File.ReadAllText(file).Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
                    var all = new string[args.Length + extra.Length];
                    args.CopyTo(all, 0);
                    extra.CopyTo(all, args.Length);
                    args = all;
                }
#endif
                return args;
            }
        }

#if UNITY_EDITOR
        // Play mode may start without a domain reload: start each run afresh.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            launchOptionsApplied = false;
            watchSpecies = null;
            watchPitchOverride = null;
            watchFrom = null;
            watchFromAt = null;
            walkTarget = null;
            walkPath.Clear();
            flyOn = false;
            flyRoute.Clear();
            flyPending = null;
            propPending = null;
            rockWatchFor = 0f;
            rockWatchSince = -1f;
            untilWork = 0f;
            untilAction = 0f;
            pendingSpawns.Clear();
            poseRequest = null;
            interactRequest = null;
            ledgeRequest = false;
            climbTarget = null;
            liftHeight = 0f;
            actionTest = null;
            wallRequest = false;
            Punchy.Player.PlayerInputSystem.TestCamera = null;
            shotDir = null;
            shotLines.Clear();
            shotIndex = -1;
            Time.timeScale = 1f;
            laterOptions = null;
        }
#endif

        /// <summary>
        /// Play-mode tests (W-97): test flags applied mid-session, as a shot line applies them ("-weather Rain -time 22"),
        /// on the next frame once the launch options are in.
        /// </summary>
        public static void RunOptions(string line) => laterOptions = line;

        static string laterOptions;

        /// <summary>
        /// For unattended tests: "-time 22" sets the clock, "-place summit" starts at the first preset location whose
        /// name contains the word (the list is logged).
        /// </summary>
        void ApplyLaunchOptions()
        {
            if (launchOptionsApplied || !EnsureLocations(out _))
                return;
            launchOptionsApplied = true;
            Debug.Log("DebugControls: places: " + string.Join(", ", locations.ConvertAll(l => l.Name)));
            ApplyOptions(LaunchArgs);
            // "-shotlist shots.txt": a list of shots to take one after another in this one session (see ShotList).
            var list = System.Array.IndexOf(LaunchArgs, "-shotlist");
            if (list >= 0 && list + 1 < LaunchArgs.Length)
                LoadShotList(LaunchArgs[list + 1]);
        }

        /// <summary>When the latest options were applied (spawns and poses wait for the world round them to settle).</summary>
        float optionsAt;
        /// <summary>How long that settling takes: the world's first build, or (a later shot) the nearby chunks after a move.</summary>
        float settle = 5f;

        // W-30: render counters, logged with each shot of a shot list.
        static Unity.Profiling.ProfilerRecorder drawCalls, batches, tris;
        static BlobAssetReference<ArchipelagoBlob> archipelagoKept;

        /// <summary>
        /// "-place HumanVillage" (by a landmark), "-place HumanVillage:centre" (in its middle) or "-place summit" (the first
        /// place whose name contains the word). False if no such place is on the island yet.
        /// </summary>
        bool PlaceAtLandmark(string name)
        {
            // An exact name first (the landmark itself), else the first that contains it.
            var index = locations.FindIndex(l => string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = locations.FindIndex(l => l.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (index < 0)
            {
                var middle = centres.FindIndex(l => string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase));
                if (middle < 0)
                    return false;
                index = locations.FindIndex(l => l.Name == centres[middle].Name);
                if (index < 0)
                {
                    locations.Add(centres[middle]);
                    index = locations.Count - 1;
                }
            }
            current = index - 1;
            Jump(1);
            Debug.Log($"DebugControls: started at {locations[index].Name}");
            return true;
        }

        /// <summary>A-74: "-place Landmark[:centre]" again whenever the island's landmarks change, until it times out.</summary>
        void PlaceAtLandmarkPending()
        {
            // (Only one not found yet: a place already found keeps the player there, so a -goto or -at after it holds.)
            if (landmarkPlacePending == null || landmarkPlaced)
                return;
            if (Time.unscaledTime > landmarkPlaceUntil)
            {
                Debug.LogWarning($"DebugControls: -place {landmarkPlacePending} gave up; no such place on the island");
                landmarkPlacePending = null;
                return;
            }
            if (!EnsureLocations(out _) || math.all(locationsKey == landmarkPlaceKey))
                return;
            landmarkPlaceKey = locationsKey;
            landmarkPlaced = PlaceAtLandmark(landmarkPlacePending);
        }

        void ApplyOptions(string[] args)
        {
            optionsAt = Time.timeSinceLevelLoad;
            landmarkPlacePending = null; // (a later shot's options replace an earlier -place still being retried)
            if (!drawCalls.Valid)
            {
                drawCalls = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Draw Calls Count");
                batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
                tris = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
            }
            // #25 (W-83): "-interiortest" builds one test room off the island (InteriorCells.TestRoom) and logs where.
            if (System.Array.IndexOf(args, "-interiortest") >= 0 && queryWorld != null && queryWorld.IsCreated &&
                terrainQuery.TryGetSingleton<TerrainSource>(out var roomSource))
                Debug.Log(InteriorCells.TestRoom(roomSource));
            // W-30: "-archipelago off|on" takes the archipelago out of the terrain or puts it back (before/after numbers from one build).
            var archArg = System.Array.IndexOf(args, "-archipelago");
            if (archArg >= 0 && archArg + 1 < args.Length)
            {
                EnsureLocations(out _);
                if (queryWorld != null && queryWorld.IsCreated && terrainQuery.TryGetSingleton<TerrainSource>(out var archSource))
                {
                    if (archSource.Archipelago.IsCreated)
                        archipelagoKept = archSource.Archipelago;
                    archSource.Archipelago = args[archArg + 1] == "off" ? default : archipelagoKept;
                    queryWorld.EntityManager.SetComponentData(terrainQuery.GetSingletonEntity(), archSource);
                }
            }
            // "-incave": on the floor of the nearest cave's deepest chamber (after any -place).
            var inCave = System.Array.IndexOf(args, "-incave") >= 0;
            footDrift = System.Array.IndexOf(args, "-footdrift") >= 0;
            var spinAt = System.Array.IndexOf(args, "-spincheck");
            spinCheck = spinAt >= 0;
            // ("-noflaresettle": fliers steer their flare the old way, for #12's before numbers from the same build.)
            Punchy.Fauna.CreatureBrainSystem.FlareSettleOff.Data = System.Array.IndexOf(args, "-noflaresettle") >= 0 ? 1 : 0;
            // A-22 (#23): "-renderpasslog" logs CreatureRenderSystem's time per pass; "-oldcull" culls the old (managed) way.
            Punchy.Fauna.CreatureRenderSystem.PassLog = System.Array.IndexOf(args, "-renderpasslog") >= 0;
            Punchy.Fauna.CreatureRenderSystem.OldCull = System.Array.IndexOf(args, "-oldcull") >= 0;
            if (spinCheck && spinAt + 1 < args.Length && ParseFloats(args[spinAt + 1]) is float[] spin && spin.Length >= 2)
            {
                spinDegrees = spin[0];
                spinSeconds = spin[1];
            }
            // "-actiontest roll:Human,Dog" (or fly:Pigeon, fish:Trout): the right face button's checks (A-04), logged, each
            // switching to the bodies named in turn (see ActionTest); "-setbody Lizard": switch to that body now.
            var testArg = System.Array.IndexOf(args, "-actiontest");
            if (testArg >= 0 && testArg + 1 < args.Length)
            {
                actionTest = args[testArg + 1];
                actionStart = Time.time;
                actionSlot = -1;
            }
            var bodyArg = System.Array.IndexOf(args, "-setbody");
            if (bodyArg >= 0 && bodyArg + 1 < args.Length)
                SetBody(args[bodyArg + 1]);
            // "-shotonhunt": one shot the moment a land hunter enters its sprint, camera framing hunter and prey (#9).
            shotOnHunt = System.Array.IndexOf(args, "-shotonhunt") >= 0;
            var huntSideArg = System.Array.IndexOf(args, "-huntside");
            huntSide = huntSideArg >= 0 && huntSideArg + 1 < args.Length && float.TryParse(args[huntSideArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var hs) ? hs : 0f;
            // A-23 (#7): "-huntpair" with -huntside aims at the middle of hunter and prey instead of the hunter, so a close
            // side-on view keeps both in frame.
            huntPair = System.Array.IndexOf(args, "-huntpair") >= 0;
            // A-21 (#6): "-leapphase 0.15" holds every leaping run (dogs, cats, wolves) at that point of its stride, for a
            // shot of each phase; "-hunter Dog" makes -shotonhunt wait for that species; "-gaitlog" logs each loaded
            // four-legged species' gallop (Leap, Bound or Rotary).
            var leapArg = System.Array.IndexOf(args, "-leapphase");
            CreatureAnimator.LeapFreeze.Data = leapArg >= 0 && leapArg + 1 < args.Length && float.TryParse(args[leapArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lp) ? math.frac(lp) + 1f : 0f;
            var hunterArg = System.Array.IndexOf(args, "-hunter");
            huntSpecies = hunterArg >= 0 && hunterArg + 1 < args.Length ? args[hunterArg + 1] : null;
            if (System.Array.IndexOf(args, "-gaitlog") >= 0)
                GaitLog();
            // "-remoteroll": a client frames, logs and shoots another player's somersault (#48).
            remoteRoll = System.Array.IndexOf(args, "-remoteroll") >= 0;
            // A-26: "-chattest" (launch line) makes folk stop to talk far more often (Punchy.Fauna.CreatureBrainSystem.ForceChat);
            // "-chatshot" frames the first pair talking side-on (ChatShot).
            if (System.Array.IndexOf(args, "-chattest") >= 0)
                Punchy.Fauna.CreatureBrainSystem.ForceChat.Data = 1;
            chatShot = System.Array.IndexOf(args, "-chatshot") >= 0;
            FolkTalkLabels.BubblesWithoutHud = chatShot;
            // A-27: "-chatmix" (launch line) logs what the townsfolk round the camera are doing, and the talks, every 15 s
            // (FolkTalkSystem.LogMix).
            Punchy.Fauna.FolkTalkSystem.ChatMix = System.Array.IndexOf(LaunchArgs, "-chatmix") >= 0;
            remoteRollStep = 0;
            // A-09 (#40): "-nocolossus" hides the Dunes colossus, "-framelog" logs frame times, "-colossusshot 300" puts the
            // player and camera that far from it.
            DunesColossus.Hidden = System.Array.IndexOf(args, "-nocolossus") >= 0;
            // A-10 (#41): "-nomantrap" hides the carnivorous plants, "-mantraptest" runs the 60 s scripted test on the first one,
            // "-mantrapshot side|lunge" frames it.
            Mantrap.Hidden = System.Array.IndexOf(args, "-nomantrap") >= 0;
            if (System.Array.IndexOf(args, "-mantraptest") >= 0)
                Mantrap.TestRequested = true;
            if (System.Array.IndexOf(args, "-mantrapstill") >= 0)
                Mantrap.StillRequested = true;
            var mmArg = System.Array.IndexOf(args, "-mimicshot");
            mimicShotMode = mmArg >= 0 && mmArg + 1 < args.Length ? args[mmArg + 1] : null;
            var mtArg = System.Array.IndexOf(args, "-mantrapshot");
            mantrapShotMode = mtArg >= 0 && mtArg + 1 < args.Length ? args[mtArg + 1] : null;
            DunesColossus.FrameLog = System.Array.IndexOf(args, "-framelog") >= 0;
            // A-72: "-walkershot Saguaro,80,side" frames a walking colossus (see WalkerShot).
            var wsArg = System.Array.IndexOf(args, "-walkershot");
            ColossusWalker.Shot = wsArg >= 0 && wsArg + 1 < args.Length ? args[wsArg + 1] : null;
            var wpArg = System.Array.IndexOf(args, "-walkerphase");
            ColossusWalker.Phase = wpArg >= 0 && wpArg + 1 < args.Length ? args[wpArg + 1] : null;
            var colArg = System.Array.IndexOf(args, "-colossusshot");
            colossusShotDistance = colArg >= 0 && colArg + 1 < args.Length && float.TryParse(args[colArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cd) ? cd : 0f;
            // "-huntlog": log land hunters' chases (Z-53).
            huntLog = System.Array.IndexOf(args, "-huntlog") >= 0;
            // "-personalitylog Dog": list each of that species' personality a few seconds in (#50).
            var plArg = System.Array.IndexOf(args, "-personalitylog");
            personalitySpecies = plArg >= 0 && plArg + 1 < args.Length ? args[plArg + 1] : null;
            // "-comfort": log weather comfort meters every 5 s; "-comfort off" disables the brain's comfort check (A-71).
            var cfArg = System.Array.IndexOf(args, "-comfort");
            comfortLog = cfArg >= 0;
            comfortNext = 6f; // (statics outlive a play with domain reload off)
            Comfort.Off.Data = cfArg >= 0 && cfArg + 1 < args.Length && args[cfArg + 1] == "off" ? 1 : 0;
            // "-weathertalk": log each weather line folk say, with its cell and how many are up there (A-71 row 4).
            WeatherTalkSystem.LogLines = System.Array.IndexOf(args, "-weathertalk") >= 0;
            // "-comfortheat -0.3": shifts the comfort grid's heat (a forced cold snap or heat wave for comfort shots).
            var chArg = System.Array.IndexOf(args, "-comfortheat");
            Comfort.HeatShift.Data = chArg >= 0 && chArg + 1 < args.Length && float.TryParse(args[chArg + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var chv) ? chv : 0f;
            // "-thirsty" (A-49 #64): every creature's thirst is topped up for this line, so drinking shows up soon.
            thirsty = System.Array.IndexOf(args, "-thirsty") >= 0;
            personalityAt = -1f;
            // "-lift 15": the player held that high over the ground (see Lift), per shot line.
            var liftArg = System.Array.IndexOf(args, "-lift");
            liftHeight = liftArg >= 0 && liftArg + 1 < args.Length && float.TryParse(args[liftArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lh) ? lh : 0f;
            liftNext = Time.time + 4f; // first sample 4 s in, once the spawned herd has settled
            // "-spawnradius 12": this line's -spawn puts land animals at most that far out (default 45 m; #46 needs deer in flee range).
            var srArg = System.Array.IndexOf(args, "-spawnradius");
            spawnRadius = srArg >= 0 && srArg + 1 < args.Length && float.TryParse(args[srArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sr) ? sr : 45f;
            liftSample = 0;
            // "-liftpass 8" (A-47 #46): with -lift, the player starts 40 m west and 10 m north of where it stood, and 5 s in
            // runs due east at that speed (m/s) for 80 m: a moving threat passing the herd 10 m off, on the ground or high up.
            var lpArg = System.Array.IndexOf(args, "-liftpass");
            liftPass = lpArg >= 0 && lpArg + 1 < args.Length && float.TryParse(args[lpArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lpv) ? lpv : 0f;
            liftPassStart = null;
            // "-nobrainlod" (A-47 #59): every creature thinks every frame. "-fixeddt" (#61): Play mode steps a fixed 1/30 s,
            // so two runs of the same line can be compared number for number.
            Punchy.Fauna.CreatureBrainSystem.BrainLodOff.Data = System.Array.IndexOf(args, "-nobrainlod") >= 0 ? 1 : 0;
            // "-brainlod 60,3" (A-54 #59): the LOD's range (m) and frames per think for this line (default 80 m, every 4th).
            var lodArg = System.Array.IndexOf(args, "-brainlod");
            Punchy.Fauna.CreatureBrainSystem.BrainLod.Data = lodArg >= 0 && lodArg + 1 < args.Length && ParseFloats(args[lodArg + 1]) is float[] lod && lod.Length >= 2
                ? new float2(lod[0], lod[1]) : Punchy.Fauna.CreatureBrainSystem.BrainLodDefault;
            Time.captureFramerate = System.Array.IndexOf(args, "-fixeddt") >= 0 ? 30 : 0;
            fixedFrames = Time.captureFramerate > 0 ? 0 : -1;
            fixedSearch0 = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            // #46/#43 tests: "-flatfear" (plain 3D fear distance), "-nobiometable" (every biome has every sound),
            // "-callrate 1.5" (the call budget a second), "-calllog" (calls played a 10 s window, with the biome).
            Punchy.Fauna.CreatureBrainSystem.HeightFearOff.Data = System.Array.IndexOf(args, "-flatfear") >= 0 ? 1 : 0;
            // A-46: "-oldwander" (#12, walkers ease down while turning, as before); flees are counted from each line's start
            // (logged with the shot, #57's fair check).
            Punchy.Fauna.CreatureBrainSystem.OldWander.Data = System.Array.IndexOf(args, "-oldwander") >= 0 ? 1 : 0;
            // A-46b: "-oldplay" (#12, a playing runner re-aims freely each frame, as before).
            Punchy.Fauna.CreatureBrainSystem.OldPlay.Data = System.Array.IndexOf(args, "-oldplay") >= 0 ? 1 : 0;
            lineSearch0 = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            // "-pickclass Fish" (#68): what one click on the Character tab's class does; "-menu Character": the menu open
            // on that tab for this line's shot (closed again on lines without it).
            var pickAt = System.Array.IndexOf(args, "-pickclass");
            if (pickAt >= 0 && pickAt + 1 < args.Length && System.Enum.TryParse<Punchy.Net.CharacterTab.BodyClass>(args[pickAt + 1], out var pickClass))
            {
                var pickAvatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
                if (pickAvatars != null)
                    Punchy.Net.CharacterTab.PickClass(pickAvatars, pickClass);
            }
            // A-74 (#8): "-pickspecies Elf": the player in that playable body (not remembered), for race reaction shots.
            var speciesAt = System.Array.IndexOf(args, "-pickspecies");
            if (speciesAt >= 0 && speciesAt + 1 < args.Length && FindAnyObjectByType<Punchy.Net.NetAvatars>() is Punchy.Net.NetAvatars speciesAvatars)
            {
                var index = speciesAvatars.PlayableSpecies.FindIndex(sp => sp != null && string.Equals(sp.name, args[speciesAt + 1], System.StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    speciesAvatars.SetLocalSpecies(index);
                else
                    Debug.LogWarning($"DebugControls: -pickspecies {args[speciesAt + 1]}: no such playable body");
            }
            var menuAt = System.Array.IndexOf(args, "-menu");
            Punchy.UI.MainMenu.Show(menuAt >= 0 && menuAt + 1 < args.Length && !args[menuAt + 1].StartsWith("-") ? args[menuAt + 1] : null, menuAt >= 0);
            Punchy.Fauna.AmbientSounds.TableOff.Data = System.Array.IndexOf(args, "-nobiometable") >= 0 ? 1 : 0;
            var rateArg = System.Array.IndexOf(args, "-callrate");
            if (rateArg >= 0 && rateArg + 1 < args.Length && float.TryParse(args[rateArg + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cr))
                Punchy.Audio.CreatureAudioSystem.CallRate = cr;
            callLog = System.Array.IndexOf(args, "-calllog") >= 0;
            callLogNext = Time.time + 10f;
            callLogFrom = Punchy.Audio.CreatureAudioSystem.CallsPlayed;
            // "-voicelog": log cats' and dogs' voices and howls as they play (Z-51); "-stancelog": folk lines with the
            // speaker's stance, and the stance odds per player body (Z-52).
            if (System.Array.IndexOf(args, "-voicelog") >= 0)
                Punchy.Audio.CreatureAudioSystem.LogVoices = true;
            if (System.Array.IndexOf(args, "-stancelog") >= 0)
                FolkTalkSystem.LogStances = true;
            // "-nohud": no readout and no interaction prompt (for screenshots). "-hud": start with the readout on.
            ShowReadout = System.Array.IndexOf(args, "-hud") >= 0;
            // "-meteors": every night a meteor shower (Sky.hlsl's test switch), for checking shooting stars against clouds (Z-09).
            if (System.Array.IndexOf(args, "-meteors") >= 0)
                Shader.SetGlobalFloat("_PunchySkyTest", 1f);
            Punchy.Player.PlayerMovementSystem.NoPushOut.Data = System.Array.IndexOf(args, "-nopushout") >= 0;
            // "-oldlie" (A-42): lying four-legged bodies' legs left where they fall, for before shots from the same build.
            CreatureAnimator.OldLie.Data = System.Array.IndexOf(args, "-oldlie") >= 0 ? 1 : 0;
            // "-solidsoff" (A-44): wild animals ignore cave walls, towers and carved ground as before, for before numbers
            // from the same build. "-solidstats": log their contacts, sink and floating every 60 s (SolidStatsLog).
            CreatureMotion.SolidsOff.Data = System.Array.IndexOf(args, "-solidsoff") >= 0 ? 1 : 0;
            CreatureMotion.StatsOn.Data = System.Array.IndexOf(args, "-solidstats") >= 0 ? 1 : 0;
            // "-redstar": one red-orange shooting star held mid-flight high in the east, for shots (W-42).
            if (System.Array.IndexOf(args, "-redstar") >= 0)
                Shader.SetGlobalFloat("_PunchySkyTest", 2f);
            if (System.Array.IndexOf(args, "-nohud") >= 0)
            {
                ShowReadout = false;
                noHud = true;
                InteractionPrompt.Hidden = true;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-time" && Cycle != null && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours))
                    Cycle.TimeOfDay = Mathf.Repeat(hours, 24f);
                if (args[i] == "-watch")
                    watchSpecies = args[i + 1];
                // "-viewpitch 60": the player's own camera looking down from that angle (-20: up), as if the mouse moved.
                if (args[i] == "-viewpitch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var viewPitch))
                {
                    var rig = FindAnyObjectByType<ThirdPersonCamera>();
                    if (rig != null)
                        rig.SetView(rig.transform.eulerAngles.y, viewPitch);
                }
                // "-view yaw,pitch": the player's own camera turned that way (A-30: aimed at a cave wall).
                if (args[i] == "-view" && ParseFloats(args[i + 1]) is float[] view && view.Length >= 2 && FindAnyObjectByType<ThirdPersonCamera>() is ThirdPersonCamera viewRig)
                    viewRig.SetView(view[0], view[1]);
                // "-watchpitch 45": watch from above (to see over leaves), rather than low or level.
                if (args[i] == "-watchpitch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var watchPitch))
                    watchPitchOverride = watchPitch;
                if (args[i] == "-watchfrom" && ParseFloats(args[i + 1]) is float[] from && from.Length >= 2)
                    watchFrom = new float2(from[0], from[1]);
                // "-walk x,y,z" (A-34): the player walks toward that point from where -goto put it (as if the stick were
                // pushed), so the cave push-out acts on its steps; the shot lands mid-walk at -wait.
                // W-61b: "-walk x,z;x,z;x,z" (or x,y,z points) walks the waypoints in order, one leg at a time.
                if (args[i] == "-walk" && ParseWalkPath(args[i + 1]) is float3 firstLeg)
                {
                    walkTarget = firstLeg;
                    walkEnd = null;
                    walkStillAt = float.MaxValue;
                    walkStillSince = Time.unscaledTime;
                    walkMoves = 0;
                    walkLogNext = 0f;
                    Punchy.Player.PlayerMovementSystem.PushedTotal.Data = 0f;
                }
                // "-untilwork 60" (A-34): after -wait, hold the shot up to that many seconds more until the watched folk
                // has been at its work (Mode Work) for a second, so a smith on a break is caught hammering.
                if (args[i] == "-untilwork" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var untilWorkFor))
                    untilWork = untilWorkFor;
                if (args[i] == "-untilaction" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var untilActionFor))
                    untilAction = untilActionFor;
                // "-timescale 4": everything runs four times as fast (a dusk, a burrow cycle, a roost in seconds).
                if (args[i] == "-timescale" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
                    Time.timeScale = Mathf.Clamp(scale, 0.1f, 20f);
                if (args[i] == "-spawn")
                    pendingSpawns.Add(args[i + 1]);
                if (args[i] == "-pose")
                    poseRequest = args[i + 1];
                if (args[i] == "-interact")
                    interactRequest = args[i + 1];
                // "-emote Wave" (Wave, Bow, Cheer, Dance): as if its key were pressed.
                if (args[i] == "-emote" && System.Enum.TryParse<IdleAction>(args[i + 1], true, out var emote) && emote >= IdleAction.Wave)
                    InteractionInputSystem.Requested = emote;
                // "-shotdir C:\shots": tests that take their own screenshots (-interact) save them there.
                if (args[i] == "-shotdir")
                    shotDir = args[i + 1];
                // "-cam x,y,z,yaw,pitch" or "-camlook x,y,z,tx,ty,tz": the camera put there directly (its follow off till
                // the next shot), for framing a shot exactly.
                if ((args[i] == "-cam" || args[i] == "-camlook") && ParseFloats(args[i + 1]) is float[] c && c.Length >= 5)
                    PoseCamera(new Vector3(c[0], c[1], c[2]), args[i] == "-cam"
                        ? Quaternion.Euler(c[4], c[3], 0f)
                        : Quaternion.LookRotation(new Vector3(c[3], c[4], c.Length > 5 ? c[5] : 0f) - new Vector3(c[0], c[1], c[2])));
                if (args[i] == "-place" && args[i + 1].StartsWith("cave:", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var caveSource))
                    PlaceInCave(caveSource, args[i + 1].Substring(5));
                if (args[i] == "-rockwatch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rockSeconds))
                {
                    rockWatchFor = rockSeconds;
                    rockWatchSince = -1f;
                }
                if (args[i] == "-flyhitch" && int.TryParse(args[i + 1], out var hitchMs))
                    flyHitchMs = hitchMs;
                if (args[i] == "-flycave" && EnsureLocations(out var flySource))
                    StartFlyCave(flySource, args[i + 1]);
                if (args[i] == "-place" && args[i + 1].StartsWith("batfolk", System.StringComparison.OrdinalIgnoreCase))
                    batFolkPlaceUntil = EnsureLocations(out var batSource) && PlaceAtBatFolk(batSource) ? 0f : Time.unscaledTime + 60f;
                else if (args[i] == "-place" && args[i + 1].StartsWith("tree:", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var treeSource))
                    PlaceByTree(treeSource, args[i + 1].Substring(5));
                else if (args[i] == "-place" && args[i + 1].StartsWith("prop:", System.StringComparison.OrdinalIgnoreCase))
                {
                    // (#137: kept until the flora lookup is up, which is built on workers since #102; Update retries it.)
                    propPending = args[i + 1].Substring(5);
                    propPendingUntil = Time.unscaledTime + PropWait;
                    if (EnsureLocations(out var propSource) && PlaceByProp(propSource, propPending))
                        propPending = null;
                }
                else if (args[i] == "-place" && args[i + 1].StartsWith("cavestream", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var streamSource))
                    PlaceByCaveStream(streamSource, args[i + 1].Substring(10));
                else if (args[i] == "-place" && args[i + 1].StartsWith("riverfall", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var fallSource))
                    PlaceByRiverFall(fallSource, args[i + 1].Substring(9));
                else if (args[i] == "-place" && string.Equals(args[i + 1], "meadow", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var meadowSource))
                    PlaceInMeadow(meadowSource);
                else if (args[i] == "-place" && string.Equals(args[i + 1], "crossing", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var crossingSource))
                    PlaceByCrossing(crossingSource);
                else if (args[i] == "-place" && string.Equals(args[i + 1], "clearlake", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var lakeSource))
                    PlaceByClearLake(lakeSource);
                else if (args[i] == "-place" && (string.Equals(args[i + 1], "ledge", System.StringComparison.OrdinalIgnoreCase) || args[i + 1].StartsWith("wall", System.StringComparison.OrdinalIgnoreCase)))
                {
                    // ("wall": the same, for a wall-climbing body: the nearest wall of a building or rock, not a rim to mantle.)
                    wallRequest = args[i + 1].StartsWith("wall", System.StringComparison.OrdinalIgnoreCase);
                    // ("wall:Lizard": as that body; A-13 #33.)
                    if (wallRequest && args[i + 1].IndexOf(':') > 0)
                        SetBody(args[i + 1].Substring(args[i + 1].IndexOf(':') + 1));
                    // To the nearest settlement first: its buildings, wells and stalls have the ledges (PlaceByLedge,
                    // once their tops have loaded round the player).
                    var town = locations.FindIndex(l => l.Name.StartsWith("Nearest settlement"));
                    if (town >= 0)
                    {
                        current = town - 1;
                        Jump(1);
                    }
                    ledgeRequest = true;
                    wallVisited.Clear();
                    wallTallest = 0f;
                    wallSearchAt = 0f;
                }
                // P-5/P-7: "-place temple" / "temple:N": on a sunken temple room's floor (for camera shots underground, #85).
                else if (args[i] == "-place" && args[i + 1].StartsWith("temple", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var templeSource))
                    PlaceInTemple(templeSource, args[i + 1]);
                else if (args[i] == "-place")
                {
                    landmarkPlacePending = args[i + 1];
                    landmarkPlaceUntil = Time.unscaledTime + 120f;
                    landmarkPlaceKey = locationsKey;
                    landmarkPlaced = PlaceAtLandmark(args[i + 1]);
                }
            }
            if (inCave && EnsureLocations(out var source))
                GoIntoCave(source);
            // "-goto 120,-340" (x,z, as F3 copies it): there, on the ground (after any -place).
            var goTo = System.Array.IndexOf(args, "-goto");
            if (goTo >= 0 && goTo + 1 < args.Length && EnsureLocations(out var ground) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var goer))
            {
                var parts = args[goTo + 1].Split(',');
                if (parts.Length >= 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gx) &&
                    float.TryParse(parts[^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gz))
                {
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(goer);
                    t.Position = new float3(gx, ground.SampleHeight(new float2(gx, gz)) + 0.1f, gz);
                    // "-goto x,y,z": onto the floor under that point (in a cave), not the ground surface.
                    if (parts.Length >= 3 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gy))
                        t.Position.y = ground.FloorUnder(new float3(gx, gy + 1f, gz), out _) + 0.05f;
                    em.SetComponentData(goer, t);
                    Debug.Log($"DebugControls: to {gx:F0},{t.Position.y:F1},{gz:F0}");
                    // W-67b: the kit boxes there may not be placed yet (tiles still loading after the jump): wait for them.
                    gotoSettle = new float3(gx, parts.Length >= 3 ? t.Position.y - 0.05f : float.NaN, gz);
                    gotoSettleUntil = Time.unscaledTime + 5f;
                }
            }
            // #72 (W-87): "-campsitetest swamp": 9 m west of the test campsite the bake added (CampsiteTest), facing it.
            if (goTo < 0 && System.Array.IndexOf(args, "-campsitetest") >= 0 && !math.any(math.isnan(Punchy.Terrain.CampsiteTest.At)) &&
                EnsureLocations(out var campGround) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var camper))
            {
                var c = Punchy.Terrain.CampsiteTest.At - new float2(9f, 0f);
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(camper);
                t.Position = new float3(c.x, campGround.SampleHeight(c) + 0.1f, c.y);
                t.Rotation = quaternion.LookRotationSafe(new float3(1f, 0f, 0f), math.up());
                em.SetComponentData(camper, t);
                Debug.Log($"DebugControls: beside the test campsite, {Punchy.Terrain.CampsiteTest.LastLog}");
                gotoSettle = new float3(c.x, float.NaN, c.y);
                gotoSettleUntil = Time.unscaledTime + 5f;
            }
            // "-gocave -1669,2494" (x,z): onto the floor at the cave node nearest that spot in plan, at any depth.
            // "-gocave #2": the second-largest cave (by summed chamber volume), at its biggest node.
            var goCave = System.Array.IndexOf(args, "-gocave");
            if (goCave >= 0 && goCave + 1 < args.Length && EnsureLocations(out var goCaveSource) && goCaveSource.Caves.IsCreated &&
                playerQuery.TryGetSingletonEntity<LocalTransform>(out var caver))
                GoToCave(goCaveSource, caver, args[goCave + 1]);
            // Testing toggles, per shot line: "-oldcave" (every cave chunk drawn, managed camera probe, as before W-48),
            // "-nocavewalls" (no cave walls drawn, to price them), "-perflog" (frame costs over the last 3 s of the wait).
            CaveSystem.NoRoomCull = ThirdPersonCamera.OldProbe = System.Array.IndexOf(args, "-oldcave") >= 0;
            CaveSystem.NoWalls = System.Array.IndexOf(args, "-nocavewalls") >= 0;
            // "-oldtrace" (#81): TraceSystem's managed per-creature ground lookups, as before W-66.
            Punchy.Fauna.TraceSystem.OldTrace = System.Array.IndexOf(args, "-oldtrace") >= 0;
            perfLog = System.Array.IndexOf(args, "-perflog") >= 0;
            // A-45 A/B toggles: "-oldsearch" (#57, every brain search every frame), "-oldsnapshot" (#56, each reader copies
            // the creatures itself), "-oldtrace" (#55, the ground sampled for every creature in trace range).
            // (-oldsearch also holds from the command line or PlayArgs through every shot-list line: A-55's A/B lost it
            // when the first shot line, without the flag, switched it back off.)
            var oldSearch = System.Array.IndexOf(args, "-oldsearch") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldsearch") >= 0 ? 1 : 0;
            Debug.Log($"DebugControls: -oldsearch {(oldSearch != 0 ? "ON (every brain search every frame)" : "off (staggered searches)")}");
            Punchy.Fauna.CreatureBrainSystem.SearchEveryFrame.Data = oldSearch;
            // #132 A/B: "-oldwalls" places every landmark prop within 15 m for each wall check, as before.
            FloraPlacement.OldWalls.Data = System.Array.IndexOf(args, "-oldwalls") >= 0 ? 1 : 0;
            // A-70 row 1 A/B: "-oldfootprints" places buildings again for every wall check, as before; "-footprintcheck"
            // asks FloraPlacement.Blocked too and counts any disagreement with the baked walls (logged with perf2).
            // (Both also hold from PlayArgs through every shot-list line, as -oldsearch does.)
            Punchy.Fauna.BuildingFootprints.Off.Data = System.Array.IndexOf(args, "-oldfootprints") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldfootprints") >= 0 ? 1 : 0;
            Punchy.Fauna.BuildingFootprints.Check.Data = System.Array.IndexOf(args, "-footprintcheck") >= 0 || System.Array.IndexOf(LaunchArgs, "-footprintcheck") >= 0 ? 1 : 0;
            Punchy.Fauna.CreatureSnapshotSystem.Off = System.Array.IndexOf(args, "-oldsnapshot") >= 0;
            // A-70 row 3 A/B: "-syncsnapshot", the snapshot copied on the main thread (waiting for the brain), as before.
            Punchy.Fauna.CreatureSnapshotSystem.Sync = System.Array.IndexOf(args, "-syncsnapshot") >= 0 || System.Array.IndexOf(LaunchArgs, "-syncsnapshot") >= 0;
            // A-70 row 4 A/B: "-oldposerate", every creature posed every frame with the full capsule and no frustum cull.
            Punchy.Fauna.CreatureRenderSystem.OldPoseRate = System.Array.IndexOf(args, "-oldposerate") >= 0 || System.Array.IndexOf(LaunchArgs, "-oldposerate") >= 0;
            Punchy.Fauna.TraceSystem.OldOrder = System.Array.IndexOf(args, "-oldtrace") >= 0;
            Punchy.Fauna.TraceSystem.Stats = perfLog;
            Punchy.Fauna.ReaderTimes.On = perfLog;
            Punchy.Fauna.CreatureBrainSystem.SpreadOn.Data = perfLog ? 1 : 0;
            CaveSystem.CountStats = ThirdPersonCamera.ProbeStats = perfLog;
            // "-goflat" (A-13 #46): to the nearest dry spot whose ground height spread over a 30 m circle is under 0.5 m
            // (found once, by rings round the start; the spot and its measured spread are logged).
            if (System.Array.IndexOf(args, "-goflat") >= 0 && EnsureLocations(out var flatSource) && playerQuery.TryGetSingletonEntity<LocalTransform>(out var flatGoer))
            {
                // ("-goflat 2": the third such spot, each at least 150 m from the ones before, so a test line gets a
                // fresh herd away from the last line's, A-49 #46.)
                var flatAt = System.Array.IndexOf(args, "-goflat");
                var flatIndex = flatAt + 1 < args.Length && int.TryParse(args[flatAt + 1], out var fi) ? math.max(fi, 0) : 0;
                while (flatSpots.Count <= flatIndex && FindFlatSpot(flatSource, out var foundSpread, flatSpots) is float2 foundFlat)
                {
                    flatSpots.Add(foundFlat);
                    flatSpreads.Add(foundSpread);
                }
                flatSpot = flatSpots.Count > flatIndex ? flatSpots[flatIndex] : (float2?)null;
                flatSpread = flatSpot.HasValue ? flatSpreads[flatIndex] : 0f;
                if (flatSpot.HasValue)
                {
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(flatGoer);
                    var f = flatSpot.Value;
                    t.Position = new float3(f.x, flatSource.SampleHeight(f) + 0.1f, f.y);
                    em.SetComponentData(flatGoer, t);
                    Debug.Log($"DebugControls: flat spot at ({f.x:F0}, {f.y:F0}), ground {flatSource.SampleHeight(f):F1} m, height spread {flatSpread:F2} m over 30 m");
                }
                else
                    Debug.LogWarning("DebugControls: -goflat: no flat spot found");
            }
            // "-at Pool" (any AnchorKind): on the floor by the nearest anchor of that kind (after any -place).
            var at = System.Array.IndexOf(args, "-at");
            if (at >= 0 && at + 1 < args.Length && System.Enum.TryParse<AnchorKind>(args[at + 1], true, out var kind) &&
                EnsureLocations(out var terrain))
                GoToAnchor(terrain, kind);
            // "-warp town 2" (biome, town, cave or landmark; the number defaults to 1): the warp key's Nth target counted
            // from the nearest, as if the key were pressed that many times from here (A-36).
            var warpArg = System.Array.IndexOf(args, "-warp");
            if (warpArg >= 0 && warpArg + 1 < args.Length && WarpCategory(args[warpArg + 1]) is int warpCat && warpCat >= 0)
            {
                var nth = warpArg + 2 < args.Length && int.TryParse(args[warpArg + 2], out var wn) ? math.max(1, wn) : 1;
                warpIndex[warpCat] = -1;
                Warp(warpCat, 1);
                for (int k = 1; k < nth; k++)
                    Warp(warpCat, 1);
            }
            // "-weather Storm" (any WeatherPreset; Clear clears): that weather forced over the player, where it ends up.
            var weatherArg = System.Array.IndexOf(args, "-weather");
            if (weatherArg >= 0 && weatherArg + 1 < args.Length && QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var under) &&
                FindAnyObjectByType<WeatherSystem>() is WeatherSystem weather)
                weather.ForceAt(args[weatherArg + 1], new Vector2(under.Position.x, under.Position.z));
            // "-waterprobe 1" (2, 3; 0 off): the water drawn as its foam, its thickness over the bed, or its alpha.
            var probeArg = System.Array.IndexOf(args, "-waterprobe");
            if (probeArg >= 0 && probeArg + 1 < args.Length && float.TryParse(args[probeArg + 1], out var probe))
                Shader.SetGlobalFloat("_PunchyWaterProbe", probe);
            // "-camrel dx,dy,dz,tx,ty,tz": like -camlook, but both points relative to where the player ends up (after any
            // -place), for framing a place whose coordinates aren't known.
            var camRel = System.Array.IndexOf(args, "-camrel");
            if (camRel >= 0 && camRel + 1 < args.Length && ParseFloats(args[camRel + 1]) is float[] rel && rel.Length >= 6 &&
                QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var origin))
            {
                Vector3 camFrom = (Vector3)origin.Position + new Vector3(rel[0], rel[1], rel[2]);
                Vector3 camTo = (Vector3)origin.Position + new Vector3(rel[3], rel[4], rel[5]);
                PoseCamera(camFrom, Quaternion.LookRotation(camTo - camFrom));
                Debug.Log($"DebugControls: camera {camFrom.x:F1},{camFrom.y:F1},{camFrom.z:F1} looking at {camTo.x:F1},{camTo.y:F1},{camTo.z:F1}");
            }
        }

        /// <summary>Puts the player on the floor beside the nearest anchor of a kind, anywhere in the world.</summary>
        void GoToAnchor(in TerrainSource source, AnchorKind kind)
        {
            if (!source.Anchors.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            ref var anchors = ref source.Anchors.Value.Anchors;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player);
            var best = -1;
            for (int i = 0; i < anchors.Length; i++)
                if (anchors[i].Kind == kind && (best < 0 || math.distancesq(anchors[i].Position, at.Position) < math.distancesq(anchors[best].Position, at.Position)))
                    best = i;
            if (best < 0)
            {
                Debug.LogWarning($"DebugControls: no {kind} anchors");
                return;
            }
            var anchor = anchors[best];
            // A step back from it, the way it faces (so not in the water or the plant), on whatever floor is there.
            var p = anchor.Position - new float3(anchor.Facing.x, 0f, anchor.Facing.y) * (anchor.Size + 1f);
            var floor = source.FloorUnder(p + new float3(0f, 0.5f, 0f), out var inCave);
            at.Position = new float3(p.x, inCave ? floor + 0.05f : source.SampleHeight(p.xz), p.z);
            em.SetComponentData(player, at);
            Debug.Log($"DebugControls: at {kind} anchor {best} ({anchor.Position.x:F0}, {anchor.Position.y:F0}, {anchor.Position.z:F0}), in cave {anchor.InCave}");
        }

        /// <summary>Puts the player on the floor of the deepest chamber of the cave whose mouth is nearest.</summary>
        void GoIntoCave(in TerrainSource source)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            ref var set = ref source.Caves.Value;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player);
            var best = -1;
            for (int i = 0; i < set.Caves.Length; i++)
                if (set.Caves[i].NodeCount >= 2 && (best < 0 || math.distance(set.Caves[i].Mouth, at.Position) < math.distance(set.Caves[best].Mouth, at.Position)))
                    best = i;
            if (best < 0)
                return;
            ref var cave = ref set.Caves[best];
            var node = set.Nodes[cave.FirstNode + cave.NodeCount - 1];
            var floor = source.FloorUnder(node.Position, out var inCave);
            at.Position = new float3(node.Position.x, inCave ? floor + 0.05f : node.Position.y, node.Position.z);
            em.SetComponentData(player, at);
            Debug.Log($"DebugControls: into cave {best}, chamber floor at ({at.Position.x:F0}, {at.Position.y:F0}, {at.Position.z:F0})");
        }

        /// <summary>
        /// Spawns the "-spawn" requests near the player (where the game simulates animals: alone or hosting), each on
        /// ground or in water to suit it, a few seconds after the world is up.
        /// </summary>
        void SpawnRequested()
        {
            if (pendingSpawns.Count == 0 || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null)
                return;
            if (!director.CanSpawn)
            {
                // A joined client doesn't simulate animals: only a host or a solo game can spawn them.
                Debug.LogWarning("DebugControls: -spawn needs a solo or hosting game");
                pendingSpawns.Clear();
                return;
            }
            var rng = new Unity.Mathematics.Random(1234u);
            foreach (var request in pendingSpawns)
            {
                var parts = request.Split(':');
                var count = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 3;
                var index = director.SpeciesIndex(parts[0]);
                if (index < 0)
                {
                    Debug.LogWarning($"DebugControls: no species '{parts[0]}' to spawn");
                    continue;
                }
                var wantsWater = director.SpeciesHabitat(index) == Habitat.Swims || director.SpeciesHabitat(index) == Habitat.Floats;
                var spawned = 0;
                // Cave dwellers go on the cave floor round the player (who should be in a cave: -incave, -at), and so
                // do cave visitors when the player is in one.
                source.FloorUnder(player.Position + new float3(0f, 0.5f, 0f), out var playerInCave);
                if (director.SpeciesCaveDweller(index) || director.SpeciesVisitsCaves(index) && playerInCave)
                {
                    for (int attempt = 0; attempt < 400 && spawned < count; attempt++)
                    {
                        var xz = player.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(2f, 15f);
                        var floor = source.FloorUnder(new float3(xz.x, player.Position.y + 0.5f, xz.y), out var inCave);
                        if (!inCave || math.abs(floor - player.Position.y) > 2f || source.CaveDistance(new float3(xz.x, floor + 0.3f, xz.y)) >= -0.05f)
                            continue;
                        director.Spawn(index, new float3(xz.x, floor, xz.y));
                        spawned++;
                    }
                    Debug.Log($"DebugControls: spawned {spawned} {parts[0]} on the cave floor");
                    continue;
                }
                for (int attempt = 0; attempt < 400 && spawned < count; attempt++)
                {
                    var xz = player.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(math.min(6f, spawnRadius * 0.5f), spawnRadius);
                    var g = source.SampleGround(xz);
                    var depth = g.HasWater ? g.WaterSurface - g.Height : -1f;
                    if (wantsWater ? depth < 1f : depth > 0f)
                        continue;
                    director.Spawn(index, new float3(xz.x, wantsWater ? g.WaterSurface - math.min(depth * 0.45f, 0.6f) : g.Height, xz.y));
                    spawned++;
                }
                Debug.Log($"DebugControls: spawned {spawned} {parts[0]}");
            }
            pendingSpawns.Clear();
        }

        /// <summary>
        /// Parses "-walk": one point "x,y,z" (the old form) or "x,z", or waypoints split by ';'. Fills walkPath with
        /// the legs after the first and returns the first, or null if any point does not parse.
        /// </summary>
        static float3? ParseWalkPath(string text)
        {
            walkPath.Clear();
            walkLeg = 0;
            foreach (var part in text.Split(';'))
            {
                if (part.Length == 0)
                    continue;
                var p = ParseFloats(part);
                if (p == null || p.Length < 2)
                {
                    walkPath.Clear();
                    return null;
                }
                walkPath.Add(p.Length >= 3 ? new float3(p[0], p[1], p[2]) : new float3(p[0], 0f, p[1]));
            }
            if (walkPath.Count == 0)
                return null;
            var first = walkPath[0];
            walkPath.RemoveAt(0);
            return first;
        }

        /// <summary>
        /// W-67b: after a -goto, once kit boxes reach the point, lifts the player onto the first kit floor over the
        /// ground there (a wharf deck, not the seabed under it). With an explicit "x,y,z" it stays on the floor under
        /// that height, so a forced under-deck walk is still possible. Gives up after 5 s (no kit there).
        /// </summary>
        void SettleGoto()
        {
            if (!gotoSettle.HasValue || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var goer))
                return;
            var spot = gotoSettle.Value;
            var em = queryWorld.EntityManager;
            using var kitQuery = em.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            var ready = kitQuery.TryGetSingletonBuffer<KitBox>(out var kit, true) && KitCollision.AnyNear(kit, spot.xz, 0.5f);
            if (!ready)
            {
                if (Time.unscaledTime > gotoSettleUntil)
                {
                    Debug.Log($"DebugControls: goto no kit boxes at {V(spot.x)},{V(spot.z)} after 5 s");
                    gotoSettle = null;
                }
                return;
            }
            gotoSettle = null;
            var t = em.GetComponentData<LocalTransform>(goer);
            var ground = t.Position.y;
            float top;
            var found = float.IsNaN(spot.y)
                ? KitCollision.LowestTopAt(kit, spot.xz, ground - 0.3f, 1.7f, out top, RoofGap(spot.xz))
                : KitCollision.TopAt(kit, spot.xz, spot.y + 1f, 1.7f, out top);
            if (found && top > ground)
            {
                t.Position.y = top + 0.05f;
                em.SetComponentData(goer, t);
            }
            Debug.Log($"DebugControls: goto kit ready, floor {(found ? V(top) : "none")} (ground {V(ground)})");
        }

        /// <summary>"-walk": steers the player at the line's walk target, and logs probe lines on the way.</summary>
        void WalkTo()
        {
            if (!walkTarget.HasValue || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingleton<LocalTransform>(out var walker))
                return;
            // W-67b: hold still till the -goto has its kit boxes (and has landed on a deck if there is one).
            if (gotoSettle.HasValue)
            {
                PlayerInputSystem.TestMove = float2.zero;
                walkStillSince = Time.unscaledTime;
                walkLastY = walker.Position.y;
                return;
            }
            var to = walkTarget.Value.xz - walker.Position.xz;
            var distance = math.length(to);
            // Waypoints: within 0.6 m of a leg's end, log it and head for the next one (the last stops at 0.3 m as before).
            if (distance <= 0.6f && walkPath.Count > 0)
            {
                walkLeg++;
                Debug.Log($"DebugControls: walk leg {walkLeg} reached at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {walkPath.Count} to go");
                walkTarget = walkPath[0];
                walkPath.RemoveAt(0);
                to = walkTarget.Value.xz - walker.Position.xz;
                distance = math.length(to);
            }
            PlayerInputSystem.TestMove = distance > 0.3f ? to / distance : float2.zero;
            // W-65c: the walk's end, for the shot list (a shot after a walk waits for it): arrived on the last leg, or
            // stood still for 2 s (blocked: a door, a wall).
            if (walkEnd == null)
            {
                // (W-67: and not still falling, as after a -goto drop onto the target.)
                if (walkPath.Count == 0 && distance <= 0.35f && math.abs(walker.Position.y - walkLastY) < 0.01f)
                    walkEnd = "ARRIVED";
                else if (math.distance(walker.Position.xz, walkStillAt) > 0.05f)
                {
                    walkStillAt = walker.Position.xz;
                    walkStillSince = Time.unscaledTime;
                    walkMoves++;
                }
                else if (walkMoves > 5 && Time.unscaledTime - walkStillSince > 2f)   // (not while the world first loads)
                    walkEnd = "STOPPED";
                // W-67: arrived means on the target's floor too, not just over its x, z (a walker under a deck was
                // ARRIVED): feet within 0.3 m of the expected top, else FAIL.
                if (walkEnd == "ARRIVED" && ExpectedFloor(walkTarget.Value, walker.Position, out var expected))
                {
                    Debug.Log($"DebugControls: walk foot y {V(walker.Position.y)} expected {V(expected)} ({V(walker.Position.y - expected)} m)");
                    if (math.abs(walker.Position.y - expected) > 0.3f)
                        walkEnd = "FAIL";
                }
                walkLastY = walker.Position.y;
                if (walkEnd != null)
                    Debug.Log($"DebugControls: walk {walkEnd} at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {V(distance)} m to go, {walkPath.Count} legs left");
            }
            // Probe lines every quarter second: where it is, its clearance over the floor, and how far the push-out has moved it.
            if (Time.unscaledTime >= walkLogNext && EnsureLocations(out var walkGround))
            {
                walkLogNext = Time.unscaledTime + 0.25f;
                var floor = walkGround.FloorUnder(walker.Position + new float3(0f, 0.5f, 0f), out var walkInCave);
                Debug.Log($"DebugControls: walk at ({V(walker.Position.x)}, {V(walker.Position.y)}, {V(walker.Position.z)}), {V(distance)} m to go, " +
                          $"{V(walker.Position.y - floor)} m over the floor (in cave {walkInCave}), pushed out {V(Punchy.Player.PlayerMovementSystem.PushedTotal.Data)} m in all" +
                          (Punchy.Player.PlayerMovementSystem.NoPushOut.Data ? " (push-out off)" : ""));
            }
        }

        /// <summary>A-69 "-flycave": the route still to fly (outside, mouth, the passage's nodes in to a chamber and back out).</summary>
        static readonly List<float3> flyRoute = new();
        static int flyLeg, flyCycles, flyCycle, flyStuck, flyLanded, flyTimeouts, flyBlocked0, flyExits;
        static bool flyOn;
        /// <summary>Which legs of a cycle are the way out at the mouth, and the cycle's length.</summary>
        static int flyMouthOut, flyCycleLength, flyClimbLeg, flyClimbs;
        static float flyLegSince, flyStillSince;
        static float3 flyStillAt;
        static bool flyStillCounted, flyDown;
        /// <summary>Frame times (ms) and speeds over the last 3 s, and the window after a mouth crossing still being filled.</summary>
        static readonly Queue<float2> flyRecent = new();
        static readonly List<float2> flyAfter = new();
        /// <summary>The climb out (a steep, fast climb away over new ground, where chunks stream in): its frames, and frames on the wing but held still.</summary>
        static readonly List<float2> flyClimb = new();
        static int flyClimbHeld;
        /// <summary>"-flyhitch MS": a main-thread stall this long every 45 frames of a climb (a chunk load's hitch on
        /// Ultra, which the editor's capped frames never show), and climb frames that fell off the wing.</summary>
        static int flyHitchMs, flyClimbFrame, flyClimbDropped;
        static float flyAfterUntil;
        static string flyExitPre;
        static float3 flyLastPos;
        /// <summary>A "-flycave" spec waiting for the flying body to land, and since when.</summary>
        static string flyPending;
        static float flyPendingSince;

        /// <summary>
        /// A-69 "-flycave N" or "-flycave N:Style": the player (a flying body, "-body Bat") flies a set route through the
        /// first cave (of that style) with a chamber, N times: from 10 m outside its mouth, in node by node to the chamber
        /// and back out, steered like a stick and the climb or descend buttons. Logs each stuck event (on the wing but
        /// under 1 m/s for a quarter second), each landing on the way, and frame times and speeds 3 s either side of
        /// each exit by the mouth.
        /// </summary>
        void StartFlyCave(in TerrainSource source, string spec)
        {
            var parts = spec.Split(':');
            flyCycles = int.TryParse(parts[0], out var n) ? math.max(n, 1) : 1;
            var styleIndex = -1;
            if (parts.Length > 1)
            {
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                var styles = CaveStyleTable.Collect(palette.Landmarks);
                styleIndex = styles.FindIndex(s => s != null && string.Equals(s.name, parts[1], System.StringComparison.OrdinalIgnoreCase));
            }
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            // ("-body Bat" lands a little after the world is up: wait for a body that flies, else the route is walked.)
            using (var flierQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>()))
                if (!flierQuery.TryGetSingleton<PlayerBody>(out var flier) || !flier.Flies)
                {
                    if (flyPending == null)
                        flyPendingSince = Time.time;
                    flyPending = spec;
                    return;
                }
            flyPending = null;
            ref var set = ref source.Caves.Value;
            flyRoute.Clear();
            for (int c = 0; c < set.Caves.Length && flyRoute.Count == 0; c++)
            {
                ref var cave = ref set.Caves[c];
                if (styleIndex >= 0 && cave.Style != styleIndex + 1 || cave.NodeCount < 2)
                    continue;
                var chamber = -1;
                for (int k = 0; k < cave.NodeCount && chamber < 0; k++)
                    if (set.Kinds[cave.FirstNode + k] == CaveNodeKind.Chamber)
                        chamber = k;
                if (chamber < 0)
                    continue;
                // In at the mouth low (its sky reads as open air, so a mid-air point would sit above the lip), from
                // straight out along the passage: its first node a metre or more in (the first node is the mouth itself).
                var mouthFloor = source.FloorUnder(cave.Mouth + new float3(0f, 0.5f, 0f), out _);
                var mouth = new float3(cave.Mouth.x, mouthFloor + 1.2f, cave.Mouth.z);
                var inner = set.Nodes[cave.FirstNode + math.min(1, cave.NodeCount - 1)].Position;
                for (int k = 0; k < cave.NodeCount; k++)
                    if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                    {
                        inner = set.Nodes[cave.FirstNode + k].Position;
                        break;
                    }
                var outward = math.normalizesafe(cave.Mouth.xz - inner.xz, new float2(0f, 1f));
                var outside = new float3(mouth.x + outward.x * 10f, source.SampleHeight(mouth.xz + outward * 10f) + 4f, mouth.z + outward.y * 10f);
                outside.y = math.max(outside.y, mouth.y + 2f);
                // A doorstep 3 m out on the passage's axis, so it comes at the mouth lined up, not from the side.
                var doorstep = new float3(mouth.x + outward.x * 3f, 0f, mouth.z + outward.y * 3f);
                doorstep.y = math.max(mouth.y, source.SampleHeight(doorstep.xz) + 1.2f);
                for (int cycle = 0; cycle < flyCycles; cycle++)
                {
                    flyRoute.Add(doorstep);
                    flyRoute.Add(mouth);
                    // (Nodes at the mouth itself are left out: under open sky their mid-air point sits above the lip.)
                    for (int k = 0; k <= chamber; k++)
                        if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                            flyRoute.Add(MidAir(source, set.Nodes[cave.FirstNode + k].Position));
                    for (int k = chamber - 1; k >= 0; k--)
                        if (math.distance(set.Nodes[cave.FirstNode + k].Position.xz, cave.Mouth.xz) > 1f)
                            flyRoute.Add(MidAir(source, set.Nodes[cave.FirstNode + k].Position));
                    if (cycle == 0)
                        flyMouthOut = flyRoute.Count;
                    flyRoute.Add(mouth);
                    flyRoute.Add(doorstep);
                    flyRoute.Add(outside);
                    // Then a steep climb away, 60 m out and 30 m up (Zach's control loss: flying up fast out of a cave
                    // while a big chunk of land loads).
                    if (cycle == 0)
                        flyClimbLeg = flyRoute.Count;
                    var away = outside.xz + outward * 60f;
                    flyRoute.Add(new float3(away.x, math.max(outside.y, source.SampleHeight(away)) + 30f, away.y));
                    if (cycle == 0)
                        flyCycleLength = flyRoute.Count;
                }
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(player);
                t.Position = outside;
                t.Rotation = quaternion.RotateY(math.atan2(-outward.x, -outward.y));
                em.SetComponentData(player, t);
                using var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
                bodyQuery.TryGetSingleton<PlayerBody>(out var body);
                var flySpeed = body.FlySpeed;
                em.SetComponentData(player, new PlayerMotion { Flying = body.Flies, Airspeed = math.max(flySpeed, 1f) });
                Debug.Log($"DebugControls: flycave cave {c} (style {cave.Style}), chamber node {chamber}, mouth ({V(cave.Mouth.x)}, {V(cave.Mouth.y)}, {V(cave.Mouth.z)}), " +
                          $"{flyRoute.Count} legs over {flyCycles} cycles, body flies {body.Flies} at {V(flySpeed)} m/s");
            }
            if (flyRoute.Count == 0)
            {
                Debug.LogWarning($"DebugControls: flycave found no cave with a chamber ('{spec}')");
                return;
            }
            flyOn = true;
            flyLeg = flyCycle = flyStuck = flyLanded = flyTimeouts = flyExits = 0;
            flyBlocked0 = PlayerMovementSystem.FlyBlocked.Data;
            flyLegSince = flyStillSince = Time.time;
            flyStillAt = float3.zero;
            flyRecent.Clear();
            flyAfter.Clear();
            flyAfterUntil = 0f;
            flyClimb.Clear();
            flyClimbHeld = flyClimbs = 0;
        }

        /// <summary>A-69 "-flycave": steers the flying player along the route and counts what goes wrong.</summary>
        void FlyCave()
        {
            if (flyPending != null && EnsureLocations(out var pendingSource))
            {
                if (Time.time - flyPendingSince > 30f)
                {
                    Debug.LogWarning("DebugControls: flycave: the body never flew (pass a flying body, e.g. -body Bat)");
                    flyPending = null;
                }
                else
                    StartFlyCave(pendingSource, flyPending);
            }
            if (!flyOn || queryWorld == null || !queryWorld.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var at = em.GetComponentData<LocalTransform>(player).Position;
            var motion = em.GetComponentData<PlayerMotion>(player);
            var ms = Time.unscaledDeltaTime * 1000f;
            var speed = Time.deltaTime > 0f ? math.distance(at, flyLastPos) / Time.deltaTime : 0f;
            flyLastPos = at;
            flyRecent.Enqueue(new float2(ms, speed));
            while (flyRecent.Count > 0 && flyRecent.Count > 3f / math.max(Time.unscaledDeltaTime, 1e-3f))
                flyRecent.Dequeue();
            if (flyAfterUntil > 0f)
            {
                flyAfter.Add(new float2(ms, speed));
                if (Time.time >= flyAfterUntil)
                {
                    flyAfterUntil = 0f;
                    Debug.Log($"DebugControls: flycave exit {flyExits}: before {flyExitPre}; after {FlyWindow(flyAfter)}; SurfaceHidden {CaveSystem.SurfaceHidden:F2}");
                }
            }

            if (flyLeg % flyCycleLength == flyClimbLeg)
            {
                flyClimb.Add(new float2(ms, speed));
                flyClimbHeld += motion.Flying && speed < 1f ? 1 : 0;
                flyClimbDropped += motion.Flying ? 0 : 1;
                if (flyHitchMs > 0 && ++flyClimbFrame % 45 == 0)
                    System.Threading.Thread.Sleep(flyHitchMs);
            }
            var target = flyRoute[flyLeg];
            var to = target - at;
            var horizontal = math.length(to.xz);
            // (On the last leg, held till the last exit's frame times are in.)
            // (Arrived within 2 m across and 1 m up or down: a sphere let it tick off the mouth from above the lip.)
            var reached = horizontal < 2f && math.abs(to.y) < 1f;
            var arrived = reached || Time.time - flyLegSince > 25f;
            if (arrived && flyLeg == flyRoute.Count - 1 && flyAfterUntil > 0f)
                arrived = false;
            if (arrived)
            {
                if (!reached)
                {
                    flyTimeouts++;
                    Debug.Log($"DebugControls: flycave leg {flyLeg} TIMED OUT at ({V(at.x)}, {V(at.y)}, {V(at.z)}), {V(math.length(to))} m short");
                }
                if (flyLeg % flyCycleLength == flyClimbLeg)
                {
                    flyClimbs++;
                    Debug.Log($"DebugControls: flycave climb {flyClimbs}: {FlyWindow(flyClimb)}, {flyClimbHeld} frames on the wing held under 1 m/s, " +
                              $"{flyClimbDropped} off the wing, hitch {flyHitchMs} ms, {(reached ? "reached" : "TIMED OUT")}");
                    flyClimb.Clear();
                    flyClimbHeld = flyClimbDropped = flyClimbFrame = 0;
                }
                if (flyLeg % flyCycleLength == flyMouthOut)
                {
                    // Out by the mouth: frame times and speeds for 3 s either side.
                    flyExits++;
                    flyExitPre = FlyWindow(new List<float2>(flyRecent));
                    flyAfter.Clear();
                    flyAfterUntil = Time.time + 3f;
                }
                flyLeg++;
                flyLegSince = Time.time;
                if (flyLeg >= flyRoute.Count)
                {
                    flyOn = false;
                    PlayerInputSystem.TestMove = null;
                    PlayerInputSystem.TestPitch = null;
                    PlayerInputSystem.TestClimb = PlayerInputSystem.TestCrouch = false;
                    Debug.Log($"DebugControls: flycave DONE {flyCycles} cycles: {flyStuck} stuck, {flyLanded} landed, {flyTimeouts} legs timed out, " +
                              $"{PlayerMovementSystem.FlyBlocked.Data - flyBlocked0} blocked frames, {flyExits} exits");
                    return;
                }
                target = flyRoute[flyLeg];
                to = target - at;
                horizontal = math.length(to.xz);
            }
            // Down on the way: counted, and off again.
            if (!motion.Flying)
            {
                if (motion.Grounded && !flyDown)
                {
                    flyDown = true;
                    flyLanded++;
                    var gap = "";
                    if (flyLanded <= 6 && EnsureLocations(out var landSource))
                    {
                        // (Where it came down: how much air is above it, and where the leg's target sits in its passage.)
                        var ceiling = CeilingAbove(landSource, at);
                        var targetFloor = landSource.FloorUnder(target + new float3(0f, 0.5f, 0f), out _);
                        gap = $", air above {V(ceiling)} m, pitch {motion.Pitch:F2}; target ({V(target.x)}, {V(target.y)}, {V(target.z)}) " +
                              $"floor {V(targetFloor)}, air above target floor {V(CeilingAbove(landSource, new float3(target.x, targetFloor, target.z)))} m";
                    }
                    Debug.Log($"DebugControls: flycave LANDED on leg {flyLeg} at ({V(at.x)}, {V(at.y)}, {V(at.z)}){gap}");
                }
                // Close and level, it walks on (taking off there, nose up, lifts it over a low mouth); otherwise off again.
                var walkOn = horizontal > 2f && horizontal < 6f && to.y < 0.8f;
                PlayerInputSystem.TestMove = horizontal > 0.1f ? to.xz / horizontal : float2.zero;
                PlayerInputSystem.TestJump = motion.Grounded && !walkOn;
                return;
            }
            flyDown = false;
            // Stuck: on the wing but hardly moving for a quarter second (counted once till it moves again).
            if (math.distance(at, flyStillAt) > 0.25f)
            {
                flyStillAt = at;
                flyStillSince = Time.time;
                flyStillCounted = false;
            }
            else if (!flyStillCounted && Time.time - flyStillSince > 0.25f)
            {
                flyStillCounted = true;
                flyStuck++;
                Debug.Log($"DebugControls: flycave STUCK on leg {flyLeg} at ({V(at.x)}, {V(at.y)}, {V(at.z)}), pitch {motion.Pitch:F2}, airspeed {V(motion.Airspeed)}");
            }
            PlayerInputSystem.TestMove = horizontal > 0.1f ? to.xz / horizontal : float2.zero;
            var angle = math.atan2(to.y, math.max(horizontal, 0.5f));
            PlayerInputSystem.TestClimb = angle > 0.3f;
            PlayerInputSystem.TestCrouch = angle < -0.3f;
            PlayerInputSystem.TestPitch = math.clamp(angle, -0.3f, 0.3f) / 0.6f;
        }

        /// <summary>A-69 "-flycave": a route point lifted to the middle of its passage's air (up to 3 m off the floor), as a player flies it.</summary>
        static float3 MidAir(in TerrainSource source, float3 p)
        {
            var floor = source.FloorUnder(p + new float3(0f, 0.5f, 0f), out _);
            var air = CeilingAbove(source, new float3(p.x, floor, p.z));
            return new float3(p.x, floor + math.clamp(air * 0.5f, 0.8f, 3f), p.z);
        }

        /// <summary>A-69: how far up from a point the air goes before rock (the cave's or the ground's), up to 30 m.</summary>
        static float CeilingAbove(in TerrainSource source, float3 p)
        {
            for (float up = 0.1f; up < 30f; up += 0.1f)
            {
                var q = p + new float3(0f, up, 0f);
                if (math.min(source.CaveDistance(q), source.SampleHeight(q.xz) - q.y) > 0f)
                    return up;
            }
            return 30f;
        }

        /// <summary>A frame-time and speed summary: mean and worst frame (ms), frames over 50 ms, slowest speed (m/s).</summary>
        static string FlyWindow(List<float2> frames)
        {
            if (frames.Count == 0)
                return "no frames";
            float sum = 0f, worst = 0f, slowest = float.MaxValue;
            var hitches = 0;
            foreach (var f in frames)
            {
                sum += f.x;
                worst = math.max(worst, f.x);
                slowest = math.min(slowest, f.y);
                hitches += f.x > 50f ? 1 : 0;
            }
            return $"{frames.Count} frames, mean {V(sum / frames.Count)} ms, worst {V(worst)} ms, {hitches} over 50 ms, slowest {V(slowest)} m/s";
        }

        /// <summary>
        /// W-67: the floor a walker at the -walk target should stand on: its y if the leg gave one ("x,y,z"), else the
        /// higher of the ground under the walker's height and the highest kit top there (within 2.5 m above the feet,
        /// with headroom over it), so standing under a deck doesn't count as arriving on it.
        /// </summary>
        bool ExpectedFloor(float3 target, float3 feet, out float expected)
        {
            expected = target.y;
            if (target.y != 0f)
                return true;
            if (!EnsureLocations(out var ground))
                return false;
            expected = ground.FloorUnder(new float3(target.x, feet.y + 0.5f, target.z), out _);
            // W-67b: the first kit floor over that ground (not just one within 2.5 m of the feet: a walker 2.6 m under
            // a wharf deck missed it and was ARRIVED on the seabed). Upper floors need an explicit "x,y,z".
            using var kitQuery = queryWorld.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            if (kitQuery.TryGetSingletonBuffer<KitBox>(out var kit, true) &&
                KitCollision.LowestTopAt(kit, target.xz, expected - 0.3f, 1.7f, out var kitTop, RoofGap(target.xz)) && kitTop > expected)
                expected = kitTop;
            return true;
        }

        /// <summary>
        /// W-67c (#38): for KitCollision.LowestTopAt from 0.3 m under the ground: on dry ground a box with a body's
        /// height (1.7 m) clear under it is a roof, not a floor; under water any box over the seabed is the floor.
        /// </summary>
        float RoofGap(float2 xz)
        {
            if (!EnsureLocations(out var source))
                return float.MaxValue;
            var g = source.SampleGround(xz);
            return g.HasWater && g.WaterSurface > g.Height ? float.MaxValue : 0.3f + 1.7f;
        }

        /// <summary>
        /// Points the camera at the watched species: the nearest one doing something worth seeing (climbing, flying,
        /// in a web, an idle action), else the nearest; re-picked every few seconds. A-34: "*" for any species
        /// ("*:Merchant": OgreCamp's merchants are goblins, so "Ogre:Merchant" found none).
        /// </summary>
        void Watch()
        {
            if (watchSpecies == null || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            if (creatureWorld != queryWorld)
            {
                creatureWorld = queryWorld;
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            }
            if (watchDirector == null)
                watchDirector = FindAnyObjectByType<FaunaDirector>();
            // "Human:Hunt": only those with that task.
            var watchParts = watchSpecies.Split(':');
            var anySpecies = watchParts[0] == "*";
            var index = anySpecies ? int.MaxValue : watchDirector != null ? watchDirector.SpeciesIndex(watchParts[0]) : -1;
            var watchTask = CreatureTask.None;
            var hasTask = watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchTask);
            // ("Human:Baker": only those with that job, A-29.)
            var watchJob = FolkJob.None;
            var hasJob = !hasTask && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchJob);
            // ("Deer:Drink": only those doing that, A-49 #64.)
            var watchDoing = CreatureMode.Wander;
            var hasDoing = !hasTask && !hasJob && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchDoing);
            // ("Deer:Pant": only those doing that idle action, re-picked every second, A-71.)
            watchAction = IdleAction.None;
            var hasAction = !hasTask && !hasJob && !hasDoing && watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchAction);
            if (index < 0 || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            if (Time.unscaledTime >= nextWatchPick || !em.Exists(watched))
            {
                nextWatchPick = Time.unscaledTime + (hasAction ? 1f : 8f);
                var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                var best = float.MaxValue;
                watched = Entity.Null;
                var wrongSpecies = "";
                foreach (var e in entities)
                {
                    var species = em.GetComponentData<CreatureBody>(e).Species;
                    var c = em.GetComponentData<CreatureState>(e);
                    if (hasTask && c.Task != watchTask || hasJob && FolkJobs.Of(c) != watchJob || hasDoing && c.Mode != watchDoing || hasAction && c.Action != watchAction)
                        continue;
                    if (!anySpecies && species != index)
                    {
                        // (Kept to say who does hold that job, if the asked species has none.)
                        if ((hasTask || hasJob) && wrongSpecies.Length < 80)
                            wrongSpecies += $" {watchDirector.SpeciesAt(species)?.name}";
                        continue;
                    }
                    var g = terrainQuery.GetSingleton<TerrainSource>().SampleGround(c.Position.xz);
                    var swimming = g.HasWater && c.Position.y < g.WaterSurface - 0.2f;
                    var busy = swimming || c.Mode == CreatureMode.Flee && c.Speed > 2f || c.Mode == CreatureMode.Climb || c.Mode == CreatureMode.Web || c.Fly > 0.5f ||
                               c.Action != IdleAction.None || c.Mode == CreatureMode.Hunt || c.Mode == CreatureMode.Burrow || c.Mode == CreatureMode.Roost;
                    var score = math.distance(c.Position, player.Position) - (busy ? 1000f : 0f) - (c.Fly > 0.5f ? 1000f : 0f);
                    if (score < best)
                    {
                        best = score;
                        watched = e;
                    }
                }
                entities.Dispose();
                if (watched == Entity.Null && wrongSpecies.Length > 0 && watchWarned != watchSpecies)
                {
                    watchWarned = watchSpecies;
                    Debug.LogWarning($"DebugControls: no {watchSpecies}; that job is held by:{wrongSpecies} (try -watch *:{watchParts[1]})");
                }
                if (watchAirFrames > 0)
                    Debug.Log($"DebugControls: steepest flight over {watchAirFrames} airborne frames: {math.degrees(math.atan(watchSteepest)):F0}°");
                watchSteepest = 0f;
                watchAirFrames = 0;
                watchLast = Vector3.zero;
                if (watched != Entity.Null)
                {
                    var c = em.GetComponentData<CreatureState>(watched);
                    Debug.Log($"DebugControls: watching {watchSpecies} ({c.Mode}, stage {c.Stage}, action {c.Action}, fly {c.Fly:F1}, " +
                              $"height above ground {c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz):F1} m, " +
                              $"player {math.distance(c.Position.xz, player.Position.xz):F0} m away, home {math.distance(c.Position.xz, c.Home):F0} m (leash {c.Leash:F0}), " +
                              // (A folk at work keeps TargetHeight 0, which read as "119 m down" in A-33: not a bad position.)
                              $"target {math.distance(c.Position.xz, c.Target):F2} m across, {(c.TargetHeight != 0f ? $"{c.TargetHeight - c.Position.y:F2} m up" : "no height")}, speed {c.Speed:F2}, " +
                              $"{watchDirector.SpeciesAt(em.GetComponentData<CreatureBody>(watched).Species)?.name} job {FolkJobs.Of(c)}, " +
                              $"yaw {math.degrees(c.Yaw):F0} deg turning {math.degrees(c.YawRate):F0} deg/s, " +
                              $"anchor {math.distance(c.Position, c.Anchor):F2} m (size {c.AnchorSize:F2}))");
                }
            }
            if (watched != Entity.Null && em.Exists(watched))
            {
                var c = em.GetComponentData<CreatureState>(watched);
                var p = (Vector3)c.Position;
                if (c.Fly > 0.5f && watchLast != Vector3.zero && Time.deltaTime > 0f)
                {
                    var d = p - watchLast;
                    var flat = new Vector2(d.x, d.z).magnitude;
                    if (d.magnitude > 0.01f)
                    {
                        var steep = math.abs(d.y) / math.max(flat, 1e-4f);
                        if (steep > 1.2f)
                            Debug.Log($"DebugControls: steep flight frame: {math.degrees(math.atan(steep)):F0}° ({c.Mode} stage {c.Stage}, " +
                                      $"speed {c.Speed:F2}, moved {flat:F3} m across, {d.y:F3} m up, {c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz):F2} m above ground)");
                        watchSteepest = math.max(watchSteepest, steep);
                        watchAirFrames++;
                    }
                }
                watchLast = p;
                // Every change of what it's doing, as it happens.
                if (c.Mode != watchMode || c.Stage != watchStage)
                {
                    Debug.Log($"DebugControls: {watchSpecies} now {c.Mode} stage {c.Stage} (target {math.distance(c.Position.xz, c.Target):F1} m)");
                    watchMode = c.Mode;
                    watchStage = c.Stage;
                    // A hunter drawing back and throwing: worth a picture.
                    if (shotDir != null && c.Mode == CreatureMode.Hunt && (c.Stage == FolkHunting.Aim || c.Stage == FolkHunting.Throw))
                        Shot($"hunt-{c.Stage}-{++watchShots}.png");
                }
                if (watchFrom.HasValue)
                {
                    // From a fixed spot above the ground (it stays put across the shot list's lines: watchFromAt isn't
                    // cleared between them), looking at the animal, or at the ground over it.
                    var terrain = terrainQuery.GetSingleton<TerrainSource>();
                    var look = new Vector3(p.x, math.max(p.y, terrain.SampleHeight(c.Position.xz)), p.z);
                    var range = watchFrom.Value.y;
                    if (!watchFromAt.HasValue || math.distance(((float3)watchFromAt.Value).xz, ((float3)look).xz) > range * 2f)
                    {
                        // (Off to the side of where it's heading, so its track crosses the view.)
                        var side = new float2(math.cos(c.Yaw), -math.sin(c.Yaw));
                        var xz = ((float3)look).xz + side * range;
                        watchFromAt = new Vector3(xz.x, terrain.SampleHeight(xz) + watchFrom.Value.x, xz.y);
                        Debug.Log($"DebugControls: watching from ({V(watchFromAt.Value.x)}, {V(watchFromAt.Value.y)}, {V(watchFromAt.Value.z)})");
                    }
                    PoseCamera(watchFromAt.Value, Quaternion.LookRotation(look - watchFromAt.Value));
                    ThirdPersonCamera.WatchTarget = null;
                    return;
                }
                var aloft = c.Position.y - terrainQuery.GetSingleton<TerrainSource>().SampleHeight(c.Position.xz) > 1f;
                ref var watchedBody = ref em.GetComponentData<CreatureBody>(watched).Blob.Value;
                // Upright bodies (people) are framed whole, from their middle.
                var tall = watchedBody.Upright ? watchedBody.StandHeight * c.Scale : 0f;
                ThirdPersonCamera.WatchTarget = (Vector3)c.Position + Vector3.up * math.max(0.3f, tall * 0.6f);
                ThirdPersonCamera.WatchPitch = watchPitchOverride ?? (aloft ? -15f : 15f);
                var size = math.max(watchedBody.BodyLength * c.Scale, tall * 0.6f);
                ThirdPersonCamera.WatchDistance = math.clamp(size * 9f, 0.6f, 8f) * (aloft ? 1.4f : 1f);
            }
            else
                ThirdPersonCamera.WatchTarget = null;
        }

        /// <summary>Holds the "-pose" specimen two metres to the player's side, in the pose asked for, and circles the camera round it.</summary>
        void Pose()
        {
            if (poseRequest == null || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.CanSpawn)
                return;
            var em = queryWorld.EntityManager;
            var parts = poseRequest.Split(':');
            var how = parts.Length > 1 ? parts[1] : "fly";
            // A third part is the camera's pitch (70: from above, to see a wing's shape).
            var viewPitch = parts.Length > 2 && float.TryParse(parts[2], out var p) ? p : 20f;
            // Gliding: in the air with the wings held level, half way through the beat.
            var glide = how == "glide";
            if (glide)
                how = "fly";
            // A-42: "lie": lying down (Rest), its legs folded; "-oldlie" on the same line leaves them as they fell before
            // A-42. A later -pose line naming another species swaps the specimen.
            var lie = how == "lie";
            if (posed != Entity.Null && em.Exists(posed) && em.GetComponentData<CreatureBody>(posed).Species != director.SpeciesIndex(parts[0]))
            {
                em.DestroyEntity(posed);
                posed = Entity.Null;
            }
            if (posed == Entity.Null || !em.Exists(posed))
            {
                var index = director.SpeciesIndex(parts[0]);
                if (index < 0)
                {
                    Debug.LogWarning($"DebugControls: no species '{parts[0]}' to pose");
                    poseRequest = null;
                    return;
                }
                var spot = player.Position.xz + new float2(2f, 0f);
                posed = director.Spawn(index, new float3(spot.x, source.SampleHeight(spot), spot.y), 0f);
                posedAt = Time.time;
                em.AddComponent<PosedForTest>(posed);
                Debug.Log($"DebugControls: posing {parts[0]} ({how})");
            }
            var c = em.GetComponentData<CreatureState>(posed);
            ref var b = ref em.GetComponentData<CreatureBody>(posed).Blob.Value;
            var dt = Time.deltaTime;
            var xz = player.Position.xz + new float2(2f, 0f);
            var ground = source.SampleHeight(xz);
            var size = b.BodyLength * c.Scale;
            c.Mode = how == "hang" ? CreatureMode.Roost : lie ? CreatureMode.Rest : CreatureMode.Idle;
            if (lie)
                c.Lie = 1f;
            // "scare" (A-42): stands 3 s, then turns and runs off straight ahead (+z), as if frightened: the flee state
            // change a skunk's gas starts from. The camera follows it.
            var scared = how == "scare" ? math.max(Time.time - posedAt - 3f, 0f) : 0f;
            if (scared > 0f)
            {
                c.Mode = CreatureMode.Flee;
                c.Speed = b.RunSpeed * c.Scale;
                c.GaitSpeed = c.Speed;
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
                xz += new float2(0f, scared * c.Speed);
                ground = source.SampleHeight(xz);
            }
            c.Stage = (byte)(how == "hang" ? 200 : 0);
            c.Fly = how == "fly" ? 1f : 0f;
            c.Vertical = how == "hang" ? -1f : 0f;
            c.Speed = how == "fly" ? b.FlySpeed * 0.6f : scared > 0f ? c.Speed : 0f;
            var onGround = how == "rest" || lie || how == "scare" || how == "nest" && b.Nests == NestSite.Ground;
            c.Position = new float3(xz.x, ground + (onGround ? 0f : math.max(1.2f, size * 4f)), xz.y);
            // Sitting in its nest (made where it is, the size FindNest gives one).
            if (how == "nest")
            {
                c.Anchor = c.Position;
                c.AnchorSize = math.max(size * 0.7f, 0.08f) * (b.Nests == NestSite.Treetop ? 1.4f : 1f);
            }
            // Swimming: out in deep water near the player (start by a lake or river), paddling on the spot.
            if (how == "swim")
            {
                if (!swimSpot.HasValue)
                    for (var d = 3f; d < 60f && !swimSpot.HasValue; d += 2f)
                        for (int k = 0; k < 12 && !swimSpot.HasValue; k++)
                        {
                            var at = player.Position.xz + new float2(math.sin(k * 0.5236f), math.cos(k * 0.5236f)) * d;
                            var w = source.SampleGround(at);
                            if (w.HasWater && w.WaterSurface - w.Height > b.StandHeight * c.Scale * 2f)
                                swimSpot = at;
                        }
                if (swimSpot.HasValue)
                {
                    var w = source.SampleGround(swimSpot.Value);
                    var below = b.LavaDweller ? -b.FrontRadius * c.Scale * 0.4f : b.StandHeight * c.Scale + b.FrontRadius * b.BodyFlatten * c.Scale * 0.55f;
                    c.Position = new float3(swimSpot.Value.x, w.WaterSurface - below, swimSpot.Value.y);
                    c.Speed = b.WalkSpeed * c.Scale;
                    CreatureMotion.AdvancePhase(ref c, ref b, dt);
                }
            }
            c.Clock += dt;
            if (glide)
                c.Phase = 0.25f;
            else if (how == "fly")
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
            em.SetComponentData(posed, c);
            // (Upright bodies, people, framed head and shoulders: their body length is just the hips.)
            var standing = b.Upright ? b.StandHeight * c.Scale : 0f;
            ThirdPersonCamera.WatchTarget = (Vector3)c.Position + Vector3.up * (b.Upright ? standing * 1.55f : size * 0.5f);
            ThirdPersonCamera.WatchPitch = viewPitch;
            ThirdPersonCamera.WatchDistance = b.Upright ? math.max(standing * 0.9f, 0.6f) : math.clamp(size * 4f, 0.5f, 6f);
        }

        /// <summary>
        /// The "-interact" test: 12 s in (after any -spawn), stands the player just in front of the nearest animal of the
        /// species, facing it; a moment later pets it or picks it up; carrying, walks off a few metres and puts it down.
        /// </summary>
        void Interact()
        {
            if (interactRequest == null || Time.timeSinceLevelLoad - optionsAt < settle + 4f || queryWorld == null || !queryWorld.IsCreated ||
                !playerQuery.TryGetSingletonEntity<LocalTransform>(out var playerEntity))
                return;
            var em = queryWorld.EntityManager;
            var parts = interactRequest.Split(':');
            var carry = parts.Length > 1 && parts[1] == "carry";
            // ("Rabbit:carry:sneak": crept up on from behind, not walked up to from the front.)
            var sneak = parts.Length > 2 && parts[2] == "sneak";
            if (interactDirector == null)
                interactDirector = FindAnyObjectByType<FaunaDirector>();
            var index = interactDirector != null ? interactDirector.SpeciesIndex(parts[0]) : -1;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no species '{parts[0]}' to interact with");
                interactRequest = null;
                return;
            }
            // (The queries made once per world, not every frame.)
            if (creatureWorld != queryWorld)
            {
                creatureWorld = queryWorld;
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            }
            if (requestWorld != queryWorld)
            {
                requestWorld = queryWorld;
                requestQuery = em.CreateEntityQuery(ComponentType.ReadWrite<InteractionRequest>());
            }
            var player = em.GetComponentData<LocalTransform>(playerEntity);
            if (!requestQuery.TryGetSingleton<InteractionRequest>(out var request))
                return;
            void Send(InteractAct act)
            {
                request.Act = act;
                request.Serial++;
                request.Target = interactTarget;
                // (Joined to someone else's game: the server knows the animal by its ghost id.)
                request.TargetGhost = em.HasComponent<Unity.NetCode.GhostInstance>(interactTarget)
                    ? em.GetComponentData<Unity.NetCode.GhostInstance>(interactTarget).ghostId : 0;
                requestQuery.SetSingleton(request);
                Debug.Log($"DebugControls: interact {act} {parts[0]} (ghost {request.TargetGhost}, {math.distance(em.GetComponentData<CreatureState>(interactTarget).Position.xz, em.GetComponentData<LocalTransform>(playerEntity).Position.xz):F1} m away)");
            }

            var now = Time.timeSinceLevelLoad;
            if (interactStep < 0f)
            {
                // Find one and stand in front of it.
                var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                var best = float.MaxValue;
                interactTarget = Entity.Null;
                foreach (var e in entities)
                {
                    if (em.HasComponent<PlayerAvatar>(e) || em.GetComponentData<CreatureBody>(e).Species != index)
                        continue;
                    var d = math.distance(em.GetComponentData<CreatureState>(e).Position, player.Position);
                    if (d < best)
                    {
                        best = d;
                        interactTarget = e;
                    }
                }
                entities.Dispose();
                if (interactTarget == Entity.Null)
                    return;
                interactStep = now;
            }
            if (!em.Exists(interactTarget))
            {
                interactRequest = null;
                return;
            }
            var s = em.GetComponentData<CreatureState>(interactTarget);
            var elapsed = now - interactStep;
            if (elapsed < 1f)
            {
                // Keep stepping up to it (it may be wandering) until the request goes.
                var toward = sneak ? new float2(math.sin(s.Yaw), math.cos(s.Yaw))
                                   : math.normalizesafe(s.Position.xz - player.Position.xz, new float2(0f, 1f));
                var stand = s.Position.xz - toward * (sneak ? 1.3f : 0.9f);
                player.Position = new float3(stand.x, s.Position.y, stand.y);
                player.Rotation = quaternion.RotateY(math.atan2(toward.x, toward.y));
                em.SetComponentData(playerEntity, player);
                ThirdPersonCamera.WatchTarget = (Vector3)player.Position + Vector3.up * 0.4f;
                ThirdPersonCamera.WatchPitch = watchPitchOverride ?? 20f;
                ThirdPersonCamera.WatchDistance = 3f;
                return;
            }
            if (request.Act == InteractAct.None || request.Target != interactTarget)
            {
                Send(carry ? InteractAct.PickUp : InteractAct.Pet);
                return;
            }
            if (elapsed > 3f && (elapsed - Time.deltaTime) <= 3f)
            {
                var avatarQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureState>());
                var avatar = avatarQuery.TryGetSingleton<CreatureState>(out var a) ? $"{a.Action} at {a.ActionTime:F2}" : "none";
                Debug.Log($"DebugControls: {parts[0]} is {s.Mode} (stage {s.Stage}, holder {s.Holder}); player pose {request.Pose}, body {avatar}");
                // #16: what it weighs against what a carrier can lift.
                ref var wb = ref em.GetComponentData<CreatureBody>(interactTarget).Blob.Value;
                var kg = wb.WeightAt(s.Scale);
                Debug.Log($"DebugControls: carry check: {parts[0]} weighs {kg:F1} kg (scale {s.Scale:F2}), capacity {Interactions.CarryCapacity:F0} kg: " +
                          (kg <= Interactions.CarryCapacity ? "light enough" : "too heavy, can't lift") + $"; held: {s.Mode == CreatureMode.Held}" +
                          $"; tame (settled, leash {s.Leash:F0} m): {Interactions.Tame(ref wb, s)}");
            }
            // Its own screenshots: being petted or just picked up, then (carrying) walking, turning on the spot and
            // sprinting.
            foreach (var at in new[] { 2.5f, 5f, 6.6f, 8.2f })
                if (shotDir != null && elapsed > at && elapsed - Time.deltaTime <= at)
                    Shot($"interact-{parts[0]}-{(carry ? "carry" : "pet")}-{at * 10f:F0}.png");
            // Carrying: walk on (as if the stick were pushed), turn round on the spot, sprint, then put it down.
            Punchy.Player.PlayerInputSystem.TestMove = null;
            Punchy.Player.PlayerInputSystem.TestSprint = false;
            if (carry && s.Mode == CreatureMode.Held && elapsed > 4f && elapsed < 8.6f)
            {
                var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz;
                if (elapsed < 6f || elapsed > 7f)
                {
                    Punchy.Player.PlayerInputSystem.TestMove = math.normalizesafe(forward, new float2(0f, 1f));
                    Punchy.Player.PlayerInputSystem.TestSprint = elapsed > 7f;
                }
                else
                {
                    player.Rotation = math.mul(player.Rotation, quaternion.RotateY(math.PI * Time.deltaTime));
                    em.SetComponentData(playerEntity, player);
                }
            }
            if (carry && elapsed > 9.5f && request.Act == InteractAct.PickUp)
                Send(InteractAct.PutDown);
            ThirdPersonCamera.WatchTarget = (Vector3)player.Position + Vector3.up * 0.8f;
            if (elapsed > 14f)
            {
                Debug.Log($"DebugControls: interaction test over: {parts[0]} is {s.Mode}");
                interactRequest = null;
                ThirdPersonCamera.WatchTarget = null;
            }
        }

        // ---------------------------------------------------------------- shot lists

        /// <summary>
        /// "-shotlist shots.txt" (with -shotdir): many screenshots in one session, instead of a session each. Each line
        /// is the usual test flags for one shot (-place, -goto, -time, -pose, -spawn, -watch, -watchpitch…) plus
        /// "-shot name" (the file, .png added) and optionally "-wait 3" (seconds after the line before the shot; default
        /// 2, plus the world's first build for the first line). Before each line the previous one's specimen, spawns
        /// and camera are cleared. Lines starting # are notes. In the editor, Play mode ends when the list is done.
        /// </summary>
        static readonly List<string[]> shotLines = new List<string[]>();
        static int shotIndex = -1;
        float shotStarted, shotWait, listStarted;

        void LoadShotList(string path)
        {
            shotLines.Clear();
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning("DebugControls: no shot list at " + path);
                return;
            }
            foreach (var line in System.IO.File.ReadAllLines(path))
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("#"))
                    shotLines.Add(line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries));
            shotIndex = 0;
            shotStarted = -1f;
            listStarted = Time.realtimeSinceStartup;
            Debug.Log($"DebugControls: shot list of {shotLines.Count}");
        }

        void ShotList()
        {
            if (shotIndex < 0 || shotDir == null || !launchOptionsApplied)
                return;
            if (shotIndex >= shotLines.Count)
            {
                Debug.Log($"DebugControls: shot list done, {shotLines.Count} shots in {Time.realtimeSinceStartup - listStarted:F1} s");
                shotIndex = -1;
                // "-quit" on the launch line: the player closes itself when the list is done (agents can't kill it).
                if (System.Array.IndexOf(LaunchArgs, "-quit") >= 0)
                    Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(); // a player of ours must end by itself (it locks the exe)
#endif
                return;
            }
            var line = shotLines[shotIndex];
            string Arg(string flag)
            {
                var i = System.Array.IndexOf(line, flag);
                return i >= 0 && i + 1 < line.Length ? line[i + 1] : null;
            }
            if (shotStarted < 0f)
            {
                ClearShot();
                ApplyOptions(line);
                // (The first line also waits for the world's first build: optionsAt counts from the scene's start.)
                var first = shotIndex == 0;
                if (!first)
                    settle = 1.5f;
                shotWait = (first ? settle : 0f) + (float.TryParse(Arg("-wait"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 2f) + (Arg("-pose") != null || Arg("-spawn") != null ? settle : 0f);
                shotStarted = Time.realtimeSinceStartup;
                return;
            }
            // (A-14 #33: a `-place wall` or ledge still being found (its props loading, or the search moving to another
            // settlement): the wait starts once the player is placed, so the shot lands mid-climb on the side camera.)
            if (ledgeRequest)
            {
                shotStarted = Time.realtimeSinceStartup;
                shotWait = float.TryParse(Arg("-wait"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var placedWait) ? placedWait : 2f;
                return;
            }
            var caves = CaveSystem.Underground > 0.5f ? FindAnyObjectByType<CaveSystem>() : null;
            var settled = caves == null || caves.Settled;
            if (perfLog && Time.realtimeSinceStartup - shotStarted >= shotWait - 3f && (settled || Time.realtimeSinceStartup - shotStarted >= shotWait))
                SamplePerf();
            if (Time.realtimeSinceStartup - shotStarted < shotWait)
                return;
            // "-untilwork": not yet, while the watched folk isn't working (or only just started).
            if (untilWork > 0f && Time.realtimeSinceStartup - shotStarted < shotWait + untilWork)
            {
                var working = watched != Entity.Null && queryWorld != null && queryWorld.IsCreated && queryWorld.EntityManager.Exists(watched) &&
                              queryWorld.EntityManager.GetComponentData<CreatureState>(watched).Mode == CreatureMode.Work;
                if (!working)
                    workSince = -1f;
                else if (workSince < 0f)
                    workSince = Time.realtimeSinceStartup;
                if (!working || Time.realtimeSinceStartup - workSince < 1f)
                    return;
            }
            // "-untilaction": likewise, till the watched one is doing the -watch action (A-71 weather poses).
            if (untilAction > 0f && Time.realtimeSinceStartup - shotStarted < shotWait + untilAction)
            {
                var doing = watched != Entity.Null && queryWorld != null && queryWorld.IsCreated && queryWorld.EntityManager.Exists(watched) &&
                            watchAction != IdleAction.None && queryWorld.EntityManager.GetComponentData<CreatureState>(watched).Action == watchAction;
                if (!doing)
                    workSince = -1f;
                else if (workSince < 0f)
                    workSince = Time.realtimeSinceStartup;
                if (!doing || Time.realtimeSinceStartup - workSince < 1f)
                    return;
            }
            // Underground, also until the caves round the camera are built (up to 10 s more).
            if (!settled && Time.realtimeSinceStartup - shotStarted < shotWait + 10f)
                return;
            // W-65c: a line that walks shoots only once the walk has ended (arrived or blocked), or after
            // "-walktimeout" s (default 60), logged as a FAIL.
            if (Arg("-walk") != null && walkEnd == null)
            {
                var limit = float.TryParse(Arg("-walktimeout"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var wt) ? wt : 60f;
                if (Time.realtimeSinceStartup - shotStarted < shotWait + limit)
                    return;
                Debug.Log($"DebugControls: walk FAIL timeout after {limit:F0} s, shot '{Arg("-shot")}' taken anyway");
            }
            Shot((Arg("-shot") ?? $"shot-{shotIndex}") + ".png");
            var at = QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var shooter) ? shooter.Position : float3.zero;
            Debug.Log($"DebugControls: shot {shotIndex + 1}/{shotLines.Count} '{Arg("-shot")}' after {Time.realtimeSinceStartup - shotStarted:F1} s" +
                      (drawCalls.Valid ? $", {drawCalls.LastValue} draw calls, {batches.LastValue} batches, {tris.LastValue / 1000} k tris" : "") +
                      $", player at ({at.x:F1}, {at.y:F1}, {at.z:F1})");
            var lineSearch = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            Debug.Log($"DebugControls: flees this line '{Arg("-shot")}' predator {lineSearch.PredatorFlee - lineSearch0.PredatorFlee}, " +
                      $"alarm {lineSearch.AlarmFlee - lineSearch0.AlarmFlee}, player {lineSearch.PlayerFlee - lineSearch0.PlayerFlee}, {SimPrint()}, searches predator {lineSearch.Predator - lineSearch0.Predator}, " +
                      $"alarm {lineSearch.Alarm - lineSearch0.Alarm}, old search {Punchy.Fauna.CreatureBrainSystem.SearchEveryFrame.Data}, " +
                      $"ground samples sky {Punchy.Fauna.CreatureRenderSystem.SkyGroundSamples} perch {Punchy.Fauna.AmbientLife.PerchGroundSamples} by frame {Time.frameCount}");
            if (perfLog)
                LogPerf(Arg("-shot"), at);
            shotIndex++;
            shotStarted = -1f;
        }

        /// <summary>#61: every creature's count and summed position, so two runs (or two builds) of a line compare exactly.</summary>
        string SimPrint()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return "sim none";
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var sum = double3.zero;
            int live = 0;
            foreach (var cs in states)
                if (!cs.Gone)
                {
                    live++;
                    sum += (double3)cs.Position;
                }
            states.Dispose();
            return $"sim {live} live, position sum ({sum.x:F2}, {sum.y:F2}, {sum.z:F2})";
        }

        // "-perflog" sums over the last 3 s of a shot line's wait.
        Unity.Profiling.ProfilerRecorder brainTick;
        Punchy.Fauna.CreatureBrainSystem.BrainSpread perfSpread0;
        double perfBrain;
        // #132: Advance and its two wall checks inside the brain tick.
        Unity.Profiling.ProfilerRecorder brainAdvance, brainWalls, brainSolids, brainBuilt;
        double perfAdvance, perfWalls, perfSolids, perfBuilt;
        bool perfLog;
        int perfFrames, perfTimed, perfChunkFrames;
        double perfFrame, perfMax, perfMain, perfRender, perfGpu, perfDraws, perfTris;
        long perfLoaded, perfSubmitted, perfInView, perfShadows, perfCaveTris;
        long perfProbeCalls0 = -1, perfProbeTicks0;
        // (A-45 baselines: trace frames/ticks/surface checks, snapshot copies/ticks, brain searches and flees.)
        struct long2 { public long X, Y; public long2(long x, long y) { X = x; Y = y; } }
        struct long3 { public long X, Y, Z; public long3(long x, long y, long z) { X = x; Y = y; Z = z; } }
        long3 perfTrace0;
        long2 perfCopies0;
        long perfWait0;
        Punchy.Fauna.CreatureBrainSystem.SearchCounts perfSearch0, lineSearch0;
        readonly FrameTiming[] perfTiming = new FrameTiming[1];

        void SamplePerf()
        {
            if (perfProbeCalls0 < 0)
            {
                perfProbeCalls0 = ThirdPersonCamera.ProbeCalls;
                perfProbeTicks0 = ThirdPersonCamera.ProbeTicks;
                perfTrace0 = new long3(Punchy.Fauna.TraceSystem.Frames, Punchy.Fauna.TraceSystem.Ticks, Punchy.Fauna.TraceSystem.NoSurfaceCalls);
                perfCopies0 = new long2(Punchy.Fauna.CreatureSnapshotSystem.Copies, Punchy.Fauna.CreatureSnapshotSystem.CopyTicks);
                perfWait0 = Punchy.Fauna.CreatureSnapshotSystem.WaitTicks;
                perfSearch0 = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
                perfSpread0 = Punchy.Fauna.CreatureBrainSystem.Spread.Data;
                Punchy.Fauna.CreatureBrainSystem.Spread.Data.WorstWallTicks = 0;
                Punchy.Fauna.CreatureRenderSystem.PoseMs.Clear();
                Punchy.Fauna.CreatureRenderSystem.PoseEvaluated = Punchy.Fauna.CreatureRenderSystem.PoseReused = Punchy.Fauna.CreatureRenderSystem.PoseFrames = 0;
                Punchy.Fauna.ReaderTimes.Clear();
            }
            var dt = Time.unscaledDeltaTime * 1000.0;
            perfFrames++;
            perfFrame += dt;
            if (!brainTick.Valid)
                brainTick = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "Brain.Tick");
            else
                perfBrain += brainTick.LastValue * 1e-6;
            if (!brainAdvance.Valid)
            {
                brainAdvance = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "Brain.Advance");
                brainWalls = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "Brain.Walls");
                brainSolids = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "Brain.Solids");
                brainBuilt = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "Brain.Built");
            }
            else
            {
                perfAdvance += brainAdvance.LastValue * 1e-6;
                perfWalls += brainWalls.LastValue * 1e-6;
                perfSolids += brainSolids.LastValue * 1e-6;
                perfBuilt += brainBuilt.LastValue * 1e-6;
            }
            perfMax = System.Math.Max(perfMax, dt);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, perfTiming) > 0)
            {
                perfTimed++;
                perfMain += perfTiming[0].cpuMainThreadFrameTime;
                perfRender += perfTiming[0].cpuRenderThreadFrameTime;
                perfGpu += perfTiming[0].gpuFrameTime;
            }
            if (drawCalls.Valid)
            {
                perfDraws += drawCalls.LastValue;
                perfTris += tris.LastValue;
            }
            perfChunkFrames++;
            perfLoaded += CaveSystem.StatLoaded;
            perfSubmitted += CaveSystem.StatSubmitted;
            perfInView += CaveSystem.StatInView;
            perfShadows += CaveSystem.StatShadows;
            perfCaveTris += CaveSystem.StatTriangles;
        }

        /// <summary>A-70 row 4, for perf2: the creature pose job's median and worst frame (ms summed over the workers), and
        /// how many creatures a frame were posed afresh or moved on from their last pose.</summary>
        static string PoseSummary()
        {
            var ms = Punchy.Fauna.CreatureRenderSystem.PoseMs;
            var frames = System.Math.Max(Punchy.Fauna.CreatureRenderSystem.PoseFrames, 1);
            var sorted = ms.ToArray();
            System.Array.Sort(sorted);
            var median = sorted.Length > 0 ? sorted[sorted.Length / 2] : 0.0;
            var worst = sorted.Length > 0 ? sorted[sorted.Length - 1] : 0.0;
            return $"pose job median {median:F3} ms, worst {worst:F3} ms, posed {(double)Punchy.Fauna.CreatureRenderSystem.PoseEvaluated / frames:F1}/frame, " +
                   $"moved on {(double)Punchy.Fauna.CreatureRenderSystem.PoseReused / frames:F1}/frame, old pose rate {Punchy.Fauna.CreatureRenderSystem.OldPoseRate}; " +
                   Punchy.Fauna.ReaderTimes.Summary();
        }

        /// <summary>One "perf" line per -perflog shot: frame, thread and GPU ms, cave chunks, lights, particles, probes.</summary>
        void LogPerf(string name, float3 at)
        {
            var n = System.Math.Max(perfFrames, 1);
            var t = System.Math.Max(perfTimed, 1);
            var c = System.Math.Max(perfChunkFrames, 1);
            var lights = 0;
            var shadowLights = 0;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled && l.intensity > 0f)
                {
                    lights++;
                    if (l.shadows != LightShadows.None)
                        shadowLights++;
                }
            var systems = 0;
            var particles = 0;
            foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (p.isPlaying)
                {
                    systems++;
                    particles += p.particleCount;
                }
            var probeCalls = ThirdPersonCamera.ProbeCalls - perfProbeCalls0;
            var probeMs = (ThirdPersonCamera.ProbeTicks - perfProbeTicks0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            // The solid query's own price here: 2000 IsRock (SolidDistance under it) round the player, in Burst.
            var benchUs = 0.0;
            if (EnsureLocations(out var benchSource))
            {
                var sink = new NativeArray<int>(1, Allocator.TempJob);
                var bench = new SolidBench { Terrain = benchSource, Centre = at + new float3(0f, 1f, 0f), Count = 2000, Hits = sink };
                bench.Run();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                bench.Run();
                benchUs = watch.Elapsed.TotalMilliseconds * 1000.0 / 2000.0;
                sink.Dispose();
            }
            Debug.Log($"DebugControls: perf '{name}' {perfFrames} frames, frame {perfFrame / n:F2} ms (max {perfMax:F1}), " +
                      (perfTimed > 0 ? $"cpu main {perfMain / t:F2}, render {perfRender / t:F2}, gpu {perfGpu / t:F2} ms" : "no frame timing") +
                      (drawCalls.Valid ? $", {perfDraws / n:F0} draw calls, {perfTris / n / 1000:F0} k tris" : "") +
                      $"; cave chunks loaded {perfLoaded / c}, submitted {perfSubmitted / c}, in view {perfInView / c}, casting {perfShadows / c}, " +
                      $"{perfCaveTris / c / 1000} k cave tris in view; lights {lights} ({shadowLights} shadowed), particles {systems} systems / {particles}; " +
                      $"camera probe {(double)probeCalls / n:F2}/frame, {probeMs / n * 1000:F0} us/frame; IsRock {benchUs:F2} us/call (Burst); " +
                      $"underground {CaveSystem.Underground:F2}, hidden {CaveSystem.SurfaceHidden:F2}, player ({at.x:F1}, {at.y:F1}, {at.z:F1})");
            // A-45 (#55-#57): traces, shared creature snapshot, staggered brain searches.
            var freq = (double)System.Diagnostics.Stopwatch.Frequency;
            var traceFrames = System.Math.Max(Punchy.Fauna.TraceSystem.Frames - perfTrace0.X, 1);
            var se = Punchy.Fauna.CreatureBrainSystem.Searches.Data;
            var fp = Punchy.Fauna.BuildingFootprints.Stats.Data;
            var sp = Punchy.Fauna.CreatureBrainSystem.Spread.Data;
            var spFrames = System.Math.Max(sp.Frames - perfSpread0.Frames, 1);
            var spWorkers = System.Math.Max(sp.Workers - perfSpread0.Workers, 1);
            var spMean = (double)(sp.SumBusyTicks - perfSpread0.SumBusyTicks) / spWorkers;
            var spMax = (double)(sp.MaxBusyTicks - perfSpread0.MaxBusyTicks) / spFrames;
            // (The brain job times itself with the profiler's Burst-safe clock, not Stopwatch.)
            var ratio = Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio;
            var spFreq = 1e9 * ratio.Denominator / ratio.Numerator;
            Debug.Log($"DebugControls: perf2 '{name}' {perfFrames} frames, creatures {World.DefaultGameObjectInjectionWorld?.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>()).CalculateEntityCount() ?? 0}; " +
                      $"trace {(Punchy.Fauna.TraceSystem.Ticks - perfTrace0.Y) * 1000.0 / freq / traceFrames:F3} ms/frame, " +
                      $"{(double)(Punchy.Fauna.TraceSystem.NoSurfaceCalls - perfTrace0.Z) / traceFrames:F1} surface checks/frame; " +
                      $"snapshot copies {(double)(Punchy.Fauna.CreatureSnapshotSystem.Copies - perfCopies0.X) / n:F1}/frame, " +
                      $"{(Punchy.Fauna.CreatureSnapshotSystem.CopyTicks - perfCopies0.Y) * 1000.0 / freq / n:F3} ms/frame, " +
                      $"brain wait {(Punchy.Fauna.CreatureSnapshotSystem.WaitTicks - perfWait0) * 1000.0 / freq / n:F3} ms/frame; " +
                      $"searches/frame predator {(double)(se.Predator - perfSearch0.Predator) / n:F1}, alarm {(double)(se.Alarm - perfSearch0.Alarm) / n:F1}, " +
                      $"chat {(double)(se.Chat - perfSearch0.Chat) / n:F1}; flees predator {se.PredatorFlee - perfSearch0.PredatorFlee}, " +
                      $"alarm {se.AlarmFlee - perfSearch0.AlarmFlee}; totals flee {se.PredatorFlee}/{se.AlarmFlee}; " +
                      $"brain ms {perfBrain / n:F3} (summed over workers), thinks/frame {(double)(se.Thinks - perfSearch0.Thinks) / n:F1}, lod skips/frame {(double)(se.LodSkips - perfSearch0.LodSkips) / n:F1}, lod off {Punchy.Fauna.CreatureBrainSystem.BrainLodOff.Data}; " +
                      $"advance ms {perfAdvance / n:F3} (walls {perfWalls / n:F3}, solids {perfSolids / n:F3}, of it buildings {perfBuilt / n:F3}); " +
                      $"footprints {(Punchy.Fauna.BuildingFootprints.Off.Data != 0 ? "off" : "on")}, baked towns {fp.x} landmarks {fp.y}, checked {fp.z} mismatches {fp.w}; " +
                      $"brain job wall {(sp.WallTicks - perfSpread0.WallTicks) * 1000.0 / spFreq / spFrames:F3} ms (worst {sp.WorstWallTicks * 1000.0 / spFreq:F3}), " +
                      $"workers {(double)spWorkers / spFrames:F1}, busiest {spMax * 1000.0 / spFreq:F3} ms, mean {spMean * 1000.0 / spFreq:F3} ms, spread {spMax / System.Math.Max(spMean, 1.0):F2}x; " + PoseSummary());
            perfBrain = perfAdvance = perfWalls = perfSolids = perfBuilt = 0;
            perfFrames = perfTimed = perfChunkFrames = 0;
            perfFrame = perfMax = perfMain = perfRender = perfGpu = perfDraws = perfTris = 0;
            perfLoaded = perfSubmitted = perfInView = perfShadows = perfCaveTris = 0;
            perfProbeCalls0 = -1;
        }

        [Unity.Burst.BurstCompile]
        struct SolidBench : Unity.Jobs.IJob
        {
            public TerrainSource Terrain;
            public float3 Centre;
            public int Count;
            public NativeArray<int> Hits;

            public void Execute()
            {
                var rng = new Unity.Mathematics.Random(12345u);
                var hits = 0;
                for (int i = 0; i < Count; i++)
                    if (Terrain.IsRock(Centre + rng.NextFloat3(-6f, 6f), 0.35f))
                        hits++;
                Hits[0] = hits;
            }
        }

        /// <summary>"-gocave": onto a cave floor by plan position or by size rank; logs which cave and its depth.</summary>
        void GoToCave(in TerrainSource source, Entity player, string arg)
        {
            ref var set = ref source.Caves.Value;
            var count = set.Caves.Length;
            var size = new float[count];
            for (int c = 0; c < count; c++)
            {
                var cv = set.Caves[c];
                for (int k = cv.FirstNode; k < cv.FirstNode + cv.NodeCount; k++)
                    size[c] += set.Nodes[k].Radius * set.Nodes[k].Radius * set.Nodes[k].Radius;
            }
            var order = new int[count];
            for (int c = 0; c < count; c++)
                order[c] = c;
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));
            int cave = -1, node = -1;
            if (arg.StartsWith("#") && int.TryParse(arg.Substring(1), out var rank) && rank >= 1 && rank <= count)
            {
                cave = order[rank - 1];
                var cv = set.Caves[cave];
                for (int k = cv.FirstNode; k < cv.FirstNode + cv.NodeCount; k++)
                    if (node < 0 || set.Nodes[k].Radius > set.Nodes[node].Radius)
                        node = k;
            }
            else
            {
                var parts = arg.Split(',');
                if (parts.Length < 2 ||
                    !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(parts[^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                    return;
                var best = float.MaxValue;
                for (int c = 0; c < count; c++)
                {
                    var cv = set.Caves[c];
                    for (int k = cv.FirstNode; k < cv.FirstNode + cv.NodeCount; k++)
                    {
                        var d = math.distancesq(set.Nodes[k].Position.xz, new float2(x, z));
                        if (d < best)
                        {
                            best = d;
                            cave = c;
                            node = k;
                        }
                    }
                }
            }
            if (node < 0)
                return;
            var pos = set.Nodes[node].Position;
            var floor = source.FloorUnder(pos + new float3(0f, 0.5f, 0f), out var inCave);
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(pos.x, floor + 0.05f, pos.z);
            em.SetComponentData(player, t);
            var top = new System.Text.StringBuilder();
            for (int i = 0; i < math.min(3, count); i++)
            {
                var cv = set.Caves[order[i]];
                top.Append($" #{i + 1} cave {order[i]} ({cv.NodeCount} nodes, size {size[order[i]]:F0}, mouth {cv.Mouth.x:F0},{cv.Mouth.y:F0},{cv.Mouth.z:F0})");
            }
            var rankOf = System.Array.IndexOf(order, cave) + 1;
            var box = set.Caves[cave].Max - set.Caves[cave].Min;
            Debug.Log($"DebugControls: gocave {arg}: cave {cave} (size rank {rankOf} of {count}, {set.Caves[cave].NodeCount} nodes, " +
                      $"box {box.x:F0}x{box.y:F0}x{box.z:F0} m), node {node} radius {set.Nodes[node].Radius:F1} at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}), " +
                      $"floor y {floor:F1} (in cave {inCave}), surface y {source.SampleHeight(pos.xz):F1}; largest:{top}");
        }

        /// <summary>
        /// "-lift 15": the player held 15 m over the ground where it stands (as if flying there), and every 3 s a line
        /// with how many creatures within 40 m are fleeing, of how many (the fear distance counts height).
        /// </summary>
        static float liftHeight;
        static float spawnRadius = 45f;
        static bool callLog;
        float callLogNext;
        int callLogFrom;

        /// <summary>"-calllog": every 10 s, the calls played in the window (x6 is a minute), thinned ones, and the biome's sound set.</summary>
        void CallLog()
        {
            if (!callLog || Time.time < callLogNext || !EnsureLocations(out var land) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var self))
                return;
            callLogNext = Time.time + 10f;
            var at = queryWorld.EntityManager.GetComponentData<LocalTransform>(self).Position;
            var played = Punchy.Audio.CreatureAudioSystem.CallsPlayed;
            Debug.Log($"DebugControls: calllog played {played - callLogFrom} in 10 s ({(played - callLogFrom) * 6} a minute), thinned total {Punchy.Audio.CreatureAudioSystem.CallsThinned}, " +
                      $"sounds {Punchy.Fauna.AmbientSounds.At(land, at.xz)} at ({at.x:F0}, {at.z:F0})");
            callLogFrom = played;
        }
        float liftNext;
        int liftSample;
        float liftPass;
        int fixedFrames = -1;

        /// <summary>The -shot name of the shot-list line being run, or null (for log lines outside the shot loop).</summary>
        string CurrentShotName()
        {
            if (shotLines == null || shotIndex < 0 || shotIndex >= shotLines.Count)
                return null;
            var line = shotLines[shotIndex];
            var i = System.Array.IndexOf(line, "-shot");
            return i >= 0 && i + 1 < line.Length ? line[i + 1] : null;
        }
        Punchy.Fauna.CreatureBrainSystem.SearchCounts fixedSearch0;
        (float2 from, float at)? liftPassStart;

        void Lift()
        {
            if (liftHeight <= 0f || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var self) || !EnsureLocations(out var ground))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(self);
            if (liftPass > 0f)
            {
                liftPassStart ??= (t.Position.xz + new float2(-40f, 10f), Time.time);
                var run = math.clamp(Time.time - liftPassStart.Value.at - 5f, 0f, 80f / liftPass);
                t.Position.xz = liftPassStart.Value.from + new float2(run * liftPass, 0f);
            }
            t.Position.y = ground.SampleHeight(t.Position.xz) + liftHeight;
            em.SetComponentData(self, t);
            if (Time.time < liftNext)
                return;
            liftNext = Time.time + 3f;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var director = FindAnyObjectByType<FaunaDirector>();
            int near = 0, fleeing = 0, over = 0;
            float gapSum = 0f, gapMax = 0f, distSum = 0f;
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                // Only deer (the spawned herd), not whatever else wanders near.
                if (director == null || director.SpeciesAt(bodies[i].Species)?.name != "Deer" || math.distance(s.Position.xz, t.Position.xz) >= 40f)
                    continue;
                near++;
                // (A1-09: the height gap to the deer, to see whether slopes put any past the 1.5 m where the weighting starts.)
                var gap = math.abs(s.Position.y - t.Position.y);
                gapSum += gap;
                gapMax = math.max(gapMax, gap);
                if (gap > 1.5f) over++;
                distSum += math.distance(s.Position.xz, t.Position.xz);
                if (s.Mode == CreatureMode.Flee)
                    fleeing++;
            }
            states.Dispose();
            bodies.Dispose();
            Debug.Log($"DebugControls: lift {liftHeight:F1} m flat {Punchy.Fauna.CreatureBrainSystem.HeightFearOff.Data}: {fleeing} of {near} deer within 40 m fleeing (sample {++liftSample}); height gap mean {gapSum / math.max(near, 1):F2} max {gapMax:F2} m, {over} over 1.5 m, mean distance {distSum / math.max(near, 1):F1} m");
        }

        static float[] ParseFloats(string text)
        {
            var parts = text.Split(',');
            var values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                    return null;
            return values;
        }

        /// <summary>The camera set to a pose directly, with whatever moves it switched off (back on in ClearShot).</summary>
        void PoseCamera(Vector3 position, Quaternion rotation)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            foreach (var b in cam.GetComponents<MonoBehaviour>())
                b.enabled = false;
            cam.transform.SetPositionAndRotation(position, rotation);
            cameraPosed = true;
        }

        bool cameraPosed;
        /// <summary>The camera's field of view before the wall shot narrowed it (0: not narrowed).</summary>
        float wallFovWas;

        /// <summary>
        /// "-place cave:Ice" (any cave style): in the first cave of that style, on the floor of its first chamber,
        /// facing back along the passage.
        /// </summary>
        void PlaceInCave(in TerrainSource source, string styleName)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var styles = CaveStyleTable.Collect(palette.Landmarks);
            var style = styles.FindIndex(s => s != null && string.Equals(s.name, styleName, System.StringComparison.OrdinalIgnoreCase));
            if (style < 0)
            {
                Debug.LogWarning($"DebugControls: no cave style '{styleName}' ({string.Join(", ", styles.ConvertAll(s => s != null ? s.name : "-"))})");
                return;
            }
            ref var set = ref source.Caves.Value;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                if (set.Caves[c].Style != style + 1)
                    continue;
                ref var cave = ref set.Caves[c];
                for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                {
                    if (set.Kinds[n] != CaveNodeKind.Chamber)
                        continue;
                    var node = set.Nodes[n].Position;
                    var floor = source.FloorUnder(node + new float3(0f, 0.5f, 0f), out _);
                    var back = n > cave.FirstNode ? set.Nodes[n - 1].Position - node : new float3(0f, 0f, 1f);
                    var em = queryWorld.EntityManager;
                    var t = em.GetComponentData<LocalTransform>(player);
                    t.Position = new float3(node.x, floor + 0.05f, node.z);
                    t.Rotation = quaternion.RotateY(math.atan2(back.x, back.z));
                    em.SetComponentData(player, t);
                    Debug.Log($"DebugControls: in a {styleName} cave ({c}), chamber at ({node.x:F0}, {floor:F0}, {node.z:F0})");
                    return;
                }
            }
            Debug.LogWarning($"DebugControls: no {styleName} cave with a chamber");
        }

        /// <summary>
        /// A-67/A-69 "-place batfolk": on the floor of the first bat folk chamber with a pod (BatFolkHomes, W-96), a few
        /// metres back from the pod's door toward the chamber's middle, facing the pod.
        /// </summary>
        bool PlaceAtBatFolk(in TerrainSource source)
        {
            if (!source.BatFolk.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return false;
            ref var bats = ref source.BatFolk.Value;
            for (int i = 0; i < bats.Sites.Length; i++)
            {
                if (BatFolkHomes.Count(source, BatFolkHomeKind.BatFolkPod, i) == 0 || !BatFolkHomes.Get(source, BatFolkHomeKind.BatFolkPod, i, 0, out var pod))
                    continue;
                var site = bats.Sites[i];
                var at = pod.Door;
                // Back toward the chamber's middle, so the pod and its keeper are in view.
                var back = math.normalizesafe(site.Centre.xz - at.xz, new float2(0f, 1f)) * math.min(4f, math.max(1.5f, site.Radius * 0.5f));
                var stand = new float3(at.x + back.x, at.y + 0.5f, at.z + back.y);
                var floor = source.FloorUnder(stand, out _);
                var em = queryWorld.EntityManager;
                var t = em.GetComponentData<LocalTransform>(player);
                t.Position = new float3(stand.x, floor + 0.05f, stand.z);
                t.Rotation = quaternion.RotateY(math.atan2(-back.x, -back.y));
                em.SetComponentData(player, t);
                Debug.Log($"DebugControls: at bat folk chamber {i}, pod door at ({at.x:F0}, {at.y:F0}, {at.z:F0}), pod floor at y {pod.Stand.y:F1}");
                return true;
            }
            // (Said once, when the retries give up.)
            if (batFolkPlaceUntil > 0f && Time.unscaledTime + 0.1f >= batFolkPlaceUntil)
                Debug.LogWarning("DebugControls: no bat folk chamber with a pod");
            return false;
        }

        /// <summary>
        /// P-5/P-7 "-place temple" (or "temple:N" for room N): the player on a sunken temple room's floor. The rooms are
        /// the ruin kit's layout in the temple's own frame (RuinRooms: u, y above Base, v); without N, the biggest closed
        /// room. Its centre's floor (plus drifted sand) goes back to the world through SunkenTempleShape.World.
        /// </summary>
        void PlaceInTemple(in TerrainSource source, string spec)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player) || !source.Landmarks.IsCreated)
                return;
            ref var set = ref source.Landmarks.Value;
            var temple = -1;
            for (int i = 0; i < set.Instances.Length && temple < 0; i++)
                if (set.Types[set.Instances[i].Type].Temple != 0)
                    temple = i;
            if (temple < 0 || set.RuinRooms.Length == 0)
            {
                Debug.LogWarning($"DebugControls: no sunken temple ({set.RuinRooms.Length} ruin rooms)");
                return;
            }
            var room = -1;
            var colon = spec.IndexOf(':');
            if (colon < 0 || !int.TryParse(spec.Substring(colon + 1), out room) || room < 0 || room >= set.RuinRooms.Length)
            {
                room = -1;
                var best = 0f;
                for (int i = 0; i < set.RuinRooms.Length; i++)
                {
                    var r = set.RuinRooms[i];
                    var area = (r.U1 - r.U0) * (r.V1 - r.V0);
                    if (r.Kind == 0 && area > best)
                        (best, room) = (area, i);
                }
                if (room < 0)
                    room = 0;
            }
            var rr = set.RuinRooms[room];
            float u = (rr.U0 + rr.U1) * 0.5f, v = (rr.V0 + rr.V1) * 0.5f;
            var layout = SunkenTempleShape.Layout(set.Instances[temple]);
            var floor = rr.FloorAt(u) + SunkenTempleShape.Sand(ref set, room, u, v);
            var p = SunkenTempleShape.World(layout, new float3(u, floor + 0.05f, v));
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = p;
            // (Facing along the room's long side.)
            var along = rr.U1 - rr.U0 >= rr.V1 - rr.V0 ? layout.F : layout.R;
            t.Rotation = quaternion.RotateY(math.atan2(along.x, along.y));
            em.SetComponentData(player, t);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            // P-9: the rooms touching this one, and whether each is open to the sky (kind 1: a cut through the ground).
            var touching = new System.Text.StringBuilder();
            for (int j = 0; j < set.RuinRooms.Length; j++)
            {
                var o = set.RuinRooms[j];
                float ou = math.min(rr.U1, o.U1) - math.max(rr.U0, o.U0), ov = math.min(rr.V1, o.V1) - math.max(rr.V0, o.V0);
                if (j != room && ou >= -0.25f && ov >= -0.25f && math.max(ou, ov) >= 0.5f)
                    touching.Append($" {j}{(o.Kind == 1 ? " (open to the sky)" : "")}");
            }
            Debug.Log($"DebugControls: temple room {room} touches:{touching}");
            Debug.Log($"DebugControls: in sunken temple room {room}/{set.RuinRooms.Length} ({rr.U1 - rr.U0:F0} x {rr.V1 - rr.V0:F0} m, ceiling {rr.Ceil - floor:F1} m, kind {rr.Kind}) at ({p.x:F1}, {p.y:F1}, {p.z:F1}); the ground is {source.SampleHeight(p.xz) - p.y:F1} m above");
        }

        /// <summary>
        /// "-place cavestream" ("#2" the second nearest): by the end of the nearest cave stream (where it meets its pool),
        /// the camera a few metres back up the stream looking at the end. Logs the 8 nearest streams' two ends.
        /// </summary>
        void PlaceByCaveStream(in TerrainSource source, string spec)
        {
            if (!source.Caves.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var pick = 1;
            if (spec.StartsWith("#"))
                int.TryParse(spec.Substring(1), out pick);
            ref var set = ref source.Caves.Value;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var ends = new List<(float distance, int stream)>();
            for (int s = 0; s < set.Streams.Length; s++)
            {
                ref var stream = ref set.Streams[s];
                if (stream.PointCount < 2)
                    continue;
                var end = set.StreamPoints[stream.FirstPoint + stream.PointCount - 1].Position;
                ends.Add((math.distance(end.xz, t.Position.xz), s));
            }
            if (ends.Count == 0)
            {
                Debug.LogWarning("DebugControls: no cave streams");
                return;
            }
            ends.Sort((p, q) => p.distance.CompareTo(q.distance));
            for (int k = 0; k < math.min(8, ends.Count); k++)
            {
                ref var stream = ref set.Streams[ends[k].stream];
                var a = set.StreamPoints[stream.FirstPoint].Position;
                var b = set.StreamPoints[stream.FirstPoint + stream.PointCount - 1].Position;
                Debug.Log($"DebugControls: cave stream {ends[k].stream} ({stream.PointCount} points) from ({a.x:F1}, {a.y:F1}, {a.z:F1}) to ({b.x:F1}, {b.y:F1}, {b.z:F1})");
            }
            ref var chosen = ref set.Streams[ends[math.clamp(pick - 1, 0, ends.Count - 1)].stream];
            var last = set.StreamPoints[chosen.FirstPoint + chosen.PointCount - 1].Position;
            // Back up the stream about 5 m (or to its start).
            var back = last;
            for (int k = chosen.PointCount - 2; k >= 0; k--)
            {
                back = set.StreamPoints[chosen.FirstPoint + k].Position;
                if (math.distance(back, last) > 5f)
                    break;
            }
            var floor = source.FloorUnder(back + new float3(0f, 0.5f, 0f), out _);
            t.Position = new float3(back.x, floor + 0.05f, back.z);
            em.SetComponentData(player, t);
            var eye = (Vector3)(back + new float3(0f, 2.5f, 0f));
            PoseCamera(eye, Quaternion.LookRotation((Vector3)last - eye));
            Debug.Log($"DebugControls: by cave stream end ({last.x:F1}, {last.y:F1}, {last.z:F1}), camera {eye.x:F1},{eye.y:F1},{eye.z:F1}");
        }

        /// <summary>
        /// "-place riverfall[#n]" (island v2): below the nth nearest river waterfall of 4-8 m (the small ones), the camera 12 m
        /// downstream at standing eye height looking at the middle of the fall. Logs the 8 nearest small falls.
        /// </summary>
        void PlaceByRiverFall(in TerrainSource source, string spec)
        {
            if (!source.Rivers.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var pick = 1;
            if (spec.StartsWith("#"))
                int.TryParse(spec.Substring(1), out pick);
            ref var falls = ref source.Rivers.Value.Falls;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var near = new List<(float distance, int fall)>();
            for (int f = 0; f < falls.Length; f++)
                if (falls[f].Drop < 8f)
                    near.Add((math.distance(falls[f].Lip.xz, t.Position.xz), f));
            if (near.Count == 0)
            {
                Debug.LogWarning("DebugControls: no small river falls (island v2 only)");
                return;
            }
            near.Sort((p, q) => p.distance.CompareTo(q.distance));
            for (int k = 0; k < math.min(8, near.Count); k++)
            {
                ref var f = ref falls[near[k].fall];
                Debug.Log($"DebugControls: river fall {near[k].fall} drop {f.Drop:F1} width {f.Width:F1} lip ({f.Lip.x:F1}, {f.Lip.y:F1}, {f.Lip.z:F1})");
            }
            ref var chosen = ref falls[near[math.clamp(pick - 1, 0, near.Count - 1)].fall];
            var outDir = math.normalizesafe(chosen.Out);
            var foot = chosen.Lip.xz + outDir * 1.6f;
            var stand = foot + outDir * 12f;
            var ground = source.SampleHeight(stand);
            t.Position = new float3(stand.x, ground + 0.05f, stand.y);
            em.SetComponentData(player, t);
            var eye = new Vector3(stand.x, ground + 1.7f, stand.y);
            var target = new Vector3(chosen.Lip.x, chosen.Lip.y - chosen.Drop * 0.5f, chosen.Lip.z);
            PoseCamera(eye, Quaternion.LookRotation(target - eye));
            Debug.Log($"DebugControls: below river fall (drop {chosen.Drop:F1} m) lip ({chosen.Lip.x:F1}, {chosen.Lip.y:F1}, {chosen.Lip.z:F1}), camera {eye.x:F1},{eye.y:F1},{eye.z:F1}");
        }

        /// <summary>
        /// "-place tree:Oak" (any tree species): beside the nearest tree of that species that stands alone (no other
        /// tree within three cells), the camera framing it from a little way off.
        /// </summary>
        void PlaceByTree(in TerrainSource source, string speciesName)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var list = director.FloraSpeciesList;
            var index = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.Equals(list[i].name, speciesName, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no plant species '{speciesName}'");
                return;
            }
            ref var flora = ref director.FloraPlacementBlob.Value;
            var first = flora.Rules[index].FirstModel;
            var last = first + flora.Rules[index].Variants;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var cellSize = flora.CellSizes[FloraPlacement.TreeLayer];
            var centre = (int2)math.floor(t.Position.xz / cellSize);
            // Outwards in rings, to 400 cells out.
            for (int r = 0; r < 400; r++)
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (math.max(math.abs(dx), math.abs(dz)) != r)
                        continue;
                    var cell = centre + new int2(dx, dz);
                    var plant = FloraPlacement.Place(ref flora, source, cell, FloraPlacement.TreeLayer);
                    if (plant.Model < first || plant.Model >= last)
                        continue;
                    var alone = true;
                    for (int z = -3; z <= 3 && alone; z++)
                        for (int x = -3; x <= 3 && alone; x++)
                            if ((x != 0 || z != 0) && IsTree(ref flora, source, cell + new int2(x, z)))
                                alone = false;
                    if (!alone)
                        continue;
                    var at = plant.Position;
                    var height = flora.Models[plant.Model].Height * plant.Scale;
                    mimicTree = at;
                    mimicTreeHeight = height;
                    mimicTreeSet = true;
                    t.Position = new float3(at.x + 6f, source.SampleHeight(at.xz + new float2(6f, 0f)) + 0.05f, at.z);
                    // Facing the trunk (west), so walking on climbs it.
                    t.Rotation = quaternion.RotateY(-math.PI * 0.5f);
                    em.SetComponentData(player, t);
                    var eye = (Vector3)at + new Vector3(16f, 7f, -16f);
                    PoseCamera(eye, Quaternion.LookRotation((Vector3)at + Vector3.up * height * 0.6f - eye));
                    Debug.Log($"DebugControls: by a lone {speciesName} at ({at.x:F0}, {at.z:F0}), {height:F0} m tall");
                    StartClimbTest(t.Position, at, at + new float3(0f, height * 0.3f, 0f), false, $"trunk at ({at.x:F1}, {at.y:F1}, {at.z:F1})");
                    return;
                }
            Debug.LogWarning($"DebugControls: no lone {speciesName} near");
        }

        /// <summary>A "-place prop:" spec waiting for the world, the player and the flora lookup, and until when (#137).</summary>
        static string propPending;
        static float propPendingUntil;
        const float PropWait = 120f;

        /// <summary>Whether a "-place prop:" request is still waiting (for tests).</summary>
        public static bool PropPlacePending => propPending != null;

        /// <summary>#137: retries a waiting "-place prop:" each frame until it can run, warning if it never can.</summary>
        void PlacePropPending()
        {
            if (propPending == null)
                return;
            if (Time.unscaledTime > propPendingUntil)
            {
                Debug.LogWarning($"DebugControls: -place prop:{propPending} gave up; the flora lookup never came up");
                propPending = null;
            }
            else if (EnsureLocations(out var source) && PlaceByProp(source, propPending))
                propPending = null;
        }

        /// <summary>
        /// "-place prop:Bench" (any plant species, wherever it's placed: a landmark's props, a town's buildings, or the
        /// biome's own scatter): logs the eight nearest with their coordinates and puts the player in front of the
        /// nearest, the camera framing it from a little way off. "prop:Bench#3" picks the third nearest, and
        /// "prop:Bench@115,-401" (or "prop:Bench@115,-401#2") measures from that point instead of the player. For lining
        /// up shots: the logged spot, height and front go straight into -camlook.
        /// </summary>
        /// <remarks>Returns false while the flora lookup or the player isn't up yet, so the request can wait (#137).</remarks>
        bool PlaceByProp(in TerrainSource source, string spec)
        {
            var director = FindAnyObjectByType<FaunaDirector>();
            if (director == null || !director.FloraPlacementBlob.IsCreated || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return false;
            var pick = 1;
            var hash = spec.IndexOf('#');
            if (hash >= 0)
            {
                int.TryParse(spec.Substring(hash + 1), out pick);
                spec = spec.Substring(0, hash);
            }
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var from = t.Position.xz;
            var near = spec.IndexOf('@');
            if (near >= 0)
            {
                if (ParseFloats(spec.Substring(near + 1)) is float[] p && p.Length >= 2)
                    from = new float2(p[0], p[1]);
                spec = spec.Substring(0, near);
            }
            var list = director.FloraSpeciesList;
            var index = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && string.Equals(list[i].name, spec, System.StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index < 0)
            {
                Debug.LogWarning($"DebugControls: no plant species '{spec}'");
                return true;
            }
            ref var flora = ref director.FloraPlacementBlob.Value;
            var first = flora.Rules[index].FirstModel;
            var last = first + flora.Rules[index].Variants;
            var found = new List<(float3 at, float height, float2 front, string where)>();
            // Placed in landmarks (each prop takes the cell holding its spot) or as a landmark's scatter (its layers
            // searched below, with the biome's).
            var scatter = 0;
            if (source.Landmarks.IsCreated)
            {
                ref var set = ref source.Landmarks.Value;
                for (int n = 0; n < set.Instances.Length; n++)
                {
                    var inst = set.Instances[n];
                    if (inst.Type >= flora.LandmarkTypeCount)
                        continue;
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    for (int l = 0; l < 3; l++)
                    {
                        var range = flora.PropRanges[inst.Type * 3 + l];
                        for (int e = range.x; e < range.x + range.y; e++)
                        {
                            var prop = flora.Props[e];
                            if (prop.Species != index)
                                continue;
                            if (prop.Count == 0 && prop.Centrepiece == 0)
                            {
                                scatter |= 1 << l;
                                continue;
                            }
                            for (int k = 0; k < math.max(prop.Count, 1); k++)
                            {
                                var spot = prop.Count == 0 ? inst.Position
                                    : LandmarkSampling.ToWorld(inst, LandmarkLayout.Spot(prop.Ring.x, prop.Angle + k * 360f / prop.Count), radius);
                                TryProp(ref flora, source, (int2)math.floor(spot / flora.CellSizes[l]), l, first, last, inst.Position - spot, $"landmark {n}", found);
                            }
                        }
                    }
                }
            }
            // Town buildings: each plot's building takes the cell holding the plot's centre, facing its street.
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int k = 0; k < towns.Plots.Length; k++)
                {
                    var plot = towns.Plots[k];
                    if (plot.Building < 0 || plot.Building >= flora.SettlementSpecies.Length || flora.SettlementSpecies[plot.Building].x != index)
                        continue;
                    var layer = flora.SettlementSpecies[plot.Building].y;
                    TryProp(ref flora, source, (int2)math.floor(plot.Centre / flora.CellSizes[layer]), layer, first, last, plot.Facing, $"town {plot.Settlement}", found);
                }
            }
            // The biome's own scatter: outwards in rings from the point, in each layer it grows in, to about 1.5 km or
            // 200,000 cells, stopping once eight are found.
            for (int e = 0; e < flora.Entries.Length; e++)
                if (flora.Entries[e].Species == index)
                    for (int r = 0; r < flora.Ranges.Length; r++)
                        if (e >= flora.Ranges[r].x && e < flora.Ranges[r].x + flora.Ranges[r].y)
                            scatter |= 1 << (r % 3);
            for (int l = 0; l < 3; l++)
            {
                if ((scatter & (1 << l)) == 0)
                    continue;
                var size = flora.CellSizes[l];
                var centre = (int2)math.floor(from / size);
                var rings = (int)math.min(1500f / size, math.sqrt(200000f) * 0.5f);
                var before = found.Count;
                for (int r = 0; r <= rings && found.Count - before < 8; r++)
                    for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                        if (math.max(math.abs(dx), math.abs(dz)) == r)
                            TryProp(ref flora, source, centre + new int2(dx, dz), l, first, last, new float2(1f, 0f), "scatter", found);
            }
            if (found.Count == 0)
            {
                Debug.LogWarning($"DebugControls: no {spec} found");
                return true;
            }
            var origin = from;
            found.Sort((a, b) => math.distance(a.at.xz, origin).CompareTo(math.distance(b.at.xz, origin)));
            for (int k = 0; k < math.min(8, found.Count); k++)
            {
                var c = found[k];
                Debug.Log(System.FormattableString.Invariant(
                    $"DebugControls: {spec} #{k + 1} at ({c.at.x:F1}, {c.at.y:F1}, {c.at.z:F1}), {math.distance(c.at.xz, origin):F0} m off, {c.height:F1} m tall, front ({c.front.x:F2}, {c.front.y:F2}), {c.where}"));
            }
            Debug.Log($"DebugControls: {found.Count} {spec} found in all");
            pick = math.clamp(pick, 1, found.Count);
            var chosen = found[pick - 1];
            var back = math.max(6f, chosen.height * 1.6f);
            // From the front, a little to one side.
            var view = math.normalize(chosen.front + new float2(chosen.front.y, -chosen.front.x) * 0.5f);
            var stand = chosen.at.xz + view * (back + 3f);
            t.Position = new float3(stand.x, source.SampleHeight(stand) + 0.05f, stand.y);
            em.SetComponentData(player, t);
            var eyeXZ = chosen.at.xz + view * back;
            var eye = new Vector3(eyeXZ.x, math.max(source.SampleHeight(eyeXZ), chosen.at.y) + 1.5f + chosen.height * 0.4f, eyeXZ.y);
            PoseCamera(eye, Quaternion.LookRotation((Vector3)chosen.at + Vector3.up * chosen.height * 0.45f - eye));
            Debug.Log(System.FormattableString.Invariant($"DebugControls: framing {spec} #{pick} from ({eye.x:F1}, {eye.y:F1}, {eye.z:F1})"));
            return true;
        }

        /// <summary>For PlaceByProp: the plant in a cell, kept if it's one of the species' models and not already listed.</summary>
        static void TryProp(ref FloraBlob flora, in TerrainSource source, int2 cell, int layer, int first, int last, float2 front, string where,
                            List<(float3 at, float height, float2 front, string where)> found)
        {
            var plant = FloraPlacement.Place(ref flora, source, cell, layer);
            if (plant.Model < first || plant.Model >= last)
                return;
            foreach (var e in found)
                if (math.distance(e.at.xz, plant.Position.xz) < 0.5f)
                    return;
            found.Add((plant.Position, flora.Models[plant.Model].Height * plant.Scale, math.normalizesafe(front, new float2(1f, 0f)), where));
        }

        /// <summary>
        /// "-place meadow": the nearest flat, grassy stretch of Grassland, the camera at eye height looking a little
        /// down across it; the player parked out of shot.
        /// </summary>
        static float2? flatSpot;
        static readonly System.Collections.Generic.List<float2> flatSpots = new System.Collections.Generic.List<float2>();
        static readonly System.Collections.Generic.List<float> flatSpreads = new System.Collections.Generic.List<float>();
        static float flatSpread;

        /// <summary>The nearest dry spot (rings of 30 m from the origin) whose ground height, centre and two rings out to 15 m, varies under 0.5 m.</summary>
        static float2? FindFlatSpot(in TerrainSource source, out float spread, System.Collections.Generic.List<float2> awayFrom)
        {
            spread = 0f;
            for (int ring = 0; ring <= 150; ring++)
                for (int k = 0; k < math.max(ring * 8, 1); k++)
                {
                    var angle = k * 2f * math.PI / math.max(ring * 8, 1);
                    var at = new float2(math.cos(angle), math.sin(angle)) * ring * 30f;
                    var near = false;
                    foreach (var other in awayFrom)
                        near |= math.distance(other, at) < 150f;
                    if (near)
                        continue;
                    var lo = float.MaxValue;
                    var hi = float.MinValue;
                    var dry = true;
                    for (int r = 0; r < 17 && dry; r++)
                    {
                        var xz = r == 0 ? at : at + new float2(math.cos(r * math.PI / 4f), math.sin(r * math.PI / 4f)) * (r <= 8 ? 7.5f : 15f);
                        var g = source.SampleGround(xz);
                        dry = !(g.HasWater && g.WaterSurface > g.Height - 0.5f);
                        lo = math.min(lo, g.Height);
                        hi = math.max(hi, g.Height);
                    }
                    if (dry && hi - lo < 0.5f)
                    {
                        spread = hi - lo;
                        return at;
                    }
                }
            return null;
        }

        /// <summary>
        /// "-place crossing" (A-31): beside the first bridge whose seeded roll gave it a fisher (else any with one), the
        /// camera about 9 m off up the bank, looking past the fisher to the bridge and the river.
        /// </summary>
        void PlaceByCrossing(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            RoadCrossings.Refresh(source);
            var pick = RoadCrossings.List.FindIndex(c => c.Fisher && c.HasSpot && c.Bridge);
            if (pick < 0)
                pick = RoadCrossings.List.FindIndex(c => c.Fisher && c.HasSpot);
            if (pick < 0)
            {
                Debug.LogWarning("DebugControls: no crossing with a fisher");
                return;
            }
            var x = RoadCrossings.List[pick];
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            // (The player on the road a little back from the bank, out of the frame behind the camera.)
            var park = x.Bank + x.Outward * 14f;
            t.Position = new float3(park.x, source.SampleHeight(park) + 0.05f, park.y);
            em.SetComponentData(player, t);
            var away = math.normalizesafe(x.Spot - x.Centre, x.Outward);
            var eyeXZ = x.Spot + away * 8f + new float2(-away.y, away.x) * 3f;
            var spotY = source.SampleHeight(x.Spot);
            var eye = new Vector3(eyeXZ.x, math.max(source.SampleHeight(eyeXZ), spotY) + 3f, eyeXZ.y);
            var lookXZ = math.lerp(x.Spot, x.Centre, 0.35f);
            var look = new Vector3(lookXZ.x, spotY + 0.8f, lookXZ.y);
            PoseCamera(eye, Quaternion.LookRotation(look - eye));
            Debug.Log($"DebugControls: at crossing {pick} ({x.Centre.x:F1}, {x.Centre.y:F1}, {(x.Bridge ? "bridge" : "ford")}), fisher spot ({x.Spot.x:F1}, {x.Spot.y:F1})");
        }

        void PlaceInMeadow(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var start = t.Position.xz;
            for (int ring = 1; ring <= 200; ring++)
                for (int k = 0; k < ring * 8; k++)
                {
                    var angle = k * 2f * math.PI / (ring * 8);
                    var at = start + new float2(math.cos(angle), math.sin(angle)) * ring * 25f;
                    if (!IsMeadow(source, palette, at, 0.98f, 0.9f, true))
                        continue;
                    var flat = true;
                    for (int d = 0; d < 8 && flat; d++)
                    {
                        var a = d * math.PI / 4f;
                        flat = IsMeadow(source, palette, at + new float2(math.cos(a), math.sin(a)) * 15f, 0.97f, 0.7f, false);
                    }
                    if (!flat)
                        continue;
                    var height = source.SampleHeight(at);
                    var park = at - new float2(0f, 30f);
                    t.Position = new float3(park.x, source.SampleHeight(park) + 0.05f, park.y);
                    em.SetComponentData(player, t);
                    PoseCamera(new Vector3(at.x, height + 1.7f, at.y), Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f)));
                    Debug.Log($"DebugControls: in a meadow at ({at.x:F0}, {height:F0}, {at.y:F0})");
                    return;
                }
            Debug.LogWarning("DebugControls: no meadow near");
        }

        static bool IsMeadow(in TerrainSource source, BiomePalette palette, float2 at, float flatness, float grass, bool grassland)
        {
            var g = source.SampleGround(at);
            if (g.HasWater)
                return false;
            if (grassland && palette.Biomes[g.Biomes.Dominant()].name != "Grassland")
                return false;
            var n = source.SampleNormal(at, 1f);
            return n.y >= flatness && source.SampleSurface(at, g, n).Grass >= grass;
        }

        /// <summary>
        /// "-place clearlake": on a dry bank beside the nearest clear (not murky) lake at least 1.5 m deep, facing
        /// the water.
        /// </summary>
        void PlaceByClearLake(in TerrainSource source)
        {
            if (!playerQuery.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var em = queryWorld.EntityManager;
            var t = em.GetComponentData<LocalTransform>(player);
            var start = t.Position.xz;
            for (int ring = 1; ring <= 400; ring++)
                for (int k = 0; k < ring * 8; k++)
                {
                    var angle = k * 2f * math.PI / (ring * 8);
                    var at = start + new float2(math.cos(angle), math.sin(angle)) * ring * 12f;
                    var g = source.SampleGround(at);
                    if (!g.HasWater || g.WaterSurface - g.Height < 1.5f || g.River > 0.02f || source.WaterLook(at, g.Biomes, out _).w > 0.05f)
                        continue;
                    for (int d = 0; d < 16; d++)
                    {
                        var a = d * math.PI / 8f;
                        var dir = new float2(math.cos(a), math.sin(a));
                        var stand = at + dir * 8f;
                        var s = source.SampleGround(stand);
                        if (s.HasWater && s.WaterSurface > s.Height - 0.1f)
                            continue;
                        if (s.Height - g.WaterSurface > 4f)
                            continue;
                        t.Position = new float3(stand.x, s.Height + 0.05f, stand.y);
                        t.Rotation = quaternion.RotateY(math.atan2(-dir.x, -dir.y));
                        em.SetComponentData(player, t);
                        lastLake = at;
                        Debug.Log($"DebugControls: by a clear lake at ({at.x:F0}, {at.y:F0}), {g.WaterSurface - g.Height:F1} m deep");
                        return;
                    }
                }
            Debug.LogWarning("DebugControls: no clear lake near");
        }

        /// <summary>
        /// "-place ledge" (after the jump to the nearest settlement, once the props' tops round the player have loaded):
        /// on open ground 1.5 m in front of the nearest rim this body can climb onto, facing it: well over a step and
        /// within its reach (for a human about 1-1.8 m up).
        /// </summary>
        void PlaceByLedge()
        {
            if (!ledgeRequest || Time.timeSinceLevelLoad - optionsAt < settle || !EnsureLocations(out var source))
                return;
            var em = queryWorld.EntityManager;
            var supportQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SupportGrids>(), ComponentType.ReadOnly<FloraSupport>());
            var holder = supportQuery.CalculateEntityCount() == 1 ? supportQuery.GetSingletonEntity() : Entity.Null;
            var grids = holder != Entity.Null ? em.GetComponentData<SupportGrids>(holder) : default;
            if (!grids.Set.IsCreated || em.GetBuffer<FloraSupport>(holder, true).Length == 0)
            {
                // (The tops load a little after the world: give them a while.)
                if (Time.timeSinceLevelLoad - optionsAt > settle + 20f)
                {
                    Debug.LogWarning("DebugControls: -place ledge: no props with tops near");
                    ledgeRequest = false;
                }
                return;
            }
            // (A wall search moving on to another settlement: its props take a moment to load round the player.)
            if (wallRequest && Time.timeSinceLevelLoad < wallSearchAt)
                return;
            ledgeRequest = false;
            var props = em.GetBuffer<FloraSupport>(holder, true);
            var playerEntity = playerQuery.GetSingletonEntity();
            var t = em.GetComponentData<LocalTransform>(playerEntity);
            var bodyQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            var size = bodyQuery.TryGetSingleton<PlayerBody>(out var body) && body.Size > 0f ? body.Size : 1f;
            // As PlayerMovementSystem: a step is 0.4 m, ledges are looked for 0.9 m ahead and climbed up to 1.8 m (all
            // times the body's size).
            float stepUp = 0.4f * size, reach = 0.9f * size, maxRise = 1.8f * size;
            // (A wall for a climber: probed at a human's scale, any wall over a metre and up to 6 m, flat top or not.)
            if (wallRequest)
            {
                stepUp = 0.4f;
                reach = 0.9f;
                maxRise = 6f;
            }
            ref var set = ref grids.Set.Value;
            var best = float.MaxValue;
            float3 bestFeet = default;
            float2 bestForward = default;
            SupportHit bestHit = default;
            // Round each prop, nearest first, walking in from outside it until a ledge comes within reach; the first prop
            // with a flat one will do. (Every probe looks through every prop: this keeps it to a moment.)
            var order = new List<int>();
            for (int p = 0; p < props.Length; p++)
                order.Add(p);
            var playerXZ = t.Position.xz;
            order.Sort((x, y) => math.distancesq(props[x].Position.xz, playerXZ).CompareTo(math.distancesq(props[y].Position.xz, playerXZ)));
            foreach (var p in order)
            {
                if (best < SlopedTop)
                    break;
                var prop = props[p];
                var outer = set.Grids[prop.Grid].Reach * prop.Scale + 2f;
                for (int k = 0; k < 24; k++)
                {
                    var a = k * math.PI / 12f;
                    var dir = new float2(math.cos(a), math.sin(a));
                    for (var d = outer; d > 0.5f; d -= 0.25f)
                    {
                        var xz = prop.Position.xz + dir * d;
                        var g = source.SampleGround(xz);
                        if (g.HasWater && g.WaterSurface > g.Height)
                            break;
                        var feet = new float3(xz.x, g.Height, xz.y);
                        // (Already up on something, a step or a deck: not open ground.)
                        if (PropSupport.Sample(ref set, props, feet, 0.3f, stepUp, out var under) && under.Height > g.Height + 0.05f)
                            break;
                        if (!PropSupport.Ledge(ref set, props, feet, -dir, reach, stepUp, maxRise, out var hit))
                            continue;
                        // The first ledge in reach this way: a real climb (not a tall step), with level ground to set off from.
                        var stand = xz + dir * 1.5f;
                        var level = math.abs(source.SampleHeight(stand) - g.Height) < stepUp &&
                                    !PropSupport.Sample(ref set, props, new float3(stand.x, g.Height, stand.y), 0.3f, stepUp, out _);
                        // A flat top to stand on past the rim (a wall's top, a well's rim, a stall's counter), not the eave
                        // of a sloping roof: level within 15 cm for 1.2 m in and half a metre to either side.
                        var flat = true;
                        var inward = new float3(-dir.x, 0f, -dir.y);
                        var across = new float3(-dir.y, 0f, dir.x);
                        // (A wall's cap is thin: for a wall climber's test only the first 0.4 m in and the sides are asked.)
                        for (int q = 0; q < (wallRequest ? 5 : 5) && flat; q++)
                        {
                            if (wallRequest && (q == 1 || q == 2))
                                continue;
                            var probe = hit.Edge + inward * (q < 3 ? 0.4f * (q + 1) : 0.8f) + across * (q == 3 ? 0.5f : q == 4 ? -0.5f : 0f);
                            probe.y = hit.Height - 0.3f;
                            flat = PropSupport.Sample(ref set, props, probe, 0.2f, 0.6f, out var top) && math.abs(top.Height - hit.Height) < 0.15f;
                        }
                        // (A sloping one only if nothing near has a flat one.)
                        var distance = math.distance(xz, t.Position.xz) + (flat ? 0f : SlopedTop);
                        if (wallRequest && flat && level)
                            wallTallest = math.max(wallTallest, hit.Height - feet.y);
                        // (A-14 #33: a wall climber's test wants a flat-topped wall of WallMin or more.)
                        if (hit.Height - feet.y >= (wallRequest ? WallMin : 0.55f * maxRise) && level && (flat || !wallRequest) && distance < best)
                        {
                            best = distance;
                            bestFeet = feet;
                            bestForward = -dir;
                            bestHit = hit;
                        }
                        break;
                    }
                }
            }
            if (best == float.MaxValue && wallRequest && NextWallSettlement(source, em, playerEntity, t))
                return;
            if (best == float.MaxValue)
            {
                Debug.LogWarning("DebugControls: -place ledge: no ledge in reach near");
                return;
            }
            var from = bestFeet.xz - bestForward * 1.5f;
            t.Position = new float3(from.x, source.SampleHeight(from) + 0.05f, from.y);
            t.Rotation = quaternion.RotateY(math.atan2(bestForward.x, bestForward.y));
            em.SetComponentData(playerEntity, t);
            em.SetComponentData(playerEntity, new PlayerMotion { Grounded = true });
            var rim = bestHit.Edge;
            // Over the rim, onto the top (a little further in than a mantle puts it, so it stops there).
            var over = rim + new float3(bestForward.x, 0f, bestForward.y) * 0.8f;
            if (wallRequest)
            {
                PlayerMovementSystem.WallProbe.Data = 1;
                StartClimbTest(t.Position, rim, rim, false, $"wall at ({V(rim.x)}, {V(rim.y)}, {V(rim.z)}), {bestHit.Height - bestFeet.y:F1} m up; player from ({V(t.Position.x)}, {V(t.Position.z)})");
                climbWall = true;
                // (A-15 #33: the mid-climb shot from 9 m back off the wall face and 1.5 m to one side, looking at the
                // lower half of the wall. A-13/14 framed it from 5 m off to the side, which put the camera inside the
                // building on a long wall and dithered the face away (PlayerReveal); from out in front, the wall is
                // behind the climber and nothing of it is cut.)
                // (Square on to the face itself, by the rim's outward normal: the line from the player to the rim can
                // be well off it, which showed the face edge-on.)
                var wallOut = math.normalizesafe(bestHit.EdgeNormal, math.normalizesafe(t.Position.xz - rim.xz, new float2(0f, -1f)));
                var wallSide = new float2(wallOut.y, -wallOut.x);
                var wallHeight = rim.y - bestFeet.y;
                var eyeXZ = rim.xz + wallOut * 9f + wallSide * 1.5f;
                var eyeY = math.max(source.SampleHeight(eyeXZ) + 1.6f, bestFeet.y + 1.8f);
                var wallEye = new Vector3(eyeXZ.x, eyeY, eyeXZ.y);
                var wallAim = new Vector3(rim.x, bestFeet.y + math.min(wallHeight * 0.4f, 2f), rim.z);
                PoseCamera(wallEye, Quaternion.LookRotation(wallAim - wallEye));
                // (A narrower view, so a lizard a quarter of a metre long reads from 9 m off.)
                if (Camera.main != null)
                {
                    if (wallFovWas == 0f)
                        wallFovWas = Camera.main.fieldOfView;
                    Camera.main.fieldOfView = 28f;
                }
                Debug.Log($"DebugControls: wall camera at ({V(wallEye.x)}, {V(wallEye.y)}, {V(wallEye.z)}) looking at ({V(wallAim.x)}, {V(wallAim.y)}, {V(wallAim.z)}), wall {V(wallHeight)} m");
                return;
            }
            climbWall = false;
            StartClimbTest(t.Position, over, rim, true, $"ledge rim at ({V(rim.x)}, {V(rim.y)}, {V(rim.z)}), {bestHit.Height - bestFeet.y:F1} m up, " +
                                                        (best < SlopedTop ? "flat top" : "sloping top (no flat one near)"));
        }

        /// <summary>How much further off a sloping top counts than a flat one, choosing a ledge (m).</summary>
        const float SlopedTop = 1000f;

        /// <summary>The least height of a flat-topped wall for `-place wall` (m; A-14 #33: a tall garden wall, not a 1 m one).</summary>
        const float WallMin = 2f;
        /// <summary>`-place wall`'s search: the settlements already tried, the tallest flat-topped wall met in them, and when
        /// the next one's props will have loaded.</summary>
        readonly HashSet<int> wallVisited = new();
        float wallTallest, wallSearchAt;

        /// <summary>
        /// No flat-topped wall of WallMin near: logs the tallest met here and moves the player to the nearest settlement not
        /// yet tried, to look again once its props have loaded. False after eight settlements, or with none left.
        /// </summary>
        bool NextWallSettlement(in TerrainSource source, EntityManager em, Entity playerEntity, LocalTransform t)
        {
            if (!source.Settlements.IsCreated || wallVisited.Count >= 8)
                return false;
            ref var towns = ref source.Settlements.Value;
            int here = -1, next = -1;
            float hereDist = float.MaxValue, nextDist = float.MaxValue;
            for (int k = 0; k < towns.Settlements.Length; k++)
            {
                var dist = math.distance(towns.Settlements[k].Centre, t.Position.xz);
                if (dist < hereDist)
                {
                    hereDist = dist;
                    here = k;
                }
            }
            wallVisited.Add(here);
            for (int k = 0; k < towns.Settlements.Length; k++)
            {
                var dist = math.distance(towns.Settlements[k].Centre, t.Position.xz);
                if (!wallVisited.Contains(k) && dist < nextDist)
                {
                    nextDist = dist;
                    next = k;
                }
            }
            Debug.Log($"DebugControls: wall search: no flat-topped wall of {WallMin:F1} m at settlement {here} (tallest flat-topped {wallTallest:F1} m)" +
                      (next >= 0 ? $"; trying settlement {next}, {nextDist:F0} m off" : "; none left"));
            if (next < 0)
                return false;
            wallTallest = 0f;
            var c = towns.Settlements[next].Centre;
            t.Position = new float3(c.x, source.SampleHeight(c) + 0.5f, c.y);
            em.SetComponentData(playerEntity, t);
            em.SetComponentData(playerEntity, new PlayerMotion { Grounded = true });
            ledgeRequest = true;
            wallSearchAt = Time.timeSinceLevelLoad + 6f;
            return true;
        }

        /// <summary>
        /// Logs a climb test's spot, with flags for a client to watch it from the side; and with "-autowalk", starts it
        /// (ClimbTest).
        /// </summary>
        void StartClimbTest(float3 home, float3 target, float3 lookAt, bool jumps, string what)
        {
            var across = math.normalizesafe(target.xz - home.xz, new float2(0f, 1f));
            var side = new float2(across.y, -across.x);
            var mid = (home.xz + target.xz) * 0.5f;
            var eye = new float3(mid.x + side.x * 7f, lookAt.y + 1.5f, mid.y + side.y * 7f);
            // (The client's own body parked behind its camera, out of shot.)
            var park = eye.xz + side * 4f;
            Debug.Log($"DebugControls: climb test: {what}, player from ({V(home.x)}, {V(home.y)}, {V(home.z)}); " +
                      $"to watch: -goto {V(park.x)},{V(park.y)} -camlook {V(eye.x)},{V(eye.y)},{V(eye.z)},{V(lookAt.x)},{V(lookAt.y)},{V(lookAt.z)}");
            if (System.Array.IndexOf(LaunchArgs, "-autowalk") < 0)
                return;
            climbHome = home.xz;
            climbHomeY = home.y;
            climbTarget = target.xz;
            climbJumps = jumps;
            climbSince = Time.time;
        }

        static string V(float v) => v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        float climbNextJump;

        /// <summary>
        /// The climb test, as if the stick were pushed: 12 s at the trunk or over the rim (jumping at a rim, which is
        /// what climbs onto it), then 8 s back to where it started (down the trunk, off the top), and again.
        /// </summary>
        /// <summary>Whether the climb test's player held a trunk last frame (to log each new hold once).</summary>
        static bool climbHolding;
        /// <summary>"-place wall": a wall climber's test (up, along, down), and the last time it logged.</summary>
        static bool wallRequest, climbWall;
        float wallLogAt, wallLastAt, climbHomeY;
        int wallStage;
        float3 wallLast;

        void ClimbTest()
        {
            if (!climbTarget.HasValue || interactRequest != null || !EnsureLocations(out _) ||
                !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            // (The camera stays turned to the climb, so the stick reads forward going out and back coming home.)
            PlayerInputSystem.TestCamera = math.normalizesafe(climbTarget.Value - climbHome, new float2(0f, 1f));
            var cycle = climbWall ? 28f : 20f;
            var phase = math.fmod(Time.time - climbSince, cycle);
            var toward = phase < (climbWall ? 10f : 12f);
            // (A wall: aimed a metre past the rim, so the body keeps pushing into it rather than stopping short.)
            var goal = toward ? climbTarget.Value + (climbWall ? math.normalizesafe(climbTarget.Value - climbHome) : float2.zero) : climbHome;
            var to = goal - player.Position.xz;
            var distance = math.length(to);
            PlayerInputSystem.TestMove = distance > 0.3f ? to / distance : float2.zero;
            // Up a trunk: straight at the trunk it holds, or straight away from it to come down, rather than at the spot
            // logged for it. (The two can be a few tenths of a metre apart; with the feet on the bark that was push enough
            // to the side to walk the player round to the trunk's far side, where heading home climbed instead.)
            if (playerQuery.TryGetSingletonEntity<PlayerTag>(out var self) && queryWorld.EntityManager.GetComponentData<PlayerMotion>(self) is var motion && motion.OnTrunk)
            {
                PlayerInputSystem.TestMove = math.normalizesafe((PlayerMovementSystem.TrunkCentre(motion, player.Position.y) - player.Position.xz) * (toward ? 1f : -1f));
                // Each time it takes hold: where the trunk it holds stands, against the spot logged for it, and which side
                // of it the player is on (to settle which side it climbs).
                if (!climbHolding)
                    Debug.Log($"DebugControls: climb test: took hold of a trunk at ({V(motion.TrunkBase.x)}, {V(motion.TrunkBase.z)}), radius {V(motion.TrunkRadius)}, " +
                              $"{V(math.distance(motion.TrunkBase.xz, climbTarget.Value))} m from the spot logged; player at ({V(player.Position.x)}, {V(player.Position.z)}), " +
                              $"on its {(math.abs(math.cos(motion.TrunkAngle)) > math.abs(math.sin(motion.TrunkAngle)) ? (math.cos(motion.TrunkAngle) > 0f ? "east" : "west") : (math.sin(motion.TrunkAngle) > 0f ? "north" : "south"))} side");
                climbHolding = true;
            }
            else
                climbHolding = false;
            // A wall: early in the climb, pushing right across it (the stick's right against the camera, which looks at
            // the wall), then back down. A log line a second: where on the wall, and which way it's pushed.
            if (climbWall && playerQuery.TryGetSingletonEntity<PlayerTag>(out var wallSelf))
            {
                var wm = queryWorld.EntityManager.GetComponentData<PlayerMotion>(wallSelf);
                var look = math.normalizesafe(PlayerInputSystem.TestCamera ?? new float2(0f, 1f), new float2(0f, 1f));
                // (Early in the climb, 1.5-3.5 s after -place wall: it reaches the top in about 4 s.)
                var sideways = phase >= 1.5f && phase < 3.5f && wm.OnWall;
                if (sideways)
                    PlayerInputSystem.TestMove = new float2(look.y, -look.x);
                // Up / top-out / step-down events, each logged once per cycle.
                if (phase < 0.5f)
                    wallStage = 0;
                var rise = player.Position.y - climbHomeY;
                if (wallStage == 0 && wm.OnWall && rise > 0.5f)
                {
                    wallStage = 1;
                    Debug.Log($"DebugControls: wall test: UP, on the wall {V(rise)} m above the start at {V(phase)} s");
                }
                if (wallStage <= 1 && !wm.OnWall && wm.Grounded && rise > 1f && toward)
                {
                    wallStage = 2;
                    Debug.Log($"DebugControls: wall test: TOP-OUT, standing on the top {V(rise)} m above the start at {V(phase)} s");
                }
                if (wallStage == 2 && !toward && wm.OnWall)
                {
                    wallStage = 3;
                    Debug.Log($"DebugControls: wall test: STEP-DOWN, over the edge and on the wall at {V(rise)} m at {V(phase)} s");
                }
                if (wallStage == 3 && !toward && !wm.OnWall && wm.Grounded && rise < 0.5f)
                {
                    wallStage = 4;
                    Debug.Log($"DebugControls: wall test: DOWN, back on the ground at {V(phase)} s");
                }
                if (Time.time >= wallLogAt)
                {
                    wallLogAt = Time.time + 1f;
                    var stick = sideways ? "right" : toward ? "up" : "down";
                    // (Rates since the last line: up and along the wall, m/s.)
                    var step = player.Position - wallLast;
                    var dtLog = math.max(Time.time - wallLastAt, 0.01f);
                    wallLast = player.Position;
                    wallLastAt = Time.time;
                    Debug.Log($"DebugControls: wall test: test move {PlayerInputSystem.TestMove}, {V(distance)} m from the rim; stick {stick}, OnWall {wm.OnWall}, grounded {wm.Grounded}, player at ({V(player.Position.x)}, {V(player.Position.y)}, {V(player.Position.z)}), vertical {V(step.y / dtLog)} m/s, along {V(math.length(step.xz) / dtLog)} m/s");
                }
            }
            if (toward && climbJumps && distance > 0.5f && distance < 2f && Time.time >= climbNextJump)
            {
                PlayerInputSystem.TestJump = true;
                climbNextJump = Time.time + 0.6f;
            }
        }


        // ---------------------------------------------------------------- right face button checks (A-04)

        /// <summary>
        /// "-actiontest roll:Human,Dog": for each body in turn, 3.5 s: switch to it, jog ahead (speed logged), one roll
        /// with the stick pushed and one standing (distance and length logged; with -shotdir a shot mid-roll).
        /// "-actiontest fly:Pigeon": takes off, climbs, glides, then holds the right face button (steady descent) and the
        /// dive button, logging height and rate each second. "-actiontest fish:Trout" (after -place clearlake): in the
        /// lake, swims, sprints and sinks 5 s, 5 s and 3 s, logging speed and depth against the surface; the camera
        /// pitch is held at the usual downward look so a sprint that follows it would show.
        /// </summary>
        static string actionTest;
        float actionStart;
        int actionSlot;
        float2 actionDir;
        float3 actionMark;
        float actionMarkAt, actionPath, actionY0;
        bool rolling;
        float3 rollFrom;
        float rollAt;
        float actionLogAt;

        void SetBody(string name)
        {
            var avatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
            if (avatars == null)
                return;
            var index = avatars.PlayableSpecies.FindIndex(s => s != null && s.name == name);
            if (index < 0)
                Debug.LogWarning($"DebugControls: no playable body '{name}'");
            else
                avatars.SetLocalSpecies(index);
        }

        void ActionTest()
        {
            if (actionTest == null || !EnsureLocations(out var source) || !playerQuery.TryGetSingletonEntity<LocalTransform>(out var self))
                return;
            var em = queryWorld.EntityManager;
            var player = em.GetComponentData<LocalTransform>(self);
            var motion = em.GetComponentData<PlayerMotion>(self);
            var colon = actionTest.IndexOf(':');
            var kind = actionTest.Substring(0, colon);
            var bodies = actionTest.Substring(colon + 1).Split(',');
            var t = Time.time - actionStart;
            var pos = player.Position;
            if (kind == "roll")
            {
                var slot = (int)(t / 3.5f);
                var lt = t - slot * 3.5f;
                if (slot >= bodies.Length)
                {
                    actionTest = null;
                    PlayerInputSystem.TestMove = null;
                    return;
                }
                if (slot != actionSlot)
                {
                    actionSlot = slot;
                    SetBody(bodies[slot]);
                    rolling = false;
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                }
                // (Jogging from 0.6 s: speed over 0.8 to 1.2 s; the roll with the stick at 1.4 s; a standing one at 2.5 s.)
                PlayerInputSystem.TestMove = lt >= 0.6f && lt < 2.0f ? actionDir : (float2?)null;
                if (lt >= 0.8f && lt < 0.8f + Time.deltaTime)
                {
                    actionMark = pos;
                    actionMarkAt = Time.time;
                }
                if (lt >= 1.2f && lt < 1.2f + Time.deltaTime)
                    Debug.Log($"DebugControls: roll test {bodies[slot]}: jog {math.distance(pos.xz, actionMark.xz) / math.max(Time.time - actionMarkAt, 0.01f):F1} m/s");
                if (lt >= 1.4f && lt < 1.4f + Time.deltaTime || lt >= 2.5f && lt < 2.5f + Time.deltaTime)
                    PlayerInputSystem.TestRoll = true;
                if (shotDir != null && slot == 0 || shotDir != null && bodies[slot] == "Dog" || shotDir != null && bodies[slot] == "Lizard")
                    if (lt >= 1.4f + 0.2f && lt < 1.4f + 0.2f + Time.deltaTime)
                        Shot($"roll-{bodies[slot]}.png");
                if (motion.Roll > 0f && !rolling)
                {
                    rolling = true;
                    rollFrom = pos;
                    rollAt = Time.time;
                }
                else if (motion.Roll <= 0f && rolling)
                {
                    rolling = false;
                    Debug.Log($"DebugControls: roll test {bodies[slot]}: roll {Time.time - rollAt:F2} s, {math.distance(pos.xz, rollFrom.xz):F1} m {(PlayerInputSystem.TestMove.HasValue ? "with the stick" : "standing")}");
                }
                return;
            }
            PlayerInputSystem.TestMove = null;
            if (kind == "fly")
            {
                if (actionSlot < 0)
                {
                    actionSlot = 0;
                    SetBody(bodies[0]);
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                }
                // (Phases: 1.5-9 climbing, 9-12 gliding, 12-16 the right face button, 16-18 the dive button.)
                if (t >= 1.5f && t < 1.5f + Time.deltaTime)
                    PlayerInputSystem.TestJump = true;
                if (t >= 1.5f && t < 19f)
                    PlayerInputSystem.TestMove = actionDir;
                PlayerInputSystem.TestClimb = t >= 1.5f && t < 9f;
                PlayerInputSystem.TestCrouch = t >= 12f && t < 16f;
                PlayerInputSystem.TestSprint = t >= 16f && t < 18f;
                if (t >= actionLogAt && t > 1f)
                {
                    actionLogAt = t + 1f;
                    var ground = source.SampleHeight(pos.xz);
                    var phase = t < 9f ? "climb" : t < 12f ? "glide" : t < 16f ? "crouch" : t < 18f ? "dive" : "end";
                    Debug.Log($"DebugControls: fly test {bodies[0]} t {t:F0} {phase}: flying {motion.Flying}, {pos.y - ground:F1} m up, rate {(pos.y - actionMark.y) / 1f:F1} m/s, airspeed {motion.Airspeed:F1}, pitch {math.degrees(motion.Pitch):F0} deg");
                    actionMark = pos;
                }
                if (t > 19f)
                {
                    actionTest = null;
                    PlayerInputSystem.TestClimb = PlayerInputSystem.TestCrouch = PlayerInputSystem.TestSprint = false;
                    PlayerInputSystem.TestMove = null;
                }
                return;
            }
            if (kind == "fish")
            {
                if (actionSlot < 0)
                {
                    actionSlot = 0;
                    SetBody(bodies[0]);
                    actionDir = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                    fishPhase = 0;
                }
                // Into the lake, 8 m out and 0.6 m under its surface, at 1.0 s.
                if (t >= 1f && t < 1f + Time.deltaTime)
                {
                    var at = lastLake ?? pos.xz + actionDir * 8f;
                    var g = source.SampleGround(at);
                    player.Position = new float3(at.x, g.WaterSurface - 0.6f, at.y);
                    em.SetComponentData(self, player);
                    Debug.Log($"DebugControls: fish test: in the lake at ({at.x:F0}, {at.y:F0}), {g.WaterSurface - g.Height:F1} m deep");
                    actionMark = player.Position;
                    actionPath = 0f;
                    actionMarkAt = 2f;
                    actionY0 = g.WaterSurface;
                }
                var phase = t < 2f ? 0 : t < 7f ? 1 : t < 12f ? 2 : t < 15f ? 3 : 4;
                // (A gentle nose-down while swimming, so it ends mid-depth; the sprint then has the same look-down and must hold.)
                PlayerInputSystem.TestPitch = phase == 1 ? -0.08f : -0.25f;
                // (Back and forth every 1.2 s, so it stays in the water; speed is the path covered.)
                var flip = (int)(t / 1.2f) % 2 == 0 ? 1f : -1f;
                PlayerInputSystem.TestMove = phase == 1 || phase == 2 ? actionDir * flip : (float2?)null;
                PlayerInputSystem.TestSprint = phase == 2;
                PlayerInputSystem.TestCrouch = phase == 3;
                if (phase >= 1 && t > 1.1f)
                {
                    actionPath += math.distance(pos.xz, actionMark.xz);
                    actionMark = pos;
                }
                // At each phase's end: its speed, its depth under the surface, and how that moved.
                var ends = new[] { 2f, 7f, 12f, 15f };
                if (phase > fishPhase)
                {
                    var done = fishPhase;
                    fishPhase = phase;
                    if (done < 1)
                        goto next;
                    var names = new[] { "", "swim", "sprint", "crouch" };
                    var len = ends[done] - ends[done - 1];
                    Debug.Log($"DebugControls: fish test {names[done]}: path speed {actionPath / math.max(len, 0.01f):F1} m/s over {len:F0} s, depth now {actionY0 - pos.y:F2} m under the surface (was {actionY0 - actionMarkY:F2}), camera pitch held at -14 deg");
                    actionPath = 0f;
                    actionMarkY = pos.y;
                }
                next:
                if (t < 2f)
                    actionMarkY = actionY0 - 0.6f;
                if (t > 16f)
                {
                    actionTest = null;
                    PlayerInputSystem.TestSprint = PlayerInputSystem.TestCrouch = false;
                    PlayerInputSystem.TestMove = null;
                    PlayerInputSystem.TestPitch = null;
                }
            }
        }
        float actionMarkY;
        static float2? lastLake;
        int fishPhase;

        static bool IsTree(ref FloraBlob flora, in TerrainSource source, int2 cell)
        {
            var plant = FloraPlacement.Place(ref flora, source, cell, FloraPlacement.TreeLayer);
            return plant.Model >= 0 && plant.Model < flora.Models.Length && flora.Models[plant.Model].Kind != TreeKind.None;
        }

        /// <summary>Back to a clean scene for the next shot: no specimen, spawns or watched animal; the camera free.</summary>
        void ClearShot()
        {
            if (cameraPosed && Camera.main != null)
                foreach (var b in Camera.main.GetComponents<MonoBehaviour>())
                    b.enabled = true;
            cameraPosed = false;
            // (The wall shot's narrower view, put back.)
            if (wallFovWas > 0f && Camera.main != null)
                Camera.main.fieldOfView = wallFovWas;
            wallFovWas = 0f;
            huntShotTaken = false;
            huntShotLogged = false;
            staleHunter = huntHunter;
            huntHunter = Entity.Null;
            colossusShotDistance = 0f;
            mantrapShotMode = null;
            mimicShotMode = null;
            mimicT0 = -1f;
            mimicStartT = mimicEndT = mimicShotT = -1f;
            mimicMaxSpeed = 0f;
            mimicFolded = mimicSettled = mimicRunMarked = false;
            mimicEntity = Entity.Null;
            Mantrap.HoldStrike = false;
            mantrapLungePending = false;
            Mantrap.ThinFog = false;
            if (posed != Entity.Null && queryWorld != null && queryWorld.IsCreated && queryWorld.EntityManager.Exists(posed))
                queryWorld.EntityManager.DestroyEntity(posed);
            posed = Entity.Null;
            liftHeight = 0f;
            actionTest = null;
            climbTarget = null;
            wallRequest = false;
            PlayerInputSystem.TestMove = null;
            PlayerInputSystem.TestSprint = false;
            PlayerInputSystem.TestCrouch = false;
            PlayerInputSystem.TestClimb = false;
            PlayerInputSystem.TestPitch = null;
            PlayerInputSystem.TestCamera = null;
            walkTarget = null;
            walkPath.Clear();
            untilWork = 0f;
            untilAction = 0f;
            workSince = -1f;
            swimSpot = null;
            poseRequest = null;
            watchSpecies = null;
            watchPitchOverride = null;
            watchFrom = null;
            interactRequest = null;
            interactStep = -1f;
            pendingSpawns.Clear();
            watched = Entity.Null;
            ThirdPersonCamera.WatchTarget = null;
            ThirdPersonCamera.WatchSpin = 12f;
        }

        /// <summary>A test's own screenshot into -shotdir, at least 1280 wide (a small Game view is rendered larger).</summary>
        static void Shot(string name) =>
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, name), Mathf.Max(1, Mathf.CeilToInt(1280f / Mathf.Max(Screen.width, 1))));

        /// <summary>Teleports the player to the next (or previous) preset location.</summary>
        public void Jump(int step)
        {
            if (!EnsureLocations(out var source))
                return;
            current = ((current + step) % locations.Count + locations.Count) % locations.Count;
            var location = locations[current];

            var em = queryWorld.EntityManager;
            var player = playerQuery.GetSingletonEntity();
            var transform = em.GetComponentData<LocalTransform>(player);
            transform.Position = new float3(location.Position.x, source.SampleHeight(location.Position) + 0.1f, location.Position.y);
            transform.Rotation = quaternion.RotateY(math.radians(location.Yaw));
            em.SetComponentData(player, transform);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(location.Yaw, 12f);
        }

        float3 locationsKey;

        /// <summary>A-48: the cached queries belong to the live world. Before EnsureLocations has run (a client at join
        /// time) or once a lost host has disposed the world, they are default or stale and TryGetSingleton throws.</summary>
        bool QueriesLive()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
                EnsureLocations(out _);
            return queryWorld == world && queryWorld.IsCreated;
        }

        bool EnsureLocations(out TerrainSource source)
        {
            source = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                floraQuery = em.CreateEntityQuery(ComponentType.ReadOnly<FloraLookup>());
                queryWorld = world;
                locations.Clear();
                centres.Clear();
            }
            if (!terrainQuery.TryGetSingleton(out source) || playerQuery.CalculateEntityCount() != 1 || paletteQuery.CalculateEntityCount() != 1)
                return false;
            // (The baked v1 island comes first and v2 replaces it: find the spots again when the landmarks change, A-29b.)
            var landmarkKey = float3.zero;
            if (source.Landmarks.IsCreated && source.Landmarks.Value.Instances.Length > 0)
                landmarkKey = new float3(source.Landmarks.Value.Instances.Length, source.Landmarks.Value.Instances[0].Position);
            if (math.any(landmarkKey != locationsKey))
            {
                locationsKey = landmarkKey;
                locations.Clear();
                centres.Clear();
            }
            if (locations.Count == 0)
            {
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                FindLocations(source, palette);
                FindWarpTargets(source, palette);
            }
            return locations.Count > 0;
        }

        /// <summary>Finds a representative spot in every land biome plus a few landmarks.</summary>
        void FindLocations(in TerrainSource source, BiomePalette palette)
        {
            locations.Add(new Location { Name = "Start", Position = float2.zero, Yaw = 0f });
            // Straight away: the nearest cave entrance and the nearest settlement, wherever they are.
            if (NearestNotable(source, palette, -1, 1, out var cave, out var caveFacing, out var caveName))
                locations.Add(new Location { Name = $"Nearest cave ({caveName})", Position = cave, Yaw = Yaw(caveFacing - cave) });
            if (NearestNotable(source, palette, -1, 0, out var town, out var townFacing, out var townName))
                locations.Add(new Location { Name = $"Nearest settlement ({townName})", Position = town, Yaw = Yaw(townFacing - town) });

            // Survey the island on a 150 m grid (Burst): dominant biome (or -1 for water) and height.
            var half = source.World.WorldSize * 0.5f;
            const float step = 150f;
            var n = (int)(half * 2f / step) + 1;
            var biomeGrid = new NativeArray<int>(n * n, Allocator.TempJob);
            var heightGrid = new NativeArray<float>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = step, Size = n, Biome = biomeGrid, Height = heightGrid }
                .Schedule(n * n, 64).Complete();

            int At(int x, int z) => x < 0 || z < 0 || x >= n || z >= n ? -2 : biomeGrid[z * n + x];
            float2 ToWorld(int x, int z) => new float2(-half + x * step, -half + z * step);

            var oceanIndex = source.Biomes.Value.OceanIndex;
            for (int b = 0; b < palette.Biomes.Length; b++)
            {
                if (b == oceanIndex)
                    continue;
                // The point most deeply inside the biome: the most rings of same-biome dry land around it.
                int bestX = -1, bestZ = -1, bestScore = -1;
                for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    if (At(x, z) != b)
                        continue;
                    var score = 0;
                    for (int r = 1; r <= 8; r++, score++)
                    {
                        var d = (int)math.round(r * 0.707f);
                        if (At(x + r, z) != b || At(x - r, z) != b || At(x, z + r) != b || At(x, z - r) != b ||
                            At(x + d, z + d) != b || At(x - d, z + d) != b || At(x + d, z - d) != b || At(x - d, z - d) != b)
                            break;
                    }
                    if (score > bestScore) { bestScore = score; bestX = x; bestZ = z; }
                }
                // Rather than open country, stand by the biome's nearest settlement, or failing that a cave entrance.
                if (NearestNotable(source, palette, b, -1, out var notable, out var facing, out var name))
                    locations.Add(new Location { Name = $"{palette.Biomes[b].name}: {name}", Position = notable, Yaw = Yaw(facing - notable) });
                else if (bestScore >= 0)
                {
                    var p = ToWorld(bestX, bestZ);
                    locations.Add(new Location { Name = palette.Biomes[b].name, Position = p, Yaw = Yaw(-p) });
                }
            }

            // Landmarks.
            var top = 0;
            for (int i = 1; i < heightGrid.Length; i++)
                if (heightGrid[i] > heightGrid[top]) top = i;
            var summit = ToWorld(top % n, top / n);
            locations.Add(new Location { Name = $"Summit ({heightGrid[top] - source.World.SeaLevel:F0} m)", Position = summit, Yaw = Yaw(summit) });
            biomeGrid.Dispose();
            heightGrid.Dispose();

            if (source.Rivers.IsCreated)
            {
                ref var map = ref source.Rivers.Value;
                int lake = -1, river = -1;
                float lakeScore = float.MaxValue, riverWidth = 0f;
                for (int i = 0; i < map.Width.Length; i++)
                {
                    var p = map.Point(i);
                    if (map.LakeDepth[i] > 4f && math.length(p) < lakeScore) { lakeScore = math.length(p); lake = i; }
                    // An inland river: well above sea level (not its mouth), and the widest such stretch.
                    if (map.LakeDepth[i] <= 0f && map.Width[i] > riverWidth && map.Down[i] >= 0 && map.Bed[i] > source.World.SeaLevel + 18f)
                    { riverWidth = map.Width[i]; river = i; }
                }
                if (lake >= 0)
                    locations.Add(new Location { Name = "Lake shore", Position = WalkToDryLand(source, map.Point(lake)), Yaw = Yaw(map.Point(lake) - WalkToDryLand(source, map.Point(lake))) });
                if (river >= 0)
                {
                    var p = map.Point(river);
                    var flow = math.normalize(map.Point(map.Down[river]) - p);
                    var bank = p + new float2(-flow.y, flow.x) * (map.Width[river] * 0.5f + 12f);
                    locations.Add(new Location { Name = "River", Position = bank, Yaw = Yaw(p - bank) });
                }
            }

            // Beach: walk outward from the centre until the ground meets the sea.
            for (var r = 500f; r < half * 1.2f; r += 25f)
            {
                var p = new float2(r * 0.8f, r * 0.6f);
                var h = source.SampleHeight(p) - source.World.SeaLevel;
                if (h > 0.5f && h < 3f)
                {
                    locations.Add(new Location { Name = "Beach", Position = p, Yaw = Yaw(p) });
                    break;
                }
            }

            // The landmark of each type nearest the start, seen from just outside it (from the low side of a cliff).
            if (source.Landmarks.IsCreated && palette.Landmarks != null)
            {
                ref var set = ref source.Landmarks.Value;
                for (int t = 0; t < palette.Landmarks.Length; t++)
                {
                    var best = -1;
                    for (int i = 0; i < set.Instances.Length; i++)
                        if (set.Instances[i].Type == t && (best < 0 || math.lengthsq(set.Instances[i].Position) < math.lengthsq(set.Instances[best].Position)))
                            best = i;
                    if (best < 0)
                        continue;
                    ref var inst = ref set.Instances[best];
                    var radius = set.Types[t].Radius * inst.Scale;
                    var away = set.Types[t].Shape == LandmarkShape.Cliff ? -inst.Facing : math.normalizesafe(inst.Position, new float2(1f, 0f));
                    var at = inst.Position + away * radius * 1.1f;
                    locations.Add(new Location { Name = palette.Landmarks[t].name, Position = at, Yaw = Yaw(inst.Position - at) });
                    // (And its middle, a few metres off it, for things that live there: "-place VolcanoCone:centre".)
                    var middle = inst.Position + away * 8f;
                    centres.Add(new Location { Name = palette.Landmarks[t].name + ":centre", Position = middle, Yaw = Yaw(inst.Position - middle) });
                }
            }
        }

        [BurstCompile]
        struct SurveyJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float Origin, Step;
            public int Size;
            [WriteOnly] public NativeArray<int> Biome;
            [WriteOnly] public NativeArray<float> Height;

            public void Execute(int i)
            {
                var p = new float2(Origin + i % Size * Step, Origin + i / Size * Step);
                var g = Source.SampleGround(p);
                Biome[i] = g.WaterDepth < -1f ? g.Biomes.Dominant() : -1;
                Height[i] = g.Height;
            }
        }

        /// <summary>
        /// The settlement (a landmark with upright, humanoid residents) nearest the start in a biome (any biome if
        /// biome is -1), else its nearest cave entrance; or only one kind (kind 0 settlements, 1 caves, -1 either).
        /// Where to stand just outside it (in front of a cliff), and the point to face.
        /// </summary>
        static bool NearestNotable(in TerrainSource source, BiomePalette palette, int biome, int kind, out float2 stand, out float2 facing, out string name)
        {
            stand = facing = default;
            name = null;
            if (!source.Landmarks.IsCreated || palette.Landmarks == null)
                return false;
            ref var set = ref source.Landmarks.Value;
            int best = -1, bestRank = int.MaxValue;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < set.Instances.Length; i++)
            {
                ref var inst = ref set.Instances[i];
                var type = inst.Type < palette.Landmarks.Length ? palette.Landmarks[inst.Type] : null;
                if (type == null)
                    continue;
                var settlement = false;
                foreach (var r in type.Residents)
                    settlement |= r.Species != null && r.Species.Body.Upright;
                var rank = settlement ? 0 : type.Cave ? 1 : -1;
                if (rank < 0 || kind >= 0 && rank != kind)
                    continue;
                source.SampleBiomes(inst.Position, out var biomes, out _);
                if (biome >= 0 && biomes.Dominant() != biome)
                    continue;
                var distance = math.length(inst.Position);
                if (rank < bestRank || rank == bestRank && distance < bestDistance)
                {
                    best = i;
                    bestRank = rank;
                    bestDistance = distance;
                }
            }
            if (best < 0)
                return false;
            ref var chosen = ref set.Instances[best];
            var radius = set.Types[chosen.Type].Radius * chosen.Scale;
            var away = set.Types[chosen.Type].Shape == LandmarkShape.Cliff ? -chosen.Facing : math.normalizesafe(chosen.Position, new float2(1f, 0f));
            stand = chosen.Position + away * radius * 0.9f;
            facing = chosen.Position;
            name = palette.Landmarks[chosen.Type].name;
            return true;
        }

        static float2 WalkToDryLand(in TerrainSource source, float2 waterPoint)
        {
            var dir = math.normalizesafe(-waterPoint, new float2(1f, 0f));
            var p = waterPoint;
            for (int k = 0; k < 200 && source.SampleGround(p).WaterDepth > -0.3f; k++)
                p += dir * 6f;
            return p + dir * 6f;
        }

        static float Yaw(float2 towards) => math.degrees(math.atan2(towards.x, towards.y));

        // ---------------------------------------------------------------- warp keys (A-36)

        // F5-F8 were free (F1 HUD, F3 copy position here; F4 is bound in the input actions). Local player only:
        // nothing goes on the network, and the world data is only read.
        static readonly Key[] WarpKeys = { Key.F5, Key.F6, Key.F7, Key.F8 };
        /// <summary>After a warp the follow camera comes in to at most this (m).</summary>
        const float WarpCameraDistance = 5f;
        /// <summary>The warp camera's pitch: low, under canopy rather than looking down through it.</summary>
        const float WarpPitch = 8f;

        /// <summary>
        /// A-38: the camera's yaw after a warp, kept out of tree crowns. Trees aren't in TerrainSource, so the plants
        /// round the player are read from the flora tables (FloraPlacement.Place, the pure function FloraSystem draws
        /// from; read only). From the landing's own heading, it turns in 30 degree steps either way and takes the first
        /// side whose line from the head out to the lens stays clear of crowns and trunks for the whole arm; if none
        /// does, the side with the longest clear stretch, the arm shortened to it. Returns the heading unchanged if
        /// the flora tables aren't up yet.
        /// </summary>
        float ClearWarpView(in TerrainSource land, float3 head, float yaw, float pitch, ref float arm)
        {
            using var q = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Flora.FloraLookup));
            if (!q.TryGetSingleton<Punchy.Flora.FloraLookup>(out var lookup) || !lookup.Blob.IsCreated)
                return yaw;
            ref var f = ref lookup.Blob.Value;
            var bestYaw = yaw;
            var bestClear = -1f;
            for (int i = 0; i < 12; i++)
            {
                // 0, +30, -30, +60, -60 ... 180.
                var turn = (i + 1) / 2 * 30f * (i % 2 == 0 ? -1f : 1f);
                var y = yaw + turn;
                var rotation = Quaternion.Euler(pitch, y, 0f);
                // Where the lens ends up: lifted over the ground as ThirdPersonCamera lifts it (on a slope that rises
                // behind the player that is well above the 8 degree line, among the crowns), so that line is tested.
                var lens = head - (float3)(rotation * Vector3.forward) * arm;
                var ground = land.SampleGround(lens.xz);
                lens.y = math.max(lens.y, math.max(ground.Height, ground.HasWater ? ground.WaterSurface : float.MinValue) + 0.5f);
                var length = math.distance(head, lens);
                // (As a share of the arm: the lens comes in along its lifted line.)
                var clear = ClearArm(ref f, land, head, (lens - head) / math.max(length, 1e-4f), length) / math.max(length, 1e-4f);
                if (clear >= 0.999f)
                {
                    Debug.Log($"DebugControls: warp camera clear of crowns, turned {turn:F0} deg, arm {arm:F1} m, lens {lens.y - head.y:F1} m over the head");
                    return y;
                }
                if (clear > bestClear + 0.05f)
                {
                    bestClear = clear;
                    bestYaw = y;
                }
            }
            arm = Mathf.Max(bestClear * arm - 0.3f, 1.5f);
            Debug.Log($"DebugControls: warp camera, no side clear of crowns; turned {Mathf.DeltaAngle(yaw, bestYaw):F0} deg, arm {arm:F1} m");
            return bestYaw;
        }

        /// <summary>How far out from the head along <paramref name="dir"/> (unit) the lens can go, up to
        /// <paramref name="length"/>, before it meets a crown or a trunk, in 0.25 m steps. The margin widens to
        /// <c>LensMargin</c> over the last 1.5 m: right at the lens, leaves just off the line still fill the frame.</summary>
        static float ClearArm(ref Punchy.Flora.FloraBlob f, in TerrainSource land, float3 head, float3 dir, float length)
        {
            const float LineMargin = 0.5f, LensMargin = 1.2f;
            var end = head + dir * length;
            var lo = math.min(head.xz, end.xz) - 9f;
            var hi = math.max(head.xz, end.xz) + 9f;
            var t = length;
            for (int layer = 1; layer <= Punchy.Flora.FloraPlacement.TreeLayer; layer++)
            {
                var size = f.CellSizes[layer];
                var c0 = (int2)math.floor(lo / size);
                var c1 = (int2)math.floor(hi / size);
                for (int cy = c0.y; cy <= c1.y; cy++)
                for (int cx = c0.x; cx <= c1.x; cx++)
                {
                    var plant = Punchy.Flora.FloraPlacement.Place(ref f, land, new int2(cx, cy), layer);
                    if (plant.Model < 0 || plant.Model >= f.Models.Length)
                        continue;
                    var shape = f.Models[plant.Model];
                    var s = plant.Scale;
                    var top = plant.Position.y + shape.Height * s;
                    if (shape.Height * s < 1f)
                        continue;
                    var crownBase = plant.Position.y + shape.CrownBase * s;
                    // Walk the line from the head out, stopping at the first point inside this plant.
                    for (var d = 0.5f; d <= t; d += 0.25f)
                    {
                        var margin = d > length - 1.5f ? LensMargin : LineMargin;
                        var crown = shape.CrownRadius * s + margin;
                        var trunk = math.max(shape.TrunkRadius * s, 0.3f) + margin;
                        var p = head + dir * d;
                        var r = math.distance(p.xz, plant.Position.xz);
                        var inCrown = shape.CrownRadius > 0f && r < crown && p.y > crownBase - margin && p.y < top + margin;
                        var inTrunk = shape.TrunkRadius > 0f && r < trunk && p.y < top;
                        if (inCrown || inTrunk)
                        {
                            t = d - 0.25f;
                            break;
                        }
                    }
                }
            }
            return t;
        }
        static readonly string[] WarpNames = { "biome", "settlement", "cave", "landmark" };
        readonly List<Location>[] warpTargets = { new List<Location>(), new List<Location>(), new List<Location>(), new List<Location>() };
        readonly int[] warpIndex = { -1, -1, -1, -1 };
        readonly float2[] warpLast = new float2[4];
        string warpBanner;
        float warpBannerUntil;
        GUIStyle warpStyle;

        /// <summary>"biome", "town"/"settlement"/"city", "cave" or "landmark" to its warp category, else -1.</summary>
        static int WarpCategory(string word) => word?.ToLowerInvariant() switch
        {
            "biome" => 0,
            "town" or "settlement" or "city" => 1,
            "cave" => 2,
            "landmark" => 3,
            _ => -1,
        };

        /// <summary>
        /// F5-F8: the next (step 1) or previous (-1) point of interest of a category. The first press (or the first
        /// after wandering 300 m from the last warp) orders the list by distance from the player, nearest first.
        /// Lands like N and -goto: on the ground, never in water (stand points are walked to dry land).
        /// </summary>
        void Warp(int category, int step)
        {
            if (!EnsureLocations(out var source))
                return;
            var list = warpTargets[category];
            if (list.Count == 0)
            {
                warpBanner = $"F{5 + category} {WarpNames[category]}: none on this island";
                warpBannerUntil = Time.unscaledTime + 3f;
                return;
            }
            var em = queryWorld.EntityManager;
            var player = playerQuery.GetSingletonEntity();
            var transform = em.GetComponentData<LocalTransform>(player);
            var here = transform.Position.xz;
            if (warpIndex[category] < 0 || math.distance(here, warpLast[category]) > 300f)
            {
                list.Sort((a, b) => math.distancesq(a.Position, here).CompareTo(math.distancesq(b.Position, here)));
                warpIndex[category] = step > 0 ? 0 : list.Count - 1;
            }
            else
                warpIndex[category] = ((warpIndex[category] + step) % list.Count + list.Count) % list.Count;
            var target = list[warpIndex[category]];
            warpLast[category] = target.Position;

            transform.Position = new float3(target.Position.x, source.SampleHeight(target.Position) + 0.1f, target.Position.y);
            transform.Rotation = quaternion.RotateY(math.radians(target.Yaw));
            em.SetComponentData(player, transform);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            // Close in and low (A-37): under tree canopy rather than looking down through it; a hill in the way pulls
            // the camera in further (ThirdPersonCamera).
            if (cam != null)
            {
                var head = transform.Position + new float3(0f, cam.TargetHeight, 0f);
                var arm = Mathf.Min(cam.Distance, WarpCameraDistance);
                var yaw = ClearWarpView(source, head, target.Yaw, WarpPitch, ref arm);
                cam.Distance = arm;
                cam.SetView(yaw, WarpPitch);
            }
            warpBanner = $"F{5 + category} {WarpNames[category]}: <b>{target.Name}</b>  {warpIndex[category] + 1}/{list.Count}  ({target.Position.x:F0}, {target.Position.y:F0})";
            warpBannerUntil = Time.unscaledTime + 3f;
            Debug.Log($"DebugControls: warp {WarpNames[category]} {warpIndex[category] + 1}/{list.Count} {target.Name} at {target.Position.x:F0},{target.Position.y:F0}");
        }

        /// <summary>
        /// The warp lists, from the baked world data (read only): one spot per biome region (a connected patch of the
        /// 150 m survey), each settlement, each cave entrance and each landmark.
        /// </summary>
        void FindWarpTargets(in TerrainSource source, BiomePalette palette)
        {
            foreach (var list in warpTargets)
                list.Clear();
            for (int c = 0; c < 4; c++)
                warpIndex[c] = -1;

            // Biome regions: flood-fill the survey grid, keep patches of 4+ cells, stand at each one's deepest cell.
            var half = source.World.WorldSize * 0.5f;
            const float step = 150f;
            var n = (int)(half * 2f / step) + 1;
            var biomeGrid = new NativeArray<int>(n * n, Allocator.TempJob);
            var heightGrid = new NativeArray<float>(n * n, Allocator.TempJob);
            new SurveyJob { Source = source, Origin = -half, Step = step, Size = n, Biome = biomeGrid, Height = heightGrid }
                .Schedule(n * n, 64).Complete();
            var region = new int[n * n];
            var stack = new Stack<int>();
            var cells = new List<int>();
            var oceanIndex = source.Biomes.Value.OceanIndex;
            for (int start = 0; start < n * n; start++)
            {
                var b = biomeGrid[start];
                if (b < 0 || b == oceanIndex || region[start] != 0)
                    continue;
                cells.Clear();
                region[start] = 1;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    cells.Add(i);
                    int x = i % n, z = i / n;
                    if (x > 0 && region[i - 1] == 0 && biomeGrid[i - 1] == b) { region[i - 1] = 1; stack.Push(i - 1); }
                    if (x < n - 1 && region[i + 1] == 0 && biomeGrid[i + 1] == b) { region[i + 1] = 1; stack.Push(i + 1); }
                    if (z > 0 && region[i - n] == 0 && biomeGrid[i - n] == b) { region[i - n] = 1; stack.Push(i - n); }
                    if (z < n - 1 && region[i + n] == 0 && biomeGrid[i + n] == b) { region[i + n] = 1; stack.Push(i + n); }
                }
                if (cells.Count < 4)
                    continue;
                // The deepest cell: the most rings of the same biome around it.
                int best = cells[0], bestScore = -1;
                foreach (var i in cells)
                {
                    int x = i % n, z = i / n, score = 0;
                    for (int r = 1; r <= 8; r++, score++)
                    {
                        bool Same(int dx, int dz) => x + dx >= 0 && z + dz >= 0 && x + dx < n && z + dz < n && biomeGrid[(z + dz) * n + x + dx] == b;
                        if (!Same(r, 0) || !Same(-r, 0) || !Same(0, r) || !Same(0, -r))
                            break;
                    }
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                var p = new float2(-half + best % n * step, -half + best / n * step);
                warpTargets[0].Add(new Location { Name = $"{palette.Biomes[b].name} ({cells.Count * 2.25f:F0} ha)", Position = p, Yaw = Yaw(-p) });
            }
            biomeGrid.Dispose();
            heightGrid.Dispose();

            // Settlements: on the main road just outside, facing the middle.
            var hasLandmarks = source.Landmarks.IsCreated && palette.Landmarks != null;
            if (source.Settlements.IsCreated)
            {
                ref var towns = ref source.Settlements.Value;
                for (int s = 0; s < towns.Settlements.Length; s++)
                {
                    ref var town = ref towns.Settlements[s];
                    var name = "Settlement";
                    if (hasLandmarks && town.Landmark >= 0 && town.Landmark < source.Landmarks.Value.Instances.Length)
                    {
                        var t = source.Landmarks.Value.Instances[town.Landmark].Type;
                        if (t < palette.Landmarks.Length && palette.Landmarks[t] != null)
                            name = palette.Landmarks[t].name;
                    }
                    var road = math.lengthsq(town.Road) > 0.01f ? town.Road : math.normalizesafe(town.Centre, new float2(1f, 0f));
                    var at = Dry(source, town.Centre + road * math.max(town.Radius, town.Palisade) * 1.05f, town.Centre);
                    warpTargets[1].Add(new Location { Name = $"{name} #{s + 1}", Position = at, Yaw = Yaw(town.Centre - at) });
                }
            }

            // Cave entrances: a few metres out from the mouth (away from the first tunnel node), facing in.
            if (source.Caves.IsCreated)
            {
                ref var caves = ref source.Caves.Value;
                for (int c = 0; c < caves.Caves.Length; c++)
                {
                    ref var cave = ref caves.Caves[c];
                    var mouth = cave.Mouth.xz;
                    var outward = cave.NodeCount >= 2
                        ? math.normalizesafe(mouth - caves.Nodes[cave.FirstNode + 1].Position.xz, new float2(1f, 0f))
                        : math.normalizesafe(mouth, new float2(1f, 0f));
                    var at = Dry(source, mouth + outward * 6f, mouth);
                    source.SampleBiomes(mouth, out var biomes, out _);
                    var biome = biomes.Dominant();
                    var where = biome >= 0 && biome < palette.Biomes.Length ? palette.Biomes[biome].name : "cave";
                    warpTargets[2].Add(new Location { Name = $"Cave #{c + 1} ({where})", Position = at, Yaw = Yaw(mouth - at) });
                }
            }

            // Landmarks: every one, seen from just outside (the low side of a cliff), as N does. Not the caves and
            // grottoes (a landmark type with a Cave): F7 already goes to those (A-43, the owner's call).
            if (hasLandmarks)
            {
                ref var set = ref source.Landmarks.Value;
                var caveKinds = new SortedDictionary<string, int>();
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var inst = ref set.Instances[i];
                    if (inst.Type >= palette.Landmarks.Length || palette.Landmarks[inst.Type] == null)
                        continue;
                    if (palette.Landmarks[inst.Type].Cave)
                    {
                        caveKinds.TryGetValue(palette.Landmarks[inst.Type].name, out var seen);
                        caveKinds[palette.Landmarks[inst.Type].name] = seen + 1;
                        continue;
                    }
                    var radius = set.Types[inst.Type].Radius * inst.Scale;
                    var away = set.Types[inst.Type].Shape == LandmarkShape.Cliff ? -inst.Facing : math.normalizesafe(inst.Position, new float2(1f, 0f));
                    var at = Dry(source, inst.Position + away * radius * 1.1f, inst.Position);
                    warpTargets[3].Add(new Location { Name = palette.Landmarks[inst.Type].name, Position = at, Yaw = Yaw(inst.Position - at) });
                }
                var dropped = 0;
                foreach (var kind in caveKinds.Values)
                    dropped += kind;
                Debug.Log($"DebugControls: F8 landmarks {warpTargets[3].Count + dropped} before, {warpTargets[3].Count} after leaving out caves: " +
                          string.Join(", ", System.Linq.Enumerable.Select(caveKinds, k => $"{k.Key} {k.Value}")));
            }

            Debug.Log($"DebugControls: warp keys F5 {WarpNames[0]}, F6 {WarpNames[1]}, F7 {WarpNames[2]}, F8 {WarpNames[3]} (Shift back; F4 is taken by the input actions); targets: biomes {warpTargets[0].Count}, settlements {warpTargets[1].Count}, caves {warpTargets[2].Count}, landmarks {warpTargets[3].Count}");
        }

        /// <summary>A stand point on dry land: if it is in water, walk back toward the thing it looks at until dry.</summary>
        static float2 Dry(in TerrainSource source, float2 p, float2 towards)
        {
            var dir = math.normalizesafe(towards - p, new float2(1f, 0f));
            for (int k = 0; k < 40 && source.SampleGround(p).WaterDepth > -0.3f; k++)
                p += dir * 4f;
            return source.SampleGround(p).WaterDepth > -0.3f ? WalkToDryLand(source, p) : p;
        }

        // ---------------------------------------------------------------- readout

        float copiedUntil;

        /// <summary>F3: the player's position, as "-goto" takes it, onto the clipboard (for reporting a spot).</summary>
        void CopyPosition()
        {
            if (playerQuery.TryGetSingleton<LocalTransform>(out var player))
            {
                // (With the height, A-43: in a cave the spot is under the ground surface, and -goto x,y,z goes back
                // to the floor there.)
                GUIUtility.systemCopyBuffer = $"-goto {player.Position.x:F0},{player.Position.y:F1},{player.Position.z:F0}";
                copiedUntil = Time.unscaledTime + 2f;
                readout = null;
            }
        }

        void OnGUI()
        {
            if (!Application.isPlaying)
                return;
            // The warp banner (F5-F8) shows for a few seconds, top centre, even with the readout hidden.
            if (warpBanner != null && Time.unscaledTime < warpBannerUntil)
            {
                warpStyle ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, richText = true };
                var size = warpStyle.CalcSize(new GUIContent(warpBanner));
                GUI.Box(new Rect((Screen.width - size.x) * 0.5f - 10, 12, size.x + 20, size.y + 10), warpBanner, warpStyle);
            }
            if (!ShowReadout)
                return;
            style ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };

            // The text is rebuilt a few times a second rather than on every GUI event, to keep garbage down.
            if (Time.unscaledTime >= nextReadout || readout == null)
            {
                nextReadout = Time.unscaledTime + 0.25f;
                readout = BuildReadout();
                readoutSize = style.CalcSize(new GUIContent(readout));
            }
            GUI.Box(new Rect(10, 10, readoutSize.x + 12, readoutSize.y + 8), readout, style);
            if (!string.IsNullOrEmpty(hints))
            {
                if (hintStyle == null)
                {
                    hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                    hintStyle.normal.textColor = new Color(0.73f, 0.73f, 0.73f);
                }
                var size = Punchy.UI.Prompts.Measure(hints, hintStyle);
                var at = new Rect(10, 10 + readoutSize.y + 12, size.x + 12, size.y + 6);
                GUI.Box(at, GUIContent.none, style);
                Punchy.UI.Prompts.Draw(new Rect(at.x + 6, at.y + 3, size.x, size.y), hints, hintStyle);
            }
        }

        /// <summary>The player's controls, each by its button on the device in use (unbound ones left out).</summary>
        static string ControlHints(bool flies, bool flying)
        {
            var hints = new List<string>();
            void Add(string button, string what)
            {
                if (!string.IsNullOrEmpty(button))
                    hints.Add($"{button} {what}");
            }
            static string A(string action) => Punchy.Player.InputGlyphs.ActionToken("Player/" + action);
            if (flying)
            {
                Add(A("Jump"), "(hold) climb");
                Add(A("Sprint"), "(hold) dive");
            }
            else
            {
                Add(A("Jump"), flies ? "jump / take off" : "jump");
                Add(A("Sprint"), "sprint on/off");
                Add(A("Walk"), "walk on/off");
                Add(A("Interact"), "pet · hold: pick up");
            }
            var emotes = Punchy.Player.InputGlyphs.Gamepad ? "D-pad up/down/right/left" : "1/2/3/4";
            Add(emotes, "wave/bow/cheer/dance");
            return string.Join(" · ", hints);
        }

        bool readoutUnderground;

        string BuildReadout()
        {
            var lines = new List<string>();
            if (EnsureLocations(out var source) && playerQuery.TryGetSingleton<LocalTransform>(out var player))
            {
                var xz = player.Position.xz;
                var palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
                var ground = source.SampleGround(xz);
                var place = current >= 0 ? locations[current].Name : "-";
                lines.Add($"<b>{place}</b>   biome: {palette.Biomes[ground.Biomes.Dominant()].name}   height {ground.Height - source.World.SeaLevel:F0} m");
                // Where exactly (for reporting a spot: F3 copies it; "-goto x,z" comes back to it).
                var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
                var heading = (math.degrees(math.atan2(forward.x, forward.z)) + 360f) % 360f;
                source.FloorUnder(player.Position + new float3(0f, 0.5f, 0f), out var underground);
                lines.Add($"x <b>{player.Position.x:F0}</b>   y <b>{player.Position.y:F0}</b>   z <b>{player.Position.z:F0}</b>   facing {heading:F0}°" +
                          (underground ? "   <b>in cave</b>" : "") + (copiedUntil > Time.unscaledTime ? "   <color=#9f9>copied</color>" : ""));
                // (Logged whenever it goes into or out of a cave, so a test run shows what the readout said there.)
                if (underground != readoutUnderground)
                {
                    readoutUnderground = underground;
                    Debug.Log($"DebugControls: readout {lines[lines.Count - 1].Replace("<b>", "").Replace("</b>", "")}");
                }
                if (Weather != null && Weather.Field.Ready)
                    lines.Add($"day: <b>{Weather.DayTypeName}</b>   weather here: {Weather.Field.Describe(new Vector2(xz.x, xz.y))}");
            }
            if (Cycle != null)
            {
                var hours = (int)Cycle.TimeOfDay;
                lines.Add($"time {hours:00}:{(int)((Cycle.TimeOfDay - hours) * 60f):00}   {1f / Time.smoothDeltaTime:F0} fps");
            }
            // The frame-rate cap, and the button that changes it on the device in use.
            var cap = Punchy.Settings.FrameRateCap.Current;
            var capButton = Punchy.Player.InputGlyphs.Action("Player/Options");
            lines.Add($"frame cap: <b>{Punchy.Settings.FrameRateCap.Describe(cap)}</b>" +
                      (Punchy.Settings.FrameRateCap.FromCommandLine ? " (-fpscap)" : "") +
                      (string.IsNullOrEmpty(capButton) ? "" : $"   <color=#bbbbbb>{capButton} to change</color>"));
            // The player's pace and the controls, named for the device last used (InputGlyphs). The debug keys have
            // nothing on a gamepad, so they're shown on the keyboard only.
            var flies = false;
            var flying = false;
            if (queryWorld != null && queryWorld.IsCreated)
            {
                using var bodyQuery = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Player.PlayerBody));
                flies = bodyQuery.TryGetSingleton<Punchy.Player.PlayerBody>(out var playerBody) && playerBody.Flies;
                using var motionQuery = queryWorld.EntityManager.CreateEntityQuery(typeof(Punchy.Player.PlayerTag), typeof(Punchy.Player.PlayerMotion));
                flying = motionQuery.TryGetSingleton<Punchy.Player.PlayerMotion>(out var motion) && motion.Flying;
            }
            var pace = flying ? "flying" : Punchy.Player.PlayerInputSystem.Sprinting ? "sprint" : Punchy.Player.PlayerInputSystem.Walking ? "walk" : "jog";
            lines.Add($"pace: <b>{pace}</b>   input: {(Punchy.Player.InputGlyphs.Gamepad ? Punchy.Player.InputGlyphs.PadKind + " gamepad" : "keyboard & mouse")}");
            // (The control hints draw under the readout, so their buttons can be icons.)
            hints = ControlHints(flies, flying) + " · " + Punchy.Player.InputGlyphs.Token(Punchy.Player.InputGlyphs.Gamepad ? "<Gamepad>/start" : "<Keyboard>/escape") + " menu";
            if (!Punchy.Player.InputGlyphs.Gamepad)
            {
                lines.Add("<color=#bbbbbb>N/Shift+N place · B day type · Y +2 h · T fast time · ] force weather · [ clear · F3 copy position · F1 hide</color>");
                lines.Add("<color=#bbbbbb>warp: F5 biome · F6 settlement · F7 cave · F8 landmark (Shift+key back)</color>");
            }

            return string.Join("\n", lines);
        }
    }
}
