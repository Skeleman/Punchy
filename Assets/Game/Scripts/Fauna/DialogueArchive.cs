using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// What folk say, read from StreamingAssets/dialogue.json (outside the build, edited by hand, read again whenever
    /// the file changes). Each culture (a species' name, "Human", "Dwarf", "HumanChild", then the species it's based on,
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
            public float Hour;
        }

        public readonly Dictionary<string, Culture> Cultures = new Dictionary<string, Culture>(StringComparer.OrdinalIgnoreCase);

        static DialogueArchive loaded;
        static DateTime loadedTime;
        static float nextCheck;
        /// <summary>The last parse error (shown once in the log), or null.</summary>
        public static string Error;

        public static string Path => System.IO.Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>The archive, read again if the file has changed (checked once a second); null if there's none.</summary>
        public static DialogueArchive Current
        {
            get
            {
                if (loaded == null || Time.realtimeSinceStartup >= nextCheck)
                {
                    nextCheck = Time.realtimeSinceStartup + 1f;
                    try
                    {
                        var path = Path;
                        if (File.Exists(path))
                        {
                            var time = File.GetLastWriteTimeUtc(path);
                            if (loaded == null || time != loadedTime)
                            {
                                loadedTime = time;
                                var parsed = Parse(File.ReadAllText(path));
                                loaded = parsed;
                                Error = null;
                                Debug.Log($"Dialogue: read {path} ({parsed.Count} lines)");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // (A typo mid-edit: keep the last good archive, and say what's wrong once.)
                        if (Error != e.Message)
                            Debug.LogWarning($"Dialogue: couldn't read {FileName}, keeping the last good one: {e.Message}");
                        Error = e.Message;
                    }
                }
                return loaded;
            }
        }

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

        public static DialogueArchive Parse(string json)
        {
            var root = JObject.Parse(json);
            var archive = new DialogueArchive();
            if (root["cultures"] is JObject cultures)
                foreach (var pair in cultures)
                {
                    if (!(pair.Value is JObject c))
                        continue;
                    var culture = new Culture();
                    if (c["words"] is JObject words)
                        foreach (var w in words)
                            culture.Words[w.Key] = Strings(w.Value);
                    if (c["lines"] is JArray lines)
                        foreach (var l in lines)
                        {
                            if (l.Type == JTokenType.String)
                                culture.Lines.Add(new Line { Text = (string)l });
                            else if (l is JObject o && o["text"] != null)
                                culture.Lines.Add(new Line
                                {
                                    Text = (string)o["text"],
                                    When = (string)o["when"],
                                    Trades = o["trade"] != null ? Strings(o["trade"]) : null,
                                    Emotes = o["emote"] != null ? Strings(o["emote"]) : null,
                                    Weight = o["weight"] != null ? (float)o["weight"] : 1f,
                                    Raw = o
                                });
                        }
                    archive.Cultures[pair.Key] = culture;
                }
            return archive;
        }

        static string[] Strings(JToken t) =>
            t is JArray a ? new List<string>(a.Values<string>()).ToArray() : t.Type == JTokenType.String ? new[] { (string)t } : Array.Empty<string>();

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
            if (emote != null)
            {
                var answer = Pick(who, ref rng, emote, true);
                return answer ?? Pick(who, ref rng, null, false);
            }
            return Pick(who, ref rng, null, false);
        }

        string Pick(Speaker who, ref Unity.Mathematics.Random rng, string emote, bool answering)
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
                    if (Fits(line, who) && (answering ? line.Emotes != null && Array.IndexOf(line.Emotes, emote) >= 0 : line.Emotes == null))
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
        static float Weight(Line line, bool own, HashSet<Line> shared) => Mathf.Max(line.Weight, 0f) * (own && shared.Contains(line) ? AnyShare : 1f);
        const float AnyShare = 0.3f;

        static bool Fits(Line line, Speaker who)
        {
            if (line.Trades != null && line.Trades.Length > 0 && Array.IndexOf(line.Trades, who.Trade) < 0)
                return false;
            if (string.IsNullOrEmpty(line.When))
                return true;
            var h = who.Hour;
            switch (line.When.ToLowerInvariant())
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
                case "trade": key = who.Trade != null ? "trade." + who.Trade : null; break;
                case "time_of_day": key = "time." + TimeOfDay(who.Hour); break;
            }
            if (key != null)
                foreach (var c in Chain(who))
                    if (c.Words.TryGetValue(key, out var options) && options.Length > 0)
                        return Fill(options[rng.NextInt(options.Length)], who, ref rng, depth + 1);
            switch (slot.ToLowerInvariant())
            {
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
