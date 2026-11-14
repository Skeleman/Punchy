using System;
using System.Collections.Generic;
using Chem;
using Chem.Lockstep;
using Punchy.Net;
using Punchy.Player;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Debug = UnityEngine.Debug;
using EcsWorld = Unity.Entities.World;

namespace Punchy.Chemistry
{
    /// <summary>
    /// The chemistry in lockstep (D19 in Spell Chemistry Valley, its docs/design/core-package/multiplayer-ports.md) over the
    /// core's open world (D20, open-world.md): one <see cref="LockstepSession"/> a process runs the cells, so every peer's
    /// world is the host's. Solo runs the same session over the core's loopback. The world has no size: a chunk is keyed
    /// by (layer, cx, cy) on Punchy's own 1 m grid (cell = floor of the absolute position; Punchy never shifts its origin),
    /// and exists once it's filled. The host (and solo) answers the core's two questions through <see cref="PunchyGround"/>:
    /// where each player is (its layer and cell), and what a chunk's ground is (TerrainSource, sampled in a Burst job, as the
    /// shared region was). The core's planner stamps the fills on the fills channel as players reach new ground, so every
    /// peer makes the same chunk on the same tick and no client samples a float for the cells; a joiner loads the filled
    /// chunks in the snapshot. Casts and the hands' input go as requests on the casts and combos channels; the host stamps
    /// them and every peer runs them on the same tick. A desync is logged (desync.Text) and put right by a fresh snapshot.
    /// </summary>
    public partial class ChemistryBridge
    {
        // A checksum line every this many ticks (10 s at 6 a second), on every peer: the two-peer test compares them.
        const int SumEvery = 60;
        // The layers (ChunkKey.Layer): the surface, and the caves and the sunken temple's rooms under it.
        public const int SurfaceLayer = 0, CaveLayer = 1;
        // A player this far (m) under the ground surface and in cave air is in the cave layer.
        const float CaveDepth = 2f;
        // The terrain must have stayed the same this long (s) before the first fill: the first TerrainSource at start-up is
        // a stand-in whose surfaces aren't baked, and a fill is for good.
        const float TerrainSettle = 3f;

        LockstepSession session;
        LockstepWorld port;
        LockstepRole sessionRole;
        NfeLockstepTransport net;
        LockstepChannel<CastCommand> casts;
        LockstepChannel<ComboCommand> combos;
        LockstepChannel<ChunkFill> fills;
        LockstepChannel<ChunkSwitch> chunks;
        OpenWorldPlanner planner;
        bool dropSession;
        int desyncs, castsLanded, sampled;
        double sampleMs;
        readonly System.Diagnostics.Stopwatch sampleWatch = new System.Diagnostics.Stopwatch();
        EntityQuery playersQuery;
        EcsWorld playersWorld;

        // The terrain the fills sample (host and solo), and since when it has been the same.
        TerrainSource terrainNow;
        bool terrainKnown;
        float terrainSince;

        /// <summary>The cells: the session's world (a snapshot or a reset replaces it).</summary>
        Chem.World world => port?.World;

        /// <summary>This peer's wizard in the core: "p" and its NetworkId, the host's (and solo's) "p0".</summary>
        string Me => session == null || session.IsHost ? LockstepChannels.PlayerName(default) : "p" + Math.Max(0, net != null ? net.LocalId : 0);

        public LockstepSession Session => session;

        /// <summary>Ticks this peer's world is behind what it holds: the host's own stamps, or a client's batches.</summary>
        int Behind => session == null ? 0 : (session.IsHost ? session.HostTick : session.KnownUpTo) - session.NetTick;

        bool TerrainSettled => terrainKnown && Time.unscaledTime - terrainSince >= TerrainSettle;

