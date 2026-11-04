using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using V2 = Punchy.Terrain.IslandV2;

namespace Punchy.Terrain
{
    /// <summary>
    /// v3 step 1 (brief v3-island-design.md section 1, card B2): mountain ranges as terrain objects, drawn before any
    /// biome. Each realm border is traced as a polyline and given a plan: open (no range), a river border (no range,
    /// kept for the river step), a short range or a full range over part of its length. A range is a spine whose crest
    /// tapers to nothing at both ends (width with it), with ridged peaks along its length, 1-3 passes (saddles at
    /// 20-40% of the crest), and 2-6 spurs whose tapered ends are the foothills. One wide massif sits on the longest
    /// mountain range. The result is a range height field (1024 a side, 9 m cells) that only v3 ground height reads.
    /// Spines, passes and border plans stay in the static lists below for roads, scoring and the checks.
    /// </summary>
    public static class V3Ranges
    {
        public const int Res = 1024;
        const float W = V2.WorldSizeM, Half = W * 0.5f;
        const float BinPx = 5f;            // border points binned every 5 design px (~45 m) along the border
        const float WallSlope = 1f;        // 45 degrees: the brief's bare-rock limit, counted as a wall above it

        public enum Plan : byte { Open, River, Short, Range }

        public class Spine
        {
            public Vector2[] Nodes; public float[] Along; public float Length;
            public float Crest, HalfWidth, TaperIn, TaperOut;
            public byte Kind; public Vector2Int Realms; public int Parent = -1;
            public readonly List<RangePass> Passes = new List<RangePass>();

            public Spine(List<Vector2> nodes)
            {
                Nodes = nodes.ToArray(); Along = new float[Nodes.Length];
                for (int i = 1; i < Nodes.Length; i++) Along[i] = Along[i - 1] + Vector2.Distance(Nodes[i - 1], Nodes[i]);
                Length = Along[Nodes.Length - 1];
            }

            public Vector2 PointAt(float s, out Vector2 tangent)
            {
                int k = 0;
                while (k < Nodes.Length - 2 && Along[k + 1] < s) k++;
                float seg = Mathf.Max(Along[k + 1] - Along[k], 1e-3f);
                tangent = (Nodes[k + 1] - Nodes[k]).normalized;
                return Vector2.Lerp(Nodes[k], Nodes[k + 1], Mathf.Clamp01((s - Along[k]) / seg));
            }
        }

        public struct RangePass { public int Spine; public float Along, Depth, Width; public Vector2 At, Across; }
        public class Border { public int A, B; public Plan Plan; public float Length; public int Spine = -1; public string Name; }

        public static readonly List<Spine> Spines = new List<Spine>();
        public static readonly List<RangePass> Passes = new List<RangePass>();
        public static readonly List<Border> Borders = new List<Border>();
        public static Vector2 MassifAt; public static float MassifRadius, MassifHeight;
        public static string LastLog = "";
        static uint seed;

        static Vector2 ToWorld(float px, float py) => new Vector2(px / V2.PX * W - Half, py / V2.PX * W - Half);
        static Vector2 ToPx(Vector2 w) => (w + new Vector2(Half, Half)) / W * V2.PX;
        static float CoastAt(Vector2 w, float wobble) { var p = ToPx(w); return V2.Coast(seed, p.x, p.y, wobble); }

        static float PolyLength(List<Vector2> p) { float l = 0; for (int i = 1; i < p.Count; i++) l += Vector2.Distance(p[i - 1], p[i]); return l; }

        /// <summary>Builds the spines and returns the range height field (Res x Res, metres, row 0 south), faded at the coast.</summary>
        internal static float[] Build(Vector2[] sites, V2.Family[] fam, uint layoutSeed, float coastWobble, Vector2 lakeWorld, float lakeRadiusM)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            seed = layoutSeed;
            Spines.Clear(); Passes.Clear(); Borders.Clear(); MassifRadius = 0f;

