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
            var pose = SystemAPI.TryGetSingleton<InteractionRequest>(out var interaction) ? interaction.Pose : IdleAction.None;
            if (hasPlayer)
                foreach (var (s, body) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>>().WithAll<PlayerAvatar>())
                {
                    Drive(ref s.ValueRW, ref body.ValueRO.Blob.Value, player, motion.Grounded, dt);
                    // On the wing: wings out, nose up or down with the climb.
                    Wings(ref s.ValueRW, ref body.ValueRO.Blob.Value, motion.Flying, motion.Pitch, motion.Flapping, dt);
                    Pose(ref s.ValueRW, ref body.ValueRO.Blob.Value, pose, dt);
                }

            transforms.Update(ref state);
            looks.Update(ref state);
            foreach (var (s, body, remote) in SystemAPI.Query<RefRW<CreatureState>, RefRO<CreatureBody>, RefRO<RemoteAvatar>>())
            {
                var ghost = remote.ValueRO.Ghost;
                if (!transforms.HasComponent(ghost) || !looks.HasComponent(ghost))
                    continue;
                ref var b = ref body.ValueRO.Blob.Value;
                // (Another player's flying body: off the ground and well above it. The climb from how it rose.)
                var was = s.ValueRO.Position;
                Drive(ref s.ValueRW, ref b, transforms[ghost], looks[ghost].Grounded, dt);
                var flying = b.Habitat == Habitat.Flies && !looks[ghost].Grounded;
                var run = math.distance(was.xz, s.ValueRO.Position.xz);
                var pitch = run > 1e-4f ? math.atan2(s.ValueRO.Position.y - was.y, run) : 0f;
                Wings(ref s.ValueRW, ref b, flying, pitch, pitch > 0.1f, dt);
                Pose(ref s.ValueRW, ref b, (IdleAction)looks[ghost].Pose, dt);
            }
        }

        /// <summary>
        /// Eases into an interaction pose (petting, carrying) and holds it half way through the action (where its
        /// weight is full), then eases out of it when it's over.
        /// </summary>
        static void Pose(ref CreatureState c, ref CreatureBlob b, IdleAction pose, float dt)
        {
            // An animal body bows as animals do: a play bow, chest down and rump up (its other emotes are a wag of the
            // tail and a toss of the head, from the same actions).
            if (!b.Upright)
                c.Bow += ((pose == IdleAction.Bow ? 1f : 0f) - c.Bow) * math.min(1f, dt * 6f);
            if (pose != IdleAction.None)
            {
                if (c.Action != pose)
                {
                    c.Action = pose;
                    c.ActionTime = 0f;
                }
                // (The action's weight is full from 0.12 to 0.85 of the way through: a quarter second in, the same out.)
                c.ActionTime = math.min(c.ActionTime + dt * 0.5f, 0.5f);
            }
            else if (c.Action != IdleAction.None)
            {
                c.ActionTime = math.max(c.ActionTime, 0.85f) + dt * 0.6f;
                if (c.ActionTime >= 1f)
                {
                    c.Action = IdleAction.None;
                    c.ActionTime = 0f;
                }
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

        /// <summary>
        /// A body on the wing: eases into flight (wings out, legs tucked), pitched with the climb or dive, and beats its
        /// wings or holds them spread by the flight rules (CreatureMotion), as a bird the brain flies would.
        /// </summary>
        static void Wings(ref CreatureState c, ref CreatureBlob b, bool flying, float pitch, bool flapping, float dt)
        {
            if (b.Habitat != Habitat.Flies)
                return;
            c.Fly += ((flying ? 1f : 0f) - c.Fly) * math.min(1f, dt * 6f);
            if (c.Fly < 0.05f)
                return;
            // (Vertical > 0.15 reads as climbing, which beats the wings.)
            var vertical = math.clamp(pitch / (math.PI * 0.47f), -0.5f, 0.5f) + (flapping ? 0.16f : 0f);
            c.Vertical += (vertical * c.Fly - c.Vertical) * math.min(1f, dt * 5f);
            if (c.Fly > 0.5f)
                CreatureMotion.AdvancePhase(ref c, ref b, dt);
        }

        static float WrapAngle(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));
    }
}
