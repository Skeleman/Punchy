using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// P-49 r3 (#27): quest-only items. A quest-only item (ItemDef.Quest) completes only its own quest: the heirloom's key
    /// never completes lost-key, and no generic item check counts it. A random chest fill (ItemTable.RandomLoot) never
    /// hands one out. Reached by reflection, as QuestChestTests does.
    /// </summary>
    public class QuestItemTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Items => Game.GetType("Punchy.Items.Items");
        static Type Table => Game.GetType("Punchy.Items.ItemTable");

        static int Id(string name) => (byte)Table.GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();

        static int CountFor(string quest, string item, params (byte, int)[] held) =>
            (int)Items.GetMethod("CountFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { quest, item, held });

        [Serializable] class QuestDef { public string id, type, item; }
        [Serializable] class QuestFile { public QuestDef[] quests; }

        static QuestDef Quest(string id) =>
            JsonUtility.FromJson<QuestFile>(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "quests.json"))).quests.First(q => q.id == id);

        [Test]
        public void HeirloomKeyDoesNotCompleteLostKey()
        {
            var lost = Quest("lost-key");
            var heirloom = Quest("heirloom");
            var key = (byte)Id("HeirloomKey");
            Assert.AreEqual(0, CountFor(lost.id, lost.item, (key, 1)), "the heirloom's key counted for lost-key");
            Assert.AreEqual(1, CountFor(heirloom.id, heirloom.item, (key, 1)), "the heirloom's key didn't count for the heirloom");
            Assert.AreEqual(1, CountFor(lost.id, lost.item, ((byte)Id("LostKey"), 1)), "lost-key's own key didn't count");
            Assert.AreEqual(0, CountFor(heirloom.id, heirloom.item, ((byte)Id("LostKey"), 1)), "lost-key's key counted for the heirloom");
            // (An ordinary key completes neither, and a generic check for "Key" never counts a quest-only key.)
            Assert.AreEqual(0, CountFor(lost.id, lost.item, ((byte)Id("Key"), 3)));
            Assert.AreEqual(0, CountFor("some-fetch", "Key", (key, 1), ((byte)Id("LostKey"), 1)));
            Assert.AreEqual(2, CountFor("some-fetch", "Key", ((byte)Id("Key"), 2), (key, 1)));
        }

        [Test]
        public void ElderStaffOnlyCompletesItsQuest()
        {
            var staff = Quest("elder-staff");
            Assert.AreEqual(1, CountFor(staff.id, staff.item, ((byte)Id("ElderStaff"), 1)));
            Assert.AreEqual(0, CountFor(staff.id, staff.item, ((byte)Id("Staff"), 1)));
            Assert.AreEqual(0, CountFor("some-fetch", "Staff", ((byte)Id("ElderStaff"), 1)));
        }

        [Test]
        public void RandomChestFillNeverYieldsAQuestItem()
        {
            var rngType = Type.GetType("Unity.Mathematics.Random, Unity.Mathematics");
            var create = rngType.GetMethod("CreateFromIndex", BindingFlags.Public | BindingFlags.Static);
            var loot = Table.GetMethod("RandomLoot", BindingFlags.Public | BindingFlags.Static);
            var items = (Array)Table.GetField("Items", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var questField = items.GetValue(0).GetType().GetField("Quest");
            var from = Id("Torch");
            var seen = new bool[items.Length];
            for (uint seed = 1; seed <= 20000; seed++)
            {
                var args = new object[] { create.Invoke(null, new object[] { seed }), from };
                var item = (byte)loot.Invoke(null, args);
                Assert.That(item, Is.InRange(from, items.Length - 1));
                Assert.IsNull(questField.GetValue(items.GetValue(item)), $"seed {seed}: the random fill gave quest-only item {item}");
                seen[item] = true;
            }
            // (Every ordinary item from the torch on still comes up.)
            for (int i = from; i < items.Length; i++)
                if (questField.GetValue(items.GetValue(i)) == null)
                    Assert.IsTrue(seen[i], $"item {i} never came up");
        }
    }
}
