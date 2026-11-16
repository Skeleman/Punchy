using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Flora
{
    /// <summary>
    /// W-175 row 6 (#415): every unit by id, so a unit can nest another ("unit" part, UnitPart.Unit) and a species can
    /// grow a saved blueprint. Holds the coded units (FloraGenerator.Units), the JSON units species load (FloraSpecies
    /// .UnitJson), and in the editor every blueprint in the Model Lab's folder. Read from worker threads (pregrown
    /// variants), so every access takes the lock.
    /// </summary>
    public static class UnitLibrary
    {
        /// <summary>Where the Model Lab saves blueprints (the editor reads nested units from here).</summary>
        public const string Folder = "Assets/Game/Flora/Units";

        static readonly object Gate = new object();
        static Dictionary<string, UnitDef> units;

        /// <summary>Adds or replaces a unit under its id (a JSON unit a species loaded). Coded ids keep their code.</summary>
        public static void Register(UnitDef unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.Id))
                return;
            lock (Gate)
            {
                Load();
                foreach (var coded in FloraGenerator.Units)
                    if (coded.Id == unit.Id)
                        return;
                units[unit.Id] = unit;
            }
        }

        /// <summary>The unit with this id, or null.</summary>
        public static UnitDef Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            lock (Gate)
            {
                Load();
                return units.TryGetValue(id, out var unit) ? unit : null;
            }
        }

        /// <summary>A unit from its JSON (as the Model Lab saves it), or null if it doesn't read.</summary>
        public static UnitDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var unit = JsonUtility.FromJson<UnitDef>(json);
                return unit != null && unit.Parts != null ? unit : null;
            }
            catch (System.ArgumentException)
            {
                return null;
            }
        }

        static void Load()
        {
            if (units != null)
                return;
            units = new Dictionary<string, UnitDef>();
            foreach (var coded in FloraGenerator.Units)
                units[coded.Id] = coded;
#if UNITY_EDITOR
            // The editor sees every saved blueprint, so the Model Lab can nest one before any species uses it. (A build
            // only has the ones a species references through UnitJson.)
            if (System.IO.Directory.Exists(Folder))
                foreach (var path in System.IO.Directory.GetFiles(Folder, "*.json"))
                    if (FromJson(System.IO.File.ReadAllText(path)) is { } saved && !string.IsNullOrEmpty(saved.Id) && !units.ContainsKey(saved.Id))
                        units[saved.Id] = saved;
#endif
        }

        /// <summary>Forgets the loaded units (the Model Lab calls this after a save, so a nested unit picks up the edit).</summary>
        public static void Reload()
        {
            lock (Gate)
                units = null;
        }
    }
}
