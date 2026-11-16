using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Punchy.Tests
{
    /// <summary>
    /// A-100 row 2: the bobber list fish notice (FishLure), read straight off the code, no scene. A cast replaces the
    /// same line's last one, a scatter is gone after its short life, a line left in long past its bite is dropped, and
    /// the nibbles come just before the bite (none for a bite too quick for a fish to come over).
    /// </summary>
    public class FishLureTests
    {
        static readonly Type Lure = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp")
            .GetType("Punchy.Fauna.FishLure", true);

        static object Call(string name, params object[] args) =>
            Lure.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == name && m.GetParameters().Length == args.Length &&
                            (args.Length == 0 || !m.GetParameters()[0].ParameterType.IsByRef))
                .Invoke(null, args);

        static int Count => (int)Lure.GetProperty("Count").GetValue(null);
        static object Get(int i) => Call("Get", i);
        static T F<T>(object o, string n) => (T)o.GetType().GetField(n).GetValue(o);
        static string StageOf(object l) => F<object>(l, "Stage").ToString();

        [SetUp]
        public void Clear() => Call("ClearAll");

        [TearDown]
        public void After() => Call("ClearAll");

        [Test]
        public void CastReplacesTheLinesLastAndScatterExpires()
        {
            Call("Cast", 1, new Unity.Mathematics.float3(0f, 2f, 0f), 5f);
            Call("Cast", 2, new Unity.Mathematics.float3(10f, 2f, 0f), 5f);
            Call("Cast", 1, new Unity.Mathematics.float3(3f, 2f, 0f), 6f);
            Assert.AreEqual(2, Count, "a second cast on line 1 replaces its first");
            Call("Bite", 2);
            Call("Scatter", 1);
            Call("Tick", 0.1f);
            Assert.AreEqual(2, Count);
            var stages = Enumerable.Range(0, Count).Select(i => (F<int>(Get(i), "Key"), StageOf(Get(i)))).ToArray();
            CollectionAssert.Contains(stages, (1, "Scatter"));
            CollectionAssert.Contains(stages, (2, "Bite"));
            Call("Tick", 1f);
            Assert.AreEqual(1, Count, "the scatter is gone after its short life");
            Assert.AreEqual(2, F<int>(Get(0), "Key"));
            Call("Clear", 2);
            Assert.AreEqual(0, Count);
        }

        [Test]
        public void StaleLinesDropAndSplashesScatter()
        {
            Call("Cast", 3, new Unity.Mathematics.float3(0f, 1f, 0f), 2f);
            Call("Splash", new Unity.Mathematics.float3(5f, 1f, 5f));
            Assert.AreEqual("Scatter", StageOf(Get(1)));
            for (int i = 0; i < 200; i++)
                Call("Tick", 1f);
            Assert.AreEqual(0, Count, "a line left in long past its bite is dropped, and the splash is long gone");
            Call("Cast", 4, new Unity.Mathematics.float3(float.NaN, 1f, 0f), 2f);
            Assert.AreEqual(0, Count, "a NaN bobber is never added");
        }

        [Test]
        public void NibblesComeJustBeforeTheBite()
        {
            Call("Cast", 5, new Unity.Mathematics.float3(0f, 1f, 0f), 6f);
            var nibbles = 0;
            var inNibble = false;
            var phase = Lure.GetMethod("NibblePhase");
            for (float t = 0f; t < 6f; t += 0.02f)
            {
                var p = (float)phase.Invoke(null, new[] { Get(0) });
                if (p > 0f && !inNibble)
                {
                    nibbles++;
                    Assert.Greater(t, 3.5f, "nibbles come near the bite, not straight after the cast");
                }
                inNibble = p > 0f;
                Call("Tick", 0.02f);
            }
            Assert.AreEqual(2, nibbles, "two nibbles before a slow bite");
            Call("Cast", 6, new Unity.Mathematics.float3(0f, 1f, 0f), 1.2f);
            var any = false;
            for (float t = 0f; t < 1.2f; t += 0.02f)
            {
                any |= (float)phase.Invoke(null, new[] { Get(1) }) > 0f;
                Call("Tick", 0.02f);
            }
            Assert.IsFalse(any, "a bite too quick for a fish to come over has no nibble");
        }
    }
}
