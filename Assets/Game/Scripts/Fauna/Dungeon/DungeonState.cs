using System.Collections.Generic;
using Punchy.Dungeon;
using Punchy.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>One dungeon as quests see it (A-91 row 4, for P-24's "dungeon" quests). See <see cref="DungeonState"/>.</summary>
    public struct DungeonStatus
    {
        /// <summary>The dungeon's stable id (DungeonInfo.Id: a cave's Key, the temple's landmark index). Save this, not the name.</summary>
        public uint Id;
        /// <summary>DungeonTypes: Cave, SunkenTemple, or Ruin for a cave with a buried ruin room in it.</summary>
        public ushort Type;
        /// <summary>A name to show ("Thornhollow Cave", "the Grimdeep Vaults"), from dialogue.json's dungeon words.</summary>
        public string Name;
        /// <summary>Where you go in (a cave's mouth; the temple's first socket).</summary>
        public float3 Entrance;
        /// <summary>Creatures the spawner picks for it (seeded: the same on every machine), and how many are still at their posts.</summary>
        public int Creatures, CreaturesLeft;
        /// <summary>
        /// Every creature it was given is gone or driven off (more than <see cref="DungeonState.DrivenOff"/> from its post);
        /// true from the start for a dungeon that was given none (its quest is the chest). Only known where animals live
        /// (solo, the host): false on a joined client, which asks the host, and while the animals are being rebuilt.
        /// </summary>
        public bool Cleared;
        /// <summary>The quest's chest: an index into the dungeon's DungeonSocket buffer (-1: no chest socket), and where it is.</summary>
        public int MarkedChest;
        public float3 MarkedChestPos;
    }

    /// <summary>
    /// A-91 row 4 (#24): the read side of dungeons for quests (P-24) and anything else that names one. Each dungeon's
    /// name, type, entrance, marked chest and creature count are seed-derived (worked out once from its sockets, the same
    /// on every machine); whether it is cleared is simulated state, read from the posted creatures where animals live.
    /// Read only: nothing here changes a dungeon. Whether the marked chest has been opened is the chest owner's (Items).
    /// </summary>
    public static class DungeonState
    {
        /// <summary>A posted creature this far from its post (m) counts as driven off.</summary>
        public const float DrivenOff = 30f;

        struct Fixed
        {
            public ushort Type;
            public string Name;
            public float3 Entrance;
            public int Creatures, MarkedChest;
            public float3 MarkedChestPos;
        }

        static readonly Dictionary<uint, Fixed> fixedOf = new();
        static readonly Dictionary<uint, uint> saltOf = new();
        static readonly Dictionary<uint, int> left = new();
        static int leftFrame = -1;
        static readonly List<DungeonContent> picks = new();
        static readonly int[] counts = new int[5];

        /// <summary>Every dungeon in the world (caves, ruins, the temple), into a list; returns how many.</summary>
        public static int All(List<DungeonStatus> into)
        {
            into.Clear();
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return 0;
            var em = w.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
                into.Add(Status(em, e));
            return into.Count;
        }

        /// <summary>One dungeon by its id; false when no dungeon has it (the world changed: a quest's "lost to time").</summary>
        public static bool TryGet(uint id, out DungeonStatus status)
        {
            status = default;
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
                return false;
            var em = w.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>(), ComponentType.ReadOnly<DungeonSocket>());
            using var infos = q.ToComponentDataArray<DungeonInfo>(Allocator.Temp);
            using var entities = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < infos.Length; i++)
                if (infos[i].Id == id)
                {
                    status = Status(em, entities[i]);
                    return true;
                }
            return false;
        }

        static DungeonStatus Status(EntityManager em, Entity e)
        {
            var info = em.GetComponentData<DungeonInfo>(e);
            // (Keyed by id and socket hash: a re-baked island with different sockets works it out again.)
            var key = info.Id ^ DungeonSpawner.Scramble(info.Hash);
            if (!fixedOf.TryGetValue(key, out var f))
                fixedOf[key] = f = Work(em, e, info);
            var status = new DungeonStatus
            {
                Id = info.Id, Type = f.Type, Name = f.Name, Entrance = f.Entrance,
                Creatures = f.Creatures, MarkedChest = f.MarkedChest, MarkedChestPos = f.MarkedChestPos,
            };
            if (Left(out var at))
            {
                status.CreaturesLeft = at.TryGetValue(info.Id, out var n) ? n : 0;
                status.Cleared = status.CreaturesLeft == 0;
            }
            return status;
        }

        /// <summary>The seed-derived part, once per dungeon.</summary>
        static Fixed Work(EntityManager em, Entity e, in DungeonInfo info)
        {
            var sockets = em.GetBuffer<DungeonSocket>(e, true);
            var f = new Fixed { Type = info.Type, MarkedChest = -1 };
            // A cave with a buried ruin room is a ruin to quests (its ruin room's sockets say so).
            if (info.Type == DungeonTypes.Cave)
                for (int i = 0; i < sockets.Length; i++)
                    if (sockets[i].DungeonType == DungeonTypes.Ruin)
                    {
                        f.Type = DungeonTypes.Ruin;
                        break;
                    }
            f.Name = NameOf(info.Id, f.Type, SaltOf(em, info.Id));
            f.Entrance = sockets.Length > 0 ? sockets[0].Pos : float3.zero;
            using var tq = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            if (info.Type == DungeonTypes.Cave && tq.TryGetSingleton<TerrainSource>(out var source))
            {
                if (source.Caves.IsCreated)
                {
                    ref var set = ref source.Caves.Value;
                    for (int c = 0; c < set.Caves.Length; c++)
                        if (set.Caves[c].Key == info.Id)
                            f.Entrance = set.Caves[c].Mouth;
                }
            }
            DungeonSpawner.PickAll(sockets, picks, counts);
            f.Creatures = counts[(int)SocketKind.Creature];
            // The marked chest: one chest socket, the same on every machine. A chest in a destination room or a dead end
            // first (the far end, worth the walk), then by its seed.
            int best = -1;
            uint bestScore = 0;
            for (int i = 0; i < sockets.Length; i++)
            {
                if (sockets[i].Kind != SocketKind.Chest)
                    continue;
                var tags = (SocketTags)sockets[i].Tags;
                var far = (tags & (SocketTags.Grotto | SocketTags.Lake | SocketTags.Roost | SocketTags.RuinRoom)) != 0 ? 2u
                        : (tags & SocketTags.DeadEnd) != 0 ? 1u : 0u;
                var score = far << 30 | DungeonSpawner.Scramble(sockets[i].Seed ^ 0x3A2C7u) >> 2;
                if (best < 0 || score > bestScore)
                    (best, bestScore) = (i, score);
            }
            if (best >= 0)
                (f.MarkedChest, f.MarkedChestPos) = (best, sockets[best].Pos);
            return f;
        }

        /// <summary>
        /// Posted creatures still near their posts, per dungeon id (counted once a frame, in the world animals live in).
        /// False where animals don't live (a joined client).
        /// </summary>
        static bool Left(out Dictionary<uint, int> at)
        {
            at = left;
            var director = Object.FindFirstObjectByType<FaunaDirector>();
            var w = director != null ? director.SimWorld : null;
            if (w == null || !w.IsCreated)
                return false;
            if (leftFrame == Time.frameCount)
                return true;
            leftFrame = Time.frameCount;
            left.Clear();
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DungeonPost>(), ComponentType.ReadOnly<CreatureState>());
            using var posts = q.ToComponentDataArray<DungeonPost>(Allocator.Temp);
            using var where = q.ToComponentDataArray<CreatureState>(Allocator.Temp);
            // (No posted creature anywhere: the director is rebuilding its animals, and the spawner makes them again
            // next frame. Unknown, not cleared.)
            if (posts.Length == 0)
            {
                leftFrame = -1;
                return false;
            }
            for (int i = 0; i < posts.Length; i++)
                if (math.distancesq(where[i].Position, posts[i].Anchor) <= DrivenOff * DrivenOff)
                    left[posts[i].Dungeon] = (left.TryGetValue(posts[i].Dungeon, out var n) ? n : 0) + 1;
            return true;
        }

        /// <summary>
        /// The salt that gives a dungeon a name no other dungeon in the world has: dungeons take their names in id order,
        /// each the first salt whose stem ("Thornwell") is still free. The same on every machine with the same world;
        /// worked out again when the set of dungeons changes.
        /// </summary>
        static uint SaltOf(EntityManager em, uint id)
        {
            if (saltOf.TryGetValue(id, out var salt))
                return salt;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonInfo>());
            using var infos = q.ToComponentDataArray<DungeonInfo>(Allocator.Temp);
            var ids = new List<uint>();
            foreach (var info in infos)
                ids.Add(info.Id);
            ids.Sort();
            saltOf.Clear();
            var taken = new HashSet<string>();
            foreach (var other in ids)
            {
                uint s = 0;
                while (s < 64 && !taken.Add(StemOf(other, s)))
                    s++;
                saltOf[other] = s;
            }
            return saltOf.TryGetValue(id, out salt) ? salt : 0;
        }

        static string[] Words(string list) =>
            DialogueArchive.Current is { } a && a.Cultures.TryGetValue(DialogueArchive.Any, out var any) &&
            any.Words.TryGetValue(list, out var w) && w.Length > 0 ? w : null;

        static string Word(string list, uint h, string fallback) => Words(list) is { } w ? w[h % (uint)w.Length] : fallback;

        /// <summary>The first half of a name: two words from dialogue.json's "dungeon_start" and "dungeon_end".</summary>
        static string StemOf(uint id, uint salt)
        {
            var h = DungeonSpawner.Scramble(id ^ 0xD0461u ^ salt * 0x9E3779B1u);
            return Word("dungeon_start", h, "Deep") + Word("dungeon_end", h >> 11, "hollow");
        }

        /// <summary>
        /// A dungeon's name from its id: a stem from dialogue.json's "dungeon_start" and "dungeon_end" words (Any
        /// culture) and a word for its type ("cave_kind", "ruin_kind", "temple_kind"). The salt (see SaltOf) keeps two
        /// dungeons from sharing a stem. Names are for showing only: they change if the words are edited; the id doesn't.
        /// </summary>
        public static string NameOf(uint id, ushort type, uint salt = 0)
        {
            var h = DungeonSpawner.Scramble(id ^ 0xD0461u);
            var stem = StemOf(id, salt);
            return type switch
            {
                DungeonTypes.SunkenTemple => $"the {Word("temple_kind", h >> 22, "Sunken Temple")} of {stem}",
                DungeonTypes.Ruin => $"the {stem} {Word("ruin_kind", h >> 22, "Ruins")}",
                _ => $"{stem} {Word("cave_kind", h >> 22, "Cave")}",
            };
        }

        /// <summary>The log's list of every dungeon (-dungeonstate, and for checking the API).</summary>
        public static string Describe()
        {
            var all = new List<DungeonStatus>();
            All(all);
            var sb = new System.Text.StringBuilder($"{all.Count} dungeons");
            foreach (var d in all)
                sb.Append($"\n{d.Id:X8} type {d.Type} '{d.Name}' entrance ({d.Entrance.x:F0},{d.Entrance.y:F0},{d.Entrance.z:F0}) " +
                          $"creatures {d.CreaturesLeft}/{d.Creatures} cleared {d.Cleared} chest {d.MarkedChest}");
            return sb.ToString();
        }
    }
}
