using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

namespace Punchy.Net
{
    /// <summary>
    /// Network settings for every connection we make: Netcode's defaults, but a crashed peer is given up on after
    /// <see cref="DisconnectTimeoutMS"/> instead of 30 s, so a player whose host vanishes carries on alone sooner.
    /// </summary>
    public static class NetDrivers
    {
        public const int DisconnectTimeoutMS = 10000;

        public static NetworkSettings Client() => WithTimeout(DefaultDriverBuilder.GetNetworkClientSettings());
        public static NetworkSettings Server() => WithTimeout(DefaultDriverBuilder.GetNetworkServerSettings());

        static NetworkSettings WithTimeout(NetworkSettings settings)
        {
            var p = settings.GetNetworkConfigParameters();
            p.disconnectTimeoutMS = DisconnectTimeoutMS;
            settings.AddRawParameterStruct(ref p);
            return settings;
        }

        /// <summary>The stock drivers (in-process link plus UDP), with our settings; used for worlds made at startup.</summary>
        public class Constructor : INetworkStreamDriverConstructor
        {
            public void CreateClientDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug) =>
                DefaultDriverBuilder.RegisterClientDriver(world, ref driver, netDebug, Client());

            public void CreateServerDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug) =>
                DefaultDriverBuilder.RegisterServerDriver(world, ref driver, netDebug, Server());
        }
    }
}
