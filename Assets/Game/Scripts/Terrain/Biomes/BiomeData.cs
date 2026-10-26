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
        public float2 NoiseOffset;
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

        public static BiomeNoiseSettings Default => new BiomeNoiseSettings
        {
            Seed = 7,
            CellSize = 1400f,
            BlendDistance = 120f,
            ShapeBlendDistance = 450f,
            WarpStrength = 350f
        };

        public bool Equals(BiomeNoiseSettings o) =>
            Seed == o.Seed && CellSize == o.CellSize && BlendDistance == o.BlendDistance &&
            ShapeBlendDistance == o.ShapeBlendDistance && WarpStrength == o.WarpStrength;
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
        public static BiomeCells Cells(in BiomeNoiseSettings s, ref BiomeSetBlob set, int2 spawnCell, float2 xz)
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
                cells.Biomes.Add(math.all(c == spawnCell) ? 0 : CellBiome(c, s.Seed, ref set));
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

        static int CellBiome(int2 cell, uint seed, ref BiomeSetBlob set)
        {
            if (set.LandCount <= 1)
                return 0;

            var h = math.hash(new int3(cell, (int)(seed * 7919u + 17u)));
            var pick = (h / (float)uint.MaxValue) * set.LandFrequency;
            for (int i = 0; i < set.LandCount; i++)
            {
                pick -= set.Biomes[i].Frequency;
                if (pick <= 0f)
                    return i;
            }
            return set.LandCount - 1;
        }

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
