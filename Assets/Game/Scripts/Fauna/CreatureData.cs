using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A species flattened for Burst: every part (mirrored slots expanded into left and right copies, parents before
    /// children), every segment (index 0 is the body), and every segment's seven key poses.
    /// </summary>
    public struct CreatureBlob
    {
        public const int PoseCount = 7;   // idle, walk reach/push/lift, run reach/push/lift
        public const int Idle = 0, WalkReach = 1, WalkPush = 2, WalkLift = 3, RunReach = 4, RunPush = 5, RunLift = 6;

        public float BodyLength, RearRadius, FrontRadius, BodyFlatten;
        public float WalkSpeed, RunSpeed;
        /// <summary>Distance the body travels per gait cycle, walking and running (from the key poses).</summary>
        public float WalkStride, RunStride;
        /// <summary>Height of the body's centre above the ground when standing (from the idle pose).</summary>
        public float StandHeight;
        /// <summary>A-118: height of the body's centre above the ground lying down, on its folded legs or its belly,
        /// whichever needs more (m, at size 1), and how far its nose tips up then (radians) when its folded front legs
        /// stand taller than its hind ones.</summary>
        public float LieHeight, LiePitch;
        /// <summary>A-118: it stands on two front and two hind legs that fold under it (LieFold), and lies on them.</summary>
        public bool LiesOnFold;
        /// <summary>Where its mouth is, at the end of its head, from the body's centre in the body's frame (m, at size 1,
        /// idle pose); HasMouth false for a body with no head part.</summary>
        public float3 Mouth;
        public bool HasMouth;
        public float2 Bob, BobsPerCycle;
        public float RunRock;
        /// <summary>Keeps the body level rather than tilting with the ground (two-legged walkers).</summary>
        public bool Upright;
        public Habitat Habitat;
        public float SwimDepth, Leaps;
        public float WanderRadius, FleeDistance, TurnRate;
        public bool FleeGas;
        public float2 IdleTime, Size;
        public ActiveTime Active;
        public float Grazing, Thirst, Playfulness, Laziness, Hunger, ChaseTime;
        /// <summary>Kg at the middle of the Size range (see WeightAt).</summary>
        public float Weight;
        /// <summary>Range individuals' aggression is rolled in, and how aggressive one must be to hunt (CreaturePersonality).</summary>
        public float2 Aggression;
        public float HuntAggression;
        /// <summary>Species (FaunaDirector indices) it hunts.</summary>
        public BlobArray<int> Prey;
        /// <summary>Whether anything hunts it (so it needs to watch out for predators).</summary>
        public bool Hunted;
        /// <summary>Folk only (empty for the rest): by FaunaDirector species index, its regard for a player in that body
        /// (-1 cool .. 1 warm) and its kinship (FolkKin: 1 its own kind, 2 a rival). See StanceToward.</summary>
        public BlobArray<float> Regard;
        public BlobArray<byte> Kinship;
        public IdleActions Idles;
        public float Fidgets, Climbing;
        public bool SpinsWebs, Burrows, CaveDweller, Roosts, Friendly, CrossesWater, Haunts, LavaDweller, DrawnToLight, DrivenOff;
        /// <summary>A-89: darts low over the water round its home, never landing (dragonflies; see CreatureSpecies.Hawks).</summary>
        public bool Hawks;
        /// <summary>No eyes: notices by sound and nearness (Sense). Wades: water folk's taste for the shallows (Decide, StartSwim).</summary>
        public bool Blind;
        public float Wades;
        /// <summary>Bat folk (#35): covers its face from light unless in goggles, never carries a light (ShunsLight); how
        /// keen it is on walks to the surface (SurfaceTrips, a Decide weight).</summary>
        public bool ShunsLight;
        public float SurfaceTrips;
        /// <summary>Turtle folk (A-103): a joined client reads its replicated task as an igloo job (FolkJobs.FromTurtleTask).</summary>
        public bool TurtleJobs;
        /// <summary>Sea folk (A-103): water of any depth is walkable ground; it stays on the bottom and never swims.</summary>
        public bool WalksBed;
        /// <summary>Its town guards' kit (helmet, weapon) and the colours of their armour and sash (linear).</summary>
        public GuardKit GuardKit;
        public float4 GuardArmour, GuardSash;
        public NestSite Nests;
        public float WebSize;
        public float2 FlightHeight;
        public float FlySpeed, FlapRate, Glide, Perching;
        /// <summary>A surface animal sometimes found in caves (see CreatureSpecies.CaveAffinity).</summary>
        public float CaveAffinity;
        /// <summary>Crawls on any surface (see CreatureSpecies.Crawls).</summary>
        public bool Crawls;
        /// <summary>Circles high over one place (see CreatureSpecies.Soars).</summary>
        public bool Soars;
        /// <summary>A swimmer that bursts out to soar and dives back in (see CreatureSpecies.Breaches).</summary>
        public bool Breaches;
        /// <summary>Raids wild hives now and then (see CreatureSpecies.RaidsHives).</summary>
        public bool RaidsHives;
        /// <summary>Has arms, so it can hold things (folk).</summary>
        public bool Hands;
        /// <summary>A child (see CreatureSpecies.Child): a variant's own.</summary>
        public bool Child;
        /// <summary>Four legs and not upright (horses, dogs, deer...): past a run it gallops (Sprint).</summary>
        public bool Gallops;
        /// <summary>Its gallop is a bound (the hind pair together, then the front pair: rabbits, squirrels, weasels)
        /// rather than a rotary gallop (each foot in turn).</summary>
        public bool Bounds;
        /// <summary>Its gallop is a leaping run (CreatureAnimator.LeapGather): gathered, then a long leap (#6).</summary>
        public bool LeapingRun;
        /// <summary>Cold-blooded (CreatureSpecies.ColdBlooded): its breath doesn't show in the cold.</summary>
        public bool ColdBlooded;
        /// <summary>No legs, and a chain lying along the ground off the body (a snake's): it moves along a serpentine wave.</summary>
        public bool Slithers;
        /// <summary>Walks on hooves (CreatureSpecies.Hooves): a clop on hard ground.</summary>
        public bool Hooves;
        /// <summary>Its climbing skill (Climbing, 0-5): CreatureSpecies.ClimbSkill, or from Crawls, Climbing and hands.</summary>
        public byte ClimbSkill;
        /// <summary>Its answer to someone emoting near it (CreatureSpecies.EmoteReaction, Default worked out).</summary>
        public EmoteReaction Reaction;
        /// <summary>Tunnels through the ground (CreatureSpecies.Tunnels): a sand or snow worm.</summary>
        public bool Tunnels;
        /// <summary>Drawn and kept about from this far off; 0 = as any other (see CreatureSpecies.SeenFrom).</summary>
        public float SeenFrom;
        /// <summary>Never moves, and snaps at what comes near (see CreatureSpecies.Rooted).</summary>
        public bool Rooted;
        /// <summary>Sits still as a plant until startled (see CreatureSpecies.Disguised).</summary>
        public bool Disguised;
        /// <summary>Its taste in weather (A-71): what makes it uncomfortable and what shelter it seeks.</summary>
        public ComfortProfile Comfort;
        /// <summary>A-102 breath (Breath): never runs short under water (fish, water and sea folk); else how long it
        /// holds its breath (s, CreatureSpecies.HoldBreath or Breath.DefaultHold).</summary>
        public bool BreathesWater;
        public float HoldBreath;

        public BlobArray<CreaturePart> Parts;
        public BlobArray<CreatureSegment> Segments;
        public BlobArray<float3> Poses;   // Segments.Length × PoseCount, radians
        /// <summary>Colourings individuals choose between (at least one).</summary>
        public BlobArray<CreatureCoatData> Coats;
        /// <summary>Hair colours individuals choose between (may be empty).</summary>
        public BlobArray<float4> HairColors;

        /// <summary>
        /// A fixed random number in -1..1 for one trait of one individual (posture, arm swing, hair…): the same for
        /// as long as the animal lives.
        /// </summary>
        /// <summary>What an individual of this size (CreatureState.Scale) weighs, kg: the species' Weight by size cubed.</summary>
        public float WeightAt(float scale)
        {
            var mid = math.max((Size.x + Size.y) * 0.5f, 1e-3f);
            var r = scale / mid;
            return Weight * r * r * r;
        }

        /// <summary>
        /// A-118: the joint angle (pitch, radians) a leg hanging beneath a four-legged body adds at one joint when it is
        /// fully folded lying down; positive swings back. Front legs fold back under the chest; a hoofed animal's with
        /// the knee forward and the cannon folded flat beneath the forearm. Hind legs fold forward under the belly, the
        /// thigh forward, the shank back down to the hock on the ground and the foot forward again, so no hind leg
        /// ever lies straight out behind.
        /// </summary>
        public static float LieFold(int joint, int count, bool hind, bool hooves)
        {
            if (hind)
            {
                if (count == 1)
                    return -0.9f;
                if (count == 2)
                    return joint == 0 ? -0.9f : 1.8f;
                if (count == 3)
                    return joint == 0 ? -0.9f : joint == 1 ? 1.5f : -1.4f;
                return joint == 0 ? -0.9f : joint == 1 ? 1.3f : joint == 2 ? -1.9f : 0.3f;
            }
            if (hooves && count >= 3)
                return joint == 0 ? 0.55f : joint == 1 ? -1.85f : joint == 2 ? 2.8f : 0f;
            return SitFold(joint, count);
        }

        /// <summary>The older fold, still used for sitting and for a carried animal's tucked legs: each leg's
        /// first joint back, the next forward, the next back.</summary>
        public static float SitFold(int joint, int count)
        {
            if (count == 1)
                return 0.9f;
            if (count == 2)
                return joint == 0 ? 1.1f : -2.2f;
            return joint == 0 ? 1f : joint == 1 ? -2.1f : joint == 2 ? 1.4f : 0f;
        }

        /// <summary>A-118: how far a hoofed animal's folded hind leg rolls out about the hip, lying (radians toward the
        /// side it lies on): the leg on that side out flat beside the belly, the other under the belly toward it.</summary>
        public static float HindRoll(bool downside) => downside ? 1f : 0.5f;

        /// <summary>A-118: a hoofed animal lying tips this far onto the side its hind legs fold to (radians).</summary>
        public const float LieTip = 0.12f;

        public static float Trait(uint individual, int trait) =>
            math.hash(new uint2(individual, (uint)trait * 2654435761u + 17u)) / (float)uint.MaxValue * 2f - 1f;

        /// <summary>Which option of a style group an individual shows.</summary>
        /// <remarks>Scrambled before the modulo: individuals are odd and math.hash keeps the low bit, so with an even
        /// option count the raw hash only ever drew every other option.</remarks>
        public static int Style(uint individual, int group, int count) =>
            (int)(SpawnTable.Scramble(math.hash(new uint2(individual, (uint)group * 40503u + 101u)))
                  % (uint)math.max(count, 1));

        /// <summary>An individual's hair colour (rgb), or the segment's own if the species has none.</summary>
        public float4 HairColor(uint individual, float4 fallback) =>
            HairColors.Length == 0 ? fallback : new float4(HairColors[Style(individual, 9973, HairColors.Length)].xyz, 0f);

        /// <summary>A segment's colour in one of the coats.</summary>
        public float4 SegmentColor(int segment, int coat)
        {
            // Alpha carries how much the segment glows.
            ref var seg = ref Segments[segment];
            ref var c = ref Coats[math.clamp(coat, 0, Coats.Length - 1)];
            var glow = seg.Glow + (seg.Fixed != 0 ? 0f : c.AccentGlow * seg.Accent);
            var rgb = seg.Fixed != 0 ? seg.FixedColor.xyz : math.lerp(c.Base.xyz, c.Accent.xyz, seg.Accent);
            return new float4(rgb, math.saturate(glow));
        }

        /// <summary>Picks a coat by weight from a random number in 0..1.</summary>
        public int PickCoat(float random)
        {
            var total = 0f;
            for (int i = 0; i < Coats.Length; i++)
                total += Coats[i].Weight;
            var pick = random * total;
            for (int i = 0; i < Coats.Length; i++)
            {
                pick -= Coats[i].Weight;
                if (pick <= 0f)
                    return i;
            }
            return Coats.Length - 1;
        }

        /// <summary>Blend of a part's key poses for one segment at gait phase p (0..1).</summary>
        /// <summary>
        /// How far into a sprint a body is (0 up to its run speed, 1 well past it): folk past a run sprint, a gait of its
        /// own (longer strides, leaning in, knees driven high, arms pumping, both feet off the ground between steps); a
        /// four-legged body gallops (a much longer stride, so fewer steps for its speed, the legs reaching well out, and
        /// a moment in the air each stride). Only players' bodies go that fast.
        /// </summary>
        public float Sprint(float speed) => Upright || Gallops ? math.smoothstep(RunSpeed * 1.05f, RunSpeed * 1.6f, speed) : 0f;

        /// <summary>
        /// How one of these folk (this individual) takes a player in a body of this species (Z-52). Its own kind are kin,
        /// a rival folk a rival. Otherwise a cool regard makes some wary (up to 80% at -1) and, from -0.3, some annoyed
        /// (up to half); a warm one makes some warm. Each individual's stance toward a species is fixed (a hash), so
        /// the host's brain and every machine's FolkTalkSystem agree with nothing sent.
        /// </summary>
        public FolkStance StanceToward(int species, uint individual)
        {
            if (species < 0 || species >= Regard.Length)
                return FolkStance.Neutral;
            if (Kinship[species] == FolkKin.Kin)
                return FolkStance.Kin;
            if (Kinship[species] == FolkKin.Rival)
                return FolkStance.Rival;
            var regard = Regard[species];
            var roll = (math.hash(new uint2(individual, (uint)species * 2654435761u)) & 0xFFFFu) / 65536f;
            if (regard > 0f)
                return roll < regard ? FolkStance.Warm : FolkStance.Neutral;
            var annoyed = regard <= -0.3f ? -regard * 0.5f : 0f;
            var wary = -regard * 0.8f;
            return roll < annoyed ? FolkStance.Annoyed : roll < annoyed + wary ? FolkStance.Wary : FolkStance.Neutral;
        }

        /// <summary>
        /// Sprint for this creature as it is now: an animal running for its life or after game gallops from just under
        /// its run speed (Z-50: so a chase is seen at a gallop; otherwise only players' bodies go fast enough).
        /// Everything that reads a leg's phase or stride uses this, so feet stay put.
        /// </summary>
        public float Sprint(in CreatureState s, float speed) =>
            Gallops && (s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Hunt) && speed > RunSpeed * 0.85f * s.Scale
                ? math.max(Sprint(speed), math.smoothstep(RunSpeed * 0.85f, RunSpeed * 1.15f, speed / math.max(s.Scale, 0.01f)))
                : Sprint(speed);

        /// <summary>
        /// A leg's place in the cycle (added to the body's Phase): its own (walk, trot) blended toward its gallop's by
        /// how far into a sprint the body is, the short way round the cycle.
        /// </summary>
        public float LegPhase(ref CreaturePart part, float sprint)
        {
            if (!Gallops || sprint <= 0f || part.Role != LimbRole.Leg)
                return part.Phase;
            var d = math.frac(part.GallopPhase - part.Phase + 0.5f) - 0.5f;
            return math.frac(part.Phase + d * sprint);
        }

        /// <summary>A sprinting leg's sweep, longer than its run's by this share (a gallop's more).</summary>
        public float SprintStride => Gallops ? 0.6f : 0.35f;

        /// <summary>A leg's joint angles at a point of its cycle, with the sprint's longer stride.</summary>
        public float3 LegAngles(int segment, float p, float move, float run, float sprint)
        {
            var a = Angles(segment, p, move, run);
            if (sprint <= 0f)
                return a;
            var idle = Poses[segment * PoseCount + Idle];
            return idle + (a - idle) * (1f + SprintStride * sprint);
        }

        public float3 Angles(int segment, float p, float move, float run)
        {
            var i = segment * PoseCount;
            var walk = Cycle(Poses[i + WalkReach], Poses[i + WalkPush], Poses[i + WalkLift], p);
            var running = Cycle(Poses[i + RunReach], Poses[i + RunPush], Poses[i + RunLift], p);
            return math.lerp(Poses[i + Idle], math.lerp(walk, running, run), move);
        }

        /// <summary>
        /// A gait cycle from three keys: the first half sweeps steadily from reach to push (the foot planted, the
        /// body passing over it); the second half eases back to reach with the lift added in the middle (the foot
        /// carried forward off the ground).
        /// </summary>
        public static float3 Cycle(float3 reach, float3 push, float3 lift, float p)
        {
            if (p < 0.5f)
                return math.lerp(reach, push, p * 2f);
            var t = (p - 0.5f) * 2f;
            return math.lerp(push, reach, math.smoothstep(0f, 1f, t)) + lift * math.sin(t * math.PI);
        }
    }

    public struct CreaturePart
    {
        /// <summary>-1 = the body, otherwise an earlier part this one hangs from the end of.</summary>
        public int Parent;
        /// <summary>Attachment point in metres, in the parent's frame (body frame for the body).</summary>
        public float3 Socket;
        public quaternion Rest;
        public float Phase;
        public int FirstSegment, SegmentCount;
        public LimbRole Role;
        public float Spring;
        /// <summary>+1, or -1 for the mirror image (yaw and roll flipped).</summary>
        public float Side;
        /// <summary>A gallop's phase for a leg of a four-legged body (CreatureBlob.LegPhase): rotary, hind left 0, hind
        /// right 0.1, fore left 0.45, fore right 0.55; bounding, the hinds 0 and the fores 0.5.</summary>
        public float GallopPhase;
        /// <summary>Phase lag per joint down the chain (a travelling ripple).</summary>
        public float JointPhaseStep;
        /// <summary>Segments bend to lie on the ground.</summary>
        public bool FollowGround;
        /// <summary>Style option group (0 = always shown), this part's option in it, and how many options it has.</summary>
        public int StyleGroup, StyleIndex, StyleCount;
        /// <summary>A leg's give: how much further its hip-to-ankle span can stretch beyond the standing pose (m, at
        /// size 1), so how far the ground can fall away under it before the hips must come down.</summary>
        public float Slack;
        /// <summary>Where a leg's foot stands from its hip in the standing pose, in the body's frame (m, at size 1).</summary>
        public float3 Stance;
    }

    public struct CreatureSegment
    {
        public float Length, RadiusStart, RadiusEnd, Flatten;
        /// <summary>Mix from the coat's base colour (0) to its accent (1).</summary>
        public float Accent;
        /// <summary>Non-zero: always FixedColor, whatever the coat (hooves, eyes, beaks).</summary>
        public int Fixed;
        public float4 FixedColor;   // linear
        public float Glow;
        /// <summary>Non-zero: coloured by the individual's hair colour.</summary>
        public int Hair;
    }

    public struct CreatureCoatData
    {
        public float4 Base, Accent;   // linear
        public float AccentGlow;
        public float Weight;
    }

    /// <summary>Which species a creature is. Blobs are built and owned by FaunaDirector.</summary>
    public struct CreatureBody : IComponentData
    {
        public BlobAssetReference<CreatureBlob> Blob;
        public int Species;
        public int SegmentCount;
        /// <summary>Which of the species' coats this animal wears.</summary>
        public int Coat;
    }

    public enum CreatureMode : byte
    {
        Idle,
        Wander,
        Flee,
        /// <summary>Under outside control: keeps its heading and speed, ignores the player (scripts, tests).</summary>
        Puppet,
        /// <summary>Head down, feeding on the spot.</summary>
        Graze,
        /// <summary>Walks to the water's edge (stage 0), then drinks (stage 1).</summary>
        Drink,
        /// <summary>Lying down, awake.</summary>
        Rest,
        /// <summary>Lying curled up, head down; slow to notice danger.</summary>
        Sleep,
        /// <summary>Chase with another of its kind (Partner): stage 0 chasing, stage 1 running away; a tag swaps them.</summary>
        Play,
        /// <summary>Chasing prey (Partner): stalking low, then sprinting.</summary>
        Hunt,
        /// <summary>Walks to its work spot (stage 0), then does its task there facing its work (stage 1).</summary>
        Work,
        /// <summary>
        /// Climbs a tree (Anchor is the trunk's foot): walks to it (0), climbs (1), walks out onto a branch (2), sits
        /// there (3), back to the trunk (4), climbs down (5).
        /// </summary>
        Climb,
        /// <summary>
        /// Goes to its web (Anchor is the web's centre): walks under it (0), climbs a thread (1), then waits in it head
        /// down. In the web, Stage says what holds it up, so every machine can draw the web from the spider alone:
        /// 2 = a trunk to the spider's right, 3 = to its left, 4 = strung between two things either side, each
        /// AnchorSize from the centre.
        /// </summary>
        Web,
        /// <summary>
        /// Goes to ground in its burrow (Anchor is the burrow's mouth): to the mouth (0), down it (1), hidden below
        /// (2), back up (3).
        /// </summary>
        Burrow,
        /// <summary>
        /// Flies to its roost and hangs there asleep (Anchor is the ceiling or branch it hangs from). In a cave: to the
        /// mouth (1), along the passage node by node (10 + node), up to the ceiling (199), hanging (200), and out again
        /// at dusk (100 + node). Under a branch: straight there (198), then hanging.
        /// </summary>
        Roost,
        /// <summary>A friendly animal going over to a person (Focus: where they are), then keeping them company.</summary>
        Greet,
        /// <summary>
        /// Being petted by a person (Holder; Focus: where they are): enjoying it (stage 0: leaning in, tail going) or
        /// putting up with it (1); then it goes on with its day, or keeps the person company.
        /// </summary>
        Petted,
        /// <summary>Picked up and carried by a person (Holder), held in their arms; it may squirm free.</summary>
        Held,
        /// <summary>
        /// Folk going indoors through their door (Anchor; AnchorYaw: the way out): to the door (0), stepping in (1),
        /// indoors out of sight (2), stepping out again (3). ActionTime is how far out of sight they are while
        /// stepping through (it rides on the ghost, so every machine dithers them the same).
        /// </summary>
        Home,
        /// <summary>
        /// A guard on its rounds (task Patrol): along the town's streets to the corner it's making for (WorkSpot,
        /// stage 0; WorkFace is the one it came from), pausing there to look about (stage 1); a gate when AnchorSize > 0.
        /// </summary>
        Patrol,
        /// <summary>
        /// Folk gone for a swim on a warm day: to the bank where they go in (WorkSpot, stage 0), swimming about in the
        /// water round Focus (1), back to the bank (2), standing drying off (3).
        /// </summary>
        Swim,
        /// <summary>
        /// Folk taking their ease: to the spot (Target, stage 0; the posture it's going for in TargetHeight), then sitting
        /// on the ground facing Focus (1: by the well, on the green), or lying back in the grass (3). (2 is kept for
        /// sitting on a seat, once there are seats to sit on.)
        /// </summary>
        Sit,
        /// <summary>
        /// A-71 row 2: out of the weather. To the shelter it chose (Target, stage 0; walking, or hurrying when the
        /// weather is bad), then waiting there facing Focus (1) till the weather lets up or its time runs out. What the
        /// shelter is (a ShelterNeed bit) rides in TargetHeight.
        /// </summary>
        Shelter,
        /// <summary>
        /// A-104: riding another creature (Riding): no mind of its own while it rides, put on its mount's seat each frame
        /// (RideSystem). How it sits (a RidePose) is in Stage; what it rides is in Holder (minus the mount's ghost id).
        /// </summary>
        Ride
    }

    /// <summary>What a worker does at its post (posed by CreatureAnimator).</summary>
    public enum CreatureTask : byte
    {
        None,
        [UnityEngine.Tooltip("Swings a hammer at an anvil (right arm), holding the work with the left.")] Hammer,
        [UnityEngine.Tooltip("Swings an axe down with both arms (chopping wood).")] Chop,
        [UnityEngine.Tooltip("Leans on a counter, now and then raising a hand (a vendor).")] Gesture,
        [UnityEngine.Tooltip("Both hands working in front, slowly (drawing water, tending a fountain, stirring).")] Tend,
        [UnityEngine.Tooltip("Stands keeping watch, hands behind the back (a gatekeeper).")] Watch,
        [UnityEngine.Tooltip("Goes out by day after game: creeps up on it, throws (and, for now, always misses).")] Hunt,
        [UnityEngine.Tooltip("A guard: walks the town's streets and out to its gates on its shift (by day or by night, half " +
                             "each), spear in hand, a torch after dark; a night guard sleeps the day indoors.")] Patrol,
        [UnityEngine.Tooltip("Fishes by day from the shore nearest home (its own spot, the same each time): waits with its " +
                             "float out, casts again now and then, sometimes lands a fish into its basket; home at dusk.")] Fish,
        [UnityEngine.Tooltip("Mines by day: walks from its camp into the nearest mine and along its passages to an ore face (its " +
                             "own, the same each day), swings its pick at it a while, carries a sack of ore back out to the " +
                             "camp, and goes back for more; home at dusk.")] Mine,
        [UnityEngine.Tooltip("A traveller on the roads between towns (FaunaDirector puts one on a road now and then, out of " +
                             "sight): walks briskly along it, staff in hand and pack on its back, to the town at its end, and " +
                             "stays there a while as a visitor.")] Travel,
        [UnityEngine.Tooltip("A messenger bird (FaunaDirector sends one to a town a player is in, now and then): flies in " +
                             "from the direction of another town with a rolled message, lands by one of the townsfolk, who " +
                             "takes it and reads it, and flies off towards another town.")] Messenger,
        [UnityEngine.Tooltip("A critter: one of the small life round a player (songbirds, owls, woodpeckers, frogs, mice), " +
                             "put at its spot from the seed by FaunaDirector's critter tier, near players only, and run on a " +
                             "light brain (no needs, no company or prey to look for).")] Critter,
        [UnityEngine.Tooltip("A hermit, living alone at a hermit's shelter (its Home anchor): chops wood, tends its garden, " +
                             "fetches water, sits by its door and its fire, sleeps in its shelter at night; wary of anyone " +
                             "who comes close (watches them, backs off, shoos them).")] Hermit,
        [UnityEngine.Tooltip("A traveller stopping the night at a roadside camp (its Camp anchor): sits round the fire in the " +
                             "evening, lies down on a bedroll for the night, and goes on its way in the morning.")] Camper
    }

    /// <summary>How one of the folk takes the player's body (CreatureBlob.StanceToward, Z-52).</summary>
    public enum FolkStance : byte
    {
        /// <summary>As ever: answers emotes (unless shy), talks when passed.</summary>
        Neutral,
        /// <summary>One of its own: always answers, now and then waves first.</summary>
        Kin,
        /// <summary>A playful rival: answers any emote with a mock bow, teases.</summary>
        Rival,
        /// <summary>Keeps a few metres off, watching; doesn't answer emotes; says only wary lines.</summary>
        Wary,
        /// <summary>Turns away from an emote and walks off; says only curt lines.</summary>
        Annoyed,
        /// <summary>Warm toward the player's folk: always answers, now and then waves first.</summary>
        Warm
    }

    /// <summary>CreatureBlob.Kinship values.</summary>
    public static class FolkKin
    {
        public const byte Stranger = 0, Kin = 1, Rival = 2;
    }

    /// <summary>
    /// The stages of a hunter's outing (CreatureMode.Hunt for a creature with the Hunt task, numbered apart from an
    /// animal's own chase): walking out and creeping up on the game, aiming, the throw, then a short run after it.
    /// </summary>
    public static class FolkHunting
    {
        public const byte Stalk = 10, Aim = 11, Throw = 12, Chase = 13;
    }

    /// <summary>World conditions animals react to (set by FaunaDirector).</summary>
    public struct FaunaClock : IComponentData
    {
        /// <summary>0 by day, 1 at night.</summary>
        public float Night;
        /// <summary>The time of day, 0 to 24 (18 at sunset).</summary>
        public float Hour;
        /// <summary>Whole days since the start (the cycle's count, shared world state): the notice board's posts.</summary>
        public int Day;
    }

    /// <summary>
    /// Something held in the hand (the equipment slot's first use): drawn at the hand by CreatureRenderSystem, with the
    /// arm posed to hold it. Numbered for the network: append, never reorder.
    /// </summary>
    public enum GuardKit : byte
    {
        /// <summary>A kettle hat, a spear.</summary>
        Human,
        /// <summary>A rounded helm with horns, an axe.</summary>
        Dwarf,
        /// <summary>A tall helm with a leaf-shaped crest, a bow across the back.</summary>
        Elf,
        /// <summary>A pot for a helmet, a crude club and a scrap shield.</summary>
        Goblin,
        /// <summary>A skull for a cap, a great club over the shoulder.</summary>
        Ogre
    }

    /// <summary>Bits of CreatureState.Worn.</summary>
    public static class CreatureWorn
    {
        public const byte Hat = 1, Pack = 2;
        /// <summary>A-103 row 5: a player's eyewear (Eyewear): a small dark pair of glasses, or chunky glow goggles. Eyes
        /// is both, the bits NetAvatars owns (Items' gear keeps them).</summary>
        public const byte Glasses = 4, Goggles = 8, Eyes = Glasses | Goggles;
    }

    public enum HeldItem : byte
    {
        None,
        /// <summary>A lantern in the off hand, lit: folk out and about after dark.</summary>
        Lantern,
        /// <summary>A guard's weapon (its folk's: GuardKit), worn in its sheath on the back or at the hip: a guard on
        /// its rounds by day. (Drawn, it's SpearDrawn.)</summary>
        Spear,
        /// <summary>A burning torch held up in the right hand: a guard on its rounds after dark.</summary>
        Torch,
        /// <summary>A fishing rod in both hands, its line out to a float on the water (and a basket at the feet).</summary>
        Rod,
        /// <summary>A stick in the right hand: a child's, for poking at things.</summary>
        Stick,
        /// <summary>A stone in the right hand: a child's, for throwing (another picked up after each throw).</summary>
        Stone,
        /// <summary>A guard's weapon drawn and in its right hand: danger about (a beast in town, someone carrying on).</summary>
        SpearDrawn,
        /// <summary>A miner's pick: worn across its back on the way, in both hands swinging at the ore face.</summary>
        Pickaxe,
        /// <summary>A sack of ore carried in both arms, out of the mine to the camp (the pick on its back).</summary>
        OreSack,
        /// <summary>A traveller's walking staff in the right hand (and a pack on its back).</summary>
        Staff,
        /// <summary>A rolled message: on a messenger bird's leg, then in the hands of the one who took it, reading it.</summary>
        Letter,
        /// <summary>A chunk of honeycomb a raiding bear knocked down, on the ground at its nose while it licks at it.</summary>
        Honey,
        /// <summary>Smoked goggles over the eyes (bat folk on the surface by day, #35): worn, not held, so the light
        /// doesn't make it cover its face.</summary>
        Goggles
    }

    /// <summary>A town guard's weapon, by its folk: how long, and where it's worn sheathed.</summary>
    public static class GuardWeapons
    {
        /// <summary>The weapon's length (m, at scale 1): spear, axe, bow, club, great club.</summary>
        public static float Length(GuardKit kit) => kit switch
        {
            GuardKit.Dwarf => 0.95f,
            GuardKit.Elf => 1.1f,
            GuardKit.Goblin => 0.55f,
            GuardKit.Ogre => 1.15f,
            _ => 2.1f
        };

        /// <summary>
        /// Worn across the back if it's long for its bearer (over 0.6 of its height, about twice its hip height),
        /// else at the hip: the same on every machine, from the folk's kit and the bearer's size alone.
        /// </summary>
        public static bool OnBack(GuardKit kit, float scale, float standHeight) =>
            Length(kit) * scale > 0.6f * 2f * standHeight * scale;
    }

    /// <summary>
    /// An individual's temperament (#50): values rolled once from its Individual within its species' ranges. Never
    /// stored or sent: Individual is replicated, so every machine (host, client) works out the same values. Add new
    /// values here; only Aggression drives behaviour so far (a dog hunts when it's over the species' HuntAggression).
    /// </summary>
    public struct CreaturePersonality
    {
        /// <summary>0 (meek) .. 1 (fierce).</summary>
        public float Aggression;

        // (Trait slots 60+ are the personality's; the animator and brain use lower ones.)
        const int AggressionTrait = 60;

        public static CreaturePersonality Of(ref CreatureBlob b, uint individual) => new CreaturePersonality
        {
            Aggression = math.lerp(b.Aggression.x, b.Aggression.y, CreatureBlob.Trait(individual, AggressionTrait) * 0.5f + 0.5f)
        };

        /// <summary>Whether this individual is fierce enough to hunt at all (species with no threshold: always).</summary>
        public static bool WillHunt(ref CreatureBlob b, uint individual) =>
            b.HuntAggression <= 0f || Of(ref b, individual).Aggression >= b.HuntAggression;
    }

    /// <summary>A creature's position, heading, gait and what it's up to.</summary>
    public struct CreatureState : IComponentData
    {
        public float3 Position;
        /// <summary>Heading in radians; forward is (sin, 0, cos).</summary>
        public float Yaw;
        public float Speed;
        /// <summary>
        /// The speed its legs are posed for: Speed eased at a creature's own acceleration (CreatureMotion.EaseGait), so a
        /// sudden start, stop or burst doesn't swap the gait's pose under a planted foot. Local only, never sent.
        /// </summary>
        public float GaitSpeed;
        public float Phase;
        public float YawRate;
        /// <summary>Spring state for tails and ears (lags the turns).</summary>
        public float Sway;
        /// <summary>Head turn relative to the body, radians; and up (+) or down (−), radians.</summary>
        public float Look;
        public float LookPitch;
        /// <summary>A glance's seconds left, then seconds before the next (what it looks at: CreatureMind.LookAt).</summary>
        public float LookFor, LookCool;
        public float Scale;
        public float2 Target;
        public float Timer;
        public CreatureMode Mode;
        public uint Seed;

        /// <summary>The other animal in a game of chase or a hunt.</summary>
        public Entity Partner;
        /// <summary>What it's paying attention to: the threat it flees, the water it drinks from.</summary>
        public float2 Focus;
        public byte Stage;
        /// <summary>Seconds in the current mode and stage.</summary>
        public float ModeTime, StageTime;
        /// <summary>Seconds alive, for breathing and chewing.</summary>
        public float Clock;
        /// <summary>Where it lives (a settlement, a den); with Leash &gt; 0 it wanders no further than that from it.</summary>
        public float2 Home;
        public float Leash;
        /// <summary>A-88 row 2 (#37): a lake fish that keeps this far over the bed (a sea folk dome school, the big bed fish), 0 for
        /// the usual swim just under the surface. Set at spawn (FaunaDirector.SpawnLakeFish).</summary>
        public float SwimAbove;
        /// <summary>
        /// A-103: a folk's own door where no Door anchor marks one (a turtle folk's igloo tunnel): xz the spot it steps in
        /// from, y unused, w the outward yaw. Zero for none (FindDoor then looks for anchors). Set at spawn; local, not sent.
        /// </summary>
        public float4 OwnDoor;
        /// <summary>
        /// Two folk stopped to talk (CreatureBrainSystem.Chatting; local, host only, not sent): seconds before this one
        /// starts or joins another talk; its part in one (1 started it, 2 the one it stopped, 3 a child standing by);
        /// what it was doing before, to carry on after (ChatWalk 1 walking: ChatResume its target; 2 on its way to its
        /// post: ChatResume.x its time left at work; 3 on its way to a seat; see BeginChat).
        /// </summary>
        public float ChatCool;
        public byte ChatRole, ChatWalk;
        /// <summary>A folk's job when the host handed it one (FolkJob; 0 = the one its Task implies). Local, not a ghost field (A-29).</summary>
        public byte Job;
        /// <summary>Fixed for life: picks this individual's traits (posture, gait, hair). Set at spawn.</summary>
        public uint Individual;
        /// <summary>A worker's job, where it stands to do it, and what it faces there.</summary>
        public CreatureTask Task;
        public float2 WorkSpot, WorkFace;
        /// <summary>A leap out of the water: progress 0..1 (0 = not leaping), and how high it goes.</summary>
        public float Air, AirHeight;

        // Posture, 0..1, eased towards what the current activity wants (read by CreatureAnimator).
        /// <summary>Lying down: body on the ground, legs folded.</summary>
        public float Lie;
        /// <summary>Head lowered to the ground (grazing, drinking, sleeping).</summary>
        public float HeadDown;
        /// <summary>Play bow: chest down, rump up.</summary>
        public float Bow;
        /// <summary>Curled up asleep: tail wrapped round, ears back.</summary>
        public float Curl;
        /// <summary>Sitting on its haunches (hind legs folded, chest up).</summary>
        public float Sit;
        /// <summary>Body pitched up (+1: climbing a trunk, sitting up tall) or down (-1: diving, hanging in a web).</summary>
        public float Vertical;
        /// <summary>In flight: wings beating or spread, legs tucked.</summary>
        public float Fly;
        /// <summary>A player's body rolling on the ground: how far through the somersault (0..1; 0 = not rolling).</summary>
        public float Roll;

        /// <summary>An idle action under way and how far through it is (0..1), and how long it lasts.</summary>
        public IdleAction Action;
        public float ActionTime, ActionLength;

        /// <summary>A fixed point it's using: the foot of the tree it climbs, its web's centre, its burrow or nest.</summary>
        public float3 Anchor;
        /// <summary>The tree's trunk radius, or the web's, burrow's or nest's radius (0 = no anchor yet, -1 = none to be
        /// had here); the web's facing and side.</summary>
        public float AnchorSize, AnchorYaw;
        public byte AnchorSide;
        /// <summary>
        /// A-73: a fishing folk's wharf spot (WharfSpotSet.Spots index + 1; 0 = none), and where that wharf stands
        /// (its base, turn and size), for the walk out along its deck.
        /// </summary>
        public short PierSpot;
        public float3 PierOrigin;
        public float PierYaw, PierScale;
        /// <summary>Where a flight or a climb ends up, height-wise (absolute).</summary>
        public float TargetHeight;
        /// <summary>Caught (by a diving bird of prey): FaunaDirector removes it.</summary>
        public bool Gone;
        /// <summary>The cave it roosts in (-1 = none), and the passage node its roost is at.</summary>
        public short LairCave, LairNode;
        /// <summary>Who is petting or carrying it, or what it rides (A-104). Positive: its holder (Interactions.HolderId).
        /// Negative: in Mode Ride, it is riding, and this is minus the mount's ghost id (Riding.SoloMount in a solo world,
        /// which has no ghosts); in Mode Held or Petted, one of the folk has it, and this is minus that folk's ghost id
        /// (Interactions.FolkHolder; A-120). 0 = neither. Read it through Riding.Mount/Dismount rather than by hand.</summary>
        public int Holder;
        /// <summary>What it has in hand (folk: a lantern at night). Replicated; each machine draws it.</summary>
        public HeldItem Held;
        /// <summary>What a player's body has on besides (CreatureWorn bits, P-23). Not sent: each machine sets it (Items.ApplyGear).</summary>
        public byte Worn;

        /// <summary>The need cue it shows (NeedCue: none, tired, hungry, thirsty, lonely): its strongest need past 0.6,
        /// set by the brain (CreatureBrainSystem.Needs) and replicated as one byte, so clients draw the same cue (#64).</summary>
        public byte Cue;
        /// <summary>Half the width of what this player's body carries in its arms (m; 0 for nothing): local, set each
        /// frame on every machine by HeldPoseSystem from the creature carried, so the arms open round it. Not sent.</summary>
        public float CarryWidth;
    }

    /// <summary>
    /// What only the brain reads and writes (#58): split off CreatureState so the snapshot, the ghosts and every other
    /// reader copy less. Host (or single player) only, never sent; FaunaDirector adds it to every animal it spawns.
    /// </summary>
    public struct CreatureMind : IComponentData
    {
        /// <summary>
        /// What it's looking at, for a glance (Look/LookPitch turn the head there): a point (LookKind 0), a creature it
        /// follows (1: LookEntity), or the player (2); for CreatureState.LookFor more seconds. Set by the brain
        /// (CreatureBrainSystem.Glance).
        /// </summary>
        public float3 LookAt;
        public Entity LookEntity;
        public byte LookKind;
        /// <summary>Brain LOD (#59): time since a far creature last thought, and how far it coasted meanwhile.</summary>
        public float LodDebt;
        public float2 LodCoast;
        /// <summary>A talk's carry-on (see CreatureState.ChatWalk and CreatureBrainSystem.BeginChat).</summary>
        public float2 ChatResume;
        /// <summary>A guard: which leg of its round it's on; and how long before it goes after another beast (s).</summary>
        public byte RouteLeg;
        public float ChaseCooldown;
        /// <summary>A guard with its weapon drawn: how long since anything needed it (it's put away after a while).</summary>
        public float WeaponCalm;
        /// <summary>Seconds left before a startled animal will settle again (a bat waved off its roost).</summary>
        public float Unsettled;
        // Needs, 0 (satisfied) to 1 (pressing): they rise with time by the species' tastes and are eased by the
        // activity that meets them; what it decides to do next is weighted by them. Only the brain reads them (#58:
        // moved here from CreatureState); what others see is CreatureState.Cue.
        public float Hunger, Thirst, Tired, Lonely;
        /// <summary>A-102 breath (Breath): how much of it is used up under water (0 full, 1 empty; 0 by default, so a
        /// new creature starts full), and whether, having emptied it, it is making for the surface until it has caught
        /// some back. Brain only, like the needs.</summary>
        public float BreathUsed;
        public bool Gasping;
        /// <summary>A-102 row 2, folk out swimming: how far under their floating height they are (m), how far they are
        /// making for (0: up to the surface), and how long before they turn again (s: a rest at the surface, or a water
        /// breather's time at a depth). Brain only (clients see the dive in Position and Vertical).</summary>
        public float Dive, DiveTo, DiveWait;
        /// <summary>A-71: how uncomfortable the weather has made it (0..1: builds while out of its comfort, fades in
        /// comfort or shelter), why (the worst reason last seen), and how hard the weather pressed at the last check.</summary>
        public float Discomfort, ComfortPressure;
        public ComfortKind DiscomfortKind;
        /// <summary>A-71 row 2: the shelter it is standing in now (none out in the open), and how long before it
        /// looks for shelter again after a search that found none (s).</summary>
        public ShelterNeed Cover;
        public float ShelterRetry;
        /// <summary>A-121 row 2: how far past a lit fire's flames it keeps (m): its species' and its own shyness, or the
        /// distance where the fire's heat gets too much for it. Set at each comfort check.</summary>
        public float FireKeep;
        /// <summary>A-71 row 6: the player it's following on a lead quest (their Holder id, Interactions.HolderId; 0 for
        /// none) and how long before it gives up (s). Set by Follow.Start and Follow.Stop on the host.</summary>
        public int FollowHolder;
        public float FollowTime;
        /// <summary>A-88 row 1: a following creature's way round something in its way (a trunk, a rock, a steep bank):
        /// the spot to the side it heads for, and how long it keeps to it (s) before making for the player again.</summary>
        public float2 FollowDetour;
        public float FollowDetourTime;
        /// <summary>A-110 row 5, a town household member far from every player: 1 while it only follows its Whereabouts
        /// (CreatureBrainSystem.Legs) instead of thinking; where its day has it (LegsAt, at the creature's Clock LegsSet),
        /// how fast that spot moves (m/s) and which way it faces at rest. Set by FaunaDirector.MembersLegs, cleared by
        /// the brain when it wakes. Host only, never sent.</summary>
        public byte Legs;
        public float2 LegsAt, LegsVel, LegsFace;
        public float LegsSet;
    }

    /// <summary>The need cue a creature shows (CreatureState.Cue), strongest need first.</summary>
    public enum NeedCue : byte { None, Tired, Hungry, Thirsty, Lonely }

    /// <summary>One segment as the creature shader draws it (a tapered capsule). Must match Creature.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SegmentInstance
    {
        public float3 Position;
        public float Length;
        public quaternion Rotation;
        public float RadiusStart, RadiusEnd, Flatten, Pad;
        public float4 Color;
        /// <summary>x: what it's made of (a SegmentMaterial, as a float), shaded to match; z: how many segments back
        /// the one it hangs from is (the joint seam, CreatureAnimator); w: its look code (SurfaceLook: fur and its
        /// length, feathers, scales, hoof, eye..., A-97), written by the render system; y: the ground height under a body (grounding), set by the render system.</summary>
        public float4 Material;
    }

    /// <summary>What a drawn segment is made of, for the creature shader (stored as a float in SegmentInstance.Material.x).</summary>
    public enum SegmentMaterial : byte
    {
        /// <summary>A body as it always was: skin, fur, feathers, scales (soft, wrapped light).</summary>
        Body,
        /// <summary>Matte, a fine weave, soft folds.</summary>
        Cloth,
        /// <summary>A soft sheen, fine grain, worn darker at the ends, a line of stitches.</summary>
        Leather,
        /// <summary>Glossy: the shared metal look (Metal.hlsl, as the world's metal parts): the sky reflected, a sharp highlight, glints; a hammered surface.</summary>
        Metal,
        /// <summary>Grain streaks along its length.</summary>
        Wood,
        /// <summary>Ivory, a waxy sheen, small pits.</summary>
        Bone
    }

    /// <summary>A specimen held in a pose for a test ("-pose"): the brain leaves it alone; DebugControls poses it.</summary>
    public struct PosedForTest : IComponentData { }

    /// <summary>Material and mesh for drawing creatures (a managed singleton set up by FaunaDirector).</summary>
    public class FaunaRenderSettings : IComponentData
    {
        public Material Material;
        public Mesh Capsule;
        /// <summary>Fewer sides and rows, for creatures in the middle and far pose bands (A-70 row 4).</summary>
        public Mesh CapsuleMid, CapsuleFar;
    }

    public static class CreatureBlobBuilder
    {
        struct Expanded
        {
            public LimbSlot Slot;
            public bool Mirrored;
            public int Parent;
            public int SlotIndex;
        }

        /// <summary>Expands slots: mirrored slots become left and right copies; children follow every copy of their parent.</summary>
        static List<Expanded> Expand(CreatureSpecies species)
        {
            var parts = new List<Expanded>();
            var copies = new List<List<int>>();
            for (int i = 0; i < species.Parts.Count; i++)
            {
                var slot = species.Parts[i];
                var mine = new List<int>();
                copies.Add(mine);
                if (slot.Limb == null)
                    continue;
                var parents = slot.Parent >= 0 && slot.Parent < i ? copies[slot.Parent] : new List<int> { -1 };
                foreach (var parent in parents)
                {
                    var parentMirrored = parent >= 0 && parts[parent].Mirrored;
                    mine.Add(parts.Count);
                    parts.Add(new Expanded { Slot = slot, Mirrored = parentMirrored, Parent = parent, SlotIndex = i });
                    if (slot.Mirror)
                    {
                        mine.Add(parts.Count);
                        parts.Add(new Expanded { Slot = slot, Mirrored = !parentMirrored, Parent = parent, SlotIndex = i });
                    }
                }
            }
            return parts;
        }

        /// <summary>
        /// The look of each segment of a species, in blob segment order (segment 0 is the body): a SurfaceLook code
        /// plus 16 × FurLength for fur. Kept off the creature blob so looks never move the fauna fingerprint.
        /// </summary>
        public static byte[] Looks(CreatureSpecies variant)
        {
            var species = variant.Body;
            var parts = Expand(species);
            var count = 1;
            foreach (var p in parts)
                count += p.Slot.Limb.Segments.Count;
            var looks = new byte[count];
            looks[0] = Lie(SurfaceLook.Of(species.Coat, SegmentSurface.Coat, species.FurLength), LimbRole.Head);
            var cursor = 1;
            foreach (var p in parts)
                foreach (var seg in p.Slot.Limb.Segments)
                {
                    var length = p.Slot.FurLength != FurLengthOverride.Default ? p.Slot.FurLength
                               : seg.FurLength != FurLengthOverride.Default ? seg.FurLength
                               : FurLengthOverride.Default;
                    looks[cursor++] = Lie(SurfaceLook.Of(species.Coat, seg.Surface,
                        length == FurLengthOverride.Default ? species.FurLength : SurfaceLook.Length(length)), p.Slot.Limb.Role);
                }
            return looks;

            // A-123 row 3: which way a feathered segment's feathers lie (the body counts as running towards the head).
            static byte Lie(byte code, LimbRole role) =>
                code != SurfaceLook.Feathers ? code
                : role is LimbRole.Head or LimbRole.Neck ? (byte)(code | SurfaceLook.FeatherBody << 4)
                : role is LimbRole.Wing or LimbRole.Tail ? (byte)(code | SurfaceLook.FeatherFlight << 4)
                : code;
        }

        /// <summary>
        /// Builds a species. A variant takes its body, parts, gait and behaviour from the species it is a variant
        /// of, and only its own colours and size.
        /// </summary>
        /// <param name="prey">Indices of the species it hunts.</param>
        /// <param name="hunted">Whether any species hunts it.</param>
        /// <param name="regard">Folk only: its regard for each species (by index), and its kinship (FolkKin).</param>
        /// <param name="build">A-124: one individual's build (BodyBuilds.Of); null for the species' own. The species' own
        /// multiplies by exactly 1, so the shared blobs (and the fauna fingerprint) bake byte for byte as before.</param>
        public static BlobAssetReference<CreatureBlob> Build(CreatureSpecies variant, IReadOnlyList<int> prey = null, bool hunted = false,
                                                             IReadOnlyList<float> regard = null, IReadOnlyList<byte> kinship = null,
                                                             Vector2? home = null, BodyBuild? build = null)
        {
            var bb = build ?? BodyBuild.Own;
            var species = variant.Body;
            var parts = Expand(species);
            // M-05: the Model Lab's adjustments for this species (null: none). Every multiplier defaults to 1 and is
            // applied as a plain multiply, which for 1 is exact, so an untuned species bakes byte for byte as before.
            var tune = CreatureTunables.For(species.name);
            var bodyLength = species.BodyLength * (tune?.BodyLength ?? 1f);
            var speed = tune?.Speed ?? 1f;
            var size = tune?.Size ?? 1f;
            var neckScale = tune?.Neck ?? 1f;
            var tailScale = tune?.Tail ?? 1f;
            var swing = tune?.Swing ?? 1f;

            var segmentCount = 1;
            foreach (var p in parts)
                segmentCount += p.Slot.Limb.Segments.Count;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CreatureBlob>();
            root.BodyLength = bodyLength;
            // (Short bodies bound, longer ones gallop, unless the species says.)
            root.Bounds = species.Gallop == GallopStyle.Bound || species.Gallop == GallopStyle.Auto && bodyLength < 0.5f;
            root.LeapingRun = species.Gallop == GallopStyle.Leap;
            // A variant's own proportions (a child's big head, chunky limbs and short legs) on the body it's based on.
            var own = variant != species;
            var headScale = (own ? variant.VariantHead : 1f) * (tune?.Head ?? 1f) * bb.Head;
            var thickness = (own ? variant.VariantThickness : 1f) * (tune?.Thickness ?? 1f) * bb.Girth;
            var legScale = (own ? variant.VariantLegs : 1f) * (tune?.Legs ?? 1f) * bb.Limbs;
            root.FrontRadius = species.BodyRadius * thickness;
            root.RearRadius = species.BodyRadius * species.BodyTaper * thickness;
            root.BodyFlatten = species.BodyFlatten;
            root.WalkSpeed = species.WalkSpeed * speed;
            root.RunSpeed = math.max(species.RunSpeed * speed, species.WalkSpeed * speed * 1.3f);
            root.Bob = new float2(species.Bob.x, species.Bob.y);
            root.BobsPerCycle = new float2(species.BobsPerCycle.x, species.BobsPerCycle.y);
            root.RunRock = math.radians(species.RunRock);
            root.Upright = species.Upright;
            root.ColdBlooded = species.ColdBlooded;
            root.Hooves = species.Hooves;
            root.Tunnels = species.Tunnels;
            root.Reaction = species.EmoteReaction != EmoteReaction.Default ? species.EmoteReaction
                          : species.Friendly ? EmoteReaction.Wags : species.FleeDistance > 0f ? EmoteReaction.Flees : EmoteReaction.Ignores;
            root.Habitat = species.Habitat;
            root.SwimDepth = species.SwimDepth;
            root.Leaps = species.Leaps;
            root.WanderRadius = species.WanderRadius;
            root.FleeDistance = species.FleeDistance;
            root.FleeGas = species.FleeGas;
            root.TurnRate = math.radians(species.TurnRate);
            root.IdleTime = new float2(species.IdleTime.x, species.IdleTime.y);
            var sizeScale = variant != species ? variant.VariantScale : 1f;
            root.Size = new float2(species.Size.x, species.Size.y) * sizeScale * size;
            root.Active = variant.Active;
            root.Grazing = variant.Grazing;
            root.Thirst = variant.Thirst;
            root.Playfulness = variant.Playfulness;
            root.Laziness = variant.Laziness;
            root.Hunger = variant.Hunger;
            root.ChaseTime = variant.ChaseTime;
            root.Weight = own && variant.Weight <= 0f ? species.Weight * sizeScale * sizeScale * sizeScale : variant.Weight;
            root.Aggression = new float2(math.saturate(math.min(variant.Aggression.x, variant.Aggression.y)),
                                         math.saturate(math.max(variant.Aggression.x, variant.Aggression.y)));
            root.HuntAggression = variant.HuntAggression;
            root.Hunted = hunted;
            root.Idles = species.Idles;
            root.Fidgets = species.Fidgets;
            root.Climbing = species.Climbing;
            root.SpinsWebs = species.SpinsWebs;
            root.Burrows = species.Burrows;
            root.CaveDweller = species.CaveDweller;
            root.Blind = species.Blind;
            root.Wades = species.Wades;
            root.ShunsLight = species.ShunsLight;
            root.SurfaceTrips = species.ShunsLight ? species.SurfaceTrips : 0f;
            root.TurtleJobs = species.TurtleJobs;
            root.WalksBed = species.WalksBed;
            root.Roosts = species.Roosts;
            root.Friendly = species.Friendly;
            root.CrossesWater = species.CrossesWater && species.Habitat == Habitat.Land;
            root.Haunts = species.Haunts;
            root.LavaDweller = species.LavaDweller;
            root.CaveAffinity = variant.CaveAffinity;
            root.Crawls = species.Crawls;
            root.DrawnToLight = species.DrawnToLight;
            root.Hawks = species.Hawks;
            root.DrivenOff = species.DrivenOff;
            root.GuardKit = species.GuardKit;
            // (Alpha 0: a segment colour's alpha is how much it glows, and armour doesn't.)
            root.GuardArmour = new float4(((Vector4)species.GuardArmour.linear).x, ((Vector4)species.GuardArmour.linear).y, ((Vector4)species.GuardArmour.linear).z, 0f);
            root.GuardSash = new float4(((Vector4)species.GuardSash.linear).x, ((Vector4)species.GuardSash.linear).y, ((Vector4)species.GuardSash.linear).z, 0f);
            root.Soars = species.Soars;
            root.Breaches = species.Breaches && species.Habitat == Habitat.Swims;
            root.RaidsHives = species.RaidsHives;
            root.Hands = species.Parts.Exists(p => p != null && p.Limb != null && p.Limb.Role == LimbRole.Arm);
            root.ClimbSkill = Climbing.SpeciesSkill(species.ClimbSkill, species.Crawls, species.Climbing, root.Hands);
            root.Child = variant.Child;
            root.SeenFrom = species.SeenFrom * sizeScale * size;
            root.Rooted = species.Rooted;
            root.Disguised = species.Disguised;
            root.Comfort = ComfortProfiles.For(variant, home ?? new Vector2(-1f, -1f));
            root.BreathesWater = species.BreathesWater || species.Habitat == Habitat.Swims;
            root.HoldBreath = species.HoldBreath > 0f ? species.HoldBreath
                : Breath.DefaultHold(root.Upright && root.Hands, root.CrossesWater, species.Habitat);
            root.Nests = species.Nests;
            root.WebSize = species.WebSize * sizeScale * size;
            root.FlightHeight = new float2(species.FlightHeight.x, math.max(species.FlightHeight.x, species.FlightHeight.y));
            root.FlySpeed = species.FlySpeed;
            root.FlapRate = species.FlapRate;
            root.Glide = species.Glide;
            root.Perching = species.Perching;
            var preyArray = builder.Allocate(ref root.Prey, prey?.Count ?? 0);
            for (int i = 0; i < preyArray.Length; i++)
                preyArray[i] = prey[i];
            var regards = regard != null && kinship != null && regard.Count == kinship.Count ? regard.Count : 0;
            var regardArray = builder.Allocate(ref root.Regard, regards);
            var kinArray = builder.Allocate(ref root.Kinship, regards);
            for (int i = 0; i < regards; i++)
            {
                regardArray[i] = regard[i];
                kinArray[i] = kinship[i];
            }

            var partArray = builder.Allocate(ref root.Parts, parts.Count);
            var segArray = builder.Allocate(ref root.Segments, segmentCount);
            var poseArray = builder.Allocate(ref root.Poses, segmentCount * CreatureBlob.PoseCount);

            // Coats come from the variant (or the species itself): its listed coats, or just Base and Accent.
            var coats = new List<CreatureCoat>(variant.Coats);
            if (coats.Count == 0)
                coats.Add(new CreatureCoat { Base = variant.Base, Accent = variant.Accent, AccentGlow = variant.AccentGlow, Weight = 1f });
            var hair = variant.HairColors.Count > 0 ? variant.HairColors : species.HairColors;
            var hairArray = builder.Allocate(ref root.HairColors, hair.Count);
            for (int i = 0; i < hair.Count; i++)
                hairArray[i] = (Vector4)hair[i].linear;

            var coatArray = builder.Allocate(ref root.Coats, coats.Count);
            for (int i = 0; i < coats.Count; i++)
                coatArray[i] = new CreatureCoatData
                {
                    Base = (Vector4)coats[i].Base.linear,
                    Accent = (Vector4)coats[i].Accent.linear,
                    AccentGlow = coats[i].AccentGlow,
                    Weight = math.max(coats[i].Weight, 0.0001f)
                };

            segArray[0] = new CreatureSegment
            {
                Length = math.max(0f, bodyLength - root.RearRadius - root.FrontRadius),
                RadiusStart = root.RearRadius,
                RadiusEnd = root.FrontRadius,
                Flatten = species.BodyFlatten
            };

            var cursor = 1;
            // (Whether each part is the head or on it: those all take the head's scale, sockets on it too.)
            var onHead = new bool[parts.Count];
            // (A-124: an upright body's shoulders are the top of the part its arms hang from; the build widens it there.)
            var shoulders = new bool[parts.Count];
            if (root.Upright && bb.Shoulders != 1f)
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i].Slot.Limb.Role == LimbRole.Arm && parts[i].Parent >= 0 && parts[parts[i].Parent].Slot.Limb.Role == LimbRole.Feature)
                        shoulders[parts[i].Parent] = true;
            for (int i = 0; i < parts.Count; i++)
            {
                var e = parts[i];
                var slot = e.Slot;
                var limb = slot.Limb;
                var side = e.Mirrored ? -1f : 1f;
                var socket = (float3)slot.Socket;
                onHead[i] = limb.Role == LimbRole.Head || e.Parent >= 0 && onHead[e.Parent];
                if (e.Parent >= 0 && onHead[e.Parent])
                    socket *= headScale;
                var lengthScale = onHead[i] ? headScale : limb.Role == LimbRole.Leg ? legScale
                                : limb.Role == LimbRole.Neck ? neckScale : limb.Role == LimbRole.Tail ? tailScale
                                : limb.Role == LimbRole.Arm ? bb.Limbs : 1f;
                var radiusScale = onHead[i] ? headScale : thickness;
                // (The head's own neck, before its last segment, only thickens: a big head, not a long neck.)
                var neck = limb.Role == LimbRole.Head ? limb.Segments.Count - 1 : 0;
                if (e.Parent < 0)
                {
                    // Body units → metres: across the radius at that point along the body, along the half-length.
                    var radius = math.lerp(root.RearRadius, root.FrontRadius, socket.z * 0.5f + 0.5f);
                    socket = new float3(socket.x * radius, socket.y * radius * species.BodyFlatten, socket.z * bodyLength * 0.5f);
                }
                var dir = (float3)slot.Direction;
                if (root.Upright && limb.Role == LimbRole.Arm && e.Parent >= 0 && parts[e.Parent].Slot.Limb.Role == LimbRole.Feature)
                    FitShoulder(segArray, partArray[e.Parent], limb, slot.Scale * slot.Thickness * radiusScale, slot.Scale * lengthScale,
                                ref socket, ref dir);
                socket.x *= side;
                partArray[i] = new CreaturePart
                {
                    Parent = e.Parent,
                    Socket = socket,
                    Rest = Rotation(dir, side),
                    Phase = math.frac(slot.Phase + (e.Mirrored && slot.Mirror ? slot.MirrorPhase : 0f)),
                    FirstSegment = cursor,
                    SegmentCount = limb.Segments.Count,
                    Role = limb.Role,
                    Spring = limb.Spring,
                    JointPhaseStep = limb.JointPhaseStep,
                    FollowGround = limb.FollowGround,
                    Side = side,
                    StyleGroup = slot.StyleGroup,
                    StyleIndex = StyleIndex(species, e.SlotIndex),
                    StyleCount = StyleIndex(species, species.Parts.Count, slot.StyleGroup)
                };

                var poses = new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };
                for (int s = 0; s < limb.Segments.Count; s++)
                {
                    var seg = limb.Segments[s];
                    segArray[cursor] = new CreatureSegment
                    {
                        Length = seg.Length * slot.Scale * (s < neck ? 1f : lengthScale),
                        RadiusStart = seg.RadiusStart * slot.Scale * slot.Thickness * (s < neck ? thickness : radiusScale),
                        RadiusEnd = seg.RadiusEnd * slot.Scale * slot.Thickness * (s < neck ? thickness : radiusScale)
                                  * (shoulders[i] && s == limb.Segments.Count - 1 ? bb.Shoulders : 1f),
                        Flatten = seg.Flatten,
                        Accent = seg.Accent,
                        Fixed = seg.UseFixedColor ? 1 : 0,
                        FixedColor = (Vector4)seg.FixedColor.linear,
                        Glow = seg.Glow,
                        Hair = seg.Hair ? 1 : 0
                    };
                    for (int k = 0; k < CreatureBlob.PoseCount; k++)
                    {
                        var deg = (float3)poses[k][s];
                        // (M-05: the walk and run keys swung further from, or closer to, the idle pose; then any nudges.)
                        if (swing != 1f && k != 0)
                            deg = (float3)poses[0][s] + (deg - (float3)poses[0][s]) * swing;
                        if (tune != null && tune.Nudge(e.SlotIndex, s, k, out var nudge))
                            deg += (float3)nudge;
                        var a = math.radians(deg);
                        poseArray[cursor * CreatureBlob.PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                    }
                    cursor++;
                }
            }

            var blob = builder.CreateBlobAssetReference<CreatureBlob>(Allocator.Persistent);
            builder.Dispose();
            MeasureGait(ref blob.Value, species.LeglessStride);
            MeasureLegs(ref blob.Value);
            MeasureMouth(ref blob.Value);
            return blob;
        }

        /// <summary>
        /// Each leg's give (Slack): its thigh and shin at full stretch against the span from hip to ankle in the
        /// standing pose, chained as the animator chains them (the rest orientation, then each joint's idle angles).
        /// </summary>
        static void MeasureLegs(ref CreatureBlob b)
        {
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount < 2)
                    continue;
                int s0 = part.FirstSegment, s1 = s0 + 1;
                var rot = math.mul(part.Rest, quaternion.EulerZXY(b.Poses[s0 * CreatureBlob.PoseCount + CreatureBlob.Idle]));
                var knee = math.mul(rot, new float3(0f, 0f, b.Segments[s0].Length));
                rot = math.mul(rot, quaternion.EulerZXY(b.Poses[s1 * CreatureBlob.PoseCount + CreatureBlob.Idle]));
                var ankle = knee + math.mul(rot, new float3(0f, 0f, b.Segments[s1].Length));
                part.Slack = math.max(b.Segments[s0].Length + b.Segments[s1].Length - math.length(ankle), 0f);
                // (On down the rest of the leg, to its foot.)
                var foot = ankle;
                for (int j = 2; j < part.SegmentCount; j++)
                {
                    rot = math.mul(rot, quaternion.EulerZXY(b.Poses[(s0 + j) * CreatureBlob.PoseCount + CreatureBlob.Idle]));
                    foot += math.mul(rot, new float3(0f, 0f, b.Segments[s0 + j].Length));
                }
                part.Stance = foot;
            }
        }

        /// <summary>
        /// The mouth (Mouth): the end of the first head part, its chain followed from the body through any neck in the
        /// idle pose, as the animator chains parts (a child from the end of its parent's last segment).
        /// </summary>
        static void MeasureMouth(ref CreatureBlob b)
        {
            var endPos = new float3[b.Parts.Length];
            var endRot = new quaternion[b.Parts.Length];
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                var parentPos = part.Parent < 0 ? float3.zero : endPos[part.Parent];
                var parentRot = part.Parent < 0 ? quaternion.identity : endRot[part.Parent];
                var pos = parentPos + math.mul(parentRot, part.Socket);
                var rot = math.mul(parentRot, part.Rest);
                for (int j = 0; j < part.SegmentCount; j++)
                {
                    var seg = part.FirstSegment + j;
                    rot = math.mul(rot, quaternion.EulerZXY(b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle]));
                    pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
                }
                endPos[i] = pos;
                endRot[i] = rot;
                if (part.Role == LimbRole.Head && !b.HasMouth)
                {
                    b.Mouth = pos;
                    b.HasMouth = true;
                }
            }
        }

        /// <summary>How many slots before <paramref name="slotIndex"/> share its style group (its option number).</summary>
        static int StyleIndex(CreatureSpecies species, int slotIndex, int group = -1)
        {
            if (group < 0)
                group = species.Parts[slotIndex].StyleGroup;
            var n = 0;
            for (int i = 0; i < slotIndex; i++)
                if (species.Parts[i].Limb != null && species.Parts[i].StyleGroup == group && group > 0)
                    n++;
            return n;
        }

        /// <summary>A-123: how far an upright arm's shoulder joint sits outside its torso's surface, in arm radii (0: the
        /// joint on the surface; 1: the arm just touching it). Part of the shoulder's ball sinks into the torso, so it reads
        /// as one continuous shoulder.</summary>
        const float ShoulderOut = 0.55f;
        /// <summary>A-123: the least room, in arm radii, left between a hanging forearm or hand and the torso; the upper arm
        /// may sink in up to <see cref="UpperArmSink"/> of its radius below the shoulder.</summary>
        const float ForearmRoom = 0.15f, UpperArmSink = 0.3f;
        /// <summary>A-123: the most an arm is splayed out from the body (degrees) to clear a torso that widens below the shoulder.</summary>
        const float MaxSplay = 14f;

        /// <summary>
        /// A-123 (Zach, 11-14): fits an upright arm to its torso from the body's own sizes, so goblins' arms don't float
        /// out from the body, humans' don't barely touch it and ogres' don't sink into it. The socket's distance out from
        /// the torso's axis becomes the torso's radius at the shoulder (its last segment, as built) plus
        /// <see cref="ShoulderOut"/> arm radii; then, where the torso widens below the shoulder (a barrel chest, a child's
        /// round middle), the arm's rest is splayed out (up to <see cref="MaxSplay"/>) just enough that it hangs clear.
        /// The hang is worked out straight down the torso's segments from the socket: near enough for the check, not a pose.
        /// <paramref name="socket"/> is the slot's, before mirroring (x out to the arm's side); <paramref name="dir"/> its
        /// rest in degrees, before mirroring.
        /// </summary>
        static void FitShoulder(BlobBuilderArray<CreatureSegment> segs, CreaturePart torso, LimbType limb, float radiusScale,
                                float lengthScale, ref float3 socket, ref float3 dir)
        {
            var top = segs[torso.FirstSegment + torso.SegmentCount - 1];
            if (top.Length <= 1e-4f || limb.Segments.Count == 0)
                return;
            var armRadius = limb.Segments[0].RadiusStart * radiusScale;
            var flatten = math.max(top.Flatten, 0.05f);
            var along = math.clamp(top.Length + socket.z, 0f, top.Length);
            var below = torso.SegmentCount > 1 ? segs[torso.FirstSegment + torso.SegmentCount - 2] : default;
            var torsoRadius = TorsoRadius(top, below, along);
            var reach = torsoRadius + ShoulderOut * armRadius;
            var upright = socket.y / flatten;
            socket.x = math.sqrt(math.max(reach * reach - upright * upright, 0f));

            // The splay: down the arm, the torso beside it (the segment below the top one once past its start).
            var need = 0f;
            var armLength = 0f;
            for (int s = 0; s < limb.Segments.Count; s++)
                armLength += limb.Segments[s].Length * lengthScale;
            var upperLength = limb.Segments[0].Length * lengthScale;
            const int Steps = 12;
            for (int k = 1; k <= Steps; k++)
            {
                var d = armLength * k / Steps;
                if (d < upperLength * 0.5f)
                    continue;
                var z = along - d;
                if (z < -below.Length)
                    break;
                var beside = TorsoRadius(top, below, z);
                // (The arm's own radius at that point along it.)
                float run = d, r = armRadius;
                for (int s = 0; s < limb.Segments.Count; s++)
                {
                    var len = limb.Segments[s].Length * lengthScale;
                    if (run <= len || s == limb.Segments.Count - 1)
                    {
                        var seg = limb.Segments[s];
                        r = math.lerp(seg.RadiusStart, seg.RadiusEnd, math.saturate(run / math.max(len, 1e-4f))) * radiusScale;
                        break;
                    }
                    run -= len;
                }
                var room = d <= upperLength ? -UpperArmSink * armRadius : ForearmRoom * armRadius;
                var outBy = beside + r + room - socket.x;
                if (outBy > 0f)
                    need = math.max(need, math.degrees(math.atan2(outBy, d)));
            }
            // (Out from the body: for an arm hanging down (pitch about 180), a negative yaw before mirroring.)
            dir.y -= math.min(need, MaxSplay);
        }

        /// <summary>A-123: how far the torso's surface is out from its axis at <paramref name="z"/> metres up its top segment
        /// (negative: down into the one below), the round ends included: a barrel chest's end bulges past its taper.</summary>
        static float TorsoRadius(CreatureSegment top, CreatureSegment below, float z)
        {
            var r = Width(top, z);
            if (below.Length > 1e-4f)
                r = math.max(r, Width(below, below.Length + z));
            return r;
        }

        /// <summary>One tapered capsule's radius at <paramref name="z"/> along it, its round ends reaching past either end.</summary>
        static float Width(CreatureSegment s, float z)
        {
            var r = z >= 0f && z <= s.Length ? math.lerp(s.RadiusStart, s.RadiusEnd, z / math.max(s.Length, 1e-4f)) : 0f;
            r = math.max(r, math.sqrt(math.max(s.RadiusStart * s.RadiusStart - z * z, 0f)));
            var e = s.Length - z;
            return math.max(r, math.sqrt(math.max(s.RadiusEnd * s.RadiusEnd - e * e, 0f)));
        }

        /// <summary>Rest orientation from degrees; the mirror image flips yaw and roll.</summary>
        public static quaternion Rotation(float3 degrees, float side)
        {
            var r = math.radians(degrees);
            return quaternion.EulerZXY(r.x, r.y * side, r.z * side);
        }

        /// <summary>
        /// Works out from the key poses how high the body stands and how far it travels per gait cycle, so the feet
        /// don't slide: in a cycle a planted foot sweeps back from reach to push while the body passes over it, so
        /// the body moves twice that sweep per cycle.
        /// </summary>
        static void MeasureGait(ref CreatureBlob b, Vector2 leglessStride)
        {
            float stand = 0f, walk = 0f, run = 0f;
            var legs = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0)
                    continue;
                stand += -Foot(ref b, ref part, b.Disguised ? CreatureBlob.WalkReach : CreatureBlob.Idle).y;
                walk += math.abs(Foot(ref b, ref part, CreatureBlob.WalkReach).z - Foot(ref b, ref part, CreatureBlob.WalkPush).z) * 2f;
                run += math.abs(Foot(ref b, ref part, CreatureBlob.RunReach).z - Foot(ref b, ref part, CreatureBlob.RunPush).z) * 2f;
                legs++;
            }
            b.StandHeight = legs > 0 ? stand / legs : b.FrontRadius * b.BodyFlatten;
            b.WalkStride = legs > 0 ? math.max(walk / legs, 0.05f) : math.max(leglessStride.x, 0.05f);
            b.RunStride = legs > 0 ? math.max(run / legs, 0.05f) : math.max(leglessStride.y, 0.05f);
            b.Gallops = !b.Upright && legs == 4;
            if (b.Gallops)
                for (int i = 0; i < b.Parts.Length; i++)
                {
                    ref var part = ref b.Parts[i];
                    if (part.Role != LimbRole.Leg || part.Parent >= 0)
                        continue;
                    var hind = part.Socket.z < 0f;
                    var right = part.Side < 0f;
                    part.GallopPhase = b.LeapingRun ? 0f : (hind ? 0f : b.Bounds ? 0.5f : 0.45f) + (b.Bounds ? 0f : right ? 0.1f : 0f);
                }
            var chain = false;
            for (int i = 0; i < b.Parts.Length; i++)
                chain |= b.Parts[i].FollowGround && b.Parts[i].Parent < 0;
            b.Slithers = legs == 0 && chain;
            MeasureLie(ref b);
        }

        /// <summary>
        /// A-118: how high the body lies. Each leg that folds (hanging beneath a four-legged body) is posed fully folded
        /// (CreatureBlob.LieFold, a hoofed hind leg rolled under the belly as it lies) and its lowest point below the
        /// body's centre found: the body's front and rear must lie at least that high, and at least on the belly. The
        /// body lies at the mean of the two, its nose tipped up by the difference over the span between the hips and
        /// shoulders. (Before, it lay on the belly's radius alone: long legs had no room under it and sprawled.)
        /// </summary>
        static void MeasureLie(ref CreatureBlob b)
        {
            var belly = b.FrontRadius * b.BodyFlatten * 0.9f;
            float front = 0f, hind = 0f, frontZ = 0f, hindZ = 0f, sideX = 0f;
            int fronts = 0, hinds = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (b.Upright || part.Role != LimbRole.Leg || part.Parent >= 0 || math.mul(part.Rest, new float3(0f, 0f, 1f)).y >= -0.7f)
                    continue;
                var isHind = part.Socket.z < 0f;
                sideX = math.max(sideX, math.abs(part.Socket.x));
                var pos = part.Socket;
                var rot = part.Rest;
                var low = 0f;
                for (int s = 0; s < part.SegmentCount; s++)
                {
                    var seg = part.FirstSegment + s;
                    var a = b.Poses[seg * CreatureBlob.PoseCount + CreatureBlob.Idle];
                    a.x += CreatureBlob.LieFold(s, part.SegmentCount, isHind, b.Hooves);
                    rot = math.mul(rot, quaternion.EulerZXY(a));
                    var start = pos;
                    pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
                    var end = pos;
                    if (isHind && b.Hooves)
                    {
                        var roll = quaternion.AxisAngle(new float3(0f, 0f, 1f), CreatureBlob.HindRoll(false));
                        start = part.Socket + math.mul(roll, start - part.Socket);
                        end = part.Socket + math.mul(roll, end - part.Socket);
                    }
                    var r = math.max(b.Segments[seg].RadiusStart, b.Segments[seg].RadiusEnd) * b.Segments[seg].Flatten;
                    low = math.max(low, math.max(-start.y, -end.y) + r);
                }
                if (isHind)
                {
                    hind = math.max(hind, low);
                    hindZ += part.Socket.z;
                    hinds++;
                }
                else
                {
                    front = math.max(front, low);
                    frontZ += part.Socket.z;
                    fronts++;
                }
            }
            // (Only a body on four folding legs or more lies on the fold; birds and the rest lie on the belly as before.)
            b.LiesOnFold = fronts >= 2 && hinds >= 2;
            if (!b.LiesOnFold)
            {
                b.LieHeight = math.min(b.StandHeight, belly);
                b.LiePitch = 0f;
                return;
            }
            front = math.min(b.StandHeight, math.max(belly, front));
            hind = math.min(b.StandHeight, math.max(b.RearRadius * b.BodyFlatten * 0.9f, hind));
            frontZ /= fronts;
            hindZ /= hinds;
            var tilt = (front - hind) / math.max(frontZ - hindZ, 0.05f);
            // (The body pitches about its centre, not midway between the hips and shoulders, so the height there is
            // what puts both at their heights; a hoofed body also tips onto one side, dropping its low-side legs.)
            b.LieHeight = math.min(b.StandHeight, front - frontZ * tilt + (b.Hooves ? math.sin(CreatureBlob.LieTip) * sideX : 0f));
            b.LiePitch = math.atan(tilt);
        }

        /// <summary>End of a body-attached part's chain in the body frame, at one key pose.</summary>
        static float3 Foot(ref CreatureBlob b, ref CreaturePart part, int pose)
        {
            var pos = part.Socket;
            var rot = part.Rest;
            for (int s = 0; s < part.SegmentCount; s++)
            {
                var seg = part.FirstSegment + s;
                var a = b.Poses[seg * CreatureBlob.PoseCount + pose];
                rot = math.mul(rot, quaternion.EulerZXY(a));
                pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
            }
            // The last segment's rounded end reaches a little further down.
            return pos + new float3(0f, -b.Segments[part.FirstSegment + part.SegmentCount - 1].RadiusEnd, 0f);
        }
    }
}
