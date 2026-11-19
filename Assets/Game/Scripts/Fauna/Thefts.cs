using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-134 row 3: a theft as the host decided it, sent to every client (the host's own client world too): who took
    /// (their NetworkId), from which household (its index in Households.All, checked by its Key), what (an item name),
    /// whether anyone saw, how many days ago (0 for one just now), and the guard sent to the door (its Individual, 0 for
    /// none) and the spot it walks to. Old marks a catch-up record sent to a joining client: it goes into the thief's
    /// record but nobody reacts to it again.
    /// </summary>
    public struct TheftRpc : IRpcCommand
    {
        public int Thief;
        public int House;
        public uint Key;
        public FixedString64Bytes Item;
        public byte Seen, Old;
        public int Ago;
        public uint Guard;
        public float2 Chase;
    }

    /// <summary>
    /// A-134 row 3: one theft from a joined client's own save, told to the host as the client joins (the host keeps 32 per
    /// player at most and drops a house it can't find). House is the saved house key (Thefts.KeyOf).
    /// </summary>
    public struct TheftRecordRpc : IRpcCommand
    {
        public FixedString128Bytes House;
        public FixedString64Bytes Item;
        public int Ago;
    }

    /// <summary>One theft as the save keeps it: the house by kind and door spot (Thefts.KeyOf), the item by name, and
    /// how many days ago it was (the cycle's Day starts again at 0 each session, so the save can't keep a day).</summary>
    [Serializable]
    public class SavedTheft
    {
        public string house;
        public string item;
        public int ago;
    }

    /// <summary>
    /// The thefts (A-134 row 3, DESIGN-npc-ownership): a take from a household's storage, seen by one of the household at
    /// home or by a guard on its rounds near the door, angers the owners (a line from the first of them the thief meets,
    /// and no favours while the record holds) and sends the nearest guard to the door. The host decides (Ownership.Took
    /// hands it the takes, FaunaDirector.ServeThefts checks the witnesses) and tells everyone by TheftRpc. Each machine
    /// keeps its own player's record (Mine), saved with the quests in the machine's one save file; a joined client tells
    /// the host its saved record as it joins. A record fades after FadeDays days.
    /// </summary>
    public static class Thefts
    {
        /// <summary>How many days a household holds a theft against a player (DESIGN-npc-ownership).</summary>
        public const int FadeDays = 3;
        /// <summary>The most records the host keeps for one joined player from its save.</summary>
        public const int MaxHeld = 32;

        /// <summary>One theft in a record: the house key, the item, the session day it happened on (resolved from the
        /// save's ago once the cycle runs), and, for one read from this machine's save on a joined client, whether the
        /// host is still to be told.</summary>
        internal sealed class Record
        {
            public string House;
            public string Item;
            public int Day;
            public int Ago;
            public bool Dated;
            public bool ToHost;
        }

        /// <summary>A take the host or solo game is still to judge: owner household, taker (NetworkId, -1 the local
        /// player) and the item's name.</summary>
        internal struct Take
        {
            public int Owner, Player;
            public string Item;
        }

        /// <summary>This machine's player's record.</summary>
        internal static readonly List<Record> Mine = new List<Record>();
        internal static readonly List<Take> Pending = new List<Take>();
        /// <summary>The cycle's day now (set each frame by the director; the save's ago counts from it).</summary>
        internal static int Today;
        static bool dirty;

        /// <summary>A household's key in the save and on the wire: its kind and its door spot, the way the households are
        /// placed from the seed, so it names the same house in every session of the same world.</summary>
        public static string KeyOf(Household house) =>
            string.Format(CultureInfo.InvariantCulture, "{0}@{1:F1},{2:F1}", house.Kind, house.Door.x, house.Door.z);

        /// <summary>The household a key names in this world (its door within Ownership.DoorCells, and the same kind), or
        /// Nobody.</summary>
        public static int Resolve(string key)
        {
            if (string.IsNullOrEmpty(key) || !Households.Ready)
                return Ownership.Nobody;
            var at = key.LastIndexOf('@');
            var comma = key.IndexOf(',', at + 1);
            if (at < 0 || comma < 0 ||
                !float.TryParse(key.Substring(at + 1, comma - at - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !float.TryParse(key.Substring(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                return Ownership.Nobody;
            var house = Ownership.HouseAt(new float2(x, z));
            return house >= 0 && Households.All[house].Kind == key.Substring(0, at) ? house : Ownership.Nobody;
        }

        public static bool KnownItem(string item) => Array.Exists(Punchy.Items.ItemTable.Items, d => d.Name == item);

        static bool Fresh(Record r) => !r.Dated || Today - r.Day < FadeDays;

        /// <summary>Whether this folk's household holds a fresh theft against this machine's player (FolkConversation:
        /// no favours then).</summary>
        public static bool HeldAgainst(uint individual)
        {
            if (Mine.Count == 0 || !Households.Of(individual, out var house, out _))
                return false;
            var key = KeyOf(house);
            foreach (var r in Mine)
                if (r.House == key && Fresh(r))
                    return true;
            return false;
        }

        /// <summary>From Ownership.Took, on the host or a solo game: an owned take for ServeThefts to judge. The item is
        /// the take's name up to its " xN" count.</summary>
        internal static void Took(int owner, int player, string what)
        {
            var item = what;
            var x = what.IndexOf(" x", StringComparison.Ordinal);
            if (x > 0)
                item = what.Substring(0, x);
            Pending.Add(new Take { Owner = owner, Player = player, Item = item });
        }

        /// <summary>Adds a theft to this machine's player's record (one already there, same house, item and day, is not
        /// added twice); true if it's new.</summary>
        internal static bool AddMine(string house, string item, int day)
        {
            foreach (var r in Mine)
                if (r.Dated && r.House == house && r.Item == item && r.Day == day)
                    return false;
            Mine.Add(new Record { House = house, Item = item, Day = day, Dated = true });
            dirty = true;
            return true;
        }

        /// <summary>From the quest save as it's read (once per machine and session): the saved record becomes this one. An
        /// old save without thefts (null) adds nothing. Each is dated, checked and (on a joined client) told to the host
        /// once the households are up.</summary>
        public static void Load(SavedTheft[] saved)
        {
            Mine.Clear();
            if (saved == null)
                return;
            foreach (var t in saved)
                if (t != null && !string.IsNullOrEmpty(t.house) && !string.IsNullOrEmpty(t.item) && t.ago >= 0 && t.ago < FadeDays)
                    Mine.Add(new Record { House = t.house, Item = t.item, Ago = t.ago, ToHost = true });
            UnityEngine.Debug.Log($"[thefts] save read: {Mine.Count} thefts in this player's record");
        }

        /// <summary>For the quest save: this player's fresh thefts, by days ago.</summary>
        public static SavedTheft[] Save()
        {
            var list = new List<SavedTheft>();
            foreach (var r in Mine)
                if (Fresh(r))
                    list.Add(new SavedTheft { house = r.House, item = r.Item, ago = r.Dated ? math.max(0, Today - r.Day) : r.Ago });
            return list.ToArray();
        }

        /// <summary>Whether the record changed since the save last asked (and the flag cleared).</summary>
        public static bool TakeDirty()
        {
            var was = dirty;
            dirty = false;
            return was;
        }

        /// <summary>Once the cycle runs and the households are up: dates the saved records and drops any whose house or item
        /// this world doesn't have (logged). Returns how many were dropped.</summary>
        internal static int DateSaved()
        {
            var dropped = 0;
            for (int i = Mine.Count - 1; i >= 0; i--)
            {
                var r = Mine[i];
                if (r.Dated)
                    continue;
                if (Resolve(r.House) < 0 || !KnownItem(r.Item))
                {
                    UnityEngine.Debug.Log($"[thefts] dropped a saved theft of {r.Item} from {r.House}: no such {(KnownItem(r.Item) ? "house" : "item")} in this world");
                    Mine.RemoveAt(i);
                    dropped++;
                    dirty = true;
                    continue;
                }
                r.Day = Today - r.Ago;
                r.Dated = true;
            }
            return dropped;
        }
    }
}
