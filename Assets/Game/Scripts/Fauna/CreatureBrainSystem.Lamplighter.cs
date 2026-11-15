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
                    // (Late, or out in the rain: at a run. The sky itself, not its discomfort, so a hardy one hurries too.)
                    var sky = Comfort.WeatherAt(s.Position.xz);
                    var wet = sky.Rain + sky.Storm > 0.3f;
                    targetSpeed = Hour > due || wet ? run : walk;
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

            /// <summary>Out on its round: it doesn't break off for shelter, it hurries through the rain (row 2).</summary>
            static bool LampRoundOn(in CreatureState s) =>
                s.Job == (byte)FolkJob.Lamplighter && s.Mode == CreatureMode.Work && s.Stage >= FolkJobs.RoundWalk;

            /// <summary>How far off a child spots the lamplighter on its round and tags along (m).</summary>
            const float TrailReach = 20f;

            /// <summary>
            /// Row 2: some evenings a child at a loose end trails the lamplighter round the town, a few steps behind,
            /// watching each lamp come on. Which evenings is rolled from the child and the day, so it's not every
            /// child every night. Host only, as the rest of the brain; clients see it walk. False when it isn't.
            /// </summary>
            bool TrailLamplighter(ref CreatureState s, ref CreatureBlob b, float dt, float walk, float run, ref float targetSpeed,
                                  ref bool moving, ref float desiredLook)
            {
                if (!HasLamps || !b.Child || Hour < 17f || Hour > 22.5f)
                    return false;
                // (Sat about the square counts: in the evening that's where the children are.)
                var loose = s.Mode == CreatureMode.Wander || s.Mode == CreatureMode.Play || s.Mode == CreatureMode.Sit ||
                            s.Mode == CreatureMode.Idle && s.Stage < CreatureAnimator.MessageWait;
                if (!loose)
                    return false;
                // (Roughly a day at the default day length (24 min): about two evenings in five.)
                var day = (uint)(Now / 1440f);
                if (math.hash(new uint2(s.Seed, day * 2654435761u + 77u)) % 5u >= 2u)
                    return false;
                if (!FindLamplighter(s.Position.xz, out var leader))
                    return false;
                // (Up from its seat first, so it doesn't sit back down in the street when the round moves off.)
                if (s.Mode == CreatureMode.Sit)
                    SetMode(ref s, CreatureMode.Wander);
                var to = leader.xz - s.Position.xz;
                var distance = math.length(to);
                var dir = distance > 1e-3f ? to / distance : new float2(0f, 1f);
                var lookAt = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                var gap = 2.5f + b.BodyLength * s.Scale * 0.5f;
                if (distance > (s.Speed > 0.2f ? gap + 0.3f : gap + 2f))
                {
                    // (A step to the side by the child's seed, so two of them don't walk in each other's tracks.)
                    var side = new float2(dir.y, -dir.x) * ((s.Seed & 1u) == 0u ? 0.8f : -0.8f);
                    s.Target = leader.xz - dir * (gap - 1f) + side;
                    targetSpeed = distance > gap + 6f ? run : walk;
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

            /// <summary>The nearest lamplighter out on its round within TrailReach of p.</summary>
            bool FindLamplighter(float2 p, out float3 position)
            {
                position = default;
                var best = TrailReach * TrailReach;
                var found = false;
                var c = Cell(p);
                var r = (int)math.ceil(TrailReach / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if (o.Gone || !LampRoundOn(in o))
                            continue;
                        var d = math.distancesq(o.Position.xz, p);
                        if (d < best)
                        {
                            best = d;
                            position = o.Position;
                            found = true;
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found;
            }
        }
    }
}
