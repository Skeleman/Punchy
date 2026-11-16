using System.IO;
using Punchy.Terrain;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// W-140: after a Windows player build, writes the island cache's stamp (what the code and assets look like now) into
    /// the build's StreamingAssets/IslandCache, so the player can key and save its own island file; and ships the
    /// editor's island file there too when it was baked under that same stamp (play once after a code or asset change to
    /// rebake it). The player still checks the file's key, so a stale file only means a fresh bake.
    /// W-168 row 2: release builds ship it too. Player builds pick Burst code by CPU, so two machines baking the same seed
    /// fresh can differ slightly; loading the shipped file means every player of a build starts on the same island. A
    /// build that leaves it out says so with a warning.
    /// No ProjectSettings change: the files are copied into the built player's data folder, not into Assets.
    /// </summary>
    public class IslandCacheBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 && report.summary.platform != BuildTarget.StandaloneWindows) return;
            var exe = report.summary.outputPath;
            var data = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data");
            if (!Directory.Exists(data)) { Debug.LogWarning($"[island] build step: no player data folder at {Path.GetFileName(data)}"); return; }
            var folder = Path.Combine(data, "StreamingAssets", IslandCache.Folder);
            var stamp = IslandCache.EditorStamp(null, out var reason);
            if (stamp == null) { Debug.LogWarning($"[island] build step: no island cache in this build ({reason})"); return; }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, IslandCache.StampFile), stamp);
            var shipped = "stamp only";
            var cache = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", IslandCache.Folder);
            var copied = 0;
            if (Directory.Exists(cache))
                foreach (var file in Directory.GetFiles(cache, "island-v*.bin"))
                {
                    var name = Path.GetFileName(file);
                    var baked = File.Exists(file + IslandCache.StampSuffix) ? File.ReadAllText(file + IslandCache.StampSuffix).Trim() : null;
                    if (baked != stamp) { shipped += $"; {name} left out (baked under another stamp: play once to rebake)"; continue; }
                    File.Copy(file, Path.Combine(folder, name), true);
                    // (The key's part digests: a player miss then names the input that moved.)
                    if (File.Exists(file + IslandCache.PartsSuffix)) File.Copy(file + IslandCache.PartsSuffix, Path.Combine(folder, name + IslandCache.PartsSuffix), true);
                    shipped += $"; {name} {new FileInfo(file).Length / (1024.0 * 1024.0):F2} MB";
                    copied++;
                }
            if (copied == 0)
                Debug.LogWarning($"[island] build step: no island file shipped ({shipped}): each player will bake the island on its own CPU. Play once in the editor, then build again.");
            else
                Debug.Log($"[island] build step: StreamingAssets/{IslandCache.Folder}: {shipped}");
        }
    }
}
