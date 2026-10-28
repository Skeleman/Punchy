using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    public enum BiomeSourceKind : byte
    {
        Noise,
        Map
    }

    /// <summary>Material properties at a point. Blendable, so biome and layer transitions are just weighted sums.</summary>
    public struct SurfaceProps
    {
        public float3 Albedo;      // linear
        public float Smoothness;
        public float Metallic;
        public float3 Emission;    // linear HDR
        public float Grass;
        public float Snow;
        public float Bump;
        public float Pebbles;

        public static SurfaceProps Lerp(in SurfaceProps a, in SurfaceProps b, float t) => new SurfaceProps
        {
            Albedo = math.lerp(a.Albedo, b.Albedo, t),
            Smoothness = math.lerp(a.Smoothness, b.Smoothness, t),
            Metallic = math.lerp(a.Metallic, b.Metallic, t),
            Emission = math.lerp(a.Emission, b.Emission, t),
            Grass = math.lerp(a.Grass, b.Grass, t),
            Snow = math.lerp(a.Snow, b.Snow, t),
            Bump = math.lerp(a.Bump, b.Bump, t),
            Pebbles = math.lerp(a.Pebbles, b.Pebbles, t)
        };

        public void AddScaled(in SurfaceProps s, float w)
        {
            Albedo += s.Albedo * w;
            Smoothness += s.Smoothness * w;
            Metallic += s.Metallic * w;
            Emission += s.Emission * w;
            Grass += s.Grass * w;
            Snow += s.Snow * w;
            Bump += s.Bump * w;
            Pebbles += s.Pebbles * w;
        }
    }

    public struct SurfaceBlobData
    {
        public SurfaceProps Props;
        public float ColorVariation;
    }

    public struct LayerBlobData
    {
        public int Surface;
        public int UseSlope;
        public float2 SlopeRange;
        public float SlopeBlend;
        public int UseHeight;
        public float2 HeightRange;
        public float HeightBlend;
        public int UseWaterDepth;
        public float2 WaterDepthRange;
        public float WaterDepthBlend;
        public int OnlyInRivers;
        public int UsePatches;
        public float PatchFrequency;
        public float PatchCoverage;
        public float EdgeNoise;
        public float Opacity;
    }

    public struct BiomeBlobData
    {
        public float Frequency;
        public float BaseElevation;
        public TerrainShapeKind Shape;
        public float Relief;
        public float FeatureFrequency;
        public int Octaves;
        public float Sharpness;
        public int TerraceSteps;
        public float2 Wind;
        public float Rainfall;
        /// <summary>Water look: rgb colour (linear), a murk 0..1; plus glow.</summary>
        public float4 Water;
        public float WaterGlow;
        public float WaterLava;
        public float2 NoiseOffset;
        /// <summary>Preferred climate: altitude, temperature, humidity, volcanism ranges.</summary>
        public float4 ClimateMin, ClimateMax;
        public int BaseSurface;
        public int FirstLayer;
        public int LayerCount;
    }

    /// <summary>All biome and surface definitions, flattened for Burst. Land biomes first, then the ocean (if any).</summary>
    public struct BiomeSetBlob
    {
        public BlobArray<SurfaceBlobData> Surfaces;
        public BlobArray<LayerBlobData> Layers;
        public BlobArray<BiomeBlobData> Biomes;
        public int LandCount;
        /// <summary>Index of the ocean biome, or -1 for an endless continent.</summary>
        public int OceanIndex;
        public float LandFrequency;
    }

    /// <summary>
    /// Each biome region's (Voronoi cell's) biome, worked out once at bake time for every cell around the island:
    /// picking it means evaluating the climate fields, far too slow to repeat for every terrain sample.
    /// </summary>
    public struct BiomeCellTableBlob
    {
        public int2 Min, Size;
        public BlobArray<byte> Ids;
        /// <summary>Each cell's realm (index in the RealmSet), or <see cref="BiomeSampling.NoRealm"/> without realms.</summary>
        public BlobArray<byte> Realms;
    }

    /// <summary>Biome index per pixel, covering a square region of the world.</summary>
    public struct BiomeMapBlob
    {
        public int Resolution;
        public float2 Origin;
        public float WorldSize;
        public BlobArray<byte> Ids;
    }

    /// <summary>Settings for procedurally placed biome regions: warped Voronoi cells, one land biome per cell.</summary>
    [Serializable]
    public struct BiomeNoiseSettings : IEquatable<BiomeNoiseSettings>
    {
        public uint Seed;
        [UnityEngine.Tooltip("Typical width of one biome region, in metres.")]
        public float CellSize;
        [UnityEngine.Tooltip("Width of the transition between neighbouring biomes' surfaces, in metres.")]
        public float BlendDistance;
        [UnityEngine.Tooltip("Width of the transition between neighbouring biomes' elevations and shapes, in metres. " +
                             "Wider = gentler slopes between e.g. a high plateau and a low plain.")]
        public float ShapeBlendDistance;
        [UnityEngine.Tooltip("How far (metres) region borders are pushed around to look organic.")]
        public float WarpStrength;
        [UnityEngine.Tooltip("Typical size of climate zones (wet, dry, high, cold, volcanic), in metres.")]
        public float ClimateSize;
        [UnityEngine.Tooltip("How strictly biomes follow climate: 0 = random by frequency, 1 = only where they fit.")]
        [UnityEngine.Range(0f, 1f)] public float ClimateInfluence;

        public static BiomeNoiseSettings Default => new BiomeNoiseSettings
        {
            Seed = 7,
            CellSize = 1400f,
            BlendDistance = 120f,
            ShapeBlendDistance = 450f,
            WarpStrength = 350f,
            ClimateSize = 3000f,
            ClimateInfluence = 1f
        };

        public bool Equals(BiomeNoiseSettings o) =>
            Seed == o.Seed && CellSize == o.CellSize && BlendDistance == o.BlendDistance &&
            ShapeBlendDistance == o.ShapeBlendDistance && WarpStrength == o.WarpStrength &&
            ClimateSize == o.ClimateSize && ClimateInfluence == o.ClimateInfluence;
    }

    /// <summary>Up to four biomes and their weights at a point (weights sum to 1).</summary>
    public struct BiomeWeights
    {
        public int4 Ids;
        public float4 Weights;

        public void Add(int id, float weight)
        {
            if (weight <= 0f)
                return;
            for (int i = 0; i < 4; i++)
            {
                if (Weights[i] > 0f && Ids[i] == id)
                {
                    Weights[i] += weight;
                    return;
                }
            }

            // New biome: take an empty slot, or replace the weakest if this one is stronger.
            int slot = 0;
            for (int i = 1; i < 4; i++)
                if (Weights[i] < Weights[slot])
                    slot = i;
            if (weight > Weights[slot])
            {
                Ids[slot] = id;
                Weights[slot] = weight;
            }
        }

        public void Normalize()
        {
            var sum = math.csum(Weights);
            Weights = sum > 0f ? Weights / sum : new float4(1f, 0f, 0f, 0f);
        }

        public int Dominant()
        {
            int best = 0;
            for (int i = 1; i < 4; i++)
                if (Weights[i] > Weights[best])
                    best = i;
            return Ids[best];
        }
    }

    /// <summary>The nine Voronoi feature points around a position: distance (in cells) and land biome of each.</summary>
    public struct BiomeCells
    {
        public FixedList64Bytes<float> Distances;
        public FixedList64Bytes<int> Biomes;
        public float Nearest;
    }

    /// <summary>Everything the surface rules can look at for one point.</summary>
    public struct GroundContext
    {
        public float2 Position;
        public float Height;
        public float Slope;        // degrees
        public float WaterDepth;   // metres below the local water surface (negative = above it)
        public float River;        // 0..1, 1 inside a river channel
        public float EdgeNoise;    // -1..1
        public float ColorNoise;   // -1..1
    }

    public static class BiomeSampling
    {
        /// <summary>Finds the warped-Voronoi feature points around xz; weights for any blend width derive from this.</summary>
        /// <param name="spawnCell">From <see cref="SpawnCell"/>; this region always gets the first biome.</param>
        public static BiomeCells Cells(in BiomeNoiseSettings s, ref BiomeSetBlob set, int2 spawnCell, float2 xz) =>
            Cells(s, ref set, default, spawnCell, xz);

        /// <param name="table">Precomputed cell biomes (<see cref="BuildCellTable"/>); cells outside it are worked out.</param>
        public static BiomeCells Cells(in BiomeNoiseSettings s, ref BiomeSetBlob set, BlobAssetReference<BiomeCellTableBlob> table, int2 spawnCell, float2 xz)
        {
            var p = Warp(s, xz);
            var cell = (int2)math.floor(p);

            var cells = new BiomeCells { Nearest = float.MaxValue };
            for (int i = 0; i < 9; i++)
            {
                var c = cell + new int2(i % 3 - 1, i / 3 - 1);
                var d = math.distance(p, c + CellJitter(c, s.Seed));
                cells.Distances.Add(d);
                // The region containing the world origin always uses the first biome, so the start area is predictable.
                cells.Biomes.Add(math.all(c == spawnCell) ? 0 : LookUp(c, s, ref set, table));
                cells.Nearest = math.min(cells.Nearest, d);
            }
            return cells;
        }

        /// <summary>World position → warped position in cell units.</summary>
        static float2 Warp(in BiomeNoiseSettings s, float2 xz)
        {
            var seedOffset = new float2(s.Seed % 997u * 0.6180339f, s.Seed % 991u * 0.4142135f) * 1000f;
            var warpFreq = 2f / math.max(s.CellSize, 1f);
            var warp = new float2(
                noise.snoise((xz + seedOffset) * warpFreq),
                noise.snoise((xz - seedOffset) * warpFreq + 31.7f));
            return (xz + warp * s.WarpStrength) / s.CellSize;
        }

        /// <summary>The Voronoi region that contains the world origin.</summary>
        public static int2 SpawnCell(in BiomeNoiseSettings s) => NearestCell(s, Warp(s, float2.zero));

        static int2 NearestCell(in BiomeNoiseSettings s, float2 p)
        {
            var cell = (int2)math.floor(p);
            var best = cell;
            var bestDistance = float.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                var c = cell + new int2(i % 3 - 1, i / 3 - 1);
                var d = math.distancesq(p, c + CellJitter(c, s.Seed));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// Land-biome weights for a transition of the given width (metres). Weights depend only on how much farther
        /// each point is than the nearest (continuous everywhere), so blends never crease. Near a border,
        /// d − nearest ≈ 2 × the distance to it.
        /// </summary>
        public static BiomeWeights Weights(in BiomeCells cells, float blendDistance, float cellSize)
        {
            var result = default(BiomeWeights);
            var blend = math.max(blendDistance, 1e-3f) / cellSize;
            for (int i = 0; i < 9; i++)
                result.Add(cells.Biomes[i], math.smoothstep(0f, 1f, 1f - (cells.Distances[i] - cells.Nearest) / blend));
            result.Normalize();
            return result;
        }

        /// <summary>Scales land weights down by the ocean factor and adds the ocean biome.</summary>
        public static BiomeWeights WithOcean(BiomeWeights land, float ocean, int oceanIndex)
        {
            if (oceanIndex < 0 || ocean <= 0f)
                return land;
            land.Weights *= 1f - ocean;
            land.Add(oceanIndex, ocean);
            land.Normalize();
            return land;
        }

        /// <summary>Bilinear blend of the four nearest biome-map pixels.</summary>
        public static BiomeWeights SampleMap(ref BiomeMapBlob map, float2 xz)
        {
            var result = default(BiomeWeights);
            AddMapSample(ref map, xz, 1f, ref result);
            result.Normalize();
            return result;
        }

        /// <summary>A wide blur of the biome map (3×3 taps across the radius), for smooth elevation transitions.</summary>
        public static BiomeWeights SampleMapBlurred(ref BiomeMapBlob map, float2 xz, float radius)
        {
            var result = default(BiomeWeights);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                AddMapSample(ref map, xz + new float2(x, y) * radius, (2 - math.abs(x)) * (2 - math.abs(y)), ref result);
            result.Normalize();
            return result;
        }

        static void AddMapSample(ref BiomeMapBlob map, float2 xz, float weight, ref BiomeWeights result)
        {
            var max = map.Resolution - 1;
            var uv = math.clamp((xz - map.Origin) / map.WorldSize * max, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;
            var row = i0.y * map.Resolution + i0.x;
            result.Add(map.Ids[row], weight * (1f - f.x) * (1f - f.y));
            result.Add(map.Ids[row + 1], weight * f.x * (1f - f.y));
            result.Add(map.Ids[row + map.Resolution], weight * (1f - f.x) * f.y);
            result.Add(map.Ids[row + map.Resolution + 1], weight * f.x * f.y);
        }

        static float2 CellJitter(int2 cell, uint seed)
        {
            var h = math.hash(new int3(cell, (int)seed));
            return 0.1f + 0.8f * new float2(h & 0xFFFF, h >> 16) / 65535f;
        }

        /// <summary>
        /// The climate at a point, each 0..1: x altitude, y temperature, z humidity, w volcanism. Smooth fields a few
        /// regions across; temperature drops with altitude, and volcanism is zero except around scattered hotspots.
        /// </summary>
        public static float4 Climate(in BiomeNoiseSettings s, float2 xz)
        {
            var q = xz / math.max(s.ClimateSize, 1f) + new float2(s.Seed % 89u, s.Seed % 97u) * 0.37f;
            static float Spread(float n) => math.saturate(0.5f + n * 0.65f);
            var altitude = Spread(noise.snoise(q + 11.3f) * 0.7f + noise.snoise(q * 2.3f - 5.1f) * 0.3f);
            var temperature = math.saturate(Spread(noise.snoise(q * 0.8f + 23.9f)) - (altitude - 0.5f) * 0.6f);
            var humidity = Spread(noise.snoise(q * 1.1f - 37.2f) * 0.75f + noise.snoise(q * 2.6f + 3.3f) * 0.25f);
            // Volcanic hotspots: about a quarter of the cells of a coarse grid hold one (if it's in high country), and
            // volcanism falls off with distance from it, so every volcanic area has a volcano at its heart and
            // ashlands around it.
            var g = xz / (math.max(s.ClimateSize, 1f) * 1.6f);
            var home = (int2)math.floor(g);
            var volcanism = 0f;
            for (int i = 0; i < 9; i++)
            {
                var c = home + new int2(i % 3 - 1, i / 3 - 1);
                var h = math.hash(new int3(c, (int)(s.Seed * 31u + 5u)));
                if ((h & 0xFF) >= 70)
                    continue;
                var spot = (float2)c + 0.2f + 0.6f * new float2((h >> 8) & 0xFFF, h >> 20) / 4095f;
                // Only in high country.
                var sq = spot * 1.6f + new float2(s.Seed % 89u, s.Seed % 97u) * 0.37f;
                if (Spread(noise.snoise(sq + 11.3f) * 0.7f + noise.snoise(sq * 2.3f - 5.1f) * 0.3f) < 0.45f)
                    continue;
                volcanism = math.max(volcanism, math.saturate(1f - math.distance(g, spot) / 0.36f));
            }
            return new float4(altitude, temperature, humidity, volcanism);
        }

        /// <summary>How well a climate suits a biome: 1 inside all its ranges, falling off outside them.</summary>
        public static float ClimateFit(ref BiomeBlobData b, float4 climate) => math.exp(-ClimateMisfit(ref b, climate));

        /// <summary>How far a climate is outside a biome's ranges (0 inside them), in units of the tolerance.</summary>
        static float ClimateMisfit(ref BiomeBlobData b, float4 climate)
        {
            var outside = math.max(math.max(b.ClimateMin - climate, climate - b.ClimateMax), 0f);
            // Volcanism is strict, so the ashlands give way to the volcano at a hotspot's heart.
            return math.lengthsq(outside / new float4(0.15f, 0.15f, 0.15f, 0.04f));
        }

        static int LookUp(int2 cell, in BiomeNoiseSettings s, ref BiomeSetBlob set, BlobAssetReference<BiomeCellTableBlob> table)
        {
            if (table.IsCreated)
            {
                var local = cell - table.Value.Min;
                if (math.all(local >= 0) && math.all(local < table.Value.Size))
                    return table.Value.Ids[local.y * table.Value.Size.x + local.x];
            }
            return CellBiome(cell, s, ref set);
        }

        /// <summary>A cell table entry for cells outside every realm (realms off).</summary>
        public const byte NoRealm = 255;

        /// <summary>
        /// Precomputes the biome of every cell that can reach within <paramref name="extent"/> of the origin.
        /// With <paramref name="realms"/>, each cell also gets a realm (see <see cref="CellRealms"/>); a cell keeps its
        /// biome if that's in its realm, and otherwise picks again among the realm's biomes. <paramref name="isLand"/>
        /// says which cells count when realms are shared out (all of them if null).
        /// </summary>
        public static BlobAssetReference<BiomeCellTableBlob> BuildCellTable(in BiomeNoiseSettings s, ref BiomeSetBlob set, float extent, Allocator allocator,
                                                                            RealmPlan realms = null, System.Func<float2, bool> isLand = null)
        {
            var reach = (extent + math.abs(s.WarpStrength)) / math.max(s.CellSize, 1f);
            var min = (int2)math.floor(-reach) - 2;
            var size = (int2)math.ceil(reach * 2f) + 5;
            var baseIds = new int[size.x * size.y];
            for (int i = 0; i < baseIds.Length; i++)
                baseIds[i] = CellBiome(min + new int2(i % size.x, i / size.x), s, ref set);
            var realmOf = realms != null && set.LandCount > 1 ? CellRealms(s, ref set, realms, min, size, baseIds, isLand) : null;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BiomeCellTableBlob>();
            root.Min = min;
            root.Size = size;
            var ids = builder.Allocate(ref root.Ids, size.x * size.y);
            var realmIds = builder.Allocate(ref root.Realms, size.x * size.y);
            for (int i = 0; i < baseIds.Length; i++)
            {
                if (realmOf == null)
                {
                    ids[i] = (byte)baseIds[i];
                    realmIds[i] = NoRealm;
                    continue;
                }
                var realm = realmOf[i];
                ids[i] = (byte)(System.Array.IndexOf(realms.Members[realm], baseIds[i]) >= 0
                    ? baseIds[i]
                    : RealmCellBiome(min + new int2(i % size.x, i / size.x), s, ref set, realms, realm));
                realmIds[i] = (byte)realm;
            }
            var blob = builder.CreateBlobAssetReference<BiomeCellTableBlob>(allocator);
            builder.Dispose();
            return blob;
        }

        /// <summary>The realm of the biome region nearest xz (from the cell table), or <see cref="NoRealm"/>.</summary>
        public static int RealmAt(in BiomeNoiseSettings s, BlobAssetReference<BiomeCellTableBlob> table, float2 xz)
        {
            if (!table.IsCreated || table.Value.Realms.Length == 0)
                return NoRealm;
            var local = NearestCell(s, Warp(s, xz)) - table.Value.Min;
            if (math.any(local < 0) || math.any(local >= table.Value.Size))
                return NoRealm;
            return table.Value.Realms[local.y * table.Value.Size.x + local.x];
        }

        /// <summary>Realm regions: warped Voronoi cells <see cref="RealmPlan.Scale"/> biome regions wide, on their own seed.</summary>
        static BiomeNoiseSettings RegionSettings(in BiomeNoiseSettings s, RealmPlan realms)
        {
            var r = s;
            r.CellSize = s.CellSize * realms.Scale;
            r.WarpStrength = s.WarpStrength * realms.Scale;
            r.Seed = s.Seed * 131u + 7u;
            return r;
        }

        /// <summary>
        /// Each biome cell's realm. The cells are grouped into realm regions, and each region's land cells vote with
        /// the realms their own biomes belong to, so realms go where their biomes already are (volcanoes keep their
        /// ashlands, glaciers their tundra). The rarest realm picks first, taking the region where its biomes are most
        /// common, so every realm with biomes on the island gets a region; the rest go to their top vote (× frequency).
        /// Last, along any border between two realms that mustn't touch, one side's cells become the buffer realm.
        /// </summary>
        static int[] CellRealms(in BiomeNoiseSettings s, ref BiomeSetBlob set, RealmPlan realms, int2 min, int2 size, int[] baseIds,
                                System.Func<float2, bool> isLand)
        {
            var count = realms.Frequency.Length;
            var rs = RegionSettings(s, realms);
            var regionCells = new int2[baseIds.Length];
            int2 lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < baseIds.Length; i++)
            {
                var cell = min + new int2(i % size.x, i / size.x);
                regionCells[i] = NearestCell(rs, Warp(rs, (cell + CellJitter(cell, s.Seed)) * s.CellSize));
                lo = math.min(lo, regionCells[i]);
                hi = math.max(hi, regionCells[i]);
            }
            var regionSize = hi - lo + 1;
            var regionCount = regionSize.x * regionSize.y;
            var regionOf = new int[baseIds.Length];
            var votes = new float[regionCount * count];
            var land = new int[regionCount];
            for (int i = 0; i < baseIds.Length; i++)
            {
                var local = regionCells[i] - lo;
                var g = regionOf[i] = local.y * regionSize.x + local.x;
                var cell = min + new int2(i % size.x, i / size.x);
                if (isLand != null && !isLand((cell + CellJitter(cell, s.Seed)) * s.CellSize))
                    continue;
                land[g]++;
                for (int r = 0; r < count; r++)
                {
                    var m = System.Array.IndexOf(realms.Members[r], baseIds[i]);
                    if (m >= 0)
                        votes[g * count + r] += realms.Weights[r][m];
                }
            }

            var regionRealm = new int[regionCount];
            for (int g = 0; g < regionCount; g++)
                regionRealm[g] = -1;
            // Rarest realm first: each takes the free land region with the largest share of its biomes.
            var totals = new float[count];
            for (int g = 0; g < regionCount; g++)
                for (int r = 0; r < count; r++)
                    totals[r] += votes[g * count + r];
            var order = new int[count];
            for (int r = 0; r < count; r++)
                order[r] = r;
            System.Array.Sort(order, (a, b) => totals[a] != totals[b] ? totals[a].CompareTo(totals[b]) : a.CompareTo(b));
            foreach (var r in order)
            {
                if (totals[r] <= 0f || realms.Frequency[r] <= 0f)
                    continue;
                var best = -1;
                var bestShare = 0f;
                for (int g = 0; g < regionCount; g++)
                {
                    if (regionRealm[g] >= 0 || land[g] == 0)
                        continue;
                    var share = votes[g * count + r] / land[g];
                    if (share > bestShare)
                    {
                        bestShare = share;
                        best = g;
                    }
                }
                if (best >= 0)
                    regionRealm[best] = r;
            }
            for (int g = 0; g < regionCount; g++)
            {
                if (regionRealm[g] >= 0)
                    continue;
                var bestScore = 0f;
                for (int r = 0; r < count; r++)
                {
                    var score = votes[g * count + r] * realms.Frequency[r];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        regionRealm[g] = r;
                    }
                }
                if (regionRealm[g] < 0)
                {
                    // No land cells with a listed biome: the realm whose biomes best suit its climate.
                    var cell = lo + new int2(g % regionSize.x, g / regionSize.x);
                    regionRealm[g] = NearestRealm(ref set, realms, Climate(s, (cell + CellJitter(cell, rs.Seed)) * rs.CellSize));
                }
            }

            var grouped = new int[baseIds.Length];
            for (int i = 0; i < grouped.Length; i++)
                grouped[i] = regionRealm[regionOf[i]];
            // A lone cell (no neighbour in its realm: a region's edge cutting across the biome grid) joins the realm
            // most common around it.
            var realm = (int[])grouped.Clone();
            var around = new int[count];
            for (int i = 0; i < realm.Length; i++)
            {
                int x = i % size.x, y = i / size.x;
                System.Array.Clear(around, 0, count);
                for (int n = 0; n < 9; n++)
                {
                    int nx = x + n % 3 - 1, ny = y + n / 3 - 1;
                    if (n != 4 && nx >= 0 && ny >= 0 && nx < size.x && ny < size.y)
                        around[grouped[ny * size.x + nx]]++;
                }
                if (around[grouped[i]] > 0)
                    continue;
                for (int r = 0; r < count; r++)
                    if (around[r] > around[realm[i]])
                        realm[i] = r;
            }
            if (realms.Buffer < 0)
                return realm;
            // Buffer strips: where two realms that mustn't touch meet, the bigger realm's cells become the buffer (so a
            // small realm isn't eaten away; ties: the later-listed one).
            var cells = new int[count];
            foreach (var r in realm)
                cells[r]++;
            var result = (int[])realm.Clone();
            for (int i = 0; i < realm.Length; i++)
            {
                int x = i % size.x, y = i / size.x;
                for (int n = 0; n < 9; n++)
                {
                    int nx = x + n % 3 - 1, ny = y + n / 3 - 1;
                    if (nx < 0 || ny < 0 || nx >= size.x || ny >= size.y)
                        continue;
                    var other = realm[ny * size.x + nx];
                    if (realms.Avoid[realm[i], other] && (cells[realm[i]] > cells[other] || cells[realm[i]] == cells[other] && realm[i] > other))
                    {
                        result[i] = realms.Buffer;
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>A region's biome within its realm: random, weighted by the realm's weights × frequency × climate.</summary>
        static int RealmCellBiome(int2 cell, in BiomeNoiseSettings s, ref BiomeSetBlob set, RealmPlan realms, int realm)
        {
            var members = realms.Members[realm];
            if (members.Length == 0)
                return CellBiome(cell, s, ref set);
            var climate = Climate(s, (cell + CellJitter(cell, s.Seed)) * s.CellSize);
            var scores = new float[members.Length];
            var total = 0f;
            for (int m = 0; m < members.Length; m++)
                total += scores[m] = realms.Weights[realm][m] * Suitability(ref set.Biomes[members[m]], climate, s.ClimateInfluence);
            if (total > 1e-20f)
                return members[Pick(scores, total, math.hash(new int3(cell, (int)(s.Seed * 7919u + 17u))))];
            // No biome of the realm suits this climate: the nearest fit.
            var best = members[0];
            var bestMisfit = float.MaxValue;
            foreach (var m in members)
            {
                var misfit = ClimateMisfit(ref set.Biomes[m], climate);
                if (misfit < bestMisfit)
                {
                    bestMisfit = misfit;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>The realm with the biome nearest to fitting a climate (for a climate no realm covers).</summary>
        static int NearestRealm(ref BiomeSetBlob set, RealmPlan realms, float4 climate)
        {
            var best = 0;
            var bestMisfit = float.MaxValue;
            for (int r = 0; r < realms.Members.Length; r++)
            {
                if (realms.Frequency[r] <= 0f)
                    continue;
                foreach (var m in realms.Members[r])
                {
                    var misfit = ClimateMisfit(ref set.Biomes[m], climate);
                    if (misfit < bestMisfit)
                    {
                        bestMisfit = misfit;
                        best = r;
                    }
                }
            }
            return best;
        }

        /// <summary>An index picked at random (hash), in proportion to the scores.</summary>
        static int Pick(float[] scores, float total, uint hash)
        {
            var pick = hash / (float)uint.MaxValue * total;
            for (int i = 0; i < scores.Length; i++)
            {
                pick -= scores[i];
                if (pick <= 0f && scores[i] > 0f)
                    return i;
            }
            for (int i = scores.Length - 1; i > 0; i--)
                if (scores[i] > 0f)
                    return i;
            return 0;
        }

        /// <summary>A region's biome: random, weighted by each biome's frequency and how well it suits the climate.</summary>
        static int CellBiome(int2 cell, in BiomeNoiseSettings s, ref BiomeSetBlob set)
        {
            if (set.LandCount <= 1)
                return 0;

            var climate = Climate(s, (cell + CellJitter(cell, s.Seed)) * s.CellSize);
            var total = 0f;
            for (int i = 0; i < set.LandCount; i++)
                total += Suitability(ref set.Biomes[i], climate, s.ClimateInfluence);
            // A climate no biome covers takes the nearest fit.
            if (total < 1e-20f)
            {
                var best = 0;
                var bestMisfit = float.MaxValue;
                for (int i = 0; i < set.LandCount; i++)
                {
                    var misfit = set.Biomes[i].Frequency > 0f ? ClimateMisfit(ref set.Biomes[i], climate) : float.MaxValue;
                    if (misfit < bestMisfit)
                    {
                        bestMisfit = misfit;
                        best = i;
                    }
                }
                return best;
            }

            var h = math.hash(new int3(cell, (int)(s.Seed * 7919u + 17u)));
            var pick = (h / (float)uint.MaxValue) * total;
            for (int i = 0; i < set.LandCount; i++)
            {
                pick -= Suitability(ref set.Biomes[i], climate, s.ClimateInfluence);
                if (pick <= 0f)
                    return i;
            }
            return set.LandCount - 1;
        }

        static float Suitability(ref BiomeBlobData b, float4 climate, float influence) =>
            b.Frequency * math.lerp(1f, ClimateFit(ref b, climate), influence);

        /// <summary>Height of one biome's relief above its base elevation, at xz (metres).</summary>
        public static float Relief(ref BiomeBlobData b, float2 xz)
        {
            if (b.Relief <= 0f)
                return 0f;

            // Offsets are added after scaling and kept small: float noise coordinates in the hundreds of
            // thousands lose enough precision to make the surface visibly jagged.
            var p = xz * b.FeatureFrequency;
            var o = b.NoiseOffset;
            float h;
            switch (b.Shape)
            {
                case TerrainShapeKind.Ridged:
                {
                    float sum = 0f, norm = 0f, amp = 1f, freq = 1f;
                    for (int i = 0; i < b.Octaves; i++)
                    {
                        var n = 1f - math.abs(noise.snoise(p * freq + o + i * 17.3f));
                        sum += n * n * amp;
                        norm += amp;
                        amp *= 0.5f;
                        freq *= 2.03f;
                    }
                    // Group ridges into massifs so the whole biome isn't uniformly crumpled.
                    var massif = math.saturate(noise.snoise(p * 0.3f + o + 71.3f) * 0.6f + 0.6f);
                    h = sum / norm * massif;
                    break;
                }
                case TerrainShapeKind.Dunes:
                {
                    // Crests run perpendicular to the wind; bend them with noise so they meander and merge.
                    var across = new float2(-b.Wind.y, b.Wind.x);
                    var along = math.dot(p, b.Wind);
                    var side = math.dot(p, across);
                    along += noise.snoise(new float2(along * 0.15f, side * 0.15f) + o) * 1.2f + noise.snoise(p * 0.5f + o) * 0.3f;
                    var phase = math.frac(along);
                    // Gentle windward slope (75% of the wavelength), steep lee face (25%).
                    var profile = phase < 0.75f ? phase / 0.75f : (1f - phase) / 0.25f;
                    var crestHeight = 0.55f + 0.45f * noise.snoise(p * 0.2f + o + 17f);
                    h = math.smoothstep(0f, 1f, profile) * crestHeight;
                    break;
                }
                default:
                {
                    float sum = 0f, norm = 0f, amp = 1f, freq = 1f;
                    for (int i = 0; i < b.Octaves; i++)
                    {
                        sum += amp * noise.snoise(p * freq + o + i * 17.3f);
                        norm += amp;
                        amp *= 0.5f;
                        freq *= 2.03f;
                    }
                    h = sum / norm * 0.5f + 0.5f;
                    break;
                }
            }

            h = math.pow(math.saturate(h), b.Sharpness);
            if (b.TerraceSteps > 0)
            {
                var t = h * b.TerraceSteps;
                h = (math.floor(t) + math.smoothstep(0.35f, 0.65f, math.frac(t))) / b.TerraceSteps;
            }
            return h * b.Relief;
        }

        /// <summary>Evaluates one biome's surface stack (base + rule layers) at a point.</summary>
        public static SurfaceProps EvaluateBiome(ref BiomeSetBlob set, int biomeIndex, in GroundContext g)
        {
            ref var biome = ref set.Biomes[biomeIndex];
            var props = SurfaceWithVariation(ref set, biome.BaseSurface, g.ColorNoise);

            for (int i = 0; i < biome.LayerCount; i++)
            {
                ref var layer = ref set.Layers[biome.FirstLayer + i];
                var mask = layer.Opacity;
                var edge = g.EdgeNoise * layer.EdgeNoise * 2f;
                if (layer.UseSlope != 0)
                    mask *= RangeMask(g.Slope + edge * layer.SlopeBlend, layer.SlopeRange, layer.SlopeBlend);
                if (layer.UseHeight != 0)
                    mask *= RangeMask(g.Height + edge * layer.HeightBlend, layer.HeightRange, layer.HeightBlend);
                if (layer.UseWaterDepth != 0)
                    mask *= RangeMask(g.WaterDepth + edge * layer.WaterDepthBlend, layer.WaterDepthRange, layer.WaterDepthBlend);
                if (layer.OnlyInRivers != 0)
                    mask *= g.River;
                if (layer.UsePatches != 0 && mask > 0f)
                {
                    var n = noise.snoise(g.Position * layer.PatchFrequency + layer.Surface * 13.7f) * 0.5f + 0.5f;
                    mask *= math.smoothstep(1f - layer.PatchCoverage - 0.08f, 1f - layer.PatchCoverage + 0.08f, n);
                }
                if (mask > 0f)
                    props = SurfaceProps.Lerp(props, SurfaceWithVariation(ref set, layer.Surface, g.ColorNoise), mask);
            }

            return props;
        }

        static SurfaceProps SurfaceWithVariation(ref BiomeSetBlob set, int surfaceIndex, float colorNoise)
        {
            if (surfaceIndex < 0 || surfaceIndex >= set.Surfaces.Length)
                return new SurfaceProps { Albedo = new float3(1f, 0f, 1f) }; // missing surface → magenta
            ref var surface = ref set.Surfaces[surfaceIndex];
            var props = surface.Props;
            props.Albedo *= 1f + colorNoise * surface.ColorVariation;
            return props;
        }

        static float RangeMask(float x, float2 range, float blend) =>
            math.smoothstep(range.x - blend, range.x + blend, x) *
            (1f - math.smoothstep(range.y - blend, range.y + blend, x));
    }
}
