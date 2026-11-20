using Punchy.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// P-35 r3 row 4 (#26): what a player has on, on its own ghost (baked by NetPlayerAuthoring), so every machine and
    /// every joiner reads it from the snapshot instead of the host sending each place to each connection. Worn places are
    /// catalog ids (ItemStack.Item: 0 nothing, row + 1); the four look bytes are the old pseudo-places (hair style and
    /// colour, sheathed, which hands block). The host writes it (Items.WriteKits); the owner never does.
    /// </summary>
    public struct NetPlayerKit : IComponentData
    {
        [GhostField] public ushort RightHand, LeftHand, Head, Neck, Body, Back, Legs, Feet;
        [GhostField] public byte Hair, HairColour, Sheathed, Block;
    }
}

namespace Punchy.Items
{
    /// <summary>
    /// Client → host (P-35 r3 row 4): Count of Item moved from one KitPlace to another. Row 4 sends it for the worn
    /// places only (Bag0 standing for "the bag", wherever in it): the host checks the place it comes from and writes the
    /// place it goes to. It replaces GearRpc, ArmourRpc and the item half of SlotRpc. A new struct, not a grown one
    /// (P-28: grown RPCs failed to read back).
    /// </summary>
    public struct KitMoveRpc : IRpcCommand
    {
        public byte From, To;
        public ushort Count, Item;
    }

    /// <summary>
    /// Host → owner (P-35 r3 row 4): a stack for one of its KitPlaces. Bag0 means "into the bag" (a pickup, a take from a
    /// container, a put that didn't fit). It replaces GrantRpc and carries the whole ItemStack (flat fields, as RPC
    /// codegen wants), so Data and Owner reach the client once the bag keeps them.
    /// </summary>
    public struct KitSlotRpc : IRpcCommand
    {
        public byte Place;
        public ushort Item, Count;
        public uint Data, Owner;
    }

    public partial class Items
    {
        /// <summary>The worn places last told to the host, and the client world they went to (a new world: all again).</summary>
        GearSet kitSent;
        World kitSentIn;

        static readonly GearSlot[] WornSlots =
            { GearSlot.Hand, GearSlot.LeftHand, GearSlot.Head, GearSlot.Neck, GearSlot.Body, GearSlot.Back, GearSlot.Legs, GearSlot.Feet };

        static KitPlace PlaceOf(GearSlot s) => s switch
        {
            GearSlot.Hand => KitPlace.RightHand, GearSlot.LeftHand => KitPlace.LeftHand, GearSlot.Head => KitPlace.Head,
            GearSlot.Neck => KitPlace.Neck, GearSlot.Body => KitPlace.Body, GearSlot.Back => KitPlace.Back,
            GearSlot.Legs => KitPlace.Legs, GearSlot.Feet => KitPlace.Feet, _ => KitPlace.None,
        };

        static GearSlot SlotOfPlace(byte place) => (KitPlace)place switch
        {
            KitPlace.RightHand => GearSlot.Hand, KitPlace.LeftHand => GearSlot.LeftHand, KitPlace.Head => GearSlot.Head,
            KitPlace.Neck => GearSlot.Neck, KitPlace.Body => GearSlot.Body, KitPlace.Back => GearSlot.Back,
            KitPlace.Legs => GearSlot.Legs, KitPlace.Feet => GearSlot.Feet, _ => GearSlot.None,
        };

        static byte WornIn(in GearSet g, GearSlot s) => s switch
        {
            GearSlot.Hand => g.Hand, GearSlot.LeftHand => g.LeftHand, GearSlot.Head => g.Head, GearSlot.Neck => g.Neck,
            GearSlot.Body => g.Body, GearSlot.Back => g.Back, GearSlot.Legs => g.Legs, GearSlot.Feet => g.Feet, _ => Nothing,
        };

        static void SetWornIn(ref GearSet g, GearSlot s, byte row)
        {
            switch (s)
            {
                case GearSlot.Hand: g.Hand = row; break;
                case GearSlot.LeftHand: g.LeftHand = row; break;
                case GearSlot.Head: g.Head = row; break;
                case GearSlot.Neck: g.Neck = row; break;
                case GearSlot.Body: g.Body = row; break;
                case GearSlot.Back: g.Back = row; break;
                case GearSlot.Legs: g.Legs = row; break;
                case GearSlot.Feet: g.Feet = row; break;
            }
        }

        /// <summary>A catalog id as the byte row the gear code keeps (Nothing for 0 or past the byte rows).</summary>
        static byte RowOfId(ushort id) => id == 0 || id > 255 ? Nothing : (byte)(id - 1);

        static NetPlayerKit KitOf(in GearSet g) => new NetPlayerKit
        {
            RightHand = ItemStack.IdOf(g.Hand), LeftHand = ItemStack.IdOf(g.LeftHand), Head = ItemStack.IdOf(g.Head),
            Neck = ItemStack.IdOf(g.Neck), Body = ItemStack.IdOf(g.Body), Back = ItemStack.IdOf(g.Back),
            Legs = ItemStack.IdOf(g.Legs), Feet = ItemStack.IdOf(g.Feet),
            Hair = g.Hair, HairColour = g.HairColour, Sheathed = g.Sheathed, Block = g.Block,
        };

