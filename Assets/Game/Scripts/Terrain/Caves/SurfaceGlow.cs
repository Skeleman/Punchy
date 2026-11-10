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
    public partial class SurfaceGlow
    {
        /// <summary>How far round the camera each layer is searched (ground, shrubs, trees), in metres.</summary>
        static readonly float3 Radius = new float3(30f, 32f, 50f);
        const float MoveBeforeRescan = 4f;

        /// <summary>The far light map (W-101, Glow.hlsl): texels a side, metres a texel (640 m across).</summary>
        public const int MapSize = 160;
        public const float MapTexel = 4f;
        /// <summary>How far round the camera each layer is searched for the far map (ground, shrubs, trees), in metres,
        /// and in steps of how many cells (a skipped cell's light is made up by the ones taken: these are soft anyway).</summary>
        static readonly float3 FarRadius = new float3(70f, 200f, 300f);
        static readonly int3 FarStride = new int3(3, 2, 1);
        const float MoveBeforeFarRescan = 24f;

        JobHandle farJob;
        bool farRunning;
        NativeArray<float4> farPlants, farWindows;
        NativeList<float4> farFixtureAt, farFixtureLight;
        float2 farCorner;
        float3 farScannedAt = new float3(float.MaxValue);

        /// <summary>The far light maps (plants; lit windows) and their corner, once a far scan has finished.</summary>
        public Texture2D MapPlants, MapWindows;
        public float2 MapCorner;
        public bool MapReady;
        /// <summary>The last far scan's time from start to pick-up (ms; an upper bound on its worker time), for the frame-cost report.</summary>
        public float FarScanMs;
        System.Diagnostics.Stopwatch farClock;

        BlobAssetReference<FloraBlob> blob;
        NativeArray<float4> modelGlow;
        /// <summary>R-10 row 4c: glass crystals' glow a model (left out of modelGlow), for their far lights only.</summary>
        NativeArray<float4> modelCrystal;
        bool anyGlow;
        BiomePalette from;
        float3 cellSizes;

        JobHandle job;
        bool running;
        NativeList<float4> positions, colors;
        float3 scannedAt = new float3(float.MaxValue);

        /// <summary>The glowing plants found by the last finished scan: position (xyz) and light (rgb, w reach).</summary>
        public readonly List<(float3 position, Vector4 light)> Found = new List<(float3, Vector4)>();

        /// <summary>R-10: the street lights (tree layer) the last far scan found, as <see cref="Found"/> holds them, out to
        /// about <see cref="FarFixtureReach"/> from the camera, so they can stay real lights and glow slots well past the
        /// near scan's 50 m.</summary>
        public readonly List<(float3 position, Vector4 light)> FarFixtures = new List<(float3, Vector4)>();
        /// <summary>How far from the camera <see cref="FarFixtures"/> is sure to be complete (m).</summary>
        public static float FarFixtureReach => FarRadius.z - MoveBeforeFarRescan;

        /// <summary>Call once a frame; picks up a finished scan and starts a new one when the camera has moved.</summary>
        public void Update(in TerrainSource source, BiomePalette palette, FloraSystem flora, float3 camPos)
        {
            if (palette != from)
            {
                Release();
                Build(palette, flora);
                from = palette;
            }
            UpdateWorld(source);
            if (!anyGlow)
                return;
            if (running && job.IsCompleted)
            {
                job.Complete();
                running = false;
                Found.Clear();
                for (int i = 0; i < positions.Length; i++)
                    Found.Add((positions[i].xyz, colors[i]));
                GatherSpill(flora);
            }
            if (!running && math.distancesq(camPos, scannedAt) > MoveBeforeRescan * MoveBeforeRescan)
            {
                scannedAt = camPos;
                positions.Clear();
                colors.Clear();
                spillAt.Clear();
                spillModel.Clear();
                job = new ScanJob
                {
                    Flora = blob,
                    Source = source,
                    ModelGlow = modelGlow,
                    Centre = camPos.xz,
                    CellSizes = cellSizes,
                    Positions = positions,
                    Colors = colors,
                    SpillAt = spillAt,
                    SpillModel = spillModel
                }.Schedule();
                running = true;
            }
            UpdateFar(source, camPos);
        }

        /// <summary>The far map: picks up a finished scan (uploading its textures) and starts one when the camera has
        /// moved far enough. Its corner snaps to the texel grid, so texels two scans share come out the same.</summary>
        void UpdateFar(in TerrainSource source, float3 camPos)
        {
            if (farRunning && farJob.IsCompleted)
            {
                farJob.Complete();
                farRunning = false;
                FarScanMs = (float)farClock.Elapsed.TotalMilliseconds;
                MapPlants.SetPixelData(farPlants, 0);
                MapPlants.Apply(false, false);
                MapWindows.SetPixelData(farWindows, 0);
                MapWindows.Apply(false, false);
                FarFixtures.Clear();
                for (int i = 0; i < farFixtureAt.Length; i++)
                    FarFixtures.Add((farFixtureAt[i].xyz, farFixtureLight[i]));
                MapCorner = farCorner;
                MapReady = true;
            }
            if (!farRunning && math.distancesq(camPos.xz, farScannedAt.xz) > MoveBeforeFarRescan * MoveBeforeFarRescan)
            {
                farScannedAt = camPos;
                farCorner = math.floor(camPos.xz / MapTexel) * MapTexel - MapSize * MapTexel * 0.5f;
                farJob = new FarScanJob
                {
                    Flora = blob,
                    Source = source,
                    ModelGlow = modelGlow,
                    Corner = farCorner,
                    CellSizes = cellSizes,
                    Plants = farPlants,
                    Windows = farWindows,
                    FixtureAt = farFixtureAt,
                    FixtureLight = farFixtureLight
                }.Schedule();
                farClock = System.Diagnostics.Stopwatch.StartNew();
                farRunning = true;
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
            if (modelCrystal.IsCreated)
                modelCrystal.Dispose();
            modelCrystal = new NativeArray<float4>(math.max(f.Models.Length, 1), Allocator.Persistent);
            anyGlow = false;
            for (int s = 0; s < f.Rules.Length; s++)
            {
                var g = f.Glow[s];
                // Glass crystals stay out: a cluster has many at the same distance, so they took turns in the nearest
                // 16 as the camera moved and the crystals seemed to pulse (F-52). They still light caves' walls.
                if (g.w > 0f && species[s].Finish == FoliageFinish.Glass)
                    for (int v = 0; v < f.Rules[s].Variants; v++)
                        modelCrystal[f.Rules[s].FirstModel + v] = g;
                if (g.w == 0f || species[s].Finish == FoliageFinish.Glass)
                    continue;
                anyGlow = true;
                for (int v = 0; v < f.Rules[s].Variants; v++)
                    modelGlow[f.Rules[s].FirstModel + v] = g;
            }
            BuildWorldMask(palette, species, ref f);
            positions = new NativeList<float4>(256, Allocator.Persistent);
            colors = new NativeList<float4>(256, Allocator.Persistent);
            spillAt = new NativeList<float4>(64, Allocator.Persistent);
            spillModel = new NativeList<float2>(64, Allocator.Persistent);
            scannedAt = new float3(float.MaxValue);
            if (!anyGlow)
                return;
            farPlants = new NativeArray<float4>(MapSize * MapSize, Allocator.Persistent);
            farWindows = new NativeArray<float4>(MapSize * MapSize, Allocator.Persistent);
            farFixtureAt = new NativeList<float4>(256, Allocator.Persistent);
            farFixtureLight = new NativeList<float4>(256, Allocator.Persistent);
            MapPlants = NewMap("Far glow (plants)");
            MapWindows = NewMap("Far glow (windows)");
            farScannedAt = new float3(float.MaxValue);
        }

        static Texture2D NewMap(string name) => new Texture2D(MapSize, MapSize, TextureFormat.RGBAFloat, false, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave
        };

        public void Release()
        {
            if (running)
            {
                job.Complete();
                running = false;
            }
            if (farRunning)
            {
                farJob.Complete();
                farRunning = false;
            }
            ReleaseWorld();
            ReleaseSpill();
            ReleaseDomes();
            if (farPlants.IsCreated)
                farPlants.Dispose();
            if (farWindows.IsCreated)
                farWindows.Dispose();
            if (farFixtureAt.IsCreated)
                farFixtureAt.Dispose();
            if (farFixtureLight.IsCreated)
                farFixtureLight.Dispose();
            FarFixtures.Clear();
            if (MapPlants != null)
                Object.Destroy(MapPlants);
            if (MapWindows != null)
                Object.Destroy(MapWindows);
            MapPlants = MapWindows = null;
            MapReady = false;
            if (blob.IsCreated)
                blob.Dispose();
            if (modelGlow.IsCreated)
                modelGlow.Dispose();
            if (modelCrystal.IsCreated)
                modelCrystal.Dispose();
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
            /// <summary>W-127: lit-window plants within SpillRadius (the tree layer's houses): xyz position, w yaw;
            /// x its model, y its scale.</summary>
            public NativeList<float4> SpillAt;
            public NativeList<float2> SpillModel;

            public void Execute()
            {
                ref var f = ref Flora.Value;
                for (int layer = 0; layer < 3; layer++)
                {
                    var size = CellSizes[layer];
                    var near = Radius[layer];
                    var r = layer == 2 ? math.max(near, SpillRadius) : near;
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
                        var fixture = FloraPlacement.IsFixtureGlow(glow.w);
                        if (glow.w < 0f && !fixture)
                        {
                            SpillAt.Add(new float4(plant.Position, plant.Yaw));
                            SpillModel.Add(new float2(plant.Model, plant.Scale));
                        }
                        if (math.distancesq(((float2)cell + 0.5f) * size, Centre) > near * near)
                            continue;
                        // Lit from about the middle of the plant; a bigger one reaches further.
                        var scale = math.max(plant.Scale, 0.3f);
                        if (fixture)
                        {
                            // W-127: a street light, lit from its lamp, out in the street (night only, still marked).
                            Positions.Add(new float4(plant.Position + new float3(0f, StructureKit.FixtureLightHeight * scale, 0f), 0f));
                            Colors.Add(new float4(glow.xyz, -(FloraPlacement.FixtureGlowBase + FloraPlacement.GlowReach(glow.w) * math.sqrt(scale))));
                            continue;
                        }
                        Positions.Add(new float4(plant.Position + new float3(0f, 0.4f * scale, 0f), 0f));
                        Colors.Add(new float4(glow.xyz, glow.w * math.sqrt(scale)));   // negative: night only (lit windows)
                    }
                }
            }
        }

        /// <summary>
        /// Bakes the light every glowing plant and lit window within FarRadius casts on the ground into the far maps:
        /// a plant spreads over a wider, softer reach than its near light (at least 12 m, so it can't slip between texels)
        /// with the same total light; a street light keeps its own reach and brightness (at least 2.5 texels), so where the
        /// night map takes over from its glow slot it lights the same ground as brightly (R-10). The alpha sums each
        /// source's height times its light, for the shader's height test.
        /// </summary>
        [BurstCompile]
        struct FarScanJob : IJob
        {
            [ReadOnly] public BlobAssetReference<FloraBlob> Flora;
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float4> ModelGlow;
            public float2 Corner;
            public float3 CellSizes;
            public NativeArray<float4> Plants, Windows;
            /// <summary>The tree layer's street lights (not strided), laid out as ScanJob's Positions and Colors.</summary>
            public NativeList<float4> FixtureAt, FixtureLight;

            public void Execute()
            {
                for (int i = 0; i < Plants.Length; i++)
                    Plants[i] = Windows[i] = float4.zero;
                FixtureAt.Clear();
                FixtureLight.Clear();
                ref var f = ref Flora.Value;
                var centre = Corner + MapSize * MapTexel * 0.5f;
                for (int layer = 0; layer < 3; layer++)
                {
                    var size = CellSizes[layer];
                    var r = FarRadius[layer];
                    var step = FarStride[layer];
                    // Steps on the whole-island grid of cells (not from the corner), so scans agree where they overlap.
                    var lo = (int2)math.floor((centre - r) / size);
                    lo -= ((lo % step) + step) % step;
                    var hi = (int2)math.floor((centre + r) / size);
                    for (int y = lo.y; y <= hi.y; y += step)
                    for (int x = lo.x; x <= hi.x; x += step)
                    {
                        var cell = new int2(x, y);
                        if (math.distancesq(((float2)cell + 0.5f) * size, centre) > r * r)
                            continue;
                        var plant = FloraPlacement.Place(ref f, Source, cell, layer);
                        if (plant.Model < 0 || plant.Model >= ModelGlow.Length)
                            continue;
                        var glow = ModelGlow[plant.Model];
                        if (glow.w == 0f)
                            continue;
                        // W-127: the night map holds street lights only; a lit window's lamp is indoors (its spill
                        // panes light the street), so it no longer lights the ground through its walls from afar.
                        var fixture = FloraPlacement.IsFixtureGlow(glow.w);
                        if (glow.w < 0f && !fixture)
                            continue;
                        var scale = math.max(plant.Scale, 0.3f);
                        var reach = FloraPlacement.GlowReach(glow.w) * math.sqrt(scale);
                        var wide = fixture ? math.max(reach, MapTexel * 2.5f) : math.max(reach * 1.6f, 12f);
                        var light = glow.xyz * (reach * reach / (wide * wide) * step * step);
                        var lift = fixture ? StructureKit.FixtureLightHeight : 0.4f;
                        if (fixture && step == 1)
                        {
                            FixtureAt.Add(new float4(plant.Position + new float3(0f, lift * scale, 0f), 0f));
                            FixtureLight.Add(new float4(glow.xyz, -(FloraPlacement.FixtureGlowBase + reach)));
                        }
                        Splat(fixture ? Windows : Plants, plant.Position + new float3(0f, lift * scale, 0f), light, wide);
                    }
                }
            }

            void Splat(NativeArray<float4> map, float3 at, float3 light, float reach)
            {
                var lo = math.max((int2)math.floor((at.xz - reach - Corner) / MapTexel), 0);
                var hi = math.min((int2)math.floor((at.xz + reach - Corner) / MapTexel), MapSize - 1);
                for (int y = lo.y; y <= hi.y; y++)
                for (int x = lo.x; x <= hi.x; x++)
                {
                    var d = math.distance(Corner + (new float2(x, y) + 0.5f) * MapTexel, at.xz);
                    if (d >= reach)
                        continue;
                    var fall = 1f - d / reach;
                    var l = light * fall * fall;
                    map[y * MapSize + x] += new float4(l, at.y * (l.x + l.y + l.z));
                }
            }
        }
    }
}
