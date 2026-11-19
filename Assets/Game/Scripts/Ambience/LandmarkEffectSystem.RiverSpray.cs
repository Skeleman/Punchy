using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using Kit = Punchy.Rendering.Effects.PlumeSystem;
using Policy = Punchy.Settings.DetailPolicy;

namespace Punchy.Ambience
{
    public partial class LandmarkEffectSystem
    {
        const float RapidGradient = 0.03f;
        // W-159 row 4: churn clumps only on strong rapids; splash wakes keep RapidGradient.
        const float ChurnGradient = 0.08f;
        const float RiverSprayMaxRange = 260f;
        const int RapidSpotsPerSegment = 3;

        public static int LastRapids, LastMouths, LastSplash;

        readonly object riverOwner = new object();
        readonly List<Kit.Plume> riverSpray = new List<Kit.Plume>();
        int2 riverCellAt = new int2(int.MinValue);
        float riverRangeAt;

        void RiverSpray(in TerrainSource source, Vector3 eye)
        {
            if (!source.Rivers.IsCreated)
                return;
            ref var map = ref source.Rivers.Value;
            var gone = Kit.Spray.Effect != null ? Kit.Spray.Effect.GonePixels : 16f;
            var range = math.min(Policy.ExtraReach(math.max(source.RiverSettings.MaxWidth, 4f), gone), RiverSprayMaxRange);
            var at = ((float3)eye).xz;
            var cell = (int2)math.floor((at - map.Origin) / map.CellSize);
            if (math.all(cell == riverCellAt) && math.abs(range - riverRangeAt) < map.CellSize)
                return;
            riverCellAt = cell;
            riverRangeAt = range;
            riverSpray.Clear();
            int rapids = 0, mouths = 0, splash = 0;
            var n = (int)math.ceil(range / map.CellSize) + 1;
            for (int z = -n; z <= n; z++)
            for (int x = -n; x <= n; x++)
            {
                var c = cell + new int2(x, z);
                if (math.any(c < 0) || math.any(c >= map.Resolution))
                    continue;
                var i = c.y * map.Resolution + c.x;
                var down = map.Down[i];
                var width = map.Width[i];
                if (width <= 0f || down < 0)
                    continue;
                var kind = map.Kind.Length > 0 ? (RiverProfileKind)map.Kind[i] : RiverProfileKind.Channel;
                if ((kind & (RiverProfileKind.Fall | RiverProfileKind.Lake)) != 0)
                    continue;
                var a = map.Point(i);
                var b = map.Point(down);
                var length = math.max(math.distance(a, b), 1f);
                var gradient = (map.WaterLevel[i] - map.WaterLevel[down]) / length;
                var h = Scramble(math.hash(new int2(i, 0x5B4A7)));
                if (gradient >= RapidGradient)
                {
                    var strength = math.saturate((gradient - RapidGradient) / (RapidGradient * 2f));
                    var churn = math.saturate((gradient - ChurnGradient) / ChurnGradient);
                    for (int k = 0; k < RapidSpotsPerSegment; k++)
                    {
                        h = Scramble(h + (uint)k);
                        if (gradient < ChurnGradient)
                            continue;
                        var t = (k + 0.2f + 0.6f * (h & 0xFF) / 255f) / RapidSpotsPerSegment;
                        var side = new float2(b.y - a.y, a.x - b.x) / length * width * 0.3f * (((h >> 8) & 0xFF) / 127.5f - 1f);
                        var xz = math.lerp(a, b, t) + side;
                        var size = width * 0.6f;
                        var clump = (0.35f + 0.25f * churn) * 3.3f;
                        if (math.distance(xz, at) - size * 0.3f > math.min(Policy.ExtraReach(clump, gone), range))
                            continue;
                        var ground = source.SampleGround(xz);
                        if (!ground.HasWater || ground.WaterSurface < ground.Height + 0.1f)
                            continue;
                        var v = ((h >> 16) & 0xFF) / 255f;
                        riverSpray.Add(new Kit.Plume
                        {
                            Position = new float3(xz.x, ground.WaterSurface - 0.05f, xz.y),
                            Seed = ((h >> 20) & 0xFFF) / 4096f,
                            Color = new float4(0.93f, 0.95f, 0.97f, 0f),
                            Shape = new float4(0.3f + 0.4f * churn, size * 0.3f, 0.35f + 0.25f * churn, 1.6f + 0.8f * v),
                            Timing = new float4(0f, 0.25f, 0.3f + 0.25f * churn, 24 + (int)(24f * churn)),
                            Out = (b - a) / length * (1f + 2f * churn)
                        });
                        rapids++;
                    }
                    if (Punchy.Flora.FloraPlacement.SplashSpot(source, i, out var rock, out var rockScale) &&
                        math.distance(rock, at) < math.min(Policy.ExtraReach(rockScale * 3f, gone), range))
                    {
                        SplashTail(source, rock, rockScale, (b - a) / length, strength, h);
                        splash++;
                    }
                }
                if ((kind & RiverProfileKind.Mouth) != 0)
                {
                    var size = width * 1.2f;
                    if (math.distance(b, at) > math.min(Policy.ExtraReach(size, gone), range))
                        continue;
                    var ground = source.SampleGround(b);
                    if (!ground.HasWater)
                        continue;
                    var v = ((h >> 12) & 0xFF) / 255f;
                    riverSpray.Add(new Kit.Plume
                    {
                        Position = new float3(b.x, ground.WaterSurface, b.y),
                        Seed = ((h >> 4) & 0xFFF) / 4096f,
                        Color = new float4(0.9f, 0.93f, 0.96f, 0f),
                        Shape = new float4(1.2f + 0.8f * v, size * 0.6f, 1.3f + 0.5f * v, 4f + 2f * v),
                        Timing = new float4(0f, 0.25f, 0.18f, 12)
                    });
                    mouths++;
                }
            }
            LastRapids = rapids;
            LastMouths = mouths;
            LastSplash = splash;
            Kit.Spray.Set(riverOwner, riverSpray);
        }

        void SplashTail(in TerrainSource source, float2 rock, float scale, float2 flow, float strength, uint h)
        {
            var surface = source.SampleGround(rock).WaterSurface;
            var radius = scale * 1.1f;
            riverSpray.Add(new Kit.Plume
            {
                Position = new float3(rock.x + flow.x * radius, surface - 0.03f, rock.y + flow.y * radius),
                Seed = ((h >> 6) & 0xFFF) / 4096f,
                Color = new float4(0.94f, 0.96f, 0.98f, 0f),
                Shape = new float4(0.12f, radius * 0.9f, 0.45f + 0.2f * strength, 2.6f),
                Timing = new float4(0f, 0.25f, 0.5f + 0.2f * strength, 32),
                Out = flow * (2.2f + 1.5f * strength)
            });
            riverSpray.Add(new Kit.Plume
            {
                Position = new float3(rock.x - flow.x * radius, surface, rock.y - flow.y * radius),
                Seed = ((h >> 18) & 0xFFF) / 4096f,
                Color = new float4(0.95f, 0.97f, 0.99f, 0f),
                Shape = new float4(0.5f + 0.3f * strength, radius * 0.7f, 0.4f, 1.2f),
                Timing = new float4(0f, 0.25f, 0.45f, 14),
                Out = flow * 0.6f
            });
        }

        void ClearRiverSpray()
        {
            Kit.Spray.Set(riverOwner, null);
            riverCellAt = new int2(int.MinValue);
        }
    }
}
