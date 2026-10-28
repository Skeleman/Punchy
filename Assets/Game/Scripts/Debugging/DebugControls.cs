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
    ///  B            next day type (sunny, partly cloudy, changeable, overcast, stormy), replacing today's weather;
    ///  Y            advance the clock two hours;  T  toggle fast time (DayNightCycle);
    ///  ] / [        force the next weather over the player / clear it (WeatherSystem);
    ///  F1           show / hide this readout.
    /// </summary>
    public class DebugControls : MonoBehaviour
    {
        public WeatherSystem Weather;
        public DayNightCycle Cycle;
        public bool ShowReadout = true;

        struct Location
        {
            public string Name;
            public float2 Position;
            public float Yaw;
        }

        readonly List<Location> locations = new List<Location>();
        /// <summary>Landmarks' middles (for -place, not in the N list).</summary>
        readonly List<Location> centres = new List<Location>();
        int current = -1;
        EntityQuery playerQuery, terrainQuery, paletteQuery;
        World queryWorld;
        GUIStyle style;
        string readout;
        Vector2 readoutSize;
        float nextReadout;

        void Update()
        {
            ApplyLaunchOptions();
            SpawnRequested();
            Watch();
            Pose();
            Interact();
            ShotList();
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f1Key.wasPressedThisFrame)
                ShowReadout = !ShowReadout;
            if (keyboard.f3Key.wasPressedThisFrame)
                CopyPosition();
            if (keyboard.bKey.wasPressedThisFrame && Weather != null)
                Weather.NextDayType();
            if (keyboard.yKey.wasPressedThisFrame && Cycle != null)
                Cycle.TimeOfDay = Mathf.Repeat(Cycle.TimeOfDay + 2f, 24f);
            if (keyboard.nKey.wasPressedThisFrame)
                Jump(keyboard.shiftKey.isPressed ? -1 : 1);
        }

        void LateUpdate() => FootDrift();

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
                var down = tip.y < footLowest[foot] + 0.04f && s.Speed > 0.2f && math.frac(s.Phase + part.Phase) < 0.5f;
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
        static float? watchPitchOverride;
        /// <summary>"-spawn Osprey" (or "Trout:6"): a few of a species put near the player once the world is up (testing).</summary>
        static readonly List<string> pendingSpawns = new List<string>();
        /// <summary>"-pose Bat" (or "Bat:fly", "Bat:glide", "Bat:rest", "Bat:hang", "Crow:nest", and a camera pitch: "Bat:glide:70"):
        /// one held beside the player while the camera circles it, for looking at a body (no brain: it stays put,
        /// beating its wings if flying).</summary>
        static string poseRequest;
        Entity posed;
        float2? swimSpot;
        /// <summary>"-interact Cat:carry" (or ":pet"): walks up to the nearest one, pets it or picks it up, and puts it
        /// down again a while later, as if the keys were pressed (alone, hosting or joined).</summary>
        static string interactRequest;
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
            pendingSpawns.Clear();
            poseRequest = null;
            interactRequest = null;
            shotDir = null;
            shotLines.Clear();
            shotIndex = -1;
            Time.timeScale = 1f;
        }
