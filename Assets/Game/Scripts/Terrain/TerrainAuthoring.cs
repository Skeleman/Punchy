using Unity.Entities;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Place one of these in the world subscene. Baking turns it into the TerrainConfig and
    /// TerrainHeightSource singletons that the streaming system reads.
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

        [Header("Height source")]
        public HeightSourceKind Source = HeightSourceKind.Noise;
        public NoiseSettings Noise = NoiseSettings.Default;
        [Tooltip("Used when Source is Heightmap. Create one with the Export button below.")]
        public HeightmapAsset Heightmap;

        [Header("Export (noise → heightmap image)")]
        [Tooltip("Pixels per side. 2^n + 1 keeps samples aligned with chunk vertices.")]
        public int ExportResolution = 1025;
        public float ExportWorldSize = 2048f;

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

                var source = new TerrainHeightSource
                {
                    Kind = authoring.Source,
                    Noise = authoring.Noise
                };

                if (authoring.Source == HeightSourceKind.Heightmap)
                {
                    var blob = BuildHeightmapBlob(authoring.Heightmap);
                    if (blob.IsCreated)
                    {
                        AddBlobAsset(ref blob, out _);
                        source.Heightmap = blob;
                    }
                    else
                    {
                        Debug.LogWarning("Terrain source is Heightmap but no valid heightmap is assigned; falling back to noise.", authoring);
                        source.Kind = HeightSourceKind.Noise;
                    }
                }

                AddComponent(entity, source);
            }

            BlobAssetReference<HeightmapBlob> BuildHeightmapBlob(HeightmapAsset asset)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);

                // Read the PNG file itself rather than the imported texture, so import compression never loses precision.
                var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
                var heights = HeightmapIO.DecodePng(System.IO.File.ReadAllBytes(path), asset.MinHeight, asset.MaxHeight, out var resolution);
                if (heights == null || resolution < 2)
                    return default;

                var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
                ref var root = ref builder.ConstructRoot<HeightmapBlob>();
                root.Resolution = resolution;
                root.Origin = asset.Origin;
                root.WorldSize = asset.WorldSize;
                var array = builder.Allocate(ref root.Heights, heights.Length);
                for (int i = 0; i < heights.Length; i++)
                    array[i] = heights[i];

                var blob = builder.CreateBlobAssetReference<HeightmapBlob>(Unity.Collections.Allocator.Persistent);
                builder.Dispose();
                return blob;
#else
                return default;
#endif
            }
        }
    }
}
