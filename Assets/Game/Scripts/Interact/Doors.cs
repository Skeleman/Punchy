using System.Collections.Generic;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Interact
{
    /// <summary>
    /// The Use key (#25, P-10): doors now, later items and chests. Its own key, apart from Interact (E: pet, pick up,
    /// sit, the animal lane's), so using a door never sits the player down. F on the keyboard (changeable in the menu's
    /// Keys tab, kept per machine), RB on a pad.
    /// </summary>
    public static class UseKey
    {
        public static readonly Key[] Choices = { Key.F, Key.G, Key.R, Key.T, Key.V };
        const string PrefKey = "Punchy.Keys.Use";
        public static Key Current { get; private set; } = Key.F;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var saved = (Key)PlayerPrefs.GetInt(PrefKey, (int)Key.F);
            Current = System.Array.IndexOf(Choices, saved) >= 0 ? saved : Key.F;
            Punchy.UI.MainMenu.AddTab("Keys", 50, DrawKeys);
        }

        /// <summary>
        /// The Use button's name for prompts, on the device in use (P-20, item 28): the key, or the pad's RB (in the pad's
        /// own name: R1 on a PlayStation pad).
        /// </summary>
        public static string Name
        {
            get
            {
                InputGlyphs.Update();
                return InputGlyphs.Gamepad ? InputGlyphs.Path(PadPath) ?? "RB" : Current.ToString();
            }
        }

        public const string PadPath = "<Gamepad>/rightShoulder";

        /// <summary>Pressed this frame (the key, or RB), and no menu open.</summary>
        public static bool WasPressed()
        {
            if (Punchy.UI.Overlay.AnyOpen)
                return false;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            return keyboard != null && keyboard[Current].wasPressedThisFrame || pad != null && pad.rightShoulder.wasPressedThisFrame;
        }

        static void Next()
        {
            Current = Choices[(System.Array.IndexOf(Choices, Current) + 1) % Choices.Length];
            PlayerPrefs.SetInt(PrefKey, (int)Current);
            PlayerPrefs.Save();
        }

        /// <summary>The Keys tab: the game's keys, and the Use key (the one that can be changed so far).</summary>
        static void DrawKeys(Punchy.UI.MenuNav nav)
        {
            GUILayout.Label("<b>Keys</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (nav.Button($"Use key: {Current}  /  pad {Pad(PadPath)}   (press to change the key)"))
                Next();
            // P-20 (item 28): every command with its keyboard and pad binding, in three columns. Actions are read from
            // their own bindings; pad buttons are named for the pad in use (A / Cross, LB / L1).
            var bold = new GUIStyle(GUI.skin.label) { richText = true };
            Row("<b>Command</b>", "<b>Keyboard / mouse</b>", "<b>Pad</b>", bold);
            foreach (var (what, keys, pad) in Table())
                Row(what, keys, pad, GUI.skin.label);
        }

        static void Row(string what, string keys, string pad, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(what, style, GUILayout.Width(210f));
            GUILayout.Label(keys, style, GUILayout.Width(110f));
            GUILayout.Label(pad, style);
            GUILayout.EndHorizontal();
        }

        /// <summary>A pad button's name for the pad in use (or the one forced with -forcepad).</summary>
        static string Pad(string path) => InputGlyphs.Path(path) ?? path;

        /// <summary>The binding table (P-20, item 28): command, keyboard, pad.</summary>
        public static System.Collections.Generic.IEnumerable<(string what, string keys, string pad)> Table()
        {
            yield return ("Move", "WASD / arrows", "left stick");
            yield return ("Look", "mouse", "right stick");
            yield return ("Jump, climb onto a ledge", Bound("Player/Jump", false, "Space"), Bound("Player/Jump", true, "A"));
            yield return ("Sprint", Bound("Player/Sprint", false, "Left Shift"), Bound("Player/Sprint", true, "left stick press"));
            yield return ("Walk", Bound("Player/Walk", false, "Left Ctrl"), Bound("Player/Walk", true, "right stick press"));
            yield return ("Roll", Bound("Player/Crouch", false, "C"), Bound("Player/Crouch", true, "B"));
            yield return ("Interact (pet, sit)", Bound("Player/Interact", false, "E"), Bound("Player/Interact", true, "Y"));
            yield return ("Use: doors, lamps, pick up, chests", Current.ToString(), Pad(PadPath));
            yield return ("Inventory and quest log", "Tab / I", Pad("<Gamepad>/leftShoulder"));
            yield return ("Inventory: drop / move an item", $"{Current} / click", $"{Pad("<Gamepad>/buttonSouth")} / {Pad(PadPath)}");
            yield return ("Inventory: close", "Tab / I / Esc", $"{Pad("<Gamepad>/leftShoulder")} / {Pad("<Gamepad>/buttonEast")}");
            yield return ("Creature actions 1-4", "1 2 3 4", "d-pad");
            yield return ("Cycle the weather", Punchy.Settings.WeatherKey.KeyboardName, Pad(Punchy.Settings.WeatherKey.PadPath));
            yield return ("Menu", "Esc", Pad("<Gamepad>/start"));
            yield return ("Menu tabs", "Q / E", $"{Pad("<Gamepad>/leftShoulder")} / {Pad(PadPath)}");
            yield return ("Options", Bound("Player/Options", false, "F4"), Bound("Player/Options", true, "View"));
        }

        /// <summary>An action's control on the keyboard and mouse or on a pad, by name, from the actions' own bindings.</summary>
        static string Bound(string action, bool gamepad, string fallback)
        {
            var a = InputSystem.actions?.FindAction(action);
            if (a == null)
                return fallback;
            var group = gamepad ? "Gamepad" : "Keyboard&Mouse";
            foreach (var b in a.bindings)
                if (!b.isComposite && !b.isPartOfComposite && b.groups != null && b.groups.Contains(group))
                    return gamepad ? Pad(b.effectivePath) : InputControlPath.ToHumanReadableString(b.effectivePath, InputControlPath.HumanReadableStringOptions.OmitDevice);
            return fallback;
        }
    }

    /// <summary>Client → host: the player used a door (by its index in the island's Door anchors).</summary>
    public struct UseDoorRpc : IRpcCommand
    {
        public int Door;
    }

    /// <summary>Host → everyone: a door is now open (1) or shut (0).</summary>
    public struct DoorStateRpc : IRpcCommand
    {
        public int Door;
        public byte Open;
    }

    /// <summary>
    /// Settlement doors you can open (#25, P-10). Every house door is a Door anchor (baked with the island, so its index
    /// is the same on every machine); its leaf is drawn by the flora shader and swung by FloraDoors. Facing one within
    /// <see cref="Reach"/> shows "F  open door"; Use toggles it. The host owns which doors are open: a client asks
    /// (UseDoorRpc), the host decides and tells everyone (DoorStateRpc), and a player who joins later is sent every
    /// open door. Solo just toggles. Open doors are set on FloraDoors every frame (villagers' doors share its list).
    /// Test flags: "-usedoor N" uses door N (through the same path) once the world is up; "-atdoor N" stands the player in front of door N
    /// (12 s after start).
    /// </summary>
    public class Doors : MonoBehaviour
    {
        public const float Reach = 2.5f;
        /// <summary>How far the door the player faces is (float.MaxValue: none): the Use key goes to the nearer of it and an item (P-11).</summary>
        public static float FacingDistance = float.MaxValue;

        static Doors instance;
        /// <summary>Which doors are open, on this machine (the host's word, or our own solo).</summary>
        readonly HashSet<int> open = new HashSet<int>();
        readonly List<int> doorAnchors = new List<int>();
        BlobAssetReference<AnchorSetBlob> indexed;
        readonly HashSet<Entity> sentTo = new HashSet<Entity>();
        World server, client;
        int facing = -1;
        string prompt;
        GUIStyle promptStyle;
        int useDoorArg = -1, atDoorArg = -1;
        float useDoorAt, atDoorAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null)
                return;
            var go = new GameObject("Doors");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<Doors>();
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-usedoor");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var door))
                instance.useDoorArg = door;
            at = System.Array.IndexOf(args, "-atdoor");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var stand))
                instance.atDoorArg = stand;
            at = System.Array.IndexOf(args, "-climbhouse");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var climb))
            {
                instance.climbArg = climb;
                if (at + 2 < args.Length && float.TryParse(args[at + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var side))
                    instance.climbSide = side;
            }
        }

        /// <summary>Door anchor <paramref name="door"/>'s position and facing (out of the house), if the world has it.</summary>
        public static bool TryGetDoor(int door, out float3 position, out float2 facing)
        {
            position = default;
            facing = default;
            if (instance == null || !instance.TryTerrain(out var terrain) || door < 0 || door >= instance.doorAnchors.Count)
                return false;
            var a = terrain.Anchors.Value.Anchors[instance.doorAnchors[door]];
            (position, facing) = (a.Position, a.Facing);
            return true;
        }

        bool TryTerrain(out TerrainSource terrain)
        {
            terrain = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!q.TryGetSingleton(out terrain) || !terrain.Anchors.IsCreated)
                return false;
            if (!indexed.Equals(terrain.Anchors))
            {
                // (The door list, in anchor order: the same on every machine with the same island.)
                indexed = terrain.Anchors;
                doorAnchors.Clear();
                ref var anchors = ref terrain.Anchors.Value.Anchors;
                for (int i = 0; i < anchors.Length; i++)
                    if (anchors[i].Kind == AnchorKind.Door)
                        doorAnchors.Add(i);
                Debug.Log($"Doors: {doorAnchors.Count} house doors on this island");
            }
            return true;
        }

        void Update()
        {
            if (NetWorlds.Server != server || NetWorlds.Client != client)
            {
                (server, client) = (NetWorlds.Server, NetWorlds.Client);
                sentTo.Clear();
                if (client != null && server == null)
                    open.Clear();   // a joining client takes the host's doors
            }
            if (!TryTerrain(out var terrain))
                return;
            var hosting = server != null && server.IsCreated && server.IsServer();
            var joined = !hosting && client != null && client.IsCreated && client.IsClient();
            if (hosting)
                ServeDoors();
            if (client != null && client.IsCreated && client.IsClient())
                ReceiveStates();   // (a host's own client gets the broadcasts too; they change nothing there)

            if (atDoorArg >= 0)
            {
                atDoorAt = atDoorAt == 0f ? Time.realtimeSinceStartup + 12f : atDoorAt;
                if (Time.realtimeSinceStartup >= atDoorAt)
                {
                    StandAt(atDoorArg, ref terrain);
                    atDoorArg = -1;
                }
            }
            FindFacing(ref terrain.Anchors.Value);
            // (P-13: an open door whose house has a room takes the player inside: Interiors.)
            if (facing >= 0 && UseKey.WasPressed() && !(open.Contains(facing) && Interiors.Enter(facing)))
                Use(facing);
            if (useDoorArg >= 0)
            {
                useDoorAt = useDoorAt == 0f ? Time.realtimeSinceStartup + 25f : useDoorAt;
                var inGame = 0;
                if (joined)
                {
                    using var iq = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamInGame));
                    inGame = iq.CalculateEntityCount();
                }
                if (Time.realtimeSinceStartup >= useDoorAt && (!joined || inGame > 0))
                {
                    Debug.Log($"Doors: -usedoor {useDoorArg}");
                    Use(useDoorArg);
                    useDoorArg = -1;
                }
            }
            if (climbArg >= 0)
                ClimbTest(ref terrain);
            foreach (var d in open)
                if (d < doorAnchors.Count)
                    Punchy.Flora.FloraDoors.SetOpen(terrain.Anchors.Value.Anchors[doorAnchors[d]].Position, 1f);
        }

        int climbArg = -1, climbMantles;
        float climbSide = 1.4f, climbStart, climbEnd;
        bool climbPlaced, climbWasMantle;
        float3 climbDoor;
        float2 climbFacing;
        float3 climbSpot;
        float climbPushAt;

        /// <summary>
        /// "-climbhouse N [side]" (P-20, item 27): the player stands 0.9 m out from house N's wall, <c>side</c> m (1.4 by
        /// default) along it from the door, and pushes into the wall with jump pressed every third of a second for 8 s, as
        /// a player trying to climb in would. Logs each climb onto a ledge (from, to) and where it ends: in front of the
        /// wall, up on top, or inside the house (behind the wall's face, below its eaves).
        /// </summary>
        void ClimbTest(ref TerrainSource terrain)
        {
            var now = Time.realtimeSinceStartup;
            if (climbStart == 0f)
                climbStart = now + 12f;
            if (now < climbStart)
                return;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>(), ComponentType.ReadOnly<PlayerMotion>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var t = em.GetComponentData<LocalTransform>(player);
            if (!climbPlaced)
            {
                if (!TryGetDoor(climbArg, out climbDoor, out climbFacing))
                {
                    Debug.LogWarning($"Doors: -climbhouse: no door {climbArg}");
                    climbArg = -1;
                    return;
                }
                climbFacing = math.normalizesafe(climbFacing);
                var along = new float2(climbFacing.y, -climbFacing.x);
                var xz = climbDoor.xz + climbFacing * 0.9f + along * climbSide;
                t.Position = new float3(xz.x, terrain.SampleHeight(xz) + 0.05f, xz.y);
                t.Rotation = quaternion.RotateY(math.atan2(-climbFacing.x, -climbFacing.y));
                em.SetComponentData(player, t);
                var cam = Object.FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
                if (cam != null)
                    cam.SetView(math.degrees(math.atan2(-climbFacing.x, -climbFacing.y)) + 35f, 15f);
                climbPlaced = true;
                climbSpot = t.Position;
                // (Held at the wall for 6 s first: the houses' collision boxes load round the player after a teleport.)
                climbPushAt = now + 6f;
                climbEnd = climbPushAt + 8f;
                Debug.Log($"Doors: -climbhouse {climbArg}: at the wall {climbSide:F1} m along from door ({climbDoor.x:F1}, {climbDoor.y:F1}, {climbDoor.z:F1}), player ({t.Position.x:F1}, {t.Position.y:F1}, {t.Position.z:F1}), waiting 6 s for the walls to load");
                return;
            }
            if (now < climbPushAt)
            {
                t.Position = climbSpot;
                em.SetComponentData(player, t);
                return;
            }
            var m = em.GetComponentData<PlayerMotion>(player);
            if (m.Mantle > 0f && !climbWasMantle)
            {
                climbMantles++;
                Debug.Log($"Doors: -climbhouse: climbing a ledge from ({m.MantleFrom.x:F1}, {m.MantleFrom.y:F1}, {m.MantleFrom.z:F1}) to ({m.MantleTo.x:F1}, {m.MantleTo.y:F1}, {m.MantleTo.z:F1})");
            }
            climbWasMantle = m.Mantle > 0f;
            if (now < climbEnd)
            {
                PlayerInputSystem.TestMove = -climbFacing;
                PlayerInputSystem.TestJump = Time.frameCount % 20 == 0;
                return;
            }
            PlayerInputSystem.TestMove = null;
            // (Out from the door's plane: the door sits in the wall's face, so behind it and below the eaves is inside.)
            // (Further than a house's depth behind it, or off to the side, the player went round the house, not in.)
            var outFrom = math.dot(t.Position.xz - climbDoor.xz, climbFacing);
            var sideways = math.abs(math.dot(t.Position.xz - climbDoor.xz, new float2(climbFacing.y, -climbFacing.x)));
            var up = t.Position.y - climbDoor.y;
            var where = up >= 2.4f ? "up on top" : outFrom >= -0.3f ? "outside, in front of the wall" :
                        outFrom > -8f && sideways < 5f ? "INSIDE the house" : "past the house (went round it)";
            Debug.Log($"Doors: -climbhouse {climbArg}: ended at ({t.Position.x:F1}, {t.Position.y:F1}, {t.Position.z:F1}), {outFrom:F2} m out from the wall, {up:F1} m above the door: {where}; {climbMantles} ledge climbs");
            climbArg = -1;
        }

        /// <summary>"-atdoor N": the player 1.8 m out from door N, facing it.</summary>
        void StandAt(int door, ref TerrainSource terrain)
        {
            if (!TryGetDoor(door, out var p, out var f))
                return;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerTag>(), ComponentType.ReadWrite<LocalTransform>());
            if (!q.TryGetSingletonEntity<LocalTransform>(out var player))
                return;
            var xz = p.xz + math.normalizesafe(f) * 1.8f;
            var t = em.GetComponentData<LocalTransform>(player);
            t.Position = new float3(xz.x, terrain.SampleHeight(xz) + 0.05f, xz.y);
            t.Rotation = quaternion.RotateY(math.atan2(-f.x, -f.y));
            em.SetComponentData(player, t);
            var cam = Object.FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
            if (cam != null)
                cam.SetView(math.degrees(math.atan2(-f.x, -f.y)) + 25f, 12f);
            Debug.Log($"Doors: standing at door {door} ({p.x:F1}, {p.y:F1}, {p.z:F1})");
        }

        /// <summary>The nearest door in front of the player within reach (or -1), and the prompt for it.</summary>
        void FindFacing(ref AnchorSetBlob set)
        {
            facing = -1;
            prompt = null;
            FacingDistance = float.MaxValue;
            var world = World.DefaultGameObjectInjectionWorld;
            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            if (!q.TryGetSingleton<LocalTransform>(out var player))
                return;
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz;
            var best = Reach;
            for (int k = 0; k < doorAnchors.Count; k++)
            {
                var a = set.Anchors[doorAnchors[k]];
                var to = a.Position - player.Position;
                var d = math.length(to.xz);
                if (d >= best || math.abs(to.y) > 2f)
                    continue;
                if (d > 0.3f && math.dot(math.normalizesafe(to.xz), math.normalizesafe(forward)) < 0.4f)
                    continue;
                (best, facing) = (d, k);
            }
            FacingDistance = facing >= 0 ? best : float.MaxValue;
            // (An item or chest at least as near takes the Use key: Items.)
            if (facing >= 0 && FacingDistance >= Punchy.Items.Items.TargetDistance)
                facing = -1;
            if (facing >= 0)
                prompt = open.Contains(facing) && Interiors.CanEnter(facing) ? $"{UseKey.Name}  go inside"
                       : $"{UseKey.Name}  {(open.Contains(facing) ? "close" : "open")} door";
        }

        /// <summary>The player used a door: solo or host decide now; a client asks the host.</summary>
        void Use(int door)
        {
            if (client != null && client.IsCreated && client.IsClient() && (server == null || !server.IsCreated))
            {
                var em = client.EntityManager;
                using var q = em.CreateEntityQuery(typeof(NetworkId));
                if (q.IsEmpty)
                    return;
                var rpc = em.CreateEntity(typeof(UseDoorRpc), typeof(SendRpcCommandRequest));
                em.SetComponentData(rpc, new UseDoorRpc { Door = door });
                em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = q.GetSingletonEntity() });
                Debug.Log($"Doors: asked the host to use door {door}");
                return;
            }
            // (Solo's local world is NetWorlds.Server too: only a real server is a host.)
            Set(door, !open.Contains(door), server != null && server.IsCreated && server.IsServer() ? "host" : "solo");
        }

        void Set(int door, bool isOpen, string who)
        {
            if (isOpen ? !open.Add(door) : !open.Remove(door))
                return;
            if (!isOpen && TryGetDoor(door, out var p, out _))
                Punchy.Flora.FloraDoors.SetOpen(p, 0f);
            Debug.Log($"Doors: door {door} {(isOpen ? "open" : "shut")} ({who})");
            if (server != null && server.IsCreated && server.IsServer())
                Broadcast(door, isOpen);
        }

        // ------------------------------------------------------------------ host

        void ServeDoors()
        {
            var em = server.EntityManager;
            // Requests from clients (and from our own client world, which sends none: the host uses doors directly).
            using (var q = em.CreateEntityQuery(typeof(UseDoorRpc), typeof(ReceiveRpcCommandRequest)))
            {
                using var requests = q.ToEntityArray(Allocator.Temp);
                foreach (var r in requests)
                {
                    var door = em.GetComponentData<UseDoorRpc>(r).Door;
                    if (door >= 0 && door < doorAnchors.Count)
                        Set(door, !open.Contains(door), "host, for a client");
                    em.DestroyEntity(r);
                }
            }
            // A player who has just joined gets every open door.
            using (var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame)))
            {
                using var connections = q.ToEntityArray(Allocator.Temp);
                foreach (var c in connections)
                {
                    if (!sentTo.Add(c))
                        continue;
                    foreach (var d in open)
                        Send(em, c, d, true);
                }
            }
        }

        void Broadcast(int door, bool isOpen)
        {
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            using var connections = q.ToEntityArray(Allocator.Temp);
            foreach (var c in connections)
                Send(em, c, door, isOpen);
        }

        static void Send(EntityManager em, Entity connection, int door, bool isOpen)
        {
            var rpc = em.CreateEntity(typeof(DoorStateRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(rpc, new DoorStateRpc { Door = door, Open = (byte)(isOpen ? 1 : 0) });
            em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        // ------------------------------------------------------------------ client

        void ReceiveStates()
        {
            var em = client.EntityManager;
            using var q = em.CreateEntityQuery(typeof(DoorStateRpc), typeof(ReceiveRpcCommandRequest));
            using var states = q.ToEntityArray(Allocator.Temp);
            foreach (var s in states)
            {
                var state = em.GetComponentData<DoorStateRpc>(s);
                Set(state.Door, state.Open != 0, "from the host");
                em.DestroyEntity(s);
            }
        }

        void OnGUI()
        {
            if (prompt == null || Punchy.UI.Overlay.AnyOpen)
                return;
            promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, richText = true };
            var size = promptStyle.CalcSize(new GUIContent(prompt)) + new Vector2(24f, 10f);
            GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.72f, size.x, size.y), prompt, promptStyle);
        }
    }
}