            // ---- 1. border points on a coarse grid, ordered along each warped-space Voronoi edge ----
            const int G = 384; float cellPx = V2.PX / G;
            var pts = new SortedDictionary<int, List<(float t, Vector2 w)>>();
            for (int y = 0; y < G; y++)
                for (int x = 0; x < G; x++)
                {
                    float px = (x + 0.5f) * cellPx, py = (y + 0.5f) * cellPx;
                    if (V2.Coast(seed, px, py, coastWobble) < 0.04f) continue;
                    var wp = V2.Warped(seed, px, py);
                    V2.Nearest(sites, wp, out int a, out int b, out float d1, out float d2);
                    if (d2 - d1 > 3f) continue;
                    int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                    var dir = (sites[hi] - sites[lo]).normalized;
                    float t = Vector2.Dot(wp - sites[lo] * V2.PX, new Vector2(-dir.y, dir.x));
                    if (!pts.TryGetValue(lo * 8 + hi, out var list)) pts[lo * 8 + hi] = list = new List<(float, Vector2)>();
                    list.Add((t, ToWorld(px, py)));
                }

            // ---- 2. one polyline per border: bin by t, split at gaps (sea) and folds, keep the longest piece ----
            var lines = new List<(Border border, List<Vector2> line)>();
            foreach (var kv in pts)
            {
                var list = kv.Value; list.Sort((p, q) => p.t.CompareTo(q.t));
                var pieces = new List<List<Vector2>>(); var cur = new List<Vector2>();
                int bin = int.MinValue, prevBin = int.MinValue; Vector2 sum = Vector2.zero; int n = 0;
                void Flush()
                {
                    if (n == 0) return;
                    var c = sum / n;
                    if (cur.Count > 0 && (bin - prevBin > 4 || Vector2.Distance(c, cur[cur.Count - 1]) > 200f)) { pieces.Add(cur); cur = new List<Vector2>(); }
                    cur.Add(c); prevBin = bin;
                }
                foreach (var (t, w) in list)
                {
                    int bi = Mathf.FloorToInt(t / BinPx);
                    if (bi != bin) { Flush(); bin = bi; sum = Vector2.zero; n = 0; }
                    sum += w; n++;
                }
                Flush(); pieces.Add(cur);
                List<Vector2> best = null; float bestLen = 0f;
                foreach (var p in pieces) { float l = PolyLength(p); if (l > bestLen) { bestLen = l; best = p; } }
                if (best == null || best.Count < 4 || bestLen < 500f) continue;
                for (int pass = 0; pass < 3; pass++)
                {
                    var sm = new List<Vector2>(best);
                    for (int i = 1; i < best.Count - 1; i++) sm[i] = best[i - 1] * 0.25f + best[i] * 0.5f + best[i + 1] * 0.25f;
                    best = sm;
                }
                int lo = kv.Key / 8, hi = kv.Key % 8;
                var border = new Border { A = lo, B = hi, Length = PolyLength(best), Name = $"{fam[lo]}-{fam[hi]}" };
                Borders.Add(border); lines.Add((border, best));
            }

            // ---- 3. plans (Zach 11-03: some borders open or a river, some short ranges) ----
            int riverPick = -1; float riverRoll = -1f;
            if (Borders.Count >= 4)
                for (int i = 0; i < Borders.Count; i++)
                {
                    var b = Borders[i];
                    if (fam[b.A] == V2.Family.Volcanic || fam[b.B] == V2.Family.Volcanic) continue;
                    float r = V2.H01(seed + 303, b.A, b.B);
                    if (r > riverRoll) { riverRoll = r; riverPick = i; }
                }
            for (int i = 0; i < Borders.Count; i++)
            {
                var b = Borders[i];
                float r = V2.H01(seed + 301, b.A, b.B);
                b.Plan = i == riverPick ? Plan.River : r < 0.18f ? Plan.Open : r < 0.42f ? Plan.Short : Plan.Range;
            }

