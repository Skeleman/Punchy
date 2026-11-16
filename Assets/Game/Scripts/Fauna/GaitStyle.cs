using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>A-124 row 2: the kind of walk a person has.</summary>
    public enum GaitKind : byte { Even, Heavy, Brisk, Ambling, Limp }

    /// <summary>
    /// A-124 row 2 (Zach, 11-14): one person's way of walking, drawn from their Individual (replicated, so host and
    /// client draw the same; a player's is their saved one). Most walk evenly; some heavily, briskly or ambling, and a
    /// few with a slight limp. On top of the stride their own legs give (the blob's, measured from their build), a trait
    /// lengthens or shortens it a little, and the style scales the existing per-person traits (CreatureAnimator's lean,
    /// arm swing, bounce and twist; the brain's pace). Upright adults and children only (no limp for children); animals
    /// keep the plain gait.
    /// </summary>
    /// <remarks>
    /// The stride scales each leg's swing about its rest pose (as a sprint's longer stride does, CreatureBlob.LegAngles),
    /// and the planted-foot maths (CreatureMotion.StepPhase, FootZ) poses the legs the same way, so feet stay planted.
    /// A limp is a shorter swing on one leg: the body passes over that foot quicker, spending less of each cycle on it.
    /// </remarks>
    public struct GaitStyle
    {
        public GaitKind Kind;
        /// <summary>Both legs' swing about rest (1 plain).</summary>
        public float Stride;
        /// <summary>Walking pace (a multiplier on the brain's walk speed; a run gets half of it).</summary>
        public float Pace;
        /// <summary>Multipliers on the animator's per-person bounce, arm swing and torso twist.</summary>
        public float Bob, Arms, Twist;
        /// <summary>Added to the torso's lean (radians, forward).</summary>
        public float Lean;
        /// <summary>The sore leg's side (+1 right, -1 left), 0 for none.</summary>
        public float LimpSide;

        /// <summary>The sore leg's swing against the other's, walking (eased towards even at a run).</summary>
        public const float LimpStride = 0.72f, LimpRunStride = 0.9f;
        /// <summary>How much a limper rises over the good leg, as a share of standing height.</summary>
        public const float LimpRise = 0.03f;
        public const int StrideTrait = 45, Group = 12402;

        public static GaitStyle Plain => new GaitStyle { Stride = 1f, Pace = 1f, Bob = 1f, Arms = 1f, Twist = 1f };

        public bool Limps => LimpSide != 0f;

        public static GaitStyle Of(ref CreatureBlob b, uint individual) => Of(individual, b.Upright, b.Child);

        public static GaitStyle Of(uint individual, bool upright, bool child)
        {
            if (!upright)
                return Plain;
            var g = Plain;
            // (Scrambled: math.hash keeps the low bit and every Individual is odd.)
            var roll = Punchy.Terrain.BiomeSampling.Scramble(individual ^ unchecked((uint)Group * 0x9E3779B9u));
            var draw = roll % 100u;
            g.Kind = draw < 6u && !child ? GaitKind.Limp
                   : draw < 20u ? GaitKind.Heavy
                   : draw < 34u ? GaitKind.Brisk
                   : draw < 48u ? GaitKind.Ambling
                   : GaitKind.Even;
            switch (g.Kind)
            {
                case GaitKind.Heavy:
                    g.Stride = 0.9f; g.Pace = 0.9f; g.Bob = 1.6f; g.Arms = 0.7f; g.Twist = 1.25f; g.Lean = 0.04f;
                    break;
                case GaitKind.Brisk:
                    g.Stride = 0.92f; g.Pace = 1.1f; g.Bob = 0.8f; g.Arms = 1.35f; g.Twist = 0.85f; g.Lean = 0.06f;
                    break;
                case GaitKind.Ambling:
                    g.Stride = 1.1f; g.Pace = 0.84f; g.Bob = 0.9f; g.Arms = 0.85f; g.Twist = 1.5f; g.Lean = -0.04f;
                    break;
                case GaitKind.Limp:
                    g.Stride = 0.95f; g.Pace = 0.88f; g.Bob = 1.1f; g.Arms = 1.1f;
                    g.LimpSide = (roll >> 16 & 1u) == 0u ? -1f : 1f;
                    break;
            }
            g.Stride *= 1f + CreatureBlob.Trait(individual, StrideTrait) * 0.07f;
            return g;
        }

        /// <summary>One leg's swing about its rest pose: the stride, less on the sore leg (and less so at a run).</summary>
        public float LegStride(float side, float run) =>
            LimpSide != 0f && side * LimpSide > 0f ? Stride * math.lerp(LimpStride, LimpRunStride, run) : Stride;

        /// <summary>
        /// How high a limper's body rises over its good leg at this point of the gait (unscaled, a share of standing
        /// height): up while the good foot is planted, down again as the sore one takes the weight.
        /// </summary>
        public float Rise(ref CreatureBlob b, float phase, float sprint)
        {
            for (int i = 0; i < b.Parts.Length; i++)
            {
                ref var part = ref b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0 || part.SegmentCount == 0 || part.Side * LimpSide >= 0f)
                    continue;
                var p = math.frac(phase + b.LegPhase(ref part, sprint));
                return p < 0.5f ? LimpRise * b.StandHeight * math.sin(p * 2f * math.PI) : 0f;
            }
            return 0f;
        }

        /// <summary>A leg's swing for the planted-foot maths and the animator: only root legs of an upright body.</summary>
        public float LegStride(ref CreaturePart part, float run) =>
            part.Role == LimbRole.Leg && part.Parent < 0 ? LegStride(part.Side, run) : 1f;
    }
}
