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

        [UnityTest]
        public IEnumerator Clearings_AerialAndGroundShots()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var view in new[] { new[] { "Temperate", "Oak" }, new[] { "Grassland", "Oak" }, new[] { "Mountains", "Pine" }, new[] { "FungalForest", "FungalGlowCap" } })
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", view[0]);
                if (spot.Length == 0)
                    continue;
                var st = Game.Hook<float[]>("FloraHabitat", view[1], spot[0], spot[2], 200f);
                if (st.Length > 0)
                    LogWatch.Measure($"grove {view[0]} {view[1]}: {st[0]:F0} plants, {st[5]:F2} per 100 m2, blocks held {st[6]:F2}");
                yield return TestSceneSetup.GoTo(spot[0], spot[2], 10f);
                Game.RunOptions($"-camlook {spot[0].ToString(c)},{(spot[1] + 220f).ToString(c)},{(spot[2] - 140f).ToString(c)},{spot[0].ToString(c)},{spot[1].ToString(c)},{spot[2].ToString(c)}");
                yield return TestSceneSetup.Seconds(3f);
                Shots.Take($"clear-{view[0]}-air", 1280, 720);
                Game.RunOptions($"-camlook {spot[0].ToString(c)},{(spot[1] + 2.2f).ToString(c)},{(spot[2] - 30f).ToString(c)},{spot[0].ToString(c)},{(spot[1] + 6f).ToString(c)},{spot[2].ToString(c)}");
                yield return TestSceneSetup.Seconds(3f);
                Shots.Take($"clear-{view[0]}-ground", 1280, 720);
            }
            yield return null;
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator Clearings_PlanMaps()
        {
            foreach (var biome in new[] { "Temperate", "Grassland", "Mountains", "FungalForest", "Tundra", "Sonoran", "MesaCanyonlands", "Dunes", "SaltFlat", "GlassDesert", "Glacier", "Ashlands", "Volcano", "EmberForest", "ElectricWastelands", "SparklingForest", "FoggyMarsh", "PoisonSwamp" })
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", biome);
                if (spot.Length == 0)
                    continue;
                const float half = 256f;
                var plan = Game.Hook<float[]>("FloraPlan", spot[0], spot[2], half);
                var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                var px = new Color32[512 * 512];
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32(214, 204, 176, 255);
                var counts = new int[3];
                for (int i = 0; i + 3 < plan.Length; i += 4)
                {
                    var layer = (int)plan[i + 2];
                    counts[layer]++;
                    var x = (int)(plan[i] - (spot[0] - half));
                    var y = (int)(plan[i + 1] - (spot[2] - half));
                    var col = layer == 2 ? new Color32(20, 90, 30, 255) : layer == 1 ? new Color32(190, 120, 30, 255) : new Color32(200, 40, 120, 255);
                    var r = layer == 2 ? 2 : layer == 1 ? 1 : 0;
                    for (int dx = -r; dx <= r; dx++)
                        for (int dy = -r; dy <= r; dy++)
                        {
                            var xx = x + dx; var yy = y + dy;
                            if (xx >= 0 && xx < 512 && yy >= 0 && yy < 512)
                                px[yy * 512 + xx] = col;
                        }
                }
                tex.SetPixels32(px);
                tex.Apply();
                var dir = System.IO.Path.Combine(Application.dataPath, "Temp", "shots");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"plan-{biome}-{(System.Environment.GetEnvironmentVariable("PUNCHY_SHOT_TAG") ?? "new")}.png"), tex.EncodeToPNG());
                LogWatch.Measure($"plan {biome} at {spot[0]:F0},{spot[2]:F0}: layer 0 {counts[0]}, 1 {counts[1]}, 2 {counts[2]} plants");
                yield return null;
            }
        }
    }
}
