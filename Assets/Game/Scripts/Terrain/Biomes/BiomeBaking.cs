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
        public static BlobAssetReference<BiomeSetBlob> BuildBiomeSet(IReadOnlyList<BiomeDefinition> biomes, Allocator allocator)
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

            var validBiomes = new List<BiomeDefinition>();
            foreach (var b in biomes)
                if (b != null)
                    validBiomes.Add(b);

            var layerCount = 0;
            foreach (var b in validBiomes)
                layerCount += b.Layers.Count;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<BiomeSetBlob>();
            var biomeArray = builder.Allocate(ref root.Biomes, validBiomes.Count);
            var layerArray = builder.Allocate(ref root.Layers, layerCount);

            var layerCursor = 0;
            root.TotalFrequency = 0f;
            for (int i = 0; i < validBiomes.Count; i++)
            {
                var b = validBiomes[i];
                biomeArray[i] = new BiomeBlobData
                {
                    HeightScale = b.HeightScale,
                    HeightOffset = b.HeightOffset,
                    Frequency = b.Frequency,
                    BaseSurface = Index(b.BaseSurface),
                    FirstLayer = layerCursor,
                    LayerCount = b.Layers.Count
                };
                root.TotalFrequency += b.Frequency;

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
                        EdgeNoise = l.EdgeNoise,
                        Opacity = l.Opacity
                    };
                }
            }

            var surfaceArray = builder.Allocate(ref root.Surfaces, surfaces.Count);
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                var albedo = s.Albedo.linear;
                var emission = s.Emission.linear;
                surfaceArray[i] = new SurfaceBlobData
                {
                    Props = new SurfaceProps
                    {
                        Albedo = new float3(albedo.r, albedo.g, albedo.b),
                        Smoothness = s.Smoothness,
                        Metallic = s.Metallic,
                        Emission = new float3(emission.r, emission.g, emission.b),
                        Grass = s.GrassDensity,
                        Snow = s.SnowDepth
                    },
                    ColorVariation = s.ColorVariation
                };
            }

            var blob = builder.CreateBlobAssetReference<BiomeSetBlob>(allocator);
            builder.Dispose();
            return blob;
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
