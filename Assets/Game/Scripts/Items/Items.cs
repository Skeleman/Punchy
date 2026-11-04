using System.Collections.Generic;
using Punchy.Dungeon;
using Punchy.Interact;
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

namespace Punchy.Items
{
    /// <summary>One kind of item (#26): its name, the colour it's drawn and shown in, how many stack, its weight (kg).</summary>
    public struct ItemDef
    {
        public string Name;
        public Color Colour;
        public int Stack;
        public float Weight;
    }

    /// <summary>The item table (#26, P-11). An item's id is its index here; never reorder, only append.</summary>
    public static class ItemTable
    {
        public static readonly ItemDef[] Items =
        {
            new ItemDef { Name = "Stick", Colour = new Color(0.55f, 0.38f, 0.2f), Stack = 20, Weight = 0.3f },
            new ItemDef { Name = "Stone", Colour = new Color(0.55f, 0.55f, 0.58f), Stack = 20, Weight = 1f },
            new ItemDef { Name = "Berry", Colour = new Color(0.75f, 0.1f, 0.3f), Stack = 30, Weight = 0.02f },
            new ItemDef { Name = "Mushroom", Colour = new Color(0.85f, 0.75f, 0.6f), Stack = 20, Weight = 0.05f },
            new ItemDef { Name = "Shell", Colour = new Color(0.95f, 0.85f, 0.8f), Stack = 20, Weight = 0.05f },
            new ItemDef { Name = "Fish", Colour = new Color(0.4f, 0.6f, 0.75f), Stack = 5, Weight = 0.8f },
            new ItemDef { Name = "Coin", Colour = new Color(1f, 0.8f, 0.2f), Stack = 99, Weight = 0.01f },
            new ItemDef { Name = "Key", Colour = new Color(0.8f, 0.7f, 0.3f), Stack = 5, Weight = 0.05f },
        };
        public const byte Key = 7;
    }

    /// <summary>Client → host: pick up world item Uid.</summary>
    public struct PickupRpc : IRpcCommand { public int Uid; }
    /// <summary>Client → host: open temple chest socket Socket.</summary>
    public struct OpenChestRpc : IRpcCommand { public int Socket; }
    /// <summary>Client → host: drop Count of Item at Pos.</summary>
    public struct DropRpc : IRpcCommand { public byte Item, Count; public float3 Pos; }
    /// <summary>Host → everyone: world item Uid is gone (picked up).</summary>
    public struct ItemGoneRpc : IRpcCommand { public int Uid; }
    /// <summary>Host → everyone: a world item appeared (a drop, a chest's contents).</summary>
    public struct ItemSpawnRpc : IRpcCommand { public int Uid; public byte Item, Count; public float3 Pos; }
    /// <summary>Host → the picker: it's yours, into the inventory.</summary>
    public struct GrantRpc : IRpcCommand { public byte Item, Count; }
    /// <summary>Host → everyone: a chest has been opened.</summary>
    public struct ChestOpenedRpc : IRpcCommand { public int Socket; }

    /// <summary>
    /// Items in the world, picking up and the inventory (#26, P-11).
    /// <list type="bullet">
    /// <item>World items: two seeded starter items by every house door (from the door's index, so every machine makes the
    /// same ones without sending them), plus what's dropped and what chests give, which the host sends. Drawn as small
    /// coloured blocks within 60 m.</item>
    /// <item>Picking up: facing an item within 2 m, the Use key (P-10's, shared with doors: the nearer one wins). The host
    /// decides: it removes the item for everyone (ItemGoneRpc) and grants it to the picker (GrantRpc). Solo decides alone.</item>
    /// <item>Inventory: 16 slots with stacks, this machine's own for the session (no disk save). Tab or I shows it; click a
    /// slot (or select with the arrows and press Use) to drop one of it on the ground in front of the player.</item>
    /// <item>Temple chests: the sunken temple's Chest sockets (TempleSockets) open with Use; the host lays out 1-3
    /// items seeded from the socket's seed, so the contents are the same on every machine.</item>
    /// </list>
    /// For the animal lane's dungeon spawns: <see cref="Spawn"/> (host or solo) puts an item in the world for everyone.
    /// Test flags: "-pickup N" picks up the N nearest items (any distance) through the same path; "-drop" drops one from
    /// the first slot afterwards; "-openchest" opens the nearest temple chest; "-inventory" shows the panel.
    /// </summary>
    public class Items : MonoBehaviour
    {
        public const float Reach = 2f, DrawRange = 60f;
        /// <summary>Uids: seeded door items are door*4+k; chest items ChestBase + socket*4+k; drops DropBase upwards.</summary>
        const int ChestBase = 1_000_000, DropBase = 2_000_000;

