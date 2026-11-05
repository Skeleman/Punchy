using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Punchy.Tests
{
    /// <summary>
    /// Puts the game in a known state for a play-mode test (W-97): the game scene loaded once per run with fixed test
    /// flags, then time of day, weather and spawns changed through the same test flags as Temp/PlayArgs.txt.
    /// The seed is the one authored in the terrain subscene (there is no seed flag), so a fixed island version gives a
    /// fixed island; the landmark hash test pins it.
    /// </summary>
    public static class TestSceneSetup
    {
        public const string Scene = "SampleScene";
        /// <summary>The flags every run starts with: island v3, noon, clear sky.</summary>
        public const string StartFlags = "-island v3 -time 12 -weather Clear";

        const string PlayArgs = "Temp/PlayArgs.txt";
        static bool loaded;
        static string savedPlayArgs;

        /// <summary>
        /// Loads the game scene (once per run; later calls return at once) and waits for the world to be up: the ground,
        /// the player and the fauna, then a few seconds more for the chunks round the player.
        /// </summary>
        public static IEnumerator Load(float timeout = 240f)
        {
            if (loaded && Game.Hook<bool>("WorldReady"))
                yield break;
            LogWatch.Start();
            // (The island version is read from the test flags when the island bakes, so they go in before the scene.)
            savedPlayArgs = File.Exists(PlayArgs) ? File.ReadAllText(PlayArgs) : null;
            File.WriteAllText(PlayArgs, StartFlags);
            SceneManager.LoadScene(Scene);
            var start = Time.realtimeSinceStartup;
            while (!Game.Hook<bool>("WorldReady") || LogWatch.Last("[fauna build]") == null)
            {
                if (Time.realtimeSinceStartup - start > timeout)
                    Assert.Fail($"world not up after {timeout:F0} s");
                yield return null;
            }
            yield return Seconds(6f);
            loaded = true;
            LogWatch.Measure($"world up in {Time.realtimeSinceStartup - start:F0} s");
        }

        /// <summary>Puts Temp/PlayArgs.txt back as it was before the run.</summary>
        public static void Restore()
        {
            if (savedPlayArgs == null && !loaded)
                return;
            if (savedPlayArgs != null)
                File.WriteAllText(PlayArgs, savedPlayArgs);
            else if (File.Exists(PlayArgs))
                File.Delete(PlayArgs);
        }

        /// <summary>The clock to this hour (0-24).</summary>
        public static IEnumerator TimeOfDay(float hour) => Options($"-time {hour.ToString(System.Globalization.CultureInfo.InvariantCulture)}", 1f);

        /// <summary>Weather forced over the player: clear, or rain.</summary>
        public static IEnumerator Weather(bool rain) => Options(rain ? "-weather Rain" : "-weather Clear", 2f);

        /// <summary>The player to x,z on the ground, then a few seconds for the chunks there.</summary>
        public static IEnumerator GoTo(float x, float z, float settle = 8f) =>
            Options($"-goto {x.ToString(System.Globalization.CultureInfo.InvariantCulture)},{z.ToString(System.Globalization.CultureInfo.InvariantCulture)}", settle);

        /// <summary>The player to a named place (a landmark, "meadow"...: DebugControls' "-place").</summary>
        public static IEnumerator Place(string name, float settle = 8f) => Options("-place " + name, settle);

        /// <summary>
        /// A species (its asset name, e.g. "Deer") spawned at x,z: the player goes there and the game spawns it beside
        /// them once the ground round them has settled (within radius m if given, else the game's 45). Waits for the
        /// game's "spawned" line.
        /// </summary>
        public static IEnumerator Spawn(string species, float x, float z, int count = 1, float radius = 0f)
        {
            var before = LogWatch.Last("DebugControls: spawned");
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var near = radius > 0f ? $" -spawnradius {radius.ToString(inv)}" : "";
            Game.RunOptions($"-goto {x.ToString(inv)},{z.ToString(inv)}{near} -spawn {species}:{count}");
            var start = Time.realtimeSinceStartup;
            while (LogWatch.Last("DebugControls: spawned") == before || !LogWatch.Last("DebugControls: spawned").Contains(species))
            {
                if (Time.realtimeSinceStartup - start > 30f)
                    Assert.Fail($"{species} not spawned within 30 s");
                yield return null;
            }
        }

        static IEnumerator Options(string line, float settle)
        {
            Game.RunOptions(line);
            yield return null;
            yield return Seconds(settle);
        }

        /// <summary>Real seconds (unscaled, so a test's own -timescale doesn't stretch them).</summary>
        public static IEnumerator Seconds(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }
    }
}
