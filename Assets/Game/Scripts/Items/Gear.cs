using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Items
{
    /// <summary>Where a piece of gear goes on the body (P-23); None for ordinary items.</summary>
    public enum GearSlot : byte { None, Hand, Head, Body, Back }

    /// <summary>Client → host: what this player has on now (item ids; 255 for nothing).</summary>
    public struct GearRpc : IRpcCommand
    {
        public byte Hand, Head, Back;
    }

    /// <summary>Host → everyone: what a player has on.</summary>
    public struct GearStateRpc : IRpcCommand
    {
        public int Player;
        public byte Hand, Head, Back;
    }

    /// <summary>
    /// Gear (P-23 row 2): items with a Slot go on the body from the inventory (X or right click on the slot, X on a pad;
    /// the worn row under the slots takes them off). What each piece does is in its ItemDef: a torch's Light is the reach of
    /// its light, a pack's Slots are extra inventory rows; a staff's Reach and a hat's Cover are numbers for now, because
    /// the game has no use for them yet. Only an upright body with hands can wear gear. The body shows it through
    /// CreatureState.Held (torch, staff: drawn and lit as the folk's are, so a torch goes through LightBudget) and Worn
    /// (hat, pack). A client tells the host, and the host tells everyone (GearRpc, GearStateRpc), so others see it.
    /// Test flag: "-equip Torch,Hat" gives and puts on those items after 20 s.
    /// </summary>
    public partial class Items
    {
        const byte Nothing = 255;
        const int BaseSlots = 16, MaxExtraSlots = 8;
        /// <summary>What's on, by GearSlot.</summary>
        readonly byte[] worn = { Nothing, Nothing, Nothing, Nothing, Nothing };
        /// <summary>What each player has on (Hand, Head, Back), by network id: the host's word.</summary>
        readonly Dictionary<int, (byte Hand, byte Head, byte Back)> gearOf = new Dictionary<int, (byte, byte, byte)>();
        readonly HashSet<Entity> gearSentTo = new HashSet<Entity>();
        string equipArg;
        bool gearLootArg;

        int Capacity => BaseSlots + (worn[(int)GearSlot.Back] != Nothing ? math.min(ItemTable.Items[worn[(int)GearSlot.Back]].Slots, MaxExtraSlots) : 0);

        static HeldItem HeldFor(byte item) => item == ItemTable.Torch ? HeldItem.Torch : item == ItemTable.Staff ? HeldItem.Staff : HeldItem.None;

        static byte WornBits(byte head, byte back) => (byte)((head != Nothing ? CreatureWorn.Hat : 0) | (back != Nothing ? CreatureWorn.Pack : 0));

        bool CanWearGear()
        {
            var fauna = FindAnyObjectByType<FaunaDirector>();
            var body = fauna != null ? fauna.PlayerSpecies : null;
            return body == null || body.Body.Upright && body.Body.Parts.Exists(p => p != null && p.Limb != null && p.Limb.Role == LimbRole.Arm);
        }

        /// <summary>Puts on the item in this inventory slot; what was on goes back to the inventory.</summary>
        void Equip(int slot)
        {
            if (slot < 0 || slot >= Capacity || slots[slot].item == Nothing)
                return;
            var item = slots[slot].item;
            var place = ItemTable.Items[item].Slot;
            if (place == GearSlot.None)
                return;
            if (!CanWearGear())
            {
                Debug.Log($"Items: this body can't wear the {ItemTable.Items[item].Name}");
                return;
            }
            var was = worn[(int)place];
            // (Swapping a pack: only if what's in its rows still fits.)
            if (was != Nothing && place == GearSlot.Back && !RowsFree(BaseSlots + math.min(ItemTable.Items[item].Slots, MaxExtraSlots)))
                return;
            if (--slots[slot].count <= 0)
                slots[slot] = (Nothing, 0);
            worn[(int)place] = item;
            if (was != Nothing)
                Grant(was, 1);
            GearChanged();
        }

        void Unequip(GearSlot place)
        {
            var item = worn[(int)place];
            if (item == Nothing)
                return;
            if (place == GearSlot.Back && !RowsFree(BaseSlots))
            {
                Debug.Log("Items: empty the pack's rows before taking it off");
                return;
            }
            worn[(int)place] = Nothing;
            Grant(item, 1);
            GearChanged();
        }

        /// <summary>Whether every slot from this one on is empty.</summary>
        bool RowsFree(int from)
        {
            for (int i = from; i < slots.Length; i++)
                if (slots[i].item != Nothing)
                    return false;
            return true;
        }

        void GearChanged()
        {
            byte hand = worn[(int)GearSlot.Hand], head = worn[(int)GearSlot.Head], back = worn[(int)GearSlot.Back];
            Debug.Log($"Items: wearing hand {GearName(hand)}, head {GearName(head)}, back {GearName(back)}; {Capacity} slots; inventory: {DescribeInventory()}");
            if (Joined)
                SendToHost(new GearRpc { Hand = hand, Head = head, Back = back });
            else if (Hosting)
                HostSetGear(OwnNetworkId(), hand, head, back);
        }

        static string GearName(byte item) => item == Nothing ? "-" : ItemTable.Items[item].Name;

        int OwnNetworkId()
        {
            var em = client.EntityManager;
            var q = Q(em, ComponentType.ReadWrite<NetworkId>());
            return q.IsEmpty ? 0 : q.GetSingleton<NetworkId>().Value;
        }

        void HostSetGear(int player, byte hand, byte head, byte back)
        {
            gearOf[player] = (hand, head, back);
            Broadcast(new GearStateRpc { Player = player, Hand = hand, Head = head, Back = back });
        }

        /// <summary>The host: clients' changes, and everything so far for whoever has just joined.</summary>
        void ServeGear(EntityManager em)
        {
            using (var rs = Q(em, ComponentType.ReadWrite<GearRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<GearRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                        HostSetGear(em.GetComponentData<NetworkId>(from).Value, m.Hand, m.Head, m.Back);
                    em.DestroyEntity(r);
                }
            using var connections = Q(em, ComponentType.ReadWrite<NetworkId>(), ComponentType.ReadWrite<NetworkStreamInGame>()).ToEntityArray(Allocator.Temp);
            foreach (var c in connections)
            {
                if (!gearSentTo.Add(c))
                    continue;
                foreach (var kv in gearOf)
                    Send(em, c, new GearStateRpc { Player = kv.Key, Hand = kv.Value.Hand, Head = kv.Value.Head, Back = kv.Value.Back });
            }
        }

        void OnGearState(GearStateRpc m) => gearOf[m.Player] = (m.Hand, m.Head, m.Back);

        /// <summary>Each frame: our own body and other players' wear what they have on (bodies are rebuilt without warning).</summary>
        void ApplyGear()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            var own = Q(em, ComponentType.ReadWrite<PlayerAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            if (own.TryGetSingletonEntity<PlayerAvatar>(out var avatar))
                Dress(em, avatar, worn[(int)GearSlot.Hand], worn[(int)GearSlot.Head], worn[(int)GearSlot.Back]);
            if (gearOf.Count == 0)
                return;
            var remote = Q(em, ComponentType.ReadWrite<RemoteAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var bodies = remote.ToEntityArray(Allocator.Temp);
            foreach (var e in bodies)
            {
                var ghost = em.GetComponentData<RemoteAvatar>(e).Ghost;
                if (!em.Exists(ghost) || !em.HasComponent<GhostOwner>(ghost) || !gearOf.TryGetValue(em.GetComponentData<GhostOwner>(ghost).NetworkId, out var g))
                    continue;
                Dress(em, e, g.Hand, g.Head, g.Back);
            }
        }

        static void Dress(EntityManager em, Entity body, byte hand, byte head, byte back)
        {
            var s = em.GetComponentData<CreatureState>(body);
            ref var blob = ref em.GetComponentData<CreatureBody>(body).Blob.Value;
            var wears = blob.Upright && blob.Hands;
            var held = wears ? HeldFor(hand) : HeldItem.None;
            var bits = wears ? WornBits(head, back) : (byte)0;
            if (s.Held == held && s.Worn == bits)
                return;
            s.Held = held;
            s.Worn = bits;
            em.SetComponentData(body, s);
            Debug.Log($"[gear] {(em.HasComponent<RemoteAvatar>(body) ? "another player's" : "our")} body: held {held}, worn {bits}");
        }

        void GearArgs(string[] args)
        {
            var at = System.Array.IndexOf(args, "-equip");
            if (at >= 0 && at + 1 < args.Length)
                equipArg = args[at + 1];
            gearLootArg = System.Array.IndexOf(args, "-gearloot") >= 0;
        }

        float budgetAt;

        void GearTest()
        {
            // (Punchy's glow budget, before the torch goes on and after: the same count every carried light goes through.)
            if (budgetAt > 0f && Time.realtimeSinceStartup >= budgetAt)
            {
                budgetAt = 0f;
                LogBudget("with gear");
            }
            if (equipArg == null || Time.realtimeSinceStartup < 20f)
                return;
            LogBudget("before");
            budgetAt = Time.realtimeSinceStartup + 4f;
            foreach (var name in equipArg.Split(','))
            {
                var id = ItemId(name);
                if (id < 0)
                    continue;
                Grant((byte)id, 1);
                Equip(System.Array.FindIndex(slots, s => s.item == id));
            }
            equipArg = null;
        }

        static void LogBudget(string when) =>
            Debug.Log($"[gear] light budget {when}: requested {Punchy.Settings.LightBudget.Requested}, slots {Punchy.Settings.LightBudget.SlotsUsed}, real {Punchy.Settings.LightBudget.RealUsed}, dropped {Punchy.Settings.LightBudget.Dropped}");

        /// <summary>"-gearloot": how many of 4000 chests hold gear, and how much (the rolls chests really use).</summary>
        void GearLootTest()
        {
            if (!gearLootArg)
                return;
            gearLootArg = false;
            int chests = 0, pieces = 0;
            for (uint seed = 1; seed <= 4000; seed++)
            {
                var c = new Container();
                RollContents(c, seed * 2654435761u, true);
                var has = false;
                foreach (var s in c.Slots)
                    if (s.item != Nothing && ItemTable.Items[s.item].Slot != GearSlot.None)
                    {
                        pieces++;
                        has = true;
                    }
                if (has)
                    chests++;
            }
            Debug.Log($"[gear] loot: {chests} of 4000 chests hold gear, {pieces} pieces");
        }

        void EquipKeys()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if (keyboard != null && keyboard.xKey.wasPressedThisFrame || pad != null && pad.buttonWest.wasPressedThisFrame)
                Equip(selected);
        }

        /// <summary>A right click on an inventory slot puts its gear on.</summary>
        void RightClickEquip(Rect slot, int index)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 1 && slot.Contains(e.mousePosition))
            {
                Equip(index);
                e.Use();
            }
        }

        static readonly (GearSlot Slot, string Label)[] WornRow = { (GearSlot.Hand, "Hand"), (GearSlot.Head, "Head"), (GearSlot.Back, "Back") };

        /// <summary>The worn row under the slots: a button per place, showing what's on and what it does; press to take it off.</summary>
        void DrawWorn(Rect inventory, float cell, float gap)
        {
            if (openContainer >= 0)
                return;
            var area = new Rect(inventory.x, inventory.yMax + 6f, inventory.width, cell + 28f);
            GUI.Box(area, "Wearing   (X or right click on an item: put on)");
            GUILayout.BeginArea(new Rect(area.x + gap, area.y + 24f, area.width - 2f * gap, cell));
            GUILayout.BeginHorizontal();
            foreach (var (place, label) in WornRow)
            {
                var item = worn[(int)place];
                var text = item == Nothing ? $"{label}\n-" : $"{label}\n{ItemTable.Items[item].Name}\n{GearEffect(item)}";
                if (nav.Button(text, slotStyle, GUILayout.Width(cell * 1.3f), GUILayout.Height(cell - 4f)))
                    Unequip(place);
                GUILayout.Space(gap);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string GearEffect(byte item)
        {
            var d = ItemTable.Items[item];
            return d.Light > 0f ? $"light {d.Light:0} m" : d.Reach > 0f ? $"reach +{d.Reach:0.#} m" : d.Cover > 0f ? $"cover {d.Cover:P0}" : d.Slots > 0 ? $"+{d.Slots} slots" : "";
        }
    }
}
