using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Punchy.Tests
{
    /// <summary>
    /// Per-person gait (A-124 row 2): planted feet don't slide for any way of walking, and clips of each folk race
    /// walking down a town street and running past, one person of each gait style a row.
    /// </summary>
    public class GaitClipTests
    {
        [UnitySetUp]
        public IEnumerator Up() => TestSceneSetup.Load();

        [OneTimeTearDown]
        public void Down()
        {
            Time.captureFramerate = 0;
            TestSceneSetup.Restore();
        }

        static readonly string[] Races =
        {
            "Human", "HumanChild", "Elf", "Dwarf", "Goblin", "Ogre", "Hermit", "CavePygmy", "BatFolk", "FishFolk",
            "SeaFolk", "SeaChild", "TurtleFolk", "TurtleChild"
        };

        /// <summary>
        /// Target: no gait style slides its planted feet more than the even walk does, plus 1 cm per metre (every upright
        /// species, walking and running, posed by the animator as in play).
        /// </summary>
        [UnityTest]
        public IEnumerator Styles_KeepFeetPlanted()
        {
            yield return TestSceneSetup.Place("HumanVillage", 8f);
            var at = Game.Hook<Vector3>("PlayerPosition");
            var lines = Game.Hook<string>("GaitSlip", at.x, at.z);
            Assert.IsNotEmpty(lines, "no upright species to walk");
            float evenWalk = 0f, evenRun = 0f, worst = 0f;
            var worstLine = "";
            foreach (var line in lines.Split('\n'))
            {
                var f = line.Split(' ');
                if (f.Length < 5)
                    continue;
                float walk = float.Parse(f[2], CultureInfo.InvariantCulture), run = float.Parse(f[3], CultureInfo.InvariantCulture);
                if (f[1] == "Even")
                {
                    evenWalk = walk;
                    evenRun = run;
                    continue;
                }
                var over = Mathf.Max(walk - evenWalk, run - evenRun);
                if (over > worst)
                {
                    worst = over;
                    worstLine = line;
                }
            }
            LogWatch.Measure("gait slip (cm/m walk run, stance sore/good):\n" + lines);
            LogWatch.Measure($"gait slip: worst over the even walk {worst:0.00} cm/m ({worstLine})");
            Assert.LessOrEqual(worst, 1f, "a gait style slides its planted feet: " + worstLine);
        }

        /// <summary>
        /// Clips, run on demand: per race a 5 s walk down the street and a 3 s run past at 30 fps, frames to
        /// Assets/Temp/gait/race-walk|run/ (made into videos outside, then Assets/Temp deleted).
        /// </summary>
        [UnityTest, Explicit("clips, run on demand"), Timeout(3600000)]
        public IEnumerator Clips_PerRace()
        {
            yield return TestSceneSetup.Place("HumanVillage", 10f);
            // (Place leaves the player at the town's edge: the parade goes through its middle, 12 m short of the player.)
            var start = Game.Hook<Vector3>("PlayerPosition");
            var centre = new float[0];
            for (int i = 0; i < 240 && centre.Length == 0; i++)
            {
                var towns = Game.Hook<float[]>("TownPlans");
                for (int t = 1; t < towns.Length && centre.Length == 0; t++)
                {
                    var c = Game.Hook<float[]>("TownCentre", (int)towns[t]);
                    if (c.Length == 3 && Mathf.Abs(c[0] - start.x) < c[2] + 40f && Mathf.Abs(c[1] - start.z) < c[2] + 40f)
                        centre = c;
                }
                if (centre.Length == 0)
                    yield return TestSceneSetup.Seconds(0.5f);
            }
            Assert.IsNotEmpty(centre, "the human village has no plan");
            yield return TestSceneSetup.GoTo(centre[0], centre[1] + 12f, 8f);
            yield return TestSceneSetup.TimeOfDay(10f);
            var at = Game.Hook<Vector3>("PlayerPosition");
            at.z -= 15f;
            var inv = CultureInfo.InvariantCulture;
            var first = true;
            foreach (var race in Races)
                foreach (var run in new[] { false, true })
                {
                    var back = run ? 9f : 4.5f;
                    var made = Game.Hook<float[]>("GaitParade", race, at.x, at.z + 3f, run, back);
                    if (made[0] <= 0f)
                    {
                        LogWatch.Measure($"gait clip: {race} not spawned");
                        continue;
                    }
                    LogWatch.Measure($"gait clip {race} {(run ? "run" : "walk")}: {Game.HookField<string>("GaitTrail")}");
                    var y = made[1];
                    var middle = at.z + 3f + (made[0] - 1f) * 0.9f;
                    Game.RunOptions(string.Format(inv, "-shotcam {0},{1},{2},{3},{4},{5}", at.x, y + 3.4f, middle - 11f, at.x, y + 0.9f, middle));
                    var folder = Path.Combine(Application.dataPath, "Temp", "gait", $"{race}-{(run ? "run" : "walk")}");
                    Directory.CreateDirectory(folder);
                    if (first)
                    {
                        // (The first clip waits for the street around the camera to be in, as every shot does.)
                        yield return Shots.Settled("a124-gait-first", 854, 480);
                        first = false;
                        made = Game.Hook<float[]>("GaitParade", race, at.x, at.z + 3f, run, back);
                    }
                    // (60 world frames a second: a clip frame takes two, so the clip plays at 30 fps in real time.)
                    Time.captureFramerate = 60;
                    var walkedOn = 0;
                    Shots.ClipBegin(854, 480);
                    for (int lead = 0; lead < 40; lead++)
                    {
                        Time.captureFramerate = 60;   // (each frame: the debug flags reset it whenever options apply)
                        walkedOn += Game.Hook<int>("GaitParadeStep", Time.deltaTime);
                        yield return null;   // (up to pace, and the camera pinned)
                    }
                    var front = Game.Hook<float[]>("GaitParadeAt");
                    LogWatch.Measure(string.Format(inv, "gait clip {0}: front row at {1:0.0},{2:0.0},{3:0.0} in world {4}, camera looks at {5:0.0},{6:0.0}",
                                                   race, front[0], front[1], front[2], front[3], at.x, middle));
                    var frames = run ? 90 : 150;
                    for (int f = 0; f < frames; f++)
                    {
                        Time.captureFramerate = 60;   // (each frame: the debug flags reset it whenever options apply)
                        walkedOn += Game.Hook<int>("GaitParadeStep", 2f * Time.deltaTime);
                        // (Through the shot tool's late render: a plain camera.Render here misses creatures and flora.)
                        yield return Shots.ClipFrame(Path.Combine(folder, $"f{f:0000}.jpg"));
                    }
                    yield return Shots.ClipEnd();
                    Time.captureFramerate = 0;
                    var end = Game.Hook<float[]>("GaitParadeAt");
                    LogWatch.Measure(string.Format(inv, "gait clip {0}: front row ends at {1:0.0},{2:0.0} after {3} frames (dt {4:0.000}; {5} steps walked by the hook)",
                                                   race, end[0], end[2], frames, Time.deltaTime, walkedOn));
                }
            Game.Hook<object>("GaitParadeClear");
            Game.RunOptions("-shotcam off");
        }
    }
}
