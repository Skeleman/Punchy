using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// The shared clock reading a herd works from (A-108 row 2): seconds counting whole days, how long a game hour is,
    /// and the hour now. A paused clock (screenshots) runs on local time and keeps its hour.
    /// </summary>
    public struct HerdTime
    {
        public double T;
        public float SecondsPerHour;
        public float Hour;
        public bool Paused;

        /// <summary>From the day/night cycle (null: noon on local time), as CreatureRenderSystem's sky clock reads it.</summary>
        public static HerdTime From(Punchy.Weather.DayNightCycle cycle, double local)
        {
            if (cycle == null)
                return new HerdTime { T = local, SecondsPerHour = 60f, Hour = 12f, Paused = true };
            var perHour = cycle.DayLengthMinutes * 2.5f;
            return new HerdTime
            {
                T = cycle.Paused ? local : (cycle.Day * 24.0 + cycle.TimeOfDay) * perHour,
                SecondsPerHour = perHour,
                Hour = cycle.TimeOfDay,
                Paused = cycle.Paused
            };
        }

        /// <summary>The hour at a moment on this clock (the hour now while paused).</summary>
        public float HourAt(double t) => Paused ? Hour : (float)((t / SecondsPerHour % 24.0 + 24.0) % 24.0);
    }

    /// <summary>What the clock needs of a herd's species, read from its blob.</summary>
    public struct HerdShape
    {
        public float BodyLength, WalkSpeed, SizeMin, SizeMax;
        public ActiveTime Active;

        public float MeanScale => (SizeMin + SizeMax) * 0.5f;

        public static HerdShape Of(ref CreatureBlob b) => new HerdShape
        {
            BodyLength = b.BodyLength,
            WalkSpeed = b.WalkSpeed,
            SizeMin = b.Size.x,
            SizeMax = math.max(b.Size.x, b.Size.y),
            Active = b.Active
        };
    }

    /// <summary>Where a herd is now: its middle, which way it's going, and whether it's on the move or bedded down.</summary>
    public struct HerdPlace
    {
        public float2 Centre, Heading;
        public bool Walking, Lying;
    }

    /// <summary>
    /// Where a baked herd (<see cref="HerdRecord"/>) is at any moment, from the shared clock alone (A-108 row 2), so every
    /// machine draws it in the same place and the host spawns its animals there when a player comes near, with nothing
    /// sent. Its day is a string of legs a few game hours long: each leg it walks from the last spot to the next (one
    /// picked by hash, or its bed, spot 0, when the leg starts in its resting hours), then grazes there. Each animal keeps
    /// its own place in the group, size and coat by hash, the same for the far form and the live animal it becomes.
    /// </summary>
    public static class HerdClock
    {
        /// <summary>A leg's length (s on the shared clock) lies between these, by herd.</summary>
        const float LegMin = 240f, LegMax = 480f;
        /// <summary>Herds amble, at this share of the species' walk.</summary>
        const float Amble = 0.6f;
        /// <summary>At most this share of a leg is walking; the rest is grazing.</summary>
        const float MaxWalk = 0.8f;

        /// <summary>A day animal beds down from dusk to dawn, a night one through the middle of the day.</summary>
        public static bool Resting(ActiveTime active, float hour) =>
            active == ActiveTime.Day ? hour < 5.5f || hour >= 20.5f :
            active == ActiveTime.Night && hour >= 8f && hour < 17f;

        /// <summary>A fixed random number in 0..1 for one draw (salt) of one animal (k, or -1 for the herd).</summary>
        public static float Random(uint seed, int k, uint salt) =>
            (BiomeSampling.Scramble(seed ^ BiomeSampling.Scramble((uint)(k + 1) * 0x9E3779B9u ^ salt * 0x85EBCA6Bu)) >> 8) / 16777216f;

        static float LegLength(in HerdRecord h) => math.lerp(LegMin, LegMax, Random(h.Seed, -1, 1));

        /// <summary>The spot leg n heads for.</summary>
        static int Target(in HerdRecord h, in HerdShape shape, in HerdTime time, long n, float leg, double shift)
        {
            if (h.Spots < 2)
                return 0;
            if (Resting(shape.Active, time.HourAt(n * (double)leg - shift)))
                return 0;
            var pick = BiomeSampling.Scramble(h.Seed ^ BiomeSampling.Scramble((uint)n * 0xC2B2AE35u + 0x27D4EB2Fu));
            return 1 + (int)(pick % (uint)(h.Spots - 1));
        }

        /// <summary>Where the herd is at the clock's moment.</summary>
        public static HerdPlace Place(ref HerdSetBlob set, in HerdRecord h, in HerdShape shape, in HerdTime time)
        {
            var leg = LegLength(h);
            var shift = Random(h.Seed, -1, 2) * leg;
            var at = time.T + shift;
            var n = (long)math.floor(at / leg);
            var into = (float)(at - n * (double)leg);
            var from = Target(h, shape, time, n - 1, leg, shift);
            var to = Target(h, shape, time, n, leg, shift);
            var a = set.Spots[h.FirstSpot + math.min(from, h.Spots - 1)];
            var b = set.Spots[h.FirstSpot + math.min(to, h.Spots - 1)];
            var gap = math.distance(a, b);
            var speed = math.max(0.1f, shape.WalkSpeed * Amble * shape.MeanScale);
            var walk = math.min(gap / speed, leg * MaxWalk);
            var place = new HerdPlace();
            if (gap < 0.5f || into >= walk)
            {
                place.Centre = b;
                place.Heading = gap < 0.5f ? new float2(0f, 1f) : (b - a) / gap;
                place.Lying = to == 0 && Resting(shape.Active, time.Hour);
                return place;
            }
            // (Eased off and on at the ends: a herd sets off and draws up, it doesn't jump to speed.)
            var u = into / walk;
            var eased = u * u * (3f - 2f * u) * 0.3f + u * 0.7f;
            place.Centre = math.lerp(a, b, eased);
            place.Heading = (b - a) / gap;
            place.Walking = true;
            return place;
        }

        /// <summary>How far out from the middle the group spreads (m).</summary>
        public static float Spread(in HerdRecord h, in HerdShape shape) => math.sqrt(h.Count) * shape.BodyLength * shape.MeanScale * 1.6f;

        /// <summary>One animal's place from the herd's middle (closer together on the move), drifting slowly as it grazes.</summary>
        public static float2 Offset(in HerdRecord h, in HerdShape shape, int k, double t, bool walking)
        {
            var r = Spread(h, shape) * math.sqrt(Random(h.Seed, k, 3));
            var a = Random(h.Seed, k, 4) * 2f * math.PI;
            var drift = (float)(t % 100000.0) * 0.012f + Random(h.Seed, k, 5) * 6.28f;
            var p = new float2(math.cos(a), math.sin(a)) * r + new float2(math.sin(drift), math.cos(drift * 0.7f)) * shape.BodyLength * shape.MeanScale * 0.4f;
            return walking ? p * 0.7f : p;
        }

        /// <summary>One animal's heading: the herd's way on the move, its own (turning slowly) while it grazes.</summary>
        public static float Yaw(in HerdRecord h, in HerdPlace place, int k, double t)
        {
            if (place.Walking)
                return math.atan2(place.Heading.x, place.Heading.y) + (Random(h.Seed, k, 6) - 0.5f) * 0.3f;
            return Random(h.Seed, k, 7) * 2f * math.PI + math.sin((float)(t % 100000.0) * 0.01f + k) * 0.6f;
        }

        /// <summary>One animal's size (CreatureState.Scale) in its species' range.</summary>
        public static float Scale(in HerdRecord h, in HerdShape shape, int k) => math.lerp(shape.SizeMin, shape.SizeMax, Random(h.Seed, k, 8));

        /// <summary>One animal's coat.</summary>
        public static int Coat(ref CreatureBlob b, in HerdRecord h, int k) => b.PickCoat(Random(h.Seed, k, 9));
    }
}
