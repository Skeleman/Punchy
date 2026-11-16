using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// W-166: the one material library holds. Every built piece states what it's made of (no bark left without a
    /// family), timber forms draw their timber as wood, and only the family's include draws a family: a shader that
    /// draws wood includes Shaders/Materials/Wood.hlsl instead of carrying its own grain. The test assembly can't
    /// reference Assembly-CSharp, so the game is reached by reflection, as the play-mode tests do.
    /// </summary>
    public class MaterialLibraryTests
    {
        static Assembly game;
        static Assembly Game => game ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");

        // Forms that are all timber but for small fittings: their wood must draw as wood.
        static readonly string[] TimberForms = { "Stable", "Pen", "Stall", "HitchRail", "Signpost", "Palisade", "RailFence", "MineFrame" };
        // Shaders that still draw wood their own way, waiting on their owners (handed off from W-166).
        static readonly string[] PendingWood = { "Creature.shader" };

        [Test]
        public void EveryBuiltPieceHasAMaterial()
        {
            var generator = Game.GetType("Punchy.Flora.FloraGenerator", true);
            var founded = generator.GetMethod("Founded", BindingFlags.Public | BindingFlags.Static);
            var families = generator.GetMethod("BarkFamilies", BindingFlags.Public | BindingFlags.Static);
            var speciesType = Game.GetType("Punchy.Flora.FloraSpecies", true);
            var familyNames = Enum.GetNames(Game.GetType("Punchy.Terrain.MaterialFamily", true));
            var problems = new List<string>();
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:FloraSpecies"))
            {
                var species = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), speciesType);
                if (species == null) continue;
                var form = speciesType.GetField("Form").GetValue(species);
                var formName = form.ToString();
                var timber = TimberForms.Contains(formName);
                if (formName == "Stump" || !(bool)founded.Invoke(null, new[] { form }) && !timber)
                    continue;
                var elf = speciesType.GetField("KitCulture").GetValue(species).ToString().Contains("Elf");
                for (int variant = 0; variant < 2; variant++)
                {
                    var counts = (int[])families.Invoke(null, new[] { species, (object)variant });
                    checkedCount++;
                    if (counts[0] > 0)
                        problems.Add($"{species.name} ({formName}) v{variant}: {counts[0]} bark vertices with no material");
                    if (timber && !elf && counts[1] == 0)
                        problems.Add($"{species.name} ({formName}) v{variant}: timber form with no wood");
                }
            }
            Assert.Greater(checkedCount, 0, "no built species found");
            Assert.IsEmpty(problems, string.Join("\n", problems) + $"\n(families: {string.Join(", ", familyNames)})");
        }

        [Test]
        public void OnlyTheFamilyIncludeDrawsAFamily()
        {
            var root = Path.Combine(Application.dataPath, "Game", "Shaders");
            var definesWood = new Regex(@"^\s*(half3|half|float3|float)\s+\w*(WoodGrain|LogEnd|Plank|Shingle)\w*\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var drawsWood = new Regex(@"PunchyWood\w*\s*\(");
            var problems = new List<string>();
            foreach (var path in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".shader") || p.EndsWith(".hlsl")))
            {
                var name = Path.GetFileName(path);
                var inMaterials = path.Replace('\', '/').Contains("/Materials/");
                var text = File.ReadAllText(path);
                if (!inMaterials && !PendingWood.Contains(name) && definesWood.IsMatch(text))
                    problems.Add($"{name} defines its own wood ({definesWood.Match(text).Value.Trim()}): draw it through Materials/Wood.hlsl");
                if (!inMaterials && drawsWood.IsMatch(text) && !text.Contains("\"Materials/Wood.hlsl\""))
                    problems.Add($"{name} draws wood without including Materials/Wood.hlsl");
            }
            foreach (var name in new[] { "Flora.shader", "Cave.shader" })
            {
                var text = File.ReadAllText(Path.Combine(root, name));
                if (!text.Contains("\"Materials/Wood.hlsl\"") || !drawsWood.IsMatch(text))
                    problems.Add($"{name} should draw its wood through Materials/Wood.hlsl");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
