using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>
    /// The item id registry (P-35 row 2, #26; DESIGN-items-save section 2), Resources/ItemIds.asset. An entry's index is
    /// its ushort id. It only grows: a removed item keeps its entry, marked Retired, so no id ever moves. Entry 0 is
    /// "nothing". Entry n + 1 is today's ItemTable row n (the shift to these ids comes with the network row).
    /// </summary>
    public class ItemIds : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            public bool Retired;
            public ItemAsset Item;
        }

        public const string NoneKey = "none";
        public List<Entry> Entries = new List<Entry>();
    }
}
