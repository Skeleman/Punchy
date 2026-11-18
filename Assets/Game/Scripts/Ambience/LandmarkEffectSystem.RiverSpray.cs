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
        const float RiverSprayMaxRange = 260f;
        const int RapidSpotsPerSegment = 3;

        public static int LastRapids, LastMouths;

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
            int rapids = 0, mouths = 0;
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
                    for (int k = 0; k < RapidSpotsPerSegment; k++)
                    {
                        h = Scramble(h + (uint)k);
                        var t = (k + 0.2f + 0.6f * (h & 0xFF) / 255f) / RapidSpotsPerSegment;
                        var side = new float2(b.y - a.y, a.x - b.x) / length * width * 0.3f * (((h >> 8) & 0xFF) / 127.5f - 1f);
                        var xz = math.lerp(a, b, t) + side;
                        var size = width * 0.6f;
                        if (math.distance(xz, at) > math.min(Policy.ExtraReach(size, gone), range))
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
                            Shape = new float4(0.5f + 0.7f * strength, size * 0.5f, 0.45f + 0.3f * strength, 1.2f + 0.6f * v),
                            Timing = new float4(0f, 0.25f, 0.3f + 0.25f * strength, 8 + (int)(8f * strength))
                        });
                        rapids++;
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
            Kit.Spray.Set(riverOwner, riverSpray);
        }

        void ClearRiverSpray()
        {
            Kit.Spray.Set(riverOwner, null);
            riverCellAt = new int2(int.MinValue);
        }
    }
}
