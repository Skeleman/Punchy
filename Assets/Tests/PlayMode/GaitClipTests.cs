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
            yield return TestSceneSetup.TimeOfDay(10f);
            var at = Game.Hook<Vector3>("PlayerPosition");
            var inv = CultureInfo.InvariantCulture;
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
                    Time.captureFramerate = 30;
                    for (int lead = 0; lead < 20; lead++)
                        yield return null;   // (up to pace, and the camera pinned)
                    var frames = run ? 90 : 150;
                    for (int f = 0; f < frames; f++)
                    {
                        yield return new WaitForEndOfFrame();
                        Frame(Path.Combine(folder, $"f{f:0000}.jpg"));
                    }
                    Time.captureFramerate = 0;
                }
            Game.Hook<object>("GaitParadeClear");
            Game.RunOptions("-shotcam off");
        }

        static void Frame(string path)
        {
            var camera = Camera.main;
            Assert.IsNotNull(camera, "no main camera to shoot with");
            var target = RenderTexture.GetTemporary(854, 480, 24);
            var was = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = was;
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(854, 480, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 854, 480), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToJPG(90));
            Object.Destroy(image);
        }
    }
}
