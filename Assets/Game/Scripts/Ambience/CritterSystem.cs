using System.Collections.Generic;
using System.Runtime.InteropServices;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Punchy.Ambience
{
    /// <summary>
    /// Fireflies and butterflies around the camera. Each 24 m tile gets a fixed set of home spots (Burst job sampling
    /// the biomes' Fireflies / Butterflies densities and the ground); the shaders fly each critter around its home
    /// procedurally, so nothing is simulated on the CPU. How many are out at once (night, weather, wind, cold) comes
    /// from AmbienceSystem through _PunchyCritters: each critter has a random threshold and only shows when the
    /// amount is above it, so they appear and vanish gradually.
    /// </summary>
    public class CritterSystem : MonoBehaviour
    {
        public Material FireflyMaterial;
        public Material ButterflyMaterial;
        public float TileSize = 24f;
        public float Radius = 70f;
        [Tooltip("Upper limit on critters of each kind per 100 m² (densities above this are clipped).")]
        public float MaxDensity = 8f;
        [Tooltip("Scales every biome's Butterflies density: a few round you in a meadow, not a cloud.")]
        public float ButterflyScale = 0.1f;

        /// <summary>
        /// Butterfly hotspots: landmark types (by asset name) multiply the butterflies inside them, strongest at the
        /// centre, fading to none extra at the edge. Villages are slightly below the meadow around them.
        /// </summary>
        static readonly Dictionary<string, float> LandmarkButterflies = new Dictionary<string, float>
        {
            ["FairyRing"] = 14f, ["Oasis"] = 10f, ["AncientTree"] = 10f, ["DesertSpring"] = 8f, ["ElfGrove"] = 6f,
            ["OakGrove"] = 4f, ["StoneCircle"] = 3f, ["Waterfall"] = 3f, ["HotSprings"] = 2f,
            ["HumanVillage"] = 0.6f, ["DwarfHold"] = 0.5f,
        };

        /// <summary>
        /// Firefly hotspots, as for butterflies: landmark types (by asset name) multiply the fireflies inside them; and
        /// whether theirs come in every colour (the great tree's: gold, green, cyan, blue, violet, rose) rather than the
        /// usual yellow-green.
        /// </summary>
        static readonly Dictionary<string, (float more, bool colours)> LandmarkFireflies = new Dictionary<string, (float, bool)>
        {
            ["AncientTree"] = (12f, true), ["FairyRing"] = (4f, false), ["Oasis"] = (3f, false),
        };

        /// <summary>Biomes (by asset name) with more butterflies than their density alone gives: the mystic glades.</summary>
        static readonly Dictionary<string, float> BiomeButterflies = new Dictionary<string, float>
        {
            ["SparklingForest"] = 4f,
        };

        const int FireflyVertices = 6;
        const int ButterflyVertices = 12;

        [StructLayout(LayoutKind.Sequential)]
        public struct Critter
        {
            public float3 Position;   // home: on the ground, or a layer or two up round a great tree
            public float Random;
            public float4 Color;
        }

        class Tile
        {
            public GraphicsBuffer Fireflies, Butterflies;
            public int FireflyCount, ButterflyCount;
            public Bounds Bounds;
            public MaterialPropertyBlock FireflyProps, ButterflyProps;

            public void Dispose()
            {
                Fireflies?.Dispose();
                Butterflies?.Dispose();
            }
        }

        readonly Dictionary<int2, Tile> tiles = new Dictionary<int2, Tile>();
        readonly List<int2> scratch = new List<int2>();
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery;
        TerrainSource lastSource;
        bool hasSource;
        NativeArray<float2> densities;   // fireflies, butterflies per biome id
        NativeArray<float> landmarkButterflies;   // butterfly multiplier per landmark type
        NativeArray<float2> landmarkFireflies;    // firefly multiplier, and every colour or not, per landmark type

        static readonly int CrittersBufferId = Shader.PropertyToID("_CritterBuffer");
        static readonly int CrittersId = Shader.PropertyToID("_PunchyCritters");

        int MaxPerTile => (int)(TileSize * TileSize * MaxDensity / 100f);

        void OnDisable()
        {
            foreach (var t in tiles.Values)
                t.Dispose();
            tiles.Clear();
            if (densities.IsCreated) densities.Dispose();
            if (landmarkButterflies.IsCreated) landmarkButterflies.Dispose();
            if (landmarkFireflies.IsCreated) landmarkFireflies.Dispose();
            hasSource = false;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || FireflyMaterial == null || ButterflyMaterial == null || !TryGetWorld(out var source, out var palette))
                return;
            if (!hasSource || !source.Equals(lastSource) || densities.Length != palette.Biomes.Length)
            {
                OnDisable();
                densities = new NativeArray<float2>(palette.Biomes.Length, Allocator.Persistent);
                for (int i = 0; i < palette.Biomes.Length; i++)
                    if (palette.Biomes[i] != null)
                        densities[i] = new float2(palette.Biomes[i].Fireflies, palette.Biomes[i].Butterflies * ButterflyScale *
                            (BiomeButterflies.TryGetValue(palette.Biomes[i].name, out var bm) ? bm : 1f));
                var types = palette.Landmarks;
                landmarkButterflies = new NativeArray<float>(types?.Length ?? 0, Allocator.Persistent);
                for (int i = 0; i < landmarkButterflies.Length; i++)
                    landmarkButterflies[i] = types[i] != null && LandmarkButterflies.TryGetValue(types[i].name, out var lm) ? lm : 1f;
                landmarkFireflies = new NativeArray<float2>(types?.Length ?? 0, Allocator.Persistent);
                for (int i = 0; i < landmarkFireflies.Length; i++)
                    landmarkFireflies[i] = types[i] != null && LandmarkFireflies.TryGetValue(types[i].name, out var lf)
                        ? new float2(lf.more, lf.colours ? 1f : 0f) : new float2(1f, 0f);
                lastSource = source;
                hasSource = true;
            }

            var amounts = Shader.GetGlobalVector(CrittersId);
            // (Deep in a cave the surface is out of sight: no tiles streamed or drawn; CaveSystem.SurfaceHidden.)
            if (amounts.x <= 0f && amounts.y <= 0f || CaveSystem.SurfaceHidden >= 0.99f)
                return;
            var camXZ = new float2(cam.transform.position.x, cam.transform.position.z);
            Stream(source, camXZ);
            Draw(cam, camXZ, amounts);
        }

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                terrainQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                queryWorld = world;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        float2 TileCentre(int2 c) => ((float2)c + 0.5f) * TileSize;

        void Stream(in TerrainSource source, float2 camXZ)
        {
            scratch.Clear();
            foreach (var kv in tiles)
                if (math.distance(TileCentre(kv.Key), camXZ) > Punchy.Settings.GraphicsSettings.DetailRange(Radius) + TileSize * 1.5f)
                    scratch.Add(kv.Key);
            foreach (var key in scratch)
            {
                tiles[key].Dispose();
                tiles.Remove(key);
            }

            scratch.Clear();
            var radius = Punchy.Settings.GraphicsSettings.DetailRange(Radius);
            var r = (int)math.ceil(radius / TileSize) + 1;
            var centre = (int2)math.floor(camXZ / TileSize);
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                var c = centre + new int2(x, z);
                var min = (float2)c * TileSize;
                if (!tiles.ContainsKey(c) && math.distance(math.clamp(camXZ, min, min + TileSize), camXZ) < radius)
                    scratch.Add(c);
            }
            if (scratch.Count == 0)
                return;
            scratch.Sort((a, b) => math.distancesq(TileCentre(a), camXZ).CompareTo(math.distancesq(TileCentre(b), camXZ)));
            var count = math.min(scratch.Count, 4);

            var perTile = MaxPerTile;
            var coords = new NativeArray<int2>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
                coords[i] = scratch[i];
            var output = new NativeArray<Critter>(count * perTile * 2, Allocator.TempJob);
            var kinds = new NativeArray<byte>(count * perTile * 2, Allocator.TempJob);
            new PlaceJob
            {
                Source = source,
                Densities = densities,
                LandmarkButterflies = landmarkButterflies,
                LandmarkFireflies = landmarkFireflies,
                Tiles = coords,
                TileSize = TileSize,
                PerTile = perTile,
                MaxDensity = MaxDensity,
                Output = output,
                Kinds = kinds
            }.Schedule(output.Length, 32).Complete();

            var fireflies = new List<Critter>();
            var butterflies = new List<Critter>();
            for (int t = 0; t < count; t++)
            {
                fireflies.Clear();
                butterflies.Clear();
                for (int i = t * perTile * 2; i < (t + 1) * perTile * 2; i++)
                {
                    if (kinds[i] == 1) fireflies.Add(output[i]);
                    else if (kinds[i] == 2) butterflies.Add(output[i]);
                }
                var min = (float2)coords[t] * TileSize;
                var groundY = source.SampleHeight(min + TileSize * 0.5f);
                var tile = new Tile
                {
                    FireflyCount = fireflies.Count,
                    ButterflyCount = butterflies.Count,
                    FireflyProps = new MaterialPropertyBlock(),
                    ButterflyProps = new MaterialPropertyBlock(),
                    Bounds = new Bounds(new Vector3(min.x + TileSize * 0.5f, groundY, min.y + TileSize * 0.5f),
                                        new Vector3(TileSize + 12f, 80f, TileSize + 12f))
                };
                if (tile.FireflyCount > 0)
                {
                    tile.Fireflies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tile.FireflyCount, Marshal.SizeOf<Critter>());
                    tile.Fireflies.SetData(fireflies);
                    tile.FireflyProps.SetBuffer(CrittersBufferId, tile.Fireflies);
                }
                if (tile.ButterflyCount > 0)
                {
                    tile.Butterflies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tile.ButterflyCount, Marshal.SizeOf<Critter>());
                    tile.Butterflies.SetData(butterflies);
                    tile.ButterflyProps.SetBuffer(CrittersBufferId, tile.Butterflies);
                }
                tiles[coords[t]] = tile;
            }
            coords.Dispose();
            output.Dispose();
            kinds.Dispose();
        }

        void Draw(Camera cam, float2 camXZ, Vector4 amounts)
        {
            foreach (var kv in tiles)
            {
                var tile = kv.Value;
                if (amounts.x > 0f && tile.FireflyCount > 0)
                    Graphics.RenderPrimitives(Params(FireflyMaterial, tile.FireflyProps, tile.Bounds), MeshTopology.Triangles, FireflyVertices, tile.FireflyCount);
                if (amounts.y > 0f && tile.ButterflyCount > 0)
                    Graphics.RenderPrimitives(Params(ButterflyMaterial, tile.ButterflyProps, tile.Bounds), MeshTopology.Triangles, ButterflyVertices, tile.ButterflyCount);
            }
        }

        static RenderParams Params(Material material, MaterialPropertyBlock props, Bounds bounds) => new RenderParams(material)
        {
            worldBounds = bounds,
            matProps = props,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };

        /// <summary>
        /// Each slot is a candidate firefly (first half of a tile's slots) or butterfly (second half), kept with
        /// probability density / MaxDensity. Kinds: 0 none, 1 firefly, 2 butterfly.
        /// </summary>
        [BurstCompile]
        struct PlaceJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float2> Densities;
            [ReadOnly] public NativeArray<float> LandmarkButterflies;
            /// <summary>Per landmark type: x the fireflies' multiplier inside it, y 1 if they come in every colour.</summary>
            [ReadOnly] public NativeArray<float2> LandmarkFireflies;
            [ReadOnly] public NativeArray<int2> Tiles;
            public float TileSize;
            public int PerTile;
            public float MaxDensity;
            [WriteOnly] public NativeArray<Critter> Output;
            [WriteOnly] public NativeArray<byte> Kinds;

            public void Execute(int i)
            {
                Kinds[i] = 0;
                var tile = Tiles[i / (PerTile * 2)];
                var slot = i % (PerTile * 2);
                var butterfly = slot >= PerTile;
                var rng = new Unity.Mathematics.Random(math.hash(new int3(tile, slot)) | 1u);
                var xz = ((float2)tile + rng.NextFloat2()) * TileSize;
                var keep = rng.NextFloat();

                Source.SampleBiomes(xz, out var biomes, out _);
                var density = 0f;
                for (int b = 0; b < 4; b++)
                    if (biomes.Weights[b] > 0f && biomes.Ids[b] < Densities.Length)
                        density += (butterfly ? Densities[biomes.Ids[b]].y : Densities[biomes.Ids[b]].x) * biomes.Weights[b];
                if (butterfly && Source.Landmarks.IsCreated && LandmarkSampling.Find(ref Source.Landmarks.Value, xz, out var hit) &&
                    hit.Type < LandmarkButterflies.Length)
                {
                    // A hotspot needs a floor too, or a fairy ring in a bare biome would stay bare.
                    var m = LandmarkButterflies[hit.Type];
                    var inside = math.smoothstep(1f, 0.3f, hit.Distance);
                    density = m > 1f ? math.lerp(density, math.max(density, 0.15f) * m, inside) : density * math.lerp(1f, m, inside);
                }
                var colours = false;
                if (!butterfly && Source.Landmarks.IsCreated && LandmarkSampling.Find(ref Source.Landmarks.Value, xz, out var near) &&
                    near.Type < LandmarkFireflies.Length && LandmarkFireflies[near.Type].x != 1f)
                {
                    var m = LandmarkFireflies[near.Type];
                    var inside = math.smoothstep(1f, 0.3f, near.Distance);
                    density = math.lerp(density, math.max(density, 0.15f) * m.x, inside);
                    colours = m.y > 0.5f && rng.NextFloat() < inside;
                }
                if (keep >= density / MaxDensity)
                    return;
                var ground = Source.SampleGround(xz);
                if (ground.HasWater && ground.WaterSurface > ground.Height - 0.2f && butterfly)
                    return;   // fireflies may hover over water; butterflies stick to land

                float3 color;
                if (butterfly)
                {
                    var pick = rng.NextInt(5);
                    color = pick == 0 ? new float3(1f, 0.42f, 0.04f)       // monarch orange
                          : pick == 1 ? new float3(0.95f, 0.95f, 0.9f)     // cabbage white
                          : pick == 2 ? new float3(1f, 0.85f, 0.18f)       // brimstone yellow
                          : pick == 3 ? new float3(0.25f, 0.45f, 0.95f)    // blue
                          : new float3(0.75f, 0.3f, 0.45f);                // pinkish
                }
                else if (colours)
                {
                    var pick = rng.NextInt(6);
                    color = pick == 0 ? new float3(1f, 0.8f, 0.25f)        // gold
                          : pick == 1 ? new float3(0.55f, 1f, 0.35f)       // green
                          : pick == 2 ? new float3(0.3f, 0.95f, 1f)        // cyan
                          : pick == 3 ? new float3(0.35f, 0.5f, 1f)        // blue
                          : pick == 4 ? new float3(0.75f, 0.4f, 1f)        // violet
                          : new float3(1f, 0.45f, 0.7f);                   // rose
                }
                else
                    color = math.lerp(new float3(0.75f, 1f, 0.25f), new float3(1f, 0.85f, 0.3f), rng.NextFloat());

                // The great tree's fireflies fill its height as well as its feet: two in three hover higher, a layer
                // or two up round the crown (Z-37); the shader still drifts each one round its own spot.
                var lift = colours ? rng.NextInt(3) * 4f : 0f;
                Output[i] = new Critter
                {
                    Position = new float3(xz.x, math.max(ground.Height, ground.HasWater ? ground.WaterSurface : ground.Height) + lift, xz.y),
                    Random = rng.NextFloat(),
                    Color = new float4(color, 1f)
                };
                Kinds[i] = (byte)(butterfly ? 2 : 1);
            }
        }
    }
}
