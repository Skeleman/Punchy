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
            var nearestPuppet = 1f;
            for (int i = 0; i < states.Length; i++)
                if (states[i].Mode == CreatureMode.Puppet && math.distancesq(states[i].Position, player) < nearestPuppet)
                {
                    nearestPuppet = math.distancesq(states[i].Position, player);
                    var body = director.SpeciesAt(bodies[i].Species);
                    playerSpecies = DialogueArchive.Spoken(body?.name);
                    playerKind = body?.Body.name;
                    if (states[i].Action >= IdleAction.Wave && states[i].Action <= IdleAction.Dance)
                    {
                        emoted = states[i].Action;
                        emotedAt = now;
                    }
                }

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
                Shown.Add(new Said { Who = entities[i], Text = text, Head = Head(s, ref b), Start = now, Until = now + 3.5f + text.Length * 0.05f });
            }
            FolkTalkLabels.Ensure();
            FolkTalkLabels.Show(Shown.Count > 0);
        }

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
            if (Shown.Count == 0)
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
        }
    }
}
