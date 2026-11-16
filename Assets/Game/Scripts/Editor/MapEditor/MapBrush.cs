using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-07 (#260): one map editor brush, as data. The brush set (raise, lower, smooth, flatten) is a folder of these
    /// assets, so adding, retuning or dropping a brush is an asset change, not code. Each brush is one of a few
    /// operations on the <see cref="IslandEdits"/> height offsets, scaled by a falloff from centre to rim.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Map Editor/Brush", fileName = "MapBrush")]
    public class MapBrush : ScriptableObject
    {
        public enum Operation
        {
            /// <summary>Adds <see cref="Strength"/> metres per second at the centre (times <see cref="Sign"/>).</summary>
            Raise,
            /// <summary>Pulls each vertex toward the mean of its neighbours (Strength: share per second).</summary>
            Smooth,
            /// <summary>Pulls each vertex toward the height under the brush where the stroke began (Strength: share per second).</summary>
            Flatten
        }

        [Tooltip("Shown on the brush button.")]
        public string Label = "Brush";
        [Tooltip("Buttons are listed in this order.")]
        public int Order;
        public Operation Op = Operation.Raise;
        [Tooltip("Raise: +1 raises, -1 lowers. Holding Shift while painting flips it.")]
        public float Sign = 1f;
        [Tooltip("Radius in metres.")]
        [Min(1f)] public float Radius = 24f;
        [Tooltip("Raise: metres per second at the centre. Smooth and Flatten: share of the way per second (1 = all the way in a second).")]
        [Min(0f)] public float Strength = 4f;
        [Tooltip("Weight from the centre (0) to the rim (1).")]
        public AnimationCurve Falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        /// <summary>
        /// Applies the brush for <paramref name="dt"/> seconds at <paramref name="centre"/>. <paramref name="ground"/>
        /// gives the ground under the edits at a grid vertex (the island as baked); <paramref name="target"/> is the
        /// flatten height. Returns the square it may have changed (for the preview to refresh).
        /// </summary>
        public (float2 min, float2 max) Apply(IslandEdits edits, Func<int, int, float> ground, float2 centre, float dt, float target, bool invert)
        {
            float r = Radius, s = IslandEdits.Spacing;
            var lo = (int2)math.floor((centre - r) / s);
            var hi = (int2)math.ceil((centre + r) / s);
            float Weight(int gx, int gz)
            {
                float d = math.distance(new float2(gx, gz) * s, centre) / r;
                return d >= 1f ? 0f : math.max(0f, Falloff.Evaluate(d)) * Strength * dt;
            }
            switch (Op)
            {
                case Operation.Raise:
                    float sign = invert ? -Sign : Sign;
                    for (int gz = lo.y; gz <= hi.y; gz++)
                        for (int gx = lo.x; gx <= hi.x; gx++)
                        {
                            float w = Weight(gx, gz);
                            if (w > 0f) edits.SetVertex(gx, gz, edits.Vertex(gx, gz) + sign * w);
                        }
                    break;
                case Operation.Flatten:
                    for (int gz = lo.y; gz <= hi.y; gz++)
                        for (int gx = lo.x; gx <= hi.x; gx++)
                        {
                            float w = math.saturate(Weight(gx, gz));
                            if (w <= 0f) continue;
                            float d = edits.Vertex(gx, gz), h = ground(gx, gz) + d;
                            edits.SetVertex(gx, gz, d + (target - h) * w);
                        }
                    break;
                case Operation.Smooth:
                    // Heights read before any write (one ring wider for the neighbours), so the result doesn't depend on order.
                    int w0 = hi.x - lo.x + 3, h0 = hi.y - lo.y + 3;
                    var heights = new float[w0 * h0];
                    for (int z = 0; z < h0; z++)
                        for (int x = 0; x < w0; x++)
                        {
                            int gx = lo.x - 1 + x, gz = lo.y - 1 + z;
                            heights[z * w0 + x] = ground(gx, gz) + edits.Vertex(gx, gz);
                        }
                    for (int gz = lo.y; gz <= hi.y; gz++)
                        for (int gx = lo.x; gx <= hi.x; gx++)
                        {
                            float w = math.saturate(Weight(gx, gz));
                            if (w <= 0f) continue;
                            int x = gx - lo.x + 1, z = gz - lo.y + 1;
                            float sum = 0f;
                            for (int dz = -1; dz <= 1; dz++)
                                for (int dx = -1; dx <= 1; dx++)
                                    sum += heights[(z + dz) * w0 + x + dx];
                            float h = heights[z * w0 + x];
                            edits.SetVertex(gx, gz, edits.Vertex(gx, gz) + (sum / 9f - h) * w);
                        }
                    break;
            }
            return ((float2)lo * s, (float2)hi * s);
        }

        /// <summary>The four starting brushes, made by the window when the brush folder is empty.</summary>
        public static IEnumerable<MapBrush> Defaults()
        {
            MapBrush Make(string name, int order, Operation op, float sign, float strength)
            {
                var b = CreateInstance<MapBrush>();
                b.name = name; b.Label = name; b.Order = order; b.Op = op; b.Sign = sign; b.Strength = strength;
                return b;
            }
            yield return Make("Raise", 0, Operation.Raise, 1f, 4f);
            yield return Make("Lower", 1, Operation.Raise, -1f, 4f);
            yield return Make("Smooth", 2, Operation.Smooth, 1f, 3f);
            yield return Make("Flatten", 3, Operation.Flatten, 1f, 2f);
        }
    }
}
