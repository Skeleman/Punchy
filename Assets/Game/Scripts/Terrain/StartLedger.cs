using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// W-168 row 1: where the time from launch to "world ready" goes, so we can choose what to bake into the shipped
    /// world file. It prints one "[start]" line when the terrain stream first reports world ready (TerrainStreamingSystem),
    /// then stops. It does not time frames for a lane to judge (rule 21); a BENCH job reads the line.
    /// Two sources:
    /// - World producers add their own main-thread time with <see cref="Add"/> (terrain chunks, the far ring, caves). Their
    ///   background batches add the time each batch was in flight with <see cref="Flight"/>. This works in every build.
    /// - Everything else (plants, fauna, colossi, particles, other systems and jobs) is read from Profiler markers whose
    ///   names match <see cref="Watch"/>: main-thread time and time on all threads. Those markers only exist in
    ///   development (Profile) builds and the editor; in a release build that part of the line says so.
    /// While loading it costs one marker scan a second and a few reads a frame. After world ready it costs nothing.
    /// </summary>
    public static class StartLedger
    {
        class Entry
        {
            public double Main, Worst, Flight;
            public int Calls, Items;
        }

        /// <summary>Marker names containing any of these are summed (case sensitive).</summary>
        static readonly string[] Watch =
        {
            "Terrain", "Cave", "Water", "Flora", "Fauna", "Coloss", "Landmark", "Iceberg", "Ribcage", "SaltTower",
            "SunkenTemple", "Anchor", "Settlement", "GroundDetail", "Grass", "Driftwood", "Critter", "Ambien", "Weather",
            "Climate", "Folk", "Creature", "BuildChunkMesh", "ParticleSystem.EndUpdateAll", "Island", "Interior", "Glow"
        };

        const int TopMarkers = 30;

        class Marker
        {
            public string Name;
            public ProfilerRecorder Main, All;
            public double MainSum, AllSum, Worst;
        }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static readonly List<Marker> markers = new List<Marker>();
        static readonly HashSet<string> markerNames = new HashSet<string>();
        static readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        static readonly Dictionary<string, double> stamps = new Dictionary<string, double>();
        static readonly List<string> stampOrder = new List<string>();
        static double nextScan;
        static int frames;
        static bool done;

        /// <summary>Starts empty on every entry to Play mode (the editor may keep statics between plays).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear()
        {
            foreach (var m in markers)
            {
                m.Main.Dispose();
                m.All.Dispose();
            }
            entries.Clear();
            markers.Clear();
            markerNames.Clear();
            stamps.Clear();
            stampOrder.Clear();
            nextScan = 0;
            frames = 0;
            done = false;
        }

        /// <summary>True until the ledger has printed (only then is anything recorded).</summary>
        public static bool Open => !done;

        /// <summary>Adds main-thread work (ms) a producer did this frame, with how many things it made.</summary>
        public static void Add(string producer, double ms, int items = 0)
        {
            if (done) return;
            var e = Get(producer);
            e.Main += ms;
            e.Worst = System.Math.Max(e.Worst, ms);
            e.Calls++;
            e.Items += items;
        }

        /// <summary>Adds the time (ms) one of a producer's background batches was in flight, start to pick-up.</summary>
        public static void Flight(string producer, double ms)
        {
            if (done) return;
            Get(producer).Flight += ms;
        }

        /// <summary>Notes the first time a stage was reached (seconds since start), e.g. "island ready".</summary>
        public static void Stamp(string stage)
        {
            if (done || stamps.ContainsKey(stage)) return;
            stamps[stage] = Time.realtimeSinceStartupAsDouble;
            stampOrder.Add(stage);
        }

        static Entry Get(string producer)
        {
            if (!entries.TryGetValue(producer, out var e))
                entries[producer] = e = new Entry();
            return e;
        }

        /// <summary>Once a frame on the main thread while loading: picks up new markers and adds last frame's marker times.</summary>
        public static void Tick()
        {
            if (done) return;
            frames++;
            var now = Time.realtimeSinceStartupAsDouble;
            if (now >= nextScan)
            {
                nextScan = now + 1.0;
                Scan();
            }
            foreach (var m in markers)
            {
                var main = m.Main.Valid ? m.Main.LastValue * 1e-6 : 0.0;
                m.MainSum += main;
                m.Worst = System.Math.Max(m.Worst, main);
                if (m.All.Valid) m.AllSum += m.All.LastValue * 1e-6;
            }
        }

        static void Scan()
        {
            handles.Clear();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || markerNames.Contains(d.Name) || !Watched(d.Name))
                    continue;
                markerNames.Add(d.Name);
                markers.Add(new Marker
                {
                    Name = d.Name,
                    Main = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.StartImmediately),
                    All = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately)
                });
            }
        }

        static bool Watched(string name)
        {
            foreach (var w in Watch)
                if (name.Contains(w))
                    return true;
            return false;
        }

        /// <summary>Prints the "[start]" line once (world ready), then frees the recorders and records nothing more.</summary>
        public static void Report(int chunks, string world)
        {
            if (done) return;
            done = true;
            var text = new StringBuilder();
            text.Append($"[start] world ready at {Time.realtimeSinceStartupAsDouble:F1} s, {chunks} chunks, {frames} frames ({world}).");
            if (stampOrder.Count > 0)
            {
                text.Append(" Stages (s):");
                foreach (var s in stampOrder)
                    text.Append($" {s} {stamps[s]:F1};");
            }
            text.Append(" World producers (main ms sum / worst frame / batches in flight ms / calls / items):");
            var list = new List<KeyValuePair<string, Entry>>(entries);
            list.Sort((a, b) => b.Value.Main.CompareTo(a.Value.Main));
            foreach (var kv in list)
                text.Append($" {kv.Key} {kv.Value.Main:F0} / {kv.Value.Worst:F0} / {kv.Value.Flight:F0} / {kv.Value.Calls} / {kv.Value.Items};");
            var valid = false;
            foreach (var m in markers)
                valid |= m.Main.Valid;
            if (!valid)
                text.Append(" Profiler markers: none (not a development build).");
            else
            {
                markers.Sort((a, b) => System.Math.Max(b.AllSum, b.MainSum).CompareTo(System.Math.Max(a.AllSum, a.MainSum)));
                text.Append($" Top markers of {markers.Count} watched (main ms sum / worst frame / all threads ms):");
                for (int i = 0; i < markers.Count && i < TopMarkers; i++)
                    text.Append($" {markers[i].Name} {markers[i].MainSum:F0} / {markers[i].Worst:F0} / {markers[i].AllSum:F0};");
            }
            foreach (var m in markers)
            {
                m.Main.Dispose();
                m.All.Dispose();
            }
            markers.Clear();
            Debug.Log(text.ToString());
        }
    }
}
