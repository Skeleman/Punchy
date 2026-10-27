using Punchy.Net;
using Punchy.Player;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Fauna
{
    /// <summary>Marks the creature that is the player's body (spawned by FaunaDirector from its Player Species).</summary>
    public struct PlayerAvatar : IComponentData { }

    /// <summary>A creature body standing in for another player (spawned by NetAvatars); Ghost is their NetPlayer.</summary>
    public struct RemoteAvatar : IComponentData
    {
        public Entity Ghost;
    }

    /// <summary>
    /// Drives the player's creature body from player movement instead of a brain: it stands where the player is,
    /// faces where the player faces, and its gait advances by the distance covered, so any species (a human, a wolf)
    /// walks, runs and turns exactly like the animals built from the same parts. While airborne the gait holds and
    /// the body rises with the jump. Other players' bodies are driven the same way from their (interpolated)
    /// NetPlayer ghosts.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMovementSystem))]
    public partial struct PlayerAvatarSystem : ISystem
    {
        ComponentLookup<LocalTransform> transforms;
        ComponentLookup<NetPlayerLook> looks;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CreatureBody>();
            transforms = state.GetComponentLookup<LocalTransform>(true);
            looks = state.GetComponentLookup<NetPlayerLook>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = math.max(SystemAPI.Time.DeltaTime, 1e-4f);
            LocalTransform player = default;
            PlayerMotion motion = default;
            var hasPlayer = false;
            foreach (var (t, m) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PlayerMotion>>().WithAll<PlayerTag>())
            {
                player = t.ValueRO;
                motion = m.ValueRO;
                hasPlayer = true;
            }
            if (hasPlayer)
                foreach (var (s, body) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithAll<PlayerAvatar>())
                    Drive(ref s.ValueRW, ref body.ValueRO.Blob.Value, player, motion.Grounded, dt);

            transforms.Update(ref state);
            looks.Update(ref state);
            foreach (var (s, body, remote) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>, RefRO<RemoteAvatar>>())
            {
                var ghost = remote.ValueRO.Ghost;
                if (!transforms.HasComponent(ghost) || !looks.HasComponent(ghost))
                    continue;
                Drive(ref s.ValueRW, ref body.ValueRO.Blob.Value, transforms[ghost], looks[ghost].Grounded, dt);
            }
        }

        static void Drive(ref CreatureState c, ref CreatureBlob b, in LocalTransform player, bool grounded, float dt)
        {
            var forward = math.mul(player.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward.x, forward.z);
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
            var speed = grounded ? moved / dt : 0f;
            c.Speed += (speed - c.Speed) * math.min(1f, dt * 12f);
            if (grounded)
            {
                var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), c.Speed / c.Scale);
                var stride = math.lerp(b.WalkStride, b.RunStride, run) * c.Scale;
                c.Phase = math.frac(c.Phase + moved / stride);
            }
        }

        static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
    }
}
