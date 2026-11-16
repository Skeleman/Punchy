using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        /// <summary>Within this (m) of a player a member on legs wakes to its full brain: talk range, as far as a chat
        /// starts (ChatReach). FaunaDirector.MembersLegs puts members on legs a little further out. // sim:</summary>
        public const float LegsRange = 30f;

        // BrainJob, part: A-110 row 5 low-rate brains. Split off CreatureBrainSystem.cs (over 1800 lines, rule 28).
        partial struct BrainJob
        {

            /// <summary>
            /// A-110 row 5: a town household member far from every player (CreatureMind.Legs, set by
            /// FaunaDirector.MembersLegs) has no mind of its own: it only walks after where its day has it (Whereabouts,
            /// handed over as a spot and its speed), on the brain LOD's low rate, coasting between. It wakes (false: the
            /// full brain runs this frame, and the director won't put it back on legs while anything is going on) when a
            /// player comes within talk range, its mode is changed by anyone else (a flee told from outside, going in, a
            /// ride), or on its staggered search something would startle it or someone wants it (a chat, a game, a
            /// hunter's quarry): the same senses Sense uses. Host (or single player) only; what clients see is its ghost.
            /// </summary>
            bool Legs(Entity self, ref CreatureState s, in CreatureBody body)
            {
                ref var b = ref body.Blob.Value;
                // (Only the modes the director set; anything else was set by someone else and needs the brain.)
                var wake = s.Gone || s.Holder != 0 || M.FollowHolder != 0 || s.ChatRole != 0 || s.Task == CreatureTask.Patrol ||
                           s.Mode != CreatureMode.Wander && s.Mode != CreatureMode.Work && s.Mode != CreatureMode.Idle;
                var reach = math.max(LegsRange, b.FleeDistance * s.Scale * 1.8f);
                for (int p = 0; p < Players.Length && !wake; p++)
                    wake = math.distancesq(Players[p].xz, s.Position.xz) < reach * reach;
                if (wake)
                    return Wake(ref s);
                // (The brain LOD's turn, phased the same way.)
                var phase = math.hash(new uint2(s.Individual, 0x59u));
                phase ^= phase >> 16;
                phase *= 0x7FEB352Du;
                phase ^= phase >> 15;
                if ((Frame + phase) % BrainLodFrames != 0)
                {
                    M.LodDebt += Step;
                    if (s.Speed > 0f)
                    {
                        s.Position.xz += new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * s.Speed * Dt;
                        s.Position.y = CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                    }
                    System.Threading.Interlocked.Increment(ref Searches.Data.LodSkips);
                    return true;
                }
                // (The time since its last turn; the coast stays where it got to, as the walk after the spot carries on from it.)
                var frameStep = Step;
                Step = frameStep + M.LodDebt;
                var dt = math.min(Step, 0.3f);
                M.LodDebt = 0f;
                M.LodCoast = float2.zero;
                var startled = false;
                if (SearchDue(in s))
                {
                    var alert = 1f;
                    startled = b.FleeDistance > 0f && FindHunter(self, ref s, alert, out _) ||
                               b.Hunted && FindPredator(self, ref s, body.Species, alert, out _) ||
                               b.DrivenOff && FindChaser(self, in s, out _) ||
                               !b.Rooted && NearColossus(s.Position.xz, b.FleeDistance > 0f ? 1.6f : 1f, out _) ||
                               b.FleeDistance > 0f && FindAlarm(self, ref s, body.Species, out _) ||
                               Wanted(self, in s);
                }
                if (startled)
                {
                    Step = frameStep;
                    return Wake(ref s);
                }
                System.Threading.Interlocked.Increment(ref Searches.Data.Legs);
                s.Clock += dt;
                s.ModeTime += dt;
                s.StageTime += dt;
                // (Its idle or work timer never runs out on legs: the director, not the brain, picks what comes next.)
                s.Timer = math.max(s.Timer, 3f);

                // Walk after the spot: at the day's own pace, a little quicker to close a gap, never past it.
                var want = M.LegsAt + M.LegsVel * math.clamp(s.Clock - M.LegsSet, 0f, 1.5f);
                var to = want - s.Position.xz;
                var gap = math.length(to);
                var pace = math.length(M.LegsVel);
                var speed = math.min(pace + math.max(0f, gap - 0.3f) * 0.5f, b.RunSpeed * 0.6f);
                var step = math.min(speed * dt, gap);
                var heading = gap > 0.05f ? to / gap : float2.zero;
                var wanted = gap > 0.3f ? heading : math.lengthsq(M.LegsFace) > 0f ? math.normalizesafe(M.LegsFace - s.Position.xz) : float2.zero;
                if (math.lengthsq(wanted) > 0f)
                {
                    var turn = WrapAngle(math.atan2(wanted.x, wanted.y) - s.Yaw);
                    s.Yaw = WrapAngle(s.Yaw + math.clamp(turn, -b.TurnRate * dt, b.TurnRate * dt));
                }
                s.Position.xz += heading * step;
                s.Position.y = CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                s.Speed = step > 0.001f ? step / dt : 0f;
                // (Coasting runs along the yaw: only while it faces the way it's going.)
                if (math.dot(heading, new float2(math.sin(s.Yaw), math.cos(s.Yaw))) < 0.9f)
                    s.Speed = 0f;
                // At its post: the work pose (Working's stage, as if it had walked there itself; the director sets
                // WorkSpot to its day's spot at the post, so a wake leaves it where it stands).
                if (s.Mode == CreatureMode.Work && s.Stage == 0 && s.Task != CreatureTask.None && pace < 0.05f &&
                    math.distance(s.Position.xz, s.WorkSpot) < 0.4f)
                {
                    s.Stage = FolkJobs.WorkStage((FolkJob)s.Job);
                    s.StageTime = 0f;
                }
                // Stood up, head up, looking ahead.
                var ease = math.min(1f, dt * 2f);
                s.Sit -= s.Sit * ease;
                s.Lie -= s.Lie * ease;
                s.Bow -= s.Bow * ease;
                s.Curl -= s.Curl * ease;
                s.HeadDown -= s.HeadDown * ease;
                s.Vertical -= s.Vertical * ease;
                s.Look -= s.Look * ease;
                s.LookPitch -= s.LookPitch * ease;
                CarriedItem(ref s, ref b);
                Breathe(ref s, ref b, s.Position.y, Step, dt);
                Step = frameStep;
                CreatureMotion.EaseGait(ref s, ref b, Dt, false);
                return true;
            }

            /// <summary>Back to the full brain from this frame on (the director puts it on legs again when it's calm).</summary>
            bool Wake(ref CreatureState s)
            {
                M.Legs = 0;
                M.LodDebt = 0f;
                M.LodCoast = float2.zero;
                return false;
            }

            /// <summary>Someone near wants this one: any creature within a chat's reach with it as its partner (asked to
            /// talk, to play, hunted, a messenger bird).</summary>
            bool Wanted(Entity self, in CreatureState s)
            {
                var c = Cell(s.Position.xz);
                for (int z = -2; z <= 2; z++)
                for (int x = -2; x <= 2; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if (o.Partner == self && math.distancesq(o.Position.xz, s.Position.xz) < 30f * 30f)
                            return true;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return false;
            }
        }
    }
}
