using Punchy.Terrain;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// What a house shows of the family inside it at an hour of a day (A-109 row 3): smoke from the hearth and a lamp in
    /// the window, each 0 to 1, and how many of the family are in. A pure function of the household record (its placement
    /// key and its members' roles), the day and the hour: no state, no random draw, nothing read from the world, so every
    /// machine gets the same answer with nothing sent or stored. It does not read who has died or who is out in the world
    /// as a real folk, since those differ from machine to machine. Dusk and dawn are fixed hours here; whatever draws the
    /// sign (R-25: panes, impostor glow, chimney smoke) may also gate the lamp by the sun.
    /// The family's hours follow FarFolk.Day, so a house goes dark-hearthed as its far figures walk out and lights as
    /// they come back.
    /// </summary>
    public readonly struct HomeSign
    {
        /// <summary>Hearth smoke, 0 (none) to 1 (a full fire).</summary>
        public readonly float Smoke;
        /// <summary>Lamp light in the window, 0 (dark) to 1.</summary>
        public readonly float Lamp;
        /// <summary>How many of the family are in the house.</summary>
        public readonly int Home;

        public HomeSign(float smoke, float lamp, int home)
        {
            Smoke = smoke;
            Lamp = lamp;
            Home = home;
        }

        /// <summary>The fixed hours the lamps go by: light is wanted before Dawn and after Dusk.</summary>
        public const float Dawn = 6.5f, Dusk = 18.5f;
        /// <summary>How long a fire or a lamp takes to come up or die down (hours).</summary>
        const float Ramp = 0.25f;

        /// <summary>When a member goes out and when it is back in (hours), by its role: FarFolk.Day's hours (an elder
        /// keeps to the house and its door, so it counts as in all day).</summary>
        public static void Hours(in Kinsfolk member, out float leave, out float back)
        {
            var u = FarFolk.Hash01(member.Individual, 11);
            switch (member.Role)
            {
                case Kin.Child:
                    leave = 8f + 0.5f * u;
                    back = 17.25f + 0.1f * u;
                    break;
                case Kin.Elder:
                    leave = back = 12f;
                    break;
                default:
                    leave = 6f + 0.75f * u;
                    back = 18f + 0.35f * u;
                    break;
            }
        }

        /// <summary>The house's own draws for a day: when it wakes, when it goes to bed (past 24 for a house that sits
        /// up late), and whether a midday fire is lit.</summary>
        static void Habits(uint key, int day, out float wake, out float bed, out bool noonFire)
        {
            var seed = BiomeSampling.Scramble(key + (uint)day * 0x85EBCA6Bu);
            wake = 4.75f + FarFolk.Hash01(seed, 1);
            bed = 21f + 2f * FarFolk.Hash01(seed, 2);
            // One house in eight sits up late that night, its lamp alone in a dark town.
            if (FarFolk.Hash01(seed, 3) < 0.125f)
                bed += 1.5f + 1.5f * FarFolk.Hash01(seed, 4);
            noonFire = FarFolk.Hash01(seed, 5) < 0.7f;
        }

        /// <summary>0 outside from..to, 1 inside, easing over Ramp at each end.</summary>
        static float Span(float hour, float from, float to) =>
            to <= from ? 0f : math.saturate((hour - from) / Ramp) * math.saturate((to - hour) / Ramp);

        /// <summary>What the house shows at this hour (0 to 24) of this day.</summary>
        public static HomeSign Of(Household house, int day, float hour)
        {
            if (house == null || house.Members == null || house.Members.Length == 0)
                return default;
            hour = math.clamp(hour, 0f, 24f);
            int home = 0, adultsHome = 0;
            float firstOut = 24f, firstBack = 24f;
            foreach (var member in house.Members)
            {
                Hours(member, out var leave, out var back);
                var isIn = hour < leave || hour >= back;
                if (isIn)
                {
                    home++;
                    if (member.Role != Kin.Child)
                        adultsHome++;
                }
                if (member.Role == Kin.Child || member.Role == Kin.Elder)
                    continue;
                firstOut = math.min(firstOut, leave);
                firstBack = math.min(firstBack, back);
            }
            Habits(house.Key, day, out var wake, out var bed, out var noonFire);
            Habits(house.Key, day - 1, out _, out var lastBed, out _);

            // Lamp: someone up and in, in the dark. Before dawn from waking; after dusk until bed; and past midnight
            // for a house that sat up late the night before.
            var lamp = math.max(Span(hour, wake, Dawn + Ramp), Span(hour, Dusk - Ramp, math.min(bed, 24f + Ramp)));
            lamp = math.max(lamp, Span(hour + 24f, 24f - Ramp, lastBed));
            if (home == 0)
                lamp = 0f;

            // Smoke: the morning fire from waking until the first of the workers is out of the door, a midday fire on
            // most days where a grown-up is in, and the evening fire from the first one back until near bedtime.
            var smoke = Span(hour, wake + 0.1f, math.max(firstOut < 24f ? firstOut + 0.5f : 8.5f, wake + 1f));
            if (noonFire && adultsHome > 0)
                smoke = math.max(smoke, 0.6f * Span(hour, 11.5f, 13.25f));
            smoke = math.max(smoke, Span(hour, firstBack < 24f ? firstBack + 0.2f : 17.5f, math.min(bed - 0.75f, 24f + Ramp)));
            smoke = math.max(smoke, 0.5f * Span(hour + 24f, 24f - Ramp, lastBed - 0.75f));
            // A dome on the sea bed has no hearth; an igloo keeps a low fire going whenever someone is in and up.
            if (house.Kind == "dome")
                smoke = 0f;
            else if (house.Kind == "igloo" && home > 0)
                smoke = math.max(smoke, 0.35f * Span(hour, wake, math.min(bed, 24f + Ramp)));
            if (home == 0)
                smoke = 0f;
            return new HomeSign(smoke, lamp, home);
        }

        /// <summary>
        /// A digest of one landmark's houses over a day, a quarter hour at a time (smoke and lamp in 256 steps each, and
        /// the count in), for checking that two machines agree. smokeHours and lampHours: the day's totals over the houses.
        /// </summary>
        public static uint Digest(System.Collections.Generic.List<Household> houses, System.Collections.Generic.List<int> which, int day,
                                  out float smokeHours, out float lampHours)
        {
            var digest = 2166136261u;
            smokeHours = lampHours = 0f;
            foreach (var h in which)
                for (int q = 0; q < 96; q++)
                {
                    var sign = Of(houses[h], day, q * 0.25f);
                    smokeHours += sign.Smoke * 0.25f;
                    lampHours += sign.Lamp * 0.25f;
                    digest = (digest ^ (uint)math.round(sign.Smoke * 255f)) * 16777619u;
                    digest = (digest ^ (uint)math.round(sign.Lamp * 255f)) * 16777619u;
                    digest = (digest ^ (uint)sign.Home) * 16777619u;
                }
            return digest;
        }
    }
}
