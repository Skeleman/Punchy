using Unity.Entities;
using Unity.NetCode;
using UnityEngine.Scripting;

namespace Punchy.Net
{
    /// <summary>
    /// Creates the netcode worlds: a server world (authoritative simulation) and a client world (the local player's
    /// view, input and everything drawn). The stock bootstrap makes whichever world it creates first the default
    /// injection world, which would be the server; MonoBehaviours (camera, weather, ambience, debug controls) must talk
    /// to the client world, so it's made the default here. Server code finds its world through <see cref="NetWorlds"/>.
    /// </summary>
    [Preserve]
    public class GameBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            if (!DetermineIfBootstrappingEnabled())
                return false;
            CreateDefaultClientServerWorlds();
            if (ClientWorld != null)
                World.DefaultGameObjectInjectionWorld = ClientWorld;
            return true;
        }
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
