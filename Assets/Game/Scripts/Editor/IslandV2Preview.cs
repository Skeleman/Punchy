using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// Island v2 design spike (#23): draws top-down preview maps of a candidate island generator, without touching the
    /// shipping one (v0.11 stays the default and its landmark hash must not change; nothing here is called from the game).
    /// A realm pass puts 4-6 macro cells over the island, each taking a climate family; the existing per-biome pick would
    /// then run inside the family (here a jittered-cell stand-in). Where two realms meet a border ridge is raised (mesas
    /// between arid realms, a volcano with a trailing ridge where the volcanic realm touches, mountains elsewhere), with
    /// gaps left as passes. A bowl is pressed into the centre for a lake, and rivers run downhill from the lake rim and
    /// from the ridges to the coast. Run: unity -batchmode -executeMethod Punchy.EditorTools.IslandV2Preview.Render -out dir
    /// </summary>
    public static class IslandV2Preview
    {
        public const int GeneratorVersion = 2;
        const int N = 1024;
        const float World = 9216f;

        enum Family { Arid, Wet, Cold, Volcanic, Temperate }
        enum Ridge { None, Mesas, Mountains, Volcano }

        static readonly Dictionary<Family, (string name, Color colour)[]> Biomes = new Dictionary<Family, (string, Color)[]>
        {
            { Family.Arid, new[] { ("Dunes", C(0.93f, 0.82f, 0.50f)), ("SaltFlat", C(0.93f, 0.93f, 0.88f)), ("Sonoran", C(0.80f, 0.62f, 0.35f)),
                                    ("GlassDesert", C(0.55f, 0.85f, 0.85f)), ("MesaCanyonlands", C(0.78f, 0.42f, 0.25f)) } },
            { Family.Wet, new[] { ("FoggyMarsh", C(0.45f, 0.62f, 0.55f)), ("PoisonSwamp", C(0.45f, 0.50f, 0.20f)),
                                  ("FungalForest", C(0.70f, 0.45f, 0.70f)), ("SparklingForest", C(0.30f, 0.75f, 0.55f)) } },
            { Family.Cold, new[] { ("Glacier", C(0.85f, 0.93f, 1.00f)), ("Tundra", C(0.65f, 0.72f, 0.72f)), ("Mountains", C(0.60f, 0.60f, 0.65f)) } },
            { Family.Volcanic, new[] { ("Ashlands", C(0.35f, 0.33f, 0.33f)), ("EmberForest", C(0.70f, 0.30f, 0.15f)), ("ElectricWastelands", C(0.55f, 0.55f, 0.85f)) } },
            { Family.Temperate, new[] { ("Temperate", C(0.35f, 0.70f, 0.30f)), ("Grassland", C(0.60f, 0.80f, 0.35f)) } },
        };

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // One variant: how many realms, which seed, how big the lake, how wide the ridges, how many ridge-fed rivers.
        struct Variant { public string name; public int realms; uint seed; public float lakeRadius, ridgeWidth; public int ridgeRivers; public float coast;
            public Variant(string n, int r, uint s, float lake, float ridge, int rr, float coastWobble) { name = n; realms = r; seed = s; lakeRadius = lake; ridgeWidth = ridge; ridgeRivers = rr; coast = coastWobble; }
            public uint Seed => seed; }

        public static void Render()
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-out");
            var dir = at >= 0 && at + 1 < args.Length ? args[at + 1] : "IslandV2";
            Directory.CreateDirectory(dir);
            var variants = new[]
            {
                new Variant("A-four-realms", 4, 101, 0.10f, 0.012f, 3, 0.10f),
                new Variant("B-five-realms", 5, 202, 0.13f, 0.016f, 4, 0.14f),
                new Variant("C-six-realms", 6, 303, 0.085f, 0.010f, 5, 0.18f),
            };
            foreach (var v in variants)
                Draw(v, dir);
            EditorApplication.Exit(0);
        }

        // ---- hashing and noise (scrambled integer hashes: a plain hash of small ints has no avalanche) ----
        static uint Scramble(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
        static uint H(uint seed, int a, int b) => Scramble(seed * 0x9E3779B1u ^ Scramble((uint)a * 0x85ebca6bu + (uint)b * 0xc2b2ae35u + 0x27d4eb2fu));
        static float H01(uint seed, int a, int b) => (H(seed, a, b) & 0xFFFFFF) / 16777216f;
        static float Smooth(float t) => t * t * (3 - 2 * t);

        static float Value(uint seed, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            float a = H01(seed, ix, iy), b = H01(seed, ix + 1, iy), c = H01(seed, ix, iy + 1), d = H01(seed, ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(uint seed, float x, float y, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += amp * Value(seed + (uint)i * 17, x, y); norm += amp; x *= 2.03f; y *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }

        static void Draw(Variant v, string dir)
        {
            uint seed = v.Seed;
            // Realm sites: spread round the island (never on the centre, which is the lake), picked over a few tries so
            // that cold and volcanic realms are never neighbours (a buffer is a temperate or arid realm between them).
            Vector2[] sites = null; Family[] fam = null; int[,] realm = null; int bestBad = int.MaxValue;
            for (uint attempt = 0; attempt < 80 && bestBad > 0; attempt++)
            {
                uint s = seed + attempt * 7919;
                var sx = new Vector2[v.realms]; var fm = Shuffled(v.realms, s);
                float turn = H01(s, 1, 1) * 6.2832f;
                for (int i = 0; i < v.realms; i++)
                {
                    float a = turn + (i + 0.4f * (H01(s, 2, i) - 0.5f)) / v.realms * 6.2832f;
                    float r = 0.24f + 0.12f * H01(s, 3, i);
                    sx[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r);
                }
                var map = RealmMap(sx, s);
                int bad = ForbiddenBorders(map, fm, v.realms);
                if (bad < bestBad) { bestBad = bad; sites = sx; fam = fm; realm = map; seed = s; }
            }

            // Biomes inside each realm: warped jittered cells of about 1 km, each taking one biome of the realm's family.
            var colour = new Color[N * N];
            var land = new bool[N * N];
            var coastMask = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float m = Coast(seed, x, y, v.coast);
                    coastMask[y * N + x] = m;
                    land[y * N + x] = m > 0;
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    if (!land[i]) { colour[i] = C(0.12f, 0.28f, 0.50f); continue; }
                    var list = Biomes[fam[realm[y, x]]];
                    float wx = x + (Fbm(seed + 31, x / 90f, y / 90f) - 0.5f) * 90f, wy = y + (Fbm(seed + 37, x / 90f, y / 90f) - 0.5f) * 90f;
                    int cell = NearestCell(seed + 5, wx / 110f, wy / 110f);
                    float shade = 0.92f + 0.08f * H01(seed + 9, cell, 3);
                    colour[i] = list[(int)(H(seed + 11, cell, realm[y, x]) % (uint)list.Length)].colour * shade;
                    colour[i].a = 1f;
                }

            // Height: a plateau rising from the coast, a raised ridge on every border, a bowl in the middle.
            var ridgeKind = new Ridge[N * N]; var ridgeAmt = new float[N * N];
            RidgeField(seed, v, sites, fam, ridgeKind, ridgeAmt);
            var h = new float[N * N];
            var route = new float[N * N];   // the height rivers follow: the land without the lake bowl, so the lake spills outwards
            Vector2 centre = new Vector2(N * 0.5f + (H01(seed, 8, 8) - 0.5f) * 40f, N * 0.5f + (H01(seed, 9, 9) - 0.5f) * 40f);
            float lakeR = v.lakeRadius * N;
            var lake = new bool[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    if (!land[i]) { h[i] = route[i] = -0.1f + coastMask[i]; continue; }
                    float rl = Vector2.Distance(new Vector2(x, y), centre) * (0.85f + 0.3f * Fbm(seed + 53, x / 120f, y / 120f));
                    float bowl = 0.30f * (1 - Smooth(Mathf.Clamp01(rl / (lakeR * 1.8f))));
                    h[i] = 0.55f * Smooth(Mathf.Clamp01(coastMask[i] / 0.35f)) + 0.45f * ridgeAmt[i] + 0.06f * Fbm(seed + 61, x / 50f, y / 50f) - bowl;
                    route[i] = h[i] + bowl;
                    if (rl < lakeR) lake[i] = true;
                }
            float lakeLevel = 0f; int lakeCount = 0;
            for (int i = 0; i < N * N; i++) if (lake[i]) { lakeLevel += h[i]; lakeCount++; }
            lakeLevel = lakeCount > 0 ? lakeLevel / lakeCount : 0f;
            for (int i = 0; i < N * N; i++) if (lake[i]) h[i] = lakeLevel;

            // Rivers: from points on the lake rim and along the ridges, steepest descent to the sea (or to the lake).
            var flow = new int[N * N];
            var sources = new List<(Vector2Int p, bool ridge)>();
            for (int k = 0; k < 3; k++)
            {
                float a = H01(seed, 70, k) * 6.2832f + k * 2.1f;
                sources.Add((new Vector2Int((int)(centre.x + Mathf.Cos(a) * lakeR * 1.15f), (int)(centre.y + Mathf.Sin(a) * lakeR * 1.15f)), false));
            }
            var ridgePoints = new List<Vector2Int>();
            for (int y = 4; y < N - 4; y += 4)
                for (int x = 4; x < N - 4; x += 4)
                    if (ridgeKind[y * N + x] != Ridge.None && ridgeAmt[y * N + x] > 0.85f && land[y * N + x] && !lake[y * N + x]) ridgePoints.Add(new Vector2Int(x, y));
            var rr = new System.Random((int)seed);
            for (int tries = 0; tries < 4000 && sources.Count < 3 + v.ridgeRivers && ridgePoints.Count > 0; tries++)
            {
                var p = ridgePoints[rr.Next(ridgePoints.Count)];
                bool apart = true;
                foreach (var s2 in sources) if ((s2.p - p).magnitude < 160) { apart = false; break; }
                if (apart) sources.Add((p, true));
            }
            var drain = Fill(route, land);
            foreach (var s in sources) Descend(s.p, drain, land, flow);

            // Thicken the one-pixel river lines so they read at a glance (twice as wide where several rivers have merged).
            var wide = new int[N * N];
            for (int y = 2; y < N - 2; y++)
                for (int x = 2; x < N - 2; x++)
                {
                    int f = flow[y * N + x]; if (f <= 0) continue;
                    int r = f > 2 ? 2 : 1;
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++) wide[(y + dy) * N + x + dx] = Mathf.Max(wide[(y + dy) * N + x + dx], f);
                }
            flow = wide;

            // Compose: biome colours, ridges over them, lake and rivers on top, sources marked.
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; var c = colour[i];
                    if (land[i] && ridgeAmt[i] > 0.08f)
                    {
                        Color rc = ridgeKind[i] == Ridge.Mesas ? C(0.62f, 0.30f, 0.15f) : ridgeKind[i] == Ridge.Volcano ? C(0.25f, 0.08f, 0.05f) : C(0.38f, 0.36f, 0.40f);
                        c = Color.Lerp(c, rc, Mathf.Clamp01(ridgeAmt[i] * 1.6f));
                    }
                    if (land[i] && flow[i] > 0) c = flow[i] > 2 ? C(0.10f, 0.35f, 0.95f) : C(0.25f, 0.55f, 1.0f);
                    if (lake[i]) c = C(0.15f, 0.45f, 0.95f);
                    tex.SetPixel(x, N - 1 - y, c);
                }
            // Volcano cones where the volcanic realm touches a ridge: a marker on its highest ridge point.
            foreach (var p in VolcanoSpots(ridgeKind, ridgeAmt, h, land, seed))
                for (int dy = -9; dy <= 9; dy++) for (int dx = -9; dx <= 9; dx++)
                    if (dx * dx + dy * dy <= 81 && p.x + dx >= 0 && p.x + dx < N && p.y + dy >= 0 && p.y + dy < N)
                        tex.SetPixel(p.x + dx, N - 1 - (p.y + dy), dx * dx + dy * dy <= 25 ? C(1f, 0.35f, 0.05f) : C(0.2f, 0.05f, 0.03f));
            foreach (var s in sources)
                for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
                    if (s.p.x + dx >= 0 && s.p.x + dx < N && s.p.y + dy >= 0 && s.p.y + dy < N)
                        tex.SetPixel(s.p.x + dx, N - 1 - (s.p.y + dy), s.ridge ? Color.white : C(1f, 1f, 0.2f));
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"island-v2-{v.name}.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"island-v2-{v.name}: generator version {GeneratorVersion}, seed {seed}, {N}x{N} px = {World} m ({World / N:F0} m per px)");
            for (int i = 0; i < v.realms; i++) sb.AppendLine($"realm {i}: {fam[i]} at ({sites[i].x:F2},{sites[i].y:F2})");
            sb.AppendLine($"forbidden (cold/volcanic) borders left: {bestBad} sampled border pixels");
            File.WriteAllText(Path.Combine(dir, $"island-v2-{v.name}.txt"), sb.ToString());
        }

        static Family[] Shuffled(int count, uint seed)
        {
            var all = new List<Family> { Family.Arid, Family.Wet, Family.Cold, Family.Volcanic, Family.Temperate };
            var list = new List<Family>();
            var rng = new System.Random((int)seed);
            while (list.Count < count)
            {
                if (all.Count == 0) all.AddRange(new[] { Family.Arid, Family.Wet, Family.Temperate });
                int k = rng.Next(all.Count); list.Add(all[k]); all.RemoveAt(k);
            }
            return list.ToArray();
        }

        // Coast: a wobbly disc, 1 at the centre falling to 0 at the shore (negative in the sea).
        static float Coast(uint seed, int x, int y, float wobble)
        {
            float dx = (x - N * 0.5f) / (N * 0.5f), dy = (y - N * 0.5f) / (N * 0.5f);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float w = Fbm(seed + 3, Mathf.Cos(ang) * 1.6f + 5, Mathf.Sin(ang) * 1.6f + 5, 3) - 0.5f;
            float local = (Fbm(seed + 4, x / 140f, y / 140f, 3) - 0.5f) * wobble * 0.7f;
            return 0.88f - r + w * wobble * 2f + local;
        }

        static int NearestCell(uint seed, float x, float y)
        {
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y); float best = 1e9f; int id = 0;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float px = cx + i + H01(seed, cx + i, cy + j), py = cy + j + H01(seed + 1, cx + i, cy + j);
                    float d = (px - x) * (px - x) + (py - y) * (py - y);
                    if (d < best) { best = d; id = (cx + i) * 4099 + (cy + j); }
                }
            return id;
        }

        static float RealmDistance(Vector2 site, float x, float y, uint seed, int index)
        {
            float wx = x + (Fbm(seed + 71, x / 260f, y / 260f) - 0.5f) * 0.28f * N, wy = y + (Fbm(seed + 73, x / 260f, y / 260f) - 0.5f) * 0.28f * N;
            return Vector2.Distance(new Vector2(wx, wy), site * N);
        }

        static int[,] RealmMap(Vector2[] sites, uint seed)
        {
            var map = new int[N, N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float best = 1e9f; int id = 0;
                    for (int s = 0; s < sites.Length; s++) { float d = RealmDistance(sites[s], x, y, seed, s); if (d < best) { best = d; id = s; } }
                    map[y, x] = id;
                }
            return map;
        }

        static int ForbiddenBorders(int[,] map, Family[] fam, int count)
        {
            int bad = 0;
            for (int y = 0; y < N - 1; y += 3)
                for (int x = 0; x < N - 1; x += 3)
                {
                    int a = map[y, x], b = map[y, x + 1], c = map[y + 1, x];
                    foreach (int o in new[] { b, c }) if (o != a && Forbidden(fam[a], fam[o])) bad++;
                }
            return bad;
        }

        static bool Forbidden(Family a, Family b) => (a == Family.Cold && b == Family.Volcanic) || (a == Family.Volcanic && b == Family.Cold);

        static Ridge KindOf(Family a, Family b)
        {
            if (a == Family.Volcanic || b == Family.Volcanic) return Ridge.Volcano;
            if (a == Family.Arid && b == Family.Arid) return Ridge.Mesas;
            return Ridge.Mountains;
        }

        // A ridge rises along every border: 1 on the line, falling off over a few widths; passes are gaps where a slow
        // noise along the border drops low, so roads and tunnels (#39) have somewhere to cross.
        static void RidgeField(uint seed, Variant v, Vector2[] sites, Family[] fam, Ridge[] kind, float[] amt)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d1 = 1e9f, d2 = 1e9f; int a = 0, b = 0;
                    for (int s = 0; s < sites.Length; s++)
                    {
                        float d = RealmDistance(sites[s], x, y, seed, s);
                        if (d < d1) { d2 = d1; b = a; d1 = d; a = s; } else if (d < d2) { d2 = d; b = s; }
                    }
                    float gap = (d2 - d1) / (N * v.ridgeWidth);
                    float pass = Mathf.SmoothStep(0.25f, 0.55f, Fbm(seed + 91, x / 110f, y / 110f, 2));
                    float a2 = Mathf.Clamp01(1.4f - gap * 0.7f) * (gap < 2f ? 1f : 0f) * pass;
                    if (a2 > 0.01f) { kind[y * N + x] = KindOf(fam[a], fam[b]); amt[y * N + x] = a2; }
                }
        }

        static List<Vector2Int> VolcanoSpots(Ridge[] kind, float[] amt, float[] h, bool[] land, uint seed)
        {
            var spots = new List<Vector2Int>();
            float best = 0; Vector2Int bp = default;
            for (int y = 0; y < N; y += 2)
                for (int x = 0; x < N; x += 2)
                {
                    int i = y * N + x;
                    if (kind[i] != Ridge.Volcano || !land[i]) continue;
                    float score = amt[i] * (0.6f + 0.4f * H01(seed + 99, x / 16, y / 16)) + 0.001f * h[i];
                    if (Vector2.Distance(new Vector2(x, y), new Vector2(N / 2, N / 2)) < N * 0.2f) continue;
                    if (score > best) { best = score; bp = new Vector2Int(x, y); }
                }
            if (best > 0) spots.Add(bp);
            return spots;
        }

        // Priority-flood from the sea: every land cell gets the lowest height a river could reach it at, with a tiny slope
        // across flats and pits, so steepest descent on the result always reaches the coast.
        static float[] Fill(float[] h, bool[] land)
        {
            var f = new float[N * N]; var done = new bool[N * N];
            var open = new SortedSet<(float, int)>();
            for (int i = 0; i < N * N; i++)
            {
                bool edge = false;
                if (land[i])
                {
                    int x = i % N, y = i / N;
                    for (int dy = -1; dy <= 1 && !edge; dy++) for (int dx = -1; dx <= 1; dx++)
                    { int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N || !land[yy * N + xx]) { edge = true; break; } }
                }
                if (!land[i] || edge) { f[i] = h[i]; if (land[i]) { done[i] = true; open.Add((f[i], i)); } }
            }
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top);
                int x = top.Item2 % N, y = top.Item2 / N;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue;
                    int j = yy * N + xx; if (done[j] || !land[j]) continue;
                    done[j] = true; f[j] = Mathf.Max(h[j], top.Item1 + 1e-5f); open.Add((f[j], j));
                }
            }
            return f;
        }

        // Steepest descent with a little momentum; a pit is crossed by taking the lowest cell within a few pixels.
        static void Descend(Vector2Int start, float[] h, bool[] land, int[] flow)
        {
            var seen = new HashSet<int>();
            var p = start;
            for (int step = 0; step < 6000; step++)
            {
                int i = p.y * N + p.x;
                if (!land[i]) return;
                flow[i]++; seen.Add(i);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int xx = p.x + dx, yy = p.y + dy; if (xx >= 0 && xx < N && yy >= 0 && yy < N) { flow[yy * N + xx] = Mathf.Max(flow[yy * N + xx], flow[i] > 5 ? 1 : 0); } }
                Vector2Int next = p; float best = h[i] - 1e-5f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = p.x + dx, yy = p.y + dy; if (xx < 1 || xx >= N - 1 || yy < 1 || yy >= N - 1) continue;
                        int j = yy * N + xx; if (seen.Contains(j)) continue;
                        if (h[j] < best) { best = h[j]; next = new Vector2Int(xx, yy); }
                    }
                if (next == p)
                {
                    best = 1e9f;
                    for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++)
                    {
                        int xx = p.x + dx, yy = p.y + dy; if (xx < 1 || xx >= N - 1 || yy < 1 || yy >= N - 1) continue;
                        int j = yy * N + xx; if (seen.Contains(j)) continue;
                        if (h[j] < best) { best = h[j]; next = new Vector2Int(xx, yy); }
                    }
                    if (next == p) return;
                }
                p = next;
            }
        }
    }
}
