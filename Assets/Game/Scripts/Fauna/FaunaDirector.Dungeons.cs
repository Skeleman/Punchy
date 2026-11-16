using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// One dungeon creature as a record (#24 phase 2 row 2): the spawner's seeded pick for one creature socket, the same
    /// on every machine. Its ordinal (its place among the dungeon's residents, in socket order) is the bit it takes in
    /// the dungeon's dead mask. A plain record a save or world file could carry (rule 22).
    /// </summary>
    public struct DungeonResident
    {
        public int Socket;
        public int Ordinal;
        public int Species;
        public float3 Position;
        public float Yaw;
        public DungeonPost Post;
    }

    /// <summary>
    /// #24 phase 2 row 2: dungeon creatures join the one population (DESIGN-population.md, A-110). A dungeon's picks are
    /// its residents, a record every machine has. The host makes a resident only while a player is within
    /// <see cref="Radius"/> of its post (a fixed reach, so every machine agrees), through the queue on a Dungeon tier
    /// with its own cap, nearest first, and lets it go once every player is past <see cref="DespawnDistance"/> of both
    /// it and its post. A resident that dies (Gone) sets its bit in the dungeon's dead mask, the host's exceptions
    /// record: it is never made again this session, nor after a director rebuild. Nothing new is sent (row 3 asks for
    /// the message that tells a joined client).
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>The most dungeon creatures in the world at once, a count of their own beside the other caps. Over it,
        /// the residents nearest a player are the ones made.</summary>
        public const int MaxDungeon = 64;

        /// <summary>The most residents a dungeon can have and still be told apart in its dead mask (one ulong).</summary>
        public const int MaxDungeonResidents = 64;

        sealed class DungeonRecord
        {
            public uint Id;
            public DungeonResident[] Residents;
            /// <summary>Per resident: its creature while it is in the world (host), and whether one is on its way.</summary>
            public Entity[] Live;
            public bool[] Pending;
        }

        readonly Dictionary<uint, DungeonRecord> dungeons = new Dictionary<uint, DungeonRecord>();
        /// <summary>Host or solo: each dungeon's dead residents, one bit per ordinal. The exceptions record.</summary>
        readonly Dictionary<uint, ulong> dungeonDead = new Dictionary<uint, ulong>();
        readonly List<(float distance, DungeonRecord record, int index)> dungeonAsks = new List<(float, DungeonRecord, int)>();
        World dungeonSession;
        int dungeonCount, dungeonLive;
        float nextDungeonLog;

        /// <summary>
        /// A dungeon's key in the director's records: its id (a temple's index, a cave's key) mixed with its type, so a
        /// temple and a cave never share one. The same on every machine.
        /// </summary>
        public static uint DungeonKey(ushort type, uint id) => id ^ (uint)type * 0x9E3779B9u;

        /// <summary>Dungeon creatures in the world or on their way, as last counted (the Dungeon tier's tally).</summary>
        public int DungeonCount => dungeonCount;

        /// <summary>A new simulation world is a new session: its dead start empty and its dungeons register again.</summary>
        void DungeonSession()
        {
            if (simWorld == dungeonSession)
                return;
            dungeonSession = simWorld;
            dungeons.Clear();
            dungeonDead.Clear();
            dungeonCount = dungeonLive = 0;
        }

        /// <summary>
        /// From <see cref="DungeonSpawnSystem"/> where animals live: a dungeon's residents (its creature picks, in socket
        /// order). Registering a dungeon again (a rebuild) keeps its dead and any of its creatures still in the world.
        /// </summary>
        internal void RegisterDungeon(uint id, List<DungeonResident> residents)
        {
            DungeonSession();
            if (residents.Count > MaxDungeonResidents)
                Debug.LogError($"[dungeon life] dungeon {id:X8} has {residents.Count} creature residents, over {MaxDungeonResidents}: " +
                               "the ones past it can't be told apart when they die");
            var record = new DungeonRecord
            {
                Id = id,
                Residents = residents.ToArray(),
                Live = new Entity[residents.Count],
                Pending = new bool[residents.Count]
            };
            if (dungeons.TryGetValue(id, out var old) && old.Residents.Length == record.Residents.Length)
                for (int i = 0; i < old.Live.Length; i++)
                {
                    record.Live[i] = old.Live[i];
                    record.Pending[i] = old.Pending[i];
                }
            dungeons[id] = record;
        }

        /// <summary>Whether a dungeon's resident (by ordinal) has died this session.</summary>
        bool DungeonDead(uint id, int ordinal) =>
            ordinal < MaxDungeonResidents && dungeonDead.TryGetValue(id, out var bits) && (bits & 1ul << ordinal) != 0;

        /// <summary>From Teardown: every creature is destroyed and the queue emptied, so none is live or on its way.</summary>
        void ForgetDungeonLive()
        {
            foreach (var d in dungeons.Values)
            {
                System.Array.Clear(d.Live, 0, d.Live.Length);
                System.Array.Clear(d.Pending, 0, d.Pending.Length);
            }
            dungeonCount = dungeonLive = 0;
        }

        /// <summary>
        /// With Despawn: residents gone (caught, killed) are marked dead and removed; those far from every player (both
        /// where they are and their post) are let go. Counts the Dungeon tier.
        /// </summary>
        void DespawnDungeons()
        {
            DungeonSession();
            var em = simWorld.EntityManager;
            dungeonCount = dungeonLive = 0;
            foreach (var d in dungeons.Values)
                for (int i = 0; i < d.Residents.Length; i++)
                {
                    if (d.Pending[i])
                    {
                        dungeonCount++;
                        continue;
                    }
                    var e = d.Live[i];
                    if (e == Entity.Null)
                        continue;
                    if (!em.Exists(e) || !em.HasComponent<CreatureState>(e))
                    {
                        d.Live[i] = Entity.Null;
                        continue;
                    }
                    var s = em.GetComponentData<CreatureState>(e);
                    if (s.Mode == CreatureMode.Held)
                    {
                        dungeonCount++;
                        dungeonLive++;
                        continue;
                    }
                    if (s.Gone)
                    {
                        var ordinal = d.Residents[i].Ordinal;
                        if (ordinal < MaxDungeonResidents)
                            dungeonDead[d.Id] = (dungeonDead.TryGetValue(d.Id, out var bits) ? bits : 0ul) | 1ul << ordinal;
                        Caught++;
                        Debug.Log($"[dungeon life] dungeon {d.Id:X8} resident {ordinal} ({d.Residents[i].Post.Name}) died at " +
                                  $"({s.Position.x:F1}, {s.Position.y:F1}, {s.Position.z:F1}): not made again this session");
                        em.DestroyEntity(e);
                        d.Live[i] = Entity.Null;
                        continue;
                    }
                    if (players.Count == 0 || NearestPlayer(s.Position.xz) > DespawnDistance && NearestPlayer(d.Residents[i].Position.xz) > DespawnDistance)
                    {
                        em.DestroyEntity(e);
                        d.Live[i] = Entity.Null;
                        continue;
                    }
                    dungeonCount++;
                    dungeonLive++;
                }
        }

        /// <summary>
        /// With the other residents: each living resident within Radius of a player whose creature isn't in the world is
        /// asked for, nearest first, up to <see cref="MaxDungeon"/>; it is made at its post, facing as its socket does.
        /// </summary>
        void SpawnDungeons()
        {
            DungeonSession();
            dungeonAsks.Clear();
            foreach (var d in dungeons.Values)
                for (int i = 0; i < d.Residents.Length; i++)
                {
                    if (d.Live[i] != Entity.Null || d.Pending[i] || DungeonDead(d.Id, d.Residents[i].Ordinal))
                        continue;
                    var near = NearestPlayer(d.Residents[i].Position.xz);
                    if (near <= Radius)
                        dungeonAsks.Add((near, d, i));
                }
            if (dungeonAsks.Count > 0)
            {
                dungeonAsks.Sort((a, b) => a.distance.CompareTo(b.distance));
                var asked = 0;
                foreach (var (_, d, i) in dungeonAsks)
                {
                    if (dungeonCount >= MaxDungeon)
                        break;
                    var r = d.Residents[i];
                    var record = d;
                    var index = i;
                    record.Pending[index] = true;
                    asked++; // (Enqueue counts it on the Dungeon tier)
                    Enqueue(r.Species, r.Position, SpawnTier.Dungeon, e =>
                    {
                        record.Pending[index] = false;
                        record.Live[index] = e;
                        simWorld.EntityManager.AddComponentData(e, record.Residents[index].Post);
                    }, r.Yaw);
                }
                if (asked > 0)
                    Debug.Log($"[dungeon life] {asked} residents asked for ({dungeonAsks.Count} wanted); dungeon tier {dungeonCount}/{MaxDungeon}");
            }
            if (LogDungeonLife && Time.time >= nextDungeonLog)
            {
                nextDungeonLog = Time.time + 5f;
                var residents = 0;
                var dead = 0;
                foreach (var d in dungeons.Values)
                {
                    residents += d.Residents.Length;
                    for (int i = 0; i < d.Residents.Length; i++)
                        if (DungeonDead(d.Id, d.Residents[i].Ordinal))
                            dead++;
                }
                Debug.Log($"[dungeon life] {dungeons.Count} dungeons, {residents} residents: {dungeonLive} in the world, " +
                          $"{dungeonCount - dungeonLive} on their way, {dead} dead; tier {dungeonCount}/{MaxDungeon}");
            }
        }

        /// <summary>"-dungeonlife" (test): log the Dungeon tier every 5 s.</summary>
        static readonly bool LogDungeonLife = System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-dungeonlife") >= 0;

        /// <summary>
        /// For DungeonState, where animals live: a dungeon's residents and how many are still at their posts (living,
        /// and either not in the world or within <paramref name="drivenOff"/> of their post). False before the dungeon is
        /// registered, or where animals don't live.
        /// </summary>
        public bool DungeonResidentsLeft(uint id, float drivenOff, out int residents, out int left)
        {
            residents = left = 0;
            if (simWorld == null || !simWorld.IsCreated || simWorld != dungeonSession || !dungeons.TryGetValue(id, out var d))
                return false;
            var em = simWorld.EntityManager;
            residents = d.Residents.Length;
            for (int i = 0; i < d.Residents.Length; i++)
            {
                if (DungeonDead(id, d.Residents[i].Ordinal))
                    continue;
                var e = d.Live[i];
                if (e != Entity.Null && em.Exists(e) && em.HasComponent<CreatureState>(e))
                {
                    var s = em.GetComponentData<CreatureState>(e);
                    if (s.Gone || math.distancesq(s.Position, d.Residents[i].Position) > drivenOff * drivenOff)
                        continue;
                }
                left++;
            }
            return true;
        }

        /// <summary>Test only: the first resident of any dungeon whose creature is in the world, or Null.</summary>
        internal Entity AnyLiveDungeonResident(out uint dungeon, out int ordinal)
        {
            dungeon = 0;
            ordinal = -1;
            if (simWorld == null || !simWorld.IsCreated)
                return Entity.Null;
            foreach (var d in dungeons.Values)
                for (int i = 0; i < d.Residents.Length; i++)
                    if (d.Live[i] != Entity.Null && simWorld.EntityManager.Exists(d.Live[i]))
                    {
                        dungeon = d.Id;
                        ordinal = d.Residents[i].Ordinal;
                        return d.Live[i];
                    }
            return Entity.Null;
        }

        /// <summary>Test only: whether a dungeon's resident (by ordinal) has a creature in the world.</summary>
        internal bool DungeonResidentLive(uint id, int ordinal)
        {
            if (!dungeons.TryGetValue(id, out var d) || simWorld == null || !simWorld.IsCreated)
                return false;
            for (int i = 0; i < d.Residents.Length; i++)
                if (d.Residents[i].Ordinal == ordinal && d.Live[i] != Entity.Null && simWorld.EntityManager.Exists(d.Live[i]))
                    return true;
            return false;
        }

        /// <summary>Test only: players the director counted last.</summary>
        internal int DungeonPlayers => players.Count;

        /// <summary>Test only: a dungeon's dead mask.</summary>
        internal ulong DungeonDeadMask(uint id) => dungeonDead.TryGetValue(id, out var bits) ? bits : 0ul;
    }
}
