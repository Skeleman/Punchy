using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Terrain
{
    public enum HeightSourceKind : byte
    {
        Noise,
        Heightmap
    }

    /// <summary>World-wide layout: how big the landmass is, where the sea is, and the master seed for terrain shapes.</summary>
    [Serializable]
    public struct WorldSettings : IEquatable<WorldSettings>
    {
        public uint Seed;
        [UnityEngine.Tooltip("Approximate diameter of the landmass in metres. Beyond it is ocean (when an Ocean biome is set).")]
        public float WorldSize;
        [UnityEngine.Tooltip("Height of the sea surface in metres. Heights, water depth and beaches are relative to this.")]
        public float SeaLevel;
        [UnityEngine.Tooltip("Width of the transition from land elevations down to the sea floor, in metres.")]
        public float CoastWidth;
        [UnityEngine.Tooltip("How far the coastline wanders in and out, as a fraction of the world radius.")]
        [UnityEngine.Range(0f, 0.6f)] public float CoastlineRoughness;

        public static WorldSettings Default => new WorldSettings
        {
            Seed = 1337,
            WorldSize = 8192f,
            SeaLevel = 0f,
            CoastWidth = 900f,
            CoastlineRoughness = 0.3f
        };

        public bool Equals(WorldSettings o) =>
            Seed == o.Seed && WorldSize == o.WorldSize && SeaLevel == o.SeaLevel &&
            CoastWidth == o.CoastWidth && CoastlineRoughness == o.CoastlineRoughness;
    }

    /// <summary>
    /// A baked grid of heights covering a square region of the world, stored as 16-bit values between MinHeight
    /// and MaxHeight (half the memory of floats; a 4097² map is 32 MB). Sampled bilinearly, clamped at the edges.
    /// </summary>
    public struct HeightmapBlob
    {
        public int Resolution;
        public float2 Origin;
        public float WorldSize;
        public float MinHeight;
        public float HeightStep;   // metres per 16-bit step
        public BlobArray<ushort> Heights;

        public float Sample(float2 xz)
        {
            var max = Resolution - 1;
            var uv = math.clamp((xz - Origin) / WorldSize * max, 0f, max);
            var i0 = math.min((int2)math.floor(uv), max - 1);
            var f = uv - i0;

            var row0 = i0.y * Resolution + i0.x;
            var row1 = row0 + Resolution;
            var bottom = math.lerp(Heights[row0], Heights[row0 + 1], f.x);
            var top = math.lerp(Heights[row1], Heights[row1 + 1], f.x);
            return MinHeight + math.lerp(bottom, top, f.y) * HeightStep;
        }
    }

    /// <summary>Everything known about the ground at one point.</summary>
    public struct GroundSample
    {
        public float Height;
        public BiomeWeights Biomes;   // narrow (surface) blend
        public float River;           // 0..1 channel mask
        /// <summary>
        /// Metres below the nearest water surface (negative = above it), for surface rules. Smoothed at the edges
        /// of lake/river zones; use <see cref="WaterSurface"/> for the exact surface.
        /// </summary>
        public float WaterDepth;
        /// <summary>Height of the nearby water body's surface, or <see cref="NoWater"/> where there is none.</summary>
        public float WaterSurface;
        /// <summary>Direction river water flows here (unit length in rivers, zero in still water).</summary>
        public float2 Flow;
        /// <summary>0..1: how steep the river is here (white water at 1).</summary>
        public float Rapids;

        public const float NoWater = -1e6f;
        public bool HasWater => WaterSurface > NoWater * 0.5f;
    }

    /// <summary>
    /// Singleton that defines the world: its layout (land, sea), biomes (which region is what), shape
    /// (heights), rivers, and what surfaces cover the ground. Everything that needs ground height or ground
    /// material samples this, so terrain meshes, player movement and camera always agree.
    /// </summary>
    public struct TerrainSource : IComponentData, IEquatable<TerrainSource>
    {
        public WorldSettings World;

        public HeightSourceKind HeightKind;
        public BlobAssetReference<HeightmapBlob> Heightmap;

        public BiomeSourceKind BiomeKind;
        public BiomeNoiseSettings BiomeNoise;
        /// <summary>Voronoi region containing the origin (always the first biome). Derived from BiomeNoise.</summary>
        public int2 SpawnCell;
        public BlobAssetReference<BiomeMapBlob> BiomeMap;
        public BlobAssetReference<BiomeSetBlob> Biomes;
        /// <summary>Each noise biome region's biome, precomputed (optional: without it they're worked out per sample).</summary>
        public BlobAssetReference<BiomeCellTableBlob> BiomeCells;

        public RiverSettings RiverSettings;
        public BlobAssetReference<RiverMapBlob> Rivers;

        /// <summary>Natural landmarks (oases, geysers, waterfalls…): baked after rivers, applied on top of them.</summary>
        public BlobAssetReference<LandmarkSetBlob> Landmarks;
        /// <summary>Settlements' streets and plots (baked after landmarks): levelled and paved on top of them.</summary>
        public BlobAssetReference<SettlementSetBlob> Settlements;
        /// <summary>Caves (baked after landmarks): air pockets cut out of the rock under the surface.</summary>
        public BlobAssetReference<CaveSetBlob> Caves;
        /// <summary>
        /// Anchors (baked last): points places export for creatures, such as roosts, nests and burrows. Look them up
        /// with <see cref="Punchy.Terrain.Anchors"/>. A world with no caves or settlements has none: check IsCreated
        /// before taking <c>ref Anchors.Value</c>.
        /// </summary>
        public BlobAssetReference<AnchorSetBlob> Anchors;

        bool HasBiomes => Biomes.IsCreated && Biomes.Value.Biomes.Length > 0;
        bool UsesBiomeMap => BiomeKind == BiomeSourceKind.Map && BiomeMap.IsCreated;
        bool UsesHeightmap => HeightKind == HeightSourceKind.Heightmap && Heightmap.IsCreated;

        // ---------------------------------------------------------------- layout & biomes

        /// <summary>0 on land, 1 out at sea; smooth across the coast band.</summary>
        public float OceanFactor(float2 xz)
        {
            if (!HasBiomes || Biomes.Value.OceanIndex < 0)
                return 0f;
            var radius = World.WorldSize * 0.5f;
            var p = xz / radius;
            var wobble = noise.snoise(p * 1.7f + World.Seed % 10007u * 0.013f) * 0.65f + noise.snoise(p * 4.1f - 9.3f) * 0.35f;
            var landness = (1f - math.length(p) + World.CoastlineRoughness * wobble) * radius;   // metres inside the coast
            return 1f - math.smoothstep(-World.CoastWidth * 0.5f, World.CoastWidth * 0.5f, landness);
        }

        /// <summary>Surface (narrow) and shape (wide) biome weights at a point.</summary>
        public void SampleBiomes(float2 xz, out BiomeWeights surface, out BiomeWeights shape)
        {
            if (!HasBiomes)
            {
                surface = default;
                surface.Normalize();
                shape = surface;
                return;
            }

            if (UsesBiomeMap)
            {
                // The painted map already contains the ocean.
                surface = BiomeSampling.SampleMap(ref BiomeMap.Value, xz);
                shape = BiomeSampling.SampleMapBlurred(ref BiomeMap.Value, xz, BiomeNoise.ShapeBlendDistance * 0.5f);
                return;
            }

            ref var set = ref Biomes.Value;
            var cells = BiomeSampling.Cells(BiomeNoise, ref set, BiomeCells, SpawnCell, xz);
            var ocean = OceanFactor(xz);
            surface = BiomeSampling.WithOcean(BiomeSampling.Weights(cells, BiomeNoise.BlendDistance, BiomeNoise.CellSize), ocean, set.OceanIndex);
            shape = BiomeSampling.WithOcean(BiomeSampling.Weights(cells, BiomeNoise.ShapeBlendDistance, BiomeNoise.CellSize), ocean, set.OceanIndex);
        }

        /// <summary>
        /// The look of the water here, blended from the biomes: rgb colour (weighted by murk, so clear-water biomes
        /// don't dilute a swamp's colour), a = murk 0..1; glow (x) and lava (y) in the out parameter.
        /// </summary>
        public float4 WaterLook(float2 xz, in BiomeWeights biomes, out float2 glow)
        {
            var look = WaterLook(biomes, out glow);
            if (Landmarks.IsCreated)
                LandmarkSampling.WaterLook(ref Landmarks.Value, xz, ref look, ref glow);
            return look;
        }

        public float4 WaterLook(in BiomeWeights biomes, out float2 glow)
        {
            glow = 0f;
            if (!HasBiomes)
                return float4.zero;
            ref var set = ref Biomes.Value;
            float3 color = 0f;
            float murk = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = biomes.Weights[i];
                if (w <= 0f)
                    continue;
                ref var b = ref set.Biomes[biomes.Ids[i]];
                color += b.Water.xyz * b.Water.w * w;
                murk += b.Water.w * w;
                glow += new float2(b.WaterGlow, b.WaterLava) * w;
            }
            return new float4(murk > 1e-4f ? color / murk : float3.zero, murk);
        }

        /// <summary>How much rain falls here (0..1), for river generation.</summary>
        public float Rainfall(float2 xz)
        {
            if (!HasBiomes)
                return 0.5f;
            SampleBiomes(xz, out var surface, out _);
            ref var set = ref Biomes.Value;
            float rain = 0f;
            for (int i = 0; i < 4; i++)
                if (surface.Weights[i] > 0f)
                    rain += surface.Weights[i] * set.Biomes[surface.Ids[i]].Rainfall;
            return rain;
        }

        // ---------------------------------------------------------------- heights

        /// <summary>Biome base elevations plus each biome's relief, blended by shape weight. No rivers.</summary>
        public float ShapeHeight(float2 xz, in BiomeWeights shape)
        {
            if (!HasBiomes)
                return World.SeaLevel + 10f;

            ref var set = ref Biomes.Value;
            float height = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = shape.Weights[i];
                if (w <= 0f)
                    continue;
                ref var biome = ref set.Biomes[shape.Ids[i]];
                height += w * (biome.BaseElevation + BiomeSampling.Relief(ref biome, xz));
            }
            return World.SeaLevel + height;
        }

        /// <summary>Ground height before river carving (what rivers are routed over).</summary>
        public float BaseHeight(float2 xz)
        {
            if (UsesHeightmap)
                return Heightmap.Value.Sample(xz);
            SampleBiomes(xz, out _, out var shape);
            return ShapeHeight(xz, shape);
        }

        public float SampleHeight(float2 xz)
        {
            // A loaded heightmap already contains carved channels and landmark shapes.
            if (UsesHeightmap)
                return Settle(xz, Heightmap.Value.Sample(xz));
            SampleBiomes(xz, out _, out var shape);
            var h = ShapeHeight(xz, shape);
            if (Rivers.IsCreated)
                h = RiverSampling.Carve(RiverSampling.Query(ref Rivers.Value, RiverSettings, xz), RiverSettings, h);
            if (Landmarks.IsCreated)
                h = LandmarkSampling.Shape(ref Landmarks.Value, xz, h);
            return Settle(xz, h);
        }

        /// <summary>
        /// Settlements' streets and plot pads levelled into the ground. Applied last, and levelling towards a fixed
        /// height, so applying it again (to a heightmap exported with it) changes nothing.
        /// </summary>
        float Settle(float2 xz, float h) => Settlements.IsCreated ? SettlementSampling.Height(ref Settlements.Value, xz, h) : h;

        /// <summary>Height plus biome, river and water information, computed together.</summary>
        public GroundSample SampleGround(float2 xz)
        {
            SampleBiomes(xz, out var surface, out var shape);
            var g = new GroundSample
            {
                Biomes = surface,
                Height = UsesHeightmap ? Heightmap.Value.Sample(xz) : ShapeHeight(xz, shape)
            };

            // Each water body only claims the area around it, so inland ground far from water has no surface at
            // all (rather than an invisible sea far below), and water meshes don't grow stray sheets.
            var water = GroundSample.NoWater;
            var flow = float2.zero;
            if (g.Height < World.SeaLevel + 3f)
                water = World.SeaLevel;

            // Water depth as the surface rules see it. Lakes and rivers only claim a limited zone, so their depth
            // fades out towards the zone's edge (to "far above water") instead of switching off along a line.
            const float farAbove = -50f;
            var ruleDepth = World.SeaLevel - g.Height;

            if (Rivers.IsCreated)
            {
                var river = RiverSampling.Query(ref Rivers.Value, RiverSettings, xz);
                // A loaded heightmap already contains the carved channels; only noise heights get carved here.
                if (!UsesHeightmap)
                    g.Height = RiverSampling.Carve(river, RiverSettings, g.Height);
                g.River = RiverSampling.ChannelMask(river);
                var edge = river.Width * 0.5f + RiverSampling.SurfaceMargin;
                // The river's own surface here and how much it claims this point (for its outlet from a lake, below).
                var riverLevel = float.MinValue;
                var riverClaim = 0f;
                if (river.Found && river.Distance < edge)
                {
                    var surfaceLevel = RiverSampling.WaterSurface(river);
                    riverLevel = surfaceLevel;
                    if (surfaceLevel > water)
                    {
                        water = surfaceLevel;
                        flow = river.Flow;
                        g.Rapids = math.smoothstep(0.015f, 0.08f, river.Gradient);
                    }
                    var fade = 1f - math.smoothstep(edge - RiverSampling.SurfaceMargin * 0.5f, edge, river.Distance);
                    riverClaim = fade;
                    ruleDepth = math.max(ruleDepth, math.lerp(farAbove, surfaceLevel - g.Height, fade));
                }

                var lake = RiverSampling.Lake(ref Rivers.Value, xz, out var lakeSurface);
                if (lake > 0f)
                {
                    // Where a river leaves a lake lower than the lake's surface (its outlet), the river's surface rises
                    // to the lake's across the lake's edge, as rapids, instead of stepping up along the edge of the
                    // lake's cells.
                    var level = lakeSurface;
                    if (riverClaim > 0f && riverLevel < lakeSurface && lake < 1f)
                    {
                        var ramp = math.lerp(riverLevel, lakeSurface, math.smoothstep(0f, 1f, lake));
                        level = math.lerp(lakeSurface, ramp, riverClaim);
                    }
                    if (level > water)
                    {
                        var outlet = level < lakeSurface - 0.01f;
                        water = level;
                        flow = outlet ? flow : float2.zero;
                        g.Rapids = outlet ? math.max(g.Rapids, math.saturate((lakeSurface - riverLevel) * 0.6f) * riverClaim) : 0f;
                    }
                    ruleDepth = math.max(ruleDepth, math.lerp(farAbove, level - g.Height, math.smoothstep(0f, 0.4f, lake)));
                }
                // A bay or beach pocket lying below a lake's surface that the lake's coarse cells miss: lake too, if
                // the ground between it and the lake is under that surface (so the water connects). Not in a river's
                // own channel (an outlet keeps its ramp above).
                if (lake < 1f && riverClaim <= 0f)
                {
                    var shore = RiverSampling.ShoreLevel(ref Rivers.Value, xz);
                    if (shore > g.Height + 0.05f && shore > water
                        && RiverSampling.NearestLakeAbove(ref Rivers.Value, xz, g.Height + 0.05f, out var lakeCentre, out var bayLevel)
                        && bayLevel > water && ConnectsBelow(xz, lakeCentre, bayLevel))
                    {
                        water = bayLevel;
                        flow = float2.zero;
                        g.Rapids = 0f;
                        ruleDepth = math.max(ruleDepth, bayLevel - g.Height);
                    }
                }
                // A river flowing through a lake is just lake there.
                g.River *= 1f - math.saturate(lake * 2f);
            }

            if (Landmarks.IsCreated)
            {
                ref var landmarks = ref Landmarks.Value;
                if (!UsesHeightmap)
                    g.Height = LandmarkSampling.Shape(ref landmarks, xz, g.Height);
                var pool = LandmarkSampling.PoolWater(ref landmarks, xz, out var claim);
                if (claim > 0f)
                {
                    if (pool > water)
                    {
                        water = pool;
                        flow = float2.zero;
                        g.Rapids = 0f;
                    }
                    ruleDepth = math.max(ruleDepth, math.lerp(farAbove, pool - g.Height, claim));
                }
            }
            // Settlements keep clear of water, so their levelling doesn't change the water rules above.
            g.Height = Settle(xz, g.Height);

            g.WaterSurface = water;
            g.Flow = flow;
            g.WaterDepth = ruleDepth;
            return g;
        }

        // ---------------------------------------------------------------- caves

        /// <summary>Signed distance to cave air at a point: negative inside a cave, large away from every cave.</summary>
        public float CaveDistance(float3 p) => Caves.IsCreated ? CaveSampling.Distance(ref Caves.Value, p, out _) : 1e9f;

        /// <summary>
        /// How much lava (0..1) a point is in: a cave's lava pool, or surface water whose look is lava (volcano craters,
        /// lava biomes). Nothing stops anyone walking or swimming into it; this is for whatever reacts to it (a future
        /// health system, effects).
        /// </summary>
        public float LavaAt(float3 p)
        {
            if (Caves.IsCreated && CaveSampling.OverLava(ref Caves.Value, p))
                return 1f;
            var ground = SampleGround(p.xz);
            if (!ground.HasWater || p.y > ground.WaterSurface + 0.1f)
                return 0f;
            WaterLook(p.xz, ground.Biomes, out var glow);
            return glow.y;
        }

        /// <summary>Whether the ground surface here is cut away by a cave entrance.</summary>
        public bool IsCaveHole(float2 xz) => MouthCut(xz, SampleHeight(xz)) < 0f;

        /// <summary>
        /// Whether the cave walls keep rock just under the ground surface here (positive, at most 0.25) or a mouth opens
        /// (negative): the same field CaveSystem builds the walls from (surface air and cave air merged smoothly over
        /// CaveSampling.MouthBlend, so a roof thinner than about half a metre opens), sampled 35 cm into the ground. The terrain is
        /// cut where this is negative, so its edge and the walls' rim agree; the walls carry a skin of the surface for a
        /// couple of metres round (CaveSystem.WallReach), under the terrain, so the terrain's per-vertex cut never shows a
        /// gap. 100 away from caves.
        /// </summary>
        public float MouthCut(float2 xz, float h)
        {
            if (!Caves.IsCreated || !CaveSampling.NearColumn(ref Caves.Value, xz))
                return 100f;
            // Into the ground along its normal, as the walls' field measures depth across the slope: mouths open in cliff
            // faces, where straight down is somewhere else entirely.
            const float depth = 0.35f;
            var normal = SampleNormal(xz, 0.5f);
            var cave = CaveDistance(new float3(xz.x, h, xz.y) - normal * depth);
            return math.min(CaveSampling.SmoothMin(depth - 0.1f, cave, CaveSampling.MouthBlend), 100f);
        }

        /// <summary>
        /// How far the ground surface at a spot (height h) is from cave air: negative where the terrain is cut away
        /// for a cave entrance, either because the surface is in the cave or because cave air lies just under it (a
        /// lip too thin to keep). 100 away from caves. (The terrain is cut by MouthCut, which follows the cave walls exactly.)
        /// </summary>
        public float CaveClearance(float2 xz, float h)
        {
            if (!Caves.IsCreated || !CaveSampling.NearColumn(ref Caves.Value, xz))
                return 100f;
            return math.min(math.min(CaveDistance(new float3(xz.x, h + 0.05f, xz.y)), CaveDistance(new float3(xz.x, h - 0.6f, xz.y))), 100f);
        }

        /// <summary>
        /// Rock at a point: below the surface and outside every cave. Things that move underground (the camera, a
        /// walking player) test this to keep out of the walls.
        /// </summary>
        public bool IsRock(float3 p, float margin = 0f) =>
            p.y < SampleHeight(p.xz) - margin && CaveDistance(p) > -margin;

        /// <summary>A bridge deck under a point that's on or just below it (see SettlementSampling.Deck).</summary>
        public bool Deck(float3 p, out float deck)
        {
            deck = 0f;
            return Settlements.IsCreated && SettlementSampling.Deck(ref Settlements.Value, p, out deck);
        }

        /// <summary>Whether the ground on the way from a to b (sampled at a quarter, half and three quarters) is all below a level.</summary>
        bool ConnectsBelow(float2 a, float2 b, float level)
        {
            for (int k = 1; k <= 3; k++)
                if (SampleHeight(math.lerp(a, b, k * 0.25f)) >= level)
                    return false;
            return true;
        }

        /// <summary>
        /// What's underfoot at a point. On a bridge it's the deck. In a cave, or where the surface is cut away by an entrance, it's the cave
        /// floor below the point (inCave true); otherwise it's the ground surface.
        /// </summary>
        public float FloorUnder(float3 p, out bool inCave)
        {
            inCave = false;
            if (Deck(p, out var deck))
                return math.max(deck, SampleHeight(p.xz));
            var surface = SampleHeight(p.xz);
            if (!Caves.IsCreated || !CaveSampling.NearColumn(ref Caves.Value, p.xz))
                return surface;
            // Look down from a little above the feet, but never from above the surface.
            var y = math.min(p.y + 0.6f, surface + 0.05f);
            var d = CaveDistance(new float3(p.x, y, p.z));
            if (d >= 0f)
            {
                // Underground but not in a cave: stay put (movement keeps us out of the walls).
                if (p.y < surface - 1.5f)
                {
                    inCave = true;
                    return p.y;
                }
                return surface;
            }
            inCave = true;
            // March down through the air by part of the distance to the nearest rock (at least 25 cm), so a deep shaft
            // takes a few dozen steps rather than running out of them partway down. Below a passage's axis the field
            // can overstate the distance down to the floor by up to 1.8 times, so the step is 0.4 of it: never past a
            // thin floor into a passage beneath.
            var start = y;
            var found = false;
            float lo = y - 0.25f, hi = y;
            for (int i = 0; i < 200; i++)
            {
                var next = y - math.max(0.25f, -d * 0.4f);
                var dn = CaveDistance(new float3(p.x, next, p.z));
                lo = next;
                hi = y;
                if (dn >= 0f)
                {
                    found = true;
                    break;
                }
                y = next;
                d = dn;
            }
            // No floor within reach: stay where we started rather than standing in mid-air.
            if (!found)
                return start;
            // Refine to the floor between lo (rock) and hi (air); the bracket is a step or so, so 7 halvings give
            // under a centimetre.
            for (int i = 0; i < 7; i++)
            {
                var mid = (lo + hi) * 0.5f;
                if (CaveDistance(new float3(p.x, mid, p.z)) < 0f) hi = mid; else lo = mid;
            }
            return hi;
        }

        /// <summary>
        /// The cave ceiling straight above a point in cave air (sphere-traced up to rock); the point's own height if
        /// it isn't in cave air.
        /// </summary>
        public float CeilingOver(float3 p)
        {
            // Up by part of the distance to rock (as FloorUnder marches down): angular or ribbed walls make the
            // field rise faster than the true distance, and a longer step would end up inside the ceiling. Then
            // halve the last step to find the ceiling to a few centimetres.
            var y = p.y;
            float air = y, rock = y;
            var found = false;
            for (int i = 0; i < 200; i++)
            {
                var d = CaveDistance(new float3(p.x, y, p.z));
                if (d >= 0f)
                {
                    rock = y;
                    found = true;
                    break;
                }
                air = y;
                y += math.max(0.1f, -d * 0.4f);
            }
            if (!found)
                return y;
            for (int i = 0; i < 6; i++)
            {
                var mid = (air + rock) * 0.5f;
                if (CaveDistance(new float3(p.x, mid, p.z)) < 0f) air = mid; else rock = mid;
            }
            return air;
        }

        public float3 SampleNormal(float2 xz, float eps)
        {
            var dx = SampleHeight(xz + new float2(eps, 0f)) - SampleHeight(xz - new float2(eps, 0f));
            var dz = SampleHeight(xz + new float2(0f, eps)) - SampleHeight(xz - new float2(0f, eps));
            return math.normalize(new float3(-dx, 2f * eps, -dz));
        }

        // ---------------------------------------------------------------- surfaces

        /// <summary>Blended surface properties for a sampled point with the given normal.</summary>
        public SurfaceProps SampleSurface(float2 xz, in GroundSample ground, float3 normal)
        {
            var result = default(SurfaceProps);
            if (!HasBiomes)
            {
                result.Albedo = new float3(0.3f);
                result.Smoothness = 0.2f;
                return result;
            }

            var context = new GroundContext
            {
                Position = xz,
                Height = ground.Height - World.SeaLevel,
                Slope = math.degrees(math.acos(math.saturate(normal.y))),
                WaterDepth = ground.WaterDepth,
                River = ground.River,
                EdgeNoise = noise.snoise(xz * 0.045f) * 0.7f + noise.snoise(xz * 0.17f) * 0.3f,
                ColorNoise = noise.snoise(xz * 0.021f + 113.5f) * 0.6f + noise.snoise(xz * 0.11f - 57.1f) * 0.4f
            };

            ref var set = ref Biomes.Value;
            for (int i = 0; i < 4; i++)
            {
                var w = ground.Biomes.Weights[i];
                if (w > 0f)
                    result.AddScaled(BiomeSampling.EvaluateBiome(ref set, ground.Biomes.Ids[i], context), w);
            }
            if (Landmarks.IsCreated)
                LandmarkSampling.Ground(ref Landmarks.Value, xz, context.EdgeNoise, context.ColorNoise, ref result);
            if (Settlements.IsCreated)
                SettlementSampling.Ground(ref Settlements.Value, xz, context.EdgeNoise, context.ColorNoise, ref result);
            return result;
        }

        public bool Equals(TerrainSource o) =>
            World.Equals(o.World) && HeightKind == o.HeightKind && Heightmap == o.Heightmap &&
            BiomeKind == o.BiomeKind && BiomeNoise.Equals(o.BiomeNoise) && BiomeMap == o.BiomeMap && Biomes == o.Biomes && BiomeCells == o.BiomeCells &&
            RiverSettings.Equals(o.RiverSettings) && Rivers == o.Rivers && Landmarks == o.Landmarks && Settlements == o.Settlements && Caves == o.Caves && Anchors == o.Anchors;
    }
}
