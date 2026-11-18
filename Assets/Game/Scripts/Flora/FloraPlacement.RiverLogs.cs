using Punchy.Terrain;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Punchy.Flora
{
    // #301 river details: a fallen tree bridging a narrow stream, and the dressing of a mid-channel gravel island
    // (pebbles over it, reeds round its tail, a snag lodged on its nose). Seeded from the river map alone.
    public static partial class FloraPlacement
    {
        const float LogStreamWidth = 10.5f;

        /// <summary>A fallen log's spot: the middle of one of the narrowest streams (rivers start near 9 m), on about half their reaches.</summary>
        static float LogFit(ref RiverMapBlob map, in RiverQuery q, float2 xz, int kind) =>
            q.Width <= LogStreamWidth && q.Distance < 2f && ReachDraw(map.Seed, (uint)NearestSegment(ref map, xz), kind) >= 0.6f ? 1f : 0f;

        /// <summary>The island's share of a RiverSpot (0 off islands): pebbles over it, reeds at its tail, a snag on its nose.</summary>
        static float IsleFit(in RiverQuery q, in RiverQuery up, in RiverQuery down, float2 xz, int kind)
        {
            var isle = RiverSampling.IsleMask(q);
            if (isle <= 0f)
                return 0f;
            if (kind == (int)RiverSpot.GravelBar)
                return isle * math.smoothstep(-0.2f, 0.2f, noise.snoise(xz / 3f + 2.3f));
            if (kind == (int)RiverSpot.InnerBend)
                return RiverSampling.IsleMask(down) < isle && isle < 0.9f ? 0.9f : 0.3f * (1f - isle);
            if (kind == (int)RiverSpot.Snag)
                return isle > 0.3f && RiverSampling.IsleMask(up) < 0.05f ? 1f : 0f;
            return 0f;
        }

        /// <summary>
        /// W-63 / #301: a fallen log lies across the stream (its X across the flow), both ends on dry bank no more than
        /// 1.5 m apart in height, its middle at their mean and its length tilted down to the lower bank.
        /// </summary>
        static FloraSystem.FloraInstance LogAcross(in TerrainSource source, in FloraRules rule, float2 xz, in GroundSample ground, float scale, ref Random rng)
        {
            var flow = math.normalizesafe(ground.Flow, new float2(0f, 1f));
            var yaw = math.atan2(flow.x, -flow.y) + rng.NextFloat(-0.25f, 0.25f);
            var axis = new float2(math.cos(yaw), math.sin(yaw));
            var span = rule.Span * scale;
            var endA = source.SampleGround(xz + axis * span);
            var endB = source.SampleGround(xz - axis * span);
            var rise = endB.Height - endA.Height;
            if (!Dry(endA) || !Dry(endB) || math.abs(rise) > 1.5f)
                return new FloraSystem.FloraInstance { Model = -1 };
            var lean = math.atan2(rise, 2f * span) + rng.NextFloat(-0.03f, 0.03f);
            return new FloraSystem.FloraInstance
            {
                Position = new float3(xz.x, (endA.Height + endB.Height) * 0.5f - 0.15f, xz.y),
                Yaw = yaw,
                Scale = scale,
                Model = rule.FirstModel + rng.NextInt(rule.Variants),
                Random = rng.NextFloat(),
                Tilt = axis * math.sin(lean),
                Ground = GroundFor(source, rule, xz, 1f, 0.25f)
            };
        }
    }
}
