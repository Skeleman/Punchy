using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// P-35 r3 row 3: the inventory holds ItemStacks. Item is the ItemTable row + 1 (0: nothing, and row 255, the old
    /// "nothing", maps to it); stacks merge only with the same item, Data and owner; TakeOne empties the last; the quest
    /// count over stacks (CountHeld) agrees with the tuple one (CountFor). Reached by reflection, as QuestChestTests does.
    /// </summary>
    public class ItemStackTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Stack => Game.GetType("Punchy.Items.ItemStack");
        static Type Items => Game.GetType("Punchy.Items.Items");
        static Type Table => Game.GetType("Punchy.Items.ItemTable");

        static byte Id(string name) => (byte)Table.GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();
        static object Of(byte row, int count, uint data = 0, uint owner = 0) => Stack.GetMethod("Of").Invoke(null, new object[] { row, count, data, owner });
        static T Get<T>(object s, string name) => (T)(Stack.GetProperty(name)?.GetValue(s) ?? Stack.GetField(name).GetValue(s));
        static bool Merges(object a, object b) => (bool)Stack.GetMethod("Merges").Invoke(a, new[] { b });

        [Test]
        public void IdIsRowPlusOne()
        {
            var torch = Id("Torch");
            var s = Of(torch, 3);
            Assert.AreEqual((ushort)(torch + 1), Get<ushort>(s, "Item"));
            Assert.AreEqual(torch, Get<byte>(s, "Row"));
            Assert.AreEqual((ushort)3, Get<ushort>(s, "Count"));
            Assert.IsFalse(Get<bool>(s, "IsEmpty"));
            Assert.AreEqual((ushort)0, (ushort)Stack.GetMethod("IdOf").Invoke(null, new object[] { (byte)255 }), "row 255 is nothing");
            Assert.AreEqual((ushort)1, (ushort)Stack.GetMethod("IdOf").Invoke(null, new object[] { (byte)0 }), "row 0 is id 1");
            Assert.IsTrue(Get<bool>(Of(255, 4), "IsEmpty"), "row 255 makes nothing");
            Assert.IsTrue(Get<bool>(Of(torch, 0), "IsEmpty"), "a count of 0 makes nothing");
            Assert.AreEqual((byte)255, Get<byte>(Activator.CreateInstance(Stack), "Row"), "an empty stack's row is 255");
        }

        [Test]
        public void MergesOnlySameItemDataAndOwner()
        {
            var torch = Id("Torch");
            Assert.IsTrue(Merges(Of(torch, 1), Of(torch, 5)), "same item, counts differ");
            Assert.IsFalse(Merges(Of(torch, 1), Of(Id("Rod"), 1)), "another item");
            Assert.IsFalse(Merges(Of(torch, 1), Of(torch, 1, data: 2)), "other Data");
            Assert.IsFalse(Merges(Of(torch, 1), Of(torch, 1, owner: 7)), "another owner's");
        }

        [Test]
        public void TakeOneEmptiesTheLast()
        {
            var s = Of(Id("Torch"), 2, 0, 9);
            var take = Stack.GetMethod("TakeOne");
            take.Invoke(s, null);   // (the box is the struct: it changes in place)
            Assert.AreEqual((ushort)1, Get<ushort>(s, "Count"));
            Assert.AreEqual(9u, Get<uint>(s, "Owner"));
            take.Invoke(s, null);
            Assert.IsTrue(Get<bool>(s, "IsEmpty"));
            Assert.AreEqual(0u, Get<uint>(s, "Owner"), "an emptied slot keeps no owner");
        }

        [Test]
        public void CountHeldAgreesWithCountFor()
        {
            var key = Id("Key");
            var stacks = Array.CreateInstance(Stack, 3);
            stacks.SetValue(Of(key, 2), 0);
            stacks.SetValue(Of(Id("Torch"), 1), 2);
            var tuples = new (byte, int)[] { (key, 2), (255, 0), (Id("Torch"), 1) };
            var held = (int)Items.GetMethod("CountHeld", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { "some-fetch", "Key", stacks });
            var count = (int)Items.GetMethod("CountFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { "some-fetch", "Key", tuples });
            Assert.AreEqual(2, held);
            Assert.AreEqual(count, held);
        }
    }
}
