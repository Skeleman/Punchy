using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: a town's lamplighter on its rounds (A-115, #315). The lamps themselves follow the clock and
        // their place on the round (LampRoutes), on every machine; the lamplighter walks the same round at the same
        // hours so it's at each lamp as it lights. Split by area (#61); the system, Execute and Think live in
        // CreatureBrainSystem.cs.
        partial struct BrainJob
        {
            /// <summary>A-115: the towns' lamp rounds (LampRoutes).</summary>
            public BlobAssetReference<LampRouteBlob> Lamps;
            public bool HasLamps;

            /// <summary>Its round: the one whose first lamp its post stands by (FaunaDirector.LamplighterPosts), or -1.</summary>
            int LampRouteOf(in CreatureState s)
            {
                if (!HasLamps || s.Job != (byte)FolkJob.Lamplighter)
                    return -1;
                ref var routes = ref Lamps.Value.Routes;
                ref var stops = ref Lamps.Value.Stops;
                for (int r = 0; r < routes.Length; r++)
                    if (math.distancesq(stops[routes[r].First].Foot, s.WorkSpot) < 1f)
                        return r;
                return -1;
            }

            /// <summary>The next lamp on this round still to light (dusk) or put out (dawn), or -1 when it's done.</summary>
            int NextLamp(int route)
            {
                ref var r = ref Lamps.Value.Routes[route];
                ref var stops = ref Lamps.Value.Stops;
                var dusk = Hour >= 12f;
                for (int k = r.First; k < r.First + r.Count; k++)
                {
                    var lit = LampStop.Lit(stops[k].Hours, Hour);
                    if (dusk ? lit < 1f : lit > 0f)
                        return k;
                }
                return -1;
            }

            /// <summary>How early it sets off for its first lamp (hours).</summary>
            const float RoundLead = 0.5f;

            /// <summary>Whether it's time for its round (a little early, to get to its first lamp) and a lamp is left.</summary>
            bool OnRound(in CreatureState s)
            {
                var route = LampRouteOf(s);
                if (route < 0)
                    return false;
                ref var r = ref Lamps.Value.Routes[route];
                var inWindow = Hour >= r.DuskStart - RoundLead && Hour <= r.DuskEnd + LampStop.Fade ||
                               Hour >= r.DawnStart - RoundLead && Hour <= r.DawnEnd + LampStop.Fade;
                return inWindow && NextLamp(route) >= 0;
            }

            /// <summary>Off on its round, pole in hand.</summary>
            bool StartRound(ref CreatureState s)
            {
                SetMode(ref s, CreatureMode.Work);
                s.Stage = FolkJobs.RoundWalk;
                s.Timer = 600f;
                return true;
            }

            /// <summary>A lamplighter's day stopped for its round (not mid-talk, nor fleeing or in someone's arms).</summary>
            bool RoundCalls(in CreatureState s)
            {
                if (s.Job != (byte)FolkJob.Lamplighter)
                    return false;
                var free = s.Mode == CreatureMode.Idle && s.Stage < CreatureAnimator.MessageWait ||
                           s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Sit || s.Mode == CreatureMode.Rest ||
                           s.Mode == CreatureMode.Sleep || s.Mode == CreatureMode.Play || s.Mode == CreatureMode.Shelter ||
                           s.Mode == CreatureMode.Home && s.Stage == 0 ||
                           s.Mode == CreatureMode.Work && s.Stage < FolkJobs.RoundWalk;
                return free && OnRound(s);
            }

            /// <summary>
            /// Work, on its round: to the next lamp (running if it's late: the lamp has come on without it), then up with
            /// the pole until it lights, or goes out. A lamp it can't reach in time the clock works anyway, and it walks
            /// on to the next. False when it isn't on its round (its day post, as any tender's).
            /// </summary>
            bool LampRound(ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run, ref float targetSpeed, ref bool moving, ref float desiredLook)
            {
                if (s.Stage < FolkJobs.RoundWalk && !OnRound(s))
                    return false;
                var route = LampRouteOf(s);
                var next = route >= 0 ? NextLamp(route) : -1;
                if (next < 0)
                {
                    // (Round done: back to its day, or home to bed.)
                    Pause(ref s, ref b, ref rng);
                    return true;
                }
                ref var stop = ref Lamps.Value.Stops[next];
                var due = Hour >= 12f ? stop.Hours.x : stop.Hours.y;
                var stand = LampStop.StandAt(stop.Foot, s.Position.xz);
                if (math.distance(s.Position.xz, stand) > 0.4f)
                {
                    s.Target = stand;
                    targetSpeed = Hour > due ? run : walk;
                    moving = true;
                    if (s.Stage != FolkJobs.RoundWalk)
                    {
                        s.Stage = FolkJobs.RoundWalk;
                        s.StageTime = 0f;
                    }
                    return true;
                }
                // At the lamp: face it, and reach up once it's nearly due.
                var toLamp = stop.Foot - s.Position.xz;
                var wanted = WrapAngle(math.atan2(toLamp.x, toLamp.y) - s.Yaw);
                s.Yaw = WrapAngle(s.Yaw + math.clamp(wanted, -b.TurnRate * dt, b.TurnRate * dt));
                desiredLook = 0f;
                // (Up a couple of seconds before it lights, so the light comes on at the top of the reach.)
                var reach = Hour >= due - 0.04f;
                if (reach != (s.Stage == FolkJobs.RoundReach))
                {
                    s.Stage = reach ? FolkJobs.RoundReach : FolkJobs.RoundWalk;
                    s.StageTime = 0f;
                }
                return true;
            }
        }
    }
}
