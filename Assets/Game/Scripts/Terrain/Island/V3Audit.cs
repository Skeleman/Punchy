using System.Collections.Generic;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    /// <summary>
    /// The water audit (v3 brief section 7, card B0): one "[hydro audit]" line of counts over a finished island, read
    /// only through <see cref="TerrainSource"/>. It runs at the end of every v3 bake, and on any island from the editor
    /// (eval <c>Punchy.Terrain.V3Audit.Run(source)</c>) so v2 has a baseline. It never changes the island.
    /// Counts: rivers (sources), river ends and open ends (not on a lake, a fall pool or the sea), lakes (4-connected
    /// lake cells), falls, landmark pools and dry pools (under 0.2 m deep), ponds, dry beds, and on a grid: water cells
    /// on steep ground, water over ground (a surface higher than a dry neighbour's ground), land between surfaces (a
    /// dry strip with water above it on both sides) and floating water (a wet cell with dry ground over 1 m below it on
    /// every side). Grid hits within 25 m of a fall lip are counted apart (curtains are allowed some).
    /// </summary>
    public static class V3Audit
    {
        /// <summary>A water surface must stand this far over a neighbour's dry ground to count as spilling.</summary>
        const float Spill = 0.3f;
        const float SteepSlope = 0.7f;      // rise over run of the ground under water (35 degrees)
        const float FallRadius = 25f;

        public static string LastLog = "";

        /// <summary>Audits the island; step is the grid spacing (m) for the cell counts. Returns and keeps the log line.</summary>
        public static string Run(in TerrainSource source, float step = 8f)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var s = source;
            float sea = s.World.SeaLevel;
            var lips = new List<float2>();
            int falls = 0, grandTiers = 0;
            if (s.GrandFalls.IsCreated)
            {
                ref var g = ref s.GrandFalls.Value;
                grandTiers = g.Tiers.Length;
                for (int i = 0; i < g.Tiers.Length; i++) lips.Add(g.Tiers[i].Lip.xz);
            }

            // ---- rivers on the river grid ----
            int sources = 0, ends = 0, openEnds = 0, lakes = 0, lakeCells = 0;
            var openAt = new List<float2>();
            if (s.Rivers.IsCreated)
            {
                ref var m = ref s.Rivers.Value;
                falls = m.Falls.Length;
                for (int i = 0; i < m.Falls.Length; i++) lips.Add(m.Falls[i].Lip.xz);
                int n = m.Width.Length;
                for (int i = 0; i < n; i++)
                {
                    if (m.Width[i] <= 0f) continue;
                    if (m.Up[i] < 0) sources++;
                    int d = m.Down[i];
                    if (d >= 0 && m.Width[d] > 0f) continue;
                    ends++;
                    bool lake = m.LakeDepth[i] > 0f || (d >= 0 && m.LakeDepth[d] > 0f);
                    var p = d >= 0 ? m.Point(d) : m.Point(i);
                    bool atSea = s.SampleHeight(p) <= sea + 0.5f || m.Bed[i] <= sea + 0.5f;
                    bool pool = false;
                    if (!lake && !atSea)
                    {
                        // A river may also end in another body (a fall's plunge pool, a landmark pool): water under its mouth.
                        var gs = s.SampleGround(p);
                        pool = gs.HasWater && gs.WaterSurface > gs.Height + 0.2f && gs.River < 0.5f;
                    }
                    if (!lake && !atSea && !pool) { openEnds++; if (openAt.Count < 6) openAt.Add(p); }
                }
                // Lakes: 4-connected components of lake cells.
                int res = m.Resolution;
                var seen = new bool[n];
                var stack = new Stack<int>();
                for (int i = 0; i < n; i++)
                {
                    if (seen[i] || m.LakeDepth[i] <= 0f) continue;
                    lakes++;
                    seen[i] = true; stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int c = stack.Pop(); lakeCells++;
                        int cx = c % res, cz = c / res;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || nz < 0 || nx >= res || nz >= res) continue;
                            int q = nz * res + nx;
                            if (seen[q] || m.LakeDepth[q] <= 0f) continue;
                            seen[q] = true; stack.Push(q);
                        }
                    }
                }
            }

            // ---- landmark pools (and the grand falls' plunge pools): dry where any probe point is under 0.2 m deep ----
            int pools = 0, dryPools = 0;
            var dryAt = new List<string>();
            if (s.Landmarks.IsCreated)
            {
                ref var set = ref s.Landmarks.Value;
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    ref var type = ref set.Types[set.Instances[i].Type];
                    for (int k = 0; k < type.PoolCount; k++)
                    {
                        LandmarkSampling.Pool(ref set, i, k, out var c, out var r);
                        pools++;
                        if (DryPool(s, c, r)) { dryPools++; if (dryAt.Count < 6) dryAt.Add($"({c.x:F0},{c.z:F0})"); }
                    }
                }
            }
            if (s.GrandFalls.IsCreated)
            {
                ref var g = ref s.GrandFalls.Value;
                for (int i = 0; i < g.Tiers.Length; i++)
                {
                    pools++;
                    if (!g.Tiers[i].Shelf && DryPool(s, g.Tiers[i].Pool, g.Tiers[i].PoolRadius)) { dryPools++; if (dryAt.Count < 6) dryAt.Add($"grand({g.Tiers[i].Pool.x:F0},{g.Tiers[i].Pool.z:F0})"); }
                }
            }
            int ponds = s.PondCount(16f, out _);

            // ---- the grid: ground and water surface (NaN = dry) every step metres, rows in parallel ----
            int size = (int)(s.World.WorldSize / step);
            float half = s.World.WorldSize * 0.5f;
            var ground = new float[size * size];
            var water = new float[size * size];
            System.Threading.Tasks.Parallel.For(0, size, z =>
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new float2(-half + (x + 0.5f) * step, -half + (z + 0.5f) * step);
                    var gs = s.SampleGround(p);
                    int i = z * size + x;
                    ground[i] = gs.Height;
                    float w = gs.HasWater && gs.WaterSurface > gs.Height + 0.05f ? gs.WaterSurface : float.NaN;
                    if (gs.Height < sea) w = float.IsNaN(w) ? sea : math.max(w, sea);
                    water[i] = w;
                }
            });
            int wet = 0, steep = 0, over = 0, between = 0, floating = 0, nearFall = 0;
            var overAt = new List<float2>(); var betweenAt = new List<float2>(); var floatAt = new List<float2>();
            float2 P(int x, int z) => new float2(-half + (x + 0.5f) * step, -half + (z + 0.5f) * step);
            bool NearFall(float2 p) { foreach (var l in lips) if (math.distancesq(l, p) < FallRadius * FallRadius) return true; return false; }
            for (int z = 1; z < size - 1; z++)
                for (int x = 1; x < size - 1; x++)
                {
                    int i = z * size + x;
                    float w = water[i], h = ground[i];
                    int e = i + 1, wv = i - 1, nn = i + size, so = i - size;
                    if (float.IsNaN(w))
                    {
                        // Land between surfaces: water above this dry ground on both sides along x or z.
                        bool bx = !float.IsNaN(water[e]) && !float.IsNaN(water[wv]) && water[e] > h + Spill && water[wv] > h + Spill;
                        bool bz = !float.IsNaN(water[nn]) && !float.IsNaN(water[so]) && water[nn] > h + Spill && water[so] > h + Spill;
                        if (bx || bz)
                        {
                            var p = P(x, z);
                            if (NearFall(p)) nearFall++;
                            else { between++; if (betweenAt.Count < 6) betweenAt.Add(p); }
                        }
                        continue;
                    }
                    if (h < sea) continue;   // open sea
                    wet++;
                    float slope = math.max(math.abs(ground[e] - ground[wv]), math.abs(ground[nn] - ground[so])) / (2f * step);
                    if (slope > SteepSlope && w - h > 0.3f) steep++;
                    int dryLow = 0, dry = 0;
                    foreach (var j in new[] { e, wv, nn, so })
                    {
                        if (!float.IsNaN(water[j])) continue;
                        dry++;
                        if (w > ground[j] + 1f) dryLow++;
                    }
                    bool spills = false;
                    foreach (var j in new[] { e, wv, nn, so })
                        if (float.IsNaN(water[j]) && w > ground[j] + Spill) spills = true;
                    if (!spills) continue;
                    var at = P(x, z);
                    if (NearFall(at)) { nearFall++; continue; }
                    over++; if (overAt.Count < 6) overAt.Add(at);
                    if (dry == 4 && dryLow == 4) { floating++; if (floatAt.Count < 6) floatAt.Add(at); }
                }

            string Spots(List<float2> l) { var sb = new System.Text.StringBuilder(); foreach (var p in l) sb.Append($" ({p.x:F0},{p.y:F0})"); return sb.ToString(); }
            LastLog = $"[hydro audit] rivers {sources} (ends {ends}, open {openEnds}), lakes {lakes} ({lakeCells} cells), falls {falls} + grand {grandTiers}, " +
                      $"pools {pools} (dry {dryPools}), ponds {ponds}, dry beds 0 | grid {step:F0} m: wet {wet}, steep {steep}, " +
                      $"water over ground {over}, land between {between}, floating {floating}, near falls {nearFall} | " +
                      $"open ends at{Spots(openAt)}; dry pools at {string.Join(" ", dryAt)}; over at{Spots(overAt)}; between at{Spots(betweenAt)}; floating at{Spots(floatAt)} | {watch.ElapsedMilliseconds} ms";
            return LastLog;
        }

        /// <summary>True where the pool's centre or any of 8 points at 0.6 radius holds under 0.2 m of water.</summary>
        static bool DryPool(in TerrainSource s, float3 c, float r)
        {
            for (int k = 0; k <= 8; k++)
            {
                float a = k * math.PI / 4f, d = k == 0 ? 0f : r * 0.6f;
                var p = c.xz + new float2(math.cos(a), math.sin(a)) * d;
                var g = s.SampleGround(p);
                float depth = (g.HasWater ? g.WaterSurface : c.y) - g.Height;
                if (depth < 0.2f) return true;
            }
            return false;
        }
    }
}
