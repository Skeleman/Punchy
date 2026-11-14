using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Settings
{
    /// <summary>
    /// The local-light manager (refactor 3.1, built on LightPolicy, 3.7): one place decides which lights light the
    /// world, instead of each system choosing its own range. Every local light source requests a light each frame
    /// (<see cref="Add"/>, <see cref="AddStill"/>, or <see cref="AddReal"/> for one that owns a Unity light) with its
    /// kind, position, colour x intensity and reach; <see cref="Resolve"/> (CaveSystem, once a frame) then:
    /// - tiers each by its on-screen reach with hysteresis (LightPolicy.Tier);
    /// - gives the real-light budget (<see cref="GraphicsSettings.RealLights"/>, "-lightbudget N") to the best-scoring
    ///   requests at the Real tier that own a Unity light (lanterns, interior lamps, the iceberg light); the owner reads
    ///   <see cref="RealLevel"/> back, which fades in and out as it gains or loses the budget and, over a long stretch
    ///   of on-screen size, as it nears the end of the Real tier (R-10: no pop where it hands over to its glow slot).
    ///   A still source with no Unity light of its own (a street
    ///   light) asks through <see cref="AddPooled"/> and is lent one from a pool while it holds the budget. The custom
    ///   shaders read real lights through Forward+ with the glow's falloff (Glow.hlsl's PunchyRealLights, R-10), so a
    ///   real light and the glow it falls back to light alike;
    /// - fills the 16 glow slots (Glow.hlsl's _PunchyCaveHeat) with the best-scoring of the rest at Glow or better
    ///   (LightPolicy.Score: importance x on-screen size x brightness), a light already in a slot counting a little
    ///   higher (stickiness), and fades lights in and out of the slots (0.8 s, with a small swell). A real light that
    ///   lost its budget falls back to a glow slot, cross-fading.
    /// A light shrinking towards the end of its glow tier dims over the last stretch rather than popping. Celestial and
    /// SkyFlash lights are global (WeatherSystem) and never come here. Cosmetic and local: nothing here is sent.
    /// "-lightlog" logs, per kind, every 5 s: requests, tiers, glow slots, real lights and the dropped.
    /// Main thread only; reuses its lists, so it allocates nothing per frame.
    /// </summary>
    public static class LightBudget
    {
        /// <summary>Glow slots (must match Glow.hlsl's arrays).</summary>
        public const int GlowSlots = 16;
        /// <summary>How long a light takes to come on as it joins the slots (with a small swell) and to go out (s).</summary>
        const float FadeIn = 0.8f, FadeOut = 0.8f;
        /// <summary>A light already in a slot (or holding a real light) scores this much higher, so lights at the edge
        /// of the budget don't flicker in and out as the camera moves (as 0.85 x distance did when it was nearest-first).</summary>
        const float Stickiness = 1f / 0.85f;
        /// <summary>A glow fades in over on-screen size from the end of its glow tier (after hysteresis) to this many
        /// times it.</summary>
        const float SizeFadeTo = 1.6f;

        static readonly int HeatId = Shader.PropertyToID("_PunchyCaveHeat");
        static readonly int HeatColorId = Shader.PropertyToID("_PunchyCaveHeatColor");
        static readonly int HeatCountId = Shader.PropertyToID("_PunchyCaveHeatCount");
        static readonly int RealReachId = Shader.PropertyToID("_PunchyRealLightReach");
        /// <summary>Profiler trial (11-11, R-10's town cost): "-realreach 0" turns off the reach cut below (every pixel
        /// walks the Forward+ light loop, as before).</summary>
        static bool reachCut = true;

        /// <summary>A real light of colour c and range r lights like a glow of c x RealToGlow with reach r x
        /// RealToGlowReach (must match Glow.hlsl's PunchyRealToGlow and PunchyRealToGlowReach).</summary>
        public const float RealToGlow = 0.28f, RealToGlowReach = 0.75f;

        struct Request
        {
            public LightKind Kind;
            public LightTier Tier;
            public bool OwnsLight, Pooled;
            public Vector4 Key, Position, Light;
            /// <summary>SizeFade: how far into its glow tier by size (0 at its end); RealFade: the same for its Real
            /// tier, which caps its real light (1 for a light that isn't an owner).</summary>
            public float Score, SizeFade, RealFade;
        }

        static readonly List<Request> requests = new List<Request>(256);
        static readonly List<(float score, int index)> ranked = new List<(float, int)>(256);
        static readonly Comparer<(float score, int index)> ByScore =
            Comparer<(float score, int index)>.Create((a, b) => b.score.CompareTo(a.score));

        // Tiers by key, last frame's and this frame's (for hysteresis).
        static Dictionary<Vector4, LightTier> tiers = new Dictionary<Vector4, LightTier>();
        static Dictionary<Vector4, LightTier> nextTiers = new Dictionary<Vector4, LightTier>();
        // Real lights: each owner's level (0..1, faded) by key, and the keys granted this frame.
        static readonly Dictionary<Vector4, float> realLevel = new Dictionary<Vector4, float>();
        static readonly HashSet<Vector4> realGranted = new HashSet<Vector4>();
        // This frame's RealFade of each granted key.
        static readonly Dictionary<Vector4, float> grantedFade = new Dictionary<Vector4, float>();
        static readonly List<Vector4> keyScratch = new List<Vector4>();

        // Pooled real lights (AddPooled): each pooled key's last position and glow, the pool, and its hidden root.
        static readonly Dictionary<Vector4, (Vector3 position, Vector4 glow)> pooled = new Dictionary<Vector4, (Vector3, Vector4)>();
        static readonly List<Light> pool = new List<Light>();
        static GameObject poolRoot;

        // The glow slots: what goes up, and each slot's fade bookkeeping (as CaveSystem's glow list had it).
        static readonly Vector4[] heat = new Vector4[GlowSlots];
        static readonly Vector4[] heatColor = new Vector4[GlowSlots];
        static readonly Dictionary<Vector4, float> heatAge = new Dictionary<Vector4, float>();
        static readonly Dictionary<Vector4, float> heatNextAge = new Dictionary<Vector4, float>();
        /// <summary>Lights that have left the slots, still fading out where they were last: colour, position, time left.</summary>
        static readonly Dictionary<Vector4, (Vector4 color, Vector4 position, float left)> heatLeaving = new Dictionary<Vector4, (Vector4, Vector4, float)>();
        /// <summary>Each slotted light's colour and position when last uploaded, so one whose request stops altogether
        /// still fades out rather than popping off.</summary>
        static readonly Dictionary<Vector4, (Vector4 color, Vector4 position)> heatLast = new Dictionary<Vector4, (Vector4, Vector4)>();

        // "-lightlog": per kind, requests, tiers (by LightTier), glow slots, real lights, dropped (wanted a light, got none).
        const int Kinds = 7, Tiers = 6;
        static readonly int[] logRequests = new int[Kinds], logSlots = new int[Kinds], logReal = new int[Kinds], logDropped = new int[Kinds];
        static readonly int[] logTiers = new int[Kinds * Tiers];
        static readonly StringBuilder logText = new StringBuilder(512);
        static float nextLog;
        public static bool LogOn;
        /// <summary>Last frame's totals, for perf logs: requests, glow slots used, real lights granted, dropped.</summary>
        public static int Requested, SlotsUsed, RealUsed, Dropped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            LogOn = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-lightlog") >= 0;
            var reachArg = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-realreach");
            reachCut = !(reachArg >= 0 && reachArg + 1 < Punchy.Debugging.DebugControls.LaunchArgs.Length
                         && Punchy.Debugging.DebugControls.LaunchArgs[reachArg + 1] == "0");
            // (Profiler pricing, 11-11: "-floraglowworld 0" leaves the island glow out of plants, R-10 row 4's addition.)
            var floraArg = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-floraglowworld");
            Shader.SetGlobalFloat("_PunchyFloraGlowWorldOff", floraArg >= 0 && floraArg + 1 < Punchy.Debugging.DebugControls.LaunchArgs.Length
                                  && Punchy.Debugging.DebugControls.LaunchArgs[floraArg + 1] == "0" ? 1f : 0f);
            Discard();
            tiers.Clear();
            realLevel.Clear();
            realGranted.Clear();
            pooled.Clear();
            pool.Clear();
            poolRoot = null;
            heatAge.Clear();
            heatLeaving.Clear();
            heatLast.Clear();
            nextLog = 0f;
        }

        /// <summary>A moving light's key: its id split in two halves (a float holds only 24 bits exactly, and fire seeds
        /// use all 32), its kind, and a w no still light's key has.</summary>
        static Vector4 MovingKey(int id, LightKind kind) => new Vector4(id & 0xFFFF, (id >> 16) & 0xFFFF, (int)kind, float.MaxValue);

        /// <summary>A light that may move (a lantern someone carries, a flame on a walking creature): id stays the same
        /// while it lives, so its fade carries on as it moves. light: rgb linear x intensity, w its reach (m).</summary>
        public static void Add(LightKind kind, int id, Vector3 position, Vector4 light)
            => Push(kind, MovingKey(id, kind), position, light, false);

        /// <summary>A light that stays where it is (a glowing plant, a pool, a sconce): its position is its id.</summary>
        public static void AddStill(LightKind kind, Vector3 position, Vector4 light)
            => Push(kind, new Vector4(position.x, position.y, position.z, 0f), position, light, false);

        /// <summary>A light whose source owns a Unity light (a lantern, a room lamp): it competes for the real-light
        /// budget, and falls back to a glow slot with <paramref name="glow"/> (the same light as a glow) when it loses.
        /// Read <see cref="RealLevel"/> for how lit to keep the Unity light.</summary>
        public static void AddReal(LightKind kind, int id, Vector3 position, Vector4 glow)
            => Push(kind, MovingKey(id, kind), position, glow, true);

        /// <summary>A still light with no Unity light of its own (a street light) that competes for the real-light
        /// budget like <see cref="AddReal"/>: while it holds it, a pooled Unity light (unshadowed) lights it the way
        /// the <paramref name="glow"/> would, and it falls back to a glow slot when it loses.</summary>
        public static void AddPooled(LightKind kind, Vector3 position, Vector4 glow)
        {
            var key = new Vector4(position.x, position.y, position.z, 0f);
            if (!Push(kind, key, position, glow, true))
                return;
            pooled[key] = (position, glow);
            var r = requests[requests.Count - 1];
            r.Pooled = true;
            requests[requests.Count - 1] = r;
        }

        /// <summary>
        /// A light inside a building (LightPolicy.IndoorLightsOutdoors): it lights only that building's inside. Every
        /// building on the island is a solid shell (Interiors builds the enterable rooms off the island, each with its
        /// own lamp, which asks through <see cref="AddReal"/>), so there's nothing inside for it to light and it takes
        /// no slot: lights at a house's middle lit its outer walls and the ground round it through the walls (P-21,
        /// item 31). Counted for "-lightlog" only.
        /// </summary>
        public static void AddIndoor(LightKind kind, Vector3 position, Vector4 light)
        {
            if (math.max(light.x, math.max(light.y, light.z)) > 0f && light.w != 0f)
                indoorCount++;
        }

        static int indoorCount;
        /// <summary>Last frame's indoor lights (<see cref="AddIndoor"/>), which light nothing outdoors.</summary>
        public static int Indoor;

        /// <summary>How lit a source's own Unity light should be (0..1, fading as it gains or loses the budget).</summary>
        public static float RealLevel(LightKind kind, int id)
            => realLevel.TryGetValue(MovingKey(id, kind), out var level) ? level : 0f;

        static bool Push(LightKind kind, Vector4 key, Vector3 position, Vector4 light, bool owns)
        {
            if (math.max(light.x, math.max(light.y, light.z)) <= 0f || light.w == 0f || kind <= LightKind.SkyFlash)
                return false;
            requests.Add(new Request
            {
                Kind = kind, Key = key, OwnsLight = owns, Light = light,
                Position = new Vector4(position.x, position.y, position.z, 0f)
            });
            return true;
        }

        /// <summary>Drops this frame's requests unused (no camera or world this frame).</summary>
        public static void Discard() => requests.Clear();

        /// <summary>Empties the glow slots at once (the world was released).</summary>
        public static void ClearSlots()
        {
            heatAge.Clear();
            heatLeaving.Clear();
            heatLast.Clear();
            Shader.SetGlobalInt(HeatCountId, 0);
            foreach (var key in pooled.Keys)
                realLevel.Remove(key);
            pooled.Clear();
            LightPool();
        }

        /// <summary>The camera distance over which the far glow map should take over from the glow slots for a glow of
        /// this kind and reach (x: fully slotted inside, y: slot gone), from the policy at the current view. Only
        /// ambient glows are cut short by the slots running out (street lights are mostly real lights, R-10).</summary>
        public static Vector2 GlowHandOff(LightKind kind, float reach)
        {
            var view = DetailPolicy.Current.Data;
            var end = GlowEnd(LightPolicy.For(kind), view);
            var to = DetailPolicy.Reach(reach, end, view.PixelsPerMetre);
            var capped = kind == LightKind.AmbientGlow ? DetailPolicy.CappedReach(to, ambientCutAt) : to;
            return new Vector2(math.min(DetailPolicy.Reach(reach, end * SizeFadeTo, view.PixelsPerMetre), capped), capped);
        }

        /// <summary>How near the closest ambient glow the 16 slots had no room for is (eased, so the hand-off doesn't jump).</summary>
        static float ambientCutAt = float.MaxValue;

        /// <summary>How near the closest street light is that neither a real light nor a glow slot lights in full (m,
        /// eased): the night map has to take over from there (CaveSystem), or the town goes dark between the last
        /// light the budget covers and the Fixture hand-off.</summary>
        public static float StreetLightGapAt => streetGapAt;
        static float streetGapAt = float.MaxValue;

        /// <summary>The on-screen size (reference pixels) below which a kind leaves the glow tier, at this view.</summary>
        static float GlowEnd(in LightPolicy.Rule rule, in DetailPolicy.View view) => TierEnd(rule, view, LightTier.Glow);

        /// <summary>The on-screen size (reference pixels) below which a kind leaves <paramref name="tier"/>, at this view.</summary>
        static float TierEnd(in LightPolicy.Rule rule, in DetailPolicy.View view, LightTier tier)
        {
            var size = rule.Size.Scaled(math.max(view.Detail, 0.1f));
            var step = (int)tier - (int)rule.Best;
            return step <= 0 ? size.Near : step == 1 ? size.Mid : size.Far;
        }

        /// <summary>
        /// Applies the policy to this frame's requests: tiers, the real-light budget and the glow slots, and uploads the
        /// slots to the shaders. Once a frame, after most sources have requested (a later one waits for the next frame).
        /// </summary>
        public static void Resolve(Camera cam)
        {
            DetailPolicy.SetView(cam);
            var view = DetailPolicy.Current.Data;
            float3 camPos = cam.transform.position;
            var dt = math.min(Time.unscaledDeltaTime, 0.1f);
            var log = LogOn && Time.unscaledTime >= nextLog;
            (Indoor, indoorCount) = (indoorCount, 0);
            if (log)
            {
                System.Array.Clear(logRequests, 0, Kinds);
                System.Array.Clear(logSlots, 0, Kinds);
                System.Array.Clear(logReal, 0, Kinds);
                System.Array.Clear(logDropped, 0, Kinds);
                System.Array.Clear(logTiers, 0, logTiers.Length);
            }

            // Tier and score every request.
            nextTiers.Clear();
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                var rule = LightPolicy.For(r.Kind);
                var reach = math.abs(r.Light.w);
                var distance = math.distance(((float4)r.Position).xyz, camPos);
                var previous = tiers.TryGetValue(r.Key, out var was) ? (int)was : -1;
                r.Tier = LightPolicy.Tier(r.Kind, reach, distance, view, previous);
                // (Two requests with one key keep the better tier.)
                if (!nextTiers.TryGetValue(r.Key, out var other) || r.Tier < other)
                    nextTiers[r.Key] = r.Tier;
                var intensity = math.max(r.Light.x, math.max(r.Light.y, r.Light.z));
                r.Score = LightPolicy.Score(r.Kind, reach, distance, intensity, view);
                var end = GlowEnd(rule, view);
                var pixels = DetailPolicy.ScreenSize(reach, distance, view.PixelsPerMetre);
                r.SizeFade = DetailPolicy.Crossfade(pixels, end * (1f - DetailPolicy.Hysteresis), end * SizeFadeTo);
                // An owner's real light dims over the same kind of stretch towards the end of its Real tier, and its
                // glow slot takes the rest (both fall off alike, R-10), instead of a 0.8 s swap at the boundary.
                var realEnd = TierEnd(rule, view, LightTier.Real);
                r.RealFade = !r.OwnsLight || rule.Global ? 1f
                    : DetailPolicy.Crossfade(pixels, realEnd * (1f - DetailPolicy.Hysteresis), realEnd * SizeFadeTo);
                requests[i] = r;
                if (log)
                {
                    logRequests[(int)r.Kind]++;
                    logTiers[(int)r.Kind * Tiers + (int)r.Tier]++;
                }
            }
            (tiers, nextTiers) = (nextTiers, tiers);

            // The real-light budget: owners at the Real tier (or better), best first.
            ranked.Clear();
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                if (r.OwnsLight && r.Tier <= LightTier.Real)
                    ranked.Add((realGranted.Contains(r.Key) ? r.Score * Stickiness : r.Score, i));
            }
            ranked.Sort(ByScore);
            realGranted.Clear();
            grantedFade.Clear();
            // (Sources' own lights, which may cast shadows, stay few; pooled street lights fill the rest.)
            var budget = GraphicsSettings.RealLights;
            var ownBudget = GraphicsSettings.OwnRealLights;
            for (int k = 0, own = 0; k < ranked.Count && realGranted.Count < budget; k++)
            {
                var r = requests[ranked[k].index];
                if (!r.Pooled && own++ >= ownBudget)
                    continue;
                realGranted.Add(r.Key);
                grantedFade[r.Key] = grantedFade.TryGetValue(r.Key, out var f) ? math.max(f, r.RealFade) : r.RealFade;
            }
            keyScratch.Clear();
            foreach (var kv in realLevel)
                keyScratch.Add(kv.Key);
            foreach (var key in realGranted)
                if (!realLevel.ContainsKey(key))
                    keyScratch.Add(key);
            foreach (var key in keyScratch)
            {
                var level = realLevel.TryGetValue(key, out var l) ? l : 0f;
                // Gaining or losing the budget fades over time; within it, the level follows on-screen size at once.
                level = grantedFade.TryGetValue(key, out var fade) ? math.min(level + dt / FadeIn, fade) : math.max(level - dt / FadeOut, 0f);
                if (level <= 0f && !realGranted.Contains(key))
                    realLevel.Remove(key);
                else
                    realLevel[key] = level;
            }
            // Pooled keys no longer lit (or never granted) let their glow go.
            keyScratch.Clear();
            foreach (var key in pooled.Keys)
                if (!realLevel.ContainsKey(key))
                    keyScratch.Add(key);
            foreach (var key in keyScratch)
                pooled.Remove(key);
            LightPool();

            // The glow slots: everything else at the Glow tier or better (and real lights fading out), best first.
            ranked.Clear();
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                if (r.Tier > LightTier.Glow || r.SizeFade <= 0f)
                    continue;
                if (r.OwnsLight && realLevel.TryGetValue(r.Key, out var real) && real >= 0.999f)
                    continue;
                ranked.Add((heatAge.ContainsKey(r.Key) ? r.Score * Stickiness : r.Score, i));
            }
            ranked.Sort(ByScore);
            var count = math.min(ranked.Count, GlowSlots);

            // The far glow map takes over from the nearest ambient glow the cap cut (CappedReach).
            var nearestCut = 4000f;
            Vector3 eyeAt = cam.transform.position;
            for (int k = count; k < ranked.Count; k++)
            {
                var r = requests[ranked[k].index];
                if (r.Kind == LightKind.AmbientGlow)
                    nearestCut = math.min(nearestCut, Vector3.Distance(r.Position, eyeAt));
            }
            var cutBefore = ambientCutAt > 4000f ? 4000f : ambientCutAt;
            ambientCutAt = DetailPolicy.Ease(cutBefore, nearestCut, dt, 2f);

            heatNextAge.Clear();
            for (int k = 0; k < count; k++)
            {
                var r = requests[ranked[k].index];
                // (One handed down from its real light joins at full, with no swell: the real light dims as it brightens.)
                var age = (heatAge.TryGetValue(r.Key, out var a) ? a
                           : r.OwnsLight && realLevel.ContainsKey(r.Key) ? FadeIn * 1.2f : 0f) + dt;
                heatNextAge[r.Key] = age;
                heatLeaving.Remove(r.Key);
                var scale = r.SizeFade * (r.OwnsLight && realLevel.TryGetValue(r.Key, out var real) ? 1f - real : 1f);
                var color = new Vector4(r.Light.x * scale, r.Light.y * scale, r.Light.z * scale, math.abs(r.Light.w));
                heatLast[r.Key] = (color, r.Position);
                // Up to full over the first 60%, a swell of about 12% over full, settling back by the end.
                var t = age / FadeIn;
                var envelope = math.smoothstep(0f, 0.6f, t) + 0.12f * math.smoothstep(0.3f, 0.6f, t) * (1f - math.smoothstep(0.6f, 1.2f, t));
                heat[k] = r.Position;
                heatColor[k] = new Vector4(color.x * envelope, color.y * envelope, color.z * envelope, color.w);
                if (log)
                    logSlots[(int)r.Kind]++;
            }
            // Those that just left start fading out.
            foreach (var kv in heatAge)
                if (!heatNextAge.ContainsKey(kv.Key) && !heatLeaving.ContainsKey(kv.Key) && heatLast.TryGetValue(kv.Key, out var last))
                    heatLeaving[kv.Key] = (last.color, last.position, FadeOut * math.saturate(kv.Value / FadeIn));
            foreach (var kv in heatAge)
                if (!heatNextAge.ContainsKey(kv.Key))
                    heatLast.Remove(kv.Key);
            heatAge.Clear();
            foreach (var kv in heatNextAge)
                heatAge[kv.Key] = kv.Value;

            // Fading ones fill the spare slots (and are dropped, already dim, when there are none).
            keyScratch.Clear();
            foreach (var key in heatLeaving.Keys)
                keyScratch.Add(key);
            foreach (var key in keyScratch)
            {
                var (color, position, left) = heatLeaving[key];
                left -= dt;
                if (left <= 0f || count >= GlowSlots)
                {
                    heatLeaving.Remove(key);
                    continue;
                }
                heatLeaving[key] = (color, position, left);
                var fade = math.smoothstep(0f, 1f, left / FadeOut);
                heat[count] = position;
                heatColor[count] = new Vector4(color.x * fade, color.y * fade, color.z * fade, color.w);
                count++;
            }

            // The nearest street light lit by neither a real light in full nor a slot: where the night map must start.
            var nearestGap = 4000f;
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                // (Street lights only: a pooled fire, R-25, is not in the night map, so it must not pull the map nearer.)
                if (!r.Pooled || r.Kind != LightKind.Fixture || r.Tier > LightTier.Glow || heatNextAge.ContainsKey(r.Key)
                    || (realLevel.TryGetValue(r.Key, out var lit) && lit >= 0.999f))
                    continue;
                nearestGap = math.min(nearestGap, Vector3.Distance(r.Position, eyeAt));
            }
            streetGapAt = DetailPolicy.Ease(streetGapAt > 4000f ? 4000f : streetGapAt, nearestGap, dt, 2f);

            Shader.SetGlobalVectorArray(HeatId, heat);
            Shader.SetGlobalVectorArray(HeatColorId, heatColor);
            Shader.SetGlobalInt(HeatCountId, count);

            // How far from the camera any real light reaches (its distance plus its reach, with a margin, as the shaders
            // light it: Glow.hlsl's PunchyRealLights): past that a pixel skips the Forward+ light loop, which every pixel
            // of terrain, plants and grass otherwise walks however few lights there are (profiler 11-11: R-10 cost
            // +0.8 ms GPU over a lit town at any light budget). 0 with no real lights on.
            var realReach = 0f;
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                if (realLevel.TryGetValue(r.Key, out var lit) && lit > 0f)
                    realReach = math.max(realReach, Vector3.Distance(r.Position, eyeAt) + math.abs(r.Light.w) * 1.5f + 2f);
            }
            Shader.SetGlobalFloat(RealReachId, reachCut ? realReach : 1e9f);

            // Totals, and the log.
            Requested = requests.Count;
            SlotsUsed = heatNextAge.Count;
            RealUsed = realGranted.Count;
            Dropped = 0;
            for (int i = 0; i < requests.Count; i++)
            {
                var r = requests[i];
                if (log && realGranted.Contains(r.Key))
                    logReal[(int)r.Kind]++;   // (by the request's kind: a pooled light's key is its position, not its kind)
                if (r.Tier > LightTier.Glow || realGranted.Contains(r.Key) || heatNextAge.ContainsKey(r.Key))
                    continue;
                Dropped++;
                if (log)
                    logDropped[(int)r.Kind]++;
            }
            if (log)
                Log(budget);
            requests.Clear();
        }

        /// <summary>Lends the pool's Unity lights to the pooled keys still lit (granted or fading out), each lighting
        /// as its glow would (range = reach / RealToGlowReach, linear colour x intensity = glow / RealToGlow), scaled
        /// by its level; the rest of the pool is switched off. Lights are made as needed and kept.</summary>
        static void LightPool()
        {
            var used = 0;
            foreach (var kv in pooled)
            {
                if (!realLevel.TryGetValue(kv.Key, out var level) || level <= 0f)
                    continue;
                var (position, glow) = kv.Value;
                var peak = math.max(glow.x, math.max(glow.y, glow.z));
                if (used == pool.Count || pool[used] == null)
                {
                    if (poolRoot == null)
                    {
                        poolRoot = new GameObject("Pooled lights") { hideFlags = HideFlags.DontSave };
                        Object.DontDestroyOnLoad(poolRoot);
                        pool.Clear();
                        used = 0;
                    }
                    var go = new GameObject("Pooled light") { hideFlags = HideFlags.DontSave };
                    go.transform.SetParent(poolRoot.transform, false);
                    var made = go.AddComponent<Light>();
                    made.type = LightType.Point;
                    made.shadows = LightShadows.None;
                    if (used == pool.Count)
                        pool.Add(made);
                    else
                        pool[used] = made;
                }
                var light = pool[used++];
                light.transform.position = position;
                light.range = math.abs(glow.w) / RealToGlowReach;
                light.color = new Color(glow.x / peak, glow.y / peak, glow.z / peak).gamma;
                light.intensity = peak / RealToGlow * level;
                if (!light.enabled)
                    light.enabled = true;
            }
            for (int i = used; i < pool.Count; i++)
                if (pool[i] != null && pool[i].enabled)
                    pool[i].enabled = false;
        }

        static void Log(int budget)
        {
            nextLog = Time.unscaledTime + 5f;
            var text = logText;
            text.Clear();
            text.Append("[lights] requests ").Append(Requested).Append(", glow slots ").Append(SlotsUsed).Append('/').Append(GlowSlots)
                .Append(", real ").Append(RealUsed).Append('/').Append(budget).Append(", dropped ").Append(Dropped).Append(", indoor ").Append(Indoor)
                .Append(" (detail ").Append(GraphicsSettings.Detail.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)).Append(')');
            for (int k = 0; k < Kinds; k++)
            {
                if (logRequests[k] == 0)
                    continue;
                text.Append("\n  ").Append(((LightKind)k).ToString()).Append(": ").Append(logRequests[k]).Append(" requests; tiers");
                for (int t = 0; t < Tiers; t++)
                    if (logTiers[k * Tiers + t] > 0)
                        text.Append(' ').Append(((LightTier)t).ToString()).Append(' ').Append(logTiers[k * Tiers + t]);
                text.Append("; slots ").Append(logSlots[k]).Append(", real ").Append(logReal[k]).Append(", dropped ").Append(logDropped[k]);
            }
            Debug.Log(text.ToString());
        }
    }
}
