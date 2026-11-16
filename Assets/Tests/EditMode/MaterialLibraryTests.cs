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
        // Shaders that still draw a family their own way, waiting on their owners (handed off from W-166): TerrainSurface.shader
        // (render's) reaches stone through the old Shaders/Stone.hlsl forwarder.
        static readonly (string include, string shader)[] Pending = { ("Stone.hlsl", "TerrainSurface.shader") };

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

        // W-166 row 3: each family with its own include gets a bark mark slot clear of every other decode in Flora.shader:
        // wood 0.01-0.035, rock strata 0.06-0.09, glowing seams over 0.15; the shader reads a slot as its value +-0.005.
        [Test]
        public void FamilyMarksHaveTheirOwnSlots()
        {
            var builder = Game.GetType("Punchy.Flora.FloraMeshBuilder", true);
            var mark = builder.GetMethod("FamilyMark", BindingFlags.Public | BindingFlags.Static);
            var familyType = Game.GetType("Punchy.Terrain.MaterialFamily", true);
            var used = new List<(string, float)>();
            foreach (var name in Enum.GetNames(familyType))
            {
                var m = (float)mark.Invoke(null, new[] { Enum.Parse(familyType, name) });
                if (m == 0f) continue;
                Assert.That(m > 0.04f && m < 0.145f && (m < 0.055f || m > 0.095f), $"{name}'s mark {m} sits in another decode's band");
                foreach (var (other, o) in used)
                    Assert.That(Mathf.Abs(m - o) > 0.011f, $"{name} ({m}) and {other} ({o}) share a slot");
                used.Add((name, m));
            }
            Assert.That(used.Any(u => u.Item1 == "Plaster"), "plaster has no slot");
            Assert.That(used.Any(u => u.Item1 == "Brick"), "brick has no slot");
            Assert.That(used.Any(u => u.Item1 == "Ice"), "ice has no slot");
            Assert.That(used.Any(u => u.Item1 == "Stone"), "built stone has no slot");
        }

        // Each family's include: what defining its look by hand looks like, what drawing it looks like, and which
        // shaders must draw it through the include (W-166 rows 2-3).
        static readonly (string include, string defines, string draws, string[] users)[] Families =
        {
            ("Wood.hlsl", @"\w*(WoodGrain|LogEnd|Plank|Shingle)\w*", @"PunchyWood\w*\s*\(", new[] { "Flora.shader", "Cave.shader", "Creature.shader" }),
            ("Thatch.hlsl", @"\w*(Thatch|Straw)\w*", @"PunchyThatch\w*\s*\(", new[] { "Flora.shader" }),
            ("Cloth.hlsl", @"\w*(Linen|Hide|Weave)\w*", @"Punchy(Linen|Hide)\w*\s*\(", new[] { "Flora.shader", "Creature.shader" }),
            ("Metal.hlsl", @"\w*Metal(Shade|Glint)\w*", @"PunchyMetal\w*\s*\(", new[] { "Flora.shader", "Creature.shader" }),
            ("Plaster.hlsl", @"\w*Plaster\w*", @"PunchyPlaster\w*\s*\(", new[] { "Flora.shader" }),
            ("Brick.hlsl", @"\w*Brick\w*", @"PunchyBrick\w*\s*\(", new[] { "Flora.shader" }),
            ("Ice.hlsl", @"\w*Ice(Block|Depth|Joint)\w*", @"PunchyIce\w*\s*\(", new[] { "Flora.shader" }),
            ("Stone.hlsl", @"\w*(Stone(Surface|Blocks|Course|Rubble|Spec)|Ashlar)\w*", @"PunchyStone\w*\s*\(", new[] { "Flora.shader", "Cave.shader", "SunkenTemple.shader" }),
            ("Bone.hlsl", @"\w*Bone(Pit|Line|Ivory)\w*", @"PunchyBone\w*\s*\(", new[] { "Creature.shader" }),
        };

        // Gear (W-166 row 3): every creature gear material maps to a family, and Creature.shader is a user of that family's
        // include in the table above, so SegmentMaterial and the library cannot drift apart.
        [Test]
        public void GearDrawsFromTheLibrary()
        {
            var segment = Game.GetType("Punchy.Fauna.SegmentMaterial", true);
            var library = Game.GetType("Punchy.Terrain.MaterialLibrary", true);
            var familyOf = library.GetMethod("FamilyOf", new[] { segment });
            Assert.IsNotNull(familyOf, "MaterialLibrary.FamilyOf(SegmentMaterial) is missing");
            var problems = new List<string>();
            foreach (var name in Enum.GetNames(segment))
            {
                var family = familyOf.Invoke(null, new[] { Enum.Parse(segment, name) }).ToString();
                if (name == "Body") { if (family != "None") problems.Add($"Body maps to {family}"); continue; }
                var include = (family == "Leather" ? "Cloth" : family) + ".hlsl";
                if (!Families.Any(f => f.include == include && f.users.Contains("Creature.shader")))
                    problems.Add($"gear {name} -> {family}: Creature.shader isn't a user of Materials/{include}");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void OnlyTheFamilyIncludeDrawsAFamily()
        {
            var root = Path.Combine(Application.dataPath, "Game", "Shaders");
            var problems = new List<string>();
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".shader") || p.EndsWith(".hlsl")).ToArray();
            foreach (var (include, definesName, drawsName, users) in Families)
            {
                var defines = new Regex(@"^\s*(half3|half|float3|float)\s+" + definesName + @"\s*\(", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                var draws = new Regex(drawsName);
                var quoted = $"\"Materials/{include}\"";
                foreach (var path in files)
                {
                    var name = Path.GetFileName(path);
                    var inMaterials = path.Replace('\\', '/').Contains("/Materials/");
                    var text = File.ReadAllText(path);
                    var pending = Pending.Contains((include, name));
                    if (!inMaterials && !pending && defines.IsMatch(text))
                        problems.Add($"{name} defines its own {include} look ({defines.Match(text).Value.Trim()}): draw it through Materials/{include}");
                    if (!inMaterials && !pending && name != include && draws.IsMatch(text) && !text.Contains(quoted))
                        problems.Add($"{name} draws {include}'s family without including Materials/{include}");
                }
                foreach (var name in users)
                {
                    var text = File.ReadAllText(Path.Combine(root, name));
                    if (!text.Contains(quoted) || !draws.IsMatch(text))
                        problems.Add($"{name} should draw its {include} family through Materials/{include}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
