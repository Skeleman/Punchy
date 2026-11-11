using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// Network settings for every connection we make: Netcode's defaults, but a crashed peer is given up on after
    /// <see cref="DisconnectTimeoutMS"/> (P-4: back to 30 s from 10 s: the island bake blocks the main thread ~6 s,
    /// and with start-up loading a host's own link or a baking joiner went past 10 s and was dropped).
    /// </summary>
    public static class NetDrivers
    {
        public const int DisconnectTimeoutMS = 30000;

        public static NetworkSettings Client() => WithTimeout(DefaultDriverBuilder.GetNetworkClientSettings());
        public static NetworkSettings Server() => WithTimeout(DefaultDriverBuilder.GetNetworkServerSettings());

        static NetworkSettings WithTimeout(NetworkSettings settings)
        {
            var p = settings.GetNetworkConfigParameters();
            p.disconnectTimeoutMS = DisconnectTimeoutMS;
            settings.AddRawParameterStruct(ref p);
            return settings;
        }

        /// <summary>
        /// A joiner's UDP driver (a LAN join or Relay; settings already made, e.g. with Relay's): the stock one, or with
        /// "-netsim" the same driver whose pipelines run through Transport's simulator stage (<see cref="NetSim"/>).
        /// </summary>
        public static void RegisterClientUdp(World world, ref NetworkDriverStore store, NetDebug netDebug, NetworkSettings settings)
        {
            if (!NetSim.On)
            {
                DefaultDriverBuilder.RegisterClientUdpDriver(world, ref store, netDebug, settings);
                return;
            }
            NetSim.AddStage(ref settings);
            // (As DefaultDriverBuilder.CreateClientSimulatorPipelines, which only exists in editor/NETCODE_DEBUG builds:
            // the same three pipelines in the same order, each ending in the simulator.)
            var instance = new NetworkDriverStore.NetworkDriverInstance { driver = NetworkDriver.Create(new UDPNetworkInterface(), settings) };
            instance.unreliablePipeline = instance.driver.CreatePipeline(typeof(SimulatorPipelineStage));
            instance.reliablePipeline = instance.driver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            instance.unreliableFragmentedPipeline = instance.driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(SimulatorPipelineStage));
            store.RegisterDriver(TransportType.Socket, instance);
        }

        /// <summary>The stock drivers (in-process link plus UDP), with our settings; used for worlds made at startup.</summary>
        public class Constructor : INetworkStreamDriverConstructor
        {
            // (As DefaultDriverBuilder.RegisterClientDriver: the in-process link when our own server is here, else UDP.)
            public void CreateClientDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug)
            {
                var server = ClientServerBootstrap.ServerWorld;
                if (ClientServerBootstrap.RequestedPlayType != ClientServerBootstrap.PlayType.Client && server != null && server.IsCreated)
                    DefaultDriverBuilder.RegisterClientIpcDriver(world, ref driver, netDebug, Client());
                else
                    RegisterClientUdp(world, ref driver, netDebug, Client());
            }

            public void CreateServerDriver(World world, ref NetworkDriverStore driver, NetDebug netDebug)
            {
                if (NetSim.On)
                    Debug.Log("netsim: hosting: the host's drivers are left as they are (a joiner's -netsim lags both directions)");
                DefaultDriverBuilder.RegisterServerDriver(world, ref driver, netDebug, Server());
            }
        }
    }

    /// <summary>
    /// P-31: "-netsim &lt;delay ms&gt;[,&lt;jitter ms&gt;[,&lt;loss %&gt;]]" (e.g. -netsim 150,30,2): a joiner's game traffic
    /// runs through Unity Transport's simulator pipeline stage, both ways: each packet it sends or receives is held
    /// back by the delay (give or take the jitter) and that share of them is dropped, so the round trip is about twice
    /// the delay. For two-player tests on one machine, where packets otherwise arrive at once. Netcode's own editor
    /// simulator works the same way (a client-side stage on every pipeline), and the connection handshake is left
    /// alone: Transport's global simulator, which also lagged the handshake, failed 2 joins in 4 at 150,30,2. A host
    /// may be given the same flag (it only logs); the joiner's stage covers both directions. Off without the flag;
    /// nothing else changes. Works in player builds (no define or ProjectSettings change).
    /// </summary>
    public static class NetSim
    {
        public static readonly bool On;
        public static readonly int DelayMS, JitterMS, LossPercent;
        /// <summary>Packets the stage can hold per pipeline (Netcode's editor simulator: 300); 150 ms each way of
        /// snapshots and RPCs fits well within it.</summary>
        const int MaxPackets = 1000;

        static NetSim()
        {
            var args = Punchy.Debugging.DebugControls.LaunchArgs;
            int at = System.Array.IndexOf(args, "-netsim");
            if (at < 0 || at + 1 >= args.Length)
                return;
            var parts = args[at + 1].Split(',');
            var n = CultureInfo.InvariantCulture;
            if (!int.TryParse(parts[0], NumberStyles.Integer, n, out DelayMS) || DelayMS < 0)
            {
                Debug.LogWarning($"netsim: '{args[at + 1]}' is not <delay ms>[,<jitter ms>[,<loss %>]]; off");
                DelayMS = 0;
                return;
            }
            if (parts.Length > 1)
                int.TryParse(parts[1], NumberStyles.Integer, n, out JitterMS);
            if (parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, n, out var loss))
                LossPercent = Mathf.RoundToInt(loss);
            JitterMS = Mathf.Clamp(JitterMS, 0, DelayMS);
            LossPercent = Mathf.Clamp(LossPercent, 0, 100);
            On = true;
            Debug.Log($"netsim: delay {DelayMS} ms, jitter {JitterMS}, loss {LossPercent}%");
        }

        /// <summary>Adds the simulator stage's parameters (both directions) to a joiner's driver settings.</summary>
        public static void AddStage(ref NetworkSettings settings) =>
            settings.WithSimulatorStageParameters(MaxPackets, NetworkParameterConstants.MaxMessageSize, ApplyMode.AllPackets,
                packetDelayMs: DelayMS, packetJitterMs: JitterMS, packetDropPercentage: LossPercent);
    }
}
