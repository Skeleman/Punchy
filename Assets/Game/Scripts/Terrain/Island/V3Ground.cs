using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using V2 = Punchy.Terrain.IslandV2;

namespace Punchy.Terrain
{
    /// <summary>
    /// Island v3 only (B3 + B4), per pixel of the biome map: how wide the biome blend is there and which realm family's
    /// geology the ground has. Absent (not created) on v1 and v2, so they sample exactly as before.
    /// </summary>
    public struct V3GroundBlob
    {
        public int Resolution;
        public float2 Origin;
        public float WorldSize;
        /// <summary>Biome blend radius in units of <see cref="V3Ground.BlendUnit"/> metres: narrow under a crest, wide on open borders.</summary>
        public BlobArray<byte> Blend;
        /// <summary>Geology family (<see cref="IslandV2.Family"/>) of the realm the pixel's biome came from (a bled cell takes its neighbour's).</summary>
        public BlobArray<byte> Family;
        /// <summary>W-70b: pass corridor, 0..255 = 0..1: how much of the biome shape relief is faded out (1 in a pass saddle).</summary>
        public BlobArray<byte> Pass;
        /// <summary>W-70b: the whole pass box, 0..255 = 0..1, where long walls of biome relief are notched (see TerrainSource.ShapeHeight).</summary>
        public BlobArray<byte> PassZone;
    }

    /// <summary>
    /// Island v3 biome field and rock pass (brief v3-island-design.md section 1, cards B3 and B4).
    /// B3: realm borders follow the range spines. The biome map has no Mountains/Ashlands/Mesa strips on the border any
    /// more (the range field makes the high ground); each pixel carries a blend radius (about 60 m under a full crest,
    /// medium in passes and at taper ends, up to 480 m on open borders), and near open borders and passes whole biome
    /// cells may take the neighbour realm's biome (bleed), less often the farther they are from the border.
    /// B4: rock and scree by slope with one rock and one scree surface per realm family, and altitude bands on the
    /// ranges (alpine grass, scree, snow by height above the range foot). Slope is max(mesh slope, range field slope),
    /// so far LODs, whose coarse mesh smooths cliffs, keep their rock.
    /// </summary>
    public static class V3Ground
    {
        public const float BlendUnit = 4f;
        const float BlendNarrow = 60f, BlendWide = 480f;

        // ---------------------------------------------------------------- bake (biome map + ground blob)

