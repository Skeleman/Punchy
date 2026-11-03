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
        /// <summary>#33 debug probe: 1 logs the wall latch's terms every frame (set by DebugControls' -place wall).</summary>
        public static readonly SharedStatic<int> WallProbe = SharedStatic<int>.GetOrCreate<WallProbeKey>();
        struct WallProbeKey { }
        /// <summary>How tall a prop's top can be and still count as a wall for a wall climber (m).</summary>
        const float WallClimbHead = 8f;
        /// <summary>On a wall, how far ahead the top's rim is looked for (Ledge starts 0.3 m out, past a small body's reach).</summary>
        const float WallLedgeReach = 0.5f;
        /// <summary>#33: how far above a wall climber's feet (m, for a body of size 1) a top just past the wall's face can
        /// be for it to climb over onto it.</summary>
        const float TopOutReach = 0.8f;

        /// <summary>A walk (the walk toggle) for a human body, m/s; other bodies by their speed next to a human.</summary>
        public const float Stroll = 1.6f;
        /// <summary>A roll's length (s), its pace next to the everyday jog, and the fastest it goes (m/s: a horse doesn't roll 15).</summary>
        public const float RollTime = 0.55f, RollSpeed = 1.3f, RollMost = 8f;
        /// <summary>The way down on the wing with the right face button: a steady descent at this nose-down angle (radians).</summary>
        const float DescendPitch = 0.5f;

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
            var rawInput = SystemAPI.GetSingleton<PlayerInput>();
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
                var input = rawInput;

                var position = t.Position;
                // (Over deep water but up in the air, jumping across a river: not swimming yet, so neither slowed nor carried
                // by the current until it's down in it.)
                var swimming = Floor(terrain, position, settings.SwimDepth, out var swimFloor, out var inCave) && position.y < swimFloor + 0.3f;
                m.LatchCooldown = math.max(m.LatchCooldown - dt, 0f);

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
                // Up a tree trunk: climbing it, round it, or letting go.
                if (m.OnTrunk)
                {
                    if (tops)
                        ClimbTrunk(ref t, ref m, body, input, terrain, settings, dt, ref grids.Set.Value, props, true);
                    else
                    {
                        var none = new SupportGridSet();
                        ClimbTrunk(ref t, ref m, body, input, terrain, settings, dt, ref none, props, false);
                    }
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

                // The right face button on the ground: a roll, a quick dive forward the way the stick points (or the way it
                // faces), over in half a second, with a short wait before the next. No steering while it lasts.
                m.RollCooldown = math.max(m.RollCooldown - dt, 0f);
                if (m.Roll <= 0f && input.CrouchPressed && m.Grounded && !swimming && !m.OnWall && m.RollCooldown <= 0f)
                {
                    m.Roll = 1e-4f;
                    m.RollDir = math.normalizesafe(input.MoveDirection, math.normalizesafe(math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f)));
                }
                var rolling = m.Roll > 0f;
                if (rolling)
                {
                    m.Roll += dt / RollTime;
                    if (m.Roll >= 1f || !m.Grounded || swimming)
                    {
                        m.Roll = 0f;
                        m.RollCooldown = 0.35f;
                        rolling = false;
                    }
                    else
                        input.MoveDirection = m.RollDir;
                }
                var pace = input.Walk ? Stroll : settings.WalkSpeed * (input.Sprint ? settings.SprintMultiplier : 1f);
                if (rolling)
                    pace = settings.WalkSpeed * RollSpeed;
                var speed = pace * math.max(body.SpeedFactor, 0.1f) * (swimming ? settings.SwimSpeedMultiplier : 1f);
                if (rolling)
                    speed = math.min(speed, RollMost);
                var from = position;
                // Up a wall already: the stick is read against the wall (forward up, back down, sideways along it).
                var wallUp = 0f;
                var wallSideNow = 0f;
                var move = input.MoveDirection;
                if (m.OnWall && body.ClimbsWalls)
                {
                    var into = math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz;
                    StickOnSurface(input, out wallUp, out var wallSide);
                    // (The camera looking away from the wall, as when it has stepped back off a top: pushing on, away from
                    // the wall, climbs down, and the stick's right is still the screen's right.)
                    if (math.dot(math.normalizesafe(input.CameraForward, into), into) < 0f)
                    {
                        wallUp = -wallUp;
                        wallSide = -wallSide;
                    }
                    wallSideNow = wallSide;
                    var across = new float2(into.y, -into.x);
                    move = into + across * wallSide * math.max(body.ClimbSpeed, 0.3f) / math.max(speed, 0.1f);
                }
                position.xz += move * speed * dt;
                if (swimming)
                {
                    // Rivers carry swimmers downstream: harder in rapids, but never faster than they can swim, so
                    // they can always fight their way to the bank.
                    var water = terrain.SampleGround(position.xz);
                    var current = math.min(settings.RiverCurrent * (1f + water.Rapids), speed * 0.8f);
                    position.xz += water.Flow * current * dt;
                }
                // A climber walking into a tree trunk takes hold of it and goes up; one in the air falling past a trunk
                // (not too fast, and not just after leaping off one) catches hold of it.
                var walkingInto = m.Grounded && math.lengthsq(input.MoveDirection) > 0.25f;
                var fallingPast = !m.Grounded && m.VerticalVelocity > -LatchFall && m.LatchCooldown <= 0f;
                if (body.ClimbsTrees && obstacles.IsCreated && (walkingInto || fallingPast) &&
                    GrabTrunk(obstacles, from, input.MoveDirection, settings.Radius, body.ClimbsWalls, !walkingInto, ref m))
                {
                    m.Grounded = false;
                    m.VerticalVelocity = 0f;
                    continue;
                }
                var probeWanted = position.xz;
                if (obstacles.IsCreated)
                    position.xz = SlideAroundObstacles(obstacles, position, settings.Radius, stepUp);
                var probeSlid = position.xz;
                if (inCave)
                    position.xz = KeepOutOfCaveWalls(terrain, from, position.xz, settings.Radius);
                var probeCave = position.xz;
                // Props' tops too high to step onto are walls (a boulder, a fountain's rim from outside): slid along.
                var blocked = false;
                // (#33: a wall climber counts anything it can't step onto as a wall, however far above its own head the
                // top is; with head height a lizard never met a garden wall, whose only surface is its top.)
                var wallHead = body.ClimbsWalls ? WallClimbHead : 1.7f * size;
                if (tops)
                    position.xz = KeepOutOfPropWalls(ref grids.Set.Value, props, from, position.xz, stepUp, wallHead, out blocked);
                // The salt tower's body and its steps from below (W-27): slid along the same way.
                position.xz = KeepOutOfTower(terrain, from, position.xz, stepUp, 1.7f * size, ref blocked);
                // (#33 probe, -place wall only: every term of the wall latch below, and what stopped the move first.)
                if (WallProbe.Data == 1 && body.ClimbsWalls)
                {
                    var anyTop = float.MinValue;
                    var anyWall = tops && PropSupport.Wall(ref grids.Set.Value, props, new float3(probeWanted.x, from.y, probeWanted.y), stepUp, 50f, out anyTop);
                    var headTop = float.MinValue;
                    var headWall = tops && PropSupport.Wall(ref grids.Set.Value, props, new float3(probeWanted.x, from.y, probeWanted.y), stepUp, 1.7f * size, out headTop);
                    var slid = math.distance(probeWanted, probeSlid);
                    var caved = math.distance(probeSlid, probeCave);
                    var propCut = math.distance(probeCave, position.xz);
                    var lenMove = math.length(move);
                    UnityEngine.Debug.Log($"WallProbe: blocked {(blocked ? 1 : 0)} pressing {(pressing0(move) ? 1 : 0)} |move| {lenMove} grounded {(m.Grounded ? 1 : 0)} onWall {(m.OnWall ? 1 : 0)} vv {m.VerticalVelocity} cooldown {m.LatchCooldown} jump {(input.JumpPressed ? 1 : 0)} swim {(swimming ? 1 : 0)} tops {(tops ? 1 : 0)} size {size} stepUp {stepUp} head {1.7f * size} wallAtHead {(headWall ? 1 : 0)} wallAny {(anyWall ? 1 : 0)} anyTopAboveFeet {anyTop - from.y} wallUp {wallUp} wallSide {wallSideNow} climbSpeed {body.ClimbSpeed} speed {speed} slideCut {slid} caveCut {caved} propCut {propCut} feetY {from.y} x {from.x} z {from.z}");
                }

                // A ledge within reach ahead: climbed onto, for a jump at it (or reaching it in the air, pressing on).
                var pressing = math.lengthsq(move) > 0.04f;
                if (tops && body.Climbs && !swimming && pressing && (input.JumpPressed || !m.Grounded && blocked) &&
                    PropSupport.Ledge(ref grids.Set.Value, props, from, math.normalize(input.MoveDirection),
                                      m.OnWall ? math.max(0.9f * size, WallLedgeReach) : 0.9f * size, stepUp,
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

                // A wall-climber pushed into a wall too high to step onto goes straight up it (until a ledge is in
                // reach and it climbs over onto the top, above); jump lets go.
                // Leaping off a wall: up and back from it.
                var wasOnWall = m.OnWall;
                m.OnWall = false;
                if (wasOnWall && input.JumpPressed)
                {
                    m.VerticalVelocity = settings.JumpSpeed * 0.8f;
                    m.LatchCooldown = 0.5f;
                    position.xz = from.xz - math.normalizesafe(math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz) * 0.4f;
                }
                else if (wasOnWall && body.ClimbsWalls && tops && wallUp >= 0f && !swimming &&
                         PropSupport.Sample(ref grids.Set.Value, props, from, 0.3f, stepUp, out var perch) &&
                         perch.Height >= from.y - 0.3f && perch.Height <= from.y + stepUp)
                {
                    // Feet on a top (a wall's, after the climb): let go of the wall, with no stick needed (#33).
                    m.Grounded = true;
                    m.VerticalVelocity = 0f;
                    position.y = math.max(from.y, perch.Height);
                    t.Position = position;
                    continue;
                }
                else if (body.ClimbsWalls && blocked && pressing && !swimming && !input.JumpPressed &&
                         (m.Grounded || wasOnWall || m.VerticalVelocity > -LatchFall && m.LatchCooldown <= 0f))
                {
                    m.OnWall = true;
                    m.Grounded = false;
                    m.VerticalVelocity = 0f;
                    // (Latching on, the push is the way it faces; once on, the stick climbs: forward up, back down.)
                    var rising = wasOnWall ? wallUp : 1f;
                    // At the top: a top within reach just past the wall's face (the rim; on a rounded rock, the top it has
                    // crept onto) and it climbs over onto it, rather than clinging on below the rim (#33).
                    var wallInto = math.normalizesafe(math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz);
                    var overAt = position.xz + wallInto * (settings.Radius + 0.15f);
                    if (tops && wasOnWall && rising > 0f &&
                        PropSupport.Sample(ref grids.Set.Value, props, new float3(overAt.x, from.y, overAt.y), 0f, TopOutReach * math.max(size, 0.5f), out var over) &&
                        over.Height > from.y - stepUp)
                    {
                        m.OnWall = false;
                        m.Mantle = 1e-4f;
                        m.MantleFrom = from;
                        m.MantleTo = new float3(overAt.x, over.Height, overAt.y);
                        t.Position = from;
                        continue;
                    }
                    position.y = from.y + rising * math.max(body.ClimbSpeed, 0.3f) * dt;
                    if (wasOnWall && rising < 0f && Floor(terrain, position, settings.SwimDepth, out var foot, out _) && position.y <= foot)
                    {
                        // Down at the foot of it: off, standing.
                        m.OnWall = false;
                        m.Grounded = true;
                        position.y = foot;
                        t.Position = position;
                        continue;
                    }
                    t.Position = position;
                    var face = new float3(input.MoveDirection.x, 0f, input.MoveDirection.y);
                    if (!wasOnWall && math.lengthsq(face) > 1e-4f)
                        t.Rotation = quaternion.LookRotationSafe(face, math.up());
                    continue;
                }

                if (m.Grounded && input.JumpPressed)
                {
                    // (As high as the body jumps next to a human: its height goes with the square of the speed.)
                    m.VerticalVelocity = settings.JumpSpeed * math.sqrt(body.JumpFactor > 0f ? body.JumpFactor : 1f);
                    m.Grounded = false;
                }

                Floor(terrain, position, settings.SwimDepth, out var ground, out var caveHere);
                // Standing on a prop's top, if it's higher than the ground there; slid off it where it's steep.
                var wasOnProp = m.OnProp;
                m.OnProp = false;
                if (tops && PropSupport.Sample(ref grids.Set.Value, props, position, settings.Radius * 0.6f, stepUp, out var top) && top.Height > ground)
                {
                    ground = top.Height;
                    m.OnProp = true;
                    if (m.Grounded && top.Normal.y < SlideSlope && math.lengthsq(top.Normal.xz) > 1e-6f)
                        position.xz += math.normalize(top.Normal.xz) * SlideSpeed * dt;
                }
                // A wall climber walking off a top's edge goes over onto the wall, turned to face it, and climbs down
                // (#33: it used to let go and fall). Its feet start just below the rim, so the wall holds it.
                if (body.ClimbsWalls && tops && wasOnProp && !m.OnProp && m.Grounded && pressing && !input.JumpPressed && !swimming &&
                    position.y - ground > math.max(settings.GroundSnapDistance + speed * dt, stepUp))
                {
                    var off = math.normalizesafe(position.xz - from.xz, input.MoveDirection);
                    m.OnWall = true;
                    m.Grounded = false;
                    m.VerticalVelocity = 0f;
                    position.y = from.y - stepUp - 0.05f;
                    t.Position = position;
                    t.Rotation = quaternion.LookRotationSafe(new float3(-off.x, 0f, -off.y), math.up());
                    continue;
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
                // In a cave: pushed back out of the rock round the body (walls, a ceiling, slopes steeper than about 50°,
                // which slide it back down rather than letting it sink in), A-30.
                if ((inCave || caveHere) && !NoPushOut.Data)
                {
                    var pushed = PushOutOfSolids(terrain, from, position, m.Grounded, settings.Radius, size, dt);
                    PushedTotal.Data += math.distance(pushed, position);
                    position = pushed;
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
            // (Rising out of it in a leap: in the air, not swimming, until it comes back down.)
            if (water && position.y < ground.WaterSurface + 0.1f && m.VerticalVelocity <= 0f)
            {
                var speed = settings.WalkSpeed * math.max(body.SpeedFactor, 0.1f) * (input.Sprint ? FishSprint : 1f);
                // Swimming the way the camera looks: across, and up or down with it. Sprinting holds its depth instead (a
                // fast dash across, level), and jump and crouch still rise and sink.
                var push = math.length(input.MoveDirection);
                var pitch = input.Sprint ? 0f : math.clamp(input.CameraPitch, -1.2f, 1.2f);
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
                    m.Grounded = false;
                    t.Position = position;
                    Face(ref t, input, settings, dt);
                    return;
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

        /// <summary>A fish's sprint next to its ordinary swim.</summary>
        const float FishSprint = 2.8f;

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
            // The right face button: down at a steady angle, gliding (the dive button is the steep stoop).
            var descend = input.CrouchHeld && !dive && !climb;
            m.Flapping = climb || steering && !descend;
            Floor(terrain, t.Position, settings.SwimDepth, out var ground, out _);
            var height = t.Position.y - ground;
            // Nose: steeply up to climb, steeply down to dive (quickly, deliberately); with the stick pushed, a little
            // up or down the way the camera looks; held level beating on, or sinking gently gliding. Near the ground on
            // the way down, it flares: nose up, slowing to land.
            var targetPitch = climb ? SteepClimb : dive ? -SteepDive : descend ? -DescendPitch
                            : steering ? math.clamp(input.CameraPitch * 0.6f, -GentlePitch, GentlePitch) : -0.12f;
            var flaring = !climb && !dive && !descend && height < Punchy.Fauna.CreatureMotion.FlareDistance * 1.5f && m.Pitch <= 0.05f;
            if (flaring)
                targetPitch = math.max(targetPitch, -0.15f);
            var pitchRate = climb || dive || descend ? 2.5f : 0.8f;
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
            // Under rock (a cave's ceiling or walls): no further that way, and the nose goes down. It slides along level if
            // that's clear (under a ceiling), else sinks (at a wall) until it's down, rather than hanging in the air
            // where it met the rock.
            if (inCave && UnderRock(terrain, position, settings.Radius))
            {
                var level = new float3(position.x, t.Position.y, position.z);
                var sink = t.Position - new float3(0f, math.max(m.Airspeed * 0.5f, 1f) * dt, 0f);
                position = !UnderRock(terrain, level, settings.Radius) ? level : !UnderRock(terrain, sink, settings.Radius) ? sink : t.Position;
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

        /// <summary>In a cave, a flying body at this point would be in the rock (its ceiling or walls).</summary>
        static bool UnderRock(in TerrainSource terrain, float3 position, float radius)
        {
            return terrain.CaveDistance(position + new float3(0f, radius, 0f)) > -radius;
        }

        /// <summary>
        /// A trunk right ahead that the body is pushing into (within half a metre, facing it): taken hold of, at the
        /// side of it the body is on. Trunks too thin to climb (a sapling) or too short are left alone.
        /// </summary>
        /// <summary>Falling faster than this (m/s), a climber can't catch hold of a trunk or wall going past.</summary>
        const float LatchFall = 8f;

        /// <param name="inAir">Falling past rather than walking into it: anywhere up its height, whichever way it faces,
        /// as long as it's right beside it.</param>
        static bool GrabTrunk(in DynamicBuffer<FloraObstacle> obstacles, float3 at, float2 move, float radius, bool anySurface, bool inAir, ref PlayerMotion m)
        {
            var way = math.normalizesafe(move);
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                // (A tree climber takes only a tree-sized trunk; a hut's round wall is for the wall-climbers.)
                var top = o.Position.y + (inAir ? o.Height - 0.3f : 1f);
                if (o.Radius < 0.08f || o.Height < 2f || at.y + 0.5f < o.Position.y || at.y > top || !anySurface && o.Radius > 0.7f)
                    continue;
                // (Round the trunk as drawn at the body's height: a leaning tree's trunk isn't over its foot.)
                var to = FloraObstacle.CentreAt(o.Position, o.Height, o.LeanMid, o.LeanTop, at.y) - at.xz;
                var gap = math.length(to) - o.Radius - radius;
                if (inAir ? gap > 0.3f : gap > 0.5f || math.dot(math.normalizesafe(to), way) < 0.7f)
                    continue;
                m.OnTrunk = true;
                m.TrunkBase = o.Position;
                m.TrunkRadius = o.Radius;
                m.TrunkHeight = o.Height;
                m.TrunkLeanMid = o.LeanMid;
                m.TrunkLeanTop = o.LeanTop;
                m.TrunkAngle = math.atan2(-to.y, -to.x);
                return true;
            }
            return false;
        }

        /// <summary>
        /// A frame up a trunk: pushing towards it climbs, away from it climbs down (off at the foot), sideways goes round
        /// it; at the top it stays (the crown isn't walkable); jump lets go, a hop back off it. The body clings to the
        /// bark facing in, its feet on the trunk.
        /// </summary>
        static void ClimbTrunk(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input, in TerrainSource terrain,
                               in PlayerMotor settings, float dt, ref SupportGridSet grids, in DynamicBuffer<FloraSupport> props, bool tops)
        {
            var outward = new float2(math.cos(m.TrunkAngle), math.sin(m.TrunkAngle));
            if (input.JumpPressed)
            {
                // A leap off it: up and away from the trunk.
                m.OnTrunk = false;
                m.Grounded = false;
                m.VerticalVelocity = settings.JumpSpeed * 0.8f;
                m.LatchCooldown = 0.5f;
                t.Position += new float3(outward.x, 0f, outward.y) * 0.5f;
                return;
            }
            var speed = math.max(body.ClimbSpeed, 0.3f);
            var inward = -outward;
            var round = new float2(-outward.y, outward.x);
            // (The stick against the surface, not the world: forward is up, back is down, and right is to the body's
            // right as it faces the trunk, which is the way TrunkAngle grows.)
            StickOnSurface(input, out var up, out var side);
            // (Round it only for a push mostly sideways: a push roughly at the trunk climbs straight, rather than
            // working its way round.)
            side = math.sign(side) * math.max(math.abs(side) - 0.45f, 0f) / 0.55f;
            var y = t.Position.y + up * speed * dt;
            // Feet on the bark (the body hangs a leg's length out from them: CreatureAnimator), drawn in to it over a
            // moment from where it took hold. (Held out at the body's radius, as before, the climbing monkey hung clear
            // of the trunk, a gap of half a metre and more.)
            var reach = math.max(m.TrunkRadius + 0.03f, math.distance(t.Position.xz, TrunkCentre(m, t.Position.y)) - dt);
            m.TrunkAngle += side * speed * dt / math.max(reach, 0.1f);
            Floor(terrain, t.Position, settings.SwimDepth, out var ground, out _);
            if (y <= ground && up < 0f)
            {
                // Down at the foot: off it, standing (stepped back to clear the trunk, so nothing shoves it out).
                m.OnTrunk = false;
                m.Grounded = true;
                y = ground;
                reach = m.TrunkRadius + settings.Radius;
            }
            var highest = m.TrunkBase.y + m.TrunkHeight - 0.3f;
            y = math.min(y, highest);
            outward = new float2(math.cos(m.TrunkAngle), math.sin(m.TrunkAngle));
            // At the top of something with a top to stand on (a hut's roof, a tower's), pushing on: up onto it.
            if (tops && up > 0.3f && y >= highest - 0.05f)
            {
                var over = m.TrunkBase.xz + outward * math.max(m.TrunkRadius - 0.5f, 0f);
                if (PropSupport.Sample(ref grids, props, new float3(over.x, y, over.y), settings.Radius * 0.6f, 1.2f, out var roof) &&
                    roof.Height > y - 0.5f)
                {
                    t.Position = new float3(over.x, roof.Height, over.y);
                    m.OnTrunk = false;
                    m.Grounded = true;
                    m.OnProp = true;
                    m.VerticalVelocity = 0f;
                    return;
                }
            }
            // (Round the trunk's centre at this height: up a leaning tree the body follows the trunk as drawn.)
            var at = TrunkCentre(m, y) + outward * reach;
            t.Position = new float3(at.x, y, at.y);
            t.Rotation = quaternion.LookRotationSafe(new float3(-outward.x, 0f, -outward.y), math.up());
            m.VerticalVelocity = 0f;
        }

        /// <summary>
        /// The stick as the player pushes it on the screen: forward (+, up a surface) and right (+), from the world-space
        /// move and where the camera looks. A test's move with no camera reads as forward.
        /// </summary>
        static void StickOnSurface(in PlayerInput input, out float forward, out float right)
        {
            var look = math.normalizesafe(input.CameraForward, new float2(0f, 1f));
            forward = math.dot(input.MoveDirection, look);
            right = math.dot(input.MoveDirection, new float2(look.y, -look.x));
        }

        /// <summary>The centre (x, z) of the trunk a body holds, at a height: as drawn, leaning or bending as it may.</summary>
        public static float2 TrunkCentre(in PlayerMotion m, float y) =>
            FloraObstacle.CentreAt(m.TrunkBase, m.TrunkHeight, m.TrunkLeanMid, m.TrunkLeanTop, y);

        static bool pressing0(float2 move) => math.lengthsq(move) > 0.04f;

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

        /// <summary>A move into the salt tower's body or a step too high to step onto: cut back to its clear X or Z part, or cancelled.</summary>
        static float2 KeepOutOfTower(in TerrainSource terrain, float3 from, float2 to, float stepUp, float head, ref bool blocked)
        {
            if (!terrain.TowerWall(new float3(to.x, from.y, to.y), stepUp, head))
                return to;
            blocked = true;
            if (!terrain.TowerWall(new float3(to.x, from.y, from.z), stepUp, head))
                return new float2(to.x, from.z);
            return !terrain.TowerWall(new float3(from.x, from.y, to.y), stepUp, head) ? new float2(from.x, to.y) : from.xz;
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

        /// <summary>How far above the feet (m, for a body of size 1) the body's lowest sphere starts, past its radius: the
        /// legs below it follow the floor, and a slope steeper than about 50° reaches the sphere and pushes the body back.</summary>
        public const float BodyKnee = 0.2f;
        /// <summary>Tests ("-nopushout" on a shot line): the cave push-out off, for a before shot of the old collision.</summary>
        public static readonly SharedStatic<bool> NoPushOut = SharedStatic<bool>.GetOrCreate<PlayerMovementSystem, NoPushOutKey>();
        struct NoPushOutKey { }
        /// <summary>Tests (-walk probe): metres the cave push-out has moved the player in all, so a run can say how much it acted.</summary>
        public static readonly SharedStatic<float> PushedTotal = SharedStatic<float>.GetOrCreate<PlayerMovementSystem, PushedTotalKey>();
        struct PushedTotalKey { }
        /// <summary>A cave floor whose normal's up part is under this (50°) can't be walked up: the body slides back.</summary>
        public const float SteepFloor = 0.64f;

        /// <summary>
        /// The body as three spheres (knee, middle, head) of the body's radius scaled by its size, each pushed out of any
        /// solid it is in along the solid's normal (sideways only: the floor is FloorUnder's job), a few passes. A head
        /// pushed further into a ceiling than it was at <paramref name="from"/> keeps the body where it was (no walking
        /// in under rock too low). Grounded bodies are stood back on the floor where they end up.
        /// </summary>
        public static float3 PushOutOfSolids(in TerrainSource terrain, float3 from, float3 position, bool grounded, float radius, float size, float dt)
        {
            var r = math.clamp(radius * size, 0.1f, 1f);
            var low = r + BodyKnee * size;
            var high = math.max(low, 1.6f * size - r);
            var start = position.xz;
            if (grounded)
            {
                // A floor steeper than SteepFloor: no further up it, and sliding back down (as off a steep prop top).
                var foot = new float3(position.x, position.y + 0.1f, position.z);
                var slope = SolidNormal(terrain, foot, SolidDistance(terrain, foot));
                var downhill = math.normalizesafe(slope.xz);
                if (slope.y < SteepFloor && slope.y > 0.05f && math.lengthsq(downhill) > 0f)
                {
                    var step = position.xz - from.xz;
                    position.xz -= downhill * math.min(math.dot(step, downhill), 0f);
                    position.xz += downhill * SlideSpeed * dt;
                }
                position = StandOnCaveFloor(terrain, position);
            }
            for (int pass = 0; pass < 4; pass++)
            {
                var moved = false;
                for (int k = 0; k < 3; k++)
                {
                    var c = position + new float3(0f, math.lerp(low, high, k * 0.5f), 0f);
                    var d = SolidDistance(terrain, c);
                    if (d >= r)
                        continue;
                    // Sideways by as much as clears the sphere: a sloping face gives only its sideways part per metre.
                    var n = SolidNormal(terrain, c, d);
                    var side = math.length(n.xz);
                    if (side < 1e-3f)
                        continue;
                    position.xz += n.xz / side * ((r - d) / math.max(side, 0.4f));
                    moved = true;
                }
                if (!moved)
                    break;
                if (grounded)
                    position = StandOnCaveFloor(terrain, position);
            }
            // (Never more than twice the body's radius in a frame: a push-out is a correction, not a teleport. A-32: one
            // radius left a body that starts or lands embedded (a 55 deg slope) up to 0.2 m in for a frame or two.)
            var shift = position.xz - start;
            if (math.lengthsq(shift) > 4f * r * r)
            {
                position.xz = start + math.normalize(shift) * (2f * r);
                if (grounded)
                    position = StandOnCaveFloor(terrain, position);
            }
            // (Only rock overhead counts (its normal points down): a steep floor beside the head is the slope the body is
            // sliding down, and undoing that step wedged a small body on a 65 deg slope for good (A-32).)
            var headAt = position + new float3(0f, high, 0f);
            var headNow = SolidDistance(terrain, headAt);
            if (headNow < r - 0.05f && headNow < SolidDistance(terrain, from + new float3(0f, high, 0f)) - 0.01f &&
                SolidNormal(terrain, headAt, headNow).y < -0.3f)
                position = from;
            return position;
        }

        static float3 StandOnCaveFloor(in TerrainSource terrain, float3 position)
        {
            var floor = terrain.FloorUnder(position + new float3(0f, 0.3f, 0f), out var inCave);
            if (inCave && math.abs(floor - position.y) < 0.6f)
                position.y = floor;
            return position;
        }

        /// <summary>
        /// Distance from a point to solid (negative inside it), what PushOutOfSolids keeps the body out of and the camera
        /// keeps its lens out of. Cave rock for now; above the ground surface it is open air, whatever the cave field
        /// says (an entrance). A landmark with a solid body of its own (the salt tower, a temple) joins in here with
        /// math.min of its own distance, and gets the push-out and the camera for free; the push-out runs where
        /// FloorUnder says the body is in a cave, so such a landmark also widens that test at the call in OnUpdate.
        /// </summary>
        public static float SolidDistance(in TerrainSource terrain, float3 p)
        {
            var open = p.y - terrain.SampleHeight(p.xz);
            if (open > 0f)
                return math.max(open, -terrain.CaveDistance(p));
            return -terrain.CaveDistance(p);
        }

        /// <summary>The way out of the solid at a point (unit length): SolidDistance's gradient, forward differences.</summary>
        public static float3 SolidNormal(in TerrainSource terrain, float3 p, float d)
        {
            const float e = 0.05f;
            var g = new float3(SolidDistance(terrain, p + new float3(e, 0f, 0f)) - d,
                               SolidDistance(terrain, p + new float3(0f, e, 0f)) - d,
                               SolidDistance(terrain, p + new float3(0f, 0f, e)) - d);
            return math.normalizesafe(g, new float3(0f, 1f, 0f));
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
