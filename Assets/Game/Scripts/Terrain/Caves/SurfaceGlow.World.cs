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
    /// is a single SetPixelData when it finishes. More static lights join through <see cref="AddWorldSource"/> before
    /// the bake.
    ///
    /// R-10 row 4 (Zach 11-09, a town glowing in the distance): the same bake also goes over every settlement cell by
    /// cell (no stride) and puts its lit windows and street lights into a second, one-channel map, <see cref="TownMap"/>.
    /// It has its own map because it follows the windows (on after dark, off by day) and fades in over a longer stretch
    /// than plants do. The bake also sums each town's light into <see cref="Towns"/>, and keeps every lit house and
    /// street light as a point (<see cref="TownPoints"/>), for the town lights seen from afar (SurfaceGlow.TownLights.cs).
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
        /// <summary>R-10 row 4: how far past a settlement's radius its cells are looked at (m), how far each lit house or
        /// street light's share of the town glow spreads (m, two texels), and how much each kind adds.</summary>
        const float TownMargin = 30f, TownReach = 36f, TownWindowLight = 1f, TownFixtureLight = 0.8f;
        /// <summary>R-10 row 4b: how high over its foot a lit house's windows and a street light's lamp are (m, at scale 1).</summary>
        const float TownWindowHeight = 1.6f, TownLampHeight = 3.2f;

        /// <summary>A town's light, for its dome: the light-weighted middle of its lit houses and street lights (y their
        /// height), how far they spread round it (m), and how much light they add up to (one lit house is about 1).</summary>
        public struct TownGlow
        {
            public float3 Centre;
            public float Radius, Light;
        }

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
        /// <summary>R-10 row 4: the towns' lit windows and street lights (one channel, over the same area as the world
        /// map), and each town's light, once the bake is done.</summary>
        public Texture2D TownMap;
        public readonly List<TownGlow> Towns = new List<TownGlow>();
        /// <summary>R-10 row 4b: every lit house and street light, xyz where its light is seen from afar, w its light
        /// (negative for a street light).</summary>
        public readonly List<float4> TownPoints = new List<float4>();

        NativeArray<float> modelFire;
        bool anyFire;
        NativeList<WorldSource> worldExtra;
        NativeArray<half4> worldOut;
        NativeArray<half> townOut;
        NativeList<TownGlow> townList;
        NativeList<float4> townPoints;
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
                TownMap = new Texture2D(WorldMapSize, WorldMapSize, TextureFormat.RHalf, false, true)
                {
                    name = "Town glow",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                TownMap.SetPixelData(townOut, 0);
                TownMap.Apply(false, true);
                Towns.Clear();
                for (int i = 0; i < townList.Length; i++)
                    Towns.Add(townList[i]);
                TownPoints.Clear();
                for (int i = 0; i < townPoints.Length; i++)
                    TownPoints.Add(townPoints[i]);
                domesDirty = true;
                WorldReady = true;
                Debug.Log($"SurfaceGlow: world glow baked in {WorldBakeMs:F0} ms: {worldCounts[0]} burning trees, " +
                          $"{worldCounts[1]} glowing plants, {worldCounts[2]} lava texels, {worldCounts[3]} lit houses and " +
                          $"{worldCounts[4]} street lights in {Towns.Count} towns, " +
                          $"{WorldMapSize * WorldMapSize * 10 / (1024 * 1024)} MB, {WorldExtent:F0} m across");
                worldOut.Dispose();
                townOut.Dispose();
                townList.Dispose();
                townPoints.Dispose();
                worldCounts.Dispose();
            }
            if (worldStarted || (!anyGlow && !anyFire))
                return;
            worldStarted = true;
            worldSource = source;
            WorldExtent = math.max(source.World.WorldSize, IslandV2.WorldSizeM);   // (the v3 island's maps are wider than the settings say)
            WorldCorner = new float2(-WorldExtent * 0.5f);
            worldOut = new NativeArray<half4>(WorldMapSize * WorldMapSize, Allocator.Persistent);
            townOut = new NativeArray<half>(WorldMapSize * WorldMapSize, Allocator.Persistent);
            townList = new NativeList<TownGlow>(64, Allocator.Persistent);
            townPoints = new NativeList<float4>(512, Allocator.Persistent);
            worldCounts = new NativeArray<int>(5, Allocator.Persistent);
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
                TownOutput = townOut,
                Towns = townList,
                TownPoints = townPoints,
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
            if (townOut.IsCreated)
                townOut.Dispose();
            if (townList.IsCreated)
                townList.Dispose();
            if (townPoints.IsCreated)
                townPoints.Dispose();
            if (worldCounts.IsCreated)
                worldCounts.Dispose();
            if (WorldMap != null)
                Object.Destroy(WorldMap);
            if (TownMap != null)
                Object.Destroy(TownMap);
            WorldMap = null;
            TownMap = null;
            Towns.Clear();
            TownPoints.Clear();
            domesDirty = true;
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
            public NativeArray<half> TownOutput;
            public NativeList<TownGlow> Towns;
            public NativeList<float4> TownPoints;
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
                BakeTowns(ref f, texel);
            }

            /// <summary>R-10 row 4: every settlement's lit houses and street lights, every cell, into the town map and
            /// the town list. A cell inside an earlier settlement's circle is left to that one, so none counts twice.</summary>
            void BakeTowns(ref FloraBlob f, float texel)
            {
                var town = new NativeArray<float>(WorldMapSize * WorldMapSize, Allocator.Temp);
                if (Source.Settlements.IsCreated)
                {
                    ref var all = ref Source.Settlements.Value.Settlements;
                    for (int s = 0; s < all.Length; s++)
                    {
                        var centre = all[s].Centre;
                        var r = all[s].Radius + TownMargin;
                        float sum = 0f, spread = 0f;
                        var weighted = float3.zero;
                        for (int layer = 1; layer < 3; layer++)
                        {
                            var size = CellSizes[layer];
                            var lo = (int2)math.floor((centre - r) / size);
                            var hi = (int2)math.floor((centre + r) / size);
                            for (int y = lo.y; y <= hi.y; y++)
                            for (int x = lo.x; x <= hi.x; x++)
                            {
                                var mid = (new float2(x, y) + 0.5f) * size;
                                if (math.distancesq(mid, centre) > r * r || InEarlierTown(ref all, s, mid))
                                    continue;
                                var plant = FloraPlacement.Place(ref f, Source, new int2(x, y), layer);
                                if (plant.Model < 0 || plant.Model >= ModelGlow.Length)
                                    continue;
                                var w = ModelGlow[plant.Model].w;
                                if (w >= 0f)
                                    continue;
                                var fixture = FloraPlacement.IsFixtureGlow(w);
                                Counts[fixture ? 4 : 3]++;
                                var light = (fixture ? TownFixtureLight : TownWindowLight) * math.sqrt(math.max(plant.Scale, 0.3f));
                                SplatTown(town, texel, plant.Position.xz, light);
                                // Seen from afar at a window's or a lamp's height.
                                TownPoints.Add(new float4(plant.Position + new float3(0f, (fixture ? TownLampHeight : TownWindowHeight) * plant.Scale, 0f), fixture ? -light : light));
                                sum += light;
                                weighted += plant.Position * light;
                                spread += math.lengthsq(plant.Position.xz) * light;
                            }
                        }
                        if (sum <= 0f)
                            continue;
                        var middle = weighted / sum;
                        // How far its lights spread: the light-weighted spread round their middle (an even disc of
                        // radius R spreads R / sqrt 2).
                        var radius = math.sqrt(math.max(spread / sum - math.lengthsq(middle.xz), 0f)) * math.SQRT2;
                        Towns.Add(new TownGlow { Centre = middle, Radius = math.max(radius, 20f), Light = sum });
                    }
                }
                for (int i = 0; i < town.Length; i++)
                    TownOutput[i] = new half(town[i]);
                town.Dispose();
            }

            static bool InEarlierTown(ref BlobArray<SettlementData> all, int s, float2 at)
            {
                for (int k = 0; k < s; k++)
                {
                    var r = all[k].Radius + TownMargin;
                    if (math.distancesq(at, all[k].Centre) <= r * r)
                        return true;
                }
                return false;
            }

            /// <summary>As Splat, one channel.</summary>
            void SplatTown(NativeArray<float> map, float texel, float2 at, float light)
            {
                var lo = math.max((int2)math.floor((at - TownReach - Corner) / texel), 0);
                var hi = math.min((int2)math.floor((at + TownReach - Corner) / texel), WorldMapSize - 1);
                for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var d = math.distance(Corner + (new float2(x, y) + 0.5f) * texel, at);
                    if (d >= TownReach)
                        continue;
                    var fall = 1f - d / TownReach;
                    map[y * WorldMapSize + x] += light * fall * fall;
                }
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
