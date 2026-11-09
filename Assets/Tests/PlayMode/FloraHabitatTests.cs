using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests.PlayMode
{
    /// <summary>
    /// W-126: flora habitat. Measures where chosen species stand against the ground (sun-facing slopes, relief) and takes the
    /// habitat shots. Numbers are logged, not asserted (they are the report); run it with PUNCHY_SHOT_TAG=old|new to name the shots.
    /// </summary>
    public class FloraHabitatTests
    {
        [UnitySetUp] public IEnumerator Up() => TestSceneSetup.Load();
        [OneTimeTearDown] public void Down() => TestSceneSetup.Restore();

        // (biome, species whose stats are logged, species stats region half-width)
        static readonly string[][] Views =
        {
            new[] { "Sonoran", "Saguaro" },
            new[] { "Temperate", "Oak" },
            new[] { "Mountains", "Pine" },
            new[] { "Tundra", "Spruce" },
            new[] { "FungalForest", "FungalGlowCap" },
        };

        [UnityTest]
        public IEnumerator Habitat_StatsAndShots()
        {
            var tag = System.Environment.GetEnvironmentVariable("PUNCHY_SHOT_TAG") ?? "new";
            var c = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var view in Views)
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", view[0]);
                if (spot.Length == 0)
                {
                    LogWatch.Measure($"habitat {view[0]}: no spot");
                    continue;
                }
                var st = Game.Hook<float[]>("FloraHabitat", view[1], spot[0], spot[2], 200f);
                if (st.Length > 0)
                    LogWatch.Measure($"habitat {view[0]} {view[1]} at {spot[0]:F0},{spot[2]:F0} (relief spread {spot[3]:F0} m): {st[0]:F0} plants, {st[5]:F2} per 100 m2, " +
                                     $"sun-facing {st[1]:F2} (ground {st[3]:F2}), relief {st[2]:F2} (ground {st[4]:F2}), blocks held {st[6]:F2}");
                yield return TestSceneSetup.GoTo(spot[0], spot[2], 10f);
                foreach (var side in new[] { "S", "N" })
                {
                    var dz = side == "S" ? -110f : 110f;
                    Game.RunOptions($"-camlook {spot[0].ToString(c)},{(spot[1] + 45f).ToString(c)},{(spot[2] + dz).ToString(c)},{spot[0].ToString(c)},{spot[1].ToString(c)},{spot[2].ToString(c)}");
                    yield return TestSceneSetup.Seconds(3f);
                    Shots.Take($"habitat-{view[0]}-{side}-{tag}", 1280, 720);
                }
            }
            yield return null;
        }
    }
}
