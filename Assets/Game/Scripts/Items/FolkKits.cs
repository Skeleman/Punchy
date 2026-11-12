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
                    k.OffHand = FolkKit.Of(ItemTable.Shield);
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
                    k.OffHand = FolkKit.Of(ItemTable.Lantern);
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
                    k.Hand = FolkKit.Of(ItemTable.OreSack);
                    k.Sheath = FolkKit.Of(ItemTable.Pickaxe);
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
                if (toolInHand && k.OffHand == 0)
                    k.OffHand = FolkKit.Of(other);
                else
                    k.Bag2 = FolkKit.Of(other);
            }
            return k;
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
                Debug.Log($"[kit] {s.Individual:X8} {bodies[near[n].i].Blob.Value.GuardKit} {s.Task}/{s.Mode} held {s.Held}, {near[n].d:0} m: hand {KitName(kit.Hand)}, off {KitName(kit.OffHand)}, " +
                          $"sheath {KitName(kit.Sheath)}, head {KitName(kit.Head)}, body {KitName(kit.Body)}, back {KitName(kit.Back)}, neck {KitName(kit.Neck)}, " +
                          $"bag {KitName(kit.Bag0)}/{KitName(kit.Bag1)}/{KitName(kit.Bag2)}/{KitName(kit.Bag3)}");
            }
        }

        static string KitName(byte slot)
        {
            var item = FolkKit.Item(slot);
            return item < 0 ? "-" : item < ItemTable.Items.Length ? ItemTable.Items[item].Name : "?" + item;
        }
    }
}
