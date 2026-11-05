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
        [UnityEngine.Tooltip("Steepest a riverbed may run (drop per metre). Where the land falls faster, the river " +
                             "cuts a gorge back upstream to spread the drop out.")]
        public float MaxGradient;
        [UnityEngine.Tooltip("How far the river wanders off the simulation grid, as a fraction of a cell.")]
        [UnityEngine.Range(0f, 0.9f)] public float Meander;
        [UnityEngine.Tooltip("Rivers never reshape terrain farther away than this, in metres.")]
        public float MaxCarveDistance;
        [UnityEngine.Tooltip("Hollows the drainage fills become lakes only if at least this big (km²)…")]
        public float MinLakeArea;
        [UnityEngine.Tooltip("…and at least this deep (metres). Smaller hollows stay dry and rivers cut through their rims.")]
        public float MinLakeDepth;

        [UnityEngine.Header("Precipitation")]
        [UnityEngine.Tooltip("Rain multiplier at sea level. Below 1 = dry lowlands, so rivers rarely start there.")]
        public float LowlandRain;
        [UnityEngine.Tooltip("Rain multiplier at Highland Height and above (moist air rising over high ground).")]
        public float HighlandRain;
        public float HighlandHeight;
        [UnityEngine.Tooltip("Extra water from snow-covered ground (meltwater), added at full snow depth.")]
        public float Snowmelt;

        [UnityEngine.Header("Shape (version 2)")]
        [UnityEngine.Tooltip("Run each river segment as a smooth curve (Hermite spline through the segment points) instead of a straight line.")]
        public bool Smooth;
        [UnityEngine.Tooltip("A segment whose bed drops at least this much (m) becomes a waterfall: a step in the bed. 0 = none. Set MaxGradient high too, or the drop is spread into a gorge.")]
        public float FallDrop;
        [UnityEngine.Tooltip("A river may only start on ground at least this high above sea level (m); lower cells carry water only downstream of such a source. 0 = anywhere.")]
        public float SourceHeight;

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
            MaxGradient = 0.12f,
            Meander = 0.7f,
            MaxCarveDistance = 110f,
            MinLakeArea = 0.15f,
            MinLakeDepth = 5f,
            LowlandRain = 0.35f,
            HighlandRain = 2.5f,
            HighlandHeight = 180f,
            Snowmelt = 3f
        };

        public bool Equals(RiverSettings o) =>
            Enabled == o.Enabled && CellSize == o.CellSize && SourceCatchment == o.SourceCatchment &&
            FullCatchment == o.FullCatchment && MinWidth == o.MinWidth && MaxWidth == o.MaxWidth &&
            ChannelDepth == o.ChannelDepth && BankSlope == o.BankSlope && MaxGradient == o.MaxGradient && Meander == o.Meander &&
            MaxCarveDistance == o.MaxCarveDistance && MinLakeArea == o.MinLakeArea && MinLakeDepth == o.MinLakeDepth &&
            LowlandRain == o.LowlandRain && HighlandRain == o.HighlandRain && HighlandHeight == o.HighlandHeight &&
            Snowmelt == o.Snowmelt && Smooth == o.Smooth && FallDrop == o.FallDrop && SourceHeight == o.SourceHeight;
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
        /// <summary>Surface height of the lake or pond covering each cell (only meaningful where LakeDepth &gt; 0).</summary>
        public BlobArray<float> LakeLevel;
        /// <summary>
        /// River bank level: follows the ground down, never rising downstream; where the ground rises across the
        /// path (a hollow's rim) the river cuts through it instead. Equals the lake surface inside lakes.
        /// </summary>
        public BlobArray<float> Bed;
        /// <summary>Water surface per river cell: never rises downstream, and backs up to the level of a lake ahead.</summary>
        public BlobArray<float> WaterLevel;
        public BlobArray<int> Down;
        /// <summary>River width at each cell, 0 = no river.</summary>
        public BlobArray<float> Width;
        /// <summary>Up to two river segments (by start cell) near each cell, -1 = none.</summary>
        public BlobArray<int2> Nearest;
        /// <summary>Depth of the lake covering this cell (metres); 0 = no lake. Only hollows big and deep enough count.</summary>
        public BlobArray<float> LakeDepth;
        /// <summary>
        /// The highest lake level within two cells of each cell (float.MinValue if none). Ground below it there may be
        /// a bay or beach pocket of that lake that the cell grid misses (TerrainSource fills it if it connects).
        /// </summary>
        public BlobArray<float> ShoreLevel;
        /// <summary>The main upstream neighbour of each river cell (the widest feeding it), -1 = a source. Used to bend curves.</summary>
        public BlobArray<int> Up;
        /// <summary>Waterfalls (only when RiverSettings.FallDrop &gt; 0): where a segment's bed steps down.</summary>
        public BlobArray<RiverFall> Falls;
        /// <summary>
        /// W-37: where each cell's segment starts (RiverBaking.PathPointsJob): the jittered centre, smoothed along wide
        /// rivers on island v2 so their bends lengthen with width. Empty in a blob baked before W-37.
        /// </summary>
        public BlobArray<float2> Points;

        public float2 Point(int index) => Points.Length > 0 ? Points[index] : JitteredPoint(index, Resolution, Origin, CellSize, Meander, Seed);

        /// <summary>Where a cell's river segment starts: its centre, nudged by a per-cell random offset so rivers meander.</summary>
        public static float2 JitteredPoint(int index, int resolution, float2 origin, float cellSize, float meander, uint seed)
        {
            var cell = new int2(index % resolution, index / resolution);
            var h = math.hash(new int2(index, (int)seed));
            var jitter = new float2(h & 0xFFFF, h >> 16) / 65535f - 0.5f;
            return origin + ((float2)cell + 0.5f + jitter * meander) * cellSize;
        }
    }

    /// <summary>A river waterfall: the lip of the water, which way it runs, how far it falls and how wide it is.</summary>
    public struct RiverFall
    {
        public float3 Lip;
        public float2 Out;
        public float Drop;
        public float Width;
    }

    public struct RiverQuery
    {
        public bool Found;
        public float Distance;   // to the river centreline, metres
        public float Level;      // bank level of the river at its closest point
        public float Width;
        public float Carve;      // lowest carved height among nearby rivers (handles confluences)
        public float Water;      // water surface of the river that carved the ground lowest here
        public float FallWater;  // upper surface of a waterfall whose channel covers this point above its lip (else MinValue)
        public float2 Flow;      // downstream direction at the closest point
        public float Gradient;   // drop per metre of the riverbed at the closest point (no more than its water's drop)
    }

    public static class RiverSampling
    {
        /// <summary>The highest lake level near a point (RiverMapBlob.ShoreLevel of its cell).</summary>
        public static float ShoreLevel(ref RiverMapBlob map, float2 xz)
        {
            var c = math.clamp((int2)math.floor((xz - map.Origin) / map.CellSize), 0, map.Resolution - 1);
            return map.ShoreLevel[c.y * map.Resolution + c.x];
        }

        /// <summary>
        /// The nearest lake cell within two cells whose surface is above <paramref name="above"/>: its centre and level.
        /// </summary>
        public static bool NearestLakeAbove(ref RiverMapBlob map, float2 xz, float above, out float2 centre, out float level)
        {
            var c = (int2)math.floor((xz - map.Origin) / map.CellSize);
            centre = default;
            level = float.MinValue;
            var best = float.MaxValue;
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                var n = c + new int2(dx, dz);
                if (math.any(n < 0) || math.any(n >= map.Resolution))
                    continue;
                var i = n.y * map.Resolution + n.x;
                if (map.LakeDepth[i] <= 0f || map.LakeLevel[i] <= above)
                    continue;
                var at = map.Origin + ((float2)n + 0.5f) * map.CellSize;
                var d = math.distancesq(at, xz);
                if (d < best)
                {
                    best = d;
                    centre = at;
                    level = map.LakeLevel[i];
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>River water surfaces extend this far (metres) past the channel edge, under the banks.</summary>
        public const float SurfaceMargin = 8f;

        /// <summary>
        /// W-43c bank roughness at one point: the channel's width scale, the valley walls' slope and a broken
        /// shoreline offset. Neutral (1, BankSlope, 0) unless <c>rough</c>; the terrain only turns it on once every
        /// place is baked (see <see cref="TerrainSource.RiverRoughOn"/>), so no landmark or cave moves.
        /// </summary>
        struct Bank
        {
            public float WidthScale, Slope, Edge, BaseSlope;

            public static Bank At(ref RiverMapBlob map, in RiverSettings s, float2 xz, bool rough)
            {
                if (!rough || !s.Smooth)
                    return new Bank { WidthScale = 1f, Slope = s.BankSlope };
                // Seeded offsets so each island's banks differ (multiplied, not math.hash: that has no avalanche).
                var off = new float2((map.Seed * 0x9E3779B9u) >> 20, (map.Seed * 0x85EBCA6Bu) >> 20);
                var p = xz + off;
                // Width changes slowly along the river (about ±20%); left and right banks wander independently.
                var width = 1f + 0.2f * noise.snoise(p / 90f);
                // Some reaches cut steep banks, others leave gentle shelves.
                var slope = s.BankSlope * math.lerp(0.5f, 2.2f, math.smoothstep(-0.6f, 0.6f, noise.snoise(p / 140f + 17.3f)));
                // A broken shoreline: bumps and bites of up to about a metre along the water's edge.
                var edge = 0.6f * noise.snoise(p / 9f + 41.7f) + 0.3f * noise.snoise(p / 3.5f - 23.1f);
                return new Bank { WidthScale = width, Slope = slope, Edge = edge, BaseSlope = s.BankSlope };
            }
        }

        /// <param name="maxBankSlope">Caps the valley walls' slope here (W-73: v3 pass tracks keep river banks walkable).</param>
        public static RiverQuery Query(ref RiverMapBlob map, in RiverSettings s, float2 xz, bool rough = false, float maxBankSlope = float.MaxValue)
        {
            var q = new RiverQuery { Carve = float.MaxValue, Distance = float.MaxValue, FallWater = float.MinValue };
            var bank = Bank.At(ref map, s, xz, rough);
            bank.Slope = math.min(bank.Slope, maxBankSlope);
            var cell = (int2)math.floor((xz - map.Origin) / map.CellSize);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var c = math.clamp(cell + new int2(x, y), 0, map.Resolution - 1);
                var candidates = map.Nearest[c.y * map.Resolution + c.x];
                Consider(ref map, s, bank, xz, candidates.x, ref q);
                Consider(ref map, s, bank, xz, candidates.y, ref q);
            }
            // Above a waterfall's lip the pool below carves its wide bowl back up under the upper river (cells are
            // narrower than the channel), so the lower water would win there and leave land between the two surfaces.
            // The upper surface runs on to the lip instead, and the curtain falls from there (W-22).
            if (q.FallWater > q.Water)
                q.Water = q.FallWater;
            return q;
        }

        /// <summary>Hermite curve of a segment: from a to b, bent towards the upstream and downstream neighbours so bends are smooth.</summary>
        static void Curve(ref RiverMapBlob map, int start, int down, out float2 a, out float2 b, out float2 ta, out float2 tb)
        {
            a = map.Point(start);
            b = map.Point(down);
            var ab = b - a;
            var len = math.length(ab);
            var up = map.Up[start];
            ta = (up >= 0 ? math.normalizesafe(b - map.Point(up), ab) : math.normalizesafe(ab)) * len;
            var next = map.Down[down];
            tb = (next >= 0 ? math.normalizesafe(map.Point(next) - a, ab) : math.normalizesafe(ab)) * len;
        }

        static float2 Hermite(float2 a, float2 b, float2 ta, float2 tb, float u)
        {
            var u2 = u * u; var u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * a + (u3 - 2 * u2 + u) * ta + (-2 * u3 + 3 * u2) * b + (u3 - u2) * tb;
        }

        /// <summary>The curve's midpoint and flow direction there (a waterfall's lip).</summary>
        public static float2 CurveMid(ref RiverMapBlob map, int start, out float2 flow)
        {
            var down = map.Down[start];
            Curve(ref map, start, down, out var a, out var b, out var ta, out var tb);
            flow = math.normalizesafe(Hermite(a, b, ta, tb, 0.51f) - Hermite(a, b, ta, tb, 0.49f), b - a);
            return Hermite(a, b, ta, tb, 0.5f);
        }

        static void Consider(ref RiverMapBlob map, in RiverSettings s, in Bank bank, float2 xz, int start, ref RiverQuery q)
        {
            if (start < 0)
                return;
            var down = map.Down[start];
            var a = map.Point(start);
            var b = down >= 0 ? map.Point(down) : a;
            var ab = b - a;
            float t, distance;
            var flow = math.normalizesafe(ab);
            if (s.Smooth && down >= 0)
            {
                // Nearest point on the curve: five samples, then refine either side of the best one.
                Curve(ref map, start, down, out a, out b, out var ta, out var tb);
                var best = 0; var bestD = float.MaxValue;
                for (int k = 0; k <= 4; k++)
                {
                    var d = math.distancesq(xz, Hermite(a, b, ta, tb, k * 0.25f));
                    if (d < bestD) { bestD = d; best = k; }
                }
                t = best * 0.25f; distance = math.sqrt(bestD);
                for (int pass = 0; pass < 2; pass++)
                {
                    var step = pass == 0 ? 0.125f : 0.0625f;
                    var centre = t;
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        var u = math.saturate(centre + sgn * step);
                        var d = math.distance(xz, Hermite(a, b, ta, tb, u));
                        if (d < distance) { distance = d; t = u; }
                    }
                }
                flow = math.normalizesafe(Hermite(a, b, ta, tb, math.min(t + 0.02f, 1f)) - Hermite(a, b, ta, tb, math.max(t - 0.02f, 0f)), flow);
            }
            else
            {
                t = math.saturate(math.dot(xz - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
                distance = math.distance(xz, a + ab * t);
            }

            var endLevel = down >= 0 ? map.Bed[down] : map.Bed[start];
            var endWidth = down >= 0 && map.Width[down] > 0f ? map.Width[down] : map.Width[start];
            // A waterfall is a step in the bed half way along the segment.
            var fall = s.FallDrop > 0f && map.Bed[start] - endLevel >= s.FallDrop;
            var tl = fall ? math.smoothstep(0.46f, 0.54f, t) : t;
            var level = math.lerp(map.Bed[start], endLevel, tl);
            var baseWidth = math.lerp(map.Width[start], endWidth, t);
            var width = baseWidth * bank.WidthScale;
            var carve = Profile(distance, level, width, Depth(baseWidth, s), bank);

            // The water here belongs to whichever river shaped the ground lowest, so water and banks always agree.
            if (carve < q.Carve)
            {
                q.Carve = carve;
                q.Water = math.lerp(map.WaterLevel[start], down >= 0 ? map.WaterLevel[down] : map.WaterLevel[start], tl);
            }
            if (fall && tl < 0.5f && distance < width * 0.5f)
                q.FallWater = math.max(q.FallWater, math.lerp(map.WaterLevel[start], map.WaterLevel[down], tl));
            if (distance - width * 0.5f < q.Distance - q.Width * 0.5f)
            {
                q.Found = true;
                q.Distance = distance;
                q.Level = level;
                // The water claims at least the unwobbled width: a narrowed channel leaves bank inside the claim
                // rather than dry ground below the water just past its edge (an open end on a low floodplain).
                q.Width = math.max(width, baseWidth);
                q.Flow = flow;
                // The water's fall too (W-36): where it backs up flat to a lake or a dam over a steep bed, no rapids.
                // W-98: blended mid-segment to mid-segment with the segment upstream or downstream, so the rapids (and
                // the water's apparent speed) ramp up over a segment into white water rather than jumping at a cell.
                var here = SegmentGradient(ref map, start);
                var next = t < 0.5f ? (map.Up[start] >= 0 ? SegmentGradient(ref map, map.Up[start]) : here)
                                    : (down >= 0 && map.Down[down] >= 0 ? SegmentGradient(ref map, down) : here);
                q.Gradient = math.lerp(here, next, math.abs(t - 0.5f));
            }

            // W-98: a river never starts out of bare ground. Its head wells up from a spring pool, a round basin wider
            // than the stream and set a little back from it (not where it rises out of a lake).
            if (s.Smooth && down >= 0 && map.Up[start] < 0 && map.LakeDepth[start] <= 0f)
            {
                var poolWidth = math.max(map.Width[start] * 2.2f, 5f) * bank.WidthScale;
                var centre = a - math.normalizesafe(ab) * poolWidth * 0.25f;
                var pd = math.distance(xz, centre);
                var pc = Profile(pd, map.Bed[start], poolWidth, Depth(map.Width[start], s) * 1.3f, bank);
                if (pc < q.Carve)
                {
                    q.Carve = pc;
                    q.Water = map.WaterLevel[start];
                }
                if (pd - poolWidth * 0.5f < q.Distance - q.Width * 0.5f)
                {
                    q.Found = true;
                    q.Distance = pd;
                    q.Level = map.Bed[start];
                    q.Width = poolWidth;
                    q.Flow = math.normalizesafe(ab);
                    q.Gradient = 0f;
                }
            }
        }

        /// <summary>The water's fall per metre along the segment from cell start to the next one down (0 at a river's end).</summary>
        static float SegmentGradient(ref RiverMapBlob map, int start)
        {
            var down = map.Down[start];
            if (down < 0)
                return 0f;
            var len = math.max(math.length(map.Point(down) - map.Point(start)), 1e-3f);
            return math.min(map.Bed[start] - map.Bed[down], map.WaterLevel[start] - map.WaterLevel[down]) / len;
        }

        /// <summary>
        /// Cross-section: a rounded channel below the bank level, then valley walls rising at the bank's slope. The
        /// depth comes from the unwobbled width so the bed stays continuous; the shoreline offset only acts in a
        /// band round the water's edge.
        /// </summary>
        static float Profile(float distance, float level, float width, float depth, in Bank bank)
        {
            var half = width * 0.5f;
            var h = distance < half
                ? level - depth * (1f - (distance / half) * (distance / half))
                : level + (distance - half) * bank.Slope;
            if (bank.Edge != 0f)
            {
                h += bank.Edge * math.smoothstep(half - 1.5f, half + 1f, distance) * (1f - math.smoothstep(half + 5f, half + 12f, distance));
                // A dip in the noisy edge must not drop the bank below the water, or the river spills out (an open end).
                if (distance >= half)
                    h = math.max(h, level + 0.4f + math.min(distance - half, SurfaceMargin + 2f) * bank.BaseSlope);
            }
            return h;
        }

        public static float Depth(float width, in RiverSettings s) => s.ChannelDepth + width * 0.04f;

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

        /// <summary>Where the water surface sits in this channel (a little below the banks, or a lake's level ahead).</summary>
        public static float WaterSurface(in RiverQuery q) => q.Water;

        /// <summary>
        /// Lakes are the dips the drainage simulation filled; their surface is the fill level.
        /// Returns 0..1 lake coverage (bilinear across cells) and the lake surface height, which is defined
        /// anywhere within a cell of the lake so the surface reaches past the shoreline; and the deepest of those
        /// lake cells (so callers can tell a shore from ground that falls away beyond the lake, below its outlet).
        /// </summary>
        public static float Lake(ref RiverMapBlob map, float2 xz, out float surface, out float depth)
        {
            var uv = (xz - map.Origin) / map.CellSize - 0.5f;
            var i0 = math.clamp((int2)math.floor(uv), 0, map.Resolution - 2);
            var f = math.saturate(uv - i0);
            var i = i0.y * map.Resolution + i0.x;
            var w = new float4((1f - f.x) * (1f - f.y), f.x * (1f - f.y), (1f - f.x) * f.y, f.x * f.y);
            var idx = new int4(i, i + 1, i + map.Resolution, i + map.Resolution + 1);

            float coverage = 0f, level = 0f, levelWeight = 0f;
            depth = 0f;
            for (int k = 0; k < 4; k++)
            {
                var isLake = map.LakeDepth[idx[k]] > 0.3f ? 1f : 0f;
                depth = math.max(depth, map.LakeDepth[idx[k]] * isLake);
                coverage += w[k] * isLake;
                level += w[k] * isLake * map.LakeLevel[idx[k]];
                levelWeight += w[k] * isLake;
            }
            surface = levelWeight > 0f ? level / levelWeight : float.MinValue;
            return coverage;
        }
    }
}
