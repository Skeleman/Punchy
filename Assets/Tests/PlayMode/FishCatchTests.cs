using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// A-100 row 1: the catch table (FishCatch) read straight off the code, no scene. Every water kind has something to
    /// catch at noon and at midnight, a small pool sometimes gives nothing, sizes stay in each row's range, and the same
    /// seed gives the same catch (so host, client and solo agree).
    /// </summary>
    public class FishCatchTests
    {
        static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp")
            .GetType("Punchy.Fauna." + name, true);

        static readonly Type Catch = Find("FishCatch"), Water = Find("FishWater"), Biome = Find("FishBiome");

        /// <summary>One table roll: (bit, name, size, junk).</summary>
        static (bool bit, string name, int size, bool junk) Roll(string water, string biome, float hour, uint seed)
        {
            var method = Catch.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Roll" && m.GetParameters()[0].ParameterType == Water);
            var args = new object[] { Enum.Parse(Water, water), Enum.Parse(Biome, biome), hour, seed, null };
            var bit = (bool)method.Invoke(null, args);
            var r = args[4];
            T F<T>(string n) => (T)r.GetType().GetField(n).GetValue(r);
            return (bit, F<string>("Name"), F<byte>("Size"), F<bool>("Junk"));
        }

        static readonly string[] Waters = { "SeaShore", "SeaDeep", "Harbour", "Lake", "RiverPool", "RiverRapids", "Swamp", "CavePool", "CaveStream", "SmallPool" };

        [Test]
        public void EveryWaterHasFishByDayAndNight()
        {
            foreach (var water in Waters)
                foreach (var hour in new[] { 12f, 0f })
                {
                    var fish = 0;
                    for (uint i = 0; i < 200; i++)
                    {
                        var r = Roll(water, "Temperate", hour, i);
                        if (r.bit && !r.junk)
                            fish++;
                    }
                    Assert.Greater(fish, 40, $"{water} at {hour}:00 gave only {fish} fish in 200 casts");
                }
        }

        [Test]
        public void SmallPoolsSometimesGiveNothingAndOnlyTinyFish()
        {
            int none = 0, big = 0;
            for (uint i = 0; i < 300; i++)
            {
                var r = Roll("SmallPool", "Temperate", 12f, i);
                if (!r.bit)
                    none++;
                else if (r.size > 10)
                    big++;
            }
            Assert.Greater(none, 30, "a small pool should come up empty now and then");
            Assert.AreEqual(0, big, "a small pool's fish are tiny");
        }

        [Test]
        public void SizesStayInRangeAndRollsRepeat()
        {
            var table = (Array)Catch.GetField("Table").GetValue(null);
            foreach (var water in Waters)
                for (uint i = 0; i < 100; i++)
                {
                    var a = Roll(water, "Temperate", i * 0.24f, i * 31u);
                    var b = Roll(water, "Temperate", i * 0.24f, i * 31u);
                    Assert.AreEqual(a, b, "same seed, same catch");
                    if (!a.bit)
                        continue;
                    var row = table.Cast<object>().First(e => (string)e.GetType().GetField("Name").GetValue(e) == a.name);
                    var min = (byte)row.GetType().GetField("MinSize").GetValue(row);
                    var max = (byte)row.GetType().GetField("MaxSize").GetValue(row);
                    Assert.That(a.size, Is.InRange((int)min, (int)max), $"{a.name} size");
                }
        }

        [Test]
        public void BiomeOnlyFishStayInTheirBiome()
        {
            for (uint i = 0; i < 300; i++)
            {
                Assert.AreNotEqual("Koi", Roll("Lake", "Temperate", 12f, i).name);
                Assert.AreNotEqual("Bog eel", Roll("Swamp", "Marsh", 12f, i).name);
            }
            Assert.IsTrue(Enumerable.Range(0, 300).Any(i => Roll("Lake", "Sparkle", 12f, (uint)i).name == "Koi"), "koi in a sparkling forest's lake");
        }
    }
}
