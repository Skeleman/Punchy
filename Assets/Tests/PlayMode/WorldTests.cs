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

        /// <summary>Target: 120 landmarks, probe hash B32C5306 since #269 (fall notch walls wander and lean). 5B47CDAE since W-182 row 4 (two CastleGateRuins on their own spots).
        /// 5A21F052 since W-182 row 3 (four CastleRuins on their own spots in Temperate,
        /// Grassland and Tundra; the other 114 unmoved). DE33C724 since the W-153 row 3 tail (fall lip shoulders: the swamp
        /// campsite near -1626,33 settles 0.2 m; the other 113 unmoved). 9113464F since W-153 rework row 3 (fall profiles and 138
        /// described falls re-shape river ground). 4DC69668 since rework row 2 (river carve from the nearest
        /// profile moved landmark spots by river ground). 9786243F since W-174 row 2 (one natural ground-edit profile: pass gullies, beds,
        /// pads and paddy rims re-shape the ground, so roadside picks re-draw: five campsites, a fairy ring, the small fungal grotto at
        /// (-1796,-201) and a grotto drop; a campsite, two burnt campfires, a cave network and a lone inn are new; the bear cave,
        /// a waterfall and the sea folk landing and village move). 117 / 547D4E79 since the W-172 lava half-row (VolcanoCone joins Ashlands' list and
        /// takes the Ashlands spot at (-891,-2021); one Ashlands BoneGraveyard at (-733,-2944) drops; the other 115 unmoved).
        /// 689D5348 since W-138 row 3a (bores capped at 800 m and kept out from
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
            Assert.AreEqual("120 B32C5306", hash);
            yield return null;
        }

        /// <summary>
        /// Target: 01766C0A since W-154 toxic pools (DeadReed and BleachedStick join the flora; landmarks unchanged). 156425C3 since W-173 kit inn and hall (11-19: the hall from kit parts and the inn through DrawKitHouse move flora boxes and perches; landmarks unchanged). 1E90DBCF since W-173 kit surfaces (crate battens and lid, counter top board: the flora blob changes; landmarks unchanged). A9684863 since W-173 tent row (tent guy ropes and stakes change the tent's flora blob; landmarks unchanged). 1F4C927C since W-173 row 1 (kit quoins out past the wall face, stone house from kit parts). 7ED8D1F1 since W-173 (hall roof boxes and kit chimney feet move flora colliders and perches; 1AC019EC before). F0EF4BD5 since refactor 3.6 (a new field in the flora placement rules, BiomeRock: the blob's bytes moved,
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
            StringAssert.Contains("fingerprint 01766C0A", line);
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
        /// W-153 row 1 (#301): where the island's water breaks physical rules, counted by WaterTruth over every river
        /// cell, every fall and a 4 m grid of the whole island. The baseline is the count of each kind when the probe
        /// was written; a kind may only fall. Lower its number when a fix lands, until every one is 0.
        /// </summary>
        [UnityTest, Order(3)]
        public IEnumerator Water_TruthCounts()
        {
            var counts = Game.Hook<int[]>("WaterTruthCounts");
            LogWatch.Measure($"water truth ({string.Join(", ", WaterTruthKinds)}): {string.Join(", ", counts)}");
            Assert.AreEqual(WaterTruthBaseline.Length, counts.Length, "the probe's kinds changed: update the baseline");
            var over = new System.Collections.Generic.List<string>();
            for (int k = 0; k < counts.Length; k++)
                if (counts[k] > WaterTruthBaseline[k])
                    over.Add($"{WaterTruthKinds[k]} {counts[k]} > {WaterTruthBaseline[k]}");
            Assert.IsEmpty(over, $"more water defects than the baseline: {string.Join(", ", over)}");
            yield return null;
        }

        /// <summary>#301: every river fall lands in water across its whole foot, a metre past each side of its sheet.</summary>
        [UnityTest, Order(3)]
        public IEnumerator Water_NoDryFallFeet()
        {
            var dry = Game.Hook<int>("DryFallFeet");
            LogWatch.Measure($"dry fall feet: {dry}");
            Assert.AreEqual(0, dry, "falls landing on ground instead of their plunge pool");
            yield return null;
        }

        /// <summary>Steep water and pits (steep water diving into the ground) round the three places that had any may only fall.</summary>
        [UnityTest, Order(3)]
        public IEnumerator Water_SheetSlopes()
        {
            int steep = 0, pits = 0;
            foreach (var spot in SheetSlopeSpots)
            {
                yield return TestSceneSetup.GoTo(spot.x, spot.y, 25f);
                var counts = Game.Hook<int[]>("WaterSheet", spot.x, spot.y, 1);
                steep += counts[8];
                pits += counts[9];
            }
            LogWatch.Measure($"water sheet slopes: steep {steep}, pits {pits}");
            Assert.LessOrEqual(steep, SheetSlopeBaseline.x, "more steep water than the baseline");
            Assert.LessOrEqual(pits, SheetSlopeBaseline.y, "more water pits than the baseline");
        }

        static readonly Vector2[] SheetSlopeSpots = { new(-2729, 546), new(-490, 546), new(-2018, -1070) };
        // Row 3 (cascade pools: water steeper than 0.2 m per metre steps down as registered falls, 140 -> 151): steep 118 -> 69, pits 22 -> 19.
        // Row 4 (closed bank pits fill flat instead of tucking): steep 69 -> 66, pits 19 -> 16.
        // Row 2 (riverless ponds of a cell or two get a bowl dug about a metre under their level): steep and pits stay 66 / 16.
        static readonly Vector2Int SheetSlopeBaseline = new(66, 16);

        static readonly string[] WaterTruthKinds =
        {
            "bend", "widthJump", "deadEnd", "source", "dryRun", "uphillGraph", "uphillSampled", "wall", "deep", "tilt", "step", "fallHead", "fallFoot"
        };
        // W-153 row 2 (still water last): dryRun 21 -> 10, uphillSampled 7 -> 0, wall 1987 -> 988, tilt 35 -> 31, step 1186 -> 959.
        // No stepped pool is left in a wall or a step: the 109 'pool' walls still counted are fall and landmark
        // head pools, which stand with ponds off (rows 3-6).
        // Main merge (W-174 rivers and cave mouths, M-07 painted edits, W-162): the river graph changed under the probe,
        // so the graph-only kinds moved (bend 80 -> 82, uphillGraph 31 -> 62) with deep 42 -> 43, step 959 -> 964 and
        // fallFoot 4 -> 5; dryRun 10 -> 9, wall 988 -> 972, tilt 31 -> 29, fallHead 47 -> 46. Re-based here.
        // uphillGraph 62 -> 10: 52 of the 62 were lake edges, where a lake cell's Bed holds the lake surface (flat water,
        // uphillSampled 0), so the probe no longer compares beds across a lake edge. The 10 left are bed rises under 0.25 m.
        // W-153 row 3 (river notches + small falls): notches cut rises left by later ground passes (dryRun 9 -> 7);
        // channel water held to a fall's lip (dryRun 7 -> 3, wall 972 -> 930, fallFoot 5 -> 2) makes each step a small
        // fall, which the probe counts as step 970 -> 1013, deep 43 -> 45 and tilt 29 -> 30 at the lips. Re-based here.
        // Reverted: that channel water stood over ground the lower valley had dug, so rivers made raised water blocks
        // with falls on every side. Notches stay; counts go back to dryRun 7, wall 972, deep 43, tilt 29, step 970,
        // fallFoot 5. The river rework (docs/agents/river-rework.md) is meant to take wall to 0.
        // Rework row 2 (carve and water from the nearest profile, blended at joins): dryRun 7 -> 0, wall 972 -> 457,
        // deep 43 -> 11, tilt 29 -> 18, step 970 -> 610, fallHead 46 -> 0, fallFoot 5 -> 0. Re-based here.
        // Rework row 3 (fall profile: bed and water step on one straight lip line, a foot pool below, every bed step
        // of 1 m or more a described fall, 75 -> 138 falls): wall 457 -> 397, deep 11 -> 10, tilt 18 -> 17,
        // step 610 -> 225. Re-based here.
        // Rework row 4 (lake clip kept to river channels): deep ground under a lake is lake bed, not a dry hole;
        // wall 397 -> 89 (lake walls 307 -> 5, thin fringes the row 6 ground test covers). Re-based here.
        // Row 3 tail (fall lip shoulders: rock beside a sheet rises just above the upper water; lip pairs on an oblique
        // fall's minor grid axis count as the fall's sheet): wall 89 -> 36 (fall walls 56 -> 5), step 225 -> 170. Re-based here.
        // Perched-lake fix (lakes re-lowered to a dam-cut outlet, reshaped): bend 82 -> 83. Re-based here.
        // Lake exits and left-lake river beds kept to the lowered lake (#617 follow-up): uphillGraph 10 -> 0, deep 10 -> 1
        // (rivers in an old lake bed meet the lowered lake's surface, not its floor).
        // Wall 36 -> 48: the -1750,-750 lake dropped 2.5 m to its dam-cut outlet and bared its old bed below the -2006,-1064 pond's ledge.
        // Wall 48 -> 49 (fall-flank): the blocky steps beside a sheet leave one more wall by a fall (7 now).
        // Wall 49 -> 11 (#365 bay mask): lake bays flood per pocket, not per point (pool walls 37 -> 3, lake walls 5 -> 1).
        // Wall 7 -> 4 (fall-walls): lip shoulders follow the carved edge and foot bowls close before the water's edge; step 129 -> 125.
        // Deep 1 -> 0 (lake-flow tie): a river at its lake's own level now takes the lake, so the last deep point goes.
        // Tilt 17 -> 15, step 125 -> 108 (W-181 row 3 cascade pools): steep reaches step down as short falls between flat pools.
        // Wall 4 -> 3 (W-173 lake-rim gully): dropped bay cells reflood to their level.
        static readonly int[] WaterTruthBaseline = { 83, 0, 0, 0, 0, 0, 0, 3, 0, 15, 108, 0, 0 };

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

        const int VillageLow = 8, VillageFolkLow = 4, VillageHigh = 50;   // 50 since A-110 merged with main: every outdoor member spawns as itself (26 folk at noon) beside the household pets (main's lizards), 44 in all; was 40

        /// <summary>TestSceneSetup's spawn and weather, and a checked shot of the result (the shot helper's own test).</summary>
        [UnityTest, Order(6)]
        public IEnumerator Setup_SpawnRainAndShot()
        {
            var at = Game.Hook<Vector3>("PlayerPosition");
            yield return TestSceneSetup.Spawn("Deer", at.x + 2f, at.z);
            yield return TestSceneSetup.Weather(true);
            yield return TestSceneSetup.Seconds(2f);
            // (P-50: the ready-gated ShotTool shot; the old immediate Shots.Take render stalled batchmode for minutes in rain.)
            yield return Shots.Settled("test-rain-deer", 960, 540);
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
            var mark = LogWatch.Mark();
            Game.RunOptions("-place prop:FishWharf@0,0");
            var start = Time.realtimeSinceStartup;
            while ((Game.Hook<bool>("PropPlacePending") || LogWatch.After(mark, "FishWharf found in all") == null) && Time.realtimeSinceStartup - start < 30f)
                yield return null;
            var found = LogWatch.After(mark, "FishWharf found in all");
            var first = LogWatch.Last("FishWharf #1 at") ?? "no #1 line";
            var framing = LogWatch.Last("framing FishWharf") ?? "no framing line";
            var at = Game.Hook<Vector3>("PlayerPosition");
            LogWatch.Measure($"place prop: {found ?? "no found line"}; {first}; player at {at}");
            Assert.IsNotNull(found, "-place prop: logged nothing (#137)");
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

        /// <summary>W-168: the cave sockets the island file keeps are the ones CaveSockets.Build makes now, cave for cave.</summary>
        [UnityTest, Order(90)]
        public IEnumerator CaveSockets_BakedEqualsBuilt()
        {
            var problems = Game.Hook<string>("CaveSocketsMatchBuild");
            LogWatch.Measure($"{LogWatch.Last("[dungeon] caves:") ?? "no dungeon line"}; baked vs built: {(problems == "" ? "every cave the same" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        /// <summary>W-176 (#475): no cave passage cuts a pit through another (side passages crossed each other at other
        /// heights) and no passage floor drops off a ledge (side passages' dead ends fell 13-20 m into their chamber).</summary>
        [UnityTest, Order(90)]
        public IEnumerator CaveLayout_NoPitsOrLedges()
        {
            var problems = Game.Hook<string>("CaveLayoutProblems");
            LogWatch.Measure($"cave layout: {Game.HookField<string>("CaveLayoutTrail")}; {(problems == "" ? "no pits or ledges" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        /// <summary>W-174 rows 4-5 (#405): the terrain's cut edge round every temple entrance, cave mouth and scarp strip
        /// follows the rim (the seam builder) with no slot or overhang wider than its kind allows, and ground inside a
        /// temple's cut reads as removed.</summary>
        [UnityTest, Order(90), Timeout(1200000)]   // one long frame: every rim cell and collar foot is sampled on the main thread, unbursted
        public IEnumerator Seams_NoOpenRims()
        {
            var problems = Game.Hook<string>("SeamProblems");
            LogWatch.Measure($"seams: {Game.HookField<string>("SeamTrail")}; {(problems == "" ? "no open seams" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        [UnityTest, Order(90), Timeout(600000)]
        public IEnumerator MouthPatch_ExactRims()
        {
            var problems = Game.Hook<string>("MouthPatchProblems");
            LogWatch.Measure($"mouth patch: {Game.HookField<string>("MouthPatchTrail")}; {(problems == "" ? "exact rims" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        /// <summary>W-175 #415: every species carrying a saved blueprint (Model Lab JSON) reads its parts at runtime and
        /// stands in at least one settlement that lists it.</summary>
        [UnityTest, Order(90)]
        public IEnumerator Blueprints_PlacedInSettlements()
        {
            var problems = Game.Hook<string>("BlueprintPlacements");
            LogWatch.Measure($"blueprints: {Game.HookField<string>("BlueprintTrail")}; {(problems == "" ? "all placed" : problems)}");
            Assert.AreEqual("", problems);
            yield return null;
        }

        /// <summary>P-47: no two dungeon chests (the temple's, every cave's, the ruin rooms') share a container id, so a
        /// chest RPC names one chest on every machine.</summary>
        [UnityTest, Order(90)]
        public IEnumerator DungeonChests_IdsUnique()
        {
            var problems = Game.Hook<string>("ChestIdClashes");
            var line = LogWatch.Last("[chests]") ?? "no chests line";
            LogWatch.Measure($"{line}; ids: {(problems == "" ? "no clashes" : problems)}");
            Assert.AreEqual("", problems);
            StringAssert.DoesNotContain(" 0 chests", line);
            yield return null;
        }

        /// <summary>P-46: the sunken temple's sockets are pinned (a hall torch too low for its prop dropped, clutter kept
        /// off chest spots). A layout change moves the hash: re-pin it and add a BENCH-QUEUE note. 646F01C4 since W-182 row 4
        /// (same 73 sockets; CastleGateRuins joins the landmark list). B2ABB7FF since W-182 row 3 (same 73 sockets;
        /// CastleRuins joins the landmark list). C41A5BA2 since W-174 row 2 (same 73 sockets on the re-shaped ground; was 2D2BFAEB).</summary>
        [UnityTest, Order(90)]
        public IEnumerator TempleSockets_Pinned()
        {
            var line = LogWatch.Last("[dungeon] sunken temple");
            LogWatch.Measure(line ?? "no sunken temple dungeon line");
            if (line == null)
                Assert.Inconclusive("the sunken temple was not built in this world");
            StringAssert.Contains("73 sockets", line);
            StringAssert.Contains("hash 646F01C4", line);
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
