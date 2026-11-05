using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// The follow hook for lead quests (A-71 row 6, P-17): a dog started following the player keeps up over a 30 m walk
    /// and is released again by Stop.
    /// </summary>
    public class FollowTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>
        /// Target: card A-71, the follower within 4 m of the player once they stop after a 30 m walk; following until
        /// stopped, and not after. A dog spawned within 10 m of the player in a meadow and started following them; the player walks 30 m at 3 m/s.
        /// </summary>
        [UnityTest]
        public IEnumerator Follow_ArrivesWithinFourMetres_StopReleases()
        {
            yield return TestSceneSetup.Place("meadow");
            var at = Game.Hook<Vector3>("PlayerPosition");
            yield return TestSceneSetup.Spawn("Dog", at.x, at.z, 1, 10f);
            yield return TestSceneSetup.Seconds(2f);
            var start = Game.Hook<float>("FollowStart", "Dog", 15f);
            Debug.Log($"follow: started on a dog {start:F1} m off");
            Assert.GreaterOrEqual(start, 0f, "no dog near the player to follow");
            yield return TestSceneSetup.Seconds(1f);
            // 30 m at 3 m/s, in 0.3 m steps, the flattest way there is (up a steep slope any animal slows to a crawl).
            var way = Game.Hook<float[]>("FlatWay", 30f);
            for (int step = 0; step < 100; step++)
            {
                Game.Hook<bool>("MovePlayer", way[0] * 0.3f, way[1] * 0.3f);
                yield return TestSceneSetup.Seconds(0.1f);
            }
            var walked = Game.Hook<float[]>("FollowStatus");
            for (int second = 0; second < 6; second++)
            {
                yield return TestSceneSetup.Seconds(1f);
                var now = Game.Hook<float[]>("FollowStatus");
                Debug.Log($"follow: {second + 1} s after the walk, {now[1]:F1} m off, speed {now[3]:F1}, following {now[0] > 0f}");
            }
            var arrived = Game.Hook<float[]>("FollowStatus");
            Game.Hook<bool>("FollowStop");
            yield return TestSceneSetup.Seconds(1f);
            var stopped = Game.Hook<float[]>("FollowStatus");
            LogWatch.Measure($"follow: dog {start:F1} m off at the start, {walked[1]:F1} m behind as the walk ended, {arrived[1]:F1} m after 6 s; " +
                             $"following {arrived[0] > 0f}, after Stop {stopped[0] > 0f} (still walking with it {stopped[2] > 0f})");
            Assert.Greater(arrived[0], 0f, "it gave up following during the walk");
            Assert.LessOrEqual(arrived[1], 4f, "it didn't come within 4 m of the player");
            Assert.AreEqual(0f, stopped[0], "still following after Stop");
            Assert.AreEqual(0f, stopped[2], "still in its follow stage after Stop");
        }
    }
}
