using Punchy.Terrain;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Punchy.Flora
{
    public static partial class FloraPlacement
    {
        const float LogStreamWidth = 10.5f;

        // Rivers start near 9 m wide, so only the narrowest streams qualify.
        static float LogFit(ref RiverMapBlob map, in RiverQuery q, float2 xz, int kind) =>
            q.Width <= LogStreamWidth && q.Distance < 2f && ReachDraw(map.Seed, (uint)NearestSegment(ref map, xz), kind) >= 0.6f ? 1f : 0f;

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
