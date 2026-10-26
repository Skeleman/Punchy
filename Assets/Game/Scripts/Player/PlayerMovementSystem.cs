using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Player
{
    /// <summary>
    /// Moves the player across the height field: camera-relative walking/sprinting, jumping and gravity.
    /// Ground height comes straight from TerrainSource, so the player never depends on which chunks are loaded.
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
            state.RequireForUpdate<TerrainSource>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var dt = SystemAPI.Time.DeltaTime;
            var input = SystemAPI.GetSingleton<PlayerInput>();
            var terrain = SystemAPI.GetSingleton<TerrainSource>();
            SystemAPI.TryGetSingletonBuffer<FloraObstacle>(out var obstacles, true);

            foreach (var (transform, motor, motion) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerMotor>, RefRW<PlayerMotion>>().WithAll<PlayerTag>())
            {
                ref var t = ref transform.ValueRW;
                ref var m = ref motion.ValueRW;
                var settings = motor.ValueRO;

                var position = t.Position;
                var swimming = Floor(terrain, position.xz, settings.SwimDepth, out _);
                var speed = settings.WalkSpeed * (input.Sprint ? settings.SprintMultiplier : 1f) *
                            (swimming ? settings.SwimSpeedMultiplier : 1f);
                position.xz += input.MoveDirection * speed * dt;
                if (obstacles.IsCreated)
                    position.xz = SlideAroundObstacles(obstacles, position, settings.Radius);

                if (m.Grounded && input.JumpPressed)
                {
                    m.VerticalVelocity = settings.JumpSpeed;
                    m.Grounded = false;
                }

                Floor(terrain, position.xz, settings.SwimDepth, out var ground);
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

        /// <summary>Pushes the player out of any trunk or cactus they walked into, so they slide around it.</summary>
        static float2 SlideAroundObstacles(in DynamicBuffer<FloraObstacle> obstacles, float3 position, float radius)
        {
            var xz = position.xz;
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                if (position.y > o.Position.y + o.Height || position.y + 1.8f < o.Position.y)
                    continue;
                var away = xz - o.Position.xz;
                var minDistance = o.Radius + radius;
                var distanceSq = math.lengthsq(away);
                if (distanceSq < minDistance * minDistance)
                    xz = o.Position.xz + (distanceSq > 1e-8f ? away * math.rsqrt(distanceSq) : new float2(1f, 0f)) * minDistance;
            }
            return xz;
        }

        /// <summary>
        /// What the player stands on: the ground, or in water deeper than SwimDepth, a point SwimDepth below the
        /// surface (so they swim with their head above water). Returns whether they're swimming.
        /// </summary>
        static bool Floor(in TerrainSource terrain, float2 xz, float swimDepth, out float floor)
        {
            var ground = terrain.SampleGround(xz);
            floor = ground.Height;
            if (!ground.HasWater || ground.WaterSurface - swimDepth <= ground.Height)
                return false;
            floor = ground.WaterSurface - swimDepth;
            return true;
        }
    }
}
