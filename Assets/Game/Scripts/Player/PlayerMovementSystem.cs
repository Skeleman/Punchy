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
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
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
                var swimming = Floor(terrain, position, settings.SwimDepth, out _, out var inCave);
                var speed = settings.WalkSpeed * (input.Sprint ? settings.SprintMultiplier : 1f) *
                            (swimming ? settings.SwimSpeedMultiplier : 1f);
                var from = position;
                position.xz += input.MoveDirection * speed * dt;
                if (swimming)
                {
                    // Rivers carry swimmers downstream: harder in rapids, but never faster than they can swim, so
                    // they can always fight their way to the bank.
                    var water = terrain.SampleGround(position.xz);
                    var current = math.min(settings.RiverCurrent * (1f + water.Rapids), speed * 0.8f);
                    position.xz += water.Flow * current * dt;
                }
                if (obstacles.IsCreated)
                    position.xz = SlideAroundObstacles(obstacles, position, settings.Radius);
                if (inCave)
                    position.xz = KeepOutOfCaveWalls(terrain, from, position.xz, settings.Radius);

                if (m.Grounded && input.JumpPressed)
                {
                    m.VerticalVelocity = settings.JumpSpeed;
                    m.Grounded = false;
                }

                Floor(terrain, position, settings.SwimDepth, out var ground, out _);
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
        /// In a cave the walls stop the player: a move that would put their body in rock is cut back to whichever
        /// of its X and Z parts is clear (so they slide along the wall), or cancelled.
        /// </summary>
        static float2 KeepOutOfCaveWalls(in TerrainSource terrain, float3 from, float2 to, float radius)
        {
            if (Clear(terrain, from, to, radius))
                return to;
            var slideX = new float2(to.x, from.z);
            if (Clear(terrain, from, slideX, radius))
                return slideX;
            var slideZ = new float2(from.x, to.y);
            return Clear(terrain, from, slideZ, radius) ? slideZ : from.xz;
        }

        static bool Clear(in TerrainSource terrain, float3 from, float2 xz, float radius)
        {
            var floor = terrain.FloorUnder(new float3(xz.x, from.y, xz.y), out var inCave);
            // Out of the entrance onto open ground: fine unless it's a step up past the knees.
            if (!inCave)
                return terrain.SampleHeight(xz) <= from.y + 0.6f;
            // A wall, or the floor rising too steeply.
            if (floor > from.y + 0.6f)
                return false;
            var body = new float3(xz.x, math.max(floor, from.y) + 1f, xz.y);
            return terrain.CaveDistance(body) < -radius * 0.8f || !terrain.IsRock(body);
        }

        /// <summary>
        /// What the player stands on: the ground (or a cave floor), or in water deeper than SwimDepth, a point
        /// SwimDepth below the surface (so they swim with their head above water). Returns whether they're swimming.
        /// </summary>
        static bool Floor(in TerrainSource terrain, float3 position, float swimDepth, out float floor, out bool inCave)
        {
            floor = terrain.FloorUnder(position, out inCave);
            if (inCave)
                return false;
            // On a bridge: the deck, not the water under it.
            if (terrain.Deck(position, out floor))
            {
                floor = math.max(floor, terrain.SampleHeight(position.xz));
                return false;
            }
            var ground = terrain.SampleGround(position.xz);
            floor = ground.Height;
            if (!ground.HasWater || ground.WaterSurface - swimDepth <= ground.Height)
                return false;
            floor = ground.WaterSurface - swimDepth;
            return true;
        }
    }
}
