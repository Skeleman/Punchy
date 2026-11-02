using Punchy.Net;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Punchy.Fauna
{
    /// <summary>
    /// Clamps every carried animal to its carrier's arms as they're drawn, each frame. First the carrier's body is told
    /// how wide the animal is (CreatureState.CarryWidth), so its arms open round it. Then the body is posed as the
    /// renderer will pose it, and the animal goes between its two hands, lying across the forearms and turning as the
    /// carrier turns, so it moves with the arms (a step's dip, a sway) rather than sitting at a fixed spot. A body
    /// without two arms falls back to where the carry table puts it. The carrier's body is the one this machine draws
    /// (our own avatar, or another player's remote body), so the animal never lags behind them as its interpolated
    /// ghost would. All of it is worked out from what every machine already has: nothing is sent.
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

            // How wide what each carrier holds is, by holder id (the widest, if somehow two).
            var widths = new NativeHashMap<int, float>(4, Allocator.Temp);
            foreach (var (s, creature) in SystemAPI.Query<RefRO<CreatureState>, RefRO<CreatureBody>>().WithNone<PlayerAvatar, RemoteAvatar>())
                if (s.ValueRO.Mode == CreatureMode.Held)
                {
                    var half = Interactions.HalfWidth(ref creature.ValueRO.Blob.Value, s.ValueRO.Scale);
                    widths[s.ValueRO.Holder] = widths.TryGetValue(s.ValueRO.Holder, out var was) ? math.max(was, half) : half;
                }

            // The carriers' bodies here, by holder id (state, body and playable species), each told how wide its load is.
            var carriers = new NativeHashMap<int, (CreatureState body, CreatureBody shape, int species)>(4, Allocator.Temp);
            var me = Interactions.HolderId(SystemAPI.TryGetSingleton<NetworkId>(out var id) ? id.Value : 0);
            var mySpecies = SystemAPI.TryGetSingleton<LocalPlayerSpecies>(out var local) ? local.Index : -1;
            foreach (var (body, shape) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithAll<PlayerAvatar>())
            {
                body.ValueRW.CarryWidth = widths.TryGetValue(me, out var w) ? w : 0f;
                carriers[me] = (body.ValueRO, shape.ValueRO, mySpecies);
            }
            foreach (var (body, shape, remote) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>, RefRO<RemoteAvatar>>())
            {
                var ghost = remote.ValueRO.Ghost;
                if (!owners.HasComponent(ghost))
                    continue;
                var holder = Interactions.HolderId(owners[ghost].NetworkId);
                body.ValueRW.CarryWidth = widths.TryGetValue(holder, out var w) ? w : 0f;
                carriers[holder] = (body.ValueRO, shape.ValueRO, looks.HasComponent(ghost) ? looks[ghost].Species : -1);
            }
            if (carriers.Count == 0 || widths.Count == 0)
                return;
            var hasTerrain = SystemAPI.TryGetSingleton<TerrainSource>(out var terrain);

            foreach (var (s, creature) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithNone<PlayerAvatar, RemoteAvatar>())
            {
                if (s.ValueRO.Mode != CreatureMode.Held || !carriers.TryGetValue(s.ValueRO.Holder, out var carrier))
                    continue;
                var half = Interactions.HalfWidth(ref creature.ValueRO.Blob.Value, s.ValueRO.Scale);
                var forward = new float3(math.sin(carrier.body.Yaw), 0f, math.cos(carrier.body.Yaw));
                float3 at;
                if (hasTerrain && Hands(ref carrier.shape.Blob.Value, carrier.body, carrier.shape.Coat, terrain, out var right, out var left))
                    // (Between the hands, a little back from them along the forearms, where it lies.)
                    at = (right + left) * 0.5f - forward * half * 0.3f;
                else
                {
                    at = Interactions.Hands(table, carrier.species, carrier.body.Position, carrier.body.Yaw, half);
                    // (The body dips with each step; the arms and what they hold go with it.)
                    if (carrier.species >= 0 && table.Blob.IsCreated && carrier.species < table.Blob.Value.Hold.Length)
                        at.y -= carrier.body.Scale * 0.04f * math.saturate(carrier.body.Speed / 2f) *
                                (0.5f - 0.5f * math.cos(4f * math.PI * carrier.body.Phase));
                }
                s.ValueRW.Position = at;
                s.ValueRW.Yaw = carrier.body.Yaw + math.PI * 0.5f;
                s.ValueRW.YawRate = 0f;
            }
        }

        /// <summary>
        /// Where a body's two hands are as the renderer poses it this frame (the ends of its right and left arms); false
        /// for a body without an arm on each side.
        /// </summary>
        static bool Hands(ref CreatureBlob b, in CreatureState s, int coat, in TerrainSource terrain, out float3 right, out float3 left)
        {
            right = left = default;
            int r = -1, l = -1;
            for (int p = 0; p < b.Parts.Length; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Arm || part.SegmentCount == 0)
                    continue;
                if (part.Side > 0f && r < 0)
                    r = p;
                else if (part.Side < 0f && l < 0)
                    l = p;
            }
            if (r < 0 || l < 0)
                return false;
            var output = new NativeArray<SegmentInstance>(b.Segments.Length + 64, Allocator.Temp);
            CreatureAnimator.Evaluate(ref b, s, coat, terrain, output, 0, false);
            right = Tip(output, ref b.Parts[r]);
            left = Tip(output, ref b.Parts[l]);
            output.Dispose();
            return true;
        }

        static float3 Tip(NativeArray<SegmentInstance> output, ref CreaturePart part)
        {
            var seg = output[part.FirstSegment + part.SegmentCount - 1];
            return seg.Position + math.mul(seg.Rotation, new float3(0f, 0f, seg.Length));
        }
    }
}
