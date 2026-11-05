using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Folk talk (W-97): walk-by lines never carry through a wall, every slot is filled, and Any's shared lines stay a
    /// minority of what a culture with its own lines says.
    /// </summary>
    public class FolkTalkTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>
        /// Target: none (zero lines through a kit wall). The player hops beside village folk for 45 s, some spots the
        /// far side of a wall; every line that comes up is checked against the kit boxes between the two heads.
        /// </summary>
        [UnityTest]
        public IEnumerator WalkBy_NeverThroughWall()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            Game.Hook<int[]>("FolkTalkScan");
            int lines = 0, walled = 0, unfilled = 0, heldBack = 0;
            for (int hop = 0; hop < 18; hop++)
            {
                Game.Hook<bool>("BesideFolk", hop, hop * 2.4f, 3.5f);
                yield return TestSceneSetup.Seconds(2.5f);
                var scan = Game.Hook<int[]>("FolkTalkScan");
                lines += scan[0];
                walled += scan[1];
                unfilled += scan[2];
                heldBack = scan[3];
            }
            LogWatch.Measure($"folk talk: {lines} lines up, {walled} through a wall, {unfilled} unfilled, {heldBack} held back by walls (all run)");
            Assert.Greater(lines, 0, "no walk-by lines came up to check");
            Assert.AreEqual(0, walled, "a walk-by line came up through a wall");
            Assert.AreEqual(0, unfilled, "a line came up with an unfilled {slot}");
        }

        /// <summary>Target: never an unfilled slot. Every culture, to a player of another kind and of its own kind.</summary>
        [UnityTest]
        public IEnumerator PlayerSlot_AlwaysFilled()
        {
            var other = Game.Hook<float[]>("DialogueStats", 300, "human");
            var same = Game.Hook<float[]>("DialogueStats", 300, null);
            LogWatch.Measure($"dialogue: {other[0]:F0} cultures, {other[1]:F0} picks to a human, {other[2]:F0} unfilled; {same[1]:F0} picks to own kind, {same[2]:F0} unfilled");
            Assert.Greater(other[1], 0f, "no lines picked (dialogue archive not loaded?)");
            Assert.AreEqual(0f, other[2], "a line to another kind kept a {slot}");
            Assert.AreEqual(0f, same[2], "a line to its own kind kept a {slot} ({player} has nothing to fill it)");
            yield return null;
        }

        /// <summary>
        /// Target: card W-97, Any's shared lines 30% of picks or fewer, over every culture with lines of its own
        /// (DialogueArchive weights them by AnyShare = 0.3 against a culture's own).
        /// </summary>
        [UnityTest]
        public IEnumerator AnyLines_ThirtyPercentOrLess()
        {
            var stats = Game.Hook<float[]>("DialogueStats", 400, "human");
            var share = stats[1] > 0f ? stats[3] / stats[1] : 1f;
            LogWatch.Measure($"Any share: {share:P1} of {stats[1]:F0} picks over {stats[0]:F0} cultures; worst {Game.HookField<string>("WorstAnyCulture")} {stats[4]:P1}");
            Assert.LessOrEqual(share, 0.30f);
            yield return null;
        }
    }
}
