using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// M-06 row 5: a species carrying a unit (a Model Lab blueprint) grows exactly what the coded builder for that unit
    /// grows, so a blueprint saved from the Lab can stand in for a builder. Reached by reflection, as UnitLibraryTests does.
    /// </summary>
    public class UnitBlueprintTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Generator => Game.GetType("Punchy.Flora.FloraGenerator");

        static Mesh[] Grow(ScriptableObject species, int variant)
        {
            var model = Generator.GetMethod("Grow", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { species, variant, false });
            return (Mesh[])model.GetType().GetField("Lods").GetValue(model);
        }

        static void AssertSame(Mesh coded, Mesh unit, string what)
        {
            Assert.AreEqual(coded.vertices, unit.vertices, what + " vertices");
            Assert.AreEqual(coded.triangles, unit.triangles, what + " triangles");
            Assert.AreEqual(coded.colors32, unit.colors32, what + " colours");
        }

        [TestCase("Stall", "StallUnit")]
        [TestCase("Smithy", "SmithyUnit")]
        public void SpeciesWithAUnitGrowsLikeTheCodedBuilder(string speciesName, string unitField)
        {
            var coded = AssetDatabase.LoadAssetAtPath<ScriptableObject>($"Assets/Game/Flora/{speciesName}.asset");
            Assert.IsNotNull(coded, speciesName);
            var blueprint = UnityEngine.Object.Instantiate(coded);
            blueprint.name = coded.name; // the seed hashes the name
            try
            {
                var unit = Generator.GetField(unitField, BindingFlags.Public | BindingFlags.Static).GetValue(null);
                blueprint.GetType().GetField("Unit").SetValue(blueprint, unit);
                for (int variant = 0; variant < 2; variant++)
                {
                    var a = Grow(coded, variant);
                    var b = Grow(blueprint, variant);
                    for (int lod = 0; lod < a.Length; lod++)
                        AssertSame(a[lod], b[lod], $"{speciesName} variant {variant} LOD {lod}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(blueprint); }
        }
    }
}
