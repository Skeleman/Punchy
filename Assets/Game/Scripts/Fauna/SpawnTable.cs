using System;
using System.Collections.Generic;
using Punchy.Terrain;
using UnityEngine;

// Design note (A-80, systems audit finding 1)
// -------------------------------------------
// One table system for every spawn, shaped like the dungeon spawner's rows + sockets:
//  - A SpawnTable asset (Resources/SpawnTables/<Name>) is a list of weighted rows. A row says WHAT may spawn (Content:
//    a name the table's user resolves, e.g. a DungeonContent or a species asset; or Species directly) and WHERE/WHEN it
//    may: socket kind and size, dungeon/chamber type, socket tags, biome (asset list or derived family), time of day,
//    season (stub until seasons exist) and an optional world-state tag.
//  - A SpawnSocket is the question: "this spot, this kind, this biome, now". The spot owner fills it; the table answers.
//    Dungeon sockets, critter spots, settlements and travellers' roads all ask the same way.
//  - Pick is one weighted draw over the rows that fit, rolled from the socket's seed. A row is either Seeded (fixed per
//    world seed: the same pick on every machine and every visit) or live (may change per visit). The first roll is
//    always seeded, so whether a socket lands on a seeded row never changes; landing on a live row re-rolls among the
//    live rows that fit with the caller's live salt (a visit or day counter). A table of seeded rows draws exactly as
//    the old dungeon tables did, so DungeonSpawner moved over with an unchanged pick hash.
//  - Biomes are matched by asset or by family flags derived from the asset's own climate numbers, never by name.
//    When biome assets get real tags (world lane, audit finding 5), Families switches to read them.
//  - Rows are matched in list order: reordering rows changes picks for the same seed (like the old code tables).
namespace Punchy.Fauna
{
    /// <summary>Parts of the day a row may spawn in (bit flags; 0 or All mean any time).</summary>
    [Flags]
    public enum DayPhase : byte { Dawn = 1, Day = 2, Dusk = 4, Night = 8, All = 15 }

    /// <summary>Seasons a row may spawn in. A stub: there are no seasons yet, so every socket is in all of them.</summary>
    [Flags]
    public enum SeasonMask : byte { Spring = 1, Summer = 2, Autumn = 4, Winter = 8, All = 15 }

    /// <summary>
    /// Broad biome families a row can filter on, derived from a biome asset's climate numbers (not its name):
    /// see <see cref="SpawnTable.FamilyOf"/>. New flags go at the end.
    /// </summary>
    [Flags]
    public enum BiomeFamily : byte { Wet = 1, Dry = 2, Hot = 4, Cold = 8, Temperate = 16, Water = 32 }

    /// <summary>One weighted row of a <see cref="SpawnTable"/>: what may spawn, where and when.</summary>
    [Serializable]
    public class SpawnRow
    {
        [Tooltip("What spawns, as a name the table's user resolves (a DungeonContent name, a species asset name, " +
                 "or Empty: a real pick of nothing). Ignored when Species is set.")]
        public string Content = "Empty";
        [Tooltip("The species that spawns, for creature tables (wins over Content).")]
        public CreatureSpecies Species;
        [Min(0)] public int Weight = 10;
        [Tooltip("How many come at once (x to y). Dungeon sockets take one.")]
        public Vector2Int Count = new Vector2Int(1, 1);
        [Tooltip("Fixed per world seed (the same pick every visit). Off: may change per visit.")]
        public bool Seeded = true;

        [Header("Where")]
        [Tooltip("Socket kind this row fills, as the table's user numbers it (dungeon: SocketKind; critters: " +
                 "AmbientLife.CritterSpot). -1: any kind.")]
        public int Kind = -1;
        [Tooltip("Socket sizes from x to y (SocketSize: 0 small, 1 medium, 2 large).")]
        public Vector2Int Size = new Vector2Int(0, 2);
        [Tooltip("Dungeon or chamber types, a bit per type number (DungeonSpawner.Cave...). 65535: any.")]
        public int Types = 0xFFFF;
        [Tooltip("Socket tag bits the socket must all have (SocketTags for dungeons). 0: none needed.")]
        public int NeedTags;
        [Tooltip("Only at this landmark type (settlement tables). Empty: anywhere.")]
        public LandmarkType Landmark;
        [Tooltip("Only in these biomes. Empty: any biome.")]
        public List<BiomeDefinition> Biomes = new List<BiomeDefinition>();
        [Tooltip("Only in biomes of one of these families (derived from the biome's climate). None: any family.")]
        public BiomeFamily Families;

