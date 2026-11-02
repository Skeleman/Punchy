using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;

namespace Punchy.Net
{
    /// <summary>
    /// Quiets netcode's "Server Tick Batching" warning (an editor-only check) to when a host's server really falls
    /// behind: under a frame-rate cap of 30 or 40 a host runs one batch of two ticks a frame, which keeps game time
    /// (the commands are absolute poses, so batching is fine), but the default threshold (1.2 ticks a frame) warned
    /// every frame or two. At 2.5 it speaks up only below about 24 fps, where game time does slow. Set once, then off.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct BatchedTickWarningSystem : ISystem
    {
        const float WarnAbove = 2.5f;

        [BurstCompile]
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<NetDebug>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SystemAPI.GetSingletonRW<NetDebug>().ValueRW.WarnAboveAverageBatchedTicksPerFrame = WarnAbove;
            state.Enabled = false;
        }
    }
}
