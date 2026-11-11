using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// P-31: a client's round-trip time to the host, as Netcode estimates it, logged every few seconds while "-netsim"
    /// or "-rttlog" is given ("netsim: rtt 302 ms (deviation 14)"), so a lag test can check the simulator took effect.
    /// With it, how smoothly players move here: the biggest step any player's body took in one frame, and how many
    /// frames were snaps (a step over <see cref="SnapMetres"/>, or over <see cref="FastMetres"/> and faster than
    /// <see cref="SnapSpeed"/>: at a few hundred frames a second a plain walking step is already "fast"), for our own
    /// player and for the others apart. Nothing runs without either flag.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RttLogSystem : SystemBase
    {
        const float Every = 5f;
        /// <summary>A frame's step counts as a snap past SnapMetres, or past FastMetres at over SnapSpeed (m/s; a sprint
        /// is under 10).</summary>
        const float SnapMetres = 1f, FastMetres = 0.3f, SnapSpeed = 25f;
        float next;
        readonly Dictionary<Entity, float3> last = new Dictionary<Entity, float3>();
        readonly HashSet<Entity> seen = new HashSet<Entity>();
        float ownMax, otherMax;
        int ownSnaps, otherSnaps;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkId>();
            Enabled = NetSim.On || System.Array.IndexOf(Punchy.Debugging.DebugControls.LaunchArgs, "-rttlog") >= 0;
        }

        protected override void OnUpdate()
        {
            var dt = math.max(UnityEngine.Time.unscaledDeltaTime, 1e-4f);
            seen.Clear();
            foreach (var (t, e) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<NetPlayer>().WithEntityAccess())
            {
                seen.Add(e);
                var p = t.ValueRO.Position;
                if (last.TryGetValue(e, out var was))
                {
                    var step = math.distance(p, was);
                    var own = EntityManager.HasComponent<GhostOwnerIsLocal>(e) && EntityManager.IsComponentEnabled<GhostOwnerIsLocal>(e);
                    var snap = step > SnapMetres || (step > FastMetres && step / dt > SnapSpeed);
                    if (own)
                    {
                        ownMax = math.max(ownMax, step);
                        ownSnaps += snap ? 1 : 0;
                    }
                    else
                    {
                        otherMax = math.max(otherMax, step);
                        otherSnaps += snap ? 1 : 0;
                    }
                }
                last[e] = p;
            }
            if (last.Count != seen.Count)
            {
                var gone = new List<Entity>();
                foreach (var e in last.Keys)
                    if (!seen.Contains(e))
                        gone.Add(e);
                foreach (var e in gone)
                    last.Remove(e);
            }

            var now = UnityEngine.Time.unscaledTime;
            if (now < next)
                return;
            next = now + Every;
            foreach (var ack in SystemAPI.Query<RefRO<NetworkSnapshotAck>>().WithAll<NetworkId>())
                Debug.Log($"netsim: rtt {ack.ValueRO.EstimatedRTT:0} ms (deviation {ack.ValueRO.DeviationRTT:0}); " +
                          $"players {seen.Count}: own max step {ownMax:0.00} m, snaps {ownSnaps}; others max step {otherMax:0.00} m, snaps {otherSnaps}");
            ownMax = otherMax = 0f;
            ownSnaps = otherSnaps = 0;
        }
    }
}
