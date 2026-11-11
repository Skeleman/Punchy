using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// P-31: a joined client's round-trip time to the host, as Netcode estimates it, logged every few seconds while
    /// "-netsim" or "-rttlog" is given ("netsim: rtt 302 ms (deviation 14)"), so a lag test can check the simulator
    /// took effect. Nothing runs without either flag.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class RttLogSystem : SystemBase
    {
        const float Every = 5f;
        float next;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
            Enabled = NetSim.On || System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-rttlog") >= 0;
        }

        protected override void OnUpdate()
        {
            var now = UnityEngine.Time.unscaledTime;
            if (now < next)
                return;
            next = now + Every;
            foreach (var ack in SystemAPI.Query<RefRO<NetworkSnapshotAck>>().WithAll<NetworkId>())
                Debug.Log($"netsim: rtt {ack.ValueRO.EstimatedRTT:0} ms (deviation {ack.ValueRO.DeviationRTT:0})");
        }
    }
}
