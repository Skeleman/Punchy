using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// What folk say, read from StreamingAssets/dialogue.json (outside the build, edited by hand, read again whenever
    /// the file changes, LiveJsonFile). Each culture (a species' name, "Human", "Dwarf", "HumanChild", then the species it's based on,
    /// then "Any") has word lists and lines; a line's {slots} are filled from the speaker (its culture's words, its
    /// trade, its home town, the time of day, the player's body). The format is written up in HANDOFF-animal.md.
    /// Local only: each machine picks its own lines, nothing is sent.
    /// </summary>
    public class DialogueArchive
    {
        public const string FileName = "dialogue.json";
        public const string Any = "Any";

        public class Line
        {
            public string Text;
            /// <summary>"day", "night", "dawn", "dusk", "morning", "evening", or null for any time.</summary>
            public string When;
            /// <summary>Only from folk with one of these trades (CreatureTask names), or null for anyone.</summary>
            public string[] Trades;
            public float Weight = 1f;
            /// <summary>An answer to one of these emotes (Wave, Bow, Cheer, Dance), never said walking by; or null.</summary>
            public string[] Emotes;
            /// <summary>Only from folk taking the player this way (FolkStance names, as written: Kin, Rival, Wary, Annoyed,
            /// Warm): the wary and annoyed say only these, the others these half the time; or null (anyone else).</summary>
            public string[] Stances;
            /// <summary>Only from folk of one of these tempers (FolkMood names: Cheerful, Plain, Grumpy), or null for any (A-29).</summary>
            public string[] Moods;
            /// <summary>"state": "dry" or "!dry": only from a sea folk out of the water, or only from one in it (A-103 row
            /// 2b); null for either.</summary>
            public bool? Dry;
            /// <summary>Kept for later (dialogue options): the raw object, so new keys need no format change.</summary>
            public JObject Raw;
        }

        public class Culture
        {
            public readonly Dictionary<string, string[]> Words = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            public readonly List<Line> Lines = new List<Line>();
        }

        /// <summary>Who's speaking, for filling slots and choosing lines.</summary>
        public struct Speaker
        {
            public string Species, BodySpecies, Trade, HomeTown, PlayerSpecies;
            /// <summary>How the speaker takes the player's body (a FolkStance name), or null for Neutral.</summary>
            public string Stance;
            /// <summary>Its job (a FolkJob name, "Baker") and temper (a FolkMood name), or null (A-29).</summary>
            public string Job, Mood;
            public float Hour;
            /// <summary>A water breather out on dry land (FolkState.OutOfWater, A-103 row 2b).</summary>
            public bool Dry;
        }

        public readonly Dictionary<string, Culture> Cultures = new Dictionary<string, Culture>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The "conversation" section (FolkConversation, A-93), or null when the file has none.</summary>
        public FolkConversation.TalkBook Talk;
        /// <summary>The "beastTalk" section: what folk say and do to an animal player (A-93 row 3), or null.</summary>
        public FolkConversation.TalkBook BeastTalk;

        static readonly LiveJsonFile<DialogueArchive> file =
            new LiveJsonFile<DialogueArchive>(FileName, "Dialogue", Parse, a => $"{a.Count} lines, {a.Talk?.Count ?? 0} talk lines");

        /// <summary>Where it's read from (JsonFiles.PathOf: beside the exe in a build, if there's a copy there).</summary>
        public static string Path => file.Path;

        /// <summary>Why the newest dialogue.json was turned down (each bad entry named), or null (A-129).</summary>
        public static string Error => file.Error;

        /// <summary>Read dialogue.json again now, changed or not.</summary>
        public static void Reload() => file.Reload();

        /// <summary>The archive, read again if the file has changed (checked once a second); null if there's none. A
        /// version with a bad entry is turned down whole and the last good one kept (LiveJsonFile).</summary>
        public static DialogueArchive Current => file.Current;

        public int Count
        {
            get
            {
                var n = 0;
                foreach (var c in Cultures.Values)
                    n += c.Lines.Count;
                return n;
            }
        }

        /// <summary>Reads the archive (throws only on broken JSON); bad entries go in problems, none if null.</summary>
        public static DialogueArchive Parse(string json, JsonProblems problems = null)
        {
            problems ??= new JsonProblems();
            var root = JObject.Parse(json);
            var archive = new DialogueArchive();
            // (The talk window's lines, A-93: FolkConversation reads them.)
            archive.Talk = FolkConversation.Parse(root["conversation"], problems);
            archive.BeastTalk = FolkConversation.Parse(root["beastTalk"], problems);
            var cultures = root["cultures"];
            if (cultures != null && !(cultures is JObject))
                problems.Add(cultures, "\"cultures\" should be an object of culture name: { words, lines }");
            if (cultures is JObject all)
                foreach (var pair in all)
                {
                    if (pair.Key.StartsWith("_"))
                        continue;
                    if (!(pair.Value is JObject c))
                    {
                        problems.Add(pair.Value, "a culture should be an object with \"words\" and \"lines\"");
                        continue;
                    }
                    var culture = new Culture();
                    var words = c["words"];
                    if (words is JObject w)
                    {
                        foreach (var list in w)
                            if (!list.Key.StartsWith("_"))
                            {
                                var options = problems.Strings(list.Value, list.Key) ?? Array.Empty<string>();
                                foreach (var word in options)
                                    problems.Slots(list.Value, word);
                                culture.Words[list.Key] = options;
                            }
                    }
                    else if (words != null)
                        problems.Add(words, "\"words\" should be an object of list name: [ words ]");
                    var lines = c["lines"];
                    if (lines is JArray arr)
                    {
                        foreach (var l in arr)
                        {
                            var line = ParseLine(l, problems);
                            if (line != null)
                                culture.Lines.Add(line);
                        }
                    }
                    else if (lines != null)
                        problems.Add(lines, "\"lines\" should be a list");
                    archive.Cultures[pair.Key] = culture;
                }
            return archive;
        }

        /// <summary>The "when" words a culture line can have (InHours).</summary>
        static readonly string[] Whens = { "day", "night", "dawn", "morning", "afternoon", "evening", "dusk" };

        /// <summary>One culture line, or null (and a problem) if it isn't a string or an object with text.</summary>
        static Line ParseLine(JToken l, JsonProblems problems)
        {
            if (l.Type == JTokenType.String)
            {
                problems.Slots(l, (string)l);
                return new Line { Text = (string)l };
            }
            if (!(l is JObject o))
            {
                problems.Add(l, $"a line should be text or an object with \"text\", not {JsonProblems.Kind(l)}");
                return null;
            }
            var text = problems.Text(o);
            if (text == null)
                return null;
            var when = problems.Str(o, "when");
            problems.OneOf(o, "when", when, Whens);
            var state = problems.Str(o, "state");
            problems.OneOf(o, "state", state, "dry", "!dry");
            return new Line
            {
                Text = text,
                When = when,
                Trades = problems.Strings(o["trade"], "trade"),
                Emotes = problems.Strings(o["emote"], "emote"),
                Stances = problems.Strings(o["stance"], "stance"),
                Moods = problems.Strings(o["mood"], "mood"),
                Dry = DryOf(o["state"]),
                Weight = problems.Num(o, "weight", 1f),
                Raw = o
            };
        }

        /// <summary>A culture line's "state": "dry" (true), "!dry" (false), anything else or none (null).</summary>
        static bool? DryOf(JToken t)
        {
            var s = t != null && t.Type == JTokenType.String ? ((string)t).Trim() : null;
            return string.Equals(s, "dry", StringComparison.OrdinalIgnoreCase) ? true
                : string.Equals(s, "!dry", StringComparison.OrdinalIgnoreCase) ? false : (bool?)null;
        }

        /// <summary>The cultures a speaker draws on, most particular first: its species, the one it's based on, Any.</summary>
        IEnumerable<Culture> Chain(Speaker who)
        {
            if (who.Species != null && Cultures.TryGetValue(who.Species, out var own))
                yield return own;
            if (who.BodySpecies != null && who.BodySpecies != who.Species && Cultures.TryGetValue(who.BodySpecies, out var body))
                yield return body;
            if (Cultures.TryGetValue(Any, out var any))
                yield return any;
        }

        /// <summary>
        /// A line for this speaker, slots filled, or null if it has nothing to say: from its own culture's lines (the
        /// first in its chain that has any) and Any's, those whose time and trade fit and whose slots can all be filled.
        /// Answering an emote ("Wave"), only lines for that emote; with none anywhere in its chain, an ordinary line.
        /// </summary>
        public string Pick(Speaker who, ref Unity.Mathematics.Random rng, string emote = null)
        {
            // Taking the player some way (Z-52): the wary and annoyed say only a line for that (for this emote, or any)
            // or nothing; kin, the warm and rivals one half the time, and their usual lines otherwise.
            if (who.Stance != null)
            {
                var only = who.Stance == nameof(FolkStance.Wary) || who.Stance == nameof(FolkStance.Annoyed);
                if (only || rng.NextFloat() < 0.5f)
                {
                    var stanced = Pick(who, ref rng, emote, emote != null, true);
                    if (stanced != null || only)
                        return stanced;
                }
            }
            if (emote != null)
            {
                var answer = Pick(who, ref rng, emote, true, false);
                return answer ?? Pick(who, ref rng, null, false, false);
            }
            // A folk with a job talks shop most of the time (A-29).
            if (who.Job != null && rng.NextFloat() < RoleShare)
            {
                var role = PickRole(who, ref rng);
                if (role != null)
                    return role;
            }
            return Pick(who, ref rng, null, false, false);
        }

        const float RoleShare = 0.7f;

        /// <summary>
        /// A line from the speaker's job pool (lines whose "trade" names its job, from every culture in its chain), slots
        /// filled, or null if its job has none. Flavoured: its own culture's lines for the job and lines for its temper
        /// ("mood") come up three times as often as Any's plain ones. Used for folk-to-folk talk too (A-29).
        /// </summary>
        public string PickRole(Speaker who, ref Unity.Mathematics.Random rng)
        {
            if (who.Job == null)
                return null;
            var candidates = new List<Line>();
            var weights = new List<float>();
            Cultures.TryGetValue(Any, out var any);
            foreach (var c in Chain(who))
                foreach (var line in c.Lines)
                    if (line.Trades != null && Array.IndexOf(line.Trades, who.Job) >= 0 && line.Stances == null && line.Emotes == null && Fits(line, who))
                    {
                        candidates.Add(line);
                        weights.Add(Mathf.Max(line.Weight, 0f) * (c != any ? 3f : 1f) * (line.Moods != null ? 3f : 1f));
                    }
            for (int tries = 0; tries < 6 && candidates.Count > 0; tries++)
            {
                var total = 0f;
                foreach (var w in weights)
                    total += w;
                var at = rng.NextFloat(total);
                var k = candidates.Count - 1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    at -= weights[i];
                    if (at < 0f)
                    {
                        k = i;
                        break;
                    }
                }
                var text = Fill(candidates[k].Text, who, ref rng, 0);
                if (text != null)
                    return text;
                candidates.RemoveAt(k);
                weights.RemoveAt(k);
            }
            return null;
        }

        string Pick(Speaker who, ref Unity.Mathematics.Random rng, string emote, bool answering, bool stanced)
        {
            var candidates = new List<Line>();
            var shared = new HashSet<Line>();
            var own = false;
            foreach (var c in Chain(who))
            {
                var isAny = Cultures.TryGetValue(Any, out var any) && c == any;
                if (own && !isAny)
                    continue;
                foreach (var line in c.Lines)
                    if (Fits(line, who) && (stanced
                            ? line.Stances != null && Array.IndexOf(line.Stances, who.Stance) >= 0 &&
                              (line.Emotes == null || answering && Array.IndexOf(line.Emotes, emote) >= 0)
                            : line.Stances == null && (answering ? line.Emotes != null && Array.IndexOf(line.Emotes, emote) >= 0 : line.Emotes == null)))
                    {
                        candidates.Add(line);
                        own |= !isAny;
                        if (isAny)
                            shared.Add(line);
                    }
            }
            // A few tries: a line with a slot that can't be filled (no home town, out on the road) is passed over.
            for (int tries = 0; tries < 6 && candidates.Count > 0; tries++)
            {
                var total = 0f;
                foreach (var l in candidates)
                    total += Weight(l, own, shared);
                var at = rng.NextFloat(total);
                var chosen = candidates[candidates.Count - 1];
                foreach (var l in candidates)
                {
                    at -= Weight(l, own, shared);
                    if (at < 0f)
                    {
                        chosen = l;
                        break;
                    }
                }
                var text = Fill(chosen.Text, who, ref rng, 0);
                if (text != null)
                    return text;
                candidates.Remove(chosen);
            }
            return null;
        }

        /// <summary>A line's chance; Any's lines come up less for a culture with lines of its own, so folk sound like themselves.</summary>
        static float Weight(Line line, bool own, HashSet<Line> shared) =>
            Mathf.Max(line.Weight, 0f) * (own && shared.Contains(line) ? AnyShare : 1f) * (line.Dry == true ? DryShare : 1f);
        const float AnyShare = 0.3f;
        /// <summary>A sea folk out of the water mostly grumbles about it (a dry line only fits one that is).</summary>
        const float DryShare = 2.5f;

        static bool Fits(Line line, Speaker who)
        {
            if (line.Trades != null && line.Trades.Length > 0 && Array.IndexOf(line.Trades, who.Trade) < 0 &&
                (who.Job == null || Array.IndexOf(line.Trades, who.Job) < 0))
                return false;
            if (line.Moods != null && line.Moods.Length > 0 && Array.IndexOf(line.Moods, who.Mood) < 0)
                return false;
            if (line.Dry.HasValue && line.Dry.Value != who.Dry)
                return false;
            return string.IsNullOrEmpty(line.When) || InHours(line.When, who.Hour);
        }

        /// <summary>Whether hour h is in a line's "when" (day, night, dawn, morning, afternoon, evening/dusk; anything else: always).</summary>
        internal static bool InHours(string when, float h)
        {
            switch (when.ToLowerInvariant())
            {
                case "day": return h >= 6f && h < 18f;
                case "night": return h >= 20f || h < 5f;
                case "dawn": return h >= 5f && h < 8f;
                case "morning": return h >= 5f && h < 12f;
                case "afternoon": return h >= 12f && h < 17f;
                case "evening": case "dusk": return h >= 17f && h < 21f;
                default: return true;
            }
        }

        /// <summary>
        /// Fills a text's {slots}: built-in ones ({trade}, {home_town}, {species}, {player}, {time_of_day}), then any
        /// word list from the speaker's chain (words can hold slots of their own). Null if a slot can't be filled.
        /// </summary>
        public string Fill(string text, Speaker who, ref Unity.Mathematics.Random rng, int depth)
        {
            if (text == null || depth > 4)
                return text;
            var sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                var close = text[i] == '{' ? text.IndexOf('}', i + 1) : -1;
                if (close < 0)
                {
                    sb.Append(text[i]);
                    continue;
                }
                var slot = text.Substring(i + 1, close - i - 1).Trim();
                var value = Slot(slot, who, ref rng, depth);
                if (value == null)
                    return null;
                sb.Append(value);
                i = close;
            }
            var s = sb.ToString();
            // (A line starting with a filled-in word gets its capital: "{trade}s are..." -> "Smiths are...".)
            return s.Length > 0 && char.IsLower(s[0]) && text.StartsWith("{") ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s;
        }

        string Slot(string slot, Speaker who, ref Unity.Mathematics.Random rng, int depth)
        {
            // A word list wins over a built-in, so a culture can say its own word for a trade or the time of day.
            var key = slot;
            switch (slot.ToLowerInvariant())
            {
                case "trade":
                    // (Its job's word first, "trade.Baker", then its task's, then the job's own name.)
                    if (who.Job != null)
                        foreach (var c in Chain(who))
                            if (c.Words.TryGetValue("trade." + who.Job, out var jobWords) && jobWords.Length > 0)
                                return Fill(jobWords[rng.NextInt(jobWords.Length)], who, ref rng, depth + 1);
                    key = who.Trade != null ? "trade." + who.Trade : null;
                    break;
                case "time_of_day": key = "time." + TimeOfDay(who.Hour); break;
            }
            if (key != null)
                foreach (var c in Chain(who))
                    if (c.Words.TryGetValue(key, out var options) && options.Length > 0)
                        return Fill(options[rng.NextInt(options.Length)], who, ref rng, depth + 1);
            switch (slot.ToLowerInvariant())
            {
                case "trade": return who.Job != null ? Spoken(who.Job) : null;
                case "home_town": return who.HomeTown;
                case "species": return Spoken(who.BodySpecies ?? who.Species);
                case "player": return who.PlayerSpecies;
                case "time_of_day": return TimeOfDay(who.Hour);
                default: return null;
            }
        }

        static string TimeOfDay(float h) => h >= 5f && h < 12f ? "morning" : h >= 12f && h < 17f ? "afternoon" : h >= 17f && h < 21f ? "evening" : "night";

        /// <summary>"HumanChild" as said: "human child".</summary>
        public static string Spoken(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    sb.Append(' ');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// A town's name, the same on every machine (from where it stands): two halves from its folk's "town_start" and
        /// "town_end" word lists ("Ash" + "ford"), or null if their culture has none.
        /// </summary>
        public string TownName(string culture, Unity.Mathematics.float2 centre)
        {
            var who = new Speaker { Species = culture, BodySpecies = culture };
            string[] starts = null, ends = null;
            foreach (var c in Chain(who))
            {
                if (starts == null && c.Words.TryGetValue("town_start", out var s) && s.Length > 0)
                    starts = s;
                if (ends == null && c.Words.TryGetValue("town_end", out var e) && e.Length > 0)
                    ends = e;
            }
            if (starts == null || ends == null)
                return null;
            var h = Unity.Mathematics.math.hash(new Unity.Mathematics.int2((int)Unity.Mathematics.math.floor(centre.x), (int)Unity.Mathematics.math.floor(centre.y)));
            return starts[(h >> 4) % (uint)starts.Length] + ends[(h >> 16) % (uint)ends.Length];
        }
    }
}
