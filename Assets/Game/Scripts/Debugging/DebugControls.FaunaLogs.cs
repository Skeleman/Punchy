using System.Collections.Generic;
using Punchy.CameraRig;
using Punchy.Fauna;
using Punchy.Flora;
using Punchy.Player;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Debugging
{
    // DebugControls, fauna logs: hunt, personality, comfort, spin check, flight trace and foot drift. Code moved from DebugControls.cs (W-116).
    public partial class DebugControls
    {
        // ---------------------------------------------------------------- hunt log

        /// <summary>"-huntlog": logs each land hunter's chase as it goes (Z-53): stalk, run, pounce (landed or missed),
        /// eating, and how the hunt ended (ate, missed, slipped away, tired). Diving birds and folk hunters are left out.</summary>
        static bool huntLog;
        /// <summary>Per creature: its mode and stage last frame.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, int2> hunts = new System.Collections.Generic.Dictionary<Entity, int2>();

        void HuntLog()
        {
            if (!huntLog || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var entities = q.ToEntityArray(Allocator.Temp);
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            FaunaDirector director = null;
            for (int i = 0; i < states.Length; i++)
            {
                var c = states[i];
                var e = entities[i];
                var now = new int2((int)c.Mode, c.Stage);
                var landHunt = c.Mode == CreatureMode.Hunt && c.Stage < FolkHunting.Stalk && bodies[i].Blob.Value.Habitat != Habitat.Flies;
                var had = hunts.TryGetValue(e, out var was);
                if (!landHunt)
                {
                    if (had)
                    {
                        hunts.Remove(e);
                        HuntLine(director ??= FindAnyObjectByType<FaunaDirector>(), bodies[i], c, em.HasComponent<CreatureMind>(e) ? em.GetComponentData<CreatureMind>(e).Hunger : 0f,
                            "ended: " + (was.y == 3 ? "ate" : was.y == 4 ? "missed" : was.y == 2 ? "slipped away" : "tired") + $", now {c.Mode}");
                    }
                    continue;
                }
                if (!had || was.y != now.y)
                    HuntLine(director ??= FindAnyObjectByType<FaunaDirector>(), bodies[i], c, em.HasComponent<CreatureMind>(e) ? em.GetComponentData<CreatureMind>(e).Hunger : 0f,
                        now.y switch { 0 => "stalking", 1 => "running", 2 => "pounce landed", 3 => "eating", 4 => "pounce missed", _ => $"stage {now.y}" });
                hunts[e] = now;
            }
            if (hunts.Count > 4000)
                hunts.Clear();
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();
        }

        static void HuntLine(FaunaDirector director, CreatureBody body, CreatureState c, float hunger, string what)
        {
            var name = director != null ? director.SpeciesAt(body.Species)?.name : body.Species.ToString();
            var aggression = CreaturePersonality.Of(ref body.Blob.Value, c.Individual).Aggression;
            Debug.Log($"DebugControls: hunt: {name} #{c.Individual:X8} {what} (speed {c.Speed:F1}, hunger {hunger:F2}, aggression {aggression:F2}) at " +
                      $"({c.Position.x:F1}, {c.Position.y:F1}, {c.Position.z:F1}); caught so far {FaunaDirector.Caught}");
        }

        // ---------------------------------------------------------------- personality log

        /// <summary>"-personalitylog Dog": 8 s into the line (and again at 20 s), logs every creature of that species with
        /// its Individual, aggression, whether it hunts, its mode and its nearest prey (#50, #7). Personality comes from the
        /// replicated Individual, so a host and a joined client should print the same values.</summary>
        static string personalitySpecies;
        static float personalityAt = -1f;

        void PersonalityLog()
        {
            if (personalitySpecies == null || queryWorld == null || !queryWorld.IsCreated)
                return;
            if (personalityAt < 0f)
                personalityAt = Time.timeSinceLevelLoad;
            var t = Time.timeSinceLevelLoad - personalityAt;
            if (!(t > 8f && t - Time.deltaTime <= 8f) && !(t > 20f && t - Time.deltaTime <= 20f))
                return;
            var director = FindAnyObjectByType<FaunaDirector>();
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var rows = new System.Collections.Generic.List<string>();
            for (int i = 0; i < states.Length; i++)
            {
                var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : null;
                if (name != personalitySpecies || states[i].Gone)
                    continue;
                ref var b = ref bodies[i].Blob.Value;
                var c = states[i];
                var prey = float.MaxValue;
                for (int j = 0; j < states.Length; j++)
                    for (int k = 0; k < b.Prey.Length; k++)
                        if (bodies[j].Species == b.Prey[k] && !states[j].Gone)
                            prey = math.min(prey, math.distance(states[j].Position, c.Position));
                rows.Add($"#{c.Individual:X8} aggression {CreaturePersonality.Of(ref b, c.Individual).Aggression:F3} " +
                         $"hunts {CreaturePersonality.WillHunt(ref b, c.Individual)} mode {c.Mode} weight {b.WeightAt(c.Scale):F1} kg " +
                         $"prey {(prey < float.MaxValue ? prey.ToString("F1") + " m" : "none")}");
            }
            rows.Sort(System.StringComparer.Ordinal);
            var role = queryWorld.Name;
            Debug.Log($"DebugControls: personality ({role}, t {t:F0} s): {rows.Count} {personalitySpecies}");
            for (int i = 0; i < rows.Count; i++)
                Debug.Log($"DebugControls: personality {personalitySpecies} {i + 1}: {rows[i]}");
            states.Dispose();
            bodies.Dispose();
        }

        // ---------------------------------------------------------------- comfort

        /// <summary>"-comfort": every 5 s, logs each species that minds the weather with its count, mean discomfort meter,
        /// mean pressure and most common complaint, plus the weather and felt heat where they stand (A-71, #69).
        /// "-comfort off" switches the comfort check off in the brain (for the perf before/after).</summary>
        static bool comfortLog;
        static float comfortNext = 6f;

        void ComfortLog()
        {
            if (!comfortLog || queryWorld == null || !queryWorld.IsCreated || Time.timeSinceLevelLoad < comfortNext)
                return;
            comfortNext = Time.timeSinceLevelLoad + 5f;
            var director = FindAnyObjectByType<FaunaDirector>();
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>(),
                ComponentType.ReadOnly<CreatureMind>());
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var minds = q.ToComponentDataArray<CreatureMind>(Allocator.Temp);
            var night = 0f;
            using (var cq = em.CreateEntityQuery(ComponentType.ReadOnly<FaunaClock>()))
                if (cq.CalculateEntityCount() == 1)
                    night = cq.GetSingleton<FaunaClock>().Night;
            var groups = new SortedDictionary<string, (int n, float meter, float pressure, int[] kinds, float3 at)>(System.StringComparer.Ordinal);
            // Row 2: how many are in shelter now (by kind), and how many are on their way to one.
            var covered = new int[6];
            int sheltered = 0, going = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Gone || !bodies[i].Blob.Value.Comfort.Minds)
                    continue;
                if (minds[i].Cover != ShelterNeed.None)
                {
                    sheltered++;
                    for (int k = 0; k < 6; k++)
                        if (((int)minds[i].Cover & (1 << k)) != 0)
                            covered[k]++;
                }
                if (states[i].Mode == CreatureMode.Shelter && states[i].Stage == 0)
                    going++;
                var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : null;
                name ??= "#" + bodies[i].Species;
                if (!groups.TryGetValue(name, out var g))
                    g = (0, 0f, 0f, new int[9], states[i].Position);
                g.n++;
                g.meter += minds[i].Discomfort;
                g.pressure += minds[i].ComfortPressure;
                g.kinds[(int)minds[i].DiscomfortKind]++;
                groups[name] = g;
            }
            Debug.Log($"[comfort] {queryWorld.Name} t {Time.timeSinceLevelLoad:F0} s night {night:F2} grid ready {Comfort.Grid.Data.Ready} " +
                      $"off {Comfort.Off.Data}: {groups.Count} species");
            var counts = CreatureBrainSystem.Shelters.Data;
            Debug.Log($"[comfort] sheltered {sheltered} (roof {covered[0]} tree {covered[1]} water {covered[2]} fire {covered[3]} shade {covered[4]} " +
                      $"cave {covered[5]}) going {going} | started {counts.Started} indoors {counts.Indoors} arrived {counts.Arrived} none found {counts.NotFound} posed {counts.Posed} rock {counts.Rock}");
            // Every folk race's profile at the player's spot (the same five individuals each), whether or not any are near:
            // one line per race, mean pressure and the commonest complaint (the card's 5 races x 3 scenes).
            if (director != null && QueriesLive() && playerQuery.TryGetSingleton<LocalTransform>(out var me) && EnsureLocations(out var ground))
            {
                var here = Comfort.WeatherAt(me.Position.xz);
                var underground = me.Position.y < ground.SampleHeight(me.Position.xz) - 2f;
                Debug.Log($"[comfort] at player ({me.Position.x:F0},{me.Position.z:F0}) hour {(Cycle != null ? Cycle.TimeOfDay : -1f):F1}: " +
                          $"heat {here.Heat:F2} felt {Comfort.FeltHeat(here, night, underground):F2} humid {here.Humidity:F2} rain {here.Rain:F2} " +
                          $"snow {here.Snow:F2} wind {here.Wind:F2} storm {here.Storm:F2} sun {here.Sunlight:F2}");
                var seen = new HashSet<byte>();
                for (int sp = 0; sp < director.SpeciesCount; sp++)
                {
                    var blob = director.BlobAt(sp);
                    if (!blob.IsCreated)
                        continue;
                    ref var profile = ref blob.Value.Comfort;
                    if (!FolkRaces.Has(profile.Race, RaceTrait.Folk) || !profile.Minds || !seen.Add(profile.Race))
                        continue;
                    var sum = 0f;
                    var kinds = new int[9];
                    for (uint k = 1; k <= 5; k++)
                    {
                        sum += Comfort.Pressure(in profile, k * 0x9E3779B9u, in here, night, underground, ShelterNeed.None, out var kind);
                        kinds[(int)kind]++;
                    }
                    int worst = 0;
                    for (int k = 1; k < kinds.Length; k++)
                        if (kinds[k] > 0 && (worst == 0 || kinds[k] > kinds[worst]))
                            worst = k;
                    Debug.Log($"[comfort] race {FolkRaces.ById(profile.Race)?.name} ({director.SpeciesAt(sp).name}): pressure {sum / 5f:F2} kind {(ComfortKind)worst} " +
                              $"likes {profile.Heat.x:F2}-{profile.Heat.y:F2} tol {profile.Tolerance:F2}");
                }
            }
            foreach (var kv in groups)
            {
                var g = kv.Value;
                int top = 0;
                for (int k = 1; k < g.kinds.Length; k++)
                    if (g.kinds[k] > g.kinds[top] || (top == 0 && g.kinds[k] > 0))
                        top = k;
                var w = Comfort.WeatherAt(g.at.xz);
                Debug.Log($"[comfort] {kv.Key} x{g.n}: meter {g.meter / g.n:F2} pressure {g.pressure / g.n:F2} kind {(ComfortKind)top} " +
                          $"({g.kinds[top]}) | heat {w.Heat:F2} felt {Comfort.FeltHeat(w, night, false):F2} humid {w.Humidity:F2} " +
                          $"rain {w.Rain:F2} snow {w.Snow:F2} wind {w.Wind:F2} storm {w.Storm:F2} sun {w.Sunlight:F2}");
            }
            states.Dispose();
            bodies.Dispose();
            minds.Dispose();
        }

        // ---------------------------------------------------------------- spin check

        /// <summary>"-spincheck" (or "-spincheck 270,3"): logs any creature that turns this many degrees within this many
        /// seconds on the spot (Zach's Z-07: brief spins of 2-3 s).</summary>
        static bool spinCheck;
        static float spinDegrees = 270f, spinSeconds = 3f;
        /// <summary>Per creature: its heading last frame, how far it has turned since standing still, for how long, and
        /// when it was last logged.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> spins = new System.Collections.Generic.Dictionary<Entity, float4>();
        /// <summary>Per species, Mode and (wandering) stage: heading change (deg), seconds, speed x seconds, |yaw rate| x
        /// seconds (#12: the songbirds' wandering split into on the ground, flying, and the flare near the spot).</summary>
        readonly System.Collections.Generic.Dictionary<int, float4> turnSum = new System.Collections.Generic.Dictionary<int, float4>();
        /// <summary>A-54 #12: heading flips (a turn of 15 deg or more one way, then back the other) per turnSum key.</summary>
        readonly System.Collections.Generic.Dictionary<int, int> flipSum = new System.Collections.Generic.Dictionary<int, int>();
        float turnLogAt, traceAt;
        readonly System.Collections.Generic.Dictionary<Entity, float> wanderSince = new System.Collections.Generic.Dictionary<Entity, float>();
        Entity traced = Entity.Null;
        int traceLines;
        readonly System.Collections.Generic.Dictionary<Entity, float> spinLogged = new System.Collections.Generic.Dictionary<Entity, float>();

        /// <summary>
        /// Watches every creature's heading: one that turns through spinDegrees within spinSeconds while barely moving
        /// (under 0.3 m/s) is spinning in place, and is logged (at most every 20 s each) with what it's doing.
        /// </summary>
        void SpinCheck()
        {
            if (!spinCheck || queryWorld == null || !queryWorld.IsCreated)
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            var entities = q.ToEntityArray(Allocator.Temp);
            var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var dt = Time.deltaTime;
            var now = Time.time;
            for (int i = 0; i < states.Length; i++)
            {
                var c = states[i];
                var e = entities[i];
                if (!spins.TryGetValue(e, out var w))
                    w = new float4(c.Yaw, 0f, 0f, 0f);
                var signed = math.atan2(math.sin(c.Yaw - w.x), math.cos(c.Yaw - w.x));
                var step = math.abs(signed);
                w.x = c.Yaw;
                // (w.w: the turn so far in the one direction; reversing after 15 deg or more of it is a flip.)
                var flipped = false;
                if (signed * w.w < 0f)
                {
                    flipped = math.abs(w.w) >= math.radians(15f);
                    w.w = signed;
                }
                else
                    w.w += signed;
                // (Heading change per second of the songbirds' and dormice's wandering, for the Z-12 before/after.)
                // (A-13 #12: Wander counts only after 5 s of it, so the turn out of a flee or a takeoff is left out.)
                if (c.Mode != CreatureMode.Wander)
                    wanderSince[e] = -1f;
                else if (!wanderSince.TryGetValue(e, out var ws) || ws < 0f)
                    wanderSince[e] = now;
                var settledWander = c.Mode != CreatureMode.Wander || now - wanderSince[e] > 5f;
                if (bodies[i].Species >= 0 && settledWander)
                {
                    var targetGap = math.distance(c.Position.xz, c.Target);
                    var bucket = c.Mode != CreatureMode.Wander ? 0 : c.Stage == 0 ? 1 : c.Stage == 1 ? (targetGap < 1.5f ? 3 : 2) : 4;
                    var key = (bodies[i].Species * 64 + (int)c.Mode) * 8 + bucket;
                    turnSum.TryGetValue(key, out var acc);
                    flipSum.TryGetValue(key, out var flips);
                    flipSum[key] = flips + (flipped ? 1 : 0);
                    turnSum[key] = new float4(acc.x + math.degrees(step), acc.y + dt, acc.z + c.Speed * dt, acc.w + math.abs(math.degrees(c.YawRate)) * dt);                    // One wandering songbird traced twice a second (40 lines): stage, speed, yaw rate, how far from its
                    // target and how far above its target height.
                    if (c.Mode == CreatureMode.Wander && traceLines < 40 && (traced == Entity.Null ? c.Stage == 1 : traced == e) && now >= traceAt)
                    {
                        var director = FindAnyObjectByType<FaunaDirector>();
                        if (director != null && director.SpeciesAt(bodies[i].Species)?.name == "Songbird")
                        {
                            traced = e;
                            traceAt = now + 0.5f;
                            traceLines++;
                            Debug.Log($"DebugControls: songbird trace: stage {c.Stage} speed {c.Speed:F2} yaw rate {math.degrees(c.YawRate):F0} deg/s " +
                                      $"target {targetGap:F2} m, above target height {c.Position.y - c.TargetHeight:F2} m, fly {c.Fly:F2}");
                        }
                    }
                }
                FlightTrace(e, c, bodies[i].Species, step, now);
                if (c.Speed < 0.3f)
                {
                    w.y += step;
                    w.z += dt;
                }
                else
                    w.y = w.z = 0f;
                if (w.z > spinSeconds)
                    w.y = w.z = 0f;
                if (w.y > math.radians(spinDegrees) && (!spinLogged.TryGetValue(e, out var at) || now - at > 20f))
                {
                    spinLogged[e] = now;
                    var director = FindAnyObjectByType<FaunaDirector>();
                    var name = director != null ? director.SpeciesAt(bodies[i].Species)?.name : bodies[i].Species.ToString();
                    Debug.LogWarning($"DebugControls: spinning: {name} turned {math.degrees(w.y):F0} deg in {w.z:F1} s at speed {c.Speed:F2} " +
                                     $"({c.Mode}, stage {c.Stage}, action {c.Action}, sit {c.Sit:F2}, lie {c.Lie:F2}, look {c.Look:F2}, " +
                                     $"yaw rate {math.degrees(c.YawRate):F0} deg/s, target {math.distance(c.Position.xz, c.Target):F2} m, " +
                                     $"focus {math.distance(c.Position.xz, c.Focus):F2} m) at ({c.Position.x:F1}, {c.Position.y:F1}, {c.Position.z:F1})");
                    w.y = w.z = 0f;
                }
                spins[e] = w;
            }
            if (now >= turnLogAt)
            {
                if (turnLogAt > 0f)
                {
                    var director = FindAnyObjectByType<FaunaDirector>();
                    // (A Mode histogram per species: creature-seconds in each mode, and the heading change in it.)
                    var lines = new System.Collections.Generic.SortedDictionary<string, string>();
                    foreach (var kv in turnSum)
                    {
                        var modeKey = kv.Key / 8;
                        var species = director != null ? director.SpeciesAt(modeKey / 64)?.name : (modeKey / 64).ToString();
                        species ??= (modeKey / 64).ToString();
                        lines.TryGetValue(species, out var line);
                        var stage = (kv.Key % 8) switch { 1 => "/ground", 2 => "/flying", 3 => "/flare", 4 => "/stage2+", _ => "" };
                        var secs = math.max(kv.Value.y, 1e-3f);
                        flipSum.TryGetValue(kv.Key, out var flipCount);
                        lines[species] = line + $" {(CreatureMode)(modeKey % 64)}{stage} {kv.Value.y:F0}s {kv.Value.x / secs:F1}deg/s v{kv.Value.z / secs:F1} yr{kv.Value.w / secs:F0} flips{flipCount * 60f / secs:F1}/min;";
                    }
                    foreach (var kv in lines)
                        Debug.Log($"DebugControls: heading change: {kv.Key}:{kv.Value}");
                }
                turnSum.Clear();
                flipSum.Clear();
                turnLogAt = now + 30f;
            }
            if (spins.Count > 4000)
                spins.Clear();
            entities.Dispose();
            states.Dispose();
            bodies.Dispose();
        }

        /// <summary>Per songbird in a flight (Wander stage 1): x start time, y heading change so far (deg), z of it while
        /// above its glide line (arrived too high, circling down), w of it within 6 m of the spot.</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> flights = new System.Collections.Generic.Dictionary<Entity, float4>();
        /// <summary>Per songbird in a flight: x bearing to the spot at takeoff (deg), y distance then, z peak |yaw rate|,
        /// w heading change in its first 3 s (the takeoff turn).</summary>
        readonly System.Collections.Generic.Dictionary<Entity, float4> flightStart = new System.Collections.Generic.Dictionary<Entity, float4>();
        readonly System.Collections.Generic.Dictionary<Entity, float2> flightTarget = new System.Collections.Generic.Dictionary<Entity, float2>();
        int flightLines, songbirdSpecies = -2;

        /// <summary>
        /// #12/A-14: one line per songbird flight as it ends, splitting its heading change into the takeoff turn, the
        /// circling while too high over the spot, and the last 6 m; plus a line for every mid-flight retarget.
        /// </summary>
        void FlightTrace(Entity e, in CreatureState c, int species, float step, float now)
        {
            if (songbirdSpecies == -2)
            {
                var director = FindAnyObjectByType<FaunaDirector>();
                if (director == null || director.SpeciesCount == 0)
                    return;
                songbirdSpecies = -1;
                for (int k = 0; k < director.SpeciesCount; k++)
                    if (director.SpeciesAt(k)?.name == "Songbird")
                        songbirdSpecies = k;
            }
            if (species != songbirdSpecies || flightLines >= 120)
                return;
            var gap = math.distance(c.Position.xz, c.Target);
            var inFlight = c.Mode == CreatureMode.Wander && c.Stage == 1;
            if (inFlight && !flights.ContainsKey(e))
            {
                var to = c.Target - c.Position.xz;
                var bearing = math.degrees(math.atan2(math.sin(math.atan2(to.x, to.y) - c.Yaw), math.cos(math.atan2(to.x, to.y) - c.Yaw)));
                flights[e] = new float4(now, 0f, 0f, 0f);
                flightStart[e] = new float4(bearing, gap, 0f, 0f);
                flightTarget[e] = c.Target;
                return;
            }
            if (inFlight)
            {
                var f = flights[e];
                var st = flightStart[e];
                var deg = math.degrees(step);
                var glide = c.TargetHeight + math.max(gap - 0.3f, 0f) * Punchy.Fauna.CreatureMotion.GlideSlope;
                f.y += deg;
                if (c.Position.y > glide + 0.5f && gap < 15f)
                    f.z += deg;
                if (gap < 6f)
                    f.w += deg;
                if (now - f.x < 3f)
                    st.w += deg;
                st.z = math.max(st.z, math.abs(math.degrees(c.YawRate)));
                if (math.distance(flightTarget[e], c.Target) > 0.5f)
                {
                    flightLines++;
                    Debug.Log($"DebugControls: flight trace: retarget {math.distance(flightTarget[e], c.Target):F1} m at {now - f.x:F1} s");
                    flightTarget[e] = c.Target;
                }
                flights[e] = f;
                flightStart[e] = st;
            }
            else if (flights.TryGetValue(e, out var done))
            {
                var st = flightStart[e];
                var secs = math.max(now - done.x, 1e-3f);
                flightLines++;
                Debug.Log($"DebugControls: flight trace: {secs:F1} s, start bearing {st.x:F0} deg at {st.y:F1} m, turned {done.y:F0} deg " +
                          $"({done.y / secs:F1} deg/s): takeoff 3 s {st.w:F0}, high over spot {done.z:F0}, last 6 m {done.w:F0}; " +
                          $"peak yaw rate {st.z:F0} deg/s; ended {c.Mode} stage {c.Stage}");
                flights.Remove(e);
                flightStart.Remove(e);
                flightTarget.Remove(e);
            }
        }

        // ---------------------------------------------------------------- foot drift

        /// <summary>"-footdrift": measures foot sliding on the player's body (logged every 5 s while moving).</summary>
        static bool footDrift;
        readonly System.Collections.Generic.List<float> footDrifts = new System.Collections.Generic.List<float>();
        readonly float3[] footStart = new float3[8];
        readonly bool[] footPlanted = new bool[8];
        readonly float[] footWorst = new float[8], footLowest = new float[8];
        float footSpeed, footLogAt;
        int footFrames;

        /// <summary>
        /// Poses the player's body as the renderer does and follows each foot (the end of each leg chain from the
        /// body): while a foot is planted (in its stance, within 4 cm of its lowest point of late) its world position
        /// should stay put.
        /// Logs how far planted feet drifted over each stance (mean, 90th percentile, worst) and the mean speed.
        /// </summary>
        void FootDrift()
        {
            if (!footDrift || queryWorld == null || !queryWorld.IsCreated || !terrainQuery.TryGetSingleton<TerrainSource>(out var terrain))
                return;
            var em = queryWorld.EntityManager;
            using var q = em.CreateEntityQuery(typeof(PlayerAvatar), typeof(CreatureState), typeof(CreatureBody));
            if (q.CalculateEntityCount() != 1)
                return;
            var s = q.GetSingleton<CreatureState>();
            var body = q.GetSingleton<CreatureBody>();
            ref var b = ref body.Blob.Value;
            var output = new NativeArray<SegmentInstance>(b.Segments.Length + 64, Allocator.Temp);
            CreatureAnimator.Evaluate(ref b, s, body.Coat, terrain, output, 0);
            var foot = 0;
            for (int p = 0; p < b.Parts.Length && foot < 8; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0)
                    continue;
                var seg = output[part.FirstSegment + part.SegmentCount - 1];
                var tip = seg.Position + math.mul(seg.Rotation, new float3(0f, 0f, seg.Length));
                footLowest[foot] = footFrames == 0 ? tip.y : math.min(footLowest[foot] + Time.deltaTime * 0.05f, tip.y);
                // (On the ground in the gait's stance half, not just low: a low swing isn't a planted foot.)
                var down = tip.y < footLowest[foot] + 0.04f && s.Speed > 0.2f && math.frac(s.Phase + b.LegPhase(ref part, b.Sprint(s, s.GaitSpeed))) < 0.5f;
                if (down && !footPlanted[foot])
                {
                    footPlanted[foot] = true;
                    footStart[foot] = tip;
                    footWorst[foot] = 0f;
                }
                else if (down)
                    footWorst[foot] = math.max(footWorst[foot], math.distance(tip.xz, footStart[foot].xz));
                else if (footPlanted[foot])
                {
                    footPlanted[foot] = false;
                    footDrifts.Add(footWorst[foot]);
                }
                foot++;
            }
            output.Dispose();
            footSpeed += s.Speed;
            footFrames++;
            if (Time.time < footLogAt)
                return;
            footLogAt = Time.time + 5f;
            if (footDrifts.Count > 0)
            {
                footDrifts.Sort();
                float sum = 0f;
                foreach (var d in footDrifts)
                    sum += d;
                Debug.Log($"DebugControls: foot drift over {footDrifts.Count} stances: mean {sum / footDrifts.Count * 100f:F1} cm, " +
                          $"p90 {footDrifts[(int)(footDrifts.Count * 0.9f)] * 100f:F1} cm, worst {footDrifts[footDrifts.Count - 1] * 100f:F1} cm; " +
                          $"speed {footSpeed / math.max(footFrames, 1):F2} m/s");
            }
            footDrifts.Clear();
            footSpeed = 0f;
            footFrames = 0;
        }
    }
}
