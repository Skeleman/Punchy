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
    ///  -shotcam x,y,z,tx,ty,tz  (shot lists / PlayArgs) pins the camera at x,y,z looking at tx,ty,tz for every frame of
    ///               every later shot until "-shotcam off", so a movement strip keeps its subject in frame (A-79).
    /// The player-facing panels (multiplayer, graphics, quit) are in the main menu on Esc or Start (UI.MainMenu).
    /// </summary>
    public partial class DebugControls : MonoBehaviour
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
        /// <summary>A-78: the landmarks key "-place batfolk" last placed on, and whether it found a chamber at all.</summary>
        float3 batFolkPlaceKey;
        bool batFolkPlaced;
        float landmarkPlaceUntil;
        bool landmarkPlaced;
        string speciesPickPending;
        float speciesPickUntil;
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

        // (No GUILayout in OnGUI: skips IMGUI's layout pass and its garbage every frame, A-82.)
        void Awake() => useGUILayout = false;

        void Update()
        {
            FreePosedCameraOnInput();
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
            ClimbWatch();
            ActionTest();
            Watch();
            SettleGoto();
            FrameUpdate();
            WalkTo();
            FlyCave();
            DungeonWalk();
            PlacePropPending();
            PlaceAtLandmarkPending();
            PickSpeciesPending();
            PlaceAtBatFolkPending();
            RockWatch();
            Pose();
            FishProbe();
            FishLineup();
            FishLurePose();
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
            BreathTest();
            DiveTest();
            PlayerDiveTest();
            NetWatch();
            ShotCam();
        }

        /// <summary>A-79: "-shotcam": a fixed camera that holds across shot-list lines (set in ApplyOptions, never in normal play).</summary>
        bool shotCamOn;
        Vector3 shotCamFrom, shotCamTo;

        /// <summary>Poses the camera at the pinned "-shotcam" point every frame, after every other shot helper, so it wins.</summary>
        void ShotCam()
        {
            if (shotCamOn)
                PoseCamera(shotCamFrom, Quaternion.LookRotation(shotCamTo - shotCamFrom));
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
