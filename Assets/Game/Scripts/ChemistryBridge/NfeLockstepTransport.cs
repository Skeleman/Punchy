using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chem.Lockstep;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using EcsWorld = Unity.Entities.World;

namespace Punchy.Chemistry
{
    /// <summary>
    /// One piece of a chemistry lockstep message (D19 in Spell Chemistry Valley): up to <see cref="Bytes"/> bytes, and its
    /// number on its link, so the pieces go to the session in the order sent whatever order the receive query finds them.
    /// NfE's RPCs aren't fragmented, so a piece stays under a packet; the session splits anything longer.
    /// </summary>
    public struct ChemMsgRpc : IRpcCommand
    {
        public const int Bytes = 1020;
        public uint Seq;
        public ushort Length;
        public FixedBytes510 A, B;
    }

    /// <summary>
    /// The chemistry's lockstep transport over Netcode for Entities: bytes for a <see cref="LockstepSession"/>, carried as
    /// <see cref="ChemMsgRpc"/> pieces, reliable and ordered per connection. A MonoBehaviour pump in the Items.cs style:
    /// the host sends from <see cref="NetWorlds.Server"/> to one connection or to each, a client to its one connection, and
    /// every frame what arrived is handed to the session from here, never inside a system. On the host every connection
    /// but its own client's is a peer (PeerId = its NetworkId), announced once it's in game and the session exists; its
    /// own client takes nothing (the host runs its batches without a send) and drops anything that reaches it. Sends wait
    /// in an outbox per link and go at most <see cref="PiecesAFrame"/> a frame, so a join snapshot never fills NfE's send
    /// queue. One a process: made for host or join, dropped as the worlds are replaced (ChemistryBridge.Lockstep.cs).
    /// </summary>
    public sealed class NfeLockstepTransport : MonoBehaviour, ILockstepTransport
    {
        const int PiecesAFrame = 24;
        const int HostLink = 0;   // a client's one link (no NetworkId is 0)

        EcsWorld server, client;
        readonly Dictionary<int, Queue<byte[]>> outbox = new Dictionary<int, Queue<byte[]>>();
        readonly Dictionary<int, uint> sent = new Dictionary<int, uint>(), next = new Dictionary<int, uint>();
        readonly Dictionary<int, SortedDictionary<uint, byte[]>> early = new Dictionary<int, SortedDictionary<uint, byte[]>>();
        readonly HashSet<int> peers = new HashSet<int>();
        readonly List<(int from, uint seq, byte[] bytes)> arrived = new List<(int, uint, byte[])>();
        readonly HashSet<int> inGame = new HashSet<int>();
        EntityQuery serverRpcs, clientRpcs, serverConnections, clientConnection;

        public LockstepRole Role { get; private set; }
        public int MaxMessageBytes => ChemMsgRpc.Bytes;

        /// <summary>The session the pump hands messages to (and, on the host, tells of peers); null holds them back.</summary>
        public LockstepSession Session { get; set; }

        /// <summary>This machine's NetworkId on its client world (-1 until connected): a client's PeerId, and the host's own client.</summary>
        public int LocalId { get; private set; } = -1;

        /// <summary>The peers the session knows of, as NetworkIds (host).</summary>
        public IReadOnlyCollection<int> Peers => peers;

