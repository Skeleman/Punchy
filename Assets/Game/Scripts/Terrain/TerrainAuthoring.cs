using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

namespace Punchy.Terrain
{
    /// <summary>
    /// Place one of these in the world subscene. Baking turns it into the TerrainConfig and
    /// TerrainSource singletons that the streaming system reads.
    /// </summary>
    public class TerrainAuthoring : MonoBehaviour
    {
        public Material Material;
        [Tooltip("Material for sea, lake and river surfaces. Leave empty to hide water.")]
        public Material WaterMaterial;

        [Header("Chunks & LOD")]
        public float ChunkSize = 64f;
        [Tooltip("Quads per chunk edge at full detail. Power of two; each LOD halves it.")]
        public int Lod0Quads = 64;
        [Range(1, 6)] public int LodLevels = 4;
        [Tooltip("Full detail out to this distance; each further LOD covers double. Keep it at least ~2.5× Chunk Size " +
                 "so LODs have room to geomorph smoothly.")]
        public float Lod0Distance = 160f;
        [Range(1, 32)] public int ViewRadiusChunks = 12;
        [Range(1, 64)] public int MaxBuildsPerFrame = 12;
        [Tooltip("How far chunk edge skirts hang down to hide cracks between LODs.")]
        public float SkirtDepth = 6f;

        [Header("World")]
        public WorldSettings World = WorldSettings.Default;

        [Header("Biomes")]
        [Tooltip("Land biomes. The first is always used at the world origin. Order matters for biome map images.")]
        public List<BiomeDefinition> Biomes = new List<BiomeDefinition>();
        [Tooltip("Fills everything beyond the coast. Leave empty for an endless continent.")]
        public BiomeDefinition OceanBiome;
        public BiomeSourceKind BiomeSource = BiomeSourceKind.Noise;
        public BiomeNoiseSettings BiomeNoise = BiomeNoiseSettings.Default;
        [Tooltip("Used when Biome Source is Map. Create one with the Export button below.")]
        public BiomeMapAsset BiomeMap;
        [Tooltip("Realms (Frost, Fire, Desert…) that noise placement divides the island into before picking biomes. " +
                 "The realm preview button below shows them whether or not Use Realms is on.")]
        public RealmSet Realms;
        [Tooltip("Place noise biomes by realm (re-bakes and reshuffles the island). Off = every region picks from all biomes.")]
        public bool UseRealms;

        [Header("Rivers")]
        public RiverSettings Rivers = RiverSettings.Default;

        [Header("Landmarks")]
        [Tooltip("Landmarks placed by hand. Biomes also scatter their own (see each biome's Landmarks).")]
        public List<PlacedLandmark> PlacedLandmarks = new List<PlacedLandmark>();
        [Tooltip("Landmarks that stand beside the roads between settlements (inns, shrines, campsites), placed once the " +
                 "roads are routed.")]
        public List<RoadsideLandmark> Roadside = new List<RoadsideLandmark>();

        [Header("Heights")]
        [FormerlySerializedAs("Source")]
        [Tooltip("Noise: heights come from the biomes' shapes. Heightmap: heights come from an image.")]
        public HeightSourceKind HeightSource = HeightSourceKind.Noise;
        [Tooltip("Used when Height Source is Heightmap. Create one with the Export button below.")]
        public HeightmapAsset Heightmap;

        [Header("Export (noise → images)")]
        [Tooltip("Heightmap pixels per side. 2^n + 1 keeps samples evenly spaced.")]
        public int ExportResolution = 4097;
        [Tooltip("Biome map pixels per side. Biomes change slowly, so this can be much lower.")]
        public int ExportBiomeResolution = 513;
        [Tooltip("Width of the exported square, centred on the origin. Should cover the landmass and some sea.")]
        public float ExportWorldSize = 9216f;

        /// <summary>All biomes in id order: land biomes (empty slots removed), then the ocean.</summary>
        public List<BiomeDefinition> Palette() => BiomeBaking.Palette(Biomes, OceanBiome);