        [Header("When")]
        public DayPhase Times = DayPhase.All;
        public SeasonMask Seasons = SeasonMask.All;
        [Tooltip("Only while this world-state tag is set (e.g. a quest flag). Empty: always.")]
        public string WorldState = "";

        [Header("Extras (the user's own meaning)")]
        [Tooltip("How far from the middle they roam, as a share of the socket's radius (settlement tables).")]
        public float Range = 0.8f;
        [Tooltip("Their work, for folk (settlement tables).")]
        public CreatureTask Task;
    }

    /// <summary>
    /// The question a spawner asks a table: one socket (a pre-placed or found spot) with what is round it. Filled by
    /// the spot's owner; nothing here is picked.
    /// </summary>
    public struct SpawnSocket
    {
        public int Kind;
        public byte Size;
        public ushort Type;
        public uint Tags;
        /// <summary>Biome palette index (64 and up count as 63), or -1 when unknown.</summary>
        public int Biome;
        /// <summary>The landmark type the socket belongs to, or null.</summary>
        public LandmarkType Landmark;
        public DayPhase Time;
        public SeasonMask Season;
        /// <summary>The socket's own seed: the same on every machine for the same world and spot.</summary>
        public uint Seed;
        /// <summary>Mixed into live rows' draw (a visit or day counter). 0 for seeded-only spawners.</summary>
        public uint LiveSalt;

        /// <summary>A socket that matches every time, season and world state.</summary>
        public static SpawnSocket Of(int kind, uint seed, byte size = 0, ushort type = 0, uint tags = 0, int biome = -1) =>
            new SpawnSocket { Kind = kind, Seed = seed, Size = size, Type = type, Tags = tags, Biome = biome, Time = DayPhase.All, Season = SeasonMask.All };
    }

    /// <summary>
    /// A data table of spawn rows (A-80): the one system every spawner picks from (see the design note at the top of
    /// this file). Loaded from Resources/SpawnTables. The draw itself is <see cref="PickRow"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Spawn Table", fileName = "SpawnTable")]
    public class SpawnTable : ScriptableObject
    {
        public List<SpawnRow> Rows = new List<SpawnRow>();

        /// <summary>The palette the biome masks are built against (the director sets it on build); null: biome lists match nothing but -1.</summary>
        public static BiomeDefinition[] Palette;

        /// <summary>Set world-state tags, read by rows with a WorldState (shared world state owns setting them).</summary>
        public static readonly HashSet<string> WorldTags = new HashSet<string>();

        static readonly Dictionary<string, SpawnTable> loaded = new Dictionary<string, SpawnTable>();

        /// <summary>A table from Resources/SpawnTables (cached; null and an error when missing).</summary>
        public static SpawnTable Load(string name)
        {
            if (loaded.TryGetValue(name, out var t) && t != null)
                return t;
            t = Resources.Load<SpawnTable>("SpawnTables/" + name);
            if (t == null)
                Debug.LogError($"SpawnTable: Resources/SpawnTables/{name} missing");
            loaded[name] = t;
            return t;
        }

        /// <summary>Derived family flags for a biome asset, from its climate numbers.</summary>
        public static BiomeFamily FamilyOf(BiomeDefinition b)
        {
            if (b == null)
                return 0;
            BiomeFamily f = 0;
            if (b.Rainfall >= 0.6f || b.PondDepth > 0f)
                f |= BiomeFamily.Wet;
            if (b.Rainfall <= 0.25f)
                f |= BiomeFamily.Dry;
            if (b.Heat >= 0.4f)
                f |= BiomeFamily.Hot;
            if (b.Cold >= 0.4f)
                f |= BiomeFamily.Cold;
            if ((f & (BiomeFamily.Hot | BiomeFamily.Cold)) == 0)
                f |= BiomeFamily.Temperate;
            if (b.Altitude.y <= 0f)
                f |= BiomeFamily.Water;
            return f;
        }

