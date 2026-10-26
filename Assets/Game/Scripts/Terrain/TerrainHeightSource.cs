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
        [UnityEngine.Tooltip("Maximum terrain height in metres.")]
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
    /// Singleton that defines the shape of the world. Everything that needs ground height
    /// (terrain meshes, player movement, camera) samples this, so they always agree.
    /// </summary>
    public struct TerrainHeightSource : IComponentData, IEquatable<TerrainHeightSource>
    {
        public HeightSourceKind Kind;
        public NoiseSettings Noise;
        public BlobAssetReference<HeightmapBlob> Heightmap;

        public float SampleHeight(float2 xz) =>
            Kind == HeightSourceKind.Heightmap && Heightmap.IsCreated
                ? Heightmap.Value.Sample(xz)
                : Noise.Sample(xz);

        public float3 SampleNormal(float2 xz, float eps)
        {
            var dx = SampleHeight(xz + new float2(eps, 0f)) - SampleHeight(xz - new float2(eps, 0f));
            var dz = SampleHeight(xz + new float2(0f, eps)) - SampleHeight(xz - new float2(0f, eps));
            return math.normalize(new float3(-dx, 2f * eps, -dz));
        }

        public bool Equals(TerrainHeightSource o) =>
            Kind == o.Kind && Noise.Equals(o.Noise) && Heightmap == o.Heightmap;
    }
}
