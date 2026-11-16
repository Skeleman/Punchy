using Punchy.Terrain;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// One kind of item as data (P-35 row 2, #26; DESIGN-items-save section 2), in Assets/Game/Data/Items/. Its fields are
    /// today's <see cref="ItemDef"/>, one for one, so the catalog gives back exactly the table it was generated from
    /// (ItemCatalogGenerator). Its id isn't kept here but in <see cref="ItemIds"/>, which never reorders.
    /// </summary>
    public class ItemAsset : ScriptableObject
    {
        /// <summary>The stable key (also in ItemIds): never changed or reused once it ships. Saves will use it.</summary>
        public string Key;
        public string DisplayName;
        public Color Colour, Accent;
        public int Stack = 1;
        public float Weight;
        public GearSlot Slot;
        public float Light, Reach, Cover;
        public int Slots;
        public float Damage, Speed;
        public bool TwoHanded;
        public byte Weapon;
        public float Armour;
        public byte Look;
        public float Length;
        public ItemSheath Sheath;
        public bool Dyed;
        public MaterialFamily Family, AccentFamily;
        public byte Kind, AccentKind;
        /// <summary>A quest-only item's quest id, and the ordinary item whose mesh it borrows (P-49 r3). Empty here is
        /// null in the def (Unity saves a null string as empty).</summary>
        public string Quest, Base;

        /// <summary>The item as the game reads it today.</summary>
        public ItemDef ToDef() => new ItemDef
        {
            Name = DisplayName, Colour = Colour, Accent = Accent, Stack = Stack, Weight = Weight, Slot = Slot, Light = Light,
            Reach = Reach, Cover = Cover, Slots = Slots, Damage = Damage, Speed = Speed, TwoHanded = TwoHanded, Weapon = Weapon,
            Armour = Armour, Look = Look, Length = Length, Sheath = Sheath, Dyed = Dyed, Family = Family,
            AccentFamily = AccentFamily, Kind = Kind, AccentKind = AccentKind,
            Quest = string.IsNullOrEmpty(Quest) ? null : Quest, Base = string.IsNullOrEmpty(Base) ? null : Base,
        };

        /// <summary>Takes every field from a table row (the generator; the key is set once, by the registry).</summary>
        public void FromDef(in ItemDef d)
        {
            DisplayName = d.Name; Colour = d.Colour; Accent = d.Accent; Stack = d.Stack; Weight = d.Weight; Slot = d.Slot;
            Light = d.Light; Reach = d.Reach; Cover = d.Cover; Slots = d.Slots; Damage = d.Damage; Speed = d.Speed;
            TwoHanded = d.TwoHanded; Weapon = d.Weapon; Armour = d.Armour; Look = d.Look; Length = d.Length; Sheath = d.Sheath;
            Dyed = d.Dyed; Family = d.Family; AccentFamily = d.AccentFamily; Kind = d.Kind; AccentKind = d.AccentKind;
            Quest = d.Quest; Base = d.Base;
        }
    }
}