        void HookLockstep() => GameBootstrap.WorldsReplacing += OnWorldsReplacing;

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
                Debug.Log($"[Chemistry] lockstep: the {sessionRole} session ends at tick {session.NetTick}, {world?.Filled.Count ?? 0} chunks filled");
            session = null;
            port = null;
            planner = null;
            CancelGround();
            viewWorld = null;
            if (net != null)
                Destroy(net);
            net = null;
            ForgetLand();
            Shader.SetGlobalFloat(OnId, 0f);
        }

        /// <summary>
        /// The terrain as it is this frame. Host and solo: a terrain that changes before anything has been cast starts the
        /// session again (the start-up stand-in, an island's later pass), since what's filled is kept for good; later, the
        /// chunks filled already keep the ground they were filled with.
        /// </summary>
        void NoteTerrain(TerrainSource terrain)
        {
            if (terrainKnown && terrain.Equals(terrainNow))
                return;
            terrainNow = terrain;
            terrainKnown = true;
            terrainSince = Time.unscaledTime;
            CancelGround();   // (a job under way samples the old terrain)
            if (session == null || !session.IsHost)
                return;
            if (castsLanded == 0 && (net == null || net.Peers.Count == 0))
            {
                Debug.Log($"[Chemistry] lockstep: the terrain changed before any cast; the {sessionRole} session starts again");
                DropSession();
            }
            else
                Debug.Log($"[Chemistry] lockstep: the terrain changed; the {world?.Filled.Count ?? 0} chunks filled keep their ground");
        }

        /// <summary>Host or solo: a session over an empty open world (a host's transport kept, if it had one).</summary>
        void StartSession(LockstepRole role)
        {
            var keep = role == LockstepRole.Host ? net : null;
            if (keep == null && net != null)
                Destroy(net);
            port = new LockstepWorld(rules, NewWorld);
            ILockstepTransport transport;
            if (role == LockstepRole.Host)
                transport = net = keep != null ? keep : NfeLockstepTransport.Make(gameObject, role, NetWorlds.Server, NetWorlds.Client);
            else
            {
                net = null;
                transport = new LockstepLoopback().AddHost("p0", solo: true);
            }
            NewSession(role, transport);
            session.MapId = "punchy-open";
            // (The planner's own Attach, but each tick's fills held to the chunks whose ground jobs are done: StampOpen.)
            planner = new OpenWorldPlanner(new PunchyGround(this));
            session.Stamping += StampOpen;
        }

        /// <summary>A client: a session that joins from the host's snapshot (the filled chunks) and runs its batches.</summary>
        void StartClient()
        {
            port = new LockstepWorld(rules, NewWorld);
            net = NfeLockstepTransport.Make(gameObject, LockstepRole.Client, null, NetWorlds.Client);
            NewSession(LockstepRole.Client, net);
            session.Joined += t => Debug.Log($"[Chemistry] lockstep: joined at tick {t} from the host's snapshot, {world?.Filled.Count ?? 0} chunks filled");
            session.CaughtUp += t => Debug.Log($"[Chemistry] lockstep: caught up at tick {t}");
        }

        Chem.World NewWorld() => Chem.World.OpenWorld(rules, 1);

        void NewSession(LockstepRole role, ILockstepTransport transport)
        {
            sessionRole = role;
            desyncs = castsLanded = sampled = 0;
            sampleMs = 0;
            session = new LockstepSession(transport, port, default);
            casts = session.Register(LockstepChannels.Casts(LockstepChannels.PlayerName));
            combos = session.Register(LockstepChannels.Combos(LockstepChannels.PlayerName, Debug.isDebugBuild));
            fills = session.Register(LockstepChannels.Fills());
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
            Debug.Log($"[Chemistry] lockstep: a {role} session over the open world");
        }

