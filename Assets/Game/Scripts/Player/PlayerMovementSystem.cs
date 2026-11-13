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
            // W-67 (#79): kit buildings' and ruins' boxes near the player (FloraSystem): floors, walls and ceilings that
            // match the art. Their old circles and support grids are skipped below (Kit flags); other props keep theirs.
            var hasKit = SystemAPI.TryGetSingletonBuffer<KitBox>(out var kitBoxes, true) && kitBoxes.Length > 0;
            // A-95 (#24): dungeon chests and stand-ins (DungeonSpawnSystem), solid the same way as kit boxes.
            var hasDungeon = SystemAPI.TryGetSingletonBuffer<Punchy.Fauna.DungeonPropBox>(out var dungeonRaw, true) && dungeonRaw.Length > 0;
            var dungeonBoxes = hasDungeon ? dungeonRaw.Reinterpret<KitBox>() : default;
            var size = body.Size > 0f ? body.Size : 1f;
            var stepUp = StepUp * size;
            // How far the body (the local avatar) is still down, lying or sitting (A-92, #231).
            var down = 0f;
            foreach (var avatar in SystemAPI.Query<RefRO<Punchy.Fauna.CreatureState>>().WithAll<Punchy.Fauna.PlayerAvatar>())
                down = math.max(avatar.ValueRO.Lie, avatar.ValueRO.Sit);

            foreach (var (transform, motor, motion) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<PlayerMotor>, RefRW<PlayerMotion>>().WithAll<PlayerTag>())
            {
                ref var t = ref transform.ValueRW;
                ref var m = ref motion.ValueRW;
                var settings = motor.ValueRO;
                var input = rawInput;
                // Getting up comes before going anywhere: lying or sitting, the body stands first (moving gets it up,
                // InteractionInputSystem) rather than crawling off with its folded legs dragging under it. A jump
                // pressed meanwhile only gets it up.
                if (down > 0.25f && m.Grounded)
                {
                    input.MoveDirection = float2.zero;
                    input.JumpPressed = false;
                    input.CrouchPressed = false;
                }

                var position = t.Position;
                // (Over deep water but up in the air, jumping across a river: not swimming yet, so neither slowed nor carried
                // by the current until it's down in it.)
                var swimming = Floor(terrain, position, settings.SwimDepth, out var swimFloor, out var inCave) && position.y < swimFloor + 0.3f;
                m.LatchCooldown = math.max(m.LatchCooldown - dt, 0f);
                Breathe(ref m, body, terrain, position, swimming, swimFloor, inCave, settings.SwimDepth, size, dt);

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
                    Fly(ref t, ref m, body, input, terrain, settings, inCave, dt, obstacles, hasFlora ? flora.Blob : default, hasKit, kitBoxes, hasDungeon, dungeonBoxes);
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
                // A-101 (#267): one climbing model. The wall ahead has a grade (a marked section's, ladders and routes,
                // else smooth) and the body climbs it when its skill is at least that, faster the more it has to spare.
                var facing = math.normalizesafe(math.mul(t.Rotation, new float3(0f, 0f, 1f)).xz, new float2(0f, 1f));
                // (On the wall, the grade of the wall it faces: pushing back to climb down probed away from it, row 1.)
                var wallReach = settings.Radius + 0.5f;
                var wallGrade = Punchy.Fauna.Climbing.WallGrade(new Punchy.Fauna.WorldClimbs(), from,
                                                                m.OnWall ? facing : math.normalizesafe(input.MoveDirection, facing), wallReach);
                var climbsWall = Punchy.Fauna.Climbing.CanClimb(body.ClimbSkill, wallGrade);
                var wallSpeed = Punchy.Fauna.Climbing.Speed(body.ClimbSpeed, body.ClimbSkill, wallGrade);
                // A-101 + W-147: a ladder (on a building, in a mine) is climbed straight up its rungs to its top and
                // stepped off there, by any body with the skill for it: from the ground or the water at its foot, or
                // stepping onto it from its top. It needs no wall behind it (a mine drop's face doesn't stop a walk).
                if (body.ClimbSkill > Punchy.Fauna.Climbing.None && LadderClimb(ref t, ref m, body, input, terrain, settings, from, facing, swimming, dt))
                    continue;
                if (m.OnWall && climbsWall)
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
                    var across = new float2(into.y, -into.x);
                    // (Along a marked band only as far as it runs: where the wall beside is a grade it can't climb, the
                    // sideways push stops at the band's edge.)
                    if (wallSide != 0f)
                    {
                        var besideAt = from + new float3(across.x, 0f, across.y) * (math.sign(wallSide) * settings.Radius);
                        if (!Punchy.Fauna.Climbing.CanClimb(body.ClimbSkill, Punchy.Fauna.Climbing.WallGrade(new Punchy.Fauna.WorldClimbs(), besideAt, facing, wallReach)))
                            wallSide = 0f;
                    }
                    wallSideNow = wallSide;
                    move = into + across * wallSide * wallSpeed / math.max(speed, 0.1f);
                }
                position.xz += move * speed * dt;
                if (swimming)
                {
                    // Rivers carry swimmers downstream: harder in rapids, but never faster than they can swim, so
                    // they can always fight their way to the bank.
                    var water = terrain.SampleGround(position);   // (a cave stream's current too, W-134)
                    var current = math.min(settings.RiverCurrent * (1f + water.Rapids), speed * 0.8f);
                    position.xz += water.Flow * current * dt;
                }
                // A climber walking into a tree trunk takes hold of it and goes up; one in the air falling past a trunk
                // (not too fast, and not just after leaping off one) catches hold of it.
                var walkingInto = m.Grounded && math.lengthsq(input.MoveDirection) > 0.25f;
                var fallingPast = !m.Grounded && m.VerticalVelocity > -LatchFall && m.LatchCooldown <= 0f;
                // (Any climber at all: a marked section on a trunk or a hut's round wall may be easier than a tree, A-101.)
                if (Punchy.Fauna.Climbing.Mantles(body.ClimbSkill) &&
                    obstacles.IsCreated && (walkingInto || fallingPast) &&
                    GrabTrunk(obstacles, from, input.MoveDirection, settings.Radius, body.ClimbSkill, !walkingInto, ref m))
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
                var wallHead = climbsWall ? WallClimbHead : 1.7f * size;
                if (tops)
                    position.xz = KeepOutOfPropWalls(ref grids.Set.Value, props, from, position.xz, stepUp, wallHead, out blocked);
                if (hasKit)   // W-67 (#79): kit walls, slid along the same way.
                    position.xz = KitCollision.KeepOut(kitBoxes, from, position.xz, settings.Radius, math.max(stepUp, KitStep), wallHead, ref blocked);
                if (hasDungeon)   // A-95: dungeon chests and stand-ins too (a low one is stepped onto).
                {
                    var kept = KitCollision.KeepOut(dungeonBoxes, from, position.xz, settings.Radius, math.max(stepUp, KitStep), wallHead, ref blocked);
                    // (Its slide along one axis is held to the kit walls too: they only checked the move it was given, and a
                    // rabbit sliding along a chest went into the ruin pillar beside it, then walked free inside the pillar.)
                    if (hasKit && math.any(kept != position.xz) &&
                        KitCollision.Wall(kitBoxes, new float3(kept.x, from.y, kept.y), settings.Radius, math.max(stepUp, KitStep), wallHead) &&
                        !KitCollision.Wall(kitBoxes, from, settings.Radius, math.max(stepUp, KitStep), wallHead))
                        kept = from.xz;
                    position.xz = kept;
                }
                // The salt tower's body and its steps from below (W-27): slid along the same way.
                position.xz = KeepOutOfTower(terrain, from, position.xz, stepUp, 1.7f * size, ref blocked);
                // (#33 probe, -place wall only: every term of the wall latch below, and what stopped the move first.)
                if (WallProbe.Data == 1 && climbsWall)
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
                if (tops && Punchy.Fauna.Climbing.Mantles(body.ClimbSkill) && !swimming && pressing && (input.JumpPressed || !m.Grounded && blocked) &&
                    PropSupport.Ledge(ref grids.Set.Value, props, from, math.normalize(input.MoveDirection),
                                      m.OnWall ? math.max(0.9f * size, WallLedgeReach) : 0.9f * size, stepUp,
                                      MantleReach * size, out var ledge) &&
                    LandingClear(terrain, tops, grids, props, hasKit, kitBoxes, from,
                                 new float3(ledge.Edge.x - ledge.EdgeNormal.x * (settings.Radius + 0.15f), ledge.Height, ledge.Edge.z - ledge.EdgeNormal.y * (settings.Radius + 0.15f)),
                                 settings.Radius, size))
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
                else if (wasOnWall && climbsWall && (tops || hasKit) && wallUp >= 0f && !swimming &&
                         TopUnder(tops, grids, props, hasKit, kitBoxes, from, 0.3f, stepUp, out var perch) &&
                         perch.Height >= from.y - 0.3f && perch.Height <= from.y + stepUp)
                {
                    // Feet on a top (a wall's, after the climb): let go of the wall, with no stick needed (#33).
                    m.Grounded = true;
                    m.VerticalVelocity = 0f;
                    position.y = math.max(from.y, perch.Height);
                    t.Position = position;
                    continue;
                }
                else if (climbsWall && blocked && pressing && !swimming && !input.JumpPressed &&
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
                    if ((tops || hasKit) && wasOnWall && rising > 0f &&
                        TopUnder(tops, grids, props, hasKit, kitBoxes, new float3(overAt.x, from.y, overAt.y), 0f, TopOutReach * math.max(size, 0.5f), out var over) &&
                        over.Height > from.y - stepUp &&
                        LandingClear(terrain, tops, grids, props, hasKit, kitBoxes, from, new float3(overAt.x, over.Height, overAt.y), settings.Radius, size))
                    {
                        m.OnWall = false;
                        m.Mantle = 1e-4f;
                        m.MantleFrom = from;
                        m.MantleTo = new float3(overAt.x, over.Height, overAt.y);
                        t.Position = from;
                        continue;
                    }
                    position.y = from.y + rising * wallSpeed * dt;
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

                // A-102 row 3: diving (crouch down, jump up), and up when out of breath.
                Dive(ref m, terrain, position, swimming, swimFloor, size, ref input, dt);

                if (m.Grounded && input.JumpPressed)
                {
                    // (As high as the body jumps next to a human: its height goes with the square of the speed.)
                    m.VerticalVelocity = settings.JumpSpeed * math.sqrt(body.JumpFactor > 0f ? body.JumpFactor : 1f);
                    m.Grounded = false;
                }

                var swimHere = Floor(terrain, position, settings.SwimDepth, out var ground, out var caveHere);
                // Dived: the swim floor is that much lower, but never into the bed (A-102 row 3).
                if (swimHere && m.Dive > 0f)
                    ground = math.max(ground - m.Dive, math.min(ground, terrain.FloorUnder(position, out _) + DiveBedClear * size));
                // Standing on a prop's top, if it's higher than the ground there; slid off it where it's steep.
                var wasOnProp = m.OnProp;
                m.OnProp = false;
                if ((tops || hasKit) && TopUnder(tops, grids, props, hasKit, kitBoxes, position, settings.Radius * 0.6f, stepUp, out var top) && top.Height > ground)
                {
                    ground = top.Height;
                    m.OnProp = true;
                    if (m.Grounded && top.Normal.y < SlideSlope && math.lengthsq(top.Normal.xz) > 1e-6f)
                        position.xz += math.normalize(top.Normal.xz) * SlideSpeed * dt;
                }
                // A-95: standing on a dungeon chest or stand-in. A body whose middle is already inside one (set down there,
                // or slipped in off its top) is lifted onto it: walls let a body inside a box move freely, so a small one
                // between a box and a step too high for it was pinned there.
                var onDungeonProp = false;
                if (hasDungeon && (KitCollision.Floor(dungeonBoxes, position, settings.Radius * 0.6f, math.max(stepUp, KitStep), out var propTop) ||
                                   InsideBox(dungeonBoxes, position, out propTop)) && propTop > ground)
                {
                    ground = propTop;
                    m.OnProp = true;
                    onDungeonProp = true;
                }
                // A wall climber walking off a top's edge goes over onto the wall, turned to face it, and climbs down
                // (#33: it used to let go and fall). Its feet start just below the rim, so the wall holds it.
                if (climbsWall && (tops || hasKit) && wasOnProp && !m.OnProp && m.Grounded && pressing && !input.JumpPressed && !swimming &&
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
                    // W-67 (#79): rising into a kit floor or roof from below: the head stops under it.
                    if (hasKit && m.VerticalVelocity > 0f && KitCollision.Ceiling(kitBoxes, from, stepUp, out var ceiling) && position.y + 1.7f * size > ceiling)
                    {
                        position.y = math.max(from.y, ceiling - 1.7f * size);
                        m.VerticalVelocity = 0f;
                    }
                    m.Grounded = position.y <= ground;
                    if (m.Grounded)
                    {
                        position.y = ground;
                        m.VerticalVelocity = 0f;
                    }
                }
                // In a cave: pushed back out of the rock round the body (walls, a ceiling, slopes rising steeper than about
                // 60° over a stride, which slide it back down rather than letting it sink in), A-30, A-37.
                if ((inCave || caveHere) && !NoPushOut.Data)
                {
                    var pushed = PushOutOfSolids(terrain, from, position, m.Grounded, settings.Radius, size, dt);
                    // A-95: on a dungeon chest or stand-in, it stays on top: the push-out stands a body within 0.6 m of the
                    // cave floor back on that floor, which sank one on a low box back into it.
                    if (onDungeonProp && m.Grounded)
                        pushed.y = math.max(pushed.y, position.y);
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
            if (inCave && Solid(terrain, position + new float3(0f, 0.8f, 0f)) > -0.4f)
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
                        BlobAssetReference<Punchy.Flora.FloraBlob> flora, bool hasKit, in DynamicBuffer<KitBox> kit,
                        bool hasDungeon, in DynamicBuffer<KitBox> dungeon)
        {
            var forward3 = math.mul(t.Rotation, new float3(0f, 0f, 1f));
            var yaw = math.atan2(forward3.x, forward3.z);
            var steering = math.lengthsq(input.MoveDirection) > 0.01f;
            var wanted = steering ? math.atan2(input.MoveDirection.x, input.MoveDirection.y)
                                  : math.atan2(input.CameraForward.x, input.CameraForward.y);
            var turn = math.atan2(math.sin(wanted - yaw), math.cos(wanted - yaw));
            // In a cave it turns tighter (A-69): at the open-air rate its turn is wider than most passages.
            var turnRate = inCave ? 4f : 2.2f;
            yaw += math.clamp(turn, -turnRate * dt, turnRate * dt);

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
            // Into rock (a cave's ceiling, walls or floor): A-69 slides it along the rock instead: the part of the step
            // into the rock is taken off (along the rock's normal), so it skims a wall or ceiling and keeps going. Only
            // a ceiling noses it level and only a floor stops a dive. Failing that, level, then sinking, then backing
            // out the way the air is; never held still where it met the rock (it used to be, and at the mouth, where
            // the open sky read as rock, it hung there).
            if (inCave && UnderRock(terrain, position, settings.Radius))
            {
                var normal = RockNormal(terrain, position, settings.Radius);
                var step = position - t.Position;
                var slide = t.Position + step - normal * math.min(math.dot(step, normal), 0f) + normal * 0.02f;
                var level = new float3(position.x, t.Position.y, position.z);
                var sink = t.Position - new float3(0f, math.max(m.Airspeed * 0.5f, 1f) * dt, 0f);
                if (!UnderRock(terrain, slide, settings.Radius))
                    position = slide;
                else if (!UnderRock(terrain, level, settings.Radius))
                    position = level;
                else if (!UnderRock(terrain, sink, settings.Radius))
                    position = sink;
                else
                {
                    position = t.Position + normal * math.max(m.Airspeed * 0.5f, 1f) * dt;
                    FlyBlocked.Data++;
                }
                if (normal.y < -0.5f)
                    m.Pitch = math.min(m.Pitch, 0f);
                else if (normal.y > 0.5f)
                    m.Pitch = math.max(m.Pitch, 0f);
            }
            // W-100: kit buildings (walls, stairs, balconies, roofs) are solid on the wing too: it slides along a wall
            // rather than flying through it, and comes down on a stair tread or a deck as it would on the ground.
            var onKit = false;
            if (hasKit)
            {
                var size = body.Size > 0f ? body.Size : 1f;
                var blocked = false;
                position.xz = KitCollision.KeepOut(kit, t.Position, position.xz, settings.Radius, 0.05f, 0.6f * size, ref blocked);
            }
            if (hasDungeon)   // A-95: dungeon chests and stand-ins too, and perched on like a deck.
            {
                var blocked = false;
                position.xz = KitCollision.KeepOut(dungeon, t.Position, position.xz, settings.Radius, 0.05f, 0.6f * (body.Size > 0f ? body.Size : 1f), ref blocked);
            }
            Floor(terrain, position, settings.SwimDepth, out ground, out _);
            if (hasKit && KitCollision.TopAt(kit, position.xz, position.y + 0.25f, 0.5f * (body.Size > 0f ? body.Size : 1f), out var kitTop) && kitTop > ground)
            {
                onKit = true;
                ground = kitTop;
            }
            if (hasDungeon && KitCollision.TopAt(dungeon, position.xz, position.y + 0.25f, 0.5f * (body.Size > 0f ? body.Size : 1f), out var propTop) && propTop > ground)
            {
                onKit = true;
                ground = propTop;
            }
            if (position.y <= ground && inCave && steering && !descend && !dive)
            {
                // A-69: brushing a cave's floor while flying on, it skims it rather than landing (landing there
                // and taking off again, nose up, kept lifting it out of low passages and the mouth).
                position.y = ground;
                m.Pitch = math.max(m.Pitch, 0f);
            }
            else if (position.y <= ground)
            {
                // Down: on its feet (or swimming), walking again.
                position.y = ground;
                m.Flying = false;
                m.Grounded = true;
                m.OnProp = onKit;
                m.VerticalVelocity = 0f;
                m.Pitch = 0f;
            }
            t.Position = position;
            t.Rotation = quaternion.RotateY(yaw);
        }

        /// <summary>In a cave, a flying body at this point would be in the rock (its ceiling or walls).</summary>
        static bool UnderRock(in TerrainSource terrain, float3 position, float radius)
        {
            return Solid(terrain, position + new float3(0f, radius, 0f)) > -radius;
        }

        /// <summary>
        /// A-69: how far into rock a point is (negative in the air). Rock is what's neither cave air nor above the
        /// ground: the cave field alone reads the open sky as rock too, so a bird leaving by the mouth was held there.
        /// </summary>
        static float Solid(in TerrainSource terrain, float3 p)
        {
            return math.min(terrain.CaveDistance(p), terrain.SampleHeight(p.xz) - p.y);
        }

        /// <summary>A-69: the way out of the rock at a flying body's top (unit, pointing into the air), from the field's slope.</summary>
        static float3 RockNormal(in TerrainSource terrain, float3 position, float radius)
        {
            const float h = 0.1f;
            var p = position + new float3(0f, radius, 0f);
            var slope = new float3(
                Solid(terrain, p + new float3(h, 0f, 0f)) - Solid(terrain, p - new float3(h, 0f, 0f)),
                Solid(terrain, p + new float3(0f, h, 0f)) - Solid(terrain, p - new float3(0f, h, 0f)),
                Solid(terrain, p + new float3(0f, 0f, h)) - Solid(terrain, p - new float3(0f, 0f, h)));
            return math.normalizesafe(-slope, new float3(0f, -1f, 0f));
        }

        /// <summary>
        /// A trunk right ahead that the body is pushing into (within half a metre, facing it): taken hold of, at the
        /// side of it the body is on. Trunks too thin to climb (a sapling) or too short are left alone.
        /// </summary>
        /// <summary>Falling faster than this (m/s), a climber can't catch hold of a trunk or wall going past.</summary>
        const float LatchFall = 8f;

        /// <param name="inAir">Falling past rather than walking into it: anywhere up its height, whichever way it faces,
        /// as long as it's right beside it.</param>
        static bool GrabTrunk(in DynamicBuffer<FloraObstacle> obstacles, float3 at, float2 move, float radius, int skill, bool inAir, ref PlayerMotion m)
        {
            var way = math.normalizesafe(move);
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                var top = o.Position.y + (inAir ? o.Height - 0.3f : 1f);
                if (o.Radius < 0.08f || o.Height < 2f || at.y + 0.5f < o.Position.y || at.y > top)
                    continue;
                // (Round the trunk as drawn at the body's height: a leaning tree's trunk isn't over its foot.)
                var to = FloraObstacle.CentreAt(o.Position, o.Height, o.LeanMid, o.LeanTop, at.y) - at.xz;
                // (A tree climber takes only a tree-sized trunk; a hut's round wall is graded a smooth wall, unless the
                // bark where it takes hold is a marked section, A-101.)
                var hold = at.xz + to - math.normalizesafe(to) * o.Radius;
                if (!Punchy.Fauna.Climbing.CanClimb(skill, TrunkGradeAt(o.Radius, new float3(hold.x, at.y, hold.y))))
                    continue;
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
            var speed = Punchy.Fauna.Climbing.Speed(body.ClimbSpeed, body.ClimbSkill,
                                                    TrunkGradeAt(m.TrunkRadius, new float3(TrunkCentre(m, t.Position.y) + outward * m.TrunkRadius, t.Position.y).xzy));
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
            // (Round it only as far as the bark stays climbable: off the end of a marked section on a wall too hard
            // for the body, it stops there, A-101.)
            var angle = m.TrunkAngle + side * speed * dt / math.max(reach, 0.1f);
            var roundTo = TrunkCentre(m, t.Position.y) + new float2(math.cos(angle), math.sin(angle)) * m.TrunkRadius;
            if (side == 0f || Punchy.Fauna.Climbing.CanClimb(body.ClimbSkill, TrunkGradeAt(m.TrunkRadius, new float3(roundTo.x, t.Position.y, roundTo.y))))
                m.TrunkAngle = angle;
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
        /// <summary>
        /// A-101 + W-147: a ladder climb, for a body with the skill for its grade (ClimbLadders, round the players). On:
        /// pushing into its rungs from its Out side (standing, swimming or falling slowly), or walking off its top
        /// toward its Out side. Up and down with the stick (as on a wall), held on its rungs' line; at its top, stepped
        /// off onto its landing; at its foot, standing (or let go into the water under a water ladder); a jump leaps
        /// off. False when no ladder holds the body, so the walk and the wall climb go on as before.
        /// </summary>
        static bool LadderClimb(ref LocalTransform t, ref PlayerMotion m, in PlayerBody body, in PlayerInput input,
                                in TerrainSource terrain, in PlayerMotor settings, float3 from, float2 facing, bool swimming, float dt)
        {
            var reach = settings.Radius + 0.5f;
            var pushing = math.lengthsq(input.MoveDirection) > 0.04f;
            var push = math.normalizesafe(input.MoveDirection, facing);
            Punchy.Terrain.ClimbLadder l;
            byte grade;
            var rising = 0f;
            if (m.OnWall)
            {
                // (Still on: the ladder it faces.)
                if (!Punchy.Fauna.ClimbLadders.At(from + new float3(facing.x, 0f, facing.y) * settings.Radius, reach, out l, out grade) ||
                    !Punchy.Fauna.Climbing.CanClimb(body.ClimbSkill, grade))
                    return false;
                if (input.JumpPressed)
                {
                    // Leaping off: up and back from it.
                    var back = math.normalizesafe(l.Out.xz, -facing);
                    m.OnWall = false;
                    m.VerticalVelocity = settings.JumpSpeed * 0.8f;
                    m.LatchCooldown = 0.5f;
                    t.Position = new float3(from.x + back.x * 0.4f, from.y, from.z + back.y * 0.4f);
                    return true;
                }
                StickOnSurface(input, out rising, out _);
                // (The camera looking away from the ladder: pushing on, away from it, climbs down.)
                var into = -math.normalizesafe(l.Out.xz, -facing);
                if (math.dot(math.normalizesafe(input.CameraForward, into), into) < 0f)
                    rising = -rising;
            }
            else
            {
                if (!pushing || input.JumpPressed || m.Mantle > 0f ||
                    !Punchy.Fauna.ClimbLadders.At(from + new float3(push.x, 0f, push.y) * settings.Radius, reach, out l, out grade) ||
                    !Punchy.Fauna.Climbing.CanClimb(body.ClimbSkill, grade))
                    return false;
                var o = math.normalizesafe(l.Out.xz, -push);
                var at = Punchy.Fauna.ClimbLadders.Local(l, from);
                var height = l.Top.y - l.Foot.y;
                var settled = m.Grounded || swimming || m.VerticalVelocity > -LatchFall && m.LatchCooldown <= 0f;
                if (math.dot(push, -o) > 0.5f && at.y > -0.1f && at.z < height - 0.3f && settled)
                    rising = 1f;   // (from its foot, or its side: up)
                else if (math.dot(push, o) > 0.5f && m.Grounded && at.y < 0.3f && math.abs(at.z - height) < 0.6f)
                    from.y = l.Top.y - 0.5f;   // (from its top, over the edge onto it, to climb down)
                else
                    return false;
            }
            var outward = math.normalizesafe(l.Out.xz, -facing);
            var speed = Punchy.Fauna.Climbing.Speed(body.ClimbSpeed, body.ClimbSkill, grade);
            var y = from.y + rising * speed * dt;
            var line = Punchy.Fauna.ClimbLadders.LineAt(l, math.min(y, l.Top.y));
            var hold = line + outward * (settings.Radius + 0.1f);
            if (rising > 0f && y >= l.Top.y)
            {
                // At its top: stepped off onto its landing, past the rungs.
                var landing = Punchy.Fauna.ClimbLadders.LineAt(l, l.Top.y) - outward * (settings.Radius + 0.25f);
                m.OnWall = false;
                m.Mantle = 1e-4f;
                m.MantleFrom = new float3(hold.x, from.y, hold.y);
                m.MantleTo = new float3(landing.x, l.Top.y, landing.y);
                m.VerticalVelocity = 0f;
                t.Position = m.MantleFrom;
                return true;
            }
            if (rising < 0f && y <= l.Foot.y + 0.05f)
            {
                // At its foot: standing, or let go into the water under a water ladder.
                var floored = Floor(terrain, new float3(from.x, y, from.z), settings.SwimDepth, out var floor, out _);
                m.OnWall = false;
                m.Grounded = !floored && floor >= l.Foot.y - 0.3f;
                m.VerticalVelocity = 0f;
                t.Position = new float3(from.x, m.Grounded ? floor : y, from.z);
                return true;
            }
            m.OnWall = true;
            m.Grounded = false;
            m.VerticalVelocity = 0f;
            var xz = math.lerp(from.xz, hold, math.min(1f, dt * 12f));
            t.Position = new float3(xz.x, y, xz.y);
            t.Rotation = quaternion.LookRotationSafe(new float3(-outward.x, 0f, -outward.y), math.up());
            return true;
        }

        static void StickOnSurface(in PlayerInput input, out float forward, out float right)
        {
            var look = math.normalizesafe(input.CameraForward, new float2(0f, 1f));
            forward = math.dot(input.MoveDirection, look);
            right = math.dot(input.MoveDirection, new float2(look.y, -look.x));
        }

        /// <summary>A-101: the grade of a trunk's bark at a point on it: a marked section's there, else by its width
        /// (a tree, or a hut's round wall graded a smooth wall).</summary>
        static byte TrunkGradeAt(float radius, float3 bark) =>
            Punchy.Fauna.WorldClimbs.At(bark, 0.3f, out var marked) ? marked : Punchy.Fauna.Climbing.TrunkGrade(radius);

        /// <summary>The centre (x, z) of the trunk a body holds, at a height: as drawn, leaning or bending as it may.</summary>
        public static float2 TrunkCentre(in PlayerMotion m, float y) =>
            FloraObstacle.CentreAt(m.TrunkBase, m.TrunkHeight, m.TrunkLeanMid, m.TrunkLeanTop, y);

        static bool pressing0(float2 move) => math.lengthsq(move) > 0.04f;

        /// <summary>The highest a body of human size steps up onto without a climb (m).</summary>
        const float StepUp = 0.4f;
        /// <summary>A-95: how far a body whose middle is inside a dungeon box is lifted onto it (over the tallest chest or stand-in).</summary>
        const float DungeonLift = 0.6f;

        /// <summary>A-95: the top of a box the feet stand inside (from its bottom up to <see cref="DungeonLift"/> under its top), if any.</summary>
        static bool InsideBox(in DynamicBuffer<KitBox> boxes, float3 feet, out float top)
        {
            top = float.MinValue;
            for (int i = 0; i < boxes.Length; i++)
                if (KitCollision.Span(boxes[i], feet.xz, out var bottom, out var t) && bottom <= feet.y + 0.05f && t > feet.y && t <= feet.y + DungeonLift)
                    top = math.max(top, t);
            return top > float.MinValue;
        }
        /// <summary>
        /// W-100: the least step up onto kit boxes, whatever the body's size: a stair tread (0.28 m or less), so a bird
        /// or a cat hops up a stair rather than meeting each tread as a wall.
        /// </summary>
        const float KitStep = 0.32f;
        /// <summary>The highest ledge (above the feet) a body of human size climbs onto, and how long it takes (s).</summary>
        const float MantleReach = 1.8f, MantleTime = 0.8f;
        /// <summary>Steeper than this (a surface normal's up part: 45°) a top slides you off, at this speed (m/s).</summary>
        const float SlideSlope = 0.707f, SlideSpeed = 3f;

        /// <summary>
        /// A move into a prop's top too high to step onto is cut back to whichever of its X and Z parts is clear (sliding
        /// along it), or cancelled; blocked says it was stopped at all.
        /// </summary>
        /// <summary>
        /// W-67 (#79): the highest top under the feet within a step up: non-kit props' grids and kit boxes together.
        /// </summary>
        /// <summary>
        /// P-20 (item 27): a climb onto a ledge only ends where a body can stand. Climbing a house wall used to carry the
        /// player over the wall's top and drop it inside the house (through the colliders). So: a floor under the landing
        /// at the rim's height (a wall's top too thin to stand on leaves only the room's floor far below), no kit box in
        /// the way over the rim or where the body would stand, and nothing solid (rock, a temple wall, a room box) there.
        /// </summary>
        static bool LandingClear(in TerrainSource terrain, bool tops, SupportGrids grids, in DynamicBuffer<FloraSupport> props, bool hasKit,
                                 in DynamicBuffer<KitBox> kit, float3 from, float3 to, float radius, float size)
        {
            var head = 1.7f * math.max(size, 0.3f);
            if (SolidDistance(terrain, to + new float3(0f, math.max(head * 0.5f, radius), 0f)) < radius * 0.5f)
                return false;
            if (hasKit)
            {
                if (KitCollision.Wall(kit, to, radius * 0.8f, KitStep, head))
                    return false;
                var over = new float3(0f, 0.35f * math.max(size, 0.3f), 0f);
                if (KitCollision.Blocks(kit, new float3(from.x, to.y, from.z) + over, to + over))
                    return false;
            }
            return TopUnder(tops, grids, props, hasKit, kit, to + new float3(0f, 0.05f, 0f), radius * 0.5f, 0.3f, out var floor) &&
                   floor.Height >= to.y - 0.3f;
        }

        static bool TopUnder(bool tops, SupportGrids grids, in DynamicBuffer<FloraSupport> props, bool hasKit, in DynamicBuffer<KitBox> kit,
                             float3 feet, float radius, float stepUp, out SupportHit hit)
        {
            hit = default;
            hit.Height = float.MinValue;
            var found = tops && PropSupport.Sample(ref grids.Set.Value, props, feet, radius, stepUp, out hit, true);
            if (hasKit && KitCollision.Floor(kit, feet, radius, math.max(stepUp, KitStep), out var height) && (!found || height > hit.Height))
            {
                hit = default;
                hit.Height = height;
                hit.Normal = new float3(0f, 1f, 0f);
                found = true;
            }
            return found;
        }

        static float2 KeepOutOfPropWalls(ref SupportGridSet set, in DynamicBuffer<FloraSupport> props, float3 from, float2 to,
                                         float stepUp, float head, out bool blocked)
        {
            blocked = PropSupport.Wall(ref set, props, new float3(to.x, from.y, to.y), stepUp, head, out _, true);
            if (!blocked)
                return to;
            var slideX = new float2(to.x, from.z);
            if (!PropSupport.Wall(ref set, props, new float3(slideX.x, from.y, slideX.y), stepUp, head, out _, true))
                return slideX;
            var slideZ = new float2(from.x, to.y);
            return !PropSupport.Wall(ref set, props, new float3(slideZ.x, from.y, slideZ.y), stepUp, head, out _, true) ? slideZ : from.xz;
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
                // (W-67: a kit building's circles are skipped; its boxes stop the player, KitCollision.)
                if (o.Kit || position.y + stepUp > o.Position.y + o.Height || position.y + 1.8f < o.Position.y)
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
        /// legs below it follow the floor. Standing, the knee sphere is pushed only off walls and overhangs, not the floor
        /// (A-37); falling or jumping it is pushed off any rock.</summary>
        public const float BodyKnee = 0.2f;
        /// <summary>Tests ("-nopushout" on a shot line): the cave push-out off, for a before shot of the old collision.</summary>
        public static readonly SharedStatic<bool> NoPushOut = SharedStatic<bool>.GetOrCreate<PlayerMovementSystem, NoPushOutKey>();
        struct NoPushOutKey { }
        /// <summary>Tests (-walk probe): metres the cave push-out has moved the player in all, so a run can say how much it acted.</summary>
        public static readonly SharedStatic<float> PushedTotal = SharedStatic<float>.GetOrCreate<PlayerMovementSystem, PushedTotalKey>();
        struct PushedTotalKey { }
        /// <summary>Tests (A-69 -flycave): frames a flying body found no way on past cave rock and had to back out of it.</summary>
        public static readonly SharedStatic<int> FlyBlocked = SharedStatic<int>.GetOrCreate<PlayerMovementSystem, FlyBlockedKey>();
        struct FlyBlockedKey { }
        /// <summary>A cave floor rising more than this per metre (60°) over <see cref="SlopeSpan"/> the way the body walks can't
        /// be walked up: the body slides back. Real cave slopes run to about 57° over that span (seg 245 on v2, A-37).</summary>
        public const float SteepRise = 1.73f;
        /// <summary>The stretch of floor (m, centred on the feet) a cave slope's steepness is judged over: about a stride
        /// either side, so one lump or ledge in it doesn't count as the slope.</summary>
        public const float SlopeSpan = 2f;

        /// <summary>The cave floor <paramref name="distance"/> m from the feet along <paramref name="direction"/>, followed in
        /// four steps, each looked for from just above the last floor (so it climbs a slope rather than looking from inside
        /// the rock above it). <paramref name="inCave"/> is false if it leaves the cave.</summary>
        static float FloorAlong(in TerrainSource terrain, float3 position, float2 direction, float distance, out bool inCave)
        {
            var y = position.y;
            inCave = true;
            for (int i = 1; i <= 4; i++)
            {
                var xz = position.xz + direction * (distance * i * 0.25f);
                var floor = terrain.FloorUnder(new float3(xz.x, y + 1.2f, xz.y), out inCave);
                if (!inCave)
                    return y;
                y = floor;
            }
            return y;
        }

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
            // (Walking up a slope it can climb: the way up, else zero.)
            var climbing = float2.zero;
            // (Standing on a slope it can walk: the way down it, else zero.)
            var downhill = float2.zero;
            if (grounded)
            {
                downhill = DownhillOnWalkableSlope(terrain, from);
                // A floor rising steeper than SteepRise the way it walks: no further up it, and sliding back down (as off a
                // steep prop top). A-37: judged by the floor's rise over SlopeSpan, not the rock's normal at the feet: a real
                // cave slope is lumpy (seg 245 averages 51 deg, but its normal reads 32-79 deg from one step to the next), and
                // every steep reading undid the step and slid the body back 3 m/s, so it never got up.
                var step = position.xz - from.xz;
                var stepLength = math.length(step);
                if (stepLength > 1e-4f)
                {
                    var way = step / stepLength;
                    var ahead = FloorAlong(terrain, position, way, SlopeSpan * 0.5f, out var aheadInCave);
                    var behind = FloorAlong(terrain, position, -way, SlopeSpan * 0.5f, out var behindInCave);
                    // (It slides back only if the floor is as steep that way where it stood at from; else it has just reached
                    // a wall's foot and only stops. Sliding back there too, a walk along or up seg 245 into its wall stepped
                    // in and slid back on alternate frames, a 0.05 m jitter, A-39.)
                    if (aheadInCave && behindInCave && ahead - behind > SteepRise * SlopeSpan)
                        position.xz = SteepThere(terrain, from, way) ? from.xz - way * SlideSpeed * dt : from.xz;
                    else if (aheadInCave && behindInCave && ahead > behind + 0.1f * SlopeSpan)
                        climbing = way;
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
                    // (Standing, the knee's rock facing up at all is the floor or a slope under the legs: FloorUnder and the
                    // SteepRise test have it. Pushed off it, a body on a 55 deg cave slope was shoved downhill each pass, A-37.)
                    if (grounded && k == 0 && n.y > 0.1f)
                        continue;
                    var side = math.length(n.xz);
                    if (side < 1e-3f)
                        continue;
                    var push = n.xz / side * ((r - d) / math.max(side, 0.4f));
                    // (Standing on a walkable slope, rock pushes along the slope or up it, never down. Climbing, a steeper lump
                    // brushes the chest, and a push back down meant it never got past (seg 245, A-37; walls ahead stop it in
                    // Clear). Beside a wall, its downhill part, pass after pass against the next step into the wall, slid a
                    // body 1.4 m down seg 245 (A-39). A-37 dropped the part back along the walk instead, which on a diagonal
                    // walk also dropped the wall's sideways push (0.14 m sink). The part left is stretched to clear the
                    // sphere along the wall's normal, up to twice the depth.)
                    var down = math.dot(push, downhill);
                    if (down > 0f)
                    {
                        var depth = math.length(push);
                        var wall = push / depth;
                        push -= downhill * down;
                        var along = math.dot(push, wall);
                        if (along > 1e-4f)
                            push *= math.min(depth / along, 2f);
                    }
                    if (math.lengthsq(push) < 1e-8f)
                        continue;
                    position.xz += push;
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
            // (On a walkable slope with the wall's downhill part dropped, a wall facing partly downhill can't always be
            // cleared along the slope: a chest or head still deeper in than at from, and over 0.08 m in, keeps the body
            // where it was, as a wall ahead does. Without this a sideways walk sank 0.31 m into seg 245's wall, A-39.
            // Not while climbing: a lump brushing the chest is pushed past, A-37; holding it there stops seg 245's climb.)
            if (math.lengthsq(downhill) > 0f && math.lengthsq(climbing) == 0f && BodyDepth(terrain, position, r, low, high) > math.max(0.08f, BodyDepth(terrain, from, r, low, high) + 0.005f))
                position = from;
            // (Climbing, a chest or head over 0.10 m in and deeper than at from is pushed sideways across the climb, never
            // back down it, at most twice the radius. If that finds no way out and the floor ahead doesn't rise (a wall,
            // not a step), the body stays at from; otherwise it takes the largest part of the step that stays out. A-40:
            // diag -1 sank 0.19 m and seg 245 up 0.14 m; holding at every such step stopped seg 245's climb at 0.99 m.)
            else if (math.lengthsq(climbing) > 0f)
            {
                var deep = BodyDepth(terrain, position, r, low, high);
                var fromDepth = BodyDepth(terrain, from, r, low, high);
                if (deep > 0.10f && deep > fromDepth + 0.005f)
                {
                    var best = position;
                    var bestDepth = deep;
                    var across = new float2(climbing.y, -climbing.x);
                    var p = position;
                    for (int i = 0; i < 4; i++)
                    {
                        var depth = BodyDepth(terrain, p, r, low, high);
                        if (depth <= 0.08f)
                            break;
                        var s = math.dot(DeepNormal(terrain, p, r, low, high), across);
                        if (math.abs(s) < 0.2f)
                            break;
                        p.xz += across * (math.sign(s) * (depth - 0.07f) / math.abs(s));
                        p = StandOnCaveFloor(terrain, p);
                    }
                    var sideways = false;
                    if (math.distance(p.xz, position.xz) <= 2f * r && BodyDepth(terrain, p, r, low, high) < bestDepth)
                    {
                        best = p;
                        bestDepth = BodyDepth(terrain, p, r, low, high);
                        sideways = true;
                    }
                    var aheadXZ = position.xz + math.normalizesafe(climbing) * 0.3f;
                    var ahead = terrain.FloorUnder(new float3(aheadXZ.x, position.y + 0.6f, aheadXZ.y), out _);
                    if (bestDepth > 0.10f && !sideways && ahead - position.y < 0.05f)
                        best = from;
                    else if (bestDepth > 0.10f)
                    {
                        for (float f = 0.5f; f > 0.1f; f *= 0.5f)
                        {
                            var q = StandOnCaveFloor(terrain, math.lerp(from, best, f));
                            var qDepth = BodyDepth(terrain, q, r, low, high);
                            if (qDepth <= 0.10f || qDepth <= fromDepth + 0.005f)
                            {
                                best = q;
                                break;
                            }
                        }
                    }
                    position = best;
                }
                // (A push back down the climb from a spot that was clear holds the body instead: on diag -1 the corner
                // pushed it back and the next step walked it in again, a 0.15 m three-frame shiver.)
                if (math.dot(position.xz - from.xz, climbing) < -0.01f && fromDepth <= 0.10f)
                    position = from;
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

        /// <summary>
        /// The way down the cave floor under the body (unit length), judged like the SteepRise test by the floor's rise
        /// over SlopeSpan along x and z; zero on flat floor (under 0.1 rise per metre), on a slope too steep to walk, or
        /// where either span leaves the cave.
        /// </summary>
        static float2 DownhillOnWalkableSlope(in TerrainSource terrain, float3 position)
        {
            var h = SlopeSpan * 0.5f;
            var east = FloorAlong(terrain, position, new float2(1f, 0f), h, out var i0);
            var west = FloorAlong(terrain, position, new float2(-1f, 0f), h, out var i1);
            var north = FloorAlong(terrain, position, new float2(0f, 1f), h, out var i2);
            var south = FloorAlong(terrain, position, new float2(0f, -1f), h, out var i3);
            if (!(i0 && i1 && i2 && i3))
                return float2.zero;
            var rise = new float2(east - west, north - south) / SlopeSpan;
            var steep = math.length(rise);
            return steep < 0.1f || steep > SteepRise ? float2.zero : -rise / steep;
        }

        /// <summary>Whether the cave floor rises steeper than SteepRise over SlopeSpan along <paramref name="way"/>.</summary>
        static bool SteepThere(in TerrainSource terrain, float3 position, float2 way)
        {
            var ahead = FloorAlong(terrain, position, way, SlopeSpan * 0.5f, out var aheadInCave);
            var behind = FloorAlong(terrain, position, -way, SlopeSpan * 0.5f, out var behindInCave);
            return aheadInCave && behindInCave && ahead - behind > SteepRise * SlopeSpan;
        }

        /// <summary>How far the chest or head sphere (whichever is deeper) is in solid; zero when both are clear.</summary>
        static float BodyDepth(in TerrainSource terrain, float3 position, float r, float low, float high)
        {
            var chest = SolidDistance(terrain, position + new float3(0f, math.lerp(low, high, 0.5f), 0f));
            var head = SolidDistance(terrain, position + new float3(0f, high, 0f));
            return math.max(0f, r - math.min(chest, head));
        }

        /// <summary>The way out (sideways, unit length) of whichever of the chest and head spheres is deeper in solid.</summary>
        static float2 DeepNormal(in TerrainSource terrain, float3 position, float r, float low, float high)
        {
            var chestAt = position + new float3(0f, math.lerp(low, high, 0.5f), 0f);
            var headAt = position + new float3(0f, high, 0f);
            var chest = SolidDistance(terrain, chestAt);
            var head = SolidDistance(terrain, headAt);
            var n = chest < head ? SolidNormal(terrain, chestAt, chest) : SolidNormal(terrain, headAt, head);
            return math.normalizesafe(n.xz);
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
            // #25 (W-83): interior cells' boxes are solid too (an empty list changes nothing).
            if (InteriorCells.Count > 0)
                return math.min(GroundSolidDistance(terrain, p), InteriorCells.Distance(p));
            return GroundSolidDistance(terrain, p);
        }

        static float GroundSolidDistance(in TerrainSource terrain, float3 p)
        {
            var open = p.y - terrain.SampleHeight(p.xz);
            // W-56: a sunken temple room is air under the ground; its walls are TowerWall's (SunkenTempleShape.Wall).
            if (terrain.TempleAir(p))
                return math.max(open, 0.5f);
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
        /// <summary>How high a body's head is over its feet, for a body of size 1 (m): A-102 breath.</summary>
        const float HeadHeight = 1.6f;

        /// <summary>
        /// A-102 breath (Punchy.Fauna.Breath), the same model as the creatures': used up while the head is under water,
        /// back in air. Swimming at the swim depth the head is held out; walking on the bottom of water too shallow to
        /// swim in, a small body's head can be under. Water breathers (a fish body, water and sea folk), ghosts and
        /// fliers on the wing are always full. Emptied, it pushes the body up (Dive).
        /// </summary>
        static void Breathe(ref PlayerMotion m, in PlayerBody body, in TerrainSource terrain, float3 position, bool swimming,
                            float swimFloor, bool inCave, float swimDepth, float size, float dt)
        {
            if (body.Swims || body.BreathesWater || body.Hovers || m.Flying || !math.isfinite(position.y))
            {
                m.BreathUsed = 0f;
                m.Gasping = false;
                return;
            }
            bool under;
            if (swimming)
                under = position.y < swimFloor - 0.05f && position.y + HeadHeight * size < swimFloor + swimDepth;
            else
            {
                var g = inCave ? terrain.SampleGround(position) : terrain.SampleGround(position.xz);
                under = (inCave ? g.CaveWater : g.HasWater && g.WaterSurface > g.Height) && position.y + HeadHeight * size < g.WaterSurface;
            }
            if (!under && m.BreathUsed <= 0f && !m.Gasping)
                return;
            m.BreathUsed = Punchy.Fauna.Breath.Use(m.BreathUsed, ref m.Gasping, under, body.HoldBreath, dt);
        }

        /// <summary>A-102 row 3 diving: how fast crouch takes a swimmer down and jump brings it up (m/s), how fast it
        /// drifts up with neither held, and how far over the bed its feet stay (m, times its size).</summary>
        const float DiveDown = 1.6f, DiveUp = 2f, DiveDrift = 0.3f, DiveBedClear = 0.1f;

        /// <summary>
        /// A-102 row 3: diving (PlayerMotion.Dive, how far under the swim floor the body is). Swimming, crouch held takes
        /// it down and jump held brings it up; with neither it drifts slowly back up. Never onto the bed. Out of breath
        /// (Gasping, Breathe) it is pushed up at Breath.RiseSpeed and can't go down again until it has caught some back
        /// (Breath.CatchBreath); a water breather never runs out, so it dives as long as it likes. Under water a jump
        /// press only rises (no leap out from down there). Out of swimming water it is back to 0.
        /// </summary>
        static void Dive(ref PlayerMotion m, in TerrainSource terrain, float3 position, bool swimming, float swimFloor, float size,
                         ref PlayerInput input, float dt)
        {
            if (!math.isfinite(m.Dive) || !swimming || !math.isfinite(swimFloor + position.y))
            {
                m.Dive = 0f;
                return;
            }
            var room = math.max(swimFloor - terrain.FloorUnder(position, out _) - DiveBedClear * size, 0f);
            var rate = m.Gasping ? -Punchy.Fauna.Breath.RiseSpeed
                     : input.JumpHeld ? -DiveUp
                     : input.CrouchHeld ? DiveDown
                     : -DiveDrift;
            m.Dive = math.clamp(m.Dive + rate * math.max(dt, 0f), 0f, room);
            if (!math.isfinite(m.Dive))
                m.Dive = 0f;
            if (m.Dive > 0.05f)
                input.JumpPressed = false;
        }

        static bool Floor(in TerrainSource terrain, float3 position, float swimDepth, out float floor, out bool inCave)
        {
            floor = terrain.FloorUnder(position, out inCave);
            if (inCave)
            {
                // W-134: a cave pool or stream deeper than SwimDepth is swum in like surface water (no swell).
                var cave = terrain.SampleGround(position);
                if (!cave.CaveWater || cave.WaterSurface - swimDepth <= floor)
                    return false;
                floor = cave.WaterSurface - swimDepth;
                return true;
            }
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
            // (On the sea, the swimmer rides the swell the water is drawn with: prototype, WaterSwells.)
            floor = ground.WaterSurface + WaterSwells.Height(position.xz, ground.WaterSurface, ground.Height) - swimDepth;
            return true;
        }
    }
}
