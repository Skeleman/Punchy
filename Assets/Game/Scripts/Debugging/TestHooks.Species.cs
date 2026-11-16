using Punchy.Fauna;
using UnityEngine;

namespace Punchy.Debugging
{
    /// <summary>
    /// Test hooks for the fauna species table, the network's creature indices (M-06): the built-in species sorted by
    /// name, then the Model Lab's species in the order they were made, so saving one never moves a built-in's index.
    /// </summary>
    public static partial class TestHooks
    {
        /// <summary>"count hash" of the built-in species (FNV-1a over their names in index order), then " lab n".</summary>
        public static string BuiltInSpecies()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null)
                return "no FaunaDirector";
            var hash = 2166136261u;
            for (int i = 0; i < director.BuiltInSpeciesCount; i++)
            {
                foreach (var c in director.SpeciesAt(i).name)
                    hash = (hash ^ c) * 16777619u;
                hash = (hash ^ 0x1F) * 16777619u;
            }
            return $"{director.BuiltInSpeciesCount} {hash:X8} lab {director.SpeciesCount - director.BuiltInSpeciesCount}";
        }

        /// <summary>What's wrong with the species order (empty: none): built-ins out of name order, or a Lab species not
        /// at its place after them (LabSpeciesList order).</summary>
        public static string SpeciesOrderProblems()
        {
            var director = Object.FindAnyObjectByType<FaunaDirector>();
            if (director == null)
                return "no FaunaDirector";
            var problems = new System.Text.StringBuilder();
            for (int i = 1; i < director.BuiltInSpeciesCount; i++)
                if (string.CompareOrdinal(director.SpeciesAt(i - 1).name, director.SpeciesAt(i).name) >= 0)
                    problems.Append($"built-in {i} {director.SpeciesAt(i).name} out of order; ");
            var next = director.BuiltInSpeciesCount;
            foreach (var s in LabSpeciesList.All())
            {
                if (s == null)
                    continue;
                var index = director.SpeciesIndex(s.name);
                if (index != next && index >= director.BuiltInSpeciesCount)
                    problems.Append($"lab {s.name} at {index}, expected {next}; ");
                if (index == next)
                    next++;
            }
            if (next != director.SpeciesCount)
                problems.Append($"{director.SpeciesCount - next} species after the Lab ones; ");
            return problems.ToString();
        }
    }
}
