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
    }
}
