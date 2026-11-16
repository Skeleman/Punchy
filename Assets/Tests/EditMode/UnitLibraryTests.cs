using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// W-175: units are data made of library parts. Every part a unit names is in the part library, unit ids are unique,
    /// and a unit survives a JSON round trip unchanged (the form the Model Lab saves, M-06 row 5). The test assembly
    /// can't reference Assembly-CSharp, so the game is reached by reflection, as MaterialLibraryTests does.
    /// </summary>
    public class UnitLibraryTests
    {
        static Assembly game;
        static Assembly Game => game ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Generator => Game.GetType("Punchy.Flora.FloraGenerator");
        static Type UnitDef => Game.GetType("Punchy.Flora.UnitDef");

        static IEnumerable<object> Units() =>
            ((IEnumerable)Generator.GetProperty("Units", BindingFlags.Public | BindingFlags.Static).GetValue(null)).Cast<object>();

        static string Id(object unit) => (string)UnitDef.GetField("Id").GetValue(unit);

        [Test]
        public void EveryUnitPartIsInTheLibrary()
        {
            var library = new HashSet<string>((IEnumerable<string>)Generator.GetProperty("PartIds", BindingFlags.Public | BindingFlags.Static).GetValue(null));
            var missing = new List<string>();
            foreach (var unit in Units())
                foreach (var part in (Array)UnitDef.GetField("Parts").GetValue(unit))
                {
                    var id = (string)part.GetType().GetField("Part").GetValue(part);
                    if (!library.Contains(id))
                        missing.Add($"{Id(unit)}: {id}");
                }
            Assert.IsNotEmpty(Units().ToList(), "no units found");
            Assert.IsEmpty(missing, "unit parts missing from the part library: " + string.Join(", ", missing));
        }

        [Test]
        public void UnitIdsAreUnique()
        {
            var ids = Units().Select(Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "duplicate unit ids: " + string.Join(", ", ids));
        }

        [Test]
        public void UnitsRoundTripThroughJson()
        {
            foreach (var unit in Units())
            {
                var json = JsonUtility.ToJson(unit);
                var back = JsonUtility.FromJson(json, UnitDef);
                Assert.AreEqual(json, JsonUtility.ToJson(back), $"{Id(unit)} changed through JSON");
                Assert.AreEqual(((Array)UnitDef.GetField("Parts").GetValue(unit)).Length, ((Array)UnitDef.GetField("Parts").GetValue(back)).Length, Id(unit));
            }
        }
    }
}
