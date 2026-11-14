using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// One population (A-110 row 2): Whereabouts gives every town household member one day, the same on every
    /// machine. Numbers only: nobody jumps, a plan built again from the records gives the same day, and the far
    /// figures are exactly the members Whereabouts puts outdoors.
    /// </summary>
    public class WhereaboutsTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>Waits for every town's plan (they are built one at a time once the world is up).</summary>
        static IEnumerator Plans()
        {
            for (int i = 0; i < 240; i++)
            {
                var towns = Game.Hook<float[]>("TownPlans");
                if (towns.Length > 1 && towns[0] > 0f)
                    yield break;
                yield return TestSceneSetup.Seconds(0.5f);
            }
            Assert.Fail("the town plans were not all built in two minutes");
        }

        /// <summary>
        /// Target: 0 breaks, 0 overruns. Over a day and into the next, in the three largest towns, no member moves further in a
        /// step than its walking speed allows: nobody teleports, at the turn of the day either.
        /// </summary>
        [UnityTest]
        public IEnumerator Day_NobodyJumps()
        {
            yield return Plans();
            var towns = Game.Hook<float[]>("TownPlans");
            Assert.GreaterOrEqual(towns.Length - 1, 3, "fewer than three towns have a plan");
            var day = (int)Game.Hook<float[]>("TownClock")[0];
            for (int i = 1; i <= 3; i++)
            {
                var d = Game.Hook<float[]>("TownDay", (int)towns[i], day);
                LogWatch.Measure($"whereabouts: town {towns[i]:F0} day {day}: {d[0]:F0} members, {d[1]:F0} breaks (worst {d[2]:F2}x), " +
                                 $"peak {d[3]:F0} outdoors at {d[4]:F2} h, {d[5]:F0} overruns, digest {(uint)d[7]:X4}{(uint)d[6]:X4}");
                Assert.Greater(d[0], 0f, "a town with a plan has no members");
                Assert.AreEqual(0f, d[1], $"town {towns[i]:F0}: a member moved {d[2]:F2} times further than its speed allows");
                Assert.Greater(d[3], 0f, $"town {towns[i]:F0}: nobody is ever outdoors");
                Assert.AreEqual(0f, d[5], $"town {towns[i]:F0}: a member's day ran over (too many stops, or not home by the turn of the day)");
                yield return null;
            }
        }

        /// <summary>
        /// Target: equal. A plan built afresh from the records (as a joining machine builds it) gives the same day,
        /// place for place, as the one this machine has been using: in the three largest towns, today and on a fixed day.
        /// </summary>
        [UnityTest]
        public IEnumerator Rebuilt_SameDay()
        {
            yield return Plans();
            var towns = Game.Hook<float[]>("TownPlans");
            var day = (int)Game.Hook<float[]>("TownClock")[0];
            for (int i = 1; i <= 3 && i < towns.Length; i++)
                foreach (var which in new[] { day, 7 })
                {
                    var d = Game.Hook<float[]>("TownRebuilt", (int)towns[i], which);
                    Assert.AreEqual(6, d.Length, $"town {towns[i]:F0} could not be built again");
                    LogWatch.Measure($"whereabouts: town {towns[i]:F0} day {which}: digest {(uint)d[1]:X4}{(uint)d[0]:X4}, built again {(uint)d[3]:X4}{(uint)d[2]:X4}");
                    Assert.AreEqual(d[4], d[5], "the plan built again has a different number of members");
                    Assert.AreEqual(d[0], d[2], "the plan built again gives a different day");
                    Assert.AreEqual(d[1], d[3], "the plan built again gives a different day");
                    yield return null;
                }
        }

        /// <summary>
        /// Target: equal. In the morning, at noon and at night, from 400 m off the human village, the far figures
        /// placed are exactly the members Whereabouts counts outdoors at that moment.
        /// </summary>
        [UnityTest]
        public IEnumerator FarCount_EqualsOutdoors()
        {
            yield return TestSceneSetup.Place("HumanVillage", 6f);
            yield return Plans();
            var at = Game.Hook<Vector3>("PlayerPosition");
            var towns = Game.Hook<float[]>("TownPlans");
            var town = -1;
            var centre = new float[0];
            for (int i = 1; i < towns.Length && town < 0; i++)
            {
                centre = Game.Hook<float[]>("TownCentre", (int)towns[i]);
                if (Mathf.Abs(centre[0] - at.x) < centre[2] + 40f && Mathf.Abs(centre[1] - at.z) < centre[2] + 40f)
                    town = (int)towns[i];
            }
            Assert.GreaterOrEqual(town, 0, "the human village has no plan");
            yield return TestSceneSetup.GoTo(centre[0] - centre[2] - 400f, centre[1], 6f);
            foreach (var hour in new[] { 8f, 12f, 22f })
            {
                yield return TestSceneSetup.TimeOfDay(hour);
                var n = new float[0];
                for (int i = 0; i < 60; i++)
                {
                    yield return TestSceneSetup.Seconds(0.5f);
                    n = Game.Hook<float[]>("TownFarCount", town);
                    if (n.Length > 0 && n[3] == 0f && n[4] <= 1f && Mathf.Abs(n[7] - hour) < 1f)
                        break;
                }
                Assert.AreEqual(8, n.Length, "the town's far figures were never drawn");
                LogWatch.Measure($"whereabouts: town {town} at {n[7]:F2} h: {n[0]:F0} far figures placed, {n[1]:F0} outdoors, {n[5]:F0} shown, {n[2]:F0} dead, {n[3]:F0} unbuilt");
                Assert.AreEqual(0f, n[3], "some members' days were still unbuilt after 30 s");
                Assert.AreEqual(n[1], n[0], $"at {hour} h the far figures are not the members outdoors");
                if (hour < 20f)
                    Assert.Greater(n[0], 0f, $"at {hour} h nobody is outdoors");
            }
        }
    }
}