        // Every peer, as each tick has run: the casts that landed, and now and then the checksum, the chunks and the memory.
        void Applied(TickBatch batch)
        {
            foreach (var c in batch.Casts)
            {
                castsLanded++;
                var (layer, y) = ChunkKey.OfRow(c.Y);
                Debug.Log($"[Chemistry] lockstep: tick {batch.Tick}: {c.By} cast {c.Spell} (charge {c.Charge}) at cell ({c.X}, {y}) in layer {layer}{SinceTeleport(c.By)}");
            }
            if (batch.Tick % SumEvery == 0)
            {
                var w = world;
                Debug.Log($"[Chemistry] lockstep {sessionRole} tick {batch.Tick} at {DateTime.Now:HH:mm:ss.f} sum {Fnv1a64.Hash(session.Checksum()):x16} " +
                          $"burning {burningCount} burnt {burntCount} ice {iceCount} behind {Behind} peers {(net != null ? net.Peers.Count : 0)} " +
                          $"filled {w.Filled.Count} active {w.ActiveKeys.Count} unpacked {w.UnpackedCount} sampled {sampled} ({sampleMs:F0} ms) " +
                          $"jobs {groundJobs} frame worst {frameWorstMs:F1} ms fills worst {fillWorstMs:F2} ms " +
                          $"mem {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} MB reserved gc {GC.GetTotalMemory(false) >> 20} MB " +
                          $"desync={session.DesyncSeen} desyncs={desyncs}");
                frameWorstMs = fillWorstMs = 0;
            }
        }

        /// <summary>Which layer a point is in: the cave layer in a cave's (or the sunken temple's) air well under the surface.</summary>
        static int LayerOf(in TerrainSource terrain, float3 p)
        {
            if (p.y > terrain.SampleHeight(p.xz) - CaveDepth)
                return SurfaceLayer;
            var air = p + new float3(0f, 0.8f, 0f);
            return terrain.CaveDistance(air) < 0f || terrain.TempleAir(air) ? CaveLayer : SurfaceLayer;
        }

