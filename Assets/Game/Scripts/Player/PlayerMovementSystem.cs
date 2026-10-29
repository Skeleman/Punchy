using Punchy.Flora;
using Punchy.Terrain;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Punchy.Player
{
    /// <summary>
    /// Moves the player across the height field: camera-relative walking/sprinting, jumping and gravity, at the pace
    /// of the body they're in. A flying body takes off with a jump and flies by the same rules as the birds
    /// (CreatureMotion): it steers where the camera looks, climbs (jump held) or dives (sprint button held) at most 35°,
    /// never slows below its minimum airspeed, glides when it isn't beating its wings, and flares to land.
    /// Ground height comes straight from TerrainSource, so the player never depends on which chunks are loaded; and the
    /// walkable tops of props near the player (PropSupport: rocks, roofs, rims, benches) are ground too: stepped up onto
    /// when low (StepUp), walls when too high, slid off when steeper than 45°, and climbed onto when within reach (a
    /// mantle: up at the wall to the rim's height, then over onto the top) by bodies that can climb.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.LocalSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInputSystem))]
    [UpdateBefore(typeof(TerrainStreamingSystem))]
    public partial struct PlayerMovementSystem : ISystem
    {
        /// <summary>A walk (the walk toggle) for a human body, m/s; other bodies by their speed next to a human.</summary>
        public const float Stroll = 1.6f;

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
            var body = SystemAPI.TryGetSingleton<PlayerBody>(out var playerBody) ? playerBody : new PlayerBody { SpeedFactor = 1f };
            SystemAPI.TryGetSingletonBuffer<FloraObstacle>(out var obstacles, true);
            var hasProps = SystemAPI.TryGetSingletonBuffer<FloraSupport>(out var props, true);
            var hasGrids = SystemAPI.TryGetSingleton<SupportGrids>(out var grids);
            var tops = hasProps && hasGrids && grids.Set.IsCreated;
            var size = body.Size > 0f ? body.Size : 1f;
            var stepUp = StepUp * size;

            foreach (var (transform, motor, motion) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerMotor>, RefRW<PlayerMotion>>().WithAll<PlayerTag>())
            {
                ref var t = ref transform.ValueRW;
                ref var m = ref motion.ValueRW;
                var settings = motor.ValueRO;

                var position = t.Position;
                var swimming = Floor(terrain, position, settings.SwimDepth, out _, out var inCave);

                // A flying body: off with a jump, and on the wing until it comes down on something.
                if (!body.Flies)
                    m.Flying = false;
                else if (!m.Flying && input.JumpPressed && (m.Grounded || swimming))
                {
                    m.Flying = true;
                    m.Grounded = false;
                    m.Airspeed = body.FlySpeed * 0.6f;
                    m.Pitch = 0.5f;
                }
                if (m.Flying)
                {
                    Fly(ref t, ref m, body, input, terrain, settings, inCave, dt);
                    continue;
                }
                // Climbing onto a ledge: hands on the rim, pulled up at the wall to its height, then a leg over and onto
                // the top.
                if (m.Mantle > 0f)
                {
                    m.Mantle += dt / MantleTime;
                    var u = math.saturate(m.Mantle);
                    var up = new float3(m.MantleFrom.x, m.MantleTo.y, m.MantleFrom.z);
                    t.Position = u < 0.6f ? math.lerp(m.MantleFrom, up, math.smoothstep(0f, 1f, u / 0.6f))
                                          : math.lerp(up, m.MantleTo, math.smoothstep(0f, 1f, (u - 0.6f) / 0.4f));
                    if (u >= 1f)
                    {
                        m.Mantle = 0f;
                        m.Grounded = true;
                        m.OnProp = true;
                        m.VerticalVelocity = 0f;
                    }
                    continue;
                }

                var pace = input.Walk ? Stroll : settings.WalkSpeed * (input.Sprint ? settings.SprintMultiplier : 1f);
                var speed = pace * math.max(body.SpeedFactor, 0.1f) * (swimming ? settings.SwimSpeedMultiplier : 1f);
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
                    position.xz = SlideAroundObstacles(obstacles, position, settings.Radius, stepUp);
                if (inCave)
                    position.xz = KeepOutOfCaveWalls(terrain, from, position.xz, settings.Radius);
                // Props' tops too high to step onto are walls (a boulder, a fountain's rim from outside): slid along.
                var blocked = false;
                if (tops)
                    position.xz = KeepOutOfPropWalls(ref grids.Set.Value, props, from, position.xz, stepUp, 1.7f * size, out blocked);

                // A ledge within reach ahead: climbed onto, for a jump at it (or reaching it in the air, pressing on).
                var pressing = math.lengthsq(input.MoveDirection) > 0.04f;
                if (tops && body.Climbs && !swimming && pressing && (input.JumpPressed || !m.Grounded && blocked) &&
                    PropSupport.Ledge(ref grids.Set.Value, props, from, math.normalize(input.MoveDirection), 0.9f * size, stepUp,
                                      MantleReach * size, out var ledge))
                {
                    m.Mantle = 1e-4f;
                    m.MantleFrom = from;
                    m.MantleTo = ledge.Edge - new float3(ledge.EdgeNormal.x, 0f, ledge.EdgeNormal.y) * (settings.Radius + 0.15f);
                    m.MantleTo.y = ledge.Height;
                    m.Grounded = false;
                    m.VerticalVelocity = 0f;
                    t.Position = from;
                    var face = -ledge.EdgeNormal;
                    t.Rotation = quaternion.LookRotationSafe(new float3(face.x, 0f, face.y), math.up());
                    continue;
                }

                if (m.Grounded && input.JumpPressed)
                {
                    m.VerticalVelocity = settings.JumpSpeed;
                    m.Grounded = false;
                }

                Floor(terrain, position, settings.SwimDepth, out var ground, out _);
                // Standing on a prop's top, if it's higher than the ground there; slid off it where it's steep.
                m.OnProp = false;
                if (tops && PropSupport.Sample(ref grids.Set.Value, props, position, settings.Radius * 0.6f, stepUp, out var top) && top.Height > ground)
                {
                    ground = top.Height;
                    m.OnProp = true;
                    if (m.Grounded && top.Normal.y < SlideSlope && math.lengthsq(top.Normal.xz) > 1e-6f)
                        position.xz += math.normalize(top.Normal.xz) * SlideSpeed * dt;
                }
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

        /// <summary>
        /// A frame on the wing: turn towards where the camera looks (banking round, not snapping), climb or dive within
        /// the birds' limits, beat the wings to keep speed up or glide down slowly, and come down with a flare.
        /// </summary>
        static void Fly(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in TerrainSource terrain,
                        in PlayerMotor settings, bool inCave, float dt)
        {
            var forward3 = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward3.x, forward3.z);
            var wanted = math.atan2(input.CameraForward.x, input.CameraForward.y);
            var turn = math.atan2(math.sin(wanted - yaw), math.cos(wanted - yaw));
            yaw += math.clamp(turn, -2.2f * dt, 2.2f * dt);

            var steepest = math.atan(Punchy.Fauna.CreatureMotion.MaxClimb);
            var dive = input.DiveHeld;
            var climb = input.JumpHeld && !dive;
            m.Flapping = climb || math.lengthsq(input.MoveDirection) > 0.01f;
            Floor(terrain, t.Position, settings.SwimDepth, out var ground, out _);
            var height = t.Position.y - ground;
            // Nose: up to climb, down to dive; held level beating on, or sinking gently gliding. Near the ground on the
            // way down, it flares: nose up, slowing to land.
            var targetPitch = climb ? steepest : dive ? -steepest : m.Flapping ? 0f : -0.12f;
            var flaring = !climb && height < Punchy.Fauna.CreatureMotion.FlareDistance * 1.5f && m.Pitch <= 0.05f;
            if (flaring)
                targetPitch = math.max(targetPitch, -0.15f);
            m.Pitch += math.clamp(targetPitch - m.Pitch, -1.5f * dt, 1.5f * dt);

            // Speed: wingbeats bring it up to flying speed; a glide bleeds it off slowly (faster climbing, gaining
            // diving), never below the minimum, except in the last moments of a landing.
            var flySpeed = math.max(body.FlySpeed, 1f);
            var lowest = flySpeed * Punchy.Fauna.CreatureMotion.MinAirspeed * (flaring ? 0.5f : 1f);
            if (m.Flapping)
                m.Airspeed += (flySpeed - m.Airspeed) * math.min(1f, dt * 1.5f);
            m.Airspeed -= math.sin(m.Pitch) * 9.81f * 0.5f * dt;
            m.Airspeed = math.clamp(m.Airspeed - (m.Flapping ? 0f : 0.15f * dt), lowest, flySpeed * 1.6f);

            var velocity = new float3(math.sin(yaw) * math.cos(m.Pitch), math.sin(m.Pitch), math.cos(yaw) * math.cos(m.Pitch)) * m.Airspeed;
            var position = t.Position + velocity * dt;
            // Under rock (a cave's ceiling or walls): no further that way, and the nose goes down.
            if (inCave && terrain.CaveDistance(position + new float3(0f, settings.Radius, 0f)) > -settings.Radius)
            {
                position = t.Position;
                m.Pitch = math.min(m.Pitch, -0.2f);
            }
            Floor(terrain, position, settings.SwimDepth, out ground, out _);
            if (position.y <= ground)
            {
                // Down: on its feet (or swimming), walking again.
                position.y = ground;
                m.Flying = false;
                m.Grounded = true;
                m.VerticalVelocity = 0f;
                m.Pitch = 0f;
            }
            t.Position = position;
            t.Rotation = quaternion.RotateY(yaw);
        }

        /// <summary>The highest a body of human size steps up onto without a climb (m).</summary>
        const float StepUp = 0.4f;
        /// <summary>The highest ledge (above the feet) a body of human size climbs onto, and how long it takes (s).</summary>
        const float MantleReach = 1.8f, MantleTime = 0.8f;
        /// <summary>Steeper than this (a surface normal's up part: 45°) a top slides you off, at this speed (m/s).</summary>
        const float SlideSlope = 0.707f, SlideSpeed = 3f;

        /// <summary>
        /// A move into a prop's top too high to step onto is cut back to whichever of its X and Z parts is clear (sliding
        /// along it), or cancelled; blocked says it was stopped at all.
        /// </summary>
        static float2 KeepOutOfPropWalls(ref SupportGridSet set, in DynamicBuffer<FloraSupport> props, float3 from, float2 to,
                                         float stepUp, float head, out bool blocked)
        {
            blocked = PropSupport.Wall(ref set, props, new float3(to.x, from.y, to.y), stepUp, head, out _);
            if (!blocked)
                return to;
            var slideX = new float2(to.x, from.z);
            if (!PropSupport.Wall(ref set, props, new float3(slideX.x, from.y, slideX.y), stepUp, head, out _))
                return slideX;
            var slideZ = new float2(from.x, to.y);
            return !PropSupport.Wall(ref set, props, new float3(slideZ.x, from.y, slideZ.y), stepUp, head, out _) ? slideZ : from.xz;
        }

        /// <summary>
        /// Pushes the player out of any trunk or cactus they walked into, so they slide around it (not a building's walls
        /// when they're up on its roof: anything topping out below a step up from the feet is passed over).
        /// </summary>
        static float2 SlideAroundObstacles(in DynamicBuffer<FloraObstacle> obstacles, float3 position, float radius, float stepUp)
        {
            var xz = position.xz;
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                if (position.y + stepUp > o.Position.y + o.Height || position.y + 1.8f < o.Position.y)
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
