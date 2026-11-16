using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
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

        // ---- W-175 row 4: the part format

        static Type UnitPart => Game.GetType("Punchy.Flora.UnitPart");

        static object Part(string id, Vector3 at = default, Vector3 spanMetres = default, float yaw = 0f, bool mirror = false)
        {
            var p = Activator.CreateInstance(UnitPart);
            UnitPart.GetField("Part").SetValue(p, id);
            UnitPart.GetField("AtMetres").SetValue(p, at);
            UnitPart.GetField("SpanMetres").SetValue(p, spanMetres);
            UnitPart.GetField("Yaw").SetValue(p, yaw);
            UnitPart.GetField("Mirror").SetValue(p, mirror);
            return p;
        }

        static object Unit(bool partStreams, params object[] parts)
        {
            var unit = Activator.CreateInstance(UnitDef);
            UnitDef.GetField("Id").SetValue(unit, "test");
            UnitDef.GetField("PartStreams").SetValue(unit, partStreams);
            var array = Array.CreateInstance(UnitPart, parts.Length);
            for (int k = 0; k < parts.Length; k++)
                array.SetValue(parts[k], k);
            UnitDef.GetField("Parts").SetValue(unit, array);
            return unit;
        }

        static Mesh GrowUnit(object unit)
        {
            var host = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Flora/Stall.asset");
            Assert.IsNotNull(host, "Stall species");
            var model = Generator.GetMethod("GrowUnit", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { host, unit, 0 });
            return ((Mesh[])model.GetType().GetField("Lods").GetValue(model))[0];
        }

        [Test]
        public void TidyJsonReadsBackTheSameUnit()
        {
            var units = Units().ToList();
            var saved = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Game/Flora/Units" })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Select(t => JsonUtility.FromJson(t.text, UnitDef)).ToList();
            foreach (var unit in units.Concat(saved))
            {
                var tidy = (string)UnitDef.GetMethod("ToJson").Invoke(unit, null);
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(tidy, @"\.\d{9}"), $"{Id(unit)}: long floats left in {tidy}");
                Assert.AreEqual(JsonUtility.ToJson(unit), JsonUtility.ToJson(JsonUtility.FromJson(tidy, UnitDef)), $"{Id(unit)} changed through tidy JSON");
            }
        }

        [Test]
        public void YawTurnsAPartAboutItsAt()
        {
            var at = new Vector3(1f, 0.45f, 0f);
            var size = new Vector3(2f, 0.9f, 0.5f);
            var plain = GrowUnit(Unit(false, Part("counter", at, size))).bounds;
            var turned = GrowUnit(Unit(false, Part("counter", at, size, yaw: 90f))).bounds;
            Assert.AreEqual(plain.center.x, turned.center.x, 0.01f, "turned about At");
            Assert.AreEqual(plain.size.x, turned.size.z, 0.01f, "width becomes depth");
            Assert.AreEqual(plain.size.z, turned.size.x, 0.01f, "depth becomes width");
        }

        [Test]
        public void MirrorFlipsAPartLeftToRightAndKeepsItFacingOut()
        {
            var plain = GrowUnit(Unit(false, Part("anvil", new Vector3(0.3f, 0f, 0f))));
            var mirrored = GrowUnit(Unit(false, Part("anvil", new Vector3(0.3f, 0f, 0f), mirror: true)));
            Assert.AreEqual(plain.bounds.size.x, mirrored.bounds.size.x, 0.001f);
            // Every face still faces out: a triangle's winding agrees with its vertices' normals.
            var v = mirrored.vertices;
            var n = mirrored.normals;
            var t = mirrored.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (face.sqrMagnitude < 1e-10f)
                    continue;
                Assert.Greater(Vector3.Dot(face, n[t[i]] + n[t[i + 1]] + n[t[i + 2]]), 0f, $"triangle {i / 3} faces in");
            }
        }

        [Test]
        public void WithPartStreamsAPartLooksTheSameWhateverComesBefore()
        {
            var fire = new Vector3(0f, 0f, 0f);
            var span = new Vector3(0.8f, 0f, 0.8f);
            var alone = GrowUnit(Unit(true, Part("log tepee", fire, span))).vertices;
            var after = GrowUnit(Unit(true, Part("wares", new Vector3(-1f, 0.9f, 0f)), Part("log tepee", fire, span))).vertices;
            Assert.Greater(after.Length, alone.Length);
            CollectionAssert.AreEqual(alone, after.Skip(after.Length - alone.Length).ToArray(), "the logs moved when wares came first");
        }

        // ---- W-175 row 5: the building parts

        static object GrowModel(object unit)
        {
            var host = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Flora/Stall.asset");
            return Generator.GetMethod("GrowUnit", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { host, unit, 0 });
        }

        static System.Collections.IList Boxes(object model) => (System.Collections.IList)model.GetType().GetField("Boxes").GetValue(model);

        [Test]
        public void EveryBuildingPartIsInTheLibraryGrowsAndCollides()
        {
            var ids = (string[])Generator.GetField("BuildPartIds").GetValue(null);
            var library = ((IEnumerable<string>)Generator.GetProperty("PartIds").GetValue(null)).ToList();
            foreach (var id in ids)
            {
                Assert.Contains(id, library);
                // Lines (wall, beam, rail) run from At to a To a metre and a half along x.
                var part = Part(id);
                UnitPart.GetField("ToMetres").SetValue(part, new Vector3(1.5f, id == "beam" ? 1f : 0f, 0f));
                var model = GrowModel(Unit(false, part));
                var mesh = ((Mesh[])model.GetType().GetField("Lods").GetValue(model))[0];
                Assert.Greater(mesh.vertexCount, 0, $"{id} drew nothing");
                // Lamps and glass are walk-through (W-67); everything else built is a box.
                if (id == "lantern")
                    Assert.AreEqual(0, Boxes(model).Count, "a lantern blocks");
                else
                    Assert.Greater(Boxes(model).Count, 0, $"{id} has no collision box");
            }
        }

        [Test]
        public void ATurnedDoorTurnsItsBoxToo()
        {
            var plain = Boxes(GrowModel(Unit(false, Part("door", new Vector3(1f, 0f, 0f)))));
            var turned = Boxes(GrowModel(Unit(false, Part("door", new Vector3(1f, 0f, 0f), yaw: 90f))));
            Assert.AreEqual(plain.Count, turned.Count);
            var box = plain[0].GetType();
            Vector3 Centre(object b)
            {
                var c = box.GetField("Centre").GetValue(b);   // a float3
                float F(string n) => (float)c.GetType().GetField(n).GetValue(c);
                return new Vector3(F("x"), F("y"), F("z"));
            }
            // The leaf stands on At, so it turns about its own foot: same x and z, still the same height.
            Assert.AreEqual(Centre(plain[0]).y, Centre(turned[0]).y, 1e-4f);
            Assert.AreEqual(1f, Centre(turned[0]).x, 1e-4f);
            // The step in front (+z) swings round with it: a 90 degree yaw takes +z to +x.
            Assert.Greater(Centre(turned[1]).x, 1.2f, "the step didn't swing round with the door");
        }

        // W-175 row 6: nested units, the unit library, JSON units and snapping.
        static Type Library => Game.GetType("Punchy.Flora.UnitLibrary");

        static object Nest(string id, Vector3 at)
        {
            var p = Part("unit", at);
            UnitPart.GetField("Unit").SetValue(p, id);
            return p;
        }

        [Test]
        public void ANestedUnitDrawsLikeTheUnitAloneMovedToItsSpot()
        {
            var child = Unit(true, Part("crate", Vector3.zero, new Vector3(0.6f, 0.6f, 0.6f)), Part("beam", new Vector3(0.5f, 0f, 0f), new Vector3(0.1f, 1.2f, 0.1f)));
            UnitDef.GetField("Id").SetValue(child, "r6 test child");
            UnitDef.GetField("Size").SetValue(child, new Vector3(2f, 2f, 2f));
            Library.GetMethod("Register").Invoke(null, new[] { child });
            Assert.AreSame(child, Library.GetMethod("Find").Invoke(null, new object[] { "r6 test child" }));

            var alone = GrowUnit(child);
            var nested = GrowUnit(Unit(false, Nest("r6 test child", new Vector3(2f, 0f, 1f))));
            Assert.AreEqual(alone.vertexCount, nested.vertexCount, "the nested unit drew different parts");
            var shift = nested.bounds.center - alone.bounds.center;
            Assert.Less((shift - new Vector3(2f, 0f, 1f)).magnitude, 0.05f, $"moved by {shift}, not to its At");
            Assert.Less((nested.bounds.size - alone.bounds.size).magnitude, 0.05f, "the nested unit changed size");
        }

        [Test]
        public void AUnitNestingItselfStops()
        {
            var loop = Unit(false, Part("crate", Vector3.zero, new Vector3(0.5f, 0.5f, 0.5f)), Nest("r6 test loop", new Vector3(1f, 0f, 0f)));
            UnitDef.GetField("Id").SetValue(loop, "r6 test loop");
            Library.GetMethod("Register").Invoke(null, new[] { loop });
            Assert.Greater(GrowUnit(loop).vertexCount, 0);
        }

        [Test]
        public void AUnitReadsBackFromItsJson()
        {
            var unit = Unit(true, Part("wall", new Vector3(1f, 0f, 0f)), Nest("stall", new Vector3(0f, 0f, 2f)));
            var json = (string)UnitDef.GetMethod("ToJson").Invoke(unit, null);
            var back = Library.GetMethod("FromJson").Invoke(null, new object[] { json });
            Assert.IsNotNull(back, json);
            var parts = (Array)UnitDef.GetField("Parts").GetValue(back);
            Assert.AreEqual(2, parts.Length);
            Assert.AreEqual("stall", UnitPart.GetField("Unit").GetValue(parts.GetValue(1)));
            Assert.AreEqual(new Vector3(1f, 0f, 0f), UnitPart.GetField("AtMetres").GetValue(parts.GetValue(0)));
            Assert.IsNull(Library.GetMethod("FromJson").Invoke(null, new object[] { "not json" }));
        }

        [Test]
        public void SnapJoinsNearPointsAndRoundsToTheGrid()
        {
            var unit = Unit(false, Part("wall", new Vector3(1f, 0f, 0f)), Part("wall", new Vector3(1.08f, 0.01f, 0f)), Part("wall", new Vector3(3.07f, 0f, 0f)));
            var joined = (int)UnitDef.GetMethod("Snap").Invoke(unit, new object[] { 0.05f, 0.12f });
            var parts = (Array)UnitDef.GetField("Parts").GetValue(unit);
            Vector3 At(int k) => (Vector3)UnitPart.GetField("AtMetres").GetValue(parts.GetValue(k));
            Assert.AreEqual(1, joined);
            Assert.AreEqual(At(0), At(1), "the near point didn't join");
            Assert.Less(Mathf.Abs(At(2).x - 3.05f), 1e-4f, "not rounded to 5 cm");
        }
    }
}
