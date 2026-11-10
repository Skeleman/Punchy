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
    /// R-2 (feedback 11-09): the world-scale static glow. One low-resolution texture over the whole island, baked once
    /// after the world loads from the light sources that never move: burning trees, surface lava and glowing plants.
    /// Terrain reads it (Glow.hlsl, PunchyGlowWorld) past the 640 m far glow map, fading into it, so glowing ground shows
    /// from any distance and there's no gap on the way in.
    ///
    /// Red holds how much fire burns there, tinted in the shader with tonight's fire colour and gone by day (the colour
    /// changes every night, so it can't be baked); green, blue and alpha hold the fixed colour of plants and lava.
    /// Seed-derived and cosmetic: every machine bakes the same map and nothing is sent. Worker thread only; the upload
    /// is a single SetPixelData when it finishes. More kinds of light (window spill, town fixtures) join through
    /// <see cref="AddWorldSource"/> before the bake.
    /// </summary>
    public partial class SurfaceGlow
    {
        /// <summary>Texels a side (18 m each on the 9216 m island; 2 MB as RGBAHalf).</summary>
        public const int WorldMapSize = 512;
        /// <summary>Plants' and lava's soft reach, at least this (m): over two texels, so none slips between them.</summary>
        const float WorldMinReach = 40f;
        /// <summary>W-127: a beacon's fire in the world map's fire channel, and how far it reaches (m).</summary>
        const float BeaconFireLight = 12f, BeaconReach = 60f;
        /// <summary>Layers are sampled every this many cells (ground, shrubs, trees), a skipped cell's light made up by
        /// those taken: about one sample per 16 m for small plants, every second tree.</summary>
        static readonly int3 WorldStride = new int3(24, 8, 2);
        const float LavaReach = 30f;
        static readonly float3 LavaLight = new float3(1.6f, 0.6f, 0.12f);

        /// <summary>A static light to bake in as well (a window's spill, a town fixture): position, light, soft reach.</summary>
        public struct WorldSource
        {
            public float3 Position, Light;
            public float Reach;
        }

        /// <summary>The baked map, once finished (<see cref="WorldReady"/>), its corner and width (m), and the bake's time (ms).</summary>
        public Texture2D WorldMap;
        public float2 WorldCorner;
        public float WorldExtent;
        public bool WorldReady;
        public float WorldBakeMs;

        NativeArray<float> modelFire;
        bool anyFire;
        NativeList<WorldSource> worldExtra;
        NativeArray<half4> worldOut;
        NativeArray<int> worldCounts;
        JobHandle worldJob;
        bool worldRunning, worldStarted;
        TerrainSource worldSource;
        System.Diagnostics.Stopwatch worldClock;

        /// <summary>"-worldglow 0" turns the world glow off, for comparing shots with and without it.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReadWorldGlowFlag()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-worldglow");
            if (at >= 0 && at + 1 < args.Length && args[at + 1] == "0")
                CaveSystem.WorldGlowStrength = 0f;
        }

        /// <summary>Adds a static light to the next bake (none is running or done yet, or it waits for the next world).</summary>
        public void AddWorldSource(in WorldSource source)
        {
            if (worldExtra.IsCreated)
                worldExtra.Add(source);
        }

        /// <summary>Marks the burning species' models, from the palette's Burning biomes (as the forest fires find them).</summary>
        void BuildWorldMask(BiomePalette palette, List<FloraSpecies> species, ref FloraBlob f)
        {
            modelFire = new NativeArray<float>(math.max(f.Models.Length, 1), Allocator.Persistent);
            worldExtra = new NativeList<WorldSource>(16, Allocator.Persistent);
            anyFire = false;
            var burning = new HashSet<FloraSpecies>();
            foreach (var biome in palette.Biomes)
                if (biome != null && biome.HasTag(BiomeTags.Burning))
                    foreach (var entry in biome.Flora)
                        if (entry.Species != null)
                            burning.Add(entry.Species);
            for (int s = 0; s < f.Rules.Length; s++)
            {
                if (!burning.Contains(species[s]))
                    continue;
                anyFire = true;
                for (int v = 0; v < f.Rules[s].Variants; v++)
                    modelFire[f.Rules[s].FirstModel + v] = 1f;
            }
            // W-127: a beacon tower's fire lights its square in the fire channel too.
            for (int i = 0; i < f.Fixtures.Length; i++)
                if (f.Fixtures[i].Spot == (int)FixtureSpot.Beacon)
                    anyFire = true;
        }

        /// <summary>Starts the bake once per world, and picks it up when it's done.</summary>
        void UpdateWorld(in TerrainSource source)
        {
            if (worldStarted && !source.Equals(worldSource))
                ResetWorld();
            if (worldRunning && worldJob.IsCompleted)
            {
                worldJob.Complete();
                worldRunning = false;
                WorldBakeMs = (float)worldClock.Elapsed.TotalMilliseconds;
                WorldMap = new Texture2D(WorldMapSize, WorldMapSize, TextureFormat.RGBAHalf, false, true)
                {
                    name = "World glow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                WorldMap.SetPixelData(worldOut, 0);
                WorldMap.Apply(false, true);   // (no CPU copy kept)
                WorldReady = true;
                Debug.Log($"SurfaceGlow: world glow baked in {WorldBakeMs:F0} ms: {worldCounts[0]} burning trees, " +
                          $"{worldCounts[1]} glowing plants, {worldCounts[2]} lava texels, {WorldMapSize * WorldMapSize * 8 / (1024 * 1024)} MB, {WorldExtent:F0} m across");
                worldOut.Dispose();
                worldCounts.Dispose();
            }
            if (worldStarted || (!anyGlow && !anyFire))
                return;
            worldStarted = true;
            worldSource = source;
            WorldExtent = math.max(source.World.WorldSize, IslandV2.WorldSizeM);   // (the v3 island's maps are wider than the settings say)
            WorldCorner = new float2(-WorldExtent * 0.5f);
            worldOut = new NativeArray<half4>(WorldMapSize * WorldMapSize, Allocator.Persistent);
            worldCounts = new NativeArray<int>(3, Allocator.Persistent);
            worldClock = System.Diagnostics.Stopwatch.StartNew();
            worldJob = new WorldBakeJob
            {
                Flora = blob,
                Source = source,
                ModelGlow = modelGlow,
                ModelFire = modelFire,
                Extra = worldExtra.AsArray(),
                Corner = WorldCorner,
                Extent = WorldExtent,
                CellSizes = cellSizes,
                Output = worldOut,
                Counts = worldCounts
            }.Schedule();
            worldRunning = true;
        }

        /// <summary>Drops the map and any bake in progress (a new world or palette; the extra sources stay).</summary>
        void ResetWorld()
        {
            if (worldRunning)
            {
                worldJob.Complete();
                worldRunning = false;
            }
            if (worldOut.IsCreated)
                worldOut.Dispose();
            if (worldCounts.IsCreated)
                worldCounts.Dispose();
            if (WorldMap != null)
                Object.Destroy(WorldMap);
            WorldMap = null;
            WorldReady = false;
            worldStarted = false;
        }

        void ReleaseWorld()
        {
            ResetWorld();
            if (modelFire.IsCreated)
                modelFire.Dispose();
            if (worldExtra.IsCreated)
                worldExtra.Dispose();
            anyFire = false;
        }

        [BurstCompile]
        struct WorldBakeJob : IJob
        {
            [ReadOnly] public BlobAssetReference<FloraBlob> Flora;
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float4> ModelGlow;
            [ReadOnly] public NativeArray<float> ModelFire;
            [ReadOnly] public NativeArray<WorldSource> Extra;
            public float2 Corner;
            public float Extent;
            public float3 CellSizes;
            public NativeArray<half4> Output;
            public NativeArray<int> Counts;

            public void Execute()
            {
                var sums = new NativeArray<float4>(WorldMapSize * WorldMapSize, Allocator.Temp);
                var texel = Extent / WorldMapSize;
                ref var f = ref Flora.Value;
                for (int layer = 0; layer < 3; layer++)
                {
                    var size = CellSizes[layer];
                    var step = WorldStride[layer];
                    // Steps on the whole-island grid of cells, as the far map's do.
                    var lo = (int2)math.floor(Corner / size);
                    lo -= ((lo % step) + step) % step;
                    var hi = (int2)math.floor((Corner + Extent) / size);
                    for (int y = lo.y; y <= hi.y; y += step)
                    for (int x = lo.x; x <= hi.x; x += step)
                    {
                        var plant = FloraPlacement.Place(ref f, Source, new int2(x, y), layer);
                        if (plant.Model < 0 || plant.Model >= ModelGlow.Length)
                            continue;
                        if (layer > 0 && ModelFire[plant.Model] > 0f)
                        {
                            Counts[0]++;
                            // Each tree's light spread over a soft 40 m, so a lone tree is faint and a forest adds up.
                            Splat(sums, texel, plant.Position.xz, new float4(0.3f * step * step, 0f, 0f, 0f), WorldMinReach);
                        }
                        var glow = ModelGlow[plant.Model];
                        // (Lit windows, negative, stay out: their light is indoor. Spill joins as an extra source.)
                        if (glow.w <= 0f)
                            continue;
                        Counts[1]++;
                        var reach = glow.w * math.sqrt(math.max(plant.Scale, 0.3f));
                        var wide = math.max(reach * 1.6f, WorldMinReach);
                        var light = glow.xyz * (reach * reach / (wide * wide) * step * step);
                        Splat(sums, texel, plant.Position.xz, new float4(0f, light), wide);
                    }
                }
                // Surface lava (lava biomes, volcano craters): one look at each texel's middle.
                for (int y = 0; y < WorldMapSize; y++)
                for (int x = 0; x < WorldMapSize; x++)
                {
                    var at = Corner + (new float2(x, y) + 0.5f) * texel;
                    var ground = Source.SampleGround(at);
                    if (!ground.HasWater)
                        continue;
                    Source.WaterLook(at, ground.Biomes, out var look);
                    if (look.y <= 0f)
                        continue;
                    Counts[2]++;
                    Splat(sums, texel, at, new float4(0f, LavaLight * look.y), LavaReach);
                }
                // W-127: each beacon tower's fire, as a burning grove's worth of fire light round its square.
                if (Source.Settlements.IsCreated)
                    for (int s = 0; s < Source.Settlements.Value.Settlements.Length; s++)
                        if (FloraPlacement.BeaconSpot(ref f, Source, s, out var beacon, out _, out _, out _, out _))
                        {
                            Counts[0]++;
                            Splat(sums, texel, beacon, new float4(BeaconFireLight, 0f, 0f, 0f), BeaconReach);
                        }
                for (int i = 0; i < Extra.Length; i++)
                    Splat(sums, texel, Extra[i].Position.xz, new float4(0f, Extra[i].Light), math.max(Extra[i].Reach, WorldMinReach));
                for (int i = 0; i < sums.Length; i++)
                    Output[i] = new half4(sums[i]);
                sums.Dispose();
            }

            void Splat(NativeArray<float4> map, float texel, float2 at, float4 value, float reach)
            {
                var lo = math.max((int2)math.floor((at - reach - Corner) / texel), 0);
                var hi = math.min((int2)math.floor((at + reach - Corner) / texel), WorldMapSize - 1);
                for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var d = math.distance(Corner + (new float2(x, y) + 0.5f) * texel, at);
                    if (d >= reach)
                        continue;
                    var fall = 1f - d / reach;
                    map[y * WorldMapSize + x] += value * (fall * fall);
                }
            }
        }
    }
}
