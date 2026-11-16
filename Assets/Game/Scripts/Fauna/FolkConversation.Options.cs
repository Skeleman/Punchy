using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Punchy.Fauna
{
    /// <summary>What picking a reply does besides the folk's answer (A-131), from a fixed list: take or turn down the
    /// line's offer (the talk window does that through Items), end the talk, or have the folk make a gesture. Nothing
    /// here changes host state: a do that would (pay, give) needs its own Request and host check first.</summary>
    public enum OptionDo : byte { None, Accept, Decline, End, Gesture }

    /// <summary>One of the player's replies to an answer (A-131): the words, the topic whose lines answer it (null: back
    /// to the topic list), and what it does (Gesture: the gesture's name, Action it parsed).</summary>
    public struct Option
    {
        public string Say, Then;
        public OptionDo Do;
        public string Gesture;
        public IdleAction Action;
        public override string ToString() => $"\"{Say}\"" + (Then != null ? $" -> {Then}" : "") +
                                             (Do == OptionDo.Gesture ? $" <{Gesture}>" : Do != OptionDo.None ? $" ({Do.ToString().ToLowerInvariant()})" : "");
    }

    /// <summary>
    /// The player's replies (A-131, docs/design/dialogue-options.md): a talk line's "options" in dialogue.json, filled
    /// and filtered like lines, plus Choose, which follows a reply to the folk's next answer. Accept/Decline for a quest
    /// or item offer is the default options list, not a second path (rule 33). Words only: nothing goes over the network.
    /// </summary>
    public static partial class FolkConversation
    {
        /// <summary>How many replies deep a branch goes: past it, only the defaults (or nothing) are offered.</summary>
        public const int MaxDepth = 3;

        /// <summary>The most replies a line can offer.</summary>
        public const int MaxOptions = 4;

        /// <summary>A talk line's reply as read: its words and conditions (Line: Text is the say, the rest match as a
        /// line's keys do), its then and its do.</summary>
        public class TalkOption
        {
            public TalkLine Line;
            public string Then;
            public OptionDo Do;
            public string Gesture;
        }

        /// <summary>Tests (TestHooks): a conversation book used in place of dialogue.json's, so a test doesn't hang on the
        /// shipped lines. Null in play.</summary>
        internal static TalkBook TestTalk;

        /// <summary>The keys an option can have, besides the line condition keys and persona traits.</summary>
        static readonly HashSet<string> OptionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "say", "then", "do", "when", "weather", "mood", "stance", "trade", "group", "willing", "pestered", "need", "home",
            "job", "kin", "gone", "pet", "state"
        };

        /// <summary>The replies the code offers when a line has none: Accept and Decline for a quest or an item.</summary>
        public static Option[] DefaultOptions(Offer offer) =>
            offer.Kind == OfferKind.Quest || offer.Kind == OfferKind.Item
                ? new[] { new Option { Say = "Accept", Do = OptionDo.Accept }, new Option { Say = "Decline", Do = OptionDo.Decline } }
                : null;

        /// <summary>
        /// The folk's answer to the player picking reply <paramref name="option"/> of <paramref name="shown"/>: its
        /// then topic asked (one deeper; at MaxDepth the answer offers only the defaults), or, with no then, an answer
        /// with null Text (back to the topic list). Chosen is the reply, and for accept or decline ChosenOffer is the
        /// offer it was about; Ends for "end" (after its then's answer, if any). An index out of range answers null Text and nothing chosen.
        /// </summary>
        public static Answer Choose(in FolkTalker who, in Answer shown, int option)
        {
            if (shown.Options == null || option < 0 || option >= shown.Options.Length)
                return new Answer { Topic = shown.Topic, Depth = shown.Depth };
            var o = shown.Options[option];
            var next = o.Then != null ? Ask(who, o.Then) : new Answer { Topic = shown.Topic };
            next.Depth = shown.Depth + 1;
            next.Chosen = o;
            if (o.Do == OptionDo.Accept || o.Do == OptionDo.Decline)
                next.ChosenOffer = shown.Offer;
            if (o.Do == OptionDo.End)
            {
                next.Ends = true;
                next.Options = null;
            }
            if (o.Do == OptionDo.Gesture && next.Gesture == null)
            {
                next.Gesture = o.Gesture;
                next.Action = o.Action;
            }
            if (next.Depth >= MaxDepth && next.Options != null)
                next.Options = DefaultOptions(next.Offer);
            return next;
        }

        /// <summary>The replies Ask shows with a line: those whose conditions fit and whose slots fill, else the
        /// defaults for its offer. Null for none.</summary>
        static Option[] OptionsOf(TalkLine line, DialogueArchive archive, DialogueArchive.Speaker speaker, in Context ctx,
                                  ref Unity.Mathematics.Random rng, Offer offer)
        {
            if (line?.Options != null)
            {
                var shown = new List<Option>(line.Options.Length);
                foreach (var t in line.Options)
                {
                    if (!Fits(t.Line, ctx))
                        continue;
                    var say = Fill(t.Line, archive, speaker, ctx, ref rng);
                    if (say == null || say.IndexOf('{') >= 0)
                        continue;
                    var o = new Option { Say = say, Then = t.Then, Do = t.Do, Gesture = t.Gesture };
                    if (o.Gesture != null && Enum.TryParse(o.Gesture, true, out IdleAction a))
                        o.Action = a;
                    shown.Add(o);
                }
                if (shown.Count > 0)
                    return shown.ToArray();
            }
            return DefaultOptions(offer);
        }

        /// <summary>A line's "options" read: 1-4 objects with "say", maybe "then" (a topic id, checked once the book is
        /// read), maybe "do" (accept, decline, end, gesture:&lt;emote&gt;; accept and decline need a quest or item offer),
        /// and line condition keys. Each bad one is a named problem.</summary>
        static TalkOption[] ParseOptions(JObject o, TalkLine line, JsonProblems problems, List<(JToken, string)> thens)
        {
            var t = o["options"];
            if (t == null)
                return null;
            if (!(t is JArray list))
            {
                problems.Add(t, $"\"options\" should be a list of replies, not {JsonProblems.Kind(t)}");
                return null;
            }
            if (list.Count < 1 || list.Count > MaxOptions)
                problems.Add(t, $"\"options\" has {list.Count} replies: a line takes 1 to {MaxOptions}");
            var options = new List<TalkOption>();
            foreach (var e in list)
            {
                if (!(e is JObject eo))
                {
                    problems.Add(e, $"a reply should be an object with \"say\", not {JsonProblems.Kind(e)}");
                    continue;
                }
                foreach (var key in eo)
                    if (!key.Key.StartsWith("_") && !OptionKeys.Contains(key.Key) && FolkPersona.TraitIndex(key.Key) < 0)
                        problems.Add(key.Value, $"unknown key \"{key.Key}\" on a reply");
                var say = problems.Str(eo, "say");
                if (say == null)
                {
                    if (eo["say"] == null)
                        problems.Add(eo, "a reply has no \"say\"");
                    continue;
                }
                if (say.Trim().Length == 0)
                    problems.Add(eo["say"], "\"say\" is empty");
                else
                    problems.Slots(eo["say"], say);
                var opt = new TalkOption { Line = new TalkLine { Text = say }, Then = problems.Str(eo, "then")?.ToLowerInvariant() };
                ParseConditions(eo, opt.Line, problems);
                var d = problems.Str(eo, "do");
                if (d != null)
                {
                    if (d.StartsWith("gesture:", StringComparison.OrdinalIgnoreCase))
                    {
                        opt.Do = OptionDo.Gesture;
                        opt.Gesture = d.Substring("gesture:".Length).Trim();
                        if (!Enum.TryParse(opt.Gesture, true, out IdleAction _))
                            problems.Add(eo["do"], $"\"do\": no gesture called \"{opt.Gesture}\"");
                    }
                    else if (Enum.TryParse(d, true, out OptionDo k) && k != OptionDo.None && k != OptionDo.Gesture)
                        opt.Do = k;
                    else
                        problems.Add(eo["do"], $"\"do\": \"{d}\" isn't accept, decline, end or gesture:<emote>");
                    if ((opt.Do == OptionDo.Accept || opt.Do == OptionDo.Decline) && line.OfferKind != OfferKind.Quest && line.OfferKind != OfferKind.Item)
                        problems.Add(eo["do"], $"\"do\": \"{d}\" needs the line to offer a quest or an item");
                }
                if (opt.Then != null)
                    thens?.Add((eo["then"], opt.Then));
                options.Add(opt);
            }
            return options.Count > 0 ? options.ToArray() : null;
        }

        /// <summary>A problem for each reply whose then names a topic with no lines in any culture of the book (the
        /// code's own topics always answer).</summary>
        static void CheckThens(TalkBook book, List<(JToken At, string Topic)> thens, JsonProblems problems)
        {
            foreach (var (at, topic) in thens)
                if (!book.Has(topic) && topic != Meet && !Array.Exists(AllTopics, x => x.Id == topic))
                    problems.Add(at, $"\"then\": no lines for topic \"{topic}\"");
        }
    }
}
