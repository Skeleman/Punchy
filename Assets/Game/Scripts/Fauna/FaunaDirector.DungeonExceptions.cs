using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// #24 phase 2 row 3: one dungeon's dead residents as the host sends them: the dungeon's key
    /// (<see cref="FaunaDirector.DungeonKey"/>) and its dead mask, one bit per resident ordinal. Always the whole mask,
    /// so a repeat changes nothing. Sent to a client as it joins for every dungeon with a death, and to every client
    /// when a dungeon's mask changes. The only wire data dungeon life adds (12 bytes; DESIGN-population.md, ruling 2's
    /// pattern).
    /// </summary>
    public struct DungeonExceptionsRpc : IRpcCommand
    {
        public uint Dungeon;
        public ulong Dead;
    }

    /// <summary>
    /// #24 phase 2 row 3: a joined client learns which dungeon residents have died. It keeps its own copy of every
    /// dungeon's residents, built from the same seeded picks by the same builder as the host's records
    /// (<see cref="DungeonSpawner.Residents"/>), so an ordinal means the same creature on both; the host sends the dead
    /// masks. With them, DungeonState's creature count and Cleared agree with the host's on a joined client, except that
    /// a resident driven off its post still counts as there (where it stands isn't sent).
    /// </summary>
    public partial class FaunaDirector
    {
        /// <summary>How long a joined client holds a mask for a dungeon it doesn't have, counted from when it arrived
        /// and from the last dungeon registered here, before it logs and drops it (a late joiner's dungeons register a
        /// little after the host's masks arrive).</summary>
        const float HoldUnknownDungeon = 30f;

        /// <summary>Host: dungeons whose mask changed and is still to be sent to every client.</summary>
        readonly HashSet<uint> dungeonDirty = new HashSet<uint>();
        /// <summary>Host: the connections that have had every dungeon's mask, as they joined.</summary>
        readonly HashSet<Entity> dungeonSentTo = new HashSet<Entity>();
        readonly List<Entity> dungeonGone = new List<Entity>();
        World dungeonServer;
        EntityQuery dungeonConnections;

        /// <summary>Joined client: each dungeon's residents (its own copy, from the seed) and dead mask (the host's).</summary>
        readonly Dictionary<uint, DungeonResident[]> dungeonSeen = new Dictionary<uint, DungeonResident[]>();
        readonly Dictionary<uint, ulong> dungeonHeard = new Dictionary<uint, ulong>();
        /// <summary>Joined client: masks not applied yet (their dungeon isn't registered here yet), in arrival order.</summary>
        readonly List<(uint key, ulong dead, float at)> dungeonHeld = new List<(uint, ulong, float)>();
        World dungeonSeenWorld, dungeonClient;
        EntityQuery dungeonReceived;
        float dungeonSeenAt;

        /// <summary>Dungeons already warned about for having more residents than the mask has bits (once each per run).</summary>
        static readonly HashSet<uint> dungeonOverWarned = new HashSet<uint>();

        /// <summary>Logs once per dungeon (host and joined client alike) that it has more residents than
        /// <see cref="MaxDungeonResidents"/>; those past it never get a bit (nothing shifts past bit 63).</summary>
        static void WarnTooManyResidents(uint id, int residents)
        {
            if (residents > MaxDungeonResidents && dungeonOverWarned.Add(id))
                Debug.LogError($"[dungeon life] dungeon {id:X8} has {residents} creature residents, over {MaxDungeonResidents}: " +
                               "the ones past it can't be told apart when they die");
        }

        /// <summary>Host or solo, from DespawnDungeons: a dungeon's mask gained a bit, so every client is sent it.</summary>
        void DungeonMaskChanged(uint id) => dungeonDirty.Add(id);

        /// <summary>
        /// Each frame, beside the towns' exceptions: a host sends each joining client every dungeon's mask and every
        /// client a changed one; a joined client applies what it was sent. Solo reads its own masks.
        /// </summary>
        void ServeDungeonExceptions()
        {
            if (simWorld != null && simWorld.IsCreated && simWorld.IsServer() && simWorld == dungeonSession)
                SendDungeonExceptions(simWorld);
            if (queryWorld != null && queryWorld.IsCreated && queryWorld.IsClient())
                HearDungeonExceptions(queryWorld, simWorld == null);
        }

        void SendDungeonExceptions(World server)
        {
            var em = server.EntityManager;
            if (dungeonServer != server)
            {
                dungeonServer = server;
                dungeonConnections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
                dungeonSentTo.Clear();
            }
            using var connections = dungeonConnections.ToEntityArray(Allocator.Temp);
            // (A connection gone is forgotten, so an entity reused by a later one is sent everything again.)
            dungeonGone.Clear();
            foreach (var c in dungeonSentTo)
                if (connections.IndexOf(c) < 0)
                    dungeonGone.Add(c);
            foreach (var c in dungeonGone)
                dungeonSentTo.Remove(c);
            foreach (var c in connections)
            {
                if (!dungeonSentTo.Add(c))
                    continue;
                var sent = 0;
                foreach (var dead in dungeonDead)
                    if (dead.Value != 0ul)
                    {
                        SendDungeonMask(em, c, dead.Key, dead.Value);
                        sent++;
                    }
                if (sent > 0)
                    Debug.Log($"[dungeon life] host: sent {sent} dungeons' dead masks to a joining client");
            }
            if (dungeonDirty.Count == 0)
                return;
            foreach (var id in dungeonDirty)
            {
                var dead = dungeonDead.TryGetValue(id, out var bits) ? bits : 0ul;
                foreach (var c in connections)
                    SendDungeonMask(em, c, id, dead);
                if (connections.Length > 0)
                    Debug.Log($"[dungeon life] host: dungeon {id:X8}'s dead mask {dead:X16} sent to {connections.Length} clients");
            }
            dungeonDirty.Clear();
        }

        static void SendDungeonMask(EntityManager em, Entity connection, uint id, ulong dead)
        {
            var rpc = em.CreateEntity(typeof(DungeonExceptionsRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(rpc, new DungeonExceptionsRpc { Dungeon = id, Dead = dead });
            em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        /// <summary>A joined client's dungeon records belong to its client world: a new one (a new host) starts empty.</summary>
        void DungeonClientSession(World client)
        {
            if (dungeonSeenWorld == client)
                return;
            dungeonSeenWorld = client;
            dungeonSeen.Clear();
            dungeonHeard.Clear();
            dungeonHeld.Clear();
        }

        /// <summary>
        /// From <see cref="DungeonSpawnSystem"/> on a joined client: a dungeon's residents, built from the seed as the
        /// host's are (none are made here; the host's creatures are sent as ghosts). Registering again keeps its mask.
        /// </summary>
        internal void RegisterSeenDungeon(World client, uint id, List<DungeonResident> residents)
        {
            DungeonClientSession(client);
            WarnTooManyResidents(id, residents.Count);
            dungeonSeen[id] = residents.ToArray();
            dungeonSeenAt = Time.realtimeSinceStartup;
        }

        /// <summary>A client world: takes the host's masks in; a joined client (apply) keeps them. (A host's own client
        /// world gets them too, and drops them: its masks are the host's.)</summary>
        void HearDungeonExceptions(World client, bool apply)
        {
            var em = client.EntityManager;
            if (dungeonClient != client)
            {
                dungeonClient = client;
                dungeonReceived = em.CreateEntityQuery(ComponentType.ReadOnly<DungeonExceptionsRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            }
            if (apply)
                DungeonClientSession(client);
            var now = Time.realtimeSinceStartup;
            if (!dungeonReceived.IsEmpty)
            {
                using var rpcs = dungeonReceived.ToEntityArray(Allocator.Temp);
                foreach (var e in rpcs)
                {
                    var m = em.GetComponentData<DungeonExceptionsRpc>(e);
                    em.DestroyEntity(e);
                    if (apply)
                        dungeonHeld.Add((m.Dungeon, m.Dead, now));
                }
            }
            if (!apply || dungeonHeld.Count == 0)
                return;
            // In arrival order: a later mask for the same dungeon is the newer one.
            var kept = 0;
            for (int i = 0; i < dungeonHeld.Count; i++)
            {
                var (id, dead, at) = dungeonHeld[i];
                if (dungeonSeen.TryGetValue(id, out var residents))
                {
                    dungeonHeard[id] = dead;
                    SeenResidentsLeft(id, out var all, out var left);
                    Debug.Log($"[dungeon life] client: dungeon {id:X8}'s dead mask {dead:X16} from the host, {math.countbits(dead)} dead; " +
                              $"{left}/{all} at their posts here");
                }
                else if (now - math.max(at, dungeonSeenAt) > HoldUnknownDungeon)
                    Debug.LogWarning($"[dungeon life] client: dungeon {id:X8} isn't one this machine has; its dead mask {dead:X16} dropped");
                else
                    dungeonHeld[kept++] = dungeonHeld[i];
            }
            dungeonHeld.RemoveRange(kept, dungeonHeld.Count - kept);
        }

        /// <summary>
        /// A joined client's DungeonResidentsLeft: a resident is there unless the host's mask has it dead. (Driven-off
        /// residents count as there: where a creature stands isn't sent, only who has died.)
        /// </summary>
        bool SeenResidentsLeft(uint id, out int residents, out int left)
        {
            residents = left = 0;
            if (!dungeonSeen.TryGetValue(id, out var seen))
                return false;
            var dead = dungeonHeard.TryGetValue(id, out var bits) ? bits : 0ul;
            residents = seen.Length;
            foreach (var r in seen)
                if (r.Ordinal >= MaxDungeonResidents || (dead >> r.Ordinal & 1ul) == 0)
                    left++;
            return true;
        }

        /// <summary>Test only: a host's (or solo's) record of a dungeon's residents.</summary>
        internal bool DungeonRecordOf(uint id, out DungeonResident[] residents)
        {
            residents = dungeons.TryGetValue(id, out var d) ? d.Residents : null;
            return residents != null;
        }

        /// <summary>
        /// Test only: "n dungeons, r residents, d dead, hash h" of this machine's dungeon records (the host's own, or a
        /// joined client's copy and the masks it was sent), the hash over every resident's key, ordinal, species and
        /// socket in key order. Host and joiner log it to show they agree.
        /// </summary>
        internal string DungeonDigest()
        {
            var joined = simWorld == null;
            var keys = new List<uint>(joined ? dungeonSeen.Keys : dungeons.Keys);
            keys.Sort();
            var hash = 2166136261u;
            int residents = 0, dead = 0;
            foreach (var k in keys)
            {
                var list = joined ? dungeonSeen[k] : dungeons[k].Residents;
                var mask = joined ? (dungeonHeard.TryGetValue(k, out var h) ? h : 0ul) : DungeonDeadMask(k);
                dead += math.countbits(mask);
                foreach (var r in list)
                {
                    residents++;
                    hash = (hash ^ k) * 16777619u;
                    hash = (hash ^ (uint)r.Ordinal) * 16777619u;
                    hash = (hash ^ (uint)r.Species) * 16777619u;
                    hash = (hash ^ (uint)r.Socket) * 16777619u;
                }
            }
            return $"{keys.Count} dungeons, {residents} residents, {dead} dead, hash {hash:X8}";
        }
    }
}
