using System.IO;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.Editor
{
    [CustomEditor(typeof(TerrainAuthoring))]
    public class TerrainAuthoringEditor : UnityEditor.Editor
    {
        const string ExportFolder = "Assets/Game/Heightmaps";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var terrain = (TerrainAuthoring)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Export noise to heightmap image"))
                    ExportAndAssign(terrain);
            }

            if (terrain.Source == HeightSourceKind.Heightmap && terrain.Heightmap != null)
                EditorGUILayout.HelpBox("Edit the heightmap PNG in any image editor (keep it 16-bit grayscale for best precision). " +
                                        "Unity re-imports it on save and the terrain updates.", MessageType.Info);
        }

        static void ExportAndAssign(TerrainAuthoring terrain)
        {
            var asset = Export(terrain);
            Undo.RecordObject(terrain, "Assign exported heightmap");
            terrain.Heightmap = asset;
            if (EditorUtility.DisplayDialog("Heightmap exported",
                    $"Saved {asset.Image.width}x{asset.Image.height} heightmap covering {asset.WorldSize} m to\n" +
                    $"{AssetDatabase.GetAssetPath(asset.Image)}\n\nSwitch the terrain to use it now?",
                    "Use heightmap", "Keep noise"))
                terrain.Source = HeightSourceKind.Heightmap;
            EditorUtility.SetDirty(terrain);
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>Samples the terrain's noise over a square centred on the origin and saves it as a PNG + HeightmapAsset.</summary>
        public static HeightmapAsset Export(TerrainAuthoring terrain)
        {
            var resolution = math.max(2, terrain.ExportResolution);
            var worldSize = terrain.ExportWorldSize;
            var origin = new float2(-worldSize * 0.5f);

            var heights = new NativeArray<float>(resolution * resolution, Allocator.TempJob);
            new SampleNoiseJob
            {
                Noise = terrain.Noise,
                Origin = origin,
                Step = worldSize / (resolution - 1),
                Resolution = resolution,
                Heights = heights
            }.Schedule(heights.Length, 1024).Complete();

            var values = heights.ToArray();
            heights.Dispose();

            // Normalise to the full 0..Amplitude range rather than the sampled min/max, so edits have headroom.
            var min = 0f;
            var max = terrain.Noise.Amplitude;

            Directory.CreateDirectory(ExportFolder);
            var baseName = $"Heightmap_seed{terrain.Noise.Seed}";
            var pngPath = AssetDatabase.GenerateUniqueAssetPath($"{ExportFolder}/{baseName}.png");
            File.WriteAllBytes(pngPath, HeightmapIO.EncodePng(values, resolution, min, max));
            AssetDatabase.ImportAsset(pngPath);
            ConfigureImporter(pngPath, resolution);

            var asset = CreateInstance<HeightmapAsset>();
            asset.Image = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            asset.Origin = origin;
            asset.WorldSize = worldSize;
            asset.MinHeight = min;
            asset.MaxHeight = max;
            var assetPath = Path.ChangeExtension(pngPath, ".asset");
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void ConfigureImporter(string path, int resolution)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;

            // The terrain reads the PNG file directly; these settings just keep the preview texture faithful.
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, Mathf.NextPowerOfTwo(resolution));
            importer.SaveAndReimport();
        }

        [BurstCompile]
        struct SampleNoiseJob : IJobParallelFor
        {
            public NoiseSettings Noise;
            public float2 Origin;
            public float Step;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Heights;

            public void Execute(int index)
            {
                var xz = Origin + new float2(index % Resolution, index / Resolution) * Step;
                Heights[index] = Noise.Sample(xz);
            }
        }
    }
}
