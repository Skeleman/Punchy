using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Punchy.Net
{
    /// <summary>
    /// Logs every connection change (connecting, handshake, connected, disconnected and why) on both the server and the
    /// client, so a player log shows plainly whether a join worked.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    public partial struct ConnectionLogSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<NetworkStreamDriver>();

        public void OnUpdate(ref SystemState state)
        {
            var side = state.World.IsServer() ? "server" : "client";
            foreach (var e in SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick)
            {
                if (e.State == ConnectionState.State.Disconnected)
                    Debug.Log($"Net ({side}): connection {e.Id.Value} disconnected ({e.DisconnectReason})");
                else
                    Debug.Log($"Net ({side}): connection {e.Id.Value} {e.State}");
            }
        }
    }
}
