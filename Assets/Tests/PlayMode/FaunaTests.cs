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

        // Built-in count and name hash in index order, pinned 2026-11-15 before any Lab species existed. A new built-in
        // species moves this on purpose (re-pin); a Lab species never should.
        const string BuiltIns = "124 3B1B2423 ";
    }
}
