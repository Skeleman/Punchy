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

        /// <summary>
        /// Target (row 3): 200 folk met by an animal of each group take it in character: every reaction turns up; a small
        /// animal is mostly petted, a predator mostly feared (scared or wary), livestock mostly puzzling or petted, vermin
        /// mostly repelled, a big beast awes; no empty line, no {slot}. The live species land in the right groups
        /// (Rabbit small, Wolf predator, Horse livestock).
        /// </summary>
        [UnityTest]
        public IEnumerator Beast_ReactsInCharacter()
        {
            const string groups = "small,predator,livestock,vermin,big,pet,beast";
            var ids = groups.Split(',');
            var r = Game.Hook<float[]>("BeastMeet", groups, 200, 4);
            var dump = Game.HookField<string>("BeastMeetText");
            // (BeastReaction in order: the test assembly sees the game only through hooks.)
            var names = new[] { "None", "Pet", "Puzzled", "Wary", "Scared", "Repelled", "Awed" };
            var k = names.Length;
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < ids.Length; g++)
            {
                sb.Append($"\n  {ids[g]}:");
                for (int i = 0; i < k; i++)
                    if (r[g * k + i] > 0f)
                        sb.Append($" {names[i]} {r[g * k + i]:F0}");
            }
            LogWatch.Measure($"beast meet (200 folk per group):{sb}\n{dump}");
            float At(string group, string x) => r[System.Array.IndexOf(ids, group) * k + System.Array.IndexOf(names, x)];
            for (int i = 1; i < k; i++)
            {
                var total = 0f;
                for (int g = 0; g < ids.Length; g++)
                    total += r[g * k + i];
                Assert.Greater(total, 0f, $"nobody was {names[i]}");
            }
            for (int g = 0; g < ids.Length; g++)
                Assert.AreEqual(0f, r[g * k], $"{ids[g]}: a meeting without a reaction");
            Assert.Greater(At("small", "Pet"), 100f, "a small animal is not mostly petted");
            Assert.Greater(At("predator", "Scared") + At("predator", "Wary"), 140f, "a predator is not mostly feared");
            Assert.Greater(At("livestock", "Puzzled") + At("livestock", "Pet"), 100f, "livestock is not mostly puzzling or petted");
            Assert.Greater(At("vermin", "Repelled"), 100f, "vermin is not mostly repelled");
            Assert.Greater(At("big", "Awed"), 80f, "a big beast awes too few");
            StringAssert.DoesNotContain("{", dump);
            StringAssert.DoesNotContain("\"\"", dump, "an empty beast line");
            StringAssert.DoesNotContain(": \n", dump, "an empty beast line");
            StringAssert.Contains(" Rabbit=small", dump);
            StringAssert.Contains(" Wolf=predator", dump);
            StringAssert.Contains(" Horse=livestock", dump);
            yield return null;
        }

        /// <summary>
        /// Target (row 3): a live folk sent off by an animal really does it, with what its brain already does: told to flee
        /// it is in Flee two seconds on and further off; told to come closer it walks (Wander) toward the player.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveFolk_BeastResponse()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            var r = Game.Hook<float[]>("BeastRespond", "Flee");
            Assert.AreEqual(1f, r[1], "no live folk took the flee");
            var before = Game.Hook<string>("FolkMode");
            yield return TestSceneSetup.Seconds(2f);
            var after = Game.Hook<string>("FolkMode");
            r = Game.Hook<float[]>("BeastRespond", "Approach");
            var approach = Game.Hook<string>("FolkMode");
            LogWatch.Measure($"beast response: flee {before} -> {after}; approach {approach}");
            Assert.AreEqual(1f, r[1], "no live folk took the approach");
            StringAssert.StartsWith("Flee ", after, "the folk told to flee isn't fleeing");
            Assert.Greater(float.Parse(after.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(before.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture), "the fleeing folk got no further off");
            StringAssert.StartsWith("Wander ", approach, "the folk told to come closer isn't walking");
        }

        /// <summary>
        /// Card A-93 row 3 shots: the same village folk met by the player as a human, a rabbit, a wolf and a horse; the
        /// sheet's labels carry what it said and did.
        /// </summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(600000)]
        public IEnumerator Beast_Shots()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            yield return TestSceneSetup.TimeOfDay(10f);
            Game.Hook<bool>("ShotRunStart");
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Human");
            var labels = new System.Collections.Generic.List<string>();
            var first = true;
            try
            {
                foreach (var body in new[] { "Human", "Rabbit", "Wolf", "Horse" })
                {
                    Game.Hook<string>("SetBody", body);
                    yield return TestSceneSetup.Seconds(2f);
                    var f = Game.Hook<float[]>("BeastLive", first);
                    first = false;
                    Assert.Greater(f.Length, 0, "no folk to meet");
                    yield return TestSceneSetup.Seconds(1f);
                    // (Again, now the player stands in front of it: its answer, and the spot it is at now.)
                    f = Game.Hook<float[]>("BeastLive", false);
                    var text = Game.HookField<string>("BeastLiveText");
                    StringAssert.StartsWith(body + ":", text, "the body didn't switch");
                    labels.Add(text);
                    var folk = new Vector3(f[0], f[1], f[2]);
                    var player = new Vector3(f[4], f[1], f[5]);
                    var mid = (folk + player) * 0.5f + Vector3.up * f[3] * 0.6f;
                    var along = (player - folk).normalized;
                    var side = new Vector3(along.z, 0f, -along.x);
                    var from = mid + side * 5f + Vector3.up * 1.2f;
                    Game.RunOptions(string.Format(System.Globalization.CultureInfo.InvariantCulture, "-shotcam {0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2}", from.x, from.y, from.z, mid.x, mid.y, mid.z));
                    yield return Shots.Settled($"a93-beast-{body.ToLowerInvariant()}", 1280, 720);
                }
            }
            finally
            {
                Game.RunOptions("-shotcam off");
                Game.Hook<string>("SetBody", was);
                Game.Hook<object>("PutBodyPref", pref);
            }
            var sheet = Game.Hook<string[]>("ShotRunSheet", System.IO.Path.Combine(Shots.Folder, "a93-beast-sheet.jpg"), labels.ToArray());
            LogWatch.Measure("beast shots:\n" + string.Join("\n", sheet));
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
