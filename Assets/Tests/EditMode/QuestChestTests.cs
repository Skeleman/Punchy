using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// P-49: a dungeon quest's chest is derived, not sent, so <c>Items.PickQuestChest</c> must give every machine the same
    /// answer: the same for the same input, only a chest of the asked kind, one of the three nearest the board, and none
    /// when there's no chest of the kind. The test assembly can't reference Assembly-CSharp, so it's reached by
    /// reflection, as MaterialLibraryTests does.
    /// </summary>
    public class QuestChestTests
    {
        static MethodInfo pick;
        static MethodInfo Pick => pick ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp")
            .GetType("Punchy.Items.Items").GetMethod("PickQuestChest", BindingFlags.Public | BindingFlags.Static);

        const int NoChest = int.MinValue;

        // (Kinds: 0 cave, 1 ruin room, 2 temple. Ids 1-4 caves at 10, 20, 30 and 40 m east; 5 a ruin room; 6 the temple.)
        static readonly int[] Ids = { 1, 2, 3, 4, 5, 6 };
        static readonly Vector3[] Positions =
        {
            new Vector3(10f, 0f, 0f), new Vector3(20f, 5f, 0f), new Vector3(30f, -5f, 0f), new Vector3(40f, 0f, 0f),
            new Vector3(0f, 0f, 50f), new Vector3(1f, 0f, 1f),
        };
        static readonly byte[] Kinds = { 0, 0, 0, 0, 1, 2 };

        static int Call(int[] ids, Vector3[] pos, byte[] kinds, int board, int slot, string id, string kind) =>
            (int)Pick.Invoke(null, new object[] { ids, pos, kinds, Vector3.zero, board, slot, id, kind });

        [Test]
        public void PicksTheSameEveryTime()
        {
            Assert.IsNotNull(Pick, "Items.PickQuestChest not found");
            for (int b = 0; b < 20; b++)
                for (int s = 0; s < 4; s++)
                    Assert.AreEqual(Call(Ids, Positions, Kinds, b, s, "heirloom", "cave"), Call(Ids, Positions, Kinds, b, s, "heirloom", "cave"));
        }

        [Test]
        public void PicksOnlyTheKindAskedAmongTheThreeNearest()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int b = 0; b < 40; b++)
                for (int s = 0; s < 4; s++)
                {
                    var cave = Call(Ids, Positions, Kinds, b, s, "heirloom", "cave");
                    CollectionAssert.Contains(new[] { 1, 2, 3 }, cave, $"board {b} slot {s}: cave quest picked {cave}");
                    seen.Add(cave);
                    Assert.AreEqual(5, Call(Ids, Positions, Kinds, b, s, "elder-staff", "ruin"), $"board {b} slot {s}: the one ruin room");
                }
            Assert.Greater(seen.Count, 1, "every board picked the same cave chest");
            // (The input's order doesn't matter: the nearest are sorted by distance, ties by id.)
            var order = new[] { 5, 3, 0, 2, 4, 1 };
            Assert.AreEqual(Call(Ids, Positions, Kinds, 7, 1, "heirloom", "cave"),
                Call(order.Select(i => Ids[i]).ToArray(), order.Select(i => Positions[i]).ToArray(), order.Select(i => Kinds[i]).ToArray(), 7, 1, "heirloom", "cave"));
        }

        [Test]
        public void NoneWithoutAChestOfTheKind()
        {
            Assert.AreEqual(NoChest, Call(new int[0], new Vector3[0], new byte[0], 0, 0, "heirloom", "cave"));
            Assert.AreEqual(NoChest, Call(new[] { 6 }, new[] { Vector3.one }, new byte[] { 2 }, 0, 0, "heirloom", "cave"), "the temple's chest is never a quest chest");
            Assert.AreEqual(NoChest, Call(new[] { 1, 2 }, new[] { Vector3.one, Vector3.zero }, new byte[] { 0, 0 }, 0, 0, "elder-staff", "ruin"));
            Assert.AreEqual(NoChest, Call(Ids, Positions, Kinds, 0, 0, "heirloom", "tower"), "an unknown kind");
        }
    }
}
