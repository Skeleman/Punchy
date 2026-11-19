using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Punchy.Flora;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-06 row 5 (#414): blueprint buildings. A blueprint is W-175's unit (UnitDef, docs/agents/parts-and-units.md):
    /// a list of part library parts (FloraGenerator.PartIds) placed in a frame of shares plus metres. The Lab lists the
    /// coded units and saved blueprints as "Blueprint/&lt;id&gt;" set pieces, grows the one being edited with the game's
    /// own builder (FloraGenerator.GrowUnit) on a host species (its size, colours and finish) beside the 1.8 m person,
    /// and saves it as JSON in <see cref="BlueprintFolder"/>. A species whose Unit is set grows the same thing
    /// (UnitBlueprintTests). Nothing here puts a blueprint into the world: placement lists are the world lane's call.
    /// </summary>
    public partial class ModelLab
    {
        const string BlueprintPrefix = "Blueprint/";
        /// <summary>Where the Lab saves blueprints (UnitDef JSON, one per id).</summary>
        public const string BlueprintFolder = "Assets/Game/Flora/Units";

        UnitDef blueprint;            // the working copy being edited (kept across rebuilds)
        string blueprintSource = "";  // the list name it was loaded from
        FloraSpecies blueprintHost;
        int blueprintSelected = -1;
        int blueprintPalette;
        float blueprintNudge = 0.25f;
        Mesh blueprintSelection;      // the selected part grown alone, drawn bright over the dimmed whole
        string blueprintWarning = "";

        static bool IsBlueprintName(string name) => name != null && name.StartsWith(BlueprintPrefix);

        /// <summary>The coded units (FloraGenerator.Units) and the saved blueprints, as "Blueprint/&lt;id&gt;".</summary>
        static IEnumerable<string> BlueprintNames()
        {
            var ids = FloraGenerator.Units.Select(u => u.Id).ToList();
            if (Directory.Exists(BlueprintFolder))
                ids.AddRange(Directory.GetFiles(BlueprintFolder, "*.json").Select(Path.GetFileNameWithoutExtension));
            return ids.Distinct().OrderBy(n => n).Select(n => BlueprintPrefix + n);
        }

        static string BlueprintPath(string id) => $"{BlueprintFolder}/{id}.json";

        /// <summary>A fresh copy of a coded unit or saved blueprint by id, or null.</summary>
        static UnitDef ReadBlueprint(string id)
        {
            var path = BlueprintPath(id);
            UnitDef src = File.Exists(path) ? JsonUtility.FromJson<UnitDef>(File.ReadAllText(path))
                                            : FloraGenerator.Units.FirstOrDefault(u => u.Id == id);
            return src == null ? null : JsonUtility.FromJson<UnitDef>(JsonUtility.ToJson(src));
        }

        /// <summary>Species that can host a unit: every structure, plus fire pits.</summary>
        static IEnumerable<FloraSpecies> HostSpecies() => AssetDatabase.FindAssets("t:" + nameof(FloraSpecies))
            .Select(g => AssetDatabase.LoadAssetAtPath<FloraSpecies>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(s => s != null && (IsStructure(s.Form) || s.Form == FloraForm.FirePit)).OrderBy(s => s.name);

        /// <summary>The species whose coded builder is this unit (a stall's Stall), so a coded unit opens at its own size.</summary>
        static FloraSpecies DefaultHost(string id)
        {
            var form = id switch { "smithy" => FloraForm.Smithy, "tent" => FloraForm.Tent, "fire pit" => FloraForm.FirePit, _ => FloraForm.Stall };
            var hosts = HostSpecies().Where(s => s.Form == form).ToList();
            return hosts.FirstOrDefault(s => string.Equals(s.name, id.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) ?? hosts.FirstOrDefault();
        }

        /// <summary>The settings a part is first placed with: its first use in a coded unit (so it lands somewhere sensible).</summary>
        static UnitPart PaletteDefault(string id)
        {
            foreach (var unit in FloraGenerator.Units)
                foreach (var p in unit.Parts)
                    if (p.Part == id)
                        return p;
            return new UnitPart { Part = id, To = new Vector3(0f, 1f, 0f), Span = new Vector3(0.5f, 0f, 0.5f) };
        }

        // ---------- scripting hooks (shots and checks) ----------

        /// <summary>Opens a coded unit or saved blueprint by id for editing ("stall", or a saved file's name).</summary>
        public static ModelLab LoadBlueprint(string id) => LoadSetPiece(BlueprintPrefix + id);

        /// <summary>Starts a new, empty blueprint with this id on the host species (by name).</summary>
        public static ModelLab NewBlueprint(string id, string host)
        {
            var w = Open();
            w.blueprint = new UnitDef { Id = id };
            w.blueprintSource = BlueprintPrefix + id;
            w.blueprintHost = HostSpecies().FirstOrDefault(s => s.name == host) ?? DefaultHost("stall");
            w.blueprintSelected = -1;
            w.setPieceVariant = 0;
            w.LoadByName(w.nameField = w.blueprintSource);
            return w;
        }

        /// <summary>Sets the host species (by name) the blueprint grows on.</summary>
        public static void BlueprintHost(string host)
        {
            var w = Open();
            w.blueprintHost = HostSpecies().FirstOrDefault(s => s.name == host) ?? w.blueprintHost;
            w.RebuildBlueprint();
        }

        /// <summary>Adds a part from the palette (with its palette defaults) and selects it. Returns its index.</summary>
        public static int BlueprintAdd(string part)
        {
            var w = Open();
            w.AddBlueprintPart(part);
            return w.blueprintSelected;
        }

        /// <summary>Sets one of a part's points: field is At, AtMetres, To, ToMetres, Span or SpanMetres.</summary>
        public static void BlueprintSet(int index, string field, Vector3 value)
        {
            var w = Open();
            if (w.blueprint == null || index < 0 || index >= w.blueprint.Parts.Length)
                return;
            object boxed = w.blueprint.Parts[index];
            typeof(UnitPart).GetField(field).SetValue(boxed, value);
            w.blueprint.Parts[index] = (UnitPart)boxed;
            w.RebuildBlueprint();
        }

        /// <summary>Sets the frame's depth as a share of its width.</summary>
        public static void BlueprintDepth(float depthOfWidth)
        {
            var w = Open();
            if (w.blueprint == null)
                return;
            w.blueprint.DepthOfWidth = depthOfWidth;
            w.RebuildBlueprint();
        }

        /// <summary>Moves a part (its At and To together) by metres.</summary>
        public static void BlueprintMove(int index, Vector3 metres) => Open().MoveBlueprintPart(index, metres);

        public static void BlueprintRemove(int index) => Open().RemoveBlueprintPart(index);

        /// <summary>Selects part i (it draws bright over the dimmed rest; -1 clears).</summary>
        public static void BlueprintSelect(int index)
        {
            var w = Open();
            w.blueprintSelected = w.blueprint != null && index < w.blueprint.Parts.Length ? index : -1;
            w.RebuildBlueprint();
        }

        /// <summary>Saves the blueprint as JSON under its id (or a new id). Returns the asset path, or null.</summary>
        public static string SaveBlueprint(string id = null) => Open().WriteBlueprint(id);

        /// <summary>The blueprint as JSON (exactly what Save writes), or null.</summary>
        public static string BlueprintJson() => Open().blueprint is { } b ? b.ToJson() : null;

        /// <summary>The loaded blueprint's id, host, frame in metres, parts and any warning.</summary>
        public static string BlueprintInfo()
        {
            var w = Open();
            if (w.blueprint == null || !IsBlueprintName(w.loadedName))
                return null;
            return $"{w.blueprint.Id} on {(w.blueprintHost != null ? w.blueprintHost.name : "no host")}: {w.FrameText()}; " +
                   $"{w.blueprint.Parts.Length} parts [{string.Join(", ", w.blueprint.Parts.Select(p => p.Part))}]" +
                   (w.blueprintWarning.Length > 0 ? "; " + w.blueprintWarning : "") + "; " + SetPieceInfo();
        }

        // ---------- building ----------

        /// <summary>Loads the blueprint named (keeping the working copy if it is the one already being edited) and grows it.</summary>
        bool BuildBlueprint(string listName)
        {
            if (blueprint == null || blueprintSource != listName)
            {
                var id = listName.Substring(BlueprintPrefix.Length);
                blueprint = ReadBlueprint(id);
                if (blueprint == null)
                {
                    Debug.LogWarning($"Model Lab: no unit or blueprint called '{id}'.");
                    return false;
                }
                blueprintSource = listName;
                blueprintSelected = -1;
                if (blueprintHost == null || FloraGenerator.Units.Any(u => u.Id == id))
                    blueprintHost = DefaultHost(id);
                // (W-175 row 4: a saved blueprint names the species it was made on.)
                if (!string.IsNullOrEmpty(blueprint.Host))
                    blueprintHost = HostSpecies().FirstOrDefault(s => s.name == blueprint.Host) ?? blueprintHost;
            }
            if (blueprintHost == null)
            {
                Debug.LogWarning("Model Lab: no species to host the blueprint.");
                return false;
            }
            GrowBlueprintMeshes();
            // (An empty blueprint still opens, framed on the person's patch of grid.)
            if (setPieceMeshes.Count == 0)
                bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f));
            return true;
        }

        /// <summary>Grows the working blueprint on its host (finest level of detail) and, apart, its selected part.</summary>
        void GrowBlueprintMeshes()
        {
            blueprintWarning = "";
            var unknown = blueprint.Parts.Select(p => p.Part).Where(id => !FloraGenerator.PartIds.Contains(id)).Distinct().ToList();
            if (unknown.Count > 0)
            {
                blueprintWarning = "not in the part library: " + string.Join(", ", unknown);
                return;
            }
            setPieceVariantCount = math.max(1, blueprintHost.Variants);
            setPieceVariant = math.clamp(setPieceVariant, 0, setPieceVariantCount - 1);
            if (blueprint.Parts.Length == 0)
                return;
            if (!GrowInto(blueprint, null))
                return;
            if (blueprintSelected >= 0 && blueprintSelected < blueprint.Parts.Length)
            {
                // The part grown alone: parts share the variant's random draws, so a seeded part may sit a touch
                // differently from its place in the whole (UnitDef.Parts); its size and place are what matter here.
                var alone = new UnitDef { Id = blueprint.Id, DepthOfWidth = blueprint.DepthOfWidth, Parts = new[] { blueprint.Parts[blueprintSelected] } };
                var keep = bounds;
                if (GrowInto(alone, new Color(1f, 0.72f, 0.2f)))
                {
                    blueprintSelection = setPieceMeshes[setPieceMeshes.Count - 1];
                    setPieceMeshes.RemoveAt(setPieceMeshes.Count - 1);
                }
                bounds = keep;
            }
        }

        bool GrowInto(UnitDef unit, Color? tint)
        {
            FloraModel model;
            try
            {
                model = FloraGenerator.GrowUnit(blueprintHost, unit, setPieceVariant);
            }
            catch (Exception e)
            {
                blueprintWarning = $"growing failed: {e.GetBaseException().Message}";
                return false;
            }
            var before = setPieceMeshes.Count;
            AddShaded(model.Lods[0], tint);
            foreach (var m in model.Lods)
                if (m != null)
                    DestroyImmediate(m);
            return setPieceMeshes.Count > before;
        }

        /// <summary>Regrows after an edit, keeping the camera where it is (the frame only grows the grid if needed).</summary>
        void RebuildBlueprint()
        {
            if (blueprint == null || !IsBlueprintName(loadedName))
                return;
            ClearSetPieceMeshes();
            GrowBlueprintMeshes();
            if (setPieceMeshes.Count > 0)
                frameBounds = bounds;
            var size = frameBounds.size.magnitude;
            var extent = math.max(4f, math.ceil(size * 0.9f / gridStep) * gridStep);
            if (extent > gridExtent)
                gridMesh = Grid(gridMesh, gridStep, gridExtent = extent);
            Repaint();
        }

        void ClearSetPieceMeshes()
        {
            foreach (var m in setPieceMeshes)
                if (m != null)
                    DestroyImmediate(m);
            setPieceMeshes.Clear();
            ClearBlueprintSelection();
        }

        void ClearBlueprintSelection()
        {
            if (blueprintSelection != null)
                DestroyImmediate(blueprintSelection);
            blueprintSelection = null;
        }

        /// <summary>With a part selected, the rest dims and the part alone draws over it.</summary>
        bool DrawBlueprintSelection()
        {
            if (blueprintSelection == null || !IsBlueprintName(loadedName))
                return false;
            dimBlock ??= new MaterialPropertyBlock();
            dimBlock.SetColor("_Color", new Color(0.3f, 0.3f, 0.3f, 1f));
            foreach (var m in setPieceMeshes)
                preview.DrawMesh(m, Matrix4x4.identity, modelMaterial, 0, dimBlock);
            preview.DrawMesh(blueprintSelection, Matrix4x4.identity, modelMaterial, 0);
            return true;
        }

        string FrameText()
        {
            if (blueprintHost == null)
                return "";
            // The frame BuildUnit grows: height from the host's Height range (this variant's draw), width = height x Spread.
            var h = blueprintHost.Height;
            var d = blueprint.DepthOfWidth;
            return $"frame height {h.x:0.0}-{h.y:0.0} m, width {h.x * blueprintHost.Spread:0.0}-{h.y * blueprintHost.Spread:0.0} m, " +
                   $"depth {h.x * blueprintHost.Spread * d:0.0}-{h.y * blueprintHost.Spread * d:0.0} m";
        }

        // ---------- editing ----------

        void AddBlueprintPart(string id)
        {
            if (blueprint == null || !FloraGenerator.PartIds.Contains(id))
                return;
            var parts = blueprint.Parts.ToList();
            parts.Add(PaletteDefault(id));
            blueprint.Parts = parts.ToArray();
            blueprintSelected = parts.Count - 1;
            RebuildBlueprint();
        }

        void RemoveBlueprintPart(int index)
        {
            if (blueprint == null || index < 0 || index >= blueprint.Parts.Length)
                return;
            var parts = blueprint.Parts.ToList();
            parts.RemoveAt(index);
            blueprint.Parts = parts.ToArray();
            blueprintSelected = math.min(blueprintSelected, parts.Count - 1);
            RebuildBlueprint();
        }

        void MoveBlueprintPart(int index, Vector3 metres)
        {
            if (blueprint == null || index < 0 || index >= blueprint.Parts.Length)
                return;
            var p = blueprint.Parts[index];
            p.AtMetres += metres;
            p.ToMetres += metres;
            blueprint.Parts[index] = p;
            RebuildBlueprint();
        }

        void SwapBlueprintParts(int a, int b)
        {
            if (blueprint == null || a < 0 || b < 0 || a >= blueprint.Parts.Length || b >= blueprint.Parts.Length)
                return;
            (blueprint.Parts[a], blueprint.Parts[b]) = (blueprint.Parts[b], blueprint.Parts[a]);
            blueprintSelected = b;
            RebuildBlueprint();
        }

        string WriteBlueprint(string id)
        {
            if (blueprint == null)
                return null;
            id = string.IsNullOrWhiteSpace(id) ? blueprint.Id : id.Trim();
            if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Debug.LogWarning($"Model Lab: '{id}' can't be a blueprint id.");
                return null;
            }
            if (FloraGenerator.Units.Any(u => u.Id == id))
            {
                // (A coded unit's id stays its code's: saving over it would leave two definitions. Save a copy under a new id.)
                Debug.LogWarning($"Model Lab: '{id}' is a coded unit; save the blueprint under a new id.");
                return null;
            }
            blueprint.Id = id;
            if (blueprintHost != null)
                blueprint.Host = blueprintHost.name;
            Directory.CreateDirectory(BlueprintFolder);
            var path = BlueprintPath(id);
            File.WriteAllText(path, blueprint.ToJson().Replace("\r\n", "\n") + "\n");
            AssetDatabase.ImportAsset(path);
            UnitLibrary.Reload();   // W-175 row 6: units nesting this one pick up the edit
            blueprintSource = BlueprintPrefix + id;
            loadedName = nameField = blueprintSource;
            setPieceNames = null;   // (the list picks up the new file)
            return path;
        }

        string blueprintSaveId = "";

        void DrawBlueprintPanel()
        {
            GUILayout.Label("Blueprint", EditorStyles.boldLabel);
            if (blueprint == null)
                return;
            var hosts = HostSpecies().ToArray();
            var hostNames = hosts.Select(s => s.name).ToArray();
            var hi = EditorGUILayout.Popup("Host species", Array.IndexOf(hosts, blueprintHost), hostNames);
            if (hi >= 0 && hosts[hi] != blueprintHost)
            {
                blueprintHost = hosts[hi];
                RebuildBlueprint();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var v = setPieceVariant;
                if (GUILayout.Button("<", GUILayout.Width(24f)))
                    v--;
                v = EditorGUILayout.IntField("Variant", v);
                if (GUILayout.Button(">", GUILayout.Width(24f)))
                    v++;
                if (v != setPieceVariant && v >= 0)
                {
                    setPieceVariant = v;
                    RebuildBlueprint();
                }
            }
            EditorGUI.BeginChangeCheck();
            var depth = EditorGUILayout.Slider("Depth / width", blueprint.DepthOfWidth, 0.1f, 3f);
            if (EditorGUI.EndChangeCheck())
            {
                blueprint.DepthOfWidth = depth;
                RebuildBlueprint();
            }
            EditorGUILayout.LabelField(FrameText() + ". Shares are of this frame; metres add to them. The person is 1.8 m.", EditorStyles.wordWrappedMiniLabel);
            if (blueprintWarning.Length > 0)
                EditorGUILayout.HelpBox(blueprintWarning, MessageType.Warning);

            // The palette: every part in the library.
            var ids = FloraGenerator.PartIds.OrderBy(n => n).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                blueprintPalette = EditorGUILayout.Popup("Palette", math.clamp(blueprintPalette, 0, ids.Length - 1), ids);
                if (GUILayout.Button("Add", GUILayout.Width(44f)))
                    AddBlueprintPart(ids[blueprintPalette]);
            }

            EditorGUILayout.LabelField($"Parts ({blueprint.Parts.Length}, drawn in order)", EditorStyles.miniBoldLabel);
            for (int i = 0; i < blueprint.Parts.Length; i++)
            {
                var selected = i == blueprintSelected;
                if (GUILayout.Toggle(selected, $"{i}: {blueprint.Parts[i].Part}", EditorStyles.miniButton) != selected)
                {
                    blueprintSelected = selected ? -1 : i;
                    RebuildBlueprint();
                    GUIUtility.ExitGUI();
                }
                if (i == blueprintSelected)
                    DrawBlueprintPart(i, ids);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (string.IsNullOrEmpty(blueprintSaveId))
                    blueprintSaveId = FloraGenerator.Units.Any(u => u.Id == blueprint.Id) ? blueprint.Id + " copy" : blueprint.Id;
                blueprintSaveId = EditorGUILayout.TextField("Save as", blueprintSaveId);
                if (GUILayout.Button("Save", GUILayout.Width(50f)) && WriteBlueprint(blueprintSaveId) != null)
                    blueprintSaveId = "";
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Revert"))
                {
                    blueprintSource = "";
                    LoadByName(nameField = BlueprintPrefix + blueprint.Id);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Copy JSON"))
                    EditorGUIUtility.systemCopyBuffer = blueprint.ToJson();
                if (GUILayout.Button(new GUIContent("Snap joins", "W-175 row 6: metre points within 12 cm of another part's point meet it; metres round to 5 cm.")))
                {
                    var joined = blueprint.Snap();
                    ShowNotification(new GUIContent($"{joined} joined"));
                    RebuildBlueprint();
                }
            }
            EditorGUILayout.HelpBox($"Saved as W-175 unit JSON in {BlueprintFolder}. A species whose Unit is set grows this " +
                                    "in place of its form's builder, as does one whose Unit Json is this file; a \"unit\" part " +
                                    "nests another unit by id.", MessageType.Info);
        }

        void DrawBlueprintPart(int i, string[] ids)
        {
            var p = blueprint.Parts[i];
            EditorGUI.BeginChangeCheck();
            var k = EditorGUILayout.Popup("Part", Array.IndexOf(ids, p.Part), ids);
            if (k >= 0)
                p.Part = ids[k];
            if (p.Part == "unit")
                p.Unit = EditorGUILayout.TextField("Unit id", p.Unit);
            p.At = EditorGUILayout.Vector3Field("At (share)", p.At);
            p.AtMetres = EditorGUILayout.Vector3Field("At + m", p.AtMetres);
            p.To = EditorGUILayout.Vector3Field("To (share)", p.To);
            p.ToMetres = EditorGUILayout.Vector3Field("To + m", p.ToMetres);
            p.Span = EditorGUILayout.Vector3Field("Span (share)", p.Span);
            p.SpanMetres = EditorGUILayout.Vector3Field("Span + m", p.SpanMetres);
            if (EditorGUI.EndChangeCheck())
            {
                blueprint.Parts[i] = p;
                RebuildBlueprint();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                blueprintNudge = EditorGUILayout.FloatField("Move (m)", blueprintNudge);
                if (GUILayout.Button("-X", EditorStyles.miniButtonLeft)) MoveBlueprintPart(i, new Vector3(-blueprintNudge, 0f, 0f));
                if (GUILayout.Button("+X", EditorStyles.miniButtonMid)) MoveBlueprintPart(i, new Vector3(blueprintNudge, 0f, 0f));
                if (GUILayout.Button("-Y", EditorStyles.miniButtonMid)) MoveBlueprintPart(i, new Vector3(0f, -blueprintNudge, 0f));
                if (GUILayout.Button("+Y", EditorStyles.miniButtonMid)) MoveBlueprintPart(i, new Vector3(0f, blueprintNudge, 0f));
                if (GUILayout.Button("-Z", EditorStyles.miniButtonMid)) MoveBlueprintPart(i, new Vector3(0f, 0f, -blueprintNudge));
                if (GUILayout.Button("+Z", EditorStyles.miniButtonRight)) MoveBlueprintPart(i, new Vector3(0f, 0f, blueprintNudge));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Up")) SwapBlueprintParts(i, i - 1);
                if (GUILayout.Button("Down")) SwapBlueprintParts(i, i + 1);
                if (GUILayout.Button("Duplicate"))
                {
                    var parts = blueprint.Parts.ToList();
                    parts.Insert(i + 1, p);
                    blueprint.Parts = parts.ToArray();
                    blueprintSelected = i + 1;
                    RebuildBlueprint();
                }
                if (GUILayout.Button("Remove")) RemoveBlueprintPart(i);
            }
        }
    }
}
