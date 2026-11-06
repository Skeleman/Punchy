using System.Collections.Generic;
using Punchy.Terrain;

namespace Punchy.Fauna
{
    /// <summary>
    /// The overworld spawn table's socket kinds (Resources/SpawnTables/Overworld, A-80): which spawner asks. The first
    /// seven are the critter spots, in AmbientLife.CritterKind's order. New kinds go at the end (rows store the number).
    /// </summary>
    public enum OverworldSlot
    {
        Songbird, Woodpecker, Owl, Frog, Mouse, Gull, WharfFish,
        Hermit, Camper, Traveller, Messenger, HoneyThief, Domestic, CaveFolk, CrossingFisher
    }

    /// <summary>
    /// The director's reads of the overworld spawn table (A-80): it replaced the critter table, the domestic table and
    /// the role look-ups, so every overworld spawner picks its species from rows. The spawners keep their own places
    /// and timing; only the "which species" question goes to the table. A pick draws nothing from the director's rng.
    /// </summary>
    public partial class FaunaDirector
    {
        SpawnTable overworld;
        readonly List<int> fitting = new List<int>();

        /// <summary>The overworld table (loaded once).</summary>
        public static SpawnTable OverworldTable => SpawnTable.Load("Overworld");

        /// <summary>Every species the overworld table can name, for the species list.</summary>
        static IEnumerable<CreatureSpecies> OverworldSpecies(SpawnTable table)
        {
            if (table == null)
                yield break;
            foreach (var r in table.Rows)
                if (r.Species != null)
                    yield return r.Species;
        }

        /// <summary>The species index the table draws for one socket of a kind, or -1 (no row fits, or its species isn't known).</summary>
        int TableSpecies(OverworldSlot slot, uint seed, DayPhase time = DayPhase.All, LandmarkType landmark = null)
        {
            if (overworld == null)
                return -1;
            var s = SpawnSocket.Of((int)slot, seed);
            s.Time = time;
            s.Landmark = landmark;
            var row = overworld.PickRow(s);
            return row >= 0 ? SpeciesIndex(overworld.Rows[row].Species) : -1;
        }

        /// <summary>Every row of a kind that fits (in table order), for spawners that take all of them.</summary>
        List<int> TableRows(OverworldSlot slot, LandmarkType landmark = null)
        {
            fitting.Clear();
            if (overworld == null)
                return fitting;
            var s = SpawnSocket.Of((int)slot, 0);
            s.Landmark = landmark;
            overworld.AllFitting(s, fitting);
            return fitting;
        }

        /// <summary>A critter spot's kind as a table kind.</summary>
        static OverworldSlot CritterSlot(AmbientLife.CritterKind kind) => (OverworldSlot)(int)kind;
    }
}
