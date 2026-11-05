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
        /// Target: REPORTS-animal A-71 (115 species): fingerprint EA6A4DE5 on animal-behaviours with main 6d89525 merged in (the
        /// weather comfort profiles in the species data; before A-71 it read 2E01A16B, A-68's target). The fingerprint is of the species data the fauna
        /// build bakes, so it reads the same wherever the player stands (the standard start spot here).
        /// </summary>
        [UnityTest, Order(2)]
        public IEnumerator Fauna_Fingerprint()
        {
            var line = LogWatch.Last("fingerprint") ?? "no fingerprint line";
            LogWatch.Measure("fauna " + line);
            StringAssert.Contains("fingerprint EA6A4DE5", line);
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

        /// <summary>
        /// A-69 (Zach: control lost flying fast up out of a cave as a crow on Ultra, while land streams in): the player
        /// as a crow flies cave 0 to its chamber and out, then climbs 30 m away over new ground with a 700 ms stall every
        /// 45 frames (a chunk load's hitch). The climb must stay on the wing and reach its point. Last, as it
        /// moves the player and changes the body (put back after, with the saved pick).
        /// </summary>
        [UnityTest, Order(7)]
        public IEnumerator Player_CrowFliesOutOfCaveThroughHitches()
        {
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Crow");
            var start = Time.realtimeSinceStartup;
            while (!Game.Hook<bool>("BodyFlies") && Time.realtimeSinceStartup - start < 15f)
                yield return null;
            var flies = Game.Hook<bool>("BodyFlies");
            var before = LogWatch.Last("flycave DONE");
            if (flies)
            {
                Game.RunOptions("-flyhitch 700 -flycave 1");
                start = Time.realtimeSinceStartup;
                while (LogWatch.Last("flycave DONE") == before && Time.realtimeSinceStartup - start < 240f)
                    yield return null;
            }
            var done = LogWatch.Last("flycave DONE");
            var climb = LogWatch.Last("flycave climb") ?? "no climb line";
            Game.RunOptions("-flyhitch 0");
            Game.Hook<string>("SetBody", was);
            yield return TestSceneSetup.Seconds(1f);
            Game.Hook<object>("PutBodyPref", pref);
            LogWatch.Measure($"crow cave flight: {done}; {climb}");
            Assert.IsTrue(flies, "the crow body never flew");
            Assert.AreNotEqual(before, done, "the flight never finished");
            // (The cave legs are measured, not asserted: at a crow's 14 m/s it can overshoot the mouth and land on the
            // hill above it now and then; the climb out through the hitches is what must hold.)
            StringAssert.Contains(" 0 off the wing", climb);
            StringAssert.Contains("reached", climb);
        }

        /// <summary>
        /// #137 (W-100 row 5): "-place prop:FishWharf" did nothing once the flora lookup was built on workers (#102);
        /// the request now waits for it. Must log the wharves it found, frame the nearest and put the player beside it
        /// (2 on v3, the nearest at (-4056.9, 0.5, 904.2), measured from 0,0 so it doesn't hang on where earlier tests left the player).
        /// </summary>
        [UnityTest, Order(8)]
        public IEnumerator Place_PropFindsAndFramesWharf()
        {
            var before = LogWatch.Last("FishWharf found in all");
            Game.RunOptions("-place prop:FishWharf@0,0");
            var start = Time.realtimeSinceStartup;
            while ((Game.Hook<bool>("PropPlacePending") || LogWatch.Last("FishWharf found in all") == before) && Time.realtimeSinceStartup - start < 30f)
                yield return null;
            var found = LogWatch.Last("FishWharf found in all") ?? "no found line";
            var first = LogWatch.Last("FishWharf #1 at") ?? "no #1 line";
            var framing = LogWatch.Last("framing FishWharf") ?? "no framing line";
            var at = Game.Hook<Vector3>("PlayerPosition");
            LogWatch.Measure($"place prop: {found}; {first}; player at {at}");
            Assert.AreNotEqual(before, LogWatch.Last("FishWharf found in all"), "-place prop: logged nothing (#137)");
            StringAssert.Contains("framing FishWharf #1", framing);
            var wharf = ParseAt(first);
            Assert.Less(Vector2.Distance(new Vector2(at.x, at.z), new Vector2(wharf.x, wharf.z)), 30f, "the player wasn't put beside the wharf");
            yield return TestSceneSetup.Seconds(1f);
        }

        /// <summary>The "(x, y, z)" after " at " in a DebugControls line.</summary>
        static Vector3 ParseAt(string line)
        {
            var open = line.IndexOf(" at (");
            if (open < 0)
                return new Vector3(float.NaN, 0f, float.NaN);
            var close = line.IndexOf(')', open);
            var parts = line.Substring(open + 5, close - open - 5).Split(',');
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(parts[0], c), float.Parse(parts[1], c), float.Parse(parts[2], c));
        }
    }
}