        /// <summary>
        /// The v3 biome map (same realms, cells and picks as v2 minus the border strips, plus bleed) and its ground
        /// blob. <paramref name="field"/> is the range field (1024 a side, metres, without the lake bowl).
        /// </summary>
        internal static BlobAssetReference<BiomeMapBlob> Build(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, float[] field, Allocator allocator,
            out BlobAssetReference<V3GroundBlob> ground, out string log)
        {
            const int N = V2.N; const float PX = V2.PX, W = V2.WorldSizeM, coastWobble = 0.14f;
            V2.PickRealms(out var sites, out var fam, out uint seed, out _);
            int oceanId = hasOcean ? palette.Count - 1 : 0;
            var pick = V2.FamilyPicks(palette, oceanId);

            var passes = V3Ranges.Passes; var spines = V3Ranges.Spines;
            var biomeIds = new byte[N * N]; var blend = new byte[N * N]; var family = new byte[N * N]; var corridor = new byte[N * N]; var zone = new byte[N * N];
            int bled = 0, land = 0, narrow = 0, wide = 0;
            float FieldAt(float wx, float wz)
            {
                int R = V3Ranges.Res;
                int fx = Mathf.Clamp((int)((wx / W + 0.5f) * R), 0, R - 1), fz = Mathf.Clamp((int)((wz / W + 0.5f) * R), 0, R - 1);
                return field[fz * R + fx];
            }
            var bledRows = new int[N]; var landRows = new int[N]; var narrowRows = new int[N]; var wideRows = new int[N];
            BakeParallel.For(N, y =>
            {
                for (int x = 0; x < N; x++)
                {
                    float px = (x + 0.5f) / N * PX, py = (y + 0.5f) / N * PX;
                    int i = y * N + x;
                    float wx = px / PX * W - W * 0.5f, wz = py / PX * W - W * 0.5f;
                    if (V2.Coast(seed, px, py, coastWobble) <= 0) { biomeIds[i] = (byte)oceanId; blend[i] = (byte)(BlendNarrow / BlendUnit); family[i] = 255; continue; }
                    landRows[y]++;
                    V2.Nearest(sites, V2.Warped(seed, px, py), out int ra, out int rb, out float d1, out float d2);
                    float cx = px + (V2.Fbm(seed + 31, px / 90f, py / 90f) - 0.5f) * 90f, cy = py + (V2.Fbm(seed + 37, px / 90f, py / 90f) - 0.5f) * 90f;
                    int cell = V2.NearestCell(seed + 5, cx / 110f, cy / 110f);

                    // How open the border is here: the range field standing on it (crest ~60 m or more = closed).
                    float h = Mathf.Max(FieldAt(wx, wz), 0f);
                    float open = 1f - Mathf.Clamp01(h / 60f);
                    float toPass = float.MaxValue, flat = 0f, box = 0f;
                    foreach (var q in passes)
                    {
                        float d = Vector2.Distance(q.At, new Vector2(wx, wz));
                        float reach = Mathf.Max(q.Width, spines[q.Spine].HalfWidth * 1.4f);
                        toPass = Mathf.Min(toPass, d / reach);
                        flat = Mathf.Max(flat, Corridor(q, spines[q.Spine].HalfWidth, new Vector2(wx, wz)));
                        box = Mathf.Max(box, Zone(q, spines[q.Spine].HalfWidth, new Vector2(wx, wz)));
                    }
                    corridor[i] = (byte)Mathf.RoundToInt(flat * 255f); zone[i] = (byte)Mathf.RoundToInt(box * 255f);
                    float passOpen = 0.85f * (1f - Mathf.Clamp01(toPass));
                    open = Mathf.Max(open, passOpen);

                    // Bleed: a whole cell (its own roll) takes the neighbour realm's biome, with a chance falling with
                    // distance from the border (d2 - d1 is about twice that distance, in layout pixels).
                    float borderM = (d2 - d1) * 0.5f * W / PX;
                    float chance = open * Mathf.Clamp01(1f - borderM / (passOpen > 0.2f ? 900f : 650f)) * 0.75f;
                    int realm = ra;
                    if (V2.H01(seed + 71, cell, rb * 31 + ra) < chance) { realm = rb; bledRows[y]++; }
                    var list = pick[fam[realm]];
                    biomeIds[i] = (byte)list[(int)(V2.H(seed + 11, cell, realm) % (uint)list.Length)];
                    family[i] = (byte)fam[realm];

                    // Blend radius: narrow where a crest stands, medium in passes and taper ends, wide on open borders.
                    // W-70b: only near the border. W-70 widened every interior cell edge too (no range there = "open"),
                    // which blurred the surface weights so far that most landmark spots lost their 90% biome.
                    float r = Mathf.Lerp(BlendNarrow, BlendWide, Mathf.Clamp01(open));
                    r = Mathf.Lerp(r, BlendNarrow, Smooth01((borderM - r) / r));
                    if (r < 100f) narrowRows[y]++; else if (r > 300f) wideRows[y]++;
                    blend[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(r / BlendUnit), 1, 255);
                }
            });
            for (int y = 0; y < N; y++) { bled += bledRows[y]; land += landRows[y]; narrow += narrowRows[y]; wide += wideRows[y]; }

            var origin = new float2(-W * 0.5f);
            var b1 = new BlobBuilder(Allocator.Temp);
            ref var map = ref b1.ConstructRoot<BiomeMapBlob>();
            map.Resolution = N; map.Origin = origin; map.WorldSize = W;
            var arr = b1.Allocate(ref map.Ids, N * N);
            for (int i = 0; i < N * N; i++) arr[i] = biomeIds[i];
            var mapBlob = b1.CreateBlobAssetReference<BiomeMapBlob>(allocator); b1.Dispose();

            var b2 = new BlobBuilder(Allocator.Temp);
            ref var g = ref b2.ConstructRoot<V3GroundBlob>();
            g.Resolution = N; g.Origin = origin; g.WorldSize = W;
            var ba = b2.Allocate(ref g.Blend, N * N); var fa = b2.Allocate(ref g.Family, N * N); var pa = b2.Allocate(ref g.Pass, N * N); var za = b2.Allocate(ref g.PassZone, N * N);
            for (int i = 0; i < N * N; i++) { ba[i] = blend[i]; fa[i] = family[i]; pa[i] = corridor[i]; za[i] = zone[i]; }
            ground = b2.CreateBlobAssetReference<V3GroundBlob>(allocator); b2.Dispose();

            log = $"[v3 biomes] land {land} px ({W / N:F0} m), bled {bled}, blend under 100 m {narrow}, over 300 m {wide}; no border strips";
            return mapBlob;
        }

