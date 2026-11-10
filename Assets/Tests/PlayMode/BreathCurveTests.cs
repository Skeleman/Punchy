using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// A-94 row 1: the cold-breath size curve (CreatureBreathSystem.ShapeFor, A-90 / #226) read straight off the code, no
    /// scene: small bodies breathe smaller puffs and less vapour per exhale on a quicker beat, fliers carry little of
    /// their velocity and breathe each exhale out in one burst (no trail), and bodies below the smallest size show none. Targets from A-90's card and its
    /// REPORTS table. The bodies are the -breathlog sizes of a songbird, an arctic fox, a deer and a horse (StandHeight,
    /// BodyLength at the species' middle size, m) and A-90's colossus-sized body (8 m tall, 12 m long), whose puffs grow
    /// past a horse's since A-94 row 3.
    /// </summary>
    public class BreathCurveTests
    {
        static readonly (string name, float height, float length)[] Bodies =
        {
            ("songbird", 0.15f, 0.11f), ("fox", 0.31f, 0.40f), ("deer", 0.99f, 1.15f), ("horse", 1.43f, 1.55f), ("colossus", 8f, 12f),
        };

        static Type System => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp")
            .GetType("Punchy.Fauna.CreatureBreathSystem", true);

        /// <summary>One body's shape as plain numbers: body, beat (s), exhale (s), puffs, size, opacity, lifetime, shows.</summary>
        struct Shape
        {
            public float Body, Every, Exhale, Puffs, Size, Opacity, Lifetime;
            public bool Shows;
            /// <summary>Vapour breathed out per exhale: wisps x their area x opacity.</summary>
            public float Vapour => Puffs * Size * Size * Opacity;
        }

        static Shape ShapeFor(float height, float length)
        {
            var s = System.GetMethod("ShapeFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { height, length });
            float F(string n) => (float)s.GetType().GetField(n).GetValue(s);
            return new Shape
            {
                Body = F("Body"), Every = F("Every"), Exhale = F("Exhale"), Puffs = F("Puffs"), Size = F("Size"), Opacity = F("Opacity"), Lifetime = F("Lifetime"),
                Shows = (bool)s.GetType().GetProperty("Shows").GetValue(s),
            };
        }

        static float Carry(float fly) => (float)System.GetMethod("Carry", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { fly });
        static float ExhaleFor(float exhale, float fly) => (float)System.GetMethod("ExhaleFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { exhale, fly });

        /// <summary>
        /// Target (A-90 row 1): along songbird &lt; fox &lt; deer &lt; horse, puff size and vapour per exhale rise and the
        /// beat slows, on to the colossus-sized body (A-94: no longer capped at a horse's); a songbird's puff is 5-10% of a
        /// person's and its beat at least 3x quicker; a flier carries at most 10% of its velocity (a walker 85%); a body
        /// under 0.1 of a person shows no breath, a songbird does.
        /// </summary>
        [Test]
        public void Breath_SmallLessBigMore()
        {
            var shapes = Bodies.Select(b => ShapeFor(b.height, b.length)).ToArray();
            var person = ShapeFor(0.94f, 0.24f);
            var log = string.Join(" | ", Bodies.Select((b, i) => $"{b.name} body {shapes[i].Body:F2} beat {shapes[i].Every:F2}s puffs {shapes[i].Puffs:F1} size {shapes[i].Size:F3} vapour {shapes[i].Vapour:F2} life {shapes[i].Lifetime:F2}s"));
            var tiny = ShapeFor(0.05f, 0.05f);
            LogWatch.Measure($"breath curve: {log}; carry walk {Carry(0f):F2} fly {Carry(1f):F2}; tiny body {tiny.Body:F2} shows {tiny.Shows}");

            for (int i = 1; i < Bodies.Length; i++)
            {
                var (small, big) = (shapes[i - 1], shapes[i]);
                var pair = $"{Bodies[i - 1].name} vs {Bodies[i].name}";
                Assert.That(big.Size, Is.GreaterThan(small.Size), $"{pair}: puff size");
                Assert.That(big.Vapour, Is.GreaterThan(small.Vapour), $"{pair}: vapour per exhale");
                Assert.That(big.Every, Is.GreaterThan(small.Every), $"{pair}: the smaller body's beat should be quicker");
            }
            var bird = shapes[0];
            Assert.That(bird.Size / person.Size, Is.InRange(0.05f, 0.1f), "a songbird's puff against a person's (A-90: about 5-10%)");
            Assert.That(person.Every / bird.Every, Is.GreaterThanOrEqualTo(3f), "a songbird's beat against a person's (A-90: about 4x)");
            Assert.That(Carry(1f), Is.LessThanOrEqualTo(0.1f), "a flier's wisps carry too much of its velocity (a trail)");
            Assert.That(Carry(0f), Is.EqualTo(0.85f).Within(1e-4f), "a walker's carry changed");
            // (A-94 row 2: a flier's wisps come out in one burst, a dot where it breathed, not a dotted line along its flight.)
            var horse = shapes[3];
            Assert.That(ExhaleFor(horse.Exhale, 1f), Is.LessThanOrEqualTo(0.1f), "a flier's exhale is long enough to draw a dotted trail");
            Assert.That(ExhaleFor(horse.Exhale, 0f), Is.EqualTo(horse.Exhale).Within(1e-5f), "a walker's exhale changed");
            Assert.IsTrue(bird.Shows, "a songbird's breath no longer shows");
            Assert.IsFalse(tiny.Shows, "a body under the smallest size still shows breath");
        }
    }
}
