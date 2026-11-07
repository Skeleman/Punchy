using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: following a player for a lead quest (A-71 row 6; started and stopped by Follow). Split from
        // CreatureBrainSystem.cs by area (#61); the system, Execute and Think live there.
        partial struct BrainJob
        {
            /// <summary>Greet's stage for one following a player on a quest (Follow.Start), not come to say hello.</summary>
            const byte FollowStage = 2;

            /// <summary>
            /// One following a player (CreatureMind.FollowHolder, set by Follow.Start): keeps a few metres behind them
            /// (sets off once they're more than Punchy.Fauna.Follow.Far ahead, stops a little past Punchy.Fauna.Follow.Near, so it doesn't creep up on
            /// them), and waits facing them while they stand. Gives up when they've gone (left the game), got more than
            /// Punchy.Fauna.Follow.GiveUp ahead, or its time is up. True while it's following (Think skips its own mode then).
            /// </summary>
            bool Following(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                           ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                if (M.FollowHolder == 0)
                {
                    // (Just stopped from outside: back to its own life from here, not a Greet.)
                    if (s.Mode == CreatureMode.Greet && s.Stage == FollowStage)
                        Pause(ref s, ref b, ref rng);
                    return false;
                }
                M.FollowTime -= dt;
                if (!FindHolder(M.FollowHolder, out var leader, out _) || M.FollowTime <= 0f || b.Rooted ||
                    math.distance(leader.xz, s.Position.xz) > Punchy.Fauna.Follow.GiveUp)
                {
                    M.FollowHolder = 0;
                    M.FollowTime = 0f;
                    if (s.Mode == CreatureMode.Greet && s.Stage == FollowStage)
                        Pause(ref s, ref b, ref rng);
                    return false;
                }
                if (s.Mode != CreatureMode.Greet || s.Stage != FollowStage)
                {
                    SetMode(ref s, CreatureMode.Greet);
                    s.Stage = FollowStage;
                }
                // (Kept topped up, so nothing reading Greet's timer thinks it's about to lose interest.)
                s.Timer = 5f;
                var to = leader.xz - s.Position.xz;
                var distance = math.length(to);
                var dir = distance > 1e-3f ? to / distance : new float2(0f, 1f);
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                // (On the move it keeps going until it's close; standing, it waits until they're a few metres off.)
                var gap = Punchy.Fauna.Follow.Near + 0.5f + b.BodyLength * s.Scale * 0.5f;
                var going = s.Speed > 0.2f ? distance > gap + 0.3f : distance > Punchy.Fauna.Follow.Far;
                if (M.FollowDetourTime > 0f)
                {
                    // A-88 row 1: going round whatever stopped it (Steer set the spot), at a run, before heading for
                    // them again. (Straight at them, a dog behind a trunk or a rock only crawled, halved each frame.)
                    M.FollowDetourTime -= dt;
                    s.Target = M.FollowDetour;
                    targetSpeed = run;
                    moving = true;
                    desiredLook = lookAt * 0.4f;
                    if (math.distance(s.Position.xz, M.FollowDetour) < 0.6f)
                        M.FollowDetourTime = 0f;
                }
                else if (going)
                {
                    // (Aimed a little short of the gap: walking stops a step before its target.)
                    s.Target = leader.xz - dir * (gap - 1f);
                    // Runs to catch up, walks briskly when only a little behind.
                    targetSpeed = distance > Punchy.Fauna.Follow.Far + 4f ? run : distance > Punchy.Fauna.Follow.Far + 1f ? math.max(walk * 1.5f, run * 0.5f) : walk;
                    moving = true;
                    desiredLook = lookAt * 0.4f;
                }
                else
                {
                    TurnTowards(ref s, ref b, leader.xz, dt);
                    desiredLook = lookAt * 0.6f;
                }
                return true;
            }
        }
    }
}
