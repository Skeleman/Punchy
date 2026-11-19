using System;
using Unity.Entities;
using Unity.Collections;
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
        /// <summary>W-153: lowest pre-river ground just outside each river cell's banks (float.MaxValue off rivers and in lakes).</summary>
        public BlobArray<float> BankLow;
        /// <summary>W-153: each cell's segment kind (RiverProfileKind bits, 0 off the rivers).</summary>
        public BlobArray<byte> Kind;

        /// <summary>W-153: the profile of the segment from cell start to the cell it drains into. Sampling reads rivers through this.</summary>
        public RiverProfile Profile(int start)
        {
            var down = Down[start];
            var end = down >= 0 ? down : start;
            return new RiverProfile
            {
                Start = start, Down = down,
                BedA = Bed[start], BedB = Bed[end],
                WaterA = WaterLevel[start], WaterB = WaterLevel[end],
                WidthA = Width[start], WidthB = down >= 0 && Width[down] > 0f ? Width[down] : Width[start],
                BankLow = BankLow.Length > 0 ? BankLow[start] : float.MaxValue,
                Kind = Kind.Length > 0 ? (RiverProfileKind)Kind[start] : RiverProfileKind.None,
            };
        }

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
        public float Carve;      // carved height of the nearest river's profile, blended with others near joins
        public float Water;      // water surface of the same profiles, blended the same way
        public float FallWater;  // upper water of a fall whose channel covers this point above its lip line (else MinValue)
        public float ShoulderLevel, ShoulderWeight; // rock a fall's lip rises to beside its sheet, and how much (W-153)
        public float PoolFloor, PoolWeight, PoolWater; // #301: floor and water of a fall's foot pool here, and how much it holds the carve down
        public float2 Flow;      // downstream direction at the closest point
        public float Gradient;   // drop per metre of the riverbed at the closest point (no more than its water's drop)
        public float Cut;        // #301 row 2: height of the cut bank face here (outer side of a bend, else 0)
        public float Bar;
        public float Isle;
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
            public float WidthScale, Slope, Edge, BaseSlope, Cut;

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
                return new Bank { WidthScale = width, Slope = slope, Edge = edge, BaseSlope = s.BankSlope, Cut = 1f };
            }
        }

        /// <param name="maxBankSlope">Caps the valley walls' slope here (W-73: v3 pass tracks keep river banks walkable).</param>
        public static RiverQuery Query(ref RiverMapBlob map, in RiverSettings s, float2 xz, bool rough = false, float maxBankSlope = float.MaxValue,
            float barScale = 1f)
        {
            var q = new RiverQuery { Carve = float.MaxValue, Distance = float.MaxValue, FallWater = float.MinValue };
            var bank = Bank.At(ref map, s, xz, rough);
            bank.Slope = math.min(bank.Slope, maxBankSlope);
            bank.Cut *= math.smoothstep(0.5f, 1.25f, maxBankSlope);
            var cell = (int2)math.floor((xz - map.Origin) / map.CellSize);
            // W-153 row 2: each segment once (the cells' lists overlap), and the carve and water blended over them.
            var seen = new FixedList128Bytes<int>();
            var blend = new Join { Nearest = float.MaxValue };
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                var c = math.clamp(cell + new int2(x, y), 0, map.Resolution - 1);
                var candidates = map.Nearest[c.y * map.Resolution + c.x];
                for (int k = 0; k < 2; k++)
                {
                    var start = k == 0 ? candidates.x : candidates.y;
                    if (start < 0 || seen.Contains(start))
                        continue;
                    seen.Add(start);
                    Consider(ref map, s, bank, xz, start, ref q, ref blend);
                }
            }
            if (blend.Weight > 0f)
            {
                q.Carve = blend.Carve / blend.Weight;
                q.Water = blend.Water / blend.Weight;
                // Tighter than the carve: segments' bars differ far more, so a dropped segment would step the bar.
                q.Bar = blend.Bar / blend.BarWeight * barScale;
                // Not within a cell of a lake: its higher surface would stand as a wall over the bar.
                if (q.Bar > 0f)
                {
                    var c = map.CellSize;
                    var lake = math.max(Lake(ref map, xz, out _, out _), math.max(
                        math.max(Lake(ref map, xz + new float2(c, 0f), out _, out _), Lake(ref map, xz - new float2(c, 0f), out _, out _)),
                        math.max(Lake(ref map, xz + new float2(0f, c), out _, out _), Lake(ref map, xz - new float2(0f, c), out _, out _))));
                    q.Bar *= 1f - math.smoothstep(0f, 0.1f, lake);
                }
                q.Isle = blend.Isle / blend.BarWeight * barScale;
                if (q.Isle > 0f)
                {
                    var c = map.CellSize;
                    var lake = math.max(Lake(ref map, xz, out _, out _), math.max(
                        math.max(Lake(ref map, xz + new float2(c, 0f), out _, out _), Lake(ref map, xz - new float2(c, 0f), out _, out _)),
                        math.max(Lake(ref map, xz + new float2(0f, c), out _, out _), Lake(ref map, xz - new float2(0f, c), out _, out _))));
                    q.Isle *= 1f - math.smoothstep(0f, 0.1f, lake);
                    q.Carve = math.lerp(q.Carve, math.max(q.Carve, blend.IsleTop / math.max(blend.Isle, 1e-6f)), q.Isle);
                }
                if (q.Bar * bank.Cut > 0f)
                    q.Carve = PointBar(q.Carve, q.Distance - q.Width * 0.5f, q.Water, q.Bar * bank.Cut);
            }
            // After the blend so neighbours can't lift the pool's edges dry; only under its own water, not a higher river's.
            if (q.PoolWeight > 0f && q.FallWater == float.MinValue)
                q.Carve = math.lerp(q.Carve, math.min(q.Carve, q.PoolFloor),
                    q.PoolWeight * (1f - math.smoothstep(0.3f, 1f, q.Water - q.PoolWater)));
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

        /// <summary>Over this many metres of edge distance, a further profile still shares the carve and water (joins).</summary>
        public const float JoinBlend = 2f;
        const float BarBlend = 0.5f;

        /// <summary>How far past a fall's lip line its upper water still reaches (m): the lip holds the upper water.</summary>
        public const float LipHold = 0.9f;
        public const float FootPoolDepth = 0.2f;  // a fall's foot pool sinks this share of its drop (at most 2.5 m)
        public const float FootPoolMargin = 4f;   // and reaches this far past the channel's edge

        /// <summary>
        /// W-153 row 2: the carve and water of the nearby profiles, weighted by how close each one's edge is (the
        /// nearest weighs 1, one JoinBlend further 1/e). Inside a channel its own profile rules; where two segments
        /// meet (a join, a confluence) the ground and water slide from one to the other instead of jumping to whichever
        /// carves lowest, and a segment's round end no longer digs back under the segment before it.
        /// </summary>
        struct Join
        {
            public float Nearest, Weight, Carve, Water, BarWeight, Bar, Isle, IsleTop;

            public void Add(float edge, float carve, float water, float bar = 0f, float isle = 0f, float isleTop = 0f)
            {
                if (edge < Nearest)
                {
                    // Rescale what is summed so far to the new nearest (log-sum-exp, so far profiles never overflow).
                    var scale = Weight > 0f ? math.exp((edge - Nearest) / JoinBlend) : 0f;
                    Weight *= scale; Carve *= scale; Water *= scale;
                    var barScale = BarWeight > 0f ? math.exp((edge - Nearest) / BarBlend) : 0f;
                    BarWeight *= barScale; Bar *= barScale; Isle *= barScale; IsleTop *= barScale;
                    Nearest = edge;
                }
                var w = math.exp((Nearest - edge) / JoinBlend);
                Weight += w; Carve += w * carve; Water += w * water;
                var wb = math.exp((Nearest - edge) / BarBlend);
                BarWeight += wb; Bar += wb * bar;
                Isle += wb * isle; IsleTop += wb * isle * isleTop;
            }
        }

        static float Wobble(float2 p, float scale, float seed) => math.smoothstep(-0.6f, 0.6f, noise.snoise(p * scale + seed));

        static void Consider(ref RiverMapBlob map, in RiverSettings s, in Bank bank, float2 xz, int start, ref RiverQuery q, ref Join blend)
        {
            if (start < 0)
                return;
            var down = map.Down[start];
            var a = map.Point(start);
            var b = down >= 0 ? map.Point(down) : a;
            var ab = b - a;
            float t, distance;
            var flow = math.normalizesafe(ab);
            var outer = 0f;
            var inner = 0f;
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
                var turn = (ta.x * tb.y - ta.y * tb.x) / math.max(math.length(ta) * math.length(tb), 1e-6f);
                var off = xz - Hermite(a, b, ta, tb, t);
                if (turn * (flow.x * off.y - flow.y * off.x) < 0f)
                    outer = math.saturate(math.abs(turn) / 0.3f);
                else
                    inner = math.saturate(math.abs(turn) / 0.3f);
            }
            else
            {
                t = math.saturate(math.dot(xz - a, ab) / math.max(math.dot(ab, ab), 1e-6f));
                distance = math.distance(xz, a + ab * t);
            }

            // W-153: bed, water and width come from the segment's profile. A waterfall is a step half way along it.
            var profile = map.Profile(start);
            var fall = profile.Falls;
            var tl = profile.Along(t);
            var level = profile.Bed(tl);
            var baseWidth = profile.Width(t);
            var width = baseWidth * bank.WidthScale;
            var notch = 0f;
            if (fall)
            {
                // From just past the sheet's edge the step comes later in blocks, leans and pauses on ledges, never before the lip, so the flanks are not a straight cut.
                var aside = math.smoothstep(2.5f, 4f, distance - math.max(width, baseWidth) * 0.5f);
                if (aside > 0f)
                {
                    var length = math.max(math.distance(a, b), 1f);
                    var jag = aside * (Wobble(xz, 1f / 9f, 41.7f) * 3.5f + math.floor(Wobble(xz, 1f / 3f, 23.3f) * 3.99f) * 1f);
                    var lean = aside * (1.2f + Wobble(xz, 1f / 14f, 7.1f) * 4f);
                    var s0 = 0.505f + jag / length;
                    var s1 = 0.55f + (jag + lean) / length;
                    var mid = math.lerp(s0, s1, math.lerp(0.35f, 0.6f, Wobble(xz, 1f / 11f, 3.3f)));
                    var k = math.lerp(0.3f, 0.7f, Wobble(xz, 1f / 5f, 12.9f));
                    var stepped = k * math.smoothstep(s0, mid, t) + (1f - k) * math.smoothstep(mid + (s1 - s0) * 0.15f, s1, t);
                    var ledge = aside * math.smoothstep(0.3f, 0.6f, Wobble(xz, 1f / 6f, 19.9f));
                    tl = math.lerp(math.smoothstep(s0, s1, t), stepped, ledge);
                    level = profile.Bed(tl);
                    notch = (jag + lean) * aside;
                }
            }
            var cut = fall || baseWidth < 3f ? 0f : math.smoothstep(0.25f, 0.6f, outer) * math.lerp(0.5f, 1.5f, outer);
            var water = profile.WaterAt(t);
            var bar = fall || baseWidth < 3f ? 0f : math.smoothstep(0.25f, 0.6f, inner) * math.saturate((level - 0.1f - water) / 0.3f);
            var carve = Profile(distance, level, width, Depth(baseWidth, s), bank, cut * bank.Cut);

            // Carve and water come from the same profile, so water and banks agree.
            // A fall's water steps on its lip line, straight across the channel at the curve's middle (where FallShape
            // and the curtain put it), so both banks agree at the lip; a little past it so the lip itself holds the upper water.
            // Inside its channel above the lip the fall owns the water outright (a river doubling back beside its own
            // fall would otherwise blend its lower water in across the bank).
            // Below the lip line the bed sinks into a foot pool, deeper than the channel for about a channel width, so
            // the curtain lands in a plunge pool rather than on a dry step; #301: its floor reaches past the banks the sheet's sides land on.
            if (fall && s.Smooth && down >= 0)
            {
                var lip = CurveMid(ref map, start, out var lipFlow);
                var past = math.dot(xz - lip, lipFlow);
                var lipSide = past <= LipHold;
                // The bed starts down just short of the lip line (Along steps on the curve). Beside the sheet the upper
                // water also ends where the bed has begun to fall: it would otherwise stand over the drop as a thin
                // floating wall that no curtain covers (fall 121's sliver). Inside the sheet's width it runs to the lip.
                var above = lipSide && (tl < 0.05f || distance < baseWidth * 0.5f);
                water = above ? profile.WaterA : profile.WaterB;
                if (above && distance < width * 0.38f)
                    q.FallWater = math.max(q.FallWater, profile.WaterA);
                // W-153: beside the sheet the upper water would stand at the lip over the cliff below with no curtain
                // to carry it down (56 walls on v3). Rock shoulders rise just above it there instead, so the sheet is
                // the only way over the lip; they narrow a wider channel to the sheet over its last few metres.
                if (lipSide)
                {
                    var edge = math.min(width, baseWidth) * 0.5f; // the carved channel can be narrower than the sheet
                    var side = math.smoothstep(edge - 1f, edge + 0.5f, distance)
                             * math.smoothstep(LipHold - 10f, LipHold - 4f, past)
                             * (1f - math.smoothstep(width * 0.5f + SurfaceMargin, width * 0.5f + SurfaceMargin + 4f, distance));
                    if (side > q.ShoulderWeight)
                    {
                        q.ShoulderWeight = side;
                        q.ShoulderLevel = profile.WaterA + 0.4f;
                    }
                }
                if (!above)
                {
                    var drop = math.max(profile.BedA - profile.BedB, 0f);
                    var foot = LipHold + math.max(notch, 0f);
                    var sink = math.smoothstep(foot, foot + 2f, past)
                             * (1f - math.smoothstep(LipHold + width * 0.6f, LipHold + width * 1.2f, past));
                    var pit = math.min(drop * FootPoolDepth, 2.5f);
                    carve = Profile(distance, level, width, Depth(baseWidth, s) + pit * sink, bank);
                    var rim = math.min(math.max(width, baseWidth) * 0.5f + FootPoolMargin + 1.5f + Wobble(xz, 1f / 8f, 5.5f) * 3f,
                                        width * 0.5f + SurfaceMargin - 0.5f); // the bowl closes before the water's edge
                    var talus = math.smoothstep(foot - 1f, foot + 1f, past) * (1f - math.smoothstep(foot + 2f, foot + 7f, past))
                              * math.smoothstep(rim, rim + 1.5f, distance) * math.lerp(0.4f, 2f, Wobble(xz, 1f / 3f, 27.1f));
                    var pool = profile.WaterB - (Depth(baseWidth, s) * 0.75f + pit) * (1f - math.smoothstep(rim - 2.5f, rim, distance))
                             + math.max(distance - rim, 0f) * bank.Slope * math.lerp(0.6f, 1.4f, Wobble(xz, 1f / 10f, 61.3f)) + talus;
                    carve = math.lerp(carve, math.min(carve, pool), sink);
                    if (sink > q.PoolWeight)
                    {
                        q.PoolWeight = sink;
                        q.PoolFloor = pool;
                        q.PoolWater = profile.WaterB;
                    }
                }
            }
            var isle = 0f;
            var isleTop = 0f;
            if (!fall && s.Smooth && down >= 0 && baseWidth >= IsleWidth && map.Up[start] >= 0 && outer < 0.3f && inner < 0.3f)
                isle = Isle(ref map, start, xz, t, distance, baseWidth, water, out isleTop);
            blend.Add(distance - width * 0.5f, carve, water, bar, isle, isleTop);
            if (distance - width * 0.5f < q.Distance - q.Width * 0.5f)
            {
                q.Found = true;
                q.Distance = distance;
                q.Level = level;
                // The water claims at least the unwobbled width: a narrowed channel leaves bank inside the claim
                // rather than dry ground below the water just past its edge (an open end on a low floodplain).
                q.Width = math.max(width, baseWidth);
                q.Flow = flow;
                q.Cut = cut;
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
                blend.Add(pd - poolWidth * 0.5f, pc, map.WaterLevel[start]);
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
        static float Profile(float distance, float level, float width, float depth, in Bank bank, float cut = 0f)
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
            if (cut > 0f)
            {
                var e = distance - half;
                var fade = 1f - math.smoothstep(6f, 14f, e);
                var terrace = math.saturate(cut / 0.5f) * fade * math.max(e - 0.7f, 0f) * bank.Slope * 0.7f;
                h += cut * math.smoothstep(0f, 0.7f, e) * fade - terrace;
                h -= 0.25f * cut * math.smoothstep(-1.4f, -0.6f, e) * (1f - math.smoothstep(-0.2f, 0.3f, e));
                if (e >= 0f)
                    h = math.max(h, level + 0.4f + math.min(e, SurfaceMargin + 2f) * bank.BaseSlope);
            }
            return h;
        }

        const float IsleWidth = 9f;

        // Kept wholly inside one segment so it never meets a neighbour's carve.
        static float Isle(ref RiverMapBlob map, int start, float2 xz, float t, float distance, float width, float water, out float top)
        {
            top = 0f;
            var down = map.Down[start];
            if (map.LakeDepth[start] > 0f || map.LakeDepth[down] > 0f || SegmentGradient(ref map, start) > 0.012f)
                return 0f;
            if ((BiomeSampling.Scramble(math.hash(new uint2((uint)start, map.Seed ^ 0x15E1u))) & 1023u) > 70u)
                return 0f;
            var length = math.distance(map.Point(start), map.Point(down));
            var along = (t - 0.5f) * length;
            var half = math.min(length * 0.3f, width * 1.6f) * (along < 0f ? 0.7f : 1.3f);
            if (half < 4f)
                return 0f;
            var r = math.length(new float2(along / half, distance / (width * 0.2f))) * (1f + 0.12f * noise.snoise(xz / 4f + 3.7f));
            top = water + 0.4f - 0.9f * r * r;
            return 1f - math.smoothstep(0.75f, 1f, r);
        }

        public static float IsleMask(in RiverQuery q) => q.Found ? math.smoothstep(0.2f, 0.6f, q.Isle) : 0f;

        public static float BarWidth(float bar) => math.lerp(4f, 10f, bar);

        // Only lowers: raising the bed left spikes where neighbouring segments' carves disagree.
        static float PointBar(float h, float e, float water, float bar)
        {
            var wide = BarWidth(bar);
            var ramp = water + 0.35f + e * (e < 0f ? 0.5f : 0.08f);
            var low = math.lerp(h, math.min(h, ramp), bar * (1f - math.smoothstep(wide, wide + 6f, e)));
            return math.max(low, math.min(h, water + 0.3f));
        }

        public static float BarMask(in RiverQuery q)
        {
            if (!q.Found || q.Bar <= 0f)
                return 0f;
            var e = q.Distance - q.Width * 0.5f;
            var wide = BarWidth(q.Bar);
            return q.Bar * math.smoothstep(-2.5f, -1f, e) * (1f - math.smoothstep(wide - 1f, wide + 3f, e));
        }

        public static float Depth(float width, in RiverSettings s) => s.ChannelDepth + width * 0.04f;

        /// <summary>
        /// Lowers terrain into the nearby river's channel and valley; fades out by MaxCarveDistance. W-174 r3 (#406):
        /// the fade is the shared ground-edit profile (<see cref="GroundEdits"/>): where the valley's rim meets the
        /// hillside wanders with world noise instead of following the river at one fixed distance, and the rim's face is
        /// broken up a little. The channel and its banks (well inside the fade's start) are untouched, so the water's
        /// containment guards in <see cref="Profile"/> still hold.
        /// </summary>
        public static float Carve(in RiverQuery q, in RiverSettings s, float height, float2 xz)
        {
            if (!q.Found)
                return height;
            var carved = SmoothMin(height, q.Carve, 2f);
            var w = GroundEdits.Band(q.Distance, s.MaxCarveDistance * 0.5f, s.MaxCarveDistance * 0.7f, s.MaxCarveDistance,
                GroundEdits.EdgeNoise(xz, 1f / 40f, 17.3f));
            var h = GroundEdits.Apply(xz, height, carved, w, 0.2f);
            // A fall's lip shoulders (W-153): ground beside its sheet rises to just above the upper water.
            if (q.ShoulderWeight > 0f && h < q.ShoulderLevel)
                h = math.lerp(h, q.ShoulderLevel, q.ShoulderWeight);
            return h;
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
