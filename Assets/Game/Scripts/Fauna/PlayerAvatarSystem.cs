using Punchy.Player;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>Marks the creature that is the player's body (spawned by FaunaDirector from its Player Species).</summary>
    public struct PlayerAvatar : IComponentData { }

    /// <summary>
    /// Drives the player's creature body from player movement instead of a brain: it stands where the player is,
    /// faces where the player faces, and its gait advances by the distance covered, so any species (a human, a wolf)
    /// walks, runs and turns exactly like the animals built from the same parts. While airborne the gait holds and
    /// the body rises with the jump.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    public partial struct PlayerAvatarSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerAvatar>();
            state.RequireForUpdate<PlayerTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            LocalTransform player = default;
            PlayerMotion motion = default;
            foreach (var (t, m) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerMotion>>().WithAll<PlayerTag>())
            {
                player = t.ValueRO;
                motion = m.ValueRO;
            }
            var dt = math.max(SystemAPI.Time.DeltaTime, 1e-4f);
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward.x, forward.z);

            foreach (var (s, body) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithAll<PlayerAvatar>())
            {
                ref var b = ref body.ValueRO.Blob.Value;
                ref var c = ref s.ValueRW;
                var moved = math.distance(c.Position.xz, player.Position.xz);
                if (moved > 5f)
                    moved = 0f;   // teleported
                c.Position = player.Position;
                c.Mode = CreatureMode.Puppet;
                c.Clock += dt;

                var turn = WrapAngle(yaw - c.Yaw);
                c.Yaw = yaw;
                c.YawRate = turn / dt;
                c.Sway += (-c.YawRate * 0.35f - c.Sway) * math.min(1f, dt * 4f);

                // Speed from how far it actually went; the gait holds still in the air.
                var speed = motion.Grounded ? moved / dt : 0f;
                c.Speed += (speed - c.Speed) * math.min(1f, dt * 12f);
                if (motion.Grounded)
                {
                    var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), c.Speed / c.Scale);
                    var stride = math.lerp(b.WalkStride, b.RunStride, run) * c.Scale;
                    c.Phase = math.frac(c.Phase + moved / stride);
                }
            }
        }

        static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
    }
}
