using Punchy.Terrain;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Punchy.Flora
{
    public static partial class FloraPlacement
    {
        const float RiffleGradient = 0.015f;
        const float SplashGradient = 0.03f;
        const float RiffleOdds = 0.8f;
        const float SplashOdds = 0.8f;
        const float CopyReach = 6f;

        // Every cell near a reach's spot that rolls the species snaps to it, so the copies coincide and show as one.
        static float RapidsFit(in TerrainSource source, float2 xz, int kind) =>
            source.Rivers.IsCreated && RapidsSpot(source, xz, kind, out _) ? 1f : 0f;

        static bool RapidsSpot(in TerrainSource source, float2 xz, int kind, out int segment)
        {
            ref var map = ref source.Rivers.Value;
            var c = math.clamp((int2)math.floor((xz - map.Origin) / map.CellSize), 0, map.Resolution - 1);
            var near = map.Nearest[c.y * map.Resolution + c.x];
            for (var k = 0; k < 2; k++)
            {
                segment = k == 0 ? near.x : near.y;
                float2 spot;
                var found = kind == (int)RiverSpot.Riffle ? RiffleSpot(source, segment, out spot, out _, out _, out _) : SplashSpot(source, segment, out spot, out _);
                if (found && math.distancesq(xz, spot) < CopyReach * CopyReach)
                    return true;
            }
            segment = -1;
            return false;
        }

        static bool RapidReach(ref RiverMapBlob map, int i, float gradient, out float2 a, out float2 b)
        {
            a = b = default;
            if (i < 0 || map.Down[i] < 0 || map.Width[i] <= 0f || map.LakeDepth[i] > 0f)
                return false;
            var d = map.Down[i];
            const byte still = (byte)(RiverProfileKind.Fall | RiverProfileKind.Lake);
            if (map.Kind.Length > 0 && ((map.Kind[i] & still) != 0 || (map.Kind[d] & still) != 0))
                return false;
            a = map.Point(i);
            b = map.Point(d);
            return (map.WaterLevel[i] - map.WaterLevel[d]) / math.max(math.distance(a, b), 1f) >= gradient;
        }

        // Fall pools are carved at fall feet, so keep a reach's spot clear of falls either way along it.
        static bool NearFall(ref RiverMapBlob map, float2 xz, float2 flow) =>
            OnFall(ref map, xz) || OnFall(ref map, xz + flow * 24f) || OnFall(ref map, xz - flow * 24f);

        static float WetEdge(in TerrainSource source, float2 p, float2 dir, float water)
        {
            for (var d = 0.25f; d <= 8f; d += 0.25f)
                if (source.SampleHeight(p + dir * d) > water - 0.03f)
                    return d;
            return -1f;
        }

        static bool LowBank(in TerrainSource source, float2 end, float2 outward, float water) =>
            NearBank(source.SampleGround(end), water) && source.SampleHeight(end + outward * 2.5f) < water + 2.5f;

        static bool RiffleSpot(in TerrainSource source, int segment, out float2 mid, out float2 across, out int stones, out float scale)
        {
            mid = across = default;
            stones = -1;
            scale = 1f;
            ref var map = ref source.Rivers.Value;
            if (!RapidReach(ref map, segment, RiffleGradient, out var a, out var b) ||
                ReachDraw(map.Seed, (uint)segment, (int)RiverSpot.Riffle) < RiffleOdds)
                return false;
            var flow = math.normalizesafe(b - a);
            var p = math.lerp(a, b, 0.5f);
            var g = source.SampleGround(p);
            if (!g.HasWater || NearFall(ref map, p, flow) || RiverSampling.Query(ref map, source.RiverSettings, p).Isle > 0f)
                return false;
            across = new float2(-flow.y, flow.x);
            var sideA = WetEdge(source, p, across, g.WaterSurface);
            var sideB = WetEdge(source, p, -across, g.WaterSurface);
            stones = sideA < 0f || sideB < 0f ? -1 : FloraGenerator.RiffleStones(sideA + sideB);
            if (stones < 0)
                return false;
            mid = p + across * (sideA - sideB) * 0.5f;
            scale = math.clamp((sideA + sideB) / FloraGenerator.RiffleRowLength(stones), 0.8f, 1.3f);
            var half = across * (sideA + sideB + 1f) * 0.5f;
            return LowBank(source, mid + half, across, g.WaterSurface) && LowBank(source, mid - half, -across, g.WaterSurface);
        }

        public static bool SplashSpot(in TerrainSource source, int segment, out float2 xz, out float scale)
        {
            xz = default;
            scale = 1f;
            ref var map = ref source.Rivers.Value;
            if (!RapidReach(ref map, segment, SplashGradient, out var a, out var b) ||
                ReachDraw(map.Seed, (uint)segment, (int)RiverSpot.Splash) < SplashOdds)
                return false;
            var h = BiomeSampling.Scramble(math.hash(new uint2((uint)segment, map.Seed ^ 0x51A5u)));
            var flow = math.normalizesafe(b - a);
            var side = ((h >> 8 & 255u) / 127.5f - 1f) * 0.25f * map.Width[segment];
            xz = math.lerp(a, b, 0.3f + 0.4f * (h & 255u) / 255f) + new float2(-flow.y, flow.x) * side;
            scale = 0.8f + 0.5f * (h >> 16 & 255u) / 255f;
            var g = source.SampleGround(xz);
            var depth = g.WaterSurface - g.Height;
            return g.HasWater && depth > 0.35f && depth < 2.2f && !NearFall(ref map, xz, flow) &&
                   RiverSampling.Query(ref map, source.RiverSettings, xz).Isle <= 0f;
        }

        static FloraSystem.FloraInstance RapidsStone(in TerrainSource source, in FloraRules rule, float2 xz)
        {
            if (!source.Rivers.IsCreated || !RapidsSpot(source, xz, rule.RiverSpot, out var segment))
                return new FloraSystem.FloraInstance { Model = -1 };
            var rng = Random.CreateFromIndex((uint)segment * 2u + (uint)rule.RiverSpot);
            float2 at;
            float yaw, scale = 1f;
            int model;
            if (rule.RiverSpot == (int)RiverSpot.Riffle)
            {
                RiffleSpot(source, segment, out at, out var across, out var stones, out scale);
                yaw = math.atan2(across.y, across.x) + rng.NextFloat(-0.12f, 0.12f);
                model = stones - 3 + 4 * rng.NextInt(math.max(1, rule.Variants / 4));
            }
            else
            {
                SplashSpot(source, segment, out at, out scale);
                yaw = rng.NextFloat(math.PI * 2f);
                model = rng.NextInt(rule.Variants);
            }
            if (model >= rule.Variants)
                return new FloraSystem.FloraInstance { Model = -1 };
            return new FloraSystem.FloraInstance
            {
                Position = new float3(at.x, source.SampleGround(at).WaterSurface, at.y),
                Yaw = yaw,
                Scale = scale,
                Model = rule.FirstModel + model,
                Random = rng.NextFloat(),
                Ground = GroundFor(source, rule, at, 1f, 0.25f)
            };
        }
    }
}
