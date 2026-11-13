using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// A-102 row 1: the breath meter (Punchy.Fauna.Breath, #37) read straight off the code, no scene. Under water it
    /// empties over the hold time; emptied, the body gasps until CatchBreath has come back; in air it refills within
    /// RefillShare of the hold time (at least MinRefill s); bad input never makes it NaN. Also the default hold times:
    /// folk longest, then swimmers, waterfowl, land animals and fliers.
    /// </summary>
    public class BreathMeterTests
    {
        static Assembly Game => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type Breath => Game.GetType("Punchy.Fauna.Breath", true);
        static float Const(string n) => (float)Breath.GetField(n).GetValue(null);

        static float Use(float used, ref bool gasping, bool under, float hold, float dt)
        {
            var args = new object[] { used, gasping, under, hold, dt };
            var r = (float)Breath.GetMethod("Use").Invoke(null, args);
            gasping = (bool)args[1];
            return r;
        }

        static float Hold(bool folk, bool crossesWater, string habitat) =>
            (float)Breath.GetMethod("DefaultHold").Invoke(null, new object[]
            {
                folk, crossesWater, Enum.Parse(Game.GetType("Punchy.Fauna.Habitat", true), habitat),
            });

        /// <summary>Steps the meter for seconds in 0.1 s steps; returns it.</summary>
        static float Run(float used, ref bool gasping, bool under, float hold, float seconds)
        {
            for (int i = 0; i < Math.Round(seconds / 0.1f); i++)
                used = Use(used, ref gasping, under, hold, 0.1f);
            return used;
        }

        [Test]
        public void EmptiesOverTheHoldTimeThenGasps()
        {
            var gasping = false;
            var used = Run(0f, ref gasping, true, 30f, 15f);
            Assert.That(used, Is.EqualTo(0.5f).Within(0.01f), "half a 30 s hold after 15 s");
            Assert.IsFalse(gasping);
            used = Run(used, ref gasping, true, 30f, 15.2f);
            Assert.That(used, Is.EqualTo(1f));
            Assert.IsTrue(gasping, "gasps once empty");
        }

        [Test]
        public void GaspsUntilSomeBreathIsBack()
        {
            var gasping = true;
            var hold = 30f;
            var refill = Math.Max(Const("MinRefill"), hold * Const("RefillShare"));
            var used = Run(1f, ref gasping, false, hold, refill * Const("CatchBreath") * 0.5f);
            Assert.IsTrue(gasping, "still gasping with half the catch-breath back");
            used = Run(used, ref gasping, false, hold, refill * Const("CatchBreath") * 0.6f);
            Assert.IsFalse(gasping, "may go down again once CatchBreath is back");
            used = Run(used, ref gasping, false, hold, refill);
            Assert.That(used, Is.EqualTo(0f), "full again within the refill time");
        }

        [Test]
        public void ShortHoldsStillTakeMinRefill()
        {
            var gasping = false;
            var used = Run(1f, ref gasping, false, 6f, Const("MinRefill") * 0.5f);
            Assert.That(used, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void NeverNaN()
        {
            var gasping = false;
            foreach (var (used, hold, dt) in new[] { (float.NaN, 30f, 0.1f), (0.5f, float.NaN, 0.1f), (0.5f, 0f, 0.1f), (0.5f, 30f, float.NaN), (0.5f, 30f, -1f), (float.PositiveInfinity, 30f, 0.1f) })
            {
                var r = Use(used, ref gasping, true, hold, dt);
                Assert.IsFalse(float.IsNaN(r) || r < 0f || r > 1f, $"used {used} hold {hold} dt {dt} gave {r}");
            }
        }

        [Test]
        public void DefaultHoldsByBody()
        {
            var folk = Hold(true, false, "Land");
            var swimmer = Hold(false, true, "Land");
            var floater = Hold(false, false, "Floats");
            var animal = Hold(false, false, "Land");
            var flier = Hold(false, false, "Flies");
            Assert.That(folk, Is.GreaterThan(swimmer));
            Assert.That(swimmer, Is.GreaterThan(floater));
            Assert.That(floater, Is.GreaterThan(animal));
            Assert.That(animal, Is.GreaterThan(flier));
            Assert.That(flier, Is.GreaterThan(0f));
        }
    }
}
