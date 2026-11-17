using System.Collections;
using System.Collections.Generic;
using Punchy.Dungeon;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// Dungeon quests (P-49): a notice board names one cave or ruin room near its town and asks for what its chest holds.
    /// No RPC of its own: the chest is derived from (board, slot) by <see cref="PickQuestChest"/>, the same on every
    /// machine (the three chests of the quest's kind nearest the board, one picked by board, slot and quest id). When a
    /// player takes it the host (or solo) puts that player's copy of the item into the chest through the container path,
    /// or on the ground by the chest if it's full; it's handed in once the chest has been opened and the item is carried
    /// (the host checks its own record of both). The save keeps the chest's id: a world where the quest now names
    /// another chest lists it as "lost to time". Test flag: "-dungeonquest cave|ruin" (take the nearest board's quest of
    /// that kind, open its chest, take the item out and hand it in, through the real calls).
    /// </summary>
    public partial class Items
    {
        /// <summary>No chest: the quest can't be taken here.</summary>
        public const int NoChest = int.MinValue;
        /// <summary>How many of the nearest chests of the kind a board picks from.</summary>
        const int QuestChestChoices = 3;
        /// <summary>How long the chest list must stay unchanged before quests trust it (caves come in over a few frames).</summary>
        const float ChestsSettleTime = 3f, ChestsSettleLimit = 60f;

        readonly Dictionary<(int, int), int> questChests = new Dictionary<(int, int), int>();
        (World, int, uint) questChestsFor;
        float chestsChangedAt = -1f, chestsFirstAt = -1f;
        string dungeonQuestArg;
        bool digestLogged;

        void DungeonQuestArgs(string[] args)
        {
            var at = System.Array.IndexOf(args, "-dungeonquest");
            if (at >= 0)
            {
                dungeonQuestArg = at + 1 < args.Length && !args[at + 1].StartsWith("-") ? args[at + 1] : "cave";
                StartCoroutine(DungeonQuestFlow());
            }
        }

        static bool IsDungeon(QuestDef q) => q.type == "dungeon";

        /// <summary>
        /// The chest a dungeon quest names: of the chests of <paramref name="kind"/> ("cave" or "ruin"; kinds[i] is 0 for a
        /// cave chest, 1 for a ruin room's, 2 for the temple's, never picked), the <see cref="QuestChestChoices"/> nearest
        /// the board (ties by id), then one of those by a scrambled hash of board, slot and quest id. <see cref="NoChest"/>
        /// if there's none of the kind. Pure, so every machine with the same chests picks the same (EditMode tests call it).
        /// </summary>
        public static int PickQuestChest(int[] ids, Vector3[] positions, byte[] kinds, Vector3 board, int boardIndex, int slot, string questId, string kind)
        {
            var want = kind == "ruin" ? 1 : kind == "cave" ? 0 : -1;
            if (want < 0 || ids == null)
                return NoChest;
            var near = new List<(float d, int id)>();
            for (int i = 0; i < ids.Length; i++)
                if (kinds[i] == want)
                    near.Add((new Vector2(positions[i].x - board.x, positions[i].z - board.z).sqrMagnitude, ids[i]));
            if (near.Count == 0)
                return NoChest;
            near.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.id.CompareTo(b.id));
            var n = math.min(near.Count, QuestChestChoices);
            var h = 2166136261u;
            foreach (var ch in questId ?? "")
                h = (h ^ ch) * 16777619u;
            // (Scrambled: a plain hash of nearby numbers barely moves, so neighbouring boards would all pick alike.)
            var pick = Punchy.Fauna.DungeonSpawner.Scramble(h ^ (uint)boardIndex * 0x9E3779B9u ^ (uint)slot << 24);
            return near[(int)(pick % (uint)n)].id;
        }

        /// <summary>The chest quest (board, slot) names (NoChest: none, or not a dungeon quest), cached until the chests change.</summary>
        int QuestChest(int board, int slot)
        {
            ChestSockets();
            if (!questChestsFor.Equals(chestsFor))
            {
                questChestsFor = chestsFor;
                questChests.Clear();
                chestsChangedAt = Time.realtimeSinceStartup;
                digestLogged = false;
            }
            if (board < 0 || !IsDungeon(Def(board, slot)))
                return NoChest;
            if (questChests.TryGetValue((board, slot), out var id))
                return id;
            var q = Def(board, slot);
            var count = chestRefs.Count;
            var ids = new int[count];
            var pos = new Vector3[count];
            var kinds = new byte[count];
            var i = 0;
            foreach (var kv in chestRefs)
            {
                ids[i] = kv.Key;
                pos[i] = kv.Value.S.Pos;
                kinds[i] = (byte)(kv.Value.Type == DungeonTypes.SunkenTemple ? 2 : kv.Value.S.DungeonType == DungeonTypes.Ruin ? 1 : 0);
                i++;
            }
            id = PickQuestChest(ids, pos, kinds, boards[board].Pos, board, slot, q.id, q.kind);
            questChests[(board, slot)] = id;
            return id;
        }

        /// <summary>Whether the chest list has stopped changing (or waiting has gone on long enough): saves and tests wait for it.</summary>
        bool ChestsSettled()
        {
            QuestChest(-1, -1);
            var now = Time.realtimeSinceStartup;
            if (chestsChangedAt < 0f)
                chestsChangedAt = now;
            if (chestsFirstAt < 0f)
                chestsFirstAt = now;
            return chestRefs.Count > 0 && now - chestsChangedAt >= ChestsSettleTime || now - chestsFirstAt > ChestsSettleLimit;
        }

        /// <summary>Each frame (UpdateQuests): once the chests settle, logs a digest of every board slot's quest chest, for the host/client test.</summary>
        void UpdateDungeonQuests()
        {
            if (digestLogged || !ChestsSettled())
                return;
            digestLogged = true;
            var h = 2166136261u;
            int n = 0, none = 0;
            for (int b = 0; b < boards.Count; b++)
                for (int s = 0; s < boards[b].Offers.Length; s++)
                    if (IsDungeon(Def(b, s)))
                    {
                        var id = QuestChest(b, s);
                        h = (h ^ (uint)id) * 16777619u;
                        n++;
                        if (id == NoChest) none++;
                    }
            Debug.Log($"Quests: dungeon chests digest {h:X8} over {n} board slots ({none} without a chest; {chestRefs.Count} chests)");
        }

        static string DungeonWord(QuestDef q) => q.kind == "ruin" ? "ruin room" : "cave";

        /// <summary>"about 120 m north-east of Ashford" for a dungeon quest's chest, or "near Ashford" if it has none.</summary>
        string QuestWhere(int board, int slot)
        {
            var id = QuestChest(board, slot);
            if (id == NoChest || !chestRefs.TryGetValue(id, out var c))
                return $"near {boards[board].Town}";
            var d = c.S.Pos.xz - boards[board].Pos.xz;
            var metres = math.max(10f, math.round(math.length(d) / 10f) * 10f);
            var octant = (int)math.round(math.atan2(d.y, d.x) / (math.PI / 4f));
            return $"about {metres:F0} m {Compass[(octant + 8) % 8]} of {boards[board].Town}";
        }

        // (x is east and z north, so the angle from +x turns through north.)
        static readonly string[] Compass = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };

        bool DungeonChestOpen(int board, int slot)
        {
            var id = QuestChest(board, slot);
            return id != NoChest && openedChests.Contains(id);
        }

        /// <summary>Host or solo: a player took a dungeon quest; their copy of the item goes into the chest (beside it if full).</summary>
        void PlaceQuestItem(int player, int board, int slot, string who)
        {
            var q = Def(board, slot);
            var item = ItemId(q.item);
            var id = QuestChest(board, slot);
            if (item < 0 || id == NoChest || !chestRefs.TryGetValue(id, out var c))
            {
                Debug.LogWarning($"Quests: {q.id} for player {player} at board {board}: no chest to put the {q.item} in");
                return;
            }
            RegisterChests();
            var count = math.max(q.count, 1);
            if (HostPut(id, (byte)item, count, who))
                Debug.Log($"Quests: put the {q.item} for {q.id} (player {player}) into chest {id} ({ChestName(c)}), {QuestWhere(board, slot)}");
            else
            {
                Spawn((byte)item, count, c.S.Pos + new float3(0f, 0.3f, 0f));
                Debug.Log($"Quests: chest {id} is full: the {q.item} for {q.id} (player {player}) lies beside it");
            }
        }

        /// <summary>A dungeon quest's line in the log: go there, take it from the chest, hand it in.</summary>
        string DungeonProgress(int board, int slot)
        {
            var q = Def(board, slot);
            if (HasFor(board, slot))
                return $"hand in at the board in {boards[board].Town}";
            if (!DungeonChestOpen(board, slot))
                return $"go to the {DungeonWord(q)} {QuestWhere(board, slot)}";
            return $"take the {q.item} from the chest";
        }

        /// <summary>"-dungeonquest cave|ruin": takes the nearest board's quest of that kind, opens its chest, takes the item, hands it in.</summary>
        IEnumerator DungeonQuestFlow()
        {
            yield return new WaitForSecondsRealtime(20f);
            while (boards.Count == 0 || quests.Length == 0 || !ChestsSettled() || !TryPlayer(out _) ||
                   Joined && Q(client.EntityManager, ComponentType.ReadWrite<NetworkStreamInGame>()).IsEmpty)
                yield return new WaitForSecondsRealtime(1f);
            TryPlayer(out var player);
            int board = -1, slot = -1;
            var best = float.MaxValue;
            for (int b = 0; b < boards.Count; b++)
            {
                var d = math.distance(boards[b].Pos.xz, player.Position.xz);
                if (d >= best)
                    continue;
                // (A posted slot if the board shows one of the kind, else its folk copy.)
                for (int s = boards[b].Offers.Length - 1; s >= 0; s--)
                    if (IsDungeon(Def(b, s)) && Def(b, s).kind == dungeonQuestArg && !myQuests.ContainsKey((b, s)) && QuestChest(b, s) != NoChest)
                        (best, board, slot) = (d, b, s);
            }
            if (board < 0)
            {
                Debug.Log($"Quests: -dungeonquest {dungeonQuestArg}: no board has one");
                yield break;
            }
            var chest = QuestChest(board, slot);
            TakeQuest(board, slot);
            Debug.Log($"Quests: -dungeonquest {dungeonQuestArg}: took {Def(board, slot).id} at board {board} slot {slot} ({boards[board].Town}), chest {chest}; progress: {QuestProgress(board, slot)}");
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log($"Quests: -dungeonquest: chest {chest} holds {DescribeContainer(chest)}; ready to hand in: {HasFor(board, slot)}");
            OpenChest(chest);
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log($"Quests: -dungeonquest: chest {chest} open: {openedChests.Contains(chest)}; progress: {QuestProgress(board, slot)}");
            var item = ItemId(Def(board, slot).item);
            if (containers.TryGetValue(chest, out var box))
                for (int i = 0; i < ContainerSlots; i++)
                    if (box.Slots[i].item == item)
                    {
                        TakeFromContainer(chest, i);
                        break;
                    }
            yield return new WaitForSecondsRealtime(3f);
            var next = NextSlot(board, out var turnIn, out var from);
            Debug.Log($"Quests: -dungeonquest: inventory {DescribeInventory()}; progress: {QuestProgress(board, slot)}; the board offers slot {next} (hand in: {turnIn})");
            if (turnIn && next == slot && from == board)
                TurnIn(board, slot);
            yield return new WaitForSecondsRealtime(3f);
            Debug.Log($"Quests: -dungeonquest: {Def(board, slot).id} state {myQuests[(board, slot)]}; inventory {DescribeInventory()}");
        }
    }
}
