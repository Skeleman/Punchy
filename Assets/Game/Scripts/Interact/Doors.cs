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

        public static string Name => Current.ToString();

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
            if (nav.Button($"Use (doors, items, chests): {Name}  /  pad RB"))
                Next();
            GUILayout.Label($"Interact (pet, pick up, sit): {InputGlyphs.ActionToken("Player/Interact") ?? "E"}");
            GUILayout.Label("Move: WASD / left stick     Look: mouse / right stick     Jump: Space");
            GUILayout.Label("Menu: Esc / Start     Frame cap: F4 / View");
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
            var hosting = server != null && server.IsCreated;
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
            if (facing >= 0 && UseKey.WasPressed())
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
            foreach (var d in open)
                if (d < doorAnchors.Count)
                    Punchy.Flora.FloraDoors.SetOpen(terrain.Anchors.Value.Anchors[doorAnchors[d]].Position, 1f);
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
                prompt = $"{UseKey.Name}  {(open.Contains(facing) ? "close" : "open")} door";
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
            Set(door, !open.Contains(door), server != null && server.IsCreated ? "host" : "solo");
        }

        void Set(int door, bool isOpen, string who)
        {
            if (isOpen ? !open.Add(door) : !open.Remove(door))
                return;
            if (!isOpen && TryGetDoor(door, out var p, out _))
                Punchy.Flora.FloraDoors.SetOpen(p, 0f);
            Debug.Log($"Doors: door {door} {(isOpen ? "open" : "shut")} ({who})");
            if (server != null && server.IsCreated)
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
