using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

namespace Punchy.Terrain
{
    /// <summary>
    /// Place one of these in the world subscene. Baking turns it into the TerrainConfig and
    /// TerrainSource singletons that the streaming system reads.
    /// </summary>
    public class TerrainAuthoring : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Material for sea, lake and river surfaces. Leave empty to hide water.")]
        public Material WaterMaterial;

        [Header("Chunks & LOD")]
        public float ChunkSize = 64f;
        [Tooltip("Quads per chunk edge at full detail. Power of two; each LOD halves it.")]
        public int Lod0Quads = 64;
        [Range(1, 6)] public int LodLevels = 4;
        [Tooltip("Full detail out to this distance; each further LOD covers double. Keep it at least ~2.5× Chunk Size " +
                 "so LODs have room to geomorph smoothly.")]
        public float Lod0Distance = 160f;
        [Range(1, 32)] public int ViewRadiusChunks = 12;
        [Range(1, 64)] public int MaxBuildsPerFrame = 12;
        [Tooltip("How far chunk edge skirts hang down to hide cracks between LODs.")]
        public float SkirtDepth = 6f;

        [Header("World")]
        public WorldSettings World = WorldSettings.Default;

        [Header("Biomes")]
        [Tooltip("Land biomes. The first is always used at the world origin. Order matters for biome map images.")]
        public List<BiomeDefinition> Biomes = new List<BiomeDefinition>();
        [Tooltip("Fills everything beyond the coast. Leave empty for an endless continent.")]
        public BiomeDefinition OceanBiome;
        public BiomeSourceKind BiomeSource = BiomeSourceKind.Noise;
        public BiomeNoiseSettings BiomeNoise = BiomeNoiseSettings.Default;
        [Tooltip("Used when Biome Source is Map. Create one with the Export button below.")]
        public BiomeMapAsset BiomeMap;

        [Header("Rivers")]
        public RiverSettings Rivers = RiverSettings.Default;

        [Header("Heights")]
        [FormerlySerializedAs("Source")]
        [Tooltip("Noise: heights come from the biomes' shapes. Heightmap: heights come from an image.")]
        public HeightSourceKind HeightSource = HeightSourceKind.Noise;
        [Tooltip("Used when Height Source is Heightmap. Create one with the Export button below.")]
        public HeightmapAsset Heightmap;

        [Header("Export (noise → images)")]
        [Tooltip("Heightmap pixels per side. 2^n + 1 keeps samples evenly spaced.")]
        public int ExportResolution = 4097;
        [Tooltip("Biome map pixels per side. Biomes change slowly, so this can be much lower.")]
        public int ExportBiomeResolution = 513;
        [Tooltip("Width of the exported square, centred on the origin. Should cover the landmass and some sea.")]
        public float ExportWorldSize = 9216f;

        /// <summary>All biomes in id order: land biomes (empty slots removed), then the ocean.</summary>
        public List<BiomeDefinition> Palette() => BiomeBaking.Palette(Biomes, OceanBiome);

        /// <summary>
        /// Builds the runtime TerrainSource (and its blobs) from these settings. Shared by the baker and the
        /// exporter. Heightmap/biome-map images are only loaded when given (editor-only callers pass them in).
        /// </summary>
        public TerrainSource BuildSource(Allocator allocator,
                                         BlobAssetReference<HeightmapBlob> heightmap = default,
                                         BlobAssetReference<BiomeMapBlob> biomeMap = default)
        {
            var palette = Palette();
            var source = new TerrainSource
            {
                World = World,
                HeightKind = heightmap.IsCreated ? HeightSourceKind.Heightmap : HeightSourceKind.Noise,
                Heightmap = heightmap,
                BiomeKind = biomeMap.IsCreated ? BiomeSourceKind.Map : BiomeSourceKind.Noise,
                BiomeMap = biomeMap,
                BiomeNoise = BiomeNoise,
                SpawnCell = BiomeSampling.SpawnCell(BiomeNoise),
                RiverSettings = Rivers
            };
            if (palette.Count > 0)
                source.Biomes = BiomeBaking.BuildBiomeSet(palette, OceanBiome != null, World.Seed, allocator);
            if (Rivers.Enabled && palette.Count > 0)
                source.Rivers = RiverBaking.Build(source, Rivers, allocator);
            return source;
        }

