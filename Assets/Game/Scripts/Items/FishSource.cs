using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Items
{
    /// <summary>What takes the bait (P-33): how long after the bobber settles it bites (s), what it is and how long (cm),
    /// and how hard it pulls on the line (0-1).</summary>
    public struct FishBite
    {
        public float Wait;
        public string Name;
        public byte Size;
        public float Strength;
    }

    /// <summary>
    /// Where a fishing line's catch comes from (P-33 row 2): the one place the player's side (Fishing) meets the fish side
    /// (A-100's catch tables, row 3). Given the water the bobber is on and a seed, it says what bites and when, or false
    /// for nothing at all in that water.
    /// </summary>
    public interface IFishSource
    {
        bool Next(in TerrainSource terrain, in GroundSample water, float3 at, uint seed, out FishBite bite);
    }

    /// <summary>
    /// The stand-in catch table until A-100 lands (P-33 row 2): two or three fish by kind of water (sea, river, lake, cave),
    /// a bite 3-8 s after the bobber settles, a size from the fish's range, all from the seed.
    /// </summary>
    public sealed class StubFishSource : IFishSource
    {
        struct Kind
        {
            public string Name;
            public byte Min, Max;
            public float Strength;
        }

        static readonly Kind[] Sea = { new Kind { Name = "mackerel", Min = 25, Max = 40, Strength = 0.6f }, new Kind { Name = "cod", Min = 40, Max = 80, Strength = 0.8f } };
        static readonly Kind[] River = { new Kind { Name = "trout", Min = 20, Max = 45, Strength = 0.6f }, new Kind { Name = "perch", Min = 15, Max = 30, Strength = 0.4f } };
        static readonly Kind[] Lake = { new Kind { Name = "carp", Min = 30, Max = 70, Strength = 0.7f }, new Kind { Name = "pike", Min = 40, Max = 90, Strength = 0.9f }, new Kind { Name = "bream", Min = 20, Max = 40, Strength = 0.4f } };
        static readonly Kind[] Cave = { new Kind { Name = "blind cavefish", Min = 8, Max = 18, Strength = 0.3f } };

        public bool Next(in TerrainSource terrain, in GroundSample water, float3 at, uint seed, out FishBite bite)
        {
            var kinds = water.CaveWater ? Cave : math.abs(water.WaterSurface - terrain.World.SeaLevel) < 0.05f ? Sea : water.River > 0.5f ? River : Lake;
            var rng = new Random(math.max(1u, seed));
            var k = kinds[rng.NextInt(kinds.Length)];
            bite = new FishBite { Wait = rng.NextFloat(3f, 8f), Name = k.Name, Size = (byte)rng.NextInt(k.Min, k.Max + 1), Strength = k.Strength };
            return true;
        }
    }
}
