using Punchy.Fauna;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// What the folk carry and wear, as items (Zach 11-12, one item system): a body's kit (FolkKit) from what it is and
    /// what it's doing, the same items a player can hold and wear, drawn the same way. Every guard owns its folk's weapon
    /// and armour (a human's spear, kettle hat and breastplate; a dwarf's axe and horned helm; an elf's bow and crested
    /// helm; a goblin's club, shield, pot and leather; an ogre's great club, beast skull, leather and hide cape) and a
    /// sash: the weapon in hand while drawn, sheathed otherwise. Workers own their job's tool, in hand at work and in the
    /// bag off it, and wear their job's hat and apron. What the brain puts in a hand (CreatureState.Held: a lantern, a
    /// torch, a rod, a staff, a letter, a stick or stone, a pick or a sack of ore) is in that hand. Cave folk, pygmies and
    /// bat folk go bareheaded (A-77).
    /// </summary>
    public static class FolkKits
    {
        public static FolkKit Fill(in CreatureState s, ref CreatureBlob b)
        {
            var k = default(FolkKit);
            var atWork = s.Mode == CreatureMode.Work && s.Stage != 0;
            var job = FolkJobs.Of(in s);
            var bare = b.CaveDweller;

            // A guard: its folk's weapon (drawn, or sheathed), helmet, armour and sash.
            if (s.Task == CreatureTask.Patrol)
            {
                var weapon = b.GuardKit switch
                {
                    GuardKit.Dwarf => ItemTable.Axe,
                    GuardKit.Elf => ItemTable.Bow,
                    GuardKit.Goblin => ItemTable.Club,
                    GuardKit.Ogre => ItemTable.GreatClub,
                    _ => ItemTable.Spear,
                };
                var drawn = s.Held == HeldItem.SpearDrawn && b.GuardKit != GuardKit.Elf;
                if (drawn)
                    k.Hand = FolkKit.Of(weapon);
                else
                    k.Sheath = FolkKit.Of(weapon);
                if (s.Held == HeldItem.Torch)
                    k.Hand = FolkKit.Of(ItemTable.Torch);
                if (!bare)
                    k.Head = FolkKit.Of(b.GuardKit switch
                    {
                        GuardKit.Dwarf => ItemTable.HornedHelm,
                        GuardKit.Elf => ItemTable.CrestedHelm,
                        GuardKit.Goblin => ItemTable.PotHelm,
                        GuardKit.Ogre => ItemTable.SkullHelm,
                        _ => ItemTable.KettleHat,
                    });
                k.Body = FolkKit.Of(b.GuardKit == GuardKit.Goblin || b.GuardKit == GuardKit.Ogre ? ItemTable.Cuirass : ItemTable.Breastplate);
                k.Neck = FolkKit.Of(ItemTable.Sash);
                if (b.GuardKit == GuardKit.Ogre)
                    k.Back = FolkKit.Of(ItemTable.HideCape);
                if (b.GuardKit == GuardKit.Goblin)
                    k.LeftHand = FolkKit.Of(ItemTable.Shield);
                return k;
            }

            // A hunter's spear: in hand going after game, across its back about the town.
            if (s.Task == CreatureTask.Hunt)
            {
                if (s.Mode == CreatureMode.Hunt)
                    k.Hand = FolkKit.Of(ItemTable.Spear);
                else
                    k.Sheath = FolkKit.Of(ItemTable.Spear);
            }

            // What the brain has put in its hands.
            switch (s.Held)
            {
                case HeldItem.Lantern:
                    k.LeftHand = FolkKit.Of(ItemTable.Lantern);
                    break;
                case HeldItem.Torch:
                    k.Hand = FolkKit.Of(ItemTable.Torch);
                    break;
                case HeldItem.Rod:
                    k.Hand = FolkKit.Of(ItemTable.Rod);
                    k.Bag0 = FolkKit.Of(ItemTable.Creel);
                    break;
                case HeldItem.Stick:
                    k.Hand = FolkKit.Of(ItemTable.Stick);
                    break;
                case HeldItem.Stone:
                    k.Hand = FolkKit.Of(ItemTable.Stone);
                    break;
                case HeldItem.Pickaxe:
                    // (In both hands at the ore face; across its back on the way.)
                    if (s.Mode == CreatureMode.Work && s.Stage == CreatureAnimator.MinerAtFace)
                        k.Hand = FolkKit.Of(ItemTable.Pickaxe);
                    else
                        k.Sheath = FolkKit.Of(ItemTable.Pickaxe);
                    break;
                case HeldItem.OreSack:
                    // (A miner's pick stays across its back, below; a bat folk bringing goods home has only the sack, #35.)
                    k.Hand = FolkKit.Of(ItemTable.OreSack);
                    break;
                case HeldItem.Staff:
                    k.Hand = FolkKit.Of(ItemTable.Staff);
                    k.Back = FolkKit.Of(ItemTable.Pack);
                    break;
                case HeldItem.Letter:
                    k.Hand = FolkKit.Of(ItemTable.Letter);
                    break;
                case HeldItem.Goggles:
                    k.Head = FolkKit.Of(ItemTable.Goggles);
                    break;
            }

            // What it owns whatever's in hand: a traveller its pack and staff (the staff in the bag while a lantern's
            // out), a miner its pick (across its back while it's not in hand).
            if (s.Task == CreatureTask.Travel)
            {
                k.Back = FolkKit.Of(ItemTable.Pack);
                if (k.Hand == 0)
                    k.Hand = FolkKit.Of(ItemTable.Staff);
                else if (k.Hand != FolkKit.Of(ItemTable.Staff))
                    k.Bag0 = FolkKit.Of(ItemTable.Staff);
            }
            if (s.Task == CreatureTask.Mine && k.Hand != FolkKit.Of(ItemTable.Pickaxe))
                k.Sheath = FolkKit.Of(ItemTable.Pickaxe);

            // A worker's tool (in hand at work, in the bag off it), and what its job wears.
            var tool = -1;
            var other = -1;
            switch (s.Task)
            {
                case CreatureTask.Watch:
                    // (A gatekeeper's spear, upright in its fist, while it keeps the gate with nothing else in hand.)
                    if (s.Held == HeldItem.None)
                        tool = ItemTable.Spear;
                    break;
                case CreatureTask.Hammer:
                    tool = ItemTable.SmithHammer;
                    other = ItemTable.Tongs;
                    k.Body = FolkKit.Of(ItemTable.LeatherApron);
                    break;
                case CreatureTask.Chop:
                    tool = job == FolkJob.Farmer ? ItemTable.Hoe : ItemTable.Axe;
                    if (!bare)
                        k.Head = FolkKit.Of(ItemTable.StrawHat);
                    break;
                case CreatureTask.Gesture:
                    // (A stallholder's wares in hand, a jar or a fruit by individual; its apron.)
                    tool = (s.Individual & 1u) != 0u ? ItemTable.Jar : ItemTable.Apple;
                    k.Body = FolkKit.Of(ItemTable.Apron);
                    break;
                case CreatureTask.Tend:
                    if (job == FolkJob.Baker)
                    {
                        tool = ItemTable.Dough;
                        k.Body = FolkKit.Of(ItemTable.BakerApron);
                        if (!bare)
                            k.Head = FolkKit.Of(ItemTable.BakerCap);
                    }
                    else if (job == FolkJob.Healer)
                    {
                        tool = ItemTable.Bowl;
                        k.Back = FolkKit.Of(ItemTable.Satchel);
                    }
                    else
                    {
                        // (A well keeper's bucket is on the well's rope, a post prop; anyone else tending carries one.)
                        if (job != FolkJob.WellKeeper)
                            other = ItemTable.Bucket;
                        if (!bare)
                            k.Head = FolkKit.Of(ItemTable.StrawHat);
                    }
                    break;
                case CreatureTask.Fish:
                    if (!bare)
                        k.Head = FolkKit.Of(ItemTable.FisherHat);
                    break;
                case CreatureTask.Hermit:
                    // (Its tool by the landmark's work, in Stage at work: 1 axe, 3 broom, 4 shovel, else a hammer; none
                    // while sitting by its door, stage 2.)
                    tool = s.Stage == 1 ? ItemTable.Axe : s.Stage == 3 ? ItemTable.Broom : s.Stage == 4 ? ItemTable.Shovel : ItemTable.SmithHammer;
                    if (!bare)
                        k.Head = FolkKit.Of(ItemTable.StrawHat);
                    break;
            }
            // What stands at its post while it works there (row 5; drawn by the Gear pass): a baker's kneading table
            // and a finished loaf, the well's rope and the bucket on it, a smith's hot iron, a woodcutter's chopping
            // block and the log on it.
            if (atWork)
            {
                (int, int) post = s.Task == CreatureTask.Hammer ? (ItemTable.HotIron, -1)
                                : s.Task == CreatureTask.Chop && job != FolkJob.Farmer ? (ItemTable.ChoppingBlock, ItemTable.Log)
                                : s.Task == CreatureTask.Tend && job == FolkJob.Baker ? (ItemTable.KneadingTable, ItemTable.Bread)
                                : s.Task == CreatureTask.Tend && job == FolkJob.WellKeeper ? (ItemTable.Rope, ItemTable.Bucket)
                                : (-1, -1);
                if (post.Item1 >= 0)
                    k.Post0 = FolkKit.Of(post.Item1);
                if (post.Item2 >= 0)
                    k.Post1 = FolkKit.Of(post.Item2);
            }
            var toolInHand = atWork && !(s.Task == CreatureTask.Hermit && s.Stage == 2);
            if (tool >= 0)
            {
                if (toolInHand && k.Hand == 0)
                    k.Hand = FolkKit.Of(tool);
                else
                    k.Bag1 = FolkKit.Of(tool);
            }
            if (other >= 0)
            {
                if (toolInHand && k.LeftHand == 0)
                    k.LeftHand = FolkKit.Of(other);
                else
                    k.Bag2 = FolkKit.Of(other);
            }
            return k;
        }

        /// <summary>
        /// Zach 11-12 row 3: the looks a folk body's kit is drawn with, by the same passes as a player's outfit (the
        /// Weapon and Armour passes): what it wears on its head, body, back and neck, and the tool or ware in its hands at
        /// work and what's in its other hand (a shield, tongs, a bucket). What moves in its own way is still drawn by its
        /// own pass from CreatureState.Held, the same thing the kit's hand has in it: a guard's weapon (drawn and
        /// sheathed) and torch, a hunter's spear, a lantern and its light, a rod and its line, a miner's pick, a
        /// traveller's staff, a letter, a child's stick or stone. Burst reads it (PoseJob, CompactJob): the looks are
        /// HandLook and WornLook, checked against the item table at start (CheckLooks).
        /// </summary>
        public static CreatureOutfit Dress(in CreatureState s)
        {
            var o = s.Outfit;
            var k = s.Kit;
            if (k.Empty || s.Mode == CreatureMode.Puppet)
                return o;
            if (s.Held == HeldItem.None && s.Task != CreatureTask.Patrol && s.Task != CreatureTask.Hunt)
                o.Weapon = HandLook(FolkKit.Item(k.Hand));
            // (The left hand's, whatever it is, but a lantern: its own pass draws it, lit.)
            var left = FolkKit.Item(k.LeftHand);
            if (left >= 0 && left != ItemTable.Lantern)
                o.LeftHand = HandLook(left);
            o.Head = WornLook(FolkKit.Item(k.Head));
            o.Body = WornLook(FolkKit.Item(k.Body));
            o.Back = WornLook(FolkKit.Item(k.Back));
            o.Neck = WornLook(FolkKit.Item(k.Neck));
            return o;
        }

        /// <summary>How an item is drawn in the hand (ItemDef.Weapon), for Burst. 0: not as a weapon look.</summary>
        public static byte HandLook(int item) => item switch
        {
            ItemTable.Stick => CreatureWeapon.Stick,
            ItemTable.Stone => CreatureWeapon.Stone,
            ItemTable.Club => CreatureWeapon.Club,
            ItemTable.Spear => CreatureWeapon.Spear,
            ItemTable.Sword => CreatureWeapon.Sword,
            ItemTable.Bow => CreatureWeapon.Bow,
            ItemTable.Axe => CreatureWeapon.Axe,
            ItemTable.Hammer => CreatureWeapon.Hammer,
            ItemTable.Rod => CreatureWeapon.Rod,
            ItemTable.Pickaxe => CreatureWeapon.Pickaxe,
            ItemTable.Shovel => CreatureWeapon.Shovel,
            ItemTable.GreatClub => CreatureWeapon.GreatClub,
            ItemTable.Shield => CreatureWeapon.Shield,
            ItemTable.Letter => CreatureWeapon.Letter,
            ItemTable.OreSack => CreatureWeapon.OreSack,
            ItemTable.Hoe => CreatureWeapon.Hoe,
            ItemTable.Broom => CreatureWeapon.Broom,
            ItemTable.SmithHammer => CreatureWeapon.SmithHammer,
            ItemTable.Tongs => CreatureWeapon.Tongs,
            ItemTable.Bucket => CreatureWeapon.Bucket,
            ItemTable.Bowl => CreatureWeapon.Bowl,
            ItemTable.Jar => CreatureWeapon.Jar,
            ItemTable.Apple => CreatureWeapon.Fruit,
            ItemTable.Bread => CreatureWeapon.Loaf,
            ItemTable.Dough => CreatureWeapon.Dough,
            _ => CreatureWeapon.None,
        };

        /// <summary>How an item is drawn worn (ItemDef.Look, by its place), for Burst. 0: not worn as a look.</summary>
        public static byte WornLook(int item) => item switch
        {
            // (P-28's wear, ids 17-29: no folk wears them yet, but a kit could.)
            17 => CreatureArmour.Cap,
            18 => CreatureArmour.Helm,
            19 => CreatureArmour.Jerkin,
            20 => CreatureArmour.Chain,
            21 => CreatureArmour.Greaves,
            22 => CreatureArmour.Boots,
            23 => CreatureArmour.Tunic,
            24 => CreatureArmour.Robe,
            25 => CreatureArmour.Cloak,
            26 => CreatureArmour.Scarf,
            27 => CreatureArmour.Hood,
            28 => CreatureArmour.Woolcap,
            29 => CreatureArmour.Brimhat,
            ItemTable.Goggles => CreatureArmour.Goggles,
            ItemTable.KettleHat => CreatureArmour.KettleHat,
            ItemTable.HornedHelm => CreatureArmour.HornedHelm,
            ItemTable.CrestedHelm => CreatureArmour.CrestedHelm,
            ItemTable.PotHelm => CreatureArmour.PotHelm,
            ItemTable.SkullHelm => CreatureArmour.SkullHelm,
            ItemTable.Breastplate => CreatureArmour.Breastplate,
            ItemTable.Cuirass => CreatureArmour.Cuirass,
            ItemTable.Sash => CreatureArmour.Sash,
            ItemTable.HideCape => CreatureArmour.HideCape,
            ItemTable.Apron => CreatureArmour.Apron,
            ItemTable.LeatherApron => CreatureArmour.LeatherApron,
            ItemTable.BakerApron => CreatureArmour.BakerApron,
            ItemTable.BakerCap => CreatureArmour.BakerCap,
            ItemTable.StrawHat => CreatureArmour.StrawHat,
            ItemTable.FisherHat => CreatureArmour.FisherHat,
            ItemTable.Satchel => CreatureArmour.Satchel,
            _ => 0,
        };

        /// <summary>
        /// Zach 11-12 row 3: the item a body's sheath is drawn with by the shared sheath pass (PoseJob.Weapon, sheathed),
        /// on its back or at its hip (OnBack); -1 for none. Its kit's sheathed weapon or tool, unless its own pass draws
        /// it, drawing and sheathing it as it goes: a guard's while it has its weapon or a torch out (Guard), a hunter's
        /// spear (Spear), a miner's pick (Miner). So a guard carrying a lantern, or with nothing in hand, still wears
        /// its folk's weapon.
        /// </summary>
        public static int Sheathed(in CreatureState s)
        {
            // (A player's: what it has put away with the sheathe key, once it's past the middle of the reach; row 4.)
            if (s.Mode == CreatureMode.Puppet)
                return s.Outfit.Kept != 0 && s.Outfit.Stow >= 128 && HandLook(s.Outfit.Kept - 1) != CreatureWeapon.None &&
                       SheathOf(s.Outfit.Kept - 1) != ItemSheath.None ? s.Outfit.Kept - 1 : -1;
            if (s.Kit.Sheath == 0 || s.Task == CreatureTask.Hunt || s.Task == CreatureTask.Mine ||
                s.Held == HeldItem.Pickaxe || s.Held == HeldItem.OreSack ||
                s.Task == CreatureTask.Patrol && (s.Held == HeldItem.Spear || s.Held == HeldItem.SpearDrawn || s.Held == HeldItem.Torch))
                return -1;
            var item = FolkKit.Item(s.Kit.Sheath);
            return SheathOf(item) == ItemSheath.None || HandLook(item) == CreatureWeapon.None ? -1 : item;
        }

        /// <summary>An item's length (ItemDef.Length, m at scale 1), for Burst.</summary>
        public static float Length(int item) => item switch
        {
            ItemTable.Stick => 0.6f,
            ItemTable.Staff => 1.6f,
            ItemTable.Club => 0.8f,
            ItemTable.Spear => 2.1f,
            ItemTable.Sword => 0.95f,
            ItemTable.Bow => 1.1f,
            ItemTable.Axe => 0.75f,
            ItemTable.Hammer => 0.7f,
            ItemTable.Rod => 1.8f,
            ItemTable.Pickaxe => 0.75f,
            ItemTable.Shovel => 1.05f,
            ItemTable.GreatClub => 1.15f,
            ItemTable.Shield => 0.34f,
            ItemTable.Hoe => 1.1f,
            ItemTable.Broom => 1.25f,
            ItemTable.SmithHammer => 0.4f,
            ItemTable.Tongs => 0.35f,
            _ => 0f,
        };

        /// <summary>Where an item goes sheathed (ItemDef.Sheath), for Burst.</summary>
        public static ItemSheath SheathOf(int item) => item switch
        {
            ItemTable.Club or ItemTable.Spear or ItemTable.Axe or ItemTable.Hammer or ItemTable.Pickaxe or ItemTable.Shovel or
                ItemTable.GreatClub or ItemTable.Hoe or ItemTable.Broom => ItemSheath.Auto,
            ItemTable.Bow or ItemTable.Rod or ItemTable.Shield => ItemSheath.Back,
            ItemTable.Sword or ItemTable.SmithHammer or ItemTable.Tongs => ItemSheath.Hip,
            _ => ItemSheath.None,
        };

        /// <summary>ItemSheaths.OnBack for Burst: on the back if it's long for its bearer, else at the hip.</summary>
        public static bool OnBack(int item, float standHeight) =>
            SheathOf(item) == ItemSheath.Back || SheathOf(item) == ItemSheath.Auto && Length(item) > 0.6f * 2f * standHeight;

        /// <summary>Whether HandLook and WornLook (and Length and SheathOf) say what the item table does, for every item
        /// (an item added with a look and not added here would be drawn bare on the folk). Logged as an error if not.</summary>
        public static void CheckLooks()
        {
            for (int i = 0; i < ItemTable.Items.Length; i++)
            {
                if (Length(i) != ItemTable.Items[i].Length || SheathOf(i) != ItemTable.Items[i].Sheath)
                    Debug.LogError($"Items: FolkKits.Length/SheathOf({i}) is {Length(i)} {SheathOf(i)}, the {ItemTable.Items[i].Name}'s are {ItemTable.Items[i].Length} {ItemTable.Items[i].Sheath}");
                if (HandLook(i) != ItemTable.Items[i].Weapon)
                    Debug.LogError($"Items: FolkKits.HandLook({i}) is {HandLook(i)}, the {ItemTable.Items[i].Name}'s look is {ItemTable.Items[i].Weapon}");
                if (WornLook(i) != ItemTable.Items[i].Look)
                    Debug.LogError($"Items: FolkKits.WornLook({i}) is {WornLook(i)}, the {ItemTable.Items[i].Name}'s look is {ItemTable.Items[i].Look}");
                // (Zach 11-12: what the folk hold and wear, a player can hold and wear: an item with a hand look goes in
                // a hand, one with a worn look in a worn place.)
                var slot = ItemTable.Items[i].Slot;
                // (A held thing's Slot is Hand, either hand: there's no off hand.)
                if (ItemTable.Items[i].Weapon != CreatureWeapon.None && slot != GearSlot.Hand)
                    Debug.LogError($"Items: FolkKits: the {ItemTable.Items[i].Name} has a hand look but a player can't hold it (Slot {slot})");
                if (ItemTable.Items[i].Look != 0 && (slot == GearSlot.None || slot == GearSlot.Hand || slot == GearSlot.LeftHand))
                    Debug.LogError($"Items: FolkKits: the {ItemTable.Items[i].Name} has a worn look but a player can't wear it (Slot {slot})");
            }
        }
    }

    /// <summary>
    /// The host (or solo): each of the folk's kit, after the brain has decided what it's doing this frame and before it's
    /// written for clients (CreatureNet). Only upright bodies with hands that aren't players'.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CreatureBrainSystem))]
    [UpdateBefore(typeof(Punchy.Net.CreatureNetWriteSystem))]
    public partial struct FolkKitSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new KitJob().ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct KitJob : IJobEntity
        {
            void Execute(ref CreatureState s, in CreatureBody body)
            {
                ref var b = ref body.Blob.Value;
                if (!b.Upright || !b.Hands || s.Mode == CreatureMode.Puppet)
                    return;
                var kit = FolkKits.Fill(in s, ref b);
                if (!kit.Equals(s.Kit))
                    s.Kit = kit;
            }
        }
    }

    // Zach 11-12 row 2's test: "-kitlog" logs the kits of the twelve folk nearest the player every 10 s, as this machine
    // has them (a client's from the host), one line each by individual, so host and client lines can be matched.
    public partial class Items
    {
        static readonly bool kitLogArg = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-kitlog") >= 0;
        float kitLogAt = 30f;

        void KitLog()
        {
            if (!kitLogArg || Time.realtimeSinceStartup < kitLogAt || !TryPlayer(out var player))
                return;
            kitLogAt = Time.realtimeSinceStartup + 10f;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<CreatureState>(), ComponentType.ReadOnly<CreatureBody>());
            using var states = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            using var bodies = q.ToComponentDataArray<CreatureBody>(Allocator.Temp);
            var near = new System.Collections.Generic.List<(float d, int i)>();
            var withKit = 0;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].Kit.Empty)
                    continue;
                withKit++;
                near.Add((math.distance(states[i].Position, player.Position), i));
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));
            Debug.Log($"[kit] {withKit} of {states.Length} creatures have a kit");
            for (int n = 0; n < math.min(near.Count, 12); n++)
            {
                var s = states[near[n].i];
                var kit = s.Kit;
                Debug.Log($"[kit] {s.Individual:X8} {bodies[near[n].i].Blob.Value.GuardKit} {s.Task}/{s.Mode} held {s.Held}, {near[n].d:0} m: right {KitName(kit.Hand)}, left {KitName(kit.LeftHand)}, " +
                          $"sheath {KitName(kit.Sheath)}, head {KitName(kit.Head)}, body {KitName(kit.Body)}, back {KitName(kit.Back)}, neck {KitName(kit.Neck)}, " +
                          $"bag {KitName(kit.Bag0)}/{KitName(kit.Bag1)}/{KitName(kit.Bag2)}/{KitName(kit.Bag3)}, post {KitName(kit.Post0)}/{KitName(kit.Post1)}");
            }
        }

        static string KitName(byte slot)
        {
            var item = FolkKit.Item(slot);
            return item < 0 ? "-" : item < ItemTable.Items.Length ? ItemTable.Items[item].Name : "?" + item;
        }
    }
}