        static GearSet GearOfKit(in NetPlayerKit k) => new GearSet
        {
            Hand = RowOfId(k.RightHand), LeftHand = RowOfId(k.LeftHand), Head = RowOfId(k.Head), Neck = RowOfId(k.Neck),
            Body = RowOfId(k.Body), Back = RowOfId(k.Back), Legs = RowOfId(k.Legs), Feet = RowOfId(k.Feet),
            Hair = k.Hair, HairColour = k.HairColour, Sheathed = k.Sheathed, Block = k.Block,
        };

        static bool SameKit(in NetPlayerKit a, in NetPlayerKit b) =>
            a.RightHand == b.RightHand && a.LeftHand == b.LeftHand && a.Head == b.Head && a.Neck == b.Neck && a.Body == b.Body &&
            a.Back == b.Back && a.Legs == b.Legs && a.Feet == b.Feet && a.Hair == b.Hair && a.HairColour == b.HairColour &&
            a.Sheathed == b.Sheathed && a.Block == b.Block;

        /// <summary>
        /// A joined client: each worn place that differs from what the host last heard, as a take-off (place → bag) and a
        /// put-on (bag → place). Called on GearChanged and every frame from Receive, so a change made before the
        /// connection had its id (an RPC sent sooner isn't delivered) still goes once it has one.
        /// </summary>
        void SyncKit()
        {
            if (!Joined)
                return;
            if (client != kitSentIn)
            {
                if (Q(client.EntityManager, ComponentType.ReadWrite<NetworkId>()).IsEmpty)
                    return;
                (kitSent, kitSentIn) = (Bare, client);
            }
            var g = Wearing;
            foreach (var s in WornSlots)
            {
                var was = WornIn(kitSent, s);
                var now = WornIn(g, s);
                if (was == now)
                    continue;
                if (was != Nothing)
                    SendToHost(new KitMoveRpc { From = (byte)PlaceOf(s), To = (byte)KitPlace.Bag0, Count = 1, Item = ItemStack.IdOf(was) });
                if (now != Nothing)
                    SendToHost(new KitMoveRpc { From = (byte)KitPlace.Bag0, To = (byte)PlaceOf(s), Count = 1, Item = ItemStack.IdOf(now) });
                SetWornIn(ref kitSent, s, now);
            }
        }

        /// <summary>The host: clients' kit moves (onto a worn place, or off one), then every player's kit onto its ghost.</summary>
        void ServeKit(EntityManager em)
        {
            using (var rs = Q(em, ComponentType.ReadWrite<KitMoveRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<KitMoveRpc>(r);
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    if (em.Exists(from) && em.HasComponent<NetworkId>(from))
                    {
                        var id = em.GetComponentData<NetworkId>(from).Value;
                        var g = gearOf.TryGetValue(id, out var was) ? was : Bare;
                        var row = RowOfId(m.Item);
                        var to = SlotOfPlace(m.To);
                        var off = SlotOfPlace(m.From);
                        if (to != GearSlot.None)
                            SetWornIn(ref g, to, row);
                        else if (off != GearSlot.None && WornIn(g, off) == row)
                            SetWornIn(ref g, off, Nothing);
                        else
                            Debug.Log($"[kit] player {id}: move of item {m.Item} from {(KitPlace)m.From} to {(KitPlace)m.To} not applied");
                        HostSetGear(id, g);
                    }
                    em.DestroyEntity(r);
                }
            WriteKits(em);
        }

        /// <summary>The host: each player's gear (gearOf) onto its NetPlayer ghost's NetPlayerKit, where it differs. A ghost
        /// spawned after the gear was set gets it here on its first frame.</summary>
        void WriteKits(EntityManager em)
        {
            if (gearOf.Count == 0)
                return;
            var q = Q(em, ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadWrite<NetPlayerKit>());
            using var ghosts = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ghosts)
            {
                if (!gearOf.TryGetValue(em.GetComponentData<GhostOwner>(e).NetworkId, out var g))
                    continue;
                var k = KitOf(g);
                if (!SameKit(em.GetComponentData<NetPlayerKit>(e), k))
                    em.SetComponentData(e, k);
            }
        }

        /// <summary>A joined client: every player's gear from its ghost's NetPlayerKit (the host's word), into gearOf.</summary>
        void ReadKits(EntityManager em)
        {
            var q = Q(em, ComponentType.ReadOnly<NetPlayer>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<NetPlayerKit>());
            using var ghosts = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ghosts)
            {
                var id = em.GetComponentData<GhostOwner>(e).NetworkId;
                var g = GearOfKit(em.GetComponentData<NetPlayerKit>(e));
                NoteSheath(id, g.Sheathed);
                gearOf[id] = g;
            }
        }

        /// <summary>Host → owner: Count of row into the bag (what GrantRpc was).</summary>
        static KitSlotRpc KitGrant(byte row, int count) =>
            new KitSlotRpc { Place = (byte)KitPlace.Bag0, Item = ItemStack.IdOf(row), Count = (ushort)Mathf.Clamp(count, 0, ushort.MaxValue) };

        /// <summary>A joined client: the host's stack for one of our places. Row 4: into the bag only.</summary>
        void OnKitSlot(KitSlotRpc m)
        {
            var row = RowOfId(m.Item);
            if (row == Nothing || m.Count == 0)
                return;
            if (m.Place != (byte)KitPlace.Bag0)
                Debug.Log($"[kit] host sent {ItemTable.Items[row].Name} x{m.Count} for {(KitPlace)m.Place}: put in the bag (row 4 grants into the bag only)");
            Grant(row, m.Count);
        }
    }
}
