using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Punchy.Fauna;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-05 (Zach, 11-10): the Model Lab, an editor window for tuning how creatures and colossi are proportioned and how
    /// creatures move, without touching the game code. Load any creature species or colossus kind by name onto a
    /// turntable (with a grid and a 1.8 m person for scale), move the proportion sliders and the model rebuilds live,
    /// and play, pause and scrub its idle, walk or run cycle at any speed while nudging its key poses.
    /// <para>
    /// Everything it changes lives in one asset, Resources/CreatureTunables (<see cref="CreatureTunables"/>), which the
    /// game reads when it bakes a creature. Save writes that asset (a small, reviewable diff; entries left at their
    /// defaults are dropped), Reset puts the loaded model (or everything) back to the defaults. Nothing here ships.
    /// </para>
    /// <para>
    /// Drawn with <see cref="PreviewRenderUtility"/>, not the game's instanced creature renderer: the renderer draws
    /// from an ECS-filled structured buffer, which a preview camera has no system to fill. Instead the Lab runs the same
    /// pose code (<see cref="CreatureAnimator.Evaluate"/>) on a blob baked by the same builder, and turns each posed
    /// segment into a tapered, flattened capsule on the CPU (the shape Creature.shader draws), in the segment colours,
    /// lit in the vertex colours and drawn unlit (the game's lit shaders add the island's haze). Colossi are built by their own builders (<see cref="StoneColossi.BuilderFor"/>) into
    /// the preview scene, in edit mode only, since builders set their kind's static rig fields.
    /// </para>
    /// </summary>
    public class ModelLab : EditorWindow
    {
        enum Family { Creature, Colossus }
        enum Gait { Idle, Walk, Run }

        const string AssetPath = "Assets/Game/Resources/" + CreatureTunables.ResourcePath + ".asset";

        PreviewRenderUtility preview;
        Material modelMaterial, lineMaterial;
        Mesh modelMesh, humanMesh, gridMesh;
        float gridStep, gridExtent;

        Family family;
        string loadedName = "";
        string nameField = "Wolf";
        string[] speciesNames = new string[0];
        CreatureSpecies species;
        BlobAssetReference<CreatureBlob> blob;
        GameObject colossus;
        Transform trunk;
        Bounds bounds;
        float ground;
        TerrainSource flat;   // default: no baked world; the samplers return flat ground (see TerrainSource.ShapeHeight)

        Gait gait = Gait.Walk;
        bool playing = true, spin = true;
        float phase, clock, playSpeed = 1f;
        double lastTick;
        float orbitYaw = 90f, orbitPitch = 12f, zoom = 1f;

        int nudgeSlot, nudgeSegment, nudgeKey = 1;
        Vector2 scroll;

        [MenuItem("Punchy/Model Lab")]
        public static ModelLab Open()
        {
            var w = GetWindow<ModelLab>("Model Lab");
            w.minSize = new Vector2(760f, 560f);
            return w;
        }

        // ---------- scripting hooks (shots, checks via the editor CLI) ----------

        /// <summary>Loads a creature species or colossus kind by name (colossi win only when no species has it).</summary>
        public static ModelLab Load(string name)
        {
            var w = Open();
            w.LoadByName(w.nameField = name);
            return w;
        }

        /// <summary>Sets the gait (Idle, Walk, Run), the cycle position (0-1) and whether it plays.</summary>
        public static void Pose(string gaitName, float cycle, bool play)
        {
            var w = Open();
            w.gait = (Gait)Enum.Parse(typeof(Gait), gaitName);
            w.SetCycle(cycle);
            w.playing = play;
            w.spin = false;
            w.Repaint();
        }

        /// <summary>Sets one proportion slider of the loaded creature (a CreatureTune field name) or the colossus sway, and rebuilds.</summary>
        public static void SetSlider(string field, float value)
        {
            var w = Open();
            if (w.family == Family.Colossus)
            {
                var c = w.ColossusEntry(true);
                if (field == nameof(ColossusTune.Sway))
                    c.Sway = value;
            }
            else
            {
                var f = typeof(CreatureTune).GetField(field);
                f.SetValue(w.CreatureEntry(true), value);
            }
            w.Rebuild();
        }

        /// <summary>Saves the tunables asset, as the Save button does.</summary>
        public static void SaveAsset() => Open().Save();

        /// <summary>Resets everything to the defaults, as the Reset all button does.</summary>
        public static void ResetAll() => Open().ResetTunables(true);

        /// <summary>Writes the whole window, as drawn, to a PNG (read from its view's own back buffer, so it works with the
        /// editor minimised); false when it couldn't be read.</summary>
        public static bool CaptureWindow(string path)
        {
            var w = Open();
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var host = typeof(EditorWindow).GetField("m_Parent", any)?.GetValue(w);
            var grab = host?.GetType().GetMethod("GrabPixels", any);
            if (grab == null)
                return false;
            // Paint it now, as it stands (a Repaint would only queue one, and the buffer would hold the last).
            typeof(EditorWindow).GetMethod("RepaintImmediately", any)?.Invoke(w, null);
            // (The buffer is in pixels; the window's rect in points.)
            var ppp = EditorGUIUtility.pixelsPerPoint;
            int width = Mathf.RoundToInt(w.position.width * ppp), height = Mathf.RoundToInt(w.position.height * ppp);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var hostPos = (Rect)host.GetType().GetProperty("position", any).GetValue(host);
            // (The view's rect includes the tab strip above the window's own area.)
            var rect = new Rect(0f, hostPos.height * ppp - height, width, height);
            grab.Invoke(host, new object[] { rt, rect });
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            RenderTexture.active = old;
            // (The view's buffer is bottom-up: flip it.)
            var rows = tex.GetPixels32();
            var flipped = new Color32[rows.Length];
            for (int y = 0; y < height; y++)
                Array.Copy(rows, y * width, flipped, (height - 1 - y) * width, width);
            tex.SetPixels32(flipped);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
            rt.Release();
            DestroyImmediate(rt);
            return true;
        }

        /// <summary>Renders only the turntable at the given size to a PNG.</summary>
        public static void CapturePreview(string path, int width, int height)
        {
            var w = Open();
            w.EnsurePreview();
            var rect = new Rect(0f, 0f, width, height);
            w.preview.BeginStaticPreview(rect);
            w.DrawScene();
            var tex = w.preview.EndStaticPreview();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
        }

        // ---------- lifecycle ----------

        void OnEnable()
        {
            speciesNames = AssetDatabase.FindAssets("t:" + nameof(CreatureSpecies))
                .Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)))
                .Distinct().OrderBy(n => n).ToArray();
            lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayMode;
            if (!string.IsNullOrEmpty(loadedName))
                LoadByName(loadedName);
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Unload();
            if (preview != null)
                preview.Cleanup();
            preview = null;
            foreach (var o in new UnityEngine.Object[] { modelMesh, humanMesh, gridMesh, modelMaterial, lineMaterial })
                if (o != null)
                    DestroyImmediate(o);
        }

        void OnPlayMode(PlayModeStateChange change)
        {
            // Colossus builders set static rig fields the running game uses: never leave a Lab copy built across play mode.
            if (change == PlayModeStateChange.ExitingEditMode && family == Family.Colossus)
                Unload();
        }

        void EnsurePreview()
        {
            if (preview != null)
                return;
            preview = new PreviewRenderUtility();
            preview.camera.fieldOfView = 30f;
            preview.camera.nearClipPlane = 0.05f;
            preview.camera.farClipPlane = 5000f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(0.32f, 0.36f, 0.42f);
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            preview.lights[1].intensity = 0.7f;
            preview.ambientColor = new Color(0.35f, 0.38f, 0.42f);
            modelMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            modelMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            modelMaterial.SetInt("_ZWrite", 1);
            modelMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            modelMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            humanMesh = BuildHuman();
        }

        void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt = (float)math.min(now - lastTick, 0.1);
            lastTick = now;
            if (string.IsNullOrEmpty(loadedName))
                return;
            if (spin)
                orbitYaw += dt * 12f;
            if (playing)
                Advance(dt * playSpeed);
            if (playing || spin)
            {
                Rebuild(false);
                Repaint();
            }
        }

        // ---------- loading ----------

        void LoadByName(string name)
        {
            Unload();
            name = name?.Trim() ?? "";
            var guid = AssetDatabase.FindAssets(name + " t:" + nameof(CreatureSpecies))
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);
            if (guid != null)
            {
                family = Family.Creature;
                species = AssetDatabase.LoadAssetAtPath<CreatureSpecies>(AssetDatabase.GUIDToAssetPath(guid));
                loadedName = name;
                nudgeSlot = nudgeSegment = 0;
                Rebuild();
                Frame();
                return;
            }
            if (Enum.TryParse(name, out StoneColossi.Kind kind))
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogWarning("Model Lab: colossi are built in edit mode only (their builders set the game's rig fields).");
                    return;
                }
                family = Family.Colossus;
                loadedName = name;
                BuildColossus(kind);
                Frame();
                return;
            }
            Debug.LogWarning($"Model Lab: no creature species or colossus kind called '{name}'.");
        }

        void BuildColossus(StoneColossi.Kind kind)
        {
            EnsurePreview();
            var build = StoneColossi.BuilderFor(kind);
            if (build == null)
            {
                Debug.LogWarning($"Model Lab: no builder registered for {kind}.");
                return;
            }
            ground = flat.SampleHeight(float2.zero);
            try
            {
                var root = build(in flat, new float3(0f, ground, 0f), 0f, 1234567u);
                colossus = root != null ? root.gameObject : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Model Lab: {kind}'s builder failed outside the game: {e.Message}");
            }
            if (colossus == null)
                return;
            foreach (var t in colossus.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var mb in colossus.GetComponentsInChildren<MonoBehaviour>(true))
                mb.enabled = false;   // (no ticking scripts of the game's in the preview)
            preview.AddSingleGO(colossus);
            trunk = colossus.transform.Find("Trunk") ?? colossus.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Trunk");
            bounds = new Bounds(colossus.transform.position, Vector3.one);
            foreach (var r in colossus.GetComponentsInChildren<Renderer>(true))
                bounds.Encapsulate(r.bounds);
            ApplySway();
        }

        void Unload()
        {
            if (blob.IsCreated)
                blob.Dispose();
            blob = default;
            species = null;
            if (colossus != null)
            {
                foreach (var mf in colossus.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !AssetDatabase.Contains(mf.sharedMesh))
                        DestroyImmediate(mf.sharedMesh);
                DestroyImmediate(colossus);
            }
            colossus = null;
            trunk = null;
            loadedName = "";
        }

        // ---------- the tunables ----------

        CreatureTunables Tunables(bool create)
        {
            var asset = CreatureTunables.Instance;
            if (asset == null)
            {
                asset = AssetDatabase.LoadAssetAtPath<CreatureTunables>(AssetPath);
                if (asset == null && create)
                {
                    asset = CreateInstance<CreatureTunables>();
                    AssetDatabase.CreateAsset(asset, AssetPath);
                }
                CreatureTunables.Reload();
            }
            return asset;
        }

        /// <summary>The loaded creature's entry (keyed by the species it is built on), made when asked for and missing.</summary>
        CreatureTune CreatureEntry(bool create)
        {
            var key = species != null ? species.Body.name : null;
            var asset = Tunables(create);
            if (asset == null || key == null)
                return null;
            var entry = asset.Creatures.FirstOrDefault(t => t != null && t.Species == key);
            if (entry == null && create)
            {
                entry = new CreatureTune { Species = key };
                asset.Creatures.Add(entry);
            }
            return entry;
        }

        ColossusTune ColossusEntry(bool create)
        {
            var asset = Tunables(create);
            if (asset == null)
                return null;
            var entry = asset.Colossi.FirstOrDefault(t => t != null && t.Kind == loadedName);
            if (entry == null && create)
            {
                entry = new ColossusTune { Kind = loadedName };
                asset.Colossi.Add(entry);
            }
            return entry;
        }

        void Save()
        {
            var asset = Tunables(true);
            asset.Creatures.RemoveAll(t => t == null || t.IsDefault);
            asset.Colossi.RemoveAll(t => t == null || t.IsDefault);
            asset.Creatures.Sort((a, b) => string.CompareOrdinal(a.Species, b.Species));
            asset.Colossi.Sort((a, b) => string.CompareOrdinal(a.Kind, b.Kind));
            foreach (var t in asset.Creatures)
                t.Poses.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Segment != b.Segment ? a.Segment.CompareTo(b.Segment) : a.Key.CompareTo(b.Key));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        void ResetTunables(bool all)
        {
            var asset = Tunables(false);
            if (asset == null)
                return;
            Undo.RecordObject(asset, all ? "Model Lab reset all" : "Model Lab reset");
            if (all)
            {
                asset.Creatures.Clear();
                asset.Colossi.Clear();
            }
            else if (family == Family.Creature && species != null)
                asset.Creatures.RemoveAll(t => t != null && t.Species == species.Body.name);
            else
                asset.Colossi.RemoveAll(t => t != null && t.Kind == loadedName);
            EditorUtility.SetDirty(asset);
            Rebuild();
        }

        // ---------- the model ----------

        /// <summary>Re-bakes the creature from its assets and the tunables (when <paramref name="rebake"/>), then re-poses and remeshes it.</summary>
        void Rebuild(bool rebake = true)
        {
            EnsurePreview();
            if (family == Family.Colossus)
            {
                ApplySway();
                return;
            }
            if (species == null)
                return;
            if (rebake || !blob.IsCreated)
            {
                if (blob.IsCreated)
                    blob.Dispose();
                blob = CreatureBlobBuilder.Build(species);
            }
            ground = flat.SampleHeight(float2.zero);
            ref var b = ref blob.Value;
            var output = new NativeArray<SegmentInstance>(b.Segments.Length, Allocator.Temp);
            try
            {
                var s = State(ref b);
                CreatureAnimator.Evaluate(ref b, in s, 0, in flat, output, 0);
                modelMesh = Capsules(output, modelMesh, out bounds);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Model Lab: posing {loadedName} failed: {e.Message}");
            }
            finally
            {
                output.Dispose();
            }
        }

        CreatureState State(ref CreatureBlob b)
        {
            var speed = gait == Gait.Walk ? b.WalkSpeed : gait == Gait.Run ? b.RunSpeed : 0f;
            return new CreatureState
            {
                Position = new float3(0f, ground, 0f),
                Yaw = 0f,
                Scale = 1f,
                Speed = speed,
                GaitSpeed = speed,
                Phase = phase,
                Clock = clock,
                Mode = gait == Gait.Idle ? CreatureMode.Idle : CreatureMode.Wander,
                Seed = 1u
            };
        }

        /// <summary>Moves the cycle on by <paramref name="dt"/> seconds of game time: the walk and run by the distance covered, as CreatureMotion steps it.</summary>
        void Advance(float dt)
        {
            clock += dt;
            if (family == Family.Creature && blob.IsCreated && gait != Gait.Idle)
            {
                ref var b = ref blob.Value;
                var s = State(ref b);
                phase = math.frac(phase + CreatureMotion.StepPhase(ref b, in s, s.Speed * dt));
            }
        }

        /// <summary>Puts the cycle at <paramref name="cycle"/> (0-1): the stride phase for walk and run, a 0-20 s span of the clock otherwise.</summary>
        void SetCycle(float cycle)
        {
            if (family == Family.Creature && gait != Gait.Idle)
                phase = math.frac(cycle);
            else
                clock = cycle * 20f;
            Rebuild(false);
        }

        float Cycle => family == Family.Creature && gait != Gait.Idle ? phase : clock / 20f % 1f;

        void ApplySway()
        {
            if (trunk == null)
                return;
            var sway = ColossusEntry(false)?.Sway ?? 1f;
            trunk.localRotation = StoneColossi.MammothTrunkPose(clock, sway);
        }

        void Frame()
        {
            zoom = 1f;
            orbitYaw = 90f;
            var size = bounds.size.magnitude;
            gridStep = size > 40f ? 5f : size > 8f ? 1f : 0.25f;
            gridExtent = math.max(4f, math.ceil(size * 0.9f / gridStep) * gridStep);
            gridMesh = Grid(gridMesh, gridStep, gridExtent);
        }

        // ---------- drawing ----------

        void DrawScene()
        {
            var cam = preview.camera;
            // Frame the model and the person together, so the scale reference is never cut off.
            // Behind a creature's tail; in front of a colossus (tiny beside it, and its rubble would hide it behind).
            var person = family == Family.Creature
                ? new Vector3(bounds.center.x, ground, bounds.min.z - 0.6f)
                : new Vector3(bounds.min.x - 2f, ground, bounds.center.z);
            var framed = bounds;
            framed.Encapsulate(person + new Vector3(0f, 1.8f, -0.3f));
            var centre = framed.center;
            var radius = math.max(framed.extents.magnitude, 1.2f);
            var dist = radius / math.sin(math.radians(cam.fieldOfView * 0.5f)) * zoom;
            var rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            cam.transform.position = centre - rot * Vector3.forward * dist;
            cam.transform.rotation = rot;
            cam.farClipPlane = dist * 4f + 100f;

            if (gridMesh != null)
                preview.DrawMesh(gridMesh, Matrix4x4.Translate(new Vector3(0f, ground + 0.002f, 0f)), lineMaterial, 0);
            if (family == Family.Creature && modelMesh != null)
                preview.DrawMesh(modelMesh, Matrix4x4.identity, modelMaterial, 0);
            preview.DrawMesh(humanMesh, Matrix4x4.Translate(person), modelMaterial, 0);
            preview.Render(true);
        }

        void OnGUI()
        {
            EnsurePreview();
            const float panel = 300f;
            var view = new Rect(0f, 0f, position.width - panel, position.height);
            var side = new Rect(position.width - panel, 0f, panel, position.height);

            DrawPreview(view);
            GUILayout.BeginArea(new Rect(side.x + 6f, side.y + 6f, side.width - 12f, side.height - 12f));
            scroll = GUILayout.BeginScrollView(scroll);
            DrawLoadPanel();
            if (!string.IsNullOrEmpty(loadedName))
            {
                if (family == Family.Creature)
                    DrawCreatureSliders();
                else
                    DrawColossusSliders();
                DrawSaveReset();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPreview(Rect view)
        {
            const float timeline = 92f;
            var scene = new Rect(view.x, view.y, view.width, view.height - timeline);
            var e = Event.current;
            if (scene.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDrag)
                {
                    orbitYaw += e.delta.x * 0.5f;
                    orbitPitch = math.clamp(orbitPitch + e.delta.y * 0.3f, -5f, 80f);
                    spin = false;
                    e.Use();
                }
                else if (e.type == EventType.ScrollWheel)
                {
                    zoom = math.clamp(zoom * (1f + e.delta.y * 0.05f), 0.2f, 4f);
                    e.Use();
                }
            }
            if (e.type == EventType.Repaint)
            {
                if (string.IsNullOrEmpty(loadedName))
                {
                    EditorGUI.DrawRect(scene, new Color(0.2f, 0.2f, 0.22f));
                    GUI.Label(new Rect(scene.x + 12f, scene.y + 12f, 400f, 20f), "Load a creature species or colossus kind (right).", EditorStyles.whiteLabel);
                }
                else
                {
                    preview.BeginPreview(scene, GUIStyle.none);
                    DrawScene();
                    GUI.DrawTexture(scene, preview.EndPreview(), ScaleMode.StretchToFill, false);
                    var info = $"{loadedName}   grid {gridStep:0.##} m   person 1.8 m   " +
                               (family == Family.Creature ? $"length {bounds.size.z:0.00} m  height {bounds.max.y - ground:0.00} m" : $"height {bounds.max.y - ground:0} m");
                    GUI.Label(new Rect(scene.x + 10f, scene.y + 8f, scene.width - 20f, 20f), info, EditorStyles.whiteBoldLabel);
                }
            }

            GUILayout.BeginArea(new Rect(view.x + 8f, view.yMax - timeline + 4f, view.width - 16f, timeline - 8f));
            DrawTimeline();
            GUILayout.EndArea();
        }

        void DrawTimeline()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(60f)))
                    playing = !playing;
                using (new EditorGUI.DisabledScope(family == Family.Colossus))
                {
                    var g = (Gait)EditorGUILayout.EnumPopup(gait, GUILayout.Width(70f));
                    if (g != gait)
                    {
                        gait = g;
                        Rebuild(false);
                    }
                }
                GUILayout.Label("Speed", GUILayout.Width(42f));
                playSpeed = EditorGUILayout.Slider(playSpeed, 0.05f, 3f, GUILayout.Width(180f));
                spin = GUILayout.Toggle(spin, "Turntable", GUILayout.Width(80f));
                if (GUILayout.Button("Frame", GUILayout.Width(52f)))
                    Frame();
            }
            var label = family == Family.Creature && gait != Gait.Idle ? "Stride (one full cycle; loops)" : "Clock (0-20 s; loops)";
            GUILayout.Label(label, EditorStyles.miniLabel);
            var rect = GUILayoutUtility.GetRect(100f, 22f, GUILayout.ExpandWidth(true));
            DrawKeyTicks(rect);
            EditorGUI.BeginChangeCheck();
            var c = GUI.HorizontalSlider(rect, Cycle, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                playing = false;   // scrubbing pauses
                SetCycle(c);
                Repaint();
            }
        }

        /// <summary>Marks on the timeline where the selected limb plants (reach) and lifts (push) in the stride.</summary>
        void DrawKeyTicks(Rect rect)
        {
            if (Event.current.type != EventType.Repaint || family != Family.Creature || !blob.IsCreated || gait == Gait.Idle)
                return;
            ref var b = ref blob.Value;
            for (int p = 0; p < b.Parts.Length; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Leg || part.Parent >= 0)
                    continue;
                // (Each leg's own phase offset: its reach comes when the cycle reaches it.)
                var x = rect.x + math.frac(1f - b.LegPhase(ref part, 0f)) * rect.width;
                EditorGUI.DrawRect(new Rect(x - 1f, rect.y, 2f, rect.height), new Color(1f, 0.75f, 0.2f, 0.8f));
            }
        }

        void DrawLoadPanel()
        {
            GUILayout.Label("Model", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                nameField = EditorGUILayout.TextField(nameField);
                if (GUILayout.Button("Load", GUILayout.Width(50f)))
                    LoadByName(nameField);
            }
            var current = family == Family.Creature ? Array.IndexOf(speciesNames, loadedName) : -1;
            var pick = EditorGUILayout.Popup("Creature", current, speciesNames);
            if (pick >= 0 && pick != current)
                LoadByName(nameField = speciesNames[pick]);
            var kinds = Enum.GetNames(typeof(StoneColossi.Kind));
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var currentKind = family == Family.Colossus ? Array.IndexOf(kinds, loadedName) : -1;
                var k = EditorGUILayout.Popup("Colossus", currentKind, kinds);
                if (k >= 0 && k != currentKind)
                    LoadByName(nameField = kinds[k]);
            }
            EditorGUILayout.Space();
        }

        void DrawCreatureSliders()
        {
            var entry = CreatureEntry(false);
            var shown = entry ?? new CreatureTune();
            if (species.Body != species)
                EditorGUILayout.HelpBox($"A variant: tuning applies to {species.Body.name}, the species it is built on.", MessageType.None);
            GUILayout.Label("Proportions", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var v = new float[9];
            v[0] = EditorGUILayout.Slider("Size", shown.Size, 0.25f, 3f);
            v[1] = EditorGUILayout.Slider("Body length", shown.BodyLength, 0.4f, 2.5f);
            v[2] = EditorGUILayout.Slider("Thickness", shown.Thickness, 0.4f, 2.5f);
            v[3] = EditorGUILayout.Slider("Legs", shown.Legs, 0.4f, 2.5f);
            v[4] = EditorGUILayout.Slider("Head", shown.Head, 0.4f, 2.5f);
            v[5] = EditorGUILayout.Slider("Neck", shown.Neck, 0.4f, 2.5f);
            v[6] = EditorGUILayout.Slider("Tail", shown.Tail, 0.2f, 3f);
            GUILayout.Label("Motion", EditorStyles.boldLabel);
            v[7] = EditorGUILayout.Slider("Speed", shown.Speed, 0.25f, 3f);
            v[8] = EditorGUILayout.Slider("Swing", shown.Swing, 0f, 2.5f);
            if (EditorGUI.EndChangeCheck())
            {
                var asset = Tunables(true);
                Undo.RecordObject(asset, "Model Lab");
                entry = CreatureEntry(true);
                entry.Size = v[0]; entry.BodyLength = v[1]; entry.Thickness = v[2]; entry.Legs = v[3]; entry.Head = v[4];
                entry.Neck = v[5]; entry.Tail = v[6]; entry.Speed = v[7]; entry.Swing = v[8];
                EditorUtility.SetDirty(asset);
                Rebuild();
            }
            EditorGUILayout.Space();
            DrawKeyPoses(entry);
        }

        /// <summary>The key-pose editor: pick a limb slot, a segment and a key, and nudge that key's joint angles.</summary>
        void DrawKeyPoses(CreatureTune entry)
        {
            GUILayout.Label("Key poses", EditorStyles.boldLabel);
            var slots = species.Body.Parts;
            if (slots.Count == 0)
                return;
            var names = slots.Select((p, i) => $"{i}: {(p.Limb != null ? p.Limb.name : "(none)")}").ToArray();
            nudgeSlot = math.clamp(EditorGUILayout.Popup("Limb slot", nudgeSlot, names), 0, slots.Count - 1);
            var limb = slots[nudgeSlot].Limb;
            if (limb == null)
                return;
            nudgeSegment = EditorGUILayout.IntSlider("Segment", nudgeSegment, 0, math.max(0, limb.Segments.Count - 1));
            nudgeKey = EditorGUILayout.Popup("Key", nudgeKey, CreatureTunables.PoseNames);
            var keys = new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };
            var baseAngles = keys[nudgeKey] != null ? keys[nudgeKey][nudgeSegment] : Vector3.zero;
            EditorGUILayout.LabelField("As authored", $"{baseAngles.x:0.#}, {baseAngles.y:0.#}, {baseAngles.z:0.#} deg");
            var nudge = Vector3.zero;
            entry?.Nudge(nudgeSlot, nudgeSegment, nudgeKey, out nudge);
            EditorGUI.BeginChangeCheck();
            var n = new Vector3(EditorGUILayout.Slider("Nudge pitch", nudge.x, -60f, 60f),
                                EditorGUILayout.Slider("Nudge yaw", nudge.y, -60f, 60f),
                                EditorGUILayout.Slider("Nudge roll", nudge.z, -60f, 60f));
            if (EditorGUI.EndChangeCheck())
            {
                var asset = Tunables(true);
                Undo.RecordObject(asset, "Model Lab pose");
                entry = CreatureEntry(true);
                entry.Poses.RemoveAll(p => p.Slot == nudgeSlot && p.Segment == nudgeSegment && p.Key == nudgeKey);
                if (n != Vector3.zero)
                    entry.Poses.Add(new PoseNudge { Slot = nudgeSlot, Segment = nudgeSegment, Key = nudgeKey, Degrees = n });
                EditorUtility.SetDirty(asset);
                // Show the key being edited: idle holds still; a walk or run key switches to that gait.
                gait = nudgeKey == 0 ? Gait.Idle : nudgeKey <= 3 ? Gait.Walk : Gait.Run;
                Rebuild();
            }
            EditorGUILayout.HelpBox("Stance sweeps from Reach to Push; the swing eases back from Push to Reach, raised by Lift. The orange ticks mark each leg's reach.", MessageType.None);
        }

        void DrawColossusSliders()
        {
            GUILayout.Label("Colossus", EditorStyles.boldLabel);
            if (colossus == null)
            {
                EditorGUILayout.HelpBox("Its builder could not run outside the game.", MessageType.Warning);
                return;
            }
            if (trunk == null)
            {
                EditorGUILayout.HelpBox("View only: this kind's proportions are still numbers in its builder (StoneColossi." + loadedName + ".cs).", MessageType.Info);
                return;
            }
            var entry = ColossusEntry(false);
            EditorGUI.BeginChangeCheck();
            var sway = EditorGUILayout.Slider("Trunk sway", entry?.Sway ?? 1f, 0f, 3f);
            if (EditorGUI.EndChangeCheck())
            {
                var asset = Tunables(true);
                Undo.RecordObject(asset, "Model Lab sway");
                ColossusEntry(true).Sway = sway;
                EditorUtility.SetDirty(asset);
                ApplySway();
            }
            EditorGUILayout.HelpBox("The body's proportions are still numbers in its builder; the trunk's sway is data.", MessageType.None);
        }

        void DrawSaveReset()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save"))
                    Save();
                if (GUILayout.Button("Reset"))
                    ResetTunables(false);
                if (GUILayout.Button("Reset all"))
                    ResetTunables(true);
            }
            var asset = Tunables(false);
            var dirty = asset != null && EditorUtility.IsDirty(asset);
            EditorGUILayout.LabelField(dirty ? "Unsaved changes (the game bakes what is in memory)." : "Saved.", EditorStyles.miniLabel);
        }

        // ---------- meshes ----------

        /// <summary>One mesh of all posed segments: each a capsule tapered from its start to its end radius and flattened, as Creature.shader draws it.</summary>
        static Mesh Capsules(NativeArray<SegmentInstance> segs, Mesh mesh, out Bounds box)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            box = new Bounds();
            var first = true;
            for (int i = 0; i < segs.Length; i++)
            {
                var s = segs[i];
                if (s.Length <= 0f && s.RadiusStart <= 0f || math.max(s.RadiusStart, s.RadiusEnd) <= 0f)
                    continue;
                var c = new Color(s.Color.x, s.Color.y, s.Color.z, 1f);
                AddCapsule(verts, normals, colors, tris, s.Position, s.Rotation, s.Length, s.RadiusStart, s.RadiusEnd, math.max(s.Flatten, 0.05f), c);
                var p0 = (Vector3)s.Position;
                var p1 = (Vector3)(s.Position + math.mul(s.Rotation, new float3(0f, 0f, s.Length)));
                var r = math.max(s.RadiusStart, s.RadiusEnd);
                if (first)
                {
                    box = new Bounds(p0, Vector3.one * r * 2f);
                    first = false;
                }
                box.Encapsulate(new Bounds(p0, Vector3.one * s.RadiusStart * 2f));
                box.Encapsulate(new Bounds(p1, Vector3.one * r * 2f));
            }
            if (mesh == null)
                mesh = new Mesh { name = "ModelLabCreature", hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddCapsule(List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<int> tris,
                               float3 at, quaternion rot, float length, float r0, float r1, float flatten, Color color)
        {
            const int sides = 12, capRows = 4;
            var start = verts.Count;
            var rows = 0;
            // Rows from the start cap's pole, round the tapered middle, to the end cap's pole: (t along, cap angle).
            for (int k = -capRows; k <= capRows + 1; k++)
            {
                float t, capX, capY;
                if (k <= 0)
                {
                    var a = (float)(-k) / capRows * math.PI * 0.5f;
                    t = 0f; capX = -math.sin(a); capY = math.cos(a);
                }
                else
                {
                    var a = (float)(k - 1) / capRows * math.PI * 0.5f;
                    t = 1f; capX = math.sin(a); capY = math.cos(a);
                }
                var r = math.lerp(r0, r1, t);
                var slope = (r0 - r1) / math.max(length + r, 1e-3f);
                for (int j = 0; j <= sides; j++)
                {
                    var ang = j * 2f * math.PI / sides;
                    var around = new float2(math.cos(ang), math.sin(ang));
                    var local = new float3(around.x * r * capY, around.y * r * capY * flatten, t * length + capX * r);
                    var n = math.normalize(new float3(around.x * capY, around.y * capY / flatten, capX + slope * capY));
                    verts.Add(at + math.mul(rot, local));
                    var worldN = math.mul(rot, n);
                    normals.Add(worldN);
                    colors.Add(Shade(color, worldN));
                }
                rows++;
            }
            for (int row = 0; row < rows - 1; row++)
                for (int j = 0; j < sides; j++)
                {
                    int a = start + row * (sides + 1) + j, b = a + sides + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
        }

        // The preview draws with an unlit vertex-colour material, so light is baked into the vertex colours here:
        // the game's lit shaders add the island's haze and cloud shadow, which washed the colours out on the turntable.
        static readonly float3 LightDir = math.normalize(new float3(0.35f, 0.85f, -0.4f));

        static Color Shade(Color c, float3 n)
        {
            var key = math.max(0f, math.dot(n, LightDir));
            var sky = 0.5f + 0.5f * n.y;
            var k = 0.32f + 0.22f * sky + 0.62f * key;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        /// <summary>A plain 1.8 m person (feet at the origin, facing +Z) for scale.</summary>
        static Mesh BuildHuman()
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var c = new Color(0.5f, 0.56f, 0.66f); // slate blue: reads as a reference, not part of the model
            void Part(float3 a, float3 b, float r0, float r1, float flatten = 1f)
            {
                var d = b - a;
                var rot = quaternion.LookRotationSafe(math.normalize(d), math.abs(d.y) > 0.9f * math.length(d) ? new float3(0f, 0f, 1f) : new float3(0f, 1f, 0f));
                AddCapsule(verts, normals, colors, tris, a, rot, math.length(d), r0, r1, flatten, c);
            }
            Part(new float3(-0.1f, 0.07f, 0f), new float3(-0.1f, 0.88f, 0f), 0.065f, 0.085f);   // legs, ankle to hip
            Part(new float3(0.1f, 0.07f, 0f), new float3(0.1f, 0.88f, 0f), 0.065f, 0.085f);
            Part(new float3(0f, 0.95f, 0f), new float3(0f, 1.38f, 0f), 0.16f, 0.19f, 0.65f);  // torso
            Part(new float3(-0.24f, 1.4f, 0f), new float3(-0.27f, 0.82f, 0f), 0.055f, 0.045f); // arms
            Part(new float3(0.24f, 1.4f, 0f), new float3(0.27f, 0.82f, 0f), 0.055f, 0.045f);
            Part(new float3(0f, 1.6f, 0f), new float3(0f, 1.69f, 0f), 0.11f, 0.11f);          // head: top at 1.80 m
            var mesh = new Mesh { name = "ModelLabPerson", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Grid(Mesh mesh, float step, float extent)
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var idx = new List<int>();
            var n = (int)math.round(extent / step);
            for (int i = -n; i <= n; i++)
            {
                var major = i % (step >= 1f ? 5 : 4) == 0;
                var c = major ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.22f);
                var x = i * step;
                verts.Add(new Vector3(x, 0f, -extent)); verts.Add(new Vector3(x, 0f, extent));
                verts.Add(new Vector3(-extent, 0f, x)); verts.Add(new Vector3(extent, 0f, x));
                for (int k = 0; k < 4; k++)
                {
                    colors.Add(c);
                    idx.Add(idx.Count);
                }
            }
            if (mesh == null)
                mesh = new Mesh { name = "ModelLabGrid", hideFlags = HideFlags.HideAndDontSave };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            return mesh;
        }
    }
}
