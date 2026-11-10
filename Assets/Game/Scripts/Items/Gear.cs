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
    public enum GearSlot : byte { None, Hand, Head, Body, Back, Legs, Feet, Neck }

    /// <summary>Client → host: what this player has on now (item ids; 255 for nothing).</summary>
    public struct GearRpc : IRpcCommand
    {
        public byte Hand, Head, Back;
    }

    /// <summary>Client → host: the armour this player has on now (P-28 row 2; item ids, 255 for nothing). Apart from
    /// GearRpc, which keeps its shape: a GearRpc grown to six places failed to read back in a Dev build.</summary>
    public struct ArmourRpc : IRpcCommand
    {
        public byte Body, Legs, Feet;
    }

    /// <summary>Host → everyone: what a player has on.</summary>
    public struct GearStateRpc : IRpcCommand
    {
        public int Player;
        public byte Hand, Head, Back;
    }

    /// <summary>Host → everyone: the armour a player has on.</summary>
    public struct ArmourStateRpc : IRpcCommand
    {
        public int Player;
        public byte Body, Legs, Feet;
    }

    /// <summary>Client → host (P-28 row 3): one place's item, for the places after Feet (the neck so far), so a new place
    /// needs no new message: the older gear messages keep their shape.</summary>
    public struct SlotRpc : IRpcCommand
    {
        public byte Slot, Item;
    }

    /// <summary>Host → everyone: one place's item for a player.</summary>
    public struct SlotStateRpc : IRpcCommand
    {
        public int Player;
        public byte Slot, Item;
    }

    /// <summary>What a player has on, place by place (item ids; 255 for nothing).</summary>
    public struct GearSet
    {
        public byte Hand, Head, Back, Body, Legs, Feet, Neck;
    }

    /// <summary>Client → host → everyone: this player swung its weapon (P-28). From a client, Player is ignored (the host
    /// knows who sent it).</summary>
    public struct SwingRpc : IRpcCommand
    {
        public int Player;
    }

    /// <summary>
    /// Gear (P-23 row 2): items with a Slot go on the body from the inventory (X or right click on the slot, X on a pad;
    /// the worn row under the slots takes them off). What each piece does is in its ItemDef: a torch's Light is the reach of
    /// its light, a pack's Slots are extra inventory rows; a staff's Reach and a hat's Cover are numbers for now, because
    /// the game has no use for them yet. Only an upright body with hands can wear gear. The body shows it through
    /// CreatureState.Held (torch, staff: drawn and lit as the folk's are, so a torch goes through LightBudget) and Worn
    /// (hat, pack). A client tells the host, and the host tells everyone (GearRpc, GearStateRpc; armour in ArmourRpc,
    /// ArmourStateRpc), so others see it.
    /// Weapons (P-28 row 1) go in the hand too: their Damage, Reach and Speed are numbers, because the game has no
    /// fighting yet; the left mouse button (X on a pad, outside the inventory) swings one, a swing lasting 1/Speed s,
    /// drawn through CreatureState.Outfit and sent like the gear (SwingRpc).
    /// Test flags: "-equip Torch,Hat" gives and puts on those items 20 s after our body first stands in the world
    /// ("-equip Club;Spear;Sword" one set after another, 12 s apart); "-swing" swings every 2 s; "-armlog" logs where
    /// the posed hands are each second.
    /// </summary>
    public partial class Items
    {
        const byte Nothing = 255;
        const int BaseSlots = 16, MaxExtraSlots = 8;
        /// <summary>What's on, by GearSlot.</summary>
        readonly byte[] worn = { Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing };
        /// <summary>What each player has on, by network id: the host's word.</summary>
        readonly Dictionary<int, GearSet> gearOf = new Dictionary<int, GearSet>();

        GearSet Wearing => new GearSet
        {
            Hand = worn[(int)GearSlot.Hand], Head = worn[(int)GearSlot.Head], Back = worn[(int)GearSlot.Back],
            Body = worn[(int)GearSlot.Body], Legs = worn[(int)GearSlot.Legs], Feet = worn[(int)GearSlot.Feet],
            Neck = worn[(int)GearSlot.Neck],
        };
        readonly HashSet<Entity> gearSentTo = new HashSet<Entity>();
        string equipArg;
        bool gearLootArg, swingArg, sprintArg;
        /// <summary>When each player last swung (Time.time), by network id; ours apart, as solo has no id.</summary>
        readonly Dictionary<int, float> swungAt = new Dictionary<int, float>();
        float ownSwing = -10f, nextTestSwing;

        int Capacity => BaseSlots + (worn[(int)GearSlot.Back] != Nothing ? math.min(ItemTable.Items[worn[(int)GearSlot.Back]].Slots, MaxExtraSlots) : 0);

        /// <summary>What the body holds for an item in hand, drawn as folk's are (a lantern lit in the off hand, a pick
        /// across the back, a rod in both hands: P-28's tools row).</summary>
        static HeldItem HeldFor(byte item) => item switch
        {
            ItemTable.Torch => HeldItem.Torch,
            ItemTable.Staff => HeldItem.Staff,
            ItemTable.Lantern => HeldItem.Lantern,
            ItemTable.Pickaxe => HeldItem.Pickaxe,
            ItemTable.Rod => HeldItem.Rod,
            _ => HeldItem.None,
        };

        static byte WornBits(byte head, byte back) => (byte)((head == ItemTable.Hat ? CreatureWorn.Hat : 0) | (back == ItemTable.Pack ? CreatureWorn.Pack : 0));

        /// <summary>How the body draws this piece of armour (ItemDef.Look; 0 for none or not armour).</summary>
        static byte Look(byte item) => item == Nothing ? (byte)0 : ItemTable.Items[item].Look;

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
            var g = Wearing;
            Debug.Log($"Items: wearing hand {GearName(g.Hand)}, head {GearName(g.Head)}, back {GearName(g.Back)}, body {GearName(g.Body)}, legs {GearName(g.Legs)}, feet {GearName(g.Feet)}, neck {GearName(g.Neck)}; armour {ArmourTotal():0}, worn {WornWeight():0.#} kg, carried {CarriedWeight():0.#} kg; {Capacity} slots; inventory: {DescribeInventory()}");
            if (Joined)
            {
                SendToHost(new GearRpc { Hand = g.Hand, Head = g.Head, Back = g.Back });
                SendToHost(new ArmourRpc { Body = g.Body, Legs = g.Legs, Feet = g.Feet });
                SendToHost(new SlotRpc { Slot = (byte)GearSlot.Neck, Item = g.Neck });
            }
            else if (Hosting)
                HostSetGear(OwnNetworkId(), g);
        }

        static string GearName(byte item) => item == Nothing ? "-" : ItemTable.Items[item].Name;

        int OwnNetworkId()
        {
            var em = client.EntityManager;
            var q = Q(em, ComponentType.ReadWrite<NetworkId>());
            return q.IsEmpty ? 0 : q.GetSingleton<NetworkId>().Value;
        }

        void HostSetGear(int player, GearSet g)
        {
            gearOf[player] = g;
            Broadcast(new GearStateRpc { Player = player, Hand = g.Hand, Head = g.Head, Back = g.Back });
            Broadcast(new ArmourStateRpc { Player = player, Body = g.Body, Legs = g.Legs, Feet = g.Feet });
            Broadcast(new SlotStateRpc { Player = player, Slot = (byte)GearSlot.Neck, Item = g.Neck });
        }

        /// <summary>The armour numbers of everything on, added up (P-28 row 2: a number only, as nothing fights yet).</summary>
        float ArmourTotal()
        {
            var total = 0f;
            foreach (var item in worn)
                if (item != Nothing)
                    total += ItemTable.Items[item].Armour;
            return total;
        }

        /// <summary>What everything on weighs (kg): counted with the inventory against the carry limit.</summary>
        float WornWeight()
        {
            var total = 0f;
            foreach (var item in worn)
                if (item != Nothing)
                    total += ItemTable.Items[item].Weight;
            return total;
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
                    {
                        var id = em.GetComponentData<NetworkId>(from).Value;
                        var g = gearOf.TryGetValue(id, out var was) ? was : Bare;
                        (g.Hand, g.Head, g.Back) = (m.Hand, m.Head, m.Back);
                        HostSetGear(id, g);
                    }
                    em.DestroyEntity(r);
                }
            using var connections = Q(em, ComponentType.ReadWrite<NetworkId>(), ComponentType.ReadWrite<NetworkStreamInGame>()).ToEntityArray(Allocator.Temp);
            foreach (var c in connections)
            {
                if (!gearSentTo.Add(c))
                    continue;
                foreach (var kv in gearOf)
                {
                    Send(em, c, new GearStateRpc { Player = kv.Key, Hand = kv.Value.Hand, Head = kv.Value.Head, Back = kv.Value.Back });
                    Send(em, c, new ArmourStateRpc { Player = kv.Key, Body = kv.Value.Body, Legs = kv.Value.Legs, Feet = kv.Value.Feet });
                    Send(em, c, new SlotStateRpc { Player = kv.Key, Slot = (byte)GearSlot.Neck, Item = kv.Value.Neck });
                }
            }
        }

        void OnGearState(GearStateRpc m)
        {
            var g = gearOf.TryGetValue(m.Player, out var was) ? was : Bare;
            (g.Hand, g.Head, g.Back) = (m.Hand, m.Head, m.Back);
            gearOf[m.Player] = g;
        }

        void OnArmourState(ArmourStateRpc m)
        {
            var g = gearOf.TryGetValue(m.Player, out var was) ? was : Bare;
            (g.Body, g.Legs, g.Feet) = (m.Body, m.Legs, m.Feet);
            gearOf[m.Player] = g;
        }

        void OnSlotState(SlotStateRpc m)
        {
            var g = gearOf.TryGetValue(m.Player, out var was) ? was : Bare;
            SetSlot(ref g, m.Slot, m.Item);
            gearOf[m.Player] = g;
        }

        /// <summary>One of the places after Feet, in a GearSet.</summary>
        static void SetSlot(ref GearSet g, byte slot, byte item)
        {
            if (slot == (byte)GearSlot.Neck)
                g.Neck = item;
        }

        /// <summary>Nothing on anywhere.</summary>
        static GearSet Bare => new GearSet { Hand = Nothing, Head = Nothing, Back = Nothing, Body = Nothing, Legs = Nothing, Feet = Nothing, Neck = Nothing };

        /// <summary>The host: clients' places after Feet, one at a time.</summary>
        void ServeSlots(EntityManager em)
        {
            using var rs = Q(em, ComponentType.ReadWrite<SlotRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                var m = em.GetComponentData<SlotRpc>(r);
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                {
                    var id = em.GetComponentData<NetworkId>(from).Value;
                    var g = gearOf.TryGetValue(id, out var was) ? was : Bare;
                    SetSlot(ref g, m.Slot, m.Item);
                    HostSetGear(id, g);
                }
                em.DestroyEntity(r);
            }
        }

        /// <summary>The host: clients' armour (as ServeGear does their other gear).</summary>
        void ServeArmour(EntityManager em)
        {
            using var rs = Q(em, ComponentType.ReadWrite<ArmourRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                var m = em.GetComponentData<ArmourRpc>(r);
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                {
                    var id = em.GetComponentData<NetworkId>(from).Value;
                    var g = gearOf.TryGetValue(id, out var was) ? was : Bare;
                    (g.Body, g.Legs, g.Feet) = (m.Body, m.Legs, m.Feet);
                    HostSetGear(id, g);
                }
                em.DestroyEntity(r);
            }
        }

        /// <summary>Each frame: our own body and other players' wear what they have on (bodies are rebuilt without warning).</summary>
        void ApplyGear()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;
            var em = world.EntityManager;
            var own = Q(em, ComponentType.ReadWrite<PlayerAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            if (own.TryGetSingletonEntity<PlayerAvatar>(out var avatar))
                Dress(em, avatar, Wearing, ownSwing);
            if (gearOf.Count == 0)
                return;
            var remote = Q(em, ComponentType.ReadWrite<RemoteAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var bodies = remote.ToEntityArray(Allocator.Temp);
            foreach (var e in bodies)
            {
                var ghost = em.GetComponentData<RemoteAvatar>(e).Ghost;
                if (!em.Exists(ghost) || !em.HasComponent<GhostOwner>(ghost) || !gearOf.TryGetValue(em.GetComponentData<GhostOwner>(ghost).NetworkId, out var g))
                    continue;
                Dress(em, e, g, swungAt.TryGetValue(em.GetComponentData<GhostOwner>(ghost).NetworkId, out var at) ? at : -10f);
            }
        }

        static void Dress(EntityManager em, Entity body, GearSet g, float swung)
        {
            var hand = g.Hand;
            var s = em.GetComponentData<CreatureState>(body);
            ref var blob = ref em.GetComponentData<CreatureBody>(body).Blob.Value;
            var wears = blob.Upright && blob.Hands;
            var held = wears ? HeldFor(hand) : HeldItem.None;
            var bits = wears ? WornBits(g.Head, g.Back) : (byte)0;
            var outfit = wears ? new CreatureOutfit { Head = Look(g.Head), Body = Look(g.Body), Legs = Look(g.Legs), Feet = Look(g.Feet), Back = Look(g.Back), Neck = Look(g.Neck) } : default;
            var weapon = wears && hand != Nothing ? ItemTable.Items[hand].Weapon : CreatureWeapon.None;
            var swing = weapon != CreatureWeapon.None ? SwingByte(swung, ItemTable.Items[hand].Speed) : (byte)0;
            outfit.Weapon = weapon;
            outfit.Swing = swing;
            if (s.Held == held && s.Worn == bits && s.Outfit.Equals(outfit))
                return;
            var was = s.Outfit;
            was.Swing = outfit.Swing;
            var changed = s.Held != held || s.Worn != bits || !was.Equals(outfit);
            s.Held = held;
            s.Worn = bits;
            s.Outfit = outfit;
            em.SetComponentData(body, s);
            if (changed)
                Debug.Log($"[gear] {(em.HasComponent<RemoteAvatar>(body) ? "another player's" : "our")} body: held {held}, worn {bits}, weapon {weapon}, armour head {outfit.Head} body {outfit.Body} legs {outfit.Legs} feet {outfit.Feet} back {outfit.Back} neck {outfit.Neck}");
        }

        /// <summary>Where a swing that began at this time is: 0 at rest, 1..255 through it.</summary>
        static byte SwingByte(float swung, float speed)
        {
            var t = (Time.time - swung) * math.max(speed, 0.1f);
            return t < 0f || t >= 1f ? (byte)0 : (byte)(1 + (int)(t * 254f));
        }

        /// <summary>Swings the weapon in hand, if there is one and it isn't already mid-swing.</summary>
        void Swing()
        {
            var hand = worn[(int)GearSlot.Hand];
            if (hand == Nothing || ItemTable.Items[hand].Weapon == CreatureWeapon.None || !CanWearGear() ||
                SwingByte(ownSwing, ItemTable.Items[hand].Speed) != 0)
                return;
            ownSwing = Time.time;
            var d = ItemTable.Items[hand];
            Debug.Log($"[gear] swing: {d.Name}, damage {d.Damage:0}, reach {d.Reach:0.#} m, {1f / math.max(d.Speed, 0.1f):0.00} s");
            if (Joined)
                SendToHost(new SwingRpc());
            else if (Hosting)
                Broadcast(new SwingRpc { Player = OwnNetworkId() });
        }

        /// <summary>Outside the inventory and menus: the attack button swings (the mouse only while it steers the camera).</summary>
        void SwingKeys()
        {
            if (swingArg && Time.realtimeSinceStartup >= nextTestSwing)
            {
                nextTestSwing = Time.realtimeSinceStartup + 2f;
                Swing();
            }
            // ("-sprint": any test walk (-walk) is a run, to see a cloak swept back.)
            if (sprintArg)
                Punchy.Player.PlayerInputSystem.TestSprint = true;
            if (panel || Punchy.UI.Overlay.AnyOpen)
                return;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked || pad != null && pad.buttonWest.wasPressedThisFrame)
                Swing();
        }

        float armLogAt;

        /// <summary>"-armlog": every second, our body posed as the renderer poses it, and where each arm's hand ends up
        /// (m from the body: up, ahead, to the right), with the weapon and swing it was posed for.</summary>
        void ArmLog()
        {
            if (System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-armlog") < 0 || Time.realtimeSinceStartup < armLogAt)
                return;
            armLogAt = Time.realtimeSinceStartup + 1f;
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var own = Q(em, ComponentType.ReadWrite<PlayerAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            if (!own.TryGetSingletonEntity<PlayerAvatar>(out var avatar) || !TryTerrain(out var terrain))
                return;
            var s = em.GetComponentData<CreatureState>(avatar);
            var body = em.GetComponentData<CreatureBody>(avatar);
            ref var b = ref body.Blob.Value;
            using var output = new NativeArray<SegmentInstance>(b.Segments.Length + 64, Allocator.Temp);
            CreatureAnimator.Evaluate(ref b, s, body.Coat, terrain, output, 0);
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var right = new float3(forward.z, 0f, -forward.x);
            var line = $"[gear] arms: weapon {s.Outfit.Weapon}, swing {s.Outfit.Swing}, mode {s.Mode}";
            for (int p = 0; p < b.Parts.Length; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Arm || part.SegmentCount == 0)
                    continue;
                var seg = output[part.FirstSegment + part.SegmentCount - 1];
                var d = seg.Position + math.mul(seg.Rotation, new float3(0f, 0f, seg.Length)) - s.Position;
                line += $"; arm side {part.Side:+0;-0} ({part.SegmentCount} segs): up {d.y:0.00}, ahead {math.dot(d, forward):0.00}, right {math.dot(d, right):0.00}";
            }
            Debug.Log(line);
        }

        /// <summary>The host: a client's swing, passed on to everyone.</summary>
        void ServeSwings(EntityManager em)
        {
            using var rs = Q(em, ComponentType.ReadWrite<SwingRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                {
                    var id = em.GetComponentData<NetworkId>(from).Value;
                    swungAt[id] = Time.time;
                    Broadcast(new SwingRpc { Player = id });
                    Debug.Log($"[gear] player {id} swung (passed on)");
                }
                em.DestroyEntity(r);
            }
        }

        void OnSwing(SwingRpc m)
        {
            swungAt[m.Player] = Time.time;
            Debug.Log($"[gear] player {m.Player} swung");
        }

        void GearArgs(string[] args)
        {
            var at = System.Array.IndexOf(args, "-equip");
            if (at >= 0 && at + 1 < args.Length)
                equipArg = args[at + 1];
            gearLootArg = System.Array.IndexOf(args, "-gearloot") >= 0;
            swingArg = System.Array.IndexOf(args, "-swing") >= 0;
            sprintArg = System.Array.IndexOf(args, "-sprint") >= 0;
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
            // (Counted from when our body first stands in the world, so a shot list's timings, which start there, line up.)
            if (equipArg == null || equipFrom < 0f && !TryPlayer(out _))
                return;
            if (equipFrom < 0f)
                equipFrom = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup < equipFrom + 20f + 12f * equipSet)
                return;
            // ("A;B;C": one set after another, 12 s apart; each new set takes the last one's hand item off first.)
            var sets = equipArg.Split(';');
            if (equipSet == 0)
            {
                LogBudget("before");
                budgetAt = Time.realtimeSinceStartup + 4f;
            }
            else
                Unequip(GearSlot.Hand);
            foreach (var name in sets[equipSet].Split(','))
            {
                var id = ItemId(name);
                if (id < 0)
                    continue;
                Grant((byte)id, 1);
                Equip(System.Array.FindIndex(slots, s => s.item == id));
            }
            if (++equipSet >= sets.Length)
                equipArg = null;
        }

        int equipSet;
        float equipFrom = -1f;

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

        static readonly (GearSlot Slot, string Label)[] WornRow =
        {
            (GearSlot.Hand, "Hand"), (GearSlot.Head, "Head"), (GearSlot.Body, "Body"), (GearSlot.Legs, "Legs"), (GearSlot.Feet, "Feet"), (GearSlot.Neck, "Neck"), (GearSlot.Back, "Back"),
        };

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
                if (nav.Button(text, slotStyle, GUILayout.Width(cell * 0.8f), GUILayout.Height(cell - 4f)))
                    Unequip(place);
                GUILayout.Space(gap);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string GearEffect(byte item)
        {
            var d = ItemTable.Items[item];
            if (d.Name == "Pickaxe")
                return $"mining, reach {d.Reach:0.#} m";
            if (d.Name == "Rod")
                return $"fishing, line {d.Reach:0} m";
            if (d.Armour > 0f)
                return $"armour {d.Armour:0}, {d.Weight:0.#} kg";
            if (d.Weapon != CreatureWeapon.None)
                return $"dmg {d.Damage:0}, reach {d.Reach:0.#} m{(d.TwoHanded ? ", 2 hands" : "")}";
            return d.Light > 0f ? $"light {d.Light:0} m" : d.Reach > 0f ? $"reach +{d.Reach:0.#} m" : d.Cover > 0f ? $"cover {d.Cover:P0}" : d.Slots > 0 ? $"+{d.Slots} slots" : "";
        }
    }
}
