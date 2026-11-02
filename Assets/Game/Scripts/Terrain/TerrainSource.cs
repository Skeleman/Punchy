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
        /// <summary>Generator version 2 only: metres added to the biome shape heights (ridges between realms, the lake bowl).</summary>
        public BlobAssetReference<HeightmapBlob> Island;

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
        /// <summary>Bat folk settlements (#35, baked after caves, read by nothing else): their chambers and which cave pools are white.</summary>
        public BlobAssetReference<BatFolkSetBlob> BatFolk;
        /// <summary>Set when every place is baked: biome ponds are dug only after that, so they never move a landmark, road, cave or anchor.</summary>
        public bool PondsOn;
        /// <summary>
        /// W-10: 0..1 per 16 m cell (cell centres, sampled bilinearly) scaling the biome ponds' dig: 0 over every pond
        /// smaller than <see cref="PondMinCells"/> cells and the dry ring round it, so no specks of water are left.
        /// Built at the end of a bake by <see cref="BuildPondMask"/>; without it every pond is dug.
        /// </summary>
        public BlobAssetReference<HeightmapBlob> PondMask;
        public const float PondStep = 16f;
        public const int PondMinCells = 3;

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
            if (Island.IsCreated)
                height += Island.Value.Sample(xz);
            return World.SeaLevel + height;
        }

        /// <summary>
        /// Biome ponds (a swamp's pools) dug into the ground. Only once the landmarks exist: rivers, landmarks and
        /// settlements are placed on the ground without them, so the ponds never move any of those.
        /// </summary>
        float Ponds(float2 xz, in BiomeWeights shape, float h) => Ponds(xz, shape, h, out _);

        /// <summary>
        /// As above; level is a terrace pool's own water surface (float.MinValue when none). W-24 (#23): a pond whose
        /// bed stays above sea level held no water, so ponds on a hillside swamp were dry pits. Those ponds are now
        /// stepped pools instead: the ground (plus a little noise, so the steps wander) is cut into bands
        /// <see cref="TerraceStep"/> high, each band's pool is flat at the band's foot, held by a low bank on its
        /// downhill side and a cut face up to the next band's bank. Flat ground gets one level pool. Ponds that reach
        /// below sea level (lowland swamps) are unchanged.
        /// </summary>
        float Ponds(float2 xz, in BiomeWeights shape, float h, out float level)
        {
            level = float.MinValue;
            var dig = PondDig(xz, shape, out var cover);
            if (dig <= 0f)
                return h;
            if (h - dig < World.SeaLevel || cover <= 0f)
                return h - dig;
            return Terrace(xz, h, cover, out level);
        }

        /// <summary>
        /// How far the biome ponds dig below the ground here before any terracing (masked, kept off streets and plots),
        /// and cover, 0..1, how far inside a pond this is. The pond grid labels ponds by this, so a stepped pool's banks
        /// don't split it into specks.
        /// </summary>
        float PondDig(float2 xz, in BiomeWeights shape, out float cover)
        {
            cover = 0f;
            if (!PondsOn || !HasBiomes)
                return 0f;
            ref var set = ref Biomes.Value;
            // W-11: a biome map (island v2) has small swamp patches whose blurred shape weights never pass 0.4, so
            // it had no ponds at all; there the core test uses the map's sharp weights. Cell-based worlds (v1) are unchanged.
            var core = UsesBiomeMap ? BiomeSampling.SampleMap(ref BiomeMap.Value, xz) : shape;
            var dig = 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = core.Weights[i];
                if (w <= 0.4f)   // W-08: only in a biome's core; at region borders the blend left lone specks
                    continue;
                ref var biome = ref set.Biomes[core.Ids[i]];
                // W-24: v2's swamp is a hillside of stepped pools, so it gets more of them (v1 unchanged).
                var d = math.smoothstep(0.4f, 0.75f, w) * (UsesBiomeMap ? BiomeSampling.Pond(ref biome, xz, V2PondDensity, V2PondCover) : BiomeSampling.Pond(ref biome, xz));
                dig += d;
                if (biome.PondDepth > 0f)
                    cover += d / biome.PondDepth;
            }
            if (dig <= 0f)
                return 0f;
            if (PondMask.IsCreated)
            {
                var mask = PondMask.Value.Sample(xz);
                dig *= mask;
                cover *= mask;
            }
            // W-24: no pool on a street or plot (Settle levels them afterwards, which left half-pools at their edges).
            if (Settlements.IsCreated)
            {
                var keep = SettlementSampling.Keep(ref Settlements.Value, xz);
                dig *= keep;
                cover *= keep;
            }
            return dig;
        }

        /// <summary>W-24: the ground of a stepped pool at xz (h the natural ground, cover as from PondDig) and its water level.</summary>
        float Terrace(float2 xz, float h, float cover, out float level)
        {
            level = float.MinValue;
            // Bands follow the ground's contours; the wobble only moves the band edges, never a level. Bands follow the ground's contours; the wobble only moves the band edges, never a level.
            var q = (h + noise.snoise(xz * 0.025f + 31.7f) * TerraceStep * 0.35f) / TerraceStep;
            var k = math.floor(q);
            var f = q - k;
            var water = k * TerraceStep;
            var bank = water + TerraceBank;
            var upper = water + TerraceStep + TerraceBank;   // the next band's bank
            var band = math.lerp(bank, water - TerraceDepth, math.smoothstep(0f, 0.22f, f));
            band = math.lerp(band, upper, math.smoothstep(0.8f, 1f, f));
            // Out towards the pond's edge the band blends back into the natural ground, which is above the water there
            // (the wobble can put it a little below): a rim at bank height first, so the pool ends against it, not in mid air.
            var rim = math.lerp(h, math.max(h, bank), math.smoothstep(0f, 0.3f, cover));
            var inside = math.smoothstep(0.3f, 0.65f, math.saturate(cover));
            var g = math.lerp(rim, band, inside);
            if (inside > 0f && g < water - 0.02f)
                level = water;
            return g;
        }

        /// <summary>W-24: height between stepped pools (m), how far a bank stands above its pool, and the pools' depth.</summary>
        public const float TerraceStep = 0.6f, TerraceBank = 0.15f, TerraceDepth = 0.45f;
        /// <summary>W-24: island v2's pond noise frequency scale and extra cover (biome map worlds only).</summary>
        public const float V2PondDensity = 1.2f, V2PondCover = 0.3f;

        /// <summary>Counts separate biome ponds (cells digging 0.3 m or more, 4-connected) on a grid of the given step; for logs.</summary>
        public int PondCount(float step, out int wetCells) => PondCount(step, out wetCells, out _);

        /// <summary>As above, also describing the size spread (cells per pond) for logs.</summary>
        public int PondCount(float step, out int wetCells, out string sizes)
        {
            sizes = "";
            var sizeList = new System.Collections.Generic.List<int>();
            wetCells = 0;
            if (!PondsOn || !HasBiomes) return 0;
            var wet = PondGrid(step, out var n, out wetCells);
            LabelPonds(wet, n, sizeList);
            int found = sizeList.Count;
            sizes = DescribePonds(sizeList);
            return found;
        }

        /// <summary>The size spread (cells per pond) of a list of pond sizes, for logs. Sorts the list.</summary>
        static string DescribePonds(System.Collections.Generic.List<int> sizeList)
        {
            if (sizeList.Count == 0) return "";
            sizeList.Sort();
            int singles = 0; foreach (var z in sizeList) if (z == 1) singles++;
            return $"cells per pond min {sizeList[0]} median {sizeList[sizeList.Count / 2]} p90 {sizeList[sizeList.Count * 9 / 10]} max {sizeList[sizeList.Count - 1]}, single-cell {singles}";
        }

        /// <summary>
        /// Which cells of a step-metre grid over the world a biome pond digs 0.3 m or more into (row-major, n per row).
        /// W-14: rows run in parallel (sampling only reads blobs); it was ~2.8 s per call on one thread in island v2's launch bake.
        /// </summary>
        bool[] PondGrid(float step, out int n, out int wetCells)
        {
            int size = (int)(World.WorldSize / step);
            var wet = new bool[size * size];
            var half = World.WorldSize * 0.5f;
            var self = this;
            System.Threading.Tasks.Parallel.For(0, size, z =>
            {
                for (int x = 0; x < size; x++)
                {
                    var xz = new float2(x + 0.5f, z + 0.5f) * step - half;
                    self.SampleBiomes(xz, out _, out var shape);
                    wet[z * size + x] = self.PondDig(xz, shape, out _) >= 0.3f;   // W-24: before terracing (banks would split a stepped pool)
                }
            });
            n = size;
            wetCells = 0;
            foreach (var w in wet) if (w) wetCells++;
            return wet;
        }

        /// <summary>Labels 4-connected wet cells: each cell's pond index (-1 = dry); each pond's cell count is added to sizes.</summary>
        static int[] LabelPonds(bool[] wet, int n, System.Collections.Generic.List<int> sizes)
        {
            var label = new int[wet.Length];
            for (int i = 0; i < label.Length; i++) label[i] = -1;
            var stack = new System.Collections.Generic.Stack<int>();
            for (int i = 0; i < wet.Length; i++)
            {
                if (!wet[i] || label[i] >= 0) continue;
                int id = sizes.Count, size = 0;
                label[i] = id; stack.Push(i);
                while (stack.Count > 0)
                {
                    var c = stack.Pop(); int cx = c % n, cz = c / n; size++;
                    if (cx > 0 && wet[c - 1] && label[c - 1] < 0) { label[c - 1] = id; stack.Push(c - 1); }
                    if (cx < n - 1 && wet[c + 1] && label[c + 1] < 0) { label[c + 1] = id; stack.Push(c + 1); }
                    if (cz > 0 && wet[c - n] && label[c - n] < 0) { label[c - n] = id; stack.Push(c - n); }
                    if (cz < n - 1 && wet[c + n] && label[c + n] < 0) { label[c + n] = id; stack.Push(c + n); }
                }
                sizes.Add(size);
            }
            return label;
        }

        /// <summary>
        /// W-10: once the ponds are on, labels their connected wet cells on the PondStep grid and returns a mask that
        /// drops every pond under PondMinCells cells: 0 on its cells and on the dry cells round it (unless they also
        /// touch a kept pond), 1 elsewhere. Assign it to PondMask. Default when nothing is dropped.
        /// </summary>
        public BlobAssetReference<HeightmapBlob> BuildPondMask(Unity.Collections.Allocator allocator, out int dropped) => BuildPondMask(allocator, out dropped, out _);

        /// <summary>
        /// As above, with a log line of the ponds before and after the mask. W-14: "after" is the ponds of PondMinCells or
        /// more from the same labelling (the mask keeps them whole), not a second full sampling pass.
        /// </summary>
        public BlobAssetReference<HeightmapBlob> BuildPondMask(Unity.Collections.Allocator allocator, out int dropped, out string report)
        {
            dropped = 0;
            report = "no ponds";
            if (!PondsOn || !HasBiomes) return default;
            var wet = PondGrid(PondStep, out var n, out var wetBefore);
            var sizes = new System.Collections.Generic.List<int>();
            var label = LabelPonds(wet, n, sizes);
            var kept = new System.Collections.Generic.List<int>();
            int wetAfter = 0;
            foreach (var z in sizes) if (z >= PondMinCells) { kept.Add(z); wetAfter += z; }
            int pondsBefore = sizes.Count;
            report = $"before {pondsBefore} separate, {wetBefore} wet; {DescribePonds(new System.Collections.Generic.List<int>(sizes))}. After {kept.Count} separate, {wetAfter} wet cells of 16 m; {DescribePonds(kept)}";
            var mask = new ushort[n * n];
            for (int i = 0; i < mask.Length; i++) mask[i] = ushort.MaxValue;
            for (int i = 0; i < label.Length; i++)
            {
                if (label[i] < 0 || sizes[label[i]] >= PondMinCells) continue;
                dropped++;
                int cx = i % n, cz = i / n;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int j = z * n + x;
                        if (label[j] >= 0 ? sizes[label[j]] < PondMinCells : !TouchesKeptPond(label, sizes, n, x, z))
                            mask[j] = 0;
                    }
            }
            if (dropped == 0) return default;
            using var builder = new BlobBuilder(Unity.Collections.Allocator.Temp);
            ref var root = ref builder.ConstructRoot<HeightmapBlob>();
            root.Resolution = n;
            root.Origin = new float2(PondStep * 0.5f - World.WorldSize * 0.5f);
            root.WorldSize = (n - 1) * PondStep;
            root.MinHeight = 0f;
            root.HeightStep = 1f / ushort.MaxValue;
            var heights = builder.Allocate(ref root.Heights, mask.Length);
            for (int i = 0; i < mask.Length; i++) heights[i] = mask[i];
            return builder.CreateBlobAssetReference<HeightmapBlob>(allocator);
        }

        static bool TouchesKeptPond(int[] label, System.Collections.Generic.List<int> sizes, int n, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    var l = label[z * n + x];
                    if (l >= 0 && sizes[l] >= PondMinCells) return true;
                }
            return false;
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
            {
                SampleBiomes(xz, out _, out var heightmapShape);
                return Settle(xz, Ponds(xz, heightmapShape, Heightmap.Value.Sample(xz)));
            }
            SampleBiomes(xz, out _, out var shape);
            var h = ShapeHeight(xz, shape);
            if (Rivers.IsCreated)
                h = RiverSampling.Carve(RiverSampling.Query(ref Rivers.Value, RiverSettings, xz), RiverSettings, h);
            if (Landmarks.IsCreated)
                h = LandmarkSampling.Shape(ref Landmarks.Value, xz, h);
            return Settle(xz, Ponds(xz, shape, h));
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

                var lake = RiverSampling.Lake(ref Rivers.Value, xz, out var lakeSurface, out var lakeDepth);
                // Ground lying deeper under the lake's surface than the lake itself is deep isn't lake bed or shore: a
                // river's gorge has cut through the lake's rim there, and a flat sheet of lake over it would hang in the
                // air (F-22; a few dozen such points on the island).
                if (lakeSurface - g.Height > lakeDepth + 2f)
                    lake = 0f;
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
                        && bayLevel > water && bayLevel - g.Height <= BayDepthLimit(lakeCentre) && ConnectsBelow(xz, lakeCentre, bayLevel))
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
            // Biome ponds (a swamp's pools): water was decided above from the ground without them, so a pond is wet only
            // where it digs below the water level the biome already has (sea level in the lowlands).
            var dug = Ponds(xz, shape, g.Height, out var terrace);
            if (terrace > float.MinValue)
            {
                // W-24: a stepped pool holds its own level.
                if (terrace > water)
                {
                    water = terrace;
                    flow = float2.zero;
                    g.Rapids = 0f;
                }
                ruleDepth = math.max(ruleDepth, water - dug);
                g.Height = dug;
            }
            else if (dug < g.Height)
            {
                if (dug < World.SeaLevel && water < World.SeaLevel)
                {
                    water = World.SeaLevel;
                    flow = float2.zero;
                }
                ruleDepth = math.max(ruleDepth, math.max(water, World.SeaLevel) - dug);
                g.Height = dug;
            }
            else if (dug > g.Height)
                g.Height = dug;   // a stepped pool's bank, a little above the ground
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
        public float MouthCut(float2 xz, float h) => MouthCut(xz, h, out _);

        /// <summary>MouthCut, and the cave field's own distance at the point it samples (100 away from caves).</summary>
        public float MouthCut(float2 xz, float h, out float cave)
        {
            cave = 100f;
            if (!Caves.IsCreated || !CaveSampling.NearColumn(ref Caves.Value, xz))
                return 100f;
            // Into the ground along its normal, as the walls' field measures depth across the slope: mouths open in cliff
            // faces, where straight down is somewhere else entirely.
            var normal = SampleNormal(xz, 0.5f);
            cave = CaveDistance(new float3(xz.x, h, xz.y) - normal * MouthCutDepth);
            return math.min(CaveSampling.SmoothMin(MouthCutDepth - 0.1f, cave, CaveSampling.MouthBlend), 100f);
        }

        /// <summary>How far into the ground MouthCut samples the cave field.</summary>
        public const float MouthCutDepth = 0.35f;

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

        /// <summary>How deep a bay beside the lake at <paramref name="lakeCentre"/> may be: no deeper than the lake itself
        /// (plus 2 m), so a valley below a breached rim isn't filled to the lake's level (F-22).</summary>
        float BayDepthLimit(float2 lakeCentre)
        {
            RiverSampling.Lake(ref Rivers.Value, lakeCentre, out _, out var depth);
            return depth + 2f;
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
            if (Landmarks.IsCreated)
                LandmarkSampling.TroddenGround(ref Landmarks.Value, xz, context.EdgeNoise, ref result);
            return result;
        }

        public bool Equals(TerrainSource o) =>
            World.Equals(o.World) && HeightKind == o.HeightKind && Heightmap == o.Heightmap && Island == o.Island &&
            BiomeKind == o.BiomeKind && BiomeNoise.Equals(o.BiomeNoise) && BiomeMap == o.BiomeMap && Biomes == o.Biomes && BiomeCells == o.BiomeCells &&
            RiverSettings.Equals(o.RiverSettings) && Rivers == o.Rivers && Landmarks == o.Landmarks && Settlements == o.Settlements && Caves == o.Caves && BatFolk == o.BatFolk && Anchors == o.Anchors && PondMask == o.PondMask;
    }
}
