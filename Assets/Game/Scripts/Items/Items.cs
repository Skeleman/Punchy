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
using UnityEngine.InputSystem.LowLevel;

namespace Punchy.Items
{
    /// <summary>One kind of item (#26): its name, the colour it's drawn in and a second colour for a part of it (ItemMeshes),
    /// how many stack, its weight (kg).</summary>
    public struct ItemDef
    {
        public string Name;
        public Color Colour, Accent;
        public int Stack;
        public float Weight;
        /// <summary>P-23: where it's worn (None: not gear), and what it gives: the reach of its light (m), of the wearer (m),
        /// its cover from the weather (0..1), extra inventory slots.</summary>
        public GearSlot Slot;
        public float Light, Reach, Cover;
        public int Slots;
        /// <summary>P-28: a weapon's numbers (the game has no fighting yet, so only the swing uses Speed: swings a
        /// second), whether it takes both hands, and how the body draws it (CreatureWeapon).</summary>
        public float Damage, Speed;
        public bool TwoHanded;
        public byte Weapon;
        /// <summary>P-28 row 2: armour's protection (a number only, for now), and how the body draws it (CreatureArmour).</summary>
        public float Armour;
        public byte Look;
        /// <summary>Zach 11-12, one item system: its length (m, at a body's scale 1), and where it goes when put away (a
        /// weapon or tool sheathed: on the back if long for its bearer, else at the hip, as a guard's). Dyed: drawn in
        /// its wearer's colour (a sash in its town's), not its own.</summary>
        public float Length;
        public ItemSheath Sheath;
        public bool Dyed;
        /// <summary>W-166 r4 (#336): what each part is made of (part 0, drawn in Colour; part 1, in Accent), so an item on
        /// the ground takes the material library's look for its family, the same look as a building's beams or a
        /// colossus's stone (None: plain colour). The kind is the family's own row (MaterialLibrary.cs); for wood, plus
        /// 16 x the grain's axis when it isn't up (48: along z, as a haft lies).</summary>
        public MaterialFamily Family, AccentFamily;
        public byte Kind, AccentKind;
    }

    /// <summary>Where an item goes when sheathed (Zach 11-12): nowhere (it can't be: back into the pack), on the back or
    /// at the hip by its length for its bearer (ItemSheaths.OnBack), always on the back, always at the hip.</summary>
    public enum ItemSheath : byte { None, Auto, Back, Hip }

    /// <summary>Where a sheathed item is worn on a body: the guards' rule (GuardWeapons.OnBack), for every item.</summary>
    public static class ItemSheaths
    {
        /// <summary>On the back if it's long for its bearer (over 0.6 of its height, about twice its hip height), else
        /// at the hip: the same on every machine, from the item and the bearer's size alone.</summary>
        public static bool OnBack(in ItemDef item, float standHeight) =>
            item.Sheath == ItemSheath.Back || item.Sheath == ItemSheath.Auto && item.Length > 0.6f * 2f * standHeight;
    }

