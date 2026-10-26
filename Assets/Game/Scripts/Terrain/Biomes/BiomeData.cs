using System;
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

        public static SurfaceProps Lerp(in SurfaceProps a, in SurfaceProps b, float t) => new SurfaceProps
        {
            Albedo = math.lerp(a.Albedo, b.Albedo, t),
            Smoothness = math.lerp(a.Smoothness, b.Smoothness, t),
            Metallic = math.lerp(a.Metallic, b.Metallic, t),
            Emission = math.lerp(a.Emission, b.Emission, t),
            Grass = math.lerp(a.Grass, b.Grass, t),
            Snow = math.lerp(a.Snow, b.Snow, t)
        };

        public void AddScaled(in SurfaceProps s, float w)
        {
            Albedo += s.Albedo * w;
            Smoothness += s.Smoothness * w;
            Metallic += s.Metallic * w;
            Emission += s.Emission * w;
            Grass += s.Grass * w;
            Snow += s.Snow * w;
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
        public float EdgeNoise;
        public float Opacity;
    }

    public struct BiomeBlobData
    {
        public float HeightScale;
        public float HeightOffset;
        public float Frequency;
        public int BaseSurface;
        public int FirstLayer;
        public int LayerCount;
    }

    /// <summary>All biome and surface definitions, flattened for Burst.</summary>
    public struct BiomeSetBlob
    {
        public BlobArray<SurfaceBlobData> Surfaces;
        public BlobArray<LayerBlobData> Layers;
        public BlobArray<BiomeBlobData> Biomes;
        public float TotalFrequency;
    }

    /// <summary>Biome index per pixel, covering a square region of the world.</summary>
    public struct BiomeMapBlob
    {
        public int Resolution;
        public float2 Origin;
        public float WorldSize;
        public BlobArray<byte> Ids;
    }

    /// <summary>Settings for procedurally placed biome regions: warped Voronoi cells, one biome per cell.</summary>
    [Serializable]
    public struct BiomeNoiseSettings : IEquatable<BiomeNoiseSettings>
    {
        public uint Seed;
        [UnityEngine.Tooltip("Typical width of one biome region, in metres.")]
        public float CellSize;
        [UnityEngine.Tooltip("Width of the transition zone between neighbouring biomes, in metres.")]
        public float BlendDistance;
        [UnityEngine.Tooltip("How far (metres) region borders are pushed around to look organic.")]
        public float WarpStrength;

        public static BiomeNoiseSettings Default => new BiomeNoiseSettings
        {
            Seed = 7,
            CellSize = 900f,
            BlendDistance = 120f,
            WarpStrength = 250f
        };

        public bool Equals(BiomeNoiseSettings o) =>
            Seed == o.Seed && CellSize == o.CellSize && BlendDistance == o.BlendDistance && WarpStrength == o.WarpStrength;
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
    }

    public static class BiomeSampling
    {
        /// <summary>Warped-Voronoi biome regions with smooth blends across cell borders.</summary>
        public static BiomeWeights SampleNoise(in BiomeNoiseSettings s, ref BiomeSetBlob set, float2 xz)
        {
            var result = default(BiomeWeights);
            if (set.Biomes.Length == 0)
            {
                result.Normalize();
                return result;
            }

            var seedOffset = new float2(s.Seed * 0.6180339f, s.Seed * 0.4142135f) * 1000f;
            var warpFreq = 1f / math.max(s.CellSize, 1f);
            var warp = new float2(
                noise.snoise((xz + seedOffset) * warpFreq * 2f),
                noise.snoise((xz - seedOffset) * warpFreq * 2f + 31.7f));
            var p = (xz + warp * s.WarpStrength) / s.CellSize;
            var cell = (int2)math.floor(p);

            // Distance to each feature point in the 3x3 neighbourhood.
            var distances = new FixedFloats();
            float nearest = float.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                var c = cell + new int2(i % 3 - 1, i / 3 - 1);
                distances[i] = math.distance(p, c + CellJitter(c, s.Seed));
                nearest = math.min(nearest, distances[i]);
            }

            // Weight each point by how much farther it is than the nearest one. Uses only the nearest
            // *distance* (continuous everywhere), never *which* point is nearest (which jumps at borders),
            // so weights and therefore heights stay smooth. Near a border, d - nearest ≈ 2 × distance to it.
            var blend = math.max(s.BlendDistance, 1e-3f) / s.CellSize;
            for (int i = 0; i < 9; i++)
            {
                var weight = math.smoothstep(0f, 1f, 1f - (distances[i] - nearest) / blend);
                var c = cell + new int2(i % 3 - 1, i / 3 - 1);
                result.Add(CellBiome(c, s.Seed, ref set), weight);
            }

            result.Normalize();
            return result;
        }

        /// <summary>Bilinear blend of the four nearest biome-map pixels.</summary>
        public static BiomeWeights SampleMap(ref BiomeMapBlob map, float2 xz)
        {
            var result = default(BiomeWeights);
            var max = map.Resolution - 1;
            var uv = math.clamp((xz - map.Origin) / map.WorldSize * max, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;
            var row = i0.y * map.Resolution + i0.x;
            result.Add(map.Ids[row], (1f - f.x) * (1f - f.y));
            result.Add(map.Ids[row + 1], f.x * (1f - f.y));
            result.Add(map.Ids[row + map.Resolution], (1f - f.x) * f.y);
            result.Add(map.Ids[row + map.Resolution + 1], f.x * f.y);
            result.Normalize();
            return result;
        }

        static float2 CellJitter(int2 cell, uint seed)
        {
            var h = math.hash(new int3(cell, (int)seed));
            return 0.1f + 0.8f * new float2(h & 0xFFFF, h >> 16) / 65535f;
        }

        static int CellBiome(int2 cell, uint seed, ref BiomeSetBlob set)
        {
            // The region at the world origin always uses the first biome, so the start area is predictable.
            if (cell.x == 0 && cell.y == 0)
                return 0;

            var h = math.hash(new int3(cell, (int)(seed * 7919u + 17u)));
            var pick = (h / (float)uint.MaxValue) * set.TotalFrequency;
            for (int i = 0; i < set.Biomes.Length; i++)
            {
                pick -= set.Biomes[i].Frequency;
                if (pick <= 0f)
                    return i;
            }
            return set.Biomes.Length - 1;
        }

        /// <summary>Evaluates one biome's surface stack (base + rule layers) at a point.</summary>
        public static SurfaceProps EvaluateBiome(ref BiomeSetBlob set, int biomeIndex, float height, float slopeDegrees,
                                                 float edgeNoise, float colorNoise)
        {
            ref var biome = ref set.Biomes[biomeIndex];
            var props = SurfaceWithVariation(ref set, biome.BaseSurface, colorNoise);

            for (int i = 0; i < biome.LayerCount; i++)
            {
                ref var layer = ref set.Layers[biome.FirstLayer + i];
                var mask = layer.Opacity;
                if (layer.UseSlope != 0)
                    mask *= RangeMask(slopeDegrees + edgeNoise * layer.EdgeNoise * layer.SlopeBlend * 2f, layer.SlopeRange, layer.SlopeBlend);
                if (layer.UseHeight != 0)
                    mask *= RangeMask(height + edgeNoise * layer.EdgeNoise * layer.HeightBlend * 2f, layer.HeightRange, layer.HeightBlend);
                if (mask > 0f)
                    props = SurfaceProps.Lerp(props, SurfaceWithVariation(ref set, layer.Surface, colorNoise), mask);
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

        /// <summary>Nine floats on the stack (Burst-friendly, no allocation).</summary>
        struct FixedFloats
        {
            float p0, p1, p2, p3, p4, p5, p6, p7, p8;

            public float this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0: return p0; case 1: return p1; case 2: return p2;
                        case 3: return p3; case 4: return p4; case 5: return p5;
                        case 6: return p6; case 7: return p7; default: return p8;
                    }
                }
                set
                {
                    switch (i)
                    {
                        case 0: p0 = value; break; case 1: p1 = value; break; case 2: p2 = value; break;
                        case 3: p3 = value; break; case 4: p4 = value; break; case 5: p5 = value; break;
                        case 6: p6 = value; break; case 7: p7 = value; break; default: p8 = value; break;
                    }
                }
            }
        }
    }
}
