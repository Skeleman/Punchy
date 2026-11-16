using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-07 (#260): the map editor (Punchy/Map Editor). Loads the island from the island cache, shows its ground as a
    /// live mesh in the scene view, and paints height edits with data-driven brushes (<see cref="MapBrush"/> assets)
    /// into a sparse edits file (<see cref="IslandEdits"/>). Editor-only for now: the game doesn't read the edits
    /// until the bake hook lands (docs/agents/map-editor.md).
    /// </summary>
    public class MapEditorWindow : EditorWindow
    {
        public const string BrushFolder = "Assets/Game/Scripts/Editor/MapEditor/Brushes";
        /// <summary>Where edits files go by default. Not committed until the bake hook reads them.</summary>
        public const string EditsFolder = "Assets/Game/Resources/WorldEdits";   // M-07 r2b: IslandEdits.bytes here is what the bake applies
        const int UndoDepth = 32;

        MapIsland island;
        readonly MapPreview preview = new MapPreview();
        IslandEdits edits = new IslandEdits();
        string editsPath;
        bool dirty;
        string status = "Not loaded.";

        List<MapBrush> brushes = new List<MapBrush>();
        int selected;
        bool paint;
        float previewSize = 1024f, previewStep = 4f;

        // The stroke in progress: the tiles as they were before it, where the brush is, and the flatten height.
        Dictionary<long, float[]> strokeBefore;
        readonly List<Dictionary<long, float[]>> undo = new List<Dictionary<long, float[]>>();
        bool painting, hasHit, invert;
        Vector3 hit;
        float flattenTarget = float.NaN;
        double lastDab;
        Vector2 scroll;
        UnityEditor.Editor brushEditor;

        [MenuItem("Punchy/Map Editor")]
        public static MapEditorWindow Open()
        {
            var w = GetWindow<MapEditorWindow>("Map Editor");
            w.minSize = new Vector2(300f, 360f);
            return w;
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Unload;
            LoadBrushes();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Unload;
            if (dirty && edits != null && !edits.IsEmpty)
                Debug.LogWarning("[map-editor] closed with unsaved edits");
            Unload();
            if (brushEditor != null) DestroyImmediate(brushEditor);
        }

        // ---- brushes ----

        void LoadBrushes()
        {
            brushes = AssetDatabase.FindAssets("t:" + nameof(MapBrush))
                .Select(g => AssetDatabase.LoadAssetAtPath<MapBrush>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(b => b != null).OrderBy(b => b.Order).ThenBy(b => b.name).ToList();
            if (brushes.Count == 0)
            {
                // First run: the four starting brushes, as assets so they can be retuned or added to.
                Directory.CreateDirectory(BrushFolder);
                foreach (var b in MapBrush.Defaults())
                    AssetDatabase.CreateAsset(b, $"{BrushFolder}/{b.name}.asset");
                AssetDatabase.SaveAssets();
                brushes = MapBrush.Defaults().Select(b => AssetDatabase.LoadAssetAtPath<MapBrush>($"{BrushFolder}/{b.name}.asset")).Where(b => b != null).ToList();
            }
            selected = math.clamp(selected, 0, math.max(0, brushes.Count - 1));
        }

        MapBrush Brush => selected < brushes.Count ? brushes[selected] : null;

        // ---- load / unload ----

        bool Load(bool interactive)
        {
            if (interactive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            // A fresh untitled scene with a light, so the game scene is never touched and the preview is lit.
            if (!AnySceneDirty()) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Unload();
            island = MapIsland.Load(out var error);
            if (island == null) { status = "Load failed: " + error; Debug.LogWarning("[map-editor] " + status); return false; }
            if (edits == null || edits.IsEmpty) edits = new IslandEdits();
            edits.IslandVersion = island.Version;
            status = island.Report;
            Debug.Log("[map-editor] " + status);
            BuildPreview(SceneCentre());
            Frame();
            return true;
        }

        static bool AnySceneDirty()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return true;
            return false;
        }

        void Unload()
        {
            painting = false; strokeBefore = null;
            preview.Dispose();
            island?.Dispose();
            island = null;
        }

        static float2 SceneCentre()
        {
            var sv = SceneView.lastActiveSceneView;
            return sv != null ? new float2(sv.pivot.x, sv.pivot.z) : float2.zero;
        }

        void BuildPreview(float2 centre)
        {
            if (island == null) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            preview.Build(island, edits, centre, previewSize, previewStep);
            Debug.Log($"[map-editor] preview {preview.Resolution}x{preview.Resolution} at {preview.Step} m in {watch.ElapsedMilliseconds} ms");
        }

        void Frame()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || !preview.Built) return;
            var c = preview.Origin + preview.Size * 0.5f;
            float h = preview.HeightAt(c);
            sv.LookAt(new Vector3(c.x, float.IsNaN(h) ? 0f : h, c.y), Quaternion.Euler(50f, 30f, 0f), preview.Size * 0.6f);
        }

        // ---- the window ----

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Island", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(island == null ? "Load island" : "Reload island")) Load(true);
                using (new EditorGUI.DisabledScope(island == null))
                    if (GUILayout.Button("Unload")) { Unload(); status = "Not loaded."; SceneView.RepaintAll(); }
            }
            EditorGUILayout.HelpBox(status, MessageType.None);

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            previewSize = EditorGUILayout.Slider("Size (m)", previewSize, 256f, 4096f);
            previewStep = EditorGUILayout.Slider("Vertex step (m)", previewStep, 2f, 16f);
            using (new EditorGUI.DisabledScope(island == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Centre on scene view")) { BuildPreview(SceneCentre()); SceneView.RepaintAll(); }
                if (GUILayout.Button("Frame")) Frame();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(island == null))
                paint = GUILayout.Toggle(paint, paint ? "Painting (click-drag in the scene view)" : "Paint", "Button");
            if (brushes.Count > 0)
            {
                selected = GUILayout.Toolbar(selected, brushes.Select(b => b.Label).ToArray());
                if (Brush != null)
                {
                    UnityEditor.Editor.CreateCachedEditor(Brush, null, ref brushEditor);
                    brushEditor.OnInspectorGUI();
                }
            }
            if (GUILayout.Button("Refresh brush list")) LoadBrushes();
            EditorGUILayout.HelpBox("Shift: the other way (raise lowers). Ctrl-click: pick the flatten height. Ctrl+Z here: undo stroke.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Edits", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(editsPath ?? "(untitled)", $"{edits.TileCount} tiles{(dirty ? ", unsaved" : "")}");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("New") && ConfirmDiscard()) { edits = new IslandEdits { IslandVersion = island?.Version ?? 0 }; editsPath = null; dirty = false; undo.Clear(); AfterBigChange(); }
                if (GUILayout.Button("Load...") && ConfirmDiscard()) LoadEdits();
                if (GUILayout.Button("Save")) SaveEdits(editsPath);
                if (GUILayout.Button("Save as...")) SaveEdits(null);
            }
            using (new EditorGUI.DisabledScope(undo.Count == 0))
                if (GUILayout.Button($"Undo stroke ({undo.Count})")) UndoStroke();
            EditorGUILayout.EndScrollView();

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Z && (e.control || e.command)) { UndoStroke(); e.Use(); }
        }

        bool ConfirmDiscard() => !dirty || edits.IsEmpty || EditorUtility.DisplayDialog("Map editor", "Discard the unsaved edits?", "Discard", "Cancel");

        void AfterBigChange()
        {
            preview.RefreshAll(edits);
            SceneView.RepaintAll();
            Repaint();
        }

        void LoadEdits()
        {
            var path = EditorUtility.OpenFilePanel("Load map edits", EditsFolder, "bytes");
            if (string.IsNullOrEmpty(path)) return;
            var loaded = IslandEdits.Load(path, out var reason);
            if (loaded == null) { EditorUtility.DisplayDialog("Map editor", "Not loaded: " + reason, "OK"); return; }
            if (island != null && loaded.IslandVersion != island.Version)
                Debug.LogWarning($"[map-edits] {Path.GetFileName(path)} was painted on island v{loaded.IslandVersion}, this is v{island.Version}");
            edits = loaded; editsPath = path; dirty = false; undo.Clear();
            Debug.Log($"[map-editor] loaded {path}: {reason}, hash {edits.ContentHash().Substring(0, 12)}");
            AfterBigChange();
        }

        void SaveEdits(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Directory.CreateDirectory(EditsFolder);
                path = EditorUtility.SaveFilePanel("Save map edits", EditsFolder, "IslandEdits", "bytes");
                if (string.IsNullOrEmpty(path)) return;
            }
            edits.Save(path);
            editsPath = path; dirty = false;
            Debug.Log($"[map-editor] saved {path}: {edits.TileCount} tiles, hash {edits.ContentHash().Substring(0, 12)}");
            if (path.Replace('\\', '/').Contains("/Assets/")) AssetDatabase.Refresh();
            Repaint();
        }

        void UndoStroke()
        {
            if (undo.Count == 0) return;
            edits.Restore(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
            dirty = true;
            AfterBigChange();
        }

        // ---- painting ----

        void OnSceneGUI(SceneView sv)
        {
            if (island == null || !preview.Built) return;
            if (Event.current.type == EventType.Repaint) preview.DrawTiles(edits);
            if (!paint || Brush == null) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
            {
                hasHit = preview.Raycast(HandleUtility.GUIPointToWorldRay(e.mousePosition), out hit);
                invert = e.shift;
                sv.Repaint();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (hasHit && (e.control || e.command))
                    flattenTarget = hit.y; // pick the flatten height only
                else if (hasHit)
                    BeginStroke();
                GUIUtility.hotControl = id;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id) e.Use();
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                EndStroke();
                GUIUtility.hotControl = 0;
                e.Use();
            }
            if (e.type == EventType.Repaint && hasHit)
            {
                var b = Brush;
                Handles.color = invert ? new Color(1f, 0.4f, 0.3f) : new Color(0.4f, 0.9f, 1f);
                Handles.DrawWireDisc(hit, Vector3.up, b.Radius);
                Handles.DrawWireDisc(hit, Vector3.up, b.Radius * 0.5f);
                if (b.Op == MapBrush.Operation.Flatten && !float.IsNaN(flattenTarget))
                {
                    Handles.color = new Color(1f, 1f, 1f, 0.6f);
                    Handles.DrawWireDisc(new Vector3(hit.x, flattenTarget, hit.z), Vector3.up, b.Radius);
                }
            }
        }

        void BeginStroke()
        {
            painting = true;
            strokeBefore = new Dictionary<long, float[]>();
            // A plain click on flatten takes the height under the brush, unless one was Ctrl-picked.
            if (Brush.Op == MapBrush.Operation.Flatten && float.IsNaN(flattenTarget)) flattenTarget = hit.y;
            lastDab = EditorApplication.timeSinceStartup - 1.0 / 30.0;
            Dab();
        }

        void EndStroke()
        {
            if (!painting) return;
            painting = false;
            if (strokeBefore != null && strokeBefore.Count > 0)
            {
                undo.Add(strokeBefore);
                if (undo.Count > UndoDepth) undo.RemoveAt(0);
            }
            strokeBefore = null;
            Repaint();
        }

        /// <summary>While the mouse is held, the brush works every editor tick, moving or not.</summary>
        void Tick()
        {
            if (painting && hasHit && island != null) Dab();
        }

        void Dab()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)math.clamp(now - lastDab, 0.0, 0.1);
            lastDab = now;
            if (dt <= 0f) return;
            var region = ApplyBrush(Brush, new float2(hit.x, hit.z), dt, flattenTarget, invert, strokeBefore);
            preview.Refresh(edits, region.min, region.max);
            hit.y = preview.HeightAt(new float2(hit.x, hit.z));
            dirty = true;
            SceneView.RepaintAll();
        }

        /// <summary>One brush step: fetch the ground it needs, keep the tiles as they were for undo, apply.</summary>
        (float2 min, float2 max) ApplyBrush(MapBrush brush, float2 centre, float dt, float target, bool flip, Dictionary<long, float[]> before)
        {
            float s = IslandEdits.Spacing;
            var lo = (int2)math.floor((centre - brush.Radius) / s) - 1;
            var hi = (int2)math.ceil((centre + brush.Radius) / s) + 1;
            island.Prefetch(lo, hi);
            if (before != null) IslandEdits.Merge(before, edits.Snapshot((float2)lo * s, (float2)hi * s));
            if (float.IsNaN(target)) target = island.Ground((int)math.round(centre.x / s), (int)math.round(centre.y / s)) + edits.Sample(centre);
            return brush.Apply(edits, island.Ground, centre, dt, target, flip);
        }

        // ---- checks and shots (for `unity command eval`) ----

        /// <summary>
        /// Loads the island, paints raise / lower / smooth / flatten strokes on land near the middle, saves and reloads the
        /// edits, checks the round trip (half-float tolerance), the stable empty hash and undo. Writes the result to
        /// Temp/mapeditor-check.txt and returns it: "PASS ..." or "FAIL ...".
        /// </summary>
        public static string Check()
        {
            var lines = new List<string>();
            string result;
            var w = Open();
            try
            {
                w.edits = new IslandEdits();
                w.undo.Clear();
                if (!w.Load(false)) throw new Exception(w.status);
                lines.Add(w.status);
                if (w.brushes.Count < 4) throw new Exception($"{w.brushes.Count} brushes");
                lines.Add("brushes: " + string.Join(", ", w.brushes.Select(b => $"{b.Label} ({b.Op}, sign {b.Sign})")));

                // Empty edits: the same hash every time, and an empty file.
                string emptyHash = new IslandEdits().ContentHash();
                if (emptyHash != new IslandEdits { IslandVersion = 0 }.ContentHash()) throw new Exception("empty hash unstable");
                lines.Add("empty hash " + emptyHash.Substring(0, 12) + ", " + new IslandEdits().ToBytes().Length + " bytes");

                // Land near the middle of the preview, well above the sea.
                var spot = w.FindLand();
                if (float.IsNaN(spot.x)) throw new Exception("no land in the preview");
                float before = w.preview.HeightAt(spot);
                lines.Add($"spot ({spot.x:F0}, {spot.y:F0}) ground {before:F2}");
                MapBrush Find(string label) => w.brushes.First(b => b.Label == label);
                var raise = Find("Raise");
                var stroke = new Dictionary<long, float[]>();
                for (int i = 0; i < 30; i++) w.ApplyBrush(raise, spot, 0.05f, float.NaN, false, stroke);
                w.undo.Add(stroke);
                w.preview.RefreshAll(w.edits);
                float raised = w.preview.HeightAt(spot);
                lines.Add($"raise 1.5 s: +{raised - before:F2} m (expect ~{raise.Strength * 1.5f:F1}), {w.edits.TileCount} tiles");
                if (raised - before < raise.Strength * 1.5f * 0.8f) throw new Exception("raise too small");

                var lower = Find("Lower");
                var spot2 = spot + new float2(80f, 0f);
                float before2 = w.preview.HeightAt(spot2);
                for (int i = 0; i < 20; i++) w.ApplyBrush(lower, spot2, 0.05f, float.NaN, false, null);
                w.preview.RefreshAll(w.edits);
                lines.Add($"lower 1 s: {w.preview.HeightAt(spot2) - before2:F2} m");
                if (w.preview.HeightAt(spot2) - before2 > -lower.Strength * 0.8f) throw new Exception("lower too small");

                // Smooth takes the bump's peak down; flatten pulls it toward the ground around it.
                var smooth = Find("Smooth");
                for (int i = 0; i < 40; i++) w.ApplyBrush(smooth, spot, 0.05f, float.NaN, false, null);
                w.preview.RefreshAll(w.edits);
                float smoothed = w.preview.HeightAt(spot);
                lines.Add($"smooth 2 s: peak {raised - before:F2} -> {smoothed - before:F2} m");
                if (!(smoothed < raised)) throw new Exception("smooth did nothing");
                var flatten = Find("Flatten");
                for (int i = 0; i < 40; i++) w.ApplyBrush(flatten, spot, 0.05f, before, false, null);
                w.preview.RefreshAll(w.edits);
                float flat = w.preview.HeightAt(spot);
                lines.Add($"flatten 2 s to {before:F2}: {flat:F2} m");
                if (math.abs(flat - before) > math.abs(smoothed - before)) throw new Exception("flatten moved away from the target");

                // Save, reload, compare.
                Directory.CreateDirectory("Temp");
                const string path = "Temp/mapeditor-check.bytes";
                w.edits.Save(path);
                string hash = w.edits.ContentHash();
                var loaded = IslandEdits.Load(path, out var reason);
                if (loaded == null) throw new Exception("reload failed: " + reason);
                float worst = 0f;
                foreach (var t in w.edits.Tiles())
                    for (int z = 0; z < IslandEdits.TileVerts; z++)
                        for (int x = 0; x < IslandEdits.TileVerts; x++)
                        {
                            int gx = t.x * IslandEdits.TileVerts + x, gz = t.y * IslandEdits.TileVerts + z;
                            float a = w.edits.Vertex(gx, gz), b = loaded.Vertex(gx, gz);
                            worst = math.max(worst, math.abs(a - b) / math.max(1f, math.abs(a)));
                        }
                lines.Add($"saved {new FileInfo(path).Length} bytes, {loaded.TileCount} tiles ({reason}), worst relative error {worst:E1}, hash {hash.Substring(0, 12)}");
                if (loaded.TileCount != w.edits.TileCount || worst > 1e-3f) throw new Exception("round trip differs");
                if (loaded.ContentHash() != hash) throw new Exception("hash changed on reload");
                var bytes = File.ReadAllBytes(path);
                bytes[bytes.Length / 2] ^= 1;
                if (IslandEdits.FromBytes(bytes, out var why) != null) throw new Exception("a damaged file loaded");
                lines.Add("damaged file refused: " + why);

                // Undo the raise stroke: only the other strokes' changes stay.
                w.UndoStroke();
                lines.Add($"undo raise: {w.edits.TileCount} tiles, spot {w.preview.HeightAt(spot) - before:F2} m from the ground");
                result = "PASS\n" + string.Join("\n", lines);
            }
            catch (Exception e)
            {
                result = "FAIL " + e.Message + "\n" + string.Join("\n", lines);
            }
            File.WriteAllText("Temp/mapeditor-check.txt", result);
            Debug.Log("[map-editor] check " + result);
            return result;
        }

        /// <summary>A point of land near the preview's middle, at least 10 m above the sea (NaN if none).</summary>
        float2 FindLand()
        {
            var c = preview.Origin + preview.Size * 0.5f;
            for (float r = 0f; r < preview.Size * 0.45f; r += 16f)
                for (int a = 0; a < 16; a++)
                {
                    var p = c + r * new float2(math.cos(a * math.PI / 8f), math.sin(a * math.PI / 8f));
                    float h = preview.HeightAt(p);
                    if (!float.IsNaN(h) && h > island.SeaLevel + 10f && !float.IsNaN(preview.HeightAt(p + 120f))) return p;
                    if (r == 0f) break;
                }
            return new float2(float.NaN);
        }

        /// <summary>Renders the preview from above at an angle to a PNG (no scene view needed).</summary>
        public static string Shot(string path, float2 centre, float distance, float pitch = 45f, float yaw = 30f, int width = 1600, int height = 900)
        {
            var w = Open();
            if (w.island == null || !w.preview.Built) return "not loaded";
            float h = w.preview.HeightAt(centre);
            var target = new Vector3(centre.x, float.IsNaN(h) ? 0f : h, centre.y);
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var go = new GameObject("MapEditorShot") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(target - rot * Vector3.forward * distance, rot);
                cam.fieldOfView = 50f; cam.nearClipPlane = 1f; cam.farClipPlane = distance * 6f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                DestroyImmediate(tex);
                return "ok " + path;
            }
            finally
            {
                DestroyImmediate(go);
                rt.Release(); DestroyImmediate(rt);
            }
        }

        /// <summary>For eval: the land spot <see cref="Check"/> paints on, so shots can frame it.</summary>
        public static float2 CheckSpot() => Open().FindLand();
    }
}
