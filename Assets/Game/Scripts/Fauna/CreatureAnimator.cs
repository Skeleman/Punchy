using Punchy.Terrain;
using Unity.Burst;
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

        /// <summary>
        /// The wind as the world draws it (WeatherSystem's _PunchyWind and _PunchyWindDirection) and the shared clock
        /// (_PunchyClock): x strength 0..1, yz the way it blows, w the clock. Set once a frame by CreatureRenderSystem, for
        /// hair blowing in it the way the trees and banners do.
        /// </summary>
        public static readonly SharedStatic<float4> Wind = SharedStatic<float4>.GetOrCreate<WindKey>();
        class WindKey { }

        /// <summary>"-leapphase p" (DebugControls): holds every leaping run at that point of its stride, for shots of
        /// each phase (#6). 0 = off, otherwise p + 1.</summary>
        public static readonly SharedStatic<float> LeapFreeze = SharedStatic<float>.GetOrCreate<LeapFreezeKey>();
        class LeapFreezeKey { }

        /// <summary>"-oldlie" (debug): 1 leaves a lying four-legged body's folded legs where they fall (before A-42),
        /// for before shots from the same build.</summary>
        public static readonly SharedStatic<int> OldLie = SharedStatic<int>.GetOrCreate<OldLieKey>();
        class OldLieKey { }

        /// <summary>Seams (A-92, #230; "-noseams" sets 1 to turn them off): at 0 the shader bends each limb segment's start towards the segment
        /// it hangs from (Material.z: how many segments back that is), hiding the crease at the joint.</summary>
        public static readonly SharedStatic<int> NoSeams = SharedStatic<int>.GetOrCreate<SeamsKey>();
        class SeamsKey { }

        /// <summary>
        /// How gathered a leaping run (CreatureBlob.LeapingRun: dogs, cats, wolves) is at a point of its stride: 1 with all
        /// four feet under the body, 0 stretched out in the leap. Held gathered, eased out into the leap, held in the
        /// air, then eased back in (#6, the owner's spec). A-43: the owner found the move between the two key poses
        /// almost immediate (it took 0.08 of a stride, under a frame at 30 fps); each ease now takes about a third of
        /// the stride (smootherstep, flat at both ends), and the holds are shorter to make room.
        /// </summary>
        public static float LeapGather(float p)
        {
            if (LeapFreeze.Data > 0f)
                p = LeapFreeze.Data - 1f;
            p = math.frac(p);
            if (p < 0.16f) return 1f;
            if (p < 0.46f) return 1f - Smootherstep(0.16f, 0.46f, p);
            if (p < 0.6f) return 0f;
            return Smootherstep(0.6f, 1f, p);
        }

        /// <summary>A smoothstep whose rate also eases in and out (no kink where a hold meets an ease).</summary>
        static float Smootherstep(float a, float b, float x)
        {
            var t = math.saturate((x - a) / (b - a));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>How much less a leaping run's hip swings at either extreme than the pushed run key gives (A-25, #6:
        /// the owner found the legs going near horizontal; about 30° back from that).</summary>
        public const float LeapHipEase = 0.52f;

        /// <summary>A leaping run's hip at one extreme (gathered or in the leap): the run key's swing pushed out by
        /// gain, then its fore-and-aft swing (x) drawn LeapHipEase back towards the rest pose.</summary>
        public static float3 LeapHip(float3 idle, float3 key, float gain)
        {
            var pose = idle + (key - idle) * gain;
            var swing = pose.x - idle.x;
            pose.x = idle.x + math.sign(swing) * math.max(math.abs(swing) - LeapHipEase, 0f);
            return pose;
        }

        /// <param name="detail">Near the camera: the legs fitted to the ground under each foot (the hips lowered on a
        /// slope, each foot reaching for the ground, tipped with it, rolling heel to toe). Further off, where none of
        /// that shows, they keep to the body's plane, saving the ground samples.</param>
        /// <param name="seams">The seams between segments (MarkJoints): only where they can show, at the full capsule (A-97).</param>
        public static void Evaluate(ref CreatureBlob b, in CreatureState s, int coat, in TerrainSource terrain,
                                    NativeArray<SegmentInstance> output, int offset, bool detail = true, bool seams = true)
        {
            var scale = s.Scale;
            // (Posed for the eased speed, so a sudden start or stop doesn't swap the pose under a planted foot.)
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.GaitSpeed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.GaitSpeed);

            // Body on the ground: height from the legs, pitch from the ground ahead and behind.
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var halfLength = b.BodyLength * 0.5f * scale;
            // Animals stand on the terrain; the player's body can be above it (jumping, swimming).
            // Swimmers and floaters are carried at their own height (the body's centre at Position).
            // (A tunneller, a sand worm, is drawn where it is, under the ground or arcing out of it, like a swimmer.)
            var aquatic = b.Habitat == Habitat.Swims || b.Habitat == Habitat.Floats || b.Tunnels;
            // (A lava swimmer out on land, a player's bone lizard, stands on its legs.)
            if (aquatic && b.LavaDweller)
            {
                var lava = terrain.SampleGround(s.Position.xz);
                aquatic = lava.HasWater && lava.WaterSurface > lava.Height + 0.2f;
            }
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
            var sprint = b.Sprint(s, s.GaitSpeed);
            // A leaping run (#6): how far into it, and how gathered it is now (1 gathered, 0 in the leap).
            var leap = b.LeapingRun && !b.Upright ? sprint : 0f;
            var gather = leap > 0f ? LeapGather(s.Phase) : 0f;
            if (b.Upright)
                pitch += sprint * 0.3f;
            // (Gathered, the back rounds and the chest dips; launching into the leap, the nose comes up a little.)
            else if (b.LeapingRun)
                pitch += leap * (0.08f * gather - 0.06f * (1f - gather));
            // A gallop rocks the body fore and aft once a stride, the hinds gathering under it and the fores reaching out
            // (Z-50); a bound, which flexes the back more, further.
            else if (b.Gallops)
                pitch += sprint * (b.Bounds ? 0.12f : 0.07f) * math.sin(2f * math.PI * s.Phase);
            var bobs = math.lerp(b.BobsPerCycle.x, b.BobsPerCycle.y, run);
            var bob = math.lerp(b.Bob.x, b.Bob.y, run) * bounce * move * scale * (0.5f - 0.5f * math.cos(2f * math.PI * bobs * s.Phase));
            pitch += b.RunRock * run * move * math.sin(2f * math.PI * s.Phase);

            // Posture: lying lowers the body onto its belly; a bow or a lowered head tips the chest down.
            var lying = math.min(b.StandHeight, b.FrontRadius * b.BodyFlatten * 0.9f);
            var height = math.lerp(b.StandHeight, lying, s.Lie) * scale;
            // (Lying across a slope the uphill hip would sit inside the flank, under the ground: raise the body by the
            // side slope times half its width. Nothing changes on flat ground.)
            if (!b.Upright && !aquatic && s.Lie > 0f)
            {
                var right = new float2(forward.z, -forward.x);
                var halfWidth = math.max(b.FrontRadius, b.RearRadius) * scale;
                var side = math.abs(GroundAt(terrain, s.Position.xz + right * halfWidth, s.Position.y)
                                    - GroundAt(terrain, s.Position.xz - right * halfWidth, s.Position.y)) / (2f * halfWidth);
                height += side * halfWidth * s.Lie * (1f - airborne);
            }
            var lowered = b.StandHeight * scale - height;
            pitch += s.Bow * 0.3f + s.HeadDown * 0.1f * (1f - s.Lie);
            // (A lowered head, drinking or grazing, or a play bow tips the chest down about the hips: the rump stays where
            // it stood, its hind feet on the ground, and the front legs bend to let the chest down.)
            if (!b.Upright)
                height -= (s.Bow * 0.3f + s.HeadDown * 0.1f) * (1f - s.Lie) * halfLength;
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
            // A-71 weather poses: panting heaves the chest fast and bends a little forward; a shiver is a fine quick
            // tremble; a hunch bends folk over and draws an animal down low.
            else if (s.Action == IdleAction.Pant)
            {
                height += act * 0.025f * b.StandHeight * scale * math.sin(s.Clock * 15f);
                pitch += act * (b.Upright ? 0.12f : 0.05f);
            }
            else if (s.Action == IdleAction.Shiver)
            {
                roll = act * 0.035f * math.sin(s.Clock * 57f);
                pitch += act * (b.Upright ? 0.1f : 0f);
            }
            else if (s.Action == IdleAction.Hunch)
            {
                if (b.Upright)
                    pitch += act * 0.3f;
                else
                    height -= act * 0.15f * b.StandHeight * scale;
            }

            // A player's roll: a forward somersault (a full turn of the body), tucked up off the ground as it goes over.
            if (s.Roll > 0f)
            {
                pitch += 2f * math.PI * s.Roll;
                height += math.sin(math.PI * s.Roll) * 0.35f * b.StandHeight * scale;
            }

            var rootRot = math.mul(quaternion.RotateY(s.Yaw), math.mul(quaternion.RotateX(pitch), quaternion.RotateZ(roll)));
            // Pitched up against a trunk, the body keeps its legs' length out from it (behind it) rather than above.
            var up = math.saturate(s.Vertical);
            // (Sprinting folk leave the ground between steps; a gallop has one moment in the air a stride, as the front
            // legs leave the ground and before the hind ones come down.)
            // (A leaping run crouches a little gathered and flies clear in the leap, every foot off the ground.)
            var lift = height - bob + (b.LeapingRun
                ? leap * b.StandHeight * scale * (0.3f * (1f - gather) - 0.06f * gather)
                : b.Gallops
                ? sprint * 0.12f * b.StandHeight * scale * math.pow(math.max(0f, math.cos(2f * math.PI * (s.Phase - 0.02f))), 2f)
                : sprint * 0.08f * scale * (0.5f + 0.5f * math.cos(4f * math.PI * s.Phase)));
            // Hanging head down in a web, the body sits right at its anchor (the web's centre).
            var hang = s.Mode == CreatureMode.Web || s.Mode == CreatureMode.Roost ? math.saturate(-s.Vertical) : 0f;
            var rootPos = new float3(s.Position.x, ground, s.Position.z) + new float3(0f, lift * (1f - up - hang), 0f) - forward * lift * up;
            // A snake on the ground or swimming: its body starts on its serpentine track (below), where the chain joins the
            // head, not on the straight line it travels. Laid out from the line instead, the whole body swung from side
            // to side about it as each wave passed through, a wag that quickened with speed. (The head itself keeps to the
            // line: moved onto the track, it slid from side to side, a vibration.)
            var weaveShift = float3.zero;
            if (s.Fly < 0.5f && b.Slithers)
            {
                var wave = math.max(b.WalkStride * scale, 0.05f);
                var along = 2f * math.PI / wave * (s.Phase * wave - b.BodyLength * scale * 1.2f);
                weaveShift = new float3(forward.z, 0f, -forward.x) * (0.12f * wave * math.sin(along));
            }
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
                    if (b.Disguised && s.Curl > 0.5f)
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
            if (Golden(ref b, in s))
            {
                var trunk = output[offset];
                trunk.Color = new float4(0.95f, 0.6f, 0.12f, trunk.Color.w);
                trunk.Material.x = (float)SegmentMaterial.Metal;
                output[offset] = trunk;
            }

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
                var phase = math.frac(s.Phase + b.LegPhase(ref part, sprint));
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
                    // (Lying down, the legs fold ahead of the body coming down, so they are tucked before it reaches the
                    // ground rather than half-straight under a half-lowered body: A-92, #231.)
                    fold = math.max(1f - (1f - s.Lie) * (1f - s.Lie), part.Socket.z > 0f ? s.Bow * 0.5f : 0f);
                    // Sitting folds the hind legs; sitting up tall tucks the front paws too; in flight all tuck up.
                    // (A player's body up a trunk is a puppet: its front paws hold on, as a climbing animal's do.)
                    if (hind)
                        fold = math.max(fold, s.Sit);
                    else if (s.Mode != CreatureMode.Climb && s.Mode != CreatureMode.Web && s.Mode != CreatureMode.Puppet)
                        fold = math.max(fold, math.saturate(s.Vertical * 1.6f));
                    // In flight the legs trail back under the tail instead (below).
                    fold *= 1f - s.Fly;
                    // A bear up at a hive braces its front paws on the trunk (below) rather than tucking them.
                    if (!hind && s.Mode == CreatureMode.Work && s.Stage == RaidAtComb)
                        fold = 0f;
                    // Carried: legs tucked up, not dangling.
                    if (s.Mode == CreatureMode.Held)
                        fold = math.max(fold, 0.85f);
                }
                // A leg busy with an idle action (pawing, scratching) isn't planted.
                var busyLeg = UsedByAction(ref part, in s) || PetsWithPaw(ref part, in s, b.Hands);
                var gaitMove = part.Role == LimbRole.Leg ? move * (1f - s.Fly) : move;
                // A snake's body (a chain that lies on the ground, off the body): serpentine. It lies along one wave
                // that stays put on the ground, sideways offset A·sin(k(D − u)) at distance u back from the head after
                // it has gone D: as it moves on, the wave runs back along the body at its own speed, so every part of
                // the body follows the head's track (rather than a tail wagging behind a straight head). Stopped, it
                // keeps its S. The wavelength is its stride, so the gait phase is how far along the wave it is.
                var serpent = part.FollowGround && part.Parent < 0;
                if (serpent)
                    pos += weaveShift;
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
                        // A worm bursting out of the ground goes over in one clean arc, not a wave: its body bent evenly
                        // along it, tail drooping behind the head, most at the top of the arc and straight where it
                        // comes out and goes back in.
                        if (b.Tunnels && s.Air > 0f)
                            a = new float3(1.1f * math.sin(math.PI * s.Air) / part.SegmentCount, 0f, 0f);
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
                    else if (b.Disguised && part.Role == LimbRole.Leg)
                    {
                        // A plant that walks on its arms: arms up at rest (the Idle keys), folded down onto their tips
                        // as it is startled (Curl falls), and walking the gait keys on the tips.
                        var k = seg * CreatureBlob.PoseCount;
                        var stand = 0.5f * (b.Poses[k + CreatureBlob.WalkReach] + b.Poses[k + CreatureBlob.WalkPush]);
                        var spider = math.lerp(stand, b.Angles(seg, jointPhase, 1f, run), math.saturate(gaitMove * 3f));
                        a = math.lerp(spider, b.Poses[k + CreatureBlob.Idle], s.Curl);
                    }
                    else if (leap > 0f && part.Role == LimbRole.Leg && part.Parent < 0)
                    {
                        // A leaping run (#6), from the body's phase rather than each leg's: gathered, the front legs swept
                        // back and out wide and the hinds forward and centred, all four under the body; in the leap the
                        // fronts reach forward and the hinds stretch out behind. The run's reach and push keys, the hip's
                        // swing pushed further than a run's.
                        var k = seg * CreatureBlob.PoseCount;
                        var idle = b.Poses[k + CreatureBlob.Idle];
                        var reachKey = b.Poses[k + CreatureBlob.RunReach];
                        var pushKey = b.Poses[k + CreatureBlob.RunPush];
                        var foot = part.SegmentCount >= 3 && j == part.SegmentCount - 1;
                        var gain = foot ? 1f : j == 0 ? 1.8f : 1.3f;
                        var gathered = idle + ((hind ? reachKey : pushKey) - idle) * gain;
                        var extended = idle + ((hind ? pushKey : reachKey) - idle) * (foot ? 1f : gain * 1.15f);
                        // (The hip eased back from the extremes, A-25: the legs went near horizontal.)
                        if (j == 0)
                        {
                            gathered = LeapHip(idle, hind ? reachKey : pushKey, gain);
                            extended = LeapHip(idle, hind ? pushKey : reachKey, gain * 1.15f);
                        }
                        var pose = math.lerp(extended, gathered, gather);
                        if (!hind && j == 0)
                            pose.z += part.Side * 0.3f * gather;
                        a = math.lerp(b.LegAngles(seg, jointPhase, gaitMove, run, sprint), pose, leap);
                    }
                    else if (part.Role == LimbRole.Leg && sprint > 0f)
                    {
                        // A sprinting leg: the longer stride, and in the swing the knee driven high and the heel kicked up.
                        // (The longer stride is the hip's and knee's: the foot keeps its run angles, eased a little towards
                        // rest, or at full push-off it bends hard back against the shin.)
                        var foot = part.SegmentCount >= 3 && j == part.SegmentCount - 1;
                        if (foot)
                        {
                            var idle = b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle];
                            a = idle + (b.Angles(seg, jointPhase, gaitMove, run) - idle) * (1f - 0.35f * sprint);
                        }
                        else
                            a = b.LegAngles(seg, jointPhase, gaitMove, run, sprint);
                        if (jointPhase >= 0.5f && b.Upright)
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
                    // Running, an upright body holds its elbow near a right angle (the run keys' 85-95°), whatever its
                    // arm swing or sprint pump: scaled up, the bend folded the forearm onto the upper arm (A-41).
                    if (b.Upright && part.Role == LimbRole.Arm && j == 1 && run > 0f)
                        a.x = math.lerp(a.x, b.Angles(seg, jointPhase, gaitMove, run).x, run);
                    // Carrying something in both arms: they hold it, rather than swinging with the stride.
                    if (part.Role == LimbRole.Arm && s.Action == IdleAction.Carry)
                        a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], act);
                    if (torso && j == 0)
                        a.x += lean;
                    if (part.Role == LimbRole.Head && j == 0)
                        a += headTilt + new float3(0.2f * leap * gather, 0f, 0f);
                    // A guard's right arm: bent to hold its weapon upright at its side once it's drawn, or raised forward
                    // with a torch; running at a beast, the weapon levelled at it, held low and forward at the hip. (With
                    // the weapon worn, or an elf's bow on its back, the arms swing free.)
                    // (Anyone carrying a torch holds it up so, guard or not.)
                    if (part.Role == LimbRole.Arm && part.Side > 0f && (s.Task == CreatureTask.Patrol && s.Held == HeldItem.SpearDrawn && b.GuardKit != GuardKit.Elf || s.Held == HeldItem.Torch))
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
                    // A player's weapon (P-28): the right arm bent, ready, the weapon up before it; a swing raises it
                    // over the shoulder and brings it down fast. A spear in both hands, levelled; a bow held out in the
                    // left, the right drawing the string back to the cheek.
                    if (part.Role == LimbRole.Arm && s.Outfit.Weapon != CreatureWeapon.None)
                    {
                        var wSwing = s.Outfit.Swing == 0 ? 0f : (s.Outfit.Swing - 1) / 254f;
                        var wTwo = s.Outfit.Weapon == CreatureWeapon.Spear || s.Outfit.Weapon == CreatureWeapon.Bow;
                        if (part.Side > 0f || wTwo)
                            a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.85f);
                        if (s.Outfit.Weapon == CreatureWeapon.Bow)
                        {
                            var wDraw = wSwing < 0.75f ? math.smoothstep(0f, 0.6f, wSwing) : 0f;
                            if (part.Side < 0f)
                                a.x += j == 0 ? -1.45f : 0f;
                            else
                                a += j == 0 ? new float3(-1.3f, 0f, -0.35f * wDraw) : j == 1 ? new float3(-0.5f - 1.6f * wDraw, 0f, 0f) : float3.zero;
                        }
                        else if (s.Outfit.Weapon == CreatureWeapon.Spear)
                        {
                            var wThrust = math.sin(wSwing * math.PI);
                            a.x += j == 0 ? (part.Side > 0f ? -0.55f - 0.5f * wThrust : -0.9f - 0.3f * wThrust) : j == 1 ? (part.Side > 0f ? -1.1f + 0.7f * wThrust : -0.6f + 0.4f * wThrust) : 0f;
                        }
                        else if (part.Side > 0f)
                        {
                            var wLift = wSwing < 0.55f ? math.smoothstep(0f, 0.55f, wSwing) : 1f - math.smoothstep(0.55f, 0.72f, wSwing);
                            var wStrike = math.smoothstep(0.6f, 0.72f, wSwing) * (1f - math.smoothstep(0.8f, 1f, wSwing));
                            a.x += j == 0 ? -0.5f - 2.0f * wLift - 0.4f * wStrike : j == 1 ? -0.9f - 0.5f * wLift + 0.6f * wStrike : 0f;
                        }
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
                    // Two folk talking (A-26): the one talking with both forearms up in front, hands going as it speaks (the
                    // right more than the left); the one listening with its arms easy at its sides, nodding now and then.
                    if (b.Upright && s.Mode == CreatureMode.Idle && (s.Stage == ChatTalk || s.Stage == ChatListen))
                    {
                        if (part.Role == LimbRole.Arm && s.Stage == ChatTalk)
                        {
                            a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.9f);
                            var big = part.Side > 0f ? 1f : 0.55f;
                            var beat = math.sin(s.Clock * 4.1f + part.Side * 1.3f) * 0.6f + math.sin(s.Clock * 6.7f + part.Side) * 0.4f;
                            a += (j == 0 ? new float3(-0.35f - 0.18f * beat, 0f, part.Side * (0.12f + 0.1f * beat))
                                : j == 1 ? new float3(-1.05f - 0.35f * beat, 0f, 0f) : float3.zero) * big;
                        }
                        else if (part.Role == LimbRole.Arm)
                            a = math.lerp(a, b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle], 0.8f);
                        if (part.Role == LimbRole.Head && j == 0)
                            a.x += s.Stage == ChatListen ? 0.22f * math.max(0f, math.sin(s.Clock * 4.5f)) * math.saturate(math.sin(s.Clock * 0.8f) * 3f)
                                                         : 0.07f * math.sin(s.Clock * 6.3f);
                    }
                    // One of the folk at a hive: the stick hand up over the head jabbing at the comb, the other half up
                    // to shield the face; running off with it, that free arm flapping at the bees.
                    if (b.Upright && part.Role == LimbRole.Arm && s.Mode == CreatureMode.Work && j <= 1)
                    {
                        if (s.Stage == RaidAtComb)
                        {
                            var jab = math.max(0f, math.sin(s.Clock * 8f));
                            a = part.Side > 0f ? (j == 0 ? new float3(-2.7f + 0.35f * jab, 0f, 0.15f) : new float3(-0.2f + 0.3f * jab, 0f, 0f))
                                               : (j == 0 ? new float3(-1.3f, 0f, -0.4f) : new float3(-1.6f, 0f, 0f));
                        }
                        else if (s.Stage == RaidLeave && s.StageTime < 12f && part.Side < 0f)
                        {
                            var flap = math.sin(s.Clock * 11f);
                            a = j == 0 ? new float3(-2.3f + 0.6f * flap, 0f, -0.5f + 0.3f * flap) : new float3(-0.6f, 0f, 0f);
                        }
                    }
                    if (b.Upright && part.Role == LimbRole.Head && s.Mode == CreatureMode.Work && s.Stage == RaidAtComb)
                        a.x -= 0.55f;
                    // A bear reared up at a hive: front legs reaching up the trunk, scraping at it by turns.
                    if (!b.Upright && part.Role == LimbRole.Leg && part.Parent < 0 && part.Socket.z > 0f &&
                        s.Mode == CreatureMode.Work && s.Stage == RaidAtComb && j <= 1)
                    {
                        var scrape = math.sin(s.Clock * 7f + part.Side * 1.6f);
                        a += j == 0 ? new float3(-1.2f + 0.35f * scrape, 0f, 0f) : new float3(0.3f - 0.25f * scrape, 0f, 0f);
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
                    if (b.Segments[seg].Hair != 0)
                        a += HairSway(in s, j, run);
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
                        a += ActionPose(ref part, in s, j, act, b.Hands);
                    // An upright body's elbow bends one way only (negative pitch, forward): never past straight, nor
                    // folded beyond 150°, however much swing, sprint or pose is added up.
                    if (b.Upright && part.Role == LimbRole.Arm && j == 1)
                        a.x = math.clamp(a.x, -2.6f, 0f);
                    // An upright body's ankle only bends so far either way from standing (toes down to push off, up in the swing),
                    // however long the stride, run and pose add up: past that the foot folds against the shin.
                    if (b.Upright && part.Role == LimbRole.Leg && part.SegmentCount >= 3 && j == part.SegmentCount - 1 && s.Sit + s.Lie < 0.3f)
                    {
                        var stand = b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle].x;
                        a.x = stand + math.clamp(a.x - stand, -AnkleBend, AnkleBend);
                    }
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
                    // (A folding leg's foot leaves the ground as it folds: no reaching for the ground, nor sinking, once tucked.)
                    var correction = ((actual - nominal) * (1f - fold) + (folds ? 0f : lowered) + sink * (1f - fold)) * (1f - airborne);
                    if (b.Disguised)
                        correction *= 1f - s.Curl;
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

                // A four-legged body lying down: its folded legs are longer than the lowered body is high, so on their own
                // they run down into the ground. Each is swung about the hip (front legs forward, hind legs forward under
                // the belly, failing that back, then out to the side) by the least angle that brings its lowest point up
                // onto the ground, judged against the local ground plane under the hip so it holds on slopes too.
                // (Splayed legs, a lizard's, don't fold, but lying flat their knees can still dip in: the same lift.)
                var lieFold = folds ? fold : part.Role == LimbRole.Leg && part.Parent < 0 && !b.Upright && !aquatic ? s.Lie : 0f;
                // (From the first moment of lying down, and refined to the least clearing angle, so the swing grows smoothly
                // with the fold rather than letting the legs dip in while it fades in: A-92, #231.)
                if (lieFold > 0.01f && OldLie.Data == 0 && part.FirstSegment + part.SegmentCount <= MaxSegments)
                {
                    var h0 = GroundAt(terrain, hip.xz, s.Position.y);
                    var grad = new float2(GroundAt(terrain, hip.xz + new float2(0.3f, 0f), s.Position.y) - h0,
                                          GroundAt(terrain, hip.xz + new float2(0f, 0.3f), s.Position.y) - h0) / 0.3f;
                    var side = math.mul(rootRot, new float3(1f, 0f, 0f));
                    var ahead = math.mul(rootRot, new float3(0f, 0f, 1f));
                    var outward = part.Socket.x >= 0f ? 1f : -1f;
                    var best = quaternion.identity;
                    var bestLow = LowestLegPoint(ref b, ref part, ref segPos, ref segRot, quaternion.identity, hip, h0, grad, scale);
                    var bestAxis = float3.zero;
                    var bestT = 0f;
                    for (int k = 1; k <= 48 && bestLow < 0f; k++)
                    {
                        // 12 steps of 0.13 rad each way: forward, then back, then rolled out to the side, then in under the body.
                        var t = (1 + (k - 1) % 12) * 0.13f;
                        var axis = k <= 12 ? -side : k <= 24 ? side : k <= 36 ? ahead * outward : -ahead * outward;
                        var q = quaternion.AxisAngle(axis, t);
                        var low = LowestLegPoint(ref b, ref part, ref segPos, ref segRot, q, hip, h0, grad, scale);
                        // The first angle that clears wins (the loop ends); failing all, the one that comes closest.
                        if (low > bestLow)
                        {
                            best = q;
                            bestLow = low;
                            bestAxis = axis;
                            bestT = t;
                        }
                    }
                    // (Halve the last step a few times to the least angle that still clears, so the legs don't jump by 0.13 rad.)
                    if (bestLow >= 0f && bestT > 0f)
                    {
                        var lo = bestT - 0.13f;
                        var hi = bestT;
                        for (int halve = 0; halve < 5; halve++)
                        {
                            var mid = 0.5f * (lo + hi);
                            if (LowestLegPoint(ref b, ref part, ref segPos, ref segRot, quaternion.AxisAngle(bestAxis, mid), hip, h0, grad, scale) >= 0f)
                                hi = mid;
                            else
                                lo = mid;
                        }
                        best = quaternion.AxisAngle(bestAxis, hi);
                    }
                    var weight = math.smoothstep(0.01f, 0.15f, lieFold);
                    var turn = math.slerp(quaternion.identity, best, weight);
                    for (int j = 0; j < part.SegmentCount; j++)
                    {
                        var seg = part.FirstSegment + j;
                        segPos[seg] = hip + math.mul(turn, segPos[seg] - hip);
                        segRot[seg] = math.mul(turn, segRot[seg]);
                    }
                    pos = hip + math.mul(turn, pos - hip);
                    rot = math.mul(turn, rot);
                    // (No angle frees the ball of the hip itself, which the belly rests on, nor quite all of a long leg on a
                    // slope, judged on a plane: lift what is left against the real ground up into the flank, at most the hip's
                    // own radius, so no leg stays in the ground.)
                    var left = LowestLegOnGround(ref b, ref part, ref segPos, ref segRot, in terrain, s.Position.y, scale);
                    if (left < 0f)
                    {
                        var rise = new float3(0f, math.min(-left, b.Segments[part.FirstSegment].RadiusStart * scale) * weight, 0f);
                        for (int j = 0; j < part.SegmentCount; j++)
                            segPos[part.FirstSegment + j] += rise;
                        pos += rise;
                    }
                }

                // Climbing onto a ledge: the hands stay on its rim (Anchor, set as the climb began) while the body pulls
                // up past them, the arms bending to keep them there, until it's over the top and pressing down instead.
                if (part.Role == LimbRole.Arm && part.SegmentCount >= 2 && s.Action == IdleAction.Mantle && b.Upright &&
                    math.lengthsq(s.Anchor) > 0f && part.FirstSegment + part.SegmentCount <= MaxSegments)
                {
                    var hold = 1f - math.smoothstep(0.45f, 0.7f, (s.ActionTime - 0.12f) / 0.73f);
                    if (hold > 0.01f)
                    {
                        var s1 = part.FirstSegment + 1;
                        var hand = segPos[s1] + math.mul(segRot[s1], new float3(0f, 0f, b.Segments[s1].Length * scale));
                        var right = new float3(forward.z, 0f, -forward.x);
                        var grip = s.Anchor + right * part.Side * 0.18f * scale;
                        pos += PlaceFoot(ref b, ref part, ref segPos, ref segRot, (grip - hand) * hold, scale);
                    }
                }

                endPos.Add(pos);
                endRot.Add(rot);
            }

            var golden = Golden(ref b, in s);
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
                var piece = Instance(segPos[seg], segRot[seg], ref segment, scale, colour);
                // A rare golden one: its body gold and gleaming as metal (eyes and other fixed colours as they are).
                if (golden && segment.Fixed == 0)
                {
                    piece.Color = new float4(math.lerp(new float3(0.95f, 0.6f, 0.12f), new float3(0.7f, 0.4f, 0.06f), segment.Accent), colour.w);
                    piece.Material.x = (float)SegmentMaterial.Metal;
                }
                output[offset + seg] = piece;
            }
            if (seams && NoSeams.Data == 0)
                MarkJoints(ref b, output, offset);
        }

        /// <summary>
        /// Seams: for each limb segment, how many segments back the one it hangs from is (the one before it in its
        /// limb, else the last of the limb it grows from, else the trunk for a tail or neck), in Material.z for the
        /// shader, which blends the start of the segment into that one's frame. Legs and arms off the trunk are left
        /// alone (their start would swing flat along the body).
        /// </summary>
        static void MarkJoints(ref CreatureBlob b, NativeArray<SegmentInstance> output, int offset)
        {
            for (int p = 0; p < b.Parts.Length; p++)
            {
                ref var part = ref b.Parts[p];
                if (part.Role != LimbRole.Leg && part.Role != LimbRole.Arm && part.Role != LimbRole.Tail && part.Role != LimbRole.Neck && part.Role != LimbRole.Head)
                    continue;
                for (int j = 0; j < part.SegmentCount; j++)
                {
                    var seg = part.FirstSegment + j;
                    if (seg <= 0 || seg >= MaxSegments || seg >= b.Segments.Length)
                        break;
                    var from = -1;
                    if (j > 0)
                        from = seg - 1;
                    else if (part.Parent >= 0)
                    {
                        ref var parent = ref b.Parts[part.Parent];
                        from = parent.SegmentCount > 0 ? parent.FirstSegment + parent.SegmentCount - 1 : -1;
                    }
                    else if (part.Role == LimbRole.Tail || part.Role == LimbRole.Neck)
                        from = 0;
                    if (from < 0 || from >= MaxSegments || from >= b.Segments.Length)
                        continue;
                    var piece = output[offset + seg];
                    if (piece.Length <= 0f || output[offset + from].Length <= 0f)
                        continue;   // hidden (zero size) on either side
                    // (Not across a fold sharper than a right angle: blended that far, the start would pinch.)
                    if (math.abs(math.dot(piece.Rotation.value, output[offset + from].Rotation.value)) < 0.7071f)
                        continue;
                    piece.Material.z = seg - from;
                    output[offset + seg] = piece;
                }
            }
        }

        /// <summary>
        /// One animal in GoldenOdds is golden, by its individual (fixed at birth and sent with it, so every machine sees
        /// the same one): any but folk, ghosts and players' bodies.
        /// </summary>
        public static bool Golden(ref CreatureBlob b, in CreatureState s) =>
            !b.Hands && !b.Haunts && s.Mode != CreatureMode.Puppet && (math.hash(new uint2(s.Individual, 0x601Du)) >> 8) % GoldenOdds == 0u;   // (the hash's low bits follow the individual's, always odd)

        public const uint GoldenOdds = 400;

        /// <summary>The most an upright body's ankle bends from standing, either way (radians: 38°).</summary>
        const float AnkleBend = 0.66f;

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
        /// <summary>A body without hands petting: its right front paw does the stroking.</summary>
        static bool PetsWithPaw(ref CreaturePart part, in CreatureState s, bool hands) =>
            s.Action == IdleAction.Pet && !hands && part.Side > 0f && FrontLeg(ref part);

        static float3 ActionPose(ref CreaturePart part, in CreatureState s, int joint, float e, bool hands)
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
                    // A player petting an animal: leaning over it, the right hand reaching down and stroking. A body
                    // with no hands lifts its right front paw forward and strokes with that, its head left to look.
                    var stroke = math.sin(s.Clock * 3.2f);
                    if (!hands)
                    {
                        if (PetsWithPaw(ref part, in s, hands))
                            return joint == 0 ? new float3((-0.95f + 0.2f * stroke) * e, 0f, 0f)
                                 : joint == 1 ? new float3((-0.45f - 0.25f * stroke) * e, 0f, 0f) : float3.zero;
                        break;
                    }
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
                case IdleAction.Snap:
                {
                    // A rooted snapper striking: the stem (its neck) whips out over what came near and the head bites
                    // down, quick; then it draws slowly back up. Its jaws (ears) clap shut at the bite.
                    var strike = math.smoothstep(0f, 0.12f, t) * (1f - math.smoothstep(0.35f, 1f, t));
                    if (part.Role == LimbRole.Neck) return new float3(1.1f * strike / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3(0.5f * strike, 0f, 0f);
                    if (part.Role == LimbRole.Ear) return new float3(0.7f * strike, 0f, 0f);
                    break;
                }
                case IdleAction.Throw:
                    // A player lunging at game: right arm thrown forward, leaning into it (a body with no arms lunges
                    // head first).
                    if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0) return new float3(0.3f * e, 0f, 0f);
                    if (part.Role == LimbRole.Arm && part.Side > 0f)
                        return joint == 0 ? new float3(-1.5f * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.15f * e, 0f, 0f);
                    break;
                case IdleAction.Carry:
                {
                    // A player carrying an animal: both arms forward with the elbows bent, cradling it at the chest, opened
                    // out round it as wide as it is (CarryWidth) with the elbows less bent: a cat between the forearms,
                    // a sheep with the arms well apart.
                    var open = math.saturate((s.CarryWidth - 0.1f) * 1.6f) * 0.7f;
                    if (part.Role == LimbRole.Arm)
                        return joint == 0 ? new float3(-(0.35f + 0.2f * open) * e, 0f, part.Side * open * e)
                             : joint == 1 ? new float3(-(1.25f - 0.6f * open) * e, 0f, 0f) : float3.zero;
                    if (part.Role == LimbRole.Head) return new float3(0.2f * e, 0f, 0f);
                    break;
                }
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
                case IdleAction.Pant:
                    // Head low and bobbing with each quick breath; hands on the knees for folk.
                    if (part.Role == LimbRole.Neck) return new float3((0.3f + 0.06f * math.sin(s.Clock * 15f)) * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.25f + 0.08f * math.sin(s.Clock * 15f)) * e, 0f, 0f);
                    if (part.Role == LimbRole.Tail) return new float3(0.3f * e, 0f, 0f);
                    if (part.Role == LimbRole.Arm && joint == 0) return new float3(-0.45f * e, 0f, 0f);
                    break;
                case IdleAction.Shiver:
                {
                    // Arms hugged across the chest, the whole of it trembling; head drawn down, ears and tail tucked.
                    var tremble = 0.05f * math.sin(s.Clock * 57f + joint);
                    if (part.Role == LimbRole.Arm && joint == 0) return new float3((-0.85f + tremble) * e, 0f, -0.25f * e);
                    if (part.Role == LimbRole.Arm && joint == 1) return new float3(-1.9f * e, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3((0.3f + tremble) * e, 0f, 0f);
                    if (part.Role == LimbRole.Neck) return new float3(0.25f * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Ear || part.Role == LimbRole.Tail) return new float3(0.5f * e, tremble * e, 0f);
                    break;
                }
                case IdleAction.Hunch:
                    // Head down and in, arms drawn up close, ears flat and tail clamped down.
                    if (part.Role == LimbRole.Head) return new float3(0.5f * e, 0f, 0f);
                    if (part.Role == LimbRole.Neck) return new float3(0.45f * e / n, 0f, 0f);
                    if (part.Role == LimbRole.Arm && joint == 0) return new float3(-0.5f * e, 0f, -0.15f * e);
                    if (part.Role == LimbRole.Arm && joint == 1) return new float3(-1.4f * e, 0f, 0f);
                    if (part.Role == LimbRole.Ear || part.Role == LimbRole.Tail) return new float3(0.7f * e, 0f, 0f);
                    break;
                case IdleAction.ShieldEyes:
                    // Bat folk from a light (#35): the right arm raised and folded so the forearm lies across the eyes
                    // (the elbow out to the side), the head bowed and turned a little away, the left hand up at the chest.
                    if (part.Role == LimbRole.Arm && joint == 0) return part.Side > 0f ? new float3(-2.1f * e, 0f, 0.55f * e) : new float3(-0.5f * e, 0f, 0f);
                    if (part.Role == LimbRole.Arm && joint == 1) return new float3((part.Side > 0f ? -2.0f : -1.3f) * e, 0f, 0f);
                    if (part.Role == LimbRole.Head) return new float3(0.35f * e, -0.3f * e, 0f);
                    break;
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
                case IdleAction.Mantle:
                {
                    // Hands up on the rim in front, pulled up to it; then, the body past the rim, pressing down on the top
                    // with a knee coming up to get a leg over. (Its ActionTime runs with the climb: see PlayerAvatarSystem.)
                    var over = math.smoothstep(0.35f, 0.65f, (t - 0.12f) / 0.73f);
                    if (part.Role == LimbRole.Arm)
                        return (joint == 0 ? new float3(math.lerp(-2.3f, -0.5f, over), 0f, -part.Side * 0.15f) :
                                joint == 1 ? new float3(math.lerp(-0.5f, -0.15f, over), 0f, 0f) : float3.zero) * e;
                    if (part.Role == LimbRole.Leg && part.Side > 0f)
                        return (joint == 0 ? new float3(-1.3f * over, 0f, 0f) : joint == 1 ? new float3(1.4f * over, 0f, 0f) : float3.zero) * e;
                    if (part.Role == LimbRole.Head) return new float3(-0.2f * e, 0f, 0f);
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
        /// <summary>Sitting-mode stage of a child sitting in a play ring on a green (with its stick or stone).</summary>
        public const byte PlayRing = 8;
        /// <summary>Idle-mode stages of one of the townsfolk a messenger bird has come to: waiting for it (the forearm
        /// up for it to land on), taking the message from its leg, reading it.</summary>
        public const byte MessageWait = 60, MessageTake = 61, MessageRead = 62;
        /// <summary>Idle stages: two folk stopped to talk, the one talking (hands going) and the one listening (nodding). A-26.</summary>
        public const byte ChatTalk = 70, ChatListen = 71;
        /// <summary>Work-mode stages of a bear raiding a wild hive: on its way, reared up at the comb, making off.
        /// (The bees mob it while it's at the comb: CreatureRenderSystem's Swarm.)</summary>
        public const byte RaidWalk = 150, RaidAtComb = 151, RaidLeave = 152, RaidLick = 153;

        /// <summary>
        /// Where a worker is in its swing (an axe, a hammer, a pick): lift 0 down to 1 raised, raised slowly and held a
        /// moment at the top (the wind-up, longest for an axe), then brought down fast; strike 1 just as it lands.
        /// </summary>
        static float SwingLift(in CreatureState s, CreatureTask job, out float strike)
        {
            var t = s.Clock + (s.Individual % 100) * 0.13f;
            var p = math.frac(t * (job == CreatureTask.Chop ? 0.55f : 0.85f));
            var top = job == CreatureTask.Chop ? 0.5f : 0.6f;
            strike = math.smoothstep(0.68f, 0.74f, p) * (1f - math.smoothstep(0.8f, 0.95f, p));
            return p < 0.7f ? math.smoothstep(0f, top, p) : 1f - math.smoothstep(0.7f, 0.76f, p);
        }

        /// <summary>
        /// The rest of a worker's body in its swing: raising, the torso leans back and (with an axe or a pick) twists
        /// away; striking, it folds forward into the blow and the head drops to the work. A hammer's is the smallest
        /// (one hand, light blows), an axe's the biggest. (The legs stay as they stand: bending them here would lift the
        /// feet off the ground.)
        /// </summary>
        static float3 SwingBody(ref CreaturePart part, in CreatureState s, int joint)
        {
            var job = s.Task == CreatureTask.Hermit ? (s.Stage == 2 || s.Stage == 3 ? CreatureTask.Tend : s.Stage == 5 ? CreatureTask.Hammer : CreatureTask.Chop) : s.Task;
            if (s.Task == CreatureTask.Tend)
                return TendBody(ref part, in s, joint);
            if (job != CreatureTask.Chop && job != CreatureTask.Hammer && !(job == CreatureTask.Mine && s.Stage == MinerAtFace))
                return float3.zero;
            var lift = SwingLift(in s, job, out var strike);
            var big = job == CreatureTask.Hammer ? 0.45f : 1f;
            // (An upright torso: pitch bends it forward, a roll twists it about its length.)
            if (part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0)
                return new float3((-0.12f * lift + 0.42f * strike) * big, 0f, job == CreatureTask.Hammer ? 0f : -0.3f * lift * (1f - strike));
            if (part.Role == LimbRole.Head && joint == 0)
                return new float3((0.25f * strike - 0.1f * lift) * big, 0f, 0f);
            return float3.zero;
        }

        /// <summary>A baker's kneading: 0 to 1 as a hand presses into the dough, the two hands half a beat apart.</summary>
        static float Knead(float t, bool right) => 0.5f + 0.5f * math.sin(t * 3.2f + (right ? 0f : math.PI));

        /// <summary>A well keeper hauling the rope: 1 as a hand reaches up for a fresh grip, 0 as it pulls down.</summary>
        static float Haul(float t, bool right) => math.smoothstep(-0.6f, 0.6f, math.sin(t * 1.7f + (right ? 0f : math.PI)));

        /// <summary>
        /// The rest of a tender's body by its job (A-33): a baker leans over its table, bobbing into each push; a healer
        /// is bent right over its bowl, head down; a well keeper leans back against the rope's weight.
        /// </summary>
        static float3 TendBody(ref CreaturePart part, in CreatureState s, int joint)
        {
            var torso = part.Role == LimbRole.Feature && part.Parent < 0 && joint == 0;
            var head = part.Role == LimbRole.Head && joint == 0;
            if (!torso && !head)
                return float3.zero;
            var t = s.Clock + (s.Individual % 100) * 0.13f;
            switch (FolkJobs.Of(in s))
            {
                case FolkJob.Baker:
                    return new float3(torso ? 0.28f + 0.08f * (Knead(t, true) + Knead(t, false) - 1f) : 0.3f, 0f, 0f);
                case FolkJob.Healer:
                    return new float3(torso ? 0.55f : 0.35f, 0f, 0f);
                case FolkJob.WellKeeper:
                    return new float3(torso ? -0.12f - 0.05f * Haul(t, true) : -0.15f, 0f, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>A gatekeeper at work at its post, or a guard standing still on its rounds.</summary>
        static bool OnWatch(in CreatureState s) =>
            s.Task == CreatureTask.Watch && s.Mode == CreatureMode.Work && s.Stage != 0 ||
            s.Task == CreatureTask.Patrol && s.Mode == CreatureMode.Patrol && s.GaitSpeed < 0.3f;

        /// <summary>A slow look about: the head turns one way, holds, sweeps back and holds the other way.</summary>
        static float3 LookAbout(in CreatureState s)
        {
            var t = s.Clock + (s.Individual % 100) * 0.37f;
            return new float3(0f, 0f, -0.75f *math.clamp(1.5f * math.sin(t * 0.32f), -1f, 1f));
        }

        static float3 TaskPose(ref CreaturePart part, in CreatureState s, int joint)
        {
            if (part.Role == LimbRole.Arm && joint <= 1 && s.Mode == CreatureMode.Hunt && s.Task == CreatureTask.Hunt)
                return HuntPose(part.Side > 0f, s.Stage, joint);
            // (A gatekeeper at its post, or a guard stopped on its rounds, slowly looks about: A-33.)
            if (part.Role == LimbRole.Head && joint == 0 && OnWatch(in s))
                return LookAbout(in s);
            if (s.Mode != CreatureMode.Work || s.Stage == 0 || s.Task == CreatureTask.None)
                return float3.zero;
            // (Swinging an axe, a hammer or a pick, the whole body goes into it: SwingBody.)
            if (part.Role != LimbRole.Arm || joint > 1)
                return SwingBody(ref part, in s, joint);
            // (A miner swings its pick only at the ore face, not on its way there and back.)
            if (s.Task == CreatureTask.Mine && s.Stage != MinerAtFace)
                return float3.zero;
            var right = part.Side > 0f;
            var t = s.Clock + (s.Individual % 100) * 0.13f;
            // (A hermit's chores take a woodcutter's or a tender's pose: its stage says which.)
            var job = s.Task == CreatureTask.Hermit ? (s.Stage == 2 || s.Stage == 3 ? CreatureTask.Tend : s.Stage == 5 ? CreatureTask.Hammer : CreatureTask.Chop) : s.Task;
            switch (job)
            {
                case CreatureTask.Hammer:
                case CreatureTask.Chop:
                case CreatureTask.Mine:
                {
                    // Raise slowly, strike fast; a hammer is one-handed (the other hand holds the work), an axe or a
                    // miner's pick two.
                    var swinging = right || job == CreatureTask.Chop || s.Task == CreatureTask.Mine;
                    if (!swinging)
                        return new float3(joint == 0 ? -0.7f : -0.7f, 0f, 0f);
                    var lift = SwingLift(in s, job, out _);
                    return new float3(joint == 0 ? -(0.45f + 1.8f * lift) : -(0.35f + 0.9f * lift), 0f, 0f);
                }
                case CreatureTask.Gesture:
                {
                    // Hands resting on the counter; one at a time rises high, held up and beckoning (the forearm
                    // flapping in at the elbow) for over a second. (A-35: a smaller, shorter raise never read.)
                    var p = math.frac(t * 0.26f + (right ? 0f : 0.5f));
                    var raise = math.smoothstep(0f, 0.06f, p) * (1f - math.smoothstep(0.36f, 0.44f, p));
                    var beckon = 0.5f + 0.5f * math.sin(t * 8f);
                    return new float3(joint == 0 ? -0.45f - 1.9f * raise : -0.9f + raise * (0.1f - 0.9f * beckon), 0f, 0f);
                }
                case CreatureTask.Tend:
                    switch (FolkJobs.Of(in s))
                    {
                        case FolkJob.Baker:
                        {
                            // Kneading: both hands pushing down and forward into the dough, one after the other.
                            var push = Knead(t, right);
                            return new float3(joint == 0 ? -0.75f - 0.3f * push : -0.75f + 0.45f * push, 0f, 0f);
                        }
                        case FolkJob.Healer:
                            // Bent over, both hands low in front holding the bowl, stirring a little.
                            return new float3(joint == 0 ? -0.5f + 0.06f * math.sin(t * 1.3f + (right ? 0f : 0.8f)) : -1f, 0f, 0f);
                        case FolkJob.WellKeeper:
                        {
                            // Hauling the rope hand over hand: each hand reaches up and pulls down in turn.
                            var reach = Haul(t, right);
                            return new float3(joint == 0 ? -0.9f - 1.3f * reach : -0.5f + 0.25f * reach, 0f, 0f);
                        }
                        default:
                            return new float3(joint == 0 ? -0.65f + 0.22f * math.sin(t * 1.5f + (right ? 0f : 1.4f)) : -0.55f, 0f, 0f);
                    }
                case CreatureTask.Watch:
                    // The right hand holds its spear upright at its side (CreatureRenderSystem's Gear); the left is behind the back.
                    if (right && s.Held == HeldItem.None)
                        return new float3(joint == 0 ? -0.25f : -1.25f, 0f, 0f);
                    return new float3(joint == 0 ? 0.35f : -0.75f, 0f, 0f);
                case CreatureTask.Fish:
                {
                    // Both hands on the rod held out over the water; casting, the right swings it up over the shoulder
                    // and whips it through; landing a fish, both lift it high. (The rod is drawn to match: see
                    // CreatureRenderSystem's Rod.)
                    // (Out on a wharf, A-73: walking the deck arms free or round a crate (OreSack's pose); mending nets
                    // by the crates, both hands busy low in front, working the mesh.)
                    if (s.Stage >= CreatureBrainSystem.PierOut)
                        return float3.zero;
                    if (s.Stage == CreatureBrainSystem.PierNets)
                        return new float3(joint == 0 ? -0.65f + 0.22f * math.sin(t * 1.5f + (right ? 0f : 1.4f)) : -0.95f + 0.25f * math.sin(t * 3.1f + (right ? 0.7f : 0f)), 0f, 0f);
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
        /// <summary>The lowest point of a leg's segments (less their radius) above a ground plane through h0 at the hip
        /// with the given slope, after turning the leg by q about the hip. Negative: into the ground.</summary>
        static float LowestLegPoint(ref CreatureBlob b, ref CreaturePart part, ref FixedList4096Bytes<float3> segPos,
            ref FixedList4096Bytes<quaternion> segRot, quaternion q, float3 hip, float h0, float2 grad, float scale)
        {
            var low = float.MaxValue;
            for (int j = 0; j < part.SegmentCount; j++)
            {
                var seg = part.FirstSegment + j;
                ref var bs = ref b.Segments[seg];
                var start = hip + math.mul(q, segPos[seg] - hip);
                var end = start + math.mul(math.mul(q, segRot[seg]), new float3(0f, 0f, bs.Length * scale));
                var g0 = h0 + math.dot(start.xz - hip.xz, grad);
                var g1 = h0 + math.dot(end.xz - hip.xz, grad);
                low = math.min(low, math.min(start.y - bs.RadiusStart * bs.Flatten * scale - g0, end.y - bs.RadiusEnd * bs.Flatten * scale - g1));
            }
            return low;
        }

        /// <summary>The lowest any segment of this leg comes to the real ground under it (m, less its radius).</summary>
        static float LowestLegOnGround(ref CreatureBlob b, ref CreaturePart part, ref FixedList4096Bytes<float3> segPos,
            ref FixedList4096Bytes<quaternion> segRot, in TerrainSource terrain, float nearY, float scale)
        {
            var low = float.MaxValue;
            for (int j = 0; j < part.SegmentCount; j++)
            {
                var seg = part.FirstSegment + j;
                ref var bs = ref b.Segments[seg];
                var start = segPos[seg];
                var end = start + math.mul(segRot[seg], new float3(0f, 0f, bs.Length * scale));
                // (Its ends and middle: on curved ground a long shin can dip in between them.)
                for (float t = 0f; t <= 1.001f; t += 0.5f)
                {
                    var at = math.lerp(start, end, t);
                    var r = math.lerp(bs.RadiusStart, bs.RadiusEnd, t) * bs.Flatten * scale;
                    low = math.min(low, at.y - r - GroundAt(terrain, at.xz, nearY));
                }
            }
            return low;
        }

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
                    return upright ? new float3(s.HeadDown * 0.5f + chew - s.LookPitch * 0.6f, 0f, -s.Look * 0.6f)
                                   : new float3(s.HeadDown * 0.5f + chew - s.LookPitch * 0.6f, s.Look * 0.6f, 0f);
                case LimbRole.Neck:
                    var n = math.max(part.SegmentCount, 1);
                    return upright ? new float3((s.HeadDown * 1.5f - s.LookPitch * 0.4f) / n, 0f, -s.Look * 0.4f / n)
                                   : new float3((s.HeadDown * 1.5f - s.LookPitch * 0.4f) / n, s.Look * 0.4f / n, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Hair (and beards and manes) in the wind and on the move: blown downwind in gusts that roll across the land
        /// (the trees' and grass's own formula, on the shared clock, so every machine sees the same), fluttering a little,
        /// streaming back when running; each joint further down moves more.
        /// </summary>
        static float3 HairSway(in CreatureState s, int joint, float run)
        {
            var w = Wind.Data;
            var strength = w.x;
            var dir = math.normalizesafe(w.yz, new float2(1f, 0f));
            var phase = w.w * (0.7f + strength * 1.1f) - math.dot(s.Position.xz, dir) * 0.04f + (s.Individual % 628u) * 0.01f;
            var gust = (0.6f + 0.4f * math.sin(phase) * math.sin(phase * 0.37f + 1.3f)) * (0.08f + 0.92f * strength);
            // The wind across the body: from in front or behind (pitching the hair back or forward), and from the side.
            var ahead = new float2(math.sin(s.Yaw), math.cos(s.Yaw));
            var along = math.dot(dir, ahead);
            var across = dir.x * ahead.y - dir.y * ahead.x;
            var flutter = math.sin(w.w * 7f + joint * 1.3f + s.Individual * 0.1f) * 0.06f * gust;
            var more = 1f + joint * 0.5f;
            return new float3((along * gust * 0.8f + run * 0.35f + flutter) * more, across * gust * 0.8f * more, 0f);
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
