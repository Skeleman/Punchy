using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Play-mode checks on the v3 island and what lives on it (W-97). All share one loaded game scene
    /// (TestSceneSetup.Load), so the whole suite pays the world build once. Each target names where it came from.
    /// </summary>
    public class WorldTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>The harness itself: the world came up with the start flags.</summary>
        [UnityTest, Order(0)]
        public IEnumerator Harness_WorldUp()
        {
            Assert.IsTrue(Game.Hook<bool>("WorldReady"));
            StringAssert.Contains("v3", LogWatch.Last("[island]") ?? "no [island] line");
            yield return null;
        }

        /// <summary>Target: REPORTS-world W-98 row 3 (spring pools at river heads): 107 landmarks, probe hash 14D92F64 (was 108 / 4D2643D7).</summary>
        [UnityTest, Order(1)]
        public IEnumerator Landmarks_V3()
        {
            var hash = Game.Hook<string>("LandmarkHash");
            LogWatch.Measure($"landmarks {hash}; game line: {LogWatch.Last("[island]")}");
            Assert.AreEqual("107 14D92F64", hash);
            yield return null;
        }

        /// <summary>
        /// Target: REPORTS-animal A-68 (115 species): fingerprint 2E01A16B on animal-behaviours with main f403b9c merged in (the
        /// bat folk look on top of main's species data; before the merge the branch read C1D6A1EA, and main 69ca891 reads
        /// 286E0A52). The fingerprint is of the species data the fauna
        /// build bakes, so it reads the same wherever the player stands (the standard start spot here).
        /// </summary>
        [UnityTest, Order(2)]
        public IEnumerator Fauna_Fingerprint()
        {
            var line = LogWatch.Last("fingerprint") ?? "no fingerprint line";
            LogWatch.Measure("fauna " + line);
            StringAssert.Contains("fingerprint 2E01A16B", line);
            yield return null;
        }

        /// <summary>
        /// No max zigzag angle was ever set (no constant, issue or report names one): measured on main 69ca891
        /// (max 119.5, p99 103.4, wide rivers the same) and given about 10 degrees of headroom. Wide rivers are smoothed (W-37), so they get the tighter limit.
        /// </summary>
        [UnityTest, Order(3)]
        public IEnumerator Rivers_BendUnderMaxZigzag()
        {
            var all = Game.Hook<float[]>("RiverTurns", 0f);
            var wide = Game.Hook<float[]>("RiverTurns", 12f);
            LogWatch.Measure($"river turns: all {all[0]:F0} cells, max {all[1]:F1}, p99 {all[2]:F1}; wide (12 m+) {wide[0]:F0} cells, max {wide[1]:F1}, p99 {wide[2]:F1}");
            Assert.Greater(all[0], 100f, "too few river cells to judge");
            Assert.Less(all[1], MaxTurnAll, "a river cell turns sharper than the zigzag limit");
            Assert.Less(wide[1], MaxTurnWide, "a wide river turns sharper than its zigzag limit");
            yield return null;
        }

        const float MaxTurnAll = 130f, MaxTurnWide = 130f;

        /// <summary>
        /// Butterflies round the player in a meadow at noon, clear sky. No range was ever set: CritterSystem clips at 8
        /// per 100 m² (MaxDensity); the band is today's value (0.30 on main
        /// 69ca891) with a tolerance.
        /// </summary>
        [UnityTest, Order(4)]
        public IEnumerator Butterflies_MeadowDensity()
        {
            yield return TestSceneSetup.Place("meadow");
            yield return TestSceneSetup.TimeOfDay(12f);
            yield return TestSceneSetup.Weather(false);
            yield return TestSceneSetup.Seconds(4f);
            var d = Game.Hook<Vector3>("CritterDensity", 60f);
            LogWatch.Measure($"meadow critters per 100 m2: fireflies {d.x:F2}, butterflies {d.y:F2} over {d.z:F0} tiles at {Game.Hook<Vector3>("PlayerPosition")}");
            Assert.Greater(d.z, 0f, "no critter tiles built round the player");
            Assert.That(d.y, Is.InRange(ButterfliesLow, ButterfliesHigh));
        }

        const float ButterfliesLow = 0.1f, ButterfliesHigh = 1.5f;

        /// <summary>
        /// Creatures round the player in a human village at noon. No range was ever set; the band is today's count (4
        /// within 50 m on main 69ca891) with a tolerance. Also checks the village's butterflies stay under the meadow ceiling (CritterSystem: villages 0.6x).
        /// </summary>
        [UnityTest, Order(5)]
        public IEnumerator Village_CreatureDensity()
        {
            yield return TestSceneSetup.Place("HumanVillage", 12f);
            var near = Game.Hook<int[]>("CreaturesNear", 50f);
            var d = Game.Hook<Vector3>("CritterDensity", 60f);
            LogWatch.Measure($"village within 50 m: {near[0]} creatures, {near[1]} folk; butterflies {d.y:F2} per 100 m2 at {Game.Hook<Vector3>("PlayerPosition")}");
            Assert.That(near[0], Is.InRange(VillageLow, VillageHigh));
            Assert.That(d.y, Is.LessThanOrEqualTo(8f), "village butterflies over the CritterSystem ceiling");
        }

        const int VillageLow = 1, VillageHigh = 20;

        /// <summary>TestSceneSetup's spawn and weather, and a checked shot of the result (the shot helper's own test).</summary>
        [UnityTest, Order(6)]
        public IEnumerator Setup_SpawnRainAndShot()
        {
            var at = Game.Hook<Vector3>("PlayerPosition");
            yield return TestSceneSetup.Spawn("Deer", at.x + 2f, at.z);
            yield return TestSceneSetup.Weather(true);
            yield return TestSceneSetup.Seconds(2f);
            Shots.Take("test-rain-deer");
            yield return TestSceneSetup.Weather(false);
        }
    }
}
