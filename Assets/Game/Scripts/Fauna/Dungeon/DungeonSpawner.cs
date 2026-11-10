using System.Collections.Generic;
using Punchy.Dungeon;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// What the dungeon spawner can put in a socket (#24): creatures (by species asset name) and items. Items are
    /// placeholder boxes for now, sized and tinted per kind, until the kit has dungeon props. Empty is a real pick: not
    /// every socket is filled. New entries go at the end; never renumber (pick hashes are built from these numbers).
    /// </summary>
    public enum DungeonContent : byte
    {
        Empty,
        // Creatures
        CaveSalamander, Bat, GiantSpider, Tarantula, BoneHound, Ghost, GlowMoth, SilverFish, BlackBear, Goblin, BoneLizard, Wolf,
        // Chests
        ChestSmall, ChestLarge, Coffer,
        // Wall decoration
        Sconce, Banner, SkullNiche, MineLamp,
        // Interactables
        Lever, Altar, OreCart, Well,
        // Clutter
        Bones, Urn, Barrel, Crate, RockPile, Cobweb,
    }

    /// <summary>
    /// The dungeon spawner's tables and its one seeded pick per socket (#24, briefs/dungeons-two-phase.md). The set
    /// pieces (player lane: TempleSockets for the sunken temple) write <see cref="DungeonSocket"/>s and never pick
    /// content; this only reads them. A pick
    /// depends on nothing but the socket (its seed, kind, size, dungeon type and biome), so host, clients and solo all
    /// make the same picks without sending anything.
    /// </summary>
    public static class DungeonSpawner
    {
        /// <summary>
        /// Dungeon types the tables know so far, as numbers in <see cref="DungeonSocket.DungeonType"/>. The set pieces
        /// own the numbers (<see cref="DungeonTypes"/>); the rest are spare until a set piece claims them.
        /// </summary>
        public const ushort Cave = 0, Temple = DungeonTypes.SunkenTemple, Crypt = 2, Ruin = 3, Mine = 4;

        /// <summary>Every type the tables know, for the log's type comparison.</summary>
        public static readonly ushort[] Types = { Cave, Temple, Crypt, Ruin, Mine };

        /// <summary>
        /// The tables (A-80): Resources/SpawnTables/Dungeon, a <see cref="SpawnTable"/> whose rows name a
        /// <see cref="DungeonContent"/>, take a <see cref="SocketKind"/> as their kind and a bit per type above as their
        /// types. Tuning is weights only; row order is part of the seed (moved from code rows in the same order, so
        /// every pick is unchanged).
        /// </summary>
        public static SpawnTable Table => table != null ? table : table = SpawnTable.Load("Dungeon");
        static SpawnTable table;
        static DungeonContent[] contentOf;

        /// <summary>The content each table row names (parsed once; an unknown name is Empty and logged).</summary>
        static DungeonContent ContentOf(int row)
        {
            if (contentOf == null || contentOf.Length != Table.Rows.Count)
            {
                contentOf = new DungeonContent[Table.Rows.Count];
                for (int i = 0; i < contentOf.Length; i++)
                    if (!System.Enum.TryParse(Table.Rows[i].Content, out contentOf[i]))
                        Debug.LogError($"[dungeon] spawn table row {i}: no content '{Table.Rows[i].Content}'");
            }
            return contentOf[row];
        }

        /// <summary>A dungeon socket as the table's question (dungeons are timeless and seeded: every time fits).</summary>
        public static SpawnSocket AsSpawnSocket(in DungeonSocket s) =>
            SpawnSocket.Of((int)s.Kind, s.Seed, s.Size, s.DungeonType, s.Tags, s.Biome);

        /// <summary>
        /// Avalanches a seed (math.hash on a lone uint does not, so neighbouring socket seeds would draw alike).
        /// </summary>
        public static uint Scramble(uint x) => SpawnTable.Scramble(x);

        /// <summary>The one seeded pick for a socket: a weighted draw over the table rows that fit it.</summary>
        public static DungeonContent Pick(in DungeonSocket s)
        {
            if (Table == null)
                return DungeonContent.Empty;
            var row = Table.PickRow(AsSpawnSocket(s));
            return row < 0 ? DungeonContent.Empty : ContentOf(row);
        }

        public static bool IsCreature(DungeonContent c) => c >= DungeonContent.CaveSalamander && c <= DungeonContent.Wolf;

        /// <summary>Picks for a whole dungeon, with a hash of (socket index, content) and counts per socket kind.</summary>
        public static uint PickAll(DynamicBuffer<DungeonSocket> sockets, List<DungeonContent> into, int[] counts)
        {
            into.Clear();
            System.Array.Clear(counts, 0, counts.Length);
            uint hash = 0x811c9dc5u;
            for (int i = 0; i < sockets.Length; i++)
            {
                var c = Pick(sockets[i]);
                into.Add(c);
                if (c != DungeonContent.Empty)
                    counts[(int)sockets[i].Kind]++;
                hash = Scramble(hash ^ ((uint)i << 8 | (uint)c));
            }
            return hash;
        }

        /// <summary>One loose item on a dungeon floor: an ItemTable id, a count and where it lies.</summary>
        public struct Loot
        {
            public byte Item, Count;
            public int Socket;
            public float3 Pos;
        }

        /// <summary>
        /// A-91 row 4 (the P-23 hook): what dungeons hand out, as data. Resources/SpawnTables/DungeonLoot, a
        /// <see cref="SpawnTable"/> whose rows name an item by its ItemTable name (Coin, Key, and P-23's gear when it
        /// lands) with a weight and a count range. A row with Kind -1 is loose loot on the floor (<see cref="PickLoot"/>);
        /// a row with Kind 1 (SocketKind.Chest) is for chests (<see cref="PickChestLoot"/>). Types, tags and biomes filter
        /// as in any table. A row naming an item the table doesn't have yet is skipped (so gear rows can go in first).
        /// The first four rows (Coin 45, Mushroom 25, Stone 25, Key 5) draw exactly as the old code rows did.
        /// </summary>
        public static SpawnTable LootTable => lootTable != null ? lootTable : lootTable = SpawnTable.Load("DungeonLoot");
        static SpawnTable lootTable;
        static int[] lootItems;

        /// <summary>The ItemTable id a loot row names, or -1 (looked up by name once; ids are append-only).</summary>
        static int LootItem(int row)
        {
            var rows = LootTable.Rows;
            if (lootItems == null || lootItems.Length != rows.Count)
            {
                lootItems = new int[rows.Count];
                for (int i = 0; i < rows.Count; i++)
                {
                    lootItems[i] = -1;
                    for (int k = 0; k < Punchy.Items.ItemTable.Items.Length; k++)
                        if (string.Equals(Punchy.Items.ItemTable.Items[k].Name, rows[i].Content, System.StringComparison.OrdinalIgnoreCase))
                            lootItems[i] = k;
                    if (lootItems[i] < 0)
                        Debug.Log($"[dungeon] loot table row {i}: no item '{rows[i].Content}' yet (skipped)");
                }
            }
            return lootItems[row];
        }

        /// <summary>
        /// One weighted draw over the loot rows that fit a socket (and name an item), from a roll: the item and how many
        /// (from the row's count range and a second roll). False when no row fits.
        /// </summary>
        static bool DrawLoot(in DungeonSocket s, bool chest, uint roll, uint countRoll, out byte item, out byte count)
        {
            item = count = 0;
            var table = LootTable;
            if (table == null)
                return false;
            var q = AsSpawnSocket(s);
            int total = 0;
            for (int i = 0; i < table.Rows.Count; i++)
                if ((table.Rows[i].Kind >= 0) == chest && SpawnTable.Fits(table.Rows[i], q) && LootItem(i) >= 0)
                    total += table.Rows[i].Weight;
            if (total <= 0)
                return false;
            var left = (int)(roll % (uint)total);
            for (int i = 0; i < table.Rows.Count; i++)
                if ((table.Rows[i].Kind >= 0) == chest && SpawnTable.Fits(table.Rows[i], q) && LootItem(i) >= 0 &&
                    (left -= table.Rows[i].Weight) < 0)
                {
                    var c = table.Rows[i].Count;
                    item = (byte)LootItem(i);
                    count = (byte)(c.y <= c.x ? math.max(1, c.x) : c.x + countRoll % (uint)(c.y - c.x + 1));
                    return count > 0;
                }
            return false;
        }

        /// <summary>
        /// Loose items for a dungeon (#24): 3-6 of its empty floor sockets (Creature or Clutter sockets that picked
        /// nothing, so no prop or chest stands there), chosen and filled from the dungeon's seed (all its socket seeds
        /// folded), so every machine gets the same list and no two dungeons lay theirs out alike. What lies there comes
        /// from the loose rows of <see cref="LootTable"/>. Returns the dungeon seed.
        /// </summary>
        public static uint PickLoot(DynamicBuffer<DungeonSocket> sockets, List<DungeonContent> picks, List<Loot> into)
        {
            into.Clear();
            uint seed = 0x9E3779B9u;
            for (int i = 0; i < sockets.Length; i++)
                seed = Scramble(seed ^ sockets[i].Seed);
            var spots = new List<(uint key, int i)>();
            for (int i = 0; i < sockets.Length && i < picks.Count; i++)
                if ((sockets[i].Kind == SocketKind.Creature || sockets[i].Kind == SocketKind.Clutter) && picks[i] == DungeonContent.Empty)
                    spots.Add((Scramble(sockets[i].Seed ^ seed ^ 0x10075EEDu), i));
            spots.Sort((a, b) => a.key.CompareTo(b.key));
            var n = math.min(spots.Count, 3 + (int)(seed % 4));
            for (int k = 0; k < n; k++)
            {
                var r = Scramble(spots[k].key ^ 0xC0FFEEu);
                var r2 = Scramble(r);
                if (!DrawLoot(sockets[spots[k].i], false, r, r2 >> 16, out var item, out var count))
                    continue;
                // (A little off the socket's centre, so the loot doesn't sit on a grid.)
                var jitter = new float3(((r2 & 255) / 255f - 0.5f) * 0.7f, 0f, (((r2 >> 8) & 255) / 255f - 0.5f) * 0.7f);
                into.Add(new Loot { Item = item, Count = count, Socket = spots[k].i, Pos = sockets[spots[k].i].Pos + jitter });
            }
            return seed;
        }

        /// <summary>
        /// A-91 row 4, the P-23 hook for chests: an extra item for a chest socket from the chest rows of
        /// <see cref="LootTable"/> (P-23's gear goes there), seeded by the socket alone, so host and clients agree.
        /// False while the table has no chest row that fits (today: none). Items (player lane) calls it when it fills a
        /// chest; the spawner never opens chests.
        /// </summary>
        public static bool PickChestLoot(in DungeonSocket chest, out byte item, out byte count)
        {
            var r = Scramble(chest.Seed ^ 0xC4E57u);
            return DrawLoot(chest, true, r, Scramble(r), out item, out count);
        }

        /// <summary>
        /// The same sockets picked as if they were another dungeon type: the pick hash and how many sockets get
        /// something. Only for the log, to show the tables differ by type; the real picks use the socket's own type.
        /// </summary>
        public static uint PickAllAs(DynamicBuffer<DungeonSocket> sockets, ushort type, out int filled)
        {
            uint hash = 0x811c9dc5u;
            filled = 0;
            for (int i = 0; i < sockets.Length; i++)
            {
                var s = sockets[i];
                s.DungeonType = type;
                var c = Pick(s);
                if (c != DungeonContent.Empty)
                    filled++;
                hash = Scramble(hash ^ ((uint)i << 8 | (uint)c));
            }
            return hash;
        }

        /// <summary>
        /// A-95 (#24): where a socket's thing stands, as a box (centre, size, facing), for its stand-in prop, its
        /// collision box and the dungeon walk. A chest is the player lane's chest (Items) over its socket. The socket
        /// itself never moves (its position is in the set pieces' hashes); only the box is nudged: off a chest it
        /// shares a corner with (to the side, so it shows: Items hides stand-ins on a chest), then out of a wall or
        /// rock along the socket's facing, and failing that the other ways round, up to <see cref="MostNudge"/>.
        /// False for wall decor (set into the wall: nothing to bump into) and when no clear spot is found.
        /// </summary>
        public static bool PropPlace(in DungeonSocket s, in NativeArray<DungeonSocket> all, in Punchy.Terrain.TerrainSource terrain,
                                     out float3 centre, out float3 size, out quaternion rot)
        {
            rot = s.Rot;
            size = s.Kind switch
            {
                SocketKind.Chest => new float3(0.82f, 0.56f, 0.52f),
                SocketKind.WallDecor => new float3(0.4f, 0.6f, 0.12f),
                SocketKind.Interactable => new float3(0.6f, 1f, 0.6f),
                _ => new float3(0.4f, 0.4f, 0.4f),
            } * (s.Kind == SocketKind.Chest ? 1f : 1f + 0.35f * s.Size);
            var fwd = math.mul(s.Rot, new float3(0f, 0f, 1f));
            centre = s.Pos + new float3(0f, size.y * 0.5f, 0f);
            if (s.Kind == SocketKind.WallDecor)
            {
                centre -= fwd * size.z * 0.5f;
                return false;
            }
            if (s.Kind == SocketKind.Chest)
                return true;
            // Off a chest: beside it, its own half width plus the chest's (and a hand's breadth) from the chest's middle.
            foreach (var o in all)
            {
                if (o.Kind != SocketKind.Chest || math.distance(o.Pos.xz, s.Pos.xz) > 0.9f || math.abs(o.Pos.y - s.Pos.y) > 1f)
                    continue;
                var side = math.mul(o.Rot, new float3(1f, 0f, 0f));
                var away = s.Pos.xz - o.Pos.xz;
                var sign = math.dot(away, side.xz) < 0f ? -1f : 1f;
                var gap = 0.41f + size.x * 0.5f + 0.1f;
                var a = o.Pos + side * sign * gap;
                var b = o.Pos - side * sign * gap;
                centre.xz = (Clear(terrain, new float3(a.x, centre.y, a.z), size, rot) ? a : b).xz;
                break;
            }
            if (Clear(terrain, centre, size, rot))
                return true;
            // Out of a wall: the facing first (a sconce's faces the room), then the other three ways and the diagonals.
            var right = math.mul(s.Rot, new float3(1f, 0f, 0f));
            for (float d = 0.1f; d <= MostNudge + 1e-3f; d += 0.1f)
                for (int k = 0; k < 8; k++)
                {
                    var dir = k switch { 0 => fwd, 1 => right, 2 => -right, 3 => -fwd, 4 => fwd + right, 5 => fwd - right, 6 => -fwd + right, _ => -fwd - right };
                    var at = centre + math.normalize(dir) * d;
                    if (Clear(terrain, at, size, rot))
                    {
                        centre = at;
                        return true;
                    }
                }
            return false;
        }

        /// <summary>How far a stand-in's box may be nudged out of a wall (m).</summary>
        public const float MostNudge = 0.8f;

        /// <summary>Nothing solid (rock, a temple wall) at a box's middle or its eight upper and lower corners (a hair in).</summary>
        static bool Clear(in Punchy.Terrain.TerrainSource terrain, float3 centre, float3 size, quaternion rot)
        {
            if (!terrain.Landmarks.IsCreated)
                return true;
            if (Punchy.Player.PlayerMovementSystem.SolidDistance(terrain, centre) <= 0f)
                return false;
            for (int c = 0; c < 8; c++)
            {
                var corner = new float3((c & 1) == 0 ? -0.45f : 0.45f, (c & 2) == 0 ? -0.4f : 0.45f, (c & 4) == 0 ? -0.45f : 0.45f) * size;
                if (Punchy.Player.PlayerMovementSystem.SolidDistance(terrain, centre + math.mul(rot, corner)) <= 0f)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A-95 (#24): a dungeon stand-in's or chest's box (<see cref="DungeonSpawner.PropPlace"/>), solid to the player as a
    /// kit box is (KitCollision, read as KitBox through Reinterpret). Seed-derived: every world that fills the dungeon
    /// makes the same boxes, on one entity per world.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct DungeonPropBox : IBufferElementData
    {
        public Punchy.Flora.KitBox Box;
    }

    /// <summary>On a dungeon entity once the spawner has filled its sockets (each world fills its own once).</summary>
    public struct DungeonFilled : IComponentData
    {
        /// <summary>The director build its creatures were made in (0: none made in this world).</summary>
        public int Builds;
    }

    /// <summary>
    /// Fills dungeons (#24): for each entity with a <see cref="DungeonSocket"/> buffer, picks content per socket, logs
    /// the pick hash, then makes the creatures (in the world animals live in: solo or the host's server) and the
    /// placeholder items (in the presentation world, so a joined client sees them too). Picks are seed-derived state:
    /// each machine makes them itself. Runs once per dungeon, so it costs nothing per frame after that.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DungeonSpawnSystem : SystemBase
    {
        readonly List<DungeonContent> picks = new();
        readonly int[] counts = new int[5];
        readonly List<GameObject> props = new();
        readonly List<DungeonSpawner.Loot> loot = new();
        FaunaDirector director;
        EntityQuery fresh, filled;

        protected override void OnCreate()
        {
            fresh = new EntityQueryBuilder(Allocator.Temp).WithAll<DungeonSocket>().WithNone<DungeonFilled>().Build(this);
            filled = new EntityQueryBuilder(Allocator.Temp).WithAll<DungeonSocket, DungeonFilled>().Build(this);
            RequireAnyForUpdate(fresh, filled);
        }

        protected override void OnDestroy()
        {
            foreach (var p in props)
                if (p != null)
                    Object.Destroy(p);
            props.Clear();
        }

        protected override void OnUpdate()
        {
            if (director == null)
                director = Object.FindFirstObjectByType<FaunaDirector>();
            // Animals are only made where they live; wait for the director to know its world.
            var animals = director != null && director.SimWorld == World;
            var shows = World == World.DefaultGameObjectInjectionWorld;
            if (!animals && !World.IsClient())
                return;
            // A director rebuild (terrain re-baked) destroys every animal: make the creatures again, not the props.
            if (animals && !filled.IsEmpty)
            {
                using var done = filled.ToEntityArray(Allocator.Temp);
                foreach (var d in done)
                {
                    if (EntityManager.GetComponentData<DungeonFilled>(d).Builds == director.Builds)
                        continue;
                    DungeonSpawner.PickAll(EntityManager.GetBuffer<DungeonSocket>(d, true), picks, counts);
                    var made = MakeCreatures(d);
                    Debug.Log($"[dungeon] {World.Name} dungeon {d.Index}: animals rebuilt, {made} creatures made again at their posts");
                }
            }
            using var dungeons = fresh.ToEntityArray(Allocator.Temp);
            foreach (var d in dungeons)
            {
                var sockets = EntityManager.GetBuffer<DungeonSocket>(d, true);
                var hash = DungeonSpawner.PickAll(sockets, picks, counts);
                // Picked twice to show a pick depends on the socket alone, then as every other type, to show the
                // tables differ by type.
                var again = DungeonSpawner.PickAll(sockets, new List<DungeonContent>(), new int[5]);
                var type = sockets.Length > 0 ? sockets[0].DungeonType : (ushort)0;
                var asTypes = new System.Text.StringBuilder();
                foreach (var t in DungeonSpawner.Types)
                    if (t != type)
                        asTypes.Append($" {t}:0x{DungeonSpawner.PickAllAs(sockets, t, out var filled):x8}/{filled}");
                Debug.Log($"[dungeon] {World.Name} dungeon {d.Index} type {type}: {sockets.Length} sockets, creatures {counts[0]}, " +
                          $"chests {counts[1]}, wall {counts[2]}, interactables {counts[3]}, clutter {counts[4]}, hash 0x{hash:x8}" +
                          $" (again 0x{again:x8}{(again == hash ? ", same" : ", DIFFERS")}; as other types{asTypes})" +
                          $" ({(animals ? "spawns animals" : "no animals here")}{(shows ? ", shows props" : "")}): " +
                          string.Join(" ", picks.FindAll(c => c != DungeonContent.Empty)));
                SystemAPI.TryGetSingleton<Punchy.Terrain.TerrainSource>(out var terrain);
                AddBoxes(sockets.ToNativeArray(Allocator.Temp), terrain);
                if (shows)
                {
                    using var items = sockets.ToNativeArray(Allocator.Temp);
                    for (int i = 0; i < items.Length; i++)
                        if (picks[i] != DungeonContent.Empty && !DungeonSpawner.IsCreature(picks[i]) &&
                            MakeProp(items[i], picks[i], items, terrain) is { } prop)
                            props.Add(prop);
                    // Loose loot: the host or solo puts it in the world for everyone (Items.Spawn is ignored on a
                    // joined client, which is sent the host's items instead).
                    var lootSeed = DungeonSpawner.PickLoot(sockets, picks, loot);
                    var list = new System.Text.StringBuilder();
                    int spawned = 0;
                    foreach (var l in loot)
                    {
                        if (Punchy.Items.Items.Spawn(l.Item, l.Count, l.Pos) >= 0)
                            spawned++;
                        list.Append($" {Punchy.Items.ItemTable.Items[l.Item].Name}x{l.Count}@{l.Socket}({l.Pos.x:F1},{l.Pos.y:F1},{l.Pos.z:F1})");
                    }
                    Debug.Log($"[dungeon] loot: dungeon {d.Index} type {type} seed 0x{lootSeed:x8}: {loot.Count} items, {spawned} spawned here:{list}");
                }
                if (animals)
                    MakeCreatures(d);
                else
                    EntityManager.AddComponentData(d, new DungeonFilled());
            }
        }

        readonly HashSet<int> boxed = new();
        Entity boxEntity;

        /// <summary>
        /// A-95 (#24): the dungeon's chests and stand-ins as boxes the player bumps into and stands on (DungeonPropBox,
        /// one entity per world), placed as the props are. Once per dungeon (by its first socket's spot: a re-baked
        /// terrain makes the same dungeons again). Wall decor is in the wall and gets none.
        /// </summary>
        void AddBoxes(NativeArray<DungeonSocket> sockets, in Punchy.Terrain.TerrainSource terrain)
        {
            using (sockets)
            {
                if (sockets.Length == 0 || !terrain.Landmarks.IsCreated || !boxed.Add((int)math.hash(sockets[0].Pos)))
                    return;
                if (boxEntity == Entity.Null || !EntityManager.Exists(boxEntity))
                {
                    boxEntity = EntityManager.CreateEntity(typeof(DungeonPropBox));
                    EntityManager.SetName(boxEntity, "Dungeon prop boxes");
                }
                var boxes = EntityManager.GetBuffer<DungeonPropBox>(boxEntity);
                for (int i = 0; i < sockets.Length; i++)
                {
                    var s = sockets[i];
                    var shown = s.Kind == SocketKind.Chest || (picks[i] != DungeonContent.Empty && !DungeonSpawner.IsCreature(picks[i]));
                    if (!shown || !DungeonSpawner.PropPlace(s, sockets, terrain, out var centre, out var size, out var rot))
                        continue;
                    var half = size * 0.5f;
                    boxes.Add(new DungeonPropBox { Box = new Punchy.Flora.KitBox { Centre = centre, Half = half, Rotation = rot, Reach = math.length(half) } });
                }
            }
        }

        /// <summary>
        /// Makes the creature picks (already in <see cref="picks"/>) at their sockets, each with its post, and marks the
        /// dungeon filled in this director build. Returns how many were made.
        /// </summary>
        int MakeCreatures(Entity d)
        {
            var made = new List<(Entity e, DungeonPost post)>();
            // (A copy: spawning a creature is a structural change, which invalidates the buffer.)
            using var copy = EntityManager.GetBuffer<DungeonSocket>(d, true).ToNativeArray(Allocator.Temp);
            var id = EntityManager.HasComponent<DungeonInfo>(d) ? EntityManager.GetComponentData<DungeonInfo>(d).Id : 0u;
            for (int i = 0; i < copy.Length; i++)
            {
                var s = copy[i];
                var c = picks[i];
                if (!DungeonSpawner.IsCreature(c))
                    continue;
                var species = director.SpeciesIndex(c.ToString());
                if (species < 0)
                    continue;
                var fwd = math.mul(s.Rot, new float3(0, 0, 1));
                var e = director.Spawn(species, s.Pos, math.atan2(fwd.x, fwd.z));
                // (Every dungeon creature gets a post, even with nothing to do there: the post is what keeps the
                // director from letting it go.)
                var post = DungeonPost.From(s, c);
                post.Dungeon = id;
                made.Add((e, post));
            }
            // (After the loop: adding components moves entities.)
            foreach (var (e, post) in made)
                if (EntityManager.Exists(e))
                    EntityManager.AddComponentData(e, post);
            if (EntityManager.HasComponent<DungeonFilled>(d))
                EntityManager.SetComponentData(d, new DungeonFilled { Builds = director.Builds });
            else
                EntityManager.AddComponentData(d, new DungeonFilled { Builds = director.Builds });
            return made.Count;
        }

        static Material propMaterial;
        static MaterialPropertyBlock propBlock;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// A placeholder box for an item pick: sized by kind, tinted by content, no collider. Null for a chest socket:
        /// the player lane's Items draws the chest there. Drawn with the items' URP material (Resources/ItemMaterial,
        /// the pipeline's default lit material), tinted through a property block, so it never draws magenta.
        /// </summary>
        static GameObject MakeProp(in DungeonSocket s, DungeonContent c, in NativeArray<DungeonSocket> all, in Punchy.Terrain.TerrainSource terrain)
        {
            if (s.Kind == SocketKind.Chest)
                return null;
            // (A-95: placed as its collision box is, nudged off a chest or out of a wall; see DungeonSpawner.PropPlace.)
            DungeonSpawner.PropPlace(s, all, terrain, out var centre, out var size, out var rot);
            // A-91: the real prop, built from parts in the cave's dressed stone, wood and iron; the tinted box below
            // only when there is no cave material to draw it with.
            if (DungeonProps.Make(c, size) is { } built)
            {
                built.transform.SetPositionAndRotation(centre, rot);
                return built;
            }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Dungeon {c}";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetPositionAndRotation(centre, rot);
            go.transform.localScale = size;
            var h = DungeonSpawner.Scramble((uint)c * 0x9E3779B1u);
            var r = go.GetComponent<Renderer>();
            propMaterial ??= Resources.Load<Material>("ItemMaterial");
            if (propMaterial != null)
                r.sharedMaterial = propMaterial;
            propBlock ??= new MaterialPropertyBlock();
            // A-61: wall decor (skull niches) is a stone-grey stand-in set into the wall, not a bright tinted slab:
            // the saturated purple read as a glitch box when the camera sat beside it (#24, a57-temple-mw).
            bool wall = s.Kind == SocketKind.WallDecor;
            propBlock.SetColor(BaseColorId, Color.HSVToRGB((h & 1023) / 1023f, wall ? 0.12f : 0.45f, wall ? 0.32f : 0.75f));
            r.SetPropertyBlock(propBlock);
            return go;
        }
    }
}
