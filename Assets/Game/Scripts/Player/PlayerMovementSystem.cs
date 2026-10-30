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
                    var hasFlora = SystemAPI.TryGetSingleton<Punchy.Flora.FloraLookup>(out var flora) && flora.Blob.IsCreated;
                    Fly(ref t, ref m, body, input, terrain, settings, inCave, dt, obstacles, hasFlora ? flora.Blob : default);
                    continue;
                }
                // A bird on its perch stays put until it walks off it or takes off.
                if (m.Perched)
                {
                    if (math.lengthsq(input.MoveDirection) < 0.01f && !input.JumpPressed)
                    {
                        m.Grounded = true;
                        m.VerticalVelocity = 0f;
                        continue;
                    }
                    m.Perched = false;
                }
                if (body.Hovers)
                {
                    Hover(ref t, ref m, body, input, terrain, settings, dt);
                    continue;
                }
                if (body.Swims)
                {
                    SwimOrFlop(ref t, ref m, body, input, terrain, settings, dt);
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
        /// A ghost's frame: drifting at a height above the ground or the water (jump to rise, up to 4 m; sprint to sink
        /// back down), floating over everything with no gravity, turning to face the way it drifts.
        /// </summary>
        /// <summary>A ghost rises no higher than this above what's under it.</summary>
        const float HoverCeiling = 300f;

        static void Hover(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in TerrainSource terrain,
                          in PlayerMotor settings, float dt)
        {
            var position = t.Position;
            // Drifting with the move keys, faster sprinting.
            position.xz += input.MoveDirection * settings.WalkSpeed * math.max(body.SpeedFactor, 0.1f) * (input.Sprint ? 1.8f : 0.8f) * dt;
            var ground = terrain.SampleGround(position.xz);
            var under = ground.HasWater ? math.max(ground.Height, ground.WaterSurface) : ground.Height;
            if (terrain.FloorUnder(position, out var inCave) is var floor && inCave)
                under = floor;
            // Straight up holding jump, straight down holding crouch, eased in and out; let go, it hangs where it is.
            var wanted = input.JumpHeld ? 4f : input.CrouchHeld ? -4f : 0f;
            m.VerticalVelocity += (wanted - m.VerticalVelocity) * math.min(1f, dt * 4f);
            position.y += m.VerticalVelocity * dt;
            // (Never below half a metre over the ground or water, nor above the ceiling; in a cave, under its rock.)
            var lowest = under + 0.5f;
            if (position.y < lowest)
            {
                position.y += (lowest - position.y) * math.min(1f, dt * 6f);
                m.VerticalVelocity = math.max(m.VerticalVelocity, 0f);
            }
            if (position.y > under + HoverCeiling)
            {
                position.y = under + HoverCeiling;
                m.VerticalVelocity = math.min(m.VerticalVelocity, 0f);
            }
            if (inCave && terrain.CaveDistance(position + new float3(0f, 0.8f, 0f)) > -0.4f)
            {
                position.y = t.Position.y;
                m.VerticalVelocity = math.min(m.VerticalVelocity, 0f);
            }
            // A slow bob while hanging still.
            m.Airspeed += dt * 1.7f;
            position.y += math.cos((float)m.Airspeed) * 0.08f * 1.7f * dt * (1f - math.saturate(math.abs(m.VerticalVelocity)));
            m.Grounded = position.y - lowest < 0.1f;
            t.Position = position;
            Face(ref t, input, settings, dt);
        }

        /// <summary>
        /// A fish's frame: in water, swimming anywhere under it, up or down the way the camera looks as it goes (sprint
        /// is only quicker; jump rises, quickly from deep down, and at the surface leaps out, carrying on across);
        /// out of it, flopping in little hops, or with jump a big one that covers ground.
        /// </summary>
        static void SwimOrFlop(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in TerrainSource terrain,
                               in PlayerMotor settings, float dt)
        {
            var position = t.Position;
            // (Airspeed is the flop's cooldown here; a hovering body runs it up as a clock, so after a change of body it
            // starts again from a hop's length.)
            m.Airspeed = math.min(m.Airspeed, 0.6f);
            var ground = terrain.SampleGround(position.xz);
            var water = ground.HasWater && ground.WaterSurface - ground.Height > 0.25f;
            if (water && position.y < ground.WaterSurface + 0.1f)
            {
                var speed = settings.WalkSpeed * math.max(body.SpeedFactor, 0.1f) * (input.Sprint ? 1.6f : 1f);
                // Swimming the way the camera looks: across, and up or down with it.
                var push = math.length(input.MoveDirection);
                var pitch = math.clamp(input.CameraPitch, -1.2f, 1.2f);
                var next = position.xz + input.MoveDirection * speed * math.cos(pitch) * dt;
                var ahead = terrain.SampleGround(next);
                // (It keeps to the water: no swimming up onto the bank except by leaping out with jump.)
                if (ahead.HasWater && ahead.WaterSurface - ahead.Height > 0.2f || input.JumpHeld)
                    position.xz = next;
                var top = ground.WaterSurface - 0.12f;
                var bed = ground.Height + 0.12f;
                // Jump rises: quickly from deep down, gently near the top; crouch sinks.
                var climb = input.JumpHeld ? (top - position.y > 1f ? 3.5f : 1.5f) : input.CrouchHeld ? -1.5f : 0f;
                position.y += (climb + push * speed * math.sin(pitch)) * dt;
                // Jumping near the surface: out it leaps, carrying on the way it was swimming.
                if (input.JumpPressed && position.y > top - 0.6f)
                {
                    m.VerticalVelocity = settings.JumpSpeed * 0.9f;
                    m.HopSpeed = speed * push;
                    position.y = top + 0.05f;
                }
                else
                {
                    position.y = math.clamp(position.y, bed, math.max(top, bed));
                    m.VerticalVelocity = 0f;
                }
                m.Grounded = true;
                t.Position = position;
                Face(ref t, input, settings, dt);
                return;
            }
            // Out of the water (or leaping): falling, and once down, flopping; jump, a big hop across the ground.
            var floor = water ? math.max(ground.WaterSurface - 0.12f, ground.Height) : ground.Height;
            if (m.Grounded && !water && input.JumpPressed && m.Airspeed <= 0.3f)
            {
                m.VerticalVelocity = 3.6f;
                m.Grounded = false;
                m.Airspeed = 0.45f;
                m.HopSpeed = 3.5f * math.max(math.length(input.MoveDirection), 0.5f);
            }
            else if (m.Grounded && !water && math.lengthsq(input.MoveDirection) > 0.01f && m.Airspeed <= 0f)
            {
                m.VerticalVelocity = 2.2f;
                m.Grounded = false;
                m.Airspeed = 0.6f;
                m.HopSpeed = 1.4f;
            }
            m.Airspeed -= dt;
            if (!m.Grounded)
            {
                // (Across the way it's pressing, or the way it faces when it isn't.)
                var way = math.lengthsq(input.MoveDirection) > 0.01f ? math.normalize(input.MoveDirection)
                        : math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz;
                position.xz += way * math.max(m.HopSpeed, 1.4f) * dt;
            }
            m.VerticalVelocity -= settings.Gravity * dt;
            position.y += m.VerticalVelocity * dt;
            if (position.y <= floor)
            {
                position.y = floor;
                m.VerticalVelocity = 0f;
                m.Grounded = true;
                m.HopSpeed = 0f;
            }
            t.Position = position;
            Face(ref t, input, settings, dt);
        }

        static void Face(ref LocalTransform t, in PlayerInput input, in PlayerMotor settings, float dt)
        {
            var flatMove = new float3(input.MoveDirection.x, 0f, input.MoveDirection.y);
            if (math.lengthsq(flatMove) > 1e-4f)
                t.Rotation = math.slerp(t.Rotation, quaternion.LookRotationSafe(flatMove, math.up()), 1f - math.exp(-settings.TurnSharpness * dt));
        }

        /// <summary>The steepest a player's bird climbs holding jump, and dives holding the dive button (radians).</summary>
        const float SteepClimb = 0.85f, SteepDive = 1.2f;
        /// <summary>Following the camera up or down with the stick pushed: at most this steep (radians), gently.</summary>
        const float GentlePitch = 0.3f;

        /// <summary>
        /// A frame on the wing: turn towards the way the stick (or keys) point, as on foot, or with neither, where the
        /// camera looks (banking round, not snapping). With the stick pushed it eases up or down the way the camera
        /// looks, gently; jump climbs and the dive button dives, steeply and at once. A dive gathers speed as a falling
        /// bird does, and it slows back to its cruise after. It beats its wings to keep speed up or glides down slowly,
        /// slides round tree trunks rather than through them, settles on a perch (a ridge, a post, a chimney) gliding
        /// down onto it, and comes down with a flare.
        /// </summary>
        static void Fly(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in TerrainSource terrain,
                        in PlayerMotor settings, bool inCave, float dt, in DynamicBuffer<FloraObstacle> obstacles,
                        BlobAssetReference<Punchy.Flora.FloraBlob> flora)
        {
            var forward3 = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward3.x, forward3.z);
            var steering = math.lengthsq(input.MoveDirection) > 0.01f;
            var wanted = steering ? math.atan2(input.MoveDirection.x, input.MoveDirection.y)
                                  : math.atan2(input.CameraForward.x, input.CameraForward.y);
            var turn = math.atan2(math.sin(wanted - yaw), math.cos(wanted - yaw));
            yaw += math.clamp(turn, -2.2f * dt, 2.2f * dt);

            var dive = input.DiveHeld;
            var climb = input.JumpHeld && !dive;
            m.Flapping = climb || steering;
            Floor(terrain, t.Position, settings.SwimDepth, out var ground, out _);
            var height = t.Position.y - ground;
            // Nose: steeply up to climb, steeply down to dive (quickly, deliberately); with the stick pushed, a little
            // up or down the way the camera looks; held level beating on, or sinking gently gliding. Near the ground on
            // the way down, it flares: nose up, slowing to land.
            var targetPitch = climb ? SteepClimb : dive ? -SteepDive
                            : steering ? math.clamp(input.CameraPitch * 0.6f, -GentlePitch, GentlePitch) : -0.12f;
            var flaring = !climb && !dive && height < Punchy.Fauna.CreatureMotion.FlareDistance * 1.5f && m.Pitch <= 0.05f;
            if (flaring)
                targetPitch = math.max(targetPitch, -0.15f);
            var pitchRate = climb || dive ? 2.5f : 0.8f;
            m.Pitch += math.clamp(targetPitch - m.Pitch, -pitchRate * dt, pitchRate * dt);

            // Speed: wingbeats bring it up to flying speed; a glide bleeds it off slowly. Climbing costs speed; diving
            // gains it as falling does (a stoop, to three times its cruise), and out of the dive the air slows it back.
            var flySpeed = math.max(body.FlySpeed, 1f);
            var lowest = flySpeed * Punchy.Fauna.CreatureMotion.MinAirspeed * (flaring ? 0.5f : 1f);
            var cruiseMost = flySpeed * 1.6f;
            if (m.Flapping && m.Airspeed < flySpeed)
                m.Airspeed += (flySpeed - m.Airspeed) * math.min(1f, dt * 1.5f);
            m.Airspeed -= math.sin(m.Pitch) * 9.81f * (m.Pitch < 0f ? 0.9f : 0.5f) * dt;
            if (m.Airspeed > cruiseMost && !dive)
                m.Airspeed -= (m.Airspeed - cruiseMost) * math.min(1f, dt * 0.8f);
            m.Airspeed = math.clamp(m.Airspeed - (m.Flapping ? 0f : 0.15f * dt), lowest, flySpeed * 3f);

            var velocity = new float3(math.sin(yaw) * math.cos(m.Pitch), math.sin(m.Pitch), math.cos(yaw) * math.cos(m.Pitch)) * m.Airspeed;
            var position = t.Position + velocity * dt;
            // Tree trunks: slid round, not flown through (above the trunk, it's clear).
            if (obstacles.IsCreated)
                position.xz = SlideAroundObstacles(obstacles, position, settings.Radius, 0f);
            // A perch just below on the way down: it settles on it.
            if (flora.IsCreated && !climb && m.Pitch <= 0.05f && height > 1f)
            {
                var perches = new Unity.Collections.FixedList512Bytes<float3>();
                Punchy.Flora.FloraPlacement.NearPerches(ref flora.Value, terrain, position.xz, 1.2f, ref perches);
                for (int i = 0; i < perches.Length; i++)
                {
                    var perch = perches[i];
                    var below = position.y - perch.y;
                    if (below < -0.1f || below > 1.2f || math.distance(perch.xz, position.xz) > 0.9f)
                        continue;
                    t.Position = perch;
                    t.Rotation = quaternion.RotateY(yaw);
                    m.Flying = false;
                    m.Perched = true;
                    m.Grounded = true;
                    m.OnProp = true;
                    m.VerticalVelocity = 0f;
                    m.Pitch = 0f;
                    return;
                }
            }
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
