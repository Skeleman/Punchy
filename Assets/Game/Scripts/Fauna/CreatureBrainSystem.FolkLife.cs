using Punchy.Terrain;
using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: the small life of the turtle and sea folk villages (A-103 row 3, the details tenet). A turtle
        // child belly-slides across the snow, an old turtle naps out in the midday sun by its porch, a sea folk child
        // runs along under the fish school. Each draws from the rng only when it could happen, so nothing else's draws
        // move. All of it is simulated on the host; joined clients see it through the ghost's mode, action and lie.
        partial struct BrainJob
        {
            /// <summary>How far ahead a turtle child looks for its slide's run (metres).</summary>
            const float SlideReach = 6f;

            /// <summary>
            /// DecideStep.FolkLife: a turtle child, awake, may take a belly-slide (half the time); an old turtle with no
            /// task, round midday, may go and lie in the sun a little out from its door (half of them ever do: by
            /// individual); a sea folk child, awake, may run after a fish it sees within a couple of cells. True if one
            /// started.
            /// </summary>
            bool DecideFolkLife(ref CreatureState s, ref CreatureBlob b, ref Random rng, float sleepy)
            {
                if (b.Child && b.TurtleJobs && sleepy < 0.5f && s.Leash > 0f)
                    return rng.NextFloat() < 0.5f && StartSlide(ref s, ref rng);
                if (b.TurtleJobs && !b.Child && s.Job == (byte)FolkJob.Elder && s.Task == CreatureTask.None &&
                    Hour >= 10.5f && Hour < 15.5f && CreatureBlob.Trait(s.Individual, 53) > 0f && math.any(s.OwnDoor.xz != float2.zero))
                {
                    if (rng.NextFloat() >= 0.05f)
                        return false;
                    // Out from its door, past where it stands, lying with its feet to the igloo.
                    var outward = math.normalizesafe(s.Home - s.OwnDoor.xz, new float2(0f, 1f));
                    var spot = s.Home + outward * 1.5f;
                    if (!OpenGround(spot))
                        return false;
                    SetMode(ref s, CreatureMode.Sit);
                    s.Target = spot;
                    s.Focus = spot + outward * 3f;
                    s.TargetHeight = 3f;   // lying back
                    s.Timer = rng.NextFloat(90f, 200f);
                    return true;
                }
                if (b.Child && BedWalk && sleepy < 0.5f && rng.NextFloat() < 0.35f)
                {
                    var fish = NearestFish(in s, 30f);
                    if (fish == Entity.Null)
                        return false;
                    SetMode(ref s, CreatureMode.Play);
                    s.Stage = CreatureAnimator.ChaseFish;
                    s.Partner = fish;
                    s.Timer = rng.NextFloat(10f, 20f);
                    return true;
                }
                return false;
            }

            /// <summary>
            /// A belly-slide: the way across open snow (of six tried) that runs downhill most, clear for at least half
            /// SlideReach and inside its range; none, and it doesn't. It turns that way and goes (ChildLife carries it).
            /// </summary>
            bool StartSlide(ref CreatureState s, ref Random rng)
            {
                var from = s.Position.xz;
                var h0 = Terrain.SampleHeight(from);
                var turn = rng.NextFloat(0f, math.PI * 2f);
                var best = float.MinValue;
                var way = float2.zero;
                for (int k = 0; k < 6; k++)
                {
                    math.sincos(turn + k * math.PI / 3f, out var sin, out var cos);
                    var dir = new float2(sin, cos);
                    var reach = 0f;
                    for (float d = 1.5f; d <= SlideReach; d += 1.5f)
                    {
                        var at = from + dir * d;
                        if (!OpenGround(at) || math.distance(at, s.Home) > s.Leash)
                            break;
                        reach = d;
                    }
                    // (A-103: a village is crowded with igloos and porches; half the run will do, the longer the better.)
                    if (reach < SlideReach * 0.5f)
                        continue;
                    var drop = h0 - Terrain.SampleHeight(from + dir * reach) + reach * 0.1f;
                    if (drop > best)
                    {
                        best = drop;
                        way = dir;
                    }
                }
                if (best == float.MinValue)
                    return false;
                SetMode(ref s, CreatureMode.Idle);
                s.Yaw = math.atan2(way.x, way.y);
                s.Action = IdleAction.Slide;
                s.ActionTime = 0f;
                s.ActionLength = rng.NextFloat(2.4f, 3.2f);
                s.Timer = s.ActionLength + rng.NextFloat(0.8f, 2f);
                return true;
            }

            /// <summary>
            /// While a belly-slide lasts it moves itself (custom): a short run-up, then gliding on and slowing, on the
            /// ground, and only onto open ground inside its range (a wall or its range's edge just stops it).
            /// </summary>
            void ChildLife(ref CreatureState s, float dt, ref bool custom)
            {
                if (s.Action != IdleAction.Slide || s.Mode != CreatureMode.Idle)
                    return;
                custom = true;
                var t = math.saturate(s.ActionTime);
                var speed = 4.5f * math.saturate(t / 0.12f) * (1f - t) * math.max(s.Scale, 0.5f) / 0.55f;
                var next = s.Position.xz + new float2(math.sin(s.Yaw), math.cos(s.Yaw)) * speed * dt;
                if (OpenGround(next) && math.distance(next, s.Home) <= s.Leash)
                    s.Position.xz = next;
                s.Position.y = CreatureMotion.Ground(Terrain, s.Position.xz, s.Position.y);
                s.Target = s.Position.xz;
                s.Speed = 0f;
            }

            /// <summary>
            /// Play, ChaseFish stage: running along under its fish, looking up at it, until the time is up or the fish is
            /// gone (KeepToLeash keeps it in its range: the school swims on and it gives up at the edge).
            /// </summary>
            void ChaseFish(ref CreatureState s, ref CreatureBlob b, ref Random rng, ref float targetSpeed, ref float desiredLook, ref bool moving)
            {
                s.Timer -= Dt;
                if (s.Timer <= 0f || !Lookup.TryGetValue(s.Partner, out var p) || States[p].Gone)
                {
                    Pause(ref s, ref b, ref rng);
                    return;
                }
                var fish = States[p].Position;
                var to = fish.xz - s.Position.xz;
                s.Target = fish.xz;
                moving = true;
                // (Under it, it trots; behind, it runs.)
                targetSpeed = math.lerp(b.WalkSpeed, b.RunSpeed, math.length(to) > 2f ? 0.7f : 0.25f) * s.Scale;
                desiredLook = math.clamp(WrapAngle(math.atan2(to.x, to.y) - s.Yaw), -1.2f, 1.2f);
                M.LookKind = 0;
                M.LookAt = fish;
                s.LookFor = math.max(s.LookFor, 0.5f);
            }

            /// <summary>The nearest fish (a swimmer, not gone) within range of it, or none.</summary>
            Entity NearestFish(in CreatureState s, float range)
            {
                var found = Entity.Null;
                var best = range * range;
                var c = Cell(s.Position.xz);
                var r = (int)math.ceil(range / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        var o = States[j];
                        if (o.Gone || Bodies[j].Blob.Value.Habitat != Habitat.Swims)
                            continue;
                        var d = math.distancesq(o.Position.xz, s.Position.xz);
                        if (d < best)
                        {
                            best = d;
                            found = Entities[j];
                        }
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return found;
            }
        }
    }
}