            // ---- 4. range spines over part of each border ----
            for (int i = 0; i < lines.Count; i++)
            {
                var (b, line) = lines[i];
                if (b.Plan != Plan.Short && b.Plan != Plan.Range) continue;
                var whole = new Spine(line);
                float r2 = V2.H01(seed + 311, b.A, b.B), r3 = V2.H01(seed + 313, b.A, b.B), r4 = V2.H01(seed + 317, b.A, b.B), r5 = V2.H01(seed + 319, b.A, b.B);
                float frac = b.Plan == Plan.Short ? 0.2f + 0.15f * r2 : 0.4f + 0.45f * r2;
                float len = Mathf.Max(frac * whole.Length, Mathf.Min(900f, whole.Length));
                float from = (whole.Length - len) * r3, to = from + len;
                var sub = new List<Vector2> { whole.PointAt(from, out _) };
                for (int k = 0; k < whole.Nodes.Length; k++) if (whole.Along[k] > from && whole.Along[k] < to) sub.Add(whole.Nodes[k]);
                sub.Add(whole.PointAt(to, out _));
                if (sub.Count < 3) continue;
                var sp = new Spine(sub) { Realms = new Vector2Int(b.A, b.B) };
                var kind = V2.KindOf(fam[b.A], fam[b.B]); sp.Kind = (byte)kind;
                sp.Crest = kind == V2.Ridge.Volcano ? 150f + 40f * r4 : kind == V2.Ridge.Mesas ? 65f + 15f * r4 : 125f + 45f * r4;
                if (b.Plan == Plan.Short) sp.Crest *= 0.8f;
                sp.HalfWidth = (kind == V2.Ridge.Volcano ? 560f : kind == V2.Ridge.Mesas ? 430f : 520f) * (0.85f + 0.3f * r5);
                sp.TaperIn = Mathf.Min(600f + 600f * V2.H01(seed + 321, b.A, b.B), 0.45f * sp.Length);
                sp.TaperOut = Mathf.Min(600f + 600f * V2.H01(seed + 323, b.A, b.B), 0.45f * sp.Length);
                b.Spine = Spines.Count; Spines.Add(sp);
            }
            int ranges = Spines.Count;

            // ---- 5. the massif: wide high ground on the longest mountain range, away from the lake ----
            int massifSpine = -1; float longest = 0f;
            for (int i = 0; i < ranges; i++)
                if (Spines[i].Kind == (byte)V2.Ridge.Mountains && Spines[i].Length > longest) { longest = Spines[i].Length; massifSpine = i; }
            if (massifSpine < 0) for (int i = 0; i < ranges; i++) if (Spines[i].Length > longest) { longest = Spines[i].Length; massifSpine = i; }
            float massifAlong = 0f;
            if (massifSpine >= 0)
            {
                var sp = Spines[massifSpine]; float bestScore = float.MinValue;
                foreach (var f in new[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f })
                {
                    var p = sp.PointAt(f * sp.Length, out _);
                    float score = Vector2.Distance(p, lakeWorld) - (CoastAt(p, coastWobble) < 0.22f ? 1e5f : 0f);
                    if (score > bestScore) { bestScore = score; MassifAt = p; massifAlong = f * sp.Length; }
                }
                MassifRadius = 1100f + 300f * V2.H01(seed + 331, 1, 1);
                MassifHeight = 150f + 30f * V2.H01(seed + 333, 1, 1);
            }

