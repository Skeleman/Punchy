using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-101 (#267): one climbing model for every climb in the game (trees, walls, cave routes, ladders, later colossi).
    /// Each climbable vertical section has a Climbability grade, hardest first: 5 a smooth wall, 4 a craggy wall (rough
    /// rock, cracks, handholds), 3 a tree, 2 assisted (vines, webs, trellis, roots, rope), 1 a ladder. Each character has
    /// a ClimbSkill (0-5) and may climb a section when its skill is at least the grade, faster the bigger the margin.
    /// Animals' skill is fixed per species (CreatureSpecies.ClimbSkill); humanoids get one value for now (2: ladders and
    /// assisted), later from stats. Grades are seed-derived (bake records); the skill goes with the body, so it's synced
    /// with the species. Burst-safe statics, shared by the player's movement and the creatures' brain.
    /// </summary>
    public static class Climbing
    {
        /// <summary>The grades (Climbability), easiest first. 0 is "not climbable".</summary>
        public const byte None = 0, Ladder = 1, Assisted = 2, Tree = 3, Craggy = 4, Smooth = 5;
        /// <summary>The top of the scale: a skill this high climbs anything.</summary>
        public const byte Most = Smooth;
        /// <summary>A humanoid's skill until it comes from stats (Zach 11-12): ladders and assisted sections.</summary>
        public const byte HumanoidSkill = Assisted;
        /// <summary>How much faster each grade of margin climbs (of the body's own climb speed).</summary>
        public const float MarginBoost = 0.1f;
        /// <summary>A trunk wider than this (m radius) is a hut's round wall, graded as a smooth wall, not a tree.</summary>
        public const float TreeRadiusMost = 0.7f;

        /// <summary>"-climbskill N" (DebugControls): the player's skill instead of its body's; -1 off. Read on the main
        /// thread when the body is set up (FaunaDirector); movement reads PlayerBody.ClimbSkill.</summary>
        public static int SkillOverride = -1;

        /// <summary>
        /// A species' skill when its asset leaves ClimbSkill at -1, so the species that climbed before A-101 climb the
        /// same: a crawler (lizards, spiders) any surface, a tree climber (Climbing above 0) trees, a body with hands the
        /// humanoid skill, the rest nothing.
        /// </summary>
        public static byte DefaultSkill(bool crawls, float climbing, bool hands) =>
            crawls ? Most : climbing > 0f ? Tree : hands ? HumanoidSkill : None;

        /// <summary>The skill a species' asset sets (-1: DefaultSkill), held to the scale.</summary>
        public static byte SpeciesSkill(int set, bool crawls, float climbing, bool hands) =>
            set < 0 ? DefaultSkill(crawls, climbing, hands) : (byte)math.min(set, Most);

        /// <summary>Whether a body with this skill may climb a section of this grade (an ungraded one never).</summary>
        public static bool CanClimb(int skill, int grade) => grade > None && skill >= grade;

        /// <summary>Whether the body pulls itself up onto ledges in reach (mantles): any climbing skill at all.</summary>
        public static bool Mantles(int skill) => skill >= Ladder;

        /// <summary>How fast (m/s) a body that climbs at baseSpeed goes up a section of this grade: its own speed with no
        /// margin, a little faster for each grade it has to spare.</summary>
        public static float Speed(float baseSpeed, int skill, int grade)
        {
            var speed = math.max(baseSpeed, 0.3f) * (1f + MarginBoost * math.max(skill - grade, 0));
            return math.isfinite(speed) ? speed : 0.3f;
        }

        /// <summary>A climb speed (m/s) for a body: a crawler's quick scurry, else as keen a tree climber as it is.</summary>
        public static float BaseSpeed(int skill, float climbing) =>
            skill >= Most ? 1.4f : math.min(0.6f + 1.6f * math.max(climbing, 0f), 1.6f);

        /// <summary>The grade of a trunk-shaped obstacle: a tree, or a hut's round wall when it's that wide.</summary>
        public static byte TrunkGrade(float radius) => radius > TreeRadiusMost ? Smooth : Tree;

        /// <summary>
        /// The grade of the wall a body is pushed into at point (its feet) facing into: a marked section's (ladders and
        /// routes, from the sections source) when one is in reach, else a smooth wall, the grade of any unmarked wall
        /// until the world lane grades cliffs and caves (W-144, W-150).
        /// </summary>
        public static byte WallGrade<TSections>(in TSections sections, float3 point, float2 into, float reach)
            where TSections : struct, IClimbSections
        {
            var ahead = point + new float3(into.x, 0f, into.y) * reach;
            return sections.GradeNear(ahead, reach, out var grade) ? (byte)math.clamp((int)grade, (int)Ladder, (int)Most) : Smooth;
        }
    }

    /// <summary>
    /// A-101: where the world's graded climbing sections are (ladders, vines, marked cave routes), for the climb check.
    /// Kept small so each source plugs in behind it as it lands on main.
    /// </summary>
    public interface IClimbSections
    {
        /// <summary>Whether a graded section is within radius of point (world), and the easiest grade there.</summary>
        bool GradeNear(float3 point, float radius, out byte grade);
    }

    /// <summary>
    /// A-101 row 0: the sections source until the world lane's marked sections are on main: none, so every wall is
    /// graded smooth (Climbing.WallGrade) and every trunk by its width.
    /// TODO(A-101, W-147): ladders: answer from FloraPlacement.NearLadders(point, radius) (ClimbLadder records, grade 1)
    /// once world/climbs-surface is on main; TODO(A-101, W-144): marked cave routes and their grades.
    /// </summary>
    public struct NoClimbSections : IClimbSections
    {
        public bool GradeNear(float3 point, float radius, out byte grade)
        {
            grade = Climbing.None;
            return false;
        }
    }
}
