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

        [Header("Chunks & LOD")]
        public float ChunkSize = 64f;
        [Tooltip("Quads per chunk edge at full detail. Power of two; each LOD halves it.")]
        public int Lod0Quads = 64;
        [Range(1, 6)] public int LodLevels = 4;
        public float Lod0Distance = 96f;
        [Range(1, 32)] public int ViewRadiusChunks = 12;
        [Range(1, 64)] public int MaxBuildsPerFrame = 12;
        [Tooltip("How far chunk edge skirts hang down to hide cracks between LODs.")]
        public float SkirtDepth = 6f;

        [Header("Heights")]
        [FormerlySerializedAs("Source")]
        public HeightSourceKind HeightSource = HeightSourceKind.Noise;
        public NoiseSettings Noise = NoiseSettings.Default;
        [Tooltip("Used when Height Source is Heightmap. Create one with the Export button below.")]
        public HeightmapAsset Heightmap;

        [Header("Biomes")]
        [Tooltip("The first biome is always used at the world origin. Order matters for biome map images.")]
        public List<BiomeDefinition> Biomes = new List<BiomeDefinition>();
        public BiomeSourceKind BiomeSource = BiomeSourceKind.Noise;
        public BiomeNoiseSettings BiomeNoise = BiomeNoiseSettings.Default;
        [Tooltip("Used when Biome Source is Map. Create one with the Export button below.")]
        public BiomeMapAsset BiomeMap;

        [Header("Export (noise → images)")]
        [Tooltip("Heightmap pixels per side. 2^n + 1 keeps samples aligned with chunk vertices.")]
        public int ExportResolution = 1025;
        [Tooltip("Biome map pixels per side. Biomes change slowly, so this can be much lower.")]
        public int ExportBiomeResolution = 257;
        public float ExportWorldSize = 2048f;

        /// <summary>Biomes with empty slots removed; indices into this list are biome ids everywhere.</summary>
        public List<BiomeDefinition> ValidBiomes()
        {
            var result = new List<BiomeDefinition>();
            foreach (var b in Biomes)
                if (b != null)
                    result.Add(b);
            return result;
        }

        class Baker : Baker<TerrainAuthoring>
        {
            public override void Bake(TerrainAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new TerrainConfig
                {
                    Material = authoring.Material,
                    ChunkSize = authoring.ChunkSize,
                    Lod0Quads = Mathf.Max(1, Mathf.ClosestPowerOfTwo(authoring.Lod0Quads)),
                    LodLevels = authoring.LodLevels,
                    Lod0Distance = authoring.Lod0Distance,
                    ViewRadiusChunks = authoring.ViewRadiusChunks,
                    MaxBuildsPerFrame = authoring.MaxBuildsPerFrame,
                    SkirtDepth = authoring.SkirtDepth
                });

                var biomes = authoring.ValidBiomes();
                // Re-bake whenever any biome or surface asset is edited.
                foreach (var biome in biomes)
                {
                    DependsOn(biome);
                    DependsOn(biome.BaseSurface);
                    foreach (var layer in biome.Layers)
                        DependsOn(layer.Surface);
                }

                var source = new TerrainSource
                {
                    HeightKind = authoring.HeightSource,
                    Noise = authoring.Noise,
                    BiomeKind = authoring.BiomeSource,
                    BiomeNoise = authoring.BiomeNoise
                };

                if (biomes.Count > 0)
                {
                    var set = BiomeBaking.BuildBiomeSet(biomes, Allocator.Persistent);
                    AddBlobAsset(ref set, out _);
                    source.Biomes = set;
                }
                else
                {
                    Debug.LogWarning("Terrain has no biomes assigned; it will render grey.", authoring);
                }

                if (authoring.HeightSource == HeightSourceKind.Heightmap)
                {
                    var blob = BuildHeightmapBlob(authoring.Heightmap);
                    if (blob.IsCreated)
                    {
                        AddBlobAsset(ref blob, out _);
                        source.Heightmap = blob;
                    }
                    else
                    {
                        Debug.LogWarning("Height source is Heightmap but no valid heightmap is assigned; falling back to noise.", authoring);
                        source.HeightKind = HeightSourceKind.Noise;
                    }
                }

                if (authoring.BiomeSource == BiomeSourceKind.Map)
                {
                    var blob = BuildBiomeMapBlob(authoring.BiomeMap, biomes);
                    if (blob.IsCreated)
                    {
                        AddBlobAsset(ref blob, out _);
                        source.BiomeMap = blob;
                    }
                    else
                    {
                        Debug.LogWarning("Biome source is Map but no valid biome map is assigned; falling back to noise.", authoring);
                        source.BiomeKind = BiomeSourceKind.Noise;
                    }
                }

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

                var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
                var heights = HeightmapIO.DecodePng(System.IO.File.ReadAllBytes(path), asset.MinHeight, asset.MaxHeight, out var resolution);
                if (heights == null || resolution < 2)
                    return default;

                var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<HeightmapBlob>();
                root.Resolution = resolution;
                root.Origin = asset.Origin;
                root.WorldSize = asset.WorldSize;
                var array = builder.Allocate(ref root.Heights, heights.Length);
                for (int i = 0; i < heights.Length; i++)
                    array[i] = heights[i];

                var blob = builder.CreateBlobAssetReference<HeightmapBlob>(Allocator.Persistent);
                builder.Dispose();
                return blob;
#else
                return default;
#endif
            }

            BlobAssetReference<BiomeMapBlob> BuildBiomeMapBlob(BiomeMapAsset asset, List<BiomeDefinition> biomes)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);

                var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
                return BiomeBaking.DecodeBiomeMap(System.IO.File.ReadAllBytes(path), asset, biomes, Allocator.Persistent);
#else
                return default;
#endif
            }
        }
    }
}
