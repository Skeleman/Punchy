using System.Collections.Generic;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// One baked herd or flock on the ground (A-108 row 2): where it lives, how many there are, and the spots it walks
    /// between. Where the herd is at any moment is worked out from this and the shared clock (<see cref="HerdClock"/>), so
    /// every machine draws it in the same place without anything being sent.
    /// </summary>
    public struct HerdRecord
    {
        /// <summary>The middle of its range (also its bed, spot 0).</summary>
        public float2 Home;
        /// <summary>How far from Home its spots lie (m).</summary>
        public float Range;
        /// <summary>Its own seed: member offsets, leg lengths and which spot each leg goes to.</summary>
        public uint Seed;
        /// <summary>Which biome (index into the palette's Biomes) and which of that biome's Fauna entries it is.</summary>
        public byte Biome, Slot;
        /// <summary>How many animals, and how many spots (spot 0 the bed, the rest grazing spots).</summary>
        public byte Count, Spots;
        /// <summary>Its first spot in <see cref="HerdSetBlob.Spots"/>.</summary>
        public int FirstSpot;
    }

    /// <summary>
    /// Every herd on the island (A-108 row 2), baked last: nothing in the terrain reads it, so the ground, landmarks and
    /// their hashes are the same with or without it.
    /// </summary>
    public struct HerdSetBlob
    {
        public BlobArray<HerdRecord> Herds;
        /// <summary>The herds' spots, each herd's together (HerdRecord.FirstSpot, Spots); every pair of one herd's spots has a
        /// clear straight walk between them (dry land, out of towns, off cave cuts and cliffs).</summary>
        public BlobArray<float2> Spots;
    }

    /// <summary>
    /// Bakes the herds (A-108 row 2) from the biomes' fauna lists: the island is walked in 256 m cells, each with four
    /// jittered candidate points; at each point the dominant biome's herding species (<see cref="Herds"/>) roll for a herd
    /// by their density there (animals per hectare over the mean group size, so the herds carry the same head count the
    /// spawner gave near a player). A herd needs a valid home and at least one grazing spot it can walk to. Each cell has its
    /// own stream, so the cells bake in parallel and a change in one leaves the others alone.
    /// </summary>
    public static class HerdBaking
    {
        public const float Cell = 256f;
        const int Candidates = 4;
        const int MaxSpots = 5;
        const int MaxCount = 14;
        /// <summary>A gameplay rule, not a look: herds keep this far beyond a settlement's edge (m).</summary>
        const float TownMargin = 40f;   // gameplay: herds stay out of towns
        const float MaxSlope = 0.6f;

        /// <summary>The stage's own stream: the world seed with "herds" mixed in.</summary>
        static uint StreamSeed(uint seed) => BiomeSampling.Scramble(seed ^ 0x4E2D5A11u);

        public static string LastLog = "";

        /// <summary>
        /// Whether a species lives in baked herds (the supervisor's rule for A-108): a land animal that comes in groups of 3
        /// or more, or one big enough to see at 300 m (<see cref="Settings.DetailPolicy.HerdSpeciesPixels"/> at the
        /// reference view, the same on every machine). Not burrowers, cave dwellers, folk or anything that hides, crawls,
        /// tunnels, roosts, soars or comes to lights.
        /// </summary>
        public static bool Herds(CreatureSpecies species)
        {
            if (species == null)
                return false;
            var b = species.Body;
            if (b.Habitat != Habitat.Land || b.Breaches || b.DrawnToLight || b.CaveDweller || b.Burrows || b.Child || species.Child ||
                b.Upright || b.Crawls || b.Tunnels || b.Rooted || b.Disguised || b.Soars || b.Roosts || b.Haunts || b.LavaDweller ||
                b.SeenFrom > 0f)
                return false;
            if (b.GroupSize.x >= 3)
                return true;
            return Settings.DetailPolicy.ScreenSize(FarSize(species), 300f, Settings.DetailPolicy.PixelsPerMetreAt(Settings.DetailPolicy.ReferenceFov))
                   >= Settings.DetailPolicy.HerdSpeciesPixels;
        }

        /// <summary>A typical one's torso length (m: the body's own length, a variant's scale and the middle of its Size range).</summary>
        public static float FarSize(CreatureSpecies species)
        {
            var b = species.Body;
            var scale = species != b ? species.VariantScale : 1f;
            return b.BodyLength * scale * (b.Size.x + b.Size.y) * 0.5f;
        }

        /// <summary>One fauna entry herds come from, named (Biome, Slot) as <see cref="HerdRecord"/> names it.</summary>
        public struct Source
        {
            public int Biome, Slot;
            public CreatureSpecies Species;
            /// <summary>Animals per hectare.</summary>
            public float Density;
            /// <summary>A Model Lab species' Home (#412), not one of the biome's own entries.</summary>
            public bool FromHome;
        }

        /// <summary>
        /// Every herding fauna entry (<see cref="Herds"/>, density above 0): each biome's own entries, then the Model Lab
        /// species' Homes (#412-414) in <see cref="LabSpeciesList"/> order, numbered on after the biome's own entries (the
        /// slots FaunaDirector.MergeHomes gives them). The bake and FaunaDirector's herd kinds both read this, so a Lab
        /// species joins the herds the way a built-in does (one population, A-110). The Homes are read from the species,
        /// not from what MergeHomes has added, so the bake is the same whether or not the fauna build has run yet: the
        /// herds bake with the island on every machine, and the world signature already names the Lab species (#442).
        /// </summary>
        public static List<Source> Sources(IReadOnlyList<BiomeDefinition> biomes)
        {
            var list = new List<Source>();
            for (int b = 0; b < biomes.Count && b < 256; b++)
            {
                var biome = biomes[b];
                if (biome == null || biome.Fauna == null)
                    continue;
                // (MergeHomes appends after the biome's own entries, so those keep their list index as their slot.)
                int own = 0;
                for (; own < biome.Fauna.Count && !(biome.Fauna[own] != null && biome.Fauna[own].FromHomes); own++)
                {
                    var f = biome.Fauna[own];
                    if (own < 255 && f != null && f.Species != null && f.Density > 0f && Herds(f.Species))
                        list.Add(new Source { Biome = b, Slot = own, Species = f.Species, Density = f.Density });
                }
                var next = own;
                foreach (var s in LabSpeciesList.All())
                {
                    if (s == null || s.Homes == null)
                        continue;
                    foreach (var home in s.Homes)
                    {
                        if (home == null || home.Biome != biome.name)
                            continue;
                        if (next < 255 && home.Density > 0f && Herds(s))
                            list.Add(new Source { Biome = b, Slot = next, Species = s, Density = home.Density, FromHome = true });
                        next++;
                    }
                }
            }
            return list;
        }

        /// <summary>What the bake needs of one herding fauna entry, read on the main thread.</summary>
        struct Slot
        {
            public byte Biome, Slot_;
            public float GroupsPerCandidate;
            public int2 Group;
            public bool NearWater;
            public float Spread;
        }

        struct Built
        {
            public HerdRecord Record;
            public float2[] Spots;
        }

        public static BlobAssetReference<HerdSetBlob> Build(in TerrainSource source, IReadOnlyList<BiomeDefinition> biomes, Allocator allocator)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Each biome's herding entries (main thread: the species are assets).
            var perBiome = new Slot[biomes.Count][];
            var candidateHectares = Cell * Cell / 10000f / Candidates;
            var sources = Sources(biomes);
            var lists = new List<Slot>[biomes.Count];
            var names = new Dictionary<int, string>();
            foreach (var f in sources)
            {
                var body = f.Species.Body;
                var group = new int2(math.max(1, body.GroupSize.x), math.max(math.max(1, body.GroupSize.x), body.GroupSize.y));
                group = math.min(group, MaxCount);
                (lists[f.Biome] ??= new List<Slot>()).Add(new Slot
                {
                    Biome = (byte)f.Biome, Slot_ = (byte)f.Slot,
                    GroupsPerCandidate = f.Density * candidateHectares / ((group.x + group.y) * 0.5f),
                    Group = group,
                    NearWater = body.NearWater,
                    Spread = FarSize(f.Species)
                });
                names[f.Biome * 256 + f.Slot] = f.Species.name;
            }
            for (int i = 0; i < biomes.Count; i++)
                perBiome[i] = lists[i] != null ? lists[i].ToArray() : System.Array.Empty<Slot>();
            int herdingSlots = sources.Count;

            var half = source.World.WorldSize * 0.55f;
            var cells = (int)math.ceil(half * 2f / Cell);
            var stream = StreamSeed(source.World.Seed);
            var perCell = new List<Built>[cells * cells];
            var src = source;
            System.Threading.Tasks.Parallel.For(0, cells * cells, index =>
            {
                var cell = new int2(index % cells, index / cells);
                var origin = -half + (float2)cell * Cell;
                var own = new List<Built>();
                var rng = new Random(BiomeSampling.Scramble(stream ^ BiomeSampling.Scramble((uint)cell.x * 0x85EBCA6Bu ^ (uint)cell.y * 0xC2B2AE35u)) | 1u);
                for (int c = 0; c < Candidates; c++)
                {
                    // (Every draw is made whatever the ground, so one candidate's outcome never moves the next one's.)
                    var at = origin + new float2((c & 1) + rng.NextFloat(), (c >> 1) + rng.NextFloat()) * (Cell * 0.5f);
                    var candidateSeed = rng.NextUInt();
                    var ground = src.SampleGround(at);
                    var b = ground.Biomes.Dominant();
                    if (b < 0 || b >= perBiome.Length || perBiome[b].Length == 0 || !Open(src, at, ground))
                        continue;
                    var crng = new Random(candidateSeed | 1u);
                    foreach (var slot in perBiome[b])
                    {
                        var roll = crng.NextFloat();
                        var herdSeed = crng.NextUInt();
                        // At most one herd of a kind per candidate point, by its expected number there.
                        if (roll >= slot.GroupsPerCandidate)
                            continue;
                        if (slot.NearWater && ground.WaterDepth < -4f)
                            continue;
                        if (TryHerd(src, slot, at, herdSeed, out var built))
                            own.Add(built);
                    }
                }
                perCell[index] = own;
            });

            var records = new List<HerdRecord>();
            var spots = new List<float2>();
            var bySpecies = new Dictionary<int, int>();
            foreach (var list in perCell)
                foreach (var h in list)
                {
                    var r = h.Record;
                    r.FirstSpot = spots.Count;
                    r.Spots = (byte)h.Spots.Length;
                    spots.AddRange(h.Spots);
                    records.Add(r);
                    var key = r.Biome * 256 + r.Slot;
                    bySpecies[key] = bySpecies.TryGetValue(key, out var n) ? n + 1 : 1;
                }

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<HerdSetBlob>();
            var outHerds = builder.Allocate(ref root.Herds, records.Count);
            for (int i = 0; i < records.Count; i++)
                outHerds[i] = records[i];
            var outSpots = builder.Allocate(ref root.Spots, spots.Count);
            for (int i = 0; i < spots.Count; i++)
                outSpots[i] = spots[i];
            var blob = builder.CreateBlobAssetReference<HerdSetBlob>(allocator);
            builder.Dispose();

            var animals = 0;
            foreach (var r in records)
                animals += r.Count;
            var top = new List<KeyValuePair<int, int>>(bySpecies);
            top.Sort((x, y) => y.Value != x.Value ? y.Value.CompareTo(x.Value) : x.Key.CompareTo(y.Key));
            var most = new System.Text.StringBuilder();
            for (int i = 0; i < top.Count && i < 6; i++)
                most.Append(i == 0 ? "" : ", ").Append(names[top[i].Key]).Append(' ').Append(top[i].Value);
            var lab = new System.Text.StringBuilder();
            foreach (var f in sources)
                if (f.FromHome)
                    lab.Append(lab.Length == 0 ? "; Lab: " : ", ").Append(f.Species.name).Append(' ')
                       .Append(bySpecies.TryGetValue(f.Biome * 256 + f.Slot, out var c) ? c : 0);
            LastLog = $"[herds] {records.Count} herds, {animals} animals, {spots.Count} spots from {herdingSlots} herding fauna entries " +
                      $"({cells}x{cells} cells, {watch.ElapsedMilliseconds} ms); most: {most}{lab}";
            return blob;
        }

        /// <summary>A herd at a candidate point: its home, range and spots, or false when there's no room for one.</summary>
        static bool TryHerd(in TerrainSource source, in Slot slot, float2 at, uint seed, out Built built)
        {
            built = default;
            var rng = new Random(seed | 1u);
            var count = rng.NextInt(slot.Group.x, slot.Group.y + 1);
            var home = at + rng.NextFloat2Direction() * rng.NextFloat(0f, Cell * 0.2f);
            // (A big animal roams wider; a herd of many a little wider again.)
            var range = math.clamp(slot.Spread * 30f, 30f, 70f) * rng.NextFloat(0.8f, 1.25f) + math.sqrt(count) * 4f;
            var tries = new float2[12];
            for (int i = 0; i < tries.Length; i++)
                tries[i] = home + rng.NextFloat2Direction() * range * rng.NextFloat(0.35f, 1f);
            var ownSeed = rng.NextUInt();
            if (!Open(source, home, source.SampleGround(home)))
                return false;
            var spots = new List<float2> { home };
            foreach (var p in tries)
            {
                if (spots.Count >= MaxSpots)
                    break;
                if (!Open(source, p, source.SampleGround(p)))
                    continue;
                var clear = true;
                foreach (var q in spots)
                    if (!ClearWalk(source, q, p))
                    {
                        clear = false;
                        break;
                    }
                if (clear)
                    spots.Add(p);
            }
            if (spots.Count < 2)
                return false;
            built = new Built
            {
                Record = new HerdRecord { Home = home, Range = range, Seed = ownSeed, Biome = slot.Biome, Slot = slot.Slot_, Count = (byte)count },
                Spots = spots.ToArray()
            };
            return true;
        }

        /// <summary>Dry, gentle, uncarved ground out of town, where a herd can stand.</summary>
        static bool Open(in TerrainSource source, float2 at, in GroundSample g)
        {
            if (g.WaterDepth > -0.5f || g.HasWater && g.WaterSurface > g.Height - 0.3f || g.Height < source.World.SeaLevel + 0.5f)
                return false;
            if (g.Height - CreatureMotion.Ground(source, at, g.Height) > 0.2f)
                return false;
            var dx = source.SampleHeight(at + new float2(1.5f, 0f)) - source.SampleHeight(at - new float2(1.5f, 0f));
            var dz = source.SampleHeight(at + new float2(0f, 1.5f)) - source.SampleHeight(at - new float2(0f, 1.5f));
            if (math.length(new float2(dx, dz)) / 3f > MaxSlope)
                return false;
            return !InTown(source, at);
        }

        /// <summary>A straight walk between two spots over open ground (every 8 m).</summary>
        static bool ClearWalk(in TerrainSource source, float2 a, float2 b)
        {
            var steps = (int)math.ceil(math.distance(a, b) / 8f);
            for (int i = 1; i < steps; i++)
            {
                var p = math.lerp(a, b, i / (float)steps);
                if (!Open(source, p, source.SampleGround(p)))
                    return false;
            }
            return true;
        }

        static bool InTown(in TerrainSource source, float2 at)
        {
            if (!source.Settlements.IsCreated)
                return false;
            ref var towns = ref source.Settlements.Value.Settlements;
            for (int i = 0; i < towns.Length; i++)
                if (math.distance(towns[i].Centre, at) < towns[i].Radius + TownMargin)
                    return true;
            return false;
        }
    }
}
