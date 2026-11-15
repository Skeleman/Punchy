using System;
using Unity.Entities;
using Unity.Mathematics;
using Punchy.Terrain;

namespace Punchy.Fauna
{
    /// <summary>
    /// The character traits a folk's persona is made of (A-93). This enum is the one list: add a trait here and to
    /// FolkPersona.Traits below, and dialogue.json lines can key on it by its name in lower case.
    /// </summary>
    public enum PersonaTrait : byte { Warmth, Mood, Trust, Curiosity, Courage, Pride, Beasts }

    /// <summary>
    /// Who a folk is in conversation (A-93): seven traits, each 0..1, worked out from its Individual hash, so every
    /// machine agrees on it with nothing sent and nothing stored (it never touches the spawn draws, so placement and the
    /// fauna fingerprint can't move). Warmth: cold to kind. Mood: grumpy to cheerful, in the same bands as
    /// FolkJobs.Mood (a quarter grumpy below 0.25, a third cheerful from 0.65). Trust: suspicious to trusting. Curiosity:
    /// incurious to nosy. Courage: timid to bold (the brain's shy ones, CreatureBlob trait 21, are timid). Pride: humble
    /// to proud. Beasts: fears or loathes animals (0) to loves them (1). Built only when a folk is spoken to.
    /// </summary>
    public struct FolkPersona
    {
        /// <summary>The traits, by PersonaTrait: name (as dialogue.json writes it) and the salt its draw is hashed with.</summary>
        public static readonly (PersonaTrait Trait, string Name, uint Salt)[] Traits =
        {
            (PersonaTrait.Warmth, "warmth", 0x3C6EF372u),
            (PersonaTrait.Mood, "mood", 0x6A09E667u),     // (FolkJobs.Mood's salt: the same grumpy and cheerful folk)
            (PersonaTrait.Trust, "trust", 0xA54FF53Au),
            (PersonaTrait.Curiosity, "curiosity", 0x510E527Fu),
            (PersonaTrait.Courage, "courage", 0x9B05688Cu),
            (PersonaTrait.Pride, "pride", 0x1F83D9ABu),
            (PersonaTrait.Beasts, "beasts", 0x5BE0CD19u),
        };
        public static int Count => Traits.Length;

        /// <summary>The values by PersonaTrait (room for eight).</summary>
        float4 lo, hi;
        /// <summary>The Individual it was drawn from.</summary>
        public uint Individual;

        public float this[PersonaTrait t]
        {
            get => (int)t < 4 ? lo[(int)t] : hi[(int)t - 4];
            set { if ((int)t < 4) lo[(int)t] = value; else hi[(int)t - 4] = value; }
        }

        public float Warmth => this[PersonaTrait.Warmth];
        public float Mood => this[PersonaTrait.Mood];
        public float Trust => this[PersonaTrait.Trust];
        public float Curiosity => this[PersonaTrait.Curiosity];
        public float Courage => this[PersonaTrait.Courage];
        public float Pride => this[PersonaTrait.Pride];
        public float Beasts => this[PersonaTrait.Beasts];

        /// <summary>One individual's persona: the same on every run and every machine.</summary>
        public static FolkPersona Of(uint individual)
        {
            var p = new FolkPersona { Individual = individual };
            foreach (var t in Traits)
            {
                var h = BiomeSampling.Scramble(individual ^ t.Salt);
                // Mood follows FolkJobs.Mood's percentile (h % 100: below 35 cheerful, from 75 grumpy) so the walk-by
                // lines and the talk agree; the rest are an even draw.
                p[t.Trait] = t.Trait == PersonaTrait.Mood
                    ? 1f - ((h % 100u) + 0.01f + 0.98f * ((BiomeSampling.Scramble(h) & 0xFFFF) / 65536f)) / 100f // (kept off the percent's edges)
                    : (h >> 8) / 16777216f;
            }
            // (The brain's shy individuals, who keep their distance from the player, are the timid ones.)
            if (CreatureBlob.Trait(individual, 21) < -0.75f)
                p[PersonaTrait.Courage] *= 0.35f;
            return p;
        }

        /// <summary>A trait's band: 0 low (below a third), 1 mid, 2 high (two thirds and up).</summary>
        public int Band(PersonaTrait t) => Band(this[t]);
        public static int Band(float v) => v < 1f / 3f ? 0 : v < 2f / 3f ? 1 : 2;
        public static readonly string[] BandNames = { "low", "mid", "high" };

