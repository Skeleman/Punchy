using System;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-124 (Zach, 11-14): how far one person's build may stray from their species' (a species asset field): extra
    /// height on top of the Size draw, girth (torso and limbs), limb length against the torso, head size and shoulder
    /// width, each as a fraction either way (0.1 = 10% under to 10% over). Animals keep the narrow defaults; folk set
    /// wider ones, their children about half.
    /// </summary>
    [Serializable]
    public struct BuildRange
    {
        [Range(0f, 0.3f)] public float Height;
        [Range(0f, 0.3f)] public float Girth;
        [Range(0f, 0.3f)] public float Limbs;
        [Range(0f, 0.3f)] public float Head;
        [Tooltip("Upright bodies only: the torso's top, where the arms join (the A-123 shoulder fit follows it).")]
        [Range(0f, 0.3f)] public float Shoulders;

        public static BuildRange Narrow => new BuildRange { Girth = 0.04f, Limbs = 0.03f, Head = 0.03f, Shoulders = 0.03f };

        public bool Any => Girth > 0f || Limbs > 0f || Head > 0f || Shoulders > 0f;
    }

    /// <summary>
    /// A-124: one build's multipliers on a body (all 1 for the species' own, which bakes byte for byte as before).
    /// Every species has <see cref="BodyBuilds.Count"/> builds; which one a creature has is picked from its Individual on
    /// every machine, so NPC builds cost nothing on the wire.
    /// </summary>
    public struct BodyBuild
    {
        public float Girth, Limbs, Head, Shoulders;

        public static BodyBuild Own => new BodyBuild { Girth = 1f, Limbs = 1f, Head = 1f, Shoulders = 1f };

        public bool IsOwn => Girth == 1f && Limbs == 1f && Head == 1f && Shoulders == 1f;
    }

    /// <summary>
    /// A-124: the builds a species' people come in, and which one an individual has. The builds are a fixed spread of
    /// <see cref="Count"/> points over the range (a Latin square: each trait takes every one of 16 even steps exactly
    /// once, the traits in different orders), so no two are alike, both ends of every trait are in, and no build is
    /// at the end of every trait at once. Height is the one continuous trait (CreatureState.Scale).
    /// The player's build is one byte (NetPlayerLook.Build): the build in the low nibble, a height step in the high one.
    /// </summary>
    public static class BodyBuilds
    {
        public const int Count = 16;
        /// <summary>The style group (CreatureBlob.Style) the build is drawn from, and the trait its extra height is.</summary>
        public const int Group = 12401, HeightTrait = 21;

        // (Steps of 5, 11 and 3 are coprime with 16, so each column is a permutation; the offsets mix the pairs up.)
        static float Step(int index, int stride, int offset) => ((index * stride + offset) % Count) / (float)(Count - 1) * 2f - 1f;

        /// <summary>Build <paramref name="index"/> of a range (index -1, or a range of all zeros: the species' own).</summary>
        public static BodyBuild Of(in BuildRange range, int index)
        {
            if (index < 0 || !range.Any)
                return BodyBuild.Own;
            index %= Count;
            return new BodyBuild
            {
                Girth = 1f + range.Girth * Step(index, 1, 0),
                Limbs = 1f + range.Limbs * Step(index, 5, 3),
                Head = 1f + range.Head * Step(index, 11, 7),
                Shoulders = 1f + range.Shoulders * Step(index, 3, 12)
            };
        }

        /// <summary>Which build an individual has. (Scrambled first: math.hash keeps the low bit, and every Individual is
        /// odd, so a plain Style draw over 16 would only ever land on the even builds.)</summary>
        public static int Pick(uint individual) =>
            (int)(Punchy.Terrain.BiomeSampling.Scramble(individual ^ unchecked((uint)Group * 0x9E3779B9u)) % (uint)Count);

        /// <summary>An individual's height on top of its Size draw (a multiplier on CreatureState.Scale).</summary>
        public static float Height(in BuildRange range, uint individual) =>
            range.Height > 0f ? 1f + range.Height * CreatureBlob.Trait(individual, HeightTrait) : 1f;

        /// <summary>The player's build byte an individual rolls before they pick one: its NPC build, a middling height.</summary>
        public static byte PlayerDefault(uint individual) => Player(Pick(individual), 8 + (int)(individual >> 28) % 3 - 1);

        public static byte Player(int build, int heightStep) => (byte)((build & 15) | (math.clamp(heightStep, 0, 15) << 4));

        public static int PlayerBuild(byte code) => code & 15;

        public static int PlayerHeightStep(byte code) => code >> 4;

        /// <summary>A player's size from their height step: the species' Size range, end to end.</summary>
        public static float PlayerScale(float2 size, byte code) => math.lerp(size.x, math.max(size.x, size.y), PlayerHeightStep(code) / 15f);
    }
}
