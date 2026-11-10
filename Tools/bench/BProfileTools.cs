// TEMPORARY (instance B, profiling): not to be committed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class BProfileTools
{
    public static void Build()
    {
        PlayerSettings.enableFrameTimingStats = true;
        BenchIdentity();
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var r = BuildPipeline.BuildPlayer(scenes, Arg("-out", "Builds/Bench/Punchy.exe"), BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("BProfile build: " + r.summary.result);
        EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    // Bench builds get their own product name, so the -screen-* flags the runner passes are saved in PunchyBench's
    // player prefs, never the real game's (Zach plays fullscreen). The runner reverts ProjectSettings after the build.
    static void BenchIdentity() => PlayerSettings.productName = "PunchyBench";

    public static void BuildDev()
    {
        BenchIdentity();
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var r = BuildPipeline.BuildPlayer(scenes, Arg("-out", "Builds/Profile/Punchy.exe"), BuildTarget.StandaloneWindows64, BuildOptions.Development);
        Debug.Log("BProfile build: " + r.summary.result);
        EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    // Development build with deep profiling support: launched with -deepprofiling, every managed call is a marker
    // (job 32: the island-ready frame's unmarked bake code).
    public static void BuildDeep()
    {
        BenchIdentity();
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var r = BuildPipeline.BuildPlayer(scenes, Arg("-out", "Builds/Profile/Punchy.exe"), BuildTarget.StandaloneWindows64, BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport);
        Debug.Log("BProfile build: " + r.summary.result);
        EditorApplication.Exit(r.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    static double main_ms(int f)
    {
        using var m = ProfilerDriver.GetRawFrameDataView(f, 0);
        return m.valid ? m.frameTimeMs : 0;
    }

    // One "OVER" line: frame, its time into the capture, its ms, the 5 samples with the most self time and the 8 with the
    // most inclusive time below depth 2 (under PlayerLoop's phases), each with its parent's name.
    static string OverLine(UnityEditor.Profiling.RawFrameDataView v, double[] selfMs, int f, int first, double ms, double t0)
    {
        var parent = new int[v.sampleCount];
        var depth = new int[v.sampleCount];
        var stack = new Stack<(int idx, int left)>();
        for (int i = 0; i < v.sampleCount; i++)
        {
            parent[i] = -1;
            if (stack.Count > 0)
            {
                var top = stack.Pop();
                parent[i] = top.idx; depth[i] = depth[top.idx] + 1;
                if (top.left - 1 > 0) stack.Push((top.idx, top.left - 1));
            }
            int c = v.GetSampleChildrenCount(i);
            if (c > 0) stack.Push((i, c));
        }
        string Name(int i) => v.GetSampleName(i) + (parent[i] >= 0 ? " < " + v.GetSampleName(parent[i]) : "");
        var idx = Enumerable.Range(0, v.sampleCount);
        var self = string.Join("; ", idx.OrderByDescending(i => selfMs[i]).Take(5).Select(i => $"{selfMs[i]:F1} {Name(i)}"));
        var incl = string.Join("; ", idx.Where(i => depth[i] >= 2).OrderByDescending(i => v.GetSampleTimeMs(i)).Take(8).Select(i => $"{v.GetSampleTimeMs(i):F1} {Name(i)}"));
        return $"OVER frame {f - first} at {(v.frameStartTimeMs - t0) / 1000.0:F2} s: {ms:F1} ms | self: {self} | incl: {incl}";
    }

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    class Acc { public List<double> incl = new(), self = new(); public double ci, cs, spikeSelf, normalSelf, spikeIncl, normalIncl, maxCall, maxFrame; public int calls; }

    public static void Analyze()
    {
        var inputs = Arg("-raws", "").Split(';');
        var outPath = Arg("-out", "analysis.txt");
        var keep = Arg("-keep", "");   // markers containing this always listed, however small (e.g. NetMenu)
        float skipSec = float.Parse(Arg("-skip", "0"), System.Globalization.CultureInfo.InvariantCulture);
        // -over N (job 44): for every main-thread frame over N ms, its top samples by self time and by inclusive time.
        float overMs = float.Parse(Arg("-over", "0"), System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        foreach (var raw in inputs)
        {
            if (raw.Length == 0) continue;
            ProfilerDriver.ClearAllFrames();
            bool ok = ProfilerDriver.LoadProfile(raw, false);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            sb.AppendLine($"==== {Path.GetFileName(raw)} loaded={ok} frames {first}..{last}");
            var acc = new Dictionary<string, Acc>();
            var frameMs = new List<double>();
            double t0 = -1;
            int used = 0, spikes = 0, normals = 0;
            bool spike = false;
            for (int f = first; f <= last; f++)
            {
                using (var main = ProfilerDriver.GetRawFrameDataView(f, 0))
                {
                    if (!main.valid) continue;
                    if (t0 < 0) t0 = main.frameStartTimeMs;
                    if ((main.frameStartTimeMs - t0) / 1000.0 < skipSec) continue;
                    frameMs.Add(main.frameTimeMs);
                    spike = main.frameTimeMs > 33.3;
                    if (spike) spikes++; else normals++;
                }
                used++;
                foreach (var a in acc.Values) { a.ci = 0; a.cs = 0; }
                for (int t = 0; ; t++)
                {
                    using var v = ProfilerDriver.GetRawFrameDataView(f, t);
                    if (!v.valid) break;
                    string group = t == 0 ? "Main" : v.threadName.StartsWith("Worker") || v.threadName.StartsWith("Job") ? "Workers" : v.threadName == "Render Thread" ? "Render" : null;
                    if (group == null) continue;
                    var stack = new Stack<(int idx, int left)>();
                    var selfMs = new double[v.sampleCount];
                    for (int i = 0; i < v.sampleCount; i++)
                    {
                        double incl = v.GetSampleTimeMs(i);
                        selfMs[i] = incl;
                        if (stack.Count > 0)
                        {
                            var top = stack.Pop();
                            selfMs[top.idx] -= incl;
                            if (top.left - 1 > 0) stack.Push((top.idx, top.left - 1));
                        }
                        int c = v.GetSampleChildrenCount(i);
                        if (c > 0) stack.Push((i, c));
                    }
                    if (t == 0 && overMs > 0 && main_ms(f) > overMs)
                        sb.AppendLine(OverLine(v, selfMs, f, first, main_ms(f), t0));
                    for (int i = 0; i < v.sampleCount; i++)
                    {
                        string key = group + "|" + v.GetSampleName(i);
                        if (!acc.TryGetValue(key, out var a)) { a = new Acc(); for (int k = 1; k < used; k++) { a.incl.Add(0); a.self.Add(0); } acc[key] = a; }
                        a.ci += v.GetSampleTimeMs(i);
                        a.maxCall = Math.Max(a.maxCall, v.GetSampleTimeMs(i));
                        a.cs += selfMs[i];
                        a.calls++;
                    }
                }
                foreach (var a in acc.Values) { a.incl.Add(a.ci); a.self.Add(a.cs); a.maxFrame = Math.Max(a.maxFrame, a.ci); if (spike) { a.spikeSelf += a.cs; a.spikeIncl += a.ci; } else { a.normalSelf += a.cs; a.normalIncl += a.ci; } }
            }
            sb.AppendLine($"SPIKES (frames > 33.3 ms): {spikes} of {spikes + normals}. key, spike-frame self ms, normal-frame self ms, delta, spike incl, normal incl");
            foreach (var kv in acc.OrderByDescending(k => k.Value.spikeSelf / Math.Max(1, spikes) - k.Value.normalSelf / Math.Max(1, normals)).Take(40))
                sb.AppendLine($"SPIKE {kv.Key}	{kv.Value.spikeSelf / Math.Max(1, spikes):F2}	{kv.Value.normalSelf / Math.Max(1, normals):F2}	{kv.Value.spikeSelf / Math.Max(1, spikes) - kv.Value.normalSelf / Math.Max(1, normals):F2}	{kv.Value.spikeIncl / Math.Max(1, spikes):F2}	{kv.Value.normalIncl / Math.Max(1, normals):F2}");
            frameMs.Sort();
            sb.AppendLine($"frames used {used}, frame ms median {Med(frameMs):F2} mean {(frameMs.Count > 0 ? frameMs.Average() : 0):F2} p95 {(frameMs.Count > 0 ? frameMs[(int)(frameMs.Count * 0.95)] : 0):F2}");
            // The last two: the longest single call and the most one frame spent on it (one big job, e.g. a cave-mouth chunk).
            sb.AppendLine("key\tmedInclMs\tmeanInclMs\tmedSelfMs\tmeanSelfMs\tcallsPerFrame\tmaxCallMs\tmaxFrameMs");
            foreach (var kv in acc.OrderByDescending(k => k.Value.self.Average()))
            {
                var a = kv.Value;
                double ms = a.self.Average();
                // Brain.* marks always show: some are empty counters (Brain.Moving), read by their calls a frame.
                if (ms < 0.03 && a.incl.Average() < 0.3 && !kv.Key.Contains("|Brain.") && (keep.Length == 0 || !kv.Key.Contains(keep))) continue;
                sb.AppendLine($"{kv.Key}\t{Med(a.incl):F3}\t{a.incl.Average():F3}\t{Med(a.self):F3}\t{ms:F3}\t{(double)a.calls / Math.Max(1, used):F1}\t{a.maxCall:F2}\t{a.maxFrame:F2}");
            }
        }
        File.WriteAllText(outPath, sb.ToString());
        EditorApplication.Exit(0);
    }

    static double Med(List<double> l)
    {
        if (l.Count == 0) return 0;
        var s = l.OrderBy(x => x).ToList();
        return s[s.Count / 2];
    }
}
