using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>One step of the grand falls (W-29): where the water leaves the lip, which way, how far it drops, and its plunge pool.</summary>
    public struct GrandFallTier
    {
        public float3 Lip;
        public float2 Out;
        public float Drop, Width;
        /// <summary>Plunge pool centre (y = its water level) and radius.</summary>
        public float3 Pool;
        public float PoolRadius;
        /// <summary>Distance along the path of the foot, where the pool starts.</summary>
        public float Along;
    }

    /// <summary>
    /// The grand falls (W-29): one mountain stream on the v2 island that steps down into the central lake in a few small
    /// cascades and one tall main drop, each into its own plunge pool. It is an overlay baked after every place
    /// (landmarks, caves, settlements, icebergs), so it can't move any of them: it only reshapes the ground along its
    /// path (a channel, raised banks or a cut gorge, and round pools) and gives that path its own water. Its water level
    /// is a piecewise-linear staircase along the path, so the surface never breaks: a fall is two nodes a metre or
    /// three apart with the full drop between them.
    /// </summary>
    public struct GrandFallsBlob
    {
        /// <summary>Path nodes: (x, water level, z, channel half width).</summary>
        public BlobArray<float4> Nodes;
        public BlobArray<float> Along;
        public BlobArray<GrandFallTier> Tiers;
        /// <summary>(min x, min z, max x, max z), already grown by the widest bank.</summary>
        public float4 Bounds;
    }

    public static class GrandFalls
    {
        const float Depth = 1.6f, PoolDepth = 4.5f, BankTop = 1.2f;
        public static string LastLog = "";

        /// <summary>
        /// The W-29 site, picked by survey: from a 168 m shoulder north-west of the central lake down to its shore. The
        /// middle node bends the lower river east of the Tors near the shore. A prototype for one seed: Build checks the
        /// ground still climbs ~100 m above the lake and bakes nothing otherwise.
        /// </summary>
        static readonly float2[] Site = { new float2(316f, 905f), new float2(420f, 1180f), new float2(530f, 1400f) };

        /// <summary>
        /// Height after the grand falls' channel, banks and pools, plus its water there (NoWater when outside it).
        /// <paramref name="rapids"/> is white water near the lips and in the pools.
        /// </summary>
        public static float Shape(ref GrandFallsBlob set, float2 xz, float h, out float water, out float2 flow, out float rapids, out float mask)
        {
            water = GroundSample.NoWater; flow = float2.zero; rapids = 0f; mask = 0f;
            if (math.any(xz < set.Bounds.xy) || math.any(xz > set.Bounds.zw))
                return h;
            // Nearest point on the path.
            float best = float.MaxValue, w = 0f, hw = 1f, s = 0f, drop = 0f, len = 1f;
            float2 tangent = 0f;
            for (int i = 0; i + 1 < set.Nodes.Length; i++)
            {
                var a = set.Nodes[i]; var b = set.Nodes[i + 1];
                var ab = b.xz - a.xz; var l2 = math.max(math.lengthsq(ab), 1e-4f);
                var t = math.saturate(math.dot(xz - a.xz, ab) / l2);
                var d = math.distancesq(xz, a.xz + ab * t);
                if (d < best)
                {
                    best = d; w = math.lerp(a.y, b.y, t); hw = math.lerp(a.w, b.w, t);
                    s = math.lerp(set.Along[i], set.Along[i + 1], t);
                    len = math.sqrt(l2); tangent = ab / len; drop = a.y - b.y;
                }
            }
            var dist = math.sqrt(best);
            var lev = Level(dist, hw, w, Depth, h, out var weight);
            var inWater = dist < hw + 0.5f;
            var slope = drop / len;
            rapids = slope > 0.5f ? 1f : math.smoothstep(0.02f, 0.2f, slope);
            var speed = math.min(1.2f + slope * 12f, 4f);
            // Plunge pools: below their foot only, so a pool never eats into the lip above it.
            for (int k = 0; k < set.Tiers.Length; k++)
            {
                ref var tier = ref set.Tiers[k];
                if (s < tier.Along - 0.5f) continue;
                var r = math.distance(xz, tier.Pool.xz);
                if (r > tier.PoolRadius + 40f) continue;
                var poolLev = Level(r, tier.PoolRadius, tier.Pool.y, PoolDepth, h, out var poolWeight);
                if (r < tier.PoolRadius || poolWeight > weight)
                {
                    lev = poolLev; weight = poolWeight; w = tier.Pool.y;
                    inWater = r < tier.PoolRadius + 0.5f;
                    rapids = math.saturate(1.2f - r / tier.PoolRadius) * math.saturate(tier.Drop / 10f);
                    speed = 0.6f;
                }
            }
            mask = weight;
            if (inWater)
            {
                water = w;
                flow = tangent * speed;
            }
            else rapids = 0f;
            return math.lerp(h, lev, weight);
        }

        /// <summary>The levelled height at distance d from a channel (or pool) of half width hw and water level w, and how strongly it replaces the ground.</summary>
        static float Level(float d, float hw, float w, float depth, float h, out float weight)
        {
            // Banks widen with the height they make up, so a deep cut becomes a steep gorge rather than a wall.
            var bank = 8f + 0.4f * math.abs(h - (w + BankTop));
            weight = 1f - math.smoothstep(hw, hw + bank, d);
            return d < hw ? w - depth * (1f - (d / hw) * (d / hw)) : w + BankTop * math.smoothstep(hw, hw + 3f, d);
        }

        /// <summary>Bakes the falls from the ground as it is now (call it before setting <see cref="TerrainSource.GrandFalls"/>).</summary>
        public static BlobAssetReference<GrandFallsBlob> Build(in TerrainSource source, Allocator allocator)
        {
            var src = source;
            // The path, sampled every 4 m, run on past the last site node until it is in the lake.
            var pts = new List<float2>();
            for (int i = 0; i + 1 < Site.Length; i++)
            {
                var n = (int)math.ceil(math.distance(Site[i], Site[i + 1]) / 4f);
                for (int j = 0; j < n; j++) pts.Add(math.lerp(Site[i], Site[i + 1], j / (float)n));
            }
            var dir = math.normalize(Site[Site.Length - 1] - Site[Site.Length - 2]);
            float lake = float.MinValue;
            for (int j = 0; j <= 60; j++)
            {
                var p = Site[Site.Length - 1] + dir * (4f * j);
                pts.Add(p);
                var g = src.SampleGround(p);
                if (g.WaterSurface > g.Height + 0.5f && g.WaterSurface > src.World.SeaLevel + 1f) { lake = g.WaterSurface; break; }
            }
            var top = src.SampleHeight(pts[0]);
            if (lake == float.MinValue || top < lake + 100f)
            {
                LastLog = $"[grand falls] none: lake {(lake == float.MinValue ? "not found" : lake.ToString("F1"))}, top {top:F1}";
                return default;
            }
            int count = pts.Count;
            var along = new float[count];
            var run = new float[count];   // running minimum of the ground, less the channel depth
            float m = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) along[i] = along[i - 1] + math.distance(pts[i - 1], pts[i]);
                m = math.min(m, src.SampleHeight(pts[i]));
                run[i] = math.max(m - 1.8f, lake);
            }
            float At(float s) { int i = 0; while (i + 1 < count && along[i + 1] < s) i++; return i + 1 < count ? math.lerp(run[i], run[i + 1], math.saturate((s - along[i]) / math.max(along[i + 1] - along[i], 1e-3f))) : run[count - 1]; }

            // Main drop: the steepest 100-160 m stretch in the upper half; the lip at its top, the foot at its bottom's level.
            float mainS = 0f, mainDrop = 0f, mainRun = 100f;
            for (float span = 100f; span <= 160f && mainDrop < 60f; span += 20f)
                for (float s = 40f; s < along[count - 1] * 0.55f; s += 4f)
                {
                    var dd = At(s) - At(s + span);
                    if (dd > mainDrop) { mainDrop = dd; mainS = s; mainRun = span; }
                }
            var mainFoot = At(mainS + mainRun);

            // Small cascades above and below it: wherever the ground ahead falls by stepMin, the water steps down to it.
            List<(float s, float from, float to)> falls = null;
            for (float stepMin = 5f; stepMin <= 24f; stepMin += 1f)
            {
                falls = Cascades(At, along[count - 1], mainS, mainFoot, stepMin, out var minor);
                if (minor <= 4) break;
            }

            // Nodes: every 8 m, plus each fall's lip and foot (1.5 m apart, 3 m for the main drop).
            var nodes = new List<float4>(); var alongOut = new List<float>(); var tiers = new List<GrandFallTier>();
            float Water(float s)
            {
                var level = At(0f);
                var last = 0f;
                foreach (var f in falls)
                {
                    if (s < f.s) break;
                    level = f.to; last = f.s;
                }
                // Between falls the water follows the ground's running minimum down (rapids) but never rises.
                return math.max(math.min(level, At(s)), lake);
            }
            float2 PointAt(float s) { int i = 0; while (i + 1 < count && along[i + 1] < s) i++; return i + 1 < count ? math.lerp(pts[i], pts[i + 1], math.saturate((s - along[i]) / math.max(along[i + 1] - along[i], 1e-3f))) : pts[count - 1]; }
            float HalfWidth(float s) => s < mainS ? 6f : math.lerp(9f, 11f, math.saturate((s - mainS) / 200f));
            var marks = new List<(float s, float y)>();
            for (float s = 0f; s < along[count - 1]; s += 8f) marks.Add((s, float.NaN));
            marks.Add((along[count - 1], float.NaN));
            foreach (var f in falls)
            {
                var gap = f.from - f.to > 30f ? 3f : 1.5f;
                marks.RemoveAll(x => x.s > f.s - 2f && x.s < f.s + gap + 2f);
                marks.Add((f.s, f.from));
                marks.Add((f.s + gap, f.to));
            }
            marks.Sort((a, b) => a.s.CompareTo(b.s));
            foreach (var (s, y) in marks)
            {
                var p = PointAt(s);
                var level = float.IsNaN(y) ? Water(s) : y;
                nodes.Add(new float4(p.x, level, p.y, HalfWidth(s)));
                alongOut.Add(s);
            }
            foreach (var f in falls)
            {
                var gap = f.from - f.to > 30f ? 3f : 1.5f;
                var lip = PointAt(f.s);
                var o = math.normalizesafe(PointAt(f.s + gap) - lip);
                var hw = HalfWidth(f.s + gap);
                var radius = hw * (f.from - f.to > 30f ? 2.2f : 1.6f);
                var poolXZ = lip + o * (gap + radius * 0.75f);
                tiers.Add(new GrandFallTier
                {
                    Lip = new float3(lip.x, f.from, lip.y), Out = o, Drop = f.from - f.to, Width = 2f * HalfWidth(f.s - 1f),
                    Pool = new float3(poolXZ.x, f.to, poolXZ.y), PoolRadius = radius, Along = f.s + gap
                });
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<GrandFallsBlob>();
            var nodeArr = builder.Allocate(ref root.Nodes, nodes.Count);
            var alongArr = builder.Allocate(ref root.Along, nodes.Count);
            float2 lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < nodes.Count; i++) { nodeArr[i] = nodes[i]; alongArr[i] = alongOut[i]; lo = math.min(lo, nodes[i].xz); hi = math.max(hi, nodes[i].xz); }
            var tierArr = builder.Allocate(ref root.Tiers, tiers.Count);
            for (int i = 0; i < tiers.Count; i++) tierArr[i] = tiers[i];
            // Banks reach out to 8 m + 0.4 of the cut: the deepest cut is about the main drop.
            var grow = 30f + 8f + 0.4f * (mainDrop + 10f);
            root.Bounds = new float4(lo - grow, hi + grow);
            var blob = builder.CreateBlobAssetReference<GrandFallsBlob>(allocator);
            builder.Dispose();

            var text = new System.Text.StringBuilder();
            text.Append($"[grand falls] {along[count - 1]:F0} m from ({Site[0].x:F0},{Site[0].y:F0}) at {nodes[0].y:F1} m to the lake at {lake:F1} m (total {nodes[0].y - lake:F1} m), {tiers.Count} tiers:");
            foreach (var t in tiers)
                text.Append($" {(t.Drop > 30f ? "MAIN " : "")}{t.Drop:F1} m at ({t.Lip.x:F0},{t.Lip.z:F0}) lip {t.Lip.y:F1} pool r {t.PoolRadius:F0};");
            float steepest = 0f;
            for (int i = 0; i + 1 < nodes.Count; i++)
            {
                var l = alongOut[i + 1] - alongOut[i];
                var sl = (nodes[i].y - nodes[i + 1].y) / math.max(l, 1e-3f);
                if (sl < 0.5f) steepest = math.max(steepest, sl);
            }
            text.Append($" flow 1.2-{math.min(1.2f + steepest * 12f, 4f):F1} m/s on reaches (steepest {steepest * 100f:F1}%), 0.6 in pools");
            LastLog = text.ToString();
            return blob;
        }

        /// <summary>The falls along the path: small ones found greedily above and below the given main drop.</summary>
        static List<(float s, float from, float to)> Cascades(System.Func<float, float> at, float length, float mainS, float mainFoot, float stepMin, out int minor)
        {
            var list = new List<(float, float, float)>();
            minor = 0;
            const float look = 24f;
            var level = at(0f);
            for (float s = 4f; s < length - look; s += 4f)
            {
                if (s >= mainS && s < mainS + 4f)
                {
                    level = math.min(level, at(s));
                    list.Add((s, level, mainFoot));
                    level = mainFoot;
                    continue;
                }
                if (s > mainS - look && s < mainS) { level = math.min(level, at(s)); continue; }
                var ahead = at(s + look);
                if (ahead < level - stepMin)
                {
                    var from = math.min(level, at(s));
                    list.Add((s, from, ahead));
                    minor++;
                    level = ahead;
                }
                else level = math.min(level, math.max(at(s), level - 0.003f * 4f));
            }
            return list;
        }
    }
}
