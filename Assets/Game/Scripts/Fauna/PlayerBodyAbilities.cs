using Punchy.Player;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// What a creature body can do under a player's control (PlayerBody, read by PlayerMovementSystem): its pace and
    /// height next to a human's, its jump, flight, climbing and breath. One builder for every body a player drives:
    /// its own (FaunaDirector, at the species' middle size) and an animal it rides and steers (PlayerRideSystem, at the
    /// animal's own size, A-126 row 4); boats are to come the same way.
    /// </summary>
    public static class PlayerBodyAbilities
    {
        /// <summary>A human's run (m/s) and standing height (m) at its middle size, the yardstick for the rest (set by
        /// FaunaDirector when it builds the player's body; 0 until then, which reads as "like a human").</summary>
        public static float HumanRun, HumanStand;

        /// <summary>
        /// The abilities of body b at size scale. flySpeed and jump are the species' own player overrides (0: from the
        /// body); climbSkill is the skill to use (the species', or a test's override).
        /// </summary>
        public static PlayerBody From(ref CreatureBlob b, float scale, float flySpeed, float jump, byte climbSkill)
        {
            var abilities = new PlayerBody
            {
                SpeedFactor = HumanRun > 0f ? math.clamp(b.RunSpeed * scale / HumanRun, 0.35f, 2.5f) : 1f,
                Flies = b.Habitat == Habitat.Flies,
                FlySpeed = flySpeed > 0f ? flySpeed : b.FlySpeed * 2f,
                ClimbSkill = climbSkill,
                // (A lizard scurries up; a tree climber as nimbly as it's keen: a monkey quick, a cat less, a bear hauling.)
                ClimbSpeed = Climbing.BaseSpeed(climbSkill, b.Climbing),
                // (A lava swimmer walks on land as a player; only fish swim and flop.)
                Swims = b.Habitat == Habitat.Swims && !b.LavaDweller,
                Hovers = b.Haunts,
                // (A-102: its breath under water, as the same species' creatures have it.)
                BreathesWater = b.BreathesWater,
                HoldBreath = b.HoldBreath,
                Size = HumanStand > 0f ? math.clamp(b.StandHeight * scale / HumanStand, 0.25f, 3f) : 1f
            };
            // (Its jump: the species' own, else as high as it's big next to a human, a climber springier.)
            if (jump <= 0f)
                jump = math.clamp(math.sqrt(math.max(abilities.Size, 0.01f)), 0.45f, 1.4f) * (b.Climbing > 0.5f || b.ClimbSkill >= Climbing.Tree ? 1.25f : 1f);
            abilities.JumpFactor = jump;
            return abilities;
        }

        /// <summary>
        /// A-126 row 4: an animal a player rides and steers. Its own pace, size and jump, on the ground only (no flight,
        /// hovering, swimming or climbing: a ride ends at those, Riding.Reason), and the rider's breath.
        /// </summary>
        public static PlayerBody Steered(ref CreatureBlob mount, float scale, in PlayerBody rider)
        {
            var abilities = From(ref mount, scale, 0f, 0f, 0);
            abilities.Flies = abilities.Hovers = abilities.Swims = false;
            abilities.BreathesWater = rider.BreathesWater;
            abilities.HoldBreath = rider.HoldBreath;
            return abilities;
        }
    }
}
