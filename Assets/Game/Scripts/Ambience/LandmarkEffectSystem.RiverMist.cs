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
        const float RiverMistMaxRange = 220f;
        const int MistSpotsPerSegment = 2;
        const float MistHourStep = 0.25f;

        public static int LastMistSpots;
        public static float LastMistByHour;

        readonly object mistOwner = new object();
        readonly List<Kit.Plume> riverMist = new List<Kit.Plume>();
        int2 mistCellAt = new int2(int.MinValue);
        float mistRangeAt;
        int mistHourAt = int.MinValue, mistClimateAt = -1;

        public static float MistByHour(float hour)
        {
            hour = math.fmod(math.fmod(hour, 24f) + 24f, 24f);
            return hour < 12f
                ? math.smoothstep(11f, 7f, hour) * math.lerp(0.6f, 1f, math.smoothstep(3f, 6.5f, hour))
                : math.smoothstep(15f, 19f, hour) * math.lerp(1f, 0.6f, math.smoothstep(20f, 23.5f, hour));
        }

        public static float MistByHeat(float heat) => math.lerp(1f, 0.2f, math.smoothstep(0.35f, 0.8f, heat));

        void RiverMist(in TerrainSource source, Vector3 eye)
        {
            if (!source.Rivers.IsCreated)
                return;
            ref var map = ref source.Rivers.Value;
            ref var grid = ref Punchy.Weather.Climate.Grid.Data;
            var hour = grid.Now.Known ? grid.Now.Hour : 12f;
            var byHour = MistByHour(hour);
            var hourKey = byHour <= 0f ? -1 : (int)math.floor(hour / MistHourStep);
            var gone = Kit.Steam.Effect != null ? Kit.Steam.Effect.GonePixels : 16.8f;
            var range = math.min(Policy.ExtraReach(math.max(source.RiverSettings.MaxWidth * 0.5f, 3f), gone), RiverMistMaxRange);
            var at = ((float3)eye).xz;
            var cell = (int2)math.floor((at - map.Origin) / map.CellSize);
            if (math.all(cell == mistCellAt) && math.abs(range - mistRangeAt) < map.CellSize && hourKey == mistHourAt && grid.Ready == mistClimateAt)
                return;
            mistCellAt = cell;
            mistRangeAt = range;
            mistHourAt = hourKey;
            mistClimateAt = grid.Ready;
            LastMistByHour = byHour;
            riverMist.Clear();
            if (byHour > 0f)
            {
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
                    if ((kind & RiverProfileKind.Fall) != 0)
                        continue;
                    var a = map.Point(i);
                    var b = map.Point(down);
                    var length = math.max(math.distance(a, b), 1f);
                    var h = Scramble(math.hash(new int2(i, 0x3C1D9)));
                    for (int k = 0; k < MistSpotsPerSegment; k++)
                    {
                        h = Scramble(h + (uint)k);
                        var t = (k + 0.15f + 0.7f * (h & 0xFF) / 255f) / MistSpotsPerSegment;
                        var side = new float2(b.y - a.y, a.x - b.x) / length * width * 0.25f * (((h >> 8) & 0xFF) / 127.5f - 1f);
                        var xz = math.lerp(a, b, t) + side;
                        var spread = math.clamp(width * 0.45f, 1.5f, 9f);
                        var puff = math.clamp(width * 0.25f, 1.2f, 3f);
                        if (math.distance(xz, at) > math.min(Policy.ExtraReach(math.max(spread, puff), gone), range))
                            continue;
                        var ground = source.SampleGround(xz);
                        if (!ground.HasWater || ground.WaterSurface < ground.Height + 0.1f)
                            continue;
                        var position = new float3(xz.x, ground.WaterSurface, xz.y);
                        var opacity = 0.35f * byHour * MistByHeat(Punchy.Weather.Climate.At(position).Heat);
                        if (opacity < 0.01f)
                            continue;
                        var v = ((h >> 16) & 0xFF) / 255f;
                        riverMist.Add(new Kit.Plume
                        {
                            Position = position,
                            Seed = ((h >> 20) & 0xFFF) / 4096f,
                            Color = new float4(0.88f, 0.9f, 0.93f, 0f),
                            Shape = new float4(0.3f + 0.3f * v, spread, puff, 9f + 4f * v),
                            Timing = new float4(0f, 0.25f, opacity, 8),
                            Fall = new float4(0.8f, 0f, 0f, 0f)
                        });
                    }
                }
            }
            LastMistSpots = riverMist.Count;
            Kit.Steam.Set(mistOwner, riverMist);
        }

        void ClearRiverMist()
        {
            Kit.Steam.Set(mistOwner, null);
            mistCellAt = new int2(int.MinValue);
        }
    }
}
