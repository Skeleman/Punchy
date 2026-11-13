using Unity.Entities;
using Unity.Mathematics;

namespace Punchy.Fauna
{
    public partial struct CreatureBrainSystem
    {
        // BrainJob, part: fish and the player's bobber (A-100 row 2; the bobbers are FishLure's, set by the player's
        // fishing). The system, Execute and Think live in CreatureBrainSystem.cs.
        partial struct BrainJob
        {
            /// <summary>
            /// A swimming fish near a bobber (FishLure, within Reach, in the same water): the nearest few turn and drift
            /// over to it and hang a little way off, swaying; the very nearest darts in to nibble once or twice before the
            /// bite (its nose to the bobber, up near the surface, so the wake rings show), then holds it tugging while
            /// the others keep back. A splash or a miss scatters every one of them. True while it's taken up with the
            /// bobber (Think skips its own mode then). Simulated state, so a joined client sees it in the snapshots.
            /// </summary>
            bool Lured(Entity self, ref CreatureState s, ref CreatureBlob b, ref Random rng, float dt, float walk, float run,
                       bool threatened, ref float targetSpeed, ref bool moving, ref bool custom)
            {
                if (Lures.Length == 0 || b.Habitat != Habitat.Swims || b.LavaDweller || b.Breaches || threatened ||
                    s.Air > 0f || s.Mode == CreatureMode.Flee || s.Mode == CreatureMode.Held ||
                    !(s.Mode == CreatureMode.Idle || s.Mode == CreatureMode.Wander && s.Stage == 0))
                    return false;
                // The nearest bobber in reach, on this fish's own water (not a pool over the hill's).
                var g = Terrain.SampleGround(s.Position.xz);
                if (!g.HasWater || g.WaterSurface <= g.Height)
                    return false;
                var best = -1;
                var bestD = FishLure.Reach * FishLure.Reach;
                for (int i = 0; i < Lures.Length; i++)
                {
                    var d = math.distancesq(Lures[i].At.xz, s.Position.xz);
                    if (d < bestD && math.abs(Lures[i].At.y - g.WaterSurface) < 0.5f)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (best < 0)
                    return false;
                var lure = Lures[best];
                if (lure.Stage == FishLure.Stage.Scatter)
                {
                    StartFleeing(ref s, ref b, ref rng, lure.At.xz, false);
                    return false;
                }
                var dist = math.sqrt(bestD);
                var rank = NearerFish(self, lure.At, dist);
                if (rank >= FishLure.Interested)
                    return false;

                var toLure = lure.At.xz - s.Position.xz;
                var dir = math.normalizesafe(toLure, new float2(math.sin(s.Yaw), math.cos(s.Yaw)));
                var face = math.atan2(dir.x, dir.y);
                var half = b.BodyLength * s.Scale * 0.5f;
                // (Hanging off: the nearest just out of reach of the bobber, the others further back; a bite sends them
                // further off still, out of the hooked one's way.)
                var hover = rank == 0 ? half + 0.6f : (lure.Stage == FishLure.Stage.Bite ? 2.5f : 1.6f) + rank * 0.6f + half;
                if (s.Mode != CreatureMode.Wander)
                    SetMode(ref s, CreatureMode.Wander);
                s.Stage = 0;

                // The nearest one, nibbling or hooked: moved here (custom), quick and close.
                var nibble = rank == 0 ? FishLure.NibblePhase(lure) : 0f;
                var hooked = rank == 0 && lure.Stage == FishLure.Stage.Bite;
                if ((nibble > 0f || hooked) && dist < hover + 0.6f)
                {
                    custom = true;
                    var rise = hooked ? 1f : math.sin(math.PI * nibble);
                    // (Nose to the bobber at the peak of a dart: its middle half a body back.)
                    var want = lure.At.xz - dir * math.lerp(hover, half * 0.9f, rise);
                    var was = s.Position;
                    s.Position.xz = math.lerp(s.Position.xz, want, 1f - math.exp(-dt * 25f));
                    var wantY = math.lerp(SwimHeight(s, ref b, g, g.WaterSurface), g.WaterSurface - 0.1f, rise);
                    s.Position.y = math.lerp(s.Position.y, wantY, 1f - math.exp(-dt * 20f));
                    // (Hooked, it thrashes: the head wrenching side to side.)
                    var yaw = hooked ? face + math.sin(s.Clock * 14f) * 0.35f : face;
                    s.Yaw += WrapAngle(yaw - s.Yaw) * math.min(1f, dt * 12f);
                    // (Its speed drives the tail and the wake rings: a dart is fast.)
                    s.Speed = math.min(math.distance(was, s.Position) / math.max(dt, 1e-4f), run);
                    if (hooked)
                        s.Speed = math.max(s.Speed, run * 0.6f);
                    if (!math.all(math.isfinite(s.Position)))
                        s.Position = was;
                    return true;
                }

                // Coming over, then hanging off it, drifting slowly round (each fish its own way) and facing it.
                var sway = math.sin(s.Clock * 0.4f + (s.Individual % 97) * 0.37f) * 0.35f;
                var bearing = math.atan2(-dir.x, -dir.y) + sway;
                s.Target = lure.At.xz + new float2(math.sin(bearing), math.cos(bearing)) * hover;
                var off = math.distance(s.Position.xz, s.Target);
                moving = off > 0.15f;
                targetSpeed = walk * (off > 2f ? 1f : 0.6f);
                if (!moving)
                    s.Yaw += WrapAngle(face - s.Yaw) * math.min(1f, dt * 3f);
                return true;
            }

            /// <summary>How many other swimming fish are nearer the bobber at <paramref name="at"/> than
            /// <paramref name="dist"/> (counting stops at FishLure.Interested: only that many come over).</summary>
            int NearerFish(Entity self, float3 at, float dist)
            {
                var n = 0;
                var c = Cell(at.xz);
                var r = (int)math.ceil(FishLure.Reach / CellSize);
                for (int z = -r; z <= r; z++)
                for (int x = -r; x <= r; x++)
                {
                    if (!Grid.TryGetFirstValue(c + new int2(x, z), out var j, out var it))
                        continue;
                    do
                    {
                        if (Entities[j] == self || States[j].Gone || Bodies[j].Blob.Value.Habitat != Habitat.Swims ||
                            States[j].Mode == CreatureMode.Flee)
                            continue;
                        var d = math.distance(States[j].Position.xz, at.xz);
                        // (A tie goes by entity index, so two fish the same way off don't both think they're first.)
                        if (d < dist || d == dist && Entities[j].Index < self.Index)
                            if (++n >= FishLure.Interested)
                                return n;
                    } while (Grid.TryGetNextValue(out j, ref it));
                }
                return n;
            }
        }
    }
}
