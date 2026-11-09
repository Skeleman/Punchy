using System.Collections;
using System.Collections.Generic;
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

        [UnityTest, Explicit("shots, run on demand")]
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

        [UnityTest, Explicit("shots, run on demand")]
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

        /// <summary>
        /// W-126: two framed real-render shots. A forest clearing ringed by trees (found from the plan: no tree within 18 m, most within 60 m)
        /// and a Sonoran slope facing the sun, shot from the sunny side. Spots are picked from the placement and the heights, so old and new
        /// builds frame alike. Shots only; nothing asserted beyond the files existing.
        /// </summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Habitat_FramedShots()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            var tag = System.Environment.GetEnvironmentVariable("PUNCHY_SHOT_TAG") ?? "new";
            const float half = 150f;
            const int n = 75;
            foreach (var biome in new[] { "Mountains", "FungalForest", "Temperate" })
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", biome);
                if (spot.Length == 0)
                    continue;
                var plan = Game.Hook<float[]>("FloraPlan", spot[0], spot[2], half);
                var heights = Game.Hook<float[]>("FloraHeights", spot[0], spot[2], half, (float)n);
                var water = Game.Hook<float[]>("FloraGroundMask", spot[0], spot[2], half, (float)n);
                int Cell(float x, float z) => Mathf.Clamp((int)((z - (spot[2] - half)) / (2f * half) * n), 0, n - 1) * n + Mathf.Clamp((int)((x - (spot[0] - half)) / (2f * half) * n), 0, n - 1);
                var trees = new List<Vector2>();
                for (int i = 0; i + 3 < plan.Length; i += 4)
                    if ((int)plan[i + 2] == 2)
                        trees.Add(new Vector2(plan[i], plan[i + 1]));
                var bestAt = Vector2.zero;
                var bestRing = 0;
                for (var x = spot[0] - 90f; x <= spot[0] + 90f; x += 10f)
                    for (var z = spot[2] - 90f; z <= spot[2] + 90f; z += 10f)
                    {
                        int near = 0, ring = 0;
                        foreach (var t in trees)
                        {
                            var d = Vector2.Distance(t, new Vector2(x, z));
                            if (d < 18f) near++;
                            if (d < 60f) ring++;
                        }
                        if (near == 0 && ring > bestRing && ((int)water[Cell(x, z)] & 1) == 0)
                        {
                            bestRing = ring;
                            bestAt = new Vector2(x, z);
                        }
                    }
                LogWatch.Measure($"clearing {biome}: best open spot at {bestAt.x:F0},{bestAt.y:F0} with {bestRing} trees within 60 m of {trees.Count}");
                if (bestRing < 40)
                    continue;
                var cy = heights[Cell(bestAt.x, bestAt.y)];
                var camAt = Vector2.zero;
                var bestDiff = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    var a = k * Mathf.PI / 4f;
                    var p = bestAt + 38f * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var diff = Mathf.Abs(heights[Cell(p.x, p.y)] - cy) + (((int)water[Cell(p.x, p.y)] & 1) == 1 ? 50f : 0f);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        camAt = p;
                    }
                }
                // The player stands in the clearing and the camera is set relative to it, so the live ground sets the height. It looks
                // at the centre of the nearby trees, level, 3 m up.
                var cen = Vector2.zero;
                var cnt = 0;
                foreach (var t in trees)
                    if (Vector2.Distance(t, bestAt) < 60f)
                    {
                        cen += t;
                        cnt++;
                    }
                var dir = cnt > 0 ? (cen / cnt - bestAt) : Vector2.up;
                if (dir.magnitude < 8f)
                    dir = dir.sqrMagnitude < 0.01f ? Vector2.up * 30f : dir.normalized * 30f;
                yield return TestSceneSetup.GoTo(bestAt.x, bestAt.y, 10f);
                yield return TestSceneSetup.Seconds(10f);
                Game.RunOptions($"-camrel 0,3,0,{dir.x.ToString(c)},3,{dir.y.ToString(c)}");
                yield return TestSceneSetup.Seconds(14f);
                Shots.Take($"framed-clearing-{biome}-{tag}", 1280, 720);
                break;
            }
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", "Sonoran");
                if (spot.Length == 0)
                    yield break;
                var heights = Game.Hook<float[]>("FloraHeights", spot[0], spot[2], half, (float)n);
                var cell = 2f * half / n;
                // A cell is a sunny face if it is 12-30 degrees and its downhill points south (toward the sun, SunDirection z < 0).
                bool Sunny(int x, int y)
                {
                    if (x < 1 || y < 1 || x >= n - 1 || y >= n - 1)
                        return false;
                    var dx = (heights[y * n + x + 1] - heights[y * n + x - 1]) / (2f * cell);
                    var dz = (heights[(y + 1) * n + x] - heights[(y - 1) * n + x]) / (2f * cell);
                    var slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    return slope > 12f && slope < 30f && -dz > 0.6f * Mathf.Sqrt(dx * dx + dz * dz);
                }
                int bx = n / 2, by = n / 2, best = -1;
                for (int y = 8; y < n - 8; y++)
                    for (int x = 8; x < n - 8; x++)
                    {
                        int count = 0;
                        for (int dy = -6; dy <= 6; dy++)
                            for (int dx = -6; dx <= 6; dx++)
                                if (Sunny(x + dx, y + dy))
                                    count++;
                        if (count > best)
                        {
                            best = count;
                            bx = x;
                            by = y;
                        }
                    }
                var tx = spot[0] - half + (bx + 0.5f) * cell;
                var tz = spot[2] - half + (by + 0.5f) * cell;
                var ty = heights[by * n + bx];
                LogWatch.Measure($"slope Sonoran: sunny face at {tx:F0},{tz:F0}, {best} of 169 cells sunny");
                var camZ = tz - 60f;
                var camRow = Mathf.Clamp((int)((camZ - (spot[2] - half)) / (2f * half) * n), 0, n - 1);
                                yield return TestSceneSetup.GoTo(tx, tz - 35f, 10f);
                yield return TestSceneSetup.Seconds(10f);
                Game.RunOptions($"-camrel 0,6,0,0,{(ty + 2f - heights[Mathf.Clamp((int)((tz - 35f - (spot[2] - half)) / (2f * half) * n), 0, n - 1) * n + bx]).ToString(c)},35");
                yield return TestSceneSetup.Seconds(14f);
                Shots.Take($"framed-slope-Sonoran-{tag}", 1280, 720);
            }
        }

        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Clearings_PlanMaps()
        {
            foreach (var biome in (System.Environment.GetEnvironmentVariable("PUNCHY_PLAN_BIOMES") ?? "Temperate,Grassland,Mountains,FungalForest,Tundra,Sonoran,MesaCanyonlands,Dunes,SaltFlat,GlassDesert,Glacier,Ashlands,Volcano,EmberForest,ElectricWastelands,SparklingForest,FoggyMarsh,PoisonSwamp").Split(','))
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
                var mask = Game.Hook<float[]>("FloraGroundMask", spot[0], spot[2], half, 128f);
                if (mask.Length == 128 * 128)
                    for (int i = 0; i < px.Length; i++)
                        if (((int)mask[(i / 512 / 4) * 128 + (i % 512) / 4] & 1) == 1)
                            px[i] = new Color32(150, 190, 225, 255);   // water
                if (mask.Length == 128 * 128)
                {
                    var west = new System.Collections.Generic.SortedDictionary<int, int>();
                    var east = new System.Collections.Generic.SortedDictionary<int, int>();
                    for (int i = 0; i < mask.Length; i++)
                    {
                        var d = west; if (i % 128 >= 64) d = east;
                        var id = (int)mask[i] >> 1;
                        d[id] = (d.TryGetValue(id, out var c0) ? c0 : 0) + 1;
                    }
                    LogWatch.Measure($"mask {biome} west biomes {string.Join(",", west)} east {string.Join(",", east)}");
                }
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
