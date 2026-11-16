using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// Everything the game logs while the tests run, kept so a test can read lines the game prints once at start-up
    /// ("[island] v3 ...; 108 landmarks, hash ...", "[fauna build] ... fingerprint ...").
    /// </summary>
    public static class LogWatch
    {
        static readonly List<string> lines = new List<string>();
        static bool on;

        public static void Start()
        {
            if (on)
                return;
            on = true;
            Application.logMessageReceivedThreaded += (text, stack, type) =>
            {
                lock (lines)
                    lines.Add(text);
            };
        }

        /// <summary>The last line containing the text, or null.</summary>
        public static string Last(string contains)
        {
            lock (lines)
                for (int i = lines.Count - 1; i >= 0; i--)
                    if (lines[i].Contains(contains))
                        return lines[i];
            return null;
        }

        /// <summary>How many lines have been kept so far: a mark to pass to After, so a wait only sees lines logged
        /// since. (Lines are kept across runs in one editor session, so a new line can read the same as an old one.)
        /// </summary>
        public static int Mark()
        {
            lock (lines)
                return lines.Count;
        }

        /// <summary>The first line logged at or after mark that contains every one of the texts, or null.</summary>
        public static string After(int mark, params string[] contains)
        {
            lock (lines)
                for (int i = System.Math.Max(mark, 0); i < lines.Count; i++)
                {
                    var all = true;
                    foreach (var c in contains)
                        all &= lines[i].Contains(c);
                    if (all)
                        return lines[i];
                }
            return null;
        }

        /// <summary>A value line for the run's summary: the run script prints every "[measure]" line.</summary>
        public static void Measure(string text) => Debug.Log("[measure] " + text);
    }
}
