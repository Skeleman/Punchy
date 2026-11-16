using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Play-mode checks on the fauna species table (M-06): the network names a creature by its species index, so the
    /// built-in species keep theirs when the Model Lab saves a new one (it goes after them, LabSpeciesList).
    /// </summary>
    public class FaunaTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>Target: the built-in species' count and name hash in index order, as on main before M-06 row 2. A
        /// new built-in species re-pins this (and Fauna_Fingerprint); a Lab species must not.</summary>
        [UnityTest]
        public IEnumerator Species_BuiltInIndicesUnchanged()
        {
            var table = Game.Hook<string>("BuiltInSpecies");
            LogWatch.Measure("species " + table);
            StringAssert.StartsWith(BuiltIns, table);
            Assert.AreEqual("", Game.Hook<string>("SpeciesOrderProblems"));
            yield return null;
        }

        /// <summary>
        /// #24 phase 2 row 3: on the pinned seed, no dungeon has more creature picks (each a bit in its dead mask) than
        /// the mask holds, once every dungeon is registered.
        /// </summary>
        [UnityTest]
        public IEnumerator Dungeons_ResidentsFitDeadMask()
        {
            yield return AllDungeonsRegistered();
            Assert.AreEqual("", Game.Hook<string>("DungeonResidentOverflow"));
        }

        /// <summary>
        /// #24 phase 2 row 3: the residents a joined client builds from the seed are the host's records, ordinal for
        /// ordinal (resident type, socket and post), in every dungeon: an ordinal names the same creature on both.
        /// </summary>
        [UnityTest]
        public IEnumerator Dungeons_ClientOrdinalsMatchHost()
        {
            yield return AllDungeonsRegistered();
            Assert.AreEqual("", Game.Hook<string>("DungeonOrdinalProblems"));
        }

        /// <summary>
        /// #24 phase 2 row 4 (rule 20): the dungeon props are drawn by screen size (whole, as their biggest part, or
        /// not at all), not kept as GameObjects drawn at any distance.
        /// </summary>
        [UnityTest]
        public IEnumerator Dungeons_PropsDrawnByScreenSize()
        {
            yield return AllDungeonsRegistered();
            yield return null;
            yield return null;
            Assert.AreEqual("", Game.Hook<string>("DungeonPropTiers"));
        }

        /// <summary>Waits (up to 60 s) until the director has residents for every dungeon (caves come a few per frame).</summary>
        static IEnumerator AllDungeonsRegistered()
        {
            var until = UnityEngine.Time.realtimeSinceStartup + 60f;
            string state;
            while (true)
            {
                state = Game.Hook<string>("DungeonsRegistered");
                var parts = state.Split('/');
                if (parts.Length == 2 && parts[0] == parts[1] && parts[1] != "0" || UnityEngine.Time.realtimeSinceStartup > until)
                    break;
                yield return null;
            }
            LogWatch.Measure("dungeons registered " + state);
            var p = state.Split('/');
            Assert.IsTrue(p.Length == 2 && p[0] == p[1] && p[1] != "0", "dungeons registered " + state);
        }

        // Built-in count and name hash in index order, pinned 2026-11-15 before any Lab species existed. A new built-in
        // species moves this on purpose (re-pin); a Lab species never should.
        const string BuiltIns = "124 3B1B2423 ";
    }
}
