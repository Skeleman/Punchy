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
    /// Draws the ambient particle types biomes list (sparkles, spores, embers, ash, fog wisps). Each 24 m tile around
    /// the camera gets fixed home spots per type (a Burst job reads the biome densities); the shaders wander, drift
    /// and fade each particle procedurally. A type's condition (night, day, fog, dust) comes from AmbienceSystem:
    /// each particle has a random threshold, so they thin out and return gradually as conditions change.
    /// </summary>
    public class AmbientParticleSystem : MonoBehaviour
    {
        public AmbienceSystem Ambience;
        public Material GlowMaterial;
        public Material MistMaterial;
        public float TileSize = 24f;
        [Tooltip("Upper limit on particles of one type per 100 m² (densities above this are clipped).")]
        public float MaxDensity = 60f;

        [StructLayout(LayoutKind.Sequential)]
        public struct Particle
        {
            public float3 Position;   // home, on the ground (or water)
            public float Random;
        }

        class Tile
        {
            public GraphicsBuffer[] Buffers;
            public int[] Counts;
            public MaterialPropertyBlock[] Props;
            public Bounds Bounds;

            public void Dispose()
            {
                foreach (var b in Buffers)
                    b?.Dispose();
            }
        }

        readonly List<AmbientParticleType> types = new List<AmbientParticleType>();
        readonly Dictionary<int2, Tile> tiles = new Dictionary<int2, Tile>();
        readonly List<int2> scratch = new List<int2>();
        NativeArray<float> densities;   // biome × type, per 100 m²
        NativeArray<float> maxDensity;  // per type, over all biomes
        int biomeCount;
        float radius;
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery;
        TerrainSource lastSource;
        bool built;

        static readonly int BufferId = Shader.PropertyToID("_AmbientParticles");
        static readonly int ColorId = Shader.PropertyToID("_ParticleColor");
        static readonly int SizeId = Shader.PropertyToID("_ParticleSize");
        static readonly int MotionId = Shader.PropertyToID("_ParticleMotion");
        static readonly int ShapeId = Shader.PropertyToID("_ParticleShape");

        void OnDisable() => Teardown();

        void Teardown()
        {
            foreach (var t in tiles.Values)
                t.Dispose();
            tiles.Clear();
            types.Clear();
            if (densities.IsCreated) densities.Dispose();
            if (maxDensity.IsCreated) maxDensity.Dispose();
            built = false;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null || GlowMaterial == null || MistMaterial == null || !TryGetWorld(out var source, out var palette))
                return;
            if (built && !source.Equals(lastSource))
                Teardown();
            if (!built)
                Build(palette, source);
            if (types.Count == 0)
                return;

            var camXZ = new float2(cam.transform.position.x, cam.transform.position.z);
            Stream(source, camXZ);
            Draw(camXZ);
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
                built = false;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        void Build(BiomePalette palette, in TerrainSource source)
        {
            foreach (var biome in palette.Biomes)
                if (biome != null)
                    foreach (var p in biome.Particles)
                        if (p.Type != null && !types.Contains(p.Type))
                            types.Add(p.Type);
            biomeCount = palette.Biomes.Length;
            densities = new NativeArray<float>(math.max(1, biomeCount * types.Count), Allocator.Persistent);
            maxDensity = new NativeArray<float>(math.max(1, types.Count), Allocator.Persistent);
            radius = 0f;
            for (int b = 0; b < biomeCount; b++)
            {
                if (palette.Biomes[b] == null)
                    continue;
                foreach (var p in palette.Biomes[b].Particles)
                {
                    var t = p.Type != null ? types.IndexOf(p.Type) : -1;
                    if (t < 0)
                        continue;
                    var d = math.min(p.Density, MaxDensity);
                    densities[b * types.Count + t] += d;
                    maxDensity[t] = math.max(maxDensity[t], densities[b * types.Count + t]);
                }
            }
            foreach (var t in types)
                radius = math.max(radius, Punchy.Settings.GraphicsSettings.DetailRange(t.Range));
            lastSource = source;
            built = true;
        }

        float2 TileCentre(int2 c) => ((float2)c + 0.5f) * TileSize;

        void Stream(in TerrainSource source, float2 camXZ)
        {
            scratch.Clear();
            foreach (var kv in tiles)
                if (math.distance(TileCentre(kv.Key), camXZ) > radius + TileSize * 1.5f)
                    scratch.Add(kv.Key);
            foreach (var key in scratch)
            {
                tiles[key].Dispose();
                tiles.Remove(key);
            }

            scratch.Clear();
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

            // Slots: for each tile, for each type, enough candidates for its densest biome.
            var slotsPerType = new int[types.Count];
            var slotStart = new int[types.Count];
            var perTile = 0;
            for (int t = 0; t < types.Count; t++)
            {
                slotStart[t] = perTile;
                slotsPerType[t] = (int)math.ceil(maxDensity[t] * TileSize * TileSize / 100f);
                perTile += slotsPerType[t];
            }
            if (perTile == 0)
                return;

            var coords = new NativeArray<int2>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
                coords[i] = scratch[i];
            var slotType = new NativeArray<int>(perTile, Allocator.TempJob);
            for (int t = 0; t < types.Count; t++)
                for (int k = 0; k < slotsPerType[t]; k++)
                    slotType[slotStart[t] + k] = t;
            var output = new NativeArray<Particle>(count * perTile, Allocator.TempJob);
            var keep = new NativeArray<byte>(count * perTile, Allocator.TempJob);
            new PlaceJob
            {
                Source = source,
                Densities = densities,
                MaxDensity = maxDensity,
                SlotType = slotType,
                TypeCount = types.Count,
                BiomeCount = biomeCount,
                Tiles = coords,
                TileSize = TileSize,
                PerTile = perTile,
                Output = output,
                Keep = keep
            }.Schedule(output.Length, 32).Complete();

            var list = new List<Particle>();
            for (int i = 0; i < count; i++)
            {
                var tile = new Tile
                {
                    Buffers = new GraphicsBuffer[types.Count],
                    Counts = new int[types.Count],
                    Props = new MaterialPropertyBlock[types.Count]
                };
                var min = (float2)coords[i] * TileSize;
                var groundY = source.SampleHeight(min + TileSize * 0.5f);
                tile.Bounds = new Bounds(new Vector3(min.x + TileSize * 0.5f, groundY, min.y + TileSize * 0.5f), new Vector3(TileSize + 20f, 120f, TileSize + 20f));
                for (int t = 0; t < types.Count; t++)
                {
                    list.Clear();
                    for (int k = 0; k < slotsPerType[t]; k++)
                    {
                        var s = i * perTile + slotStart[t] + k;
                        if (keep[s] != 0)
                            list.Add(output[s]);
                    }
                    tile.Counts[t] = list.Count;
                    if (list.Count == 0)
                        continue;
                    tile.Buffers[t] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, list.Count, Marshal.SizeOf<Particle>());
                    tile.Buffers[t].SetData(list);
                    tile.Props[t] = new MaterialPropertyBlock();
                    tile.Props[t].SetBuffer(BufferId, tile.Buffers[t]);
                }
                tiles[coords[i]] = tile;
            }
            coords.Dispose();
            slotType.Dispose();
            output.Dispose();
            keep.Dispose();
        }

        void Draw(float2 camXZ)
        {
            for (int t = 0; t < types.Count; t++)
            {
                var type = types[t];
                var amount = Amount(type.Condition);
                if (amount <= 0f)
                    continue;
                var material = type.Style == AmbientParticleStyle.Glow ? GlowMaterial : MistMaterial;
                foreach (var tile in tiles.Values)
                {
                    if (tile.Counts[t] == 0 || math.distance(new float2(tile.Bounds.center.x, tile.Bounds.center.z), camXZ) > Punchy.Settings.GraphicsSettings.DetailRange(type.Range) + TileSize)
                        continue;
                    var props = tile.Props[t];
                    props.SetVector(ColorId, type.Color);
                    props.SetVector(SizeId, new Vector4(type.Size.x, type.Size.y, type.Height.x, type.Height.y));
                    props.SetVector(MotionId, new Vector4(type.Wander, type.Rise, type.WindDrift, type.Travel));
                    props.SetVector(ShapeId, new Vector4(type.Twinkle, amount, Punchy.Settings.GraphicsSettings.DetailRange(type.Range), 0f));
                    var rp = new RenderParams(material)
                    {
                        worldBounds = tile.Bounds,
                        matProps = props,
                        shadowCastingMode = ShadowCastingMode.Off,
                        receiveShadows = false
                    };
                    Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, tile.Counts[t]);
                }
            }
        }

        float Amount(AmbientParticleCondition condition)
        {
            if (Ambience == null)
                return condition == AmbientParticleCondition.Always ? 1f : 0f;
            switch (condition)
            {
                case AmbientParticleCondition.Night: return Ambience.Night;
                case AmbientParticleCondition.Day: return 1f - Ambience.Night;
                case AmbientParticleCondition.Fog: return Ambience.Fog;
                case AmbientParticleCondition.Dust: return Ambience.Dust;
                default: return 1f;
            }
        }

        [BurstCompile]
        struct PlaceJob : IJobParallelFor
        {
            public TerrainSource Source;
            [ReadOnly] public NativeArray<float> Densities;
            [ReadOnly] public NativeArray<float> MaxDensity;
            [ReadOnly] public NativeArray<int> SlotType;
            public int TypeCount, BiomeCount;
            [ReadOnly] public NativeArray<int2> Tiles;
            public float TileSize;
            public int PerTile;
            [WriteOnly] public NativeArray<Particle> Output;
            [WriteOnly] public NativeArray<byte> Keep;

            public void Execute(int i)
            {
                Keep[i] = 0;
                var tile = Tiles[i / PerTile];
                var slot = i % PerTile;
                var type = SlotType[slot];
                var rng = new Unity.Mathematics.Random(math.hash(new int3(tile, slot * 31 + 7)) | 1u);
                var xz = ((float2)tile + rng.NextFloat2()) * TileSize;
                var roll = rng.NextFloat() * MaxDensity[type];

                Source.SampleBiomes(xz, out var biomes, out _);
                var density = 0f;
                for (int b = 0; b < 4; b++)
                    if (biomes.Weights[b] > 0f && biomes.Ids[b] < BiomeCount)
                        density += Densities[biomes.Ids[b] * TypeCount + type] * biomes.Weights[b];
                if (roll >= density)
                    return;
                var ground = Source.SampleGround(xz);
                var y = ground.HasWater ? math.max(ground.Height, ground.WaterSurface) : ground.Height;
                Output[i] = new Particle { Position = new float3(xz.x, y, xz.y), Random = rng.NextFloat() };
                Keep[i] = 1;
            }
        }
    }
}
