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
    public enum GearSlot : byte { None, Hand, Head, Body, Back, Legs, Feet, Neck, OffHand }

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

    /// <summary>What a player has on, place by place (item ids; 255 for nothing); and its hair (P-29: CreatureHair style
    /// and colour, sent as two more places in SlotRpc / SlotStateRpc, HairPlace and HairColourPlace).</summary>
    public struct GearSet
    {
        public byte Hand, Head, Back, Body, Legs, Feet, Neck;
        public byte Hair, HairColour;
        /// <summary>Zach 11-12: the other hand (a shield, tongs, a bucket).</summary>
        public byte OffHand;
        /// <summary>Zach 11-12 row 4: 1 while the hand's item is put away (the sheathe key), else 0; sent as SheathPlace.</summary>
        public byte Sheathed;
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
        readonly byte[] worn = { Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing, Nothing };
        /// <summary>What each player has on, by network id: the host's word.</summary>
        readonly Dictionary<int, GearSet> gearOf = new Dictionary<int, GearSet>();

        GearSet Wearing => new GearSet
        {
            Hand = worn[(int)GearSlot.Hand], Head = worn[(int)GearSlot.Head], Back = worn[(int)GearSlot.Back],
            Body = worn[(int)GearSlot.Body], Legs = worn[(int)GearSlot.Legs], Feet = worn[(int)GearSlot.Feet],
            Neck = worn[(int)GearSlot.Neck], OffHand = worn[(int)GearSlot.OffHand],
            Hair = Punchy.Net.PlayerHair.Style, HairColour = Punchy.Net.PlayerHair.Colour,
            Sheathed = sheathed ? (byte)1 : (byte)0,
        };
        /// <summary>The hair's two places in SlotRpc / SlotStateRpc (P-29), past any GearSlot; and whether the hand's item
        /// is put away (Zach 11-12 row 4).</summary>
        const byte HairPlace = 200, HairColourPlace = 201, SheathPlace = 202;
        /// <summary>
        /// Zach 11-12 row 4, the sheathe key (H, LT on a pad: Player/Sheathe): the weapon or tool in hand put away, on the
        /// back or at the hip as the folk wear theirs (FolkKits.OnBack), or drawn again, the hand reaching for it through
        /// the half-second Draw and Sheathe (DrawTime). Swinging with it put away draws it first. When each player last
        /// started one, by network id (ours apart), so every machine plays the reach from when it heard.
        /// </summary>
        bool sheathed;
        float ownSheathAt = -10f, nextTestSheathe;
        readonly Dictionary<int, float> sheathedAt = new Dictionary<int, float>();
        const float DrawTime = 0.5f;
        bool sheatheArg;
        InputAction sheatheAction;
        /// <summary>The hair last told to the host (or set as the host's own), and the client world it went to.</summary>
        (byte, byte) hairSent = (255, 255);
        World hairSentIn;
        readonly HashSet<Entity> gearSentTo = new HashSet<Entity>();
        string equipArg;
        bool gearLootArg, swingArg, sprintArg;
        /// <summary>When each player last swung (Time.time), by network id; ours apart, as solo has no id.</summary>
        readonly Dictionary<int, float> swungAt = new Dictionary<int, float>();
        float ownSwing = -10f, nextTestSwing;

        int Capacity => BaseSlots + (worn[(int)GearSlot.Back] != Nothing ? math.min(ItemTable.Items[worn[(int)GearSlot.Back]].Slots, MaxExtraSlots) : 0);

        static HeldItem HeldFor(byte item) => item == ItemTable.Torch ? HeldItem.Torch : item == ItemTable.Staff ? HeldItem.Staff :
            item == ItemTable.Lantern ? HeldItem.Lantern : HeldItem.None;

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
            // (Something new in the hand is in the hand, not put away.)
            if (place == GearSlot.Hand)
                (sheathed, ownSheathAt) = (false, -10f);
            if (was != Nothing)
                Grant(was, 1);
            // (Both hands: a two-handed thing frees the other hand, and something for the other hand puts down a
            // two-handed one.)
            var other = place == GearSlot.Hand && ItemTable.Items[item].TwoHanded ? GearSlot.OffHand :
                        place == GearSlot.OffHand && worn[(int)GearSlot.Hand] != Nothing && ItemTable.Items[worn[(int)GearSlot.Hand]].TwoHanded ? GearSlot.Hand : GearSlot.None;
            if (other != GearSlot.None && worn[(int)other] != Nothing)
            {
                Grant(worn[(int)other], 1);
                worn[(int)other] = Nothing;
            }
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
            if (place == GearSlot.Hand)
                (sheathed, ownSheathAt) = (false, -10f);
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
            Debug.Log($"Items: wearing hand {GearName(g.Hand)}, head {GearName(g.Head)}, back {GearName(g.Back)}, body {GearName(g.Body)}, legs {GearName(g.Legs)}, feet {GearName(g.Feet)}, neck {GearName(g.Neck)}, off hand {GearName(g.OffHand)}; armour {ArmourTotal():0}, worn {WornWeight():0.#} kg, carried {CarriedWeight():0.#} kg; {Capacity} slots; inventory: {DescribeInventory()}");
            if (Joined)
            {
                SendToHost(new GearRpc { Hand = g.Hand, Head = g.Head, Back = g.Back });
                SendToHost(new ArmourRpc { Body = g.Body, Legs = g.Legs, Feet = g.Feet });
                SendToHost(new SlotRpc { Slot = (byte)GearSlot.Neck, Item = g.Neck });
                SendToHost(new SlotRpc { Slot = (byte)GearSlot.OffHand, Item = g.OffHand });
                SendToHost(new SlotRpc { Slot = SheathPlace, Item = g.Sheathed });
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
            NoteSheath(player, g.Sheathed);
            gearOf[player] = g;
            Broadcast(new GearStateRpc { Player = player, Hand = g.Hand, Head = g.Head, Back = g.Back });
            Broadcast(new ArmourStateRpc { Player = player, Body = g.Body, Legs = g.Legs, Feet = g.Feet });
            Broadcast(new SlotStateRpc { Player = player, Slot = (byte)GearSlot.Neck, Item = g.Neck });
            Broadcast(new SlotStateRpc { Player = player, Slot = (byte)GearSlot.OffHand, Item = g.OffHand });
            Broadcast(new SlotStateRpc { Player = player, Slot = HairPlace, Item = g.Hair });
            Broadcast(new SlotStateRpc { Player = player, Slot = HairColourPlace, Item = g.HairColour });
            Broadcast(new SlotStateRpc { Player = player, Slot = SheathPlace, Item = g.Sheathed });
        }

        /// <summary>A player's item put away or drawn: the reach starts now, here (a first word, as on joining, doesn't
        /// play it).</summary>
        void NoteSheath(int player, byte now)
        {
            if (gearOf.TryGetValue(player, out var was) && was.Sheathed != now)
            {
                sheathedAt[player] = Time.time;
                Debug.Log($"[gear] player {player} {(now != 0 ? "put away" : "drew")} the {GearName(was.Hand)}");
            }
        }

        /// <summary>Our hair to the host when it changes, and once into each world joined (the host keeps its own in
        /// gearOf, as its gear): so the others draw it.</summary>
        void SyncHair()
        {
            var hair = (Punchy.Net.PlayerHair.Style, Punchy.Net.PlayerHair.Colour);
            var into = Hosting ? server : Joined ? client : null;
            if (into == null || hair == hairSent && into == hairSentIn)
                return;
            if (Joined)
            {
                // (Not before the connection has its id: an RPC sent sooner isn't delivered.)
                if (Q(client.EntityManager, ComponentType.ReadWrite<NetworkId>()).IsEmpty)
                    return;
                SendToHost(new SlotRpc { Slot = HairPlace, Item = hair.Style });
                SendToHost(new SlotRpc { Slot = HairColourPlace, Item = hair.Colour });
            }
            else
            {
                var id = OwnNetworkId();
                var g = gearOf.TryGetValue(id, out var was) ? was : Wearing;
                (g.Hair, g.HairColour) = hair;
                HostSetGear(id, g);
            }
            (hairSent, hairSentIn) = (hair, into);
            Debug.Log($"[hair] ours sent: {CreatureHair.Names[hair.Style % CreatureHair.Names.Length]}, {CreatureHair.ColourNames[hair.Colour % CreatureHair.ColourNames.Length]}");
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
                    Send(em, c, new SlotStateRpc { Player = kv.Key, Slot = (byte)GearSlot.OffHand, Item = kv.Value.OffHand });
                    Send(em, c, new SlotStateRpc { Player = kv.Key, Slot = HairPlace, Item = kv.Value.Hair });
                    Send(em, c, new SlotStateRpc { Player = kv.Key, Slot = HairColourPlace, Item = kv.Value.HairColour });
                    Send(em, c, new SlotStateRpc { Player = kv.Key, Slot = SheathPlace, Item = kv.Value.Sheathed });
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
            NoteSheath(m.Player, g.Sheathed);
            gearOf[m.Player] = g;
        }

        /// <summary>One of the places after Feet, in a GearSet.</summary>
        static void SetSlot(ref GearSet g, byte slot, byte item)
        {
            if (slot == (byte)GearSlot.Neck)
                g.Neck = item;
            else if (slot == (byte)GearSlot.OffHand)
                g.OffHand = item;
            else if (slot == HairPlace)
                g.Hair = item;
            else if (slot == HairColourPlace)
                g.HairColour = item;
            else if (slot == SheathPlace)
                g.Sheathed = item;
        }

        /// <summary>Nothing on anywhere.</summary>
        static GearSet Bare => new GearSet { Hand = Nothing, Head = Nothing, Back = Nothing, Body = Nothing, Legs = Nothing, Feet = Nothing, Neck = Nothing, OffHand = Nothing };

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
                Dress(em, avatar, Wearing, ownSwing, ownSheathAt);
            if (gearOf.Count == 0)
                return;
            var remote = Q(em, ComponentType.ReadWrite<RemoteAvatar>(), ComponentType.ReadWrite<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var bodies = remote.ToEntityArray(Allocator.Temp);
            foreach (var e in bodies)
            {
                var ghost = em.GetComponentData<RemoteAvatar>(e).Ghost;
                if (!em.Exists(ghost) || !em.HasComponent<GhostOwner>(ghost) || !gearOf.TryGetValue(em.GetComponentData<GhostOwner>(ghost).NetworkId, out var g))
                    continue;
                var id = em.GetComponentData<GhostOwner>(ghost).NetworkId;
                Dress(em, e, g, swungAt.TryGetValue(id, out var at) ? at : -10f, sheathedAt.TryGetValue(id, out var sAt) ? sAt : -10f);
            }
        }

        /// <summary>Whether this hand item can be put away with the sheathe key: a weapon or tool with a place to wear it
        /// (FolkKits.SheathOf). Not a rod (cast, not carried), nor what the body holds as the folk do (a torch, a staff,
        /// a lantern: CreatureState.Held).</summary>
        static bool CanSheathe(byte item) => item != Nothing && ItemTable.Items[item].Weapon != CreatureWeapon.None && item != ItemTable.Rod &&
                                             HeldFor(item) == HeldItem.None && FolkKits.SheathOf(item) != ItemSheath.None;

        /// <summary>How far into its sheath the hand's item is (CreatureOutfit.Stow): 255 put away, 0 in hand, between
        /// through the DrawTime since it was started.</summary>
        static byte StowByte(bool sheathed, float since)
        {
            var t = math.saturate((Time.time - since) / DrawTime);
            return (byte)math.round(255f * (sheathed ? t : 1f - t));
        }

        static void Dress(EntityManager em, Entity body, GearSet g, float swung, float sheathAt)
        {
            var hand = g.Hand;
            var s = em.GetComponentData<CreatureState>(body);
            ref var blob = ref em.GetComponentData<CreatureBody>(body).Blob.Value;
            var wears = blob.Upright && blob.Hands;
            var held = wears ? HeldFor(hand) : HeldItem.None;
            var bits = wears ? WornBits(g.Head, g.Back) : (byte)0;
            var outfit = wears ? new CreatureOutfit { Head = Look(g.Head), Body = Look(g.Body), Legs = Look(g.Legs), Feet = Look(g.Feet), Back = Look(g.Back), Neck = Look(g.Neck) } : default;
            var weapon = wears && hand != Nothing ? ItemTable.Items[hand].Weapon : CreatureWeapon.None;
            // (Put away, or past the middle of the reach: worn, not in hand; Zach 11-12 row 4.)
            var stow = wears && CanSheathe(hand) ? StowByte(g.Sheathed != 0, sheathAt) : (byte)0;
            if (stow != 0)
                (outfit.Kept, outfit.Stow) = ((byte)(hand + 1), stow);
            if (stow >= 128)
                weapon = CreatureWeapon.None;
            var swing = weapon != CreatureWeapon.None ? SwingByte(swung, ItemTable.Items[hand].Speed) : (byte)0;
            outfit.Weapon = weapon;
            outfit.Swing = swing;
            outfit.OffHand = wears && g.OffHand != Nothing ? ItemTable.Items[g.OffHand].Weapon : (byte)0;
            if (wears)
                (outfit.Hair, outfit.HairColour, outfit.OwnHair) = (g.Hair, g.HairColour, true);
            if (s.Held == held && s.Worn == bits && s.Outfit.Equals(outfit))
                return;
            var was = s.Outfit;
            // (Not logged for a swing or a reach going on: NoteSheath and Sheathe log those.)
            (was.Swing, was.Kept, was.Stow) = (outfit.Swing, outfit.Kept, outfit.Stow);
            var changed = s.Held != held || s.Worn != bits || !was.Equals(outfit);
            s.Held = held;
            s.Worn = bits;
            s.Outfit = outfit;
            em.SetComponentData(body, s);
            if (changed)
                Debug.Log($"[gear] {(em.HasComponent<RemoteAvatar>(body) ? "another player's" : "our")} body: held {held}, worn {bits}, weapon {weapon}, armour head {outfit.Head} body {outfit.Body} legs {outfit.Legs} feet {outfit.Feet} back {outfit.Back} neck {outfit.Neck} off hand {outfit.OffHand}, hair {outfit.Hair} colour {outfit.HairColour}");
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
            // (Put away: drawn first, and no swing until it's in hand.)
            if (sheathed)
            {
                Debug.Log("[gear] swing while put away: drawn first");
                Sheathe();
                return;
            }
            if (Time.time - ownSheathAt < DrawTime)
                return;
            // (A rod is cast, not swung: FishKeys has its button. Nor is what has no swing to it: a letter, a bowl.)
            if (hand == Nothing || ItemTable.Items[hand].Weapon == CreatureWeapon.None || ItemTable.Items[hand].Weapon == CreatureWeapon.Rod || ItemTable.Items[hand].Speed <= 0f || !CanWearGear() ||
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

        /// <summary>Puts the hand's item away, or draws it (the sheathe key; Zach 11-12 row 4). Not mid-reach, nor mid-swing.</summary>
        void Sheathe()
        {
            var hand = worn[(int)GearSlot.Hand];
            if (!CanSheathe(hand) || !CanWearGear() || Time.time - ownSheathAt < DrawTime || SwingByte(ownSwing, ItemTable.Items[hand].Speed) != 0)
                return;
            sheathed = !sheathed;
            ownSheathAt = Time.time;
            Debug.Log($"[gear] {(sheathed ? "sheathe" : "draw")}: {ItemTable.Items[hand].Name}, {(FolkKits.SheathOf(hand) == ItemSheath.Hip || !FolkKits.OnBack(hand, PlayerStandHeight()) ? "at the hip" : "on the back")}");
            if (Joined)
                SendToHost(new SlotRpc { Slot = SheathPlace, Item = sheathed ? (byte)1 : (byte)0 });
            else if (Hosting)
            {
                var id = OwnNetworkId();
                var g = gearOf.TryGetValue(id, out var was) ? was : Wearing;
                g.Sheathed = sheathed ? (byte)1 : (byte)0;
                HostSetGear(id, g);
            }
        }

        /// <summary>Our body's standing height (m), for where its sheath is (FolkKits.OnBack); 1 if it isn't up yet.</summary>
        float PlayerStandHeight()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return 1f;
            var own = Q(world.EntityManager, ComponentType.ReadOnly<PlayerAvatar>(), ComponentType.ReadOnly<CreatureBody>());
            return own.TryGetSingletonEntity<PlayerAvatar>(out var avatar) ? world.EntityManager.GetComponentData<CreatureBody>(avatar).Blob.Value.StandHeight : 1f;
        }

        /// <summary>Outside the inventory and menus: the attack button swings (the mouse only while it steers the camera),
        /// the sheathe key puts away or draws ("-sheathe": every 3 s, for shots).</summary>
        void SwingKeys()
        {
            if (swingArg && Time.realtimeSinceStartup >= nextTestSwing)
            {
                nextTestSwing = Time.realtimeSinceStartup + 2f;
                Swing();
            }
            if (sheatheArg && worn[(int)GearSlot.Hand] != Nothing && Time.realtimeSinceStartup >= nextTestSheathe)
            {
                nextTestSheathe = Time.realtimeSinceStartup + 3f;
                Sheathe();
            }
            sheatheAction ??= InputSystem.actions?.FindAction("Player/Sheathe");
            // ("-sprint": any test walk (-walk) is a run, to see a cloak swept back.)
            if (sprintArg)
                Punchy.Player.PlayerInputSystem.TestSprint = true;
            if (panel || Punchy.UI.Overlay.AnyOpen)
                return;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked || pad != null && pad.buttonWest.wasPressedThisFrame)
                Swing();
            if (sheatheAction != null && sheatheAction.WasPressedThisFrame())
                Sheathe();
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
            sheatheArg = System.Array.IndexOf(args, "-sheathe") >= 0;
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
            (GearSlot.Hand, "Hand"), (GearSlot.OffHand, "Off hand"), (GearSlot.Head, "Head"), (GearSlot.Body, "Body"), (GearSlot.Legs, "Legs"), (GearSlot.Feet, "Feet"), (GearSlot.Neck, "Neck"), (GearSlot.Back, "Back"),
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
            if (d.Armour > 0f)
                return $"armour {d.Armour:0}, {d.Weight:0.#} kg";
            if (d.Weapon == CreatureWeapon.Rod)
                return $"casts up to {d.Reach:0} m";
            if (d.Weapon != CreatureWeapon.None)
                return $"dmg {d.Damage:0}, reach {d.Reach:0.#} m{(d.TwoHanded ? ", 2 hands" : "")}";
            return d.Light > 0f ? $"light {d.Light:0} m" : d.Reach > 0f ? $"reach +{d.Reach:0.#} m" : d.Cover > 0f ? $"cover {d.Cover:P0}" : d.Slots > 0 ? $"+{d.Slots} slots" : "";
        }
    }
}