        /// <summary>Its mood band as FolkJobs names it.</summary>
        public FolkMood MoodBand => Mood >= 0.65f ? FolkMood.Cheerful : Mood < 0.25f ? FolkMood.Grumpy : FolkMood.Plain;

        /// <summary>Its trait by name ("warmth"), or -1 for no such trait.</summary>
        public static int TraitIndex(string name)
        {
            foreach (var t in Traits)
                if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                    return (int)t.Trait;
            return -1;
        }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in Traits)
                sb.Append(sb.Length > 0 ? " " : "").Append(t.Name).Append(' ').Append(this[t.Trait].ToString("F2"));
            return sb.ToString();
        }
    }

    /// <summary>How a folk is just now, as it would tell you (A-93): from what the brain already shows or knows.</summary>
    [Flags]
    public enum FolkCondition : ushort
    {
        None = 0,
        /// <summary>Late at night, its tired cue, or resting.</summary>
        Tired = 1,
        /// <summary>At its work.</summary>
        Busy = 2,
        Hungry = 4,
        Thirsty = 8,
        Lonely = 16,
        /// <summary>Fleeing, or still startled.</summary>
        Scared = 32,
        /// <summary>Hurt (nothing hurts folk yet: no damage system; the flag is here for when one comes).</summary>
        Hurt = 64,
        /// <summary>Bothered by the weather (FolkState.Weather says how).</summary>
        Weather = 128,
        Night = 256,
        /// <summary>A water breather (the sea folk) out on dry land (A-103 row 2b): its talk grumbles about the dryness.</summary>
        Dry = 512,
    }

    /// <summary>A folk's condition and the hour, worked out when it's spoken to (FolkState.Of).</summary>
    public struct FolkState
    {
        public FolkCondition Flags;
        /// <summary>What the weather is doing to it (None when it doesn't mind).</summary>
        public ComfortKind Weather;
        public float Hour;

        public bool Has(FolkCondition c) => (Flags & c) != 0;

        /// <summary>
        /// From its synced state (mode, need cue) and, where this machine runs its brain (the host), its mind (needs,
        /// startle, discomfort). A joined client has no mind to read: it goes by the cue and works out the weather's
        /// pressure itself (the same seeded tolerance the host's brain uses).
        /// </summary>
        public static FolkState Of(in CreatureState s, ref CreatureBlob b, bool hasMind, in CreatureMind mind, float hour, float night)
        {
            var f = FolkCondition.None;
            var cue = (NeedCue)s.Cue;
            if (night > 0.5f)
                f |= FolkCondition.Night;
            if (cue == NeedCue.Tired || hour >= 22f || hour < 4.5f || s.Mode == CreatureMode.Rest || s.Mode == CreatureMode.Sleep ||
                hasMind && mind.Tired > 0.65f)
                f |= FolkCondition.Tired;
            if (s.Mode == CreatureMode.Work)
                f |= FolkCondition.Busy;
            if (cue == NeedCue.Hungry || hasMind && mind.Hunger > 0.65f)
                f |= FolkCondition.Hungry;
            if (cue == NeedCue.Thirsty || hasMind && mind.Thirst > 0.65f)
                f |= FolkCondition.Thirsty;
            if (cue == NeedCue.Lonely || hasMind && mind.Lonely > 0.75f)
                f |= FolkCondition.Lonely;
            if (s.Mode == CreatureMode.Flee || hasMind && mind.Unsettled > 0f)
                f |= FolkCondition.Scared;
            var weather = ComfortKind.None;
            if (hasMind && mind.Discomfort > 0.25f)
                weather = mind.DiscomfortKind;
            else if (!hasMind && Comfort.Pressure(in b.Comfort, s.Individual, Comfort.WeatherAt(s.Position.xz), night, false,
                         ShelterNeed.None, out var felt) > 0.35f)
                weather = felt;
            if (weather != ComfortKind.None)
                f |= FolkCondition.Weather;
            return new FolkState { Flags = f, Weather = weather, Hour = hour };
        }

        /// <summary>
        /// A-103 row 2b: whether a lake bed walker (the sea folk) is out of the water, standing on dry land or wading no
        /// deeper than its shins. Only asked for WalksBed bodies (one ground sample), so other folk pay nothing.
        /// </summary>
        public static bool OutOfWater(float3 position, bool walksBed, in TerrainSource terrain)
        {
            if (!walksBed)
                return false;
            var g = terrain.SampleGround(position.xz);
            return !(g.HasWater && g.WaterSurface > position.y + 0.25f);
        }

        public override string ToString() => Weather != ComfortKind.None ? $"{Flags} ({Weather})" : Flags.ToString();
    }
}
