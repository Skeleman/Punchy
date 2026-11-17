using System.Collections.Generic;
using System.IO;
using System.Text;
using Punchy.Items;
using UnityEditor;
using UnityEngine;

namespace Punchy.EditorTools
{
    /// <summary>
    /// Writes the item catalog from <see cref="ItemTable.Items"/> (P-35 row 2, #26): an <see cref="ItemAsset"/> per row in
    /// Assets/Game/Data/Items/ and the <see cref="ItemIds"/> registry in Resources. Safe to run again at any time, and
    /// needed after rows are appended to ItemTable:
    /// - a row that already has an id keeps it and its key; its asset takes the row's fields again (a rename keeps the key);
    /// - a new row is appended with a key made from its name;
    /// - nothing is ever removed or reordered, and a table shorter than the registry is refused.
    /// Menu Punchy/Items/Generate catalog; batch: -executeMethod Punchy.EditorTools.ItemCatalogGenerator.Generate.
    /// </summary>
    public static class ItemCatalogGenerator
    {
        public const string IdsAsset = "Assets/Game/Resources/" + ItemCatalog.IdsPath + ".asset";
        public const string ItemFolder = "Assets/Game/Data/Items";

        [MenuItem("Punchy/Items/Generate catalog")]
        public static void Generate()
        {
            var ids = AssetDatabase.LoadAssetAtPath<ItemIds>(IdsAsset);
            if (ids == null)
            {
                ids = ScriptableObject.CreateInstance<ItemIds>();
                AssetDatabase.CreateAsset(ids, IdsAsset);
            }
            if (ids.Entries.Count == 0)
                ids.Entries.Add(new ItemIds.Entry { Key = ItemIds.NoneKey });
            var table = ItemTable.Items;
            if (ids.Entries.Count - 1 > table.Length)
            {
                Debug.LogError($"Items: the registry has {ids.Entries.Count - 1} items but ItemTable only {table.Length}: rows are " +
                               "never removed (mark one Retired in the registry instead). Nothing written.");
                return;
            }
            Directory.CreateDirectory(ItemFolder);
            var used = new HashSet<string>();
            foreach (var e in ids.Entries)
                used.Add(e.Key);
            int made = 0, renamed = 0;
            for (int row = 0; row < table.Length; row++)
            {
                var id = row + 1;
                if (id == ids.Entries.Count)
                {
                    var key = Unique(KeyFor(table[row].Name), used);
                    ids.Entries.Add(new ItemIds.Entry { Key = key });
                    made++;
                }
                var entry = ids.Entries[id];
                if (entry.Retired)
                    continue;
                if (entry.Item == null)
                {
                    var path = $"{ItemFolder}/{entry.Key}.asset";
                    entry.Item = AssetDatabase.LoadAssetAtPath<ItemAsset>(path);
                    if (entry.Item == null)
                    {
                        entry.Item = ScriptableObject.CreateInstance<ItemAsset>();
                        AssetDatabase.CreateAsset(entry.Item, path);
                    }
                    ids.Entries[id] = entry;
                }
                if (entry.Item.DisplayName != null && entry.Item.DisplayName != table[row].Name)
                    renamed++;
                entry.Item.Key = entry.Key;
                entry.Item.FromDef(table[row]);
                EditorUtility.SetDirty(entry.Item);
            }
            EditorUtility.SetDirty(ids);
            AssetDatabase.SaveAssets();
            ItemCatalog.Reload();
            Debug.Log($"Items: catalog written, {ids.Entries.Count - 1} items ({made} new, {renamed} renamed), keys hash " +
                      $"{ItemCatalog.Hash:x8}, fingerprint {ItemCatalog.Fingerprint(ItemCatalog.Defs, 1):x16}, " +
                      $"ItemTable {ItemCatalog.Fingerprint(table):x16}");
        }

        /// <summary>"Smith's hammer" -> "smiths_hammer": lower case letters and digits, words joined by '_'.</summary>
        public static string KeyFor(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name.ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                    sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_')
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                        sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd('_') : "item";
        }

        static string Unique(string key, HashSet<string> used)
        {
            var k = key;
            for (int n = 2; !used.Add(k); n++)
                k = key + "_" + n;
            return k;
        }
    }
}
