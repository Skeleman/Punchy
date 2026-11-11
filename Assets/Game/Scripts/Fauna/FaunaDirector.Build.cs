using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Fauna
{
    // A-81 row 4: the director's build (the world it reads, baking the species, the fingerprint, carry and config),
    // moved out of FaunaDirector.cs as it was.
    public partial class FaunaDirector
    {
        bool TryGetWorld(out TerrainSource source, out BiomePalette palette)
        {
            source = default;
            palette = null;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            // Without netcode the local world is also where animals live; with it, the server world if we host.
            var sim = world.IsClient() ? NetWorlds.Server : world;
            if (queryWorld != world || simWorld != sim || simWorld != null && !simWorld.IsCreated)
            {
                if (queryWorld != null && queryWorld.IsCreated)
                    Teardown();
                DropAvatar();
                var em = world.EntityManager;
                terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
                paletteQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
                playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                avatarQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerAvatar>());
                replicaQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureReplica>());
                newReplicaQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<CreatureNet>(), ComponentType.ReadOnly<LocalTransform>() },
                    None = new[] { ComponentType.ReadOnly<CreatureReplica>() }
                });
                queryWorld = world;
                simWorld = sim;
                if (sim != null)
                {
                    var sem = sim.EntityManager;
                    // Animals: everything with a body except players' (ours, and other players' stand-ins).
                    var wild = new EntityQueryDesc
                    {
                        All = new[] { ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>() },
                        None = new[] { ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureReplica>() }
                    };
                    simCreatureQuery = sem.CreateEntityQuery(wild);
                    // (Dungeon creatures are their own tier (#24): kept at their posts for the dungeon's life, never
                    // let go by distance nor counted towards the wild, settled or cave caps.)
                    wild.None = new[] { ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<RemoteAvatar>(), ComponentType.ReadOnly<CreatureReplica>(), ComponentType.ReadOnly<DungeonPost>() };
                    wildQuery = sem.CreateEntityQuery(wild);
                    netPlayerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<LocalTransform>());
                    simPlayerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
                    ghostSpawnerQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<CreatureGhostSpawner>());
                    floraQuery = sem.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Flora.FloraLookup>());
                }
                built = false;
            }
            if (!terrainQuery.TryGetSingleton(out source) || paletteQuery.CalculateEntityCount() != 1)
                return false;
            palette = queryWorld.EntityManager.GetComponentObject<BiomePalette>(paletteQuery.GetSingletonEntity());
            return palette != null && palette.Biomes != null;
        }

        void Build(in TerrainSource source, BiomePalette palette)
        {
            // Each species once, by name: in a built player the Domestics table (loaded from Resources) holds its own
            // copies of species the world's palette also lists, so the same species can arrive as two objects. (Counted
            // twice, the indices shifted, and an editor and a build disagreed on what each index meant.)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var stages = new System.Text.StringBuilder();
            void Stage(string what)
            {
                stages.Append(' ').Append(what).Append(' ').Append(watch.ElapsedMilliseconds);
                watch.Restart();
            }
            var named = new HashSet<string>();
            void AddSpecies(CreatureSpecies s)
            {
                if (s != null && named.Add(s.name))
                    species.Add(s);
            }
            biomes = palette.Biomes;
            foreach (var biome in biomes)
                if (biome != null)
                    foreach (var f in biome.Fauna)
                        AddSpecies(f.Species);
            landmarks = palette.Landmarks ?? System.Array.Empty<LandmarkType>();
            foreach (var landmark in landmarks)
                if (landmark != null)
                    foreach (var r in landmark.Residents)
                        AddSpecies(r.Species);
            // A-80: critters, wild folk, messengers, settlement animals and cave folk all come from the overworld table.
            overworld = OverworldTable;
            SpawnTable.Palette = biomes;
            Debug.Log($"FaunaDirector: overworld spawn table {(overworld != null ? overworld.Rows.Count + " rows" : "missing")}");
            foreach (var s in OverworldSpecies(overworld))
                AddSpecies(s);
            caveTable = CaveTable;
            foreach (var s in OverworldSpecies(caveTable))
                AddSpecies(s);
            // The index is how the network names a species, so it must mean the same animal on every machine: sort by
            // asset name, and refuse to network if two share one.
            species.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 1; i < species.Count; i++)
                if (species[i].name == species[i - 1].name)
                    Debug.LogError($"FaunaDirector: two species are both named '{species[i].name}'; creature indices would be ambiguous over the network.");
            speciesByName.Clear();
            for (int i = 0; i < species.Count; i++)
                speciesByName[species[i].name] = i;
            Stage("species");
            // Who hunts whom: a listed species covers its variants too. Matched by name, not by object: in a built
            // player a species loaded from Resources (the Domestics table's Dog) points at Resources copies of its
            // prey, which aren't the objects the palette listed (A-20: dogs saw no rabbits in the build).
            static bool Lists(List<CreatureSpecies> list, CreatureSpecies s)
            {
                if (list == null || s == null)
                    return false;
                foreach (var x in list)
                    if (x != null && x.name == s.name)
                        return true;
                return false;
            }
            var hunted = new bool[species.Count];
            var preyLists = new List<int>[species.Count];
            for (int i = 0; i < species.Count; i++)
            {
                preyLists[i] = new List<int>();
                for (int j = 0; j < species.Count; j++)
                    if (j != i && (Lists(species[i].Hunts, species[j]) || Lists(species[i].Hunts, species[j].Body)))
                    {
                        preyLists[i].Add(j);
                        hunted[j] = true;
                    }
            }
            Stage("prey");
            // A-71: each species' home climate, the middle of the temperature and humidity of the biomes that list it
            // (weighted by how narrow each biome's ranges are: a biome that spans every climate says nothing).
            var homes = new Dictionary<string, Vector3>();
            foreach (var biome in biomes)
            {
                if (biome == null)
                    continue;
                var w = Mathf.Clamp01(1f - (biome.Temperature.y - biome.Temperature.x));
                if (w < 0.05f)
                    continue;
                var t = (biome.Temperature.x + biome.Temperature.y) * 0.5f;
                var h = (biome.Humidity.x + biome.Humidity.y) * 0.5f;
                foreach (var f in biome.Fauna)
                    if (f.Species != null)
                    {
                        homes.TryGetValue(f.Species.name, out var sum);
                        homes[f.Species.name] = sum + new Vector3(t * w, h * w, w);
                    }
            }
            for (int i = 0; i < species.Count; i++)
            {
                Regards(i, out var regard, out var kinship);
                Vector2? home = homes.TryGetValue(species[i].name, out var sum) ? new Vector2(sum.x / sum.z, sum.y / sum.z) : null;
                blobs.Add(CreatureBlobBuilder.Build(species[i], preyLists[i], hunted[i], regard, kinship, home));
            }
            Stage("blobs");

            capsule = BuildCapsule(12, 6, 4);
            // (Fewer sides and rows for the middle and far pose bands, A-70 row 4: 336 triangles, then 112, then 48.)
            capsuleMid = BuildCapsule(8, 3, 2);
            capsuleFar = BuildCapsule(6, 2, 1);
            Stage("capsule");
            var em = queryWorld.EntityManager;
            settingsEntity = em.CreateEntity();
            em.SetName(settingsEntity, "Fauna Render Settings");
            em.AddComponentObject(settingsEntity, new FaunaRenderSettings { Material = Material, Capsule = capsule, CapsuleMid = capsuleMid, CapsuleFar = capsuleFar });
            em.AddComponentData(settingsEntity, new FaunaClock());
            carryBlob = BuildCarryTable();
            Stage("carry");
            em.AddComponentData(settingsEntity, new CarryTable { Blob = carryBlob });
            // (Segment looks, A-97: off the creature blobs so they never move the fauna fingerprint.)
            lookBlob = SurfaceLook.Build(species);
            em.AddComponentData(settingsEntity, new CreatureLooks { Blob = lookBlob });
            if (simWorld != null && simWorld != queryWorld)
            {
                simClockEntity = simWorld.EntityManager.CreateEntity(typeof(FaunaClock));
                simWorld.EntityManager.SetName(simClockEntity, "Fauna Clock");
                simWorld.EntityManager.AddComponentData(simClockEntity, new CarryTable { Blob = carryBlob });
            }
            if (simWorld != null)
            {
                // The tree grid must match FloraSystem's (its tree layer's cell size), so animals find the trees drawn.
                var flora = FindAnyObjectByType<Punchy.Flora.FloraSystem>();
                var cells = flora != null
                    ? new float3(Punchy.Flora.FloraPlacement.CellSize(flora.Ground.TileSize, flora.Ground.CellSize),
                                 Punchy.Flora.FloraPlacement.CellSize(flora.Shrubs.TileSize, flora.Shrubs.CellSize),
                                 Punchy.Flora.FloraPlacement.CellSize(flora.Trees.TileSize, flora.Trees.CellSize))
                    : new float3(0.7f, 2f, 4.5f);
                floraBlob = Punchy.Flora.FloraPlacement.Build(palette, cells, floraSpecies, obstacles: true, grownShapes: pendingShapes);
                // (A-73: where fishing folk stand on the wharves, from the same grown shapes.)
                wharfBlob = WharfSpots.Build(floraSpecies, floraBlob, pendingShapes);
                stages.Append(' ').Append(WharfSpots.LastLog);
                WharfSpots.GullSpots(ref floraBlob.Value, source, ref wharfBlob.Value, gullCells);
                var gulls = 0;
                foreach (var list in gullCells.Values)
                    gulls += list.Count;
                stages.Append(" gulls ").Append(gulls).Append(" (").Append(WharfSpots.MastGulls.Count).Append(" on buoy masts)");
                if (pendingShapes != null)
                    stages.Append(" (shapes waited ").Append(shapeFrames).Append(" frames)");
                pendingShapes = null;
                floraEntity = simWorld.EntityManager.CreateEntity();
                simWorld.EntityManager.SetName(floraEntity, "Flora Lookup");
                simWorld.EntityManager.AddComponentData(floraEntity, new Punchy.Flora.FloraLookup { Blob = floraBlob });
                simWorld.EntityManager.AddComponentData(floraEntity, new WharfSpotLookup { Blob = wharfBlob });
                Stage("flora");
            }
            Debug.Log($"[fauna build] {species.Count} species, ms:{stages} fingerprint {BuildFingerprint():X8}");
            lastSource = source;
            configStamp = ConfigStamp();
            built = true;
            Builds++;
        }

        /// <summary>
        /// Editor only (0 in a player): a hash of everything Build makes, the species order and every baked blob byte
        /// for byte (bodies, carry table, flora lookup), so a change to how Build runs can show it makes the same data.
        /// The blobs go through a temp file (Entities' internal file writer, found by reflection) because this assembly
        /// has no unsafe code to read them directly.
        /// </summary>
        uint BuildFingerprint()
        {
            if (!Application.isEditor)
                return 0;
            var path = System.IO.Path.Combine(Application.temporaryCachePath, "fauna-fingerprint.bin");
            var type = typeof(Unity.Entities.Serialization.BinaryWriter).Assembly.GetType("Unity.Entities.Serialization.StreamBinaryWriter");
            if (type == null)
                return 0;
            using (var writer = (Unity.Entities.Serialization.BinaryWriter)System.Activator.CreateInstance(type,
                       System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                       null, new object[] { path, 65536, default(EntityManager) }, null))
            {
                foreach (var blob in blobs)
                    BlobAssetSerializeExtensions.Write(writer, blob);
                if (carryBlob.IsCreated)
                    BlobAssetSerializeExtensions.Write(writer, carryBlob);
                if (floraBlob.IsCreated)
                    BlobAssetSerializeExtensions.Write(writer, floraBlob);
            }
            var hash = 2166136261u;
            foreach (var s in species)
                foreach (var c in s.name)
                    hash = (hash ^ c) * 16777619u;
            foreach (var b in System.IO.File.ReadAllBytes(path))
                hash = (hash ^ b) * 16777619u;
            System.IO.File.Delete(path);
            return hash;
        }

        /// <summary>
        /// Who can carry and where a carried animal sits, per playable body (players are named by their index in
        /// NetAvatars.PlayableSpecies): folk carry, at the height and reach of a human's arms (0.98 m up, 0.38 m out)
        /// scaled by how high their arms are next to a human's, so a goblin holds it lower and an ogre higher. Taken
        /// from the species, so every machine puts it in the same place.
        /// </summary>
        BlobAssetReference<CarryBlob> BuildCarryTable()
        {
            var avatars = FindAnyObjectByType<Punchy.Net.NetAvatars>();
            var playable = avatars != null ? avatars.PlayableSpecies : new List<CreatureSpecies>();
            var stand = new float[playable.Count];
            var chest = new float[playable.Count];
            var upright = new bool[playable.Count];
            var reference = 0.9f;
            for (int i = 0; i < playable.Count; i++)
            {
                if (playable[i] == null)
                    continue;
                // (How high its arms are: legs plus half the trunk, an upright body's length being its height.)
                var body = CreatureBlobBuilder.Build(playable[i]);
                var size = (playable[i].Size.x + playable[i].Size.y) * 0.5f;
                stand[i] = (body.Value.StandHeight + body.Value.BodyLength * 0.5f) * size;
                chest[i] = math.max(body.Value.FrontRadius, body.Value.RearRadius) * body.Value.BodyFlatten * size;
                upright[i] = body.Value.Upright;
                body.Dispose();
                if (playable[i].Role == DirectorRole.Folk)
                    reference = math.max(stand[i], 0.1f);
            }
            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CarryBlob>();
            var folk = builder.Allocate(ref root.Folk, playable.Count);
            var hold = builder.Allocate(ref root.Hold, playable.Count);
            for (int i = 0; i < playable.Count; i++)
            {
                folk[i] = (byte)(upright[i] ? 1 : 0);
                // (Out by the chest's own depth and a forearm's thickness, so a stout body holds it further out.)
                hold[i] = new float2(0.98f * (stand[i] > 0f ? stand[i] / reference : 1f), chest[i] + 0.1f);
            }
            var blob = builder.CreateBlobAssetReference<CarryBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }

        /// <summary>
        /// Folk (upright species) only: how species i regards a player in each species' body (Z-52), from its Regard
        /// list (a listed species covers its variants) or else RegardToAll, and its kinship: its own kind (the same
        /// species it's based on) kin, a Rival entry a rival. Null for the rest.
        /// </summary>
        void Regards(int i, out float[] regard, out byte[] kinship)
        {
            regard = null;
            kinship = null;
            var folk = species[i].Body;
            if (folk == null || !folk.Upright)
                return;
            regard = new float[species.Count];
            kinship = new byte[species.Count];
            for (int j = 0; j < species.Count; j++)
            {
                var other = species[j];
                if (other.Body == folk)
                {
                    kinship[j] = FolkKin.Kin;
                    continue;
                }
                regard[j] = folk.RegardToAll;
                foreach (var r in folk.Regard)
                    if (r != null && r.Species != null && (r.Species == other || r.Species == other.Body))
                    {
                        regard[j] = r.Value;
                        kinship[j] = r.Rival ? FolkKin.Rival : FolkKin.Stranger;
                        break;
                    }
            }
        }

        /// <summary>
        /// Changes whenever any species, limb type or prey list in use is edited (editor only): the sum of their
        /// dirty counters, which Unity bumps on every inspector edit. Cheap enough to check every second.
        /// </summary>
        long ConfigStamp()
        {
            long stamp = 0;
#if UNITY_EDITOR
            foreach (var variant in species)
            {
                stamp += UnityEditor.EditorUtility.GetDirtyCount(variant);
                var s = variant.Body;
                if (s != variant)
                    stamp += UnityEditor.EditorUtility.GetDirtyCount(s);
                foreach (var slot in s.Parts)
                    if (slot.Limb != null)
                        stamp += UnityEditor.EditorUtility.GetDirtyCount(slot.Limb);
            }
#endif
            return stamp;
        }

        bool ConfigChanged()
        {
            if (!Application.isEditor || Time.unscaledTime < nextConfigCheck)
                return false;
            nextConfigCheck = Time.unscaledTime + 1f;
            return ConfigStamp() != configStamp;
        }

        /// <summary>A species' index (by name: a build can hold more than one copy of the same species asset).</summary>
        public int SpeciesIndex(CreatureSpecies s) => s != null ? SpeciesIndex(s.name) : -1;
        public int SpeciesIndex(string name) => name != null && speciesByName.TryGetValue(name, out var i) ? i : -1;
        /// <summary>The first species playing a part for the director (CreatureSpecies.Role), or -1.</summary>
        public int RoleIndex(DirectorRole role)
        {
            for (int i = 0; i < species.Count; i++)
                if (species[i] != null && species[i].Role == role)
                    return i;
            return -1;
        }
        /// <summary>The species asset at an index (for things that read its data directly, as the sound does).</summary>
        public CreatureSpecies SpeciesAt(int index) => index >= 0 && index < species.Count ? species[index] : null;
        public int SpeciesCount => species.Count;
        /// <summary>The baked body at an index (tests read a species' tables from it: the stance check).</summary>
        public BlobAssetReference<CreatureBlob> BlobAt(int index) => index >= 0 && index < blobs.Count ? blobs[index] : default;
        /// <summary>How many animals birds of prey have caught (for the test log).</summary>
        public static int Caught;
        public Habitat SpeciesHabitat(int index) => species[index].Body.Habitat;
        public bool SpeciesCaveDweller(int index) => species[index].Body.CaveDweller;
        /// <summary>A species that goes into caves now and then (CaveAffinity), though it lives outside.</summary>
        public bool SpeciesVisitsCaves(int index) => species[index].Body.CaveAffinity > 0f;
        /// <summary>A species' name as people say it ("MountainLion": "mountain lion").</summary>
        public string SpeciesName(int index)
        {
            if (index < 0 || index >= species.Count)
                return "animal";
            var name = species[index].name;
            var words = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    words.Append(' ');
                words.Append(char.ToLowerInvariant(name[i]));
            }
            return words.ToString();
        }
        /// <summary>Whether this machine simulates animals (alone or hosting), so Spawn works.</summary>
        public bool CanSpawn => simWorld != null && simWorld.IsCreated && built;
    }
}