        // Host and solo: every player's layer and cell (absolute, 1 m cells): each NetPlayer on a server, else the one here.
        List<(string who, int layer, int x, int y)> Characters()
        {
            var list = new List<(string, int, int, int)>();
            if (!TerrainSettled)
                return list;
            var server = NetWorlds.Server;
            var ecs = server != null && server.IsCreated && server.IsServer() ? server : EcsWorld.DefaultGameObjectInjectionWorld;
            if (ecs == null || !ecs.IsCreated)
                return list;
            if (playersWorld != ecs)
            {
                playersQuery = ecs.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(),
                    ecs.IsServer() ? ComponentType.ReadOnly<NetPlayer>() : ComponentType.ReadOnly<PlayerTag>());
                playersWorld = ecs;
            }
            using var ts = playersQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int k = 0; k < ts.Length; k++)
            {
                var p = ts[k].Position;
                list.Add(("player" + k, LayerOf(terrainNow, p), ChunkKey.CellOf(p.x, 1), ChunkKey.CellOf(p.z, 1)));
            }
            return list;
        }

        // Host and solo, -chem-sync-fills (or a chunk with no job under way): a chunk's ground sampled now, on the main thread.
        ChunkGround SampleChunk(ChunkKey key)
        {
            sampleWatch.Restart();
            var kinds = new NativeArray<byte>(Chunks.Area, Allocator.TempJob);
            GroundJobFor(key, kinds).Schedule(Chunks.Area, 256).Complete();
            var g = GroundOf(kinds);
            kinds.Dispose();
            sampled++;
            sampleMs += sampleWatch.Elapsed.TotalMilliseconds;
            return g;
        }

        GroundJob GroundJobFor(ChunkKey key, NativeArray<byte> kinds) =>
            new GroundJob { Source = terrainNow, Corner = new int2(key.X * Chunks.Size, key.Y * Chunks.Size), Cave = key.Layer != SurfaceLayer, Kinds = kinds };

        // A chunk's ground from its cells' kinds (GroundJob's).
        static ChunkGround GroundOf(NativeArray<byte> kinds)
        {
            var g = new ChunkGround("dirt");
            for (int i = 0; i < Chunks.Area; i++)
                switch (kinds[i])
                {
                    case KindGrass:
                        g.SetAt(i, "grass");
                        break;
                    case KindWater:
                        // A pool two height steps down, filled to just under the bank, so the water stays put.
                        g.SetAt(i, "dirt", 6, -2);
                        break;
                    case KindRock:
                        g.SetAt(i, "rock");
                        break;
                }
            return g;
        }

        /// <summary>The core's port over Punchy: where the players are and what the ground is (host and solo only).</summary>
        sealed class PunchyGround : IOpenGround
        {
            readonly ChemistryBridge bridge;
            public PunchyGround(ChemistryBridge bridge) => this.bridge = bridge;
            public IEnumerable<(string who, int layer, int x, int y)> Characters() => bridge.charactersNow ?? bridge.Characters();
            public ChunkGround Sample(ChunkKey key) => bridge.TakeGround(key);
        }

        /// <summary>
        /// A cave's floor under a point: the first cave air below <paramref name="from"/> (1 m steps, down to 80 m), then down
        /// through it to the rock. False where there's no cave under the point.
        /// </summary>
        static bool CaveFloor(in TerrainSource source, float2 xz, float from, out float floor)
        {
            floor = from;
            if (!source.Caves.IsCreated || !CaveSampling.NearColumn(ref source.Caves.Value, xz))
                return false;
            float y = from;
            int k = 0;
            for (; k < 80 && source.CaveDistance(new float3(xz.x, y, xz.y)) >= 0f; k++)
                y -= 1f;
            if (k == 80)
                return false;
            for (k = 0; k < 400 && source.CaveDistance(new float3(xz.x, y - 0.25f, xz.y)) < 0f; k++)
                y -= 0.25f;
            floor = y;
            return true;
        }

        /// <summary>
        /// A chunk's cells' kinds on the 1 m grid (at cell centres). The surface: grass where blades grow (as
        /// GroundDetailSystem decides), water, rock on steep slopes, dirt. A cave layer: its floor's water, dirt where
        /// there's a cave floor, rock elsewhere.
        /// </summary>
        [BurstCompile]
        struct GroundJob : IJobParallelFor
        {
            public TerrainSource Source;
            public int2 Corner;
            public bool Cave;
            [WriteOnly] public NativeArray<byte> Kinds;

            public void Execute(int i)
            {
                var xz = new float2(Corner.x + (i & (Chunks.Size - 1)) + 0.5f, Corner.y + (i >> Chunks.Shift) + 0.5f);
                if (Cave)
                {
                    if (!CaveFloor(Source, xz, Source.SampleHeight(xz) - 1f, out var floor))
                    {
                        Kinds[i] = KindRock;
                        return;
                    }
                    var pool = Source.SampleGround(new float3(xz.x, floor + 0.3f, xz.y));
                    Kinds[i] = pool.CaveWater && pool.WaterSurface > pool.Height + 0.1f ? KindWater : KindDirt;
                    return;
                }
                Kinds[i] = SurfaceKind(Source, xz, out _);
            }
        }

        /// <summary>A surface cell's kind, and its ground (or water surface) height.</summary>
        static byte SurfaceKind(in TerrainSource source, float2 xz, out float height)
        {
            var ground = source.SampleGround(xz);
            if (ground.HasWater && !ground.CaveWater && ground.WaterSurface > ground.Height + 0.1f)
            {
                height = ground.WaterSurface;
                return KindWater;
            }
            height = ground.Height;
            var normal = source.SampleNormal(xz, 1f);
            var surface = source.SampleSurface(xz, ground, normal);
            // As GroundDetailSystem decides where blades grow.
            var grass = surface.Grass * math.saturate(1f - surface.Snow * 3f) * math.smoothstep(0.78f, 0.9f, normal.y);
            return grass > 0.1f ? KindGrass : normal.y < 0.7f ? KindRock : KindDirt;
        }
    }
}