        public static NfeLockstepTransport Make(GameObject on, LockstepRole role, EcsWorld server, EcsWorld client)
        {
            var t = on.AddComponent<NfeLockstepTransport>();
            t.Role = role;
            t.server = server;
            t.client = client;
            if (server != null)
                Queries(server, out t.serverRpcs, out t.serverConnections);
            if (client != null)
                Queries(client, out t.clientRpcs, out t.clientConnection);
            return t;

            static void Queries(EcsWorld w, out EntityQuery rpcs, out EntityQuery connections)
            {
                var em = w.EntityManager;
                rpcs = em.CreateEntityQuery(ComponentType.ReadOnly<ChemMsgRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
                connections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
            }
        }

        public void SendToHost(ReadOnlySpan<byte> message) => Queue(HostLink, message);

        public void SendTo(PeerId peer, ReadOnlySpan<byte> message)
        {
            if (peers.Contains(peer.Value))
                Queue(peer.Value, message);
        }

        public void Broadcast(ReadOnlySpan<byte> message)
        {
            foreach (var p in peers)
                Queue(p, message);
        }

        void Queue(int link, ReadOnlySpan<byte> message)
        {
            if (!outbox.TryGetValue(link, out var q))
                outbox[link] = q = new Queue<byte[]>();
            q.Enqueue(message.ToArray());
        }

        bool Alive(EcsWorld w) => w != null && w.IsCreated;

        void Update()
        {
            if (Alive(client) && clientConnection.TryGetSingleton<NetworkId>(out var id))
                LocalId = id.Value;
            if (Role == LockstepRole.Host && Alive(server))
            {
                Connections();
                Take(server, serverRpcs, true);
            }
            if (Alive(client))
                Take(client, clientRpcs, Role == LockstepRole.Client);
            Deliver();
            Flush();
        }

        // Host: connections in game (other than our own client's) become peers once there's a session; gone ones leave.
        void Connections()
        {
            var em = server.EntityManager;
            inGame.Clear();
            using (var cs = serverConnections.ToEntityArray(Allocator.Temp))
                foreach (var c in cs)
                    if (em.HasComponent<NetworkStreamInGame>(c))
                        inGame.Add(em.GetComponentData<NetworkId>(c).Value);
            foreach (var p in new List<int>(peers))
                if (!inGame.Contains(p))
                {
                    peers.Remove(p);
                    outbox.Remove(p);
                    sent.Remove(p);
                    next.Remove(p);
                    early.Remove(p);
                    Session?.PeerLeft(new PeerId(p));
                    Debug.Log($"[Chemistry] lockstep: peer {p} left");
                }
            if (Session == null || LocalId < 0)
                return;
            foreach (var p in inGame)
                if (p != LocalId && peers.Add(p))
                {
                    Debug.Log($"[Chemistry] lockstep: peer {p} joined");
                    Session.PeerJoined(new PeerId(p), "p" + p);
                }
        }

        // Every piece that has come in on this world: noted (if it's for us) and used up.
        void Take(EcsWorld w, EntityQuery rpcs, bool ours)
        {
            if (rpcs.IsEmpty)
                return;
            var em = w.EntityManager;
            using var rs = rpcs.ToEntityArray(Allocator.Temp);
            foreach (var r in rs)
            {
                if (ours)
                {
                    var m = em.GetComponentData<ChemMsgRpc>(r);
                    int from = HostLink;
                    if (w == server)
                    {
                        var source = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                        from = em.Exists(source) && em.HasComponent<NetworkId>(source) ? em.GetComponentData<NetworkId>(source).Value : -1;
                    }
                    if (from >= 0)
                        arrived.Add((from, m.Seq, Read(ref m)));
                }
                em.DestroyEntity(r);
            }
        }

        // To the session in each link's order: a piece found ahead of one before it waits for it.
        void Deliver()
        {
            foreach (var (from, seq, bytes) in arrived)
            {
                if (!early.TryGetValue(from, out var held))
                    early[from] = held = new SortedDictionary<uint, byte[]>();
                held[seq] = bytes;
            }
            arrived.Clear();
            if (Session == null)
                return;
            foreach (var kv in early)
            {
                next.TryGetValue(kv.Key, out var want);
                while (kv.Value.TryGetValue(want, out var bytes))
                {
                    kv.Value.Remove(want);
                    want++;
                    if (Role == LockstepRole.Client || peers.Contains(kv.Key))
                        Session.Received(new PeerId(kv.Key), bytes);
                }
                next[kv.Key] = want;
            }
        }

        void Flush()
        {
            foreach (var kv in outbox)
            {
                if (kv.Value.Count == 0)
                    continue;
                var w = Role == LockstepRole.Host ? server : client;
                if (!Alive(w))
                    return;
                var em = w.EntityManager;
                Entity target;
                if (Role == LockstepRole.Host)
                    target = ConnectionOf(kv.Key);
                else
                    target = clientConnection.IsEmpty ? Entity.Null : clientConnection.GetSingletonEntity();
                if (target == Entity.Null)
                    continue;
                sent.TryGetValue(kv.Key, out var seq);
                for (int n = 0; n < PiecesAFrame && kv.Value.Count > 0; n++)
                {
                    var m = new ChemMsgRpc { Seq = seq++ };
                    Write(ref m, kv.Value.Dequeue());
                    var e = em.CreateEntity(typeof(ChemMsgRpc), typeof(SendRpcCommandRequest));
                    em.SetComponentData(e, m);
                    em.SetComponentData(e, new SendRpcCommandRequest { TargetConnection = target });
                }
                sent[kv.Key] = seq;
            }
        }

        Entity ConnectionOf(int id)
        {
            var em = server.EntityManager;
            using var cs = serverConnections.ToEntityArray(Allocator.Temp);
            foreach (var c in cs)
                if (em.GetComponentData<NetworkId>(c).Value == id)
                    return c;
            return Entity.Null;
        }

        // The piece's bytes, A then B (no unsafe code in this assembly: spans over the fixed buffers).
        static Span<byte> Of(ref FixedBytes510 b) => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref b, 1));

        static void Write(ref ChemMsgRpc m, byte[] bytes)
        {
            if (bytes.Length > ChemMsgRpc.Bytes)
                throw new ArgumentException($"a piece of {bytes.Length} bytes, past {ChemMsgRpc.Bytes}");
            m.Length = (ushort)bytes.Length;
            var a = Of(ref m.A);
            int inA = Math.Min(bytes.Length, a.Length);
            bytes.AsSpan(0, inA).CopyTo(a);
            bytes.AsSpan(inA).CopyTo(Of(ref m.B));
        }

        static byte[] Read(ref ChemMsgRpc m)
        {
            var bytes = new byte[Math.Min((int)m.Length, ChemMsgRpc.Bytes)];
            var a = Of(ref m.A);
            int inA = Math.Min(bytes.Length, a.Length);
            a.Slice(0, inA).CopyTo(bytes);
            Of(ref m.B).Slice(0, bytes.Length - inA).CopyTo(bytes.AsSpan(inA));
            return bytes;
        }
    }
}
