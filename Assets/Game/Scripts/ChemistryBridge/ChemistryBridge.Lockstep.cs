using System;
using System.Collections.Generic;
using System.Globalization;
using Chem;
using Chem.Lockstep;
using Punchy.Net;
using Punchy.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;
using EcsWorld = Unity.Entities.World;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The chemistry in lockstep (D19 in Spell Chemistry Valley, its docs/design/core-package/multiplayer-ports.md): one
    /// <see cref="LockstepSession"/> a process runs the cells, so every peer's world is the host's. Solo runs the same
    /// session over the core's loopback, so there's one code path. The host (or solo) fills one shared region from
    /// TerrainSource and sends it in the hello (the kinds, run-length coded), so a client builds the same cells without
    /// sampling a float; a joiner then loads the host's snapshot (the cells' changes against that region) and runs the
    /// batches after it. Casts and the hands' input go as requests on the session's casts and combos channels (never an
    /// input-component field); the host stamps them and every peer runs them on the same tick. The host's chunk plan
    /// follows each player's reach. A desync is logged (desync.Text) and put right by a fresh snapshot (the core's).
    /// </summary>
    public partial class ChemistryBridge
    {
        // A checksum line every this many ticks (10 s at 6 a second), on every peer: the two-peer test compares them.
        const int SumEvery = 60;

        LockstepSession session;
        LockstepWorld port;
        LockstepRole sessionRole;
        NfeLockstepTransport net;
        LockstepChannel<CastCommand> casts;
        LockstepChannel<ComboCommand> combos;
        LockstepChannel<ChunkSwitch> chunks;
        ChunkPlanner planner = new ChunkPlanner();
        RegionSource region;
        bool dropSession;
        int desyncs;
        EntityQuery playersQuery;
        EcsWorld playersWorld;

        /// <summary>The cells: the session's world (a snapshot or a reset replaces it).</summary>
        Chem.World world => port != null && region != null ? port.World : null;

        /// <summary>This peer's wizard in the core: "p" and its NetworkId, the host's (and solo's) "p0".</summary>
        string Me => session == null || session.IsHost ? LockstepChannels.PlayerName(default) : "p" + Math.Max(0, net != null ? net.LocalId : 0);

        public LockstepSession Session => session;

        /// <summary>Ticks this peer's world is behind what it holds: the host's own stamps, or a client's batches.</summary>
        int Behind => session == null ? 0 : (session.IsHost ? session.HostTick : session.KnownUpTo) - session.NetTick;

        void HookLockstep() => GameBootstrap.WorldsReplacing += OnWorldsReplacing;

        void UnhookLockstep()
        {
            GameBootstrap.WorldsReplacing -= OnWorldsReplacing;
            DropSession();
        }

        // The game is leaving solo, a host or a join: the session goes with it, and the next one starts as the new worlds do.
        void OnWorldsReplacing() => dropSession = true;

        /// <summary>What this process is now, from the worlds there are; null while they're being replaced.</summary>
        static LockstepRole? CurrentRole()
        {
            var server = NetWorlds.Server;
            if (server != null && server.IsCreated && server.IsServer())
                return LockstepRole.Host;
            var client = NetWorlds.Client;
            if (client == null || !client.IsCreated)
                return null;
            return client.IsClient() ? LockstepRole.Client : LockstepRole.Solo;
        }

        void DropSession()
        {
            dropSession = false;
            if (session != null)
                Debug.Log($"[Chemistry] lockstep: the {sessionRole} session ends at tick {session.NetTick}");
            session = null;
            port = null;
            region = null;
            viewWorld = null;
            if (net != null)
                Destroy(net);
            net = null;
            ForgetLand();
            Shader.SetGlobalFloat(OnId, 0f);
        }

        /// <summary>Host or solo: a session over the region just seeded (a host's transport kept, if it had one).</summary>
        void StartSession(LockstepRole role, int2 at, byte[] kinds)
        {
            var keep = role == LockstepRole.Host ? net : null;
            if (keep == null && net != null)
                Destroy(net);
            region = new RegionSource(at, kinds);
            port = new LockstepWorld(rules, BuildRegion);
            ILockstepTransport transport;
            if (role == LockstepRole.Host)
                transport = net = keep != null ? keep : NfeLockstepTransport.Make(gameObject, role, NetWorlds.Server, NetWorlds.Client);
            else
            {
                net = null;
                transport = new LockstepLoopback().AddHost("p0", solo: true);
            }
            NewSession(role, transport);
            session.MapId = "punchy";
            session.Start["corner"] = $"{at.x},{at.y}";
            session.Start["region"] = region.Encoded;
            session.Stamping += PlanChunks;
        }

        /// <summary>A client: a session waiting for the host's hello (the region), then its snapshot.</summary>
        void StartClient()
        {
            region = null;
            port = new LockstepWorld(rules, BuildRegion);
            net = NfeLockstepTransport.Make(gameObject, LockstepRole.Client, null, NetWorlds.Client);
            NewSession(LockstepRole.Client, net);
            session.AutoJoin = false;
            session.HelloReceived += hello =>
            {
                if (!hello.Start.TryGetValue("corner", out var c) || !hello.Start.TryGetValue("region", out var r))
                {
                    Debug.LogWarning("[Chemistry] lockstep: the host's hello has no region");
                    return;
                }
                var parts = c.Split(',');
                region = new RegionSource(new int2(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)), RegionSource.Decode(r));
                port.Rebuild();
                corner = region.Corner;
                seeded = region.Kinds;
                heights = null;   // sampled here (cosmetic only: flames and puffs sit on them)
                Debug.Log($"[Chemistry] lockstep: the host's region at ({corner.x}, {corner.y}), {r.Length} chars; joining");
                session.Join();
            };
            session.Joined += t => Debug.Log($"[Chemistry] lockstep: joined at tick {t} from the host's snapshot");
            session.CaughtUp += t => Debug.Log($"[Chemistry] lockstep: caught up at tick {t}");
        }

        void NewSession(LockstepRole role, ILockstepTransport transport)
        {
            sessionRole = role;
            desyncs = 0;
            planner = new ChunkPlanner();
            session = new LockstepSession(transport, port, default);
            casts = session.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
            combos = session.Register(LockstepChannels.Combos(LockstepChannels.PlayerName, Debug.isDebugBuild));
            chunks = session.Register(LockstepChannels.Chunks());
            if (transport is LoopbackPeer peer)
                peer.Session = session;
            if (net != null)
                net.Session = session;
            session.TickApplied += Applied;
            session.Desynced += d =>
            {
                desyncs++;
                Debug.LogWarning($"[Chemistry] lockstep: desync: {d.Text}");
            };
            session.Failed += why => Debug.LogWarning($"[Chemistry] lockstep: {why}");
            session.Refused += r => Debug.Log($"[Chemistry] lockstep: refused {r.Channel.Name} from peer {r.Peer.Value}: {r.Why}");
            Debug.Log($"[Chemistry] lockstep: a {role} session");
        }

        Chem.World BuildRegion()
        {
            if (region == null)
                return new Chem.World(rules, 64, 64);   // (a client's, until the hello brings the host's region)
            var w = new Chem.World(rules, Size, Size, 1);
            w.MakeFrom(region);
            return w;
        }

        // Every peer, as each tick has run: the casts that landed, and now and then the checksum.
        void Applied(TickBatch batch)
        {
            foreach (var c in batch.Casts)
                Debug.Log($"[Chemistry] lockstep: tick {batch.Tick}: {c.By} cast {c.Spell} (charge {c.Charge}) at cell ({c.X}, {c.Y})");
            if (batch.Tick % SumEvery == 0)
                Debug.Log($"[Chemistry] lockstep {sessionRole} tick {batch.Tick} at {DateTime.Now:HH:mm:ss.f} sum {Fnv1a64.Hash(session.Checksum()):x16} " +
                          $"burning {burningCount} burnt {burntCount} ice {iceCount} behind {Behind} peers {(net != null ? net.Peers.Count : 0)} desync={session.DesyncSeen} desyncs={desyncs}");
        }

        // Host: the chunks follow each player's reach (its own and every NetPlayer on the server), stamped on the tick.
        void PlanChunks(int tick)
        {
            var w = world;
            if (w == null)
                return;
            var at = new List<(int x, int y)>();
            var server = NetWorlds.Server;
            if (server != null && server.IsCreated)
            {
                if (playersWorld != server)
                {
                    playersQuery = server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(),
                        server.IsServer() ? ComponentType.ReadOnly<NetPlayer>() : ComponentType.ReadOnly<PlayerTag>());
                    playersWorld = server;
                }
                using var ts = playersQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                foreach (var t in ts)
                    at.Add(((int)math.floor(t.Position.x) - corner.x, (int)math.floor(t.Position.z) - corner.y));
            }
            foreach (var s in planner.Plan(w, at))
                session.Stamp(chunks, s);
        }

        /// <summary>
        /// The shared region as made: each cell's kind as the host sampled it (grass, water two steps down and filled,
        /// rock, dirt). A save is only its changes against this, and a client makes the same cells from the kinds alone.
        /// </summary>
        sealed class RegionSource : IChunkSource
        {
            public readonly int2 Corner;
            public readonly byte[] Kinds;
            public readonly string Encoded;

            public RegionSource(int2 corner, byte[] kinds)
            {
                Corner = corner;
                Kinds = kinds;
                Encoded = Encode(kinds);
                Version = $"punchy-region:{corner.x},{corner.y}:{Size}:{Fnv1a64.Hash(Encoded):x16}";
            }

            public string Version { get; }

            public void Make(int x, int y, Cell cell)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size)
                    return;
                switch (Kinds[y * Size + x])
                {
                    case KindGrass:
                        cell.T = "grass";
                        break;
                    case KindWater:
                        // A pool two height steps down, filled to just under the bank, so the water stays put.
                        cell.Height = -2;
                        cell.Water = 6;
                        break;
                    case KindRock:
                        cell.T = "rock";
                        break;
                }
            }

            // Runs of one kind: a kind byte and the run's length (a varint), base64.
            static string Encode(byte[] kinds)
            {
                var bytes = new List<byte>();
                for (int i = 0; i < kinds.Length;)
                {
                    int j = i;
                    while (j < kinds.Length && kinds[j] == kinds[i])
                        j++;
                    bytes.Add(kinds[i]);
                    for (uint run = (uint)(j - i); ; run >>= 7)
                    {
                        if (run < 0x80)
                        {
                            bytes.Add((byte)run);
                            break;
                        }
                        bytes.Add((byte)(run & 0x7f | 0x80));
                    }
                    i = j;
                }
                return Convert.ToBase64String(bytes.ToArray());
            }

            public static byte[] Decode(string text)
            {
                var bytes = Convert.FromBase64String(text);
                var kinds = new byte[Size * Size];
                int at = 0;
                for (int i = 0; i < bytes.Length && at < kinds.Length;)
                {
                    byte kind = bytes[i++];
                    uint run = 0;
                    for (int shift = 0; i < bytes.Length; shift += 7)
                    {
                        byte b = bytes[i++];
                        run |= (uint)(b & 0x7f) << shift;
                        if (b < 0x80)
                            break;
                    }
                    for (; run > 0 && at < kinds.Length; run--)
                        kinds[at++] = kind;
                }
                return kinds;
            }
        }
    }
}