        /// <summary>The part of the day for an hour (0-24).</summary>
        public static DayPhase PhaseOf(float hour) =>
            hour < 5f || hour >= 21f ? DayPhase.Night : hour < 8f ? DayPhase.Dawn : hour < 18f ? DayPhase.Day : DayPhase.Dusk;

        /// <summary>Whether a row may fill this socket.</summary>
        public static bool Fits(SpawnRow r, in SpawnSocket s)
        {
            if (r.Kind >= 0 && r.Kind != s.Kind)
                return false;
            if (s.Size < r.Size.x || s.Size > r.Size.y)
                return false;
            // (Types 16 and up fit nothing, as in the old dungeon tables.)
            if (s.Type >= 16 || (r.Types & (1 << s.Type)) == 0)
                return false;
            if (((uint)r.NeedTags & s.Tags) != (uint)r.NeedTags)
                return false;
            // (By asset name: the palette's landmark types, baked into the world subscene, aren't the same objects as a table's.)
            if (r.Landmark != null && (s.Landmark == null || r.Landmark.name != s.Landmark.name))
                return false;
            var times = r.Times == 0 ? DayPhase.All : r.Times;
            if (s.Time != 0 && (times & s.Time) == 0)
                return false;
            var seasons = r.Seasons == 0 ? SeasonMask.All : r.Seasons;
            if (s.Season != 0 && (seasons & s.Season) == 0)
                return false;
            if (!string.IsNullOrEmpty(r.WorldState) && !WorldTags.Contains(r.WorldState))
                return false;
            if ((r.Biomes != null && r.Biomes.Count > 0) || r.Families != 0)
            {
                var biome = Palette != null && s.Biome >= 0 && s.Biome < Palette.Length ? Palette[s.Biome] : null;
                if (biome == null)
                    return false;
                if (r.Biomes != null && r.Biomes.Count > 0 && !r.Biomes.Exists(b => b != null && b.name == biome.name))
                    return false;
                if (r.Families != 0 && (FamilyOf(biome) & r.Families) == 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The pick for a socket: the index of the row drawn (weighted over the rows that fit, in list order), or -1
        /// when none fit. Seeded first roll; a live row re-rolls among the live rows with the socket's LiveSalt.
        /// </summary>
        public int PickRow(in SpawnSocket s)
        {
            int total = 0;
            for (int i = 0; i < Rows.Count; i++)
                if (Fits(Rows[i], s))
                    total += Rows[i].Weight;
            if (total == 0)
                return -1;
            var roll = (int)(Scramble(s.Seed ^ 0x5bd1e995u) % (uint)total);
            int pick = -1;
            for (int i = 0; i < Rows.Count; i++)
                if (Fits(Rows[i], s) && (roll -= Rows[i].Weight) < 0)
                {
                    pick = i;
                    break;
                }
            if (pick < 0 || Rows[pick].Seeded)
                return pick;
            int live = 0;
            for (int i = 0; i < Rows.Count; i++)
                if (!Rows[i].Seeded && Fits(Rows[i], s))
                    live += Rows[i].Weight;
            var liveRoll = (int)(Scramble(s.Seed ^ Scramble(s.LiveSalt ^ 0x11FEu)) % (uint)live);
            for (int i = 0; i < Rows.Count; i++)
                if (!Rows[i].Seeded && Fits(Rows[i], s) && (liveRoll -= Rows[i].Weight) < 0)
                    return i;
            return pick;
        }

        /// <summary>How many a picked row brings, from its count range and the socket's seed.</summary>
        public int CountOf(int row, in SpawnSocket s)
        {
            if (row < 0)
                return 0;
            var c = Rows[row].Count;
            return c.y <= c.x ? c.x : c.x + (int)(Scramble(s.Seed ^ 0xC0047u) % (uint)(c.y - c.x + 1));
        }

        /// <summary>Every row that fits a socket, in order (for spawners that take all fitting rows, not one draw).</summary>
        public void AllFitting(in SpawnSocket s, List<int> into)
        {
            into.Clear();
            for (int i = 0; i < Rows.Count; i++)
                if (Fits(Rows[i], s) && Rows[i].Weight > 0)
                    into.Add(i);
        }

        /// <summary>
        /// Avalanches a seed (math.hash on a lone uint does not, so neighbouring socket seeds would draw alike).
        /// </summary>
        public static uint Scramble(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x;
        }
    }
}