        /// <summary>All landmark types in use, in the landmark blob's order.</summary>
        public List<LandmarkType> LandmarkTypes()
        {
            var types = LandmarkBaking.Types(Palette(), PlacedLandmarks);
            // Roadside types after the rest, so adding one doesn't move the others' indices.
            foreach (var r in Roadside)
                if (r.Type != null && !types.Contains(r.Type))
                    types.Add(r.Type);
            LandmarkBaking.AddLooks(types);
            // #36: fishing villages last of all, so no other type's index moves (placed on island v2 only).
            var village = FishingVillageBaking.LoadType();
            if (village != null && !types.Contains(village))
                types.Add(village);
            // #38: turtle folk settlements after the villages, for the same reason.
            var turtles = TurtleVillageBaking.LoadType();
            if (turtles != null && !types.Contains(turtles))
                types.Add(turtles);
            // #37: the sea folk village and its landing after those.
            foreach (var sea in new[] { SeaFolkBaking.LoadVillage(), SeaFolkBaking.LoadLanding() })
                if (sea != null && !types.Contains(sea))
                    types.Add(sea);
            // W-114: the ribcage last of all.
            var ribcage = RibcageBaking.LoadType();
            if (ribcage != null && !types.Contains(ribcage))
                types.Add(ribcage);
            // W-122: the marsh folk village after it.
            var marshFolk = MarshFolkBaking.LoadType();
            if (marshFolk != null && !types.Contains(marshFolk))
                types.Add(marshFolk);
            return types;
        }

        /// <summary>
        /// Builds the runtime TerrainSource (and its blobs) from these settings. Shared by the baker and the
        /// exporter. Heightmap/biome-map images are only loaded when given (editor-only callers pass them in).
        /// </summary>
        public TerrainSource BuildSource(Allocator allocator,
                                         BlobAssetReference<HeightmapBlob> heightmap = default,
                                         BlobAssetReference<BiomeMapBlob> biomeMap = default)
        {
            var palette = Palette();
            var source = new TerrainSource
            {
                World = World,
                HeightKind = heightmap.IsCreated ? HeightSourceKind.Heightmap : HeightSourceKind.Noise,
                Heightmap = heightmap,
                BiomeKind = biomeMap.IsCreated ? BiomeSourceKind.Map : BiomeSourceKind.Noise,
                BiomeMap = biomeMap,
                BiomeNoise = BiomeNoise,
                SpawnCell = BiomeSampling.SpawnCell(BiomeNoise),
                RiverSettings = Rivers
            };
            if (palette.Count > 0)
            {
                source.Biomes = BiomeBaking.BuildBiomeSet(palette, OceanBiome != null, World.Seed, allocator);
                if (!biomeMap.IsCreated)
                {
                    var coast = source;
                    source.BiomeCells = BiomeSampling.BuildCellTable(BiomeNoise, ref source.Biomes.Value, World.WorldSize * 0.5f + World.CoastWidth * 2f, allocator,
                                                                     UseRealms && Realms != null ? Realms.Plan(palette) : null, xz => coast.OceanFactor(xz) < 0.5f);
                }
            }
            BuildPlaces(ref source, palette, LandmarkTypes(), PlacedLandmarks, Roadside, allocator);
            return source;
        }

