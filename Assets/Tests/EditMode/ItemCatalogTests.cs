using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// P-35 row 2 (#26): the item catalog is today's ItemTable as data, so nothing changes in play. Its fingerprint
    /// from id 1 on equals ItemTable's, row for row; entry 0 is "nothing"; keys are unique and never empty. Reached by
    /// reflection, as QuestChestTests does, since the test assembly can't reference Assembly-CSharp.
    /// </summary>
    public class ItemCatalogTests
    {
        static Assembly game;
        static Assembly Game => game ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Catalog => Game.GetType("Punchy.Items.ItemCatalog");
        static Array Table => (Array)Game.GetType("Punchy.Items.ItemTable").GetField("Items").GetValue(null);
        static Array Defs => (Array)Catalog.GetProperty("Defs").GetValue(null);
        static ulong Fingerprint(Array table, int first) => (ulong)Catalog.GetMethod("Fingerprint").Invoke(null, new object[] { table, first });
        static string KeyOf(int id) => (string)Catalog.GetMethod("KeyOf").Invoke(null, new object[] { (ushort)id });

        [Test]
        public void CatalogIsTheItemTable()
        {
            var table = Table;
            var defs = Defs;
            Assert.AreEqual(table.Length + 1, defs.Length, "the catalog has an entry per ItemTable row plus 'nothing' (regenerate it)");
            Assert.AreEqual(Fingerprint(table, 0), Fingerprint(defs, 1), "the catalog's fingerprint differs from ItemTable's (regenerate it)");
            // Row for row, so a failure names the item.
            var one = Array.CreateInstance(table.GetType().GetElementType(), 1);
            var other = Array.CreateInstance(table.GetType().GetElementType(), 1);
            for (int i = 0; i < table.Length; i++)
            {
                one.SetValue(table.GetValue(i), 0);
                other.SetValue(defs.GetValue(i + 1), 0);
                Assert.AreEqual(Fingerprint(one, 0), Fingerprint(other, 0), $"item {i} ({KeyOf(i + 1)}) differs");
            }
        }

        [Test]
        public void KeysAreUniqueAndZeroIsNothing()
        {
            Assert.AreEqual("none", KeyOf(0));
            var seen = new HashSet<string>();
            for (int id = 0; id < Defs.Length; id++)
            {
                var key = KeyOf(id);
                Assert.IsFalse(string.IsNullOrEmpty(key), $"id {id} has no key");
                Assert.IsTrue(seen.Add(key), $"key {key} is used twice");
            }
        }

        /// <summary>A field added to ItemDef must be added to ItemAsset too (and to its Fingerprint), or the catalog
        /// would silently drop it.</summary>
        [Test]
        public void ItemAssetHasEveryItemDefField()
        {
            var asset = Game.GetType("Punchy.Items.ItemAsset");
            foreach (var f in Game.GetType("Punchy.Items.ItemDef").GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsNotNull(asset.GetField(f.Name == "Name" ? "DisplayName" : f.Name), $"ItemAsset has no {f.Name}");
        }

        [Test]
        public void FingerprintSeesAChange()
        {
            var table = Table;
            var copy = (Array)table.Clone();
            var row = copy.GetValue(0);
            var weight = row.GetType().GetField("Weight");
            weight.SetValue(row, (float)weight.GetValue(row) + 0.001f);
            copy.SetValue(row, 0);
            Assert.AreNotEqual(Fingerprint(table, 0), Fingerprint(copy, 0));
        }
    }
}
