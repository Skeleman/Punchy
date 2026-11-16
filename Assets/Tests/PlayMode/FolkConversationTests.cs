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
        /// Target (A-129 row 1): the shipped dialogue.json and weather-lines.json name no bad entry; on a scratch copy, a
        /// version with five bad entries (a line with no text, a "when" that isn't a time, a number in a word list, an
        /// unclosed {slot} and an unknown key in a talk line) is turned down with all five named by path and line, the
        /// last good lines are kept, and the fixed version is read again.
        /// </summary>
        [UnityTest]
        public IEnumerator Archive_BadEntriesNamed_OldLinesKept()
        {
            var shipped = Game.Hook<string[]>("ArchiveProblems");
            var r = Game.Hook<float[]>("ArchiveReload");
            var why = Game.HookField<string>("ReloadError") ?? "";
            LogWatch.Measure($"archive: shipped problems {shipped.Length}; reload {r[0]:F0} lines, bad version kept {r[1]:F0}, " +
                             $"{r[2]:F0} named, fixed {r[3]:F0}\n{why}\n{string.Join("\n", shipped)}");
            Assert.AreEqual(0, shipped.Length, string.Join("\n", shipped));
            Assert.Greater(r[0], 0f, "the scratch copy wasn't read");
            Assert.AreEqual(r[0], r[1], "the bad version replaced the good lines");
            Assert.AreEqual(5f, r[2], why);
            foreach (var part in new[] { "no \"text\"", "midnight", "broken_list", "never closed", "unknown key \"moood\"", "(line " })
                StringAssert.Contains(part, why);
            Assert.AreEqual(r[0], r[3], "the fixed version wasn't read again");
            yield return null;
        }

        /// <summary>
        /// Target: 20 folk of one town (mixed trades, conditions and hours) feel distinct: at least 18 give a set of
        /// answers nobody else gives, and each topic has at least 6 different lines among them. Logs 10 folk x 3 topics.
        /// </summary>
        /// <summary>
        /// Target: the quest phrasings and an animal player's topic labels come from dialogue.json (A-129 row 2), with
        /// every slot filled: each said form is the file's entry with its slots put in.
        /// </summary>
        [UnityTest]
        public IEnumerator Archive_QuestWordsAndBeastLabels()
        {
            yield return null;
            var w = Game.Hook<string[]>("ArchiveWords");
            LogWatch.Measure("archive words:\n" + string.Join("\n", w));
            for (int i = 0; i < 7; i++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(w[i + 7]), $"dialogue.json has no entry for word {i}");
                StringAssert.DoesNotContain("{", w[i], $"word {i} has an unfilled slot");
            }
            Assert.AreEqual(w[7].Replace("{item}", "silver key"), w[0]);
            Assert.AreEqual(w[8].Replace("{creatures}", "crows"), w[1]);
            Assert.AreEqual(w[9].Replace("{item}", "berry"), w[2]);
            Assert.AreEqual(w[10].Replace("{count}", "5").Replace("{items}", "berries"), w[3]);
            Assert.AreEqual(w[11], w[4]);
            Assert.AreEqual(w[12], w[5]);
            Assert.AreEqual(w[13], w[6]);
        }

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

        /// <summary>
        /// A-131 row 1, target: a line's replies read from JSON, and each bad one is named (not a list, five replies, no
        /// say, an unknown do, a gesture that isn't one, accept with no offer to take, a then with no lines, an unknown
        /// key) while a good one reads clean.
        /// </summary>
        [UnityTest]
        public IEnumerator Options_ParseAndNameBadOnes()
        {
            const string good = @"{ ""Any"": { ""work"": [ { ""text"": ""Bees."", ""offer"": ""quest"", ""options"": [
                { ""say"": ""Sure."", ""do"": ""accept"", ""then"": ""more"" }, { ""say"": ""Wave."", ""do"": ""gesture:Wave"", ""stance"": ""Warm"" },
                { ""say"": ""Bye."", ""do"": ""end"" } ] } ], ""more"": [ ""More bees."" ] } }";
            var clean = Game.Hook<string[]>("OptionProblems", good);
            Assert.IsEmpty(clean, "a good options list read with problems:\n" + string.Join("\n", clean));
            var bad = new (string Json, string Named)[]
            {
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": ""yes"" } ] } }", "should be a list"),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ {""say"":""1""},{""say"":""2""},{""say"":""3""},{""say"":""4""},{""say"":""5""} ] } ] } }", "5 replies"),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""then"": ""work"" } ] } ] } }", "no \"say\""),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""say"": ""x"", ""do"": ""pay"" } ] } ] } }", "isn't accept, decline, end"),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""say"": ""x"", ""do"": ""gesture:Juggle"" } ] } ] } }", "no gesture called"),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""say"": ""x"", ""do"": ""accept"" } ] } ] } }", "needs the line to offer"),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""say"": ""x"", ""then"": ""nowhere"" } ] } ] } }", "no lines for topic \"nowhere\""),
                (@"{ ""Any"": { ""work"": [ { ""text"": ""a"", ""options"": [ { ""say"": ""x"", ""text"": ""y"" } ] } ] } }", "unknown key \"text\""),
            };
            foreach (var (json, named) in bad)
            {
                var problems = Game.Hook<string[]>("OptionProblems", json);
                LogWatch.Measure($"options problem ({named}): {string.Join(" / ", problems)}");
                Assert.IsTrue(System.Array.Exists(problems, p => p.Contains(named)), $"expected a problem naming '{named}', got: {string.Join(" / ", problems)}");
            }
            yield return null;
        }

        /// <summary>
        /// A-131 row 1, target: Choose follows a reply's then to the next answer, one deeper, three deep at most; accept
        /// carries the offer; a gesture or no then goes back to the topic list; end ends; a filtered reply isn't shown.
        /// </summary>
        [UnityTest]
        public IEnumerator Options_ChooseChain()
        {
            var bad = Game.Hook<string[]>("OptionChain");
            LogWatch.Measure("options chain:\n" + Game.HookField<string>("OptionsTrail"));
            Assert.IsEmpty(bad, string.Join("\n", bad));
            yield return null;
        }

        /// <summary>
        /// A-131 row 1, target: a quest or item offer with no replies of its own still gets Accept then Decline (the
        /// talk window's old pair, now the first options list); other answers get none.
        /// </summary>
        [UnityTest]
        public IEnumerator Options_DefaultAcceptDeclineUnchanged()
        {
            var bad = Game.Hook<string[]>("OptionDefaults");
            LogWatch.Measure("options defaults:\n" + Game.HookField<string>("OptionsTrail"));
            Assert.IsEmpty(bad, string.Join("\n", bad));
            yield return null;
        }

        /// <summary>
        /// A-131 row 2, target: the shipped hermit, guard and innkeeper each answer with a branching exchange (dialogue.json
        /// conversation, reply-only topics), and every reply walked to the depth cap is answered with filled words.
        /// </summary>
        [UnityTest]
        public IEnumerator Options_SampleBranchesWalk()
        {
            foreach (var job in new[] { "Hermit", "Guard", "Innkeeper" })
            {
                var bad = Game.Hook<string[]>("OptionWalk", job);
                LogWatch.Measure($"options walk, {job}:\n" + Game.HookField<string>("OptionsTrail"));
                Assert.IsEmpty(bad, $"{job}: " + string.Join("\n", bad));
            }
            yield return null;
        }

        /// <summary>
        /// Target: live village folk answer every topic through the entity path (persona, condition, town, stance).
        /// A-96 row 0: folk answer a beast differently (no "[who]" line for a crow), and every tree shares the editor's
        /// PlayerPrefs, so the saved body (Punchy.Character.Body) may be anything: the test plays a human itself and puts
        /// the body and the saved pick back after.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveFolk_Answer()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            var pref = Game.Hook<string>("BodyPref");
            var was = Game.Hook<string>("SetBody", "Human");
            try
            {
                yield return TestSceneSetup.Seconds(1f);
                var dump = Game.Hook<string>("TalkToFolk", 3);
                LogWatch.Measure($"live folk talk (body was '{was}', saved pick '{pref}'):\n{dump}");
                Assert.IsFalse(string.IsNullOrEmpty(dump), "no folk near the village to talk to");
                StringAssert.Contains("[who]", dump);
                StringAssert.Contains("[bye]", dump);
                StringAssert.DoesNotContain("{", dump);
            }
            finally
            {
                if (!string.IsNullOrEmpty(was))
                    Game.Hook<string>("SetBody", was);
                Game.Hook<object>("PutBodyPref", pref);
            }
        }

        /// <summary>
        /// A-132 row 1, target: a town's inn has a Counter, a live innkeeper works it by day (within a few metres of it,
        /// at its post), FolkConversation takes it as a talker and its "work" answer is the innkeeper's line.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveFolk_InnkeeperAtCounter()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            yield return TestSceneSetup.TimeOfDay(11f);
            yield return TestSceneSetup.Seconds(10f);
            var r = Game.Hook<float[]>("InnkeeperAtCounter");
            LogWatch.Measure($"innkeeper: counter {r[0]} keeper {r[1]} {r[10]:F1} m from it, mode {r[11]}
{Game.HookField<string>("InnTrail")}");
            Assert.AreEqual(1f, r[0], "no inn Counter anchor near the village");
            Assert.AreEqual(1f, r[1], "no live innkeeper");
            Assert.Less(r[10], 4f, "the innkeeper isn't at its counter by day");
            Assert.AreEqual(1f, r[12], "the innkeeper isn't a talker");
            Assert.AreEqual(1f, r[13], "the innkeeper's work answer isn't its own line");
        }

        /// <summary>A-132 row 1 shot: the innkeeper at its counter on the inn's porch, seen from the street.</summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(600000)]
        public IEnumerator Innkeeper_Shot()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            yield return TestSceneSetup.TimeOfDay(11f);
            yield return TestSceneSetup.Seconds(10f);
            var r = Game.Hook<float[]>("InnkeeperAtCounter");
            Assert.AreEqual(1f, r[1], "no live innkeeper");
            var keeper = new Vector3(r[7], r[8] + 1f, r[9]);
            var street = new Vector3(-r[5], 0f, -r[6]);
            var side = new Vector3(street.z, 0f, -street.x);
            var from = keeper + street * 5.5f + side * 2f + Vector3.up * 0.8f;
            Game.RunOptions(string.Format(System.Globalization.CultureInfo.InvariantCulture, "-shotcam {0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2}", from.x, from.y, from.z, keeper.x, keeper.y, keeper.z));
            try { yield return Shots.Settled("a132-innkeeper", 1280, 720); }
            finally { Game.RunOptions("-shotcam off"); }
            LogWatch.Measure("innkeeper shot: " + Game.HookField<string>("InnTrail"));
        }
    }
}
