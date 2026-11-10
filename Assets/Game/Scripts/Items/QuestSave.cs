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
            public bool tracked;
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
                list.Add(new SavedQuest { id = Def(kv.Key.Item1, kv.Key.Item2).id, town = TownKey(kv.Key.Item1), state = kv.Value, tracked = kv.Key == tracked });
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

        /// <summary>How a taken quest stands, for the log: "Berries 3/5", or where to hand it in.</summary>
        string QuestProgress(int board, int slot)
        {
            var q = Def(board, slot);
            if (q.type == "clear")
                return "drive them all off, then wait";
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