        /// <summary>Rivers, landmarks, settlements, caves and anchors, in that order: the part of a bake that follows the ground.</summary>
        public static void BuildPlaces(ref TerrainSource source, List<BiomeDefinition> palette, List<LandmarkType> landmarkTypes,
                                       List<PlacedLandmark> placed, List<RoadsideLandmark> roadsideTypes, Allocator allocator, bool villages = false)
        {
            // W-14: each stage's time goes to the "[bake] stages" log line (island v2 rebuilds all of this at launch).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var stages = new System.Text.StringBuilder("[bake] stages ms:");
            void Stage(string name) { stages.Append($" {name} {watch.ElapsedMilliseconds}"); watch.Restart(); }
            source.PondsOn = false;
            source.RiverRoughOn = false;
            source.PondMask = default;
            // W-53 (W-37, option b): wide rivers on island v2 meander in bends that scale with width, but every place
            // (landmarks, settlements, the salt tower) is chosen against the jittered rivers they were always placed
            // by, so no landmark moves. Two river maps are baked; the jittered one is dropped once places are set.
            var drawnRivers = default(BlobAssetReference<RiverMapBlob>);
            if (source.RiverSettings.Enabled && palette.Count > 0)
            {
                // Both bakes see the ground without rivers (a source with a river map samples its carve).
                var bare = source;
                // W-76: the second bake copies the first's drainage grid (same ground, same settings) instead of
                // sampling and flooding the whole world again.
                using var drainage = new RiverBaking.Drainage();
                if (source.RiverSettings.Smooth)
                    drawnRivers = RiverBaking.Build(bare, source.RiverSettings, allocator, drainage: drainage);
                source.Rivers = RiverBaking.Build(bare, source.RiverSettings, allocator, meander: false, drainage: drainage);
            }
            Stage("rivers");
            if (palette.Count > 0)
                source.Landmarks = LandmarkBaking.Build(source, palette, landmarkTypes, placed, allocator);
            Stage("landmarks");
            if (source.Landmarks.IsCreated)
            {
                source.Settlements = SettlementBaking.Build(source, landmarkTypes, roadsideTypes, out var roadside, allocator, palette);
                if (roadside.Count > 0)
                {
                    var before = source.Landmarks;
                    LandmarkBaking.ApplyLooks(source, palette, landmarkTypes, roadside);
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, roadside, allocator);
                    before.Dispose();
                }
            }
            // W-27: one-per-island landmarks (the salt tower) go last of all, once roads and settlements are known.
            if (source.Landmarks.IsCreated)
            {
                var singles = SaltTowerBaking.Place(source, palette, landmarkTypes);
                if (singles.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, singles, allocator);
                    before.Dispose();
                }
            }
            if (drawnRivers.IsCreated)
            {
                source.Rivers.Dispose();
                source.Rivers = drawnRivers;
            }
            Stage("settlements");
            stages.Append(' ').Append(SettlementBaking.LastTiming);
            if (source.Landmarks.IsCreated)
                source.Caves = CaveBaking.Build(source, palette, landmarkTypes, allocator);
            Stage("caves");
            // #36: fishing villages after the caves too, each on its own new spot, so no landmark, road or cave moves.
            if (villages && source.Landmarks.IsCreated)
            {
                var mouths = FishingVillageBaking.Place(source, landmarkTypes);
                Stage("fishing");
                if (mouths.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, mouths, allocator);
                    before.Dispose();
                }
                // #38: turtle folk settlements on the highest peaks, last of all, each on its own new spot.
                var peaks = TurtleVillageBaking.Place(source, landmarkTypes);
                Stage("turtles");
                if (peaks.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, peaks, allocator);
                    before.Dispose();
                }
                // #37: the sea folk settlement in the largest deep lake and its landing, after the turtles.
                // W-138: at the spots SettlementBaking found for the landing spur.
                var lake = SeaFolkBaking.Place(source, landmarkTypes, SettlementBaking.LastSeaFolk);
                Stage("seafolk");
                if (lake.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, lake, allocator);
                    before.Dispose();
                }
                // W-114: the Ashlands ribcage on its own spot, after everything else, so no other landmark moves.
                var bones = RibcageBaking.Place(source, palette, landmarkTypes);
                Stage("ribcage");
                if (bones.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, bones, allocator);
                    before.Dispose();
                }
                // W-122: the marsh folk villages on their own spots, after the ribcage, so no other landmark moves.
                var marsh = MarshFolkBaking.Place(source, palette, landmarkTypes);
                Stage("marshfolk");
                if (marsh.Count > 0)
                {
                    var before = source.Landmarks;
                    source.Landmarks = LandmarkBaking.Append(before, landmarkTypes, marsh, allocator);
                    before.Dispose();
                }
            }
            // #35: bat folk read the caves only and write nothing anything else reads, so every hash stays put.
            source.BatFolk = BatFolkBaking.Build(source, allocator);
            UnityEngine.Debug.Log(BatFolkBaking.LastLog);
            Stage("batfolk");
            source.Anchors = AnchorBaking.Build(source, landmarkTypes, allocator);
            Stage("anchors");
            // W-28: icebergs on their own stream, so nothing above (or any hash) sees them. W-111: before the water
            // surface pass, which they don't read (sea coasts only), so that pass is the bake's last step.
            source.Icebergs = IcebergBaking.Build(source, palette, allocator);
            UnityEngine.Debug.Log(IcebergBaking.LastLog);
            Stage("icebergs");
            // W-111: the water surface pass runs last; riverbeds and channels were cut at the start (rivers stage).
            // W-43c: rough riverbanks (width, slope and shoreline wobble) only now, so no landmark or cave moves.
            source.RiverRoughOn = true;
            source.PondsOn = true;
            // W-10: label the ponds' wet cells and drop every pond under 3 cells (with the dry ring round it).
            // W-14: one labelling pass gives the mask and the log (the two extra PondCount passes cost ~2.8 s each).
            source.PondMask = source.BuildPondMask(allocator, out var dropped, out var pondReport);
            Stage("pondmask");
            UnityEngine.Debug.Log($"[ponds] {pondReport}. Dropped {dropped} cells.");
            UnityEngine.Debug.Log(stages.ToString());
        }

        class Baker : Baker<TerrainAuthoring>
        {
            public override void Bake(TerrainAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                AddComponent(entity, new TerrainConfig
                {
                    Material = authoring.Material,
                    WaterMaterial = authoring.WaterMaterial,
                    ChunkSize = authoring.ChunkSize,
                    Lod0Quads = Mathf.Max(1, Mathf.ClosestPowerOfTwo(authoring.Lod0Quads)),
                    LodLevels = authoring.LodLevels,
                    Lod0Distance = authoring.Lod0Distance,
                    ViewRadiusChunks = authoring.ViewRadiusChunks,
                    MaxBuildsPerFrame = authoring.MaxBuildsPerFrame,
                    SkirtDepth = authoring.SkirtDepth
                });

                var palette = authoring.Palette();
                // Re-bake whenever any biome or surface asset is edited.
                foreach (var biome in palette)
                {
                    DependsOn(biome);
                    DependsOn(biome.BaseSurface);
                    foreach (var layer in biome.Layers)
                        DependsOn(layer.Surface);
                }
                // Realms decide which biome each region gets, when they're on.
                if (authoring.UseRealms && authoring.Realms != null)
                {
                    DependsOn(authoring.Realms);
                    foreach (var realm in authoring.Realms.Realms)
                        DependsOn(realm);
                }
                var landmarks = authoring.LandmarkTypes();
                foreach (var type in landmarks)
                {
                    DependsOn(type);
                    DependsOn(type.Ground);
                }
                // Cave styles shape the passages, so editing one re-bakes too.
                foreach (var style in CaveStyleTable.Collect(landmarks))
                    DependsOn(style);
                // So do settlement layouts (streets and plots level the ground).
                foreach (var layout in SettlementBaking.Layouts(landmarks))
                {
                    DependsOn(layout);
                    DependsOn(layout.StreetSurface);
                }
                if (palette.Count == 0)
                    Debug.LogWarning("Terrain has no biomes assigned; it will be flat and grey.", authoring);
                int[] biomeRealms = null;
                if (authoring.UseRealms && authoring.Realms != null)
                {
                    var plan = authoring.Realms.Plan(palette);
                    biomeRealms = new int[palette.Count];
                    for (int b = 0; b < palette.Count; b++)
                        biomeRealms[b] = System.Array.FindIndex(plan.Members, m => System.Array.IndexOf(m, b) >= 0);
                }
                AddComponentObject(entity, new BiomePalette { Biomes = palette.ToArray(), Landmarks = landmarks.ToArray(), BiomeRealms = biomeRealms, Placed = authoring.PlacedLandmarks, Roadside = authoring.Roadside });

                var heightmap = default(BlobAssetReference<HeightmapBlob>);
                if (authoring.HeightSource == HeightSourceKind.Heightmap)
                {
                    heightmap = BuildHeightmapBlob(authoring.Heightmap);
                    if (!heightmap.IsCreated)
                        Debug.LogWarning("Height source is Heightmap but no valid heightmap is assigned; using noise.", authoring);
                }

                var biomeMap = default(BlobAssetReference<BiomeMapBlob>);
                if (authoring.BiomeSource == BiomeSourceKind.Map)
                {
                    biomeMap = BuildBiomeMapBlob(authoring.BiomeMap, palette);
                    if (!biomeMap.IsCreated)
                        Debug.LogWarning("Biome source is Map but no valid biome map is assigned; using noise.", authoring);
                }

                var source = authoring.BuildSource(Allocator.Persistent, heightmap, biomeMap);
                if (source.Heightmap.IsCreated) AddBlobAsset(ref source.Heightmap, out _);
                if (source.BiomeMap.IsCreated) AddBlobAsset(ref source.BiomeMap, out _);
                if (source.Biomes.IsCreated) AddBlobAsset(ref source.Biomes, out _);
                if (source.BiomeCells.IsCreated) AddBlobAsset(ref source.BiomeCells, out _);
                if (source.Rivers.IsCreated) AddBlobAsset(ref source.Rivers, out _);
                if (source.Landmarks.IsCreated) AddBlobAsset(ref source.Landmarks, out _);
                if (source.Settlements.IsCreated) AddBlobAsset(ref source.Settlements, out _);
                if (source.Caves.IsCreated) AddBlobAsset(ref source.Caves, out _);
                if (source.BatFolk.IsCreated) AddBlobAsset(ref source.BatFolk, out _);
                if (source.Icebergs.IsCreated) AddBlobAsset(ref source.Icebergs, out _);
                if (source.Anchors.IsCreated) AddBlobAsset(ref source.Anchors, out _);
                if (source.PondMask.IsCreated) AddBlobAsset(ref source.PondMask, out _);
                AddComponent(entity, source);
            }

            // Both image loaders read the PNG file itself rather than the imported texture,
            // so import compression never loses precision.

            BlobAssetReference<HeightmapBlob> BuildHeightmapBlob(HeightmapAsset asset)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);
                return LoadHeightmap(asset);
