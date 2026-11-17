using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// P-35 r3 row 5: ids from placement records (WorldIds). A record's id is its kind and baked position, so it doesn't
    /// depend on order and moves only when the thing moves; a seeded uid keeps its family readable on any peer; room
    /// storage, seeded items and the host's drop counter never overlap; a town's worth of doors doesn't clash. Reached
    /// by reflection, as ItemStackTests does (float3 too: this assembly doesn't reference Unity.Mathematics).
    /// </summary>
    public class WorldIdTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Ids => Game.GetType("Punchy.Items.WorldIds");
        static object Kind(string name) => Enum.Parse(Game.GetType("Punchy.Items.RecordKind"), name);
        static object Family(string name) => Enum.Parse(Game.GetType("Punchy.Items.SeedFamily"), name);

        static Type Float3 => Type.GetType("Unity.Mathematics.float3, Unity.Mathematics");

        static uint Record(string kind, float x, float y, float z) =>
            (uint)Ids.GetMethod("Record", new[] { Game.GetType("Punchy.Items.RecordKind"), Float3 })
                .Invoke(null, new[] { Kind(kind), Activator.CreateInstance(Float3, x, y, z) });
        static int Seeded(string family, uint record, int k) => (int)Ids.GetMethod("Seeded").Invoke(null, new[] { Family(family), (object)record, k });
        static int Storage(uint record, int k) => (int)Ids.GetMethod("Storage").Invoke(null, new object[] { record, k });
        static bool Is(string what, int id) => (bool)Ids.GetMethod(what).Invoke(null, new object[] { id });

        [Test]
        public void RecordIsKindAndPosition()
        {
            var door = Record("Door", 812.25f, 31.5f, -406.75f);
            Assert.AreEqual(door, Record("Door", 812.25f, 31.5f, -406.75f), "the same record twice");
            Assert.AreEqual(door, Record("Door", 812.251f, 31.501f, -406.749f), "under half a centimetre is the same place");
            Assert.AreNotEqual(door, Record("Door", 812.3f, 31.5f, -406.75f), "moved 5 cm: another record");
            Assert.AreNotEqual(door, Record("Landmark", 812.25f, 31.5f, -406.75f), "another kind at the same place");
        }

        [Test]
        public void FamiliesAndTablesDontOverlap()
        {
            var door = Record("Door", 10f, 2f, 20f);
            var spare = Seeded("Spare", Record("WorkPost", 10f, 0f, 20f), 0);
            var starter = Seeded("DoorStarter", door, 1);
            var rod = Seeded("Rod", door, 0);
            Assert.IsTrue(Is("IsSeeded", spare) && Is("IsSeeded", starter) && Is("IsSeeded", rod));
            Assert.IsTrue(Is("IsSpare", spare));
            Assert.IsFalse(Is("IsSpare", starter) || Is("IsSpare", rod), "only spares read as spares");
            Assert.Greater(spare, 0, "seeded uids are positive (-1 means none)");
            Assert.Greater(starter, 0);
            Assert.Greater(rod, 0);
            for (int counter = 1; counter < 5000; counter += 97)
                Assert.IsFalse(Is("IsSeeded", counter) || Is("IsSpare", counter), "the host's counter is not seeded");
            var store = Storage(door, 2);
            Assert.IsTrue(Is("IsStorage", store));
            Assert.Greater(store, 0);
            Assert.IsFalse(Is("IsStorage", 0x40000000 | 123 << 12 | 7), "a cave chest (CaveChestBit) is not room storage");
            Assert.IsFalse(Is("IsStorage", 12), "a temple chest (its socket index) is not room storage");
        }

        [Test]
        public void ATownOfDoorsDoesntClash()
        {
            // (2,000 doors a metre or two apart, the worst case for a hash that doesn't mix: neighbours draw apart.)
            var starters = new HashSet<int>();
            var stores = new HashSet<int>();
            int starterClashes = 0, storeClashes = 0;
            for (int x = 0; x < 50; x++)
                for (int z = 0; z < 40; z++)
                {
                    var door = Record("Door", x * 1.5f, 12f, z * 2f);
                    for (int k = 0; k < 2; k++)
                        if (!starters.Add(Seeded("DoorStarter", door, k))) starterClashes++;
                    for (int k = 0; k < 4; k++)
                        if (!stores.Add(Storage(door, k))) storeClashes++;
                }
            Assert.LessOrEqual(starterClashes, 1, "starter item clashes in 4,000");
            Assert.LessOrEqual(storeClashes, 1, "room store clashes in 8,000");
        }
    }
}
