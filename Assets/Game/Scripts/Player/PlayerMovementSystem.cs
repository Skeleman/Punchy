using Punchy.Terrain;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Player
{
    /// <summary>
    /// Moves the player across the height field: camera-relative walking/sprinting, jumping and gravity.
    /// Ground height comes straight from TerrainHeightSource, so the player never depends on which chunks are loaded.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    [UpdateBefore(typeof(TerrainStreamingSystem))]
    public partial struct PlayerMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerInput>();
            state.RequireForUpdate<TerrainHeightSource>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            var input = SystemAPI.GetSingleton<PlayerInput>();
            var terrain = SystemAPI.GetSingleton<TerrainHeightSource>();

            foreach (var (transform, motor, motion) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerMotor>, RefRW<PlayerMotion>>().WithAll<PlayerTag>())
            {
                ref var t = ref transform.ValueRW;
                ref var m = ref motion.ValueRW;
                var settings = motor.ValueRO;

                var speed = settings.WalkSpeed * (input.Sprint ? settings.SprintMultiplier : 1f);
                var position = t.Position;
                position.xz += input.MoveDirection * speed * dt;

                if (m.Grounded && input.JumpPressed)
                {
                    m.VerticalVelocity = settings.JumpSpeed;
                    m.Grounded = false;
                }

                var ground = terrain.SampleHeight(position.xz);
                if (m.Grounded && position.y - ground <= settings.GroundSnapDistance + speed * dt)
                {
                    // Walking: follow the ground up and down slopes.
                    position.y = ground;
                    m.VerticalVelocity = 0f;
                }
                else
                {
                    m.VerticalVelocity -= settings.Gravity * dt;
                    position.y += m.VerticalVelocity * dt;
                    m.Grounded = position.y <= ground;
                    if (m.Grounded)
                    {
                        position.y = ground;
                        m.VerticalVelocity = 0f;
                    }
                }

                t.Position = position;

                var flatMove = new float3(input.MoveDirection.x, 0f, input.MoveDirection.y);
                if (math.lengthsq(flatMove) > 1e-4f)
                {
                    var target = quaternion.LookRotationSafe(flatMove, math.up());
                    t.Rotation = math.slerp(t.Rotation, target, 1f - math.exp(-settings.TurnSharpness * dt));
                }
            }
        }
    }
}
