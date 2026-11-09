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
    // DebugControls, shot lists, perf samples, cave goto, lift and the posed camera. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        // ---------------------------------------------------------------- shot lists

        /// <summary>
        /// "-shotlist shots.txt" (with -shotdir): many screenshots in one session, instead of a session each. Each line
        /// is the usual test flags for one shot (-place, -goto, -time, -pose, -spawn, -watch, -watchpitch…) plus
        /// "-shot name" (the file, .png added) and optionally "-wait 3" (seconds after the line before the shot; default
        /// 2, plus the world's first build for the first line). Before each line the previous one's specimen, spawns
        /// and camera are cleared. Lines starting # are notes. In the editor, Play mode ends when the list is done.
        /// T-1: the shot itself goes through <see cref="ShotTool"/> (its ready gate, then the camera's own frame), except
        /// lines that show a menu or the HUD ("-menu", "-hud"), which still capture the screen with its overlay.
        /// "-shottimeout 60": the gate's limit, after which the line is logged as a FAIL and has no file.
        /// </summary>
        static readonly List<string[]> shotLines = new List<string[]>();
        static int shotIndex = -1;
        float shotStarted, shotWait, listStarted;
        bool shotPending;

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
            // T-1: the shot tool is at its gate, or shooting; the line ends when it is done or has failed.
            if (shotPending)
            {
                if (ShotTool.Busy)
                    return;
                shotPending = false;
                ShotTaken(line, Arg("-shot"));
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
            var file = (Arg("-shot") ?? $"shot-{shotIndex}") + ".png";
            if (Arg("-menu") != null || System.Array.IndexOf(line, "-hud") >= 0)
                Shot(file);
            else
            {
                // (A creature being watched, or a walk, keeps the camera moving: the gate then doesn't wait for it to stop.)
                var w = Mathf.Max(1280, Screen.width);
                var h = Mathf.Max(1, Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(Screen.width, 1)));
                var timeout = float.TryParse(Arg("-shottimeout"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var st) ? st : ShotTool.DefaultTimeout;
                ShotTool.Take(System.IO.Path.Combine(shotDir, file), w, h, timeout, still: watched == Entity.Null && Arg("-walk") == null);
                shotPending = true;
                return;
            }
            ShotTaken(line, Arg("-shot"));
        }

        /// <summary>The log lines after a shot-list line's shot (or its failure), then on to the next line.</summary>
        void ShotTaken(string[] line, string name)
        {
            string Arg(string flag)
            {
                var i = System.Array.IndexOf(line, flag);
                return i >= 0 && i + 1 < line.Length ? line[i + 1] : null;
            }
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
                      $"{perfCaveTris / c / 1000} k cave tris in view; lights {lights} ({shadowLights} shadowed; LightBudget last frame {Punchy.Settings.LightBudget.Requested} requests, {Punchy.Settings.LightBudget.SlotsUsed} glow slots, {Punchy.Settings.LightBudget.RealUsed}/{Punchy.Settings.GraphicsSettings.RealLights} real, {Punchy.Settings.LightBudget.Dropped} dropped), particles {systems} systems / {particles}; " +
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

        /// <summary>
        /// Item 24: a camera posed by a test flag (-place, -cam, -camrel...) is handed back to the player as soon as
        /// they look round (mouse or right stick) or move, unless a shot list is running. Called every frame.
        /// </summary>
        void FreePosedCameraOnInput()
        {
            if (!cameraPosed || shotIndex >= 0)
                return;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            var keyboard = Keyboard.current;
            var looked = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 4f
                      || pad != null && (pad.rightStick.ReadValue().sqrMagnitude > 0.1f || pad.leftStick.ReadValue().sqrMagnitude > 0.1f)
                      || keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed);
            if (!looked)
                return;
            // (Only the camera: spawns and the rest of a shot's set-up stay.)
            if (Camera.main != null)
                foreach (var b in Camera.main.GetComponents<MonoBehaviour>())
                    b.enabled = true;
            cameraPosed = false;
            Debug.Log("DebugControls: posed camera released (look or move input)");
        }

        /// <summary>The camera's field of view before the wall shot narrowed it (0: not narrowed).</summary>
        float wallFovWas;
    }
}
