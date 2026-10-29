using System.Collections.Generic;
using Punchy.Flora;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Terrain
{
    /// <summary>
    /// Finds the glowing plants growing on the surface near the camera (glowbells, swamp shrubs, lit windows), so
    /// their light can join the cave glow list and fall on the ground, plants and creatures round them. The flora
    /// system keeps its placed plants to itself, so this asks the same placement rules for the cells near the camera
    /// (in a Burst job, picked up a frame later, redone each time the camera moves a few metres). Seed-derived, so
    /// every machine finds the same lights.
    /// </summary>
    public class SurfaceGlow
    {
        /// <summary>How far round the camera each layer is searched (ground, shrubs, trees), in metres.</summary>
        static readonly float3 Radius = new float3(22f, 32f, 50f);
        const float MoveBeforeRescan = 4f;

        BlobAssetReference<FloraBlob> blob;
        NativeArray<float4> modelGlow;
        bool anyGlow;
        BiomePalette from;
        float3 cellSizes;

        JobHandle job;
        bool running;
        NativeList<float4> positions, colors;
        float3 scannedAt = new float3(float.MaxValue);

        /// <summary>The glowing plants found by the last finished scan: position (xyz) and light (rgb, w reach).</summary>
        public readonly List<(float3 position, Vector4 light)> Found = new List<(float3, Vector4)>();

        /// <summary>Call once a frame; picks up a finished scan and starts a new one when the camera has moved.</summary>
        public void Update(in TerrainSource source, BiomePalette palette, FloraSystem flora, float3 camPos)
        {
            if (palette != from)
            {
                Release();
                Build(palette, flora);
                from = palette;
            }
            if (!anyGlow)
                return;
            if (running && job.IsCompleted)
            {
                job.Complete();
                running = false;
                Found.Clear();
                for (int i = 0; i < positions.Length; i++)
                    Found.Add((positions[i].xyz, colors[i]));
            }
            if (!running && math.distancesq(camPos, scannedAt) > MoveBeforeRescan * MoveBeforeRescan)
            {
                scannedAt = camPos;
                positions.Clear();
                colors.Clear();
                job = new ScanJob
                {
                    Flora = blob,
                    Source = source,
                    ModelGlow = modelGlow,
                    Centre = camPos.xz,
                    CellSizes = cellSizes,
                    Positions = positions,
                    Colors = colors
                }.Schedule();
                running = true;
            }
        }

        void Build(BiomePalette palette, FloraSystem flora)
        {
            cellSizes = flora != null
                ? new float3(FloraPlacement.CellSize(flora.Ground.TileSize, flora.Ground.CellSize),
                             FloraPlacement.CellSize(flora.Shrubs.TileSize, flora.Shrubs.CellSize),
                             FloraPlacement.CellSize(flora.Trees.TileSize, flora.Trees.CellSize))
                : new float3(0.7f, 2f, 4.5f);
            var species = new List<FloraSpecies>();
            blob = FloraPlacement.Build(palette, cellSizes, species);
            ref var f = ref blob.Value;
            modelGlow = new NativeArray<float4>(math.max(f.Models.Length, 1), Allocator.Persistent);
            anyGlow = false;
            for (int s = 0; s < f.Rules.Length; s++)
            {
                var g = f.Glow[s];
                // Glass crystals stay out: a cluster has many at the same distance, so they took turns in the nearest
                // 16 as the camera moved and the crystals seemed to pulse (F-52). They still light caves' walls.
                if (g.w == 0f || species[s].Finish == FoliageFinish.Glass)
                    continue;
                anyGlow = true;
                for (int v = 0; v < f.Rules[s].Variants; v++)
                    modelGlow[f.Rules[s].FirstModel + v] = g;
            }
            positions = new NativeList<float4>(256, Allocator.Persistent);
            colors = new NativeList<float4>(256, Allocator.Persistent);
            scannedAt = new float3(float.MaxValue);
        }

        public void Release()
        {
            if (running)
            {
                job.Complete();
                running = false;
            }
            if (blob.IsCreated)
                blob.Dispose();
            if (modelGlow.IsCreated)
                modelGlow.Dispose();
            if (positions.IsCreated)
                positions.Dispose();
            if (colors.IsCreated)
                colors.Dispose();
            Found.Clear();
            from = null;
            anyGlow = false;
        }

        [BurstCompile]
        struct ScanJob : IJob
        {
            [ReadOnly] public BlobAssetReference<FloraBlob> Flora;
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float4> ModelGlow;
            public float2 Centre;
            public float3 CellSizes;
            public NativeList<float4> Positions, Colors;

            public void Execute()
            {
                ref var f = ref Flora.Value;
                for (int layer = 0; layer < 3; layer++)
                {
                    var size = CellSizes[layer];
                    var r = Radius[layer];
                    var lo = (int2)math.floor((Centre - r) / size);
                    var hi = (int2)math.floor((Centre + r) / size);
                    for (int y = lo.y; y <= hi.y; y++)
                    for (int x = lo.x; x <= hi.x; x++)
                    {
                        var cell = new int2(x, y);
                        if (math.distancesq(((float2)cell + 0.5f) * size, Centre) > r * r)
                            continue;
                        var plant = FloraPlacement.Place(ref f, Source, cell, layer);
                        if (plant.Model < 0 || plant.Model >= ModelGlow.Length)
                            continue;
                        var glow = ModelGlow[plant.Model];
                        if (glow.w == 0f)
                            continue;
                        // Lit from about the middle of the plant; a bigger one reaches further.
                        var scale = math.max(plant.Scale, 0.3f);
                        Positions.Add(new float4(plant.Position + new float3(0f, 0.4f * scale, 0f), 0f));
                        Colors.Add(new float4(glow.xyz, glow.w * math.sqrt(scale)));   // negative: night only (lit windows)
                    }
                }
            }
        }
    }
}
