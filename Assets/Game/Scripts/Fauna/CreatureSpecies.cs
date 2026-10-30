using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A kind of animal, assembled from parts: a body and limb types attached in slots (legs, neck, head, ears,
    /// tail…), with colours, gait and behaviour. Slots can be mirrored to make left/right pairs, and each has a gait
    /// phase so the same leg type walks, trots or hops depending on how the slots are timed.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Creature Species", fileName = "Creature")]
    public class CreatureSpecies : ScriptableObject
    {
        [Tooltip("Reuse another species' whole body, gait and behaviour; this asset then only sets colours (Base, " +
                 "Accent, Coats) and size. For recoloured relatives: arctic fox from red fox, polar bear from bear.")]
        public CreatureSpecies VariantOf;
        [Tooltip("Variants only: size relative to the species they're based on.")]
        [Min(0.05f)] public float VariantScale = 1f;
        [Tooltip("Variants only: the head's size relative to the body it's based on (and all on it: eyes, ears, hair). " +
                 "Children: about 1.6.")]
        [Min(0.1f)] public float VariantHead = 1f;
        [Tooltip("Variants only: how thick the torso and limbs are relative to the body they're based on (children: " +
                 "chunkier, about 1.3).")]
        [Min(0.1f)] public float VariantThickness = 1f;
        [Tooltip("Variants only: leg length relative to the body it's based on (children: shorter, about 0.8).")]
        [Min(0.1f)] public float VariantLegs = 1f;

        [Header("Body")]
        [Tooltip("Length of the torso, nose-end to tail-end of the trunk, in metres.")]
        [Min(0.01f)] public float BodyLength = 0.8f;
        [Min(0.005f)] public float BodyRadius = 0.15f;
        [Tooltip("Rear radius relative to the front (below 1 = slimmer hips).")]
        [Range(0.2f, 2f)] public float BodyTaper = 1f;
        [Tooltip("Height of the torso relative to its width.")]
        [Range(0.2f, 3f)] public float BodyFlatten = 1.1f;
        public Color Base = new Color(0.55f, 0.4f, 0.25f);
        public Color Accent = new Color(0.9f, 0.85f, 0.75f);
        [Tooltip("How much the accent-coloured parts glow when no Coats are listed.")]
        [Range(0f, 1f)] public float AccentGlow;
        [Tooltip("Alternative colourings; each animal picks one (by weight). Empty = everyone uses Base and Accent.")]
        public List<CreatureCoat> Coats = new List<CreatureCoat>();
        [Tooltip("Random size of individuals.")]
        public Vector2 Size = new Vector2(0.9f, 1.1f);
        [Tooltip("Colours an individual's hair and beard (segments marked Hair) are picked from. Empty = the " +
                 "species this is a variant of, or the segments' own colour.")]
        public List<Color> HairColors = new List<Color>();

        [Header("Parts")]
        public List<LimbSlot> Parts = new List<LimbSlot>();

        [Header("Gait")]
        public float WalkSpeed = 1.4f;
        public float RunSpeed = 6f;
        [Tooltip("How far the body dips per step, walking and running (metres).")]
        public Vector2 Bob = new Vector2(0.02f, 0.06f);
        [Tooltip("Dips per gait cycle, walking and running (2 for a walk; 1 for a bound or hop).")]
        public Vector2Int BobsPerCycle = new Vector2Int(2, 2);
        [Tooltip("Rocking of the body nose-up / nose-down per cycle when running, in degrees (bounding, hopping).")]
        public float RunRock = 0f;
        [Tooltip("Stands upright on two legs (humanoids): the body stays level on slopes instead of tilting with " +
                 "the ground, and the legs reach for the ground on their own.")]
        public bool Upright;
        [Tooltip("Distance travelled per gait cycle, walking and running, for animals without legs (snakes). " +
                 "Animals with legs measure it from their leg poses.")]
        public Vector2 LeglessStride = new Vector2(0.6f, 0.9f);

        [Header("Behaviour")]
        [Tooltip("How far it wanders from where it is in one go.")]
        public float WanderRadius = 25f;
        [Tooltip("Seconds it stands around between wanders.")]
        public Vector2 IdleTime = new Vector2(2f, 8f);
        [Tooltip("Runs away from the player within this distance (0 = unafraid).")]
        public float FleeDistance = 12f;
        [Tooltip("Degrees per second.")]
        public float TurnRate = 140f;
        [Tooltip("Drawn, kept about and sent to other players from this far off (metres), for huge animals seen across " +
                 "the land (rocs, sky serpents). 0 = as near as any other animal.")]
        [Min(0f)] public float SeenFrom;
        [Tooltip("Number that appear together.")]
        public Vector2Int GroupSize = new Vector2Int(1, 1);
        [Tooltip("Only appears close to rivers, lakes and the sea (frogs).")]
        public bool NearWater;
        [Tooltip("Where it lives: on land, swimming under the water (fish), or floating on it (ducks).")]
        public Habitat Habitat = Habitat.Land;
        [Tooltip("Swimmers: how far below the surface they like to swim, in metres (shallower where the water is).")]
        [Min(0.1f)] public float SwimDepth = 0.6f;
        [Tooltip("Swimmers: leaps out of the water per minute.")]
        [Min(0f)] public float Leaps;

        [Header("Activities (each species' own, variants included)")]
        [Tooltip("When it's awake: it sleeps through the rest of the day. Always = naps now and then.")]
        public ActiveTime Active = ActiveTime.Day;
        [Tooltip("How much of its time it spends head-down feeding (grazing, pecking, foraging).")]
        [Range(0f, 1f)] public float Grazing;
        [Tooltip("How often it walks to nearby water to drink.")]
        [Range(0f, 1f)] public float Thirst = 0.3f;
        [Tooltip("How keen it is to play chase with others of its kind.")]
        [Range(0f, 1f)] public float Playfulness;
        [Tooltip("A child (folk): out by day with a stick to poke at things or a stone to throw, between games of chase.")]
        public bool Child;
        [Tooltip("How often it lies down to rest.")]
        [Range(0f, 1f)] public float Laziness = 0.25f;
        [Tooltip("Animals it chases (any variant of a listed species counts).")]
        public List<CreatureSpecies> Hunts = new List<CreatureSpecies>();
        [Tooltip("How keen it is to hunt when prey is about.")]
        [Range(0f, 1f)] public float Hunger = 0.3f;
        [Tooltip("Seconds it keeps up a chase before giving up.")]
        [Min(1f)] public float ChaseTime = 10f;

        [Header("Habits")]
        [Tooltip("Little things it does while standing about (pawing the ground, grooming, scratching, sitting…).")]
        public IdleActions Idles = IdleActions.LookAround;
        [Tooltip("How often it does them.")]
        [Range(0f, 1f)] public float Fidgets = 0.5f;
        [Tooltip("How keen it is to climb trees and sit in them (monkeys, squirrels).")]
        [Range(0f, 1f)] public float Climbing;
        [Tooltip("Builds a web beside a tree (or low in the grass) and waits in it.")]
        public bool SpinsWebs;
        [Tooltip("Radius of its web, in metres.")]
        [Min(0.1f)] public float WebSize = 0.6f;
        [Tooltip("Digs a burrow: bolts down it when startled, sleeps in it at night, pops up to look about.")]
        public bool Burrows;
        [Tooltip("Lives inside caves: appears on cave floors when a player comes near a cave, and stays in.")]
        public bool CaveDweller;
        [Tooltip("Crawls on any surface (lizards, spiders): steep rock faces outside, walls and ceilings in caves, its body " +
                 "turned to lie on the surface.")]
        public bool Crawls;
        [Tooltip("Cold-blooded (reptiles, amphibians, insects, spiders, fish): no breath shows in the cold. Variants take " +
                 "their body's.")]
        public bool ColdBlooded;
        [Tooltip("A part it plays for the director: Folk is the everyday person (the default player body, the measure of " +
                 "sizes and paces, and the strangers on errands: travellers, honey thieves); Hermit lives at hermit spots; " +
                 "Messenger carries letters between towns. The first species with a role takes it.")]
        public DirectorRole Role;
        [Tooltip("Walks on hooves (horses, deer, goats, cattle): a clop on hard ground. Others' feet are padded or shod. " +
                 "Variants take their body's.")]
        public bool Hooves;
        [Tooltip("What a player in this body can climb: trees (monkeys, cats, bears, squirrels) or any surface (lizards, " +
                 "spiders). Left at None it follows Crawls (any surface) and Climbing (trees). Variants take their body's.")]
        public ClimbAbility ClimbsAs;
        [Tooltip("Tunnels through the ground (sand and snow worms): travels deep under it, shown only by the ripple it " +
                 "drives through the surface, and now and then bursts out in a great arc over flat ground and dives back.")]
        public bool Tunnels;
        [Tooltip("Sounds of its kind beyond its calls: a cat purrs when held and content, a dog pants after a run. Variants " +
                 "take their body's unless they set their own.")]
        public VoiceFamily Voice;
        [Tooltip("How likely a surface animal is to be found in a cave (bears, wolves, foxes): 0 never. A cave near where " +
                 "it lives may have one or two in its chambers near the mouth, and under rock it moves as cave animals do.")]
        [Range(0f, 1f)] public float CaveAffinity;
        [Tooltip("Sleeps hanging upside down from a cave ceiling (or under a branch, with no cave near): bats.")]
        public bool Roosts;
        [Tooltip("Comes over to people, sits by them wagging its tail and follows them about (within its home range): dogs.")]
        public bool Friendly;
        [Tooltip("Lives in lava (with habitat Swims): keeps to lava, never water, and comes up to bask at its surface.")]
        public bool LavaDweller;
        [Tooltip("A ghost: drifts above the ground (and water), half seen and faintly glowing, only at night; drifts closer " +
                 "to watch a person, and is gone when they come near, to appear again further off.")]
        public bool Haunts;
        [Tooltip("A land animal that swims across rivers and lakes when its way lies over water (dogs, deer, bears, horses), " +
                 "paddling with its back at the surface.")]
        public bool CrossesWater;
        [Tooltip("Keeps a nest (in a tree, on a treetop or on the ground) and goes back to it to sleep.")]
        public NestSite Nests;
        [Tooltip("A cave flier drawn to light (moths): spawns at a cave's glowing plants and flits round one of them, never " +
                 "landing or fleeing.")]
        public bool DrawnToLight;
        [Tooltip("A beast town guards drive off (wolves, bears, big cats): come into a town, the guards on shift run at it " +
                 "with spears levelled and it flees out of the place.")]
        public bool DrivenOff;

        [Header("Town guards (folk)")]
        [Tooltip("How this folk's town guards are kitted out, so a guard is known at a glance across the town: the shape " +
                 "of helmet, and the weapon carried by day (a torch by night). Only guards wear it.")]
        public GuardKit GuardKit;
        [Tooltip("Their armour: a breastplate and shoulder pads in metal or leather.")]
        public Color GuardArmour = new Color(0.55f, 0.57f, 0.6f);
        [Tooltip("Their sash, down the front: the town's colour (as on its hall's banners), so its guards read as one.")]
        public Color GuardSash = new Color(0.62f, 0.12f, 0.1f);
        [Tooltip("Rooted to the spot where it grew (a snapping flower): never walks or runs, and snaps at anything that comes " +
                 "within its Flee Distance (the player or another animal) instead of running from it.")]
        public bool Rooted;
        [Tooltip("Passes for a plant (a scuttling bush): mostly sits still with its legs tucked under it, not looking about " +
                 "or fidgeting, until something comes within its Flee Distance; then it runs, to settle somewhere else.")]
        public bool Disguised;

        [Header("Flight (habitat Flies)")]
        [Tooltip("Height above the ground it cruises at, in metres.")]
        public Vector2 FlightHeight = new Vector2(8f, 18f);
        [Tooltip("Airspeed, metres per second.")]
        [Min(0.5f)] public float FlySpeed = 7f;
        [Tooltip("A player's cruising airspeed in this body (m/s), from the real bird's: a pigeon about 17, a crow 14, an " +
                 "owl 10. 0: twice FlySpeed. Variants take their body's unless they set their own.")]
        [Min(0f)] public float PlayerFlySpeed;
        [Tooltip("Wingbeats per second.")]
        [Min(0.1f)] public float FlapRate = 6f;
        [Tooltip("How much it glides between bursts of flapping (soaring birds of prey).")]
        [Range(0f, 1f)] public float Glide;
        [Tooltip("How often it lands in a tree rather than on the ground.")]
        [Range(0f, 1f)] public float Perching = 0.6f;
        [Tooltip("Soars: most flights are long spells circling high over one place on still wings (rocs, vultures), " +
                 "before it moves on or comes down.")]
        public bool Soars;
        [Tooltip("A swimmer that takes to the air (sky serpents): lives deep in big lakes and the sea, now and then bursts " +
                 "out of the water in a spray to soar for minutes (by the Flight settings), then dives back in. Found only " +
                 "over water at least 6 m deep, at most one near each player, its Density the chance a look over deep " +
                 "water finds one.")]
        public bool Breaches;
        [Tooltip("Goes after wild honey (bears): now and then by day, rarely, walks to a hive near it, rears up at the comb " +
                 "with the bees mobbing it, then makes off well away from it.")]
        public bool RaidsHives;

        [Header("Sound")]
        [Tooltip("Its calls (CreatureAudioSystem plays them near the listener, each individual on its own clock): a hen's " +
                 "cluck, a duck's quack, a dog's bark. A variant with none uses the species it's based on.")]
        public List<CreatureCall> Calls = new List<CreatureCall>();

        /// <summary>The species whose body, gait and behaviour this one uses (itself unless it's a variant).</summary>
        public CreatureSpecies Body
        {
            get
            {
                var s = this;
                for (int i = 0; i < 8 && s.VariantOf != null && s.VariantOf != s; i++)
                    s = s.VariantOf;
                return s;
            }
        }
    }

    public enum Habitat : byte
    {
        Land,
        [Tooltip("Swims under the surface; keeps to water deep enough to swim in.")] Swims,
        [Tooltip("Floats on the surface (waterfowl).")] Floats,
        [Tooltip("Flies, landing on the ground or perching in trees (songbirds, crows, birds of prey).")] Flies
    }

    /// <summary>Where a species keeps its nest.</summary>
    public enum NestSite : byte
    {
        None,
        [Tooltip("A cup of twigs against the trunk, low in the crown (songbirds, crows).")] Branch,
        [Tooltip("A big platform of sticks on top of a tree (ospreys, eagles).")] Treetop,
        [Tooltip("A scrape lined with grass, on flat ground (quail, chickens, ducks).")] Ground,
        [Tooltip("A mud cup on a rock wall, just inside a cave mouth (swallows): the cave's Nest anchor, shared by the colony.")] Ledge
    }

    /// <summary>Little things an animal does while standing about (which of them a species does).</summary>
    [Flags]
    public enum IdleActions : ushort
    {
        None = 0,
        [Tooltip("Scrapes at the ground with a front hoof.")] PawGround = 1 << 0,
        [Tooltip("Turns to lick its flank or shoulder.")] Groom = 1 << 1,
        [Tooltip("Sits back and scratches behind an ear with a hind foot.")] Scratch = 1 << 2,
        [Tooltip("Shakes itself off, head to tail.")] Shake = 1 << 3,
        [Tooltip("Stretches: chest down and front legs out, then the back legs.")] Stretch = 1 << 4,
        [Tooltip("Noses at the ground.")] Sniff = 1 << 5,
        [Tooltip("Sits on its haunches for a while.")] Sit = 1 << 6,
        [Tooltip("Sits up tall to look about (rabbits, prairie dogs, marmots).")] SitUp = 1 << 7,
        [Tooltip("Flicks its tail back and forth.")] TailFlick = 1 << 8,
        [Tooltip("Preens: turns its head back to its feathers.")] Preen = 1 << 9,
        [Tooltip("Bobs its head and body up and down (lizard push-ups).")] HeadBob = 1 << 10,
        [Tooltip("Lifts its head and looks around.")] LookAround = 1 << 11,
        [Tooltip("Sits back and howls at night; others of its kind nearby join in.")] Howl = 1 << 12
    }

    /// <summary>
    /// The idle action an animal is doing now (one of IdleActions, by number), or a pose a player's body takes for an
    /// interaction (Pet, Carry, Throw: not idle actions any species picks for itself).
    /// </summary>
    /// <summary>What a body can climb (CreatureSpecies.ClimbsAs).</summary>
    public enum ClimbAbility : byte { None, Trees, AnySurface }

    /// <summary>A part a species plays for FaunaDirector (CreatureSpecies.Role).</summary>
    public enum DirectorRole : byte { None, Folk, Hermit, Messenger }

    /// <summary>Sounds a species makes beyond its calls (CreatureSpecies.Voice; CreatureAudioSystem).</summary>
    public enum VoiceFamily : byte { None, Cat, Dog }

    public enum IdleAction : byte
    {
        None, PawGround, Groom, Scratch, Shake, Stretch, Sniff, Sit, SitUp, TailFlick, Preen, HeadBob, LookAround, Howl,
        Pet, Carry, Throw,
        // Emotes: a player's (InteractionInputSystem), and folk answering one.
        Wave, Bow, Cheer, Dance,
        // A child poking at the ground with a stick (not an idle action any species picks).
        Poke,
        // A guard drawing its weapon from its sheath (on its back or at its hip), or putting it away.
        Draw, Sheathe,
        // A player climbing up onto a ledge: hands on the rim, pulling up, a knee coming up over.
        Mantle,
        // A rooted creature striking at something within reach (not an idle action any species picks).
        Snap,
        // A player sitting down or lying back where they are, or on a seat (postures held until they move).
        SitDown, LieBack
    }

    public enum ActiveTime : byte
    {
        [Tooltip("Sleeps at night.")] Day,
        [Tooltip("Sleeps by day.")] Night,
        [Tooltip("Awake at all hours, taking naps.")] Always
    }

    /// <summary>When a call is made.</summary>
    public enum CallTime : byte { Any, Day, Night, Dawn }

    /// <summary>One of a species' calls: which sound, how often (seconds between, by individual), how high, how loud and
    /// how far it carries, and at what time of day.</summary>
    [Serializable]
    public class CreatureCall
    {
        public Punchy.Audio.SoundId Sound;
        [Tooltip("Seconds between calls (each individual somewhere in the range).")]
        public Vector2 Interval = new Vector2(8f, 20f);
        [Tooltip("Pitch multiplier range (each individual somewhere in it; smaller bodies higher).")]
        public Vector2 Pitch = new Vector2(0.95f, 1.05f);
        [Range(0f, 1f)] public float Volume = 0.6f;
        [Tooltip("How far it's heard (m).")]
        [Min(1f)] public float Reach = 30f;
        public CallTime When = CallTime.Any;
    }

    [Serializable]
    public class CreatureCoat
    {
        public string Name = "Coat";
        public Color Base = new Color(0.55f, 0.4f, 0.25f);
        public Color Accent = new Color(0.9f, 0.85f, 0.75f);
        [Tooltip("How much the accent-coloured parts glow (embers, toxic markings).")]
        [Range(0f, 1f)] public float AccentGlow;
        [Min(0f)] public float Weight = 1f;
    }

    [Serializable]
    public class LimbSlot
    {
        public LimbType Limb;
        [Tooltip("-1 = attach to the body; otherwise the index of an earlier part to attach to the end of.")]
        public int Parent = -1;
        [Tooltip("On the body: position in body units (x, y across the radius; z from -1 rear to 1 front).\n" +
                 "On a part: offset in metres from the end of its last segment, in that segment's frame.")]
        public Vector3 Socket;
        [Tooltip("Rest orientation in degrees (x pitch, y yaw, z roll). A part points forward by default: (90, 0, 0) " +
                 "points down, (0, 180, 0) backwards, (0, 90, 0) out to the right.")]
        public Vector3 Direction;
        [Tooltip("Also add a mirror image on the other side (left/right pairs).")]
        public bool Mirror;
        [Tooltip("Where in the gait cycle this part is (0..1).")]
        [Range(0f, 1f)] public float Phase;
        [Tooltip("Phase of the mirror image relative to this one (0.5 = alternating; 0 = together, as in a hop).")]
        [Range(0f, 1f)] public float MirrorPhase = 0.5f;
        [Min(0.01f)] public float Scale = 1f;
        [Tooltip("Multiplies the part's radii on top of Scale: the same leg slender on a deer, stocky on a bison.")]
        [Min(0.05f)] public float Thickness = 1f;
        [Tooltip("Style option group (hairstyles, beards): each individual shows just one of the slots in a group, " +
                 "picked at random (list a slot twice to make it more common). 0 = always shown.")]
        [Min(0)] public int StyleGroup;
    }

    /// <summary>A species living in a biome, and how many.</summary>
    [Serializable]
    public class BiomeFauna
    {
        public CreatureSpecies Species;
        [Tooltip("Animals per hectare (10 000 m²).")]
        [Min(0f)] public float Density = 0.5f;
    }
}
