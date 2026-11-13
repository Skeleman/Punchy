using System;
using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>The kind of water a line lands in (A-100): what can bite there comes from <see cref="FishCatch.Table"/>.</summary>
    public enum FishWater : byte
    {
        None,
        /// <summary>The sea in its shallows (under about 6 m deep).</summary>
        SeaShore,
        /// <summary>The sea out where it is deep.</summary>
        SeaDeep,
        /// <summary>The sea by a settlement: piers and wharves.</summary>
        Harbour,
        Lake,
        /// <summary>A river where it runs slow: pools, bends.</summary>
        RiverPool,
        /// <summary>A river's white water.</summary>
        RiverRapids,
        /// <summary>Still water in a marsh or swamp biome.</summary>
        Swamp,
        CavePool,
        CaveStream,
        /// <summary>A small still pool on dry land (W-143's pools, a spring): tiny fish or none.</summary>
        SmallPool,
    }

    /// <summary>Biome groups a catch can be limited to. None = any biome.</summary>
    [Flags]
    public enum FishBiome : ushort
    {
        None = 0,
        Temperate = 1 << 0,
        Cold = 1 << 1,
        Warm = 1 << 2,
        Marsh = 1 << 3,
        Poison = 1 << 4,
        Sparkle = 1 << 5,
        Fungal = 1 << 6,
        Ember = 1 << 7,
        Strange = 1 << 8,
    }

    /// <summary>When a catch bites. Twilight is dawn (5-8) and dusk (17-20).</summary>
    public enum FishHours : byte { Any, Day, Night, Twilight }

    public enum FishRarity : byte { Common, Uncommon, Rare, Legendary }

    /// <summary>What a roll gives: the bite's wait, the catch's name and size, how hard it pulls, and how it looks.</summary>
    public struct FishCatchResult
    {
        /// <summary>Seconds from the bobber settling to the bite.</summary>
        public float Wait;
        public string Name;
        /// <summary>Length in cm (a junk item's size).</summary>
        public byte Size;
        /// <summary>0..1: how hard it fights.</summary>
        public float Strength;
        /// <summary>The creature species whose body it wears (null for junk), and which of its coats (-1: any).</summary>
        public string Look;
        public int Coat;
        public FishRarity Rarity;
        public bool Junk;
        public FishWater Water;
    }

    /// <summary>One row of the catch table.</summary>
    public readonly struct FishEntry
    {
        public readonly string Name;
        public readonly FishWater[] Waters;
        public readonly FishBiome Biomes;
        public readonly FishHours Hours;
        public readonly FishRarity Rarity;
        public readonly byte MinSize, MaxSize;
        public readonly float Strength;
        public readonly string Look;
        public readonly int Coat;
        public readonly bool Junk;
        /// <summary>Overrides the rarity's weight when above 0 (junk and "nothing bites").</summary>
        public readonly float Weight;

        public FishEntry(string name, FishWater[] waters, FishRarity rarity, byte minSize, byte maxSize, float strength, string look,
            int coat = -1, FishBiome biomes = FishBiome.None, FishHours hours = FishHours.Any, bool junk = false, float weight = 0f)
        {
            Name = name;
            Waters = waters;
            Rarity = rarity;
            MinSize = minSize;
            MaxSize = maxSize;
            Strength = strength;
            Look = look;
            Coat = coat;
            Biomes = biomes;
            Hours = hours;
            Junk = junk;
            Weight = weight;
        }

        /// <summary>No bite at all: a roll that lands here returns false.</summary>
        public bool Nothing => Name == null;

        public float Chance => Weight > 0f ? Weight : FishCatch.RarityWeight(Rarity);

        public bool LivesIn(FishWater water)
        {
            foreach (var w in Waters)
                if (w == water)
                    return true;
            return false;
        }
    }

    /// <summary>
    /// A-100: the catch tables behind fishing (P-33). Which fish live in which water, by biome and hour, how big and how
    /// rare; a small tail of junk and treasure; and the roll a cast makes. Pure data and maths: no scene, no state, so a
    /// host, a client and a solo game roll the same thing from the same seed. The player lane's IFishSource wraps
    /// <see cref="Roll(in TerrainSource, in GroundSample, float3, uint, float, out FishCatchResult)"/>.
    /// </summary>
    public static class FishCatch
    {
        const FishWater SeaShore = FishWater.SeaShore, SeaDeep = FishWater.SeaDeep, Harbour = FishWater.Harbour, Lake = FishWater.Lake,
            RiverPool = FishWater.RiverPool, Rapids = FishWater.RiverRapids, Swamp = FishWater.Swamp, CavePool = FishWater.CavePool,
            CaveStream = FishWater.CaveStream, SmallPool = FishWater.SmallPool;
        const FishRarity Common = FishRarity.Common, Uncommon = FishRarity.Uncommon, Rare = FishRarity.Rare, Legendary = FishRarity.Legendary;

        static FishWater[] In(params FishWater[] w) => w;

        /// <summary>Every water kind that has a table, in report order.</summary>
        public static readonly FishWater[] Waters = { SeaShore, SeaDeep, Harbour, Lake, RiverPool, Rapids, Swamp, CavePool, CaveStream, SmallPool };

        /// <summary>
        /// The catch table. Looks reuse the swimming species (Trout, Carp, Koi, LakeSturgeon, Mackerel, SilverFish, GlowEel)
        /// at the catch's size; coats are the species' coat index. Sizes are cm.
        /// </summary>
        public static readonly FishEntry[] Table =
        {
            // Sea.
            new FishEntry("Mackerel", In(SeaShore, SeaDeep, Harbour), Common, 25, 40, 0.55f, "Mackerel", hours: FishHours.Day),
            new FishEntry("Herring", In(SeaShore, Harbour), Common, 18, 30, 0.35f, "SilverFish"),
            new FishEntry("Sea bream", In(SeaShore, Harbour), Common, 20, 40, 0.5f, "SilverFish"),
            new FishEntry("Flounder", In(SeaShore, Harbour), Uncommon, 20, 45, 0.45f, "Carp", coat: 1),
            new FishEntry("Cod", In(SeaDeep, Harbour), Common, 40, 90, 0.75f, "Trout", coat: 1),
            new FishEntry("Tuna", In(SeaDeep), Uncommon, 80, 200, 0.95f, "Mackerel"),
            new FishEntry("Conger eel", In(Harbour, SeaDeep), Uncommon, 60, 150, 0.8f, "GlowEel", hours: FishHours.Night),
            new FishEntry("Moonfish", In(SeaDeep), Rare, 40, 70, 0.6f, "SilverFish", hours: FishHours.Night),
            new FishEntry("Ice cod", In(SeaShore, SeaDeep), Common, 30, 60, 0.6f, "Trout", coat: 0, biomes: FishBiome.Cold),
            // Lakes.
            new FishEntry("Carp", In(Lake, RiverPool, Swamp), Common, 30, 70, 0.7f, "Carp", coat: 0),
            new FishEntry("Perch", In(Lake, RiverPool), Common, 15, 30, 0.4f, "Trout", coat: 1),
            new FishEntry("Bream", In(Lake), Common, 20, 40, 0.4f, "Carp", coat: 1),
            new FishEntry("Pike", In(Lake, RiverPool), Uncommon, 40, 110, 0.9f, "LakeSturgeon", coat: 0, hours: FishHours.Twilight),
            new FishEntry("Lake sturgeon", In(Lake), Rare, 90, 220, 1f, "LakeSturgeon", coat: 1),
            new FishEntry("Arctic char", In(Lake, Rapids), Common, 25, 50, 0.6f, "Trout", coat: 0, biomes: FishBiome.Cold),
            new FishEntry("Koi", In(Lake, SmallPool), Uncommon, 25, 60, 0.5f, "Koi", biomes: FishBiome.Sparkle),
            new FishEntry("Golden koi", In(Lake), Legendary, 50, 80, 0.6f, "Koi", coat: 1, biomes: FishBiome.Sparkle),
            new FishEntry("Sunfish", In(Lake, RiverPool), Common, 10, 20, 0.3f, "Koi", coat: 1, biomes: FishBiome.Warm | FishBiome.Temperate),
            new FishEntry("Spore carp", In(Lake, Swamp), Uncommon, 30, 60, 0.6f, "Carp", coat: 1, biomes: FishBiome.Fungal),
            new FishEntry("Ember trout", In(Lake, RiverPool, Rapids), Uncommon, 20, 45, 0.65f, "Trout", coat: 0, biomes: FishBiome.Ember),
            // Rivers.
            new FishEntry("Trout", In(RiverPool, Rapids), Common, 20, 45, 0.6f, "Trout", coat: 0),
            new FishEntry("Chub", In(RiverPool), Common, 20, 45, 0.45f, "Carp", coat: 0),
            new FishEntry("Salmon", In(Rapids), Uncommon, 50, 90, 0.9f, "Trout", coat: 1),
            new FishEntry("Grayling", In(Rapids), Common, 25, 40, 0.5f, "Trout", coat: 0, biomes: FishBiome.Cold | FishBiome.Temperate),
            new FishEntry("Catfish", In(RiverPool, Lake, Swamp), Uncommon, 40, 120, 0.85f, "LakeSturgeon", coat: 1, hours: FishHours.Night),
            // Swamps.
            new FishEntry("Mudfish", In(Swamp), Common, 10, 22, 0.25f, "Trout", coat: 1),
            new FishEntry("Bowfin", In(Swamp), Common, 35, 70, 0.7f, "Carp", coat: 0),
            new FishEntry("Glow eel", In(Swamp, CavePool), Uncommon, 40, 90, 0.65f, "GlowEel", hours: FishHours.Night),
            new FishEntry("Bog eel", In(Swamp), Common, 40, 80, 0.6f, "GlowEel", biomes: FishBiome.Poison),
            // Caves.
            new FishEntry("Blind cavefish", In(CavePool, CaveStream), Common, 8, 18, 0.3f, "SilverFish"),
            new FishEntry("Pale loach", In(CaveStream), Common, 6, 14, 0.2f, "Trout", coat: 1),
            new FishEntry("Ghost sturgeon", In(CavePool), Rare, 80, 150, 0.9f, "LakeSturgeon", coat: 2),
            // Small pools: tiny fish, or nothing.
            new FishEntry("Minnow", In(SmallPool, RiverPool), Common, 4, 8, 0.1f, "Trout", coat: 0),
            new FishEntry("Stickleback", In(SmallPool), Common, 4, 7, 0.1f, "SilverFish"),
            new FishEntry(null, In(SmallPool), Common, 0, 0, 0f, null, weight: 14f),
            // Junk and treasure.
            new FishEntry("Old boot", In(SeaShore, Harbour, Lake, RiverPool, Swamp), Common, 25, 32, 0.2f, null, junk: true, weight: 1.2f),
            new FishEntry("Driftwood", In(SeaShore, Lake, RiverPool, Rapids, Swamp), Common, 30, 120, 0.35f, null, junk: true, weight: 1.6f),
            new FishEntry("Rope tangle", In(Harbour), Common, 20, 60, 0.25f, null, junk: true, weight: 1.2f),
            new FishEntry("Lost ring", In(SeaShore, SeaDeep, Harbour, Lake, RiverPool, Swamp, CavePool), Rare, 2, 3, 0.1f, null, junk: true, weight: 0.25f),
            new FishEntry("Glass float", In(SeaShore, SeaDeep, Harbour), Rare, 10, 20, 0.15f, null, junk: true, weight: 0.3f),
        };

        public static float RarityWeight(FishRarity r) => r switch
        {
            FishRarity.Common => 10f,
            FishRarity.Uncommon => 4f,
            FishRarity.Rare => 1f,
            _ => 0.25f,
        };

        /// <summary>How deep the sea has to be to count as deep, m.</summary>
        public const float DeepSea = 6f;
        /// <summary>How far past a settlement's edge its harbour water reaches, m.</summary>
        public const float HarbourReach = 30f;

        /// <summary>The kind of water at a sampled point. <paramref name="water"/> is SampleGround at <paramref name="at"/> (float3 for caves).</summary>
        public static FishWater Classify(in TerrainSource terrain, in GroundSample water, float3 at)
        {
            if (!water.HasWater || !(water.WaterSurface > water.Height))
                return FishWater.None;
            var flowing = water.River > 0.5f || math.lengthsq(water.Flow) > 0.25f;
            if (water.CaveWater)
                return flowing || water.Rapids > 0.2f ? FishWater.CaveStream : FishWater.CavePool;
            var depth = water.WaterSurface - water.Height;
            if (math.abs(water.WaterSurface - terrain.World.SeaLevel) < 0.05f && water.River < 0.5f)
            {
                if (NearSettlement(terrain, at.xz))
                    return FishWater.Harbour;
                return depth > DeepSea ? FishWater.SeaDeep : FishWater.SeaShore;
            }
            if (flowing)
                return water.Rapids > 0.35f ? FishWater.RiverRapids : FishWater.RiverPool;
            if ((BiomeAt(terrain, water) & (FishBiome.Marsh | FishBiome.Poison)) != 0)
                return FishWater.Swamp;
            if (depth < 1.5f && IsSmallPool(terrain, at.xz, water.WaterSurface))
                return FishWater.SmallPool;
            return FishWater.Lake;
        }

        /// <summary>Still water with dry ground most of the way round within 8 m: a pool, not a lake.</summary>
        static bool IsSmallPool(in TerrainSource terrain, float2 xz, float surface)
        {
            var dry = 0;
            for (int k = 0; k < 8; k++)
            {
                var a = k * (math.PI / 4f);
                var g = terrain.SampleGround(xz + new float2(math.sin(a), math.cos(a)) * 8f);
                if (!g.HasWater || math.abs(g.WaterSurface - surface) > 0.5f || g.WaterSurface <= g.Height)
                    dry++;
            }
            return dry >= 6;
        }

        static bool NearSettlement(in TerrainSource terrain, float2 xz)
        {
            if (!terrain.Settlements.IsCreated)
                return false;
            ref var set = ref terrain.Settlements.Value.Settlements;
            for (int i = 0; i < set.Length; i++)
            {
                var reach = set[i].Radius + HarbourReach;
                if (math.distancesq(set[i].Centre, xz) < reach * reach)
                    return true;
            }
            return false;
        }

        /// <summary>The biome group of the water's dominant biome.</summary>
        public static FishBiome BiomeAt(in TerrainSource terrain, in GroundSample water)
        {
            if (!terrain.Biomes.IsCreated)
                return FishBiome.Temperate;
            var id = water.Biomes.Dominant();
            ref var biomes = ref terrain.Biomes.Value.Biomes;
            if (id < 0 || id >= biomes.Length)
                return FishBiome.Temperate;
            return ForBiome(biomes[id].NameHash);
        }

        /// <summary>A biome's group by its NameHash (FNV-1a of the asset name, as AmbientSounds.ForBiome).</summary>
        public static FishBiome ForBiome(uint nameHash) => nameHash switch
        {
            748784547u or 3623817682u => FishBiome.Cold,                       // Mountains, Glacier
            315160912u or 3368052845u or 659108819u or 3701048830u => FishBiome.Warm, // Dunes, Sonoran, MesaCanyonlands, SaltFlat
            3183111906u => FishBiome.Marsh,                                     // FoggyMarsh
            232645075u => FishBiome.Poison,                                     // PoisonSwamp
            1203221319u => FishBiome.Sparkle,                                   // SparklingForest
            3657515439u => FishBiome.Fungal,                                    // FungalForest
            2483322509u or 441784083u => FishBiome.Ember,                       // EmberForest, Ashlands
            2317849686u or 3103161546u => FishBiome.Strange,                    // ElectricWastelands, GlassDesert
            _ => FishBiome.Temperate,                                           // Grassland, Ocean and the rest
        };

        public static bool InHours(FishHours hours, float hour)
        {
            hour = (hour % 24f + 24f) % 24f;
            var day = hour >= 6f && hour < 19f;
            return hours switch
            {
                FishHours.Day => day,
                FishHours.Night => !day,
                FishHours.Twilight => hour >= 5f && hour < 8f || hour >= 17f && hour < 20f,
                _ => true,
            };
        }

        /// <summary>
        /// One cast: classifies the water, then rolls the table. False when nothing bites (a small pool's empty roll,
        /// or no water). Same seed, same place, same hour: same catch, on host, client or solo.
        /// </summary>
        public static bool Roll(in TerrainSource terrain, in GroundSample water, float3 at, uint seed, float hour, out FishCatchResult result)
        {
            var kind = Classify(terrain, water, at);
            return Roll(kind, BiomeAt(terrain, water), hour, seed, out result);
        }

        /// <summary>The table roll for a known water kind, biome group and hour.</summary>
        public static bool Roll(FishWater water, FishBiome biome, float hour, uint seed, out FishCatchResult result)
        {
            result = default;
            result.Water = water;
            if (water == FishWater.None)
                return false;
            var total = 0f;
            foreach (var e in Table)
                if (Fits(e, water, biome, hour))
                    total += e.Chance;
            if (total <= 0f)
                return false;
            var rng = new Unity.Mathematics.Random(Scramble(seed) | 1u);
            var pick = rng.NextFloat(total);
            var chosen = -1;
            for (int i = 0; i < Table.Length && chosen < 0; i++)
            {
                if (!Fits(Table[i], water, biome, hour))
                    continue;
                pick -= Table[i].Chance;
                if (pick < 0f)
                    chosen = i;
            }
            if (chosen < 0)
                for (int i = Table.Length - 1; i >= 0 && chosen < 0; i--)
                    if (Fits(Table[i], water, biome, hour))
                        chosen = i;
            var entry = Table[chosen];
            if (entry.Nothing)
                return false;
            // Size leans small: most catches are the lower half of the range, the odd one near the top.
            var t = math.pow(rng.NextFloat(), 1.6f);
            var size = math.lerp(entry.MinSize, entry.MaxSize, t);
            // Fish feed at dawn and dusk (shorter waits); small pools and big rare fish make you wait longer.
            var wait = rng.NextFloat(3f, 8f);
            if (InHours(FishHours.Twilight, hour))
                wait *= 0.75f;
            if (water == FishWater.SmallPool)
                wait *= 1.6f;
            wait += (int)entry.Rarity * 1.5f;
            result = new FishCatchResult
            {
                Wait = wait,
                Name = entry.Name,
                Size = (byte)math.clamp((int)math.round(size), 1, 255),
                // A big one of its kind pulls harder than a small one.
                Strength = math.saturate(entry.Strength * (entry.Junk ? 1f : math.lerp(0.8f, 1.15f, t))),
                Look = entry.Look,
                Coat = entry.Coat,
                Rarity = entry.Rarity,
                Junk = entry.Junk,
                Water = water,
            };
            return true;
        }

        static bool Fits(in FishEntry e, FishWater water, FishBiome biome, float hour) =>
            e.LivesIn(water) && (e.Biomes == FishBiome.None || (e.Biomes & biome) != 0) && InHours(e.Hours, hour);

        /// <summary>A seed's bits mixed (math.hash of a single uint barely avalanches).</summary>
        public static uint Scramble(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        /// <summary>The game clock's hour (DayNightCycle), or noon when there is none. Main thread only.</summary>
        public static float Hour()
        {
            var cycle = UnityEngine.Object.FindAnyObjectByType<Punchy.Weather.DayNightCycle>();
            return cycle != null ? cycle.TimeOfDay : 12f;
        }
    }
}
