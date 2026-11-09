using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests.PlayMode
{
    /// <summary>
    /// T-1: the shot tool's own shots. A dense forest and a town, each shot the old way (Shots.Take, an immediate render)
    /// and through the tool (Shots.Settled: ready gate, then the camera's own frame), so the two sit side by side. It also
    /// writes t1-shotlist.txt beside the shots: the same spots as absolute -goto/-camlook lines, for a player build's
    /// -shotlist, to show the editor and the build agree.
    /// </summary>
    public class ShotToolTests
    {
        [UnitySetUp] public IEnumerator Up() => TestSceneSetup.Load();
        [OneTimeTearDown] public void Down() => TestSceneSetup.Restore();

        static readonly System.Globalization.CultureInfo C = System.Globalization.CultureInfo.InvariantCulture;

        static string V(Vector3 v) => $"{v.x.ToString("F1", C)},{v.y.ToString("F1", C)},{v.z.ToString("F1", C)}";

        [UnityTest, Explicit("shots, run on demand"), Timeout(600000)]
        public IEnumerator SettledFrame_ForestAndTown()
        {
            var lines = new List<string>();
            IEnumerator Both(string name)
            {
                var cam = Camera.main;
                Assert.IsNotNull(cam, "no main camera");
                // The line that frames this from scratch in a player: the player under the camera's target, the camera posed.
                var at = cam.transform.position + cam.transform.forward * 30f;
                lines.Add($"-goto {at.x.ToString("F1", C)},{at.z.ToString("F1", C)} -time 12 -weather Clear -nohud -camlook {V(cam.transform.position)},{V(at)} -wait 3 -shot {name}");
                Shots.Take(name + "-old", 1280, 720);
                yield return Shots.Settled(name, 1280, 720);
            }

            var spot = Game.Hook<float[]>("FloraViewSpot", "Temperate");
            Assume.That(spot.Length > 0, "no temperate view spot");
            yield return TestSceneSetup.GoTo(spot[0], spot[2], 4f);
            Game.RunOptions($"-camlook {spot[0].ToString(C)},{(spot[1] + 40f).ToString(C)},{(spot[2] - 90f).ToString(C)},{spot[0].ToString(C)},{spot[1].ToString(C)},{spot[2].ToString(C)}");
            yield return TestSceneSetup.Seconds(1f);
            yield return Both("t1-forest-mid");
            Game.RunOptions($"-camlook {spot[0].ToString(C)},{(spot[1] + 6f).ToString(C)},{(spot[2] - 30f).ToString(C)},{spot[0].ToString(C)},{(spot[1] + 6f).ToString(C)},{spot[2].ToString(C)}");
            yield return TestSceneSetup.Seconds(1f);
            yield return Both("t1-forest-ground");

            yield return TestSceneSetup.Place("HumanVillage:centre", 6f);
            Game.RunOptions("-camrel 0,30,-55,0,3,0");
            yield return TestSceneSetup.Seconds(1f);
            yield return Both("t1-town");

            File.WriteAllLines(Path.Combine(Shots.Folder, "t1-shotlist.txt"), lines);
            LogWatch.Measure("t1 shot list:\n" + string.Join("\n", lines));
        }

        /// <summary>
        /// T-1 row 2: "-frame" on the subjects that came out badly framed before (W-126's saguaro slope and clearings, a
        /// cave mouth, an inn roof, a village from the water). Each case is one -frame line, then a settled shot; the
        /// "[frame]" line (heading, distance, rays hidden) goes into the summary. Fails if a subject can't be framed.
        /// </summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(900000)]
        public IEnumerator Framing_BySubject()
        {
            LogWatch.Start();
            IEnumerator Frame(string name, string line)
            {
                var before = LogWatch.Last("[frame]");
                Game.RunOptions("-time 12 -weather Clear -nohud " + line);
                var until = Time.realtimeSinceStartup + 40f;
                string got;
                while ((got = LogWatch.Last("[frame]")) == before && Time.realtimeSinceStartup < until)
                    yield return null;
                Assert.AreNotEqual(before, got, $"{name}: no [frame] line for '{line}'");
                Assert.IsFalse(got.Contains("FAIL"), $"{name}: {got}");
                LogWatch.Measure($"{name}: {got}");
                yield return Shots.Settled(name, 1280, 720);
            }

            var sonoran = Game.Hook<float[]>("FloraViewSpot", "Sonoran");
            Assume.That(sonoran.Length > 0, "no Sonoran view spot");
            yield return TestSceneSetup.GoTo(sonoran[0], sonoran[2], 4f);
            yield return Frame("t1-frame-saguaro", $"-frame flora:Saguaro@{sonoran[0].ToString("F0", C)},{sonoran[2].ToString("F0", C)} -framing mid");

            // A clearing: the tool's own finder ("clearing"), from the Temperate view spot (W-126's FramedShots subject).
            var spot = Game.Hook<float[]>("FloraViewSpot", "Temperate");
            Assume.That(spot.Length > 0, "no Temperate view spot");
            var at = $"{spot[0].ToString("F0", C)},{spot[2].ToString("F0", C)}";
            yield return TestSceneSetup.GoTo(spot[0], spot[2], 4f);
            yield return Frame("t1-frame-clearing-air", $"-frame clearing@{at} -framing aerial");
            yield return Frame("t1-frame-clearing-ground", $"-frame clearing@{at} -framing ground");

            yield return Frame("t1-frame-cave", "-frame CaveEntrance -framing close");
            yield return Frame("t1-frame-inn", "-frame flora:Inn -framing aerial");
            yield return Frame("t1-frame-village", "-frame flora:FishStiltHouse -framing mid -from water");
        }

        /// <summary>
        /// T-1 row 3: ten framed shots across five biomes, day (12:00) and dusk (18:10), each with its written check, then
        /// one seeded failure (a camera posed under the ground) to show a bad shot retried. Writes one small contact sheet
        /// (t1-selfcheck-sheet.jpg beside the shots) and measures every check. Fails if the sheet is 300 KB or more.
        /// </summary>
        [UnityTest, Explicit("shots, run on demand"), Timeout(1200000)]
        public IEnumerator SelfCheck_TenShots()
        {
            LogWatch.Start();
            Game.Hook<bool>("ShotRunStart");
            var labels = new List<string>();
            IEnumerator Framed(string label, string biome, string subject, string framing, float hour)
            {
                var spot = Game.Hook<float[]>("FloraViewSpot", biome);
                Assume.That(spot.Length > 0, $"no {biome} view spot");
                var at = $"{spot[0].ToString("F0", C)},{spot[2].ToString("F0", C)}";
                yield return TestSceneSetup.GoTo(spot[0], spot[2], 4f);
                // (A marker first: the same subject and framing twice logs the same [frame] line.)
                Debug.Log($"[frame] next: {label}");
                var before = LogWatch.Last("[frame]");
                Game.RunOptions($"-time {hour.ToString(C)} -weather Clear -nohud -frame {subject}@{at} -framing {framing}");
                var until = Time.realtimeSinceStartup + 40f;
                string got;
                while ((got = LogWatch.Last("[frame]")) == before && Time.realtimeSinceStartup < until)
                    yield return null;
                Assert.AreNotEqual(before, got, $"{label}: no [frame] line");
                Assert.IsFalse(got.Contains("FAIL"), $"{label}: {got}");
                labels.Add(label);
                yield return Shots.Settled("t1-check-" + label.Replace(' ', '-'), 1280, 720);
            }

            yield return Framed("temperate clearing day", "Temperate", "clearing", "aerial", 12f);
            yield return Framed("temperate clearing dusk", "Temperate", "clearing", "ground", 18.17f);
            yield return Framed("sonoran saguaro day", "Sonoran", "flora:Saguaro", "mid", 12f);
            yield return Framed("sonoran saguaro dusk", "Sonoran", "flora:Saguaro", "close", 18.17f);
            yield return Framed("tundra spruce day", "Tundra", "flora:Spruce", "mid", 12f);
            yield return Framed("tundra spruce dusk", "Tundra", "flora:Spruce", "ground", 18.17f);
            yield return Framed("fungal oak day", "FungalForest", "flora:FungalOak", "mid", 12f);
            yield return Framed("fungal oak dusk", "FungalForest", "flora:FungalOak", "mid", 18.17f);
            yield return Framed("mountain pine day", "Mountains", "flora:Pine", "aerial", 12f);
            yield return Framed("mountain pine dusk", "Mountains", "flora:Pine", "mid", 18.17f);

            // The seeded failure: a camera posed 6 m under the ground. Its check must say so and retry (lifted and turned).
            var p = Game.Hook<Vector3>("PlayerPosition");
            Game.RunOptions($"-time 12 -camlook {p.x.ToString("F1", C)},{(p.y - 6f).ToString("F1", C)},{p.z.ToString("F1", C)},{(p.x + 30f).ToString("F1", C)},{(p.y - 6f).ToString("F1", C)},{p.z.ToString("F1", C)}");
            yield return TestSceneSetup.Seconds(1f);
            labels.Add("seeded under ground");
            yield return Shots.Settled("t1-check-seeded-buried", 1280, 720);
            var seeded = LogWatch.Last("[shot] 't1-check-seeded-buried-");
            Assert.IsTrue(seeded != null && seeded.Contains("first inside geometry"), "the buried camera wasn't caught: " + seeded);

            var lines = Game.Hook<string[]>("ShotRunSheet", Path.Combine(Shots.Folder, "t1-selfcheck-sheet.jpg"), labels.ToArray());
            LogWatch.Measure("t1 self-check, sheet " + string.Join("\n", lines));
            Assert.Less(int.Parse(lines[0].Split(' ')[0]), 300, "contact sheet too big");
        }
    }
}
