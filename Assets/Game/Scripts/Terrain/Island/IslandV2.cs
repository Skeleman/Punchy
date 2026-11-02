using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Generator version 2 (#23, layout B: five realms). Version 1 (the shipping island) stays the default; run the player
    /// with <c>-island v2</c> (or set the environment variable PUNCHY_ISLAND=v2 before entering play in the editor) and
    /// <see cref="IslandV2System"/> bakes a second world at launch from the same biome assets: a realm pass divides the
    /// island into five climate families, each biome region takes a biome of its realm's family, border ridges rise where
    /// two realms meet (with gaps left as passes), and a lake bowl is pressed in beside the spawn. Rivers, landmarks,
    /// settlements and caves then bake over it exactly as in version 1.
    /// </summary>
    public static class IslandV2
    {
        public const int Version = 2;
        const int N = 512;                 // grid cells per side
        const float PX = 1024f;            // the design grid the constants below are written for
        const float WorldSizeM = 9216f;
        const float LakeShiftM = 1700f;    // lake centre east of the origin, so the spawn stands on its shore

        enum Family { Arid, Wet, Cold, Volcanic, Temperate }
        enum Ridge { None, Mesas, Mountains, Volcano }

        static readonly Dictionary<Family, string[]> BiomeNames = new Dictionary<Family, string[]>
        {
            { Family.Arid, new[] { "Dunes", "SaltFlat", "Sonoran", "GlassDesert", "MesaCanyonlands" } },
            { Family.Wet, new[] { "FoggyMarsh", "PoisonSwamp", "FungalForest", "SparklingForest" } },
            { Family.Cold, new[] { "Glacier", "Tundra", "Mountains" } },
            { Family.Volcanic, new[] { "Ashlands", "EmberForest", "ElectricWastelands" } },
            { Family.Temperate, new[] { "Temperate", "Grassland" } },
        };

        public static bool Requested()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-island" && args[i + 1].ToLowerInvariant() == "v2")
                    return true;
            return Environment.GetEnvironmentVariable("PUNCHY_ISLAND") == "v2";
        }

        public struct Result
        {
            public BlobAssetReference<BiomeMapBlob> Map;
            public BlobAssetReference<HeightmapBlob> Island;
            public string Report;
        }

        // ---- scrambled integer hashes (a plain hash of small ints has no avalanche) ----
        static uint Scramble(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
        static uint H(uint seed, int a, int b) => Scramble(seed * 0x9E3779B1u ^ Scramble((uint)a * 0x85ebca6bu + (uint)b * 0xc2b2ae35u + 0x27d4eb2fu));
        static float H01(uint seed, int a, int b) => (H(seed, a, b) & 0xFFFFFF) / 16777216f;
        static float Smooth(float t) => t * t * (3 - 2 * t);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Value(uint seed, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            return Lerp(Lerp(H01(seed, ix, iy), H01(seed, ix + 1, iy), fx), Lerp(H01(seed, ix, iy + 1), H01(seed, ix + 1, iy + 1), fx), fy);
        }

        static float Fbm(uint seed, float x, float y, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += amp * Value(seed + (uint)i * 17, x, y); norm += amp; x *= 2.03f; y *= 2.03f; amp *= 0.5f; }
            return sum / norm;
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

        static bool Forbidden(Family a, Family b) => (a == Family.Cold && b == Family.Volcanic) || (a == Family.Volcanic && b == Family.Cold);

        static Ridge KindOf(Family a, Family b)
        {
            if (a == Family.Volcanic || b == Family.Volcanic) return Ridge.Volcano;
            if (a == Family.Arid && b == Family.Arid) return Ridge.Mesas;
            return Ridge.Mountains;
        }

        // Coast: a wobbly disc, 1 at the centre falling to 0 at the shore (negative in the sea). x, y in design px.
        static float Coast(uint seed, float x, float y, float wobble)
        {
            float dx = (x - PX * 0.5f) / (PX * 0.5f), dy = (y - PX * 0.5f) / (PX * 0.5f);
            float r = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx);
            float w = Fbm(seed + 3, Mathf.Cos(ang) * 1.6f + 5, Mathf.Sin(ang) * 1.6f + 5, 3) - 0.5f;
            float local = (Fbm(seed + 4, x / 140f, y / 140f, 3) - 0.5f) * wobble * 0.7f;
            return 0.88f - r + w * wobble * 2f + local;
        }

        static Vector2 Warped(uint seed, float x, float y) =>
            new Vector2(x + (Fbm(seed + 71, x / 260f, y / 260f) - 0.5f) * 0.28f * PX, y + (Fbm(seed + 73, x / 260f, y / 260f) - 0.5f) * 0.28f * PX);

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

        // The two nearest realms at a warped point: ids and distances.
        static void Nearest(Vector2[] sites, Vector2 p, out int a, out int b, out float d1, out float d2)
        {
            d1 = d2 = 1e9f; a = b = 0;
            for (int s = 0; s < sites.Length; s++)
            {
                float d = Vector2.Distance(p, sites[s] * PX);
                if (d < d1) { d2 = d1; b = a; d1 = d; a = s; } else if (d < d2) { d2 = d; b = s; }
            }
        }

        /// <summary>Builds the biome map and the height offsets (layout B: five realms, seed 202). Palette is the game's biome palette.</summary>
        public static Result Generate(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, Allocator allocator)
        {
            const int realms = 5; const uint baseSeed = 202; const float lakeRadius = 0.13f, ridgeWidth = 0.016f, coastWobble = 0.14f;
            uint seed = baseSeed;

            // Realm sites; pick over a few tries so that cold and volcanic realms are never neighbours. Scored on a coarse grid.
            Vector2[] sites = null; Family[] fam = null; int bestBad = int.MaxValue;
            for (uint attempt = 0; attempt < 80 && bestBad > 0; attempt++)
            {
                uint s = baseSeed + attempt * 7919;
                var sx = new Vector2[realms]; var fm = Shuffled(realms, s);
                float turn = H01(s, 1, 1) * 6.2832f;
                for (int i = 0; i < realms; i++)
                {
                    float a = turn + (i + 0.4f * (H01(s, 2, i) - 0.5f)) / realms * 6.2832f;
                    float r = 0.24f + 0.12f * H01(s, 3, i);
                    sx[i] = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r);
                }
                const int G = 96; var map = new int[G, G];
                for (int y = 0; y < G; y++) for (int x = 0; x < G; x++) { Nearest(sx, Warped(s, (x + 0.5f) / G * PX, (y + 0.5f) / G * PX), out map[y, x], out _, out _, out _); }
                int bad = 0;
                for (int y = 0; y < G - 1; y++) for (int x = 0; x < G - 1; x++)
                {
                    int a = map[y, x];
                    if (map[y, x + 1] != a && Forbidden(fm[a], fm[map[y, x + 1]])) bad++;
                    if (map[y + 1, x] != a && Forbidden(fm[a], fm[map[y + 1, x]])) bad++;
                }
                if (bad < bestBad) { bestBad = bad; sites = sx; fam = fm; seed = s; }
            }

            // Palette ids by name; a family biome the palette lacks is skipped (a family with none falls back to biome 0).
            var ids = new Dictionary<string, int>();
            for (int i = 0; i < palette.Count; i++) if (palette[i] != null) ids[palette[i].name] = i;
            int oceanId = hasOcean ? palette.Count - 1 : 0;
            var pick = new Dictionary<Family, int[]>();
            var missing = new List<string>();
            foreach (var kv in BiomeNames)
            {
                var list = new List<int>();
                foreach (var n in kv.Value) { if (ids.TryGetValue(n, out var id) && id != oceanId) list.Add(id); else missing.Add(n); }
                if (list.Count == 0) list.Add(0);
                pick[kv.Key] = list.ToArray();
            }
            int Named(string n, int fallback) => ids.TryGetValue(n, out var id) ? id : fallback;

            var biomeIds = new byte[N * N];
            var offset = new float[N * N];
            float cx0 = PX * 0.5f + LakeShiftM / WorldSizeM * PX + (H01(seed, 8, 8) - 0.5f) * 40f, cy0 = PX * 0.5f + (H01(seed, 9, 9) - 0.5f) * 40f;
            float lakeR = lakeRadius * PX;
            int ridgeCells = 0, lakeCells = 0, landCells = 0;
            var lineHeights = new List<float>();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float px = (x + 0.5f) / N * PX, py = (y + 0.5f) / N * PX;
                    int i = y * N + x;
                    float coast = Coast(seed, px, py, coastWobble);
                    if (coast <= 0) { biomeIds[i] = (byte)oceanId; continue; }
                    landCells++;
                    var wp = Warped(seed, px, py);
                    Nearest(sites, wp, out int ra, out int rb, out float d1, out float d2);
                    var f = fam[ra];
                    var list = pick[f];
                    float wx = px + (Fbm(seed + 31, px / 90f, py / 90f) - 0.5f) * 90f, wy = py + (Fbm(seed + 37, px / 90f, py / 90f) - 0.5f) * 90f;
                    int cell = NearestCell(seed + 5, wx / 110f, wy / 110f);
                    int id = list[(int)(H(seed + 11, cell, ra) % (uint)list.Length)];

                    // Border ridge: 1 on the line, falling off over a few widths; passes are gaps where slow noise drops low.
                    float gap = (d2 - d1) / (PX * ridgeWidth);
                    float pass = Mathf.Lerp(0.3f, 1f, Smooth(Mathf.Clamp01((Fbm(seed + 91, px / 110f, py / 110f, 2) - 0.3f) / 0.25f)));   // W-08: full ridge most places, low saddles (0.3) where the slow noise is low
                    float amt = Mathf.Clamp01(1.4f - gap * 0.7f) * (gap < 2f ? 1f : 0f) * pass;
                    float h = 0f;
                    if (amt > 0.01f)
                    {
                        var kind = KindOf(f, fam[rb]);
                        float peak = kind == Ridge.Volcano ? 170f : kind == Ridge.Mesas ? 70f : 110f;
                        // W-08 (#20): a chain, not a wall: peaks and saddles along the line from a ridged noise (1 at crests, 0 in saddles).
                        float ridged = 1f - Mathf.Abs(2f * Fbm(seed + 97, px / 55f, py / 55f, 3) - 1f);
                        h += peak * (0.5f + 0.5f * ridged * ridged) * Smooth(amt) * Mathf.Clamp01(coast / 0.12f);
                        if (amt > 0.45f)
                        {
                            id = kind == Ridge.Volcano ? Named("Ashlands", id) : kind == Ridge.Mesas ? Named("MesaCanyonlands", id) : Named("Mountains", id);
                            ridgeCells++;
                        }
                    }
                    if (gap < 0.35f && coast > 0.12f) lineHeights.Add(h);   // along the border line, passes included
                    // Lake bowl.
                    float rl = Mathf.Sqrt((px - cx0) * (px - cx0) + (py - cy0) * (py - cy0)) * (0.85f + 0.3f * Fbm(seed + 53, px / 120f, py / 120f));
                    float bowl = 1f - Smooth(Mathf.Clamp01(rl / (lakeR * 1.8f)));
                    h -= 38f * bowl;
                    if (rl < lakeR) lakeCells++;
                    biomeIds[i] = (byte)id; offset[i] = h;
                }

            // Blobs. The map's row 0 is the south edge (z = origin), like the biome map images.
            var origin = new float2(-WorldSizeM * 0.5f);
            var b1 = new BlobBuilder(Allocator.Temp);
            ref var map1 = ref b1.ConstructRoot<BiomeMapBlob>();
            map1.Resolution = N; map1.Origin = origin; map1.WorldSize = WorldSizeM;
            var arr = b1.Allocate(ref map1.Ids, N * N);
            for (int i = 0; i < N * N; i++) arr[i] = biomeIds[i];
            var mapBlob = b1.CreateBlobAssetReference<BiomeMapBlob>(allocator); b1.Dispose();

            var b2 = new BlobBuilder(Allocator.Temp);
            ref var hm = ref b2.ConstructRoot<HeightmapBlob>();
            hm.Resolution = N; hm.Origin = origin; hm.WorldSize = WorldSizeM; hm.MinHeight = -60f; hm.HeightStep = 260f / 65535f;
            var harr = b2.Allocate(ref hm.Heights, N * N);
            for (int i = 0; i < N * N; i++) harr[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt((offset[i] + 60f) / hm.HeightStep), 0, 65535);
            var islandBlob = b2.CreateBlobAssetReference<HeightmapBlob>(allocator); b2.Dispose();

            var sb = new System.Text.StringBuilder();
            sb.Append($"generator version {Version}, layout B seed {seed}; realms:");
            for (int i = 0; i < realms; i++) sb.Append($" {fam[i]}({sites[i].x:F2},{sites[i].y:F2})");
            sb.Append($"; forbidden borders {bestBad}; land {landCells} cells, ridge {ridgeCells}, lake {lakeCells} (cell {WorldSizeM / N:F0} m)");
            if (lineHeights.Count > 0) { lineHeights.Sort(); sb.Append($"; border line cells {lineHeights.Count}, ridge height min {lineHeights[0]:F0} p10 {lineHeights[lineHeights.Count / 10]:F0} median {lineHeights[lineHeights.Count / 2]:F0} max {lineHeights[lineHeights.Count - 1]:F0} m"); }
            if (missing.Count > 0) sb.Append("; biomes not in palette: " + string.Join(",", missing));
            return new Result { Map = mapBlob, Island = islandBlob, Report = sb.ToString() };
        }

        /// <summary>Rivers for version 2: wider, with slow smooth bends; they start where water collects (the lake, ridge snow and rock).</summary>
        public static RiverSettings Rivers(RiverSettings v1)
        {
            v1.MinWidth = 9f; v1.MaxWidth = 60f;
            v1.Meander = 0.9f;
            v1.SourceCatchment = 0.25f;
            v1.FullCatchment = 25f;
            v1.MinLakeArea = 0.1f; v1.MinLakeDepth = 4f;
            // W-05b: smooth curved segments, and drops of 4 m or more stay as steps (waterfalls) instead of being spread into gorges.
            v1.Smooth = true; v1.FallDrop = 4f; v1.MaxGradient = 1.5f;
            // W-07 (#18): springs on high ground. A river may only start 45 m or more above the sea (ridges, outcrops, upland lake rims). PUNCHY_SPRINGS=old keeps the W-06 values.
            if (System.Environment.GetEnvironmentVariable("PUNCHY_SPRINGS") != "old")
            {
                v1.SourceHeight = 45f;
            }
            return v1;
        }
    }

    /// <summary>
    /// At launch with <c>-island v2</c>, replaces the baked version 1 terrain with a version 2 bake (see <see cref="IslandV2"/>).
    /// The streaming system notices the changed source and redraws; landmarks print as "[island] ... hash" in the log.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class IslandV2System : SystemBase
    {
        bool done;
        EntityQuery query;

        /// <summary>Source height, mouth height, length and width of the longest rivers, and the waterfall count.</summary>
        static string RiverReport(ref RiverMapBlob map)
        {
            var rows = new List<(float length, string text)>();
            int sources = 0; var srcH = new List<float>();
            for (int i = 0; i < map.Width.Length; i++)
            {
                if (map.Width[i] <= 0f || map.Up[i] >= 0) continue;
                sources++; srcH.Add(map.Bed[i]);
                float length = 0f; int c = i, steps = 0; var p = map.Point(c);
                while (map.Down[c] >= 0 && map.Width[map.Down[c]] > 0f && steps++ < 4000) { var n = map.Down[c]; var q = map.Point(n); length += math.distance(p, q); p = q; c = n; }
                rows.Add((length, $"src {map.Bed[i]:F0} m -> end {map.Bed[c]:F0} m (drop {map.Bed[i] - map.Bed[c]:F0}), {length:F0} m, w {map.Width[i]:F0}-{map.Width[c]:F0}"));
            }
            rows.Sort((a, b) => b.length.CompareTo(a.length));
            srcH.Sort();
            var sb = new System.Text.StringBuilder($"rivers: {sources} sources (bed min {(srcH.Count > 0 ? srcH[0] : 0):F0} median {(srcH.Count > 0 ? srcH[srcH.Count / 2] : 0):F0} max {(srcH.Count > 0 ? srcH[srcH.Count - 1] : 0):F0}), {map.Falls.Length} waterfalls; longest:");
            for (int k = 0; k < math.min(6, rows.Count); k++) sb.Append($" | {rows[k].text}");
            // Falls by drop, and how many sit within 60 m of another (chains of them count as one cluster).
            int[] bucket = new int[4]; int near = 0, clusters = 0;
            var root = new int[map.Falls.Length];
            for (int a = 0; a < root.Length; a++) root[a] = a;
            int Find(int x) { while (root[x] != x) x = root[x] = root[root[x]]; return x; }
            for (int a = 0; a < map.Falls.Length; a++)
            {
                var d = map.Falls[a].Drop;
                bucket[d < 6f ? 0 : d < 8f ? 1 : d < 12f ? 2 : 3]++;
                bool close = false;
                for (int c = 0; c < map.Falls.Length; c++)
                    if (c != a && math.distance(map.Falls[a].Lip.xz, map.Falls[c].Lip.xz) < 60f) { close = true; root[Find(a)] = Find(c); }
                if (close) near++;
            }
            for (int a = 0; a < root.Length; a++) if (Find(a) == a) clusters++;
            sb.Append($" | falls by drop: 4-6 m {bucket[0]}, 6-8 m {bucket[1]}, 8-12 m {bucket[2]}, 12+ m {bucket[3]}; {near} within 60 m of another, {clusters} clusters");
            return sb.ToString();
        }

        protected override void OnCreate()
        {
            query = GetEntityQuery(ComponentType.ReadWrite<TerrainSource>(), ComponentType.ReadOnly<BiomePalette>());
            done = !IslandV2.Requested();
            if (!done) RequireForUpdate(query);
        }

        protected override void OnUpdate()
        {
            if (done) return;
            done = true;
            var entity = query.GetSingletonEntity();
            var source = EntityManager.GetComponentData<TerrainSource>(entity);
            var palette = EntityManager.GetComponentObject<BiomePalette>(entity);
            if (palette.Biomes == null || palette.Biomes.Length == 0 || !source.Biomes.IsCreated) return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var biomes = new List<BiomeDefinition>(palette.Biomes);
            bool hasOcean = source.Biomes.Value.OceanIndex >= 0;
            var gen = IslandV2.Generate(biomes, hasOcean, Allocator.Persistent);
            long genMs = watch.ElapsedMilliseconds;
            source.BiomeKind = BiomeSourceKind.Map;
            source.BiomeMap = gen.Map;
            source.Island = gen.Island;
            source.BiomeCells = default;
            source.RiverSettings = IslandV2.Rivers(source.RiverSettings);
            source.Rivers = default; source.Landmarks = default; source.Settlements = default; source.Caves = default; source.Anchors = default; source.PondMask = default;
            var types = new List<LandmarkType>(palette.Landmarks);
            TerrainAuthoring.BuildPlaces(ref source, biomes, types, palette.Placed ?? new List<PlacedLandmark>(), palette.Roadside ?? new List<RoadsideLandmark>(), Allocator.Persistent);
            EntityManager.SetComponentData(entity, source);
            long placesMs = watch.ElapsedMilliseconds - genMs;

            string hashText = "no landmarks";
            if (source.Landmarks.IsCreated)
            {
                ref var set = ref source.Landmarks.Value;
                var lines = new List<string>();
                for (int i = 0; i < set.Instances.Length; i++)
                {
                    var l = set.Instances[i];
                    var t = l.Type < palette.Landmarks.Length && palette.Landmarks[l.Type] != null ? palette.Landmarks[l.Type].name : l.Type.ToString();
                    lines.Add($"{t} {l.Position.x:F1} {l.Position.y:F1} {l.Scale:F3}");
                }
                lines.Sort(StringComparer.Ordinal);
                uint h = 2166136261u;
                foreach (var line in lines) foreach (var ch in line) h = (h ^ ch) * 16777619u;
                hashText = $"{lines.Count} landmarks, hash {h:X8}";
            }
            Debug.Log($"[island] {gen.Report}; baked in {watch.ElapsedMilliseconds} ms (generate {genMs}, places {placesMs}); {hashText}");
            var report = System.Diagnostics.Stopwatch.StartNew();
            // (Ponds: the "[ponds]" line from BuildPlaces; W-14 dropped the second count here, ~2.8 s.)
            if (source.Rivers.IsCreated) Debug.Log("[island] " + RiverReport(ref source.Rivers.Value));
            Debug.Log($"[island] reports {report.ElapsedMilliseconds} ms; v2 ready at {UnityEngine.Time.realtimeSinceStartup:F1} s since start");
        }
    }
}
