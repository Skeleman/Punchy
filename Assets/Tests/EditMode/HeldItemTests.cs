using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// A row built on another (ItemDef.Base) is held as that one is: the Elder staff quest item is held like the Staff,
    /// not as a weapon. Reached by reflection, as QuestChestTests does.
    /// </summary>
    public class HeldItemTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Table => Game.GetType("Punchy.Items.ItemTable");

        static byte Id(string name) => (byte)Table.GetField(name, BindingFlags.Public | BindingFlags.Static).GetRawConstantValue();

        static string HeldFor(byte item) =>
            Game.GetType("Punchy.Items.Items").GetMethod("HeldFor", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { item }).ToString();

        [Test]
        public void ElderStaffIsHeldLikeTheStaff()
        {
            Assert.AreEqual("Staff", HeldFor(Id("Staff")));
            Assert.AreEqual("Staff", HeldFor(Id("ElderStaff")));
            Assert.AreEqual("None", HeldFor(Id("Sword")));
            Assert.AreEqual("None", HeldFor(255), "nothing in hand");
        }
    }
}
