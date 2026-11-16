#if UNITY_EDITOR
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Punchy.Fauna;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for the player's replies in talk (A-131, FolkConversation.Options): reading options from JSON, a chain
    /// of Choose calls on a made-up book, the Accept/Decline default, and a walk of the shipped branches. Each returns
    /// what went wrong (empty: all good) and leaves what it saw in OptionsTrail for the log.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>What the last options hook saw, one answer per line.</summary>
        public static string OptionsTrail;

        /// <summary>A conversation section with a three-deep branch, a gesture, an end, a filtered reply and an offer.</summary>
        const string OptionBook = @"{
  ""topics"": { ""work"": ""Work"" },
  ""Any"": {
    ""work"": [ { ""text"": ""I keep bees."", ""options"": [
      { ""say"": ""Bees?"", ""then"": ""bees"" },
      { ""say"": ""Goodbye."", ""do"": ""end"" },
      { ""say"": ""You look tired."", ""state"": ""Tired"" } ] } ],
    ""bees"": [ { ""text"": ""Honey bees, mostly."", ""options"": [
      { ""say"": ""Could I have some honey?"", ""then"": ""honey"" },
      { ""say"": ""Wave."", ""do"": ""gesture:Wave"" } ] } ],
    ""honey"": [ { ""text"": ""Take a jar."", ""offer"": ""item:honey"", ""options"": [
      { ""say"": ""Thank you!"", ""do"": ""accept"", ""then"": ""stings"" },
      { ""say"": ""No, thanks."", ""do"": ""decline"" } ] } ],
    ""stings"": [ { ""text"": ""Mind the stings."", ""options"": [ { ""say"": ""More bees?"", ""then"": ""bees"" } ] } ],
    ""quest"": [ { ""text"": ""There's a job going."", ""offer"": ""quest"" } ],
    ""gift"": [ { ""text"": ""Have an apple."", ""offer"": ""item:apple"" } ],
    ""rumour"": [ { ""text"": ""They say the bridge is out."", ""offer"": ""rumour"" } ]
  }
}";

        static FolkTalker OptionTalker(int i, string job = null)
        {
            var ind = PersonaIndividual(5000 + i);
            return new FolkTalker
            {
                Species = "Human", Job = job, HomeTown = "Millfield", Individual = ind, Persona = FolkPersona.Of(ind),
                State = new FolkState { Hour = 12f },
            };
        }

        /// <summary>The problems a conversation section in JSON reads with (A-131: bad replies named).</summary>
        public static string[] OptionProblems(string json)
        {
            var problems = new JsonProblems();
            FolkConversation.Parse(JObject.Parse(json), problems);
            OptionsTrail = problems.ToString();
            return new List<string>(problems.All).ToArray();
        }

        /// <summary>
        /// A Choose chain on OptionBook: work's replies (the tired one filtered out), Bees? to bees, honey's item offer,
        /// accept carrying the offer to stings at the depth cap (no replies past it), the wave gesture back to the topic
        /// list, goodbye ending, an index out of range; bees, a reply-only topic, never in the topic list.
        /// </summary>
        public static string[] OptionChain()
        {
            var bad = new List<string>();
            var sb = new System.Text.StringBuilder();
            void Check(bool ok, string what)
            {
                if (!ok)
                    bad.Add(what);
            }
            var problems = new JsonProblems();
            FolkConversation.TestTalk = FolkConversation.Parse(JObject.Parse(OptionBook), problems);
            try
            {
                Check(problems.Count == 0, "the test book reads with problems: " + problems);
                var who = OptionTalker(0);
                var a0 = FolkConversation.Ask(who, "work");
                sb.Append(a0).Append('\n');
                Check(a0.Text == "I keep bees." && a0.Depth == 0, "work: " + a0);
                Check(a0.Options != null && a0.Options.Length == 2, "work: the tired reply should be filtered out, two left");
                var a1 = FolkConversation.Choose(who, a0, 0);
                sb.Append(a1).Append('\n');
                Check(a1.Topic == "bees" && a1.Text == "Honey bees, mostly." && a1.Depth == 1 && a1.Chosen.Say == "Bees?", "Bees?: " + a1);
                var a2 = FolkConversation.Choose(who, a1, 0);
                sb.Append(a2).Append('\n');
                Check(a2.Topic == "honey" && a2.Offer.Kind == OfferKind.Item && a2.Offer.Id == "honey" && a2.Depth == 2, "honey: " + a2);
                Check(a2.Options != null && a2.Options.Length == 2 && a2.Options[0].Do == OptionDo.Accept, "honey: its own accept/decline replies");
                var a3 = FolkConversation.Choose(who, a2, 0);
                sb.Append(a3).Append('\n');
                Check(a3.Topic == "stings" && a3.Depth == 3 && a3.Chosen.Do == OptionDo.Accept && a3.ChosenOffer.Kind == OfferKind.Item &&
                      a3.ChosenOffer.Id == "honey", "accept: " + a3);
                Check(a3.Options == null, "at the depth cap stings should offer no replies");
                var wave = FolkConversation.Choose(who, a1, 1);
                sb.Append(wave).Append('\n');
                Check(wave.Text == null && wave.Action == IdleAction.Wave && !wave.Ends, "wave: back to the topic list with the gesture: " + wave);
                var bye = FolkConversation.Choose(who, a0, 1);
                sb.Append(bye).Append('\n');
                Check(bye.Ends && bye.Text == null, "goodbye should end the talk: " + bye);
                var none = FolkConversation.Choose(who, a0, 7);
                Check(none.Text == null && none.Chosen.Say == null, "an index out of range should choose nothing");
                foreach (var t in FolkConversation.Topics(who))
                    Check(t.Id != "bees", "bees (no label) shows in the topic list");
            }
            finally
            {
                FolkConversation.TestTalk = null;
                FolkConversation.ForgetAsks();
            }
            OptionsTrail = sb.ToString();
            return bad.ToArray();
        }

        /// <summary>A line with a quest or item offer and no replies of its own answers with Accept then Decline, as the
        /// talk window has always offered; one with another offer, or none, has no replies.</summary>
        public static string[] OptionDefaults()
        {
            var bad = new List<string>();
            var sb = new System.Text.StringBuilder();
            FolkConversation.TestTalk = FolkConversation.Parse(JObject.Parse(OptionBook));
            try
            {
                // (A quest line needs a folk with a quest to give: half of them have one.)
                var who = OptionTalker(1);
                for (var i = 1; i <= 40 && FolkConversation.Ask(who, "quest").Offer.Kind != OfferKind.Quest; i++)
                    who = OptionTalker(1 + i);
                foreach (var (topic, pair) in new[] { ("quest", true), ("gift", true), ("rumour", false), ("who", false) })
                {
                    var a = FolkConversation.Ask(who, topic);
                    sb.Append(a).Append('\n');
                    var o = a.Options;
                    if (pair && (o == null || o.Length != 2 || o[0].Do != OptionDo.Accept || o[0].Say != "Accept" || o[1].Do != OptionDo.Decline ||
                                 o[1].Say != "Decline" || o[0].Then != null || o[1].Then != null))
                        bad.Add($"{topic}: should offer Accept, Decline: {a}");
                    if (!pair && o != null)
                        bad.Add($"{topic}: should offer no replies: {a}");
                }
                var quest = FolkConversation.Ask(who, "quest");
                var took = FolkConversation.Choose(who, quest, 0);
                if (took.Text != null || took.Chosen.Do != OptionDo.Accept || took.ChosenOffer.Kind != OfferKind.Quest)
                    bad.Add("Accept on a quest should go back to the topic list carrying the quest offer: " + took);
            }
            finally
            {
                FolkConversation.TestTalk = null;
                FolkConversation.ForgetAsks();
            }
            OptionsTrail = sb.ToString();
            return bad.ToArray();
        }

        /// <summary>
        /// A-131 row 2: the shipped branching exchange of folk with this job (FolkTalker.Job), walked. Asks each topic
        /// of up to 40 such folk until an answer has a reply with a then, then follows every reply to the depth cap:
        /// every then answered (text, no unfilled slot), every reply's words filled, depth never past MaxDepth.
        /// Returns the problems; "none found" when no folk of the job got a branching answer.
        /// </summary>
        public static string[] OptionWalk(string job)
        {
            var bad = new List<string>();
            var sb = new System.Text.StringBuilder();
            var visited = 0;
            void Walk(in FolkTalker who, in Answer a, string indent)
            {
                if (a.Options == null)
                    return;
                if (a.Depth > FolkConversation.MaxDepth)
                    bad.Add($"past the depth cap: {a}");
                for (int i = 0; i < a.Options.Length; i++)
                {
                    var o = a.Options[i];
                    if (string.IsNullOrEmpty(o.Say) || o.Say.IndexOf('{') >= 0)
                        bad.Add($"reply words unfilled: {o}");
                    var next = FolkConversation.Choose(who, a, i);
                    visited++;
                    sb.Append(indent).Append(o).Append(" => ").Append(next).Append('\n');
                    if (o.Then != null && (string.IsNullOrEmpty(next.Text) || next.Text == "..." || next.Text.IndexOf('{') >= 0 || next.Topic != o.Then))
                        bad.Add($"{o.Then} not answered: {next}");
                    if (o.Then != null)
                        Walk(who, next, indent + "  ");
                }
            }
            var found = false;
            for (int i = 0; i < 40 && !found; i++)
            {
                var who = OptionTalker(100 + i, job);
                foreach (var t in FolkConversation.Topics(who))
                {
                    var a = FolkConversation.Ask(who, t.Id);
                    if (a.Options == null || !System.Array.Exists(a.Options, o => o.Then != null))
                        continue;
                    found = true;
                    sb.Append(job).Append(' ').Append(a).Append('\n');
                    Walk(who, a, "  ");
                    break;
                }
            }
            FolkConversation.ForgetAsks();
            if (!found)
                bad.Add($"none found: no {job} answered with a branching reply");
            sb.Append($"{visited} replies followed\n");
            OptionsTrail = sb.ToString();
            return bad.ToArray();
        }
    }
}
#endif
