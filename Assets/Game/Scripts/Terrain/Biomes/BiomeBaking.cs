using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>Turns biome/surface ScriptableObjects and biome map images into Burst-readable blobs.</summary>
    public static class BiomeBaking
    {
        /// <summary>
        /// The palette every biome id refers to: land biomes in list order (empty slots skipped), then the ocean.
        /// Used by the blob, biome map images and anything else that turns ids back into biomes.
        /// </summary>
        /// <summary>A stable hash of a biome's name (FNV-1a): see BiomeBlobData.NameHash.</summary>
        public static uint NameHash(BiomeDefinition biome)
        {
            var h = 2166136261u;
            foreach (var ch in biome.name)
            {
                h ^= ch;
                h *= 16777619u;
            }
            return h;
        }

        public static List<BiomeDefinition> Palette(IEnumerable<BiomeDefinition> landBiomes, BiomeDefinition ocean)
        {
            var result = new List<BiomeDefinition>();
            foreach (var b in landBiomes)
                if (b != null && b != ocean)
                    result.Add(b);
            if (ocean != null)
                result.Add(ocean);
            return result;
        }

        /// <param name="palette">From <see cref="Palette"/>.</param>
        /// <param name="hasOcean">Whether the last palette entry is the ocean.</param>
        public static BlobAssetReference<BiomeSetBlob> BuildBiomeSet(IReadOnlyList<BiomeDefinition> palette, bool hasOcean, uint seed, Allocator allocator)
        {
            // Collect every distinct surface referenced by any biome.
            var surfaces = new List<TerrainSurface>();
            int Index(TerrainSurface s)
            {
                if (s == null)
                    return -1;
                var i = surfaces.IndexOf(s);
                if (i < 0)
                {
                    surfaces.Add(s);
                    i = surfaces.Count - 1;
                }
                return i;
            }

            var layerCount = 0;
            foreach (var b in palette)
                layerCount += b.Layers.Count;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BiomeSetBlob>();
            var biomeArray = builder.Allocate(ref root.Biomes, palette.Count);
            var layerArray = builder.Allocate(ref root.Layers, layerCount);
            root.LandCount = hasOcean ? palette.Count - 1 : palette.Count;
            root.OceanIndex = hasOcean ? palette.Count - 1 : -1;

            var layerCursor = 0;
            root.LandFrequency = 0f;
            for (int i = 0; i < palette.Count; i++)
            {
                var b = palette[i];
                var wind = math.radians(b.WindDirection);
                // Keyed on the name, not the palette index, so adding a biome doesn't move the others' relief.
                var nameHash = NameHash(b);
                var offsetHash = math.hash(new uint2(seed, nameHash * 2654435761u));
                biomeArray[i] = new BiomeBlobData
                {
                    Frequency = b.Frequency,
                    NameHash = nameHash,
                    BaseElevation = b.BaseElevation,
                    Shape = b.Shape,
                    Relief = b.Relief,
                    FeatureFrequency = 1f / math.max(b.FeatureSize, 1f),
                    Octaves = math.clamp(b.Octaves, 1, 8),
                    Sharpness = b.Sharpness,
                    TerraceSteps = b.TerraceSteps,
                    Wind = new float2(math.sin(wind), math.cos(wind)),
                    Rainfall = b.Rainfall,
                    Water = new float4(b.WaterColor.linear.r, b.WaterColor.linear.g, b.WaterColor.linear.b, b.WaterMurk),
                    WaterGlow = b.WaterGlow,
                    WaterLava = b.WaterLava,
                    // Each biome reads a different region of the noise field.
                    NoiseOffset = new float2(offsetHash & 0xFFFF, offsetHash >> 16) / 65535f * 500f,
                    ClimateMin = new float4(b.Altitude.x, b.Temperature.x, b.Humidity.x, b.Volcanism.x),
                    ClimateMax = new float4(b.Altitude.y, b.Temperature.y, b.Humidity.y, b.Volcanism.y),
                    BaseSurface = Index(b.BaseSurface),
                    FirstLayer = layerCursor,
                    LayerCount = b.Layers.Count
                };
                if (i < root.LandCount)
                    root.LandFrequency += b.Frequency;

                foreach (var l in b.Layers)
                {
                    layerArray[layerCursor++] = new LayerBlobData
                    {
                        Surface = Index(l.Surface),
                        UseSlope = l.UseSlope ? 1 : 0,
                        SlopeRange = l.SlopeRange,
                        SlopeBlend = math.max(l.SlopeBlend, 0.01f),
                        UseHeight = l.UseHeight ? 1 : 0,
                        HeightRange = l.HeightRange,
                        HeightBlend = math.max(l.HeightBlend, 0.01f),
                        UseWaterDepth = l.UseWaterDepth ? 1 : 0,
                        WaterDepthRange = l.WaterDepthRange,
                        WaterDepthBlend = math.max(l.WaterDepthBlend, 0.01f),
                        OnlyInRivers = l.OnlyInRivers ? 1 : 0,
                        UsePatches = l.UsePatches ? 1 : 0,
                        PatchFrequency = 1f / math.max(l.PatchSize, 1f),
                        PatchCoverage = l.PatchCoverage,
                        EdgeNoise = l.EdgeNoise,
                        Opacity = l.Opacity
                    };
                }
            }

            var surfaceArray = builder.Allocate(ref root.Surfaces, surfaces.Count);
            for (int i = 0; i < surfaces.Count; i++)
                surfaceArray[i] = SurfaceData(surfaces[i]);

            var blob = builder.CreateBlobAssetReference<BiomeSetBlob>(allocator);
            builder.Dispose();
            return blob;
        }

        /// <summary>A surface asset's look and features, for blobs.</summary>
        public static SurfaceBlobData SurfaceData(TerrainSurface s)
        {
            var albedo = s.Albedo.linear;
            var emission = s.Emission.linear;
            return new SurfaceBlobData
            {
                Props = new SurfaceProps
                {
                    Albedo = new float3(albedo.r, albedo.g, albedo.b),
                    Smoothness = s.Smoothness,
                    Metallic = s.Metallic,
                    Emission = new float3(emission.r, emission.g, emission.b),
                    Grass = s.GrassDensity,
                    Bump = s.Bumpiness,
                    Pebbles = s.Pebbles,
                    Strata = s.Strata,
                    Snow = s.SnowDepth
                },
                ColorVariation = s.ColorVariation
            };
        }

        /// <summary>Encodes biome indices as an RGB PNG using each biome's MapColor.</summary>
        public static byte[] EncodeBiomeMap(byte[] ids, int resolution, IReadOnlyList<BiomeDefinition> biomes)
        {
            var pixels = new Color32[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                pixels[i] = ids[i] < biomes.Count && biomes[ids[i]] != null ? (Color32)biomes[ids[i]].MapColor : new Color32(255, 0, 255, 255);

            var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            tex.SetPixels32(pixels);
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        /// <summary>Decodes a biome map PNG: each pixel becomes the index of the biome with the nearest MapColor.</summary>
        public static BlobAssetReference<BiomeMapBlob> DecodeBiomeMap(byte[] png, BiomeMapAsset asset, IReadOnlyList<BiomeDefinition> biomes, Allocator allocator)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (biomes.Count == 0 || !tex.LoadImage(png, false))
            {
                Object.DestroyImmediate(tex);
                return default;
            }

            var resolution = math.min(tex.width, tex.height);
            var pixels = tex.GetPixels32();
            var palette = new float3[biomes.Count];
            for (int i = 0; i < biomes.Count; i++)
            {
                var c = biomes[i] != null ? (Color32)biomes[i].MapColor : new Color32(255, 0, 255, 255);
                palette[i] = new float3(c.r, c.g, c.b);
            }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BiomeMapBlob>();
            root.Resolution = resolution;
            root.Origin = asset.Origin;
            root.WorldSize = asset.WorldSize;
            var ids = builder.Allocate(ref root.Ids, resolution * resolution);
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                var c = pixels[y * tex.width + x];
                var rgb = new float3(c.r, c.g, c.b);
                int best = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < palette.Length; i++)
                {
                    var d = math.distancesq(rgb, palette[i]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = i;
                    }
                }
                ids[y * resolution + x] = (byte)best;
            }

            Object.DestroyImmediate(tex);
            var blob = builder.CreateBlobAssetReference<BiomeMapBlob>(allocator);
            builder.Dispose();
            return blob;
        }
    }
}
