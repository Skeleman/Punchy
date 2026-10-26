using System.Collections.Generic;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// Keeps the land around the player populated with the animals each biome lists in its Fauna: spawns groups out
    /// of sight-range (between Min Spawn Distance and Radius) wherever a species is below its biome's density, and
    /// removes animals that end up far behind. Builds each species' runtime data and the shared capsule mesh.
    /// </summary>
    public class FaunaDirector : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Animals are kept within this distance of the player.")]
        public float Radius = 120f;
        [Tooltip("New animals appear at least this far away.")]
        public float MinSpawnDistance = 40f;
        [Tooltip("Animals further than this are removed.")]
        public float DespawnDistance = 170f;
        public int MaxCreatures = 300;
        [Tooltip("Spawn attempts per second.")]
        public float SpawnRate = 6f;

        readonly List<CreatureSpecies> species = new List<CreatureSpecies>();
        readonly List<BlobAssetReference<CreatureBlob>> blobs = new List<BlobAssetReference<CreatureBlob>>();
        BiomeDefinition[] biomes;
        World queryWorld;
        EntityQuery terrainQuery, paletteQuery, playerQuery, creatureQuery;
        Entity settingsEntity;
        Mesh capsule;
        float spawnBudget;
        bool built;
        TerrainSource lastSource;
        int[] counts;
        string configHash;
        float nextConfigCheck;
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(0x9E3779B9u);

        void OnDisable() => Teardown();

        void Teardown()
        {
            if (queryWorld != null && queryWorld.IsCreated)
            {
                var em = queryWorld.EntityManager;
                em.DestroyEntity(creatureQuery);
                if (settingsEntity != Entity.Null && em.Exists(settingsEntity))
                    em.DestroyEntity(settingsEntity);
            }
            settingsEntity = Entity.Null;
            foreach (var b in blobs)
                if (b.IsCreated) b.Dispose();
            blobs.Clear();
            species.Clear();
            if (capsule != null) Destroy(capsule);
            capsule = null;
            built = false;
        }

        void Update()
        {
            if (Material == null || !TryGetWorld(out var source, out var palette, out var playerPos))
                return;
            // Rebuild when the terrain is re-baked or (in the editor) a species or limb type is edited.
            if (built && (!source.Equals(lastSource) || ConfigChanged()))
                Teardown();
            if (!built)
                Build(source, palette);

            Despawn(playerPos.xz);
            spawnBudget = math.min(spawnBudget + SpawnRate * Time.deltaTime, 3f);
            while (spawnBudget >= 1f)
            {
                spawnBudget -= 1f;
                TrySpawn(source, playerPos.xz);
            }
        }

        bool TryGetWorld(out TerrainSource source, out BiomePalette palette, out float3 player)
        {
            source = default;
            palette = null;
            player = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            if (queryWorld != world)
            {
                var em = world.EntityManager;
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                creatureQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
                queryWorld = world;
                built = false;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1 || !playerQuery.TryGetSingleton<LocalTransform>(out var t))
                return false;
            player = t.Position;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        void Build(in TerrainSource source, BiomePalette palette)
        {
            biomes = palette.Biomes;
            foreach (var biome in biomes)
                if (biome != null)
                    foreach (var f in biome.Fauna)
                        if (f.Species != null && !species.Contains(f.Species))
                            species.Add(f.Species);
            foreach (var s in species)
                blobs.Add(CreatureBlobBuilder.Build(s));
            counts = new int[species.Count];

            capsule = BuildCapsule(12, 6, 4);
            var em = queryWorld.EntityManager;
            settingsEntity = em.CreateEntity();
            em.SetName(settingsEntity, "Fauna Render Settings");
            em.AddComponentObject(settingsEntity, new FaunaRenderSettings { Material = Material, Capsule = capsule });
            lastSource = source;
            configHash = ConfigHash();
            built = true;
        }

        string ConfigHash()
        {
            var text = new System.Text.StringBuilder();
            foreach (var s in species)
            {
                text.Append(JsonUtility.ToJson(s));
                foreach (var slot in s.Parts)
                    if (slot.Limb != null)
                        text.Append(JsonUtility.ToJson(slot.Limb));
            }
            return text.ToString();
        }

        bool ConfigChanged()
        {
            if (!Application.isEditor || Time.unscaledTime < nextConfigCheck)
                return false;
            nextConfigCheck = Time.unscaledTime + 1f;
            return ConfigHash() != configHash;
        }

        public int SpeciesIndex(CreatureSpecies s) => species.IndexOf(s);

        // ------------------------------------------------------------------------------------------- population

        void Despawn(float2 player)
        {
            var em = queryWorld.EntityManager;
            var states = creatureQuery.ToComponentDataArray<CreatureState>(Allocator.Temp);
            var bodies = creatureQuery.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var entities = creatureQuery.ToEntityArray(Allocator.Temp);
            System.Array.Clear(counts, 0, counts.Length);
            var far = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var d = math.distance(states[i].Position.xz, player);
                if (d > DespawnDistance)
                    far.Add(entities[i]);
                else if (d <= Radius && bodies[i].Species < counts.Length)
                    counts[bodies[i].Species]++;
            }
            if (far.Length > 0)
                em.DestroyEntity(far.AsArray());
            far.Dispose();
            states.Dispose();
            bodies.Dispose();
            entities.Dispose();
        }

        void TrySpawn(in TerrainSource source, float2 player)
        {
            if (creatureQuery.CalculateEntityCount() >= MaxCreatures || species.Count == 0)
                return;
            // A random spot in the ring around the player; whichever of its biome's species is furthest below its
            // density there gets a group.
            var angle = rng.NextFloat(math.PI * 2f);
            var distance = math.sqrt(rng.NextFloat(MinSpawnDistance * MinSpawnDistance, Radius * Radius));
            var at = player + new float2(math.cos(angle), math.sin(angle)) * distance;
            var ground = source.SampleGround(at);
            if (ground.HasWater && ground.WaterSurface > ground.Height)
                return;
            var biome = biomes[ground.Biomes.Dominant()];
            if (biome == null)
                return;

            var areaHectares = math.PI * Radius * Radius / 10000f;
            int best = -1;
            var bestShortfall = 0f;
            foreach (var f in biome.Fauna)
            {
                var index = f.Species != null ? species.IndexOf(f.Species) : -1;
                if (index < 0)
                    continue;
                var shortfall = f.Density * areaHectares - counts[index];
                if (shortfall > bestShortfall)
                {
                    bestShortfall = shortfall;
                    best = index;
                }
            }
            if (best < 0)
                return;
            var s = species[best];
            var group = rng.NextInt(math.max(1, s.GroupSize.x), math.max(s.GroupSize.x, s.GroupSize.y) + 1);
            for (int i = 0; i < group; i++)
            {
                var p = at + rng.NextFloat2Direction() * rng.NextFloat(0f, 2f + group * 1.5f);
                var g = source.SampleGround(p);
                if (!(g.HasWater && g.WaterSurface > g.Height))
                    Spawn(best, new float3(p.x, g.Height, p.y));
            }
            counts[best] += group;
        }

        /// <summary>Creates one animal of a species (by index) at a point. Also handy for testing.</summary>
        public Entity Spawn(int speciesIndex, float3 position, float yaw = float.NaN)
        {
            var em = queryWorld.EntityManager;
            ref var b = ref blobs[speciesIndex].Value;
            var e = em.CreateEntity(typeof(CreatureState), typeof(CreatureBody));
            em.SetComponentData(e, new CreatureBody { Blob = blobs[speciesIndex], Species = speciesIndex, SegmentCount = b.Segments.Length });
            em.SetComponentData(e, new CreatureState
            {
                Position = position,
                Yaw = float.IsNaN(yaw) ? rng.NextFloat(-math.PI, math.PI) : yaw,
                Phase = rng.NextFloat(),
                Scale = rng.NextFloat(b.Size.x, math.max(b.Size.x, b.Size.y)),
                Mode = CreatureMode.Idle,
                Timer = rng.NextFloat(0.5f, 4f),
                Seed = rng.NextUInt() | 1u
            });
            return e;
        }

        // ------------------------------------------------------------------------------------------- mesh

        /// <summary>
        /// A unit capsule template: positions hold (cos, sin) around the axis and t (0..1) along it; UV0 holds the
        /// cap offset along the axis and the radius factor, in units of the local radius. The creature shader turns
        /// it into any length and taper.
        /// </summary>
        static Mesh BuildCapsule(int sides, int lengthRows, int capRows)
        {
            var rows = new List<Vector3>();   // t, capZ, capR
            for (int k = 0; k < capRows; k++)
            {
                var phi = (1f - k / (float)capRows) * Mathf.PI * 0.5f;
                rows.Add(new Vector3(0f, -Mathf.Sin(phi), Mathf.Cos(phi)));
            }
            for (int k = 0; k <= lengthRows; k++)
                rows.Add(new Vector3(k / (float)lengthRows, 0f, 1f));
            for (int k = 1; k <= capRows; k++)
            {
                var phi = k / (float)capRows * Mathf.PI * 0.5f;
                rows.Add(new Vector3(1f, Mathf.Sin(phi), Mathf.Cos(phi)));
            }

            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var row in rows)
                for (int s = 0; s < sides; s++)
                {
                    var a = s / (float)sides * Mathf.PI * 2f;
                    positions.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), row.x));
                    uvs.Add(new Vector2(row.y, row.z));
                }
            var indices = new List<int>();
            for (int r = 0; r < rows.Count - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * sides + s, b = r * sides + (s + 1) % sides;
                indices.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
            }
            var mesh = new Mesh { name = "Creature Capsule" };
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
