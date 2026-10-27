using System;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Punchy.Net
{
    /// <summary>
    /// Creates the game's worlds. Playing alone (the default) there's just one local world with no networking, as
    /// before multiplayer, so solo play pays nothing for it. Hosting (F2, or <c>-host</c>) swaps in a server world
    /// (authoritative simulation, listening on <see cref="Port"/>) plus a client world (the local player's view, input
    /// and everything drawn) connected to it in-process; <c>-join &lt;ip&gt;</c> creates only a client that joins
    /// that host (and Multiplayer Play Mode's virtual players join the editor's host the same way).
    /// The stock bootstrap makes whichever world it creates first the default injection world, which would be the
    /// server; MonoBehaviours (camera, weather, ambience, debug controls) must talk to the client world, so it's made
    /// the default here. Server code finds its world through <see cref="NetWorlds"/>.
    /// </summary>
    [Preserve]
    public class GameBootstrap : ClientServerBootstrap
    {
        public const ushort Port = 7979;

        /// <summary>Where the local player should be put once the reloaded scene has a player (see NetMenu).</summary>
        public enum Placement { None, KeepPosition, NearHost }
        public static Placement PendingPlacement;
        public static Unity.Mathematics.float3 CarriedPosition;

        /// <summary>The host we're joining (for the F2 panel), and since when; negative once in game or not joining.</summary>
        public static string JoiningTo;
        public static float JoiningSince = -1f;

        static void StartedJoining(string to)
        {
            JoiningTo = to;
            JoiningSince = Time.unscaledTime;
            Debug.Log($"GameBootstrap: joining {to}");
        }

        public override bool Initialize(string defaultWorldName)
        {
            if (!DetermineIfBootstrappingEnabled())
                return false;
            NetworkStreamReceiveSystem.DriverConstructor = new NetDrivers.Constructor();
            var join = JoinAddressFromCommandLine();
            if (join != null && NetworkEndpoint.TryParse(join, Port, out var endpoint))
            {
                // Connect by hand: auto-connect goes by the build's play type (client and server), so it would swap
                // the host's address for loopback.
                AutoConnectPort = 0;
                PendingPlacement = Placement.NearHost;
                var client = CreateClientWorld("ClientWorld");
                using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
                    query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, endpoint);
                StartedJoining(endpoint.ToString());
            }
            else if (HasArgument("-joincode"))
            {
                // An online join replaces the worlds as soon as Relay answers; just a client until then.
                AutoConnectPort = 0;
                CreateClientWorld("ClientWorld");
            }
            else if (HasArgument("-host") || RequestedPlayType == PlayType.Client)
            {
                // Host on the LAN straight away (or, as a Multiplayer Play Mode virtual player, join the editor).
                AutoConnectPort = Port;
                CreateDefaultClientServerWorlds();
            }
            else
            {
                AutoConnectPort = 0;
                CreateLocalWorld(defaultWorldName);
                return true;
            }
            MakeClientDefault();
            return true;
        }

        /// <summary>Playing alone, with no networking at all.</summary>
        public static bool Solo
        {
            get
            {
                var world = World.DefaultGameObjectInjectionWorld;
                return world != null && world.IsCreated && !world.IsClient() && !world.IsServer();
            }
        }

        /// <summary>Drops any network game and carries on alone where we stand, without networking.</summary>
        public static void PlaySolo()
        {
            JoiningSince = -1f;
            PendingPlacement = Placement.KeepPosition;
            DisposeGameWorlds();
            AutoConnectPort = 0;
            CreateLocalWorld("Default World");
            ReloadScene();
        }

        static string JoinAddressFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-join")
                    return args[i + 1];
            return null;
        }

        static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static void MakeClientDefault()
        {
            var client = NetWorlds.Client;
            if (client != null)
                World.DefaultGameObjectInjectionWorld = client;
        }

        /// <summary>Drops the current game and joins the host at an address (keeps the scene, rebuilds the worlds).</summary>
        public static void Join(NetworkEndpoint host)
        {
            StartedJoining(host.ToString());
            PendingPlacement = Placement.NearHost;
            DisposeGameWorlds();
            AutoConnectPort = 0;
            var client = CreateClientWorld("ClientWorld");
            World.DefaultGameObjectInjectionWorld = client;
            using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, host);
            ReloadScene();
        }

        /// <summary>Drops the current game and hosts a new one on <see cref="Port"/>.</summary>
        public static void Host()
        {
            JoiningSince = -1f;
            PendingPlacement = Placement.KeepPosition;
            DisposeGameWorlds();
            AutoConnectPort = 0;
            var server = CreateServerWorld("ServerWorld");
            var client = CreateClientWorld("ClientWorld");
            World.DefaultGameObjectInjectionWorld = client;
            using (var query = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(NetworkEndpoint.AnyIpv4.WithPort(Port));
            using (var query = client.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
                query.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(client.EntityManager, NetworkEndpoint.LoopbackIpv4.WithPort(Port));
            ReloadScene();
        }

        /// <summary>
        /// Drops the current game and hosts a new one reachable through Unity Relay (friends join with a code): the
        /// server listens both on an in-process link for our own client and on the Relay allocation.
        /// </summary>
        public static void HostRelay(RelayServerData relay)
        {
            JoiningSince = -1f;
            PendingPlacement = Placement.KeepPosition;
            DisposeGameWorlds();
            AutoConnectPort = 0;
            var server = CreateServerWorld("ServerWorld");
            var client = CreateClientWorld("ClientWorld");
            World.DefaultGameObjectInjectionWorld = client;

            var em = server.EntityManager;
            using (var debug = em.CreateEntityQuery(ComponentType.ReadOnly<NetDebug>()))
            using (var driverQuery = em.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                var netDebug = debug.GetSingleton<NetDebug>();
                var store = new NetworkDriverStore();
                DefaultDriverBuilder.RegisterServerIpcDriver(server, ref store, netDebug, NetDrivers.Server());
                var relaySettings = NetDrivers.Server();
                relaySettings.WithRelayParameters(ref relay);
                DefaultDriverBuilder.RegisterServerUdpDriver(server, ref store, netDebug, relaySettings);
                ref var driver = ref driverQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                driver.ResetDriverStore(server.Unmanaged, ref store);
                driver.Listen(NetworkEndpoint.AnyIpv4.WithPort(Port));
            }
            // Our own client reaches the server in-process (Relay is only for the others).
            var cem = client.EntityManager;
            using (var debug = cem.CreateEntityQuery(ComponentType.ReadOnly<NetDebug>()))
            using (var driverQuery = cem.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                var store = new NetworkDriverStore();
                DefaultDriverBuilder.RegisterClientIpcDriver(client, ref store, debug.GetSingleton<NetDebug>(), NetDrivers.Client());
                ref var driver = ref driverQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                driver.ResetDriverStore(client.Unmanaged, ref store);
                driver.Connect(cem, NetworkEndpoint.LoopbackIpv4.WithPort(Port));
            }
            ReloadScene();
        }

        /// <summary>Drops the current game and joins a host through Unity Relay.</summary>
        public static void JoinRelay(RelayServerData relay)
        {
            StartedJoining("an online game");
            PendingPlacement = Placement.NearHost;
            DisposeGameWorlds();
            AutoConnectPort = 0;
            var client = CreateClientWorld("ClientWorld");
            World.DefaultGameObjectInjectionWorld = client;
            var em = client.EntityManager;
            using (var debug = em.CreateEntityQuery(ComponentType.ReadOnly<NetDebug>()))
            using (var driverQuery = em.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>()))
            {
                var settings = NetDrivers.Client();
                settings.WithRelayParameters(ref relay);
                var store = new NetworkDriverStore();
                DefaultDriverBuilder.RegisterClientUdpDriver(client, ref store, debug.GetSingleton<NetDebug>(), settings);
                ref var driver = ref driverQuery.GetSingletonRW<NetworkStreamDriver>().ValueRW;
                driver.ResetDriverStore(client.Unmanaged, ref store);
                driver.Connect(em, relay.Endpoint);
            }
            ReloadScene();
        }

        /// <summary>Raised just before the worlds are replaced, while the old game is still intact.</summary>
        public static event Action WorldsReplacing;

        static void DisposeGameWorlds()
        {
            WorldsReplacing?.Invoke();
            // Remember where the local player was, so hosting again (or the host leaving) doesn't send them home.
            var client = NetWorlds.Client;
            if (client != null && client.IsCreated)
                using (var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Punchy.Player.PlayerTag>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()))
                    if (q.TryGetSingleton<Unity.Transforms.LocalTransform>(out var t))
                        CarriedPosition = t.Position;
            var solo = Solo ? World.DefaultGameObjectInjectionWorld : null;
            for (int i = World.All.Count - 1; i >= 0; i--)
            {
                var world = World.All[i];
                if (world.IsClient() || world.IsServer() || world == solo)
                    world.Dispose();
            }
        }

        // The subscene and every MonoBehaviour bind to the worlds as the scene loads, so reload it onto the new ones.
        static void ReloadScene() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Finds the netcode worlds, for MonoBehaviours and tools that need a specific one.</summary>
    public static class NetWorlds
    {
        /// <summary>The local player's world: the client world, or the single local world without netcode.</summary>
        public static World Client
        {
            get
            {
                foreach (var world in World.All)
                    if (world.IsClient() && !world.IsThinClient())
                        return world;
                return World.DefaultGameObjectInjectionWorld;
            }
        }

        /// <summary>The authoritative world: the server world when this machine hosts, the local world without netcode,
        /// or null on a client that only joined someone else's game.</summary>
        public static World Server
        {
            get
            {
                foreach (var world in World.All)
                    if (world.IsServer())
                        return world;
                var local = World.DefaultGameObjectInjectionWorld;
                return local != null && !local.IsClient() ? local : null;
            }
        }
    }
}
