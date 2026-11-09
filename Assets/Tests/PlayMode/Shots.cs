using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// A checked screenshot for tests (W-97): renders the main camera into Assets/Temp/shots/name-yyyyMMdd-HHmmss.png,
    /// then fails the test unless the file is there, was written after the call, and differs from the last shot of
    /// that name (a stale or frozen frame). Assets/Temp is never committed.
    /// T-1: <see cref="Settled"/> is the reliable one: the game's ShotTool waits at its ready gate (terrain, flora tiles
    /// and shadow cache, caves, a still camera), then shoots the camera's own frame, so trees are never missing or
    /// placeholders. Take is the old immediate render, kept for tests that shoot mid-action.
    /// </summary>
    public static class Shots
    {
        public static string Folder => Path.Combine(Application.dataPath, "Temp", "shots");

        /// <summary>The file the last <see cref="Settled"/> wrote.</summary>
        public static string LastPath { get; private set; }

        /// <summary>
        /// Yield on this: a shot through the game's ShotTool into Assets/Temp/shots/name-yyyyMMdd-HHmmss.png. Fails the
        /// test with the tool's own line (what it was waiting on) when the world isn't ready within timeout seconds.
        /// </summary>
        public static IEnumerator Settled(string name, int width = 1280, int height = 720, float timeout = 60f)
        {
            var called = DateTime.Now;
            Directory.CreateDirectory(Folder);
            var previous = new DirectoryInfo(Folder).GetFiles(name + "-*.png").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            var path = Path.Combine(Folder, $"{name}-{called:yyyyMMdd-HHmmss}.png");
            var tool = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp").GetType("Punchy.Debugging.ShotTool", true);
            object Static(string member) => tool.GetProperty(member, BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var started = (bool)tool.GetMethod("Take", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { path, width, height, timeout, true });
            Assert.IsTrue(started, "the shot tool is busy with another shot");
            while ((bool)Static("Busy"))
                yield return null;
            var log = (string)Static("LastLog");
            if (Static("Status").ToString() != "Done")
                Assert.Fail(log);
            Check(path, called, previous);
            LastPath = path;
        }

        /// <summary>
        /// The old immediate render (no ready gate, no written check), kept so existing tests behave as before. New shots
        /// use <see cref="Settled"/>: see "How to take shots" in docs/agents/testing.md.
        /// </summary>
        public static string Take(string name, int width = 960, int height = 540)
        {
            var called = DateTime.Now;
            Directory.CreateDirectory(Folder);
            var previous = new DirectoryInfo(Folder).GetFiles(name + "-*.png").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            var path = Path.Combine(Folder, $"{name}-{called:yyyyMMdd-HHmmss}.png");

            var camera = Camera.main;
            Assert.IsNotNull(camera, "no main camera to shoot with");
            var target = RenderTexture.GetTemporary(width, height, 24);
            var was = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = was;
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            Check(path, called, previous);
            return path;
        }

        static void Check(string path, DateTime called, FileInfo previous)
        {
            Assert.IsTrue(File.Exists(path), $"shot {path} not written");
            // (File times are to the 100 ns, but the call time is taken first: allow the clock's own granularity.)
            Assert.GreaterOrEqual(File.GetLastWriteTime(path), called.AddMilliseconds(-20), "shot is older than the call");
            if (previous != null && previous.FullName != Path.GetFullPath(path))
                Assert.IsFalse(File.ReadAllBytes(previous.FullName).SequenceEqual(File.ReadAllBytes(path)),
                    $"shot is identical to the last one ({previous.Name})");
            LogWatch.Measure($"shot {Path.GetFileName(path)}{(previous != null ? " (differs from " + previous.Name + ")" : " (first of its name)")}");
        }
    }
}