        static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>
        /// W-70b: a walkable track through a pass, 1 on it and 0 off it: a strip along the crossing (out to 1.8
        /// half-widths of the range either side, fading by 2.6) and narrow across it (TrackHalf, fading over TrackFade,
        /// a few ground cells). The biome shape relief is faded out on it, so the smooth range field carries one
        /// walkable line; the rest of the saddle keeps its crags and steep sections.
        /// </summary>
        const float TrackHalf = 24f, TrackFade = 72f;

        static float Corridor(V3Ranges.RangePass q, float halfWidth, Vector2 p)
        {
            var d = p - q.At;
            float a = Mathf.Abs(Vector2.Dot(d, q.Across)), b = Mathf.Abs(Vector2.Dot(d, new Vector2(q.Across.y, -q.Across.x)));
            float wa = 1f - Smooth01((a - 1.8f * halfWidth) / (0.8f * halfWidth));
            // The track's width wanders along it, so the relief it cuts back steps in and out instead of lining the
            // track with two long straight walls.
            float s = Vector2.Dot(d, q.Across), vary = 0.5f + 0.25f * Mathf.Sin(s / 37f + q.Along * 0.01f) + 0.25f * Mathf.Sin(s / 23f + 1.7f);
            float wb = 1f - Smooth01((b - TrackHalf * (0.5f + 1.5f * vary)) / TrackFade);
            return wa * wb;
        }

        /// <summary>W-70b: the whole pass box (the crossing as for the track, the full pass width along the spine, fading by half that).</summary>
        static float Zone(V3Ranges.RangePass q, float halfWidth, Vector2 p)
        {
            var d = p - q.At;
            float a = Mathf.Abs(Vector2.Dot(d, q.Across)), b = Mathf.Abs(Vector2.Dot(d, new Vector2(q.Across.y, -q.Across.x)));
            return (1f - Smooth01((a - 1.8f * halfWidth) / (0.8f * halfWidth))) * (1f - Smooth01((b - q.Width) / (0.5f * q.Width)));
        }

        // ---------------------------------------------------------------- sampling (Burst)

        /// <summary>W-70b: pass box weight (0..1) at a point, bilinear.</summary>
        public static float PassBox(ref V3GroundBlob g, float2 xz) => Bilinear(ref g.PassZone, ref g, xz);

        /// <summary>W-70b: pass corridor weight (0..1) at a point, bilinear: how much biome shape relief to fade out.</summary>
        public static float PassFlat(ref V3GroundBlob g, float2 xz) => Bilinear(ref g.Pass, ref g, xz);