            // ---- 6. passes: 1-3 per range, saddles at 20-40% of the crest, kept out of the massif ----
            for (int i = 0; i < ranges; i++)
            {
                var sp = Spines[i]; var rv = sp.Realms;
                int count = sp.Length < 1500f ? 1 : 1 + Mathf.Min(2, (int)(V2.H01(seed + 341, rv.x, rv.y) * 3f));
                for (int k = 0; k < count; k++)
                {
                    float f = (k + 0.5f) / count + (V2.H01(seed + 343 + (uint)k, rv.x, rv.y) - 0.5f) * 0.5f / count;
                    float along = Mathf.Clamp(f * sp.Length, 0.15f * sp.Length, 0.85f * sp.Length);
                    if (i == massifSpine && Mathf.Abs(along - massifAlong) < MassifRadius)
                        along = Mathf.Clamp(massifAlong + (along >= massifAlong ? 1f : -1f) * MassifRadius * 0.95f, 0.08f * sp.Length, 0.92f * sp.Length);
                    float width = 180f + 140f * V2.H01(seed + 347 + (uint)k, rv.x, rv.y);
                    bool clash = false; foreach (var q in sp.Passes) if (Mathf.Abs(q.Along - along) < 2f * width) clash = true;
                    if (clash) continue;
                    var at = sp.PointAt(along, out var tan);
                    var pass = new RangePass { Spine = i, Along = along, Width = width, Depth = 0.2f + 0.2f * V2.H01(seed + 349 + (uint)k, rv.x, rv.y), At = at, Across = new Vector2(-tan.y, tan.x) };
                    sp.Passes.Add(pass); Passes.Add(pass);
                }
            }

            // ---- 7. spurs: 2-6 per range, branching at 30-70 degrees, lower, narrower, tapering out into foothills ----
            for (int i = 0; i < ranges; i++)
            {
                var sp = Spines[i];
                int count = 2 + (int)(V2.H01(seed + 351, sp.Realms.x, sp.Realms.y) * 4.99f);
                for (int k = 0; k < count; k++)
                {
                    float R(uint salt) => V2.H01(seed + 360 + salt, i, k);
                    float along = sp.Length * (0.15f + 0.7f * R(0));
                    var start = sp.PointAt(along, out var tan);
                    float ang = (30f + 40f * R(1)) * Mathf.Deg2Rad * (R(2) < 0.5f ? -1f : 1f);
                    var dir = R(3) < 0.5f ? tan : -tan;
                    dir = new Vector2(dir.x * Mathf.Cos(ang) - dir.y * Mathf.Sin(ang), dir.x * Mathf.Sin(ang) + dir.y * Mathf.Cos(ang));
                    float len = Mathf.Min(500f + 900f * R(4), 0.8f * sp.Length);
                    var nodes = new List<Vector2> { start }; var p = start;
                    for (float s = 0; s < len; s += 60f)
                    {
                        float curl = (V2.Fbm(seed + 371 + (uint)i, p.x / 400f, p.y / 400f, 2) - 0.5f) * 0.35f;
                        dir = new Vector2(dir.x * Mathf.Cos(curl) - dir.y * Mathf.Sin(curl), dir.x * Mathf.Sin(curl) + dir.y * Mathf.Cos(curl));
                        p += dir * 60f;
                        if (CoastAt(p, coastWobble) < 0.08f) break;
                        nodes.Add(p);
                    }
                    if (nodes.Count < 4) continue;
                    float t0 = Smooth01(along / sp.TaperIn) * Smooth01((sp.Length - along) / sp.TaperOut);
                    var spur = new Spine(nodes) { Parent = i, Kind = sp.Kind, Realms = sp.Realms };
                    spur.Crest = sp.Crest * t0 * (0.45f + 0.2f * R(5));
                    spur.HalfWidth = sp.HalfWidth * (0.5f + 0.15f * R(6));
                    spur.TaperIn = 120f; spur.TaperOut = 0.65f * spur.Length;
                    if (spur.Crest > 10f) Spines.Add(spur);
                }
            }

