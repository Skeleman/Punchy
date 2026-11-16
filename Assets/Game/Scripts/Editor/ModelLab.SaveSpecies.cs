using System.Collections.Generic;
using System.IO;
using System.Linq;
using Punchy.Fauna;
using Punchy.Terrain;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-06 row 3b (#413): "Save as new creature". Writes the loaded creature, builder edits and proportions included, as
    /// a new species: its own asset under Assets/Game/Fauna/Lab/&lt;name&gt;/ with the biomes it lives in
    /// (<see cref="CreatureSpecies.Homes"/>), appended to Resources/LabSpecies (<see cref="LabSpeciesList"/>), which
    /// gives it a network index after every built-in species. The species chain (the species and what it is a variant
    /// of) is copied whole, so the new body has its own name and its own proportions entry in CreatureTunables; limb
    /// types are copied only when the builder changed them, otherwise the new body shares the built-in limb asset. The
    /// loaded asset never changes. Names are unique among species assets, and a saved species is never overwritten
    /// (the list is append only): change it afterwards as the asset it is.
    /// </summary>
    public partial class ModelLab
    {
        const string LabFolder = "Assets/Game/Fauna/Lab";
        const string LabListPath = "Assets/Game/Resources/" + LabSpeciesList.ResourcePath + ".asset";

        string newSpeciesName = "";
        readonly List<SpeciesHome> newHomes = new List<SpeciesHome>();
        bool saveAsNewOpen;
        string saveAsNewResult = "";

        void DrawSaveAsNew()
        {
            if (family != Family.Creature || species == null || source == null)
                return;
            EditorGUILayout.Space();
            saveAsNewOpen = EditorGUILayout.Foldout(saveAsNewOpen, "Save as new creature", true);
            if (!saveAsNewOpen)
                return;
            using (new EditorGUI.IndentLevelScope())
            {
                newSpeciesName = EditorGUILayout.TextField("Name", newSpeciesName);
                var biomeNames = BiomeNames();
                EditorGUILayout.LabelField("Lives in (biome, animals per hectare)", EditorStyles.miniLabel);
                for (int i = 0; i < newHomes.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var home = newHomes[i];
                        var at = System.Array.IndexOf(biomeNames, home.Biome);
                        at = EditorGUILayout.Popup(Mathf.Max(0, at), biomeNames);
                        home.Biome = biomeNames.Length > 0 ? biomeNames[at] : home.Biome;
                        home.Density = Mathf.Max(0f, EditorGUILayout.FloatField(home.Density, GUILayout.Width(60f)));
                        if (GUILayout.Button("x", GUILayout.Width(22f)))
                        {
                            newHomes.RemoveAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
                if (GUILayout.Button("Add biome") && biomeNames.Length > 0)
                    newHomes.Add(new SpeciesHome { Biome = biomeNames[0], Density = 0.5f });
                var problem = NewSpeciesProblem(newSpeciesName, newHomes);
                using (new EditorGUI.DisabledScope(problem != null || EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Save as new creature"))
                        saveAsNewResult = SaveAsNew(newSpeciesName, newHomes);
                EditorGUILayout.HelpBox(problem ?? (saveAsNewResult.Length > 0 ? saveAsNewResult :
                    "A new species, built on " + source.name + ", in the biomes above. " + source.name + " itself is unchanged."),
                    problem != null ? MessageType.Warning : MessageType.None);
            }
        }

        static string[] BiomeNames() =>
            AssetDatabase.FindAssets("t:" + nameof(BiomeDefinition))
                .Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

        static HashSet<string> SpeciesNames() =>
            new HashSet<string>(AssetDatabase.FindAssets("t:" + nameof(CreatureSpecies))
                .Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))));

        /// <summary>Why this name and these homes can't be saved, or null.</summary>
        static string NewSpeciesProblem(string name, List<SpeciesHome> homes)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Give it a name.";
            if (name.Any(c => !char.IsLetterOrDigit(c)))
                return "Letters and digits only (the name is the asset's file name and its network name).";
            if (!char.IsLetter(name[0]))
                return "Start the name with a letter.";
            if (SpeciesNames().Contains(name))
                return "A species named " + name + " already exists.";
            if (homes.Count == 0)
                return "Pick at least one biome for it to live in.";
            if (homes.Select(h => h.Biome).Distinct().Count() != homes.Count)
                return "Each biome once.";
            return null;
        }

        /// <summary>Writes the work copy as a new species named <paramref name="name"/>; returns what it did.</summary>
        string SaveAsNew(string name, List<SpeciesHome> homes)
        {
            var problem = NewSpeciesProblem(name, homes);
            if (problem != null)
                return problem;
            var folder = LabFolder + "/" + name;
            if (!AssetDatabase.IsValidFolder(LabFolder))
                AssetDatabase.CreateFolder("Assets/Game/Fauna", "Lab");
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(LabFolder, name);

            // Limb types: a copy the builder changed becomes its own asset; an unchanged one maps back to the original.
            var limbs = new Dictionary<LimbType, LimbType>();
            int copiedLimbs = 0;
            foreach (var pair in limbSource)
            {
                var copy = pair.Key;
                var original = pair.Value;
                if (EditorJsonUtility.ToJson(copy) == EditorJsonUtility.ToJson(original))
                {
                    limbs[copy] = original;
                    continue;
                }
                var limb = Instantiate(copy);
                limb.name = name + "_" + original.name;
                limb.hideFlags = HideFlags.None;
                AssetDatabase.CreateAsset(limb, folder + "/" + limb.name + ".asset");
                limbs[copy] = limb;
                copiedLimbs++;
            }

            // The species chain, root first: root "<name>", each species it is a variant of "<name>_<original name>".
            var chain = new List<CreatureSpecies>();
            for (var s = species; s != null && chain.Count < 8 && !chain.Contains(s); s = s.VariantOf)
                chain.Add(s);
            var saved = new CreatureSpecies[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                var c = Instantiate(chain[i]);
                c.name = i == 0 ? name : name + "_" + chain[i].name;
                c.hideFlags = HideFlags.None;
                foreach (var slot in c.Parts)
                    if (slot.Limb != null && limbs.TryGetValue(slot.Limb, out var limb))
                        slot.Limb = limb;
                c.Homes = i == 0 ? homes.Select(h => new SpeciesHome { Biome = h.Biome, Density = h.Density }).ToList() : new List<SpeciesHome>();
                saved[i] = c;
            }
            for (int i = 0; i < saved.Length; i++)
                saved[i].VariantOf = i + 1 < saved.Length ? saved[i + 1] : null;
            for (int i = saved.Length - 1; i >= 0; i--)
                AssetDatabase.CreateAsset(saved[i], folder + "/" + saved[i].name + ".asset");

            // Proportions and pose nudges key on the body's name: the new body takes a copy of the loaded one's entry.
            var tunes = Tunables(false);
            var entry = tunes != null ? tunes.Creatures.FirstOrDefault(t => t != null && t.Species == species.Body.name) : null;
            if (entry != null && !entry.IsDefault)
            {
                var copy = JsonUtility.FromJson<CreatureTune>(JsonUtility.ToJson(entry));
                copy.Species = saved[saved.Length - 1].name;
                tunes.Creatures.Add(copy);
                Save();
            }

            // Append to the Lab list (made on first save): the new species' index comes after every other.
            var list = AssetDatabase.LoadAssetAtPath<LabSpeciesList>(LabListPath);
            if (list == null)
            {
                list = CreateInstance<LabSpeciesList>();
                AssetDatabase.CreateAsset(list, LabListPath);
            }
            list.Species.Add(saved[0]);
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return $"Saved {name} ({saved.Length} species asset{(saved.Length > 1 ? "s" : "")}, {copiedLimbs} limb type{(copiedLimbs != 1 ? "s" : "")}" +
                   $"{(entry != null && !entry.IsDefault ? ", its proportions" : "")}) in {folder}; Lab species #{list.Species.Count}, " +
                   "living in " + string.Join(", ", homes.Select(h => h.Biome + " " + h.Density.ToString("0.##"))) + ".";
        }

        /// <summary>
        /// Scripting hook: saves the loaded creature as a new species. <paramref name="homes"/> is "Biome:density,..."
        /// (density defaults to 0.5). Returns what it did, or why not.
        /// </summary>
        public static string SaveAsNewSpecies(string name, string homes)
        {
            var w = Open();
            if (w.family != Family.Creature || w.species == null)
                return "No creature loaded.";
            var list = new List<SpeciesHome>();
            foreach (var part in (homes ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                var bits = part.Split(':');
                list.Add(new SpeciesHome
                {
                    Biome = bits[0].Trim(),
                    Density = bits.Length > 1 && float.TryParse(bits[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0.5f
                });
            }
            var known = BiomeNames();
            var missing = list.FirstOrDefault(h => !known.Contains(h.Biome));
            if (missing != null)
                return "No biome named " + missing.Biome + ".";
            return w.saveAsNewResult = w.SaveAsNew(name, list);
        }

        /// <summary>Scripting hook (shots): fills the panel's fields without saving.</summary>
        public static void ShowSaveAsNew(string name, string biome, float density)
        {
            var w = Open();
            w.saveAsNewOpen = true;
            w.newSpeciesName = name;
            w.newHomes.Clear();
            w.newHomes.Add(new SpeciesHome { Biome = biome, Density = density });
            w.Repaint();
        }
    }
}