        class Baker : Baker<TerrainAuthoring>
        {
            public override void Bake(TerrainAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new TerrainConfig
                {
                    Material = authoring.Material,
                    WaterMaterial = authoring.WaterMaterial,
                    ChunkSize = authoring.ChunkSize,
                    Lod0Quads = Mathf.Max(1, Mathf.ClosestPowerOfTwo(authoring.Lod0Quads)),
                    LodLevels = authoring.LodLevels,
                    Lod0Distance = authoring.Lod0Distance,
                    ViewRadiusChunks = authoring.ViewRadiusChunks,
                    MaxBuildsPerFrame = authoring.MaxBuildsPerFrame,
                    SkirtDepth = authoring.SkirtDepth
                });

                var palette = authoring.Palette();
                // Re-bake whenever any biome or surface asset is edited.
                foreach (var biome in palette)
                {
                    DependsOn(biome);
                    DependsOn(biome.BaseSurface);
                    foreach (var layer in biome.Layers)
                        DependsOn(layer.Surface);
                }
                if (palette.Count == 0)
                    Debug.LogWarning("Terrain has no biomes assigned; it will be flat and grey.", authoring);
                AddComponentObject(entity, new BiomePalette { Biomes = palette.ToArray() });

                var heightmap = default(BlobAssetReference<HeightmapBlob>);
                if (authoring.HeightSource == HeightSourceKind.Heightmap)
                {
                    heightmap = BuildHeightmapBlob(authoring.Heightmap);
                    if (!heightmap.IsCreated)
                        Debug.LogWarning("Height source is Heightmap but no valid heightmap is assigned; using noise.", authoring);
                }

                var biomeMap = default(BlobAssetReference<BiomeMapBlob>);
                if (authoring.BiomeSource == BiomeSourceKind.Map)
                {
                    biomeMap = BuildBiomeMapBlob(authoring.BiomeMap, palette);
                    if (!biomeMap.IsCreated)
                        Debug.LogWarning("Biome source is Map but no valid biome map is assigned; using noise.", authoring);
                }

                var source = authoring.BuildSource(Allocator.Persistent, heightmap, biomeMap);
                if (source.Heightmap.IsCreated) AddBlobAsset(ref source.Heightmap, out _);
                if (source.BiomeMap.IsCreated) AddBlobAsset(ref source.BiomeMap, out _);
                if (source.Biomes.IsCreated) AddBlobAsset(ref source.Biomes, out _);
                if (source.Rivers.IsCreated) AddBlobAsset(ref source.Rivers, out _);
                AddComponent(entity, source);
            }

            // Both image loaders read the PNG file itself rather than the imported texture,
            // so import compression never loses precision.

            BlobAssetReference<HeightmapBlob> BuildHeightmapBlob(HeightmapAsset asset)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);
                return LoadHeightmap(asset);
#else
                return default;
#endif
            }

            BlobAssetReference<BiomeMapBlob> BuildBiomeMapBlob(BiomeMapAsset asset, List<BiomeDefinition> palette)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);
                var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
                return BiomeBaking.DecodeBiomeMap(System.IO.File.ReadAllBytes(path), asset, palette, Allocator.Persistent);
#else
                return default;
#endif
            }
        }

#if UNITY_EDITOR
        public static BlobAssetReference<HeightmapBlob> LoadHeightmap(HeightmapAsset asset)
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
            var heights = HeightmapIO.DecodePng(System.IO.File.ReadAllBytes(path), out var resolution);
            if (heights == null || resolution < 2)
                return default;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<HeightmapBlob>();
            root.Resolution = resolution;
            root.Origin = asset.Origin;
            root.WorldSize = asset.WorldSize;
            root.MinHeight = asset.MinHeight;
            root.HeightStep = (asset.MaxHeight - asset.MinHeight) / 65535f;
            var array = builder.Allocate(ref root.Heights, heights.Length);
            for (int i = 0; i < heights.Length; i++)
                array[i] = heights[i];

            var blob = builder.CreateBlobAssetReference<HeightmapBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
#endif
    }
}
