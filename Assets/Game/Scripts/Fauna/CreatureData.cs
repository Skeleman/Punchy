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
        public float2 Bob, BobsPerCycle;
        public float RunRock;
        /// <summary>Keeps the body level rather than tilting with the ground (two-legged walkers).</summary>
        public bool Upright;
        public Habitat Habitat;
        public float SwimDepth, Leaps;
        public float WanderRadius, FleeDistance, TurnRate;
        public float2 IdleTime, Size;
        public ActiveTime Active;
        public float Grazing, Thirst, Playfulness, Laziness, Hunger, ChaseTime;
        /// <summary>Species (FaunaDirector indices) it hunts.</summary>
        public BlobArray<int> Prey;
        /// <summary>Whether anything hunts it (so it needs to watch out for predators).</summary>
        public bool Hunted;
        public IdleActions Idles;
        public float Fidgets, Climbing;
        public bool SpinsWebs, Burrows, CaveDweller, Roosts, Friendly, CrossesWater, Haunts, LavaDweller, DrawnToLight, DrivenOff;
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
        /// <summary>Cold-blooded (CreatureSpecies.ColdBlooded): its breath doesn't show in the cold.</summary>
        public bool ColdBlooded;
        /// <summary>No legs, and a chain lying along the ground off the body (a snake's): it moves along a serpentine wave.</summary>
        public bool Slithers;
        /// <summary>Walks on hooves (CreatureSpecies.Hooves): a clop on hard ground.</summary>
        public bool Hooves;
        /// <summary>What a player in this body can climb (CreatureSpecies.ClimbsAs, or from Crawls and Climbing).</summary>
        public ClimbAbility ClimbsAs;
        /// <summary>Its answer to someone emoting near it (CreatureSpecies.EmoteReaction, Default worked out).</summary>
        public EmoteReaction Reaction;
        /// <summary>Drawn and kept about from this far off; 0 = as any other (see CreatureSpecies.SeenFrom).</summary>
        public float SeenFrom;
        /// <summary>Never moves, and snaps at what comes near (see CreatureSpecies.Rooted).</summary>
        public bool Rooted;
        /// <summary>Sits still as a plant until startled (see CreatureSpecies.Disguised).</summary>
        public bool Disguised;

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
        public static float Trait(uint individual, int trait) =>
            math.hash(new uint2(individual, (uint)trait * 2654435761u + 17u)) / (float)uint.MaxValue * 2f - 1f;

        /// <summary>Which option of a style group an individual shows.</summary>
        public static int Style(uint individual, int group, int count) =>
            (int)(math.hash(new uint2(individual, (uint)group * 40503u + 101u)) % (uint)math.max(count, 1));

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
        Sit
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
        Honey
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
        /// <summary>
        /// What it's looking at, for a glance (Look/LookPitch turn the head there): a point (LookKind 0), a creature it
        /// follows (1: LookEntity), or the player (2); for LookFor more seconds, then none for LookCool. Set by the
        /// brain (CreatureBrainSystem.Glance), readable by any behaviour that wants to know where it's looking.
        /// </summary>
        public float3 LookAt;
        public Entity LookEntity;
        public float LookFor, LookCool;
        public byte LookKind;
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

        /// <summary>An idle action under way and how far through it is (0..1), and how long it lasts.</summary>
        public IdleAction Action;
        public float ActionTime, ActionLength;

        /// <summary>A fixed point it's using: the foot of the tree it climbs, its web's centre, its burrow or nest.</summary>
        public float3 Anchor;
        /// <summary>The tree's trunk radius, or the web's, burrow's or nest's radius (0 = no anchor yet, -1 = none to be
        /// had here); the web's facing and side.</summary>
        public float AnchorSize, AnchorYaw;
        public byte AnchorSide;
        /// <summary>Where a flight or a climb ends up, height-wise (absolute).</summary>
        public float TargetHeight;
        /// <summary>Caught (by a diving bird of prey): FaunaDirector removes it.</summary>
        public bool Gone;
        /// <summary>The cave it roosts in (-1 = none), and the passage node its roost is at.</summary>
        public short LairCave, LairNode;
        /// <summary>Who is petting or carrying it (see Interactions.HolderId; 0 = nobody).</summary>
        public int Holder;
        /// <summary>What it has in hand (folk: a lantern at night). Replicated; each machine draws it.</summary>
        public HeldItem Held;
        /// <summary>A guard: which leg of its round it's on; and how long before it goes after another beast (s).
        /// Simulation only (not sent).</summary>
        public byte RouteLeg;
        public float ChaseCooldown;
        /// <summary>A guard with its weapon drawn: how long since anything needed it (it's put away after a while).</summary>
        public float WeaponCalm;

        // Needs, 0 (satisfied) to 1 (pressing): they rise with time by the species' tastes and are eased by the
        // activity that meets them; what it decides to do next is weighted by them. Simulation only (not sent).
        public float Hunger, Thirst, Tired, Lonely;
        /// <summary>Seconds left before a startled animal will settle again (a bat waved off its roost). Simulation only.</summary>
        public float Unsettled;
    }

    /// <summary>One segment as the creature shader draws it (a tapered capsule). Must match Creature.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SegmentInstance
    {
        public float3 Position;
        public float Length;
        public quaternion Rotation;
        public float RadiusStart, RadiusEnd, Flatten, Pad;
        public float4 Color;
        /// <summary>x: what it's made of (a SegmentMaterial, as a float), shaded to match; yzw spare.</summary>
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

        /// <summary>
        /// Builds a species. A variant takes its body, parts, gait and behaviour from the species it is a variant
        /// of, and only its own colours and size.
        /// </summary>
        /// <param name="prey">Indices of the species it hunts.</param>
        /// <param name="hunted">Whether any species hunts it.</param>
        public static BlobAssetReference<CreatureBlob> Build(CreatureSpecies variant, IReadOnlyList<int> prey = null, bool hunted = false)
        {
            var species = variant.Body;
            // Expand slots: mirrored slots become left and right copies; children follow every copy of their parent.
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

            var segmentCount = 1;
            foreach (var p in parts)
                segmentCount += p.Slot.Limb.Segments.Count;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CreatureBlob>();
            root.BodyLength = species.BodyLength;
            // A variant's own proportions (a child's big head, chunky limbs and short legs) on the body it's based on.
            var own = variant != species;
            var headScale = own ? variant.VariantHead : 1f;
            var thickness = own ? variant.VariantThickness : 1f;
            var legScale = own ? variant.VariantLegs : 1f;
            root.FrontRadius = species.BodyRadius * thickness;
            root.RearRadius = species.BodyRadius * species.BodyTaper * thickness;
            root.BodyFlatten = species.BodyFlatten;
            root.WalkSpeed = species.WalkSpeed;
            root.RunSpeed = math.max(species.RunSpeed, species.WalkSpeed * 1.3f);
            root.Bob = new float2(species.Bob.x, species.Bob.y);
            root.BobsPerCycle = new float2(species.BobsPerCycle.x, species.BobsPerCycle.y);
            root.RunRock = math.radians(species.RunRock);
            root.Upright = species.Upright;
            root.ColdBlooded = species.ColdBlooded;
            root.Hooves = species.Hooves;
            root.ClimbsAs = species.ClimbsAs != ClimbAbility.None ? species.ClimbsAs
                          : species.Crawls ? ClimbAbility.AnySurface : species.Climbing > 0f ? ClimbAbility.Trees : ClimbAbility.None;
            root.Reaction = species.EmoteReaction != EmoteReaction.Default ? species.EmoteReaction
                          : species.Friendly ? EmoteReaction.Wags : species.FleeDistance > 0f ? EmoteReaction.Flees : EmoteReaction.Ignores;
            root.Habitat = species.Habitat;
            root.SwimDepth = species.SwimDepth;
            root.Leaps = species.Leaps;
            root.WanderRadius = species.WanderRadius;
            root.FleeDistance = species.FleeDistance;
            root.TurnRate = math.radians(species.TurnRate);
            root.IdleTime = new float2(species.IdleTime.x, species.IdleTime.y);
            var sizeScale = variant != species ? variant.VariantScale : 1f;
            root.Size = new float2(species.Size.x, species.Size.y) * sizeScale;
            root.Active = variant.Active;
            root.Grazing = variant.Grazing;
            root.Thirst = variant.Thirst;
            root.Playfulness = variant.Playfulness;
            root.Laziness = variant.Laziness;
            root.Hunger = variant.Hunger;
            root.ChaseTime = variant.ChaseTime;
            root.Hunted = hunted;
            root.Idles = species.Idles;
            root.Fidgets = species.Fidgets;
            root.Climbing = species.Climbing;
            root.SpinsWebs = species.SpinsWebs;
            root.Burrows = species.Burrows;
            root.CaveDweller = species.CaveDweller;
            root.Roosts = species.Roosts;
            root.Friendly = species.Friendly;
            root.CrossesWater = species.CrossesWater && species.Habitat == Habitat.Land;
            root.Haunts = species.Haunts;
            root.LavaDweller = species.LavaDweller;
            root.CaveAffinity = variant.CaveAffinity;
            root.Crawls = species.Crawls;
            root.DrawnToLight = species.DrawnToLight;
            root.DrivenOff = species.DrivenOff;
            root.GuardKit = species.GuardKit;
            // (Alpha 0: a segment colour's alpha is how much it glows, and armour doesn't.)
            root.GuardArmour = new float4(((Vector4)species.GuardArmour.linear).x, ((Vector4)species.GuardArmour.linear).y, ((Vector4)species.GuardArmour.linear).z, 0f);
            root.GuardSash = new float4(((Vector4)species.GuardSash.linear).x, ((Vector4)species.GuardSash.linear).y, ((Vector4)species.GuardSash.linear).z, 0f);
            root.Soars = species.Soars;
            root.Breaches = species.Breaches && species.Habitat == Habitat.Swims;
            root.RaidsHives = species.RaidsHives;
            root.Hands = species.Parts.Exists(p => p != null && p.Limb != null && p.Limb.Role == LimbRole.Arm);
            root.Child = variant.Child;
            root.SeenFrom = species.SeenFrom * sizeScale;
            root.Rooted = species.Rooted;
            root.Disguised = species.Disguised;
            root.Nests = species.Nests;
            root.WebSize = species.WebSize * sizeScale;
            root.FlightHeight = new float2(species.FlightHeight.x, math.max(species.FlightHeight.x, species.FlightHeight.y));
            root.FlySpeed = species.FlySpeed;
            root.FlapRate = species.FlapRate;
            root.Glide = species.Glide;
            root.Perching = species.Perching;
            var preyArray = builder.Allocate(ref root.Prey, prey?.Count ?? 0);
            for (int i = 0; i < preyArray.Length; i++)
                preyArray[i] = prey[i];

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
                Length = math.max(0f, species.BodyLength - root.RearRadius - root.FrontRadius),
                RadiusStart = root.RearRadius,
                RadiusEnd = root.FrontRadius,
                Flatten = species.BodyFlatten
            };

            var cursor = 1;
            // (Whether each part is the head or on it: those all take the head's scale, sockets on it too.)
            var onHead = new bool[parts.Count];
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
                var lengthScale = onHead[i] ? headScale : limb.Role == LimbRole.Leg ? legScale : 1f;
                var radiusScale = onHead[i] ? headScale : thickness;
                // (The head's own neck, before its last segment, only thickens: a big head, not a long neck.)
                var neck = limb.Role == LimbRole.Head ? limb.Segments.Count - 1 : 0;
                if (e.Parent < 0)
                {
                    // Body units → metres: across the radius at that point along the body, along the half-length.
                    var radius = math.lerp(root.RearRadius, root.FrontRadius, socket.z * 0.5f + 0.5f);
                    socket = new float3(socket.x * radius, socket.y * radius * species.BodyFlatten, socket.z * species.BodyLength * 0.5f);
                }
                socket.x *= side;
                var dir = (float3)slot.Direction;
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
                        RadiusEnd = seg.RadiusEnd * slot.Scale * slot.Thickness * (s < neck ? thickness : radiusScale),
                        Flatten = seg.Flatten,
                        Accent = seg.Accent,
                        Fixed = seg.UseFixedColor ? 1 : 0,
                        FixedColor = (Vector4)seg.FixedColor.linear,
                        Glow = seg.Glow,
                        Hair = seg.Hair ? 1 : 0
                    };
                    for (int k = 0; k < CreatureBlob.PoseCount; k++)
                    {
                        var a = math.radians((float3)poses[k][s]);
                        poseArray[cursor * CreatureBlob.PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                    }
                    cursor++;
                }
            }

            var blob = builder.CreateBlobAssetReference<CreatureBlob>(Allocator.Persistent);
            builder.Dispose();
            MeasureGait(ref blob.Value, species.LeglessStride);
            MeasureLegs(ref blob.Value);
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
                stand += -Foot(ref b, ref part, CreatureBlob.Idle).y;
                walk += math.abs(Foot(ref b, ref part, CreatureBlob.WalkReach).z - Foot(ref b, ref part, CreatureBlob.WalkPush).z) * 2f;
                run += math.abs(Foot(ref b, ref part, CreatureBlob.RunReach).z - Foot(ref b, ref part, CreatureBlob.RunPush).z) * 2f;
                legs++;
            }
            b.StandHeight = legs > 0 ? stand / legs : b.FrontRadius * b.BodyFlatten;
            b.WalkStride = legs > 0 ? math.max(walk / legs, 0.05f) : math.max(leglessStride.x, 0.05f);
            b.RunStride = legs > 0 ? math.max(run / legs, 0.05f) : math.max(leglessStride.y, 0.05f);
            b.Gallops = !b.Upright && legs == 4;
            var chain = false;
            for (int i = 0; i < b.Parts.Length; i++)
                chain |= b.Parts[i].FollowGround && b.Parts[i].Parent < 0;
            b.Slithers = legs == 0 && chain;
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
