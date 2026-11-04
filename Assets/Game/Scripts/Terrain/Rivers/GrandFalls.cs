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
        /// <summary>One stretch of the lake shelf's long fall (W-77): no plunge pool of its own, it drops straight into the lake.</summary>
        public bool Shelf;
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

        /// <summary>
        /// The lake shelf (W-77, #88), v3 only: a long terrace of higher water along the central lake's shore that spills
        /// over its whole lip as one continuous fall. Lip points are (x, z, out x, out z), out pointing at the lake;
        /// empty when there is no shelf.
        /// </summary>
        public BlobArray<float4> ShelfLip;
        /// <summary>Shelf water level, the lake's level below it, and the shelf's width behind the lip at its middle.</summary>
        public float ShelfLevel, ShelfLake, ShelfWidth;
        public float4 ShelfBounds;
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
            if (set.ShelfLip.Length > 1 && math.all(xz >= set.ShelfBounds.xy) && math.all(xz <= set.ShelfBounds.zw))
                return ShapeShelf(ref set, xz, h, out water, out flow, out rapids, out mask);
            if (set.Nodes.Length < 2 || math.any(xz < set.Bounds.xy) || math.any(xz > set.Bounds.zw))
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
                if (tier.Shelf || s < tier.Along - 0.5f) continue;
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

        /// <summary>Half a scored shore stretch in 2.5 degree rays (W-81: about 30 degrees either side), and the shelf's width.</summary>
        const int ShelfHalfSteps = 12;
        /// <summary>W-84: half a scored shore stretch as shore length (m); the ray count follows the shore's distance.</summary>
        const float ShelfArc = 160f;
        /// <summary>W-84: the least land behind every ray of a stretch (m over the lake), the shelf's own lowest rise.</summary>
        const float ShelfMinBehind = 10f;

        /// <summary>W-81 (#88): the lake shelf is tabled behind this flag, off by default (seed-derived either way: every
        /// player must launch with the same setting). Turn it on with <c>-lakeshelf</c> or PUNCHY_LAKESHELF=1.
        /// W-83: read through DebugControls.LaunchArgs, so in the editor Temp/PlayArgs.txt turns it on too.</summary>
        public static bool LakeShelfOn() =>
            System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-lakeshelf") >= 0
            || System.Environment.GetEnvironmentVariable("PUNCHY_LAKESHELF") == "1";
        const float ShelfDepth = 50f;

        /// <summary>W-91 (#88): the palette id with the most weight in a blend.</summary>
        static int Dominant(in BiomeWeights w)
        {
            int id = w.Ids.x; float best = w.Weights.x;
            if (w.Weights.y > best) { best = w.Weights.y; id = w.Ids.y; }
            if (w.Weights.z > best) { best = w.Weights.z; id = w.Ids.z; }
            if (w.Weights.w > best) { id = w.Ids.w; }
            return id;
        }

        /// <summary>
        /// Bakes the lake shelf (W-77, #88): rays from the lake centre across the shelf's arc find the shore, smoothed
        /// into one lip line; the long fall is a tier every 14 m along it.
        /// </summary>
        static BlobAssetReference<GrandFallsBlob> BuildShelf(in TerrainSource source, Allocator allocator, IReadOnlyList<BiomeDefinition> biomes)
        {
            var src = source;
            var c = IslandV3.LakeCentre;
            var g0 = src.SampleGround(c);
            // W-77b (#88): the v3 central lake sits at sea level, so only ask for open water over the bed here.
            if (!g0.HasWater || g0.WaterSurface < g0.Height + 0.5f)
            {
                LastLog += "; shelf none: no lake at the centre";
                return default;
            }
            var lake = g0.WaterSurface;
            // W-81 (#88): rays all round the lake find the shore and the ground behind it. Each stretch is scored by how
            // far its middle sits back from the chord between its ends (a bay) and by the height of the land behind it
            // (the mountain side), with a small seeded nudge; the shelf fills the best bay's back.
            const int rays = 144; // every 2.5 degrees
            var shoreR = new float[rays]; var behindH = new float[rays]; var rayDir = new float2[rays];
            var nearB = new int[rays]; var farB = new int[rays]; var riseH = new float[rays];
            int open = 0;
            for (int k = 0; k < rays; k++)
            {
                var ang = math.radians(k * 360f / rays);
                var d = new float2(math.cos(ang), math.sin(ang));
                rayDir[k] = d; shoreR[k] = -1f;
                for (int j = 1; j <= 500; j++)
                {
                    var g = src.SampleGround(c + d * (4f * j));
                    if (!g.HasWater || g.WaterSurface < g.Height + 0.3f || math.abs(g.WaterSurface - lake) > 1f) { shoreR[k] = 4f * j; break; }
                }
                if (shoreR[k] < 0f) { open++; continue; }
                // The land behind: mean ground height 30-90 m past the shore.
                float sum = 0f;
                for (int j = 0; j < 4; j++) sum += src.SampleGround(c + d * (shoreR[k] + 30f + 20f * j)).Height;
                behindH[k] = sum * 0.25f - lake;
                // W-91 (#88): the biomes 100 m and 200 m behind the shore, and how much the land keeps rising out to 300 m.
                nearB[k] = Dominant(src.SampleGround(c + d * (shoreR[k] + 100f)).Biomes);
                farB[k] = Dominant(src.SampleGround(c + d * (shoreR[k] + 200f)).Biomes);
                riseH[k] = src.SampleGround(c + d * (shoreR[k] + 300f)).Height - src.SampleGround(c + d * (shoreR[k] + 60f)).Height;
            }
            // W-91 (#88): Zach wants the shelf to come down from the mountains towards the temperate land, not out of the
            // desert. Each ray's land score: temperate or grassland behind it (forest half), desert a penalty, plus the
            // land rising towards the peaks. It outweighs the bay shape, so a plainer bay on the right side wins.
            string NameOf(int id) => biomes != null && id >= 0 && id < biomes.Count && biomes[id] != null ? biomes[id].name : "?";
            float LandOf(int id)
            {
                var n = NameOf(id);
                if (n.StartsWith("Temperate") || n.StartsWith("Grassland")) return 1f;
                if (n.StartsWith("Mountains")) return 0.6f;
                if (n.Contains("Forest") && !n.StartsWith("Ember")) return 0.5f;
                foreach (var arid in new[] { "Dunes", "SaltFlat", "Sonoran", "GlassDesert", "MesaCanyonlands" })
                    if (n.StartsWith(arid)) return -1f;
                return 0f;
            }
            // W-84 (#88): a stretch spans about the same shore length on any lake (ShelfArc either side, 4-12 rays), not a
            // fixed 30 degrees: on the big v3 lake (shore ~750 m out) 30 degrees is ~400 m of shore, and no stretch that
            // long has 12 m of high ground behind all of it.
            System.Func<int, int> halfAt = k => math.clamp((int)math.round(math.degrees(ShelfArc / math.max(shoreR[k], 1f)) / 2.5f), 4, ShelfHalfSteps);
            var rng = new Unity.Mathematics.Random(math.max(1u, source.World.Seed * 747796405u + 2891336453u));
            int bestK = -1, bestHalf = 0; float bestScore = float.MinValue, bestBay = 0f, bestBehind = 0f, bestLand = 0f;
            int oldK = -1; float oldScore = float.MinValue;
            for (int k = 0; k < rays; k++)
            {
                var nudge = rng.NextFloat(0f, 0.08f);
                bool ok = shoreR[k] >= 0f; float minBehind = float.MaxValue;
                var half = ok ? halfAt(k) : 0;
                for (int s = -half; s <= half && ok; s++)
                {
                    var kk = (k + s + rays) % rays;
                    if (shoreR[kk] < 0f) ok = false; else minBehind = math.min(minBehind, behindH[kk]);
                }
                // Only real high ground behind the whole stretch: a low saddle or an inlet to other water is no bay for it.
                if (!ok || minBehind < ShelfMinBehind) continue;
                int ka = (k - half + rays) % rays, kb = (k + half) % rays;
                var a = c + rayDir[ka] * shoreR[ka]; var b = c + rayDir[kb] * shoreR[kb]; var m = c + rayDir[k] * shoreR[k];
                // How far the middle sits behind the chord, away from the lake centre (positive = a bay).
                var chordN = math.normalizesafe(new float2(b.y - a.y, a.x - b.x));
                if (math.dot(chordN, m - c) < 0f) chordN = -chordN;
                var bay = math.dot(m - a, chordN);
                var chord = math.max(math.distance(a, b), 1f);
                var score = math.clamp(bay / chord, -0.4f, 0.4f) * 1.5f + math.saturate(minBehind / 80f) * 1.5f + nudge;
                if (score > oldScore) { oldScore = score; oldK = k; }
                float land = 0f;
                for (int s = -half; s <= half; s++)
                {
                    var kk = (k + s + rays) % rays;
                    land += 0.6f * LandOf(nearB[kk]) + 0.4f * LandOf(farB[kk]) + 0.5f * math.saturate(riseH[kk] / 60f);
                }
                land /= 2 * half + 1;
                score += 2.5f * land;
                if (score > bestScore) { bestScore = score; bestK = k; bestBay = bay; bestBehind = minBehind; bestHalf = half; bestLand = land; }
            }
            if (bestK < 0)
            {
                // W-84 (#88): say how close the best stretch came, so a seed with no bay can be read from the log.
                int high = 0, topK = 0; float topMin = float.MinValue;
                for (int k = 0; k < rays; k++)
                {
                    if (shoreR[k] >= 0f && behindH[k] >= ShelfMinBehind) high++;
                    float mn = float.MaxValue; var half = shoreR[k] < 0f ? 0 : halfAt(k);
                    for (int s = -half; s <= half; s++) { var kk = (k + s + rays) % rays; mn = math.min(mn, shoreR[kk] < 0f ? -999f : behindH[kk]); }
                    if (mn > topMin) { topMin = mn; topK = k; }
                }
                LastLog += $"; shelf none: no closed shore stretch ({open} open rays, {high} rays with >= {ShelfMinBehind:F0} m behind, best stretch min {topMin:F1} m at {topK * 360f / rays:F0} deg, shore {shoreR[topK]:F0} m)";
                return default;
            }
            // Flush with the land behind: the shelf takes the lowest of the stretch's ground behind, kept between 10 and 40 m.
            var rise = math.clamp(bestBehind, 10f, 40f);
            var level = lake + rise;
            // The lip: the bay's back (its middle two thirds), shore smoothed once so the joints follow its curve.
            var radii = new List<float>(); var dirs = new List<float2>();
            int lipHalf = bestHalf * 2 / 3;
            for (int s = -lipHalf; s <= lipHalf; s++) { var kk = (bestK + s + rays) % rays; radii.Add(shoreR[kk]); dirs.Add(rayDir[kk]); }
            {
                var sm = new List<float>(radii);
                for (int i = 1; i + 1 < radii.Count; i++) sm[i] = 0.25f * radii[i - 1] + 0.5f * radii[i] + 0.25f * radii[i + 1];
                radii = sm;
            }
            var pts = new List<float2>();
            for (int i = 0; i < radii.Count; i++) pts.Add(c + dirs[i] * radii[i]);
            // Each lip point faces out along the edge's own normal (the mean of its two segments'), towards the lake.
            var lip = new List<float4>();
            for (int i = 0; i < pts.Count; i++)
            {
                var tan = math.normalizesafe(pts[math.min(i + 1, pts.Count - 1)] - pts[math.max(i - 1, 0)]);
                var o = new float2(tan.y, -tan.x);
                if (math.dot(o, c - pts[i]) < 0f) o = -o;
                lip.Add(new float4(pts[i], o));
            }
            // The falls: one sheet per chord of the lip resampled every ~10 m, each facing its own chord's normal and
            // exactly as wide as it, so neighbouring sheets meet at the joints with no gap or overlap at the lip.
            var tiers = new List<GrandFallTier>();
            float length = 0f;
            var cum = new List<float> { 0f };
            for (int i = 0; i + 1 < lip.Count; i++) { length += math.distance(lip[i].xy, lip[i + 1].xy); cum.Add(length); }
            int sheets = math.max(1, (int)math.round(length / 10f));
            float2 At(float s)
            {
                int i = 0;
                while (i + 2 < cum.Count && cum[i + 1] < s) i++;
                var t = math.saturate((s - cum[i]) / math.max(cum[i + 1] - cum[i], 1e-3f));
                return math.lerp(lip[i].xy, lip[i + 1].xy, t);
            }
            for (int q = 0; q < sheets; q++)
            {
                var p0 = At(length * q / sheets); var p1 = At(length * (q + 1) / sheets);
                var p = 0.5f * (p0 + p1);
                var tan = math.normalizesafe(p1 - p0);
                var o = new float2(tan.y, -tan.x);
                if (math.dot(o, c - p) < 0f) o = -o;
                tiers.Add(new GrandFallTier
                {
                    Lip = new float3(p.x, level, p.y), Out = o, Drop = rise, Width = math.distance(p0, p1) + 0.2f,
                    Pool = new float3(p.x + o.x * 8f, lake, p.y + o.y * 8f), PoolRadius = 8f, Along = length * (q + 0.5f) / sheets, Shelf = true
                });
            }
            var bearing = (bestK * 360f / rays);
            // W-91 (#88): the biomes behind the old pick (bay and height only) and the new one.
            LastLog += $"; shelf biomes before (bay-only pick) at bearing {oldK * 360f / rays:F0}: {NameOf(nearB[oldK])} 100 m behind, {NameOf(farB[oldK])} 200 m behind, rise {riseH[oldK]:F0} m";
            LastLog += $"; after at bearing {bearing:F0}: {NameOf(nearB[bestK])} 100 m behind, {NameOf(farB[bestK])} 200 m behind, rise {riseH[bestK]:F0} m, land score {bestLand:F2}";
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<GrandFallsBlob>();
            var lipArr = builder.Allocate(ref root.ShelfLip, lip.Count);
            float2 lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < lip.Count; i++) { lipArr[i] = lip[i]; lo = math.min(lo, lip[i].xy); hi = math.max(hi, lip[i].xy); }
            var tierArr = builder.Allocate(ref root.Tiers, tiers.Count);
            for (int i = 0; i < tiers.Count; i++) tierArr[i] = tiers[i];
            root.ShelfLevel = level; root.ShelfLake = lake; root.ShelfWidth = ShelfDepth;
            var grow = ShelfDepth + 6f + 10f + 0.5f * 60f;
            root.ShelfBounds = new float4(lo - grow, hi + grow);
            root.Bounds = root.ShelfBounds;
            var blob = builder.CreateBlobAssetReference<GrandFallsBlob>(allocator);
            builder.Dispose();
            LastLog += $"; lake shelf bay at bearing {bearing:F0} (score {bestScore:F2}, bay {bestBay:F0} m behind its chord, land behind {bestBehind:F0} m over the lake)";
            LastLog += $"; lake shelf at {level:F1} m over the lake at {lake:F1} m: lip {length:F0} m from ({lip[0].x:F0},{lip[0].y:F0}) to ({lip[lip.Count - 1].x:F0},{lip[lip.Count - 1].y:F0}), {tiers.Count} fall stretches";
            return blob;
        }

        /// <summary>
        /// The lake shelf: behind the lip a flat basin held at the shelf level (its bed carved or raised to it), a rim
        /// beyond that which only ever raises the ground, and at the lip a sheer face down into the lake. The basin is
        /// widest in the middle and closes at both ends, so the shelf reads as one long terrace.
        /// </summary>
        static float ShapeShelf(ref GrandFallsBlob set, float2 xz, float h, out float water, out float2 flow, out float rapids, out float mask)
        {
            water = GroundSample.NoWater; flow = float2.zero; rapids = 0f; mask = 0f;
            int n = set.ShelfLip.Length;
            float best = float.MaxValue, u = 0f;
            float2 near = 0f, outDir = 0f;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = set.ShelfLip[i]; var b = set.ShelfLip[i + 1];
                var ab = b.xy - a.xy; var l2 = math.max(math.lengthsq(ab), 1e-4f);
                var t = math.saturate(math.dot(xz - a.xy, ab) / l2);
                var q = a.xy + ab * t;
                var d = math.distancesq(xz, q);
                if (d < best) { best = d; near = q; u = (i + t) / (n - 1); outDir = math.normalizesafe(math.lerp(a.zw, b.zw, t)); }
            }
            var off = xz - near;
            var front = math.dot(off, outDir);            // metres towards the lake from the lip
            var lateral = math.length(off - outDir * front); // past either end of the lip
            var level = set.ShelfLevel;
            // Half an ellipse: full width in the middle, closing to nothing at the ends.
            var c = 2f * u - 1f;
            var width = set.ShelfWidth * math.sqrt(math.max(0f, 1f - c * c)) + 6f;
            var behind = -front;
            if (front > 0f && lateral >= 1f)
                return h; // the lake beside the ends stays as it is
            if (front > 0f)
            {
                // The face: the lip's edge drops sheer into the lake within a few metres.
                var k = math.smoothstep(0f, 3f, front);
                mask = 1f - k;
                return math.min(h, math.lerp(level - 0.3f, h, k));
            }
            // Distance out of the basin (landwards past its width, or sideways past an end).
            var outside = math.max(behind - width, lateral);
            if (outside <= 0f)
            {
                // The basin: bed 2.5 m under the shelf level, rising to the lip and to the back wall.
                var bed = level - 2.5f * math.saturate(behind / 5f) * math.saturate((width - behind) / 6f);
                mask = 1f;
                water = level;
                var toLip = math.saturate(1f - behind / math.max(width, 1f));
                flow = outDir * (0.3f + 1.7f * toLip * toLip);
                rapids = math.saturate(1f - behind / 6f);
                return bed;
            }
            // The rim: at least 1.5 m above the water, easing back into the ground; never lowers a hill behind it.
            var rim = level + 1.5f * math.smoothstep(0f, 3f, outside);
            var bank = 10f + 0.5f * math.abs(h - rim);
            var weight = 1f - math.smoothstep(0f, bank, outside);
            mask = weight * 0.5f;
            return math.lerp(h, math.max(h, rim), weight);
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
        public static BlobAssetReference<GrandFallsBlob> Build(in TerrainSource source, Allocator allocator, bool shelf = false, IReadOnlyList<BiomeDefinition> biomes = null)
        {
            // W-83 (#88): with the shelf on, the shelf and its falls are the lake's falls. Before, it was built only
            // when the grand falls path found no lake, so wherever that path did find one the flag did nothing.
            if (shelf)
            {
                LastLog = "[grand falls] lake shelf on";
                return BuildShelf(source, allocator, biomes);
            }
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
