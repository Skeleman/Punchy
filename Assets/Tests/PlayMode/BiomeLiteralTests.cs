using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// W-116 row 2 (audit 5): game code finds biomes by family and tag (BiomeTags), never by asset name. This fails if a
    /// biome's asset name shows up as a string literal in non-Editor game code, outside the files still waiting on the
    /// animal lane. No scene needed: it only reads the source files.
    /// </summary>
    public class BiomeLiteralTests
    {
        /// <summary>Files allowed to keep biome-name literals for now: the colossi, which A-81 moves to ColossusDefinition assets.</summary>
        static readonly string[] AllowList = { "StoneColossi.cs", "DunesColossus.cs" };

        /// <summary>Target: no "BiomeName" literal (any biome asset's exact name, quoted) in Assets/Game scripts outside Editor folders and the allow-list.</summary>
        [Test]
        public void Biomes_NoNameLiteralsInGameCode()
        {
            var assets = Application.dataPath;
            var names = Directory.GetFiles(Path.Combine(assets, "Game", "Biomes"), "*.asset")
                .Select(Path.GetFileNameWithoutExtension).ToArray();
            Assert.That(names.Length, Is.GreaterThan(0), "no biome assets found");
            var pattern = new Regex("\"(" + string.Join("|", names.Select(Regex.Escape)) + ")\"");
            var hits = Directory.GetFiles(Path.Combine(assets, "Game"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Editor/") && !AllowList.Contains(Path.GetFileName(f)))
                .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, line, i)))
                .Where(h => pattern.IsMatch(h.line))
                .Select(h => $"{Path.GetFileName(h.f)}:{h.i + 1}: {h.line.Trim()}")
                .ToList();
            Debug.Log($"[measure] biome literals: {names.Length} biome names, {hits.Count} literal(s) outside the allow-list ({string.Join(", ", AllowList)})");
            Assert.That(hits, Is.Empty, "biome-name literals in game code (look biomes up with BiomeTags instead):\n" + string.Join("\n", hits));
        }
    }
}
