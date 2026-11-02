using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Folk say a short line as you walk by (NPC conversations, stage 1): one of the townsfolk within a few metres of
    /// the player, awake and not busy fleeing, says something from DialogueArchive now and then, shown as text over its
    /// head for a few seconds (FolkTalkLabels). Emote at them (keys 1-4) and those who answer it (the brain turns them to
    /// face you and gesture back) say a line for that emote too. Each machine picks its own lines from its own copy of
    /// the archive; nothing is sent.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial class FolkTalkSystem : SystemBase
    {
        /// <summary>How near the player folk speak (m); how long one waits to speak again (s); the least time between
        /// any two lines, so a crowd doesn't all speak at once; at most this many lines up at a time.</summary>
        const float Reach = 5f, Again = 90f, Gap = 2.5f;
        const int MaxShown = 3;
        /// <summary>How near folk answering the player's emote say something (m; the brain answers from 20).</summary>
        const float AnswerReach = 15f;

        /// <summary>A line up over someone's head (for FolkTalkLabels, and tests).</summary>
        public struct Said
        {
            public Entity Who;
            public string Text;
            public float3 Head;
            public float Start, Until;
        }
        public static readonly List<Said> Shown = new List<Said>();
        /// <summary>Lines said since start (for tests); and off for screenshots that shouldn't have any.</summary>
        public static int LinesSaid, Answers;
        public static bool Quiet;
        /// <summary>
        /// Two folk stopped to talk (A-26, CreatureBrainSystem.Chatting): a small "..." over the head of whichever is
        /// talking (from the Stage byte, so every machine shows it); and on the host or solo (where who is talking to
        /// whom is known) one log line per talk, with the pair, the place and how long it went on. Talks logged since start.
        /// </summary>
        public static readonly List<float3> Bubbles = new List<float3>();
        public static int Chats;
        struct Chat
        {
            public Entity With;
            public string Names;
            public float Start;
            public float3 At;
            public int Children;
        }
        readonly Dictionary<Entity, Chat> chats = new Dictionary<Entity, Chat>();
        readonly Dictionary<Entity, int> childrenBy = new Dictionary<Entity, int>();
        readonly HashSet<Entity> talking = new HashSet<Entity>();
        readonly List<Entity> ended = new List<Entity>();
        /// <summary>
        /// Tests ("-stancelog", Z-52): log each line with the speaker's stance, and once per player body, how every folk
        /// species takes that body over 4000 individuals (the odds the stance hash gives).
        /// </summary>
        public static bool LogStances;
        int stanceTableFor = -1;

        void LogStanceTable(int playerIndex)
        {
            stanceTableFor = playerIndex;
            var report = new System.Text.StringBuilder($"FolkTalk: stances toward a {director.SpeciesAt(playerIndex)?.name} body:");
            for (int sp = 0; sp < director.SpeciesCount; sp++)
            {
                var blob = director.BlobAt(sp);
                if (!blob.IsCreated || !blob.Value.Upright || !blob.Value.Hands || blob.Value.Haunts)
                    continue;
                var counts = new int[6];
                for (uint ind = 1; ind <= 4000; ind++)
                    counts[(int)blob.Value.StanceToward(playerIndex, ind * 2654435761u + 12345u)]++;
                report.Append($" {director.SpeciesAt(sp).name}[");
                for (int k = 0; k < counts.Length; k++)
                    if (counts[k] > 0)
                        report.Append($"{(FolkStance)k} {counts[k] / 40f:F0}% ");
                report.Length--;
                report.Append(']');
            }
            UnityEngine.Debug.Log(report.ToString());
        }

        EntityQuery query, playerQuery, paletteQuery;
        readonly Dictionary<Entity, float> nextTalk = new Dictionary<Entity, float>();
        /// <summary>What each of the folk near was doing last frame (to catch the moment it starts answering an emote).</summary>
        readonly Dictionary<Entity, IdleAction> lastAction = new Dictionary<Entity, IdleAction>();
        /// <summary>The player's last emote and when (Time.time).</summary>
        IdleAction emoted;
        float emotedAt = -100f;
        readonly List<Entity> stale = new List<Entity>();
        float nextAny;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x7A1C);
        /// <summary>Each settlement's name (null for none), worked out once per archive.</summary>
        string[] townNames;
        DialogueArchive namedWith;
        FaunaDirector director;

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            paletteQuery = GetEntityQuery(ComponentType.ReadOnly<BiomePalette>());
        }

        protected override void OnUpdate()
        {
            var now = UnityEngine.Time.time;
            Tidy(now);
            WatchChats(now, SystemAPI.TryGetSingleton<TerrainSource>(out var chatTerrain), chatTerrain);
            var archive = DialogueArchive.Current;
            if (archive == null || Quiet || playerQuery.CalculateEntityCount() != 1 || !SystemAPI.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            if (director == null || UnityEngine.Time.frameCount % 120 == 0)
                director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || director.SpeciesCount == 0)
                return;
            if (namedWith != archive)
                NameTowns(archive, terrain);
            var player = playerQuery.GetSingleton<LocalTransform>().Position;
            var hour = SystemAPI.TryGetSingleton<FaunaClock>(out var clock) ? clock.Hour : 12f;

            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);
            // The player's own body (the puppet where the player stands), for {player}: only to folk of another kind (a
            // dwarf doesn't remark on a dwarf).
            string playerSpecies = null, playerKind = null;
            var playerIndex = -1;
            var nearestPuppet = 1f;
            for (int i = 0; i < states.Length; i++)
                if (states[i].Mode == CreatureMode.Puppet && math.distancesq(states[i].Position, player) < nearestPuppet)
                {
                    nearestPuppet = math.distancesq(states[i].Position, player);
                    var body = director.SpeciesAt(bodies[i].Species);
                    playerSpecies = DialogueArchive.Spoken(body?.name);
                    playerKind = body?.Body.name;
                    playerIndex = bodies[i].Species;
                    if (states[i].Action >= IdleAction.Wave && states[i].Action <= IdleAction.Dance)
                    {
                        emoted = states[i].Action;
                        emotedAt = now;
                    }
                }

            if (LogStances && playerIndex >= 0 && playerIndex != stanceTableFor)
                LogStanceTable(playerIndex);

            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                ref var b = ref bodies[i].Blob.Value;
                // Lines already up follow their speaker.
                for (int k = 0; k < Shown.Count; k++)
                    if (Shown[k].Who == entities[i])
                    {
                        var said = Shown[k];
                        said.Head = Head(s, ref b);
                        Shown[k] = said;
                    }
                // (A ghost speaks only to a ghost: a player in a ghost's body.)
                var spirit = b.Haunts && playerKind != null && playerKind == director.SpeciesAt(bodies[i].Species)?.Body.name;
                // Starting to answer the player's emote just now (one of theirs, a moment ago, nearby): a line for it.
                var answering = false;
                if ((b.Upright && b.Hands || spirit) && s.Mode != CreatureMode.Puppet && math.distancesq(s.Position, player) < AnswerReach * AnswerReach)
                {
                    var was = lastAction.TryGetValue(entities[i], out var a) ? a : IdleAction.None;
                    lastAction[entities[i]] = s.Action;
                    answering = s.Action >= IdleAction.Wave && s.Action <= IdleAction.Dance && s.Action != was && now - emotedAt < 4f;
                }
                if (!answering)
                {
                    if (now < nextAny || Shown.Count >= MaxShown || !(CanTalk(s, ref b) || spirit && CanHaunt(s)) ||
                        math.distancesq(s.Position, player) > Reach * Reach)
                        continue;
                    if (nextTalk.TryGetValue(entities[i], out var next) && now < next)
                        continue;
                }
                nextTalk[entities[i]] = now + Again;
                var species = director.SpeciesAt(bodies[i].Species);
                var who = new DialogueArchive.Speaker
                {
                    Species = species?.name,
                    BodySpecies = species?.Body.name,
                    Trade = s.Task != CreatureTask.None ? s.Task.ToString() : null,
                    HomeTown = s.Task == CreatureTask.Travel || s.Task == CreatureTask.Camper ? null : HomeTown(s, terrain),
                    PlayerSpecies = playerKind != null && playerKind == species?.Body.name ? null : playerSpecies,
                    // (The same stance the host's brain works out: a hash of who it is and the player's body.)
                    Stance = Stance(b.Upright && b.Hands && !b.Haunts ? b.StanceToward(playerIndex, s.Individual) : FolkStance.Neutral),
                    Hour = hour
                };
                var text = archive.Pick(who, ref rng, answering ? emoted.ToString() : null);
                if (string.IsNullOrEmpty(text))
                    continue;
                if (answering)
                {
                    // (An answer goes up even with the others: in place of this one's own line, or the oldest.)
                    for (int k = Shown.Count - 1; k >= 0; k--)
                        if (Shown[k].Who == entities[i])
                            Shown.RemoveAt(k);
                    if (Shown.Count >= MaxShown + 1)
                        Shown.RemoveAt(0);
                    Answers++;
                }
                nextAny = now + Gap;
                LinesSaid++;
                if (LogStances)
                    UnityEngine.Debug.Log($"FolkTalk: {who.Species} ({who.Stance ?? "Neutral"}) to a {director.SpeciesAt(playerIndex)?.name}" +
                                          $"{(answering ? " answering " + emoted : "")}: {text}");
                Shown.Add(new Said { Who = entities[i], Text = text, Head = Head(s, ref b), Start = now, Until = now + 3.5f + text.Length * 0.05f });
            }
            FolkTalkLabels.Ensure();
            FolkTalkLabels.Show(Shown.Count > 0 || Bubbles.Count > 0);
        }

        void WatchChats(float now, bool hasTerrain, in TerrainSource terrain)
        {
            Bubbles.Clear();
            if (director == null && UnityEngine.Time.frameCount % 60 == 0)
                director = Object.FindAnyObjectByType<FaunaDirector>();
            var cam = Camera.main;
            var states = query.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = query.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = query.ToEntityArray(Allocator.Temp);
            talking.Clear();
            childrenBy.Clear();
            for (int i = 0; i < states.Length; i++)
            {
                var s = states[i];
                if (s.Mode != CreatureMode.Idle || s.Stage != CreatureAnimator.ChatTalk && s.Stage != CreatureAnimator.ChatListen)
                    continue;
                if (s.Stage == CreatureAnimator.ChatTalk && !Quiet && cam != null && math.distancesq((float3)cam.transform.position, s.Position) < 30f * 30f)
                    Bubbles.Add(Head(s, ref bodies[i].Blob.Value) + new float3(0f, 0.1f, 0f));
                if (s.ChatRole == 3)
                    childrenBy[s.Partner] = (childrenBy.TryGetValue(s.Partner, out var n) ? n : 0) + 1;
                if (s.ChatRole != 1)
                    continue;
                talking.Add(entities[i]);
                if (!chats.ContainsKey(entities[i]))
                {
                    var other = EntityManager.Exists(s.Partner) && EntityManager.HasComponent<CreatureBody>(s.Partner)
                        ? director?.SpeciesAt(EntityManager.GetComponentData<CreatureBody>(s.Partner).Species)?.name : null;
                    chats[entities[i]] = new Chat
                    {
                        With = s.Partner,
                        Names = $"{director?.SpeciesAt(bodies[i].Species)?.name ?? "?"} & {other ?? "?"}",
                        Start = now,
                        At = s.Position
                    };
                }
            }
            foreach (var pair in childrenBy)
                if (chats.TryGetValue(pair.Key, out var c) && pair.Value > c.Children)
                {
                    c.Children = pair.Value;
                    chats[pair.Key] = c;
                }
            ended.Clear();
            foreach (var pair in chats)
                if (!talking.Contains(pair.Key))
                    ended.Add(pair.Key);
            foreach (var e in ended)
            {
                var c = chats[e];
                chats.Remove(e);
                Chats++;
                var town = hasTerrain ? HomeTown(new CreatureState { Position = c.At }, terrain) : null;
                UnityEngine.Debug.Log($"FolkChat: {c.Names} at ({c.At.x:F0}, {c.At.z:F0}){(town != null ? " in " + town : "")} for {now - c.Start:F1} s" +
                                      $"{(c.Children > 0 ? $", {c.Children} child(ren) by them" : "")}");
            }
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
            if (Bubbles.Count > 0)
            {
                FolkTalkLabels.Ensure();
                FolkTalkLabels.Show(true);
            }
        }

        static string Stance(FolkStance stance) => stance == FolkStance.Neutral ? null : stance.ToString();

        /// <summary>Townsfolk awake and about: upright, with hands, not a ghost or a player's body, not asleep, fleeing or at home.</summary>
        static bool CanTalk(in CreatureState s, ref CreatureBlob b) =>
            b.Upright && b.Hands && !b.Haunts &&
            s.Mode != CreatureMode.Puppet && s.Mode != CreatureMode.Sleep && s.Mode != CreatureMode.Flee && s.Mode != CreatureMode.Rest &&
            s.Mode != CreatureMode.Home;   // (indoors, or on the way: not through the walls)

        /// <summary>A ghost abroad (not a player's body, not gone to rest by day).</summary>
        static bool CanHaunt(in CreatureState s) => s.Mode != CreatureMode.Puppet && s.Mode != CreatureMode.Sleep;

        static float3 Head(in CreatureState s, ref CreatureBlob b) => s.Position + new float3(0f, b.StandHeight * s.Scale + 0.45f, 0f);

        void Tidy(float now)
        {
            for (int k = Shown.Count - 1; k >= 0; k--)
                if (now >= Shown[k].Until || !EntityManager.Exists(Shown[k].Who))
                    Shown.RemoveAt(k);
            if (lastAction.Count > 256)
                lastAction.Clear();
            if (nextTalk.Count > 256)
            {
                stale.Clear();
                foreach (var pair in nextTalk)
                    if (now >= pair.Value || !EntityManager.Exists(pair.Key))
                        stale.Add(pair.Key);
                foreach (var e in stale)
                    nextTalk.Remove(e);
            }
            if (Shown.Count == 0 && Bubbles.Count == 0)
                FolkTalkLabels.Show(false);
        }

        /// <summary>The town a speaker lives in: the settlement round its home, or null.</summary>
        string HomeTown(in CreatureState s, in TerrainSource terrain)
        {
            if (!terrain.Settlements.IsCreated || townNames == null)
                return null;
            ref var towns = ref terrain.Settlements.Value.Settlements;
            var home = math.all(s.Home == float2.zero) ? s.Position.xz : s.Home;
            for (int t = 0; t < towns.Length && t < townNames.Length; t++)
                if (math.distance(towns[t].Centre, home) < towns[t].Radius * 1.5f + 10f)
                    return townNames[t];
            return null;
        }

        /// <summary>Every settlement's name, from the culture of its folk (its landmark's upright residents).</summary>
        void NameTowns(DialogueArchive archive, in TerrainSource terrain)
        {
            namedWith = archive;
            townNames = null;
            if (!terrain.Settlements.IsCreated || paletteQuery.CalculateEntityCount() != 1)
                return;
            var palette = EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            ref var towns = ref terrain.Settlements.Value.Settlements;
            townNames = new string[towns.Length];
            for (int t = 0; t < towns.Length; t++)
            {
                string culture = null;
                var type = palette != null && palette.Landmarks != null && towns[t].Type >= 0 && towns[t].Type < palette.Landmarks.Length ? palette.Landmarks[towns[t].Type] : null;
                if (type != null)
                    foreach (var r in type.Residents)
                        if (r.Species != null && r.Species.Body.Upright)
                        {
                            culture = r.Species.Body.name;
                            break;
                        }
                townNames[t] = archive.TownName(culture ?? DialogueArchive.Any, towns[t].Centre);
            }
        }
    }

    /// <summary>Draws FolkTalkSystem.Shown: each line over its speaker's head, fading in and out, smaller further off.</summary>
    public class FolkTalkLabels : MonoBehaviour
    {
        static FolkTalkLabels instance;
        GUIStyle style, shadow;

        public static void Ensure()
        {
            if (instance != null)
                return;
            var go = new GameObject("FolkTalkLabels");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FolkTalkLabels>();
            instance.enabled = false;
        }

        public static void Show(bool show)
        {
            if (instance != null && instance.enabled != show)
                instance.enabled = show;
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (InteractionPrompt.Hidden || cam == null || Event.current.type != EventType.Repaint)
                return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, wordWrap = true };
                style.normal.textColor = Color.white;
                shadow = new GUIStyle(style);
            }
            var clock = Time.time;
            foreach (var said in FolkTalkSystem.Shown)
            {
                var screen = cam.WorldToScreenPoint(said.Head);
                if (screen.z < 0.3f)
                    continue;
                var fade = Mathf.Clamp01((clock - said.Start) / 0.25f) * Mathf.Clamp01((said.Until - clock) / 0.5f);
                var size = Mathf.RoundToInt(Screen.height / 42f * Mathf.Clamp(6f / screen.z, 0.6f, 1.2f));
                style.fontSize = shadow.fontSize = size;
                var width = Mathf.Min(Screen.width * 0.3f, size * 18f);
                var height = style.CalcHeight(new GUIContent(said.Text), width);
                var rect = new Rect(screen.x - width / 2f, Screen.height - screen.y - height, width, height);
                style.normal.textColor = new Color(1f, 0.97f, 0.88f, fade);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.75f * fade);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), said.Text, shadow);
                GUI.Label(rect, said.Text, style);
            }
            // (Talking folk: a small "..." over the head.)
            foreach (var head in FolkTalkSystem.Bubbles)
            {
                var screen = cam.WorldToScreenPoint(head);
                if (screen.z < 0.3f)
                    continue;
                var size = Mathf.RoundToInt(Screen.height / 30f * Mathf.Clamp(5f / screen.z, 0.5f, 1.3f));
                style.fontSize = shadow.fontSize = size;
                var rect = new Rect(screen.x - size * 1.5f, Screen.height - screen.y - size * 1.4f, size * 3f, size * 1.4f);
                style.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), "...", shadow);
                GUI.Label(rect, "...", style);
            }
        }
    }
}
