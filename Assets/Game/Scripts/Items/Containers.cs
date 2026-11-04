using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>Client → host: put Count of Item into container Container.</summary>
    public struct PutRpc : IRpcCommand { public int Container; public byte Item, Count; }
    /// <summary>Client → host: take what's in slot Slot of container Container.</summary>
    public struct TakeRpc : IRpcCommand { public int Container; public byte Slot; }
    /// <summary>Host → everyone: slot Slot of container Container now holds Count of Item (Item 255: empty).</summary>
    public struct ContainerSlotRpc : IRpcCommand { public int Container; public byte Slot, Item, Count; }

    /// <summary>
    /// Object inventories (#26, P-14): containers of <see cref="ContainerSlots"/> slots, the temple's chests (once opened)
    /// and the storage in house rooms (Interiors). Every machine fills a container from its seed when it's registered, so
    /// nothing is sent until something changes; then the host owns it: a client asks (PutRpc / TakeRpc), the host
    /// changes the slot and tells everyone (ContainerSlotRpc), and a player who joins later is sent every changed
    /// container. Open one with the Use key within reach: the inventory screen shows its slots beside the player's;
    /// clicking one of yours puts one in, clicking one of its takes that stack.
    /// Dropped items (P-11's) stay for everyone and for late joiners; at most <see cref="MaxDrops"/> are kept, the
    /// oldest go first.
    /// Test flags: "-container N" picks container N for "-putitem" (puts slot 0's item in, 35 s in) and "-takeitem"
    /// (takes the last filled slot, 50 s in).
    /// </summary>
    public partial class Items
    {
        public const int ContainerSlots = 8, MaxDrops = 256;
        /// <summary>Container ids: a temple chest is its socket index; room storage RoomBase + room * 4 + k.</summary>
        public const int RoomBase = 100_000;

        class Container
        {
            public string Name;
            public float3 Pos;
            public bool IsChest;
            public (byte item, int count)[] Slots = new (byte, int)[ContainerSlots];
        }

        readonly Dictionary<int, Container> containers = new Dictionary<int, Container>();
        readonly HashSet<int> changedContainers = new HashSet<int>();
        readonly Queue<int> dropOrder = new Queue<int>();
        int openContainer = -1, targetContainer = -1;
        int containerArg = -1;
        bool putArg, takeArg;
        float putAt, takeAt;

        /// <summary>
        /// A container at <paramref name="pos"/>, filled from <paramref name="seed"/> (0-3 starter items, now and then a
        /// small surprise). Called the same way on every machine (room storage from Interiors; chests from their sockets).
        /// </summary>
        public static void RegisterContainer(int id, float3 pos, string name, uint seed, bool isChest = false)
        {
            if (instance == null || instance.containers.ContainsKey(id))
                return;
            var c = new Container { Name = name, Pos = pos, IsChest = isChest };
            for (int i = 0; i < ContainerSlots; i++)
                c.Slots[i] = (255, 0);
            var rng = Unity.Mathematics.Random.CreateFromIndex(math.max(seed, 1u));
            var n = isChest ? rng.NextInt(1, 4) : rng.NextInt(0, 4);
            for (int k = 0; k < n; k++)
            {
                // (Coins and finds; a key or a purse of coins now and then: the surprise.)
                var roll = rng.NextInt(0, 20);
                byte item = roll == 0 ? ItemTable.Key : roll < 6 ? (byte)6 : (byte)rng.NextInt(0, 6);
                var count = item == 6 ? (roll == 1 ? 25 : rng.NextInt(2, 9)) : rng.NextInt(1, 3);
                c.Slots[k] = (item, count);
            }
            instance.containers[id] = c;
        }

        /// <summary>A container's contents as text ("Coin x4, Stick x1", "empty"), or "?" if there's none by that id.</summary>
        public static string DescribeContainer(int id) =>
            instance != null && instance.containers.TryGetValue(id, out var c) ? Describe(c.Slots) : "?";

        static string Describe((byte item, int count)[] slots)
        {
            var parts = new List<string>();
            foreach (var s in slots)
                if (s.item != 255)
                    parts.Add($"{ItemTable.Items[s.item].Name} x{s.count}");
            return parts.Count > 0 ? string.Join(", ", parts) : "empty";
        }

        /// <summary>The temple chests as containers (once the dungeon sockets are in), seeded from each socket's seed.</summary>
        void RegisterChests()
        {
            foreach (var (socket, pos) in ChestSockets())
                if (!containers.ContainsKey(socket) && TryChest(socket, out var s))
                    RegisterContainer(socket, pos, "chest", s.Seed, true);
        }

        /// <summary>The nearest open-able container within reach (chests only once opened), for the Use key.</summary>
        void FindContainer(float3 player, ref float distance)
        {
            targetContainer = -1;
            foreach (var kv in containers)
            {
                if (kv.Value.IsChest && !openedChests.Contains(kv.Key))
                    continue;
                var d = math.distance(kv.Value.Pos.xz, player.xz);
                if (d < distance && d <= Reach && math.abs(kv.Value.Pos.y - player.y) < 1.8f)
                    (distance, targetContainer) = (d, kv.Key);
            }
        }

        void OpenContainer(int id)
        {
            openContainer = id;
            panel = true;
            nav.Reset();
            Debug.Log($"Items: looking in container {id} ({containers[id].Name}): {Describe(containers[id].Slots)}");
        }

        /// <summary>One of the player's slot into the open container (the host decides where it goes).</summary>
        void Put(int slot)
        {
            if (openContainer < 0 || slot < 0 || slot >= slots.Length || slots[slot].item == 255)
                return;
            var item = slots[slot].item;
            if (--slots[slot].count <= 0)
                slots[slot] = (255, 0);
            Debug.Log($"Items: put {ItemTable.Items[item].Name} into container {openContainer}; inventory: {DescribeInventory()}");
            if (Joined)
                SendToHost(new PutRpc { Container = openContainer, Item = item, Count = 1 });
            else if (!HostPut(openContainer, item, 1, Hosting ? "host" : "solo"))
                Grant(item, 1);   // (full: back it comes)
        }

        /// <summary>A container slot's stack into the inventory.</summary>
        void TakeFromContainer(int id, int slot)
        {
            if (!containers.TryGetValue(id, out var c) || slot < 0 || slot >= ContainerSlots || c.Slots[slot].item == 255)
                return;
            if (Joined)
            {
                SendToHost(new TakeRpc { Container = id, Slot = (byte)slot });
                Debug.Log($"Items: asked the host for container {id} slot {slot}");
                return;
            }
            var s = c.Slots[slot];
            SetSlot(id, slot, 255, 0, Hosting ? "host" : "solo");
            Grant(s.item, s.count);
        }

        bool HostPut(int id, byte item, int count, string who)
        {
            if (!containers.TryGetValue(id, out var c))
                return false;
            var stack = ItemTable.Items[item].Stack;
            for (int i = 0; i < ContainerSlots; i++)
                if (c.Slots[i].item == item && c.Slots[i].count + count <= stack)
                {
                    SetSlot(id, i, item, c.Slots[i].count + count, who);
                    return true;
                }
            for (int i = 0; i < ContainerSlots; i++)
                if (c.Slots[i].item == 255)
                {
                    SetSlot(id, i, item, count, who);
                    return true;
                }
            return false;
        }

        void SetSlot(int id, int slot, byte item, int count, string who)
        {
            var c = containers[id];
            c.Slots[slot] = (item, count);
            changedContainers.Add(id);
            Debug.Log($"Items: container {id} slot {slot}: {(item == 255 ? "empty" : $"{ItemTable.Items[item].Name} x{count}")} ({who})");
            if (Hosting)
                Broadcast(new ContainerSlotRpc { Container = id, Slot = (byte)slot, Item = item, Count = (byte)math.min(count, 255) });
        }

        /// <summary>The host's side of containers, and the drop cap (called from Serve).</summary>
        void ServeContainers(EntityManager em)
        {
            using (var q = em.CreateEntityQuery(typeof(PutRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<PutRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    Carry(PlayerId(em, from), m.Item, -m.Count);
                    if (!HostPut(m.Container, m.Item, m.Count, "host, for a client") && em.Exists(from))
                    {
                        Send(em, from, new GrantRpc { Item = m.Item, Count = m.Count });   // (full: back to them)
                        Carry(PlayerId(em, from), m.Item, m.Count);
                    }
                    em.DestroyEntity(r);
                }
            using (var q = em.CreateEntityQuery(typeof(TakeRpc), typeof(ReceiveRpcCommandRequest)))
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<TakeRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    if (containers.TryGetValue(m.Container, out var c) && m.Slot < ContainerSlots && c.Slots[m.Slot].item != 255 && em.Exists(from))
                    {
                        var s = c.Slots[m.Slot];
                        SetSlot(m.Container, m.Slot, 255, 0, "host, for a client");
                        Carry(PlayerId(em, from), s.item, s.count);
                        Send(em, from, new GrantRpc { Item = s.item, Count = (byte)math.min(s.count, 255) });
                    }
                    em.DestroyEntity(r);
                }
        }

        /// <summary>Every changed container, to a player who has just joined.</summary>
        void SendContainers(EntityManager em, Entity connection)
        {
            foreach (var id in changedContainers)
            {
                var c = containers[id];
                for (int i = 0; i < ContainerSlots; i++)
                    Send(em, connection, new ContainerSlotRpc { Container = id, Slot = (byte)i, Item = c.Slots[i].item, Count = (byte)math.min(c.Slots[i].count, 255) });
            }
        }

        void ReceiveContainer(ContainerSlotRpc m)
        {
            if (!containers.TryGetValue(m.Container, out var c) || m.Slot >= ContainerSlots)
                return;
            if (c.Slots[m.Slot].item == m.Item && c.Slots[m.Slot].count == m.Count)
                return;
            c.Slots[m.Slot] = (m.Item, m.Count);
            changedContainers.Add(m.Container);
            Debug.Log($"Items: container {m.Container} slot {m.Slot}: {(m.Item == 255 ? "empty" : $"{ItemTable.Items[m.Item].Name} x{m.Count}")} (from the host)");
        }

        /// <summary>A drop joins the list; past MaxDrops the oldest still lying there goes, for everyone.</summary>
        void NoteDrop(int uid)
        {
            dropOrder.Enqueue(uid);
            while (dropOrder.Count > MaxDrops)
            {
                var old = dropOrder.Dequeue();
                if (world.Remove(old) && added.Remove(old))
                {
                    Debug.Log($"Items: more than {MaxDrops} dropped items: item {old} (the oldest) cleared");
                    if (Hosting)
                        Broadcast(new ItemGoneRpc { Uid = old });
                }
            }
        }

        void ContainerTests()
        {
            if (containerArg < 0 || !containers.ContainsKey(containerArg))
                return;
            if (Joined)
            {
                using var q = client.EntityManager.CreateEntityQuery(typeof(NetworkStreamInGame));
                if (q.IsEmpty)
                    return;
            }
            var now = Time.realtimeSinceStartup;
            if (putArg && now > (putAt = putAt == 0f ? now + 35f : putAt))
            {
                putArg = false;
                openContainer = containerArg;
                var slot = System.Array.FindIndex(slots, s => s.item != 255);
                Debug.Log($"Items: -putitem: slot {slot} into container {containerArg} ({Describe(containers[containerArg].Slots)})");
                Put(slot);
            }
            if (takeArg && now > (takeAt = takeAt == 0f ? now + 50f : takeAt))
            {
                takeArg = false;
                var c = containers[containerArg];
                var last = -1;
                for (int i = 0; i < ContainerSlots; i++)
                    if (c.Slots[i].item != 255) last = i;
                Debug.Log($"Items: -takeitem: container {containerArg} slot {last} ({Describe(c.Slots)})");
                TakeFromContainer(containerArg, last);
            }
        }

        /// <summary>The open container's pane, left of the inventory: its slots as MenuNav stops after the player's 16.</summary>
        void DrawContainerPane(float cell, float gap, Rect inventory)
        {
            if (openContainer < 0 || !containers.TryGetValue(openContainer, out var c))
                return;
            var w = 4 * cell + 5 * gap;
            var area = new Rect(inventory.x - w - 12f, inventory.y, w, 2 * (cell + gap) + gap + 28f);
            GUI.Box(area, $"{char.ToUpper(c.Name[0])}{c.Name.Substring(1)}   (click: take)");
            GUILayout.BeginArea(new Rect(area.x + gap, area.y + 28f, w - 2 * gap, area.height - 28f));
            var colour = GUI.color;
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 4; col++)
                {
                    var i = row * 4 + col;
                    var s = c.Slots[i];
                    if (nav.Button(s.item == 255 ? "" : $"{ItemTable.Items[s.item].Name}\nx{s.count}", slotStyle, GUILayout.Width(cell), GUILayout.Height(cell)))
                        TakeFromContainer(openContainer, i);
                    if (s.item != 255 && Event.current.type == EventType.Repaint)
                    {
                        var rect = GUILayoutUtility.GetLastRect();
                        GUI.color = ItemTable.Items[s.item].Colour;
                        GUI.DrawTexture(new Rect(rect.x + cell * 0.3f, rect.y + 8f, cell * 0.4f, cell * 0.35f), Texture2D.whiteTexture);
                        GUI.color = colour;
                    }
                    GUILayout.Space(gap);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
            GUILayout.EndArea();
        }
    }
}
