using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Animal surface detail (A-92 row 2, #230): each experiment behind its own flag, compared with it off on the horse,
    /// wolf and rabbit up close and at 10 m, and its cost on a 30-horse herd on High, uncapped.
    /// </summary>
    public class DetailTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>The flags under test (the options line is read afresh each time, so every line carries them).</summary>
        static string flags = "";

        static readonly string[] Species = { "Horse", "Wolf", "Rabbit" };

        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Detail_SeamShots() => Shoot("seams");

        [UnityTest, Explicit("bench, run on demand"), Timeout(900000)]
        public IEnumerator Detail_SeamHerd() => Herd("seams");

        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Detail_FurShots() => Shoot("fur");

        [UnityTest, Explicit("bench, run on demand"), Timeout(900000)]
        public IEnumerator Detail_FurHerd() => Herd("fur");

        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Detail_ShellShots() => Shoot("shells");

        [UnityTest, Explicit("bench, run on demand"), Timeout(900000)]
        public IEnumerator Detail_ShellHerd() => Herd("shells");

        /// <summary>Shells only reach a few metres, so also from 3.5 m off one horse of the herd, the rest behind it.</summary>
        [UnityTest, Explicit("bench, run on demand"), Timeout(900000)]
        public IEnumerator Detail_ShellHerdNear() => Herd("shells", true);

        /// <summary>All three kept together (row 3): seams, fur and shells against none, near the herd.</summary>
        [UnityTest, Explicit("bench, run on demand"), Timeout(900000)]
        public IEnumerator Detail_AllHerdNear() => Herd("all", true);

        /// <summary>
        /// Card A-92 shots: each species as the player's body, standing on the flattest spot near a meadow start, from the
        /// side and a little in front at 2 m and 10 m, with the flag off and on.
        /// </summary>
        static IEnumerator Shoot(string flag)
        {
            yield return TestSceneSetup.Place("meadow");
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Horse");
            yield return TestSceneSetup.Seconds(1f);
            var spot = Game.Hook<float[]>("SlopeSpot", 0f, 60f);
            var at = Game.Hook<Vector3>("PlayerPosition");
            Game.Hook<bool>("MovePlayer", spot[0] - at.x, spot[1] - at.z);
            yield return TestSceneSetup.Seconds(2f);
            foreach (var species in Species)
            {
                Game.Hook<string>("SetBody", species);
                yield return TestSceneSetup.Seconds(2.5f);
                var pose = Game.Hook<float[]>("AvatarPose");
                var body = new Vector3(pose[2], pose[6], pose[3]);
                var ahead = new Vector3(Mathf.Sin(pose[5]), 0f, Mathf.Cos(pose[5]));
                var right = new Vector3(ahead.z, 0f, -ahead.x);
                var size = species == "Rabbit" ? 0.35f : 1f;
                var side = (right * 0.85f + ahead * 0.5f).normalized;
                foreach (var far in new[] { false, true })
                    foreach (var on in new[] { false, true })
                    {
                        flags = on ? "" : "-no" + flag;
                        var d = far ? 10f : 2f * size;
                        Cam(body + side * d + Vector3.up * (far ? 1.6f : 0.6f * size), body + Vector3.up * 0.7f * size);
                        yield return Shots.Settled($"a92-{flag}-{species.ToLowerInvariant()}-{(far ? "10m" : "2m")}-{(on ? "on" : "off")}", 1280, 720);
                    }
            }
            flags = "";
            Game.RunOptions("-shotcam off");
            Game.Hook<string>("SetBody", was);
            yield return TestSceneSetup.Seconds(1f);
            Game.Hook<object>("PutBodyPref", pref);
        }

        /// <summary>
        /// Target: card A-92, the experiment costs under 0.3 ms on a 30-horse herd seen whole (from 18 m or more) on High, uncapped.
        /// Frame times (main thread, render thread, GPU) and the pose job's median, with the flag off and on in turn, four
        /// rounds of 4 s each, medians of each round's per-frame times.
        /// </summary>
        static IEnumerator Herd(string flag, bool near = false)
        {
            yield return TestSceneSetup.Place("meadow");
            var spot = Game.Hook<float[]>("SlopeSpot", 0f, 60f);
            // (Spawn waits for a new "spawned" line: a marker first, or a second bench in the same session would see the
            // first one's line again and wait in vain.)
            Debug.Log("DebugControls: spawned nothing yet (A-92 herd bench)");
            yield return TestSceneSetup.Spawn("Horse", spot[0], spot[1], 30, 10f);
            yield return TestSceneSetup.Seconds(3f);
            var was = Game.Hook<string>("BenchStart", "High");
            // (The herd drifts off from the player: framed wherever it is, from far enough to see most of it, near enough
            // that it is drawn at full detail.)
            var herd = Game.Hook<float[]>("HerdAround", "Horse", spot[0], spot[1], 150f);
            var centre = new Vector3(herd[1], herd[2], herd[3]);
            var back = Mathf.Clamp(herd[4] * 1.8f, 18f, 30f);
            if (near)
            {
                var one = Game.Hook<float[]>("SpeciesNear", "Horse", 150f);
                centre = new Vector3(one[2], one[3], one[4]);
                back = 3.5f;
            }
            var off = new List<float>[4];
            var on = new List<float>[4];
            for (int k = 0; k < 4; k++)
            {
                off[k] = new List<float>();
                on[k] = new List<float>();
            }
            var lines = "";
            for (int round = 0; round < 4; round++)
                foreach (var flagOn in new[] { false, true })
                {
                    flags = flagOn ? "" : flag == "all" ? "-noseams -nofur -noshells" : "-no" + flag;
                    Cam(centre + new Vector3(back, back / 3f, 0f), centre + Vector3.up * 1f);
                    yield return TestSceneSetup.Seconds(1.5f);
                    var frames = new[] { new List<float>(), new List<float>(), new List<float>() };
                    Game.Hook<float>("PoseMedian", true);
                    var until = Time.realtimeSinceStartup + 4f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                        {
                            frames[0].Add((float)timing[0].cpuMainThreadFrameTime);
                            frames[1].Add((float)timing[0].cpuRenderThreadFrameTime);
                            frames[2].Add((float)timing[0].gpuFrameTime);
                        }
                    }
                    var m = new[] { Median(frames[0]), Median(frames[1]), Median(frames[2]), Game.Hook<float>("PoseMedian", false) };
                    var into = flagOn ? on : off;
                    for (int k = 0; k < 4; k++)
                        into[k].Add(m[k]);
                    lines += $"{(flagOn ? "on" : "off")} main {m[0]:F2} render {m[1]:F2} gpu {m[2]:F2} pose {m[3]:F3}; ";
                }
            flags = "";
            Game.RunOptions("-shotcam off");
            Game.Hook<string>("BenchEnd", was);
            var names = new[] { "main", "render", "gpu", "pose" };
            var summary = "";
            for (int k = 0; k < 4; k++)
                summary += $"{names[k]} {Median(off[k]):F3} -> {Median(on[k]):F3} ({Median(on[k]) - Median(off[k]):+0.000;-0.000} ms); ";
            LogWatch.Measure($"herd {flag}{(near ? " near" : "")}: {herd[0]:F0} horses from {back:F0} m (four in five within {herd[4]:F0} m, all within {herd[5]:F0} m); {summary}rounds: {lines}");
            Assert.GreaterOrEqual(herd[0], 25f, "no herd to measure");
        }

        static readonly FrameTiming[] timing = new FrameTiming[1];

        static float Median(List<float> v)
        {
            var s = new List<float>(v);
            s.Sort();
            return s.Count == 0 ? 0f : s[s.Count / 2];
        }

        static void Cam(Vector3 from, Vector3 at)
        {
            // (Never with the lens in the ground: at least 0.3 m above it.)
            var ground = Game.Hook<float[]>("FloraHeights", from.x, from.z, 0.01f, 1f);
            if (ground.Length > 0)
                from.y = Mathf.Max(from.y, ground[0] + 0.3f);
            Game.RunOptions(string.Format(CultureInfo.InvariantCulture, "-shotcam {0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2} {6}", from.x, from.y, from.z, at.x, at.y, at.z, flags));
        }
    }
}
