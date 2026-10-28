using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Punchy.CameraRig;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Entities;
using Unity.Profiling;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// A repeatable performance run, started with the -benchroute launch flag: it visits a town, a forest full of
    /// animals, the inside of the biggest cave, a storm and water views, then crosses terrain chunks at sprint, glide
    /// and stress speeds. It settles at each stop, then measures frame times and logs one "BenchRoute:" line per stop
    /// (average fps, 1% lows, worst frame, CPU vs GPU time when the build has Frame Timing Stats on, and when the
    /// slow frames happened) before quitting. Use it with -weatherseed and compare builds by the logged numbers. Places
    /// come from DebugControls' own list (by name), so the route follows the world as it changes.
    /// </summary>
    public class BenchmarkRoute : MonoBehaviour
    {
        const float Settle = 10f, Measure = 20f;

        readonly List<float> frameMs = new List<float>();
        readonly List<double> cpuMs = new List<double>(), gpuMs = new List<double>();
        readonly FrameTiming[] timing = new FrameTiming[1];
        readonly StringBuilder summary = new StringBuilder();
        bool measuring;
        int gcAtStart;
        // Graphics and process memory at the end of each stop (does streaming fill video memory over a route?).
        ProfilerRecorder gfxUsed, gfxReserved, systemUsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchroute") < 0)
                return;
            var go = new GameObject("BenchmarkRoute");
            DontDestroyOnLoad(go);
            go.AddComponent<BenchmarkRoute>();
        }

        void OnDestroy()
        {
            gfxUsed.Dispose();
            gfxReserved.Dispose();
            systemUsed.Dispose();
        }

        IEnumerator Start()
        {
            gfxUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            gfxReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Reserved Memory");
            systemUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            // Wait for the world, the player and DebugControls' place list.
            DebugControls controls = null;
            while (controls == null || !TryGetWorld(out _, out _))
            {
                controls = FindAnyObjectByType<DebugControls>();
                yield return null;
            }
            yield return new WaitForSecondsRealtime(Settle);

            yield return Stop("town", () => JumpTo(controls, "HumanVillage") && SetWeather("Partly cloudy", 11));
            // Walking through the village (streets, plots, roads leading out), at walking pace.
            yield return Stop("townwalk", () => JumpTo(controls, "HumanVillage") && SetWeather("Partly cloudy", 24), 6f, 0.1f, 30f);
            yield return Stop("forest", () => JumpTo(controls, "OakGrove") && SetWeather("Partly cloudy", 12));
            yield return Stop("cave", () => PlaceInBiggestCave() && SetWeather("Partly cloudy", 13));
            yield return Stop("lavacave", () => PlaceInBiggestCave("LavaTube") && SetWeather("Partly cloudy", 22));
            // An elf grove late at night: the worst case for glowing things.
            yield return Stop("nightgrove", () => JumpTo(controls, "ElfGrove") && SetWeather("Partly cloudy", 23), hour: 23f);
            yield return Stop("storm", () => JumpTo(controls, "Mountains") && SetWeather("Stormy", 14));
            // Water filling much of the view: a lake shore, a river (often rapids), and the shore again in a storm.
            yield return Stop("lake", () => JumpTo(controls, "Lake shore") && SetWeather("Partly cloudy", 19));
            yield return Stop("river", () => JumpTo(controls, "River") && SetWeather("Partly cloudy", 20));
            yield return Stop("stormshore", () => JumpTo(controls, "Lake shore") && SetWeather("Stormy", 21));
            // Travel across terrain chunks: a sprint and a glide at play speeds (spikes here matter), then an 80 m/s
            // dash as a stress figure only (fast travel will sit behind a loading screen).
            yield return Stop("run", () => JumpTo(controls, "Start") && SetWeather("Partly cloudy", 15), 12f, 0.1f, 40f);
            yield return Stop("glide", () => JumpTo(controls, "Start") && SetWeather("Partly cloudy", 16), 25f, 15f, 40f);
            yield return Stop("dash", () => JumpTo(controls, "Start") && SetWeather("Partly cloudy", 17), 80f, 3f);
            // -benchspot x,z,yaw (on the ground) or x,y,z,yaw (on the floor under that point, e.g. in a cave): also
            // measure one spot, with a screenshot of it when -benchshot <file> is given.
            var args = System.Environment.GetCommandLineArgs();
            var spotArg = System.Array.IndexOf(args, "-benchspot");
            if (spotArg >= 0 && spotArg + 1 < args.Length)
            {
                var v = System.Array.ConvertAll(args[spotArg + 1].Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                var at = v.Length >= 4 ? new float3(v[0], v[1], v[2]) : new float3(v[0], float.NaN, v[1]);
                var yaw = v.Length >= 4 ? v[3] : v.Length > 2 ? v[2] : 0f;
                yield return Stop("spot", () => PlaceAt(at, yaw) && SetWeather("Partly cloudy", 18));
                var shot = System.Array.IndexOf(args, "-benchshot");
                if (shot >= 0 && shot + 1 < args.Length)
                {
                    ScreenCapture.CaptureScreenshot(args[shot + 1]);
                    yield return new WaitForSecondsRealtime(1f);
                }
            }

            Debug.Log("BenchRoute: done\n" + summary);
            Application.Quit();
        }

        /// <summary>
        /// One stop: arrive, settle, measure. With a speed, the player is carried in a straight line at that speed
        /// (at a height above the ground) the whole time, so streaming has to keep up; settling is included, so the
        /// measured part is already at speed.
        /// </summary>
        IEnumerator Stop(string name, System.Func<bool> arrive, float speed = 0f, float height = 0f, float measure = Measure, float hour = 12f)
        {
            // -benchstops run,glide runs only the listed stops (to profile one of them on its own).
            var args = System.Environment.GetCommandLineArgs();
            var only = System.Array.IndexOf(args, "-benchstops");
            if (only >= 0 && only + 1 < args.Length && System.Array.IndexOf(args[only + 1].Split(','), name) < 0)
                yield break;
            // -benchlong <stop> <seconds>: measure that stop for longer (effects that build up over time).
            var longArg = System.Array.IndexOf(args, "-benchlong");
            if (longArg >= 0 && longArg + 2 < args.Length && args[longArg + 1] == name)
                measure = float.Parse(args[longArg + 2], System.Globalization.CultureInfo.InvariantCulture);
            if (!arrive())
            {
                Debug.LogWarning($"BenchRoute: {name}: couldn't get there, skipped");
                yield break;
            }
            // Every stop at its own hour (noon unless asked), so a night stop doesn't carry over into the next.
            var cycle = FindAnyObjectByType<DayNightCycle>();
            if (cycle != null)
                cycle.TimeOfDay = hour;
            float3 start = default;
            if (speed > 0f && TryGetWorld(out var em, out var player))
                start = em.GetComponentData<LocalTransform>(player).Position;
            // -profilestop <stop> <file>: record a Profiler capture of just that stop's measured part (Development
            // builds only), so a long route doesn't fill the disk.
            var profile = System.Array.IndexOf(args, "-profilestop");
            var profiling = profile >= 0 && profile + 2 < args.Length && args[profile + 1] == name;
            var t = 0f;
            frameMs.Clear(); cpuMs.Clear(); gpuMs.Clear();
            while (t < Settle + measure)
            {
                t += Time.unscaledDeltaTime;
                if (speed > 0f)
                    Carry(start + math.normalize(new float3(1f, 0f, 0.3f)) * speed * t, height);
                if (!measuring && t > Settle)
                {
                    gcAtStart = System.GC.CollectionCount(0);   // garbage collections during the measured part
                    if (profiling)
                    {
                        UnityEngine.Profiling.Profiler.logFile = args[profile + 2];
                        UnityEngine.Profiling.Profiler.enableBinaryLog = true;
                        UnityEngine.Profiling.Profiler.maxUsedMemory = 256 * 1024 * 1024;
                        UnityEngine.Profiling.Profiler.enabled = true;
                    }
                }
                measuring = t > Settle;
                yield return null;
            }
            measuring = false;
            if (profiling)
            {
                UnityEngine.Profiling.Profiler.enabled = false;
                UnityEngine.Profiling.Profiler.logFile = "";
            }
            Report(name);
            // -benchshots <folder>: a screenshot of every stop as it ends, to compare looks between builds.
            var shots = System.Array.IndexOf(args, "-benchshots");
            if (shots >= 0 && shots + 1 < args.Length)
            {
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(args[shots + 1], name + ".png"));
                yield return null;
            }
        }

        void Update()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (!measuring)
                return;
            frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                cpuMs.Add(timing[0].cpuFrameTime);
                gpuMs.Add(timing[0].gpuFrameTime);
            }
        }

        void Report(string name)
        {
            if (frameMs.Count == 0)
                return;
            var sorted = new List<float>(frameMs);
            sorted.Sort();
            float total = 0f;
            foreach (var ms in sorted) total += ms;
            // 1% low: the average frame rate over the slowest 1% of frames.
            var worst = math.max(1, sorted.Count / 100);
            float worstTotal = 0f;
            for (int i = sorted.Count - worst; i < sorted.Count; i++) worstTotal += sorted[i];
            var line = $"{name}: avg {1000f * sorted.Count / total:F1} fps, 1% low {1000f * worst / worstTotal:F1} fps, " +
                       $"median {sorted[sorted.Count / 2]:F1} ms, worst {sorted[sorted.Count - 1]:F1} ms, " +
                       $"cpu {Average(cpuMs)} ms, gpu {Average(gpuMs)} ms, frames {sorted.Count}, GC {System.GC.CollectionCount(0) - gcAtStart}, gfx {Megabytes(gfxUsed)}/{Megabytes(gfxReserved)} MB used/reserved, system {Megabytes(systemUsed)} MB";
            // When the hitches happen: the worst frame and the frames over 33 ms in each 5 s of the measured part.
            var windows = new StringBuilder();
            float windowTime = 0f, windowWorst = 0f;
            int windowSlow = 0;
            foreach (var ms in frameMs)
            {
                windowTime += ms;
                windowWorst = math.max(windowWorst, ms);
                if (ms > 33.3f) windowSlow++;
                if (windowTime >= 5000f)
                {
                    windows.Append($" {windowWorst:F0}/{windowSlow}");
                    windowTime = windowWorst = 0f;
                    windowSlow = 0;
                }
            }
            line += $", per 5 s worst ms/slow frames:{windows}";
            summary.AppendLine(line);
            Debug.Log("BenchRoute: " + line);
        }

        static string Megabytes(ProfilerRecorder recorder) =>
            recorder.Valid ? (recorder.LastValue / (1024 * 1024)).ToString() : "n/a";

        static string Average(List<double> values)
        {
            if (values.Count == 0)
                return "n/a";   // Frame Timing Stats is off in this build
            double sum = 0;
            foreach (var v in values) sum += v;
            return (sum / values.Count).ToString("F1");
        }

        /// <summary>
        /// Gives the stop the same weather on every run: a fixed day type, its cells reseeded from a fixed random state
        /// (the weather otherwise drifts apart between runs, as new cells form at frame-rate-dependent moments).
        /// </summary>
        static bool SetWeather(string dayType, int seed)
        {
            var weather = FindAnyObjectByType<WeatherSystem>();
            var index = weather != null ? weather.DayTypes.FindIndex(d => d.Name == dayType) : -1;
            if (index < 0)
                return false;
            UnityEngine.Random.InitState(seed);
            weather.SetDayType(index, true);
            return true;
        }

        /// <summary>Jumps to the first of DebugControls' places whose name contains the text (as -place does).</summary>
        static bool JumpTo(DebugControls controls, string place)
        {
            var type = typeof(DebugControls);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var ensure = type.GetMethod("EnsureLocations", flags);
            var list = type.GetField("locations", flags)?.GetValue(controls) as IList;
            var current = type.GetField("current", flags);
            if (ensure == null || list == null || current == null || !(bool)ensure.Invoke(controls, new object[] { null }))
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                var name = list[i].GetType().GetField("Name")?.GetValue(list[i]) as string;
                if (name == null || name.IndexOf(place, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                current.SetValue(controls, i - 1);
                controls.Jump(1);
                return true;
            }
            return false;
        }

        /// <summary>Puts the player on the floor of the widest spot in the cave with the most nodes.</summary>
        /// <summary>
        /// Puts the player at a point facing yaw degrees: on the ground when y is NaN, else on the floor under (x, y, z),
        /// which may be a cave's.
        /// </summary>
        static bool PlaceAt(float3 at, float yaw)
        {
            if (!TryGetWorld(out var em, out var player))
                return false;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (terrainQuery.IsEmpty)
                return false;
            var source = terrainQuery.GetSingleton<TerrainSource>();
            var floor = float.IsNaN(at.y) ? source.SampleHeight(at.xz) : source.FloorUnder(at, out _);
            em.SetComponentData(player, new LocalTransform { Position = new float3(at.x, floor + 0.1f, at.z), Rotation = quaternion.RotateY(math.radians(yaw)), Scale = 1f });
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(yaw, 12f);
            return true;
        }

        /// <summary>
        /// Puts the player on the floor of the widest spot in the cave with the most nodes, optionally only among caves
        /// of a style (by asset name; read by reflection, as branches without cave styles must still compile).
        /// </summary>
        static bool PlaceInBiggestCave(string style = null)
        {
            if (!TryGetWorld(out var em, out var player))
                return false;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (terrainQuery.IsEmpty)
                return false;
            var source = terrainQuery.GetSingleton<TerrainSource>();
            if (!source.Caves.IsCreated)
                return false;
            ref var set = ref source.Caves.Value;
            IList styles = null;
            FieldInfo styleField = null;
            if (style != null)
            {
                var caves = FindAnyObjectByType<CaveSystem>();
                styles = caves != null ? typeof(CaveSystem).GetField("styles", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(caves) as IList : null;
                styleField = typeof(CaveData).GetField("Style");
                if (styles == null || styleField == null)
                    return false;
            }
            int best = -1;
            for (int c = 0; c < set.Caves.Length; c++)
            {
                if (style != null)
                {
                    // Style slots count from 1 (slot 0 is the material's own plain rock).
                    var slot = (int)styleField.GetValue(set.Caves[c]);
                    if (slot < 1 || slot > styles.Count || (styles[slot - 1] as Object)?.name != style)
                        continue;
                }
                if (best < 0 || set.Caves[c].NodeCount > set.Caves[best].NodeCount)
                    best = c;
            }
            if (best < 0)
                return false;
            ref var cave = ref set.Caves[best];
            int node = cave.FirstNode;
            for (int n = cave.FirstNode; n < cave.FirstNode + cave.NodeCount; n++)
                if (set.Nodes[n].Radius > set.Nodes[node].Radius)
                    node = n;
            var p = set.Nodes[node].Position;
            var next = set.Nodes[node == cave.FirstNode ? node + 1 : node - 1].Position;
            var yaw = math.degrees(math.atan2(next.x - p.x, next.z - p.z));
            em.SetComponentData(player, new LocalTransform { Position = new float3(p.x, source.FloorUnder(p, out _) + 0.1f, p.z), Rotation = quaternion.RotateY(math.radians(yaw)), Scale = 1f });
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
            var cam = FindAnyObjectByType<ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(yaw, 10f);
            return true;
        }

        /// <summary>Moves the player to a point the given height above the ground there.</summary>
        static void Carry(float3 at, float height)
        {
            if (!TryGetWorld(out var em, out var player))
                return;
            using var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (terrainQuery.IsEmpty)
                return;
            var source = terrainQuery.GetSingleton<TerrainSource>();
            var transform = em.GetComponentData<LocalTransform>(player);
            transform.Position = new float3(at.x, source.SampleHeight(at.xz) + height, at.z);
            em.SetComponentData(player, transform);
            em.SetComponentData(player, new PlayerMotion { Grounded = true });
        }

        static bool TryGetWorld(out EntityManager em, out Entity player)
        {
            em = default;
            player = Entity.Null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (query.CalculateEntityCount() != 1)
                return false;
            player = query.GetSingletonEntity();
            return true;
        }
    }
}
