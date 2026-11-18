using System;
using System.Collections.Generic;
using System.Linq;
using Punchy.Flora;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// M-08 (#414): the Model Lab's read-only view of the game's set pieces, built by the game's own builders from the
    /// same definitions the world uses. Buildings are the building-form FloraSpecies assets, grown by
    /// <see cref="FloraGenerator.Grow"/> (finest level of detail); dungeon pieces are cave room kits
    /// (<see cref="CaveRoomKit.Build"/> and <see cref="CaveRoomKit.BuildMeshes"/>), one per <see cref="CaveRoomType"/>,
    /// with the ceiling left off so the turntable looks in. Nothing here is saved or changes the game: the gap to
    /// blueprint editing (W-175 units) is ModelLab.Blueprints (M-06 row 5).
    /// </summary>
    public partial class ModelLab
    {
        const string BuildingPrefix = "Building/", RoomPrefix = "Cave room/";

        /// <summary>Forms that are built structures (houses, halls, shrines, wells, bridges, ruins, kit buildings).</summary>
        static bool IsStructure(FloraForm f) => f is FloraForm.Hut or FloraForm.StoneHouse or FloraForm.Tent or FloraForm.Tower
            or FloraForm.Pen or FloraForm.Stall or FloraForm.Inn or FloraForm.Smithy or FloraForm.Forum or FloraForm.Fountain
            or FloraForm.Well or FloraForm.Hall or FloraForm.Shrine or FloraForm.Garden or FloraForm.Field or FloraForm.Lodge or FloraForm.Bridge
            or FloraForm.Palisade or FloraForm.MineFrame or FloraForm.Ruin or FloraForm.HazardShelter or FloraForm.KitBuilding
            or FloraForm.KitRuin or FloraForm.Stable;

        string[] setPieceNames;
        int setPieceVariant;
        int setPieceVariantCount; // a building species' variant count; 0 for cave rooms (any seed)
        // M-08 row 3: the side list of what the piece is made of; each row names the drawn mesh it highlights on hover (-1: none).
        readonly List<(string Label, int Mesh)> setPieceParts = new List<(string, int)>();
        int setPieceHover = -1;
        MaterialPropertyBlock dimBlock;
        static readonly string[] RoomGroupNames = { "Stone", "Trim", "Wood", "Cyan crystal", "Violet crystal", "Pale" };
        static readonly string[] RoomShapeNames = { "box", "crystal", "drum" };
        string setPieceNote = "";
        readonly List<Mesh> setPieceMeshes = new List<Mesh>();

        /// <summary>Every set piece the Lab can show: "Building/&lt;species&gt;" and "Cave room/&lt;type&gt;".</summary>
        public static string[] SetPieceNames() => Open().SetPieces();

        string[] SetPieces()
        {
            if (setPieceNames == null)
            {
                var buildings = AssetDatabase.FindAssets("t:" + nameof(FloraSpecies))
                    .Select(g => AssetDatabase.LoadAssetAtPath<FloraSpecies>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(s => s != null && IsStructure(s.Form))
                    .Select(s => BuildingPrefix + s.name).Distinct().OrderBy(n => n);
                var rooms = Enum.GetNames(typeof(CaveRoomType)).Select(n => RoomPrefix + n);
                setPieceNames = buildings.Concat(rooms).Concat(BlueprintNames()).ToArray();   // (blueprints: M-06 row 5)
            }
            return setPieceNames;
        }

        /// <summary>Shows a set piece by its list name (see <see cref="SetPieceNames"/>), variant 0.</summary>
        public static ModelLab LoadSetPiece(string name)
        {
            var w = Open();
            w.setPieceVariant = 0;
            w.LoadByName(w.nameField = name);
            return w;
        }

        /// <summary>M-08 row 2: rebuilds the loaded set piece as another variant (a building's variant, clamped to its
        /// species' count; a cave room's seed). Returns the variant shown.</summary>
        public static int SetPieceVariant(int variant)
        {
            var w = Open();
            w.StepSetPiece(variant);
            return w.setPieceVariant;
        }

        /// <summary>M-08 row 3: the loaded set piece's part rows, as the side list shows them.</summary>
        public static string[] SetPiecePartRows() => Open().setPieceParts.Select(r => r.Label).ToArray();

        /// <summary>Highlights part row i as a hover would (-1 clears), for checks and shots.</summary>
        public static void HoverSetPiecePart(int row)
        {
            var w = Open();
            w.setPieceHover = row >= 0 && row < w.setPieceParts.Count ? w.setPieceParts[row].Mesh : -1;
            w.Repaint();
        }

        void StepSetPiece(int variant)
        {
            if (family != Family.SetPiece || string.IsNullOrEmpty(loadedName))
                return;
            var name = loadedName;
            var yaw = orbitYaw;
            var pitch = orbitPitch;
            var z = zoom;
            setPieceVariant = math.max(0, variant);
            LoadByName(name);
            // (Stepping keeps the camera where it was, so variants compare in place.)
            orbitYaw = yaw;
            orbitPitch = pitch;
            zoom = z;
        }

        /// <summary>A set piece's meshes, bounds and size, or null if it is no set piece's name.</summary>
        public static string SetPieceInfo()
        {
            var w = Open();
            if (w.family != Family.SetPiece || string.IsNullOrEmpty(w.loadedName))
                return null;
            var tris = w.setPieceMeshes.Sum(m => m != null ? (int)m.GetIndexCount(0) / 3 : 0);
            return $"{w.loadedName} v{w.setPieceVariant}: {w.setPieceMeshes.Count} meshes, {tris} tris, size {w.frameBounds.size.x:0.0} x {w.frameBounds.size.y:0.0} x {w.frameBounds.size.z:0.0} m";
        }

        bool TryLoadSetPiece(string name)
        {
            if (!name.StartsWith(BuildingPrefix) && !name.StartsWith(RoomPrefix) && !IsBlueprintName(name))
                return false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // (A grow in play mode would take the running game's pregrown variant.)
                Debug.LogWarning("Model Lab: set pieces are built in edit mode only.");
                return true;
            }
            EnsurePreview();
            family = Family.SetPiece;
            loadedName = name;
            ground = 0f;
            var ok = IsBlueprintName(name) ? BuildBlueprint(name)
                   : name.StartsWith(BuildingPrefix) ? BuildBuilding(name.Substring(BuildingPrefix.Length)) : BuildRoom(name.Substring(RoomPrefix.Length));
            if (!ok)
            {
                Unload();
                return true;
            }
            frameBounds = bounds;
            orbitPitch = 30f;
            Frame();
            return true;
        }

        bool BuildBuilding(string speciesName)
        {
            var species = AssetDatabase.FindAssets(speciesName + " t:" + nameof(FloraSpecies))
                .Select(g => AssetDatabase.LoadAssetAtPath<FloraSpecies>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(s => s != null && s.name == speciesName);
            if (species == null)
            {
                Debug.LogWarning($"Model Lab: no flora species called '{speciesName}'.");
                return false;
            }
            setPieceVariantCount = math.max(1, species.Variants);
            setPieceVariant = math.clamp(setPieceVariant, 0, setPieceVariantCount - 1);
            FloraModel model;
            try
            {
                model = FloraGenerator.Grow(species, setPieceVariant);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Model Lab: growing {speciesName} failed: {e.Message}");
                return false;
            }
            var lod0 = model.Lods[0];
            AddShaded(lod0);
            // (Only LOD 0 is shown; the coarser levels are dropped here.)
            foreach (var m in model.Lods)
                if (m != null)
                    DestroyImmediate(m);
            setPieceParts.Add(($"{species.name}: one merged mesh (LOD 0). Naming its walls, roofs and trim needs the kit's part list from the grower (flora hook, asked).", -1));
            setPieceNote = $"{species.Form}" + (species.Form is FloraForm.KitBuilding or FloraForm.KitRuin ? $" {species.KitShape} ({species.KitCulture})" : "") +
                           $", variant {setPieceVariant} of {species.Variants}; drawn at scale 1 (towns scale buildings to their folk).";
            return setPieceMeshes.Count > 0;
        }

        bool BuildRoom(string typeName)
        {
            setPieceVariantCount = 0;
            if (!Enum.TryParse(typeName, out CaveRoomType type))
            {
                Debug.LogWarning($"Model Lab: no cave room type called '{typeName}'.");
                return false;
            }
            var room = SampleRoom(type, (uint)setPieceVariant);
            CaveRoomKit.Kit kit;
            try
            {
                kit = CaveRoomKit.Build(room);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Model Lab: building the {type} room failed: {e.Message}");
                return false;
            }
            // Leave the ceiling off (parts wholly above most of the room's height) so the turntable looks in.
            var open = new CaveRoomKit.Kit { Floor = kit.Floor };
            var cut = kit.Floor + room.Height - 0.6f;
            for (int i = 0; i < kit.Parts.Count; i++)
            {
                if (i == kit.SetFrom)
                    open.SetFrom = open.Parts.Count;
                var p = kit.Parts[i];
                if (i < kit.SetFrom && p.Centre.y > cut)
                    continue;
                open.Parts.Add(p);
            }
            var meshes = CaveRoomKit.BuildMeshes(open, 0f, kit.Contacts);
            for (int g = 0; g < meshes.Length; g++)
            {
                if (meshes[g] == null)
                    continue;
                CaveRoomKit.Colours((int)type, g, out var rock, out _);
                var before = setPieceMeshes.Count;
                AddShaded(meshes[g], rock);
                if (setPieceMeshes.Count > before)
                    setPieceParts.Add((RoomGroupLabel(open, g), before));
                DestroyImmediate(meshes[g]);
            }
            setPieceNote = $"{type} room {room.HX * 2f:0.0} x {room.HZ * 2f:0.0} m, {room.Height:0.0} m high, seed {room.Seed}; ceiling left off. " +
                           "The cave's own rock round it isn't drawn (that comes from the baked cave field).";
            return setPieceMeshes.Count > 0;
        }

        /// <summary>
        /// A cave room of this type as the bake plans one (CaveBaking.Rooms: its size ranges, a door to fit), facing +Z
        /// from the origin, picked from the seed. (The ranges are copied from the bake, which keeps them inline.)
        /// </summary>
        /// <summary>One room group's row: its name and its parts counted by shell (walls, floor, door frame) or set
        /// piece (what the room type adds) and by shape, e.g. "Stone: 40 shell boxes, 6 set drums".</summary>
        static string RoomGroupLabel(CaveRoomKit.Kit kit, int group)
        {
            var counts = new int[2, RoomShapeNames.Length];
            for (int i = 0; i < kit.Parts.Count; i++)
            {
                var p = kit.Parts[i];
                if (p.Group == group)
                    counts[i < kit.SetFrom ? 0 : 1, math.min((int)p.Shape, RoomShapeNames.Length - 1)]++;
            }
            var bits = new List<string>();
            for (int k = 0; k < 2; k++)
                for (int sh = 0; sh < RoomShapeNames.Length; sh++)
                    if (counts[k, sh] > 0)
                        bits.Add($"{counts[k, sh]} {(k == 0 ? "shell" : "set")} {RoomShapeNames[sh]}{(counts[k, sh] == 1 ? "" : sh == 0 ? "es" : "s")}");
            var name = group < RoomGroupNames.Length ? RoomGroupNames[group] : "Group " + group;
            return $"{name}: {string.Join(", ", bits)}";
        }

        static CaveRoom SampleRoom(CaveRoomType type, uint seed)
        {
            var pick = new Unity.Mathematics.Random(math.hash(new uint2(seed, (uint)type + 0x5e7u)) | 1u);
            float2 hx, hz, h;
            switch (type)
            {
                case CaveRoomType.Lake: hx = new float2(4.5f, 5.5f); hz = new float2(4.5f, 6f); h = new float2(5f, 6.5f); break;
                case CaveRoomType.Roost: hx = new float2(3.5f, 4.5f); hz = new float2(4f, 5.5f); h = new float2(6f, 8f); break;
                case CaveRoomType.Ruin: hx = new float2(7f, 9f); hz = new float2(7f, 9f); h = new float2(5f, 6f); break;
                default: hx = new float2(3.5f, 4.5f); hz = new float2(3.5f, 5f); h = new float2(4.5f, 6f); break;
            }
            return new CaveRoom
            {
                Cave = -1, Node = (int)type, Type = (int)type, Origin = float3.zero, F = new float2(0f, 1f),
                HX = pick.NextFloat(hx.x, hx.y), HZ = pick.NextFloat(hz.x, hz.y), Height = pick.NextFloat(h.x, h.y),
                DoorWidth = 2.2f, DoorHeight = 2.6f, Seed = pick.NextUInt() | 1u, Pool = -1, Approach = 0f
            };
        }

        /// <summary>
        /// Adds a readable copy of a game mesh with the Lab's light baked into its vertex colours (the preview's material
        /// is unlit). The game uploads its meshes unreadable; the editor can still read them as mesh data.
        /// <paramref name="tint"/> replaces the colours of meshes the game colours in its material (cave rooms).
        /// </summary>
        void AddShaded(Mesh src, Color? tint = null)
        {
            if (src == null)
                return;
            using var all = Mesh.AcquireReadOnlyMeshData(src);
            var data = all[0];
            var n = data.vertexCount;
            if (n == 0)
                return;
            var pos = new NativeArray<Vector3>(n, Allocator.Temp);
            var nrm = new NativeArray<Vector3>(n, Allocator.Temp);
            var col = new NativeArray<Color>(n, Allocator.Temp);
            data.GetVertices(pos);
            data.GetNormals(nrm);
            if (tint == null && data.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color))
                data.GetColors(col);
            var colours = new Color[n];
            for (int i = 0; i < n; i++)
            {
                var c = tint ?? col[i];
                c.a = 1f;
                colours[i] = Shade(c, nrm[i]);
            }
            var tris = new List<int>();
            for (int s = 0; s < data.subMeshCount; s++)
            {
                var sub = new NativeArray<int>(data.GetSubMesh(s).indexCount, Allocator.Temp);
                data.GetIndices(sub, s);
                tris.AddRange(sub);
                sub.Dispose();
            }
            var mesh = new Mesh { name = src.name + " (Lab)", hideFlags = HideFlags.HideAndDontSave, indexFormat = n > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.colors = colours;
            mesh.SetTriangles(tris, 0, true);
            pos.Dispose();
            nrm.Dispose();
            col.Dispose();
            bounds = setPieceMeshes.Count == 0 ? mesh.bounds : Encapsulated(bounds, mesh.bounds);
            setPieceMeshes.Add(mesh);
        }

        static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        void UnloadSetPiece()
        {
            foreach (var m in setPieceMeshes)
                if (m != null)
                    DestroyImmediate(m);
            setPieceMeshes.Clear();
            ClearBlueprintSelection();
            setPieceParts.Clear();
            setPieceHover = -1;
            setPieceNote = "";
        }

        void DrawSetPieces()
        {
            if (DrawBlueprintSelection())
                return;
            // Hovering a part row in the side list dims every other mesh, so the row's parts stand out.
            dimBlock ??= new MaterialPropertyBlock();
            dimBlock.SetColor("_Color", new Color(0.3f, 0.3f, 0.3f, 1f));
            for (int i = 0; i < setPieceMeshes.Count; i++)
                preview.DrawMesh(setPieceMeshes[i], Matrix4x4.identity, modelMaterial, 0, setPieceHover >= 0 && i != setPieceHover ? dimBlock : null);
        }

        void DrawSetPiecePicker()
        {
            var names = SetPieces();
            var current = family == Family.SetPiece ? Array.IndexOf(names, loadedName) : -1;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var pick = EditorGUILayout.Popup("Set piece", current, names);
                if (pick >= 0 && pick != current)
                {
                    setPieceVariant = 0;
                    LoadByName(nameField = names[pick]);
                }
            }
        }

        void DrawSetPiecePanel()
        {
            if (IsBlueprintName(loadedName))
            {
                DrawBlueprintPanel();
                return;
            }
            GUILayout.Label("Set piece", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var isRoom = loadedName.StartsWith(RoomPrefix);
                var v = setPieceVariant;
                if (GUILayout.Button("<", GUILayout.Width(24f)))
                    v--;
                v = EditorGUILayout.IntField(isRoom ? "Seed" : "Variant", v);
                if (GUILayout.Button(">", GUILayout.Width(24f)))
                    v++;
                if (GUILayout.Button("Random", GUILayout.Width(60f)))
                    v = UnityEngine.Random.Range(0, isRoom ? 100000 : setPieceVariantCount);
                if (v != setPieceVariant && v >= 0)
                    StepSetPiece(v);
            }
            EditorGUILayout.LabelField(setPieceNote, EditorStyles.wordWrappedMiniLabel);
            DrawSetPieceParts();
            EditorGUILayout.HelpBox("View only: built by the game's own builders from its definitions. Blueprints (W-175 units) " +
                                    "are edited and saved under Set piece > Blueprint (M-06 row 5).", MessageType.Info);
        }

        /// <summary>M-08 row 3 (the cheaper version): the piece's parts as a side list; hovering a row highlights its mesh.</summary>
        void DrawSetPieceParts()
        {
            if (setPieceParts.Count == 0)
                return;
            wantsMouseMove = true;
            EditorGUILayout.LabelField("Parts", EditorStyles.miniBoldLabel);
            var hover = -1;
            foreach (var (label, mesh) in setPieceParts)
            {
                EditorGUILayout.LabelField(label, EditorStyles.wordWrappedMiniLabel);
                if (mesh >= 0 && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                    hover = mesh;
            }
            if (Event.current.type == EventType.Repaint && hover != setPieceHover)
            {
                setPieceHover = hover;
                Repaint();
            }
            else if (Event.current.type == EventType.MouseMove)
                Repaint();
        }
    }
}
