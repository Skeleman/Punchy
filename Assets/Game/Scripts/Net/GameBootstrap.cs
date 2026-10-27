using System;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace Punchy.Net
{
    /// <summary>
    /// Creates the netcode worlds: a server world (authoritative simulation) and a client world (the local player's
    /// view, input and everything drawn). By default this machine hosts: it listens on <see cref="Port"/> and its own
    /// client connects over loopback. Launching with <c>-join &lt;ip&gt;</c> creates only a client that joins that
    /// host instead (and Multiplayer Play Mode's virtual players join the editor's host the same way).
    /// The stock bootstrap makes whichever world it creates first the default injection world, which would be the
    /// server; MonoBehaviours (camera, weather, ambience, debug controls) must talk to the client world, so it's made
    /// the default here. Server code finds its world through <see cref="NetWorlds"/>.
    /// </summary>
    [Preserve]
    public class GameBootstrap : ClientServerBootstrap
    {
        public const ushort Port = 7979;

        public override bool Initialize(string defaultWorldName)
        {
            if (!DetermineIfBootstrappingEnabled())
                return false;
            AutoConnectPort = Port;
            var join = JoinAddressFromCommandLine();
            if (join != null && NetworkEndpoint.TryParse(join, Port, out var endpoint))
            {
                DefaultConnectAddress = endpoint;
                CreateClientWorld("ClientWorld");
            }
            else
                CreateDefaultClientServerWorlds();
            MakeClientDefault();
            return true;
        }

        static string JoinAddressFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-join")
                    return args[i + 1];
            return null;
        }

        static void MakeClientDefault()
        {
            var client = NetWorlds.Client;
            if (client != null)
                World.DefaultGameObjectInjectionWorld = client;
        }

        /// <summary>Drops the current game and joins the host at an address (keeps the scene, rebuilds the worlds).</summary>
        public static void Join(NetworkEndpoint host)
        {
            DisposeNetWorlds();
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
            DisposeNetWorlds();
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

        static void DisposeNetWorlds()
        {
            for (int i = World.All.Count - 1; i >= 0; i--)
            {
                var world = World.All[i];
                if (world.IsClient() || world.IsServer())
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
