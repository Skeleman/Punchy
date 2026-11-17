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

    /// <summary>How a folk takes an animal coming up to it (A-93 row 3): fond of it as of a pet, puzzled by a tame beast
    /// that seems to want something, wary of it, scared of it, put off by it (rats, spiders, slimes), or awed (big or
    /// rare creatures).</summary>
    public enum BeastReaction : byte { None, Pet, Puzzled, Wary, Scared, Repelled, Awed }

    /// <summary>What a folk does about an animal player, with what the brain already does (FolkConversation.Respond):
    /// comes closer, comes closer with a bite to eat (the answer offers it), steps back, or runs off.</summary>
    public enum BeastResponse : byte { None, Approach, Feed, BackOff, Flee }

    /// <summary>A folk's answer: the words, a gesture to make while saying them (an IdleAction's name, or another word the
    /// talk window can show; Action is it parsed, None when it isn't an IdleAction), what it offers, and whether it ends
    /// the talk. To an animal player: how it takes it and what it does about it.</summary>
    public struct Answer
    {
        public string Topic, Text, Gesture;
        public IdleAction Action;
        public Offer Offer;
        public bool Ends;
        public BeastReaction Reaction;
        public BeastResponse Response;
        /// <summary>A-131: the player's replies to it (1-4), or null for none (back to the topic list). An offer of a quest
        /// or an item with no replies in the file gets Accept and Decline (FolkConversation.DefaultOptions).</summary>
        public Option[] Options;
        /// <summary>A-131: how many replies deep this answer is (0 for a topic's answer; FolkConversation.Choose counts).</summary>
        public int Depth;
        /// <summary>A-131: on an answer from FolkConversation.Choose, the reply picked, and for accept or decline the offer
        /// it was about (the earlier answer's: taking it through Items stays the talk window's).</summary>
        public Option Chosen;
        public Offer ChosenOffer;
        public override string ToString() => $"[{Topic}] \"{Text}\"" + (Gesture != null ? $" <{Gesture}>" : "") + (Offer.Kind != OfferKind.None ? $" offer {Offer}" : "")
            + (Reaction != BeastReaction.None ? $" ({Reaction}" + (Response != BeastResponse.None ? $", {Response})" : ")") : "") + (Ends ? " (ends)" : "")
            + (Options != null ? " replies: " + string.Join(" | ", Array.ConvertAll(Options, o => o.ToString())) : "");
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
        /// <summary>An animal player's species group (FolkConversation.GroupOf: "small", "pet", "livestock", "predator",
        /// "vermin", "big", "beast", or a group dialogue.json's beastTalk names); null for a person.</summary>
        public string PlayerGroup;
        public uint Individual;
        public FolkPersona Persona;
        public FolkState State;
        /// <summary>How it takes the player's body (CreatureBlob.StanceToward).</summary>
        public FolkStance Stance;
        /// <summary>The house it lives in and its place in it (A-105: Households.Of), or null for a folk with no house; its
        /// family lines name the other members.</summary>
        public Household House;
        public int Member;
    }

    /// <summary>
    /// Talking with a folk (A-93): the topics the player can raise and its answer to each, picked from dialogue.json's
    /// "conversation" section by its culture, its persona (FolkPersona: trait bands), its condition (FolkState: tired,
    /// busy, hungry, scared, the weather), its job and how it takes the player's body. Built only when asked, nothing
    /// runs per frame. Local: each machine picks its own words; a quest or item it offers is the asker's to take through
    /// the usual host calls (the talk window, P-26). The "asked too often" ledger is this machine's.
    /// </summary>
    public static partial class FolkConversation
    {
        public const string Who = "who", How = "how", Work = "work", Town = "town", News = "news", Favour = "favour", Help = "help", Bye = "bye";
        /// <summary>An animal player's one topic: going up to the folk (A-93 row 3).</summary>
        public const string Meet = "meet";
        /// <summary>Asking after its family (A-105 row 3): a topic dialogue.json adds, answered from its household.</summary>
        public const string Family = "family";

        /// <summary>The topics in menu order. The labels shown come from dialogue.json (conversation.topics, and
        /// beastTalk.topics for an animal player's); these are only for a missing file or a topic it leaves out.</summary>
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
            // (A puppet is a player's body, or a scripted one: nobody to talk to.)
            if (!b.Upright || !b.Hands || b.Haunts || s.Mode == CreatureMode.Puppet)
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
            var dry = false;
            using (var tq = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>()))
                if (tq.TryGetSingleton<TerrainSource>(out var terrain))
                {
                    if (s.Task != CreatureTask.Travel && s.Task != CreatureTask.Camper)
                        town = world.GetExistingSystemManaged<FolkTalkSystem>()?.TownOf(s, terrain);
                    dry = FolkState.OutOfWater(s.Position, b.WalksBed, terrain);
                }

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
                PlayerGroup = beast ? GroupOf(playerBody) : null,
                Individual = s.Individual,
                Persona = FolkPersona.Of(s.Individual),
                State = FolkState.Of(s, ref b, hasMind, mind, hour, night),
                Stance = b.StanceToward(playerIndex, s.Individual),
            };
            if (dry)
                who.State.Flags |= FolkCondition.Dry;
            Households.Of(s.Individual, out who.House, out who.Member);
            return true;
        }

        // ---- From a FolkTalker ----

        /// <summary>The topics for this talker: all of them to a person (with any topic dialogue.json adds, just before
        /// goodbye); to an animal, going up to it and wandering off.</summary>
        public static Topic[] Topics(in FolkTalker who)
        {
            var archive = DialogueArchive.Current;
            var labels = who.Beast ? archive?.BeastTalk?.Labels : archive?.Talk?.Labels;
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
            var reaction = who.Beast && topic == Meet ? React(who) : BeastReaction.None;
            var rng = new Unity.Mathematics.Random(BiomeSampling.Scramble(who.Individual ^ Hash(topic) ^ (++asks * 0x9E3779B9u)) | 1u);
            var ctx = new Context
            {
                Who = who,
                Mood = TalkMood(who),
                Willing = Willing(who, out var pestered),
                Pestered = pestered,
                Wronged = Thefts.HeldAgainst(who.Individual),
                Quest = QuestFor(who.Individual),
                Group = who.PlayerGroup,
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
                // (To an animal: "a wolf", not "a Wolf".)
                PlayerSpecies = who.Beast ? who.PlayerSpecies?.ToLowerInvariant() : who.PlayerSpecies,
                Stance = who.Stance == FolkStance.Neutral ? null : who.Stance.ToString(),
                Mood = ctx.Mood,
                Hour = who.State.Hour,
                Dry = who.State.Has(FolkCondition.Dry),
            };
            TalkLine line = null;
            string text = null;
            kinPicked = -1;
            var talk = TestTalk ?? archive?.Talk;
            // (A reply-only topic, A-131: lines and no label, reached through an option's "then".)
            var known = Array.Exists(AllTopics, t => t.Id == topic) || topic == Meet || talk != null && (talk.Labels.ContainsKey(topic) || talk.Has(topic));
            if (!known)
                topic = Who;
            // (An animal player: the beastTalk lines for how it takes it, or its goodbye; then the plain ones.)
            if (who.Beast && archive?.BeastTalk != null)
                text = Choose(archive.BeastTalk, archive, speaker, reaction != BeastReaction.None ? ReactionId(reaction) : topic, ctx, ref rng, out line);
            if (text == null && talk != null)
                text = Choose(talk, archive, speaker, topic, ctx, ref rng, out line);
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
            if (kinPicked >= 0 && who.House != null && HouseholdLog)
                LogKin(who, answer.Text);
            if (!who.Beast)
                answer.Options = OptionsOf(line, archive, speaker, ctx, ref rng, answer.Offer);
            if (reaction != BeastReaction.None)
            {
                answer.Reaction = reaction;
                answer.Response = line?.Response ?? DefaultResponse(reaction);
                // (Feeding it needs the bite to eat: a line that says "feed" without one just comes closer.)
                if (answer.Response == BeastResponse.Feed && answer.Offer.Kind != OfferKind.Item)
                    answer.Response = BeastResponse.Approach;
            }
            return answer;
        }

        // ---- An animal player ----

        /// <summary>The beastTalk topic for a reaction: its name in lower case ("pet", "scared").</summary>
        public static string ReactionId(BeastReaction r) => r.ToString().ToLowerInvariant();

        /// <summary>
        /// An animal species' group, as folk see it: the group dialogue.json's beastTalk "groups" puts it in (by its own
        /// name or its body's), else from what it is: a pet (Friendly), vermin (spins webs), a predator (driven off, or a
        /// hunter of 20 kg or more), big (600 kg or more), livestock (hooves), small (under 15 kg), else a beast.
        /// </summary>
        public static string GroupOf(CreatureSpecies species)
        {
            if (species == null)
                return "beast";
            var groups = DialogueArchive.Current?.BeastTalk?.Groups;
            if (groups != null && (groups.TryGetValue(species.name, out var g) || groups.TryGetValue(species.Body.name, out g)))
                return g;
            var b = species.Body;
            var kg = b.Weight * math.pow(species == b ? 1f : species.VariantScale, 3f);
            if (b.Friendly)
                return "pet";
            if (b.SpinsWebs)
                return "vermin";
            if (b.DrivenOff || b.Hunts.Count > 0 && kg >= 20f)
                return "predator";
            if (kg >= 600f)
                return "big";
            if (b.Hooves)
                return "livestock";
            return kg < 15f ? "small" : "beast";
        }

        /// <summary>
        /// How this folk takes the animal the player is (persona x species group x stance): from the group (small things
        /// and pets endearing, livestock and other beasts puzzling, predators scary, vermin off-putting, big ones awing),
        /// then its feeling for beasts (low: wary of what others pet, scared of what others admire; high: calmer, fond of
        /// livestock, curious past disgust), its courage (low: wary turns scared; high: scared turns wary) and its stance
        /// (wary or annoyed: no petting; kin or warm: fond, unless it's disgust or awe).
        /// </summary>
        public static BeastReaction React(in FolkTalker who)
        {
            var group = who.PlayerGroup ?? "beast";
            var r = BaseReaction(group);
            var p = who.Persona;
            var beasts = p.Band(PersonaTrait.Beasts);
            var courage = p.Band(PersonaTrait.Courage);
            if (beasts == 0)
                r = r == BeastReaction.Pet || r == BeastReaction.Puzzled ? BeastReaction.Wary : r == BeastReaction.Awed ? BeastReaction.Scared : r;
            else if (beasts == 2)
                r = r == BeastReaction.Scared && courage > 0 ? BeastReaction.Wary
                    : r == BeastReaction.Wary ? BeastReaction.Puzzled
                    : r == BeastReaction.Repelled && p.Band(PersonaTrait.Curiosity) == 2 ? BeastReaction.Puzzled
                    : r == BeastReaction.Puzzled && group == "livestock" ? BeastReaction.Pet
                    : r;
            if (courage == 0 && r == BeastReaction.Wary)
                r = BeastReaction.Scared;
            else if (courage == 2 && r == BeastReaction.Scared)
                r = BeastReaction.Wary;
            if ((who.Stance == FolkStance.Wary || who.Stance == FolkStance.Annoyed) && (r == BeastReaction.Pet || r == BeastReaction.Puzzled))
                r = BeastReaction.Wary;
            // (Fond of the player's folk: one step warmer, not straight to petting a wolf.)
            else if (who.Stance == FolkStance.Kin || who.Stance == FolkStance.Warm)
                r = r == BeastReaction.Scared ? BeastReaction.Wary : r == BeastReaction.Wary || r == BeastReaction.Puzzled ? BeastReaction.Pet : r;
            // (Nobody runs screaming from a rabbit: wary at worst.)
            if (r == BeastReaction.Scared && (group == "small" || group == "pet"))
                r = BeastReaction.Wary;
            return r;
        }

        static BeastReaction BaseReaction(string group) => group switch
        {
            "small" or "pet" => BeastReaction.Pet,
            "predator" => BeastReaction.Scared,
            "vermin" => BeastReaction.Repelled,
            "big" or "awe" => BeastReaction.Awed,
            _ => BeastReaction.Puzzled,
        };

        /// <summary>What it does when the line doesn't say: comes to a pet, backs off from what it's wary of or put off by,
        /// runs from what scares it, stays put otherwise.</summary>
        static BeastResponse DefaultResponse(BeastReaction r) => r switch
        {
            BeastReaction.Pet => BeastResponse.Approach,
            BeastReaction.Wary or BeastReaction.Repelled => BeastResponse.BackOff,
            BeastReaction.Scared => BeastResponse.Flee,
            _ => BeastResponse.None,
        };

        /// <summary>
        /// Makes a folk do an answer's response to an animal player at playerPos, with what the brain already does: runs
        /// off 30 m (Flee from the player), steps back 5 m, or walks up to just short of the player (Wander to a point;
        /// the brain pauses it there). On the simulating world only (host or solo, like Mantrap's flee); false on a joined
        /// client (its talk window has to ask the host) or when there's nothing to do. Called once per answer, not per frame.
        /// </summary>
        public static bool Respond(Entity folk, in Answer a, float3 playerPos)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || !world.EntityManager.Exists(folk) || !world.EntityManager.HasComponent<CreatureState>(folk))
                return false;
            return Respond(world.EntityManager.GetComponentData<CreatureState>(folk).Individual, a, playerPos);
        }

        /// <summary>Respond, for the folk with this Individual.</summary>
        public static bool Respond(uint individual, in Answer a, float3 playerPos)
        {
            if (a.Response == BeastResponse.None)
                return false;
            if (director == null)
                director = UnityEngine.Object.FindAnyObjectByType<FaunaDirector>();
            var sim = director != null ? director.SimWorld : null;
            if (sim == null || !sim.IsCreated)
                return false;
            var em = sim.EntityManager;
            // (Found by its Individual: its entity on the simulating world needn't be the one the caller holds.)
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var s = em.GetComponentData<CreatureState>(e);
                if (s.Individual != individual)
                    continue;
                var from = new float2(playerPos.x, playerPos.z);
                var dir = math.normalizesafe(s.Position.xz - from, new float2(1f, 0f));
                s.ModeTime = 0f;
                s.StageTime = 0f;
                s.Stage = 0;
                s.Action = IdleAction.None;
                s.ActionTime = 0f;
                switch (a.Response)
                {
                    case BeastResponse.Flee:
                        s.Mode = CreatureMode.Flee;
                        s.Focus = from;
                        s.Target = s.Position.xz + dir * 30f;
                        break;
                    case BeastResponse.BackOff:
                        s.Mode = CreatureMode.Wander;
                        s.Target = s.Position.xz + dir * 5f;
                        break;
                    default:
                        // (Up to the animal, just short of it.)
                        s.Mode = CreatureMode.Wander;
                        s.Target = from + dir * 1.2f;
                        break;
                }
                em.SetComponentData(e, s);
                return true;
            }
            return false;
        }

        // ---- Choosing a line ----

        /// <summary>What the lines are matched against, worked out once per answer.</summary>
        struct Context
        {
            public FolkTalker Who;
            public string Mood;
            /// <summary>Wronged (A-134 row 3): the folk's household holds a theft against this player (Thefts).</summary>
            public bool Willing, Pestered, Wronged;
            public string Quest;
            /// <summary>An animal player's group (FolkTalker.PlayerGroup).</summary>
            public string Group;
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
                var text = Fill(fits[k], archive, speaker, ctx, ref rng);
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
            if (l.Groups != null && Array.IndexOf(l.Groups, ctx.Group) < 0)
                return false;
            if (l.Willing.HasValue && l.Willing.Value != ctx.Willing)
                return false;
            if (l.Pestered.HasValue && l.Pestered.Value != ctx.Pestered)
                return false;
            if (l.Wronged.HasValue && l.Wronged.Value != ctx.Wronged)
                return false;
            var hasQuest = ctx.Quest != null;
            if (l.Need.HasValue && l.Need.Value != hasQuest || l.OfferKind == OfferKind.Quest && !hasQuest)
                return false;
            if (l.Home.HasValue && l.Home.Value != (who.HomeTown != null))
                return false;
            if (l.Job.HasValue && l.Job.Value != (who.Job != null || who.Trade != null))
                return false;
            if ((l.Kin != null || l.Gone) && !HasKin(who, l))
                return false;
            if (l.Pet && (who.House == null || who.House.Pet == null))
                return false;
            return string.IsNullOrEmpty(l.When) || DialogueArchive.InHours(l.When, who.State.Hour);
        }

        /// <summary>A line's text with its slots filled: {quest} (the quest it offers, in a few words) and {kin_name} /
        /// {kin_word} (a household member the line fits, A-105) here, the rest by the archive ({trade}, {home_town},
        /// {player}, its culture's words); without an archive, only lines with no other slot.</summary>
        static string Fill(TalkLine line, DialogueArchive archive, DialogueArchive.Speaker speaker, in Context ctx, ref Unity.Mathematics.Random rng)
        {
            var text = line.Text;
            if (text == null)
                return null;
            if (text.IndexOf("{kin_", StringComparison.Ordinal) >= 0)
            {
                var who = ctx.Who;
                var count = 0;
                if (who.House != null)
                    for (int m = 0; m < who.House.Members.Length; m++)
                        if (KinFits(who, line, m))
                            count++;
                if (count == 0)
                    return null;
                var pick = rng.NextInt(count);
                for (int m = 0; m < who.House.Members.Length; m++)
                    if (KinFits(who, line, m) && pick-- == 0)
                    {
                        kinPicked = m;
                        text = text.Replace("{kin_name}", who.House.NameOf(m)).Replace("{kin_word}", who.House.WordFor(who.Member, m));
                        break;
                    }
            }
            if (text.IndexOf("{pet_", StringComparison.Ordinal) >= 0)
            {
                var house = ctx.Who.House;
                if (house == null || house.Pet == null)
                    return null;
                text = text.Replace("{pet_name}", house.PetName).Replace("{pet_kind}", house.PetWord);
            }
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

        // ---- Family (A-105 row 3) ----

        /// <summary>The household member the last answer's line named, or -1.</summary>
        static int kinPicked = -1;

        /// <summary>Whether household member m is one a line can be about: kin of the talker, of a relation its "kin" key
        /// names (any when it has none), living or, for a "gone" line, died.</summary>
        static bool KinFits(in FolkTalker who, TalkLine l, int m)
        {
            var rel = who.House.Relation(who.Member, m);
            if (rel == KinRelation.None || who.House.Gone(m) != l.Gone)
                return false;
            return l.Kin == null || Array.IndexOf(l.Kin, RelationName(rel)) >= 0;
        }

        /// <summary>Whether a line's kin key fits the talker's house: "none" when nobody else living is there, else some
        /// member the line can be about.</summary>
        static bool HasKin(in FolkTalker who, TalkLine l)
        {
            var none = l.Kin != null && Array.IndexOf(l.Kin, "none") >= 0;
            var any = false;
            if (who.House != null)
                for (int m = 0; m < who.House.Members.Length && !any; m++)
                    any = none ? m != who.Member && !who.House.Gone(m) : KinFits(who, l, m);
            return none ? !any : any;
        }

        /// <summary>A relation as dialogue.json's kin key says it: "partner", "child", "inlaw"...</summary>
        public static string RelationName(KinRelation r) => r.ToString().ToLowerInvariant();

        /// <summary>The -households launch flag: family talk lines are logged with the house they came from.</summary>
        static bool HouseholdLog => Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-households") >= 0;

        static void LogKin(in FolkTalker who, string text)
        {
            var h = who.House;
            Debug.Log($"[households] talk {h.NameOf(who.Member)} {h.Members[who.Member].Role} of {h.Kind} at ({h.Door.x:F1}, {h.Door.z:F1}) landmark {h.Landmark}: " +
                      $"\"{text}\" names {h.NameOf(kinPicked)} {h.Members[kinPicked].Role} ({RelationName(h.Relation(who.Member, kinPicked))}{(h.Gone(kinPicked) ? ", died" : "")})");
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
            // (Asked too often lately: no, whoever it is, until the asks fade. Nor, while it holds, does a household the
            // player stole from: A-134 row 3.)
            if (pestered || Thefts.HeldAgainst(who.Individual))
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
        /// <summary>Per quest: its own "talk" words, or null, and its type, item, creature and count for QuestWords.</summary>
        static (string Talk, string Type, string Item, string Creature, int Count)[] questParts = Array.Empty<(string, string, string, string, int)>();
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
            if (i < 0)
                return id;
            var q = questParts[i];
            var words = !string.IsNullOrEmpty(q.Talk) ? q.Talk : QuestWords(q.Type, q.Item, q.Creature, q.Count);
            return string.IsNullOrEmpty(words) ? id : words;
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
                var parts = new List<(string, string, string, string, int)>();
                if (JObject.Parse(System.IO.File.ReadAllText(path))["quests"] is JArray qs)
                    foreach (var q in qs)
                        if (q is JObject o && o["id"] != null)
                        {
                            ids.Add((string)o["id"]);
                            // (A few words for the talk: "talk" if the quest has them, else from its type.)
                            parts.Add(((string)o["talk"], (string)o["type"], (string)o["item"], (string)o["creature"], o["count"] != null ? (int)o["count"] : 1));
                        }
                questIds = ids.ToArray();
                questParts = parts.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FolkConversation: couldn't read quests.json: {e.Message}");
            }
        }

        /// <summary>
        /// A quest put in a few words, by dialogue.json's conversation.quests (A-129 row 2): "find" ("find the {item} I
        /// lost"), "clear" ("drive off the {creatures}"), "bring" / "bringMany" ("bring me {count} {items}") and "other".
        /// QuestPhrases are only for a missing file or a key it leaves out.
        /// </summary>
        internal static string QuestWords(string type, string item, string creature, int count)
        {
            var thing = DialogueArchive.Spoken(item)?.ToLowerInvariant();
            var beast = DialogueArchive.Spoken(creature)?.ToLowerInvariant();
            var key = type == "find" && thing != null ? "find"
                : type == "clear" && beast != null ? "clear"
                : thing != null ? count > 1 ? "bringMany" : "bring"
                : "other";
            var phrases = DialogueArchive.Current?.Talk?.Quests;
            if (phrases == null || !phrases.TryGetValue(key, out var phrase) || string.IsNullOrEmpty(phrase))
                QuestPhrases.TryGetValue(key, out phrase);
            return phrase
                .Replace("{items}", thing != null ? Plural(thing) : "")
                .Replace("{item}", thing ?? "")
                .Replace("{creatures}", beast != null ? Plural(beast) : "")
                .Replace("{creature}", beast ?? "")
                .Replace("{count}", count.ToString());
        }

        /// <summary>The quest phrases with no dialogue.json (the file's conversation.quests has the same keys).</summary>
        public static readonly Dictionary<string, string> QuestPhrases = new Dictionary<string, string>
        {
            ["find"] = "find the {item} I lost", ["clear"] = "drive off the {creatures}", ["bring"] = "bring me a {item}",
            ["bringMany"] = "bring me {count} {items}", ["other"] = "a little job",
        };

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
            /// <summary>beastTalk: the animal player's groups it fits (FolkConversation.GroupOf), or null for any.</summary>
            public string[] Groups;
            /// <summary>Wronged (A-134 row 3): the speaker's household holds a theft against the player.</summary>
            public bool? Willing, Pestered, Need, Home, Job, Wronged;
            public bool Ends;
            /// <summary>beastTalk: what the folk does with this line, or null for its reaction's usual response.</summary>
            public BeastResponse? Response;
            /// <summary>Family talk (A-105): the household relations the line fits ("partner", "child"..., "none" for a folk
            /// with nobody else at home), or null; Gone: it's about a member who has died.</summary>
            public string[] Kin;
            public bool Gone;
            /// <summary>A-120: a line about the household's pet ({pet_name}, {pet_kind}), said only by a folk whose house
            /// keeps one.</summary>
            public bool Pet;
            /// <summary>How many things it's keyed on (traits, conditions): a more particular line is more likely.</summary>
            public int Keys;
            /// <summary>A-131: the player's replies to this line (1-4), or null.</summary>
            public TalkOption[] Options;
        }

        /// <summary>Lines per culture and topic, and the topics' labels.</summary>
        public class TalkBook
        {
            public readonly Dictionary<string, Dictionary<string, List<TalkLine>>> Cultures =
                new Dictionary<string, Dictionary<string, List<TalkLine>>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>beastTalk: species name → group, overriding the one GroupOf works out.</summary>
            public readonly Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>conversation.quests: a quest in a few words, by kind (QuestWords).</summary>
            public readonly Dictionary<string, string> Quests = new Dictionary<string, string>(StringComparer.Ordinal);
            /// <summary>Whether any culture has lines for this topic (a reply-only topic has lines and no label).</summary>
            public bool Has(string topic)
            {
                foreach (var c in Cultures.Values)
                    if (c.TryGetValue(topic, out var lines) && lines.Count > 0)
                        return true;
                return false;
            }

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
        /// "when", "willing", "pestered", "wronged", "need", "home", "job" (true/false), "gesture", "offer" ("quest", "item:Berry",
        /// "directions", "rumour", "discount", "refuses"), "ends", "weight". The beastTalk section reads the same, its topics
        /// the reactions ("pet", "puzzled", "wary", "scared", "repelled", "awed") and "bye", with "groups" (group → species
        /// names) and two more keys for a line: "group" (the animal player's groups) and "response" ("approach", "feed",
        /// "backoff", "flee", "none"). Family lines (A-105) add "kin" (the household relations: "partner", "child",
        /// "parent", "grandparent", "grandchild", "sibling", "inlaw", or "none") and "gone" (about a member who died), and
        /// fill {kin_name} and {kin_word}.
        /// </summary>
        public static TalkBook Parse(JToken section, JsonProblems problems = null)
        {
            if (section == null || section.Type == JTokenType.Null)
                return null;
            if (!(section is JObject root))
            {
                problems?.Add(section, "should be an object of culture name: { topic: [ lines ] }");
                return null;
            }
            problems ??= new JsonProblems();
            var book = new TalkBook();
            var thens = new List<(JToken At, string Topic)>();
            foreach (var pair in root)
            {
                if (pair.Key.StartsWith("_"))
                    continue;
                // (beastTalk: { "groups": { group: [ species, ... ] } }.)
                if (pair.Key == "groups" && pair.Value is JObject groups)
                {
                    foreach (var g in groups)
                        if (!g.Key.StartsWith("_") && problems.Strings(g.Value, g.Key) is string[] names)
                            foreach (var n in names)
                                book.Groups[n] = g.Key;
                    continue;
                }
                // (conversation: { "quests": { kind: "a few words with {item}, {items}, {creatures} or {count}" } }.)
                if (pair.Key == "quests" && pair.Value is JObject quests)
                {
                    foreach (var q in quests)
                        if (!q.Key.StartsWith("_"))
                        {
                            if (!QuestPhrases.ContainsKey(q.Key))
                                problems.Add(q.Value, $"no quest kind called \"{q.Key}\" ({string.Join(", ", QuestPhrases.Keys)})");
                            else if (q.Value.Type == JTokenType.String && ((string)q.Value).Trim().Length > 0)
                            {
                                problems.Slots(q.Value, (string)q.Value);
                                book.Quests[q.Key] = (string)q.Value;
                            }
                            else
                                problems.Add(q.Value, "a quest's words should be text");
                        }
                    continue;
                }
                if (pair.Key == "topics" && pair.Value is JObject labels)
                {
                    foreach (var l in labels)
                        if (!l.Key.StartsWith("_"))
                        {
                            if (l.Value.Type == JTokenType.String)
                                book.Labels[l.Key] = (string)l.Value;
                            else
                                problems.Add(l.Value, $"a topic's label should be text, not {JsonProblems.Kind(l.Value)}");
                        }
                    continue;
                }
                if (!(pair.Value is JObject topics))
                {
                    problems.Add(pair.Value, "a culture should be an object of topic: [ lines ]");
                    continue;
                }
                var culture = new Dictionary<string, List<TalkLine>>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in topics)
                {
                    if (t.Key.StartsWith("_"))
                        continue;
                    if (!(t.Value is JArray lines))
                    {
                        problems.Add(t.Value, "a topic should be a list of lines");
                        continue;
                    }
                    var list = new List<TalkLine>();
                    foreach (var l in lines)
                    {
                        var line = ParseLine(l, problems, thens);
                        if (line != null)
                            list.Add(line);
                    }
                    culture[t.Key] = list;
                }
                book.Cultures[pair.Key] = culture;
            }
            CheckThens(book, thens, problems);
            return book;
        }

        /// <summary>The keys a talk line can have, besides the persona traits' names (any other is a named problem: a typo).</summary>
        static readonly HashSet<string> LineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "gesture", "when", "weight", "weather", "mood", "stance", "trade", "group", "willing", "pestered", "wronged", "need",
            "home", "job", "ends", "kin", "gone", "pet", "offer", "response", "state", "options"
        };

        static readonly string[] KinWords = { "partner", "child", "parent", "grandparent", "grandchild", "sibling", "inlaw", "none" };

        static TalkLine ParseLine(JToken t, JsonProblems problems, List<(JToken, string)> thens = null)
        {
            if (t.Type == JTokenType.String)
            {
                problems.Slots(t, (string)t);
                return new TalkLine { Text = (string)t };
            }
            if (!(t is JObject o))
            {
                problems.Add(t, $"a line should be text or an object with \"text\", not {JsonProblems.Kind(t)}");
                return null;
            }
            var text = problems.Text(o);
            if (text == null)
                return null;
            foreach (var key in o)
                if (!key.Key.StartsWith("_") && !LineKeys.Contains(key.Key) && FolkPersona.TraitIndex(key.Key) < 0)
                    problems.Add(key.Value, $"unknown key \"{key.Key}\"");
            var line = new TalkLine
            {
                Text = text,
                Gesture = problems.Str(o, "gesture"),
                Weight = problems.Num(o, "weight", 1f),
                Ends = problems.Bool(o, "ends") ?? false,
            };
            if (line.Gesture != null && !Enum.TryParse(line.Gesture, true, out IdleAction _))
                problems.Add(o["gesture"], $"\"gesture\": no gesture called \"{line.Gesture}\"");
            ParseConditions(o, line, problems);
            var offer = problems.Str(o, "offer");
            line.OfferKind = OfferOf(offer, out line.OfferId);
            if (offer != null && line.OfferKind == OfferKind.None)
                problems.Add(o["offer"], $"\"offer\": \"{offer}\" isn't quest, item:<id>, directions, rumour, discount or refuses");
            var response = problems.Str(o, "response");
            if (response != null)
            {
                if (Enum.TryParse(response, true, out BeastResponse r))
                    line.Response = r;
                else
                    problems.Add(o["response"], $"\"response\": \"{response}\" isn't approach, feed, backoff, flee or none");
            }
            line.Options = ParseOptions(o, line, problems, thens);
            return line;
        }

        /// <summary>The keys that say when a line fits (traits, state, weather, mood, stance, trade, when, group, the
        /// true/false ones, kin, gone, pet) into line: shared by lines and, A-131, by a line's options.</summary>
        static void ParseConditions(JObject o, TalkLine line, JsonProblems problems)
        {
            line.When = problems.Str(o, "when");
            line.Weather = problems.Strings(o["weather"], "weather");
            line.Moods = problems.Strings(o["mood"], "mood");
            line.Stances = problems.Strings(o["stance"], "stance");
            line.Trades = problems.Strings(o["trade"], "trade");
            line.Groups = problems.Strings(o["group"], "group");
            line.Willing = problems.Bool(o, "willing");
            line.Pestered = problems.Bool(o, "pestered");
            line.Wronged = problems.Bool(o, "wronged");
            line.Need = problems.Bool(o, "need");
            line.Home = problems.Bool(o, "home");
            line.Job = problems.Bool(o, "job");
            line.Kin = problems.Strings(o["kin"], "kin");
            line.Gone = problems.Bool(o, "gone") ?? false;
            line.Pet = problems.Bool(o, "pet") ?? false;
            problems.OneOf(o, "when", line.When, "day", "night", "dawn", "morning", "afternoon", "evening", "dusk");
            if (line.Weather != null)
                foreach (var w in line.Weather)
                    if (!Enum.TryParse(w, true, out ComfortKind _))
                        problems.Add(o["weather"], $"\"weather\": no weather called \"{w}\"");
            if (line.Kin != null)
                foreach (var k in line.Kin)
                    problems.OneOf(o, "kin", k, KinWords);
            foreach (var trait in FolkPersona.Traits)
            {
                var bands = problems.Strings(o[trait.Name], trait.Name);
                if (bands == null)
                    continue;
                line.Bands ??= new byte[FolkPersona.Count];
                foreach (var band in bands)
                {
                    var b = Array.IndexOf(FolkPersona.BandNames, band.ToLowerInvariant());
                    if (b >= 0)
                        line.Bands[(int)trait.Trait] |= (byte)(1 << b);
                    // ("mood" also takes the talk mood's names, matched as written: Plain, Cheerful, Grumpy.)
                    else if (trait.Trait == PersonaTrait.Mood && Enum.TryParse(band, false, out FolkMood m) && m.ToString() == band)
                        continue;
                    else
                        problems.Add(o[trait.Name], $"\"{trait.Name}\": \"{band}\" isn't one of {string.Join(", ", FolkPersona.BandNames)}" +
                                     (trait.Trait == PersonaTrait.Mood ? ", Plain, Cheerful, Grumpy" : ""));
                }
                line.Keys++;
            }
            var states = problems.Strings(o["state"], "state");
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
                    else
                        problems.Add(o["state"], $"\"state\": no state called \"{st}\"");
                }
            if (line.Weather != null || line.Moods != null || line.Trades != null)
                line.Keys++;
            if (line.Groups != null)
                line.Keys++;
            if (line.Kin != null || line.Gone || line.Pet)
                line.Keys++;
        }

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
