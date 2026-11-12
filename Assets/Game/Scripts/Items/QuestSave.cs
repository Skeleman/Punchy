using System.Collections.Generic;
using System.IO;
using Punchy.Terrain;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Punchy.Items
{
    /// <summary>
    /// Keeping quests across sessions, and choosing which one to follow (P-24 row 1). The save names everything by what
    /// stays meaningful as the island changes: the quest's id, its town (its name and where it stands), and the world it
    /// was saved in (a hash of the towns). Loaded against a changed world, a quest whose id, town or item no longer exists
    /// isn't an error: it's listed in the quest log as "lost to time", and dropping it refunds some coin. New fields can be
    /// added freely: JsonUtility skips what it doesn't know and leaves what it can't find at their defaults. A file per
    /// machine, each player keeping their own; a joined client tells the host again what it had taken. Reaching a quest
    /// again doesn't pay its reward twice: a done quest is only shown as done. Tracking is the log's alone: no overlay.
    /// Test flags: "-questsave path" (another file, so tests leave the real one alone), "-droplost" (drops every lost
    /// quest, 40 s in).
    /// </summary>
    public partial class Items
    {
        const string SaveName = "quests-save.json";
        const int LostRefund = 5;

        [System.Serializable]
        class SavedQuest
        {
            public string id, town;
            public int state;
            public bool tracked, visited;
        }

        [System.Serializable]
        class QuestSaveFile
        {
            public string world;
            public SavedQuest[] quests;
        }

        class LostQuest
        {
            public SavedQuest Saved;
            public string Why;
        }

        readonly List<LostQuest> lostQuests = new List<LostQuest>();
        (int Board, int Slot) tracked = (-1, -1);
        bool saveLoaded, saveDirty, dropLostArg;
        string savePath;

        void QuestSaveArgs(string[] args)
        {
            var at = System.Array.IndexOf(args, "-questsave");
            savePath = at >= 0 && at + 1 < args.Length ? args[at + 1] : Path.Combine(Application.persistentDataPath, SaveName);
            dropLostArg = System.Array.IndexOf(args, "-droplost") >= 0;
            if (System.Array.IndexOf(args, "-questflow") >= 0)
                StartCoroutine(QuestFlow());
        }

        string TownKey(int board) => $"{boards[board].Town}@{math.round(boards[board].Pos.x):F0},{math.round(boards[board].Pos.z):F0}";

        string WorldHash()
        {
            var h = 2166136261u;
            for (int b = 0; b < boards.Count; b++)
                foreach (var ch in TownKey(b))
                    h = (h ^ ch) * 16777619u;
            return h.ToString("X8");
        }

        /// <summary>Called each frame once the boards are up: reads the save once, writes it after a change.</summary>
        void UpdateQuestSave()
        {
            if (boards.Count == 0 || quests.Length == 0)
                return;
            if (!saveLoaded && (!Joined || !Q(client.EntityManager, Unity.Entities.ComponentType.ReadWrite<Unity.NetCode.NetworkId>()).IsEmpty))
                LoadSave();
            if (saveDirty && saveLoaded)
                WriteSave();
            if (dropLostArg && Time.realtimeSinceStartup > 40f)
            {
                dropLostArg = false;
                while (lostQuests.Count > 0)
                    DropLost(lostQuests[0]);
            }
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if (panel && (keyboard != null && keyboard.uKey.wasPressedThisFrame || pad != null && pad.buttonNorth.wasPressedThisFrame))
                TrackNext();
        }

        void TrackNext()
        {
            var open = new List<(int, int)>();
            foreach (var kv in myQuests)
                if (kv.Value == 1)
                    open.Add(kv.Key);
            if (open.Count == 0)
                return;
            open.Sort();
            tracked = open[(open.IndexOf(tracked) + 1) % open.Count];
            saveDirty = true;
        }

        void LoadSave()
        {
            saveLoaded = true;
            if (!File.Exists(savePath))
                return;
            QuestSaveFile file;
            try
            {
                file = JsonUtility.FromJson<QuestSaveFile>(File.ReadAllText(savePath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Quests: couldn't read the save: {e.Message}");
                return;
            }
            if (file?.quests == null)
                return;
            var world = WorldHash();
            int restored = 0;
            foreach (var q in file.quests)
            {
                if (!Find(q, out var board, out var slot, out var why))
                {
                    lostQuests.Add(new LostQuest { Saved = q, Why = why });
                    Debug.Log($"Quests: \"{q.id}\" in {q.town} is lost to time: {why}");
                    continue;
                }
                restored++;
                myQuests[(board, slot)] = q.state;
                if (q.visited)
                    visited.Add((board, slot));
                if (q.tracked)
                    tracked = (board, slot);
                // (Taken: the host is told again. Done: only shown, so its reward isn't paid twice.)
                if (q.state == 1)
                {
                    if (Joined)
                        SendToHost(new TakeQuestRpc { Board = board, Slot = (byte)slot });
                    else
                        HostRecord(0, board, slot, 1, "from the save");
                }
            }
            Debug.Log($"Quests: save read: {restored} restored, {lostQuests.Count} lost to time, saved in world {file.world}, this world {world}");
        }

        /// <summary>Where a saved quest is in this world, or why it isn't.</summary>
        bool Find(SavedQuest q, out int board, out int slot, out string why)
        {
            (board, slot, why) = (-1, -1, null);
            var def = System.Array.Find(quests, d => d.id == q.id);
            if (def == null)
            {
                why = "that quest no longer exists";
                return false;
            }
            if (!string.IsNullOrEmpty(def.item) && def.type != "clear" && ItemId(def.item) < 0)
            {
                why = $"there's no such thing as {def.item} any more";
                return false;
            }
            for (int b = 0; b < boards.Count; b++)
                if (TownKey(b) == q.town)
                    board = b;
            if (board < 0)
            {
                why = "that town is gone";
                return false;
            }
            for (int s = 0; s < boards[board].Offers.Length; s++)
                if (Def(board, s).id == q.id)
                {
                    slot = s;
                    return true;
                }
            why = "that town no longer asks for it";
            return false;
        }

        void WriteSave()
        {
            saveDirty = false;
            var list = new List<SavedQuest>();
            foreach (var kv in myQuests)
                list.Add(new SavedQuest { id = Def(kv.Key.Item1, kv.Key.Item2).id, town = TownKey(kv.Key.Item1), state = kv.Value, tracked = kv.Key == tracked, visited = visited.Contains(kv.Key) });
            foreach (var lost in lostQuests)
                list.Add(lost.Saved);
            try
            {
                File.WriteAllText(savePath, JsonUtility.ToJson(new QuestSaveFile { world = WorldHash(), quests = list.ToArray() }, true));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Quests: couldn't write the save: {e.Message}");
            }
        }

        void DropLost(LostQuest lost)
        {
            lostQuests.Remove(lost);
            Grant((byte)ItemId("Coin"), LostRefund);
            saveDirty = true;
            Debug.Log($"Quests: dropped \"{lost.Saved.id}\", {LostRefund} coins back; inventory: {DescribeInventory()}");
        }

        /// <summary>The quests that send the player somewhere: reached (within this many m of the other town's board).</summary>
        const float VisitReach = 15f;
        readonly HashSet<(int, int)> visited = new HashSet<(int, int)>();

        /// <summary>The other town of a deliver or visit quest: the same for every machine, never the board it came from.</summary>
        int DestBoard(int board, int slot)
        {
            if (boards.Count < 2)
                return board;
            var h = math.hash(new uint2((uint)board, (uint)slot + 0x51u));
            return (board + 1 + (int)(h % (uint)(boards.Count - 1))) % boards.Count;
        }

        void UpdateVisits(Unity.Transforms.LocalTransform player)
        {
            foreach (var kv in myQuests)
                if (kv.Value == 1 && Def(kv.Key.Item1, kv.Key.Item2).type == "visit" && !visited.Contains(kv.Key) &&
                    math.distance(boards[DestBoard(kv.Key.Item1, kv.Key.Item2)].Pos.xz, player.Position.xz) < VisitReach)
                {
                    visited.Add(kv.Key);
                    saveDirty = true;
                    Debug.Log($"Quests: reached {boards[DestBoard(kv.Key.Item1, kv.Key.Item2)].Town} for {Def(kv.Key.Item1, kv.Key.Item2).id}");
                    break;
                }
        }

        /// <summary>"-questflow": takes a deliver and a visit quest, goes where each says and hands them in, through the real calls.</summary>
        System.Collections.IEnumerator QuestFlow()
        {
            yield return new WaitForSecondsRealtime(25f);
            foreach (var type in new[] { "deliver", "visit" })
            {
                int board = -1, slot = -1;
                for (int b = 0; b < boards.Count && board < 0; b++)
                    for (int s = 0; s < boards[b].Offers.Length; s++)
                        if (Def(b, s).type == type && !myQuests.ContainsKey((b, s)))
                            (board, slot) = (b, s);
                if (board < 0)
                {
                    Debug.Log($"Quests: -questflow: no board offers a {type}");
                    continue;
                }
                var dest = DestBoard(board, slot);
                MoveTo(boards[board].Pos);
                yield return new WaitForSecondsRealtime(1f);
                TakeQuest(board, slot);
                Debug.Log($"Quests: -questflow {type}: {Def(board, slot).id} from {boards[board].Town} to {boards[dest].Town}; inventory: {DescribeInventory()}; progress: {QuestProgress(board, slot)}");
                MoveTo(boards[dest].Pos);
                yield return new WaitForSecondsRealtime(2f);
                if (type == "visit")
                {
                    MoveTo(boards[board].Pos);
                    yield return new WaitForSecondsRealtime(2f);
                }
                var there = type == "visit" ? board : dest;
                var next = NextSlot(there, out var turnIn, out var from);
                Debug.Log($"Quests: -questflow {type}: at {boards[there].Town} the board offers slot {next} (hand in: {turnIn}); progress: {QuestProgress(board, slot)}");
                if (turnIn)
                    TurnIn(from, next);
                yield return new WaitForSecondsRealtime(1f);
                Debug.Log($"Quests: -questflow {type}: state {myQuests[(board, slot)]}; inventory: {DescribeInventory()}");
            }
        }

        void MoveTo(float3 at)
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var q = Q(em, Unity.Entities.ComponentType.ReadWrite<Punchy.Player.PlayerTag>(), Unity.Entities.ComponentType.ReadWrite<Unity.Transforms.LocalTransform>());
            if (!q.TryGetSingletonEntity<Unity.Transforms.LocalTransform>(out var player))
                return;
            var t = em.GetComponentData<Unity.Transforms.LocalTransform>(player);
            t.Position = at + new float3(2f, 1f, 2f);
            em.SetComponentData(player, t);
        }

        /// <summary>How a taken quest stands, for the log: "Berries 3/5", or where to hand it in.</summary>
        string QuestProgress(int board, int slot)
        {
            var q = Def(board, slot);
            if (q.type == "clear")
                return "drive them all off, then wait";
            if (q.type == "visit")
                return visited.Contains((board, slot)) ? $"back to the board in {boards[board].Town}" : $"go to {boards[DestBoard(board, slot)].Town}";
            if (q.type == "deliver")
                return $"take it to the board in {boards[DestBoard(board, slot)].Town}";
            if (HasFor(board, slot))
                return $"hand in at the board in {boards[board].Town}";
            var id = ItemId(q.item);
            var have = 0;
            if (id >= 0)
                foreach (var s in slots)
                    if (s.item == id)
                        have += s.count;
            return $"{q.item} {have}/{math.max(q.count, 1)}";
        }
    }
}
