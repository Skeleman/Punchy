using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Items
{
    /// <summary>One item kind for Burst code: the numbers of an <see cref="ItemAsset"/>, without its name.</summary>
    public struct ItemBlobRow
    {
        public float4 Colour, Accent;
        public int Stack, Slots;
        public float Weight, Light, Reach, Cover, Damage, Speed, Armour, Length;
        public GearSlot Slot;
        public ItemSheath Sheath;
        public MaterialFamily Family, AccentFamily;
        public byte Weapon, Look, Kind, AccentKind;
        public bool TwoHanded, Dyed;
    }

    /// <summary>The catalog for Burst code (FolkKits.Fill, brains, FishCatch once they move over): a row per id, row 0
    /// "nothing", and the hash of the keys in order.</summary>
    public struct ItemBlob
    {
        public uint Hash;
        public BlobArray<ItemBlobRow> Rows;
    }

    /// <summary>
    /// The item catalog (P-35 row 2, #26; DESIGN-items-save section 2): the <see cref="ItemIds"/> registry and its
    /// <see cref="ItemAsset"/>s, loaded once, as a managed array by id (<see cref="Defs"/>) and as a BlobAsset
    /// (<see cref="Blob"/>). Ids here are ushort, 0 = nothing, and id n + 1 is <see cref="ItemTable"/> row n.
    /// Nothing reads it in play yet: the game still runs on ItemTable until the network row moves it over. Until then
    /// <see cref="Check"/> logs an error at start if the two ever differ (regenerate: Punchy/Items/Generate catalog).
    /// </summary>
    public static class ItemCatalog
    {
        public const string IdsPath = "ItemIds";

        static ItemIds ids;
        static ItemDef[] defs;
        static string[] keys;
        static uint hash;
        static BlobAssetReference<ItemBlob> blob;

        /// <summary>Every id's item (index 0: an empty def for "nothing", and for a retired id).</summary>
        public static ItemDef[] Defs { get { Load(); return defs; } }
        public static int Count => Defs.Length;
        /// <summary>FNV-1a of the keys in order: what the join check will compare (row 3).</summary>
        public static uint Hash { get { Load(); return hash; } }
        public static string KeyOf(ushort id) { Load(); return id < keys.Length ? keys[id] : null; }

        /// <summary>The id of a key (0: none such).</summary>
        public static ushort IdOf(string key)
        {
            Load();
            for (int i = 1; i < keys.Length; i++)
                if (keys[i] == key)
                    return (ushort)i;
            return 0;
        }

        public static BlobAssetReference<ItemBlob> Blob
        {
            get
            {
                Load();
                if (!blob.IsCreated)
                {
                    blob = Build(defs, hash);
                    Application.quitting += () => { if (blob.IsCreated) blob.Dispose(); };
                }
                return blob;
            }
        }

        static void Load()
        {
            if (defs != null)
                return;
            ids = Resources.Load<ItemIds>(IdsPath);
            var n = ids != null ? ids.Entries.Count : 1;
            defs = new ItemDef[n];
            keys = new string[n];
            keys[0] = ItemIds.NoneKey;
            for (int i = 1; i < n; i++)
            {
                var e = ids.Entries[i];
                keys[i] = e.Key;
                if (!e.Retired && e.Item != null)
                    defs[i] = e.Item.ToDef();
            }
            hash = KeyHash(keys);
        }

        /// <summary>Drops what was loaded (the generator, after writing the assets).</summary>
        public static void Reload()
        {
            defs = null;
            if (blob.IsCreated)
                blob.Dispose();
            blob = default;
        }

        static BlobAssetReference<ItemBlob> Build(ItemDef[] from, uint keysHash)
        {
            using var b = new BlobBuilder(Allocator.Temp);
            ref var root = ref b.ConstructRoot<ItemBlob>();
            root.Hash = keysHash;
            var rows = b.Allocate(ref root.Rows, from.Length);
            for (int i = 0; i < from.Length; i++)
            {
                var d = from[i];
                rows[i] = new ItemBlobRow
                {
                    Colour = new float4(d.Colour.r, d.Colour.g, d.Colour.b, d.Colour.a),
                    Accent = new float4(d.Accent.r, d.Accent.g, d.Accent.b, d.Accent.a),
                    Stack = d.Stack, Slots = d.Slots, Weight = d.Weight, Light = d.Light, Reach = d.Reach, Cover = d.Cover,
                    Damage = d.Damage, Speed = d.Speed, Armour = d.Armour, Length = d.Length, Slot = d.Slot, Sheath = d.Sheath,
                    Family = d.Family, AccentFamily = d.AccentFamily, Weapon = d.Weapon, Look = d.Look, Kind = d.Kind,
                    AccentKind = d.AccentKind, TwoHanded = d.TwoHanded, Dyed = d.Dyed,
                };
            }
            return b.CreateBlobAssetReference<ItemBlob>(Allocator.Persistent);
        }

        /// <summary>FNV-1a over each key's UTF-16 chars, with a 0 between keys.</summary>
        public static uint KeyHash(string[] k)
        {
            var h = 2166136261u;
            foreach (var key in k)
            {
                if (key != null)
                    foreach (var c in key)
                        h = (h ^ c) * 16777619u;
                h = (h ^ 0u) * 16777619u;
            }
            return h;
        }

        /// <summary>
        /// A fingerprint of an item table: every field of every row, floats by their bits, in order (FNV-1a 64). The
        /// catalog from row First on must give the same as ItemTable.Items (the "nothing changes" proof).
        /// </summary>
        public static ulong Fingerprint(ItemDef[] table, int first = 0)
        {
            var h = 14695981039346656037ul;
            void Add(uint v) { for (int s = 0; s < 32; s += 8) h = (h ^ ((v >> s) & 0xff)) * 1099511628211ul; }
            void F(float v) => Add(math.asuint(v));
            void C(Color c) { F(c.r); F(c.g); F(c.b); F(c.a); }
            for (int i = first; i < table.Length; i++)
            {
                var d = table[i];
                if (d.Name != null)
                    foreach (var ch in d.Name)
                        Add(ch);
                Add(0xffffffffu);
                C(d.Colour); C(d.Accent);
                Add((uint)d.Stack); F(d.Weight); Add((uint)d.Slot); F(d.Light); F(d.Reach); F(d.Cover); Add((uint)d.Slots);
                F(d.Damage); F(d.Speed); Add(d.TwoHanded ? 1u : 0u); Add(d.Weapon); F(d.Armour); Add(d.Look); F(d.Length);
                Add((uint)d.Sheath); Add(d.Dyed ? 1u : 0u); Add((uint)d.Family); Add((uint)d.AccentFamily); Add(d.Kind);
                Add(d.AccentKind);
            }
            return h;
        }

        /// <summary>At start: whether the catalog (from id 1) is still today's ItemTable. Logged as an error if not.</summary>
        public static void Check()
        {
            Load();
            if (ids == null)
            {
                Debug.LogError("Items: no item catalog (Resources/" + IdsPath + ")");
                return;
            }
            var table = Fingerprint(ItemTable.Items);
            var catalog = Fingerprint(defs, 1);
            if (defs.Length - 1 != ItemTable.Items.Length || table != catalog)
                Debug.LogError($"Items: the item catalog ({defs.Length - 1} items, {catalog:x16}) differs from ItemTable " +
                               $"({ItemTable.Items.Length}, {table:x16}): run Punchy/Items/Generate catalog");
            else
                Debug.Log($"Items: catalog matches ItemTable ({ItemTable.Items.Length} items, fingerprint {table:x16}, keys {hash:x8})");
        }
    }
}