    /// <summary>The item table (#26, P-11). An item's id is its index here; never reorder, only append.</summary>
    public static class ItemTable
    {
        public static readonly ItemDef[] Items =
        {
            new ItemDef { Name = "Stick", Colour = new Color(0.55f, 0.38f, 0.2f), Stack = 20, Weight = 0.3f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Stick, Length = 0.6f, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Stone", Colour = new Color(0.55f, 0.55f, 0.58f), Stack = 20, Weight = 1f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Stone, Family = MaterialFamily.Stone },
            new ItemDef { Name = "Berry", Colour = new Color(0.75f, 0.1f, 0.3f), Accent = new Color(0.25f, 0.5f, 0.2f), Stack = 30, Weight = 0.02f },
            new ItemDef { Name = "Mushroom", Colour = new Color(0.72f, 0.32f, 0.2f), Accent = new Color(0.9f, 0.85f, 0.72f), Stack = 20, Weight = 0.05f },
            new ItemDef { Name = "Shell", Colour = new Color(0.95f, 0.85f, 0.8f), Stack = 20, Weight = 0.05f },
            new ItemDef { Name = "Fish", Colour = new Color(0.4f, 0.6f, 0.75f), Accent = new Color(0.3f, 0.42f, 0.55f), Stack = 5, Weight = 0.8f },
            new ItemDef { Name = "Coin", Colour = new Color(1f, 0.8f, 0.2f), Stack = 99, Weight = 0.01f, Family = MaterialFamily.Metal },
            new ItemDef { Name = "Key", Colour = new Color(0.8f, 0.7f, 0.3f), Stack = 5, Weight = 0.05f, Family = MaterialFamily.Metal },
            new ItemDef { Name = "Torch", Colour = new Color(0.42f, 0.28f, 0.15f), Accent = new Color(1f, 0.55f, 0.15f), Stack = 1, Weight = 0.6f, Slot = GearSlot.Hand, Light = 11f, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Staff", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.72f, 0.66f, 0.5f), Stack = 1, Weight = 1.2f, Slot = GearSlot.Hand, Reach = 0.5f, Length = 1.6f, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Hat", Colour = new Color(0.62f, 0.5f, 0.3f), Accent = new Color(0.36f, 0.24f, 0.16f), Stack = 1, Weight = 0.2f, Slot = GearSlot.Head, Cover = 0.3f, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Pack", Colour = new Color(0.42f, 0.33f, 0.2f), Accent = new Color(0.25f, 0.18f, 0.12f), Stack = 1, Weight = 1.5f, Slot = GearSlot.Back, Slots = 8, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Club", Colour = new Color(0.45f, 0.3f, 0.17f), Accent = new Color(0.3f, 0.2f, 0.12f), Stack = 1, Weight = 1.4f, Slot = GearSlot.Hand, Damage = 6f, Reach = 0.9f, Speed = 1.1f, Weapon = Punchy.Fauna.CreatureWeapon.Club, Length = 0.8f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Spear", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.8f, Slot = GearSlot.Hand, Damage = 8f, Reach = 1.9f, Speed = 0.8f, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.Spear, Length = 2.1f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Sword", Colour = new Color(0.7f, 0.72f, 0.75f), Accent = new Color(0.4f, 0.26f, 0.14f), Stack = 1, Weight = 1.5f, Slot = GearSlot.Hand, Damage = 9f, Reach = 1.1f, Speed = 1.4f, Weapon = Punchy.Fauna.CreatureWeapon.Sword, Length = 0.95f, Sheath = ItemSheath.Hip, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Bow", Colour = new Color(0.55f, 0.38f, 0.2f), Accent = new Color(0.88f, 0.85f, 0.75f), Stack = 1, Weight = 0.9f, Slot = GearSlot.Hand, Damage = 7f, Reach = 25f, Speed = 0.6f, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.Bow, Length = 1.1f, Sheath = ItemSheath.Back, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Arrow", Colour = new Color(0.6f, 0.45f, 0.28f), Accent = new Color(0.9f, 0.9f, 0.88f), Stack = 30, Weight = 0.03f, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Cap", Colour = new Color(0.45f, 0.3f, 0.18f), Accent = new Color(0.32f, 0.2f, 0.12f), Stack = 1, Weight = 0.5f, Slot = GearSlot.Head, Armour = 1f, Cover = 0.1f, Look = Punchy.Fauna.CreatureArmour.Cap, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Helm", Colour = new Color(0.55f, 0.57f, 0.6f), Accent = new Color(0.4f, 0.41f, 0.44f), Stack = 1, Weight = 2.5f, Slot = GearSlot.Head, Armour = 3f, Cover = 0.2f, Look = Punchy.Fauna.CreatureArmour.Helm, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Jerkin", Colour = new Color(0.47f, 0.31f, 0.18f), Accent = new Color(0.3f, 0.19f, 0.1f), Stack = 1, Weight = 3f, Slot = GearSlot.Body, Armour = 2f, Look = Punchy.Fauna.CreatureArmour.Jerkin, Family = MaterialFamily.Leather, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Chainmail", Colour = new Color(0.52f, 0.54f, 0.57f), Accent = new Color(0.36f, 0.37f, 0.4f), Stack = 1, Weight = 9f, Slot = GearSlot.Body, Armour = 5f, Look = Punchy.Fauna.CreatureArmour.Chain, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Greaves", Colour = new Color(0.5f, 0.52f, 0.55f), Accent = new Color(0.36f, 0.24f, 0.14f), Stack = 1, Weight = 2.5f, Slot = GearSlot.Legs, Armour = 2f, Look = Punchy.Fauna.CreatureArmour.Greaves, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Boots", Colour = new Color(0.33f, 0.21f, 0.12f), Accent = new Color(0.22f, 0.14f, 0.08f), Stack = 1, Weight = 1.2f, Slot = GearSlot.Feet, Armour = 1f, Look = Punchy.Fauna.CreatureArmour.Boots, Family = MaterialFamily.Leather },
            new ItemDef { Name = "Tunic", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 0.6f, Slot = GearSlot.Body, Look = Punchy.Fauna.CreatureArmour.Tunic, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Robe", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 1.2f, Slot = GearSlot.Body, Cover = 0.1f, Look = Punchy.Fauna.CreatureArmour.Robe, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Cloak", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 1f, Slot = GearSlot.Back, Cover = 0.25f, Look = Punchy.Fauna.CreatureArmour.Cloak, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Scarf", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 0.15f, Slot = GearSlot.Neck, Cover = 0.05f, Look = Punchy.Fauna.CreatureArmour.Scarf, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Hood", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 0.25f, Slot = GearSlot.Head, Cover = 0.25f, Look = Punchy.Fauna.CreatureArmour.Hood, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Woolcap", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 0.1f, Slot = GearSlot.Head, Cover = 0.15f, Look = Punchy.Fauna.CreatureArmour.Woolcap, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Brimhat", Colour = new Color(0.6f, 0.27f, 0.22f), Accent = new Color(0.78f, 0.69f, 0.46f), Stack = 1, Weight = 0.3f, Slot = GearSlot.Head, Cover = 0.35f, Look = Punchy.Fauna.CreatureArmour.Brimhat, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            // Item rows (Zach 11-10: never idle, more weapons and wear in small rows).
            new ItemDef { Name = "Axe", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.6f, Slot = GearSlot.Hand, Damage = 8f, Reach = 1f, Speed = 1f, Weapon = Punchy.Fauna.CreatureWeapon.Axe, Length = 0.75f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Hammer", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.5f, 0.51f, 0.54f), Stack = 1, Weight = 2.2f, Slot = GearSlot.Hand, Damage = 10f, Reach = 0.9f, Speed = 0.8f, Weapon = Punchy.Fauna.CreatureWeapon.Hammer, Length = 0.7f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            // P-33: a fishing rod, held like a weapon but cast (Reach: the longest cast, m).
            new ItemDef { Name = "Rod", Colour = new Color(0.55f, 0.4f, 0.22f), Accent = new Color(0.6f, 0.44f, 0.28f), Stack = 1, Weight = 0.8f, Slot = GearSlot.Hand, Reach = 22f, Speed = 1.6f, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.Rod, Length = 1.8f, Sheath = ItemSheath.Back, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Pickaxe", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 2.2f, Slot = GearSlot.Hand, Damage = 7f, Reach = 1.1f, Speed = 0.8f, Weapon = Punchy.Fauna.CreatureWeapon.Pickaxe, Length = 0.75f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            // (Held, it is the folk's lit lantern, HeldItem.Lantern: its light is theirs, 9 m.)
            new ItemDef { Name = "Lantern", Colour = new Color(0.22f, 0.2f, 0.18f), Accent = new Color(1f, 0.78f, 0.4f), Stack = 1, Weight = 1f, Slot = GearSlot.Hand, Light = 9f, Family = MaterialFamily.Metal },
            new ItemDef { Name = "Shovel", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.8f, Slot = GearSlot.Hand, Damage = 5f, Reach = 1.3f, Speed = 0.9f, Weapon = Punchy.Fauna.CreatureWeapon.Shovel, Length = 1.05f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            // Zach 11-12, one item system: everything folk carry, wear and work with, as items (ids from 36).
            // What guards carry: an ogre's great club, a goblin's scrap shield.
            new ItemDef { Name = "Great club", Colour = new Color(0.36f, 0.24f, 0.14f), Accent = new Color(0.25f, 0.17f, 0.1f), Stack = 1, Weight = 4f, Slot = GearSlot.Hand, Damage = 11f, Reach = 1.3f, Speed = 0.6f, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.GreatClub, Length = 1.15f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48 },
            new ItemDef { Name = "Shield", Colour = new Color(0.36f, 0.3f, 0.24f), Accent = new Color(0.25f, 0.22f, 0.2f), Stack = 1, Weight = 3f, Slot = GearSlot.Hand, Armour = 3f, Weapon = Punchy.Fauna.CreatureWeapon.Shield, Length = 0.34f, Sheath = ItemSheath.Back, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            // What travellers and miners carry: a letter, ore and a sack of it.
            new ItemDef { Name = "Letter", Colour = new Color(0.9f, 0.86f, 0.74f), Accent = new Color(0.6f, 0.12f, 0.1f), Stack = 5, Weight = 0.02f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Letter },
            new ItemDef { Name = "Ore", Colour = new Color(0.42f, 0.36f, 0.32f), Accent = new Color(0.55f, 0.3f, 0.15f), Stack = 20, Weight = 2f, Family = MaterialFamily.Stone, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Ore sack", Colour = new Color(0.55f, 0.45f, 0.3f), Accent = new Color(0.42f, 0.36f, 0.32f), Stack = 1, Weight = 12f, Slot = GearSlot.Hand, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.OreSack, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Stone },
            // Workers' tools.
            new ItemDef { Name = "Hoe", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.5f, Slot = GearSlot.Hand, Damage = 4f, Reach = 1.2f, Speed = 0.9f, Weapon = Punchy.Fauna.CreatureWeapon.Hoe, Length = 1.1f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Broom", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.76f, 0.62f, 0.33f), Stack = 1, Weight = 1f, Slot = GearSlot.Hand, Damage = 1f, Reach = 1.3f, Speed = 1f, Weapon = Punchy.Fauna.CreatureWeapon.Broom, Length = 1.25f, Sheath = ItemSheath.Auto, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Thatch },
            new ItemDef { Name = "Smith's hammer", Colour = new Color(0.5f, 0.36f, 0.2f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.2f, Slot = GearSlot.Hand, Damage = 5f, Reach = 0.7f, Speed = 1.2f, Weapon = Punchy.Fauna.CreatureWeapon.SmithHammer, Length = 0.4f, Sheath = ItemSheath.Hip, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Tongs", Colour = new Color(0.55f, 0.56f, 0.58f), Accent = new Color(0.4f, 0.41f, 0.44f), Stack = 1, Weight = 0.8f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Tongs, Length = 0.35f, Sheath = ItemSheath.Hip, Family = MaterialFamily.Metal },
            new ItemDef { Name = "Bucket", Colour = new Color(0.42f, 0.3f, 0.18f), Accent = new Color(0.55f, 0.56f, 0.58f), Stack = 1, Weight = 1.5f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Bucket, Family = MaterialFamily.Wood, AccentFamily = MaterialFamily.Metal },
            // What healers, traders and bakers have in hand, and food.
            new ItemDef { Name = "Bowl", Colour = new Color(0.5f, 0.36f, 0.22f), Accent = new Color(0.32f, 0.42f, 0.2f), Stack = 5, Weight = 0.4f, Slot = GearSlot.Hand, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.Bowl, Family = MaterialFamily.Wood },
            new ItemDef { Name = "Jar", Colour = new Color(0.55f, 0.32f, 0.2f), Accent = new Color(0.7f, 0.6f, 0.45f), Stack = 5, Weight = 0.6f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Jar },
            new ItemDef { Name = "Apple", Colour = new Color(0.78f, 0.18f, 0.12f), Accent = new Color(0.3f, 0.5f, 0.2f), Stack = 20, Weight = 0.15f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Fruit },
            new ItemDef { Name = "Bread", Colour = new Color(0.62f, 0.38f, 0.16f), Accent = new Color(0.8f, 0.6f, 0.35f), Stack = 10, Weight = 0.4f, Slot = GearSlot.Hand, Weapon = Punchy.Fauna.CreatureWeapon.Loaf },
            new ItemDef { Name = "Dough", Colour = new Color(0.9f, 0.82f, 0.62f), Stack = 5, Weight = 0.5f, Slot = GearSlot.Hand, TwoHanded = true, Weapon = Punchy.Fauna.CreatureWeapon.Dough },
            // Props at a worker's post (placed there; heavy).
            new ItemDef { Name = "Kneading table", Colour = new Color(0.6f, 0.48f, 0.32f), Accent = new Color(0.9f, 0.88f, 0.82f), Stack = 1, Weight = 25f, Family = MaterialFamily.Wood },
            new ItemDef { Name = "Chopping block", Colour = new Color(0.32f, 0.22f, 0.14f), Accent = new Color(0.7f, 0.56f, 0.36f), Stack = 1, Weight = 30f, Family = MaterialFamily.Wood, AccentFamily = MaterialFamily.Wood },
            new ItemDef { Name = "Log", Colour = new Color(0.32f, 0.22f, 0.14f), Accent = new Color(0.7f, 0.56f, 0.36f), Stack = 10, Weight = 4f, Family = MaterialFamily.Wood, Kind = 48, AccentFamily = MaterialFamily.Wood, AccentKind = 48 },
            new ItemDef { Name = "Rope", Colour = new Color(0.6f, 0.52f, 0.36f), Stack = 5, Weight = 0.5f, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Hot iron", Colour = new Color(1f, 0.42f, 0.08f), Accent = new Color(0.3f, 0.31f, 0.33f), Stack = 1, Weight = 1f, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Creel", Colour = new Color(0.55f, 0.42f, 0.25f), Accent = new Color(0.2f, 0.15f, 0.1f), Stack = 1, Weight = 1f, Family = MaterialFamily.Thatch, AccentFamily = MaterialFamily.Thatch },
            new ItemDef { Name = "Honeycomb", Colour = new Color(0.85f, 0.6f, 0.15f), Accent = new Color(0.95f, 0.8f, 0.3f), Stack = 5, Weight = 0.3f },
            // What guards and workers wear.
            new ItemDef { Name = "Goggles", Colour = new Color(0.1f, 0.1f, 0.12f), Accent = new Color(0.36f, 0.22f, 0.12f), Stack = 1, Weight = 0.2f, Slot = GearSlot.Head, Look = Punchy.Fauna.CreatureArmour.Goggles, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Kettle hat", Colour = new Color(0.55f, 0.56f, 0.58f), Accent = new Color(0.4f, 0.41f, 0.44f), Stack = 1, Weight = 2.2f, Slot = GearSlot.Head, Armour = 3f, Cover = 0.3f, Look = Punchy.Fauna.CreatureArmour.KettleHat, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Horned helm", Colour = new Color(0.55f, 0.56f, 0.58f), Accent = new Color(0.78f, 0.74f, 0.62f), Stack = 1, Weight = 2.6f, Slot = GearSlot.Head, Armour = 3f, Cover = 0.2f, Look = Punchy.Fauna.CreatureArmour.HornedHelm, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Bone },
            new ItemDef { Name = "Crested helm", Colour = new Color(0.55f, 0.56f, 0.58f), Accent = new Color(0.04f, 0.2f, 0.16f), Stack = 1, Weight = 2f, Slot = GearSlot.Head, Armour = 3f, Cover = 0.2f, Look = Punchy.Fauna.CreatureArmour.CrestedHelm, Dyed = true, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Pot helm", Colour = new Color(0.3f, 0.29f, 0.27f), Accent = new Color(0.22f, 0.21f, 0.2f), Stack = 1, Weight = 2f, Slot = GearSlot.Head, Armour = 2f, Cover = 0.25f, Look = Punchy.Fauna.CreatureArmour.PotHelm, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Skull helm", Colour = new Color(0.78f, 0.74f, 0.62f), Accent = new Color(0.6f, 0.56f, 0.46f), Stack = 1, Weight = 3f, Slot = GearSlot.Head, Armour = 2f, Cover = 0.2f, Look = Punchy.Fauna.CreatureArmour.SkullHelm, Family = MaterialFamily.Bone, AccentFamily = MaterialFamily.Bone },
            new ItemDef { Name = "Breastplate", Colour = new Color(0.55f, 0.56f, 0.58f), Accent = new Color(0.4f, 0.41f, 0.44f), Stack = 1, Weight = 7f, Slot = GearSlot.Body, Armour = 5f, Look = Punchy.Fauna.CreatureArmour.Breastplate, Family = MaterialFamily.Metal, AccentFamily = MaterialFamily.Metal },
            new ItemDef { Name = "Leather cuirass", Colour = new Color(0.36f, 0.22f, 0.12f), Accent = new Color(0.25f, 0.15f, 0.08f), Stack = 1, Weight = 4f, Slot = GearSlot.Body, Armour = 3f, Look = Punchy.Fauna.CreatureArmour.Cuirass, Family = MaterialFamily.Leather, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Sash", Colour = new Color(0.32f, 0.06f, 0.04f), Accent = new Color(0.55f, 0.42f, 0.18f), Stack = 1, Weight = 0.2f, Slot = GearSlot.Neck, Look = Punchy.Fauna.CreatureArmour.Sash, Dyed = true, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Hide cape", Colour = new Color(0.36f, 0.27f, 0.18f), Accent = new Color(0.25f, 0.19f, 0.12f), Stack = 1, Weight = 3f, Slot = GearSlot.Back, Cover = 0.3f, Look = Punchy.Fauna.CreatureArmour.HideCape, Family = MaterialFamily.Cloth, Kind = 1, AccentFamily = MaterialFamily.Leather },
            new ItemDef { Name = "Apron", Colour = new Color(0.62f, 0.58f, 0.5f), Accent = new Color(0.45f, 0.42f, 0.36f), Stack = 1, Weight = 0.4f, Slot = GearSlot.Body, Look = Punchy.Fauna.CreatureArmour.Apron, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Leather apron", Colour = new Color(0.36f, 0.22f, 0.12f), Accent = new Color(0.25f, 0.15f, 0.08f), Stack = 1, Weight = 1.2f, Slot = GearSlot.Body, Armour = 1f, Look = Punchy.Fauna.CreatureArmour.LeatherApron, Family = MaterialFamily.Leather, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Baker's apron", Colour = new Color(0.86f, 0.84f, 0.78f), Accent = new Color(0.7f, 0.68f, 0.62f), Stack = 1, Weight = 0.4f, Slot = GearSlot.Body, Look = Punchy.Fauna.CreatureArmour.BakerApron, Family = MaterialFamily.Cloth, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Baker's cap", Colour = new Color(0.86f, 0.84f, 0.78f), Stack = 1, Weight = 0.1f, Slot = GearSlot.Head, Look = Punchy.Fauna.CreatureArmour.BakerCap, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Straw hat", Colour = new Color(0.82f, 0.7f, 0.42f), Accent = new Color(0.5f, 0.38f, 0.2f), Stack = 1, Weight = 0.25f, Slot = GearSlot.Head, Cover = 0.35f, Look = Punchy.Fauna.CreatureArmour.StrawHat, Family = MaterialFamily.Thatch, AccentFamily = MaterialFamily.Cloth },
            new ItemDef { Name = "Fisher's hat", Colour = new Color(0.34f, 0.38f, 0.3f), Stack = 1, Weight = 0.2f, Slot = GearSlot.Head, Cover = 0.35f, Look = Punchy.Fauna.CreatureArmour.FisherHat, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Satchel", Colour = new Color(0.4f, 0.25f, 0.13f), Accent = new Color(0.3f, 0.18f, 0.09f), Stack = 1, Weight = 0.6f, Slot = GearSlot.Back, Slots = 4, Look = Punchy.Fauna.CreatureArmour.Satchel, Family = MaterialFamily.Leather, AccentFamily = MaterialFamily.Leather },
            // A-106 + A-103 job gear at a worker's post, as items (P-38, rule 33): a roofer's guy rope and coil, a mender's
            // trestle and the dented helm on it, a fire tender's spirit flame, what a sweeper or shoveller has cleared, an
            // ice cutter's snow block and the ice block on it.
            new ItemDef { Name = "Guy rope", Colour = new Color(0.6f, 0.52f, 0.36f), Stack = 1, Weight = 1.5f, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Rope coil", Colour = new Color(0.6f, 0.52f, 0.36f), Stack = 1, Weight = 1f, Family = MaterialFamily.Cloth },
            new ItemDef { Name = "Trestle", Colour = new Color(0.5f, 0.38f, 0.24f), Accent = new Color(0.36f, 0.24f, 0.13f), Stack = 1, Weight = 12f, Family = MaterialFamily.Wood, AccentFamily = MaterialFamily.Wood },
            new ItemDef { Name = "Dented helm", Colour = new Color(0.3f, 0.31f, 0.33f), Stack = 1, Weight = 2f, Family = MaterialFamily.Metal },
            new ItemDef { Name = "Spirit flame", Colour = new Color(0.55f, 0.95f, 0.86f), Accent = new Color(0.45f, 0.85f, 0.78f), Stack = 1, Weight = 0f },
            new ItemDef { Name = "Heap", Colour = new Color(0.6f, 0.58f, 0.55f), Stack = 1, Weight = 20f },
            new ItemDef { Name = "Snow block", Colour = new Color(0.86f, 0.9f, 0.94f), Stack = 5, Weight = 15f },
            new ItemDef { Name = "Ice block", Colour = new Color(0.62f, 0.8f, 0.9f), Stack = 5, Weight = 15f, Family = MaterialFamily.Ice },
            // P-38 row 3: eyewear as head items (A-103 row 5), put on with G or from the inventory.
            new ItemDef { Name = "Glasses", Colour = new Color(0.04f, 0.04f, 0.05f), Stack = 1, Weight = 0.05f, Slot = GearSlot.Head, Look = Punchy.Fauna.CreatureArmour.Glasses },
            new ItemDef { Name = "Glow goggles", Colour = new Color(0.32f, 0.85f, 0.45f), Accent = new Color(0.2f, 0.17f, 0.14f), Stack = 1, Weight = 0.2f, Slot = GearSlot.Head, Look = Punchy.Fauna.CreatureArmour.GlowGoggles, AccentFamily = MaterialFamily.Leather },
        };
        public const byte Fish = 5, Key = 7, Torch = 8, Staff = 9, Hat = 10, Pack = 11;
        /// <summary>Item row: the lantern (held: HeldItem.Lantern).</summary>
        public const byte Lantern = 34;
        /// <summary>Zach 11-12, one item system: the ids folk kits are filled with (FolkKits). Checked against the names
        /// at start (Check).</summary>
        public const byte Stick = 0, Stone = 1, Club = 12, Spear = 13, Sword = 14, Bow = 15, Axe = 30, Hammer = 31, Rod = 32, Pickaxe = 33, Shovel = 35,
                          GreatClub = 36, Shield = 37, Letter = 38, Ore = 39, OreSack = 40, Hoe = 41, Broom = 42, SmithHammer = 43, Tongs = 44,
                          Bucket = 45, Bowl = 46, Jar = 47, Apple = 48, Bread = 49, Dough = 50, KneadingTable = 51, ChoppingBlock = 52, Log = 53,
                          Rope = 54, HotIron = 55, Creel = 56, Honeycomb = 57, Goggles = 58, KettleHat = 59, HornedHelm = 60, CrestedHelm = 61,
                          PotHelm = 62, SkullHelm = 63, Breastplate = 64, Cuirass = 65, Sash = 66, HideCape = 67, Apron = 68, LeatherApron = 69,
                          BakerApron = 70, BakerCap = 71, StrawHat = 72, FisherHat = 73, Satchel = 74,
                          GuyRope = 75, RopeCoil = 76, Trestle = 77, DentedHelm = 78, SpiritFlame = 79, Heap = 80, SnowBlock = 81, IceBlock = 82,
                          Glasses = 83, GlowGoggles = 84;
        /// <summary>A snow shoveller's knit cap (A-106), from the outfit rows.</summary>
        public const byte Woolcap = 28;

        /// <summary>Whether each id above is still the item it names (an id is its index: one appended in the wrong place
        /// would shift them). Logged as an error if not.</summary>
        public static void Check()
        {
            (byte id, string name)[] named =
            {
                (Stick, "Stick"), (Stone, "Stone"), (Fish, "Fish"), (Torch, "Torch"), (Staff, "Staff"), (Pack, "Pack"), (Club, "Club"), (Spear, "Spear"),
                (Sword, "Sword"), (Bow, "Bow"), (Axe, "Axe"), (Rod, "Rod"), (Pickaxe, "Pickaxe"), (Lantern, "Lantern"), (Shovel, "Shovel"),
                (GreatClub, "Great club"), (Shield, "Shield"), (Letter, "Letter"), (OreSack, "Ore sack"), (Hoe, "Hoe"), (Broom, "Broom"),
                (SmithHammer, "Smith's hammer"), (Tongs, "Tongs"), (Bucket, "Bucket"), (Bowl, "Bowl"), (Jar, "Jar"), (Apple, "Apple"), (Dough, "Dough"),
                (Creel, "Creel"), (Goggles, "Goggles"), (KettleHat, "Kettle hat"), (HornedHelm, "Horned helm"), (CrestedHelm, "Crested helm"),
                (PotHelm, "Pot helm"), (SkullHelm, "Skull helm"), (Breastplate, "Breastplate"), (Cuirass, "Leather cuirass"), (Sash, "Sash"),
                (HideCape, "Hide cape"), (Apron, "Apron"), (LeatherApron, "Leather apron"), (BakerApron, "Baker's apron"), (BakerCap, "Baker's cap"),
                (StrawHat, "Straw hat"), (FisherHat, "Fisher's hat"), (Satchel, "Satchel"),
                (GuyRope, "Guy rope"), (RopeCoil, "Rope coil"), (Trestle, "Trestle"), (DentedHelm, "Dented helm"), (SpiritFlame, "Spirit flame"),
                (Heap, "Heap"), (SnowBlock, "Snow block"), (IceBlock, "Ice block"), (Woolcap, "Woolcap"),
                (Glasses, "Glasses"), (GlowGoggles, "Glow goggles"),
            };
            foreach (var (id, name) in named)
                if (id >= Items.Length || Items[id].Name != name)
                    Debug.LogError($"Items: item {id} should be the {name}, is the {(id < Items.Length ? Items[id].Name : "end of the table")}");
        }
    }

    /// <summary>Client → host: pick up world item Uid.</summary>
    public struct PickupRpc : IRpcCommand { public int Uid; }
    /// <summary>
    /// Client → host: open the chest at socket Socket of dungeon Dungeon (DungeonInfo.Id) of type Type (DungeonInfo.Type;
    /// a chest in a cave's ruin room names its cave). P-47: 8 bytes; it was the temple's socket index alone.
    /// </summary>
    public struct OpenChestRpc : IRpcCommand { public uint Dungeon; public ushort Type, Socket; }
    /// <summary>Client → host: drop Count of Item at Pos.</summary>
    public struct DropRpc : IRpcCommand { public byte Item, Count; public float3 Pos; }
    /// <summary>Host → everyone: world item Uid is gone (picked up).</summary>
    public struct ItemGoneRpc : IRpcCommand { public int Uid; }
    /// <summary>Host → everyone: a world item appeared (a drop, a chest's contents).</summary>
    public struct ItemSpawnRpc : IRpcCommand { public int Uid; public byte Item, Count; public float3 Pos; }
    /// <summary>Host → the picker: it's yours, into the inventory.</summary>
    public struct GrantRpc : IRpcCommand { public byte Item, Count; }
    /// <summary>Host → everyone: a chest has been opened (named as in <see cref="OpenChestRpc"/>).</summary>
    public struct ChestOpenedRpc : IRpcCommand { public uint Dungeon; public ushort Type, Socket; }

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
    /// <item>Dungeon chests: every dungeon's Chest sockets (the sunken temple's, every cave's and the caves' ruin rooms',
    /// P-47) open with Use; the host lays out 1-3 items seeded from the socket's seed, plus the dungeon loot table's
    /// chest pick (<see cref="Punchy.Fauna.DungeonSpawner.PickChestLoot"/>), so the contents are the same on every
    /// machine. A cave's marked chest (DungeonState, the quests' chest) holds at least 2.</item>
    /// </list>
    /// For the animal lane's dungeon spawns: <see cref="Spawn"/> (host or solo) puts an item in the world for everyone.
    /// Test flags: "-pickup N" picks up the N nearest items (any distance) through the same path; "-drop" drops one from
    /// the first slot afterwards; "-openchest" opens the nearest temple chest ("-openchest cave,ruin": the first cave
    /// chest and the first ruin room chest by id, the same pick on every machine); "-takechest cave,ruin" takes what is
    /// in those chests once they are open; "-badchestrpc" (a joined client) sends the host a chest that isn't one;
    /// "-inventory" shows the panel.
    /// </summary>
    public partial class Items : MonoBehaviour
    {
        public const float Reach = 2f, DrawRange = 60f;
        /// <summary>Uids: seeded door items are door*4+k; drops DropBase upwards; spares at the folk's posts SpareBase
        /// upwards (Spares.cs). (Chests keep their contents as containers: <see cref="ChestId"/>.)</summary>
        const int DropBase = 2_000_000;

        struct WorldItem { public byte Item, Count; public float3 Pos; }

        static Items instance;
        /// <summary>The distance to the item the player faces (float.MaxValue: none), for the Use key's arbitration with doors.</summary>
        public static float TargetDistance = float.MaxValue;

        readonly Dictionary<int, WorldItem> world = new Dictionary<int, WorldItem>();
        /// <summary>Seeded items taken, and items added since (drops, chests): what a late joiner is sent.</summary>
        readonly HashSet<int> gone = new HashSet<int>();
        readonly Dictionary<int, WorldItem> added = new Dictionary<int, WorldItem>();
        readonly HashSet<int> openedChests = new HashSet<int>();
        readonly (byte item, int count)[] slots = new (byte, int)[BaseSlots + MaxExtraSlots];
        BlobAssetReference<AnchorSetBlob> seededFor;
        readonly HashSet<Entity> sentTo = new HashSet<Entity>();
        World server, client;
        int nextDrop = DropBase;
        int target = -1, targetChest = -1;
        string prompt;
        /// <summary>The in-world menu is up (any tab; P-39, #397: the inventory is its Items tab).</summary>
        static bool panel => Punchy.UI.FieldMenu.Open;
        /// <summary>The menu shows the Items tab: the slots have the focus and the Use key drops, puts or takes.</summary>
        static bool itemsTab => Punchy.UI.FieldMenu.Showing == ItemsTab;
        const string ItemsTab = "Items", QuestsTab = "Quests";
        int selected, lastFocus = -1;
        static Punchy.UI.MenuNav nav => Punchy.UI.FieldMenu.Nav;
        bool padTest;
        float padAt;
        int padStep;
        float3 stickFrom;
        Gamepad testPad;
        int pickupArg, pickupDone;
        bool dropArg, openChestArg, badChestArg;
        /// <summary>"-openchest cave,ruin" / "-takechest cave,ruin": which chests (empty: the nearest temple chest).</summary>
        string[] openChestKinds = System.Array.Empty<string>(), takeChestKinds = System.Array.Empty<string>();
        float testAt;
        readonly Dictionary<int, GameObject> shown = new Dictionary<int, GameObject>();
        GUIStyle promptStyle, slotStyle;
        static Material itemMaterial;
        static Material libraryMaterial;
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
            ItemTable.Check();
            FolkKits.CheckLooks();
            for (int i = 0; i < instance.slots.Length; i++)
                instance.slots[i].item = 255;
            instance.StartPack();
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            var at = System.Array.IndexOf(args, "-pickup");
            if (at >= 0 && at + 1 < args.Length)
                int.TryParse(args[at + 1], out instance.pickupArg);
            // ("-pickupspares N": the same, the N nearest spares at the folk's posts only; Spares.cs.)
            at = System.Array.IndexOf(args, "-pickupspares");
            if (at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out instance.pickupArg))
                instance.spareOnlyArg = true;
            instance.dropArg = System.Array.IndexOf(args, "-drop") >= 0;
            instance.openChestArg = System.Array.IndexOf(args, "-openchest") >= 0;
            instance.openChestKinds = Kinds(args, "-openchest");
            instance.takeChestKinds = Kinds(args, "-takechest");
            instance.badChestArg = System.Array.IndexOf(args, "-badchestrpc") >= 0;
            // (P-39: the inventory and the quest log are tabs of the in-world menu; "-inventory" opens it there.)
            Punchy.UI.FieldMenu.AddTab(ItemsTab, 0, instance.DrawItemsTab);
            Punchy.UI.FieldMenu.AddTab(QuestsTab, 10, instance.DrawQuestsTab);
            instance.padTest = System.Array.IndexOf(args, "-invpad") >= 0;
            instance.itemRowArg = System.Array.IndexOf(args, "-itemrow") >= 0;
            var rowAt = System.Array.IndexOf(args, "-itemrow");
            if (rowAt >= 0 && rowAt + 1 < args.Length && int.TryParse(args[rowAt + 1], out var rowFrom))
                instance.itemRowFrom = math.max(rowFrom, 0);
            var ca = System.Array.IndexOf(args, "-container");
            if (ca >= 0 && ca + 1 < args.Length)
                int.TryParse(args[ca + 1], out instance.containerArg);
            instance.putArg = System.Array.IndexOf(args, "-putitem") >= 0;
            instance.takeArg = System.Array.IndexOf(args, "-takeitem") >= 0;
            instance.QuestArgs(args);
            instance.GearArgs(args);
            instance.FishArgs(args);
            instance.CheckArgs(args);
        }

        // P-20 (profiler finding 11): Items made 30-35 allocations a frame. Entity queries are now built once per world and
        // kept (CreateEntityQuery allocates every call); the Receive handlers are cached delegates; the chest sockets, the
        // item meshes' sets and the Use prompt are reused from frame to frame.
        readonly Dictionary<(World, ComponentType, ComponentType, ComponentType), EntityQuery> queries =
            new Dictionary<(World, ComponentType, ComponentType, ComponentType), EntityQuery>();

        EntityQuery Q(EntityManager em, ComponentType a) => Q(em, a, default, default);
        EntityQuery Q(EntityManager em, ComponentType a, ComponentType b) => Q(em, a, b, default);

        /// <summary>A query on em's world for these components, made the first time and kept (callers don't dispose it).</summary>
        EntityQuery Q(EntityManager em, ComponentType a, ComponentType b, ComponentType c)
        {
            var key = (em.World, a, b, c);
            if (!queries.TryGetValue(key, out var q))
            {
                q = c.TypeIndex != default ? em.CreateEntityQuery(a, b, c) : b.TypeIndex != default ? em.CreateEntityQuery(a, b) : em.CreateEntityQuery(a);
                queries[key] = q;
            }
            return q;
        }

        bool Hosting => server != null && server.IsCreated && server.IsServer();
        bool Joined => !Hosting && client != null && client.IsCreated && client.IsClient();

        void Update()
        {
            if (NetWorlds.Server != server || NetWorlds.Client != client)
            {
                (server, client) = (NetWorlds.Server, NetWorlds.Client);
                sentTo.Clear();
                // (A world that went away takes its queries with it.)
                var stale = new List<(World, ComponentType, ComponentType, ComponentType)>();
                foreach (var key in queries.Keys)
                    if (key.Item1 == null || !key.Item1.IsCreated)
                        stale.Add(key);
                foreach (var key in stale)
                    queries.Remove(key);
                if (Joined)
                {
                    // A joining client takes the host's world: back to the seeded items, then what the host sends.
                    gone.Clear();
                    added.Clear();
                    openedChests.Clear();
                    seededFor = default;
                    rodsFor = default;
                }
            }
            if (!TryTerrain(out var terrain))
                return;
            RegisterChests();
            StockPosts();
            LayRods(terrain);
            if (Hosting)
                Serve();
            if (client != null && client.IsCreated && client.IsClient())
                Receive();

            FindTarget();
            // (FindTarget keeps a target only when it won the Use key: UseTarget.)
            if ((target >= 0 || targetChest >= 0 || targetContainer >= 0) && UseKey.WasPressed())
            {
                if (target >= 0) Pickup(target);
                else if (targetChest >= 0) OpenChest(targetChest);
                else OpenContainer(targetContainer);
            }
            // (Walked away from an open container: it closes.)
            if (openContainer >= 0 && TryPlayer(out var at) && math.distance(at.Position.xz, containers[openContainer].Pos.xz) > 3f)
                openContainer = -1;
            ContainerTests();
            UpdateQuests(terrain);
            Tests(terrain);
            GearTest();
            EyewearKey();
            GearLootTest();
            SwingKeys();
            FishKeys(terrain);
            SyncHair();
            ApplyGear();
            DrawFishing(terrain);
            ArmLog();
            PadTest();
            UpdateInventoryKeys();
            Draw();
            DrawChests();
        }

        bool TryTerrain(out TerrainSource terrain)
        {
            terrain = default;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return false;
            var q = Q(w.EntityManager, ComponentType.ReadOnly<TerrainSource>());
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
            var q = Q(w.EntityManager, ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<LocalTransform>());
            return q.TryGetSingleton(out player);
        }

        string promptCache;
        (string, int, int, int, byte, byte) promptCacheFor;

        void FindTarget()
        {
            target = targetChest = -1;
            prompt = null;
            TargetDistance = float.MaxValue;
            if (!TryPlayer(out var player))
                return;
            // (Items, chests and containers are all scored by the one facing rule, UseTarget; the best of them bids.)
            var forward = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz);
            var camera = Camera.main;
            var look = camera != null ? math.normalizesafe(((float3)camera.transform.forward).xz) : forward;
            var best = float.MaxValue;
            foreach (var kv in world)
            {
                var to = kv.Value.Pos - player.Position;
                var d = math.length(to.xz);
                if (d > Reach || math.abs(to.y) > 1.5f)
                    continue;
                var score = UseTarget.Score(player.Position, forward, look, kv.Value.Pos);
                if (score < best)
                    (best, TargetDistance, target) = (score, d, kv.Key);
            }
            foreach (var (socket, pos) in ChestSockets())
            {
                if (openedChests.Contains(socket))
                    continue;
                var d = math.distance(pos.xz, player.Position.xz);
                if (d > Reach || math.abs(pos.y - player.Position.y) >= 1.5f)
                    continue;
                var score = UseTarget.Score(player.Position, forward, look, pos);
                if (score < best)
                    (best, TargetDistance, target, targetChest) = (score, d, -1, socket);
            }
            var before = best;
            FindContainer(player.Position, forward, look, ref best, ref TargetDistance);
            if (best < before && targetContainer >= 0)
                (target, targetChest) = (-1, -1);
            else
                targetContainer = -1;
            if (!UseTarget.Bid(UseKind.Item, best))
            {
                (target, targetChest, targetContainer) = (-1, -1, -1);
                return;
            }
            if (target >= 0 || targetChest >= 0 || targetContainer >= 0)
                UseKey.Offer();
            var key = UseKey.Name;
            var item = target >= 0 ? world[target] : default;
            var promptKey = (key, targetContainer, target, targetChest, item.Item, item.Count);
            if (promptCache != null && promptCacheFor.Equals(promptKey))
            {
                prompt = promptCache;
                return;
            }
            if (targetContainer >= 0)
                prompt = $"{key}  look in the {containers[targetContainer].Name}";
            else if (target >= 0)
                prompt = $"{key}  pick up {ItemTable.Items[item.Item].Name.ToLowerInvariant()}{(item.Count > 1 ? $" x{item.Count}" : "")}";
            else if (targetChest >= 0)
                prompt = $"{key}  open the chest";
            (promptCache, promptCacheFor) = (prompt, promptKey);
        }

        /// <summary>
        /// P-47: one chest of any dungeon. Temple chests keep their socket index as their id (and container id);
        /// cave and ruin room chests take <see cref="ChestId"/>'s hashed id.
        /// </summary>
        struct ChestRef
        {
            public uint Dungeon;
            public ushort Type, Socket;
            /// <summary>DungeonState's marked chest of a cave (the quests' chest).</summary>
            public bool Marked;
            public DungeonSocket S;
        }

        /// <summary>Cave and ruin chest ids: this bit, an 18-bit hash of the dungeon, then the socket's 12 bits.</summary>
        public const int CaveChestBit = 0x40000000, MaxChestSocket = 0xFFF;

        /// <summary>
        /// A chest's id, also its container id: the socket index for the temple's (as before P-47), else
        /// CaveChestBit | (Scramble(dungeon ^ type &lt;&lt; 16) &amp; 0x3FFFF) &lt;&lt; 12 | socket. The same on every machine,
        /// so an RPC's (dungeon, type, socket) names one; WorldTests checks no two of the island's chests share one.
        /// </summary>
        public static int ChestId(uint dungeon, ushort type, int socket) =>
            type == DungeonTypes.SunkenTemple ? socket
            : CaveChestBit | (int)(Punchy.Fauna.DungeonSpawner.Scramble(dungeon ^ ((uint)type << 16)) & 0x3FFFFu) << 12 | (socket & MaxChestSocket);

        /// <summary>Whether the chest at <paramref name="socket"/> of a dungeon (DungeonInfo Id and Type) is open, as this machine knows it.</summary>
        public static bool ChestOpened(uint dungeon, ushort type, int socket) =>
            instance != null && instance.openedChests.Contains(ChestId(dungeon, type, socket));

        readonly Dictionary<int, ChestRef> chestRefs = new Dictionary<int, ChestRef>();
        readonly List<(int, float3)> chestSockets = new List<(int, float3)>();
        int chestSocketsFrame = -1;
        (World world, int count, uint sum) chestsFor;

        /// <summary>
        /// Every dungeon's chest sockets (the temple's, the caves' and their ruin rooms'): (chest id, position). Rebuilt
        /// when the dungeon entities change (caves come in over a few frames without the island file), else reused.
        /// </summary>
        List<(int, float3)> ChestSockets()
        {
            if (chestSocketsFrame == Time.frameCount)
                return chestSockets;
            chestSocketsFrame = Time.frameCount;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return chestSockets;
            var q = Q(w.EntityManager, ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            uint sum = 0;
            using (var infos = q.ToComponentDataArray<DungeonInfo>(Allocator.Temp))
                foreach (var info in infos)
                    sum += info.Id ^ info.Hash;
            var key = (w, q.CalculateEntityCount(), sum);
            if (key.Equals(chestsFor))
                return chestSockets;
            chestsFor = key;
            CollectChests(w.EntityManager, q, chestRefs, null);
            chestSockets.Clear();
            foreach (var kv in chestRefs)
                chestSockets.Add((kv.Key, kv.Value.S.Pos));
            chestSockets.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return chestSockets;
        }

        /// <summary>Each dungeon's chest sockets by chest id; two chests on one id go into <paramref name="clashes"/> (and the log).</summary>
        static void CollectChests(EntityManager em, EntityQuery q, Dictionary<int, ChestRef> into, List<string> clashes)
        {
            into.Clear();
            using var infos = q.ToComponentDataArray<DungeonInfo>(Allocator.Temp);
            using var entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < infos.Length; i++)
            {
                var info = infos[i];
                var buffer = em.GetBuffer<DungeonSocket>(entities[i], true);
                var marked = -1;
                if (info.Type != DungeonTypes.SunkenTemple && Punchy.Fauna.DungeonState.TryGet(info.Id, out var status))
                    marked = status.MarkedChest;
                for (int j = 0; j < buffer.Length; j++)
                {
                    if (buffer[j].Kind != SocketKind.Chest)
                        continue;
                    string clash = null;
                    var id = ChestId(info.Id, info.Type, j);
                    if (j > MaxChestSocket)
                        clash = $"dungeon {info.Id:X8} type {info.Type}: chest socket {j} is past {MaxChestSocket}";
                    else if (into.TryGetValue(id, out var other))
                        clash = $"chest id {id:X8}: dungeon {info.Id:X8} type {info.Type} socket {j} and dungeon {other.Dungeon:X8} type {other.Type} socket {other.Socket}";
                    if (clash != null)
                    {
                        clashes?.Add(clash);
                        Debug.LogError("Items: " + clash + " (that chest won't open)");
                        continue;
                    }
                    into[id] = new ChestRef { Dungeon = info.Id, Type = info.Type, Socket = (ushort)j, Marked = j == marked, S = buffer[j] };
                }
            }
        }

        /// <summary>
        /// For WorldTests (P-47): every dungeon chest's id on this island, "" when no two share one, else the clashes.
        /// "N chests in M dungeons" is put in <paramref name="summary"/>.
        /// </summary>
        public static string ChestIdClashes(out string summary)
        {
            summary = "no world";
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return "no world";
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            var all = new Dictionary<int, ChestRef>();
            var clashes = new List<string>();
            CollectChests(w.EntityManager, q, all, clashes);
            int caves = 0, ruins = 0, temple = 0;
            foreach (var c in all.Values)
                if (c.Type == DungeonTypes.SunkenTemple) temple++;
                else if (c.S.DungeonType == DungeonTypes.Ruin) ruins++;
                else caves++;
            summary = $"{all.Count} chests in {q.CalculateEntityCount()} dungeons (temple {temple}, cave {caves}, ruin room {ruins})";
            return string.Join("; ", clashes);
        }

        bool TryChest(int id, out DungeonSocket s)
        {
            ChestSockets();
            var found = chestRefs.TryGetValue(id, out var c);
            s = c.S;
            return found;
        }

        /// <summary>What the local player carries and wears now (kg), for the Status tab.</summary>
        public static float Carried => instance != null ? instance.CarriedWeight() : 0f;

        /// <summary>What the inventory and everything on weigh now (kg).</summary>
        float CarriedWeight()
        {
            var total = WornWeight();
            foreach (var slot in slots)
                if (slot.item != 255)
                    total += ItemTable.Items[slot.item].Weight * slot.count;
            return total;
        }

        void Pickup(int uid)
        {
            // (P-27: only stops anything when stats.json sets enforceCarry.)
            if (world.TryGetValue(uid, out var lying) && !Punchy.Player.PlayerStats.CanCarry(CarriedWeight() + ItemTable.Items[lying.Item].Weight * lying.Count))
            {
                Debug.Log($"Items: too heavy to pick up item {uid}");
                return;
            }
            if (Joined)
            {
                SendToHost(new PickupRpc { Uid = uid });
                Debug.Log($"Items: asked the host for item {uid}");
                return;
            }
            if (Take(uid, out var it))
                Grant(it.Item, it.Count);
        }

        void OpenChest(int id)
        {
            if (Joined)
            {
                if (!chestRefs.TryGetValue(id, out var c))
                    return;
                SendToHost(new OpenChestRpc { Dungeon = c.Dungeon, Type = c.Type, Socket = c.Socket });
                Debug.Log($"Items: asked the host to open chest {id} ({ChestName(c)})");
                return;
            }
            HostOpenChest(id);
        }

        static string ChestName(in ChestRef c) =>
            $"{(c.Type == DungeonTypes.SunkenTemple ? "temple" : c.S.DungeonType == DungeonTypes.Ruin ? "ruin room" : "cave")} {c.Dungeon:X8} socket {c.Socket}{(c.Marked ? ", marked" : "")}";

        /// <summary>Into the inventory: onto a stack of the same item with room, else the first empty slot.</summary>
        void Grant(byte item, int count)
        {
            var stack = ItemTable.Items[item].Stack;
            for (int i = 0; i < Capacity && count > 0; i++)
                if (slots[i].item == item && slots[i].count < stack)
                {
                    var n = math.min(count, stack - slots[i].count);
                    slots[i].count += n;
                    count -= n;
                }
            for (int i = 0; i < Capacity && count > 0; i++)
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
            var q = Q(em, ComponentType.ReadWrite<NetworkId>());
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
            if (uid >= DropBase && uid < SpareBase)
                NoteDrop(uid);
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
            if (!Joined)
                NoteSpareTaken(uid);
            if (Hosting)
                Broadcast(new ItemGoneRpc { Uid = uid });
            return true;
        }

        /// <summary>
        /// Opening a chest (P-14): its lid opens for everyone and it becomes a container; its seeded contents (1-3 items
        /// from the socket's seed, the same everywhere) stay inside to be taken through the inventory screen.
        /// </summary>
        void HostOpenChest(int id)
        {
            if (!TryChest(id, out var s) || !openedChests.Add(id))
                return;
            RegisterChests();
            var c = chestRefs[id];
            Debug.Log($"Items: chest {id} ({ChestName(c)}) opened (seed {s.Seed:X8}): {(containers.TryGetValue(id, out var box) ? Describe(box.Slots) : "?")}");
            if (Hosting)
                Broadcast(new ChestOpenedRpc { Dungeon = c.Dungeon, Type = c.Type, Socket = c.Socket });
            if (containers.ContainsKey(id))
                OpenContainer(id);
        }

        void Serve()
        {
            var em = server.EntityManager;
            ServeGear(em);
            ServeArmour(em);
            ServeSlots(em);
            ServeSwings(em);
            ServeFish(em);
            using (var rs = Q(em, ComponentType.ReadWrite<PickupRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var uid = em.GetComponentData<PickupRpc>(r).Uid;
                    var from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                    if (Take(uid, out var it) && em.Exists(from))
                    {
                        Send(em, from, new GrantRpc { Item = it.Item, Count = it.Count });
                        Carry(PlayerId(em, from), it.Item, it.Count);   // (P-17: the host's tally)
                    }
                    em.DestroyEntity(r);
                }
            using (var rs = Q(em, ComponentType.ReadWrite<DropRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var d = em.GetComponentData<DropRpc>(r);
                    Carry(PlayerId(em, em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection), d.Item, -d.Count);
                    Spawn(d.Item, d.Count, d.Pos);
                    em.DestroyEntity(r);
                }
            using (var rs = Q(em, ComponentType.ReadWrite<OpenChestRpc>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var m = em.GetComponentData<OpenChestRpc>(r);
                    var id = ChestId(m.Dungeon, m.Type, m.Socket);
                    ChestSockets();
                    // (P-47: a chest the host doesn't have, or a hash that names another chest, is dropped, said once.)
                    if (chestRefs.TryGetValue(id, out var asked) && asked.Dungeon == m.Dungeon && asked.Type == m.Type && asked.Socket == m.Socket)
                        HostOpenChest(id);
                    else
                        Debug.Log($"Items: dropped a bad OpenChestRpc: dungeon {m.Dungeon:X8} type {m.Type} socket {m.Socket} is no chest here");
                    em.DestroyEntity(r);
                }
            ServeContainers(em);
            ServeQuests(em);
            // A player who has just joined: what's been taken, added and opened so far.
            using (var cs = Q(em, ComponentType.ReadWrite<NetworkId>(), ComponentType.ReadWrite<NetworkStreamInGame>()).ToEntityArray(Allocator.Temp))
                foreach (var c in cs)
                {
                    if (!sentTo.Add(c))
                        continue;
                    foreach (var uid in gone) Send(em, c, new ItemGoneRpc { Uid = uid });
                    foreach (var kv in added) Send(em, c, new ItemSpawnRpc { Uid = kv.Key, Item = kv.Value.Item, Count = kv.Value.Count, Pos = kv.Value.Pos });
                    foreach (var s in openedChests)
                        if (chestRefs.TryGetValue(s, out var chest))
                            Send(em, c, new ChestOpenedRpc { Dungeon = chest.Dungeon, Type = chest.Type, Socket = chest.Socket });
                    SendContainers(em, c);
                }
        }

        void Broadcast<T>(T rpc) where T : unmanaged, IRpcCommand
        {
            var em = server.EntityManager;
            var q = Q(em, ComponentType.ReadWrite<NetworkId>(), ComponentType.ReadWrite<NetworkStreamInGame>());
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
            Each(em, joined, onItemGone ??= OnItemGone);
            Each(em, joined, onItemSpawn ??= OnItemSpawn);
            Each(em, joined, onGrant ??= OnGrant);
            Each(em, joined, onContainerSlot ??= ReceiveContainer);
            Each(em, joined, onQuestState ??= ReceiveQuestState);
            Each(em, joined, onChestOpened ??= OnChestOpened);
            Each(em, joined, onGearState ??= OnGearState);
            Each(em, joined, onArmourState ??= OnArmourState);
            Each(em, joined, onSlotState ??= OnSlotState);
            Each(em, joined, onSwing ??= OnSwing);
            Each(em, joined, onLeftSwing ??= OnLeftSwing);
            Each(em, joined, onFish ??= OnFish);
        }

        System.Action<ItemGoneRpc> onItemGone;
        System.Action<ItemSpawnRpc> onItemSpawn;
        System.Action<GrantRpc> onGrant;
        System.Action<ContainerSlotRpc> onContainerSlot;
        System.Action<QuestStateRpc> onQuestState;
        System.Action<ChestOpenedRpc> onChestOpened;
        System.Action<GearStateRpc> onGearState;
        System.Action<ArmourStateRpc> onArmourState;
        System.Action<SlotStateRpc> onSlotState;
        System.Action<SwingRpc> onSwing;
        System.Action<LeftSwingRpc> onLeftSwing;
        System.Action<FishRpc> onFish;

        /// <summary>Each RPC of type T that has come in: handled (on a joined client only) and used up.</summary>
        void Each<T>(EntityManager em, bool joined, System.Action<T> act) where T : unmanaged, IRpcCommand
        {
            using var rs = Q(em, ComponentType.ReadWrite<T>(), ComponentType.ReadWrite<ReceiveRpcCommandRequest>()).ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                if (joined)
                    act(em.GetComponentData<T>(r));
                em.DestroyEntity(r);
            }
        }

        void OnItemGone(ItemGoneRpc m)
        {
            if (world.Remove(m.Uid))
                Debug.Log($"Items: item {m.Uid} gone (from the host)");
            if (!added.Remove(m.Uid)) gone.Add(m.Uid);
        }

        void OnItemSpawn(ItemSpawnRpc m)
        {
            var w = new WorldItem { Item = m.Item, Count = m.Count, Pos = m.Pos };
            world[m.Uid] = w;
            added[m.Uid] = w;
            Debug.Log($"Items: item {m.Uid} ({ItemTable.Items[m.Item].Name} x{m.Count}) appeared (from the host)");
        }

        void OnGrant(GrantRpc m) => Grant(m.Item, m.Count);

        void OnChestOpened(ChestOpenedRpc m)
        {
            // (The id comes from the message alone, so a chest whose cave isn't in yet still opens when it is.)
            var id = ChestId(m.Dungeon, m.Type, m.Socket);
            if (openedChests.Add(id))
                Debug.Log($"Items: chest {id} (dungeon {m.Dungeon:X8} type {m.Type} socket {m.Socket}) opened (from the host)");
        }

        // ------------------------------------------------------------------ tests, keys, drawing

        bool itemRowArg;
        int itemRowFrom;

        /// <summary>"-itemrow" (P-23): one of every item in rows of eight from 1.8 m in front of the player, and in the
        /// inventory, the camera close; "-itemrow 36" only those from id 36 on (Zach 11-12: the folk's items).</summary>
        void ItemRow(in TerrainSource terrain)
        {
            if (!itemRowArg || Time.realtimeSinceStartup < 20f || !TryPlayer(out var player))
                return;
            itemRowArg = false;
            var forward = math.normalizesafe(math.mul(player.Rotation, new float3(0f, 0f, 1f)).xz);
            var right = new float2(forward.y, -forward.x);
            var count = ItemTable.Items.Length;
            const int across = 8;
            for (int i = math.min(itemRowFrom, count - 1); i < count; i++)
            {
                var n = i - math.min(itemRowFrom, count - 1);
                var xz = player.Position.xz + forward * (1.8f + n / across * 0.7f) + right * ((n % across - (across - 1) * 0.5f) * 0.6f);
                Spawn((byte)i, 1, new float3(xz.x, terrain.SampleHeight(xz), xz.y));
                Grant((byte)i, 1);
            }
            var cam = FindAnyObjectByType<Punchy.CameraRig.ThirdPersonCamera>();
            if (cam != null)
            {
                cam.Distance = 4.5f;
                cam.SetView(math.degrees(math.atan2(forward.x, forward.y)), 45f);
            }
            Debug.Log($"Items: -itemrow: {count} items spawned; inventory: {DescribeInventory()}");
        }

        void Tests(in TerrainSource terrain)
        {
            ItemRow(terrain);
            KitLog();
            if (pickupArg <= 0 && !dropArg && !openChestArg && takeChestKinds.Length == 0 && !badChestArg)
                return;
            testAt = testAt == 0f ? Time.realtimeSinceStartup + 30f : testAt;
            if (Time.realtimeSinceStartup < testAt || !TryPlayer(out var player))
                return;
            if (Joined)
            {
                var q = Q(client.EntityManager, ComponentType.ReadWrite<NetworkStreamInGame>());
                if (q.IsEmpty)
                    return;
            }
            testAt = Time.realtimeSinceStartup + 1.5f;
            if (badChestArg && Joined)
            {
                badChestArg = false;
                SendToHost(new OpenChestRpc { Dungeon = 0xDEADBEEFu, Type = DungeonTypes.Cave, Socket = 4095 });
                Debug.Log("Items: -badchestrpc: sent the host a chest that isn't one");
                return;
            }
            if (openChestArg && openChestKinds.Length > 0)
            {
                // (One a step: the first chest of each kind by id, so every machine picks the same.)
                var kind = openChestKinds[0];
                openChestKinds = openChestKinds[1..];
                openChestArg = openChestKinds.Length > 0;
                var id = FirstChest(kind);
                Debug.Log(id == int.MinValue ? $"Items: -openchest {kind}: no such chest ({chestRefs.Count} chests)"
                    : $"Items: -openchest {kind}: chest {id} ({ChestName(chestRefs[id])}), {math.distance(chestRefs[id].S.Pos, player.Position):F0} m away");
                if (id != int.MinValue) OpenChest(id);
                return;
            }
            if (openChestArg)
            {
                openChestArg = false;
                var best = -1;
                var bestD = float.MaxValue;
                foreach (var (socket, pos) in ChestSockets())
                {
                    var d = math.distance(pos, player.Position);
                    if (socket < CaveChestBit && d < bestD) (bestD, best) = (d, socket);
                }
                Debug.Log($"Items: -openchest: chest {best}, {bestD:F1} m away");
                if (best >= 0) OpenChest(best);
                return;
            }
            if (takeChestKinds.Length > 0 && TakeChestStep())
                return;
            if (pickupDone < pickupArg)
            {
                var best = -1;
                var bestD = float.MaxValue;
                foreach (var kv in world)
                {
                    if (spareOnlyArg && kv.Key < SpareBase)
                        continue;
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

        /// <summary>"-openchest a,b": the words after the flag (none when the next argument is another flag).</summary>
        static string[] Kinds(string[] args, string flag)
        {
            var at = System.Array.IndexOf(args, flag);
            return at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("-") ? System.Array.Empty<string>()
                : args[at + 1].Split(',', System.StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>The lowest chest id of a kind ("temple", "cave", "ruin"; ruin: a cave's ruin room), int.MinValue for none.</summary>
        int FirstChest(string kind)
        {
            foreach (var (id, _) in ChestSockets())
            {
                var c = chestRefs[id];
                var k = c.Type == DungeonTypes.SunkenTemple ? "temple" : c.S.DungeonType == DungeonTypes.Ruin ? "ruin" : "cave";
                if (k == kind)
                    return id;
            }
            return int.MinValue;
        }

        /// <summary>
        /// "-takechest cave,ruin": once the first chest of each kind is open (by anyone), take its slots one a step
        /// through the usual path (a joined client asks the host); false when there's nothing to do this step.
        /// </summary>
        bool TakeChestStep()
        {
            var kind = takeChestKinds[0];
            var id = FirstChest(kind);
            if (id == int.MinValue || !openedChests.Contains(id) || !containers.TryGetValue(id, out var c))
                return false;
            for (int i = ContainerSlots - 1; i >= 0; i--)
                if (c.Slots[i].item != 255)
                {
                    Debug.Log($"Items: -takechest {kind}: chest {id} slot {i} ({ItemTable.Items[c.Slots[i].item].Name} x{c.Slots[i].count})");
                    TakeFromContainer(id, i);
                    return true;
                }
            takeChestKinds = takeChestKinds[1..];
            Debug.Log($"Items: -takechest {kind}: chest {id} empty now; inventory: {DescribeInventory()}");
            return true;
        }

        /// <summary>
        /// The Items tab of the in-world menu (FieldMenu opens and closes it, reads the d-pad and moves the focus): while
        /// it shows, the focus is on a slot, and Submit (Enter, A) or the Use key drops one of its items (puts it in, or
        /// takes it out of, an open container).
        /// </summary>
        void UpdateInventoryKeys()
        {
            if (Punchy.UI.Overlay.Frozen)
                return;
            if (!itemsTab)
            {
                // (Closing the menu, or leaving its Items tab, steps back from an open container.)
                openContainer = -1;
                lastFocus = -1;
                return;
            }
            if (nav.Active && nav.Focus != lastFocus)
            {
                lastFocus = nav.Focus;
                (byte item, int count) s = nav.Focus < Capacity ? slots[nav.Focus] : ((byte)255, 0);
                Debug.Log($"Items: inventory slot {nav.Focus} ({(s.item == 255 ? "empty" : $"{ItemTable.Items[s.item].Name} x{s.count}")})");
            }
            selected = nav.Focus;
            // (In the menu the pad's A is its own Submit, and RB switches tab; the Use key as before.)
            if (target < 0 && targetChest < 0 && targetContainer < 0 && UseKey.KeyWasPressed())
            {
                if (openContainer >= 0 && selected >= Capacity) TakeFromContainer(openContainer, selected - Capacity);
                else if (openContainer >= 0) Put(selected);
                else if (selected < Capacity) Drop(selected);
            }
            EquipKeys();
        }

        /// <summary>
        /// "-invpad": drives the panel with a virtual gamepad through the Input System (the real input path): View opens it,
        /// d-pad right three times, A drops one, B closes it. Starts 45 s in (after any -pickup). P-39 (#397): with the panel
        /// open it also holds the left stick up for a step, and logs how far the player walked and where the focus went
        /// (the stick walks, the d-pad alone moves the focus).
        /// </summary>
        void PadTest()
        {
            if (!padTest)
                return;
            padAt = padAt == 0f ? Time.realtimeSinceStartup + 45f : padAt;
            if (Time.realtimeSinceStartup < padAt)
                return;
            padAt = Time.realtimeSinceStartup + 1f;   // (a second a step, so a shot can catch the panel open)
            // (A test window often isn't in front: let its input through anyway, as a real pad's would be when focused.)
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            if (testPad == null)
            {
                testPad = InputSystem.AddDevice<Gamepad>("InventoryTestPad");
                // (P-16: a device added while the window isn't in front can come up disabled.)
                InputSystem.EnableDevice(testPad);
            }
            testPad.MakeCurrent();
            // (Start: a stand-in for "the left stick, held up": the pad's Start opens the main menu, so it's never pressed.)
            GamepadButton[] script = { GamepadButton.Select, GamepadButton.Start, GamepadButton.DpadRight, GamepadButton.DpadRight, GamepadButton.DpadLeft, GamepadButton.South, GamepadButton.East };
            var press = padStep % 2 == 0;
            var button = script[padStep / 2];
            var stick = button == GamepadButton.Start;
            var state = new UnityEngine.InputSystem.LowLevel.GamepadState();
            if (press && stick)
                state.leftStick = new Vector2(0f, 1f);
            else if (press)
                state = state.WithButton(button);
            if (stick && TryPlayer(out var at))
            {
                if (press)
                    stickFrom = at.Position;
                else
                    Debug.Log($"Items: -invpad stick walked {math.distance(stickFrom.xz, at.Position.xz):0.00} m with the panel {(panel ? "open" : "closed")}; focus {nav.Focus} (active {nav.Active})");
            }
            InputSystem.QueueStateEvent(testPad, state);
            // (P-16: processed now, so this frame's actions and wasPressedThisFrame see it.)
            InputSystem.Update();
            if (press)
                Debug.Log($"Items: -invpad presses {(stick ? "left stick up" : button.ToString())} (pad enabled {testPad.enabled}, current {Gamepad.current == testPad}, {button} down {testPad[button].isPressed})");
            if (++padStep >= script.Length * 2)
            {
                padTest = false;
                InputSystem.RemoveDevice(testPad);
            }
        }

        readonly Dictionary<int, (GameObject root, Transform lid)> chests = new Dictionary<int, (GameObject, Transform)>();
        float hideStandInsAt;

        /// <summary>
        /// P-12: a chest at every temple Chest socket, a box and a lid on a hinge at the back; the lid opens (to 105
        /// degrees, over half a second) once the chest is in openedChests (the host's word) and stays open. The A-50
        /// spawner's placeholder box on the same socket (a "Dungeon ..." primitive, drawn magenta in a build) is hidden.
        /// </summary>
        void DrawChests()
        {
            var list = ChestSockets();
            if (list.Count == 0)
                return;
            foreach (var (socket, pos) in list)
            {
                if (!chests.TryGetValue(socket, out var c) || c.root == null)
                {
                    TryChest(socket, out var s);
                    var root = new GameObject($"Chest {socket}") { hideFlags = HideFlags.HideInHierarchy };
                    root.transform.SetPositionAndRotation(pos, s.Rot);
                    Part(root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.8f, 0.44f, 0.5f), new Color(0.42f, 0.27f, 0.14f));
                    Part(root.transform, new Vector3(0f, 0.22f, 0.255f), new Vector3(0.82f, 0.08f, 0.02f), new Color(0.35f, 0.33f, 0.3f));
                    var hinge = new GameObject("Lid hinge").transform;
                    hinge.SetParent(root.transform, false);
                    hinge.localPosition = new Vector3(0f, 0.44f, -0.25f);
                    Part(hinge, new Vector3(0f, 0.06f, 0.25f), new Vector3(0.82f, 0.12f, 0.52f), new Color(0.48f, 0.31f, 0.16f));
                    Part(hinge, new Vector3(0f, 0.04f, 0.5f), new Vector3(0.12f, 0.1f, 0.03f), new Color(0.85f, 0.7f, 0.25f));
                    c = (root, hinge);
                    chests[socket] = c;
                }
                var angle = openedChests.Contains(socket) ? -105f : 0f;
                var now = c.lid.localEulerAngles.x > 180f ? c.lid.localEulerAngles.x - 360f : c.lid.localEulerAngles.x;
                c.lid.localRotation = Quaternion.Euler(Mathf.MoveTowards(now, angle, Time.deltaTime * 210f), 0f, 0f);
            }
            if (Time.realtimeSinceStartup >= hideStandInsAt)
            {
                hideStandInsAt = Time.realtimeSinceStartup + 3f;
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!r.enabled || !r.gameObject.name.StartsWith("Dungeon "))
                        continue;
                    foreach (var (_, pos) in list)
                        if (math.distance(((float3)r.transform.position).xz, pos.xz) < 0.4f)
                        {
                            r.enabled = false;
                            break;
                        }
                }
            }
        }

        static void Part(Transform parent, Vector3 at, Vector3 size, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.hideFlags = HideFlags.HideInHierarchy;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (itemMaterial == null) itemMaterial = Resources.Load<Material>("ItemMaterial");   // (not ??=: it misses a destroyed Unity object)
            if (itemMaterial != null)
                r.sharedMaterial = itemMaterial;
            var b = new MaterialPropertyBlock();
            b.SetColor(BaseColorId, colour);
            r.SetPropertyBlock(b);
        }

        /// <summary>Small coloured blocks for the items within DrawRange of the camera (a pool of primitives).</summary>
        readonly HashSet<int> drawNear = new HashSet<int>();
        readonly List<int> drawDrop = new List<int>();

        void Draw()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var near = drawNear;
            near.Clear();
            foreach (var kv in world)
                if (math.distancesq(kv.Value.Pos, (float3)cam.transform.position) < DrawRange * DrawRange)
                    near.Add(kv.Key);
            var drop = drawDrop;
            drop.Clear();
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
                var go = new GameObject("Item " + uid) { hideFlags = HideFlags.HideInHierarchy };
                var mesh = ItemMeshes.Of(it.Item);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                go.transform.position = (Vector3)it.Pos + Vector3.up * 0.02f;
                go.transform.rotation = Quaternion.Euler(0f, uid * 37 % 360, 0f);
                // (Items/Resources/ItemLibrary: Colossus.shader, W-166 r4, so an item on the ground takes the material
                // library's look for what it's made of (ItemDef.Family), the same as a building's beams. Cubes, stands
                // and props keep ItemMaterial, the pipeline's own lit material.)
                if (libraryMaterial == null) libraryMaterial = Resources.Load<Material>("ItemLibrary");   // (not ??=: it misses a destroyed Unity object)
                // (The item's Colour and Accent are in the mesh's vertex colours, ItemMeshes.Of: no property block.)
                if (libraryMaterial != null)
                    r.sharedMaterials = mesh.subMeshCount > 1 ? new[] { libraryMaterial, libraryMaterial } : new[] { libraryMaterial };
                if (uid >= SpareBase)
                    StandSpare(uid, it, go, mesh);
                shown[uid] = go;
            }
            DrawStands(cam.transform.position);
        }

        // P-20 (profiler finding 11): drawn through a GuiHook, on only while there is something to draw, so this
        // script stays out of Unity's IMGUI pass (and its per-frame garbage) the rest of the time.
        void Start()
        {
            Punchy.UI.GuiHook.Add(gameObject, DrawGui, () => !Punchy.UI.Overlay.Frozen && prompt != null, true);
        }

        /// <summary>Where the inventory stands: on the right, its top a quarter down the screen (higher for more rows).</summary>
        Rect InventoryRect(float cell, float gap)
        {
            var w = 4 * cell + 5 * gap;
            var rows = Capacity / 4;
            return new Rect(Screen.width - w - 20f, Mathf.Max(80f, Screen.height * 0.25f - 28f - (rows - 4) * cell), w, rows * (cell + gap) + gap + 28f);
        }

        /// <summary>The in-world menu's Quests tab: the quest log, where the inventory stands.</summary>
        void DrawQuestsTab(Punchy.UI.MenuNav menuNav)
        {
            var area = InventoryRect(74f, 6f);
            DrawQuestLog(new Rect(area.x, area.y, area.width, Screen.height - area.y - 40f));
        }

        void DrawGui()
        {
            if (Punchy.UI.Overlay.Frozen)
                return;
            if (prompt != null)
            {
                // (P-16: long text, a quest's, wraps at 60% of the screen and the box grows to fit.)
                promptStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(12, 12, 6, 6) };
                var content = new GUIContent(prompt);
                var width = Mathf.Min(promptStyle.CalcSize(content).x + 4f, Screen.width * 0.6f);
                var height = promptStyle.CalcHeight(content, width);
                GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height * 0.72f, width, height), content, promptStyle);
            }
        }

        /// <summary>
        /// The in-world menu's Items tab: the inventory on the right, an open container's pane left of it, what's worn
        /// under it (P-39).
        /// </summary>
        void DrawItemsTab(Punchy.UI.MenuNav menuNav)
        {
            slotStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.LowerCenter, wordWrap = true };
            const float cell = 74f, gap = 6f;
            var area = InventoryRect(cell, gap);
            var w = area.width;
            var rows = Capacity / 4;
            // (P-20: the button for the device in use: the Use key, or A on a pad.)
            GUI.Box(area, $"Inventory   ({(InputGlyphs.Gamepad ? InputGlyphs.Path("<Gamepad>/buttonSouth") ?? "A" : UseKey.Current.ToString())}: drop one)");
            var colour = GUI.color;
            GUILayout.BeginArea(new Rect(area.x + gap, area.y + 28f, w - 2 * gap, area.height - 28f));
            for (int row = 0; row < rows; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 4; col++)
                {
                    var i = row * 4 + col;
                    var s = slots[i];
                    if (nav.Button(s.item == 255 ? "" : $"{ItemTable.Items[s.item].Name}\nx{s.count}", slotStyle, GUILayout.Width(cell), GUILayout.Height(cell)))
                    {
                        selected = i;
                        if (openContainer >= 0) Put(i);
                        else Drop(i);
                    }
                    RightClickEquip(GUILayoutUtility.GetLastRect(), i);
                    if (s.item != 255 && Event.current.type == EventType.Repaint)
                    {
                        var rect = GUILayoutUtility.GetLastRect();
                        GUI.color = Color.white;
                        GUI.DrawTexture(new Rect(rect.x + cell * 0.2f, rect.y + 2f, cell * 0.6f, cell * 0.6f), ItemMeshes.Icon(s.item), ScaleMode.ScaleToFit);
                        GUI.color = colour;
                    }
                    GUILayout.Space(gap);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(gap);
            }
            GUILayout.EndArea();
            DrawContainerPane(cell, gap, area);
            DrawWorn(area, cell, gap);
            GUI.color = colour;
        }
    }
}
