using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Punchy.Terrain;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// What folk say when the weather gets to them (A-71 row 4), read from StreamingAssets/weather-lines.json (edited by
    /// hand, read again whenever the file changes, like DialogueArchive). Lines are keyed by complaint (a ComfortKind
    /// name, or Relief on reaching shelter), then by race (a ComfortRace name), demeanor (Cheerful, Grumpy, Timid, Proud,
    /// Dramatic) or Any. A race with its own strong voice (ownVoice) says only its own lines; the rest mix their race's
    /// pool, their demeanor's and the generic one. Local only: each machine picks its own lines, nothing is sent.
    /// </summary>
    public class WeatherLines
    {
        public const string FileName = "weather-lines.json";
        public const string Any = "Any", Relief = "Relief";

        /// <summary>lines[kind][key]: the pool for one complaint and one race, demeanor or Any.</summary>
        public readonly Dictionary<string, Dictionary<string, string[]>> Lines =
            new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> OwnVoice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static WeatherLines loaded;
        static DateTime loadedTime;
        static float nextCheck;
        static string error;

        public static string Path => System.IO.Path.Combine(Application.streamingAssetsPath, FileName);

        /// <summary>The lines, read again if the file has changed (checked once a second); null if there's no file.</summary>
        public static WeatherLines Current
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
                                loaded = Parse(File.ReadAllText(path));
                                error = null;
                                Debug.Log($"WeatherLines: read {FileName}: {loaded.Summary()}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // (A typo mid-edit: keep the last good set, and say what's wrong once.)
                        if (error != e.Message)
                            Debug.LogWarning($"WeatherLines: couldn't read {FileName}, keeping the last good one: {e.Message}");
                        error = e.Message;
                    }
                }
                return loaded;
            }
        }

        public static WeatherLines Parse(string json)
        {
            var root = JObject.Parse(json);
            var set = new WeatherLines();
            if (root["ownVoice"] is JArray own)
                foreach (var v in own)
                    set.OwnVoice.Add((string)v);
            if (root["alias"] is JObject alias)
                foreach (var pair in alias)
                    set.Alias[pair.Key] = (string)pair.Value;
            if (root["lines"] is JObject kinds)
                foreach (var kind in kinds)
                {
                    var pools = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    if (kind.Value is JObject keys)
                        foreach (var key in keys)
                            if (key.Value is JArray arr && arr.Count > 0)
                                pools[key.Key] = arr.ToObject<string[]>();
                    set.Lines[kind.Key] = pools;
                }
            return set;
        }

        /// <summary>Lines in all, and per complaint the generic pool's size and the smallest race or demeanor pool.</summary>
        public string Summary()
        {
            var total = 0;
            var parts = new System.Text.StringBuilder();
            foreach (var kind in Lines)
            {
                int any = 0, least = int.MaxValue, n = 0;
                foreach (var pool in kind.Value)
                {
                    n += pool.Value.Length;
                    if (pool.Key.Equals(Any, StringComparison.OrdinalIgnoreCase))
                        any = pool.Value.Length;
                    else
                        least = Math.Min(least, pool.Value.Length);
                }
                total += n;
                parts.Append($" {kind.Key} {n} (Any {any}, least {(least == int.MaxValue ? 0 : least)} over {kind.Value.Count - 1} pools);");
            }
            return $"{total} lines;{parts}";
        }

        /// <summary>
        /// A folk's demeanor for weather lines: its temper (FolkJobs.Mood: cheerful or grumpy), and the plain ones split
        /// by another hash into plain, timid, proud and dramatic.
        /// </summary>
        public static string Demeanor(uint individual)
        {
            var mood = FolkJobs.Mood(individual);
            if (mood != FolkMood.Plain)
                return mood.ToString();
            switch (BiomeSampling.Scramble(individual ^ 0x3C6EF372u) % 4u)
            {
                case 1: return "Timid";
                case 2: return "Proud";
                case 3: return "Dramatic";
                default: return "Plain";
            }
        }

        /// <summary>
        /// A line for this complaint (a ComfortKind name or Relief) from a folk of this race and demeanor, or null if
        /// there's none. A strong voice keeps to its race's pool; others take their race's pool half the time (if it has
        /// one), their demeanor's a third of the time, else the generic pool.
        /// </summary>
        public string Pick(string kind, ComfortRace race, string demeanor, ref Unity.Mathematics.Random rng)
        {
            if (!Lines.TryGetValue(kind, out var pools))
                return null;
            var raceKey = race.ToString();
            if (!pools.ContainsKey(raceKey) && Alias.TryGetValue(raceKey, out var alias))
                raceKey = alias;
            pools.TryGetValue(raceKey, out var own);
            if (own != null && OwnVoice.Contains(race.ToString()))
                return own[rng.NextInt(own.Length)];
            var roll = rng.NextFloat();
            string[] pool = null;
            if (own != null && roll < 0.5f)
                pool = own;
            else if (roll < 0.83f && demeanor != null && pools.TryGetValue(demeanor, out var mood))
                pool = mood;
            if (pool == null && !pools.TryGetValue(Any, out pool))
                return null;
            return pool[rng.NextInt(pool.Length)];
        }
    }
}
