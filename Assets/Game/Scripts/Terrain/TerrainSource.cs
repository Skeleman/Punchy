using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    public enum HeightSourceKind : byte
    {
        Noise,
        Heightmap
    }

    /// <summary>Fractal (fBm) simplex noise parameters. Same settings + seed always give the same world.</summary>
    [Serializable]
    public struct NoiseSettings : IEquatable<NoiseSettings>
    {
        public uint Seed;
        [UnityEngine.Tooltip("Base feature frequency in cycles per metre. Smaller = broader hills.")]
        public float Frequency;
        [UnityEngine.Range(1, 10)] public int Octaves;
        [UnityEngine.Range(0f, 1f)] public float Persistence;
        public float Lacunarity;
        [UnityEngine.Tooltip("Maximum terrain height in metres (before biome height scaling).")]
        public float Amplitude;
        [UnityEngine.Tooltip("Values above 1 flatten valleys and sharpen peaks.")]
        public float Exponent;

        public static NoiseSettings Default => new NoiseSettings
        {
            Seed = 1337,
            Frequency = 0.0025f,
            Octaves = 6,
            Persistence = 0.5f,
            Lacunarity = 2f,
            Amplitude = 120f,
            Exponent = 1.8f
        };

        public float Sample(float2 xz)
        {
            // Hash the seed into a large offset so different seeds read different regions of the noise field.
            var offset = (float2)(new uint2(Seed * 0x9E3779B9u, (Seed + 1u) * 0x85EBCA6Bu) % 100000u);
            var p = xz + offset;

            float sum = 0f, norm = 0f, amp = 1f, freq = Frequency;
            for (int i = 0; i < Octaves; i++)
            {
                sum += amp * noise.snoise(p * freq);
                norm += amp;
                amp *= Persistence;
                freq *= Lacunarity;
            }

            var h = math.saturate(sum / math.max(norm, 1e-5f) * 0.5f + 0.5f);
            return math.pow(h, Exponent) * Amplitude;
        }

        public bool Equals(NoiseSettings o) =>
            Seed == o.Seed && Frequency == o.Frequency && Octaves == o.Octaves && Persistence == o.Persistence &&
            Lacunarity == o.Lacunarity && Amplitude == o.Amplitude && Exponent == o.Exponent;
    }

    /// <summary>A baked grid of heights covering a square region of the world. Sampled bilinearly, clamped at the edges.</summary>
    public struct HeightmapBlob
    {
        public int Resolution;
        public float2 Origin;
        public float WorldSize;
        public BlobArray<float> Heights;

        public float Sample(float2 xz)
        {
            var max = Resolution - 1;
            var uv = math.clamp((xz - Origin) / WorldSize * max, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;

            var row0 = i0.y * Resolution + i0.x;
            var row1 = row0 + Resolution;
            var bottom = math.lerp(Heights[row0], Heights[row0 + 1], f.x);
            var top = math.lerp(Heights[row1], Heights[row1 + 1], f.x);
            return math.lerp(bottom, top, f.y);
        }
    }

    /// <summary>
    /// Singleton that defines the world: its shape (heights) and its biomes (which region is what, and
    /// what surfaces cover it). Everything that needs ground height or ground material samples this,
    /// so terrain meshes, player movement and camera always agree.
    /// </summary>
    public struct TerrainSource : IComponentData, IEquatable<TerrainSource>
    {
        public HeightSourceKind HeightKind;
        public NoiseSettings Noise;
        public BlobAssetReference<HeightmapBlob> Heightmap;

        public BiomeSourceKind BiomeKind;
        public BiomeNoiseSettings BiomeNoise;
        public BlobAssetReference<BiomeMapBlob> BiomeMap;
        public BlobAssetReference<BiomeSetBlob> Biomes;

        public BiomeWeights SampleBiomes(float2 xz)
        {
            if (BiomeKind == BiomeSourceKind.Map && BiomeMap.IsCreated)
                return BiomeSampling.SampleMap(ref BiomeMap.Value, xz);
            if (Biomes.IsCreated)
                return BiomeSampling.SampleNoise(BiomeNoise, ref Biomes.Value, xz);
            var none = default(BiomeWeights);
            none.Normalize();
            return none;
        }

        public float SampleHeight(float2 xz)
        {
            if (HeightKind == HeightSourceKind.Heightmap && Heightmap.IsCreated)
                return Heightmap.Value.Sample(xz);
            return ShapeNoiseHeight(Noise.Sample(xz), SampleBiomes(xz));
        }

        /// <summary>Applies each biome's height scale/offset to the raw noise height, blended by biome weight.</summary>
        public float ShapeNoiseHeight(float rawHeight, in BiomeWeights biomes)
        {
            if (!Biomes.IsCreated || Biomes.Value.Biomes.Length == 0)
                return rawHeight;

            ref var set = ref Biomes.Value;
            float height = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = biomes.Weights[i];
                if (w <= 0f)
                    continue;
                ref var biome = ref set.Biomes[biomes.Ids[i]];
                height += w * (rawHeight * biome.HeightScale + biome.HeightOffset);
            }
            return height;
        }

        public float3 SampleNormal(float2 xz, float eps)
        {
            var dx = SampleHeight(xz + new float2(eps, 0f)) - SampleHeight(xz - new float2(eps, 0f));
            var dz = SampleHeight(xz + new float2(0f, eps)) - SampleHeight(xz - new float2(0f, eps));
            return math.normalize(new float3(-dx, 2f * eps, -dz));
        }

        /// <summary>Blended surface properties at a point, given its height and normal.</summary>
        public SurfaceProps SampleSurface(float2 xz, float height, float3 normal, in BiomeWeights biomes)
        {
            var result = default(SurfaceProps);
            if (!Biomes.IsCreated || Biomes.Value.Biomes.Length == 0)
            {
                result.Albedo = new float3(0.3f);
                result.Smoothness = 0.2f;
                return result;
            }

            var slope = math.degrees(math.acos(math.saturate(normal.y)));
            var edgeNoise = noise.snoise(xz * 0.045f) * 0.7f + noise.snoise(xz * 0.17f) * 0.3f;
            var colorNoise = noise.snoise(xz * 0.021f + 113.5f) * 0.6f + noise.snoise(xz * 0.11f - 57.1f) * 0.4f;

            ref var set = ref Biomes.Value;
            for (int i = 0; i < 4; i++)
            {
                var w = biomes.Weights[i];
                if (w > 0f)
                    result.AddScaled(BiomeSampling.EvaluateBiome(ref set, biomes.Ids[i], height, slope, edgeNoise, colorNoise), w);
            }
            return result;
        }

        public bool Equals(TerrainSource o) =>
            HeightKind == o.HeightKind && Noise.Equals(o.Noise) && Heightmap == o.Heightmap &&
            BiomeKind == o.BiomeKind && BiomeNoise.Equals(o.BiomeNoise) && BiomeMap == o.BiomeMap && Biomes == o.Biomes;
    }
}