            // ---- 8. raster: per cell, the nearest point of each spine (segments bucketed), then max of profiles ----
            const int Bk = 64; float bucketM = W / Bk;
            var segSpine = new List<int>(); var segK = new List<int>();
            var buckets = new List<int>[Bk * Bk];
            for (int i = 0; i < Spines.Count; i++)
            {
                var sp = Spines[i]; float reach = 3.2f * sp.HalfWidth;
                for (int k = 0; k < sp.Nodes.Length - 1; k++)
                {
                    int id = segSpine.Count; segSpine.Add(i); segK.Add(k);
                    var a = sp.Nodes[k]; var b = sp.Nodes[k + 1];
                    int x0 = Mathf.Clamp((int)((Mathf.Min(a.x, b.x) - reach + Half) / bucketM), 0, Bk - 1), x1 = Mathf.Clamp((int)((Mathf.Max(a.x, b.x) + reach + Half) / bucketM), 0, Bk - 1);
                    int y0 = Mathf.Clamp((int)((Mathf.Min(a.y, b.y) - reach + Half) / bucketM), 0, Bk - 1), y1 = Mathf.Clamp((int)((Mathf.Max(a.y, b.y) + reach + Half) / bucketM), 0, Bk - 1);
                    for (int by = y0; by <= y1; by++) for (int bx = x0; bx <= x1; bx++) (buckets[by * Bk + bx] ??= new List<int>()).Add(id);
                }
            }
            var field = new float[Res * Res];
            float cell = W / Res;
            System.Threading.Tasks.Parallel.For(0, Res, y =>
            {
                for (int x = 0; x < Res; x++)
                {
                    var p = new Vector2(-Half + (x + 0.5f) * cell, -Half + (y + 0.5f) * cell);
                    float coast = CoastAt(p, coastWobble);
                    if (coast <= 0f) continue;
                    float h = 0f, prox = 0f;
                    var bucket = buckets[Mathf.Clamp((int)((p.y + Half) / bucketM), 0, Bk - 1) * Bk + Mathf.Clamp((int)((p.x + Half) / bucketM), 0, Bk - 1)];
                    if (bucket != null)
                    {
                        // Every segment near the cell adds its own profile and the max is kept: a max of continuous
                        // pieces stays continuous, where "nearest point on the spine" jumps inside bends.
                        foreach (var id in bucket)
                        {
                            int si = segSpine[id], k = segK[id]; var sp = Spines[si];
                            var a = sp.Nodes[k]; var ab = sp.Nodes[k + 1] - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-3f));
                            float d = (a + ab * t - p).magnitude, s = sp.Along[k] + t * (sp.Along[k + 1] - sp.Along[k]);
                            if (d >= 3.2f * sp.HalfWidth) continue;
                            float taper = Smooth01(s / sp.TaperIn) * Smooth01((sp.Length - s) / sp.TaperOut);
                            if (taper <= 0f) continue;
                            prox = Mathf.Max(prox, taper * (1f - d / (3.2f * sp.HalfWidth)));
                            if (d >= 1.15f * sp.HalfWidth) continue;
                            float hw = sp.HalfWidth * (0.35f + 0.65f * taper) * (0.85f + 0.3f * V2.Fbm(seed + 411 + (uint)si, s / 700f, 3.1f, 2));
                            float u = d / hw;
                            if (u >= 1f) continue;
                            h = Mathf.Max(h, CrestAt(sp, si, s) * taper * Profile(sp.Kind, u));
                        }
                    }
                    if (MassifRadius > 0f)
                    {
                        float r = Vector2.Distance(p, MassifAt) * (0.8f + 0.4f * V2.Fbm(seed + 415, p.x / 600f, p.y / 600f, 3));
                        float t = 1f - r / MassifRadius;
                        if (t > 0f)
                        {
                            float ridged = 1f - Mathf.Abs(2f * V2.Fbm(seed + 417, p.x / 330f, p.y / 330f, 3) - 1f);
                            h = Mathf.Max(h, MassifHeight * (0.62f * Smooth01(t * 1.7f) + 0.5f * ridged * ridged * Smooth01(t * 1.3f)));
                            prox = Mathf.Max(prox, Mathf.Clamp01(t * 2f));
                        }
                    }
                    h *= 0.9f + 0.2f * V2.Fbm(seed + 421, p.x / 170f, p.y / 170f, 3);
                    h += prox * 14f * Smooth01((V2.Fbm(seed + 431, p.x / 240f, p.y / 240f, 3) - 0.38f) / 0.4f);   // rolling foothill bumps
                    field[y * Res + x] = h * Mathf.Clamp01(coast / 0.12f);
                }
            });

            // ---- 9. soften: where the nearest point on a bent spine jumps (inside a bend, near a pass dip), the profile
            // can step; two 5-tap box passes each way (~18 m radius) turn those steps into slopes ----
            var tmp = new float[Res * Res];
            for (int pass = 0; pass < 2; pass++)
            {
                System.Threading.Tasks.Parallel.For(0, Res, y =>
                {
                    for (int x = 0; x < Res; x++)
                    {
                        float acc = 0f; for (int d = -2; d <= 2; d++) acc += field[y * Res + Mathf.Clamp(x + d, 0, Res - 1)];
                        tmp[y * Res + x] = acc * 0.2f;
                    }
                });
                System.Threading.Tasks.Parallel.For(0, Res, y =>
                {
                    for (int x = 0; x < Res; x++)
                    {
                        float acc = 0f; for (int d = -2; d <= 2; d++) acc += tmp[Mathf.Clamp(y + d, 0, Res - 1) * Res + x];
                        field[y * Res + x] = acc * 0.2f;
                    }
                });
            }

            // ---- the [ranges] line ----
            var sb = new System.Text.StringBuilder("[ranges] ");
            float total = 0f, rangeLen = 0f, peakMax = 0f, peakMin = float.MaxValue;
            for (int i = 0; i < Spines.Count; i++)
            {
                var sp = Spines[i]; total += sp.Length; if (i < ranges) rangeLen += sp.Length;
                float peak = 0f;
                for (float s = 0; s <= sp.Length; s += 30f) peak = Mathf.Max(peak, CrestAt(sp, i, s) * Smooth01(s / sp.TaperIn) * Smooth01((sp.Length - s) / sp.TaperOut));
                if (i < ranges) { peakMax = Mathf.Max(peakMax, peak); peakMin = Mathf.Min(peakMin, peak); }
            }
            float fieldMax = 0f; foreach (var v in field) fieldMax = Mathf.Max(fieldMax, v);
            sb.Append($"spines {Spines.Count} ({ranges} ranges + {Spines.Count - ranges} spurs), length {total / 1000f:F1} km (ranges {rangeLen / 1000f:F1}); range peak max {peakMax:F0} min {(ranges > 0 ? peakMin : 0):F0} m, field max {fieldMax:F0} m");
            sb.Append($"; passes {Passes.Count}; borders {Borders.Count}:");
            int noRange = 0;
            foreach (var b in Borders)
            {
                if (b.Spine < 0) { noRange++; sb.Append($" {b.Name} {b.Plan.ToString().ToLowerInvariant()} (no range, {b.Length / 1000f:F1} km)"); continue; }
                var sp = Spines[b.Spine];
                sb.Append($" {b.Name} {b.Plan.ToString().ToLowerInvariant()} {(V2.Ridge)sp.Kind} {sp.Length / 1000f:F1}/{b.Length / 1000f:F1} km {sp.Passes.Count} pass{(sp.Passes.Count == 1 ? "" : "es")};");
            }
            sb.Append($" no range on {noRange}");
            if (MassifRadius > 0f) sb.Append($"; massif at ({MassifAt.x:F0},{MassifAt.y:F0}) r {MassifRadius:F0} m h {MassifHeight:F0} m");
            sb.Append($"; field {Res} ({cell:F0} m cells), {watch.ElapsedMilliseconds} ms");
            LastLog = sb.ToString();
            return field;
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>Crest height at s along the spine: ridged peaks and saddles along its length, dipped at its passes.</summary>
        static float CrestAt(Spine sp, int si, float s)
        {
            float r = 1f - Mathf.Abs(2f * V2.Fbm(seed + 401 + (uint)si * 7, s / 380f, si * 3.7f, 3) - 1f);
            float c = sp.Kind == (byte)V2.Ridge.Volcano ? sp.Crest * (0.35f + 0.65f * r * r * r)
                    : sp.Kind == (byte)V2.Ridge.Mesas ? sp.Crest * (0.85f + 0.15f * r)
                    : sp.Crest * (0.55f + 0.45f * r * r);
            foreach (var q in sp.Passes) c *= Mathf.Lerp(1f, q.Depth, Smooth01(1f - Mathf.Abs(s - q.Along) / q.Width));
            return c;
        }

        /// <summary>Cross-section by distance from the spine (u = 0 on the crest, 1 at the foot): a sharp crest and a
        /// concave lower flank; cones for volcano chains, soft steps for mesa bands.</summary>
        static float Profile(byte kind, float u)
        {
            float p = 1f - u;
            if (kind == (byte)V2.Ridge.Volcano) return Mathf.Pow(p, 2.3f);
            if (kind == (byte)V2.Ridge.Mesas)
            {
                float q = Mathf.Pow(p, 1.2f) * 3f, fl = Mathf.Floor(q), f = q - fl;
                return (fl + Smooth01(Smooth01(f))) / 3f;
            }
            return Mathf.Pow(p, 1.8f);
        }

        // ---------------------------------------------------------------- checks on a finished island

        /// <summary>
        /// Walls and passes on any island: land cells (8 m grid) whose ground is steeper than 45 degrees (and 38), and
        /// for each v3 pass the steepest 4 m step on a straight walk across the range through it.
        /// </summary>
        public static string Checks(in TerrainSource source, float step = 8f)
        {
            var s = source; float sea = s.World.SeaLevel;
            int size = (int)(s.World.WorldSize / step); float half = s.World.WorldSize * 0.5f;
            var g = new float[size * size];
            System.Threading.Tasks.Parallel.For(0, size, z =>
            {
                for (int x = 0; x < size; x++) g[z * size + x] = s.SampleHeight(new float2(-half + (x + 0.5f) * step, -half + (z + 0.5f) * step));
            });
            int land = 0, walls = 0, steep = 0; float maxSlope = 0f; var wallAt = new List<string>();
            for (int z = 1; z < size - 1; z++)
                for (int x = 1; x < size - 1; x++)
                {
                    int i = z * size + x; if (g[i] <= sea + 0.5f) continue;
                    land++;
                    float gx = (g[i + 1] - g[i - 1]) / (2f * step), gz = (g[i + size] - g[i - size]) / (2f * step);
                    float sl = Mathf.Sqrt(gx * gx + gz * gz); maxSlope = Mathf.Max(maxSlope, sl);
                    if (sl > 0.78f) steep++;
                    if (sl > WallSlope) { walls++; if (wallAt.Count < 4 && walls % 50 == 1) wallAt.Add($"({-half + (x + 0.5f) * step:F0},{-half + (z + 0.5f) * step:F0})"); }
                }
            var sb = new System.Text.StringBuilder($"[ranges check] land cells {land} ({step:F0} m): over 45 deg {walls}, over 38 deg {steep}, max {Mathf.Atan(maxSlope) * Mathf.Rad2Deg:F0} deg; walls e.g. {string.Join(" ", wallAt)}");
            if (Passes.Count > 0 && IslandV2.RequestedVersion() == IslandV3.Version)
            {
                // A crossing is walkable if one straight line through the saddle is: try 7 lines (0, +-30, +-60, +-90 m
                // along the spine) and keep the best. Steps over water (rivers through the pass) are counted, not sloped.
                sb.Append("; passes (best of 7 lines, max 4 m step on dry ground / range field alone / water steps):");
                float worst = 0f, worstField = 0f;
                foreach (var q in Passes)
                {
                    var sp = Spines[q.Spine]; float reach = 1.6f * sp.HalfWidth, saddle = s.SampleHeight(new float2(q.At.x, q.At.y));
                    var tan = new Vector2(q.Across.y, -q.Across.x);
                    float best = float.MaxValue, bestField = 0f; int bestWet = 0;
                    for (int line = -3; line <= 3; line++)
                    {
                        float dry = 0f, fieldMax = 0f, prev = float.NaN, prevF = 0f; int wet = 0;
                        for (float t = -reach; t <= reach; t += 4f)
                        {
                            var p = q.At + q.Across * t + tan * (line * 30f); var xz = new float2(p.x, p.y);
                            var gs = s.SampleGround(xz); float f = s.Island.IsCreated ? s.Island.Value.Sample(xz) : 0f;
                            bool water = gs.HasWater && gs.WaterSurface > gs.Height - 0.05f || gs.River > 0.3f;
                            if (!float.IsNaN(prev))
                            {
                                if (water) wet++; else dry = Mathf.Max(dry, Mathf.Abs(gs.Height - prev) / 4f);
                                fieldMax = Mathf.Max(fieldMax, Mathf.Abs(f - prevF) / 4f);
                            }
                            prev = gs.Height; prevF = f;
                        }
                        if (dry < best) { best = dry; bestField = fieldMax; bestWet = wet; }
                    }
                    worst = Mathf.Max(worst, best); worstField = Mathf.Max(worstField, bestField);
                    var b = Borders.Find(bb => bb.Spine == q.Spine);
                    sb.Append($" {b?.Name}@({q.At.x:F0},{q.At.y:F0}) {Mathf.Atan(best) * Mathf.Rad2Deg:F0}/{Mathf.Atan(bestField) * Mathf.Rad2Deg:F0} deg w{bestWet} h{saddle:F0};");
                }
                sb.Append($" worst pass {Mathf.Atan(worst) * Mathf.Rad2Deg:F0} deg (range field {Mathf.Atan(worstField) * Mathf.Rad2Deg:F0})");
            }
            return sb.ToString();
        }

        /// <summary>Top-down height map of any island to a PNG (north up): height colours with hillshade; v3 passes in red, the massif ring in white.</summary>
        public static string HeightPng(in TerrainSource source, string path, int px = 1024, bool marks = true)
        {
            var s = source; float sea = s.World.SeaLevel, size = s.World.WorldSize, half = size * 0.5f, cell = size / px;
            var h = new float[px * px];
            System.Threading.Tasks.Parallel.For(0, px, j => { for (int i = 0; i < px; i++) h[j * px + i] = s.SampleHeight(new float2(-half + (i + 0.5f) * cell, -half + (j + 0.5f) * cell)); });
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
            var cols = new Color[px * px];
            for (int j = 0; j < px; j++)
                for (int i = 0; i < px; i++)
                {
                    int k = j * px + i; float hh = h[k] - sea;
                    Color c;
                    if (hh < 0f) c = Color.Lerp(new Color(0.25f, 0.45f, 0.65f), new Color(0.08f, 0.15f, 0.35f), Mathf.Clamp01(-hh / 40f));
                    else
                    {
                        float t = Mathf.Clamp01(hh / 320f);
                        c = t < 0.25f ? Color.Lerp(new Color(0.35f, 0.55f, 0.3f), new Color(0.65f, 0.65f, 0.4f), t / 0.25f)
                          : t < 0.6f ? Color.Lerp(new Color(0.65f, 0.65f, 0.4f), new Color(0.5f, 0.38f, 0.28f), (t - 0.25f) / 0.35f)
                          : Color.Lerp(new Color(0.5f, 0.38f, 0.28f), Color.white, (t - 0.6f) / 0.4f);
                        float dx = i + 1 < px ? h[k + 1] - h[k] : 0f, dz = j + 1 < px ? h[k + px] - h[k] : 0f;
                        c *= Mathf.Clamp(1f + (dz - dx) * 0.08f, 0.55f, 1.4f);
                    }
                    cols[k] = c;
                }
            if (marks && IslandV2.RequestedVersion() == IslandV3.Version)
            {
                void Dot(Vector2 w, Color c, int r)
                {
                    int ci = (int)((w.x + half) / cell), cj = (int)((w.y + half) / cell);
                    for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                        if (ci + di >= 0 && ci + di < px && cj + dj >= 0 && cj + dj < px && di * di + dj * dj <= r * r) cols[(cj + dj) * px + ci + di] = c;
                }
                foreach (var q in Passes) Dot(q.At, Color.red, 5);
                if (MassifRadius > 0f) for (int a = 0; a < 360; a += 2) Dot(MassifAt + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * MassifRadius, Color.white, 1);
            }
            tex.SetPixels(cols); tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }
    }
}
