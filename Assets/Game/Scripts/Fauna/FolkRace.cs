using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// What a folk race is like, as tags (A-81 row 2): behaviour asks for a trait, never for a race by name. Folk is any
    /// race at all (doors, shading its eyes, weather talk); Fishing is who works a wharf or a cave pool. Append new
    /// traits, never reorder: race assets store the bits.
    /// </summary>
    [System.Flags]
    public enum RaceTrait : byte
    {
        None = 0, Folk = 1, ColdAdapted = 2, HeatAdapted = 4, Aquatic = 8, Fishing = 16, CaveDweller = 32, Spirit = 64
    }

    /// <summary>
    /// A-81 row 2: one folk race as data, in Resources/Races. Its species (by species or body name) get its weather taste
    /// as their ComfortProfile and its <see cref="Id"/> as the profile's race; the brain looks its traits up by that id
    /// (<see cref="FolkRaces.Has"/>). Its asset name is its key in weather-lines.json. A race that ignores the weather
    /// (Minds off, a ghost) gets only its id. Lower <see cref="Order"/> is matched first (a hermit before the human
    /// whose body it wears).
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Folk Race", fileName = "Race")]
    public class FolkRace : ScriptableObject
    {
        [Tooltip("Its id in the comfort profile (1 and up; never change a placed one's: the baked species data stores it).")]
        public byte Id;
        [Tooltip("Match order (lower first) when a species could fit more than one race.")]
        public int Order;
        [Tooltip("Species or body asset names that belong to it.")]
        public string[] Species = new string[0];
        [Tooltip("What it is like: behaviour reads these, not the race.")]
        public RaceTrait Traits = RaceTrait.Folk;
        [Tooltip("Off: it ignores the weather (its species get only the race id).")]
        public bool Minds = true;
        [Tooltip("The heat it is happy in, on the climate's 0..1 temperature scale.")]
        public Vector2 Heat = new Vector2(0.28f, 0.72f);
        [Tooltip("How much each bothers it (0 not at all, 1 a lot, below 0 it likes it).")]
        public float Rain, Snow, Storm, Bright, Wind, Dry;
        [Tooltip("The shelter it looks for, best first.")]
        public ShelterNeed Seeks;
    }

    /// <summary>
    /// The folk races, loaded once from Resources/Races, and their traits by id in a SharedStatic so Burst jobs can ask
    /// (<see cref="Has"/>). The table is filled when the races load, which ComfortProfiles does while the director bakes
    /// its species, before any creature thinks.
    /// </summary>
    public static class FolkRaces
    {
        static List<FolkRace> all;

        /// <summary>Every race, in match order.</summary>
        public static List<FolkRace> All
        {
            get
            {
                if (all == null)
                {
                    all = new List<FolkRace>(Resources.LoadAll<FolkRace>("Races"));
                    all.Sort((a, b) => a.Order.CompareTo(b.Order));
                    ref var table = ref RaceTraitTable.Traits.Data;
                    table.Clear();
                    foreach (var r in all)
                    {
                        while (table.Length <= r.Id)
                            table.Add(0);
                        table[r.Id] = (byte)r.Traits;
                    }
                }
                return all;
            }
        }

        /// <summary>The race a species belongs to (by its own or its body's name), or null for an animal.</summary>
        public static FolkRace Of(string species, string body)
        {
            foreach (var r in All)
                foreach (var n in r.Species)
                    if (n == species || n == body)
                        return r;
            return null;
        }

        /// <summary>The race with this id, or null (0: an animal).</summary>
        public static FolkRace ById(byte id)
        {
            if (id == 0)
                return null;
            foreach (var r in All)
                if (r.Id == id)
                    return r;
            return null;
        }

        /// <summary>Whether race <paramref name="id"/> has all of <paramref name="trait"/> (false for animals, id 0). Burst-safe.</summary>
        public static bool Has(byte id, RaceTrait trait)
        {
            ref var table = ref RaceTraitTable.Traits.Data;
            return id < table.Length && ((RaceTrait)table[id] & trait) == trait;
        }
    }

    /// <summary>The races' traits by id, for <see cref="FolkRaces.Has"/> (its own class so Burst sees only this static).</summary>
    static class RaceTraitTable
    {
        public static readonly SharedStatic<FixedList32Bytes<byte>> Traits = SharedStatic<FixedList32Bytes<byte>>.GetOrCreate<TraitsKey>();
        class TraitsKey { }
    }
}
