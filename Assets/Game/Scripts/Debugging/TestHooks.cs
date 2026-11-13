#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Punchy.Ambience;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// What the play-mode tests (Assets/Tests/PlayMode, W-97) read from the game. The test assembly can't reference
    /// Assembly-CSharp, so it calls these public statics by reflection; each returns plain values (bool, int, float,
    /// float[], string, Vector3) so the tests need no game types. Editor only: nothing here reaches a player build.
    /// Mid-session changes (weather, time, place, spawns) go through DebugControls.RunOptions, the same test flags as PlayArgs.
    /// </summary>
    public static class TestHooks
    {
        static EntityManager EM => World.DefaultGameObjectInjectionWorld.EntityManager;

        static bool Source(out TerrainSource source)
        {
            source = default;
            if (World.DefaultGameObjectInjectionWorld == null || !World.DefaultGameObjectInjectionWorld.IsCreated)
                return false;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            return q.TryGetSingleton(out source);
        }

        static BiomePalette Palette()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            return q.CalculateEntityCount() == 1 ? EM.GetComponentObject<BiomePalette>(q.GetSingletonEntity()) : null;
        }

        /// <summary>The ground, the player and the fauna are all up: a test can start measuring.</summary>
        public static bool WorldReady()
        {
            if (!Source(out _))
                return false;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            return q.CalculateEntityCount() == 1 && director != null && director.SpeciesCount > 0;
        }

        /// <summary>A "-place prop:" request is still waiting for the flora lookup (#137).</summary>
        public static bool PropPlacePending() => DebugControls.PropPlacePending;

        public static Vector3 PlayerPosition()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            return q.TryGetSingleton<LocalTransform>(out var t) ? (Vector3)t.Position : Vector3.zero;
        }

        /// <summary>
        /// Every landmark as "type x z scale" (biome looks counted as their base type, the great mushroom apart), sorted,
        /// FNV-1a hashed: the landmark hash the reports quote (v3: 108 / 4D2643D7). "count hash".
        /// </summary>
        public static string LandmarkHash()
        {
            var pal = Palette();
            if (!Source(out var s) || !s.Landmarks.IsCreated || pal == null)
                return "none";
            ref var set = ref s.Landmarks.Value;
            var all = new List<LandmarkType>(pal.Landmarks);
            LandmarkBaking.AddLooks(all);
            var baseOf = new Dictionary<LandmarkType, LandmarkType>();
            foreach (var type in all)
                if (type != null && type.BiomeLooks != null)
                    foreach (var look in type.BiomeLooks)
                        if (look?.Look != null && look.Look.name != "GreatMushroom")
                            baseOf[look.Look] = type;
            var lines = new List<string>();
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var l = set.Instances[i];
                var t = l.Type < all.Count ? all[l.Type] : null;
                if (t != null && baseOf.TryGetValue(t, out var b))
                    t = b;
                lines.Add($"{(t != null ? t.name : l.Type.ToString())} {l.Position.x:F1} {l.Position.y:F1} {l.Scale:F3}");
            }
            lines.Sort(System.StringComparer.Ordinal);
            var h = 2166136261u;
            foreach (var line in lines)
                foreach (var ch in line)
                    h = (h ^ ch) * 16777619u;
            return $"{lines.Count} {h:X8}";
        }

        /// <summary>Collects the island blobs of a source, boxed, so another source's blobs can be checked against them.</summary>
        struct BlobList : IslandCache.IBlobVisitor
        {
            public List<object> Blobs;
            public List<byte[]> Bytes;
            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged
            {
                Blobs?.Add(blob);
                Bytes?.Add(IslandCache.BlobBytes(blob));
            }
        }

        static List<byte[]> IslandBlobBytes(ref TerrainSource source)
        {
            var list = new BlobList { Bytes = new List<byte[]>() };
            IslandCache.Each(ref source, ref list);
            return list.Bytes;
        }

        static List<string> IslandBlobNames()
        {
            var names = new NameList { Names = new List<string>() };
            var none = default(TerrainSource);
            IslandCache.Each(ref none, ref names);
            return names.Names;
        }

        struct NameList : IslandCache.IBlobVisitor
        {
            public List<string> Names;
            public void Visit<T>(string name, ref BlobAssetReference<T> blob) where T : unmanaged => Names.Add(name);
        }

        /// <summary>
        /// W-140: bakes the island fresh from the inputs the live island started from, and checks it byte for byte against
        /// the live island (loaded from the cache or baked, whichever this launch did) and against a write and read of the
        /// island file, plus the landmark hash all three ways. "" when all match, else what differs. Takes a bake (~14 s).
        /// </summary>
        public static string IslandCacheRoundTrip()
        {
            var pal = Palette();
            if (!Source(out var live) || pal == null || IslandV2.ActiveVersion < 2) return "no v2/v3 island";
            int version = IslandV2.ActiveVersion;
            var types = IslandV2System.LandmarkTypes(pal);
            var fresh = IslandCache.LastInputs;
            IslandV2System.Bake(ref fresh, pal, types, version);
            var problems = new List<string>();
            var names = IslandBlobNames();
            var freshBytes = IslandBlobBytes(ref fresh);
            var liveBytes = IslandBlobBytes(ref live);
            var path = System.IO.Path.Combine(Application.temporaryCachePath, $"island-roundtrip-{System.Diagnostics.Process.GetCurrentProcess().Id}.bin");
            var loaded = IslandCache.LastInputs;
            bool parsed = false;
            try
            {
                const string key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                if (!IslandCache.WriteFile(path, ref fresh, version, key)) problems.Add("file not written");
                else if (!(parsed = IslandCache.Parse(System.IO.File.ReadAllBytes(path), ref loaded, version, key, out var why))) problems.Add("file not read: " + why);
                // A key that differs by one byte must miss.
                if (parsed && IslandCache.Parse(System.IO.File.ReadAllBytes(path), ref live, version, "f" + key.Substring(1), out _)) problems.Add("a changed key still loaded");
                var loadedBytes = parsed ? IslandBlobBytes(ref loaded) : null;
                for (int i = 0; i < names.Count; i++)
                {
                    if (freshBytes[i] == null) problems.Add(names[i] + " unreadable after a fresh bake");
                    if (!Same(freshBytes[i], liveBytes[i])) problems.Add($"{names[i]} live != fresh {Diff(liveBytes[i], freshBytes[i])}");
                    if (loadedBytes != null && !Same(freshBytes[i], loadedBytes[i])) problems.Add($"{names[i]} loaded != fresh {Diff(loadedBytes[i], freshBytes[i])}");
                }
                if (live.BiomeKind != fresh.BiomeKind) problems.Add("biome kind");
                if (!Same(StructBytes(live.RiverSettings), StructBytes(fresh.RiverSettings))) problems.Add("river settings live");
                if (parsed && !Same(StructBytes(loaded.RiverSettings), StructBytes(fresh.RiverSettings))) problems.Add("river settings loaded");
                var hFresh = IslandV2System.LandmarkHash(ref fresh, types);
                var hLive = IslandV2System.LandmarkHash(ref live, types);
                if (hFresh != hLive) problems.Add($"landmark hash live {hLive} != fresh {hFresh}");
                if (parsed) { var hLoaded = IslandV2System.LandmarkHash(ref loaded, types); if (hLoaded != hFresh) problems.Add($"landmark hash loaded {hLoaded} != fresh {hFresh}"); }
            }
            finally
            {
                IslandCache.Free(ref fresh);
                if (parsed) IslandCache.Free(ref loaded);
                try { System.IO.File.Delete(path); } catch (System.Exception) { }
            }
            return string.Join("; ", problems);
        }

        static byte[] StructBytes<T>(T value) where T : unmanaged =>
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref value, 1)).ToArray();

        /// <summary>Where two byte strings first differ (lengths, first offset, and the bytes there).</summary>
        static string Diff(byte[] a, byte[] b)
        {
            if (a == null || b == null) return "(unreadable)";
            int n = System.Math.Min(a.Length, b.Length), at = 0, count = 0;
            while (at < n && a[at] == b[at]) at++;
            for (int i = at; i < n; i++) if (a[i] != b[i]) count++;
            return $"({a.Length} vs {b.Length} bytes, {count} differ from {at}: {(at < n ? a[at] + " vs " + b[at] : "-")})";
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>W-140: every blob field of TerrainSource that the island file neither saves nor names as not cached
        /// ("" when none): a new blob the bake fills must join IslandCache.Each, or a cache hit would leave it empty.</summary>
        public static string IslandCacheMissingBlobs()
        {
            var known = new HashSet<string>(IslandBlobNames());
            known.UnionWith(IslandCache.NotCached);
            var missing = new List<string>();
            foreach (var f in typeof(TerrainSource).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(BlobAssetReference<>) && !known.Contains(f.Name))
                    missing.Add(f.Name);
            return string.Join(", ", missing);
        }

        /// <summary>
        /// River bends: the turn (degrees) at each river cell between the segment in and the segment out, over rivers at
        /// least minWidth wide. [cells, largest turn, 99th percentile turn].
        /// </summary>
        public static float[] RiverTurns(float minWidth)
        {
            if (!Source(out var s) || !s.Rivers.IsCreated)
                return new[] { 0f, 0f, 0f };
            ref var r = ref s.Rivers.Value;
            var turns = new List<float>();
            for (int i = 0; i < r.Width.Length; i++)
            {
                if (r.Width[i] < minWidth || r.Width[i] <= 0f)
                    continue;
                int up = r.Up[i], down = r.Down[i];
                if (up < 0 || down < 0 || down >= r.Width.Length || r.Width[down] <= 0f)
                    continue;
                var a = r.Point(i) - r.Point(up);
                var c = r.Point(down) - r.Point(i);
                if (math.lengthsq(a) < 1e-4f || math.lengthsq(c) < 1e-4f)
                    continue;
                turns.Add(math.degrees(math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(c)), -1f, 1f))));
            }
            if (turns.Count == 0)
                return new[] { 0f, 0f, 0f };
            turns.Sort();
            return new[] { turns.Count, turns[turns.Count - 1], turns[(int)(turns.Count * 0.99f)] };
        }

        /// <summary>Fireflies and butterflies per 100 m² over the critter tiles within radius of the player, and the tile count.</summary>
        public static Vector3 CritterDensity(float radius)
        {
            var critters = Object.FindAnyObjectByType<CritterSystem>();
            return critters != null ? (Vector3)critters.DensityNear(((float3)PlayerPosition()).xz, radius) : Vector3.zero;
        }

        /// <summary>Creatures (not the player's body) within radius of the player: [all, upright folk with hands].</summary>
        public static int[] CreaturesNear(float radius)
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var player = (float3)PlayerPosition();
            int all = 0, folk = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Mode == CreatureMode.Puppet || math.distance(states[i].Position.xz, player.xz) > radius)
                    continue;
                all++;
                ref var b = ref bodies[i].Blob.Value;
                if (b.Upright && b.Hands)
                    folk++;
            }
            return new[] { all, folk };
        }

        /// <summary>
        /// Puts the player beside the n-th outdoor folk within 80 m (wrapping), offset a few metres the way of the
        /// angle, so walk-by talk gets a chance; some spots land the other side of a wall. False if there are none.
        /// </summary>
        public static bool BesideFolk(int n, float angle, float distance)
        {
            if (!Source(out var s))
                return false;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var player = (float3)PlayerPosition();
            var near = new List<float3>();
            for (int i = 0; i < states.Length; i++)
            {
                ref var b = ref bodies[i].Blob.Value;
                if (states[i].Mode != CreatureMode.Puppet && states[i].Mode != CreatureMode.Home && b.Upright && b.Hands &&
                    math.distance(states[i].Position.xz, player.xz) < 80f)
                    near.Add(states[i].Position);
            }
            if (near.Count == 0)
                return false;
            var at = near[n % near.Count];
            var xz = at.xz + new float2(math.cos(angle), math.sin(angle)) * distance;
            using var pq = EM.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!pq.TryGetSingletonEntity<LocalTransform>(out var e))
                return false;
            var t = EM.GetComponentData<LocalTransform>(e);
            t.Position = new float3(xz.x, math.max(s.SampleHeight(xz), at.y), xz.y);
            EM.SetComponentData(e, t);
            return true;
        }

        static float scannedTo = -1f;

        /// <summary>
        /// Walk-by folk talk since the last call: [new lines up, of those with a kit wall between speaker and player,
        /// of those with an unfilled {slot} left, lines held back by a wall so far].
        /// </summary>
        public static int[] FolkTalkScan()
        {
            int fresh = 0, walled = 0, unfilled = 0;
            using var kq = EM.CreateEntityQuery(ComponentType.ReadOnly<KitBox>());
            var hasKit = kq.TryGetSingletonBuffer<KitBox>(out var kit, true) && kit.Length > 0;
            var player = (float3)PlayerPosition();
            var latest = scannedTo;
            foreach (var said in FolkTalkSystem.Shown)
            {
                if (said.Start <= scannedTo)
                    continue;
                latest = math.max(latest, said.Start);
                fresh++;
                if (said.Text.IndexOf('{') >= 0 || said.Text.IndexOf('}') >= 0)
                    unfilled++;
                if (hasKit && EM.Exists(said.Who) && EM.HasComponent<CreatureState>(said.Who) && EM.HasComponent<CreatureBody>(said.Who))
                {
                    var s = EM.GetComponentData<CreatureState>(said.Who);
                    var body = EM.GetComponentData<CreatureBody>(said.Who);
                    if (FolkTalkSystem.Walled(kit, s, ref body.Blob.Value, player))
                        walled++;
                }
            }
            scannedTo = latest;
            return new[] { fresh, walled, unfilled, FolkTalkSystem.HeldBack };
        }

        /// <summary>
        /// Picks lines for an outdoor speaker of each culture with lines of its own, n times each, at noon, to a player
        /// of playerSpecies (null: the same kind). [cultures, picks, picks with an unfilled {slot}, picks from Any's lines,
        /// worst culture's Any share]; the worst culture's name is in WorstAnyCulture.
        /// </summary>
        public static float[] DialogueStats(int n, string playerSpecies)
        {
            var archive = DialogueArchive.Current;
            if (archive == null || !archive.Cultures.TryGetValue("Any", out var any))
                return new float[6];
            var anyLines = Templates(any);
            var rng = new Unity.Mathematics.Random(97u);
            int cultures = 0, picks = 0, unfilled = 0, fromAny = 0;
            var worst = 0f;
            WorstAnyCulture = null;
            foreach (var pair in archive.Cultures)
            {
                if (pair.Value == any || pair.Value.Lines.Count == 0)
                    continue;
                var own = Templates(pair.Value);
                int mine = 0, shared = 0;
                var who = new DialogueArchive.Speaker { Species = pair.Key, PlayerSpecies = playerSpecies, Hour = 12f };
                for (int k = 0; k < n; k++)
                {
                    var text = archive.Pick(who, ref rng);
                    if (text == null)
                        continue;
                    picks++;
                    if (text.IndexOf('{') >= 0 || text.IndexOf('}') >= 0)
                        unfilled++;
                    // (A text matching an Any line and none of its own counts as Any's.)
                    if (!Matches(own, text) && Matches(anyLines, text))
                        shared++;
                    else
                        mine++;
                }
                if (mine + shared == 0)
                    continue;
                cultures++;
                fromAny += shared;
                var share = shared / (float)(mine + shared);
                if (share > worst)
                {
                    worst = share;
                    WorstAnyCulture = pair.Key;
                }
            }
            return new float[] { cultures, picks, unfilled, fromAny, worst };
        }

        public static string WorstAnyCulture;

        /// <summary>
        /// A digest of n folk personas (A-93), each trait quantised to 1/256, individuals spread as spawns spread them:
        /// the same on every run. Its bands are in PersonaBands.
        /// </summary>
        public static string PersonaDigest(int n)
        {
            var h = 2166136261u;
            for (int i = 0; i < n; i++)
            {
                var p = FolkPersona.Of(PersonaIndividual(i));
                for (int t = 0; t < FolkPersona.Count; t++)
                    h = (h ^ (uint)(int)math.floor(p[(PersonaTrait)t] * 256f)) * 16777619u;
            }
            return h.ToString("X8");
        }

        static uint PersonaIndividual(int i) => (uint)i * 2654435761u + 12345u;

        /// <summary>
        /// Over n personas: per trait the share in its high band and its low band ([high0, low0, high1, low1, ...]), then
        /// the share whose mood band disagrees with FolkJobs.Mood (should be 0: the talk and the walk-by lines agree).
        /// </summary>
        public static float[] PersonaBands(int n)
        {
            var r = new float[FolkPersona.Count * 2 + 1];
            for (int i = 0; i < n; i++)
            {
                var ind = PersonaIndividual(i);
                var p = FolkPersona.Of(ind);
                for (int t = 0; t < FolkPersona.Count; t++)
                {
                    var b = p.Band((PersonaTrait)t);
                    if (b == 2) r[t * 2] += 1f / n;
                    if (b == 0) r[t * 2 + 1] += 1f / n;
                }
                if (p.MoodBand != FolkJobs.Mood(ind))
                    r[FolkPersona.Count * 2] += 1f / n;
            }
            return r;
        }

        /// <summary>
        /// Every topic asked of made-up folk of every culture in dialogue.json (and of no culture), many individuals, at
        /// all hours, in every condition, with and without a home and a job, to a player of playerSpecies (null: its own
        /// kind; beast: an animal player). [cultures, answers, empty answers, unfilled {slots}, exceptions, help answers
        /// offering a quest, quests with no id]. The first exception's message is in SweepError.
        /// </summary>
        public static float[] ConversationSweep(string playerSpecies, bool beast)
        {
            SweepError = null;
            FolkConversation.ForgetAsks();
            var cultures = new List<string> { null };
            var archive = DialogueArchive.Current;
            if (archive != null)
                cultures.AddRange(archive.Cultures.Keys);
            int answers = 0, empty = 0, unfilled = 0, errors = 0, quests = 0, noId = 0;
            var conditions = new[] { FolkCondition.None, FolkCondition.Tired | FolkCondition.Night, FolkCondition.Busy, FolkCondition.Hungry,
                                     FolkCondition.Thirsty, FolkCondition.Lonely, FolkCondition.Scared, FolkCondition.Weather };
            var stances = (FolkStance[])System.Enum.GetValues(typeof(FolkStance));
            foreach (var culture in cultures)
                for (int i = 0; i < 24; i++)
                {
                    var ind = PersonaIndividual(i);
                    var cond = conditions[i % conditions.Length];
                    var who = new FolkTalker
                    {
                        Species = culture,
                        Job = i % 3 == 0 ? null : "Smith",
                        HomeTown = i % 2 == 0 ? "Testford" : null,
                        PlayerSpecies = playerSpecies,
                        Beast = beast,
                        Individual = ind,
                        Persona = FolkPersona.Of(ind),
                        State = new FolkState { Flags = cond, Weather = cond == FolkCondition.Weather ? ComfortKind.Rain : ComfortKind.None, Hour = (i * 5) % 24 },
                        Stance = stances[i % stances.Length],
                    };
                    try
                    {
                        var topics = FolkConversation.Topics(who);
                        // (Every topic, then favours again until it's pestered.)
                        foreach (var t in topics)
                            Count(FolkConversation.Ask(who, t.Id));
                        foreach (var (id, _) in FolkConversation.AllTopics)
                            Count(FolkConversation.Ask(who, id));
                        for (int k = 0; k < 3; k++)
                            Count(FolkConversation.Ask(who, FolkConversation.Favour));
                    }
                    catch (System.Exception e)
                    {
                        errors++;
                        SweepError ??= $"{culture ?? "(none)"} #{i}: {e}";
                    }
                }
            FolkConversation.ForgetAsks();
            return new float[] { cultures.Count, answers, empty, unfilled, errors, quests, noId };

            void Count(Answer a)
            {
                answers++;
                if (string.IsNullOrEmpty(a.Text) || a.Text == "...")
                    empty++;
                if (a.Text != null && (a.Text.IndexOf('{') >= 0 || a.Text.IndexOf('}') >= 0))
                    unfilled++;
                if (a.Offer.Kind == OfferKind.Quest)
                {
                    quests++;
                    if (string.IsNullOrEmpty(a.Offer.Id))
                        noId++;
                }
            }
        }

        public static string SweepError;

        /// <summary>
        /// One made-up town of n folk of a culture (a village's mix of jobs, most folk at ease, a few busy, tired, hungry or
        /// cold), each asked every topic in topics (comma separated), to a player of its own kind: [folk whose answers
        /// differ from every other's, folk, distinct lines over all topics, then distinct lines per topic]. TownTalkText:
        /// the first dump folk, persona and answers.
        /// </summary>
        public static float[] TownTalk(string culture, int n, string topics, int dump)
        {
            FolkConversation.ForgetAsks();
            var ids = topics.Split(',');
            var jobs = new[] { "Farmer", "Baker", null, "Smith", "Guard", "Herder", "Healer", null, "Trader", "Fisher" };
            var conditions = new[] { FolkCondition.None, FolkCondition.None, FolkCondition.Busy, FolkCondition.None, FolkCondition.Tired,
                                     FolkCondition.None, FolkCondition.Hungry, FolkCondition.Weather, FolkCondition.None, FolkCondition.Lonely };
            var perTopic = new HashSet<string>[ids.Length];
            for (int t = 0; t < ids.Length; t++)
                perTopic[t] = new HashSet<string>();
            var all = new HashSet<string>();
            var sets = new List<string>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var ind = PersonaIndividual(1000 + i);
                var cond = conditions[i % conditions.Length];
                var who = new FolkTalker
                {
                    Species = culture,
                    Job = jobs[i % jobs.Length],
                    HomeTown = "Millfield",
                    Individual = ind,
                    Persona = FolkPersona.Of(ind),
                    State = new FolkState { Flags = cond, Weather = cond == FolkCondition.Weather ? ComfortKind.Cold : ComfortKind.None, Hour = 10f + i % 8 },
                };
                if (i < dump)
                    sb.Append($"{culture} #{ind:X8} {who.Job ?? "-"} | {who.Persona} | {who.State}\n");
                var set = new System.Text.StringBuilder();
                for (int t = 0; t < ids.Length; t++)
                {
                    var a = FolkConversation.Ask(who, ids[t]);
                    perTopic[t].Add(a.Text);
                    all.Add(a.Text);
                    set.Append(a.Text).Append('|');
                    if (i < dump)
                        sb.Append("  ").Append(a).Append('\n');
                }
                sets.Add(set.ToString());
            }
            FolkConversation.ForgetAsks();
            TownTalkText = sb.ToString();
            var unique = 0;
            foreach (var s in sets)
                if (sets.FindAll(o => o == s).Count == 1)
                    unique++;
            var r = new List<float> { unique, n, all.Count };
            foreach (var p in perTopic)
                r.Add(p.Count);
            return r.ToArray();
        }

        public static string TownTalkText;

        /// <summary>
        /// A-93 row 3: n made-up folk (Human, every persona, a few wary, annoyed, warm or kin) met by an animal of each
        /// group in groups (comma separated, as FolkConversation.GroupOf names them): per group, how many took it each
        /// way, by BeastReaction (None, Pet, Puzzled, Wary, Scared, Repelled, Awed). BeastMeetText: the group each live
        /// animal species is in, every distinct line per group with its reaction and response, then the first dump folk
        /// met as each group.
        /// </summary>
        public static float[] BeastMeet(string groups, int n, int dump)
        {
            var ids = groups.Split(',');
            var reactions = System.Enum.GetValues(typeof(BeastReaction)).Length;
            var r = new float[ids.Length * reactions];
            var sb = new System.Text.StringBuilder();
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director != null)
            {
                sb.Append("groups:");
                for (int i = 0; i < director.SpeciesCount; i++)
                {
                    var sp = director.SpeciesAt(i);
                    if (sp != null && !sp.Body.Upright)
                        sb.Append(' ').Append(sp.name).Append('=').Append(FolkConversation.GroupOf(sp));
                }
                sb.Append('\n');
            }
            var lines = new SortedSet<string>();
            var stances = new[] { FolkStance.Neutral, FolkStance.Neutral, FolkStance.Neutral, FolkStance.Neutral, FolkStance.Wary,
                                  FolkStance.Neutral, FolkStance.Warm, FolkStance.Neutral, FolkStance.Annoyed, FolkStance.Kin };
            var dumpText = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var ind = PersonaIndividual(2000 + i);
                var who = new FolkTalker
                {
                    Species = "Human",
                    BodySpecies = "Human",
                    HomeTown = i % 3 == 0 ? null : "Millfield",
                    Beast = true,
                    Individual = ind,
                    Persona = FolkPersona.Of(ind),
                    State = new FolkState { Hour = 9f + i % 10 },
                    Stance = stances[i % stances.Length],
                };
                if (i < dump)
                    dumpText.Append($"Human #{ind:X8} | {who.Persona} | stance {who.Stance}\n");
                for (int g = 0; g < ids.Length; g++)
                {
                    who.PlayerGroup = ids[g];
                    who.PlayerSpecies = ids[g] == "small" ? "Rabbit" : ids[g] == "predator" ? "Wolf" : ids[g] == "livestock" ? "Horse"
                        : ids[g] == "vermin" ? "Tarantula" : ids[g] == "big" ? "Bison" : ids[g] == "pet" ? "Dog" : "Deer";
                    var a = FolkConversation.Ask(who, FolkConversation.Meet);
                    r[g * reactions + (int)a.Reaction]++;
                    lines.Add($"{ids[g]} {a.Reaction}/{a.Response}: {a.Text}");
                    if (i < dump)
                        dumpText.Append("  as ").Append(who.PlayerSpecies).Append(": ").Append(a).Append('\n');
                }
            }
            sb.Append($"{lines.Count} distinct lines\n");
            foreach (var l in lines)
                sb.Append("  ").Append(l).Append('\n');
            sb.Append(dumpText);
            BeastMeetText = sb.ToString();
            return r;
        }

        public static string BeastMeetText;

        /// <summary>
        /// A-93 row 3: the live folk nearest the player sent off with a beast response (Flee, BackOff, Approach) as if the
        /// player were an animal right there: [its Individual, 1 if FolkConversation.Respond took it]. FolkMode then
        /// tells how its brain took it.
        /// </summary>
        public static float[] BeastRespond(string response)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var at = (float3)PlayerPosition();
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var best = Entity.Null;
            var bestD = float.MaxValue;
            foreach (var e in entities)
            {
                var d = math.distance(EM.GetComponentData<CreatureState>(e).Position, at);
                if (d < bestD && FolkConversation.TryTalker(world, e, null, out _))
                {
                    best = e;
                    bestD = d;
                }
            }
            if (best == Entity.Null || !System.Enum.TryParse(response, true, out BeastResponse kind))
                return new float[] { 0f, 0f };
            var s = EM.GetComponentData<CreatureState>(best);
            // (The "animal" stands 3 m off it, toward the player.)
            var from = s.Position + math.normalizesafe(at - s.Position, new float3(1f, 0f, 0f)) * 3f;
            var ok = FolkConversation.Respond(best, new Answer { Reaction = BeastReaction.Scared, Response = kind }, from);
            BeastRespondFolk = s.Individual;
            UnityEngine.Debug.Log($"BeastRespond: {response} to species {EM.GetComponentData<CreatureBody>(best).Species} ({UnityEngine.Object.FindAnyObjectByType<FaunaDirector>()?.SpeciesName(EM.GetComponentData<CreatureBody>(best).Species)}) task {s.Task} leash {s.Leash:F0} at {bestD:F1} m");
            return new float[] { s.Individual, ok ? 1f : 0f };
        }

        public static uint BeastRespondFolk;

        /// <summary>The mode and distance from the player (m) of the folk BeastRespond sent off ("Flee 12.3"), or "" if gone.</summary>
        public static string FolkMode()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var s = EM.GetComponentData<CreatureState>(e);
                if (s.Individual == BeastRespondFolk)
                    return System.FormattableString.Invariant($"{s.Mode} {math.distance(s.Position, (float3)PlayerPosition()):F1}");
            }
            return "";
        }

        static uint beastLiveFolk;

        /// <summary>
        /// A-93 row 3 shots: the same live folk (the talker nearest the player on the first call after first = true) met by
        /// the player's body now: the player put 2.5 m in front of it (facing it), and its answer to topic (meet for an
        /// animal body; a human gets who). Returns [folk x, y, z, head height, player x, z], or empty if no folk;
        /// BeastLiveText: "Body: answer".
        /// </summary>
        public static float[] BeastLive(bool first)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var at = (float3)PlayerPosition();
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var best = Entity.Null;
            var bestD = float.MaxValue;
            foreach (var e in entities)
            {
                var st = EM.GetComponentData<CreatureState>(e);
                var d = first ? math.distance(st.Position, at) : st.Individual == beastLiveFolk ? 0f : float.MaxValue;
                if (d < bestD && FolkConversation.TryTalker(world, e, null, out _))
                {
                    best = e;
                    bestD = d;
                }
            }
            if (best == Entity.Null)
                return new float[0];
            var s = EM.GetComponentData<CreatureState>(best);
            beastLiveFolk = s.Individual;
            FolkConversation.TryTalker(world, best, null, out var who);
            var a = FolkConversation.Ask(who, who.Beast ? FolkConversation.Meet : FolkConversation.Who);
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            BeastLiveText = $"{(director != null && director.PlayerSpecies != null ? director.PlayerSpecies.name : "?")}: {a}";
            var ahead = math.normalizesafe(new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw)), new float3(0f, 0f, 1f));
            var spot = s.Position + ahead * 2.5f;
            MovePlayer(spot.x - at.x, spot.z - at.z);
            var height = EM.GetComponentData<CreatureBody>(best).Blob.Value.StandHeight * s.Scale;
            return new float[] { s.Position.x, s.Position.y, s.Position.z, height, spot.x, spot.z };
        }

        public static string BeastLiveText;

        /// <summary>
        /// Talks to up to n live folk near the player (the real entity path: persona, condition, home town, stance): one
        /// line per folk with its persona and condition, then each topic's answer. Empty when no folk is near.
        /// </summary>
        public static string TalkToFolk(int n)
        {
            var sb = new System.Text.StringBuilder();
            var world = World.DefaultGameObjectInjectionWorld;
            var at = PlayerPosition();
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var near = new List<(float, Entity)>();
            foreach (var e in entities)
            {
                var s = EM.GetComponentData<CreatureState>(e);
                var d = math.distance(s.Position, (float3)at);
                if (d < 200f && FolkConversation.TryTalker(world, e, null, out _))
                    near.Add((d, e));
            }
            near.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            for (int k = 0; k < near.Count && k < n; k++)
            {
                if (!FolkConversation.TryTalker(world, near[k].Item2, null, out var who))
                    continue;
                sb.Append($"{who.Species} #{who.Individual:X8} job {who.Job ?? who.Trade ?? "-"} town {who.HomeTown ?? "-"} stance {who.Stance}\n  {who.Persona}\n  state {who.State} hour {who.State.Hour:F1}\n");
                foreach (var t in FolkConversation.Topics(near[k].Item2))
                    sb.Append("  ").Append(FolkConversation.Ask(near[k].Item2, t.Id)).Append('\n');
            }
            FolkConversation.ForgetAsks();
            return sb.ToString();
        }

        /// <summary>
        /// A-69: dry land where a biome is the dominant one, sampled every 32 m over the island: [km², cells, then the
        /// x,z of up to 8 cells spread through it (every n-th)]. Empty if the world isn't up or the biome isn't there.
        /// </summary>
        public static float[] BiomeArea(string name)
        {
            if (!Source(out var s))
                return new float[0];
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (q.CalculateEntityCount() == 0)
                return new float[0];
            var palette = q.GetSingleton<BiomePalette>();
            var id = System.Array.FindIndex(palette.Biomes, b => b != null && b.name == name);
            if (id < 0)
                return new float[0];
            const float step = 32f;
            var half = s.World.WorldSize * 0.5f;
            var cells = new List<float2>();
            for (var x = -half; x < half; x += step)
                for (var z = -half; z < half; z += step)
                {
                    var g = s.SampleGround(new float2(x, z));
                    if (!g.HasWater && g.Biomes.Dominant() == id)
                        cells.Add(new float2(x, z));
                }
            var result = new List<float> { cells.Count * step * step / 1e6f, cells.Count };
            for (int i = 0, every = math.max(1, cells.Count / 8); i < cells.Count && result.Count < 18; i += every)
            {
                result.Add(cells[i].x);
                result.Add(cells[i].y);
            }
            return result.ToArray();
        }

        /// <summary>
        /// W-126: times FloraPlacement.Place over a square of cells (half-width m) on the shrub and tree layers (the Burst-free
        /// managed path, so only compare runs of the same build): [ms, plants placed, cells asked]. Run twice; read the second.
        /// </summary>
        public static float[] FloraPlaceCost(float cx, float cz, float half)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (!Source(out var s) || director == null || !director.FloraPlacementBlob.IsCreated)
                return new float[0];
            ref var f = ref director.FloraPlacementBlob.Value;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            float placed = 0f, asked = 0f;
            for (int layer = 1; layer < 3; layer++)
            {
                var size = f.CellSizes[layer];
                var lo = (int2)math.floor(new float2(cx - half, cz - half) / size);
                var hi = (int2)math.floor(new float2(cx + half, cz + half) / size);
                for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        asked++;
                        if (FloraPlacement.Place(ref f, s, new int2(x, y), layer).Model >= 0)
                            placed++;
                    }
            }
            return new[] { (float)clock.Elapsed.TotalMilliseconds, placed, asked };
        }

        /// <summary>
        /// W-126: where one species stands in a square (every layer): [plants, mean sun-facing (+1 sunny slope, -1 shaded) over
        /// the slopes it stands on, mean relief (+ crest, - hollow), the same two means over the whole square's ground (every 8 m),
        /// plants per 100 m2, share of 32 m blocks holding at least one]. Empty if the species or world is missing.
        /// </summary>
        public static float[] FloraHabitat(string species, float cx, float cz, float half)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (!Source(out var s) || director == null || !director.FloraPlacementBlob.IsCreated)
                return new float[0];
            var index = -1;
            for (int i = 0; i < director.FloraSpeciesList.Count; i++)
                if (director.FloraSpeciesList[i] != null && director.FloraSpeciesList[i].name == species)
                    index = i;
            if (index < 0)
                return new float[0];
            ref var f = ref director.FloraPlacementBlob.Value;
            var layer = (int)director.FloraSpeciesList[index].Layer;
            var size = f.CellSizes[layer];
            var lo = (int2)math.floor(new float2(cx - half, cz - half) / size);
            var hi = (int2)math.floor(new float2(cx + half, cz + half) / size);
            float n = 0f, sun = 0f, sunN = 0f, relief = 0f;
            var blocks = new HashSet<int2>();
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                {
                    var plant = FloraPlacement.Place(ref f, s, new int2(x, y), layer);
                    if (plant.Model < f.Rules[index].FirstModel || plant.Model >= f.Rules[index].FirstModel + f.Rules[index].Variants)
                        continue;
                    var xz = plant.Position.xz;
                    n++;
                    relief += FloraPlacement.Relief(s, xz);
                    var sf = FloraPlacement.SunFacing(s.SampleNormal(xz, 1f));
                    if (sf.y > 0f)
                    {
                        sun += sf.x;
                        sunN++;
                    }
                    blocks.Add((int2)math.floor(xz / 32f));
                }
            float gSun = 0f, gSunN = 0f, gRelief = 0f, gN = 0f, nonEmpty = 0f;
            for (var x = cx - half; x < cx + half; x += 8f)
                for (var z = cz - half; z < cz + half; z += 8f)
                {
                    var at = new float2(x, z);
                    gN++;
                    gRelief += FloraPlacement.Relief(s, at);
                    var sf = FloraPlacement.SunFacing(s.SampleNormal(at, 1f));
                    if (sf.y > 0f)
                    {
                        gSun += sf.x;
                        gSunN++;
                    }
                }
            var allBlocks = (int)(math.ceil(2f * half / 32f) * math.ceil(2f * half / 32f));
            nonEmpty = blocks.Count / (float)math.max(1, allBlocks);
            return new[] { n, sun / math.max(1f, sunN), relief / math.max(1f, n), gSun / math.max(1f, gSunN), gRelief / math.max(1f, gN),
                           n / (4f * half * half / 100f), nonEmpty };
        }

        /// <summary>
        /// W-126: every plant in a square as a plan view, [x, z, layer, species index] per plant, for clearing and grove maps.
        /// Plants too small to read at plan scale (layer 0) are thinned to one in four.
        /// </summary>
        public static float[] FloraPlan(float cx, float cz, float half)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (!Source(out var s) || director == null || !director.FloraPlacementBlob.IsCreated)
                return new float[0];
            ref var f = ref director.FloraPlacementBlob.Value;
            var list = new List<float>();
            for (int layer = 0; layer < 3; layer++)
            {
                var size = f.CellSizes[layer];
                var lo = (int2)math.floor(new float2(cx - half, cz - half) / size);
                var hi = (int2)math.floor(new float2(cx + half, cz + half) / size);
                for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        if (layer == 0 && ((x + 2 * y) & 3) != 0)
                            continue;
                        var plant = FloraPlacement.Place(ref f, s, new int2(x, y), layer);
                        if (plant.Model < 0)
                            continue;
                        list.Add(plant.Position.x);
                        list.Add(plant.Position.z);
                        list.Add(layer);
                        var sp = 0;
                        for (int i = 0; i < f.Rules.Length; i++)
                            if (plant.Model >= f.Rules[i].FirstModel && plant.Model < f.Rules[i].FirstModel + f.Rules[i].Variants)
                                sp = i;
                        list.Add(sp);
                    }
            }
            return list.ToArray();
        }

        /// <summary>W-126: ground heights over a square as an n by n grid (row-major from the south-west corner), for framing shots.</summary>
        public static float[] FloraHeights(float cx, float cz, float half, float n)
        {
            if (!Source(out var s))
                return new float[0];
            var count = (int)n;
            var grid = new float[count * count];
            for (int y = 0; y < count; y++)
                for (int x = 0; x < count; x++)
                    grid[y * count + x] = s.SampleHeight(new float2(cx - half + (x + 0.5f) * 2f * half / count, cz - half + (y + 0.5f) * 2f * half / count));
            return grid;
        }

        /// <summary>W-126: ground over a square as an n by n grid (row-major from the south-west corner): 0 dry, 1 water, plus 2 x the dominant biome index.</summary>
        public static float[] FloraGroundMask(float cx, float cz, float half, float n)
        {
            if (!Source(out var s))
                return new float[0];
            var count = (int)n;
            var grid = new float[count * count];
            for (int y = 0; y < count; y++)
                for (int x = 0; x < count; x++)
                {
                    var g = s.SampleGround(new float2(cx - half + (x + 0.5f) * 2f * half / count, cz - half + (y + 0.5f) * 2f * half / count));
                    grid[y * count + x] = (g.HasWater ? 1f : 0f) + 2f * g.Biomes.Dominant();
                }
            return grid;
        }

        /// <summary>T-1 row 3: starts a new run for the contact sheet (forgets the shots written so far).</summary>
        public static bool ShotRunStart()
        {
            ShotTool.ClearTaken();
            return true;
        }

        /// <summary>
        /// T-1 row 3: the contact sheet of the run's shots at path (labels[i] names the i-th shot), and each shot's written
        /// check: "label: check; good" or "label: check; BAD reason". The sheet's size in KB is the first line.
        /// </summary>
        public static string[] ShotRunSheet(string path, string[] labels)
        {
            var bytes = ShotSheet.Write(path, ShotTool.Written, labels);
            var lines = new List<string> { $"{bytes / 1024} KB" };
            for (int i = 0; i < ShotTool.Written.Count; i++)
            {
                var t = ShotTool.Written[i];
                lines.Add($"{(i < labels.Length ? labels[i] : System.IO.Path.GetFileName(t.Path))}: {t.Check}; {(t.Bad == null ? "good" : "BAD " + t.Bad)}");
            }
            return lines.ToArray();
        }

        /// <summary>
        /// W-126: the liveliest ground inside a biome for habitat shots: the dry spot (64 m grid, the biome dominant at it and round
        /// it) with the widest spread of heights over a 90 m ring. [x, y, z, spread], empty if none. A same-world pick, so old and new shots frame alike.
        /// </summary>
        public static float[] FloraViewSpot(string biome)
        {
            var palette = Palette();
            if (!Source(out var s) || palette == null)
                return new float[0];
            var id = System.Array.FindIndex(palette.Biomes, b => b != null && b.name == biome);
            if (id < 0)
                return new float[0];
            var half = s.World.WorldSize * 0.5f;
            var best = new float[0];
            for (var x = -half + 200f; x < half - 200f; x += 64f)
                for (var z = -half + 200f; z < half - 200f; z += 64f)
                {
                    var at = new float2(x, z);
                    var g = s.SampleGround(at);
                    if (g.HasWater || g.Biomes.Dominant() != id || s.SampleGround(at + new float2(90f, 0f)).Biomes.Dominant() != id ||
                        s.SampleGround(at - new float2(90f, 0f)).Biomes.Dominant() != id)
                        continue;
                    float lo = g.Height, hi = g.Height;
                    for (int k = 0; k < 8; k++)
                    {
                        var a = k * math.PI / 4f;
                        var h = s.SampleHeight(at + 90f * new float2(math.cos(a), math.sin(a)));
                        lo = math.min(lo, h);
                        hi = math.max(hi, h);
                    }
                    if (best.Length == 0 || hi - lo > best[3])
                        best = new[] { x, g.Height, z, hi - lo };
                }
            return best;
        }

        /// <summary>A-69: creatures of one species (asset name) within radius of the player: [count, nearest distance (or -1), its x, y, z].</summary>
        public static float[] SpeciesNear(string name, float radius)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex(name) : -1;
            var result = new float[] { 0f, -1f, 0f, 0f, 0f };
            if (index < 0)
                return result;
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var player = (float3)PlayerPosition();
            for (int i = 0; i < states.Length; i++)
            {
                var d = math.distance(states[i].Position.xz, player.xz);
                if (bodies[i].Species != index || states[i].Mode == CreatureMode.Puppet || d > radius)
                    continue;
                result[0]++;
                if (result[1] < 0f || d < result[1])
                {
                    result[1] = d;
                    result[2] = states[i].Position.x;
                    result[3] = states[i].Position.y;
                    result[4] = states[i].Position.z;
                }
            }
            return result;
        }

        /// <summary>
        /// A-69: switches the player's body to a playable species by asset name, as the Character tab does, and returns
        /// the body it had ("" for none). A switch mid-session is saved as next session's body: put the old one back
        /// with SetBody, then the saved pick with PutBodyPref(BodyPref() read before).
        /// </summary>
        public static string SetBody(string name)
        {
            var avatars = Object.FindAnyObjectByType<Punchy.Net.NetAvatars>();
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            var was = director != null && director.PlayerSpecies != null ? director.PlayerSpecies.name : "";
            if (avatars != null)
                avatars.SetLocalSpecies(avatars.PlayableSpecies.FindIndex(s => s != null && s.name == name));
            return was;
        }

        const string BodyPrefKey = "Punchy.Character.Body";

        public static string BodyPref() => PlayerPrefs.GetString(BodyPrefKey, "");

        public static void PutBodyPref(string name)
        {
            if (string.IsNullOrEmpty(name))
                PlayerPrefs.DeleteKey(BodyPrefKey);
            else
                PlayerPrefs.SetString(BodyPrefKey, name);
            PlayerPrefs.Save();
        }

        /// <summary>The player's body flies (PlayerBody.Flies).</summary>
        public static bool BodyFlies()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<PlayerBody>());
            return q.TryGetSingleton<PlayerBody>(out var body) && body.Flies;
        }

        static List<Regex> Templates(DialogueArchive.Culture c)
        {
            var list = new List<Regex>();
            foreach (var line in c.Lines)
                if (!string.IsNullOrEmpty(line.Text))
                    list.Add(new Regex("^" + Regex.Replace(Regex.Escape(line.Text), @"\\\{[^}]*}", ".+?") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline));
            return list;
        }

        static bool Matches(List<Regex> templates, string text)
        {
            foreach (var t in templates)
                if (t.IsMatch(text))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------------------- A-71 row 6: following

        static Entity followed;

        /// <summary>A-71 row 6: the nearest creature of this species (asset name) within radius of the player set
        /// following them (Follow.Start, solo network id 0); its distance from them, or -1 if there's none.</summary>
        public static float FollowStart(string species, float radius)
        {
            var player = (float3)PlayerPosition();
            followed = Follow.Find(species, player, radius);
            if (followed == Entity.Null || !Follow.Start(followed, 0))
            {
                var server = Punchy.Net.NetWorlds.Server;
                var director = Object.FindAnyObjectByType<FaunaDirector>();
                Debug.Log($"FollowStart: no {species} to follow within {radius} m of {player} (found {followed}, world {server?.Name ?? "none"}, " +
                          $"species index {(director != null ? director.SpeciesIndex(species) : -2)})");
                return -1f;
            }
            return math.distance(EM.GetComponentData<CreatureState>(followed).Position.xz, player.xz);
        }

        /// <summary>A-71 row 6: Follow.Stop on the one FollowStart set going.</summary>
        public static bool FollowStop()
        {
            Follow.Stop(followed);
            return followed != Entity.Null;
        }

        /// <summary>A-71 row 6: the follower now: [following (1/0), its distance from the player (m), still in its
        /// follow stage (1/0), its speed (m/s)]. A-88 row 1: then its mode and stage, the smoothed frame time (s), its
        /// position (x, z) and the player's (x, z), so a lagging run shows why.</summary>
        public static float[] FollowStatus()
        {
            if (followed == Entity.Null || !EM.Exists(followed))
                return new float[] { 0f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
            var s = EM.GetComponentData<CreatureState>(followed);
            return new float[]
            {
                Follow.IsFollowing(followed) ? 1f : 0f,
                math.distance(s.Position.xz, ((float3)PlayerPosition()).xz),
                s.Mode == CreatureMode.Greet && s.Stage == 2 ? 1f : 0f,
                s.Speed,
                (float)s.Mode, s.Stage, Time.smoothDeltaTime,
                s.Position.x, s.Position.z, PlayerPosition().x, PlayerPosition().z
            };
        }

        /// <summary>A-71 row 6: of 16 ways out from the player, the one whose steepest 2 m step over `length` metres is
        /// gentlest and dry (a unit [x, z]), so a test walk isn't up a slope too steep for an animal to keep up on.</summary>
        public static float[] FlatWay(float length)
        {
            if (!Source(out var source))
                return new float[] { 1f, 0f };
            var from = ((float3)PlayerPosition()).xz;
            var best = new float2(1f, 0f);
            var bestSteep = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                var angle = k * math.PI / 8f;
                var dir = new float2(math.sin(angle), math.cos(angle));
                var steep = 0f;
                var last = source.SampleHeight(from);
                for (float d = 2f; d <= length + 4f; d += 2f)
                {
                    var xz = from + dir * d;
                    var g = source.SampleGround(xz);
                    if (g.HasWater && g.WaterSurface - g.Height > 0f)
                        steep = float.MaxValue;
                    var h = source.SampleHeight(xz);
                    steep = math.max(steep, math.abs(h - last) / 2f);
                    last = h;
                }
                if (steep < bestSteep)
                {
                    bestSteep = steep;
                    best = dir;
                }
            }
            return new float[] { best.x, best.y };
        }

        /// <summary>A-71 row 6: the player moved by dx, dz on the ground (a test's walk, a step at a time).</summary>
        public static bool MovePlayer(float dx, float dz)
        {
            if (!Source(out var source))
                return false;
            using var pq = EM.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!pq.TryGetSingletonEntity<LocalTransform>(out var e))
                return false;
            var t = EM.GetComponentData<LocalTransform>(e);
            var xz = t.Position.xz + new float2(dx, dz);
            t.Position = new float3(xz.x, source.SampleHeight(xz), xz.y);
            EM.SetComponentData(e, t);
            return true;
        }

        // ------------------------------------------------------------------------------- A-92 row 1: lying legs (#231)

        /// <summary>A-92 row 1: a dry spot within radius of the player whose ground slope (rise over run) is nearest the
        /// one asked for (0: the flattest): [x, z, its slope, the downhill heading (radians)].</summary>
        public static float[] SlopeSpot(float slope, float radius)
        {
            if (!Source(out var source))
                return new float[] { 0f, 0f, -1f, 0f };
            var from = ((float3)PlayerPosition()).xz;
            var best = new float[] { from.x, from.y, -1f, 0f };
            var bestMiss = float.MaxValue;
            for (float x = -radius; x <= radius; x += 3f)
                for (float z = -radius; z <= radius; z += 3f)
                {
                    var at = from + new float2(x, z);
                    var g = source.SampleGround(at);
                    if (g.HasWater && g.WaterSurface > g.Height - 0.5f)
                        continue;
                    var grad = new float2(source.SampleHeight(at + new float2(1f, 0f)) - source.SampleHeight(at - new float2(1f, 0f)),
                                          source.SampleHeight(at + new float2(0f, 1f)) - source.SampleHeight(at - new float2(0f, 1f))) * 0.5f;
                    // (Even ground across the body's length, not a ledge: the slope a metre either way is much the same.)
                    var far = new float2(source.SampleHeight(at + new float2(2f, 0f)) - source.SampleHeight(at - new float2(2f, 0f)),
                                         source.SampleHeight(at + new float2(0f, 2f)) - source.SampleHeight(at - new float2(0f, 2f))) * 0.25f;
                    var miss = math.abs(math.length(grad) - slope) + math.length(far - grad);
                    if (miss < bestMiss)
                    {
                        bestMiss = miss;
                        best = new[] { at.x, at.y, math.length(grad), math.atan2(-grad.x, -grad.y) };
                    }
                }
            return best;
        }

        /// <summary>
        /// A-92 row 1: every land species that lies down (four-legged, not upright, not a flier or swimmer) posed lying at
        /// x, z by CreatureAnimator.Evaluate, at lie weights 0.3 to 1 and four headings: one line per species,
        /// "name clearance where", the lowest any leg segment comes to the ground (m, less its radius; negative: into it).
        /// </summary>
        public static string LieClearance(float x, float z)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null || !Source(out var source))
                return "";
            var lines = new System.Text.StringBuilder();
            var seen = new HashSet<string>();
            for (int i = 0; i < director.SpeciesCount; i++)
            {
                var sp = director.SpeciesAt(i);
                if (sp == null || !seen.Add(sp.name))
                    continue;
                var blob = CreatureBlobBuilder.Build(sp);
                ref var b = ref blob.Value;
                var legs = 0;
                for (int p = 0; p < b.Parts.Length; p++)
                    if (b.Parts[p].Role == LimbRole.Leg && b.Parts[p].Parent < 0)
                        legs++;
                if (b.Upright || legs < 4 || b.Habitat != Habitat.Land || b.Tunnels)
                {
                    blob.Dispose();
                    continue;
                }
                var output = new NativeArray<SegmentInstance>(b.Segments.Length, Allocator.Temp);
                var low = float.MaxValue;
                var worst = "";
                foreach (var lie in new[] { 0.3f, 0.5f, 0.7f, 0.85f, 1f })
                    for (int k = 0; k < 4; k++)
                    {
                        var s = new CreatureState
                        {
                            Position = new float3(x, source.SampleHeight(new float2(x, z)), z),
                            Yaw = k * math.PI / 2f, Scale = 1f, Mode = CreatureMode.Rest, Lie = lie, Clock = 1f
                        };
                        CreatureAnimator.Evaluate(ref b, in s, 0, in source, output, 0);
                        for (int p = 0; p < b.Parts.Length; p++)
                        {
                            ref var part = ref b.Parts[p];
                            if (part.Role != LimbRole.Leg || part.Parent >= 0)
                                continue;
                            for (int j = 0; j < part.SegmentCount; j++)
                            {
                                var g = output[part.FirstSegment + j];
                                if (g.Length <= 0f && g.RadiusStart <= 0f)
                                    continue;
                                var end = g.Position + math.mul(g.Rotation, new float3(0f, 0f, g.Length));
                                for (float t = 0f; t <= 1.001f; t += 0.25f)
                                {
                                    var at = math.lerp(g.Position, end, t);
                                    var r = math.lerp(g.RadiusStart, g.RadiusEnd, t) * g.Flatten;
                                    var clear = at.y - r - source.SampleHeight(at.xz);
                                    if (clear < low)
                                    {
                                        low = clear;
                                        worst = $"lie{lie:0.00}/yaw{k}/leg{p}.{j}";
                                    }
                                }
                            }
                        }
                    }
                lines.Append(sp.name).Append(' ').Append(low.ToString("0.000")).Append(' ').Append(worst).Append('\n');
                output.Dispose();
                blob.Dispose();
            }
            return lines.ToString();
        }

        /// <summary>A-92 row 1: the local player sat (1) or laid (2) down as the interact button would, or got up (0).</summary>
        public static bool RestPlayer(int how)
        {
            var input = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<InteractionInputSystem>();
            if (input == null)
                return false;
            input.Rest = how == 1 ? IdleAction.SitDown : how == 2 ? IdleAction.LieBack : IdleAction.None;
            return true;
        }

        /// <summary>A-92 row 1: the local player's body now: [lie, sit, x, z, speed (m/s), yaw, ground height], or empty if there's none.</summary>
        public static float[] AvatarPose()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<PlayerAvatar>());
            if (!q.TryGetSingleton<CreatureState>(out var s))
                return new float[0];
            return new[] { s.Lie, s.Sit, s.Position.x, s.Position.z, s.Speed, s.Yaw, Source(out var source) ? source.SampleHeight(s.Position.xz) : s.Position.y };
        }

        /// <summary>A-92 row 1: the stick held this way (world XZ, its length how far it's pushed) while on, as
        /// PlayerInputSystem.TestMove; off lets go.</summary>
        public static void HoldMove(float x, float z, bool on) => PlayerInputSystem.TestMove = on ? new float2(x, z) : null;

        static readonly Dictionary<Entity, float3> lastAt = new();
        static double lastAtTime;

        static int Legs(ref CreatureBlob b)
        {
            var legs = 0;
            for (int p = 0; p < b.Parts.Length; p++)
                if (b.Parts[p].Role == LimbRole.Leg && b.Parts[p].Parent < 0)
                    legs++;
            return legs;
        }

        /// <summary>A-92 row 1: of the four-legged creatures (not the player's) still half down (lie or sit over 0.3) now
        /// and at the last call, how many there are and the fastest one's ground speed since then (m/s): [count, speed].
        /// The first call only takes positions.</summary>
        public static float[] LyingDrag()
        {
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(),
                ComponentType.Exclude<PlayerAvatar>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var dt = (float)(Time.timeAsDouble - lastAtTime);
            lastAtTime = Time.timeAsDouble;
            var was = new Dictionary<Entity, float3>(lastAt);
            lastAt.Clear();
            int count = 0;
            var fastest = 0f;
            var who = "";
            for (int i = 0; i < states.Length; i++)
            {
                ref var b = ref bodies[i].Blob.Value;
                if (b.Upright || math.max(states[i].Lie, states[i].Sit) <= 0.3f || states[i].Mode == CreatureMode.Held ||
                    b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats || b.Habitat == Habitat.Flies || states[i].Fly > 0.5f || Legs(ref b) < 4)
                    continue;
                lastAt[entities[i]] = states[i].Position;
                if (dt > 1e-4f && was.TryGetValue(entities[i], out var p))
                {
                    count++;
                    var v = math.distance(p.xz, states[i].Position.xz) / dt;
                    if (v > fastest)
                    {
                        fastest = v;
                        var director = Object.FindAnyObjectByType<FaunaDirector>();
                        var sp = director != null ? director.SpeciesAt(bodies[i].Species) : null;
                        who = $"{(sp != null ? sp.name : bodies[i].Species.ToString())} lie {states[i].Lie:F2} sit {states[i].Sit:F2} " +
                              $"speed {states[i].Speed:F2} mode {states[i].Mode} moved {v * dt:F2} m";
                    }
                }
            }
            if (fastest > 0.15f)
                Debug.Log($"LyingDrag: {fastest:F3} m/s, {who}");
            return new float[] { count, fastest };
        }

        /// <summary>
        /// A-92 row 2: a bench at a graphics preset ("High"), uncapped (no frame cap, no vsync). Returns what BenchEnd
        /// puts back (the three sliders, the frame cap and vsync), since setting a preset saves it for this machine.
        /// </summary>
        public static string BenchStart(string preset)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var was = string.Join(",", Punchy.Settings.GraphicsSettings.View.ToString(inv), Punchy.Settings.GraphicsSettings.Detail.ToString(inv),
                                  Punchy.Settings.GraphicsSettings.Shadows.ToString(inv), Application.targetFrameRate.ToString(inv), QualitySettings.vSyncCount.ToString(inv));
            Punchy.Settings.GraphicsSettings.Set((Punchy.Settings.GraphicsPreset)System.Enum.Parse(typeof(Punchy.Settings.GraphicsPreset), preset));
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            return was;
        }

        /// <summary>A-92 row 2: puts back what BenchStart changed.</summary>
        public static void BenchEnd(string was)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var v = was.Split(',');
            if (v.Length < 5)
                return;
            Punchy.Settings.GraphicsSettings.Set(float.Parse(v[0], inv), float.Parse(v[1], inv), float.Parse(v[2], inv));
            Application.targetFrameRate = int.Parse(v[3], inv);
            QualitySettings.vSyncCount = int.Parse(v[4], inv);
        }

        /// <summary>A-92 row 2: the creature pose job's median frame (ms summed over the workers) since the last call that cleared it.</summary>
        public static float PoseMedian(bool clear)
        {
            var ms = CreatureRenderSystem.PoseMs.ToArray();
            if (clear)
                CreatureRenderSystem.PoseMs.Clear();
            System.Array.Sort(ms);
            return ms.Length > 0 ? (float)ms[ms.Length / 2] : 0f;
        }

        /// <summary>
        /// A-92 row 2: a herd of one species within radius of (x, z), wherever it has wandered: { count, centre x, centre
        /// y, centre z, how far out four in five of it stand, the farthest }, to frame the herd for a bench.
        /// </summary>
        public static float[] HerdAround(string name, float x, float z, float radius)
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            var index = director != null ? director.SpeciesIndex(name) : -1;
            if (index < 0)
                return new float[6];
            using var q = EM.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var from = new float2(x, z);
            var sum = float3.zero;
            var members = new System.Collections.Generic.List<float3>();
            for (int i = 0; i < states.Length; i++)
                if (bodies[i].Species == index && states[i].Mode != CreatureMode.Puppet && math.distance(states[i].Position.xz, from) <= radius)
                {
                    members.Add(states[i].Position);
                    sum += states[i].Position;
                }
            if (members.Count == 0)
                return new float[6];
            var centre = sum / members.Count;
            var spread = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
                spread[i] = math.distance(members[i].xz, centre.xz);
            System.Array.Sort(spread);
            return new float[] { members.Count, centre.x, centre.y, centre.z, spread[(int)(spread.Length * 0.8f)], spread[spread.Length - 1] };
        }

        static bool SameGround(in GroundSample a, in GroundSample b) =>
            a.Height == b.Height && a.River == b.River && a.WaterDepth == b.WaterDepth && a.WaterSurface == b.WaterSurface &&
            math.all(a.Flow == b.Flow) && a.Rapids == b.Rapids && a.CaveWater == b.CaveWater;

        /// <summary>
        /// W-134: the float3 SampleGround against the float2 one. Over n seeded spots across the island: at the ground,
        /// and 5 m over it, the answers must match exactly (the surface pays one compare and nothing else), and 30 m
        /// under it too wherever that isn't cave water. Then every water pool under the ground, 0.3 m under its surface
        /// at the middle, and the middle point of every stream, 0.1 m under: each must read as cave water at its own
        /// level. Returns {spots, surface mismatches, deep spots, deep mismatches, pools, pools read, streams, streams
        /// read, the worst level error in metres}.
        /// </summary>
        public static float[] CaveWaterQuery(int n)
        {
            if (!Source(out var source) || !source.Caves.IsCreated)
                return new float[9];
            var rng = new Unity.Mathematics.Random(0x134u);
            var half = source.World.WorldSize * 0.5f;
            int mismatches = 0, deep = 0, deepMismatches = 0;
            for (int i = 0; i < n; i++)
            {
                var xz = rng.NextFloat2(-half, half);
                var flat = source.SampleGround(xz);
                if (!SameGround(flat, source.SampleGround(new float3(xz.x, flat.Height, xz.y))) ||
                    !SameGround(flat, source.SampleGround(new float3(xz.x, flat.Height + 5f, xz.y))))
                    mismatches++;
                var under = source.SampleGround(new float3(xz.x, flat.Height - 30f, xz.y));
                if (!under.CaveWater)
                {
                    deep++;
                    if (!SameGround(flat, under))
                        deepMismatches++;
                }
            }
            ref var set = ref source.Caves.Value;
            int pools = 0, poolsRead = 0, streams = 0, streamsRead = 0;
            var worst = 0f;
            for (int i = 0; i < set.Pools.Length; i++)
            {
                var c = set.Pools[i].Centre;
                if (set.Pools[i].Lava > 0.5f || c.y >= source.SampleHeight(c.xz))
                    continue;
                pools++;
                var g = source.SampleGround(c - new float3(0f, 0.3f, 0f));
                if (g.CaveWater && g.WaterSurface - g.Height > 0.15f)
                    poolsRead++;
                worst = math.max(worst, g.CaveWater ? math.abs(g.WaterSurface - c.y) : 99f);
            }
            for (int s = 0; s < set.Streams.Length; s++)
            {
                ref var stream = ref set.Streams[s];
                var c = set.StreamPoints[stream.FirstPoint + stream.PointCount / 2].Position;
                if (c.y >= source.SampleHeight(c.xz))
                    continue;
                streams++;
                var g = source.SampleGround(c - new float3(0f, 0.1f, 0f));
                if (g.CaveWater && g.River > 0.5f)
                    streamsRead++;
                worst = math.max(worst, g.CaveWater ? math.abs(g.WaterSurface - c.y) : 99f);
            }
            return new float[] { n, mismatches, deep, deepMismatches, pools, poolsRead, streams, streamsRead, worst };
        }

        /// <summary>
        /// W-134: what the water does round the player (stood in a cave pool by the test): {the float3 sample there reads
        /// cave water (1/0), its depth there, ripples CreatureWakeSystem holds within 3 m, presses WaterPatchSystem made on
        /// the field at its last step}.
        /// </summary>
        public static float[] CaveWaterStir()
        {
            if (!Source(out var source))
                return new float[4];
            float3 at = PlayerPosition();
            var g = source.SampleGround(at + new float3(0f, 0.2f, 0f));
            var world = World.DefaultGameObjectInjectionWorld;
            var wake = world.GetExistingSystemManaged<CreatureWakeSystem>();
            var patch = world.GetExistingSystemManaged<WaterPatchSystem>();
            return new float[]
            {
                g.CaveWater ? 1f : 0f, g.CaveWater ? g.WaterSurface - g.Height : 0f,
                wake != null ? wake.RipplesNear(at.xz, 3f) : -1f, patch != null ? patch.Presses : -1f
            };
        }

        /// <summary>
        /// The camera following the player again after a test posed it ("-place prop:" frames its find and leaves the
        /// camera there): ripples and other near-camera work only run round the camera.
        /// </summary>
        public static bool FreeCamera()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
                return false;
            foreach (var b in cam.GetComponents<UnityEngine.MonoBehaviour>())
                b.enabled = true;
            return true;
        }
    }
}
#endif
