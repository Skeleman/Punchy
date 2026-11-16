using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A hand-edited JSON file read again whenever it changes, while playing (A-129, #393): the one reloader for the text
    /// archives (dialogue.json, weather-lines.json; quests and names can join). Checked once a second by write time, or at
    /// once after Reload(). A file that won't parse, or whose parse names a bad entry, is turned down whole: the last good
    /// one stays in use and the log says, once, what's wrong and where (each bad entry's path and line). Where it reads
    /// from: JsonFiles.PathOf. Local only, nothing is sent.
    /// </summary>
    public sealed class LiveJsonFile<T> where T : class
    {
        readonly string fileName, label;
        readonly Func<string, JsonProblems, T> parse;
        readonly Func<T, string> summary;
        T loaded;
        DateTime loadedTime;
        string loadedPath;
        float nextCheck;
        bool forced;

        /// <param name="label">The log prefix ("Dialogue").</param>
        /// <param name="parse">Reads the text, naming each bad entry in the problems (it mustn't throw on one).</param>
        /// <param name="summary">What a good read holds, for the log ("812 lines").</param>
        public LiveJsonFile(string fileName, string label, Func<string, JsonProblems, T> parse, Func<T, string> summary)
        {
            this.fileName = fileName;
            this.label = label;
            this.parse = parse;
            this.summary = summary;
        }

        /// <summary>Why the newest version was turned down (all its bad entries), or null if it was read.</summary>
        public string Error { get; private set; }

        public string Path => JsonFiles.PathOf(fileName);

        /// <summary>Read the file again on the next Current, changed or not.</summary>
        public void Reload() => forced = true;

        /// <summary>The last good read, read again if the file has changed (checked once a second); null if there's none.</summary>
        public T Current
        {
            get
            {
                // (The clock restarts with each play session but this object doesn't: a check more than a second away
                // is a stale one from the last session, so check now.)
                var now = Time.realtimeSinceStartup;
                if (loaded == null || forced || now >= nextCheck || nextCheck - now > 1f)
                {
                    nextCheck = now + 1f;
                    Check();
                }
                return loaded;
            }
        }

        void Check()
        {
            var path = Path;
            try
            {
                if (!File.Exists(path))
                    return;
                var time = File.GetLastWriteTimeUtc(path);
                if (loaded != null && !forced && time == loadedTime && path == loadedPath)
                    return;
                forced = false;
                loadedTime = time;
                loadedPath = path;
                var problems = new JsonProblems();
                T read;
                try
                {
                    read = parse(File.ReadAllText(path), problems);
                }
                catch (JsonException e)
                {
                    // (A typo mid-edit: a missing comma or quote. The message already says where.)
                    problems.Add(e.Message);
                    read = null;
                }
                if (problems.Count > 0 || read == null)
                {
                    var why = problems.Count > 0 ? problems.ToString() : "nothing in it";
                    if (Error != why)
                        Debug.LogWarning($"{label}: {fileName} has {Math.Max(problems.Count, 1)} bad entr{(problems.Count == 1 ? "y" : "ies")}, " +
                                         $"{(loaded != null ? "keeping the last good one" : "using none")} until it's fixed:\n{why}");
                    Error = why;
                    return;
                }
                loaded = read;
                Error = null;
                Debug.Log($"{label}: read {path} ({summary(read)})");
            }
            catch (Exception e)
            {
                // (Mid-save, the file can be locked or half written for a moment: try again next second.)
                var why = e.GetType().Name + ": " + e.Message;
                if (Error != why)
                    Debug.LogWarning($"{label}: couldn't read {fileName}, keeping the last good one: {why}");
                Error = why;
                loadedTime = default;
            }
        }
    }

    /// <summary>Where the hand-edited files are read from.</summary>
    public static class JsonFiles
    {
        /// <summary>
        /// In a build, a copy beside the game's exe wins (easy to find and edit: Punchy/dialogue.json); otherwise, and
        /// always in the editor, StreamingAssets (in a build, Punchy_Data/StreamingAssets).
        /// </summary>
        public static string PathOf(string fileName)
        {
            if (!Application.isEditor)
            {
                var exeDir = System.IO.Path.GetDirectoryName(Application.dataPath);
                if (exeDir != null)
                {
                    var beside = System.IO.Path.Combine(exeDir, fileName);
                    if (File.Exists(beside))
                        return beside;
                }
            }
            return System.IO.Path.Combine(Application.streamingAssetsPath, fileName);
        }
    }

    /// <summary>
    /// The bad entries a parse found, each with its JSON path and line ("cultures.Dwarf.lines[3] (line 120): no text"),
    /// and readers that name a wrong-typed value instead of throwing on it.
    /// </summary>
    public sealed class JsonProblems
    {
        /// <summary>How many problems the log lists before "and N more".</summary>
        const int Shown = 20;
        readonly List<string> list = new List<string>();

        public int Count => list.Count;
        public IReadOnlyList<string> All => list;

        public void Add(string problem) => list.Add(problem);

        public void Add(JToken at, string problem) => list.Add($"{Where(at)}: {problem}");

        /// <summary>A token's path and line: "cultures.Dwarf.lines[3] (line 120)".</summary>
        public static string Where(JToken at)
        {
            if (at == null)
                return "(top)";
            var path = string.IsNullOrEmpty(at.Path) ? "(top)" : at.Path;
            return at is IJsonLineInfo info && info.HasLineInfo() ? $"{path} (line {info.LineNumber})" : path;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < Shown; i++)
                sb.Append("  ").Append(list[i]).Append('\n');
            if (list.Count > Shown)
                sb.Append($"  and {list.Count - Shown} more\n");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>A string value, or null if absent; a problem if it's there but not a string.</summary>
        public string Str(JObject o, string key)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null)
                return null;
            if (t.Type == JTokenType.String)
                return (string)t;
            Add(t, $"\"{key}\" should be text, not {Kind(t)}");
            return null;
        }

        /// <summary>A line's "text": a problem if missing, not a string, empty or with an unclosed { or a stray }.</summary>
        public string Text(JObject o)
        {
            var t = o["text"];
            if (t == null)
            {
                Add(o, "no \"text\"");
                return null;
            }
            var s = Str(o, "text");
            if (s == null)
                return null;
            if (s.Trim().Length == 0)
                Add(t, "\"text\" is empty");
            else
                Slots(t, s);
            return s;
        }

        /// <summary>A problem for each { without a } before the next {, and each } without a {.</summary>
        public void Slots(JToken at, string s)
        {
            var open = -1;
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '{')
                {
                    if (open >= 0)
                    {
                        Add(at, $"a {{ is never closed in \"{s}\"");
                        return;
                    }
                    open = i;
                }
                else if (s[i] == '}')
                {
                    if (open < 0)
                    {
                        Add(at, $"a }} has no {{ in \"{s}\"");
                        return;
                    }
                    if (i == open + 1)
                        Add(at, $"an empty {{}} in \"{s}\"");
                    open = -1;
                }
            if (open >= 0)
                Add(at, $"a {{ is never closed in \"{s}\"");
        }

        /// <summary>A number, or the fallback if absent; a problem if it's there but not a number.</summary>
        public float Num(JObject o, string key, float fallback)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null)
                return fallback;
            if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer)
                return (float)t;
            Add(t, $"\"{key}\" should be a number, not {Kind(t)}");
            return fallback;
        }

        /// <summary>true/false, or null if absent; a problem if it's there but not true or false.</summary>
        public bool? Bool(JObject o, string key)
        {
            var t = o[key];
            if (t == null || t.Type == JTokenType.Null)
                return null;
            if (t.Type == JTokenType.Boolean)
                return (bool)t;
            Add(t, $"\"{key}\" should be true or false, not {Kind(t)}");
            return null;
        }

        /// <summary>A string or a list of strings, or null if absent; a problem for anything else in it.</summary>
        public string[] Strings(JToken t, string key)
        {
            if (t == null || t.Type == JTokenType.Null)
                return null;
            if (t.Type == JTokenType.String)
                return new[] { (string)t };
            if (t is JArray a)
            {
                var list = new List<string>(a.Count);
                foreach (var v in a)
                    if (v.Type == JTokenType.String)
                        list.Add((string)v);
                    else
                        Add(v, $"\"{key}\" should hold only text, not {Kind(v)}");
                return list.ToArray();
            }
            Add(t, $"\"{key}\" should be text or a list of text, not {Kind(t)}");
            return null;
        }

        /// <summary>A problem unless s (if given) is one of the allowed words (any case).</summary>
        public void OneOf(JObject o, string key, string s, params string[] allowed)
        {
            if (s == null)
                return;
            foreach (var a in allowed)
                if (string.Equals(s.Trim(), a, StringComparison.OrdinalIgnoreCase))
                    return;
            Add(o[key], $"\"{key}\": \"{s}\" isn't one of {string.Join(", ", allowed)}");
        }

        /// <summary>What a wrong value is, as said: "a list", "a number".</summary>
        public static string Kind(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Array: return "a list";
                case JTokenType.Object: return "an object";
                case JTokenType.Integer: case JTokenType.Float: return "a number";
                case JTokenType.Boolean: return "true/false";
                case JTokenType.String: return "text";
                default: return t.Type.ToString().ToLowerInvariant();
            }
        }
    }
}
