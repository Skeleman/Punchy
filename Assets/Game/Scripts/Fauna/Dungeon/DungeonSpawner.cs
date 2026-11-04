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

        const ushort AnyType = 0xFFFF;
        const ulong AnyBiome = ulong.MaxValue;

        /// <summary>
        /// One weighted table row: content that may fill a socket of this kind, from MinSize to MaxSize, in the dungeon
        /// types of TypeMask (bit per type) and biomes of BiomeMask (bit per biome index; 64 and up count as bit 63).
        /// </summary>
        public struct Row
        {
            public SocketKind Kind;
            public byte MinSize, MaxSize;
            public ushort TypeMask;
            public ulong BiomeMask;
            public ushort Weight;
            public DungeonContent Content;
        }

        static Row R(SocketKind kind, DungeonContent content, ushort weight, ushort types = AnyType, byte min = 0, byte max = 2,
                     ulong biomes = AnyBiome) =>
            new Row { Kind = kind, Content = content, Weight = weight, TypeMask = types, MinSize = min, MaxSize = max, BiomeMask = biomes };

        static ushort T(params ushort[] types)
        {
            ushort m = 0;
            foreach (var t in types)
                m |= (ushort)(1 << t);
            return m;
        }

        /// <summary>The tables, all kinds in one list (each pick filters by its socket). Tuning is weights only.</summary>
        public static readonly Row[] Rows =
        {
            // Creatures: cave life everywhere, the dead in crypts, goblins in ruins and mines, big animals in big sockets.
            R(SocketKind.Creature, DungeonContent.Empty, 30),
            // The sunken temple is a damp ruin: salamanders and moths, spiders, ghosts, a few goblin squatters.
            R(SocketKind.Creature, DungeonContent.CaveSalamander, 30, T(Cave, Temple, Mine)),
            R(SocketKind.Creature, DungeonContent.Bat, 25, T(Cave, Temple, Ruin, Mine)),
            R(SocketKind.Creature, DungeonContent.GlowMoth, 15, T(Cave, Temple, Ruin)),
            R(SocketKind.Creature, DungeonContent.SilverFish, 15, T(Temple, Crypt, Ruin, Mine), max: 0),
            R(SocketKind.Creature, DungeonContent.Tarantula, 15, T(Cave, Temple, Ruin), max: 1),
            R(SocketKind.Creature, DungeonContent.GiantSpider, 12, T(Cave, Temple, Crypt), 1),
            R(SocketKind.Creature, DungeonContent.BoneLizard, 20, T(Crypt)),
            R(SocketKind.Creature, DungeonContent.BoneHound, 15, T(Crypt), 1),
            R(SocketKind.Creature, DungeonContent.Ghost, 20, T(Temple, Crypt, Ruin), 1),
            R(SocketKind.Creature, DungeonContent.Goblin, 25, T(Ruin, Mine), 1),
            R(SocketKind.Creature, DungeonContent.Goblin, 10, T(Temple), 1),
            R(SocketKind.Creature, DungeonContent.Wolf, 10, T(Cave, Ruin), 1),
            R(SocketKind.Creature, DungeonContent.BlackBear, 8, T(Cave), 2),
            // Chests: big chests need a big socket; crypts and temples hold coffers.
            R(SocketKind.Chest, DungeonContent.Empty, 15),
            R(SocketKind.Chest, DungeonContent.ChestSmall, 40),
            R(SocketKind.Chest, DungeonContent.ChestLarge, 20, AnyType, 1),
            R(SocketKind.Chest, DungeonContent.Coffer, 35, T(Temple, Crypt, Ruin)),
            // Wall decoration
            R(SocketKind.WallDecor, DungeonContent.Empty, 20),
            R(SocketKind.WallDecor, DungeonContent.Sconce, 30, T(Crypt, Ruin)),
            R(SocketKind.WallDecor, DungeonContent.Banner, 20, T(Temple, Ruin), 1),
            R(SocketKind.WallDecor, DungeonContent.SkullNiche, 30, T(Temple, Crypt)),
            R(SocketKind.WallDecor, DungeonContent.MineLamp, 35, T(Mine)),
            R(SocketKind.WallDecor, DungeonContent.Cobweb, 25, T(Cave, Temple, Crypt, Ruin)),
            // Interactables (the temple's own torches are interactable sockets too: they keep their sconce)
            R(SocketKind.Interactable, DungeonContent.Empty, 10),
            R(SocketKind.Interactable, DungeonContent.Lever, 30, T(Temple, Crypt, Ruin, Mine)),
            R(SocketKind.Interactable, DungeonContent.Altar, 25, T(Temple, Crypt, Ruin), 1),
            R(SocketKind.Interactable, DungeonContent.OreCart, 35, T(Mine), 1),
            R(SocketKind.Interactable, DungeonContent.Well, 15, T(Cave, Ruin), 1),
            // Clutter
            R(SocketKind.Clutter, DungeonContent.Empty, 25),
            R(SocketKind.Clutter, DungeonContent.Bones, 25, T(Cave, Temple, Crypt)),
            R(SocketKind.Clutter, DungeonContent.Urn, 25, T(Temple, Crypt, Ruin)),
            R(SocketKind.Clutter, DungeonContent.Barrel, 20, T(Ruin, Mine)),
            R(SocketKind.Clutter, DungeonContent.Crate, 20, T(Mine, Ruin)),
            R(SocketKind.Clutter, DungeonContent.RockPile, 30, T(Cave, Temple, Mine)),
            R(SocketKind.Clutter, DungeonContent.Cobweb, 15, T(Cave, Temple, Crypt), max: 1),
        };

        /// <summary>
        /// Avalanches a seed (math.hash on a lone uint does not, so neighbouring socket seeds would draw alike).
        /// </summary>
        public static uint Scramble(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x;
        }

        /// <summary>The one seeded pick for a socket: a weighted draw over the rows that fit it.</summary>
        public static DungeonContent Pick(in DungeonSocket s)
        {
            var typeBit = s.DungeonType < 16 ? (ushort)(1 << s.DungeonType) : (ushort)0;
            var biomeBit = 1ul << math.min((int)s.Biome, 63);
            int total = 0;
            for (int i = 0; i < Rows.Length; i++)
                if (Fits(Rows[i], s, typeBit, biomeBit))
                    total += Rows[i].Weight;
            if (total == 0)
                return DungeonContent.Empty;
            var roll = (int)(Scramble(s.Seed ^ 0x5bd1e995u) % (uint)total);
            for (int i = 0; i < Rows.Length; i++)
                if (Fits(Rows[i], s, typeBit, biomeBit) && (roll -= Rows[i].Weight) < 0)
                    return Rows[i].Content;
            return DungeonContent.Empty;
        }

        static bool Fits(in Row r, in DungeonSocket s, ushort typeBit, ulong biomeBit) =>
            r.Kind == s.Kind && s.Size >= r.MinSize && s.Size <= r.MaxSize && (r.TypeMask & typeBit) != 0 && (r.BiomeMask & biomeBit) != 0;

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

        // ItemTable ids (player lane, Punchy.Items.ItemTable): Stone 1, Mushroom 3, Coin 6, Key 7.
        static readonly (byte item, ushort weight, byte maxCount)[] LootRows = { (6, 45, 5), (3, 25, 3), (1, 25, 2), (7, 5, 1) };

        /// <summary>
        /// Loose items for a dungeon (#24): 3-6 of its empty floor sockets (Creature or Clutter sockets that picked
        /// nothing, so no prop or chest stands there), chosen and filled from the dungeon's seed (all its socket seeds
        /// folded), so every machine gets the same list and no two dungeons lay theirs out alike. Coins most, then
        /// mushrooms and stones, a key rarely. Returns the dungeon seed.
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
            int total = 0;
            foreach (var row in LootRows)
                total += row.weight;
            for (int k = 0; k < n; k++)
            {
                var r = Scramble(spots[k].key ^ 0xC0FFEEu);
                var roll = (int)(r % (uint)total);
                var pick = LootRows[0];
                foreach (var row in LootRows)
                    if ((roll -= row.weight) < 0)
                    {
                        pick = row;
                        break;
                    }
                var r2 = Scramble(r);
                // (A little off the socket's centre, so the loot doesn't sit on a grid.)
                var jitter = new float3(((r2 & 255) / 255f - 0.5f) * 0.7f, 0f, (((r2 >> 8) & 255) / 255f - 0.5f) * 0.7f);
                into.Add(new Loot
                {
                    Item = pick.item,
                    Count = (byte)(1 + (r2 >> 16) % pick.maxCount),
                    Socket = spots[k].i,
                    Pos = sockets[spots[k].i].Pos + jitter,
                });
            }
            return seed;
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
                if (shows)
                {
                    using var items = sockets.ToNativeArray(Allocator.Temp);
                    for (int i = 0; i < items.Length; i++)
                        if (picks[i] != DungeonContent.Empty && !DungeonSpawner.IsCreature(picks[i]) &&
                            MakeProp(items[i], picks[i]) is { } prop)
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

        /// <summary>
        /// Makes the creature picks (already in <see cref="picks"/>) at their sockets, each with its post, and marks the
        /// dungeon filled in this director build. Returns how many were made.
        /// </summary>
        int MakeCreatures(Entity d)
        {
            var made = new List<(Entity e, DungeonPost post)>();
            // (A copy: spawning a creature is a structural change, which invalidates the buffer.)
            using var copy = EntityManager.GetBuffer<DungeonSocket>(d, true).ToNativeArray(Allocator.Temp);
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
                made.Add((e, DungeonPost.From(s, c)));
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
        static GameObject MakeProp(in DungeonSocket s, DungeonContent c)
        {
            if (s.Kind == SocketKind.Chest)
                return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Dungeon {c}";
            Object.Destroy(go.GetComponent<Collider>());
            var size = s.Kind switch
            {
                SocketKind.Chest => new Vector3(0.9f, 0.6f, 0.6f),
                SocketKind.WallDecor => new Vector3(0.4f, 0.6f, 0.12f),
                SocketKind.Interactable => new Vector3(0.6f, 1f, 0.6f),
                _ => new Vector3(0.4f, 0.4f, 0.4f),
            } * (1f + 0.35f * s.Size);
            go.transform.SetPositionAndRotation((Vector3)s.Pos + Vector3.up * size.y * 0.5f, s.Rot);
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
            if (wall)
                go.transform.position -= go.transform.forward * size.z * 0.5f;
            r.SetPropertyBlock(propBlock);
            return go;
        }
    }
}
