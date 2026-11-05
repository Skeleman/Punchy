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

        /// <summary>A value line for the run's summary: the run script prints every "[measure]" line.</summary>
        public static void Measure(string text) => Debug.Log("[measure] " + text);
    }
}
