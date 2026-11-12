using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Generator version 2 (#23, layout B: five realms). Version 1 (the first island) is now opt-in (W-22: v2 is the default); run the player
    /// with <c>-island v1</c> (or set the environment variable PUNCHY_ISLAND=v1 before entering play in the editor) for v1. Otherwise
    /// <see cref="IslandV2System"/> bakes a second world at launch from the same biome assets: a realm pass divides the
    /// island into five climate families, each biome region takes a biome of its realm's family, border ridges rise where
    /// two realms meet (with gaps left as passes), and a lake bowl is pressed in beside the spawn. Rivers, landmarks,
    /// settlements and caves then bake over it exactly as in version 1.
    /// </summary>
    public static class IslandV2
    {
        public const int Version = 2;
        internal const int N = 512;                 // grid cells per side
        internal const float PX = 1024f;            // the design grid the constants below are written for
        internal const float WorldSizeM = 9216f;
        internal const float LakeShiftM = 1700f;    // lake centre east of the origin, so the spawn stands on its shore

        internal enum Family { Arid, Wet, Cold, Volcanic, Temperate }
        internal enum Ridge { None, Mesas, Mountains, Volcano }

        /// <summary>
        /// Each realm family's palette ids, from the biomes' Family tag in FamilyOrder (W-116; was a list of names). The
        /// ocean is left out; a family with no biome falls back to biome 0 and is added to <paramref name="missing"/>.
        /// </summary>
        internal static Dictionary<Family, int[]> FamilyPicks(IReadOnlyList<BiomeDefinition> palette, int oceanId, List<string> missing = null)
        {
            var pick = new Dictionary<Family, int[]>();
            foreach (Family f in System.Enum.GetValues(typeof(Family)))
            {
                var list = BiomeTags.InFamily(palette, (BiomeFamily)((int)f + 1));
                list.Remove(oceanId);
                if (list.Count == 0) { list.Add(0); missing?.Add(f.ToString()); }
                pick[f] = list.ToArray();
            }
            return pick;
        }

        /// <summary>
        /// W-22: whether this launch bakes through IslandV2System (versions 2 and 3; v3 is the default since W-111). Only
        /// <c>-island v1</c> (or PUNCHY_ISLAND=v1) asks for version 1; the command line wins over the environment.
        /// </summary>
        public static bool Requested() => RequestedVersion() != 1;

        /// <summary>
        /// W-68 (v3 B1): the island version this launch asks for: 1, 2, or 3 (the default since W-111, and any other value). Read
        /// the same way as above: <c>-island v1|v2|v3</c> wins over PUNCHY_ISLAND (which the editor menu
        /// Punchy/Island sets inside the editor). Versions 2 and 3 both bake through <see cref="IslandV2System"/>.
        /// </summary>
        public static int RequestedVersion()
        {
            string v = null;
            // W-92 (#88): the test flags, so in the editor "-island v3" in Temp/PlayArgs.txt picks v3 too (before, only
            // the env did, and "-island v3 -lakeshelf" there gave a v2 island with no shelf).
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            for (int i = 0; i < args.Length - 1 && v == null; i++)
                if (args[i] == "-island")
                    v = args[i + 1];
            v = (v ?? Environment.GetEnvironmentVariable("PUNCHY_ISLAND"))?.ToLowerInvariant();
            // W-111: v3 is the default island now; v1 and v2 stay reachable by name.
            return v == "v1" ? 1 : v == "v2" ? Version : IslandV3.Version;
        }

        /// <summary>
        /// W-74: whether the v3 bake ends with its audit reports (V3Audit, V3Ranges.Checks, V3Ground.Report, ~17 s).
        /// Only on request: <c>-islandaudit</c> on the command line or PUNCHY_ISLANDAUDIT=1 (the editor menu
        /// Punchy/Island/Audit Reports and the world probe set it). Releases and joins skip them.
        /// </summary>
        public static bool AuditRequested() =>
            Array.IndexOf(Environment.GetCommandLineArgs(), "-islandaudit") >= 0
            || Environment.GetEnvironmentVariable("PUNCHY_ISLANDAUDIT") == "1";

        /// <summary>
        /// P-2 (v3 B14, brief 8B): the island version the host sent this joined client (WorldSync's IslandVersionRpc;
        /// 0 = none yet). A joined client's <see cref="IslandV2System"/> waits for it and bakes that version instead of
        /// its own launch's, so a v2-launched client joins a v3 host on the v3 island. Solo and hosting ignore it: the
        /// launch flag decides.
        /// </summary>
        public static int HostVersion;

        /// <summary>The version this process's island was baked (or kept) at: 0 until then. The world fingerprint reads it.</summary>
        public static int ActiveVersion;

        public struct Result
        {
            public BlobAssetReference<BiomeMapBlob> Map;
            public BlobAssetReference<HeightmapBlob> Island;
            /// <summary>Island v3 only: blend radius and geology per pixel (unset for v2).</summary>
            public BlobAssetReference<V3GroundBlob> V3Ground;
            public string Report;
        }

        // ---- scrambled integer hashes (a plain hash of small ints has no avalanche) ----
        internal static uint Scramble(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
        internal static uint H(uint seed, int a, int b) => Scramble(seed * 0x9E3779B1u ^ Scramble((uint)a * 0x85ebca6bu + (uint)b * 0xc2b2ae35u + 0x27d4eb2fu));
        internal static float H01(uint seed, int a, int b) => (H(seed, a, b) & 0xFFFFFF) / 16777216f;
        internal static float Smooth(float t) => t * t * (3 - 2 * t);
        internal static float Lerp(float a, float b, float t) => a + (b - a) * t;

        internal static float Value(uint seed, float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = Smooth(x - ix), fy = Smooth(y - iy);
            return Lerp(Lerp(H01(seed, ix, iy), H01(seed, ix + 1, iy), fx), Lerp(H01(seed, ix, iy + 1), H01(seed, ix + 1, iy + 1), fx), fy);
        }

        internal static float Fbm(uint seed, float x, float y, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += amp * Value(seed + (uint)i * 17, x, y); norm += amp; x *= 2.03f; y *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }

        internal static Family[] Shuffled(int count, uint seed)
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

        internal static bool Forbidden(Family a, Family b) => (a == Family.Cold && b == Family.Volcanic) || (a == Family.Volcanic && b == Family.Cold);

        internal static Ridge KindOf(Family a, Family b)
        {
            if (a == Family.Volcanic || b == Family.Volcanic) return Ridge.Volcano;
            if (a == Family.Arid && b == Family.Arid) return Ridge.Mesas;
            return Ridge.Mountains;
        }

        // Coast: a wobbly disc, 1 at the centre falling to 0 at the shore (negative in the sea). x, y in design px.
        internal static float Coast(uint seed, float x, float y, float wobble)
        {
            float dx = (x - PX * 0.5f) / (PX * 0.5f), dy = (y - PX * 0.5f) / (PX * 0.5f);
            float r = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx);
            float w = Fbm(seed + 3, Mathf.Cos(ang) * 1.6f + 5, Mathf.Sin(ang) * 1.6f + 5, 3) - 0.5f;
            float local = (Fbm(seed + 4, x / 140f, y / 140f, 3) - 0.5f) * wobble * 0.7f;
            return 0.88f - r + w * wobble * 2f + local;
        }

        internal static Vector2 Warped(uint seed, float x, float y) =>
            new Vector2(x + (Fbm(seed + 71, x / 260f, y / 260f) - 0.5f) * 0.28f * PX, y + (Fbm(seed + 73, x / 260f, y / 260f) - 0.5f) * 0.28f * PX);

        internal static int NearestCell(uint seed, float x, float y)
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
        internal static void Nearest(Vector2[] sites, Vector2 p, out int a, out int b, out float d1, out float d2)
        {
            d1 = d2 = 1e9f; a = b = 0;
            for (int s = 0; s < sites.Length; s++)
            {
                float d = Vector2.Distance(p, sites[s] * PX);
                if (d < d1) { d2 = d1; b = a; d1 = d; a = s; } else if (d < d2) { d2 = d; b = s; }
            }
        }

        internal const int Realms = 5;

        /// <summary>
        /// Realm sites and families (seed 202); picked over a few tries so that cold and volcanic realms are never
        /// neighbours, scored on a coarse grid. W-69: split out of <see cref="Generate"/> so v3's ranges read the same layout.
        /// </summary>
        internal static void PickRealms(out Vector2[] sites, out Family[] fam, out uint seed, out int bestBad)
        {
            // W-103: the layout only depends on constants, and v3 asks for it twice per bake: picked once per process,
            // callers get their own copies of the arrays.
            if (pickedSites == null)
            {
                PickRealmsOnce(out var s, out var f, out pickedSeed, out pickedBad);
                pickedFam = f; pickedSites = s;
            }
            sites = (Vector2[])pickedSites.Clone(); fam = (Family[])pickedFam.Clone(); seed = pickedSeed; bestBad = pickedBad;
        }

        static Vector2[] pickedSites; static Family[] pickedFam; static uint pickedSeed; static int pickedBad;

        static void PickRealmsOnce(out Vector2[] sites, out Family[] fam, out uint seed, out int bestBad)
        {
            const int realms = Realms; const uint baseSeed = 202;
            seed = baseSeed; sites = null; fam = null; bestBad = int.MaxValue;
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
        }

        /// <summary>The report's layout part: version, seed, realms and forbidden borders (W-103: v3 prints it without the v2 pass).</summary>
        internal static string Layout()
        {
            PickRealms(out var sites, out var fam, out uint seed, out int bestBad);
            var sb = new System.Text.StringBuilder();
            sb.Append($"generator version {Version}, layout B seed {seed}; realms:");
            for (int i = 0; i < Realms; i++) sb.Append($" {fam[i]}({sites[i].x:F2},{sites[i].y:F2})");
            sb.Append($"; forbidden borders {bestBad}");
            return sb.ToString();
        }

        /// <summary>Builds the biome map and the height offsets (layout B: five realms, seed 202). Palette is the game's biome palette.</summary>
        public static Result Generate(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, Allocator allocator)
        {
            const int realms = Realms; const float lakeRadius = 0.13f, ridgeWidth = 0.016f, coastWobble = 0.14f;
            PickRealms(out var sites, out var fam, out uint seed, out int bestBad);

            // Palette ids by family tag (a family with none falls back to biome 0); ridges by their ridge tag.
            int oceanId = hasOcean ? palette.Count - 1 : 0;
            var missing = new List<string>();
            var pick = FamilyPicks(palette, oceanId, missing);
            int ridgeVolcano = BiomeTags.First(palette, BiomeTags.RidgeVolcano), ridgeMesa = BiomeTags.First(palette, BiomeTags.RidgeMesa),
                ridgeMountain = BiomeTags.First(palette, BiomeTags.RidgeMountain);
            int Tagged(int tagged, int fallback) => tagged >= 0 ? tagged : fallback;

            var biomeIds = new byte[N * N];
            var offset = new float[N * N];
            float cx0 = PX * 0.5f + LakeShiftM / WorldSizeM * PX + (H01(seed, 8, 8) - 0.5f) * 40f, cy0 = PX * 0.5f + (H01(seed, 9, 9) - 0.5f) * 40f;
            float lakeR = lakeRadius * PX;
            // W-103: rows run in parallel (every cell reads only the seed and the realms); counts and border heights are
            // kept per row and summed in row order, so the result is identical to the old single-thread loop.
            var rowRidge = new int[N]; var rowLake = new int[N]; var rowLand = new int[N];
            var rowLines = new List<float>[N];
            BakeParallel.For(N, y =>
            {
                int ridgeCells = 0, lakeCells = 0, landCells = 0;
                var lineHeights = rowLines[y] = new List<float>();
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
                            id = kind == Ridge.Volcano ? Tagged(ridgeVolcano, id) : kind == Ridge.Mesas ? Tagged(ridgeMesa, id) : Tagged(ridgeMountain, id);
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
                rowRidge[y] = ridgeCells; rowLake[y] = lakeCells; rowLand[y] = landCells;
            });
            int ridgeCells = 0, lakeCells = 0, landCells = 0;
            var lineHeights = new List<float>();
            for (int y = 0; y < N; y++)
            {
                ridgeCells += rowRidge[y]; lakeCells += rowLake[y]; landCells += rowLand[y];
                lineHeights.AddRange(rowLines[y]);
            }

            // Blobs. The map's row 0 is the south edge (z = origin), like the biome map images.
            var origin = new float2(-WorldSizeM * 0.5f);
            var b1 = new BlobBuilder(Allocator.Temp);
            ref var map1 = ref b1.ConstructRoot<BiomeMapBlob>();
            map1.Resolution = N; map1.Origin = origin; map1.WorldSize = WorldSizeM;
            b1.Construct(ref map1.Ids, biomeIds);
            var mapBlob = b1.CreateBlobAssetReference<BiomeMapBlob>(allocator); b1.Dispose();

            var b2 = new BlobBuilder(Allocator.Temp);
            ref var hm = ref b2.ConstructRoot<HeightmapBlob>();
            hm.Resolution = N; hm.Origin = origin; hm.WorldSize = WorldSizeM; hm.MinHeight = -60f; hm.HeightStep = 260f / 65535f;
            var harr = b2.Allocate(ref hm.Heights, N * N);
            for (int i = 0; i < N * N; i++) harr[i] = (ushort)Mathf.Clamp(Mathf.RoundToInt((offset[i] + 60f) / hm.HeightStep), 0, 65535);
            var islandBlob = b2.CreateBlobAssetReference<HeightmapBlob>(allocator); b2.Dispose();

            var sb = new System.Text.StringBuilder(Layout());
            sb.Append($"; land {landCells} cells, ridge {ridgeCells}, lake {lakeCells} (cell {WorldSizeM / N:F0} m)");
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
            version = IslandV2.RequestedVersion();
            v2 = version != 1;
            IslandV2.ActiveVersion = 0;
            RequireForUpdate(query);
        }

        protected override void OnDestroy()
        {
            if (shares) Shared.Release();
            shares = false;
        }

        bool v2;
        bool shares;   // W-73: this world holds the shared bake's blobs (released in OnDestroy)
        int version;   // 2, or 3 for the v3 path (IslandV3), which also ends with the water audit
        bool decided;
        float waitingSince = -1f;
        /// <summary>How long a joined client waits for the host's island version before baking its own (s).</summary>
        const float HostWait = 15f;

        /// <summary>
        /// P-2: a joined client (a client world with no server in this process) takes the host's island version,
        /// waiting up to <see cref="HostWait"/> for it; false while it's still waiting. Terrain streaming rebuilds its
        /// chunks when the TerrainSource changes, so the few frames of the subscene's own bake before this are redrawn.
        /// </summary>
        bool DecideVersion()
        {
            if (decided)
                return true;
            var joined = (World.Flags & WorldFlags.GameClient) == WorldFlags.GameClient;
            foreach (var w in World.All)
                if (w.IsCreated && (w.Flags & WorldFlags.GameServer) == WorldFlags.GameServer)
                    joined = false;
            if (joined)
            {
                var host = IslandV2.HostVersion;
                if (host == 0)
                {
                    if (waitingSince < 0f)
                        waitingSince = UnityEngine.Time.realtimeSinceStartup;
                    if (UnityEngine.Time.realtimeSinceStartup - waitingSince < HostWait)
                        return false;
                    Debug.LogWarning($"[island] no island version from the host after {HostWait:F0} s; baking this launch's v{version} ({World.Name})");
                }
                else if (host != version)
                {
                    Debug.Log($"[island] host runs island v{host}; this launch asked for v{version}: switching to v{host} ({World.Name})");
                    version = host;
                }
                else
                    Debug.Log($"[island] host runs island v{host}, the same as this launch ({World.Name})");
                v2 = version != 1;
            }
            decided = true;
            return true;
        }

        /// <summary>
        /// The landmark count and fingerprint hash: each instance as "type x z scale", sorted, FNV-1a. Logged for both
        /// versions so every player run can show the seeds held (W-16: version 1 used to log nothing).
        /// </summary>
        public static string LandmarkHash(ref TerrainSource source, IReadOnlyList<LandmarkType> types)
        {
            if (!source.Landmarks.IsCreated)
                return "no landmarks";
            ref var set = ref source.Landmarks.Value;
            var lines = new List<string>();
            for (int i = 0; i < set.Instances.Length; i++)
            {
                var l = set.Instances[i];
                var t = l.Type < types.Count && types[l.Type] != null ? types[l.Type].name : l.Type.ToString();
                lines.Add($"{t} {l.Position.x:F1} {l.Position.y:F1} {l.Scale:F3}");
            }
            lines.Sort(StringComparer.Ordinal);
            uint h = 2166136261u;
            foreach (var line in lines) foreach (var ch in line) h = (h ^ ch) * 16777619u;
            return $"{lines.Count} landmarks, hash {h:X8}";
        }

        /// <summary>
        /// The landmark types the bake places: the palette's list, the biome looks, then the code-loaded village types
        /// (fishing village #36, turtle village #38, sea folk village and landing #37), should the baked palette predate
        /// them. Those are also appended to the palette itself (plants and WorldSync.Signature read its list) while no
        /// look was added. W-74: every world calls this, the ones sharing the bake too, so all palettes match.
        /// </summary>
        public static List<LandmarkType> LandmarkTypes(BiomePalette palette)
        {
            var types = new List<LandmarkType>(palette.Landmarks);
            LandmarkBaking.AddLooks(types);
            foreach (var extra in new[] { FishingVillageBaking.LoadType(), TurtleVillageBaking.LoadType(), SeaFolkBaking.LoadVillage(), SeaFolkBaking.LoadLanding(), RibcageBaking.LoadType(), MarshFolkBaking.LoadType() })
                // W-140 row 4: matched by name, not object. In a player build Resources.Load returns other objects than
                // the subscene palette's for the same asset, which appended all six again (72 types, not 66).
                if (extra != null && !types.Exists(t => t != null && t.name == extra.name))
                {
                    types.Add(extra);
                    if (palette.Landmarks.Length == types.Count - 1)
                        palette.Landmarks = types.ToArray();
                }
            return types;
        }

        protected override void OnUpdate()
        {
            if (done || !DecideVersion()) return;
            var entity = query.GetSingletonEntity();
            var source = EntityManager.GetComponentData<TerrainSource>(entity);
            var palette = EntityManager.GetComponentObject<BiomePalette>(entity);
            if (!v2)
            {
                // Version 1 keeps its baked terrain: just report the landmarks once they've loaded.
                if (!source.Landmarks.IsCreated || palette.Landmarks == null) return;
                done = true;
                IslandV2.ActiveVersion = 1;
                Debug.Log($"[island] generator version 1 (baked); {LandmarkHash(ref source, palette.Landmarks)}");
                Debug.Log(BatFolkBaking.Report(source));
                Debug.Log(IcebergBaking.Report(source));
                return;
            }
            done = true;
            if (palette.Biomes == null || palette.Biomes.Length == 0 || !source.Biomes.IsCreated) return;

            // #23 (W-27b): one bake per process. A host has a server and a client world (and hosting from solo
            // replaces the solo world with both), each with its own TerrainSource; baking in each blocked the main
            // thread ~4 s per world, and the host's own connection timed out. Later worlds take the first bake's
            // blobs (Persistent, never disposed, read-only once baked). P-2: kept per version, so a client that
            // switches to the host's version rebakes rather than taking another version's island.
            if (Shared.Has(version))
            {
                Shared.ApplyTo(ref source, version);
                EntityManager.SetComponentData(entity, source);
                shares = true; Shared.Users++;
                IslandV2.ActiveVersion = version;
                // W-74 (0c): this world's palette needs the same appended types as the baking world's, or its
                // landmark names (WorldSync.Signature) differ and the host refuses its own client.
                // #102: the hash names types from that list, as the bake's does, so both paths print the same value.
                var sharedTypes = LandmarkTypes(palette);
                // W-79b: this world's plants are built again (a host's client world after its server's), and the
                // first build took the pregrown parts: grow them again on worker threads, not ~4.6 s on the main thread.
                var regrown = Punchy.Flora.FloraSystem.Pregrow(palette);
                Debug.Log($"[island] v{version} shared with {World.Name} from this process's bake (no rebake); {LandmarkHash(ref source, sharedTypes)}; {regrown} plant variants pregrown");
                return;
            }

            // #102: the plants depend on the palette, not the island, so they grow on worker threads beside the bake.
            var types = LandmarkTypes(palette);
            var pregrown = Punchy.Flora.FloraSystem.Pregrow(palette);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // W-140: the island cache. A key match loads the saved island instead of baking; anything else (no file,
            // a changed key, an unreadable file) bakes fresh and rewrites the file. The key is made from the source
            // as the subscene gave it, before the bake changes it.
            IslandCache.LastInputs = source;
            string key = IslandCache.Key(ref source, palette, types, version, out string reason);
            bool hit = key != null && IslandCache.TryLoad(ref source, version, key, out reason);
            string bakeText;
            if (hit)
                bakeText = $"v{version} loaded from the island cache in {watch.ElapsedMilliseconds} ms";
            else
            {
                var baked = Bake(ref source, palette, types, version);
                bakeText = $"{baked.Report}; baked in {baked.TotalMs} ms (generate {baked.GenerateMs}, places {baked.TotalMs - baked.GenerateMs}: grand falls {baked.FallsMs}, archipelago {baked.ArchipelagoMs})";
                if (key != null) IslandCache.Save(ref source, version, key);
            }
            Debug.Log($"[island] cache {(hit ? "hit" : "miss")} {reason} {watch.ElapsedMilliseconds} ms");
            EntityManager.SetComponentData(entity, source);
            Shared.Keep(source, version);
            shares = true; Shared.Users++;
            IslandV2.ActiveVersion = version;

            string hashText = LandmarkHash(ref source, types) + $"; {pregrown} plant variants pregrown";
            Debug.Log($"[island] {bakeText}; {hashText}");
            var report = System.Diagnostics.Stopwatch.StartNew();
            // W-140: each island blob's serialised size (the island cache's file size).
            Debug.Log(IslandCache.SizeReport(ref source));
            // (Ponds: the "[ponds]" line from BuildPlaces; W-14 dropped the second count here, ~2.8 s.)
            if (source.Rivers.IsCreated) Debug.Log("[island] " + RiverReport(ref source.Rivers.Value));
            // W-68: v3 ends the bake with the water audit (V3Audit; on v2 run it from the editor for a baseline).
            // W-74: only when asked (-islandaudit / PUNCHY_ISLANDAUDIT=1); they cost ~17 s against a ~6 s bake.
            // W-140: the audits read the bake's own tables (V3Ranges), which a cache hit doesn't build: -freshisland runs them.
            if (version == IslandV3.Version && IslandV2.AuditRequested())
            {
                if (hit) Debug.Log("[island] audits skipped on a cache hit (-freshisland or PUNCHY_FRESHISLAND=1 runs them)");
                else { Debug.Log(V3Audit.Run(source)); Debug.Log(V3Ranges.Checks(source)); Debug.Log(V3Ground.Report(source)); }
            }
            Debug.Log($"[island] reports {report.ElapsedMilliseconds} ms; v{version} ready at {UnityEngine.Time.realtimeSinceStartup:F1} s since start ({World.Name})");
        }

        /// <summary>What <see cref="Bake"/> did, for the "[island]" line.</summary>
        public struct BakeResult
        {
            public string Report;
            public long TotalMs, GenerateMs, FallsMs, ArchipelagoMs;
        }

        /// <summary>
        /// W-140: the island bake (v2 or v3) on its own, from the source as the subscene gave it, the palette and its
        /// landmark types (<see cref="LandmarkTypes"/>). Writes every blob in <see cref="IslandCache.Each{V}"/> and the
        /// island scalars into the source (all Persistent). Used by the system on a cache miss, by the build step and
        /// by the cache's loaded == fresh test.
        /// </summary>
        public static BakeResult Bake(ref TerrainSource source, BiomePalette palette, List<LandmarkType> types, int version)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var biomes = new List<BiomeDefinition>(palette.Biomes);
            bool hasOcean = source.Biomes.Value.OceanIndex >= 0;
            var gen = version == IslandV3.Version ? IslandV3.Generate(biomes, hasOcean, Allocator.Persistent) : IslandV2.Generate(biomes, hasOcean, Allocator.Persistent);
            long genMs = watch.ElapsedMilliseconds;
            source.BiomeKind = BiomeSourceKind.Map;
            source.BiomeMap = gen.Map;
            source.Island = gen.Island;
            source.V3Ground = gen.V3Ground;
            source.BiomeCells = default;
            source.RiverSettings = version == IslandV3.Version ? IslandV3.Rivers(source.RiverSettings) : IslandV2.Rivers(source.RiverSettings);
            source.Rivers = default; source.Landmarks = default; source.Settlements = default; source.Caves = default; source.BatFolk = default; source.Icebergs = default; source.GrandFalls = default; source.Archipelago = default; source.Anchors = default; source.PondMask = default; source.CliffKit = default;
            TerrainAuthoring.BuildPlaces(ref source, biomes, types, palette.Placed ?? new List<PlacedLandmark>(), palette.Roadside ?? new List<RoadsideLandmark>(), Allocator.Persistent, villages: true);
            // #72 (W-87): "-campsitetest <look>" adds one test campsite (debug only; nothing without the flag).
            CampsiteTest.Add(ref source, biomes, types, Allocator.Persistent);
            // W-29: the grand falls go in after every place, so no landmark, cave or settlement sees them.
            long fallsAt = watch.ElapsedMilliseconds;
            source.GrandFalls = GrandFalls.Build(source, Allocator.Persistent, version == IslandV3.Version && GrandFalls.LakeShelfOn(), biomes);
            Debug.Log(GrandFalls.LastLog);
            long fallsMs = watch.ElapsedMilliseconds - fallsAt;
            // W-30: the archipelago too, off the dune coast; it only raises sea floor.
            source.Archipelago = Archipelago.Build(source, BiomeTags.First(biomes, BiomeTags.ArchipelagoCoast), Allocator.Persistent);
            Debug.Log(Archipelago.LastLog);
            long archMs = watch.ElapsedMilliseconds - fallsAt - fallsMs;
            // W-150: the cliff kit last, on the finished ground; its own stream, so no place sees it.
            source.CliffKit = CliffKitBaking.Build(source, biomes, Allocator.Persistent);
            Debug.Log(CliffKitBaking.LastLog);
            return new BakeResult { Report = gen.Report, TotalMs = watch.ElapsedMilliseconds, GenerateMs = genMs, FallsMs = fallsMs, ArchipelagoMs = archMs };
        }

        /// <summary>
        /// The version 2 bake's output, kept for every later world in this process (see <see cref="OnUpdate"/>). Only
        /// the fields this system writes are kept: the rest of TerrainSource (biome table, noise settings) comes
        /// from each world's own subscene and may be unloaded with it.
        /// </summary>
        static class Shared
        {
            static readonly Dictionary<int, TerrainSource> bakes = new Dictionary<int, TerrainSource>();

            // W-73: worlds using the blobs, and whether the app (or Play mode) is quitting. Play mode keeps statics
            // (domain reload is off), so the old Reset dropped a whole bake (~22 MB of Persistent blobs) per cycle.
            // Freed only once every world that took them is gone and Play mode has ended (other systems' jobs may still
            // read them while a world is being destroyed). Mid-session (hosting from solo destroys the solo world) the
            // next world still shares them instead of baking again (#23). A player frees them when the process exits.
            public static int Users;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void Reset()
            {
                Dispose();   // whatever an earlier Play session left (no world from it is alive any more)
                Users = 0;
                IslandV2.HostVersion = 0; IslandV2.ActiveVersion = 0;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.playModeStateChanged -= OnPlayMode;
                UnityEditor.EditorApplication.playModeStateChanged += OnPlayMode;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Dispose;
#endif
            }

#if UNITY_EDITOR
            static void OnPlayMode(UnityEditor.PlayModeStateChange change)
            {
                if (change == UnityEditor.PlayModeStateChange.EnteredEditMode && Users == 0)
                    Dispose();
            }
#endif

            public static bool Has(int version) => bakes.ContainsKey(version);

            public static void Keep(in TerrainSource source, int version) => bakes[version] = source;

            /// <summary>A world that took the blobs is gone (see <see cref="Users"/>).</summary>
            public static void Release() => Users = Math.Max(0, Users - 1);

            /// <summary>Frees every blob this system's bakes made, every version (the subscene's own blobs belong to
            /// the subscene), then forgets them.</summary>
            static void Dispose()
            {
                foreach (var kv in bakes)
                {
                    var baked = kv.Value;
                    Free(ref baked.BiomeMap); Free(ref baked.Island); Free(ref baked.V3Ground);
                    Free(ref baked.Rivers); Free(ref baked.Landmarks); Free(ref baked.Settlements); Free(ref baked.Caves);
                    Free(ref baked.BatFolk); Free(ref baked.Icebergs); Free(ref baked.GrandFalls); Free(ref baked.Archipelago);
                    Free(ref baked.Anchors); Free(ref baked.PondMask); Free(ref baked.CliffKit);
                }
                bakes.Clear();
            }

            static void Free<T>(ref BlobAssetReference<T> blob) where T : unmanaged
            {
                if (blob.IsCreated) blob.Dispose();
                blob = default;
            }

            public static void ApplyTo(ref TerrainSource source, int version)
            {
                var baked = bakes[version];
                source.BiomeKind = baked.BiomeKind;
                source.BiomeMap = baked.BiomeMap;
                source.Island = baked.Island;
                source.V3Ground = baked.V3Ground;
                source.BiomeCells = baked.BiomeCells;
                source.RiverSettings = baked.RiverSettings;
                source.Rivers = baked.Rivers;
                source.Landmarks = baked.Landmarks;
                source.Settlements = baked.Settlements;
                source.Caves = baked.Caves;
                source.BatFolk = baked.BatFolk;
                source.Icebergs = baked.Icebergs;
                source.GrandFalls = baked.GrandFalls;
                source.Archipelago = baked.Archipelago;
                source.Anchors = baked.Anchors;
                source.PondsOn = baked.PondsOn;
                source.RiverRoughOn = baked.RiverRoughOn;
                source.PondMask = baked.PondMask;
                source.CliffKit = baked.CliffKit;
            }
        }
    }
}