        struct WorldItem { public byte Item, Count; public float3 Pos; }

        static Items instance;
        /// <summary>The distance to the item the player faces (float.MaxValue: none), for the Use key's arbitration with doors.</summary>
        public static float TargetDistance = float.MaxValue;

        readonly Dictionary<int, WorldItem> world = new Dictionary<int, WorldItem>();
        /// <summary>Seeded items taken, and items added since (drops, chests): what a late joiner is sent.</summary>
        readonly HashSet<int> gone = new HashSet<int>();
        readonly Dictionary<int, WorldItem> added = new Dictionary<int, WorldItem>();
        readonly HashSet<int> openedChests = new HashSet<int>();
        readonly (byte item, int count)[] slots = new (byte, int)[16];
        BlobAssetReference<AnchorSetBlob> seededFor;
        readonly HashSet<Entity> sentTo = new HashSet<Entity>();
        World server, client;
        int nextDrop = DropBase;
        int target = -1, targetChest = -1;
        string prompt;
        bool panel;
        int selected;
        int pickupArg, pickupDone;
        bool dropArg, openChestArg;
        float testAt;
        readonly Dictionary<int, GameObject> shown = new Dictionary<int, GameObject>();
        GUIStyle promptStyle, slotStyle;
        static Material itemMaterial;
        static MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null)
                return;
            var go = new GameObject("Items");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            instance = go.AddComponent<Items>();
            var names = new List<string>();
            for (int i = 0; i < ItemTable.Items.Length; i++)
                names.Add($"{i} {ItemTable.Items[i].Name} (stack {ItemTable.Items[i].Stack}, {ItemTable.Items[i].Weight} kg)");
            Debug.Log("Items: table of " + ItemTable.Items.Length + ": " + string.Join(", ", names));
            for (int i = 0; i < instance.slots.Length; i++)
                instance.slots[i].item = 255;
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-pickup");
            if (at >= 0 && at + 1 < args.Length)
                int.TryParse(args[at + 1], out instance.pickupArg);
            instance.dropArg = System.Array.IndexOf(args, "-drop") >= 0;
            instance.openChestArg = System.Array.IndexOf(args, "-openchest") >= 0;
            instance.panel = System.Array.IndexOf(args, "-inventory") >= 0;
        }

        bool Hosting => server != null && server.IsCreated;
        bool Joined => !Hosting && client != null && client.IsCreated && client.IsClient();

        void Update()
        {
            if (NetWorlds.Server != server || NetWorlds.Client != client)
            {
                (server, client) = (NetWorlds.Server, NetWorlds.Client);
                sentTo.Clear();
                if (Joined)
                {
                    // A joining client takes the host's world: back to the seeded items, then what the host sends.
                    gone.Clear();
                    added.Clear();
                    openedChests.Clear();
                    seededFor = default;
                }
            }
            if (!TryTerrain(out var terrain))
                return;
            if (Hosting)
                Serve();
            if (client != null && client.IsCreated && client.IsClient())
                Receive();

            FindTarget();
            // (The Use key goes to the nearer of a door and an item, judged on last frame's distances.)
            if ((target >= 0 || targetChest >= 0) && TargetDistance <= Doors.FacingDistance && UseKey.WasPressed())
            {
                if (target >= 0) Pickup(target);
                else OpenChest(targetChest);
            }
            Tests(terrain);
            UpdateInventoryKeys();
            Draw();
        }

        bool TryTerrain(out TerrainSource terrain)
        {
            terrain = default;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return false;
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (!q.TryGetSingleton(out terrain) || !terrain.Anchors.IsCreated || Punchy.Terrain.IslandV2.ActiveVersion == 0)
                return false;
            if (!seededFor.Equals(terrain.Anchors))
                Seed(terrain);
            return true;
        }

        /// <summary>Two starter items by every house door, from the door's index: the same on every machine.</summary>
        void Seed(in TerrainSource terrain)
        {
            seededFor = terrain.Anchors;
            world.Clear();
            ref var anchors = ref terrain.Anchors.Value.Anchors;
            int door = 0;
            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i].Kind != AnchorKind.Door)
                    continue;
                for (int k = 0; k < 2; k++)
                {
                    var h = math.hash(new uint3((uint)door, (uint)k, 0x17E35u));
                    var angle = (h & 0xFFFF) / 65535f * math.PI * 2f;
                    var dist = 3f + (h >> 16 & 0xFF) / 255f * 5f;
                    var xz = anchors[i].Position.xz + new float2(math.cos(angle), math.sin(angle)) * dist;
                    var kind = (byte)((h >> 24) % 7);   // (not keys: those come from chests)
                    var uid = door * 4 + k;
                    if (!gone.Contains(uid))
                        world[uid] = new WorldItem { Item = kind, Count = (byte)(kind == 2 ? 3 : 1), Pos = new float3(xz.x, terrain.SampleHeight(xz), xz.y) };
                }
                door++;
            }
            foreach (var kv in added)
                world[kv.Key] = kv.Value;
            Debug.Log($"Items: {world.Count} items in the world ({door} doors x 2 seeded, {added.Count} added, {gone.Count} taken)");
        }

        // ------------------------------------------------------------------ the player's side

        bool TryPlayer(out LocalTransform player)
        {
            player = default;
            var w = World.DefaultGameObjectInjectionWorld;
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            return q.TryGetSingleton(out player);
        }

        void FindTarget()
        {
            target = targetChest = -1;
            prompt = null;
            TargetDistance = float.MaxValue;
            if (!TryPlayer(out var player))
                return;
            var forward = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz);
            foreach (var kv in world)
            {
                var to = kv.Value.Pos - player.Position;
                var d = math.length(to.xz);
                if (d >= TargetDistance || d > Reach || math.abs(to.y) > 1.5f || d > 0.4f && math.dot(math.normalizesafe(to.xz), forward) < 0.3f)
                    continue;
                (TargetDistance, target) = (d, kv.Key);
            }
            foreach (var (socket, pos) in ChestSockets())
            {
                if (openedChests.Contains(socket))
                    continue;
                var d = math.distance(pos.xz, player.Position.xz);
                if (d < TargetDistance && d <= Reach && math.abs(pos.y - player.Position.y) < 1.5f)
                    (TargetDistance, target, targetChest) = (d, -1, socket);
            }
            if (TargetDistance > Doors.FacingDistance)
                return;
            if (target >= 0)
            {
                var it = world[target];
                prompt = $"{UseKey.Name}  pick up {ItemTable.Items[it.Item].Name.ToLowerInvariant()}{(it.Count > 1 ? $" x{it.Count}" : "")}";
            }
            else if (targetChest >= 0)
                prompt = $"{UseKey.Name}  open the chest";
        }

        /// <summary>The temple's chest sockets: (index in its DungeonSocket buffer, position).</summary>
        IEnumerable<(int, float3)> ChestSockets()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            if (q.IsEmpty)
                yield break;
            var buffer = w.EntityManager.GetBuffer<DungeonSocket>(q.GetSingletonEntity(), true);
            var list = new List<(int, float3)>();
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Kind == SocketKind.Chest)
                    list.Add((i, buffer[i].Pos));
            foreach (var c in list)
                yield return c;
        }

        bool TryChest(int socket, out DungeonSocket s)
        {
            s = default;
            var w = World.DefaultGameObjectInjectionWorld;
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            if (q.IsEmpty)
                return false;
            var buffer = w.EntityManager.GetBuffer<DungeonSocket>(q.GetSingletonEntity(), true);
            if (socket < 0 || socket >= buffer.Length || buffer[socket].Kind != SocketKind.Chest)
                return false;
            s = buffer[socket];
            return true;
        }

        void Pickup(int uid)
        {
            if (Joined)
            {
                SendToHost(new PickupRpc { Uid = uid });
                Debug.Log($"Items: asked the host for item {uid}");
                return;
            }
            if (Take(uid, out var it))
                Grant(it.Item, it.Count);
        }

        void OpenChest(int socket)
        {
            if (Joined)
            {
                SendToHost(new OpenChestRpc { Socket = socket });
                Debug.Log($"Items: asked the host to open chest {socket}");
                return;
            }
            HostOpenChest(socket);
        }

        /// <summary>Into the inventory: onto a stack of the same item with room, else the first empty slot.</summary>
        void Grant(byte item, int count)
        {
            var stack = ItemTable.Items[item].Stack;
            for (int i = 0; i < slots.Length && count > 0; i++)
                if (slots[i].item == item && slots[i].count < stack)
                {
                    var n = math.min(count, stack - slots[i].count);
                    slots[i].count += n;
                    count -= n;
                }
            for (int i = 0; i < slots.Length && count > 0; i++)
                if (slots[i].item == 255)
                {
                    var n = math.min(count, stack);
                    slots[i] = (item, n);
                    count -= n;
                }
            Debug.Log($"Items: picked up {ItemTable.Items[item].Name}; inventory: {DescribeInventory()}");
        }

        string DescribeInventory()
        {
            var parts = new List<string>();
            foreach (var s in slots)
                if (s.item != 255)
                    parts.Add($"{ItemTable.Items[s.item].Name} x{s.count}");
            return parts.Count > 0 ? string.Join(", ", parts) : "empty";
        }

        /// <summary>One of slot <paramref name="slot"/>'s items on the ground 1 m in front of the player.</summary>
        void Drop(int slot)
        {
            if (slot < 0 || slot >= slots.Length || slots[slot].item == 255 || !TryPlayer(out var player) || !TryTerrain(out var terrain))
                return;
            var item = slots[slot].item;
            if (--slots[slot].count <= 0)
                slots[slot] = (255, 0);
            var xz = player.Position.xz + math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz) * 1f;
            var pos = new float3(xz.x, terrain.SampleHeight(xz), xz.y);
            Debug.Log($"Items: dropped {ItemTable.Items[item].Name}; inventory: {DescribeInventory()}");
            if (Joined)
                SendToHost(new DropRpc { Item = item, Count = 1, Pos = pos });
            else
                Spawn(item, 1, pos);
        }

        void SendToHost<T>(T rpc) where T : unmanaged, IRpcCommand
        {
            var em = client.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NetworkId));
            if (q.IsEmpty)
                return;
            var e = em.CreateEntity(typeof(T), typeof(SendRpcCommandRequest));
            em.SetComponentData(e, rpc);
            em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = q.GetSingletonEntity() });
        }

        // ------------------------------------------------------------------ the host's side (solo too)

        /// <summary>
        /// For dungeon spawns and drops (host or solo only; a client's call is ignored): an item in the world, for
        /// everyone. Returns its uid, or -1.
        /// </summary>
        public static int Spawn(byte item, int count, float3 pos)
        {
            if (instance == null || instance.Joined || item >= ItemTable.Items.Length)
                return -1;
            var uid = instance.nextDrop++;
            instance.Add(uid, item, count, pos);
            return uid;
        }

        void Add(int uid, byte item, int count, float3 pos)
        {
            var w = new WorldItem { Item = item, Count = (byte)math.clamp(count, 1, 255), Pos = pos };
            world[uid] = w;
            added[uid] = w;
            Debug.Log($"Items: item {uid} ({ItemTable.Items[item].Name} x{w.Count}) on the ground at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})");
            if (Hosting)
                Broadcast(new ItemSpawnRpc { Uid = uid, Item = item, Count = w.Count, Pos = pos });
        }

        bool Take(int uid, out WorldItem it)
        {
            if (!world.TryGetValue(uid, out it))
                return false;
            world.Remove(uid);
            if (!added.Remove(uid))
                gone.Add(uid);
            Debug.Log($"Items: item {uid} ({ItemTable.Items[it.Item].Name}) taken{(Hosting ? " (host)" : "")}");
            if (Hosting)
                Broadcast(new ItemGoneRpc { Uid = uid });
            return true;
        }

        /// <summary>A chest's contents: 1-3 items from the socket's seed, laid out in front of it. The same everywhere.</summary>
        void HostOpenChest(int socket)
        {
            if (!openedChests.Add(socket) || !TryChest(socket, out var s))
                return;
            var rng = Unity.Mathematics.Random.CreateFromIndex(s.Seed);
            var n = rng.NextInt(1, 4);
            var face = math.mul(s.Rot, new float3(0f, 0f, 1f));
            var side = math.cross(new float3(0f, 1f, 0f), face);
            var names = new List<string>();
            for (int k = 0; k < n; k++)
            {
                // (Coins, keys and finds: more coins than anything.)
                var roll = rng.NextInt(0, 10);
                byte item = roll < 4 ? (byte)6 : roll < 6 ? ItemTable.Key : (byte)rng.NextInt(0, 6);
                var count = item == 6 ? rng.NextInt(3, 12) : 1;
                var pos = s.Pos + face * 0.7f + side * ((k - (n - 1) * 0.5f) * 0.45f);
                Add(ChestBase + socket * 4 + k, item, count, pos);
                names.Add($"{ItemTable.Items[item].Name} x{count}");
            }
            Debug.Log($"Items: chest {socket} opened (seed {s.Seed:X8}): {string.Join(", ", names)}");
            if (Hosting)
                Broadcast(new ChestOpenedRpc { Socket = socket });
        }

        void Serve()
        {
            var em = server.EntityManager;
            using (var q = em.CreateEntityQuery(typeof(PickupRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var uid = em.GetComponentData<PickupRpc>(r).Uid;
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    if (Take(uid, out var it) && em.Exists(from))
                        Send(em, from, new GrantRpc { Item = it.Item, Count = it.Count });
                    em.DestroyEntity(r);
                }
            using (var q = em.CreateEntityQuery(typeof(DropRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var d = em.GetComponentData<DropRpc>(r);
                    Spawn(d.Item, d.Count, d.Pos);
                    em.DestroyEntity(r);
                }
            using (var q = em.CreateEntityQuery(typeof(OpenChestRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    HostOpenChest(em.GetComponentData<OpenChestRpc>(r).Socket);
                    em.DestroyEntity(r);
                }
            // A player who has just joined: what's been taken, added and opened so far.
            using (var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame)))
            using (var cs = q.ToEntityArray(Allocator.Temp))
                foreach (var c in cs)
                {
                    if (!sentTo.Add(c))
                        continue;
                    foreach (var uid in gone) Send(em, c, new ItemGoneRpc { Uid = uid });
                    foreach (var kv in added) Send(em, c, new ItemSpawnRpc { Uid = kv.Key, Item = kv.Value.Item, Count = kv.Value.Count, Pos = kv.Value.Pos });
                    foreach (var s in openedChests) Send(em, c, new ChestOpenedRpc { Socket = s });
                }
        }

        void Broadcast<T>(T rpc) where T : unmanaged, IRpcCommand
        {
            var em = server.EntityManager;
            using var q = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            using var cs = q.ToEntityArray(Allocator.Temp);
            foreach (var c in cs)
                Send(em, c, rpc);
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            var e = em.CreateEntity(typeof(T), typeof(SendRpcCommandRequest));
            em.SetComponentData(e, rpc);
            em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }

        /// <summary>The host's word, on a client (a host's own client gets the broadcasts too: they change nothing there).</summary>
        void Receive()
        {
            var em = client.EntityManager;
            var joined = Joined;
            void Each<T>(System.Action<T> act) where T : unmanaged, IRpcCommand
            {
                using var q = em.CreateEntityQuery(typeof(T), typeof(ReceiveRpcCommandRequest));
                using var rs = q.ToEntityArray(Allocator.Temp);
                foreach (var r in rs)
                {
                    if (joined)
                        act(em.GetComponentData<T>(r));
                    em.DestroyEntity(r);
                }
            }
            Each<ItemGoneRpc>(m =>
            {
                if (world.Remove(m.Uid))
                    Debug.Log($"Items: item {m.Uid} gone (from the host)");
                if (!added.Remove(m.Uid)) gone.Add(m.Uid);
            });
            Each<ItemSpawnRpc>(m =>
            {
                var w = new WorldItem { Item = m.Item, Count = m.Count, Pos = m.Pos };
                world[m.Uid] = w;
                added[m.Uid] = w;
                Debug.Log($"Items: item {m.Uid} ({ItemTable.Items[m.Item].Name} x{m.Count}) appeared (from the host)");
            });
            Each<GrantRpc>(m => Grant(m.Item, m.Count));
            Each<ChestOpenedRpc>(m =>
            {
                if (openedChests.Add(m.Socket))
                    Debug.Log($"Items: chest {m.Socket} opened (from the host)");
            });
        }

        // ------------------------------------------------------------------ tests, keys, drawing

        void Tests(in TerrainSource terrain)
        {
            if (pickupArg <= 0 && !dropArg && !openChestArg)
                return;
            testAt = testAt == 0f ? Time.realtimeSinceStartup + 30f : testAt;
            if (Time.realtimeSinceStartup < testAt || !TryPlayer(out var player))
                return;
            if (Joined)
            {
                using var q = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamInGame));
                if (q.IsEmpty)
                    return;
            }
            testAt = Time.realtimeSinceStartup + 1.5f;
            if (openChestArg)
            {
                openChestArg = false;
                var best = -1;
                var bestD = float.MaxValue;
                foreach (var (socket, pos) in ChestSockets())
                {
                    var d = math.distance(pos, player.Position);
                    if (d < bestD) (bestD, best) = (d, socket);
                }
                Debug.Log($"Items: -openchest: chest {best}, {bestD:F1} m away");
                if (best >= 0) OpenChest(best);
                return;
            }
            if (pickupDone < pickupArg)
            {
                var best = -1;
                var bestD = float.MaxValue;
                foreach (var kv in world)
                {
                    var d = math.distance(kv.Value.Pos, player.Position);
                    if (d < bestD) (bestD, best) = (d, kv.Key);
                }
                pickupDone++;
                Debug.Log($"Items: -pickup {pickupDone}/{pickupArg}: item {best}, {bestD:F1} m away");
                if (best >= 0) Pickup(best);
                return;
            }
            if (dropArg)
            {
                dropArg = false;
                Debug.Log("Items: -drop: one from slot 0");
                Drop(0);
            }
        }

        void UpdateInventoryKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Punchy.UI.Overlay.AnyOpen)
                return;
            if (keyboard.tabKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame)
                panel = !panel;
            if (!panel)
                return;
            if (keyboard.rightArrowKey.wasPressedThisFrame) selected = (selected + 1) % slots.Length;
            if (keyboard.leftArrowKey.wasPressedThisFrame) selected = (selected + slots.Length - 1) % slots.Length;
            if (keyboard.downArrowKey.wasPressedThisFrame) selected = (selected + 4) % slots.Length;
            if (keyboard.upArrowKey.wasPressedThisFrame) selected = (selected + slots.Length - 4) % slots.Length;
            if (target < 0 && targetChest < 0 && UseKey.WasPressed())
                Drop(selected);
        }

        /// <summary>Small coloured blocks for the items within DrawRange of the camera (a pool of primitives).</summary>
        void Draw()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var near = new HashSet<int>();
            foreach (var kv in world)
                if (math.distancesq(kv.Value.Pos, (float3)cam.transform.position) < DrawRange * DrawRange)
                    near.Add(kv.Key);
            var drop = new List<int>();
            foreach (var kv in shown)
                if (!near.Contains(kv.Key) || !world.ContainsKey(kv.Key))
                    drop.Add(kv.Key);
            foreach (var uid in drop)
            {
                Destroy(shown[uid]);
                shown.Remove(uid);
            }
            foreach (var uid in near)
            {
                if (shown.ContainsKey(uid))
                    continue;
                var it = world[uid];
                var go = GameObject.CreatePrimitive(it.Item == 2 || it.Item == 6 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "Item " + uid;
                go.hideFlags = HideFlags.HideInHierarchy;
                var size = it.Item == 0 ? new Vector3(0.08f, 0.08f, 0.6f) : it.Item == 5 ? new Vector3(0.15f, 0.12f, 0.45f) : Vector3.one * 0.2f;
                go.transform.localScale = size;
                go.transform.position = (Vector3)it.Pos + Vector3.up * (size.y * 0.5f + 0.02f);
                go.transform.rotation = Quaternion.Euler(0f, uid * 37 % 360, 0f);
                // (Items/Resources/ItemMaterial: the pipeline's own lit material. A primitive's default material has no URP
                // shader in a player build, and drew magenta.)
                var r = go.GetComponent<Renderer>();
                itemMaterial ??= Resources.Load<Material>("ItemMaterial");
                if (itemMaterial != null)
                    r.sharedMaterial = itemMaterial;
                block ??= new MaterialPropertyBlock();
                block.SetColor(BaseColorId, ItemTable.Items[it.Item].Colour);
                r.SetPropertyBlock(block);
                shown[uid] = go;
            }
        }

        void OnGUI()
        {
            if (Punchy.UI.Overlay.AnyOpen)
                return;
            if (prompt != null)
            {
                promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
                var size = promptStyle.CalcSize(new GUIContent(prompt)) + new Vector2(24f, 10f);
                GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height * 0.72f, size.x, size.y), prompt, promptStyle);
            }
            if (!panel)
                return;
            slotStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.LowerCenter, wordWrap = true };
            const float cell = 74f, gap = 6f;
            var w = 4 * cell + 5 * gap;
            var origin = new Vector2(Screen.width - w - 20f, Screen.height * 0.25f);
            GUI.Box(new Rect(origin.x, origin.y - 28f, w, 4 * (cell + gap) + gap + 28f), $"Inventory   (click or {UseKey.Name}: drop one)");
            var colour = GUI.color;
            for (int i = 0; i < slots.Length; i++)
            {
                var rect = new Rect(origin.x + gap + i % 4 * (cell + gap), origin.y + gap + i / 4 * (cell + gap), cell, cell);
                GUI.color = i == selected ? new Color(1f, 0.9f, 0.5f) : Color.white;
                var s = slots[i];
                if (GUI.Button(rect, s.item == 255 ? "" : $"{ItemTable.Items[s.item].Name}\nx{s.count}", slotStyle))
                {
                    selected = i;
                    Drop(i);
                }
                if (s.item != 255)
                {
                    GUI.color = ItemTable.Items[s.item].Colour;
                    GUI.DrawTexture(new Rect(rect.x + cell * 0.3f, rect.y + 8f, cell * 0.4f, cell * 0.35f), Texture2D.whiteTexture);
                }
            }
            GUI.color = colour;
        }
    }
}