#else
                return default;
#endif
            }

            BlobAssetReference<BiomeMapBlob> BuildBiomeMapBlob(BiomeMapAsset asset, List<BiomeDefinition> palette)
            {
#if UNITY_EDITOR
                DependsOn(asset);
                if (asset == null || asset.Image == null)
                    return default;
                DependsOn(asset.Image);
                var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
                return BiomeBaking.DecodeBiomeMap(System.IO.File.ReadAllBytes(path), asset, palette, Allocator.Persistent);
#else
                return default;
#endif
            }
        }

#if UNITY_EDITOR
        public static BlobAssetReference<HeightmapBlob> LoadHeightmap(HeightmapAsset asset)
        {
            var path = UnityEditor.AssetDatabase.GetAssetPath(asset.Image);
            var heights = HeightmapIO.DecodePng(System.IO.File.ReadAllBytes(path), out var resolution);
            if (heights == null || resolution < 2)
                return default;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<HeightmapBlob>();
            root.Resolution = resolution;
            root.Origin = asset.Origin;
            root.WorldSize = asset.WorldSize;
            root.MinHeight = asset.MinHeight;
            root.HeightStep = (asset.MaxHeight - asset.MinHeight) / 65535f;
            var array = builder.Allocate(ref root.Heights, heights.Length);
            for (int i = 0; i < heights.Length; i++)
                array[i] = heights[i];

            var blob = builder.CreateBlobAssetReference<HeightmapBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
#endif
    }
}
