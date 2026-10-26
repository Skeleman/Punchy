using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    [Serializable]
    public struct RiverSettings : IEquatable<RiverSettings>
    {
        public bool Enabled;
        [UnityEngine.Tooltip("Resolution of the drainage simulation, in metres per cell.")]
        public float CellSize;
        [UnityEngine.Tooltip("Rain-weighted area (km²) that must drain through a point before a river appears there.")]
        public float SourceCatchment;
        [UnityEngine.Tooltip("Catchment (km²) at which a river reaches its maximum width.")]
        public float FullCatchment;
        public float MinWidth;
        public float MaxWidth;
        [UnityEngine.Tooltip("Depth of the channel below its banks, in metres (wider rivers are a little deeper).")]
        public float ChannelDepth;
        [UnityEngine.Tooltip("Steepness of the valley walls the river cuts (rise per metre). Lower = wider, gentler valleys.")]
        public float BankSlope;
        [UnityEngine.Tooltip("How far the river wanders off the simulation grid, as a fraction of a cell.")]
        [UnityEngine.Range(0f, 0.9f)] public float Meander;
        [UnityEngine.Tooltip("Rivers never reshape terrain farther away than this, in metres.")]
        public float MaxCarveDistance;
        [UnityEngine.Tooltip("Hollows the drainage fills become lakes only if at least this big (km²)…")]
        public float MinLakeArea;
        [UnityEngine.Tooltip("…and at least this deep (metres). Smaller hollows stay dry.")]
        public float MinLakeDepth;

        public static RiverSettings Default => new RiverSettings
        {
            Enabled = true,
            CellSize = 16f,
            SourceCatchment = 0.4f,
            FullCatchment = 40f,
            MinWidth = 4f,
            MaxWidth = 34f,
            ChannelDepth = 1.6f,
            BankSlope = 0.55f,
            Meander = 0.7f,
            MaxCarveDistance = 110f,
            MinLakeArea = 0.15f,
            MinLakeDepth = 5f
        };

        public bool Equals(RiverSettings o) =>
            Enabled == o.Enabled && CellSize == o.CellSize && SourceCatchment == o.SourceCatchment &&
            FullCatchment == o.FullCatchment && MinWidth == o.MinWidth && MaxWidth == o.MaxWidth &&
            ChannelDepth == o.ChannelDepth && BankSlope == o.BankSlope && Meander == o.Meander &&
            MaxCarveDistance == o.MaxCarveDistance && MinLakeArea == o.MinLakeArea && MinLakeDepth == o.MinLakeDepth;
    }

    /// <summary>
    /// The result of the drainage simulation on a coarse grid. Each cell drains to one downstream neighbour;
    /// cells carrying enough water hold a river segment from their (jittered) centre to their downstream cell's.
    /// </summary>
    public struct RiverMapBlob
    {
        public int Resolution;
        public float2 Origin;
        public float CellSize;
        public float Meander;
        public uint Seed;
        /// <summary>Drainage level: elevation with every dip filled, so it never rises downstream.</summary>
        public BlobArray<float> Level;
        public BlobArray<int> Down;
        /// <summary>River width at each cell, 0 = no river.</summary>
        public BlobArray<float> Width;
        /// <summary>Up to two river segments (by start cell) near each cell, -1 = none.</summary>
        public BlobArray<int2> Nearest;
        /// <summary>Depth of the lake covering this cell (metres); 0 = no lake. Only hollows big and deep enough count.</summary>
        public BlobArray<float> LakeDepth;

        public float2 Point(int index)
        {
            var cell = new int2(index % Resolution, index / Resolution);
            var h = math.hash(new int2(index, (int)Seed));
            var jitter = new float2(h & 0xFFFF, h >> 16) / 65535f - 0.5f;
            return Origin + ((float2)cell + 0.5f + jitter * Meander) * CellSize;
        }
    }

    public struct RiverQuery
    {
        public bool Found;
        public float Distance;   // to the river centreline, metres
        public float Level;      // bank level of the river at its closest point
        public float Width;
        public float Carve;      // lowest carved height among nearby rivers (handles confluences)
    }

    public static class RiverSampling
    {
        public static RiverQuery Query(ref RiverMapBlob map, in RiverSettings s, float2 xz)
        {
            var q = new RiverQuery { Carve = float.MaxValue, Distance = float.MaxValue };
            var cell = (int2)math.floor((xz - map.Origin) / map.CellSize);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var c = math.clamp(cell + new int2(x, y), 0, map.Resolution - 1);
                var candidates = map.Nearest[c.y * map.Resolution + c.x];
                Consider(ref map, s, xz, candidates.x, ref q);
                Consider(ref map, s, xz, candidates.y, ref q);
            }
            return q;
        }

        static void Consider(ref RiverMapBlob map, in RiverSettings s, float2 xz, int start, ref RiverQuery q)
        {
            if (start < 0)
                return;
            var down = map.Down[start];
            var a = map.Point(start);
            var b = down >= 0 ? map.Point(down) : a;
            var ab = b - a;
            var t = math.saturate(math.dot(xz - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
            var distance = math.distance(xz, a + ab * t);

            var endLevel = down >= 0 ? map.Level[down] : map.Level[start];
            var endWidth = down >= 0 && map.Width[down] > 0f ? map.Width[down] : map.Width[start];
            var level = math.lerp(map.Level[start], endLevel, t);
            var width = math.lerp(map.Width[start], endWidth, t);
            var carve = Profile(distance, level, width, s);

            if (carve < q.Carve)
                q.Carve = carve;
            if (distance - width * 0.5f < q.Distance - q.Width * 0.5f)
            {
                q.Found = true;
                q.Distance = distance;
                q.Level = level;
                q.Width = width;
            }
        }

        /// <summary>Cross-section: a rounded channel below the bank level, then valley walls rising at BankSlope.</summary>
        static float Profile(float distance, float level, float width, in RiverSettings s)
        {
            var half = width * 0.5f;
            if (distance < half)
            {
                var r = distance / half;
                return level - Depth(width, s) * (1f - r * r);
            }
            return level + (distance - half) * s.BankSlope;
        }

        static float Depth(float width, in RiverSettings s) => s.ChannelDepth + width * 0.04f;

        /// <summary>Lowers terrain into the nearby river's channel and valley; fades out by MaxCarveDistance.</summary>
        public static float Carve(in RiverQuery q, in RiverSettings s, float height)
        {
            if (!q.Found)
                return height;
            var carved = SmoothMin(height, q.Carve, 2f);
            var fade = math.smoothstep(s.MaxCarveDistance * 0.6f, s.MaxCarveDistance, q.Distance);
            return math.lerp(carved, height, fade);
        }

        /// <summary>min(a, b) with the corner rounded over a band of width k, so banks don't end in a crease.</summary>
        static float SmoothMin(float a, float b, float k)
        {
            var h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }

        public static float ChannelMask(in RiverQuery q) =>
            q.Found ? 1f - math.smoothstep(q.Width * 0.5f, q.Width * 0.5f + 2f, q.Distance) : 0f;

        /// <summary>Where the water surface will sit in this channel (a little below the banks).</summary>
        public static float WaterSurface(in RiverQuery q, in RiverSettings s) => q.Level - Depth(q.Width, s) * 0.25f;

        /// <summary>
        /// Lakes are the dips the drainage simulation filled; their surface is the fill level.
        /// Returns 0..1 lake coverage (bilinear across cells) and the lake surface height.
        /// </summary>
        public static float Lake(ref RiverMapBlob map, float2 xz, out float surface)
        {
            var uv = (xz - map.Origin) / map.CellSize - 0.5f;
            var i0 = math.clamp((int2)math.floor(uv), 0, map.Resolution - 2);
            var f = math.saturate(uv - i0);
            var i = i0.y * map.Resolution + i0.x;
            var w = new float4((1f - f.x) * (1f - f.y), f.x * (1f - f.y), (1f - f.x) * f.y, f.x * f.y);
            var idx = new int4(i, i + 1, i + map.Resolution, i + map.Resolution + 1);

            float coverage = 0f, level = 0f, levelWeight = 0f;
            for (int k = 0; k < 4; k++)
            {
                var isLake = map.LakeDepth[idx[k]] > 0.3f ? 1f : 0f;
                coverage += w[k] * isLake;
                level += w[k] * isLake * map.Level[idx[k]];
                levelWeight += w[k] * isLake;
            }
            surface = levelWeight > 0f ? level / levelWeight : float.MinValue;
            return coverage;
        }
    }
}
