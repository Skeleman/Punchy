using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Lying legs (A-92 row 1, #231): every four-legged land species posed lying down, flat and on a slope, keeps its
    /// legs folded above the ground rather than through it.
    /// </summary>
    public class LyingTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>
        /// Target: card A-92, no leg segment more than 1 cm into the ground at any lie weight from 0.3 to 1, at four
        /// headings, on the flattest spot near a meadow start and on one sloping about 1 in 3.
        /// </summary>
        [UnityTest]
        public IEnumerator Lying_LegsStayAboveGround_FlatAndSlope()
        {
            yield return TestSceneSetup.Place("meadow");
            var failures = "";
            foreach (var slope in new[] { 0f, 0.33f })
            {
                var spot = Game.Hook<float[]>("SlopeSpot", slope, 60f);
                var lines = Game.Hook<string>("LieClearance", spot[0], spot[1]);
                LogWatch.Measure($"lying: slope {spot[2]:F2} at ({spot[0]:F0}, {spot[1]:F0}): " + lines.Replace('\n', ';'));
                Assert.IsNotEmpty(lines, "no lying species measured");
                foreach (var line in lines.Split('\n'))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 2 && float.Parse(parts[1], CultureInfo.InvariantCulture) < -0.01f)
                        failures += $"{parts[0]} {parts[1]} m (slope {spot[2]:F2}); ";
                }
            }
            Assert.IsEmpty(failures, "legs into the ground while lying");
        }

        /// <summary>
        /// Target: card A-92, no crawling with dragged legs. The player as a horse, lying down, then pushing the stick
        /// gently (0.3): it gets up where it lay (under 5 cm while still more than a quarter down), then walks off.
        /// </summary>
        [UnityTest]
        public IEnumerator Lying_PlayerStandsBeforeMoving()
        {
            yield return TestSceneSetup.Place("meadow");
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Horse");
            yield return TestSceneSetup.Seconds(2f);
            Assert.IsTrue(Game.Hook<bool>("RestPlayer", 2), "no interaction input system");
            yield return TestSceneSetup.Seconds(3f);
            var pose = Game.Hook<float[]>("AvatarPose");
            var lay = pose.Length > 0 ? pose[0] : 0f;
            var from = pose.Length > 0 ? new Vector2(pose[2], pose[3]) : Vector2.zero;
            var way = Game.Hook<float[]>("FlatWay", 10f);
            Game.Hook<object>("HoldMove", way[0] * 0.3f, way[1] * 0.3f, true);
            var dragged = 0f;
            var stoodAfter = -1f;
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 5f)
            {
                yield return null;
                pose = Game.Hook<float[]>("AvatarPose");
                if (pose.Length == 0)
                    break;
                var moved = Vector2.Distance(from, new Vector2(pose[2], pose[3]));
                if (Mathf.Max(pose[0], pose[1]) > 0.25f)
                    dragged = Mathf.Max(dragged, moved);
                else if (stoodAfter < 0f)
                    stoodAfter = Time.realtimeSinceStartup - start;
            }
            Game.Hook<object>("HoldMove", 0f, 0f, false);
            var walked = pose.Length > 0 ? Vector2.Distance(from, new Vector2(pose[2], pose[3])) : 0f;
            Game.Hook<string>("SetBody", was);
            yield return TestSceneSetup.Seconds(1f);
            Game.Hook<object>("PutBodyPref", pref);
            LogWatch.Measure($"lying player: lay {lay:F2}, moved {dragged:F3} m while down, stood after {stoodAfter:F2} s, walked {walked:F2} m in 5 s");
            Assert.Greater(lay, 0.9f, "the horse body never lay down");
            Assert.Less(dragged, 0.05f, "the body moved off while still down");
            Assert.GreaterOrEqual(stoodAfter, 0f, "it never got up");
            Assert.Greater(walked, 1f, "it never walked off once up");
        }

        /// <summary>
        /// Card A-92 shots: the player as a horse lying, side and front, on the flattest spot near a meadow start and on
        /// a 1-in-3 slope; then four stills 0.2 s apart from the side as a gentle push gets it up.
        /// </summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(600000)]
        public IEnumerator Lying_Shots()
        {
            yield return TestSceneSetup.Place("meadow");
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Horse");
            yield return TestSceneSetup.Seconds(2f);
            var home = Game.Hook<Vector3>("PlayerPosition");
            foreach (var slope in new[] { 0f, 0.33f })
            {
                var spot = Game.Hook<float[]>("SlopeSpot", slope, 60f);
                var at = Game.Hook<Vector3>("PlayerPosition");
                Game.Hook<bool>("MovePlayer", spot[0] - at.x, spot[1] - at.z);
                yield return TestSceneSetup.Seconds(1.5f);
                Game.Hook<bool>("RestPlayer", 2);
                yield return TestSceneSetup.Seconds(3f);
                var pose = Game.Hook<float[]>("AvatarPose");
                var body = new Vector3(pose[2], pose[6], pose[3]);
                var ahead = new Vector3(Mathf.Sin(pose[5]), 0f, Mathf.Cos(pose[5]));
                var right = new Vector3(ahead.z, 0f, -ahead.x);
                var name = slope > 0f ? "slope" : "flat";
                Cam(body + right * 3.2f + Vector3.up * 0.7f, body + Vector3.up * 0.35f);
                yield return Shots.Settled($"a92-lying-{name}-side", 1280, 720);
                Cam(body + ahead * 3.2f + Vector3.up * 0.7f, body + Vector3.up * 0.35f);
                yield return Shots.Settled($"a92-lying-{name}-front", 1280, 720);
                if (slope == 0f)
                {
                    Cam(body + right * 3.2f + Vector3.up * 0.9f, body + Vector3.up * 0.6f);
                    yield return TestSceneSetup.Seconds(1f);
                    Game.Hook<object>("HoldMove", ahead.x * 0.05f, ahead.z * 0.05f, true);
                    for (int k = 0; k < 4; k++)
                    {
                        yield return TestSceneSetup.Seconds(0.2f);
                        yield return new WaitForEndOfFrame();
                        Shots.Take($"a92-standing-{k}", 1280, 720);
                    }
                    Game.Hook<object>("HoldMove", 0f, 0f, false);
                    yield return TestSceneSetup.Seconds(1f);
                }
                Game.Hook<bool>("RestPlayer", 0);
                yield return TestSceneSetup.Seconds(2f);
            }
            Game.RunOptions("-shotcam off");
            var now = Game.Hook<Vector3>("PlayerPosition");
            Game.Hook<bool>("MovePlayer", home.x - now.x, home.z - now.z);
            Game.Hook<string>("SetBody", was);
            yield return TestSceneSetup.Seconds(1f);
            Game.Hook<object>("PutBodyPref", pref);
        }

        static void Cam(Vector3 from, Vector3 at)
        {
            // (On a slope the uphill side can put the lens in the ground: keep it at least 0.6 m above the ground under it.)
            var ground = Game.Hook<float[]>("FloraHeights", from.x, from.z, 0.01f, 1f);
            if (ground.Length > 0)
                from.y = Mathf.Max(from.y, ground[0] + 0.6f);
            Game.RunOptions(string.Format(CultureInfo.InvariantCulture, "-shotcam {0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2}", from.x, from.y, from.z, at.x, at.y, at.z));
        }

        /// <summary>
        /// Target: card A-92, no animal crawls off still lying. For 30 s round a meadow start, no four-legged creature
        /// more than 0.3 down (lying or sitting) moves faster than 0.15 m/s.
        /// </summary>
        [UnityTest]
        public IEnumerator Lying_AnimalsGetUpBeforeMoving()
        {
            yield return TestSceneSetup.Place("meadow");
            Game.Hook<float[]>("LyingDrag");
            var samples = 0f;
            var fastest = 0f;
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 30f)
            {
                yield return TestSceneSetup.Seconds(0.25f);
                var d = Game.Hook<float[]>("LyingDrag");
                samples += d[0];
                fastest = Mathf.Max(fastest, d[1]);
            }
            LogWatch.Measure($"lying animals: {samples:F0} samples of animals down, fastest {fastest:F3} m/s");
            if (samples == 0f)
                Assert.Inconclusive("no animal lay or sat down near the start");
            Assert.Less(fastest, 0.15f, "an animal moved off while still down");
        }
    }
}
