using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>Something the player can say to a folk: its id (FolkConversation.Who...) and what the menu shows.</summary>
    public struct Topic
    {
        public string Id, Label;
        public override string ToString() => $"{Id} \"{Label}\"";
    }

    /// <summary>What an answer offers: a quest to take (Id: the quest's id in quests.json), an item (Id: its name in the
    /// item table), directions (Id: the place), a rumour, a discount, or a refusal.</summary>
    public enum OfferKind : byte { None, Quest, Item, Directions, Rumour, Discount, Refuses }

    public struct Offer
    {
        public OfferKind Kind;
        public string Id;
        public override string ToString() => Kind == OfferKind.None ? "-" : Id != null ? $"{Kind}:{Id}" : Kind.ToString();
    }

    /// <summary>A folk's answer: the words, a gesture to make while saying them (an IdleAction's name, or another word the
    /// talk window can show; Action is it parsed, None when it isn't an IdleAction), what it offers, and whether it ends
    /// the talk.</summary>
    public struct Answer
    {
        public string Topic, Text, Gesture;
        public IdleAction Action;
        public Offer Offer;
        public bool Ends;
        public override string ToString() => $"[{Topic}] \"{Text}\"" + (Gesture != null ? $" <{Gesture}>" : "") + (Offer.Kind != OfferKind.None ? $" offer {Offer}" : "") + (Ends ? " (ends)" : "");
    }

    /// <summary>Everything an answer is built from, gathered from a folk entity (FolkConversation.TryTalker) or made up
    /// directly (tests, the text dump).</summary>
    public struct FolkTalker
    {
        /// <summary>Its species' name and the body it's built on (its cultures, most particular first).</summary>
        public string Species, BodySpecies;
        /// <summary>Its job's name (FolkJobs.Name) or null; its task's name when the task speaks for itself (a smith).</summary>
        public string Job, Trade;
        /// <summary>The settlement it lives in, or null (a traveller, a camper, out in the wild).</summary>
        public string HomeTown;
        /// <summary>The player's body as said ("wolf"), null when it's the folk's own kind.</summary>
        public string PlayerSpecies;
        /// <summary>The player is in an animal's body (not upright with hands): it gets the beast talk (A-93 row 3).</summary>
        public bool Beast;
        public uint Individual;
        public FolkPersona Persona;
        public FolkState State;
        /// <summary>How it takes the player's body (CreatureBlob.StanceToward).</summary>
        public FolkStance Stance;
    }

    /// <summary>
    /// Talking with a folk (A-93): the topics the player can raise and its answer to each, picked from dialogue.json's
    /// "conversation" section by its culture, its persona (FolkPersona: trait bands), its condition (FolkState: tired,
    /// busy, hungry, scared, the weather), its job and how it takes the player's body. Built only when asked, nothing
    /// runs per frame. Local: each machine picks its own words; a quest or item it offers is the asker's to take through
    /// the usual host calls (the talk window, P-26). The "asked too often" ledger is this machine's.
    /// </summary>
    public static class FolkConversation
    {
        public const string Who = "who", How = "how", Work = "work", Town = "town", News = "news", Favour = "favour", Help = "help", Bye = "bye";
        /// <summary>An animal player's one topic: going up to the folk (A-93 row 3).</summary>
        public const string Meet = "meet";

        /// <summary>The topics in menu order, with their labels (dialogue.json's conversation.topics can relabel them).</summary>
        public static readonly (string Id, string Label)[] AllTopics =
        {
            (Who, "Who are you?"),
            (How, "How are you?"),
            (Work, "What do you do?"),
            (Town, "Tell me about this town."),
            (News, "Heard any news?"),
            (Favour, "Can you help me?"),
            (Help, "Do you need anything?"),
            (Bye, "Goodbye."),
        };
        static readonly (string Id, string Label)[] BeastTopics = { (Meet, "(go up to them)"), (Bye, "(wander off)") };

        static FaunaDirector director;

        // ---- From a folk entity ----

        /// <summary>The topics the player can raise with this folk, the player in playerBody (null: the player's body now).
        /// Empty when it isn't a folk.</summary>
        public static Topic[] Topics(Entity folk, CreatureSpecies playerBody = null) =>
            TryTalker(World.DefaultGameObjectInjectionWorld, folk, playerBody, out var who) ? Topics(who) : Array.Empty<Topic>();

        /// <summary>This folk's answer to a topic, the player in playerBody (null: the player's body now). Text null when it
        /// isn't a folk.</summary>
        public static Answer Ask(Entity folk, string topic, CreatureSpecies playerBody = null) =>
            TryTalker(World.DefaultGameObjectInjectionWorld, folk, playerBody, out var who) ? Ask(who, topic) : new Answer { Topic = topic };

        /// <summary>Gathers what an answer is built from for a folk entity (false when it isn't one: no hands, not upright, a
        /// ghost, gone).</summary>
        public static bool TryTalker(World world, Entity folk, CreatureSpecies playerBody, out FolkTalker who)
        {
            who = default;
            if (world == null || !world.IsCreated)
                return false;
            var em = world.EntityManager;
            if (!em.Exists(folk) || !em.HasComponent<CreatureState>(folk) || !em.HasComponent<CreatureBody>(folk))
                return false;
            var s = em.GetComponentData<CreatureState>(folk);
            var body = em.GetComponentData<CreatureBody>(folk);
            if (!body.Blob.IsCreated)
                return false;
            ref var b = ref body.Blob.Value;
            if (!b.Upright || !b.Hands || b.Haunts)
                return false;
            if (director == null)
                director = UnityEngine.Object.FindAnyObjectByType<FaunaDirector>();
            var species = director != null ? director.SpeciesAt(body.Species) : null;
            if (playerBody == null && director != null)
                playerBody = director.PlayerSpecies;
            var playerIndex = -1;
            if (director != null && playerBody != null)
                for (int i = 0; i < director.SpeciesCount; i++)
                    if (director.SpeciesAt(i) == playerBody)
                        playerIndex = i;
            var beast = false;
            if (playerBody != null)
            {
                var pb = playerIndex >= 0 ? director.BlobAt(playerIndex) : default;
                beast = pb.IsCreated ? !(pb.Value.Upright && pb.Value.Hands) : !playerBody.Body.Upright;
            }

            float hour = 12f, night = 0f;
            using (var cq = em.CreateEntityQuery(ComponentType.ReadOnly<FaunaClock>()))
                if (cq.TryGetSingleton<FaunaClock>(out var clock))
                {
                    hour = clock.Hour;
                    night = clock.Night;
                }
            string town = null;
            if (s.Task != CreatureTask.Travel && s.Task != CreatureTask.Camper)
                using (var tq = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>()))
                    if (tq.TryGetSingleton<TerrainSource>(out var terrain))
                        town = world.GetExistingSystemManaged<FolkTalkSystem>()?.TownOf(s, terrain);

            var hasMind = em.HasComponent<CreatureMind>(folk);
            var mind = hasMind ? em.GetComponentData<CreatureMind>(folk) : default;
            var job = FolkJobs.Of(s);
            var playerKind = playerBody != null ? playerBody.Body.name : null;
            who = new FolkTalker
            {
                Species = species != null ? species.name : null,
                BodySpecies = species != null ? species.Body.name : null,
                Job = job == FolkJob.None ? null : FolkJobs.Name(job),
                Trade = s.Task != CreatureTask.None && s.Job == 0 ? s.Task.ToString() : null,
                HomeTown = town,
                PlayerSpecies = playerBody == null || species != null && playerKind == species.Body.name ? null : DialogueArchive.Spoken(playerBody.name),
                Beast = beast,
                Individual = s.Individual,
                Persona = FolkPersona.Of(s.Individual),
                State = FolkState.Of(s, ref b, hasMind, mind, hour, night),
                Stance = b.StanceToward(playerIndex, s.Individual),
            };
            return true;
        }

        // ---- From a FolkTalker ----

        /// <summary>The topics for this talker: all of them to a person (with any topic dialogue.json adds, just before
        /// goodbye); to an animal, going up to it and wandering off.</summary>
        public static Topic[] Topics(in FolkTalker who)
        {
            var labels = DialogueArchive.Current?.Talk?.Labels;
            var topics = new List<Topic>();
            foreach (var (id, label) in who.Beast ? BeastTopics : AllTopics)
            {
                // (A topic that's only in the file goes in ahead of goodbye.)
                if (id == Bye && !who.Beast && labels != null)
                    foreach (var extra in labels)
                        if (IsExtra(extra.Key) && !string.IsNullOrEmpty(extra.Value))
                            topics.Add(new Topic { Id = extra.Key, Label = extra.Value });
                topics.Add(new Topic { Id = id, Label = labels != null && labels.TryGetValue(id, out var l) && !string.IsNullOrEmpty(l) ? l : label });
            }
            return topics.ToArray();
        }

        /// <summary>A topic dialogue.json adds that the code doesn't know (not one of AllTopics, not meet).</summary>
        static bool IsExtra(string id) => id != Meet && !Array.Exists(AllTopics, t => t.Id == id);

        static uint asks;

        /// <summary>This talker's answer to a topic (an unknown topic is answered as "who"). Never null text.</summary>
        public static Answer Ask(in FolkTalker who, string topic)
        {
            topic = string.IsNullOrEmpty(topic) ? Who : topic.ToLowerInvariant();
            if (who.Beast && topic != Bye)
                topic = Meet;
            var rng = new Unity.Mathematics.Random(BiomeSampling.Scramble(who.Individual ^ Hash(topic) ^ (++asks * 0x9E3779B9u)) | 1u);
            var ctx = new Context
            {
                Who = who,
                Mood = TalkMood(who),
                Willing = Willing(who, out var pestered),
                Pestered = pestered,
                Quest = QuestFor(who.Individual),
            };
            if (topic == Favour)
                Ledger(who.Individual, true);
            var archive = DialogueArchive.Current;
            var speaker = new DialogueArchive.Speaker
            {
                Species = who.Species,
                BodySpecies = who.BodySpecies,
                Trade = who.Trade,
                Job = who.Job,
                HomeTown = who.HomeTown,
                PlayerSpecies = who.PlayerSpecies,
                Stance = who.Stance == FolkStance.Neutral ? null : who.Stance.ToString(),
                Mood = ctx.Mood,
                Hour = who.State.Hour,
            };
            TalkLine line = null;
            string text = null;
            var known = Array.Exists(AllTopics, t => t.Id == topic) || topic == Meet || archive?.Talk != null && archive.Talk.Labels.ContainsKey(topic);
            if (!known)
                topic = Who;
            if (archive?.Talk != null)
                text = Choose(archive.Talk, archive, speaker, topic, ctx, ref rng, out line);
            if (text == null)
                text = Choose(Defaults, archive, speaker, topic, ctx, ref rng, out line);
            if (text == null)
            {
                line = null;
                text = "...";
            }
            var answer = new Answer { Topic = topic, Text = text, Gesture = line?.Gesture, Ends = topic == Bye || line != null && line.Ends };
            if (answer.Gesture != null && Enum.TryParse(answer.Gesture, true, out IdleAction action))
                answer.Action = action;
            answer.Offer = OfferOf(line, ctx);
            return answer;
        }

        // ---- Choosing a line ----

        /// <summary>What the lines are matched against, worked out once per answer.</summary>
        struct Context
        {
            public FolkTalker Who;
            public string Mood;
            public bool Willing, Pestered;
            public string Quest;
        }

        /// <summary>
        /// A topic's line from a book for this talker, slots filled, or null: every fitting line of its cultures (its
        /// species, its body's, Any; Any's lines count less when its own culture has some), a line that matches its
        /// condition (tired, hungry, the weather...) ahead of one that doesn't, so "how are you" is honest, and a line
        /// keyed on more of its traits more likely than a general one.
        /// </summary>
        static string Choose(TalkBook book, DialogueArchive archive, DialogueArchive.Speaker speaker, string topic, in Context ctx,
                             ref Unity.Mathematics.Random rng, out TalkLine chosen)
        {
            chosen = null;
            var fits = new List<TalkLine>();
            var weights = new List<float>();
            var own = false;
            var conditioned = false;
            for (int c = 0; c < 3; c++)
            {
                var culture = c == 0 ? ctx.Who.Species : c == 1 ? ctx.Who.BodySpecies : DialogueArchive.Any;
                if (culture == null || c == 1 && culture == ctx.Who.Species || c < 2 && culture == DialogueArchive.Any)
                    continue;
                if (!book.Cultures.TryGetValue(culture, out var topics) || !topics.TryGetValue(topic, out var lines))
                    continue;
                foreach (var l in lines)
                    if (Fits(l, ctx))
                    {
                        if (c < 2)
                            own = true;
                        fits.Add(l);
                        weights.Add(math.max(l.Weight, 0f) * (c == 2 && own ? AnyShare : 1f) * (1f + l.Keys));
                        conditioned |= l.State != FolkCondition.None || l.Weather != null;
                    }
            }
            // (Its condition comes first: a tired folk says it's tired rather than "can't complain".)
            if (conditioned)
                for (int i = fits.Count - 1; i >= 0; i--)
                    if (fits[i].State == FolkCondition.None && fits[i].Weather == null)
                    {
                        fits.RemoveAt(i);
                        weights.RemoveAt(i);
                    }
            while (fits.Count > 0)
            {
                var total = 0f;
                foreach (var w in weights)
                    total += w;
                var roll = rng.NextFloat(total);
                var k = 0;
                for (; k < fits.Count - 1; k++)
                {
                    roll -= weights[k];
                    if (roll < 0f)
                        break;
                }
                var text = Fill(fits[k].Text, archive, speaker, ctx, ref rng);
                if (text != null)
                {
                    chosen = fits[k];
                    return text;
                }
                // (A slot it can't fill, a town for a traveller: another line.)
                fits.RemoveAt(k);
                weights.RemoveAt(k);
            }
            return null;
        }
        const float AnyShare = 0.3f;

        static bool Fits(TalkLine l, in Context ctx)
        {
            ref readonly var who = ref ctx.Who;
            if (l.Bands != null)
                for (int t = 0; t < l.Bands.Length; t++)
                    if (l.Bands[t] != 0 && (l.Bands[t] & (1 << who.Persona.Band((PersonaTrait)t))) == 0)
                        return false;
            if ((who.State.Flags & l.State) != l.State || (who.State.Flags & l.NotState) != 0)
                return false;
            if (l.Weather != null && Array.IndexOf(l.Weather, who.State.Weather.ToString()) < 0)
                return false;
            if (l.Moods != null && Array.IndexOf(l.Moods, ctx.Mood) < 0)
                return false;
            if (l.Stances != null && Array.IndexOf(l.Stances, who.Stance.ToString()) < 0)
                return false;
            if (l.Trades != null && Array.IndexOf(l.Trades, who.Job) < 0 && Array.IndexOf(l.Trades, who.Trade) < 0)
                return false;
            if (l.Willing.HasValue && l.Willing.Value != ctx.Willing)
                return false;
            if (l.Pestered.HasValue && l.Pestered.Value != ctx.Pestered)
                return false;
            var hasQuest = ctx.Quest != null;
            if (l.Need.HasValue && l.Need.Value != hasQuest || l.OfferKind == OfferKind.Quest && !hasQuest)
                return false;
            if (l.Home.HasValue && l.Home.Value != (who.HomeTown != null))
                return false;
            if (l.Job.HasValue && l.Job.Value != (who.Job != null || who.Trade != null))
                return false;
            return string.IsNullOrEmpty(l.When) || DialogueArchive.InHours(l.When, who.State.Hour);
        }

        /// <summary>A line's text with its slots filled: {quest} (the quest it offers, in a few words) here, the rest by the
        /// archive ({trade}, {home_town}, {player}, its culture's words); without an archive, only lines with no other slot.</summary>
        static string Fill(string text, DialogueArchive archive, DialogueArchive.Speaker speaker, in Context ctx, ref Unity.Mathematics.Random rng)
        {
            if (text == null)
                return null;
            if (text.IndexOf("{quest}", StringComparison.Ordinal) >= 0)
            {
                if (ctx.Quest == null)
                    return null;
                text = text.Replace("{quest}", QuestWords(ctx.Quest));
            }
            if (archive != null)
                return archive.Fill(text, speaker, ref rng, 0);
            return text.IndexOf('{') >= 0 ? null : text;
        }

        static OfferKind OfferOf(string s, out string id)
        {
            id = null;
            if (string.IsNullOrEmpty(s))
                return OfferKind.None;
            var colon = s.IndexOf(':');
            if (colon >= 0)
            {
                id = s.Substring(colon + 1).Trim();
                s = s.Substring(0, colon);
            }
            return Enum.TryParse(s.Trim(), true, out OfferKind k) ? k : OfferKind.None;
        }

        static Offer OfferOf(TalkLine line, in Context ctx)
        {
            if (line == null || line.OfferKind == OfferKind.None)
                return default;
            var offer = new Offer { Kind = line.OfferKind, Id = line.OfferId };
            if (offer.Kind == OfferKind.Quest)
                offer.Id = ctx.Quest;
            else if (offer.Kind == OfferKind.Directions && offer.Id == null)
                offer.Id = ctx.Who.HomeTown;
            return offer;
        }

        // ---- Persona and stance ----

        /// <summary>Its temper toward the player, as the walk-by lines have it (FolkTalkSystem.Mood): the wary and annoyed
        /// grumpy, kin and the warm cheerful, else its own.</summary>
        static string TalkMood(in FolkTalker who) =>
            (who.Stance == FolkStance.Wary || who.Stance == FolkStance.Annoyed ? FolkMood.Grumpy
                : who.Stance == FolkStance.Warm || who.Stance == FolkStance.Kin ? FolkMood.Cheerful
                : who.Persona.MoodBand).ToString();

        /// <summary>
        /// Whether it'll do the player a favour: trust and warmth against how often it's been asked lately and how it
        /// takes the player's body (kin and the warm help more readily, the wary and annoyed hardly). Pestered: asked
        /// twice or more in the last few minutes.
        /// </summary>
        static bool Willing(in FolkTalker who, out bool pestered)
        {
            var asked = Ledger(who.Individual, false);
            pestered = asked >= 2f;
            var bias = who.Stance switch
            {
                FolkStance.Kin => 0.3f,
                FolkStance.Warm => 0.2f,
                FolkStance.Rival => 0f,
                FolkStance.Wary => -0.25f,
                FolkStance.Annoyed => -0.45f,
                _ => 0f
            };
            // (Asked too often lately: no, whoever it is, until the asks fade.)
            if (pestered)
                return false;
            var p = who.Persona;
            return p.Trust * 0.55f + p.Warmth * 0.45f + bias + (p.Mood - 0.5f) * 0.1f - asked * 0.2f >= 0.42f;
        }

        /// <summary>How many favours each folk was asked lately (Individual → asks, when): one ask fades every 90 s.</summary>
        static readonly Dictionary<uint, (float Asks, float At)> asked = new Dictionary<uint, (float, float)>();
        const float AskFade = 90f;

        /// <summary>Its recent asks (faded to now); add: count one more.</summary>
        static float Ledger(uint individual, bool add)
        {
            var now = Time.realtimeSinceStartup;
            var n = asked.TryGetValue(individual, out var e) ? math.max(e.Asks - (now - e.At) / AskFade, 0f) : 0f;
            if (add)
            {
                if (asked.Count > 512)
                    asked.Clear();
                asked[individual] = (n + 1f, now);
            }
            return n;
        }

        /// <summary>Tests: forget every ask.</summary>
        public static void ForgetAsks() => asked.Clear();

        // ---- Quests ----

        static string[] questIds = Array.Empty<string>();
        static string[] questTexts = Array.Empty<string>();
        static DateTime questStamp;

        /// <summary>The quest this folk wants done (a quests.json id), or null: about half of folk have one.</summary>
        static string QuestFor(uint individual)
        {
            LoadQuests();
            if (questIds.Length == 0 || BiomeSampling.Scramble(individual ^ 0x2545F491u) % 100u >= 50u)
                return null;
            return questIds[BiomeSampling.Scramble(individual ^ 0x9E3779B9u) % (uint)questIds.Length];
        }

        static string QuestWords(string id)
        {
            var i = Array.IndexOf(questIds, id);
            return i >= 0 && !string.IsNullOrEmpty(questTexts[i]) ? questTexts[i] : id;
        }

        static void LoadQuests()
        {
            try
            {
                var path = System.IO.Path.Combine(Application.streamingAssetsPath, "quests.json");
                if (!System.IO.File.Exists(path))
                    return;
                var stamp = System.IO.File.GetLastWriteTimeUtc(path);
                if (stamp == questStamp)
                    return;
                questStamp = stamp;
                var ids = new List<string>();
                var texts = new List<string>();
                if (JObject.Parse(System.IO.File.ReadAllText(path))["quests"] is JArray qs)
                    foreach (var q in qs)
                        if (q is JObject o && o["id"] != null)
                        {
                            ids.Add((string)o["id"]);
                            // (A few words for the talk: "talk" if the quest has them, else from its type.)
                            texts.Add((string)o["talk"] ?? QuestWords((string)o["type"], (string)o["item"], (string)o["creature"], o["count"] != null ? (int)o["count"] : 1));
                        }
                questIds = ids.ToArray();
                questTexts = texts.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FolkConversation: couldn't read quests.json: {e.Message}");
            }
        }

        /// <summary>A quest put in a few words: "bring me 5 berries", "find the key I lost", "drive off the crows".</summary>
        static string QuestWords(string type, string item, string creature, int count)
        {
            var thing = DialogueArchive.Spoken(item)?.ToLowerInvariant();
            var beast = DialogueArchive.Spoken(creature)?.ToLowerInvariant();
            switch (type)
            {
                case "find" when thing != null: return $"find the {thing} I lost";
                case "clear" when beast != null: return $"drive off the {Plural(beast)}";
                default: return thing != null ? (count > 1 ? $"bring me {count} {Plural(thing)}" : $"bring me a {thing}") : "a little job";
            }
        }

        /// <summary>English plural, good enough for item and creature names: berry → berries, fish stays fish.</summary>
        static string Plural(string w) =>
            w.EndsWith("fish") || w.EndsWith("sheep") || w.EndsWith("deer") ? w
            : w.Length > 1 && w.EndsWith("y") && "aeiou".IndexOf(w[w.Length - 2]) < 0 ? w.Substring(0, w.Length - 1) + "ies"
            : w.EndsWith("s") || w.EndsWith("sh") || w.EndsWith("ch") || w.EndsWith("x") ? w + "es"
            : w + "s";

        static uint Hash(string s)
        {
            var h = 2166136261u;
            foreach (var ch in s)
                h = (h ^ ch) * 16777619u;
            return h;
        }

        // ---- The book of lines ----

        /// <summary>One conversation line and when it fits (see the conversation._readme in dialogue.json).</summary>
        public class TalkLine
        {
            public string Text, Gesture, OfferId, When;
            public OfferKind OfferKind;
            public float Weight = 1f;
            /// <summary>Per PersonaTrait, the bands it fits (bit 0 low, 1 mid, 2 high; 0 any), or null for any persona.</summary>
            public byte[] Bands;
            /// <summary>Conditions it needs, and conditions it can't have.</summary>
            public FolkCondition State, NotState;
            public string[] Weather, Moods, Stances, Trades;
            public bool? Willing, Pestered, Need, Home, Job;
            public bool Ends;
            /// <summary>How many things it's keyed on (traits, conditions): a more particular line is more likely.</summary>
            public int Keys;
        }

        /// <summary>Lines per culture and topic, and the topics' labels.</summary>
        public class TalkBook
        {
            public readonly Dictionary<string, Dictionary<string, List<TalkLine>>> Cultures =
                new Dictionary<string, Dictionary<string, List<TalkLine>>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int Count
            {
                get
                {
                    var n = 0;
                    foreach (var c in Cultures.Values)
                        foreach (var t in c.Values)
                            n += t.Count;
                    return n;
                }
            }
        }

        /// <summary>
        /// Reads a "conversation" section: { "topics": { id: label }, culture: { topic: [ line, ... ] } }. A line is a
        /// string, or an object with "text" and any of: a trait's name ("warmth": "high", or ["low", "mid"]), "state"
        /// ("tired", or ["hungry", "!busy"]: ! for not), "weather" (a ComfortKind: "Rain"), "mood", "stance", "trade",
        /// "when", "willing", "pestered", "need", "home", "job" (true/false), "gesture", "offer" ("quest", "item:Berry",
        /// "directions", "rumour", "discount", "refuses"), "ends", "weight".
        /// </summary>
        public static TalkBook Parse(JToken section)
        {
            if (!(section is JObject root))
                return null;
            var book = new TalkBook();
            foreach (var pair in root)
            {
                if (pair.Key.StartsWith("_"))
                    continue;
                if (pair.Key == "topics" && pair.Value is JObject labels)
                {
                    foreach (var l in labels)
                        if (!l.Key.StartsWith("_"))
                            book.Labels[l.Key] = (string)l.Value;
                    continue;
                }
                if (!(pair.Value is JObject topics))
                    continue;
                var culture = new Dictionary<string, List<TalkLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in topics)
                {
                    if (t.Key.StartsWith("_") || !(t.Value is JArray lines))
                        continue;
                    var list = new List<TalkLine>();
                    foreach (var l in lines)
                    {
                        var line = ParseLine(l);
                        if (line != null)
                            list.Add(line);
                    }
                    culture[t.Key] = list;
                }
                book.Cultures[pair.Key] = culture;
            }
            return book;
        }

        static TalkLine ParseLine(JToken t)
        {
            if (t.Type == JTokenType.String)
                return new TalkLine { Text = (string)t };
            if (!(t is JObject o) || o["text"] == null)
                return null;
            var line = new TalkLine
            {
                Text = (string)o["text"],
                Gesture = (string)o["gesture"],
                When = (string)o["when"],
                Weight = o["weight"] != null ? (float)o["weight"] : 1f,
                Weather = Strings(o["weather"]),
                Moods = Strings(o["mood"]),
                Stances = Strings(o["stance"]),
                Trades = Strings(o["trade"]),
                Willing = Bool(o["willing"]),
                Pestered = Bool(o["pestered"]),
                Need = Bool(o["need"]),
                Home = Bool(o["home"]),
                Job = Bool(o["job"]),
                Ends = o["ends"] != null && (bool)o["ends"],
            };
            line.OfferKind = OfferOf((string)o["offer"], out line.OfferId);
            foreach (var trait in FolkPersona.Traits)
            {
                var bands = Strings(o[trait.Name]);
                if (bands == null)
                    continue;
                line.Bands ??= new byte[FolkPersona.Count];
                foreach (var band in bands)
                {
                    var b = Array.IndexOf(FolkPersona.BandNames, band.ToLowerInvariant());
                    if (b >= 0)
                        line.Bands[(int)trait.Trait] |= (byte)(1 << b);
                }
                line.Keys++;
            }
            var states = Strings(o["state"]);
            if (states != null)
                foreach (var st in states)
                {
                    var not = st.StartsWith("!");
                    if (Enum.TryParse(not ? st.Substring(1) : st, true, out FolkCondition c))
                    {
                        if (not)
                            line.NotState |= c;
                        else
                            line.State |= c;
                        line.Keys++;
                    }
                }
            if (line.Weather != null || line.Moods != null || line.Trades != null)
                line.Keys++;
            return line;
        }

        static string[] Strings(JToken t) =>
            t == null ? null : t is JArray a ? new List<string>(a.Values<string>()).ToArray() : t.Type == JTokenType.String ? new[] { (string)t } : null;

        static bool? Bool(JToken t) => t == null || t.Type == JTokenType.Null ? (bool?)null : (bool)t;

        /// <summary>
        /// The lines every folk falls back on when dialogue.json has none that fit (a topic it lacks, a file mid-edit):
        /// plain, one with no slot per topic, so every topic always has an answer.
        /// </summary>
        static TalkBook Defaults => defaults ??= Parse(JObject.Parse(DefaultLines));
        static TalkBook defaults;

        const string DefaultLines = @"{
  ""Any"": {
    ""who"": [
      ""Just a local. Nobody special."",
      { ""text"": ""A {trade}, from {home_town}."", ""job"": true, ""home"": true },
      { ""text"": ""Who's asking?"", ""warmth"": ""low"" },
      { ""text"": ""I'm the best {trade} you'll meet, that's who."", ""pride"": ""high"", ""job"": true, ""gesture"": ""Bow"" }
    ],
    ""how"": [
      ""Can't complain."",
      { ""text"": ""Very well, thank you!"", ""mood"": ""Cheerful"", ""gesture"": ""Cheer"" },
      { ""text"": ""Could be better."", ""mood"": ""Grumpy"" },
      { ""text"": ""Tired. It's late."", ""state"": ""tired"" },
      { ""text"": ""Hungry, if you must know."", ""state"": ""hungry"" },
      { ""text"": ""Parched."", ""state"": ""thirsty"" },
      { ""text"": ""Busy. What is it?"", ""state"": ""busy"" },
      { ""text"": ""Not now, something's out there!"", ""state"": ""scared"" },
      { ""text"": ""This weather gets into my bones."", ""state"": ""weather"", ""gesture"": ""Shiver"" }
    ],
    ""work"": [
      ""Bit of this, bit of that."",
      { ""text"": ""I'm a {trade}."", ""job"": true }
    ],
    ""town"": [
      ""I'm only passing through."",
      { ""text"": ""This is {home_town}. It's home."", ""home"": true }
    ],
    ""news"": [
      ""Nothing much happens round here."",
      { ""text"": ""They say there's a cave in the hills nobody comes back from the same."", ""curiosity"": ""high"", ""offer"": ""rumour"" }
    ],
    ""favour"": [
      { ""text"": ""Sorry, I can't help you."", ""willing"": false, ""offer"": ""refuses"" },
      { ""text"": ""You again? No."", ""willing"": false, ""pestered"": true, ""offer"": ""refuses"" },
      { ""text"": ""Here, take a few berries for the road."", ""willing"": true, ""offer"": ""item:Berry"" },
      { ""text"": ""The town's that way. Follow the road."", ""willing"": true, ""offer"": ""directions"" }
    ],
    ""help"": [
      { ""text"": ""No, I'm all right, thanks."", ""need"": false },
      { ""text"": ""Well, if you're offering, you could {quest}."", ""need"": true, ""offer"": ""quest"" }
    ],
    ""bye"": [
      { ""text"": ""Safe travels."", ""gesture"": ""Wave"" },
      { ""text"": ""Hmph."", ""mood"": ""Grumpy"" }
    ],
    ""meet"": [
      ""Hm? What do you want, little one?"",
      { ""text"": ""Aww, hello there!"", ""beasts"": ""high"", ""gesture"": ""Pet"" },
      { ""text"": ""Shoo! Go on, get away!"", ""beasts"": ""low"", ""gesture"": ""Wave"" }
    ]
  }
}";
    }
}
