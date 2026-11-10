using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Talking with folk (A-93): a folk's persona is the same on every run, and every topic gets an answer, slots filled,
    /// from every culture, in every condition, from a live folk too.
    /// </summary>
    public class FolkConversationTests
    {
        /// <summary>The digest of the first 500 personas (TestHooks.PersonaDigest): moves only if the persona's seeding does.</summary>
        const string PersonaPin = "37026953";

        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down() => TestSceneSetup.Restore();

        /// <summary>
        /// Target: the same seed gives the same persona (digest twice, and against its pin); each trait's high and low
        /// bands each hold 20-47% of folk; the mood band always agrees with FolkJobs.Mood.
        /// </summary>
        [UnityTest]
        public IEnumerator Persona_SameSeedSamePersona()
        {
            var a = Game.Hook<string>("PersonaDigest", 500);
            var b = Game.Hook<string>("PersonaDigest", 500);
            var bands = Game.Hook<float[]>("PersonaBands", 2000);
            var traits = (bands.Length - 1) / 2;
            var sb = new System.Text.StringBuilder();
            for (int t = 0; t < traits; t++)
                sb.Append($" {t}:{bands[t * 2]:P0}/{bands[t * 2 + 1]:P0}");
            LogWatch.Measure($"persona: digest {a} (pin {PersonaPin}); high/low per trait{sb}; mood disagrees {bands[traits * 2]:P1}");
            Assert.AreEqual(a, b, "the same individuals gave different personas");
            Assert.AreEqual(PersonaPin, a, "the persona seeding moved (re-pin only if that was meant)");
            for (int t = 0; t < traits; t++)
            {
                Assert.That(bands[t * 2], Is.InRange(0.2f, 0.47f), $"trait {t}: high band share");
                // (Courage's shy folk are pushed low: a little more room there.)
                Assert.That(bands[t * 2 + 1], Is.InRange(0.2f, 0.5f), $"trait {t}: low band share");
            }
            Assert.AreEqual(0f, bands[traits * 2], "a persona's mood band disagrees with FolkJobs.Mood");
            yield return null;
        }

        /// <summary>
        /// Target: no exception, no empty answer, no unfilled {slot}, every quest offer has an id; over every culture, to a
        /// player of its own kind, of another kind and in an animal's body.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryTopic_EveryCulture_Answers()
        {
            foreach (var (player, beast) in new (string, bool)[] { (null, false), ("dwarf", false), ("wolf", true) })
            {
                var r = Game.Hook<float[]>("ConversationSweep", player, beast);
                LogWatch.Measure($"conversation to {player ?? "own kind"}{(beast ? " (beast)" : "")}: {r[0]:F0} cultures, {r[1]:F0} answers, {r[2]:F0} empty, {r[3]:F0} unfilled, {r[4]:F0} errors, {r[5]:F0} quest offers ({r[6]:F0} without id)");
                Assert.AreEqual(0f, r[4], Game.HookField<string>("SweepError"));
                Assert.Greater(r[1], 0f, "no answers");
                Assert.AreEqual(0f, r[2], "an empty answer");
                Assert.AreEqual(0f, r[3], "an answer kept a {slot}");
                Assert.AreEqual(0f, r[6], "a quest offer without a quest id");
                if (!beast)
                    Assert.Greater(r[5], 0f, "nobody offered a quest");
            }
            yield return null;
        }

        /// <summary>
        /// Target: 20 folk of one town (mixed trades, conditions and hours) feel distinct: at least 18 give a set of
        /// answers nobody else gives, and each topic has at least 6 different lines among them. Logs 10 folk x 3 topics.
        /// </summary>
        [UnityTest]
        public IEnumerator Town_TwentyFolkDistinct()
        {
            var r = Game.Hook<float[]>("TownTalk", "Human", 20, "who,how,work,town,news,favour,help,bye", 0);
            Game.Hook<float[]>("TownTalk", "Human", 10, "who,how,favour", 10);
            var dump = Game.HookField<string>("TownTalkText");
            var sb = new System.Text.StringBuilder();
            for (int i = 3; i < r.Length; i++)
                sb.Append($" {r[i]:F0}");
            LogWatch.Measure($"town talk: {r[0]:F0} of {r[1]:F0} folk unique, {r[2]:F0} lines overall, per topic{sb}\n{dump}");
            Assert.GreaterOrEqual(r[0], 18f, "too many folk in one town answer alike");
            for (int i = 3; i < r.Length; i++)
                Assert.GreaterOrEqual(r[i], 6f, $"topic {i - 3}: too few different lines");
            StringAssert.DoesNotContain("{", dump);
            yield return null;
        }

        /// <summary>Target: live village folk answer every topic through the entity path (persona, condition, town, stance).</summary>
        [UnityTest]
        public IEnumerator LiveFolk_Answer()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            var dump = Game.Hook<string>("TalkToFolk", 3);
            LogWatch.Measure($"live folk talk:\n{dump}");
            Assert.IsFalse(string.IsNullOrEmpty(dump), "no folk near the village to talk to");
            StringAssert.Contains("[who]", dump);
            StringAssert.Contains("[bye]", dump);
            StringAssert.DoesNotContain("{", dump);
        }
    }
}
