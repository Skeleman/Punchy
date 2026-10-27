using System.Collections.Generic;
using System.IO;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.Editor
{
    [CustomEditor(typeof(TerrainAuthoring))]
    public class TerrainAuthoringEditor : UnityEditor.Editor
    {
        const string HeightmapFolder = "Assets/Game/Heightmaps";
        const string BiomeMapFolder = "Assets/Game/BiomeMaps";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var terrain = (TerrainAuthoring)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Export noise to heightmap + biome map images"))
                    ExportAndAssign(terrain);
            }

            if (terrain.HeightSource == HeightSourceKind.Heightmap || terrain.BiomeSource == BiomeSourceKind.Map)
                EditorGUILayout.HelpBox("Edit the PNGs in any image editor. Keep the heightmap 16-bit grayscale; paint the biome map " +
                                        "using each biome's Map Color. Unity re-imports on save and the terrain updates.", MessageType.Info);
        }

        static void ExportAndAssign(TerrainAuthoring terrain)
        {
            var (heightmap, biomeMap) = Export(terrain);
            Undo.RecordObject(terrain, "Assign exported maps");
            terrain.Heightmap = heightmap;
            terrain.BiomeMap = biomeMap;
            if (EditorUtility.DisplayDialog("Maps exported",
                    $"Saved a {heightmap.Image.width}px heightmap and a {biomeMap.Image.width}px biome map covering {heightmap.WorldSize} m:\n" +
                    $"{AssetDatabase.GetAssetPath(heightmap.Image)}\n{AssetDatabase.GetAssetPath(biomeMap.Image)}\n\n" +
                    "Switch the terrain to use them now?",
                    "Use maps", "Keep noise"))
            {
                terrain.HeightSource = HeightSourceKind.Heightmap;
                terrain.BiomeSource = BiomeSourceKind.Map;
            }
            EditorUtility.SetDirty(terrain);
            EditorGUIUtility.PingObject(heightmap);
        }

        /// <summary>
        /// Samples the noise world over a square centred on the origin and saves a 16-bit heightmap and an
        /// RGB biome map (each with its placement asset). Exported heights include biome height shaping.
        /// </summary>
        public static (HeightmapAsset heightmap, BiomeMapAsset biomeMap) Export(TerrainAuthoring terrain)
        {
            var palette = terrain.Palette();
            var worldSize = terrain.ExportWorldSize;
            var origin = new float2(-worldSize * 0.5f);

            // Always export the procedural world, whatever the terrain currently reads from.
            var source = terrain.BuildSource(Allocator.Persistent);
            try
            {
                var seedName = $"seed{terrain.World.Seed}_{terrain.BiomeNoise.Seed}";
                return (ExportHeightmap(terrain, source, origin, worldSize, seedName),
                        ExportBiomeMap(terrain, source, palette, origin, worldSize, seedName));
            }
            finally
            {
                if (source.Biomes.IsCreated)
                    source.Biomes.Dispose();
                if (source.Rivers.IsCreated)
                    source.Rivers.Dispose();
                if (source.BiomeCells.IsCreated)
                    source.BiomeCells.Dispose();
                if (source.Landmarks.IsCreated)
                    source.Landmarks.Dispose();
            }
        }

        static HeightmapAsset ExportHeightmap(TerrainAuthoring terrain, TerrainSource source, float2 origin, float worldSize, string seedName)
        {
            var resolution = math.max(2, terrain.ExportResolution);
            var heights = new NativeArray<float>(resolution * resolution, Allocator.TempJob);
            new SampleWorldJob
            {
                Source = source,
                Origin = origin,
                Step = worldSize / (resolution - 1),
                Resolution = resolution,
                Heights = heights
            }.Schedule(heights.Length, 1024).Complete();
            var values = heights.ToArray();
            heights.Dispose();

            // Normalise to the sampled range plus some headroom, so painted edits can go a bit higher or lower.
            var min = float.MaxValue;
            var max = float.MinValue;
            foreach (var h in values)
            {
                min = math.min(min, h);
                max = math.max(max, h);
            }
            var headroom = (max - min) * 0.1f + 1f;
            min = math.floor(min - headroom);
            max = math.ceil(max + headroom);

            Directory.CreateDirectory(HeightmapFolder);
            var pngPath = AssetDatabase.GenerateUniqueAssetPath($"{HeightmapFolder}/Heightmap_{seedName}.png");
            File.WriteAllBytes(pngPath, HeightmapIO.EncodePng(values, resolution, min, max));
            ImportImage(pngPath, resolution, linear: true);

            var asset = CreateInstance<HeightmapAsset>();
            asset.Image = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            asset.Origin = origin;
            asset.WorldSize = worldSize;
            asset.MinHeight = min;
            asset.MaxHeight = max;
            AssetDatabase.CreateAsset(asset, Path.ChangeExtension(pngPath, ".asset"));
            AssetDatabase.SaveAssets();
            return asset;
        }

        static BiomeMapAsset ExportBiomeMap(TerrainAuthoring terrain, TerrainSource source, List<BiomeDefinition> palette,
                                            float2 origin, float worldSize, string seedName)
        {
            var resolution = math.max(2, terrain.ExportBiomeResolution);
            var ids = new NativeArray<byte>(resolution * resolution, Allocator.TempJob);
            new SampleBiomeJob
            {
                Source = source,
                Origin = origin,
                Step = worldSize / (resolution - 1),
                Resolution = resolution,
                Ids = ids
            }.Schedule(ids.Length, 256).Complete();
            var values = ids.ToArray();
            ids.Dispose();

            Directory.CreateDirectory(BiomeMapFolder);
            var pngPath = AssetDatabase.GenerateUniqueAssetPath($"{BiomeMapFolder}/BiomeMap_{seedName}.png");
            File.WriteAllBytes(pngPath, BiomeBaking.EncodeBiomeMap(values, resolution, palette));
            ImportImage(pngPath, resolution, linear: false);

            var asset = CreateInstance<BiomeMapAsset>();
            asset.Image = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            asset.Origin = origin;
            asset.WorldSize = worldSize;
            AssetDatabase.CreateAsset(asset, Path.ChangeExtension(pngPath, ".asset"));
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void ImportImage(string path, int resolution, bool linear)
        {
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;

            // The terrain reads the PNG file directly; these settings just keep the preview texture faithful.
            importer.sRGBTexture = !linear;
            importer.mipmapEnabled = false;
            importer.filterMode = linear ? FilterMode.Bilinear : FilterMode.Point;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, Mathf.NextPowerOfTwo(resolution));
            importer.SaveAndReimport();
        }

        [BurstCompile]
        struct SampleWorldJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public float Step;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Heights;

            public void Execute(int index)
            {
                var xz = Origin + new float2(index % Resolution, index / Resolution) * Step;
                Heights[index] = Source.SampleHeight(xz);
            }
        }

        [BurstCompile]
        struct SampleBiomeJob : IJobParallelFor
        {
            public TerrainSource Source;
            public float2 Origin;
            public float Step;
            public int Resolution;
            [WriteOnly] public NativeArray<byte> Ids;

            public void Execute(int index)
            {
                var xz = Origin + new float2(index % Resolution, index / Resolution) * Step;
                Source.SampleBiomes(xz, out var surface, out _);
                Ids[index] = (byte)surface.Dominant();
            }
        }
    }
}
