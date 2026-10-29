using System.Collections.Generic;
using Punchy.Fauna;
using Punchy.Terrain;
using Punchy.Weather;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>The host's sky: time of day and the whole weather simulation's state (one ghost, always sent).</summary>
    public struct WorldState : IComponentData
    {
        [GhostField(Quantization = 10000)] public float TimeOfDay;
        [GhostField] public int DayType;
        [GhostField(Quantization = 100)] public float WindAngle;
        [GhostField(Quantization = 100)] public float PressureTime;
        [GhostField(Quantization = 10)] public float2 PressureOffset;
        [GhostField(Quantization = 100000)] public float2 CloudOffset;
        /// <summary>Whole days since the start: with the time of day it sets both moons' phases.</summary>
        [GhostField] public int Day;
    }

    /// <summary>One of the host's weather cells (WeatherField.CellState).</summary>
    public struct WeatherCellState : IBufferElementData
    {
        [GhostField(Quantization = 10)] public float2 Position;
        [GhostField(Quantization = 100)] public float2 Drift;
        [GhostField(Quantization = 10)] public float Radius;
        [GhostField(Quantization = 10)] public float Age, Grow, Hold, Fade;
        [GhostField] public int Preset;
    }

    /// <summary>Points at the WorldState ghost prefab; baked into the world subscene.</summary>
    public struct WorldStateSpawner : IComponentData
    {
        public Entity Prefab;
    }

    /// <summary>Host → everyone: a lightning strike, so all players see and hear the same bolt.</summary>
    public struct LightningRpc : IRpcCommand
    {
        public float3 Position;
    }

    /// <summary>Client → host on joining: a fingerprint of the world it built, so a mismatched one is turned away.</summary>
    public struct WorldCheckRpc : IRpcCommand
    {
        public uint Signature;
    }

    /// <summary>
    /// Keeps every player under the same sky. On the host it publishes the day/night cycle and the weather field
    /// (cells, pressure pattern, wind, day type, cloud drift) on a WorldState ghost, and sends lightning as RPCs; on
    /// a client that joined someone else it puts WeatherSystem in remote mode and copies that state in every frame.
    /// It also checks that joining clients built the same world (seed, biomes, landmarks, species tables) and
    /// disconnects them if not.
    /// </summary>
    [DefaultExecutionOrder(-20)]   // before DayNightCycle and WeatherSystem read the time and cells
    public class WorldSync : MonoBehaviour
    {
        public DayNightCycle Cycle;
        public WeatherSystem Weather;
        public NetAvatars Avatars;

        /// <summary>Testing: mixed into this machine's world fingerprint, to check that a mismatched client is refused.</summary>
        public static uint TestSalt;

        readonly List<WeatherField.CellState> cells = new List<WeatherField.CellState>();

        // The sky as it was when the worlds were last replaced, so hosting again (or carrying on alone after the
        // host leaves) keeps the time and weather instead of starting the day over. Statics survive the scene reload.
        static WorldState carried;
        static readonly List<WeatherField.CellState> carriedCells = new List<WeatherField.CellState>();
        static bool hasCarried;

        void OnEnable() => GameBootstrap.WorldsReplacing += CarrySky;
        readonly HashSet<Entity> checkedConnections = new HashSet<Entity>();
        World server, client;
        Entity stateGhost;
        bool subscribed;

        void OnDisable()
        {
            GameBootstrap.WorldsReplacing -= CarrySky;
            if (Weather != null)
            {
                Weather.Remote = false;
                if (subscribed) Weather.Struck -= OnStruck;
            }
            subscribed = false;
        }

        void Update()
        {
            var s = NetWorlds.Server;
            var c = NetWorlds.Client;
            if (s != server || c != client)
            {
                server = s;
                client = c;
                stateGhost = Entity.Null;
                checkedConnections.Clear();
            }
            if (Weather != null && !subscribed)
            {
                Weather.Struck += OnStruck;
                subscribed = true;
            }
            Shader.SetGlobalFloat(ClockId, SharedClock());
            var hosting = server != null && server.IsCreated && server.IsServer();
            var joined = !hosting && client != null && client.IsCreated && client.IsClient();
            if (Weather != null)
                Weather.Remote = joined;

            if (hasCarried && (joined || RestoreSky()))
                hasCarried = false;   // a joined player takes the host's sky instead
            if (hosting)
            {
                EnsureStateGhost();
                CheckJoiners();
            }
            if (client != null && client.IsCreated && client.IsClient())
            {
                SendWorldCheck();
                ReceiveLightning(joined);
                if (joined)
                    ApplyHostState();
            }
        }

        void LateUpdate()
        {
            if (server != null && server.IsCreated && server.IsServer() && stateGhost != Entity.Null)
                PublishState();
        }

        void CarrySky()
        {
            if (Weather == null || !Weather.Field.Ready)
                return;
            carried = new WorldState
            {
                TimeOfDay = Cycle != null ? Cycle.TimeOfDay : 12f,
                Day = Cycle != null ? Cycle.Day : 0,
                DayType = Weather.DayTypeIndex,
                WindAngle = Weather.WindAngle,
                PressureTime = Weather.Field.PressureTime,
                PressureOffset = Weather.Field.PressureOffset,
                CloudOffset = Weather.CloudOffset
            };
            Weather.Field.ExportCells(carriedCells);
            hasCarried = true;
        }

        /// <summary>Puts the carried sky back once the new weather field is ready; false until then.</summary>
        bool RestoreSky()
        {
            if (Weather == null || !Weather.Field.Ready)
                return false;
            if (Cycle != null)
            {
                Cycle.TimeOfDay = carried.TimeOfDay;
                Cycle.Day = carried.Day;
            }
            if (Weather.DayTypeIndex != carried.DayType)
                Weather.SetDayType(carried.DayType, false);
            Weather.WindAngle = carried.WindAngle;
            Weather.CloudOffset = carried.CloudOffset;
            Weather.Field.PressureTime = carried.PressureTime;
            Weather.Field.PressureOffset = carried.PressureOffset;
            Weather.Field.ImportCells(carriedCells);
            Debug.Log($"WorldSync: carried the sky over ({(int)carried.TimeOfDay:00}:{(int)(carried.TimeOfDay % 1f * 60f):00}, {carriedCells.Count} weather cells)");
            return true;
        }

        static readonly int ClockId = Shader.PropertyToID("_PunchyClock");

        /// <summary>
        /// Seconds by the server's tick, which every connected player agrees on, so ambient motion driven by it
        /// (fireflies, butterflies, motes) matches between screens. Time since start when not in a game.
        /// </summary>
        float SharedClock()
        {
            if (client == null || !client.IsCreated || !client.IsClient())
                return Time.time;
            var em = client.EntityManager;
            using var time = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            if (!time.TryGetSingleton<NetworkTime>(out var t) || !t.ServerTick.IsValid)
                return Time.time;
            using var rate = em.CreateEntityQuery(ComponentType.ReadOnly<ClientServerTickRate>());
            var hz = rate.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? r.SimulationTickRate : 60;
            return (float)((t.ServerTick.TickIndexForValidTick + (double)t.ServerTickFraction) / hz);
        }

        // ------------------------------------------------------------------------------------------- host

        void EnsureStateGhost()
        {
            var em = server.EntityManager;
            if (stateGhost != Entity.Null && em.Exists(stateGhost))
                return;
            using var spawner = em.CreateEntityQuery(ComponentType.ReadOnly<WorldStateSpawner>());
            if (!spawner.TryGetSingleton<WorldStateSpawner>(out var s))
                return;
            stateGhost = em.Instantiate(s.Prefab);
            em.SetName(stateGhost, "WorldState");
        }

        void PublishState()
        {
            var em = server.EntityManager;
            if (!em.Exists(stateGhost) || Weather == null)
                return;
            var field = Weather.Field;
            em.SetComponentData(stateGhost, new WorldState
            {
                TimeOfDay = Cycle != null ? Cycle.TimeOfDay : 12f,
                Day = Cycle != null ? Cycle.Day : 0,
                DayType = Weather.DayTypeIndex,
                WindAngle = Weather.WindAngle,
                PressureTime = field.Ready ? field.PressureTime : 0f,
                PressureOffset = field.Ready ? (float2)field.PressureOffset : float2.zero,
                CloudOffset = Weather.CloudOffset
            });
            var buffer = em.GetBuffer<WeatherCellState>(stateGhost);
            buffer.Clear();
            if (!field.Ready)
                return;
            field.ExportCells(cells);
            foreach (var c in cells)
                buffer.Add(new WeatherCellState
                {
                    Position = c.Position, Drift = c.Drift, Radius = c.Radius,
                    Age = c.Age, Grow = c.Grow, Hold = c.Hold, Fade = c.Fade, Preset = c.Preset
                });
        }

        void OnStruck(Vector3 position)
        {
            if (server == null || !server.IsCreated)
                return;
            var em = server.EntityManager;
            var rpc = em.CreateEntity(typeof(LightningRpc), typeof(SendRpcCommandRequest));
            em.SetComponentData(rpc, new LightningRpc { Position = position });
            // No target connection: sent to everyone.
        }

        /// <summary>Turns away clients whose world differs from ours.</summary>
        void CheckJoiners()
        {
            var em = server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<WorldCheckRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            if (query.IsEmpty)
                return;
            var ours = Signature(server);
            var requests = query.ToEntityArray(Allocator.Temp);
            foreach (var r in requests)
            {
                var theirs = em.GetComponentData<WorldCheckRpc>(r).Signature;
                var connection = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                if (ours != 0 && theirs != ours && em.Exists(connection))
                {
                    Debug.LogWarning($"WorldSync: a player built a different world ({theirs:X8}, ours {ours:X8}): seed, biomes or species differ. Disconnecting them.");
                    em.AddComponent<NetworkStreamRequestDisconnect>(connection);
                }
                else if (em.Exists(connection))
                    Debug.Log($"WorldSync: player {em.GetComponentData<NetworkId>(connection).Value} has the same world ({theirs:X8}); welcome");
                em.DestroyEntity(r);
            }
            requests.Dispose();
        }

        // ------------------------------------------------------------------------------------------- client

        void SendWorldCheck()
        {
            var em = client.EntityManager;
            using var connections = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            var list = connections.ToEntityArray(Allocator.Temp);
            foreach (var connection in list)
            {
                if (checkedConnections.Contains(connection))
                    continue;
                var signature = Signature(client);
                if (signature == 0)
                    continue;   // world not loaded yet
                checkedConnections.Add(connection);
                Debug.Log($"WorldSync: in game as player {em.GetComponentData<NetworkId>(connection).Value}; sending world check {signature:X8}");
                var rpc = em.CreateEntity(typeof(WorldCheckRpc), typeof(SendRpcCommandRequest));
                em.SetComponentData(rpc, new WorldCheckRpc { Signature = signature });
                em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            list.Dispose();
        }

        void ReceiveLightning(bool apply)
        {
            var em = client.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<LightningRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            if (query.IsEmpty)
                return;
            var strikes = query.ToEntityArray(Allocator.Temp);
            foreach (var e in strikes)
            {
                // The host already saw its own strike; only joined players need it.
                if (apply && Weather != null)
                    Weather.Strike(em.GetComponentData<LightningRpc>(e).Position);
                em.DestroyEntity(e);
            }
            strikes.Dispose();
        }

        void ApplyHostState()
        {
            var em = client.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<WorldState>(), ComponentType.ReadOnly<WeatherCellState>());
            if (!query.TryGetSingletonEntity<WorldState>(out var ghost))
                return;
            var state = em.GetComponentData<WorldState>(ghost);
            if (Cycle != null)
            {
                Cycle.TimeOfDay = state.TimeOfDay;
                Cycle.Day = state.Day;
            }
            if (Weather == null)
                return;
            if (Weather.DayTypeIndex != state.DayType)
                Weather.SetDayType(state.DayType, false);
            Weather.WindAngle = state.WindAngle;
            Weather.CloudOffset = state.CloudOffset;
            var field = Weather.Field;
            if (!field.Ready)
                return;
            field.PressureTime = state.PressureTime;
            field.PressureOffset = state.PressureOffset;
            cells.Clear();
            foreach (var c in em.GetBuffer<WeatherCellState>(ghost))
                cells.Add(new WeatherField.CellState
                {
                    Position = c.Position, Drift = c.Drift, Radius = c.Radius,
                    Age = c.Age, Grow = c.Grow, Hold = c.Hold, Fade = c.Fade, Preset = c.Preset
                });
            field.ImportCells(cells);
        }

        // ------------------------------------------------------------------------------------------- world fingerprint

        /// <summary>
        /// A hash of everything a world is generated from and everything the network refers to by index: the world
        /// settings (seed, size, sea level), the biome and landmark lists, the creature species table and the
        /// playable species table. 0 while the world isn't loaded.
        /// </summary>
        uint Signature(World world)
        {
            var em = world.EntityManager;
            using var terrain = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainSource>());
            using var palettes = em.CreateEntityQuery(ComponentType.ReadOnly<BiomePalette>());
            if (!terrain.TryGetSingleton<TerrainSource>(out var source) || palettes.CalculateEntityCount() != 1)
                return 0;
            var palette = em.GetComponentObject<BiomePalette>(palettes.GetSingletonEntity());
            if (palette == null || palette.Biomes == null)
                return 0;
            var hash = 2166136261u;
            void Add(string s)
            {
                foreach (var ch in s ?? "-")
                    hash = (hash ^ ch) * 16777619u;
                hash = (hash ^ 0x1F) * 16777619u;
            }
            Add(source.World.Seed.ToString());
            Add(source.World.WorldSize.ToString("R"));
            Add(source.World.SeaLevel.ToString("R"));
            var species = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var b in palette.Biomes)
            {
                Add(b != null ? b.name : null);
                if (b != null)
                    foreach (var f in b.Fauna)
                        if (f.Species != null) species.Add(f.Species.name);
            }
            foreach (var l in palette.Landmarks ?? System.Array.Empty<LandmarkType>())
            {
                Add(l != null ? l.name : null);
                if (l != null)
                    foreach (var r in l.Residents)
                        if (r.Species != null) species.Add(r.Species.name);
            }
            // (The settlements' animals and hunters too: FaunaDirector's table is exactly these names, sorted, so this
            // fixes every creature index the network sends.)
            var domestics = Resources.Load<Punchy.Fauna.DomesticTable>("Domestics");
            if (domestics != null)
                foreach (var d in domestics.Entries)
                    if (d.Species != null) species.Add(d.Species.name);
            foreach (var s in species)
                Add(s);
            if (Avatars != null)
                foreach (var s in Avatars.PlayableSpecies)
                    Add(s != null ? s.name : null);
            if (TestSalt != 0 && world.IsClient())
                Add(TestSalt.ToString());
            return hash == 0 ? 1u : hash;
        }
    }
}
