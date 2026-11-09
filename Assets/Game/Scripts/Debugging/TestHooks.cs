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
                return new float[5];
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
    }
}
#endif
