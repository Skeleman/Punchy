using System;
using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-07 (#260): the island in edit mode, for the map editor. Rebuilds the subscene's TerrainSource from the
    /// TerrainAuthoring in the world scene and runs the same key / load-or-bake as IslandV2System, so the editor shows
    /// exactly the island play mode gets (and a miss here writes the cache play mode then hits). All heights come from
    /// <see cref="TerrainSource.SampleHeight"/>: the editor never computes ground itself.
    /// </summary>
    public sealed class MapIsland : IDisposable
    {
        public TerrainSource Source;
        public int Version;
        public bool CacheHit;
        public string Report;

        // Built by BuildSource and not freed by IslandCache.Free (they're not island blobs); the bake drops its
        // reference to the cell table, so both are kept here to dispose at the end.
        BlobAssetReference<BiomeSetBlob> biomes;
        BlobAssetReference<BiomeCellTableBlob> cells;
        // The ground under the edits at each 2 m edits vertex, filled as brushes reach new ground.
        readonly Dictionary<long, float> ground = new Dictionary<long, float>();

        public bool Loaded => Source.Biomes.IsCreated;
        public float SeaLevel => Source.World.SeaLevel;
        public float WorldSize => Source.World.WorldSize;

        /// <summary>Loads (or bakes) the island; null with the reason in <paramref name="error"/> when it can't.</summary>
        public static MapIsland Load(out string error)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int version = IslandV2.RequestedVersion();
            if (version == 1) { error = "island v1 is baked in the subscene; the map editor needs v2 or v3"; return null; }

            // The world scene holds the TerrainAuthoring; open it beside the current scene when it isn't open already.
            var scene = EditorSceneManager.GetSceneByPath(IslandCache.WorldScene);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(IslandCache.WorldScene, OpenSceneMode.Additive);
            TerrainSource source;
            BiomePalette palette;
            try
            {
                TerrainAuthoring authoring = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((authoring = root.GetComponentInChildren<TerrainAuthoring>(true)) != null) break;
                if (authoring == null) { error = "no TerrainAuthoring in " + IslandCache.WorldScene; return null; }
                if (authoring.HeightSource != HeightSourceKind.Noise || authoring.BiomeSource != BiomeSourceKind.Noise)
                { error = "the map editor expects noise height and biome sources (as the island bake does)"; return null; }
                EditorUtility.DisplayProgressBar("Map editor", "Building the island inputs", 0.1f);
                source = authoring.BuildSource(Allocator.Persistent);
                // The palette as the baker gives it (TerrainAuthoring.Baker).
                var biomeList = authoring.Palette();
                int[] biomeRealms = null;
                if (authoring.UseRealms && authoring.Realms != null)
                {
                    var plan = authoring.Realms.Plan(biomeList);
                    biomeRealms = new int[biomeList.Count];
                    for (int b = 0; b < biomeList.Count; b++)
                        biomeRealms[b] = Array.FindIndex(plan.Members, m => Array.IndexOf(m, b) >= 0);
                }
                palette = new BiomePalette { Biomes = biomeList.ToArray(), Landmarks = authoring.LandmarkTypes().ToArray(), BiomeRealms = biomeRealms, Placed = authoring.PlacedLandmarks, Roadside = authoring.Roadside };
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
            if (!source.Biomes.IsCreated) { EditorUtility.ClearProgressBar(); error = "the terrain has no biomes"; return null; }

            var island = new MapIsland { Version = version, biomes = source.Biomes, cells = source.BiomeCells };
            try
            {
                var types = IslandV2System.LandmarkTypes(palette);
                // M-07 r2b: the editor paints offsets over the unedited island, so it keys and bakes without the edits
                // file (play mode bakes with it; switching between them costs one re-bake each way).
                PaintedEdits.Suppress = true;
                string key = IslandCache.Key(ref source, palette, types, version, out string reason);
                // BuildSource laid out v1's places; the island replaces them, so free them first.
                // (Free clears every island blob field and leaves the inputs: Biomes, BiomeCells, Heightmap.)
                IslandCache.Free(ref source);
                EditorUtility.DisplayProgressBar("Map editor", "Loading the island from the cache", 0.3f);
                island.CacheHit = key != null && IslandCache.TryLoad(ref source, version, key, out reason);
                if (!island.CacheHit)
                {
                    EditorUtility.DisplayProgressBar("Map editor", $"Baking island v{version} (cache {reason})", 0.5f);
                    bool burst = BakeBurst.Check();
                    IslandV2System.Bake(ref source, palette, types, version);
                    if (key != null && burst) IslandCache.Save(ref source, version, key);
                }
                island.Source = source;
                island.Report = $"island v{version}: cache {(island.CacheHit ? "hit" : "miss")} ({reason}) in {watch.ElapsedMilliseconds} ms";
            }
            finally
            {
                PaintedEdits.Suppress = false;
                EditorUtility.ClearProgressBar();
            }
            error = null;
            return island;
        }

        public void Dispose()
        {
            if (!Loaded) return;
            IslandCache.Free(ref Source);
            if (biomes.IsCreated) biomes.Dispose();
            if (cells.IsCreated) cells.Dispose();
            Source = default;
            ground.Clear();
        }

        static long Key(int gx, int gz) => ((long)gx << 32) | (uint)gz;

        /// <summary>The island's ground at edits vertex (gx, gz). Call <see cref="Prefetch"/> over a brush first.</summary>
        public float Ground(int gx, int gz)
        {
            if (ground.TryGetValue(Key(gx, gz), out var h)) return h;
            h = Source.SampleHeight(new float2(gx, gz) * IslandEdits.Spacing);
            ground[Key(gx, gz)] = h;
            return h;
        }

        /// <summary>Samples (on worker threads) every edits vertex in [lo, hi] not sampled yet.</summary>
        public void Prefetch(int2 lo, int2 hi)
        {
            var want = new List<int2>();
            for (int gz = lo.y; gz <= hi.y; gz++)
                for (int gx = lo.x; gx <= hi.x; gx++)
                    if (!ground.ContainsKey(Key(gx, gz))) want.Add(new int2(gx, gz));
            if (want.Count == 0) return;
            var points = new NativeArray<float2>(want.Count, Allocator.TempJob);
            var heights = new NativeArray<float>(want.Count, Allocator.TempJob);
            for (int i = 0; i < want.Count; i++) points[i] = (float2)want[i] * IslandEdits.Spacing;
            new SampleJob { Source = Source, Points = points, Heights = heights }.Schedule(want.Count, 64).Complete();
            for (int i = 0; i < want.Count; i++) ground[Key(want[i].x, want[i].y)] = heights[i];
            points.Dispose(); heights.Dispose();
        }

        /// <summary>Heights of a grid (res x res, <paramref name="step"/> metres apart from <paramref name="origin"/>), sampled in parallel.</summary>
        public float[] SampleGrid(float2 origin, float step, int res)
        {
            var heights = new NativeArray<float>(res * res, Allocator.TempJob);
            new GridJob { Source = Source, Origin = origin, Step = step, Resolution = res, Heights = heights }.Schedule(res * res, 256).Complete();
            var result = heights.ToArray();
            heights.Dispose();
            return result;
        }

        [BurstCompile]
        struct SampleJob : IJobParallelFor
        {
            [ReadOnly] public TerrainSource Source;
            [ReadOnly] public NativeArray<float2> Points;
            [WriteOnly] public NativeArray<float> Heights;
            public void Execute(int i) => Heights[i] = Source.SampleHeight(Points[i]);
        }

        [BurstCompile]
        struct GridJob : IJobParallelFor
        {
            [ReadOnly] public TerrainSource Source;
            public float2 Origin;
            public float Step;
            public int Resolution;
            [WriteOnly] public NativeArray<float> Heights;
            public void Execute(int i) => Heights[i] = Source.SampleHeight(Origin + new float2(i % Resolution, i / Resolution) * Step);
        }
    }
}