        static float Bilinear(ref BlobArray<byte> m, ref V3GroundBlob g, float2 xz)
        {
            var max = g.Resolution - 1;
            var uv = math.clamp((xz - g.Origin) / g.WorldSize * g.Resolution - 0.5f, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;
            int row = i0.y * g.Resolution + i0.x;
            float a = math.lerp(m[row], m[row + 1], f.x), b = math.lerp(m[row + g.Resolution], m[row + g.Resolution + 1], f.x);
            return math.lerp(a, b, f.y) * (1f / 255f);
        }

        /// <summary>Blend radius (metres) at a point, bilinear.</summary>
        public static float BlendRadius(ref V3GroundBlob g, float2 xz)
        {
            var max = g.Resolution - 1;
            var uv = math.clamp((xz - g.Origin) / g.WorldSize * g.Resolution - 0.5f, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;
            int row = i0.y * g.Resolution + i0.x;
            float a = math.lerp(g.Blend[row], g.Blend[row + 1], f.x), b = math.lerp(g.Blend[row + g.Resolution], g.Blend[row + g.Resolution + 1], f.x);
            return math.lerp(a, b, f.y) * BlendUnit;
        }

        /// <summary>Rock, scree, alpine grass and snow surfaces for a family.</summary>
        static SurfaceProps Geology(int fam, int kind)
        {
            // kind: 0 rock, 1 scree, 2 alpine grass. Albedo linear.
            float3 rock, scree, alpine; float strata, alpineGrass;
            switch (fam)
            {
                case 0: rock = new float3(0.50f, 0.28f, 0.15f); scree = new float3(0.58f, 0.38f, 0.23f); alpine = new float3(0.36f, 0.30f, 0.17f); strata = 1f; alpineGrass = 0.15f; break;   // arid: sandstone
                case 1: rock = new float3(0.085f, 0.095f, 0.105f); scree = new float3(0.14f, 0.15f, 0.15f); alpine = new float3(0.10f, 0.17f, 0.07f); strata = 0.6f; alpineGrass = 0.8f; break;   // wet: dark slate
                case 2: rock = new float3(0.30f, 0.30f, 0.32f); scree = new float3(0.38f, 0.38f, 0.39f); alpine = new float3(0.20f, 0.24f, 0.13f); strata = 0.35f; alpineGrass = 0.5f; break;   // cold: granite
                case 3: rock = new float3(0.055f, 0.05f, 0.05f); scree = new float3(0.11f, 0.09f, 0.08f); alpine = new float3(0.13f, 0.12f, 0.11f); strata = 0.2f; alpineGrass = 0.05f; break;   // volcanic: basalt
                default: rock = new float3(0.52f, 0.50f, 0.45f); scree = new float3(0.60f, 0.58f, 0.52f); alpine = new float3(0.17f, 0.27f, 0.09f); strata = 0.7f; alpineGrass = 0.75f; break;   // temperate: pale limestone
            }
            var p = default(SurfaceProps);
            if (kind == 0) { p.Albedo = rock; p.Smoothness = 0.22f; p.Bump = 1f; p.Strata = strata; p.Pebbles = 0.15f; }
            else if (kind == 1) { p.Albedo = scree; p.Smoothness = 0.15f; p.Bump = 0.7f; p.Pebbles = 1f; }
            else { p.Albedo = alpine; p.Smoothness = 0.1f; p.Bump = 0.4f; p.Grass = alpineGrass; p.Pebbles = 0.2f; }
            return p;
        }

        /// <summary>Altitude band starts (metres above the range foot) per family: alpine grass, scree, snow (arid: no snow).</summary>
        static float3 Bands(int fam) => fam switch
        {
            0 => new float3(70f, 120f, 9999f),
            2 => new float3(45f, 85f, 125f),
            3 => new float3(60f, 100f, 9999f),
            _ => new float3(70f, 130f, 185f)
        };

        /// <summary>Weights (alpine, scree, rock, snow) for one family at a point; the band cut-offs wobble with the edge noise.</summary>
        public static float4 Weights(int fam, float slope, float lift, float edge)
        {
            var bands = Bands(fam);
            float l = lift + edge * 10f, s = slope + edge * 4f;
            float alpine = math.smoothstep(bands.x, bands.x + 15f, l);
            float screeBand = math.smoothstep(bands.y, bands.y + 15f, l);
            float snow = math.smoothstep(bands.z, bands.z + 15f, l) * (1f - math.smoothstep(34f, 44f, s));
            float scree = math.max(screeBand, math.smoothstep(28f, 38f, s));
            float rock = math.smoothstep(38f, 45f, s);
            return new float4(alpine, scree, rock, snow);
        }

        /// <summary>
        /// The v3 rock pass, after the biome surfaces: geology from the four nearest pixels (bleed blends it too),
        /// slope = max(mesh slope, range field slope), lift = range field height (above the range foot).
        /// </summary>
        public static void Apply(ref V3GroundBlob g, float2 xz, float slope, float lift, float edge, ref SurfaceProps result)
        {
            var max = g.Resolution - 1;
            var uv = math.clamp((xz - g.Origin) / g.WorldSize * g.Resolution - 0.5f, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;
            int row = i0.y * g.Resolution + i0.x;
            var outp = default(SurfaceProps); float total = 0f;
            for (int k = 0; k < 4; k++)
            {
                int idx = row + (k & 1) + (k >> 1) * g.Resolution;
                float w = ((k & 1) == 1 ? f.x : 1f - f.x) * ((k >> 1) == 1 ? f.y : 1f - f.y);
                int fam = g.Family[idx];
                if (w <= 0f || fam == 255) continue;
                var bw = Weights(fam, slope, lift, edge);
                var p = result;
                p = SurfaceProps.Lerp(p, Geology(fam, 2), bw.x);
                p = SurfaceProps.Lerp(p, Geology(fam, 1), bw.y);
                p = SurfaceProps.Lerp(p, Geology(fam, 0), bw.z);
                p.Snow = math.max(p.Snow, bw.w);
                outp.AddScaled(p, w); total += w;
            }
            if (total <= 0f) return;
            var kept = 1f - total;   // ocean corners keep the biome surface
            outp.AddScaled(result, kept);
            result = outp;
        }

        // ---------------------------------------------------------------- checks (editor / bake log)

        static readonly string[] BandNames = { "ground", "alpine", "scree", "rock", "snow" };

        /// <summary>"[rock]": land cells (16 m) per band per realm family, by the strongest weight (ground = none over 0.5).</summary>
        public static string Report(in TerrainSource source, float step = 16f)
        {
            var s = source; if (!s.V3Ground.IsCreated) return "[rock] no v3 ground";
            int size = (int)(s.World.WorldSize / step); float half = s.World.WorldSize * 0.5f, sea = s.World.SeaLevel;
            var counts = new int[5 * 5 * size];
            BakeParallel.For(size, z =>
            {
                ref var g = ref s.V3Ground.Value;
                for (int x = 0; x < size; x++)
                {
                    var xz = new float2(-half + (x + 0.5f) * step, -half + (z + 0.5f) * step);
                    var gs = s.SampleGround(xz);
                    if (gs.Height <= sea + 0.5f || gs.HasWater && gs.WaterSurface > gs.Height) continue;
                    int px = math.clamp((int)((xz.x - g.Origin.x) / g.WorldSize * g.Resolution), 0, g.Resolution - 1), pz = math.clamp((int)((xz.y - g.Origin.y) / g.WorldSize * g.Resolution), 0, g.Resolution - 1);
                    int fam = g.Family[pz * g.Resolution + px]; if (fam == 255) continue;
                    var n = s.SampleNormal(xz, 1f);
                    float slope = math.max(math.degrees(math.acos(math.saturate(n.y))), s.FieldSlope(xz));
                    float lift = s.Island.IsCreated ? math.max(s.Island.Value.Sample(xz), 0f) : 0f;
                    var w = Weights(fam, slope, lift, 0f);
                    int band = 0; float best = 0.5f;
                    if (w.x > best) { band = 1; best = w.x; }
                    if (w.y > best * 0.999f && w.y > 0.5f) { band = 2; best = w.y; }
                    if (w.z > 0.5f) band = 3;
                    if (w.w > 0.5f && w.z <= 0.5f) band = 4;
                    counts[(z * 5 + fam) * 5 + band]++;
                }
            });
            var sb = new System.Text.StringBuilder($"[rock] land cells ({step:F0} m) per realm, ground/alpine/scree/rock/snow:");
            for (int f = 0; f < 5; f++)
            {
                var t = new int[5];
                for (int z = 0; z < size; z++) for (int b = 0; b < 5; b++) t[b] += counts[(z * 5 + f) * 5 + b];
                sb.Append($" {(V2.Family)f} {t[0]}/{t[1]}/{t[2]}/{t[3]}/{t[4]};");
            }
            return sb.ToString();
        }

        /// <summary>Top-down biome map (north up): each pixel's surface-dominant biome in its base surface colour, hillshaded; passes red.</summary>
        public static string BiomePng(in TerrainSource source, string path, int px = 1024)
        {
            var s = source; float size = s.World.WorldSize, half = size * 0.5f, cell = size / px, sea = s.World.SeaLevel;
            var h = new float[px * px]; var cols = new Color[px * px];
            BakeParallel.For(px, j =>
            {
                ref var set = ref s.Biomes.Value;
                for (int i = 0; i < px; i++)
                {
                    var xz = new float2(-half + (i + 0.5f) * cell, -half + (j + 0.5f) * cell);
                    h[j * px + i] = s.SampleHeight(xz);
                    s.SampleBiomes(xz, out var surf, out _);
                    var c = float3.zero;
                    for (int k = 0; k < 4; k++) if (surf.Weights[k] > 0f) c += set.Surfaces[set.Biomes[surf.Ids[k]].BaseSurface].Props.Albedo * surf.Weights[k];
                    c = math.pow(math.saturate(c), 1f / 2.2f);
                    cols[j * px + i] = new Color(c.x, c.y, c.z);
                }
            });
            for (int j = 0; j < px; j++)
                for (int i = 0; i < px; i++)
                {
                    int k = j * px + i;
                    if (h[k] <= sea) { cols[k] = Color.Lerp(cols[k], new Color(0.1f, 0.2f, 0.4f), 0.6f); continue; }
                    float dx = i + 1 < px ? h[k + 1] - h[k] : 0f, dz = j + 1 < px ? h[k + px] - h[k] : 0f;
                    cols[k] *= Mathf.Clamp(1f + (dz - dx) * 0.06f, 0.55f, 1.4f);
                }
            foreach (var q in V3Ranges.Passes)
            {
                int ci = (int)((q.At.x + half) / cell), cj = (int)((q.At.y + half) / cell);
                for (int dj = -5; dj <= 5; dj++) for (int di = -5; di <= 5; di++)
                    if (ci + di >= 0 && ci + di < px && cj + dj >= 0 && cj + dj < px && di * di + dj * dj <= 25) cols[(cj + dj) * px + ci + di] = Color.red;
            }
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
            tex.SetPixels(cols); tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }
    }
}
