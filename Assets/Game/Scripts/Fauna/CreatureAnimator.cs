using Punchy.Terrain;
using Unity.Collections;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Poses a creature for drawing. Layers, in order:
    ///  1. gait: every part blends its key poses by its own phase in the gait cycle (walk and run keys mixed by
    ///     speed, idle mixed in as it stops);
    ///  2. body: stands at the height its legs give it, follows the slope of the ground, bobs with each step and
    ///     rocks when bounding;
    ///  3. secondary motion: tails and ears swing out of turns on a spring; the head turns to look;
    ///  4. posture (from the activity): lying down lowers the body to the ground and folds legs that hang beneath
    ///     it; head-down bends the neck and head to the ground, chewing; a play bow drops the chest; curling up to
    ///     sleep wraps the tail round and lays the ears back; sitting folds the hind legs and raises the chest;
    ///     climbing (or sitting up tall) pitches the body up, diving or hanging in a web pitches it down; in flight
    ///     the wings beat through their walk keys and the legs tuck up; resting animals breathe; an idle action
    ///     (pawing, grooming, scratching, shaking, stretching, sniffing, preening…) bends the parts it uses;
    ///  5. the individual: each animal has fixed traits (from CreatureState.Individual) for posture, arm swing,
    ///     bounce, torso twist and head tilt, shows one option per style group (hairstyles, beards) and has its own
    ///     hair colour; a worker at its post poses its arms for its task (hammering, chopping, tending…);
    ///  6. feet: legs are bent with two-bone IK so their feet meet the actual ground rather than a flat plane;
    ///     chains marked Follow Ground (snake bodies, dragging tails) lie along the terrain segment by segment.
    /// </summary>
    public static class CreatureAnimator
    {
        const int MaxSegments = 128;

        /// <param name="detail">Near the camera: the legs fitted to the ground under each foot (the hips lowered on a
        /// slope, each foot reaching for the ground, tipped with it, rolling heel to toe). Further off, where none of
        /// that shows, they keep to the body's plane, saving the ground samples.</param>
        public static void Evaluate(ref CreatureBlob b, in CreatureState s, int coat, in TerrainSource terrain,
                                    NativeArray<SegmentInstance> output, int offset, bool detail = true)
        {
            var scale = s.Scale;
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.Speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed);

            // Body on the ground: height from the legs, pitch from the ground ahead and behind.
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var halfLength = b.BodyLength * 0.5f * scale;
            // Animals stand on the terrain; the player's body can be above it (jumping, swimming).
            // Swimmers and floaters are carried at their own height (the body's centre at Position).
            var aquatic = b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats;
            var terrainHeight = aquatic ? s.Position.y - b.StandHeight * scale : GroundAt(terrain, s.Position.xz, s.Position.y);
            var ground = math.max(terrainHeight, s.Position.y);
            if (aquatic)
                ground = terrainHeight;
            var airborne = aquatic ? 0f : math.saturate((s.Position.y - terrainHeight - 0.05f) / 0.3f);
            // Four-legged bodies tilt with the slope; upright ones stay level (their legs take up the slope), and
            // nobody tilts to follow the ground under them while in the air.
            var gradient = 0f;
            if (!b.Upright && !aquatic)
            {
                var front = GroundAt(terrain, s.Position.xz + forward.xz * halfLength, s.Position.y);
                var back = GroundAt(terrain, s.Position.xz - forward.xz * halfLength, s.Position.y);
                gradient = (front - back) / (2f * halfLength);   // rise per metre forward
            }
            var pitch = math.clamp(-math.atan(gradient), -0.45f, 0.45f) * (1f - airborne);
            // The plane the body stands on is as steep as it pitches (at most about 26°): on anything steeper, the legs
            // make up the rest, each to the ground under it.
            gradient = -math.tan(pitch);
            // Swimming (a land animal low in deep water): nose up a little, to keep its head out.
            if (b.CrossesWater)
            {
                var water = terrain.SampleGround(s.Position.xz);
                if (water.HasWater && s.Position.y < water.WaterSurface - b.StandHeight * scale * 0.5f)
                    pitch -= 0.22f;
            }
            // A leaping fish noses up out of the water and dives back in nose first.
            if (s.Air > 0f)
                pitch -= math.cos(math.PI * s.Air) * 0.9f;

            // This individual's way of moving: posture, arm swing, bounce, twist and head carriage.
            var id = s.Individual;
            var lean = CreatureBlob.Trait(id, 1) * 0.13f;
            var armSwing = 1f + CreatureBlob.Trait(id, 2) * 0.45f;
            var bounce = 1f + CreatureBlob.Trait(id, 3) * 0.5f;
            var twist = 1f + CreatureBlob.Trait(id, 4) * 0.6f;
            var headTilt = new float3(CreatureBlob.Trait(id, 6) * 0.1f, 0f, CreatureBlob.Trait(id, 7) * 0.07f);

            // Past a run, folk sprint: leaning well in, and lifting clear of the ground between steps.
            var sprint = b.Sprint(s.Speed);
            pitch += sprint * 0.3f;
            var bobs = math.lerp(b.BobsPerCycle.x, b.BobsPerCycle.y, run);
            var bob = math.lerp(b.Bob.x, b.Bob.y, run) * bounce * move * scale * (0.5f - 0.5f * math.cos(2f * math.PI * bobs * s.Phase));
            pitch += b.RunRock * run * move * math.sin(2f * math.PI * s.Phase);

            // Posture: lying lowers the body onto its belly; a bow or a lowered head tips the chest down.
            var lying = math.min(b.StandHeight, b.FrontRadius * b.BodyFlatten * 0.9f);
            var height = math.lerp(b.StandHeight, lying, s.Lie) * scale;
            var lowered = b.StandHeight * scale - height;
            pitch += s.Bow * 0.3f + s.HeadDown * 0.1f * (1f - s.Lie);
            // Sitting: rump down on folded hind legs, chest up.
            pitch -= s.Sit * 0.5f;
            height -= s.Sit * b.StandHeight * scale * 0.3f;
            // Folk sit and lie their own way, not rump-down like a four-legged body: sitting, the hips down near the
            // ground with the body upright; lying, over onto the back (the limbs: SitPose and LiePose).
            if (b.Upright)
            {
                // (Legs out, leaning back a little on its hands; cross-legged or on a seat, sitting up. On a seat the
                // hips are at the seat's height, which comes in Anchor.)
                var seated = Seated(in s);
                pitch += s.Sit * (0.5f - (seated || CrossLegged(in s) ? 0.05f : 0.25f));
                var hips = seated ? math.max(s.Anchor.y - s.Position.y, 0f) + 0.1f * scale : 0.16f * scale;
                height = math.lerp(height + s.Sit * b.StandHeight * scale * 0.3f, hips, s.Sit);
                pitch -= 1.5f * s.Lie;
            }
            // Climbing a trunk or sitting up tall (up), diving or hanging head down in a web (down).
            pitch -= s.Vertical * math.PI * 0.47f;
            var still = 1f - move;
            var breath = 0.5f + 0.5f * math.sin(s.Clock * (s.Curl > 0.5f ? 1.3f : 2.2f));
            height += breath * 0.01f * scale * still * (0.3f + s.Lie);
            // Folk out swimming, in water deep enough: lying forward at the surface (their head held up, below).
            var swimming = 0f;
            if (b.Upright && s.Mode == CreatureMode.Swim)
            {
                var water = terrain.SampleGround(s.Position.xz);
                if (water.HasWater)
                    swimming = math.smoothstep(b.StandHeight * scale * 0.6f, b.StandHeight * scale * 0.9f, water.WaterSurface - water.Height);
                pitch += 1.35f * swimming;
                height = math.lerp(height, 0.05f * scale, swimming);
            }

            // Idle actions that move the whole body: a shake rolls it, lizard push-ups bob it, sniffing dips it.
            var act = ActionEnvelope(s.ActionTime);
            var roll = 0f;
            if (s.Action == IdleAction.Shake)
                roll = act * 0.45f * math.sin(s.Clock * 28f);
            else if (s.Action == IdleAction.HeadBob)
                height += act * 0.35f * b.StandHeight * scale * math.abs(math.sin(s.Clock * 9f));
            else if (s.Action == IdleAction.Sniff)
                pitch += act * 0.08f;

            var rootRot = math.mul(quaternion.RotateY(s.Yaw), math.mul(quaternion.RotateX(pitch), quaternion.RotateZ(roll)));
            // Pitched up against a trunk, the body keeps its legs' length out from it (behind it) rather than above.
            var up = math.saturate(s.Vertical);
            var lift = height - bob + sprint * 0.08f * scale * (0.5f + 0.5f * math.cos(4f * math.PI * s.Phase));
            // Hanging head down in a web, the body sits right at its anchor (the web's centre).
            var hang = s.Mode == CreatureMode.Web || s.Mode == CreatureMode.Roost ? math.saturate(-s.Vertical) : 0f;
            var rootPos = new float3(s.Position.x, ground, s.Position.z) + new float3(0f, lift * (1f - up - hang), 0f) - forward * lift * up;
            // A crawler on a rock face, a wall or a ceiling lies on it: its back to the open, its head the way it's going
            // (straight up the face if it's heading into it), standing off the surface on its legs.
            var crawling = false;
            if (b.Crawls)
            {
                var surfaceUp = CreatureMotion.Surface(terrain, s.Position, out var steep, out _);
                if (steep)
                {
                    crawling = true;
                    var along = forward - surfaceUp * math.dot(forward, surfaceUp);
                    if (math.lengthsq(along) < 0.04f)
                        along = new float3(0f, 1f, 0f) - surfaceUp * surfaceUp.y;
                    rootRot = math.mul(quaternion.LookRotationSafe(math.normalizesafe(along, forward), surfaceUp), quaternion.RotateZ(roll));
                    rootPos = s.Position + surfaceUp * (height - bob);
                }
            }

            // Across a slope or over a step, the hips come down just far enough that the downhill feet can reach the
            // ground: under each leg's foot as it stands now, how far the ground falls below the body's plane, against
            // how much further that leg can stretch. (The legs' own correction below then bends the others by as much.)
            var sink = 0f;
            if (detail && !aquatic && !crawling && airborne < 0.5f && s.Fly < 0.5f && math.abs(s.Vertical) < 0.3f && s.Lie < 0.5f)
            {
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var leg = ref b.Parts[i];
                    if (leg.Role != LimbRole.Leg || leg.Parent >= 0 || leg.SegmentCount < 2)
                        continue;
                    // (Where its foot stands, not straight under the hip: on a bank the two can be far apart in height.
                    // Its give is none for a straight leg, more for a bent one.)
                    var foot = rootPos + math.mul(rootRot, (leg.Socket + leg.Stance) * scale);
                    var plane = ground + math.dot(foot.xz - s.Position.xz, forward.xz) * gradient;
                    sink = math.max(sink, plane - GroundAt(terrain, foot.xz, s.Position.y) - leg.Slack * scale * 0.9f);
                }
                sink = math.clamp(sink, 0f, b.StandHeight * scale * 0.4f);
                rootPos.y -= sink;
            }

            // Body.
            ref var body = ref b.Segments[0];
            output[offset] = Instance(rootPos + math.mul(rootRot, new float3(0f, 0f, -halfLength + body.RadiusStart * scale)), rootRot, ref body, scale, b.SegmentColor(0, coat));

            var segPos = new FixedList4096Bytes<float3>();
            var segRot = new FixedList4096Bytes<quaternion>();
            var endPos = new FixedList4096Bytes<float3>();
            var endRot = new FixedList4096Bytes<quaternion>();
            segPos.Length = math.min(b.Segments.Length, MaxSegments);
            segRot.Length = segPos.Length;
            // Parts hidden for this individual (unchosen style options, and anything hanging from them).
            var partHidden = new FixedList512Bytes<bool>();
            var segHidden = new FixedList512Bytes<bool>();
            segHidden.Length = math.min(b.Segments.Length, 500);

            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                var parentPos = part.Parent < 0 ? rootPos : endPos[part.Parent];
                var parentRot = part.Parent < 0 ? rootRot : endRot[part.Parent];
                var pos = parentPos + math.mul(parentRot, part.Socket * scale);
                var rot = math.mul(parentRot, part.Rest);
                var hip = pos;
                var phase = math.frac(s.Phase + part.Phase);
                var hidden = part.Parent >= 0 && partHidden[part.Parent] ||
                             part.StyleGroup > 0 && CreatureBlob.Style(id, part.StyleGroup, part.StyleCount) != part.StyleIndex;
                partHidden.Add(hidden);
                var torso = b.Upright && part.Parent < 0 && part.Role != LimbRole.Leg;
                var amplitude = part.Role == LimbRole.Arm ? armSwing : torso ? twist : 1f;

                // Legs hanging beneath the body fold up when it lies down (front legs half-fold for a play bow);
                // splayed legs (lizards, spiders) keep their feet planted and bend at the knees instead.
                var fold = 0f;
                var folds = part.Role == LimbRole.Leg && part.Parent < 0 && math.mul(part.Rest, new float3(0f, 0f, 1f)).y < -0.7f && !b.Upright;
                var hind = part.Socket.z < 0f;
                if (folds)
                {
                    fold = math.max(s.Lie, part.Socket.z > 0f ? s.Bow * 0.5f : 0f);
                    // Sitting folds the hind legs; sitting up tall tucks the front paws too; in flight all tuck up.
                    if (hind)
                        fold = math.max(fold, s.Sit);
                    else if (s.Mode != CreatureMode.Climb && s.Mode != CreatureMode.Web)
                        fold = math.max(fold, math.saturate(s.Vertical * 1.6f));
                    // In flight the legs trail back under the tail instead (below).
                    fold *= 1f - s.Fly;
                    // Carried: legs tucked up, not dangling.
                    if (s.Mode == CreatureMode.Held)
                        fold = math.max(fold, 0.85f);
                }
                // A leg busy with an idle action (pawing, scratching) isn't planted.
                var busyLeg = UsedByAction(ref part, in s);
                var gaitMove = part.Role == LimbRole.Leg ? move * (1f - s.Fly) : move;
                // A snake's body (a chain that lies on the ground, off the body): serpentine. It lies along one wave
                // that stays put on the ground, sideways offset A·sin(k(D − u)) at distance u back from the head after
                // it has gone D: as it moves on, the wave runs back along the body at its own speed, so every part of
                // the body follows the head's track (rather than a tail wagging behind a straight head). Stopped, it
                // keeps its S. The wavelength is its stride, so the gait phase is how far along the wave it is.
                var serpent = part.FollowGround && part.Parent < 0;
                var wave = math.max(b.WalkStride * scale, 0.05f);
                var back = b.BodyLength * scale * 1.2f;
                var heading = 0f;

                for (int j = 0; j < part.SegmentCount; j++)
                {
                    var seg = part.FirstSegment + j;
                    var jointPhase = math.frac(phase - j * part.JointPhaseStep);
                    float3 a;
                    if (serpent)
                    {
                        // Each segment turned to the wave's direction at its middle; the joint turns by the change.
                        // (In the air the wave runs up and down the body, not side to side: a sky serpent's.)
                        var length = b.Segments[seg].Length * scale;
                        var k = 2f * math.PI / wave;
                        var direction = math.atan(0.12f * wave * k * math.cos(k * (s.Phase * wave - (back + length * 0.5f))));
                        a = new float3((direction - heading) * s.Fly, (direction - heading) * (1f - s.Fly), 0f);
                        heading = direction;
                        back += length;
                    }
                    else if (part.Role == LimbRole.Wing)
                    {
                        // Folded at rest; in flight, beating through the walk keys (or held spread, gliding).
                        var k = seg * CreatureBlob.PoseCount;
                        var flap = CreatureBlob.Cycle(b.Poses[k + CreatureBlob.WalkReach], b.Poses[k + CreatureBlob.WalkPush],
                                                      b.Poses[k + CreatureBlob.WalkLift], jointPhase);
                        a = math.lerp(b.Poses[k + CreatureBlob.Idle], flap, s.Fly);
                    }
                    else if (part.Role == LimbRole.Leg && sprint > 0f)
                    {
                        // A sprinting leg: the longer stride, and in the swing the knee driven high and the heel kicked up.
                        a = b.LegAngles(seg, jointPhase, gaitMove, run, sprint);
                        if (jointPhase >= 0.5f)
                        {
                            var swing = math.sin((jointPhase - 0.5f) * 2f * math.PI);
                            if (j == 0) a.x -= 0.55f * sprint * swing;
                            else if (j == 1) a.x += 1.1f * sprint * swing;
                        }
                    }
                    else
                        a = b.Angles(seg, jointPhase, gaitMove, run);
                    // A sprinter's arms pump hard, elbows bent.
                    if (part.Role == LimbRole.Arm && sprint > 0f)
                    {
                        var rest = b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle];
                        a = rest + (a - rest) * (1f + 0.5f * sprint);
                        if (j == 1)
                            a.x -= 0.85f * sprint;
                    }
                    if (amplitude != 1f)
                    {
                        var idle = b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle];
                        a = idle + (a - idle) * amplitude;
                    }
                    // Carrying something in both arms: they hold it, rather than swinging with the stride.
                    if (part.Role == LimbRole.Arm && s.Action == IdleAction.Carry)
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], act);
                    if (torso && j == 0)
                        a.x += lean;
                    if (part.Role == LimbRole.Head && j == 0)
                        a += headTilt;
                    // A guard's right arm: bent to hold its weapon upright at its side once it's drawn, or raised forward
                    // with a torch; running at a beast, the weapon levelled at it, held low and forward at the hip. (With
                    // the weapon worn, or an elf's bow on its back, the arms swing free.)
                    if (part.Role == LimbRole.Arm && part.Side > 0f && s.Task == CreatureTask.Patrol && (s.Held == HeldItem.SpearDrawn && b.GuardKit != GuardKit.Elf || s.Held == HeldItem.Torch))
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.8f);
                        var torch = s.Held == HeldItem.Torch;
                        var levelled = s.Mode == CreatureMode.Patrol && s.Stage == 2 && !torch;
                        a.x += j == 0 ? (torch ? -0.75f : levelled ? -0.45f : -0.15f) : j == 1 ? (torch ? -0.9f : levelled ? -0.6f : -1.05f) : 0f;
                    }
                    // Reaching for the weapon, drawing it or putting it away: the right hand up over the shoulder to
                    // the back, or down across to the left hip, and back, in half a second.
                    if (part.Role == LimbRole.Arm && part.Side > 0f && (s.Action == IdleAction.Draw || s.Action == IdleAction.Sheathe))
                    {
                        var reach = math.sin(math.saturate(s.ActionTime) * math.PI);
                        var toBack = s.Task == CreatureTask.Hunt || GuardWeapons.OnBack(b.GuardKit, s.Scale, b.StandHeight);   // (a hunter's spear is always on its back)
                        a += (j == 0 ? (toBack ? new float3(-2.5f, 0f, -0.35f) : new float3(-0.35f, 0f, 0.75f))
                            : j == 1 ? (toBack ? new float3(-1.4f, 0f, 0f) : new float3(-1.0f, 0f, 0f)) : float3.zero) * reach;
                    }
                    // A traveller's staff in the right hand: the arm a little forward and bent, planting it as it goes.
                    if (part.Role == LimbRole.Arm && part.Side > 0f && s.Held == HeldItem.Staff)
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.7f);
                        a.x += j == 0 ? -0.3f : j == 1 ? -0.7f : 0f;
                    }
                    // Meeting a messenger bird: the left forearm raised out in front at chest height, bent, for it to land
                    // on; once it's there, the right hand across to take the roll from its leg.
                    if (b.Upright && part.Role == LimbRole.Arm && s.Mode == CreatureMode.Idle && (s.Stage == MessageWait || s.Stage == MessageTake))
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.9f);
                        if (part.Side < 0f)
                            a += j == 0 ? new float3(-0.85f, 0f, part.Side * 0.15f) : j == 1 ? new float3(-0.75f, 0f, 0f) : float3.zero;
                        else if (s.Stage == MessageTake)
                        {
                            var reach = math.smoothstep(0f, 0.5f, s.StageTime);
                            a += (j == 0 ? new float3(-1.25f, 0f, part.Side * 0.6f) : j == 1 ? new float3(-0.7f, 0f, 0f) : float3.zero) * reach;
                        }
                    }
                    // Reading a message: both hands up in front holding it open, head bent to it.
                    if (b.Upright && s.Held == HeldItem.Letter && part.Role == LimbRole.Arm)
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.85f);
                        a += j == 0 ? new float3(-0.45f, 0f, part.Side * 0.35f) : j == 1 ? new float3(-1.8f, 0f, 0f) : float3.zero;
                    }
                    if (b.Upright && s.Held == HeldItem.Letter && part.Role == LimbRole.Head)
                        a.x += 0.3f;
                    // A sack of ore carried in both arms against the chest.
                    if (part.Role == LimbRole.Arm && s.Held == HeldItem.OreSack)
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.85f);
                        a += j == 0 ? new float3(-0.8f, 0f, part.Side * 0.5f) : j == 1 ? new float3(-1.75f, 0f, 0f) : float3.zero;
                    }
                    // A lantern in the off hand: that arm held a little forward and bent, hardly swinging.
                    if (part.Role == LimbRole.Arm && part.Side < 0f && s.Held == HeldItem.Lantern)
                    {
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.75f);
                        a.x += j == 0 ? -0.3f : j == 1 ? -0.55f : 0f;
                    }
                    a += Secondary(ref part, in s, j, run, b.Upright);
                    a += TaskPose(ref part, in s, j);
                    if (swimming > 0f)
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle] + SwimPose(ref part, in s, j), swimming);
                    if (b.Upright && s.Sit > 0f)
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle] + SitPose(ref part, in s, j), s.Sit);
                    if (b.Upright && s.Lie > 0f)
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle] + LiePose(ref part, in s, j), s.Lie);
                    if (b.Upright && part.Role == LimbRole.Arm && j <= 1 && s.Mode == CreatureMode.Sit && s.Stage >= HearthStand && s.Stage <= HearthSeat)
                        a = HearthPose(ref part, in s, j, a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle]);
                    // (Upright bodies have no idle actions of their own, but players' bodies take interaction poses.)
                    if (s.Action != IdleAction.None && (!b.Upright || s.Action >= IdleAction.Pet))
                        a += ActionPose(ref part, in s, j, act);
                    // An upright body's elbow bends one way only (negative pitch, forward): never past straight, nor
                    // folded beyond 150°, however much swing, sprint or pose is added up.
                    if (b.Upright && part.Role == LimbRole.Arm && j == 1)
                        a.x = math.clamp(a.x, -2.6f, 0f);
                    if (seg < segHidden.Length)
                        segHidden[seg] = hidden;
                    if (fold > 0f)
                        a.x += Fold(j, part.SegmentCount) * fold;
                    // Flying: legs swept back along the belly, feet tucked; nose up (flaring to land, or climbing
                    // away), they come down and forward to meet the ground.
                    var tuck = s.Fly * (1f - math.saturate(s.Vertical * 2f));
                    if (folds && tuck > 0f)
                        a.x += (j == 0 ? 1.35f : j == part.SegmentCount - 1 ? 0.6f : -0.2f) * tuck;
                    if (folds && s.Fly > 0f && s.Vertical > 0f && j == 0)
                        a.x -= 0.5f * s.Fly * math.saturate(s.Vertical * 2f);
                    rot = math.mul(rot, quaternion.EulerZXY(a));
                    // (Swimming or in the air, the chain keeps the body's line rather than draping to the ground.)
                    if (part.FollowGround && !aquatic && s.Fly < 0.5f)
                        rot = LieOnGround(in terrain, pos, rot, b.Segments[seg].Length * scale, b.Segments[seg].RadiusEnd * b.Segments[seg].Flatten * scale);
                    if (seg < MaxSegments)
                    {
                        segPos[seg] = pos;
                        segRot[seg] = rot;
                    }
                    pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length * scale));
                }

                // Folk's legs out along the ground, sitting or lying back: swung up or down about the hip so the heel
                // rests on the ground under it, wherever that is (on a slope too), rather than the leg running into it.
                var laid = b.Upright && part.Role == LimbRole.Leg && part.Parent < 0 && part.SegmentCount >= 3 &&
                            part.FirstSegment + part.SegmentCount <= MaxSegments
                    ? math.max(s.Sit * (CrossLegged(in s) || Seated(in s) ? 0f : 1f), s.Lie) : 0f;
                if (laid > 0.01f)
                {
                    var fs = part.FirstSegment + part.SegmentCount - 1;
                    var heel = segPos[fs];
                    var reach = math.length(heel - hip);
                    var rest = GroundAt(terrain, heel.xz, s.Position.y) + b.Segments[fs].RadiusStart * scale;
                    if (reach > 1e-3f)
                    {
                        var turn = (math.asin(math.clamp((rest - hip.y) / reach, -1f, 1f)) - math.asin(math.clamp((heel.y - hip.y) / reach, -1f, 1f))) * laid;
                        var axis = math.normalizesafe(math.cross(heel - hip, new float3(0f, 1f, 0f)));
                        var q = quaternion.AxisAngle(axis, turn);
                        for (int j = 0; j < part.SegmentCount; j++)
                        {
                            var seg = part.FirstSegment + j;
                            segPos[seg] = hip + math.mul(q, segPos[seg] - hip);
                            segRot[seg] = math.mul(q, segRot[seg]);
                        }
                        pos = hip + math.mul(q, pos - hip);
                        rot = math.mul(q, rot);
                    }
                }

                if (part.Role == LimbRole.Leg && part.Parent < 0 && part.SegmentCount >= 2 && part.FirstSegment + part.SegmentCount <= MaxSegments &&
                    detail && !busyLeg && s.Fly < 0.5f && up < 0.5f && !crawling && (!b.Upright || s.Sit + s.Lie < 0.3f))
                {
                    // Where the ground would be under this foot if it were the flat, sloped plane the body is
                    // standing on, versus where it really is: bend the leg by the difference.
                    var along = math.dot(pos.xz - s.Position.xz, forward.xz);
                    var nominal = ground + along * gradient;
                    var actual = GroundAt(terrain, pos.xz, s.Position.y);
                    var reach = 0f;
                    for (int j = 0; j < part.SegmentCount; j++)
                        reach += b.Segments[part.FirstSegment + j].Length * scale;
                    var correction = ((actual - nominal) * (1f - fold) + (folds ? 0f : lowered) + sink) * (1f - airborne);
                    correction = math.clamp(correction, -0.45f * reach, 0.45f * reach);
                    pos += PlaceFoot(ref b, ref part, ref segPos, ref segRot, new float3(0f, correction, 0f), scale);

                    // A foot that lies along the ground (folk's feet) tips with it while standing, toes up a rise and down
                    // a fall, by at most 30°: the ground just ahead of and behind it, turned about the ankle. (Walking,
                    // the heel and toe below roll it through each step instead.)
                    if (part.SegmentCount >= 3 && fold < 0.5f && move < 0.5f)
                    {
                        var fs = part.FirstSegment + part.SegmentCount - 1;
                        var len = b.Segments[fs].Length * scale;
                        var footDir = math.mul(segRot[fs], new float3(0f, 0f, 1f));
                        if (math.abs(footDir.y) < 0.6f && len > 1e-3f)
                        {
                            var flat = math.normalizesafe(new float2(footDir.x, footDir.z));
                            var mid = segPos[fs].xz + flat * len * 0.5f;
                            var span = len * 0.5f + 0.05f * scale;
                            var rise = GroundAt(terrain, mid + flat * span, s.Position.y) - GroundAt(terrain, mid - flat * span, s.Position.y);
                            var tilt = math.clamp(math.atan2(rise, 2f * span), -0.52f, 0.52f);
                            var right = math.mul(segRot[fs], new float3(1f, 0f, 0f));
                            segRot[fs] = math.mul(quaternion.AxisAngle(right, -tilt), segRot[fs]);
                            pos = segPos[fs] + math.mul(segRot[fs], new float3(0f, 0f, len));
                            rot = segRot[fs];
                        }
                    }

                    // Heel and toe (an upright body walking, fading out into a run): the foot comes down heel first,
                    // toes up, lies flat through the middle of the step, then rolls up onto its toes to push off (the
                    // toes staying put and the heel rising, the leg bending to let it), and swings through to the next
                    // heel strike.
                    var walking = move * (1f - run);
                    if (b.Upright && part.SegmentCount == 3 && walking > 0.01f)
                    {
                        var fs = part.FirstSegment + 2;
                        var len = b.Segments[fs].Length * scale;
                        var footRoll = walking * HeelToe(phase);
                        var right = math.mul(segRot[fs], new float3(1f, 0f, 0f));
                        var turned = math.mul(quaternion.AxisAngle(right, footRoll), segRot[fs]);
                        if (footRoll > 0f)
                        {
                            // Up on the toes: the ankle lifts so the toe end stays where it was.
                            var toe = segPos[fs] + math.mul(segRot[fs], new float3(0f, 0f, len));
                            PlaceFoot(ref b, ref part, ref segPos, ref segRot, toe - math.mul(turned, new float3(0f, 0f, len)) - segPos[fs], scale);
                        }
                        segRot[fs] = turned;
                        pos = segPos[fs] + math.mul(turned, new float3(0f, 0f, len));
                        rot = turned;
                    }
                }

                endPos.Add(pos);
                endRot.Add(rot);
            }

            for (int seg = 1; seg < b.Segments.Length && seg < MaxSegments; seg++)
            {
                ref var segment = ref b.Segments[seg];
                if (seg < segHidden.Length && segHidden[seg])
                {
                    output[offset + seg] = default;   // zero size: not drawn
                    continue;
                }
                var colour = b.SegmentColor(seg, coat);
                if (segment.Hair != 0)
                    colour = b.HairColor(id, colour);
                output[offset + seg] = Instance(segPos[seg], segRot[seg], ref segment, scale, colour);
            }
        }

        /// <summary>How far into an idle action it is, as a weight: eases in, holds, eases out.</summary>
        static float ActionEnvelope(float t) => math.smoothstep(0f, 0.12f, t) * (1f - math.smoothstep(0.85f, 1f, t));

        /// <summary>A weight that's 1 between a and b (easing over a tenth either side), for the phases of an action.</summary>
        static float Window(float t, float a, float b) => math.smoothstep(a, a + 0.1f, t) * (1f - math.smoothstep(b - 0.1f, b, t));

        static bool FrontLeg(ref CreaturePart part) => part.Role == LimbRole.Leg && part.Parent < 0 && part.Socket.z > 0f;
        static bool HindLeg(ref CreaturePart part) => part.Role == LimbRole.Leg && part.Parent < 0 && part.Socket.z < 0f;

        /// <summary>The right front leg paws, the right hind leg scratches.</summary>
        static bool UsedByAction(ref CreaturePart part, in CreatureState s) =>
            part.Side > 0f && (s.Action == IdleAction.PawGround && FrontLeg(ref part) || s.Action == IdleAction.Scratch && HindLeg(ref part));

        /// <summary>
        /// The joint angles an idle action adds (weighted by how far into it the animal is). Legs: negative pitch
        /// reaches forward, positive swings back. Necks and heads turn by yaw (pitch nods them down).
        /// </summary>
        static float3 ActionPose(ref CreaturePart part, in CreatureState s, int joint, float e)
        {
            var n = math.max(part.SegmentCount, 1);
            var t = s.ActionTime;
            // Grooming and preening turn to one side or the other, fixed per individual.
            var side = CreatureBlob.Trait(s.Individual, 13) > 0f ? 1f : -1f;
            switch (s.Action)
            {
                case IdleAction.PawGround:
                    if (UsedByAction(ref part, in s))
                    {
                        // Reach forward, then drag the hoof back through the dirt.
                        var w = math.sin(math.PI * 2f * math.frac(s.Clock * 1.3f));
                        return joint == 0 ? new float3((-0.5f * w - 0.25f) * e, 0f, 0f)
                             : joint == 1 ? new float3(-0.6f * math.max(0f, w) * e, 0f, 0f) : float3.zero;
                    }
                    if (part.Role == LimbRole.Head) return new float3(0.3f * e, 0f, 0f);
                    if (part.Role == LimbRole.Neck) return new float3(0.35f * e / n, 0f, 0f);
                    break;
                case IdleAction.Groom:
                    if (part.Role == LimbRole.Neck) return new float3(0.25f * e / n, side * 1.3f * e / n, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.3f + 0.12f * math.sin(s.Clock * 10f)) * e, side * 0.7f * e, 0f);
                    break;
                case IdleAction.Scratch:
                    if (UsedByAction(ref part, in s))
                        return joint == 0 ? new float3((-1.1f + 0.18f * math.sin(s.Clock * 30f)) * e, 0f, 0f)
                             : joint == 1 ? new float3(0.8f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.1f * e, 0f, 0.45f * e);
                    if (part.Role == LimbRole.Neck) return new float3(0f, 0.4f * e / n, 0f);
                    break;
                case IdleAction.Shake:
                    if (part.Role == LimbRole.Ear || part.Role == LimbRole.Tail)
                        return new float3(0f, 0.6f * math.sin(s.Clock * 28f) * e * (1f + joint * 0.5f), 0f);
                    break;
                case IdleAction.Stretch:
                {
                    // Chest down with the front legs out, then the hind legs stretched back with the head up.
                    float first = Window(t, 0.05f, 0.5f), second = Window(t, 0.5f, 0.95f);
                    if (FrontLeg(ref part) && joint == 0) return new float3(-0.7f * first, 0f, 0f);
                    if (HindLeg(ref part) && joint == 0) return new float3(0.6f * second, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3(-0.3f * second, 0f, 0f);
                    if (part.Role == LimbRole.Tail) return new float3(-0.3f * second, 0f, 0f);
                    break;
                }
                case IdleAction.Sniff:
                    if (part.Role == LimbRole.Neck) return new float3(0.8f * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.35f + 0.07f * math.sin(s.Clock * 16f)) * e, 0f, 0f);
                    break;
                case IdleAction.TailFlick:
                    if (part.Role == LimbRole.Tail)
                        return new float3(-0.2f * e, 0.7f * math.sin(s.Clock * 7f) * e * (1f + joint * 0.5f), 0f);
                    break;
                case IdleAction.Preen:
                    if (part.Role == LimbRole.Neck) return new float3(0.2f * e / n, side * 2f * e / n, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.4f + 0.1f * math.sin(s.Clock * 14f)) * e, side * 0.8f * e, 0f);
                    if (part.Role == LimbRole.Wing && part.Side * side > 0f && joint == 0) return new float3(0f, 0f, 0.25f * e * part.Side);
                    break;
                case IdleAction.HeadBob:
                    if (part.Role == LimbRole.Head) return new float3(-0.2f * e, 0f, 0f);
                    break;
                case IdleAction.Howl:
                    // Muzzle up to the sky, ears back, a slow sway as the note rises and falls.
                    if (part.Role == LimbRole.Neck) return new float3(-0.9f * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3((-0.7f + 0.08f * math.sin(s.Clock * 1.7f)) * e, 0f, 0f);
                    if (part.Role == LimbRole.Ear) return new float3(0.5f * e, 0f, 0f);
                    break;
                case IdleAction.LookAround:
                case IdleAction.SitUp:
                    if (part.Role == LimbRole.Head) return new float3(-0.15f * e, 0f, 0f);
                    if (part.Role == LimbRole.Ear) return new float3(-0.3f * e, 0f, 0f);
                    break;
                case IdleAction.Pet:
                {
                    // A player petting an animal: leaning over it, the right hand reaching down and stroking (a body
                    // with no arms noses at it instead).
                    var stroke = math.sin(s.Clock * 3.2f);
                    if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0) return new float3(0.45f * e, 0f, 0f);
                    if (part.Role == LimbRole.Arm && part.Side > 0f)
                        return joint == 0 ? new float3((-0.95f + 0.18f * stroke) * e, 0f, 0f)
                             : joint == 1 ? new float3((-0.25f - 0.15f * stroke) * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Neck) return new float3(0.5f * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.45f + 0.06f * stroke) * e, 0f, 0f);
                    break;
                }
                case IdleAction.Poke:
                    // A child jabbing at the ground ahead with a stick: the right arm down and forward, jabbing; head down
                    // to watch.
                    if (part.Role == LimbRole.Arm && part.Side > 0f)
                        return joint == 0 ? new float3((-0.85f + 0.25f * math.max(0f, math.sin(s.Clock * 9f))) * e, 0f, 0f) : joint == 1 ? new float3(-0.15f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.45f * e, 0f, 0f);
                    if (part.Role == LimbRole.Neck) return new float3(0.3f * e, 0f, 0f);
                    break;
                case IdleAction.Throw:
                    // A player lunging at game: right arm thrown forward, leaning into it (a body with no arms lunges
                    // head first).
                    if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0) return new float3(0.3f * e, 0f, 0f);
                    if (part.Role == LimbRole.Arm && part.Side > 0f)
                        return joint == 0 ? new float3(-1.5f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.15f * e, 0f, 0f);
                    break;
                case IdleAction.Carry:
                    // A player carrying an animal: both arms forward with the elbows bent, cradling it at the chest.
                    if (part.Role == LimbRole.Arm)
                        return joint == 0 ? new float3(-0.35f * e, 0f, 0f) : joint == 1 ? new float3(-1.25f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.2f * e, 0f, 0f);
                    break;
                // Emotes (a player's, or folk answering one). Bodies without arms make do: a nod, a wag of the tail.
                case IdleAction.Wave:
                {
                    // The right arm up, swinging side to side from the shoulder; head tilted a little.
                    var swing = math.sin(s.Clock * 9f);
                    if (part.Role == LimbRole.Arm && part.Side > 0f)
                        return joint == 0 ? new float3(-2.55f * e, 0f, 0.35f * swing * e) : joint == 1 ? new float3(-0.45f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(-0.1f * e, 0f, 0.15f * e);
                    if (part.Role == LimbRole.Tail) return new float3(-0.2f * e, 0.6f * swing * e * (1f + joint * 0.5f), 0f);
                    break;
                }
                case IdleAction.Bow:
                {
                    // Bent at the waist, head down, then up again (a body without a torso part bows its neck).
                    var w = Window(t, 0.05f, 0.8f) * e;
                    if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0) return new float3(0.75f * w, 0f, 0f);
                    if (part.Role == LimbRole.Neck) return new float3(0.6f * w / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3(0.35f * w, 0f, 0f);
                    if (part.Role == LimbRole.Arm && joint == 0) return new float3(-0.25f * w, 0f, 0f);
                    break;
                }
                case IdleAction.Cheer:
                {
                    // Both arms flung up and shaken, face to the sky.
                    var shake = math.sin(s.Clock * 11f + (part.Side > 0f ? 0f : 1.3f));
                    if (part.Role == LimbRole.Arm)
                        return joint == 0 ? new float3((-2.8f + 0.15f * shake) * e, 0f, 0f) : joint == 1 ? new float3(-0.2f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(-0.3f * e, 0f, 0f);
                    if (part.Role == LimbRole.Tail) return new float3(-0.4f * e, 0.5f * shake * e, 0f);
                    break;
                }
                case IdleAction.Dance:
                {
                    // Arms pumping in turn with the elbows bent, hips swaying, head bobbing to the beat.
                    var beat = s.Clock * 7f + (s.Individual % 7u) * 0.4f;
                    if (part.Role == LimbRole.Arm)
                    {
                        var pump = math.max(0f, math.sin(beat + (part.Side > 0f ? 0f : math.PI)));
                        return joint == 0 ? new float3((-0.9f - 0.8f * pump) * e, 0f, 0f) : joint == 1 ? new float3(-1.3f * e, 0f, 0f) : float3.zero;
                    }
                    if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0) return new float3(0f, 0.25f * math.sin(beat * 0.5f) * e, 0f);
                    if (part.Role == LimbRole.Head) return new float3(0.15f * math.sin(beat) * e, 0f, 0f);
                    if (part.Role == LimbRole.Tail) return new float3(-0.3f * e, 0.7f * math.sin(beat) * e * (1f + joint * 0.5f), 0f);
                    break;
                }
            }
            return float3.zero;
        }

        /// <summary>
        /// A worker's arms at its post (only once it's there and standing). Arms hang down from the shoulder:
        /// negative pitch raises them forwards, positive swings them back; mirrored arms keep the same pitch.
        /// </summary>
        /// <summary>A miner's stage at the ore face, swinging its pick (CreatureBrainSystem's mining round).</summary>
        public const byte MinerAtFace = 101;
        /// <summary>Sitting-mode stages at the town's fire: standing by it, sitting on the ground by it, on a seat near it.</summary>
        public const byte HearthStand = 5, HearthGround = 6, HearthSeat = 7;
        /// <summary>Idle-mode stages of one of the townsfolk a messenger bird has come to: waiting for it (the forearm
        /// up for it to land on), taking the message from its leg, reading it.</summary>
        public const byte MessageWait = 60, MessageTake = 61, MessageRead = 62;
        /// <summary>Work-mode stages of a bear raiding a wild hive: on its way, reared up at the comb, making off.
        /// (The bees mob it while it's at the comb: CreatureRenderSystem's Swarm.)</summary>
        public const byte RaidWalk = 150, RaidAtComb = 151, RaidLeave = 152;

        static float3 TaskPose(ref CreaturePart part, in CreatureState s, int joint)
        {
            if (part.Role == LimbRole.Arm && joint <= 1 && s.Mode == CreatureMode.Hunt && s.Task == CreatureTask.Hunt)
                return HuntPose(part.Side > 0f, s.Stage, joint);
            if (part.Role != LimbRole.Arm || s.Mode != CreatureMode.Work || s.Stage == 0 || s.Task == CreatureTask.None || joint > 1)
                return float3.zero;
            // (A miner swings its pick only at the ore face, not on its way there and back.)
            if (s.Task == CreatureTask.Mine && s.Stage != MinerAtFace)
                return float3.zero;
            var right = part.Side > 0f;
            var t = s.Clock + (s.Individual % 100) * 0.13f;
            switch (s.Task)
            {
                case CreatureTask.Hammer:
                case CreatureTask.Chop:
                case CreatureTask.Mine:
                {
                    // Raise slowly, strike fast; a hammer is one-handed (the other hand holds the work), an axe or a
                    // miner's pick two.
                    var swinging = right || s.Task == CreatureTask.Chop || s.Task == CreatureTask.Mine;
                    if (!swinging)
                        return new float3(joint == 0 ? -0.7f : -0.7f, 0f, 0f);
                    var p = math.frac(t * (s.Task == CreatureTask.Chop ? 0.55f : 0.85f));
                    var lift = p < 0.7f ? math.smoothstep(0f, 0.7f, p) : 1f - math.smoothstep(0.7f, 0.8f, p);
                    return new float3(joint == 0 ? -(0.45f + 1.8f * lift) : -(0.35f + 0.9f * lift), 0f, 0f);
                }
                case CreatureTask.Gesture:
                {
                    // Hands resting on the counter; every so often one rises to beckon or point.
                    var p = math.frac(t * 0.21f + (right ? 0f : 0.5f));
                    var raise = math.smoothstep(0f, 0.06f, p) * (1f - math.smoothstep(0.14f, 0.24f, p));
                    return new float3(joint == 0 ? -0.45f - 1.2f * raise : -0.9f + 0.4f * raise, 0f, 0f);
                }
                case CreatureTask.Tend:
                    return new float3(joint == 0 ? -0.65f + 0.22f * math.sin(t * 1.5f + (right ? 0f : 1.4f)) : -0.55f, 0f, 0f);
                case CreatureTask.Watch:
                    return new float3(joint == 0 ? 0.35f : -0.75f, 0f, 0f);   // hands clasped behind the back
                case CreatureTask.Fish:
                {
                    // Both hands on the rod held out over the water; casting, the right swings it up over the shoulder
                    // and whips it through; landing a fish, both lift it high. (The rod is drawn to match: see
                    // CreatureRenderSystem's Rod.)
                    if (s.Stage == 3)
                        return new float3(joint == 0 ? -1.5f : -0.6f, 0f, 0f);
                    if (s.Stage == 2 && right)
                        return new float3(joint == 0 ? -0.75f - 1.85f * CastBack(s.ActionTime) : -0.5f, 0f, 0f);
                    return new float3(joint == 0 ? -0.75f : -0.55f, 0f, 0f);
                }
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Folk sitting on a seat (below), or on the ground one of two ways (by individual, a little under half of them
        /// cross-legged):
        /// legs out in front, knees a little bent and apart, leaning back a touch on hands set behind (the legs then
        /// laid along the ground under them); or cross-legged, knees out to the sides, shins crossed in front, sitting
        /// up with the hands on the knees.
        /// </summary>
        static float3 SitPose(ref CreaturePart part, in CreatureState s, int joint)
        {
            // On a seat: thighs out level along it, shins down to the ground, hands on the thighs.
            if (Seated(in s))
                switch (part.Role)
                {
                    case LimbRole.Leg:
                        return joint == 0 ? new float3(-1.5f, part.Side * 0.1f, 0f) : joint == 1 ? new float3(1.5f, 0f, 0f) : float3.zero;
                    case LimbRole.Arm:
                        return joint == 0 ? new float3(-0.45f, 0f, part.Side * 0.15f) : joint == 1 ? new float3(-0.55f, 0f, 0f) : float3.zero;
                    default:
                        return float3.zero;
                }
            if (CrossLegged(in s))
                switch (part.Role)
                {
                    case LimbRole.Leg:
                    {
                        // The thigh out and forward from the hip and turned out, so the knee bends across the body: the
                        // shin folds back towards the other hip. (Solved for: thigh about level, out at 50 degrees;
                        // shin back across, level.) One shin over the other, by individual: the upper a little raised.
                        var over = (CreatureBlob.Trait(s.Individual, 31) > 0f) == (part.Side > 0f);
                        return joint == 0 ? new float3(-0.7f, part.Side * (over ? 1.42f : 1.56f), -part.Side * 3f) : joint == 1 ? new float3(2.5f, 0f, 0f) : float3.zero;
                    }
                    case LimbRole.Arm:
                        return joint == 0 ? new float3(-0.3f, 0f, part.Side * 0.45f) : joint == 1 ? new float3(-0.45f, 0f, 0f) : float3.zero;
                    default:
                        return float3.zero;
                }
            switch (part.Role)
            {
                case LimbRole.Leg:
                    return joint == 0 ? new float3(-1.38f, part.Side * 0.12f, 0f) : joint == 1 ? new float3(0.1f, 0f, 0f) : float3.zero;
                case LimbRole.Arm:
                    return joint == 0 ? new float3(0.6f, 0f, part.Side * 0.2f) : float3.zero;
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Folk at the town's fire. Some (by individual) warm their hands a while, both held out to it, then drop them
        /// again; now and then one talking lifts its right hand to make its point. (Not one leaning back on its hands,
        /// sitting with its legs out.) `idle` is the arm's idle pose, `a` the pose so far.
        /// </summary>
        static float3 HearthPose(ref CreaturePart part, in CreatureState s, int joint, float3 a, float3 idle)
        {
            var leaning = s.Stage == HearthGround && !CrossLegged(in s);
            var right = part.Side > 0f;
            var phase = (s.Individual % 97u) * 0.31f;
            var warm = CreatureBlob.Trait(s.Individual, 42) > -0.3f && !leaning
                ? math.smoothstep(0.0f, 0.08f, math.frac(s.Clock * 0.025f + phase)) * (1f - math.smoothstep(0.55f, 0.63f, math.frac(s.Clock * 0.025f + phase)))
                : 0f;
            // Warming: forearms raised towards the fire, hands a little apart.
            var held = joint == 0 ? new float3(-1.05f, 0f, -part.Side * 0.15f) : new float3(-0.75f, 0f, 0f);
            a = math.lerp(a, idle + held, warm);
            // Talking: the right hand up and out, turned open, then down again (not while warming).
            if (right && warm < 0.5f && CreatureBlob.Trait(s.Individual, 43) > -0.5f)
            {
                var p = math.frac(s.Clock * 0.09f + phase * 1.7f);
                var raise = math.smoothstep(0f, 0.05f, p) * (1f - math.smoothstep(0.16f, 0.24f, p));
                var flick = math.sin(s.Clock * 5f) * 0.12f;
                var talk = joint == 0 ? new float3(-0.8f + flick, 0f, 0.25f) : new float3(-1.1f, 0f, 0f);
                a = math.lerp(a, idle + talk, raise * (leaning ? 0f : 1f));
            }
            return a;
        }

        /// <summary>Sitting on a seat (a bench, a log, a stump...), at the height in Anchor, rather than on the ground.</summary>
        static bool Seated(in CreatureState s) => s.Mode == CreatureMode.Sit && (s.Stage == 4 || s.Stage == HearthSeat);

        /// <summary>Whether this one sits on the ground cross-legged rather than with its legs out (fixed for life).</summary>
        static bool CrossLegged(in CreatureState s) => CreatureBlob.Trait(s.Individual, 29) > 0.1f;

        /// <summary>
        /// Folk lying back in the grass: hands behind the head, legs out straight, or (some of them) one knee drawn up.
        /// </summary>
        static float3 LiePose(ref CreaturePart part, in CreatureState s, int joint)
        {
            switch (part.Role)
            {
                case LimbRole.Arm:
                    return joint == 0 ? new float3(-2.8f, 0f, 0f) : joint == 1 ? new float3(-1.8f, 0f, 0f) : float3.zero;
                case LimbRole.Leg:
                    if (part.Side > 0f && CreatureBlob.Trait(s.Individual, 17) > 0f)
                        return joint == 0 ? new float3(-0.7f, 0f, 0f) : joint == 1 ? new float3(1.3f, 0f, 0f) : float3.zero;
                    return float3.zero;
                case LimbRole.Head:
                    return new float3(0.25f, 0f, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Breaststroke, for a body lying forward in the water: the arms reach out ahead, sweep round and out, and come
        /// back in under the chest bent; the legs draw up and kick back out together, a beat behind; the head held up
        /// to see ahead.
        /// </summary>
        static float3 SwimPose(ref CreaturePart part, in CreatureState s, int joint)
        {
            var p = 2f * math.PI * s.Phase;
            switch (part.Role)
            {
                case LimbRole.Arm:
                    if (joint == 0)
                        return new float3(-2.3f + 0.7f * math.sin(p), part.Side * 0.6f * math.max(0f, math.sin(p)), 0f);
                    return joint == 1 ? new float3(-0.3f - 0.6f * (0.5f + 0.5f * math.cos(p)), 0f, 0f) : float3.zero;
                case LimbRole.Leg:
                {
                    var draw = 0.5f + 0.5f * math.sin(p + math.PI);
                    return joint == 0 ? new float3(-0.45f * draw, part.Side * 0.25f * draw, 0f) : joint == 1 ? new float3(1.3f * draw, 0f, 0f) : float3.zero;
                }
                case LimbRole.Neck:
                    return new float3(-0.75f / math.max(part.SegmentCount, 1), 0f, 0f);
                case LimbRole.Head:
                    return new float3(-0.9f, 0f, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>How far back over the shoulder a cast has the rod (0 out over the water, 1 right back), through
        /// the cast (0..1): up and back, then whipped through.</summary>
        public static float CastBack(float t) => math.smoothstep(0f, 0.25f, t) * (1f - math.smoothstep(0.25f, 0.6f, t));

        /// <summary>
        /// A hunter's arms: creeping up, both carried low in front (the spear); aiming, the right drawn back high over
        /// the shoulder with the left pointing at the game; the throw, the right whipped through and forward.
        /// </summary>
        static float3 HuntPose(bool right, byte stage, int joint)
        {
            switch (stage)
            {
                case FolkHunting.Stalk:
                    return new float3(joint == 0 ? -0.55f : -0.7f, 0f, 0f);
                case FolkHunting.Aim:
                    return right ? new float3(joint == 0 ? -2.7f : -0.7f, 0f, 0f) : new float3(joint == 0 ? -1.3f : 0f, 0f, 0f);
                case FolkHunting.Throw:
                    return right ? new float3(joint == 0 ? -0.9f : 0f, 0f, 0f) : new float3(joint == 0 ? -0.4f : -0.3f, 0f, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// The ground under a point near height <paramref name="nearY"/>: the surface, or a cave floor for a creature
        /// (the player's body) underground.
        /// </summary>
        static float GroundAt(in TerrainSource terrain, float2 xz, float nearY) =>
            terrain.Caves.IsCreated ? terrain.FloorUnder(new float3(xz.x, nearY, xz.y), out _) : terrain.SampleHeight(xz);

        /// <summary>
        /// Tilts a segment up or down (keeping its heading) so its far end rests on the ground, for chains that lie
        /// along the terrain. The next segment then starts from there, so the chain drapes over bumps.
        /// </summary>
        static quaternion LieOnGround(in TerrainSource terrain, float3 start, quaternion rot, float length, float radius)
        {
            if (length < 1e-4f)
                return rot;
            var dir = math.mul(rot, new float3(0f, 0f, 1f));
            var flat = math.normalizesafe(new float3(dir.x, 0f, dir.z), dir);
            var end = start + flat * length;
            var rise = math.clamp(GroundAt(terrain, end.xz, start.y) + radius - start.y, -0.9f * length, 0.9f * length);
            var wanted = flat * math.sqrt(length * length - rise * rise) + new float3(0f, rise, 0f);
            return math.mul(FromTo(dir, wanted / length), rot);
        }

        /// <summary>
        /// A folded leg's extra pitch per joint: the thigh swings back, the shin folds forward under it, the foot
        /// tucks back (positive pitch swings a leg backwards).
        /// </summary>
        static float Fold(int joint, int count)
        {
            if (count == 1)
                return 0.9f;
            if (count == 2)
                return joint == 0 ? 1.1f : -2.2f;
            return joint == 0 ? 1f : joint == 1 ? -2.1f : joint == 2 ? 1.4f : 0f;
        }

        /// <summary>
        /// Spring, look and posture offsets added to a part's joint angles. An upright body's neck and head point up
        /// rather than forward, so turning them to look is a roll about their own length, not a yaw (which would
        /// tilt the head sideways).
        /// </summary>
        static float3 Secondary(ref CreaturePart part, in CreatureState s, int joint, float run, bool upright)
        {
            // Chewing or lapping while the head is down.
            var chew = math.sin(s.Clock * 7f) * 0.06f * s.HeadDown * (1f - s.Curl);
            switch (part.Role)
            {
                case LimbRole.Tail:
                    // Each joint swings a little more than the last, so the tail curls out of the turn; asleep, it
                    // wraps round the body.
                    return new float3(s.Curl * 0.1f, (s.Sway * part.Spring + s.Curl * 0.3f) * (1f + joint * 0.5f), 0f);
                case LimbRole.Ear:
                    return new float3(-0.5f * run * part.Spring - s.Curl * 0.6f, s.Sway * part.Spring * 0.5f * part.Side, 0f);
                case LimbRole.Head:
                    return upright ? new float3(s.HeadDown * 0.5f + chew, 0f, -s.Look * 0.6f)
                                   : new float3(s.HeadDown * 0.5f + chew, s.Look * 0.6f, 0f);
                case LimbRole.Neck:
                    var n = math.max(part.SegmentCount, 1);
                    return upright ? new float3(s.HeadDown * 1.5f / n, 0f, -s.Look * 0.4f / n)
                                   : new float3(s.HeadDown * 1.5f / n, s.Look * 0.4f / n, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// A walking foot's roll through its step (radians, + toes down): toes up at the heel strike, flat through the
        /// stance, up on the toes at push-off, then back round to toes-up for the next strike.
        /// </summary>
        static float HeelToe(float p)
        {
            if (p < 0.12f)
                return math.lerp(-0.3f, 0f, p / 0.12f);
            if (p < 0.3f)
                return 0f;
            if (p < 0.55f)
                return math.lerp(0f, 0.55f, math.smoothstep(0.3f, 0.55f, p));
            return math.lerp(0.55f, -0.3f, math.smoothstep(0.55f, 1f, p));
        }

        /// <summary>
        /// Two-bone IK on a leg's first two segments: moves the end of the second segment by delta, keeping the knee
        /// bending the way it did. The rest of the leg (foot, toes) keeps its orientation and shifts with it.
        /// Returns how far the end of the leg moved.
        /// </summary>
        static float3 PlaceFoot(ref CreatureBlob b, ref CreaturePart part,
                                ref FixedList4096Bytes<float3> segPos, ref FixedList4096Bytes<quaternion> segRot,
                                float3 delta, float scale)
        {
            int s0 = part.FirstSegment, s1 = s0 + 1;
            var l0 = b.Segments[s0].Length * scale;
            var l1 = b.Segments[s1].Length * scale;
            var hip = segPos[s0];
            var knee = segPos[s1];
            var ankle = knee + math.mul(segRot[s1], new float3(0f, 0f, l1));
            var target = ankle + delta;

            var toTarget = target - hip;
            var d = math.clamp(math.length(toTarget), math.abs(l0 - l1) + 1e-3f, (l0 + l1) * 0.999f);
            var dir = math.normalizesafe(toTarget, math.mul(segRot[s0], new float3(0f, 0f, 1f)));
            target = hip + dir * d;

            // Keep the knee in the plane it already bends in, on the same side.
            var normal = math.cross(knee - hip, ankle - knee);
            normal = math.lengthsq(normal) > 1e-10f ? math.normalize(normal) : math.mul(segRot[s0], new float3(1f, 0f, 0f));
            var cosA = math.clamp((l0 * l0 + d * d - l1 * l1) / (2f * l0 * d), -1f, 1f);
            var sinA = math.sqrt(1f - cosA * cosA);
            var perp = math.normalizesafe(math.cross(normal, dir));
            var kneeA = hip + l0 * (dir * cosA + perp * sinA);
            var kneeB = hip + l0 * (dir * cosA - perp * sinA);
            var newKnee = math.distancesq(kneeA, knee) < math.distancesq(kneeB, knee) ? kneeA : kneeB;

            segRot[s0] = math.mul(FromTo(math.normalizesafe(knee - hip), math.normalizesafe(newKnee - hip)), segRot[s0]);
            segRot[s1] = math.mul(FromTo(math.normalizesafe(ankle - knee), math.normalizesafe(target - newKnee)), segRot[s1]);
            segPos[s1] = newKnee;

            var shift = target - ankle;
            for (int j = 2; j < part.SegmentCount; j++)
                segPos[s0 + j] += shift;
            return shift;
        }

        static quaternion FromTo(float3 a, float3 b)
        {
            var w = 1f + math.dot(a, b);
            if (w < 1e-5f)
            {
                var axis = math.abs(a.x) > 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                return quaternion.AxisAngle(math.normalize(math.cross(a, axis)), math.PI);
            }
            var c = math.cross(a, b);
            return math.normalize(new quaternion(c.x, c.y, c.z, w));
        }

        static SegmentInstance Instance(float3 position, quaternion rotation, ref CreatureSegment seg, float scale, float4 color) => new SegmentInstance
        {
            Position = position,
            Rotation = rotation,
            Length = seg.Length * scale,
            RadiusStart = seg.RadiusStart * scale,
            RadiusEnd = seg.RadiusEnd * scale,
            Flatten = seg.Flatten,
            Color = color
        };
    }
}
