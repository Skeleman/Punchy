using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// A checked screenshot for tests (W-97): renders the main camera into Assets/Temp/shots/name-yyyyMMdd-HHmmss.png,
    /// then fails the test unless the file is there, was written after the call, and differs from the last shot of
    /// that name (a stale or frozen frame). Assets/Temp is never committed.
    /// </summary>
    public static class Shots
    {
        public static string Folder => Path.Combine(Application.dataPath, "Temp", "shots");

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

            Assert.IsTrue(File.Exists(path), $"shot {path} not written");
            // (File times are to the 100 ns, but the call time is taken first: allow the clock's own granularity.)
            Assert.GreaterOrEqual(File.GetLastWriteTime(path), called.AddMilliseconds(-20), "shot is older than the call");
            if (previous != null && previous.FullName != Path.GetFullPath(path))
                Assert.IsFalse(File.ReadAllBytes(previous.FullName).SequenceEqual(File.ReadAllBytes(path)),
                    $"shot is identical to the last one ({previous.Name})");
            LogWatch.Measure($"shot {Path.GetFileName(path)}{(previous != null ? " (differs from " + previous.Name + ")" : " (first of its name)")}");
            return path;
        }
    }
}
