using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// A-102 (#37) breath: one meter for every body, creatures and players alike, worked the way stamina usually is.
    /// It is kept as how much has been used (0 full, 1 empty) so a new body starts full. It is used up while the head is
    /// under water, over the species' hold time (CreatureBlob.HoldBreath), and comes back quickly in air. Emptied, the
    /// body "gasps": it is pushed up until its head is out and can't go down again until it has caught some breath back
    /// (CatchBreath). Water breathers (fish, water and sea folk: CreatureBlob.BreathesWater) never use it. Creatures'
    /// breath is simulated by the brain (CreatureMind, host only); the player's is local player state (PlayerMotion),
    /// sent to the others with the avatar (NetPlayerLook.BreathUsed). Burst-safe statics, shared by both.
    /// </summary>
    public static class Breath
    {
        /// <summary>Hold times (s) when a species doesn't set one: folk; animals that swim across water (otters, dogs);
        /// waterfowl; fliers; other land animals.</summary>
        public const float FolkHold = 30f, SwimmerHold = 20f, FloaterHold = 12f, FlierHold = 6f, AnimalHold = 8f;
        /// <summary>Getting it all back takes this share of the hold time, but at least MinRefill seconds.</summary>
        public const float RefillShare = 0.2f, MinRefill = 2f;
        /// <summary>Having emptied it, how much must come back (of 1) before the body may go down again.</summary>
        public const float CatchBreath = 0.25f;
        /// <summary>How fast a gasping body is pushed up (m/s), and how fast one not yet full may sink back (m/s).</summary>
        public const float RiseSpeed = 1.5f, SinkSpeed = 1f;
        /// <summary>How far out of the water a surfaced head is held (m, times the body's scale).</summary>
        public const float HeadOut = 0.1f;

        /// <summary>The hold time (s) a species gets when its asset leaves HoldBreath at 0.</summary>
        public static float DefaultHold(bool folk, bool crossesWater, Habitat habitat)
        {
            if (folk)
                return FolkHold;
            if (crossesWater)
                return SwimmerHold;
            return habitat == Habitat.Floats ? FloaterHold : habitat == Habitat.Flies ? FlierHold : AnimalHold;
        }

        /// <summary>
        /// One step of the meter: used (0 full .. 1 empty) after dt seconds with the head under water or not, for a body
        /// that holds its breath for hold seconds. Sets gasping when it empties and clears it once CatchBreath is back.
        /// </summary>
        public static float Use(float used, ref bool gasping, bool under, float hold, float dt)
        {
            hold = math.max(hold, 0.5f);
            dt = math.max(dt, 0f);
            used += under ? dt / hold : -dt / math.max(MinRefill, hold * RefillShare);
            if (!math.isfinite(used))
                used = 0f;
            used = math.saturate(used);
            if (used >= 1f)
                gasping = true;
            else if (used <= 1f - CatchBreath)
                gasping = false;
            return used;
        }

        /// <summary>How high a creature's head is (world y): folk swimming hold theirs just out of the water; otherwise
        /// about where its eyes are, as the folk's glances take it (CreatureBrainSystem HeadHeight).</summary>
        public static float Head(in CreatureState s, ref CreatureBlob b)
        {
            var scale = math.max(s.Scale, 0.1f);
            if (b.Upright && s.Mode == CreatureMode.Swim)
                return s.Position.y + 0.35f * scale;
            return s.Position.y + b.StandHeight * scale * (b.Upright ? 1.8f : 1.2f);
        }
    }
}
