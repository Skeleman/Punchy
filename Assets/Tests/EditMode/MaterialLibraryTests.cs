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
        // Shaders that still draw a family their own way, waiting on their owners (handed off from W-166). Empty since
        // Creature.shader draws its coats through Materials/Coat.hlsl; kept so a future hand-off has a place to go.
        static readonly (string include, string shader)[] Pending = { };

        [Test]
        public void StranglerRootsAreStableAndLeaveTheFarLevelAlone()
        {
            var species = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Flora/Bogwood.asset");
            var field = species.GetType().GetField("Strangler");
            var grow = Game.GetType("Punchy.Flora.FloraGenerator").GetMethod("Grow", BindingFlags.Public | BindingFlags.Static);
            int[] Count()
            {
                var counts = new List<int>();
                for (int v = 0; v < 4; v++)
                {
                    var model = grow.Invoke(null, new object[] { species, v, false });
                    var lods = (Mesh[])model.GetType().GetField("Lods").GetValue(model);
                    counts.AddRange(lods.Select(m => m != null ? m.triangles.Length : 0));
                }
                return counts.ToArray();
            }
            var keep = (float)field.GetValue(species);
            try
            {
                var on = Count();
                Assert.AreEqual(on, Count(), "roots differ between two builds");
                field.SetValue(species, 0f);
                var off = Count();
                for (int i = 2; i < on.Length; i += 3)
                    Assert.AreEqual(off[i], on[i], "roots reached the far level");
                Assert.Greater(on.Sum(), off.Sum(), "no variant grew roots");
            }
            finally { field.SetValue(species, keep); }
        }

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
            Assert.That(used.Any(u => u.Item1 == "Paint"), "paint has no slot");
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
            ("Paint.hlsl", @"\w*Paint\w*", @"PunchyPaint\w*\s*\(", new[] { "Flora.shader" }),
            ("Ice.hlsl", @"\w*Ice(Block|Depth|Joint)\w*", @"PunchyIce\w*\s*\(", new[] { "Flora.shader" }),
            ("Stone.hlsl", @"\w*(Stone(Surface|Blocks|Course|Rubble|Spec)|Ashlar)\w*", @"PunchyStone\w*\s*\(", new[] { "Flora.shader", "Cave.shader", "SunkenTemple.shader", "TerrainSurface.shader" }),
            ("Bone.hlsl", @"\w*Bone(Pit|Line|Ivory)\w*", @"PunchyBone\w*\s*\(", new[] { "Creature.shader" }),
            ("Coat.hlsl", @"\w*Coat(Fur|Feathers|Scales|Skin|Chitin)\w*", @"PunchyCoat\w*\s*\(", new[] { "Creature.shader" }),
        };

        // Coverings (W-166 #336): one library row per creature coat kind, so the look in Materials/Coat.hlsl and
        // Fauna.CoatKind cannot drift apart, and each row is usable (a pattern size and a highlight power above 0).
        [Test]
        public void EveryCoatKindHasARow()
        {
            var coatKind = Game.GetType("Punchy.Fauna.CoatKind", true);
            var library = Game.GetType("Punchy.Terrain.MaterialLibrary", true);
            var rows = (Array)library.GetField("Coat").GetValue(null);
            var max = (int)library.GetField("MaxCoatKinds").GetValue(null);
            var kinds = Enum.GetValues(coatKind).Length;
            Assert.AreEqual(kinds, rows.Length, "MaterialLibrary.Coat needs one row per CoatKind");
            Assert.LessOrEqual(kinds, max, "Coat.hlsl holds at most MaxCoatKinds rows");
            var problems = new List<string>();
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows.GetValue(i);
                var size = (float)row.GetType().GetField("Size").GetValue(row);
                var power = (float)row.GetType().GetField("SpecPower").GetValue(row);
                if (size <= 1e-3f || power <= 1e-3f)
                    problems.Add($"{Enum.GetName(coatKind, i)}: size {size}, spec power {power} (0 reads as an unpushed row)");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // Cloth and metal (#336): one row per kind, none zero where Cloth.hlsl or Metal.hlsl divides or raises by it.
        [TestCase("ClothKind", "Cloth", "MaxClothKinds", "Size", "Contrast")]
        [TestCase("MetalKind", "Metal", "MaxMetalKinds", "SpecPower", "GlintPower")]
        public void EveryClothAndMetalKindHasARow(string kindName, string table, string maxName, string first, string second)
        {
            var kind = Game.GetType("Punchy.Terrain." + kindName, true);
            var library = Game.GetType("Punchy.Terrain.MaterialLibrary", true);
            var rows = (Array)library.GetField(table).GetValue(null);
            var max = (int)library.GetField(maxName).GetValue(null);
            var kinds = Enum.GetValues(kind).Length;
            Assert.AreEqual(kinds, rows.Length, $"MaterialLibrary.{table} needs one row per {kindName}");
            Assert.LessOrEqual(kinds, max, $"{table}.hlsl holds at most {maxName} rows");
            var problems = new List<string>();
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows.GetValue(i);
                var a = (float)row.GetType().GetField(first).GetValue(row);
                var b = (float)row.GetType().GetField(second).GetValue(row);
                if (a <= 1e-3f || b <= 1e-3f)
                    problems.Add($"{Enum.GetName(kind, i)}: {first} {a}, {second} {b} (0 reads as an unpushed row)");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // Stone (W-166 row 2): a plain scarp bakes exactly as before stone came in (no shape change, zero paint offsets),
        // and every biome names a real row of MaterialLibrary.Stone, so its scarps and river notches have a rock to be.
        [Test]
        public void PlainStoneLeavesAScarpAlone()
        {
            var scarp = Game.GetType("Punchy.Terrain.Scarp", true);
            var kind = Game.GetType("Punchy.Terrain.StoneKind", true);
            var stone = Game.GetType("Punchy.Terrain.ScarpBake", true).GetMethod("Stone");
            var sc = Activator.CreateInstance(scarp);
            foreach (var (f, v) in new[] { ("Crack", 1.3f), ("Push", 2.1f), ("Setback", 0.7f) }) scarp.GetField(f).SetValue(sc, v);
            var args = new object[] { Enum.ToObject(kind, 0), sc };
            stone.Invoke(null, args);
            sc = args[1];
            float F(string f) => (float)scarp.GetField(f).GetValue(sc);
            Assert.AreEqual(1.3f, F("Crack"), 1e-5f);
            Assert.AreEqual(2.1f, F("Push"), 1e-5f);
            Assert.AreEqual(0.7f, F("Setback"), 1e-5f);
            foreach (var f in new[] { "Beds", "Joints", "Polish", "Gully", "Chimneys" }) Assert.AreEqual(0f, F(f), 1e-6f, f);
            var tint = scarp.GetField("Tint").GetValue(sc);
            foreach (var c in new[] { "x", "y", "z" }) Assert.AreEqual(0f, (float)tint.GetType().GetField(c).GetValue(tint), 1e-6f, "Tint." + c);
        }

        [Test]
        public void EveryBiomeNamesAStone()
        {
            var kind = Game.GetType("Punchy.Terrain.StoneKind", true);
            var biome = Game.GetType("Punchy.Terrain.BiomeDefinition", true);
            var rows = (Array)Game.GetType("Punchy.Terrain.MaterialLibrary", true).GetField("Stone").GetValue(null);
            var problems = new List<string>();
            var guids = AssetDatabase.FindAssets("t:" + biome.Name);
            Assert.IsNotEmpty(guids, "no biome assets found");
            foreach (var guid in guids)
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), biome);
                var value = Convert.ToInt32(biome.GetField("Stone").GetValue(asset));
                if (!Enum.IsDefined(kind, value) || value >= rows.Length) problems.Add($"{asset.name}: stone {value}");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

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
                var family = Path.GetFullPath(Path.Combine(root, "Materials", include));
                foreach (var path in files)
                {
                    var name = Path.GetFileName(path);
                    var inMaterials = path.Replace('\\', '/').Contains("/Materials/");
                    var text = File.ReadAllText(path);
                    var pending = Pending.Contains((include, name));
                    if (!inMaterials && !pending && defines.IsMatch(text))
                        problems.Add($"{name} defines its own {include} look ({defines.Match(text).Value.Trim()}): draw it through Materials/{include}");
                    if (!inMaterials && !pending && name != include && draws.IsMatch(text) && !Includes(path, root).Contains(family))
                        problems.Add($"{name} draws {include}'s family without including Materials/{include}");
                }
                foreach (var name in users)
                {
                    var path = Path.Combine(root, name);
                    var reached = Includes(path, root);
                    // The draw may sit in a shared include on the way (Cave and TerrainSurface reach Stone through
                    // Materials/RockWall.hlsl), so the shader's own text and everything it pulls in short of the family count.
                    var drawn = reached.Where(p => p != family).Prepend(Path.GetFullPath(path)).Any(p => draws.IsMatch(File.ReadAllText(p)));
                    if (!reached.Contains(family) || !drawn)
                        problems.Add($"{name} should draw its {include} family through Materials/{include}");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        // W-163 row 1: the one wood grain keeps its layers (a domain warp, darker dirt bands, a seed per piece of
        // timber), each detail fades by on-screen size (rule 20), and the shaders that draw wood still compile.
        [Test]
        public void WoodGrainKeepsItsLayers()
        {
            var text = File.ReadAllText(Path.Combine(Application.dataPath, "Game", "Shaders", "Materials", "Wood.hlsl"));
            var grain = Regex.Match(text, @"half3 PunchyWoodGrain\(.*?\n}", RegexOptions.Singleline).Value;
            Assert.IsNotEmpty(grain, "PunchyWoodGrain is missing");
            StringAssert.Contains("q += shift", grain, "the ring noise should be read through a warped coordinate");
            StringAssert.Contains("smoothstep(0.5, 0.9, dirt)", grain, "dirt should come in darker bands");
            foreach (var fade in new[] { "fRing", "fPore", "fKnot", "fDirt" })
                Assert.IsTrue(Regex.IsMatch(grain, fade + @" = PunchyWoodFade\("), fade + " should fade by on-screen size");
            StringAssert.Contains("PunchyWoodAlongAxis(albedo, p, axisCode, footprint, kind, PunchyWoodPieceSeed(key))", text,
                "posts and beams should each get their own seed");
        }

        [Test]
        public void WoodShadersCompile()
        {
            var problems = new List<string>();
            foreach (var name in new[] { "Punchy/Flora", "Punchy/Cave", "Punchy/Creature" })
            {
                var shader = Shader.Find(name);
                if (shader == null) { problems.Add(name + " not found"); continue; }
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        problems.Add($"{name}: {m.message} ({m.file}:{m.line})");
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        static readonly Regex IncludeLine = new(@"^\s*#include(_with_pragmas)?\s+""([^""]+)""", RegexOptions.Multiline);

        /// <summary>Every project shader file a file reaches through #include, followed through chains (a family
        /// include reached via another include counts). Paths resolve next to the including file, then under Shaders;
        /// package includes don't resolve and are skipped.</summary>
        static HashSet<string> Includes(string path, string root)
        {
            var seen = new HashSet<string>();
            var todo = new Stack<string>();
            todo.Push(Path.GetFullPath(path));
            while (todo.Count > 0)
            {
                var file = todo.Pop();
                foreach (Match m in IncludeLine.Matches(File.ReadAllText(file)))
                {
                    var rel = m.Groups[2].Value;
                    var found = new[] { Path.Combine(Path.GetDirectoryName(file), rel), Path.Combine(root, rel) }
                        .Select(Path.GetFullPath).FirstOrDefault(File.Exists);
                    if (found != null && seen.Add(found)) todo.Push(found);
                }
            }
            return seen;
        }
    }
}
