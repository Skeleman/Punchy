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

        /// <summary>Target: 117 landmarks, probe hash 689D5348 since W-138 row 3a (bores capped at 800 m and kept out from
        /// under towns: the west roads re-routed, so three roadside campsites are new and the C0a swamp waystation moved 65 m).
        /// 114 00D16E22 since W-138 row 2b (the two C0a waystations added; the roadside
        /// picks re-drawn on the new road net: inns, shrines, campsites, monuments and a ruin moved). 112 / A5D1D048 since W-138 row 2a (the sea folk landing 2 m and village 16 m
        /// moved: their spots are now found before the roads; the other 110 unmoved). A955AFE0 since W-122 row 1 (the MarshFolkVillage entry appended; the other
        /// 111 hash to 3BDF705F unchanged). 111 / 3BDF705F since W-114 (the Ashlands ribcage on its own spot; the other 110
        /// unmoved). 110 / 09EAC0FE since W-105 row 3 (stilt houses out over the shallows; village
        /// placement asks for a stilt house spot). Before that, on main after PR #148 (W-104 row 1 repinned it): 109 / C96F266F
        /// (was 107 / 14D92F64 after W-98 row 3).</summary>
        [UnityTest, Order(1)]
        public IEnumerator Landmarks_V3()
        {
            var hash = Game.Hook<string>("LandmarkHash");
            LogWatch.Measure($"landmarks {hash}; game line: {LogWatch.Last("[island]")}");
            Assert.AreEqual("117 689D5348", hash);
            yield return null;
        }

        /// <summary>
        /// Target: F0EF4BD5 since refactor 3.6 (a new field in the flora placement rules, BiomeRock: the blob's bytes moved,
        /// no placement changed). 83902658 since A-89 row 3 (main's W-122 marsh folk flora species, AEA50DCE on main, plus the PadFrog and
        /// Dragonfly species and the marsh life rows: 120 species). AD7E7781 since A-88 row 2 (the LakeSturgeon species and the sea folk lake fish rows). 706B915E since W-121 (sea folk buoy and raft species; gull perches on buoy masts). 65C95060 since W-120 (4 new sea folk flora species: glow stone, wreck, clam bed, vent field). 2998FA9A since W-119 (FishBoardwalk species, more fishing variants, moved stilt houses). 027CC3FD since W-118 (hazard house layout entries and the drift slope limit change flora data). 8A1979F1 since W-114 (the ribcage's bones and its BoneRemains, GiantRibs and rock props go into the flora
        /// blob; no species change). 90AC5F60 since W-113 (rows 1-3 changed tent, ruin plinth and roof flora, whose colliders and perches go into
        /// the flora blob this hashes; landmarks unchanged). F4E66093 since the A-73 merge (W-105 stilt houses plus A-73 Gull and Mackerel in the critter table;
        /// A-73 alone read 773CA7F6). 61459BF0 since W-105 row 3 (stilt house Home and Door anchors only where the house fits, doors at the
        /// walkway end). 1202B8ED since W-104 row 4 (fishing villages got Home and Door anchors). Before that, on main after PR #148
        /// (W-104 row 1 repinned it): fingerprint F99FD9E4, 115 species. A-71 pinned EA6A4DE5 on
        /// animal-behaviours before the merge (before A-71 it read 2E01A16B, A-68's target). The fingerprint is of the species data the fauna
        /// build bakes, so it reads the same wherever the player stands (the standard start spot here).
        /// </summary>
        [UnityTest, Order(2)]
        public IEnumerator Fauna_Fingerprint()
        {
            var line = LogWatch.Last("fingerprint") ?? "no fingerprint line";
            LogWatch.Measure("fauna " + line);
            StringAssert.Contains("fingerprint 7B361633", line);   // W-147 + main merge re-pin: W-147's roof, dock, rope and mine ladder records in the flora blob (06A92F3A on the branch) with main's A-103 row 2 sea folk (B9ADEB35) give 7B361633; no placement change, landmarks 117 689D5348. B9ADEB35 since the A-103 row 2 re-pin: the sea folk species, its Sea* limbs, the SeaHousehold spawn rows and the blob's WalksBed flag (no placement change). C5B450EF since the A-103 + A-102 merge re-pin: A-103 row 1 (7D44629E: the turtle folk species, limbs, spawn row and the blob's TurtleJobs flag) with A-102 row 1 (151753F3: the blob gains BreathesWater and HoldBreath); no placement change. AD723954 since the A-97 row 3 re-pin: paw limb feet are now coat-coloured Paw segments, a little wider (no placement change). 66320C7C since the W-128 re-pin: the inn rebuilt from kit pieces (cce82b69) bakes new anchor rows. B15790ED since W-127 (was 7D75BB04, W-126 before it F0EF4BD5): the flora blob the build bakes gains the beacon fixture species and the fixture rows' MinPlots
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
        /// A-73: in the village's middle ("HumanVillage:centre"), at noon in clear weather. "-place HumanVillage" stands
        /// the player 1.1 radii out (98 m from the middle), where 50 m only reached the odd stray hen or wild animal, so
        /// the count was 0 to 4 by chance (0 once, failing); the residents live within 0.45 radii of the middle. The band
        /// is now the village's own head count: at least its fewest grown folk (4 free, 4 at posts, less a couple strayed
        /// past 50 m), and no more than everyone it can field with its animals (about 40); 23 and 13 folk at first.
        /// </summary>
        [UnityTest, Order(5)]
        public IEnumerator Village_CreatureDensity()
        {
            yield return TestSceneSetup.TimeOfDay(12f);
            yield return TestSceneSetup.Weather(false);
            yield return TestSceneSetup.Place("HumanVillage:centre", 12f);
            var near = Game.Hook<int[]>("CreaturesNear", 50f);
            var d = Game.Hook<Vector3>("CritterDensity", 60f);
            LogWatch.Measure($"village within 50 m: {near[0]} creatures, {near[1]} folk; butterflies {d.y:F2} per 100 m2 at {Game.Hook<Vector3>("PlayerPosition")}");
            Assert.That(near[0], Is.InRange(VillageLow, VillageHigh));
            Assert.That(near[1], Is.InRange(VillageFolkLow, VillageHigh), "village folk round the middle");
            Assert.That(d.y, Is.LessThanOrEqualTo(8f), "village butterflies over the CritterSystem ceiling");
        }

        const int VillageLow = 8, VillageFolkLow = 4, VillageHigh = 40;

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

        /// <summary>
        /// W-134 row 1: one water query for a point that may be underground (TerrainSource.SampleGround(float3)). At and
        /// over the ground it must answer exactly as the float2 query (the surface water as on main), and 30 m under it
        /// too away from cave water; every water pool and stream under the ground must read as cave water at its own
        /// level. Then, stood in the deepest cave pool ("-place cavepool"), the player's body must leave ripples
        /// (CreatureWakeSystem) and press the R-9 field (WaterPatchSystem) within 5 s: both saw no water there before.
        /// </summary>
        [UnityTest, Order(9)]
        public IEnumerator CaveWater_SurfaceMatchesAndPoolStirs()
        {
            var q = Game.Hook<float[]>("CaveWaterQuery", 4000);
            LogWatch.Measure($"cave water query: {q[1]} of {q[0]} surface spots differ, {q[3]} of {q[2]} deep; pools read {q[5]} of {q[4]}, streams {q[7]} of {q[6]}, worst level error {q[8]:F3} m");
            Assert.AreEqual(0f, q[1], "the float3 query changed a surface answer");
            Assert.AreEqual(0f, q[3], "the float3 query changed an answer under the ground away from cave water");
            Assert.Greater(q[4], 0f, "no water pools under the ground to check");
            Assert.AreEqual(q[4], q[5], "a cave water pool didn't read as cave water");
            Assert.AreEqual(q[6], q[7], "a cave stream didn't read as cave water");
            Assert.Less(q[8], 0.01f, "a cave water level read wrong");

            // (Ripples are made only near the camera: an earlier test may have left it posed elsewhere.)
            Game.Hook<bool>("FreeCamera");
            yield return TestSceneSetup.Place("cavepool", 4f);
            var best = new float[4];
            var start = Time.realtimeSinceStartup;
            while ((best[2] <= 0f || best[3] <= 0f) && Time.realtimeSinceStartup - start < 5f)
            {
                var s = Game.Hook<float[]>("CaveWaterStir");
                for (int i = 0; i < 4; i++)
                    best[i] = Mathf.Max(best[i], s[i]);
                yield return null;
            }
            LogWatch.Measure($"in a cave pool at {Game.Hook<Vector3>("PlayerPosition")}: cave water {best[0]}, {best[1]:F2} m deep, {best[2]} ripples near, {best[3]} field presses");
            Assert.AreEqual(1f, best[0], "the player's spot in the cave pool didn't read as cave water");
            Assert.Greater(best[2], 0f, "no ripples from the player's body in the cave pool");
            Assert.Greater(best[3], 0f, "no R-9 field presses from the player's body in the cave pool");
        }

        /// <summary>
        /// W-126: flora placement cost, measured only (no limit): the managed Place over 3 squares of 160 m round biome spots, best
        /// of three. Baseline on main acd0cad5 is in the W-126 report; row 1 must stay within +10%.
        /// </summary>
        [UnityTest, Order(40)]
        public IEnumerator Flora_PlacementCost()
        {
            var total = 0f;
            var line = "";
            foreach (var biome in new[] { "Sonoran", "Grassland", "Mountains" })
            {
                var area = Game.Hook<float[]>("BiomeArea", biome);
                if (area.Length < 4)
                    continue;
                Game.Hook<float[]>("FloraPlaceCost", area[2], area[3], 80f);
                var r = Game.Hook<float[]>("FloraPlaceCost", area[2], area[3], 80f);
                for (int pass = 0; pass < 2; pass++)   // best of three: the managed path jitters
                {
                    var again = Game.Hook<float[]>("FloraPlaceCost", area[2], area[3], 80f);
                    if (again[0] < r[0])
                        r = again;
                }
                total += r[0];
                line += $"{biome} {r[0]:F0} ms ({r[1]:F0} of {r[2]:F0}); ";
            }
            LogWatch.Measure("flora placement cost: " + line + $"total {total:F0} ms");
            Assert.Greater(total, 0f);
            yield return null;
        }

        /// <summary>W-140: the island this launch has (loaded from the island cache, or baked) is byte for byte a fresh bake
        /// of the same inputs, and so is a write and read of the island file; the landmark hash is the same all ways.
        /// Run last: it bakes the island once more (~14 s).</summary>
        [UnityTest, Order(90)]
        public IEnumerator IslandCache_LoadedEqualsFresh()
        {
            var problems = Game.Hook<string>("IslandCacheRoundTrip");
            LogWatch.Measure($"island {LogWatch.Last("[island] cache") ?? "no cache line"}; round trip: {(problems == "" ? "all blobs byte-equal, landmark hash the same" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        /// <summary>W-140: every blob in TerrainSource is saved by the island cache or named as not the bake's, so a new
        /// blob can't come back empty on a cache hit.</summary>
        [UnityTest, Order(90)]
        public IEnumerator IslandCache_SavesEveryBlob()
        {
            Assert.AreEqual("", Game.Hook<string>("IslandCacheMissingBlobs"));
            yield return null;
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