#endif

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

        void ApplyOptions(string[] args)
        {
            optionsAt = Time.timeSinceLevelLoad;
            // "-incave": on the floor of the nearest cave's deepest chamber (after any -place).
            var inCave = System.Array.IndexOf(args, "-incave") >= 0;
            footDrift = System.Array.IndexOf(args, "-footdrift") >= 0;
            // "-nohud": no readout (for screenshots).
            if (System.Array.IndexOf(args, "-nohud") >= 0)
            {
                ShowReadout = false;
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
                // "-watchpitch 45": watch from above (to see over leaves), rather than low or level.
                if (args[i] == "-watchpitch" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var watchPitch))
                    watchPitchOverride = watchPitch;
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
                else if (args[i] == "-place" && args[i + 1].StartsWith("tree:", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var treeSource))
                    PlaceByTree(treeSource, args[i + 1].Substring(5));
                else if (args[i] == "-place" && string.Equals(args[i + 1], "meadow", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var meadowSource))
                    PlaceInMeadow(meadowSource);
                else if (args[i] == "-place" && string.Equals(args[i + 1], "clearlake", System.StringComparison.OrdinalIgnoreCase) && EnsureLocations(out var lakeSource))
                    PlaceByClearLake(lakeSource);
                else if (args[i] == "-place")
                {
                    // An exact name first (the landmark itself), else the first that contains it.
                    var index = locations.FindIndex(l => string.Equals(l.Name, args[i + 1], System.StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                        index = locations.FindIndex(l => l.Name.IndexOf(args[i + 1], System.StringComparison.OrdinalIgnoreCase) >= 0);
                    if (index >= 0)
                    {
                        current = index - 1;
                        Jump(1);
                        Debug.Log($"DebugControls: started at {locations[index].Name}");
                    }
                    else
                    {
                        var middle = centres.FindIndex(l => string.Equals(l.Name, args[i + 1], System.StringComparison.OrdinalIgnoreCase));
                        if (middle >= 0)
                        {
                            locations.Add(centres[middle]);
                            current = locations.Count - 2;
                            Jump(1);
                            Debug.Log($"DebugControls: started at {centres[middle].Name}");
                        }
                    }
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
                    em.SetComponentData(goer, t);
                    Debug.Log($"DebugControls: to {gx:F0},{gz:F0}");
                }
            }
            // "-at Pool" (any AnchorKind): on the floor by the nearest anchor of that kind (after any -place).
            var at = System.Array.IndexOf(args, "-at");
            if (at >= 0 && at + 1 < args.Length && System.Enum.TryParse<AnchorKind>(args[at + 1], true, out var kind) &&
                EnsureLocations(out var terrain))
                GoToAnchor(terrain, kind);
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
                // Cave dwellers go on the cave floor round the player (who should be in a cave: -incave, -at).
                if (director.SpeciesCaveDweller(index))
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
                    var xz = player.Position.xz + rng.NextFloat2Direction() * rng.NextFloat(6f, 45f);
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
        /// Points the camera at the watched species: the nearest one doing something worth seeing (climbing, flying,
        /// in a web, an idle action), else the nearest; re-picked every few seconds.
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
            var index = watchDirector != null ? watchDirector.SpeciesIndex(watchParts[0]) : -1;
            var watchTask = CreatureTask.None;
            var hasTask = watchParts.Length > 1 && System.Enum.TryParse(watchParts[1], true, out watchTask);
            if (index < 0 || !playerQuery.TryGetSingleton<LocalTransform>(out var player))
                return;
            if (Time.unscaledTime >= nextWatchPick || !em.Exists(watched))
            {
                nextWatchPick = Time.unscaledTime + 8f;
                var entities = creatureQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
                var best = float.MaxValue;
                watched = Entity.Null;
                foreach (var e in entities)
                {
                    if (em.GetComponentData<CreatureBody>(e).Species != index)
                        continue;
                    var c = em.GetComponentData<CreatureState>(e);
                    if (hasTask && c.Task != watchTask)
                        continue;
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
                              $"target {math.distance(c.Position.xz, c.Target):F2} m across, {c.TargetHeight - c.Position.y:F2} m up, speed {c.Speed:F2}, " +
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
                em.AddComponent<PosedForTest>(posed);
                Debug.Log($"DebugControls: posing {parts[0]} ({how})");
            }
            var c = em.GetComponentData<CreatureState>(posed);
            ref var b = ref em.GetComponentData<CreatureBody>(posed).Blob.Value;
            var dt = Time.deltaTime;
            var xz = player.Position.xz + new float2(2f, 0f);
            var ground = source.SampleHeight(xz);
            var size = b.BodyLength * c.Scale;
            c.Mode = how == "hang" ? CreatureMode.Roost : CreatureMode.Idle;
            c.Stage = (byte)(how == "hang" ? 200 : 0);
            c.Fly = how == "fly" ? 1f : 0f;
            c.Vertical = how == "hang" ? -1f : 0f;
            c.Speed = how == "fly" ? b.FlySpeed * 0.6f : 0f;
            var onGround = how == "rest" || how == "nest" && b.Nests == NestSite.Ground;
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
                Debug.Log($"DebugControls: interact {act} {parts[0]}");
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
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
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
            if (Time.realtimeSinceStartup - shotStarted < shotWait)
                return;
            // Underground, also until the caves round the camera are built (up to 10 s more).
            var caves = CaveSystem.Underground > 0.5f ? FindAnyObjectByType<CaveSystem>() : null;
            if (caves != null && !caves.Settled && Time.realtimeSinceStartup - shotStarted < shotWait + 10f)
                return;
            Shot((Arg("-shot") ?? $"shot-{shotIndex}") + ".png");
            Debug.Log($"DebugControls: shot {shotIndex + 1}/{shotLines.Count} '{Arg("-shot")}' after {Time.realtimeSinceStartup - shotStarted:F1} s");
            shotIndex++;
            shotStarted = -1f;
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
                    t.Position = new float3(at.x + 6f, source.SampleHeight(at.xz + new float2(6f, 0f)) + 0.05f, at.z);
                    em.SetComponentData(player, t);
                    var eye = (Vector3)at + new Vector3(16f, 7f, -16f);
                    PoseCamera(eye, Quaternion.LookRotation((Vector3)at + Vector3.up * height * 0.6f - eye));
                    Debug.Log($"DebugControls: by a lone {speciesName} at ({at.x:F0}, {at.z:F0}), {height:F0} m tall");
                    return;
                }
            Debug.LogWarning($"DebugControls: no lone {speciesName} near");
        }

        /// <summary>
        /// "-place meadow": the nearest flat, grassy stretch of Grassland, the camera at eye height looking a little
        /// down across it; the player parked out of shot.
        /// </summary>
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
                        Debug.Log($"DebugControls: by a clear lake at ({at.x:F0}, {at.y:F0}), {g.WaterSurface - g.Height:F1} m deep");
                        return;
                    }
                }
            Debug.LogWarning("DebugControls: no clear lake near");
        }

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
            if (posed != Entity.Null && queryWorld != null && queryWorld.IsCreated && queryWorld.EntityManager.Exists(posed))
                queryWorld.EntityManager.DestroyEntity(posed);
            posed = Entity.Null;
            swimSpot = null;
            poseRequest = null;
            watchSpecies = null;
            watchPitchOverride = null;
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
                queryWorld = world;
                locations.Clear();
            }
            if (!terrainQuery.TryGetSingleton(out source) || playerQuery.CalculateEntityCount() != 1 || paletteQuery.CalculateEntityCount() != 1)
                return false;
            if (locations.Count == 0)
                FindLocations(source, queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity()));
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

        // ---------------------------------------------------------------- readout

        float copiedUntil;

        /// <summary>F3: the player's position, as "-goto" takes it, onto the clipboard (for reporting a spot).</summary>
        void CopyPosition()
        {
            if (playerQuery.TryGetSingleton<LocalTransform>(out var player))
            {
                GUIUtility.systemCopyBuffer = $"-goto {player.Position.x:F0},{player.Position.z:F0}";
                copiedUntil = Time.unscaledTime + 2f;
                readout = null;
            }
        }

        void OnGUI()
        {
            if (!ShowReadout || !Application.isPlaying)
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
            static string A(string action) => Punchy.Player.InputGlyphs.Action("Player/" + action);
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
                lines.Add($"x <b>{player.Position.x:F0}</b>   y <b>{player.Position.y:F0}</b>   z <b>{player.Position.z:F0}</b>   facing {heading:F0}°" +
                          (copiedUntil > Time.unscaledTime ? "   <color=#9f9>copied</color>" : ""));
                if (Weather != null && Weather.Field.Ready)
                    lines.Add($"day: <b>{Weather.DayTypeName}</b>   weather here: {Weather.Field.Describe(new Vector2(xz.x, xz.y))}");
            }
            if (Cycle != null)
            {
                var hours = (int)Cycle.TimeOfDay;
                lines.Add($"time {hours:00}:{(int)((Cycle.TimeOfDay - hours) * 60f):00}   {1f / Time.smoothDeltaTime:F0} fps");
            }
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
            lines.Add("<color=#bbbbbb>" + ControlHints(flies, flying) + "</color>");
            if (!Punchy.Player.InputGlyphs.Gamepad)
                lines.Add("<color=#bbbbbb>N/Shift+N place · B day type · Y +2 h · T fast time · ] force weather · [ clear · F3 copy position · F1 hide</color>");

            return string.Join("\n", lines);
        }
    }
}
