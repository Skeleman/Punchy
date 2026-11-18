namespace Punchy.Items
{
    /// <summary>
    /// Some of one item (P-35 r3 row 3, DESIGN-items-save section 2): the one shape an inventory slot, a worn place, a
    /// container slot or a lying item will share. Item is the catalog id (ItemIds): 0 is nothing, id n + 1 is ItemTable row
    /// n (the same + 1 FolkKit uses), so ItemTable's rows and constants keep their numbers. Data is a per-kind value (0 for
    /// most; later a fish's species and size, a letter's recipient). Owner is the household it belongs to (Thefts' key,
    /// 0: nobody's). Row 3 uses it for the player's inventory only; the network still sends rows as bytes until row 4.
    /// </summary>
    public struct ItemStack
    {
        public ushort Item;
        public ushort Count;
        public uint Data;
        public uint Owner;

        public bool IsEmpty => Item == 0 || Count == 0;

        /// <summary>Its ItemTable row (255 when empty, as the byte item ids on the network have it).</summary>
        public byte Row => IsEmpty || Item > 256 ? (byte)255 : (byte)(Item - 1);

        /// <summary>The catalog id of an ItemTable row (255, the old "nothing", gives 0).</summary>
        public static ushort IdOf(byte row) => row == 255 ? (ushort)0 : (ushort)(row + 1);

        /// <summary>Count of ItemTable row Row (nothing for row 255 or a count under 1).</summary>
        public static ItemStack Of(byte row, int count, uint data = 0, uint owner = 0) =>
            row == 255 || count < 1 ? default : new ItemStack { Item = IdOf(row), Count = (ushort)count, Data = data, Owner = owner };

        /// <summary>Whether two stacks may be one: same item, same Data and same owner.</summary>
        public bool Merges(in ItemStack other) => Item == other.Item && Data == other.Data && Owner == other.Owner;

        /// <summary>One fewer; empty once none are left.</summary>
        public void TakeOne()
        {
            if (Count <= 1)
                this = default;
            else
                Count--;
        }

        public override string ToString() => IsEmpty ? "empty" : $"{ItemTable.Items[Row].Name} x{Count}";
    }

    /// <summary>
    /// The places a kit has (P-35 r3, DESIGN-items-save section 3), for players and folk alike; row 4 puts them on the
    /// network in place of GearSlot. Bag0 is the first inventory slot, and the rest follow it.
    /// </summary>
    public enum KitPlace : byte { None, RightHand, LeftHand, Sheath, Head, Face, Neck, Body, Back, Legs, Feet, Bag0 }
}
