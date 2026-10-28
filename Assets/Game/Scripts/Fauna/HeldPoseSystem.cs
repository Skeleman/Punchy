using Punchy.Net;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Punchy.Fauna
{
    /// <summary>
    /// Clamps every carried animal to its carrier's arms as they're drawn, each frame: at the hands of the carrier's
    /// body (where the carry table puts them for that body, lowered with each step the body takes), lying across the
    /// forearms, turning as the carrier turns. The carrier's body is the one this machine draws (our own avatar, or
    /// another player's remote body), so the animal never lags behind them as its interpolated ghost would.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateBefore(typeof(CreatureRenderSystem))]
    public partial struct HeldPoseSystem : ISystem
    {
        ComponentLookup<GhostOwner> owners;
        ComponentLookup<NetPlayerLook> looks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureState>();
            owners = state.GetComponentLookup<GhostOwner>(true);
            looks = state.GetComponentLookup<NetPlayerLook>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            owners.Update(ref state);
            looks.Update(ref state);
            var table = SystemAPI.TryGetSingleton<CarryTable>(out var carryTable) ? carryTable : default;

            // The carriers' bodies here, by holder id: (state, playable species).
            var carriers = new NativeHashMap<int, (CreatureState body, int species)>(4, Allocator.Temp);
            var me = Interactions.HolderId(SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0);
            var mySpecies = SystemAPI.TryGetSingleton<LocalPlayerSpecies>(out var local) ? local.Index : -1;
            foreach (var body in SystemAPI.Query<RefRO<CreatureState>>().WithAll<PlayerAvatar>())
                carriers[me] = (body.ValueRO, mySpecies);
            foreach (var (body, remote) in SystemAPI.Query<RefRO<CreatureState>, RefRO<RemoteAvatar>>())
            {
                var ghost = remote.ValueRO.Ghost;
                if (owners.HasComponent(ghost))
                    carriers[Interactions.HolderId(owners[ghost].NetworkId)] = (body.ValueRO, looks.HasComponent(ghost) ? looks[ghost].Species : -1);
            }
            if (carriers.Count == 0)
                return;

            foreach (var (s, creature) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithNone<PlayerAvatar, RemoteAvatar>())
            {
                if (s.ValueRO.Mode != CreatureMode.Held || !carriers.TryGetValue(s.ValueRO.Holder, out var carrier))
                    continue;
                var at = Interactions.Hands(table, carrier.species, carrier.body.Position, carrier.body.Yaw,
                                            Interactions.HalfWidth(ref creature.ValueRO.Blob.Value, s.ValueRO.Scale));
                // (The body dips with each step; the arms and what they hold go with it.)
                if (carrier.species >= 0 && table.Blob.IsCreated && carrier.species < table.Blob.Value.Hold.Length)
                    at.y -= carrier.body.Scale * 0.04f * math.saturate(carrier.body.Speed / 2f) *
                            (0.5f - 0.5f * math.cos(4f * math.PI * carrier.body.Phase));
                s.ValueRW.Position = at;
                s.ValueRW.Yaw = carrier.body.Yaw + math.PI * 0.5f;
                s.ValueRW.YawRate = 0f;
            }
        }
    }
}
