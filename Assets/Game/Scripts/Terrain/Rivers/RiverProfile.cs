using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>What a river segment is. Flags, because a source segment can also fall.</summary>
    [Flags]
    public enum RiverProfileKind : byte
    {
        None = 0,
        Channel = 1,
        Fall = 2,
        Lake = 4,
        Source = 8,
        Mouth = 16,
    }

    /// <summary>
    /// W-153 river rework, row 1: one river segment, from its start cell to the cell it drains into. It holds the bed
    /// (bank level), water and width at both ends, the lowest ground just outside its banks and what kind of segment it
    /// is, so sampling reads the channel, its banks and its water from one place instead of from separate arrays that
    /// separate passes set (docs/agents/river-rework.md). <see cref="RiverMapBlob.Profile"/> builds one.
    /// </summary>
    public struct RiverProfile
    {
        public int Start, Down;
        public float BedA, BedB, WaterA, WaterB, WidthA, WidthB;
        /// <summary>Lowest pre-river ground just outside the banks (float.MaxValue where not measured).</summary>
        public float BankLow;
        public RiverProfileKind Kind;

        public bool Falls => (Kind & RiverProfileKind.Fall) != 0;
        /// <summary>How far along the bed and water have gone at curve parameter t: a fall steps half way along.</summary>
        public float Along(float t) => Falls ? math.smoothstep(0.46f, 0.54f, t) : t;
        public float Bed(float along) => math.lerp(BedA, BedB, along);
        public float Water(float along) => math.lerp(WaterA, WaterB, along);
        public float Width(float t) => math.lerp(WidthA, WidthB, t);
    }

    /// <summary>
    /// The profile bake: the steps that settle each river segment's bed, water and banks together once the drainage
    /// and lake passes are done. Pits are filled here (formerly RiverBaking.FillRiverPits), the bank lows are measured
    /// once for the dam cuts and the profiles' banks, and each segment's kind is set.
    /// </summary>
    public static class RiverProfiles
    {
        /// <summary>Water must sit at least this far under the lowest ground beside its banks.</summary>
        public const float BankMargin = 0.3f;
        /// <summary>
        /// River cells whose water stands above their bank low less BankMargin after the bake (for the probe). A bake
        /// clamp that sinks such water into its channel cut walls and steps but left wide channels dry at their
        /// centreline, because samples took their water from the lowest-carving segment: the clamp waits for row 2's
        /// nearest-profile sampling (docs/agents/river-rework.md).
        /// </summary>
        public static int LastOverBank;

        /// <summary>
        /// W-36: a river cell whose bed lies below the bed of the river cell it drains into is a pit: its water backs up
        /// to the dam downstream, metres above its own channel, while its carve still digs the ground down to its low bed
        /// across the whole width. The water then stood above the dug-out banks with open ends along the sides. This
        /// raises such a bed to sit under its backed-up water like any river bed, so the river is flat to the dam
        /// instead of a sunken channel. Downstream first, so a filled cell can be the dam of the one above it. Rivers
        /// backed up by a lake or the sea are left alone, and water levels do not change.
        /// </summary>
        internal static void FillPits(in RiverSettings settings, float seaLevel, NativeArray<int> down, NativeList<int> order,
                                      NativeArray<float> lakes, NativeArray<float> width, NativeArray<float> bed, NativeArray<float> water)
        {
            for (int k = 0; k < order.Length; k++)
            {
                var c = order[k];
                var d = down[c];
                if (d < 0 || width[c] <= 0f || lakes[c] > 0f || lakes[d] > 0f || bed[d] <= bed[c] || water[c] < seaLevel + 0.5f)
                    continue;
                var under = RiverSampling.Depth(width[c], settings) * 0.25f;
                if (water[c] > bed[c] - under + 0.05f)
                    bed[c] = water[c] + under;
            }
        }

        /// <summary>
        /// The lowest pre-river ground 4 to 12 m outside each river cell's banks, in eight directions (float.MaxValue
        /// off the rivers and in lakes). W-103: these samples are most of the rivers stage; they only read the ground,
        /// so they run in parallel (same managed maths, same result).
        /// </summary>
        internal static float[] BankLows(in TerrainSource source, NativeArray<float2> points, NativeArray<float> width, NativeArray<float> lakes)
        {
            var count = width.Length;
            var widths = width.ToArray();
            var depths = lakes.ToArray();
            var centres = points.ToArray();
            var ground = source;
            var lows = new float[count];
            BakeParallel.For(count, c =>
            {
                lows[c] = float.MaxValue;
                if (widths[c] <= 0f || depths[c] > 0f)
                    return;
                var low = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    math.sincos(k * math.PI / 4f, out var sn, out var cs);
                    var dir = new float2(cs, sn);
                    for (float r = widths[c] * 0.5f + 4f; r <= widths[c] * 0.5f + 12f; r += 4f)
                        low = math.min(low, ground.BaseHeight(centres[c] + dir * r));
                }
                lows[c] = low;
            });
            return lows;
        }

        /// <summary>Each cell's segment kind, as RiverProfileKind bits (0 off the rivers).</summary>
        internal static byte[] Kinds(in RiverSettings settings, float seaLevel, NativeArray<int> down, NativeArray<float> width,
                                     NativeArray<float> lakes, NativeArray<float> bed, NativeArray<float> water, float[] lows)
        {
            var count = down.Length;
            var fed = new bool[count];
            for (int i = 0; i < count; i++)
                if (width[i] > 0f && down[i] >= 0)
                    fed[down[i]] = true;
            var kinds = new byte[count];
            LastOverBank = 0;
            for (int i = 0; i < count; i++)
            {
                if (width[i] <= 0f)
                    continue;
                var d = down[i];
                var kind = RiverProfileKind.Channel;
                if (settings.FallDrop > 0f && d >= 0 && bed[i] - bed[d] >= settings.FallDrop)
                    kind |= RiverProfileKind.Fall;
                if (lakes[i] > 0f)
                    kind |= RiverProfileKind.Lake;
                if (!fed[i])
                    kind |= RiverProfileKind.Source;
                if (d < 0 || water[d] <= seaLevel + 0.01f && width[d] <= 0f)
                    kind |= RiverProfileKind.Mouth;
                kinds[i] = (byte)kind;
                if (lows != null && lakes[i] <= 0f && water[i] > lows[i] - BankMargin + 0.01f)
                    LastOverBank++;
            }
            return kinds;
        }
    }
}
